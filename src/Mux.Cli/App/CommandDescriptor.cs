namespace Mux.Cli.App
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// A single command in the mux command catalog: a stable id, a human-readable title, an optional
    /// category (for menu grouping), an optional key chord (TUIKit syntax, e.g. <c>"ctrl+q"</c>),
    /// optional slash aliases (without the leading <c>/</c>), and the handler to run. The catalog is the
    /// single source consumed by keybindings, the menu bar, the command palette, and the slash router.
    /// </summary>
    public sealed class CommandDescriptor
    {
        #region Public-Members

        /// <summary>
        /// The stable command identifier.
        /// </summary>
        public string Id { get; }

        /// <summary>
        /// The human-readable command title.
        /// </summary>
        public string Title { get; }

        /// <summary>
        /// The menu/category grouping label (e.g. "Session", "View"), or empty when uncategorized.
        /// </summary>
        public string Category { get; }

        /// <summary>
        /// The key chord that triggers the command (TUIKit chord syntax), or null for no binding.
        /// </summary>
        public string? Chord { get; }

        /// <summary>
        /// Slash aliases for this command (without the leading <c>/</c>), matched case-insensitively.
        /// </summary>
        public IReadOnlyList<string> SlashAliases { get; }

        /// <summary>
        /// The handler invoked when the command runs.
        /// </summary>
        public Action Handler { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="CommandDescriptor"/> class.
        /// </summary>
        /// <param name="id">The stable command id. Must not be null or empty.</param>
        /// <param name="title">The human-readable title. Must not be null.</param>
        /// <param name="chord">The optional key chord (e.g. "ctrl+q"); null for no keybinding.</param>
        /// <param name="handler">The handler to run. Must not be null.</param>
        /// <param name="category">Optional menu grouping label.</param>
        /// <param name="slashAliases">Optional slash aliases (without the leading slash).</param>
        /// <exception cref="ArgumentException">Thrown when <paramref name="id"/> is null or empty.</exception>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="handler"/> is null.</exception>
        public CommandDescriptor(
            string id,
            string title,
            string? chord,
            Action handler,
            string? category = null,
            IEnumerable<string>? slashAliases = null)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentException("Command id cannot be null or empty.", nameof(id));
            Id = id;
            Title = title ?? string.Empty;
            Category = string.IsNullOrWhiteSpace(category) ? string.Empty : category;
            Chord = string.IsNullOrWhiteSpace(chord) ? null : chord;
            Handler = handler ?? throw new ArgumentNullException(nameof(handler));

            List<string> aliases = new List<string>();
            if (slashAliases != null)
            {
                foreach (string alias in slashAliases)
                {
                    string trimmed = (alias ?? string.Empty).Trim().TrimStart('/');
                    if (trimmed.Length > 0 && !aliases.Contains(trimmed, StringComparer.OrdinalIgnoreCase))
                    {
                        aliases.Add(trimmed);
                    }
                }
            }

            SlashAliases = aliases;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Determines whether the given slash token (without the leading slash) matches one of this
        /// command's aliases, case-insensitively.
        /// </summary>
        /// <param name="token">The command token to test.</param>
        /// <returns>True when the token matches an alias.</returns>
        public bool MatchesSlash(string token)
        {
            if (string.IsNullOrEmpty(token)) return false;
            foreach (string alias in SlashAliases)
            {
                if (string.Equals(alias, token, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        #endregion
    }
}
