namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Touchstone.Core;

    /// <summary>
    /// Touchstone suite of end-to-end CLI command tests against the mock server. Ported from the
    /// <c>CliCommandTests</c> xUnit suite. Cases invoke <c>Mux.Cli.Program.Main</c> in-process with
    /// captured console streams; cases that read config from disk save and restore <c>MUX_CONFIG_DIR</c>.
    /// </summary>
    public static class CliCommandSuite
    {
        /// <summary>
        /// Builds the CLI-command suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/> for the CLI-command cases.</returns>
        public static TestSuiteDescriptor Create()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>
            {
                Case("PrintCommandJsonlEmitsStructuredEvents", "print --output-format jsonl emits structured events", (CancellationToken ct) =>
                {
                    using MockHttpServer server = new MockHttpServer();
                    server.RegisterStreamingResponse("jsonl print test", new List<string> { "{\"choices\":[{\"delta\":{\"content\":\"Structured output works.\"},\"finish_reason\":\"stop\"}]}" });
                    server.Start();

                    CliInvocationResult result = InvokeCli(new[] { "print", "--output-format", "jsonl", "--yolo", "--base-url", server.BaseUrl, "--model", "test-model", "--adapter-type", "openai-compatible", "jsonl print test" });
                    MuxAssert.AreEqual(0, result.ExitCode, "exit code");
                    MuxAssert.AreEqual(string.Empty, result.StdErr.Trim(), "stderr");

                    string[] lines = SplitLines(result.StdOut);
                    MuxAssert.IsTrue(lines.Length >= 3, "line count");
                    JsonDocument first = JsonDocument.Parse(lines[0]);
                    JsonDocument second = JsonDocument.Parse(lines[1]);
                    JsonDocument last = JsonDocument.Parse(lines[^1]);
                    MuxAssert.AreEqual(1, first.RootElement.GetProperty("contractVersion").GetInt32(), "contractVersion");
                    MuxAssert.AreEqual("run_started", first.RootElement.GetProperty("eventType").GetString(), "started eventType");
                    MuxAssert.AreEqual("print", first.RootElement.GetProperty("commandName").GetString(), "commandName");
                    MuxAssert.IsFalse(first.RootElement.GetProperty("mcp").GetProperty("supported").GetBoolean(), "mcp supported");
                    MuxAssert.IsTrue(first.RootElement.GetProperty("builtInToolCount").GetInt32() > 0, "builtInToolCount");
                    MuxAssert.AreEqual("assistant_text", second.RootElement.GetProperty("eventType").GetString(), "assistant_text");
                    MuxAssert.AreEqual("run_completed", last.RootElement.GetProperty("eventType").GetString(), "completed eventType");
                    MuxAssert.AreEqual("completed", last.RootElement.GetProperty("status").GetString(), "status");
                    return Task.CompletedTask;
                }),

                Case("PrintCommandTextOutputHasNoBracketingBlankLines", "print (text) stdout is the answer plus a single terminating newline — no leading or trailing blank line", (CancellationToken ct) =>
                {
                    using MockHttpServer server = new MockHttpServer();
                    server.RegisterStreamingResponse("no-blanks test", new List<string> { "{\"choices\":[{\"delta\":{\"content\":\"Just the answer.\"},\"finish_reason\":\"stop\"}]}" });
                    server.Start();

                    CliInvocationResult result = InvokeCli(new[] { "print", "--yolo", "--base-url", server.BaseUrl, "--model", "test-model", "--adapter-type", "openai-compatible", "no-blanks test" });
                    MuxAssert.AreEqual(0, result.ExitCode, "exit code");

                    string normalized = result.StdOut.Replace("\r\n", "\n");
                    MuxAssert.IsFalse(normalized.StartsWith("\n", StringComparison.Ordinal), "no leading blank line");
                    MuxAssert.IsFalse(normalized.EndsWith("\n\n", StringComparison.Ordinal), "no trailing blank line");
                    MuxAssert.AreEqual("Just the answer.\n", normalized, "answer plus a single terminating newline");
                    return Task.CompletedTask;
                }),

                Case("PrintCommandCompactionStrategyOverrideIsApplied", "print reflects the compaction-strategy override in runtime metadata", (CancellationToken ct) =>
                {
                    using MockHttpServer server = new MockHttpServer();
                    server.RegisterStreamingResponse("strategy override test", new List<string> { "{\"choices\":[{\"delta\":{\"content\":\"Strategy override works.\"},\"finish_reason\":\"stop\"}]}" });
                    server.Start();

                    CliInvocationResult result = InvokeCli(new[] { "print", "--output-format", "jsonl", "--yolo", "--base-url", server.BaseUrl, "--model", "test-model", "--adapter-type", "openai-compatible", "--compaction-strategy", "trim", "strategy override test" });
                    MuxAssert.AreEqual(0, result.ExitCode, "exit code");
                    MuxAssert.AreEqual(string.Empty, result.StdErr.Trim(), "stderr");

                    JsonDocument started = JsonDocument.Parse(SplitLines(result.StdOut)[0]);
                    MuxAssert.AreEqual("run_started", started.RootElement.GetProperty("eventType").GetString(), "eventType");
                    MuxAssert.AreEqual("trim", started.RootElement.GetProperty("compactionStrategy").GetString(), "compactionStrategy");
                    MuxAssert.IsTrue(OverridesContain(started, "compactionStrategy"), "override applied");
                    return Task.CompletedTask;
                }),

                Case("PrintCommandEndpointMaxAgentIterationsOverrideIsApplied", "print applies endpoint-scoped max agent iterations", (CancellationToken ct) =>
                {
                    using MockHttpServer server = new MockHttpServer();
                    server.RegisterStreamingResponse("endpoint iteration override test", new List<string> { "{\"choices\":[{\"delta\":{\"content\":\"Endpoint iteration override works.\"},\"finish_reason\":\"stop\"}]}" });
                    server.Start();

                    string configDir = CreateTempConfigDirectory(new[]
                    {
                        new Dictionary<string, object?> { ["name"] = "iteration-endpoint", ["adapterType"] = "openai-compatible", ["baseUrl"] = server.BaseUrl, ["model"] = "test-model", ["isDefault"] = true, ["maxAgentIterations"] = 7 }
                    }, settingsJson: "{\"maxAgentIterations\":50}");
                    try
                    {
                        CliInvocationResult result = InvokeCli(new[] { "print", "--config-dir", configDir, "--output-format", "jsonl", "--yolo", "--endpoint", "iteration-endpoint", "endpoint iteration override test" });
                        MuxAssert.AreEqual(0, result.ExitCode, "exit code");
                        MuxAssert.AreEqual(string.Empty, result.StdErr.Trim(), "stderr");
                        JsonDocument started = JsonDocument.Parse(SplitLines(result.StdOut)[0]);
                        MuxAssert.AreEqual("run_started", started.RootElement.GetProperty("eventType").GetString(), "eventType");
                        MuxAssert.AreEqual(7, started.RootElement.GetProperty("maxIterations").GetInt32(), "maxIterations");
                    }
                    finally
                    {
                        DeleteDir(configDir);
                    }
                    return Task.CompletedTask;
                }),

                Case("PrintCommandAskApprovalReturnsUnsupportedOption", "print rejects ask approval in non-interactive mode", (CancellationToken ct) =>
                {
                    CliInvocationResult result = InvokeCli(new[] { "print", "--output-format", "jsonl", "--approval-policy", "ask", "--base-url", "http://localhost:65534", "--model", "test-model", "--adapter-type", "openai-compatible", "jsonl print test" });
                    MuxAssert.AreEqual(1, result.ExitCode, "exit code");
                    MuxAssert.AreEqual(string.Empty, result.StdErr.Trim(), "stderr");
                    JsonDocument json = JsonDocument.Parse(result.StdOut);
                    MuxAssert.AreEqual(1, json.RootElement.GetProperty("contractVersion").GetInt32(), "contractVersion");
                    MuxAssert.AreEqual("error", json.RootElement.GetProperty("eventType").GetString(), "eventType");
                    MuxAssert.AreEqual("unsupported_option", json.RootElement.GetProperty("code").GetString(), "code");
                    MuxAssert.AreEqual("unsupported_option", json.RootElement.GetProperty("errorCode").GetString(), "errorCode");
                    MuxAssert.AreEqual("configuration", json.RootElement.GetProperty("failureCategory").GetString(), "failureCategory");
                    MuxAssert.AreEqual("print", json.RootElement.GetProperty("commandName").GetString(), "commandName");
                    MuxAssert.IsTrue(json.RootElement.TryGetProperty("configDirectory", out _), "has configDirectory");
                    return Task.CompletedTask;
                }),

                Case("PrintCommandNoMcpReturnsUnsupportedOption", "print rejects MCP flags with a configuration error", (CancellationToken ct) =>
                {
                    CliInvocationResult result = InvokeCli(new[] { "print", "--output-format", "jsonl", "--no-mcp", "--base-url", "http://localhost:65534", "--model", "test-model", "--adapter-type", "openai-compatible", "jsonl print test" });
                    MuxAssert.AreEqual(1, result.ExitCode, "exit code");
                    MuxAssert.AreEqual(string.Empty, result.StdErr.Trim(), "stderr");
                    JsonDocument json = JsonDocument.Parse(result.StdOut);
                    MuxAssert.AreEqual(1, json.RootElement.GetProperty("contractVersion").GetInt32(), "contractVersion");
                    MuxAssert.AreEqual("error", json.RootElement.GetProperty("eventType").GetString(), "eventType");
                    MuxAssert.AreEqual("unsupported_option", json.RootElement.GetProperty("code").GetString(), "code");
                    MuxAssert.AreEqual("unsupported_option", json.RootElement.GetProperty("errorCode").GetString(), "errorCode");
                    MuxAssert.AreEqual("configuration", json.RootElement.GetProperty("failureCategory").GetString(), "failureCategory");
                    return Task.CompletedTask;
                }),

                Case("PrintCommandRuntimeFailureReturnsStructuredClassification", "print runtime failures expose failure classification and runtime metadata", (CancellationToken ct) =>
                {
                    CliInvocationResult result = InvokeCli(new[] { "print", "--output-format", "jsonl", "--yolo", "--base-url", "http://127.0.0.1:1", "--model", "test-model", "--adapter-type", "openai-compatible", "jsonl print test" });
                    MuxAssert.AreEqual(1, result.ExitCode, "exit code");
                    MuxAssert.Contains("retry", result.StdErr?.ToLowerInvariant(), "retry hint");

                    string[] lines = SplitLines(result.StdOut);
                    MuxAssert.IsTrue(lines.Length >= 3, "line count");
                    string? errorLine = Array.Find(lines, line => line.Contains("\"eventType\":\"error\"", StringComparison.Ordinal));
                    MuxAssert.IsNotNull(errorLine, "error line");
                    JsonDocument errorJson = JsonDocument.Parse(errorLine!);
                    MuxAssert.AreEqual(1, errorJson.RootElement.GetProperty("contractVersion").GetInt32(), "contractVersion");
                    MuxAssert.AreEqual("error", errorJson.RootElement.GetProperty("eventType").GetString(), "eventType");
                    MuxAssert.AreEqual("llm_connection_error", errorJson.RootElement.GetProperty("code").GetString(), "code");
                    MuxAssert.AreEqual("llm_connection_error", errorJson.RootElement.GetProperty("errorCode").GetString(), "errorCode");
                    MuxAssert.AreEqual("network", errorJson.RootElement.GetProperty("failureCategory").GetString(), "failureCategory");
                    MuxAssert.AreEqual("print", errorJson.RootElement.GetProperty("commandName").GetString(), "commandName");
                    MuxAssert.AreEqual("http://127.0.0.1:1", errorJson.RootElement.GetProperty("baseUrl").GetString(), "baseUrl");
                    MuxAssert.AreEqual("test-model", errorJson.RootElement.GetProperty("model").GetString(), "model");
                    return Task.CompletedTask;
                }),

                Case("PrintCommandEmitsContextStatusEventsWhenPressured", "print JSONL includes context status events under context pressure", (CancellationToken ct) =>
                {
                    ContextPressureCase("context stress test", "X", "jsonl", (result) =>
                    {
                        MuxAssert.AreEqual(1, result.ExitCode, "exit code");
                        MuxAssert.AreEqual(string.Empty, result.StdErr.Trim(), "stderr");
                        MuxAssert.IsTrue(SplitLines(result.StdOut).Any(line => line.Contains("\"eventType\":\"context_status\"", StringComparison.Ordinal)), "context_status present");
                    });
                    return Task.CompletedTask;
                }),

                Case("PrintCommandTextEmitsContextWarningsToStderr", "print text mode writes context warnings to stderr under pressure", (CancellationToken ct) =>
                {
                    ContextPressureCase("context stderr test", "Y", "text", (result) =>
                    {
                        MuxAssert.AreEqual(1, result.ExitCode, "exit code");
                        MuxAssert.Contains("Context usage:", result.StdErr, "context usage warning");
                    });
                    return Task.CompletedTask;
                }),

                Case("ProbeCommandMissingEndpointReturnsEndpointNotFound", "probe classifies a missing named endpoint", (CancellationToken ct) =>
                {
                    string configDir = CreateTempConfigDirectory(new[]
                    {
                        new Dictionary<string, object?> { ["name"] = "configured-endpoint", ["adapterType"] = "openai-compatible", ["baseUrl"] = "http://localhost:1234", ["model"] = "test-model", ["isDefault"] = true }
                    });
                    string? original = Environment.GetEnvironmentVariable("MUX_CONFIG_DIR");
                    try
                    {
                        Environment.SetEnvironmentVariable("MUX_CONFIG_DIR", configDir);
                        CliInvocationResult result = InvokeCli(new[] { "probe", "--output-format", "json", "--endpoint", "missing-endpoint" });
                        MuxAssert.AreEqual(1, result.ExitCode, "exit code");
                        MuxAssert.AreEqual(string.Empty, result.StdErr.Trim(), "stderr");
                        JsonDocument json = JsonDocument.Parse(result.StdOut);
                        MuxAssert.AreEqual(1, json.RootElement.GetProperty("contractVersion").GetInt32(), "contractVersion");
                        MuxAssert.IsFalse(json.RootElement.GetProperty("success").GetBoolean(), "success");
                        MuxAssert.AreEqual("endpoint_not_found", json.RootElement.GetProperty("errorCode").GetString(), "errorCode");
                        MuxAssert.AreEqual("configuration", json.RootElement.GetProperty("failureCategory").GetString(), "failureCategory");
                    }
                    finally
                    {
                        Environment.SetEnvironmentVariable("MUX_CONFIG_DIR", original);
                        DeleteDir(configDir);
                    }
                    return Task.CompletedTask;
                }),

                Case("ProbeCommandJsonReturnsSuccessPayload", "probe returns machine-readable JSON success output", (CancellationToken ct) =>
                {
                    using MockHttpServer server = new MockHttpServer();
                    server.RegisterResponse("Respond with OK", "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"OK probe successful\"}}]}");
                    server.Start();
                    CliInvocationResult result = InvokeCli(new[] { "probe", "--output-format", "json", "--base-url", server.BaseUrl, "--model", "test-model", "--adapter-type", "openai-compatible" });
                    MuxAssert.AreEqual(0, result.ExitCode, "exit code");
                    MuxAssert.AreEqual(string.Empty, result.StdErr.Trim(), "stderr");
                    JsonDocument json = JsonDocument.Parse(result.StdOut);
                    MuxAssert.AreEqual(1, json.RootElement.GetProperty("contractVersion").GetInt32(), "contractVersion");
                    MuxAssert.IsTrue(json.RootElement.GetProperty("success").GetBoolean(), "success");
                    MuxAssert.AreEqual("test-model", json.RootElement.GetProperty("model").GetString(), "model");
                    MuxAssert.Contains("ok", json.RootElement.GetProperty("responsePreview").GetString()?.ToLowerInvariant(), "responsePreview");
                    return Task.CompletedTask;
                }),

                Case("PrintCommandOutputLastMessageWritesFinalAssistantResponse", "print can persist only the final assistant response to a file", (CancellationToken ct) =>
                {
                    using MockHttpServer server = new MockHttpServer();
                    server.RegisterStreamingResponse("artifact success test", new List<string> { "{\"choices\":[{\"delta\":{\"content\":\"Final artifact text.\"},\"finish_reason\":\"stop\"}]}" });
                    server.Start();
                    string tempDir = Path.Combine(Path.GetTempPath(), "mux_cli_artifact_" + Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(tempDir);
                    string artifactPath = Path.Combine(tempDir, "last-message.txt");
                    try
                    {
                        CliInvocationResult result = InvokeCli(new[] { "print", "--yolo", "--output-last-message", artifactPath, "--base-url", server.BaseUrl, "--model", "test-model", "--adapter-type", "openai-compatible", "artifact success test" });
                        MuxAssert.AreEqual(0, result.ExitCode, "exit code");
                        MuxAssert.AreEqual(string.Empty, result.StdErr.Trim(), "stderr");
                        MuxAssert.IsTrue(File.Exists(artifactPath), "artifact exists");
                        MuxAssert.AreEqual("Final artifact text.", File.ReadAllText(artifactPath), "artifact content");
                        MuxAssert.Contains("Final artifact text.", result.StdOut, "stdout content");
                    }
                    finally
                    {
                        DeleteDir(tempDir);
                    }
                    return Task.CompletedTask;
                }),

                Case("PrintCommandOutputLastMessageFailureDoesNotCreateArtifact", "print does not leave a stale last-message artifact after failure", (CancellationToken ct) =>
                {
                    using MockHttpServer server = new MockHttpServer();
                    server.RegisterStreamingResponse("artifact failure test", new List<string> { "{\"choices\":[{\"delta\":{\"tool_calls\":[{\"index\":0,\"id\":\"call_write\",\"function\":{\"name\":\"write_file\",\"arguments\":\"{\\\"file_path\\\":\\\"artifact.txt\\\",\\\"content\\\":\\\"denied\\\"}\"}}]},\"finish_reason\":\"tool_calls\"}]}" });
                    server.Start();
                    string tempDir = Path.Combine(Path.GetTempPath(), "mux_cli_artifact_" + Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(tempDir);
                    string artifactPath = Path.Combine(tempDir, "last-message.txt");
                    try
                    {
                        File.WriteAllText(artifactPath, "stale");
                        CliInvocationResult result = InvokeCli(new[] { "print", "--output-last-message", artifactPath, "--base-url", server.BaseUrl, "--model", "test-model", "--adapter-type", "openai-compatible", "artifact failure test" });
                        MuxAssert.AreNotEqual(0, result.ExitCode, "exit code");
                        MuxAssert.IsNotNull(result.StdOut, "stdout");
                        MuxAssert.IsFalse(string.IsNullOrWhiteSpace(result.StdErr), "stderr present");
                        MuxAssert.IsFalse(File.Exists(artifactPath), "artifact removed");
                    }
                    finally
                    {
                        DeleteDir(tempDir);
                    }
                    return Task.CompletedTask;
                }),

                Case("PrintCommandConfigDirFlagOverridesEnvironment", "--config-dir overrides MUX_CONFIG_DIR and is reported in runtime metadata", (CancellationToken ct) =>
                {
                    using MockHttpServer server = new MockHttpServer();
                    server.RegisterStreamingResponse("config dir override test", new List<string> { "{\"choices\":[{\"delta\":{\"content\":\"Config dir override works.\"},\"finish_reason\":\"stop\"}]}" });
                    server.Start();
                    string configDirA = CreateTempConfigDirectory(new[] { new Dictionary<string, object?> { ["name"] = "config-endpoint", ["adapterType"] = "openai-compatible", ["baseUrl"] = server.BaseUrl, ["model"] = "test-model", ["isDefault"] = true } });
                    string configDirB = CreateTempConfigDirectory(new[] { new Dictionary<string, object?> { ["name"] = "wrong-endpoint", ["adapterType"] = "openai-compatible", ["baseUrl"] = "http://127.0.0.1:1", ["model"] = "wrong-model", ["isDefault"] = true } });
                    string? original = Environment.GetEnvironmentVariable("MUX_CONFIG_DIR");
                    try
                    {
                        Environment.SetEnvironmentVariable("MUX_CONFIG_DIR", configDirB);
                        CliInvocationResult result = InvokeCli(new[] { "print", "--config-dir", configDirA, "--output-format", "jsonl", "--yolo", "--endpoint", "config-endpoint", "config dir override test" });
                        MuxAssert.AreEqual(0, result.ExitCode, "exit code");
                        MuxAssert.AreEqual(string.Empty, result.StdErr.Trim(), "stderr");
                        JsonDocument started = JsonDocument.Parse(SplitLines(result.StdOut)[0]);
                        MuxAssert.AreEqual(configDirA, started.RootElement.GetProperty("configDirectory").GetString(), "configDirectory");
                        MuxAssert.IsTrue(OverridesContain(started, "configDir"), "override applied");
                    }
                    finally
                    {
                        Environment.SetEnvironmentVariable("MUX_CONFIG_DIR", original);
                        DeleteDir(configDirA);
                        DeleteDir(configDirB);
                    }
                    return Task.CompletedTask;
                }),

                Case("EndpointCommandShowJsonRedactsHeadersAndReportsTools", "endpoint show redacts secrets and reports tool capability", (CancellationToken ct) =>
                {
                    string configDir = CreateTempConfigDirectory(new[]
                    {
                        new Dictionary<string, object?>
                        {
                            ["name"] = "chat-only", ["adapterType"] = "openai-compatible", ["baseUrl"] = "http://localhost:1234/v1", ["model"] = "model-a", ["isDefault"] = true, ["autoApproveTools"] = true, ["maxAgentIterations"] = 44,
                            ["headers"] = new Dictionary<string, string> { ["Authorization"] = "Bearer super-secret" },
                            ["quirks"] = new Dictionary<string, object?> { ["supportsTools"] = false }
                        }
                    });
                    try
                    {
                        CliInvocationResult result = InvokeCli(new[] { "endpoint", "show", "chat-only", "--config-dir", configDir, "--output-format", "json" });
                        MuxAssert.AreEqual(0, result.ExitCode, "exit code");
                        MuxAssert.AreEqual(string.Empty, result.StdErr.Trim(), "stderr");
                        JsonElement endpoint = JsonDocument.Parse(result.StdOut).RootElement.GetProperty("endpoint");
                        MuxAssert.AreEqual("chat-only", endpoint.GetProperty("name").GetString(), "name");
                        MuxAssert.IsTrue(endpoint.GetProperty("autoApproveTools").GetBoolean(), "autoApproveTools");
                        MuxAssert.AreEqual(44, endpoint.GetProperty("maxAgentIterations").GetInt32(), "maxAgentIterations");
                        MuxAssert.AreEqual(44, endpoint.GetProperty("effectiveMaxAgentIterations").GetInt32(), "effectiveMaxAgentIterations");
                        MuxAssert.AreEqual("endpoint", endpoint.GetProperty("maxAgentIterationsSource").GetString(), "source");
                        MuxAssert.IsFalse(endpoint.GetProperty("toolsEnabled").GetBoolean(), "toolsEnabled");
                        MuxAssert.AreEqual("[redacted]", endpoint.GetProperty("headers").GetProperty("Authorization").GetString(), "redacted header");
                    }
                    finally
                    {
                        DeleteDir(configDir);
                    }
                    return Task.CompletedTask;
                }),

                Case("ProbeCommandRequireToolsReturnsCapabilityFailure", "probe can require tool support and fail when tools are disabled", (CancellationToken ct) =>
                {
                    string configDir = CreateTempConfigDirectory(new[]
                    {
                        new Dictionary<string, object?> { ["name"] = "chat-only", ["adapterType"] = "openai-compatible", ["baseUrl"] = "http://127.0.0.1:1", ["model"] = "model-a", ["isDefault"] = true, ["quirks"] = new Dictionary<string, object?> { ["supportsTools"] = false } }
                    });
                    try
                    {
                        CliInvocationResult result = InvokeCli(new[] { "probe", "--config-dir", configDir, "--output-format", "json", "--require-tools", "--endpoint", "chat-only" });
                        MuxAssert.AreEqual(1, result.ExitCode, "exit code");
                        MuxAssert.AreEqual(string.Empty, result.StdErr.Trim(), "stderr");
                        JsonDocument json = JsonDocument.Parse(result.StdOut);
                        MuxAssert.IsFalse(json.RootElement.GetProperty("success").GetBoolean(), "success");
                        MuxAssert.IsTrue(json.RootElement.GetProperty("requireTools").GetBoolean(), "requireTools");
                        MuxAssert.IsFalse(json.RootElement.GetProperty("toolsEnabled").GetBoolean(), "toolsEnabled");
                        MuxAssert.AreEqual("tools_required", json.RootElement.GetProperty("errorCode").GetString(), "errorCode");
                        MuxAssert.AreEqual("capability", json.RootElement.GetProperty("failureCategory").GetString(), "failureCategory");
                    }
                    finally
                    {
                        DeleteDir(configDir);
                    }
                    return Task.CompletedTask;
                })
            };

            foreach (string flag in new[] { "--ignore-cert-errors", "--insecure" })
            {
                string f = flag;
                cases.Add(Case("PrintCommandIgnoreCertErrorsOverride_" + f.TrimStart('-'), "print reflects certificate bypass flag '" + f + "'", (CancellationToken ct) =>
                {
                    using MockHttpServer server = new MockHttpServer();
                    string prompt = "ignore cert errors test " + f;
                    server.RegisterStreamingResponse(prompt, new List<string> { "{\"choices\":[{\"delta\":{\"content\":\"Certificate override works.\"},\"finish_reason\":\"stop\"}]}" });
                    server.Start();
                    CliInvocationResult result = InvokeCli(new[] { "print", "--output-format", "jsonl", "--yolo", f, "--base-url", server.BaseUrl, "--model", "test-model", "--adapter-type", "openai-compatible", prompt });
                    MuxAssert.AreEqual(0, result.ExitCode, "exit code");
                    MuxAssert.Contains("TLS certificate validation is disabled", result.StdErr, "tls warning");
                    JsonDocument started = JsonDocument.Parse(SplitLines(result.StdOut)[0]);
                    MuxAssert.AreEqual("run_started", started.RootElement.GetProperty("eventType").GetString(), "eventType");
                    MuxAssert.IsTrue(started.RootElement.GetProperty("ignoreCertErrors").GetBoolean(), "ignoreCertErrors");
                    MuxAssert.IsTrue(OverridesContain(started, "ignoreCertErrors"), "override applied");
                    return Task.CompletedTask;
                }));
            }

            foreach (string action in new[] { "list", "ls" })
            {
                string a = action;
                cases.Add(Case("EndpointCommandListAlias_" + a, "endpoint " + a + " returns configured endpoints as JSON", (CancellationToken ct) =>
                {
                    string configDir = CreateTempConfigDirectory(new[]
                    {
                        new Dictionary<string, object?> { ["name"] = "first-endpoint", ["adapterType"] = "openai-compatible", ["baseUrl"] = "http://localhost:1234/v1", ["model"] = "model-a", ["isDefault"] = true },
                        new Dictionary<string, object?> { ["name"] = "second-endpoint", ["adapterType"] = "ollama", ["baseUrl"] = "http://localhost:11434/v1", ["model"] = "model-b", ["isDefault"] = false }
                    });
                    try
                    {
                        CliInvocationResult result = InvokeCli(new[] { "endpoint", a, "--config-dir", configDir, "--output-format", "json" });
                        MuxAssert.AreEqual(0, result.ExitCode, "exit code");
                        MuxAssert.AreEqual(string.Empty, result.StdErr.Trim(), "stderr");
                        JsonDocument json = JsonDocument.Parse(result.StdOut);
                        MuxAssert.IsTrue(json.RootElement.GetProperty("success").GetBoolean(), "success");
                        MuxAssert.AreEqual(configDir, json.RootElement.GetProperty("configDirectory").GetString(), "configDirectory");
                        MuxAssert.AreEqual(2, json.RootElement.GetProperty("endpoints").GetArrayLength(), "endpoint count");
                    }
                    finally
                    {
                        DeleteDir(configDir);
                    }
                    return Task.CompletedTask;
                }));
            }

            foreach (string mode in new[] { "text", "json" })
            {
                string m = mode;
                cases.Add(Case("CommandOutputIsBracketedByBlankLines_" + m, "command output (" + m + ") has a leading and trailing blank line", (CancellationToken ct) =>
                {
                    string configDir = CreateTempConfigDirectory(new[]
                    {
                        new Dictionary<string, object?> { ["name"] = "only-endpoint", ["adapterType"] = "openai-compatible", ["baseUrl"] = "http://localhost:1234/v1", ["model"] = "model-a", ["isDefault"] = true }
                    });
                    try
                    {
                        string[] args = m == "json"
                            ? new[] { "endpoint", "list", "--config-dir", configDir, "--output-format", "json" }
                            : new[] { "endpoint", "list", "--config-dir", configDir };
                        CliInvocationResult result = InvokeCli(args);
                        MuxAssert.AreEqual(0, result.ExitCode, "exit code");

                        string normalized = result.StdOut.Replace("\r\n", "\n");
                        MuxAssert.IsTrue(normalized.StartsWith("\n", StringComparison.Ordinal), "leading blank line");
                        MuxAssert.IsTrue(normalized.EndsWith("\n\n", StringComparison.Ordinal), "trailing blank line");
                        // The bracketing must not swallow the actual payload.
                        MuxAssert.IsFalse(string.IsNullOrWhiteSpace(normalized), "output present");
                    }
                    finally
                    {
                        DeleteDir(configDir);
                    }
                    return Task.CompletedTask;
                }));
            }

            return new TestSuiteDescriptor("CliCommand", "End-to-end CLI command behavior", cases);
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, Func<CancellationToken, Task> body)
        {
            return new TestCaseDescriptor("CliCommand", caseId, displayName, body);
        }

        private static void ContextPressureCase(string prompt, string fillChar, string outputFormat, Action<CliInvocationResult> assert)
        {
            using MockHttpServer server = new MockHttpServer();
            string tempDir = Path.Combine(Path.GetTempPath(), "mux_cli_compaction_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            string largeFile = Path.Combine(tempDir, "large.txt");
            string repeatedLine = new string(fillChar[0], 80);
            File.WriteAllText(largeFile, string.Join(Environment.NewLine, Enumerable.Repeat(repeatedLine, 80)));
            string routeContains = new string(fillChar[0], 40);
            string escapedPath = largeFile.Replace("\\", "\\\\").Replace("\"", "\\\"");
            string toolCallChunk = "{\"choices\":[{\"delta\":{\"tool_calls\":[{\"index\":0,\"id\":\"call_ctx\",\"function\":{\"name\":\"read_file\",\"arguments\":\"{\\\"file_path\\\":\\\"" + escapedPath + "\\\"}\"}}]},\"finish_reason\":\"tool_calls\"}]}";
            server.RegisterStreamingResponse(prompt, new List<string> { toolCallChunk });
            server.RegisterStreamingResponse(routeContains, new List<string> { toolCallChunk });
            server.Start();

            string configDir = CreateTempConfigDirectory(new[]
            {
                new Dictionary<string, object?> { ["name"] = "compact-endpoint", ["adapterType"] = "openai-compatible", ["baseUrl"] = server.BaseUrl, ["model"] = "test-model", ["contextWindow"] = 8192, ["maxTokens"] = 1024, ["isDefault"] = true }
            }, settingsJson: "{\"maxAgentIterations\":8,\"tokenEstimationRatio\":2.0}");
            string? original = Environment.GetEnvironmentVariable("MUX_CONFIG_DIR");
            try
            {
                Environment.SetEnvironmentVariable("MUX_CONFIG_DIR", configDir);
                CliInvocationResult result = InvokeCli(new[] { "print", "--output-format", outputFormat, "--yolo", "--endpoint", "compact-endpoint", prompt });
                assert(result);
            }
            finally
            {
                Environment.SetEnvironmentVariable("MUX_CONFIG_DIR", original);
                DeleteDir(configDir);
                DeleteDir(tempDir);
            }
        }

        private static bool OverridesContain(JsonDocument started, string value)
        {
            return started.RootElement.GetProperty("cliOverridesApplied").EnumerateArray().Any(item => string.Equals(item.GetString(), value, StringComparison.Ordinal));
        }

        private static string[] SplitLines(string stdout)
        {
            return stdout.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        }

        private static void DeleteDir(string dir)
        {
            try
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
            catch (IOException)
            {
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

        private static string CreateTempConfigDirectory(IEnumerable<Dictionary<string, object?>> endpoints, string? settingsJson = null)
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "mux_cli_tests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            string json = JsonSerializer.Serialize(new Dictionary<string, object?> { ["endpoints"] = endpoints });
            File.WriteAllText(Path.Combine(tempDir, "endpoints.json"), json);
            if (!string.IsNullOrWhiteSpace(settingsJson))
            {
                File.WriteAllText(Path.Combine(tempDir, "settings.json"), settingsJson);
            }
            return tempDir;
        }
    }
}
