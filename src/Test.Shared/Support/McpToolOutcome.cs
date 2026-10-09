namespace Test.Shared.Support
{
    /// <summary>
    /// The text and error flag of one MCP <c>tools/call</c> result, as seen by a test client.
    /// </summary>
    public sealed class McpToolOutcome
    {
        /// <summary>Whether the call failed (a JSON-RPC error or a result with <c>isError</c>).</summary>
        public bool IsError { get; set; }

        /// <summary>The concatenated text content, or the error message.</summary>
        public string Text { get; set; } = string.Empty;
    }
}
