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
                                MuxAssert.AreEqual("hello over http", FirstText(result.Content), "echo content is a single unwrapped text block");

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
                        }),

                    new TestCaseDescriptor(
                        "McpToolManager",
                        "ExecuteAsyncUnknownToolReturnsErrorResult",
                        "ExecuteAsync returns a failed ToolResult (not an exception) when the tool name is not registered",
                        async (CancellationToken ct) =>
                        {
                            using (McpToolManager manager = new McpToolManager(new List<McpServerConfig>()))
                            {
                                using JsonDocument arguments = JsonDocument.Parse("{}");
                                ToolResult result = await manager.ExecuteAsync("call-x", "ghost.tool", arguments.RootElement, ct).ConfigureAwait(false);

                                MuxAssert.IsNotNull(result, "result not null");
                                MuxAssert.AreEqual("call-x", result.ToolCallId, "tool call id echoed");
                                MuxAssert.IsFalse(result.Success, "execute reports failure");
                                MuxAssert.Contains("unknown_mcp_tool", result.Content, "unknown-tool error code");
                            }
                        }),

                    new TestCaseDescriptor(
                        "McpToolManager",
                        "AddServerAsyncInvalidHttpUrlThrows",
                        "AddServerAsync rejects an HTTP server whose URL is not an absolute http(s) URL",
                        async (CancellationToken ct) =>
                        {
                            using (McpToolManager manager = new McpToolManager(new List<McpServerConfig>()))
                            {
                                McpServerConfig config = new McpServerConfig
                                {
                                    Name = "bad-url",
                                    Transport = McpTransportTypeEnum.Http,
                                    Url = "not-an-absolute-url"
                                };

                                await MuxAssert.ThrowsAsync<InvalidOperationException>(
                                    () => manager.AddServerAsync(config, ct),
                                    "invalid http url rejected").ConfigureAwait(false);

                                MuxAssert.IsFalse(manager.GetServerStatus().Any(s => string.Equals(s.Name, "bad-url", StringComparison.OrdinalIgnoreCase)), "no status recorded for rejected server");
                            }
                        }),

                    new TestCaseDescriptor(
                        "McpToolManager",
                        "AddServerAsyncDuplicateNameThrows",
                        "AddServerAsync rejects a second server registered under an already-used name",
                        async (CancellationToken ct) =>
                        {
                            using (McpToolManager manager = new McpToolManager(new List<McpServerConfig>()))
                            using (TestMcpHttpServer server = new TestMcpHttpServer())
                            {
                                await server.StartAsync().ConfigureAwait(false);

                                McpServerConfig config = new McpServerConfig
                                {
                                    Name = "dupe",
                                    Transport = McpTransportTypeEnum.Http,
                                    Url = server.BaseUrl,
                                    McpPath = server.McpPath
                                };

                                await manager.AddServerAsync(config, ct).ConfigureAwait(false);

                                await MuxAssert.ThrowsAsync<InvalidOperationException>(
                                    () => manager.AddServerAsync(config, ct),
                                    "duplicate server name rejected").ConfigureAwait(false);
                            }
                        }),

                    new TestCaseDescriptor(
                        "McpToolManager",
                        "RemoveServerAsyncClearsToolsAndStatus",
                        "RemoveServerAsync disconnects a server and clears its tools and status",
                        async (CancellationToken ct) =>
                        {
                            using (McpToolManager manager = new McpToolManager(new List<McpServerConfig>()))
                            using (TestMcpHttpServer server = new TestMcpHttpServer())
                            {
                                await server.StartAsync().ConfigureAwait(false);

                                McpServerConfig config = new McpServerConfig
                                {
                                    Name = "removable",
                                    Transport = McpTransportTypeEnum.Http,
                                    Url = server.BaseUrl,
                                    McpPath = server.McpPath
                                };

                                await manager.AddServerAsync(config, ct).ConfigureAwait(false);
                                MuxAssert.IsTrue(manager.HasTool("removable.echo"), "tool registered before removal");

                                await manager.RemoveServerAsync("removable").ConfigureAwait(false);

                                MuxAssert.IsFalse(manager.HasTool("removable.echo"), "tool unregistered after removal");
                                MuxAssert.IsFalse(manager.GetToolDefinitions().Any(t => t.Name.StartsWith("removable.", StringComparison.Ordinal)), "no definitions remain");
                                MuxAssert.IsFalse(manager.GetServerStatus().Any(s => string.Equals(s.Name, "removable", StringComparison.OrdinalIgnoreCase)), "no status remains");
                            }
                        }),

                    new TestCaseDescriptor(
                        "McpToolManager",
                        "DiscoveryListsOnlyApplicationTools",
                        "Discovery surfaces exactly the server's own tools; Voltaic 2.x publishes no demo tools (ping/echo/getTime/getSessions) by default",
                        async (CancellationToken ct) =>
                        {
                            using (McpToolManager manager = new McpToolManager(new List<McpServerConfig>()))
                            using (TestMcpHttpServer server = new TestMcpHttpServer())
                            {
                                await server.StartAsync().ConfigureAwait(false);
                                await manager.AddServerAsync(HttpConfig("exact", server), ct).ConfigureAwait(false);

                                List<string> names = manager.GetToolDefinitions().Select(t => t.Name).OrderBy(n => n, StringComparer.Ordinal).ToList();
                                List<string> expected = TestMcpHttpServer.ApplicationToolNames.Select(n => "exact." + n).OrderBy(n => n, StringComparer.Ordinal).ToList();
                                MuxAssert.AreEqual(string.Join(",", expected), string.Join(",", names), "only application tools discovered");

                                foreach (string demo in new[] { "ping", "getTime", "getSessions", "getClients" })
                                {
                                    MuxAssert.IsFalse(manager.HasTool("exact." + demo), "demo tool not published: " + demo);
                                }

                                McpConnectionResult? connectionResult = manager.GetConnectionResults().FirstOrDefault(r => string.Equals(r.Name, "exact", StringComparison.OrdinalIgnoreCase));
                                MuxAssert.IsNotNull(connectionResult, "connection result recorded");
                                MuxAssert.AreEqual(TestMcpHttpServer.ApplicationToolNames.Count, connectionResult!.ToolCount, "connection result counts only application tools");
                            }
                        }),

                    new TestCaseDescriptor(
                        "McpToolManager",
                        "DiscoveryIncludesDiagnosticToolsOnlyWhenOptedIn",
                        "A server that opts into Voltaic's diagnostic tools also surfaces getTime, and the application's echo still wins",
                        async (CancellationToken ct) =>
                        {
                            using (McpToolManager manager = new McpToolManager(new List<McpServerConfig>()))
                            using (TestMcpHttpServer server = new TestMcpHttpServer(includeDiagnosticTools: true))
                            {
                                await server.StartAsync().ConfigureAwait(false);
                                await manager.AddServerAsync(HttpConfig("diag", server), ct).ConfigureAwait(false);

                                MuxAssert.IsTrue(manager.HasTool("diag.getTime"), "opt-in diagnostic tool discovered");
                                MuxAssert.IsFalse(manager.HasTool("diag.ping"), "ping is a protocol method, never a tool");
                                MuxAssert.IsFalse(manager.HasTool("diag.getSessions"), "getSessions removed in Voltaic 2.x");

                                using JsonDocument arguments = JsonDocument.Parse("{\"text\":\"app echo\"}");
                                ToolResult result = await manager.ExecuteAsync("call-d", "diag.echo", arguments.RootElement, ct).ConfigureAwait(false);
                                MuxAssert.IsTrue(result.Success, "echo executes");
                                MuxAssert.Contains("app echo", result.Content, "echo content");
                            }
                        }),

                    new TestCaseDescriptor(
                        "McpToolManager",
                        "ExecuteAsyncStrictSchemaAcceptsDeclaredArguments",
                        "A tool whose schema sets additionalProperties:false succeeds when only declared arguments are sent",
                        async (CancellationToken ct) =>
                        {
                            using (McpToolManager manager = new McpToolManager(new List<McpServerConfig>()))
                            using (TestMcpHttpServer server = new TestMcpHttpServer())
                            {
                                await server.StartAsync().ConfigureAwait(false);
                                await manager.AddServerAsync(HttpConfig("strict", server), ct).ConfigureAwait(false);

                                using JsonDocument arguments = JsonDocument.Parse("{\"text\":\"declared\"}");
                                ToolResult result = await manager.ExecuteAsync("call-s1", "strict.strict_echo", arguments.RootElement, ct).ConfigureAwait(false);

                                MuxAssert.IsTrue(result.Success, "strict tool succeeds with declared args");
                                MuxAssert.AreEqual("strict:declared", FirstText(result.Content), "strict tool content");
                            }
                        }),

                    new TestCaseDescriptor(
                        "McpToolManager",
                        "ExecuteAsyncStrictSchemaRejectsUndeclaredArgument",
                        "Voltaic 2.x enforces additionalProperties:false; an undeclared argument yields a failed ToolResult naming the property",
                        async (CancellationToken ct) =>
                        {
                            using (McpToolManager manager = new McpToolManager(new List<McpServerConfig>()))
                            using (TestMcpHttpServer server = new TestMcpHttpServer())
                            {
                                await server.StartAsync().ConfigureAwait(false);
                                await manager.AddServerAsync(HttpConfig("strict", server), ct).ConfigureAwait(false);

                                using JsonDocument arguments = JsonDocument.Parse("{\"text\":\"declared\",\"bogus\":1}");
                                ToolResult result = await manager.ExecuteAsync("call-s2", "strict.strict_echo", arguments.RootElement, ct).ConfigureAwait(false);

                                MuxAssert.IsFalse(result.Success, "undeclared argument rejected");
                                MuxAssert.Contains("mcp_call_failed", result.Content, "mcp call failure code");
                                MuxAssert.Contains("bogus", result.Content, "error names the undeclared property");
                                MuxAssert.IsFalse(result.Content.Contains("strict:declared", StringComparison.Ordinal), "handler did not run");

                                // The server stays usable after a validation error.
                                using JsonDocument good = JsonDocument.Parse("{\"text\":\"again\"}");
                                ToolResult retry = await manager.ExecuteAsync("call-s3", "strict.strict_echo", good.RootElement, ct).ConfigureAwait(false);
                                MuxAssert.IsTrue(retry.Success, "subsequent valid call succeeds");
                            }
                        }),

                    new TestCaseDescriptor(
                        "McpToolManager",
                        "ExecuteAsyncRemovedDemoToolReturnsUnknownTool",
                        "Calling a v1.x demo tool name (getSessions/ping) against a connected server fails as unknown, not as a live call",
                        async (CancellationToken ct) =>
                        {
                            using (McpToolManager manager = new McpToolManager(new List<McpServerConfig>()))
                            using (TestMcpHttpServer server = new TestMcpHttpServer())
                            {
                                await server.StartAsync().ConfigureAwait(false);
                                await manager.AddServerAsync(HttpConfig("legacy", server), ct).ConfigureAwait(false);

                                using JsonDocument arguments = JsonDocument.Parse("{}");
                                foreach (string demo in new[] { "getSessions", "ping" })
                                {
                                    ToolResult result = await manager.ExecuteAsync("call-" + demo, "legacy." + demo, arguments.RootElement, ct).ConfigureAwait(false);
                                    MuxAssert.IsFalse(result.Success, demo + " not callable");
                                    MuxAssert.Contains("unknown_mcp_tool", result.Content, demo + " reported as unknown tool");
                                }
                            }
                        })
                });
        }

        private static string? FirstText(string toolCallResultJson)
        {
            using JsonDocument document = JsonDocument.Parse(toolCallResultJson);
            JsonElement content = document.RootElement.GetProperty("content");
            MuxAssert.AreEqual(1, content.GetArrayLength(), "single content block");
            MuxAssert.AreEqual("text", content[0].GetProperty("type").GetString(), "text content block");
            return content[0].GetProperty("text").GetString();
        }

        private static McpServerConfig HttpConfig(string name, TestMcpHttpServer server)
        {
            return new McpServerConfig
            {
                Name = name,
                Transport = McpTransportTypeEnum.Http,
                Url = server.BaseUrl,
                McpPath = server.McpPath
            };
        }
    }
}
