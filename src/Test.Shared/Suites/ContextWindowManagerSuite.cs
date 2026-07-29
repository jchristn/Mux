namespace Test.Shared.Suites
{
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Agent;
    using Mux.Core.Enums;
    using Mux.Core.Models;
    using Touchstone.Core;

    /// <summary>
    /// Touchstone suite for <see cref="ContextWindowManager"/> context-budget snapshots. Ported from
    /// the <c>ContextWindowManagerTests</c> xUnit suite.
    /// </summary>
    public static class ContextWindowManagerSuite
    {
        /// <summary>
        /// Builds the context-window-manager suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/> for the context-window-manager cases.</returns>
        public static TestSuiteDescriptor Create()
        {
            return new TestSuiteDescriptor(
                "ContextWindowManager",
                "Context budget snapshot computation",
                new List<TestCaseDescriptor>
                {
                    new TestCaseDescriptor(
                        "ContextWindowManager",
                        "GetBudgetSnapshotComputesUsageBreakdown",
                        "Budget snapshot accounts for system prompt, messages, tools, and reserved output",
                        (CancellationToken ct) =>
                        {
                            ContextWindowManager manager = new ContextWindowManager(1000, tokenEstimationRatio: 1.0, safetyMarginPercent: 10);
                            List<ConversationMessage> messages = new List<ConversationMessage>
                            {
                                new ConversationMessage { Role = RoleEnum.User, Content = "hello" },
                                new ConversationMessage { Role = RoleEnum.Assistant, Content = "world" }
                            };
                            List<ToolDefinition> tools = new List<ToolDefinition>
                            {
                                new ToolDefinition { Name = "read_file", Description = "Reads a file", ParametersSchema = new { type = "object" } }
                            };

                            ContextBudgetSnapshot snapshot = manager.GetBudgetSnapshot("system", messages, tools, reservedOutputTokens: 200, warningThresholdPercent: 80);

                            MuxAssert.AreEqual(1000, snapshot.ContextWindowSize, "ContextWindowSize");
                            MuxAssert.AreEqual(100, snapshot.SafetyMarginTokens, "SafetyMarginTokens");
                            MuxAssert.AreEqual(200, snapshot.ReservedOutputTokens, "ReservedOutputTokens");
                            MuxAssert.AreEqual(700, snapshot.UsableInputLimit, "UsableInputLimit");
                            MuxAssert.AreEqual(6, snapshot.SystemPromptTokens, "SystemPromptTokens");
                            MuxAssert.AreEqual(10, snapshot.MessageTokens, "MessageTokens");
                            MuxAssert.IsTrue(snapshot.ToolTokens > 0, "ToolTokens > 0");
                            MuxAssert.AreEqual(snapshot.SystemPromptTokens + snapshot.MessageTokens + snapshot.ToolTokens, snapshot.UsedTokens, "UsedTokens");
                            MuxAssert.AreEqual(snapshot.UsableInputLimit - snapshot.UsedTokens, snapshot.RemainingTokens, "RemainingTokens");
                            MuxAssert.AreEqual(560, snapshot.WarningThresholdTokens, "WarningThresholdTokens");
                            MuxAssert.IsFalse(snapshot.IsApproachingLimit, "IsApproachingLimit");
                            MuxAssert.IsFalse(snapshot.IsOverLimit, "IsOverLimit");
                            return Task.CompletedTask;
                        })
                });
        }
    }
}
