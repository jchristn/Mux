namespace Test.Nunit
{
    using System.Collections;
    using System.Threading;
    using System.Threading.Tasks;
    using NUnit.Framework;
    using Test.Shared;
    using Touchstone.Core;
    using Touchstone.NunitAdapter;

    /// <summary>
    /// Runs each MUX Touchstone case as an individual NUnit test, giving per-case visibility in the
    /// test explorer.
    /// </summary>
    [TestFixture]
    public sealed class MuxNunitTests
    {
        private static IEnumerable TestCases()
        {
            return new TouchstoneTestCaseSource(MuxSuites.All);
        }

        /// <summary>
        /// Executes a single Touchstone case.
        /// </summary>
        /// <param name="testCase">The descriptor supplied by the Touchstone test-case source.</param>
        /// <returns>A task representing the asynchronous test run.</returns>
        [Test]
        [TestCaseSource(nameof(TestCases))]
        public async Task RunTest(TestCaseDescriptor testCase)
        {
            await testCase.ExecuteAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }
}
