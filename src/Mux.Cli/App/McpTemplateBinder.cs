namespace Mux.Cli.App
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Agent;
    using Mux.Core.Models;

    /// <summary>
    /// Binds the currently discovered MCP tools onto an interactive <see cref="AgentLoopOptions"/> template:
    /// it registers them as callable tools (<see cref="AgentLoopOptions.AdditionalTools"/> plus the
    /// <see cref="AgentLoopOptions.ExternalToolExecutor"/>) and makes the model aware of them by appending an
    /// MCP tools section to the base system prompt. The base prompt (the MCP-free prompt produced by the
    /// normal resolution pipeline) is passed in untouched, so the model's core instructions are preserved and
    /// only the MCP section is added or removed as servers connect and disconnect.
    /// </summary>
    public static class McpTemplateBinder
    {
        /// <summary>
        /// Applies the given MCP tools to the template. When there are no MCP tools the template's system
        /// prompt is exactly <paramref name="basePrompt"/> and <see cref="AgentLoopOptions.AdditionalTools"/>
        /// is null.
        /// </summary>
        /// <param name="template">The interactive template to mutate. Must not be null.</param>
        /// <param name="basePrompt">The MCP-free base system prompt.</param>
        /// <param name="baseCompaction">The compaction system prompt.</param>
        /// <param name="mcpTools">The currently discovered MCP tool definitions. May be null or empty.</param>
        /// <param name="executor">The executor that runs MCP tool calls, or null when none is available.</param>
        /// <param name="builtInToolCount">The number of built-in tools, used to report the effective tool count.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="template"/> is null.</exception>
        public static void Apply(
            AgentLoopOptions template,
            string basePrompt,
            string baseCompaction,
            IReadOnlyList<ToolDefinition>? mcpTools,
            Func<string, JsonElement, string, CancellationToken, Task<ToolResult>>? executor,
            int builtInToolCount)
        {
            if (template == null) throw new ArgumentNullException(nameof(template));

            bool hasMcp = mcpTools != null && mcpTools.Count > 0;

            template.AdditionalTools = hasMcp ? new List<ToolDefinition>(mcpTools!) : null;
            template.ExternalToolExecutor = executor;
            template.SystemPrompt = hasMcp ? (basePrompt ?? string.Empty) + BuildMcpSection(mcpTools!) : (basePrompt ?? string.Empty);
            template.CompactionSystemPrompt = baseCompaction;
            template.EffectiveToolCount = builtInToolCount + (hasMcp ? mcpTools!.Count : 0);
        }

        /// <summary>
        /// Builds the system-prompt section that lists the MCP tools, so the model is explicitly aware they
        /// exist and can be called. Returns an empty string when there are no MCP tools.
        /// </summary>
        /// <param name="mcpTools">The MCP tool definitions.</param>
        /// <returns>The prompt section (leading with a blank line), or an empty string.</returns>
        public static string BuildMcpSection(IReadOnlyList<ToolDefinition>? mcpTools)
        {
            if (mcpTools == null || mcpTools.Count == 0)
            {
                return string.Empty;
            }

            StringBuilder builder = new StringBuilder();
            builder.Append("\n\nThe following tools are provided by connected MCP (Model Context Protocol) servers ");
            builder.Append("and can be called exactly like the built-in tools:\n");
            foreach (ToolDefinition tool in mcpTools)
            {
                builder.Append($"- {tool.Name}: {tool.Description}\n");
            }

            return builder.ToString().TrimEnd();
        }
    }
}
