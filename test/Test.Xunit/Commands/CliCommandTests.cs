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

            (int exitCode, string stdout, string stderr) = InvokeCli(new[]
            {
                "print",
                "--output-format", "jsonl",
                "--yolo",
                "--base-url", server.BaseUrl,
                "--model", "test-model",
                "--adapter-type", "openai-compatible",
                "jsonl print test"
            });

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

            (int exitCode, string stdout, string stderr) = InvokeCli(new[]
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
            (int exitCode, string stdout, string stderr) = InvokeCli(new[]
            {
                "print",
                "--output-format", "jsonl",
                "--approval-policy", "ask",
                "--base-url", "http://localhost:65534",
                "--model", "test-model",
                "--adapter-type", "openai-compatible",
                "jsonl print test"
            });

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
            (int exitCode, string stdout, string stderr) = InvokeCli(new[]
            {
                "print",
                "--output-format", "jsonl",
                "--no-mcp",
                "--base-url", "http://localhost:65534",
                "--model", "test-model",
                "--adapter-type", "openai-compatible",
                "jsonl print test"
            });

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
            (int exitCode, string stdout, string stderr) = InvokeCli(new[]
            {
                "print",
                "--output-format", "jsonl",
                "--yolo",
                "--base-url", "http://127.0.0.1:1",
                "--model", "test-model",
                "--adapter-type", "openai-compatible",
                "jsonl print test"
            });

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

                (int exitCode, string stdout, string stderr) = InvokeCli(new[]
                {
                    "print",
                    "--output-format", "jsonl",
                    "--yolo",
                    "--endpoint", "compact-endpoint",
                    "context stress test"
                });

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

                (int exitCode, string stdout, string stderr) = InvokeCli(new[]
                {
                    "print",
                    "--output-format", "text",
                    "--yolo",
                    "--endpoint", "compact-endpoint",
                    "context stderr test"
                });

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
                (int exitCode, string stdout, string stderr) = InvokeCli(new[]
                {
                    "probe",
                    "--output-format", "json",
                    "--endpoint", "missing-endpoint"
                });

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

            (int exitCode, string stdout, string stderr) = InvokeCli(new[]
            {
                "probe",
                "--output-format", "json",
                "--base-url", server.BaseUrl,
                "--model", "test-model",
                "--adapter-type", "openai-compatible"
            });

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
                (int exitCode, string stdout, string stderr) = InvokeCli(new[]
                {
                    "print",
                    "--yolo",
                    "--output-last-message", artifactPath,
                    "--base-url", server.BaseUrl,
                    "--model", "test-model",
                    "--adapter-type", "openai-compatible",
                    "artifact success test"
                });

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

                (int exitCode, string stdout, string stderr) = InvokeCli(new[]
                {
                    "print",
                    "--output-last-message", artifactPath,
                    "--base-url", server.BaseUrl,
                    "--model", "test-model",
                    "--adapter-type", "openai-compatible",
                    "artifact failure test"
                });

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

                (int exitCode, string stdout, string stderr) = InvokeCli(new[]
                {
                    "print",
                    "--config-dir", configDirA,
                    "--output-format", "jsonl",
                    "--yolo",
                    "--endpoint", "config-endpoint",
                    "config dir override test"
                });

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
        /// Verifies that endpoint list returns configured endpoints in machine-readable JSON.
        /// </summary>
        [Fact]
        public void EndpointCommand_ListJson_ReturnsConfiguredEndpoints()
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
                (int exitCode, string stdout, string stderr) = InvokeCli(new[]
                {
                    "endpoint",
                    "list",
                    "--config-dir", configDir,
                    "--output-format", "json"
                });

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
                (int exitCode, string stdout, string stderr) = InvokeCli(new[]
                {
                    "endpoint",
                    "show",
                    "chat-only",
                    "--config-dir", configDir,
                    "--output-format", "json"
                });

                Assert.Equal(0, exitCode);
                Assert.Equal(string.Empty, stderr.Trim());

                JsonDocument json = JsonDocument.Parse(stdout);
                JsonElement endpoint = json.RootElement.GetProperty("endpoint");
                Assert.Equal("chat-only", endpoint.GetProperty("name").GetString());
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
                (int exitCode, string stdout, string stderr) = InvokeCli(new[]
                {
                    "probe",
                    "--config-dir", configDir,
                    "--output-format", "json",
                    "--require-tools",
                    "--endpoint", "chat-only"
                });

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

        private static (int ExitCode, string StdOut, string StdErr) InvokeCli(string[] args)
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
                return (exitCode, stdout.ToString(), stderr.ToString());
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
