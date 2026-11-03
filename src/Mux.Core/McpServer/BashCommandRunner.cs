namespace Mux.Core.McpServer
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.Diagnostics;
    using System.IO;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Voltaic.Mcp;

    /// <summary>Runs one noninteractive Bash process, draining both streams with bounded capture.</summary>
    internal static class BashCommandRunner
    {
        internal const int OutputLimit = 65536;

        internal static string ResolveExecutable(string? configured)
        {
            if (!string.IsNullOrWhiteSpace(configured))
            {
                string path = Path.GetFullPath(configured);
                if (!File.Exists(path)) throw new McpToolException("Bash executable does not exist: " + path);
                return path;
            }

            List<string> candidates = new List<string>();
            if (OperatingSystem.IsWindows())
            {
                // Do not pick Windows' legacy bash.exe (a WSL launcher with different path semantics).
                foreach (string root in new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs") })
                    if (!string.IsNullOrWhiteSpace(root)) candidates.Add(Path.Combine(root, "Git", "bin", "bash.exe"));
                foreach (string root in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
                {
                    candidates.Add(Path.Combine(root, "..", "bin", "bash.exe"));
                    if (!root.Contains("Windows", StringComparison.OrdinalIgnoreCase)) candidates.Add(Path.Combine(root, "bash.exe"));
                }
            }
            else
            {
                candidates.Add("/bin/bash");
                candidates.Add("/usr/bin/bash");
                foreach (string root in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
                    candidates.Add(Path.Combine(root, "bash"));
            }
            foreach (string candidate in candidates)
                if (File.Exists(candidate)) return Path.GetFullPath(candidate);
            throw new McpToolException("Bash was not found. Install Bash (Git for Windows on Windows), or pass --bash-path <executable> to mux mcp serve. To use WSL, run mux inside WSL.");
        }

        internal static async Task<string> RunAsync(string? executable, string command, string directory, int timeoutMs, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            ProcessStartInfo start = new ProcessStartInfo
            {
                FileName = ResolveExecutable(executable), WorkingDirectory = directory,
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true
            };
            start.ArgumentList.Add("--noprofile");
            start.ArgumentList.Add("--norc");
            start.ArgumentList.Add("-c");
            start.ArgumentList.Add(command);
            // Explicit scripts only; do not source BASH_ENV or a parent shell's startup configuration.
            start.Environment.Remove("BASH_ENV");
            start.Environment.Remove("ENV");
            using Process process = new Process { StartInfo = start };
            try { process.Start(); }
            catch (Exception ex) when (ex is Win32Exception || ex is InvalidOperationException)
            { throw new McpToolException("Unable to start Bash: " + ex.Message); }
            process.StandardInput.Close();
            using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            deadline.CancelAfter(timeoutMs);
            StringBuilder stdout = new StringBuilder();
            StringBuilder stderr = new StringBuilder();
            bool truncated = false;
            async Task DrainAsync(StreamReader reader, StringBuilder target)
            {
                char[] buffer = new char[4096];
                while (true)
                {
                    int count = await reader.ReadAsync(buffer.AsMemory(), deadline.Token).ConfigureAwait(false);
                    if (count == 0) return;
                    int keep = Math.Min(count, OutputLimit - target.Length);
                    target.Append(buffer, 0, keep);
                    if (keep < count) truncated = true;
                }
            }
            Task drains = Task.WhenAll(DrainAsync(process.StandardOutput, stdout), DrainAsync(process.StandardError, stderr));
            bool timedOut = false;
            try
            {
                await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
                await drains.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                timedOut = !token.IsCancellationRequested;
                try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { } catch (Win32Exception) { }
                await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
                try { await drains.ConfigureAwait(false); } catch (OperationCanceledException) { }
                token.ThrowIfCancellationRequested();
            }
            return JsonSerializer.Serialize(new { stdout = stdout.ToString(), stderr = stderr.ToString(), exit_code = process.ExitCode, timed_out = timedOut, output_truncated = truncated });
        }
    }
}
