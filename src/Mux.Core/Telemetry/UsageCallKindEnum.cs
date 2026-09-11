namespace Mux.Core.Telemetry
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Classifies the origin of an LLM call recorded in usage telemetry, so spend and latency can be
    /// attributed to the kind of work that produced them.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum UsageCallKindEnum
    {
        /// <summary>
        /// A call made by the primary agent loop of an interactive or headless run.
        /// </summary>
        Primary,

        /// <summary>
        /// A call made by the history-compaction sidecar (summarization of prior turns).
        /// </summary>
        Compaction,

        /// <summary>
        /// A call made inside a delegated subagent's isolated loop.
        /// </summary>
        Subagent,

        /// <summary>
        /// A call made by the REST server's tool-free chat endpoint.
        /// </summary>
        Chat,

        /// <summary>
        /// A call made by a model-load probe (for example <c>mux probe</c>).
        /// </summary>
        Probe
    }
}
