namespace Mux.Server.Models
{
    /// <summary>
    /// The body of <c>POST /v1.0/api/skills/packs/install</c> and <c>/remove</c>.
    /// </summary>
    public sealed class SkillPackRequestDto
    {
        /// <summary>The pack id (required).</summary>
        public string? Pack { get; set; }

        /// <summary>One skill to install or remove, or null for the whole pack.</summary>
        public string? Skill { get; set; }

        /// <summary>Reinstall this pack's copies, or remove edited ones.</summary>
        public bool Force { get; set; }
    }
}
