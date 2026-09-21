namespace Mux.Publisher.Channels.Drivers
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using Mux.Publisher.Manifest;

    /// <summary>
    /// Builds a signed, notarized macOS <c>.dmg</c>. It first assembles a proper <c>.app</c> bundle
    /// (<c>Info.plist</c>, icon, the published binary under <c>Contents/MacOS/</c>) — a bare binary gets
    /// no Dock icon and no Gatekeeper trust — then hardened-runtime <c>codesign</c>s it, wraps it in a
    /// <c>.dmg</c>, and <c>notarytool submit --wait</c> + <c>stapler staple</c>. Notarization is not
    /// optional: macOS refuses unnotarized downloaded apps. Startup registration is handled by the app
    /// on first run, so no <c>.pkg</c>/postinstall script is needed.
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
            string bundleId = config.OptionOrDefault("bundleId", "com.jchristn." + project);

            SigningEntry? mac = context.Manifest.Signing.MacOs;
            bool canSign = mac != null && !string.IsNullOrWhiteSpace(mac.VaultRef);
            if (!canSign)
            {
                plan.AddNote("macOS: the .dmg is UNSIGNED (no Apple Developer certificate). On first launch users must right-click the app and choose Open (or run 'xattr -dr com.apple.quarantine /Applications/" + display + ".app'); on Apple Silicon a downloaded unsigned app may report \"is damaged\" until the quarantine attribute is removed. Ship these instructions with the release.");
            }

            foreach (string rid in ChannelHelpers.ResolveRuntimes(context.Manifest, config, RequiredOs))
            {
                PublishedArtifact? published = context.PublishedFor(rid);
                if (published == null)
                {
                    plan.AddNote("dmg: no publish for " + rid + "; skipped.");
                    continue;
                }

                string appName = display + ".app";
                string appDir = "dmg/" + rid + "/" + appName;
                string binaryName = System.IO.Path.GetFileName(published.PrimaryBinary);

                // 1. Render Info.plist into the bundle.
                plan.AddFile(appDir + "/Contents/Info.plist", RenderInfoPlist(display, bundleId, context.Version, binaryName));
                plan.AddFile(appDir + "/Contents/PkgInfo", "APPL????\n");

                string appAbs = System.IO.Path.Combine(context.StagingRoot, appDir);
                string macOsDir = appAbs + "/Contents/MacOS";
                string resourcesDir = appAbs + "/Contents/Resources";

                // 2. Assemble the bundle from the publish output + icon.
                plan.AddCommand(new ShellCommand("mkdir", new List<string> { "-p", macOsDir, resourcesDir }) { Description = "Create .app bundle layout (" + rid + ")" });
                plan.AddCommand(new ShellCommand("cp", new List<string> { "-R", published.PublishDir + "/.", macOsDir }) { Description = "Copy published binaries into the bundle" });
                plan.AddCommand(new ShellCommand("cp", new List<string> { System.IO.Path.Combine(context.RepoRoot, "assets", "icon-green.icns"), resourcesDir + "/" + project + ".icns" }) { Description = "Copy the app icon", ContinueOnError = true });
                plan.AddCommand(new ShellCommand("chmod", new List<string> { "+x", macOsDir + "/" + binaryName }) { Description = "Mark the launcher executable" });

                // 3. Hardened-runtime codesign.
                if (canSign)
                {
                    plan.AddCommand(new ShellCommand("codesign", new List<string>
                    {
                        "--force", "--deep", "--options", "runtime", "--timestamp",
                        "--sign", "$" + mac!.VaultRef + "_IDENTITY",
                        appAbs
                    })
                    { Description = "Hardened-runtime codesign the .app" });
                }

                // 4. Build the .dmg.
                string dmgName = Naming.Dmg(project, context.Version, rid);
                string dmgOut = System.IO.Path.Combine(context.OutputRoot, "macos", dmgName);
                plan.AddCommand(new ShellCommand("hdiutil", new List<string>
                {
                    "create", "-volname", display, "-srcfolder", appAbs, "-ov", "-format", "UDZO", dmgOut
                })
                { Description = "Create the .dmg for " + rid });

                // 4b. Attach a Software License Agreement so the volume shows an Agree/Disagree prompt and will
                //     not mount until the user accepts. Done before notarize/staple so the ticket covers the
                //     final artifact. The resource plist carries a default-English LPic/STR# plus the license
                //     as a TEXT resource.
                string slaName = "dmg/" + rid + "/sla.plist";
                plan.AddFile(slaName, Mux.Publisher.Publishing.LicenseAssets.DmgSlaResourcesPlist(
                    Mux.Publisher.Publishing.LicenseAssets.ReadLicenseText(context.RepoRoot)));
                plan.AddCommand(new ShellCommand("hdiutil", new List<string>
                {
                    "udifrez", "-xml", System.IO.Path.Combine(context.StagingRoot, slaName), "-quiet", dmgOut
                })
                { Description = "Attach the license agreement (SLA) to the .dmg for " + rid });

                // 5. Notarize + staple.
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
        /// Renders an <c>Info.plist</c> for the app bundle.
        /// </summary>
        /// <param name="displayName">The display (and bundle) name.</param>
        /// <param name="bundleId">The CFBundleIdentifier.</param>
        /// <param name="version">The release version.</param>
        /// <param name="executable">The CFBundleExecutable (launcher file name).</param>
        /// <returns>The rendered plist.</returns>
        public static string RenderInfoPlist(string displayName, string bundleId, string version, string executable)
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
            builder.AppendLine("<!DOCTYPE plist PUBLIC \"-//Apple//DTD PLIST 1.0//EN\" \"http://www.apple.com/DTDs/PropertyList-1.0.dtd\">");
            builder.AppendLine("<plist version=\"1.0\">");
            builder.AppendLine("<dict>");
            AppendKey(builder, "CFBundleName", displayName);
            AppendKey(builder, "CFBundleDisplayName", displayName);
            AppendKey(builder, "CFBundleIdentifier", bundleId);
            AppendKey(builder, "CFBundleVersion", version);
            AppendKey(builder, "CFBundleShortVersionString", version);
            AppendKey(builder, "CFBundleExecutable", executable);
            AppendKey(builder, "CFBundleIconFile", "mux");
            AppendKey(builder, "CFBundlePackageType", "APPL");
            builder.AppendLine("    <key>LSMinimumSystemVersion</key>");
            builder.AppendLine("    <string>11.0</string>");
            builder.AppendLine("    <key>NSHighResolutionCapable</key>");
            builder.AppendLine("    <true/>");
            builder.AppendLine("</dict>");
            builder.AppendLine("</plist>");
            return builder.ToString();
        }

        private static void AppendKey(StringBuilder builder, string key, string value)
        {
            builder.AppendLine("    <key>" + key + "</key>");
            builder.AppendLine("    <string>" + Xml(value) + "</string>");
        }

        private static string Xml(string value)
        {
            return (value ?? string.Empty).Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
        }
    }
}
