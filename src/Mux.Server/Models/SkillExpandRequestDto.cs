namespace Mux.Server.Models
{
    /// <summary>
    /// Body of <c>POST /v1.0/api/skills/expand</c>: typed slash text to expand into a skill invocation.
    /// </summary>
    public sealed class SkillExpandRequestDto
    {
        /// <summary>The typed text, for example <c>/code-review main</c>.</summary>
        public string? Input { get; set; }

        /// <summary>The working directory whose project skills apply. Null uses the server's current directory.</summary>
        public string? WorkingDirectory { get; set; }
    }
}
