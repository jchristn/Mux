namespace Mux.Desktop.Conversation
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Runtime.CompilerServices;
    using System.Text;
    using System.Threading;
    using Mux.Core.Agent;
    using Mux.Core.Enums;
    using Mux.Core.Llm;
    using Mux.Core.Models;
    using Mux.Core.Prompting;
    using Mux.Core.Settings;
    using Mux.Core.Skills;
    using Mux.Core.Telemetry;
    using Mux.Core.Tools;

    /// <summary>
    /// The real <see cref="ITurnRunner"/>: resolves the selected endpoint from configuration, builds an
    /// <see cref="AgentLoopOptions"/>, and drives <see cref="AgentLoop.RunAsync"/> in-process. Tool approvals
    /// are handled through the supplied approval callback under the <see cref="ApprovalPolicyEnum.AutoSafe"/>
    /// policy (read-only tools auto-approve; mutating tools prompt). Usage is recorded when a recorder is
    /// supplied.
    /// </summary>
    /// <remarks>
    /// The system + compaction prompts are resolved through the shared
    /// <see cref="Mux.Core.Prompting.SystemPromptResolver"/>, so the desktop and the TUI produce an identical
    /// prompt (tools-disabled variant, real <c>{ToolDescriptions}</c>, and <c>{TaskPlanningGuidance}</c>).
    /// </remarks>
    public sealed class AgentLoopTurnRunner : ITurnRunner
    {
        private readonly string _ConfigDirectory;
        private readonly Func<ToolCall, System.Threading.Tasks.Task<string>> _ApprovalHandler;
        private readonly IUsageRecorder? _UsageRecorder;
        private string? _EndpointName;
        private string? _SessionId;
        private string _WorkingDirectory = Directory.GetCurrentDirectory();
        private McpRuntime? _Mcp;
        private SkillRuntime? _Skills;

        /// <summary>
        /// Instantiate the runner.
        /// </summary>
        /// <param name="configDirectory">The active mux config directory. Required.</param>
        /// <param name="approvalHandler">Callback returning "y"/"n"/"always" for a proposed tool call. Required.</param>
        /// <param name="usageRecorder">Optional usage recorder; null disables recording.</param>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
        public AgentLoopTurnRunner(
            string configDirectory,
            Func<ToolCall, System.Threading.Tasks.Task<string>> approvalHandler,
            IUsageRecorder? usageRecorder)
        {
            ArgumentNullException.ThrowIfNull(configDirectory);
            ArgumentNullException.ThrowIfNull(approvalHandler);

            _ConfigDirectory = configDirectory;
            _ApprovalHandler = approvalHandler;
            _UsageRecorder = usageRecorder;
        }

        /// <summary>The name of the endpoint to run against; null selects the default endpoint.</summary>
        public string? EndpointName
        {
            get => _EndpointName;
            set => _EndpointName = value;
        }

        /// <summary>
        /// The live MCP runtime whose currently-connected tools are exposed to the model, or null when MCP is
        /// not wired. Set once by the host; read per turn so newly connected servers apply to the next turn.
        /// </summary>
        public McpRuntime? Mcp
        {
            get => _Mcp;
            set => _Mcp = value;
        }

        /// <summary>
        /// The live skills runtime registered as an external tool provider, or null when skills are off. Set
        /// once by the host; read per turn.
        /// </summary>
        public SkillRuntime? Skills
        {
            get => _Skills;
            set => _Skills = value;
        }

        /// <summary>The session/thread id to tag usage telemetry with, so per-conversation stats can be queried.</summary>
        public string? SessionId
        {
            get => _SessionId;
            set => _SessionId = value;
        }

        /// <summary>The working directory tools execute in. Defaults to the process working directory.</summary>
        public string WorkingDirectory
        {
            get => _WorkingDirectory;
            set => _WorkingDirectory = string.IsNullOrWhiteSpace(value) ? Directory.GetCurrentDirectory() : value;
        }

        /// <summary>
        /// Ask the current endpoint's model to summarize the conversation into a short title (a single,
        /// non-streaming, tool-free completion). Returns a cleaned title, or null on any failure so callers
        /// can fall back to a heuristic title.
        /// </summary>
        /// <param name="history">The conversation so far.</param>
        /// <param name="token">A token to cancel the request.</param>
        /// <returns>A short title, or null if one could not be generated.</returns>
        public async System.Threading.Tasks.Task<string?> GenerateTitleAsync(IReadOnlyList<ConversationMessage> history, CancellationToken token)
        {
            if (history == null || history.Count == 0)
            {
                return null;
            }

            try
            {
                MuxSettings settings = SettingsLoader.LoadSettings();
                List<EndpointConfig> endpoints = SettingsLoader.LoadEndpoints();
                EndpointConfig endpoint = SettingsLoader.ResolveEndpoint(endpoints, _EndpointName, null, null, null, null, null);

                List<ConversationMessage> messages = new List<ConversationMessage>
                {
                    new ConversationMessage
                    {
                        Role = RoleEnum.System,
                        Content = "You write concise, descriptive titles for chat conversations. Reply with ONLY the title: 3 to 7 words, Title Case, no surrounding quotes and no trailing punctuation."
                    },
                    new ConversationMessage
                    {
                        Role = RoleEnum.User,
                        Content = "Write a short title that summarizes this conversation:\n\n" + BuildTitleTranscript(history)
                    }
                };

                using LlmClient client = new LlmClient(endpoint, settings.IgnoreCertErrors);
                ConversationMessage response = await client.SendAsync(messages, new List<ToolDefinition>(), token).ConfigureAwait(false);
                return CleanTitle(response.Content);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static string BuildTitleTranscript(IReadOnlyList<ConversationMessage> history)
        {
            StringBuilder builder = new StringBuilder();
            foreach (ConversationMessage message in history)
            {
                if (string.IsNullOrWhiteSpace(message.Content))
                {
                    continue;
                }

                string role = message.Role == RoleEnum.User ? "User" : message.Role == RoleEnum.Assistant ? "Assistant" : "System";
                builder.Append(role).Append(": ").Append(message.Content!.Trim()).Append('\n');
                if (builder.Length > 4000)
                {
                    break;
                }
            }

            return builder.ToString();
        }

        private static string? CleanTitle(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return null;
            }

            string title = raw.Trim();
            int newline = title.IndexOfAny(new[] { '\n', '\r' });
            if (newline >= 0)
            {
                title = title.Substring(0, newline).Trim();
            }

            title = title.Trim('"', '\'', '`', ' ').TrimEnd('.', ' ').Trim();
            if (title.Length > 80)
            {
                title = title.Substring(0, 80).Trim();
            }

            return title.Length == 0 ? null : title;
        }

        /// <summary>
        /// Resolve the endpoint, build the loop options, and stream the turn's events.
        /// </summary>
        /// <param name="prompt">The user prompt for this turn.</param>
        /// <param name="history">The prior conversation history (turns 1..N-1).</param>
        /// <param name="token">A token to cancel the turn.</param>
        /// <returns>The streamed agent events.</returns>
        public async IAsyncEnumerable<AgentEvent> RunAsync(
            string prompt,
            IReadOnlyList<ConversationMessage> history,
            [EnumeratorCancellation] CancellationToken token)
        {
            MuxSettings settings = SettingsLoader.LoadSettings();
            List<EndpointConfig> endpoints = SettingsLoader.LoadEndpoints();
            EndpointConfig endpoint = SettingsLoader.ResolveEndpoint(endpoints, _EndpointName, null, null, null, null, null);

            // Resolve the system + compaction prompts through the shared Core resolver so the desktop and the
            // TUI produce an identical prompt: the tools-disabled variant when the endpoint has no tool
            // support, real {ToolDescriptions}, and the {TaskPlanningGuidance} block when enabled.
            bool toolsEnabled = endpoint.Quirks?.SupportsTools ?? true;
            List<ToolDefinition> builtInTools = new BuiltInToolRegistry(settings).GetToolDefinitions();
            ResolvedSystemPrompt resolved = SystemPromptResolver.Resolve(
                SettingsLoader.LoadSystemPrompt(null, settings),
                SettingsLoader.GetActivePromptProfile(),
                toolsEnabled,
                builtInTools,
                _WorkingDirectory,
                settings.TaskPlanningEnabled,
                null);

            AgentLoopOptions options = new AgentLoopOptions(endpoint)
            {
                ConversationHistory = new List<ConversationMessage>(history),
                SystemPrompt = resolved.SystemPrompt,
                CompactionSystemPrompt = resolved.CompactionSystemPrompt,
                ApprovalPolicy = ApprovalPolicyEnum.AutoSafe,
                WorkingDirectory = _WorkingDirectory,
                MuxSettings = settings,
                MaxIterations = settings.GetEffectiveMaxAgentIterations(endpoint),
                ConfigDirectory = _ConfigDirectory,
                CommandName = "desktop",
                SessionId = _SessionId ?? string.Empty,
                PromptUserFunc = _ApprovalHandler,
                UsageRecorder = _UsageRecorder
            };

            // Compose the live MCP tools + skills runtime onto the options so the desktop model can call them,
            // matching the TUI. When neither is wired this reduces to the base prompt with no external tools.
            if (toolsEnabled)
            {
                IReadOnlyList<ToolDefinition> mcpTools = _Mcp?.CurrentTools ?? new List<ToolDefinition>();
                System.Func<string, System.Text.Json.JsonElement, string, CancellationToken, System.Threading.Tasks.Task<ToolResult>>? executor =
                    _Mcp != null ? _Mcp.ExecuteToolAsync : null;
                ExternalToolsBinder.Apply(
                    options,
                    resolved.SystemPrompt,
                    resolved.CompactionSystemPrompt,
                    mcpTools,
                    executor,
                    _Skills,
                    builtInTools.Count);
            }

            using AgentLoop loop = new AgentLoop(options);
            await foreach (AgentEvent agentEvent in loop.RunAsync(prompt, token))
            {
                yield return agentEvent;
            }
        }
    }
}
