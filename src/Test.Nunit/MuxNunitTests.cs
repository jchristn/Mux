namespace Test.Nunit
{
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using NUnit.Framework;
    using Test.Shared;
    using Touchstone.Core;
    using Touchstone.NunitAdapter;

    /// <summary>
    /// Runs each MUX Touchstone case as an individual NUnit test, giving per-case visibility in the
    /// test explorer. Skipped cases are reported as NUnit ignores with their skip reason.
    /// </summary>
    [TestFixture]
    public sealed class MuxNunitTests
    {
        private static IEnumerable<TestCaseData> TestCases()
        {
            return TouchstoneNunitCases.From(MuxSuites.All);
        }

        /// <summary>
        /// Executes a single Touchstone case.
        /// </summary>
        /// <param name="testCase">The descriptor supplied by the Touchstone test-case source.</param>
        /// <returns>A task representing the asynchronous test run.</returns>
        [Test]
        [TestCaseSource(nameof(TestCases))]
        public Task RunTest(TestCaseDescriptor testCase)
        {
            return TouchstoneNunit.ExecuteAsync(testCase, null, null, TestContext.CurrentContext.CancellationToken);
        }
    }
}
