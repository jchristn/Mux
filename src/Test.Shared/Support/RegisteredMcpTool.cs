namespace Test.Shared.Support
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Voltaic.Core;

    /// <summary>
    /// One tool captured from <c>MuxMcpTools.RegisterAll</c>: its description, input schema, and handler, so tests
    /// can invoke the handler directly without an MCP transport.
    /// </summary>
    public sealed class RegisteredMcpTool
    {
        #region Public-Members

        /// <summary>The tool description.</summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>The input schema object.</summary>
        public object Schema { get; set; } = new object();

        /// <summary>The handler.</summary>
        public Func<RpcParameters, CancellationToken, Task<object>> Handler { get; set; } = (RpcParameters p, CancellationToken c) => Task.FromResult<object>(string.Empty);

        #endregion
    }
}
