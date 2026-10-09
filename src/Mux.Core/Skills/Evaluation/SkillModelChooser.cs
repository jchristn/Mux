namespace Mux.Core.Skills.Evaluation
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Enums;
    using Mux.Core.Models;
    using Mux.Core.Prompting;

    /// <summary>
    /// Asks a model which skill it would use for a prompt. It sends the skill listing the system prompt carries and the
    /// real <c>skill</c> and <c>run_skill</c> tool definitions, makes one call, and reports the skill named by the first
    /// <c>skill</c> or <c>run_skill</c> call in the reply. Nothing is executed. The model call is a delegate so tests
    /// can script it and the CLI can pass an <c>LlmClient</c>. Thread-safe if the delegate is.
    /// </summary>
    public sealed class SkillModelChooser
    {
        #region Private-Members

        private const string SystemPreamble = "You are mux, a coding agent working in the user's project. When one of the listed skills fits the request, use it through the skill or run_skill tool.";

        private readonly Func<List<ConversationMessage>, List<ToolDefinition>, CancellationToken, Task<ConversationMessage>> _Send;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="SkillModelChooser"/> class.
        /// </summary>
        /// <param name="send">Sends messages and tool definitions to a model and returns its reply.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="send"/> is null.</exception>
        public SkillModelChooser(Func<List<ConversationMessage>, List<ToolDefinition>, CancellationToken, Task<ConversationMessage>> send)
        {
            _Send = send ?? throw new ArgumentNullException(nameof(send));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Builds the system message: the preamble and the skill listing, one <c>- name: description</c> line per skill.
        /// </summary>
        /// <param name="listed">The listed skills.</param>
        /// <returns>The system message text.</returns>
        public static string BuildSystemPrompt(IReadOnlyList<Skill> listed)
        {
            StringBuilder builder = new StringBuilder(SystemPreamble).Append("\n\n");
            builder.Append(PromptResolver.Shared.GetEffective("section.skills")).Append('\n');
            foreach (Skill skill in listed ?? Array.Empty<Skill>())
            {
                builder.Append("- ").Append(skill.Manifest.Name).Append(": ").Append(skill.Manifest.Description).Append('\n');
            }

            return builder.ToString().TrimEnd();
        }

        /// <summary>
        /// The skill tool definitions the model sees.
        /// </summary>
        /// <param name="listed">The listed skills (the tools are only offered when there is at least one).</param>
        /// <returns>The definitions.</returns>
        public static List<ToolDefinition> BuildTools(IReadOnlyList<Skill> listed)
        {
            return new SkillToolProvider(new SkillCatalog(listed ?? Array.Empty<Skill>()), new SkillExecutor()).GetToolDefinitions().ToList();
        }

        /// <summary>
        /// Reads the skill a reply chose: the <c>name</c> argument of the first <c>skill</c> or <c>run_skill</c> call.
        /// <c>skill</c> with the name <c>list</c> is not a choice.
        /// </summary>
        /// <param name="reply">The model's reply.</param>
        /// <returns>The chosen skill name, or null.</returns>
        public static string? ReadChoice(ConversationMessage? reply)
        {
            if (reply?.ToolCalls == null)
            {
                return null;
            }

            foreach (ToolCall call in reply.ToolCalls)
            {
                if (call.Name != "skill" && call.Name != "run_skill")
                {
                    continue;
                }

                try
                {
                    using (JsonDocument document = JsonDocument.Parse(string.IsNullOrWhiteSpace(call.Arguments) ? "{}" : call.Arguments))
                    {
                        if (document.RootElement.ValueKind == JsonValueKind.Object
                            && document.RootElement.TryGetProperty("name", out JsonElement name)
                            && name.ValueKind == JsonValueKind.String)
                        {
                            string chosen = name.GetString()!.Trim();
                            if (chosen.Length > 0 && !string.Equals(chosen, "list", StringComparison.OrdinalIgnoreCase))
                            {
                                return chosen;
                            }
                        }
                    }
                }
                catch (JsonException)
                {
                }
            }

            return null;
        }

        /// <summary>
        /// Asks the model about one case.
        /// </summary>
        /// <param name="evalCase">The case.</param>
        /// <param name="listed">The skills listed for its project.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>The chosen skill as a one-element list, or an empty list when the model chose none.</returns>
        public async Task<List<string>> ChooseAsync(SkillEvalCase evalCase, IReadOnlyList<Skill> listed, CancellationToken cancellationToken)
        {
            if (evalCase == null) throw new ArgumentNullException(nameof(evalCase));

            List<ConversationMessage> messages = new List<ConversationMessage>
            {
                new ConversationMessage { Role = RoleEnum.System, Content = BuildSystemPrompt(listed) },
                new ConversationMessage { Role = RoleEnum.User, Content = evalCase.Prompt }
            };

            ConversationMessage reply = await _Send(messages, BuildTools(listed), cancellationToken).ConfigureAwait(false);
            string? choice = ReadChoice(reply);
            return choice == null ? new List<string>() : new List<string> { choice };
        }

        #endregion
    }
}
