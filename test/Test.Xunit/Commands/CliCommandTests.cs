namespace Test.Xunit.Commands
{
    using System;
    using System.Collections.Generic;
    using System.IO;
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

        private static string CreateTempConfigDirectory(IEnumerable<Dictionary<string, object?>> endpoints)
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "mux_cli_tests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            string json = JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["endpoints"] = endpoints
            });

            File.WriteAllText(Path.Combine(tempDir, "endpoints.json"), json);
            return tempDir;
        }
    }
}
