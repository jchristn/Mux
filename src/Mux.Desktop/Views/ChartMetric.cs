namespace Mux.Desktop.Views
{
    /// <summary>
    /// The chart shown by the usage window, matching the mux serve dashboard's chart tabs.
    /// </summary>
    public enum ChartMetric
    {
        /// <summary>Stacked succeeded/failed calls per bucket, with the failure reasons.</summary>
        Calls,

        /// <summary>Stacked uncached-input/cached/output tokens per bucket.</summary>
        Tokens,

        /// <summary>Tokens per bucket stacked by the selected dimension (top six plus Other).</summary>
        TokensBy,

        /// <summary>Token activity intensity per time bucket.</summary>
        Heatmap,

        /// <summary>Derived cost (USD) per bucket.</summary>
        Cost,

        /// <summary>Total latency distribution (ms) per bucket.</summary>
        Latency,

        /// <summary>Time-to-first-token distribution (ms) per bucket.</summary>
        Ttft,

        /// <summary>Streaming duration distribution (ms) per bucket.</summary>
        Streaming,

        /// <summary>Throughput distribution (tokens/sec) per bucket.</summary>
        Throughput,

        /// <summary>Whole-range tokens, cost, and calls per value of the selected dimension.</summary>
        UsageBy
    }
}
