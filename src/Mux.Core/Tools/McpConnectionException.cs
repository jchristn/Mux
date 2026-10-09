namespace Mux.Core.Tools
{
    using System;

    /// <summary>
    /// Thrown when mux cannot connect to an MCP server. <see cref="Summary"/> is one line that names the server, the
    /// address or command, and the cause (for example "connection refused" or "HTTP 401 Unauthorized");
    /// <see cref="Details"/> holds the diagnostic lines behind it (status, headers, response body, client log, and
    /// so on). <see cref="Exception.Message"/> is the summary followed by the details, so callers that only show the
    /// message still show everything.
    /// </summary>
    public sealed class McpConnectionException : InvalidOperationException
    {
        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="McpConnectionException"/> class.
        /// </summary>
        /// <param name="summary">The one-line summary.</param>
        /// <param name="details">The diagnostic lines, or empty.</param>
        /// <param name="innerException">The underlying exception, or null.</param>
        /// <param name="reply">The HTTP reply the diagnostic probe received, or null (stdio, or no reply).</param>
        public McpConnectionException(string summary, string details, Exception? innerException = null, McpProbeReply? reply = null)
            : base(Compose(summary, details), innerException)
        {
            Summary = summary ?? string.Empty;
            Details = details ?? string.Empty;
            Reply = reply;
        }

        #endregion

        #region Public-Members

        /// <summary>The one-line summary of the failure.</summary>
        public string Summary { get; }

        /// <summary>The diagnostic detail lines (newline-separated), or empty.</summary>
        public string Details { get; }

        /// <summary>The HTTP reply the diagnostic probe received, or null when there was none.</summary>
        public McpProbeReply? Reply { get; }

        #endregion

        #region Private-Methods

        private static string Compose(string summary, string details)
        {
            string first = summary ?? string.Empty;
            return string.IsNullOrWhiteSpace(details) ? first : first + Environment.NewLine + details.TrimEnd();
        }

        #endregion
    }
}
