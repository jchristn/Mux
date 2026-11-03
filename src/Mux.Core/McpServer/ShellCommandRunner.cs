namespace Mux.Core.McpServer
{
    using System;
    using System.ComponentModel;
    using System.Diagnostics;
    using System.IO;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Voltaic.Mcp;

    /// <summary>Shared bounded capture and process lifetime handling for MCP shell tools.</summary>
    internal static class ShellCommandRunner
    {
        internal const int OutputLimit = 65536;

        internal static async Task<ShellCommandResult> RunAsync(ProcessStartInfo start, string shellName, int timeoutMs, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            using Process process = new Process { StartInfo = start };
            try { process.Start(); }
            catch (Exception ex) when (ex is Win32Exception || ex is InvalidOperationException)
            { throw new McpToolException("Unable to start " + shellName + ": " + ex.Message); }
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
            return new ShellCommandResult(stdout.ToString(), stderr.ToString(), process.ExitCode, timedOut, truncated);
        }
    }

    internal sealed record ShellCommandResult(string Stdout, string Stderr, int ExitCode, bool TimedOut, bool OutputTruncated);
}
