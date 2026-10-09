namespace Mux.Server.Models
{
    /// <summary>
    /// Body of <c>POST /v1.0/api/memory</c>: a memory to create or update (matched by name).
    /// </summary>
    public sealed class MemorySaveRequestDto
    {
        /// <summary>The name. Required.</summary>
        public string? Name { get; set; }

        /// <summary>The one-line description. Defaults to the first line of the content.</summary>
        public string? Description { get; set; }

        /// <summary>The full text. Defaults to the description.</summary>
        public string? Content { get; set; }

        /// <summary><c>project</c> (default) or <c>global</c>.</summary>
        public string? Scope { get; set; }

        /// <summary>The working directory that selects the project scope. Defaults to the server's current directory.</summary>
        public string? WorkingDirectory { get; set; }
    }
}
