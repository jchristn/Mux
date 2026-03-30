namespace Mux.Core.Enums
{
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Type of LLM backend adapter.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum AdapterTypeEnum
    {
        /// <summary>
        /// Ollama local inference server.
        /// </summary>
        [EnumMember(Value = "ollama")]
        Ollama,

        /// <summary>
        /// OpenAI API.
        /// </summary>
        [EnumMember(Value = "openai")]
        OpenAi,

        /// <summary>
        /// vLLM inference server.
        /// </summary>
        [EnumMember(Value = "vllm")]
        Vllm,

        /// <summary>
        /// Generic OpenAI-compatible API.
        /// </summary>
        [EnumMember(Value = "openai_compatible")]
        OpenAiCompatible
    }
}
