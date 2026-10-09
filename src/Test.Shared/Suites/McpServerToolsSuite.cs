namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Enums;
    using Mux.Core.McpServer;
    using Mux.Core.Models;
    using Mux.Core.Sessions;
    using Mux.Core.Skills;
    using Test.Shared.Support;
    using Touchstone.Core;
    using Voltaic.Core;
    using Voltaic.Mcp;

    /// <summary>
    /// Touchstone suite for the tools mux exposes as an MCP server, invoked directly through the handlers it registers:
    /// registration and schemas, every argument check of <c>run</c>, <c>list_sessions</c>, <c>get_session</c>,
    /// <c>list_endpoints</c>, <c>list_skills</c>, and <c>run_skill</c> (missing, wrong types, out of range, unsafe
    /// ids), the approval-policy ceiling matrix, run serialization with read-only tools never blocked, cancellation and
    /// executor failures releasing the run lock, secret masking in every shape, skills listing and execution, and the
    /// bearer check.
    /// </summary>
    public static class McpServerToolsSuite
    {
        #region Private-Members

        private const string SuiteId = "McpServerTools";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Builds the MCP server tools suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/> for the server tool cases.</returns>
        public static TestSuiteDescriptor Create()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();
            void Add(string id, string name, Func<string, CancellationToken, Task> body)
            {
                cases.Add(new TestCaseDescriptor(SuiteId, id, name, (CancellationToken ct) => WithTempAsync((string root) => body(root, ct))));
            }

            // --- registration ---
            Add("RegistersEveryToolWithObjectSchema", "Every tool registers with a description and an object input schema", (string root, CancellationToken ct) =>
            {
                Dictionary<string, RegisteredMcpTool> tools = Register(new MuxMcpServerOptions { AllowSkills = true }, new FakeMcpRunExecutor(), new List<EndpointConfig>(), Store(root), null);
                MuxAssert.AreEqual("get_session,list_endpoints,list_sessions,list_skills,run,run_skill", string.Join(",", Sorted(tools.Keys)), "six tools with --allow-skills");
                foreach (KeyValuePair<string, RegisteredMcpTool> tool in tools)
                {
                    MuxAssert.IsTrue(tool.Value.Description.Length > 20, tool.Key + " has a description");
                    string schema = JsonSerializer.Serialize(tool.Value.Schema);
                    MuxAssert.Contains("\"type\":\"object\"", schema, tool.Key + " schema is an object");
                }

                MuxAssert.Contains("\"required\":[\"prompt\"]", JsonSerializer.Serialize(tools["run"].Schema), "run requires prompt");
                MuxAssert.Contains("\"required\":[\"id\"]", JsonSerializer.Serialize(tools["get_session"].Schema), "get_session requires id");
                MuxAssert.Contains("deny", tools["run"].Description, "run names its ceiling");
                return Task.CompletedTask;
            });

            // --- run ---
            Add("RunRequiresStringPrompt", "run refuses a missing, null, numeric, or array prompt", async (string root, CancellationToken ct) =>
            {
                Dictionary<string, RegisteredMcpTool> tools = Register(new MuxMcpServerOptions(), new FakeMcpRunExecutor(), new List<EndpointConfig>(), Store(root), null, root);
                MuxAssert.Contains("prompt is required", await Error(tools["run"], "{}", ct).ConfigureAwait(false), "missing");
                MuxAssert.Contains("prompt is required", await Error(tools["run"], "{\"prompt\":null}", ct).ConfigureAwait(false), "null");
                MuxAssert.Contains("prompt must be a string", await Error(tools["run"], "{\"prompt\":5}", ct).ConfigureAwait(false), "number");
                MuxAssert.Contains("prompt must be a string", await Error(tools["run"], "{\"prompt\":[\"a\"]}", ct).ConfigureAwait(false), "array");
            });
            Add("RunRejectsNonObjectArguments", "Arguments that are not a JSON object or not JSON at all are refused", async (string root, CancellationToken ct) =>
            {
                Dictionary<string, RegisteredMcpTool> tools = Register(new MuxMcpServerOptions(), new FakeMcpRunExecutor(), new List<EndpointConfig>(), Store(root), null, root);
                MuxAssert.Contains("must be a JSON object", await Error(tools["run"], "[1,2]", ct).ConfigureAwait(false), "array");
                MuxAssert.Contains("must be a JSON object", await Error(tools["run"], "\"text\"", ct).ConfigureAwait(false), "string");
                MuxAssert.Contains("not valid JSON", await Error(tools["run"], "{broken", ct).ConfigureAwait(false), "invalid JSON");
            });
            Add("RunMaxTurnsBounds", "max_turns must be a whole number from 1 to 200", async (string root, CancellationToken ct) =>
            {
                FakeMcpRunExecutor executor = new FakeMcpRunExecutor();
                Dictionary<string, RegisteredMcpTool> tools = Register(new MuxMcpServerOptions(), executor, new List<EndpointConfig>(), Store(root), null, root);
                foreach (string bad in new[] { "0", "201", "-3", "\"ten\"", "1.5", "true" })
                {
                    MuxAssert.Contains("max_turns must be a whole number from 1 to 200", await Error(tools["run"], "{\"prompt\":\"p\",\"max_turns\":" + bad + "}", ct).ConfigureAwait(false), "rejects " + bad);
                }

                await Ok(tools["run"], "{\"prompt\":\"p\",\"max_turns\":1}", ct).ConfigureAwait(false);
                await Ok(tools["run"], "{\"prompt\":\"p\",\"max_turns\":200}", ct).ConfigureAwait(false);
                MuxAssert.AreEqual(1, executor.Requests[0].MaxTurns ?? 0, "lower bound passed");
                MuxAssert.AreEqual(200, executor.Requests[1].MaxTurns ?? 0, "upper bound passed");
            });
            Add("RunWorkingDirectoryResolution", "working_directory resolves relative to the default and must exist", async (string root, CancellationToken ct) =>
            {
                Directory.CreateDirectory(Path.Combine(root, "sub"));
                FakeMcpRunExecutor executor = new FakeMcpRunExecutor();
                Dictionary<string, RegisteredMcpTool> tools = Register(new MuxMcpServerOptions(), executor, new List<EndpointConfig>(), Store(root), null, root);
                await Ok(tools["run"], "{\"prompt\":\"p\",\"working_directory\":\"sub\"}", ct).ConfigureAwait(false);
                MuxAssert.AreEqual(Path.GetFullPath(Path.Combine(root, "sub")), executor.Requests[0].WorkingDirectory, "relative resolved");
                await Ok(tools["run"], "{\"prompt\":\"p\",\"working_directory\":" + JsonSerializer.Serialize(Path.Combine(root, "sub")) + "}", ct).ConfigureAwait(false);
                MuxAssert.AreEqual(Path.GetFullPath(Path.Combine(root, "sub")), executor.Requests[1].WorkingDirectory, "absolute kept");
                MuxAssert.Contains("does not exist", await Error(tools["run"], "{\"prompt\":\"p\",\"working_directory\":\"missing\"}", ct).ConfigureAwait(false), "missing refused");
                MuxAssert.Contains("working_directory must be a string", await Error(tools["run"], "{\"prompt\":\"p\",\"working_directory\":7}", ct).ConfigureAwait(false), "wrong type refused");
            });
            Add("RunEndpointDefaultAndOverride", "run uses the server's default endpoint unless the call names one", async (string root, CancellationToken ct) =>
            {
                FakeMcpRunExecutor executor = new FakeMcpRunExecutor();
                Dictionary<string, RegisteredMcpTool> tools = Register(new MuxMcpServerOptions { DefaultEndpoint = "server-default" }, executor, new List<EndpointConfig>(), Store(root), null, root);
                await Ok(tools["run"], "{\"prompt\":\"p\"}", ct).ConfigureAwait(false);
                await Ok(tools["run"], "{\"prompt\":\"p\",\"endpoint\":\"chosen\"}", ct).ConfigureAwait(false);
                MuxAssert.AreEqual("server-default", executor.Requests[0].Endpoint, "default used");
                MuxAssert.AreEqual("chosen", executor.Requests[1].Endpoint, "override used");
                MuxAssert.Contains("endpoint must be a string", await Error(tools["run"], "{\"prompt\":\"p\",\"endpoint\":{}}", ct).ConfigureAwait(false), "wrong type");
            });
            Add("RunIgnoresUnknownArguments", "Unknown extra arguments to run are ignored", async (string root, CancellationToken ct) =>
            {
                FakeMcpRunExecutor executor = new FakeMcpRunExecutor();
                Dictionary<string, RegisteredMcpTool> tools = Register(new MuxMcpServerOptions(), executor, new List<EndpointConfig>(), Store(root), null, root);
                string result = await Ok(tools["run"], "{\"prompt\":\"hello\",\"color\":\"blue\",\"nested\":{\"a\":1}}", ct).ConfigureAwait(false);
                MuxAssert.Contains("ANSWER: hello", result, "ran");
            });
            Add("RunReturnsFullSummary", "run returns every summary field, including the executor's error message", async (string root, CancellationToken ct) =>
            {
                Dictionary<string, RegisteredMcpTool> tools = Register(new MuxMcpServerOptions(), new FakeMcpRunExecutor(), new List<EndpointConfig>(), Store(root), null, root);
                using (JsonDocument doc = JsonDocument.Parse(await Ok(tools["run"], "{\"prompt\":\"sum\"}", ct).ConfigureAwait(false)))
                {
                    foreach (string field in new[] { "answer", "status", "endpoint", "model", "approval_policy", "working_directory", "iterations", "tool_calls", "errors", "duration_ms", "input_tokens", "output_tokens" })
                    {
                        MuxAssert.IsTrue(doc.RootElement.TryGetProperty(field, out _), "field " + field);
                    }

                    MuxAssert.AreEqual("deny", doc.RootElement.GetProperty("approval_policy").GetString(), "deny ceiling by default");
                    MuxAssert.AreEqual(10, doc.RootElement.GetProperty("input_tokens").GetInt32(), "tokens");
                }
            });
            Add("PolicyCeilingMatrix", "Every requested policy against every ceiling is allowed or refused correctly", (string root, CancellationToken ct) =>
            {
                ApprovalPolicyEnum[] ceilings = { ApprovalPolicyEnum.Deny, ApprovalPolicyEnum.AutoSafe, ApprovalPolicyEnum.AutoApprove };
                string[] requests = { "deny", "auto-safe", "auto" };
                for (int c = 0; c < ceilings.Length; c++)
                {
                    for (int r = 0; r < requests.Length; r++)
                    {
                        if (r <= c)
                        {
                            ApprovalPolicyEnum effective = MuxMcpTools.ResolvePolicy(requests[r], ceilings[c]);
                            MuxAssert.AreEqual(ceilings[r], effective, requests[r] + " under " + ceilings[c]);
                        }
                        else
                        {
                            McpToolException error = MuxAssert.Throws<McpToolException>(() => MuxMcpTools.ResolvePolicy(requests[r], ceilings[c]), requests[r] + " refused under " + ceilings[c]);
                            MuxAssert.Contains("more permissive", error.Message, "explains");
                        }
                    }

                    MuxAssert.AreEqual(ceilings[c], MuxMcpTools.ResolvePolicy(null, ceilings[c]), "blank means the ceiling");
                    MuxAssert.AreEqual(ceilings[c], MuxMcpTools.ResolvePolicy("  ", ceilings[c]), "whitespace means the ceiling");
                    MuxAssert.Contains("'ask' is not available", MuxAssert.Throws<McpToolException>(() => MuxMcpTools.ResolvePolicy("ask", ceilings[c]), "ask refused").Message, "ask explained");
                }

                foreach (string spelling in new[] { "AUTO-SAFE", "auto_safe", "autosafe", " Auto-Safe " }) MuxAssert.AreEqual(ApprovalPolicyEnum.AutoSafe, MuxMcpTools.ResolvePolicy(spelling, ApprovalPolicyEnum.AutoApprove), spelling);
                foreach (string spelling in new[] { "yolo", "auto-approve", "AUTO" }) MuxAssert.AreEqual(ApprovalPolicyEnum.AutoApprove, MuxMcpTools.ResolvePolicy(spelling, ApprovalPolicyEnum.AutoApprove), spelling);
                MuxAssert.Throws<McpToolException>(() => MuxMcpTools.ResolvePolicy("sometimes", ApprovalPolicyEnum.AutoApprove), "unknown refused");
                return Task.CompletedTask;
            });
            Add("RunPolicyRefusedThroughTool", "A run asking for more than the ceiling is refused before the executor runs", async (string root, CancellationToken ct) =>
            {
                FakeMcpRunExecutor executor = new FakeMcpRunExecutor();
                Dictionary<string, RegisteredMcpTool> tools = Register(new MuxMcpServerOptions { MaxApprovalPolicy = ApprovalPolicyEnum.AutoSafe }, executor, new List<EndpointConfig>(), Store(root), null, root);
                MuxAssert.Contains("more permissive", await Error(tools["run"], "{\"prompt\":\"p\",\"approval_policy\":\"auto\"}", ct).ConfigureAwait(false), "refused");
                MuxAssert.AreEqual(0, executor.Requests.Count, "the executor never ran");
                await Ok(tools["run"], "{\"prompt\":\"p\",\"approval_policy\":\"deny\"}", ct).ConfigureAwait(false);
                MuxAssert.AreEqual(ApprovalPolicyEnum.Deny, executor.Requests[0].ApprovalPolicy, "stricter allowed");
            });
            Add("RunsAreSerializedReadOnlyToolsAreNot", "Two runs never overlap, while read-only tools answer during a run", async (string root, CancellationToken ct) =>
            {
                GatedMcpRunExecutor executor = new GatedMcpRunExecutor();
                Dictionary<string, RegisteredMcpTool> tools = Register(new MuxMcpServerOptions(), executor, new List<EndpointConfig> { new EndpointConfig { Name = "e1", BaseUrl = "http://h", Model = "m" } }, Store(root), null, root);
                Task<object> first = tools["run"].Handler(new RpcParameters("{\"prompt\":\"one\"}"), ct);
                MuxAssert.IsTrue(await executor.WaitStartedAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false), "first run started");
                Task<object> second = tools["run"].Handler(new RpcParameters("{\"prompt\":\"two\"}"), ct);
                MuxAssert.IsFalse(await executor.WaitStartedAsync(TimeSpan.FromMilliseconds(300)).ConfigureAwait(false), "the second run waits for the first");
                string endpoints = (string)await tools["list_endpoints"].Handler(new RpcParameters("{}"), ct).WaitAsync(TimeSpan.FromSeconds(5), ct).ConfigureAwait(false);
                MuxAssert.Contains("e1", endpoints, "a read-only tool answered during the run");
                executor.ReleaseOne();
                MuxAssert.Contains("done: one", (string)await first.WaitAsync(TimeSpan.FromSeconds(10), ct).ConfigureAwait(false), "first finished");
                MuxAssert.IsTrue(await executor.WaitStartedAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false), "second started after the first");
                executor.ReleaseOne();
                MuxAssert.Contains("done: two", (string)await second.WaitAsync(TimeSpan.FromSeconds(10), ct).ConfigureAwait(false), "second finished");
                MuxAssert.AreEqual(1, executor.MaxActive, "never more than one at a time");
            });
            Add("CancelledRunReleasesLock", "Cancelling a run releases the run lock for the next caller", async (string root, CancellationToken ct) =>
            {
                GatedMcpRunExecutor executor = new GatedMcpRunExecutor();
                Dictionary<string, RegisteredMcpTool> tools = Register(new MuxMcpServerOptions(), executor, new List<EndpointConfig>(), Store(root), null, root);
                using (CancellationTokenSource cancel = new CancellationTokenSource())
                {
                    Task<object> doomed = tools["run"].Handler(new RpcParameters("{\"prompt\":\"one\"}"), cancel.Token);
                    MuxAssert.IsTrue(await executor.WaitStartedAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false), "started");
                    cancel.Cancel();
                    await MuxAssert.ThrowsAsync<OperationCanceledException>(() => doomed, "cancelled").ConfigureAwait(false);
                }

                Task<object> next = tools["run"].Handler(new RpcParameters("{\"prompt\":\"two\"}"), ct);
                MuxAssert.IsTrue(await executor.WaitStartedAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false), "the next run starts");
                executor.ReleaseOne();
                MuxAssert.Contains("done: two", (string)await next.WaitAsync(TimeSpan.FromSeconds(10), ct).ConfigureAwait(false), "and finishes");
            });
            Add("CancelledWhileWaitingForLock", "A run cancelled while waiting for the lock never starts", async (string root, CancellationToken ct) =>
            {
                GatedMcpRunExecutor executor = new GatedMcpRunExecutor();
                Dictionary<string, RegisteredMcpTool> tools = Register(new MuxMcpServerOptions(), executor, new List<EndpointConfig>(), Store(root), null, root);
                Task<object> holder = tools["run"].Handler(new RpcParameters("{\"prompt\":\"one\"}"), ct);
                MuxAssert.IsTrue(await executor.WaitStartedAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false), "first started");
                using (CancellationTokenSource cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(200)))
                {
                    await MuxAssert.ThrowsAsync<OperationCanceledException>(() => tools["run"].Handler(new RpcParameters("{\"prompt\":\"never\"}"), cancel.Token), "waiting run cancelled").ConfigureAwait(false);
                }

                executor.ReleaseOne();
                await holder.WaitAsync(TimeSpan.FromSeconds(10), ct).ConfigureAwait(false);
                MuxAssert.AreEqual(1, executor.Completed, "only the first run ever executed");
            });
            Add("ExecutorFailureReleasesLock", "An executor that throws fails the call and the lock is released", async (string root, CancellationToken ct) =>
            {
                GatedMcpRunExecutor executor = new GatedMcpRunExecutor { ThrowMessage = "model server down" };
                Dictionary<string, RegisteredMcpTool> tools = Register(new MuxMcpServerOptions(), executor, new List<EndpointConfig>(), Store(root), null, root);
                Task<object> failing = tools["run"].Handler(new RpcParameters("{\"prompt\":\"one\"}"), ct);
                await executor.WaitStartedAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                executor.ReleaseOne();
                InvalidOperationException error = await MuxAssert.ThrowsAsync<InvalidOperationException>(() => failing, "failure surfaces").ConfigureAwait(false);
                MuxAssert.Contains("model server down", error.Message, "message kept");
                executor.ThrowMessage = null;
                Task<object> next = tools["run"].Handler(new RpcParameters("{\"prompt\":\"two\"}"), ct);
                MuxAssert.IsTrue(await executor.WaitStartedAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false), "the lock was released");
                executor.ReleaseOne();
                await next.WaitAsync(TimeSpan.FromSeconds(10), ct).ConfigureAwait(false);
            });

            Add("BlankStringsCountAsMissing", "Blank or whitespace strings count as missing for required arguments", async (string root, CancellationToken ct) =>
            {
                Dictionary<string, RegisteredMcpTool> tools = Register(new MuxMcpServerOptions { AllowSkills = true }, new FakeMcpRunExecutor(), new List<EndpointConfig>(), Store(root), null, root);
                MuxAssert.Contains("prompt is required", await Error(tools["run"], "{\"prompt\":\"   \"}", ct).ConfigureAwait(false), "blank prompt");
                MuxAssert.Contains("prompt is required", await Error(tools["run"], "", ct).ConfigureAwait(false), "no arguments at all means an empty object");
                MuxAssert.Contains("id is required", await Error(tools["get_session"], "{\"id\":\"\"}", ct).ConfigureAwait(false), "blank id");
            });
            Add("EmptyCatalogsAnswer", "With no endpoints or sessions the read-only tools answer with empty lists, ignoring unknown arguments", async (string root, CancellationToken ct) =>
            {
                Dictionary<string, RegisteredMcpTool> tools = Register(new MuxMcpServerOptions(), new FakeMcpRunExecutor(), new List<EndpointConfig>(), Store(root), null, root);
                MuxAssert.AreEqual("{\"endpoints\":[]}", await Ok(tools["list_endpoints"], "{\"unused\":1}", ct).ConfigureAwait(false), "no endpoints");
                MuxAssert.Contains("\"sessions\":[]", await Ok(tools["list_sessions"], "{\"unused\":true}", ct).ConfigureAwait(false), "no sessions");
            });

            // --- sessions ---
            Add("ListSessionsEmptyAndLimits", "list_sessions handles an empty store, defaults to 20, honors limit, and checks its range", async (string root, CancellationToken ct) =>
            {
                SessionStore store = Store(root);
                Dictionary<string, RegisteredMcpTool> tools = Register(new MuxMcpServerOptions(), new FakeMcpRunExecutor(), new List<EndpointConfig>(), store, null, root);
                using (JsonDocument empty = JsonDocument.Parse(await Ok(tools["list_sessions"], "{}", ct).ConfigureAwait(false)))
                {
                    MuxAssert.AreEqual(0, empty.RootElement.GetProperty("total").GetInt32(), "empty");
                }

                for (int i = 0; i < 25; i++) await SaveSession(store, "s" + i.ToString("00", System.Globalization.CultureInfo.InvariantCulture), "Session " + i, 2, DateTime.UtcNow.AddMinutes(i), ct).ConfigureAwait(false);
                using (JsonDocument doc = JsonDocument.Parse(await Ok(tools["list_sessions"], "{}", ct).ConfigureAwait(false)))
                {
                    MuxAssert.AreEqual(25, doc.RootElement.GetProperty("total").GetInt32(), "total counts all");
                    MuxAssert.AreEqual(20, doc.RootElement.GetProperty("sessions").GetArrayLength(), "default limit 20");
                    MuxAssert.AreEqual("s24", doc.RootElement.GetProperty("sessions")[0].GetProperty("id").GetString(), "newest first");
                }

                using (JsonDocument three = JsonDocument.Parse(await Ok(tools["list_sessions"], "{\"limit\":3}", ct).ConfigureAwait(false)))
                {
                    MuxAssert.AreEqual(3, three.RootElement.GetProperty("sessions").GetArrayLength(), "limit 3");
                    MuxAssert.AreEqual(2, three.RootElement.GetProperty("sessions")[0].GetProperty("messages").GetInt32(), "message count");
                }

                foreach (string bad in new[] { "0", "201", "\"5\"", "2.5" }) MuxAssert.Contains("limit must be a whole number from 1 to 200", await Error(tools["list_sessions"], "{\"limit\":" + bad + "}", ct).ConfigureAwait(false), "rejects " + bad);
            });
            Add("GetSessionValidation", "get_session needs a safe, known id and checks max_messages", async (string root, CancellationToken ct) =>
            {
                SessionStore store = Store(root);
                await SaveSession(store, "real", "Real", 3, DateTime.UtcNow, ct).ConfigureAwait(false);
                Dictionary<string, RegisteredMcpTool> tools = Register(new MuxMcpServerOptions(), new FakeMcpRunExecutor(), new List<EndpointConfig>(), store, null, root);
                MuxAssert.Contains("id is required", await Error(tools["get_session"], "{}", ct).ConfigureAwait(false), "missing id");
                MuxAssert.Contains("id must be a string", await Error(tools["get_session"], "{\"id\":12}", ct).ConfigureAwait(false), "numeric id");
                foreach (string unsafeId in new[] { "../real", "..", "a/b", "a\\\\b", "nul\\u0000l" })
                {
                    MuxAssert.Contains("No session has the id", await Error(tools["get_session"], "{\"id\":\"" + unsafeId + "\"}", ct).ConfigureAwait(false), "refuses " + unsafeId);
                }

                MuxAssert.Contains("Use list_sessions", await Error(tools["get_session"], "{\"id\":\"ghost\"}", ct).ConfigureAwait(false), "unknown id points at list_sessions");
                foreach (string bad in new[] { "0", "501", "\"9\"" }) MuxAssert.Contains("max_messages must be a whole number from 1 to 500", await Error(tools["get_session"], "{\"id\":\"real\",\"max_messages\":" + bad + "}", ct).ConfigureAwait(false), "rejects " + bad);
                using (JsonDocument doc = JsonDocument.Parse(await Ok(tools["get_session"], "{\"id\":\"real\"}", ct).ConfigureAwait(false)))
                {
                    MuxAssert.AreEqual("Real", doc.RootElement.GetProperty("title").GetString(), "title");
                    MuxAssert.AreEqual(3, doc.RootElement.GetProperty("total_messages").GetInt32(), "total");
                    MuxAssert.AreEqual("user", doc.RootElement.GetProperty("messages")[0].GetProperty("role").GetString(), "lowercase roles");
                }
            });
            Add("GetSessionNewestAndTruncation", "get_session returns the newest messages and cuts very long ones", async (string root, CancellationToken ct) =>
            {
                SessionStore store = Store(root);
                SessionSnapshot snapshot = new SessionSnapshot { Id = "long", Title = "Long", UpdatedUtc = DateTime.UtcNow };
                for (int i = 0; i < 10; i++) snapshot.ConversationHistory.Add(new ConversationMessage { Role = i % 2 == 0 ? RoleEnum.User : RoleEnum.Assistant, Content = "m" + i });
                snapshot.ConversationHistory.Add(new ConversationMessage { Role = RoleEnum.Assistant, Content = new string('q', 9000) });
                await store.SaveAsync(snapshot, ct).ConfigureAwait(false);
                Dictionary<string, RegisteredMcpTool> tools = Register(new MuxMcpServerOptions(), new FakeMcpRunExecutor(), new List<EndpointConfig>(), store, null, root);
                using (JsonDocument doc = JsonDocument.Parse(await Ok(tools["get_session"], "{\"id\":\"long\",\"max_messages\":2}", ct).ConfigureAwait(false)))
                {
                    JsonElement messages = doc.RootElement.GetProperty("messages");
                    MuxAssert.AreEqual(2, messages.GetArrayLength(), "two newest");
                    MuxAssert.AreEqual("m9", messages[0].GetProperty("content").GetString(), "the second newest");
                    string last = messages[1].GetProperty("content").GetString()!;
                    MuxAssert.Contains("[mux: message cut at 8000 characters]", last, "long message cut");
                    MuxAssert.IsTrue(last.Length < 8100, "cut length");
                    MuxAssert.AreEqual(11, doc.RootElement.GetProperty("total_messages").GetInt32(), "total still counts all");
                }
            });

            // --- endpoints ---
            Add("EndpointsNeverLeakSecrets", "list_endpoints omits API keys and headers and masks URL credentials", async (string root, CancellationToken ct) =>
            {
                List<EndpointConfig> endpoints = new List<EndpointConfig>
                {
                    new EndpointConfig { Name = "keyed", BaseUrl = "https://api.example.com/v1", Model = "m1", ApiKey = "sk-SECRET-ONE", IsDefault = true },
                    new EndpointConfig { Name = "basic", BaseUrl = "https://user:PASS-TWO@host.example.com/v1", Model = "m2" },
                    new EndpointConfig { Name = "query", BaseUrl = "https://host.example.com/v1?key=QUERY-THREE&region=us", Model = "m3" }
                };
                endpoints[0].Headers["X-Secret"] = "HEADER-FOUR";
                Dictionary<string, RegisteredMcpTool> tools = Register(new MuxMcpServerOptions { DefaultEndpoint = "basic" }, new FakeMcpRunExecutor(), endpoints, Store(root), null, root);
                string text = await Ok(tools["list_endpoints"], "{}", ct).ConfigureAwait(false);
                foreach (string secret in new[] { "sk-SECRET-ONE", "PASS-TWO", "QUERY-THREE", "HEADER-FOUR", "X-Secret" })
                {
                    MuxAssert.DoesNotContain(secret, text, "never shows " + secret);
                }

                MuxAssert.Contains("key=***", text, "query value masked");
                MuxAssert.Contains("region=***", text, "every query value masked");
                MuxAssert.Contains("***@host.example.com", text, "user info masked");
                using (JsonDocument doc = JsonDocument.Parse(text))
                {
                    JsonElement rows = doc.RootElement.GetProperty("endpoints");
                    MuxAssert.AreEqual(3, rows.GetArrayLength(), "three endpoints");
                    MuxAssert.IsTrue(rows[0].GetProperty("is_default").GetBoolean(), "default flag");
                    MuxAssert.IsTrue(rows[1].GetProperty("is_server_default").GetBoolean(), "server default flag");
                    MuxAssert.IsFalse(rows[0].GetProperty("is_server_default").GetBoolean(), "only one server default");
                }
            });
            Add("MaskUrlShapes", "MaskUrl leaves clean URLs alone and masks user info and query values in every shape", (string root, CancellationToken ct) =>
            {
                MuxAssert.AreEqual("https://host.example.com", MuxMcpTools.MaskUrl("https://host.example.com"), "no trailing slash added");
                MuxAssert.AreEqual("https://host.example.com/", MuxMcpTools.MaskUrl("https://host.example.com/"), "trailing slash kept");
                MuxAssert.AreEqual("https://host.example.com/v1/x", MuxMcpTools.MaskUrl("https://host.example.com/v1/x"), "path kept");
                MuxAssert.Contains("***@", MuxMcpTools.MaskUrl("http://onlyuser@h.example.com/"), "user without password masked");
                MuxAssert.DoesNotContain("pw", MuxMcpTools.MaskUrl("http://u:pw@h.example.com/"), "password removed");
                MuxAssert.Contains("flag", MuxMcpTools.MaskUrl("http://h.example.com/?flag"), "a valueless query key is kept");
                MuxAssert.AreEqual("not a url", MuxMcpTools.MaskUrl("not a url"), "non-URLs pass through");
                MuxAssert.AreEqual(string.Empty, MuxMcpTools.MaskUrl(null), "null becomes empty");
                MuxAssert.AreEqual("  ", MuxMcpTools.MaskUrl("  "), "blank passes through");
                return Task.CompletedTask;
            });

            // --- skills ---
            Add("SkillsDisabledWithoutRuntime", "Without a skills runtime list_skills says disabled and run_skill explains why", async (string root, CancellationToken ct) =>
            {
                Dictionary<string, RegisteredMcpTool> tools = Register(new MuxMcpServerOptions { AllowSkills = true }, new FakeMcpRunExecutor(), new List<EndpointConfig>(), Store(root), null, root);
                using (JsonDocument doc = JsonDocument.Parse(await Ok(tools["list_skills"], "{}", ct).ConfigureAwait(false)))
                {
                    MuxAssert.IsFalse(doc.RootElement.GetProperty("enabled").GetBoolean(), "disabled");
                    MuxAssert.AreEqual(0, doc.RootElement.GetProperty("skills").GetArrayLength(), "none");
                }

                MuxAssert.Contains("skillsEnabled", await Error(tools["run_skill"], "{\"name\":\"x\",\"command\":\"y\"}", ct).ConfigureAwait(false), "config explained");
            });
            Add("RunSkillGatedWithoutFlag", "Without --allow-skills run_skill is not registered, and the handler refuses if reached", async (string root, CancellationToken ct) =>
            {
                Dictionary<string, RegisteredMcpTool> without = Register(new MuxMcpServerOptions(), new FakeMcpRunExecutor(), new List<EndpointConfig>(), Store(root), null, root);
                MuxAssert.IsFalse(without.ContainsKey("run_skill"), "not registered");
                using (SkillRuntime skills = await SkillsAsync(root, ct).ConfigureAwait(false))
                {
                    MuxMcpTools tools = new MuxMcpTools(new MuxMcpServerOptions(), new FakeMcpRunExecutor(), () => new List<EndpointConfig>(), Store(root), skills);
                    using (JsonDocument doc = JsonDocument.Parse(await Ok(Capture(tools)["list_skills"], "{}", ct).ConfigureAwait(false)))
                    {
                        MuxAssert.IsFalse(doc.RootElement.GetProperty("run_skill_allowed").GetBoolean(), "listing says run_skill is not allowed");
                    }
                }
            });
            Add("SkillsListedAndRun", "list_skills lists skills with commands, and run_skill executes one with arguments", async (string root, CancellationToken ct) =>
            {
                using (SkillRuntime skills = await SkillsAsync(root, ct).ConfigureAwait(false))
                {
                    Dictionary<string, RegisteredMcpTool> tools = Capture(new MuxMcpTools(new MuxMcpServerOptions { AllowSkills = true, DefaultWorkingDirectory = root }, new FakeMcpRunExecutor(), () => new List<EndpointConfig>(), Store(root), skills));
                    string listed = await Ok(tools["list_skills"], "{}", ct).ConfigureAwait(false);
                    MuxAssert.Contains("\"name\":\"greeter\"", listed, "skill listed");
                    MuxAssert.Contains("\"hello\"", listed, "command listed");
                    MuxAssert.Contains("\"run_skill_allowed\":true", listed, "run_skill allowed");
                    string ran = await Ok(tools["run_skill"], "{\"name\":\"greeter\",\"command\":\"hello\",\"args\":[\"World\"]}", ct).ConfigureAwait(false);
                    MuxAssert.Contains("Hello, World", ran, "skill output returned: " + ran);
                }
            });
            Add("RunSkillArgumentChecks", "run_skill checks name, command, args shape, unknown skills, and the working directory", async (string root, CancellationToken ct) =>
            {
                using (SkillRuntime skills = await SkillsAsync(root, ct).ConfigureAwait(false))
                {
                    Dictionary<string, RegisteredMcpTool> tools = Capture(new MuxMcpTools(new MuxMcpServerOptions { AllowSkills = true, DefaultWorkingDirectory = root }, new FakeMcpRunExecutor(), () => new List<EndpointConfig>(), Store(root), skills));
                    MuxAssert.Contains("name is required", await Error(tools["run_skill"], "{\"command\":\"hello\"}", ct).ConfigureAwait(false), "missing name");
                    MuxAssert.Contains("command is required", await Error(tools["run_skill"], "{\"name\":\"greeter\"}", ct).ConfigureAwait(false), "missing command");
                    MuxAssert.Contains("args must be an array of strings", await Error(tools["run_skill"], "{\"name\":\"greeter\",\"command\":\"hello\",\"args\":\"World\"}", ct).ConfigureAwait(false), "args not an array");
                    MuxAssert.Contains("args must be an array of strings", await Error(tools["run_skill"], "{\"name\":\"greeter\",\"command\":\"hello\",\"args\":[1]}", ct).ConfigureAwait(false), "non-string arg");
                    MuxAssert.Contains("No enabled skill named 'ghost'", await Error(tools["run_skill"], "{\"name\":\"ghost\",\"command\":\"x\"}", ct).ConfigureAwait(false), "unknown skill");
                    MuxAssert.Contains("does not exist", await Error(tools["run_skill"], "{\"name\":\"greeter\",\"command\":\"hello\",\"working_directory\":\"nope\"}", ct).ConfigureAwait(false), "missing directory");
                    MuxAssert.Contains("does not exist", await Error(tools["list_skills"], "{\"working_directory\":\"nope\"}", ct).ConfigureAwait(false), "list_skills checks the directory too");
                    string unknownCommand = await Ok(tools["run_skill"], "{\"name\":\"greeter\",\"command\":\"frobnicate\"}", ct).ConfigureAwait(false);
                    MuxAssert.Contains("command_not_found", unknownCommand, "an unknown command is reported by the skill runtime");
                }
            });

            // --- auth ---
            Add("BearerCheckCases", "The bearer check accepts only 'Bearer <exact key>' and challenges everything else", (string root, CancellationToken ct) =>
            {
                MuxAssert.IsTrue(MuxMcpServerHost.Authenticate("Bearer k3y", "k3y").IsAuthenticated, "exact key");
                MuxAssert.IsTrue(MuxMcpServerHost.Authenticate("bearer k3y", "k3y").IsAuthenticated, "scheme is case-insensitive");
                MuxAssert.IsTrue(MuxMcpServerHost.Authenticate("Bearer   k3y  ", "k3y").IsAuthenticated, "surrounding spaces trimmed");
                foreach (string? bad in new[] { null, string.Empty, "k3y", "Basic k3y", "Bearer", "Bearer ", "Bearer K3Y", "Bearer k3y-extra", "Bearer k3", "Token k3y" })
                {
                    AuthenticationResult result = MuxMcpServerHost.Authenticate(bad, "k3y");
                    MuxAssert.IsFalse(result.IsAuthenticated, "refuses '" + bad + "'");
                    MuxAssert.AreEqual(401, result.StatusCode, "401 for '" + bad + "'");
                    MuxAssert.Contains("Bearer", result.Headers["WWW-Authenticate"], "challenge for '" + bad + "'");
                }

                MuxAssert.IsFalse(MuxMcpServerHost.Authenticate("Bearer ", string.Empty).IsAuthenticated, "an empty configured key never matches an empty token");
                MuxAssert.AreEqual("mcp-client", MuxMcpServerHost.Authenticate("Bearer k3y", "k3y").Principal, "principal named");
                return Task.CompletedTask;
            });
            Add("HostGuards", "The host rejects null collaborators and invalid ports", async (string root, CancellationToken ct) =>
            {
                MuxMcpTools tools = new MuxMcpTools(new MuxMcpServerOptions(), new FakeMcpRunExecutor(), () => new List<EndpointConfig>(), Store(root), null);
                MuxAssert.Throws<ArgumentNullException>(() => new MuxMcpServerHost(null!, new MuxMcpServerOptions()), "null tools");
                MuxAssert.Throws<ArgumentNullException>(() => new MuxMcpServerHost(tools, null!), "null options");
                MuxMcpServerHost host = new MuxMcpServerHost(tools, new MuxMcpServerOptions());
                await MuxAssert.ThrowsAsync<ArgumentOutOfRangeException>(() => host.RunHttpAsync("localhost", 0, null, ct), "port 0").ConfigureAwait(false);
                await MuxAssert.ThrowsAsync<ArgumentOutOfRangeException>(() => host.RunHttpAsync("localhost", 70000, null, ct), "port 70000").ConfigureAwait(false);
                MuxAssert.Throws<ArgumentNullException>(() => new MuxMcpTools(new MuxMcpServerOptions(), null!, () => new List<EndpointConfig>(), Store(root), null), "null executor");
                MuxAssert.Throws<ArgumentNullException>(() => new MuxMcpTools(new MuxMcpServerOptions(), new FakeMcpRunExecutor(), null!, Store(root), null), "null endpoints");
                MuxAssert.Throws<ArgumentNullException>(() => new MuxMcpTools(new MuxMcpServerOptions(), new FakeMcpRunExecutor(), () => new List<EndpointConfig>(), null!, null), "null sessions");
            });

            return new TestSuiteDescriptor(SuiteId, "MCP server tools: arguments, policies, serialization, sessions, masking, skills, auth", cases);
        }

        #endregion

        #region Private-Methods

        private static Dictionary<string, RegisteredMcpTool> Register(MuxMcpServerOptions options, IMcpRunExecutor executor, List<EndpointConfig> endpoints, SessionStore store, SkillRuntime? skills, string? root = null)
        {
            if (root != null && string.IsNullOrEmpty(options.DefaultWorkingDirectory)) options.DefaultWorkingDirectory = root;
            return Capture(new MuxMcpTools(options, executor, () => endpoints, store, skills));
        }

        private static Dictionary<string, RegisteredMcpTool> Capture(MuxMcpTools tools)
        {
            Dictionary<string, RegisteredMcpTool> captured = new Dictionary<string, RegisteredMcpTool>(StringComparer.Ordinal);
            tools.RegisterAll((string name, string description, object schema, Func<RpcParameters, CancellationToken, Task<object>> handler) =>
                captured[name] = new RegisteredMcpTool { Description = description, Schema = schema, Handler = handler });
            return captured;
        }

        private static async Task<string> Ok(RegisteredMcpTool tool, string json, CancellationToken ct)
        {
            object result = await tool.Handler(new RpcParameters(json), ct).ConfigureAwait(false);
            return result as string ?? JsonSerializer.Serialize(result);
        }

        private static async Task<string> Error(RegisteredMcpTool tool, string json, CancellationToken ct)
        {
            try
            {
                object result = await tool.Handler(new RpcParameters(json), ct).ConfigureAwait(false);
                return "NO ERROR: " + (result as string ?? JsonSerializer.Serialize(result));
            }
            catch (McpToolException ex)
            {
                return ex.Message;
            }
        }

        private static SessionStore Store(string root)
        {
            return new SessionStore(Path.Combine(root, "sessions"));
        }

        private static async Task SaveSession(SessionStore store, string id, string title, int messages, DateTime updated, CancellationToken ct)
        {
            SessionSnapshot snapshot = new SessionSnapshot { Id = id, Title = title, UpdatedUtc = updated, CreatedUtc = updated };
            for (int i = 0; i < messages; i++) snapshot.ConversationHistory.Add(new ConversationMessage { Role = i % 2 == 0 ? RoleEnum.User : RoleEnum.Assistant, Content = "m" + i });
            await store.SaveAsync(snapshot, ct).ConfigureAwait(false);
        }

        // A skills runtime over a temporary directory holding one skill, greeter, whose hello command prints a greeting.
        private static async Task<SkillRuntime> SkillsAsync(string root, CancellationToken ct)
        {
            string skillsDir = Path.Combine(root, "skills");
            string skillDir = Path.Combine(skillsDir, "greeter");
            Directory.CreateDirectory(skillDir);
            DefaultSkillDef definition = new DefaultSkillDef
            {
                Id = "greeter",
                Title = "Greeter",
                Description = "Says hello to someone by name.",
                Commands = new List<DefaultSkillCommandDef> { new DefaultSkillCommandDef("hello", "Print a greeting.", "sh", "echo \"Hello, $1\"\n") }
            };
            File.WriteAllText(Path.Combine(skillDir, "SKILL.md"), DefaultSkillBuilder.Build(definition));
            SkillRuntime runtime = new SkillRuntime(skillsDir, () => new List<SkillIndexEntry>(), () => { }, TimeSpan.FromMinutes(5));
            runtime.Start();
            await runtime.FirstRefreshCompleted.WaitAsync(TimeSpan.FromSeconds(10), ct).ConfigureAwait(false);
            return runtime;
        }

        private static List<string> Sorted(IEnumerable<string> values)
        {
            List<string> list = new List<string>(values);
            list.Sort(StringComparer.Ordinal);
            return list;
        }

        private static async Task WithTempAsync(Func<string, Task> body)
        {
            string root = Path.Combine(Path.GetTempPath(), "mux-mcptools-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                await body(Path.GetFullPath(root)).ConfigureAwait(false);
            }
            finally
            {
                try { Directory.Delete(root, true); } catch (Exception) { }
            }
        }

        #endregion
    }
}
