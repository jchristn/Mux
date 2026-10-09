namespace Mux.Server.Models
{
    /// <summary>
    /// Body of <c>POST /v1.0/api/mcp-servers/validate</c>: either the <see cref="Name"/> of a saved server, or an
    /// unsaved <see cref="Server"/> definition (a blank secret reuses the saved server's secret with the same name).
    /// </summary>
    public sealed class McpValidateRequestDto
    {
        /// <summary>The name of a saved MCP server to validate.</summary>
        public string? Name { get; set; }

        /// <summary>An MCP server definition to validate without saving it. Takes precedence over <see cref="Name"/>.</summary>
        public McpServerDto? Server { get; set; }

        /// <summary>How long to wait for the connection, in seconds (1 to 120). Defaults to 30.</summary>
        public int? TimeoutSeconds { get; set; }
    }
}
