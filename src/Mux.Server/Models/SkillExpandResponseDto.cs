namespace Mux.Server.Models
{
    /// <summary>
    /// Result of <c>POST /v1.0/api/skills/expand</c>. When <see cref="Matched"/> is false the input named no
    /// usable, user-invocable skill and the other fields are empty.
    /// </summary>
    public sealed class SkillExpandResponseDto
    {
        /// <summary>Whether the input named a usable, user-invocable skill.</summary>
        public bool Matched { get; set; }

        /// <summary>The matched skill's name, or empty.</summary>
        public string Skill { get; set; } = string.Empty;

        /// <summary>The argument text after the skill name, or empty.</summary>
        public string Arguments { get; set; } = string.Empty;

        /// <summary>The user message to send in place of the slash text, or empty.</summary>
        public string Prompt { get; set; } = string.Empty;

        /// <summary>Whether the matched skill is a playbook (declares no commands).</summary>
        public bool IsPlaybook { get; set; }
    }
}
