namespace Mux.Core.Tools.Tools
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Models;
    using Mux.Core.Subagents;
    using Mux.Core.Tools;

    /// <summary>
    /// Delegates a self-contained sub-task to a named subagent that runs in an isolated conversation and
    /// returns only its final answer. The model calls this to hand off scoped work — a focused review, a
    /// bounded search, a mechanical transformation — without polluting its own context with the child's
    /// intermediate steps. Read-only with respect to the workspace write lease itself: the tool does not
    /// take the lease, so the child's own mutating tool calls serialize through it normally rather than
    /// deadlocking against a lease the parent tool call would otherwise hold.
    /// </summary>
    public sealed class SpawnSubagentTool : IToolExecutor
    {
        #region Private-Members

        private readonly SubagentRegistry _Registry;
        private readonly ISubagentExecutor _Executor;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="SpawnSubagentTool"/> class.
        /// </summary>
        /// <param name="registry">The registry of available subagents. Must not be null.</param>
        /// <param name="executor">The executor that runs a subagent. Must not be null.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public SpawnSubagentTool(SubagentRegistry registry, ISubagentExecutor executor)
        {
            _Registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _Executor = executor ?? throw new ArgumentNullException(nameof(executor));
        }

        #endregion

        #region Public-Members

        /// <summary>
        /// The unique name of this tool.
        /// </summary>
        public string Name => "spawn_subagent";

        /// <summary>
        /// A human-readable description of what this tool does, including the available subagents so the
        /// model can pick one by name.
        /// </summary>
        public string Description
        {
            get
            {
                StringBuilder builder = new StringBuilder();
                builder.Append("Delegates a self-contained sub-task to a named subagent that runs in its own isolated ");
                builder.Append("conversation and returns only its final answer. Use this to hand off focused work ");
                builder.Append("(a review, a scoped search, a mechanical change) so your own context stays clean. ");
                builder.Append("Available subagents:");
                foreach (SubagentDefinition definition in _Registry.Definitions)
                {
                    builder.Append(' ');
                    builder.Append(definition.Name);
                    if (!string.IsNullOrWhiteSpace(definition.Description))
                    {
                        builder.Append(" (").Append(definition.Description).Append(')');
                    }

                    builder.Append(';');
                }

                return builder.ToString();
            }
        }

        /// <summary>
        /// The JSON Schema object describing the tool's input parameters.
        /// </summary>
        public object ParametersSchema => new
        {
            type = "object",
            properties = new
            {
                subagent = new
                {
                    type = "string",
                    description = "The name of the subagent to run, chosen from the available subagents."
                },
                prompt = new
                {
                    type = "string",
                    description = "The complete, self-contained task for the subagent. It does not see this conversation, so include all needed context."
                }
            },
            required = new[] { "subagent", "prompt" }
        };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Executes the spawn_subagent tool: resolves the requested subagent and runs it in isolation.
        /// </summary>
        /// <param name="toolCallId">The unique identifier for this tool call.</param>
        /// <param name="arguments">The parsed JSON arguments containing the subagent name and prompt.</param>
        /// <param name="workingDirectory">The working directory for the child run's tools.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>A <see cref="ToolResult"/> carrying the subagent's final answer or an error.</returns>
        public async Task<ToolResult> ExecuteAsync(string toolCallId, JsonElement arguments, string workingDirectory, CancellationToken cancellationToken)
        {
            string name = GetString(arguments, "subagent");
            string prompt = GetString(arguments, "prompt");

            if (string.IsNullOrWhiteSpace(name))
            {
                return Error(toolCallId, "missing_subagent", "The 'subagent' argument is required.");
            }

            if (string.IsNullOrWhiteSpace(prompt))
            {
                return Error(toolCallId, "missing_prompt", "The 'prompt' argument is required.");
            }

            SubagentDefinition? definition = _Registry.Find(name);
            if (definition == null)
            {
                List<string> available = new List<string>();
                foreach (SubagentDefinition candidate in _Registry.Definitions)
                {
                    available.Add(candidate.Name);
                }

                return new ToolResult
                {
                    ToolCallId = toolCallId,
                    Success = false,
                    Content = JsonSerializer.Serialize(new { error = "unknown_subagent", message = $"No subagent named '{name}'.", available })
                };
            }

            SubagentResult result;
            try
            {
                result = await _Executor.ExecuteAsync(definition, prompt, workingDirectory, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return Error(toolCallId, "subagent_failed", ex.Message);
            }

            if (result == null)
            {
                return Error(toolCallId, "subagent_failed", "The subagent produced no result.");
            }

            return new ToolResult
            {
                ToolCallId = toolCallId,
                Success = result.Success,
                Content = JsonSerializer.Serialize(new
                {
                    ok = result.Success,
                    subagent = definition.Name,
                    iterations = result.Iterations,
                    result = result.FinalText,
                    error = result.Error
                })
            };
        }

        #endregion

        #region Private-Methods

        private static string GetString(JsonElement element, string propertyName)
        {
            if (element.ValueKind == JsonValueKind.Object
                && element.TryGetProperty(propertyName, out JsonElement value)
                && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString() ?? string.Empty;
            }

            return string.Empty;
        }

        private static ToolResult Error(string toolCallId, string code, string message)
        {
            return new ToolResult
            {
                ToolCallId = toolCallId,
                Success = false,
                Content = JsonSerializer.Serialize(new { error = code, message })
            };
        }

        #endregion
    }
}
