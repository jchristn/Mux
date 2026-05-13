namespace Test.Xunit.Tools
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using global::Xunit;
    using Mux.Core.Enums;
    using Mux.Core.Models;
    using Mux.Core.Tools;
    using Test.Shared;

    /// <summary>
    /// Unit tests for the <see cref="McpToolManager"/> class.
    /// </summary>
    public class McpToolManagerTests : IDisposable
    {
        #region Private-Members

        private readonly McpToolManager _Manager;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new test instance with an empty McpToolManager.
        /// </summary>
        public McpToolManagerTests()
        {
            _Manager = new McpToolManager(new List<McpServerConfig>());
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Verifies that HasTool returns false for an unknown tool name.
        /// </summary>
        [Fact]
        public void HasTool_UnknownTool_ReturnsFalse()
        {
            bool result = _Manager.HasTool("nonexistent.tool");
            Assert.False(result);
        }

        /// <summary>
        /// Verifies that GetToolDefinitions returns an empty list when no servers are configured.
        /// </summary>
        [Fact]
        public void GetToolDefinitions_EmptyManager_ReturnsEmpty()
        {
            List<ToolDefinition> definitions = _Manager.GetToolDefinitions();
            Assert.NotNull(definitions);
            Assert.Empty(definitions);
        }

        /// <summary>
        /// Verifies that GetServerStatus returns an empty list when no servers are configured.
        /// </summary>
        [Fact]
        public void GetServerStatus_EmptyManager_ReturnsEmpty()
        {
            List<(string Name, int ToolCount, bool Connected)> status = _Manager.GetServerStatus();
            Assert.NotNull(status);
            Assert.Empty(status);
        }

        /// <summary>
        /// Verifies that HTTP MCP servers can be connected, discovered, and executed.
        /// </summary>
        [Fact]
        public async Task AddServerAsync_HttpServer_DiscoversAndExecutesTool()
        {
            using TestMcpHttpServer server = new TestMcpHttpServer();
            await server.StartAsync();

            McpServerConfig config = new McpServerConfig
            {
                Name = "http-test",
                Transport = McpTransportTypeEnum.Http,
                Url = server.BaseUrl,
                McpPath = server.McpPath
            };

            await _Manager.AddServerAsync(config, CancellationToken.None);

            List<ToolDefinition> definitions = _Manager.GetToolDefinitions();
            Assert.Contains(definitions, tool => string.Equals(tool.Name, "http-test.echo", StringComparison.Ordinal));

            List<(string Name, int ToolCount, bool Connected)> status = _Manager.GetServerStatus();
            Assert.Contains(status, serverStatus => string.Equals(serverStatus.Name, "http-test", StringComparison.OrdinalIgnoreCase) &&
                serverStatus.Connected &&
                serverStatus.ToolCount >= 1);

            using JsonDocument arguments = JsonDocument.Parse("{\"text\":\"hello over http\"}");
            ToolResult result = await _Manager.ExecuteAsync("call-1", "http-test.echo", arguments.RootElement, CancellationToken.None);

            Assert.True(result.Success);
            Assert.Contains("hello over http", result.Content, StringComparison.Ordinal);
        }

        /// <summary>
        /// Disposes the test manager.
        /// </summary>
        public void Dispose()
        {
            _Manager.Dispose();
        }

        #endregion
    }
}
