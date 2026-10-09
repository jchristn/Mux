namespace Test.Shared.Support
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.McpServer;

    /// <summary>
    /// A scripted <see cref="IMcpRunExecutor"/> for MCP server tests: records each request, reports a fixed number of
    /// progress steps, and either answers at once or waits until cancelled.
    /// </summary>
    public sealed class FakeMcpRunExecutor : IMcpRunExecutor
    {
        #region Public-Members

        /// <summary>The requests received, in order.</summary>
        public List<McpRunRequest> Requests { get; } = new List<McpRunRequest>();

        /// <summary>Progress steps reported per run.</summary>
        public int ProgressSteps { get; set; }

        /// <summary>When true, the run waits until its token is cancelled.</summary>
        public bool WaitForCancel { get; set; }

        /// <summary>Set when a waiting run observed cancellation.</summary>
        public TaskCompletionSource<bool> Cancelled { get; } = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>Set when a waiting run has started.</summary>
        public TaskCompletionSource<bool> Started { get; } = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        #endregion

        #region Public-Methods

        /// <inheritdoc/>
        public async Task<McpRunResult> RunAsync(McpRunRequest request, Func<string, Task> progress, CancellationToken cancellationToken)
        {
            lock (Requests)
            {
                Requests.Add(request);
            }

            for (int i = 1; i <= ProgressSteps; i++)
            {
                await progress("step " + i).ConfigureAwait(false);
                await Task.Delay(30, cancellationToken).ConfigureAwait(false);
            }

            if (WaitForCancel)
            {
                Started.TrySetResult(true);
                try
                {
                    await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    Cancelled.TrySetResult(true);
                    throw;
                }
            }

            return new McpRunResult
            {
                Answer = "ANSWER: " + request.Prompt,
                Status = "completed",
                Endpoint = request.Endpoint ?? "default",
                Model = "fake-model",
                Iterations = 2,
                ToolCalls = 1,
                DurationMs = 5,
                InputTokens = 10,
                OutputTokens = 4
            };
        }

        #endregion
    }
}
