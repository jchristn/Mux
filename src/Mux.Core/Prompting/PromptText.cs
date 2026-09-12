namespace Mux.Core.Prompting
{
    /// <summary>
    /// Small shared helpers for turning a (possibly multi-line) prompt into a short, single-line label —
    /// used for the TUI's pending-queue preview and both front ends' git-checkpoint labels. Kept in Core so
    /// the "flatten and cap a prompt" logic is defined once rather than copied per front end.
    /// </summary>
    public static class PromptText
    {
        /// <summary>
        /// Flattens a prompt onto one line (newlines and carriage returns become spaces) and trims it.
        /// </summary>
        /// <param name="prompt">The prompt; null is treated as empty.</param>
        /// <returns>The single-line, trimmed text (never null).</returns>
        public static string Flatten(string? prompt)
        {
            return (prompt ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Trim();
        }

        /// <summary>
        /// Produces a single-line preview of a prompt, capped to <paramref name="maxLength"/> characters with
        /// a trailing ellipsis when it overflows. The prompt is flattened and trimmed first.
        /// </summary>
        /// <param name="prompt">The prompt; null is treated as empty.</param>
        /// <param name="maxLength">The maximum visible length before eliding (floored at 1).</param>
        /// <returns>The preview text (never null; empty when the prompt is blank).</returns>
        public static string Preview(string? prompt, int maxLength)
        {
            string flattened = Flatten(prompt);
            int max = maxLength < 1 ? 1 : maxLength;
            return flattened.Length <= max ? flattened : flattened.Substring(0, max - 1) + "…";
        }
    }
}
