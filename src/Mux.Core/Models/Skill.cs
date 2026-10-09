namespace Mux.Core.Models
{
    using System.Collections.Generic;
    using Mux.Core.Enums;

    /// <summary>
    /// A fully loaded skill: its parsed manifest, its Markdown body, the fenced code blocks extracted from
    /// that body (keyed by their <c>id</c>), the directory it was loaded from, and the result of validating
    /// it. An invalid skill is still represented here so the inventory can explain why it failed.
    /// </summary>
    public class Skill
    {
        #region Private-Members

        private SkillManifest _Manifest = new SkillManifest();
        private string _Body = string.Empty;
        private string _DirectoryPath = string.Empty;
        private Dictionary<string, string> _CodeBlocks = new Dictionary<string, string>();
        private SkillValidationResult _Validation = new SkillValidationResult();
        private SkillScopeEnum _Scope = SkillScopeEnum.User;

        #endregion

        #region Public-Members

        /// <summary>
        /// The skill's parsed frontmatter.
        /// </summary>
        public SkillManifest Manifest
        {
            get => _Manifest;
            set => _Manifest = value ?? new SkillManifest();
        }

        /// <summary>
        /// The Markdown body of <c>SKILL.md</c> below the frontmatter — the instructions the model reads
        /// when it opens the skill.
        /// </summary>
        public string Body
        {
            get => _Body;
            set => _Body = value ?? string.Empty;
        }

        /// <summary>
        /// The absolute path of the skill's directory.
        /// </summary>
        public string DirectoryPath
        {
            get => _DirectoryPath;
            set => _DirectoryPath = value ?? string.Empty;
        }

        /// <summary>
        /// The fenced code blocks from the body that carried an <c>id=</c> tag, keyed by that id. Never null.
        /// </summary>
        public Dictionary<string, string> CodeBlocks
        {
            get => _CodeBlocks;
            set => _CodeBlocks = value ?? new Dictionary<string, string>();
        }

        /// <summary>
        /// The result of validating this skill. Never null.
        /// </summary>
        public SkillValidationResult Validation
        {
            get => _Validation;
            set => _Validation = value ?? new SkillValidationResult();
        }

        /// <summary>
        /// Where the skill was loaded from. Defaults to <see cref="SkillScopeEnum.User"/>.
        /// </summary>
        public SkillScopeEnum Scope
        {
            get => _Scope;
            set => _Scope = value;
        }

        /// <summary>
        /// Whether this project skill's commands are blocked because the project has not been trusted. A
        /// blocked skill is not advertised or runnable; its status explains why. Always false for user skills.
        /// </summary>
        public bool CommandsBlocked { get; set; }

        /// <summary>
        /// Whether this project skill hides a user skill with the same id.
        /// </summary>
        public bool ShadowsUserSkill { get; set; }

        /// <summary>
        /// The per-user category override from <c>~/.mux/skills.json</c>, or null when the skill uses the category
        /// in its <c>SKILL.md</c> (or an inferred one). Applied by the runtime and the surfaces that list skills.
        /// </summary>
        public string? CategoryOverride { get; set; }

        /// <summary>
        /// The effective category: the override, then the frontmatter <c>category</c>, then one inferred from the
        /// tags, then <c>general</c>.
        /// </summary>
        public string Category => Mux.Core.Skills.SkillCategories.Resolve(_Manifest, CategoryOverride);

        /// <summary>
        /// Whether the skill can be offered to the model and run: valid, enabled, and not blocked.
        /// </summary>
        public bool IsUsable => IsValid && _Manifest.Enabled && !CommandsBlocked;

        /// <summary>
        /// A convenience shortcut for <c>Validation.IsValid</c>.
        /// </summary>
        public bool IsValid => _Validation.IsValid;

        #endregion
    }
}
