namespace Mux.Desktop.Views
{
    using System;
    using System.Globalization;
    using Avalonia;
    using Avalonia.Controls;
    using Avalonia.Layout;
    using Avalonia.Media;
    using Mux.Core.Models;

    /// <summary>
    /// A form for creating or editing one external web-search provider (parity row 34): name, provider type,
    /// endpoint, API key, timeout, and the enabled/default flags. On Save it writes the values back into the
    /// supplied <see cref="ExternalSearchProviderConfig"/> and returns true via <c>ShowDialog&lt;bool&gt;</c>.
    /// </summary>
    public sealed class SearchProviderFormDialog : Window
    {
        private readonly ExternalSearchProviderConfig _Config;
        private readonly TextBox _Name = new TextBox();
        private readonly ComboBox _Type = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        private readonly TextBox _Endpoint = new TextBox();
        private readonly TextBox _ApiKey = new TextBox { PasswordChar = '●' };
        private readonly TextBox _Timeout = new TextBox();
        private readonly CheckBox _Enabled = new CheckBox { Content = "Enabled" };
        private readonly CheckBox _IsDefault = new CheckBox { Content = "Use as the default provider" };
        private readonly TextBlock _Error = new TextBlock { Foreground = new SolidColorBrush(Color.Parse("#cf222e")), FontSize = 12, TextWrapping = TextWrapping.Wrap };

        /// <summary>
        /// Instantiate the search-provider form.
        /// </summary>
        /// <param name="config">The provider to edit (pre-filled and written back on Save). Required.</param>
        /// <param name="isNew">True when creating a new provider (affects the title only).</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="config"/> is null.</exception>
        public SearchProviderFormDialog(ExternalSearchProviderConfig config, bool isNew)
        {
            ArgumentNullException.ThrowIfNull(config);
            _Config = config;

            Title = isNew ? "Add search provider" : "Edit search provider";
            Icon = IconResources.LoadWindowIcon();
            Width = 520;
            SizeToContent = SizeToContent.Height;
            CanResize = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = AppTheme.Current.Surface;

            _Type.ItemsSource = new[] { "tavily", "you" };
            _Type.SelectedItem = string.IsNullOrEmpty(config.ProviderType) ? "tavily" : config.ProviderType;
            if (_Type.SelectedItem == null)
            {
                _Type.SelectedIndex = 0;
            }

            _Name.Text = config.Name;
            _Endpoint.Text = config.Endpoint;
            _ApiKey.Text = config.ApiKey;
            _Timeout.Text = config.TimeoutMs.ToString(CultureInfo.InvariantCulture);
            _Enabled.IsChecked = config.Enabled;
            _IsDefault.IsChecked = config.IsDefault;

            Content = BuildContent();
        }

        private Control BuildContent()
        {
            AppTheme theme = AppTheme.Current;
            StackPanel form = new StackPanel { Margin = new Thickness(24), Spacing = 10 };
            form.Children.Add(Field("Name", _Name, "A friendly name for this provider configuration."));
            form.Children.Add(Field("Provider type", _Type, "Which search backend this is. Only wired-up types (tavily, you) will actually run."));
            form.Children.Add(Field("Endpoint", _Endpoint, "The provider's API base URL (leave blank to use the provider's default)."));
            form.Children.Add(Field("API key", _ApiKey, "Your API key for the provider, or an env-var reference the provider resolves."));
            form.Children.Add(Field("Timeout (ms)", _Timeout, "How long to wait for a search response before giving up (1000–300000)."));
            form.Children.Add(_Enabled.Tip("Include this provider when the agent performs a web search."));
            form.Children.Add(_IsDefault.Tip("Try this provider first before any others."));
            form.Children.Add(_Error);

            StackPanel buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
            Button cancel = new Button { Content = "Cancel" };
            cancel.Tip("Discard changes and close.");
            cancel.Click += (sender, args) => Close(false);
            buttons.Children.Add(cancel);
            Button ok = new Button { Content = "Save", Background = theme.AccentButton, Foreground = theme.AccentText };
            ok.Tip("Save this search provider.");
            ok.Click += (sender, args) => Ok();
            buttons.Children.Add(ok);
            form.Children.Add(buttons);

            return form;
        }

        private Control Field(string label, Control control, string tip)
        {
            StackPanel panel = new StackPanel { Spacing = 4 };
            panel.Children.Add(new TextBlock { Text = label, Foreground = AppTheme.Current.Text, FontWeight = FontWeight.SemiBold });
            control.HorizontalAlignment = HorizontalAlignment.Stretch;
            panel.Children.Add(control);
            return panel.Tip(tip);
        }

        private void Ok()
        {
            string name = (_Name.Text ?? string.Empty).Trim();
            if (name.Length == 0)
            {
                _Error.Text = "Name is required.";
                return;
            }

            if (!int.TryParse((_Timeout.Text ?? string.Empty).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int timeout) || timeout < 1000)
            {
                _Error.Text = "Timeout must be a whole number of at least 1000 ms.";
                return;
            }

            _Config.Name = name;
            _Config.ProviderType = _Type.SelectedItem as string ?? "tavily";
            _Config.Endpoint = (_Endpoint.Text ?? string.Empty).Trim();
            _Config.ApiKey = (_ApiKey.Text ?? string.Empty).Trim();
            _Config.TimeoutMs = timeout;
            _Config.Enabled = _Enabled.IsChecked ?? true;
            _Config.IsDefault = _IsDefault.IsChecked ?? false;
            Close(true);
        }
    }
}
