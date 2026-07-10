namespace Test.Shared
{
    /// <summary>
    /// Contains captured output from an in-process CLI invocation.
    /// </summary>
    public class CliInvocationResult
    {
        #region Private-Members

        private int _ExitCode = 0;
        private string _StdOut = string.Empty;
        private string _StdErr = string.Empty;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="CliInvocationResult"/> class.
        /// </summary>
        /// <param name="exitCode">The CLI process exit code.</param>
        /// <param name="stdOut">The captured standard output.</param>
        /// <param name="stdErr">The captured standard error.</param>
        public CliInvocationResult(int exitCode, string stdOut, string stdErr)
        {
            _ExitCode = exitCode;
            _StdOut = stdOut;
            _StdErr = stdErr;
        }

        #endregion

        #region Public-Properties

        /// <summary>
        /// Gets or sets the CLI process exit code.
        /// </summary>
        public int ExitCode
        {
            get => _ExitCode;
            set => _ExitCode = value;
        }

        /// <summary>
        /// Gets or sets the captured standard output.
        /// </summary>
        public string StdOut
        {
            get => _StdOut;
            set => _StdOut = value ?? string.Empty;
        }

        /// <summary>
        /// Gets or sets the captured standard error.
        /// </summary>
        public string StdErr
        {
            get => _StdErr;
            set => _StdErr = value ?? string.Empty;
        }

        #endregion
    }
}
