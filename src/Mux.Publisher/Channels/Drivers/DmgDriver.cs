namespace Mux.Publisher.Channels.Drivers
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using Mux.Publisher.Manifest;

    /// <summary>
    /// Builds a macOS <c>.dmg</c> for drag-to-Applications installs. It assembles and signs the <c>.app</c>
    /// (ad hoc when no Developer ID is configured; see <see cref="MacAppBundle"/>), then lays out the disk image
    /// window: the app, an <c>Applications</c> shortcut to drag it onto, and an <c>If &lt;App&gt; won't open.txt</c>
    /// note with the Gatekeeper steps. With a Developer ID configured it also notarizes and staples. The
    /// <c>.pkg</c> channel is the recommended macOS installer, since only it can put the command on PATH and
    /// show the license for acceptance.
    /// </summary>
    public sealed class DmgDriver : DriverBase
    {
        /// <inheritdoc />
        public override string Name => "dmg";

        /// <inheritdoc />
        public override TargetOs RequiredOs => TargetOs.MacOs;

        /// <inheritdoc />
        public override ChannelPlan Plan(PublisherContext context, ChannelConfig config)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (config == null) throw new ArgumentNullException(nameof(config));

            ChannelPlan plan = new ChannelPlan(Name);
            string project = Project(context);
            string display = Display(context);
            string bundleId = config.OptionOrDefault("bundleId", Identifier(context));

            SigningEntry? mac = context.Manifest.Signing.MacOs;
            bool canSign = MacAppBundle.CanSign(context);
            if (!canSign)
            {
                plan.AddNote("macOS: the .dmg is ad-hoc signed, not notarized (no Apple Developer certificate, by policy). Users open it with System Settings > Privacy & Security > Open Anyway, or 'xattr -dr com.apple.quarantine /Applications/" + display + ".app'. The note inside the image repeats the steps.");
            }

            foreach (string rid in ChannelHelpers.ResolveRuntimes(context.Manifest, config, RequiredOs))
            {
                PublishedArtifact? published = context.PublishedFor(rid);
                if (published == null)
                {
                    plan.AddNote("dmg: no publish for " + rid + "; skipped.");
                    continue;
                }

                // The image's window shows everything in this folder.
                string contentDir = "dmg/" + rid + "/content";
                string contentAbs = System.IO.Path.Combine(context.StagingRoot, contentDir);
                string appAbs = MacAppBundle.Assemble(plan, context, published, contentDir, display, bundleId);

                plan.AddFile(contentDir + "/If " + display + " won't open.txt", Mux.Publisher.Publishing.UnsignedNotice.MacOsNote(display, CliFileName(context, published)));
                plan.AddCommand(new ShellCommand("ln", new List<string> { "-sfn", "/Applications", contentAbs + "/Applications" }) { Description = "Add the Applications shortcut to drag the app onto" });

                // Build the .dmg.
                string dmgName = Naming.Dmg(project, context.Version, rid);
                string dmgOut = System.IO.Path.Combine(context.OutputRoot, "macos", dmgName);
                // hdiutil sizes a -srcfolder image from its own estimate, which can come up short for a large
                // self-contained bundle ("create failed - No space left on device"). Pass an explicit size
                // with headroom when the publish output is on disk; UDZO compresses the result, so the extra
                // room costs nothing in the shipped .dmg.
                List<string> createArgs = new List<string> { "create", "-volname", display, "-srcfolder", contentAbs };
                long publishBytes = DirectorySizeBytes(published.PublishDir);
                if (publishBytes > 0)
                {
                    createArgs.Add("-size");
                    createArgs.Add(DmgImageSizeMegabytes(publishBytes) + "m");
                }

                createArgs.AddRange(new[] { "-ov", "-format", "UDZO", dmgOut });
                plan.AddCommand(new ShellCommand("hdiutil", createArgs) { Description = "Create the .dmg for " + rid });

                // Attach a Software License Agreement so older macOS shows an Agree/Disagree prompt before
                // mounting. Apple deprecated `hdiutil udifrez` and it fails on current macOS (including the
                // release runners), so the step is best-effort: the license also ships inside the app bundle,
                // and the .pkg is where users accept it.
                string slaName = "dmg/" + rid + "/sla.plist";
                string licenseText = Mux.Publisher.Publishing.LicenseAssets.ReadLicenseText(context.RepoRoot);
                plan.AddFile(slaName, Mux.Publisher.Publishing.LicenseAssets.DmgSlaResourcesPlist(licenseText));
                plan.AddCommand(new ShellCommand("hdiutil", new List<string>
                {
                    "udifrez", "-xml", System.IO.Path.Combine(context.StagingRoot, slaName), "-quiet", dmgOut
                })
                {
                    Description = "Attach the license agreement (SLA) to the .dmg for " + rid,
                    ContinueOnError = true
                });

                // Notarize + staple (only with a Developer ID).
                if (canSign && mac!.Notarize)
                {
                    plan.AddCommand(new ShellCommand("xcrun", new List<string>
                    {
                        "notarytool", "submit", dmgOut,
                        "--apple-id", "$" + mac.VaultRef + "_APPLE_ID",
                        "--team-id", "$" + mac.VaultRef + "_TEAM_ID",
                        "--password", "$" + mac.VaultRef + "_APP_PASSWORD",
                        "--wait"
                    })
                    { Description = "Notarize the .dmg (notarytool --wait)" });

                    plan.AddCommand(new ShellCommand("xcrun", new List<string> { "stapler", "staple", dmgOut })
                    { Description = "Staple the notarization ticket" });
                }
                else if (canSign)
                {
                    plan.AddNote("macOS: signing configured but notarize=false; downloaded builds may still be blocked by Gatekeeper.");
                }
            }

            return plan;
        }

        /// <summary>
        /// Renders an <c>Info.plist</c> for the app bundle (kept for callers of the previous API).
        /// </summary>
        /// <param name="displayName">The display (and bundle) name.</param>
        /// <param name="bundleId">The CFBundleIdentifier.</param>
        /// <param name="version">The release version.</param>
        /// <param name="executable">The CFBundleExecutable (launcher file name).</param>
        /// <returns>The rendered plist.</returns>
        public static string RenderInfoPlist(string displayName, string bundleId, string version, string executable)
        {
            return MacAppBundle.RenderInfoPlist(displayName, bundleId, version, executable);
        }

        /// <summary>
        /// The image size, in megabytes, to give <c>hdiutil create</c> for a bundle whose content is
        /// <paramref name="contentBytes"/>: the content plus 25% for filesystem overhead plus a fixed 64 MB
        /// for the icon, plist, license, and codesign data added to the bundle, rounded up.
        /// </summary>
        /// <param name="contentBytes">The size of the published output in bytes.</param>
        /// <returns>The size in megabytes (at least 64).</returns>
        public static long DmgImageSizeMegabytes(long contentBytes)
        {
            const long Mb = 1024L * 1024L;
            long withOverhead = Math.Max(0, contentBytes) + (Math.Max(0, contentBytes) / 4);
            return ((withOverhead + Mb - 1) / Mb) + 64;
        }

        private static long DirectorySizeBytes(string? dir)
        {
            if (string.IsNullOrEmpty(dir) || !System.IO.Directory.Exists(dir)) return 0;
            long total = 0;
            foreach (string file in System.IO.Directory.EnumerateFiles(dir, "*", System.IO.SearchOption.AllDirectories))
            {
                try { total += new System.IO.FileInfo(file).Length; }
                catch (System.IO.IOException) { }
                catch (UnauthorizedAccessException) { }
            }

            return total;
        }
    }
}
