namespace Test.Xunit
{
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using global::Xunit;
    using Test.Shared;
    using Touchstone.Core;
    using Touchstone.XunitAdapter;

    /// <summary>
    /// Runs every MUX Touchstone suite as a single xUnit test through the Touchstone xUnit adapter,
    /// honoring suite lifecycle hooks and preserving descriptor order.
    /// </summary>
    public sealed class MuxXunitFactTests : TouchstoneFactBase
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
        [Fact]
        public async Task RunAll()
        {
            await RunAllAsync();
        }
    }
}
