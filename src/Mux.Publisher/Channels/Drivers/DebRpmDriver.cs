namespace Mux.Publisher.Channels.Drivers
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using Mux.Publisher.Manifest;

    /// <summary>
    /// Builds <c>.deb</c> and <c>.rpm</c> packages from the same staged tree with <c>fpm</c>. The
    /// self-contained publish lands under <c>/opt/&lt;project&gt;</c> with a launcher symlink in
    /// <c>/usr/bin</c> and, for GUI artifacts, a <c>.desktop</c> entry. Login-startup is registered by
    /// the app on first run (per the packaging decision), so no systemd unit is shipped in the package.
    /// </summary>
    public sealed class DebRpmDriver : DriverBase
    {
        /// <inheritdoc />
        public override string Name => "debrpm";

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
                    plan.AddNote("debrpm: no publish for " + rid + "; skipped.");
                    continue;
                }

                string binaryName = System.IO.Path.GetFileName(published.PrimaryBinary);
                string stageDir = "debrpm/" + rid + "/root";
                string stageAbs = System.IO.Path.Combine(context.StagingRoot, stageDir);
                string optDir = stageAbs + "/opt/" + project;

                if (context.Artifact.Kind == ArtifactKind.Gui)
                {
                    plan.AddFile(stageDir + "/usr/share/applications/" + project + ".desktop",
                        RenderDesktopEntry(display, context.Manifest.Project.Description, "/opt/" + project + "/" + binaryName, project));
                }

                // Assemble the payload tree.
                plan.AddCommand(new ShellCommand("mkdir", new List<string> { "-p", optDir, stageAbs + "/usr/bin" }) { Description = "Create package tree (" + rid + ")" });
                plan.AddCommand(new ShellCommand("cp", new List<string> { "-R", published.PublishDir + "/.", optDir }) { Description = "Copy published binaries" });
                plan.AddCommand(new ShellCommand("chmod", new List<string> { "+x", optDir + "/" + binaryName }) { Description = "Mark the launcher executable" });
                plan.AddCommand(new ShellCommand("ln", new List<string> { "-sf", "/opt/" + project + "/" + binaryName, stageAbs + "/usr/bin/" + project }) { Description = "Symlink launcher into /usr/bin" });

                foreach (string type in new[] { "deb", "rpm" })
                {
                    string arch = type == "deb" ? ChannelHelpers.DebArch(rid) : ChannelHelpers.RpmArch(rid);
                    string outFile = type == "deb"
                        ? System.IO.Path.Combine(context.OutputRoot, "linux", Naming.Deb(project, context.Version, rid))
                        : System.IO.Path.Combine(context.OutputRoot, "linux", Naming.Rpm(project, context.Version, rid));

                    plan.AddCommand(new ShellCommand("fpm", new List<string>
                    {
                        "-s", "dir",
                        "-t", type,
                        "-n", project,
                        "-v", context.Version,
                        "-a", arch,
                        "--license", context.Manifest.Project.License,
                        "--description", context.Manifest.Project.Description,
                        "--url", context.Manifest.Project.Homepage,
                        "--maintainer", "Joel Christner",
                        "-C", stageAbs,
                        "-p", outFile,
                        "--force",
                        "."
                    })
                    { Description = "Build " + type + " for " + rid });
                }
            }

            plan.AddNote("debrpm: install directly (dpkg -i / rpm -i) or via the apt/yum repos (apt-get install / dnf install " + project + ").");
            return plan;
        }

        /// <summary>
        /// Renders a freedesktop <c>.desktop</c> entry for a GUI package.
        /// </summary>
        /// <param name="name">The display name.</param>
        /// <param name="comment">The comment/description.</param>
        /// <param name="exec">The executable path.</param>
        /// <param name="icon">The icon name.</param>
        /// <returns>The rendered desktop entry.</returns>
        public static string RenderDesktopEntry(string name, string comment, string exec, string icon)
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("[Desktop Entry]");
            builder.AppendLine("Type=Application");
            builder.AppendLine("Name=" + name);
            builder.AppendLine("Comment=" + comment);
            builder.AppendLine("Exec=" + exec);
            builder.AppendLine("Icon=" + icon);
            builder.AppendLine("Terminal=false");
            builder.AppendLine("Categories=Development;Utility;");
            return builder.ToString();
        }
    }
}
