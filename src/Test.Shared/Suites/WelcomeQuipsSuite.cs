namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Utility;
    using Touchstone.Core;

    /// <summary>
    /// Touchstone suite for <see cref="WelcomeQuips"/>: the catalog is large, clean, and unique, and both
    /// selection paths return a member of the catalog.
    /// </summary>
    public static class WelcomeQuipsSuite
    {
        /// <summary>
        /// Builds the welcome-quips suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/> for the welcome-quips cases.</returns>
        public static TestSuiteDescriptor Create()
        {
            return new TestSuiteDescriptor(
                "WelcomeQuips",
                "Welcome quips catalog",
                new List<TestCaseDescriptor>
                {
                    new TestCaseDescriptor("WelcomeQuips", "CatalogIsLargeAndClean", "There are ~100 non-empty, unique quips", (CancellationToken ct) =>
                    {
                        MuxAssert.IsTrue(WelcomeQuips.All.Count >= 100, "at least 100 quips (was " + WelcomeQuips.All.Count + ")");
                        HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
                        foreach (string quip in WelcomeQuips.All)
                        {
                            MuxAssert.IsFalse(string.IsNullOrWhiteSpace(quip), "quip not blank");
                            MuxAssert.AreEqual(quip, quip.Trim(), "quip has no surrounding whitespace");
                            MuxAssert.IsTrue(seen.Add(quip), "quip is unique: " + quip);
                        }

                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("WelcomeQuips", "PickReturnsMember", "Pick returns a catalog member for varied seeds", (CancellationToken ct) =>
                    {
                        HashSet<string> catalog = new HashSet<string>(WelcomeQuips.All, StringComparer.Ordinal);
                        for (int seed = 0; seed < 50; seed++)
                        {
                            string quip = WelcomeQuips.Pick(new Random(seed));
                            MuxAssert.IsTrue(catalog.Contains(quip), "picked quip is in the catalog");
                        }

                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("WelcomeQuips", "NextIsNonEmpty", "Next returns a non-empty quip", (CancellationToken ct) =>
                    {
                        MuxAssert.IsFalse(string.IsNullOrWhiteSpace(WelcomeQuips.Next()), "Next non-empty");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("WelcomeQuips", "PickNullThrows", "Pick rejects a null random source", (CancellationToken ct) =>
                    {
                        MuxAssert.Throws<ArgumentNullException>(() => WelcomeQuips.Pick(null!), "null random");
                        return Task.CompletedTask;
                    })
                });
        }
    }
}
