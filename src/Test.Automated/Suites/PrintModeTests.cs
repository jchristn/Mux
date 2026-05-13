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
            await RunTest("ContextCompaction_EmittedWhenHistoryIsTooLarge", ContextCompaction_EmittedWhenHistoryIsTooLarge);
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
        /// Verifies that the agent loop emits context warning and compaction events when
        /// the carried conversation history exceeds the active usable context budget.
        /// </summary>
        private async Task ContextCompaction_EmittedWhenHistoryIsTooLarge()
        {
            using (MockHttpServer server = new MockHttpServer())
            {
                string sseChunk = "{\"choices\":[{\"delta\":{\"content\":\"Context compaction succeeded.\"},\"finish_reason\":\"stop\"}]}";
                server.RegisterStreamingResponse("context compaction test", new List<string> { sseChunk });
                server.Start();

                EndpointConfig endpoint = BuildEndpoint(server.BaseUrl);
                endpoint.ContextWindow = 1024;
                endpoint.MaxTokens = 1024;

                List<ConversationMessage> history = new List<ConversationMessage>();
                for (int i = 0; i < 8; i++)
                {
                    history.Add(new ConversationMessage
                    {
                        Role = RoleEnum.User,
                        Content = new string('u', 100)
                    });
                    history.Add(new ConversationMessage
                    {
                        Role = RoleEnum.Assistant,
                        Content = new string('a', 100)
                    });
                }

                AgentLoopOptions options = new AgentLoopOptions(endpoint)
                {
                    ApprovalPolicy = ApprovalPolicyEnum.AutoApprove,
                    MaxIterations = 5,
                    ConversationHistory = history,
                    CompactionPreserveTurns = 1,
                    ContextWindowSafetyMarginPercent = 15,
                    ContextWarningThresholdPercent = 80,
                    TokenEstimationRatio = 2.0
                };

                List<AgentEvent> events = await CollectEvents(options, "context compaction test");

                bool hasContextStatus = events.Any((AgentEvent e) => e is ContextStatusEvent);
                AssertTrue(hasContextStatus, "Expected a ContextStatusEvent when usage approaches or exceeds the usable budget");

                bool hasContextCompacted = events.Any((AgentEvent e) => e is ContextCompactedEvent);
                AssertTrue(hasContextCompacted, "Expected a ContextCompactedEvent when old history is trimmed");

                bool hasTextEvent = events.Any((AgentEvent e) => e is AssistantTextEvent);
                AssertTrue(hasTextEvent, "Expected the run to continue successfully after compaction");

                bool hasContextLimitError = events.Any((AgentEvent e) =>
                    e is ErrorEvent errorEvent && errorEvent.Code == "context_limit_exceeded");
                AssertFalse(hasContextLimitError, "Did not expect context_limit_exceeded after successful compaction");
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
                Headers = new Dictionary<string, string>()
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
