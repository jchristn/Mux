namespace Test.Shared.Support
{
    /// <summary>
    /// The captured outcome of running one imported skill's script command in a test: standard output, standard error
    /// (or the executor's error message), and the exit code (-1 when the process did not report one).
    /// </summary>
    public sealed class EngineeringScriptRun
    {
        #region Public-Members

        /// <summary>Standard output.</summary>
        public string Stdout { get; set; } = string.Empty;

        /// <summary>Standard error, or the executor's error message.</summary>
        public string Stderr { get; set; } = string.Empty;

        /// <summary>The exit code, or -1.</summary>
        public int ExitCode { get; set; } = -1;

        #endregion
    }
}
