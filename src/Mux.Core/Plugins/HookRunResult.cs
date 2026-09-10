namespace Mux.Core.Plugins
{
    /// <summary>
    /// The outcome of running a single hook process: which hook ran, its exit code, captured stdout/stderr,
    /// whether it timed out or failed to start, and whether it vetoed the event.
    /// </summary>
    public sealed class HookRunResult
    {
        #region Public-Members

        /// <summary>
        /// A human-readable identifier for the hook (its name, or the command when unnamed).
        /// </summary>
        public string HookName { get; set; } = string.Empty;

        /// <summary>
        /// The process exit code, or a synthetic non-zero value when the hook could not be started or
        /// timed out.
        /// </summary>
        public int ExitCode { get; set; }

        /// <summary>
        /// The captured standard output (trimmed of a single trailing newline).
        /// </summary>
        public string StdOut { get; set; } = string.Empty;

        /// <summary>
        /// The captured standard error (trimmed of a single trailing newline).
        /// </summary>
        public string StdErr { get; set; } = string.Empty;

        /// <summary>
        /// Whether the hook exceeded its timeout and was killed.
        /// </summary>
        public bool TimedOut { get; set; }

        /// <summary>
        /// Whether the hook process failed to start (for example the command was not found).
        /// </summary>
        public bool Started { get; set; } = true;

        /// <summary>
        /// Whether this hook vetoed the event: a blocking hook that exited non-zero on a vetoable event.
        /// </summary>
        public bool Vetoed { get; set; }

        #endregion
    }
}
