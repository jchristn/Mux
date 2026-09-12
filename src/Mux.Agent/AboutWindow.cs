namespace Mux.Agent
{
    using Avalonia;
    using Avalonia.Controls;
    using Avalonia.Input;
    using Avalonia.Layout;
    using Avalonia.Media;
    using Mux.Core.Settings;

    /// <summary>
    /// A small code-only About window for the tray agent. It shows the mux ASCII-art wordmark in the
    /// product's green accent (echoing the TUI) over a dark, terminal-like background, the version and
    /// license, and a clickable GitHub link that opens the repository in the default browser. All content
    /// is centered.
    /// </summary>
    public sealed class AboutWindow : Window
    {
        private const string RepositoryUrl = "https://github.com/jchristn/Mux";

        // The mux ASCII-art wordmark (matches the TUI splash), as a single block so its internal alignment
        // is preserved while the block itself is centered.
        private const string Wordmark =
            "▄▄   ▄▄ ▄▄ ▄▄ ▄▄ ▄▄\n" +
            "██▀▄▀██ ██ ██ ▀█▄█▀\n" +
            "██   ██ ▀███▀ ██ ██";

        /// <summary>
        /// Instantiate the About window.
        /// </summary>
        public AboutWindow()
        {
            Title = "About mux";
            Icon = App.LoadIcon(); // the mux wordmark glyph, matching the tray icon (theme-aware; null is fine)
            Width = 440;
            Height = 300;
            CanResize = false;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = new SolidColorBrush(Color.Parse("#0d1117"));

            SolidColorBrush green = new SolidColorBrush(Color.Parse("#3fb950"));
            SolidColorBrush light = new SolidColorBrush(Color.Parse("#e6edf3"));
            SolidColorBrush muted = new SolidColorBrush(Color.Parse("#8b949e"));
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
                Foreground = green,
                TextAlignment = TextAlignment.Left,
                HorizontalAlignment = HorizontalAlignment.Center
            });

            panel.Children.Add(new TextBlock
            {
                Text = "AI agent for local and remote LLMs",
                Foreground = light,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            });

            panel.Children.Add(new TextBlock
            {
                Text = "v" + Defaults.ProductVersion + "  ·  MIT License",
                Foreground = muted,
                FontSize = 12,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            });

            TextBlock link = new TextBlock
            {
                Text = "github.com/jchristn/Mux",
                Foreground = green,
                TextDecorations = TextDecorations.Underline,
                Cursor = new Cursor(StandardCursorType.Hand),
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 4, 0, 0)
            };
            link.PointerPressed += (sender, args) => AgentHost.OpenUrl(RepositoryUrl);
            panel.Children.Add(link);

            panel.Children.Add(new TextBlock
            {
                Text = "(c)2026 Joel Christner",
                Foreground = muted,
                FontSize = 11,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            });

            Content = panel;
        }
    }
}
