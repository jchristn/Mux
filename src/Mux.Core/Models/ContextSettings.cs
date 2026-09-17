namespace Mux.Core.Models
{
    using System;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Settings that steer how mux turns a large file into model context: whether a file over the inline
    /// threshold is mapped, summarized, or truncated, and how the optional summary cache behaves. Values are
    /// validated and clamped on assignment, so a hand-edited or client-supplied settings file cannot push them
    /// out of range.
    /// </summary>
    public class ContextSettings
    {
        #region Private-Members

        private string _LargeFileMode = "map";
        private int _InlineThresholdBytes = 65536;
        private double _InlineContextWindowFraction = 0.25;
        private int _SummaryChunkLines = 400;
        private bool _SummaryCacheEnabled = true;
        private int _SummaryCacheRetentionDays = 7;

        #endregion

        #region Public-Members

        /// <summary>
        /// How a file larger than <see cref="InlineThresholdBytes"/> is turned into context: <c>map</c> (a
        /// structural outline with line ranges — the default), <c>summarize</c> (an iterative map-reduce
        /// summary that still carries line-range pointers), or <c>truncate</c> (a head slice, the strict cap).
        /// Unknown values fall back to <c>map</c>.
        /// </summary>
        [JsonPropertyName("largeFileMode")]
        public string LargeFileMode
        {
            get => _LargeFileMode;
            set => _LargeFileMode = TryNormalizeLargeFileMode(value, out string normalized) ? normalized : "map";
        }

        /// <summary>
        /// The byte size at or below which a file is inlined whole. Files above it follow
        /// <see cref="LargeFileMode"/>. Clamped to the range 1024–10485760. Defaults to 65536 (64 KiB).
        /// </summary>
        [JsonPropertyName("inlineThresholdBytes")]
        public int InlineThresholdBytes
        {
            get => _InlineThresholdBytes;
            set => _InlineThresholdBytes = Math.Clamp(value, 1024, 10_485_760);
        }

        /// <summary>
        /// The fraction of the selected endpoint's context window a single file may occupy before it is mapped
        /// or summarized instead of inlined whole. So "too large to inline" scales with the model: a wide
        /// context window inlines much larger files than a narrow one. Applied via
        /// <see cref="ResolveInlineThresholdBytes"/>; when the endpoint reports no context window,
        /// <see cref="InlineThresholdBytes"/> is used instead. Clamped to the range 0.05–0.9. Defaults to 0.25.
        /// </summary>
        [JsonPropertyName("inlineContextWindowFraction")]
        public double InlineContextWindowFraction
        {
            get => _InlineContextWindowFraction;
            set => _InlineContextWindowFraction = Math.Clamp(value, 0.05, 0.9);
        }

        /// <summary>
        /// The number of lines per chunk when summarizing a large file. Clamped to the range 50–5000.
        /// Defaults to 400.
        /// </summary>
        [JsonPropertyName("summaryChunkLines")]
        public int SummaryChunkLines
        {
            get => _SummaryChunkLines;
            set => _SummaryChunkLines = Math.Clamp(value, 50, 5000);
        }

        /// <summary>
        /// Whether summarized large-file context is cached to disk (keyed by content hash) so an unchanged file
        /// is not re-summarized every turn. Defaults to true.
        /// </summary>
        [JsonPropertyName("summaryCacheEnabled")]
        public bool SummaryCacheEnabled
        {
            get => _SummaryCacheEnabled;
            set => _SummaryCacheEnabled = value;
        }

        /// <summary>
        /// How many days a summary-cache entry is retained before a periodic cleanup pass deletes it. Clamped
        /// to the range 1–365. Defaults to 7.
        /// </summary>
        [JsonPropertyName("summaryCacheRetentionDays")]
        public int SummaryCacheRetentionDays
        {
            get => _SummaryCacheRetentionDays;
            set => _SummaryCacheRetentionDays = Math.Clamp(value, 1, 365);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Resolves the effective inline size threshold in bytes: below or at it a file is inlined whole, above
        /// it the file is mapped or summarized. When the endpoint reports a context window, the threshold is
        /// <see cref="InlineContextWindowFraction"/> of that window converted to bytes (tokens ×
        /// <paramref name="tokenEstimationRatio"/>), so it scales with the model; otherwise
        /// <paramref name="fallbackBytes"/> is used. The result is floored at 1024 bytes so even a tiny window
        /// still permits a small inline.
        /// </summary>
        /// <param name="contextWindowTokens">The selected endpoint's context window in tokens; 0 or negative means unknown.</param>
        /// <param name="tokenEstimationRatio">Estimated characters (≈ bytes) per token; 0 or negative means unknown.</param>
        /// <param name="fallbackBytes">The byte threshold to use when the context window is unknown (floored at 1).</param>
        /// <returns>The effective inline threshold in bytes.</returns>
        public int ResolveInlineThresholdBytes(int contextWindowTokens, double tokenEstimationRatio, int fallbackBytes)
        {
            if (contextWindowTokens <= 0 || tokenEstimationRatio <= 0)
            {
                return Math.Max(1, fallbackBytes);
            }

            double derived = _InlineContextWindowFraction * contextWindowTokens * tokenEstimationRatio;
            if (derived < 1024) derived = 1024;
            if (derived > int.MaxValue) return int.MaxValue;
            return (int)derived;
        }

        /// <summary>
        /// Normalizes a large-file-mode string to one of <c>map</c>, <c>summarize</c>, or <c>truncate</c>.
        /// </summary>
        /// <param name="value">The raw mode value.</param>
        /// <param name="normalized">The normalized value when successful; otherwise <c>map</c>.</param>
        /// <returns>True if the input matched a supported mode; otherwise false.</returns>
        public static bool TryNormalizeLargeFileMode(string? value, out string normalized)
        {
            string candidate = (value ?? string.Empty).Trim().ToLowerInvariant();

            switch (candidate)
            {
                case "map":
                    normalized = "map";
                    return true;
                case "summarize":
                case "summary":
                    normalized = "summarize";
                    return true;
                case "truncate":
                case "refuse":
                    normalized = "truncate";
                    return true;
                default:
                    normalized = "map";
                    return false;
            }
        }

        #endregion
    }
}
