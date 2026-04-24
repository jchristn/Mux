namespace Test.Xunit.Agent
{
    using System.Collections.Generic;
    using global::Xunit;
    using Mux.Core.Agent;
    using Mux.Core.Enums;
    using Mux.Core.Models;

    /// <summary>
    /// Unit tests for <see cref="ConversationTrimCompactor"/>.
    /// </summary>
    public class ConversationTrimCompactorTests
    {
        /// <summary>
        /// Verifies that trim-to-target preserves leading system memory and recent turns.
        /// </summary>
        [Fact]
        public void TrimToTarget_PreservesLeadingSystemMessageAndRecentTurns()
        {
            List<ConversationMessage> history = new List<ConversationMessage>
            {
                System("summary"),
                User("u1"),
                Assistant("a1"),
                User("u2"),
                Assistant("a2"),
                User("u3"),
                Assistant("a3")
            };

            ConversationTrimResult result = ConversationTrimCompactor.TrimToTarget(
                history,
                preserveTurns: 2,
                targetUsedTokens: 15,
                estimateUsedTokens: EstimateContentLength);

            Assert.True(result.DidTrim);
            Assert.True(result.ReachedTarget);
            Assert.Collection(
                result.CompactedHistory,
                m => Assert.Equal("summary", m.Content),
                m => Assert.Equal("u2", m.Content),
                m => Assert.Equal("a2", m.Content),
                m => Assert.Equal("u3", m.Content),
                m => Assert.Equal("a3", m.Content));
        }

        /// <summary>
        /// Verifies that trim-all removes every eligible older message while preserving the recent tail.
        /// </summary>
        [Fact]
        public void TrimAllEligible_RemovesAllCompactablePrefix()
        {
            List<ConversationMessage> history = new List<ConversationMessage>
            {
                User("u1"),
                Assistant("a1"),
                User("u2"),
                Assistant("a2"),
                User("u3"),
                Assistant("a3"),
                User("u4"),
                Assistant("a4")
            };

            ConversationTrimResult result = ConversationTrimCompactor.TrimAllEligible(
                history,
                preserveTurns: 2,
                estimateUsedTokens: EstimateContentLength);

            Assert.True(result.DidTrim);
            Assert.Collection(
                result.CompactedHistory,
                m => Assert.Equal("u3", m.Content),
                m => Assert.Equal("a3", m.Content),
                m => Assert.Equal("u4", m.Content),
                m => Assert.Equal("a4", m.Content));
        }

        private static int EstimateContentLength(List<ConversationMessage> history)
        {
            int total = 0;

            foreach (ConversationMessage message in history)
            {
                total += message.Content?.Length ?? 0;
            }

            return total;
        }

        private static ConversationMessage System(string content)
        {
            return new ConversationMessage
            {
                Role = RoleEnum.System,
                Content = content
            };
        }

        private static ConversationMessage User(string content)
        {
            return new ConversationMessage
            {
                Role = RoleEnum.User,
                Content = content
            };
        }

        private static ConversationMessage Assistant(string content)
        {
            return new ConversationMessage
            {
                Role = RoleEnum.Assistant,
                Content = content
            };
        }
    }
}
