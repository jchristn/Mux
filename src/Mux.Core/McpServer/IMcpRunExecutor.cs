namespace Mux.Core.McpServer
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Executes the agent turn behind the MCP <c>run</c> tool. The CLI implements it with the same runtime resolution as
    /// <c>mux print</c>; tests substitute a fake.
    /// </summary>
    public interface IMcpRunExecutor
    {
        /// <summary>
        /// Runs one headless agent turn.
        /// </summary>
        /// <param name="request">The validated request.</param>
        /// <param name="progress">Called with a short message after each agent step (for progress notifications). Never null.</param>
        /// <param name="cancellationToken">Cancels the turn (for example when the MCP client cancels the call).</param>
        /// <returns>The answer and run summary.</returns>
        Task<McpRunResult> RunAsync(McpRunRequest request, Func<string, Task> progress, CancellationToken cancellationToken);
    }
}
