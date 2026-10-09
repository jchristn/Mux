namespace Test.Shared.Support
{
    /// <summary>
    /// The outcome of one script command run in <c>ScriptCommandsSuite1</c>: standard output and error, the exit code,
    /// and an executor error message when the run could not start.
    /// </summary>
    public sealed class ScriptRun1
    {
        #region Public-Members

        /// <summary>Standard output.</summary>
        public string StdOut { get; set; } = string.Empty;

        /// <summary>Standard error.</summary>
        public string StdErr { get; set; } = string.Empty;

        /// <summary>The exit code, or -1 when the run did not produce one.</summary>
        public int ExitCode { get; set; } = -1;

        /// <summary>The executor's error message, or empty.</summary>
        public string Error { get; set; } = string.Empty;

        #endregion
    }
}
