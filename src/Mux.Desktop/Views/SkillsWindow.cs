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
    using Mux.Desktop.I18n;

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
        private readonly ComboBox _CategoryFilter = new ComboBox { MinWidth = 200, VerticalAlignment = VerticalAlignment.Center };
        private List<SkillStatus> _All = new List<SkillStatus>();
        private bool _UpdatingFilter;

        /// <summary>
        /// Instantiate the skills manager.
        /// </summary>
        public SkillsWindow()
        {
            MuxSettings settings = SettingsLoader.LoadSettings();
            _SkillsEnabled = settings.SkillsEnabled;
            _SkillsDirectory = SettingsLoader.ResolveSkillsDirectory(settings);

            AppTheme theme = AppTheme.Current;

            Title = Localizer.T("nav.skills");
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
                new TableColumn<SkillStatus>(Localizer.T("skill.col.title"), s => string.IsNullOrEmpty(s.Title) ? s.Name : s.Title, new GridLength(2.5, GridUnitType.Star), s => s.Title, s => s.Enabled ? Localizer.T("skill.badge.enabled") : null, tooltip: Localizer.T("skill.col.title.tip")),
                new TableColumn<SkillStatus>(Localizer.T("skill.col.id"), s => s.Name, new GridLength(2, GridUnitType.Star), s => s.Name, tooltip: Localizer.T("skill.col.id.tip")),
                new TableColumn<SkillStatus>(Localizer.T("skill.col.category"), s => s.Category + (s.CategoryOverridden ? " *" : string.Empty), new GridLength(1.4, GridUnitType.Star), s => s.Category, tooltip: Localizer.T("skill.col.category.tip")),
                new TableColumn<SkillStatus>(Localizer.T("skill.col.commands"), s => s.CommandCount.ToString(), new GridLength(1, GridUnitType.Star), s => s.CommandCount, tooltip: Localizer.T("skill.col.commands.tip")),
                new TableColumn<SkillStatus>(Localizer.T("skill.col.status"), s => s.Valid ? Localizer.T("skill.status.valid") : Localizer.T("skill.status.invalid"), new GridLength(1.4, GridUnitType.Star), s => s.Valid ? 1 : 0, tooltip: Localizer.T("skill.col.status.tip"))
            };
        }

        private IReadOnlyList<TableRowAction<SkillStatus>> BuildActions(SkillStatus status)
        {
            List<TableRowAction<SkillStatus>> actions = new List<TableRowAction<SkillStatus>>();
            if (status.Valid)
            {
                actions.Add(new TableRowAction<SkillStatus>(status.Enabled ? Localizer.T("skill.action.disable") : Localizer.T("skill.action.enable"), s => OnToggle(s)));
            }

            actions.Add(new TableRowAction<SkillStatus>(Localizer.T("act.edit"), s => OnEdit(s)));
            actions.Add(new TableRowAction<SkillStatus>(Localizer.T("skill.action.setCategory"), s => OnSetCategory(s)));
            actions.Add(new TableRowAction<SkillStatus>(Localizer.T("act.delete"), s => OnDelete(s), destructive: true));
            return actions;
        }

        private Control BuildLayout(AppTheme theme)
        {
            DockPanel root = new DockPanel { Margin = new Thickness(20) };

            DockPanel headerRow = new DockPanel();
            StackPanel titleBlock = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
            titleBlock.Children.Add(new TextBlock { Text = Localizer.T("nav.skills"), FontSize = 20, FontWeight = FontWeight.SemiBold, Foreground = theme.Text });
            titleBlock.Children.Add(new TextBlock { Text = _SkillsDirectory, Foreground = theme.Muted, FontSize = 11, TextWrapping = TextWrapping.Wrap });
            if (!_SkillsEnabled)
            {
                titleBlock.Children.Add(new TextBlock { Text = Localizer.T("skill.disabledWarning"), Foreground = new SolidColorBrush(Color.Parse("#bf8700")), FontSize = 12 });
            }

            DockPanel.SetDock(titleBlock, Dock.Left);
            headerRow.Children.Add(titleBlock);

            StackPanel headerButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top };

            _CategoryFilter.Tip(Localizer.T("skill.filter.tip"));
            _CategoryFilter.SelectionChanged += (object? sender, SelectionChangedEventArgs e) => { if (!_UpdatingFilter) ApplyFilter(); };
            headerButtons.Children.Add(_CategoryFilter);

            Button import = new Button { Content = "⬇  " + Localizer.T("skill.import"), Background = theme.SurfaceAlt, Foreground = theme.Text, BorderBrush = theme.Border, BorderThickness = new Thickness(1), Padding = new Thickness(12, 6, 12, 6) };
            import.Tip(Localizer.T("skill.import.tip"));
            import.Click += (sender, args) => OnImport();
            headerButtons.Children.Add(import);

            Button packs = new Button { Content = "▣  " + Localizer.T("skill.packs"), Background = theme.SurfaceAlt, Foreground = theme.Text, BorderBrush = theme.Border, BorderThickness = new Thickness(1), Padding = new Thickness(12, 6, 12, 6) };
            packs.Tip(Localizer.T("skill.packs.tip"));
            packs.Click += (sender, args) => OnPacks();
            headerButtons.Children.Add(packs);

            Button add = new Button { Content = "＋  " + Localizer.T("skill.add"), Background = theme.AccentButton, Foreground = theme.AccentText, Padding = new Thickness(12, 6, 12, 6) };
            add.Tip(Localizer.T("skill.add.tip"));
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
                IReadOnlyList<Skill> skills = loader.Discover();
                SkillCategories.ApplyOverrides(skills);
                _All = new List<SkillStatus>(new SkillCatalog(skills).GetStatus());
            }
            catch (Exception)
            {
                _All = new List<SkillStatus>();
            }

            RefreshFilterOptions();
            ApplyFilter();
        }

        // Rebuilds the category picker from the categories in use (with counts), keeping the current choice.
        private void RefreshFilterOptions()
        {
            string? selected = SelectedCategory();
            Dictionary<string, int> counts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (SkillStatus status in _All)
            {
                counts[status.Category] = counts.TryGetValue(status.Category, out int n) ? n + 1 : 1;
            }

            List<string> categories = new List<string>(counts.Keys);
            categories.Sort((string a, string b) => SkillCategories.Order(a) != SkillCategories.Order(b) ? SkillCategories.Order(a).CompareTo(SkillCategories.Order(b)) : string.CompareOrdinal(a, b));
            List<string> items = new List<string> { Localizer.T("skill.filter.all") };
            int selectedIndex = 0;
            foreach (string category in categories)
            {
                if (string.Equals(category, selected, StringComparison.Ordinal)) selectedIndex = items.Count;
                items.Add(category + " (" + counts[category] + ")");
            }

            _UpdatingFilter = true;
            _CategoryFilter.ItemsSource = items;
            _CategoryFilter.SelectedIndex = selectedIndex;
            _UpdatingFilter = false;
        }

        private string? SelectedCategory()
        {
            if (_CategoryFilter.SelectedIndex <= 0 || !(_CategoryFilter.SelectedItem is string item)) return null;
            int paren = item.LastIndexOf(" (", StringComparison.Ordinal);
            return paren > 0 ? item.Substring(0, paren) : item;
        }

        private void ApplyFilter()
        {
            string? category = SelectedCategory();
            _Table.SetRows(category == null ? _All : _All.FindAll(s => string.Equals(s.Category, category, StringComparison.Ordinal)));
        }

        private async void OnSetCategory(SkillStatus status)
        {
            string? current = new SkillManager(_SkillsDirectory).GetCategoryOverride(status.Name);
            if (await new SkillCategoryDialog(_SkillsDirectory, status.Name, status.Category, current).ShowDialog<bool>(this))
            {
                Reload();
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

        private async void OnPacks()
        {
            if (await new SkillPacksDialog(_SkillsDirectory).ShowDialog<bool>(this))
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
                    Title = Localizer.T("skill.importPicker.title"),
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
                await new ConfirmDialog(Localizer.T("skill.importFailed.title"), Localizer.T("skill.importFailed.notLocal"), Localizer.T("skill.ok"), destructive: false).ShowDialog<bool>(this);
                return;
            }

            try
            {
                string id = new SkillManager(_SkillsDirectory).Import(source, null);
                Reload();
                await new ConfirmDialog(Localizer.T("skill.imported.title"), Localizer.T("skill.imported.message") + " \"" + id + "\"", Localizer.T("skill.ok"), destructive: false).ShowDialog<bool>(this);
            }
            catch (Exception ex)
            {
                await new ConfirmDialog(Localizer.T("skill.importFailed.title"), ex.Message, Localizer.T("skill.ok"), destructive: false).ShowDialog<bool>(this);
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
            bool confirmed = await new ConfirmDialog(Localizer.T("skill.delete.title"), Localizer.T("skill.delete.confirm") + " \"" + status.Name + "\"?", Localizer.T("act.delete"), destructive: true).ShowDialog<bool>(this);
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
