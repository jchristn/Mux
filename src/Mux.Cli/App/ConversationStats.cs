namespace Mux.Cli.App
{
    /// <summary>
    /// A snapshot of conversation telemetry rendered in the sidebar: per-turn and session-wide timing
    /// and token measurements. Timing is measured client-side by the shell; token counts are the
    /// engine's estimates until real provider usage is wired through (input/output/prompt/cached are
    /// populated when the endpoint returns usage data, otherwise left at zero).
    /// </summary>
    public sealed class ConversationStats
    {
        /// <summary>Whether a turn is currently running.</summary>
        public bool Busy { get; set; }

        /// <summary>The number of turns currently running concurrently.</summary>
        public int InFlight { get; set; }

        /// <summary>The number of prompts queued for a free concurrency slot.</summary>
        public int Queued { get; set; }

        /// <summary>The number of completed turns this session.</summary>
        public int Turns { get; set; }

        /// <summary>Time-to-first-token of the most recent turn, in milliseconds (-1 when unknown).</summary>
        public long LastTtftMs { get; set; } = -1;

        /// <summary>Streaming duration (first token → completion) of the most recent turn, in milliseconds.</summary>
        public long LastStreamMs { get; set; }

        /// <summary>Estimated context tokens after the most recent turn.</summary>
        public int LastContextTokens { get; set; }

        /// <summary>Sum of streaming durations across the session, in milliseconds.</summary>
        public long SessionStreamMs { get; set; }

        /// <summary>Sum of time-to-first-token across turns that reported one, in milliseconds.</summary>
        public long SessionTtftMs { get; set; }

        /// <summary>The number of turns that reported a time-to-first-token (for averaging).</summary>
        public int TtftSamples { get; set; }

        /// <summary>Session prompt/input tokens reported by the provider (0 when unavailable).</summary>
        public long InputTokens { get; set; }

        /// <summary>Session output/completion tokens reported by the provider (0 when unavailable).</summary>
        public long OutputTokens { get; set; }

        /// <summary>Session cached prompt tokens reported by the provider (0 when unavailable).</summary>
        public long CachedTokens { get; set; }
    }
}
