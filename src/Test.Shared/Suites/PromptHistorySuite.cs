namespace Test.Shared.Suites
{
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Prompting;
    using Touchstone.Core;

    /// <summary>
    /// Touchstone suite for <see cref="PromptHistory"/>: Up/Down recall, draft preservation and restore,
    /// consecutive-duplicate collapsing, capacity trimming, and the empty-history no-op.
    /// </summary>
    public static class PromptHistorySuite
    {
        /// <summary>
        /// Builds the prompt-history suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/> for the prompt-history cases.</returns>
        public static TestSuiteDescriptor Create()
        {
            return new TestSuiteDescriptor(
                "PromptHistory",
                "Composer prompt history recall",
                new List<TestCaseDescriptor>
                {
                    new TestCaseDescriptor("PromptHistory", "EmptyIsNoOp", "Up on empty history does nothing", (CancellationToken ct) =>
                    {
                        PromptHistory history = new PromptHistory();
                        MuxAssert.IsFalse(history.TryPrevious("draft", out string result), "no previous on empty");
                        MuxAssert.AreEqual("draft", result, "current text unchanged");
                        MuxAssert.IsFalse(history.TryNext(out _), "no next when not navigating");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("PromptHistory", "UpWalksOlder", "Up walks from newest to oldest and clamps", (CancellationToken ct) =>
                    {
                        PromptHistory history = new PromptHistory();
                        history.Add("one");
                        history.Add("two");
                        history.Add("three");

                        MuxAssert.IsTrue(history.TryPrevious("draft", out string r1), "first up");
                        MuxAssert.AreEqual("three", r1, "newest first");
                        history.TryPrevious("ignored", out string r2);
                        MuxAssert.AreEqual("two", r2, "then older");
                        history.TryPrevious("ignored", out string r3);
                        MuxAssert.AreEqual("one", r3, "then oldest");
                        history.TryPrevious("ignored", out string r4);
                        MuxAssert.AreEqual("one", r4, "clamps at oldest");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("PromptHistory", "DownRestoresDraft", "Down walks newer and restores the draft", (CancellationToken ct) =>
                    {
                        PromptHistory history = new PromptHistory();
                        history.Add("a");
                        history.Add("b");

                        history.TryPrevious("my draft", out string _);   // b
                        history.TryPrevious("ignored", out string _);    // a
                        MuxAssert.IsTrue(history.TryNext(out string n1), "next");
                        MuxAssert.AreEqual("b", n1, "newer");
                        MuxAssert.IsTrue(history.TryNext(out string n2), "next again");
                        MuxAssert.AreEqual("my draft", n2, "draft restored");
                        MuxAssert.IsFalse(history.IsNavigating, "no longer navigating after draft");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("PromptHistory", "CollapsesConsecutiveDuplicates", "Consecutive duplicate submissions collapse", (CancellationToken ct) =>
                    {
                        PromptHistory history = new PromptHistory();
                        history.Add("same");
                        history.Add("same");
                        history.Add("  same  ");
                        MuxAssert.AreEqual(1, history.Entries.Count, "one entry after dup submits");

                        history.Add("other");
                        MuxAssert.AreEqual(2, history.Entries.Count, "distinct entry added");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("PromptHistory", "CapacityTrims", "Oldest entries drop past capacity", (CancellationToken ct) =>
                    {
                        PromptHistory history = new PromptHistory(2);
                        history.Add("a");
                        history.Add("b");
                        history.Add("c");
                        MuxAssert.AreEqual(2, history.Entries.Count, "capped at 2");
                        MuxAssert.AreEqual("b", history.Entries[0], "oldest dropped");
                        MuxAssert.AreEqual("c", history.Entries[1], "newest kept");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("PromptHistory", "LoadThenRecall", "Loaded entries are recallable", (CancellationToken ct) =>
                    {
                        PromptHistory history = new PromptHistory();
                        history.Load(new List<string> { "x", "", "y" });
                        MuxAssert.AreEqual(2, history.Entries.Count, "blank entries dropped on load");
                        history.TryPrevious("d", out string r);
                        MuxAssert.AreEqual("y", r, "newest loaded entry first");
                        return Task.CompletedTask;
                    })
                });
        }
    }
}
