namespace Mux.Core.Skills.Packaging
{
    using System.Collections.Generic;

    /// <summary>
    /// What happened to one skill during an import: where it came from and went, its status, the normalization
    /// changes, things to review by hand, and validation errors.
    /// </summary>
    public sealed class SkillImportReport
    {
        #region Public-Members

        /// <summary>The skill id (its folder name in the destination).</summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>The skill's folder in the source.</summary>
        public string SourcePath { get; set; } = string.Empty;

        /// <summary>The destination folder.</summary>
        public string Target { get; set; } = string.Empty;

        /// <summary><c>imported</c>, <c>would-import</c> (dry run), <c>skipped</c>, or <c>invalid</c>.</summary>
        public string Status { get; set; } = string.Empty;

        /// <summary>The category written to the frontmatter (or already there).</summary>
        public string Category { get; set; } = string.Empty;

        /// <summary>Normalization changes, one entry per rule with a count.</summary>
        public List<string> Changes { get; set; } = new List<string>();

        /// <summary>References with no mux equivalent, for a person to review.</summary>
        public List<string> Flags { get; set; } = new List<string>();

        /// <summary>Validation errors after import, or why the skill was skipped.</summary>
        public List<string> Errors { get; set; } = new List<string>();

        #endregion
    }
}
