namespace Mux.Desktop.Views
{
    using System;
    using System.Globalization;
    using Avalonia;
    using Avalonia.Controls;
    using Avalonia.Layout;
    using Avalonia.Media;
    using Mux.Core.Enums;
    using Mux.Core.Models;

    /// <summary>
    /// A form for creating or editing a single endpoint, at field parity with the TUI's endpoint form:
    /// name, adapter, base URL, model, API key, default, max output tokens, temperature, context window,
    /// timeout, auto-approve tools, max agent iterations, reasoning effort, Gemini thinking budget, and show
    /// thinking. Pre-fills from the supplied <see cref="EndpointConfig"/> and, on Save, writes the edited
    /// values back and returns true via <c>ShowDialog&lt;bool&gt;</c>. Overrides the form does not surface
    /// (OpenAI reasoning value, Ollama think, custom headers) are preserved on the instance.
    /// </summary>
    public sealed class EndpointFormDialog : Window
    {
        private const string EffortOff = "(off)";

        private readonly EndpointConfig _Config;
        private readonly TextBox _Name = new TextBox();
        private readonly ComboBox _Adapter = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        private readonly TextBox _BaseUrl = new TextBox();
        private readonly TextBox _Model = new TextBox();
        private readonly TextBox _ApiKey = new TextBox { PasswordChar = '●' };
        private readonly TextBox _Temperature = new TextBox();
        private readonly TextBox _MaxTokens = new TextBox();
        private readonly TextBox _ContextWindow = new TextBox();
        private readonly TextBox _Timeout = new TextBox();
        private readonly TextBox _MaxAgentIterations = new TextBox();
        private readonly ComboBox _ReasoningEffort = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        private readonly TextBox _GeminiThinkingBudget = new TextBox();
        private readonly CheckBox _IsDefault = new CheckBox { Content = "Use as the default endpoint" };
        private readonly CheckBox _AutoApprove = new CheckBox { Content = "Auto-approve tool calls" };
        private readonly CheckBox _ShowThinking = new CheckBox { Content = "Show thinking (reasoning)" };
        private readonly TextBlock _Error = new TextBlock { Foreground = new SolidColorBrush(Color.Parse("#cf222e")), FontSize = 12, TextWrapping = TextWrapping.Wrap };

        /// <summary>
        /// Instantiate the endpoint form.
        /// </summary>
        /// <param name="config">The endpoint to edit (pre-filled and written back on Save). Required.</param>
        /// <param name="isNew">True when creating a new endpoint (affects the title only).</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="config"/> is null.</exception>
        public EndpointFormDialog(EndpointConfig config, bool isNew)
        {
            ArgumentNullException.ThrowIfNull(config);
            _Config = config;

            Title = isNew ? "Add endpoint" : "Edit endpoint";
            Icon = IconResources.LoadWindowIcon();
            Width = 930;
            Height = 640;
            MinWidth = 720;
            MinHeight = 520;
            CanResize = true;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = AppTheme.Current.Surface;

            _Adapter.ItemsSource = Enum.GetValues(typeof(AdapterTypeEnum));
            _Adapter.SelectedItem = config.AdapterType;
            _Name.Text = config.Name;
            _BaseUrl.Text = config.BaseUrl;
            _Model.Text = config.Model;
            _ApiKey.Text = config.ApiKey ?? string.Empty;
            _Temperature.Text = config.Temperature.ToString(CultureInfo.InvariantCulture);
            _MaxTokens.Text = config.MaxTokens.ToString(CultureInfo.InvariantCulture);
            _ContextWindow.Text = config.ContextWindow.ToString(CultureInfo.InvariantCulture);
            _Timeout.Text = config.TimeoutMs.ToString(CultureInfo.InvariantCulture);
            _MaxAgentIterations.Text = config.MaxAgentIterations?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
            _GeminiThinkingBudget.Text = config.ReasoningEffort?.GeminiThinkingBudget?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
            _IsDefault.IsChecked = config.IsDefault;
            _AutoApprove.IsChecked = config.AutoApproveTools;
            _ShowThinking.IsChecked = config.ShowThinking;

            _ReasoningEffort.ItemsSource = new[] { EffortOff, "minimal", "low", "medium", "high" };
            _ReasoningEffort.SelectedItem = config.ReasoningEffort?.Level.HasValue == true
                ? config.ReasoningEffort.Level.Value.ToString().ToLowerInvariant()
                : EffortOff;

            Content = BuildContent();
        }

        private Control BuildContent()
        {
            AppTheme theme = AppTheme.Current;
            DockPanel root = new DockPanel { Margin = new Thickness(24) };

            StackPanel buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Margin = new Thickness(0, 14, 0, 0) };
            Button cancel = new Button { Content = "Cancel" };
            cancel.Click += (sender, args) => Close(false);
            buttons.Children.Add(cancel);
            Button ok = new Button { Content = "Save", Background = theme.AccentButton, Foreground = theme.AccentText };
            ok.Click += (sender, args) => Ok();
            buttons.Children.Add(ok);
            DockPanel.SetDock(buttons, Dock.Bottom);
            root.Children.Add(buttons);

            DockPanel.SetDock(_Error, Dock.Bottom);
            root.Children.Add(_Error);

            // A two-column grid so every field fits without scrolling.
            Grid grid = new Grid { ColumnSpacing = 20, RowSpacing = 12 };
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            for (int r = 0; r < 7; r++)
            {
                grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            }

            Place(grid, Field("Name", _Name), 0, 0);
            Place(grid, Field("Adapter", _Adapter), 0, 1);
            Place(grid, Field("Base URL", _BaseUrl), 1, 0);
            Place(grid, Field("Model", _Model), 1, 1);
            Place(grid, Field("API key (optional)", _ApiKey), 2, 0);
            Place(grid, Field("Reasoning effort", _ReasoningEffort), 2, 1);
            Place(grid, Field("Max output tokens", _MaxTokens), 3, 0);
            Place(grid, Field("Temperature (0.0–2.0)", _Temperature), 3, 1);
            Place(grid, Field("Context window", _ContextWindow), 4, 0);
            Place(grid, Field("Timeout (ms)", _Timeout), 4, 1);
            Place(grid, Field("Max agent iterations (blank = global)", _MaxAgentIterations), 5, 0);
            Place(grid, Field("Gemini thinking budget (blank = default)", _GeminiThinkingBudget), 5, 1);

            StackPanel checks = new StackPanel { Spacing = 8, Margin = new Thickness(0, 4, 0, 0) };
            checks.Children.Add(_IsDefault);
            checks.Children.Add(_AutoApprove);
            checks.Children.Add(_ShowThinking);
            Grid.SetRow(checks, 6);
            Grid.SetColumn(checks, 0);
            Grid.SetColumnSpan(checks, 2);
            grid.Children.Add(checks);

            root.Children.Add(grid);
            return root;
        }

        private static void Place(Grid grid, Control field, int row, int column)
        {
            Grid.SetRow(field, row);
            Grid.SetColumn(field, column);
            grid.Children.Add(field);
        }

        private Control Field(string label, Control control)
        {
            StackPanel panel = new StackPanel { Spacing = 4 };
            panel.Children.Add(new TextBlock { Text = label, Foreground = AppTheme.Current.Text, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.NoWrap });
            control.HorizontalAlignment = HorizontalAlignment.Stretch;
            panel.Children.Add(control);
            return panel;
        }

        private void Ok()
        {
            string name = (_Name.Text ?? string.Empty).Trim();
            if (name.Length == 0)
            {
                _Error.Text = "Name is required.";
                return;
            }

            if ((_BaseUrl.Text ?? string.Empty).Trim().Length == 0)
            {
                _Error.Text = "Base URL is required.";
                return;
            }

            if (!double.TryParse(_Temperature.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double temperature) || temperature < 0.0 || temperature > 2.0)
            {
                _Error.Text = "Temperature must be between 0.0 and 2.0.";
                return;
            }

            if (!int.TryParse(_MaxTokens.Text, out int maxTokens) || maxTokens < 1024)
            {
                _Error.Text = "Max output tokens must be a whole number of at least 1024.";
                return;
            }

            if (!int.TryParse(_ContextWindow.Text, out int contextWindow) || contextWindow < 1024)
            {
                _Error.Text = "Context window must be a whole number of at least 1024.";
                return;
            }

            if (!int.TryParse(_Timeout.Text, out int timeout) || timeout < 10000)
            {
                _Error.Text = "Timeout must be a whole number of at least 10000 ms.";
                return;
            }

            int? maxIterations = null;
            string iterText = (_MaxAgentIterations.Text ?? string.Empty).Trim();
            if (iterText.Length > 0)
            {
                if (!int.TryParse(iterText, out int iters) || iters < 1 || iters > 100)
                {
                    _Error.Text = "Max agent iterations must be 1–100, or blank.";
                    return;
                }

                maxIterations = iters;
            }

            int? geminiBudget = null;
            string budgetText = (_GeminiThinkingBudget.Text ?? string.Empty).Trim();
            if (budgetText.Length > 0)
            {
                if (!int.TryParse(budgetText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int budget) || budget < -1 || budget > 32768)
                {
                    _Error.Text = "Gemini thinking budget must be -1–32768, or blank.";
                    return;
                }

                geminiBudget = budget;
            }

            _Config.Name = name;
            if (_Adapter.SelectedItem is AdapterTypeEnum adapter)
            {
                _Config.AdapterType = adapter;
            }

            _Config.BaseUrl = (_BaseUrl.Text ?? string.Empty).Trim();
            _Config.Model = (_Model.Text ?? string.Empty).Trim();
            _Config.ApiKey = string.IsNullOrWhiteSpace(_ApiKey.Text) ? null : _ApiKey.Text;
            _Config.Temperature = temperature;
            _Config.MaxTokens = maxTokens;
            _Config.ContextWindow = contextWindow;
            _Config.TimeoutMs = timeout;
            _Config.MaxAgentIterations = maxIterations;
            _Config.IsDefault = _IsDefault.IsChecked ?? false;
            _Config.AutoApproveTools = _AutoApprove.IsChecked ?? false;
            _Config.ShowThinking = _ShowThinking.IsChecked ?? false;
            _Config.ReasoningEffort = BuildEffort(geminiBudget);
            Close(true);
        }

        private ReasoningEffortConfig? BuildEffort(int? geminiBudget)
        {
            string selected = _ReasoningEffort.SelectedItem as string ?? EffortOff;
            if (selected == EffortOff)
            {
                return null;
            }

            // Preserve overrides the form does not surface (OpenAI value, Ollama think) on an edit.
            ReasoningEffortConfig effort = _Config.ReasoningEffort?.Clone() ?? new ReasoningEffortConfig();
            if (Enum.TryParse(selected, ignoreCase: true, out ReasoningLevelEnum level))
            {
                effort.Level = level;
            }

            effort.GeminiThinkingBudget = geminiBudget;
            return effort;
        }
    }
}
