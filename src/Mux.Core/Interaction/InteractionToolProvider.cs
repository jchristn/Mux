namespace Mux.Core.Interaction
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Models;
    using Mux.Core.Tools;

    /// <summary>
    /// The tools through which the model talks to the user mid-turn: <c>ask_user</c> (a multiple-choice question,
    /// always offered) and <c>exit_plan</c> (present a plan for approval, offered only in plan mode). Both are
    /// read-only and never need approval themselves. The surface supplies the callbacks that show the question or the
    /// plan; without one (for example in <c>mux print</c>), <c>ask_user</c> tells the model to choose a sensible
    /// default and <c>exit_plan</c> records the plan as the result.
    /// </summary>
    public sealed class InteractionToolProvider : IExternalToolProvider
    {
        #region Private-Members

        private readonly Func<AskUserRequest, CancellationToken, Task<AskUserResponse>>? _AskUser;
        private readonly Func<PlanProposal, CancellationToken, Task<PlanReview>>? _ReviewPlan;
        private readonly bool _PlanMode;
        private readonly object _Sync = new object();
        private PlanProposal? _LastProposal;
        private PlanReview? _LastReview;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="InteractionToolProvider"/> class.
        /// </summary>
        /// <param name="askUser">Shows a question to the user, or null when no user is available.</param>
        /// <param name="reviewPlan">Shows a plan for approval, or null when no user is available.</param>
        /// <param name="planMode">Whether the run is in plan mode (offers <c>exit_plan</c>).</param>
        public InteractionToolProvider(
            Func<AskUserRequest, CancellationToken, Task<AskUserResponse>>? askUser,
            Func<PlanProposal, CancellationToken, Task<PlanReview>>? reviewPlan,
            bool planMode)
        {
            _AskUser = askUser;
            _ReviewPlan = reviewPlan;
            _PlanMode = planMode;
        }

        #endregion

        #region Public-Members

        /// <summary>The question tool.</summary>
        public const string AskUserToolName = "ask_user";

        /// <summary>The plan-approval tool.</summary>
        public const string ExitPlanToolName = "exit_plan";

        /// <summary>The answer given when no user can respond.</summary>
        public const string NoUserMessage = "No user is available to answer; choose a sensible default, state the assumption, and continue.";

        /// <summary>
        /// The system-prompt section added in plan mode.
        /// </summary>
        public const string PlanModeGuidance = "\n\n# Plan mode\n\nYou are in plan mode. Explore and read freely, but do not change anything: tools that write files, run processes, or otherwise mutate state are unavailable. Investigate until you understand the task, then call exit_plan once with a concise Markdown plan (what will change, where, and how it will be verified) and a short ordered list of steps. Do not start the work; it begins only after the user approves the plan.";

        /// <inheritdoc/>
        public string Name => "interaction";

        /// <summary>The most recent plan passed to <c>exit_plan</c>, or null.</summary>
        public PlanProposal? LastProposal
        {
            get { lock (_Sync) { return _LastProposal; } }
        }

        /// <summary>The user's review of <see cref="LastProposal"/>, or null when none was given.</summary>
        public PlanReview? LastReview
        {
            get { lock (_Sync) { return _LastReview; } }
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc/>
        public IReadOnlyList<ToolDefinition> GetToolDefinitions()
        {
            List<ToolDefinition> tools = new List<ToolDefinition>
            {
                new ToolDefinition
                {
                    Name = AskUserToolName,
                    Description = "Ask the user a multiple-choice question when a decision is genuinely theirs and cannot be settled from the request, the code, or a sensible default. Give 2 to 4 short options; the user can always type another answer instead.",
                    ParametersSchema = new
                    {
                        type = "object",
                        properties = new
                        {
                            question = new { type = "string", description = "The question, ending with a question mark." },
                            options = new
                            {
                                type = "array",
                                minItems = 2,
                                maxItems = 4,
                                description = "2 to 4 distinct choices.",
                                items = new
                                {
                                    type = "object",
                                    properties = new
                                    {
                                        label = new { type = "string", description = "A short label (1 to 5 words)." },
                                        description = new { type = "string", description = "What choosing it means." }
                                    },
                                    required = new[] { "label" }
                                }
                            },
                            multi_select = new { type = "boolean", description = "True to let the user pick several options." }
                        },
                        required = new[] { "question", "options" }
                    }
                }
            };

            if (_PlanMode)
            {
                tools.Add(new ToolDefinition
                {
                    Name = ExitPlanToolName,
                    Description = "End plan mode by presenting your plan to the user for approval. Call it once, when the plan is complete. If the user asks for changes, revise and call it again.",
                    ParametersSchema = new
                    {
                        type = "object",
                        properties = new
                        {
                            plan = new { type = "string", description = "The plan in Markdown: what will change, where, and how it will be verified." },
                            steps = new { type = "array", description = "Optional short, ordered steps; they become the task list once approved.", items = new { type = "string" } }
                        },
                        required = new[] { "plan" }
                    }
                });
            }

            return tools;
        }

        /// <inheritdoc/>
        public bool HasTool(string toolName)
        {
            return string.Equals(toolName, AskUserToolName, StringComparison.OrdinalIgnoreCase)
                || (_PlanMode && string.Equals(toolName, ExitPlanToolName, StringComparison.OrdinalIgnoreCase));
        }

        /// <inheritdoc/>
        public ToolMutationKind GetMutationKind(string toolName)
        {
            return ToolMutationKind.ReadOnly;
        }

        /// <inheritdoc/>
        public async Task<ToolResult> ExecuteAsync(string toolName, JsonElement arguments, string workingDirectory, CancellationToken cancellationToken)
        {
            if (string.Equals(toolName, AskUserToolName, StringComparison.OrdinalIgnoreCase))
            {
                return await AskAsync(toolName, arguments, cancellationToken).ConfigureAwait(false);
            }

            if (string.Equals(toolName, ExitPlanToolName, StringComparison.OrdinalIgnoreCase))
            {
                if (!_PlanMode)
                {
                    return Result(toolName, false, new { error = "not_in_plan_mode", message = "exit_plan is only available in plan mode." });
                }

                return await ExitPlanAsync(toolName, arguments, cancellationToken).ConfigureAwait(false);
            }

            return Result(toolName, false, new { error = "unknown_tool", message = "'" + toolName + "' is not an interaction tool." });
        }

        /// <summary>
        /// Parses and validates <c>ask_user</c> arguments.
        /// </summary>
        /// <param name="arguments">The tool arguments.</param>
        /// <param name="request">The parsed request.</param>
        /// <param name="error">Why the arguments are invalid, or empty.</param>
        /// <returns>True when the arguments are valid.</returns>
        public static bool TryParseAskUser(JsonElement arguments, out AskUserRequest request, out string error)
        {
            request = new AskUserRequest();
            error = string.Empty;
            if (arguments.ValueKind != JsonValueKind.Object)
            {
                error = "Arguments must be an object with question and options.";
                return false;
            }

            request.Question = ReadString(arguments, "question").Trim();
            if (request.Question.Length == 0)
            {
                error = "question is required.";
                return false;
            }

            if (!arguments.TryGetProperty("options", out JsonElement options) || options.ValueKind != JsonValueKind.Array)
            {
                error = "options must be an array of 2 to 4 choices.";
                return false;
            }

            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (JsonElement option in options.EnumerateArray())
            {
                AskUserOption parsed = new AskUserOption();
                if (option.ValueKind == JsonValueKind.String)
                {
                    parsed.Label = (option.GetString() ?? string.Empty).Trim();
                }
                else if (option.ValueKind == JsonValueKind.Object)
                {
                    parsed.Label = ReadString(option, "label").Trim();
                    parsed.Description = ReadString(option, "description").Trim();
                }

                if (parsed.Label.Length == 0)
                {
                    error = "Every option needs a label.";
                    return false;
                }

                if (!seen.Add(parsed.Label))
                {
                    error = "Option labels must be distinct ('" + parsed.Label + "' repeats).";
                    return false;
                }

                request.Options.Add(parsed);
            }

            if (request.Options.Count < 2 || request.Options.Count > 4)
            {
                error = "options must have 2 to 4 choices (got " + request.Options.Count + ").";
                return false;
            }

            request.MultiSelect = arguments.TryGetProperty("multi_select", out JsonElement multi) && multi.ValueKind == JsonValueKind.True;
            return true;
        }

        #endregion

        #region Private-Methods

        private async Task<ToolResult> AskAsync(string toolName, JsonElement arguments, CancellationToken cancellationToken)
        {
            if (!TryParseAskUser(arguments, out AskUserRequest request, out string error))
            {
                return Result(toolName, false, new { error = "invalid_arguments", message = error });
            }

            if (_AskUser == null)
            {
                return Result(toolName, true, new { answered = false, message = NoUserMessage });
            }

            AskUserResponse response;
            try
            {
                response = await _AskUser(request, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return Result(toolName, true, new { answered = false, timed_out = true, message = "The question timed out without an answer; choose a sensible default, state the assumption, and continue." });
            }
            catch (TimeoutException)
            {
                return Result(toolName, true, new { answered = false, timed_out = true, message = "The question timed out without an answer; choose a sensible default, state the assumption, and continue." });
            }

            if (response == null || response.Dismissed || (response.Selected.Count == 0 && string.IsNullOrWhiteSpace(response.OtherText)))
            {
                return Result(toolName, true, new { answered = false, dismissed = true, message = "The user dismissed the question without answering. Do not ask again; choose a sensible default, state the assumption, and continue." });
            }

            List<string> valid = new List<string>();
            foreach (string label in response.Selected)
            {
                AskUserOption? match = request.Options.Find(o => string.Equals(o.Label, label, StringComparison.OrdinalIgnoreCase));
                if (match != null && !valid.Contains(match.Label))
                {
                    valid.Add(match.Label);
                }
            }

            if (!request.MultiSelect && valid.Count > 1)
            {
                valid = valid.GetRange(0, 1);
            }

            string? other = string.IsNullOrWhiteSpace(response.OtherText) ? null : response.OtherText!.Trim();
            return Result(toolName, true, new { answered = true, question = request.Question, answers = valid, other });
        }

        private async Task<ToolResult> ExitPlanAsync(string toolName, JsonElement arguments, CancellationToken cancellationToken)
        {
            PlanProposal proposal = new PlanProposal { Plan = arguments.ValueKind == JsonValueKind.Object ? ReadString(arguments, "plan").Trim() : string.Empty };
            if (proposal.Plan.Length == 0)
            {
                return Result(toolName, false, new { error = "invalid_arguments", message = "plan is required: describe what will change, where, and how it will be verified." });
            }

            if (arguments.TryGetProperty("steps", out JsonElement steps) && steps.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement step in steps.EnumerateArray())
                {
                    string text = step.ValueKind == JsonValueKind.String ? (step.GetString() ?? string.Empty).Trim() : string.Empty;
                    if (text.Length > 0 && proposal.Steps.Count < 50)
                    {
                        proposal.Steps.Add(text.Length > 200 ? text.Substring(0, 200) : text);
                    }
                }
            }

            lock (_Sync)
            {
                _LastProposal = proposal;
                _LastReview = null;
            }

            if (_ReviewPlan == null)
            {
                return Result(toolName, true, new { recorded = true, approved = false, message = "No user is available to review the plan; it was recorded as the result. Stop here and do not carry it out." });
            }

            PlanReview review;
            try
            {
                review = await _ReviewPlan(proposal, cancellationToken).ConfigureAwait(false) ?? new PlanReview();
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                review = new PlanReview { Decision = PlanReviewDecisionEnum.KeepPlanning, Feedback = "The review timed out." };
            }

            lock (_Sync)
            {
                _LastReview = review;
            }

            if (review.Approved)
            {
                return Result(toolName, true, new
                {
                    approved = true,
                    auto_accept = review.Decision == PlanReviewDecisionEnum.ApproveAutoAccept,
                    message = "The user approved the plan. Stop now with a one-line confirmation; execution starts in the next turn."
                });
            }

            string feedback = string.IsNullOrWhiteSpace(review.Feedback) ? "(no specific feedback)" : review.Feedback.Trim();
            return Result(toolName, true, new
            {
                approved = false,
                feedback,
                message = "The user wants changes before approving: " + feedback + " Revise the plan (keep exploring if needed) and call exit_plan again."
            });
        }

        private static string ReadString(JsonElement element, string name)
        {
            return element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? string.Empty
                : string.Empty;
        }

        private static ToolResult Result(string toolName, bool success, object payload)
        {
            return new ToolResult { ToolCallId = toolName, Success = success, Content = JsonSerializer.Serialize(payload) };
        }

        #endregion
    }
}
