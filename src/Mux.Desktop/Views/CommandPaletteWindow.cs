namespace Mux.Desktop.Views
{
    using System;
    using System.Collections.Generic;
    using Avalonia;
    using Avalonia.Controls;
    using Avalonia.Input;
    using Avalonia.Interactivity;
    using Avalonia.Layout;
    using Avalonia.Media;
    using Mux.Desktop.I18n;

    /// <summary>
    /// A keyboard-driven command palette (parity with the TUI's command palette / <c>Ctrl+K</c>). Type to
    /// filter, Up/Down to move, Enter to run the highlighted command, Esc to dismiss. Returns the chosen
    /// command's action via <c>ShowDialog&lt;Action?&gt;</c> (null when dismissed).
    /// </summary>
    public sealed class CommandPaletteWindow : Window
    {
        private readonly List<PaletteCommand> _Commands;
        private readonly TextBox _Search = new TextBox { PlaceholderText = Localizer.T("palette.search.placeholder"), FontSize = 15 };
        private readonly ListBox _List = new ListBox { Background = Brushes.Transparent };

        /// <summary>
        /// Instantiate the command palette.
        /// </summary>
        /// <param name="commands">The available commands. Required.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="commands"/> is null.</exception>
        public CommandPaletteWindow(List<PaletteCommand> commands)
        {
            _Commands = commands ?? throw new ArgumentNullException(nameof(commands));

            AppTheme theme = AppTheme.Current;

            Title = Localizer.T("palette.title");
            Icon = IconResources.LoadWindowIcon();
            Width = 560;
            Height = 440;
            CanResize = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = theme.Surface;

            _Search.TextChanged += (sender, args) => Filter();
            _Search.AddHandler(InputElement.KeyDownEvent, OnSearchKey, RoutingStrategies.Tunnel);
            _List.DoubleTapped += (sender, args) => RunSelected();

            Content = BuildContent(theme);
            Filter();
        }

        /// <summary>
        /// Focus the search box once shown.
        /// </summary>
        /// <param name="e">The event data.</param>
        protected override void OnOpened(EventArgs e)
        {
            base.OnOpened(e);
            _Search.Focus();
        }

        private Control BuildContent(AppTheme theme)
        {
            DockPanel root = new DockPanel { Margin = new Thickness(16) };

            Border searchHost = new Border
            {
                BorderBrush = theme.Border,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Child = _Search
            };
            DockPanel.SetDock(searchHost, Dock.Top);
            root.Children.Add(searchHost);

            _List.Margin = new Thickness(0, 12, 0, 0);
            root.Children.Add(_List);
            return root;
        }

        private void Filter()
        {
            string query = (_Search.Text ?? string.Empty).Trim();
            AppTheme theme = AppTheme.Current;
            _List.Items.Clear();

            foreach (PaletteCommand command in _Commands)
            {
                if (query.Length > 0 && command.Label.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0
                    && (command.Hint == null || command.Hint.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0))
                {
                    continue;
                }

                StackPanel panel = new StackPanel { Spacing = 1 };
                panel.Children.Add(new TextBlock { Text = command.Label, Foreground = theme.Text, FontWeight = FontWeight.SemiBold });
                if (!string.IsNullOrEmpty(command.Hint))
                {
                    panel.Children.Add(new TextBlock { Text = command.Hint, Foreground = theme.Muted, FontSize = 12 });
                }

                _List.Items.Add(new ListBoxItem { Content = panel, Tag = command, Padding = new Thickness(10, 7, 10, 7) });
            }

            if (_List.Items.Count > 0)
            {
                _List.SelectedIndex = 0;
            }
        }

        private void OnSearchKey(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                RunSelected();
            }
            else if (e.Key == Key.Escape)
            {
                e.Handled = true;
                Close(null);
            }
            else if (e.Key == Key.Down)
            {
                e.Handled = true;
                Move(1);
            }
            else if (e.Key == Key.Up)
            {
                e.Handled = true;
                Move(-1);
            }
        }

        private void Move(int delta)
        {
            int count = _List.Items.Count;
            if (count == 0)
            {
                return;
            }

            int next = _List.SelectedIndex + delta;
            if (next < 0)
            {
                next = 0;
            }
            else if (next >= count)
            {
                next = count - 1;
            }

            _List.SelectedIndex = next;
        }

        private void RunSelected()
        {
            if (_List.SelectedItem is ListBoxItem item && item.Tag is PaletteCommand command)
            {
                Close(command.Invoke);
            }
        }
    }
}
