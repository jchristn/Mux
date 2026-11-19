namespace Mux.Server.Models
{
    /// <summary>
    /// The result of compacting a saved conversation.
    /// </summary>
    public class SessionCompactResult
    {
        /// <summary>Whether the conversation changed.</summary>
        public bool Compacted { get; set; }

        /// <summary>A human-readable outcome (for example "nothing to compact").</summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>The endpoint that wrote the summary.</summary>
        public string Endpoint { get; set; } = string.Empty;

        /// <summary>Messages before compaction.</summary>
        public int MessagesBefore { get; set; }

        /// <summary>Messages after compaction.</summary>
        public int MessagesAfter { get; set; }

        /// <summary>Estimated tokens before compaction.</summary>
        public int EstimatedTokensBefore { get; set; }

        /// <summary>Estimated tokens after compaction.</summary>
        public int EstimatedTokensAfter { get; set; }

        /// <summary>The endpoint's context window in tokens, for showing usage.</summary>
        public int ContextWindow { get; set; }
    }
}
