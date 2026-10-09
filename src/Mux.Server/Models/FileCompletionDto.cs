namespace Mux.Server.Models
{
    using System.Collections.Generic;

    /// <summary>
    /// Result of <c>GET /v1.0/api/files/complete</c>: project paths that match what was typed after <c>@</c>, best
    /// match first, for composer completion.
    /// </summary>
    public sealed class FileCompletionDto
    {
        /// <summary>The working directory the paths are relative to.</summary>
        public string WorkingDirectory { get; set; } = string.Empty;

        /// <summary>The text that was matched.</summary>
        public string Prefix { get; set; } = string.Empty;

        /// <summary>Relative paths with forward slashes; directories end with <c>/</c>.</summary>
        public List<string> Paths { get; set; } = new List<string>();

        /// <summary>The same paths formatted as mentions (quoted when they contain spaces).</summary>
        public List<string> Mentions { get; set; } = new List<string>();
    }
}
