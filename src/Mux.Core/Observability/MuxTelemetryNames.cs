namespace Mux.Core.Observability
{
    /// <summary>
    /// Every public telemetry name mux emits: the meter and activity-source names, each metric family, the
    /// bounded label keys, and the bounded label values. These strings are a public contract consumed by
    /// Grafana dashboards and alert rules (see <c>TELEMETRY.md</c>); treat a rename as a breaking change.
    /// Metric names are dotted OpenTelemetry instrument names; a Prometheus exporter rewrites them to snake
    /// case with unit and <c>_total</c> suffixes (for example <c>mux.agent.run.duration</c> becomes
    /// <c>mux_agent_run_duration_seconds</c>).
    /// </summary>
    public static class MuxTelemetryNames
    {
        #region Sources

        /// <summary>
        /// The <see cref="System.Diagnostics.Metrics.Meter"/> name every mux metric is emitted on. A host
        /// subscribes to this name (for example Radiant's <c>settings.Sources.AddMeter("Mux")</c>).
        /// </summary>
        public const string MeterName = "Mux";

        /// <summary>
        /// The <see cref="System.Diagnostics.ActivitySource"/> name every mux span is emitted on. A host
        /// subscribes to this name (for example Radiant's <c>settings.Sources.AddActivitySource("Mux")</c>).
        /// </summary>
        public const string ActivitySourceName = "Mux";

        #endregion

        #region Agent-Metrics

        /// <summary>Counter of completed agent runs, labeled by outcome and call kind.</summary>
        public const string AgentRuns = "mux.agent.runs";

        /// <summary>Histogram of end-to-end agent run duration in seconds, labeled by outcome and call kind.</summary>
        public const string AgentRunDuration = "mux.agent.run.duration";

        /// <summary>Up/down counter of agent runs currently executing in the process.</summary>
        public const string AgentRunsActive = "mux.agent.runs.active";

        /// <summary>Histogram of per-stage agent workflow duration in seconds, labeled by stage and outcome.</summary>
        public const string AgentStageDuration = "mux.agent.stage.duration";

        /// <summary>Histogram of loop iterations (model calls) per agent run, labeled by outcome.</summary>
        public const string AgentIterations = "mux.agent.iterations";

        /// <summary>Counter of error events raised by agent runs, labeled by error type.</summary>
        public const string AgentErrors = "mux.agent.errors";

        /// <summary>Counter of in-run context compactions, labeled by strategy.</summary>
        public const string AgentCompactions = "mux.agent.compactions";

        #endregion

        #region Tool-Metrics

        /// <summary>Counter of tool calls, labeled by tool kind, tool name, and outcome.</summary>
        public const string ToolCalls = "mux.tool.calls";

        /// <summary>Histogram of tool execution duration in seconds, labeled by tool kind, tool name, and outcome.</summary>
        public const string ToolDuration = "mux.tool.duration";

        /// <summary>Counter of tool-call approval decisions, labeled by decision.</summary>
        public const string ApprovalDecisions = "mux.approval.decisions";

        /// <summary>Counter of subagent runs, labeled by outcome.</summary>
        public const string SubagentRuns = "mux.subagent.runs";

        /// <summary>Histogram of subagent run duration in seconds, labeled by outcome.</summary>
        public const string SubagentDuration = "mux.subagent.duration";

        #endregion

        #region Llm-Metrics

        /// <summary>Counter of LLM requests, labeled by provider, operation, and outcome.</summary>
        public const string LlmRequests = "mux.llm.requests";

        /// <summary>Histogram of LLM request duration in seconds, labeled by provider, operation, and outcome.</summary>
        public const string LlmRequestDuration = "mux.llm.request.duration";

        /// <summary>Histogram of streaming time-to-first-token in seconds, labeled by provider.</summary>
        public const string LlmTimeToFirstToken = "mux.llm.time_to_first_token";

        /// <summary>Counter of provider-reported tokens, labeled by provider and token type.</summary>
        public const string LlmTokens = "mux.llm.tokens";

        /// <summary>Counter of transport-level LLM retries, labeled by provider and operation.</summary>
        public const string LlmRetries = "mux.llm.retries";

        #endregion

        #region Integration-Metrics

        /// <summary>Counter of outbound integration calls, labeled by service, operation, and outcome.</summary>
        public const string IntegrationRequests = "mux.integration.requests";

        /// <summary>Histogram of outbound integration call duration in seconds, labeled by service, operation, and outcome.</summary>
        public const string IntegrationDuration = "mux.integration.duration";

        #endregion

        #region Job-Metrics

        /// <summary>Counter of finished background jobs, labeled by outcome.</summary>
        public const string Jobs = "mux.jobs";

        /// <summary>Histogram of per-stage job duration in seconds (queued, run), labeled by stage.</summary>
        public const string JobStageDuration = "mux.job.stage.duration";

        /// <summary>Counter of job stage completions, labeled by stage and outcome.</summary>
        public const string JobStageEvents = "mux.job.stage.events";

        /// <summary>Observable gauge of jobs waiting for a concurrency slot.</summary>
        public const string JobsQueued = "mux.jobs.queued";

        /// <summary>Observable gauge of jobs holding a concurrency slot.</summary>
        public const string JobsActive = "mux.jobs.active";

        /// <summary>Observable gauge of total job concurrency slots across live job managers.</summary>
        public const string JobsCapacity = "mux.jobs.capacity";

        /// <summary>Observable gauge of the Unix time (seconds) of the last successfully completed job.</summary>
        public const string JobLastSuccess = "mux.job.last_success.timestamp";

        /// <summary>Histogram of workspace write-lease wait time in seconds, labeled by outcome.</summary>
        public const string WriteLeaseWaitDuration = "mux.write_lease.wait.duration";

        /// <summary>Up/down counter of jobs currently waiting for the workspace write lease.</summary>
        public const string WriteLeaseWaiters = "mux.write_lease.waiters";

        #endregion

        #region Usage-Recorder-Metrics

        /// <summary>Counter of usage-telemetry events by pipeline outcome (enqueued, dropped, written, write_failed).</summary>
        public const string UsageEvents = "mux.usage.events";

        /// <summary>Observable gauge of usage events buffered and not yet persisted.</summary>
        public const string UsageQueueDepth = "mux.usage.queue.depth";

        /// <summary>Histogram of usage-store batch write duration in seconds, labeled by outcome.</summary>
        public const string UsageWriteDuration = "mux.usage.write.duration";

        #endregion

        #region Session-Run-Checkpoint-Metrics

        /// <summary>Counter of session-store operations, labeled by operation and outcome.</summary>
        public const string SessionOperations = "mux.session.operations";

        /// <summary>Histogram of session-store operation duration in seconds, labeled by operation and outcome.</summary>
        public const string SessionOperationDuration = "mux.session.operation.duration";

        /// <summary>Observable gauge of non-terminal runs tracked by live run registries (REST/WebSocket hub).</summary>
        public const string RunsActive = "mux.runs.active";

        /// <summary>Counter of runs reaching a terminal state in a run registry, labeled by status.</summary>
        public const string RunsCompleted = "mux.runs.completed";

        /// <summary>Counter of git checkpoint operations, labeled by operation and outcome.</summary>
        public const string CheckpointOperations = "mux.checkpoint.operations";

        /// <summary>Histogram of git checkpoint operation duration in seconds, labeled by operation and outcome.</summary>
        public const string CheckpointDuration = "mux.checkpoint.duration";

        #endregion

        #region Build-Config-Metrics

        /// <summary>Observable gauge fixed at 1, labeled by product version and runtime.</summary>
        public const string BuildInfo = "mux.build.info";

        /// <summary>Observable gauge of configured LLM endpoints (set by the host).</summary>
        public const string ConfigEndpoints = "mux.config.endpoints";

        /// <summary>Observable gauge of configured MCP servers (set by the host).</summary>
        public const string ConfigMcpServers = "mux.config.mcp_servers";

        /// <summary>Observable gauge of the configured job concurrency limit (set by the host).</summary>
        public const string ConfigMaxConcurrency = "mux.config.max_concurrency";

        #endregion

        #region Label-Keys

        /// <summary>Label key: bounded outcome of an operation.</summary>
        public const string LabelOutcome = "outcome";

        /// <summary>Label key: agent call kind (primary, compaction, subagent, chat).</summary>
        public const string LabelCallKind = "call_kind";

        /// <summary>Label key: workflow or pipeline stage.</summary>
        public const string LabelStage = "stage";

        /// <summary>Label key: OpenTelemetry error type (a bounded error code).</summary>
        public const string LabelErrorType = "error.type";

        /// <summary>Label key: compaction strategy.</summary>
        public const string LabelStrategy = "strategy";

        /// <summary>Label key: tool kind (builtin, mcp, skill, external, unknown).</summary>
        public const string LabelToolKind = "tool.kind";

        /// <summary>Label key: tool name. Built-in tool names only; other kinds collapse to the kind.</summary>
        public const string LabelToolName = "tool.name";

        /// <summary>Label key: approval decision.</summary>
        public const string LabelDecision = "decision";

        /// <summary>Label key: OpenTelemetry GenAI provider (the mux adapter type).</summary>
        public const string LabelProvider = "gen_ai.provider.name";

        /// <summary>Label key: OpenTelemetry GenAI operation.</summary>
        public const string LabelLlmOperation = "gen_ai.operation.name";

        /// <summary>Label key: OpenTelemetry GenAI token type.</summary>
        public const string LabelTokenType = "gen_ai.token.type";

        /// <summary>Label key: integration service (llm, mcp, web_search, git, hook).</summary>
        public const string LabelService = "service";

        /// <summary>Label key: integration or store operation.</summary>
        public const string LabelOperation = "operation";

        /// <summary>Label key: run status.</summary>
        public const string LabelStatus = "status";

        /// <summary>Label key: product version (build info only).</summary>
        public const string LabelVersion = "service.version";

        /// <summary>Label key: .NET runtime description (build info only).</summary>
        public const string LabelRuntime = "runtime";

        #endregion

        #region Outcome-Values

        /// <summary>Outcome: the operation succeeded.</summary>
        public const string OutcomeSuccess = "success";

        /// <summary>Outcome: the operation returned a failure result.</summary>
        public const string OutcomeFailure = "failure";

        /// <summary>Outcome: the operation threw or the transport failed.</summary>
        public const string OutcomeError = "error";

        /// <summary>Outcome: the operation was cancelled.</summary>
        public const string OutcomeCancelled = "cancelled";

        /// <summary>Outcome: the operation was refused by policy or the user.</summary>
        public const string OutcomeDenied = "denied";

        /// <summary>Outcome: the operation timed out.</summary>
        public const string OutcomeTimeout = "timeout";

        /// <summary>Outcome: the consumer stopped enumerating before the run finished.</summary>
        public const string OutcomeAbandoned = "abandoned";

        /// <summary>Outcome: the run threw an unhandled exception.</summary>
        public const string OutcomeFailed = "failed";

        #endregion

        #region Stage-Values

        /// <summary>Agent stage: one model call (stream or completion).</summary>
        public const string StageLlm = "llm";

        /// <summary>Agent stage: approval of a proposed tool call (includes time waiting on a human).</summary>
        public const string StageApproval = "approval";

        /// <summary>Agent stage: execution of an approved tool call.</summary>
        public const string StageTool = "tool";

        /// <summary>Agent stage: in-run context compaction.</summary>
        public const string StageCompaction = "compaction";

        /// <summary>Agent stage: waiting for the shared workspace write lease before a mutating tool.</summary>
        public const string StageWriteLeaseWait = "write_lease_wait";

        /// <summary>Job stage: waiting for a concurrency slot.</summary>
        public const string StageQueued = "queued";

        /// <summary>Job stage: one agent turn for the job.</summary>
        public const string StageRun = "run";

        #endregion

        #region Service-Values

        /// <summary>Integration service: an LLM provider.</summary>
        public const string ServiceLlm = "llm";

        /// <summary>Integration service: an MCP server.</summary>
        public const string ServiceMcp = "mcp";

        /// <summary>Integration service: an external web-search provider.</summary>
        public const string ServiceWebSearch = "web_search";

        /// <summary>Integration service: the git CLI (checkpoints).</summary>
        public const string ServiceGit = "git";

        /// <summary>Integration service: a plugin hook subprocess.</summary>
        public const string ServiceHook = "hook";

        #endregion

        #region Span-Attribute-Keys

        /// <summary>Span attribute: mux run id.</summary>
        public const string AttrRunId = "mux.run.id";

        /// <summary>Span attribute: mux session id.</summary>
        public const string AttrSessionId = "mux.session.id";

        /// <summary>Span attribute: mux job id.</summary>
        public const string AttrJobId = "mux.job.id";

        /// <summary>Span attribute: configured endpoint name.</summary>
        public const string AttrEndpointName = "mux.endpoint.name";

        /// <summary>Span attribute: requested model.</summary>
        public const string AttrRequestModel = "gen_ai.request.model";

        /// <summary>Span attribute: model reported by the provider.</summary>
        public const string AttrResponseModel = "gen_ai.response.model";

        /// <summary>Span attribute: provider finish reason.</summary>
        public const string AttrFinishReasons = "gen_ai.response.finish_reasons";

        /// <summary>Span attribute: input tokens.</summary>
        public const string AttrInputTokens = "gen_ai.usage.input_tokens";

        /// <summary>Span attribute: output tokens.</summary>
        public const string AttrOutputTokens = "gen_ai.usage.output_tokens";

        /// <summary>Span attribute: tool name (full, unbounded; spans only).</summary>
        public const string AttrToolName = "gen_ai.tool.name";

        /// <summary>Span attribute: tool call id.</summary>
        public const string AttrToolCallId = "gen_ai.tool.call.id";

        /// <summary>Span attribute: server address of an outbound call.</summary>
        public const string AttrServerAddress = "server.address";

        /// <summary>Span attribute: MCP server name.</summary>
        public const string AttrMcpServer = "mux.mcp.server";

        /// <summary>Span attribute: MCP transport (stdio or http).</summary>
        public const string AttrMcpTransport = "mux.mcp.transport";

        /// <summary>Span attribute: loop iterations completed.</summary>
        public const string AttrIterations = "mux.agent.iterations";

        /// <summary>Span attribute: tool calls proposed in a run.</summary>
        public const string AttrToolCallCount = "mux.agent.tool_calls";

        /// <summary>Span attribute: subagent name.</summary>
        public const string AttrSubagentName = "mux.subagent.name";

        /// <summary>Span attribute: number of hooks run for an event.</summary>
        public const string AttrHookCount = "mux.hook.count";

        /// <summary>Span attribute: number of events in a usage write batch.</summary>
        public const string AttrBatchSize = "mux.usage.batch_size";

        #endregion
    }
}
