namespace Mux.Core.Models
{
    using System.Collections.Generic;

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
        /// A convenience shortcut for <c>Validation.IsValid</c>.
        /// </summary>
        public bool IsValid => _Validation.IsValid;

        #endregion
    }
}
