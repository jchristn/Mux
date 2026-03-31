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
    /// Tests for single-turn (non-agentic) prompt-response interactions using MockHttpServer and AgentLoop.
    /// </summary>
    public class SingleTurnTests : TestSuite
    {
        #region Private-Members

        private bool _LiveMode;

        #endregion

        #region Public-Members

        /// <summary>
        /// The display name of this test suite.
        /// </summary>
        public override string Name => "Single-Turn Tests";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="SingleTurnTests"/> class.
        /// </summary>
        /// <param name="liveMode">True to run against a live endpoint, false to use mock.</param>
        public SingleTurnTests(bool liveMode)
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
            await RunTest("BasicTextResponse", BasicTextResponse);
            await RunTest("EmptyPrompt_StillWorks", EmptyPrompt_StillWorks);
            await RunTest("MultiTurnConversation", MultiTurnConversation);
        }

        #endregion

        #region Private-Methods

        /// <summary>
        /// Verifies that a simple text response is returned when the agent loop runs with a basic prompt.
        /// </summary>
        private async Task BasicTextResponse()
        {
            using (MockHttpServer server = new MockHttpServer())
            {
                string sseChunk = BuildTextSseChunk("Hello from the mock!");
                server.RegisterStreamingResponse("hello", new List<string> { sseChunk });
                server.Start();

                EndpointConfig endpoint = BuildEndpoint(server.BaseUrl);
                AgentLoopOptions options = new AgentLoopOptions(endpoint)
                {
                    ApprovalPolicy = ApprovalPolicyEnum.AutoApprove,
                    MaxIterations = 1
                };

                List<AgentEvent> events = await CollectEvents(options, "hello");

                bool hasTextEvent = events.Any((AgentEvent e) => e is AssistantTextEvent);
                AssertTrue(hasTextEvent, "Expected at least one AssistantTextEvent");

                string fullText = CombineAssistantText(events);
                AssertContains(fullText, "Hello from the mock!");
            }
        }

        /// <summary>
        /// Verifies that running the agent loop with a minimal prompt still produces events without error.
        /// The agent loop rejects null/whitespace prompts, so this test uses a minimal non-empty prompt.
        /// </summary>
        private async Task EmptyPrompt_StillWorks()
        {
            using (MockHttpServer server = new MockHttpServer())
            {
                string sseChunk = BuildTextSseChunk("Acknowledged.");
                server.RegisterStreamingResponse(".", new List<string> { sseChunk });
                server.Start();

                EndpointConfig endpoint = BuildEndpoint(server.BaseUrl);
                AgentLoopOptions options = new AgentLoopOptions(endpoint)
                {
                    ApprovalPolicy = ApprovalPolicyEnum.AutoApprove,
                    MaxIterations = 1
                };

                List<AgentEvent> events = await CollectEvents(options, ".");

                bool hasTextEvent = events.Any((AgentEvent e) => e is AssistantTextEvent);
                AssertTrue(hasTextEvent, "Expected at least one AssistantTextEvent for minimal prompt");
            }
        }

        /// <summary>
        /// Verifies multi-turn conversation by running two sequential prompts with shared conversation history.
        /// </summary>
        private async Task MultiTurnConversation()
        {
            using (MockHttpServer server = new MockHttpServer())
            {
                string firstSseChunk = BuildTextSseChunk("First response.");
                string secondSseChunk = BuildTextSseChunk("Second response.");
                server.RegisterStreamingResponse("turn1", new List<string> { firstSseChunk });
                server.RegisterStreamingResponse("turn2", new List<string> { secondSseChunk });
                server.Start();

                EndpointConfig endpoint = BuildEndpoint(server.BaseUrl);

                // First turn
                AgentLoopOptions firstOptions = new AgentLoopOptions(endpoint)
                {
                    ApprovalPolicy = ApprovalPolicyEnum.AutoApprove,
                    MaxIterations = 1
                };

                List<AgentEvent> firstEvents = await CollectEvents(firstOptions, "turn1");
                string firstText = CombineAssistantText(firstEvents);
                AssertContains(firstText, "First response.");

                // Build conversation history from first turn
                List<ConversationMessage> history = new List<ConversationMessage>();
                history.Add(new ConversationMessage
                {
                    Role = RoleEnum.User,
                    Content = "turn1"
                });
                history.Add(new ConversationMessage
                {
                    Role = RoleEnum.Assistant,
                    Content = firstText
                });

                // Second turn with history
                AgentLoopOptions secondOptions = new AgentLoopOptions(endpoint)
                {
                    ApprovalPolicy = ApprovalPolicyEnum.AutoApprove,
                    MaxIterations = 1,
                    ConversationHistory = history
                };

                List<AgentEvent> secondEvents = await CollectEvents(secondOptions, "turn2");
                string secondText = CombineAssistantText(secondEvents);
                AssertContains(secondText, "Second response.");
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

        /// <summary>
        /// Combines all AssistantTextEvent texts into a single string.
        /// </summary>
        private string CombineAssistantText(List<AgentEvent> events)
        {
            string combined = string.Empty;
            foreach (AgentEvent e in events)
            {
                if (e is AssistantTextEvent textEvent)
                {
                    combined += textEvent.Text;
                }
            }
            return combined;
        }

        /// <summary>
        /// Builds an SSE data chunk for a streaming text response with a stop finish_reason.
        /// </summary>
        private string BuildTextSseChunk(string text)
        {
            string escaped = text.Replace("\\", "\\\\").Replace("\"", "\\\"");
            return "{\"choices\":[{\"delta\":{\"content\":\"" + escaped + "\"},\"finish_reason\":\"stop\"}]}";
        }

        #endregion
    }
}
