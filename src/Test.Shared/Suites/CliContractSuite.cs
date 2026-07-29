namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Agent;
    using Mux.Core.Enums;
    using Mux.Core.Models;
    using Touchstone.Core;

    /// <summary>
    /// Touchstone suite for the non-interactive CLI contract used by orchestration consumers. Ported
    /// from the legacy <c>CliContractTests</c> suite. Invokes <c>Mux.Cli.Program.Main</c> in-process
    /// with captured console streams.
    /// </summary>
    public static class CliContractSuite
    {
        /// <summary>
        /// Builds the CLI-contract suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/> containing all CLI-contract cases.</returns>
        public static TestSuiteDescriptor Create()
        {
            return new TestSuiteDescriptor(
                "CliContract",
                "Non-interactive CLI contract",
                new List<TestCaseDescriptor>
                {
                    new TestCaseDescriptor(
                        "CliContract",
                        "AgentLoopEmitsRunLifecycleEvents",
                        "Agent loop emits run lifecycle events",
                        (CancellationToken ct) => AgentLoopEmitsRunLifecycleEventsAsync(ct)),

                    new TestCaseDescriptor(
                        "CliContract",
                        "PrintCommandJsonlEmitsStructuredEvents",
                        "print --output-format jsonl emits structured events",
                        (CancellationToken ct) => { PrintCommandJsonlEmitsStructuredEvents(); return Task.CompletedTask; }),

                    new TestCaseDescriptor(
                        "CliContract",
                        "ProbeCommandJsonReturnsSuccessPayload",
                        "probe --output-format json returns success payload",
                        (CancellationToken ct) => { ProbeCommandJsonReturnsSuccessPayload(); return Task.CompletedTask; }),

                    new TestCaseDescriptor(
                        "CliContract",
                        "ArmadaStyleRunAndProbeWorkWithConfigDirAndArtifact",
                        "Armada-style run and probe work with config dir and artifact",
                        (CancellationToken ct) => { ArmadaStyleRunAndProbeWorkWithConfigDirAndArtifact(); return Task.CompletedTask; })
                });
        }

        private static async Task AgentLoopEmitsRunLifecycleEventsAsync(CancellationToken ct)
        {
            using MockHttpServer server = new MockHttpServer();
            server.RegisterStreamingResponse("lifecycle test", new List<string> { AgentTestHarness.BuildTextSseChunk("Lifecycle works.") });
            server.Start();

            AgentLoopOptions options = new AgentLoopOptions(AgentTestHarness.BuildMockEndpoint(server.BaseUrl))
            {
                ApprovalPolicy = ApprovalPolicyEnum.AutoApprove,
                MaxIterations = 5,
                WorkingDirectory = "C:\\Code\\Mux"
            };

            List<AgentEvent> events = await AgentTestHarness.CollectEventsAsync(options, "lifecycle test", ct).ConfigureAwait(false);

            MuxAssert.IsTrue(events[0] is RunStartedEvent, "first event is RunStartedEvent");
            MuxAssert.IsTrue(events[^1] is RunCompletedEvent, "last event is RunCompletedEvent");

            RunCompletedEvent completed = (RunCompletedEvent)events[^1];
            MuxAssert.AreEqual("completed", completed.Status, "run status");
            MuxAssert.AreEqual(1, completed.IterationsCompleted, "iterations completed");
        }

        private static void PrintCommandJsonlEmitsStructuredEvents()
        {
            using MockHttpServer server = new MockHttpServer();
            server.RegisterStreamingResponse("cli jsonl test", new List<string> { "{\"choices\":[{\"delta\":{\"content\":\"CLI jsonl works.\"},\"finish_reason\":\"stop\"}]}" });
            server.Start();

            CliInvocationResult result = InvokeCli(new[]
            {
                "print",
                "--output-format", "jsonl",
                "--yolo",
                "--base-url", server.BaseUrl,
                "--model", "test-model",
                "--adapter-type", "openai-compatible",
                "cli jsonl test"
            });

            MuxAssert.AreEqual(0, result.ExitCode, "exit code");
            MuxAssert.AreEqual(string.Empty, result.StdErr.Trim(), "stderr");

            string[] lines = result.StdOut.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            MuxAssert.IsTrue(lines.Length >= 3, "expected JSONL event output");

            JsonDocument started = JsonDocument.Parse(lines[0]);
            JsonDocument completed = JsonDocument.Parse(lines[^1]);

            MuxAssert.AreEqual(1, started.RootElement.GetProperty("contractVersion").GetInt32(), "started contractVersion");
            MuxAssert.AreEqual("run_started", started.RootElement.GetProperty("eventType").GetString(), "started eventType");
            MuxAssert.AreEqual("print", started.RootElement.GetProperty("commandName").GetString(), "started commandName");
            MuxAssert.IsFalse(started.RootElement.GetProperty("mcp").GetProperty("supported").GetBoolean(), "MCP reported unsupported in print mode");
            MuxAssert.AreEqual(1, completed.RootElement.GetProperty("contractVersion").GetInt32(), "completed contractVersion");
            MuxAssert.AreEqual("run_completed", completed.RootElement.GetProperty("eventType").GetString(), "completed eventType");
            MuxAssert.AreEqual("completed", completed.RootElement.GetProperty("status").GetString(), "completed status");
        }

        private static void ProbeCommandJsonReturnsSuccessPayload()
        {
            using MockHttpServer server = new MockHttpServer();
            server.RegisterResponse("Respond with OK", "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"OK probe successful\"}}]}");
            server.Start();

            CliInvocationResult result = InvokeCli(new[]
            {
                "probe",
                "--output-format", "json",
                "--base-url", server.BaseUrl,
                "--model", "test-model",
                "--adapter-type", "openai-compatible"
            });

            MuxAssert.AreEqual(0, result.ExitCode, "exit code");
            MuxAssert.AreEqual(string.Empty, result.StdErr.Trim(), "stderr");

            JsonDocument json = JsonDocument.Parse(result.StdOut);
            MuxAssert.AreEqual(1, json.RootElement.GetProperty("contractVersion").GetInt32(), "contractVersion");
            MuxAssert.IsTrue(json.RootElement.GetProperty("success").GetBoolean(), "success");
            MuxAssert.AreEqual("test-model", json.RootElement.GetProperty("model").GetString(), "model");
            MuxAssert.AreEqual("probe", json.RootElement.GetProperty("commandName").GetString(), "commandName");
            MuxAssert.IsFalse(json.RootElement.GetProperty("mcpSupported").GetBoolean(), "probe reports MCP unsupported");
        }

        private static void ArmadaStyleRunAndProbeWorkWithConfigDirAndArtifact()
        {
            using MockHttpServer server = new MockHttpServer();
            server.RegisterStreamingResponse("armada launch test", new List<string> { "{\"choices\":[{\"delta\":{\"content\":\"Armada final response.\"},\"finish_reason\":\"stop\"}]}" });
            server.RegisterResponse("Respond with OK", "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"OK probe successful\"}}]}");
            server.Start();

            string configDir = CreateTempConfigDirectory(new[]
            {
                new Dictionary<string, object?>
                {
                    ["name"] = "armada-endpoint",
                    ["adapterType"] = "openai-compatible",
                    ["baseUrl"] = server.BaseUrl,
                    ["model"] = "test-model",
                    ["isDefault"] = true
                }
            });
            string artifactPath = Path.Combine(configDir, "last-message.txt");

            try
            {
                CliInvocationResult printResult = InvokeCli(new[]
                {
                    "print",
                    "--config-dir", configDir,
                    "--output-format", "jsonl",
                    "--output-last-message", artifactPath,
                    "--endpoint", "armada-endpoint",
                    "--yolo",
                    "armada launch test"
                });

                MuxAssert.AreEqual(0, printResult.ExitCode, "print exit code");
                MuxAssert.AreEqual(string.Empty, printResult.StdErr.Trim(), "print stderr");
                MuxAssert.IsTrue(File.Exists(artifactPath), "final-message artifact created");
                MuxAssert.AreEqual("Armada final response.", File.ReadAllText(artifactPath), "artifact content");

                string[] printLines = printResult.StdOut.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
                JsonDocument started = JsonDocument.Parse(printLines[0]);
                MuxAssert.AreEqual(configDir, started.RootElement.GetProperty("configDirectory").GetString(), "configDirectory");
                MuxAssert.AreEqual("armada-endpoint", started.RootElement.GetProperty("endpointName").GetString(), "endpointName");

                CliInvocationResult probeResult = InvokeCli(new[]
                {
                    "probe",
                    "--config-dir", configDir,
                    "--output-format", "json",
                    "--require-tools",
                    "--endpoint", "armada-endpoint"
                });

                MuxAssert.AreEqual(0, probeResult.ExitCode, "probe exit code");
                MuxAssert.AreEqual(string.Empty, probeResult.StdErr.Trim(), "probe stderr");

                JsonDocument probe = JsonDocument.Parse(probeResult.StdOut);
                MuxAssert.IsTrue(probe.RootElement.GetProperty("success").GetBoolean(), "probe success");
                MuxAssert.IsTrue(probe.RootElement.GetProperty("requireTools").GetBoolean(), "probe require-tools mode");
                MuxAssert.IsTrue(probe.RootElement.GetProperty("toolsEnabled").GetBoolean(), "endpoint supports tools");
            }
            finally
            {
                if (Directory.Exists(configDir)) Directory.Delete(configDir, true);
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
            string tempDir = Path.Combine(Path.GetTempPath(), "mux_cli_contract_" + Guid.NewGuid().ToString("N"));
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
