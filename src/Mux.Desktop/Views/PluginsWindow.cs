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

            Title = "Plugins";
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
                new TableColumn<HookDefinition>("Name", h => h.Name, new GridLength(2, GridUnitType.Star), h => h.Name, tooltip: "The hook's name."),
                new TableColumn<HookDefinition>("Event", h => h.Event.ToString(), new GridLength(1.6, GridUnitType.Star), h => h.Event.ToString(), tooltip: "The lifecycle event that fires this hook."),
                new TableColumn<HookDefinition>("Command", h => h.Command, new GridLength(3, GridUnitType.Star), h => h.Command, tooltip: "The executable that runs when the event fires."),
                new TableColumn<HookDefinition>("Blocking", h => h.Blocking ? "yes" : "no", new GridLength(1, GridUnitType.Star), h => h.Blocking ? 1 : 0, tooltip: "Whether a non-zero exit vetoes the event.")
            };
        }

        private IReadOnlyList<TableRowAction<HookDefinition>> BuildHookActions(HookDefinition hook)
        {
            return new List<TableRowAction<HookDefinition>>
            {
                new TableRowAction<HookDefinition>("Edit", h => OnEditHook(h)),
                new TableRowAction<HookDefinition>("Delete", h => OnDeleteHook(h), destructive: true)
            };
        }

        private static List<TableColumn<CustomCommandDefinition>> BuildCommandColumns()
        {
            return new List<TableColumn<CustomCommandDefinition>>
            {
                new TableColumn<CustomCommandDefinition>("Command", c => "/" + c.Name, new GridLength(2, GridUnitType.Star), c => c.Name, tooltip: "Invoke this command by typing /name."),
                new TableColumn<CustomCommandDefinition>("Description", c => string.IsNullOrEmpty(c.Description) ? "—" : c.Description, new GridLength(3, GridUnitType.Star), tooltip: "What this command does."),
                new TableColumn<CustomCommandDefinition>("Runs", c => c.Command, new GridLength(3, GridUnitType.Star), c => c.Command, tooltip: "The executable this command runs.")
            };
        }

        private IReadOnlyList<TableRowAction<CustomCommandDefinition>> BuildCommandActions(CustomCommandDefinition command)
        {
            return new List<TableRowAction<CustomCommandDefinition>>
            {
                new TableRowAction<CustomCommandDefinition>("Edit", c => OnEditCommand(c)),
                new TableRowAction<CustomCommandDefinition>("Delete", c => OnDeleteCommand(c), destructive: true)
            };
        }

        private Control BuildLayout(AppTheme theme)
        {
            Grid root = new Grid
            {
                Margin = new Thickness(20),
                RowDefinitions = new RowDefinitions("Auto,*,Auto,*")
            };

            root.Children.Add(SectionHeader("Event hooks", "Run a program out-of-process on session-start, prompt-submit, or session-end.", OnAddHook, "＋  Add hook", theme, 0));
            _HooksTable.Margin = new Thickness(0, 8, 0, 12);
            Grid.SetRow(_HooksTable, 1);
            root.Children.Add(_HooksTable);

            root.Children.Add(SectionHeader("Custom commands", "Define /name slash commands that run a program.", OnAddCommand, "＋  Add command", theme, 2));
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
            if (await new ConfirmDialog("Delete hook", "Delete \"" + hook.Name + "\"?", "Delete", destructive: true).ShowDialog<bool>(this))
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
            if (await new ConfirmDialog("Delete command", "Delete \"/" + command.Name + "\"?", "Delete", destructive: true).ShowDialog<bool>(this))
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
