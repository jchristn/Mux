namespace Mux.Core.Models
{
    using System.Collections.Generic;

    /// <summary>
    /// A detached, UI-facing snapshot of one skill's state: its identity, whether it is enabled and valid,
    /// how many commands it declares, its tags, and — when invalid — a short error summary.
    /// </summary>
    public class SkillStatus
    {
        #region Private-Members

        private string _Name = string.Empty;
        private string _Title = string.Empty;
        private bool _Enabled = true;
        private bool _Valid = true;
        private int _CommandCount = 0;
        private List<string> _Tags = new List<string>();
        private string? _Error = null;
        private string _Scope = "user";
        private string _ArgumentHint = string.Empty;
        private List<string> _Warnings = new List<string>();

        #endregion

        #region Public-Members

        /// <summary>
        /// The skill identifier.
        /// </summary>
        public string Name
        {
            get => _Name;
            set => _Name = value ?? string.Empty;
        }

        /// <summary>
        /// The skill's human-readable title.
        /// </summary>
        public string Title
        {
            get => _Title;
            set => _Title = value ?? string.Empty;
        }

        /// <summary>
        /// Whether the skill is enabled for the current session.
        /// </summary>
        public bool Enabled
        {
            get => _Enabled;
            set => _Enabled = value;
        }

        /// <summary>
        /// Whether the skill passed validation.
        /// </summary>
        public bool Valid
        {
            get => _Valid;
            set => _Valid = value;
        }

        /// <summary>
        /// The number of commands the skill declares.
        /// </summary>
        public int CommandCount
        {
            get => _CommandCount;
            set => _CommandCount = value;
        }

        /// <summary>
        /// The skill's tags. Never null.
        /// </summary>
        public List<string> Tags
        {
            get => _Tags;
            set => _Tags = value ?? new List<string>();
        }

        /// <summary>
        /// A short error summary when the skill is invalid; otherwise null.
        /// </summary>
        public string? Error
        {
            get => _Error;
            set => _Error = value;
        }

        /// <summary>
        /// Where the skill came from: <c>user</c> or <c>project</c>. Never null.
        /// </summary>
        public string Scope
        {
            get => _Scope;
            set => _Scope = string.IsNullOrWhiteSpace(value) ? "user" : value;
        }

        /// <summary>
        /// Whether this project skill's commands are blocked until the project is trusted.
        /// </summary>
        public bool CommandsBlocked { get; set; }

        /// <summary>
        /// Whether this project skill hides a user skill with the same id.
        /// </summary>
        public bool ShadowsUserSkill { get; set; }

        /// <summary>
        /// Whether the skill can be invoked by name as a slash command. Defaults to true.
        /// </summary>
        public bool UserInvocable { get; set; } = true;

        /// <summary>
        /// The argument hint shown next to the slash command. Never null.
        /// </summary>
        public string ArgumentHint
        {
            get => _ArgumentHint;
            set => _ArgumentHint = value ?? string.Empty;
        }

        /// <summary>
        /// Non-fatal validation warnings, such as ignored frontmatter fields. Never null.
        /// </summary>
        public List<string> Warnings
        {
            get => _Warnings;
            set => _Warnings = value ?? new List<string>();
        }

        /// <summary>
        /// The skill's effective category (override, then <c>SKILL.md</c>, then inferred). Never null.
        /// </summary>
        public string Category { get; set; } = "general";

        /// <summary>
        /// Whether <see cref="Category"/> comes from a per-user override in <c>skills.json</c>.
        /// </summary>
        public bool CategoryOverridden { get; set; }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Returns a detached copy of this status, including copies of its lists.
        /// </summary>
        /// <returns>The copy.</returns>
        public SkillStatus Clone()
        {
            return new SkillStatus
            {
                Name = _Name,
                Title = _Title,
                Enabled = _Enabled,
                Valid = _Valid,
                CommandCount = _CommandCount,
                Tags = new List<string>(_Tags),
                Error = _Error,
                Scope = _Scope,
                CommandsBlocked = CommandsBlocked,
                ShadowsUserSkill = ShadowsUserSkill,
                UserInvocable = UserInvocable,
                ArgumentHint = _ArgumentHint,
                Warnings = new List<string>(_Warnings),
                Category = Category,
                CategoryOverridden = CategoryOverridden
            };
        }

        #endregion
    }
}
