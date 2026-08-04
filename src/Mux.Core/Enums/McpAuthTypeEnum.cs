namespace Mux.Core.Enums
{
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Supported authentication schemes for HTTP MCP servers.
    /// </summary>
    [JsonConverter(typeof(McpAuthTypeEnumConverter))]
    public enum McpAuthTypeEnum
    {
        /// <summary>
        /// No authentication is sent with requests.
        /// </summary>
        [EnumMember(Value = "none")]
        None,

        /// <summary>
        /// Send an <c>Authorization: Bearer &lt;token&gt;</c> header with every request.
        /// </summary>
        [EnumMember(Value = "bearer")]
        Bearer,

        /// <summary>
        /// Send an API key as a caller-specified header with every request.
        /// </summary>
        [EnumMember(Value = "apikey")]
        ApiKey
    }
}
