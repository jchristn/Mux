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
        private List<string> _AppliesTo = new List<string>();
        private bool _UserInvocable = true;
        private bool _ModelInvocable = true;
        private string _ArgumentHint = string.Empty;
        private List<string> _UnrecognizedFields = new List<string>();

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

        /// <summary>
        /// File globs, relative to the project root, that make the skill relevant (for example
        /// <c>package.json</c> or <c>requirements*.txt</c>). When the skill listing mode is <c>relevant</c>, a
        /// skill with globs is advertised to the model only when at least one glob matches. Empty means the
        /// skill is always relevant. Never null.
        /// </summary>
        public List<string> AppliesTo
        {
            get => _AppliesTo;
            set => _AppliesTo = value ?? new List<string>();
        }

        /// <summary>
        /// Whether a user can invoke the skill by name as a slash command (<c>/&lt;name&gt; args</c>).
        /// Defaults to <c>true</c>. Also read from the Claude Code field <c>user-invocable</c>.
        /// </summary>
        public bool UserInvocable
        {
            get => _UserInvocable;
            set => _UserInvocable = value;
        }

        /// <summary>
        /// Whether the skill is advertised to the model in the system-prompt listing. Defaults to
        /// <c>true</c>. The Claude Code field <c>disable-model-invocation: true</c> sets this to <c>false</c>;
        /// such a skill stays invocable by name and through the <c>skill</c> tool.
        /// </summary>
        public bool ModelInvocable
        {
            get => _ModelInvocable;
            set => _ModelInvocable = value;
        }

        /// <summary>
        /// A short hint describing the arguments accepted when the skill is invoked by name, shown next to
        /// the command in completion and listings (for example <c>[base-branch]</c>). Empty when unspecified.
        /// Never null.
        /// </summary>
        public string ArgumentHint
        {
            get => _ArgumentHint;
            set => _ArgumentHint = value ?? string.Empty;
        }

        /// <summary>
        /// Top-level frontmatter keys the parser did not recognize, in the order they appeared. The loader
        /// reports each as a validation warning; they never make a skill invalid. Never null.
        /// </summary>
        public List<string> UnrecognizedFields
        {
            get => _UnrecognizedFields;
            set => _UnrecognizedFields = value ?? new List<string>();
        }

        /// <summary>
        /// Whether the skill is a playbook: it declares no commands, so invoking it means following its body.
        /// </summary>
        public bool IsPlaybook => _Commands.Count == 0;

        #endregion
    }
}
