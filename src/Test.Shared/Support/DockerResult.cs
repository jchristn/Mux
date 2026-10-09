namespace Test.Shared.Support
{
    /// <summary>
    /// The outcome of a docker command.
    /// </summary>
    public sealed class DockerResult
    {
        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="DockerResult"/> class.
        /// </summary>
        /// <param name="exitCode">The exit code (-1 when docker could not run or timed out).</param>
        /// <param name="standardOutput">Standard output.</param>
        /// <param name="standardError">Standard error.</param>
        public DockerResult(int exitCode, string standardOutput, string standardError)
        {
            ExitCode = exitCode;
            StandardOutput = standardOutput ?? string.Empty;
            StandardError = standardError ?? string.Empty;
        }

        #endregion

        #region Public-Members

        /// <summary>The exit code.</summary>
        public int ExitCode { get; }

        /// <summary>Standard output.</summary>
        public string StandardOutput { get; }

        /// <summary>Standard error.</summary>
        public string StandardError { get; }

        #endregion
    }
}
