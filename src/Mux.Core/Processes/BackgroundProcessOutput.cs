namespace Mux.Core.Processes
{
    /// <summary>
    /// The result of reading a background process's output: the text produced since the previous read, whether a
    /// requested pattern appeared, and the process state at the time of the read.
    /// </summary>
    public sealed class BackgroundProcessOutput
    {
        #region Public-Members

        /// <summary>The process id.</summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>The output produced since the previous read (stdout and stderr interleaved by line).</summary>
        public string Text { get; set; } = string.Empty;

        /// <summary>Characters produced since the previous read that were dropped because the buffer was full.</summary>
        public long DroppedChars { get; set; }

        /// <summary>Whether the process is still running.</summary>
        public bool Running { get; set; }

        /// <summary>The exit code once the process has exited, or null while it runs.</summary>
        public int? ExitCode { get; set; }

        /// <summary>Whether the requested wait pattern was found, or null when no pattern was given.</summary>
        public bool? Matched { get; set; }

        /// <summary>The text that matched the wait pattern, or null.</summary>
        public string? MatchText { get; set; }

        /// <summary>Whether the wait ended because the timeout passed.</summary>
        public bool TimedOut { get; set; }

        #endregion
    }
}
