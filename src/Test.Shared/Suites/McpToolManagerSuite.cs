namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Enums;
    using Mux.Core.Models;
    using Mux.Core.Tools;
    using Touchstone.Core;

    /// <summary>
    /// Touchstone suite for <see cref="McpToolManager"/>. Ported from the <c>McpToolManagerTests</c>
    /// xUnit suite. The connectivity case uses an in-process <see cref="TestMcpHttpServer"/>.
    /// </summary>
    public static class McpToolManagerSuite
    {
        /// <summary>
        /// Builds the MCP-tool-manager suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/> for the MCP-tool-manager cases.</returns>
        public static TestSuiteDescriptor Create()
        {
            return new TestSuiteDescriptor(
                "McpToolManager",
                "MCP tool manager behavior",
                new List<TestCaseDescriptor>
                {
                    new TestCaseDescriptor(
                        "McpToolManager",
                        "HasToolUnknownToolReturnsFalse",
                        "HasTool returns false for an unknown tool name",
                        (CancellationToken ct) =>
                        {
                            using (McpToolManager manager = new McpToolManager(new List<McpServerConfig>()))
                            {
                                MuxAssert.IsFalse(manager.HasTool("nonexistent.tool"), "unknown tool");
                            }
                            return Task.CompletedTask;
                        }),

                    new TestCaseDescriptor(
                        "McpToolManager",
                        "GetToolDefinitionsEmptyManagerReturnsEmpty",
                        "GetToolDefinitions returns an empty list when no servers are configured",
                        (CancellationToken ct) =>
                        {
                            using (McpToolManager manager = new McpToolManager(new List<McpServerConfig>()))
                            {
                                List<ToolDefinition> definitions = manager.GetToolDefinitions();
                                MuxAssert.IsNotNull(definitions, "definitions not null");
                                MuxAssert.AreEqual(0, definitions.Count, "definitions empty");
                            }
                            return Task.CompletedTask;
                        }),

                    new TestCaseDescriptor(
                        "McpToolManager",
                        "GetServerStatusEmptyManagerReturnsEmpty",
                        "GetServerStatus returns an empty list when no servers are configured",
                        (CancellationToken ct) =>
                        {
                            using (McpToolManager manager = new McpToolManager(new List<McpServerConfig>()))
                            {
                                List<McpServerStatus> status = manager.GetServerStatus();
                                MuxAssert.IsNotNull(status, "status not null");
                                MuxAssert.AreEqual(0, status.Count, "status empty");
                            }
                            return Task.CompletedTask;
                        }),

                    new TestCaseDescriptor(
                        "McpToolManager",
                        "AddServerAsyncHttpServerDiscoversAndExecutesTool",
                        "HTTP MCP servers can be connected, discovered, and executed",
                        async (CancellationToken ct) =>
                        {
                            using (McpToolManager manager = new McpToolManager(new List<McpServerConfig>()))
                            using (TestMcpHttpServer server = new TestMcpHttpServer())
                            {
                                await server.StartAsync().ConfigureAwait(false);

                                McpServerConfig config = new McpServerConfig
                                {
                                    Name = "http-test",
                                    Transport = McpTransportTypeEnum.Http,
                                    Url = server.BaseUrl,
                                    McpPath = server.McpPath
                                };

                                await manager.AddServerAsync(config, ct).ConfigureAwait(false);

                                List<ToolDefinition> definitions = manager.GetToolDefinitions();
                                MuxAssert.IsTrue(definitions.Any(tool => string.Equals(tool.Name, "http-test.echo", StringComparison.Ordinal)), "echo tool discovered");

                                List<McpServerStatus> status = manager.GetServerStatus();
                                MuxAssert.IsTrue(
                                    status.Any(s => string.Equals(s.Name, "http-test", StringComparison.OrdinalIgnoreCase) && s.Connected && s.ToolCount >= 1),
                                    "server connected with tools");

                                using JsonDocument arguments = JsonDocument.Parse("{\"text\":\"hello over http\"}");
                                ToolResult result = await manager.ExecuteAsync("call-1", "http-test.echo", arguments.RootElement, ct).ConfigureAwait(false);

                                MuxAssert.IsTrue(result.Success, "execute success");
                                MuxAssert.Contains("hello over http", result.Content, "echo content");

                                List<McpConnectionResult> results = manager.GetConnectionResults();
                                McpConnectionResult? connectionResult = results.FirstOrDefault(r => string.Equals(r.Name, "http-test", StringComparison.OrdinalIgnoreCase));
                                MuxAssert.IsNotNull(connectionResult, "connection result recorded");
                                MuxAssert.IsTrue(connectionResult!.Connected, "connection result connected");
                                MuxAssert.IsTrue(connectionResult.ToolCount >= 1, "connection result tool count");
                                MuxAssert.AreEqual("http", connectionResult.Method, "connection result method");
                                MuxAssert.IsNull(connectionResult.Error, "connection result no error");
                            }
                        }),

                    new TestCaseDescriptor(
                        "McpToolManager",
                        "InitializeAsyncRecordsFailureForUnreachableServer",
                        "InitializeAsync records a failure result (with transport and error) for a server that cannot connect",
                        async (CancellationToken ct) =>
                        {
                            McpServerConfig config = new McpServerConfig
                            {
                                Name = "broken-stdio",
                                Transport = McpTransportTypeEnum.Stdio,
                                Command = "mux-nonexistent-command-8f3a2c1e",
                                Args = new List<string>()
                            };

                            using (McpToolManager manager = new McpToolManager(new List<McpServerConfig> { config }))
                            {
                                await manager.InitializeAsync(ct).ConfigureAwait(false);

                                List<McpConnectionResult> results = manager.GetConnectionResults();
                                McpConnectionResult? failure = results.FirstOrDefault(r => string.Equals(r.Name, "broken-stdio", StringComparison.OrdinalIgnoreCase));
                                MuxAssert.IsNotNull(failure, "failure result recorded");
                                MuxAssert.IsFalse(failure!.Connected, "failure result not connected");
                                MuxAssert.AreEqual("stdio", failure.Method, "failure result method");
                                MuxAssert.IsFalse(string.IsNullOrEmpty(failure.Error), "failure result has error details");
                            }
                        })
                });
        }
    }
}
