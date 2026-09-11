namespace Mux.Desktop.Views
{
    using System;
    using Avalonia;
    using Avalonia.Controls;
    using Avalonia.Layout;
    using Avalonia.Media;
    using Avalonia.Media.Imaging;
    using Mux.Core.Settings;
    using Mux.Desktop.I18n;

    /// <summary>
    /// A borderless startup splash shown while the application initializes. Presents the mux glyph, the
    /// product name, version, tagline, and a loading indicator over a dark, terminal-like background.
    /// </summary>
    public sealed class SplashWindow : Window
    {
        /// <summary>
        /// Instantiate the splash window.
        /// </summary>
        /// <param name="localization">The localization service supplying UI strings. Required.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="localization"/> is null.</exception>
        public SplashWindow(ILocalizationService localization)
        {
            ArgumentNullException.ThrowIfNull(localization);

            WindowDecorations = WindowDecorations.None;
            CanResize = false;
            ShowInTaskbar = false;
            Width = 440;
            Height = 280;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = new SolidColorBrush(Color.Parse("#0d1117"));
            Icon = IconResources.LoadWindowIcon();

            SolidColorBrush green = new SolidColorBrush(Color.Parse("#3fb950"));
            SolidColorBrush light = new SolidColorBrush(Color.Parse("#e6edf3"));
            SolidColorBrush muted = new SolidColorBrush(Color.Parse("#8b949e"));

            StackPanel panel = new StackPanel
            {
                Margin = new Thickness(28),
                Spacing = 12,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            Bitmap? logo = IconResources.LoadLogoBitmap(true);
            if (logo != null)
            {
                panel.Children.Add(new Image
                {
                    Source = logo,
                    Width = 96,
                    Height = 96,
                    HorizontalAlignment = HorizontalAlignment.Center
                });
            }

            panel.Children.Add(new TextBlock
            {
                Text = localization.Get(StringKeys.AppTitle),
                Foreground = green,
                FontSize = 30,
                FontWeight = FontWeight.Bold,
                HorizontalAlignment = HorizontalAlignment.Center
            });

            panel.Children.Add(new TextBlock
            {
                Text = localization.Get(StringKeys.AppTagline),
                Foreground = light,
                FontSize = 13,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            });

            panel.Children.Add(new TextBlock
            {
                Text = "v" + Defaults.ProductVersion + "  ·  " + localization.Get(StringKeys.SplashLoading),
                Foreground = muted,
                FontSize = 12,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            });

            Content = panel;
        }
    }
}
