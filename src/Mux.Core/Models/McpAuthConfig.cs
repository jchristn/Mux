namespace Mux.Core.Models
{
    using System.Text.Json.Serialization;
    using Mux.Core.Enums;

    /// <summary>
    /// Defines the authentication used when connecting to an HTTP MCP server. Ignored for stdio servers.
    /// </summary>
    public class McpAuthConfig
    {
        #region Private-Members

        private McpAuthTypeEnum _Type = McpAuthTypeEnum.None;
        private string _BearerToken = string.Empty;
        private string _ApiKeyHeader = "X-API-Key";
        private string _ApiKeyValue = string.Empty;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="McpAuthConfig"/> class with default values.
        /// </summary>
        public McpAuthConfig()
        {
        }

        #endregion

        #region Public-Members

        /// <summary>
        /// The authentication scheme to use. Defaults to <see cref="McpAuthTypeEnum.None"/>.
        /// </summary>
        [JsonPropertyName("type")]
        public McpAuthTypeEnum Type
        {
            get => _Type;
            set => _Type = value;
        }

        /// <summary>
        /// The bearer token sent as an <c>Authorization: Bearer &lt;token&gt;</c> header when
        /// <see cref="Type"/> is <see cref="McpAuthTypeEnum.Bearer"/>.
        /// </summary>
        [JsonPropertyName("bearerToken")]
        public string BearerToken
        {
            get => _BearerToken;
            set => _BearerToken = value ?? string.Empty;
        }

        /// <summary>
        /// The header name carrying the API key when <see cref="Type"/> is
        /// <see cref="McpAuthTypeEnum.ApiKey"/>. Defaults to <c>X-API-Key</c>.
        /// </summary>
        [JsonPropertyName("apiKeyHeader")]
        public string ApiKeyHeader
        {
            get => _ApiKeyHeader;
            set => _ApiKeyHeader = string.IsNullOrWhiteSpace(value) ? "X-API-Key" : value;
        }

        /// <summary>
        /// The API key value sent in the <see cref="ApiKeyHeader"/> header when <see cref="Type"/> is
        /// <see cref="McpAuthTypeEnum.ApiKey"/>.
        /// </summary>
        [JsonPropertyName("apiKeyValue")]
        public string ApiKeyValue
        {
            get => _ApiKeyValue;
            set => _ApiKeyValue = value ?? string.Empty;
        }

        #endregion
    }
}
