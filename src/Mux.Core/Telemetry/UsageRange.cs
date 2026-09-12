namespace Mux.Core.Telemetry
{
    /// <summary>
    /// Selectable time ranges for the usage analytics surface. Each range maps to a fixed window and bucket
    /// granularity (see <see cref="UsageWindow"/>) shared by the desktop analytics view and the mux serve
    /// dashboard so native charts match the web charts exactly.
    /// </summary>
    public enum UsageRange
    {
        /// <summary>The last hour, in sixty one-minute buckets.</summary>
        Hour,

        /// <summary>The last day, in ninety-six fifteen-minute buckets.</summary>
        Day,

        /// <summary>The last week, in eighty-four two-hour buckets.</summary>
        Week,

        /// <summary>The last month, in sixty twelve-hour buckets.</summary>
        Month
    }
}
