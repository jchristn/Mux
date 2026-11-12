namespace Mux.Core.Telemetry
{
    /// <summary>
    /// How an x-axis label of the token heatmap sits over the columns it names (see
    /// <see cref="UsageHeatmapLabel"/>).
    /// </summary>
    public enum UsageHeatmapLabelAlignEnum
    {
        /// <summary>The text starts at the left edge of its first column (an ungrouped label with room).</summary>
        Start,

        /// <summary>The text is centred over its columns (a grouped label, such as a weekday over AM and PM).</summary>
        Center,

        /// <summary>The text ends at the right edge of its column (an ungrouped label that would overrun the grid).</summary>
        End
    }
}
