namespace Mux.Server.Models
{
    /// <summary>
    /// The <c>compacted</c> event streamed when the run compacts its context (automatically, when the conversation
    /// approaches the endpoint's context window).
    /// </summary>
    public class ChatCompactedEvent
    {
        /// <summary>Messages before compaction.</summary>
        public int MessagesBefore { get; set; }

        /// <summary>Messages after compaction.</summary>
        public int MessagesAfter { get; set; }

        /// <summary>Estimated tokens before compaction.</summary>
        public int EstimatedTokensBefore { get; set; }

        /// <summary>Estimated tokens after compaction.</summary>
        public int EstimatedTokensAfter { get; set; }

        /// <summary>Whether a summary message replaced the older turns.</summary>
        public bool SummaryCreated { get; set; }

        /// <summary>The compaction strategy that ran (for example <c>summary</c> or <c>trim</c>).</summary>
        public string Strategy { get; set; } = string.Empty;

        /// <summary>Why compaction ran.</summary>
        public string Reason { get; set; } = string.Empty;
    }
}
