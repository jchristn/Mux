namespace Mux.Core.Plugins
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Runs hooks out-of-process for a lifecycle event. Each matching hook is launched as its own process
    /// with the event payload delivered as a JSON document on stdin; stdout and stderr are captured and the
    /// exit code is observed. Hooks run sequentially in configuration order. A hook marked
    /// <see cref="HookDefinition.Blocking"/> that exits non-zero on a vetoable event marks the aggregate run
    /// as vetoed, letting the caller block the action (for example refusing a prompt submission).
    /// <para>
    /// Hooks are untrusted user configuration executed as plain processes: mux never runs them through a
    /// shell, so arguments are passed as a literal argument vector with no interpolation.
    /// </para>
    /// </summary>
    public sealed class HookRunner
    {
        #region Public-Methods

        /// <summary>
        /// Runs every hook registered for <paramref name="hookEvent"/> and returns their results.
        /// </summary>
        /// <param name="registry">The plugin registry supplying the hooks. Must not be null.</param>
        /// <param name="hookEvent">The event that fired.</param>
        /// <param name="payloadJson">A JSON payload delivered to each hook on stdin. Null sends nothing.</param>
        /// <param name="workingDirectory">The working directory for the hook processes.</param>
        /// <param name="cancellationToken">A token to cancel the run.</param>
        /// <returns>One <see cref="HookRunResult"/> per hook, in configuration order.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="registry"/> is null.</exception>
        public async Task<IReadOnlyList<HookRunResult>> RunAsync(
            PluginRegistry registry,
            HookEventEnum hookEvent,
            string? payloadJson,
            string workingDirectory,
            CancellationToken cancellationToken)
        {
            if (registry is null) throw new ArgumentNullException(nameof(registry));

            List<HookRunResult> results = new List<HookRunResult>();
            bool vetoable = IsVetoable(hookEvent);

            foreach (HookDefinition hook in registry.HooksFor(hookEvent))
            {
                HookRunResult result = await RunOneAsync(hook, payloadJson, workingDirectory, vetoable, cancellationToken).ConfigureAwait(false);
                results.Add(result);
            }

            return results;
        }

        /// <summary>
        /// Determines whether any result in a run vetoed the event.
        /// </summary>
        /// <param name="results">The results to inspect.</param>
        /// <returns>True when at least one hook vetoed.</returns>
        public static bool WasVetoed(IReadOnlyList<HookRunResult> results)
        {
            if (results == null)
            {
                return false;
            }

            foreach (HookRunResult result in results)
            {
                if (result.Vetoed)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Whether an event can be vetoed by a blocking hook.
        /// </summary>
        /// <param name="hookEvent">The event.</param>
        /// <returns>True for events that support veto.</returns>
        public static bool IsVetoable(HookEventEnum hookEvent)
        {
            return hookEvent == HookEventEnum.UserPromptSubmit;
        }

        #endregion

        #region Private-Methods

        private static async Task<HookRunResult> RunOneAsync(
            HookDefinition hook,
            string? payloadJson,
            string workingDirectory,
            bool vetoable,
            CancellationToken cancellationToken)
        {
            string hookName = string.IsNullOrWhiteSpace(hook.Name) ? hook.Command : hook.Name;
            HookRunResult result = new HookRunResult { HookName = hookName };

            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = hook.Command,
                WorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory) ? System.IO.Directory.GetCurrentDirectory() : workingDirectory,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            foreach (string arg in hook.Args)
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
                // A blocking hook that cannot start does not veto — treat a broken hook as absent rather than
                // as a deliberate refusal, so a typo in hooks.json never wedges the session.
                return result;
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            try
            {
                if (!string.IsNullOrEmpty(payloadJson))
                {
                    await process.StandardInput.WriteAsync(payloadJson).ConfigureAwait(false);
                }

                process.StandardInput.Close();
            }
            catch (Exception)
            {
                // The process may have exited before consuming stdin; ignore.
            }

            using (CancellationTokenSource timeoutCts = new CancellationTokenSource(hook.TimeoutMs))
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
                        result.StdOut = TrimTrailingNewline(stdout.ToString());
                        result.StdErr = TrimTrailingNewline(stderr.ToString());
                        return result;
                    }

                    TryKill(process);
                    throw;
                }
            }

            result.ExitCode = process.ExitCode;
            result.StdOut = TrimTrailingNewline(stdout.ToString());
            result.StdErr = TrimTrailingNewline(stderr.ToString());
            result.Vetoed = vetoable && hook.Blocking && process.ExitCode != 0;
            return result;
        }

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

        private static string TrimTrailingNewline(string value)
        {
            return value.TrimEnd('\n', '\r');
        }

        #endregion
    }
}
