namespace Test.Shared.Support
{
    /// <summary>
    /// The outcome of running a CLI verb in-process: its exit code and what it wrote to stdout and stderr.
    /// </summary>
    public sealed class CliRun
    {
        #region Public-Members

        /// <summary>The exit code.</summary>
        public int Code { get; set; }

        /// <summary>Everything written to stdout.</summary>
        public string Out { get; set; } = string.Empty;

        /// <summary>Everything written to stderr.</summary>
        public string Err { get; set; } = string.Empty;

        #endregion
    }
}
