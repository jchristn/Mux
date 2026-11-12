namespace Mux.Desktop.Services
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// The non-time constraints the desktop usage window applies to every query: an optional set of endpoint
    /// names (the endpoint text filter resolved against the known endpoints), an optional set of session ids
    /// (the conversation text filter resolved against the known conversations), and an optional exact model.
    /// A null set means "any"; an empty set means "nothing matched", which yields empty results.
    /// </summary>
    public sealed class UsageScope
    {
        #region Private-Members

        private readonly IReadOnlyList<string>? _Endpoints;
        private readonly IReadOnlyCollection<string>? _SessionIds;
        private readonly string? _Model;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate a scope.
        /// </summary>
        /// <param name="endpoints">Endpoint names to include; null for any, empty for none.</param>
        /// <param name="sessionIds">Session ids to include; null for any, empty for none.</param>
        /// <param name="model">An exact model name, or null/blank for any.</param>
        public UsageScope(IReadOnlyList<string>? endpoints = null, IReadOnlyCollection<string>? sessionIds = null, string? model = null)
        {
            _Endpoints = endpoints;
            _SessionIds = sessionIds;
            _Model = string.IsNullOrWhiteSpace(model) ? null : model;
        }

        /// <summary>A scope with no constraints.</summary>
        public static UsageScope Any { get; } = new UsageScope();

        #endregion

        #region Public-Members

        /// <summary>Endpoint names to include; null for any, empty when the endpoint filter matched nothing.</summary>
        public IReadOnlyList<string>? Endpoints
        {
            get => _Endpoints;
        }

        /// <summary>Session ids to include; null for any, empty when the conversation filter matched nothing.</summary>
        public IReadOnlyCollection<string>? SessionIds
        {
            get => _SessionIds;
        }

        /// <summary>An exact model name, or null for any.</summary>
        public string? Model
        {
            get => _Model;
        }

        /// <summary>True when a constraint matched nothing, so every query is empty.</summary>
        public bool MatchesNothing
        {
            get => (_Endpoints != null && _Endpoints.Count == 0) || (_SessionIds != null && _SessionIds.Count == 0);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Resolve the endpoint text filter (case-insensitive "contains") against the known endpoint names.
        /// </summary>
        /// <param name="text">The filter text; blank means any endpoint.</param>
        /// <param name="knownEndpoints">Every endpoint name recorded in the store.</param>
        /// <returns>Null for any endpoint, otherwise the matching names (possibly empty), ordered and distinct.</returns>
        public static IReadOnlyList<string>? MatchEndpoints(string? text, IEnumerable<string>? knownEndpoints)
        {
            string needle = (text ?? string.Empty).Trim();
            if (needle.Length == 0)
            {
                return null;
            }

            SortedSet<string> matches = new SortedSet<string>(StringComparer.Ordinal);
            if (knownEndpoints != null)
            {
                foreach (string name in knownEndpoints)
                {
                    if (!string.IsNullOrEmpty(name) && name.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        matches.Add(name);
                    }
                }
            }

            return new List<string>(matches);
        }

        /// <summary>
        /// Resolve the conversation text filter (case-insensitive "contains") against the known conversations,
        /// matching the same label the call table shows: the conversation title, or the first eight characters
        /// of the session id when the conversation has no title. A text equal to a full session id also matches it.
        /// </summary>
        /// <param name="text">The filter text; blank means any conversation.</param>
        /// <param name="conversations">Known conversations: session id to title (title may be blank).</param>
        /// <returns>Null for any conversation, otherwise the matching session ids (possibly empty).</returns>
        public static IReadOnlyCollection<string>? MatchSessions(string? text, IReadOnlyDictionary<string, string>? conversations)
        {
            string needle = (text ?? string.Empty).Trim();
            if (needle.Length == 0)
            {
                return null;
            }

            HashSet<string> matches = new HashSet<string>(StringComparer.Ordinal);
            if (conversations != null)
            {
                foreach (KeyValuePair<string, string> entry in conversations)
                {
                    if (string.IsNullOrEmpty(entry.Key))
                    {
                        continue;
                    }

                    string label = ConversationLabel(entry.Key, entry.Value);
                    if (label.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0
                        || string.Equals(entry.Key, needle, StringComparison.OrdinalIgnoreCase))
                    {
                        matches.Add(entry.Key);
                    }
                }
            }

            return matches;
        }

        /// <summary>
        /// The label the call table shows for a conversation: its title, else the first eight characters of
        /// the session id, else an em dash.
        /// </summary>
        /// <param name="sessionId">The session id, or null.</param>
        /// <param name="title">The resolved title, or null/blank.</param>
        /// <returns>The label.</returns>
        public static string ConversationLabel(string? sessionId, string? title)
        {
            if (!string.IsNullOrWhiteSpace(title))
            {
                return title!;
            }

            if (string.IsNullOrEmpty(sessionId))
            {
                return "—";
            }

            return sessionId!.Length > 8 ? sessionId.Substring(0, 8) : sessionId;
        }

        #endregion
    }
}
