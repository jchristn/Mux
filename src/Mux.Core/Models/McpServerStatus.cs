namespace Mux.Core.Models
{
    /// <summary>
    /// Connection status for a configured MCP server.
    /// </summary>
    public class McpServerStatus
    {
        #region Private-Members

        private string _Name = string.Empty;
        private int _ToolCount = 0;
        private bool _Connected = false;

        #endregion

        #region Public-Members

        /// <summary>
        /// The configured MCP server name.
        /// </summary>
        public string Name
        {
            get => _Name;
            set => _Name = value ?? string.Empty;
        }

        /// <summary>
        /// The number of tools discovered from the MCP server.
        /// </summary>
        public int ToolCount
        {
            get => _ToolCount;
            set => _ToolCount = value;
        }

        /// <summary>
        /// Whether the MCP server is currently connected.
        /// </summary>
        public bool Connected
        {
            get => _Connected;
            set => _Connected = value;
        }

        #endregion
    }
}
