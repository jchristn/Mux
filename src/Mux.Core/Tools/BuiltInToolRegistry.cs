namespace Mux.Core.Tools
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Models;
    using Mux.Core.Tools.Tools;

    /// <summary>
    /// Registry of all built-in tools available to the agent loop.
    /// Provides tool definition retrieval and execution routing.
    /// </summary>
    public class BuiltInToolRegistry
    {
        #region Private-Members

        private Dictionary<string, IToolExecutor> _Tools = new Dictionary<string, IToolExecutor>(StringComparer.OrdinalIgnoreCase);

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="BuiltInToolRegistry"/> class,
        /// registering all built-in tool implementations.
        /// </summary>
        public BuiltInToolRegistry()
        {
            RegisterTool(new ReadFileTool());
            RegisterTool(new WriteFileTool());
            RegisterTool(new EditFileTool());
            RegisterTool(new MultiEditTool());
            RegisterTool(new DeleteFileTool());
            RegisterTool(new FileMetadataTool());
            RegisterTool(new ListDirectoryTool());
            RegisterTool(new ManageDirectoryTool());
            RegisterTool(new GlobTool());
            RegisterTool(new GrepTool());
            RegisterTool(new RunProcessTool());
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

        #endregion

        #region Private-Methods

        private void RegisterTool(IToolExecutor tool)
        {
            _Tools[tool.Name] = tool;
        }

        #endregion
    }
}
