namespace Mux.Core.Skills
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// The declarative definition of one seeded default skill: its manifest fields, an optional prose body,
    /// and its commands. A skill with no commands is a playbook (the model reads the body through the
    /// <c>skill</c> tool and follows it with its normal tools); a skill with commands and a body is a hybrid.
    /// <see cref="DefaultSkillBuilder.Build(DefaultSkillDef)"/> turns a definition into <c>SKILL.md</c> text.
    /// Instances are not thread-safe; build them once and treat them as read-only afterwards.
    /// </summary>
    public sealed class DefaultSkillDef
    {
        #region Private-Members

        private string _Id = string.Empty;
        private string _Title = string.Empty;
        private string _Description = string.Empty;
        private bool _Mutating = false;
        private List<string> _Tags = new List<string>();
        private string _WhenToUse = string.Empty;
        private List<string> _AppliesTo = new List<string>();
        private List<string> _RequiresTools = new List<string>();
        private string _ArgumentHint = string.Empty;
        private string _Body = string.Empty;
        private List<DefaultSkillCommandDef> _Commands = new List<DefaultSkillCommandDef>();
        private Dictionary<string, string> _Resources = new Dictionary<string, string>(StringComparer.Ordinal);

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="DefaultSkillDef"/> class.
        /// </summary>
        public DefaultSkillDef()
        {
        }

        #endregion

        #region Public-Members

        /// <summary>
        /// The skill id, which is also its folder name. Lowercase and hyphen-separated. Never null.
        /// </summary>
        public string Id
        {
            get => _Id;
            set => _Id = value ?? string.Empty;
        }

        /// <summary>
        /// The human title shown in listings. Never null.
        /// </summary>
        public string Title
        {
            get => _Title;
            set => _Title = value ?? string.Empty;
        }

        /// <summary>
        /// The one- or two-sentence description the model sees before it opens the skill. Never null.
        /// </summary>
        public string Description
        {
            get => _Description;
            set => _Description = value ?? string.Empty;
        }

        /// <summary>
        /// Whether the skill's commands change the workspace. Defaults to <c>false</c>.
        /// </summary>
        public bool Mutating
        {
            get => _Mutating;
            set => _Mutating = value;
        }

        /// <summary>
        /// The tags used for grouping and search. Never null.
        /// </summary>
        public List<string> Tags
        {
            get => _Tags;
            set => _Tags = value ?? new List<string>();
        }

        /// <summary>
        /// Guidance the model uses to decide when the skill is relevant. Never null.
        /// </summary>
        public string WhenToUse
        {
            get => _WhenToUse;
            set => _WhenToUse = value ?? string.Empty;
        }

        /// <summary>
        /// File globs, relative to the project root, that make the skill relevant. Empty means the skill is
        /// always listed. Never null.
        /// </summary>
        public List<string> AppliesTo
        {
            get => _AppliesTo;
            set => _AppliesTo = value ?? new List<string>();
        }

        /// <summary>
        /// Executables that must be on PATH for the skill to be listed (alternatives separated by <c>|</c>). Empty
        /// means none. Never null.
        /// </summary>
        public List<string> RequiresTools
        {
            get => _RequiresTools;
            set => _RequiresTools = value ?? new List<string>();
        }

        /// <summary>
        /// A short hint describing the arguments the skill accepts when invoked by name, for example
        /// <c>[base-branch]</c>. Empty when the skill takes no arguments. Never null.
        /// </summary>
        public string ArgumentHint
        {
            get => _ArgumentHint;
            set => _ArgumentHint = value ?? string.Empty;
        }

        /// <summary>
        /// The skill's category (kebab-case), or empty to use <see cref="DefaultSkillCategories.For"/>. Written to
        /// the seeded <c>SKILL.md</c> as <c>category:</c>. Never null.
        /// </summary>
        public string Category { get; set; } = string.Empty;

        /// <summary>
        /// The prose procedure written into the body after the when-to-use paragraph. May contain
        /// <c>$ARGUMENTS</c> and <c>$1</c> to <c>$9</c> placeholders. Required when there are no commands.
        /// Never null.
        /// </summary>
        public string Body
        {
            get => _Body;
            set => _Body = value ?? string.Empty;
        }

        /// <summary>
        /// The commands the skill declares. May be empty for a playbook. Never null.
        /// </summary>
        public List<DefaultSkillCommandDef> Commands
        {
            get => _Commands;
            set => _Commands = value ?? new List<DefaultSkillCommandDef>();
        }

        /// <summary>
        /// Extra files written into the skill directory when it is seeded, keyed by path relative to the skill
        /// directory (for example <c>resources/mux-skill.ps1</c>). Paths use forward slashes and must stay inside
        /// the skill directory. Never null.
        /// </summary>
        public Dictionary<string, string> Resources
        {
            get => _Resources;
            set => _Resources = value ?? new Dictionary<string, string>(StringComparer.Ordinal);
        }

        /// <summary>
        /// Whether this definition is a playbook (no commands).
        /// </summary>
        public bool IsPlaybook => _Commands.Count == 0;

        #endregion
    }
}
