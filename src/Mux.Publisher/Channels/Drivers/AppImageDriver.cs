namespace Mux.Publisher.Channels.Drivers
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using Mux.Publisher.Manifest;

    /// <summary>
    /// Builds a distro-agnostic AppImage with <c>appimagetool</c>. It assembles an AppDir (an
    /// <c>AppRun</c> launcher, a <c>.desktop</c> entry, an icon, and the self-contained publish under
    /// <c>usr/bin</c>), then packs it into a single portable <c>.AppImage</c> that runs in place — no
    /// installation step, matching the packaging decision that the app self-registers startup.
    /// </summary>
    public sealed class AppImageDriver : DriverBase
    {
        /// <inheritdoc />
        public override string Name => "appimage";

        /// <inheritdoc />
        public override TargetOs RequiredOs => TargetOs.Linux;

        /// <inheritdoc />
        public override ChannelPlan Plan(PublisherContext context, ChannelConfig config)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (config == null) throw new ArgumentNullException(nameof(config));

            ChannelPlan plan = new ChannelPlan(Name);
            string project = Project(context);
            string display = Display(context);

            foreach (string rid in ChannelHelpers.ResolveRuntimes(context.Manifest, config, RequiredOs))
            {
                PublishedArtifact? published = context.PublishedFor(rid);
                if (published == null)
                {
                    plan.AddNote("appimage: no publish for " + rid + "; skipped.");
                    continue;
                }

                string binaryName = System.IO.Path.GetFileName(published.PrimaryBinary);
                string appDir = "appimage/" + rid + "/" + display + ".AppDir";
                string appDirAbs = System.IO.Path.Combine(context.StagingRoot, appDir);

                plan.AddFile(appDir + "/AppRun", RenderAppRun(binaryName));
                plan.AddFile(appDir + "/" + project + ".desktop",
                    DebRpmDriver.RenderDesktopEntry(display, context.Manifest.Project.Description, binaryName, project));

                plan.AddCommand(new ShellCommand("mkdir", new List<string> { "-p", appDirAbs + "/usr/bin" }) { Description = "Create AppDir (" + rid + ")" });
                plan.AddCommand(new ShellCommand("cp", new List<string> { "-R", published.PublishDir + "/.", appDirAbs + "/usr/bin" }) { Description = "Copy published binaries" });
                plan.AddCommand(new ShellCommand("chmod", new List<string> { "+x", appDirAbs + "/usr/bin/" + binaryName, appDirAbs + "/AppRun" }) { Description = "Mark launcher + AppRun executable" });

                // The bundled tray agent and CLI ride inside the AppDir (beside the desktop binary, so the
                // agent autostart resolves). Mark them executable too.
                foreach (BundledBinary extra in published.Bundled)
                {
                    plan.AddCommand(new ShellCommand("chmod", new List<string> { "+x", appDirAbs + "/usr/bin/" + extra.FileName }) { Description = "Mark the bundled " + (string.IsNullOrEmpty(extra.Role) ? "binary" : extra.Role) + " executable" });
                }

                plan.AddCommand(new ShellCommand("cp", new List<string> { System.IO.Path.Combine(context.RepoRoot, "assets", "icon-green.png"), appDirAbs + "/" + project + ".png" }) { Description = "Copy the app icon", ContinueOnError = true });

                // appimagetool reads the target architecture from the ARCH environment variable, which
                // the release workflow exports (aarch64/x86_64) before invoking this channel.
                string outFile = System.IO.Path.Combine(context.OutputRoot, "linux", Naming.AppImage(display, context.Version, rid));
                plan.AddCommand(new ShellCommand("appimagetool", new List<string> { appDirAbs, outFile })
                {
                    Description = "Pack the AppImage for " + rid + " (set ARCH=" + (rid.EndsWith("arm64", StringComparison.OrdinalIgnoreCase) ? "aarch64" : "x86_64") + ")"
                });
            }

            plan.AddNote("appimage: portable single file — chmod +x and run; no installation needed.");
            return plan;
        }

        /// <summary>
        /// Renders the AppDir <c>AppRun</c> launcher that execs the published binary.
        /// </summary>
        /// <param name="binaryName">The published launcher file name.</param>
        /// <returns>The rendered shell script.</returns>
        public static string RenderAppRun(string binaryName)
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("#!/bin/sh");
            builder.AppendLine("HERE=\"$(dirname \"$(readlink -f \"$0\")\")\"");
            builder.AppendLine("exec \"$HERE/usr/bin/" + binaryName + "\" \"$@\"");
            return builder.ToString();
        }
    }
}
