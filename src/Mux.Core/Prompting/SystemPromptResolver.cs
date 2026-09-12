namespace Mux.Core.Prompting
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using Mux.Core.Models;
    using Mux.Core.Settings;

    /// <summary>
    /// The result of resolving a turn's system prompts: the fully-substituted system prompt and the
    /// compaction sidecar prompt.
    /// </summary>
    public sealed class ResolvedSystemPrompt
    {
        /// <summary>The effective system prompt with all placeholders substituted.</summary>
        public string SystemPrompt { get; set; } = string.Empty;

        /// <summary>The effective compaction system prompt (empty inherits the built-in default inside the loop).</summary>
        public string CompactionSystemPrompt { get; set; } = string.Empty;
    }

    /// <summary>
    /// Builds a turn's effective system prompt from a loaded persona prompt, the active prompt profile, and
    /// the runtime tool context — the front-end-agnostic resolution shared by the CLI's
    /// <c>CommandRuntimeResolver</c> and the desktop's turn runner so both front ends produce an identical
    /// prompt. Selects the tools-disabled variant when the endpoint has no tool support, fills the
    /// <c>{WorkingDirectory}</c>, <c>{ToolDescriptions}</c>, and <c>{TaskPlanningGuidance}</c> placeholders,
    /// and appends any caller-supplied text after substitution so it survives profile switches.
    /// </summary>
    public static class SystemPromptResolver
    {
        /// <summary>
        /// Resolves the effective system prompt and compaction prompt for a turn.
        /// </summary>
        /// <param name="loadedSystemPrompt">The persona prompt to use when tools are enabled — typically the
        /// result of <see cref="SettingsLoader.LoadSystemPrompt"/> (which already honors the active profile's
        /// system prompt and any CLI override). Null is treated as empty.</param>
        /// <param name="activeProfile">The active prompt profile, supplying the tools-disabled prompt and the
        /// compaction prompt. Must not be null.</param>
        /// <param name="toolsEnabled">Whether the selected endpoint supports tool calling.</param>
        /// <param name="tools">The tools whose names and descriptions fill <c>{ToolDescriptions}</c> (only
        /// when <paramref name="toolsEnabled"/> is true). May be null or empty.</param>
        /// <param name="workingDirectory">The directory substituted for <c>{WorkingDirectory}</c>.</param>
        /// <param name="taskPlanningEnabled">Whether task planning is enabled in settings; the
        /// <c>{TaskPlanningGuidance}</c> block is only injected when this is true and tools are enabled.</param>
        /// <param name="appendSystemPrompt">Optional caller-supplied text appended after all substitution.</param>
        /// <returns>The resolved prompts; never null.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="activeProfile"/> is null.</exception>
        public static ResolvedSystemPrompt Resolve(
            string? loadedSystemPrompt,
            PromptProfile activeProfile,
            bool toolsEnabled,
            IReadOnlyList<ToolDefinition>? tools,
            string workingDirectory,
            bool taskPlanningEnabled,
            string? appendSystemPrompt)
        {
            if (activeProfile == null) throw new ArgumentNullException(nameof(activeProfile));

            string baseText = toolsEnabled
                ? (loadedSystemPrompt ?? string.Empty)
                : (string.IsNullOrWhiteSpace(activeProfile.ToolsDisabledPrompt)
                    ? Defaults.ToolsDisabledSystemPrompt
                    : activeProfile.ToolsDisabledPrompt);

            StringBuilder toolDescBuilder = new StringBuilder();
            if (toolsEnabled && tools != null)
            {
                foreach (ToolDefinition tool in tools)
                {
                    toolDescBuilder.AppendLine($"- {tool.Name}: {tool.Description}");
                }
            }

            string taskPlanningGuidance = (toolsEnabled && taskPlanningEnabled)
                ? Defaults.TaskPlanningGuidance
                : string.Empty;

            string systemPrompt = baseText
                .Replace("{WorkingDirectory}", workingDirectory ?? string.Empty)
                .Replace("{ToolDescriptions}", toolDescBuilder.ToString().TrimEnd())
                .Replace("{TaskPlanningGuidance}", taskPlanningGuidance);

            // Append caller-supplied system-prompt text after all placeholder substitution so it survives
            // profile switches and is never consumed by a placeholder.
            if (!string.IsNullOrWhiteSpace(appendSystemPrompt))
            {
                systemPrompt = string.IsNullOrEmpty(systemPrompt)
                    ? appendSystemPrompt!.Trim()
                    : systemPrompt + Environment.NewLine + Environment.NewLine + appendSystemPrompt!.Trim();
            }

            return new ResolvedSystemPrompt
            {
                SystemPrompt = systemPrompt,
                CompactionSystemPrompt = activeProfile.CompactionPrompt ?? string.Empty
            };
        }
    }
}
