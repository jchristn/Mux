namespace Mux.Core.Startup
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Runtime.InteropServices;

    /// <summary>
    /// Registers the mux tray agent to start at login, cross-platform, from within the app itself — so
    /// the installers never need to run a startup script (which is why macOS can ship a plain <c>.dmg</c>
    /// rather than a <c>.pkg</c>). The recipe builders are pure and unit tested; <see cref="EnsureRegisteredOnFirstRun"/>
    /// performs the idempotent, best-effort install. This ports the logic of the repository's
    /// <c>scripts/{windows,macos,linux}/run-at-startup.*</c> helpers into the product.
    /// </summary>
    public static class StartupRegistrar
    {
        /// <summary>The launchd/systemd label and Run-key value name used for the agent.</summary>
        public const string Label = "com.jchristn.mux.agent";

        /// <summary>The Windows HKCU Run value name.</summary>
        public const string WindowsRunValueName = "MuxAgent";

        /// <summary>The marker file (under the config directory) recording that first-run registration ran.</summary>
        public const string MarkerFileName = "startup-registered";

        /// <summary>The environment variable that, set to "0", opts out of auto-start registration.</summary>
        public const string OptOutEnvVar = "MUX_AGENT_AUTOSTART";

        /// <summary>
        /// Registers the agent for login-startup exactly once, unless the user opted out. Safe to call on
        /// every launch: it returns immediately when a marker file already records a prior registration or
        /// when <c>MUX_AGENT_AUTOSTART=0</c>. All I/O is best-effort — failures are swallowed so a
        /// registration problem never blocks the agent from running.
        /// </summary>
        /// <param name="executablePath">The absolute path to the agent executable to register.</param>
        /// <param name="configDirectory">The directory where the first-run marker is stored.</param>
        /// <returns>True when registration was performed on this call; false when skipped.</returns>
        public static bool EnsureRegisteredOnFirstRun(string executablePath, string configDirectory)
        {
            if (string.IsNullOrWhiteSpace(executablePath)) return false;

            string? optOut = Environment.GetEnvironmentVariable(OptOutEnvVar);
            if (string.Equals(optOut, "0", StringComparison.Ordinal)) return false;

            string marker = Path.Combine(configDirectory, MarkerFileName);
            try
            {
                if (File.Exists(marker)) return false;
            }
            catch
            {
                // Unable to read the marker; fall through and attempt once.
            }

            bool ok = Register(executablePath);

            try
            {
                Directory.CreateDirectory(configDirectory);
                File.WriteAllText(marker, DateTimeOffset.UtcNow.ToString("o"));
            }
            catch
            {
                // Best-effort marker; if it fails we may retry next launch, which is harmless (idempotent).
            }

            return ok;
        }

        /// <summary>
        /// Performs the platform-specific registration once, best-effort.
        /// </summary>
        /// <param name="executablePath">The absolute path to the agent executable.</param>
        /// <returns>True on apparent success.</returns>
        public static bool Register(string executablePath)
        {
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return RegisterWindows(executablePath);
                if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return RegisterMac(executablePath);
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) return RegisterLinux(executablePath);
            }
            catch
            {
                // Swallow — registration is a convenience, never a hard requirement.
            }

            return false;
        }

        private static bool RegisterWindows(string executablePath)
        {
            List<string> args = BuildWindowsRegArgs(executablePath);
            return RunQuiet("reg", args);
        }

        private static bool RegisterMac(string executablePath)
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string plistPath = Path.Combine(home, "Library", "LaunchAgents", Label + ".plist");
            Directory.CreateDirectory(Path.GetDirectoryName(plistPath)!);
            File.WriteAllText(plistPath, BuildLaunchAgentPlist(executablePath));

            // Load it now so the agent is running after first registration (bootout any stale copy first).
            string uid = GetUidBestEffort();
            RunQuiet("launchctl", new List<string> { "bootout", "gui/" + uid + "/" + Label });
            bool ok = RunQuiet("launchctl", new List<string> { "bootstrap", "gui/" + uid, plistPath });
            return ok || File.Exists(plistPath);
        }

        private static bool RegisterLinux(string executablePath)
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string unitPath = Path.Combine(home, ".config", "systemd", "user", "mux-agent.service");
            Directory.CreateDirectory(Path.GetDirectoryName(unitPath)!);
            File.WriteAllText(unitPath, BuildSystemdUserUnit(executablePath));

            bool systemd = RunQuiet("systemctl", new List<string> { "--user", "daemon-reload" })
                           && RunQuiet("systemctl", new List<string> { "--user", "enable", "--now", "mux-agent" });
            if (systemd) return true;

            // Fallback: a desktop autostart entry when systemd --user is unavailable.
            string desktopPath = Path.Combine(home, ".config", "autostart", "mux-agent.desktop");
            Directory.CreateDirectory(Path.GetDirectoryName(desktopPath)!);
            File.WriteAllText(desktopPath, BuildAutostartDesktopEntry(executablePath));
            return File.Exists(desktopPath);
        }

        // ----- Pure recipe builders (unit tested) --------------------------------------------------

        /// <summary>
        /// Builds the <c>reg add</c> argument list that writes the HKCU Run value for the agent.
        /// </summary>
        /// <param name="executablePath">The absolute path to the agent executable.</param>
        /// <returns>The argument list for <c>reg</c>.</returns>
        public static List<string> BuildWindowsRegArgs(string executablePath)
        {
            return new List<string>
            {
                "add",
                "HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Run",
                "/v", WindowsRunValueName,
                "/t", "REG_SZ",
                "/d", "\"" + executablePath + "\"",
                "/f"
            };
        }

        /// <summary>
        /// Builds the launchd user-agent plist. <c>RunAtLoad</c> starts it at login; <c>KeepAlive</c>
        /// with <c>SuccessfulExit=false</c> restarts on crash but respects a clean Exit from the tray menu.
        /// </summary>
        /// <param name="executablePath">The absolute path to the agent executable.</param>
        /// <returns>The plist XML.</returns>
        public static string BuildLaunchAgentPlist(string executablePath)
        {
            System.Text.StringBuilder b = new System.Text.StringBuilder();
            b.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
            b.AppendLine("<!DOCTYPE plist PUBLIC \"-//Apple//DTD PLIST 1.0//EN\" \"http://www.apple.com/DTDs/PropertyList-1.0.dtd\">");
            b.AppendLine("<plist version=\"1.0\">");
            b.AppendLine("<dict>");
            b.AppendLine("    <key>Label</key>");
            b.AppendLine("    <string>" + Label + "</string>");
            b.AppendLine("    <key>ProgramArguments</key>");
            b.AppendLine("    <array>");
            b.AppendLine("        <string>" + XmlEscape(executablePath) + "</string>");
            b.AppendLine("    </array>");
            b.AppendLine("    <key>RunAtLoad</key>");
            b.AppendLine("    <true/>");
            b.AppendLine("    <key>KeepAlive</key>");
            b.AppendLine("    <dict>");
            b.AppendLine("        <key>SuccessfulExit</key>");
            b.AppendLine("        <false/>");
            b.AppendLine("    </dict>");
            b.AppendLine("</dict>");
            b.AppendLine("</plist>");
            return b.ToString();
        }

        /// <summary>
        /// Builds the systemd <b>user</b> unit for the agent (per-user daemon; restarts on failure).
        /// </summary>
        /// <param name="executablePath">The absolute path to the agent executable.</param>
        /// <returns>The unit file content.</returns>
        public static string BuildSystemdUserUnit(string executablePath)
        {
            System.Text.StringBuilder b = new System.Text.StringBuilder();
            b.AppendLine("[Unit]");
            b.AppendLine("Description=mux tray agent (hosts the local REST server)");
            b.AppendLine();
            b.AppendLine("[Service]");
            b.AppendLine("ExecStart=" + executablePath);
            b.AppendLine("Restart=on-failure");
            b.AppendLine();
            b.AppendLine("[Install]");
            b.AppendLine("WantedBy=default.target");
            return b.ToString();
        }

        /// <summary>
        /// Builds the freedesktop autostart entry used when systemd <c>--user</c> is unavailable.
        /// </summary>
        /// <param name="executablePath">The absolute path to the agent executable.</param>
        /// <returns>The desktop entry content.</returns>
        public static string BuildAutostartDesktopEntry(string executablePath)
        {
            System.Text.StringBuilder b = new System.Text.StringBuilder();
            b.AppendLine("[Desktop Entry]");
            b.AppendLine("Type=Application");
            b.AppendLine("Name=mux Agent");
            b.AppendLine("Comment=mux tray agent (hosts the local REST server)");
            b.AppendLine("Exec=" + executablePath);
            b.AppendLine("X-GNOME-Autostart-enabled=true");
            return b.ToString();
        }

        private static string XmlEscape(string value)
        {
            return (value ?? string.Empty).Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
        }

        private static string GetUidBestEffort()
        {
            // launchctl addresses the GUI domain by numeric uid; obtain it from `id -u`.
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo("id", "-u")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using Process? p = Process.Start(psi);
                if (p != null)
                {
                    string output = p.StandardOutput.ReadToEnd().Trim();
                    p.WaitForExit(2000);
                    if (!string.IsNullOrEmpty(output)) return output;
                }
            }
            catch
            {
                // ignore
            }

            return "501";
        }

        private static bool RunQuiet(string executable, List<string> arguments)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = executable,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                foreach (string a in arguments) psi.ArgumentList.Add(a);

                using Process? p = Process.Start(psi);
                if (p == null) return false;
                p.WaitForExit(5000);
                return p.HasExited && p.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }
    }
}
