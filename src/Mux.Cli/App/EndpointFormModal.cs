namespace Mux.Cli.App
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using Mux.Core.Enums;
    using Mux.Core.Models;
    using TUIKit;
    using TUIKit.Input;
    using TUIKit.Modals;
    using TUIKit.Widgets;

    /// <summary>
    /// A single modal form for creating or editing an endpoint, covering every adapter mux supports
    /// (Ollama, Ollama Cloud, OpenAI, OpenAI-compatible, vLLM, Anthropic, Gemini, Azure OpenAI, Vertex AI,
    /// AWS Bedrock, and xAI). It hosts a <see cref="Form"/> with fields for the name, adapter type, base URL,
    /// model, the credentials the adapter uses (an API key and, for the Chat Completions adapters and Ollama,
    /// its placement and parameter name; a region for Vertex and Bedrock and a project for Vertex), and every
    /// tunable endpoint parameter, navigated with Tab. Fields that do not apply to the selected adapter are
    /// hidden, the same rules the desktop app, web dashboard, and VS Code forms use. Enter validates and
    /// returns a populated <see cref="EndpointConfig"/> (via <see cref="Modal.Completion"/>), and Escape cancels
    /// (returns null). Fields that are not surfaced here — the nested <see cref="EndpointConfig.Quirks"/>,
    /// custom headers, and a stored Azure <see cref="EndpointConfig.ApiVersion"/> — are carried over unchanged.
    /// </summary>
    public sealed class EndpointFormModal : Modal
    {
        #region Private-Members

        private const int PadX = 3;
        private const int PadY = 1;

        // Wide enough to hold the footer hint and the longest field label ("Gemini thinking budget…")
        // without truncation on a normal-width terminal; it still shrinks to fit narrower screens in Render.
        private const int ContentWidth = 60;

        // Default roots for the local native/OpenAI-compatible APIs; Ollama Cloud uses its fixed public root.
        private const string OllamaDefaultBaseUrl = "http://localhost:11434";
        private const string OpenAiDefaultBaseUrl = "http://localhost:11434/v1";

        // Every adapter, in the order the picker shows them: local runners first, then hosted APIs and clouds.
        private static readonly string[] _Adapters =
        {
            "openai-compatible", "ollama", "vllm", "ollama-cloud", "openai", "anthropic", "gemini",
            "azure-openai", "vertex", "bedrock", "xai"
        };
        private static readonly string[] _Placements = { "bearer", "header", "query" };


        private readonly string _Title;
        private readonly EndpointConfig? _Existing;
        private readonly Form _Form;
        private readonly ScrollingFormHost _Host;
        private readonly TextField _Name;
        private readonly RadioGroup _Adapter;
        private readonly TextField _BaseUrl;
        private readonly TextField _Model;
        private readonly TextField _ApiKey;
        private readonly TextField _AuthPlacement;
        private readonly TextField _AuthParameterName;
        private readonly TextField _Region;
        private readonly TextField _Project;
        private readonly Checkbox _Default;
        private readonly TextField _MaxTokens;
        private readonly TextField _Temperature;
        private readonly TextField _ContextWindow;
        private readonly TextField _Timeout;
        private readonly Checkbox _AutoApprove;
        private readonly TextField _ToolAutoApproveSeconds;
        private readonly TextField _MaxAgentIterations;
        private readonly TextField _ReasoningEffort;
        private readonly TextField _GeminiThinkingBudget;
        private readonly Checkbox _ShowThinking;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="EndpointFormModal"/> class.
        /// </summary>
        /// <param name="title">The box title (for example "Add endpoint").</param>
        /// <param name="existing">An endpoint to pre-fill for editing, or null to start empty.</param>
        public EndpointFormModal(string title, EndpointConfig? existing = null)
        {
            _Title = title ?? "Endpoint";
            _Existing = existing;

            // A fresh EndpointConfig supplies the built-in defaults (max tokens, temperature, context
            // window, timeout) for the Add case, so the numeric fields are never blank.
            EndpointConfig source = existing ?? new EndpointConfig();

            _Name = new TextField();
            _Adapter = new RadioGroup(_Adapters);
            _BaseUrl = new TextField();
            _Model = new TextField();
            _ApiKey = new TextField();
            _AuthPlacement = new TextField();
            _AuthParameterName = new TextField();
            _Region = new TextField { Value = source.Region ?? string.Empty };
            _Project = new TextField { Value = source.Project ?? string.Empty };
            _Default = new Checkbox("Use as default endpoint", source.IsDefault);
            _MaxTokens = new TextField();
            _Temperature = new TextField();
            _ContextWindow = new TextField();
            _Timeout = new TextField();
            _ToolAutoApproveSeconds = new TextField { Value = source.ToolAutoApproveSeconds?.ToString(CultureInfo.InvariantCulture) ?? string.Empty };
            _AutoApprove = new Checkbox("Auto-approve tool calls", source.AutoApproveTools);
            _MaxAgentIterations = new TextField();
            _ReasoningEffort = new TextField();
            _GeminiThinkingBudget = new TextField();
            _ShowThinking = new Checkbox("Show thinking (reasoning)", source.ShowThinking);

            if (existing != null)
            {
                _Name.Value = existing.Name;
                _Adapter.SelectedIndex = IndexOfAdapter(existing.AdapterType);

                _BaseUrl.Value = existing.BaseUrl;
                _Model.Value = existing.Model;

                // Prefer the endpoint's own API key; fall back to a legacy Authorization: Bearer header so
                // endpoints saved before the auth-placement model still populate the key field on edit.
                if (!string.IsNullOrEmpty(existing.ApiKey))
                {
                    _ApiKey.Value = existing.ApiKey!;
                }
                else if (existing.Headers != null && existing.Headers.TryGetValue("Authorization", out string? auth) && auth != null)
                {
                    _ApiKey.Value = auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? auth.Substring(7) : auth;
                }
            }
            else
            {
                _BaseUrl.Value = DefaultBaseUrlFor(ParseAdapter(_Adapter.SelectedOption));
            }

            _AuthPlacement.Value = AuthPlacementEnumConverter.ToWire(source.AuthPlacement);
            _AuthParameterName.Value = source.AuthParameterName ?? string.Empty;

            _MaxTokens.Value = source.MaxTokens.ToString(CultureInfo.InvariantCulture);
            _Temperature.Value = source.Temperature.ToString(CultureInfo.InvariantCulture);
            _ContextWindow.Value = source.ContextWindow.ToString(CultureInfo.InvariantCulture);
            _Timeout.Value = source.TimeoutMs.ToString(CultureInfo.InvariantCulture);
            _MaxAgentIterations.Value = source.MaxAgentIterations.HasValue
                ? source.MaxAgentIterations.Value.ToString(CultureInfo.InvariantCulture)
                : string.Empty;
            _ReasoningEffort.Value = source.ReasoningEffort?.Level.HasValue == true
                ? ReasoningLevelEnumConverter.ToWire(source.ReasoningEffort.Level!.Value)
                : string.Empty;
            _GeminiThinkingBudget.Value = source.ReasoningEffort?.GeminiThinkingBudget.HasValue == true
                ? source.ReasoningEffort.GeminiThinkingBudget!.Value.ToString(CultureInfo.InvariantCulture)
                : string.Empty;

            _Form = new Form();
            _Form.Add("Name", _Name, () => _Name.Value.Trim().Length == 0 ? "Name is required." : null);
            _Form.Add("Adapter", _Adapter);
            _Form.Add("Base URL", _BaseUrl, () => RequiresBaseUrl(SelectedAdapter) && _BaseUrl.Value.Trim().Length == 0 ? "Base URL is required." : null);
            _Form.Add("Model", _Model, () => _Model.Value.Trim().Length == 0 ? (SelectedAdapter == AdapterTypeEnum.AzureOpenAi ? "Deployment name is required." : "Model is required.") : null);
            _Form.Add("API key (optional)", _ApiKey);
            _Form.Add("API key placement (bearer/header/query)", _AuthPlacement, () => IsOpenAiFamily(SelectedAdapter) ? ValidatePlacement(_AuthPlacement.Value) : null);
            _Form.Add("Auth parameter name (for header/query)", _AuthParameterName, () => IsOpenAiFamily(SelectedAdapter) ? ValidateParameterName(_AuthPlacement.Value, _AuthParameterName.Value) : null);
            _Form.Add("Region", _Region, () => UsesRegion(SelectedAdapter) && _Region.Value.Trim().Length == 0 ? "Region is required for " + AdapterTypeEnumExtensions.ToKebab(SelectedAdapter) + "." : null);
            _Form.Add("Google Cloud project", _Project, () => SelectedAdapter == AdapterTypeEnum.Vertex && _Project.Value.Trim().Length == 0 ? "Project is required for vertex." : null);
            _Form.Add("Default", _Default);
            _Form.Add("Max output tokens", _MaxTokens, () => ValidateInt(_MaxTokens.Value, "Max output tokens", 1024, 131072));
            _Form.Add("Temperature", _Temperature, () => ValidateDouble(_Temperature.Value, "Temperature", 0.0, 2.0));
            _Form.Add("Context window", _ContextWindow, () => ValidateInt(_ContextWindow.Value, "Context window", 1024, 1048576));
            _Form.Add("Timeout (ms)", _Timeout, () => ValidateInt(_Timeout.Value, "Timeout", 10000, int.MaxValue));
            _Form.Add("Auto-approve tools immediately", _AutoApprove);
            _Form.Add("Auto-approve seconds (blank = global, 0 = off)", _ToolAutoApproveSeconds, () => ValidateOptionalInt(_ToolAutoApproveSeconds.Value, "Tool auto-approve seconds", 0, int.MaxValue));
            _Form.Add("Max agent iterations (blank = global)", _MaxAgentIterations, () => ValidateOptionalInt(_MaxAgentIterations.Value, "Max agent iterations", 1, 1024));
            _Form.Add("Reasoning effort (blank = off)", _ReasoningEffort, () => ValidateOptionalEffort(_ReasoningEffort.Value));
            _Form.Add("Gemini thinking budget (blank = default)", _GeminiThinkingBudget, () => ValidateOptionalInt(_GeminiThinkingBudget.Value, "Gemini thinking budget", -1, 32768));
            _Form.Add("Show thinking", _ShowThinking);
            _Form.SubmitRequested += (sender, e) => Complete();
            _Host = new ScrollingFormHost(_Form);
            UpdateAdaptiveFields();
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc/>
        public override bool HandleKey(KeyEvent key)
        {
            if (key.Code == KeyCode.Escape)
            {
                Close(null);
                return true;
            }

            if (key.Code == KeyCode.Enter && key.Modifiers == KeyModifiers.None)
            {
                Submit();
                return true;
            }

            // Track the adapter selection across the key so that changing it can re-seed the Base URL
            // prefill. This only rewrites a value that still matches a known default, so a URL the user
            // has typed or edited is never clobbered.
            int adapterBefore = _Adapter.SelectedIndex;
            bool handled = _Form.HandleKey(key);
            OnAdapterMaybeChanged(adapterBefore);
            return handled;
        }

        /// <inheritdoc/>
        public override bool HandlePaste(string text)
        {
            // A pasted API key or URL goes to the focused text field; a paste over a selector is dropped.
            _Form.HandlePaste(text);
            return true;
        }

        /// <inheritdoc/>
        public override bool HandleMouse(MouseEvent mouse)
        {
            int adapterBefore = _Adapter.SelectedIndex;
            _Host.HandleMouse(mouse);

            // Clicking the adapter selector re-seeds the Base URL prefill and the visible fields, mirroring the
            // keyboard path.
            OnAdapterMaybeChanged(adapterBefore);
            return true;
        }

        /// <inheritdoc/>
        public override void Render(ISurface surface)
        {
            if (surface == null) throw new ArgumentNullException(nameof(surface));

            int screenWidth = surface.Size.Width;
            int screenHeight = surface.Size.Height;

            int hintRows = 2; // blank + hint/error
            int contentWidth = Math.Max(8, Math.Min(ContentWidth, screenWidth - 2 - (2 * PadX)));
            int formHeight = _Host.MeasureHeight(contentWidth);
            int boxWidth = Math.Min(screenWidth, contentWidth + 2 + (2 * PadX));

            // Cap the box to the screen; when the form is taller than the visible area the content is
            // scrolled to keep the focused field in view (see the scroll offset below).
            int maxContentHeight = Math.Max(1, screenHeight - hintRows - 2 - (2 * PadY));
            int visibleFormHeight = Math.Min(formHeight, maxContentHeight);
            int boxHeight = Math.Min(screenHeight, visibleFormHeight + hintRows + 2 + (2 * PadY));

            int boxX = Math.Max(0, (screenWidth - boxWidth) / 2);
            int boxY = Math.Max(0, (screenHeight - boxHeight) / 2);
            Rect box = new Rect(boxX, boxY, boxWidth, boxHeight);

            surface.Fill(box, Cell.Blank(CellStyle.Default));
            surface.DrawBox(box, CellStyle.Default.WithForeground(Color.FromPalette(8)), _Title);

            int contentX = boxX + 1 + PadX;
            int firstRow = boxY + 1 + PadY;
            int usableHeight = boxHeight - 2 - (2 * PadY) - hintRows;
            if (usableHeight < 1)
            {
                return;
            }

            // The host scrolls the form so the focused field, and any error under it, stays in view.
            _Host.Render(surface, new Rect(contentX, firstRow, contentWidth, usableHeight));

            int hintRow = boxY + boxHeight - 2;
            string? errors = _Host.ErrorSummary;
            if (errors != null)
            {
                surface.DrawText(contentX, hintRow, Trim(errors, contentWidth), CellStyle.Default.WithForeground(Color.FromPalette(9)));
            }
            else
            {
                surface.DrawText(contentX, hintRow, Trim("Tab/↑↓ move · Space toggle · Enter save · Esc cancel", contentWidth), CellStyle.Default.WithForeground(Color.FromPalette(8)));
            }
        }

        #endregion

        #region Private-Methods

        private void Submit()
        {
            // Submit validates every field, draws each error under its field, focuses the first invalid one,
            // and raises SubmitRequested (handled by Complete) only when the whole form is valid.
            _Form.Submit();
        }

        private void Complete()
        {
            AdapterTypeEnum adapterType = ParseAdapter(_Adapter.SelectedOption);
            string apiKey = _ApiKey.Value.Trim();

            EndpointConfig endpoint = new EndpointConfig
            {
                Name = _Name.Value.Trim(),
                AdapterType = adapterType,
                BaseUrl = NormalizeBaseUrl(adapterType, _BaseUrl.Value.Trim()),
                Model = _Model.Value.Trim(),
                IsDefault = _Default.Checked,
                MaxTokens = int.Parse(_MaxTokens.Value.Trim(), CultureInfo.InvariantCulture),
                Temperature = double.Parse(_Temperature.Value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture),
                ContextWindow = int.Parse(_ContextWindow.Value.Trim(), CultureInfo.InvariantCulture),
                TimeoutMs = int.Parse(_Timeout.Value.Trim(), CultureInfo.InvariantCulture),
                AutoApproveTools = _AutoApprove.Checked,
                ToolAutoApproveSeconds = ParseOptionalInt(_ToolAutoApproveSeconds.Value),
                MaxAgentIterations = ParseOptionalInt(_MaxAgentIterations.Value),
                ReasoningEffort = BuildReasoningConfig(),
                ShowThinking = _ShowThinking.Checked,

                // The key is stored on the endpoint and placed by AuthPlacement (bearer header, a caller-named
                // header, or a query-string parameter) — the engine applies it, so no Authorization header is
                // written here.
                ApiKey = UsesApiKey(adapterType) && apiKey.Length > 0 ? apiKey : null,
                AuthPlacement = IsOpenAiFamily(adapterType) ? AuthPlacementEnumConverter.Parse(_AuthPlacement.Value) : AuthPlacementEnum.Bearer,
                AuthParameterName = IsOpenAiFamily(adapterType) && _AuthParameterName.Value.Trim().Length > 0 ? _AuthParameterName.Value.Trim() : null,
                Region = UsesRegion(adapterType) && _Region.Value.Trim().Length > 0 ? _Region.Value.Trim() : null,
                Project = adapterType == AdapterTypeEnum.Vertex && _Project.Value.Trim().Length > 0 ? _Project.Value.Trim() : null,

                // Azure's v1 API needs no api-version; a stored value is kept as is (the engine ignores it).
                ApiVersion = _Existing?.ApiVersion,

                // The quirks object is not editable here; carry it over so an edit never silently resets a
                // hand-tuned backend behavior profile back to the adapter defaults.
                Quirks = _Existing?.Quirks
            };

            // Preserve any custom headers other than Authorization: the API key is now carried on the endpoint
            // (see AuthPlacement above), so a stale legacy Authorization header is intentionally dropped.
            if (_Existing?.Headers != null)
            {
                foreach (KeyValuePair<string, string> header in _Existing.Headers)
                {
                    if (!string.Equals(header.Key, "Authorization", StringComparison.OrdinalIgnoreCase))
                    {
                        endpoint.Headers[header.Key] = header.Value;
                    }
                }
            }

            Close(endpoint);
        }

        private static string? ValidateInt(string value, string label, int min, int max)
        {
            string trimmed = (value ?? string.Empty).Trim();
            if (trimmed.Length == 0)
            {
                return label + " is required.";
            }

            if (!int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
            {
                return label + " must be a whole number.";
            }

            if (parsed < min || parsed > max)
            {
                return $"{label} must be between {min} and {max}.";
            }

            return null;
        }

        private static string? ValidateDouble(string value, string label, double min, double max)
        {
            string trimmed = (value ?? string.Empty).Trim();
            if (trimmed.Length == 0)
            {
                return label + " is required.";
            }

            if (!double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed))
            {
                return label + " must be a number.";
            }

            if (parsed < min || parsed > max)
            {
                return $"{label} must be between {min} and {max}.";
            }

            return null;
        }

        private ReasoningEffortConfig? BuildReasoningConfig()
        {
            string levelText = _ReasoningEffort.Value.Trim();
            if (levelText.Length == 0)
            {
                return null;
            }

            if (!ReasoningLevelEnumConverter.TryParse(levelText, out ReasoningLevelEnum level))
            {
                return null;
            }

            // Preserve overrides the form does not surface (OpenAI value, Ollama think) on an edit.
            ReasoningEffortConfig config = _Existing?.ReasoningEffort?.Clone() ?? new ReasoningEffortConfig();
            config.Level = level;

            string budgetText = _GeminiThinkingBudget.Value.Trim();
            if (budgetText.Length > 0
                && int.TryParse(budgetText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int budget))
            {
                config.GeminiThinkingBudget = budget;
            }
            else
            {
                config.GeminiThinkingBudget = null;
            }

            return config;
        }

        private static string? ValidatePlacement(string value)
        {
            string trimmed = (value ?? string.Empty).Trim().ToLowerInvariant();
            if (trimmed.Length == 0 || Array.IndexOf(_Placements, trimmed) >= 0)
            {
                return null;
            }

            return "Placement must be " + string.Join(", ", _Placements) + ".";
        }

        private static string? ValidateParameterName(string placement, string parameterName)
        {
            AuthPlacementEnum resolved = AuthPlacementEnumConverter.Parse(placement);
            if (resolved != AuthPlacementEnum.Bearer && (parameterName ?? string.Empty).Trim().Length == 0)
            {
                return "Auth parameter name is required for header or query placement.";
            }

            return null;
        }

        private static string? ValidateOptionalEffort(string value)
        {
            string trimmed = (value ?? string.Empty).Trim();
            if (trimmed.Length == 0)
            {
                return null;
            }

            return ReasoningLevelEnumConverter.TryParse(trimmed, out _)
                ? null
                : "Reasoning effort must be none, minimal, low, medium, high, xhigh, or max.";
        }

        private static string? ValidateOptionalInt(string value, string label, int min, int max)
        {
            string trimmed = (value ?? string.Empty).Trim();
            if (trimmed.Length == 0)
            {
                return null;
            }

            return ValidateInt(trimmed, label, min, max);
        }

        private static int? ParseOptionalInt(string value)
        {
            string trimmed = (value ?? string.Empty).Trim();
            if (trimmed.Length == 0)
            {
                return null;
            }

            return int.Parse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture);
        }

        private AdapterTypeEnum SelectedAdapter => ParseAdapter(_Adapter.SelectedOption);

        // Re-seeds a still-default Base URL and re-applies field visibility when the adapter selection moved.
        private void OnAdapterMaybeChanged(int adapterBefore)
        {
            if (_Adapter.SelectedIndex == adapterBefore)
            {
                return;
            }

            if (IsDefaultBaseUrl(_BaseUrl.Value))
            {
                _BaseUrl.Value = DefaultBaseUrlFor(SelectedAdapter);
            }

            UpdateAdaptiveFields();
        }

        // Shows only the fields that apply to the selected adapter, matching the desktop, web, and VS Code
        // endpoint forms: key placement for the Chat Completions adapters and Ollama; an API key for every
        // keyed adapter (not the cloud-IAM ones); region for Vertex and Bedrock; project for Vertex.
        private void UpdateAdaptiveFields()
        {
            AdapterTypeEnum adapter = SelectedAdapter;
            bool openAiFamily = IsOpenAiFamily(adapter);
            SetVisible(_ApiKey, UsesApiKey(adapter));
            SetVisible(_AuthPlacement, openAiFamily);
            SetVisible(_AuthParameterName, openAiFamily);
            SetVisible(_Region, UsesRegion(adapter));
            SetVisible(_Project, adapter == AdapterTypeEnum.Vertex);
            SetVisible(_GeminiThinkingBudget, adapter == AdapterTypeEnum.Gemini || adapter == AdapterTypeEnum.Vertex);

            SetLabel(_BaseUrl, RequiresBaseUrl(adapter)
                ? (adapter == AdapterTypeEnum.AzureOpenAi ? "Base URL (https://RESOURCE.openai.azure.com)" : "Base URL")
                : adapter == AdapterTypeEnum.OllamaCloud ? "Base URL (fixed: https://ollama.com)" : "Base URL (blank = provider default)");
            SetLabel(_Model, adapter == AdapterTypeEnum.AzureOpenAi ? "Deployment name" : adapter == AdapterTypeEnum.Bedrock ? "Model or inference profile id" : "Model");
            SetLabel(_ApiKey, KeyLabel(adapter));
            SetLabel(_Region, adapter == AdapterTypeEnum.Bedrock
                ? "AWS region (auth: AWS_ACCESS_KEY_ID + SECRET)"
                : "GCP region (auth: Application Default Credentials)");
        }

        private void SetVisible(IWidget widget, bool visible)
        {
            int index = IndexOfField(widget);
            if (index >= 0)
            {
                _Form.SetFieldVisible(index, visible);
            }
        }

        private void SetLabel(IWidget widget, string label)
        {
            int index = IndexOfField(widget);
            if (index >= 0)
            {
                _Form.SetFieldLabel(index, label);
            }
        }

        private int IndexOfField(IWidget widget)
        {
            for (int i = 0; i < _Form.FieldCount; i++)
            {
                if (ReferenceEquals(_Form.GetField(i), widget))
                {
                    return i;
                }
            }

            return -1;
        }

        private static string KeyLabel(AdapterTypeEnum adapter)
        {
            switch (adapter)
            {
                case AdapterTypeEnum.OpenAi: return "API key (blank = OPENAI_API_KEY)";
                case AdapterTypeEnum.Anthropic: return "API key (blank = ANTHROPIC_API_KEY)";
                case AdapterTypeEnum.Gemini: return "API key (blank = GEMINI_API_KEY / GOOGLE_API_KEY)";
                case AdapterTypeEnum.XAi: return "API key (blank = XAI_API_KEY)";
                case AdapterTypeEnum.OllamaCloud: return "API key (blank = OLLAMA_API_KEY)";
                case AdapterTypeEnum.AzureOpenAi: return "API key (Azure OpenAI)";
                default: return "API key (optional; ${ENV_VAR} references work)";
            }
        }

        // The Chat Completions adapters and Ollama honor a configurable key placement; every other keyed adapter
        // has a fixed scheme (bearer for openai/xai/ollama-cloud, x-api-key for anthropic, and so on).
        private static bool IsOpenAiFamily(AdapterTypeEnum adapter) =>
            adapter == AdapterTypeEnum.Ollama || adapter == AdapterTypeEnum.OpenAiCompatible || adapter == AdapterTypeEnum.Vllm;

        // Vertex and Bedrock authenticate with cloud credentials (ADC / AWS signing), never an API key.
        private static bool UsesApiKey(AdapterTypeEnum adapter) => adapter != AdapterTypeEnum.Vertex && adapter != AdapterTypeEnum.Bedrock;

        private static bool UsesRegion(AdapterTypeEnum adapter) => adapter == AdapterTypeEnum.Vertex || adapter == AdapterTypeEnum.Bedrock;

        // Self-hosted servers and Azure have no public default; the hosted adapters fall back to their API.
        private static bool RequiresBaseUrl(AdapterTypeEnum adapter) =>
            IsOpenAiFamily(adapter) || adapter == AdapterTypeEnum.AzureOpenAi;

        private static int IndexOfAdapter(AdapterTypeEnum adapterType)
        {
            int index = Array.IndexOf(_Adapters, AdapterTypeEnumExtensions.ToKebab(adapterType));
            return index < 0 ? 0 : index;
        }

        private static AdapterTypeEnum ParseAdapter(string adapter)
        {
            return AdapterTypeEnumExtensions.FromKebab(adapter);
        }

        private static string DefaultBaseUrlFor(AdapterTypeEnum adapterType)
        {
            switch (adapterType)
            {
                case AdapterTypeEnum.XAi: return "https://api.x.ai/v1";
                case AdapterTypeEnum.OllamaCloud: return "https://ollama.com";
                case AdapterTypeEnum.Ollama: return OllamaDefaultBaseUrl;
                case AdapterTypeEnum.OpenAiCompatible:
                case AdapterTypeEnum.Vllm: return OpenAiDefaultBaseUrl;
                default: return string.Empty; // hosted APIs use their public default; Azure needs the resource URL
            }
        }

        private static bool IsDefaultBaseUrl(string? value)
        {
            string trimmed = (value ?? string.Empty).Trim().TrimEnd('/');
            return trimmed.Length == 0
                || string.Equals(trimmed, "https://api.x.ai/v1", StringComparison.OrdinalIgnoreCase)
                || string.Equals(trimmed, "https://ollama.com", StringComparison.OrdinalIgnoreCase)
                || string.Equals(trimmed, OllamaDefaultBaseUrl, StringComparison.OrdinalIgnoreCase)
                || string.Equals(trimmed, OpenAiDefaultBaseUrl, StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeBaseUrl(AdapterTypeEnum adapterType, string baseUrl)
        {
            // The Ollama adapter speaks Ollama's native API at the server root; a trailing /v1 targets the
            // separate OpenAI-compatible surface and 404s. Drop it so a stray /v1 is never persisted.
            if (adapterType == AdapterTypeEnum.OllamaCloud) return "https://ollama.com";
            if (adapterType == AdapterTypeEnum.XAi && string.IsNullOrWhiteSpace(baseUrl)) return "https://api.x.ai/v1";
            if (adapterType == AdapterTypeEnum.Ollama)
            {
                string trimmed = baseUrl.TrimEnd('/');
                if (trimmed.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
                {
                    return trimmed.Substring(0, trimmed.Length - "/v1".Length);
                }
            }

            return baseUrl;
        }

        private static string Trim(string text, int width)
        {
            if (width <= 0)
            {
                return string.Empty;
            }

            return text.Length <= width ? text : text.Substring(0, Math.Max(0, width - 1)) + "…";
        }

        #endregion
    }
}
