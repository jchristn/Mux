namespace Mux.Desktop.Views
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using Avalonia;
    using Avalonia.Controls;
    using Avalonia.Layout;
    using Avalonia.Media;
    using Mux.Desktop.I18n;

    /// <summary>
    /// A full-height editor for a skill's <c>SKILL.md</c> (parity with the TUI's skill editor). Loads the file
    /// into a monospace text area and, on Save, writes it back to disk and returns true.
    /// </summary>
    public sealed class SkillEditorDialog : Window
    {
        private readonly string _Path;
        private readonly TextBox _Editor = new TextBox
        {
            AcceptsReturn = true,
            AcceptsTab = true,
            TextWrapping = TextWrapping.NoWrap,
            FontFamily = new FontFamily("Cascadia Mono,Consolas,Menlo,monospace"),
            FontSize = 12.5,
            VerticalContentAlignment = VerticalAlignment.Top
        };
        private readonly TextBlock _Error = new TextBlock { Foreground = new SolidColorBrush(Color.Parse("#cf222e")), FontSize = 12, TextWrapping = TextWrapping.Wrap };
        private readonly TextBox _Category = new TextBox { Width = 260 };
        private readonly ComboBox _KnownCategories = new ComboBox { Width = 220 };
        private readonly string _SkillName;
        private readonly string _SkillsDirectory;
        private readonly string? _OriginalOverride;

        /// <summary>
        /// Instantiate the skill editor.
        /// </summary>
        /// <param name="skillMdPath">The absolute path to the skill's <c>SKILL.md</c>. Required.</param>
        /// <param name="skillName">The skill's id, used in the title.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="skillMdPath"/> is null.</exception>
        public SkillEditorDialog(string skillMdPath, string skillName)
        {
            _Path = skillMdPath ?? throw new ArgumentNullException(nameof(skillMdPath));
            _SkillName = skillName ?? string.Empty;
            _SkillsDirectory = Path.GetDirectoryName(Path.GetDirectoryName(_Path) ?? string.Empty) ?? string.Empty;
            _OriginalOverride = _SkillName.Length > 0 && _SkillsDirectory.Length > 0 ? new Mux.Core.Skills.SkillManager(_SkillsDirectory).GetCategoryOverride(_SkillName) : null;
            _Category.Text = _OriginalOverride ?? string.Empty;
            List<string> knownOptions = new List<string> { Localizer.T("skill.category.pick") };
            knownOptions.AddRange(Mux.Core.Skills.SkillCategories.Known);
            _KnownCategories.ItemsSource = knownOptions;
            _KnownCategories.SelectedIndex = 0;
            _KnownCategories.SelectionChanged += (object? sender, SelectionChangedEventArgs e) =>
            {
                if (_KnownCategories.SelectedIndex > 0 && _KnownCategories.SelectedItem is string picked) _Category.Text = picked;
            };

            AppTheme theme = AppTheme.Current;

            Title = Localizer.T("skill.edit.title") + " · " + skillName;
            Icon = IconResources.LoadWindowIcon();
            Width = 1230;
            Height = 900;
            MinWidth = 560;
            MinHeight = 400;
            CanResize = true;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = theme.Surface;

            try
            {
                _Editor.Text = File.Exists(_Path) ? File.ReadAllText(_Path) : string.Empty;
            }
            catch (Exception exception)
            {
                _Editor.Text = string.Empty;
                _Error.Text = Localizer.T("skill.editor.readError") + exception.Message;
            }

            Content = BuildContent(theme);
        }

        private Control BuildContent(AppTheme theme)
        {
            DockPanel root = new DockPanel { Margin = new Thickness(20) };

            // Header: the file path, with a copy button that copies the editor's current text (unsaved edits
            // included) so the whole SKILL.md can be pasted elsewhere.
            DockPanel header = new DockPanel();
            Button copy = CopyButton.Create(() => _Editor.Text ?? string.Empty, theme, this);
            DockPanel.SetDock(copy, Dock.Right);
            header.Children.Add(copy);
            TextBlock path = new TextBlock { Text = _Path, Foreground = theme.Muted, FontSize = 11, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
            header.Children.Add(path);
            DockPanel.SetDock(header, Dock.Top);
            root.Children.Add(header);

            // The category is a per-user override stored in skills.json (not in SKILL.md); blank uses the file's.
            StackPanel categoryRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 10, 0, 0) };
            categoryRow.Children.Add(new TextBlock { Text = Localizer.T("skill.category.label"), FontWeight = FontWeight.SemiBold, Foreground = theme.Text, VerticalAlignment = VerticalAlignment.Center });
            categoryRow.Children.Add(_KnownCategories);
            categoryRow.Children.Add(_Category);
            categoryRow.Tip(Localizer.T("skill.category.help"));
            DockPanel.SetDock(categoryRow, Dock.Top);
            root.Children.Add(categoryRow);

            StackPanel buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Margin = new Thickness(0, 12, 0, 0) };
            Button cancel = new Button { Content = Localizer.T("act.cancel") };
            cancel.Tip(Localizer.T("skill.editor.cancel.tip"));
            cancel.Click += (sender, args) => Close(false);
            buttons.Children.Add(cancel);
            Button ok = new Button { Content = Localizer.T("act.save"), Background = theme.AccentButton, Foreground = theme.AccentText };
            ok.Tip(Localizer.T("skill.editor.save.tip"));
            ok.Click += (sender, args) => Ok();
            buttons.Children.Add(ok);
            DockPanel.SetDock(buttons, Dock.Bottom);
            root.Children.Add(buttons);

            DockPanel.SetDock(_Error, Dock.Bottom);
            root.Children.Add(_Error);

            _Editor.Tip(Localizer.T("skill.editor.body.tip"));
            root.Children.Add(new Border
            {
                BorderBrush = theme.Border,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Margin = new Thickness(0, 10, 0, 0),
                Child = _Editor
            });
            return root;
        }

        private void Ok()
        {
            string typed = (_Category.Text ?? string.Empty).Trim();
            if (!Mux.Core.Skills.SkillCategories.TryParse(typed, out string? normalized, out string categoryError))
            {
                _Error.Text = categoryError;
                return;
            }

            try
            {
                File.WriteAllText(_Path, _Editor.Text ?? string.Empty);
                if (_SkillName.Length > 0 && _SkillsDirectory.Length > 0 && !string.Equals(normalized, _OriginalOverride, StringComparison.Ordinal))
                {
                    new Mux.Core.Skills.SkillManager(_SkillsDirectory).SetCategory(_SkillName, normalized);
                }
            }
            catch (Exception exception)
            {
                _Error.Text = Localizer.T("skill.editor.saveError") + exception.Message;
                return;
            }

            Close(true);
        }
    }
}
