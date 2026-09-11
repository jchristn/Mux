namespace Mux.Desktop.Views
{
    using System;
    using Avalonia.Controls;
    using Avalonia.Media;

    /// <summary>
    /// Describes a single column of a <see cref="DataTableView{TRow}"/>: its header, how to render a row's
    /// cell text, an optional inline "badge" (a green pill drawn after the text, e.g. <c>default</c> or
    /// <c>active</c>), an optional per-cell foreground, an optional sort key (columns with a sort key are
    /// clickable to sort), and its width.
    /// </summary>
    /// <typeparam name="TRow">The row model type.</typeparam>
    public sealed class TableColumn<TRow>
    {
        /// <summary>
        /// Instantiate a column.
        /// </summary>
        /// <param name="header">The column header text.</param>
        /// <param name="text">Projects a row to its cell text.</param>
        /// <param name="width">The column width (fixed or star).</param>
        /// <param name="sortKey">Optional projection to a sortable key; null makes the column non-sortable.</param>
        /// <param name="badge">Optional projection to inline badge text; null or empty draws no badge.</param>
        /// <param name="foreground">Optional projection to a cell foreground brush; null uses the default text color.</param>
        /// <param name="tooltip">Optional descriptive tooltip for the column header.</param>
        public TableColumn(string header, Func<TRow, string> text, GridLength width, Func<TRow, IComparable>? sortKey = null, Func<TRow, string?>? badge = null, Func<TRow, IBrush?>? foreground = null, string? tooltip = null)
        {
            Header = header ?? string.Empty;
            Text = text ?? throw new ArgumentNullException(nameof(text));
            Width = width;
            SortKey = sortKey;
            Badge = badge;
            Foreground = foreground;
            Tooltip = tooltip;
        }

        /// <summary>The column header text.</summary>
        public string Header { get; }

        /// <summary>Projects a row to its cell text.</summary>
        public Func<TRow, string> Text { get; }

        /// <summary>Optional projection to inline badge text drawn after the cell text.</summary>
        public Func<TRow, string?>? Badge { get; }

        /// <summary>Optional projection to a sortable key; null makes the column non-sortable.</summary>
        public Func<TRow, IComparable>? SortKey { get; }

        /// <summary>Optional projection to a cell foreground brush; null uses the default text color.</summary>
        public Func<TRow, IBrush?>? Foreground { get; }

        /// <summary>Optional descriptive tooltip for the column header.</summary>
        public string? Tooltip { get; }

        /// <summary>The column width.</summary>
        public GridLength Width { get; }
    }
}
