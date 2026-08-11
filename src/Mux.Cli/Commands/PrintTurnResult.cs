namespace Mux.Cli.Commands
{
    using System.Collections.Generic;
    using Mux.Core.Models;

    /// <summary>
    /// The outcome of a single print turn (one prompt through the agent loop): its exit code, the final
    /// assistant text, and the full conversation the loop produced. Used to thread history across turns in
    /// <c>--input-format jsonl</c> mode and to drive the artifact and session-persistence steps once the
    /// turn (or the last turn) completes.
    /// </summary>
    public class PrintTurnResult
    {
        #region Public-Members

        /// <summary>
        /// The turn's exit code: 0 success, 1 error, 2 tool call denied.
        /// </summary>
        public int ExitCode { get; set; }

        /// <summary>
        /// The final assistant-visible response text emitted during the turn.
        /// </summary>
        public string AssistantText { get; set; } = string.Empty;

        /// <summary>
        /// The full conversation (system, user, assistant, and tool messages) as it stood when the turn
        /// completed.
        /// </summary>
        public List<ConversationMessage> FinalConversation { get; set; } = new List<ConversationMessage>();

        #endregion
    }
}
