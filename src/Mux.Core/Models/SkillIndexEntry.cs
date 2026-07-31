namespace Mux.Core.Models
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// One row of the skills index (<c>~/.mux/skills.json</c>): the runtime enablement and optional pinned
    /// version for a skill, kept separate from the author's <c>SKILL.md</c> so toggling a skill from a menu
    /// never rewrites a hand-edited file.
    /// </summary>
    public class SkillIndexEntry
    {
        #region Private-Members

        private string _Id = string.Empty;
        private bool _Enabled = true;
        private string? _PinnedVersion = null;

        #endregion

        #region Public-Members

        /// <summary>
        /// The skill identifier this row applies to.
        /// </summary>
        [JsonPropertyName("id")]
        public string Id
        {
            get => _Id;
            set => _Id = value ?? string.Empty;
        }

        /// <summary>
        /// Whether the skill is enabled for the session. Defaults to true.
        /// </summary>
        [JsonPropertyName("enabled")]
        public bool Enabled
        {
            get => _Enabled;
            set => _Enabled = value;
        }

        /// <summary>
        /// An optional pinned skill version, or null to use whatever is on disk.
        /// </summary>
        [JsonPropertyName("pinnedVersion")]
        public string? PinnedVersion
        {
            get => _PinnedVersion;
            set => _PinnedVersion = value;
        }

        #endregion
    }
}
