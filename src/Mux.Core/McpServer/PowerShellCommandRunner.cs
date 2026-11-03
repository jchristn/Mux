namespace Mux.Core.McpServer
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Voltaic.Mcp;

    /// <summary>Discovers PowerShell and runs a fresh, noninteractive shell per call.</summary>
    internal static class PowerShellCommandRunner
    {
        internal static IEnumerable<string> ExecutableCandidates(bool windows, string searchPath, string programFiles, string systemDirectory)
        {
            string name = windows ? "pwsh.exe" : "pwsh";
            string[] roots = searchPath.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (string root in roots) yield return Path.Combine(root.Trim('"'), name);
            if (windows)
            {
                if (!string.IsNullOrWhiteSpace(programFiles)) yield return Path.Combine(programFiles, "PowerShell", "7", "pwsh.exe");
                if (!string.IsNullOrWhiteSpace(systemDirectory)) yield return Path.Combine(systemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
                foreach (string root in roots) yield return Path.Combine(root.Trim('"'), "powershell.exe");
            }
        }

        internal static string ResolveExecutable(string? configured)
        {
            if (!string.IsNullOrWhiteSpace(configured))
            {
                string path = Path.GetFullPath(configured);
                if (!File.Exists(path)) throw new McpToolException("PowerShell executable does not exist: " + path);
                return path;
            }
            foreach (string candidate in ExecutableCandidates(OperatingSystem.IsWindows(), Environment.GetEnvironmentVariable("PATH") ?? "", Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.SystemDirectory))
                if (File.Exists(candidate)) return Path.GetFullPath(candidate);
            throw new McpToolException("PowerShell was not found. Install PowerShell (pwsh) and add it to PATH, or pass --powershell-path <executable> to mux mcp serve. On Windows, Windows PowerShell (powershell.exe) is also supported.");
        }

        internal static async Task<string> RunAsync(string? executable, string command, string directory, int timeoutMs, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            string path = ResolveExecutable(executable);
            ProcessStartInfo start = new ProcessStartInfo
            {
                FileName = path, WorkingDirectory = directory,
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
            };
            // Explicit UTF-8 output also makes Windows PowerShell's redirected output predictable.
            // Encode the script as UTF-16LE to preserve quotes, newlines, and Unicode across native argv parsing.
            // Parse the submitted script separately so leading using/param declarations remain valid.
            // Script-block invocation can otherwise turn a final nonterminating error into exit 0.
            string body = command + "\nif (-not $?) { exit 1 }\n";
            string script = "[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false); $OutputEncoding = [Console]::OutputEncoding; $ProgressPreference = 'SilentlyContinue'; & ([scriptblock]::Create('" + body.Replace("'", "''", StringComparison.Ordinal) + "'))";
            foreach (string argument in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-InputFormat", "Text", "-OutputFormat", "Text", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(script)) })
                start.ArgumentList.Add(argument);
            ShellCommandResult result = await ShellCommandRunner.RunAsync(start, "PowerShell", timeoutMs, token).ConfigureAwait(false);
            return JsonSerializer.Serialize(new { stdout = result.Stdout, stderr = result.Stderr, exit_code = result.ExitCode, timed_out = result.TimedOut, output_truncated = result.OutputTruncated, executable = path });
        }
    }
}
