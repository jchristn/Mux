namespace Mux.Desktop.Conversation
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Runtime.CompilerServices;
    using System.Threading;
    using Mux.Core.Agent;
    using Mux.Core.Enums;
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

            AgentLoopOptions options = new AgentLoopOptions(endpoint)
            {
                ConversationHistory = new List<ConversationMessage>(history),
                SystemPrompt = systemPrompt,
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
