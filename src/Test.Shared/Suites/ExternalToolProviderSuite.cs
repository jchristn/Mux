namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Agent;
    using Mux.Core.Enums;
    using Mux.Core.Models;
    using Mux.Core.Tools;
    using Touchstone.Core;

    /// <summary>
    /// Touchstone suite for the agent loop's external-tool provider seam: a tool call for a name the
    /// built-in registry does not own is routed to the first <see cref="IExternalToolProvider"/> that
    /// claims it, and the provider's result flows back through the loop.
    /// </summary>
    public static class ExternalToolProviderSuite
    {
        private const string SuiteId = "ExternalToolProvider";

        /// <summary>
        /// Builds the external-tool-provider suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/> for the provider-seam cases.</returns>
        public static TestSuiteDescriptor Create()
        {
            return new TestSuiteDescriptor(
                SuiteId,
                "Agent loop routes tool calls to external tool providers",
                new List<TestCaseDescriptor>
                {
                    new TestCaseDescriptor(SuiteId, "ProviderToolIsRoutedAndExecuted", "A tool call routes to the owning provider and its result returns", (CancellationToken ct) =>
                        ProviderToolIsRoutedAndExecutedAsync(ct))
                });
        }

        private static async Task ProviderToolIsRoutedAndExecutedAsync(CancellationToken ct)
        {
            using (MockHttpServer server = new MockHttpServer())
            {
                string toolCallChunk = "{\"choices\":[{\"delta\":{\"tool_calls\":[{\"index\":0,\"id\":\"call_p\",\"function\":{\"name\":\"provider_echo\",\"arguments\":\"{}\"}}]},\"finish_reason\":\"tool_calls\"}]}";
                string followUpChunk = "{\"choices\":[{\"delta\":{\"content\":\"The provider ran.\"},\"finish_reason\":\"stop\"}]}";

                // The mock matches the longest registered substring found in the request body, so the
                // follow-up key (present in the tool result) is deliberately longer than the prompt key,
                // ensuring the loop stops after the tool runs once instead of re-proposing the call.
                server.RegisterStreamingResponse("runtool", new List<string> { toolCallChunk });
                server.RegisterStreamingResponse("PROVIDER_RAN", new List<string> { followUpChunk });
                server.Start();

                FakeExternalToolProvider provider = new FakeExternalToolProvider("provider_echo", ToolMutationKind.ReadOnly);

                AgentLoopOptions options = new AgentLoopOptions(AgentTestHarness.BuildMockEndpoint(server.BaseUrl))
                {
                    ApprovalPolicy = ApprovalPolicyEnum.AutoApprove,
                    MaxIterations = 5,
                    ExternalToolProviders = new List<IExternalToolProvider> { provider }
                };

                List<AgentEvent> events = await AgentTestHarness.CollectEventsAsync(options, "runtool please", ct).ConfigureAwait(false);

                MuxAssert.IsTrue(events.Any((AgentEvent e) => e is ToolCallProposedEvent), "a ToolCallProposedEvent");

                ToolCallCompletedEvent? completed = events.OfType<ToolCallCompletedEvent>().FirstOrDefault();
                MuxAssert.IsNotNull(completed, "a ToolCallCompletedEvent");
                MuxAssert.IsTrue(completed!.Result.Success, "provider tool succeeded");
                MuxAssert.Contains("PROVIDER_RAN", completed.Result.Content, "provider result flowed back");
                MuxAssert.AreEqual(1, provider.ExecuteCount, "provider executed exactly once");
            }
        }
    }
}
