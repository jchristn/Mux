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

        #endregion
    }
}
