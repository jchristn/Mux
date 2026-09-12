namespace Mux.Core.Agent
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Enums;
    using Mux.Core.Llm;
    using Mux.Core.Models;
    using Mux.Core.Settings;

    /// <summary>
    /// Performs on-demand ("/compact") conversation compaction: summarizes the older turns of a conversation
    /// into a single synthetic summary message and keeps the most recent turns verbatim. It reuses
    /// <see cref="ConversationCompactionPlanner"/> (the same splitter the agent loop's automatic compaction
    /// uses) and the configurable compaction system prompt, so a manual compaction matches an automatic one.
    /// The summary itself is produced by a single, non-streaming, tool-free model call.
    /// </summary>
    public static class ConversationCompactor
    {
        /// <summary>
        /// The marker prefixed to a synthetic summary message, matching the agent loop so subsequent
        /// automatic compactions recognize and re-summarize it correctly.
        /// </summary>
        public const string SummaryPrefix = "[mux summary generated automatically; older conversation condensed]";

        /// <summary>
        /// Assembles the compacted conversation from a plan and a generated summary: a single system summary
        /// message followed by the preserved recent messages. Pure and deterministic (no model call).
        /// </summary>
        /// <param name="plan">The split plan from <see cref="ConversationCompactionPlanner.CreatePlan"/>.</param>
        /// <param name="summary">The summary text of the compacted messages.</param>
        /// <returns>The compacted conversation.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="plan"/> is null.</exception>
        public static List<ConversationMessage> Assemble(ConversationCompactionPlan plan, string summary)
        {
            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            List<ConversationMessage> result = new List<ConversationMessage>
            {
                new ConversationMessage
                {
                    Role = RoleEnum.System,
                    Content = SummaryPrefix + Environment.NewLine + Environment.NewLine + (summary ?? string.Empty).Trim()
                }
            };

            if (plan.MessagesToPreserve != null)
            {
                result.AddRange(plan.MessagesToPreserve);
            }

            return result;
        }

        /// <summary>
        /// Builds a plain-text digest of messages for the summarizer, capped to a character budget.
        /// </summary>
        /// <param name="messages">The messages to digest.</param>
        /// <param name="maxChars">The character budget.</param>
        /// <returns>The digest.</returns>
        public static string BuildDigest(IReadOnlyList<ConversationMessage> messages, int maxChars)
        {
            StringBuilder builder = new StringBuilder();
            if (messages == null)
            {
                return string.Empty;
            }

            foreach (ConversationMessage message in messages)
            {
                if (string.IsNullOrWhiteSpace(message.Content))
                {
                    continue;
                }

                string role = message.Role == RoleEnum.User ? "User" : message.Role == RoleEnum.Assistant ? "Assistant" : "System";
                builder.Append(role).Append(": ").Append(message.Content!.Trim()).Append(Environment.NewLine);
                if (builder.Length >= maxChars)
                {
                    break;
                }
            }

            return builder.ToString();
        }

        /// <summary>
        /// Compacts a conversation by summarizing its older turns. Returns a result indicating whether the
        /// conversation changed, the new history when it did, and a human-readable notice.
        /// </summary>
        /// <param name="history">The conversation history (user/assistant turns).</param>
        /// <param name="endpoint">The endpoint whose model produces the summary. Required.</param>
        /// <param name="compactionSystemPrompt">The compaction system prompt; blank uses the built-in default.</param>
        /// <param name="preserveTurns">How many recent user-led turns to keep verbatim.</param>
        /// <param name="ignoreCertErrors">Whether to bypass TLS validation for the summary call.</param>
        /// <param name="token">A token to cancel the operation.</param>
        /// <returns>The compaction result.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="endpoint"/> is null.</exception>
        public static async Task<CompactionResult> CompactAsync(
            IReadOnlyList<ConversationMessage> history,
            EndpointConfig endpoint,
            string? compactionSystemPrompt,
            int preserveTurns,
            bool ignoreCertErrors,
            CancellationToken token)
        {
            if (endpoint == null)
            {
                throw new ArgumentNullException(nameof(endpoint));
            }

            if (history == null || history.Count == 0)
            {
                return CompactionResult.NothingToDo("There is nothing to compact yet.");
            }

            List<ConversationMessage> filtered = ConversationCompactionPlanner.RemoveSyntheticSummaries(new List<ConversationMessage>(history), SummaryPrefix);
            ConversationCompactionPlan plan = ConversationCompactionPlanner.CreatePlan(filtered, preserveTurns, SummaryPrefix);
            if (!plan.CanCompact)
            {
                return CompactionResult.NothingToDo("The conversation is short enough that there is nothing to compact.");
            }

            string systemPrompt = string.IsNullOrWhiteSpace(compactionSystemPrompt) ? Defaults.CompactionSystemPrompt : compactionSystemPrompt!;
            List<ConversationMessage> messages = new List<ConversationMessage>
            {
                new ConversationMessage { Role = RoleEnum.System, Content = systemPrompt },
                new ConversationMessage
                {
                    Role = RoleEnum.User,
                    Content = "Compact this older conversation history:" + Environment.NewLine + Environment.NewLine + BuildDigest(plan.MessagesToCompact, 12000)
                }
            };

            try
            {
                using LlmClient client = new LlmClient(endpoint, ignoreCertErrors);
                ConversationMessage response = await client.SendAsync(messages, new List<ToolDefinition>(), token).ConfigureAwait(false);
                string summary = (response.Content ?? string.Empty).Trim();
                if (summary.Length == 0)
                {
                    return CompactionResult.Failed("The model returned no summary.");
                }

                return CompactionResult.Ok(Assemble(plan, summary), plan.MessagesToCompact.Count);
            }
            catch (Exception exception)
            {
                return CompactionResult.Failed(exception.Message);
            }
        }
    }

    /// <summary>
    /// The outcome of a <see cref="ConversationCompactor.CompactAsync"/> call.
    /// </summary>
    public sealed class CompactionResult
    {
        private CompactionResult()
        {
        }

        /// <summary>Whether the operation succeeded (no error).</summary>
        public bool Success { get; private set; }

        /// <summary>Whether the conversation was actually compacted (false when there was nothing to do).</summary>
        public bool Compacted { get; private set; }

        /// <summary>The compacted history when <see cref="Compacted"/> is true; otherwise null.</summary>
        public List<ConversationMessage>? History { get; private set; }

        /// <summary>How many messages were folded into the summary.</summary>
        public int CompactedCount { get; private set; }

        /// <summary>A human-readable notice describing the outcome.</summary>
        public string Message { get; private set; } = string.Empty;

        /// <summary>Builds a successful compaction result.</summary>
        /// <param name="history">The compacted history.</param>
        /// <param name="compactedCount">The number of messages summarized.</param>
        /// <returns>The result.</returns>
        public static CompactionResult Ok(List<ConversationMessage> history, int compactedCount)
        {
            return new CompactionResult
            {
                Success = true,
                Compacted = true,
                History = history,
                CompactedCount = compactedCount,
                Message = "Compacted " + compactedCount + " message" + (compactedCount == 1 ? string.Empty : "s") + " into a summary."
            };
        }

        /// <summary>Builds a no-op result (nothing needed compacting).</summary>
        /// <param name="message">The notice.</param>
        /// <returns>The result.</returns>
        public static CompactionResult NothingToDo(string message)
        {
            return new CompactionResult { Success = true, Compacted = false, Message = message };
        }

        /// <summary>Builds a failure result.</summary>
        /// <param name="message">The error notice.</param>
        /// <returns>The result.</returns>
        public static CompactionResult Failed(string message)
        {
            return new CompactionResult { Success = false, Compacted = false, Message = "Compaction failed: " + message };
        }
    }
}
