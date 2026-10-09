namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Hosting;
    using Touchstone.Core;

    /// <summary>
    /// Touchstone suite for the tray agent's Launch Terminal: <see cref="MuxCliLocator"/> (override, PATH, .NET tools,
    /// beside the program, checkout builds, dll fallback, nothing found) and <see cref="TerminalLaunchPlanner"/> (the
    /// macOS <c>.command</c> script, the Windows <c>start</c> command, the Linux emulator list, and quoting), including
    /// running the generated macOS script with a stub CLI.
    /// </summary>
    public static class TerminalLaunchSuite
    {
        #region Private-Members

        private const string SuiteId = "TerminalLaunch";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Builds the terminal launch suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/> for the launch cases.</returns>
        public static TestSuiteDescriptor Create()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();
            void Add(string id, string name, Action<string> body)
            {
                cases.Add(new TestCaseDescriptor(SuiteId, id, name, (CancellationToken ct) => { WithTemp(body); return Task.CompletedTask; }));
            }

            Add("OverrideFileAndDirectory", "MUX_CLI wins as a file or a directory, and a dll runs through dotnet", (string dir) =>
            {
                string exe = Touch(dir, "custom/mux");
                MuxCliLocation file = MuxCliLocator.Locate(exe, null, null, Array.Empty<string>(), false)!;
                MuxAssert.AreEqual(exe, file.Executable, "file override");
                MuxAssert.AreEqual("override", file.Source, "source");
                MuxCliLocation folder = MuxCliLocator.Locate(Path.Combine(dir, "custom"), null, null, Array.Empty<string>(), false)!;
                MuxAssert.AreEqual(exe, folder.Executable, "directory override");
                string dll = Touch(dir, "dllonly/Mux.Cli.dll");
                MuxCliLocation viaDotnet = MuxCliLocator.Locate(Path.Combine(dir, "dllonly"), null, null, Array.Empty<string>(), false)!;
                MuxAssert.AreEqual("dotnet", viaDotnet.Executable, "dll runs through dotnet");
                MuxAssert.AreEqual(dll, viaDotnet.LeadingArguments[0], "dll path passed");
            });
            Add("OverrideMissingFallsThrough", "A missing MUX_CLI is ignored and the search continues", (string dir) =>
            {
                string onPath = Touch(dir, "bin/mux");
                MuxCliLocation found = MuxCliLocator.Locate(Path.Combine(dir, "nope"), Path.Combine(dir, "bin"), null, Array.Empty<string>(), false)!;
                MuxAssert.AreEqual(onPath, found.Executable, "PATH used");
                MuxAssert.AreEqual("path", found.Source, "source");
            });
            Add("PathThenDotnetTools", "PATH comes before ~/.dotnet/tools, which comes before checkouts", (string dir) =>
            {
                string tool = Touch(dir, "home/.dotnet/tools/mux");
                Touch(dir, "repo/src/Mux.Cli/bin/Debug/net10.0/Mux.Cli");
                MuxCliLocation tools = MuxCliLocator.Locate(null, Path.Combine(dir, "empty"), Path.Combine(dir, "home"), new[] { Path.Combine(dir, "repo") }, false)!;
                MuxAssert.AreEqual(tool, tools.Executable, "dotnet tools");
                MuxAssert.AreEqual("dotnet-tools", tools.Source, "source");
                string onPath = Touch(dir, "pathdir/mux");
                MuxAssert.AreEqual(onPath, MuxCliLocator.Locate(null, "  " + Path.PathSeparator + Path.Combine(dir, "pathdir"), Path.Combine(dir, "home"), Array.Empty<string>(), false)!.Executable, "PATH first, blank entries skipped");
            });
            Add("BesideAndCheckout", "A copy beside the program beats a checkout build, and the newest checkout build wins", (string dir) =>
            {
                string agentBin = Path.Combine(dir, "repo", "src", "Mux.Agent", "bin", "Debug", "net10.0");
                Directory.CreateDirectory(agentBin);
                string older = Touch(dir, "repo/src/Mux.Cli/bin/Debug/net8.0/Mux.Cli");
                string newer = Touch(dir, "repo/src/Mux.Cli/bin/Debug/net10.0/Mux.Cli");
                File.SetLastWriteTimeUtc(older, DateTime.UtcNow.AddHours(-2));
                File.SetLastWriteTimeUtc(newer, DateTime.UtcNow);
                MuxCliLocation checkout = MuxCliLocator.Locate(null, null, null, new[] { agentBin }, false)!;
                MuxAssert.AreEqual(newer, checkout.Executable, "newest build found by walking up");
                MuxAssert.AreEqual("checkout", checkout.Source, "source");
                string beside = Touch(dir, "repo/src/Mux.Agent/bin/Debug/net10.0/Mux.Cli");
                MuxCliLocation besideFound = MuxCliLocator.Locate(null, null, null, new[] { agentBin }, false)!;
                MuxAssert.AreEqual(beside, besideFound.Executable, "beside the program first");
                MuxAssert.AreEqual("beside", besideFound.Source, "source");
            });
            Add("WindowsNamesAndNothingFound", "Windows looks for .exe names; with nothing anywhere the result is null", (string dir) =>
            {
                Touch(dir, "win/mux");
                MuxAssert.IsNull(MuxCliLocator.Locate(null, Path.Combine(dir, "win"), null, Array.Empty<string>(), true), "a unix name does not count on Windows");
                string exe = Touch(dir, "win/mux.exe");
                MuxAssert.AreEqual(exe, MuxCliLocator.Locate(null, Path.Combine(dir, "win"), null, Array.Empty<string>(), true)!.Executable, "exe found");
                MuxAssert.IsNull(MuxCliLocator.Locate(null, null, null, new string?[] { null, Path.Combine(dir, "missing") }, false), "nothing found");
                Directory.CreateDirectory(Path.Combine(dir, "unbuilt", "src", "Mux.Cli"));
                MuxAssert.IsNull(MuxCliLocator.Locate(null, null, null, new[] { Path.Combine(dir, "unbuilt") }, false), "an unbuilt checkout finds nothing");
            });
            Add("MacScriptRunsTheCli", "The macOS script uses a login shell, removes itself, quotes paths, and starts the located CLI", (string dir) =>
            {
                string script = TerminalLaunchPlanner.BuildMacCommandFile(new MuxCliLocation { Executable = "/Apps/My Tools/mux", Source = "path" }, "/Users/someone/it's here");
                MuxAssert.IsTrue(script.StartsWith("#!/bin/zsh -l\n", StringComparison.Ordinal), "login shell");
                MuxAssert.Contains("rm -f \"$0\"", script, "removes itself");
                MuxAssert.Contains("cd '/Users/someone/it'\\''s here'", script, "working directory quoted");
                MuxAssert.Contains("'/Apps/My Tools/mux'\n", script, "CLI quoted");
                MuxAssert.Contains("exec \"${SHELL:-/bin/zsh}\" -l", script, "window stays open afterwards");
                string dotnet = TerminalLaunchPlanner.BuildMacCommandFile(new MuxCliLocation { Executable = "dotnet", LeadingArguments = new List<string> { "/x/Mux.Cli.dll" } }, "/tmp");
                MuxAssert.Contains("'dotnet' '/x/Mux.Cli.dll'", dotnet, "dll through dotnet");
                string missing = TerminalLaunchPlanner.BuildMacCommandFile(null, "/tmp");
                MuxAssert.Contains("command -v mux", missing, "falls back to the shell PATH");
                MuxAssert.Contains("mux was not found", missing, "explains when missing");
            });
            cases.Add(new TestCaseDescriptor(SuiteId, "MacScriptExecutes", "The generated script really runs the CLI from a login shell and cleans itself up", (CancellationToken ct) =>
            {
                WithTemp((string dir) =>
                {
                    string marker = Path.Combine(dir, "ran.txt");
                    string stub = Path.Combine(dir, "stub cli");
                    File.WriteAllText(stub, "#!/bin/sh\npwd > '" + marker + "'\n");
                    File.SetUnixFileMode(stub, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                    string workDir = Path.Combine(dir, "work dir");
                    Directory.CreateDirectory(workDir);
                    string text = TerminalLaunchPlanner.BuildMacCommandFile(new MuxCliLocation { Executable = stub }, workDir)
                        .Replace("exec \"${SHELL:-/bin/zsh}\" -l\n", string.Empty)
                        .Replace("clear\n", string.Empty);
                    string script = Path.Combine(dir, "launch.command");
                    File.WriteAllText(script, text);
                    File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                    using (Process process = Process.Start(new ProcessStartInfo { FileName = "/bin/zsh", ArgumentList = { script }, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false })!)
                    {
                        process.WaitForExit(20000);
                        MuxAssert.AreEqual(0, process.ExitCode, "script succeeded: " + process.StandardError.ReadToEnd());
                    }

                    MuxAssert.IsTrue(File.Exists(marker), "the CLI ran");
                    MuxAssert.AreEqual(Path.GetFullPath(workDir).TrimEnd('/'), File.ReadAllText(marker).Trim().Replace("/private/var/", "/var/"), "started in the working directory");
                    MuxAssert.IsFalse(File.Exists(script), "the script removed itself");
                });
                return Task.CompletedTask;
            }, skip: !File.Exists("/bin/zsh") || OperatingSystem.IsWindows(), skipReason: "zsh is not available"));
            Add("WindowsCommand", "Windows opens a titled console in the working directory that stays open", (string dir) =>
            {
                TerminalCommand command = TerminalLaunchPlanner.BuildWindowsCommand(new MuxCliLocation { Executable = @"C:\Program Files\mux\mux.exe" }, @"C:\Users\me");
                MuxAssert.AreEqual("cmd.exe", command.FileName, "cmd");
                MuxAssert.AreEqual("/c start \"mux\" /d \"C:\\Users\\me\" cmd.exe /k \"C:\\Program Files\\mux\\mux.exe\"", command.RawArguments, "quoted title, directory, and path");
                TerminalCommand fallback = TerminalLaunchPlanner.BuildWindowsCommand(null, @"C:\Users\me");
                MuxAssert.Contains("cmd.exe /k mux || echo mux was not found", fallback.RawArguments ?? string.Empty, "PATH fallback with guidance");
            });
            Add("LinuxCommands", "Linux tries several emulators, each running a login shell that starts mux and stays open", (string dir) =>
            {
                IReadOnlyList<TerminalCommand> commands = TerminalLaunchPlanner.BuildLinuxCommands(new MuxCliLocation { Executable = "/opt/mux/mux" }, "/home/me");
                List<string> names = new List<string>();
                foreach (TerminalCommand command in commands) names.Add(command.FileName);
                MuxAssert.AreEqual("x-terminal-emulator,gnome-terminal,konsole,xfce4-terminal,kitty,alacritty,xterm", string.Join(",", names), "emulators in order");
                MuxAssert.AreEqual("--", commands[1].Arguments[0], "gnome-terminal uses --");
                MuxAssert.AreEqual("-e", commands[0].Arguments[0], "x-terminal-emulator uses -e");
                string run = commands[0].Arguments[commands[0].Arguments.Count - 1];
                MuxAssert.Contains("cd '/home/me'", run, "working directory");
                MuxAssert.Contains("'/opt/mux/mux';", run, "CLI started");
                MuxAssert.Contains("exec \"${SHELL:-/bin/sh}\" -l", run, "stays open");
                MuxAssert.Contains("mux was not found", TerminalLaunchPlanner.BuildLinuxCommands(null, "/home/me")[0].Arguments[3], "guidance when missing");
                MuxAssert.AreEqual("'a'\\''b'", TerminalLaunchPlanner.QuotePosix("a'b"), "single quotes escaped");
                MuxAssert.IsNull(TerminalLaunchPlanner.PosixCommandLine(new MuxCliLocation()), "blank location has no command line");
            });

            return new TestSuiteDescriptor(SuiteId, "Tray agent Launch Terminal: CLI location and terminal commands", cases);
        }

        #endregion

        #region Private-Methods

        private static string Touch(string root, string relative)
        {
            string path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "x");
            return path;
        }

        private static void WithTemp(Action<string> body)
        {
            string root = Path.Combine(Path.GetTempPath(), "mux-term-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                body(Path.GetFullPath(root));
            }
            finally
            {
                try { Directory.Delete(root, true); } catch (Exception) { }
            }
        }

        #endregion
    }
}
