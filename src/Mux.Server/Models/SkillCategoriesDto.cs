namespace Mux.Server.Models
{
    using System.Collections.Generic;
    using Mux.Core.Skills;

    /// <summary>
    /// Response of <c>GET /v1.0/api/skills/categories</c>: the categories in use with their skill counts, and the
    /// canonical categories a surface should offer when editing.
    /// </summary>
    public sealed class SkillCategoriesDto
    {
        /// <summary>The categories in use, canonical ones first in canonical order.</summary>
        public List<SkillCategoryCount> Items { get; set; } = new List<SkillCategoryCount>();

        /// <summary>The number of categories in use.</summary>
        public int Count { get; set; }

        /// <summary>The canonical categories.</summary>
        public List<string> Known { get; set; } = new List<string>();
    }
}
