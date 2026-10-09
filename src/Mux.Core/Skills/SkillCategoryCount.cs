namespace Mux.Core.Skills
{
    /// <summary>
    /// How many skills fall in one category, for category listings and filters.
    /// </summary>
    public sealed class SkillCategoryCount
    {
        #region Public-Members

        /// <summary>The category.</summary>
        public string Category { get; set; } = string.Empty;

        /// <summary>The number of skills in it.</summary>
        public int Count { get; set; }

        /// <summary>Whether the category is one of the canonical ones.</summary>
        public bool Known { get; set; }

        #endregion
    }
}
