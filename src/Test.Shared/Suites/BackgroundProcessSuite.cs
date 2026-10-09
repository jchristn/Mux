namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Runtime.CompilerServices;
    using System.Text.Json;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Cli.App;
    using Mux.Core.Agent;
    using Mux.Core.Enums;
    using Mux.Core.Jobs;
    using Mux.Core.Models;
    using Mux.Core.Processes;
    using Mux.Core.Skills;
    using Mux.Core.Tools;
    using Test.Shared.Support;
    using Touchstone.Core;
    using TUIKit.Terminal;

    /// <summary>
    /// Touchstone suite for background processes (Phase 5.2): the <see cref="BackgroundProcessRegistry"/> with real
    /// processes (start, read only new output, wait for a pattern, time out, exit codes, output cap, concurrency limit,
    /// stop, dispose kills everything), the four process tools, the settings, the sidebar line, the terminal
    /// <c>/processes</c> command, <c>mux print</c> killing its processes at exit, and the <c>react-dev-server</c>
    /// skill's detection. Positive and negative cases throughout. Process cases use pwsh and are skipped without it.
    /// </summary>
    public static class BackgroundProcessSuite
    {
        #region Private-Members

        private const string SuiteId = "BackgroundProcesses";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Builds the background process suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/> for the background process cases.</returns>
        public static TestSuiteDescriptor Create()
        {
            bool pwsh = IsOnPath("pwsh");
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();
            void Add(string id, string name, Func<CancellationToken, Task> body)
            {
                cases.Add(new TestCaseDescriptor(SuiteId, id, name, body));
            }

            void AddProcess(string id, string name, Func<string, CancellationToken, Task> body)
            {
                cases.Add(new TestCaseDescriptor(SuiteId, id, name, (CancellationToken ct) => WithTempAsync((string dir) => body(dir, ct)), skip: !pwsh, skipReason: "pwsh is not on PATH"));
            }

            // --- registry ---
            AddProcess("StartWaitReadStop", "A process starts, a pattern wait returns the ready line, reads return only new output, and stop kills it", async (string dir, CancellationToken ct) =>
            {
                using (BackgroundProcessRegistry registry = new BackgroundProcessRegistry())
                {
                    BackgroundProcessInfo started = registry.Start(Ps("Write-Output 'booting'; Start-Sleep -Milliseconds 300; Write-Output 'READY on http://localhost:5173'; Start-Sleep -Seconds 60"), dir, "web");
                    MuxAssert.AreEqual("p1", started.Id, "first id");
                    MuxAssert.AreEqual("web", started.Name, "name kept");
                    MuxAssert.IsTrue(started.Running && started.ProcessId > 0, "running with a pid");
                    BackgroundProcessOutput ready = await registry.ReadAsync("P1", @"READY on (\S+)", 30000, ct).ConfigureAwait(false);
                    MuxAssert.IsTrue(ready.Matched == true, "matched: " + ready.Text);
                    MuxAssert.Contains("http://localhost:5173", ready.MatchText ?? string.Empty, "match text");
                    MuxAssert.Contains("booting", ready.Text, "earlier output included");
                    MuxAssert.IsTrue(ready.Running && !ready.TimedOut, "still running, no timeout");
                    BackgroundProcessOutput again = await registry.ReadAsync("p1", null, 0, ct).ConfigureAwait(false);
                    MuxAssert.AreEqual(string.Empty, again.Text, "nothing new since the last read");
                    MuxAssert.IsNull(again.Matched, "no pattern, no match flag");
                    MuxAssert.IsTrue(registry.HasRunning(), "running");
                    BackgroundProcessOutput stopped = await registry.StopAsync("p1", ct).ConfigureAwait(false);
                    MuxAssert.IsFalse(stopped.Running, "stopped");
                    BackgroundProcessInfo after = registry.Get("p1")!;
                    MuxAssert.IsTrue(after.StoppedByUser, "marked as stopped by mux");
                    MuxAssert.IsFalse(registry.HasRunning(), "nothing running");
                    MuxAssert.IsFalse(IsAlive(started.ProcessId), "the process is gone");
                }
            });
            AddProcess("ExitReportsCodeAndOutput", "A process that exits ends a pattern wait early with its exit code and output", async (string dir, CancellationToken ct) =>
            {
                using (BackgroundProcessRegistry registry = new BackgroundProcessRegistry())
                {
                    registry.Start(Ps("Write-Output 'compile error in App.tsx'; exit 3"), dir);
                    BackgroundProcessOutput output = await registry.ReadAsync("p1", "never-appears", 30000, ct).ConfigureAwait(false);
                    MuxAssert.IsTrue(output.Matched == false, "no match");
                    MuxAssert.IsFalse(output.Running, "exited");
                    MuxAssert.AreEqual(3, output.ExitCode ?? -1, "exit code");
                    MuxAssert.IsFalse(output.TimedOut, "ended by exit, not timeout");
                    MuxAssert.Contains("compile error in App.tsx", output.Text, "output kept");
                    BackgroundProcessOutput stop = await registry.StopAsync("p1", ct).ConfigureAwait(false);
                    MuxAssert.AreEqual(3, stop.ExitCode ?? -1, "stopping an exited process keeps its code");
                    MuxAssert.IsFalse(registry.Get("p1")!.StoppedByUser, "not marked as stopped by mux");
                }
            });
            AddProcess("WaitTimesOut", "A pattern that never appears times out and leaves the process running", async (string dir, CancellationToken ct) =>
            {
                using (BackgroundProcessRegistry registry = new BackgroundProcessRegistry())
                {
                    registry.Start(Ps("Write-Output 'starting'; Start-Sleep -Seconds 60"), dir);
                    Stopwatch watch = Stopwatch.StartNew();
                    BackgroundProcessOutput output = await registry.ReadAsync("p1", "never-appears", 800, ct).ConfigureAwait(false);
                    MuxAssert.IsTrue(output.TimedOut, "timed out");
                    MuxAssert.IsTrue(output.Matched == false, "no match");
                    MuxAssert.IsTrue(output.Running, "still running");
                    MuxAssert.IsTrue(watch.ElapsedMilliseconds >= 700, "waited for the timeout");
                }
            });
            AddProcess("NoPatternWaitsForFirstOutput", "Without a pattern, a read with a timeout waits only until new output arrives", async (string dir, CancellationToken ct) =>
            {
                using (BackgroundProcessRegistry registry = new BackgroundProcessRegistry())
                {
                    registry.Start(Ps("Start-Sleep -Milliseconds 400; Write-Output 'late line'; Start-Sleep -Seconds 60"), dir);
                    BackgroundProcessOutput output = await registry.ReadAsync("p1", null, 30000, ct).ConfigureAwait(false);
                    MuxAssert.Contains("late line", output.Text, "waited for output");
                    MuxAssert.IsFalse(output.TimedOut, "not a timeout");
                }
            });
            AddProcess("OutputCapKeepsNewest", "Output beyond the buffer is dropped from the oldest end and reported", async (string dir, CancellationToken ct) =>
            {
                using (BackgroundProcessRegistry registry = new BackgroundProcessRegistry(8, BackgroundProcessRegistry.MinOutputCapacity))
                {
                    registry.Start(Ps("1..1000 | ForEach-Object { 'line ' + $_.ToString('0000') + ' ' + ('x' * 80) }; Write-Output 'LAST LINE'"), dir);
                    BackgroundProcessOutput output = await registry.ReadAsync("p1", "never-appears", 60000, ct).ConfigureAwait(false);
                    MuxAssert.IsFalse(output.Running, "finished");
                    MuxAssert.IsTrue(output.DroppedChars > 0, "dropped count reported: " + output.DroppedChars);
                    MuxAssert.IsTrue(output.Text.Length <= BackgroundProcessRegistry.MinOutputCapacity, "buffer bounded");
                    MuxAssert.Contains("LAST LINE", output.Text, "newest kept");
                    MuxAssert.DoesNotContain("line 0001 ", output.Text, "oldest dropped");
                    MuxAssert.IsTrue(registry.Get("p1")!.OutputChars > BackgroundProcessRegistry.MinOutputCapacity, "total counts everything");
                }
            });
            AddProcess("ConcurrencyLimit", "Starting beyond the limit is refused until a process stops", async (string dir, CancellationToken ct) =>
            {
                using (BackgroundProcessRegistry registry = new BackgroundProcessRegistry(1, BackgroundProcessRegistry.DefaultOutputCapacity))
                {
                    registry.Start(Ps("Start-Sleep -Seconds 60"), dir);
                    InvalidOperationException refused = MuxAssert.Throws<InvalidOperationException>(() => registry.Start(Ps("Start-Sleep -Seconds 60"), dir), "second start refused");
                    MuxAssert.Contains("the limit is 1", refused.Message, "limit named");
                    MuxAssert.Contains("backgroundProcessMaxConcurrent", refused.Message, "setting named");
                    await registry.StopAsync("p1", ct).ConfigureAwait(false);
                    MuxAssert.AreEqual("p2", registry.Start(Ps("Start-Sleep -Seconds 60"), dir).Id, "allowed after a stop");
                }
            });
            AddProcess("DisposeKillsEverything", "Disposing the registry kills every process tree and refuses new starts", async (string dir, CancellationToken ct) =>
            {
                BackgroundProcessRegistry registry = new BackgroundProcessRegistry();
                BackgroundProcessInfo a = registry.Start(Ps("Start-Sleep -Seconds 60"), dir);
                BackgroundProcessInfo b = registry.Start(Ps("Start-Sleep -Seconds 60"), dir);
                await Task.Delay(200, ct).ConfigureAwait(false);
                registry.Dispose();
                MuxAssert.IsFalse(registry.HasRunning(), "nothing running");
                MuxAssert.IsFalse(IsAlive(a.ProcessId) || IsAlive(b.ProcessId), "both processes are gone");
                MuxAssert.Throws<InvalidOperationException>(() => registry.Start(Ps("exit 0"), dir), "no starts after dispose");
                registry.Dispose();
            });
            AddProcess("ListTailClearAndAnsi", "List keeps start order, tail does not consume, clear removes exited processes, and ANSI codes are stripped", async (string dir, CancellationToken ct) =>
            {
                using (BackgroundProcessRegistry registry = new BackgroundProcessRegistry())
                {
                    int changes = 0;
                    registry.Changed += (object? sender, EventArgs e) => Interlocked.Increment(ref changes);
                    registry.Start(Ps("Write-Output \"$([char]27)[32mGREEN$([char]27)[0m done\"; exit 0"), dir);
                    registry.Start(Ps("Start-Sleep -Seconds 60"), dir);
                    await registry.ReadAsync("p1", "never-appears", 30000, ct).ConfigureAwait(false);
                    IReadOnlyList<BackgroundProcessInfo> list = registry.List();
                    MuxAssert.AreEqual("p1", list[0].Id, "oldest first");
                    MuxAssert.AreEqual("p2", list[1].Id, "then the next");
                    MuxAssert.IsTrue(changes >= 3, "changed on two starts and an exit: " + changes);
                    MuxAssert.IsNull(registry.Tail("p9", 100), "unknown id has no tail");
                    MuxAssert.AreEqual(1, registry.RemoveExited(), "one exited process removed");
                    MuxAssert.AreEqual(1, registry.List().Count, "the running one stays");
                    MuxAssert.AreEqual(0, registry.RemoveExited(), "nothing more to remove");

                    registry.Start(Ps("Write-Output \"$([char]27)[1mbold$([char]27)[0m\"; Start-Sleep -Seconds 60"), dir);
                    await registry.ReadAsync("p3", "bold", 30000, ct).ConfigureAwait(false);
                    string tail = registry.Tail("p3", 1000)!;
                    MuxAssert.Contains("bold", tail, "tail shows output");
                    MuxAssert.DoesNotContain("\u001b", tail, "ANSI codes stripped");
                    MuxAssert.AreEqual(0L, registry.Get("p3")!.UnreadChars, "the read consumed it");
                }
            });
            AddProcess("RegistryRejectsBadInput", "Blank commands, missing directories, unknown ids, and invalid patterns are rejected", async (string dir, CancellationToken ct) =>
            {
                using (BackgroundProcessRegistry registry = new BackgroundProcessRegistry())
                {
                    MuxAssert.Throws<ArgumentException>(() => registry.Start("  ", dir), "blank command");
                    MuxAssert.Throws<ArgumentException>(() => registry.Start("echo hi", Path.Combine(dir, "missing")), "missing directory");
                    await MuxAssert.ThrowsAsync<KeyNotFoundException>(() => registry.ReadAsync("p7", null, 0, ct), "unknown id read").ConfigureAwait(false);
                    await MuxAssert.ThrowsAsync<KeyNotFoundException>(() => registry.StopAsync("p7", ct), "unknown id stop").ConfigureAwait(false);
                    registry.Start(Ps("Start-Sleep -Seconds 60"), dir);
                    await MuxAssert.ThrowsAsync<ArgumentException>(() => registry.ReadAsync("p1", "([unclosed", 100, ct), "invalid pattern").ConfigureAwait(false);
                    MuxAssert.IsNull(registry.Get("nope"), "unknown id");
                }
            });
            Add("RegistryConfigureClamps", "Registry limits are clamped", (CancellationToken ct) =>
            {
                using (BackgroundProcessRegistry registry = new BackgroundProcessRegistry(0, 10))
                {
                    MuxAssert.AreEqual(1, registry.MaxConcurrent, "at least one");
                    MuxAssert.AreEqual(BackgroundProcessRegistry.MinOutputCapacity, registry.OutputCapacityChars, "minimum buffer");
                    registry.Configure(999, int.MaxValue);
                    MuxAssert.AreEqual(BackgroundProcessRegistry.MaxConcurrentLimit, registry.MaxConcurrent, "at most 64");
                    MuxAssert.AreEqual(BackgroundProcessRegistry.MaxOutputCapacity, registry.OutputCapacityChars, "maximum buffer");
                }

                return Task.CompletedTask;
            });

            // --- tools ---
            Add("ToolDefinitionsAndKinds", "The four process tools are defined, with start and stop mutating and output and list read-only", (CancellationToken ct) =>
            {
                using (BackgroundProcessRegistry registry = new BackgroundProcessRegistry())
                {
                    BackgroundProcessToolProvider provider = new BackgroundProcessToolProvider(registry);
                    List<string> names = new List<ToolDefinition>(provider.GetToolDefinitions()).ConvertAll(d => d.Name);
                    MuxAssert.AreEqual("process_start,process_output,process_list,process_stop", string.Join(",", names), "names");
                    MuxAssert.AreEqual(ToolMutationKind.Mutating, provider.GetMutationKind("process_start"), "start mutates");
                    MuxAssert.AreEqual(ToolMutationKind.Mutating, provider.GetMutationKind("PROCESS_STOP"), "stop mutates");
                    MuxAssert.AreEqual(ToolMutationKind.ReadOnly, provider.GetMutationKind("process_output"), "output reads");
                    MuxAssert.AreEqual(ToolMutationKind.ReadOnly, provider.GetMutationKind("process_list"), "list reads");
                    MuxAssert.IsTrue(provider.HasTool("process_list") && !provider.HasTool("run_process"), "claims only its tools");
                    MuxAssert.AreEqual("processes", provider.Name, "provider name");
                    MuxAssert.IsTrue(ReferenceEquals(registry, provider.Registry), "registry exposed");
                    MuxAssert.Throws<ArgumentNullException>(() => new BackgroundProcessToolProvider(null!), "null registry");
                }

                return Task.CompletedTask;
            });
            AddProcess("ToolsDriveAProcess", "process_start waits for readiness, process_output and process_list report, and process_stop ends it", async (string dir, CancellationToken ct) =>
            {
                Directory.CreateDirectory(Path.Combine(dir, "app"));
                using (BackgroundProcessRegistry registry = new BackgroundProcessRegistry())
                {
                    BackgroundProcessToolProvider provider = new BackgroundProcessToolProvider(registry);
                    string command = Ps("Write-Output (Get-Location).Path; Write-Output 'Local: http://localhost:4321/'; Start-Sleep -Seconds 60");
                    ToolResult start = await provider.ExecuteAsync("process_start", Json(new { command, working_directory = "app", name = "web", wait_for = @"Local:\s+(\S+)", timeout_ms = 30000 }), dir, ct).ConfigureAwait(false);
                    MuxAssert.IsTrue(start.Success, "start succeeded: " + start.Content);
                    using (JsonDocument doc = JsonDocument.Parse(start.Content))
                    {
                        MuxAssert.AreEqual("p1", doc.RootElement.GetProperty("id").GetString(), "id");
                        MuxAssert.IsTrue(doc.RootElement.GetProperty("ready").GetBoolean(), "ready");
                        MuxAssert.Contains("http://localhost:4321/", doc.RootElement.GetProperty("match").GetString() ?? string.Empty, "match");
                        MuxAssert.Contains(Path.Combine(dir, "app"), doc.RootElement.GetProperty("output").GetString() ?? string.Empty, "relative directory resolved");
                    }

                    ToolResult output = await provider.ExecuteAsync("process_output", Json(new { id = "p1" }), dir, ct).ConfigureAwait(false);
                    MuxAssert.Contains("\"running\":true", output.Content, "output reports running");
                    MuxAssert.Contains("\"output\":\"\"", output.Content, "nothing new");
                    ToolResult list = await provider.ExecuteAsync("process_list", Json(new { }), dir, ct).ConfigureAwait(false);
                    MuxAssert.Contains("\"id\":\"p1\"", list.Content, "listed");
                    MuxAssert.Contains("\"name\":\"web\"", list.Content, "name listed");
                    ToolResult stop = await provider.ExecuteAsync("process_stop", Json(new { id = "p1" }), dir, ct).ConfigureAwait(false);
                    MuxAssert.Contains("\"was_running\":true", stop.Content, "was running");
                    MuxAssert.Contains("\"running\":false", stop.Content, "now stopped");

                    await provider.ExecuteAsync("process_start", Json(new { command = Ps("Start-Sleep -Seconds 60") }), dir, ct).ConfigureAwait(false);
                    ToolResult all = await provider.ExecuteAsync("process_stop", Json(new { id = "all" }), dir, ct).ConfigureAwait(false);
                    MuxAssert.Contains("\"stopped\":1", all.Content, "stop all");
                }
            });
            AddProcess("ToolsRejectBadCalls", "The process tools report missing arguments, unknown ids, bad timeouts, the limit, and unknown names", async (string dir, CancellationToken ct) =>
            {
                using (BackgroundProcessRegistry registry = new BackgroundProcessRegistry(1, BackgroundProcessRegistry.DefaultOutputCapacity))
                {
                    BackgroundProcessToolProvider provider = new BackgroundProcessToolProvider(registry);
                    ToolResult missing = await provider.ExecuteAsync("process_start", Json(new { }), dir, ct).ConfigureAwait(false);
                    MuxAssert.Contains("invalid_arguments", missing.Content, "missing command");
                    ToolResult unknown = await provider.ExecuteAsync("process_output", Json(new { id = "p42" }), dir, ct).ConfigureAwait(false);
                    MuxAssert.Contains("unknown_process", unknown.Content, "unknown id");
                    MuxAssert.Contains("process_list", unknown.Content, "points at the list");
                    await provider.ExecuteAsync("process_start", Json(new { command = Ps("Start-Sleep -Seconds 60") }), dir, ct).ConfigureAwait(false);
                    ToolResult badTimeout = await provider.ExecuteAsync("process_output", Json(new { id = "p1", timeout_ms = true }), dir, ct).ConfigureAwait(false);
                    MuxAssert.Contains("invalid_arguments", badTimeout.Content, "boolean timeout");
                    ToolResult limit = await provider.ExecuteAsync("process_start", Json(new { command = Ps("Start-Sleep -Seconds 60") }), dir, ct).ConfigureAwait(false);
                    MuxAssert.IsFalse(limit.Success, "limit refused");
                    MuxAssert.Contains("process_limit", limit.Content, "limit code");
                    ToolResult badDir = await provider.ExecuteAsync("process_stop", Json(new { }), dir, ct).ConfigureAwait(false);
                    MuxAssert.Contains("invalid_arguments", badDir.Content, "stop needs an id");
                    ToolResult other = await provider.ExecuteAsync("process_frobnicate", Json(new { }), dir, ct).ConfigureAwait(false);
                    MuxAssert.Contains("unknown_tool", other.Content, "unknown tool");
                }
            });

            // --- settings and sidebar ---
            Add("ProcessSettingsDefaultsAndClamps", "backgroundProcessMaxConcurrent and backgroundProcessOutputBytes default, clamp, and round-trip", (CancellationToken ct) =>
            {
                MuxSettings defaults = new MuxSettings();
                MuxAssert.AreEqual(8, defaults.BackgroundProcessMaxConcurrent, "default concurrency");
                MuxAssert.AreEqual(1048576, defaults.BackgroundProcessOutputBytes, "default buffer");
                MuxSettings clamped = new MuxSettings { BackgroundProcessMaxConcurrent = 0, BackgroundProcessOutputBytes = 5 };
                MuxAssert.AreEqual(1, clamped.BackgroundProcessMaxConcurrent, "concurrency floor");
                MuxAssert.AreEqual(16384, clamped.BackgroundProcessOutputBytes, "buffer floor");
                clamped.BackgroundProcessMaxConcurrent = 500;
                clamped.BackgroundProcessOutputBytes = int.MaxValue;
                MuxAssert.AreEqual(64, clamped.BackgroundProcessMaxConcurrent, "concurrency ceiling");
                MuxAssert.AreEqual(16777216, clamped.BackgroundProcessOutputBytes, "buffer ceiling");
                MuxSettings parsed = JsonSerializer.Deserialize<MuxSettings>("{\"backgroundProcessMaxConcurrent\":3,\"backgroundProcessOutputBytes\":65536}")!;
                MuxAssert.AreEqual(3, parsed.BackgroundProcessMaxConcurrent, "from JSON");
                MuxAssert.AreEqual(65536, parsed.BackgroundProcessOutputBytes, "from JSON");
                return Task.CompletedTask;
            });
            Add("SidebarProcessLine", "The sidebar shows running, exited, and stopped processes", (CancellationToken ct) =>
            {
                MuxAssert.AreEqual(" ● p1 web", SidebarView.FormatProcessLine(new BackgroundProcessInfo { Id = "p1", Name = "web", Command = "npm run dev", Running = true }), "running with a name");
                MuxAssert.AreEqual(" ● p2 npm test -- --watch", SidebarView.FormatProcessLine(new BackgroundProcessInfo { Id = "p2", Command = "npm test -- --watch", Running = true }), "running without a name");
                MuxAssert.AreEqual(" ○ p3 exit 1 pytest", SidebarView.FormatProcessLine(new BackgroundProcessInfo { Id = "p3", Command = "pytest", ExitCode = 1 }), "exited");
                MuxAssert.AreEqual(" ○ p4 stopped api", SidebarView.FormatProcessLine(new BackgroundProcessInfo { Id = "p4", Name = "api", StoppedByUser = true, ExitCode = 137 }), "stopped");
                MuxAssert.Throws<ArgumentNullException>(() => SidebarView.FormatProcessLine(null!), "null");
                return Task.CompletedTask;
            });

            // --- terminal ---
            AddProcess("TerminalProcessesCommand", "/processes lists, shows output, stops, and clears; bad input is reported", async (string dir, CancellationToken ct) =>
            {
                using (BackgroundProcessRegistry registry = new BackgroundProcessRegistry())
                {
                    HeadlessBackend backend = new HeadlessBackend(160, 40);
                    await using (JobManager manager = new JobManager(EchoRunner, maxConcurrency: 1))
                    using (MuxTuiApp app = new MuxTuiApp(backend, manager, "demo", ApprovalPolicyEnum.AutoApprove, processRegistry: registry))
                    {
                        Submit(backend, app, "/processes");
                        registry.Start(Ps("Write-Output 'SERVER UP'; Start-Sleep -Seconds 60"), dir, "web");
                        await registry.ReadAsync("p1", "SERVER UP", 30000, ct).ConfigureAwait(false);
                        MuxAssert.Contains("● p1 web", string.Join("\n", app.SidebarSnapshot()), "sidebar lists it");
                        Submit(backend, app, "/processes");
                        Submit(backend, app, "/ps output p1");
                        Submit(backend, app, "/processes stop p9");
                        Submit(backend, app, "/processes stop");
                        Submit(backend, app, "/processes frob");
                        Submit(backend, app, "/processes stop p1");
                        for (int i = 0; i < 100 && registry.HasRunning(); i++) await Task.Delay(50, ct).ConfigureAwait(false);
                        Submit(backend, app, "/processes stop p1");
                        Submit(backend, app, "/processes clear");
                        string transcript = string.Join("\n", app.TranscriptSnapshot());
                        MuxAssert.Contains("No background processes.", transcript, "empty list first");
                        MuxAssert.Contains("p1  running  pid", transcript, "listed as running");
                        MuxAssert.Contains("SERVER UP", transcript, "output shown");
                        MuxAssert.Contains("No background process has the id 'p9'", transcript, "unknown id");
                        MuxAssert.Contains("Name a process", transcript, "missing id");
                        MuxAssert.Contains("Usage: /processes", transcript, "usage");
                        MuxAssert.Contains("Stopped p1", transcript, "stopped");
                        MuxAssert.Contains("Process p1 already exited", transcript, "second stop explains");
                        MuxAssert.Contains("Cleared 1 exited process.", transcript, "cleared");
                        MuxAssert.IsFalse(registry.HasRunning(), "nothing running");
                    }
                }
            });
            Add("TerminalWithoutRegistry", "/processes without a registry says the feature is unavailable", async (CancellationToken ct) =>
            {
                HeadlessBackend backend = new HeadlessBackend(140, 40);
                await using (JobManager manager = new JobManager(EchoRunner, maxConcurrency: 1))
                using (MuxTuiApp app = new MuxTuiApp(backend, manager, "demo", ApprovalPolicyEnum.AutoApprove))
                {
                    Submit(backend, app, "/processes");
                    MuxAssert.Contains("Background processes are not available", string.Join("\n", app.TranscriptSnapshot()), "notice");
                }
            });

            // --- mux print ---
            cases.Add(new TestCaseDescriptor(SuiteId, "PrintKillsProcessesAtExit", "A process the model starts during mux print is killed when the command exits", (CancellationToken ct) =>
            {
                string configDir = Path.Combine(Path.GetTempPath(), "mux-print-proc-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(configDir);
                try
                {
                    File.WriteAllText(Path.Combine(configDir, "settings.json"), "{\"skillsEnabled\":false}");
                    using (MockHttpServer server = new MockHttpServer())
                    {
                        string arguments = JsonSerializer.Serialize(new { command = Ps("Write-Output 'WATCHER READY'; Start-Sleep -Seconds 120"), name = "watcher", wait_for = "WATCHER READY", timeout_ms = 30000 });
                        string toolCall = "{\"choices\":[{\"delta\":{\"tool_calls\":[{\"index\":0,\"id\":\"call_proc\",\"function\":{\"name\":\"process_start\",\"arguments\":" + JsonSerializer.Serialize(arguments) + "}}]},\"finish_reason\":\"tool_calls\"}]}";
                        server.RegisterStreamingResponse("zq7", new List<string> { toolCall });
                        server.RegisterStreamingResponse("dropped_chars", new List<string> { AgentTestHarness.BuildTextSseChunk("The watcher is up.") });
                        server.Start();
                        CliInvocationResult result = InvokeCli(new[]
                        {
                            "print", "--config-dir", configDir, "--yolo", "--output-format", "jsonl",
                            "--base-url", server.BaseUrl, "--model", "test-model", "--adapter-type", "openai-compatible",
                            "zq7 start the watcher please"
                        });
                        MuxAssert.AreEqual(0, result.ExitCode, "exit 0: " + result.StdErr + " | " + result.StdOut);
                        Match pid = Regex.Match(result.StdOut, "\"pid\":(\\d+)");
                        MuxAssert.IsTrue(pid.Success, "pid reported in the tool result: " + result.StdOut);
                        MuxAssert.Contains("The watcher is up.", result.StdOut, "final answer");
                        MuxAssert.IsFalse(IsAlive(int.Parse(pid.Groups[1].Value)), "the background process was killed at exit");
                    }
                }
                finally
                {
                    try { Directory.Delete(configDir, true); } catch (Exception) { }
                }

                return Task.CompletedTask;
            }, skip: !pwsh, skipReason: "pwsh is not on PATH"));

            // --- react-dev-server ---
            AddProcess("ReactDevServerDetect", "react-dev-server detect finds the dev command, framework, port, and patterns", async (string dir, CancellationToken ct) =>
            {
                string skills = Path.Combine(dir, "skills");
                DefaultSkillLibrary.SeedInto(skills);
                SkillTestContext c = new SkillTestContext(dir, skills, ct);
                Write(c, "vite/package.json", "{\"name\":\"x\",\"scripts\":{\"dev\":\"vite\"},\"dependencies\":{\"react\":\"18\"},\"devDependencies\":{\"vite\":\"5\"}}");
                Write(c, "next/package.json", "{\"name\":\"x\",\"scripts\":{\"dev\":\"next dev -p 4000\"},\"dependencies\":{\"react\":\"18\",\"next\":\"14\"}}");
                Write(c, "next/pnpm-lock.yaml", "lockfileVersion: 9");
                Write(c, "cra/package.json", "{\"name\":\"x\",\"scripts\":{\"start\":\"PORT=3005 react-scripts start\"},\"dependencies\":{\"react\":\"18\",\"react-scripts\":\"5\"}}");
                Write(c, "noscript/package.json", "{\"name\":\"x\",\"scripts\":{\"build\":\"vite build\"},\"dependencies\":{\"react\":\"18\"}}");
                Write(c, "notreact/package.json", "{\"name\":\"x\",\"scripts\":{\"dev\":\"node server.js\"}}");
                (await c.RunIn("vite", false, "react-dev-server", "detect").ConfigureAwait(false))
                    .Exit(0).Has("Dev command: npm run dev").Has("Framework: Vite").Has("Expected URL: http://localhost:5173").Has("Ready pattern: https?://");
                (await c.RunIn("next", false, "react-dev-server", "detect").ConfigureAwait(false))
                    .Exit(0).Has("Dev command: pnpm run dev").Has("Framework: Next.js").Has("Expected URL: http://localhost:4000");
                (await c.RunIn("cra", false, "react-dev-server", "detect").ConfigureAwait(false))
                    .Exit(0).Has("Dev command: npm run start").Has("Framework: Create React App").Has("http://localhost:3005");
                (await c.RunIn("noscript", false, "react-dev-server", "detect").ConfigureAwait(false)).Exit(2).Has("no dev, start, or serve script");
                (await c.RunIn("notreact", false, "react-dev-server", "detect").ConfigureAwait(false)).Exit(2);
            });
            Add("ReactDevServerDefined", "react-dev-server is a read-only skill whose body starts the server with process_start", (CancellationToken ct) =>
            {
                DefaultSkillDef? found = null;
                foreach (DefaultSkillDef definition in DefaultReactSkills.All()) if (definition.Id == "react-dev-server") found = definition;
                MuxAssert.IsNotNull(found, "defined");
                MuxAssert.IsFalse(found!.Mutating, "detect is read-only");
                MuxAssert.Contains("process_start", found.Body, "uses process_start");
                MuxAssert.Contains("process_stop", found.Body, "says how to stop it");
                MuxAssert.Contains("Never start a second copy", found.Body, "no duplicates");
                MuxAssert.AreEqual(153, DefaultSkillLibrary.All().Count, "library size");
                return Task.CompletedTask;
            });

            return new TestSuiteDescriptor(SuiteId, "Background processes: registry, tools, terminal, print, react-dev-server", cases);
        }

        #endregion

        #region Private-Methods

        // Wraps a PowerShell script for the platform shell: single quotes for /bin/sh so $ is not expanded, double
        // quotes for cmd.exe.
        private static string Ps(string script)
        {
            if (OperatingSystem.IsWindows())
            {
                return "pwsh -NoProfile -NonInteractive -Command \"" + script.Replace("\"", "\\\"") + "\"";
            }

            return "pwsh -NoProfile -NonInteractive -Command '" + script.Replace("'", "'\\''") + "'";
        }

        private static bool IsAlive(int pid)
        {
            if (pid <= 0) return false;
            for (int i = 0; i < 40; i++)
            {
                try
                {
                    using (Process process = Process.GetProcessById(pid))
                    {
                        if (process.HasExited) return false;
                    }
                }
                catch (ArgumentException)
                {
                    return false;
                }
                catch (InvalidOperationException)
                {
                    return false;
                }

                Thread.Sleep(100);
            }

            return true;
        }

        private static JsonElement Json(object value)
        {
            using (JsonDocument document = JsonDocument.Parse(JsonSerializer.Serialize(value)))
            {
                return document.RootElement.Clone();
            }
        }

        private static void Write(SkillTestContext c, string relative, string content)
        {
            string path = Path.Combine(c.Project, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }

        private static void Submit(HeadlessBackend backend, MuxTuiApp app, string prompt)
        {
            backend.FeedInput(prompt + "\r");
            app.PumpInputOnce();
        }

        private static async IAsyncEnumerable<AgentEvent> EchoRunner(Job job, string prompt, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask.ConfigureAwait(false);
            yield return new AssistantTextEvent { Text = "Echo: " + prompt };
            yield return new RunCompletedEvent { RunId = Guid.NewGuid().ToString("N"), Status = "completed", IterationsCompleted = 1, DurationMs = 1 };
        }

        private static CliInvocationResult InvokeCli(string[] args)
        {
            TextWriter originalOut = Console.Out;
            TextWriter originalErr = Console.Error;
            StringWriter stdout = new StringWriter();
            StringWriter stderr = new StringWriter();
            try
            {
                Console.SetOut(stdout);
                Console.SetError(stderr);
                int exitCode = Mux.Cli.Program.Main(args);
                return new CliInvocationResult(exitCode, stdout.ToString(), stderr.ToString());
            }
            finally
            {
                Console.SetOut(originalOut);
                Console.SetError(originalErr);
            }
        }

        private static async Task WithTempAsync(Func<string, Task> body)
        {
            string root = Path.Combine(Path.GetTempPath(), "mux-proc-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                await body(root).ConfigureAwait(false);
            }
            finally
            {
                try { Directory.Delete(root, true); } catch (Exception) { }
            }
        }

        private static bool IsOnPath(string executable)
        {
            string[] names = OperatingSystem.IsWindows() ? new[] { executable + ".exe", executable + ".cmd" } : new[] { executable };
            foreach (string directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator))
            {
                foreach (string name in names)
                {
                    if (!string.IsNullOrWhiteSpace(directory) && File.Exists(Path.Combine(directory, name)))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        #endregion
    }
}
