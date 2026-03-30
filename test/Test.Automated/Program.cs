namespace Test.Automated
{
    using System;
    using System.Linq;
    using System.Threading.Tasks;
    using Test.Automated.Suites;
    using Test.Shared;

    /// <summary>
    /// Entry point for the automated integration test runner.
    /// </summary>
    public class Program
    {
        /// <summary>
        /// Main entry point. Parses flags and runs all registered test suites.
        /// </summary>
        /// <param name="args">Command-line arguments. Pass <c>--live</c> to run against a live endpoint instead of mock.</param>
        /// <returns>0 if all tests passed, 1 if any test failed.</returns>
        public static async Task<int> Main(string[] args)
        {
            bool liveMode = args.Contains("--live", StringComparer.OrdinalIgnoreCase);

            string modeLabel = liveMode ? "LIVE" : "MOCK";
            TestRunner runner = new TestRunner($"MUX Automated Tests ({modeLabel})");

            runner.AddSuite(new SingleTurnTests(liveMode));
            runner.AddSuite(new ToolUseTests(liveMode));
            runner.AddSuite(new PrintModeTests(liveMode));
            runner.AddSuite(new ApprovalPolicyTests(liveMode));
            runner.AddSuite(new EndpointSwitchingTests(liveMode));
            runner.AddSuite(new MultiEditTests(liveMode));

            int exitCode = await runner.RunAllAsync().ConfigureAwait(false);
            return exitCode;
        }
    }
}
