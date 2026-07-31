namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Agent;
    using Mux.Core.Enums;
    using Mux.Core.Models;
    using Touchstone.Core;

    /// <summary>
    /// Touchstone suite for <see cref="AgentLoopOptions"/> validation and <see cref="AgentLoop"/>
    /// construction/default behavior. Ported from the <c>AgentLoopTests</c> xUnit suite. Full
    /// agent-loop integration coverage lives in the <see cref="SingleTurnSuite"/> and related suites.
    /// </summary>
    public static class AgentLoopOptionsSuite
    {
        private static EndpointConfig BuildEndpoint(AdapterTypeEnum adapterType = AdapterTypeEnum.OpenAiCompatible)
        {
            return new EndpointConfig
            {
                Name = "test",
                BaseUrl = "http://localhost:11434",
                Model = "test-model",
                AdapterType = adapterType
            };
        }

        /// <summary>
        /// Builds the agent-loop-options suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/> for the agent-loop-options cases.</returns>
        public static TestSuiteDescriptor Create()
        {
            return new TestSuiteDescriptor(
                "AgentLoopOptions",
                "Agent loop options validation and construction",
                new List<TestCaseDescriptor>
                {
                    new TestCaseDescriptor(
                        "AgentLoopOptions",
                        "NullEndpointThrows",
                        "AgentLoopOptions requires a non-null endpoint",
                        (CancellationToken ct) =>
                        {
                            MuxAssert.Throws<ArgumentNullException>(() => new AgentLoopOptions(null!), "null endpoint");
                            return Task.CompletedTask;
                        }),

                    new TestCaseDescriptor(
                        "AgentLoopOptions",
                        "DefaultsAreCorrect",
                        "AgentLoopOptions sets default values correctly",
                        (CancellationToken ct) =>
                        {
                            EndpointConfig endpoint = BuildEndpoint();
                            AgentLoopOptions options = new AgentLoopOptions(endpoint);

                            MuxAssert.AreEqual(endpoint, options.Endpoint, "Endpoint");
                            MuxAssert.IsNotNull(options.ConversationHistory, "ConversationHistory not null");
                            MuxAssert.AreEqual(0, options.ConversationHistory.Count, "ConversationHistory empty");
                            MuxAssert.AreEqual(string.Empty, options.SystemPrompt, "SystemPrompt");
                            MuxAssert.AreEqual(ApprovalPolicyEnum.Ask, options.ApprovalPolicy, "ApprovalPolicy");
                            MuxAssert.AreEqual(50, options.MaxIterations, "MaxIterations");
                            MuxAssert.IsFalse(options.Verbose, "Verbose");
                            MuxAssert.AreEqual(3.5, options.TokenEstimationRatio, "TokenEstimationRatio");
                            MuxAssert.AreEqual(15, options.ContextWindowSafetyMarginPercent, "ContextWindowSafetyMarginPercent");
                            MuxAssert.IsTrue(options.AutoCompactEnabled, "AutoCompactEnabled");
                            MuxAssert.AreEqual(80, options.ContextWarningThresholdPercent, "ContextWarningThresholdPercent");
                            MuxAssert.AreEqual("summary", options.CompactionStrategy, "CompactionStrategy");
                            MuxAssert.AreEqual(3, options.CompactionPreserveTurns, "CompactionPreserveTurns");
                            MuxAssert.IsNull(options.AdditionalTools, "AdditionalTools");
                            MuxAssert.IsNull(options.ExternalToolProviders, "ExternalToolProviders");
                            MuxAssert.IsNull(options.PromptUserFunc, "PromptUserFunc");
                            MuxAssert.IsNull(options.ExternalToolExecutor, "ExternalToolExecutor");
                            return Task.CompletedTask;
                        }),

                    new TestCaseDescriptor(
                        "AgentLoopOptions",
                        "MaxIterationsClamped",
                        "MaxIterations is clamped to the valid range",
                        (CancellationToken ct) =>
                        {
                            AgentLoopOptions options = new AgentLoopOptions(BuildEndpoint());

                            options.MaxIterations = 0;
                            MuxAssert.AreEqual(1, options.MaxIterations, "clamped low");
                            options.MaxIterations = 200;
                            MuxAssert.AreEqual(100, options.MaxIterations, "clamped high");
                            options.MaxIterations = 50;
                            MuxAssert.AreEqual(50, options.MaxIterations, "within range");
                            return Task.CompletedTask;
                        }),

                    new TestCaseDescriptor(
                        "AgentLoopOptions",
                        "NullConversationHistoryBecomesEmptyList",
                        "Setting ConversationHistory to null results in an empty list",
                        (CancellationToken ct) =>
                        {
                            AgentLoopOptions options = new AgentLoopOptions(BuildEndpoint());
                            options.ConversationHistory = null!;
                            MuxAssert.IsNotNull(options.ConversationHistory, "ConversationHistory not null");
                            MuxAssert.AreEqual(0, options.ConversationHistory.Count, "ConversationHistory empty");
                            return Task.CompletedTask;
                        }),

                    new TestCaseDescriptor(
                        "AgentLoopOptions",
                        "NullSystemPromptBecomesEmpty",
                        "Setting SystemPrompt to null results in an empty string",
                        (CancellationToken ct) =>
                        {
                            AgentLoopOptions options = new AgentLoopOptions(BuildEndpoint());
                            options.SystemPrompt = null!;
                            MuxAssert.AreEqual(string.Empty, options.SystemPrompt, "SystemPrompt");
                            return Task.CompletedTask;
                        }),

                    new TestCaseDescriptor(
                        "AgentLoopOptions",
                        "NullOptionsThrows",
                        "AgentLoop requires a non-null options argument",
                        (CancellationToken ct) =>
                        {
                            MuxAssert.Throws<ArgumentNullException>(() => new AgentLoop(null!), "null options");
                            return Task.CompletedTask;
                        }),

                    new TestCaseDescriptor(
                        "AgentLoopOptions",
                        "ConstructAndDisposeNoErrors",
                        "AgentLoop constructs and disposes without errors",
                        (CancellationToken ct) =>
                        {
                            AgentLoopOptions options = new AgentLoopOptions(BuildEndpoint(AdapterTypeEnum.Ollama));
                            using (AgentLoop loop = new AgentLoop(options))
                            {
                                MuxAssert.IsNotNull(loop, "loop");
                            }
                            return Task.CompletedTask;
                        }),

                    new TestCaseDescriptor(
                        "AgentLoopOptions",
                        "EmptyPromptThrows",
                        "RunAsync throws when given an empty prompt",
                        async (CancellationToken ct) =>
                        {
                            AgentLoopOptions options = new AgentLoopOptions(BuildEndpoint(AdapterTypeEnum.Ollama));
                            using (AgentLoop loop = new AgentLoop(options))
                            {
                                await MuxAssert.ThrowsAsync<ArgumentException>(async () =>
                                {
                                    await foreach (AgentEvent agentEvent in loop.RunAsync("").ConfigureAwait(false))
                                    {
                                    }
                                }, "empty prompt").ConfigureAwait(false);
                            }
                        })
                });
        }
    }
}
