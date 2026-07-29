namespace Test.Nunit
{
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using NUnit.Framework;
    using Test.Shared;
    using Touchstone.Core;
    using Touchstone.NunitAdapter;

    /// <summary>
    /// Runs every MUX Touchstone suite as a single NUnit test through the Touchstone NUnit adapter,
    /// honoring suite lifecycle hooks and preserving descriptor order.
    /// </summary>
    [TestFixture]
    public sealed class MuxNunitFactTests : TouchstoneNunitBase
    {
        /// <inheritdoc/>
        protected override IReadOnlyList<TestSuiteDescriptor> Suites
        {
            get { return MuxSuites.All; }
        }

        /// <summary>
        /// Executes all registered suites and fails the test if any case fails.
        /// </summary>
        /// <returns>A task representing the asynchronous test run.</returns>
        [Test]
        public async Task RunAll()
        {
            await RunAllAsync().ConfigureAwait(false);
        }
    }
}
