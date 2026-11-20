namespace Mux.Server.Models
{
    using System;

    /// <summary>
    /// A suggested working directory for a new conversation, returned by <c>GET /v1.0/api/workspaces</c>. Any
    /// existing directory may be used; these are only suggestions.
    /// </summary>
    public class WorkspaceDto
    {
        /// <summary>Absolute directory path.</summary>
        public string Path { get; set; } = string.Empty;

        /// <summary>The folder name (last path segment), for display.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Where the suggestion came from: <c>recent</c> (a saved session ran there) or <c>home</c>.</summary>
        public string Source { get; set; } = "recent";

        /// <summary>Most recent session update in this directory (UTC), or null for the home directory.</summary>
        public DateTime? LastUsedUtc { get; set; } = null;

        /// <summary>Number of saved sessions that ran in this directory.</summary>
        public int SessionCount { get; set; } = 0;
    }
}
