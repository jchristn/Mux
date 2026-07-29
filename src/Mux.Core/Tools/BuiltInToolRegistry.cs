namespace Mux.Core.Tools
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Models;
    using Mux.Core.Search;
    using Mux.Core.Tools.Tools;

    /// <summary>
    /// Registry of all built-in tools available to the agent loop.
    /// Provides tool definition retrieval and execution routing.
    /// </summary>
    public class BuiltInToolRegistry
    {
        #region Private-Members

        private Dictionary<string, IToolExecutor> _Tools = new Dictionary<string, IToolExecutor>(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, ToolMutationKind> _MutationKinds = new Dictionary<string, ToolMutationKind>(StringComparer.OrdinalIgnoreCase);

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="BuiltInToolRegistry"/> class,
        /// registering all built-in tool implementations.
        /// </summary>
        public BuiltInToolRegistry(MuxSettings? muxSettings = null)
        {
            RegisterTool(new ReadFileTool(), ToolMutationKind.ReadOnly);
            RegisterTool(new WriteFileTool(), ToolMutationKind.Mutating);
            RegisterTool(new EditFileTool(), ToolMutationKind.Mutating);
            RegisterTool(new MultiEditTool(), ToolMutationKind.Mutating);
            RegisterTool(new DeleteFileTool(), ToolMutationKind.Mutating);
            RegisterTool(new FileMetadataTool(), ToolMutationKind.ReadOnly);
            RegisterTool(new ListDirectoryTool(), ToolMutationKind.ReadOnly);
            RegisterTool(new ManageDirectoryTool(), ToolMutationKind.Mutating);
            RegisterTool(new GlobTool(), ToolMutationKind.ReadOnly);
            RegisterTool(new GrepTool(), ToolMutationKind.ReadOnly);
            RegisterTool(new RunProcessTool(), ToolMutationKind.Mutating);
            RegisterTool(new WebRetrieveTool(muxSettings?.IgnoreCertErrors ?? false), ToolMutationKind.ReadOnly);

            if (WebSearchServiceFactory.Create(muxSettings) is { } searchService)
            {
                RegisterTool(new WebSearchTool(searchService), ToolMutationKind.ReadOnly);
            }
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Returns all registered tool definitions suitable for sending to the LLM.
        /// </summary>
        /// <returns>A list of <see cref="ToolDefinition"/> objects describing each tool.</returns>
        public List<ToolDefinition> GetToolDefinitions()
        {
            List<ToolDefinition> definitions = new List<ToolDefinition>();

            foreach (KeyValuePair<string, IToolExecutor> kvp in _Tools)
            {
                ToolDefinition definition = new ToolDefinition
                {
                    Name = kvp.Value.Name,
                    Description = kvp.Value.Description,
                    ParametersSchema = kvp.Value.ParametersSchema
                };

                definitions.Add(definition);
            }

            return definitions;
        }

        /// <summary>
        /// Executes a tool by name, routing the call to the appropriate <see cref="IToolExecutor"/>.
        /// </summary>
        /// <param name="toolCallId">The unique identifier for this tool call.</param>
        /// <param name="toolName">The name of the tool to execute.</param>
        /// <param name="arguments">The parsed JSON arguments from the LLM.</param>
        /// <param name="workingDirectory">The current working directory for the execution context.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>A <see cref="ToolResult"/> containing the execution output.</returns>
        /// <exception cref="KeyNotFoundException">Thrown when the specified tool name is not registered.</exception>
        public async Task<ToolResult> ExecuteAsync(string toolCallId, string toolName, JsonElement arguments, string workingDirectory, CancellationToken cancellationToken)
        {
            if (!_Tools.TryGetValue(toolName, out IToolExecutor? executor))
            {
                return new ToolResult
                {
                    ToolCallId = toolCallId,
                    Success = false,
                    Content = JsonSerializer.Serialize(new { error = "unknown_tool", message = $"Tool '{toolName}' is not registered." })
                };
            }

            return await executor.ExecuteAsync(toolCallId, arguments, workingDirectory, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Checks whether a tool with the specified name is registered.
        /// </summary>
        /// <param name="name">The tool name to look up.</param>
        /// <returns><c>true</c> if the tool exists; otherwise <c>false</c>.</returns>
        public bool HasTool(string name)
        {
            return _Tools.ContainsKey(name);
        }

        /// <summary>
        /// Gets the mutation classification for a tool. Returns <see cref="ToolMutationKind.Mutating"/>
        /// for any tool that is not a registered read-only built-in — the safe default, so unknown or
        /// external (for example MCP) tools serialize through the workspace write lease.
        /// </summary>
        /// <param name="toolName">The tool name to classify. Null or unknown names are treated as mutating.</param>
        /// <returns>The tool's <see cref="ToolMutationKind"/>.</returns>
        public ToolMutationKind GetMutationKind(string toolName)
        {
            if (toolName != null && _MutationKinds.TryGetValue(toolName, out ToolMutationKind kind))
            {
                return kind;
            }

            return ToolMutationKind.Mutating;
        }

        #endregion

        #region Private-Methods

        private void RegisterTool(IToolExecutor tool, ToolMutationKind mutationKind)
        {
            _Tools[tool.Name] = tool;
            _MutationKinds[tool.Name] = mutationKind;
        }

        #endregion
    }
}
