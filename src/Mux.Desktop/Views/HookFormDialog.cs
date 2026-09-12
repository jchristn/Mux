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
    /// A form for creating or editing a lifecycle hook (parity with the TUI's hooks editor): name, event,
    /// command, args, blocking, and timeout. Pre-fills from the supplied <see cref="HookDefinition"/> and, on
    /// Save, writes the edited values back and returns true.
    /// </summary>
    public sealed class HookFormDialog : Window
    {
        private readonly HookDefinition _Hook;
        private readonly TextBox _Name = new TextBox();
        private readonly ComboBox _Event = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        private readonly TextBox _Command = new TextBox();
        private readonly TextBox _Args = new TextBox { AcceptsReturn = true, MinHeight = 60, TextWrapping = TextWrapping.Wrap };
        private readonly TextBox _Timeout = new TextBox();
        private readonly CheckBox _Blocking = new CheckBox { Content = "Blocking (a non-zero exit vetoes the event)" };
        private readonly TextBlock _Error = new TextBlock { Foreground = new SolidColorBrush(Color.Parse("#cf222e")), FontSize = 12, TextWrapping = TextWrapping.Wrap };

        /// <summary>
        /// Instantiate the hook form.
        /// </summary>
        /// <param name="hook">The hook to edit (pre-filled and written back on Save). Required.</param>
        /// <param name="isNew">True when creating a new hook (affects the title only).</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="hook"/> is null.</exception>
        public HookFormDialog(HookDefinition hook, bool isNew)
        {
            _Hook = hook ?? throw new ArgumentNullException(nameof(hook));

            Title = isNew ? "Add hook" : "Edit hook";
            Icon = IconResources.LoadWindowIcon();
            Width = 560;
            SizeToContent = SizeToContent.Height;
            CanResize = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = AppTheme.Current.Surface;

            _Event.ItemsSource = Enum.GetValues(typeof(HookEventEnum));
            _Event.SelectedItem = hook.Event;
            _Name.Text = hook.Name;
            _Command.Text = hook.Command;
            _Args.Text = string.Join(Environment.NewLine, hook.Args);
            _Timeout.Text = hook.TimeoutMs.ToString(CultureInfo.InvariantCulture);
            _Blocking.IsChecked = hook.Blocking;

            Content = BuildContent();
        }

        private Control BuildContent()
        {
            AppTheme theme = AppTheme.Current;
            StackPanel form = new StackPanel { Margin = new Thickness(24), Spacing = 10 };
            form.Children.Add(Field("Name", _Name, "A label for this hook."));
            form.Children.Add(Field("Event", _Event, "The lifecycle event that fires this hook."));
            form.Children.Add(Field("Command", _Command, "The executable to run when the event fires."));
            form.Children.Add(Field("Args (one per line)", _Args, "Arguments passed to the command, one per line."));
            form.Children.Add(Field("Timeout (ms)", _Timeout, "How long to wait for the hook before giving up."));
            form.Children.Add(_Blocking.Tip("If checked, a non-zero exit from a prompt-submit hook vetoes the submission."));
            form.Children.Add(_Error);

            StackPanel buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
            Button cancel = new Button { Content = "Cancel" };
            cancel.Tip("Discard changes and close.");
            cancel.Click += (sender, args) => Close(false);
            buttons.Children.Add(cancel);
            Button ok = new Button { Content = "Save", Background = theme.AccentButton, Foreground = theme.AccentText };
            ok.Tip("Save this hook.");
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

            if ((_Command.Text ?? string.Empty).Trim().Length == 0)
            {
                _Error.Text = "Command is required.";
                return;
            }

            if (!int.TryParse((_Timeout.Text ?? string.Empty).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int timeout) || timeout <= 0)
            {
                _Error.Text = "Timeout must be a positive whole number of milliseconds.";
                return;
            }

            _Hook.Name = name;
            if (_Event.SelectedItem is HookEventEnum ev)
            {
                _Hook.Event = ev;
            }

            _Hook.Command = (_Command.Text ?? string.Empty).Trim();
            _Hook.Args = ParseLines(_Args.Text);
            _Hook.TimeoutMs = timeout;
            _Hook.Blocking = _Blocking.IsChecked ?? false;
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
