namespace Test.Xunit.Agent
{
    using System.Collections.Generic;
    using global::Xunit;
    using Mux.Core.Agent;
    using Mux.Core.Enums;
    using Mux.Core.Models;

    /// <summary>
    /// Unit tests for <see cref="ConversationCompactionPlanner"/>.
    /// </summary>
    public class ConversationCompactionPlannerTests
    {
        private const string SyntheticPrefix = "[mux summary generated automatically; older conversation condensed]";

        /// <summary>
        /// Verifies that older turns are compacted while the most recent user-led turns are preserved.
        /// </summary>
        [Fact]
        public void CreatePlan_PreservesRecentTurns()
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

            ConversationCompactionPlan plan = ConversationCompactionPlanner.CreatePlan(history, preserveTurns: 2, SyntheticPrefix);

            Assert.True(plan.CanCompact);
            Assert.Collection(
                plan.MessagesToCompact,
                m => Assert.Equal("u1", m.Content),
                m => Assert.Equal("a1", m.Content),
                m => Assert.Equal("u2", m.Content),
                m => Assert.Equal("a2", m.Content));
            Assert.Collection(
                plan.MessagesToPreserve,
                m => Assert.Equal("u3", m.Content),
                m => Assert.Equal("a3", m.Content),
                m => Assert.Equal("u4", m.Content),
                m => Assert.Equal("a4", m.Content));
        }

        /// <summary>
        /// Verifies that synthetic summary messages are excluded before compaction planning.
        /// </summary>
        [Fact]
        public void CreatePlan_RemovesSyntheticSummaryBeforePlanning()
        {
            List<ConversationMessage> history = new List<ConversationMessage>
            {
                new ConversationMessage
                {
                    Role = RoleEnum.System,
                    Content = SyntheticPrefix + "\n\nsummary"
                },
                User("u1"),
                Assistant("a1"),
                User("u2"),
                Assistant("a2"),
                User("u3"),
                Assistant("a3")
            };

            ConversationCompactionPlan plan = ConversationCompactionPlanner.CreatePlan(history, preserveTurns: 2, SyntheticPrefix);

            Assert.Collection(
                plan.MessagesToCompact,
                m => Assert.Equal("u1", m.Content),
                m => Assert.Equal("a1", m.Content));
            Assert.DoesNotContain(plan.MessagesToCompact, m => m.Role == RoleEnum.System);
            Assert.DoesNotContain(plan.MessagesToPreserve, m => m.Role == RoleEnum.System);
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
