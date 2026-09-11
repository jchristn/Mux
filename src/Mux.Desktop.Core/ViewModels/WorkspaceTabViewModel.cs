namespace Mux.Desktop.ViewModels
{
    using System;
    using CommunityToolkit.Mvvm.ComponentModel;
    using Mux.Core.Agent;

    /// <summary>
    /// View model for one tab in the tabbed workspace: a single conversation running as a parallel workload.
    /// Tracks the tab's title, focus, and — critically — its attention status, so a background workload that
    /// produces text, finishes, errors, or needs approval surfaces an indicator the user can act on. The
    /// attention model is pure and testable: a front end feeds it the conversation's <see cref="AgentEvent"/>
    /// stream and busy/focus transitions; it computes the tab's <see cref="Status"/>.
    /// </summary>
    /// <remarks>
    /// Threading: this type makes no thread guarantees. A front end must marshal <see cref="NotifyEvent"/>,
    /// <see cref="NotifyBusy"/>, and <see cref="NotifyApprovalPending"/> onto its UI dispatcher, since the
    /// underlying conversation raises events with no thread affinity.
    /// </remarks>
    public sealed class WorkspaceTabViewModel : ObservableObject
    {
        private readonly string _Id;
        private string _Title;
        private bool _IsActive;
        private bool _IsRunning;
        private bool _HasUnread;
        private bool _HasError;
        private bool _NeedsApproval;
        private int _UnreadCount;
        private TabStatus _Status = TabStatus.Idle;

        /// <summary>
        /// Instantiate a tab view model for a thread.
        /// </summary>
        /// <param name="id">The thread/session id this tab represents. Required.</param>
        /// <param name="title">The tab title. Required.</param>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
        public WorkspaceTabViewModel(string id, string title)
        {
            ArgumentNullException.ThrowIfNull(id);
            ArgumentNullException.ThrowIfNull(title);

            _Id = id;
            _Title = title;
        }

        /// <summary>The thread/session id this tab represents.</summary>
        public string Id
        {
            get => _Id;
        }

        /// <summary>The tab title.</summary>
        public string Title
        {
            get => _Title;
            set => SetProperty(ref _Title, value ?? string.Empty);
        }

        /// <summary>Whether this tab is the focused (active) tab.</summary>
        public bool IsActive
        {
            get => _IsActive;
            private set => SetProperty(ref _IsActive, value);
        }

        /// <summary>Whether a turn is in flight in this tab.</summary>
        public bool IsRunning
        {
            get => _IsRunning;
            private set => SetProperty(ref _IsRunning, value);
        }

        /// <summary>Whether unseen events/text arrived while the tab was not focused.</summary>
        public bool HasUnread
        {
            get => _HasUnread;
            private set => SetProperty(ref _HasUnread, value);
        }

        /// <summary>Whether the last turn ended in an error and the tab has not been focused since.</summary>
        public bool HasError
        {
            get => _HasError;
            private set => SetProperty(ref _HasError, value);
        }

        /// <summary>Whether a background turn is blocked awaiting tool approval.</summary>
        public bool NeedsApproval
        {
            get => _NeedsApproval;
            private set => SetProperty(ref _NeedsApproval, value);
        }

        /// <summary>The number of unseen events since the tab was last focused.</summary>
        public int UnreadCount
        {
            get => _UnreadCount;
            private set => SetProperty(ref _UnreadCount, value);
        }

        /// <summary>The tab's current attention/activity status (highest-priority condition).</summary>
        public TabStatus Status
        {
            get => _Status;
            private set => SetProperty(ref _Status, value);
        }

        /// <summary>
        /// Feed one agent event from this tab's conversation. When the tab is not focused, content events
        /// (assistant text, tool completion, task-plan changes, run completion) mark it unread and errors mark
        /// it errored. Events for the focused tab are seen live and do not raise attention.
        /// </summary>
        /// <param name="agentEvent">The event to observe. Required.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="agentEvent"/> is null.</exception>
        public void NotifyEvent(AgentEvent agentEvent)
        {
            ArgumentNullException.ThrowIfNull(agentEvent);

            if (_IsActive)
            {
                return;
            }

            switch (agentEvent)
            {
                case ErrorEvent:
                    HasError = true;
                    break;
                case AssistantTextEvent:
                case ToolCallCompletedEvent:
                case TaskPlanUpdatedEvent:
                case RunCompletedEvent:
                    HasUnread = true;
                    UnreadCount = _UnreadCount + 1;
                    break;
                default:
                    // Thinking, heartbeats, run-started, proposals, and approvals are not attention-worthy.
                    break;
            }

            Recompute();
        }

        /// <summary>
        /// Update whether a turn is in flight in this tab.
        /// </summary>
        /// <param name="running">True when a turn is running.</param>
        public void NotifyBusy(bool running)
        {
            IsRunning = running;
            Recompute();
        }

        /// <summary>
        /// Signal that a background turn is blocked awaiting tool approval. Ignored while the tab is focused,
        /// where approval is handled by the live approval dialog.
        /// </summary>
        public void NotifyApprovalPending()
        {
            if (_IsActive)
            {
                return;
            }

            NeedsApproval = true;
            Recompute();
        }

        /// <summary>
        /// Mark the tab focused, clearing all attention (unread, error, approval) and reflecting only live
        /// state (running or idle).
        /// </summary>
        public void Activate()
        {
            IsActive = true;
            HasUnread = false;
            HasError = false;
            NeedsApproval = false;
            UnreadCount = 0;
            Recompute();
        }

        /// <summary>
        /// Mark the tab unfocused. Live state is retained; subsequent events raise attention.
        /// </summary>
        public void Deactivate()
        {
            IsActive = false;
            Recompute();
        }

        private void Recompute()
        {
            if (_NeedsApproval)
            {
                Status = TabStatus.NeedsApproval;
            }
            else if (_HasError)
            {
                Status = TabStatus.Error;
            }
            else if (_HasUnread)
            {
                Status = TabStatus.Unread;
            }
            else if (_IsRunning)
            {
                Status = TabStatus.Running;
            }
            else
            {
                Status = TabStatus.Idle;
            }
        }
    }
}
