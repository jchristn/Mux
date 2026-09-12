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
    /// Touchstone suite for <see cref="ConversationCompactor"/>'s pure pieces: assembling a compacted
    /// conversation from a plan + summary, and building the summarizer digest.
    /// </summary>
    public static class ConversationCompactorSuite
    {
        /// <summary>
        /// Builds the conversation-compactor suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/> for the compactor cases.</returns>
        public static TestSuiteDescriptor Create()
        {
            return new TestSuiteDescriptor(
                "ConversationCompactor",
                "On-demand conversation compaction",
                new List<TestCaseDescriptor>
                {
                    new TestCaseDescriptor("ConversationCompactor", "AssemblePrependsSummaryAndKeepsPreserved", "Assemble builds a summary message then the preserved turns", (CancellationToken ct) =>
                    {
                        ConversationCompactionPlan plan = new ConversationCompactionPlan
                        {
                            MessagesToCompact = new List<ConversationMessage>
                            {
                                new ConversationMessage { Role = RoleEnum.User, Content = "old q" },
                                new ConversationMessage { Role = RoleEnum.Assistant, Content = "old a" }
                            },
                            MessagesToPreserve = new List<ConversationMessage>
                            {
                                new ConversationMessage { Role = RoleEnum.User, Content = "recent q" },
                                new ConversationMessage { Role = RoleEnum.Assistant, Content = "recent a" }
                            }
                        };

                        List<ConversationMessage> result = ConversationCompactor.Assemble(plan, "  the summary  ");

                        MuxAssert.AreEqual(3, result.Count, "summary + two preserved");
                        MuxAssert.AreEqual(RoleEnum.System, result[0].Role, "first message is a system summary");
                        MuxAssert.IsTrue(result[0].Content!.StartsWith(ConversationCompactor.SummaryPrefix), "summary carries the synthetic prefix");
                        MuxAssert.Contains("the summary", result[0].Content, "summary text is included (trimmed)");
                        MuxAssert.AreEqual("recent q", result[1].Content, "first preserved message kept");
                        MuxAssert.AreEqual("recent a", result[2].Content, "second preserved message kept");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("ConversationCompactor", "DigestFormatsRolesAndCaps", "BuildDigest labels roles, skips blanks, and respects the cap", (CancellationToken ct) =>
                    {
                        List<ConversationMessage> messages = new List<ConversationMessage>
                        {
                            new ConversationMessage { Role = RoleEnum.User, Content = "hello" },
                            new ConversationMessage { Role = RoleEnum.Assistant, Content = "   " },
                            new ConversationMessage { Role = RoleEnum.Assistant, Content = "world" }
                        };

                        string digest = ConversationCompactor.BuildDigest(messages, 12000);
                        MuxAssert.Contains("User: hello", digest, "user line");
                        MuxAssert.Contains("Assistant: world", digest, "assistant line");
                        MuxAssert.DoesNotContain("Assistant:    ", digest, "blank message skipped");

                        string capped = ConversationCompactor.BuildDigest(messages, 3);
                        MuxAssert.IsTrue(capped.Length <= 40, "capped digest stops early");
                        return Task.CompletedTask;
                    })
                });
        }
    }
}
