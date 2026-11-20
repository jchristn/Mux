namespace Mux.Publisher.Channels.Drivers
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using Mux.Publisher.Manifest;

    /// <summary>
    /// Assembles a macOS <c>.app</c> bundle for the <c>dmg</c> and <c>pkg</c> channels: <c>Info.plist</c>, the
    /// <c>.icns</c> icon, the published binaries (including the bundled tray agent and CLI) under
    /// <c>Contents/MacOS/</c>, and the license under <c>Contents/Resources/</c>. It signs the bundle last, after
    /// every file is in place: with the configured Developer ID when signing is set up, and otherwise ad hoc
    /// (<c>codesign --sign -</c>), which Apple silicon requires and which keeps Gatekeeper from reporting a
    /// downloaded copy as "damaged".
    /// </summary>
    public static class MacAppBundle
    {
        /// <summary>
        /// Adds the files and commands that build and sign the bundle at <c>&lt;parentDir&gt;/&lt;Display&gt;.app</c>.
        /// </summary>
        /// <param name="plan">The plan to append to.</param>
        /// <param name="context">The publisher context.</param>
        /// <param name="published">The published desktop payload for one runtime.</param>
        /// <param name="parentDir">The staging-relative directory that holds the bundle.</param>
        /// <param name="display">The display (and bundle) name.</param>
        /// <param name="bundleId">The <c>CFBundleIdentifier</c>.</param>
        /// <returns>The absolute path of the assembled <c>.app</c>.</returns>
        public static string Assemble(ChannelPlan plan, PublisherContext context, PublishedArtifact published, string parentDir, string display, string bundleId)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (published == null) throw new ArgumentNullException(nameof(published));

            string project = context.Manifest.Project.Name;
            string appName = display + ".app";
            string appDir = parentDir + "/" + appName;
            string binaryName = System.IO.Path.GetFileName(published.PrimaryBinary);
            string rid = published.Rid;

            plan.AddFile(appDir + "/Contents/Info.plist", RenderInfoPlist(display, bundleId, context.Version, binaryName, project, context.Manifest.Project.EffectivePublisher));
            plan.AddFile(appDir + "/Contents/PkgInfo", "APPL????\n");
            plan.AddFile(appDir + "/Contents/Resources/LICENSE.txt", Mux.Publisher.Publishing.LicenseAssets.ReadLicenseText(context.RepoRoot));

            string appAbs = System.IO.Path.Combine(context.StagingRoot, appDir);
            string macOsDir = appAbs + "/Contents/MacOS";
            string resourcesDir = appAbs + "/Contents/Resources";

            plan.AddCommand(new ShellCommand("mkdir", new List<string> { "-p", macOsDir, resourcesDir }) { Description = "Create .app bundle layout (" + rid + ")" });
            plan.AddCommand(new ShellCommand("cp", new List<string> { "-R", published.PublishDir + "/.", macOsDir }) { Description = "Copy published binaries into the bundle" });
            plan.AddCommand(new ShellCommand("cp", new List<string> { System.IO.Path.Combine(context.RepoRoot, "assets", "icons", "icon-macos.icns"), resourcesDir + "/" + project + ".icns" }) { Description = "Copy the app icon", ContinueOnError = true });
            plan.AddCommand(new ShellCommand("chmod", new List<string> { "+x", macOsDir + "/" + binaryName }) { Description = "Mark the launcher executable" });

            // The bundled tray agent and CLI ride inside the .app beside the desktop binary, where the app finds
            // the agent to start it.
            foreach (BundledBinary extra in published.Bundled)
            {
                plan.AddCommand(new ShellCommand("chmod", new List<string> { "+x", macOsDir + "/" + extra.FileName }) { Description = "Mark the bundled " + (string.IsNullOrEmpty(extra.Role) ? "binary" : extra.Role) + " executable" });
            }

            // codesign treats everything in Contents/MacOS as code, so a non-code file or folder there (the XML docs,
            // Playwright's .playwright driver folder) fails signing with "bundle format unrecognized". Move each one
            // into Contents/Resources and leave a relative symlink, so lookups next to the executable still work.
            // The script runs from a staged file: command arguments go through $VAR expansion (for secret
            // references), which would empty the script's own shell variables.
            string scriptRelative = "macos-bundle/move-non-code.sh";
            plan.AddFile(scriptRelative, MoveNonCodeScript);
            plan.AddCommand(new ShellCommand("sh", new List<string> { System.IO.Path.Combine(context.StagingRoot, scriptRelative), macOsDir })
            {
                Description = "Move non-code files from Contents/MacOS to Contents/Resources (symlinked back)"
            });

            AddSigning(plan, context, appAbs);
            return appAbs;
        }

        /// <summary>
        /// Signs the bundle: hardened runtime with the configured identity, or ad hoc when none is configured,
        /// then verifies the signature strictly so a broken bundle fails the build instead of reaching users.
        /// </summary>
        /// <param name="plan">The plan to append to.</param>
        /// <param name="context">The publisher context.</param>
        /// <param name="appAbs">The absolute path of the bundle.</param>
        public static void AddSigning(ChannelPlan plan, PublisherContext context, string appAbs)
        {
            SigningEntry? mac = context.Manifest.Signing.MacOs;
            bool canSign = mac != null && !string.IsNullOrWhiteSpace(mac.VaultRef);
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
            else
            {
                plan.AddCommand(new ShellCommand("codesign", new List<string> { "--force", "--deep", "--sign", "-", appAbs })
                { Description = "Ad-hoc sign the .app (no certificate; required on Apple silicon)" });
            }

            plan.AddCommand(new ShellCommand("codesign", new List<string> { "--verify", "--deep", "--strict", "--verbose=2", appAbs })
            { Description = "Verify the bundle signature" });
        }

        /// <summary>
        /// The <c>sh</c> script (argument 1: the <c>Contents/MacOS</c> folder) that moves every entry that is not a
        /// Mach-O binary into <c>Contents/Resources</c> and symlinks it back. Existing symlinks are left alone, so it is
        /// safe to run twice.
        /// </summary>
        public const string MoveNonCodeScript =
            "#!/bin/sh\n"
            + "# Generated by Mux.Publisher: move non-code entries out of Contents/MacOS (argument 1) and symlink them back.\n"
            + "cd \"$1\" || exit 1\n"
            + "for f in * .[!.]*; do\n"
            + "    [ -e \"$f\" ] || [ -L \"$f\" ] || continue\n"
            + "    [ -L \"$f\" ] && continue\n"
            + "    if [ -f \"$f\" ] && file -b \"$f\" | grep -q 'Mach-O'; then continue; fi\n"
            + "    rm -rf \"../Resources/$f\"\n"
            + "    mv \"$f\" ../Resources/ && ln -s \"../Resources/$f\" \"$f\" || exit 1\n"
            + "done\n";

        /// <summary>Whether a Developer ID signing identity is configured for macOS.</summary>
        /// <param name="context">The publisher context.</param>
        /// <returns>True when signing is configured.</returns>
        public static bool CanSign(PublisherContext context)
        {
            SigningEntry? mac = context.Manifest.Signing.MacOs;
            return mac != null && !string.IsNullOrWhiteSpace(mac.VaultRef);
        }

        /// <summary>
        /// Renders an <c>Info.plist</c> for the app bundle.
        /// </summary>
        /// <param name="displayName">The display (and bundle) name.</param>
        /// <param name="bundleId">The CFBundleIdentifier.</param>
        /// <param name="version">The release version.</param>
        /// <param name="executable">The CFBundleExecutable (launcher file name).</param>
        /// <param name="iconName">The icon file name in Contents/Resources, without extension.</param>
        /// <param name="publisher">The publisher, for the copyright line.</param>
        /// <returns>The rendered plist.</returns>
        public static string RenderInfoPlist(string displayName, string bundleId, string version, string executable, string iconName = "mux", string publisher = "")
        {
            StringBuilder builder = new StringBuilder();
            builder.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n");
            builder.Append("<!DOCTYPE plist PUBLIC \"-//Apple//DTD PLIST 1.0//EN\" \"http://www.apple.com/DTDs/PropertyList-1.0.dtd\">\n");
            builder.Append("<plist version=\"1.0\">\n");
            builder.Append("<dict>\n");
            AppendKey(builder, "CFBundleName", displayName);
            AppendKey(builder, "CFBundleDisplayName", displayName);
            AppendKey(builder, "CFBundleIdentifier", bundleId);
            AppendKey(builder, "CFBundleVersion", version);
            AppendKey(builder, "CFBundleShortVersionString", version);
            AppendKey(builder, "CFBundleExecutable", executable);
            AppendKey(builder, "CFBundleIconFile", iconName);
            AppendKey(builder, "CFBundlePackageType", "APPL");
            if (!string.IsNullOrWhiteSpace(publisher))
            {
                AppendKey(builder, "NSHumanReadableCopyright", "Copyright " + DateTime.UtcNow.Year + " " + publisher);
            }

            builder.Append("    <key>LSMinimumSystemVersion</key>\n");
            builder.Append("    <string>11.0</string>\n");
            builder.Append("    <key>NSHighResolutionCapable</key>\n");
            builder.Append("    <true/>\n");
            builder.Append("</dict>\n");
            builder.Append("</plist>\n");
            return builder.ToString();
        }

        /// <summary>XML-escapes a value for a plist or distribution file.</summary>
        /// <param name="value">The raw value.</param>
        /// <returns>The escaped value.</returns>
        public static string Xml(string value)
        {
            return (value ?? string.Empty).Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
        }

        private static void AppendKey(StringBuilder builder, string key, string value)
        {
            builder.Append("    <key>" + key + "</key>\n");
            builder.Append("    <string>" + Xml(value) + "</string>\n");
        }
    }
}
