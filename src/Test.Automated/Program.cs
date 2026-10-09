namespace Test.Automated
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Test.Shared;
    using Test.Shared.Support;
    using Touchstone.Cli;
    using Touchstone.Core;

    /// <summary>
    /// Entry point for the automated Touchstone console runner. Executes every registered MUX suite
    /// and returns a process exit code of 0 when all cases pass, non-zero otherwise.
    /// </summary>
    public static class Program
    {
        /// <summary>
        /// Runs all MUX Touchstone suites through the console runner.
        /// </summary>
        /// <param name="args">Command-line arguments. Pass <c>--results &lt;path&gt;</c> to export JSON results,
        /// <c>--suite &lt;id&gt;</c> (repeatable, case-insensitive) to run only those suites, and <c>--llm-endpoint</c>,
        /// <c>--llm-model</c>, <c>--llm-adapter</c>, <c>--llm-api-key</c>, <c>--llm-floor</c>, <c>--llm-cases</c>,
        /// <c>--llm-report</c>, and <c>--llm-timeout</c> to run the live skill-selection suite against a model, and
        /// <c>--docker</c> to run the Docker integration suite.</param>
        /// <returns>0 if all tests pass; a non-zero value if any test fails.</returns>
        private static readonly Dictionary<string, string> LiveOptions = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["--llm-endpoint"] = LiveModelOptions.EndpointVariable,
            ["--llm-model"] = LiveModelOptions.ModelVariable,
            ["--llm-adapter"] = LiveModelOptions.AdapterVariable,
            ["--llm-api-key"] = LiveModelOptions.ApiKeyVariable,
            ["--llm-floor"] = LiveModelOptions.FloorVariable,
            ["--llm-cases"] = LiveModelOptions.CasesVariable,
            ["--llm-report"] = LiveModelOptions.ReportVariable,
            ["--llm-timeout"] = LiveModelOptions.TimeoutVariable
        };

        public static async Task<int> Main(string[] args)
        {
            string? resultsPath = null;
            HashSet<string> suiteIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--results" && i + 1 < args.Length)
                {
                    resultsPath = args[++i];
                }
                else if (args[i] == "--suite" && i + 1 < args.Length)
                {
                    suiteIds.Add(args[++i]);
                }
                else if (args[i] == "--docker")
                {
                    // Opt in to the Docker integration suite (throwaway database and tool containers).
                    Environment.SetEnvironmentVariable(DockerHarness.EnableVariable, "1");
                }
                else if (LiveOptions.TryGetValue(args[i], out string? variable) && i + 1 < args.Length)
                {
                    // Live-model settings travel as environment variables so the xUnit and NUnit adapters can use them too.
                    Environment.SetEnvironmentVariable(variable, args[++i]);
                }
            }

            List<TestSuiteDescriptor> suites = new List<TestSuiteDescriptor>();
            foreach (TestSuiteDescriptor suite in MuxSuites.All)
            {
                if (suiteIds.Count == 0 || suiteIds.Contains(suite.SuiteId))
                {
                    suites.Add(suite);
                }
            }

            if (suites.Count == 0)
            {
                Console.Error.WriteLine("No suite matches " + string.Join(", ", suiteIds) + ".");
                return 2;
            }

            return await ConsoleRunner.RunAsync(suites, resultsPath: resultsPath, cancellationToken: CancellationToken.None).ConfigureAwait(false);
        }
    }
}
