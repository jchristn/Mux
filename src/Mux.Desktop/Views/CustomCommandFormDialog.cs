namespace Mux.Desktop.Views
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using Avalonia;
    using Avalonia.Controls;
    using Avalonia.Layout;
    using Avalonia.Media;
    using Mux.Core.Plugins;
    using Mux.Desktop.I18n;

    /// <summary>
    /// A form for creating or editing a custom slash command (parity with the TUI's commands editor): name,
    /// description, command, args, and timeout. Pre-fills from the supplied
    /// <see cref="CustomCommandDefinition"/> and, on Save, writes the edited values back and returns true.
    /// </summary>
    public sealed class CustomCommandFormDialog : Window
    {
        private readonly CustomCommandDefinition _Command;
        private readonly TextBox _Name = new TextBox();
        private readonly TextBox _Description = new TextBox();
        private readonly TextBox _Exe = new TextBox();
        private readonly TextBox _Args = new TextBox { AcceptsReturn = true, MinHeight = 60, TextWrapping = TextWrapping.Wrap };
        private readonly TextBox _Timeout = new TextBox();
        private readonly TextBlock _Error = new TextBlock { Foreground = new SolidColorBrush(Color.Parse("#cf222e")), FontSize = 12, TextWrapping = TextWrapping.Wrap };

        /// <summary>
        /// Instantiate the custom-command form.
        /// </summary>
        /// <param name="command">The command to edit (pre-filled and written back on Save). Required.</param>
        /// <param name="isNew">True when creating a new command (affects the title only).</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="command"/> is null.</exception>
        public CustomCommandFormDialog(CustomCommandDefinition command, bool isNew)
        {
            _Command = command ?? throw new ArgumentNullException(nameof(command));

            Title = isNew ? Localizer.T("command.add.title") : Localizer.T("command.edit.title");
            Icon = IconResources.LoadWindowIcon();
            Width = 560;
            SizeToContent = SizeToContent.Height;
            CanResize = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = AppTheme.Current.Surface;

            _Name.Text = command.Name;
            _Description.Text = command.Description;
            _Exe.Text = command.Command;
            _Args.Text = string.Join(Environment.NewLine, command.Args);
            _Timeout.Text = command.TimeoutMs.ToString(CultureInfo.InvariantCulture);

            Content = BuildContent();
        }

        private Control BuildContent()
        {
            AppTheme theme = AppTheme.Current;
            StackPanel form = new StackPanel { Margin = new Thickness(24), Spacing = 10 };
            form.Children.Add(Field(Localizer.T("command.name"), _Name, Localizer.T("command.name.tip")));
            form.Children.Add(Field(Localizer.T("col.description"), _Description, Localizer.T("command.description.tip")));
            form.Children.Add(Field(Localizer.T("command.command"), _Exe, Localizer.T("command.command.tip")));
            form.Children.Add(Field(Localizer.T("command.args"), _Args, Localizer.T("command.args.tip")));
            form.Children.Add(Field(Localizer.T("command.timeout"), _Timeout, Localizer.T("command.timeout.tip")));
            form.Children.Add(_Error);

            StackPanel buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
            Button cancel = new Button { Content = Localizer.T("act.cancel") };
            cancel.Tip(Localizer.T("command.cancel.tip"));
            cancel.Click += (sender, args) => Close(false);
            buttons.Children.Add(cancel);
            Button ok = new Button { Content = Localizer.T("act.save"), Background = theme.AccentButton, Foreground = theme.AccentText };
            ok.Tip(Localizer.T("command.save.tip"));
            ok.Click += (sender, args) => Ok();
            buttons.Children.Add(ok);
            form.Children.Add(buttons);

            return form;
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
            string name = (_Name.Text ?? string.Empty).Trim();
            if (name.Length == 0)
            {
                _Error.Text = Localizer.T("command.err.nameRequired");
                return;
            }

            if ((_Exe.Text ?? string.Empty).Trim().Length == 0)
            {
                _Error.Text = Localizer.T("command.err.commandRequired");
                return;
            }

            if (!int.TryParse((_Timeout.Text ?? string.Empty).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int timeout) || timeout <= 0)
            {
                _Error.Text = Localizer.T("command.err.timeoutInvalid");
                return;
            }

            _Command.Name = name;
            _Command.Description = (_Description.Text ?? string.Empty).Trim();
            _Command.Command = (_Exe.Text ?? string.Empty).Trim();
            _Command.Args = ParseLines(_Args.Text);
            _Command.TimeoutMs = timeout;
            Close(true);
        }

        private static List<string> ParseLines(string? text)
        {
            List<string> result = new List<string>();
            if (string.IsNullOrEmpty(text))
            {
                return result;
            }

            foreach (string line in text.Replace("\r\n", "\n").Split('\n'))
            {
                string trimmed = line.Trim();
                if (trimmed.Length > 0)
                {
                    result.Add(trimmed);
                }
            }

            return result;
        }
    }
}
