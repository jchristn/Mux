namespace Test.Xunit.Commands
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text.Json;
    using global::Xunit;
    using Test.Shared;

    /// <summary>
    /// End-to-end CLI command tests against the mock server.
    /// </summary>
    public class CliCommandTests
    {
        /// <summary>
        /// Verifies that print mode emits machine-readable JSONL events.
        /// </summary>
        [Fact]
        public void PrintCommand_Jsonl_EmitsStructuredEvents()
        {
            using MockHttpServer server = new MockHttpServer();
            string sseChunk = "{\"choices\":[{\"delta\":{\"content\":\"Structured output works.\"},\"finish_reason\":\"stop\"}]}";
            server.RegisterStreamingResponse("jsonl print test", new System.Collections.Generic.List<string> { sseChunk });
            server.Start();

            CliInvocationResult invocationResult1 = InvokeCli(new[]
            {
                "print",
                "--output-format", "jsonl",
                "--yolo",
                "--base-url", server.BaseUrl,
                "--model", "test-model",
                "--adapter-type", "openai-compatible",
                "jsonl print test"
            });
            int exitCode = invocationResult1.ExitCode;
            string stdout = invocationResult1.StdOut;
            string stderr = invocationResult1.StdErr;

            Assert.Equal(0, exitCode);
            Assert.Equal(string.Empty, stderr.Trim());

            string[] lines = stdout.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            Assert.True(lines.Length >= 3);

            JsonDocument first = JsonDocument.Parse(lines[0]);
            JsonDocument second = JsonDocument.Parse(lines[1]);
            JsonDocument last = JsonDocument.Parse(lines[^1]);

            Assert.Equal(1, first.RootElement.GetProperty("contractVersion").GetInt32());
            Assert.Equal("run_started", first.RootElement.GetProperty("eventType").GetString());
            Assert.Equal("print", first.RootElement.GetProperty("commandName").GetString());
            Assert.False(first.RootElement.GetProperty("mcp").GetProperty("supported").GetBoolean());
            Assert.True(first.RootElement.GetProperty("builtInToolCount").GetInt32() > 0);
            Assert.Equal("assistant_text", second.RootElement.GetProperty("eventType").GetString());
            Assert.Equal("run_completed", last.RootElement.GetProperty("eventType").GetString());
            Assert.Equal("completed", last.RootElement.GetProperty("status").GetString());
        }

        /// <summary>
        /// Verifies that the CLI compaction-strategy override is reflected in the effective runtime metadata.
        /// </summary>
        [Fact]
        public void PrintCommand_Jsonl_CompactionStrategyOverride_IsApplied()
        {
            using MockHttpServer server = new MockHttpServer();
            string sseChunk = "{\"choices\":[{\"delta\":{\"content\":\"Strategy override works.\"},\"finish_reason\":\"stop\"}]}";
            server.RegisterStreamingResponse("strategy override test", new System.Collections.Generic.List<string> { sseChunk });
            server.Start();

            CliInvocationResult invocationResult2 = InvokeCli(new[]
            {
                "print",
                "--output-format", "jsonl",
                "--yolo",
                "--base-url", server.BaseUrl,
                "--model", "test-model",
                "--adapter-type", "openai-compatible",
                "--compaction-strategy", "trim",
                "strategy override test"
            });
            int exitCode = invocationResult2.ExitCode;
            string stdout = invocationResult2.StdOut;
            string stderr = invocationResult2.StdErr;

            Assert.Equal(0, exitCode);
            Assert.Equal(string.Empty, stderr.Trim());

            string[] lines = stdout.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            JsonDocument started = JsonDocument.Parse(lines[0]);

            Assert.Equal("run_started", started.RootElement.GetProperty("eventType").GetString());
            Assert.Equal("trim", started.RootElement.GetProperty("compactionStrategy").GetString());
            Assert.Contains(
                started.RootElement.GetProperty("cliOverridesApplied").EnumerateArray().Select(static item => item.GetString()),
                value => string.Equals(value, "compactionStrategy", StringComparison.Ordinal));
        }

        /// <summary>
        /// Verifies that print mode rejects ask approval in non-interactive mode with a structured error code.
        /// </summary>
        [Fact]
        public void PrintCommand_AskApproval_ReturnsUnsupportedOption()
        {
            CliInvocationResult invocationResult3 = InvokeCli(new[]
            {
                "print",
                "--output-format", "jsonl",
                "--approval-policy", "ask",
                "--base-url", "http://localhost:65534",
                "--model", "test-model",
                "--adapter-type", "openai-compatible",
                "jsonl print test"
            });
            int exitCode = invocationResult3.ExitCode;
            string stdout = invocationResult3.StdOut;
            string stderr = invocationResult3.StdErr;

            Assert.Equal(1, exitCode);
            Assert.Equal(string.Empty, stderr.Trim());

            JsonDocument json = JsonDocument.Parse(stdout);
            Assert.Equal(1, json.RootElement.GetProperty("contractVersion").GetInt32());
            Assert.Equal("error", json.RootElement.GetProperty("eventType").GetString());
            Assert.Equal("unsupported_option", json.RootElement.GetProperty("code").GetString());
            Assert.Equal("unsupported_option", json.RootElement.GetProperty("errorCode").GetString());
            Assert.Equal("configuration", json.RootElement.GetProperty("failureCategory").GetString());
            Assert.Equal("print", json.RootElement.GetProperty("commandName").GetString());
            Assert.True(json.RootElement.TryGetProperty("configDirectory", out _));
        }

        /// <summary>
        /// Verifies that print mode rejects MCP flags with a structured configuration error.
        /// </summary>
        [Fact]
        public void PrintCommand_NoMcp_ReturnsUnsupportedOption()
        {
            CliInvocationResult invocationResult4 = InvokeCli(new[]
            {
                "print",
                "--output-format", "jsonl",
                "--no-mcp",
                "--base-url", "http://localhost:65534",
                "--model", "test-model",
                "--adapter-type", "openai-compatible",
                "jsonl print test"
            });
            int exitCode = invocationResult4.ExitCode;
            string stdout = invocationResult4.StdOut;
            string stderr = invocationResult4.StdErr;

            Assert.Equal(1, exitCode);
            Assert.Equal(string.Empty, stderr.Trim());

            JsonDocument json = JsonDocument.Parse(stdout);
            Assert.Equal(1, json.RootElement.GetProperty("contractVersion").GetInt32());
            Assert.Equal("error", json.RootElement.GetProperty("eventType").GetString());
            Assert.Equal("unsupported_option", json.RootElement.GetProperty("code").GetString());
            Assert.Equal("unsupported_option", json.RootElement.GetProperty("errorCode").GetString());
            Assert.Equal("configuration", json.RootElement.GetProperty("failureCategory").GetString());
        }

        /// <summary>
        /// Verifies that print mode runtime failures expose failure classification and runtime metadata.
        /// </summary>
        [Fact]
        public void PrintCommand_RuntimeFailure_ReturnsStructuredClassification()
        {
            CliInvocationResult invocationResult5 = InvokeCli(new[]
            {
                "print",
                "--output-format", "jsonl",
                "--yolo",
                "--base-url", "http://127.0.0.1:1",
                "--model", "test-model",
                "--adapter-type", "openai-compatible",
                "jsonl print test"
            });
            int exitCode = invocationResult5.ExitCode;
            string stdout = invocationResult5.StdOut;
            string stderr = invocationResult5.StdErr;

            Assert.Equal(1, exitCode);
            Assert.Contains("Retry", stderr, StringComparison.OrdinalIgnoreCase);

            string[] lines = stdout.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            Assert.True(lines.Length >= 3);

            JsonDocument errorJson = JsonDocument.Parse(Array.Find(lines, static line => line.Contains("\"eventType\":\"error\"", StringComparison.Ordinal))!);

            Assert.Equal(1, errorJson.RootElement.GetProperty("contractVersion").GetInt32());
            Assert.Equal("error", errorJson.RootElement.GetProperty("eventType").GetString());
            Assert.Equal("llm_connection_error", errorJson.RootElement.GetProperty("code").GetString());
            Assert.Equal("llm_connection_error", errorJson.RootElement.GetProperty("errorCode").GetString());
            Assert.Equal("network", errorJson.RootElement.GetProperty("failureCategory").GetString());
            Assert.Equal("print", errorJson.RootElement.GetProperty("commandName").GetString());
            Assert.Equal("http://127.0.0.1:1", errorJson.RootElement.GetProperty("baseUrl").GetString());
            Assert.Equal("test-model", errorJson.RootElement.GetProperty("model").GetString());
        }

        /// <summary>
        /// Verifies that print JSONL output includes additive context status events under context pressure.
        /// </summary>
        [Fact]
        public void PrintCommand_Jsonl_EmitsContextStatusEventsWhenPressured()
        {
            using MockHttpServer server = new MockHttpServer();
            string tempDir = Path.Combine(Path.GetTempPath(), "mux_cli_compaction_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            string largeFile = Path.Combine(tempDir, "large.txt");
            string repeatedLine = new string('X', 80);
            string largeContent = string.Join(Environment.NewLine, Enumerable.Repeat(repeatedLine, 80));
            File.WriteAllText(largeFile, largeContent);
            string routeContains = new string('X', 40);

            string escapedPath = largeFile.Replace("\\", "\\\\").Replace("\"", "\\\"");
            string toolCallChunk = "{\"choices\":[{\"delta\":{\"tool_calls\":[{\"index\":0,\"id\":\"call_ctx\",\"function\":{\"name\":\"read_file\",\"arguments\":\"{\\\"file_path\\\":\\\"" + escapedPath + "\\\"}\"}}]},\"finish_reason\":\"tool_calls\"}]}";

            server.RegisterStreamingResponse("context stress test", new System.Collections.Generic.List<string> { toolCallChunk });
            server.RegisterStreamingResponse(routeContains, new System.Collections.Generic.List<string> { toolCallChunk });
            server.Start();

            string configDir = CreateTempConfigDirectory(
                new[]
                {
                    new Dictionary<string, object?>
                    {
                        ["name"] = "compact-endpoint",
                        ["adapterType"] = "openai-compatible",
                        ["baseUrl"] = server.BaseUrl,
                        ["model"] = "test-model",
                        ["contextWindow"] = 8192,
                        ["maxTokens"] = 1024,
                        ["isDefault"] = true
                    }
                },
                settingsJson: "{\"maxAgentIterations\":8,\"tokenEstimationRatio\":2.0}");

            string? originalConfigDir = Environment.GetEnvironmentVariable("MUX_CONFIG_DIR");

            try
            {
                Environment.SetEnvironmentVariable("MUX_CONFIG_DIR", configDir);

                CliInvocationResult invocationResult6 = InvokeCli(new[]
                {
                    "print",
                    "--output-format", "jsonl",
                    "--yolo",
                    "--endpoint", "compact-endpoint",
                    "context stress test"
                });
                int exitCode = invocationResult6.ExitCode;
                string stdout = invocationResult6.StdOut;
                string stderr = invocationResult6.StdErr;

                Assert.Equal(1, exitCode);
                Assert.Equal(string.Empty, stderr.Trim());
                string[] lines = stdout.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
                Assert.Contains(lines, static line => line.Contains("\"eventType\":\"context_status\"", StringComparison.Ordinal));
            }
            finally
            {
                Environment.SetEnvironmentVariable("MUX_CONFIG_DIR", originalConfigDir);
                if (Directory.Exists(configDir))
                {
                    Directory.Delete(configDir, true);
                }
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
            }
        }

        /// <summary>
        /// Verifies that print text mode writes context warnings to stderr under context pressure.
        /// </summary>
        [Fact]
        public void PrintCommand_Text_EmitsContextWarningsToStderr()
        {
            using MockHttpServer server = new MockHttpServer();
            string tempDir = Path.Combine(Path.GetTempPath(), "mux_cli_compaction_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            string largeFile = Path.Combine(tempDir, "large.txt");
            string repeatedLine = new string('Y', 80);
            string largeContent = string.Join(Environment.NewLine, Enumerable.Repeat(repeatedLine, 80));
            File.WriteAllText(largeFile, largeContent);
            string routeContains = new string('Y', 40);

            string escapedPath = largeFile.Replace("\\", "\\\\").Replace("\"", "\\\"");
            string toolCallChunk = "{\"choices\":[{\"delta\":{\"tool_calls\":[{\"index\":0,\"id\":\"call_ctx\",\"function\":{\"name\":\"read_file\",\"arguments\":\"{\\\"file_path\\\":\\\"" + escapedPath + "\\\"}\"}}]},\"finish_reason\":\"tool_calls\"}]}";

            server.RegisterStreamingResponse("context stderr test", new System.Collections.Generic.List<string> { toolCallChunk });
            server.RegisterStreamingResponse(routeContains, new System.Collections.Generic.List<string> { toolCallChunk });
            server.Start();

            string configDir = CreateTempConfigDirectory(
                new[]
                {
                    new Dictionary<string, object?>
                    {
                        ["name"] = "compact-endpoint",
                        ["adapterType"] = "openai-compatible",
                        ["baseUrl"] = server.BaseUrl,
                        ["model"] = "test-model",
                        ["contextWindow"] = 8192,
                        ["maxTokens"] = 1024,
                        ["isDefault"] = true
                    }
                },
                settingsJson: "{\"maxAgentIterations\":8,\"tokenEstimationRatio\":2.0}");

            string? originalConfigDir = Environment.GetEnvironmentVariable("MUX_CONFIG_DIR");

            try
            {
                Environment.SetEnvironmentVariable("MUX_CONFIG_DIR", configDir);

                CliInvocationResult invocationResult7 = InvokeCli(new[]
                {
                    "print",
                    "--output-format", "text",
                    "--yolo",
                    "--endpoint", "compact-endpoint",
                    "context stderr test"
                });
                int exitCode = invocationResult7.ExitCode;
                string stdout = invocationResult7.StdOut;
                string stderr = invocationResult7.StdErr;

                Assert.Equal(1, exitCode);
                Assert.Contains("Context usage:", stderr, StringComparison.Ordinal);
            }
            finally
            {
                Environment.SetEnvironmentVariable("MUX_CONFIG_DIR", originalConfigDir);
                if (Directory.Exists(configDir))
                {
                    Directory.Delete(configDir, true);
                }
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
            }
        }

        /// <summary>
        /// Verifies that probe mode classifies a missing named endpoint without string parsing.
        /// </summary>
        [Fact]
        public void ProbeCommand_MissingEndpoint_ReturnsEndpointNotFound()
        {
            string tempDir = CreateTempConfigDirectory(new[]
            {
                new Dictionary<string, object?>
                {
                    ["name"] = "configured-endpoint",
                    ["adapterType"] = "openai-compatible",
                    ["baseUrl"] = "http://localhost:1234",
                    ["model"] = "test-model",
                    ["isDefault"] = true
                }
            });

            string? originalConfigDir = Environment.GetEnvironmentVariable("MUX_CONFIG_DIR");

            try
            {
                Environment.SetEnvironmentVariable("MUX_CONFIG_DIR", tempDir);
                CliInvocationResult invocationResult8 = InvokeCli(new[]
                {
                    "probe",
                    "--output-format", "json",
                    "--endpoint", "missing-endpoint"
                });
                int exitCode = invocationResult8.ExitCode;
                string stdout = invocationResult8.StdOut;
                string stderr = invocationResult8.StdErr;

                Assert.Equal(1, exitCode);
                Assert.Equal(string.Empty, stderr.Trim());

                JsonDocument json = JsonDocument.Parse(stdout);
                Assert.Equal(1, json.RootElement.GetProperty("contractVersion").GetInt32());
                Assert.False(json.RootElement.GetProperty("success").GetBoolean());
                Assert.Equal("endpoint_not_found", json.RootElement.GetProperty("errorCode").GetString());
                Assert.Equal("configuration", json.RootElement.GetProperty("failureCategory").GetString());
            }
            finally
            {
                Environment.SetEnvironmentVariable("MUX_CONFIG_DIR", originalConfigDir);
                Directory.Delete(tempDir, true);
            }
        }

        /// <summary>
        /// Verifies that probe mode returns machine-readable JSON success output.
        /// </summary>
        [Fact]
        public void ProbeCommand_Json_ReturnsSuccessPayload()
        {
            using MockHttpServer server = new MockHttpServer();
            string responseJson = "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"OK probe successful\"}}]}";
            server.RegisterResponse("Respond with OK", responseJson);
            server.Start();

            CliInvocationResult invocationResult9 = InvokeCli(new[]
            {
                "probe",
                "--output-format", "json",
                "--base-url", server.BaseUrl,
                "--model", "test-model",
                "--adapter-type", "openai-compatible"
            });
            int exitCode = invocationResult9.ExitCode;
            string stdout = invocationResult9.StdOut;
            string stderr = invocationResult9.StdErr;

            Assert.Equal(0, exitCode);
            Assert.Equal(string.Empty, stderr.Trim());

            JsonDocument json = JsonDocument.Parse(stdout);
            Assert.Equal(1, json.RootElement.GetProperty("contractVersion").GetInt32());
            Assert.True(json.RootElement.GetProperty("success").GetBoolean());
            Assert.Equal("test-model", json.RootElement.GetProperty("model").GetString());
            Assert.Contains("OK", json.RootElement.GetProperty("responsePreview").GetString(), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Verifies that print mode can persist only the final assistant response text to a file.
        /// </summary>
        [Fact]
        public void PrintCommand_OutputLastMessage_WritesFinalAssistantResponse()
        {
            using MockHttpServer server = new MockHttpServer();
            string sseChunk = "{\"choices\":[{\"delta\":{\"content\":\"Final artifact text.\"},\"finish_reason\":\"stop\"}]}";
            server.RegisterStreamingResponse("artifact success test", new System.Collections.Generic.List<string> { sseChunk });
            server.Start();

            string tempDir = Path.Combine(Path.GetTempPath(), "mux_cli_artifact_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            string artifactPath = Path.Combine(tempDir, "last-message.txt");

            try
            {
                CliInvocationResult invocationResult10 = InvokeCli(new[]
                {
                    "print",
                    "--yolo",
                    "--output-last-message", artifactPath,
                    "--base-url", server.BaseUrl,
                    "--model", "test-model",
                    "--adapter-type", "openai-compatible",
                    "artifact success test"
                });
                int exitCode = invocationResult10.ExitCode;
                string stdout = invocationResult10.StdOut;
                string stderr = invocationResult10.StdErr;

                Assert.Equal(0, exitCode);
                Assert.Equal(string.Empty, stderr.Trim());
                Assert.True(File.Exists(artifactPath));
                Assert.Equal("Final artifact text.", File.ReadAllText(artifactPath));
                Assert.Contains("Final artifact text.", stdout, StringComparison.Ordinal);
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
            }
        }

        /// <summary>
        /// Verifies that print mode does not leave a stale last-message artifact behind after failure.
        /// </summary>
        [Fact]
        public void PrintCommand_OutputLastMessage_FailureDoesNotCreateArtifact()
        {
            using MockHttpServer server = new MockHttpServer();
            string toolCallChunk = "{\"choices\":[{\"delta\":{\"tool_calls\":[{\"index\":0,\"id\":\"call_write\",\"function\":{\"name\":\"write_file\",\"arguments\":\"{\\\"file_path\\\":\\\"artifact.txt\\\",\\\"content\\\":\\\"denied\\\"}\"}}]},\"finish_reason\":\"tool_calls\"}]}";
            server.RegisterStreamingResponse("artifact failure test", new System.Collections.Generic.List<string> { toolCallChunk });
            server.Start();

            string tempDir = Path.Combine(Path.GetTempPath(), "mux_cli_artifact_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            string artifactPath = Path.Combine(tempDir, "last-message.txt");

            try
            {
                File.WriteAllText(artifactPath, "stale");

                CliInvocationResult invocationResult11 = InvokeCli(new[]
                {
                    "print",
                    "--output-last-message", artifactPath,
                    "--base-url", server.BaseUrl,
                    "--model", "test-model",
                    "--adapter-type", "openai-compatible",
                    "artifact failure test"
                });
                int exitCode = invocationResult11.ExitCode;
                string stdout = invocationResult11.StdOut;
                string stderr = invocationResult11.StdErr;

                Assert.NotEqual(0, exitCode);
                Assert.NotNull(stdout);
                Assert.False(string.IsNullOrWhiteSpace(stderr));
                Assert.False(File.Exists(artifactPath));
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
            }
        }

        /// <summary>
        /// Verifies that --config-dir overrides MUX_CONFIG_DIR and is reported in JSONL runtime metadata.
        /// </summary>
        [Fact]
        public void PrintCommand_ConfigDirFlag_OverridesEnvironment()
        {
            using MockHttpServer server = new MockHttpServer();
            string sseChunk = "{\"choices\":[{\"delta\":{\"content\":\"Config dir override works.\"},\"finish_reason\":\"stop\"}]}";
            server.RegisterStreamingResponse("config dir override test", new System.Collections.Generic.List<string> { sseChunk });
            server.Start();

            string configDirA = CreateTempConfigDirectory(new[]
            {
                new Dictionary<string, object?>
                {
                    ["name"] = "config-endpoint",
                    ["adapterType"] = "openai-compatible",
                    ["baseUrl"] = server.BaseUrl,
                    ["model"] = "test-model",
                    ["isDefault"] = true
                }
            });
            string configDirB = CreateTempConfigDirectory(new[]
            {
                new Dictionary<string, object?>
                {
                    ["name"] = "wrong-endpoint",
                    ["adapterType"] = "openai-compatible",
                    ["baseUrl"] = "http://127.0.0.1:1",
                    ["model"] = "wrong-model",
                    ["isDefault"] = true
                }
            });

            string? originalConfigDir = Environment.GetEnvironmentVariable("MUX_CONFIG_DIR");

            try
            {
                Environment.SetEnvironmentVariable("MUX_CONFIG_DIR", configDirB);

                CliInvocationResult invocationResult12 = InvokeCli(new[]
                {
                    "print",
                    "--config-dir", configDirA,
                    "--output-format", "jsonl",
                    "--yolo",
                    "--endpoint", "config-endpoint",
                    "config dir override test"
                });
                int exitCode = invocationResult12.ExitCode;
                string stdout = invocationResult12.StdOut;
                string stderr = invocationResult12.StdErr;

                Assert.Equal(0, exitCode);
                Assert.Equal(string.Empty, stderr.Trim());

                string[] lines = stdout.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
                JsonDocument started = JsonDocument.Parse(lines[0]);
                Assert.Equal(configDirA, started.RootElement.GetProperty("configDirectory").GetString());
                Assert.Contains(
                    started.RootElement.GetProperty("cliOverridesApplied").EnumerateArray().Select(static item => item.GetString()),
                    value => string.Equals(value, "configDir", StringComparison.Ordinal));
            }
            finally
            {
                Environment.SetEnvironmentVariable("MUX_CONFIG_DIR", originalConfigDir);
                if (Directory.Exists(configDirA))
                {
                    Directory.Delete(configDirA, true);
                }
                if (Directory.Exists(configDirB))
                {
                    Directory.Delete(configDirB, true);
                }
            }
        }

        /// <summary>
        /// Verifies that endpoint list aliases return configured endpoints in machine-readable JSON.
        /// </summary>
        /// <param name="action">The list action alias.</param>
        [Theory]
        [InlineData("list")]
        [InlineData("ls")]
        public void EndpointCommand_ListAliasesJson_ReturnConfiguredEndpoints(string action)
        {
            string configDir = CreateTempConfigDirectory(new[]
            {
                new Dictionary<string, object?>
                {
                    ["name"] = "first-endpoint",
                    ["adapterType"] = "openai-compatible",
                    ["baseUrl"] = "http://localhost:1234/v1",
                    ["model"] = "model-a",
                    ["isDefault"] = true
                },
                new Dictionary<string, object?>
                {
                    ["name"] = "second-endpoint",
                    ["adapterType"] = "ollama",
                    ["baseUrl"] = "http://localhost:11434/v1",
                    ["model"] = "model-b",
                    ["isDefault"] = false
                }
            });

            try
            {
                CliInvocationResult invocationResult13 = InvokeCli(new[]
                {
                    "endpoint",
                    action,
                    "--config-dir", configDir,
                    "--output-format", "json"
                });
                int exitCode = invocationResult13.ExitCode;
                string stdout = invocationResult13.StdOut;
                string stderr = invocationResult13.StdErr;

                Assert.Equal(0, exitCode);
                Assert.Equal(string.Empty, stderr.Trim());

                JsonDocument json = JsonDocument.Parse(stdout);
                Assert.True(json.RootElement.GetProperty("success").GetBoolean());
                Assert.Equal(configDir, json.RootElement.GetProperty("configDirectory").GetString());
                Assert.Equal(2, json.RootElement.GetProperty("endpoints").GetArrayLength());
            }
            finally
            {
                if (Directory.Exists(configDir))
                {
                    Directory.Delete(configDir, true);
                }
            }
        }

        /// <summary>
        /// Verifies that endpoint show redacts secret values and reports tool capability.
        /// </summary>
        [Fact]
        public void EndpointCommand_ShowJson_RedactsHeadersAndReportsTools()
        {
            string configDir = CreateTempConfigDirectory(new[]
            {
                new Dictionary<string, object?>
                {
                    ["name"] = "chat-only",
                    ["adapterType"] = "openai-compatible",
                    ["baseUrl"] = "http://localhost:1234/v1",
                    ["model"] = "model-a",
                    ["isDefault"] = true,
                    ["autoApproveTools"] = true,
                    ["headers"] = new Dictionary<string, string>
                    {
                        ["Authorization"] = "Bearer super-secret"
                    },
                    ["quirks"] = new Dictionary<string, object?>
                    {
                        ["supportsTools"] = false
                    }
                }
            });

            try
            {
                CliInvocationResult invocationResult14 = InvokeCli(new[]
                {
                    "endpoint",
                    "show",
                    "chat-only",
                    "--config-dir", configDir,
                    "--output-format", "json"
                });
                int exitCode = invocationResult14.ExitCode;
                string stdout = invocationResult14.StdOut;
                string stderr = invocationResult14.StdErr;

                Assert.Equal(0, exitCode);
                Assert.Equal(string.Empty, stderr.Trim());

                JsonDocument json = JsonDocument.Parse(stdout);
                JsonElement endpoint = json.RootElement.GetProperty("endpoint");
                Assert.Equal("chat-only", endpoint.GetProperty("name").GetString());
                Assert.True(endpoint.GetProperty("autoApproveTools").GetBoolean());
                Assert.False(endpoint.GetProperty("toolsEnabled").GetBoolean());
                Assert.Equal("[redacted]", endpoint.GetProperty("headers").GetProperty("Authorization").GetString());
            }
            finally
            {
                if (Directory.Exists(configDir))
                {
                    Directory.Delete(configDir, true);
                }
            }
        }

        /// <summary>
        /// Verifies that probe can require tool support and fail clearly when tools are disabled.
        /// </summary>
        [Fact]
        public void ProbeCommand_RequireTools_ReturnsCapabilityFailure()
        {
            string configDir = CreateTempConfigDirectory(new[]
            {
                new Dictionary<string, object?>
                {
                    ["name"] = "chat-only",
                    ["adapterType"] = "openai-compatible",
                    ["baseUrl"] = "http://127.0.0.1:1",
                    ["model"] = "model-a",
                    ["isDefault"] = true,
                    ["quirks"] = new Dictionary<string, object?>
                    {
                        ["supportsTools"] = false
                    }
                }
            });

            try
            {
                CliInvocationResult invocationResult15 = InvokeCli(new[]
                {
                    "probe",
                    "--config-dir", configDir,
                    "--output-format", "json",
                    "--require-tools",
                    "--endpoint", "chat-only"
                });
                int exitCode = invocationResult15.ExitCode;
                string stdout = invocationResult15.StdOut;
                string stderr = invocationResult15.StdErr;

                Assert.Equal(1, exitCode);
                Assert.Equal(string.Empty, stderr.Trim());

                JsonDocument json = JsonDocument.Parse(stdout);
                Assert.False(json.RootElement.GetProperty("success").GetBoolean());
                Assert.True(json.RootElement.GetProperty("requireTools").GetBoolean());
                Assert.False(json.RootElement.GetProperty("toolsEnabled").GetBoolean());
                Assert.Equal("tools_required", json.RootElement.GetProperty("errorCode").GetString());
                Assert.Equal("capability", json.RootElement.GetProperty("failureCategory").GetString());
            }
            finally
            {
                if (Directory.Exists(configDir))
                {
                    Directory.Delete(configDir, true);
                }
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

            string json = JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["endpoints"] = endpoints
            });

            File.WriteAllText(Path.Combine(tempDir, "endpoints.json"), json);

            if (!string.IsNullOrWhiteSpace(settingsJson))
            {
                File.WriteAllText(Path.Combine(tempDir, "settings.json"), settingsJson);
            }

            return tempDir;
        }
    }
}
