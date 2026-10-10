namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Agent;
    using Mux.Core.Enums;
    using Mux.Core.Models;
    using Mux.Core.Settings;
    using Mux.Core.Telemetry;
    using Touchstone.Core;

    /// <summary>
    /// Touchstone suite for what a headless caller learns about a run's outcome: an endpoint value that
    /// references an unset environment variable fails before any request; a run whose model call fails ends
    /// <c>failed</c> rather than <c>completed_with_errors</c>; <c>run_completed.usage</c> carries cached and
    /// reasoning tokens and the cost; <c>run_started</c> says where the context window came from; and
    /// <c>run_started.mcp</c> reports <c>enabled</c>.
    /// </summary>
    public static class RunOutcomeSuite
    {
        private const string Suite = "RunOutcome";

        /// <summary>
        /// Builds the run-outcome suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/> containing all run-outcome cases.</returns>
        public static TestSuiteDescriptor Create()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>
            {
                // --- unresolved environment references ---
                new TestCaseDescriptor(Suite, "FindUnresolvedReportsUnsetNames", "Unset ${VAR}, %VAR%, and $env:VAR references are reported by name", (CancellationToken ct) =>
                {
                    string unset = UniqueName("MUX_TEST_UNSET_");
                    string set = UniqueName("MUX_TEST_SET_");
                    Environment.SetEnvironmentVariable(set, "value");
                    try
                    {
                        MuxAssert.AreEqual(unset, SettingsLoader.FindUnresolvedEnvironmentReferences("${" + unset + "}").Single(), "brace");
                        MuxAssert.AreEqual(unset, SettingsLoader.FindUnresolvedEnvironmentReferences("%" + unset + "%").Single(), "percent");
                        MuxAssert.AreEqual(unset, SettingsLoader.FindUnresolvedEnvironmentReferences("Bearer $env:" + unset).Single(), "powershell");
                        MuxAssert.AreEqual(0, SettingsLoader.FindUnresolvedEnvironmentReferences("${" + set + "}").Count, "set variable is resolved");
                        MuxAssert.AreEqual(0, SettingsLoader.FindUnresolvedEnvironmentReferences("sk-literal$value").Count, "bare $ in a literal key is not a reference");
                        MuxAssert.AreEqual(0, SettingsLoader.FindUnresolvedEnvironmentReferences(null).Count, "null");
                        MuxAssert.AreEqual(1, SettingsLoader.FindUnresolvedEnvironmentReferences("${" + unset + "}-${" + unset + "}").Count, "names are distinct");
                    }
                    finally
                    {
                        Environment.SetEnvironmentVariable(set, null);
                    }
                    return Task.CompletedTask;
                }),

                new TestCaseDescriptor(Suite, "ValidateNamesFieldAndVariable", "Validation names the endpoint, the field, and the unset variable", (CancellationToken ct) =>
                {
                    string unset = UniqueName("MUX_TEST_UNSET_");
                    EndpointConfig endpoint = new EndpointConfig { Name = "garrison", ApiKey = "${" + unset + "}" };
                    UnresolvedEnvironmentReferenceException? caught = null;
                    try
                    {
                        SettingsLoader.ValidateEnvironmentReferences(endpoint);
                    }
                    catch (UnresolvedEnvironmentReferenceException ex)
                    {
                        caught = ex;
                    }

                    MuxAssert.IsNotNull(caught, "throws");
                    MuxAssert.AreEqual("apiKey", caught!.FieldName, "field");
                    MuxAssert.AreEqual(unset, caught.VariableNames.Single(), "variable");
                    MuxAssert.AreEqual($"Endpoint 'garrison': apiKey references environment variable {unset}, which is not set.", caught.Message, "message");

                    EndpointConfig headerEndpoint = new EndpointConfig { Name = "h", Headers = new Dictionary<string, string> { ["X-Token"] = "%" + unset + "%" } };
                    try
                    {
                        SettingsLoader.ValidateEnvironmentReferences(headerEndpoint);
                        MuxAssert.Fail("header reference should throw");
                    }
                    catch (UnresolvedEnvironmentReferenceException ex)
                    {
                        MuxAssert.AreEqual("headers.X-Token", ex.FieldName, "header field");
                    }

                    SettingsLoader.ValidateEnvironmentReferences(new EndpointConfig { Name = "ok", ApiKey = "sk-literal" });
                    return Task.CompletedTask;
                }),

                new TestCaseDescriptor(Suite, "PrintFailsBeforeRequestOnUnsetApiKey", "print --output-format jsonl reports config_unresolved_env and sends nothing when the API key variable is unset", (CancellationToken ct) =>
                {
                    string unset = UniqueName("MUX_TEST_UNSET_");
                    using MockHttpServer server = new MockHttpServer();
                    server.RegisterStreamingResponse("unset key run", new List<string> { AgentTestHarness.BuildTextSseChunk("should not be reached") });
                    server.Start();

                    string configDir = CreateConfigDirectory(server.BaseUrl, apiKey: "${" + unset + "}", contextWindow: null);
                    try
                    {
                        CliInvocationResult result = InvokeCli(new[] { "print", "--config-dir", configDir, "--output-format", "jsonl", "--yolo", "unset key run" });
                        MuxAssert.AreEqual(1, result.ExitCode, "exit code");
                        MuxAssert.AreEqual(0, server.ReceivedRequests.Count, "no request sent");

                        string[] lines = Lines(result.StdOut);
                        MuxAssert.AreEqual(1, lines.Length, "one event");
                        using JsonDocument error = JsonDocument.Parse(lines[0]);
                        MuxAssert.AreEqual("error", error.RootElement.GetProperty("eventType").GetString(), "event type");
                        MuxAssert.AreEqual("config_unresolved_env", error.RootElement.GetProperty("code").GetString(), "code");
                        MuxAssert.AreEqual("configuration", error.RootElement.GetProperty("failureCategory").GetString(), "category");
                        MuxAssert.Contains(unset, error.RootElement.GetProperty("message").GetString() ?? string.Empty, "names the variable");
                    }
                    finally
                    {
                        DeleteDirectory(configDir);
                    }
                    return Task.CompletedTask;
                }),

                new TestCaseDescriptor(Suite, "LlmClientRefusesUnresolvedKey", "The agent loop reports config_unresolved_env instead of sending an unresolved key, and the run fails", async (CancellationToken ct) =>
                {
                    string unset = UniqueName("MUX_TEST_UNSET_");
                    using MockHttpServer server = new MockHttpServer();
                    server.RegisterStreamingResponse("loop unset key", new List<string> { AgentTestHarness.BuildTextSseChunk("should not be reached") });
                    server.Start();

                    EndpointConfig endpoint = AgentTestHarness.BuildMockEndpoint(server.BaseUrl);
                    endpoint.ApiKey = "${" + unset + "}";
                    List<AgentEvent> events = await AgentTestHarness.CollectEventsAsync(Options(endpoint), "loop unset key", ct).ConfigureAwait(false);

                    MuxAssert.AreEqual(0, server.ReceivedRequests.Count, "no request sent");
                    MuxAssert.AreEqual("config_unresolved_env", events.OfType<ErrorEvent>().Single().Code, "error code");
                    MuxAssert.AreEqual("failed", events.OfType<RunCompletedEvent>().Single().Status, "status");
                }),

                // --- run status ---
                new TestCaseDescriptor(Suite, "ModelErrorEndsFailed", "A model call that fails on the first turn ends the run failed", async (CancellationToken ct) =>
                {
                    using MockHttpServer server = new MockHttpServer();
                    server.RegisterStatusResponse("auth fails", 401, "{\"error\":{\"message\":\"Incorrect API key provided\"}}");
                    server.Start();

                    List<AgentEvent> events = await AgentTestHarness.CollectEventsAsync(Options(AgentTestHarness.BuildMockEndpoint(server.BaseUrl)), "auth fails", ct).ConfigureAwait(false);
                    MuxAssert.IsTrue(events.OfType<ErrorEvent>().Any(), "error event");
                    MuxAssert.AreEqual("failed", events.OfType<RunCompletedEvent>().Single().Status, "status");
                }),

                new TestCaseDescriptor(Suite, "ModelErrorAfterToolCallEndsFailed", "A model call that fails after a tool call ends the run failed, not completed_with_errors", async (CancellationToken ct) =>
                {
                    string dir = Path.Combine(Path.GetTempPath(), "mux_outcome_" + Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(dir);
                    try
                    {
                        string data = Path.Combine(dir, "data.txt");
                        File.WriteAllText(data, "MIDRUN_FILE_MARKER");
                        using MockHttpServer server = new MockHttpServer();
                        server.RegisterStreamingResponse("midrun run", new List<string> { ToolCallChunk("read_file", "{\"file_path\":" + JsonSerializer.Serialize(data) + "}") });
                        server.RegisterStatusResponse("MIDRUN_FILE_MARKER", 401, "{\"error\":{\"message\":\"key revoked\"}}");
                        server.Start();

                        AgentLoopOptions options = Options(AgentTestHarness.BuildMockEndpoint(server.BaseUrl));
                        options.WorkingDirectory = dir;
                        List<AgentEvent> events = await AgentTestHarness.CollectEventsAsync(options, "midrun run", ct).ConfigureAwait(false);

                        MuxAssert.IsTrue(events.OfType<ToolCallCompletedEvent>().Single().Result.Success, "tool ran");
                        MuxAssert.AreEqual("failed", events.OfType<RunCompletedEvent>().Single().Status, "status");
                    }
                    finally
                    {
                        DeleteDirectory(dir);
                    }
                }),

                new TestCaseDescriptor(Suite, "AnsweredRunWithDeniedToolIsCompletedWithErrors", "A run the model answers despite a denied tool call stays completed_with_errors", async (CancellationToken ct) =>
                {
                    using MockHttpServer server = new MockHttpServer();
                    server.RegisterStreamingResponse("denied run", new List<string> { ToolCallChunk("write_file", "{\"file_path\":\"x.txt\",\"content\":\"x\"}") });
                    server.RegisterStreamingResponse("tool_call_denied", new List<string> { AgentTestHarness.BuildTextSseChunk("Answered anyway.") });
                    server.Start();

                    AgentLoopOptions options = Options(AgentTestHarness.BuildMockEndpoint(server.BaseUrl));
                    options.ApprovalPolicy = ApprovalPolicyEnum.Deny;
                    List<AgentEvent> events = await AgentTestHarness.CollectEventsAsync(options, "denied run", ct).ConfigureAwait(false);

                    MuxAssert.AreEqual("Answered anyway.", AgentTestHarness.CombineAssistantText(events), "answer");
                    MuxAssert.AreEqual("completed_with_errors", events.OfType<RunCompletedEvent>().Single().Status, "status");
                }),

                new TestCaseDescriptor(Suite, "PrintModelErrorReportsFailedAndExitsOne", "print --output-format jsonl ends a 401 run with status failed and exit code 1", (CancellationToken ct) =>
                {
                    using MockHttpServer server = new MockHttpServer();
                    server.RegisterStatusResponse("print auth fails", 401, "{\"error\":{\"message\":\"Incorrect API key provided\"}}");
                    server.Start();

                    string configDir = CreateConfigDirectory(server.BaseUrl, apiKey: null, contextWindow: null);
                    try
                    {
                        CliInvocationResult result = InvokeCli(new[] { "print", "--config-dir", configDir, "--output-format", "jsonl", "--yolo", "print auth fails" });
                        MuxAssert.AreEqual(1, result.ExitCode, "exit code");
                        using JsonDocument completed = JsonDocument.Parse(Lines(result.StdOut)[^1]);
                        MuxAssert.AreEqual("run_completed", completed.RootElement.GetProperty("eventType").GetString(), "last event");
                        MuxAssert.AreEqual("failed", completed.RootElement.GetProperty("status").GetString(), "status");
                    }
                    finally
                    {
                        DeleteDirectory(configDir);
                    }
                    return Task.CompletedTask;
                }),

                // --- usage ---
                new TestCaseDescriptor(Suite, "UsageCarriesCachedReasoningAndCost", "run_completed.usage carries cached and reasoning tokens and costUsd when the model is priced", async (CancellationToken ct) =>
                {
                    using MockHttpServer server = new MockHttpServer();
                    server.RegisterStreamingResponse("usage run", new List<string>
                    {
                        "{\"choices\":[{\"delta\":{\"content\":\"Priced.\"},\"finish_reason\":\"stop\"}],\"usage\":{\"prompt_tokens\":1000000,\"completion_tokens\":100000,\"total_tokens\":1100000,\"prompt_tokens_details\":{\"cached_tokens\":400000},\"completion_tokens_details\":{\"reasoning_tokens\":30000}}}"
                    });
                    server.Start();

                    PricingTable pricing = new PricingTable();
                    pricing.Models["test-model"] = new ModelPricing { InputPerMTok = 2.0, CachedInputPerMTok = 0.5, OutputPerMTok = 10.0 };
                    AgentLoopOptions options = Options(AgentTestHarness.BuildMockEndpoint(server.BaseUrl));
                    options.Pricing = pricing;
                    List<AgentEvent> events = await AgentTestHarness.CollectEventsAsync(options, "usage run", ct).ConfigureAwait(false);

                    RunCompletedEvent completed = events.OfType<RunCompletedEvent>().Single();
                    MuxAssert.AreEqual(400000, completed.CachedTokens, "cached");
                    MuxAssert.AreEqual(30000, completed.ReasoningTokens, "reasoning");

                    using JsonDocument envelope = JsonDocument.Parse(AgentEventSerializer.ToEnvelopeLine(completed));
                    JsonElement usage = envelope.RootElement.GetProperty("usage");
                    MuxAssert.AreEqual(400000, usage.GetProperty("cachedTokens").GetInt32(), "cachedTokens");
                    MuxAssert.AreEqual(30000, usage.GetProperty("reasoningTokens").GetInt32(), "reasoningTokens");
                    // 600k uncached at $2 + 400k cached at $0.50 + 100k output at $10 = 1.2 + 0.2 + 1.0.
                    MuxAssert.AreEqual(2.4, usage.GetProperty("costUsd").GetDouble(), "costUsd");
                }),

                new TestCaseDescriptor(Suite, "UsageOmitsCostWhenUnpriced", "run_completed.usage omits costUsd when no rate is known and zero-fills cached and reasoning", (CancellationToken ct) =>
                {
                    RunCompletedEvent completed = new RunCompletedEvent { RunId = "r", Status = "completed", InputTokens = 10, OutputTokens = 5, TotalTokens = 15 };
                    using JsonDocument envelope = JsonDocument.Parse(AgentEventSerializer.ToEnvelopeLine(completed));
                    JsonElement usage = envelope.RootElement.GetProperty("usage");
                    MuxAssert.AreEqual(0, usage.GetProperty("cachedTokens").GetInt32(), "cachedTokens");
                    MuxAssert.AreEqual(0, usage.GetProperty("reasoningTokens").GetInt32(), "reasoningTokens");
                    MuxAssert.IsFalse(usage.TryGetProperty("costUsd", out _), "no costUsd");
                    return Task.CompletedTask;
                }),

                // --- context window source and mcp.enabled ---
                new TestCaseDescriptor(Suite, "ContextWindowSourceDefault", "run_started reports contextWindowSource default when the endpoint does not set one", (CancellationToken ct) =>
                {
                    JsonElement started = RunStarted(contextWindow: null, extraArgs: Array.Empty<string>());
                    MuxAssert.AreEqual("default", started.GetProperty("contextWindowSource").GetString(), "source");
                    MuxAssert.AreEqual(32768, started.GetProperty("contextWindow").GetInt32(), "window");
                    return Task.CompletedTask;
                }),

                new TestCaseDescriptor(Suite, "ContextWindowSourceEndpoint", "run_started reports contextWindowSource endpoint when endpoints.json sets it", (CancellationToken ct) =>
                {
                    JsonElement started = RunStarted(contextWindow: 131072, extraArgs: Array.Empty<string>());
                    MuxAssert.AreEqual("endpoint", started.GetProperty("contextWindowSource").GetString(), "source");
                    MuxAssert.AreEqual(131072, started.GetProperty("contextWindow").GetInt32(), "window");
                    return Task.CompletedTask;
                }),

                new TestCaseDescriptor(Suite, "ContextWindowSourceCli", "--context-window overrides the endpoint and reports contextWindowSource cli", (CancellationToken ct) =>
                {
                    JsonElement started = RunStarted(contextWindow: 131072, extraArgs: new[] { "--context-window", "65536" });
                    MuxAssert.AreEqual("cli", started.GetProperty("contextWindowSource").GetString(), "source");
                    MuxAssert.AreEqual(65536, started.GetProperty("contextWindow").GetInt32(), "window");
                    MuxAssert.IsTrue(started.GetProperty("cliOverridesApplied").EnumerateArray().Any((JsonElement e) => e.GetString() == "contextWindow"), "override listed");
                    return Task.CompletedTask;
                }),

                new TestCaseDescriptor(Suite, "ContextWindowOutOfRangeRejected", "--context-window outside 1024..1048576 fails before the run", (CancellationToken ct) =>
                {
                    string configDir = CreateConfigDirectory("http://127.0.0.1:9", apiKey: null, contextWindow: null);
                    try
                    {
                        CliInvocationResult result = InvokeCli(new[] { "print", "--config-dir", configDir, "--output-format", "jsonl", "--yolo", "--context-window", "512", "x" });
                        MuxAssert.AreEqual(1, result.ExitCode, "exit code");
                        MuxAssert.Contains("--context-window", result.StdOut, "message names the flag");
                    }
                    finally
                    {
                        DeleteDirectory(configDir);
                    }
                    return Task.CompletedTask;
                }),

                new TestCaseDescriptor(Suite, "McpReportsEnabledWithSupportedAlias", "run_started.mcp reports enabled and keeps supported as an alias", (CancellationToken ct) =>
                {
                    JsonElement mcp = RunStarted(contextWindow: null, extraArgs: Array.Empty<string>()).GetProperty("mcp");
                    MuxAssert.IsFalse(mcp.GetProperty("enabled").GetBoolean(), "enabled");
                    MuxAssert.IsFalse(mcp.GetProperty("supported").GetBoolean(), "supported alias");
                    return Task.CompletedTask;
                })
            };

            return new TestSuiteDescriptor(Suite, "Headless run outcome, usage, and run_started contract", cases);
        }

        private static AgentLoopOptions Options(EndpointConfig endpoint)
        {
            return new AgentLoopOptions(endpoint)
            {
                ApprovalPolicy = ApprovalPolicyEnum.AutoApprove,
                MaxIterations = 5,
                WorkingDirectory = Path.GetTempPath()
            };
        }

        private static JsonElement RunStarted(int? contextWindow, string[] extraArgs)
        {
            using MockHttpServer server = new MockHttpServer();
            server.RegisterStreamingResponse("started run", new List<string> { AgentTestHarness.BuildTextSseChunk("ok") });
            server.Start();

            string configDir = CreateConfigDirectory(server.BaseUrl, apiKey: null, contextWindow: contextWindow);
            try
            {
                List<string> args = new List<string> { "print", "--config-dir", configDir, "--output-format", "jsonl", "--yolo" };
                args.AddRange(extraArgs);
                args.Add("started run");
                CliInvocationResult result = InvokeCli(args.ToArray());
                MuxAssert.AreEqual(0, result.ExitCode, "exit code: " + result.StdOut + result.StdErr);
                using JsonDocument started = JsonDocument.Parse(Lines(result.StdOut)[0]);
                return started.RootElement.Clone();
            }
            finally
            {
                DeleteDirectory(configDir);
            }
        }

        private static string CreateConfigDirectory(string baseUrl, string? apiKey, int? contextWindow)
        {
            string dir = Path.Combine(Path.GetTempPath(), "mux_outcome_cfg_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);

            Dictionary<string, object?> endpoint = new Dictionary<string, object?>
            {
                ["name"] = "outcome-endpoint",
                ["adapterType"] = "openai-compatible",
                ["baseUrl"] = baseUrl,
                ["model"] = "test-model",
                ["isDefault"] = true
            };
            if (apiKey != null) endpoint["apiKey"] = apiKey;
            if (contextWindow.HasValue) endpoint["contextWindow"] = contextWindow.Value;

            string json = JsonSerializer.Serialize(new Dictionary<string, object?> { ["endpoints"] = new[] { endpoint } });
            File.WriteAllText(Path.Combine(dir, "endpoints.json"), json);
            return dir;
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

        private static string[] Lines(string output)
        {
            return output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        }

        private static string ToolCallChunk(string tool, string argumentsJson)
        {
            string arguments = JsonSerializer.Serialize(argumentsJson);
            return "{\"choices\":[{\"delta\":{\"tool_calls\":[{\"index\":0,\"id\":\"call_" + tool + "\",\"function\":{\"name\":\"" + tool + "\",\"arguments\":" + arguments + "}}]},\"finish_reason\":\"tool_calls\"}]}";
        }

        private static string UniqueName(string prefix)
        {
            return prefix + Guid.NewGuid().ToString("N").Substring(0, 8).ToUpperInvariant();
        }

        private static void DeleteDirectory(string dir)
        {
            try
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
            catch (IOException)
            {
            }
        }
    }
}
