namespace Mux.Server.Models
{
    using System.Collections.Generic;

    /// <summary>
    /// One opt-in skill pack as returned by <c>/v1.0/api/skills/packs</c>.
    /// </summary>
    public sealed class SkillPackDto
    {
        /// <summary>The pack id.</summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>A short title.</summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>What the pack contains.</summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>The category its skills belong to.</summary>
        public string Category { get; set; } = string.Empty;

        /// <summary>Where the skills came from.</summary>
        public string Source { get; set; } = string.Empty;

        /// <summary>The license the skills are distributed under.</summary>
        public string License { get; set; } = string.Empty;

        /// <summary>How many skills the pack has.</summary>
        public int SkillCount { get; set; }

        /// <summary>How many of them are installed.</summary>
        public int InstalledCount { get; set; }

        /// <summary>The pack's skills (only on the single-pack route; null in the list).</summary>
        public List<SkillPackSkillDto>? Skills { get; set; }
    }
}
