namespace Mux.Core.Processes
{
    using System;

    /// <summary>
    /// A point-in-time description of one background process: what it runs, where, and whether it is still alive.
    /// </summary>
    public sealed class BackgroundProcessInfo
    {
        #region Public-Members

        /// <summary>The short id used by the process tools and <c>/processes</c> (for example <c>p1</c>).</summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>An optional friendly name given at start, or empty.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>The command line as started (run through the platform shell).</summary>
        public string Command { get; set; } = string.Empty;

        /// <summary>The working directory.</summary>
        public string WorkingDirectory { get; set; } = string.Empty;

        /// <summary>The operating-system process id, or 0 when unknown.</summary>
        public int ProcessId { get; set; }

        /// <summary>When the process started (UTC).</summary>
        public DateTime StartedUtc { get; set; }

        /// <summary>When the process exited (UTC), or null while it runs.</summary>
        public DateTime? ExitedUtc { get; set; }

        /// <summary>Whether the process is still running.</summary>
        public bool Running { get; set; }

        /// <summary>The exit code once the process has exited, or null while it runs.</summary>
        public int? ExitCode { get; set; }

        /// <summary>Whether mux stopped the process (as opposed to it exiting on its own).</summary>
        public bool StoppedByUser { get; set; }

        /// <summary>The total characters of output produced so far, including any dropped from the buffer.</summary>
        public long OutputChars { get; set; }

        /// <summary>The characters of output not yet returned by a read.</summary>
        public long UnreadChars { get; set; }

        #endregion
    }
}
