namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Cli.Commands;
    using Mux.Core.Agent;
    using Touchstone.Core;

    /// <summary>
    /// Touchstone suite for the headless output-mode matrix on <c>mux print</c>: the four public shapes
    /// (streamed text, buffered text, streamed jsonl, buffered json) crossed with the <c>--stats</c> /
    /// <c>--no-stats</c> toggle and the contract-v2 <c>usage</c> block. Every axis is exercised in both the
    /// positive (feature engaged) and negative (feature off / field absent) direction:
    /// the pure <see cref="HeadlessOutputPlan"/> resolver, the <c>--stats</c>/<c>--buffer</c> parser flags,
    /// the <see cref="StructuredOutputFormatter"/> stats gating, and a full end-to-end truth table driven
    /// through <see cref="Mux.Cli.Program.Main"/> against a mock backend.
    /// </summary>
    public static class HeadlessOutputModesSuite
    {
        /// <summary>
        /// Builds the headless-output-modes suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/> containing all output-mode cases.</returns>
        public static TestSuiteDescriptor Create()
        {
            return new TestSuiteDescriptor(
                "HeadlessOutputModes",
                "Headless output modes, stats toggle, and usage contract",
                new List<TestCaseDescriptor>
                {
                    // ---- Group A: the pure plan resolver, full truth table ----
                    new TestCaseDescriptor("HeadlessOutputModes", "PlanResolverTruthTable", "HeadlessOutputPlan resolves every format x stats x buffer combination", (CancellationToken ct) =>
                    {
                        // (format, statsRequested, buffer) => (streamed, includeStats, emitTextStatsFooter)

                        // text: streamed by default, statless on stdout; --buffer holds it; --stats adds a stderr footer.
                        AssertPlan(OutputFormatEnum.Text, null, false, true, false, false, "streamed text, no stats (default)");
                        AssertPlan(OutputFormatEnum.Text, null, true, false, false, false, "buffered text, no stats");
                        AssertPlan(OutputFormatEnum.Text, true, false, true, true, true, "streamed text + stats footer");
                        AssertPlan(OutputFormatEnum.Text, true, true, false, true, true, "buffered text + stats footer");
                        AssertPlan(OutputFormatEnum.Text, false, false, true, false, false, "text with stats explicitly off");

                        // json: always buffered; stats on by default, off with --no-stats; --buffer is a no-op.
                        AssertPlan(OutputFormatEnum.Json, null, false, false, true, false, "json default = buffered + stats");
                        AssertPlan(OutputFormatEnum.Json, false, false, false, false, false, "json --no-stats");
                        AssertPlan(OutputFormatEnum.Json, true, false, false, true, false, "json --stats");
                        AssertPlan(OutputFormatEnum.Json, null, true, false, true, false, "json ignores --buffer (stays buffered)");

                        // jsonl: always streamed; stats on by default, off with --no-stats; --buffer is a no-op.
                        AssertPlan(OutputFormatEnum.Jsonl, null, false, true, true, false, "jsonl default = streamed + stats");
                        AssertPlan(OutputFormatEnum.Jsonl, false, false, true, false, false, "jsonl --no-stats");
                        AssertPlan(OutputFormatEnum.Jsonl, true, false, true, true, false, "jsonl --stats");
                        AssertPlan(OutputFormatEnum.Jsonl, null, true, true, true, false, "jsonl ignores --buffer (stays streamed)");

                        // A text stats footer is never emitted for structured output.
                        MuxAssert.IsFalse(HeadlessOutputPlan.Resolve(OutputFormatEnum.Json, true, false).EmitTextStatsFooter, "json never emits the text footer");
                        MuxAssert.IsFalse(HeadlessOutputPlan.Resolve(OutputFormatEnum.Jsonl, true, false).EmitTextStatsFooter, "jsonl never emits the text footer");
                        return Task.CompletedTask;
                    }),

                    // ---- Group B: the new parser flags, positive and negative ----
                    new TestCaseDescriptor("HeadlessOutputModes", "StatsFlagParses", "--stats / --no-stats set the tri-state stats selection", (CancellationToken ct) =>
                    {
                        MuxAssert.IsNull(CliArgumentParser.ParsePrint(new[] { "go" }).Stats, "Stats is null (unset) by default");
                        MuxAssert.AreEqual(true, CliArgumentParser.ParsePrint(new[] { "--stats", "go" }).Stats ?? false, "--stats sets true");
                        MuxAssert.AreEqual(false, CliArgumentParser.ParsePrint(new[] { "--no-stats", "go" }).Stats ?? true, "--no-stats sets false");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("HeadlessOutputModes", "BufferFlagParses", "--buffer and --no-stream set buffering; off by default", (CancellationToken ct) =>
                    {
                        MuxAssert.IsFalse(CliArgumentParser.ParsePrint(new[] { "go" }).Buffer, "Buffer off by default");
                        MuxAssert.IsTrue(CliArgumentParser.ParsePrint(new[] { "--buffer", "go" }).Buffer, "--buffer sets true");
                        MuxAssert.IsTrue(CliArgumentParser.ParsePrint(new[] { "--no-stream", "go" }).Buffer, "--no-stream sets true");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("HeadlessOutputModes", "ModeFlagsCoexistWithPrompt", "the mode flags parse alongside other options and preserve the prompt", (CancellationToken ct) =>
                    {
                        PrintSettings settings = CliArgumentParser.ParsePrint(new[]
                        {
                            "--output-format", "json", "--no-stats", "--buffer", "--yolo", "explain the code"
                        });
                        MuxAssert.AreEqual("json", settings.OutputFormat, "output format parsed");
                        MuxAssert.AreEqual(false, settings.Stats ?? true, "--no-stats parsed");
                        MuxAssert.IsTrue(settings.Buffer, "--buffer parsed");
                        MuxAssert.AreEqual("explain the code", settings.Prompt, "positional prompt preserved");
                        return Task.CompletedTask;
                    }),

                    // ---- Group C: formatter stats gating and usage block, positive and negative ----
                    new TestCaseDescriptor("HeadlessOutputModes", "RunSummaryIncludesUsageWhenStatsOn", "FormatRunSummary carries the usage block and metrics when stats are included", (CancellationToken ct) =>
                    {
                        JsonElement root = JsonDocument.Parse(
                            StructuredOutputFormatter.FormatRunSummary(SampleCompleted(), "the answer", "sess-1", includeStats: true)).RootElement;

                        MuxAssert.AreEqual(2, root.GetProperty("contractVersion").GetInt32(), "contractVersion is 2");
                        MuxAssert.AreEqual("the answer", root.GetProperty("result").GetString(), "result present");
                        MuxAssert.AreEqual("sess-1", root.GetProperty("sessionId").GetString(), "sessionId present");
                        MuxAssert.AreEqual("completed", root.GetProperty("status").GetString(), "status present");
                        MuxAssert.AreEqual(2, root.GetProperty("iterationsCompleted").GetInt32(), "iterationsCompleted present");

                        JsonElement usage = root.GetProperty("usage");
                        MuxAssert.AreEqual(11, usage.GetProperty("inputTokens").GetInt32(), "usage.inputTokens");
                        MuxAssert.AreEqual(22, usage.GetProperty("outputTokens").GetInt32(), "usage.outputTokens");
                        MuxAssert.AreEqual(33, usage.GetProperty("totalTokens").GetInt32(), "usage.totalTokens");
                        MuxAssert.AreEqual(44, usage.GetProperty("estimatedTokens").GetInt32(), "usage.estimatedTokens");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("HeadlessOutputModes", "RunSummaryOmitsUsageWhenStatsOff", "FormatRunSummary drops usage and metrics when stats are excluded, keeping the answer", (CancellationToken ct) =>
                    {
                        JsonElement root = JsonDocument.Parse(
                            StructuredOutputFormatter.FormatRunSummary(SampleCompleted(), "the answer", "sess-1", includeStats: false)).RootElement;

                        MuxAssert.AreEqual(2, root.GetProperty("contractVersion").GetInt32(), "contractVersion still present");
                        MuxAssert.AreEqual("the answer", root.GetProperty("result").GetString(), "result still present");
                        MuxAssert.AreEqual("completed", root.GetProperty("status").GetString(), "status still present");
                        MuxAssert.IsFalse(root.TryGetProperty("usage", out _), "usage omitted");
                        MuxAssert.IsFalse(root.TryGetProperty("iterationsCompleted", out _), "iterationsCompleted omitted");
                        MuxAssert.IsFalse(root.TryGetProperty("durationMs", out _), "durationMs omitted");
                        MuxAssert.IsFalse(root.TryGetProperty("finalEstimatedTokens", out _), "finalEstimatedTokens omitted");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("HeadlessOutputModes", "RunCompletedEventGatesStats", "the run_completed jsonl event includes/omits usage based on the stats flag", (CancellationToken ct) =>
                    {
                        JsonElement withStats = JsonDocument.Parse(StructuredOutputFormatter.FormatEvent(SampleCompleted(), includeStats: true)).RootElement;
                        MuxAssert.AreEqual("run_completed", withStats.GetProperty("eventType").GetString(), "eventType (with stats)");
                        MuxAssert.IsTrue(withStats.TryGetProperty("usage", out JsonElement usage), "usage present with stats");
                        MuxAssert.AreEqual(33, usage.GetProperty("totalTokens").GetInt32(), "usage.totalTokens carried on the event");
                        MuxAssert.IsTrue(withStats.TryGetProperty("durationMs", out _), "durationMs present with stats");

                        JsonElement noStats = JsonDocument.Parse(StructuredOutputFormatter.FormatEvent(SampleCompleted(), includeStats: false)).RootElement;
                        MuxAssert.AreEqual("run_completed", noStats.GetProperty("eventType").GetString(), "eventType still present without stats");
                        MuxAssert.AreEqual("completed", noStats.GetProperty("status").GetString(), "status still present without stats");
                        MuxAssert.IsFalse(noStats.TryGetProperty("usage", out _), "usage omitted without stats");
                        MuxAssert.IsFalse(noStats.TryGetProperty("durationMs", out _), "durationMs omitted without stats");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("HeadlessOutputModes", "DefaultFormatEventKeepsStats", "the single-argument FormatEvent overload defaults to including stats", (CancellationToken ct) =>
                    {
                        JsonElement root = JsonDocument.Parse(StructuredOutputFormatter.FormatEvent(SampleCompleted())).RootElement;
                        MuxAssert.IsTrue(root.TryGetProperty("usage", out _), "usage present on the default overload (backward compatible)");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("HeadlessOutputModes", "TextStatsFooterFormats", "FormatTextStatsFooter renders token counts and tolerates a missing summary", (CancellationToken ct) =>
                    {
                        string footer = StructuredOutputFormatter.FormatTextStatsFooter(SampleCompleted());
                        MuxAssert.Contains("input=11", footer, "footer reports input tokens");
                        MuxAssert.Contains("output=22", footer, "footer reports output tokens");
                        MuxAssert.Contains("total=33", footer, "footer reports total tokens");
                        MuxAssert.Contains("est 44", footer, "footer reports the estimate");

                        string none = StructuredOutputFormatter.FormatTextStatsFooter(null);
                        MuxAssert.Contains("no run statistics", none, "null summary yields a safe sentinel, not a crash");
                        return Task.CompletedTask;
                    }),

                    // ---- Group D: end-to-end truth table through Program.Main against a mock backend ----
                    new TestCaseDescriptor("HeadlessOutputModes", "E2E_StreamedText_NoStats", "streamed text prints the answer to stdout with no stats footer on stderr", (CancellationToken ct) =>
                    {
                        CliInvocationResult result = RunPrint("--output-format", "text");
                        MuxAssert.AreEqual(0, result.ExitCode, "exit code");
                        MuxAssert.Contains("Answer text.", result.StdOut, "answer on stdout");
                        MuxAssert.DoesNotContain("mux: tokens", result.StdErr, "no stats footer without --stats");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("HeadlessOutputModes", "E2E_BufferedText_NoStats", "buffered text still prints the whole answer and no footer", (CancellationToken ct) =>
                    {
                        CliInvocationResult result = RunPrint("--output-format", "text", "--buffer");
                        MuxAssert.AreEqual(0, result.ExitCode, "exit code");
                        MuxAssert.Contains("Answer text.", result.StdOut, "answer on stdout");
                        MuxAssert.DoesNotContain("mux: tokens", result.StdErr, "no stats footer without --stats");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("HeadlessOutputModes", "E2E_StreamedText_WithStats", "streamed text with --stats emits the footer to stderr while stdout stays the answer", (CancellationToken ct) =>
                    {
                        CliInvocationResult result = RunPrint("--output-format", "text", "--stats");
                        MuxAssert.AreEqual(0, result.ExitCode, "exit code");
                        MuxAssert.Contains("Answer text.", result.StdOut, "answer on stdout");
                        MuxAssert.DoesNotContain("mux: tokens", result.StdOut, "stats never pollute stdout");
                        MuxAssert.Contains("mux: tokens", result.StdErr, "stats footer on stderr with --stats");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("HeadlessOutputModes", "E2E_BufferedText_WithStats", "buffered text with --stats emits the footer to stderr", (CancellationToken ct) =>
                    {
                        CliInvocationResult result = RunPrint("--output-format", "text", "--buffer", "--stats");
                        MuxAssert.AreEqual(0, result.ExitCode, "exit code");
                        MuxAssert.Contains("Answer text.", result.StdOut, "answer on stdout");
                        MuxAssert.Contains("mux: tokens", result.StdErr, "stats footer on stderr");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("HeadlessOutputModes", "E2E_Json_WithStats", "json (default) is one object carrying result and the usage block", (CancellationToken ct) =>
                    {
                        CliInvocationResult result = RunPrint("--output-format", "json");
                        MuxAssert.AreEqual(0, result.ExitCode, "exit code");
                        MuxAssert.AreEqual(string.Empty, result.StdErr.Trim(), "no stderr in json mode");

                        JsonElement root = JsonDocument.Parse(result.StdOut).RootElement;
                        MuxAssert.AreEqual(2, root.GetProperty("contractVersion").GetInt32(), "contractVersion is 2");
                        MuxAssert.Contains("Answer text.", root.GetProperty("result").GetString() ?? string.Empty, "result carries the answer");
                        MuxAssert.IsTrue(root.TryGetProperty("usage", out JsonElement usage), "usage present by default");
                        MuxAssert.IsTrue(usage.TryGetProperty("totalTokens", out _), "usage carries totalTokens");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("HeadlessOutputModes", "E2E_Json_NoStats", "json --no-stats drops usage and metrics but keeps the answer", (CancellationToken ct) =>
                    {
                        CliInvocationResult result = RunPrint("--output-format", "json", "--no-stats");
                        MuxAssert.AreEqual(0, result.ExitCode, "exit code");

                        JsonElement root = JsonDocument.Parse(result.StdOut).RootElement;
                        MuxAssert.Contains("Answer text.", root.GetProperty("result").GetString() ?? string.Empty, "result still present");
                        MuxAssert.AreEqual("completed", root.GetProperty("status").GetString(), "status still present");
                        MuxAssert.IsFalse(root.TryGetProperty("usage", out _), "usage omitted with --no-stats");
                        MuxAssert.IsFalse(root.TryGetProperty("iterationsCompleted", out _), "metrics omitted with --no-stats");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("HeadlessOutputModes", "E2E_Jsonl_WithStats", "jsonl (default) terminal event carries the usage block", (CancellationToken ct) =>
                    {
                        CliInvocationResult result = RunPrint("--output-format", "jsonl");
                        MuxAssert.AreEqual(0, result.ExitCode, "exit code");

                        JsonElement completed = LastEvent(result.StdOut);
                        MuxAssert.AreEqual("run_completed", completed.GetProperty("eventType").GetString(), "last event is run_completed");
                        MuxAssert.IsTrue(completed.TryGetProperty("usage", out _), "usage present by default");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("HeadlessOutputModes", "E2E_Jsonl_NoStats", "jsonl --no-stats terminal event omits usage and metrics", (CancellationToken ct) =>
                    {
                        CliInvocationResult result = RunPrint("--output-format", "jsonl", "--no-stats");
                        MuxAssert.AreEqual(0, result.ExitCode, "exit code");

                        JsonElement completed = LastEvent(result.StdOut);
                        MuxAssert.AreEqual("run_completed", completed.GetProperty("eventType").GetString(), "last event still run_completed");
                        MuxAssert.IsFalse(completed.TryGetProperty("usage", out _), "usage omitted with --no-stats");
                        MuxAssert.IsFalse(completed.TryGetProperty("durationMs", out _), "metrics omitted with --no-stats");
                        return Task.CompletedTask;
                    })
                });
        }

        private static void AssertPlan(
            OutputFormatEnum format,
            bool? statsRequested,
            bool buffer,
            bool expectStreamed,
            bool expectIncludeStats,
            bool expectFooter,
            string label)
        {
            HeadlessOutputPlan plan = HeadlessOutputPlan.Resolve(format, statsRequested, buffer);
            MuxAssert.AreEqual(expectStreamed, plan.Streamed, $"{label}: streamed");
            MuxAssert.AreEqual(expectIncludeStats, plan.IncludeStats, $"{label}: includeStats");
            MuxAssert.AreEqual(expectFooter, plan.EmitTextStatsFooter, $"{label}: emitTextStatsFooter");
        }

        private static RunCompletedEvent SampleCompleted()
        {
            return new RunCompletedEvent
            {
                RunId = "r1",
                SessionId = "sess-1",
                Status = "completed",
                IterationsCompleted = 2,
                ToolCallCount = 1,
                ErrorCount = 0,
                AssistantTextChars = 10,
                DurationMs = 42,
                FinalEstimatedTokens = 44,
                CompactionCount = 0,
                InputTokens = 11,
                OutputTokens = 22,
                TotalTokens = 33
            };
        }

        private static JsonElement LastEvent(string stdout)
        {
            string[] lines = stdout.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            MuxAssert.IsTrue(lines.Length >= 1, "at least one jsonl line");
            return JsonDocument.Parse(lines[^1]).RootElement;
        }

        private static CliInvocationResult RunPrint(params string[] modeArgs)
        {
            using MockHttpServer server = new MockHttpServer();
            server.RegisterStreamingResponse("modes test", new List<string> { AgentTestHarness.BuildTextSseChunk("Answer text.") });
            server.Start();

            List<string> args = new List<string> { "print" };
            args.AddRange(modeArgs);
            args.AddRange(new[]
            {
                "--yolo",
                "--base-url", server.BaseUrl,
                "--model", "test-model",
                "--adapter-type", "openai-compatible",
                "modes test"
            });

            return InvokeCli(args.ToArray());
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
    }
}
