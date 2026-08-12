namespace Mux.Core.Models
{
    /// <summary>
    /// The outcome of the most recent attempt to connect to a configured MCP server and discover its tools.
    /// Unlike <see cref="McpServerStatus"/>, which reports live connectivity, this captures the result of a
    /// single connect attempt — including the transport used and, on failure, the error details — so the shell
    /// can surface a clear success or failure notice to the user.
    /// </summary>
    public class McpConnectionResult
    {
        #region Private-Members

        private string _Name = string.Empty;
        private int _ToolCount = 0;
        private bool _Connected = false;
        private string _Method = string.Empty;
        private string? _Error;

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
        /// The number of tools discovered when the connection succeeded; zero on failure.
        /// </summary>
        public int ToolCount
        {
            get => _ToolCount;
            set => _ToolCount = value;
        }

        /// <summary>
        /// Whether the connect attempt succeeded.
        /// </summary>
        public bool Connected
        {
            get => _Connected;
            set => _Connected = value;
        }

        /// <summary>
        /// The transport used for the attempt (for example "stdio" or "http").
        /// </summary>
        public string Method
        {
            get => _Method;
            set => _Method = value ?? string.Empty;
        }

        /// <summary>
        /// The failure details when <see cref="Connected"/> is false; null on success.
        /// </summary>
        public string? Error
        {
            get => _Error;
            set => _Error = value;
        }

        #endregion
    }
}
