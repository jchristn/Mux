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

    /// <summary>
    /// A manager for the configured endpoints (parity with the TUI's <c>/endpoint</c>): a sortable table with
    /// per-row actions (edit, set-default, delete) and a green <c>default</c> badge on the active endpoint.
    /// Persists to <c>endpoints.json</c> through <see cref="SettingsLoader"/> and invokes a callback after any
    /// change so the shell's model picker can refresh.
    /// </summary>
    public sealed class EndpointsWindow : Window
    {
        private readonly Action? _OnChanged;
        private readonly List<EndpointConfig> _Endpoints;
        private readonly DataTableView<EndpointConfig> _Table;

        /// <summary>
        /// Instantiate the endpoints manager.
        /// </summary>
        /// <param name="onChanged">Invoked after endpoints are saved; null to ignore.</param>
        public EndpointsWindow(Action? onChanged)
        {
            _OnChanged = onChanged;
            _Endpoints = SettingsLoader.LoadEndpoints();

            AppTheme theme = AppTheme.Current;

            Title = "Endpoints";
            Icon = IconResources.LoadWindowIcon();
            Width = 860;
            Height = 560;
            MinWidth = 640;
            MinHeight = 360;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = theme.Surface;

            _Table = new DataTableView<EndpointConfig>(BuildColumns(), BuildActions, OnEdit);
            Content = BuildLayout(theme);
            Refresh();
        }

        private static List<TableColumn<EndpointConfig>> BuildColumns()
        {
            return new List<TableColumn<EndpointConfig>>
            {
                new TableColumn<EndpointConfig>("Name", e => e.Name, new GridLength(2, GridUnitType.Star), e => e.Name, e => e.IsDefault ? "default" : null, tooltip: "The endpoint's display name. The green “default” badge marks the endpoint new conversations use."),
                new TableColumn<EndpointConfig>("Adapter", e => e.AdapterType.ToString(), new GridLength(1.2, GridUnitType.Star), e => e.AdapterType.ToString(), tooltip: "The provider protocol this endpoint speaks (OpenAI, Ollama, Anthropic, and so on)."),
                new TableColumn<EndpointConfig>("Model", e => string.IsNullOrEmpty(e.Model) ? "—" : e.Model, new GridLength(2, GridUnitType.Star), e => e.Model, tooltip: "The model identifier requests are sent to."),
                new TableColumn<EndpointConfig>("Base URL", e => e.BaseUrl, new GridLength(2.5, GridUnitType.Star), e => e.BaseUrl, tooltip: "The server address requests are sent to."),
                new TableColumn<EndpointConfig>("Context", e => e.ContextWindow.ToString("N0"), new GridLength(1, GridUnitType.Star), e => e.ContextWindow, tooltip: "The model's context window size, in tokens.")
            };
        }

        private IReadOnlyList<TableRowAction<EndpointConfig>> BuildActions(EndpointConfig endpoint)
        {
            List<TableRowAction<EndpointConfig>> actions = new List<TableRowAction<EndpointConfig>>
            {
                new TableRowAction<EndpointConfig>("Edit", e => OnEdit(e)),
                new TableRowAction<EndpointConfig>("Import models…", e => OnImport(e))
            };

            if (!endpoint.IsDefault)
            {
                actions.Add(new TableRowAction<EndpointConfig>("Set as default", e => OnSetDefault(e)));
            }

            actions.Add(new TableRowAction<EndpointConfig>("Delete", e => OnDelete(e), destructive: true));
            return actions;
        }

        private Control BuildLayout(AppTheme theme)
        {
            DockPanel root = new DockPanel { Margin = new Thickness(20) };

            DockPanel header = new DockPanel();
            TextBlock title = new TextBlock { Text = "Endpoints", FontSize = 20, FontWeight = FontWeight.SemiBold, Foreground = theme.Text, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(title, Dock.Left);
            header.Children.Add(title);

            Button add = new Button { Content = "＋  Add endpoint", Background = theme.AccentButton, Foreground = theme.AccentText, HorizontalAlignment = HorizontalAlignment.Right, Padding = new Thickness(12, 6, 12, 6) };
            add.Tip("Add a new model endpoint (provider, URL, model, and generation settings).");
            add.Click += (sender, args) => OnAdd();
            DockPanel.SetDock(add, Dock.Right);
            header.Children.Add(add);

            DockPanel.SetDock(header, Dock.Top);
            root.Children.Add(header);

            _Table.Margin = new Thickness(0, 14, 0, 0);
            root.Children.Add(_Table);
            return root;
        }

        private void Refresh()
        {
            _Table.SetRows(_Endpoints);
        }

        private async void OnAdd()
        {
            EndpointConfig endpoint = new EndpointConfig
            {
                Name = string.Empty,
                AdapterType = Mux.Core.Enums.AdapterTypeEnum.Ollama,
                BaseUrl = "http://localhost:11434",
                Model = string.Empty,
                MaxTokens = 8192,
                Temperature = 0.1,
                ContextWindow = 32768
            };

            if (await new EndpointFormDialog(endpoint, isNew: true).ShowDialog<bool>(this))
            {
                _Endpoints.Add(endpoint);
                Persist(endpoint);
            }
        }

        private async void OnEdit(EndpointConfig endpoint)
        {
            if (await new EndpointFormDialog(endpoint, isNew: false).ShowDialog<bool>(this))
            {
                Persist(endpoint);
            }
        }

        private async void OnImport(EndpointConfig source)
        {
            HashSet<string> existingModels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (EndpointConfig existing in _Endpoints)
            {
                if (!string.IsNullOrEmpty(existing.Model))
                {
                    existingModels.Add(existing.Model);
                }
            }

            List<string>? selected = await new ImportModelsDialog(source, existingModels).ShowDialog<List<string>?>(this);
            if (selected == null || selected.Count == 0)
            {
                return;
            }

            foreach (string model in selected)
            {
                _Endpoints.Add(new EndpointConfig
                {
                    Name = UniqueName(model),
                    AdapterType = source.AdapterType,
                    BaseUrl = source.BaseUrl,
                    Model = model,
                    ApiKey = source.ApiKey,
                    Temperature = source.Temperature,
                    MaxTokens = source.MaxTokens,
                    ContextWindow = source.ContextWindow,
                    TimeoutMs = source.TimeoutMs,
                    IsDefault = false
                });
            }

            Persist(null);
        }

        private string UniqueName(string baseName)
        {
            string candidate = string.IsNullOrWhiteSpace(baseName) ? "endpoint" : baseName;
            string name = candidate;
            int suffix = 2;
            while (_Endpoints.Exists(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                name = candidate + " (" + suffix + ")";
                suffix++;
            }

            return name;
        }

        private async void OnDelete(EndpointConfig endpoint)
        {
            bool confirmed = await new ConfirmDialog("Delete endpoint", "Delete \"" + endpoint.Name + "\"?", "Delete", destructive: true).ShowDialog<bool>(this);
            if (confirmed)
            {
                _Endpoints.Remove(endpoint);
                Persist(null);
            }
        }

        private void OnSetDefault(EndpointConfig endpoint)
        {
            foreach (EndpointConfig other in _Endpoints)
            {
                other.IsDefault = ReferenceEquals(other, endpoint);
            }

            Persist(null);
        }

        private void Persist(EndpointConfig? justEditedDefault)
        {
            if (justEditedDefault != null && justEditedDefault.IsDefault)
            {
                foreach (EndpointConfig other in _Endpoints)
                {
                    if (!ReferenceEquals(other, justEditedDefault))
                    {
                        other.IsDefault = false;
                    }
                }
            }

            try
            {
                SettingsLoader.SaveEndpoints(_Endpoints);
            }
            catch (Exception)
            {
                // Best-effort; a later pass surfaces save errors inline.
            }

            Refresh();
            _OnChanged?.Invoke();
        }
    }
}
