namespace Mux.Core.Models
{
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Per-backend behavioral flags that control adapter behavior.
    /// </summary>
    public class BackendQuirks
    {
        #region Private-Members

        private bool _AssembleToolCallDeltas = true;
        private bool _SupportsParallelToolCalls = false;
        private bool _SupportsTools = true;
        private bool _EnableMalformedToolCallRecovery = true;
        private bool _RequiresToolResultContentAsString = false;
        private string _DefaultFinishReason = "stop";
        private List<string> _StripRequestFields = new List<string>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="BackendQuirks"/> class with default values.
        /// </summary>
        public BackendQuirks()
        {
        }

        #endregion

        #region Public-Members

        /// <summary>
        /// Whether to assemble streamed tool-call deltas into complete tool calls.
        /// </summary>
        [JsonPropertyName("assembleToolCallDeltas")]
        public bool AssembleToolCallDeltas
        {
            get => _AssembleToolCallDeltas;
            set => _AssembleToolCallDeltas = value;
        }

        /// <summary>
        /// Whether the backend supports parallel tool calls in a single response.
        /// </summary>
        [JsonPropertyName("supportsParallelToolCalls")]
        public bool SupportsParallelToolCalls
        {
            get => _SupportsParallelToolCalls;
            set => _SupportsParallelToolCalls = value;
        }

        /// <summary>
        /// Whether the model supports tool calling. When false, tools are omitted from requests.
        /// </summary>
        [JsonPropertyName("supportsTools")]
        public bool SupportsTools
        {
            get => _SupportsTools;
            set => _SupportsTools = value;
        }

        /// <summary>
        /// Whether mux should try to infer tool calls from malformed freeform assistant text.
        /// </summary>
        [JsonPropertyName("enableMalformedToolCallRecovery")]
        public bool EnableMalformedToolCallRecovery
        {
            get => _EnableMalformedToolCallRecovery;
            set => _EnableMalformedToolCallRecovery = value;
        }

        /// <summary>
        /// Whether tool result content must be serialized as a plain string.
        /// </summary>
        [JsonPropertyName("requiresToolResultContentAsString")]
        public bool RequiresToolResultContentAsString
        {
            get => _RequiresToolResultContentAsString;
            set => _RequiresToolResultContentAsString = value;
        }

        /// <summary>
        /// The default finish reason to use when the backend does not provide one.
        /// </summary>
        [JsonPropertyName("defaultFinishReason")]
        public string DefaultFinishReason
        {
            get => _DefaultFinishReason;
            set => _DefaultFinishReason = value ?? "stop";
        }

        /// <summary>
        /// A list of request field names to strip before sending to the backend.
        /// </summary>
        [JsonPropertyName("stripRequestFields")]
        public List<string> StripRequestFields
        {
            get => _StripRequestFields;
            set => _StripRequestFields = value ?? new List<string>();
        }

        #endregion
    }
}
