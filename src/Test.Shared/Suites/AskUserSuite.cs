namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Runtime.CompilerServices;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Cli.App;
    using Mux.Core.Agent;
    using Mux.Core.Enums;
    using Mux.Core.Interaction;
    using Mux.Core.Jobs;
    using Mux.Core.Models;
    using Touchstone.Core;
    using TUIKit.Terminal;

    /// <summary>
    /// Touchstone suite for the <c>ask_user</c> tool (Phase 6, row 26): argument validation, answers by option, by
    /// free text ("Other"), and by several options, dismissal, timeouts and cancellation, the no-user default used by
    /// <c>mux print</c>, the tool running under a Deny approval policy inside the agent loop, and the terminal modal.
    /// Positive and negative cases throughout.
    /// </summary>
    public static class AskUserSuite
    {
        #region Private-Members

        private const string SuiteId = "AskUser";

        private const string ThreeOptions = "{\"question\":\"Which database?\",\"options\":[{\"label\":\"Postgres\",\"description\":\"Relational\"},{\"label\":\"SQLite\"},\"Mongo\"]}";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Builds the ask-user suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/> for the ask_user cases.</returns>
        public static TestSuiteDescriptor Create()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();
            void Add(string id, string name, Func<CancellationToken, Task> body)
            {
                cases.Add(new TestCaseDescriptor(SuiteId, id, name, body));
            }

            Add("ValidationRejectsBadQuestions", "A blank question, too few or too many options, blank or repeated labels, and non-objects are rejected", (CancellationToken ct) =>
            {
                Dictionary<string, string> bad = new Dictionary<string, string>
                {
                    ["blank question"] = "{\"question\":\"  \",\"options\":[\"a\",\"b\"]}",
                    ["no options"] = "{\"question\":\"q?\"}",
                    ["options not an array"] = "{\"question\":\"q?\",\"options\":\"a,b\"}",
                    ["one option"] = "{\"question\":\"q?\",\"options\":[\"a\"]}",
                    ["five options"] = "{\"question\":\"q?\",\"options\":[\"a\",\"b\",\"c\",\"d\",\"e\"]}",
                    ["blank label"] = "{\"question\":\"q?\",\"options\":[\"a\",{\"label\":\" \"}]}",
                    ["repeated label"] = "{\"question\":\"q?\",\"options\":[\"Same\",\"same\"]}",
                    ["not an object"] = "[1,2]"
                };
                foreach (KeyValuePair<string, string> pair in bad)
                {
                    MuxAssert.IsFalse(InteractionToolProvider.TryParseAskUser(Json(pair.Value), out _, out string error), pair.Key + " rejected");
                    MuxAssert.IsTrue(error.Length > 0, pair.Key + " explained");
                }

                MuxAssert.IsTrue(InteractionToolProvider.TryParseAskUser(Json(ThreeOptions), out AskUserRequest request, out string ok), "valid: " + ok);
                MuxAssert.AreEqual(3, request.Options.Count, "three options, strings accepted");
                MuxAssert.AreEqual("Relational", request.Options[0].Description, "description kept");
                MuxAssert.IsFalse(request.MultiSelect, "single select by default");
                MuxAssert.IsTrue(InteractionToolProvider.TryParseAskUser(Json("{\"question\":\"q?\",\"options\":[\"a\",\"b\",\"c\",\"d\"],\"multi_select\":true}"), out AskUserRequest four, out _), "four options and multi-select");
                MuxAssert.IsTrue(four.MultiSelect, "multi-select parsed");
                return Task.CompletedTask;
            });
            Add("InvalidCallReturnsError", "An invalid ask_user call returns invalid_arguments without asking anyone", async (CancellationToken ct) =>
            {
                int asked = 0;
                InteractionToolProvider provider = new InteractionToolProvider((AskUserRequest r, CancellationToken t) => { asked++; return Task.FromResult(AskUserResponse.Choose("a")); }, null, false);
                ToolResult result = await provider.ExecuteAsync("ask_user", Json("{\"question\":\"q?\",\"options\":[\"only\"]}"), ".", ct).ConfigureAwait(false);
                MuxAssert.IsFalse(result.Success, "failed");
                MuxAssert.Contains("invalid_arguments", result.Content, "code");
                MuxAssert.Contains("2 to 4", result.Content, "explains the range");
                MuxAssert.AreEqual(0, asked, "the user was not asked");
            });
            Add("NoUserDefault", "Without a user (mux print, no UI) ask_user tells the model to choose a default and state it", async (CancellationToken ct) =>
            {
                InteractionToolProvider provider = new InteractionToolProvider(null, null, false);
                ToolResult result = await provider.ExecuteAsync("ask_user", Json(ThreeOptions), ".", ct).ConfigureAwait(false);
                MuxAssert.IsTrue(result.Success, "succeeds");
                MuxAssert.Contains("\"answered\":false", result.Content, "not answered");
                MuxAssert.Contains("No user is available to answer; choose a sensible default, state the assumption, and continue.", result.Content, "default message");
            });
            Add("AnswersByOptionOtherAndSeveral", "Answers map to the option labels, free text comes back as other, and single select keeps one choice", async (CancellationToken ct) =>
            {
                ToolResult one = await Ask(AskUserResponse.Choose("sqlite"), ThreeOptions, ct).ConfigureAwait(false);
                MuxAssert.Contains("\"answers\":[\"SQLite\"]", one.Content, "label matched case-insensitively");
                MuxAssert.Contains("\"answered\":true", one.Content, "answered");
                ToolResult two = await Ask(AskUserResponse.Choose("Postgres", "Mongo"), ThreeOptions, ct).ConfigureAwait(false);
                MuxAssert.Contains("\"answers\":[\"Postgres\"]", two.Content, "single select keeps the first");
                ToolResult multi = await Ask(AskUserResponse.Choose("Postgres", "Mongo", "Nope"), ThreeOptions.Replace("\"options\"", "\"multi_select\":true,\"options\""), ct).ConfigureAwait(false);
                MuxAssert.Contains("\"answers\":[\"Postgres\",\"Mongo\"]", multi.Content, "multi-select keeps both and drops unknown labels");
                ToolResult other = await Ask(AskUserResponse.Other("  CockroachDB  "), ThreeOptions, ct).ConfigureAwait(false);
                MuxAssert.Contains("\"other\":\"CockroachDB\"", other.Content, "free text trimmed");
                MuxAssert.Contains("\"answers\":[]", other.Content, "no options chosen");
            });
            Add("DismissTimeoutAndCancel", "A dismissal or empty answer, a timeout, and cancelling the run are each reported correctly", async (CancellationToken ct) =>
            {
                ToolResult dismissed = await Ask(AskUserResponse.Dismiss(), ThreeOptions, ct).ConfigureAwait(false);
                MuxAssert.Contains("\"dismissed\":true", dismissed.Content, "dismissed");
                MuxAssert.Contains("Do not ask again", dismissed.Content, "do not re-ask");
                ToolResult empty = await Ask(new AskUserResponse(), ThreeOptions, ct).ConfigureAwait(false);
                MuxAssert.Contains("\"dismissed\":true", empty.Content, "empty answer counts as dismissed");
                InteractionToolProvider timeout = new InteractionToolProvider((AskUserRequest r, CancellationToken t) => throw new TimeoutException(), null, false);
                MuxAssert.Contains("\"timed_out\":true", (await timeout.ExecuteAsync("ask_user", Json(ThreeOptions), ".", ct).ConfigureAwait(false)).Content, "timeout");
                InteractionToolProvider inner = new InteractionToolProvider((AskUserRequest r, CancellationToken t) => throw new OperationCanceledException(), null, false);
                MuxAssert.Contains("\"timed_out\":true", (await inner.ExecuteAsync("ask_user", Json(ThreeOptions), ".", ct).ConfigureAwait(false)).Content, "an inner cancellation is a timeout");
                using (CancellationTokenSource run = new CancellationTokenSource())
                {
                    run.Cancel();
                    InteractionToolProvider cancelled = new InteractionToolProvider((AskUserRequest r, CancellationToken t) => throw new OperationCanceledException(t), null, false);
                    await MuxAssert.ThrowsAsync<OperationCanceledException>(() => cancelled.ExecuteAsync("ask_user", Json(ThreeOptions), ".", run.Token), "a cancelled run propagates").ConfigureAwait(false);
                }
            });
            Add("AgentLoopAsksEvenUnderDeny", "Inside the agent loop ask_user needs no approval (even under Deny) and the answer reaches the model", async (CancellationToken ct) =>
            {
                using (MockHttpServer server = new MockHttpServer())
                {
                    string call = "{\"choices\":[{\"delta\":{\"tool_calls\":[{\"index\":0,\"id\":\"call_ask\",\"function\":{\"name\":\"ask_user\",\"arguments\":" + JsonSerializer.Serialize(ThreeOptions) + "}}]},\"finish_reason\":\"tool_calls\"}]}";
                    server.RegisterStreamingResponse("zq9", new List<string> { call });
                    server.RegisterStreamingResponse("answered", new List<string> { AgentTestHarness.BuildTextSseChunk("Using Postgres.") });
                    server.Start();
                    AskUserRequest? seen = null;
                    AgentLoopOptions options = new AgentLoopOptions(AgentTestHarness.BuildMockEndpoint(server.BaseUrl))
                    {
                        ApprovalPolicy = ApprovalPolicyEnum.Deny,
                        MaxIterations = 3,
                        AskUserFunc = (AskUserRequest r, CancellationToken t) => { seen = r; return Task.FromResult(AskUserResponse.Choose("Postgres")); }
                    };
                    List<AgentEvent> events = await AgentTestHarness.CollectEventsAsync(options, "zq9 pick a database", ct).ConfigureAwait(false);
                    MuxAssert.IsNotNull(seen, "the user was asked");
                    MuxAssert.AreEqual("Which database?", seen!.Question, "question passed through");
                    MuxAssert.IsFalse(events.Exists(e => e is ErrorEvent error && error.Code == "tool_call_denied"), "not denied");
                    MuxAssert.Contains("Postgres", server.ReceivedRequests[server.ReceivedRequests.Count - 1], "answer sent to the model");
                    MuxAssert.IsTrue(events.Exists(e => e is AssistantTextEvent text && text.Text.Contains("Using Postgres.")), "model continued");
                }
            });
            Add("TerminalModalAnswers", "The terminal modal returns a chosen option, a typed Other answer, several options, and a dismissal", async (CancellationToken ct) =>
            {
                HeadlessBackend backend = new HeadlessBackend(160, 40);
                await using (JobManager manager = new JobManager(EchoRunner, maxConcurrency: 1))
                using (MuxTuiApp app = new MuxTuiApp(backend, manager, "demo", ApprovalPolicyEnum.Ask))
                {
                    InteractionToolProvider.TryParseAskUser(Json(ThreeOptions), out AskUserRequest single, out _);
                    AskUserResponse second = await Drive(app, backend, single, new[] { "\u001b[B\r" }, ct).ConfigureAwait(false);
                    MuxAssert.AreEqual("SQLite", string.Join(",", second.Selected), "second option");

                    AskUserResponse other = await Drive(app, backend, single, new[] { "\u001b[B\u001b[B\u001b[B\r", "Cockroach\r" }, ct).ConfigureAwait(false);
                    MuxAssert.AreEqual("Cockroach", other.OtherText, "typed answer");
                    MuxAssert.AreEqual(0, other.Selected.Count, "no option");

                    using (CancellationTokenSource turn = new CancellationTokenSource())
                    {
                        Task<AskUserResponse> pending = app.AskUserAsync(single, turn.Token);
                        await Task.Delay(50, ct).ConfigureAwait(false);
                        turn.Cancel();
                        AskUserResponse dismissed = await pending.WaitAsync(TimeSpan.FromSeconds(10), ct).ConfigureAwait(false);
                        MuxAssert.IsTrue(dismissed.Dismissed, "cancelling the turn closes the question as dismissed");
                    }

                    InteractionToolProvider.TryParseAskUser(Json(ThreeOptions.Replace("\"options\"", "\"multi_select\":true,\"options\"")), out AskUserRequest multi, out _);
                    AskUserResponse several = await Drive(app, backend, multi, new[] { " \u001b[B\u001b[B \r" }, ct).ConfigureAwait(false);
                    MuxAssert.AreEqual("Postgres,Mongo", string.Join(",", several.Selected), "two checked");

                    MuxAssert.Contains("? Which database?", string.Join("\n", app.TranscriptSnapshot()), "question echoed");
                    await MuxAssert.ThrowsAsync<ArgumentNullException>(() => app.AskUserAsync(null!, ct), "null request").ConfigureAwait(false);
                }
            });

            return new TestSuiteDescriptor(SuiteId, "ask_user: validation, answers, no-user default, agent loop, terminal modal", cases);
        }

        #endregion

        #region Private-Methods

        private static async Task<AskUserResponse> Drive(MuxTuiApp app, HeadlessBackend backend, AskUserRequest request, string[] inputs, CancellationToken ct)
        {
            Task<AskUserResponse> pending = app.AskUserAsync(request, CancellationToken.None);
            foreach (string input in inputs)
            {
                await Task.Delay(50, ct).ConfigureAwait(false);
                backend.FeedInput(input);
                app.PumpInputOnce();
            }

            return await pending.WaitAsync(TimeSpan.FromSeconds(10), ct).ConfigureAwait(false);
        }

        private static Task<ToolResult> Ask(AskUserResponse answer, string arguments, CancellationToken ct)
        {
            InteractionToolProvider provider = new InteractionToolProvider((AskUserRequest r, CancellationToken t) => Task.FromResult(answer), null, false);
            return provider.ExecuteAsync("ask_user", Json(arguments), ".", ct);
        }

        private static JsonElement Json(string text)
        {
            using (JsonDocument document = JsonDocument.Parse(text))
            {
                return document.RootElement.Clone();
            }
        }

        private static async IAsyncEnumerable<AgentEvent> EchoRunner(Job job, string prompt, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask.ConfigureAwait(false);
            yield return new AssistantTextEvent { Text = "Echo: " + prompt };
            yield return new RunCompletedEvent { RunId = Guid.NewGuid().ToString("N"), Status = "completed", IterationsCompleted = 1, DurationMs = 1 };
        }

        #endregion
    }
}
