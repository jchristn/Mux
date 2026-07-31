namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Cli.App;
    using Mux.Core.Models;
    using Touchstone.Core;

    /// <summary>
    /// Touchstone suite for <see cref="McpRuntime"/> lifecycle with no configured servers: it starts and
    /// completes an initial refresh, exposes an empty tool/status set, routes unknown tool calls to an error
    /// result, and disposes cleanly. (Live server connection is exercised by <c>McpToolManagerSuite</c>.)
    /// </summary>
    public static class McpRuntimeSuite
    {
        private const string SuiteId = "McpRuntime";

        /// <summary>
        /// Builds the MCP-runtime suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/> for the runtime cases.</returns>
        public static TestSuiteDescriptor Create()
        {
            return new TestSuiteDescriptor(
                SuiteId,
                "MCP runtime lifecycle with no configured servers",
                new List<TestCaseDescriptor>
                {
                    Case("EmptyRuntimeRefreshesAndReportsUnknownTool", "With no servers the runtime has no tools and rejects unknown calls", async (CancellationToken ct) =>
                    {
                        int changes = 0;
                        using (McpRuntime runtime = new McpRuntime(
                            () => new List<McpServerConfig>(),
                            () => Interlocked.Increment(ref changes),
                            TimeSpan.FromMilliseconds(50)))
                        {
                            runtime.Start();
                            await runtime.FirstRefreshCompleted.WaitAsync(TimeSpan.FromSeconds(5), ct).ConfigureAwait(false);

                            MuxAssert.AreEqual(0, runtime.CurrentTools.Count, "no tools discovered");
                            MuxAssert.AreEqual(0, runtime.GetStatus().Count, "no server status");
                            MuxAssert.IsTrue(changes >= 1, "tools-changed fired on first refresh");

                            ToolResult result = await runtime
                                .ExecuteToolAsync("srv.tool", default, string.Empty, ct)
                                .ConfigureAwait(false);
                            MuxAssert.IsFalse(result.Success, "unknown MCP tool not executed");
                            MuxAssert.Contains("unknown_mcp_tool", result.Content, "unknown-tool error returned");
                        }
                    }),

                    Case("DisposeIsIdempotentAndSafe", "Requesting refresh after dispose is a no-op and does not throw", async (CancellationToken ct) =>
                    {
                        McpRuntime runtime = new McpRuntime(
                            () => new List<McpServerConfig>(),
                            () => { },
                            TimeSpan.FromMilliseconds(50));
                        runtime.Start();
                        await runtime.FirstRefreshCompleted.WaitAsync(TimeSpan.FromSeconds(5), ct).ConfigureAwait(false);

                        runtime.Dispose();
                        runtime.Dispose();       // idempotent
                        runtime.RequestRefresh(); // no-op after dispose

                        MuxAssert.AreEqual(0, runtime.CurrentTools.Count, "no tools after dispose");
                    })
                });
        }

        private static TestCaseDescriptor Case(string id, string name, Func<CancellationToken, Task> body)
        {
            return new TestCaseDescriptor(SuiteId, id, name, body);
        }
    }
}
