namespace Test.Xunit
{
    using System.Threading;
    using System.Threading.Tasks;
    using global::Xunit;
    using Test.Shared;
    using Touchstone.Core;

    /// <summary>
    /// Runs each non-skipped MUX Touchstone case as an individual xUnit theory row, giving per-case
    /// visibility in the test explorer. Skipped descriptors are excluded here (they are still honored by
    /// <see cref="MuxXunitFactTests"/> and the console runner).
    /// </summary>
    public sealed class MuxXunitTheoryTests
    {
        /// <summary>
        /// Gets the non-skipped Touchstone cases as xUnit theory data.
        /// </summary>
        /// <returns>Theory data containing one row per non-skipped case.</returns>
        public static TheoryData<TestCaseDescriptor> TestCases()
        {
            TheoryData<TestCaseDescriptor> data = new TheoryData<TestCaseDescriptor>();

            foreach (TestSuiteDescriptor suite in MuxSuites.All)
            {
                foreach (TestCaseDescriptor testCase in suite.Cases)
                {
                    if (!testCase.Skip)
                    {
                        data.Add(testCase);
                    }
                }
            }

            return data;
        }

        /// <summary>
        /// Executes a single Touchstone case.
        /// </summary>
        /// <param name="testCase">The descriptor supplied by the theory-data source.</param>
        /// <returns>A task representing the asynchronous test run.</returns>
        [Theory]
        [MemberData(nameof(TestCases))]
        public async Task RunTest(TestCaseDescriptor testCase)
        {
            await testCase.ExecuteAsync(CancellationToken.None);
        }
    }
}
