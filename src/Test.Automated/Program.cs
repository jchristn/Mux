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
        /// Maps each live-model command-line flag to the environment variable used by the live
        /// skill-selection suite.
        /// </summary>
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

        /// <summary>
        /// Runs all MUX Touchstone suites through the console runner.
        /// </summary>
        /// <param name="args">Command-line arguments. Pass <c>--results &lt;path&gt;</c> to export JSON results,
        /// <c>--suite &lt;id&gt;</c> (repeatable, case-insensitive) to run only those suites,
        /// <c>--group &lt;id&gt;</c> (repeatable, case-insensitive) to run only the suites of those named groups
        /// (<c>--suite</c> then filters within the selected groups), <c>--list-groups</c> to print the groups,
        /// and <c>--llm-endpoint</c>, <c>--llm-model</c>, <c>--llm-adapter</c>, <c>--llm-api-key</c>, <c>--llm-floor</c>,
        /// <c>--llm-cases</c>, <c>--llm-report</c>, and <c>--llm-timeout</c> to run the live skill-selection suite
        /// against a model, and <c>--docker</c> to run the Docker integration suite.</param>
        /// <returns>0 if all tests pass; a non-zero value if any test fails.</returns>
        public static async Task<int> Main(string[] args)
        {
            string? resultsPath = null;
            bool listGroups = false;
            HashSet<string> suiteIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            List<MuxSuites.GroupInfo> groups = new List<MuxSuites.GroupInfo>();

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
                else if (args[i] == "--group" && i + 1 < args.Length)
                {
                    string groupId = args[++i];
                    MuxSuites.GroupInfo? group = MuxSuites.FindGroup(groupId);
                    if (group == null)
                    {
                        List<string> ids = new List<string>();
                        foreach (MuxSuites.GroupInfo known in MuxSuites.Groups) ids.Add(known.Id);
                        Console.Error.WriteLine("Unknown group '" + groupId + "'. Valid groups: " + string.Join(", ", ids) + ".");
                        return 2;
                    }

                    groups.Add(group);
                }
                else if (args[i] == "--list-groups")
                {
                    listGroups = true;
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

            if (listGroups)
            {
                foreach (MuxSuites.GroupInfo group in MuxSuites.Groups)
                {
                    Console.WriteLine(group.Id.PadRight(16) + group.Description);
                }

                return 0;
            }

            // The selection universe is every suite, or the union of the named groups; --suite filters
            // within that universe, so group and suite ids combine predictably.
            List<TestSuiteDescriptor> universe = new List<TestSuiteDescriptor>();
            if (groups.Count == 0)
            {
                universe.AddRange(MuxSuites.All);
            }
            else
            {
                HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (MuxSuites.GroupInfo group in groups)
                {
                    foreach (TestSuiteDescriptor suite in group.CreateSuites())
                    {
                        if (seen.Add(suite.SuiteId)) universe.Add(suite);
                    }
                }
            }

            List<TestSuiteDescriptor> suites = new List<TestSuiteDescriptor>();
            foreach (TestSuiteDescriptor suite in universe)
            {
                if (suiteIds.Count == 0 || suiteIds.Contains(suite.SuiteId))
                {
                    suites.Add(suite);
                }
            }

            if (suites.Count == 0)
            {
                List<string> requested = new List<string>(suiteIds);
                foreach (MuxSuites.GroupInfo group in groups) requested.Add("group:" + group.Id);
                Console.Error.WriteLine("No suite matches " + string.Join(", ", requested) + ".");
                return 2;
            }

            return await ConsoleRunner.RunAsync(suites, resultsPath: resultsPath, cancellationToken: CancellationToken.None).ConfigureAwait(false);
        }
    }
}
