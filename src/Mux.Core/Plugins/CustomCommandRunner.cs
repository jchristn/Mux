namespace Mux.Core.Plugins
{
    using System;
    using System.Diagnostics;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Runs a <see cref="CustomCommandDefinition"/> out-of-process and captures its output. Like hooks,
    /// custom commands are launched as a literal argument vector with no shell, so a command's arguments are
    /// never re-interpreted.
    /// </summary>
    public sealed class CustomCommandRunner
    {
        #region Public-Methods

        /// <summary>
        /// Runs the custom command and returns its result.
        /// </summary>
        /// <param name="definition">The command to run. Must not be null.</param>
        /// <param name="workingDirectory">The working directory for the process.</param>
        /// <param name="cancellationToken">A token to cancel the run.</param>
        /// <returns>The command's captured result.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="definition"/> is null.</exception>
        public async Task<HookRunResult> RunAsync(CustomCommandDefinition definition, string workingDirectory, CancellationToken cancellationToken)
        {
            if (definition is null) throw new ArgumentNullException(nameof(definition));

            HookRunResult result = new HookRunResult { HookName = definition.Name };

            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = definition.Command,
                WorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory) ? System.IO.Directory.GetCurrentDirectory() : workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            foreach (string arg in definition.Args)
            {
                startInfo.ArgumentList.Add(arg ?? string.Empty);
            }

            using Process process = new Process { StartInfo = startInfo };

            StringBuilder stdout = new StringBuilder();
            StringBuilder stderr = new StringBuilder();
            process.OutputDataReceived += (sender, e) => { if (e.Data != null) stdout.Append(e.Data).Append('\n'); };
            process.ErrorDataReceived += (sender, e) => { if (e.Data != null) stderr.Append(e.Data).Append('\n'); };

            try
            {
                process.Start();
            }
            catch (Exception ex)
            {
                result.Started = false;
                result.ExitCode = 127;
                result.StdErr = ex.Message;
                return result;
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            using (CancellationTokenSource timeoutCts = new CancellationTokenSource(definition.TimeoutMs))
            using (CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token))
            {
                try
                {
                    await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    if (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
                    {
                        result.TimedOut = true;
                        TryKill(process);
                        result.ExitCode = 124;
                        result.StdOut = stdout.ToString().TrimEnd('\n', '\r');
                        result.StdErr = stderr.ToString().TrimEnd('\n', '\r');
                        return result;
                    }

                    TryKill(process);
                    throw;
                }
            }

            result.ExitCode = process.ExitCode;
            result.StdOut = stdout.ToString().TrimEnd('\n', '\r');
            result.StdErr = stderr.ToString().TrimEnd('\n', '\r');
            return result;
        }

        #endregion

        #region Private-Methods

        private static void TryKill(Process process)
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
            }
        }

        #endregion
    }
}
