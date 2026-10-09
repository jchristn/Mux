namespace Mux.Core.Agent
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Runtime.CompilerServices;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Approvals;
    using Mux.Core.Enums;
    using Mux.Core.Jobs;
    using Mux.Core.Llm;
    using Mux.Core.Models;
    using Mux.Core.Observability;
    using Mux.Core.Prompting;
    using Mux.Core.Settings;
    using Mux.Core.Telemetry;
    using Mux.Core.Tools;

    /// <summary>
    /// Orchestrates the agent loop: sends messages to the LLM, processes tool calls,
    /// and yields events to the caller as an async stream.
    /// </summary>
    public class AgentLoop : IDisposable
    {
        #region Private-Members

        private const int InRunCompactionTargetPercent = 60;
        private const int InRunProtectedTailMessageCount = 6;
        private static readonly string SyntheticSummaryPrefix = PromptCatalog.SyntheticSummaryPrefix;

        private AgentLoopOptions _Options;
        private LlmClient _LlmClient;
        private BuiltInToolRegistry _ToolRegistry;
        private IApprovalRouter _ApprovalRouter;
        private readonly Mux.Core.Plugins.HookRunner _HookRunner = new Mux.Core.Plugins.HookRunner();
        private const int MaxStopHookReentries = 3;
        private IUsageRecorder _UsageRecorder;
        private bool _Disposed = false;
        private List<ConversationMessage> _FinalConversation = new List<ConversationMessage>();
        private readonly Mux.Core.Interaction.InteractionToolProvider _Interaction;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="AgentLoop"/> class.
        /// </summary>
        /// <param name="options">The configuration options for this agent loop.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="options"/> is null.</exception>
        public AgentLoop(AgentLoopOptions options)
        {
            _Options = options ?? throw new ArgumentNullException(nameof(options));
            _LlmClient = new LlmClient(options.Endpoint, options.IgnoreCertErrors);
            _LlmClient.OnRetry = options.OnRetry;
            _ToolRegistry = new BuiltInToolRegistry(options.MuxSettings, options.TaskPlan, options.Subagents, options.SubagentExecutor, options.Endpoint?.ContextWindow ?? 0);
            _ApprovalRouter = new ApprovalRouter(options.ApprovalPolicy, options.AutoSafeApprovalAllowlist);
            _UsageRecorder = options.UsageRecorder ?? NullUsageRecorder.Instance;

            // Plan mode: read-only exploration that ends with exit_plan. The options object is per run (jobs and
            // surfaces build a fresh one), so forcing the posture and adding the guidance here affects only this run.
            if (options.PlanMode)
            {
                options.SandboxPosture = SandboxPostureEnum.ReadOnly;
                if ((options.SystemPrompt ?? string.Empty).IndexOf("# Plan mode", StringComparison.Ordinal) < 0)
                {
                    options.SystemPrompt = (options.SystemPrompt ?? string.Empty) + Mux.Core.Interaction.InteractionToolProvider.PlanModeGuidance;
                }
            }

            _Interaction = new Mux.Core.Interaction.InteractionToolProvider(options.AskUserFunc, options.ReviewPlanFunc, options.PlanMode);
        }

        #endregion

        #region Public-Members

        /// <summary>
        /// The full conversation (system, user, assistant, and tool messages) as it stood when the most
        /// recent <see cref="RunAsync"/> enumeration completed. Empty until a run finishes. Callers that
        /// persist a resumable session read this after enumerating the event stream so the saved history
        /// is exactly what the loop produced, rather than a reconstruction from events.
        /// </summary>
        public IReadOnlyList<ConversationMessage> FinalConversation
        {
            get => _FinalConversation;
        }

        /// <summary>
        /// The run's <c>ask_user</c> and <c>exit_plan</c> tools, exposing the last plan presented and its review.
        /// </summary>
        public Mux.Core.Interaction.InteractionToolProvider Interaction
        {
            get => _Interaction;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Runs the agent loop for the given user prompt, yielding events as they occur.
        /// </summary>
        /// <param name="prompt">The user prompt to send to the LLM.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>An async sequence of <see cref="AgentEvent"/> instances representing the agent's activity.</returns>
        public async IAsyncEnumerable<AgentEvent> RunAsync(
            string prompt,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(prompt))
                throw new ArgumentException("Prompt cannot be null or empty.", nameof(prompt));

            // Telemetry wrapper: one "agent run" span per run plus the run-level metrics. The span nests under
            // whatever is current (a Watson request span, a job stage span). Async-iterator segments do not
            // carry AsyncLocal changes across yields, so the span is re-established as Activity.Current before
            // every inner MoveNextAsync; LLM, tool, and stage spans opened by the inner loop then nest under it.
            string runId = Guid.NewGuid().ToString("N");
            string callKind = _Options.UsageCallKind.ToString().ToLowerInvariant();
            Activity? runActivity = MuxTelemetry.StartActivity("agent run");
            MuxTelemetry.SetTag(runActivity, MuxTelemetryNames.AttrRunId, runId);
            MuxTelemetry.SetTag(runActivity, MuxTelemetryNames.AttrSessionId, _Options.SessionId);
            MuxTelemetry.SetTag(runActivity, MuxTelemetryNames.AttrJobId, _Options.JobId);
            MuxTelemetry.SetTag(runActivity, MuxTelemetryNames.AttrEndpointName, _Options.Endpoint.Name);
            MuxTelemetry.SetTag(runActivity, MuxTelemetryNames.LabelProvider, ProviderName(_Options.Endpoint));
            MuxTelemetry.SetTag(runActivity, MuxTelemetryNames.AttrRequestModel, _Options.Endpoint.Model);
            MuxTelemetry.SetTag(runActivity, MuxTelemetryNames.LabelCallKind, callKind);
            MuxTelemetry.AgentRunStarted();

            long startTimestamp = Stopwatch.GetTimestamp();
            string outcome = MuxTelemetryNames.OutcomeAbandoned;
            int iterations = 0;
            IAsyncEnumerator<AgentEvent> inner = RunCoreAsync(prompt, runId, cancellationToken).GetAsyncEnumerator(cancellationToken);

            try
            {
                while (true)
                {
                    if (runActivity != null) Activity.Current = runActivity;

                    bool moved;
                    try
                    {
                        moved = await inner.MoveNextAsync().ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        outcome = MuxTelemetryNames.OutcomeCancelled;
                        MuxTelemetry.SetError(runActivity, MuxTelemetryNames.OutcomeCancelled, "run cancelled");
                        throw;
                    }
                    catch (Exception ex)
                    {
                        outcome = MuxTelemetryNames.OutcomeFailed;
                        MuxTelemetry.RecordException(runActivity, ex);
                        throw;
                    }

                    if (!moved) break;

                    AgentEvent agentEvent = inner.Current;
                    if (agentEvent is RunCompletedEvent completed)
                    {
                        outcome = completed.Status;
                        iterations = completed.IterationsCompleted;
                        MuxTelemetry.SetTag(runActivity, MuxTelemetryNames.AttrIterations, completed.IterationsCompleted);
                        MuxTelemetry.SetTag(runActivity, MuxTelemetryNames.AttrToolCallCount, completed.ToolCallCount);
                        MuxTelemetry.SetTag(runActivity, MuxTelemetryNames.AttrInputTokens, completed.InputTokens);
                        MuxTelemetry.SetTag(runActivity, MuxTelemetryNames.AttrOutputTokens, completed.OutputTokens);
                    }
                    else if (agentEvent is ErrorEvent errorEvent)
                    {
                        MuxTelemetry.RecordAgentError(errorEvent.Code);
                        runActivity?.AddEvent(new ActivityEvent(
                            "mux.agent.error",
                            DateTimeOffset.UtcNow,
                            new ActivityTagsCollection { { MuxTelemetryNames.LabelErrorType, MuxTelemetry.SanitizeCode(errorEvent.Code) } }));
                    }
                    else if (agentEvent is ContextCompactedEvent compactedEvent)
                    {
                        MuxTelemetry.RecordCompaction(compactedEvent.Strategy);
                    }

                    yield return agentEvent;
                }
            }
            finally
            {
                try { await inner.DisposeAsync().ConfigureAwait(false); } catch (Exception) { }

                if (string.Equals(outcome, MuxTelemetryNames.OutcomeAbandoned, StringComparison.Ordinal)
                    && cancellationToken.IsCancellationRequested)
                {
                    outcome = MuxTelemetryNames.OutcomeCancelled;
                }

                MuxTelemetry.SetTag(runActivity, MuxTelemetryNames.LabelOutcome, outcome);
                if (string.Equals(outcome, "completed", StringComparison.Ordinal)) MuxTelemetry.SetOk(runActivity);
                else if (runActivity != null && runActivity.Status == ActivityStatusCode.Unset) MuxTelemetry.SetError(runActivity, outcome, "run finished with outcome " + outcome);

                MuxTelemetry.RecordAgentRun(outcome, callKind, MuxTelemetry.SecondsSince(startTimestamp), iterations);
                MuxTelemetry.Stop(runActivity);
            }
        }

        private async IAsyncEnumerable<AgentEvent> RunCoreAsync(
            string prompt,
            string runId,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            int iterationCount = 0;
            int toolCallCount = 0;
            int errorCount = 0;
            int stopHookReentries = 0;
            int assistantTextChars = 0;
            bool maxIterationsReached = false;
            bool budgetExceeded = false;
            int compactionCount = 0;

            // 1. Build conversation
            List<ConversationMessage> conversation = BuildConversation(prompt);

            // 2. Merge tool definitions
            List<ToolDefinition> allTools = MergeToolDefinitions();
            ContextBudgetSnapshot initialSnapshot = GetContextBudgetSnapshot(conversation, allTools);

            yield return new RunStartedEvent
            {
                RunId = runId,
                SessionId = _Options.SessionId,
                EndpointName = _Options.Endpoint.Name,
                AdapterType = _Options.Endpoint.AdapterType.ToString(),
                BaseUrl = _Options.Endpoint.BaseUrl,
                Model = _Options.Endpoint.Model,
                ApprovalPolicy = _Options.ApprovalPolicy.ToString(),
                WorkingDirectory = _Options.WorkingDirectory,
                MaxIterations = _Options.MaxIterations,
                ToolsEnabled = _Options.Endpoint.Quirks?.SupportsTools ?? true,
                CommandName = _Options.CommandName,
                ConfigDirectory = _Options.ConfigDirectory,
                EndpointSelectionSource = _Options.EndpointSelectionSource,
                CliOverridesApplied = new List<string>(_Options.CliOverridesApplied),
                McpSupported = _Options.McpSupported,
                McpConfigured = _Options.McpConfigured,
                McpServerCount = _Options.McpServerCount,
                BuiltInToolCount = _Options.BuiltInToolCount,
                EffectiveToolCount = _Options.EffectiveToolCount,
                ContextWindow = initialSnapshot.ContextWindowSize,
                ReservedOutputTokens = initialSnapshot.ReservedOutputTokens,
                UsableInputLimit = initialSnapshot.UsableInputLimit,
                WarningThresholdTokens = initialSnapshot.WarningThresholdTokens,
                TokenEstimationRatio = _Options.TokenEstimationRatio,
                CompactionStrategy = _Options.CompactionStrategy,
                IgnoreCertErrors = _Options.IgnoreCertErrors,
                SandboxPosture = ToolGovernance.PostureName(_Options.SandboxPosture),
                ReasoningEffort = _Options.Endpoint.ReasoningEffort,
                ShowThinking = _Options.Endpoint.ShowThinking
            };

            // Track the most recent per-call metrics so each iteration records only its own fresh call.
            LlmCallMetrics? previousCall = _LlmClient.LastCall;

            // 3. Enter loop
            for (int step = 0; step < _Options.MaxIterations; step++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                iterationCount = step + 1;

                // Enforce the optional token budget before spending another model call. The estimate is
                // mux's own working-context estimate (backend-agnostic), not a provider billing figure.
                if (_Options.MaxTokenBudget.HasValue)
                {
                    ContextBudgetSnapshot budgetSnapshot = GetContextBudgetSnapshot(conversation, allTools);
                    if (budgetSnapshot.UsedTokens > _Options.MaxTokenBudget.Value)
                    {
                        budgetExceeded = true;
                        errorCount++;
                        yield return new ErrorEvent
                        {
                            Code = "budget_exceeded",
                            Message = $"Estimated context tokens ({budgetSnapshot.UsedTokens}) exceeded the configured budget of {_Options.MaxTokenBudget.Value}. Stopping before the next model call."
                        };
                        break;
                    }
                }

                bool shouldAbortBeforeModelCall;
                List<AgentEvent> contextEvents = PrepareConversationForModelCall(
                    ref conversation,
                    allTools,
                    step == 0 ? "preflight" : "iteration",
                    cancellationToken,
                    out shouldAbortBeforeModelCall,
                    out int compactionDelta);

                foreach (AgentEvent contextEvent in contextEvents)
                {
                    if (contextEvent is ErrorEvent)
                    {
                        errorCount++;
                    }

                    yield return contextEvent;
                }

                compactionCount += compactionDelta;

                if (shouldAbortBeforeModelCall)
                {
                    break;
                }

                // 3a. Stream LLM response, yielding text events immediately
                StringBuilder assistantTextBuilder = new StringBuilder();
                List<ToolCall> proposedToolCalls = new List<ToolCall>();
                string? streamErrorCode = null;
                long llmStageStart = Stopwatch.GetTimestamp();

                await foreach (AgentEvent streamEvent in _LlmClient
                    .StreamAsync(conversation, allTools, cancellationToken)
                    .ConfigureAwait(false))
                {
                    // Yield text events immediately for real-time streaming
                    if (streamEvent is AssistantTextEvent textEvent)
                    {
                        assistantTextBuilder.Append(textEvent.Text);
                        assistantTextChars += textEvent.Text.Length;
                        yield return streamEvent;
                    }
                    else if (streamEvent is ToolCallProposedEvent proposedEvent)
                    {
                        // Buffer tool calls for approval processing below
                        proposedToolCalls.Add(proposedEvent.ToolCall);
                    }
                    else
                    {
                        // Yield error events and others immediately
                        if (streamEvent is ErrorEvent errorEvent)
                        {
                            errorCount++;
                            streamErrorCode = errorEvent.Code;
                        }
                        yield return streamEvent;
                    }
                }

                MuxTelemetry.RecordAgentStage(
                    MuxTelemetryNames.StageLlm,
                    streamErrorCode == null ? MuxTelemetryNames.OutcomeSuccess : streamErrorCode,
                    MuxTelemetry.SecondsSince(llmStageStart));

                // Record durable usage telemetry for this model call. Only fresh metrics (a call that
                // actually completed this iteration) are attached; a stream that errored before completing
                // is recorded as a failed call with its error code.
                LlmCallMetrics? completedCall = _LlmClient.LastCall;
                bool isFreshCall = completedCall != null && !ReferenceEquals(completedCall, previousCall);
                RecordCallUsage(runId, iterationCount, isFreshCall ? completedCall : null, streamErrorCode);
                previousCall = completedCall;

                // Add assistant message to conversation history
                string assistantText = assistantTextBuilder.ToString();
                ConversationMessage assistantMessage = new ConversationMessage
                {
                    Role = RoleEnum.Assistant,
                    Content = string.IsNullOrEmpty(assistantText) ? null : assistantText,
                    ToolCalls = proposedToolCalls.Count > 0 ? proposedToolCalls : null
                };
                conversation.Add(assistantMessage);

                // 3c/3d. Check for tool calls
                if (proposedToolCalls.Count == 0)
                {
                    // Stop hooks run when the model finishes cleanly. A hook that exits 2 asks the model to keep
                    // going with the hook's stderr as a new user message, at most MaxStopHookReentries times per run.
                    IReadOnlyList<Mux.Core.Plugins.HookDefinition> stopHooks = streamErrorCode == null && _Options.Hooks != null
                        ? _Options.Hooks.HooksFor(Mux.Core.Plugins.HookEventEnum.Stop)
                        : new List<Mux.Core.Plugins.HookDefinition>();
                    if (stopHooks.Count == 0)
                    {
                        break;
                    }

                    IReadOnlyList<Mux.Core.Plugins.HookRunResult> stopResults = await _HookRunner.RunHooksAsync(
                        stopHooks,
                        Mux.Core.Plugins.HookEventEnum.Stop,
                        Mux.Core.Plugins.ToolHookPayload.ForStop(_Options.SessionId, _Options.WorkingDirectory, stopHookReentries > 0, assistantText),
                        _Options.WorkingDirectory,
                        cancellationToken).ConfigureAwait(false);

                    string? continueMessage = null;
                    string continueHook = string.Empty;
                    foreach (Mux.Core.Plugins.HookRunResult stopResult in stopResults)
                    {
                        if (IsHookFeedback(stopResult))
                        {
                            if (continueMessage == null)
                            {
                                continueMessage = FirstNonEmpty(stopResult.StdErr, stopResult.StdOut, "A stop hook asked you to continue.");
                                continueHook = stopResult.HookName;
                            }
                        }
                        else if (IsHookWarning(stopResult))
                        {
                            yield return HookWarning("stop", stopResult, string.Empty, string.Empty);
                        }
                    }

                    if (continueMessage == null)
                    {
                        break;
                    }

                    if (stopHookReentries >= MaxStopHookReentries)
                    {
                        yield return new HookEvent
                        {
                            HookEventName = "stop",
                            HookName = continueHook,
                            Outcome = HookEvent.OutcomeWarning,
                            ExitCode = 2,
                            Message = "A stop hook asked to continue again, but the limit of " + MaxStopHookReentries + " continuations per run was reached."
                        };
                        break;
                    }

                    stopHookReentries++;
                    yield return new HookEvent
                    {
                        HookEventName = "stop",
                        HookName = continueHook,
                        Outcome = HookEvent.OutcomeContinued,
                        ExitCode = 2,
                        Message = continueMessage
                    };
                    conversation.Add(new ConversationMessage
                    {
                        Role = RoleEnum.User,
                        Content = "A stop hook asked to continue: " + continueMessage
                    });
                    continue;
                }

                // 3e. Process each tool call
                foreach (ToolCall toolCall in proposedToolCalls)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    toolCallCount++;

                    // Yield proposed event
                    yield return new ToolCallProposedEvent { ToolCall = toolCall };

                    // Governance gate: enforce allow/deny lists and the sandbox posture before approval so a
                    // tool the model should never have called (including one it hallucinated past the
                    // advertised set) is refused without executing.
                    string? governanceDenyReason = EvaluateToolGovernance(toolCall);
                    if (governanceDenyReason != null)
                    {
                        MuxTelemetry.RecordApproval("policy_denied");
                        MuxTelemetry.RecordToolCall(ToolKindOf(toolCall.Name), ToolLabelOf(toolCall.Name), MuxTelemetryNames.OutcomeDenied, -1);
                        errorCount++;
                        yield return new ErrorEvent
                        {
                            Code = "tool_call_denied",
                            Message = governanceDenyReason
                        };

                        conversation.Add(new ConversationMessage
                        {
                            Role = RoleEnum.Tool,
                            ToolCallId = toolCall.Id,
                            Content = JsonSerializer.Serialize(new { error = "tool_call_denied", message = governanceDenyReason })
                        });
                        continue;
                    }

                    // Run through approval
                    bool approved = false;
                    ErrorEvent? approvalError = null;
                    long approvalStart = Stopwatch.GetTimestamp();
                    Activity? approvalActivity = MuxTelemetry.StartActivity("stage:approval");
                    MuxTelemetry.SetTag(approvalActivity, MuxTelemetryNames.AttrToolName, toolCall.Name);
                    MuxTelemetry.SetTag(approvalActivity, MuxTelemetryNames.AttrToolCallId, toolCall.Id);

                    try
                    {
                        ApprovalRequest approvalRequest = new ApprovalRequest
                        {
                            JobId = _Options.JobId,
                            ToolCallId = toolCall.Id,
                            ToolName = toolCall.Name,
                            ArgumentsSummary = toolCall.Arguments,
                            MutationKind = ClassifyTool(toolCall.Name)
                        };

                        // ask_user and exit_plan are the user's own channel; asking permission to ask would be noise,
                        // and a Deny policy must not silence them.
                        approved = _Interaction.HasTool(toolCall.Name) || await _ApprovalRouter
                            .RequestApprovalAsync(
                                approvalRequest,
                                async (ApprovalRequest request, CancellationToken escalationToken) =>
                                {
                                    Func<ToolCall, Task<string>> promptFunc = _Options.PromptUserFunc ?? DefaultPromptUserFunc;
                                    string response = await promptFunc(toolCall).ConfigureAwait(false);
                                    return MapPromptResponseToDecision(response);
                                },
                                cancellationToken)
                            .ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        approvalError = new ErrorEvent
                        {
                            Code = "approval_error",
                            Message = $"Approval check failed: {ex.Message}"
                        };
                        MuxTelemetry.RecordException(approvalActivity, ex);
                    }

                    string approvalDecision = approvalError != null ? MuxTelemetryNames.OutcomeError : (approved ? "approved" : "denied");
                    MuxTelemetry.RecordApproval(approvalDecision);
                    MuxTelemetry.RecordAgentStage(MuxTelemetryNames.StageApproval, approvalDecision, MuxTelemetry.SecondsSince(approvalStart));
                    MuxTelemetry.SetTag(approvalActivity, MuxTelemetryNames.LabelDecision, approvalDecision);
                    if (approvalError == null) MuxTelemetry.SetOk(approvalActivity);
                    MuxTelemetry.Stop(approvalActivity);
                    if (!approved && approvalError == null)
                    {
                        MuxTelemetry.RecordToolCall(ToolKindOf(toolCall.Name), ToolLabelOf(toolCall.Name), MuxTelemetryNames.OutcomeDenied, -1);
                    }

                    if (approvalError != null)
                    {
                        errorCount++;
                        yield return approvalError;
                        // Add an error tool result to conversation
                        conversation.Add(new ConversationMessage
                        {
                            Role = RoleEnum.Tool,
                            ToolCallId = toolCall.Id,
                            Content = JsonSerializer.Serialize(new { error = "approval_error", message = approvalError.Message })
                        });
                        continue;
                    }

                    if (!approved)
                    {
                        errorCount++;
                        yield return new ErrorEvent
                        {
                            Code = "tool_call_denied",
                            Message = $"Tool call '{toolCall.Name}' (id: {toolCall.Id}) was denied by the user."
                        };

                        // Add denial result to conversation
                        conversation.Add(new ConversationMessage
                        {
                            Role = RoleEnum.Tool,
                            ToolCallId = toolCall.Id,
                            Content = JsonSerializer.Serialize(new { error = "tool_call_denied", message = PromptResolver.Shared.GetEffective("result.tool_call_denied") })
                        });
                        continue;
                    }

                    // Approved
                    yield return new ToolCallApprovedEvent { ToolCallId = toolCall.Id };

                    // pre-tool-use hooks run after approval and before execution. Exit 2 blocks the call and its
                    // stderr becomes the tool result; any other failure is a warning and the call goes ahead.
                    string? hookBlockMessage = null;
                    string hookBlockName = string.Empty;
                    IReadOnlyList<Mux.Core.Plugins.HookDefinition> preHooks = _Options.Hooks != null
                        ? _Options.Hooks.HooksFor(Mux.Core.Plugins.HookEventEnum.PreToolUse, toolCall.Name)
                        : new List<Mux.Core.Plugins.HookDefinition>();
                    if (preHooks.Count > 0)
                    {
                        IReadOnlyList<Mux.Core.Plugins.HookRunResult> preResults = await _HookRunner.RunHooksAsync(
                            preHooks,
                            Mux.Core.Plugins.HookEventEnum.PreToolUse,
                            Mux.Core.Plugins.ToolHookPayload.ForTool("PreToolUse", _Options.SessionId, _Options.WorkingDirectory, toolCall.Name, toolCall.Id, toolCall.Arguments, null, null),
                            _Options.WorkingDirectory,
                            cancellationToken).ConfigureAwait(false);
                        foreach (Mux.Core.Plugins.HookRunResult preResult in preResults)
                        {
                            if (IsHookFeedback(preResult))
                            {
                                hookBlockMessage = FirstNonEmpty(preResult.StdErr, preResult.StdOut, "A pre-tool-use hook blocked this call.");
                                hookBlockName = preResult.HookName;
                                break;
                            }

                            if (IsHookWarning(preResult))
                            {
                                yield return HookWarning("pre-tool-use", preResult, toolCall.Name, toolCall.Id);
                            }
                        }
                    }

                    if (hookBlockMessage != null)
                    {
                        errorCount++;
                        yield return new HookEvent
                        {
                            HookEventName = "pre-tool-use",
                            HookName = hookBlockName,
                            Outcome = HookEvent.OutcomeBlocked,
                            ExitCode = 2,
                            ToolName = toolCall.Name,
                            ToolCallId = toolCall.Id,
                            Message = hookBlockMessage
                        };
                        yield return new ErrorEvent
                        {
                            Code = "tool_call_blocked_by_hook",
                            Message = $"Tool call '{toolCall.Name}' (id: {toolCall.Id}) was blocked by hook '{hookBlockName}': {hookBlockMessage}"
                        };

                        ToolResult blockedResult = new ToolResult
                        {
                            ToolCallId = toolCall.Id,
                            Success = false,
                            Content = JsonSerializer.Serialize(new { error = "tool_call_blocked_by_hook", hook = hookBlockName, message = hookBlockMessage })
                        };
                        yield return new ToolCallCompletedEvent
                        {
                            ToolCallId = toolCall.Id,
                            ToolName = toolCall.Name,
                            Result = blockedResult,
                            ElapsedMs = 0
                        };
                        conversation.Add(new ConversationMessage
                        {
                            Role = RoleEnum.Tool,
                            ToolCallId = toolCall.Id,
                            Content = blockedResult.Content
                        });
                        continue;
                    }

                    // Execute the tool
                    ToolResult result;
                    int taskPlanVersionBefore = _Options.TaskPlan?.Version ?? 0;
                    System.Diagnostics.Stopwatch toolStopwatch = System.Diagnostics.Stopwatch.StartNew();
                    string toolKind = ToolKindOf(toolCall.Name);
                    string toolOutcome = MuxTelemetryNames.OutcomeSuccess;
                    Activity? toolActivity = MuxTelemetry.StartActivity("tool " + toolCall.Name);
                    MuxTelemetry.SetTag(toolActivity, MuxTelemetryNames.AttrToolName, toolCall.Name);
                    MuxTelemetry.SetTag(toolActivity, MuxTelemetryNames.AttrToolCallId, toolCall.Id);
                    MuxTelemetry.SetTag(toolActivity, MuxTelemetryNames.LabelToolKind, toolKind);

                    try
                    {
                        result = await ExecuteToolCallAsync(toolCall, cancellationToken).ConfigureAwait(false);
                        if (!result.Success)
                        {
                            toolOutcome = MuxTelemetryNames.OutcomeFailure;
                            MuxTelemetry.SetError(toolActivity, "tool_failure", "tool returned a failure result");
                        }
                        else
                        {
                            MuxTelemetry.SetOk(toolActivity);
                        }
                    }
                    catch (Exception ex)
                    {
                        toolOutcome = MuxTelemetryNames.OutcomeError;
                        MuxTelemetry.RecordException(toolActivity, ex);
                        result = new ToolResult
                        {
                            ToolCallId = toolCall.Id,
                            Success = false,
                            Content = JsonSerializer.Serialize(new { error = "tool_execution_error", message = ex.Message })
                        };
                    }

                    toolStopwatch.Stop();
                    MuxTelemetry.Stop(toolActivity);
                    MuxTelemetry.RecordToolCall(toolKind, ToolLabelOf(toolCall.Name), toolOutcome, toolStopwatch.Elapsed.TotalSeconds);
                    MuxTelemetry.RecordAgentStage(MuxTelemetryNames.StageTool, toolOutcome, toolStopwatch.Elapsed.TotalSeconds);

                    // post-tool-use hooks see the result; their stdout (exit 0) or stderr (exit 2) is appended to the
                    // result the model reads and the completed event carries.
                    IReadOnlyList<Mux.Core.Plugins.HookDefinition> postHooks = _Options.Hooks != null
                        ? _Options.Hooks.HooksFor(Mux.Core.Plugins.HookEventEnum.PostToolUse, toolCall.Name)
                        : new List<Mux.Core.Plugins.HookDefinition>();
                    if (postHooks.Count > 0)
                    {
                        IReadOnlyList<Mux.Core.Plugins.HookRunResult> postResults = await _HookRunner.RunHooksAsync(
                            postHooks,
                            Mux.Core.Plugins.HookEventEnum.PostToolUse,
                            Mux.Core.Plugins.ToolHookPayload.ForTool("PostToolUse", _Options.SessionId, _Options.WorkingDirectory, toolCall.Name, toolCall.Id, toolCall.Arguments, result.Success, result.Content),
                            _Options.WorkingDirectory,
                            cancellationToken).ConfigureAwait(false);
                        foreach (Mux.Core.Plugins.HookRunResult postResult in postResults)
                        {
                            string? appended = null;
                            if (IsHookFeedback(postResult))
                            {
                                appended = FirstNonEmpty(postResult.StdErr, postResult.StdOut, string.Empty);
                            }
                            else if (postResult.Started && !postResult.TimedOut && postResult.ExitCode == 0)
                            {
                                appended = postResult.StdOut;
                            }
                            else
                            {
                                yield return HookWarning("post-tool-use", postResult, toolCall.Name, toolCall.Id);
                            }

                            if (!string.IsNullOrWhiteSpace(appended))
                            {
                                result.Content = (result.Content ?? string.Empty) + "\n\n[hook " + postResult.HookName + "] " + appended.Trim();
                                yield return new HookEvent
                                {
                                    HookEventName = "post-tool-use",
                                    HookName = postResult.HookName,
                                    Outcome = HookEvent.OutcomeAppended,
                                    ExitCode = postResult.ExitCode,
                                    ToolName = toolCall.Name,
                                    ToolCallId = toolCall.Id,
                                    Message = appended.Trim()
                                };
                            }
                        }
                    }

                    yield return new ToolCallCompletedEvent
                    {
                        ToolCallId = toolCall.Id,
                        ToolName = toolCall.Name,
                        Result = result,
                        ElapsedMs = toolStopwatch.ElapsedMilliseconds
                    };

                    // A task tool mutates the per-job plan; surface the change as its own event so the TUI
                    // checklist, the sidebar, and the jsonl contract all observe it.
                    if (_Options.TaskPlan != null && _Options.TaskPlan.Version != taskPlanVersionBefore)
                    {
                        yield return new TaskPlanUpdatedEvent
                        {
                            ChangeKind = _Options.TaskPlan.LastChangeKind,
                            ChangedTaskId = _Options.TaskPlan.LastChangedTaskId,
                            Tasks = _Options.TaskPlan.Snapshot()
                        };
                    }

                    // Append tool result to conversation
                    conversation.Add(new ConversationMessage
                    {
                        Role = RoleEnum.Tool,
                        ToolCallId = toolCall.Id,
                        Content = result.Content
                    });
                }

                // 3f. Yield heartbeat
                yield return new HeartbeatEvent { StepNumber = step + 1 };

                // 3g. Loop back
            }

            // 4. Check if we exhausted iterations
            if (conversation.Count > 0)
            {
                ConversationMessage lastMessage = conversation[conversation.Count - 1];
                if (lastMessage.Role == RoleEnum.Tool)
                {
                    maxIterationsReached = true;
                    errorCount++;
                    yield return new ErrorEvent
                    {
                        Code = "max_iterations_reached",
                        Message = $"Agent loop reached the maximum of {_Options.MaxIterations} iterations."
                    };
                }
            }

            stopwatch.Stop();
            ContextBudgetSnapshot finalSnapshot = GetContextBudgetSnapshot(conversation, allTools);
            _FinalConversation = conversation;

            yield return new RunCompletedEvent
            {
                RunId = runId,
                SessionId = _Options.SessionId,
                Status = budgetExceeded
                    ? "budget_exceeded"
                    : (maxIterationsReached
                        ? "max_iterations_reached"
                        : (errorCount > 0 ? "completed_with_errors" : "completed")),
                IterationsCompleted = iterationCount,
                ToolCallCount = toolCallCount,
                ErrorCount = errorCount,
                AssistantTextChars = assistantTextChars,
                DurationMs = stopwatch.ElapsedMilliseconds,
                FinalEstimatedTokens = finalSnapshot.UsedTokens,
                CompactionCount = compactionCount,
                InputTokens = _LlmClient.CumulativeUsage.InputTokens,
                OutputTokens = _LlmClient.CumulativeUsage.OutputTokens,
                TotalTokens = _LlmClient.CumulativeUsage.TotalTokens,
                TaskSummary = _Options.TaskPlan != null && !_Options.TaskPlan.IsEmpty
                    ? Mux.Core.Tasks.TaskPlanSummary.FromTasks(_Options.TaskPlan.Snapshot())
                    : null
            };
        }

        /// <summary>
        /// Releases the resources used by this <see cref="AgentLoop"/> instance.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion

        #region Private-Methods

        /// <summary>
        /// Releases the unmanaged resources and optionally the managed resources.
        /// </summary>
        /// <param name="disposing">True to release both managed and unmanaged resources.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (!_Disposed)
            {
                if (disposing)
                {
                    _LlmClient?.Dispose();
                }

                _Disposed = true;
            }
        }

        private List<ConversationMessage> BuildConversation(string prompt)
        {
            List<ConversationMessage> conversation = new List<ConversationMessage>();

            // System message
            if (!string.IsNullOrEmpty(_Options.SystemPrompt))
            {
                conversation.Add(new ConversationMessage
                {
                    Role = RoleEnum.System,
                    Content = _Options.SystemPrompt
                });
            }

            // Existing history
            foreach (ConversationMessage message in _Options.ConversationHistory)
            {
                conversation.Add(message);
            }

            // New user message
            conversation.Add(new ConversationMessage
            {
                Role = RoleEnum.User,
                Content = prompt
            });

            return conversation;
        }

        private List<ToolDefinition> MergeToolDefinitions()
        {
            List<ToolDefinition> allTools = new List<ToolDefinition>();

            // Built-in tools
            List<ToolDefinition> builtInTools = _ToolRegistry.GetToolDefinitions();
            allTools.AddRange(builtInTools);

            // Additional (MCP) tools
            if (_Options.AdditionalTools != null)
            {
                allTools.AddRange(_Options.AdditionalTools);
            }

            // External tool providers (MCP connections, skills catalog).
            if (_Options.ExternalToolProviders != null)
            {
                foreach (IExternalToolProvider provider in _Options.ExternalToolProviders)
                {
                    allTools.AddRange(provider.GetToolDefinitions());
                }
            }

            // ask_user (and exit_plan in plan mode): the model's channel to the user.
            allTools.AddRange(_Interaction.GetToolDefinitions());

            // Apply tool governance so the model is never offered a tool it may not call: drop tools
            // excluded by the allow/deny policy, and (under the read-only posture) drop mutating tools.
            bool hasAllowDeny = (_Options.AllowedTools != null && _Options.AllowedTools.Count > 0)
                || (_Options.DeniedTools != null && _Options.DeniedTools.Count > 0);
            if (hasAllowDeny || _Options.SandboxPosture == SandboxPostureEnum.ReadOnly)
            {
                List<ToolDefinition> permitted = new List<ToolDefinition>();
                foreach (ToolDefinition tool in allTools)
                {
                    if (!ToolGovernance.IsPermitted(tool.Name, _Options.AllowedTools, _Options.DeniedTools))
                    {
                        continue;
                    }

                    if (_Options.SandboxPosture == SandboxPostureEnum.ReadOnly
                        && ClassifyTool(tool.Name) == ToolMutationKind.Mutating)
                    {
                        continue;
                    }

                    permitted.Add(tool);
                }

                return permitted;
            }

            return allTools;
        }

        private IExternalToolProvider? FindProviderFor(string toolName)
        {
            if (_Interaction != null && _Interaction.HasTool(toolName))
            {
                return _Interaction;
            }

            if (_Options.ExternalToolProviders == null)
            {
                return null;
            }

            foreach (IExternalToolProvider provider in _Options.ExternalToolProviders)
            {
                if (provider.HasTool(toolName))
                {
                    return provider;
                }
            }

            return null;
        }

        private ContextBudgetSnapshot GetContextBudgetSnapshot(
            List<ConversationMessage> conversation,
            List<ToolDefinition> allTools)
        {
            ContextWindowManager manager = new ContextWindowManager(
                _Options.Endpoint.ContextWindow,
                _Options.TokenEstimationRatio,
                _Options.ContextWindowSafetyMarginPercent);

            return manager.GetBudgetSnapshot(
                systemPrompt: null,
                messages: conversation,
                tools: allTools,
                reservedOutputTokens: _Options.Endpoint.MaxTokens,
                warningThresholdPercent: _Options.ContextWarningThresholdPercent);
        }

        private List<AgentEvent> PrepareConversationForModelCall(
            ref List<ConversationMessage> conversation,
            List<ToolDefinition> allTools,
            string trigger,
            CancellationToken cancellationToken,
            out bool shouldAbort,
            out int compactionCountDelta)
        {
            shouldAbort = false;
            compactionCountDelta = 0;

            List<AgentEvent> events = new List<AgentEvent>();
            ContextBudgetSnapshot snapshot = GetContextBudgetSnapshot(conversation, allTools);
            string warningLevel = GetWarningLevel(snapshot);

            if (!string.Equals(warningLevel, "ok", StringComparison.Ordinal))
            {
                events.Add(CreateContextStatusEvent(snapshot, conversation.Count, trigger));
            }

            if (!snapshot.IsOverLimit)
            {
                return events;
            }

            if (!_Options.AutoCompactEnabled)
            {
                events.Add(CreateContextLimitExceededError(snapshot, "automatic compaction is disabled"));
                shouldAbort = true;
                return events;
            }

            List<ConversationMessage> compactedConversation = new List<ConversationMessage>(conversation);
            long compactionStart = Stopwatch.GetTimestamp();
            Activity? compactionActivity = MuxTelemetry.StartActivity("stage:compaction");
            MuxTelemetry.SetTag(compactionActivity, MuxTelemetryNames.LabelStrategy, _Options.CompactionStrategy);
            ContextCompactedEvent? compactionEvent;
            List<ConversationMessage> workingConversation;
            string failureDetail;
            try
            {
                compactionEvent = TryCompactActiveConversation(
                    compactedConversation,
                    allTools,
                    snapshot,
                    cancellationToken,
                    out workingConversation,
                    out failureDetail);
            }
            catch (Exception ex)
            {
                MuxTelemetry.RecordException(compactionActivity, ex);
                MuxTelemetry.Stop(compactionActivity);
                MuxTelemetry.RecordAgentStage(MuxTelemetryNames.StageCompaction, MuxTelemetryNames.OutcomeError, MuxTelemetry.SecondsSince(compactionStart));
                throw;
            }

            string compactionOutcome = compactionEvent == null ? MuxTelemetryNames.OutcomeFailure : MuxTelemetryNames.OutcomeSuccess;
            if (compactionEvent == null) MuxTelemetry.SetError(compactionActivity, "compaction_failed", failureDetail);
            else MuxTelemetry.SetOk(compactionActivity);
            MuxTelemetry.Stop(compactionActivity);
            MuxTelemetry.RecordAgentStage(MuxTelemetryNames.StageCompaction, compactionOutcome, MuxTelemetry.SecondsSince(compactionStart));

            if (compactionEvent == null)
            {
                events.Add(CreateContextLimitExceededError(snapshot, failureDetail));
                shouldAbort = true;
                return events;
            }

            conversation = workingConversation;
            compactionCountDelta = 1;

            ContextBudgetSnapshot afterSnapshot = GetContextBudgetSnapshot(conversation, allTools);
            events.Add(compactionEvent);
            events.Add(CreateContextStatusEvent(afterSnapshot, conversation.Count, "post_compaction"));

            if (afterSnapshot.IsOverLimit)
            {
                events.Add(CreateContextLimitExceededError(afterSnapshot, "active conversation still exceeds the usable context budget after compaction"));
                shouldAbort = true;
            }

            return events;
        }

        private ContextCompactedEvent? TryCompactActiveConversation(
            List<ConversationMessage> conversation,
            List<ToolDefinition> allTools,
            ContextBudgetSnapshot snapshot,
            CancellationToken cancellationToken,
            out List<ConversationMessage> compactedConversation,
            out string failureDetail)
        {
            compactedConversation = new List<ConversationMessage>(conversation);
            failureDetail = "no older active conversation messages were eligible for in-run compaction";

            bool usedSummary = false;
            bool usedTrim = false;
            List<ConversationMessage> workingConversation = new List<ConversationMessage>(conversation);

            if (string.Equals(_Options.CompactionStrategy, "summary", StringComparison.Ordinal))
            {
                if (TryCreateSummaryCompactedConversation(
                    workingConversation,
                    cancellationToken,
                    out List<ConversationMessage> summaryConversation,
                    out string? summaryFailure))
                {
                    workingConversation = summaryConversation;
                    usedSummary = true;
                }
                else if (!string.IsNullOrWhiteSpace(summaryFailure))
                {
                    failureDetail = $"summary-based in-run compaction failed: {summaryFailure}";
                }
            }

            ContextBudgetSnapshot postSummarySnapshot = GetContextBudgetSnapshot(workingConversation, allTools);
            if (string.Equals(_Options.CompactionStrategy, "trim", StringComparison.Ordinal) || postSummarySnapshot.IsOverLimit)
            {
                ConversationTrimResult trimResult = TrimActiveConversationToTarget(workingConversation, allTools, postSummarySnapshot);
                if (trimResult.DidTrim)
                {
                    workingConversation = trimResult.CompactedHistory;
                    usedTrim = true;
                }
                else if (!usedSummary)
                {
                    return null;
                }
            }

            ContextBudgetSnapshot finalSnapshot = GetContextBudgetSnapshot(workingConversation, allTools);
            if (!usedSummary && !usedTrim)
            {
                return null;
            }

            compactedConversation = workingConversation;
            return new ContextCompactedEvent
            {
                Scope = "active_conversation",
                Mode = "auto",
                Strategy = usedSummary && usedTrim
                    ? "summary+trim"
                    : (usedSummary ? "summary" : "trim"),
                MessagesBefore = conversation.Count,
                MessagesAfter = workingConversation.Count,
                EstimatedTokensBefore = snapshot.UsedTokens,
                EstimatedTokensAfter = finalSnapshot.UsedTokens,
                SummaryCreated = usedSummary,
                Reason = "Active conversation exceeded the usable context budget before a model call."
            };
        }

        private ConversationTrimResult TrimActiveConversationToTarget(
            List<ConversationMessage> conversation,
            List<ToolDefinition> allTools,
            ContextBudgetSnapshot snapshot)
        {
            int targetUsedTokens = Math.Max(
                1,
                (int)(snapshot.UsableInputLimit * (InRunCompactionTargetPercent / 100.0)));

            ConversationTrimResult trimResult = ConversationTrimCompactor.TrimToTarget(
                conversation,
                _Options.CompactionPreserveTurns,
                targetUsedTokens,
                candidateHistory => GetContextBudgetSnapshot(candidateHistory, allTools).UsedTokens);

            ContextBudgetSnapshot postPlannerSnapshot = GetContextBudgetSnapshot(trimResult.CompactedHistory, allTools);
            if (!postPlannerSnapshot.IsOverLimit)
            {
                return trimResult;
            }

            return EmergencyTrimActiveConversation(trimResult.CompactedHistory, allTools, targetUsedTokens);
        }

        private ConversationTrimResult EmergencyTrimActiveConversation(
            List<ConversationMessage> conversation,
            List<ToolDefinition> allTools,
            int targetUsedTokens)
        {
            List<ConversationMessage> result = new List<ConversationMessage>(conversation);
            int usedTokensBefore = GetContextBudgetSnapshot(result, allTools).UsedTokens;
            int protectedPrefixCount = GetLeadingSystemMessageCount(result);

            while (GetContextBudgetSnapshot(result, allTools).UsedTokens > targetUsedTokens)
            {
                int latestUserIndex = FindLastUserIndex(result);
                int protectedTailStart = Math.Max(protectedPrefixCount, result.Count - InRunProtectedTailMessageCount);
                int removeIndex = -1;

                for (int i = protectedPrefixCount; i < result.Count; i++)
                {
                    bool isLatestUser = i == latestUserIndex;
                    bool isProtectedTail = i >= protectedTailStart;
                    if (!isLatestUser && !isProtectedTail)
                    {
                        removeIndex = i;
                        break;
                    }
                }

                if (removeIndex < 0)
                {
                    break;
                }

                result.RemoveAt(removeIndex);
            }

            int usedTokensAfter = GetContextBudgetSnapshot(result, allTools).UsedTokens;
            return new ConversationTrimResult
            {
                CompactedHistory = result,
                RemovedMessageCount = Math.Max(0, conversation.Count - result.Count),
                UsedTokensBefore = usedTokensBefore,
                UsedTokensAfter = usedTokensAfter,
                ReachedTarget = usedTokensAfter <= targetUsedTokens
            };
        }

        private bool TryCreateSummaryCompactedConversation(
            List<ConversationMessage> conversation,
            CancellationToken cancellationToken,
            out List<ConversationMessage> compactedConversation,
            out string? failureMessage)
        {
            int protectedPrefixCount = GetLeadingSystemMessageCount(conversation);
            List<ConversationMessage> protectedPrefix = new List<ConversationMessage>();

            for (int i = 0; i < protectedPrefixCount; i++)
            {
                if (!IsSyntheticSummaryMessage(conversation[i]))
                {
                    protectedPrefix.Add(conversation[i]);
                }
            }

            List<ConversationMessage> compactableConversation = conversation.Count > protectedPrefixCount
                ? conversation.GetRange(protectedPrefixCount, conversation.Count - protectedPrefixCount)
                : new List<ConversationMessage>();

            ConversationCompactionPlan plan = ConversationCompactionPlanner.CreatePlan(
                compactableConversation,
                _Options.CompactionPreserveTurns,
                SyntheticSummaryPrefix);

            if (!plan.CanCompact)
            {
                compactedConversation = new List<ConversationMessage>(conversation);
                failureMessage = null;
                return false;
            }

            try
            {
                string summary = GenerateCompactionSummary(plan.MessagesToCompact, cancellationToken);
                if (string.IsNullOrWhiteSpace(summary))
                {
                    compactedConversation = new List<ConversationMessage>(conversation);
                    failureMessage = "compaction produced no summary";
                    return false;
                }

                compactedConversation = new List<ConversationMessage>(protectedPrefix)
                {
                    new ConversationMessage
                    {
                        Role = RoleEnum.System,
                        Content = $"{SyntheticSummaryPrefix}{Environment.NewLine}{Environment.NewLine}{summary.Trim()}"
                    }
                };
                compactedConversation.AddRange(plan.MessagesToPreserve);
                failureMessage = null;
                return true;
            }
            catch (Exception ex)
            {
                compactedConversation = new List<ConversationMessage>(conversation);
                failureMessage = ex.Message;
                return false;
            }
        }

        private void RecordCallUsage(string runId, int iteration, LlmCallMetrics? metrics, string? errorCode)
        {
            // Nothing worth recording: no completed call this iteration and no error to note. (The
            // compaction sidecar uses the non-streaming path, which reports no usage, so its spend is not
            // captured here — a known gap pending a provider-library change.)
            bool errored = !string.IsNullOrEmpty(errorCode);
            if (metrics == null && !errored)
            {
                return;
            }

            UsageEvent usageEvent = UsageEvent.FromCall(
                _Options.Endpoint,
                metrics,
                metrics?.Usage,
                _Options.UsageCallKind,
                _Options.CommandName,
                !errored && (metrics?.Success ?? false));
            usageEvent.RunId = runId;
            usageEvent.SessionId = string.IsNullOrEmpty(_Options.SessionId) ? null : _Options.SessionId;
            usageEvent.JobId = string.IsNullOrEmpty(_Options.JobId) ? null : _Options.JobId;
            usageEvent.Project = ExtractProject(_Options.WorkingDirectory);
            usageEvent.Iteration = iteration;
            usageEvent.ErrorCode = errored ? errorCode : null;

            // Best-effort: the recorder never throws, but guard anyway so a telemetry fault cannot break a run.
            try
            {
                _UsageRecorder.Record(usageEvent);
            }
            catch (Exception)
            {
                // Intentionally swallowed — usage recording must never affect the agent loop.
            }
        }

        private static string? ExtractProject(string? workingDirectory)
        {
            if (string.IsNullOrWhiteSpace(workingDirectory))
            {
                return null;
            }

            string trimmed = workingDirectory.TrimEnd('/', '\\');
            string name = System.IO.Path.GetFileName(trimmed);
            return string.IsNullOrEmpty(name) ? null : name;
        }

        private string GenerateCompactionSummary(List<ConversationMessage> messagesToCompact, CancellationToken cancellationToken)
        {
            string compactableDigest = BuildConversationDigest(messagesToCompact, maxChars: 12000);
            string systemPrompt = string.IsNullOrWhiteSpace(_Options.CompactionSystemPrompt)
                ? PromptCatalog.DefaultFor("compaction.system")
                : _Options.CompactionSystemPrompt;
            string userPrompt =
                $"{PromptResolver.Shared.GetEffective("compaction.user")}{Environment.NewLine}{Environment.NewLine}{compactableDigest}";

            string summary = RunSidecarPromptAsync(systemPrompt, userPrompt, cancellationToken)
                .GetAwaiter()
                .GetResult();

            return summary.Trim();
        }

        private async Task<string> RunSidecarPromptAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken)
        {
            EndpointConfig sidecarEndpoint = CreateSidecarEndpoint();
            using LlmClient client = new LlmClient(sidecarEndpoint, _Options.IgnoreCertErrors);
            client.OnRetry = _Options.OnRetry;

            ConversationMessage response = await client.SendAsync(
                new List<ConversationMessage>
                {
                    new ConversationMessage
                    {
                        Role = RoleEnum.System,
                        Content = systemPrompt
                    },
                    new ConversationMessage
                    {
                        Role = RoleEnum.User,
                        Content = userPrompt
                    }
                },
                new List<ToolDefinition>(),
                cancellationToken).ConfigureAwait(false);

            return response.Content?.Trim() ?? string.Empty;
        }

        private EndpointConfig CreateSidecarEndpoint()
        {
            EndpointConfig sidecarEndpoint = CloneEndpoint(_Options.Endpoint);
            sidecarEndpoint.Quirks ??= Defaults.QuirksForAdapter(sidecarEndpoint.AdapterType);
            sidecarEndpoint.Quirks.SupportsTools = false;
            sidecarEndpoint.Quirks.EnableMalformedToolCallRecovery = false;
            sidecarEndpoint.Temperature = 0.0;
            sidecarEndpoint.MaxTokens = Math.Min(sidecarEndpoint.MaxTokens, 2048);
            return sidecarEndpoint;
        }

        private static EndpointConfig CloneEndpoint(EndpointConfig endpoint)
        {
            return new EndpointConfig
            {
                Name = endpoint.Name,
                AdapterType = endpoint.AdapterType,
                BaseUrl = endpoint.BaseUrl,
                Model = endpoint.Model,
                IsDefault = endpoint.IsDefault,
                MaxTokens = endpoint.MaxTokens,
                Temperature = endpoint.Temperature,
                ContextWindow = endpoint.ContextWindow,
                TimeoutMs = endpoint.TimeoutMs,
                Headers = new Dictionary<string, string>(endpoint.Headers),
                AutoApproveTools = endpoint.AutoApproveTools,
                MaxAgentIterations = endpoint.MaxAgentIterations,
                Quirks = CloneBackendQuirks(endpoint.Quirks)
            };
        }

        private static BackendQuirks? CloneBackendQuirks(BackendQuirks? quirks)
        {
            if (quirks == null)
            {
                return null;
            }

            return new BackendQuirks
            {
                AssembleToolCallDeltas = quirks.AssembleToolCallDeltas,
                SupportsParallelToolCalls = quirks.SupportsParallelToolCalls,
                SupportsTools = quirks.SupportsTools,
                EnableMalformedToolCallRecovery = quirks.EnableMalformedToolCallRecovery,
                RequiresToolResultContentAsString = quirks.RequiresToolResultContentAsString,
                DefaultFinishReason = quirks.DefaultFinishReason,
                StripRequestFields = new List<string>(quirks.StripRequestFields)
            };
        }

        private static bool IsSyntheticSummaryMessage(ConversationMessage message)
        {
            return message.Role == RoleEnum.System
                && !string.IsNullOrWhiteSpace(message.Content)
                && message.Content.StartsWith(SyntheticSummaryPrefix, StringComparison.Ordinal);
        }

        private static string BuildConversationDigest(IEnumerable<ConversationMessage> messages, int maxChars)
        {
            StringBuilder sb = new StringBuilder();

            foreach (ConversationMessage message in messages)
            {
                if (string.IsNullOrWhiteSpace(message.Content) && (message.ToolCalls == null || message.ToolCalls.Count == 0))
                {
                    continue;
                }

                sb.Append('[');
                sb.Append(message.Role.ToString().ToLowerInvariant());
                sb.Append("] ");

                if (!string.IsNullOrWhiteSpace(message.Content))
                {
                    sb.Append(message.Content.Trim());
                }
                else if (message.ToolCalls != null && message.ToolCalls.Count > 0)
                {
                    sb.Append("tool calls: ");

                    for (int i = 0; i < message.ToolCalls.Count; i++)
                    {
                        if (i > 0)
                        {
                            sb.Append(", ");
                        }

                        sb.Append(message.ToolCalls[i].Name);
                    }
                }

                sb.AppendLine();
                sb.AppendLine();
            }

            string digest = sb.ToString().Trim();

            if (digest.Length <= maxChars)
            {
                return digest;
            }

            int half = Math.Max(1, (maxChars - 24) / 2);
            return digest.Substring(0, half).TrimEnd()
                + Environment.NewLine
                + PromptResolver.Shared.GetEffective("digest.truncation-marker")
                + Environment.NewLine
                + digest.Substring(Math.Max(0, digest.Length - half)).TrimStart();
        }

        private static int GetLeadingSystemMessageCount(List<ConversationMessage> conversation)
        {
            int count = 0;

            while (count < conversation.Count && conversation[count].Role == RoleEnum.System)
            {
                count++;
            }

            return count;
        }

        private static int FindLastUserIndex(List<ConversationMessage> conversation)
        {
            for (int i = conversation.Count - 1; i >= 0; i--)
            {
                if (conversation[i].Role == RoleEnum.User)
                {
                    return i;
                }
            }

            return -1;
        }

        private static string GetWarningLevel(ContextBudgetSnapshot snapshot)
        {
            if (snapshot.IsOverLimit)
            {
                return "critical";
            }

            if (snapshot.IsApproachingLimit)
            {
                return "approaching";
            }

            return "ok";
        }

        private static ContextStatusEvent CreateContextStatusEvent(ContextBudgetSnapshot snapshot, int messageCount, string trigger)
        {
            return new ContextStatusEvent
            {
                Scope = "active_conversation",
                EstimatedTokens = snapshot.UsedTokens,
                UsableInputLimit = snapshot.UsableInputLimit,
                RemainingTokens = snapshot.RemainingTokens,
                RemainingPercent = snapshot.UsableInputLimit <= 0
                    ? 0
                    : Math.Round((snapshot.RemainingTokens / (double)snapshot.UsableInputLimit) * 100.0, 1),
                WarningThresholdTokens = snapshot.WarningThresholdTokens,
                MessageCount = messageCount,
                Trigger = trigger,
                WarningLevel = GetWarningLevel(snapshot)
            };
        }

        private static ErrorEvent CreateContextLimitExceededError(ContextBudgetSnapshot snapshot, string detail)
        {
            return new ErrorEvent
            {
                Code = "context_limit_exceeded",
                Message = $"Estimated context usage exceeds the usable limit ({snapshot.UsedTokens} / {snapshot.UsableInputLimit} tokens); {detail}."
            };
        }

        private async Task<ToolResult> ExecuteToolCallAsync(ToolCall toolCall, CancellationToken cancellationToken)
        {
            JsonElement arguments = ParseToolArguments(toolCall.Arguments);

            // Classify the tool: unknown/external (MCP) tools are treated as mutating (the safe default).
            ToolMutationKind mutationKind = ClassifyTool(toolCall.Name);

            // Mutating tools serialize through the shared workspace write lease when one is configured;
            // read-only tools bypass it and run concurrently across jobs.
            if (_Options.WriteLease != null && mutationKind == ToolMutationKind.Mutating)
            {
                Task<WriteLeaseHandle> acquire = _Options.WriteLease.AcquireAsync(_Options.JobId, cancellationToken);
                bool waited = !acquire.IsCompleted;
                long leaseWaitStart = Stopwatch.GetTimestamp();
                Activity? leaseActivity = null;
                if (waited)
                {
                    leaseActivity = MuxTelemetry.StartActivity("stage:write_lease_wait");
                    _Options.OnWriteLeaseWaitChanged?.Invoke(true);
                }

                WriteLeaseHandle handle;
                try
                {
                    handle = await acquire.ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    MuxTelemetry.RecordException(leaseActivity, ex);
                    MuxTelemetry.Stop(leaseActivity);
                    if (waited) MuxTelemetry.RecordAgentStage(MuxTelemetryNames.StageWriteLeaseWait, MuxTelemetryNames.OutcomeError, MuxTelemetry.SecondsSince(leaseWaitStart));
                    throw;
                }

                if (waited)
                {
                    MuxTelemetry.SetOk(leaseActivity);
                    MuxTelemetry.Stop(leaseActivity);
                    MuxTelemetry.RecordAgentStage(MuxTelemetryNames.StageWriteLeaseWait, MuxTelemetryNames.OutcomeSuccess, MuxTelemetry.SecondsSince(leaseWaitStart));
                }

                try
                {
                    if (waited)
                    {
                        _Options.OnWriteLeaseWaitChanged?.Invoke(false);
                    }

                    return await ExecuteToolCallCoreAsync(toolCall, arguments, cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    handle.Dispose();
                }
            }

            return await ExecuteToolCallCoreAsync(toolCall, arguments, cancellationToken).ConfigureAwait(false);
        }

        private async Task<ToolResult> ExecuteToolCallCoreAsync(ToolCall toolCall, JsonElement arguments, CancellationToken cancellationToken)
        {
            // Check if it is a built-in tool
            if (_ToolRegistry.HasTool(toolCall.Name))
            {
                return await _ToolRegistry
                    .ExecuteAsync(toolCall.Id, toolCall.Name, arguments, _Options.WorkingDirectory, cancellationToken)
                    .ConfigureAwait(false);
            }

            // Try an external tool provider (MCP connection, skills catalog) that owns the tool.
            IExternalToolProvider? provider = FindProviderFor(toolCall.Name);
            if (provider != null)
            {
                return await provider
                    .ExecuteAsync(toolCall.Name, arguments, _Options.WorkingDirectory, cancellationToken)
                    .ConfigureAwait(false);
            }

            // Fall back to the legacy single external executor for MCP tools.
            if (_Options.ExternalToolExecutor != null)
            {
                return await _Options
                    .ExternalToolExecutor(toolCall.Name, arguments, _Options.WorkingDirectory, cancellationToken)
                    .ConfigureAwait(false);
            }

            // Unknown tool
            return new ToolResult
            {
                ToolCallId = toolCall.Id,
                Success = false,
                Content = JsonSerializer.Serialize(new { error = "unknown_tool", message = PromptResolver.Shared.Resolve("result.unknown_tool", new Dictionary<string, string> { { "{ToolName}", toolCall.Name } }) })
            };
        }

        private string? EvaluateToolGovernance(ToolCall toolCall)
        {
            if (!ToolGovernance.IsPermitted(toolCall.Name, _Options.AllowedTools, _Options.DeniedTools))
            {
                return PromptResolver.Shared.Resolve("result.tool_policy_denied", new Dictionary<string, string> { { "{ToolName}", toolCall.Name } });
            }

            if (_Options.SandboxPosture == SandboxPostureEnum.ReadOnly
                && ClassifyTool(toolCall.Name) == ToolMutationKind.Mutating)
            {
                return $"Tool '{toolCall.Name}' is blocked by the read-only sandbox posture.";
            }

            if (_Options.SandboxPosture == SandboxPostureEnum.WorkspaceWrite)
            {
                JsonElement arguments = ParseToolArguments(toolCall.Arguments);
                return ToolGovernance.CheckWorkspaceWrite(
                    toolCall.Name,
                    arguments,
                    _Options.WorkingDirectory,
                    _Options.AdditionalDirectories);
            }

            return null;
        }

        private ToolMutationKind ClassifyTool(string toolName)
        {
            if (_ToolRegistry.HasTool(toolName))
            {
                return _ToolRegistry.GetMutationKind(toolName);
            }

            // A provider that owns the tool decides its classification (a read-only skill, for example).
            IExternalToolProvider? provider = FindProviderFor(toolName);
            if (provider != null)
            {
                return provider.GetMutationKind(toolName);
            }

            // Unknown/external tools are treated as mutating — the safe default.
            return ToolMutationKind.Mutating;
        }

        private string ToolKindOf(string toolName)
        {
            if (_ToolRegistry.HasTool(toolName)) return "builtin";

            IExternalToolProvider? provider = FindProviderFor(toolName);
            if (provider != null)
            {
                return provider is Mux.Core.Skills.SkillToolProvider || provider is Mux.Core.Skills.SkillRuntime
                    ? "skill"
                    : (toolName.Contains('.') ? "mcp" : "external");
            }

            return _Options.ExternalToolExecutor != null ? "mcp" : "unknown";
        }

        private string ToolLabelOf(string toolName)
        {
            // Only built-in tool names are a bounded set; every other tool collapses to its kind so MCP and
            // skill tool names (user configuration) never become metric label values. Spans keep the full name.
            return _ToolRegistry.HasTool(toolName) ? toolName : ToolKindOf(toolName);
        }

        private static string ProviderName(EndpointConfig endpoint)
        {
            return endpoint.AdapterType.ToString().ToLowerInvariant();
        }

        // A hook "speaks" to the model by exiting 2 (block, feedback, or continue). Start failures and timeouts never
        // count, so a broken hook can never block work.
        private static bool IsHookFeedback(Mux.Core.Plugins.HookRunResult result)
        {
            return result.Started && !result.TimedOut && result.ExitCode == 2;
        }

        private static bool IsHookWarning(Mux.Core.Plugins.HookRunResult result)
        {
            return !result.Started || result.TimedOut || (result.ExitCode != 0 && result.ExitCode != 2);
        }

        private static HookEvent HookWarning(string hookEventName, Mux.Core.Plugins.HookRunResult result, string toolName, string toolCallId)
        {
            string reason = !result.Started
                ? "could not start: " + result.StdErr
                : (result.TimedOut ? "timed out and was stopped" : "exited with code " + result.ExitCode + (string.IsNullOrWhiteSpace(result.StdErr) ? string.Empty : ": " + result.StdErr));
            return new HookEvent
            {
                HookEventName = hookEventName,
                HookName = result.HookName,
                Outcome = HookEvent.OutcomeWarning,
                ExitCode = result.ExitCode,
                ToolName = toolName,
                ToolCallId = toolCallId,
                Message = "Hook '" + result.HookName + "' " + reason + "; continuing."
            };
        }

        private static string FirstNonEmpty(string? first, string? second, string fallback)
        {
            if (!string.IsNullOrWhiteSpace(first)) return first!.Trim();
            if (!string.IsNullOrWhiteSpace(second)) return second!.Trim();
            return fallback;
        }

        private static ApprovalDecision MapPromptResponseToDecision(string response)
        {
            string trimmed = (response ?? string.Empty).Trim();

            if (string.Equals(trimmed, "always", StringComparison.OrdinalIgnoreCase))
            {
                return ApprovalDecision.AlwaysThisSession;
            }

            if (string.Equals(trimmed, "n", StringComparison.OrdinalIgnoreCase)
                || string.Equals(trimmed, "no", StringComparison.OrdinalIgnoreCase))
            {
                return ApprovalDecision.Denied;
            }

            // "y", "yes", or empty all approve this call.
            return ApprovalDecision.Approved;
        }

        private static Task<string> DefaultPromptUserFunc(ToolCall toolCall)
        {
            return Task.FromResult("y");
        }

        private static JsonElement ParseToolArguments(string argumentsJson)
        {
            try
            {
                return JsonDocument.Parse(argumentsJson).RootElement.Clone();
            }
            catch (JsonException)
            {
                string repairedJson = argumentsJson.Replace("\\", "\\\\", StringComparison.Ordinal);
                return JsonDocument.Parse(repairedJson).RootElement.Clone();
            }
        }

        #endregion
    }
}
