namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Cli.Commands;
    using Mux.Core.Enums;
    using Mux.Core.McpServer;
    using Mux.Core.Models;
    using Mux.Core.Sessions;
    using Mux.Core.Tools;
    using Test.Shared.Support;
    using Touchstone.Core;
    using Voltaic.Core;
    using Voltaic.Mcp;

    /// <summary>
    /// Touchstone suite for <c>mux mcp serve</c> (row 29): the tool catalog and schemas, <c>run</c> through a fake
    /// executor (answer, summary, approval capping), progress and cancellation over Streamable HTTP, the session and
    /// endpoint tools (including secret masking), <c>run_skill</c> gating, the bearer key, mux's own MCP client
    /// connecting over HTTP and over stdio to a real <c>mux mcp serve</c> child process, and a real agent turn against a
    /// mock model. Positive and negative cases throughout.
    /// </summary>
    public static class McpServerSuite
    {
        #region Private-Members

        private const string SuiteId = "McpServer";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Builds the MCP server suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/> for the MCP server cases.</returns>
        public static TestSuiteDescriptor Create()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();
            void Add(string id, string name, Func<CancellationToken, Task> body)
            {
                cases.Add(new TestCaseDescriptor(SuiteId, id, name, body));
            }

            // --- pure helpers ---
            Add("PolicyCeiling", "Requested approval policies are capped by the server ceiling and 'ask' is refused", (CancellationToken ct) =>
            {
                MuxAssert.AreEqual(ApprovalPolicyEnum.AutoSafe, MuxMcpTools.ResolvePolicy(null, ApprovalPolicyEnum.AutoSafe), "no request uses the ceiling");
                MuxAssert.AreEqual(ApprovalPolicyEnum.Deny, MuxMcpTools.ResolvePolicy("deny", ApprovalPolicyEnum.AutoApprove), "stricter is allowed");
                MuxAssert.AreEqual(ApprovalPolicyEnum.AutoSafe, MuxMcpTools.ResolvePolicy("Auto-Safe", ApprovalPolicyEnum.AutoApprove), "case-insensitive");
                MuxAssert.AreEqual(ApprovalPolicyEnum.AutoApprove, MuxMcpTools.ResolvePolicy("auto", ApprovalPolicyEnum.AutoApprove), "equal is allowed");
                McpToolException looser = MuxAssert.Throws<McpToolException>(() => MuxMcpTools.ResolvePolicy("auto", ApprovalPolicyEnum.AutoSafe), "looser refused");
                MuxAssert.Contains("more permissive than this server allows (auto-safe)", looser.Message, "names the ceiling");
                MuxAssert.Throws<McpToolException>(() => MuxMcpTools.ResolvePolicy("auto-safe", ApprovalPolicyEnum.Deny), "auto-safe above deny refused");
                McpToolException ask = MuxAssert.Throws<McpToolException>(() => MuxMcpTools.ResolvePolicy("ask", ApprovalPolicyEnum.AutoApprove), "ask refused");
                MuxAssert.Contains("no person is attached", ask.Message, "explains why");
                MuxAssert.AreEqual(ApprovalPolicyEnum.Deny, new MuxMcpServerOptions { MaxApprovalPolicy = ApprovalPolicyEnum.Ask }.MaxApprovalPolicy, "an ask ceiling becomes deny");
                return Task.CompletedTask;
            });
            Add("MaskUrlHidesCredentials", "Base URLs lose embedded user information and query values", (CancellationToken ct) =>
            {
                MuxAssert.AreEqual("http://***@host:8080/v1?key=***&region=***", MuxMcpTools.MaskUrl("http://user:pw@host:8080/v1?key=abc&region=us"), "user info and query masked");
                MuxAssert.AreEqual("http://localhost:11434", MuxMcpTools.MaskUrl("http://localhost:11434"), "plain URL unchanged");
                MuxAssert.AreEqual("https://api.openai.com/v1", MuxMcpTools.MaskUrl("https://api.openai.com/v1"), "path kept");
                MuxAssert.AreEqual("not a url", MuxMcpTools.MaskUrl("not a url"), "non-URL returned as is");
                MuxAssert.AreEqual(string.Empty, MuxMcpTools.MaskUrl(null), "null is empty");
                return Task.CompletedTask;
            });
            Add("BearerAuthentication", "The bearer check accepts only the exact key", (CancellationToken ct) =>
            {
                MuxAssert.IsTrue(MuxMcpServerHost.Authenticate("Bearer s3cret", "s3cret").IsAuthenticated, "exact key");
                MuxAssert.IsTrue(MuxMcpServerHost.Authenticate("bearer  s3cret ", "s3cret").IsAuthenticated, "scheme case and spaces");
                AuthenticationResult wrong = MuxMcpServerHost.Authenticate("Bearer nope", "s3cret");
                MuxAssert.IsFalse(wrong.IsAuthenticated, "wrong key");
                MuxAssert.AreEqual(401, wrong.StatusCode, "401");
                MuxAssert.Contains("Bearer", wrong.Headers["WWW-Authenticate"], "challenge");
                MuxAssert.IsFalse(MuxMcpServerHost.Authenticate(null, "s3cret").IsAuthenticated, "missing header");
                MuxAssert.IsFalse(MuxMcpServerHost.Authenticate("Basic s3cret", "s3cret").IsAuthenticated, "other scheme");
                MuxAssert.IsFalse(MuxMcpServerHost.Authenticate("Bearer ", string.Empty).IsAuthenticated, "empty key never matches");
                return Task.CompletedTask;
            });
            Add("ServeArgumentParsing", "mux mcp serve parses its flags and rejects bad ones", (CancellationToken ct) =>
            {
                MuxAssert.IsTrue(McpServeCommand.TryParse(new[] { "serve" }, out McpServeArguments? plain, out _), "bare serve");
                MuxAssert.IsNull(plain!.HttpPort, "stdio by default");
                MuxAssert.AreEqual(ApprovalPolicyEnum.Deny, plain.MaxApprovalPolicy, "deny by default");
                MuxAssert.IsTrue(McpServeCommand.TryParse(new[] { "serve", "--http", "8811", "--api-key", "k", "--allow-skills", "--approval-policy", "auto-safe", "-e", "big" }, out McpServeArguments? full, out string fullError), "full: " + fullError);
                MuxAssert.AreEqual(8811, full!.HttpPort ?? 0, "port");
                MuxAssert.AreEqual("k", full.ApiKey, "key");
                MuxAssert.IsTrue(full.AllowSkills, "skills");
                MuxAssert.AreEqual(ApprovalPolicyEnum.AutoSafe, full.MaxApprovalPolicy, "ceiling");
                MuxAssert.AreEqual("big", full.Endpoint, "endpoint");
                MuxAssert.IsTrue(McpServeCommand.TryParse(new[] { "serve", "--yolo" }, out McpServeArguments? yolo, out _), "yolo");
                MuxAssert.AreEqual(ApprovalPolicyEnum.AutoApprove, yolo!.MaxApprovalPolicy, "yolo is auto");
                foreach (string[] bad in new[]
                {
                    new string[0], new[] { "start" }, new[] { "serve", "--http" }, new[] { "serve", "--http", "0" }, new[] { "serve", "--http", "x" },
                    new[] { "serve", "--approval-policy", "ask" }, new[] { "serve", "--approval-policy", "maybe" }, new[] { "serve", "--frob" },
                    new[] { "serve", "--working-directory", "/no/such/dir/mux" }, new[] { "serve", "--api-key" }
                })
                {
                    MuxAssert.IsFalse(McpServeCommand.TryParse(bad, out McpServeArguments? parsed, out string error), "rejects " + string.Join(" ", bad));
                    MuxAssert.IsTrue(error.Length > 0, "explains " + string.Join(" ", bad));
                }

                return Task.CompletedTask;
            });
            Add("ToolNamesFollowFlags", "run_skill is listed only with --allow-skills", (CancellationToken ct) =>
            {
                MuxMcpTools without = new MuxMcpTools(new MuxMcpServerOptions(), new FakeMcpRunExecutor(), () => new List<EndpointConfig>(), new SessionStore(Path.GetTempPath()), null);
                MuxAssert.AreEqual("run,list_sessions,get_session,list_endpoints,list_skills", string.Join(",", without.ToolNames), "default tools");
                MuxMcpTools with = new MuxMcpTools(new MuxMcpServerOptions { AllowSkills = true }, new FakeMcpRunExecutor(), () => new List<EndpointConfig>(), new SessionStore(Path.GetTempPath()), null);
                MuxAssert.Contains("run_skill", string.Join(",", with.ToolNames), "with the flag");
                MuxAssert.Throws<ArgumentNullException>(() => new MuxMcpTools(null!, new FakeMcpRunExecutor(), () => new List<EndpointConfig>(), new SessionStore(Path.GetTempPath()), null), "null options");
                MuxAssert.Throws<ArgumentNullException>(() => with.RegisterAll(null!), "null register");
                return Task.CompletedTask;
            });

            // --- over HTTP with a fake executor ---
            Add("HttpToolsListAndRun", "tools/list shows the catalog with schemas, and run returns the answer and summary", (CancellationToken ct) => WithServerAsync(new MuxMcpServerOptions { MaxApprovalPolicy = ApprovalPolicyEnum.AutoSafe }, null, async (McpServerFixture f) =>
            {
                McpHttpClient client = (await f.Server.ConnectAsync(null, ct).ConfigureAwait(false))!;
                using (client)
                {
                    JsonElement list = Result(await client.CallAsync("tools/list", new { }, 10000, ct).ConfigureAwait(false));
                    List<string> names = new List<string>();
                    JsonElement runSchema = default;
                    foreach (JsonElement tool in list.GetProperty("tools").EnumerateArray())
                    {
                        string name = tool.GetProperty("name").GetString()!;
                        names.Add(name);
                        if (name == "run") runSchema = tool.GetProperty("inputSchema");
                    }

                    MuxAssert.AreEqual("get_session,list_endpoints,list_sessions,list_skills,run", string.Join(",", Sorted(names)), "five tools, no diagnostics, no run_skill");
                    MuxAssert.AreEqual("prompt", runSchema.GetProperty("required")[0].GetString(), "prompt required");
                    MuxAssert.IsTrue(runSchema.GetProperty("properties").TryGetProperty("approval_policy", out _), "approval_policy declared");

                    McpToolOutcome run = await CallTool(client, "run", new { prompt = "fix the build", endpoint = "e1", max_turns = 7 }, ct).ConfigureAwait(false);
                    MuxAssert.IsFalse(run.IsError, "run succeeded: " + run.Text);
                    using (JsonDocument doc = JsonDocument.Parse(run.Text))
                    {
                        MuxAssert.AreEqual("ANSWER: fix the build", doc.RootElement.GetProperty("answer").GetString(), "answer");
                        MuxAssert.AreEqual("auto-safe", doc.RootElement.GetProperty("approval_policy").GetString(), "ceiling used by default");
                        MuxAssert.AreEqual(2, doc.RootElement.GetProperty("iterations").GetInt32(), "summary");
                        MuxAssert.AreEqual("e1", doc.RootElement.GetProperty("endpoint").GetString(), "endpoint passed");
                    }

                    McpRunRequest seen = f.Executor.Requests[0];
                    MuxAssert.AreEqual(7, seen.MaxTurns ?? 0, "max_turns passed");
                    MuxAssert.AreEqual(ApprovalPolicyEnum.AutoSafe, seen.ApprovalPolicy, "policy passed");
                    MuxAssert.AreEqual(Path.GetFullPath(f.Root), seen.WorkingDirectory, "default working directory");
                }
            }));
            Add("HttpRunRejectsBadArguments", "run reports missing prompts, bad numbers, unknown directories, and looser policies as tool errors", (CancellationToken ct) => WithServerAsync(new MuxMcpServerOptions(), null, async (McpServerFixture f) =>
            {
                using (McpHttpClient client = (await f.Server.ConnectAsync(null, ct).ConfigureAwait(false))!)
                {
                    McpToolOutcome missing = await CallTool(client, "run", new { endpoint = "x" }, ct).ConfigureAwait(false);
                    MuxAssert.IsTrue(missing.IsError, "missing prompt is an error");
                    McpToolOutcome zero = await CallTool(client, "run", new { prompt = "p", max_turns = 0 }, ct).ConfigureAwait(false);
                    MuxAssert.IsTrue(zero.IsError, "max_turns 0 refused");
                    McpToolOutcome dir = await CallTool(client, "run", new { prompt = "p", working_directory = "no-such-subdir" }, ct).ConfigureAwait(false);
                    MuxAssert.IsTrue(dir.IsError && dir.Text.Contains("does not exist", StringComparison.Ordinal), "unknown directory: " + dir.Text);
                    McpToolOutcome looser = await CallTool(client, "run", new { prompt = "p", approval_policy = "auto" }, ct).ConfigureAwait(false);
                    MuxAssert.IsTrue(looser.IsError && looser.Text.Contains("more permissive", StringComparison.Ordinal), "looser policy refused: " + looser.Text);
                    McpToolOutcome wrongType = await CallTool(client, "run", new { prompt = 42 }, ct).ConfigureAwait(false);
                    MuxAssert.IsTrue(wrongType.IsError, "non-string prompt refused");
                    MuxAssert.AreEqual(0, f.Executor.Requests.Count, "nothing ran");
                }
            }));
            Add("HttpProgressNotifications", "A run with a progressToken streams progress notifications", (CancellationToken ct) => WithServerAsync(new MuxMcpServerOptions(), null, async (McpServerFixture f) =>
            {
                f.Executor.ProgressSteps = 3;
                using (McpHttpClient client = (await f.Server.ConnectAsync(null, ct).ConfigureAwait(false))!)
                {
                    List<string> messages = new List<string>();
                    client.NotificationReceived += (object? sender, JsonRpcRequest notification) =>
                    {
                        if (notification.Method == "notifications/progress")
                        {
                            lock (messages) messages.Add(JsonSerializer.Serialize(notification.Params));
                        }
                    };
                    JsonRpcResponse response = await client.CallAsync("tools/call", new { name = "run", arguments = new { prompt = "go" }, _meta = new { progressToken = "tok-1" } }, 20000, ct).ConfigureAwait(false);
                    MuxAssert.IsNull(response.Error, "no error");
                    for (int i = 0; i < 40 && messages.Count < 3; i++) await Task.Delay(50, ct).ConfigureAwait(false);
                    MuxAssert.IsTrue(messages.Count >= 1, "progress delivered: " + messages.Count);
                    MuxAssert.Contains("tok-1", messages[0], "token echoed");
                    MuxAssert.Contains("step", string.Join(" ", messages), "messages carried");
                }
            }));
            Add("HttpCancellationStopsRun", "Cancelling a run request cancels the executor", (CancellationToken ct) => WithServerAsync(new MuxMcpServerOptions(), null, async (McpServerFixture f) =>
            {
                f.Executor.WaitForCancel = true;
                using (McpHttpClient client = (await f.Server.ConnectAsync(null, ct).ConfigureAwait(false))!)
                using (CancellationTokenSource cancel = new CancellationTokenSource())
                {
                    Task<JsonRpcResponse> pending = client.CallAsync("tools/call", new { name = "run", arguments = new { prompt = "long" } }, 60000, cancel.Token);
                    await f.Executor.Started.Task.WaitAsync(TimeSpan.FromSeconds(10), ct).ConfigureAwait(false);
                    cancel.Cancel();
                    try { await pending.ConfigureAwait(false); } catch (OperationCanceledException) { }
                    bool cancelled = await f.Executor.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(10), ct).ConfigureAwait(false);
                    MuxAssert.IsTrue(cancelled, "the executor saw the cancellation");
                }

                f.Executor.WaitForCancel = false;
                using (McpHttpClient again = (await f.Server.ConnectAsync(null, ct).ConfigureAwait(false))!)
                {
                    McpToolOutcome next = await CallTool(again, "run", new { prompt = "after" }, ct).ConfigureAwait(false);
                    MuxAssert.IsFalse(next.IsError, "the run gate was released: " + next.Text);
                }
            }));
            Add("HttpSessionTools", "list_sessions and get_session read the store; unknown and unsafe ids are errors", (CancellationToken ct) => WithServerAsync(new MuxMcpServerOptions(), null, async (McpServerFixture f) =>
            {
                SessionSnapshot older = new SessionSnapshot { Id = "s-old", Title = "Older", EndpointName = "e1", Model = "m1" };
                older.ConversationHistory.Add(new ConversationMessage { Role = RoleEnum.User, Content = "first question" });
                await f.Sessions.SaveAsync(older, ct).ConfigureAwait(false);
                await Task.Delay(20, ct).ConfigureAwait(false);
                SessionSnapshot newer = new SessionSnapshot { Id = "s-new", Title = "Newer", EndpointName = "e2", Model = "m2" };
                for (int i = 0; i < 5; i++) newer.ConversationHistory.Add(new ConversationMessage { Role = i % 2 == 0 ? RoleEnum.User : RoleEnum.Assistant, Content = "msg " + i + (i == 4 ? new string('x', 9000) : string.Empty) });
                await f.Sessions.SaveAsync(newer, ct).ConfigureAwait(false);
                using (McpHttpClient client = (await f.Server.ConnectAsync(null, ct).ConfigureAwait(false))!)
                {
                    McpToolOutcome list = await CallTool(client, "list_sessions", new { limit = 1 }, ct).ConfigureAwait(false);
                    MuxAssert.IsFalse(list.IsError, list.Text);
                    MuxAssert.Contains("\"total\":2", list.Text, "total count");
                    MuxAssert.Contains("s-new", list.Text, "newest first");
                    MuxAssert.DoesNotContain("s-old", list.Text, "limit honored");
                    McpToolOutcome get = await CallTool(client, "get_session", new { id = "s-new", max_messages = 2 }, ct).ConfigureAwait(false);
                    MuxAssert.Contains("\"total_messages\":5", get.Text, "total messages");
                    MuxAssert.Contains("msg 3", get.Text, "recent messages");
                    MuxAssert.DoesNotContain("msg 2", get.Text, "older messages trimmed");
                    MuxAssert.Contains("message cut at 8000 characters", get.Text, "long message cut");
                    McpToolOutcome unknown = await CallTool(client, "get_session", new { id = "nope" }, ct).ConfigureAwait(false);
                    MuxAssert.IsTrue(unknown.IsError && unknown.Text.Contains("No session has the id 'nope'", StringComparison.Ordinal), "unknown id: " + unknown.Text);
                    McpToolOutcome traversal = await CallTool(client, "get_session", new { id = "../../etc/passwd" }, ct).ConfigureAwait(false);
                    MuxAssert.IsTrue(traversal.IsError, "path traversal refused");
                    McpToolOutcome missingId = await CallTool(client, "get_session", new { }, ct).ConfigureAwait(false);
                    MuxAssert.IsTrue(missingId.IsError, "id required");
                }
            }));
            Add("HttpEndpointsNeverLeakSecrets", "list_endpoints omits API keys and masks credentials in base URLs", (CancellationToken ct) => WithServerAsync(new MuxMcpServerOptions { DefaultEndpoint = "remote" }, null, async (McpServerFixture f) =>
            {
                f.Endpoints.Add(new EndpointConfig { Name = "local", AdapterType = AdapterTypeEnum.Ollama, BaseUrl = "http://localhost:11434", Model = "qwen", IsDefault = true });
                f.Endpoints.Add(new EndpointConfig { Name = "remote", AdapterType = AdapterTypeEnum.OpenAiCompatible, BaseUrl = "https://bob:hunter2@api.example.com/v1?api_key=sk-LEAKME", Model = "big", ApiKey = "sk-SUPERSECRET" });
                using (McpHttpClient client = (await f.Server.ConnectAsync(null, ct).ConfigureAwait(false))!)
                {
                    McpToolOutcome result = await CallTool(client, "list_endpoints", new { }, ct).ConfigureAwait(false);
                    MuxAssert.Contains("\"name\":\"local\"", result.Text, "local listed");
                    MuxAssert.Contains("\"is_server_default\":true", result.Text, "server default flagged");
                    MuxAssert.DoesNotContain("sk-SUPERSECRET", result.Text, "no API key");
                    MuxAssert.DoesNotContain("hunter2", result.Text, "no password");
                    MuxAssert.DoesNotContain("sk-LEAKME", result.Text, "no query secret");
                    MuxAssert.DoesNotContain("apiKey", result.Text, "no key field");
                }
            }));
            Add("HttpRunSkillGated", "run_skill is absent and refused without the flag, and explains disabled skills with it", (CancellationToken ct) => WithServerAsync(new MuxMcpServerOptions(), null, async (McpServerFixture f) =>
            {
                using (McpHttpClient client = (await f.Server.ConnectAsync(null, ct).ConfigureAwait(false))!)
                {
                    McpToolOutcome refused = await CallTool(client, "run_skill", new { name = "git-status-vs-head", command = "run" }, ct).ConfigureAwait(false);
                    MuxAssert.IsTrue(refused.IsError, "unknown tool without the flag: " + refused.Text);
                    McpToolOutcome skills = await CallTool(client, "list_skills", new { }, ct).ConfigureAwait(false);
                    MuxAssert.Contains("\"enabled\":false", skills.Text, "skills runtime absent");
                }
            }));
            Add("HttpRunSkillWithoutRuntime", "With --allow-skills but skills disabled, run_skill explains why", (CancellationToken ct) => WithServerAsync(new MuxMcpServerOptions { AllowSkills = true }, null, async (McpServerFixture f) =>
            {
                using (McpHttpClient client = (await f.Server.ConnectAsync(null, ct).ConfigureAwait(false))!)
                {
                    McpToolOutcome result = await CallTool(client, "run_skill", new { name = "x", command = "y" }, ct).ConfigureAwait(false);
                    MuxAssert.IsTrue(result.IsError && result.Text.Contains("Skills are disabled", StringComparison.Ordinal), result.Text);
                }
            }));
            Add("HttpBearerKeyRequired", "With an API key, clients without it are refused and clients with it connect", (CancellationToken ct) => WithServerAsync(new MuxMcpServerOptions(), "k-123", async (McpServerFixture f) =>
            {
                McpHttpClient? anonymous = await f.Server.ConnectAsync(null, ct).ConfigureAwait(false);
                MuxAssert.IsNull(anonymous, "no key refused");
                McpHttpClient? wrong = await f.Server.ConnectAsync("nope", ct).ConfigureAwait(false);
                MuxAssert.IsNull(wrong, "wrong key refused");
                using (McpHttpClient client = (await f.Server.ConnectAsync("k-123", ct).ConfigureAwait(false))!)
                {
                    MuxAssert.IsNotNull(client, "right key accepted");
                    McpToolOutcome run = await CallTool(client, "run", new { prompt = "authorized" }, ct).ConfigureAwait(false);
                    MuxAssert.IsFalse(run.IsError, run.Text);
                }
            }));
            Add("MuxClientConnectsOverHttp", "mux's own MCP client lists and calls the server's tools over HTTP with a bearer key", (CancellationToken ct) => WithServerAsync(new MuxMcpServerOptions(), "k-abc", async (McpServerFixture f) =>
            {
                McpServerConfig config = new McpServerConfig { Name = "muxself", Transport = McpTransportTypeEnum.Http, Url = f.Server.BaseUrl, McpPath = MuxMcpServerHost.HttpPath };
                config.Auth = new McpAuthConfig { Type = McpAuthTypeEnum.Bearer, BearerToken = "k-abc" };
                using (McpToolManager manager = new McpToolManager(new List<McpServerConfig> { config }))
                {
                    await manager.InitializeAsync(ct).ConfigureAwait(false);
                    List<string> names = manager.GetToolDefinitions().ConvertAll(d => d.Name);
                    MuxAssert.IsTrue(names.Contains("muxself.run") && names.Contains("muxself.list_endpoints"), "tools discovered: " + string.Join(",", names));
                    using (JsonDocument args = JsonDocument.Parse("{\"prompt\":\"from mux\"}"))
                    {
                        ToolResult result = await manager.ExecuteAsync("c1", "muxself.run", args.RootElement, ct).ConfigureAwait(false);
                        MuxAssert.Contains("ANSWER: from mux", result.Content, "answer through mux's client");
                    }
                }

                McpServerConfig noKey = new McpServerConfig { Name = "anon", Transport = McpTransportTypeEnum.Http, Url = f.Server.BaseUrl, McpPath = MuxMcpServerHost.HttpPath };
                using (McpToolManager manager = new McpToolManager(new List<McpServerConfig> { noKey }))
                {
                    await manager.InitializeAsync(ct).ConfigureAwait(false);
                    McpConnectionResult status = manager.GetConnectionResults()[0];
                    MuxAssert.IsFalse(status.Connected, "refused without the key");
                    MuxAssert.Contains("401", status.Error ?? string.Empty, "the error names the status");
                }
            }));

            // --- real agent turns ---
            Add("RealExecutorRunsAgainstMockModel", "McpRunExecutor runs a real agent turn and summarizes it", async (CancellationToken ct) =>
            {
                using (MockHttpServer model = new MockHttpServer())
                {
                    model.RegisterStreamingResponse("summarize mcp", new List<string> { AgentTestHarness.BuildTextSseChunk("Real answer from the mock model.") });
                    model.Start();
                    string configDir = ConfigDir(model.BaseUrl);
                    try
                    {
                        McpRunExecutor executor = new McpRunExecutor(configDir, null, null);
                        List<string> progress = new List<string>();
                        McpRunResult result = await executor.RunAsync(new McpRunRequest { Prompt = "summarize mcp", WorkingDirectory = configDir, ApprovalPolicy = ApprovalPolicyEnum.Deny }, (string m) => { progress.Add(m); return Task.CompletedTask; }, ct).ConfigureAwait(false);
                        MuxAssert.AreEqual("Real answer from the mock model.", result.Answer, "answer");
                        MuxAssert.AreEqual("completed", result.Status, "status");
                        MuxAssert.AreEqual("mock", result.Endpoint, "endpoint");
                        MuxAssert.IsTrue(result.Iterations >= 1, "iterations counted");
                        MuxAssert.IsTrue(File.Exists(Path.Combine(configDir, "usage.db")), "the run recorded durable usage telemetry");

                        McpRunResult missing = await executor.RunAsync(new McpRunRequest { Prompt = "x", Endpoint = "no-such-endpoint", WorkingDirectory = configDir }, (string m) => Task.CompletedTask, ct).ConfigureAwait(false);
                        MuxAssert.AreEqual("failed", missing.Status, "unknown endpoint fails");
                        MuxAssert.IsTrue(!string.IsNullOrEmpty(missing.ErrorMessage), "with a message");
                    }
                    finally
                    {
                        try { Directory.Delete(configDir, true); } catch (Exception) { }
                    }
                }
            });
            Add("LogFilterSummarizes", "The stdio log filter shortens message lines to method, id, and size and passes other lines through", (CancellationToken ct) =>
            {
                string received = McpLogFilterWriter.Summarize("[10:00:00.000Z] Received: {\"jsonrpc\":\"2.0\",\"id\":7,\"method\":\"tools/call\",\"params\":{\"name\":\"run\",\"arguments\":{\"prompt\":\"TOP SECRET PLAN\"}}}");
                MuxAssert.Contains("[10:00:00.000Z] Received: tools/call (id 7, ", received, "method and id kept");
                MuxAssert.Contains(" bytes)", received, "size kept");
                MuxAssert.DoesNotContain("TOP SECRET PLAN", received, "prompt removed");
                string sent = McpLogFilterWriter.Summarize("[10:00:01.000Z] Sent: {\"jsonrpc\":\"2.0\",\"id\":7,\"result\":{\"content\":[{\"type\":\"text\",\"text\":\"THE ANSWER\"}]}}");
                MuxAssert.Contains("Sent: response (id 7, ", sent, "responses summarized");
                MuxAssert.DoesNotContain("THE ANSWER", sent, "answer removed");
                MuxAssert.Contains("Sent: error response (id 3, ", McpLogFilterWriter.Summarize("[t] Sent: {\"id\":3,\"error\":{\"code\":-1}}"), "errors summarized");
                MuxAssert.Contains("Received: notifications/initialized (", McpLogFilterWriter.Summarize("[t] Received: {\"method\":\"notifications/initialized\"}"), "notifications have no id");
                MuxAssert.Contains("Received: batch of 2 (", McpLogFilterWriter.Summarize("[t] Received: [{},{}]"), "batches counted");
                MuxAssert.Contains("Received: unparsed message (", McpLogFilterWriter.Summarize("[t] Received: {not json"), "bad JSON never echoed");
                MuxAssert.AreEqual("[t] MCP server started", McpLogFilterWriter.Summarize("[t] MCP server started"), "other lines unchanged");
                MuxAssert.AreEqual(string.Empty, McpLogFilterWriter.Summarize(string.Empty), "empty line");

                StringWriter inner = new StringWriter();
                McpLogFilterWriter writer = new McpLogFilterWriter(inner);
                writer.Write("[t] Received: {\"id\":1,\"method\":\"run\",");
                writer.Write("\"params\":{\"prompt\":\"SPLIT SECRET\"}}\r\nplain line");
                writer.WriteLine();
                writer.WriteLine("second");
                writer.Flush();
                string output = inner.ToString();
                MuxAssert.Contains("Received: run (id 1, ", output, "a message split across writes is summarized once");
                MuxAssert.DoesNotContain("SPLIT SECRET", output, "split prompt removed");
                MuxAssert.Contains("plain line" + Environment.NewLine + "second", output, "plain lines kept in order");
                MuxAssert.Throws<ArgumentNullException>(() => new McpLogFilterWriter(null!), "null inner writer");
                return Task.CompletedTask;
            });
            string? cliDll = FindCliDll();
            cases.Add(new TestCaseDescriptor(SuiteId, "StdioLogsOmitPrompts", "Over stdio, stderr shows message summaries instead of prompts unless --log-messages is passed", async (CancellationToken ct) =>
            {
                using (MockHttpServer model = new MockHttpServer())
                {
                    model.RegisterStreamingResponse("stdio log check", new List<string> { AgentTestHarness.BuildTextSseChunk("LOGGED ANSWER TEXT") });
                    model.Start();
                    string configDir = ConfigDir(model.BaseUrl);
                    try
                    {
                        string quiet = await RunStdioSessionAsync(cliDll!, configDir, false, ct).ConfigureAwait(false);
                        MuxAssert.Contains("Received: tools/call (id 2, ", quiet, "the call is summarized: " + quiet);
                        MuxAssert.DoesNotContain("PRIVATE PROMPT TEXT", quiet, "the prompt is not logged");
                        MuxAssert.DoesNotContain("LOGGED ANSWER TEXT", quiet, "the answer is not logged");
                        string verbose = await RunStdioSessionAsync(cliDll!, configDir, true, ct).ConfigureAwait(false);
                        MuxAssert.Contains("PRIVATE PROMPT TEXT", verbose, "--log-messages keeps the full log");
                    }
                    finally
                    {
                        try { Directory.Delete(configDir, true); } catch (Exception) { }
                    }
                }
            }, skip: cliDll == null, skipReason: "Mux.Cli.dll was not found next to the test build"));
            cases.Add(new TestCaseDescriptor(SuiteId, "StdioChildProcessEndToEnd", "mux's MCP client launches `mux mcp serve` over stdio and runs a real turn against a mock model", async (CancellationToken ct) =>
            {
                using (MockHttpServer model = new MockHttpServer())
                {
                    model.RegisterStreamingResponse("stdio round trip", new List<string> { AgentTestHarness.BuildTextSseChunk("Hello over stdio.") });
                    model.Start();
                    string configDir = ConfigDir(model.BaseUrl);
                    try
                    {
                        McpServerConfig config = new McpServerConfig
                        {
                            Name = "muxchild",
                            Transport = McpTransportTypeEnum.Stdio,
                            Command = "dotnet",
                            Args = new List<string> { cliDll!, "mcp", "serve", "--config-dir", configDir, "--working-directory", configDir }
                        };
                        using (McpToolManager manager = new McpToolManager(new List<McpServerConfig> { config }))
                        {
                            await manager.InitializeAsync(ct).ConfigureAwait(false);
                            McpConnectionResult status = manager.GetConnectionResults()[0];
                            MuxAssert.IsTrue(status.Connected, "connected: " + status.Error);
                            MuxAssert.AreEqual(5, status.ToolCount, "five tools over stdio");
                            using (JsonDocument args = JsonDocument.Parse("{\"prompt\":\"stdio round trip\"}"))
                            {
                                ToolResult result = await manager.ExecuteAsync("c1", "muxchild.run", args.RootElement, ct).ConfigureAwait(false);
                                MuxAssert.Contains("Hello over stdio.", result.Content, "answer from the child process: " + result.Content);
                            }

                            using (JsonDocument none = JsonDocument.Parse("{}"))
                            {
                                ToolResult endpoints = await manager.ExecuteAsync("c2", "muxchild.list_endpoints", none.RootElement, ct).ConfigureAwait(false);
                                MuxAssert.Contains("mock", endpoints.Content, "endpoints listed");
                                MuxAssert.Contains("test-model", endpoints.Content, "model listed");
                                MuxAssert.DoesNotContain("sk-CHILDSECRET", endpoints.Content, "no key over stdio either");
                            }
                        }
                    }
                    finally
                    {
                        try { Directory.Delete(configDir, true); } catch (Exception) { }
                    }
                }
            }, skip: cliDll == null, skipReason: "Mux.Cli.dll was not found next to the test build"));

            return new TestSuiteDescriptor(SuiteId, "mux mcp serve: tools, HTTP and stdio transports, auth, progress, cancellation", cases);
        }

        #endregion

        #region Private-Methods

        private static async Task WithServerAsync(MuxMcpServerOptions options, string? apiKey, Func<McpServerFixture, Task> body)
        {
            string root = Path.Combine(Path.GetTempPath(), "mux-mcpserve-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            options.DefaultWorkingDirectory = root;
            McpServerFixture fixture = new McpServerFixture { Root = root, Sessions = new SessionStore(Path.Combine(root, "sessions")) };
            fixture.Server = await McpTestServer.StartAsync(options, fixture.Executor, fixture.Endpoints, fixture.Sessions, null, apiKey).ConfigureAwait(false);
            try
            {
                await body(fixture).ConfigureAwait(false);
            }
            finally
            {
                await fixture.Server.DisposeAsync().ConfigureAwait(false);
                try { Directory.Delete(root, true); } catch (Exception) { }
            }
        }

        private static async Task<McpToolOutcome> CallTool(McpHttpClient client, string name, object arguments, CancellationToken ct)
        {
            JsonRpcResponse response = await client.CallAsync("tools/call", new { name, arguments }, 20000, ct).ConfigureAwait(false);
            if (response.Error != null)
            {
                return new McpToolOutcome { IsError = true, Text = response.Error.Message ?? string.Empty };
            }

            JsonElement result = Result(response);
            System.Text.StringBuilder text = new System.Text.StringBuilder();
            if (result.TryGetProperty("content", out JsonElement content))
            {
                foreach (JsonElement part in content.EnumerateArray())
                {
                    if (part.TryGetProperty("text", out JsonElement t)) text.Append(t.GetString() ?? string.Empty);
                }
            }

            bool isError = result.TryGetProperty("isError", out JsonElement flag) && flag.ValueKind == JsonValueKind.True;
            return new McpToolOutcome { IsError = isError, Text = text.ToString() };
        }

        private static JsonElement Result(JsonRpcResponse response)
        {
            MuxAssert.IsNull(response.Error, "JSON-RPC error: " + response.Error?.Message);
            using (JsonDocument doc = JsonDocument.Parse(JsonSerializer.Serialize(response.Result)))
            {
                return doc.RootElement.Clone();
            }
        }

        private static List<string> Sorted(List<string> names)
        {
            List<string> copy = new List<string>(names);
            copy.Sort(StringComparer.Ordinal);
            return copy;
        }

        // Drives `mux mcp serve` over raw stdio: initialize, initialized, one run call, then closes stdin and returns
        // everything the server wrote to stderr.
        private static async Task<string> RunStdioSessionAsync(string cliDll, string configDir, bool logMessages, CancellationToken ct)
        {
            System.Diagnostics.ProcessStartInfo info = new System.Diagnostics.ProcessStartInfo("dotnet")
            {
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            foreach (string argument in new[] { cliDll, "mcp", "serve", "--config-dir", configDir, "--working-directory", configDir }) info.ArgumentList.Add(argument);
            if (logMessages) info.ArgumentList.Add("--log-messages");
            using (System.Diagnostics.Process process = System.Diagnostics.Process.Start(info)!)
            {
                Task<string> stderr = process.StandardError.ReadToEndAsync();
                string initialize = JsonSerializer.Serialize(new { jsonrpc = "2.0", id = 1, method = "initialize", @params = new { protocolVersion = McpProtocol.LatestProtocolVersion, capabilities = new { }, clientInfo = new { name = "test", version = "1" } } });
                await process.StandardInput.WriteLineAsync(initialize).ConfigureAwait(false);
                await process.StandardInput.FlushAsync().ConfigureAwait(false);
                await ReadUntilIdAsync(process, 1, ct).ConfigureAwait(false);
                await process.StandardInput.WriteLineAsync("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}").ConfigureAwait(false);
                string call = JsonSerializer.Serialize(new { jsonrpc = "2.0", id = 2, method = "tools/call", @params = new { name = "run", arguments = new { prompt = "PRIVATE PROMPT TEXT stdio log check" } } });
                await process.StandardInput.WriteLineAsync(call).ConfigureAwait(false);
                await process.StandardInput.FlushAsync().ConfigureAwait(false);
                await ReadUntilIdAsync(process, 2, ct).ConfigureAwait(false);
                process.StandardInput.Close();
                if (!process.WaitForExit(20000)) { try { process.Kill(true); } catch (Exception) { } }
                return await stderr.ConfigureAwait(false);
            }
        }

        private static async Task ReadUntilIdAsync(System.Diagnostics.Process process, int id, CancellationToken ct)
        {
            using (CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(60));
                while (true)
                {
                    string? line = await process.StandardOutput.ReadLineAsync(timeout.Token).ConfigureAwait(false);
                    if (line == null) throw new InvalidOperationException("the server closed stdout before answering id " + id);
                    if (line.Contains("\"id\":" + id + ",", StringComparison.Ordinal) || line.EndsWith("\"id\":" + id + "}", StringComparison.Ordinal)) return;
                }
            }
        }

        private static string ConfigDir(string modelBaseUrl)
        {
            string dir = Path.Combine(Path.GetTempPath(), "mux-mcpserve-cfg-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            string endpoints = JsonSerializer.Serialize(new
            {
                endpoints = new object[]
                {
                    new { name = "mock", adapterType = "openai-compatible", baseUrl = modelBaseUrl, model = "test-model", isDefault = true, apiKey = "sk-CHILDSECRET" }
                }
            });
            File.WriteAllText(Path.Combine(dir, "endpoints.json"), endpoints);
            File.WriteAllText(Path.Combine(dir, "settings.json"), "{\"skillsEnabled\":false,\"projectInstructionsEnabled\":false}");
            return dir;
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
