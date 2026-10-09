namespace Mux.Server.Models
{
    using System;

    /// <summary>
    /// One persistent memory as returned by <c>/v1.0/api/memory</c>.
    /// </summary>
    public sealed class MemoryDto
    {
        /// <summary>The memory's name (its slug).</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary><c>project</c> or <c>global</c>.</summary>
        public string Scope { get; set; } = "project";

        /// <summary>The one-line description shown in the prompt index.</summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>The full text.</summary>
        public string Content { get; set; } = string.Empty;

        /// <summary>When the memory was created (UTC).</summary>
        public DateTime CreatedUtc { get; set; }

        /// <summary>When the memory was last changed (UTC).</summary>
        public DateTime UpdatedUtc { get; set; }
    }
}
