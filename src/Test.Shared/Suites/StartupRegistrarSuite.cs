namespace Test.Shared.Suites
{
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Startup;
    using Touchstone.Core;

    /// <summary>
    /// Touchstone suite for <see cref="StartupRegistrar"/>: the pure login-startup recipe builders emit the
    /// right launchd plist, systemd user unit, autostart entry, and Windows Run-key command for a given
    /// executable path. (The IO install path is platform-specific and best-effort, so only the pure
    /// builders are asserted here.)
    /// </summary>
    public static class StartupRegistrarSuite
    {
        private const string Exe = "/opt/mux/Mux.Agent";

        /// <summary>Builds the startup-registrar suite descriptor.</summary>
        /// <returns>The suite descriptor.</returns>
        public static TestSuiteDescriptor Create()
        {
            return new TestSuiteDescriptor(
                "StartupRegistrar",
                "First-run login-startup recipe builders",
                new List<TestCaseDescriptor>
                {
                    new TestCaseDescriptor("StartupRegistrar", "LaunchAgentPlist", "The launchd plist runs at load and references the exe", (CancellationToken ct) =>
                    {
                        string plist = StartupRegistrar.BuildLaunchAgentPlist(Exe);
                        MuxAssert.Contains("<key>Label</key>", plist, "has label key");
                        MuxAssert.Contains(StartupRegistrar.Label, plist, "uses the agent label");
                        MuxAssert.Contains("<key>RunAtLoad</key>", plist, "runs at load");
                        MuxAssert.Contains(Exe, plist, "references the exe");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("StartupRegistrar", "SystemdUserUnit", "The systemd unit is a user unit that restarts on failure", (CancellationToken ct) =>
                    {
                        string unit = StartupRegistrar.BuildSystemdUserUnit(Exe);
                        MuxAssert.Contains("[Service]", unit, "has service section");
                        MuxAssert.Contains("ExecStart=" + Exe, unit, "execs the agent");
                        MuxAssert.Contains("Restart=on-failure", unit, "restarts on failure");
                        MuxAssert.Contains("WantedBy=default.target", unit, "user target");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("StartupRegistrar", "AutostartDesktopEntry", "The autostart entry launches the agent", (CancellationToken ct) =>
                    {
                        string desktop = StartupRegistrar.BuildAutostartDesktopEntry(Exe);
                        MuxAssert.Contains("[Desktop Entry]", desktop, "desktop entry");
                        MuxAssert.Contains("Exec=" + Exe, desktop, "exec line");
                        MuxAssert.Contains("X-GNOME-Autostart-enabled=true", desktop, "autostart enabled");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("StartupRegistrar", "WindowsRegArgs", "The reg args write the HKCU Run value with a quoted path", (CancellationToken ct) =>
                    {
                        string exe = "C:\\Program Files\\mux\\Mux.Agent.exe";
                        List<string> args = StartupRegistrar.BuildWindowsRegArgs(exe);
                        string joined = string.Join(" ", args);
                        MuxAssert.Contains("HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Run", joined, "targets the Run key");
                        MuxAssert.Contains(StartupRegistrar.WindowsRunValueName, joined, "uses the value name");
                        MuxAssert.Contains("\"" + exe + "\"", joined, "quotes the exe path");
                        return Task.CompletedTask;
                    })
                });
        }
    }
}
