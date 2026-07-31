namespace Mux.Core.Tools
{
    using System.Collections.Generic;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Models;

    /// <summary>
    /// Supplies tools to the agent loop from outside the built-in registry — for example an MCP server
    /// connection or the skills catalog. The agent loop aggregates every provider's definitions alongside
    /// the built-in tools and routes a tool call to the first provider that claims the name.
    /// </summary>
    /// <remarks>Implementations must be safe to call concurrently: the agent loop may invoke
    /// <see cref="GetToolDefinitions"/>, <see cref="HasTool"/>, <see cref="GetMutationKind"/>, and
    /// <see cref="ExecuteAsync"/> from multiple turns at once.</remarks>
    public interface IExternalToolProvider
    {
        /// <summary>
        /// A short, stable name identifying this provider (for diagnostics and ordering).
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Returns the tool definitions this provider currently exposes. Never null; may be empty.
        /// </summary>
        /// <returns>The provider's current tool definitions.</returns>
        IReadOnlyList<ToolDefinition> GetToolDefinitions();

        /// <summary>
        /// Indicates whether this provider owns the tool with the specified name.
        /// </summary>
        /// <param name="toolName">The tool name to look up. Null or unknown names return <c>false</c>.</param>
        /// <returns><c>true</c> when this provider can execute the named tool; otherwise <c>false</c>.</returns>
        bool HasTool(string toolName);

        /// <summary>
        /// Returns the mutation classification for one of this provider's tools, used to decide whether the
        /// call serializes through the workspace write lease. Providers that cannot classify a tool should
        /// return <see cref="ToolMutationKind.Mutating"/>, the safe default.
        /// </summary>
        /// <param name="toolName">The tool name to classify.</param>
        /// <returns>The tool's <see cref="ToolMutationKind"/>.</returns>
        ToolMutationKind GetMutationKind(string toolName);

        /// <summary>
        /// Executes one of this provider's tools and returns the result.
        /// </summary>
        /// <param name="toolName">The tool name to execute.</param>
        /// <param name="arguments">The parsed JSON arguments from the model.</param>
        /// <param name="workingDirectory">The working directory for the execution context.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>A <see cref="ToolResult"/> containing the execution output.</returns>
        Task<ToolResult> ExecuteAsync(
            string toolName,
            JsonElement arguments,
            string workingDirectory,
            CancellationToken cancellationToken);
    }
}
