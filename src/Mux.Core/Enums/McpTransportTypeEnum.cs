namespace Mux.Core.Enums
{
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Supported MCP transport types.
    /// </summary>
    [JsonConverter(typeof(McpTransportTypeEnumConverter))]
    public enum McpTransportTypeEnum
    {
        /// <summary>
        /// Launch a local subprocess and communicate over standard input/output.
        /// </summary>
        [EnumMember(Value = "stdio")]
        Stdio,

        /// <summary>
        /// Connect to a remote MCP server over HTTP.
        /// </summary>
        [EnumMember(Value = "http")]
        Http
    }
}
