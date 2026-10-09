namespace Mux.Core.Processes
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Globalization;
    using System.IO;
    using System.Runtime.InteropServices;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Owns the background processes of one session: dev servers, <c>docker compose up</c>, watch-mode test runners,
    /// and anything else that does not fit a foreground tool with a timeout. Each process runs through the platform
    /// shell (as <c>run_process</c> does), its stdout and stderr are kept in a bounded buffer that keeps the newest
    /// text, and reads return only what is new since the previous read. The number of running processes is capped,
    /// and <see cref="Dispose"/> kills every process tree, so nothing outlives the session. Thread-safe.
    /// </summary>
    public sealed class BackgroundProcessRegistry : IDisposable
    {
        #region Private-Members

        private static readonly bool _IsWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

        private readonly object _Sync = new object();
        private readonly List<BackgroundProcessEntry> _Entries = new List<BackgroundProcessEntry>();
        private int _MaxConcurrent;
        private int _OutputCapacityChars;
        private int _NextNumber = 1;
        private bool _Disposed;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="BackgroundProcessRegistry"/> class.
        /// </summary>
        /// <param name="maxConcurrent">The most processes that may run at once (clamped to 1..<see cref="MaxConcurrentLimit"/>).</param>
        /// <param name="outputCapacityChars">The output kept per process, in characters (clamped to <see cref="MinOutputCapacity"/>..<see cref="MaxOutputCapacity"/>).</param>
        public BackgroundProcessRegistry(int maxConcurrent = DefaultMaxConcurrent, int outputCapacityChars = DefaultOutputCapacity)
        {
            Configure(maxConcurrent, outputCapacityChars);
        }

        #endregion

        #region Public-Members

        /// <summary>The default number of processes that may run at once.</summary>
        public const int DefaultMaxConcurrent = 8;

        /// <summary>The highest allowed concurrency setting.</summary>
        public const int MaxConcurrentLimit = 64;

        /// <summary>The default output kept per process (1 MB of text).</summary>
        public const int DefaultOutputCapacity = 1048576;

        /// <summary>The smallest allowed output buffer.</summary>
        public const int MinOutputCapacity = 16384;

        /// <summary>The largest allowed output buffer.</summary>
        public const int MaxOutputCapacity = 16777216;

        /// <summary>The longest a single read may wait, in milliseconds.</summary>
        public const int MaxWaitMs = 300000;

        /// <summary>
        /// Raised (outside the lock) when a process starts, exits, or is stopped.
        /// </summary>
        public event EventHandler? Changed;

        /// <summary>The most processes that may run at once.</summary>
        public int MaxConcurrent
        {
            get { lock (_Sync) { return _MaxConcurrent; } }
        }

        /// <summary>The output kept per process, in characters.</summary>
        public int OutputCapacityChars
        {
            get { lock (_Sync) { return _OutputCapacityChars; } }
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Updates the limits. Running processes keep their buffers; new limits apply to new processes.
        /// </summary>
        /// <param name="maxConcurrent">The most processes that may run at once.</param>
        /// <param name="outputCapacityChars">The output kept per process, in characters.</param>
        public void Configure(int maxConcurrent, int outputCapacityChars)
        {
            lock (_Sync)
            {
                _MaxConcurrent = Math.Clamp(maxConcurrent, 1, MaxConcurrentLimit);
                _OutputCapacityChars = Math.Clamp(outputCapacityChars, MinOutputCapacity, MaxOutputCapacity);
            }
        }

        /// <summary>
        /// Starts a command in the background through the platform shell (<c>/bin/sh -c</c> or <c>cmd.exe /c</c>).
        /// </summary>
        /// <param name="command">The command line. Must not be blank.</param>
        /// <param name="workingDirectory">The working directory; must exist.</param>
        /// <param name="name">An optional friendly name.</param>
        /// <returns>A snapshot of the started process.</returns>
        /// <exception cref="ArgumentException">Thrown when the command is blank or the directory does not exist.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the concurrency limit is reached or the registry is disposed.</exception>
        public BackgroundProcessInfo Start(string command, string workingDirectory, string? name = null)
        {
            if (string.IsNullOrWhiteSpace(command)) throw new ArgumentException("A command is required.", nameof(command));
            if (string.IsNullOrWhiteSpace(workingDirectory) || !Directory.Exists(workingDirectory))
            {
                throw new ArgumentException("The working directory '" + workingDirectory + "' does not exist.", nameof(workingDirectory));
            }

            BackgroundProcessEntry entry;
            lock (_Sync)
            {
                if (_Disposed) throw new InvalidOperationException("The background process registry has been disposed.");
                int running = _Entries.FindAll(e => e.Info.Running).Count;
                if (running >= _MaxConcurrent)
                {
                    throw new InvalidOperationException("Already running " + running + " background processes (the limit is " + _MaxConcurrent
                        + ", setting backgroundProcessMaxConcurrent). Stop one with process_stop first.");
                }

                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    WorkingDirectory = workingDirectory,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    RedirectStandardInput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                if (_IsWindows)
                {
                    // cmd.exe does its own parsing and does not understand the \" escaping ArgumentList applies, so
                    // the command goes through verbatim, as run_process passes it.
                    startInfo.FileName = "cmd.exe";
                    startInfo.Arguments = "/c " + command;
                }
                else
                {
                    startInfo.FileName = "/bin/sh";
                    startInfo.ArgumentList.Add("-c");
                    startInfo.ArgumentList.Add(command);
                }

                startInfo.Environment["NO_COLOR"] = "1";
                startInfo.Environment["FORCE_COLOR"] = "0";

                Process process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
                BackgroundProcessInfo info = new BackgroundProcessInfo
                {
                    Id = "p" + _NextNumber.ToString(CultureInfo.InvariantCulture),
                    Name = (name ?? string.Empty).Trim(),
                    Command = command.Trim(),
                    WorkingDirectory = workingDirectory,
                    StartedUtc = DateTime.UtcNow,
                    Running = true
                };
                entry = new BackgroundProcessEntry(info, process, _OutputCapacityChars);
                process.OutputDataReceived += (object sender, DataReceivedEventArgs e) => { if (e.Data != null) entry.AppendLine(e.Data); };
                process.ErrorDataReceived += (object sender, DataReceivedEventArgs e) => { if (e.Data != null) entry.AppendLine(e.Data); };
                process.Exited += (object? sender, EventArgs e) => OnExited(entry);
                process.Start();
                _NextNumber++;
                info.ProcessId = SafeProcessId(process);
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                try { process.StandardInput.Close(); } catch (Exception) { }
                _Entries.Add(entry);
            }

            OnChanged();
            return entry.Snapshot();
        }

        /// <summary>
        /// Returns snapshots of every process (running and exited), oldest first.
        /// </summary>
        /// <returns>The processes.</returns>
        public IReadOnlyList<BackgroundProcessInfo> List()
        {
            lock (_Sync)
            {
                return _Entries.ConvertAll(e => e.Snapshot());
            }
        }

        /// <summary>
        /// Returns a snapshot of one process.
        /// </summary>
        /// <param name="id">The process id (case-insensitive).</param>
        /// <returns>The snapshot, or null when no process has that id.</returns>
        public BackgroundProcessInfo? Get(string id)
        {
            return Find(id)?.Snapshot();
        }

        /// <summary>
        /// Whether any process is still running.
        /// </summary>
        /// <returns>True when at least one process runs.</returns>
        public bool HasRunning()
        {
            lock (_Sync)
            {
                return _Entries.Exists(e => e.Info.Running);
            }
        }

        /// <summary>
        /// Reads the output produced since the previous read. With a pattern, waits until the pattern appears in the
        /// unread output, the process exits, or the timeout passes; without one, waits up to the timeout only when
        /// there is nothing new yet.
        /// </summary>
        /// <param name="id">The process id.</param>
        /// <param name="waitFor">A regular expression to wait for, or null.</param>
        /// <param name="timeoutMs">How long to wait, in milliseconds (0 to <see cref="MaxWaitMs"/>).</param>
        /// <param name="cancellationToken">Cancels the wait.</param>
        /// <returns>The output and the process state.</returns>
        /// <exception cref="KeyNotFoundException">Thrown when no process has the id.</exception>
        /// <exception cref="ArgumentException">Thrown when the pattern is not a valid regular expression.</exception>
        public async Task<BackgroundProcessOutput> ReadAsync(string id, string? waitFor, int timeoutMs, CancellationToken cancellationToken)
        {
            BackgroundProcessEntry entry = Find(id) ?? throw new KeyNotFoundException("No background process has the id '" + id + "'.");
            Regex? pattern = null;
            if (!string.IsNullOrEmpty(waitFor))
            {
                try
                {
                    pattern = new Regex(waitFor, RegexOptions.CultureInvariant | RegexOptions.Multiline, TimeSpan.FromSeconds(2));
                }
                catch (ArgumentException ex)
                {
                    throw new ArgumentException("wait_for is not a valid regular expression: " + ex.Message, nameof(waitFor));
                }
            }

            int wait = Math.Clamp(timeoutMs, 0, MaxWaitMs);
            Stopwatch watch = Stopwatch.StartNew();
            bool timedOut = false;
            Match? match = null;
            while (true)
            {
                Task changed = entry.WaitForChangeAsync();
                string unread = entry.PeekUnread(out _);
                bool running = entry.Snapshot().Running;
                if (pattern != null)
                {
                    match = SafeMatch(pattern, unread);
                    if (match != null || !running)
                    {
                        break;
                    }
                }
                else if (unread.Length > 0 || !running || wait == 0)
                {
                    break;
                }

                TimeSpan remaining = TimeSpan.FromMilliseconds(wait) - watch.Elapsed;
                if (remaining <= TimeSpan.Zero)
                {
                    timedOut = wait > 0 || pattern != null;
                    break;
                }

                await Task.WhenAny(changed, Task.Delay(remaining, cancellationToken)).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
            }

            string text = entry.ReadUnread(out long dropped);
            BackgroundProcessInfo state = entry.Snapshot();
            return new BackgroundProcessOutput
            {
                Id = state.Id,
                Text = text,
                DroppedChars = dropped,
                Running = state.Running,
                ExitCode = state.ExitCode,
                Matched = pattern == null ? (bool?)null : match != null,
                MatchText = match?.Value,
                TimedOut = timedOut && match == null
            };
        }

        /// <summary>
        /// Returns the newest output of a process without consuming it (for <c>/processes</c> views).
        /// </summary>
        /// <param name="id">The process id.</param>
        /// <param name="maxChars">The most characters to return.</param>
        /// <returns>The tail, or null when no process has the id.</returns>
        public string? Tail(string id, int maxChars)
        {
            return Find(id)?.Tail(maxChars);
        }

        /// <summary>
        /// Stops a process and its children, waiting briefly for it to exit, and returns its unread output.
        /// </summary>
        /// <param name="id">The process id.</param>
        /// <param name="cancellationToken">Cancels the wait for exit.</param>
        /// <returns>The final output and state.</returns>
        /// <exception cref="KeyNotFoundException">Thrown when no process has the id.</exception>
        public async Task<BackgroundProcessOutput> StopAsync(string id, CancellationToken cancellationToken)
        {
            BackgroundProcessEntry entry = Find(id) ?? throw new KeyNotFoundException("No background process has the id '" + id + "'.");
            if (entry.Snapshot().Running && entry.Process != null)
            {
                entry.MarkStoppedByUser();
                Kill(entry.Process);
                using (CancellationTokenSource timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
                using (CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, cancellationToken))
                {
                    try
                    {
                        await entry.Process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                    }
                }

                OnExited(entry);
            }

            string text = entry.ReadUnread(out long dropped);
            BackgroundProcessInfo state = entry.Snapshot();
            return new BackgroundProcessOutput { Id = state.Id, Text = text, DroppedChars = dropped, Running = state.Running, ExitCode = state.ExitCode };
        }

        /// <summary>
        /// Stops every running process.
        /// </summary>
        /// <returns>The number of processes stopped.</returns>
        public int StopAll()
        {
            List<BackgroundProcessEntry> running;
            lock (_Sync)
            {
                running = _Entries.FindAll(e => e.Info.Running);
            }

            foreach (BackgroundProcessEntry entry in running)
            {
                entry.MarkStoppedByUser();
                if (entry.Process != null)
                {
                    Kill(entry.Process);
                    try { entry.Process.WaitForExit(3000); } catch (Exception) { }
                    OnExited(entry);
                }
            }

            return running.Count;
        }

        /// <summary>
        /// Removes exited processes from the list.
        /// </summary>
        /// <returns>The number removed.</returns>
        public int RemoveExited()
        {
            int removed;
            lock (_Sync)
            {
                List<BackgroundProcessEntry> exited = _Entries.FindAll(e => !e.Info.Running);
                foreach (BackgroundProcessEntry entry in exited)
                {
                    entry.Process?.Dispose();
                }

                removed = _Entries.RemoveAll(e => !e.Info.Running);
            }

            if (removed > 0) OnChanged();
            return removed;
        }

        /// <summary>
        /// Kills every process tree and refuses further starts.
        /// </summary>
        public void Dispose()
        {
            lock (_Sync)
            {
                if (_Disposed) return;
                _Disposed = true;
            }

            StopAll();
            lock (_Sync)
            {
                foreach (BackgroundProcessEntry entry in _Entries)
                {
                    entry.Process?.Dispose();
                }
            }
        }

        #endregion

        #region Private-Methods

        private BackgroundProcessEntry? Find(string id)
        {
            string key = (id ?? string.Empty).Trim();
            lock (_Sync)
            {
                return _Entries.Find(e => string.Equals(e.Info.Id, key, StringComparison.OrdinalIgnoreCase));
            }
        }

        private void OnExited(BackgroundProcessEntry entry)
        {
            Process? process = entry.Process;
            int exitCode = -1;
            try
            {
                if (process != null && process.HasExited)
                {
                    // Drain the redirected streams so the last lines arrive before the exit is reported.
                    process.WaitForExit();
                    exitCode = process.ExitCode;
                }
            }
            catch (Exception)
            {
            }

            if (entry.Snapshot().Running && (process == null || SafeHasExited(process)))
            {
                entry.MarkExited(exitCode, DateTime.UtcNow);
                OnChanged();
            }
        }

        private static bool SafeHasExited(Process process)
        {
            try { return process.HasExited; } catch (Exception) { return true; }
        }

        private static int SafeProcessId(Process process)
        {
            try { return process.Id; } catch (Exception) { return 0; }
        }

        private static void Kill(Process process)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (Exception)
            {
                // Already gone, or not ours to kill.
            }
        }

        private static Match? SafeMatch(Regex pattern, string text)
        {
            try
            {
                Match match = pattern.Match(text);
                return match.Success ? match : null;
            }
            catch (RegexMatchTimeoutException)
            {
                return null;
            }
        }

        private void OnChanged()
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }

        #endregion
    }
}
