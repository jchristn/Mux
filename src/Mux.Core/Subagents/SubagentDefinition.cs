namespace Mux.Core.Subagents
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    /// <summary>
    /// A named, user-authored subagent: a reusable persona the primary agent can delegate a scoped
    /// sub-task to. Each definition carries its own system prompt, an optional endpoint override, an
    /// optional tool allow-list, and an optional iteration cap. A subagent runs in an isolated
    /// conversation (it never sees or mutates the parent's history), so delegating a self-contained
    /// unit of work keeps the parent's context clean and lets a cheaper or more specialized model do
    /// the work. Definitions are loaded from <c>~/.mux/subagents.json</c>.
    /// </summary>
    public sealed class SubagentDefinition
    {
        #region Private-Members

        private string _Name = string.Empty;
        private string _Description = string.Empty;
        private string _SystemPrompt = string.Empty;
        private List<string> _AllowedTools = new List<string>();

        #endregion

        #region Public-Members

        /// <summary>
        /// The stable, unique name the model uses to select this subagent (for example
        /// <c>"reviewer"</c>). Never null; an empty name fails validation.
        /// </summary>
        [JsonPropertyName("name")]
        public string Name
        {
            get => _Name;
            set => _Name = value ?? string.Empty;
        }

        /// <summary>
        /// A short description of what this subagent is for, surfaced to the model in the
        /// <c>spawn_subagent</c> tool schema so it can pick the right one. Never null.
        /// </summary>
        [JsonPropertyName("description")]
        public string Description
        {
            get => _Description;
            set => _Description = value ?? string.Empty;
        }

        /// <summary>
        /// The system prompt that defines the subagent's behavior. It fully replaces the parent's system
        /// prompt for the isolated child run. Never null; an empty prompt fails validation.
        /// </summary>
        [JsonPropertyName("systemPrompt")]
        public string SystemPrompt
        {
            get => _SystemPrompt;
            set => _SystemPrompt = value ?? string.Empty;
        }

        /// <summary>
        /// The name of the endpoint the subagent should run under, or null to inherit the parent's
        /// endpoint. Lets a subagent use a cheaper or more specialized model than the primary agent.
        /// </summary>
        [JsonPropertyName("endpointName")]
        public string? EndpointName { get; set; }

        /// <summary>
        /// Optional tool-name glob patterns the subagent is limited to. When non-empty, the child run
        /// advertises and permits only matching tools; empty (the default) inherits the parent's tool
        /// policy. A tight allow-list is the primary way to constrain what a delegated task can do.
        /// </summary>
        [JsonPropertyName("allowedTools")]
        public List<string> AllowedTools
        {
            get => _AllowedTools;
            set => _AllowedTools = value ?? new List<string>();
        }

        /// <summary>
        /// An optional cap on the subagent's agent-loop iterations, or null to inherit the parent's cap.
        /// A small cap keeps a delegated task bounded.
        /// </summary>
        [JsonPropertyName("maxIterations")]
        public int? MaxIterations { get; set; }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Creates a deep copy so callers never hold a reference into shared registry state.
        /// </summary>
        /// <returns>An independent copy of this definition.</returns>
        public SubagentDefinition Clone()
        {
            return new SubagentDefinition
            {
                Name = _Name,
                Description = _Description,
                SystemPrompt = _SystemPrompt,
                EndpointName = EndpointName,
                AllowedTools = new List<string>(_AllowedTools),
                MaxIterations = MaxIterations
            };
        }

        #endregion
    }
}
