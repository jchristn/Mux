namespace Mux.Core.Sessions
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Enums;
    using Mux.Core.Llm;
    using Mux.Core.Models;
    using Mux.Core.Prompting;

    /// <summary>
    /// Summarizes a conversation into a short title with one tool-free model call. Shared by the desktop app and the
    /// REST chat path so a conversation gets the same AI title wherever it was started.
    /// </summary>
    public static class SessionTitleGenerator
    {
        #region Public-Members

        /// <summary>
        /// The conversation size, in characters of message content, at which the heuristic first-prompt title is
        /// replaced with an AI summary (once).
        /// </summary>
        public const int SummaryThresholdChars = 250;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Counts the characters of message content in a conversation.
        /// </summary>
        /// <param name="history">The conversation. Null counts as empty.</param>
        /// <returns>The total content length.</returns>
        public static int ConversationCharCount(IReadOnlyList<ConversationMessage>? history)
        {
            if (history == null)
            {
                return 0;
            }

            int total = 0;
            foreach (ConversationMessage message in history)
            {
                if (!string.IsNullOrEmpty(message.Content))
                {
                    total += message.Content!.Length;
                }
            }

            return total;
        }

        /// <summary>
        /// Whether a turn that grew a conversation from <paramref name="charsBefore"/> to <paramref name="charsAfter"/>
        /// characters crosses the summary threshold, so the title should be summarized now.
        /// </summary>
        /// <param name="charsBefore">The content size before the turn.</param>
        /// <param name="charsAfter">The content size after the turn.</param>
        /// <returns>True when the threshold was crossed by this turn.</returns>
        public static bool CrossesThreshold(int charsBefore, int charsAfter)
        {
            return charsBefore < SummaryThresholdChars && charsAfter >= SummaryThresholdChars;
        }

        /// <summary>
        /// Asks the endpoint's model to summarize the conversation into a short title (a single, non-streaming,
        /// tool-free completion). Returns a cleaned title, or null on any failure so callers keep their heuristic title.
        /// </summary>
        /// <param name="endpoint">The endpoint to ask. Null returns null.</param>
        /// <param name="history">The conversation so far.</param>
        /// <param name="ignoreCertErrors">Whether to ignore TLS certificate errors.</param>
        /// <param name="token">A token to cancel the request.</param>
        /// <returns>A short title, or null if one could not be generated.</returns>
        public static async Task<string?> GenerateAsync(EndpointConfig? endpoint, IReadOnlyList<ConversationMessage>? history, bool ignoreCertErrors, CancellationToken token)
        {
            if (endpoint == null || history == null || history.Count == 0)
            {
                return null;
            }

            try
            {
                List<ConversationMessage> messages = new List<ConversationMessage>
                {
                    new ConversationMessage
                    {
                        Role = RoleEnum.System,
                        Content = PromptResolver.Shared.GetEffective("title.system")
                    },
                    new ConversationMessage
                    {
                        Role = RoleEnum.User,
                        Content = PromptResolver.Shared.GetEffective("title.user") + "\n\n" + BuildTitleTranscript(history)
                    }
                };

                using LlmClient client = new LlmClient(endpoint, ignoreCertErrors);
                ConversationMessage response = await client.SendAsync(messages, new List<ToolDefinition>(), token).ConfigureAwait(false);
                return CleanTitle(response.Content);
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Cleans a model's raw title reply: first line only, without quotes or trailing periods, at most 80 characters.
        /// </summary>
        /// <param name="raw">The raw reply.</param>
        /// <returns>The cleaned title, or null when nothing usable remains.</returns>
        public static string? CleanTitle(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return null;
            }

            string title = raw.Trim();
            int newline = title.IndexOfAny(new[] { '\n', '\r' });
            if (newline >= 0)
            {
                title = title.Substring(0, newline).Trim();
            }

            title = title.Trim('"', '\'', '`', ' ').TrimEnd('.', ' ').Trim();
            if (title.Length > 80)
            {
                title = title.Substring(0, 80).Trim();
            }

            return title.Length == 0 ? null : title;
        }

        #endregion

        #region Private-Methods

        private static string BuildTitleTranscript(IReadOnlyList<ConversationMessage> history)
        {
            StringBuilder builder = new StringBuilder();
            foreach (ConversationMessage message in history)
            {
                if (string.IsNullOrWhiteSpace(message.Content))
                {
                    continue;
                }

                string role = message.Role == RoleEnum.User ? "User" : message.Role == RoleEnum.Assistant ? "Assistant" : "System";
                builder.Append(role).Append(": ").Append(message.Content!.Trim()).Append('\n');
                if (builder.Length > 4000)
                {
                    break;
                }
            }

            return builder.ToString();
        }

        #endregion
    }
}
