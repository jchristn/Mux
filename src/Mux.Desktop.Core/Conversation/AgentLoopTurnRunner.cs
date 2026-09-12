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
    using Mux.Core.Settings;
    using Mux.Core.Telemetry;

    /// <summary>
    /// The real <see cref="ITurnRunner"/>: resolves the selected endpoint from configuration, builds an
    /// <see cref="AgentLoopOptions"/>, and drives <see cref="AgentLoop.RunAsync"/> in-process. Tool approvals
    /// are handled through the supplied approval callback under the <see cref="ApprovalPolicyEnum.AutoSafe"/>
    /// policy (read-only tools auto-approve; mutating tools prompt). Usage is recorded when a recorder is
    /// supplied.
    /// </summary>
    /// <remarks>
    /// The system prompt uses mux's resolved persona prompt with its <c>{WorkingDirectory}</c> and
    /// <c>{ToolDescriptions}</c> placeholders substituted here (the CLI resolver normally does this). Full
    /// prompt-profile parity arrives when the shared runtime resolver is promoted into Mux.Core.
    /// </remarks>
    public sealed class AgentLoopTurnRunner : ITurnRunner
    {
        private readonly string _ConfigDirectory;
        private readonly Func<ToolCall, System.Threading.Tasks.Task<string>> _ApprovalHandler;
        private readonly IUsageRecorder? _UsageRecorder;
        private string? _EndpointName;
        private string? _SessionId;
        private string _WorkingDirectory = Directory.GetCurrentDirectory();

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

            string systemPrompt = SettingsLoader.LoadSystemPrompt(null, settings)
                .Replace("{WorkingDirectory}", _WorkingDirectory, StringComparison.Ordinal)
                .Replace("{ToolDescriptions}", string.Empty, StringComparison.Ordinal);

            // Use the active prompt profile's compaction prompt so the user's editable prompt drives the
            // automatic history compaction (blank falls back to the built-in default inside the loop).
            string compactionPrompt = SettingsLoader.GetActivePromptProfile().CompactionPrompt ?? string.Empty;

            AgentLoopOptions options = new AgentLoopOptions(endpoint)
            {
                ConversationHistory = new List<ConversationMessage>(history),
                SystemPrompt = systemPrompt,
                CompactionSystemPrompt = compactionPrompt,
                ApprovalPolicy = ApprovalPolicyEnum.AutoSafe,
                WorkingDirectory = _WorkingDirectory,
                MuxSettings = settings,
                MaxIterations = settings.MaxAgentIterations,
                ConfigDirectory = _ConfigDirectory,
                CommandName = "desktop",
                SessionId = _SessionId ?? string.Empty,
                PromptUserFunc = _ApprovalHandler,
                UsageRecorder = _UsageRecorder
            };

            using AgentLoop loop = new AgentLoop(options);
            await foreach (AgentEvent agentEvent in loop.RunAsync(prompt, token))
            {
                yield return agentEvent;
            }
        }
    }
}
