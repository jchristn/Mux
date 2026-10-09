namespace Mux.Server.Models
{
    using System.Collections.Generic;

    /// <summary>
    /// Result of <c>POST /v1.0/api/mcp-servers/validate</c>: whether mux connected, the transport, the discovered tools,
    /// and on failure a one-line <see cref="Error"/> plus the diagnostic <see cref="Details"/> (connection cause, HTTP
    /// status and headers, response body, client log, server stderr). Auth secrets never appear in either.
    /// </summary>
    public sealed class McpValidateResponseDto
    {
        /// <summary>The server name.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Whether the connection and MCP handshake succeeded.</summary>
        public bool Connected { get; set; }

        /// <summary>The transport used: <c>stdio</c> or <c>http</c>.</summary>
        public string Method { get; set; } = string.Empty;

        /// <summary>The number of tools discovered.</summary>
        public int ToolCount { get; set; }

        /// <summary>The discovered tool names, prefixed with the server name.</summary>
        public List<string> Tools { get; set; } = new List<string>();

        /// <summary>The one-line failure summary, or null on success.</summary>
        public string? Error { get; set; }

        /// <summary>The diagnostic lines behind <see cref="Error"/>, or null.</summary>
        public string? Details { get; set; }

        /// <summary>How long the attempt took, in milliseconds.</summary>
        public long ElapsedMs { get; set; }
    }
}
