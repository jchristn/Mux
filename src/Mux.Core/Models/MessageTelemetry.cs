namespace Mux.Core.Models
{
    /// <summary>Display-only metrics persisted with an assistant response; never part of a model request.</summary>
    public sealed class MessageTelemetry
    {
        /// <summary>The model that produced this response.</summary>
        public string Model { get; set; } = string.Empty;
        /// <summary>Milliseconds to the first token, or -1 if no token streamed.</summary>
        public long TtftMs { get; set; }
        /// <summary>Milliseconds spent streaming.</summary>
        public long StreamingMs { get; set; }
        /// <summary>Total turn duration in milliseconds.</summary>
        public long TotalMs { get; set; }
        /// <summary>Input token count.</summary>
        public int InputTokens { get; set; }
        /// <summary>Output token count.</summary>
        public int OutputTokens { get; set; }
        /// <summary>Total token count.</summary>
        public int TotalTokens { get; set; }
    }
}
