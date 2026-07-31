namespace Mux.Core.Sessions
{
    using System.Collections.Generic;
    using Mux.Core.Models;
    using Mux.Core.Tasks;

    /// <summary>
    /// Serializable snapshot of a single job within a persisted session. State and approval policy are
    /// stored as plain strings for forward tolerance. The job's forked conversation history is captured
    /// as the durable per-job content; the ephemeral agent event stream is not persisted.
    /// </summary>
    public class PersistedJobSnapshot
    {
        #region Private-Members

        private string _Id = string.Empty;
        private string _Title = string.Empty;
        private string _Prompt = string.Empty;
        private string _State = "Queued";
        private string _ApprovalPolicy = "Ask";
        private List<string> _PendingFollowUps = new List<string>();
        private List<ConversationMessage> _ConversationHistory = new List<ConversationMessage>();
        private List<AgentTask> _TaskPlan = new List<AgentTask>();

        #endregion

        #region Public-Members

        /// <summary>
        /// The job identifier.
        /// </summary>
        public string Id
        {
            get => _Id;
            set => _Id = value ?? string.Empty;
        }

        /// <summary>
        /// The job display title.
        /// </summary>
        public string Title
        {
            get => _Title;
            set => _Title = value ?? string.Empty;
        }

        /// <summary>
        /// The initial prompt that created the job.
        /// </summary>
        public string Prompt
        {
            get => _Prompt;
            set => _Prompt = value ?? string.Empty;
        }

        /// <summary>
        /// The job lifecycle state name at the time of the snapshot (a <c>JobState</c> value name).
        /// </summary>
        public string State
        {
            get => _State;
            set => _State = string.IsNullOrEmpty(value) ? "Queued" : value;
        }

        /// <summary>
        /// The job's approval policy name (an <c>ApprovalPolicyEnum</c> value name).
        /// </summary>
        public string ApprovalPolicy
        {
            get => _ApprovalPolicy;
            set => _ApprovalPolicy = string.IsNullOrEmpty(value) ? "Ask" : value;
        }

        /// <summary>
        /// Any follow-up prompts still queued behind the job at snapshot time.
        /// </summary>
        public List<string> PendingFollowUps
        {
            get => _PendingFollowUps;
            set => _PendingFollowUps = value ?? new List<string>();
        }

        /// <summary>
        /// The job's forked conversation history.
        /// </summary>
        public List<ConversationMessage> ConversationHistory
        {
            get => _ConversationHistory;
            set => _ConversationHistory = value ?? new List<ConversationMessage>();
        }

        /// <summary>
        /// The job's task plan at snapshot time, in plan order. Empty when the job had no plan. Older
        /// session files without this field deserialize to an empty list (forward tolerance).
        /// </summary>
        public List<AgentTask> TaskPlan
        {
            get => _TaskPlan;
            set => _TaskPlan = value ?? new List<AgentTask>();
        }

        #endregion
    }
}
