namespace Mux.Desktop.Views
{
    using System;
    using System.Globalization;
    using Avalonia;
    using Avalonia.Controls;
    using Avalonia.Layout;
    using Avalonia.Media;
    using Mux.Core.Models;
    using Mux.Desktop.I18n;

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
        private readonly CheckBox _Enabled = new CheckBox { Content = Localizer.T("search.enabled") };
        private readonly CheckBox _IsDefault = new CheckBox { Content = Localizer.T("search.useDefault") };
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

            Title = isNew ? Localizer.T("search.form.addTitle") : Localizer.T("search.form.editTitle");
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
            form.Children.Add(Field(Localizer.T("col.name"), _Name, Localizer.T("search.name.tip")));
            form.Children.Add(Field(Localizer.T("search.providerType"), _Type, Localizer.T("search.providerType.tip")));
            form.Children.Add(Field(Localizer.T("search.endpoint"), _Endpoint, Localizer.T("search.endpoint.tip")));
            form.Children.Add(Field(Localizer.T("search.apiKey"), _ApiKey, Localizer.T("search.apiKey.tip")));
            form.Children.Add(Field(Localizer.T("search.timeout"), _Timeout, Localizer.T("search.timeout.tip")));
            form.Children.Add(_Enabled.Tip(Localizer.T("search.enabled.tip")));
            form.Children.Add(_IsDefault.Tip(Localizer.T("search.useDefault.tip")));
            form.Children.Add(_Error);

            StackPanel buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
            Button cancel = new Button { Content = Localizer.T("act.cancel") };
            cancel.Tip(Localizer.T("search.cancel.tip"));
            cancel.Click += (sender, args) => Close(false);
            buttons.Children.Add(cancel);
            Button ok = new Button { Content = Localizer.T("act.save"), Background = theme.AccentButton, Foreground = theme.AccentText };
            ok.Tip(Localizer.T("search.save.tip"));
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
                _Error.Text = Localizer.T("search.err.nameRequired");
                return;
            }

            if (!int.TryParse((_Timeout.Text ?? string.Empty).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int timeout) || timeout < 1000)
            {
                _Error.Text = Localizer.T("search.err.timeout");
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
