namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Net;
    using System.Net.Http;
    using System.Net.Http.Headers;
    using System.Runtime.CompilerServices;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Cli.App;
    using Mux.Cli.Commands;
    using Mux.Core.Agent;
    using Mux.Core.Enums;
    using Mux.Core.Jobs;
    using Mux.Core.McpServer;
    using Mux.Core.Models;
    using Mux.Core.Sessions;
    using Mux.Core.Settings;
    using Mux.Core.Tools;
    using Mux.Server;
    using Test.Shared.Support;
    using Touchstone.Core;
    using TUIKit.Terminal;
    using Voltaic.Core;
    using Voltaic.Mcp;

    /// <summary>
    /// Touchstone suite for MCP end to end: the <c>/v1.0/api/mcp-servers</c> REST routes (auth, validation, unique
    /// names, secret masking and preservation, encoded names), the server's wire protocol over HTTP (ping, unknown
    /// methods and tools, auth status codes, concurrent sessions) and over raw stdio (bad lines, notifications, ping,
    /// unknown methods), <c>mux print --mcp-config</c> (inline JSON, file, <c>--no-mcp</c>, <c>--strict-mcp-config</c>,
    /// bad config), the terminal MCP manager showing an offline cause and full details, and more
    /// <c>mux mcp serve</c> argument checks.
    /// </summary>
    public static class McpEndToEndSuite
    {
        #region Private-Members

        private const string SuiteId = "McpEndToEnd";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Builds the MCP end-to-end suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/> for the end-to-end cases.</returns>
        public static TestSuiteDescriptor Create()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();
            void Add(string id, string name, Func<CancellationToken, Task> body, bool skip = false, string skipReason = "")
            {
                cases.Add(new TestCaseDescriptor(SuiteId, id, name, body, skip: skip, skipReason: skipReason));
            }

            // --- REST routes ---
            Add("RestRequiresKey", "The MCP server routes refuse requests without the API key", (CancellationToken ct) => WithRestAsync(async (HttpClient http, string baseUrl, string configDir) =>
            {
                http.DefaultRequestHeaders.Authorization = null;
                using (HttpResponseMessage get = await http.GetAsync(baseUrl, ct).ConfigureAwait(false)) MuxAssert.AreEqual(401, (int)get.StatusCode, "GET");
                using (HttpResponseMessage put = await http.PutAsync(baseUrl, Body("{\"Items\":[]}"), ct).ConfigureAwait(false)) MuxAssert.AreEqual(401, (int)put.StatusCode, "PUT");
                using (HttpResponseMessage delete = await http.DeleteAsync(baseUrl + "?name=x", ct).ConfigureAwait(false)) MuxAssert.AreEqual(401, (int)delete.StatusCode, "DELETE");
                http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "wrong-key");
                using (HttpResponseMessage wrong = await http.GetAsync(baseUrl, ct).ConfigureAwait(false)) MuxAssert.AreEqual(401, (int)wrong.StatusCode, "wrong key");
            }));
            Add("RestPutGetRoundTripMasksSecrets", "PUT saves servers, GET returns them with the secret masked as a flag", (CancellationToken ct) => WithRestAsync(async (HttpClient http, string baseUrl, string configDir) =>
            {
                using (HttpResponseMessage empty = await http.GetAsync(baseUrl, ct).ConfigureAwait(false))
                {
                    MuxAssert.Contains("\"Count\":0", await empty.Content.ReadAsStringAsync(ct).ConfigureAwait(false), "starts empty");
                }

                string payload = "{\"Items\":[{\"Name\":\"web\",\"Transport\":\"http\",\"Url\":\"http://h:1\",\"AuthType\":\"bearer\",\"AuthSecret\":\"TOP-SECRET-TOKEN\"},{\"Name\":\"fs\",\"Transport\":\"stdio\",\"Command\":\"node\",\"Args\":[\"s.js\"],\"Env\":[\"ROOT=/tmp\",\"bad-entry\"]}]}";
                using (HttpResponseMessage put = await http.PutAsync(baseUrl, Body(payload), ct).ConfigureAwait(false))
                {
                    string body = await put.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    MuxAssert.AreEqual(200, (int)put.StatusCode, "saved: " + body);
                    MuxAssert.DoesNotContain("TOP-SECRET-TOKEN", body, "the secret is never echoed");
                    MuxAssert.Contains("\"AuthSecretSet\":true", body, "secret flagged as set");
                }

                using (HttpResponseMessage get = await http.GetAsync(baseUrl, ct).ConfigureAwait(false))
                {
                    string body = await get.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    MuxAssert.Contains("\"Count\":2", body, "two servers");
                    MuxAssert.DoesNotContain("TOP-SECRET-TOKEN", body, "GET never shows the secret");
                    MuxAssert.Contains("ROOT=/tmp", body, "env kept");
                    MuxAssert.DoesNotContain("bad-entry", body, "an env entry without '=' is dropped");
                }

                List<McpServerConfig> saved = SettingsLoader.ParseMcpServers(File.ReadAllText(Path.Combine(configDir, "mcp-servers.json")));
                MuxAssert.AreEqual("TOP-SECRET-TOKEN", saved.Find(s => s.Name == "web")!.Auth.BearerToken, "the secret is stored");
            }));
            Add("RestBlankSecretPreservesStored", "Saving again with a blank secret keeps the stored one; a new secret replaces it", (CancellationToken ct) => WithRestAsync(async (HttpClient http, string baseUrl, string configDir) =>
            {
                await http.PutAsync(baseUrl, Body("{\"Items\":[{\"Name\":\"api\",\"Transport\":\"http\",\"Url\":\"http://h\",\"AuthType\":\"apikey\",\"AuthHeader\":\"X-K\",\"AuthSecret\":\"FIRST\"}]}"), ct).ConfigureAwait(false);
                await http.PutAsync(baseUrl, Body("{\"Items\":[{\"Name\":\"api\",\"Transport\":\"http\",\"Url\":\"http://h2\",\"AuthType\":\"apikey\",\"AuthHeader\":\"X-K\",\"AuthSecret\":\"\"}]}"), ct).ConfigureAwait(false);
                McpServerConfig kept = SettingsLoader.ParseMcpServers(File.ReadAllText(Path.Combine(configDir, "mcp-servers.json")))[0];
                MuxAssert.AreEqual("FIRST", kept.Auth.ApiKeyValue, "blank keeps the stored secret");
                MuxAssert.AreEqual("http://h2", kept.Url, "other fields updated");
                await http.PutAsync(baseUrl, Body("{\"Items\":[{\"Name\":\"api\",\"Transport\":\"http\",\"Url\":\"http://h2\",\"AuthType\":\"apikey\",\"AuthSecret\":\"SECOND\"}]}"), ct).ConfigureAwait(false);
                McpServerConfig replaced = SettingsLoader.ParseMcpServers(File.ReadAllText(Path.Combine(configDir, "mcp-servers.json")))[0];
                MuxAssert.AreEqual("SECOND", replaced.Auth.ApiKeyValue, "a new secret replaces it");
                MuxAssert.AreEqual("X-API-Key", replaced.Auth.ApiKeyHeader, "a blank header falls back to X-API-Key");
            }));
            Add("RestPutValidation", "PUT rejects invalid JSON, a missing items array, a blank name, and duplicate names", (CancellationToken ct) => WithRestAsync(async (HttpClient http, string baseUrl, string configDir) =>
            {
                foreach (KeyValuePair<string, string> bad in new Dictionary<string, string>
                {
                    ["{ not json"] = "not valid JSON",
                    ["{}"] = "items",
                    ["{\"Items\":[{\"Name\":\"  \"}]}"] = "needs a name",
                    ["{\"Items\":[{\"Name\":\"dup\"},{\"Name\":\"DUP\"}]}"] = "unique"
                })
                {
                    using (HttpResponseMessage response = await http.PutAsync(baseUrl, Body(bad.Key), ct).ConfigureAwait(false))
                    {
                        string body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                        MuxAssert.AreEqual(400, (int)response.StatusCode, "400 for " + bad.Key);
                        MuxAssert.Contains(bad.Value, body, "explains " + bad.Key);
                    }
                }

                MuxAssert.IsFalse(File.Exists(Path.Combine(configDir, "mcp-servers.json")), "nothing was written");
            }));
            Add("RestDeleteByNameIncludingEncoded", "DELETE removes a server by name (case-insensitive, URL-encoded), ignores unknown names, and needs a name", (CancellationToken ct) => WithRestAsync(async (HttpClient http, string baseUrl, string configDir) =>
            {
                await http.PutAsync(baseUrl, Body("{\"Items\":[{\"Name\":\"my server\",\"Command\":\"node\"},{\"Name\":\"keep\",\"Command\":\"node\"}]}"), ct).ConfigureAwait(false);
                using (HttpResponseMessage noName = await http.DeleteAsync(baseUrl, ct).ConfigureAwait(false)) MuxAssert.AreEqual(400, (int)noName.StatusCode, "name required");
                using (HttpResponseMessage unknown = await http.DeleteAsync(baseUrl + "?name=ghost", ct).ConfigureAwait(false)) MuxAssert.Contains("\"Count\":2", await unknown.Content.ReadAsStringAsync(ct).ConfigureAwait(false), "unknown name changes nothing");
                using (HttpResponseMessage encoded = await http.DeleteAsync(baseUrl + "?name=" + Uri.EscapeDataString("MY SERVER"), ct).ConfigureAwait(false))
                {
                    string body = await encoded.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    MuxAssert.Contains("\"Count\":1", body, "the encoded, differently cased name was removed: " + body);
                    MuxAssert.Contains("keep", body, "the other server stays");
                }
            }));

            // --- server wire protocol over HTTP ---
            Add("HttpPingUnknownMethodAndTool", "The MCP server answers ping, refuses unknown methods and tools, and keeps working", (CancellationToken ct) => WithServerAsync(new MuxMcpServerOptions(), null, async (McpTestServer server) =>
            {
                using (McpHttpClient client = (await server.ConnectAsync(null, ct).ConfigureAwait(false))!)
                {
                    JsonRpcResponse ping = await client.CallAsync("ping", new { }, 10000, ct).ConfigureAwait(false);
                    MuxAssert.IsNull(ping.Error, "ping answered");
                    JsonRpcResponse unknown = await client.CallAsync("frobnicate/now", new { }, 10000, ct).ConfigureAwait(false);
                    MuxAssert.IsNotNull(unknown.Error, "unknown method refused");
                    MuxAssert.AreEqual(-32601, unknown.Error!.Code, "method not found code");
                    JsonRpcResponse noTool = await client.CallAsync("tools/call", new { name = "no_such_tool", arguments = new { } }, 10000, ct).ConfigureAwait(false);
                    MuxAssert.IsTrue(noTool.Error != null || JsonSerializer.Serialize(noTool.Result).Contains("\"isError\":true", StringComparison.Ordinal), "unknown tool refused");
                    JsonRpcResponse gated = await client.CallAsync("tools/call", new { name = "run_skill", arguments = new { name = "x", command = "y" } }, 10000, ct).ConfigureAwait(false);
                    MuxAssert.IsTrue(gated.Error != null || JsonSerializer.Serialize(gated.Result).Contains("\"isError\":true", StringComparison.Ordinal), "run_skill unavailable without --allow-skills");
                    JsonRpcResponse list = await client.CallAsync("tools/list", new { }, 10000, ct).ConfigureAwait(false);
                    MuxAssert.IsNull(list.Error, "still serving after errors");
                }
            }));
            Add("HttpNoResourcesOrPrompts", "The MCP server exposes no resources or prompts", (CancellationToken ct) => WithServerAsync(new MuxMcpServerOptions(), null, async (McpTestServer server) =>
            {
                using (McpHttpClient client = (await server.ConnectAsync(null, ct).ConfigureAwait(false))!)
                {
                    foreach (string method in new[] { "resources/list", "prompts/list" })
                    {
                        JsonRpcResponse response = await client.CallAsync(method, new { }, 10000, ct).ConfigureAwait(false);
                        if (response.Error == null)
                        {
                            string json = JsonSerializer.Serialize(response.Result);
                            MuxAssert.IsTrue(json.Contains("[]", StringComparison.Ordinal), method + " is empty: " + json);
                        }
                    }
                }
            }));
            Add("HttpAuthStatusCodes", "With a key, the HTTP endpoint answers 401 with a challenge for missing, wrong, or non-bearer credentials", (CancellationToken ct) => WithServerAsync(new MuxMcpServerOptions(), "srv-key", async (McpTestServer server) =>
            {
                using (HttpClient http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) })
                {
                    foreach (string? auth in new[] { null, "Bearer nope", "Basic c3J2LWtleQ==", "srv-key" })
                    {
                        using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, server.BaseUrl + MuxMcpServerHost.HttpPath))
                        {
                            request.Content = Body("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"ping\"}");
                            request.Headers.Accept.ParseAdd("application/json");
                            request.Headers.Accept.ParseAdd("text/event-stream");
                            if (auth != null) request.Headers.TryAddWithoutValidation("Authorization", auth);
                            using (HttpResponseMessage response = await http.SendAsync(request, ct).ConfigureAwait(false))
                            {
                                MuxAssert.AreEqual(401, (int)response.StatusCode, "401 for '" + auth + "'");
                                MuxAssert.IsTrue(response.Headers.WwwAuthenticate.Count > 0, "challenge for '" + auth + "'");
                            }
                        }
                    }
                }

                MuxAssert.IsNotNull(await server.ConnectAsync("srv-key", ct).ConfigureAwait(false), "the right key connects");
            }));
            Add("HttpConcurrentSessions", "Several clients can hold sessions and list tools at the same time", (CancellationToken ct) => WithServerAsync(new MuxMcpServerOptions(), null, async (McpTestServer server) =>
            {
                List<McpHttpClient> clients = new List<McpHttpClient>();
                try
                {
                    for (int i = 0; i < 4; i++) clients.Add((await server.ConnectAsync(null, ct).ConfigureAwait(false))!);
                    List<Task<JsonRpcResponse>> lists = clients.ConvertAll(c => c.CallAsync("tools/list", new { }, 10000, ct));
                    foreach (JsonRpcResponse response in await Task.WhenAll(lists).ConfigureAwait(false))
                    {
                        MuxAssert.IsNull(response.Error, "each session lists tools");
                        MuxAssert.Contains("list_sessions", JsonSerializer.Serialize(response.Result), "catalog returned");
                    }
                }
                finally
                {
                    foreach (McpHttpClient client in clients) client.Dispose();
                }
            }));

            // --- server wire protocol over raw stdio ---
            string? cliDll = FindCliDll();
            Add("StdioProtocolEdgeCases", "Over stdio the server survives bad lines, answers ping and tools/list, and refuses unknown methods", async (CancellationToken ct) =>
            {
                string configDir = Path.Combine(Path.GetTempPath(), "mux-mcp-e2e-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(configDir);
                File.WriteAllText(Path.Combine(configDir, "settings.json"), "{\"skillsEnabled\":false}");
                ProcessStartInfo info = new ProcessStartInfo("dotnet") { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
                foreach (string argument in new[] { cliDll!, "mcp", "serve", "--config-dir", configDir, "--working-directory", configDir }) info.ArgumentList.Add(argument);
                try
                {
                    using (Process process = Process.Start(info)!)
                    {
                        Task<string> stderr = process.StandardError.ReadToEndAsync();
                        async Task<JsonDocument> Ask(string line, int id)
                        {
                            await process.StandardInput.WriteLineAsync(line).ConfigureAwait(false);
                            await process.StandardInput.FlushAsync().ConfigureAwait(false);
                            return await ReadResponseAsync(process, id, ct).ConfigureAwait(false);
                        }

                        await process.StandardInput.WriteLineAsync("this is not json").ConfigureAwait(false);
                        await process.StandardInput.WriteLineAsync("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/whatever\"}").ConfigureAwait(false);
                        using (JsonDocument init = await Ask(JsonSerializer.Serialize(new { jsonrpc = "2.0", id = 1, method = "initialize", @params = new { protocolVersion = McpProtocol.LatestProtocolVersion, capabilities = new { }, clientInfo = new { name = "t", version = "1" } } }), 1).ConfigureAwait(false))
                        {
                            MuxAssert.AreEqual("mux", init.RootElement.GetProperty("result").GetProperty("serverInfo").GetProperty("name").GetString(), "server name after bad lines");
                            MuxAssert.IsTrue(init.RootElement.GetProperty("result").GetProperty("instructions").GetString()!.Contains("run", StringComparison.Ordinal), "instructions sent");
                        }

                        await process.StandardInput.WriteLineAsync("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}").ConfigureAwait(false);
                        using (JsonDocument ping = await Ask("{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"ping\"}", 2).ConfigureAwait(false))
                        {
                            MuxAssert.IsTrue(ping.RootElement.TryGetProperty("result", out _), "ping answered");
                        }

                        using (JsonDocument unknown = await Ask("{\"jsonrpc\":\"2.0\",\"id\":3,\"method\":\"no/such/method\"}", 3).ConfigureAwait(false))
                        {
                            MuxAssert.AreEqual(-32601, unknown.RootElement.GetProperty("error").GetProperty("code").GetInt32(), "method not found");
                        }

                        using (JsonDocument tools = await Ask("{\"jsonrpc\":\"2.0\",\"id\":4,\"method\":\"tools/list\"}", 4).ConfigureAwait(false))
                        {
                            string json = tools.RootElement.GetRawText();
                            MuxAssert.Contains("\"run\"", json, "run listed");
                            MuxAssert.DoesNotContain("run_skill", json, "run_skill hidden without the flag");
                        }

                        using (JsonDocument badArgs = await Ask("{\"jsonrpc\":\"2.0\",\"id\":5,\"method\":\"tools/call\",\"params\":{\"name\":\"get_session\",\"arguments\":{\"id\":\"../../etc\"}}}", 5).ConfigureAwait(false))
                        {
                            string json = badArgs.RootElement.GetRawText();
                            MuxAssert.Contains("No session has the id", json, "traversal refused over the wire");
                        }

                        process.StandardInput.Close();
                        if (!process.WaitForExit(20000)) { try { process.Kill(true); } catch (Exception) { } }
                        MuxAssert.AreEqual(0, process.ExitCode, "exits cleanly when stdin closes");
                        string log = await stderr.ConfigureAwait(false);
                        MuxAssert.Contains("approval ceiling deny", log, "startup line on stderr");
                    }
                }
                finally
                {
                    try { Directory.Delete(configDir, true); } catch (Exception) { }
                }
            }, skip: cliDll == null, skipReason: "Mux.Cli.dll was not found next to the test build");

            // --- mux print --mcp-config ---
            Add("PrintUsesInlineMcpConfig", "mux print --mcp-config with inline JSON exposes and calls the MCP tool", (CancellationToken ct) => WithPrintAsync(ct, async (ScriptableMcpHttpServer mcp, MockHttpServer model, string configDir) =>
            {
                string inline = "{\"servers\":[{\"name\":\"tools\",\"transport\":\"http\",\"url\":\"" + mcp.BaseUrl + "\"}]}";
                CliInvocationResult result = InvokeCli(new[] { "print", "--config-dir", configDir, "--yolo", "--mcp-config", inline, "--base-url", model.BaseUrl, "--model", "m", "--adapter-type", "openai-compatible", "zq9 use the mcp tool" });
                MuxAssert.AreEqual(0, result.ExitCode, "exit 0: " + result.StdErr);
                MuxAssert.Contains("Used the MCP tool.", result.StdOut, "final answer");
                MuxAssert.IsTrue(model.ReceivedRequests.Exists(r => r.Contains("MCP_TOOL_OUTPUT_7", StringComparison.Ordinal)), "the MCP tool's output reached the model");
                MuxAssert.IsTrue(model.ReceivedRequests[0].Contains("tools.lookup", StringComparison.Ordinal), "the MCP tool was offered to the model");
                await Task.CompletedTask.ConfigureAwait(false);
            }));
            Add("PrintUsesMcpConfigFile", "mux print --mcp-config with a file path works the same way", (CancellationToken ct) => WithPrintAsync(ct, async (ScriptableMcpHttpServer mcp, MockHttpServer model, string configDir) =>
            {
                string file = Path.Combine(configDir, "extra-mcp.json");
                File.WriteAllText(file, "{\"servers\":[{\"name\":\"tools\",\"transport\":\"http\",\"url\":\"" + mcp.BaseUrl + "\"}]}");
                CliInvocationResult result = InvokeCli(new[] { "print", "--config-dir", configDir, "--yolo", "--mcp-config", file, "--base-url", model.BaseUrl, "--model", "m", "--adapter-type", "openai-compatible", "zq9 use the mcp tool" });
                MuxAssert.AreEqual(0, result.ExitCode, "exit 0: " + result.StdErr);
                MuxAssert.IsTrue(model.ReceivedRequests.Exists(r => r.Contains("MCP_TOOL_OUTPUT_7", StringComparison.Ordinal)), "the tool ran");
                await Task.CompletedTask.ConfigureAwait(false);
            }));
            Add("PrintNoMcpWins", "--no-mcp keeps MCP tools out even with --mcp-config", (CancellationToken ct) => WithPrintAsync(ct, async (ScriptableMcpHttpServer mcp, MockHttpServer model, string configDir) =>
            {
                string inline = "{\"servers\":[{\"name\":\"tools\",\"transport\":\"http\",\"url\":\"" + mcp.BaseUrl + "\"}]}";
                InvokeCli(new[] { "print", "--config-dir", configDir, "--yolo", "--no-mcp", "--mcp-config", inline, "--base-url", model.BaseUrl, "--model", "m", "--adapter-type", "openai-compatible", "plain question" });
                MuxAssert.IsTrue(model.ReceivedRequests.Count > 0, "the model was called");
                MuxAssert.IsFalse(model.ReceivedRequests[0].Contains("tools.lookup", StringComparison.Ordinal), "no MCP tool offered");
                await Task.CompletedTask.ConfigureAwait(false);
            }));
            Add("PrintStrictIgnoresConfigDirServers", "--strict-mcp-config ignores servers from the config directory; without it they are merged", (CancellationToken ct) => WithPrintAsync(ct, async (ScriptableMcpHttpServer mcp, MockHttpServer model, string configDir) =>
            {
                await using (ScriptableMcpHttpServer other = await ScriptableMcpHttpServer.StartAsync((McpHttpServer s) => ScriptableMcpHttpServer.Text(s, "extra", "EXTRA")).ConfigureAwait(false))
                {
                    File.WriteAllText(Path.Combine(configDir, "mcp-servers.json"), "{\"servers\":[{\"name\":\"fromdir\",\"transport\":\"http\",\"url\":\"" + other.BaseUrl + "\"}]}");
                    string inline = "{\"servers\":[{\"name\":\"tools\",\"transport\":\"http\",\"url\":\"" + mcp.BaseUrl + "\"}]}";
                    InvokeCli(new[] { "print", "--config-dir", configDir, "--yolo", "--strict-mcp-config", "--mcp-config", inline, "--base-url", model.BaseUrl, "--model", "m", "--adapter-type", "openai-compatible", "plain question" });
                    MuxAssert.IsFalse(model.ReceivedRequests[0].Contains("fromdir.extra", StringComparison.Ordinal), "strict: config-dir server left out");
                    MuxAssert.IsTrue(model.ReceivedRequests[0].Contains("tools.lookup", StringComparison.Ordinal), "strict: the given server is in");
                    int before = model.ReceivedRequests.Count;
                    InvokeCli(new[] { "print", "--config-dir", configDir, "--yolo", "--mcp-config", inline, "--base-url", model.BaseUrl, "--model", "m", "--adapter-type", "openai-compatible", "plain question" });
                    MuxAssert.IsTrue(model.ReceivedRequests[before].Contains("fromdir.extra", StringComparison.Ordinal), "without strict: config-dir server merged");
                }
            }));
            Add("PrintBadMcpConfig", "An unreadable or invalid --mcp-config fails the run with a clear error", (CancellationToken ct) => WithPrintAsync(ct, async (ScriptableMcpHttpServer mcp, MockHttpServer model, string configDir) =>
            {
                foreach (string bad in new[] { "{ not json", Path.Combine(configDir, "missing.json"), "{\"servers\":[{\"name\":\"x\",\"transport\":\"pigeon\"}]}" })
                {
                    CliInvocationResult result = InvokeCli(new[] { "print", "--config-dir", configDir, "--yolo", "--mcp-config", bad, "--base-url", model.BaseUrl, "--model", "m", "--adapter-type", "openai-compatible", "q" });
                    MuxAssert.AreEqual(1, result.ExitCode, "exit 1 for " + bad);
                    MuxAssert.Contains("Invalid --mcp-config", result.StdOut + result.StdErr, "explains " + bad);
                }

                await Task.CompletedTask.ConfigureAwait(false);
            }));

            // --- terminal MCP manager ---
            Add("TerminalShowsOfflineCauseAndDetails", "The /mcp manager shows an offline server's cause, and '?' writes the full diagnosis", async (CancellationToken ct) =>
            {
                string configDir = Path.Combine(Path.GetTempPath(), "mux-mcp-tui-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(configDir);
                string? original = Environment.GetEnvironmentVariable("MUX_CONFIG_DIR");
                Environment.SetEnvironmentVariable("MUX_CONFIG_DIR", configDir);
                try
                {
                    int port = StubHttpServer.FreeLoopbackPort();
                    SettingsLoader.SaveMcpServers(new List<McpServerConfig> { new McpServerConfig { Name = "down", Transport = McpTransportTypeEnum.Http, Url = "http://127.0.0.1:" + port } });
                    using (McpRuntime runtime = new McpRuntime(SettingsLoader.LoadMcpServers, () => { }, TimeSpan.FromMinutes(5)))
                    {
                        runtime.Start();
                        await runtime.FirstRefreshCompleted.WaitAsync(TimeSpan.FromSeconds(30), ct).ConfigureAwait(false);
                        MuxAssert.Contains("connection refused", runtime.GetStatus()[0].Error ?? string.Empty, "runtime has the cause");
                        HeadlessBackend backend = new HeadlessBackend(200, 50);
                        await using (JobManager manager = new JobManager(EchoRunner, maxConcurrency: 1))
                        using (MuxTuiApp app = new MuxTuiApp(backend, manager, "demo", ApprovalPolicyEnum.AutoApprove, mcpRuntime: runtime))
                        {
                            backend.FeedInput("/mcp\r");
                            app.PumpInputOnce();
                            backend.FeedInput("?");
                            app.PumpInputOnce();
                            string transcript = string.Empty;
                            for (int i = 0; i < 50 && !transcript.Contains("MCP server down", StringComparison.Ordinal); i++)
                            {
                                await Task.Delay(50, ct).ConfigureAwait(false);
                                transcript = string.Join("\n", app.TranscriptSnapshot());
                            }

                            MuxAssert.Contains("MCP server down", transcript, "details header written: " + transcript);
                            MuxAssert.Contains("connection refused", transcript, "cause written");
                        }
                    }
                }
                finally
                {
                    Environment.SetEnvironmentVariable("MUX_CONFIG_DIR", original);
                    try { Directory.Delete(configDir, true); } catch (Exception) { }
                }
            });

            // --- serve arguments ---
            Add("ServeArgumentsMore", "mux mcp serve parses --host, --log-messages, --config-dir, -w, and -e, and rejects missing values", (CancellationToken ct) =>
            {
                string dir = Path.GetTempPath();
                MuxAssert.IsTrue(McpServeCommand.TryParse(new[] { "serve", "--host", "0.0.0.0", "--log-messages", "--config-dir", "/x", "-w", dir, "-e", "big" }, out McpServeArguments? parsed, out string error), "valid: " + error);
                MuxAssert.AreEqual("0.0.0.0", parsed!.Host, "host");
                MuxAssert.IsTrue(parsed.LogMessages, "log messages");
                MuxAssert.AreEqual(Path.GetFullPath(dir), parsed.WorkingDirectory, "working directory");
                MuxAssert.AreEqual("big", parsed.Endpoint, "endpoint");
                MuxAssert.IsTrue(McpServeCommand.TryParse(new[] { "SERVE" }, out _, out _), "verb is case-insensitive");
                MuxAssert.IsTrue(McpServeCommand.TryParse(new[] { "serve", "--http", "65535" }, out McpServeArguments? max, out _), "highest port");
                MuxAssert.AreEqual(65535, max!.HttpPort ?? 0, "port kept");
                MuxAssert.IsTrue(McpServeCommand.TryParse(new[] { "serve", "--approval-policy", "deny", "--yolo" }, out McpServeArguments? last, out _), "later flag wins");
                MuxAssert.AreEqual(ApprovalPolicyEnum.AutoApprove, last!.MaxApprovalPolicy, "--yolo after --approval-policy wins");
                foreach (string[] bad in new[]
                {
                    new[] { "serve", "--host" }, new[] { "serve", "--host", " " }, new[] { "serve", "-e" }, new[] { "serve", "--endpoint", "" },
                    new[] { "serve", "-w" }, new[] { "serve", "--http", "65536" }, new[] { "serve", "--http", "-1" }, new[] { "serve", "--approval-policy" },
                    new[] { "serve", "extra-positional" }, new[] { "list" }, null!
                })
                {
                    MuxAssert.IsFalse(McpServeCommand.TryParse(bad, out McpServeArguments? none, out string message), "rejects " + (bad == null ? "null" : string.Join(" ", bad)));
                    MuxAssert.IsNull(none, "no result");
                    MuxAssert.IsTrue(message.Length > 0, "explains");
                }

                return Task.CompletedTask;
            });

            return new TestSuiteDescriptor(SuiteId, "MCP end to end: REST routes, wire protocol, print, terminal, serve arguments", cases);
        }

        #endregion

        #region Private-Methods

        private static StringContent Body(string json)
        {
            return new StringContent(json, Encoding.UTF8, "application/json");
        }

        private static async Task WithRestAsync(Func<HttpClient, string, string, Task> body)
        {
            string configDir = Path.Combine(Path.GetTempPath(), "mux-mcp-rest-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(configDir);
            string? original = Environment.GetEnvironmentVariable("MUX_CONFIG_DIR");
            Environment.SetEnvironmentVariable("MUX_CONFIG_DIR", configDir);
            MuxServer? server = null;
            try
            {
                RestServerSettings rest = new RestServerSettings { Hostname = "127.0.0.1", ApiKey = "rest-key" };
                int port = 0;
                for (int attempt = 0; attempt < 10 && server == null; attempt++)
                {
                    port = StubHttpServer.FreeLoopbackPort();
                    rest.Port = port;
                    MuxServer candidate = new MuxServer(rest, "9.9.9-test", new SessionStore(Path.Combine(configDir, "sessions")), () => new List<EndpointConfig>(), null);
                    try { candidate.Start(); server = candidate; } catch (Exception) { candidate.Dispose(); Thread.Sleep(50); }
                }

                MuxAssert.IsNotNull(server, "server bound");
                using (HttpClient http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) })
                {
                    for (int attempt = 0; attempt < 20; attempt++)
                    {
                        try { await http.GetAsync("http://127.0.0.1:" + port + "/v1.0/api/health").ConfigureAwait(false); break; }
                        catch (Exception) { await Task.Delay(100).ConfigureAwait(false); }
                    }

                    http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "rest-key");
                    await body(http, "http://127.0.0.1:" + port + "/v1.0/api/mcp-servers", configDir).ConfigureAwait(false);
                }
            }
            finally
            {
                server?.Dispose();
                Environment.SetEnvironmentVariable("MUX_CONFIG_DIR", original);
                try { Directory.Delete(configDir, true); } catch (Exception) { }
            }
        }

        private static async Task WithServerAsync(MuxMcpServerOptions options, string? apiKey, Func<McpTestServer, Task> body)
        {
            string root = Path.Combine(Path.GetTempPath(), "mux-mcp-e2e-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            options.DefaultWorkingDirectory = root;
            McpTestServer server = await McpTestServer.StartAsync(options, new FakeMcpRunExecutor(), new List<EndpointConfig>(), new SessionStore(Path.Combine(root, "sessions")), null, apiKey).ConfigureAwait(false);
            try
            {
                await body(server).ConfigureAwait(false);
            }
            finally
            {
                await server.DisposeAsync().ConfigureAwait(false);
                try { Directory.Delete(root, true); } catch (Exception) { }
            }
        }

        // An MCP server with one tool (lookup) and a mock model that calls tools.lookup once, then answers.
        private static async Task WithPrintAsync(CancellationToken ct, Func<ScriptableMcpHttpServer, MockHttpServer, string, Task> body)
        {
            string configDir = Path.Combine(Path.GetTempPath(), "mux-mcp-print-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(configDir);
            File.WriteAllText(Path.Combine(configDir, "settings.json"), "{\"skillsEnabled\":false,\"projectInstructionsEnabled\":false}");
            try
            {
                await using (ScriptableMcpHttpServer mcp = await ScriptableMcpHttpServer.StartAsync((McpHttpServer s) => ScriptableMcpHttpServer.Text(s, "lookup", "MCP_TOOL_OUTPUT_7")).ConfigureAwait(false))
                using (MockHttpServer model = new MockHttpServer())
                {
                    string toolCall = "{\"choices\":[{\"delta\":{\"tool_calls\":[{\"index\":0,\"id\":\"call_mcp\",\"function\":{\"name\":\"tools.lookup\",\"arguments\":\"{}\"}}]},\"finish_reason\":\"tool_calls\"}]}";
                    model.RegisterStreamingResponse("zq9", new List<string> { toolCall });
                    model.RegisterStreamingResponse("MCP_TOOL_OUTPUT_7", new List<string> { AgentTestHarness.BuildTextSseChunk("Used the MCP tool.") });
                    model.RegisterStreamingResponse("plain question", new List<string> { AgentTestHarness.BuildTextSseChunk("Plain answer.") });
                    model.RegisterStreamingResponse("q", new List<string> { AgentTestHarness.BuildTextSseChunk("ok") });
                    model.Start();
                    await body(mcp, model, configDir).ConfigureAwait(false);
                }
            }
            finally
            {
                try { Directory.Delete(configDir, true); } catch (Exception) { }
            }
        }

        private static CliInvocationResult InvokeCli(string[] args)
        {
            TextWriter originalOut = Console.Out;
            TextWriter originalErr = Console.Error;
            StringWriter stdout = new StringWriter();
            StringWriter stderr = new StringWriter();
            try
            {
                Console.SetOut(stdout);
                Console.SetError(stderr);
                int exitCode = Mux.Cli.Program.Main(args);
                return new CliInvocationResult(exitCode, stdout.ToString(), stderr.ToString());
            }
            finally
            {
                Console.SetOut(originalOut);
                Console.SetError(originalErr);
            }
        }

        private static async Task<JsonDocument> ReadResponseAsync(Process process, int id, CancellationToken ct)
        {
            using (CancellationTokenSource limit = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                limit.CancelAfter(TimeSpan.FromSeconds(60));
                while (true)
                {
                    string? line = await process.StandardOutput.ReadLineAsync(limit.Token).ConfigureAwait(false);
                    if (line == null) throw new InvalidOperationException("the server closed stdout before answering id " + id);
                    if (!line.TrimStart().StartsWith("{", StringComparison.Ordinal)) continue;
                    JsonDocument doc = JsonDocument.Parse(line);
                    if (doc.RootElement.TryGetProperty("id", out JsonElement idElement) && idElement.ValueKind == JsonValueKind.Number && idElement.GetInt32() == id)
                    {
                        return doc;
                    }

                    doc.Dispose();
                }
            }
        }

        private static async IAsyncEnumerable<AgentEvent> EchoRunner(Job job, string prompt, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask.ConfigureAwait(false);
            yield return new AssistantTextEvent { Text = "Echo: " + prompt };
            yield return new RunCompletedEvent { RunId = Guid.NewGuid().ToString("N"), Status = "completed", IterationsCompleted = 1, DurationMs = 1 };
        }

        private static string? FindCliDll()
        {
            string framework = "net" + Environment.Version.Major + ".0";
            DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                foreach (string configuration in new[] { "Debug", "Release" })
                {
                    string candidate = Path.Combine(dir.FullName, "Mux.Cli", "bin", configuration, framework, "Mux.Cli.dll");
                    if (File.Exists(candidate)) return candidate;
                }

                dir = dir.Parent;
            }

            return null;
        }

        #endregion
    }
}
