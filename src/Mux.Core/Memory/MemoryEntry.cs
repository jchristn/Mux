namespace Mux.Core.Memory
{
    using System;

    /// <summary>
    /// One remembered fact: a short name (its slug is the file name), a one-line description used in the prompt
    /// index, the full content, its scope, and timestamps.
    /// </summary>
    public sealed class MemoryEntry
    {
        #region Public-Members

        /// <summary>The memory's name as given (also used, slugged, as its file name).</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>The slug of the name: lowercase letters, digits, and hyphens.</summary>
        public string Slug { get; set; } = string.Empty;

        /// <summary>A one-line summary shown in the prompt index.</summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>The full text of the memory.</summary>
        public string Content { get; set; } = string.Empty;

        /// <summary>Where the memory applies.</summary>
        public MemoryScopeEnum Scope { get; set; } = MemoryScopeEnum.Project;

        /// <summary>When the memory was first saved (UTC).</summary>
        public DateTime CreatedUtc { get; set; }

        /// <summary>When the memory was last changed (UTC).</summary>
        public DateTime UpdatedUtc { get; set; }

        /// <summary>The file that holds the memory.</summary>
        public string FilePath { get; set; } = string.Empty;

        #endregion
    }
}
