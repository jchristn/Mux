namespace Mux.Desktop.Views
{
    using System;
    using Avalonia;
    using Avalonia.Controls;
    using Avalonia.Input;
    using Avalonia.Layout;
    using Avalonia.Media;
    using Mux.Core.Settings;
    using Mux.Desktop.I18n;

    /// <summary>
    /// The About window. Shows the mux ASCII-art wordmark in the accent color over the app's current theme
    /// surface, the version and license, a clickable repository link, and a short getting-started note.
    /// Follows the active <see cref="AppTheme"/> so it matches the rest of the desktop app.
    /// </summary>
    public sealed class AboutWindow : Window
    {
        private const string RepositoryUrl = "https://github.com/jchristn/Mux";

        // The mux ASCII-art wordmark (matches the TUI splash and the tray agent).
        private const string Wordmark =
            " _____ _ _ _ _\n" +
            "|     | | |_'_|\n" +
            "|_|_|_|___|_,_|";

        /// <summary>
        /// Instantiate the About window.
        /// </summary>
        /// <param name="localization">The localization service supplying UI strings. Required.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="localization"/> is null.</exception>
        public AboutWindow(ILocalizationService localization)
        {
            ArgumentNullException.ThrowIfNull(localization);

            AppTheme theme = AppTheme.Current;

            Title = localization.Get(StringKeys.AboutHelp);
            Icon = IconResources.LoadWindowIcon();
            Width = 520;
            Height = 460;
            CanResize = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = theme.Surface;

            FontFamily mono = new FontFamily("Cascadia Mono,Consolas,Menlo,monospace");

            StackPanel panel = new StackPanel
            {
                Margin = new Thickness(28),
                Spacing = 10,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            panel.Children.Add(new TextBlock
            {
                Text = Wordmark,
                FontFamily = mono,
                FontSize = 18,
                FontWeight = FontWeight.Bold,
                Foreground = theme.Accent,
                TextAlignment = TextAlignment.Left,
                HorizontalAlignment = HorizontalAlignment.Center
            });

            panel.Children.Add(new TextBlock
            {
                Text = localization.Get(StringKeys.AppTagline),
                Foreground = theme.Text,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            });

            panel.Children.Add(new TextBlock
            {
                Text = "v" + Defaults.ProductVersion + "  ·  " + localization.Get(StringKeys.License),
                Foreground = theme.Muted,
                FontSize = 12,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            });

            TextBlock link = new TextBlock
            {
                Text = "github.com/jchristn/Mux",
                Foreground = theme.Accent,
                TextDecorations = TextDecorations.Underline,
                Cursor = new Cursor(StandardCursorType.Hand),
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 2, 0, 0)
            };
            link.PointerPressed += (sender, args) => BrowserLauncher.OpenUrl(RepositoryUrl);
            panel.Children.Add(link);

            panel.Children.Add(new Border
            {
                BorderBrush = theme.Border,
                BorderThickness = new Thickness(0, 1, 0, 0),
                Margin = new Thickness(0, 10, 0, 4),
                Width = 380
            });

            panel.Children.Add(new TextBlock
            {
                Text = localization.Get(StringKeys.HelpHeading),
                Foreground = theme.Text,
                FontWeight = FontWeight.SemiBold,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            });

            panel.Children.Add(new TextBlock
            {
                Text = localization.Get(StringKeys.HelpBody),
                Foreground = theme.Muted,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 420,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            });

            panel.Children.Add(new TextBlock
            {
                Text = "(c)2026 Joel Christner",
                Foreground = theme.Muted,
                FontSize = 11,
                Margin = new Thickness(0, 6, 0, 0),
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            });

            Content = panel;
        }
    }
}
