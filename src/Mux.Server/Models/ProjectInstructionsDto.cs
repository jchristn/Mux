namespace Mux.Server.Models
{
    using System.Collections.Generic;

    /// <summary>
    /// Result of <c>GET /v1.0/api/context/instructions</c>: the project instruction files a run in the given
    /// working directory would load into its system prompt.
    /// </summary>
    public sealed class ProjectInstructionsDto
    {
        /// <summary>The working directory the files were resolved for.</summary>
        public string WorkingDirectory { get; set; } = string.Empty;

        /// <summary>Whether project instructions are enabled in settings.</summary>
        public bool Enabled { get; set; }

        /// <summary>The absolute paths of the files included, in prompt order (user file, then outer to inner).</summary>
        public List<string> Sources { get; set; } = new List<string>();

        /// <summary>Files found but left out because the size cap was reached.</summary>
        public List<string> DroppedSources { get; set; } = new List<string>();

        /// <summary>The UTF-8 byte count of the included content.</summary>
        public long TotalBytes { get; set; }

        /// <summary>Whether the size cap dropped or cut a file.</summary>
        public bool Truncated { get; set; }

        /// <summary>The combined text placed in the system prompt (empty when nothing applies).</summary>
        public string Text { get; set; } = string.Empty;
    }
}
