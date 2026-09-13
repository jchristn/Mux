namespace Mux.Desktop.Views
{
    using System;
    using System.Collections.Generic;
    using Avalonia;
    using Avalonia.Controls;
    using Avalonia.Layout;
    using Avalonia.Media;
    using Mux.Core.Models;
    using Mux.Core.Settings;
    using Mux.Desktop.I18n;

    /// <summary>
    /// A manager for external web-search providers (parity row 34): the master enable/fallback toggles plus a
    /// sortable table of providers with add, edit, delete, and set-default. Persists through
    /// <see cref="SettingsLoader.SaveSettings"/> (the providers live under <c>settings.externalSearch</c>).
    /// </summary>
    public sealed class SearchProvidersWindow : Window
    {
        private readonly MuxSettings _Settings;
        private readonly List<ExternalSearchProviderConfig> _Providers;
        private readonly DataTableView<ExternalSearchProviderConfig> _Table;

        /// <summary>
        /// Instantiate the search-providers manager.
        /// </summary>
        public SearchProvidersWindow()
        {
            _Settings = SettingsLoader.LoadSettings();
            _Providers = _Settings.ExternalSearch.Providers;

            AppTheme theme = AppTheme.Current;

            Title = Localizer.T("search.title");
            Icon = IconResources.LoadWindowIcon();
            Width = 900;
            Height = 600;
            MinWidth = 640;
            MinHeight = 380;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = theme.Surface;

            _Table = new DataTableView<ExternalSearchProviderConfig>(BuildColumns(), BuildActions, OnEdit);
            Content = BuildLayout(theme);
            Refresh();
        }

        private static List<TableColumn<ExternalSearchProviderConfig>> BuildColumns()
        {
            return new List<TableColumn<ExternalSearchProviderConfig>>
            {
                new TableColumn<ExternalSearchProviderConfig>(Localizer.T("col.name"), p => p.Name, new GridLength(2, GridUnitType.Star), p => p.Name, p => p.IsDefault ? Localizer.T("search.default") : null, tooltip: Localizer.T("search.col.name.tip")),
                new TableColumn<ExternalSearchProviderConfig>(Localizer.T("search.col.type"), p => p.ProviderType, new GridLength(1.2, GridUnitType.Star), p => p.ProviderType, tooltip: Localizer.T("search.col.type.tip")),
                new TableColumn<ExternalSearchProviderConfig>(Localizer.T("search.endpoint"), p => string.IsNullOrEmpty(p.Endpoint) ? Localizer.T("search.endpointDefault") : p.Endpoint, new GridLength(3, GridUnitType.Star), p => p.Endpoint, tooltip: Localizer.T("search.col.endpoint.tip")),
                new TableColumn<ExternalSearchProviderConfig>(Localizer.T("search.col.enabled"), p => p.Enabled ? Localizer.T("search.yes") : Localizer.T("search.no"), new GridLength(1, GridUnitType.Star), p => p.Enabled ? 1 : 0, tooltip: Localizer.T("search.col.enabled.tip"))
            };
        }

        private IReadOnlyList<TableRowAction<ExternalSearchProviderConfig>> BuildActions(ExternalSearchProviderConfig provider)
        {
            List<TableRowAction<ExternalSearchProviderConfig>> actions = new List<TableRowAction<ExternalSearchProviderConfig>>
            {
                new TableRowAction<ExternalSearchProviderConfig>(Localizer.T("act.edit"), p => OnEdit(p)),
                new TableRowAction<ExternalSearchProviderConfig>(provider.Enabled ? Localizer.T("search.disable") : Localizer.T("search.enable"), p => OnToggleEnabled(p))
            };

            if (!provider.IsDefault)
            {
                actions.Add(new TableRowAction<ExternalSearchProviderConfig>(Localizer.T("search.setDefault"), p => OnSetDefault(p)));
            }

            actions.Add(new TableRowAction<ExternalSearchProviderConfig>(Localizer.T("act.delete"), p => OnDelete(p), destructive: true));
            return actions;
        }

        private Control BuildLayout(AppTheme theme)
        {
            DockPanel root = new DockPanel { Margin = new Thickness(20) };

            StackPanel header = new StackPanel { Spacing = 8 };
            DockPanel titleRow = new DockPanel();
            TextBlock title = new TextBlock { Text = Localizer.T("search.title"), FontSize = 20, FontWeight = FontWeight.SemiBold, Foreground = theme.Text, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(title, Dock.Left);
            titleRow.Children.Add(title);

            Button add = new Button { Content = "＋  " + Localizer.T("search.add"), Background = theme.AccentButton, Foreground = theme.AccentText, HorizontalAlignment = HorizontalAlignment.Right, Padding = new Thickness(12, 6, 12, 6) };
            add.Tip(Localizer.T("search.add.tip"));
            add.Click += (sender, args) => OnAdd();
            DockPanel.SetDock(add, Dock.Right);
            titleRow.Children.Add(add);
            header.Children.Add(titleRow);

            CheckBox enabled = new CheckBox { Content = Localizer.T("search.enableSearch"), IsChecked = _Settings.ExternalSearch.Enabled };
            enabled.Tip(Localizer.T("search.enableSearch.tip"));
            enabled.IsCheckedChanged += (sender, args) =>
            {
                _Settings.ExternalSearch.Enabled = enabled.IsChecked ?? false;
                Persist();
            };
            header.Children.Add(enabled);

            CheckBox fallback = new CheckBox { Content = Localizer.T("search.allowFallback"), IsChecked = _Settings.ExternalSearch.AllowFallback };
            fallback.Tip(Localizer.T("search.allowFallback.tip"));
            fallback.IsCheckedChanged += (sender, args) =>
            {
                _Settings.ExternalSearch.AllowFallback = fallback.IsChecked ?? true;
                Persist();
            };
            header.Children.Add(fallback);

            DockPanel.SetDock(header, Dock.Top);
            root.Children.Add(header);

            _Table.Margin = new Thickness(0, 14, 0, 0);
            root.Children.Add(_Table);
            return root;
        }

        private void Refresh()
        {
            _Table.SetRows(_Providers);
        }

        private async void OnAdd()
        {
            ExternalSearchProviderConfig provider = new ExternalSearchProviderConfig { ProviderType = "tavily", Enabled = true };
            if (await new SearchProviderFormDialog(provider, isNew: true).ShowDialog<bool>(this))
            {
                _Providers.Add(provider);
                Normalize(provider);
                Persist();
            }
        }

        private async void OnEdit(ExternalSearchProviderConfig provider)
        {
            if (await new SearchProviderFormDialog(provider, isNew: false).ShowDialog<bool>(this))
            {
                Normalize(provider);
                Persist();
            }
        }

        private void OnToggleEnabled(ExternalSearchProviderConfig provider)
        {
            provider.Enabled = !provider.Enabled;
            Persist();
        }

        private void OnSetDefault(ExternalSearchProviderConfig provider)
        {
            foreach (ExternalSearchProviderConfig other in _Providers)
            {
                other.IsDefault = ReferenceEquals(other, provider);
            }

            Persist();
        }

        private async void OnDelete(ExternalSearchProviderConfig provider)
        {
            bool confirmed = await new ConfirmDialog(Localizer.T("search.delete.title"), string.Format(Localizer.T("search.delete.confirm"), provider.Name), Localizer.T("act.delete"), destructive: true).ShowDialog<bool>(this);
            if (confirmed)
            {
                _Providers.Remove(provider);
                Persist();
            }
        }

        private void Normalize(ExternalSearchProviderConfig justEdited)
        {
            if (!justEdited.IsDefault)
            {
                return;
            }

            foreach (ExternalSearchProviderConfig other in _Providers)
            {
                if (!ReferenceEquals(other, justEdited))
                {
                    other.IsDefault = false;
                }
            }
        }

        private void Persist()
        {
            try
            {
                SettingsLoader.SaveSettings(_Settings);
            }
            catch (Exception)
            {
                // Best-effort.
            }

            Refresh();
        }
    }
}
