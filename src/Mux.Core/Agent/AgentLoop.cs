namespace Mux.Core.Agent
{
    using System;
    using System.Collections.Generic;
    using System.Runtime.CompilerServices;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Enums;
    using Mux.Core.Llm;
    using Mux.Core.Models;
    using Mux.Core.Tools;

    /// <summary>
    /// Orchestrates the agent loop: sends messages to the LLM, processes tool calls,
    /// and yields events to the caller as an async stream.
    /// </summary>
    public class AgentLoop : IDisposable
    {
        #region Private-Members

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

            // 1. Build conversation
            List<ConversationMessage> conversation = BuildConversation(prompt);

            // 2. Merge tool definitions
            List<ToolDefinition> allTools = MergeToolDefinitions();

            // 3. Enter loop
            for (int step = 0; step < _Options.MaxIterations; step++)
            {
                cancellationToken.ThrowIfCancellationRequested();

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

                    yield return new ToolCallCompletedEvent
                    {
                        ToolCallId = toolCall.Id,
                        Result = result
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
                    yield return new ErrorEvent
                    {
                        Code = "max_iterations_reached",
                        Message = $"Agent loop reached the maximum of {_Options.MaxIterations} iterations."
                    };
                }
            }
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

        private async Task<ToolResult> ExecuteToolCallAsync(ToolCall toolCall, CancellationToken cancellationToken)
        {
            JsonElement arguments = JsonDocument.Parse(toolCall.Arguments).RootElement;

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

        #endregion
    }
}
