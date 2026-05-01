namespace Mux.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Mux.Core.Enums;

    /// <summary>
    /// Defines an MCP (Model Context Protocol) server configuration.
    /// </summary>
    public class McpServerConfig
    {
        #region Private-Members

        private string _Name = string.Empty;
        private McpTransportTypeEnum _Transport = McpTransportTypeEnum.Stdio;
        private string _Command = string.Empty;
        private List<string> _Args = new List<string>();
        private Dictionary<string, string> _Env = new Dictionary<string, string>();
        private string _Url = string.Empty;
        private string _McpPath = "/mcp";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="McpServerConfig"/> class with default values.
        /// </summary>
        public McpServerConfig()
        {
        }

        #endregion

        #region Public-Members

        /// <summary>
        /// The unique name of this MCP server.
        /// </summary>
        [JsonPropertyName("name")]
        public string Name
        {
            get => _Name;
            set => _Name = value ?? throw new ArgumentNullException(nameof(Name));
        }

        /// <summary>
        /// The transport used to connect to this MCP server.
        /// </summary>
        [JsonPropertyName("transport")]
        public McpTransportTypeEnum Transport
        {
            get => _Transport;
            set => _Transport = value;
        }

        /// <summary>
        /// The command used to start the MCP server process.
        /// </summary>
        [JsonPropertyName("command")]
        public string Command
        {
            get => _Command;
            set => _Command = value ?? throw new ArgumentNullException(nameof(Command));
        }

        /// <summary>
        /// The command-line arguments to pass to the MCP server process.
        /// </summary>
        [JsonPropertyName("args")]
        public List<string> Args
        {
            get => _Args;
            set => _Args = value ?? new List<string>();
        }

        /// <summary>
        /// Environment variables to set for the MCP server process.
        /// </summary>
        [JsonPropertyName("env")]
        public Dictionary<string, string> Env
        {
            get => _Env;
            set => _Env = value ?? new Dictionary<string, string>();
        }

        /// <summary>
        /// The base URL used for HTTP MCP servers.
        /// </summary>
        [JsonPropertyName("url")]
        public string Url
        {
            get => _Url;
            set => _Url = value ?? throw new ArgumentNullException(nameof(Url));
        }

        /// <summary>
        /// The HTTP MCP path used for streamable HTTP servers.
        /// </summary>
        [JsonPropertyName("mcpPath")]
        public string McpPath
        {
            get => _McpPath;
            set => _McpPath = string.IsNullOrWhiteSpace(value) ? "/mcp" : value;
        }

        #endregion
    }
}
