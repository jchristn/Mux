namespace Mux.Core.Enums
{
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Type of LLM backend adapter.
    /// </summary>
    [JsonConverter(typeof(AdapterTypeEnumConverter))]
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
        OpenAiCompatible,

        /// <summary>
        /// Anthropic Claude Messages API (native). Authenticates with an API key sent as <c>x-api-key</c>.
        /// </summary>
        [EnumMember(Value = "anthropic")]
        Anthropic,

        /// <summary>
        /// Google Gemini (AI Studio / generativelanguage) API. Authenticates with an API key.
        /// </summary>
        [EnumMember(Value = "gemini")]
        Gemini,

        /// <summary>
        /// Azure OpenAI. The model is the deployment name; authenticates with an <c>api-key</c> header.
        /// </summary>
        [EnumMember(Value = "azure_openai")]
        AzureOpenAi,

        /// <summary>
        /// Google Vertex AI (Gemini on Vertex). Requires a project and region; credentials come from
        /// Application Default Credentials.
        /// </summary>
        [EnumMember(Value = "vertex")]
        Vertex,

        /// <summary>
        /// AWS Bedrock (unified Converse API). Requires a region; credentials come from the AWS environment.
        /// </summary>
        [EnumMember(Value = "bedrock")]
        Bedrock
    }
}
