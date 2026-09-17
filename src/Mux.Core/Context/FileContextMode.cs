namespace Mux.Core.Context
{
    /// <summary>
    /// How a file that exceeds the inline size threshold is turned into model context.
    /// </summary>
    public enum FileContextMode
    {
        /// <summary>Emit a structural outline with line ranges plus the first lines. Instant, free, lossless.</summary>
        Map,

        /// <summary>Emit an iterative map-reduce summary that still carries line-range pointers. Needs a model call.</summary>
        Summarize,

        /// <summary>Preserve the strict behavior: a head slice, or (on the agent tool path) the hard refusal.</summary>
        Truncate
    }
}
