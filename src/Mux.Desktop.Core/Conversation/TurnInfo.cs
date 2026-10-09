namespace Mux.Desktop.Conversation
{
    using System;
    using System.Globalization;

    /// <summary>
    /// The telemetry shown under a finished chat turn: timing (time to first token, streaming, total), token
    /// counts, agent steps, tool calls, errors, the estimated context size, and the run status. Builds the
    /// one-line summary on the turn's info button and the multi-line details shown when it is opened.
    /// </summary>
    public sealed class TurnInfo
    {
        #region Public-Members

        /// <summary>The whole turn's duration, in milliseconds.</summary>
        public long TotalMs { get; set; }

        /// <summary>The time to the first streamed token, in milliseconds, or null when nothing streamed.</summary>
        public long? TtftMs { get; set; }

        /// <summary>Input (prompt) tokens.</summary>
        public int InputTokens { get; set; }

        /// <summary>Output (completion) tokens.</summary>
        public int OutputTokens { get; set; }

        /// <summary>Total tokens.</summary>
        public int TotalTokens { get; set; }

        /// <summary>Agent loop steps completed.</summary>
        public int Steps { get; set; }

        /// <summary>Tool calls made.</summary>
        public int ToolCalls { get; set; }

        /// <summary>Errors raised during the turn.</summary>
        public int Errors { get; set; }

        /// <summary>The estimated context size after the turn, in tokens.</summary>
        public int ContextTokens { get; set; }

        /// <summary>The run status (for example <c>completed</c>).</summary>
        public string Status { get; set; } = string.Empty;

        /// <summary>
        /// The streaming time: the total minus the time to first token, never negative.
        /// </summary>
        public long StreamingMs => Math.Max(0, TotalMs - (TtftMs ?? 0));

        #endregion

        #region Public-Methods

        /// <summary>
        /// The compact label shown under the turn, for example <c>ⓘ  1.2 s · 345 tokens</c>.
        /// </summary>
        /// <returns>The summary label.</returns>
        public string BuildSummary()
        {
            return "ⓘ  " + FormatMs(TotalMs) + " · " + TotalTokens.ToString(CultureInfo.InvariantCulture) + " tokens";
        }

        /// <summary>
        /// The multi-line details. <paramref name="timingFormat"/> takes time to first token, streaming, total,
        /// input, output, and total tokens; <paramref name="activityFormat"/> takes steps, tool calls, errors, context
        /// tokens, and status. A missing or malformed localized format falls back to English, so the details always
        /// render.
        /// </summary>
        /// <param name="timingFormat">The localized timing and token format.</param>
        /// <param name="activityFormat">The localized steps, tools, errors, context, and status format.</param>
        /// <returns>The details text.</returns>
        public string BuildDetails(string? timingFormat, string? activityFormat)
        {
            string timing = SafeFormat(timingFormat, DefaultTimingFormat, TtftMs ?? 0, StreamingMs, FormatMs(TotalMs), InputTokens, OutputTokens, TotalTokens);
            string activity = SafeFormat(activityFormat, DefaultActivityFormat, Steps, ToolCalls, Errors, ContextTokens, string.IsNullOrWhiteSpace(Status) ? "unknown" : Status);
            return timing + "\n" + activity;
        }

        /// <summary>
        /// Formats a duration as <c>450 ms</c> under a second, otherwise <c>1.2 s</c>.
        /// </summary>
        /// <param name="milliseconds">The duration in milliseconds.</param>
        /// <returns>The formatted duration.</returns>
        public static string FormatMs(long milliseconds)
        {
            if (milliseconds < 1000)
            {
                return Math.Max(0, milliseconds).ToString(CultureInfo.InvariantCulture) + " ms";
            }

            return (milliseconds / 1000.0).ToString("0.0", CultureInfo.InvariantCulture) + " s";
        }

        #endregion

        #region Private-Members

        private const string DefaultTimingFormat = "Time to first token: {0} ms\nStreaming: {1} ms\nTotal: {2}\nTokens: input {3} · output {4} · total {5}";
        private const string DefaultActivityFormat = "Steps: {0} · tool calls: {1} · errors: {2}\nContext: ~{3} tokens · status: {4}";

        #endregion

        #region Private-Methods

        private static string SafeFormat(string? format, string fallback, params object[] values)
        {
            if (!string.IsNullOrWhiteSpace(format))
            {
                try
                {
                    return string.Format(CultureInfo.CurrentCulture, format, values);
                }
                catch (FormatException)
                {
                }
            }

            return string.Format(CultureInfo.InvariantCulture, fallback, values);
        }

        #endregion
    }
}
