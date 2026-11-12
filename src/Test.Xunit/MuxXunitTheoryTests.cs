namespace Test.Xunit
{
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using global::Xunit;
    using global::Xunit.Abstractions;
    using Test.Shared;
    using Touchstone.Core;
    using Touchstone.Xunit2Adapter;

    /// <summary>
    /// Runs each non-skipped MUX Touchstone case as an individual xUnit theory row, giving per-case
    /// visibility in the test explorer. Skipped descriptors are omitted here (xUnit v2 cannot skip a row at
    /// run time; they are still reported by the console and NUnit runners). Each case executes once per
    /// target framework.
    /// </summary>
    public sealed class MuxXunitTheoryTests
    {
        private readonly ITestOutputHelper _Output;

        /// <summary>
        /// Initializes a new instance of the <see cref="MuxXunitTheoryTests"/> class.
        /// </summary>
        /// <param name="output">Receives scenario diagnostics for each case.</param>
        public MuxXunitTheoryTests(ITestOutputHelper output)
        {
            _Output = output;
        }

        /// <summary>
        /// Gets the non-skipped Touchstone cases as xUnit theory data.
        /// </summary>
        /// <returns>Theory data containing one row per non-skipped case.</returns>
        public static TheoryData<TouchstoneXunit2Case> TestCases()
        {
            return TouchstoneXunit2Cases.From<MuxSuiteProvider>(new TestRunOptions { SkipBehavior = SkipBehavior.Omit });
        }

        /// <summary>
        /// Executes a single Touchstone case.
        /// </summary>
        /// <param name="testCase">The row supplied by the theory-data source.</param>
        /// <returns>A task representing the asynchronous test run.</returns>
        [Theory]
        [MemberData(nameof(TestCases))]
        public Task RunTest(TouchstoneXunit2Case testCase)
        {
            return TouchstoneXunit2.ExecuteAsync(testCase, _Output);
        }

        /// <summary>
        /// Exposes the MUX suites to the xUnit data source, which rebuilds rows in the process that runs them.
        /// </summary>
        public sealed class MuxSuiteProvider : ITestSuiteProvider
        {
            /// <inheritdoc/>
            public IReadOnlyList<TestSuiteDescriptor> Suites
            {
                get { return MuxSuites.All; }
            }
        }
    }
}
