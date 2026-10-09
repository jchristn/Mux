namespace Mux.Core.Processes
{
    using System;
    using System.Diagnostics;
    using System.Text;
    using System.Text.RegularExpressions;
    using System.Threading.Tasks;

    /// <summary>
    /// The live state of one background process owned by a <see cref="BackgroundProcessRegistry"/>: the process,
    /// a bounded output buffer that keeps the newest text, and the read cursor. Thread-safe.
    /// </summary>
    internal sealed class BackgroundProcessEntry
    {
        #region Private-Members

        private static readonly Regex _AnsiPattern = new Regex(@"\x1B(\[[0-9;?]*[ -/]*[@-~]|\][^\x07\x1B]*(\x07|\x1B\\)|[@-Z\\-_])", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private readonly object _Sync = new object();
        private readonly StringBuilder _Buffer = new StringBuilder();
        private readonly int _CapacityChars;
        private long _BufferStart;
        private long _Total;
        private long _ReadCursor;
        private TaskCompletionSource<bool> _Signal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="BackgroundProcessEntry"/> class.
        /// </summary>
        /// <param name="info">The descriptive information; the entry keeps and updates it.</param>
        /// <param name="process">The started process, or null in tests that only exercise the buffer.</param>
        /// <param name="capacityChars">The most characters of output kept.</param>
        public BackgroundProcessEntry(BackgroundProcessInfo info, Process? process, int capacityChars)
        {
            Info = info;
            Process = process;
            _CapacityChars = Math.Max(1024, capacityChars);
        }

        #endregion

        #region Public-Members

        /// <summary>The descriptive information (mutated under the entry's lock).</summary>
        public BackgroundProcessInfo Info { get; }

        /// <summary>The process, or null.</summary>
        public Process? Process { get; }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Appends one line of output, dropping the oldest text when the buffer is full, and wakes readers.
        /// </summary>
        /// <param name="line">The line, without its newline. ANSI escape sequences are removed.</param>
        public void AppendLine(string line)
        {
            string clean = _AnsiPattern.Replace(line ?? string.Empty, string.Empty) + "\n";
            TaskCompletionSource<bool> signal;
            lock (_Sync)
            {
                _Buffer.Append(clean);
                _Total += clean.Length;
                if (_Buffer.Length > _CapacityChars)
                {
                    int drop = _Buffer.Length - _CapacityChars;
                    _Buffer.Remove(0, drop);
                    _BufferStart += drop;
                }

                Info.OutputChars = _Total;
                signal = _Signal;
                _Signal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            }

            signal.TrySetResult(true);
        }

        /// <summary>
        /// Records that the process exited and wakes readers.
        /// </summary>
        /// <param name="exitCode">The exit code.</param>
        /// <param name="exitedUtc">When it exited.</param>
        public void MarkExited(int exitCode, DateTime exitedUtc)
        {
            TaskCompletionSource<bool> signal;
            lock (_Sync)
            {
                if (!Info.Running)
                {
                    return;
                }

                Info.Running = false;
                Info.ExitCode = exitCode;
                Info.ExitedUtc = exitedUtc;
                signal = _Signal;
                _Signal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            }

            signal.TrySetResult(true);
        }

        /// <summary>
        /// Returns the unread text without consuming it, and how many unread characters were dropped.
        /// </summary>
        /// <param name="dropped">Unread characters lost to the buffer limit.</param>
        /// <returns>The unread text still in the buffer.</returns>
        public string PeekUnread(out long dropped)
        {
            lock (_Sync)
            {
                return PeekUnreadNoLock(out dropped);
            }
        }

        /// <summary>
        /// Returns the unread text and advances the read cursor to the end.
        /// </summary>
        /// <param name="dropped">Unread characters lost to the buffer limit.</param>
        /// <returns>The unread text.</returns>
        public string ReadUnread(out long dropped)
        {
            lock (_Sync)
            {
                string text = PeekUnreadNoLock(out dropped);
                _ReadCursor = _Total;
                Info.UnreadChars = 0;
                return text;
            }
        }

        /// <summary>
        /// Returns the newest retained text, up to a number of characters, without moving the read cursor.
        /// </summary>
        /// <param name="maxChars">The most characters to return.</param>
        /// <returns>The tail of the buffer.</returns>
        public string Tail(int maxChars)
        {
            lock (_Sync)
            {
                int length = Math.Min(_Buffer.Length, Math.Max(0, maxChars));
                return _Buffer.ToString(_Buffer.Length - length, length);
            }
        }

        /// <summary>
        /// A task that completes the next time output arrives or the process exits.
        /// </summary>
        /// <returns>The signal task.</returns>
        public Task WaitForChangeAsync()
        {
            lock (_Sync)
            {
                return _Signal.Task;
            }
        }

        /// <summary>
        /// Returns a copy of the descriptive information with the unread count filled in.
        /// </summary>
        /// <returns>The snapshot.</returns>
        public BackgroundProcessInfo Snapshot()
        {
            lock (_Sync)
            {
                Info.UnreadChars = _Total - _ReadCursor;
                return new BackgroundProcessInfo
                {
                    Id = Info.Id,
                    Name = Info.Name,
                    Command = Info.Command,
                    WorkingDirectory = Info.WorkingDirectory,
                    ProcessId = Info.ProcessId,
                    StartedUtc = Info.StartedUtc,
                    ExitedUtc = Info.ExitedUtc,
                    Running = Info.Running,
                    ExitCode = Info.ExitCode,
                    StoppedByUser = Info.StoppedByUser,
                    OutputChars = Info.OutputChars,
                    UnreadChars = Info.UnreadChars
                };
            }
        }

        /// <summary>
        /// Marks the process as stopped by mux.
        /// </summary>
        public void MarkStoppedByUser()
        {
            lock (_Sync)
            {
                Info.StoppedByUser = true;
            }
        }

        #endregion

        #region Private-Methods

        private string PeekUnreadNoLock(out long dropped)
        {
            long from = Math.Max(_ReadCursor, _BufferStart);
            dropped = from - _ReadCursor;
            int offset = (int)(from - _BufferStart);
            return _Buffer.ToString(offset, _Buffer.Length - offset);
        }

        #endregion
    }
}
