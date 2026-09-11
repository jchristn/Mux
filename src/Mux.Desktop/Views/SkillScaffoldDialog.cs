namespace Mux.Desktop.Views
{
    using System;
    using Avalonia;
    using Avalonia.Controls;
    using Avalonia.Layout;
    using Avalonia.Media;
    using Mux.Core.Models;
    using Mux.Core.Skills;

    /// <summary>
    /// A form for creating a new skill (parity with the TUI's skill scaffold): id, title, description, whether
    /// it mutates the workspace, and the interpreter. On Save it writes the skill via
    /// <see cref="SkillManager.Create"/> and returns true; validation and creation errors surface inline.
    /// </summary>
    public sealed class SkillScaffoldDialog : Window
    {
        private readonly string _SkillsDirectory;
        private readonly TextBox _Id = new TextBox { PlaceholderText = "my-skill" };
        private readonly TextBox _Title = new TextBox();
        private readonly TextBox _Description = new TextBox { AcceptsReturn = true, MinHeight = 60, TextWrapping = TextWrapping.Wrap };
        private readonly TextBox _Interpreter = new TextBox { Text = "bash" };
        private readonly CheckBox _Mutating = new CheckBox { Content = "This skill can modify the workspace (mutating)" };
        private readonly TextBlock _Error = new TextBlock { Foreground = new SolidColorBrush(Color.Parse("#cf222e")), FontSize = 12, TextWrapping = TextWrapping.Wrap };

        /// <summary>
        /// Instantiate the skill scaffold form.
        /// </summary>
        /// <param name="skillsDirectory">The directory new skills are created under. Required.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="skillsDirectory"/> is null.</exception>
        public SkillScaffoldDialog(string skillsDirectory)
        {
            _SkillsDirectory = skillsDirectory ?? throw new ArgumentNullException(nameof(skillsDirectory));

            Title = "Add skill";
            Icon = IconResources.LoadWindowIcon();
            Width = 560;
            Height = 520;
            MinWidth = 460;
            MinHeight = 400;
            CanResize = true;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = AppTheme.Current.Surface;

            Content = BuildContent();
        }

        private Control BuildContent()
        {
            AppTheme theme = AppTheme.Current;
            DockPanel root = new DockPanel { Margin = new Thickness(20) };

            StackPanel buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Margin = new Thickness(0, 12, 0, 0) };
            Button cancel = new Button { Content = "Cancel" };
            cancel.Tip("Close without creating a skill.");
            cancel.Click += (sender, args) => Close(false);
            buttons.Children.Add(cancel);
            Button ok = new Button { Content = "Create", Background = theme.AccentButton, Foreground = theme.AccentText };
            ok.Tip("Create the skill folder and its SKILL.md.");
            ok.Click += (sender, args) => Ok();
            buttons.Children.Add(ok);
            DockPanel.SetDock(buttons, Dock.Bottom);
            root.Children.Add(buttons);

            DockPanel.SetDock(_Error, Dock.Bottom);
            root.Children.Add(_Error);

            StackPanel form = new StackPanel { Spacing = 10, Margin = new Thickness(0, 0, 14, 0) };
            form.Children.Add(Field("Id (lowercase, hyphen-separated)", _Id, "The skill's folder name and unique id, e.g. my-skill. Lowercase letters, digits, and hyphens only."));
            form.Children.Add(Field("Title", _Title, "A human-readable title shown in the skills list."));
            form.Children.Add(Field("Description", _Description, "What the skill does; this is how the model decides when to use it."));
            form.Children.Add(Field("Interpreter", _Interpreter, "The interpreter its commands run under (e.g. bash, python)."));
            form.Children.Add(_Mutating.Tip("Check if this skill can modify the workspace, so its commands require approval."));
            root.Children.Add(new ScrollViewer { Content = form });
            return root;
        }

        private Control Field(string label, Control control, string tip)
        {
            StackPanel panel = new StackPanel { Spacing = 4 };
            panel.Children.Add(new TextBlock { Text = label, Foreground = AppTheme.Current.Text, FontWeight = FontWeight.SemiBold });
            control.HorizontalAlignment = HorizontalAlignment.Stretch;
            panel.Children.Add(control);
            return panel.Tip(tip);
        }

        private void Ok()
        {
            string id = (_Id.Text ?? string.Empty).Trim();
            if (id.Length == 0)
            {
                _Error.Text = "Id is required.";
                return;
            }

            if (!SkillManager.IsValidId(id))
            {
                _Error.Text = "Id must be lowercase and hyphen-separated (e.g. my-skill).";
                return;
            }

            SkillScaffold scaffold = new SkillScaffold
            {
                Id = id,
                Title = (_Title.Text ?? string.Empty).Trim(),
                Description = (_Description.Text ?? string.Empty).Trim(),
                Interpreter = (_Interpreter.Text ?? string.Empty).Trim(),
                Mutating = _Mutating.IsChecked ?? false
            };

            try
            {
                new SkillManager(_SkillsDirectory).Create(scaffold);
            }
            catch (Exception exception)
            {
                _Error.Text = exception.Message;
                return;
            }

            Close(true);
        }
    }
}
