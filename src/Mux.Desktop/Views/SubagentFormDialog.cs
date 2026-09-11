namespace Mux.Desktop.Views
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using Avalonia;
    using Avalonia.Controls;
    using Avalonia.Layout;
    using Avalonia.Media;
    using Mux.Core.Models;
    using Mux.Core.Settings;
    using Mux.Core.Subagents;

    /// <summary>
    /// A form for creating or editing a single subagent definition (name, description, system prompt, optional
    /// endpoint, allowed tools, and iteration cap). Pre-fills from the supplied
    /// <see cref="SubagentDefinition"/> and, on OK, writes the edited values back and returns true.
    /// </summary>
    public sealed class SubagentFormDialog : Window
    {
        private const string InheritLabel = "(inherit default endpoint)";

        private readonly SubagentDefinition _Definition;
        private readonly TextBox _Name = new TextBox();
        private readonly TextBox _Description = new TextBox();
        private readonly ComboBox _Endpoint = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        private readonly TextBox _SystemPrompt = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 120, FontFamily = new FontFamily("Cascadia Code, Consolas, monospace") };
        private readonly TextBox _AllowedTools = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 72 };
        private readonly TextBox _MaxIterations = new TextBox { Width = 120, HorizontalAlignment = HorizontalAlignment.Left };
        private readonly TextBlock _Error = new TextBlock { Foreground = new SolidColorBrush(Color.Parse("#cf222e")), FontSize = 12 };

        /// <summary>
        /// Instantiate the subagent form.
        /// </summary>
        /// <param name="definition">The subagent to edit (pre-filled and written back on OK). Required.</param>
        /// <param name="isNew">True when creating a new subagent (affects the title only).</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="definition"/> is null.</exception>
        public SubagentFormDialog(SubagentDefinition definition, bool isNew)
        {
            ArgumentNullException.ThrowIfNull(definition);
            _Definition = definition;

            Title = isNew ? "Add subagent" : "Edit subagent";
            Icon = IconResources.LoadWindowIcon();
            Width = 1020;
            Height = 780;
            MinWidth = 640;
            MinHeight = 560;
            CanResize = true;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = AppTheme.Current.Surface;

            List<string> endpointOptions = new List<string> { InheritLabel };
            foreach (EndpointConfig endpoint in SettingsLoader.LoadEndpoints())
            {
                endpointOptions.Add(endpoint.Name);
            }

            _Endpoint.ItemsSource = endpointOptions;
            _Endpoint.SelectedItem = string.IsNullOrEmpty(definition.EndpointName) ? InheritLabel : definition.EndpointName;
            if (_Endpoint.SelectedItem == null)
            {
                _Endpoint.SelectedIndex = 0;
            }

            _Name.Text = definition.Name;
            _Description.Text = definition.Description;
            _SystemPrompt.Text = definition.SystemPrompt;
            _AllowedTools.Text = string.Join(Environment.NewLine, definition.AllowedTools);
            _MaxIterations.Text = definition.MaxIterations?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;

            Content = BuildContent();
        }

        private Control BuildContent()
        {
            AppTheme theme = AppTheme.Current;
            DockPanel root = new DockPanel { Margin = new Thickness(20) };

            StackPanel buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Margin = new Thickness(0, 12, 0, 0) };
            Button cancel = new Button { Content = "Cancel" };
            cancel.Tip("Discard changes and close.");
            cancel.Click += (sender, args) => Close(false);
            buttons.Children.Add(cancel);
            Button ok = new Button { Content = "Save", Background = theme.AccentButton, Foreground = theme.AccentText };
            ok.Tip("Save this subagent definition.");
            ok.Click += (sender, args) => Ok();
            buttons.Children.Add(ok);
            DockPanel.SetDock(buttons, Dock.Bottom);
            root.Children.Add(buttons);

            DockPanel.SetDock(_Error, Dock.Bottom);
            root.Children.Add(_Error);

            StackPanel form = new StackPanel { Spacing = 10, Margin = new Thickness(0, 0, 14, 0) };
            form.Children.Add(Field("Name", _Name, "The subagent's name, referenced when the main agent delegates to it."));
            form.Children.Add(Field("Description", _Description, "What this subagent does; helps the model decide when to hand work to it."));
            form.Children.Add(Field("Endpoint", _Endpoint, "The endpoint this subagent runs on, or inherit the main conversation's default."));
            form.Children.Add(Field("System prompt", _SystemPrompt, "The persona and instructions this subagent runs with."));
            form.Children.Add(Field("Allowed tools (one per line; blank = all)", _AllowedTools, "Restrict which tools this subagent may use, one tool name per line. Blank allows all."));
            form.Children.Add(Field("Max iterations (blank = default)", _MaxIterations, "Cap on this subagent's agent-loop turns. Blank uses the global setting."));

            root.Children.Add(new ScrollViewer { Content = form });
            return root;
        }

        private Control Field(string label, Control control, string tip)
        {
            StackPanel panel = new StackPanel { Spacing = 4 };
            panel.Children.Add(new TextBlock { Text = label, FontWeight = FontWeight.SemiBold, Foreground = AppTheme.Current.Text });
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

            string maxText = (_MaxIterations.Text ?? string.Empty).Trim();
            int? maxIterations = null;
            if (maxText.Length > 0)
            {
                if (!int.TryParse(maxText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) || parsed <= 0)
                {
                    _Error.Text = "Max iterations must be a positive whole number, or blank.";
                    return;
                }

                maxIterations = parsed;
            }

            _Definition.Name = name;
            _Definition.Description = (_Description.Text ?? string.Empty).Trim();
            string endpoint = _Endpoint.SelectedItem as string ?? InheritLabel;
            _Definition.EndpointName = endpoint == InheritLabel ? null : endpoint;
            _Definition.SystemPrompt = _SystemPrompt.Text ?? string.Empty;
            _Definition.AllowedTools = ParseLines(_AllowedTools.Text);
            _Definition.MaxIterations = maxIterations;
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
