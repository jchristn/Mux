namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Enums;
    using Mux.Core.Models;
    using Mux.Core.Tools;
    using Test.Shared.Support;
    using Touchstone.Core;
    using Voltaic.Core;
    using Voltaic.Mcp;
    using ToolDefinition = Mux.Core.Models.ToolDefinition;

    /// <summary>
    /// Touchstone suite for mux as an MCP client over Streamable HTTP, driven against an in-process Voltaic server whose
    /// tools each case defines: discovery and name prefixing, schemas, execution and argument passing, tool errors,
    /// schema enforcement, cancellation, routing across servers with the same tool names, duplicate server names,
    /// runtime add and remove, status, authentication headers (bearer, API key, environment expansion), path and URL
    /// validation, a server that disappears mid-session, pagination, large and Unicode payloads, and concurrency.
    /// </summary>
    public static class McpClientSuite
    {
        #region Private-Members

        private const string SuiteId = "McpClient";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Builds the MCP client suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/> for the MCP client cases.</returns>
        public static TestSuiteDescriptor Create()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();
            void Add(string id, string name, Func<CancellationToken, Task> body)
            {
                cases.Add(new TestCaseDescriptor(SuiteId, id, name, body));
            }

            Add("DiscoversAndPrefixesTools", "Tools are discovered and prefixed with the server name, with descriptions tagged", async (CancellationToken ct) =>
            {
                await using (ScriptableMcpHttpServer server = await ScriptableMcpHttpServer.StartAsync((McpHttpServer s) =>
                {
                    ScriptableMcpHttpServer.Text(s, "alpha", "A");
                    ScriptableMcpHttpServer.Text(s, "beta", "B");
                }).ConfigureAwait(false))
                using (McpToolManager manager = await Connect(ct, Http("srv", server.BaseUrl)).ConfigureAwait(false))
                {
                    List<ToolDefinition> tools = manager.GetToolDefinitions();
                    List<string> names = tools.ConvertAll(t => t.Name);
                    names.Sort(StringComparer.Ordinal);
                    MuxAssert.AreEqual("srv.alpha,srv.beta", string.Join(",", names), "prefixed names");
                    MuxAssert.Contains("[MCP:srv]", tools[0].Description, "description tagged with the server");
                    MuxAssert.IsTrue(manager.HasTool("srv.alpha") && manager.HasTool("SRV.ALPHA"), "lookup is case-insensitive");
                    MuxAssert.IsFalse(manager.HasTool("alpha"), "unprefixed names are not registered");
                    MuxAssert.IsFalse(manager.HasTool("other.alpha"), "another server's prefix is not registered");
                }
            });
            Add("SchemaPassedThrough", "A tool's input schema reaches the model unchanged", async (CancellationToken ct) =>
            {
                await using (ScriptableMcpHttpServer server = await ScriptableMcpHttpServer.StartAsync((McpHttpServer s) =>
                    s.RegisterTool("lookup", "Looks up a user", new { type = "object", properties = new { id = new { type = "integer", minimum = 1 }, verbose = new { type = "boolean" } }, required = new[] { "id" } }, (RpcParameters a) => (object)"ok")).ConfigureAwait(false))
                using (McpToolManager manager = await Connect(ct, Http("db", server.BaseUrl)).ConfigureAwait(false))
                {
                    string schema = JsonSerializer.Serialize(manager.GetToolDefinitions()[0].ParametersSchema);
                    MuxAssert.Contains("\"required\":[\"id\"]", schema, "required kept");
                    MuxAssert.Contains("\"minimum\":1", schema, "constraints kept");
                    MuxAssert.Contains("\"verbose\"", schema, "all properties kept");
                }
            });
            Add("ToolNamesWithDotsAndDashes", "Tool names containing dots and dashes are prefixed once and routed back correctly", async (CancellationToken ct) =>
            {
                await using (ScriptableMcpHttpServer server = await ScriptableMcpHttpServer.StartAsync((McpHttpServer s) =>
                {
                    ScriptableMcpHttpServer.Text(s, "files.read-all", "DOTTED");
                    ScriptableMcpHttpServer.Text(s, "get-thing", "DASHED");
                }).ConfigureAwait(false))
                using (McpToolManager manager = await Connect(ct, Http("srv", server.BaseUrl)).ConfigureAwait(false))
                {
                    MuxAssert.Contains("DOTTED", (await manager.ExecuteAsync("c", "srv.files.read-all", Json("{}"), ct).ConfigureAwait(false)).Content, "dotted name routed with its full original name");
                    MuxAssert.Contains("DASHED", (await manager.ExecuteAsync("c", "srv.get-thing", Json("{}"), ct).ConfigureAwait(false)).Content, "dashed name routed");
                }
            });
            Add("ExecuteReturnsContent", "Executing a tool returns its content and succeeds", async (CancellationToken ct) =>
            {
                await using (ScriptableMcpHttpServer server = await ScriptableMcpHttpServer.StartAsync((McpHttpServer s) => ScriptableMcpHttpServer.Text(s, "hello", "HELLO_WORLD")).ConfigureAwait(false))
                using (McpToolManager manager = await Connect(ct, Http("srv", server.BaseUrl)).ConfigureAwait(false))
                {
                    ToolResult result = await manager.ExecuteAsync("call-1", "srv.hello", Json("{}"), ct).ConfigureAwait(false);
                    MuxAssert.IsTrue(result.Success, "success: " + result.Content);
                    MuxAssert.AreEqual("call-1", result.ToolCallId, "call id kept");
                    MuxAssert.Contains("HELLO_WORLD", result.Content, "content returned");
                    ToolResult upper = await manager.ExecuteAsync("call-2", "SRV.hello", Json("{}"), ct).ConfigureAwait(false);
                    MuxAssert.IsTrue(upper.Success, "case-insensitive prefix routes to the same tool: " + upper.Content);
                }
            });
            Add("ArgumentsArriveIntact", "Nested, numeric, boolean, null, and Unicode arguments reach the server intact", async (CancellationToken ct) =>
            {
                await using (ScriptableMcpHttpServer server = await ScriptableMcpHttpServer.StartAsync((McpHttpServer s) =>
                    s.RegisterTool("echo_args", "Echoes its arguments", new { type = "object" }, (RpcParameters a) => (object)(a?.RawJson ?? "null"))).ConfigureAwait(false))
                using (McpToolManager manager = await Connect(ct, Http("srv", server.BaseUrl)).ConfigureAwait(false))
                {
                    string args = "{\"n\":42.5,\"flag\":true,\"none\":null,\"list\":[1,\"two\",{\"three\":3}],\"text\":\"héllo 世界 \\ud83d\\ude80\"}";
                    ToolResult result = await manager.ExecuteAsync("c", "srv.echo_args", Json(args), ct).ConfigureAwait(false);
                    MuxAssert.IsTrue(result.Success, "success: " + result.Content);
                    string echoed = TextOf(result.Content);
                    using (JsonDocument doc = JsonDocument.Parse(echoed))
                    {
                        MuxAssert.AreEqual(42.5, doc.RootElement.GetProperty("n").GetDouble(), "number");
                        MuxAssert.IsTrue(doc.RootElement.GetProperty("flag").GetBoolean(), "boolean");
                        MuxAssert.AreEqual(JsonValueKind.Null, doc.RootElement.GetProperty("none").ValueKind, "null");
                        MuxAssert.AreEqual(3, doc.RootElement.GetProperty("list")[2].GetProperty("three").GetInt32(), "nested");
                        MuxAssert.AreEqual("héllo 世界 🚀", doc.RootElement.GetProperty("text").GetString(), "Unicode");
                    }
                }
            });
            Add("ToolErrorIsFailure", "A tool error (isError) fails the result and keeps the message for the model", async (CancellationToken ct) =>
            {
                await using (ScriptableMcpHttpServer server = await ScriptableMcpHttpServer.StartAsync((McpHttpServer s) =>
                    s.RegisterTool("broken", "Fails", new { type = "object", properties = new { } }, (RpcParameters a) => throw new McpToolException("disk is full"))).ConfigureAwait(false))
                using (McpToolManager manager = await Connect(ct, Http("srv", server.BaseUrl)).ConfigureAwait(false))
                {
                    ToolResult result = await manager.ExecuteAsync("c", "srv.broken", Json("{}"), ct).ConfigureAwait(false);
                    MuxAssert.IsFalse(result.Success, "failed");
                    MuxAssert.Contains("disk is full", result.Content, "message kept");
                    MuxAssert.Contains("\"isError\":true", result.Content.Replace(" ", string.Empty), "raw result kept");
                }
            });
            Add("UnexpectedExceptionIsFailure", "An unexpected handler exception still comes back as a failed result, not a crash", async (CancellationToken ct) =>
            {
                await using (ScriptableMcpHttpServer server = await ScriptableMcpHttpServer.StartAsync((McpHttpServer s) =>
                    s.RegisterTool("explode", "Throws", new { type = "object", properties = new { } }, (RpcParameters a) => throw new InvalidOperationException("internal detail"))).ConfigureAwait(false))
                using (McpToolManager manager = await Connect(ct, Http("srv", server.BaseUrl)).ConfigureAwait(false))
                {
                    ToolResult result = await manager.ExecuteAsync("c", "srv.explode", Json("{}"), ct).ConfigureAwait(false);
                    MuxAssert.IsFalse(result.Success, "failed");
                    MuxAssert.IsTrue(result.Content.Length > 0, "a result body exists");
                }
            });
            Add("SchemaViolationsRejected", "Undeclared arguments and missing required arguments are rejected as tool errors", async (CancellationToken ct) =>
            {
                await using (ScriptableMcpHttpServer server = await ScriptableMcpHttpServer.StartAsync((McpHttpServer s) =>
                    s.RegisterTool("strict", "Strict", new { type = "object", properties = new { text = new { type = "string" } }, required = new[] { "text" }, additionalProperties = false }, (RpcParameters a) => (object)("got " + a.GetString("text")))).ConfigureAwait(false))
                using (McpToolManager manager = await Connect(ct, Http("srv", server.BaseUrl)).ConfigureAwait(false))
                {
                    ToolResult extra = await manager.ExecuteAsync("c", "srv.strict", Json("{\"text\":\"a\",\"bogus\":1}"), ct).ConfigureAwait(false);
                    MuxAssert.IsFalse(extra.Success, "undeclared argument rejected: " + extra.Content);
                    ToolResult missing = await manager.ExecuteAsync("c", "srv.strict", Json("{}"), ct).ConfigureAwait(false);
                    MuxAssert.IsFalse(missing.Success, "missing required argument rejected: " + missing.Content);
                    ToolResult ok = await manager.ExecuteAsync("c", "srv.strict", Json("{\"text\":\"fine\"}"), ct).ConfigureAwait(false);
                    MuxAssert.IsTrue(ok.Success, "valid call accepted: " + ok.Content);
                    MuxAssert.Contains("got fine", ok.Content, "handler ran");
                }
            });
            Add("UnknownToolReported", "Calling an unregistered tool reports unknown_mcp_tool without contacting a server", async (CancellationToken ct) =>
            {
                using (McpToolManager manager = new McpToolManager(new List<McpServerConfig>()))
                {
                    await manager.InitializeAsync(ct).ConfigureAwait(false);
                    ToolResult result = await manager.ExecuteAsync("c", "nowhere.tool", Json("{}"), ct).ConfigureAwait(false);
                    MuxAssert.IsFalse(result.Success, "failed");
                    MuxAssert.Contains("unknown_mcp_tool", result.Content, "error code");
                    MuxAssert.AreEqual(0, manager.GetToolDefinitions().Count, "no tools");
                    MuxAssert.AreEqual(0, manager.GetServerStatus().Count, "no servers");
                }
            });
            Add("CancellationStopsSlowTool", "Cancelling a slow tool call returns promptly with a failure", async (CancellationToken ct) =>
            {
                await using (ScriptableMcpHttpServer server = await ScriptableMcpHttpServer.StartAsync((McpHttpServer s) =>
                    s.RegisterTool("slow", "Sleeps", new { type = "object", properties = new { } }, async (RpcParameters a) => { await Task.Delay(30000).ConfigureAwait(false); return (object)"late"; })).ConfigureAwait(false))
                using (McpToolManager manager = await Connect(ct, Http("srv", server.BaseUrl)).ConfigureAwait(false))
                using (CancellationTokenSource cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(400)))
                {
                    Stopwatch watch = Stopwatch.StartNew();
                    ToolResult result = await manager.ExecuteAsync("c", "srv.slow", Json("{}"), cancel.Token).ConfigureAwait(false);
                    MuxAssert.IsFalse(result.Success, "failed");
                    MuxAssert.Contains("mcp_call_failed", result.Content, "reported as a failed call");
                    MuxAssert.IsTrue(watch.Elapsed < TimeSpan.FromSeconds(10), "returned promptly: " + watch.Elapsed);
                }
            });
            Add("SameToolNameOnTwoServersRoutesCorrectly", "Two servers exposing the same tool name each get their own prefixed tool", async (CancellationToken ct) =>
            {
                await using (ScriptableMcpHttpServer first = await ScriptableMcpHttpServer.StartAsync((McpHttpServer s) => ScriptableMcpHttpServer.Text(s, "who", "FIRST")).ConfigureAwait(false))
                await using (ScriptableMcpHttpServer second = await ScriptableMcpHttpServer.StartAsync((McpHttpServer s) => ScriptableMcpHttpServer.Text(s, "who", "SECOND")).ConfigureAwait(false))
                using (McpToolManager manager = await Connect(ct, Http("one", first.BaseUrl), Http("two", second.BaseUrl)).ConfigureAwait(false))
                {
                    MuxAssert.AreEqual(2, manager.GetToolDefinitions().Count, "two tools");
                    MuxAssert.Contains("FIRST", (await manager.ExecuteAsync("c", "one.who", Json("{}"), ct).ConfigureAwait(false)).Content, "first server answers its own tool");
                    MuxAssert.Contains("SECOND", (await manager.ExecuteAsync("c", "two.who", Json("{}"), ct).ConfigureAwait(false)).Content, "second server answers its own tool");
                }
            });
            Add("DuplicateServerNamesFirstWins", "Two configured servers with the same name: the first is used and the second is ignored", async (CancellationToken ct) =>
            {
                await using (ScriptableMcpHttpServer first = await ScriptableMcpHttpServer.StartAsync((McpHttpServer s) => ScriptableMcpHttpServer.Text(s, "who", "FIRST")).ConfigureAwait(false))
                await using (ScriptableMcpHttpServer second = await ScriptableMcpHttpServer.StartAsync((McpHttpServer s) => ScriptableMcpHttpServer.Text(s, "who", "SECOND")).ConfigureAwait(false))
                using (McpToolManager manager = await Connect(ct, Http("dup", first.BaseUrl), Http("DUP", second.BaseUrl)).ConfigureAwait(false))
                {
                    MuxAssert.AreEqual(1, manager.GetServerStatus().Count, "one connection");
                    MuxAssert.AreEqual(1, manager.GetToolDefinitions().Count, "one tool");
                    MuxAssert.Contains("FIRST", (await manager.ExecuteAsync("c", "dup.who", Json("{}"), ct).ConfigureAwait(false)).Content, "the first definition wins");
                    MuxAssert.AreEqual(1, manager.GetConnectionResults().Count, "one result");
                }
            });
            Add("AddAndRemoveAtRuntime", "Servers can be added and removed at runtime; a duplicate add is refused", async (CancellationToken ct) =>
            {
                await using (ScriptableMcpHttpServer server = await ScriptableMcpHttpServer.StartAsync((McpHttpServer s) => ScriptableMcpHttpServer.Text(s, "ping", "PONG")).ConfigureAwait(false))
                using (McpToolManager manager = await Connect(ct).ConfigureAwait(false))
                {
                    await manager.AddServerAsync(Http("live", server.BaseUrl), ct).ConfigureAwait(false);
                    MuxAssert.IsTrue(manager.HasTool("live.ping"), "added server's tool registered");
                    await MuxAssert.ThrowsAsync<InvalidOperationException>(() => manager.AddServerAsync(Http("LIVE", server.BaseUrl), ct), "duplicate name refused").ConfigureAwait(false);
                    await MuxAssert.ThrowsAsync<ArgumentNullException>(() => manager.AddServerAsync((McpServerConfig)null!, ct), "null config").ConfigureAwait(false);
                    await manager.RemoveServerAsync("Live").ConfigureAwait(false);
                    MuxAssert.IsFalse(manager.HasTool("live.ping"), "tool removed");
                    MuxAssert.AreEqual(0, manager.GetServerStatus().Count, "connection removed");
                    MuxAssert.AreEqual(0, manager.GetConnectionResults().Count, "result removed");
                    await manager.RemoveServerAsync("never-added").ConfigureAwait(false);
                    ToolResult gone = await manager.ExecuteAsync("c", "live.ping", Json("{}"), ct).ConfigureAwait(false);
                    MuxAssert.Contains("unknown_mcp_tool", gone.Content, "removed tool is unknown");
                }
            });
            Add("AddFailingServerThrows", "Adding an unreachable server at runtime throws and registers nothing", async (CancellationToken ct) =>
            {
                using (McpToolManager manager = await Connect(ct).ConfigureAwait(false))
                {
                    await MuxAssert.ThrowsAsync<InvalidOperationException>(() => manager.AddServerAsync(Http("down", "http://127.0.0.1:" + StubHttpServer.FreeLoopbackPort()), ct), "unreachable add throws").ConfigureAwait(false);
                    MuxAssert.AreEqual(0, manager.GetToolDefinitions().Count, "nothing registered");
                }
            });
            Add("StatusAndResultsDescribeEachServer", "Status and connection results describe good and bad servers side by side", async (CancellationToken ct) =>
            {
                await using (ScriptableMcpHttpServer server = await ScriptableMcpHttpServer.StartAsync((McpHttpServer s) =>
                {
                    ScriptableMcpHttpServer.Text(s, "a", "A");
                    ScriptableMcpHttpServer.Text(s, "b", "B");
                    ScriptableMcpHttpServer.Text(s, "c", "C");
                }).ConfigureAwait(false))
                using (McpToolManager manager = await Connect(ct, Http("good", server.BaseUrl), Http("bad", "http://127.0.0.1:" + StubHttpServer.FreeLoopbackPort())).ConfigureAwait(false))
                {
                    List<McpServerStatus> status = manager.GetServerStatus();
                    MuxAssert.AreEqual(1, status.Count, "only the connected server has a live status");
                    MuxAssert.AreEqual(3, status[0].ToolCount, "tool count");
                    MuxAssert.IsTrue(status[0].Connected, "connected");
                    Dictionary<string, McpConnectionResult> results = new Dictionary<string, McpConnectionResult>(StringComparer.OrdinalIgnoreCase);
                    foreach (McpConnectionResult r in manager.GetConnectionResults()) results[r.Name] = r;
                    MuxAssert.IsTrue(results["good"].Connected && results["good"].ToolCount == 3, "good result");
                    MuxAssert.AreEqual("http", results["good"].Method, "method label");
                    MuxAssert.IsFalse(results["bad"].Connected, "bad result");
                    MuxAssert.Contains("connection refused", results["bad"].Error ?? string.Empty, "cause named");
                    MuxAssert.IsTrue(!string.IsNullOrEmpty(results["bad"].Details), "details attached");
                    results["good"].Error = "mutated";
                    MuxAssert.IsNull(manager.GetConnectionResults().Find(r => r.Name == "good")!.Error, "results are detached copies");
                }
            });
            Add("BearerTokenSentWithEnvExpansion", "A bearer token (with an environment variable) is sent on every request", async (CancellationToken ct) =>
            {
                Environment.SetEnvironmentVariable("MUX_TEST_MCP_TOKEN", "tok-123");
                try
                {
                    await using (ScriptableMcpHttpServer server = await ScriptableMcpHttpServer.StartAsync((McpHttpServer s) => ScriptableMcpHttpServer.Text(s, "x", "X"), "Bearer tok-123").ConfigureAwait(false))
                    {
                        McpServerConfig config = Http("auth", server.BaseUrl);
                        config.Auth = new McpAuthConfig { Type = McpAuthTypeEnum.Bearer, BearerToken = "${MUX_TEST_MCP_TOKEN}" };
                        using (McpToolManager manager = await Connect(ct, config).ConfigureAwait(false))
                        {
                            MuxAssert.IsTrue(manager.HasTool("auth.x"), "connected with the expanded token");
                            MuxAssert.IsTrue((await manager.ExecuteAsync("c", "auth.x", Json("{}"), ct).ConfigureAwait(false)).Success, "calls authenticate too");
                            foreach (string header in server.AuthorizationHeaders) MuxAssert.AreEqual("Bearer tok-123", header, "every request carries the token");
                        }
                    }
                }
                finally
                {
                    Environment.SetEnvironmentVariable("MUX_TEST_MCP_TOKEN", null);
                }
            });
            Add("WrongBearerRefused", "A wrong bearer token fails to connect with an HTTP 401 cause", async (CancellationToken ct) =>
            {
                await using (ScriptableMcpHttpServer server = await ScriptableMcpHttpServer.StartAsync((McpHttpServer s) => ScriptableMcpHttpServer.Text(s, "x", "X"), "Bearer right").ConfigureAwait(false))
                {
                    McpServerConfig config = Http("auth", server.BaseUrl);
                    config.Auth = new McpAuthConfig { Type = McpAuthTypeEnum.Bearer, BearerToken = "wrong" };
                    using (McpToolManager manager = await Connect(ct, config).ConfigureAwait(false))
                    {
                        McpConnectionResult result = manager.GetConnectionResults()[0];
                        MuxAssert.IsFalse(result.Connected, "refused");
                        MuxAssert.Contains("401", result.Error ?? string.Empty, "status named");
                        MuxAssert.DoesNotContain("wrong", (result.Error ?? string.Empty) + (result.Details ?? string.Empty), "the token never appears");
                    }
                }
            });
            Add("ApiKeyHeaders", "API-key auth sends X-API-Key by default or a custom header, and skips a blank value", async (CancellationToken ct) =>
            {
                await using (ScriptableMcpHttpServer server = await ScriptableMcpHttpServer.StartAsync((McpHttpServer s) => ScriptableMcpHttpServer.Text(s, "x", "X")).ConfigureAwait(false))
                {
                    McpServerConfig config = Http("key", server.BaseUrl);
                    config.Auth = new McpAuthConfig { Type = McpAuthTypeEnum.ApiKey, ApiKeyValue = "k-1" };
                    using (McpToolManager manager = await Connect(ct, config).ConfigureAwait(false))
                    {
                        MuxAssert.IsTrue(manager.HasTool("key.x"), "connected");
                    }

                    MuxAssert.IsTrue(new List<string>(server.ApiKeyHeaders).Contains("k-1"), "default header sent");
                }

                await using (ScriptableMcpHttpServer custom = await ScriptableMcpHttpServer.StartAsync((McpHttpServer s) => ScriptableMcpHttpServer.Text(s, "x", "X")).ConfigureAwait(false))
                {
                    custom.ApiKeyHeaderName = "X-Custom-Key";
                    McpServerConfig config = Http("key", custom.BaseUrl);
                    config.Auth = new McpAuthConfig { Type = McpAuthTypeEnum.ApiKey, ApiKeyHeader = "X-Custom-Key", ApiKeyValue = "k-2" };
                    using (McpToolManager manager = await Connect(ct, config).ConfigureAwait(false))
                    {
                        MuxAssert.IsTrue(manager.HasTool("key.x"), "connected");
                    }

                    MuxAssert.IsTrue(new List<string>(custom.ApiKeyHeaders).Contains("k-2"), "custom header sent");
                }

                await using (ScriptableMcpHttpServer blank = await ScriptableMcpHttpServer.StartAsync((McpHttpServer s) => ScriptableMcpHttpServer.Text(s, "x", "X")).ConfigureAwait(false))
                {
                    McpServerConfig config = Http("key", blank.BaseUrl);
                    config.Auth = new McpAuthConfig { Type = McpAuthTypeEnum.ApiKey, ApiKeyValue = "   " };
                    using (McpToolManager manager = await Connect(ct, config).ConfigureAwait(false))
                    {
                        MuxAssert.IsTrue(manager.HasTool("key.x"), "connected");
                    }

                    foreach (string value in blank.ApiKeyHeaders) MuxAssert.AreEqual(string.Empty, value, "a blank key sends no header");
                    foreach (string value in blank.AuthorizationHeaders) MuxAssert.AreEqual(string.Empty, value, "and no Authorization header");
                }
            });
            Add("NoAuthSendsNoCredentials", "With auth none, no credentials are sent", async (CancellationToken ct) =>
            {
                await using (ScriptableMcpHttpServer server = await ScriptableMcpHttpServer.StartAsync((McpHttpServer s) => ScriptableMcpHttpServer.Text(s, "x", "X")).ConfigureAwait(false))
                using (McpToolManager manager = await Connect(ct, Http("open", server.BaseUrl)).ConfigureAwait(false))
                {
                    MuxAssert.IsTrue(manager.HasTool("open.x"), "connected");
                    MuxAssert.IsTrue(server.AuthorizationHeaders.Count > 0, "requests recorded");
                    foreach (string header in server.AuthorizationHeaders) MuxAssert.AreEqual(string.Empty, header, "no Authorization header");
                }
            });
            Add("McpPathVariants", "A wrong MCP path fails with a 404 cause; a path without a leading slash is normalized", async (CancellationToken ct) =>
            {
                await using (ScriptableMcpHttpServer server = await ScriptableMcpHttpServer.StartAsync((McpHttpServer s) => ScriptableMcpHttpServer.Text(s, "x", "X")).ConfigureAwait(false))
                {
                    McpServerConfig wrong = Http("p", server.BaseUrl);
                    wrong.McpPath = "/not-mcp";
                    using (McpToolManager manager = await Connect(ct, wrong).ConfigureAwait(false))
                    {
                        McpConnectionResult result = manager.GetConnectionResults()[0];
                        MuxAssert.IsFalse(result.Connected, "wrong path fails");
                        MuxAssert.Contains("404", result.Error ?? string.Empty, "404 named");
                    }

                    McpServerConfig bare = Http("p", server.BaseUrl);
                    bare.McpPath = "mcp";
                    using (McpToolManager manager = await Connect(ct, bare).ConfigureAwait(false))
                    {
                        MuxAssert.IsTrue(manager.HasTool("p.x"), "a path without a slash still connects: " + manager.GetConnectionResults()[0].Error);
                    }

                    McpServerConfig blank = Http("p", server.BaseUrl);
                    blank.McpPath = "  ";
                    MuxAssert.AreEqual("/mcp", blank.McpPath, "a blank path becomes /mcp");
                }
            });
            Add("InvalidUrlsRefused", "Non-HTTP and relative URLs are refused before any request", async (CancellationToken ct) =>
            {
                foreach (string url in new[] { "ftp://127.0.0.1/x", "relative/path", "file:///etc/passwd", string.Empty })
                {
                    using (McpToolManager manager = await Connect(ct, Http("u", url)).ConfigureAwait(false))
                    {
                        McpConnectionResult result = manager.GetConnectionResults()[0];
                        MuxAssert.IsFalse(result.Connected, "refused: " + url);
                        MuxAssert.Contains("http", (result.Error ?? string.Empty).ToLowerInvariant(), "explains the URL requirement for '" + url + "'");
                    }
                }
            });
            Add("ServerGoneMidSession", "A server that stops after connecting turns calls into failures, not exceptions", async (CancellationToken ct) =>
            {
                ScriptableMcpHttpServer server = await ScriptableMcpHttpServer.StartAsync((McpHttpServer s) => ScriptableMcpHttpServer.Text(s, "x", "X")).ConfigureAwait(false);
                using (McpToolManager manager = await Connect(ct, Http("flaky", server.BaseUrl)).ConfigureAwait(false))
                {
                    MuxAssert.IsTrue((await manager.ExecuteAsync("c", "flaky.x", Json("{}"), ct).ConfigureAwait(false)).Success, "works while up");
                    await server.DisposeAsync().ConfigureAwait(false);
                    using (CancellationTokenSource limit = new CancellationTokenSource(TimeSpan.FromSeconds(30)))
                    {
                        ToolResult after = await manager.ExecuteAsync("c", "flaky.x", Json("{}"), limit.Token).ConfigureAwait(false);
                        MuxAssert.IsFalse(after.Success, "fails after the server is gone");
                        MuxAssert.Contains("mcp_call_failed", after.Content, "reported as a failed call");
                    }
                }
            });
            Add("InitializeHonorsCancellation", "Initialize with a cancelled token throws OperationCanceledException", async (CancellationToken ct) =>
            {
                using (CancellationTokenSource cancelled = new CancellationTokenSource())
                using (McpToolManager manager = new McpToolManager(new List<McpServerConfig> { Http("x", "http://127.0.0.1:1") }))
                {
                    cancelled.Cancel();
                    await MuxAssert.ThrowsAsync<OperationCanceledException>(() => manager.InitializeAsync(cancelled.Token), "cancelled").ConfigureAwait(false);
                }

                MuxAssert.Throws<ArgumentNullException>(() => new McpToolManager(null!), "null configs");
            });
            Add("DisposeIsSafeAndClears", "Dispose clears tools and is idempotent", async (CancellationToken ct) =>
            {
                await using (ScriptableMcpHttpServer server = await ScriptableMcpHttpServer.StartAsync((McpHttpServer s) => ScriptableMcpHttpServer.Text(s, "x", "X")).ConfigureAwait(false))
                {
                    McpToolManager manager = await Connect(ct, Http("d", server.BaseUrl)).ConfigureAwait(false);
                    manager.Dispose();
                    manager.Dispose();
                    MuxAssert.AreEqual(0, manager.GetToolDefinitions().Count, "tools cleared");
                    MuxAssert.IsFalse(manager.HasTool("d.x"), "lookup cleared");
                    MuxAssert.AreEqual(0, manager.GetConnectionResults().Count, "results cleared");
                }
            });
            Add("ZeroToolServerConnects", "A server with no tools connects and reports zero tools", async (CancellationToken ct) =>
            {
                await using (ScriptableMcpHttpServer server = await ScriptableMcpHttpServer.StartAsync((McpHttpServer s) => { }).ConfigureAwait(false))
                using (McpToolManager manager = await Connect(ct, Http("empty", server.BaseUrl)).ConfigureAwait(false))
                {
                    McpConnectionResult result = manager.GetConnectionResults()[0];
                    MuxAssert.IsTrue(result.Connected, "connected: " + result.Error);
                    MuxAssert.AreEqual(0, result.ToolCount, "no tools");
                    MuxAssert.AreEqual(0, manager.GetToolDefinitions().Count, "nothing registered");
                }
            });
            Add("PaginatedToolListFullyDiscovered", "A server that pages tools/list still has every tool discovered", async (CancellationToken ct) =>
            {
                await using (ScriptableMcpHttpServer server = await ScriptableMcpHttpServer.StartAsync((McpHttpServer s) =>
                {
                    s.PageSize = 10;
                    for (int i = 0; i < 35; i++) ScriptableMcpHttpServer.Text(s, "t" + i.ToString("00", System.Globalization.CultureInfo.InvariantCulture), "T" + i);
                }).ConfigureAwait(false))
                using (McpToolManager manager = await Connect(ct, Http("paged", server.BaseUrl)).ConfigureAwait(false))
                {
                    MuxAssert.AreEqual(35, manager.GetToolDefinitions().Count, "all pages followed");
                    MuxAssert.IsTrue(manager.HasTool("paged.t34"), "the last page's tool is registered");
                    MuxAssert.AreEqual(35, manager.GetConnectionResults()[0].ToolCount, "tool count");
                }
            });
            Add("LargeResultRoundTrips", "A large tool result arrives complete", async (CancellationToken ct) =>
            {
                string big = new string('z', 200000);
                await using (ScriptableMcpHttpServer server = await ScriptableMcpHttpServer.StartAsync((McpHttpServer s) => ScriptableMcpHttpServer.Text(s, "big", big)).ConfigureAwait(false))
                using (McpToolManager manager = await Connect(ct, Http("srv", server.BaseUrl)).ConfigureAwait(false))
                {
                    ToolResult result = await manager.ExecuteAsync("c", "srv.big", Json("{}"), ct).ConfigureAwait(false);
                    MuxAssert.IsTrue(result.Success, "success");
                    MuxAssert.AreEqual(200000, TextOf(result.Content).Length, "complete");
                }
            });
            Add("MultipleContentPartsKept", "A result with several content parts keeps all of them", async (CancellationToken ct) =>
            {
                await using (ScriptableMcpHttpServer server = await ScriptableMcpHttpServer.StartAsync((McpHttpServer s) =>
                    s.RegisterTool("multi", "Two parts", new { type = "object", properties = new { } }, (RpcParameters a) => (object)new { content = new object[] { new { type = "text", text = "PART_ONE" }, new { type = "text", text = "PART_TWO" } } })).ConfigureAwait(false))
                using (McpToolManager manager = await Connect(ct, Http("srv", server.BaseUrl)).ConfigureAwait(false))
                {
                    ToolResult result = await manager.ExecuteAsync("c", "srv.multi", Json("{}"), ct).ConfigureAwait(false);
                    MuxAssert.IsTrue(result.Success, "success: " + result.Content);
                    MuxAssert.Contains("PART_ONE", result.Content, "first part");
                    MuxAssert.Contains("PART_TWO", result.Content, "second part");
                }
            });
            Add("ConcurrentCallsAllSucceed", "Ten concurrent calls to one server all succeed with their own results", async (CancellationToken ct) =>
            {
                await using (ScriptableMcpHttpServer server = await ScriptableMcpHttpServer.StartAsync((McpHttpServer s) =>
                    s.RegisterTool("twice", "Doubles n", new { type = "object", properties = new { n = new { type = "integer" } } }, async (RpcParameters a) => { await Task.Delay(50).ConfigureAwait(false); return (object)("v=" + (a.GetInt64("n") * 2)); })).ConfigureAwait(false))
                using (McpToolManager manager = await Connect(ct, Http("srv", server.BaseUrl)).ConfigureAwait(false))
                {
                    List<Task<ToolResult>> calls = new List<Task<ToolResult>>();
                    for (int i = 0; i < 10; i++) calls.Add(manager.ExecuteAsync("c" + i, "srv.twice", Json("{\"n\":" + i + "}"), ct));
                    ToolResult[] results = await Task.WhenAll(calls).ConfigureAwait(false);
                    for (int i = 0; i < 10; i++)
                    {
                        MuxAssert.IsTrue(results[i].Success, "call " + i);
                        MuxAssert.Contains("v=" + (i * 2), results[i].Content, "call " + i + " got its own result");
                    }
                }
            });
            Add("RuntimeServesToolsAndRefreshes", "McpRuntime exposes tools, executes them, announces once, and picks up config changes", async (CancellationToken ct) =>
            {
                await using (ScriptableMcpHttpServer first = await ScriptableMcpHttpServer.StartAsync((McpHttpServer s) => ScriptableMcpHttpServer.Text(s, "one", "ONE")).ConfigureAwait(false))
                await using (ScriptableMcpHttpServer second = await ScriptableMcpHttpServer.StartAsync((McpHttpServer s) => ScriptableMcpHttpServer.Text(s, "two", "TWO")).ConfigureAwait(false))
                {
                    List<McpServerConfig> configs = new List<McpServerConfig> { Http("a", first.BaseUrl) };
                    int changes = 0;
                    List<string> notices = new List<string>();
                    using (McpRuntime runtime = new McpRuntime(() => new List<McpServerConfig>(configs), () => Interlocked.Increment(ref changes), TimeSpan.FromMinutes(5), (string n) => { lock (notices) notices.Add(n); }))
                    {
                        runtime.Start();
                        await runtime.FirstRefreshCompleted.WaitAsync(TimeSpan.FromSeconds(30), ct).ConfigureAwait(false);
                        MuxAssert.AreEqual(1, runtime.CurrentTools.Count, "one tool");
                        ToolResult result = await runtime.ExecuteToolAsync("a.one", Json("{}"), ".", ct).ConfigureAwait(false);
                        MuxAssert.Contains("ONE", result.Content, "executes through the runtime");
                        ToolResult unknown = await runtime.ExecuteToolAsync("a.nope", Json("{}"), ".", ct).ConfigureAwait(false);
                        MuxAssert.IsFalse(unknown.Success, "unknown tool fails");
                        MuxAssert.IsTrue(changes >= 1, "tools-changed callback fired");

                        configs.Add(Http("b", second.BaseUrl));
                        runtime.RequestRefresh();
                        for (int i = 0; i < 100 && runtime.CurrentTools.Count < 2; i++) await Task.Delay(100, ct).ConfigureAwait(false);
                        MuxAssert.AreEqual(2, runtime.CurrentTools.Count, "a new server is picked up after a refresh");
                        MuxAssert.AreEqual(2, runtime.GetStatus().Count, "both servers have status");
                        lock (notices)
                        {
                            MuxAssert.AreEqual(1, notices.FindAll(n => n.Contains(" a ", StringComparison.Ordinal) || n.Contains("server a", StringComparison.Ordinal)).Count, "server a announced once: " + string.Join(" | ", notices));
                        }
                    }
                }
            });
            Add("RuntimeToleratesBrokenLoader", "A config loader that throws leaves the runtime empty instead of crashing", async (CancellationToken ct) =>
            {
                using (McpRuntime runtime = new McpRuntime(() => throw new InvalidOperationException("bad file"), () => { }, TimeSpan.FromMinutes(5)))
                {
                    runtime.Start();
                    await runtime.FirstRefreshCompleted.WaitAsync(TimeSpan.FromSeconds(30), ct).ConfigureAwait(false);
                    MuxAssert.AreEqual(0, runtime.CurrentTools.Count, "no tools");
                    MuxAssert.AreEqual(0, runtime.GetStatus().Count, "no status");
                }

                using (McpRuntime nulls = new McpRuntime(() => null!, () => { }, TimeSpan.FromMinutes(5)))
                {
                    nulls.Start();
                    await nulls.FirstRefreshCompleted.WaitAsync(TimeSpan.FromSeconds(30), ct).ConfigureAwait(false);
                    MuxAssert.AreEqual(0, nulls.CurrentTools.Count, "a null list counts as empty");
                }
            });

            return new TestSuiteDescriptor(SuiteId, "MCP client over HTTP: discovery, execution, errors, auth, routing, runtime", cases);
        }

        #endregion

        #region Private-Methods

        private static McpServerConfig Http(string name, string url)
        {
            return new McpServerConfig { Name = name, Transport = McpTransportTypeEnum.Http, Url = url };
        }

        private static async Task<McpToolManager> Connect(CancellationToken ct, params McpServerConfig[] configs)
        {
            McpToolManager manager = new McpToolManager(new List<McpServerConfig>(configs));
            await manager.InitializeAsync(ct).ConfigureAwait(false);
            return manager;
        }

        private static JsonElement Json(string text)
        {
            using (JsonDocument document = JsonDocument.Parse(text))
            {
                return document.RootElement.Clone();
            }
        }

        // Pulls the concatenated text parts out of a raw tools/call result.
        private static string TextOf(string rawResult)
        {
            StringBuilder text = new StringBuilder();
            using (JsonDocument doc = JsonDocument.Parse(rawResult))
            {
                if (doc.RootElement.TryGetProperty("content", out JsonElement content) && content.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement part in content.EnumerateArray())
                    {
                        if (part.TryGetProperty("text", out JsonElement t)) text.Append(t.GetString());
                    }
                }
            }

            return text.ToString();
        }

        #endregion
    }
}
