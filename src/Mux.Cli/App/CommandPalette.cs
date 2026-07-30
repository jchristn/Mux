namespace Mux.Cli.App
{
    using System;
    using System.Collections.Generic;
    using TUIKit.Input;
    using TUIKit.Widgets;

    /// <summary>
    /// A fuzzy command palette over the command catalog. Wraps a TUIKit <see cref="FuzzyList"/> of
    /// command titles and maps the selection back to its <see cref="CommandDescriptor"/>. The shell
    /// forwards keys while the palette is open, reads <see cref="Selected"/> to run the choice, and binds
    /// <see cref="Widget"/> into a region to render it.
    /// </summary>
    public sealed class CommandPalette
    {
        #region Private-Members

        private readonly Dictionary<string, CommandDescriptor> _ByTitle = new Dictionary<string, CommandDescriptor>(StringComparer.Ordinal);
        private readonly FuzzyList _List;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="CommandPalette"/> class from a catalog snapshot.
        /// </summary>
        /// <param name="catalog">The command catalog to expose. Must not be null.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="catalog"/> is null.</exception>
        public CommandPalette(MuxCommandCatalog catalog)
        {
            if (catalog is null) throw new ArgumentNullException(nameof(catalog));

            List<string> titles = new List<string>();
            foreach (CommandDescriptor descriptor in catalog.Commands)
            {
                if (!_ByTitle.ContainsKey(descriptor.Title))
                {
                    _ByTitle[descriptor.Title] = descriptor;
                    titles.Add(descriptor.Title);
                }
            }

            _List = new FuzzyList(titles);
        }

        #endregion

        #region Public-Members

        /// <summary>
        /// The underlying widget, for binding into a layout region.
        /// </summary>
        public FuzzyList Widget
        {
            get => _List;
        }

        /// <summary>
        /// The current fuzzy query.
        /// </summary>
        public string Query
        {
            get => _List.Query;
        }

        /// <summary>
        /// The number of catalog entries matching the current query.
        /// </summary>
        public int MatchCount
        {
            get => _List.MatchCount;
        }

        /// <summary>
        /// The command matching the current selection, or null when nothing is selected.
        /// </summary>
        public CommandDescriptor? Selected
        {
            get
            {
                string? title = _List.SelectedItem;
                if (title != null && _ByTitle.TryGetValue(title, out CommandDescriptor? descriptor))
                {
                    return descriptor;
                }

                return null;
            }
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Forwards a key event to the underlying fuzzy list (typing, backspace, selection movement).
        /// </summary>
        /// <param name="key">The key event.</param>
        /// <returns>True when the list consumed the key.</returns>
        public bool HandleKey(KeyEvent key)
        {
            return _List.HandleKey(key);
        }

        #endregion
    }
}
