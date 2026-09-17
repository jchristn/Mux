namespace Mux.Core.Prompting
{
    using System;
    using System.Collections.Generic;
    using Mux.Core.Settings;

    /// <summary>
    /// The single, code-defined catalog of every operational and persona prompt the model reads. Defining the
    /// catalog in code (rather than a seed file) guarantees a default always exists even when no configuration
    /// is present, which is what lets a Prompts view be "pre-populated with sensible defaults out of the box."
    /// User overrides are additive and live elsewhere (profile persona prompts in <c>prompts.json</c>'s
    /// profiles, operational overrides in its <c>operational</c> map); this catalog only supplies the defaults
    /// and the metadata, and <see cref="PromptResolver"/> is the one place that layers overrides on top.
    /// </summary>
    /// <remarks>
    /// The persona, tools-disabled, compaction-system, and task-planning defaults intentionally source their
    /// text from <see cref="Defaults"/>, whose named constants remain their single home and are already
    /// referenced across the codebase; the catalog surfaces them for editing without relocating the literal.
    /// The previously duplicated literals — the synthetic-summary marker and the compaction user framing — now
    /// live here as their single home so both former call sites (<c>ConversationCompactor</c> and
    /// <c>AgentLoop</c>) resolve to one entry.
    /// </remarks>
    public static class PromptCatalog
    {
        private static readonly IReadOnlyList<PromptDefinition> _All = BuildAll();
        private static readonly Dictionary<string, PromptDefinition> _ByKey = IndexByKey(_All);

        /// <summary>The synthetic-summary marker text. Kept as a stable marker (its single home) because it is
        /// also used to <em>detect</em> already-persisted synthetic summaries, so it must not drift.</summary>
        public const string SyntheticSummaryPrefix = "[mux summary generated automatically; older conversation condensed]";

        /// <summary>
        /// Gets every catalog definition in a stable, display-friendly order (grouped by kind).
        /// </summary>
        public static IReadOnlyList<PromptDefinition> All
        {
            get { return _All; }
        }

        /// <summary>
        /// Returns the definition for a key.
        /// </summary>
        /// <param name="key">The catalog key; null or unknown returns false.</param>
        /// <param name="definition">The definition when found; otherwise null.</param>
        /// <returns>True when a definition exists for the key; otherwise false.</returns>
        public static bool TryGet(string? key, out PromptDefinition? definition)
        {
            definition = null;
            if (string.IsNullOrEmpty(key)) return false;
            return _ByKey.TryGetValue(key!, out definition);
        }

        /// <summary>
        /// Returns the coded default content for a key, with no overrides applied. Never throws for a known
        /// key; use this from paths (such as tool description getters) that must never fail.
        /// </summary>
        /// <param name="key">The catalog key.</param>
        /// <returns>The default content, or an empty string when the key is unknown.</returns>
        public static string DefaultFor(string key)
        {
            return _ByKey.TryGetValue(key ?? string.Empty, out PromptDefinition? definition) ? definition!.DefaultContent : string.Empty;
        }

        private static Dictionary<string, PromptDefinition> IndexByKey(IReadOnlyList<PromptDefinition> all)
        {
            Dictionary<string, PromptDefinition> map = new Dictionary<string, PromptDefinition>(StringComparer.Ordinal);
            foreach (PromptDefinition definition in all)
            {
                map[definition.Key] = definition;
            }

            return map;
        }

        private static IReadOnlyList<PromptDefinition> BuildAll()
        {
            List<PromptDefinition> list = new List<PromptDefinition>();

            // --- Persona (profile-scoped): text sourced from Defaults, edited via the active profile. ---
            list.Add(new PromptDefinition(
                "system", PromptKind.SystemPersona, PromptScope.Profile,
                "System prompt",
                "The main persona sent with every turn when the endpoint supports tools.",
                new[] { "{WorkingDirectory}", "{ToolDescriptions}", "{TaskPlanningGuidance}" },
                Defaults.SystemPrompt));

            list.Add(new PromptDefinition(
                "tools-disabled", PromptKind.SystemPersona, PromptScope.Profile,
                "System prompt (tools disabled)",
                "The persona used instead of the system prompt when the endpoint has no tool support.",
                new[] { "{WorkingDirectory}" },
                Defaults.ToolsDisabledSystemPrompt));

            // --- Compaction (global). ---
            list.Add(new PromptDefinition(
                "compaction.system", PromptKind.Compaction, PromptScope.Global,
                "Compaction system prompt",
                "The system prompt for the sidecar call that summarizes older history. A profile's compaction prompt overrides it.",
                null,
                Defaults.CompactionSystemPrompt));

            list.Add(new PromptDefinition(
                "compaction.user", PromptKind.Compaction, PromptScope.Global,
                "Compaction user framing",
                "The instruction that introduces the older history to compact. The conversation digest is appended after it.",
                null,
                "Compact this older conversation history:"));

            list.Add(new PromptDefinition(
                "compaction.summary-prefix", PromptKind.Compaction, PromptScope.Global,
                "Synthetic summary marker",
                "The marker prefixed to a compaction summary. Also used to detect already-summarized turns, so it behaves as a stable marker.",
                null,
                SyntheticSummaryPrefix));

            // --- Task planning (global): text sourced from Defaults. ---
            list.Add(new PromptDefinition(
                "task-planning.guidance", PromptKind.TaskPlanning, PromptScope.Global,
                "Task-planning guidance",
                "Guidance injected into the system prompt when task planning is enabled.",
                null,
                Defaults.TaskPlanningGuidance));

            // --- Title generation (global). ---
            list.Add(new PromptDefinition(
                "title.system", PromptKind.TitleGeneration, PromptScope.Global,
                "Title system prompt",
                "Instructs the model to produce a short session title.",
                null,
                "You write concise, descriptive titles for chat conversations. Reply with ONLY the title: 3 to 7 words, Title Case, no surrounding quotes and no trailing punctuation."));

            list.Add(new PromptDefinition(
                "title.user", PromptKind.TitleGeneration, PromptScope.Global,
                "Title user framing",
                "The instruction that introduces the conversation to title. The transcript is appended after it.",
                null,
                "Write a short title that summarizes this conversation:"));

            // --- Tool-section lead-ins (global): the surrounding blank line and per-item lines are owned by code. ---
            list.Add(new PromptDefinition(
                "section.skills", PromptKind.ToolSection, PromptScope.Global,
                "Skills section lead-in",
                "Introduces the list of available skills in the system prompt.",
                null,
                "The following skills are available. Call the `skill` tool with a skill's name to read its instructions, then `run_skill` to execute one of its commands:"));

            list.Add(new PromptDefinition(
                "section.mcp", PromptKind.ToolSection, PromptScope.Global,
                "MCP section lead-in",
                "Introduces the list of connected MCP tools in the system prompt.",
                null,
                "The following tools are provided by connected MCP (Model Context Protocol) servers and can be called exactly like the built-in tools:"));

            // --- Tool descriptions (global). ---
            AddToolDescriptions(list);

            // --- Tool-result payload messages (global). ---
            list.Add(new PromptDefinition(
                "result.tool_call_denied", PromptKind.ToolResult, PromptScope.Global,
                "Tool result: user-denied",
                "The message returned to the model when the user denies a tool call.",
                null,
                "The user denied this tool call."));

            list.Add(new PromptDefinition(
                "result.unknown_tool", PromptKind.ToolResult, PromptScope.Global,
                "Tool result: unknown tool",
                "The message returned to the model when it calls a tool that is not registered.",
                new[] { "{ToolName}" },
                "Tool '{ToolName}' is not registered and no external executor is configured."));

            list.Add(new PromptDefinition(
                "result.tool_policy_denied", PromptKind.ToolResult, PromptScope.Global,
                "Tool result: policy-denied",
                "The message returned to the model when a tool is blocked by the configured tool policy.",
                new[] { "{ToolName}" },
                "Tool '{ToolName}' is not permitted by the configured tool policy (--allow-tools/--deny-tools)."));

            list.Add(new PromptDefinition(
                "digest.truncation-marker", PromptKind.ToolResult, PromptScope.Global,
                "Digest truncation marker",
                "The marker appended to a conversation digest that was truncated to fit its character budget.",
                null,
                "...[conversation truncated]..."));

            // --- Large-file context (global). ---
            list.Add(new PromptDefinition(
                "file.map.note", PromptKind.FileContext, PromptScope.Global,
                "Large-file map note",
                "The note prepended to a structural map of a large file, telling the model how to read more.",
                new[] { "{Path}" },
                "The file {Path} is large, so this is a structural map instead of the full contents. Use read_file with offset and limit to read a specific line range, grep to search it, or request a summary."));

            list.Add(new PromptDefinition(
                "file.summary.map", PromptKind.FileContext, PromptScope.Global,
                "File summary — map step",
                "The system prompt for summarizing one chunk of a large file during the map step.",
                null,
                "You are condensing one chunk of a large file for a coding agent. In a few dense lines, note the key declarations, types, functions, and responsibilities this chunk contains, each with its line range. Preserve line numbers exactly as shown. Output plain text only — no preamble."));

            list.Add(new PromptDefinition(
                "file.summary.reduce", PromptKind.FileContext, PromptScope.Global,
                "File summary — reduce step",
                "The system prompt for merging per-chunk notes into a final navigable file summary.",
                null,
                "You are merging per-chunk notes into one concise, navigable summary of a file for a coding agent. Produce a dense outline of what the file contains and where, keeping line-range pointers (for example \"auth handling — lines 400-508\") so the agent can read exact ranges with read_file. Output plain text only — no preamble."));

            // --- Diagnostics (global). ---
            list.Add(new PromptDefinition(
                "probe.system", PromptKind.Diagnostics, PromptScope.Global,
                "Probe system prompt",
                "The system prompt for the endpoint diagnostic probe.",
                null,
                "You are mux probe mode. Reply with a brief confirmation that includes the word OK."));

            list.Add(new PromptDefinition(
                "probe.user", PromptKind.Diagnostics, PromptScope.Global,
                "Probe user prompt",
                "The default user prompt for the endpoint diagnostic probe (overridable per run with --probe-prompt).",
                null,
                "Respond with OK and a short confirmation."));

            return list;
        }

        private static void AddToolDescriptions(List<PromptDefinition> list)
        {
            AddTool(list, "read_file", "Reads a file from the filesystem and returns its contents with line numbers (like cat -n). Supports optional offset and limit parameters to read a specific range of lines.");
            AddTool(list, "grep", "Searches files recursively for lines matching a regular expression. Returns matching lines with file path and line number. Limited to the first 100 matches.");
            AddTool(list, "glob", "Searches for files matching a glob pattern. Supports * (any characters in filename), ** (any path segments), and ? (single character).");
            AddTool(list, "list_directory", "Lists files and directories at a given path. Directories are listed first (marked [DIR]), then files (marked [FILE]), sorted alphabetically within each group.");
            AddTool(list, "file_metadata", "Returns metadata about a file or directory including size, creation time, last modified time, last access time, and attributes. Works for both files and directories.");
            AddTool(list, "write_file", "Writes content to a file. Creates parent directories if they do not exist. Preserves original line ending style for existing files; uses platform default for new files.");
            AddTool(list, "edit_file", "Performs an exact string replacement in a file. Finds old_string and replaces it with new_string. Returns an error if the old_string is not found or matches multiple locations (ambiguous).");
            AddTool(list, "multi_edit", "Performs multiple sequential string replacements in a single file. All edits are validated before any are applied. Each edit modifies the working content for subsequent edits.");
            AddTool(list, "delete_file", "Deletes a file from the filesystem. Returns an error if the file does not exist.");
            AddTool(list, "manage_directory", "Creates, deletes, or renames a directory. Use action 'create' to create a directory (including parents), 'delete' to remove an empty or non-empty directory, or 'rename' to move/rename a directory.");
            AddTool(list, "run_process", "Runs a shell command and captures its output. Current runtime: {OperatingSystem} using shell {Shell} {ShellArgsHint}. Use commands that are valid for that shell and operating system. Returns stdout, stderr, exit code, and whether the process timed out.", "{OperatingSystem}", "{Shell}", "{ShellArgsHint}");
            AddTool(list, "plan_tasks", "Establishes or replaces the plan of tasks for the current request. Call this at the start of any request that will take more than a couple of steps or spans several files, then keep the plan current with update_task. Each task has a stable id, a short title, and optional dependsOn ids of tasks that must complete first. Re-calling replaces the whole plan.");
            AddTool(list, "update_task", "Updates the status of one task in the current plan. Set status to in_progress when you start a task and completed when you finish it; keep exactly one task in_progress at a time. Use blocked (with a note) when a task cannot proceed, skipped when it is no longer needed, and failed (a note is required) when an attempt failed.");
            AddTool(list, "web_search", "Searches the public web using configured external search providers and returns structured results with URLs and snippets.");
            AddTool(list, "web_retrieve", "Retrieves a URL with a headless browser and returns rendered text, title, final URL, status, and optional HTML.");
            AddTool(list, "spawn_subagent", "Delegates a self-contained sub-task to a named subagent that runs in its own isolated conversation and returns only its final answer. Use this to hand off focused work (a review, a scoped search, a mechanical change) so your own context stays clean. Available subagents:");
        }

        private static void AddTool(List<PromptDefinition> list, string toolName, string defaultContent, params string[] placeholders)
        {
            list.Add(new PromptDefinition(
                "tool." + toolName,
                PromptKind.ToolDescription,
                PromptScope.Global,
                toolName + " description",
                "The description the " + toolName + " tool advertises to the model.",
                placeholders.Length == 0 ? null : placeholders,
                defaultContent));
        }
    }
}
