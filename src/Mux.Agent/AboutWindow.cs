namespace Mux.Agent
{
    using Avalonia;
    using Avalonia.Controls;
    using Avalonia.Layout;
    using Avalonia.Media;
    using Mux.Core.Settings;

    /// <summary>
    /// A small code-only About window for the tray agent.
    /// </summary>
    public sealed class AboutWindow : Window
    {
        /// <summary>
        /// Instantiate the About window.
        /// </summary>
        /// <param name="baseUrl">The REST base URL, or empty when the server is not running.</param>
        public AboutWindow(string baseUrl)
        {
            Title = "About mux";
            Width = 380;
            Height = 200;
            CanResize = false;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;

            StackPanel panel = new StackPanel
            {
                Margin = new Thickness(24),
                Spacing = 8,
                HorizontalAlignment = HorizontalAlignment.Left
            };

            panel.Children.Add(new TextBlock { Text = "mux", FontSize = 24, FontWeight = FontWeight.Bold });
            panel.Children.Add(new TextBlock { Text = "Version " + Defaults.ProductVersion });
            panel.Children.Add(new TextBlock { Text = "Your AI agent, your models, your infrastructure." });
            if (!string.IsNullOrEmpty(baseUrl))
            {
                panel.Children.Add(new TextBlock { Text = "REST API: " + baseUrl });
            }
            panel.Children.Add(new TextBlock { Text = "MIT License", Foreground = Brushes.Gray });

            Content = panel;
        }
    }
}
