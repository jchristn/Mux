namespace Mux.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// The parsed frontmatter of a skill's <c>SKILL.md</c>: its identity, when-to-use guidance, mutation
    /// posture, tags, and the commands it declares. Populated by the skill loader from the YAML-style
    /// frontmatter block; empty or missing optional fields fall back to the documented defaults.
    /// </summary>
    public class SkillManifest
    {
        #region Private-Members

        private string _Name = string.Empty;
        private string _Title = string.Empty;
        private string _Description = string.Empty;
        private string _Version = "0.0.0";
        private bool _Enabled = true;
        private bool _Mutating = true;
        private string _WhenToUse = string.Empty;
        private List<string> _AllowedTools = new List<string>();
        private List<string> _Tags = new List<string>();
        private List<SkillCommand> _Commands = new List<SkillCommand>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="SkillManifest"/> class.
        /// </summary>
        public SkillManifest()
        {
        }

        #endregion

        #region Public-Members

        /// <summary>
        /// The stable skill identifier surfaced to the model. Must match the skill directory name.
        /// </summary>
        public string Name
        {
            get => _Name;
            set => _Name = value ?? string.Empty;
        }

        /// <summary>
        /// A human-readable title for menus and listings. Falls back to <see cref="Name"/> when empty.
        /// </summary>
        public string Title
        {
            get => string.IsNullOrWhiteSpace(_Title) ? _Name : _Title;
            set => _Title = value ?? string.Empty;
        }

        /// <summary>
        /// A one- or two-sentence description; the only body text the model sees before it opens the skill.
        /// </summary>
        public string Description
        {
            get => _Description;
            set => _Description = value ?? string.Empty;
        }

        /// <summary>
        /// The skill's semantic version, used for provenance and pinning. Defaults to <c>0.0.0</c>.
        /// </summary>
        public string Version
        {
            get => _Version;
            set => _Version = string.IsNullOrWhiteSpace(value) ? "0.0.0" : value.Trim();
        }

        /// <summary>
        /// The author's default enablement. The runtime override in <c>skills.json</c> takes precedence.
        /// Defaults to <c>true</c>.
        /// </summary>
        public bool Enabled
        {
            get => _Enabled;
            set => _Enabled = value;
        }

        /// <summary>
        /// Whether the skill's commands mutate the workspace. When <c>true</c> (the default), commands
        /// serialize through the write lease and pass the approval policy; when <c>false</c>, they run as
        /// read-only work without the lease.
        /// </summary>
        public bool Mutating
        {
            get => _Mutating;
            set => _Mutating = value;
        }

        /// <summary>
        /// Guidance the model uses to judge when the skill is relevant. Empty when unspecified.
        /// </summary>
        public string WhenToUse
        {
            get => _WhenToUse;
            set => _WhenToUse = value ?? string.Empty;
        }

        /// <summary>
        /// The advisory list of tools the skill expects to use. Recorded but not enforced. Never null.
        /// </summary>
        public List<string> AllowedTools
        {
            get => _AllowedTools;
            set => _AllowedTools = value ?? new List<string>();
        }

        /// <summary>
        /// The tags used for grouping, filtering, and search in the inventory view. Never null.
        /// </summary>
        public List<string> Tags
        {
            get => _Tags;
            set => _Tags = value ?? new List<string>();
        }

        /// <summary>
        /// The commands the skill declares. Never null; may be empty.
        /// </summary>
        public List<SkillCommand> Commands
        {
            get => _Commands;
            set => _Commands = value ?? new List<SkillCommand>();
        }

        #endregion
    }
}
