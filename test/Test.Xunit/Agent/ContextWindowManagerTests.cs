namespace Test.Xunit.Agent
{
    using System.Collections.Generic;
    using global::Xunit;
    using Mux.Core.Agent;
    using Mux.Core.Enums;
    using Mux.Core.Models;

    /// <summary>
    /// Unit tests for <see cref="ContextWindowManager"/> context budget snapshots.
    /// </summary>
    public class ContextWindowManagerTests
    {
        /// <summary>
        /// Verifies that the budget snapshot accounts for system prompt, messages, tools, and reserved output.
        /// </summary>
        [Fact]
        public void GetBudgetSnapshot_ComputesUsageBreakdown()
        {
            ContextWindowManager manager = new ContextWindowManager(1000, tokenEstimationRatio: 1.0, safetyMarginPercent: 10);
            List<ConversationMessage> messages = new List<ConversationMessage>
            {
                new ConversationMessage
                {
                    Role = RoleEnum.User,
                    Content = "hello"
                },
                new ConversationMessage
                {
                    Role = RoleEnum.Assistant,
                    Content = "world"
                }
            };
            List<ToolDefinition> tools = new List<ToolDefinition>
            {
                new ToolDefinition
                {
                    Name = "read_file",
                    Description = "Reads a file",
                    ParametersSchema = new { type = "object" }
                }
            };

            ContextBudgetSnapshot snapshot = manager.GetBudgetSnapshot(
                "system",
                messages,
                tools,
                reservedOutputTokens: 200,
                warningThresholdPercent: 80);

            Assert.Equal(1000, snapshot.ContextWindowSize);
            Assert.Equal(100, snapshot.SafetyMarginTokens);
            Assert.Equal(200, snapshot.ReservedOutputTokens);
            Assert.Equal(700, snapshot.UsableInputLimit);
            Assert.Equal(6, snapshot.SystemPromptTokens);
            Assert.Equal(10, snapshot.MessageTokens);
            Assert.True(snapshot.ToolTokens > 0);
            Assert.Equal(snapshot.SystemPromptTokens + snapshot.MessageTokens + snapshot.ToolTokens, snapshot.UsedTokens);
            Assert.Equal(snapshot.UsableInputLimit - snapshot.UsedTokens, snapshot.RemainingTokens);
            Assert.Equal(560, snapshot.WarningThresholdTokens);
            Assert.False(snapshot.IsApproachingLimit);
            Assert.False(snapshot.IsOverLimit);
        }
    }
}
