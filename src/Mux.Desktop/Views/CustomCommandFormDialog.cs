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

            Title = isNew ? "Add command" : "Edit command";
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
            form.Children.Add(Field("Name (typed as /name)", _Name, "The command's name; invoke it as /name."));
            form.Children.Add(Field("Description", _Description, "A short description shown in the command menu."));
            form.Children.Add(Field("Command", _Exe, "The executable to run."));
            form.Children.Add(Field("Args (one per line)", _Args, "Arguments passed to the command, one per line."));
            form.Children.Add(Field("Timeout (ms)", _Timeout, "How long to wait for the command before giving up."));
            form.Children.Add(_Error);

            StackPanel buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
            Button cancel = new Button { Content = "Cancel" };
            cancel.Tip("Discard changes and close.");
            cancel.Click += (sender, args) => Close(false);
            buttons.Children.Add(cancel);
            Button ok = new Button { Content = "Save", Background = theme.AccentButton, Foreground = theme.AccentText };
            ok.Tip("Save this command.");
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
                _Error.Text = "Name is required.";
                return;
            }

            if ((_Exe.Text ?? string.Empty).Trim().Length == 0)
            {
                _Error.Text = "Command is required.";
                return;
            }

            if (!int.TryParse((_Timeout.Text ?? string.Empty).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int timeout) || timeout <= 0)
            {
                _Error.Text = "Timeout must be a positive whole number of milliseconds.";
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
