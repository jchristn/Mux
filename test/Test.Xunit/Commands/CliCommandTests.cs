namespace Test.Xunit.Commands
{
    using System;
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

            Assert.Equal("run_started", first.RootElement.GetProperty("eventType").GetString());
            Assert.Equal("assistant_text", second.RootElement.GetProperty("eventType").GetString());
            Assert.Equal("run_completed", last.RootElement.GetProperty("eventType").GetString());
            Assert.Equal("completed", last.RootElement.GetProperty("status").GetString());
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
    }
}
