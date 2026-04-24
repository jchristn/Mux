namespace Mux.Core.Agent
{
    using System;
    using System.Collections.Generic;
    using Mux.Core.Enums;
    using Mux.Core.Models;

    /// <summary>
    /// Performs trim-only conversation compaction while preserving leading system memory
    /// messages and the most recent user-led turns.
    /// </summary>
    public static class ConversationTrimCompactor
    {
        #region Public-Methods

        /// <summary>
        /// Trims the oldest eligible messages until the estimated usage reaches the requested target
        /// or no more history can be removed without violating the preserved-turn policy.
        /// </summary>
        /// <param name="history">The persisted conversation history.</param>
        /// <param name="preserveTurns">The number of recent user-led turns to keep verbatim.</param>
        /// <param name="targetUsedTokens">The desired maximum estimated token usage for the resulting history.</param>
        /// <param name="estimateUsedTokens">A callback that estimates total used tokens for a candidate history.</param>
        /// <returns>The trim result.</returns>
        public static ConversationTrimResult TrimToTarget(
            List<ConversationMessage> history,
            int preserveTurns,
            int targetUsedTokens,
            Func<List<ConversationMessage>, int> estimateUsedTokens)
        {
            return TrimInternal(
                history,
                preserveTurns,
                Math.Max(0, targetUsedTokens),
                estimateUsedTokens,
                trimAllEligible: false);
        }

        /// <summary>
        /// Trims every eligible message outside the preserved region.
        /// </summary>
        /// <param name="history">The persisted conversation history.</param>
        /// <param name="preserveTurns">The number of recent user-led turns to keep verbatim.</param>
        /// <param name="estimateUsedTokens">A callback that estimates total used tokens for a candidate history.</param>
        /// <returns>The trim result.</returns>
        public static ConversationTrimResult TrimAllEligible(
            List<ConversationMessage> history,
            int preserveTurns,
            Func<List<ConversationMessage>, int> estimateUsedTokens)
        {
            return TrimInternal(
                history,
                preserveTurns,
                targetUsedTokens: 0,
                estimateUsedTokens,
                trimAllEligible: true);
        }

        #endregion

        #region Private-Methods

        private static ConversationTrimResult TrimInternal(
            List<ConversationMessage> history,
            int preserveTurns,
            int targetUsedTokens,
            Func<List<ConversationMessage>, int> estimateUsedTokens,
            bool trimAllEligible)
        {
            if (history == null) throw new ArgumentNullException(nameof(history));
            if (estimateUsedTokens == null) throw new ArgumentNullException(nameof(estimateUsedTokens));

            List<ConversationMessage> original = new List<ConversationMessage>(history);
            List<ConversationMessage> result = new List<ConversationMessage>(history);
            int originalUsedTokens = estimateUsedTokens(original);

            int protectedPrefixCount = 0;
            while (protectedPrefixCount < result.Count && result[protectedPrefixCount].Role == RoleEnum.System)
            {
                protectedPrefixCount++;
            }

            List<int> userMessageIndexes = new List<int>();
            for (int i = 0; i < result.Count; i++)
            {
                if (result[i].Role == RoleEnum.User)
                {
                    userMessageIndexes.Add(i);
                }
            }

            int safePreserveTurns = Math.Max(1, preserveTurns);
            if (userMessageIndexes.Count <= safePreserveTurns)
            {
                return BuildResult(original, result, originalUsedTokens, estimateUsedTokens(result), targetUsedTokens, trimAllEligible);
            }

            int splitIndex = userMessageIndexes[userMessageIndexes.Count - safePreserveTurns];
            int removableCount = Math.Max(0, splitIndex - protectedPrefixCount);

            if (removableCount == 0)
            {
                return BuildResult(original, result, originalUsedTokens, estimateUsedTokens(result), targetUsedTokens, trimAllEligible);
            }

            if (trimAllEligible)
            {
                result.RemoveRange(protectedPrefixCount, removableCount);
            }
            else
            {
                int currentUsedTokens = originalUsedTokens;
                int remainingRemovableCount = removableCount;

                while (currentUsedTokens > targetUsedTokens && remainingRemovableCount > 0)
                {
                    result.RemoveAt(protectedPrefixCount);
                    remainingRemovableCount--;
                    currentUsedTokens = estimateUsedTokens(result);
                }
            }

            int finalUsedTokens = estimateUsedTokens(result);
            return BuildResult(original, result, originalUsedTokens, finalUsedTokens, targetUsedTokens, trimAllEligible);
        }

        private static ConversationTrimResult BuildResult(
            List<ConversationMessage> original,
            List<ConversationMessage> result,
            int originalUsedTokens,
            int finalUsedTokens,
            int targetUsedTokens,
            bool trimAllEligible)
        {
            int removedMessageCount = Math.Max(0, original.Count - result.Count);

            return new ConversationTrimResult
            {
                CompactedHistory = result,
                RemovedMessageCount = removedMessageCount,
                UsedTokensBefore = originalUsedTokens,
                UsedTokensAfter = finalUsedTokens,
                ReachedTarget = trimAllEligible || finalUsedTokens <= targetUsedTokens
            };
        }

        #endregion
    }

    /// <summary>
    /// Result of a trim-only compaction pass.
    /// </summary>
    public class ConversationTrimResult
    {
        /// <summary>
        /// The conversation history after trimming.
        /// </summary>
        public List<ConversationMessage> CompactedHistory { get; set; } = new List<ConversationMessage>();

        /// <summary>
        /// The number of removed messages.
        /// </summary>
        public int RemovedMessageCount { get; set; } = 0;

        /// <summary>
        /// The estimated total used tokens before trimming.
        /// </summary>
        public int UsedTokensBefore { get; set; } = 0;

        /// <summary>
        /// The estimated total used tokens after trimming.
        /// </summary>
        public int UsedTokensAfter { get; set; } = 0;

        /// <summary>
        /// Whether the trim operation reached its requested target.
        /// </summary>
        public bool ReachedTarget { get; set; } = false;

        /// <summary>
        /// Whether the trim operation removed any messages.
        /// </summary>
        public bool DidTrim => RemovedMessageCount > 0;
    }
}
