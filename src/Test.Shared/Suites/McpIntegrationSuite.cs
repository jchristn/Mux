namespace Test.Shared.Suites
{
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Touchstone.Core;

    /// <summary>
    /// Touchstone suite for MCP server connectivity and tool discovery. Ported from the legacy
    /// <c>McpIntegrationTests</c> suite, whose single case was an unimplemented placeholder; carried
    /// forward as a skipped descriptor. Real MCP coverage lives in the <c>McpToolManager</c> unit suite.
    /// </summary>
    public static class McpIntegrationSuite
    {
        /// <summary>
        /// Builds the MCP-integration suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/> containing the (skipped) MCP-integration case.</returns>
        public static TestSuiteDescriptor Create()
        {
            return new TestSuiteDescriptor(
                "McpIntegration",
                "MCP server connectivity and tool discovery",
                new List<TestCaseDescriptor>
                {
                    new TestCaseDescriptor(
                        "McpIntegration",
                        "McpServerDiscovery",
                        "MCP servers are discovered and tools enumerated",
                        (CancellationToken ct) => Task.CompletedTask,
                        skip: true,
                        skipReason: "Requires a live MCP server; unit-level coverage lives in the McpToolManager suite.")
                });
        }
    }
}
