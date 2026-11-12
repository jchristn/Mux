namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Cli.Commands;
    using Mux.Core.Branding;
    using Mux.Core.Startup;
    using Touchstone.Core;

    /// <summary>
    /// Touchstone suite for starting the tray agent from every surface: where the agent is looked for (the install
    /// scripts' per-user and system-wide paths on each OS, preferring the copy from the same install), the
    /// MUX_AGENT_AUTOSTART switch, and the <c>mux agent</c> command the VS Code extension runs. Also covers the block
    /// geometry the About windows use to draw the wordmark without font gaps.
    /// </summary>
    public static class AgentStartupSuite
    {
        private const string SuiteId = "AgentStartup";

        /// <summary>
        /// Builds the agent-startup suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/>.</returns>
        public static TestSuiteDescriptor Create()
        {
            return new TestSuiteDescriptor(
                SuiteId,
                "Tray agent startup: install locations, autostart, mux agent, and the wordmark geometry",
                new List<TestCaseDescriptor>
                {
                    new TestCaseDescriptor(SuiteId, "InstalledLocationsPerOs", "Installed agent paths match the install scripts on macOS, Linux/WSL, and Windows, per-user first", (CancellationToken ct) =>
                    {
                        IReadOnlyList<string> mac = AgentLocator.InstalledLocations("macos", "/Users/u", _ => null);
                        MuxAssert.AreEqual("/Users/u/Applications/Mux Agent.app/Contents/MacOS/Mux.Agent", Unix(mac[0]), "macOS per-user");
                        MuxAssert.AreEqual("/Applications/Mux Agent.app/Contents/MacOS/Mux.Agent", Unix(mac[1]), "macOS system");

                        IReadOnlyList<string> linux = AgentLocator.InstalledLocations("linux", "/home/u", _ => null);
                        MuxAssert.AreEqual("/home/u/.local/share/mux/apps/agent/Mux.Agent", Unix(linux[0]), "Linux per-user");
                        MuxAssert.AreEqual("/opt/mux/agent/Mux.Agent", Unix(linux[1]), "Linux system");
                        IReadOnlyList<string> xdg = AgentLocator.InstalledLocations("linux", "/home/u", n => n == "XDG_DATA_HOME" ? "/data" : null);
                        MuxAssert.AreEqual("/data/mux/apps/agent/Mux.Agent", Unix(xdg[0]), "XDG_DATA_HOME honored");

                        Dictionary<string, string> env = new Dictionary<string, string> { ["LOCALAPPDATA"] = "C:/Users/u/AppData/Local", ["ProgramFiles"] = "C:/Program Files" };
                        IReadOnlyList<string> windows = AgentLocator.InstalledLocations("windows", "C:/Users/u", n => env.TryGetValue(n, out string? v) ? v : null);
                        MuxAssert.AreEqual("C:/Users/u/AppData/Local/Programs/Mux/Agent/Mux.Agent.exe", Unix(windows[0]), "Windows per-user");
                        MuxAssert.AreEqual("C:/Program Files/Mux/Agent/Mux.Agent.exe", Unix(windows[1]), "Windows system");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor(SuiteId, "PrefersTheSameInstall", "The agent from the same install as the running app comes first; otherwise per-user before system", (CancellationToken ct) =>
                    {
                        IReadOnlyList<string> mac = AgentLocator.InstalledLocations("macos", "/Users/u", _ => null);
                        MuxAssert.AreEqual(Unix(mac[1]), Unix(AgentLocator.PreferSameInstall(mac, "/Applications/Mux.app/Contents/MacOS/")[0]), "desktop in /Applications starts the /Applications agent");
                        MuxAssert.AreEqual(Unix(mac[0]), Unix(AgentLocator.PreferSameInstall(mac, "/Users/u/Applications/Mux.app/Contents/MacOS/")[0]), "desktop in ~/Applications starts the ~/Applications agent");
                        MuxAssert.AreEqual(Unix(mac[0]), Unix(AgentLocator.PreferSameInstall(mac, "/Users/u/.dotnet/tools/.store/mux.cli/")[0]), "the CLI prefers the per-user agent");

                        IReadOnlyList<string> linux = AgentLocator.InstalledLocations("linux", "/home/u", _ => null);
                        MuxAssert.AreEqual(Unix(linux[1]), Unix(AgentLocator.PreferSameInstall(linux, "/opt/mux/desktop/")[0]), "desktop in /opt starts the /opt agent");
                        MuxAssert.AreEqual(Unix(linux[0]), Unix(AgentLocator.PreferSameInstall(linux, null)[0]), "no base directory keeps the order");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor(SuiteId, "OverrideAndExecutableName", "MUX_AGENT names the executable or its folder", (CancellationToken ct) =>
                    {
                        string dir = Path.Combine(Path.GetTempPath(), "mux_agentloc_" + Guid.NewGuid().ToString("N"));
                        Directory.CreateDirectory(dir);
                        string? previous = Environment.GetEnvironmentVariable(AgentLocator.OverrideVariable);
                        try
                        {
                            string exe = Path.Combine(dir, AgentLocator.ExecutableName());
                            File.WriteAllText(exe, "fake");
                            Environment.SetEnvironmentVariable(AgentLocator.OverrideVariable, exe);
                            MuxAssert.AreEqual(exe, AgentLocator.Locate(), "a file path");
                            Environment.SetEnvironmentVariable(AgentLocator.OverrideVariable, dir);
                            MuxAssert.AreEqual(exe, AgentLocator.Locate(), "a folder");
                            MuxAssert.AreEqual(OperatingSystem.IsWindows() ? "Mux.Agent.exe" : "Mux.Agent", AgentLocator.ExecutableName(), "executable name");
                        }
                        finally
                        {
                            Environment.SetEnvironmentVariable(AgentLocator.OverrideVariable, previous);
                            try { Directory.Delete(dir, true); } catch (Exception) { }
                        }

                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor(SuiteId, "ReplaceOnlyWithCurrentCopy", "A running agent of another version is replaced only by a copy of this version (or an unmarked build), never by the same stale copy", (CancellationToken ct) =>
                    {
                        string current = Mux.Core.Settings.Defaults.ProductVersion;
                        MuxAssert.IsFalse(AgentLauncher.ShouldReplace(current, current), "running current: keep");
                        MuxAssert.IsFalse(AgentLauncher.ShouldReplace(null, current), "unknown running version: keep");
                        MuxAssert.IsTrue(AgentLauncher.ShouldReplace("0.0.1", current), "stale running, current copy: replace");
                        MuxAssert.IsTrue(AgentLauncher.ShouldReplace("0.0.1", null), "stale running, unmarked build: replace");
                        MuxAssert.IsFalse(AgentLauncher.ShouldReplace("0.0.1", "0.0.1"), "stale running, same stale copy: keep");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor(SuiteId, "InstalledVersionMarkers", "Installed versions are read from mux-version.txt or the macOS Info.plist", (CancellationToken ct) =>
                    {
                        string dir = Path.Combine(Path.GetTempPath(), "mux_agentver_" + Guid.NewGuid().ToString("N"));
                        try
                        {
                            string flat = Path.Combine(dir, "agent");
                            Directory.CreateDirectory(flat);
                            File.WriteAllText(Path.Combine(flat, "mux-version.txt"), "1.2.3\n");
                            MuxAssert.AreEqual("1.2.3", AgentLocator.InstalledVersion(Path.Combine(flat, "Mux.Agent")), "marker file");
                            string macos = Path.Combine(dir, "Mux Agent.app", "Contents", "MacOS");
                            Directory.CreateDirectory(macos);
                            File.WriteAllText(Path.Combine(dir, "Mux Agent.app", "Contents", "Info.plist"),
                                "<plist><dict><key>CFBundleVersion</key><string>9</string><key>CFBundleShortVersionString</key><string>4.5.6</string></dict></plist>");
                            MuxAssert.AreEqual("4.5.6", AgentLocator.InstalledVersion(Path.Combine(macos, "Mux.Agent")), "Info.plist");
                            MuxAssert.IsNull(AgentLocator.InstalledVersion(Path.Combine(dir, "none", "Mux.Agent")), "no marker");
                            MuxAssert.IsNull(AgentLocator.InstalledVersion(null), "null");
                        }
                        finally
                        {
                            try { Directory.Delete(dir, true); } catch (Exception) { }
                        }

                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor(SuiteId, "AutostartSwitch", "MUX_AGENT_AUTOSTART turns automatic starts off with 0, false, no, or off", (CancellationToken ct) =>
                    {
                        foreach (string? on in new string?[] { null, "", " ", "1", "true", "yes", "anything" })
                            MuxAssert.IsTrue(AgentLauncher.AutostartEnabled(on), "on: '" + on + "'");
                        foreach (string off in new[] { "0", "false", "FALSE", " no ", "Off" })
                            MuxAssert.IsFalse(AgentLauncher.AutostartEnabled(off), "off: '" + off + "'");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor(SuiteId, "AgentCommand", "mux agent prints usage for a bad verb, and describes each start result", (CancellationToken ct) =>
                    {
                        StringWriter output = new StringWriter(), error = new StringWriter();
                        MuxAssert.AreEqual(AgentCommand.UsageExitCode, AgentCommand.Run(Array.Empty<string>(), output, error), "no verb");
                        MuxAssert.Contains("Usage: mux agent <start|status>", error.ToString(), "usage");
                        MuxAssert.AreEqual(AgentCommand.UsageExitCode, AgentCommand.Run(new[] { "launch" }, output, error), "unknown verb");
                        MuxAssert.Contains("already running", AgentCommand.Describe(AgentStartResult.AlreadyRunning, null), "already running: the VS Code extension matches this text");
                        MuxAssert.Contains("Started the mux agent: /x/Mux.Agent", AgentCommand.Describe(AgentStartResult.Started, "/x/Mux.Agent"), "started");
                        MuxAssert.Contains("not installed", AgentCommand.Describe(AgentStartResult.NotFound, null), "not found");
                        MuxAssert.Contains(AgentLocator.OverrideVariable, AgentCommand.Describe(AgentStartResult.NotFound, null), "names the override");
                        MuxAssert.AreEqual(3, AgentCommand.NotFoundExitCode, "the VS Code extension treats non-zero as unavailable");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor(SuiteId, "WordmarkRectangles", "Block art becomes merged rectangles that cover exactly the filled half-cells", (CancellationToken ct) =>
                    {
                        (int columns, int halfRows) = BlockArt.Size(BlockArt.Wordmark);
                        MuxAssert.AreEqual(19, columns, "wordmark width");
                        MuxAssert.AreEqual(6, halfRows, "three rows are six half-rows");
                        var rectangles = BlockArt.Rectangles(BlockArt.Wordmark);
                        bool[,] covered = new bool[halfRows, columns];
                        foreach ((int x, int y, int w, int h) in rectangles)
                            for (int dy = 0; dy < h; dy++)
                                for (int dx = 0; dx < w; dx++)
                                {
                                    MuxAssert.IsFalse(covered[y + dy, x + dx], "no overlap at " + (x + dx) + "," + (y + dy));
                                    covered[y + dy, x + dx] = true;
                                }

                        for (int row = 0; row < BlockArt.Wordmark.Count; row++)
                        {
                            string line = BlockArt.Wordmark[row];
                            for (int x = 0; x < columns; x++)
                            {
                                char c = x < line.Length ? line[x] : ' ';
                                MuxAssert.AreEqual(c == '█' || c == '▀', covered[row * 2, x], "top half of '" + c + "' at " + x + "," + row);
                                MuxAssert.AreEqual(c == '█' || c == '▄', covered[(row * 2) + 1, x], "bottom half of '" + c + "' at " + x + "," + row);
                            }
                        }

                        // "██" over "██" is one 2x4 rectangle, not four squares with seams between them.
                        var block = BlockArt.Rectangles(new[] { "██", "██" });
                        MuxAssert.AreEqual(1, block.Count, "touching blocks merge");
                        MuxAssert.AreEqual((0, 0, 2, 4), block[0], "one rectangle");
                        MuxAssert.AreEqual(0, BlockArt.Rectangles(new[] { "  ", "ab" }).Count, "other characters are blank");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor(SuiteId, "WordmarkTerminalCells", "Terminal cells are the wordmark at twice its size, 38x6, filled exactly where its rectangles are", (CancellationToken ct) =>
                    {
                        bool[,] cells = BlockArt.TerminalCells(BlockArt.Wordmark);
                        MuxAssert.AreEqual(6, cells.GetLength(0), "one row per half-row");
                        MuxAssert.AreEqual(38, cells.GetLength(1), "two cells per column");
                        bool[,] covered = new bool[6, 38];
                        foreach ((int x, int y, int w, int h) in BlockArt.Rectangles(BlockArt.Wordmark))
                            for (int dy = 0; dy < h; dy++)
                                for (int dx = 0; dx < w * 2; dx++) covered[y + dy, (x * 2) + dx] = true;
                        for (int y = 0; y < 6; y++)
                            for (int x = 0; x < 38; x++) MuxAssert.AreEqual(covered[y, x], cells[y, x], "cell " + x + "," + y);
                        bool[,] half = BlockArt.HalfCells(new[] { "▀▄█ " });
                        MuxAssert.IsTrue(half[0, 0] && !half[1, 0] && !half[0, 1] && half[1, 1] && half[0, 2] && half[1, 2] && !half[0, 3] && !half[1, 3], "half-cells of each glyph");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor(SuiteId, "DesktopInstalledLocationsPerOs", "Installed desktop paths match the install scripts on macOS, Linux/WSL, and Windows, per-user first", (CancellationToken ct) =>
                    {
                        IReadOnlyList<string> mac = DesktopLocator.InstalledLocations("macos", "/Users/u", _ => null);
                        MuxAssert.AreEqual(2, mac.Count, "two mac paths");
                        MuxAssert.AreEqual("/Users/u/Applications/Mux.app/Contents/MacOS/Mux.Desktop", Unix(mac[0]), "mac per-user");
                        MuxAssert.AreEqual("/Applications/Mux.app/Contents/MacOS/Mux.Desktop", Unix(mac[1]), "mac system");
                        IReadOnlyList<string> linux = DesktopLocator.InstalledLocations("linux", "/home/u", _ => null);
                        MuxAssert.AreEqual("/home/u/.local/share/mux/apps/desktop/Mux.Desktop", Unix(linux[0]), "linux per-user");
                        MuxAssert.AreEqual("/opt/mux/desktop/Mux.Desktop", Unix(linux[1]), "linux system");
                        IReadOnlyList<string> xdg = DesktopLocator.InstalledLocations("linux", "/home/u", n => n == "XDG_DATA_HOME" ? "/data" : null);
                        MuxAssert.AreEqual("/data/mux/apps/desktop/Mux.Desktop", Unix(xdg[0]), "XDG_DATA_HOME");
                        Dictionary<string, string> env = new Dictionary<string, string> { ["LOCALAPPDATA"] = "C:/Users/u/AppData/Local", ["ProgramFiles"] = "C:/Program Files" };
                        IReadOnlyList<string> windows = DesktopLocator.InstalledLocations("windows", "C:/Users/u", n => env.TryGetValue(n, out string? v) ? v : null);
                        MuxAssert.AreEqual("C:/Users/u/AppData/Local/Programs/Mux/Desktop/Mux.Desktop.exe", Unix(windows[0]), "windows per-user");
                        MuxAssert.AreEqual("C:/Program Files/Mux/Desktop/Mux.Desktop.exe", Unix(windows[1]), "windows system");
                        MuxAssert.AreEqual(1, DesktopLocator.InstalledLocations("windows", "C:/Users/u", _ => null).Count, "no ProgramFiles, no system path");
                        MuxAssert.AreEqual("C:/Users/u/AppData/Local/Programs/Mux/Desktop/Mux.Desktop.exe", Unix(DesktopLocator.InstalledLocations("windows", "C:/Users/u", _ => null)[0]), "LOCALAPPDATA defaults under home");
                        MuxAssert.AreEqual(OperatingSystem.IsWindows() ? "Mux.Desktop.exe" : "Mux.Desktop", DesktopLocator.ExecutableName(), "executable name");
                        MuxAssert.AreEqual("/Users/u/Applications/Mux Agent.app/Contents/MacOS/Mux.Agent", Unix(AgentLocator.InstalledLocations("macos", "/Users/u", _ => null)[0]), "the agent's paths are unchanged by the shared locator");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor(SuiteId, "DesktopSearchOrder", "MUX_DESKTOP, then beside the agent, then the same install, then per-user, then system", (CancellationToken ct) =>
                    {
                        // macOS: the agent in /Applications/Mux Agent.app opens /Applications/Mux.app first.
                        string macAgent = "/Applications/Mux Agent.app/Contents/MacOS/";
                        List<string> mac = DesktopLocator.Candidates("macos", "/Users/u", _ => null, macAgent).Select(Unix).ToList();
                        MuxAssert.AreEqual(3, mac.Count, "beside plus two installs");
                        MuxAssert.AreEqual("/Applications/Mux Agent.app/Contents/MacOS/Mux.Desktop", mac[0], "beside the agent");
                        MuxAssert.AreEqual("/Applications/Mux.app/Contents/MacOS/Mux.Desktop", mac[1], "same install (system)");
                        MuxAssert.AreEqual("/Users/u/Applications/Mux.app/Contents/MacOS/Mux.Desktop", mac[2], "then per-user");
                        List<string> macUser = DesktopLocator.Candidates("macos", "/Users/u", _ => null, "/Users/u/Applications/Mux Agent.app/Contents/MacOS/").Select(Unix).ToList();
                        MuxAssert.AreEqual("/Users/u/Applications/Mux.app/Contents/MacOS/Mux.Desktop", macUser[1], "per-user agent opens the per-user desktop");
                        List<string> macCli = DesktopLocator.Candidates("macos", "/Users/u", _ => null, "/Users/u/.dotnet/tools/").Select(Unix).ToList();
                        MuxAssert.AreEqual("/Users/u/Applications/Mux.app/Contents/MacOS/Mux.Desktop", macCli[1], "no shared install: per-user first");
                        MuxAssert.AreEqual("/Applications/Mux.app/Contents/MacOS/Mux.Desktop", macCli[2], "then system");

                        // Linux and WSL: the /opt agent opens the /opt desktop first.
                        List<string> linux = DesktopLocator.Candidates("linux", "/home/u", _ => null, "/opt/mux/agent/").Select(Unix).ToList();
                        MuxAssert.AreEqual("/opt/mux/desktop/Mux.Desktop", linux[1], "same install (system)");
                        MuxAssert.AreEqual("/home/u/.local/share/mux/apps/desktop/Mux.Desktop", linux[2], "then per-user");
                        List<string> linuxUser = DesktopLocator.Candidates("linux", "/home/u", _ => null, "/home/u/.local/share/mux/apps/agent/").Select(Unix).ToList();
                        MuxAssert.AreEqual("/home/u/.local/share/mux/apps/desktop/Mux.Desktop", linuxUser[1], "per-user agent opens the per-user desktop");
                        MuxAssert.AreEqual("/opt/mux/desktop/Mux.Desktop", linuxUser[2], "then system");

                        // Windows: the Program Files agent opens the Program Files desktop first.
                        Dictionary<string, string> env = new Dictionary<string, string> { ["LOCALAPPDATA"] = "C:/Users/u/AppData/Local", ["ProgramFiles"] = "C:/Program Files" };
                        Func<string, string?> read = n => env.TryGetValue(n, out string? v) ? v : null;
                        List<string> windows = DesktopLocator.Candidates("windows", "C:/Users/u", read, "C:/Program Files/Mux/Agent/").Select(Unix).ToList();
                        MuxAssert.AreEqual("C:/Program Files/Mux/Agent/Mux.Desktop.exe", windows[0], "beside the agent");
                        MuxAssert.AreEqual("C:/Program Files/Mux/Desktop/Mux.Desktop.exe", windows[1], "same install (system)");
                        MuxAssert.AreEqual("C:/Users/u/AppData/Local/Programs/Mux/Desktop/Mux.Desktop.exe", windows[2], "then per-user");
                        List<string> windowsUser = DesktopLocator.Candidates("windows", "C:/Users/u", read, "C:/Users/u/AppData/Local/Programs/Mux/Agent/").Select(Unix).ToList();
                        MuxAssert.AreEqual("C:/Users/u/AppData/Local/Programs/Mux/Desktop/Mux.Desktop.exe", windowsUser[1], "per-user agent opens the per-user desktop");

                        // MUX_DESKTOP comes before everything, as a file and then as a folder.
                        List<string> overridden = DesktopLocator.Candidates("linux", "/home/u", n => n == DesktopLocator.OverrideVariable ? "/custom" : null, "/opt/mux/agent/").Select(Unix).ToList();
                        MuxAssert.AreEqual("/custom", overridden[0], "override as a file");
                        MuxAssert.AreEqual("/custom/Mux.Desktop", overridden[1], "override as a folder");
                        MuxAssert.AreEqual("/opt/mux/agent/Mux.Desktop", overridden[2], "then beside the agent");
                        MuxAssert.AreEqual(2, DesktopLocator.Candidates("linux", "/home/u", _ => null, null).Count, "no base directory: installs only");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor(SuiteId, "DesktopOverrideAndBundle", "MUX_DESKTOP finds the executable; a macOS copy is opened through its .app bundle", (CancellationToken ct) =>
                    {
                        string? previous = Environment.GetEnvironmentVariable(DesktopLocator.OverrideVariable);
                        string dir = Path.Combine(Path.GetTempPath(), "mux-desktop-" + Guid.NewGuid().ToString("N"));
                        try
                        {
                            Directory.CreateDirectory(dir);
                            string exe = Path.Combine(dir, DesktopLocator.ExecutableName());
                            File.WriteAllText(exe, string.Empty);
                            Environment.SetEnvironmentVariable(DesktopLocator.OverrideVariable, exe);
                            MuxAssert.AreEqual(exe, DesktopLocator.Locate(), "a file path");
                            Environment.SetEnvironmentVariable(DesktopLocator.OverrideVariable, dir);
                            MuxAssert.AreEqual(exe, DesktopLocator.Locate(), "a folder");
                        }
                        finally
                        {
                            Environment.SetEnvironmentVariable(DesktopLocator.OverrideVariable, previous);
                            try { Directory.Delete(dir, true); } catch (Exception) { }
                        }

                        MuxAssert.AreEqual("/Applications/Mux.app", DesktopLocator.AppBundle("/Applications/Mux.app/Contents/MacOS/Mux.Desktop"), "system bundle");
                        MuxAssert.AreEqual("/Users/u/Applications/Mux.app", DesktopLocator.AppBundle("/Users/u/Applications/Mux.app/Contents/MacOS/Mux.Desktop"), "per-user bundle");
                        MuxAssert.IsNull(DesktopLocator.AppBundle("/opt/mux/desktop/Mux.Desktop"), "not a bundle");
                        MuxAssert.IsNull(DesktopLocator.AppBundle(null), "null");
                        return Task.CompletedTask;
                    })
                });
        }

        private static string Unix(string path)
        {
            return path.Replace('\\', '/');
        }
    }
}
