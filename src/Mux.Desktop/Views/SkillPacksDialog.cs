namespace Mux.Desktop.Views
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using Avalonia;
    using Avalonia.Controls;
    using Avalonia.Layout;
    using Avalonia.Media;
    using Mux.Core.Skills.Packaging;
    using Mux.Desktop.I18n;

    /// <summary>
    /// Browses the opt-in skill packs shipped with mux and installs or removes them (or single skills from them) in the
    /// skills directory through <see cref="SkillPackInstaller"/>. Returns true when anything was installed or removed,
    /// so the skills window reloads.
    /// </summary>
    public sealed class SkillPacksDialog : Window
    {
        #region Private-Members

        private readonly SkillPackInstaller _Installer;
        private readonly ComboBox _Packs = new ComboBox { MinWidth = 360, HorizontalAlignment = HorizontalAlignment.Left };
        private readonly TextBlock _Description = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12 };
        private readonly StackPanel _Rows = new StackPanel { Spacing = 6 };
        private readonly TextBlock _Status = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12 };
        private bool _Changed;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="SkillPacksDialog"/> class.
        /// </summary>
        /// <param name="skillsDirectory">The skills directory. Must not be blank.</param>
        /// <param name="catalog">The packs to offer; null uses the packs embedded in mux.</param>
        /// <exception cref="ArgumentException">Thrown when the directory is blank.</exception>
        public SkillPacksDialog(string skillsDirectory, SkillPackCatalog? catalog = null)
        {
            _Installer = new SkillPackInstaller(skillsDirectory, catalog);
            AppTheme theme = AppTheme.Current;
            Title = Localizer.T("skill.packs.title");
            Icon = IconResources.LoadWindowIcon();
            Width = 820;
            Height = 640;
            MinWidth = 560;
            MinHeight = 420;
            CanResize = true;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = theme.Surface;
            _Description.Foreground = theme.Muted;
            _Status.Foreground = theme.Muted;

            List<string> options = new List<string>();
            foreach (SkillPack pack in _Installer.Catalog.Packs)
            {
                options.Add(pack.Id);
            }

            _Packs.ItemsSource = options;
            _Packs.SelectionChanged += (object? sender, SelectionChangedEventArgs e) => RenderPack();
            Content = BuildContent(theme);
            if (options.Count > 0)
            {
                _Packs.SelectedIndex = 0;
            }
            else
            {
                _Description.Text = Localizer.T("skill.packs.none");
            }
        }

        #endregion

        #region Private-Methods

        private Control BuildContent(AppTheme theme)
        {
            DockPanel root = new DockPanel { Margin = new Thickness(20) };

            StackPanel buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Margin = new Thickness(0, 12, 0, 0) };
            Button installAll = new Button { Content = Localizer.T("skill.packs.installAll") };
            installAll.Click += (object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Apply(install: true, skillId: null);
            buttons.Children.Add(installAll);
            Button removeAll = new Button { Content = Localizer.T("skill.packs.removeAll") };
            removeAll.Click += (object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Apply(install: false, skillId: null);
            buttons.Children.Add(removeAll);
            Button close = new Button { Content = Localizer.T("skill.packs.close"), Background = theme.AccentButton, Foreground = theme.AccentText };
            close.Click += (object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Close(_Changed);
            buttons.Children.Add(close);
            DockPanel.SetDock(buttons, Dock.Bottom);
            root.Children.Add(buttons);

            DockPanel.SetDock(_Status, Dock.Bottom);
            root.Children.Add(_Status);

            StackPanel header = new StackPanel { Spacing = 8, Margin = new Thickness(0, 0, 0, 10) };
            StackPanel picker = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            picker.Children.Add(new TextBlock { Text = Localizer.T("skill.packs.pack"), FontWeight = FontWeight.SemiBold, Foreground = theme.Text, VerticalAlignment = VerticalAlignment.Center });
            picker.Children.Add(_Packs);
            header.Children.Add(picker);
            header.Children.Add(_Description);
            DockPanel.SetDock(header, Dock.Top);
            root.Children.Add(header);

            root.Children.Add(new ScrollViewer { Classes = { Mux.Desktop.Styling.MuxThemeStyles.GutterClass }, Content = _Rows });
            return root;
        }

        private SkillPack? SelectedPack()
        {
            return _Packs.SelectedItem is string id ? _Installer.Catalog.Find(id) : null;
        }

        private void RenderPack()
        {
            AppTheme theme = AppTheme.Current;
            _Rows.Children.Clear();
            SkillPack? pack = SelectedPack();
            if (pack == null)
            {
                return;
            }

            _Description.Text = pack.Title + ": " + pack.Description
                + (pack.License.Length > 0 ? "  ·  " + pack.License : string.Empty)
                + "  ·  " + _Installer.InstalledCount(pack.Id).ToString(CultureInfo.InvariantCulture) + "/" + pack.Skills.Count.ToString(CultureInfo.InvariantCulture);
            foreach (BundledSkill skill in pack.Skills)
            {
                bool installed = _Installer.IsInstalled(pack.Id, skill.Id);
                Grid row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
                StackPanel text = new StackPanel { Spacing = 2 };
                text.Children.Add(new TextBlock { Text = skill.Id, FontWeight = FontWeight.SemiBold, Foreground = theme.Text });
                text.Children.Add(new TextBlock { Text = SkillImportNormalizer.ReadFrontmatterValue(skill.SkillMarkdown, "description") ?? string.Empty, Foreground = theme.Muted, FontSize = 12, TextWrapping = TextWrapping.Wrap });
                Grid.SetColumn(text, 0);
                row.Children.Add(text);
                string skillId = skill.Id;
                Button action = new Button { Content = installed ? Localizer.T("skill.packs.remove") : Localizer.T("skill.packs.install"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
                action.Click += (object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Apply(!installed, skillId);
                Grid.SetColumn(action, 1);
                row.Children.Add(action);
                _Rows.Children.Add(row);
            }
        }

        private void Apply(bool install, string? skillId)
        {
            SkillPack? pack = SelectedPack();
            if (pack == null)
            {
                return;
            }

            try
            {
                SkillPackResult result = install ? _Installer.Install(pack.Id, skillId) : _Installer.Remove(pack.Id, skillId);
                _Changed |= result.Installed.Count > 0 || result.Removed.Count > 0;
                _Status.Text = string.Format(CultureInfo.CurrentCulture, Localizer.T("skill.packs.result"), result.Installed.Count, result.Removed.Count, result.Skipped.Count)
                    + (result.Skipped.Count > 0 ? " " + string.Join("; ", result.Skipped) : string.Empty);
            }
            catch (Exception ex) when (ex is KeyNotFoundException || ex is InvalidOperationException || ex is System.IO.IOException || ex is UnauthorizedAccessException)
            {
                _Status.Text = ex.Message;
            }

            RenderPack();
        }

        #endregion
    }
}
