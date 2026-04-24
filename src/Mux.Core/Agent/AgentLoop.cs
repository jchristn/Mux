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
    using Mux.Core.Enums;
    using Mux.Core.Llm;
    using Mux.Core.Models;
    using Mux.Core.Settings;
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
        private const string SyntheticSummaryPrefix = "[mux summary generated automatically; older conversation condensed]";

        private AgentLoopOptions _Options;
        private LlmClient _LlmClient;
        private BuiltInToolRegistry _ToolRegistry;
        private ApprovalHandler _ApprovalHandler;
        private bool _Disposed = false;

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
            _LlmClient = new LlmClient(options.Endpoint);
            _LlmClient.OnRetry = options.OnRetry;
            _ToolRegistry = new BuiltInToolRegistry();
            _ApprovalHandler = new ApprovalHandler(options.ApprovalPolicy);
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

            Stopwatch stopwatch = Stopwatch.StartNew();
            string runId = Guid.NewGuid().ToString("N");
            int iterationCount = 0;
            int toolCallCount = 0;
            int errorCount = 0;
            int assistantTextChars = 0;
            bool maxIterationsReached = false;
            int compactionCount = 0;

            // 1. Build conversation
            List<ConversationMessage> conversation = BuildConversation(prompt);

            // 2. Merge tool definitions
            List<ToolDefinition> allTools = MergeToolDefinitions();
            ContextBudgetSnapshot initialSnapshot = GetContextBudgetSnapshot(conversation, allTools);

            yield return new RunStartedEvent
            {
                RunId = runId,
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
                CompactionStrategy = _Options.CompactionStrategy
            };

            // 3. Enter loop
            for (int step = 0; step < _Options.MaxIterations; step++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                iterationCount = step + 1;

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
                        if (streamEvent is ErrorEvent)
                        {
                            errorCount++;
                        }
                        yield return streamEvent;
                    }
                }

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
                    break;
                }

                // 3e. Process each tool call
                foreach (ToolCall toolCall in proposedToolCalls)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    toolCallCount++;

                    // Yield proposed event
                    yield return new ToolCallProposedEvent { ToolCall = toolCall };

                    // Run through approval
                    bool approved = false;
                    ErrorEvent? approvalError = null;

                    try
                    {
                        Func<ToolCall, Task<string>> promptFunc = _Options.PromptUserFunc
                            ?? DefaultPromptUserFunc;

                        approved = await _ApprovalHandler
                            .RequestApprovalAsync(toolCall, promptFunc)
                            .ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        approvalError = new ErrorEvent
                        {
                            Code = "approval_error",
                            Message = $"Approval check failed: {ex.Message}"
                        };
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
                            Content = JsonSerializer.Serialize(new { error = "tool_call_denied", message = "The user denied this tool call." })
                        });
                        continue;
                    }

                    // Approved
                    yield return new ToolCallApprovedEvent { ToolCallId = toolCall.Id };

                    // Execute the tool
                    ToolResult result;
                    System.Diagnostics.Stopwatch toolStopwatch = System.Diagnostics.Stopwatch.StartNew();

                    try
                    {
                        result = await ExecuteToolCallAsync(toolCall, cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        result = new ToolResult
                        {
                            ToolCallId = toolCall.Id,
                            Success = false,
                            Content = JsonSerializer.Serialize(new { error = "tool_execution_error", message = ex.Message })
                        };
                    }

                    toolStopwatch.Stop();

                    yield return new ToolCallCompletedEvent
                    {
                        ToolCallId = toolCall.Id,
                        ToolName = toolCall.Name,
                        Result = result,
                        ElapsedMs = toolStopwatch.ElapsedMilliseconds
                    };

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

            yield return new RunCompletedEvent
            {
                RunId = runId,
                Status = maxIterationsReached
                    ? "max_iterations_reached"
                    : (errorCount > 0 ? "completed_with_errors" : "completed"),
                IterationsCompleted = iterationCount,
                ToolCallCount = toolCallCount,
                ErrorCount = errorCount,
                AssistantTextChars = assistantTextChars,
                DurationMs = stopwatch.ElapsedMilliseconds,
                FinalEstimatedTokens = finalSnapshot.UsedTokens,
                CompactionCount = compactionCount
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

            return allTools;
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
            ContextCompactedEvent? compactionEvent = TryCompactActiveConversation(
                compactedConversation,
                allTools,
                snapshot,
                cancellationToken,
                out List<ConversationMessage> workingConversation,
                out string failureDetail);

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

        private string GenerateCompactionSummary(List<ConversationMessage> messagesToCompact, CancellationToken cancellationToken)
        {
            string compactableDigest = BuildConversationDigest(messagesToCompact, maxChars: 12000);
            string systemPrompt =
                "You compact older conversation history for a coding agent. " +
                "Preserve goals, constraints, important files, decisions, errors, and unresolved work. " +
                "Return plain text only, concise but information-dense, suitable to carry forward as session memory.";
            string userPrompt =
                $"Compact this older conversation history:{Environment.NewLine}{Environment.NewLine}{compactableDigest}";

            string summary = RunSidecarPromptAsync(systemPrompt, userPrompt, cancellationToken)
                .GetAwaiter()
                .GetResult();

            return summary.Trim();
        }

        private async Task<string> RunSidecarPromptAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken)
        {
            EndpointConfig sidecarEndpoint = CreateSidecarEndpoint();
            using LlmClient client = new LlmClient(sidecarEndpoint);
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
                + "...[conversation truncated]..."
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

            // Check if it is a built-in tool
            if (_ToolRegistry.HasTool(toolCall.Name))
            {
                return await _ToolRegistry
                    .ExecuteAsync(toolCall.Id, toolCall.Name, arguments, _Options.WorkingDirectory, cancellationToken)
                    .ConfigureAwait(false);
            }

            // Try external executor for MCP tools
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
                Content = JsonSerializer.Serialize(new { error = "unknown_tool", message = $"Tool '{toolCall.Name}' is not registered and no external executor is configured." })
            };
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
