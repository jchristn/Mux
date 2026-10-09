namespace Mux.Core.Worktrees
{
    /// <summary>
    /// The exit code and captured output of one git command run by <see cref="WorktreeManager"/>.
    /// </summary>
    internal sealed class GitOutput
    {
        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="GitOutput"/> class.
        /// </summary>
        /// <param name="exitCode">The exit code.</param>
        /// <param name="stdOut">Standard output.</param>
        /// <param name="stdErr">Standard error.</param>
        public GitOutput(int exitCode, string stdOut, string stdErr)
        {
            ExitCode = exitCode;
            StdOut = stdOut ?? string.Empty;
            StdErr = stdErr ?? string.Empty;
        }

        #endregion

        #region Public-Members

        /// <summary>The exit code.</summary>
        public int ExitCode { get; }

        /// <summary>Standard output.</summary>
        public string StdOut { get; }

        /// <summary>Standard error.</summary>
        public string StdErr { get; }

        #endregion
    }
}
