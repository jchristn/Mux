namespace Test.Shared.Suites
{
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Touchstone.Core;

    /// <summary>
    /// Touchstone suite for atomic multi-edit operations. Ported from the legacy <c>MultiEditTests</c>
    /// suite, whose single case was an unimplemented placeholder; carried forward as a skipped
    /// descriptor. Note: the <c>MultiEditToolTests</c> unit suite provides real edit-tool coverage.
    /// </summary>
    public static class MultiEditSuite
    {
        /// <summary>
        /// Builds the multi-edit suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/> containing the (skipped) multi-edit case.</returns>
        public static TestSuiteDescriptor Create()
        {
            return new TestSuiteDescriptor(
                "MultiEdit",
                "Atomic multi-edit operations",
                new List<TestCaseDescriptor>
                {
                    new TestCaseDescriptor(
                        "MultiEdit",
                        "AtomicMultiEditAllOrNothing",
                        "Multi-edit applies all edits atomically or rolls back",
                        (CancellationToken ct) => Task.CompletedTask,
                        skip: true,
                        skipReason: "Integration placeholder; unit-level coverage lives in the MultiEditTool suite.")
                });
        }
    }
}
