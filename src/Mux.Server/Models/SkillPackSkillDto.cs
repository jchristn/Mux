namespace Mux.Server.Models
{
    /// <summary>
    /// One skill in a pack, as returned by <c>GET /v1.0/api/skills/packs/{id}</c>.
    /// </summary>
    public sealed class SkillPackSkillDto
    {
        /// <summary>The skill id.</summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>The skill's description from its frontmatter.</summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>The skill's category from its frontmatter, or empty.</summary>
        public string Category { get; set; } = string.Empty;

        /// <summary>Whether it is installed from this pack.</summary>
        public bool Installed { get; set; }

        /// <summary>How many files the skill bundles (including SKILL.md).</summary>
        public int FileCount { get; set; }
    }
}
