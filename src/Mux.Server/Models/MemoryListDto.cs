namespace Mux.Server.Models
{
    using System.Collections.Generic;

    /// <summary>
    /// Result of <c>GET /v1.0/api/memory</c>: the memories visible from a working directory.
    /// </summary>
    public sealed class MemoryListDto
    {
        /// <summary>The working directory the project scope was resolved for.</summary>
        public string WorkingDirectory { get; set; } = string.Empty;

        /// <summary>The folder key of the project scope.</summary>
        public string ProjectKey { get; set; } = string.Empty;

        /// <summary>Whether memory is enabled in settings.</summary>
        public bool Enabled { get; set; }

        /// <summary>The memories, project first, newest first within each scope.</summary>
        public List<MemoryDto> Memories { get; set; } = new List<MemoryDto>();
    }
}
