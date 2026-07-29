namespace Mux.Core.Jobs
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Channels;
    using Mux.Core.Agent;
    using Mux.Core.Enums;
    using Mux.Core.Models;

    /// <summary>
    /// Represents one agent job, including its isolated input history, event transcript, and
    /// cancellation state. Public snapshot members are copied under a lock; state transitions are
    /// guarded internally by <see cref="JobManager"/>.
    /// </summary>
    public class Job
    {
        #region Private-Members

        private readonly object _SyncRoot = new object();
        private readonly Channel<AgentEvent> _EventChannel = Channel.CreateUnbounded<AgentEvent>();
        private readonly CancellationTokenSource _CancellationTokenSource = new CancellationTokenSource();
        private readonly List<ConversationMessage> _ConversationHistory;
        private readonly Queue<string> _PendingFollowUps = new Queue<string>();
        private readonly List<AgentEvent> _Transcript = new List<AgentEvent>();
        private readonly string _Id;
        private readonly string _SessionId;
        private readonly string _Prompt;
        private string _Title;
        private JobState _State = JobState.Queued;
        private ApprovalPolicyEnum _ApprovalPolicy = ApprovalPolicyEnum.Ask;
        private DateTime _CreatedUtc = DateTime.UtcNow;
        private DateTime? _StartedUtc = null;
        private DateTime? _CompletedUtc = null;
        private ContextStatusEvent? _LastContextStatus = null;
        private int _RunCount = 0;
        private int _ToolCallCount = 0;
        private int _ErrorCount = 0;
        private int _AssistantTextChars = 0;
        private long _DurationMs = 0;
        private bool _InitialPromptDequeued = false;
        private string _FailureMessage = string.Empty;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="Job"/> class.
        /// </summary>
        /// <param name="id">The stable job identifier.</param>
        /// <param name="sessionId">The owning session identifier.</param>
        /// <param name="title">The display title.</param>
        /// <param name="prompt">The initial prompt.</param>
        /// <param name="approvalPolicy">The approval policy for this job.</param>
        /// <param name="conversationHistory">The forked conversation history for this job.</param>
        /// <exception cref="ArgumentNullException">Thrown when a required string is null.</exception>
        public Job(
            string id,
            string sessionId,
            string title,
            string prompt,
            ApprovalPolicyEnum approvalPolicy,
            IEnumerable<ConversationMessage>? conversationHistory)
        {
            _Id = id ?? throw new ArgumentNullException(nameof(id));
            _SessionId = sessionId ?? throw new ArgumentNullException(nameof(sessionId));
            _Title = title ?? string.Empty;
            _Prompt = prompt ?? throw new ArgumentNullException(nameof(prompt));
            _ApprovalPolicy = approvalPolicy;
            _ConversationHistory = CopyConversation(conversationHistory);
        }

        #endregion

        #region Public-Members

        /// <summary>
        /// The stable job identifier.
        /// </summary>
        public string Id
        {
            get => _Id;
        }

        /// <summary>
        /// The owning session identifier.
        /// </summary>
        public string SessionId
        {
            get => _SessionId;
        }

        /// <summary>
        /// The initial prompt that created the job.
        /// </summary>
        public string Prompt
        {
            get => _Prompt;
        }

        /// <summary>
        /// The display title for this job.
        /// </summary>
        public string Title
        {
            get
            {
                lock (_SyncRoot)
                {
                    return _Title;
                }
            }
            set
            {
                lock (_SyncRoot)
                {
                    _Title = value ?? string.Empty;
                }
            }
        }

        /// <summary>
        /// The current lifecycle state.
        /// </summary>
        public JobState State
        {
            get
            {
                lock (_SyncRoot)
                {
                    return _State;
                }
            }
        }

        /// <summary>
        /// The approval policy scoped to this job.
        /// </summary>
        public ApprovalPolicyEnum ApprovalPolicy
        {
            get
            {
                lock (_SyncRoot)
                {
                    return _ApprovalPolicy;
                }
            }
            set
            {
                lock (_SyncRoot)
                {
                    _ApprovalPolicy = value;
                }
            }
        }

        /// <summary>
        /// The cancellation token observed by the active run.
        /// </summary>
        public CancellationToken CancellationToken
        {
            get => _CancellationTokenSource.Token;
        }

        /// <summary>
        /// The UTC timestamp when this job was created.
        /// </summary>
        public DateTime CreatedUtc
        {
            get
            {
                lock (_SyncRoot)
                {
                    return _CreatedUtc;
                }
            }
        }

        /// <summary>
        /// The UTC timestamp when this job first started.
        /// </summary>
        public DateTime? StartedUtc
        {
            get
            {
                lock (_SyncRoot)
                {
                    return _StartedUtc;
                }
            }
        }

        /// <summary>
        /// The UTC timestamp when this job reached a terminal state.
        /// </summary>
        public DateTime? CompletedUtc
        {
            get
            {
                lock (_SyncRoot)
                {
                    return _CompletedUtc;
                }
            }
        }

        /// <summary>
        /// The most recent context-status event emitted by this job, if any.
        /// </summary>
        public ContextStatusEvent? LastContextStatus
        {
            get
            {
                lock (_SyncRoot)
                {
                    return _LastContextStatus;
                }
            }
        }

        /// <summary>
        /// The number of agent turns run by this job.
        /// </summary>
        public int RunCount
        {
            get
            {
                lock (_SyncRoot)
                {
                    return _RunCount;
                }
            }
        }

        /// <summary>
        /// The total number of tool calls reported by completed runs.
        /// </summary>
        public int ToolCallCount
        {
            get
            {
                lock (_SyncRoot)
                {
                    return _ToolCallCount;
                }
            }
        }

        /// <summary>
        /// The total number of errors reported by completed runs.
        /// </summary>
        public int ErrorCount
        {
            get
            {
                lock (_SyncRoot)
                {
                    return _ErrorCount;
                }
            }
        }

        /// <summary>
        /// The total assistant text character count reported by completed runs.
        /// </summary>
        public int AssistantTextChars
        {
            get
            {
                lock (_SyncRoot)
                {
                    return _AssistantTextChars;
                }
            }
        }

        /// <summary>
        /// The cumulative duration reported by completed runs.
        /// </summary>
        public long DurationMs
        {
            get
            {
                lock (_SyncRoot)
                {
                    return _DurationMs;
                }
            }
        }

        /// <summary>
        /// The last failure message recorded for this job.
        /// </summary>
        public string FailureMessage
        {
            get
            {
                lock (_SyncRoot)
                {
                    return _FailureMessage;
                }
            }
        }

        /// <summary>
        /// A copied snapshot of the conversation history forked for this job.
        /// </summary>
        public List<ConversationMessage> ConversationHistory
        {
            get
            {
                lock (_SyncRoot)
                {
                    return CopyConversation(_ConversationHistory);
                }
            }
        }

        /// <summary>
        /// A copied snapshot of the events emitted by this job.
        /// </summary>
        public List<AgentEvent> Transcript
        {
            get
            {
                lock (_SyncRoot)
                {
                    return new List<AgentEvent>(_Transcript);
                }
            }
        }

        /// <summary>
        /// The number of queued follow-up prompts waiting behind the active turn.
        /// </summary>
        public int PendingFollowUpCount
        {
            get
            {
                lock (_SyncRoot)
                {
                    return _PendingFollowUps.Count;
                }
            }
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Reads agent events emitted by this job until the job reaches a terminal state.
        /// </summary>
        /// <param name="cancellationToken">A token to cancel the read.</param>
        /// <returns>An async sequence of agent events.</returns>
        public IAsyncEnumerable<AgentEvent> ReadEventsAsync(CancellationToken cancellationToken)
        {
            return _EventChannel.Reader.ReadAllAsync(cancellationToken);
        }

        #endregion

        #region Internal-Members

        internal ChannelWriter<AgentEvent> EventWriter
        {
            get => _EventChannel.Writer;
        }

        internal CancellationTokenSource CancellationTokenSource
        {
            get => _CancellationTokenSource;
        }

        #endregion

        #region Internal-Methods

        internal JobState SetState(JobState state)
        {
            lock (_SyncRoot)
            {
                JobState previous = _State;
                _State = state;

                if (state == JobState.Running && !_StartedUtc.HasValue)
                {
                    _StartedUtc = DateTime.UtcNow;
                }

                if (IsTerminalState(state) && !_CompletedUtc.HasValue)
                {
                    _CompletedUtc = DateTime.UtcNow;
                }

                return previous;
            }
        }

        internal bool IsTerminal
        {
            get
            {
                lock (_SyncRoot)
                {
                    return IsTerminalState(_State);
                }
            }
        }

        internal bool TrySetAwaitingWriteLease()
        {
            lock (_SyncRoot)
            {
                if (_State == JobState.Running)
                {
                    _State = JobState.AwaitingWriteLease;
                    return true;
                }

                return false;
            }
        }

        internal bool TryResumeRunningFromLeaseWait()
        {
            lock (_SyncRoot)
            {
                if (_State == JobState.AwaitingWriteLease)
                {
                    _State = JobState.Running;
                    return true;
                }

                return false;
            }
        }

        internal void AddFollowUp(string prompt)
        {
            if (string.IsNullOrWhiteSpace(prompt))
                throw new ArgumentException("Follow-up prompt cannot be null or empty.", nameof(prompt));

            lock (_SyncRoot)
            {
                if (IsTerminalState(_State))
                    throw new InvalidOperationException("Cannot add a follow-up to a terminal job.");

                _PendingFollowUps.Enqueue(prompt);
            }
        }

        internal string? DequeuePromptForRun()
        {
            lock (_SyncRoot)
            {
                if (!_InitialPromptDequeued)
                {
                    _InitialPromptDequeued = true;
                    _RunCount++;
                    return _Prompt;
                }

                if (_PendingFollowUps.Count == 0)
                {
                    return null;
                }

                _RunCount++;
                return _PendingFollowUps.Dequeue();
            }
        }

        internal void RecordEvent(AgentEvent agentEvent)
        {
            if (agentEvent is null) throw new ArgumentNullException(nameof(agentEvent));

            lock (_SyncRoot)
            {
                _Transcript.Add(agentEvent);

                if (agentEvent is ContextStatusEvent contextStatusEvent)
                {
                    _LastContextStatus = contextStatusEvent;
                }
                else if (agentEvent is RunCompletedEvent completedEvent)
                {
                    _ToolCallCount += completedEvent.ToolCallCount;
                    _ErrorCount += completedEvent.ErrorCount;
                    _AssistantTextChars += completedEvent.AssistantTextChars;
                    _DurationMs += completedEvent.DurationMs;
                }
                else if (agentEvent is ErrorEvent)
                {
                    _ErrorCount++;
                }
            }
        }

        internal void RecordFailure(Exception exception)
        {
            if (exception is null) throw new ArgumentNullException(nameof(exception));

            lock (_SyncRoot)
            {
                _FailureMessage = exception.Message;
            }
        }

        internal static bool IsTerminalState(JobState state)
        {
            return state == JobState.Completed
                || state == JobState.Failed
                || state == JobState.Cancelled;
        }

        #endregion

        #region Private-Methods

        private static List<ConversationMessage> CopyConversation(IEnumerable<ConversationMessage>? messages)
        {
            List<ConversationMessage> copy = new List<ConversationMessage>();

            if (messages is null)
            {
                return copy;
            }

            foreach (ConversationMessage message in messages)
            {
                List<ToolCall>? toolCalls = null;
                if (message.ToolCalls != null)
                {
                    toolCalls = new List<ToolCall>();
                    foreach (ToolCall toolCall in message.ToolCalls)
                    {
                        toolCalls.Add(new ToolCall
                        {
                            Id = toolCall.Id,
                            Name = toolCall.Name,
                            Arguments = toolCall.Arguments
                        });
                    }
                }

                copy.Add(new ConversationMessage
                {
                    Role = message.Role,
                    Content = message.Content,
                    ToolCallId = message.ToolCallId,
                    ToolCalls = toolCalls
                });
            }

            return copy;
        }

        #endregion
    }
}
