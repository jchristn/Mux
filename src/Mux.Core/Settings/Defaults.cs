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
        public static readonly string ProductVersion = "0.2.0";

        /// <summary>
        /// The default system prompt used when no custom prompt is configured.
        /// Contains placeholders for <c>{WorkingDirectory}</c> and <c>{ToolDescriptions}</c>.
        /// </summary>
        public static readonly string SystemPrompt =
            "You are mux, an AI assistant. You help the user by reading, writing, and editing data including documents, code, and other types " +
            "in their project. You have access to tools that let you interact with the filesystem, run commands, and search.\n\n" +
            "Your current working directory is: {WorkingDirectory}\n\n" +
            "Available tools:\n{ToolDescriptions}\n\n" +
            "Guidelines:\n" +
            "- Use the available tools to explore the codebase before making changes.\n" +
            "- When editing files, read them first to understand context.\n" +
            "- Prefer precise, minimal edits over rewriting entire files.\n" +
            "- Explain your reasoning when making non-trivial changes.\n" +
            "- If a task is ambiguous, ask for clarification before proceeding.";

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
