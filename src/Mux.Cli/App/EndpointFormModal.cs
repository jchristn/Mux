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
    /// A single modal form for creating or editing an endpoint. It hosts a <see cref="Form"/> with fields
    /// for the name, adapter type, base URL, model, an optional API key, and every tunable endpoint
    /// parameter (default flag, max output tokens, temperature, context window, timeout, tool auto-approval,
    /// and the optional max-agent-iterations override), navigated with Tab; Enter validates and returns a
    /// populated <see cref="EndpointConfig"/> (via <see cref="Modal.Completion"/>), and Escape cancels
    /// (returns null). Fields that are not surfaced here — the nested <see cref="EndpointConfig.Quirks"/>
    /// and any non-<c>Authorization</c> headers — are carried over unchanged from the edited endpoint.
    /// </summary>
    public sealed class EndpointFormModal : Modal
    {
        #region Private-Members

        private const int PadX = 3;
        private const int PadY = 1;

        // Wide enough to hold the footer hint and the longest field label ("Max agent iterations…") without
        // truncation on a normal-width terminal; it still shrinks to fit narrower screens in Render.
        private const int ContentWidth = 54;

        // The local-inference default host. The native Ollama API lives at the root; every other adapter
        // speaks the OpenAI-compatible surface under /v1. These seed the Base URL prefill per adapter.
        private const string OllamaDefaultBaseUrl = "http://localhost:11434";
        private const string OpenAiDefaultBaseUrl = "http://localhost:11434/v1";

        private static readonly string[] _Adapters = { "openai-compatible", "ollama", "openai", "vllm" };

        // Per-field widget row heights, in the same order fields are added to the form below. Used to
        // compute the focused field's vertical offset so the modal can scroll a form that is taller than
        // the visible box. Text fields and checkboxes occupy one row; the adapter radio group occupies one
        // row per option.
        private static readonly int[] _WidgetRows =
        {
            1,                    // Name
            _Adapters.Length,     // Adapter
            1,                    // Base URL
            1,                    // Model
            1,                    // API key
            1,                    // Default endpoint
            1,                    // Max output tokens
            1,                    // Temperature
            1,                    // Context window
            1,                    // Timeout
            1,                    // Auto-approve tools
            1,                    // Max agent iterations
        };

        private readonly string _Title;
        private readonly EndpointConfig? _Existing;
        private readonly Form _Form;
        private readonly TextField _Name;
        private readonly RadioGroup _Adapter;
        private readonly TextField _BaseUrl;
        private readonly TextField _Model;
        private readonly TextField _ApiKey;
        private readonly Checkbox _Default;
        private readonly TextField _MaxTokens;
        private readonly TextField _Temperature;
        private readonly TextField _ContextWindow;
        private readonly TextField _Timeout;
        private readonly Checkbox _AutoApprove;
        private readonly TextField _MaxAgentIterations;
        private string _Error = string.Empty;

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
            _Default = new Checkbox("Use as default endpoint", source.IsDefault);
            _MaxTokens = new TextField();
            _Temperature = new TextField();
            _ContextWindow = new TextField();
            _Timeout = new TextField();
            _AutoApprove = new Checkbox("Auto-approve tool calls", source.AutoApproveTools);
            _MaxAgentIterations = new TextField();

            if (existing != null)
            {
                _Name.Value = existing.Name;
                int target = IndexOfAdapter(existing.AdapterType);
                for (int i = 0; i < target; i++)
                {
                    _Adapter.HandleKey(KeyEvent.Special(KeyCode.Down));
                }

                _BaseUrl.Value = existing.BaseUrl;
                _Model.Value = existing.Model;
                if (existing.Headers != null && existing.Headers.TryGetValue("Authorization", out string? auth) && auth != null)
                {
                    _ApiKey.Value = auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? auth.Substring(7) : auth;
                }
            }
            else
            {
                _BaseUrl.Value = DefaultBaseUrlFor(ParseAdapter(_Adapter.SelectedOption));
            }

            _MaxTokens.Value = source.MaxTokens.ToString(CultureInfo.InvariantCulture);
            _Temperature.Value = source.Temperature.ToString(CultureInfo.InvariantCulture);
            _ContextWindow.Value = source.ContextWindow.ToString(CultureInfo.InvariantCulture);
            _Timeout.Value = source.TimeoutMs.ToString(CultureInfo.InvariantCulture);
            _MaxAgentIterations.Value = source.MaxAgentIterations.HasValue
                ? source.MaxAgentIterations.Value.ToString(CultureInfo.InvariantCulture)
                : string.Empty;

            _Form = new Form();
            _Form.Add("Name", _Name, () => _Name.Value.Trim().Length == 0 ? "Name is required." : null);
            _Form.Add("Adapter", _Adapter);
            _Form.Add("Base URL", _BaseUrl, () => _BaseUrl.Value.Trim().Length == 0 ? "Base URL is required." : null);
            _Form.Add("Model", _Model, () => _Model.Value.Trim().Length == 0 ? "Model is required." : null);
            _Form.Add("API key (optional)", _ApiKey);
            _Form.Add("Default", _Default);
            _Form.Add("Max output tokens", _MaxTokens, () => ValidateInt(_MaxTokens.Value, "Max output tokens", 1024, 131072));
            _Form.Add("Temperature", _Temperature, () => ValidateDouble(_Temperature.Value, "Temperature", 0.0, 2.0));
            _Form.Add("Context window", _ContextWindow, () => ValidateInt(_ContextWindow.Value, "Context window", 1024, 1048576));
            _Form.Add("Timeout (ms)", _Timeout, () => ValidateInt(_Timeout.Value, "Timeout", 10000, int.MaxValue));
            _Form.Add("Auto-approve tools", _AutoApprove);
            _Form.Add("Max agent iterations (blank = global)", _MaxAgentIterations, () => ValidateOptionalInt(_MaxAgentIterations.Value, "Max agent iterations", 1, 100));
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

            if (_Adapter.SelectedIndex != adapterBefore && IsDefaultBaseUrl(_BaseUrl.Value))
            {
                _BaseUrl.Value = DefaultBaseUrlFor(ParseAdapter(_Adapter.SelectedOption));
            }

            return handled;
        }

        /// <inheritdoc/>
        public override void Render(ISurface surface)
        {
            if (surface == null) throw new ArgumentNullException(nameof(surface));

            int screenWidth = surface.Size.Width;
            int screenHeight = surface.Size.Height;

            int formHeight = EstimateFormHeight();
            int hintRows = 2; // blank + hint/error
            int contentWidth = Math.Max(8, Math.Min(ContentWidth, screenWidth - 2 - (2 * PadX)));
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

            // The Form only renders its field widgets into a BufferSurface, so render the full form into a
            // buffer tall enough to hold every field, then copy a vertical window of it into the modal box.
            // The window is scrolled so the focused field is always visible even when the form overflows.
            int scrollY = ComputeScrollOffset(formHeight, usableHeight);
            CellBuffer buffer = new CellBuffer(contentWidth, Math.Max(usableHeight, formHeight));
            _Form.Render(new BufferSurface(buffer));
            for (int y = 0; y < usableHeight; y++)
            {
                int sourceY = y + scrollY;
                for (int x = 0; x < contentWidth; x++)
                {
                    surface.Set(contentX + x, firstRow + y, buffer.Get(x, sourceY));
                }
            }

            int hintRow = boxY + boxHeight - 2;
            if (_Error.Length > 0)
            {
                surface.DrawText(contentX, hintRow, Trim(_Error, contentWidth), CellStyle.Default.WithForeground(Color.FromPalette(9)));
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
            string? error = _Form.Validate();
            if (error != null)
            {
                _Error = error;
                return;
            }

            AdapterTypeEnum adapterType = ParseAdapter(_Adapter.SelectedOption);

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
                MaxAgentIterations = ParseOptionalInt(_MaxAgentIterations.Value),

                // The quirks object is not editable here; carry it over so an edit never silently resets a
                // hand-tuned backend behavior profile back to the adapter defaults.
                Quirks = _Existing?.Quirks
            };

            // Preserve any custom headers other than Authorization, which is owned by the API key field.
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

            string apiKey = _ApiKey.Value.Trim();
            if (apiKey.Length > 0)
            {
                endpoint.Headers["Authorization"] = "Bearer " + apiKey;
            }

            Close(endpoint);
        }

        private int ComputeScrollOffset(int formHeight, int usableHeight)
        {
            if (formHeight <= usableHeight)
            {
                return 0;
            }

            int focused = _Form.FocusedIndex;
            if (focused < 0 || focused >= _WidgetRows.Length)
            {
                return 0;
            }

            // Reconstruct the focused field's vertical span using the same per-field layout the Form uses:
            // one label row, the widget rows, then a trailing spacer row.
            int top = 0;
            for (int i = 0; i < focused; i++)
            {
                top += 1 + _WidgetRows[i] + 1;
            }

            int blockHeight = 1 + _WidgetRows[focused] + 1;

            int scrollY = 0;
            if (top < scrollY)
            {
                scrollY = top;
            }

            if (top + blockHeight > scrollY + usableHeight)
            {
                scrollY = top + blockHeight - usableHeight;
            }

            int maxScroll = Math.Max(0, formHeight - usableHeight);
            return Math.Clamp(scrollY, 0, maxScroll);
        }

        private int EstimateFormHeight()
        {
            // Each field renders a label row plus its widget height plus a spacing row.
            int height = 0;
            for (int i = 0; i < _WidgetRows.Length; i++)
            {
                height += 1 + _WidgetRows[i] + 1;
            }

            return height;
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

        private static int IndexOfAdapter(AdapterTypeEnum adapterType)
        {
            switch (adapterType)
            {
                case AdapterTypeEnum.Ollama: return 1;
                case AdapterTypeEnum.OpenAi: return 2;
                case AdapterTypeEnum.Vllm: return 3;
                default: return 0;
            }
        }

        private static AdapterTypeEnum ParseAdapter(string adapter)
        {
            switch (adapter)
            {
                case "ollama": return AdapterTypeEnum.Ollama;
                case "openai": return AdapterTypeEnum.OpenAi;
                case "vllm": return AdapterTypeEnum.Vllm;
                default: return AdapterTypeEnum.OpenAiCompatible;
            }
        }

        private static string DefaultBaseUrlFor(AdapterTypeEnum adapterType)
        {
            return adapterType == AdapterTypeEnum.Ollama ? OllamaDefaultBaseUrl : OpenAiDefaultBaseUrl;
        }

        private static bool IsDefaultBaseUrl(string? value)
        {
            string trimmed = (value ?? string.Empty).Trim().TrimEnd('/');
            return string.Equals(trimmed, OllamaDefaultBaseUrl, StringComparison.OrdinalIgnoreCase)
                || string.Equals(trimmed, OpenAiDefaultBaseUrl, StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeBaseUrl(AdapterTypeEnum adapterType, string baseUrl)
        {
            // The Ollama adapter speaks Ollama's native API at the server root; a trailing /v1 targets the
            // separate OpenAI-compatible surface and 404s. Drop it so a stray /v1 is never persisted.
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
