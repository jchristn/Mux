namespace Test.Shared.Suites
{
    using System.Collections.Generic;
    using System.IO;
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
                    }),

                    new TestCaseDescriptor("StartupRegistrar", "IdentifierNames", "Registrations use the ai.latticetech.mux identifier and remember the 1.x names", (CancellationToken ct) =>
                    {
                        MuxAssert.AreEqual("ai.latticetech.mux.agent", StartupRegistrar.Label, "launchd label");
                        MuxAssert.AreEqual("com.jchristn.mux.agent", StartupRegistrar.LegacyLabel, "legacy launchd label");
                        MuxAssert.AreEqual("Mux Agent", StartupRegistrar.WindowsRunValueName, "Run value name");
                        MuxAssert.AreEqual("MuxAgent", StartupRegistrar.LegacyWindowsRunValueName, "legacy Run value name");
                        MuxAssert.AreEqual("ai.latticetech.mux.agent.service", StartupRegistrar.LinuxUnitFileName, "systemd unit");
                        MuxAssert.AreEqual("/etc/xdg/autostart/ai.latticetech.mux.agent.desktop", StartupRegistrar.LinuxSystemAutostartPath, "package autostart entry");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("StartupRegistrar", "MigratesLegacyLaunchAgent", "A 1.x LaunchAgent is replaced by one under the new label", (CancellationToken ct) =>
                    {
                        string home = TempHome();
                        try
                        {
                            string agents = Path.Combine(home, "Library", "LaunchAgents");
                            Directory.CreateDirectory(agents);
                            File.WriteAllText(Path.Combine(agents, StartupRegistrar.LegacyLabel + ".plist"), "old");
                            MuxAssert.IsTrue(StartupRegistrar.MigrateLegacyMac(home, Exe), "migrates");
                            MuxAssert.IsFalse(File.Exists(Path.Combine(agents, StartupRegistrar.LegacyLabel + ".plist")), "legacy plist removed");
                            string current = File.ReadAllText(Path.Combine(agents, StartupRegistrar.Label + ".plist"));
                            MuxAssert.Contains(StartupRegistrar.Label, current, "new plist uses the new label");
                            MuxAssert.Contains(Exe, current, "new plist runs the agent");
                            MuxAssert.IsFalse(StartupRegistrar.MigrateLegacyMac(home, Exe), "nothing left to migrate");
                        }
                        finally
                        {
                            Directory.Delete(home, true);
                        }

                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("StartupRegistrar", "MigratesLegacyLinuxEntries", "A 1.x systemd unit or autostart entry moves to the new names", (CancellationToken ct) =>
                    {
                        string home = TempHome();
                        try
                        {
                            string unitDir = Path.Combine(home, ".config", "systemd", "user");
                            string autostart = Path.Combine(home, ".config", "autostart");
                            MuxAssert.IsFalse(StartupRegistrar.MigrateLegacyLinux(home, Exe, false), "nothing to migrate");

                            Directory.CreateDirectory(autostart);
                            File.WriteAllText(Path.Combine(autostart, StartupRegistrar.LegacyLinuxAutostartFileName), "old");
                            MuxAssert.IsTrue(StartupRegistrar.MigrateLegacyLinux(home, Exe, false), "migrates the autostart entry");
                            MuxAssert.IsFalse(File.Exists(Path.Combine(autostart, StartupRegistrar.LegacyLinuxAutostartFileName)), "legacy entry removed");
                            bool packaged = File.Exists(StartupRegistrar.LinuxSystemAutostartPath);
                            MuxAssert.AreEqual(!packaged, File.Exists(Path.Combine(autostart, StartupRegistrar.LinuxAutostartFileName)), "new entry written unless the package starts the agent");

                            Directory.CreateDirectory(unitDir);
                            File.WriteAllText(Path.Combine(unitDir, StartupRegistrar.LegacyLinuxUnitFileName), "old");
                            MuxAssert.IsTrue(StartupRegistrar.MigrateLegacyLinux(home, Exe, false), "migrates the unit");
                            MuxAssert.IsFalse(File.Exists(Path.Combine(unitDir, StartupRegistrar.LegacyLinuxUnitFileName)), "legacy unit removed");
                            MuxAssert.AreEqual(!packaged, File.Exists(Path.Combine(unitDir, StartupRegistrar.LinuxUnitFileName)), "new unit written unless the package starts the agent");
                        }
                        finally
                        {
                            Directory.Delete(home, true);
                        }

                        return Task.CompletedTask;
                    })
                });
        }

        private static string TempHome()
        {
            string home = Path.Combine(Path.GetTempPath(), "mux-startup-" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(home);
            return home;
        }
    }
}
