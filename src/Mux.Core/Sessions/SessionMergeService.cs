namespace Mux.Core.Sessions
{
    using System;
    using System.Collections.Generic;
    using Mux.Core.Models;

    /// <summary>
    /// Folds a background job's produced messages into a focused conversation history. Merging is
    /// always an explicit caller action — job transcripts are never merged automatically on completion.
    /// </summary>
    public static class SessionMergeService
    {
        /// <summary>
        /// Returns a new history consisting of <paramref name="focusedHistory"/> followed by
        /// <paramref name="messagesToMerge"/>. Neither input is mutated.
        /// </summary>
        /// <param name="focusedHistory">The current focused history. Must not be null.</param>
        /// <param name="messagesToMerge">The messages to append. Must not be null.</param>
        /// <returns>A new combined history list.</returns>
        /// <exception cref="ArgumentNullException">Thrown when either argument is null.</exception>
        public static List<ConversationMessage> Merge(
            IReadOnlyList<ConversationMessage> focusedHistory,
            IReadOnlyList<ConversationMessage> messagesToMerge)
        {
            if (focusedHistory is null) throw new ArgumentNullException(nameof(focusedHistory));
            if (messagesToMerge is null) throw new ArgumentNullException(nameof(messagesToMerge));

            List<ConversationMessage> merged = new List<ConversationMessage>(focusedHistory.Count + messagesToMerge.Count);
            merged.AddRange(focusedHistory);
            merged.AddRange(messagesToMerge);
            return merged;
        }
    }
}
