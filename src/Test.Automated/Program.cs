namespace Test.Automated
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Test.Shared;
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
        /// <param name="args">Command-line arguments. Pass <c>--results &lt;path&gt;</c> to export JSON results, and
        /// <c>--suite &lt;id&gt;</c> (repeatable, case-insensitive) to run only those suites.</param>
        /// <returns>0 if all tests pass; a non-zero value if any test fails.</returns>
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
