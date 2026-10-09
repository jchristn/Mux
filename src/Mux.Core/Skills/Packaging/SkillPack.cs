namespace Mux.Core.Skills.Packaging
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// An opt-in group of skills shipped inside mux (<c>src/Mux.Core/Skills/Packs/&lt;id&gt;/</c>): its metadata from
    /// <c>pack.json</c> and its skills. Nothing from a pack reaches the user's skills directory until it is installed.
    /// </summary>
    public sealed class SkillPack
    {
        #region Public-Members

        /// <summary>The pack id (its folder name), for example <c>engineering</c>.</summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>A short title.</summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>What the pack contains.</summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>The category its skills belong to.</summary>
        public string Category { get; set; } = string.Empty;

        /// <summary>Where the skills came from (for example a repository URL and commit).</summary>
        public string Source { get; set; } = string.Empty;

        /// <summary>The license the skills are distributed under.</summary>
        public string License { get; set; } = string.Empty;

        /// <summary>The pack's skills, sorted by id.</summary>
        public List<BundledSkill> Skills { get; set; } = new List<BundledSkill>();

        #endregion

        #region Public-Methods

        /// <summary>
        /// Finds one of the pack's skills by id.
        /// </summary>
        /// <param name="skillId">The skill id (case-insensitive).</param>
        /// <returns>The skill, or null.</returns>
        public BundledSkill? Find(string skillId)
        {
            return Skills.Find((BundledSkill skill) => string.Equals(skill.Id, (skillId ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase));
        }

        #endregion
    }
}
