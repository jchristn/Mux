namespace Mux.Core.Sessions
{
    using System;
    using System.Collections.Generic;
    using Mux.Core.Models;

    /// <summary>
    /// Reconciles two versions of a conversation history — the copy a surface holds in memory (incoming) and the
    /// copy currently persisted in the shared store (existing) — into the single history that should be written.
    /// Every surface persists through this one policy so a save can never silently truncate a conversation.
    ///
    /// <para>The rule, comparing the two as ordered sequences:</para>
    /// <list type="bullet">
    /// <item>No existing history (new or empty session) — take the incoming history.</item>
    /// <item>Existing is a prefix of incoming (the normal case: the surface appended a turn to what it loaded) —
    /// take the incoming history, which is the superset.</item>
    /// <item>Incoming is a strict prefix of existing (the surface's view is stale — the store grew on another
    /// surface after this one loaded) — keep the existing history. This is the anti-truncation guarantee.</item>
    /// <item>The two diverge (a genuine concurrent edit to the same conversation on two surfaces) — take the
    /// incoming history: the surface actively persisting is authoritative for its own turn.</item>
    /// </list>
    ///
    /// <para>Messages are compared by role, textual content, tool-call id, and tool-call count — a stable enough
    /// identity for prefix detection without serializing tool-call arguments. The policy never mutates either
    /// input list; it returns one of them (or a caller-supplied list) by reference.</para>
    /// </summary>
    public static class SessionMergePolicy
    {
        #region Public-Methods

        /// <summary>
        /// Reconciles an incoming conversation history against the one already persisted.
        /// </summary>
        /// <param name="existing">The history currently in the store, or null when none exists.</param>
        /// <param name="incoming">The history the persisting surface holds. Required.</param>
        /// <returns>The history that should be written: never shorter than a version it is a prefix of.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="incoming"/> is null.</exception>
        public static List<ConversationMessage> Reconcile(
            IReadOnlyList<ConversationMessage>? existing,
            List<ConversationMessage> incoming)
        {
            if (incoming == null) throw new ArgumentNullException(nameof(incoming));

            if (existing == null || existing.Count == 0)
            {
                return incoming;
            }

            if (incoming.Count == 0)
            {
                // A surface with nothing loaded must not wipe a populated store.
                return new List<ConversationMessage>(existing);
            }

            if (incoming.Count < existing.Count)
            {
                // Incoming is shorter: keep existing only when incoming is a genuine prefix of it (a stale
                // view). Otherwise the two diverged and the active surface wins.
                return IsPrefixOf(incoming, existing)
                    ? new List<ConversationMessage>(existing)
                    : incoming;
            }

            // Incoming is at least as long: it is the version to persist in both the normal-growth and the
            // divergent cases. (When existing is a prefix of incoming this is plainly correct; when they diverge
            // the active surface is authoritative.)
            return incoming;
        }

        #endregion

        #region Private-Methods

        // True when every message in candidate matches the same-index message in longer (candidate is an
        // ordered prefix). Callers guarantee candidate.Count <= longer.Count.
        private static bool IsPrefixOf(IReadOnlyList<ConversationMessage> candidate, IReadOnlyList<ConversationMessage> longer)
        {
            for (int i = 0; i < candidate.Count; i++)
            {
                if (!SameMessage(candidate[i], longer[i]))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool SameMessage(ConversationMessage left, ConversationMessage right)
        {
            if (left == null || right == null)
            {
                return ReferenceEquals(left, right);
            }

            if (left.Role != right.Role)
            {
                return false;
            }

            if (!string.Equals(left.Content ?? string.Empty, right.Content ?? string.Empty, StringComparison.Ordinal))
            {
                return false;
            }

            if (!string.Equals(left.ToolCallId ?? string.Empty, right.ToolCallId ?? string.Empty, StringComparison.Ordinal))
            {
                return false;
            }

            int leftCalls = left.ToolCalls?.Count ?? 0;
            int rightCalls = right.ToolCalls?.Count ?? 0;
            return leftCalls == rightCalls;
        }

        #endregion
    }
}
