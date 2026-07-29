namespace Test.Automated
{
    using System.Threading;
    using System.Threading.Tasks;
    using Test.Shared;
    using Touchstone.Cli;

    /// <summary>
    /// Entry point for the automated Touchstone console runner. Executes every registered MUX suite
    /// and returns a process exit code of 0 when all cases pass, non-zero otherwise.
    /// </summary>
    public static class Program
    {
        /// <summary>
        /// Runs all MUX Touchstone suites through the console runner.
        /// </summary>
        /// <param name="args">Command-line arguments. Pass <c>--results &lt;path&gt;</c> to export JSON results.</param>
        /// <returns>0 if all tests pass; a non-zero value if any test fails.</returns>
        public static async Task<int> Main(string[] args)
        {
            string? resultsPath = null;

            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--results" && i + 1 < args.Length)
                {
                    resultsPath = args[i + 1];
                    break;
                }
            }

            return await ConsoleRunner.RunAsync(MuxSuites.All, resultsPath: resultsPath, cancellationToken: CancellationToken.None).ConfigureAwait(false);
        }
    }
}
