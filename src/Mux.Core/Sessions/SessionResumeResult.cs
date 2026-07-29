namespace Mux.Core.Sessions
{
    using System.Collections.Generic;
    using Mux.Core.Models;

    /// <summary>
    /// The rehydrated state produced from a <see cref="SessionSnapshot"/> when resuming a session:
    /// the focused conversation and prompt history, session metadata, and the jobs partitioned into
    /// those that had finished versus those that were still active at save time and therefore require
    /// an explicit re-run.
    /// </summary>
    public class SessionResumeResult
    {
        #region Private-Members

        private string _Id = string.Empty;
        private string _Title = string.Empty;
        private string _EndpointName = string.Empty;
        private string _Model = string.Empty;
        private List<ConversationMessage> _ConversationHistory = new List<ConversationMessage>();
        private List<string> _PromptHistory = new List<string>();
        private List<PersistedJobSnapshot> _InterruptedJobs = new List<PersistedJobSnapshot>();
        private List<PersistedJobSnapshot> _CompletedJobs = new List<PersistedJobSnapshot>();

        #endregion

        #region Public-Members

        /// <summary>
        /// The resumed session id.
        /// </summary>
        public string Id
        {
            get => _Id;
            set => _Id = value ?? string.Empty;
        }

        /// <summary>
        /// The resumed session title.
        /// </summary>
        public string Title
        {
            get => _Title;
            set => _Title = value ?? string.Empty;
        }

        /// <summary>
        /// Whether the title was pinned by the user.
        /// </summary>
        public bool TitlePinned { get; set; }

        /// <summary>
        /// The active endpoint name.
        /// </summary>
        public string EndpointName
        {
            get => _EndpointName;
            set => _EndpointName = value ?? string.Empty;
        }

        /// <summary>
        /// The active model.
        /// </summary>
        public string Model
        {
            get => _Model;
            set => _Model = value ?? string.Empty;
        }

        /// <summary>
        /// The number of context compactions performed in the session.
        /// </summary>
        public int CompactionCount { get; set; }

        /// <summary>
        /// The focused conversation history to resume with.
        /// </summary>
        public List<ConversationMessage> ConversationHistory
        {
            get => _ConversationHistory;
            set => _ConversationHistory = value ?? new List<ConversationMessage>();
        }

        /// <summary>
        /// The persisted prompt history to resume with.
        /// </summary>
        public List<string> PromptHistory
        {
            get => _PromptHistory;
            set => _PromptHistory = value ?? new List<string>();
        }

        /// <summary>
        /// Jobs that were still active (non-terminal) at save time. These are not auto-restarted; the
        /// user must explicitly re-run them (required for mutating work; optional for read-only).
        /// </summary>
        public List<PersistedJobSnapshot> InterruptedJobs
        {
            get => _InterruptedJobs;
            set => _InterruptedJobs = value ?? new List<PersistedJobSnapshot>();
        }

        /// <summary>
        /// Jobs that had reached a terminal state (completed, failed, or cancelled) at save time.
        /// </summary>
        public List<PersistedJobSnapshot> CompletedJobs
        {
            get => _CompletedJobs;
            set => _CompletedJobs = value ?? new List<PersistedJobSnapshot>();
        }

        #endregion
    }
}
