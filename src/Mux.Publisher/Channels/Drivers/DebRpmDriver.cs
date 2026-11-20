namespace Mux.Publisher.Channels.Drivers
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using Mux.Publisher.Manifest;

    /// <summary>
    /// Builds <c>.deb</c> and <c>.rpm</c> packages from the same staged tree with <c>fpm</c>. The self-contained
    /// publish lands under <c>/opt/&lt;project&gt;</c> with the command symlinked into <c>/usr/bin</c>. GUI packages
    /// also install a menu entry and hicolor icons named after the project identifier, an
    /// <c>/etc/xdg/autostart</c> entry that starts the bundled tray agent in each user's desktop session (a root
    /// systemd service can't reach the session), and the license under <c>/usr/share/doc</c> and
    /// <c>/usr/share/licenses</c>. Runtime dependencies (ICU, OpenSSL, the X11 client libraries, fontconfig) are
    /// declared so a missing library fails the install instead of the first launch.
    /// </summary>
    public sealed class DebRpmDriver : DriverBase
    {
        /// <summary>Icon sizes installed under <c>/usr/share/icons/hicolor</c> (from <c>assets/installer/hicolor</c>).</summary>
        public static readonly int[] IconSizes = { 48, 64, 128, 256, 512 };

        /// <summary>Debian/Ubuntu dependencies of a self-contained .NET desktop app with an Avalonia tray agent.</summary>
        public static readonly string[] DebDepends =
        {
            "libicu76 | libicu75 | libicu74 | libicu73 | libicu72 | libicu71 | libicu70 | libicu69 | libicu68 | libicu67 | libicu66",
            "libssl3t64 | libssl3 | libssl1.1",
            "zlib1g",
            "libstdc++6",
            "libgcc-s1",
            "ca-certificates",
            "libx11-6",
            "libice6",
            "libsm6",
            "libfontconfig1"
        };

        /// <summary>Fedora/RHEL/openSUSE dependencies of the same payload.</summary>
        public static readonly string[] RpmDepends =
        {
            "libicu",
            "openssl-libs",
            "zlib",
            "libstdc++",
            "libgcc",
            "ca-certificates",
            "libX11",
            "libICE",
            "libSM",
            "fontconfig"
        };

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
            string identifier = Identifier(context);
            ProjectInfo info = context.Manifest.Project;
            bool gui = context.Artifact.Kind == ArtifactKind.Gui;
            string licenseText = Mux.Publisher.Publishing.LicenseAssets.ReadLicenseText(context.RepoRoot);

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
                string? agent = AgentFileName(context, published);

                // The license, where each distribution family looks for it.
                plan.AddFile(stageDir + "/usr/share/doc/" + project + "/copyright", licenseText);
                plan.AddFile(stageDir + "/usr/share/licenses/" + project + "/LICENSE", licenseText);

                if (gui)
                {
                    plan.AddFile(stageDir + "/usr/share/applications/" + identifier + ".desktop",
                        RenderDesktopEntry(display, DesktopComment(info), "/opt/" + project + "/" + binaryName, identifier));
                    if (agent != null)
                    {
                        plan.AddFile(stageDir + "/etc/xdg/autostart/" + identifier + ".agent.desktop",
                            RenderAutostartEntry(display, "/opt/" + project + "/" + agent));
                    }
                }

                // Assemble the payload tree.
                plan.AddCommand(new ShellCommand("mkdir", new List<string> { "-p", optDir, stageAbs + "/usr/bin" }) { Description = "Create package tree (" + rid + ")" });
                plan.AddCommand(new ShellCommand("cp", new List<string> { "-R", published.PublishDir + "/.", optDir }) { Description = "Copy published binaries" });
                plan.AddCommand(new ShellCommand("chmod", new List<string> { "+x", optDir + "/" + binaryName }) { Description = "Mark the launcher executable" });

                if (gui)
                {
                    foreach (int size in IconSizes)
                    {
                        string dir = stageAbs + "/usr/share/icons/hicolor/" + size + "x" + size + "/apps";
                        plan.AddCommand(new ShellCommand("mkdir", new List<string> { "-p", dir }) { Description = "Create the " + size + "px icon folder" });
                        plan.AddCommand(new ShellCommand("cp", new List<string>
                        {
                            System.IO.Path.Combine(context.RepoRoot, "assets", "installer", "hicolor", size + "x" + size, "apps", identifier + ".png"),
                            dir + "/" + identifier + ".png"
                        })
                        { Description = "Install the " + size + "px icon" });
                    }
                }

                // The `mux` command on PATH should be the CLI, not the GUI: when the desktop payload bundles the
                // CLI, point /usr/bin/mux at it (and mark the bundled binaries executable). The menu entry still
                // launches the GUI binary.
                string cliBinary = published.CliBinary ?? CliFileName(context, published) ?? binaryName;
                foreach (BundledBinary extra in published.Bundled)
                {
                    plan.AddCommand(new ShellCommand("chmod", new List<string> { "+x", optDir + "/" + extra.FileName }) { Description = "Mark the bundled " + (string.IsNullOrEmpty(extra.Role) ? "binary" : extra.Role) + " executable" });
                }

                plan.AddCommand(new ShellCommand("ln", new List<string> { "-sf", "/opt/" + project + "/" + cliBinary, stageAbs + "/usr/bin/" + project }) { Description = "Symlink the CLI into /usr/bin" });

                foreach (string type in new[] { "deb", "rpm" })
                {
                    string arch = type == "deb" ? ChannelHelpers.DebArch(rid) : ChannelHelpers.RpmArch(rid);
                    string outFile = type == "deb"
                        ? System.IO.Path.Combine(context.OutputRoot, "linux", Naming.Deb(project, context.Version, rid))
                        : System.IO.Path.Combine(context.OutputRoot, "linux", Naming.Rpm(project, context.Version, rid));

                    List<string> args = new List<string>
                    {
                        "-s", "dir",
                        "-t", type,
                        "-n", project,
                        "-v", context.Version,
                        "-a", arch,
                        "--license", info.License,
                        "--description", PackageDescription(info),
                        "--url", info.Homepage,
                        "--vendor", info.EffectivePublisher,
                        "--maintainer", info.EffectiveMaintainer,
                        "--category", type == "deb" ? "devel" : "Development/Tools"
                    };

                    if (gui)
                    {
                        foreach (string dependency in type == "deb" ? DebDepends : RpmDepends)
                        {
                            args.Add("-d");
                            args.Add(dependency);
                        }
                    }

                    if (type == "deb")
                    {
                        // The autostart entry is not a user-edited config file; don't prompt about it on upgrade.
                        args.Add("--deb-no-default-config-files");
                    }

                    args.AddRange(new[] { "-C", stageAbs, "-p", outFile, "--force", "." });
                    plan.AddCommand(new ShellCommand("fpm", args) { Description = "Build " + type + " for " + rid });
                }
            }

            plan.AddNote("debrpm: install with 'sudo apt install ./<file>.deb' or 'sudo dnf install ./<file>.rpm' (apt and dnf install the declared dependencies; dpkg -i and rpm -i do not).");
            return plan;
        }

        /// <summary>The package description: the tagline, then the longer paragraph (fpm puts the first line in the summary).</summary>
        /// <param name="info">The project block.</param>
        /// <returns>The description.</returns>
        public static string PackageDescription(ProjectInfo info)
        {
            if (string.IsNullOrWhiteSpace(info.Tagline)) return info.Description;
            return info.Tagline + "\n" + info.Description;
        }

        /// <summary>The menu entry comment: the tagline when there is one, otherwise the description.</summary>
        /// <param name="info">The project block.</param>
        /// <returns>The comment.</returns>
        public static string DesktopComment(ProjectInfo info)
        {
            return string.IsNullOrWhiteSpace(info.Tagline) ? info.Description : info.Tagline;
        }

        /// <summary>
        /// Renders the <c>/etc/xdg/autostart</c> entry that starts the tray agent in every user's desktop session.
        /// A user's own <c>~/.config/autostart</c> entry of the same name overrides it.
        /// </summary>
        /// <param name="display">The product name.</param>
        /// <param name="exec">The agent executable path.</param>
        /// <returns>The desktop entry.</returns>
        public static string RenderAutostartEntry(string display, string exec)
        {
            StringBuilder builder = new StringBuilder();
            builder.Append("[Desktop Entry]\n");
            builder.Append("Type=Application\n");
            builder.Append("Name=" + display + " Agent\n");
            builder.Append("Comment=Keeps " + display + " running in the background (tray agent)\n");
            builder.Append("Exec=" + exec + "\n");
            builder.Append("Terminal=false\n");
            builder.Append("NoDisplay=true\n");
            builder.Append("X-GNOME-Autostart-enabled=true\n");
            return builder.ToString();
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
            builder.Append("[Desktop Entry]\n");
            builder.Append("Type=Application\n");
            builder.Append("Name=" + name + "\n");
            builder.Append("Comment=" + comment + "\n");
            builder.Append("Exec=" + exec + "\n");
            builder.Append("Icon=" + icon + "\n");
            builder.Append("Terminal=false\n");
            builder.Append("Categories=Development;Utility;\n");
            return builder.ToString();
        }
    }
}
