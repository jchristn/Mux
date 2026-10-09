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
    using Mux.Core.Jobs;
    using Mux.Core.Models;
    using Mux.Core.Sessions;
    using Mux.Core.Tools;
    using Test.Shared.Support;
    using Touchstone.Core;
    using TUIKit.Terminal;

    /// <summary>
    /// Touchstone suite for harness-level loops (<c>/loop</c>): the <see cref="LoopCommand"/> parser, the
    /// <see cref="LoopScheduler"/> on a manual clock (fixed interval, self-paced, iteration cap, cancel mid-run,
    /// pause and resume, restore paused, overlap prevention and skipped fire times), the <c>schedule_next</c> tool,
    /// the headless <see cref="LoopDriver"/>, the loop settings, session persistence, the sidebar line, the tool
    /// binder, the terminal <c>/loop</c> and <c>/loops</c> commands, and <c>mux print --loop</c>. Positive and
    /// negative cases throughout.
    /// </summary>
    public static class LoopSchedulerSuite
    {
        #region Private-Members

        private const string SuiteId = "LoopScheduler";

        private static readonly DateTimeOffset Start = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Builds the loop scheduler suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/> for the loop cases.</returns>
        public static TestSuiteDescriptor Create()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();
            void Add(string id, string name, Action body)
            {
                cases.Add(new TestCaseDescriptor(SuiteId, id, name, (CancellationToken ct) => { body(); return Task.CompletedTask; }));
            }

            void AddAsync(string id, string name, Func<CancellationToken, Task> body)
            {
                cases.Add(new TestCaseDescriptor(SuiteId, id, name, body));
            }

            // --- LoopCommand parsing ---
            Add("ParsesFixedInterval", "/loop 5m <prompt> parses a fixed interval and the prompt", () =>
            {
                MuxAssert.IsTrue(LoopCommand.TryParse("5m check the deploy", out LoopCommand? command, out string error), "parses: " + error);
                MuxAssert.AreEqual(TimeSpan.FromMinutes(5), command!.Interval!.Value, "interval");
                MuxAssert.AreEqual("check the deploy", command.Prompt, "prompt");
                MuxAssert.IsNull(command.MaxIterations, "no cap requested");
            });
            Add("ParsesCompoundIntervalAndMax", "Compound intervals and --max parse in either order", () =>
            {
                MuxAssert.IsTrue(LoopCommand.TryParse("1h30m --max 3 summarize", out LoopCommand? a, out _), "interval then max");
                MuxAssert.AreEqual(5400.0, a!.Interval!.Value.TotalSeconds, "1h30m");
                MuxAssert.AreEqual(3, a.MaxIterations!.Value, "max");
                MuxAssert.IsTrue(LoopCommand.TryParse("--max 4 keep going", out LoopCommand? b, out _), "max without interval");
                MuxAssert.IsNull(b!.Interval, "self-paced");
                MuxAssert.AreEqual(4, b.MaxIterations!.Value, "max");
                MuxAssert.IsTrue(LoopCommand.TryParse("90S poll", out LoopCommand? c, out _), "upper-case unit");
                MuxAssert.AreEqual(90.0, c!.Interval!.Value.TotalSeconds, "90s");
            });
            Add("SelfPacedWithoutInterval", "A prompt with no interval is self-paced, even when it starts with a number", () =>
            {
                MuxAssert.IsTrue(LoopCommand.TryParse("keep fixing the failing tests", out LoopCommand? a, out _), "plain prompt");
                MuxAssert.IsNull(a!.Interval, "self-paced");
                MuxAssert.IsTrue(LoopCommand.TryParse("4k video check", out LoopCommand? b, out _), "number-like word");
                MuxAssert.IsNull(b!.Interval, "4k is not an interval");
                MuxAssert.AreEqual("4k video check", b.Prompt, "prompt kept whole");
            });
            Add("RejectsBadLoopRequests", "Missing prompts, bad caps, and impossible intervals are rejected with the usage line", () =>
            {
                foreach (string bad in new[] { string.Empty, "   ", "5m", "5m --max 2", "--max 0 go", "--max abc go", "--max", "0s go", "40d go" })
                {
                    MuxAssert.IsFalse(LoopCommand.TryParse(bad, out LoopCommand? command, out string error), "rejects '" + bad + "'");
                    MuxAssert.IsNull(command, "no command for '" + bad + "'");
                    MuxAssert.Contains("Usage: /loop", error, "usage shown for '" + bad + "'");
                }
            });
            Add("IntervalParseAndFormat", "Intervals parse and format compactly", () =>
            {
                MuxAssert.IsTrue(LoopCommand.TryParseInterval("1d2h", out TimeSpan day), "1d2h");
                MuxAssert.AreEqual(93600.0, day.TotalSeconds, "1d2h seconds");
                MuxAssert.IsFalse(LoopCommand.TryParseInterval("5", out _), "unitless rejected");
                MuxAssert.IsFalse(LoopCommand.TryParseInterval("5x", out _), "unknown unit rejected");
                MuxAssert.IsFalse(LoopCommand.TryParseInterval(null, out _), "null rejected");
                MuxAssert.AreEqual("1m30s", LoopCommand.FormatInterval(TimeSpan.FromSeconds(90)), "90s");
                MuxAssert.AreEqual("1h", LoopCommand.FormatInterval(TimeSpan.FromHours(1)), "1h");
                MuxAssert.AreEqual("1d1s", LoopCommand.FormatInterval(TimeSpan.FromSeconds(86401)), "1d1s");
                MuxAssert.AreEqual("0s", LoopCommand.FormatInterval(TimeSpan.FromSeconds(-5)), "negative is 0s");
            });

            // --- scheduler ---
            Add("CreateFiresImmediatelyAndNeverOverlaps", "A new loop is due at once, and a running loop is never due again", () =>
            {
                LoopScheduler s = NewScheduler(out _);
                LoopDefinition loop = s.Create("check", TimeSpan.FromMinutes(5));
                MuxAssert.AreEqual("L1", loop.Id, "first id");
                MuxAssert.AreEqual(1, s.GetDue().Count, "due at once");
                LoopDefinition? running = s.TryBegin(loop.Id);
                MuxAssert.IsNotNull(running, "began");
                MuxAssert.AreEqual(LoopStatusEnum.Running, running!.Status, "running");
                MuxAssert.AreEqual(1, running.IterationCount, "iteration counted");
                MuxAssert.AreEqual(0, s.GetDue().Count, "running loop is not due");
                MuxAssert.IsNull(s.TryBegin(loop.Id), "cannot begin twice");
                MuxAssert.IsNull(s.TryBegin("L99"), "unknown id");
                MuxAssert.IsTrue(s.HasLiveLoops(), "live while running");
            });
            Add("FixedIntervalSchedulesFromStart", "A fixed loop's next fire time is measured from when the iteration started", () =>
            {
                LoopScheduler s = NewScheduler(out ManualClock clock);
                LoopDefinition loop = s.Create("check", TimeSpan.FromMinutes(5));
                s.TryBegin(loop.Id);
                clock.Advance(TimeSpan.FromSeconds(40));
                LoopDefinition done = s.Complete(loop.Id)!;
                MuxAssert.AreEqual(LoopStatusEnum.Scheduled, done.Status, "scheduled");
                MuxAssert.AreEqual(Start.UtcDateTime.AddMinutes(5), done.NextFireUtc!.Value, "start plus interval");
                MuxAssert.AreEqual(0, s.GetDue().Count, "not due yet");
                MuxAssert.AreEqual(Start.UtcDateTime.AddMinutes(5), s.GetNextFireUtc()!.Value, "next fire reported");
                clock.Advance(TimeSpan.FromSeconds(260));
                MuxAssert.AreEqual(1, s.GetDue().Count, "due after the interval");
            });
            Add("OverrunSkipsMissedFireTimes", "An iteration that outlasts the interval skips missed fire times instead of bursting", () =>
            {
                LoopScheduler s = NewScheduler(out ManualClock clock);
                LoopDefinition loop = s.Create("check", TimeSpan.FromMinutes(1));
                s.TryBegin(loop.Id);
                clock.Advance(TimeSpan.FromSeconds(150));
                LoopDefinition done = s.Complete(loop.Id)!;
                MuxAssert.AreEqual(Start.UtcDateTime.AddMinutes(3), done.NextFireUtc!.Value, "next slot after now");
                MuxAssert.AreEqual(2, done.SkippedCount, "two slots skipped");
                MuxAssert.AreEqual(0, s.GetDue().Count, "no burst");
            });
            Add("IterationCapCompletes", "A loop completes at its iteration cap and never fires again", () =>
            {
                LoopScheduler s = NewScheduler(out ManualClock clock);
                LoopDefinition loop = s.Create("check", TimeSpan.FromMinutes(1), 2);
                RunIteration(s, loop.Id);
                clock.Advance(TimeSpan.FromMinutes(1));
                LoopDefinition done = RunIteration(s, loop.Id);
                MuxAssert.AreEqual(LoopStatusEnum.Completed, done.Status, "completed");
                MuxAssert.Contains("iteration cap (2)", done.StatusReason ?? string.Empty, "reason");
                MuxAssert.IsNull(done.NextFireUtc, "no next fire");
                clock.Advance(TimeSpan.FromHours(1));
                MuxAssert.AreEqual(0, s.GetDue().Count, "never due");
                MuxAssert.IsFalse(s.HasLiveLoops(), "nothing live");
                MuxAssert.AreEqual(0, s.Snapshot().Count, "finished loops are not persisted");
            });
            Add("SelfPacedStopsWithoutDecision", "A self-paced iteration that ends without schedule_next stops the loop", () =>
            {
                LoopScheduler s = NewScheduler(out _);
                LoopDefinition loop = s.Create("fix it", null);
                MuxAssert.IsTrue(loop.IsSelfPaced, "self-paced");
                LoopDefinition done = RunIteration(s, loop.Id);
                MuxAssert.AreEqual(LoopStatusEnum.Stopped, done.Status, "stopped");
                MuxAssert.Contains("without calling schedule_next", done.StatusReason ?? string.Empty, "reason");
            });
            Add("SelfPacedDelayAndStopDecisions", "schedule_next delays the next iteration or stops the loop", () =>
            {
                LoopScheduler s = NewScheduler(out ManualClock clock);
                LoopDefinition loop = s.Create("watch the build", null);
                s.TryBegin(loop.Id);
                MuxAssert.IsTrue(s.TryRecordDecision(null, 120, false, " build still running ", out string message), "delay recorded: " + message);
                MuxAssert.Contains("2m", message, "confirmation names the delay");
                clock.Advance(TimeSpan.FromSeconds(10));
                LoopDefinition next = s.Complete(loop.Id)!;
                MuxAssert.AreEqual(LoopStatusEnum.Scheduled, next.Status, "scheduled");
                MuxAssert.AreEqual(clock.UtcNow.AddSeconds(120), next.NextFireUtc!.Value, "now plus delay");
                MuxAssert.AreEqual("build still running", next.LastReason, "reason trimmed and kept");
                clock.Advance(TimeSpan.FromSeconds(120));
                s.TryBegin(loop.Id);
                MuxAssert.IsTrue(s.TryRecordDecision("l1", null, true, "green now", out _), "stop recorded (id is case-insensitive)");
                LoopDefinition stopped = s.Complete(loop.Id)!;
                MuxAssert.AreEqual(LoopStatusEnum.Stopped, stopped.Status, "stopped");
                MuxAssert.Contains("the model stopped the loop: green now", stopped.StatusReason ?? string.Empty, "stop reason");
            });
            Add("DecisionValidation", "schedule_next refuses bad delays, missing delays, fixed loops, and idle schedulers", () =>
            {
                LoopScheduler s = NewScheduler(out _);
                MuxAssert.IsFalse(s.TryRecordDecision(null, 60, false, "x", out string idle), "nothing running");
                MuxAssert.Contains("No self-paced loop iteration is running", idle, "idle message");
                LoopDefinition fixedLoop = s.Create("fixed", TimeSpan.FromMinutes(1));
                s.TryBegin(fixedLoop.Id);
                MuxAssert.IsFalse(s.TryRecordDecision(fixedLoop.Id, 60, false, "x", out string fixedMessage), "fixed loop refused");
                MuxAssert.Contains("not a running self-paced loop", fixedMessage, "fixed message");
                MuxAssert.IsFalse(s.TryRecordDecision("L42", 60, false, "x", out string unknown), "unknown id");
                MuxAssert.Contains("No loop has the id", unknown, "unknown message");
                LoopDefinition selfPaced = s.Create("self", null);
                s.TryBegin(selfPaced.Id);
                MuxAssert.IsFalse(s.TryRecordDecision(null, 10, false, "x", out string low), "below the minimum");
                MuxAssert.Contains("between 30 and 3600", low, "range message");
                MuxAssert.IsFalse(s.TryRecordDecision(null, 3601, false, "x", out _), "above the maximum");
                MuxAssert.IsFalse(s.TryRecordDecision(null, null, false, "x", out string missing), "no delay and no stop");
                MuxAssert.Contains("delay_seconds", missing, "missing delay message");
                LoopDefinition second = s.Create("self two", null);
                s.TryBegin(second.Id);
                MuxAssert.IsFalse(s.TryRecordDecision(null, 60, false, "x", out string ambiguous), "two running self-paced loops need an id");
                MuxAssert.Contains("pass loop_id", ambiguous, "ambiguity message");
                MuxAssert.IsTrue(s.TryRecordDecision(second.Id, 60, false, "x", out _), "explicit id works");
            });
            Add("CancelMidRunStopsAfterIteration", "Cancelling a running loop lets the iteration finish and nothing fires after it", () =>
            {
                LoopScheduler s = NewScheduler(out ManualClock clock);
                LoopDefinition loop = s.Create("check", TimeSpan.FromMinutes(1));
                s.TryBegin(loop.Id);
                MuxAssert.IsTrue(s.Cancel(loop.Id), "cancelled");
                LoopDefinition after = s.Complete(loop.Id)!;
                MuxAssert.AreEqual(LoopStatusEnum.Stopped, after.Status, "stays stopped");
                MuxAssert.AreEqual("cancelled", after.StatusReason, "reason");
                clock.Advance(TimeSpan.FromHours(1));
                MuxAssert.AreEqual(0, s.GetDue().Count, "never due");
                MuxAssert.IsFalse(s.Cancel(loop.Id), "second cancel is a no-op");
                MuxAssert.IsFalse(s.Cancel("nope"), "unknown id");
            });
            Add("CancelAllStopsEveryActiveLoop", "CancelAll stops scheduled, running, and paused loops", () =>
            {
                LoopScheduler s = NewScheduler(out _);
                LoopDefinition a = s.Create("a", TimeSpan.FromMinutes(1));
                LoopDefinition b = s.Create("b", null);
                LoopDefinition c = s.Create("c", TimeSpan.FromMinutes(2));
                s.TryBegin(b.Id);
                s.Pause(c.Id);
                MuxAssert.AreEqual(3, s.CancelAll(), "three stopped");
                MuxAssert.AreEqual(0, s.CancelAll(), "nothing left");
                MuxAssert.IsFalse(s.HasLiveLoops(), "nothing live");
                MuxAssert.AreEqual(LoopStatusEnum.Stopped, s.Get(a.Id)!.Status, "a stopped");
            });
            Add("FailedIterationPausesAndResumes", "A failed or cancelled turn pauses the loop; resume fires it at once", () =>
            {
                LoopScheduler s = NewScheduler(out _);
                LoopDefinition loop = s.Create("check", TimeSpan.FromMinutes(10));
                s.TryBegin(loop.Id);
                LoopDefinition paused = s.Complete(loop.Id, failed: true)!;
                MuxAssert.AreEqual(LoopStatusEnum.Paused, paused.Status, "paused");
                MuxAssert.Contains("failed or was cancelled", paused.StatusReason ?? string.Empty, "reason");
                MuxAssert.IsFalse(s.HasLiveLoops(), "a paused loop is not live");
                MuxAssert.IsTrue(s.Resume(loop.Id), "resumed");
                MuxAssert.AreEqual(1, s.GetDue().Count, "due immediately after resume");
            });
            Add("PauseAndResumeRules", "Only scheduled loops pause and only paused loops resume", () =>
            {
                LoopScheduler s = NewScheduler(out _);
                LoopDefinition loop = s.Create("check", TimeSpan.FromMinutes(1));
                MuxAssert.IsFalse(s.Resume(loop.Id), "a scheduled loop is not resumed");
                MuxAssert.IsTrue(s.Pause(loop.Id), "paused");
                MuxAssert.AreEqual(0, s.GetDue().Count, "paused loop is not due");
                MuxAssert.IsFalse(s.Pause(loop.Id), "already paused");
                MuxAssert.IsTrue(s.Resume(loop.Id), "resumed");
                s.TryBegin(loop.Id);
                MuxAssert.IsFalse(s.Pause(loop.Id), "a running loop cannot be paused");
                MuxAssert.IsFalse(s.Pause("L9"), "unknown id");
                MuxAssert.IsFalse(s.Resume("L9"), "unknown id");
            });
            Add("ResumeAtCapCompletes", "Resuming a paused loop that already used its cap completes it", () =>
            {
                LoopScheduler s = NewScheduler(out _);
                LoopDefinition loop = s.Create("check", TimeSpan.FromMinutes(1), 1);
                s.TryBegin(loop.Id);
                s.Complete(loop.Id, failed: true);
                MuxAssert.IsTrue(s.Resume(loop.Id), "resume handled");
                MuxAssert.AreEqual(LoopStatusEnum.Completed, s.Get(loop.Id)!.Status, "completed instead of firing");
            });
            Add("RestoreBringsLoopsBackPaused", "Restored loops come back paused, finished ones are dropped, and ids continue", () =>
            {
                LoopScheduler original = NewScheduler(out _);
                LoopDefinition a = original.Create("a", TimeSpan.FromMinutes(5));
                LoopDefinition b = original.Create("b", null);
                original.TryBegin(b.Id);
                LoopDefinition c = original.Create("c", TimeSpan.FromMinutes(5));
                original.Cancel(c.Id);
                List<LoopDefinition> saved = original.Snapshot();
                MuxAssert.AreEqual(2, saved.Count, "only active loops persist");
                saved.Add(new LoopDefinition { Id = "L9", Prompt = "done", Status = LoopStatusEnum.Completed });
                saved.Add(new LoopDefinition { Id = string.Empty, Prompt = "no id" });
                saved.Add(null!);

                LoopScheduler restored = NewScheduler(out _);
                MuxAssert.AreEqual(2, restored.Restore(saved), "two restored");
                foreach (LoopDefinition loop in restored.List())
                {
                    MuxAssert.AreEqual(LoopStatusEnum.Paused, loop.Status, loop.Id + " paused");
                    MuxAssert.Contains("restored with the session", loop.StatusReason ?? string.Empty, loop.Id + " reason");
                }

                MuxAssert.AreEqual(0, restored.GetDue().Count, "nothing runs on its own");
                MuxAssert.AreEqual("L3", restored.Create("new", null).Id, "ids continue after the highest restored id");
                MuxAssert.AreEqual(0, restored.Restore(null), "null clears");
                MuxAssert.AreEqual(0, restored.List().Count, "cleared");
            });
            Add("CreateValidation", "Create rejects blank prompts, short intervals, and zero caps, and lowers caps above the maximum", () =>
            {
                LoopScheduler s = new LoopScheduler(10, 60, new ManualClock(Start));
                MuxAssert.Throws<ArgumentException>(() => s.Create("  ", null), "blank prompt");
                MuxAssert.Throws<ArgumentOutOfRangeException>(() => s.Create("x", TimeSpan.FromSeconds(30)), "interval below the minimum");
                MuxAssert.Throws<ArgumentOutOfRangeException>(() => s.Create("x", null, 0), "zero cap");
                MuxAssert.AreEqual(10, s.Create("x", null, 500).MaxIterations, "cap lowered to the setting");
                MuxAssert.AreEqual(10, s.Create("y", null).MaxIterations, "default cap");
                MuxAssert.AreEqual(3, s.Create("z", TimeSpan.FromMinutes(1), 3).MaxIterations, "explicit cap kept");
                MuxAssert.AreEqual("x", s.Create("  x  ", null).Prompt, "prompt trimmed");
            });
            Add("ConfigureClampsLimits", "Scheduler limits are clamped to their allowed ranges", () =>
            {
                LoopScheduler s = new LoopScheduler(0, 0, new ManualClock(Start));
                MuxAssert.AreEqual(1, s.MaxIterations, "cap at least 1");
                MuxAssert.AreEqual(1, s.MinIntervalSeconds, "interval at least 1");
                s.Configure(99999, 99999);
                MuxAssert.AreEqual(LoopScheduler.MaxIterationsLimit, s.MaxIterations, "cap at most 1000");
                MuxAssert.AreEqual(LoopScheduler.MaxDelaySeconds, s.MinIntervalSeconds, "interval at most 3600");
            });
            Add("IterationPromptAndDescription", "Iteration prompts tell the model how to pace, and descriptions summarize state", () =>
            {
                LoopScheduler s = NewScheduler(out ManualClock clock);
                LoopDefinition self = s.TryBegin(s.Create("fix the tests", null).Id)!;
                string selfPrompt = s.BuildIterationPrompt(self);
                MuxAssert.Contains("fix the tests", selfPrompt, "prompt kept");
                MuxAssert.Contains("[mux loop L1, iteration 1 of at most 50", selfPrompt, "header");
                MuxAssert.Contains("call schedule_next with delay_seconds (30 to 3600)", selfPrompt, "pacing instruction");
                MuxAssert.Contains("If you do not call it, the loop stops.", selfPrompt, "stop rule");
                LoopDefinition fixedLoop = s.TryBegin(s.Create("poll", TimeSpan.FromMinutes(5)).Id)!;
                string fixedPrompt = s.BuildIterationPrompt(fixedLoop);
                MuxAssert.Contains("repeating every 5m", fixedPrompt, "fixed header");
                MuxAssert.DoesNotContain("schedule_next", fixedPrompt, "no pacing tool for fixed loops");
                LoopDefinition scheduled = s.Complete(fixedLoop.Id)!;
                clock.Advance(TimeSpan.FromSeconds(60));
                string described = LoopScheduler.Describe(scheduled, clock.UtcNow);
                MuxAssert.Contains("L2  every 5m  1/50  next in 4m  poll", described, "description");
                MuxAssert.Throws<ArgumentNullException>(() => s.BuildIterationPrompt(null!), "null loop");
                LoopDefinition longPrompt = new LoopDefinition { Id = "L7", Prompt = new string('a', 100), Status = LoopStatusEnum.Paused };
                MuxAssert.Contains("...", LoopScheduler.Describe(longPrompt, clock.UtcNow), "long prompts are shortened");
            });
            Add("ChangedEventRaised", "The scheduler raises Changed on create, begin, complete, pause, resume, and cancel", () =>
            {
                LoopScheduler s = NewScheduler(out _);
                int count = 0;
                s.Changed += (object? sender, EventArgs e) => count++;
                LoopDefinition loop = s.Create("x", TimeSpan.FromMinutes(1));
                s.TryBegin(loop.Id);
                s.Complete(loop.Id);
                s.Pause(loop.Id);
                s.Resume(loop.Id);
                s.Cancel(loop.Id);
                MuxAssert.AreEqual(6, count, "six changes");
                s.Pause(loop.Id);
                MuxAssert.AreEqual(6, count, "a refused change raises nothing");
            });

            // --- schedule_next tool ---
            Add("ScheduleNextOfferedOnlyWhileSelfPacedRuns", "schedule_next is listed only while a self-paced iteration is running", () =>
            {
                LoopScheduler s = NewScheduler(out _);
                LoopToolProvider provider = new LoopToolProvider(s);
                MuxAssert.AreEqual(0, provider.GetToolDefinitions().Count, "hidden with no loops");
                LoopDefinition fixedLoop = s.Create("fixed", TimeSpan.FromMinutes(1));
                s.TryBegin(fixedLoop.Id);
                MuxAssert.AreEqual(0, provider.GetToolDefinitions().Count, "hidden for a fixed loop");
                LoopDefinition self = s.Create("self", null);
                MuxAssert.AreEqual(0, provider.GetToolDefinitions().Count, "hidden while the self-paced loop only waits");
                s.TryBegin(self.Id);
                IReadOnlyList<ToolDefinition> tools = provider.GetToolDefinitions();
                MuxAssert.AreEqual(1, tools.Count, "listed while running");
                MuxAssert.AreEqual("schedule_next", tools[0].Name, "name");
                MuxAssert.IsTrue(provider.HasTool("SCHEDULE_NEXT"), "case-insensitive");
                MuxAssert.IsFalse(provider.HasTool("run_process"), "other tools are not claimed");
                MuxAssert.AreEqual(ToolMutationKind.ReadOnly, provider.GetMutationKind("schedule_next"), "read-only");
                MuxAssert.AreEqual("loops", provider.Name, "provider name");
                MuxAssert.Throws<ArgumentNullException>(() => new LoopToolProvider(null!), "null scheduler");
            });
            AddAsync("ScheduleNextRecordsDecision", "Calling schedule_next records the delay and reports it", async (CancellationToken ct) =>
            {
                LoopScheduler s = NewScheduler(out ManualClock clock);
                LoopToolProvider provider = new LoopToolProvider(s);
                LoopDefinition loop = s.Create("self", null);
                s.TryBegin(loop.Id);
                ToolResult result = await provider.ExecuteAsync("schedule_next", Json("{\"delay_seconds\":90,\"reason\":\"waiting on CI\"}"), ".", ct).ConfigureAwait(false);
                MuxAssert.IsTrue(result.Success, "success: " + result.Content);
                MuxAssert.Contains("\"scheduled\":true", result.Content, "scheduled flag");
                LoopDefinition next = s.Complete(loop.Id)!;
                MuxAssert.AreEqual(clock.UtcNow.AddSeconds(90), next.NextFireUtc!.Value, "delay applied");

                s.TryBegin(loop.Id);
                ToolResult stop = await provider.ExecuteAsync("schedule_next", Json("{\"stop\":\"true\",\"reason\":\"done\",\"loop_id\":\"L1\"}"), ".", ct).ConfigureAwait(false);
                MuxAssert.IsTrue(stop.Success, "stop accepted (string boolean)");
                MuxAssert.AreEqual(LoopStatusEnum.Stopped, s.Complete(loop.Id)!.Status, "stopped");

                LoopDefinition again = s.Create("self 2", null);
                s.TryBegin(again.Id);
                ToolResult stringDelay = await provider.ExecuteAsync("schedule_next", Json("{\"delay_seconds\":\"45\",\"reason\":\"x\"}"), ".", ct).ConfigureAwait(false);
                MuxAssert.IsTrue(stringDelay.Success, "numeric string delay accepted");
            });
            AddAsync("ScheduleNextRejectsBadCalls", "schedule_next rejects malformed delays, out-of-range delays, idle calls, and unknown tool names", async (CancellationToken ct) =>
            {
                LoopScheduler s = NewScheduler(out _);
                LoopToolProvider provider = new LoopToolProvider(s);
                ToolResult idle = await provider.ExecuteAsync("schedule_next", Json("{\"delay_seconds\":60,\"reason\":\"x\"}"), ".", ct).ConfigureAwait(false);
                MuxAssert.IsFalse(idle.Success, "idle refused");
                MuxAssert.Contains("not_scheduled", idle.Content, "idle code");
                LoopDefinition loop = s.Create("self", null);
                s.TryBegin(loop.Id);
                ToolResult text = await provider.ExecuteAsync("schedule_next", Json("{\"delay_seconds\":\"soon\",\"reason\":\"x\"}"), ".", ct).ConfigureAwait(false);
                MuxAssert.Contains("invalid_arguments", text.Content, "non-numeric delay");
                ToolResult fraction = await provider.ExecuteAsync("schedule_next", Json("{\"delay_seconds\":45.5,\"reason\":\"x\"}"), ".", ct).ConfigureAwait(false);
                MuxAssert.Contains("invalid_arguments", fraction.Content, "fractional delay");
                ToolResult tooShort = await provider.ExecuteAsync("schedule_next", Json("{\"delay_seconds\":5,\"reason\":\"x\"}"), ".", ct).ConfigureAwait(false);
                MuxAssert.IsFalse(tooShort.Success, "short delay refused");
                ToolResult unknown = await provider.ExecuteAsync("other", Json("{}"), ".", ct).ConfigureAwait(false);
                MuxAssert.Contains("unknown_tool", unknown.Content, "unknown tool");
                MuxAssert.AreEqual(LoopStatusEnum.Stopped, s.Complete(loop.Id)!.Status, "rejected calls record nothing, so the loop stops");
            });

            // --- driver ---
            AddAsync("DriverRunsFixedLoopToCap", "The driver runs a fixed loop to its cap, waiting the interval between iterations", async (CancellationToken ct) =>
            {
                LoopScheduler s = NewScheduler(out ManualClock clock);
                s.Create("poll", TimeSpan.FromMinutes(1), 3);
                List<TimeSpan> waits = new List<TimeSpan>();
                LoopDriver driver = new LoopDriver(s, (TimeSpan wait, CancellationToken token) => { waits.Add(wait); clock.Advance(wait); return Task.CompletedTask; });
                List<string> prompts = new List<string>();
                int iterations = await driver.RunAsync((LoopDefinition loop, string prompt, CancellationToken token) =>
                {
                    prompts.Add(prompt);
                    clock.Advance(TimeSpan.FromSeconds(5));
                    return Task.FromResult(true);
                }, ct).ConfigureAwait(false);
                MuxAssert.AreEqual(3, iterations, "three iterations");
                MuxAssert.AreEqual(2, waits.Count, "two waits");
                MuxAssert.AreEqual(55.0, waits[0].TotalSeconds, "waits the rest of the interval");
                MuxAssert.Contains("iteration 3 of at most 3", prompts[2], "third prompt header");
                MuxAssert.AreEqual(LoopStatusEnum.Completed, s.Get("L1")!.Status, "completed");
            });
            AddAsync("DriverRunsSelfPacedUntilNoDecision", "The driver follows schedule_next decisions and stops when an iteration makes none", async (CancellationToken ct) =>
            {
                LoopScheduler s = NewScheduler(out ManualClock clock);
                s.Create("watch", null);
                LoopDriver driver = new LoopDriver(s, (TimeSpan wait, CancellationToken token) => { clock.Advance(wait); return Task.CompletedTask; });
                int iterations = await driver.RunAsync((LoopDefinition loop, string prompt, CancellationToken token) =>
                {
                    if (loop.IterationCount < 3)
                    {
                        s.TryRecordDecision(loop.Id, 45, false, "again", out _);
                    }

                    return Task.FromResult(true);
                }, ct).ConfigureAwait(false);
                MuxAssert.AreEqual(3, iterations, "three iterations");
                MuxAssert.AreEqual(LoopStatusEnum.Stopped, s.Get("L1")!.Status, "stopped after the silent iteration");
            });
            AddAsync("DriverPausesOnFailureAndRethrows", "A failed iteration pauses the loop; an exception pauses it and propagates", async (CancellationToken ct) =>
            {
                LoopScheduler s = NewScheduler(out _);
                s.Create("a", TimeSpan.FromMinutes(1));
                LoopDriver driver = new LoopDriver(s, (TimeSpan wait, CancellationToken token) => Task.CompletedTask);
                int iterations = await driver.RunAsync((LoopDefinition loop, string prompt, CancellationToken token) => Task.FromResult(false), ct).ConfigureAwait(false);
                MuxAssert.AreEqual(1, iterations, "one iteration");
                MuxAssert.AreEqual(LoopStatusEnum.Paused, s.Get("L1")!.Status, "paused");

                LoopScheduler other = NewScheduler(out _);
                other.Create("b", null);
                LoopDriver throwing = new LoopDriver(other, (TimeSpan wait, CancellationToken token) => Task.CompletedTask);
                await MuxAssert.ThrowsAsync<InvalidOperationException>(() => throwing.RunAsync((LoopDefinition loop, string prompt, CancellationToken token) => throw new InvalidOperationException("boom"), ct), "exception propagates").ConfigureAwait(false);
                MuxAssert.AreEqual(LoopStatusEnum.Paused, other.Get("L1")!.Status, "paused after the exception");
                await MuxAssert.ThrowsAsync<ArgumentNullException>(() => throwing.RunAsync(null!, ct), "null callback").ConfigureAwait(false);
            });
            AddAsync("DriverStopsOnCancellation", "Cancelling the driver ends the run without starting more iterations", async (CancellationToken ct) =>
            {
                LoopScheduler s = NewScheduler(out ManualClock clock);
                s.Create("a", TimeSpan.FromMinutes(1), 10);
                using (CancellationTokenSource cts = new CancellationTokenSource())
                {
                    LoopDriver driver = new LoopDriver(s, (TimeSpan wait, CancellationToken token) => { clock.Advance(wait); return Task.CompletedTask; });
                    int iterations = await driver.RunAsync((LoopDefinition loop, string prompt, CancellationToken token) =>
                    {
                        if (loop.IterationCount == 2) cts.Cancel();
                        return Task.FromResult(true);
                    }, cts.Token).ConfigureAwait(false);
                    MuxAssert.AreEqual(2, iterations, "stopped after the cancelling iteration");
                }
            });
            AddAsync("DriverWithNoLoopsReturnsAtOnce", "The driver returns immediately when nothing is scheduled", async (CancellationToken ct) =>
            {
                LoopScheduler s = NewScheduler(out _);
                LoopDefinition paused = s.Create("a", TimeSpan.FromMinutes(1));
                s.Pause(paused.Id);
                int iterations = await new LoopDriver(s).RunAsync((LoopDefinition loop, string prompt, CancellationToken token) => Task.FromResult(true), ct).ConfigureAwait(false);
                MuxAssert.AreEqual(0, iterations, "no iterations");
                MuxAssert.Throws<ArgumentNullException>(() => new LoopDriver(null!), "null scheduler");
            });

            // --- settings, persistence, sidebar, binder ---
            Add("LoopSettingsDefaultsAndClamps", "loopMaxIterations and loopMinIntervalSeconds default, clamp, and round-trip through JSON", () =>
            {
                MuxSettings defaults = new MuxSettings();
                MuxAssert.AreEqual(50, defaults.LoopMaxIterations, "default cap");
                MuxAssert.AreEqual(30, defaults.LoopMinIntervalSeconds, "default interval");
                MuxSettings clamped = new MuxSettings { LoopMaxIterations = 0, LoopMinIntervalSeconds = -4 };
                MuxAssert.AreEqual(1, clamped.LoopMaxIterations, "cap floor");
                MuxAssert.AreEqual(1, clamped.LoopMinIntervalSeconds, "interval floor");
                clamped.LoopMaxIterations = 5000;
                clamped.LoopMinIntervalSeconds = 99999;
                MuxAssert.AreEqual(1000, clamped.LoopMaxIterations, "cap ceiling");
                MuxAssert.AreEqual(3600, clamped.LoopMinIntervalSeconds, "interval ceiling");
                MuxSettings parsed = JsonSerializer.Deserialize<MuxSettings>("{\"loopMaxIterations\":7,\"loopMinIntervalSeconds\":5}")!;
                MuxAssert.AreEqual(7, parsed.LoopMaxIterations, "cap from JSON");
                MuxAssert.AreEqual(5, parsed.LoopMinIntervalSeconds, "interval from JSON");
                MuxAssert.Contains("\"loopMaxIterations\":7", JsonSerializer.Serialize(parsed), "property name");
            });
            Add("LoopDefinitionJsonRoundTrip", "A loop serializes with a string status and round-trips", () =>
            {
                LoopDefinition loop = new LoopDefinition { Id = "L4", Prompt = "p", IntervalSeconds = 60, MaxIterations = 5, IterationCount = 2, Status = LoopStatusEnum.Paused, CreatedUtc = Start.UtcDateTime };
                string json = JsonSerializer.Serialize(loop);
                MuxAssert.Contains("\"Status\":\"Paused\"", json, "string status");
                MuxAssert.DoesNotContain("IsSelfPaced", json, "computed members are not stored");
                LoopDefinition back = JsonSerializer.Deserialize<LoopDefinition>(json)!;
                MuxAssert.AreEqual(60, back.IntervalSeconds!.Value, "interval");
                MuxAssert.AreEqual(LoopStatusEnum.Paused, back.Status, "status");
                MuxAssert.IsNull(new LoopDefinition { IntervalSeconds = 0 }.IntervalSeconds, "zero interval means self-paced");
                MuxAssert.AreEqual(1, new LoopDefinition { MaxIterations = -3 }.MaxIterations, "cap floor");
            });
            Add("ResumeCarriesLoops", "Resuming a session carries its loops and tolerates a missing or broken list", () =>
            {
                SessionSnapshot snapshot = new SessionSnapshot { Id = "s1", Loops = new List<LoopDefinition> { new LoopDefinition { Id = "L1", Prompt = "p", Status = LoopStatusEnum.Scheduled }, null! } };
                SessionResumeResult resume = SessionResumeService.Resume(snapshot);
                MuxAssert.AreEqual(1, resume.Loops.Count, "loop carried, null skipped");
                MuxAssert.AreEqual(0, SessionResumeService.Resume(new SessionSnapshot { Id = "s2" }).Loops.Count, "no loops");
            });
            AddAsync("SessionSavesKeepOrClearLoops", "A save without loops keeps the stored ones; an empty list clears them", async (CancellationToken ct) =>
            {
                string dir = Path.Combine(Path.GetTempPath(), "mux-loops-" + Guid.NewGuid().ToString("N"));
                try
                {
                    SessionStore store = new SessionStore(dir);
                    SessionService service = new SessionService(store);
                    SessionSnapshot first = Conversation("s1", 2);
                    first.Loops = new List<LoopDefinition> { new LoopDefinition { Id = "L1", Prompt = "poll", IntervalSeconds = 60, Status = LoopStatusEnum.Scheduled } };
                    await service.PersistConversationAsync(first, ct).ConfigureAwait(false);

                    await service.PersistConversationAsync(Conversation("s1", 4), ct).ConfigureAwait(false);
                    SessionSnapshot kept = (await store.LoadAsync("s1", ct).ConfigureAwait(false))!;
                    MuxAssert.AreEqual(1, kept.Loops?.Count ?? 0, "a surface without loops keeps them");

                    SessionSnapshot cleared = Conversation("s1", 6);
                    cleared.Loops = new List<LoopDefinition>();
                    await service.PersistConversationAsync(cleared, ct).ConfigureAwait(false);
                    SessionSnapshot after = (await store.LoadAsync("s1", ct).ConfigureAwait(false))!;
                    MuxAssert.AreEqual(0, after.Loops?.Count ?? 0, "an empty list clears them");
                }
                finally
                {
                    try { Directory.Delete(dir, true); } catch (Exception) { }
                }
            });
            Add("SidebarLoopLine", "The sidebar formats each loop with pacing, progress, and next fire", () =>
            {
                DateTime now = Start.UtcDateTime;
                MuxAssert.AreEqual(" L1 5m 3/50 in 4m12s", SidebarView.FormatLoopLine(new LoopDefinition { Id = "L1", IntervalSeconds = 300, MaxIterations = 50, IterationCount = 3, Status = LoopStatusEnum.Scheduled, NextFireUtc = now.AddSeconds(252) }, now), "scheduled");
                MuxAssert.AreEqual(" L2 self 1/10 running", SidebarView.FormatLoopLine(new LoopDefinition { Id = "L2", MaxIterations = 10, IterationCount = 1, Status = LoopStatusEnum.Running }, now), "running");
                MuxAssert.AreEqual(" L3 1m 0/5 paused", SidebarView.FormatLoopLine(new LoopDefinition { Id = "L3", IntervalSeconds = 60, MaxIterations = 5, Status = LoopStatusEnum.Paused }, now), "paused");
                MuxAssert.AreEqual(" L4 1m 0/5 due", SidebarView.FormatLoopLine(new LoopDefinition { Id = "L4", IntervalSeconds = 60, MaxIterations = 5, Status = LoopStatusEnum.Scheduled, NextFireUtc = now }, now), "due");
                MuxAssert.Throws<ArgumentNullException>(() => SidebarView.FormatLoopLine(null!, now), "null loop");
            });
            Add("BinderAddsLoopProvider", "The tool binder composes additional providers after skills, without skills too", () =>
            {
                LoopToolProvider provider = new LoopToolProvider(NewScheduler(out _));
                AgentLoopOptions template = new AgentLoopOptions(new EndpointConfig { Name = "e", BaseUrl = "http://localhost", Model = "m" });
                ExternalToolsBinder.Apply(template, "BASE", "COMPACT", null, null, null, 5, new List<IExternalToolProvider> { provider });
                MuxAssert.IsNotNull(template.ExternalToolProviders, "providers set");
                MuxAssert.AreEqual(1, template.ExternalToolProviders!.Count, "one provider");
                MuxAssert.AreEqual(5, template.EffectiveToolCount, "dynamic tools are not counted");
                ExternalToolsBinder.Apply(template, "BASE", "COMPACT", null, null, null, 5);
                MuxAssert.IsNull(template.ExternalToolProviders, "no providers without skills or extras");
                ToolRuntimeBinder binder = new ToolRuntimeBinder(template, "BASE", "COMPACT", 5) { AdditionalProviders = new List<IExternalToolProvider> { provider } };
                binder.Rebind();
                MuxAssert.AreEqual(1, template.ExternalToolProviders!.Count, "binder applies extras");
                binder.AdditionalProviders = null!;
                MuxAssert.AreEqual(0, binder.AdditionalProviders.Count, "null becomes empty");
            });

            // --- terminal /loop and /loops ---
            AddAsync("TerminalLoopRunsIterations", "/loop runs the prompt now, again after the interval, and /loops cancel stops it", async (CancellationToken ct) =>
            {
                ManualClock clock = new ManualClock(Start);
                LoopScheduler s = new LoopScheduler(50, 30, clock);
                HeadlessBackend backend = new HeadlessBackend(140, 40);
                await using (JobManager manager = new JobManager(EchoRunner, maxConcurrency: 1))
                using (MuxTuiApp app = new MuxTuiApp(backend, manager, "demo", ApprovalPolicyEnum.AutoApprove, loopScheduler: s))
                {
                    Submit(backend, app, "/loop 1m --max 3 check the build");
                    await app.DrainProjectorsAsync().ConfigureAwait(false);
                    string transcript = string.Join("\n", app.TranscriptSnapshot());
                    MuxAssert.Contains("Loop L1 created: every 1m, at most 3 iterations", transcript, "created notice");
                    MuxAssert.Contains("Echo: check the build", transcript, "first iteration ran at once");
                    MuxAssert.Contains("next iteration in 1m", transcript, "next fire reported");
                    MuxAssert.Contains("L1 1m 1/3", string.Join("\n", app.SidebarSnapshot()), "sidebar shows the loop");

                    app.TickLoops();
                    await app.DrainProjectorsAsync().ConfigureAwait(false);
                    MuxAssert.AreEqual(1, s.Get("L1")!.IterationCount, "not due before the interval");

                    clock.Advance(TimeSpan.FromMinutes(1));
                    app.TickLoops();
                    await app.DrainProjectorsAsync().ConfigureAwait(false);
                    MuxAssert.AreEqual(2, s.Get("L1")!.IterationCount, "second iteration after the interval");
                    MuxAssert.AreEqual(1, app.SnapshotLoops(), "persisted with the session snapshot");

                    Submit(backend, app, "/loops cancel L1");
                    clock.Advance(TimeSpan.FromMinutes(5));
                    app.TickLoops();
                    await app.DrainProjectorsAsync().ConfigureAwait(false);
                    transcript = string.Join("\n", app.TranscriptSnapshot());
                    MuxAssert.Contains("Stopped loop L1.", transcript, "cancel notice");
                    MuxAssert.AreEqual(2, s.Get("L1")!.IterationCount, "nothing fires after cancel");
                    MuxAssert.AreEqual(0, app.SnapshotLoops(), "a stopped loop is not persisted");
                }
            });
            AddAsync("TerminalLoopCommandErrors", "/loop and /loops report bad input without creating loops", async (CancellationToken ct) =>
            {
                LoopScheduler s = new LoopScheduler(50, 60, new ManualClock(Start));
                HeadlessBackend backend = new HeadlessBackend(140, 40);
                await using (JobManager manager = new JobManager(EchoRunner, maxConcurrency: 1))
                using (MuxTuiApp app = new MuxTuiApp(backend, manager, "demo", ApprovalPolicyEnum.AutoApprove, loopScheduler: s))
                {
                    Submit(backend, app, "/loop 5m");
                    Submit(backend, app, "/loop 10s too fast");
                    Submit(backend, app, "/loops pause L7");
                    Submit(backend, app, "/loops explode");
                    Submit(backend, app, "/loops cancel");
                    Submit(backend, app, "/loops");
                    await app.DrainProjectorsAsync().ConfigureAwait(false);
                    string transcript = string.Join("\n", app.TranscriptSnapshot());
                    MuxAssert.Contains("No prompt given.", transcript, "missing prompt");
                    MuxAssert.Contains("The interval must be at least 1m", transcript, "minimum interval");
                    MuxAssert.Contains("No loop has the id 'L7'", transcript, "unknown id");
                    MuxAssert.Contains("Usage: /loops", transcript, "usage for unknown verb");
                    MuxAssert.Contains("Name a loop", transcript, "missing id");
                    MuxAssert.Contains("No loops. Start one with /loop", transcript, "empty list");
                    MuxAssert.AreEqual(0, s.List().Count, "no loops created");
                }
            });
            AddAsync("TerminalSelfPacedAndRestore", "A self-paced terminal loop stops without schedule_next, and resumed loops come back paused", async (CancellationToken ct) =>
            {
                LoopScheduler s = new LoopScheduler(50, 30, new ManualClock(Start));
                HeadlessBackend backend = new HeadlessBackend(140, 40);
                await using (JobManager manager = new JobManager(EchoRunner, maxConcurrency: 1))
                using (MuxTuiApp app = new MuxTuiApp(backend, manager, "demo", ApprovalPolicyEnum.AutoApprove, loopScheduler: s))
                {
                    Submit(backend, app, "/loop tidy up");
                    await app.DrainProjectorsAsync().ConfigureAwait(false);
                    string transcript = string.Join("\n", app.TranscriptSnapshot());
                    MuxAssert.Contains("self-paced", transcript, "self-paced notice");
                    MuxAssert.Contains("Loop L1 stopped: the iteration ended without calling schedule_next", transcript, "stopped without a decision");

                    SessionResumeResult resume = new SessionResumeResult
                    {
                        Id = "restored",
                        Loops = new List<LoopDefinition> { new LoopDefinition { Id = "L5", Prompt = "poll", IntervalSeconds = 60, MaxIterations = 5, Status = LoopStatusEnum.Scheduled } }
                    };
                    app.RestoreSession(resume);
                    transcript = string.Join("\n", app.TranscriptSnapshot());
                    MuxAssert.Contains("1 loop was restored paused", transcript, "restore notice");
                    MuxAssert.AreEqual(LoopStatusEnum.Paused, s.Get("L5")!.Status, "paused");
                    Submit(backend, app, "/loops resume L5");
                    await app.DrainProjectorsAsync().ConfigureAwait(false);
                    MuxAssert.AreEqual(1, s.Get("L5")!.IterationCount, "resumed loop ran");
                }
            });
            AddAsync("TerminalWithoutSchedulerSaysSo", "/loop in a shell without a scheduler explains that loops are unavailable", async (CancellationToken ct) =>
            {
                HeadlessBackend backend = new HeadlessBackend(140, 40);
                await using (JobManager manager = new JobManager(EchoRunner, maxConcurrency: 1))
                using (MuxTuiApp app = new MuxTuiApp(backend, manager, "demo", ApprovalPolicyEnum.AutoApprove))
                {
                    Submit(backend, app, "/loop 5m hi");
                    await app.DrainProjectorsAsync().ConfigureAwait(false);
                    MuxAssert.Contains("Loops are not available", string.Join("\n", app.TranscriptSnapshot()), "notice");
                    MuxAssert.AreEqual(-1, app.SnapshotLoops(), "no loops field");
                }
            });

            // --- mux print --loop ---
            Add("PrintLoopRejectsBadFlags", "mux print rejects a bad --loop, --loop-max without --loop, and --loop with jsonl input", () =>
            {
                CliInvocationResult badInterval = InvokeCli(new[] { "print", "--loop", "soon", "--base-url", "http://127.0.0.1:9", "--model", "m", "hi" });
                MuxAssert.AreEqual(1, badInterval.ExitCode, "bad interval exits 1");
                MuxAssert.Contains("Invalid --loop 'soon'", badInterval.StdOut + badInterval.StdErr, "bad interval message");
                CliInvocationResult maxOnly = InvokeCli(new[] { "print", "--loop-max", "3", "--base-url", "http://127.0.0.1:9", "--model", "m", "hi" });
                MuxAssert.Contains("--loop-max needs --loop", maxOnly.StdOut + maxOnly.StdErr, "max without loop");
                CliInvocationResult jsonl = InvokeCli(new[] { "print", "--loop", "5m", "--input-format", "jsonl", "--base-url", "http://127.0.0.1:9", "--model", "m" });
                MuxAssert.Contains("--loop cannot be combined with --input-format jsonl", jsonl.StdOut + jsonl.StdErr, "jsonl conflict");
                CliInvocationResult zero = InvokeCli(new[] { "print", "--loop", "5m", "--loop-max", "0", "--base-url", "http://127.0.0.1:9", "--model", "m", "hi" });
                MuxAssert.Contains("--loop-max must be at least 1", zero.StdOut + zero.StdErr, "zero cap");
            });
            Add("PrintSelfPacedLoopFollowsScheduleNext", "mux print --loop self runs iterations while the model calls schedule_next and exits when it stops", () =>
            {
                string configDir = Path.Combine(Path.GetTempPath(), "mux-print-loop-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(configDir);
                try
                {
                    File.WriteAllText(Path.Combine(configDir, "settings.json"), "{\"loopMinIntervalSeconds\":1,\"skillsEnabled\":false}");
                    using (MockHttpServer server = new MockHttpServer())
                    {
                        string toolCall = "{\"choices\":[{\"delta\":{\"tool_calls\":[{\"index\":0,\"id\":\"call_next\",\"function\":{\"name\":\"schedule_next\",\"arguments\":\"{\\\"delay_seconds\\\":1,\\\"reason\\\":\\\"not yet\\\"}\"}}]},\"finish_reason\":\"tool_calls\"}]}";
                        server.RegisterStreamingResponse("iteration 1 of at most 4", new List<string> { toolCall });
                        server.RegisterStreamingResponse("will run again 1s after this iteration ends", new List<string> { AgentTestHarness.BuildTextSseChunk("First pass done.") });
                        server.RegisterStreamingResponse("[mux loop L1, iteration 2 of at most 4. You pace this loop", new List<string> { AgentTestHarness.BuildTextSseChunk("All green, nothing left.") });
                        server.Start();

                        CliInvocationResult result = InvokeCli(new[]
                        {
                            "print", "--config-dir", configDir, "--loop", "self", "--loop-max", "4", "--yolo",
                            "--base-url", server.BaseUrl, "--model", "test-model", "--adapter-type", "openai-compatible",
                            "keep the build green"
                        });

                        string all = result.StdOut + "\n" + result.StdErr;
                        MuxAssert.AreEqual(0, result.ExitCode, "exits 0: " + all);
                        MuxAssert.Contains("Loop L1: self-paced, at most 4 iterations.", all, "start notice");
                        MuxAssert.Contains("Loop L1, iteration 2 of at most 4.", all, "second iteration ran");
                        MuxAssert.Contains("ended after 2 iteration(s): the iteration ended without calling schedule_next", all, "stopped without a decision");
                        MuxAssert.Contains("All green, nothing left.", result.StdOut, "final answer printed");
                    }
                }
                finally
                {
                    try { Directory.Delete(configDir, true); } catch (Exception) { }
                }
            });

            return new TestSuiteDescriptor(SuiteId, "Loops: /loop scheduler, schedule_next, driver, terminal, and print", cases);
        }

        #endregion

        #region Private-Methods

        private static LoopScheduler NewScheduler(out ManualClock clock)
        {
            clock = new ManualClock(Start);
            return new LoopScheduler(LoopScheduler.DefaultMaxIterations, LoopScheduler.DefaultMinIntervalSeconds, clock);
        }

        private static LoopDefinition RunIteration(LoopScheduler scheduler, string id)
        {
            MuxAssert.IsNotNull(scheduler.TryBegin(id), "began " + id);
            return scheduler.Complete(id)!;
        }

        private static JsonElement Json(string text)
        {
            using (JsonDocument document = JsonDocument.Parse(text))
            {
                return document.RootElement.Clone();
            }
        }

        private static SessionSnapshot Conversation(string id, int messages)
        {
            SessionSnapshot snapshot = new SessionSnapshot { Id = id, Title = "t" };
            for (int i = 0; i < messages; i++)
            {
                snapshot.ConversationHistory.Add(new ConversationMessage { Role = i % 2 == 0 ? RoleEnum.User : RoleEnum.Assistant, Content = "m" + i });
            }

            return snapshot;
        }

        private static void Submit(HeadlessBackend backend, MuxTuiApp app, string prompt)
        {
            backend.FeedInput(prompt + "\r");
            app.PumpInputOnce();
        }

        private static async IAsyncEnumerable<AgentEvent> EchoRunner(Job job, string prompt, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask.ConfigureAwait(false);
            string firstLine = prompt.Split('\n')[0];
            yield return new AssistantTextEvent { Text = "Echo: " + firstLine };
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

        #endregion
    }
}
