namespace Test.Shared.Suites
{
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Touchstone.Core;

    /// <summary>
    /// Touchstone suite for CLI-driven endpoint and model switching. Ported from the legacy
    /// <c>EndpointSwitchingTests</c> suite, whose cases were unimplemented placeholders; carried
    /// forward as skipped descriptors. Real CLI override coverage lives in the CLI command unit suites.
    /// </summary>
    public static class EndpointSwitchingSuite
    {
        /// <summary>
        /// Builds the endpoint-switching suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/> containing the (skipped) endpoint-switching cases.</returns>
        public static TestSuiteDescriptor Create()
        {
            return new TestSuiteDescriptor(
                "EndpointSwitching",
                "CLI-driven endpoint and model switching",
                new List<TestCaseDescriptor>
                {
                    new TestCaseDescriptor(
                        "EndpointSwitching",
                        "CliModelOverrideWorks",
                        "--model CLI flag overrides the default model",
                        (CancellationToken ct) => Task.CompletedTask,
                        skip: true,
                        skipReason: "Integration placeholder; CLI override behavior is covered by the CLI command unit suites."),

                    new TestCaseDescriptor(
                        "EndpointSwitching",
                        "CliEndpointOverrideWorks",
                        "--endpoint CLI flag overrides the default endpoint",
                        (CancellationToken ct) => Task.CompletedTask,
                        skip: true,
                        skipReason: "Integration placeholder; CLI override behavior is covered by the CLI command unit suites.")
                });
        }
    }
}
