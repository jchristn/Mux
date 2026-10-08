namespace Mux.Core.Models
{
    using System;
    using System.Text.Json.Serialization;

    /// <summary>
    /// One recorded trust decision for a project root, persisted in <c>~/.mux/trusted-projects.json</c>.
    /// </summary>
    public class ProjectTrustEntry
    {
        #region Private-Members

        private string _Root = string.Empty;
        private string _Level = "unknown";

        #endregion

        #region Public-Members

        /// <summary>
        /// The absolute path of the project root the decision applies to. Never null.
        /// </summary>
        [JsonPropertyName("root")]
        public string Root
        {
            get => _Root;
            set => _Root = value ?? string.Empty;
        }

        /// <summary>
        /// The trust level as its wire name: <c>all</c>, <c>playbooks</c>, <c>ignore</c>, or <c>unknown</c>.
        /// Unrecognized values read back as <c>unknown</c>. Never null.
        /// </summary>
        [JsonPropertyName("level")]
        public string Level
        {
            get => _Level;
            set => _Level = string.IsNullOrWhiteSpace(value) ? "unknown" : value.Trim().ToLowerInvariant();
        }

        /// <summary>
        /// When the decision was recorded, in UTC.
        /// </summary>
        [JsonPropertyName("decidedUtc")]
        public DateTime DecidedUtc { get; set; } = DateTime.UtcNow;

        #endregion
    }
}
