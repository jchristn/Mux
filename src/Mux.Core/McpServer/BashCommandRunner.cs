namespace Mux.Core.McpServer
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Voltaic.Mcp;

    /// <summary>Runs one noninteractive Bash process, draining both streams with bounded capture.</summary>
    internal static class BashCommandRunner
    {
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
            ShellCommandResult result = await ShellCommandRunner.RunAsync(start, "Bash", timeoutMs, token).ConfigureAwait(false);
            return JsonSerializer.Serialize(new { stdout = result.Stdout, stderr = result.Stderr, exit_code = result.ExitCode, timed_out = result.TimedOut, output_truncated = result.OutputTruncated });
        }
    }
}
