namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Cli.Commands;
    using Mux.Core.Agent;
    using Mux.Core.Enums;
    using Mux.Core.Models;
    using Touchstone.Core;

    /// <summary>
    /// Touchstone suite for the headless-parity features added to <c>mux print</c>: the
    /// <c>--max-turns</c>, <c>--append-system-prompt</c>, and <c>--max-token-budget</c> quick wins;
    /// the token-budget enforcement in <see cref="AgentLoop"/>; the <see cref="AgentLoop.FinalConversation"/>
    /// and conversation-history replay that back session resume; the <c>sessionId</c> contract additions;
    /// and the single-object <c>json</c> run summary. Each behavior is exercised in both the positive
    /// (feature engaged) and negative (feature off, invalid input, or limit hit) direction.
    /// </summary>
    public static class HeadlessFeaturesSuite
    {
        /// <summary>
        /// Builds the headless-features suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/> containing all headless-feature cases.</returns>
        public static TestSuiteDescriptor Create()
        {
            return new TestSuiteDescriptor(
                "HeadlessFeatures",
                "Headless-parity CLI, budget, and session features",
                new List<TestCaseDescriptor>
                {
                    new TestCaseDescriptor("HeadlessFeatures", "ParsePrintReadsNewFlags", "print parser reads the Phase 1/2 flags", (CancellationToken ct) =>
                    {
                        PrintSettings settings = CliArgumentParser.ParsePrint(new[]
                        {
                            "--max-turns", "7",
                            "--append-system-prompt", "Be terse.",
                            "--max-token-budget", "1234",
                            "--resume", "my-session",
                            "--session-id", "abc123",
                            "--fork-session",
                            "--no-session-persistence",
                            "hello world"
                        });

                        MuxAssert.AreEqual(7, settings.MaxTurns ?? -1, "MaxTurns parsed");
                        MuxAssert.AreEqual("Be terse.", settings.AppendSystemPrompt, "AppendSystemPrompt parsed");
                        MuxAssert.AreEqual(1234, settings.MaxTokenBudget ?? -1, "MaxTokenBudget parsed");
                        MuxAssert.AreEqual("my-session", settings.Resume, "Resume parsed");
                        MuxAssert.AreEqual("abc123", settings.SessionId, "SessionId parsed");
                        MuxAssert.IsTrue(settings.ForkSession, "ForkSession parsed");
                        MuxAssert.IsTrue(settings.NoSessionPersistence, "NoSessionPersistence parsed");
                        MuxAssert.AreEqual("hello world", settings.Prompt, "positional prompt preserved");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("HeadlessFeatures", "ContinueFlagParses", "--continue parses as a boolean", (CancellationToken ct) =>
                    {
                        PrintSettings settings = CliArgumentParser.ParsePrint(new[] { "--continue", "do more" });
                        MuxAssert.IsTrue(settings.Continue, "Continue parsed");
                        MuxAssert.AreEqual("do more", settings.Prompt, "prompt preserved with --continue");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("HeadlessFeatures", "EffortFlagsParse", "the reasoning-effort flags parse", (CancellationToken ct) =>
                    {
                        PrintSettings settings = CliArgumentParser.ParsePrint(new[]
                        {
                            "--effort", "High",
                            "--effort-openai-value", "high",
                            "--effort-gemini-budget", "16000",
                            "--effort-ollama-think", "medium",
                            "go"
                        });

                        MuxAssert.AreEqual("high", settings.Effort, "effort normalized to lowercase");
                        MuxAssert.AreEqual("high", settings.EffortOpenAiValue, "openai value parsed");
                        MuxAssert.AreEqual(16000, settings.EffortGeminiBudget ?? -1, "gemini budget parsed");
                        MuxAssert.AreEqual("medium", settings.EffortOllamaThink, "ollama think parsed");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("HeadlessFeatures", "EffortRejectsUnknownLevel", "--effort with an unknown level is rejected", (CancellationToken ct) =>
                    {
                        MuxAssert.Throws<InvalidOperationException>(
                            () => CliArgumentParser.ParsePrint(new[] { "--effort", "banana", "go" }),
                            "unknown effort level throws");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("HeadlessFeatures", "EffortAcceptsOff", "--effort off parses", (CancellationToken ct) =>
                    {
                        PrintSettings settings = CliArgumentParser.ParsePrint(new[] { "--effort", "off", "go" });
                        MuxAssert.AreEqual("off", settings.Effort, "off parsed");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("HeadlessFeatures", "ShowThinkingFlagParses", "--show-thinking parses as a boolean", (CancellationToken ct) =>
                    {
                        MuxAssert.IsFalse(CliArgumentParser.ParsePrint(new[] { "go" }).ShowThinking, "off by default");
                        MuxAssert.IsTrue(CliArgumentParser.ParsePrint(new[] { "--show-thinking", "go" }).ShowThinking, "set by the flag");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("HeadlessFeatures", "MaxTurnsRejectsNonInteger", "--max-turns with a non-integer value fails", (CancellationToken ct) =>
                    {
                        MuxAssert.Throws<FormatException>(
                            () => CliArgumentParser.ParsePrint(new[] { "--max-turns", "lots", "go" }),
                            "non-integer --max-turns throws");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("HeadlessFeatures", "PrintOutputFormatAllowsJson", "print accepts text, json, and jsonl", (CancellationToken ct) =>
                    {
                        MuxAssert.AreEqual(
                            OutputFormatEnum.Json,
                            CommandRuntimeResolver.ParseOutputFormat("json", OutputFormatEnum.Text, OutputFormatEnum.Json, OutputFormatEnum.Jsonl),
                            "json parses for print");
                        MuxAssert.AreEqual(
                            OutputFormatEnum.Jsonl,
                            CommandRuntimeResolver.ParseOutputFormat("jsonl", OutputFormatEnum.Text, OutputFormatEnum.Json, OutputFormatEnum.Jsonl),
                            "jsonl parses for print");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("HeadlessFeatures", "PrintOutputFormatRejectsUnknown", "an unsupported print output format is rejected", (CancellationToken ct) =>
                    {
                        MuxAssert.Throws<InvalidOperationException>(
                            () => CommandRuntimeResolver.ParseOutputFormat("xml", OutputFormatEnum.Text, OutputFormatEnum.Json, OutputFormatEnum.Jsonl),
                            "unknown output format throws");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("HeadlessFeatures", "InteractivePromptFlagParses", "interactive --prompt seeds the startup prompt", (CancellationToken ct) =>
                    {
                        InteractiveSettings settings = CliArgumentParser.ParseInteractive(new[] { "--endpoint", "ollama", "--prompt", "hello there" });
                        MuxAssert.AreEqual("hello there", settings.Prompt, "--prompt value captured");
                        MuxAssert.AreEqual("ollama", settings.Endpoint, "other options still parse alongside --prompt");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("HeadlessFeatures", "InteractivePositionalSeedsPrompt", "a bare positional prompt seeds interactive mode", (CancellationToken ct) =>
                    {
                        InteractiveSettings settings = CliArgumentParser.ParseInteractive(new[] { "summarize", "the", "readme" });
                        MuxAssert.AreEqual("summarize the readme", settings.Prompt, "positionals joined into the prompt");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("HeadlessFeatures", "InteractiveExplicitPromptWinsOverPositional", "--prompt takes precedence over positionals", (CancellationToken ct) =>
                    {
                        InteractiveSettings settings = CliArgumentParser.ParseInteractive(new[] { "--prompt", "explicit", "stray", "words" });
                        MuxAssert.AreEqual("explicit", settings.Prompt, "explicit --prompt is not overwritten by positionals");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("HeadlessFeatures", "InteractiveNoPromptIsNull", "interactive with no prompt leaves Prompt null", (CancellationToken ct) =>
                    {
                        InteractiveSettings settings = CliArgumentParser.ParseInteractive(new[] { "--endpoint", "ollama" });
                        MuxAssert.IsNull(settings.Prompt, "no prompt supplied");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("HeadlessFeatures", "MaxTokenBudgetClampsAndClears", "MaxTokenBudget clamps to >=1 and preserves null", (CancellationToken ct) =>
                    {
                        AgentLoopOptions options = new AgentLoopOptions(AgentTestHarness.BuildMockEndpoint("http://localhost:1"));
                        MuxAssert.IsNull(options.MaxTokenBudget, "MaxTokenBudget defaults to null (off)");
                        options.MaxTokenBudget = 0;
                        MuxAssert.AreEqual(1, options.MaxTokenBudget ?? -1, "zero clamps to 1");
                        options.MaxTokenBudget = 500;
                        MuxAssert.AreEqual(500, options.MaxTokenBudget ?? -1, "in-range value preserved");
                        options.MaxTokenBudget = null;
                        MuxAssert.IsNull(options.MaxTokenBudget, "null clears the budget");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("HeadlessFeatures", "BudgetExceededStopsRun", "a tiny token budget stops the run before the model call", (CancellationToken ct) => BudgetExceededStopsRunAsync(ct)),

                    new TestCaseDescriptor("HeadlessFeatures", "BudgetNotExceededCompletes", "a generous token budget does not interfere with completion", (CancellationToken ct) => BudgetNotExceededCompletesAsync(ct)),

                    new TestCaseDescriptor("HeadlessFeatures", "FinalConversationCapturesTurn", "FinalConversation captures the user and assistant turn", (CancellationToken ct) => FinalConversationCapturesTurnAsync(ct)),

                    new TestCaseDescriptor("HeadlessFeatures", "ConversationHistoryReplayed", "prior conversation history is replayed into the run", (CancellationToken ct) => ConversationHistoryReplayedAsync(ct)),

                    new TestCaseDescriptor("HeadlessFeatures", "SessionIdSurfacedOnEvents", "session id is surfaced on run start and completion events", (CancellationToken ct) => SessionIdSurfacedOnEventsAsync(ct)),

                    new TestCaseDescriptor("HeadlessFeatures", "RunSummarySerializesFields", "FormatRunSummary emits the expected fields", (CancellationToken ct) =>
                    {
                        RunCompletedEvent completed = new RunCompletedEvent
                        {
                            RunId = "r1",
                            SessionId = "sess-1",
                            Status = "completed",
                            IterationsCompleted = 3,
                            ToolCallCount = 2,
                            ErrorCount = 0,
                            DurationMs = 42,
                            FinalEstimatedTokens = 128,
                            CompactionCount = 1
                        };

                        string json = StructuredOutputFormatter.FormatRunSummary(completed, "the answer", "sess-1");
                        MuxAssert.Contains("\"result\":\"the answer\"", json, "result present");
                        MuxAssert.Contains("\"status\":\"completed\"", json, "status present");
                        MuxAssert.Contains("\"sessionId\":\"sess-1\"", json, "sessionId present");
                        MuxAssert.Contains("\"contractVersion\":2", json, "contractVersion present");
                        MuxAssert.Contains("\"iterationsCompleted\":3", json, "iterationsCompleted present");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("HeadlessFeatures", "RunSummaryRedactsSecrets", "FormatRunSummary redacts secret-like text in the result", (CancellationToken ct) =>
                    {
                        string json = StructuredOutputFormatter.FormatRunSummary(new RunCompletedEvent { Status = "completed" }, "token is sk-ABC123DEF456", string.Empty);
                        MuxAssert.DoesNotContain("sk-ABC123DEF456", json, "secret is not present verbatim");
                        MuxAssert.Contains("REDACTED", json, "redaction marker present");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("HeadlessFeatures", "RunEventsSerializeSessionId", "run_started and run_completed serialize sessionId", (CancellationToken ct) =>
                    {
                        string started = StructuredOutputFormatter.FormatEvent(new RunStartedEvent { RunId = "r1", SessionId = "sess-9" });
                        string completed = StructuredOutputFormatter.FormatEvent(new RunCompletedEvent { RunId = "r1", SessionId = "sess-9", Status = "completed" });
                        MuxAssert.Contains("\"sessionId\":\"sess-9\"", started, "run_started carries sessionId");
                        MuxAssert.Contains("\"sessionId\":\"sess-9\"", completed, "run_completed carries sessionId");
                        return Task.CompletedTask;
                    })
                });
        }

        private static async Task BudgetExceededStopsRunAsync(CancellationToken ct)
        {
            using (MockHttpServer server = new MockHttpServer())
            {
                server.RegisterStreamingResponse("budget", new List<string> { AgentTestHarness.BuildTextSseChunk("should not be reached") });
                server.Start();

                List<ConversationMessage> history = new List<ConversationMessage>();
                for (int i = 0; i < 6; i++)
                {
                    history.Add(new ConversationMessage { Role = RoleEnum.User, Content = new string('u', 200) });
                    history.Add(new ConversationMessage { Role = RoleEnum.Assistant, Content = new string('a', 200) });
                }

                AgentLoopOptions options = new AgentLoopOptions(AgentTestHarness.BuildMockEndpoint(server.BaseUrl))
                {
                    ApprovalPolicy = ApprovalPolicyEnum.AutoApprove,
                    MaxIterations = 5,
                    ConversationHistory = history,
                    MaxTokenBudget = 1
                };

                List<AgentEvent> events = await AgentTestHarness.CollectEventsAsync(options, "budget", ct).ConfigureAwait(false);

                MuxAssert.IsTrue(
                    events.Any((AgentEvent e) => e is ErrorEvent error && error.Code == "budget_exceeded"),
                    "ErrorEvent with code 'budget_exceeded'");
                MuxAssert.IsTrue(
                    events.Any((AgentEvent e) => e is RunCompletedEvent completed && completed.Status == "budget_exceeded"),
                    "RunCompletedEvent status 'budget_exceeded'");
                MuxAssert.IsFalse(
                    events.Any((AgentEvent e) => e is AssistantTextEvent),
                    "no assistant text because the model was never called");
            }
        }

        private static async Task BudgetNotExceededCompletesAsync(CancellationToken ct)
        {
            using (MockHttpServer server = new MockHttpServer())
            {
                server.RegisterStreamingResponse("budget ok", new List<string> { AgentTestHarness.BuildTextSseChunk("Budget respected.") });
                server.Start();

                AgentLoopOptions options = new AgentLoopOptions(AgentTestHarness.BuildMockEndpoint(server.BaseUrl))
                {
                    ApprovalPolicy = ApprovalPolicyEnum.AutoApprove,
                    MaxIterations = 5,
                    MaxTokenBudget = 10_000_000
                };

                List<AgentEvent> events = await AgentTestHarness.CollectEventsAsync(options, "budget ok", ct).ConfigureAwait(false);

                MuxAssert.IsFalse(
                    events.Any((AgentEvent e) => e is ErrorEvent error && error.Code == "budget_exceeded"),
                    "no budget_exceeded under a generous budget");
                MuxAssert.IsTrue(events.Any((AgentEvent e) => e is AssistantTextEvent), "assistant text produced");
            }
        }

        private static async Task FinalConversationCapturesTurnAsync(CancellationToken ct)
        {
            using (MockHttpServer server = new MockHttpServer())
            {
                server.RegisterStreamingResponse("capture", new List<string> { AgentTestHarness.BuildTextSseChunk("Captured.") });
                server.Start();

                AgentLoopOptions options = new AgentLoopOptions(AgentTestHarness.BuildMockEndpoint(server.BaseUrl))
                {
                    ApprovalPolicy = ApprovalPolicyEnum.AutoApprove,
                    MaxIterations = 5
                };

                using (AgentLoop loop = new AgentLoop(options))
                {
                    await foreach (AgentEvent _ in loop.RunAsync("capture", ct).ConfigureAwait(false))
                    {
                    }

                    MuxAssert.IsTrue(loop.FinalConversation.Count >= 2, "final conversation retains the turn");
                    MuxAssert.IsTrue(
                        loop.FinalConversation.Any((ConversationMessage m) => m.Role == RoleEnum.User && m.Content == "capture"),
                        "user prompt captured");
                    MuxAssert.IsTrue(
                        loop.FinalConversation.Any((ConversationMessage m) => m.Role == RoleEnum.Assistant),
                        "assistant reply captured");
                }
            }
        }

        private static async Task ConversationHistoryReplayedAsync(CancellationToken ct)
        {
            using (MockHttpServer server = new MockHttpServer())
            {
                server.RegisterStreamingResponse("second turn", new List<string> { AgentTestHarness.BuildTextSseChunk("Continued.") });
                server.Start();

                List<ConversationMessage> history = new List<ConversationMessage>
                {
                    new ConversationMessage { Role = RoleEnum.User, Content = "first turn" },
                    new ConversationMessage { Role = RoleEnum.Assistant, Content = "prior answer" }
                };

                AgentLoopOptions options = new AgentLoopOptions(AgentTestHarness.BuildMockEndpoint(server.BaseUrl))
                {
                    ApprovalPolicy = ApprovalPolicyEnum.AutoApprove,
                    MaxIterations = 5,
                    ConversationHistory = history
                };

                using (AgentLoop loop = new AgentLoop(options))
                {
                    await foreach (AgentEvent _ in loop.RunAsync("second turn", ct).ConfigureAwait(false))
                    {
                    }

                    MuxAssert.IsTrue(
                        loop.FinalConversation.Any((ConversationMessage m) => m.Content == "first turn"),
                        "prior user turn replayed into the conversation");
                    MuxAssert.IsTrue(
                        loop.FinalConversation.Any((ConversationMessage m) => m.Content == "prior answer"),
                        "prior assistant turn replayed into the conversation");
                    MuxAssert.IsTrue(
                        loop.FinalConversation.Any((ConversationMessage m) => m.Content == "second turn"),
                        "new user turn appended after the prior history");
                }
            }
        }

        private static async Task SessionIdSurfacedOnEventsAsync(CancellationToken ct)
        {
            using (MockHttpServer server = new MockHttpServer())
            {
                server.RegisterStreamingResponse("session", new List<string> { AgentTestHarness.BuildTextSseChunk("With session.") });
                server.Start();

                AgentLoopOptions options = new AgentLoopOptions(AgentTestHarness.BuildMockEndpoint(server.BaseUrl))
                {
                    ApprovalPolicy = ApprovalPolicyEnum.AutoApprove,
                    MaxIterations = 5,
                    SessionId = "sess-42"
                };

                List<AgentEvent> events = await AgentTestHarness.CollectEventsAsync(options, "session", ct).ConfigureAwait(false);

                RunStartedEvent? started = events.OfType<RunStartedEvent>().FirstOrDefault();
                RunCompletedEvent? completed = events.OfType<RunCompletedEvent>().FirstOrDefault();
                MuxAssert.IsNotNull(started, "run_started emitted");
                MuxAssert.IsNotNull(completed, "run_completed emitted");
                MuxAssert.AreEqual("sess-42", started!.SessionId, "run_started carries the session id");
                MuxAssert.AreEqual("sess-42", completed!.SessionId, "run_completed carries the session id");
            }
        }
    }
}
