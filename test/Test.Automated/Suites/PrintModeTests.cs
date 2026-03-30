namespace Test.Automated.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Agent;
    using Mux.Core.Enums;
    using Mux.Core.Models;
    using Test.Shared;

    /// <summary>
    /// Tests for print mode (non-interactive, single-shot) operation verifying completion, heartbeats, and iteration limits.
    /// </summary>
    public class PrintModeTests : TestSuite
    {
        #region Private-Members

        private bool _LiveMode;

        #endregion

        #region Public-Members

        /// <summary>
        /// The display name of this test suite.
        /// </summary>
        public override string Name => "Print-Mode Tests";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="PrintModeTests"/> class.
        /// </summary>
        /// <param name="liveMode">True to run against a live endpoint, false to use mock.</param>
        public PrintModeTests(bool liveMode)
        {
            _LiveMode = liveMode;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Defines and runs all tests in this suite.
        /// </summary>
        /// <returns>A task representing the asynchronous operation.</returns>
        public override async Task RunTestsAsync()
        {
            await RunTest("AgentLoop_CompletesSuccessfully", AgentLoop_CompletesSuccessfully);
            await RunTest("HeartbeatEmitted_WhenToolsUsed", HeartbeatEmitted_WhenToolsUsed);
            await RunTest("MaxIterations_StopsLoop", MaxIterations_StopsLoop);
        }

        #endregion

        #region Private-Methods

        /// <summary>
        /// Verifies that the agent loop completes successfully with a simple text response
        /// and emits no ErrorEvent instances.
        /// </summary>
        private async Task AgentLoop_CompletesSuccessfully()
        {
            using (MockHttpServer server = new MockHttpServer())
            {
                string sseChunk = "{\"choices\":[{\"delta\":{\"content\":\"Print mode works.\"},\"finish_reason\":\"stop\"}]}";
                server.RegisterStreamingResponse("print test", new List<string> { sseChunk });
                server.Start();

                EndpointConfig endpoint = BuildEndpoint(server.BaseUrl);
                AgentLoopOptions options = new AgentLoopOptions(endpoint)
                {
                    ApprovalPolicy = ApprovalPolicyEnum.AutoApprove,
                    MaxIterations = 5
                };

                List<AgentEvent> events = await CollectEvents(options, "print test");

                bool hasTextEvent = events.Any((AgentEvent e) => e is AssistantTextEvent);
                AssertTrue(hasTextEvent, "Expected at least one AssistantTextEvent");

                bool hasErrorEvent = events.Any((AgentEvent e) => e is ErrorEvent);
                AssertFalse(hasErrorEvent, "Expected no ErrorEvent in a successful completion");
            }
        }

        /// <summary>
        /// Verifies that a HeartbeatEvent is emitted when the agent loop processes tool calls.
        /// </summary>
        private async Task HeartbeatEmitted_WhenToolsUsed()
        {
            using (MockHttpServer server = new MockHttpServer())
            {
                // First response: model proposes a tool call
                string toolCallChunk = "{\"choices\":[{\"delta\":{\"tool_calls\":[{\"index\":0,\"id\":\"call_hb\",\"function\":{\"name\":\"read_file\",\"arguments\":\"{\\\"file_path\\\":\\\"nonexistent.txt\\\"}\"}}]},\"finish_reason\":\"tool_calls\"}]}";

                // Follow-up response after tool result
                string followUpChunk = "{\"choices\":[{\"delta\":{\"content\":\"Done with heartbeat test.\"},\"finish_reason\":\"stop\"}]}";

                server.RegisterStreamingResponse("heartbeat test", new List<string> { toolCallChunk });
                server.RegisterStreamingResponse("nonexistent.txt", new List<string> { followUpChunk });
                server.Start();

                EndpointConfig endpoint = BuildEndpoint(server.BaseUrl);
                AgentLoopOptions options = new AgentLoopOptions(endpoint)
                {
                    ApprovalPolicy = ApprovalPolicyEnum.AutoApprove,
                    MaxIterations = 5
                };

                List<AgentEvent> events = await CollectEvents(options, "heartbeat test");

                bool hasHeartbeat = events.Any((AgentEvent e) => e is HeartbeatEvent);
                AssertTrue(hasHeartbeat, "Expected a HeartbeatEvent when tools are used");
            }
        }

        /// <summary>
        /// Verifies that the agent loop stops after reaching the maximum number of iterations
        /// and emits an ErrorEvent with code "max_iterations_reached".
        /// </summary>
        private async Task MaxIterations_StopsLoop()
        {
            using (MockHttpServer server = new MockHttpServer())
            {
                // Register a response that always returns a tool call, creating an infinite loop scenario.
                // The mock server route matching uses Contains, so any request body containing "loop" will match.
                string toolCallChunk = "{\"choices\":[{\"delta\":{\"tool_calls\":[{\"index\":0,\"id\":\"call_loop\",\"function\":{\"name\":\"read_file\",\"arguments\":\"{\\\"file_path\\\":\\\"loop.txt\\\"}\"}}]},\"finish_reason\":\"tool_calls\"}]}";

                server.RegisterStreamingResponse("loop", new List<string> { toolCallChunk });
                server.Start();

                EndpointConfig endpoint = BuildEndpoint(server.BaseUrl);
                AgentLoopOptions options = new AgentLoopOptions(endpoint)
                {
                    ApprovalPolicy = ApprovalPolicyEnum.AutoApprove,
                    MaxIterations = 2
                };

                List<AgentEvent> events = await CollectEvents(options, "infinite loop test");

                bool hasMaxIterationsError = events.Any((AgentEvent e) =>
                    e is ErrorEvent errorEvent && errorEvent.Code == "max_iterations_reached");
                AssertTrue(hasMaxIterationsError, "Expected ErrorEvent with code 'max_iterations_reached'");
            }
        }

        /// <summary>
        /// Builds an endpoint configuration pointing to the mock server.
        /// </summary>
        private EndpointConfig BuildEndpoint(string baseUrl)
        {
            EndpointConfig endpoint = new EndpointConfig
            {
                Name = "mock-test",
                AdapterType = AdapterTypeEnum.OpenAiCompatible,
                BaseUrl = baseUrl,
                Model = "test-model",
                ApiKey = "test-key"
            };
            return endpoint;
        }

        /// <summary>
        /// Collects all events from an agent loop run into a list.
        /// </summary>
        private async Task<List<AgentEvent>> CollectEvents(AgentLoopOptions options, string prompt)
        {
            List<AgentEvent> events = new List<AgentEvent>();
            using (AgentLoop loop = new AgentLoop(options))
            {
                CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                await foreach (AgentEvent agentEvent in loop.RunAsync(prompt, cts.Token))
                {
                    events.Add(agentEvent);
                }
            }
            return events;
        }

        #endregion
    }
}
