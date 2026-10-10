namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Net.Sockets;
    using System.Runtime.CompilerServices;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Agent;
    using Mux.Core.Checkpoints;
    using Mux.Core.Enums;
    using Mux.Core.Jobs;
    using Mux.Core.Llm;
    using Mux.Core.Models;
    using Mux.Core.Observability;
    using Mux.Core.Plugins;
    using Mux.Core.Runs;
    using Mux.Core.Sessions;
    using Mux.Core.Subagents;
    using Mux.Core.Telemetry;
    using Mux.Core.Tools;
    using Mux.Core.Tools.Tools;
    using Test.Shared.Support;
    using Touchstone.Core;

    /// <summary>
    /// Proves Mux.Core emits its OpenTelemetry signals: spans on the <c>Mux</c> activity source and metrics on
    /// the <c>Mux</c> meter, for every instrumented category (agent workflow and stages, LLM, tools, approvals,
    /// MCP, web search, hooks, subagents, jobs and the write lease, usage writer queue, sessions, runs,
    /// checkpoints, build and config gauges), including failure paths and the no-listener path. Uses an
    /// in-memory <see cref="TelemetryCapture"/>; no exporter or network collector is involved.
    /// </summary>
    public static class TelemetryCoreSuite
    {
        private const string Suite = "TelemetryCore";

        /// <summary>
        /// Builds the suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/> for core telemetry cases.</returns>
        public static TestSuiteDescriptor Create()
        {
            return new TestSuiteDescriptor(
                Suite,
                "Mux.Core metrics and traces are emitted for every instrumented code path",
                new List<TestCaseDescriptor>
                {
                    new TestCaseDescriptor(Suite, "NoListenerDoesNotThrow", "Every recording helper is safe with no listener and with null spans", NoListenerDoesNotThrowAsync),
                    new TestCaseDescriptor(Suite, "LabelsAreBounded", "Free-form codes collapse to bounded label values", LabelsAreBoundedAsync),
                    new TestCaseDescriptor(Suite, "AgentRunTraceAndMetrics", "An agent run with a tool call emits nested run/llm/approval/tool spans and workflow metrics", AgentRunTraceAndMetricsAsync),
                    new TestCaseDescriptor(Suite, "LlmConnectionFailure", "An unreachable LLM marks the client span failed and counts retries and errors", LlmConnectionFailureAsync),
                    new TestCaseDescriptor(Suite, "LlmSendFailure", "A failed non-streaming completion records a failed chat request", LlmSendFailureAsync),
                    new TestCaseDescriptor(Suite, "DeniedToolCall", "A denied tool call is counted as denied without a tool span", DeniedToolCallAsync),
                    new TestCaseDescriptor(Suite, "JobPipeline", "A job emits a root span parented across the worker hand-off with queued and run stages", JobPipelineAsync),
                    new TestCaseDescriptor(Suite, "JobFailure", "A failing job is counted as failed and its span marked as an error", JobFailureAsync),
                    new TestCaseDescriptor(Suite, "WriteLeaseWaitAndTimeout", "Write-lease waits record wait time, waiter gauge, and timeouts", WriteLeaseWaitAndTimeoutAsync),
                    new TestCaseDescriptor(Suite, "McpCalls", "MCP connect, tools/list, and tools/call emit client spans and integration metrics, including failures", McpCallsAsync),
                    new TestCaseDescriptor(Suite, "WebSearchFailure", "A failing web search records an error integration call", WebSearchFailureAsync),
                    new TestCaseDescriptor(Suite, "Hooks", "Hook subprocesses record integration outcomes for success and start failure", HooksAsync),
                    new TestCaseDescriptor(Suite, "Subagent", "A subagent run emits a subagent span wrapping a child agent run", SubagentAsync),
                    new TestCaseDescriptor(Suite, "SessionStore", "Session save/load/delete record operations, including not-found", SessionStoreAsync),
                    new TestCaseDescriptor(Suite, "UsageRecorderQueue", "The usage writer records enqueue, write, drop, and queue depth", UsageRecorderQueueAsync),
                    new TestCaseDescriptor(Suite, "RunRegistry", "The run registry reports active runs and terminal statuses", RunRegistryAsync),
                    new TestCaseDescriptor(Suite, "Checkpoints", "Git checkpoints emit capture/restore spans with git client spans, including failure", CheckpointsAsync),
                    new TestCaseDescriptor(Suite, "BuildAndConfigGauges", "Build info and safe configuration gauges are observable", BuildAndConfigGaugesAsync)
                });
        }

        #region Cases

        private static Task NoListenerDoesNotThrowAsync(CancellationToken ct)
        {
            // These must never throw, with or without a listener, and must tolerate null spans.
            Activity? activity = MuxTelemetry.StartActivity("noop");
            if (!MuxTelemetry.Source.HasListeners())
            {
                MuxAssert.IsNull(activity, "no span without a listener");
            }

            MuxTelemetry.SetTag(null, "k", "v");
            MuxTelemetry.SetOk(null);
            MuxTelemetry.SetError(null, "x", "y");
            MuxTelemetry.RecordException(null, new InvalidOperationException("boom"));
            MuxTelemetry.Stop(null);
            MuxTelemetry.Stop(activity);
            MuxTelemetry.StartActivity("noop", ActivityKind.Client, default(ActivityContext), DateTimeOffset.UtcNow)?.Dispose();

            MuxTelemetry.AgentRunStarted();
            MuxTelemetry.RecordAgentRun("completed", "primary", 0.01, 1);
            MuxTelemetry.RecordAgentStage(MuxTelemetryNames.StageLlm, "success", 0.01);
            MuxTelemetry.RecordAgentError(null);
            MuxTelemetry.RecordCompaction(null);
            MuxTelemetry.RecordToolCall("builtin", "read_file", "success", 0.01);
            MuxTelemetry.RecordToolCall("builtin", "read_file", "denied", -1);
            MuxTelemetry.RecordApproval("approved");
            MuxTelemetry.RecordSubagent("success", 0.01);
            MuxTelemetry.RecordLlmRequest("ollama", "chat", "success", 0.01);
            MuxTelemetry.RecordTimeToFirstToken("ollama", 0.01);
            MuxTelemetry.RecordTokens("ollama", "input", 0);
            MuxTelemetry.RecordLlmRetry("ollama", "chat");
            MuxTelemetry.RecordIntegration("git", "status", "success", 0.01);
            MuxTelemetry.RecordJobStage(MuxTelemetryNames.StageQueued, "success", 0.01);
            MuxTelemetry.RecordJob("completed");
            MuxTelemetry.AddWriteLeaseWaiter(1);
            MuxTelemetry.AddWriteLeaseWaiter(-1);
            MuxTelemetry.RecordWriteLeaseWait("acquired", 0.01);
            MuxTelemetry.RecordUsageEvents("enqueued", 0);
            MuxTelemetry.RecordUsageWrite("success", 0.01);
            MuxTelemetry.RecordSessionOperation("save", "success", 0.01);
            MuxTelemetry.RecordRunCompleted("Completed");
            MuxTelemetry.RecordCheckpoint("capture", "success", 0.01);
            object owner = new object();
            MuxTelemetry.RegisterGaugeSource(MuxTelemetryNames.JobsQueued, owner, (object o) => throw new InvalidOperationException("read failure is swallowed"));
            MuxTelemetry.UnregisterGaugeSources(owner);
            MuxTelemetry.UnregisterGaugeSources(null!);
            return Task.CompletedTask;
        }

        private static Task LabelsAreBoundedAsync(CancellationToken ct)
        {
            MuxAssert.AreEqual("llm_error", MuxTelemetry.SanitizeCode("LLM_ERROR"), "lower-cased code kept");
            MuxAssert.AreEqual("tools/call", MuxTelemetry.SanitizeCode("tools/call"), "slash allowed");
            MuxAssert.AreEqual("other", MuxTelemetry.SanitizeCode("user typed: rm -rf /"), "free text collapsed");
            MuxAssert.AreEqual("other", MuxTelemetry.SanitizeCode(new string('a', 49)), "overlong collapsed");
            MuxAssert.AreEqual("other", MuxTelemetry.SanitizeCode(null), "null collapsed");
            MuxAssert.AreEqual("other", MuxTelemetry.SanitizeCode("  "), "blank collapsed");
            return Task.CompletedTask;
        }

        private static async Task AgentRunTraceAndMetricsAsync(CancellationToken ct)
        {
            string tempDir = NewTempDirectory();
            string tempFile = Path.Combine(tempDir, "telemetry_read.txt");
            File.WriteAllText(tempFile, "telemetry file contents");

            try
            {
                using TelemetryCapture capture = new TelemetryCapture();
                using MockHttpServer server = new MockHttpServer();
                string escapedPath = tempFile.Replace("\\", "\\\\").Replace("\"", "\\\"");
                string toolCallChunk = "{\"choices\":[{\"delta\":{\"tool_calls\":[{\"index\":0,\"id\":\"call_t1\",\"function\":{\"name\":\"read_file\",\"arguments\":\"{\\\"file_path\\\":\\\"" + escapedPath + "\\\"}\"}}]},\"finish_reason\":\"tool_calls\"}]}";
                server.RegisterStreamingResponse("telemetry read", new List<string> { toolCallChunk });
                server.RegisterStreamingResponse("telemetry file contents", new List<string> { AgentTestHarness.BuildTextSseChunk("done reading") });
                server.Start();

                AgentLoopOptions options = new AgentLoopOptions(AgentTestHarness.BuildMockEndpoint(server.BaseUrl))
                {
                    ApprovalPolicy = ApprovalPolicyEnum.AutoApprove,
                    MaxIterations = 5,
                    WorkingDirectory = tempDir,
                    SessionId = "telemetry-session"
                };

                Activity root = capture.StartRoot("test agent run");
                List<AgentEvent> events = await AgentTestHarness.CollectEventsAsync(options, "telemetry read", ct).ConfigureAwait(false);
                root.Stop();

                MuxAssert.IsTrue(events.Any((AgentEvent e) => e is ToolCallCompletedEvent), "tool executed");

                List<Activity> spans = capture.SpansInTrace(root.TraceId);
                Activity run = Single(spans, "agent run");
                MuxAssert.AreEqual(root.SpanId, run.ParentSpanId, "agent run nests under the caller's span");
                MuxAssert.AreEqual(ActivityStatusCode.Ok, run.Status, "agent run status");
                MuxAssert.AreEqual("completed", run.GetTagItem(MuxTelemetryNames.LabelOutcome) as string, "agent run outcome tag");
                MuxAssert.AreEqual("telemetry-session", run.GetTagItem(MuxTelemetryNames.AttrSessionId) as string, "session id on span (not on metrics)");

                List<Activity> llmSpans = spans.Where((Activity a) => a.DisplayName == "llm chat_stream").ToList();
                MuxAssert.AreEqual(2, llmSpans.Count, "one llm span per model call");
                MuxAssert.IsTrue(llmSpans.All((Activity a) => a.ParentSpanId == run.SpanId && a.Kind == ActivityKind.Client), "llm spans are client children of the run");
                MuxAssert.IsTrue(llmSpans.All((Activity a) => a.Status == ActivityStatusCode.Ok), "llm spans ok");

                Activity tool = Single(spans, "tool read_file");
                MuxAssert.AreEqual(run.SpanId, tool.ParentSpanId, "tool span parent");
                MuxAssert.AreEqual(ActivityStatusCode.Ok, tool.Status, "tool span ok");
                Activity approval = Single(spans, "stage:approval");
                MuxAssert.AreEqual(run.SpanId, approval.ParentSpanId, "approval span parent");

                // W3C trace context reached the LLM endpoint on the outbound HTTP call.
                List<string> traceparents = server.ReceivedTraceparents;
                MuxAssert.IsTrue(traceparents.Count >= 1, "traceparent header sent to the LLM endpoint");
                MuxAssert.Contains(root.TraceId.ToHexString(), traceparents[0], "traceparent carries the run's trace id");

                MuxAssert.IsTrue(capture.Any(MuxTelemetryNames.AgentRuns, "outcome=completed", "call_kind=primary"), "agent run counter");
                MuxAssert.IsTrue(capture.Any(MuxTelemetryNames.AgentRunDuration, "outcome=completed"), "agent run duration");
                MuxAssert.IsTrue(capture.Measurements(MuxTelemetryNames.AgentIterations, "outcome=completed").Any((CapturedMeasurement m) => m.Value == 2), "iterations histogram");
                MuxAssert.IsTrue(capture.Any(MuxTelemetryNames.LlmRequests, "gen_ai.provider.name=openaicompatible", "gen_ai.operation.name=chat_stream", "outcome=success"), "llm request counter");
                MuxAssert.IsTrue(capture.Any(MuxTelemetryNames.LlmRequestDuration, "gen_ai.provider.name=openaicompatible"), "llm duration");
                MuxAssert.IsTrue(capture.Any(MuxTelemetryNames.IntegrationRequests, "service=llm", "operation=chat_stream", "outcome=success"), "llm mirrored into integrations");
                MuxAssert.IsTrue(capture.Any(MuxTelemetryNames.ToolCalls, "tool.kind=builtin", "tool.name=read_file", "outcome=success"), "tool counter");
                MuxAssert.IsTrue(capture.Any(MuxTelemetryNames.ToolDuration, "tool.name=read_file"), "tool duration");
                MuxAssert.IsTrue(capture.Any(MuxTelemetryNames.ApprovalDecisions, "decision=approved"), "approval decision");
                MuxAssert.IsTrue(capture.Any(MuxTelemetryNames.AgentStageDuration, "stage=llm", "outcome=success"), "llm stage");
                MuxAssert.IsTrue(capture.Any(MuxTelemetryNames.AgentStageDuration, "stage=approval", "outcome=approved"), "approval stage");
                MuxAssert.IsTrue(capture.Any(MuxTelemetryNames.AgentStageDuration, "stage=tool", "outcome=success"), "tool stage");
                MuxAssert.IsTrue(capture.Any(MuxTelemetryNames.AgentRunsActive), "active-run gauge moved");
                MuxAssert.IsFalse(capture.Measurements(MuxTelemetryNames.AgentRuns).Any((CapturedMeasurement m) => m.Tags.ContainsKey(MuxTelemetryNames.AttrSessionId)), "no session id label on metrics");
            }
            finally
            {
                TryDeleteDirectory(tempDir);
            }
        }

        private static async Task LlmConnectionFailureAsync(CancellationToken ct)
        {
            using TelemetryCapture capture = new TelemetryCapture();
            AgentLoopOptions options = new AgentLoopOptions(AgentTestHarness.BuildMockEndpoint("http://127.0.0.1:" + ClosedLoopbackPort()))
            {
                ApprovalPolicy = ApprovalPolicyEnum.AutoApprove,
                MaxIterations = 2
            };

            Activity root = capture.StartRoot("test llm failure");
            List<AgentEvent> events = await AgentTestHarness.CollectEventsAsync(options, "hello", ct).ConfigureAwait(false);
            root.Stop();

            MuxAssert.IsTrue(events.Any((AgentEvent e) => e is ErrorEvent err && err.Code == "llm_connection_error"), "connection error surfaced");

            List<Activity> spans = capture.SpansInTrace(root.TraceId);
            Activity llm = Single(spans, "llm chat_stream");
            MuxAssert.AreEqual(ActivityStatusCode.Error, llm.Status, "llm span failed");
            MuxAssert.AreEqual("llm_connection_error", llm.GetTagItem(MuxTelemetryNames.LabelErrorType) as string, "llm span error.type");
            Activity run = Single(spans, "agent run");
            MuxAssert.AreEqual(ActivityStatusCode.Error, run.Status, "run span failed");
            MuxAssert.IsTrue(run.Events.Any((ActivityEvent e) => e.Name == "mux.agent.error"), "error event on run span");

            MuxAssert.IsTrue(capture.Any(MuxTelemetryNames.LlmRequests, "outcome=llm_connection_error"), "failed llm request counted");
            MuxAssert.IsTrue(capture.Any(MuxTelemetryNames.LlmRetries, "gen_ai.operation.name=chat_stream"), "retries counted");
            MuxAssert.IsTrue(capture.Any(MuxTelemetryNames.AgentErrors, "error.type=llm_connection_error"), "agent error counter");
            MuxAssert.IsTrue(capture.Any(MuxTelemetryNames.AgentRuns, "outcome=failed"), "run outcome is failed when the model never answers");
            MuxAssert.IsTrue(capture.Any(MuxTelemetryNames.AgentStageDuration, "stage=llm", "outcome=llm_connection_error"), "llm stage failure");
        }

        private static async Task LlmSendFailureAsync(CancellationToken ct)
        {
            using TelemetryCapture capture = new TelemetryCapture();
            using LlmClient client = new LlmClient(AgentTestHarness.BuildMockEndpoint("http://127.0.0.1:" + ClosedLoopbackPort()));

            Activity root = capture.StartRoot("test send failure");
            bool threw = false;
            try
            {
                await client.SendAsync(
                    new List<ConversationMessage> { new ConversationMessage { Role = RoleEnum.User, Content = "hi" } },
                    new List<ToolDefinition>(),
                    ct).ConfigureAwait(false);
            }
            catch (HttpRequestException)
            {
                threw = true;
            }

            root.Stop();
            MuxAssert.IsTrue(threw, "send failed");
            Activity span = Single(capture.SpansInTrace(root.TraceId), "llm chat");
            MuxAssert.AreEqual(ActivityStatusCode.Error, span.Status, "chat span failed");
            MuxAssert.IsTrue(span.Events.Any((ActivityEvent e) => e.Name == "exception"), "exception event recorded");
            MuxAssert.IsTrue(capture.Any(MuxTelemetryNames.LlmRequests, "gen_ai.operation.name=chat", "outcome=llm_connection_error"), "failed chat counted");
        }

        private static async Task DeniedToolCallAsync(CancellationToken ct)
        {
            using TelemetryCapture capture = new TelemetryCapture();
            using MockHttpServer server = new MockHttpServer();
            string toolCallChunk = "{\"choices\":[{\"delta\":{\"tool_calls\":[{\"index\":0,\"id\":\"call_d1\",\"function\":{\"name\":\"write_file\",\"arguments\":\"{\\\"file_path\\\":\\\"x.txt\\\",\\\"content\\\":\\\"y\\\"}\"}}]},\"finish_reason\":\"tool_calls\"}]}";
            server.RegisterStreamingResponse("deny telemetry", new List<string> { toolCallChunk });
            server.RegisterStreamingResponse("tool_call_denied", new List<string> { AgentTestHarness.BuildTextSseChunk("ok, denied") });
            server.Start();

            AgentLoopOptions options = new AgentLoopOptions(AgentTestHarness.BuildMockEndpoint(server.BaseUrl))
            {
                ApprovalPolicy = ApprovalPolicyEnum.Deny,
                MaxIterations = 3
            };

            Activity root = capture.StartRoot("test denied");
            await AgentTestHarness.CollectEventsAsync(options, "deny telemetry", ct).ConfigureAwait(false);
            root.Stop();

            List<Activity> spans = capture.SpansInTrace(root.TraceId);
            MuxAssert.IsFalse(spans.Any((Activity a) => a.DisplayName == "tool write_file"), "denied tool never executes");
            MuxAssert.IsTrue(capture.Any(MuxTelemetryNames.ToolCalls, "tool.name=write_file", "outcome=denied"), "denied tool counted");
            MuxAssert.IsTrue(capture.Any(MuxTelemetryNames.ApprovalDecisions, "decision=denied"), "denied decision counted");
            MuxAssert.IsTrue(capture.Any(MuxTelemetryNames.AgentErrors, "error.type=tool_call_denied"), "denial error counted");
        }

        private static async Task JobPipelineAsync(CancellationToken ct)
        {
            using TelemetryCapture capture = new TelemetryCapture();
            await using JobManager manager = new JobManager(CompleteImmediatelyAsync, maxConcurrency: 2);

            Activity root = capture.StartRoot("test submit job");
            Job job = await manager.SubmitAsync("telemetry job", ct).ConfigureAwait(false);
            root.Stop();

            await WaitForStateAsync(job, JobState.Completed, ct).ConfigureAwait(false);
            MuxAssert.IsTrue(
                await TelemetryCapture.WaitForAsync(() => capture.SpansInTrace(root.TraceId).Any((Activity a) => a.DisplayName == "job"), 5000, ct).ConfigureAwait(false),
                "job span exported");

            List<Activity> spans = capture.SpansInTrace(root.TraceId);
            Activity jobSpan = Single(spans, "job");
            MuxAssert.AreEqual(root.SpanId, jobSpan.ParentSpanId, "job span parented to the submitter across the worker hand-off");
            MuxAssert.AreEqual(ActivityStatusCode.Ok, jobSpan.Status, "job span ok");
            MuxAssert.AreEqual(job.Id, jobSpan.GetTagItem(MuxTelemetryNames.AttrJobId) as string, "job id on span");
            MuxAssert.AreEqual(jobSpan.SpanId, Single(spans, "stage:queued").ParentSpanId, "queued stage child");
            MuxAssert.AreEqual(jobSpan.SpanId, Single(spans, "stage:run").ParentSpanId, "run stage child");

            MuxAssert.IsTrue(capture.Any(MuxTelemetryNames.Jobs, "outcome=completed"), "job counter");
            MuxAssert.IsTrue(capture.Any(MuxTelemetryNames.JobStageDuration, "stage=queued"), "queued stage histogram");
            MuxAssert.IsTrue(capture.Any(MuxTelemetryNames.JobStageDuration, "stage=run"), "run stage histogram");
            MuxAssert.IsTrue(capture.Any(MuxTelemetryNames.JobStageEvents, "stage=run", "outcome=success"), "run stage counter");

            capture.CollectObservables();
            MuxAssert.IsTrue(capture.Measurements(MuxTelemetryNames.JobLastSuccess).Any((CapturedMeasurement m) => m.Value > 1_600_000_000), "last-success timestamp gauge");
            MuxAssert.IsTrue(capture.Measurements(MuxTelemetryNames.JobsCapacity).Any((CapturedMeasurement m) => m.Value >= 2), "capacity gauge includes the live manager");
            MuxAssert.IsTrue(capture.Any(MuxTelemetryNames.JobsQueued), "queued gauge observable");
            MuxAssert.IsTrue(capture.Any(MuxTelemetryNames.JobsActive), "active gauge observable");
        }

        private static async Task JobFailureAsync(CancellationToken ct)
        {
            using TelemetryCapture capture = new TelemetryCapture();
            await using JobManager manager = new JobManager(ThrowingRunnerAsync, maxConcurrency: 1);

            Activity root = capture.StartRoot("test failing job");
            Job job = await manager.SubmitAsync("fail please", ct).ConfigureAwait(false);
            root.Stop();

            await WaitForStateAsync(job, JobState.Failed, ct).ConfigureAwait(false);
            MuxAssert.IsTrue(
                await TelemetryCapture.WaitForAsync(() => capture.SpansInTrace(root.TraceId).Any((Activity a) => a.DisplayName == "job"), 5000, ct).ConfigureAwait(false),
                "job span exported");

            List<Activity> spans = capture.SpansInTrace(root.TraceId);
            Activity jobSpan = Single(spans, "job");
            MuxAssert.AreEqual(ActivityStatusCode.Error, jobSpan.Status, "job span failed");
            MuxAssert.AreEqual(ActivityStatusCode.Error, Single(spans, "stage:run").Status, "run stage failed");
            MuxAssert.IsTrue(capture.Any(MuxTelemetryNames.Jobs, "outcome=failed"), "failed job counted");
            MuxAssert.IsTrue(capture.Any(MuxTelemetryNames.JobStageEvents, "stage=run", "outcome=error"), "failed run stage counted");
        }

        private static async Task WriteLeaseWaitAndTimeoutAsync(CancellationToken ct)
        {
            using TelemetryCapture capture = new TelemetryCapture();
            WriteLease lease = new WriteLease();
            WriteLeaseHandle held = await lease.AcquireAsync("holder", ct).ConfigureAwait(false);

            Task<WriteLeaseHandle> waiter = lease.AcquireAsync("waiter", ct);
            MuxAssert.IsTrue(
                await TelemetryCapture.WaitForAsync(() => capture.Measurements(MuxTelemetryNames.WriteLeaseWaiters).Any((CapturedMeasurement m) => m.Value == 1), 2000, ct).ConfigureAwait(false),
                "waiter gauge incremented");
            held.Dispose();
            WriteLeaseHandle second = await waiter.ConfigureAwait(false);
            MuxAssert.IsTrue(capture.Any(MuxTelemetryNames.WriteLeaseWaitDuration, "outcome=acquired"), "acquired wait recorded");

            lease.AcquisitionTimeoutMs = 50;
            bool timedOut = false;
            try
            {
                await lease.AcquireAsync("late", ct).ConfigureAwait(false);
            }
            catch (WriteLeaseTimeoutException)
            {
                timedOut = true;
            }

            second.Dispose();
            MuxAssert.IsTrue(timedOut, "lease acquisition timed out");
            MuxAssert.IsTrue(capture.Any(MuxTelemetryNames.WriteLeaseWaitDuration, "outcome=timeout"), "timeout recorded");
            MuxAssert.IsTrue(capture.Measurements(MuxTelemetryNames.WriteLeaseWaiters).Any((CapturedMeasurement m) => m.Value == -1), "waiter gauge decremented");
        }

        private static async Task McpCallsAsync(CancellationToken ct)
        {
            using TelemetryCapture capture = new TelemetryCapture();
            using TestMcpHttpServer server = new TestMcpHttpServer();
            await server.StartAsync().ConfigureAwait(false);

            Activity root = capture.StartRoot("test mcp");
            using (McpToolManager manager = new McpToolManager(new List<McpServerConfig>()))
            {
                McpServerConfig config = new McpServerConfig
                {
                    Name = "otel-http",
                    Transport = McpTransportTypeEnum.Http,
                    Url = server.BaseUrl,
                    McpPath = server.McpPath
                };
                await manager.AddServerAsync(config, ct).ConfigureAwait(false);

                using JsonDocument echoArgs = JsonDocument.Parse("{\"text\":\"hi\"}");
                ToolResult ok = await manager.ExecuteAsync("c1", "otel-http.echo", echoArgs.RootElement, ct).ConfigureAwait(false);
                MuxAssert.IsTrue(ok.Success, "echo succeeded");

                using JsonDocument emptyArgs = JsonDocument.Parse("{}");
                ToolResult failed = await manager.ExecuteAsync("c2", "otel-http.fail_tool", emptyArgs.RootElement, ct).ConfigureAwait(false);
                MuxAssert.IsFalse(failed.Success, "fail_tool failed");
            }

            using (McpToolManager broken = new McpToolManager(new List<McpServerConfig>
            {
                new McpServerConfig { Name = "otel-broken", Transport = McpTransportTypeEnum.Stdio, Command = "mux-nonexistent-command-otel-7c1d" }
            }))
            {
                await broken.InitializeAsync(ct).ConfigureAwait(false);
            }

            root.Stop();

            List<Activity> spans = capture.SpansInTrace(root.TraceId);
            MuxAssert.IsTrue(spans.Any((Activity a) => a.DisplayName == "mcp connect" && a.Status == ActivityStatusCode.Ok), "connect span ok");
            MuxAssert.IsTrue(spans.Any((Activity a) => a.DisplayName == "mcp connect" && a.Status == ActivityStatusCode.Error), "failed connect span");
            MuxAssert.IsTrue(spans.Any((Activity a) => a.DisplayName == "mcp tools/list" && a.Kind == ActivityKind.Client), "tools/list span");
            List<Activity> calls = spans.Where((Activity a) => a.DisplayName == "mcp tools/call").ToList();
            MuxAssert.AreEqual(2, calls.Count, "one span per tools/call");
            MuxAssert.IsTrue(calls.Any((Activity a) => a.Status == ActivityStatusCode.Error), "failed tool call span");
            MuxAssert.AreEqual("otel-http", calls[0].GetTagItem(MuxTelemetryNames.AttrMcpServer) as string, "server name on span");

            MuxAssert.IsTrue(capture.Any(MuxTelemetryNames.IntegrationRequests, "service=mcp", "operation=connect", "outcome=success"), "connect counted");
            MuxAssert.IsTrue(capture.Any(MuxTelemetryNames.IntegrationRequests, "service=mcp", "operation=connect", "outcome=error"), "failed connect counted");
            MuxAssert.IsTrue(capture.Any(MuxTelemetryNames.IntegrationRequests, "service=mcp", "operation=tools/list", "outcome=success"), "tools/list counted");
            MuxAssert.IsTrue(capture.Any(MuxTelemetryNames.IntegrationRequests, "service=mcp", "operation=tools/call", "outcome=success"), "tools/call counted");
            MuxAssert.IsTrue(capture.Any(MuxTelemetryNames.IntegrationRequests, "service=mcp", "operation=tools/call", "outcome=failure"), "failed tools/call counted");
            MuxAssert.IsTrue(capture.Any(MuxTelemetryNames.IntegrationDuration, "service=mcp", "operation=tools/call"), "tools/call latency");
        }

        private static async Task WebSearchFailureAsync(CancellationToken ct)
        {
            using TelemetryCapture capture = new TelemetryCapture();
            WebSearchTool tool = new WebSearchTool(new FailingWebSearchService());
            using JsonDocument args = JsonDocument.Parse("{\"query\":\"otel\"}");

            Activity root = capture.StartRoot("test web search");
            ToolResult result = await tool.ExecuteAsync("w1", args.RootElement, Path.GetTempPath(), ct).ConfigureAwait(false);
            root.Stop();

            MuxAssert.IsFalse(result.Success, "search failed");
            Activity span = Single(capture.SpansInTrace(root.TraceId), "web_search search");
            MuxAssert.AreEqual(ActivityStatusCode.Error, span.Status, "search span failed");
            MuxAssert.IsTrue(capture.Any(MuxTelemetryNames.IntegrationRequests, "service=web_search", "operation=search", "outcome=error"), "search failure counted");
        }

        private static async Task HooksAsync(CancellationToken ct)
        {
            using TelemetryCapture capture = new TelemetryCapture();
            PluginConfig config = new PluginConfig();
            config.Hooks.Add(new HookDefinition { Name = "ok-hook", Event = HookEventEnum.SessionStart, Command = "dotnet", Args = new List<string> { "--version" } });
            config.Hooks.Add(new HookDefinition { Name = "broken-hook", Event = HookEventEnum.SessionStart, Command = "mux-nonexistent-hook-otel-3b9e" });
            PluginRegistry registry = new PluginRegistry(config);

            Activity root = capture.StartRoot("test hooks");
            IReadOnlyList<HookRunResult> results = await new HookRunner().RunAsync(registry, HookEventEnum.SessionStart, null, Path.GetTempPath(), ct).ConfigureAwait(false);
            root.Stop();

            MuxAssert.AreEqual(2, results.Count, "both hooks ran");
            Activity span = Single(capture.SpansInTrace(root.TraceId), "hook sessionstart");
            MuxAssert.AreEqual(ActivityStatusCode.Error, span.Status, "hook span marks the failed start");
            MuxAssert.IsTrue(capture.Any(MuxTelemetryNames.IntegrationRequests, "service=hook", "operation=sessionstart", "outcome=success"), "successful hook counted");
            MuxAssert.IsTrue(capture.Any(MuxTelemetryNames.IntegrationRequests, "service=hook", "operation=sessionstart", "outcome=error"), "hook start failure counted");
        }

        private static async Task SubagentAsync(CancellationToken ct)
        {
            using TelemetryCapture capture = new TelemetryCapture();
            using MockHttpServer server = new MockHttpServer();
            server.RegisterStreamingResponse("summarize otel", new List<string> { AgentTestHarness.BuildTextSseChunk("subagent done") });
            server.Start();

            EndpointConfig endpoint = AgentTestHarness.BuildMockEndpoint(server.BaseUrl);
            AgentLoopSubagentExecutor executor = new AgentLoopSubagentExecutor(
                () => new AgentLoopOptions(endpoint) { ApprovalPolicy = ApprovalPolicyEnum.AutoApprove, MaxIterations = 3 },
                (string name) => null);
            SubagentDefinition definition = new SubagentDefinition { Name = "otel-helper", SystemPrompt = "You help." };

            Activity root = capture.StartRoot("test subagent");
            SubagentResult result = await executor.ExecuteAsync(definition, "summarize otel", Path.GetTempPath(), ct).ConfigureAwait(false);
            root.Stop();

            MuxAssert.IsTrue(result.Success, "subagent succeeded");
            List<Activity> spans = capture.SpansInTrace(root.TraceId);
            Activity sub = Single(spans, "subagent run");
            MuxAssert.AreEqual("otel-helper", sub.GetTagItem(MuxTelemetryNames.AttrSubagentName) as string, "subagent name on span");
            MuxAssert.AreEqual(sub.SpanId, Single(spans, "agent run").ParentSpanId, "child agent run nests under the subagent span");
            MuxAssert.IsTrue(capture.Any(MuxTelemetryNames.SubagentRuns, "outcome=success"), "subagent counter");
            MuxAssert.IsTrue(capture.Any(MuxTelemetryNames.AgentRuns, "call_kind=subagent"), "child run labeled as subagent");
        }

        private static async Task SessionStoreAsync(CancellationToken ct)
        {
            string dir = NewTempDirectory();
            try
            {
                using TelemetryCapture capture = new TelemetryCapture();
                SessionStore store = new SessionStore(dir);
                Activity root = capture.StartRoot("test sessions");
                await store.SaveAsync(new SessionSnapshot { Id = "otel-session" }, ct).ConfigureAwait(false);
                SessionSnapshot? loaded = await store.LoadAsync("otel-session", ct).ConfigureAwait(false);
                SessionSnapshot? missing = await store.LoadAsync("otel-missing", ct).ConfigureAwait(false);
                await store.ListAsync(ct).ConfigureAwait(false);
                bool deleted = await store.DeleteAsync("otel-session", ct).ConfigureAwait(false);
                root.Stop();

                MuxAssert.IsNotNull(loaded, "loaded");
                MuxAssert.IsNull(missing, "missing");
                MuxAssert.IsTrue(deleted, "deleted");
                List<Activity> spans = capture.SpansInTrace(root.TraceId);
                MuxAssert.IsTrue(spans.Any((Activity a) => a.DisplayName == "session save" && a.Status == ActivityStatusCode.Ok), "save span");
                MuxAssert.AreEqual(2, spans.Count((Activity a) => a.DisplayName == "session load"), "load spans (list does not add per-file spans)");
                MuxAssert.IsTrue(capture.Any(MuxTelemetryNames.SessionOperations, "operation=save", "outcome=success"), "save counted");
                MuxAssert.IsTrue(capture.Any(MuxTelemetryNames.SessionOperations, "operation=load", "outcome=failure"), "not-found load counted");
                MuxAssert.IsTrue(capture.Any(MuxTelemetryNames.SessionOperations, "operation=list", "outcome=success"), "list counted");
                MuxAssert.IsTrue(capture.Any(MuxTelemetryNames.SessionOperations, "operation=delete", "outcome=success"), "delete counted");
                MuxAssert.IsTrue(capture.Any(MuxTelemetryNames.SessionOperationDuration, "operation=load"), "load latency");
            }
            finally
            {
                TryDeleteDirectory(dir);
            }
        }

        private static async Task UsageRecorderQueueAsync(CancellationToken ct)
        {
            string dir = NewTempDirectory();
            try
            {
                using TelemetryCapture capture = new TelemetryCapture();
                using SqliteUsageStore store = new SqliteUsageStore(Path.Combine(dir, "usage.db"), 90, 100000);
                await using (SqliteUsageRecorder recorder = new SqliteUsageRecorder(store))
                {
                    capture.CollectObservables();
                    MuxAssert.IsTrue(capture.Any(MuxTelemetryNames.UsageQueueDepth), "queue depth gauge observable");

                    recorder.Record(new UsageEvent { SessionId = "otel-usage", RunId = "r1" });
                    recorder.Record(new UsageEvent { SessionId = null });
                    await recorder.FlushAsync(ct).ConfigureAwait(false);
                }

                MuxAssert.IsTrue(capture.Any(MuxTelemetryNames.UsageEvents, "outcome=enqueued"), "enqueue counted");
                MuxAssert.IsTrue(capture.Any(MuxTelemetryNames.UsageEvents, "outcome=dropped"), "drop counted");
                MuxAssert.IsTrue(capture.Any(MuxTelemetryNames.UsageEvents, "outcome=written"), "write counted");
                MuxAssert.IsTrue(capture.Any(MuxTelemetryNames.UsageWriteDuration, "outcome=success"), "write latency");
                Activity batch = capture.SpansNamed("usage write_batch").Last();
                MuxAssert.AreEqual(default(ActivitySpanId), batch.ParentSpanId, "background batch span is a root");
            }
            finally
            {
                SqliteConnectionPoolClear();
                TryDeleteDirectory(dir);
            }
        }

        private static Task RunRegistryAsync(CancellationToken ct)
        {
            using TelemetryCapture capture = new TelemetryCapture();
            using (RunRegistry registry = new RunRegistry())
            {
                registry.Create("otel-run", "otel-session", "ep", "model", CancellationToken.None);
                capture.CollectObservables();
                MuxAssert.IsTrue(capture.Measurements(MuxTelemetryNames.RunsActive).Any((CapturedMeasurement m) => m.Value >= 1), "active run observed");
                registry.Complete("otel-run", RunStatusEnum.Canceled);
            }

            MuxAssert.IsTrue(capture.Any(MuxTelemetryNames.RunsCompleted, "status=canceled"), "terminal status counted");
            return Task.CompletedTask;
        }

        private static async Task CheckpointsAsync(CancellationToken ct)
        {
            string dir = NewTempDirectory();
            try
            {
                File.WriteAllText(Path.Combine(dir, "a.txt"), "one");
                RunGit(dir, "init", "-q");

                using TelemetryCapture capture = new TelemetryCapture();
                GitCheckpointService service = new GitCheckpointService(dir);
                Activity root = capture.StartRoot("test checkpoints");
                string sha = await service.CaptureAsync("otel", ct).ConfigureAwait(false);
                bool restoreFailed = false;
                try
                {
                    await service.RestoreAsync("0000000000000000000000000000000000000000", ct).ConfigureAwait(false);
                }
                catch (Exception)
                {
                    restoreFailed = true;
                }

                root.Stop();

                MuxAssert.IsFalse(string.IsNullOrWhiteSpace(sha), "captured");
                MuxAssert.IsTrue(restoreFailed, "restore of an unknown snapshot failed");
                List<Activity> spans = capture.SpansInTrace(root.TraceId);
                Activity capture1 = Single(spans, "checkpoint capture");
                MuxAssert.AreEqual(ActivityStatusCode.Ok, capture1.Status, "capture span ok");
                MuxAssert.IsTrue(spans.Any((Activity a) => a.DisplayName == "git write-tree" && a.ParentSpanId == capture1.SpanId && a.Kind == ActivityKind.Client), "git client span under capture");
                MuxAssert.AreEqual(ActivityStatusCode.Error, Single(spans, "checkpoint restore").Status, "restore span failed");
                MuxAssert.IsTrue(capture.Any(MuxTelemetryNames.CheckpointOperations, "operation=capture", "outcome=success"), "capture counted");
                MuxAssert.IsTrue(capture.Any(MuxTelemetryNames.CheckpointOperations, "operation=restore", "outcome=error"), "restore failure counted");
                MuxAssert.IsTrue(capture.Any(MuxTelemetryNames.IntegrationRequests, "service=git", "operation=write-tree", "outcome=success"), "git integration counted");
                MuxAssert.IsTrue(capture.Any(MuxTelemetryNames.IntegrationRequests, "service=git", "outcome=failure"), "git non-zero exit counted");
            }
            finally
            {
                TryDeleteDirectory(dir);
            }
        }

        private static Task BuildAndConfigGaugesAsync(CancellationToken ct)
        {
            using TelemetryCapture capture = new TelemetryCapture();
            MuxTelemetry.SetConfiguration(4, 2, 3);
            capture.CollectObservables();

            MuxAssert.IsTrue(capture.Any(MuxTelemetryNames.BuildInfo, "service.version=" + Mux.Core.Settings.Defaults.ProductVersion), "build info gauge");
            MuxAssert.IsTrue(capture.Measurements(MuxTelemetryNames.ConfigEndpoints).Any((CapturedMeasurement m) => m.Value == 4), "endpoint count gauge");
            MuxAssert.IsTrue(capture.Measurements(MuxTelemetryNames.ConfigMcpServers).Any((CapturedMeasurement m) => m.Value == 2), "mcp count gauge");
            MuxAssert.IsTrue(capture.Measurements(MuxTelemetryNames.ConfigMaxConcurrency).Any((CapturedMeasurement m) => m.Value == 3), "concurrency gauge");
            return Task.CompletedTask;
        }

        #endregion

        #region Helpers

        private static Activity Single(List<Activity> spans, string name)
        {
            List<Activity> matches = spans.Where((Activity a) => string.Equals(a.DisplayName, name, StringComparison.Ordinal)).ToList();
            if (matches.Count != 1)
            {
                throw new AssertionFailedException("expected exactly one '" + name + "' span, found " + matches.Count
                    + " (spans: " + string.Join(", ", spans.Select((Activity a) => a.DisplayName)) + ")");
            }

            return matches[0];
        }

        private static async IAsyncEnumerable<AgentEvent> CompleteImmediatelyAsync(Job job, string prompt, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Yield();
            yield return new RunCompletedEvent { Status = "completed" };
        }

        private static async IAsyncEnumerable<AgentEvent> ThrowingRunnerAsync(Job job, string prompt, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Yield();
            throw new InvalidOperationException("runner exploded (test)");
#pragma warning disable CS0162
            yield break;
#pragma warning restore CS0162
        }

        private static async Task WaitForStateAsync(Job job, JobState state, CancellationToken ct)
        {
            bool reached = await TelemetryCapture.WaitForAsync(() => job.State == state, 10000, ct).ConfigureAwait(false);
            MuxAssert.IsTrue(reached, "job reached " + state);
        }

        private static int ClosedLoopbackPort()
        {
            TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        private static string NewTempDirectory()
        {
            string dir = Path.Combine(Path.GetTempPath(), "mux_otel_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        private static void TryDeleteDirectory(string dir)
        {
            try
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
            catch (Exception)
            {
            }
        }

        private static void SqliteConnectionPoolClear()
        {
            try
            {
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            }
            catch (Exception)
            {
            }
        }

        private static void RunGit(string dir, params string[] args)
        {
            ProcessStartInfo info = new ProcessStartInfo("git") { WorkingDirectory = dir, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (string arg in args) info.ArgumentList.Add(arg);
            using Process process = Process.Start(info)!;
            process.WaitForExit(10000);
        }

        #endregion
    }
}
