namespace Mux.Desktop.Views
{
    /// <summary>
    /// The metric shown by the usage timeseries chart, matching the mux serve dashboard's chart tabs.
    /// </summary>
    public enum ChartMetric
    {
        /// <summary>Stacked prompt/cached/output tokens per bucket.</summary>
        Tokens,

        /// <summary>Derived cost (USD) per bucket.</summary>
        Cost,

        /// <summary>Average total latency (ms) per bucket.</summary>
        Latency,

        /// <summary>Average time-to-first-token (ms) per bucket.</summary>
        Ttft,

        /// <summary>Average streaming duration (ms) per bucket.</summary>
        Streaming,

        /// <summary>Average throughput (tokens/sec) per bucket.</summary>
        Throughput
    }
}
