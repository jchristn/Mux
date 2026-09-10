namespace Mux.Core.Subagents
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Agent;
    using Mux.Core.Models;

    /// <summary>
    /// The production <see cref="ISubagentExecutor"/>: runs each subagent as a nested <see cref="AgentLoop"/>
    /// with a fresh, empty conversation so the child is fully isolated from the parent's history. The child
    /// inherits the parent's runtime posture (settings, approval policy, sandbox, deny-list) from a template,
    /// but takes its own system prompt, optional endpoint, optional tool allow-list, and optional iteration
    /// cap from the <see cref="SubagentDefinition"/>. Nested subagents are not offered — the child template
    /// never carries a registry or executor — so a subagent cannot spawn further subagents.
    /// </summary>
    public sealed class AgentLoopSubagentExecutor : ISubagentExecutor
    {
        #region Private-Members

        private readonly Func<AgentLoopOptions> _TemplateProvider;
        private readonly Func<string, EndpointConfig?> _EndpointResolver;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="AgentLoopSubagentExecutor"/> class.
        /// </summary>
        /// <param name="templateProvider">Returns the current parent agent-loop options to inherit runtime
        /// posture from. Read on each call so live setting changes are picked up. Must not be null.</param>
        /// <param name="endpointResolver">Resolves an endpoint by name for a subagent's endpoint override,
        /// returning null when the name is unknown (the parent endpoint is then used). Must not be null.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public AgentLoopSubagentExecutor(Func<AgentLoopOptions> templateProvider, Func<string, EndpointConfig?> endpointResolver)
        {
            _TemplateProvider = templateProvider ?? throw new ArgumentNullException(nameof(templateProvider));
            _EndpointResolver = endpointResolver ?? throw new ArgumentNullException(nameof(endpointResolver));
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<SubagentResult> ExecuteAsync(SubagentDefinition definition, string prompt, string workingDirectory, CancellationToken cancellationToken)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            if (string.IsNullOrWhiteSpace(prompt)) throw new ArgumentException("Prompt cannot be null or empty.", nameof(prompt));

            AgentLoopOptions parent = _TemplateProvider() ?? throw new InvalidOperationException("Subagent template is unavailable.");

            EndpointConfig endpoint = parent.Endpoint;
            if (!string.IsNullOrWhiteSpace(definition.EndpointName))
            {
                EndpointConfig? resolved = _EndpointResolver(definition.EndpointName!);
                if (resolved == null)
                {
                    return SubagentResult.Failed($"Subagent '{definition.Name}' references unknown endpoint '{definition.EndpointName}'.");
                }

                endpoint = resolved;
            }

            AgentLoopOptions child = new AgentLoopOptions(endpoint)
            {
                SystemPrompt = definition.SystemPrompt,
                CompactionSystemPrompt = parent.CompactionSystemPrompt,
                ApprovalPolicy = parent.ApprovalPolicy,
                AutoSafeApprovalAllowlist = parent.AutoSafeApprovalAllowlist,
                PromptUserFunc = parent.PromptUserFunc,
                WorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory) ? parent.WorkingDirectory : workingDirectory,
                MaxIterations = definition.MaxIterations ?? parent.MaxIterations,
                MuxSettings = parent.MuxSettings,
                IgnoreCertErrors = parent.IgnoreCertErrors,
                TokenEstimationRatio = parent.TokenEstimationRatio,
                ContextWindowSafetyMarginPercent = parent.ContextWindowSafetyMarginPercent,
                AutoCompactEnabled = parent.AutoCompactEnabled,
                ContextWarningThresholdPercent = parent.ContextWarningThresholdPercent,
                CompactionStrategy = parent.CompactionStrategy,
                CompactionPreserveTurns = parent.CompactionPreserveTurns,
                CommandName = parent.CommandName,
                ConfigDirectory = parent.ConfigDirectory,
                SandboxPosture = parent.SandboxPosture,
                DeniedTools = parent.DeniedTools == null ? null : new List<string>(parent.DeniedTools),
                AdditionalDirectories = parent.AdditionalDirectories == null ? null : new List<string>(parent.AdditionalDirectories),
                MaxTokenBudget = parent.MaxTokenBudget
            };

            // A subagent's allow-list, when present, tightly scopes the child's tools; otherwise it inherits
            // the parent's allow policy. TaskPlan, Subagents, and SubagentExecutor are intentionally left null
            // so the child neither tracks tasks against the parent's plan nor spawns further subagents.
            if (definition.AllowedTools.Count > 0)
            {
                child.AllowedTools = new List<string>(definition.AllowedTools);
            }
            else if (parent.AllowedTools != null)
            {
                child.AllowedTools = new List<string>(parent.AllowedTools);
            }

            StringBuilder finalText = new StringBuilder();
            string status = "unknown";
            int iterations = 0;
            string? errorMessage = null;

            using (AgentLoop loop = new AgentLoop(child))
            {
                await foreach (AgentEvent agentEvent in loop.RunAsync(prompt, cancellationToken).ConfigureAwait(false))
                {
                    switch (agentEvent)
                    {
                        case AssistantTextEvent text:
                            finalText.Append(text.Text);
                            break;
                        case ErrorEvent error:
                            errorMessage = string.IsNullOrWhiteSpace(errorMessage) ? error.Message : errorMessage;
                            break;
                        case RunCompletedEvent completed:
                            status = completed.Status;
                            iterations = completed.IterationsCompleted;
                            break;
                    }
                }
            }

            bool success = string.Equals(status, "completed", StringComparison.OrdinalIgnoreCase);
            return new SubagentResult
            {
                Success = success,
                FinalText = finalText.ToString().Trim(),
                Iterations = iterations,
                Error = success ? null : (errorMessage ?? ("Subagent run ended with status '" + status + "'."))
            };
        }

        #endregion
    }
}
