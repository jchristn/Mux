namespace Mux.Desktop.Views
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Text;
    using Avalonia;
    using Avalonia.Controls;
    using Avalonia.Layout;
    using Avalonia.Media;
    using Mux.Core.Enums;
    using Mux.Core.Models;
    using Mux.Desktop.I18n;

    /// <summary>
    /// A form for creating or editing a single endpoint. The fields shown adapt to the selected adapter type:
    /// the OpenAI-family adapters (ollama, openai, openai-compatible, vllm) surface an API-key placement
    /// selector (bearer header / custom header / query-string parameter) and a parameter-name field; the cloud
    /// adapters surface region, project, and api-version instead; and every adapter except the cloud-IAM ones
    /// (vertex, bedrock) takes an API key and free-form custom headers. Common fields (name, model, sampling,
    /// timeout, reasoning effort, behavior flags) are always shown. Pre-fills from the supplied
    /// <see cref="EndpointConfig"/> and, on Save, writes the edited values back and returns true via
    /// <c>ShowDialog&lt;bool&gt;</c>. Overrides the form does not surface (OpenAI reasoning value, Ollama think)
    /// are preserved on the instance.
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
        private readonly ComboBox _AuthPlacement = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        private readonly TextBox _AuthParameterName = new TextBox();
        private readonly TextBox _Region = new TextBox();
        private readonly TextBox _Project = new TextBox();
        private readonly TextBox _ApiVersion = new TextBox();
        private readonly TextBox _Headers = new TextBox { AcceptsReturn = true, MinHeight = 72, TextWrapping = TextWrapping.NoWrap };
        private readonly TextBox _Temperature = new TextBox();
        private readonly TextBox _MaxTokens = new TextBox();
        private readonly TextBox _ContextWindow = new TextBox();
        private readonly TextBox _Timeout = new TextBox();
        private readonly TextBox _MaxAgentIterations = new TextBox();
        private readonly ComboBox _ReasoningEffort = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        private readonly TextBox _GeminiThinkingBudget = new TextBox();
        private readonly CheckBox _IsDefault = new CheckBox { Content = Localizer.T("endpoint.form.useDefault") };
        private readonly CheckBox _AutoApprove = new CheckBox { Content = Localizer.T("endpoint.form.autoApprove") };
        private readonly CheckBox _ShowThinking = new CheckBox { Content = Localizer.T("endpoint.form.showThinking") };
        private readonly TextBlock _Error = new TextBlock { Foreground = new SolidColorBrush(Color.Parse("#cf222e")), FontSize = 12, TextWrapping = TextWrapping.Wrap };

        // Field wrappers whose visibility depends on the selected adapter (and, for the parameter name, the
        // selected placement). Held so UpdateAdaptiveVisibility can toggle IsVisible without rebuilding.
        private Control _ApiKeyField = new Control();
        private Control _PlacementField = new Control();
        private Control _ParameterNameField = new Control();
        private Control _RegionField = new Control();
        private Control _ProjectField = new Control();
        private Control _ApiVersionField = new Control();
        private Control _HeadersField = new Control();

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

            Title = isNew ? Localizer.T("endpoint.form.addTitle") : Localizer.T("endpoint.form.editTitle");
            Icon = IconResources.LoadWindowIcon();
            Width = 680;
            Height = 720;
            MinWidth = 520;
            MinHeight = 480;
            CanResize = true;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = AppTheme.Current.Surface;

            _Adapter.ItemsSource = Enum.GetValues(typeof(AdapterTypeEnum));
            _Adapter.SelectedItem = config.AdapterType;
            _AuthPlacement.ItemsSource = new[] { "bearer", "header", "query" };
            _AuthPlacement.SelectedItem = AuthPlacementEnumConverter.ToWire(config.AuthPlacement);
            _Name.Text = config.Name;
            _BaseUrl.Text = config.BaseUrl;
            _Model.Text = config.Model;
            _ApiKey.Text = config.ApiKey ?? string.Empty;
            _AuthParameterName.Text = config.AuthParameterName ?? string.Empty;
            _Region.Text = config.Region ?? string.Empty;
            _Project.Text = config.Project ?? string.Empty;
            _ApiVersion.Text = config.ApiVersion ?? string.Empty;
            _Headers.Text = HeadersToText(config.Headers);
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

            _Adapter.SelectionChanged += (sender, args) => UpdateAdaptiveVisibility();
            _AuthPlacement.SelectionChanged += (sender, args) => UpdateAdaptiveVisibility();
            UpdateAdaptiveVisibility();
        }

        private Control BuildContent()
        {
            AppTheme theme = AppTheme.Current;
            DockPanel root = new DockPanel { Margin = new Thickness(24) };

            StackPanel buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Margin = new Thickness(0, 14, 0, 0) };
            Button cancel = new Button { Content = Localizer.T("act.cancel") };
            cancel.Tip(Localizer.T("endpoint.form.cancel.tip"));
            cancel.Click += (sender, args) => Close(false);
            buttons.Children.Add(cancel);
            Button ok = new Button { Content = Localizer.T("act.save"), Background = theme.AccentButton, Foreground = theme.AccentText };
            ok.Tip(Localizer.T("endpoint.form.save.tip"));
            ok.Click += (sender, args) => Ok();
            buttons.Children.Add(ok);
            DockPanel.SetDock(buttons, Dock.Bottom);
            root.Children.Add(buttons);

            DockPanel.SetDock(_Error, Dock.Bottom);
            root.Children.Add(_Error);

            // A single scrolling column: field visibility can be toggled per row without leaving grid gaps,
            // which a fixed two-column grid cannot do cleanly as the adapter changes.
            StackPanel body = new StackPanel { Spacing = 12 };

            body.Children.Add(Section(Localizer.T("endpoint.form.section.connection")));
            body.Children.Add(Field(Localizer.T("col.name"), _Name, Localizer.T("endpoint.form.name.tip")));
            body.Children.Add(Field(Localizer.T("endpoint.col.adapter"), _Adapter, Localizer.T("endpoint.form.adapter.tip")));
            body.Children.Add(Field(Localizer.T("endpoint.col.baseUrl"), _BaseUrl, Localizer.T("endpoint.form.baseUrl.tip")));
            body.Children.Add(Field(Localizer.T("col.model"), _Model, Localizer.T("endpoint.form.model.tip")));

            body.Children.Add(Section(Localizer.T("endpoint.form.section.auth")));
            _PlacementField = Field(Localizer.T("endpoint.form.authPlacement"), _AuthPlacement, Localizer.T("endpoint.form.authPlacement.tip"));
            _ApiKeyField = Field(Localizer.T("endpoint.form.apiKey"), _ApiKey, Localizer.T("endpoint.form.apiKey.tip"));
            _ParameterNameField = Field(Localizer.T("endpoint.form.authParameterName"), _AuthParameterName, Localizer.T("endpoint.form.authParameterName.tip"));
            _RegionField = Field(Localizer.T("endpoint.form.region"), _Region, Localizer.T("endpoint.form.region.tip"));
            _ProjectField = Field(Localizer.T("endpoint.form.project"), _Project, Localizer.T("endpoint.form.project.tip"));
            _ApiVersionField = Field(Localizer.T("endpoint.form.apiVersion"), _ApiVersion, Localizer.T("endpoint.form.apiVersion.tip"));
            body.Children.Add(_PlacementField);
            body.Children.Add(_ApiKeyField);
            body.Children.Add(_ParameterNameField);
            body.Children.Add(_RegionField);
            body.Children.Add(_ProjectField);
            body.Children.Add(_ApiVersionField);

            _HeadersField = Field(Localizer.T("endpoint.form.headers"), _Headers, Localizer.T("endpoint.form.headers.tip"));
            body.Children.Add(Section(Localizer.T("endpoint.form.section.headers")));
            body.Children.Add(_HeadersField);

            body.Children.Add(Section(Localizer.T("endpoint.form.section.modelSampling")));
            body.Children.Add(TwoColumn(
                Field(Localizer.T("endpoint.form.maxTokens"), _MaxTokens, Localizer.T("endpoint.form.maxTokens.tip")),
                Field(Localizer.T("endpoint.form.temperature"), _Temperature, Localizer.T("endpoint.form.temperature.tip"))));
            body.Children.Add(TwoColumn(
                Field(Localizer.T("endpoint.form.contextWindow"), _ContextWindow, Localizer.T("endpoint.form.contextWindow.tip")),
                Field(Localizer.T("endpoint.form.timeout"), _Timeout, Localizer.T("endpoint.form.timeout.tip"))));
            body.Children.Add(TwoColumn(
                Field(Localizer.T("endpoint.form.reasoning"), _ReasoningEffort, Localizer.T("endpoint.form.reasoning.tip")),
                Field(Localizer.T("endpoint.form.geminiBudget"), _GeminiThinkingBudget, Localizer.T("endpoint.form.geminiBudget.tip"))));
            body.Children.Add(Field(Localizer.T("endpoint.form.maxIterations"), _MaxAgentIterations, Localizer.T("endpoint.form.maxIterations.tip")));

            body.Children.Add(Section(Localizer.T("endpoint.form.section.behavior")));
            StackPanel checks = new StackPanel { Spacing = 8 };
            checks.Children.Add(_IsDefault.Tip(Localizer.T("endpoint.form.useDefault.tip")));
            checks.Children.Add(_AutoApprove.Tip(Localizer.T("endpoint.form.autoApprove.tip")));
            checks.Children.Add(_ShowThinking.Tip(Localizer.T("endpoint.form.showThinking.tip")));
            body.Children.Add(checks);

            ScrollViewer scroller = new ScrollViewer
            {
                Content = body,
                HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
            };
            root.Children.Add(scroller);
            return root;
        }

        // Shows only the fields that apply to the selected adapter and placement.
        private void UpdateAdaptiveVisibility()
        {
            AdapterTypeEnum adapter = _Adapter.SelectedItem is AdapterTypeEnum selected ? selected : AdapterTypeEnum.Ollama;
            bool openAiFamily = adapter == AdapterTypeEnum.Ollama
                || adapter == AdapterTypeEnum.OpenAi
                || adapter == AdapterTypeEnum.OpenAiCompatible
                || adapter == AdapterTypeEnum.Vllm;
            bool usesKey = openAiFamily
                || adapter == AdapterTypeEnum.Anthropic
                || adapter == AdapterTypeEnum.Gemini
                || adapter == AdapterTypeEnum.AzureOpenAi;
            string placement = _AuthPlacement.SelectedItem as string ?? "bearer";

            _PlacementField.IsVisible = openAiFamily;
            _ApiKeyField.IsVisible = usesKey;
            _ParameterNameField.IsVisible = openAiFamily && (placement == "header" || placement == "query");
            _RegionField.IsVisible = adapter == AdapterTypeEnum.Vertex || adapter == AdapterTypeEnum.Bedrock;
            _ProjectField.IsVisible = adapter == AdapterTypeEnum.Vertex;
            _ApiVersionField.IsVisible = adapter == AdapterTypeEnum.AzureOpenAi;
            _HeadersField.IsVisible = usesKey;
        }

        private Control Section(string text)
        {
            return new TextBlock
            {
                Text = text,
                Foreground = AppTheme.Current.Accent,
                FontWeight = FontWeight.Bold,
                Margin = new Thickness(0, 8, 0, 0)
            };
        }

        private static Control TwoColumn(Control left, Control right)
        {
            Grid grid = new Grid { ColumnSpacing = 20 };
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            Grid.SetColumn(left, 0);
            Grid.SetColumn(right, 1);
            grid.Children.Add(left);
            grid.Children.Add(right);
            return grid;
        }

        private Control Field(string label, Control control, string tip)
        {
            StackPanel panel = new StackPanel { Spacing = 4 };
            panel.Children.Add(new TextBlock { Text = label, Foreground = AppTheme.Current.Text, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.NoWrap });
            control.HorizontalAlignment = HorizontalAlignment.Stretch;
            panel.Children.Add(control);
            return panel.Tip(tip);
        }

        // Renders the endpoint's headers as one "Name: Value" per line for editing.
        private static string HeadersToText(Dictionary<string, string> headers)
        {
            if (headers == null || headers.Count == 0)
            {
                return string.Empty;
            }

            StringBuilder builder = new StringBuilder();
            foreach (KeyValuePair<string, string> header in headers)
            {
                if (string.IsNullOrEmpty(header.Key))
                {
                    continue;
                }

                if (builder.Length > 0)
                {
                    builder.Append('\n');
                }

                builder.Append(header.Key).Append(": ").Append(header.Value);
            }

            return builder.ToString();
        }

        // Parses the headers text back to a dictionary: each non-empty line splits on the first colon into a
        // name and value (both trimmed); a blank name is skipped.
        private static Dictionary<string, string> ParseHeaders(string? text)
        {
            Dictionary<string, string> headers = new Dictionary<string, string>();
            if (string.IsNullOrWhiteSpace(text))
            {
                return headers;
            }

            string[] lines = text.Replace("\r\n", "\n").Split('\n');
            foreach (string line in lines)
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                int colon = line.IndexOf(':');
                string name = colon < 0 ? line.Trim() : line.Substring(0, colon).Trim();
                string value = colon < 0 ? string.Empty : line.Substring(colon + 1).Trim();
                if (name.Length > 0)
                {
                    headers[name] = value;
                }
            }

            return headers;
        }

        private void Ok()
        {
            string name = (_Name.Text ?? string.Empty).Trim();
            if (name.Length == 0)
            {
                _Error.Text = Localizer.T("endpoint.form.err.nameRequired");
                return;
            }

            AdapterTypeEnum adapter = _Adapter.SelectedItem is AdapterTypeEnum selected ? selected : _Config.AdapterType;

            // Hosted/cloud adapters resolve a default endpoint (anthropic/gemini) or use SDK credentials
            // (vertex/bedrock), so a base URL is optional for them; every other adapter needs one.
            bool baseUrlOptional = adapter == AdapterTypeEnum.Anthropic
                || adapter == AdapterTypeEnum.Gemini
                || adapter == AdapterTypeEnum.Vertex
                || adapter == AdapterTypeEnum.Bedrock;
            if (!baseUrlOptional && (_BaseUrl.Text ?? string.Empty).Trim().Length == 0)
            {
                _Error.Text = Localizer.T("endpoint.form.err.baseUrlRequired");
                return;
            }

            if (!double.TryParse(_Temperature.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double temperature) || temperature < 0.0 || temperature > 2.0)
            {
                _Error.Text = Localizer.T("endpoint.form.err.temperature");
                return;
            }

            if (!int.TryParse(_MaxTokens.Text, out int maxTokens) || maxTokens < 1024)
            {
                _Error.Text = Localizer.T("endpoint.form.err.maxTokens");
                return;
            }

            if (!int.TryParse(_ContextWindow.Text, out int contextWindow) || contextWindow < 1024)
            {
                _Error.Text = Localizer.T("endpoint.form.err.contextWindow");
                return;
            }

            if (!int.TryParse(_Timeout.Text, out int timeout) || timeout < 10000)
            {
                _Error.Text = Localizer.T("endpoint.form.err.timeout");
                return;
            }

            int? maxIterations = null;
            string iterText = (_MaxAgentIterations.Text ?? string.Empty).Trim();
            if (iterText.Length > 0)
            {
                if (!int.TryParse(iterText, out int iters) || iters < 1 || iters > 100)
                {
                    _Error.Text = Localizer.T("endpoint.form.err.maxIterations");
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
                    _Error.Text = Localizer.T("endpoint.form.err.geminiBudget");
                    return;
                }

                geminiBudget = budget;
            }

            _Config.Name = name;
            _Config.AdapterType = adapter;
            _Config.BaseUrl = (_BaseUrl.Text ?? string.Empty).Trim();
            _Config.Model = (_Model.Text ?? string.Empty).Trim();
            _Config.ApiKey = string.IsNullOrWhiteSpace(_ApiKey.Text) ? null : _ApiKey.Text;
            _Config.AuthPlacement = AuthPlacementEnumConverter.Parse(_AuthPlacement.SelectedItem as string);
            string parameterName = (_AuthParameterName.Text ?? string.Empty).Trim();
            _Config.AuthParameterName = parameterName.Length == 0 ? null : parameterName;
            _Config.Region = string.IsNullOrWhiteSpace(_Region.Text) ? null : _Region.Text!.Trim();
            _Config.Project = string.IsNullOrWhiteSpace(_Project.Text) ? null : _Project.Text!.Trim();
            _Config.ApiVersion = string.IsNullOrWhiteSpace(_ApiVersion.Text) ? null : _ApiVersion.Text!.Trim();
            _Config.Headers = ParseHeaders(_Headers.Text);
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
