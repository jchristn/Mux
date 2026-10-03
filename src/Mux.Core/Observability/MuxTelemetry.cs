namespace Mux.Core.Observability
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Diagnostics.Metrics;
    using System.Runtime.InteropServices;
    using System.Threading;
    using Mux.Core.Settings;

    /// <summary>
    /// Mux's application-level telemetry: one <see cref="Meter"/> and one <see cref="ActivitySource"/>, both
    /// named <see cref="MuxTelemetryNames.MeterName"/> ("Mux"), plus typed recording helpers for every
    /// instrumented code path. Emission rides the .NET base class library only; mux takes no exporter or SDK
    /// dependency and opens no connection. A host collects by subscribing to the names (for example with
    /// Radiant's <c>settings.Sources.AddMeter("Mux")</c> / <c>AddActivitySource("Mux")</c>). When nothing is
    /// listening every call is effectively free.
    /// <para>
    /// Instrumentation is best-effort: every method here swallows its own failures so telemetry can never
    /// affect a run. Labels are bounded by construction (codes are sanitized; ids and free-form text go on
    /// spans, never on metrics). Thread safety: all members are safe to call concurrently.
    /// </para>
    /// </summary>
    public static class MuxTelemetry
    {
        #region Private-Members

        private static readonly Meter _Meter = new Meter(MuxTelemetryNames.MeterName, Defaults.ProductVersion);
        private static readonly ActivitySource _Source = new ActivitySource(MuxTelemetryNames.ActivitySourceName, Defaults.ProductVersion);

        private static readonly Counter<long> _AgentRuns = _Meter.CreateCounter<long>(MuxTelemetryNames.AgentRuns, "{run}", "Completed agent runs by outcome and call kind.");
        private static readonly Histogram<double> _AgentRunDuration = _Meter.CreateHistogram<double>(MuxTelemetryNames.AgentRunDuration, "s", "End-to-end agent run duration.");
        private static readonly UpDownCounter<long> _AgentRunsActive = _Meter.CreateUpDownCounter<long>(MuxTelemetryNames.AgentRunsActive, "{run}", "Agent runs currently executing.");
        private static readonly Histogram<double> _AgentStageDuration = _Meter.CreateHistogram<double>(MuxTelemetryNames.AgentStageDuration, "s", "Per-stage agent workflow duration.");
        private static readonly Histogram<long> _AgentIterations = _Meter.CreateHistogram<long>(MuxTelemetryNames.AgentIterations, "{iteration}", "Loop iterations (model calls) per agent run.");
        private static readonly Counter<long> _AgentErrors = _Meter.CreateCounter<long>(MuxTelemetryNames.AgentErrors, "{error}", "Error events raised by agent runs by error type.");
        private static readonly Counter<long> _AgentCompactions = _Meter.CreateCounter<long>(MuxTelemetryNames.AgentCompactions, "{compaction}", "In-run context compactions by strategy.");

        private static readonly Counter<long> _ToolCalls = _Meter.CreateCounter<long>(MuxTelemetryNames.ToolCalls, "{call}", "Tool calls by kind, name, and outcome.");
        private static readonly Histogram<double> _ToolDuration = _Meter.CreateHistogram<double>(MuxTelemetryNames.ToolDuration, "s", "Tool execution duration.");
        private static readonly Counter<long> _ApprovalDecisions = _Meter.CreateCounter<long>(MuxTelemetryNames.ApprovalDecisions, "{decision}", "Tool-call approval decisions.");
        private static readonly Counter<long> _SubagentRuns = _Meter.CreateCounter<long>(MuxTelemetryNames.SubagentRuns, "{run}", "Subagent runs by outcome.");
        private static readonly Histogram<double> _SubagentDuration = _Meter.CreateHistogram<double>(MuxTelemetryNames.SubagentDuration, "s", "Subagent run duration.");

        private static readonly Counter<long> _LlmRequests = _Meter.CreateCounter<long>(MuxTelemetryNames.LlmRequests, "{request}", "LLM requests by provider, operation, and outcome.");
        private static readonly Histogram<double> _LlmRequestDuration = _Meter.CreateHistogram<double>(MuxTelemetryNames.LlmRequestDuration, "s", "LLM request duration.");
        private static readonly Histogram<double> _LlmTimeToFirstToken = _Meter.CreateHistogram<double>(MuxTelemetryNames.LlmTimeToFirstToken, "s", "Streaming time to first token.");
        private static readonly Counter<long> _LlmTokens = _Meter.CreateCounter<long>(MuxTelemetryNames.LlmTokens, "{token}", "Provider-reported tokens by provider and token type.");
        private static readonly Counter<long> _LlmRetries = _Meter.CreateCounter<long>(MuxTelemetryNames.LlmRetries, "{retry}", "Transport-level LLM retries.");

        private static readonly Counter<long> _IntegrationRequests = _Meter.CreateCounter<long>(MuxTelemetryNames.IntegrationRequests, "{request}", "Outbound integration calls by service, operation, and outcome.");
        private static readonly Histogram<double> _IntegrationDuration = _Meter.CreateHistogram<double>(MuxTelemetryNames.IntegrationDuration, "s", "Outbound integration call duration.");

        private static readonly Counter<long> _Jobs = _Meter.CreateCounter<long>(MuxTelemetryNames.Jobs, "{job}", "Finished background jobs by outcome.");
        private static readonly Histogram<double> _JobStageDuration = _Meter.CreateHistogram<double>(MuxTelemetryNames.JobStageDuration, "s", "Per-stage job duration (queued, run).");
        private static readonly Counter<long> _JobStageEvents = _Meter.CreateCounter<long>(MuxTelemetryNames.JobStageEvents, "{event}", "Job stage completions by stage and outcome.");
        private static readonly Histogram<double> _WriteLeaseWaitDuration = _Meter.CreateHistogram<double>(MuxTelemetryNames.WriteLeaseWaitDuration, "s", "Workspace write-lease wait time.");
        private static readonly UpDownCounter<long> _WriteLeaseWaiters = _Meter.CreateUpDownCounter<long>(MuxTelemetryNames.WriteLeaseWaiters, "{job}", "Jobs waiting for the workspace write lease.");

        private static readonly Counter<long> _UsageEvents = _Meter.CreateCounter<long>(MuxTelemetryNames.UsageEvents, "{event}", "Usage-telemetry events by pipeline outcome.");
        private static readonly Histogram<double> _UsageWriteDuration = _Meter.CreateHistogram<double>(MuxTelemetryNames.UsageWriteDuration, "s", "Usage-store batch write duration.");

        private static readonly Counter<long> _SessionOperations = _Meter.CreateCounter<long>(MuxTelemetryNames.SessionOperations, "{operation}", "Session-store operations by operation and outcome.");
        private static readonly Histogram<double> _SessionOperationDuration = _Meter.CreateHistogram<double>(MuxTelemetryNames.SessionOperationDuration, "s", "Session-store operation duration.");
        private static readonly Counter<long> _RunsCompleted = _Meter.CreateCounter<long>(MuxTelemetryNames.RunsCompleted, "{run}", "Runs reaching a terminal state in a run registry.");
        private static readonly Counter<long> _CheckpointOperations = _Meter.CreateCounter<long>(MuxTelemetryNames.CheckpointOperations, "{operation}", "Git checkpoint operations by operation and outcome.");
        private static readonly Histogram<double> _CheckpointDuration = _Meter.CreateHistogram<double>(MuxTelemetryNames.CheckpointDuration, "s", "Git checkpoint operation duration.");

        private static readonly object _GaugeLock = new object();
        private static readonly List<MuxGaugeSource> _GaugeSources = new List<MuxGaugeSource>();

        private static long _LastJobSuccessUnixMs = 0;
        private static long _ConfigEndpoints = -1;
        private static long _ConfigMcpServers = -1;
        private static long _ConfigMaxConcurrency = -1;

        private static readonly string[] _LongHistogramNames = new string[]
        {
            MuxTelemetryNames.AgentRunDuration,
            MuxTelemetryNames.AgentStageDuration,
            MuxTelemetryNames.SubagentDuration,
            MuxTelemetryNames.LlmRequestDuration,
            MuxTelemetryNames.LlmTimeToFirstToken,
            MuxTelemetryNames.IntegrationDuration,
            MuxTelemetryNames.JobStageDuration,
            MuxTelemetryNames.WriteLeaseWaitDuration,
            MuxTelemetryNames.ToolDuration
        };

        private static readonly string[] _ShortHistogramNames = new string[]
        {
            MuxTelemetryNames.UsageWriteDuration,
            MuxTelemetryNames.SessionOperationDuration,
            MuxTelemetryNames.CheckpointDuration
        };

        private static readonly double[] _LongBuckets = new double[]
        {
            0.01, 0.05, 0.1, 0.25, 0.5, 1, 2.5, 5, 10, 20, 30, 60, 120, 300, 600, 1800
        };

        private static readonly double[] _ShortBuckets = new double[]
        {
            0.0005, 0.001, 0.0025, 0.005, 0.01, 0.025, 0.05, 0.1, 0.25, 0.5, 1, 2.5, 5, 10
        };

        #endregion

        #region Public-Members

        /// <summary>
        /// The mux activity source. Exposed for hosts and tests; application code should prefer
        /// <see cref="StartActivity(string, ActivityKind)"/>, which never throws.
        /// </summary>
        public static ActivitySource Source
        {
            get => _Source;
        }

        /// <summary>
        /// Histogram instrument names whose values span milliseconds to tens of minutes (agent runs, model
        /// calls, tools, integrations, job stages). A host should give these the <see cref="LongBuckets"/>
        /// boundaries; the SDK defaults are tuned for milliseconds and would put every observation in one bucket.
        /// </summary>
        public static IReadOnlyList<string> LongHistogramNames
        {
            get => _LongHistogramNames;
        }

        /// <summary>
        /// Histogram instrument names for fast local operations (session files, usage writes, checkpoints).
        /// A host should give these the <see cref="ShortBuckets"/> boundaries.
        /// </summary>
        public static IReadOnlyList<string> ShortHistogramNames
        {
            get => _ShortHistogramNames;
        }

        /// <summary>
        /// Recommended bucket boundaries in seconds for <see cref="LongHistogramNames"/>: 10 ms to 30 minutes.
        /// Returns a defensive copy.
        /// </summary>
        public static double[] LongBuckets
        {
            get => (double[])_LongBuckets.Clone();
        }

        /// <summary>
        /// Recommended bucket boundaries in seconds for <see cref="ShortHistogramNames"/>: 0.5 ms to 10 s.
        /// Returns a defensive copy.
        /// </summary>
        public static double[] ShortBuckets
        {
            get => (double[])_ShortBuckets.Clone();
        }

        #endregion

        #region Constructors-and-Factories

        static MuxTelemetry()
        {
            try
            {
                // No unit: a unit of "1" makes Prometheus exporters append "_ratio" to the info series name.
                _Meter.CreateObservableGauge<long>(MuxTelemetryNames.BuildInfo, ObserveBuildInfo, null, "Build information; always 1.");
                _Meter.CreateObservableGauge<long>(MuxTelemetryNames.JobsQueued, () => SumGauge(MuxTelemetryNames.JobsQueued), "{job}", "Jobs waiting for a concurrency slot.");
                _Meter.CreateObservableGauge<long>(MuxTelemetryNames.JobsActive, () => SumGauge(MuxTelemetryNames.JobsActive), "{job}", "Jobs holding a concurrency slot.");
                _Meter.CreateObservableGauge<long>(MuxTelemetryNames.JobsCapacity, () => SumGauge(MuxTelemetryNames.JobsCapacity), "{job}", "Job concurrency slots across live job managers.");
                _Meter.CreateObservableGauge<double>(MuxTelemetryNames.JobLastSuccess, ObserveLastJobSuccess, "s", "Unix time of the last successfully completed job.");
                _Meter.CreateObservableGauge<long>(MuxTelemetryNames.UsageQueueDepth, () => SumGauge(MuxTelemetryNames.UsageQueueDepth), "{event}", "Usage events buffered and not yet persisted.");
                _Meter.CreateObservableGauge<long>(MuxTelemetryNames.RunsActive, () => SumGauge(MuxTelemetryNames.RunsActive), "{run}", "Non-terminal runs tracked by live run registries.");
                _Meter.CreateObservableGauge<long>(MuxTelemetryNames.ConfigEndpoints, () => ObserveConfig(ref _ConfigEndpoints), "{endpoint}", "Configured LLM endpoints.");
                _Meter.CreateObservableGauge<long>(MuxTelemetryNames.ConfigMcpServers, () => ObserveConfig(ref _ConfigMcpServers), "{server}", "Configured MCP servers.");
                _Meter.CreateObservableGauge<long>(MuxTelemetryNames.ConfigMaxConcurrency, () => ObserveConfig(ref _ConfigMaxConcurrency), "{job}", "Configured job concurrency limit.");
            }
            catch (Exception)
            {
                // Best-effort: telemetry setup must never prevent mux from loading.
            }
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Starts a span on the mux activity source, parented to <see cref="Activity.Current"/>. Never throws.
        /// </summary>
        /// <param name="name">A low-cardinality span name (for example <c>stage:approval</c> or <c>llm chat</c>).</param>
        /// <param name="kind">The span kind. Defaults to <see cref="ActivityKind.Internal"/>.</param>
        /// <returns>The started activity, or null when no listener is sampling or creation failed.</returns>
        public static Activity? StartActivity(string name, ActivityKind kind = ActivityKind.Internal)
        {
            try
            {
                return _Source.StartActivity(name, kind);
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Starts a span with an explicit parent context, used for background hand-offs where
        /// <see cref="Activity.Current"/> does not flow (for example a job dequeued by a worker). Never throws.
        /// </summary>
        /// <param name="name">A low-cardinality span name.</param>
        /// <param name="kind">The span kind.</param>
        /// <param name="parentContext">The parent context; default starts a new trace.</param>
        /// <param name="startTime">Optional explicit start time (for spans reconstructed after the fact, such as a queue wait).</param>
        /// <returns>The started activity, or null when no listener is sampling or creation failed.</returns>
        public static Activity? StartActivity(string name, ActivityKind kind, ActivityContext parentContext, DateTimeOffset startTime = default)
        {
            try
            {
                return _Source.StartActivity(name, kind, parentContext, null, null, startTime);
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Sets a span attribute. Never throws; a null activity is ignored.
        /// </summary>
        /// <param name="activity">The span, or null.</param>
        /// <param name="key">The attribute key.</param>
        /// <param name="value">The attribute value. Null removes the attribute.</param>
        public static void SetTag(Activity? activity, string key, object? value)
        {
            if (activity == null) return;
            try
            {
                activity.SetTag(key, value);
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// Marks a span OK. Never throws.
        /// </summary>
        /// <param name="activity">The span, or null.</param>
        public static void SetOk(Activity? activity)
        {
            if (activity == null) return;
            try
            {
                activity.SetStatus(ActivityStatusCode.Ok);
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// Marks a span failed with a bounded error type and an optional description. Never throws.
        /// </summary>
        /// <param name="activity">The span, or null.</param>
        /// <param name="errorType">A bounded error code (sanitized and stored as <c>error.type</c>).</param>
        /// <param name="description">An optional human-readable description (span only).</param>
        public static void SetError(Activity? activity, string? errorType, string? description = null)
        {
            if (activity == null) return;
            try
            {
                activity.SetTag(MuxTelemetryNames.LabelErrorType, SanitizeCode(errorType));
                activity.SetStatus(ActivityStatusCode.Error, description);
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// Records an exception on a span as an OpenTelemetry <c>exception</c> event and marks the span
        /// failed. The message is attached; the stack trace is not, to keep spans small. Never throws.
        /// </summary>
        /// <param name="activity">The span, or null.</param>
        /// <param name="exception">The exception, or null.</param>
        public static void RecordException(Activity? activity, Exception? exception)
        {
            if (activity == null || exception == null) return;
            try
            {
                ActivityTagsCollection tags = new ActivityTagsCollection
                {
                    { "exception.type", exception.GetType().FullName },
                    { "exception.message", exception.Message }
                };
                activity.AddEvent(new ActivityEvent("exception", DateTimeOffset.UtcNow, tags));
                activity.SetTag(MuxTelemetryNames.LabelErrorType, exception.GetType().Name);
                activity.SetStatus(ActivityStatusCode.Error, exception.Message);
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// Stops and disposes a span. Never throws.
        /// </summary>
        /// <param name="activity">The span, or null.</param>
        public static void Stop(Activity? activity)
        {
            if (activity == null) return;
            try
            {
                activity.Dispose();
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// Normalizes an arbitrary code into a bounded label value: lower-case <c>[a-z0-9_./-]</c>, at most
        /// 48 characters. Anything else, or a blank value, becomes <c>other</c>.
        /// </summary>
        /// <param name="code">The candidate code.</param>
        /// <returns>A safe label value. Never null.</returns>
        public static string SanitizeCode(string? code)
        {
            if (string.IsNullOrWhiteSpace(code) || code.Length > 48) return "other";
            string lower = code.Trim().ToLowerInvariant();
            foreach (char c in lower)
            {
                bool ok = (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_' || c == '.' || c == '/' || c == '-';
                if (!ok) return "other";
            }

            return lower;
        }

        /// <summary>
        /// Converts elapsed <see cref="Stopwatch"/> ticks (from <see cref="Stopwatch.GetTimestamp"/>) to seconds.
        /// </summary>
        /// <param name="startTimestamp">The starting timestamp.</param>
        /// <returns>Elapsed seconds since <paramref name="startTimestamp"/>.</returns>
        public static double SecondsSince(long startTimestamp)
        {
            return (Stopwatch.GetTimestamp() - startTimestamp) / (double)Stopwatch.Frequency;
        }

        /// <summary>
        /// Registers a live contributor to an observable gauge. Never throws.
        /// </summary>
        /// <param name="gaugeName">The gauge (for example <see cref="MuxTelemetryNames.JobsQueued"/>).</param>
        /// <param name="owner">The owning object, held weakly.</param>
        /// <param name="read">Reads the owner's current value.</param>
        public static void RegisterGaugeSource(string gaugeName, object owner, Func<object, long> read)
        {
            try
            {
                lock (_GaugeLock)
                {
                    _GaugeSources.RemoveAll((MuxGaugeSource s) => s.IsDead());
                    _GaugeSources.Add(new MuxGaugeSource(gaugeName, owner, read));
                }
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// Removes every gauge contribution registered by <paramref name="owner"/>. Never throws.
        /// </summary>
        /// <param name="owner">The owner passed to <see cref="RegisterGaugeSource"/>.</param>
        public static void UnregisterGaugeSources(object owner)
        {
            if (owner == null) return;
            try
            {
                lock (_GaugeLock)
                {
                    _GaugeSources.RemoveAll((MuxGaugeSource s) => s.IsDead() || s.IsOwnedBy(owner));
                }
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// Publishes safe configuration gauges (counts only; never names, URLs, or secrets). Called by a
        /// host at startup. Negative values leave a gauge unreported.
        /// </summary>
        /// <param name="endpointCount">Configured LLM endpoints.</param>
        /// <param name="mcpServerCount">Configured MCP servers.</param>
        /// <param name="maxConcurrency">Configured job concurrency limit.</param>
        public static void SetConfiguration(int endpointCount, int mcpServerCount, int maxConcurrency)
        {
            Interlocked.Exchange(ref _ConfigEndpoints, endpointCount);
            Interlocked.Exchange(ref _ConfigMcpServers, mcpServerCount);
            Interlocked.Exchange(ref _ConfigMaxConcurrency, maxConcurrency);
        }

        /// <summary>
        /// Marks an agent run started (active-run gauge up).
        /// </summary>
        public static void AgentRunStarted()
        {
            try
            {
                _AgentRunsActive.Add(1);
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// Records a finished agent run (active-run gauge down, outcome counter, duration, iterations).
        /// </summary>
        /// <param name="outcome">Run outcome (a <c>RunCompletedEvent</c> status, or cancelled/failed/abandoned).</param>
        /// <param name="callKind">The usage call kind (primary, compaction, subagent, chat).</param>
        /// <param name="seconds">Run duration in seconds.</param>
        /// <param name="iterations">Loop iterations completed.</param>
        public static void RecordAgentRun(string outcome, string callKind, double seconds, int iterations)
        {
            try
            {
                _AgentRunsActive.Add(-1);
                TagList tags = new TagList
                {
                    { MuxTelemetryNames.LabelOutcome, SanitizeCode(outcome) },
                    { MuxTelemetryNames.LabelCallKind, SanitizeCode(callKind) }
                };
                _AgentRuns.Add(1, tags);
                _AgentRunDuration.Record(seconds, tags);
                _AgentIterations.Record(iterations, new KeyValuePair<string, object?>(MuxTelemetryNames.LabelOutcome, SanitizeCode(outcome)));
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// Records the duration of one agent workflow stage.
        /// </summary>
        /// <param name="stage">One of the <c>Stage*</c> values in <see cref="MuxTelemetryNames"/>.</param>
        /// <param name="outcome">Stage outcome.</param>
        /// <param name="seconds">Stage duration in seconds.</param>
        public static void RecordAgentStage(string stage, string outcome, double seconds)
        {
            try
            {
                _AgentStageDuration.Record(
                    seconds,
                    new KeyValuePair<string, object?>(MuxTelemetryNames.LabelStage, stage),
                    new KeyValuePair<string, object?>(MuxTelemetryNames.LabelOutcome, SanitizeCode(outcome)));
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// Records an error event raised by an agent run.
        /// </summary>
        /// <param name="errorCode">The <c>ErrorEvent.Code</c>; sanitized to a bounded value.</param>
        public static void RecordAgentError(string? errorCode)
        {
            try
            {
                _AgentErrors.Add(1, new KeyValuePair<string, object?>(MuxTelemetryNames.LabelErrorType, SanitizeCode(errorCode)));
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// Records an in-run context compaction.
        /// </summary>
        /// <param name="strategy">The strategy applied (summary, trim, summary+trim).</param>
        public static void RecordCompaction(string? strategy)
        {
            try
            {
                _AgentCompactions.Add(1, new KeyValuePair<string, object?>(MuxTelemetryNames.LabelStrategy, SanitizeCode(strategy)));
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// Records a tool call.
        /// </summary>
        /// <param name="toolKind">builtin, mcp, skill, external, or unknown.</param>
        /// <param name="toolName">The label-safe tool name (a built-in name, or the kind for other tools).</param>
        /// <param name="outcome">success, failure, error, or denied.</param>
        /// <param name="seconds">Execution duration in seconds; negative skips the histogram (denied calls never execute).</param>
        public static void RecordToolCall(string toolKind, string toolName, string outcome, double seconds)
        {
            try
            {
                TagList tags = new TagList
                {
                    { MuxTelemetryNames.LabelToolKind, toolKind },
                    { MuxTelemetryNames.LabelToolName, SanitizeCode(toolName) },
                    { MuxTelemetryNames.LabelOutcome, outcome }
                };
                _ToolCalls.Add(1, tags);
                if (seconds >= 0) _ToolDuration.Record(seconds, tags);
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// Records a tool-call approval decision.
        /// </summary>
        /// <param name="decision">approved, denied, policy_denied, or error.</param>
        public static void RecordApproval(string decision)
        {
            try
            {
                _ApprovalDecisions.Add(1, new KeyValuePair<string, object?>(MuxTelemetryNames.LabelDecision, decision));
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// Records a subagent run.
        /// </summary>
        /// <param name="outcome">The subagent outcome.</param>
        /// <param name="seconds">Duration in seconds.</param>
        public static void RecordSubagent(string outcome, double seconds)
        {
            try
            {
                KeyValuePair<string, object?> tag = new KeyValuePair<string, object?>(MuxTelemetryNames.LabelOutcome, SanitizeCode(outcome));
                _SubagentRuns.Add(1, tag);
                _SubagentDuration.Record(seconds, tag);
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// Records one LLM request (and mirrors it into the integration family as service <c>llm</c>).
        /// </summary>
        /// <param name="provider">The provider (mux adapter type, lower case).</param>
        /// <param name="operation">chat, chat_stream, or model_load.</param>
        /// <param name="outcome">success, or a bounded failure code.</param>
        /// <param name="seconds">Request duration in seconds.</param>
        public static void RecordLlmRequest(string provider, string operation, string outcome, double seconds)
        {
            try
            {
                TagList tags = new TagList
                {
                    { MuxTelemetryNames.LabelProvider, provider },
                    { MuxTelemetryNames.LabelLlmOperation, operation },
                    { MuxTelemetryNames.LabelOutcome, SanitizeCode(outcome) }
                };
                _LlmRequests.Add(1, tags);
                _LlmRequestDuration.Record(seconds, tags);
            }
            catch (Exception)
            {
            }

            RecordIntegration(MuxTelemetryNames.ServiceLlm, operation, outcome, seconds);
        }

        /// <summary>
        /// Records streaming time-to-first-token.
        /// </summary>
        /// <param name="provider">The provider.</param>
        /// <param name="seconds">Time to first token in seconds.</param>
        public static void RecordTimeToFirstToken(string provider, double seconds)
        {
            try
            {
                _LlmTimeToFirstToken.Record(seconds, new KeyValuePair<string, object?>(MuxTelemetryNames.LabelProvider, provider));
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// Records provider-reported tokens. Zero counts are skipped.
        /// </summary>
        /// <param name="provider">The provider.</param>
        /// <param name="tokenType">input, output, cached, or reasoning.</param>
        /// <param name="count">The token count.</param>
        public static void RecordTokens(string provider, string tokenType, long count)
        {
            if (count <= 0) return;
            try
            {
                _LlmTokens.Add(
                    count,
                    new KeyValuePair<string, object?>(MuxTelemetryNames.LabelProvider, provider),
                    new KeyValuePair<string, object?>(MuxTelemetryNames.LabelTokenType, tokenType));
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// Records a transport-level LLM retry.
        /// </summary>
        /// <param name="provider">The provider.</param>
        /// <param name="operation">The LLM operation being retried.</param>
        public static void RecordLlmRetry(string provider, string operation)
        {
            try
            {
                _LlmRetries.Add(
                    1,
                    new KeyValuePair<string, object?>(MuxTelemetryNames.LabelProvider, provider),
                    new KeyValuePair<string, object?>(MuxTelemetryNames.LabelLlmOperation, operation));
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// Records one outbound integration call.
        /// </summary>
        /// <param name="service">llm, mcp, web_search, git, or hook.</param>
        /// <param name="operation">A bounded operation name for the service.</param>
        /// <param name="outcome">success, failure, error, cancelled, or timeout.</param>
        /// <param name="seconds">Call duration in seconds.</param>
        public static void RecordIntegration(string service, string operation, string outcome, double seconds)
        {
            try
            {
                TagList tags = new TagList
                {
                    { MuxTelemetryNames.LabelService, service },
                    { MuxTelemetryNames.LabelOperation, SanitizeCode(operation) },
                    { MuxTelemetryNames.LabelOutcome, SanitizeCode(outcome) }
                };
                _IntegrationRequests.Add(1, tags);
                _IntegrationDuration.Record(seconds, tags);
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// Records a job stage (queued or run).
        /// </summary>
        /// <param name="stage"><see cref="MuxTelemetryNames.StageQueued"/> or <see cref="MuxTelemetryNames.StageRun"/>.</param>
        /// <param name="outcome">Stage outcome.</param>
        /// <param name="seconds">Stage duration in seconds.</param>
        public static void RecordJobStage(string stage, string outcome, double seconds)
        {
            try
            {
                KeyValuePair<string, object?> stageTag = new KeyValuePair<string, object?>(MuxTelemetryNames.LabelStage, stage);
                _JobStageDuration.Record(seconds, stageTag);
                _JobStageEvents.Add(1, stageTag, new KeyValuePair<string, object?>(MuxTelemetryNames.LabelOutcome, outcome));
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// Records a finished job, updating the last-success timestamp when it completed.
        /// </summary>
        /// <param name="outcome">completed, cancelled, or failed.</param>
        public static void RecordJob(string outcome)
        {
            try
            {
                _Jobs.Add(1, new KeyValuePair<string, object?>(MuxTelemetryNames.LabelOutcome, outcome));
                if (string.Equals(outcome, "completed", StringComparison.Ordinal))
                {
                    Interlocked.Exchange(ref _LastJobSuccessUnixMs, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                }
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// Adjusts the write-lease waiter gauge.
        /// </summary>
        /// <param name="delta">+1 when a job starts waiting, -1 when it stops.</param>
        public static void AddWriteLeaseWaiter(int delta)
        {
            try
            {
                _WriteLeaseWaiters.Add(delta);
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// Records a write-lease wait.
        /// </summary>
        /// <param name="outcome">acquired, cancelled, or timeout.</param>
        /// <param name="seconds">Wait duration in seconds.</param>
        public static void RecordWriteLeaseWait(string outcome, double seconds)
        {
            try
            {
                _WriteLeaseWaitDuration.Record(seconds, new KeyValuePair<string, object?>(MuxTelemetryNames.LabelOutcome, outcome));
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// Records usage-telemetry pipeline events.
        /// </summary>
        /// <param name="outcome">enqueued, dropped, written, or write_failed.</param>
        /// <param name="count">Number of events.</param>
        public static void RecordUsageEvents(string outcome, long count)
        {
            if (count <= 0) return;
            try
            {
                _UsageEvents.Add(count, new KeyValuePair<string, object?>(MuxTelemetryNames.LabelOutcome, outcome));
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// Records a usage-store batch write.
        /// </summary>
        /// <param name="outcome">success or error.</param>
        /// <param name="seconds">Write duration in seconds.</param>
        public static void RecordUsageWrite(string outcome, double seconds)
        {
            try
            {
                _UsageWriteDuration.Record(seconds, new KeyValuePair<string, object?>(MuxTelemetryNames.LabelOutcome, outcome));
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// Records a session-store operation.
        /// </summary>
        /// <param name="operation">save, load, delete, list, or duplicate.</param>
        /// <param name="outcome">success, failure (not found), or error.</param>
        /// <param name="seconds">Duration in seconds.</param>
        public static void RecordSessionOperation(string operation, string outcome, double seconds)
        {
            try
            {
                TagList tags = new TagList
                {
                    { MuxTelemetryNames.LabelOperation, operation },
                    { MuxTelemetryNames.LabelOutcome, outcome }
                };
                _SessionOperations.Add(1, tags);
                _SessionOperationDuration.Record(seconds, tags);
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// Records a run reaching a terminal state in a run registry.
        /// </summary>
        /// <param name="status">The terminal run status.</param>
        public static void RecordRunCompleted(string status)
        {
            try
            {
                _RunsCompleted.Add(1, new KeyValuePair<string, object?>(MuxTelemetryNames.LabelStatus, SanitizeCode(status)));
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// Records a git checkpoint operation.
        /// </summary>
        /// <param name="operation">capture or restore.</param>
        /// <param name="outcome">success or error.</param>
        /// <param name="seconds">Duration in seconds.</param>
        public static void RecordCheckpoint(string operation, string outcome, double seconds)
        {
            try
            {
                TagList tags = new TagList
                {
                    { MuxTelemetryNames.LabelOperation, operation },
                    { MuxTelemetryNames.LabelOutcome, outcome }
                };
                _CheckpointOperations.Add(1, tags);
                _CheckpointDuration.Record(seconds, tags);
            }
            catch (Exception)
            {
            }
        }

        #endregion

        #region Private-Methods

        private static long SumGauge(string gaugeName)
        {
            long total = 0;
            try
            {
                lock (_GaugeLock)
                {
                    foreach (MuxGaugeSource source in _GaugeSources)
                    {
                        if (string.Equals(source.GaugeName, gaugeName, StringComparison.Ordinal))
                        {
                            total += source.Read();
                        }
                    }
                }
            }
            catch (Exception)
            {
            }

            return total;
        }

        private static IEnumerable<Measurement<long>> ObserveBuildInfo()
        {
            return new Measurement<long>[]
            {
                new Measurement<long>(
                    1,
                    new KeyValuePair<string, object?>(MuxTelemetryNames.LabelVersion, Defaults.ProductVersion),
                    new KeyValuePair<string, object?>(MuxTelemetryNames.LabelRuntime, RuntimeInformation.FrameworkDescription))
            };
        }

        private static IEnumerable<Measurement<double>> ObserveLastJobSuccess()
        {
            long unixMs = Interlocked.Read(ref _LastJobSuccessUnixMs);
            if (unixMs <= 0) return Array.Empty<Measurement<double>>();
            return new Measurement<double>[] { new Measurement<double>(unixMs / 1000.0) };
        }

        private static IEnumerable<Measurement<long>> ObserveConfig(ref long field)
        {
            long value = Interlocked.Read(ref field);
            if (value < 0) return Array.Empty<Measurement<long>>();
            return new Measurement<long>[] { new Measurement<long>(value) };
        }

        #endregion
    }
}
