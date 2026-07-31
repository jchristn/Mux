namespace Mux.Core.Settings
{
    using System.Collections.Generic;
    using Mux.Core.Enums;
    using Mux.Core.Models;

    /// <summary>
    /// Provides default values and preset configurations for the mux system.
    /// </summary>
    public static class Defaults
    {
        #region Public-Members

        /// <summary>
        /// The product name.
        /// </summary>
        public static readonly string ProductName = "mux";

        /// <summary>
        /// The product version.
        /// </summary>
        public static readonly string ProductVersion = "0.4.0";

        /// <summary>
        /// The default system prompt used when no custom prompt is configured.
        /// Contains placeholders for <c>{WorkingDirectory}</c> and <c>{ToolDescriptions}</c>.
        /// </summary>
        public static readonly string SystemPrompt =
            "You are mux, an AI assistant. You help the user by reading, writing, and editing data including documents, code, and other types " +
            "in their project. You have access to tools that let you interact with the filesystem, run commands, and search.\n\n" +
            "Your current working directory is: {WorkingDirectory}\n\n" +
            "Available tools:\n{ToolDescriptions}\n\n" +
            "Tool use guidance:\n" +
            "- For filesystem search, use glob to find files by path/name and grep to search file contents.\n" +
            "- After grep or glob finds candidates, use read_file with offset/limit to inspect nearby context.\n" +
            "- Use run_process when shell tools are better suited, such as rg, git grep, find, dir, or test commands.\n" +
            "- Use web_retrieve for a known URL.\n" +
            "- Use web_search only when it is listed as an available tool and the task requires public web discovery.\n" +
            "- For edits, read the relevant file first, then prefer edit_file or multi_edit for targeted changes; use write_file for new files or full rewrites.\n" +
            "- After making changes, verify with read_file, grep, tests, or run_process when practical.\n" +
            "- Treat delete_file and directory deletion as destructive; use them only when the user clearly asked or the task requires it.\n\n" +
            "Guidelines:\n" +
            "- Use the available tools to explore the codebase before making changes.\n" +
            "- The listed tools are executable capabilities. When the user asks you to retrieve, fetch, read, scrape, browse, or display the contents of a URL, call web_retrieve with that URL if it is available.\n" +
            "- Use web_search, when available, only to discover candidate web results; use web_retrieve to fetch the actual contents of a selected URL. If the user asks web_search to retrieve a URL, use web_retrieve instead.\n" +
            "- Do not claim that you cannot access web content when web_retrieve is available; call the tool and summarize or display the returned data.\n" +
            "- When editing files, read them first to understand context.\n" +
            "- Prefer precise, minimal edits over rewriting entire files.\n" +
            "- Explain your reasoning when making non-trivial changes.\n" +
            "- If a task is ambiguous, ask for clarification before proceeding.";

        /// <summary>
        /// The system prompt used instead of <see cref="SystemPrompt"/> when the active endpoint does not
        /// support tools. Contains the <c>{WorkingDirectory}</c> placeholder (no tool listing).
        /// </summary>
        public static readonly string ToolsDisabledSystemPrompt =
            "You are mux, an AI assistant. You help the user by reading, writing, and editing data including documents, code, and other types " +
            "in their project.\n\n" +
            "Your current working directory is: {WorkingDirectory}\n\n" +
            "Guidelines:\n" +
            "- Explain your reasoning when making non-trivial suggestions.\n" +
            "- If a task is ambiguous, ask for clarification before proceeding.";

        /// <summary>
        /// The system prompt for the automatic history-compaction sidecar call. No placeholders.
        /// </summary>
        public static readonly string CompactionSystemPrompt =
            "You compact older conversation history for a coding agent. " +
            "Preserve goals, constraints, important files, decisions, errors, and unresolved work. " +
            "Return plain text only, concise but information-dense, suitable to carry forward as session memory.";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Returns the default <see cref="BackendQuirks"/> for the Ollama adapter.
        /// </summary>
        /// <returns>A <see cref="BackendQuirks"/> configured for Ollama.</returns>
        public static BackendQuirks OllamaDefaults()
        {
            return new BackendQuirks
            {
                AssembleToolCallDeltas = true,
                SupportsParallelToolCalls = false,
                StripRequestFields = new List<string> { "parallel_tool_calls", "stream_options" }
            };
        }

        /// <summary>
        /// Returns the default <see cref="BackendQuirks"/> for the OpenAI adapter.
        /// </summary>
        /// <returns>A <see cref="BackendQuirks"/> configured for OpenAI.</returns>
        public static BackendQuirks OpenAiDefaults()
        {
            return new BackendQuirks
            {
                SupportsParallelToolCalls = true
            };
        }

        /// <summary>
        /// Returns the default <see cref="BackendQuirks"/> for a generic OpenAI-compatible adapter.
        /// </summary>
        /// <returns>A <see cref="BackendQuirks"/> with all default values.</returns>
        public static BackendQuirks GenericOpenAiDefaults()
        {
            return new BackendQuirks();
        }

        /// <summary>
        /// Returns the default <see cref="BackendQuirks"/> for the specified adapter type.
        /// </summary>
        /// <param name="adapterType">The adapter type to retrieve defaults for.</param>
        /// <returns>A <see cref="BackendQuirks"/> appropriate for the given adapter type.</returns>
        public static BackendQuirks QuirksForAdapter(AdapterTypeEnum adapterType)
        {
            switch (adapterType)
            {
                case AdapterTypeEnum.Ollama:
                    return OllamaDefaults();
                case AdapterTypeEnum.OpenAi:
                    return OpenAiDefaults();
                case AdapterTypeEnum.Vllm:
                case AdapterTypeEnum.OpenAiCompatible:
                    return GenericOpenAiDefaults();
                default:
                    return GenericOpenAiDefaults();
            }
        }

        #endregion
    }
}
