namespace Mux.Core.Subagents
{
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Runs a single subagent to completion in isolation and returns its result. The
    /// <c>spawn_subagent</c> tool depends on this abstraction rather than the concrete agent loop so the
    /// tool can be unit-tested with a fake executor, while production wires
    /// <see cref="AgentLoopSubagentExecutor"/> which runs a nested <c>AgentLoop</c>.
    /// </summary>
    public interface ISubagentExecutor
    {
        /// <summary>
        /// Executes the given subagent against the supplied task prompt in an isolated conversation.
        /// </summary>
        /// <param name="definition">The subagent to run. Must not be null.</param>
        /// <param name="prompt">The task prompt for the subagent. Must not be null or empty.</param>
        /// <param name="workingDirectory">The working directory for the child run's tools.</param>
        /// <param name="cancellationToken">A token to cancel the subagent run.</param>
        /// <returns>The subagent's result.</returns>
        Task<SubagentResult> ExecuteAsync(SubagentDefinition definition, string prompt, string workingDirectory, CancellationToken cancellationToken);
    }
}
