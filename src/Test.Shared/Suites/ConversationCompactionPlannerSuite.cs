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
    /// Touchstone suite for <see cref="ConversationCompactionPlanner"/>. Ported from the
    /// <c>ConversationCompactionPlannerTests</c> xUnit suite.
    /// </summary>
    public static class ConversationCompactionPlannerSuite
    {
        private const string SyntheticPrefix = "[mux summary generated automatically; older conversation condensed]";

        private static ConversationMessage User(string content) => new ConversationMessage { Role = RoleEnum.User, Content = content };

        private static ConversationMessage Assistant(string content) => new ConversationMessage { Role = RoleEnum.Assistant, Content = content };

        /// <summary>
        /// Builds the compaction-planner suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/> for the compaction-planner cases.</returns>
        public static TestSuiteDescriptor Create()
        {
            return new TestSuiteDescriptor(
                "ConversationCompactionPlanner",
                "Conversation compaction planning",
                new List<TestCaseDescriptor>
                {
                    new TestCaseDescriptor(
                        "ConversationCompactionPlanner",
                        "CreatePlanPreservesRecentTurns",
                        "Older turns are compacted while recent turns are preserved",
                        (CancellationToken ct) =>
                        {
                            List<ConversationMessage> history = new List<ConversationMessage>
                            {
                                User("u1"), Assistant("a1"), User("u2"), Assistant("a2"),
                                User("u3"), Assistant("a3"), User("u4"), Assistant("a4")
                            };

                            ConversationCompactionPlan plan = ConversationCompactionPlanner.CreatePlan(history, preserveTurns: 2, SyntheticPrefix);

                            MuxAssert.IsTrue(plan.CanCompact, "CanCompact");
                            AssertContents(plan.MessagesToCompact, new[] { "u1", "a1", "u2", "a2" }, "MessagesToCompact");
                            AssertContents(plan.MessagesToPreserve, new[] { "u3", "a3", "u4", "a4" }, "MessagesToPreserve");
                            return Task.CompletedTask;
                        }),

                    new TestCaseDescriptor(
                        "ConversationCompactionPlanner",
                        "CreatePlanRemovesSyntheticSummaryBeforePlanning",
                        "Synthetic summary messages are excluded before planning",
                        (CancellationToken ct) =>
                        {
                            List<ConversationMessage> history = new List<ConversationMessage>
                            {
                                new ConversationMessage { Role = RoleEnum.System, Content = SyntheticPrefix + "\n\nsummary" },
                                User("u1"), Assistant("a1"), User("u2"), Assistant("a2"), User("u3"), Assistant("a3")
                            };

                            ConversationCompactionPlan plan = ConversationCompactionPlanner.CreatePlan(history, preserveTurns: 2, SyntheticPrefix);

                            AssertContents(plan.MessagesToCompact, new[] { "u1", "a1" }, "MessagesToCompact");
                            MuxAssert.IsFalse(plan.MessagesToCompact.Any(m => m.Role == RoleEnum.System), "no System in MessagesToCompact");
                            MuxAssert.IsFalse(plan.MessagesToPreserve.Any(m => m.Role == RoleEnum.System), "no System in MessagesToPreserve");
                            return Task.CompletedTask;
                        })
                });
        }

        private static void AssertContents(IReadOnlyList<ConversationMessage> actual, IReadOnlyList<string> expectedContents, string label)
        {
            MuxAssert.AreEqual(expectedContents.Count, actual.Count, label + " count");
            for (int i = 0; i < expectedContents.Count; i++)
            {
                MuxAssert.AreEqual(expectedContents[i], actual[i].Content, label + " [" + i + "]");
            }
        }
    }
}
