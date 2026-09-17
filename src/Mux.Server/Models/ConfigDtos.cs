namespace Mux.Server.Models
{
    using System;
    using System.Collections.Generic;
    using Mux.Core.Enums;
    using Mux.Core.Models;

    /// <summary>
    /// A configured endpoint projected for editing in the dashboard. Secret values are never sent to the
    /// client: <see cref="ApiKey"/> is write-only (blank on read) with <see cref="ApiKeySet"/> reporting
    /// whether one is stored, and header values are blanked on read (their keys are preserved). On write, a
    /// blank secret preserves the stored value.
    /// </summary>
    public sealed class EndpointDto
    {
        /// <summary>Unique endpoint name.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Adapter type (kebab-case).</summary>
        public string AdapterType { get; set; } = "ollama";

        /// <summary>Base URL.</summary>
        public string BaseUrl { get; set; } = string.Empty;

        /// <summary>Model identifier (or Azure deployment name).</summary>
        public string Model { get; set; } = string.Empty;

        /// <summary>Whether this endpoint is the default.</summary>
        public bool IsDefault { get; set; }

        /// <summary>Max output tokens.</summary>
        public int MaxTokens { get; set; }

        /// <summary>Sampling temperature.</summary>
        public double Temperature { get; set; }

        /// <summary>Model context window.</summary>
        public int ContextWindow { get; set; }

        /// <summary>HTTP timeout in milliseconds.</summary>
        public int TimeoutMs { get; set; }

        /// <summary>Auto-approve tool calls when this endpoint is active.</summary>
        public bool AutoApproveTools { get; set; }

        /// <summary>Per-endpoint agent-iteration override; null inherits the global setting.</summary>
        public int? MaxAgentIterations { get; set; }

        /// <summary>Whether the model's reasoning is displayed.</summary>
        public bool ShowThinking { get; set; }

        /// <summary>Reasoning-effort level (off/minimal/low/medium/high), or null.</summary>
        public string? ReasoningEffort { get; set; }

        /// <summary>Whether a stored API key exists (read-only indicator).</summary>
        public bool ApiKeySet { get; set; }

        /// <summary>New API key to store; blank/null preserves the existing key. Never populated on read.</summary>
        public string? ApiKey { get; set; }

        /// <summary>Cloud region (vertex/bedrock).</summary>
        public string? Region { get; set; }

        /// <summary>Google Cloud project id (vertex).</summary>
        public string? Project { get; set; }

        /// <summary>Azure OpenAI api-version.</summary>
        public string? ApiVersion { get; set; }

        /// <summary>Header entries; values are blanked on read and preserved-on-blank on write.</summary>
        public List<HeaderDto> Headers { get; set; } = new List<HeaderDto>();
    }

    /// <summary>One HTTP header entry for an endpoint. The value is blanked on read; a blank value on write preserves the stored value for that key.</summary>
    public sealed class HeaderDto
    {
        /// <summary>Header name.</summary>
        public string Key { get; set; } = string.Empty;

        /// <summary>Header value (blank on read; blank on write preserves the stored value).</summary>
        public string Value { get; set; } = string.Empty;

        /// <summary>Whether a value is stored for this header (read-only indicator).</summary>
        public bool ValueSet { get; set; }
    }

    /// <summary>An MCP server projected for editing. The auth secret is blanked on read and preserved-on-blank on write.</summary>
    public sealed class McpServerDto
    {
        /// <summary>Unique server name.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Transport: stdio or http.</summary>
        public string Transport { get; set; } = "stdio";

        /// <summary>Executable for stdio servers.</summary>
        public string? Command { get; set; }

        /// <summary>Args for stdio servers.</summary>
        public List<string> Args { get; set; } = new List<string>();

        /// <summary>Environment KEY=VALUE entries for stdio servers.</summary>
        public List<string> Env { get; set; } = new List<string>();

        /// <summary>Base URL for http servers.</summary>
        public string? Url { get; set; }

        /// <summary>Streamable HTTP MCP path.</summary>
        public string? McpPath { get; set; }

        /// <summary>Auth type: none, bearer, or apikey.</summary>
        public string AuthType { get; set; } = "none";

        /// <summary>Header name for apikey auth.</summary>
        public string? AuthHeader { get; set; }

        /// <summary>Whether an auth secret is stored (read-only indicator).</summary>
        public bool AuthSecretSet { get; set; }

        /// <summary>New auth secret; blank/null preserves the stored secret. Never populated on read.</summary>
        public string? AuthSecret { get; set; }
    }

    /// <summary>A prompt profile projected for editing. All three prompt fields are editable; a blank field
    /// inherits the built-in default for that prompt.</summary>
    public sealed class PromptProfileDto
    {
        /// <summary>Profile name.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Whether this profile is active.</summary>
        public bool IsActive { get; set; }

        /// <summary>System prompt override (blank inherits the built-in default).</summary>
        public string SystemPrompt { get; set; } = string.Empty;

        /// <summary>Tools-disabled system prompt override (blank inherits the built-in default). Null (omitted
        /// on write) preserves the stored value, so a partial update never clears it; blank clears it.</summary>
        public string? ToolsDisabledPrompt { get; set; }

        /// <summary>Compaction system prompt override (blank inherits the built-in default). Null (omitted on
        /// write) preserves the stored value, so a partial update never clears it; blank clears it.</summary>
        public string? CompactionPrompt { get; set; }
    }

    /// <summary>One operational-prompt catalog entry projected for the dashboard: its metadata, coded default,
    /// current effective value, and whether an override is in effect.</summary>
    public sealed class PromptCatalogEntryDto
    {
        /// <summary>Stable catalog key (for example <c>tool.read_file</c>).</summary>
        public string Key { get; set; } = string.Empty;

        /// <summary>Kind wire string used to group the entry (for example <c>tool-description</c>).</summary>
        public string Kind { get; set; } = string.Empty;

        /// <summary>Where an override is stored: <c>Profile</c>, <c>Global</c>, or <c>External</c>.</summary>
        public string Scope { get; set; } = string.Empty;

        /// <summary>Short human label.</summary>
        public string DisplayName { get; set; } = string.Empty;

        /// <summary>One-line explanation of the prompt's purpose.</summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>Placeholder tokens an override must preserve (for example <c>{ToolName}</c>).</summary>
        public List<string> Placeholders { get; set; } = new List<string>();

        /// <summary>The coded default content.</summary>
        public string Default { get; set; } = string.Empty;

        /// <summary>The current effective content (the override when set, otherwise the default).</summary>
        public string Effective { get; set; } = string.Empty;

        /// <summary>Whether a non-empty override is currently in effect.</summary>
        public bool Overridden { get; set; }

        /// <summary>Whether this entry can carry an operational override (true only for global-scoped entries).</summary>
        public bool Editable { get; set; }
    }

    /// <summary>A request to set or clear one operational-prompt override. A blank or null <see cref="Content"/>
    /// clears the override (restoring the default).</summary>
    public sealed class PromptOverrideDto
    {
        /// <summary>The catalog key to override.</summary>
        public string Key { get; set; } = string.Empty;

        /// <summary>The override text; blank or null clears the override.</summary>
        public string? Content { get; set; }
    }

    /// <summary>A request to build a model-context block from a file's contents. Omitted fields fall back to
    /// the server's <c>context</c> settings.</summary>
    public sealed class FileContextRequestDto
    {
        /// <summary>The file path (used for the map note and outline heuristics).</summary>
        public string Path { get; set; } = string.Empty;

        /// <summary>The full file contents.</summary>
        public string Content { get; set; } = string.Empty;

        /// <summary>Large-file mode: <c>map</c>, <c>summarize</c>, or <c>truncate</c>. Null uses the configured default.</summary>
        public string? Mode { get; set; }

        /// <summary>The endpoint whose context window sizes the inline threshold (and, for summarize, runs the
        /// summarizer). Null or unknown uses the default endpoint.</summary>
        public string? EndpointName { get; set; }

        /// <summary>Explicit inline size gate in bytes; overrides the context-window-derived threshold. Null derives it from the endpoint's context window.</summary>
        public int? InlineThresholdBytes { get; set; }

        /// <summary>Leading lines to include in a map/truncation. Null uses a default.</summary>
        public int? HeadLines { get; set; }

        /// <summary>Lines per summarizer chunk. Null uses the configured default.</summary>
        public int? SummaryChunkLines { get; set; }
    }

    /// <summary>A built file-context block.</summary>
    public sealed class FileContextResponseDto
    {
        /// <summary>The context block text to feed the model.</summary>
        public string Text { get; set; } = string.Empty;

        /// <summary>The mode actually used: <c>map</c>, <c>summarize</c>, or <c>truncate</c>.</summary>
        public string Mode { get; set; } = string.Empty;

        /// <summary>Whether the file was small enough to be inlined whole.</summary>
        public bool Inlined { get; set; }

        /// <summary>The number of outline entries emitted.</summary>
        public int OutlineEntryCount { get; set; }

        /// <summary>Whether a summary was served from the cache.</summary>
        public bool FromCache { get; set; }
    }

    /// <summary>A keybinding override projected for editing.</summary>
    public sealed class KeybindingDto
    {
        /// <summary>Command id.</summary>
        public string CommandId { get; set; } = string.Empty;

        /// <summary>Chord, or null/blank to unbind.</summary>
        public string? Chord { get; set; }
    }

    /// <summary>A rendered session export returned for client-side download.</summary>
    public sealed class SessionExportDto
    {
        /// <summary>Format extension (md or html).</summary>
        public string Format { get; set; } = "md";

        /// <summary>Suggested download filename.</summary>
        public string Filename { get; set; } = string.Empty;

        /// <summary>The rendered document content.</summary>
        public string Content { get; set; } = string.Empty;
    }

    /// <summary>A subagent definition projected for editing.</summary>
    public sealed class SubagentDto
    {
        /// <summary>Unique subagent name.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Short description surfaced to the model.</summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>System prompt for the isolated child run.</summary>
        public string SystemPrompt { get; set; } = string.Empty;

        /// <summary>Endpoint override, or null to inherit.</summary>
        public string? EndpointName { get; set; }

        /// <summary>Tool-name globs the child is limited to.</summary>
        public List<string> AllowedTools { get; set; } = new List<string>();

        /// <summary>Iteration cap, or null to inherit.</summary>
        public int? MaxIterations { get; set; }
    }

    /// <summary>An event hook projected for editing.</summary>
    public sealed class HookDto
    {
        /// <summary>Optional name.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Lifecycle event (session-start, user-prompt-submit, session-end).</summary>
        public string Event { get; set; } = "session-start";

        /// <summary>Executable to run.</summary>
        public string Command { get; set; } = string.Empty;

        /// <summary>Command arguments.</summary>
        public List<string> Args { get; set; } = new List<string>();

        /// <summary>Whether a non-zero exit vetoes a vetoable event.</summary>
        public bool Blocking { get; set; }

        /// <summary>Per-run timeout in milliseconds.</summary>
        public int TimeoutMs { get; set; } = 15000;
    }

    /// <summary>A custom slash command projected for editing.</summary>
    public sealed class CustomCommandDto
    {
        /// <summary>Slash-command name (no leading slash).</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Short description.</summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>Executable to run.</summary>
        public string Command { get; set; } = string.Empty;

        /// <summary>Command arguments.</summary>
        public List<string> Args { get; set; } = new List<string>();

        /// <summary>Per-run timeout in milliseconds.</summary>
        public int TimeoutMs { get; set; } = 30000;
    }

    /// <summary>The plugin configuration (hooks + custom commands) projected for editing.</summary>
    public sealed class PluginConfigDto
    {
        /// <summary>Event hooks.</summary>
        public List<HookDto> Hooks { get; set; } = new List<HookDto>();

        /// <summary>Custom commands.</summary>
        public List<CustomCommandDto> Commands { get; set; } = new List<CustomCommandDto>();
    }

    /// <summary>A skill projected for the dashboard (read-mostly).</summary>
    public sealed class SkillDto
    {
        /// <summary>Skill id/name.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Human title.</summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>Description.</summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>Whether the skill is enabled.</summary>
        public bool Enabled { get; set; }

        /// <summary>Whether the skill validates.</summary>
        public bool Valid { get; set; }

        /// <summary>Whether the skill is mutating.</summary>
        public bool Mutating { get; set; }

        /// <summary>Number of commands.</summary>
        public int Commands { get; set; }

        /// <summary>Validation errors, if any.</summary>
        public List<string> Errors { get; set; } = new List<string>();

        /// <summary>The SKILL.md body (populated only on the detail route).</summary>
        public string? Body { get; set; }
    }
}
