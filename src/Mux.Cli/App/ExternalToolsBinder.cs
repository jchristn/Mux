namespace Mux.Cli.App
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Agent;
    using Mux.Core.Models;
    using Mux.Core.Tools;

    /// <summary>
    /// Composes every external tool source onto an interactive <see cref="AgentLoopOptions"/> template so
    /// MCP and skills coexist rather than contend for a single hook. MCP rides the legacy
    /// <see cref="AgentLoopOptions.AdditionalTools"/> plus <see cref="AgentLoopOptions.ExternalToolExecutor"/>
    /// path; the skills runtime is registered as an <see cref="IExternalToolProvider"/>. Both append their
    /// prompt sections to the base prompt in a fixed order, so the assembled system prompt is deterministic.
    /// </summary>
    public static class ExternalToolsBinder
    {
        /// <summary>
        /// Applies the current MCP tools and skills runtime to the template.
        /// </summary>
        /// <param name="template">The interactive template to mutate. Must not be null.</param>
        /// <param name="basePrompt">The MCP- and skills-free base system prompt.</param>
        /// <param name="baseCompaction">The compaction system prompt.</param>
        /// <param name="mcpTools">The currently discovered MCP tool definitions, or null.</param>
        /// <param name="mcpExecutor">The executor for MCP tool calls, or null.</param>
        /// <param name="skillRuntime">The skills runtime (an external tool provider), or null when skills are off.</param>
        /// <param name="builtInToolCount">The number of built-in tools, used to report the effective count.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="template"/> is null.</exception>
        public static void Apply(
            AgentLoopOptions template,
            string basePrompt,
            string baseCompaction,
            IReadOnlyList<ToolDefinition>? mcpTools,
            Func<string, JsonElement, string, CancellationToken, Task<ToolResult>>? mcpExecutor,
            SkillRuntime? skillRuntime,
            int builtInToolCount)
        {
            if (template == null) throw new ArgumentNullException(nameof(template));

            bool hasMcp = mcpTools != null && mcpTools.Count > 0;
            template.AdditionalTools = hasMcp ? new List<ToolDefinition>(mcpTools!) : null;
            template.ExternalToolExecutor = mcpExecutor;

            int skillToolCount = 0;
            string skillSection = string.Empty;
            if (skillRuntime != null)
            {
                template.ExternalToolProviders = new List<IExternalToolProvider> { skillRuntime };
                skillToolCount = skillRuntime.GetToolDefinitions().Count;
                skillSection = skillRuntime.BuildPromptSection();
            }
            else
            {
                template.ExternalToolProviders = null;
            }

            string prompt = basePrompt ?? string.Empty;
            prompt += McpTemplateBinder.BuildMcpSection(mcpTools);
            prompt += skillSection;

            template.SystemPrompt = prompt;
            template.CompactionSystemPrompt = baseCompaction;
            template.EffectiveToolCount = builtInToolCount + (hasMcp ? mcpTools!.Count : 0) + skillToolCount;
        }
    }
}
