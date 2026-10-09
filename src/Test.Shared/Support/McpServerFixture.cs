namespace Test.Shared.Support
{
    using System.Collections.Generic;
    using Mux.Core.Models;
    using Mux.Core.Sessions;

    /// <summary>
    /// Everything an MCP server test case needs: a temp root, the fake executor, mutable endpoints, the session store,
    /// and the running server.
    /// </summary>
    public sealed class McpServerFixture
    {
        /// <summary>The temp directory (also the default working directory).</summary>
        public string Root { get; set; } = string.Empty;

        /// <summary>The scripted run executor.</summary>
        public FakeMcpRunExecutor Executor { get; } = new FakeMcpRunExecutor();

        /// <summary>The endpoints <c>list_endpoints</c> reports; tests may add to it.</summary>
        public List<EndpointConfig> Endpoints { get; } = new List<EndpointConfig>();

        /// <summary>The session store the session tools read.</summary>
        public SessionStore Sessions { get; set; } = null!;

        /// <summary>The running server.</summary>
        public McpTestServer Server { get; set; } = null!;
    }
}
