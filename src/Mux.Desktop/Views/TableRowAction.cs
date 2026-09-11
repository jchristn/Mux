namespace Mux.Desktop.Views
{
    using System;

    /// <summary>
    /// A single row action offered by a <see cref="DataTableView{TRow}"/> — surfaced both in the per-row "⋯"
    /// actions menu and in the right-click context menu. The first non-destructive action is also invoked on
    /// double-click.
    /// </summary>
    /// <typeparam name="TRow">The row model type.</typeparam>
    public sealed class TableRowAction<TRow>
    {
        /// <summary>
        /// Instantiate a row action.
        /// </summary>
        /// <param name="label">The menu label.</param>
        /// <param name="invoke">The callback invoked with the row.</param>
        /// <param name="destructive">True to style the action as destructive (e.g. Delete).</param>
        public TableRowAction(string label, Action<TRow> invoke, bool destructive = false)
        {
            Label = label ?? string.Empty;
            Invoke = invoke ?? throw new ArgumentNullException(nameof(invoke));
            Destructive = destructive;
        }

        /// <summary>The menu label.</summary>
        public string Label { get; }

        /// <summary>The callback invoked with the row.</summary>
        public Action<TRow> Invoke { get; }

        /// <summary>Whether the action is destructive.</summary>
        public bool Destructive { get; }
    }
}
