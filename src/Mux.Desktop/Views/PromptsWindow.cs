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
    using Mux.Desktop.Prompting;

    /// <summary>
    /// A manager for prompt profiles (parity with the TUI's <c>/prompt</c>): a sortable table with per-row
    /// actions (edit, set-active, delete) and a green <c>active</c> badge on the active profile. Persists
    /// through <see cref="SettingsLoader.SavePrompts"/>.
    /// </summary>
    public sealed class PromptsWindow : Window
    {
        private readonly List<PromptProfile> _Prompts;
        private readonly DataTableView<PromptProfile> _Table;
        private readonly PromptCatalogViewModel _Catalog = new PromptCatalogViewModel();
        private readonly DataTableView<PromptCatalogRow> _CatalogTable;

        /// <summary>
        /// Instantiate the prompts manager.
        /// </summary>
        /// <param name="onChanged">Invoked after prompts are saved; null to ignore.</param>
        public PromptsWindow(Action? onChanged)
        {
            _ = onChanged;
            _Prompts = SettingsLoader.LoadPrompts();

            AppTheme theme = AppTheme.Current;

            Title = Localizer.T("prompt.title");
            Icon = IconResources.LoadWindowIcon();
            Width = 900;
            Height = 720;
            MinWidth = 640;
            MinHeight = 480;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = theme.Surface;

            _Table = new DataTableView<PromptProfile>(BuildColumns(), BuildActions, OnEdit);
            _CatalogTable = new DataTableView<PromptCatalogRow>(BuildCatalogColumns(), BuildCatalogActions, OnEditCatalog);
            Content = BuildLayout(theme);
            Refresh();
            RefreshCatalog();
        }

        private static List<TableColumn<PromptProfile>> BuildColumns()
        {
            return new List<TableColumn<PromptProfile>>
            {
                new TableColumn<PromptProfile>(Localizer.T("col.name"), p => string.IsNullOrEmpty(p.Name) ? Localizer.T("prompt.unnamed") : p.Name, new GridLength(2, GridUnitType.Star), p => p.Name, p => p.IsActive ? Localizer.T("prompt.active") : null, tooltip: Localizer.T("prompt.col.name.tip")),
                new TableColumn<PromptProfile>(Localizer.T("prompt.systemPrompt"), p => Preview(p.SystemPrompt), new GridLength(4, GridUnitType.Star), tooltip: Localizer.T("prompt.col.systemPrompt.tip"))
            };
        }

        private IReadOnlyList<TableRowAction<PromptProfile>> BuildActions(PromptProfile prompt)
        {
            List<TableRowAction<PromptProfile>> actions = new List<TableRowAction<PromptProfile>>
            {
                new TableRowAction<PromptProfile>(Localizer.T("act.edit"), p => OnEdit(p))
            };

            if (!prompt.IsActive)
            {
                actions.Add(new TableRowAction<PromptProfile>(Localizer.T("prompt.setActive"), p => OnSetActive(p)));
            }

            actions.Add(new TableRowAction<PromptProfile>(Localizer.T("act.delete"), p => OnDelete(p), destructive: true));
            return actions;
        }

        private static string Preview(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return Localizer.T("prompt.empty");
            }

            string collapsed = text.Replace("\r", " ").Replace("\n", " ").Trim();
            return collapsed.Length > 140 ? collapsed.Substring(0, 140) + "…" : collapsed;
        }

        private Control BuildLayout(AppTheme theme)
        {
            DockPanel root = new DockPanel { Margin = new Thickness(20) };

            DockPanel header = new DockPanel();
            TextBlock title = new TextBlock { Text = Localizer.T("prompt.title"), FontSize = 20, FontWeight = FontWeight.SemiBold, Foreground = theme.Text, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(title, Dock.Left);
            header.Children.Add(title);

            Button add = new Button { Content = "＋  " + Localizer.T("prompt.add"), Background = theme.AccentButton, Foreground = theme.AccentText, HorizontalAlignment = HorizontalAlignment.Right, Padding = new Thickness(12, 6, 12, 6) };
            add.Tip(Localizer.T("prompt.add.tip"));
            add.Click += (sender, args) => OnAdd();
            DockPanel.SetDock(add, Dock.Right);
            header.Children.Add(add);

            DockPanel.SetDock(header, Dock.Top);
            root.Children.Add(header);

            Grid grid = new Grid { Margin = new Thickness(0, 14, 0, 0) };
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            grid.RowDefinitions.Add(new RowDefinition(new GridLength(3, GridUnitType.Star)));
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            grid.RowDefinitions.Add(new RowDefinition(new GridLength(2, GridUnitType.Star)));

            TextBlock profilesLabel = new TextBlock { Text = Localizer.T("prompt.title"), FontWeight = FontWeight.SemiBold, Foreground = theme.Muted, Margin = new Thickness(0, 0, 0, 6) };
            Grid.SetRow(profilesLabel, 0);
            grid.Children.Add(profilesLabel);

            Grid.SetRow(_Table, 1);
            grid.Children.Add(_Table);

            StackPanel catalogHeader = new StackPanel { Margin = new Thickness(0, 16, 0, 6), Spacing = 2 };
            catalogHeader.Children.Add(new TextBlock { Text = Localizer.T(StringKeys.PromptCatalogTitle), FontWeight = FontWeight.SemiBold, Foreground = theme.Text });
            catalogHeader.Children.Add(new TextBlock { Text = Localizer.T(StringKeys.PromptCatalogDesc), Foreground = theme.Muted, FontSize = 12, TextWrapping = TextWrapping.Wrap });
            Grid.SetRow(catalogHeader, 2);
            grid.Children.Add(catalogHeader);

            Grid.SetRow(_CatalogTable, 3);
            grid.Children.Add(_CatalogTable);

            root.Children.Add(grid);
            return root;
        }

        private void Refresh()
        {
            _Table.SetRows(_Prompts);
        }

        private void RefreshCatalog()
        {
            _CatalogTable.SetRows(_Catalog.Rows);
        }

        private static List<TableColumn<PromptCatalogRow>> BuildCatalogColumns()
        {
            return new List<TableColumn<PromptCatalogRow>>
            {
                new TableColumn<PromptCatalogRow>(Localizer.T(StringKeys.PromptCatalogKind), r => r.Kind, new GridLength(2, GridUnitType.Star), r => r.Kind),
                new TableColumn<PromptCatalogRow>(Localizer.T("col.name"), r => r.DisplayName, new GridLength(3, GridUnitType.Star), r => r.DisplayName, r => r.Overridden ? Localizer.T(StringKeys.PromptCatalogCustom) : null),
                new TableColumn<PromptCatalogRow>(Localizer.T("prompt.systemPrompt"), r => Preview(r.Effective), new GridLength(5, GridUnitType.Star))
            };
        }

        private IReadOnlyList<TableRowAction<PromptCatalogRow>> BuildCatalogActions(PromptCatalogRow row)
        {
            List<TableRowAction<PromptCatalogRow>> actions = new List<TableRowAction<PromptCatalogRow>>
            {
                new TableRowAction<PromptCatalogRow>(row.Editable ? Localizer.T("act.edit") : Localizer.T(StringKeys.ActView), r => OnEditCatalog(r))
            };

            if (row.Editable && row.Overridden)
            {
                actions.Add(new TableRowAction<PromptCatalogRow>(Localizer.T(StringKeys.ActReset), r => OnResetCatalog(r)));
            }

            return actions;
        }

        private async void OnEditCatalog(PromptCatalogRow row)
        {
            if (!row.Editable)
            {
                await new ConfirmDialog(row.DisplayName, row.Description + "\n\n" + Localizer.T(StringKeys.PromptCatalogPersonaNote) + "\n\n" + row.Effective, Localizer.T(StringKeys.ActClose), destructive: false).ShowDialog<bool>(this);
                return;
            }

            PromptCatalogEditDialog dialog = new PromptCatalogEditDialog(row);
            if (await dialog.ShowDialog<bool>(this))
            {
                if (!_Catalog.TrySetOverride(row.Key, dialog.Content, out string error))
                {
                    await new ConfirmDialog(Localizer.T("prompt.title"), error, Localizer.T(StringKeys.ActClose), destructive: false).ShowDialog<bool>(this);
                }

                RefreshCatalog();
            }
        }

        private void OnResetCatalog(PromptCatalogRow row)
        {
            _Catalog.Reset(row.Key);
            RefreshCatalog();
        }

        private async void OnAdd()
        {
            PromptProfile prompt = new PromptProfile();
            if (await new PromptProfileFormDialog(prompt, isNew: true).ShowDialog<bool>(this))
            {
                _Prompts.Add(prompt);
                Normalize(prompt);
                Persist();
            }
        }

        private async void OnEdit(PromptProfile prompt)
        {
            if (await new PromptProfileFormDialog(prompt, isNew: false).ShowDialog<bool>(this))
            {
                Normalize(prompt);
                Persist();
            }
        }

        private async void OnDelete(PromptProfile prompt)
        {
            bool confirmed = await new ConfirmDialog(Localizer.T("prompt.delete.title"), string.Format(Localizer.T("prompt.delete.confirm"), prompt.Name), Localizer.T("act.delete"), destructive: true).ShowDialog<bool>(this);
            if (confirmed)
            {
                _Prompts.Remove(prompt);
                Persist();
            }
        }

        private void OnSetActive(PromptProfile prompt)
        {
            foreach (PromptProfile other in _Prompts)
            {
                other.IsActive = ReferenceEquals(other, prompt);
            }

            Persist();
        }

        private void Normalize(PromptProfile justEdited)
        {
            if (!justEdited.IsActive)
            {
                return;
            }

            foreach (PromptProfile other in _Prompts)
            {
                if (!ReferenceEquals(other, justEdited))
                {
                    other.IsActive = false;
                }
            }
        }

        private void Persist()
        {
            try
            {
                SettingsLoader.SavePrompts(_Prompts);
            }
            catch (Exception)
            {
                // Best-effort.
            }

            Refresh();
        }
    }
}
