namespace Mux.Desktop.Views
{
    using System;
    using System.Collections.Generic;
    using Avalonia;
    using Avalonia.Controls;
    using Avalonia.Layout;
    using Avalonia.Media;
    using Mux.Core.Settings;
    using Mux.Core.Subagents;
    using Mux.Desktop.I18n;

    /// <summary>
    /// A manager for subagent definitions (parity with the TUI's subagents surface): a sortable table with
    /// per-row actions (edit, delete). Persists through <see cref="SettingsLoader.SaveSubagents"/>.
    /// </summary>
    public sealed class SubagentsWindow : Window
    {
        private readonly List<SubagentDefinition> _Subagents;
        private readonly DataTableView<SubagentDefinition> _Table;

        /// <summary>
        /// Instantiate the subagents manager.
        /// </summary>
        public SubagentsWindow()
        {
            _Subagents = SettingsLoader.LoadSubagents();

            AppTheme theme = AppTheme.Current;

            Title = Localizer.T("nav.subagents");
            Icon = IconResources.LoadWindowIcon();
            Width = 1290;
            Height = 700;
            MinWidth = 720;
            MinHeight = 360;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = theme.Surface;

            _Table = new DataTableView<SubagentDefinition>(BuildColumns(), BuildActions, OnEdit);
            Content = BuildLayout(theme);
            Refresh();
        }

        private static List<TableColumn<SubagentDefinition>> BuildColumns()
        {
            return new List<TableColumn<SubagentDefinition>>
            {
                new TableColumn<SubagentDefinition>(Localizer.T("col.name"), s => string.IsNullOrEmpty(s.Name) ? Localizer.T("subagent.unnamed") : s.Name, new GridLength(2, GridUnitType.Star), s => s.Name, tooltip: Localizer.T("subagent.col.name.tip")),
                new TableColumn<SubagentDefinition>(Localizer.T("col.description"), s => string.IsNullOrEmpty(s.Description) ? "—" : s.Description, new GridLength(3, GridUnitType.Star), tooltip: Localizer.T("subagent.col.description.tip")),
                new TableColumn<SubagentDefinition>(Localizer.T("subagent.col.endpoint"), s => string.IsNullOrEmpty(s.EndpointName) ? Localizer.T("subagent.endpointDefault") : s.EndpointName, new GridLength(1.5, GridUnitType.Star), s => s.EndpointName ?? string.Empty, tooltip: Localizer.T("subagent.col.endpoint.tip")),
                new TableColumn<SubagentDefinition>(Localizer.T("subagent.col.tools"), s => s.AllowedTools.Count == 0 ? Localizer.T("subagent.toolsAll") : s.AllowedTools.Count.ToString(), new GridLength(1, GridUnitType.Star), s => s.AllowedTools.Count, tooltip: Localizer.T("subagent.col.tools.tip")),
                new TableColumn<SubagentDefinition>(Localizer.T("subagent.col.maxIters"), s => s.MaxIterations?.ToString() ?? "—", new GridLength(1, GridUnitType.Star), s => s.MaxIterations ?? 0, tooltip: Localizer.T("subagent.col.maxIters.tip"))
            };
        }

        private IReadOnlyList<TableRowAction<SubagentDefinition>> BuildActions(SubagentDefinition subagent)
        {
            return new List<TableRowAction<SubagentDefinition>>
            {
                new TableRowAction<SubagentDefinition>(Localizer.T("act.edit"), s => OnEdit(s)),
                new TableRowAction<SubagentDefinition>(Localizer.T("act.delete"), s => OnDelete(s), destructive: true)
            };
        }

        private Control BuildLayout(AppTheme theme)
        {
            DockPanel root = new DockPanel { Margin = new Thickness(20) };

            DockPanel header = new DockPanel();
            TextBlock title = new TextBlock { Text = Localizer.T("nav.subagents"), FontSize = 20, FontWeight = FontWeight.SemiBold, Foreground = theme.Text, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(title, Dock.Left);
            header.Children.Add(title);

            Button add = new Button { Content = "＋  " + Localizer.T("subagent.add"), Background = theme.AccentButton, Foreground = theme.AccentText, HorizontalAlignment = HorizontalAlignment.Right, Padding = new Thickness(12, 6, 12, 6) };
            add.Tip(Localizer.T("subagent.add.tip"));
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
            _Table.SetRows(_Subagents);
        }

        private async void OnAdd()
        {
            SubagentDefinition subagent = new SubagentDefinition();
            if (await new SubagentFormDialog(subagent, isNew: true).ShowDialog<bool>(this))
            {
                _Subagents.Add(subagent);
                Persist();
            }
        }

        private async void OnEdit(SubagentDefinition subagent)
        {
            if (await new SubagentFormDialog(subagent, isNew: false).ShowDialog<bool>(this))
            {
                Persist();
            }
        }

        private async void OnDelete(SubagentDefinition subagent)
        {
            bool confirmed = await new ConfirmDialog(Localizer.T("subagent.delete.title"), Localizer.T("act.delete") + " \"" + subagent.Name + "\"?", Localizer.T("act.delete"), destructive: true).ShowDialog<bool>(this);
            if (confirmed)
            {
                _Subagents.Remove(subagent);
                Persist();
            }
        }

        private void Persist()
        {
            try
            {
                SettingsLoader.SaveSubagents(_Subagents);
            }
            catch (Exception)
            {
                // Best-effort.
            }

            Refresh();
        }
    }
}
