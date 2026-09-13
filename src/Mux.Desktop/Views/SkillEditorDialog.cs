namespace Mux.Desktop.Views
{
    using System;
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

        /// <summary>
        /// Instantiate the skill editor.
        /// </summary>
        /// <param name="skillMdPath">The absolute path to the skill's <c>SKILL.md</c>. Required.</param>
        /// <param name="skillName">The skill's id, used in the title.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="skillMdPath"/> is null.</exception>
        public SkillEditorDialog(string skillMdPath, string skillName)
        {
            _Path = skillMdPath ?? throw new ArgumentNullException(nameof(skillMdPath));

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

            TextBlock path = new TextBlock { Text = _Path, Foreground = theme.Muted, FontSize = 11, TextWrapping = TextWrapping.Wrap };
            DockPanel.SetDock(path, Dock.Top);
            root.Children.Add(path);

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
            try
            {
                File.WriteAllText(_Path, _Editor.Text ?? string.Empty);
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
