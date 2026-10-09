namespace Mux.Core.Skills.Packaging
{
    using System.Collections.Generic;

    /// <summary>
    /// The outcome of installing or removing pack skills: what changed and what was left alone, with reasons.
    /// </summary>
    public sealed class SkillPackResult
    {
        #region Public-Members

        /// <summary>The pack id.</summary>
        public string Pack { get; set; } = string.Empty;

        /// <summary>Skill ids written to the skills directory.</summary>
        public List<string> Installed { get; set; } = new List<string>();

        /// <summary>Skill ids removed from the skills directory.</summary>
        public List<string> Removed { get; set; } = new List<string>();

        /// <summary>Skills left alone, as <c>id: reason</c>.</summary>
        public List<string> Skipped { get; set; } = new List<string>();

        #endregion
    }
}
