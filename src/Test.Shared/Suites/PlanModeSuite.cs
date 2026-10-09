namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.IO;
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
    using Mux.Core.Tasks;
    using Mux.Core.Tools;
    using Touchstone.Core;
    using TUIKit.Terminal;

    /// <summary>
    /// Touchstone suite for plan mode (Phase 6, row 25): the <c>exit_plan</c> tool and its review paths, the read-only
    /// posture in plan mode (mutating tools hidden and refused) and normal execution afterwards, plan steps becoming
    /// tasks, the terminal's <c>/plan</c>, Shift+Tab mode cycling, and the approve-then-execute flow in a headless
    /// shell, and <c>mux print --plan</c> printing the plan as the result. Positive and negative cases throughout.
    /// </summary>
    public static class PlanModeSuite
    {
        #region Private-Members

        private const string SuiteId = "PlanMode";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Builds the plan mode suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/> for the plan mode cases.</returns>
        public static TestSuiteDescriptor Create()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();
            void Add(string id, string name, Func<CancellationToken, Task> body)
            {
                cases.Add(new TestCaseDescriptor(SuiteId, id, name, body));
            }

            // --- exit_plan tool ---
            Add("ExitPlanOnlyInPlanMode", "exit_plan is offered and accepted only in plan mode; ask_user always is", async (CancellationToken ct) =>
            {
                InteractionToolProvider normal = new InteractionToolProvider(null, null, false);
                List<string> normalNames = Names(normal);
                MuxAssert.AreEqual("ask_user", string.Join(",", normalNames), "only ask_user outside plan mode");
                MuxAssert.IsFalse(normal.HasTool("exit_plan"), "exit_plan not claimed");
                ToolResult refused = await normal.ExecuteAsync("exit_plan", Json("{\"plan\":\"x\"}"), ".", ct).ConfigureAwait(false);
                MuxAssert.Contains("not_in_plan_mode", refused.Content, "refused outside plan mode");
                InteractionToolProvider plan = new InteractionToolProvider(null, null, true);
                MuxAssert.AreEqual("ask_user,exit_plan", string.Join(",", Names(plan)), "both in plan mode");
                MuxAssert.IsTrue(plan.HasTool("EXIT_PLAN"), "case-insensitive");
                MuxAssert.AreEqual(ToolMutationKind.ReadOnly, plan.GetMutationKind("exit_plan"), "read-only");
                ToolResult unknown = await plan.ExecuteAsync("other", Json("{}"), ".", ct).ConfigureAwait(false);
                MuxAssert.Contains("unknown_tool", unknown.Content, "unknown tool");
            });
            Add("ExitPlanReviewPaths", "Approve, approve with auto-accept, keep planning, no reviewer, and a timed-out review produce the right results", async (CancellationToken ct) =>
            {
                string args = "{\"plan\":\"# Plan\\nRefactor the parser.\",\"steps\":[\"Write tests\",\" \",42,\"Refactor\"]}";
                InteractionToolProvider approve = new InteractionToolProvider(null, (PlanProposal p, CancellationToken t) => Task.FromResult(new PlanReview { Decision = PlanReviewDecisionEnum.Approve }), true);
                ToolResult approved = await approve.ExecuteAsync("exit_plan", Json(args), ".", ct).ConfigureAwait(false);
                MuxAssert.Contains("\"approved\":true", approved.Content, "approved");
                MuxAssert.Contains("\"auto_accept\":false", approved.Content, "not auto-accept");
                MuxAssert.AreEqual("Write tests,Refactor", string.Join(",", approve.LastProposal!.Steps), "blank and non-string steps dropped");
                MuxAssert.IsTrue(approve.LastReview!.Approved, "review kept");

                InteractionToolProvider auto = new InteractionToolProvider(null, (PlanProposal p, CancellationToken t) => Task.FromResult(new PlanReview { Decision = PlanReviewDecisionEnum.ApproveAutoAccept }), true);
                MuxAssert.Contains("\"auto_accept\":true", (await auto.ExecuteAsync("exit_plan", Json(args), ".", ct).ConfigureAwait(false)).Content, "auto-accept");

                InteractionToolProvider keep = new InteractionToolProvider(null, (PlanProposal p, CancellationToken t) => Task.FromResult(new PlanReview { Decision = PlanReviewDecisionEnum.KeepPlanning, Feedback = "Add a rollback step." }), true);
                ToolResult kept = await keep.ExecuteAsync("exit_plan", Json(args), ".", ct).ConfigureAwait(false);
                MuxAssert.Contains("\"approved\":false", kept.Content, "not approved");
                MuxAssert.Contains("Add a rollback step.", kept.Content, "feedback returned");
                MuxAssert.Contains("call exit_plan again", kept.Content, "asks for a revision");

                InteractionToolProvider none = new InteractionToolProvider(null, null, true);
                ToolResult recorded = await none.ExecuteAsync("exit_plan", Json(args), ".", ct).ConfigureAwait(false);
                MuxAssert.Contains("\"recorded\":true", recorded.Content, "recorded without a reviewer");
                MuxAssert.Contains("do not carry it out", recorded.Content, "told to stop");
                MuxAssert.IsNotNull(none.LastProposal, "proposal kept");
                MuxAssert.IsNull(none.LastReview, "no review");

                InteractionToolProvider slow = new InteractionToolProvider(null, (PlanProposal p, CancellationToken t) => throw new OperationCanceledException(), true);
                ToolResult timedOut = await slow.ExecuteAsync("exit_plan", Json(args), ".", ct).ConfigureAwait(false);
                MuxAssert.Contains("The review timed out.", timedOut.Content, "timeout keeps planning");
            });
            Add("ExitPlanRejectsBlankPlan", "exit_plan without a plan is rejected and nothing is recorded", async (CancellationToken ct) =>
            {
                InteractionToolProvider provider = new InteractionToolProvider(null, null, true);
                MuxAssert.Contains("invalid_arguments", (await provider.ExecuteAsync("exit_plan", Json("{\"plan\":\"   \"}"), ".", ct).ConfigureAwait(false)).Content, "blank plan");
                MuxAssert.Contains("invalid_arguments", (await provider.ExecuteAsync("exit_plan", Json("{}"), ".", ct).ConfigureAwait(false)).Content, "missing plan");
                MuxAssert.Contains("invalid_arguments", (await provider.ExecuteAsync("exit_plan", Json("[]"), ".", ct).ConfigureAwait(false)).Content, "not an object");
                MuxAssert.IsNull(provider.LastProposal, "nothing recorded");
            });
            Add("PlanStepsBecomeTasks", "Plan steps become pending tasks that the task plan accepts, and the execution prompt carries them", (CancellationToken ct) =>
            {
                PlanProposal proposal = new PlanProposal { Plan = "# Plan", Steps = new List<string> { "Write tests", "Refactor", "Verify" } };
                List<AgentTask> tasks = proposal.ToTasks();
                MuxAssert.AreEqual("1,2,3", string.Join(",", tasks.ConvertAll(t => t.Id)), "ids");
                MuxAssert.AreEqual("Refactor", tasks[1].Title, "titles");
                TaskPlan plan = new TaskPlan();
                plan.SetPlan(tasks);
                MuxAssert.AreEqual(3, plan.TotalCount, "task plan accepts them");
                string prompt = proposal.ToExecutionPrompt();
                MuxAssert.Contains("The plan below was approved.", prompt, "execution header");
                MuxAssert.Contains("Steps:\n1. Write tests\n2. Refactor\n3. Verify", prompt, "numbered steps");
                MuxAssert.AreEqual(0, new PlanProposal { Plan = "x" }.ToTasks().Count, "no steps, no tasks");
                MuxAssert.AreEqual("x", new PlanProposal { Plan = " x " }.ToText(), "no steps section without steps");
                return Task.CompletedTask;
            });

            // --- agent loop posture ---
            Add("PlanModeRefusesMutatingTools", "In plan mode write_file is not offered, a proposed write is refused, and the plan-mode guidance is added", async (CancellationToken ct) =>
            {
                string dir = TempDir();
                try
                {
                    using (MockHttpServer server = new MockHttpServer())
                    {
                        string target = Path.Combine(dir, "should-not-exist.txt");
                        server.RegisterStreamingResponse("zp1", new List<string> { ToolCallChunk("write_file", JsonSerializer.Serialize(new { file_path = target, content = "x" })) });
                        server.RegisterStreamingResponse("read-only sandbox posture", new List<string> { AgentTestHarness.BuildTextSseChunk("Understood.") });
                        server.Start();
                        AgentLoopOptions options = new AgentLoopOptions(AgentTestHarness.BuildMockEndpoint(server.BaseUrl))
                        {
                            ApprovalPolicy = ApprovalPolicyEnum.AutoApprove,
                            WorkingDirectory = dir,
                            MaxIterations = 4,
                            PlanMode = true,
                            SystemPrompt = "BASE"
                        };
                        List<AgentEvent> events = await AgentTestHarness.CollectEventsAsync(options, "zp1 change the file", ct).ConfigureAwait(false);
                        MuxAssert.IsFalse(File.Exists(target), "the file was not written");
                        MuxAssert.IsTrue(events.Exists(e => e is ErrorEvent error && error.Code == "tool_call_denied" && error.Message.Contains("read-only")), "refused by the posture");
                        MuxAssert.AreEqual(SandboxPostureEnum.ReadOnly, options.SandboxPosture, "posture forced");
                        MuxAssert.Contains("# Plan mode", options.SystemPrompt ?? string.Empty, "guidance added");
                        string firstRequest = server.ReceivedRequests[0];
                        MuxAssert.DoesNotContain("\"write_file\"", firstRequest, "write_file not offered");
                        MuxAssert.Contains("\"exit_plan\"", firstRequest, "exit_plan offered");
                        MuxAssert.Contains("\"read_file\"", firstRequest, "read-only tools still offered");
                    }
                }
                finally
                {
                    TryDelete(dir);
                }
            });
            Add("NormalModeAllowsWritesAndHidesExitPlan", "Outside plan mode write_file runs and exit_plan is not offered", async (CancellationToken ct) =>
            {
                string dir = TempDir();
                try
                {
                    using (MockHttpServer server = new MockHttpServer())
                    {
                        string target = Path.Combine(dir, "written.txt");
                        server.RegisterStreamingResponse("zp2", new List<string> { ToolCallChunk("write_file", JsonSerializer.Serialize(new { file_path = target, content = "hello" })) });
                        server.RegisterStreamingResponse("written.txt\\\"", new List<string> { AgentTestHarness.BuildTextSseChunk("Done.") });
                        server.Start();
                        AgentLoopOptions options = new AgentLoopOptions(AgentTestHarness.BuildMockEndpoint(server.BaseUrl))
                        {
                            ApprovalPolicy = ApprovalPolicyEnum.AutoApprove,
                            WorkingDirectory = dir,
                            MaxIterations = 3,
                            SystemPrompt = "BASE"
                        };
                        await AgentTestHarness.CollectEventsAsync(options, "zp2 write it", ct).ConfigureAwait(false);
                        MuxAssert.IsTrue(File.Exists(target), "the file was written");
                        MuxAssert.DoesNotContain("\"exit_plan\"", server.ReceivedRequests[0], "exit_plan not offered");
                        MuxAssert.Contains("\"ask_user\"", server.ReceivedRequests[0], "ask_user offered");
                        MuxAssert.DoesNotContain("# Plan mode", options.SystemPrompt ?? string.Empty, "no plan guidance");
                    }
                }
                finally
                {
                    TryDelete(dir);
                }
            });
            Add("KeepPlanningFeedbackReachesModel", "Keep-planning feedback from the reviewer is returned to the model in the same turn", async (CancellationToken ct) =>
            {
                using (MockHttpServer server = new MockHttpServer())
                {
                    server.RegisterStreamingResponse("zp3", new List<string> { ToolCallChunk("exit_plan", JsonSerializer.Serialize(new { plan = "Step one only", steps = new[] { "one" } })) });
                    server.RegisterStreamingResponse("please add a rollback step", new List<string> { AgentTestHarness.BuildTextSseChunk("Revising the plan.") });
                    server.Start();
                    int reviews = 0;
                    AgentLoopOptions options = new AgentLoopOptions(AgentTestHarness.BuildMockEndpoint(server.BaseUrl))
                    {
                        ApprovalPolicy = ApprovalPolicyEnum.Deny,
                        MaxIterations = 4,
                        PlanMode = true,
                        ReviewPlanFunc = (PlanProposal p, CancellationToken t) =>
                        {
                            reviews++;
                            return Task.FromResult(new PlanReview { Decision = PlanReviewDecisionEnum.KeepPlanning, Feedback = "please add a rollback step" });
                        }
                    };
                    List<AgentEvent> events = await AgentTestHarness.CollectEventsAsync(options, "zp3 plan it", ct).ConfigureAwait(false);
                    MuxAssert.AreEqual(1, reviews, "reviewed once (exit_plan bypasses the Deny policy)");
                    MuxAssert.Contains("please add a rollback step", server.ReceivedRequests[server.ReceivedRequests.Count - 1], "feedback sent to the model");
                    MuxAssert.IsTrue(events.Exists(e => e is AssistantTextEvent text && text.Text.Contains("Revising")), "model continued");
                }
            });

            // --- terminal ---
            Add("TerminalModeCyclingAndPlanCommand", "Shift+Tab and CycleInteractionMode cycle normal, auto-approve, and plan; /plan toggles; /plan <prompt> runs in plan mode", async (CancellationToken ct) =>
            {
                HeadlessBackend backend = new HeadlessBackend(160, 40);
                await using (JobManager manager = new JobManager(ModeEchoRunner, maxConcurrency: 1))
                using (MuxTuiApp app = new MuxTuiApp(backend, manager, "demo", ApprovalPolicyEnum.Ask))
                {
                    MuxAssert.AreEqual(InteractionModeEnum.Normal, app.InteractionMode, "starts normal");
                    app.CycleInteractionMode();
                    MuxAssert.AreEqual(InteractionModeEnum.AutoApprove, app.InteractionMode, "then auto-approve");
                    app.CycleInteractionMode();
                    MuxAssert.AreEqual(InteractionModeEnum.Plan, app.InteractionMode, "then plan");
                    app.CycleInteractionMode();
                    MuxAssert.AreEqual(InteractionModeEnum.Normal, app.InteractionMode, "back to normal");
                    backend.FeedInput("\u001b[Z");
                    app.PumpInputOnce();
                    MuxAssert.AreEqual(InteractionModeEnum.AutoApprove, app.InteractionMode, "Shift+Tab cycles");
                    Submit(backend, app, "auto turn");
                    await app.DrainProjectorsAsync().ConfigureAwait(false);
                    app.SetInteractionMode(InteractionModeEnum.Normal);
                    Submit(backend, app, "/plan");
                    MuxAssert.AreEqual(InteractionModeEnum.Plan, app.InteractionMode, "/plan enters");
                    Submit(backend, app, "/plan");
                    MuxAssert.AreEqual(InteractionModeEnum.Normal, app.InteractionMode, "/plan leaves");
                    Submit(backend, app, "/plan design the cache");
                    await app.DrainProjectorsAsync().ConfigureAwait(false);
                    MuxAssert.AreEqual(InteractionModeEnum.Plan, app.InteractionMode, "/plan <prompt> enters");
                    string transcript = string.Join("\n", app.TranscriptSnapshot());
                    MuxAssert.Contains("Echo[plan=False policy=AutoApprove]: auto turn", transcript, "auto-approve turn");
                    MuxAssert.Contains("Echo[plan=True policy=Ask]: design the cache", transcript, "plan turn");
                    MuxAssert.Contains("Mode: plan.", transcript, "mode announced");
                }
            });
            Add("TerminalApprovalRunsThePlan", "Approving with auto-accept in the terminal leaves plan mode and runs the plan with its steps as tasks", async (CancellationToken ct) =>
            {
                HeadlessBackend backend = new HeadlessBackend(160, 40);
                MuxTuiApp? holder = null;
                List<string> seen = new List<string>();
                async IAsyncEnumerable<AgentEvent> Runner(Job job, string prompt, [EnumeratorCancellation] CancellationToken token)
                {
                    seen.Add("plan=" + job.PlanMode + " policy=" + job.ApprovalPolicy + " tasks=" + job.TaskPlan.TotalCount + " :: " + prompt.Split('\n')[0]);
                    if (prompt.Contains("PLANME", StringComparison.Ordinal))
                    {
                        PlanReview review = await holder!.ReviewPlanAsync(new PlanProposal { Plan = "Do the thing", Steps = new List<string> { "first", "second" } }, token).ConfigureAwait(false);
                        yield return new AssistantTextEvent { Text = "review=" + review.Decision };
                    }
                    else
                    {
                        yield return new AssistantTextEvent { Text = "executed" };
                    }

                    yield return new RunCompletedEvent { RunId = Guid.NewGuid().ToString("N"), Status = "completed", IterationsCompleted = 1, DurationMs = 1 };
                }

                await using (JobManager manager = new JobManager(Runner, maxConcurrency: 1))
                using (MuxTuiApp app = new MuxTuiApp(backend, manager, "demo", ApprovalPolicyEnum.Ask))
                {
                    holder = app;
                    app.SetInteractionMode(InteractionModeEnum.Plan);
                    Submit(backend, app, "PLANME now");
                    await WaitUntilAsync(() => seen.Count >= 1, ct).ConfigureAwait(false);
                    await Task.Delay(200, ct).ConfigureAwait(false);
                    backend.FeedInput("\r");
                    app.PumpInputOnce();
                    await WaitUntilAsync(() => seen.Count >= 2, ct).ConfigureAwait(false);
                    await app.DrainProjectorsAsync().ConfigureAwait(false);
                    MuxAssert.Contains("plan=True policy=Ask tasks=0 :: PLANME now", seen[0], "first turn in plan mode");
                    MuxAssert.Contains("plan=False policy=AutoApprove tasks=2 :: The plan below was approved.", seen[1], "execution turn: auto-approve, seeded tasks");
                    MuxAssert.AreEqual(InteractionModeEnum.AutoApprove, app.InteractionMode, "left plan mode");
                    string transcript = string.Join("\n", app.TranscriptSnapshot());
                    MuxAssert.Contains("Proposed plan", transcript, "plan shown");
                    MuxAssert.Contains("Plan approved. Carrying it out with edits auto-approved.", transcript, "approval announced");
                }
            });
            Add("TerminalKeepPlanningSendsFeedback", "Choosing keep planning in the terminal returns the typed feedback and stays in plan mode", async (CancellationToken ct) =>
            {
                HeadlessBackend backend = new HeadlessBackend(160, 40);
                await using (JobManager manager = new JobManager(ModeEchoRunner, maxConcurrency: 1))
                using (MuxTuiApp app = new MuxTuiApp(backend, manager, "demo", ApprovalPolicyEnum.Ask))
                {
                    app.SetInteractionMode(InteractionModeEnum.Plan);
                    Task<PlanReview> review = app.ReviewPlanAsync(new PlanProposal { Plan = "p" }, CancellationToken.None);
                    backend.FeedInput("\u001b[B\u001b[B\r");
                    app.PumpInputOnce();
                    await Task.Delay(100, ct).ConfigureAwait(false);
                    backend.FeedInput("cover the edge cases\r");
                    app.PumpInputOnce();
                    PlanReview result = await review.WaitAsync(TimeSpan.FromSeconds(10), ct).ConfigureAwait(false);
                    MuxAssert.AreEqual(PlanReviewDecisionEnum.KeepPlanning, result.Decision, "keep planning");
                    MuxAssert.AreEqual("cover the edge cases", result.Feedback, "feedback typed");
                    MuxAssert.AreEqual(InteractionModeEnum.Plan, app.InteractionMode, "still planning");

                    using (CancellationTokenSource cancel = new CancellationTokenSource())
                    {
                        Task<PlanReview> cancelled = app.ReviewPlanAsync(new PlanProposal { Plan = "p" }, cancel.Token);
                        cancel.Cancel();
                        PlanReview closed = await cancelled.WaitAsync(TimeSpan.FromSeconds(10), ct).ConfigureAwait(false);
                        MuxAssert.IsFalse(closed.Approved, "cancelling the turn closes the review unapproved");
                    }
                }
            });

            // --- mux print --plan ---
            Add("PrintPlanPrintsThePlan", "mux print --plan explores read-only and prints the plan as the result without executing it", (CancellationToken ct) =>
            {
                string configDir = TempDir();
                try
                {
                    File.WriteAllText(Path.Combine(configDir, "settings.json"), "{\"skillsEnabled\":false}");
                    using (MockHttpServer server = new MockHttpServer())
                    {
                        server.RegisterStreamingResponse("zp4", new List<string> { ToolCallChunk("exit_plan", JsonSerializer.Serialize(new { plan = "## Cache plan\nAdd an LRU cache.", steps = new[] { "Add the cache", "Test eviction" } })) });
                        server.RegisterStreamingResponse("No user is available to review the plan", new List<string> { AgentTestHarness.BuildTextSseChunk("Plan recorded.") });
                        server.Start();
                        CliInvocationResult text = InvokeCli(new[] { "print", "--config-dir", configDir, "--plan", "--base-url", server.BaseUrl, "--model", "m", "--adapter-type", "openai-compatible", "zp4 plan a cache" });
                        MuxAssert.AreEqual(0, text.ExitCode, "exit 0: " + text.StdErr);
                        MuxAssert.Contains("## Cache plan", text.StdOut, "plan printed");
                        MuxAssert.Contains("Steps:\n1. Add the cache\n2. Test eviction", text.StdOut.Replace("\r\n", "\n"), "steps printed");
                        CliInvocationResult json = InvokeCli(new[] { "print", "--config-dir", configDir, "--plan", "--output-format", "json", "--base-url", server.BaseUrl, "--model", "m", "--adapter-type", "openai-compatible", "zp4 plan a cache" });
                        MuxAssert.AreEqual(0, json.ExitCode, "json exit 0");
                        using (JsonDocument doc = JsonDocument.Parse(json.StdOut.Trim()))
                        {
                            MuxAssert.Contains("Add an LRU cache.", doc.RootElement.GetProperty("result").GetString() ?? string.Empty, "json result is the plan");
                        }
                    }
                }
                finally
                {
                    TryDelete(configDir);
                }

                return Task.CompletedTask;
            });

            return new TestSuiteDescriptor(SuiteId, "Plan mode: exit_plan, read-only posture, terminal flow, print --plan", cases);
        }

        #endregion

        #region Private-Methods

        private static List<string> Names(InteractionToolProvider provider)
        {
            return new List<ToolDefinition>(provider.GetToolDefinitions()).ConvertAll(t => t.Name);
        }

        private static JsonElement Json(string text)
        {
            using (JsonDocument document = JsonDocument.Parse(text))
            {
                return document.RootElement.Clone();
            }
        }

        private static string ToolCallChunk(string name, string argumentsJson)
        {
            return "{\"choices\":[{\"delta\":{\"tool_calls\":[{\"index\":0,\"id\":\"call_" + name + "\",\"function\":{\"name\":\"" + name + "\",\"arguments\":" + JsonSerializer.Serialize(argumentsJson) + "}}]},\"finish_reason\":\"tool_calls\"}]}";
        }

        private static void Submit(HeadlessBackend backend, MuxTuiApp app, string prompt)
        {
            backend.FeedInput(prompt + "\r");
            app.PumpInputOnce();
        }

        private static async IAsyncEnumerable<AgentEvent> ModeEchoRunner(Job job, string prompt, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask.ConfigureAwait(false);
            yield return new AssistantTextEvent { Text = "Echo[plan=" + job.PlanMode + " policy=" + job.ApprovalPolicy + "]: " + prompt.Split('\n')[0] };
            yield return new RunCompletedEvent { RunId = Guid.NewGuid().ToString("N"), Status = "completed", IterationsCompleted = 1, DurationMs = 1 };
        }

        private static async Task WaitUntilAsync(Func<bool> condition, CancellationToken ct)
        {
            for (int i = 0; i < 200 && !condition(); i++)
            {
                await Task.Delay(50, ct).ConfigureAwait(false);
            }

            MuxAssert.IsTrue(condition(), "condition reached in time");
        }

        private static string TempDir()
        {
            string dir = Path.Combine(Path.GetTempPath(), "mux-plan-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        private static void TryDelete(string dir)
        {
            try { Directory.Delete(dir, true); } catch (Exception) { }
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

        #endregion
    }
}
