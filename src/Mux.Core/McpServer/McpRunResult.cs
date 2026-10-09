namespace Mux.Core.McpServer
{
    /// <summary>
    /// The outcome of one <c>run</c> tool call: the final answer and a compact summary of the agent turn.
    /// </summary>
    public sealed class McpRunResult
    {
        #region Public-Members

        /// <summary>The assistant's final answer text.</summary>
        public string Answer { get; set; } = string.Empty;

        /// <summary>The run status, for example <c>completed</c>, <c>completed_with_errors</c>, or <c>failed</c>.</summary>
        public string Status { get; set; } = "completed";

        /// <summary>The endpoint that ran the turn.</summary>
        public string Endpoint { get; set; } = string.Empty;

        /// <summary>The model that ran the turn.</summary>
        public string Model { get; set; } = string.Empty;

        /// <summary>Agent iterations (model calls).</summary>
        public int Iterations { get; set; }

        /// <summary>Tool calls the model made.</summary>
        public int ToolCalls { get; set; }

        /// <summary>Errors raised during the turn.</summary>
        public int Errors { get; set; }

        /// <summary>Wall-clock duration in milliseconds.</summary>
        public long DurationMs { get; set; }

        /// <summary>Input tokens reported by the backend, when known.</summary>
        public long InputTokens { get; set; }

        /// <summary>Output tokens reported by the backend, when known.</summary>
        public long OutputTokens { get; set; }

        /// <summary>The first error message when the turn failed, or null.</summary>
        public string? ErrorMessage { get; set; }

        #endregion
    }
}
