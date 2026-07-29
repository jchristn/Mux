namespace Mux.Core.Sessions
{
    using System;
    using System.Collections.Generic;
    using Mux.Core.Models;

    /// <summary>
    /// A versioned, serializable snapshot of a mux session: its identity, title, active endpoint/model,
    /// focused conversation history, persisted prompt history, and per-job snapshots. Deserialization is
    /// forward-tolerant — unknown JSON fields are ignored — so newer snapshots load in older builds.
    /// </summary>
    public class SessionSnapshot
    {
        #region Private-Members

        private int _SchemaVersion = CurrentSchemaVersion;
        private string _Id = string.Empty;
        private string _Title = string.Empty;
        private string _EndpointName = string.Empty;
        private string _Model = string.Empty;
        private List<ConversationMessage> _ConversationHistory = new List<ConversationMessage>();
        private List<string> _PromptHistory = new List<string>();
        private List<PersistedJobSnapshot> _Jobs = new List<PersistedJobSnapshot>();

        #endregion

        #region Public-Members

        /// <summary>
        /// The current snapshot schema version written by this build.
        /// </summary>
        public const int CurrentSchemaVersion = 1;

        /// <summary>
        /// The schema version of this snapshot. Defaults to <see cref="CurrentSchemaVersion"/>.
        /// </summary>
        public int SchemaVersion
        {
            get => _SchemaVersion;
            set => _SchemaVersion = value;
        }

        /// <summary>
        /// The stable session identifier (also the on-disk file stem).
        /// </summary>
        public string Id
        {
            get => _Id;
            set => _Id = value ?? string.Empty;
        }

        /// <summary>
        /// The session title.
        /// </summary>
        public string Title
        {
            get => _Title;
            set => _Title = value ?? string.Empty;
        }

        /// <summary>
        /// Whether the title was pinned by the user (and should not be auto-refreshed).
        /// </summary>
        public bool TitlePinned { get; set; }

        /// <summary>
        /// When the session was created (UTC).
        /// </summary>
        public DateTime CreatedUtc { get; set; }

        /// <summary>
        /// When the session was last updated (UTC).
        /// </summary>
        public DateTime UpdatedUtc { get; set; }

        /// <summary>
        /// The active endpoint name at snapshot time.
        /// </summary>
        public string EndpointName
        {
            get => _EndpointName;
            set => _EndpointName = value ?? string.Empty;
        }

        /// <summary>
        /// The active model at snapshot time.
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
        /// The focused conversation history.
        /// </summary>
        public List<ConversationMessage> ConversationHistory
        {
            get => _ConversationHistory;
            set => _ConversationHistory = value ?? new List<ConversationMessage>();
        }

        /// <summary>
        /// The persisted prompt history (most-recent last), used for up/down recall on resume.
        /// </summary>
        public List<string> PromptHistory
        {
            get => _PromptHistory;
            set => _PromptHistory = value ?? new List<string>();
        }

        /// <summary>
        /// Snapshots of the session's jobs (queued, active, or completed) at snapshot time.
        /// </summary>
        public List<PersistedJobSnapshot> Jobs
        {
            get => _Jobs;
            set => _Jobs = value ?? new List<PersistedJobSnapshot>();
        }

        #endregion
    }
}
