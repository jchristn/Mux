namespace Mux.Core.Enums
{
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Determines how an endpoint's API key is presented to the upstream service. Applies to the OpenAI-family
    /// HTTP adapters (<c>openai</c>, <c>openai-compatible</c>, <c>vllm</c>, <c>ollama</c>), whose credential
    /// placement varies across providers. Native adapters that carry a fixed scheme (<c>anthropic</c>,
    /// <c>gemini</c>, <c>azure-openai</c>, <c>vertex</c>, <c>bedrock</c>) ignore this and authenticate their own way.
    /// </summary>
    [JsonConverter(typeof(AuthPlacementEnumConverter))]
    public enum AuthPlacementEnum
    {
        /// <summary>
        /// Send the key as an <c>Authorization: Bearer &lt;key&gt;</c> header (the default).
        /// </summary>
        [EnumMember(Value = "bearer")]
        Bearer,

        /// <summary>
        /// Send the key as the value of a caller-named request header (see
        /// <see cref="Mux.Core.Models.EndpointConfig.AuthParameterName"/>).
        /// </summary>
        [EnumMember(Value = "header")]
        Header,

        /// <summary>
        /// Append the key as a caller-named query-string parameter on every request URI (see
        /// <see cref="Mux.Core.Models.EndpointConfig.AuthParameterName"/>).
        /// </summary>
        [EnumMember(Value = "query")]
        Query
    }
}
