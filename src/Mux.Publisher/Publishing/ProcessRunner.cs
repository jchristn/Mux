namespace Mux.Publisher.Publishing
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>Runs external processes. Abstracted so packaging logic can be tested without shelling out.</summary>
    public interface IProcessRunner
    {
        /// <summary>
        /// Runs a process to completion, capturing stdout/stderr.
        /// </summary>
        /// <param name="executable">The program to run.</param>
        /// <param name="arguments">The argument list (no shell splitting).</param>
        /// <param name="workingDirectory">The working directory, or null for the current directory.</param>
        /// <param name="ct">A cancellation token.</param>
        /// <returns>The captured result.</returns>
        Task<ProcessResult> RunAsync(string executable, IReadOnlyList<string> arguments, string? workingDirectory, CancellationToken ct);
    }

    /// <summary>The captured outcome of a process run.</summary>
    public sealed class ProcessResult
    {
        /// <summary>The process exit code.</summary>
        public int ExitCode { get; set; }

        /// <summary>Captured standard output.</summary>
        public string StandardOutput { get; set; } = string.Empty;

        /// <summary>Captured standard error.</summary>
        public string StandardError { get; set; } = string.Empty;
    }

    /// <summary>The default <see cref="IProcessRunner"/> backed by <see cref="Process"/>.</summary>
    public sealed class ProcessRunner : IProcessRunner
    {
        /// <inheritdoc />
        public async Task<ProcessResult> RunAsync(string executable, IReadOnlyList<string> arguments, string? workingDirectory, CancellationToken ct)
        {
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = executable,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            if (!string.IsNullOrEmpty(workingDirectory))
            {
                startInfo.WorkingDirectory = workingDirectory;
            }

            foreach (string argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            using (Process process = new Process { StartInfo = startInfo })
            {
                StringBuilder stdout = new StringBuilder();
                StringBuilder stderr = new StringBuilder();
                process.OutputDataReceived += (_, e) => { if (e.Data != null) stdout.AppendLine(e.Data); };
                process.ErrorDataReceived += (_, e) => { if (e.Data != null) stderr.AppendLine(e.Data); };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                await process.WaitForExitAsync(ct).ConfigureAwait(false);

                return new ProcessResult
                {
                    ExitCode = process.ExitCode,
                    StandardOutput = stdout.ToString(),
                    StandardError = stderr.ToString()
                };
            }
        }
    }
}
