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
    /// Touchstone suite for <see cref="ConversationTrimCompactor"/>. Ported from the
    /// <c>ConversationTrimCompactorTests</c> xUnit suite.
    /// </summary>
    public static class ConversationTrimCompactorSuite
    {
        private static ConversationMessage System(string content) => new ConversationMessage { Role = RoleEnum.System, Content = content };

        private static ConversationMessage User(string content) => new ConversationMessage { Role = RoleEnum.User, Content = content };

        private static ConversationMessage Assistant(string content) => new ConversationMessage { Role = RoleEnum.Assistant, Content = content };

        private static int EstimateContentLength(List<ConversationMessage> history)
        {
            int total = 0;
            foreach (ConversationMessage message in history)
            {
                total += message.Content?.Length ?? 0;
            }
            return total;
        }

        /// <summary>
        /// Builds the trim-compactor suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/> for the trim-compactor cases.</returns>
        public static TestSuiteDescriptor Create()
        {
            return new TestSuiteDescriptor(
                "ConversationTrimCompactor",
                "Conversation trim compaction",
                new List<TestCaseDescriptor>
                {
                    new TestCaseDescriptor(
                        "ConversationTrimCompactor",
                        "TrimToTargetPreservesLeadingSystemMessageAndRecentTurns",
                        "Trim-to-target preserves leading system memory and recent turns",
                        (CancellationToken ct) =>
                        {
                            List<ConversationMessage> history = new List<ConversationMessage>
                            {
                                System("summary"), User("u1"), Assistant("a1"), User("u2"), Assistant("a2"), User("u3"), Assistant("a3")
                            };

                            ConversationTrimResult result = ConversationTrimCompactor.TrimToTarget(history, preserveTurns: 2, targetUsedTokens: 15, estimateUsedTokens: EstimateContentLength);

                            MuxAssert.IsTrue(result.DidTrim, "DidTrim");
                            MuxAssert.IsTrue(result.ReachedTarget, "ReachedTarget");
                            AssertContents(result.CompactedHistory, new[] { "summary", "u2", "a2", "u3", "a3" }, "CompactedHistory");
                            return Task.CompletedTask;
                        }),

                    new TestCaseDescriptor(
                        "ConversationTrimCompactor",
                        "TrimAllEligibleRemovesAllCompactablePrefix",
                        "Trim-all removes every eligible older message while preserving the recent tail",
                        (CancellationToken ct) =>
                        {
                            List<ConversationMessage> history = new List<ConversationMessage>
                            {
                                User("u1"), Assistant("a1"), User("u2"), Assistant("a2"),
                                User("u3"), Assistant("a3"), User("u4"), Assistant("a4")
                            };

                            ConversationTrimResult result = ConversationTrimCompactor.TrimAllEligible(history, preserveTurns: 2, estimateUsedTokens: EstimateContentLength);

                            MuxAssert.IsTrue(result.DidTrim, "DidTrim");
                            AssertContents(result.CompactedHistory, new[] { "u3", "a3", "u4", "a4" }, "CompactedHistory");
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
