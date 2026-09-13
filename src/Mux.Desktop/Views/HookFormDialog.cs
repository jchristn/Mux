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
        private readonly CheckBox _Blocking = new CheckBox { Content = Localizer.T("hook.blocking") };
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

            Title = isNew ? Localizer.T("hook.add.title") : Localizer.T("hook.edit.title");
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
            form.Children.Add(Field(Localizer.T("col.name"), _Name, Localizer.T("hook.name.tip")));
            form.Children.Add(Field(Localizer.T("hook.event"), _Event, Localizer.T("hook.event.tip")));
            form.Children.Add(Field(Localizer.T("hook.command"), _Command, Localizer.T("hook.command.tip")));
            form.Children.Add(Field(Localizer.T("hook.args"), _Args, Localizer.T("hook.args.tip")));
            form.Children.Add(Field(Localizer.T("hook.timeout"), _Timeout, Localizer.T("hook.timeout.tip")));
            form.Children.Add(_Blocking.Tip(Localizer.T("hook.blocking.tip")));
            form.Children.Add(_Error);

            StackPanel buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
            Button cancel = new Button { Content = Localizer.T("act.cancel") };
            cancel.Tip(Localizer.T("hook.cancel.tip"));
            cancel.Click += (sender, args) => Close(false);
            buttons.Children.Add(cancel);
            Button ok = new Button { Content = Localizer.T("act.save"), Background = theme.AccentButton, Foreground = theme.AccentText };
            ok.Tip(Localizer.T("hook.save.tip"));
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
                _Error.Text = Localizer.T("hook.err.nameRequired");
                return;
            }

            if ((_Command.Text ?? string.Empty).Trim().Length == 0)
            {
                _Error.Text = Localizer.T("hook.err.commandRequired");
                return;
            }

            if (!int.TryParse((_Timeout.Text ?? string.Empty).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int timeout) || timeout <= 0)
            {
                _Error.Text = Localizer.T("hook.err.timeoutInvalid");
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
