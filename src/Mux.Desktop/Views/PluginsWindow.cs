namespace Mux.Desktop.Views
{
    using System;
    using System.Collections.Generic;
    using Avalonia;
    using Avalonia.Controls;
    using Avalonia.Layout;
    using Avalonia.Media;
    using Mux.Core.Plugins;
    using Mux.Core.Settings;
    using Mux.Desktop.I18n;

    /// <summary>
    /// A manager for plugins (parity row 31): out-of-process lifecycle hooks and custom slash commands, both
    /// stored in <c>hooks.json</c>. Two sortable tables — one for hooks, one for commands — each with add,
    /// edit, and delete. Persists through <see cref="SettingsLoader.SavePluginConfig"/>.
    /// </summary>
    public sealed class PluginsWindow : Window
    {
        private readonly PluginConfig _Config;
        private readonly DataTableView<HookDefinition> _HooksTable;
        private readonly DataTableView<CustomCommandDefinition> _CommandsTable;

        /// <summary>
        /// Instantiate the plugins manager.
        /// </summary>
        public PluginsWindow()
        {
            _Config = SettingsLoader.LoadPluginConfig();

            AppTheme theme = AppTheme.Current;

            Title = Localizer.T("plugin.title");
            Icon = IconResources.LoadWindowIcon();
            Width = 900;
            Height = 640;
            MinWidth = 640;
            MinHeight = 400;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = theme.Surface;

            _HooksTable = new DataTableView<HookDefinition>(BuildHookColumns(), BuildHookActions, OnEditHook);
            _CommandsTable = new DataTableView<CustomCommandDefinition>(BuildCommandColumns(), BuildCommandActions, OnEditCommand);
            Content = BuildLayout(theme);
            Refresh();
        }

        private static List<TableColumn<HookDefinition>> BuildHookColumns()
        {
            return new List<TableColumn<HookDefinition>>
            {
                new TableColumn<HookDefinition>(Localizer.T("col.name"), h => h.Name, new GridLength(2, GridUnitType.Star), h => h.Name, tooltip: Localizer.T("plugin.hook.col.name.tip")),
                new TableColumn<HookDefinition>(Localizer.T("plugin.hook.col.event"), h => h.Event.ToString(), new GridLength(1.6, GridUnitType.Star), h => h.Event.ToString(), tooltip: Localizer.T("plugin.hook.col.event.tip")),
                new TableColumn<HookDefinition>(Localizer.T("plugin.hook.col.command"), h => h.Command, new GridLength(3, GridUnitType.Star), h => h.Command, tooltip: Localizer.T("plugin.hook.col.command.tip")),
                new TableColumn<HookDefinition>(Localizer.T("plugin.hook.col.blocking"), h => h.Blocking ? Localizer.T("plugin.yes") : Localizer.T("plugin.no"), new GridLength(1, GridUnitType.Star), h => h.Blocking ? 1 : 0, tooltip: Localizer.T("plugin.hook.col.blocking.tip"))
            };
        }

        private IReadOnlyList<TableRowAction<HookDefinition>> BuildHookActions(HookDefinition hook)
        {
            return new List<TableRowAction<HookDefinition>>
            {
                new TableRowAction<HookDefinition>(Localizer.T("act.edit"), h => OnEditHook(h)),
                new TableRowAction<HookDefinition>(Localizer.T("act.delete"), h => OnDeleteHook(h), destructive: true)
            };
        }

        private static List<TableColumn<CustomCommandDefinition>> BuildCommandColumns()
        {
            return new List<TableColumn<CustomCommandDefinition>>
            {
                new TableColumn<CustomCommandDefinition>(Localizer.T("plugin.command.col.command"), c => "/" + c.Name, new GridLength(2, GridUnitType.Star), c => c.Name, tooltip: Localizer.T("plugin.command.col.command.tip")),
                new TableColumn<CustomCommandDefinition>(Localizer.T("col.description"), c => string.IsNullOrEmpty(c.Description) ? "—" : c.Description, new GridLength(3, GridUnitType.Star), tooltip: Localizer.T("plugin.command.col.description.tip")),
                new TableColumn<CustomCommandDefinition>(Localizer.T("plugin.command.col.runs"), c => c.Command, new GridLength(3, GridUnitType.Star), c => c.Command, tooltip: Localizer.T("plugin.command.col.runs.tip"))
            };
        }

        private IReadOnlyList<TableRowAction<CustomCommandDefinition>> BuildCommandActions(CustomCommandDefinition command)
        {
            return new List<TableRowAction<CustomCommandDefinition>>
            {
                new TableRowAction<CustomCommandDefinition>(Localizer.T("act.edit"), c => OnEditCommand(c)),
                new TableRowAction<CustomCommandDefinition>(Localizer.T("act.delete"), c => OnDeleteCommand(c), destructive: true)
            };
        }

        private Control BuildLayout(AppTheme theme)
        {
            Grid root = new Grid
            {
                Margin = new Thickness(20),
                RowDefinitions = new RowDefinitions("Auto,*,Auto,*")
            };

            root.Children.Add(SectionHeader(Localizer.T("plugin.hooks.section"), Localizer.T("plugin.hooks.section.subtitle"), OnAddHook, "＋  " + Localizer.T("plugin.addHook"), theme, 0));
            _HooksTable.Margin = new Thickness(0, 8, 0, 12);
            Grid.SetRow(_HooksTable, 1);
            root.Children.Add(_HooksTable);

            root.Children.Add(SectionHeader(Localizer.T("plugin.commands.section"), Localizer.T("plugin.commands.section.subtitle"), OnAddCommand, "＋  " + Localizer.T("plugin.addCommand"), theme, 2));
            _CommandsTable.Margin = new Thickness(0, 8, 0, 0);
            Grid.SetRow(_CommandsTable, 3);
            root.Children.Add(_CommandsTable);

            return root;
        }

        private static Control SectionHeader(string title, string subtitle, Action onAdd, string addLabel, AppTheme theme, int row)
        {
            DockPanel header = new DockPanel { Margin = new Thickness(0, row == 0 ? 0 : 10, 0, 0) };

            Button add = new Button { Content = addLabel, Background = theme.AccentButton, Foreground = theme.AccentText, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Padding = new Thickness(12, 6, 12, 6) };
            add.Click += (sender, args) => onAdd();
            DockPanel.SetDock(add, Dock.Right);
            header.Children.Add(add);

            StackPanel text = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(new TextBlock { Text = title, FontSize = 17, FontWeight = FontWeight.SemiBold, Foreground = theme.Text });
            text.Children.Add(new TextBlock { Text = subtitle, FontSize = 12, Foreground = theme.Muted, TextWrapping = TextWrapping.Wrap });
            header.Children.Add(text);

            Grid.SetRow(header, row);
            return header;
        }

        private void Refresh()
        {
            _HooksTable.SetRows(_Config.Hooks);
            _CommandsTable.SetRows(_Config.Commands);
        }

        private async void OnAddHook()
        {
            HookDefinition hook = new HookDefinition();
            if (await new HookFormDialog(hook, isNew: true).ShowDialog<bool>(this))
            {
                _Config.Hooks.Add(hook);
                Persist();
            }
        }

        private async void OnEditHook(HookDefinition hook)
        {
            if (await new HookFormDialog(hook, isNew: false).ShowDialog<bool>(this))
            {
                Persist();
            }
        }

        private async void OnDeleteHook(HookDefinition hook)
        {
            if (await new ConfirmDialog(Localizer.T("plugin.deleteHook.title"), Localizer.T("act.delete") + " \"" + hook.Name + "\"?", Localizer.T("act.delete"), destructive: true).ShowDialog<bool>(this))
            {
                _Config.Hooks.Remove(hook);
                Persist();
            }
        }

        private async void OnAddCommand()
        {
            CustomCommandDefinition command = new CustomCommandDefinition();
            if (await new CustomCommandFormDialog(command, isNew: true).ShowDialog<bool>(this))
            {
                _Config.Commands.Add(command);
                Persist();
            }
        }

        private async void OnEditCommand(CustomCommandDefinition command)
        {
            if (await new CustomCommandFormDialog(command, isNew: false).ShowDialog<bool>(this))
            {
                Persist();
            }
        }

        private async void OnDeleteCommand(CustomCommandDefinition command)
        {
            if (await new ConfirmDialog(Localizer.T("plugin.deleteCommand.title"), Localizer.T("act.delete") + " \"/" + command.Name + "\"?", Localizer.T("act.delete"), destructive: true).ShowDialog<bool>(this))
            {
                _Config.Commands.Remove(command);
                Persist();
            }
        }

        private void Persist()
        {
            try
            {
                SettingsLoader.SavePluginConfig(_Config);
            }
            catch (Exception)
            {
                // Best-effort.
            }

            Refresh();
        }
    }
}
