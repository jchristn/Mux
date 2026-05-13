namespace Test.Automated.Suites
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
    using Test.Shared;

    /// <summary>
    /// Tests the non-interactive CLI contract used by orchestration consumers.
    /// </summary>
    public class CliContractTests : TestSuite
    {
        #region Private-Members

        private bool _LiveMode;

        #endregion

        #region Public-Members

        /// <summary>
        /// The display name of this test suite.
        /// </summary>
        public override string Name => "CLI Contract Tests";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="CliContractTests"/> class.
        /// </summary>
        public CliContractTests(bool liveMode)
        {
            _LiveMode = liveMode;
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public override async Task RunTestsAsync()
        {
            await RunTest("AgentLoop_EmitsRunLifecycleEvents", AgentLoop_EmitsRunLifecycleEvents);
            await RunTest("PrintCommand_Jsonl_EmitsStructuredEvents", PrintCommand_Jsonl_EmitsStructuredEvents);
            await RunTest("ProbeCommand_Json_ReturnsSuccessPayload", ProbeCommand_Json_ReturnsSuccessPayload);
            await RunTest("ArmadaStyle_RunAndProbe_WorkWithConfigDirAndArtifact", ArmadaStyle_RunAndProbe_WorkWithConfigDirAndArtifact);
        }

        #endregion

        #region Private-Methods

        private async Task AgentLoop_EmitsRunLifecycleEvents()
        {
            using MockHttpServer server = new MockHttpServer();
            string sseChunk = "{\"choices\":[{\"delta\":{\"content\":\"Lifecycle works.\"},\"finish_reason\":\"stop\"}]}";
            server.RegisterStreamingResponse("lifecycle test", new List<string> { sseChunk });
            server.Start();

            EndpointConfig endpoint = new EndpointConfig
            {
                Name = "mock-test",
                AdapterType = AdapterTypeEnum.OpenAiCompatible,
                BaseUrl = server.BaseUrl,
                Model = "test-model",
                Headers = new Dictionary<string, string>()
            };

            AgentLoopOptions options = new AgentLoopOptions(endpoint)
            {
                ApprovalPolicy = ApprovalPolicyEnum.AutoApprove,
                MaxIterations = 5,
                WorkingDirectory = "C:\\Code\\Mux"
            };

            List<AgentEvent> events = new List<AgentEvent>();
            using AgentLoop loop = new AgentLoop(options);
            using CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await foreach (AgentEvent agentEvent in loop.RunAsync("lifecycle test", cts.Token))
            {
                events.Add(agentEvent);
            }

            AssertTrue(events[0] is RunStartedEvent, "Expected the first event to be RunStartedEvent");
            AssertTrue(events[^1] is RunCompletedEvent, "Expected the last event to be RunCompletedEvent");

            RunCompletedEvent completed = (RunCompletedEvent)events[^1];
            AssertEqual("completed", completed.Status);
            AssertEqual(1, completed.IterationsCompleted);
        }

        private Task PrintCommand_Jsonl_EmitsStructuredEvents()
        {
            using MockHttpServer server = new MockHttpServer();
            string sseChunk = "{\"choices\":[{\"delta\":{\"content\":\"CLI jsonl works.\"},\"finish_reason\":\"stop\"}]}";
            server.RegisterStreamingResponse("cli jsonl test", new List<string> { sseChunk });
            server.Start();

            (int exitCode, string stdout, string stderr) = InvokeCli(new[]
            {
                "print",
                "--output-format", "jsonl",
                "--yolo",
                "--base-url", server.BaseUrl,
                "--model", "test-model",
                "--adapter-type", "openai-compatible",
                "cli jsonl test"
            });

            AssertEqual(0, exitCode);
            AssertEqual(string.Empty, stderr.Trim());

            string[] lines = stdout.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            AssertTrue(lines.Length >= 3, "Expected JSONL event output");

            JsonDocument started = JsonDocument.Parse(lines[0]);
            JsonDocument completed = JsonDocument.Parse(lines[^1]);

            AssertEqual(1, started.RootElement.GetProperty("contractVersion").GetInt32());
            AssertEqual("run_started", started.RootElement.GetProperty("eventType").GetString());
            AssertEqual("print", started.RootElement.GetProperty("commandName").GetString());
            AssertFalse(started.RootElement.GetProperty("mcp").GetProperty("supported").GetBoolean(), "Expected MCP to be reported as unsupported in print mode");
            AssertEqual(1, completed.RootElement.GetProperty("contractVersion").GetInt32());
            AssertEqual("run_completed", completed.RootElement.GetProperty("eventType").GetString());
            AssertEqual("completed", completed.RootElement.GetProperty("status").GetString());
            return Task.CompletedTask;
        }

        private Task ProbeCommand_Json_ReturnsSuccessPayload()
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

            AssertEqual(0, exitCode);
            AssertEqual(string.Empty, stderr.Trim());

            JsonDocument json = JsonDocument.Parse(stdout);
            AssertEqual(1, json.RootElement.GetProperty("contractVersion").GetInt32());
            AssertTrue(json.RootElement.GetProperty("success").GetBoolean());
            AssertEqual("test-model", json.RootElement.GetProperty("model").GetString());
            AssertEqual("probe", json.RootElement.GetProperty("commandName").GetString());
            AssertFalse(json.RootElement.GetProperty("mcpSupported").GetBoolean(), "Expected probe to report MCP as unsupported");
            return Task.CompletedTask;
        }

        private Task ArmadaStyle_RunAndProbe_WorkWithConfigDirAndArtifact()
        {
            using MockHttpServer server = new MockHttpServer();
            string printChunk = "{\"choices\":[{\"delta\":{\"content\":\"Armada final response.\"},\"finish_reason\":\"stop\"}]}";
            string probeResponse = "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"OK probe successful\"}}]}";
            server.RegisterStreamingResponse("armada launch test", new List<string> { printChunk });
            server.RegisterResponse("Respond with OK", probeResponse);
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
                (int printExitCode, string printStdout, string printStderr) = InvokeCli(new[]
                {
                    "print",
                    "--config-dir", configDir,
                    "--output-format", "jsonl",
                    "--output-last-message", artifactPath,
                    "--endpoint", "armada-endpoint",
                    "--yolo",
                    "armada launch test"
                });

                AssertEqual(0, printExitCode);
                AssertEqual(string.Empty, printStderr.Trim());
                AssertTrue(File.Exists(artifactPath), "Expected the final-message artifact to be created");
                AssertEqual("Armada final response.", File.ReadAllText(artifactPath));

                string[] printLines = printStdout.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
                JsonDocument started = JsonDocument.Parse(printLines[0]);
                AssertEqual(configDir, started.RootElement.GetProperty("configDirectory").GetString());
                AssertEqual("armada-endpoint", started.RootElement.GetProperty("endpointName").GetString());

                (int probeExitCode, string probeStdout, string probeStderr) = InvokeCli(new[]
                {
                    "probe",
                    "--config-dir", configDir,
                    "--output-format", "json",
                    "--require-tools",
                    "--endpoint", "armada-endpoint"
                });

                AssertEqual(0, probeExitCode);
                AssertEqual(string.Empty, probeStderr.Trim());

                JsonDocument probe = JsonDocument.Parse(probeStdout);
                AssertTrue(probe.RootElement.GetProperty("success").GetBoolean(), "Expected probe to succeed");
                AssertTrue(probe.RootElement.GetProperty("requireTools").GetBoolean(), "Expected probe to report require-tools mode");
                AssertTrue(probe.RootElement.GetProperty("toolsEnabled").GetBoolean(), "Expected the endpoint to support tools");
            }
            finally
            {
                if (Directory.Exists(configDir))
                {
                    Directory.Delete(configDir, true);
                }
            }

            return Task.CompletedTask;
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
            string tempDir = Path.Combine(Path.GetTempPath(), "mux_cli_contract_" + Guid.NewGuid().ToString("N"));
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

        #endregion
    }
}
