namespace Mux.Core.Interaction
{
    using System;
    using System.Collections.Generic;

    /// <summary>Matches slash aliases while the user edits the leading command token.</summary>
    public static class SlashCommandCompletion
    {
        /// <summary>Matches a configured value after a slash command.</summary>
        public static List<string> MatchArgument(string text, int caret, string command, IEnumerable<string> values, int limit = 12)
        {
            List<string> matches = new List<string>();
            if (string.IsNullOrEmpty(text) || caret != text.Length || limit < 1 || text.IndexOfAny(new[] { '\r', '\n' }) >= 0) return matches;
            int space = text.IndexOfAny(new[] { ' ', '\t' });
            if (space < 0 || !text.Substring(0, space).Equals("/" + command, StringComparison.OrdinalIgnoreCase)) return matches;
            string prefix = text.Substring(space + 1).TrimStart();
            if (prefix.Length > 0 && char.IsWhiteSpace(prefix[prefix.Length - 1])) return matches;
            SortedSet<string> names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string value in values)
                if (!string.IsNullOrWhiteSpace(value) && value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) names.Add(value);
            foreach (string name in names)
            {
                matches.Add("/" + command + " " + name);
                if (matches.Count >= limit) break;
            }
            return matches;
        }

        /// <summary>Returns distinct, sorted slash commands matching an unfinished command token.</summary>
        /// <param name="text">The composer text.</param>
        /// <param name="caret">The caret offset.</param>
        /// <param name="aliases">Public aliases supported by the current client, without the slash.</param>
        /// <param name="limit">Maximum number of suggestions.</param>
        /// <returns>Matching slash commands. Arguments and ordinary prompts produce no matches.</returns>
        public static List<string> Match(string text, int caret, IEnumerable<string> aliases, int limit = 12)
        {
            List<string> matches = new List<string>();
            if (string.IsNullOrEmpty(text) || caret != text.Length || text[0] != '/' || limit < 1) return matches;
            for (int i = 1; i < text.Length; i++) if (char.IsWhiteSpace(text[i])) return matches;
            string prefix = text.Substring(1);
            SortedSet<string> names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string alias in aliases)
                if (!string.IsNullOrWhiteSpace(alias) && alias.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) names.Add(alias);
            foreach (string name in names)
            {
                matches.Add("/" + name);
                if (matches.Count >= limit) break;
            }
            return matches;
        }
    }
}
