namespace Mux.Server.Models
{
    using System.Collections.Generic;

    /// <summary>
    /// The outcome of installing or removing pack skills.
    /// </summary>
    public sealed class SkillPackResultDto
    {
        /// <summary>The pack id.</summary>
        public string Pack { get; set; } = string.Empty;

        /// <summary>Skill ids installed.</summary>
        public List<string> Installed { get; set; } = new List<string>();

        /// <summary>Skill ids removed.</summary>
        public List<string> Removed { get; set; } = new List<string>();

        /// <summary>Skills left alone, as <c>id: reason</c>.</summary>
        public List<string> Skipped { get; set; } = new List<string>();
    }
}
