namespace Mux.Publisher.Channels.Drivers
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using Mux.Publisher.Manifest;

    /// <summary>
    /// Builds the recommended macOS installer, a <c>.pkg</c>. It installs the signed <c>.app</c> into
    /// <c>/Applications</c>, and its postinstall script puts the command on PATH (<c>/usr/local/bin</c>) and installs
    /// and starts the tray agent's LaunchAgent for the console user, replacing an agent registered under an earlier
    /// identifier. Installer.app shows the product name, the logo (with a dark-mode variant), a welcome page with the
    /// tagline and what gets installed, and the license, which the user must agree to before installing.
    /// </summary>
    public sealed class PkgDriver : DriverBase
    {
        /// <summary>The LaunchAgent label mux 1.x registered, removed on upgrade so only one agent starts.</summary>
        public const string LegacyAgentLabel = "com.jchristn.mux.agent";

        /// <inheritdoc />
        public override string Name => "pkg";

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
            string identifier = Identifier(context);
            ProjectInfo info = context.Manifest.Project;

            if (!MacAppBundle.CanSign(context))
            {
                plan.AddNote("macOS: the .pkg is not signed with a Developer ID Installer certificate (by policy). Users open it with System Settings > Privacy & Security > Open Anyway, or install it with 'sudo installer -pkg <file> -target /'.");
            }

            foreach (string rid in ChannelHelpers.ResolveRuntimes(context.Manifest, config, RequiredOs))
            {
                PublishedArtifact? published = context.PublishedFor(rid);
                if (published == null)
                {
                    plan.AddNote("pkg: no publish for " + rid + "; skipped.");
                    continue;
                }

                string baseDir = "pkg/" + rid;
                string rootAbs = System.IO.Path.Combine(context.StagingRoot, baseDir, "root");
                string scriptsAbs = System.IO.Path.Combine(context.StagingRoot, baseDir, "scripts");
                string resourcesAbs = System.IO.Path.Combine(context.StagingRoot, baseDir, "resources");
                string componentDirAbs = System.IO.Path.Combine(context.StagingRoot, baseDir, "component");
                string componentPlistAbs = System.IO.Path.Combine(context.StagingRoot, baseDir, "component.plist");
                string distributionAbs = System.IO.Path.Combine(context.StagingRoot, baseDir, "distribution.xml");
                string componentPkgName = identifier + ".pkg";

                // 1. The payload: the signed .app under Applications/ (installed relative to /).
                MacAppBundle.Assemble(plan, context, published, baseDir + "/root/Applications", display, identifier);

                string? agent = AgentFileName(context, published);
                string? cli = CliFileName(context, published);

                // 2. The postinstall script and the Installer.app resources.
                plan.AddFile(baseDir + "/scripts/postinstall", RenderPostinstall(display, identifier + ".agent", agent, cli));
                plan.AddCommand(new ShellCommand("chmod", new List<string> { "+x", scriptsAbs + "/postinstall" }) { Description = "Mark the postinstall script executable" });

                plan.AddFile(baseDir + "/resources/welcome.html", RenderWelcome(display, info.Tagline, context.Version, agent != null, cli));
                plan.AddFile(baseDir + "/resources/license.txt", Mux.Publisher.Publishing.LicenseAssets.ReadLicenseText(context.RepoRoot));
                plan.AddCommand(new ShellCommand("mkdir", new List<string> { "-p", resourcesAbs, componentDirAbs }) { Description = "Create installer resource folders" });
                foreach (string background in new[] { "pkg-background.png", "pkg-background-dark.png" })
                {
                    plan.AddCommand(new ShellCommand("cp", new List<string>
                    {
                        System.IO.Path.Combine(context.RepoRoot, "assets", "installer", background), resourcesAbs + "/" + background
                    })
                    { Description = "Copy the installer background (" + background + ")", ContinueOnError = true });
                }

                plan.AddFile(baseDir + "/distribution.xml", RenderDistribution(display, identifier, context.Version, componentPkgName, rid));

                // 3. The component package. Turn off bundle relocation, or Installer would "upgrade" a copy of the
                //    app found anywhere on disk instead of installing into /Applications.
                plan.AddCommand(new ShellCommand("pkgbuild", new List<string> { "--analyze", "--root", rootAbs, componentPlistAbs }) { Description = "Describe the payload's bundles" });
                plan.AddCommand(new ShellCommand("plutil", new List<string> { "-replace", "0.BundleIsRelocatable", "-bool", "NO", componentPlistAbs }) { Description = "Install the app in /Applications only (no relocation)" });
                plan.AddCommand(new ShellCommand("pkgbuild", new List<string>
                {
                    "--root", rootAbs,
                    "--component-plist", componentPlistAbs,
                    "--identifier", identifier,
                    "--version", context.Version,
                    "--install-location", "/",
                    "--scripts", scriptsAbs,
                    componentDirAbs + "/" + componentPkgName
                })
                { Description = "Build the component package for " + rid });

                // 4. The product archive Installer.app opens.
                string pkgOut = System.IO.Path.Combine(context.OutputRoot, "macos", Naming.Pkg(project, context.Version, rid));
                plan.AddCommand(new ShellCommand("productbuild", new List<string>
                {
                    "--distribution", distributionAbs,
                    "--resources", resourcesAbs,
                    "--package-path", componentDirAbs,
                    pkgOut
                })
                { Description = "Build the .pkg for " + rid });
            }

            return plan;
        }

        /// <summary>
        /// Renders the Installer.app distribution file: title, logo backgrounds for light and dark mode, the
        /// welcome page, the license that must be agreed to, the host architecture, and the single package.
        /// </summary>
        /// <param name="display">The product name.</param>
        /// <param name="identifier">The package identifier.</param>
        /// <param name="version">The release version.</param>
        /// <param name="componentPkgName">The component package file name.</param>
        /// <param name="rid">The runtime identifier (selects the host architecture).</param>
        /// <returns>The distribution XML.</returns>
        public static string RenderDistribution(string display, string identifier, string version, string componentPkgName, string rid)
        {
            string arch = rid != null && rid.EndsWith("arm64", StringComparison.OrdinalIgnoreCase) ? "arm64" : "x86_64";
            string title = MacAppBundle.Xml(display);
            StringBuilder b = new StringBuilder();
            b.Append("<?xml version=\"1.0\" encoding=\"utf-8\"?>\n");
            b.Append("<installer-gui-script minSpecVersion=\"2\">\n");
            b.Append("    <title>" + title + "</title>\n");
            b.Append("    <options customize=\"never\" require-scripts=\"false\" hostArchitectures=\"" + arch + "\"/>\n");
            b.Append("    <domains enable_localSystem=\"true\"/>\n");
            b.Append("    <volume-check>\n");
            b.Append("        <allowed-os-versions>\n");
            b.Append("            <os-version min=\"11.0\"/>\n");
            b.Append("        </allowed-os-versions>\n");
            b.Append("    </volume-check>\n");
            b.Append("    <background file=\"pkg-background.png\" alignment=\"bottomleft\" scaling=\"none\"/>\n");
            b.Append("    <background-darkAqua file=\"pkg-background-dark.png\" alignment=\"bottomleft\" scaling=\"none\"/>\n");
            b.Append("    <welcome file=\"welcome.html\" mime-type=\"text/html\"/>\n");
            b.Append("    <license file=\"license.txt\" mime-type=\"text/plain\"/>\n");
            b.Append("    <choices-outline>\n");
            b.Append("        <line choice=\"default\">\n");
            b.Append("            <line choice=\"" + identifier + "\"/>\n");
            b.Append("        </line>\n");
            b.Append("    </choices-outline>\n");
            b.Append("    <choice id=\"default\"/>\n");
            b.Append("    <choice id=\"" + identifier + "\" visible=\"false\" title=\"" + title + "\">\n");
            b.Append("        <pkg-ref id=\"" + identifier + "\"/>\n");
            b.Append("    </choice>\n");
            b.Append("    <pkg-ref id=\"" + identifier + "\" version=\"" + MacAppBundle.Xml(version) + "\" onConclusion=\"none\">" + componentPkgName + "</pkg-ref>\n");
            b.Append("</installer-gui-script>\n");
            return b.ToString();
        }

        /// <summary>
        /// Renders the welcome page: the tagline and what the installer puts on the Mac.
        /// </summary>
        /// <param name="display">The product name.</param>
        /// <param name="tagline">The tagline (may be empty).</param>
        /// <param name="version">The release version.</param>
        /// <param name="hasAgent">Whether a tray agent is installed.</param>
        /// <param name="cli">The command name, or null.</param>
        /// <returns>The welcome HTML.</returns>
        public static string RenderWelcome(string display, string tagline, string version, bool hasAgent, string? cli)
        {
            string name = MacAppBundle.Xml(display);
            StringBuilder b = new StringBuilder();
            b.Append("<!DOCTYPE html>\n<html><head><meta charset=\"utf-8\">\n");
            b.Append("<style>body{font:13px -apple-system,BlinkMacSystemFont,sans-serif;line-height:1.45}h1{font-size:17px;margin:0 0 4px}p.tag{font-size:14px;margin:0 0 14px}ul{padding-left:18px}</style>\n");
            b.Append("</head><body>\n");
            b.Append("<h1>" + name + " " + MacAppBundle.Xml(version) + "</h1>\n");
            if (!string.IsNullOrWhiteSpace(tagline)) b.Append("<p class=\"tag\">" + MacAppBundle.Xml(tagline) + "</p>\n");
            b.Append("<p>This installs:</p>\n<ul>\n");
            b.Append("<li><b>" + name + "</b> in your Applications folder.</li>\n");
            if (!string.IsNullOrWhiteSpace(cli))
            {
                b.Append("<li>The <code>" + MacAppBundle.Xml(cli!) + "</code> command, linked at <code>/usr/local/bin/" + MacAppBundle.Xml(cli!) + "</code> so it works in Terminal.</li>\n");
            }

            if (hasAgent)
            {
                b.Append("<li>The " + name + " tray agent, which starts when you log in and keeps " + name + " running in the background. Quit it from its menu bar icon at any time.</li>\n");
            }

            b.Append("</ul>\n");
            b.Append("<p>" + name + " is free, open-source software under the MIT license.</p>\n");
            b.Append("</body></html>\n");
            return b.ToString();
        }

        /// <summary>
        /// Renders the postinstall script. It links the command into <c>/usr/local/bin</c> (creating the folder,
        /// which a fresh Mac may not have), then, for the user at the console, removes a LaunchAgent registered under
        /// the legacy label, writes the agent's LaunchAgent, and starts it. Every step after the link is
        /// best-effort, so a missing console user (an unattended install) never fails the install.
        /// </summary>
        /// <param name="display">The product (and .app) name.</param>
        /// <param name="agentLabel">The LaunchAgent label, for example <c>ai.latticetech.mux.agent</c>.</param>
        /// <param name="agentBinary">The agent executable name inside Contents/MacOS, or null for no agent.</param>
        /// <param name="cli">The command name inside Contents/MacOS, or null for no command.</param>
        /// <returns>The script, with LF line endings.</returns>
        public static string RenderPostinstall(string display, string agentLabel, string? agentBinary, string? cli)
        {
            string app = "/Applications/" + display + ".app";
            StringBuilder b = new StringBuilder();
            b.Append("#!/bin/sh\n");
            b.Append("# Generated by Mux.Publisher. Runs as root after Installer copies " + display + ".app into /Applications.\n");
            b.Append("APP=\"" + app + "\"\n");
            if (!string.IsNullOrWhiteSpace(cli))
            {
                b.Append("\n# Put the command on PATH. A fresh Mac may not have /usr/local/bin yet.\n");
                b.Append("mkdir -p /usr/local/bin\n");
                b.Append("ln -sf \"$APP/Contents/MacOS/" + cli + "\" /usr/local/bin/" + cli + "\n");
            }

            if (!string.IsNullOrWhiteSpace(agentBinary))
            {
                b.Append("\n# Start the tray agent at login for the user at the console (none during an unattended install).\n");
                b.Append("USER_NAME=$(stat -f%Su /dev/console 2>/dev/null)\n");
                b.Append("case \"$USER_NAME\" in \"\"|root|loginwindow|_mbsetupuser) exit 0 ;; esac\n");
                b.Append("USER_ID=$(id -u \"$USER_NAME\" 2>/dev/null) || exit 0\n");
                b.Append("USER_HOME=$(dscl . -read \"/Users/$USER_NAME\" NFSHomeDirectory 2>/dev/null | awk '{print $2}')\n");
                b.Append("[ -n \"$USER_HOME\" ] || USER_HOME=\"/Users/$USER_NAME\"\n");
                b.Append("AGENTS=\"$USER_HOME/Library/LaunchAgents\"\n");
                b.Append("PLIST=\"$AGENTS/" + agentLabel + ".plist\"\n");
                b.Append("\n# Remove the agent an earlier version registered under its old label, so only one agent starts.\n");
                b.Append("launchctl bootout \"gui/$USER_ID/" + LegacyAgentLabel + "\" >/dev/null 2>&1 || true\n");
                b.Append("rm -f \"$AGENTS/" + LegacyAgentLabel + ".plist\"\n");
                b.Append("\nmkdir -p \"$AGENTS\"\n");
                b.Append("chown \"$USER_NAME\" \"$AGENTS\" 2>/dev/null || true\n");
                b.Append("cat > \"$PLIST\" <<'PLIST_EOF'\n");
                b.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n");
                b.Append("<!DOCTYPE plist PUBLIC \"-//Apple//DTD PLIST 1.0//EN\" \"http://www.apple.com/DTDs/PropertyList-1.0.dtd\">\n");
                b.Append("<plist version=\"1.0\">\n");
                b.Append("<dict>\n");
                b.Append("    <key>Label</key>\n");
                b.Append("    <string>" + agentLabel + "</string>\n");
                b.Append("    <key>ProgramArguments</key>\n");
                b.Append("    <array>\n");
                b.Append("        <string>" + MacAppBundle.Xml(app + "/Contents/MacOS/" + agentBinary) + "</string>\n");
                b.Append("    </array>\n");
                b.Append("    <key>RunAtLoad</key>\n");
                b.Append("    <true/>\n");
                b.Append("    <key>KeepAlive</key>\n");
                b.Append("    <dict>\n");
                b.Append("        <key>SuccessfulExit</key>\n");
                b.Append("        <false/>\n");
                b.Append("    </dict>\n");
                b.Append("</dict>\n");
                b.Append("</plist>\n");
                b.Append("PLIST_EOF\n");
                b.Append("chown \"$USER_NAME\" \"$PLIST\" 2>/dev/null || true\n");
                b.Append("chmod 644 \"$PLIST\"\n");
                b.Append("\n# Restart the agent from the newly installed app.\n");
                b.Append("launchctl bootout \"gui/$USER_ID/" + agentLabel + "\" >/dev/null 2>&1 || true\n");
                b.Append("launchctl bootstrap \"gui/$USER_ID\" \"$PLIST\" >/dev/null 2>&1 || true\n");
            }

            b.Append("\nexit 0\n");
            return b.ToString();
        }
    }
}
