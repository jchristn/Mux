namespace Mux.Desktop.Views
{
    using System;
    using Avalonia;
    using Avalonia.Controls;
    using Avalonia.Layout;
    using Avalonia.Media;
    using Mux.Core.Enums;
    using Mux.Core.Models;

    /// <summary>
    /// A quick picker for a conversation endpoint's reasoning effort (parity with the TUI's <c>/effort</c>):
    /// choose Off / Minimal / Low / Medium / High. On choose it writes the level back into the supplied
    /// <see cref="EndpointConfig"/> (preserving any provider-specific overrides) and returns true; the caller
    /// persists the endpoints. "Off" clears the reasoning configuration.
    /// </summary>
    public sealed class ReasoningEffortDialog : Window
    {
        private const string Off = "off";

        private readonly EndpointConfig _Endpoint;

        /// <summary>
        /// Instantiate the effort picker for an endpoint.
        /// </summary>
        /// <param name="endpoint">The endpoint whose reasoning effort is edited. Required.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="endpoint"/> is null.</exception>
        public ReasoningEffortDialog(EndpointConfig endpoint)
        {
            _Endpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));

            AppTheme theme = AppTheme.Current;

            Title = "Reasoning effort";
            Icon = IconResources.LoadWindowIcon();
            Width = 380;
            SizeToContent = SizeToContent.Height;
            CanResize = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = theme.Surface;

            Content = BuildContent(theme);
        }

        private Control BuildContent(AppTheme theme)
        {
            StackPanel panel = new StackPanel { Margin = new Thickness(20), Spacing = 8 };
            panel.Children.Add(new TextBlock { Text = "Reasoning effort for \"" + _Endpoint.Name + "\"", FontWeight = FontWeight.SemiBold, Foreground = theme.Text });
            panel.Children.Add(new TextBlock { Text = "Higher effort makes reasoning models think longer. Applies to this endpoint.", Foreground = theme.Muted, FontSize = 12, TextWrapping = TextWrapping.Wrap });

            string current = _Endpoint.ReasoningEffort?.Level?.ToString().ToLowerInvariant() ?? Off;

            panel.Children.Add(Option("Off", Off, current, theme, "Send no reasoning field."));
            panel.Children.Add(Option("Minimal", "minimal", current, theme, "The lightest reasoning."));
            panel.Children.Add(Option("Low", "low", current, theme, "A little reasoning."));
            panel.Children.Add(Option("Medium", "medium", current, theme, "Balanced reasoning."));
            panel.Children.Add(Option("High", "high", current, theme, "The most reasoning."));

            Button cancel = new Button { Content = "Cancel", HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 6, 0, 0) };
            cancel.Tip("Close without changing the reasoning effort.");
            cancel.Click += (sender, args) => Close(false);
            panel.Children.Add(cancel);

            return panel;
        }

        private Control Option(string label, string value, string current, AppTheme theme, string tip)
        {
            bool active = string.Equals(value, current, StringComparison.OrdinalIgnoreCase);
            Button button = new Button
            {
                Content = active ? label + "   ✓" : label,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Background = active ? theme.AccentButton : theme.SurfaceAlt,
                Foreground = active ? theme.AccentText : theme.Text,
                Padding = new Thickness(12, 8, 12, 8)
            };
            button.Tip(tip);
            button.Click += (sender, args) =>
            {
                Apply(value);
                Close(true);
            };
            return button;
        }

        private void Apply(string value)
        {
            if (string.Equals(value, Off, StringComparison.OrdinalIgnoreCase))
            {
                _Endpoint.ReasoningEffort = null;
                return;
            }

            ReasoningEffortConfig effort = _Endpoint.ReasoningEffort?.Clone() ?? new ReasoningEffortConfig();
            if (Enum.TryParse(value, ignoreCase: true, out ReasoningLevelEnum level))
            {
                effort.Level = level;
            }

            _Endpoint.ReasoningEffort = effort;
        }
    }
}
