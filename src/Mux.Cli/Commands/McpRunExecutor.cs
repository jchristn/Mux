namespace Mux.Cli.Commands
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Agent;
    using Mux.Core.Enums;
    using Mux.Core.McpServer;
    using Mux.Core.Models;
    using Mux.Core.Processes;
    using Mux.Core.Settings;
    using Mux.Core.Skills;
    using Mux.Core.Tools;

    /// <summary>
    /// Runs the agent turn behind <c>mux mcp serve</c>'s <c>run</c> tool with the same runtime resolution as
    /// <c>mux print</c> (endpoint, system prompt, project instructions, governance), plus the server's skills, hooks,
    /// and background processes. Nothing is written to stdout, which carries the MCP protocol in stdio mode.
    /// </summary>
    public sealed class McpRunExecutor : IMcpRunExecutor
    {
        #region Private-Members

        private readonly string? _ConfigDirectory;
        private readonly SkillRuntime? _Skills;
        private readonly BackgroundProcessRegistry? _Processes;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="McpRunExecutor"/> class.
        /// </summary>
        /// <param name="configDirectory">The config directory to resolve settings from, or null for the active one.</param>
        /// <param name="skills">The server's skills runtime, or null.</param>
        /// <param name="processes">The server's background process registry, or null.</param>
        public McpRunExecutor(string? configDirectory, SkillRuntime? skills, BackgroundProcessRegistry? processes)
        {
            _ConfigDirectory = configDirectory;
            _Skills = skills;
            _Processes = processes;
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc/>
        public async Task<McpRunResult> RunAsync(McpRunRequest request, Func<string, Task> progress, CancellationToken cancellationToken)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (progress == null) throw new ArgumentNullException(nameof(progress));

            using IDisposable scope = SettingsLoader.PushConfigDirectoryOverride(_ConfigDirectory);
            PrintSettings settings = new PrintSettings
            {
                Endpoint = request.Endpoint,
                WorkingDirectory = request.WorkingDirectory,
                ApprovalPolicy = "deny",
                MaxTurns = request.MaxTurns
            };

            ResolvedRuntime runtime;
            try
            {
                runtime = CommandRuntimeResolver.ResolveRuntime(settings, "mcp", supportsMcp: false, allowAskApproval: false);
            }
            catch (InvalidOperationException ex)
            {
                return new McpRunResult { Status = "failed", ErrorMessage = ex.Message, Endpoint = request.Endpoint ?? string.Empty };
            }

            // Durable usage telemetry, as for mux print: each run's model calls land in the usage database and the
            // /usage views. Disposed after the run so its events are flushed.
            using Mux.Core.Telemetry.UsageTelemetry usageTelemetry = Mux.Core.Telemetry.UsageTelemetry.Create(
                runtime.MuxSettings, runtime.Metadata.ConfigDirectory, null);
            AgentLoopOptions options = new AgentLoopOptions(runtime.Endpoint)
            {
                UsageRecorder = usageTelemetry.Recorder,
                MuxSettings = runtime.MuxSettings,
                IgnoreCertErrors = runtime.MuxSettings.IgnoreCertErrors,
                SystemPrompt = runtime.SystemPrompt,
                CompactionSystemPrompt = runtime.CompactionSystemPrompt,
                ApprovalPolicy = request.ApprovalPolicy,
                WorkingDirectory = runtime.WorkingDirectory,
                MaxTokenBudget = runtime.MuxSettings.MaxTokenBudget,
                SandboxPosture = runtime.SandboxPosture,
                AllowedTools = runtime.AllowedTools,
                DeniedTools = runtime.DeniedTools,
                MaxIterations = request.MaxTurns ?? runtime.MaxAgentIterations,
                TokenEstimationRatio = runtime.MuxSettings.TokenEstimationRatio,
                ContextWindowSafetyMarginPercent = runtime.MuxSettings.ContextWindowSafetyMarginPercent,
                AutoCompactEnabled = runtime.MuxSettings.AutoCompactEnabled,
                ContextWarningThresholdPercent = runtime.MuxSettings.ContextWarningThresholdPercent,
                CompactionStrategy = runtime.MuxSettings.CompactionStrategy,
                CompactionPreserveTurns = runtime.MuxSettings.CompactionPreserveTurns,
                CommandName = "mcp",
                ConfigDirectory = runtime.Metadata.ConfigDirectory,
                BuiltInToolCount = runtime.Capabilities.BuiltInToolCount,
                EffectiveToolCount = runtime.Capabilities.EffectiveToolCount,
                TaskPlan = runtime.MuxSettings.TaskPlanningEnabled ? new Mux.Core.Tasks.TaskPlan() : null,
                Hooks = Mux.Core.Plugins.PluginRegistry.LoadHooksOrNull()
            };

            if (runtime.Capabilities.ToolsEnabled)
            {
                options.ExternalToolProviders = new List<IExternalToolProvider>();
                if (_Skills != null)
                {
                    options.ExternalToolProviders.Add(_Skills);
                    options.SystemPrompt += _Skills.BuildPromptSection(runtime.WorkingDirectory);
                }

                if (_Processes != null)
                {
                    options.ExternalToolProviders.Add(new BackgroundProcessToolProvider(_Processes));
                }
            }

            McpRunResult result = new McpRunResult { Endpoint = runtime.Endpoint.Name, Model = runtime.Endpoint.Model, Status = "failed" };
            Stopwatch watch = Stopwatch.StartNew();
            RunCompletedEvent? completed = null;
            int steps = 0;
            using (AgentLoop loop = new AgentLoop(options))
            {
                await foreach (AgentEvent agentEvent in loop.RunAsync(request.Prompt, cancellationToken).WithCancellation(cancellationToken).ConfigureAwait(false))
                {
                    switch (agentEvent)
                    {
                        case ToolCallCompletedEvent toolDone:
                            await progress("tool " + toolDone.ToolName + (toolDone.Result != null && !toolDone.Result.Success ? " failed" : " done")).ConfigureAwait(false);
                            break;
                        case HeartbeatEvent:
                            steps++;
                            await progress("step " + steps + " finished").ConfigureAwait(false);
                            break;
                        case ErrorEvent error:
                            result.ErrorMessage ??= error.Message;
                            break;
                        case RunCompletedEvent done:
                            completed = done;
                            break;
                    }
                }

                result.Answer = LastAssistantText(loop.FinalConversation);
            }

            watch.Stop();
            result.DurationMs = watch.ElapsedMilliseconds;
            if (completed != null)
            {
                result.Status = completed.Status;
                result.Iterations = completed.IterationsCompleted;
                result.ToolCalls = completed.ToolCallCount;
                result.Errors = completed.ErrorCount;
                result.InputTokens = completed.InputTokens;
                result.OutputTokens = completed.OutputTokens;
            }

            return result;
        }

        #endregion

        #region Private-Methods

        private static string LastAssistantText(IReadOnlyList<ConversationMessage> conversation)
        {
            for (int i = conversation.Count - 1; i >= 0; i--)
            {
                ConversationMessage message = conversation[i];
                if (message.Role == RoleEnum.Assistant && !string.IsNullOrWhiteSpace(message.Content))
                {
                    return message.Content!;
                }
            }

            return string.Empty;
        }

        #endregion
    }
}
