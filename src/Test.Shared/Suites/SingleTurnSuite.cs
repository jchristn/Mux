namespace Test.Shared.Suites
{
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Agent;
    using Mux.Core.Enums;
    using Mux.Core.Models;
    using Touchstone.Core;

    /// <summary>
    /// Touchstone suite for single-turn (non-agentic) prompt-response interactions exercised through
    /// <see cref="AgentLoop"/> and a <see cref="MockHttpServer"/>. Ported from the legacy
    /// <c>SingleTurnTests</c> suite; runs in mock mode.
    /// </summary>
    public static class SingleTurnSuite
    {
        /// <summary>
        /// Builds the single-turn suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/> containing all single-turn cases.</returns>
        public static TestSuiteDescriptor Create()
        {
            return new TestSuiteDescriptor(
                "SingleTurn",
                "Single-turn prompt-response interactions",
                new List<TestCaseDescriptor>
                {
                    new TestCaseDescriptor(
                        "SingleTurn",
                        "BasicTextResponse",
                        "Basic text response",
                        async (CancellationToken ct) =>
                        {
                            using (MockHttpServer server = new MockHttpServer())
                            {
                                server.RegisterStreamingResponse("hello", new List<string> { AgentTestHarness.BuildTextSseChunk("Hello from the mock!") });
                                server.Start();

                                AgentLoopOptions options = new AgentLoopOptions(AgentTestHarness.BuildMockEndpoint(server.BaseUrl))
                                {
                                    ApprovalPolicy = ApprovalPolicyEnum.AutoApprove,
                                    MaxIterations = 1
                                };

                                List<AgentEvent> events = await AgentTestHarness.CollectEventsAsync(options, "hello", ct).ConfigureAwait(false);

                                MuxAssert.IsTrue(events.Any((AgentEvent e) => e is AssistantTextEvent), "at least one AssistantTextEvent");
                                MuxAssert.Contains("Hello from the mock!", AgentTestHarness.CombineAssistantText(events), "assistant text");
                            }
                        }),

                    new TestCaseDescriptor(
                        "SingleTurn",
                        "MinimalPromptStillWorks",
                        "Minimal prompt still works",
                        async (CancellationToken ct) =>
                        {
                            using (MockHttpServer server = new MockHttpServer())
                            {
                                server.RegisterStreamingResponse(".", new List<string> { AgentTestHarness.BuildTextSseChunk("Acknowledged.") });
                                server.Start();

                                AgentLoopOptions options = new AgentLoopOptions(AgentTestHarness.BuildMockEndpoint(server.BaseUrl))
                                {
                                    ApprovalPolicy = ApprovalPolicyEnum.AutoApprove,
                                    MaxIterations = 1
                                };

                                List<AgentEvent> events = await AgentTestHarness.CollectEventsAsync(options, ".", ct).ConfigureAwait(false);

                                MuxAssert.IsTrue(events.Any((AgentEvent e) => e is AssistantTextEvent), "at least one AssistantTextEvent for minimal prompt");
                            }
                        }),

                    new TestCaseDescriptor(
                        "SingleTurn",
                        "MultiTurnConversation",
                        "Multi-turn conversation with shared history",
                        async (CancellationToken ct) =>
                        {
                            using (MockHttpServer server = new MockHttpServer())
                            {
                                server.RegisterStreamingResponse("turn1", new List<string> { AgentTestHarness.BuildTextSseChunk("First response.") });
                                server.RegisterStreamingResponse("turn2", new List<string> { AgentTestHarness.BuildTextSseChunk("Second response.") });
                                server.Start();

                                EndpointConfig endpoint = AgentTestHarness.BuildMockEndpoint(server.BaseUrl);

                                AgentLoopOptions firstOptions = new AgentLoopOptions(endpoint)
                                {
                                    ApprovalPolicy = ApprovalPolicyEnum.AutoApprove,
                                    MaxIterations = 1
                                };

                                List<AgentEvent> firstEvents = await AgentTestHarness.CollectEventsAsync(firstOptions, "turn1", ct).ConfigureAwait(false);
                                string firstText = AgentTestHarness.CombineAssistantText(firstEvents);
                                MuxAssert.Contains("First response.", firstText, "first turn text");

                                List<ConversationMessage> history = new List<ConversationMessage>
                                {
                                    new ConversationMessage { Role = RoleEnum.User, Content = "turn1" },
                                    new ConversationMessage { Role = RoleEnum.Assistant, Content = firstText }
                                };

                                AgentLoopOptions secondOptions = new AgentLoopOptions(endpoint)
                                {
                                    ApprovalPolicy = ApprovalPolicyEnum.AutoApprove,
                                    MaxIterations = 1,
                                    ConversationHistory = history
                                };

                                List<AgentEvent> secondEvents = await AgentTestHarness.CollectEventsAsync(secondOptions, "turn2", ct).ConfigureAwait(false);
                                MuxAssert.Contains("Second response.", AgentTestHarness.CombineAssistantText(secondEvents), "second turn text");
                            }
                        })
                });
        }
    }
}
