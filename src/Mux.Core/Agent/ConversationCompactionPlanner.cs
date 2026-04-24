namespace Mux.Core.Agent
{
    using System;
    using System.Collections.Generic;
    using Mux.Core.Enums;
    using Mux.Core.Models;

    /// <summary>
    /// Splits persisted conversation history into a compactable prefix and a preserved tail.
    /// </summary>
    public static class ConversationCompactionPlanner
    {
        #region Public-Methods

        /// <summary>
        /// Removes any synthetic summary messages previously inserted by mux.
        /// </summary>
        /// <param name="history">The persisted conversation history.</param>
        /// <param name="syntheticSummaryPrefix">The prefix used to identify synthetic summary messages.</param>
        /// <returns>A history list without synthetic summary messages.</returns>
        public static List<ConversationMessage> RemoveSyntheticSummaries(List<ConversationMessage> history, string syntheticSummaryPrefix)
        {
            if (history == null) throw new ArgumentNullException(nameof(history));

            List<ConversationMessage> result = new List<ConversationMessage>();

            foreach (ConversationMessage message in history)
            {
                if (IsSyntheticSummaryMessage(message, syntheticSummaryPrefix))
                {
                    continue;
                }

                result.Add(message);
            }

            return result;
        }

        /// <summary>
        /// Creates a plan that preserves the most recent user-led turns and selects older
        /// messages for model-based compaction.
        /// </summary>
        /// <param name="history">The persisted conversation history.</param>
        /// <param name="preserveTurns">The number of recent user turns to preserve verbatim.</param>
        /// <param name="syntheticSummaryPrefix">The prefix used to identify synthetic summary messages.</param>
        /// <returns>A compaction plan describing compactable and preserved messages.</returns>
        public static ConversationCompactionPlan CreatePlan(
            List<ConversationMessage> history,
            int preserveTurns,
            string syntheticSummaryPrefix)
        {
            if (history == null) throw new ArgumentNullException(nameof(history));

            int safePreserveTurns = Math.Max(1, preserveTurns);
            List<ConversationMessage> filteredHistory = RemoveSyntheticSummaries(history, syntheticSummaryPrefix);
            List<int> userMessageIndexes = new List<int>();

            for (int i = 0; i < filteredHistory.Count; i++)
            {
                if (filteredHistory[i].Role == RoleEnum.User)
                {
                    userMessageIndexes.Add(i);
                }
            }

            if (userMessageIndexes.Count <= safePreserveTurns)
            {
                return new ConversationCompactionPlan
                {
                    MessagesToCompact = new List<ConversationMessage>(),
                    MessagesToPreserve = filteredHistory
                };
            }

            int splitIndex = userMessageIndexes[userMessageIndexes.Count - safePreserveTurns];

            return new ConversationCompactionPlan
            {
                MessagesToCompact = filteredHistory.GetRange(0, splitIndex),
                MessagesToPreserve = filteredHistory.GetRange(splitIndex, filteredHistory.Count - splitIndex)
            };
        }

        #endregion

        #region Private-Methods

        private static bool IsSyntheticSummaryMessage(ConversationMessage message, string syntheticSummaryPrefix)
        {
            return message.Role == RoleEnum.System
                && !string.IsNullOrWhiteSpace(message.Content)
                && message.Content.StartsWith(syntheticSummaryPrefix, StringComparison.Ordinal);
        }

        #endregion
    }

    /// <summary>
    /// Result of splitting history for compaction.
    /// </summary>
    public class ConversationCompactionPlan
    {
        /// <summary>
        /// Messages that should be summarized or trimmed away.
        /// </summary>
        public List<ConversationMessage> MessagesToCompact { get; set; } = new List<ConversationMessage>();

        /// <summary>
        /// Messages that should remain verbatim after compaction.
        /// </summary>
        public List<ConversationMessage> MessagesToPreserve { get; set; } = new List<ConversationMessage>();

        /// <summary>
        /// Whether the plan has any messages eligible for compaction.
        /// </summary>
        public bool CanCompact => MessagesToCompact.Count > 0;
    }
}
