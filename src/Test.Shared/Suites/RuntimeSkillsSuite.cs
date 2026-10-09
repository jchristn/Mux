namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Net.Sockets;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Skills;
    using Test.Shared.Support;
    using Touchstone.Core;

    /// <summary>
    /// Touchstone suite for the runtime diagnosis skills: log-triage grouping, port-inspect against a real listener, and
    /// bench timing, BenchmarkDotNet discovery, and k6. Positive and negative cases.
    /// </summary>
    public static class RuntimeSkillsSuite
    {
        #region Private-Members

        private const string SuiteId = "RuntimeSkills";

        private const string SampleLog = @"2026-10-09T10:00:01.123Z INFO Starting service on port 8080
2026-10-09T10:00:02.000Z ERROR Failed to connect to database at db-01:5432 after 3 attempts
2026-10-09T10:00:05.000Z ERROR Failed to connect to database at db-02:5432 after 5 attempts
2026-10-09T10:00:06.000Z ERROR Unhandled exception processing order 8f14e45f-ceea-467a-9575-1a2b3c4d5e6f
System.NullReferenceException: Object reference not set to an instance of an object.
   at Shop.Orders.OrderService.Submit(Order order) in /src/Shop/Orders/OrderService.cs:line 42
   at Shop.Api.OrdersController.Post(OrderDto dto) in /src/Shop/Api/OrdersController.cs:line 17
2026-10-09T10:00:07.000Z INFO healthy
Traceback (most recent call last):
  File ""/app/worker.py"", line 12, in <module>
    main()
  File ""/app/worker.py"", line 8, in main
    raise ValueError(""bad payload"")
ValueError: bad payload
2026-10-09T10:00:09.000Z ERROR Unhandled exception processing order 1a2b3c4d-ceea-467a-9575-8f14e45f0000
   at Shop.Orders.OrderService.Submit(Order order) in /src/Shop/Orders/OrderService.cs:line 42
{""level"":""error"",""msg"":""timeout calling payments"",""ms"":5003}";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Builds the suite descriptor.
        /// </summary>
        /// <returns>The descriptor.</returns>
        public static TestSuiteDescriptor Create()
        {
            bool ready = IsOnPath("pwsh");
            bool portTool = OperatingSystem.IsWindows() || IsOnPath("lsof") || IsOnPath("ss");
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();
            void Add(string id, string name, Func<SkillTestContext, Task> body, bool skip = false, string reason = "")
            {
                cases.Add(new TestCaseDescriptor(SuiteId, id, name, (CancellationToken ct) => RunWithContextAsync(body, ct), skip: !ready || skip, skipReason: !ready ? "pwsh is not on PATH" : reason));
            }

            cases.Add(new TestCaseDescriptor(SuiteId, "SkillsDefined", "log-triage, port-inspect, and bench are read-only debugging skills listed everywhere", (CancellationToken ct) =>
            {
                Dictionary<string, DefaultSkillDef> defs = DefaultSkillLibrary.Definitions().ToDictionary(d => d.Id, StringComparer.Ordinal);
                foreach (string id in new[] { "log-triage", "port-inspect", "bench" })
                {
                    MuxAssert.IsFalse(defs[id].Mutating, id + " read-only");
                    MuxAssert.AreEqual("debugging", DefaultSkillCategories.For(id), id + " category");
                    MuxAssert.AreEqual(0, defs[id].AppliesTo.Count, id + " is not gated");
                }

                MuxAssert.AreEqual("time,dotnet,k6", string.Join(",", defs["bench"].Commands.Select(c => c.Name)), "bench commands");
                return Task.CompletedTask;
            }));

            Add("LogTriageGroupsErrors", "log-triage groups repeated errors with masked ids, keeps .NET and Python stacks, reads JSON entries, and honors --top", async (SkillTestContext c) =>
            {
                string log = Write(c, "app.log", SampleLog);
                SkillRunResult run = await c.Run("log-triage", "summarize", log).ConfigureAwait(false);
                run.Exit(0).Has("Read 17 lines; 6 error lines in 4 groups.")
                    .Has("2x  lines 2..3  ERROR Failed to connect to database at db-<n>:<n> after <n> attempts")
                    .Has("2x  lines 4..15  ERROR Unhandled exception processing order <guid>")
                    .Has("System.NullReferenceException").Has("OrderService.cs:line 42")
                    .Has("1x  lines 9  ValueError: bad payload").Has("raise ValueError")
                    .Has("ERROR timeout calling payments").Lacks("INFO healthy");
                (await c.Run("log-triage", "summarize", log, "--top", "1").ConfigureAwait(false)).Exit(0).Has("[mux: 3 more groups");
                (await c.Run("log-triage", "summarize", Write(c, "ok.log", "INFO started\nINFO ready\n")).ConfigureAwait(false)).Exit(0).Has("No errors, exceptions, or failures found.");
                (await c.Run("log-triage", "summarize", Path.Combine(c.Project, "missing.log")).ConfigureAwait(false)).Exit(2).Has("log file not found");
                (await c.Run("log-triage", "summarize", log, "--top", "0").ConfigureAwait(false)).Exit(2).Has("--top must be a whole number");
                (await c.Run("log-triage", "summarize").ConfigureAwait(false)).Exit(2).Has("pass the log file");
            });

            Add("PortInspectFindsListener", "port-inspect reports a real listener's port and owning process, and nothing on a free port", async (SkillTestContext c) =>
            {
                TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
                listener.Start();
                try
                {
                    int port = ((IPEndPoint)listener.LocalEndpoint).Port;
                    (await c.Run("port-inspect", "list", port.ToString()).ConfigureAwait(false))
                        .Exit(0).Has(port.ToString()).Has(Environment.ProcessId.ToString()).Has("Read-only");
                    int free = StubHttpServer.FreeLoopbackPort();
                    (await c.Run("port-inspect", "list", free.ToString()).ConfigureAwait(false)).Exit(0).Has("Nothing is listening on port " + free);
                    (await c.Run("port-inspect", "list", "http").ConfigureAwait(false)).Exit(2).Has("pass a port number");
                }
                finally
                {
                    listener.Stop();
                }
            }, !portTool, "no lsof, ss, or Windows networking cmdlets");

            Add("BenchTimesCommands", "bench time measures one or two commands in-process and fails when a run fails", async (SkillTestContext c) =>
            {
                string ok = OperatingSystem.IsWindows() ? "cd ." : "true";
                string bad = OperatingSystem.IsWindows() ? "exit /b 3" : "exit 3";
                (await c.Run("bench", "time", ok, ok, "--runs", "2").ConfigureAwait(false)).Exit(0).Has("hyperfine is not installed").Has("Command: " + ok).Has("mean").Has("(2 runs)");
                (await c.Run("bench", "time", bad, "--runs", "2").ConfigureAwait(false)).Exit(1).Has("exited nonzero");
            }, IsOnPath("hyperfine"), "hyperfine is installed");

            Add("BenchInputAndProjects", "bench rejects bad input, finds a BenchmarkDotNet project, and runs k6 scripts", async (SkillTestContext c) =>
            {
                Write(c, ".git/HEAD", "ref: refs/heads/main");
                (await c.Run("bench", "time").ConfigureAwait(false)).Exit(2).Has("pass one command");
                (await c.Run("bench", "time", "a", "b", "c").ConfigureAwait(false)).Exit(2).Has("at most two");
                (await c.Run("bench", "time", "a", "--runs", "0").ConfigureAwait(false)).Exit(2).Has("--runs must be a whole number");
                (await c.Run(true, "bench", "dotnet").ConfigureAwait(false)).Exit(2).Has("no project references BenchmarkDotNet");
                Write(c, "bench/Perf/Perf.csproj", "<Project><ItemGroup><PackageReference Include=\"BenchmarkDotNet\" Version=\"0.14.0\" /></ItemGroup></Project>");
                Write(c, "src/App/App.csproj", "<Project />");
                (await c.Run(true, "bench", "dotnet", "*Parser*").ConfigureAwait(false)).Exit(0).Has("DRYRUN: dotnet run -c Release --project bench/Perf/Perf.csproj -- --filter *Parser*");
                (await c.Run(true, "bench", "k6", "load.js").ConfigureAwait(false)).Exit(0).Has("DRYRUN: k6 run --quiet load.js");
                (await c.Run("bench", "k6", "missing.js").ConfigureAwait(false)).Exit(2).Has("k6 script not found");
                (await c.Run("bench", "k6").ConfigureAwait(false)).Exit(2).Has("pass the k6 script");
            });

            return new TestSuiteDescriptor(SuiteId, "Runtime diagnosis skills: log-triage, port-inspect, bench", cases);
        }

        #endregion

        #region Private-Methods

        private static string Write(SkillTestContext c, string relative, string content)
        {
            string path = Path.Combine(c.Project, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content.Replace("\r\n", "\n"));
            return path;
        }

        private static bool IsOnPath(string executable)
        {
            foreach (string directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator))
            {
                if (string.IsNullOrWhiteSpace(directory)) continue;
                foreach (string candidate in new[] { executable, executable + ".exe", executable + ".cmd" })
                {
                    if (File.Exists(Path.Combine(directory, candidate))) return true;
                }
            }

            return false;
        }

        private static async Task RunWithContextAsync(Func<SkillTestContext, Task> body, CancellationToken ct)
        {
            string root = Path.Combine(Path.GetTempPath(), "mux-runtime-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                string skills = Path.Combine(root, "skills");
                DefaultSkillLibrary.SeedInto(skills);
                Directory.CreateDirectory(Path.Combine(root, "project"));
                await body(new SkillTestContext(root, skills, ct)).ConfigureAwait(false);
            }
            finally
            {
                try { Directory.Delete(root, true); } catch (Exception) { }
            }
        }

        #endregion
    }
}
