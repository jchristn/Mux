namespace Mux.Server.Models
{
    /// <summary>
    /// Body of <c>PUT /v1.0/api/skills/category</c>: the skill and its new category. A null or blank category clears
    /// the override so the skill uses the category in its <c>SKILL.md</c>.
    /// </summary>
    public sealed class SkillCategoryRequestDto
    {
        /// <summary>The skill id.</summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>The category (normalized to kebab-case), or null or blank to clear the override.</summary>
        public string? Category { get; set; }
    }
}
