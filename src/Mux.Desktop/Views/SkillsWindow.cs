namespace Mux.Desktop.Views
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using Avalonia;
    using Avalonia.Controls;
    using Avalonia.Layout;
    using Avalonia.Media;
    using Avalonia.Platform.Storage;
    using Mux.Core.Models;
    using Mux.Core.Settings;
    using Mux.Core.Skills;

    /// <summary>
    /// A manager for installed skills (parity with the TUI's <c>/skills</c>): a sortable table with a green
    /// <c>enabled</c> badge and per-row actions (enable/disable, edit its <c>SKILL.md</c>, delete), plus an
    /// "Add skill" scaffold. Discovers from the resolved skills directory via <see cref="SkillLoader"/> and
    /// mutates via <see cref="SkillManager"/>.
    /// </summary>
    public sealed class SkillsWindow : Window
    {
        private readonly string _SkillsDirectory;
        private readonly bool _SkillsEnabled;
        private readonly DataTableView<SkillStatus> _Table;

        /// <summary>
        /// Instantiate the skills manager.
        /// </summary>
        public SkillsWindow()
        {
            MuxSettings settings = SettingsLoader.LoadSettings();
            _SkillsEnabled = settings.SkillsEnabled;
            _SkillsDirectory = SettingsLoader.ResolveSkillsDirectory(settings);

            AppTheme theme = AppTheme.Current;

            Title = "Skills";
            Icon = IconResources.LoadWindowIcon();
            Width = 1350;
            Height = 725;
            MinWidth = 720;
            MinHeight = 360;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = theme.Surface;

            _Table = new DataTableView<SkillStatus>(BuildColumns(), BuildActions, OnEdit);
            Content = BuildLayout(theme);
            Reload();
        }

        private static List<TableColumn<SkillStatus>> BuildColumns()
        {
            return new List<TableColumn<SkillStatus>>
            {
                new TableColumn<SkillStatus>("Title", s => string.IsNullOrEmpty(s.Title) ? s.Name : s.Title, new GridLength(2.5, GridUnitType.Star), s => s.Title, s => s.Enabled ? "enabled" : null, tooltip: "The skill's human-readable title. The green “enabled” badge marks skills exposed to the model."),
                new TableColumn<SkillStatus>("Id", s => s.Name, new GridLength(2, GridUnitType.Star), s => s.Name, tooltip: "The skill's folder id under the skills directory."),
                new TableColumn<SkillStatus>("Commands", s => s.CommandCount.ToString(), new GridLength(1, GridUnitType.Star), s => s.CommandCount, tooltip: "How many commands this skill defines."),
                new TableColumn<SkillStatus>("Status", s => s.Valid ? "valid" : "invalid", new GridLength(1.4, GridUnitType.Star), s => s.Valid ? 1 : 0, tooltip: "Whether the skill's SKILL.md parses correctly. Invalid skills cannot be enabled.")
            };
        }

        private IReadOnlyList<TableRowAction<SkillStatus>> BuildActions(SkillStatus status)
        {
            List<TableRowAction<SkillStatus>> actions = new List<TableRowAction<SkillStatus>>();
            if (status.Valid)
            {
                actions.Add(new TableRowAction<SkillStatus>(status.Enabled ? "Disable" : "Enable", s => OnToggle(s)));
            }

            actions.Add(new TableRowAction<SkillStatus>("Edit", s => OnEdit(s)));
            actions.Add(new TableRowAction<SkillStatus>("Delete", s => OnDelete(s), destructive: true));
            return actions;
        }

        private Control BuildLayout(AppTheme theme)
        {
            DockPanel root = new DockPanel { Margin = new Thickness(20) };

            DockPanel headerRow = new DockPanel();
            StackPanel titleBlock = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
            titleBlock.Children.Add(new TextBlock { Text = "Skills", FontSize = 20, FontWeight = FontWeight.SemiBold, Foreground = theme.Text });
            titleBlock.Children.Add(new TextBlock { Text = _SkillsDirectory, Foreground = theme.Muted, FontSize = 11, TextWrapping = TextWrapping.Wrap });
            if (!_SkillsEnabled)
            {
                titleBlock.Children.Add(new TextBlock { Text = "Skills are disabled in Settings — enable \"Load user skills\" to use them.", Foreground = new SolidColorBrush(Color.Parse("#bf8700")), FontSize = 12 });
            }

            DockPanel.SetDock(titleBlock, Dock.Left);
            headerRow.Children.Add(titleBlock);

            StackPanel headerButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top };

            Button import = new Button { Content = "⬇  Import", Background = theme.SurfaceAlt, Foreground = theme.Text, BorderBrush = theme.Border, BorderThickness = new Thickness(1), Padding = new Thickness(12, 6, 12, 6) };
            import.Tip("Import an existing skill from another folder (validates and copies it under the skills directory).");
            import.Click += (sender, args) => OnImport();
            headerButtons.Children.Add(import);

            Button add = new Button { Content = "＋  Add skill", Background = theme.AccentButton, Foreground = theme.AccentText, Padding = new Thickness(12, 6, 12, 6) };
            add.Tip("Scaffold a new skill (creates a SKILL.md folder under the skills directory).");
            add.Click += (sender, args) => OnAdd();
            headerButtons.Children.Add(add);

            DockPanel.SetDock(headerButtons, Dock.Right);
            headerRow.Children.Add(headerButtons);

            DockPanel.SetDock(headerRow, Dock.Top);
            root.Children.Add(headerRow);

            _Table.Margin = new Thickness(0, 14, 0, 0);
            root.Children.Add(_Table);
            return root;
        }

        private void Reload()
        {
            try
            {
                SkillLoader loader = new SkillLoader(_SkillsDirectory);
                _Table.SetRows(new SkillCatalog(loader.Discover()).GetStatus());
            }
            catch (Exception)
            {
                _Table.SetRows(new List<SkillStatus>());
            }
        }

        private void OnToggle(SkillStatus status)
        {
            try
            {
                new SkillManager(_SkillsDirectory).SetEnabled(status.Name, !status.Enabled);
            }
            catch (Exception)
            {
                // Best-effort.
            }

            Reload();
        }

        private async void OnAdd()
        {
            if (await new SkillScaffoldDialog(_SkillsDirectory).ShowDialog<bool>(this))
            {
                Reload();
            }
        }

        private async void OnImport()
        {
            // Pick a source skill folder, then validate-and-copy it under the skills directory (parity with
            // the TUI's "Import skill…"). The source keeps its folder name as the imported id.
            IReadOnlyList<IStorageFolder> picked;
            try
            {
                picked = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
                {
                    Title = "Import skill — choose the skill's folder",
                    AllowMultiple = false
                });
            }
            catch (Exception)
            {
                return;
            }

            if (picked == null || picked.Count == 0)
            {
                return;
            }

            string? source = picked[0].TryGetLocalPath();
            if (string.IsNullOrEmpty(source))
            {
                await new ConfirmDialog("Import failed", "That location can't be read from disk. Choose a local folder.", "OK", destructive: false).ShowDialog<bool>(this);
                return;
            }

            try
            {
                string id = new SkillManager(_SkillsDirectory).Import(source, null);
                Reload();
                await new ConfirmDialog("Skill imported", "Imported skill \"" + id + "\" and enabled it.", "OK", destructive: false).ShowDialog<bool>(this);
            }
            catch (Exception ex)
            {
                await new ConfirmDialog("Import failed", ex.Message, "OK", destructive: false).ShowDialog<bool>(this);
            }
        }

        private async void OnEdit(SkillStatus status)
        {
            string path = Path.Combine(_SkillsDirectory, status.Name, "SKILL.md");
            if (await new SkillEditorDialog(path, status.Name).ShowDialog<bool>(this))
            {
                Reload();
            }
        }

        private async void OnDelete(SkillStatus status)
        {
            bool confirmed = await new ConfirmDialog("Delete skill", "Delete skill \"" + status.Name + "\" from disk?", "Delete", destructive: true).ShowDialog<bool>(this);
            if (!confirmed)
            {
                return;
            }

            try
            {
                new SkillManager(_SkillsDirectory).Remove(status.Name);
            }
            catch (Exception)
            {
                // Best-effort.
            }

            Reload();
        }
    }
}
