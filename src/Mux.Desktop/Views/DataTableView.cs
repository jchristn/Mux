namespace Mux.Desktop.Views
{
    using System;
    using System.Collections.Generic;
    using Avalonia;
    using Avalonia.Controls;
    using Avalonia.Controls.Primitives;
    using Avalonia.Layout;
    using Avalonia.Media;

    /// <summary>
    /// A lightweight, dependency-free sortable table. Columns are described by <see cref="TableColumn{TRow}"/>
    /// (with optional inline badges); per-row actions are described by <see cref="TableRowAction{TRow}"/> and
    /// surfaced through a per-row "⋯" menu, a right-click context menu, and double-click (which runs the first
    /// non-destructive action). Clicking a sortable header toggles ascending/descending and shows ▲/▼. The
    /// header stays fixed while the body scrolls.
    /// </summary>
    /// <typeparam name="TRow">The row model type.</typeparam>
    public sealed class DataTableView<TRow> : Border
    {
        private readonly List<TableColumn<TRow>> _Columns;
        private readonly Func<TRow, IReadOnlyList<TableRowAction<TRow>>> _Actions;
        private readonly Action<TRow>? _OnRowActivated;
        private readonly List<TRow> _Rows = new List<TRow>();
        private readonly Grid _HeaderGrid = new Grid();
        private readonly StackPanel _Body = new StackPanel();

        private int _SortColumn = -1;
        private bool _Descending;

        /// <summary>
        /// Instantiate the table.
        /// </summary>
        /// <param name="columns">The column descriptors (a trailing actions column is added automatically).</param>
        /// <param name="actions">Projects a row to its available actions.</param>
        /// <param name="onRowActivated">Optional callback run when a row is clicked (e.g. open its editor).</param>
        public DataTableView(List<TableColumn<TRow>> columns, Func<TRow, IReadOnlyList<TableRowAction<TRow>>> actions, Action<TRow>? onRowActivated = null)
        {
            _Columns = columns ?? throw new ArgumentNullException(nameof(columns));
            _Actions = actions ?? throw new ArgumentNullException(nameof(actions));
            _OnRowActivated = onRowActivated;

            AppTheme theme = AppTheme.Current;
            BorderBrush = theme.Border;
            BorderThickness = new Thickness(1);
            CornerRadius = new CornerRadius(8);
            Background = theme.Surface;

            DockPanel root = new DockPanel();

            Border headerHost = new Border
            {
                Background = theme.SurfaceAlt,
                BorderBrush = theme.Border,
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(12, 8, 12, 8),
                Child = _HeaderGrid
            };
            DockPanel.SetDock(headerHost, Dock.Top);
            root.Children.Add(headerHost);

            ScrollViewer scroll = new ScrollViewer
            {
                Content = _Body,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            root.Children.Add(scroll);

            Child = root;
            BuildHeader(theme);
        }

        /// <summary>
        /// Replace the table's rows and re-render (preserving the current sort).
        /// </summary>
        /// <param name="rows">The new rows.</param>
        public void SetRows(IEnumerable<TRow> rows)
        {
            _Rows.Clear();
            if (rows != null)
            {
                _Rows.AddRange(rows);
            }

            ApplySort();
            RenderBody(AppTheme.Current);
        }

        private void BuildHeader(AppTheme theme)
        {
            _HeaderGrid.Children.Clear();
            _HeaderGrid.ColumnDefinitions.Clear();
            DefineColumns(_HeaderGrid);

            for (int c = 0; c < _Columns.Count; c++)
            {
                TableColumn<TRow> column = _Columns[c];
                string arrow = _SortColumn == c ? (_Descending ? "  ▼" : "  ▲") : string.Empty;

                Control headerControl;
                if (column.SortKey != null)
                {
                    int captured = c;
                    Button button = new Button
                    {
                        Content = column.Header + arrow,
                        Background = Brushes.Transparent,
                        Foreground = theme.Muted,
                        BorderThickness = new Thickness(0),
                        Padding = new Thickness(0),
                        FontWeight = FontWeight.SemiBold,
                        HorizontalAlignment = HorizontalAlignment.Left,
                        HorizontalContentAlignment = HorizontalAlignment.Left,
                        Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand)
                    };
                    button.Click += (sender, args) => ToggleSort(captured);
                    headerControl = button;
                }
                else
                {
                    headerControl = new TextBlock { Text = column.Header, Foreground = theme.Muted, FontWeight = FontWeight.SemiBold };
                }

                string headerTip = column.Tooltip ?? (column.SortKey != null ? "Click to sort by " + column.Header : column.Header);
                headerControl.Tip(headerTip);
                Grid.SetColumn(headerControl, c);
                _HeaderGrid.Children.Add(headerControl);
            }

            TextBlock actionsHeader = new TextBlock { Text = string.Empty, Foreground = theme.Muted };
            Grid.SetColumn(actionsHeader, _Columns.Count);
            _HeaderGrid.Children.Add(actionsHeader);
        }

        private void DefineColumns(Grid grid)
        {
            foreach (TableColumn<TRow> column in _Columns)
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = column.Width });
            }

            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
        }

        private void ToggleSort(int column)
        {
            if (_SortColumn == column)
            {
                _Descending = !_Descending;
            }
            else
            {
                _SortColumn = column;
                _Descending = false;
            }

            AppTheme theme = AppTheme.Current;
            BuildHeader(theme);
            ApplySort();
            RenderBody(theme);
        }

        private void ApplySort()
        {
            if (_SortColumn < 0 || _SortColumn >= _Columns.Count)
            {
                return;
            }

            Func<TRow, IComparable>? key = _Columns[_SortColumn].SortKey;
            if (key == null)
            {
                return;
            }

            _Rows.Sort((left, right) =>
            {
                IComparable a = key(left);
                IComparable b = key(right);
                int result;
                if (a == null && b == null)
                {
                    result = 0;
                }
                else if (a == null)
                {
                    result = -1;
                }
                else if (b == null)
                {
                    result = 1;
                }
                else
                {
                    result = a.CompareTo(b);
                }

                return _Descending ? -result : result;
            });
        }

        private void RenderBody(AppTheme theme)
        {
            _Body.Children.Clear();
            if (_Rows.Count == 0)
            {
                _Body.Children.Add(new TextBlock { Text = "Nothing here yet.", Foreground = theme.Muted, Margin = new Thickness(12, 12, 12, 12) });
                return;
            }

            for (int r = 0; r < _Rows.Count; r++)
            {
                _Body.Children.Add(BuildRow(_Rows[r], r, theme));
            }
        }

        private Control BuildRow(TRow row, int index, AppTheme theme)
        {
            Grid grid = new Grid { VerticalAlignment = VerticalAlignment.Center };
            DefineColumns(grid);

            for (int c = 0; c < _Columns.Count; c++)
            {
                TableColumn<TRow> column = _Columns[c];
                Control cell = BuildCell(column, row, theme);
                Grid.SetColumn(cell, c);
                grid.Children.Add(cell);
            }

            Button actionsButton = new Button
            {
                Content = "⋯",
                Background = Brushes.Transparent,
                Foreground = theme.Text,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(6, 0, 6, 0),
                FontSize = 16,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };
            actionsButton.Flyout = BuildFlyout(row, theme);
            actionsButton.Tip("Actions for this row (or right-click the row)");
            Grid.SetColumn(actionsButton, _Columns.Count);
            grid.Children.Add(actionsButton);

            Border rowBorder = new Border
            {
                Background = index % 2 == 1 ? theme.SurfaceAlt : Brushes.Transparent,
                Padding = new Thickness(12, 9, 12, 9),
                Child = grid,
                ContextMenu = BuildContextMenu(row, theme),
                Cursor = _OnRowActivated != null ? new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand) : Avalonia.Input.Cursor.Default
            };

            if (_OnRowActivated != null)
            {
                rowBorder.Tip("Click to open this row for editing; right-click for more actions");
                rowBorder.Tapped += (sender, args) =>
                {
                    // A click on the ⋯ actions button opens its menu; don't also activate the row.
                    if (!actionsButton.IsPointerOver)
                    {
                        _OnRowActivated(row);
                    }
                };
            }

            return rowBorder;
        }

        private Control BuildCell(TableColumn<TRow> column, TRow row, AppTheme theme)
        {
            string text = column.Text(row) ?? string.Empty;
            string? badge = column.Badge?.Invoke(row);
            IBrush foreground = column.Foreground?.Invoke(row) ?? theme.Text;
            TextBlock label = new TextBlock
            {
                Text = text,
                Foreground = foreground,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 0, 10, 0)
            };

            if (!string.IsNullOrEmpty(text))
            {
                label.Tip(text);
            }

            if (string.IsNullOrEmpty(badge))
            {
                return label;
            }

            StackPanel panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
            panel.Children.Add(label);
            panel.Children.Add(new Border
            {
                Background = theme.AccentButton,
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 1, 6, 1),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock { Text = badge, Foreground = theme.AccentText, FontSize = 10 }
            });
            return panel;
        }

        private MenuFlyout BuildFlyout(TRow row, AppTheme theme)
        {
            MenuFlyout flyout = new MenuFlyout();
            foreach (TableRowAction<TRow> action in _Actions(row))
            {
                flyout.Items.Add(BuildMenuItem(action, row, theme));
            }

            return flyout;
        }

        private ContextMenu BuildContextMenu(TRow row, AppTheme theme)
        {
            ContextMenu menu = new ContextMenu();
            foreach (TableRowAction<TRow> action in _Actions(row))
            {
                menu.Items.Add(BuildMenuItem(action, row, theme));
            }

            return menu;
        }

        private MenuItem BuildMenuItem(TableRowAction<TRow> action, TRow row, AppTheme theme)
        {
            MenuItem item = new MenuItem { Header = action.Label };
            if (action.Destructive)
            {
                item.Foreground = theme.Error;
            }

            item.Click += (sender, args) => action.Invoke(row);
            return item;
        }
    }
}
