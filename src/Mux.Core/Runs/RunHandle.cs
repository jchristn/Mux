namespace Mux.Core.Runs
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Channels;
    using System.Threading.Tasks;
    using Mux.Core.Agent;
    using Mux.Core.Tasks;

    /// <summary>
    /// A first-class, addressable agent run. Holds the run's linked cancellation source (so an external cancel
    /// request and a client disconnect both stop it), its live status and counters, its pending tool-call
    /// approvals, and an event fan-out with a bounded replay ring buffer so subscribers (a Server-Sent Events
    /// response, WebSocket clients) can attach mid-run and observe the full ordered stream.
    ///
    /// <para>The fan-out carries <b>serialized canonical envelope frames</b> (the same shape
    /// <c>mux print --output-format jsonl</c> emits). A run driven in the same process feeds it typed events
    /// via <see cref="ApplyEvent"/> (which also updates live state); a run driven in another process feeds it
    /// pre-serialized frames via <see cref="ApplyEnvelope"/>. Subscribers cannot tell the two apart.</para>
    ///
    /// <para>Thread safety: all members are safe to call concurrently.</para>
    /// </summary>
    public sealed class RunHandle : IDisposable
    {
        #region Private-Members

        private readonly string _RunId;
        private readonly string _SessionId;
        private readonly CancellationTokenSource _LinkedCts;
        private readonly object _Sync = new object();
        private readonly List<string> _Ring = new List<string>();
        private readonly List<Channel<string>> _Subscribers = new List<Channel<string>>();
        private readonly ConcurrentDictionary<string, TaskCompletionSource<string>> _Approvals =
            new ConcurrentDictionary<string, TaskCompletionSource<string>>();

        private string _EndpointName = string.Empty;
        private string _Model = string.Empty;
        private RunStatusEnum _Status = RunStatusEnum.Running;
        private string? _CurrentToolName;
        private string? _LastError;
        private IReadOnlyList<AgentTask> _Tasks = new List<AgentTask>();
        private int _TotalTaskCount;
        private int _CompletedTaskCount;
        private int _IterationsCompleted;
        private int _ToolCallCount;
        private int _ErrorCount;
        private int _InputTokens;
        private int _OutputTokens;
        private int _TotalTokens;
        private int _FinalEstimatedTokens;
        private DateTime? _CompletedUtc;
        private int _MaxBufferedEvents = 512;
        private int _SubscriberQueueCapacity = 1024;
        private bool _Completed;
        private bool _Disposed;

        #endregion

        #region Public-Members

        /// <summary>The run correlation identifier.</summary>
        public string RunId => _RunId;

        /// <summary>The persisted session identifier this run belongs to (may be empty).</summary>
        public string SessionId => _SessionId;

        /// <summary>The endpoint name the run executes against.</summary>
        public string EndpointName
        {
            get => _EndpointName;
            set => _EndpointName = value ?? string.Empty;
        }

        /// <summary>The model the run executes against.</summary>
        public string Model
        {
            get => _Model;
            set => _Model = value ?? string.Empty;
        }

        /// <summary>The current lifecycle status.</summary>
        public RunStatusEnum Status
        {
            get { lock (_Sync) { return _Status; } }
            set { lock (_Sync) { _Status = value; } }
        }

        /// <summary>The tool currently executing, or null when none is in flight.</summary>
        public string? CurrentToolName
        {
            get { lock (_Sync) { return _CurrentToolName; } }
        }

        /// <summary>The most recent error message observed during the run, or null.</summary>
        public string? LastError
        {
            get { lock (_Sync) { return _LastError; } }
        }

        /// <summary>The latest task-plan snapshot for the run (empty when the run has no plan).</summary>
        public IReadOnlyList<AgentTask> Tasks
        {
            get { lock (_Sync) { return _Tasks; } }
        }

        /// <summary>Total tasks in the latest task-plan snapshot.</summary>
        public int TotalTaskCount { get { lock (_Sync) { return _TotalTaskCount; } } }

        /// <summary>Completed tasks in the latest task-plan snapshot.</summary>
        public int CompletedTaskCount { get { lock (_Sync) { return _CompletedTaskCount; } } }

        /// <summary>Iterations completed (set on run completion).</summary>
        public int IterationsCompleted { get { lock (_Sync) { return _IterationsCompleted; } } }

        /// <summary>Tool calls handled (set on run completion).</summary>
        public int ToolCallCount { get { lock (_Sync) { return _ToolCallCount; } } }

        /// <summary>Error events observed during the run.</summary>
        public int ErrorCount { get { lock (_Sync) { return _ErrorCount; } } }

        /// <summary>Provider-reported input tokens (set on run completion).</summary>
        public int InputTokens { get { lock (_Sync) { return _InputTokens; } } }

        /// <summary>Provider-reported output tokens (set on run completion).</summary>
        public int OutputTokens { get { lock (_Sync) { return _OutputTokens; } } }

        /// <summary>Provider-reported total tokens (set on run completion).</summary>
        public int TotalTokens { get { lock (_Sync) { return _TotalTokens; } } }

        /// <summary>Estimated final context tokens (set on run completion).</summary>
        public int FinalEstimatedTokens { get { lock (_Sync) { return _FinalEstimatedTokens; } } }

        /// <summary>When the run started (UTC).</summary>
        public DateTime StartedUtc { get; }

        /// <summary>When the run reached a terminal status (UTC), or null while running.</summary>
        public DateTime? CompletedUtc { get { lock (_Sync) { return _CompletedUtc; } } }

        /// <summary>Whether the run has reached a terminal status.</summary>
        public bool IsTerminal { get { lock (_Sync) { return _Completed; } } }

        /// <summary>
        /// Maximum number of frames retained for replay to late subscribers. Minimum 1. Default 512.
        /// </summary>
        public int MaxBufferedEvents
        {
            get => _MaxBufferedEvents;
            set => _MaxBufferedEvents = value < 1 ? 1 : value;
        }

        /// <summary>
        /// Bounded per-subscriber queue capacity. When a subscriber cannot keep up, the oldest queued frame is
        /// dropped rather than stalling the run. Minimum 1. Default 1024.
        /// </summary>
        public int SubscriberQueueCapacity
        {
            get => _SubscriberQueueCapacity;
            set => _SubscriberQueueCapacity = value < 1 ? 1 : value;
        }

        /// <summary>The run's cancellation token (fires on explicit cancel or client disconnect).</summary>
        public CancellationToken Token => _LinkedCts.Token;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate a run handle.
        /// </summary>
        /// <param name="runId">Run correlation id. Must not be null.</param>
        /// <param name="sessionId">Persisted session id, or empty.</param>
        /// <param name="endpointName">Endpoint the run targets.</param>
        /// <param name="model">Model the run targets.</param>
        /// <param name="externalToken">A token (typically the HTTP request token) linked into the run's own
        /// cancellation source so a client disconnect also cancels the run.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="runId"/> is null.</exception>
        public RunHandle(string runId, string sessionId, string endpointName, string model, CancellationToken externalToken)
        {
            _RunId = runId ?? throw new ArgumentNullException(nameof(runId));
            _SessionId = sessionId ?? string.Empty;
            _EndpointName = endpointName ?? string.Empty;
            _Model = model ?? string.Empty;
            _LinkedCts = CancellationTokenSource.CreateLinkedTokenSource(externalToken);
            StartedUtc = DateTime.UtcNow;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Requests cancellation of the run. Idempotent; a no-op once the run is terminal or disposed.
        /// </summary>
        /// <returns>True when cancellation was newly requested; false when the run was already terminal,
        /// already canceled, or disposed.</returns>
        public bool Cancel()
        {
            lock (_Sync)
            {
                if (_Disposed || _Completed || _LinkedCts.IsCancellationRequested)
                {
                    return false;
                }
            }

            try
            {
                _LinkedCts.Cancel();
                return true;
            }
            catch (ObjectDisposedException)
            {
                return false;
            }
        }

        /// <summary>
        /// Registers a pending approval for a tool call and returns the completion source the run awaits.
        /// </summary>
        /// <param name="toolCallId">The proposed tool call id. Must not be null or empty.</param>
        /// <returns>A completion source resolved by <see cref="ResolveApproval"/> with the decision.</returns>
        /// <exception cref="ArgumentException">Thrown when <paramref name="toolCallId"/> is null or empty.</exception>
        public TaskCompletionSource<string> RegisterApproval(string toolCallId)
        {
            if (string.IsNullOrEmpty(toolCallId)) throw new ArgumentException("Tool call id is required.", nameof(toolCallId));

            TaskCompletionSource<string> source = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            _Approvals[toolCallId] = source;
            Status = RunStatusEnum.AwaitingApproval;
            return source;
        }

        /// <summary>
        /// Resolves a pending tool-call approval with a decision ("y", "always", or "n").
        /// </summary>
        /// <param name="toolCallId">The proposed tool call id.</param>
        /// <param name="decision">The decision verdict.</param>
        /// <returns>True when a pending approval was found and resolved; otherwise false.</returns>
        public bool ResolveApproval(string toolCallId, string decision)
        {
            if (string.IsNullOrEmpty(toolCallId)) return false;

            if (_Approvals.TryRemove(toolCallId, out TaskCompletionSource<string>? source))
            {
                Status = RunStatusEnum.Running;
                return source.TrySetResult(decision ?? "n");
            }

            return false;
        }

        /// <summary>
        /// Atomically captures the current replay buffer and registers a live subscriber channel. Send the
        /// returned frames, then read the channel for subsequent frames.
        /// </summary>
        /// <returns>A subscription carrying the replay snapshot and the live reader.</returns>
        public RunSubscription Subscribe()
        {
            Channel<string> channel = System.Threading.Channels.Channel.CreateBounded<string>(
                new BoundedChannelOptions(_SubscriberQueueCapacity)
                {
                    FullMode = BoundedChannelFullMode.DropOldest,
                    SingleReader = true,
                    SingleWriter = false
                });

            lock (_Sync)
            {
                List<string> replay = new List<string>(_Ring);
                if (_Completed)
                {
                    channel.Writer.TryComplete();
                }
                else
                {
                    _Subscribers.Add(channel);
                }

                return new RunSubscription(replay, channel);
            }
        }

        /// <summary>
        /// Removes a subscriber and completes its channel. Safe to call more than once.
        /// </summary>
        /// <param name="subscription">The subscription to remove. Null is ignored.</param>
        public void Unsubscribe(RunSubscription? subscription)
        {
            if (subscription == null) return;

            lock (_Sync)
            {
                _Subscribers.Remove(subscription.Channel);
            }

            subscription.Channel.Writer.TryComplete();
        }

        /// <summary>
        /// Applies an in-process agent event to the run's live state (status, counters, current tool, task
        /// plan), then serializes it to a canonical envelope frame and fans it out to all subscribers.
        /// Terminal events (<see cref="RunCompletedEvent"/>) mark the run complete and close subscriber
        /// channels.
        /// </summary>
        /// <param name="agentEvent">The event to apply. Null is ignored.</param>
        public void ApplyEvent(AgentEvent? agentEvent)
        {
            if (agentEvent == null) return;

            bool terminal = false;
            lock (_Sync)
            {
                switch (agentEvent)
                {
                    case ToolCallProposedEvent proposed:
                        _CurrentToolName = proposed.ToolCall?.Name;
                        break;
                    case ToolCallCompletedEvent _:
                        _CurrentToolName = null;
                        break;
                    case ErrorEvent error:
                        _LastError = error.Message;
                        _ErrorCount++;
                        break;
                    case TaskPlanUpdatedEvent plan:
                        _Tasks = new List<AgentTask>(plan.Tasks);
                        _TotalTaskCount = plan.TotalCount;
                        _CompletedTaskCount = plan.CompletedCount;
                        break;
                    case RunCompletedEvent completed:
                        _IterationsCompleted = completed.IterationsCompleted;
                        _ToolCallCount = completed.ToolCallCount;
                        _ErrorCount = completed.ErrorCount;
                        _InputTokens = completed.InputTokens;
                        _OutputTokens = completed.OutputTokens;
                        _TotalTokens = completed.TotalTokens;
                        _FinalEstimatedTokens = completed.FinalEstimatedTokens;
                        _Status = MapStatus(completed.Status);
                        terminal = true;
                        break;
                }
            }

            FanOut(AgentEventSerializer.ToEnvelopeLine(agentEvent), terminal);
        }

        /// <summary>
        /// Applies a pre-serialized canonical envelope frame produced elsewhere (a run driven in another
        /// process and published to this hub). Updates minimal live state from the frame and fans it out to
        /// subscribers verbatim. A <c>run_completed</c> frame marks the run terminal.
        /// </summary>
        /// <param name="frame">A serialized canonical envelope (a single JSON object). Null/blank is ignored.</param>
        public void ApplyEnvelope(string? frame)
        {
            if (string.IsNullOrWhiteSpace(frame)) return;

            bool terminal = false;
            try
            {
                using JsonDocument doc = JsonDocument.Parse(frame!);
                JsonElement root = doc.RootElement;
                string eventType = root.TryGetProperty("eventType", out JsonElement t) ? (t.GetString() ?? string.Empty) : string.Empty;
                lock (_Sync)
                {
                    if (eventType == "error" && root.TryGetProperty("message", out JsonElement m))
                    {
                        _LastError = m.GetString();
                        _ErrorCount++;
                    }
                    else if (eventType == "run_completed")
                    {
                        if (root.TryGetProperty("status", out JsonElement s))
                        {
                            _Status = MapStatus(s.GetString() ?? string.Empty);
                        }
                        terminal = true;
                    }
                }
            }
            catch (JsonException)
            {
                // Relay a malformed frame verbatim; it just won't update typed state.
            }

            FanOut(frame!, terminal);
        }

        /// <summary>
        /// Marks the run terminal with the given status when no <see cref="RunCompletedEvent"/> was observed
        /// (for example a cancellation). Completes subscriber channels. Idempotent.
        /// </summary>
        /// <param name="status">The terminal status to record.</param>
        public void MarkTerminal(RunStatusEnum status)
        {
            List<Channel<string>> targets;

            lock (_Sync)
            {
                if (_Completed) return;
                _Status = status;
                _CompletedUtc = DateTime.UtcNow;
                _Completed = true;
                targets = new List<Channel<string>>(_Subscribers);
                _Subscribers.Clear();
            }

            foreach (Channel<string> channel in targets)
            {
                channel.Writer.TryComplete();
            }
        }

        /// <summary>
        /// Releases the run's cancellation source and completes any remaining subscriber channels.
        /// </summary>
        public void Dispose()
        {
            List<Channel<string>> targets;
            lock (_Sync)
            {
                if (_Disposed) return;
                _Disposed = true;
                targets = new List<Channel<string>>(_Subscribers);
                _Subscribers.Clear();
            }

            foreach (Channel<string> channel in targets)
            {
                channel.Writer.TryComplete();
            }

            try { _LinkedCts.Dispose(); } catch (ObjectDisposedException) { }
        }

        #endregion

        #region Private-Methods

        private void FanOut(string frame, bool terminal)
        {
            List<Channel<string>> targets;
            lock (_Sync)
            {
                _Ring.Add(frame);
                while (_Ring.Count > _MaxBufferedEvents)
                {
                    _Ring.RemoveAt(0);
                }

                if (terminal)
                {
                    _CompletedUtc = DateTime.UtcNow;
                    _Completed = true;
                }

                targets = new List<Channel<string>>(_Subscribers);
                if (terminal)
                {
                    _Subscribers.Clear();
                }
            }

            foreach (Channel<string> channel in targets)
            {
                channel.Writer.TryWrite(frame);
                if (terminal)
                {
                    channel.Writer.TryComplete();
                }
            }
        }

        private static RunStatusEnum MapStatus(string status)
        {
            string normalized = (status ?? string.Empty).ToLowerInvariant();
            if (normalized.Contains("cancel"))
            {
                return RunStatusEnum.Canceled;
            }

            if (normalized.StartsWith("completed", StringComparison.Ordinal) || normalized.Length == 0)
            {
                return RunStatusEnum.Completed;
            }

            return RunStatusEnum.Failed;
        }

        #endregion
    }
}
