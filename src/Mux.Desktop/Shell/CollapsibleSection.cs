namespace Mux.Desktop.Shell
{
    using System;
    using Avalonia;
    using Avalonia.Controls;
    using Avalonia.Input;
    using Avalonia.Layout;
    using Avalonia.Media;

    /// <summary>
    /// A compact, theme-aware collapsible section: a small clickable header row with a chevron, and a body
    /// that shows or hides. Used for the "Thinking" panel and tool-call cards. Deliberately lightweight — no
    /// heavy control chrome or tall header — so it reads quietly in the transcript.
    /// </summary>
    public sealed class CollapsibleSection
    {
        private readonly TextBlock _Chevron;
        private readonly TextBlock _HeaderLabel;
        private readonly StackPanel _Body;
        private readonly StackPanel _Root;
        private bool _Expanded;

        /// <summary>
        /// Build a collapsible section.
        /// </summary>
        /// <param name="header">The header text. Required.</param>
        /// <param name="theme">The active palette. Required.</param>
        /// <param name="expanded">Whether the body starts expanded.</param>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
        public CollapsibleSection(string header, AppTheme theme, bool expanded = false)
        {
            ArgumentNullException.ThrowIfNull(header);
            ArgumentNullException.ThrowIfNull(theme);

            _Expanded = expanded;

            FontFamily mono = new FontFamily("Cascadia Mono,Consolas,Menlo,monospace");

            _Chevron = new TextBlock
            {
                Text = expanded ? "▾" : "▸",
                Foreground = theme.Muted,
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0)
            };
            _HeaderLabel = new TextBlock
            {
                Text = header,
                Foreground = theme.Muted,
                FontSize = 12,
                FontFamily = mono,
                VerticalAlignment = VerticalAlignment.Center
            };

            StackPanel headerRow = new StackPanel { Orientation = Orientation.Horizontal };
            headerRow.Children.Add(_Chevron);
            headerRow.Children.Add(_HeaderLabel);

            Border headerBorder = new Border
            {
                Padding = new Thickness(6, 3, 6, 3),
                Background = Brushes.Transparent,
                Cursor = new Cursor(StandardCursorType.Hand),
                Child = headerRow
            };
            headerBorder.PointerPressed += (sender, args) => ToggleExpanded();

            _Body = new StackPanel { Spacing = 6, Margin = new Thickness(18, 2, 0, 4), IsVisible = expanded };

            _Root = new StackPanel { Margin = new Thickness(0, 0, 60, 0), HorizontalAlignment = HorizontalAlignment.Left };
            _Root.Children.Add(headerBorder);
            _Root.Children.Add(_Body);
        }

        /// <summary>The control to add to the transcript.</summary>
        public Control Root
        {
            get => _Root;
        }

        /// <summary>The body panel; add content controls here.</summary>
        public StackPanel Body
        {
            get => _Body;
        }

        /// <summary>
        /// Update the header text and optional color.
        /// </summary>
        /// <param name="text">The new header text.</param>
        /// <param name="color">Optional new header color; null keeps the current color.</param>
        public void SetHeader(string text, IBrush? color = null)
        {
            _HeaderLabel.Text = text ?? string.Empty;
            if (color != null)
            {
                _HeaderLabel.Foreground = color;
            }
        }

        private void ToggleExpanded()
        {
            _Expanded = !_Expanded;
            _Body.IsVisible = _Expanded;
            _Chevron.Text = _Expanded ? "▾" : "▸";
        }
    }
}
