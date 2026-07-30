namespace Mux.Cli.App
{
    using System;
    using Mux.Core.Enums;
    using Mux.Core.Models;
    using TUIKit;
    using TUIKit.Input;
    using TUIKit.Modals;
    using TUIKit.Widgets;

    /// <summary>
    /// A single modal form for creating or editing an endpoint. It hosts a <see cref="Form"/> with fields
    /// for the name, adapter type, base URL, model, and an optional API key, navigated with Tab; Enter
    /// validates and returns a populated <see cref="EndpointConfig"/> (via <see cref="Modal.Completion"/>),
    /// and Escape cancels (returns null).
    /// </summary>
    public sealed class EndpointFormModal : Modal
    {
        #region Private-Members

        private const int PadX = 3;
        private const int PadY = 1;
        private const int ContentWidth = 46;

        private static readonly string[] _Adapters = { "openai-compatible", "ollama", "openai", "vllm" };

        private readonly string _Title;
        private readonly Form _Form;
        private readonly TextField _Name;
        private readonly RadioGroup _Adapter;
        private readonly TextField _BaseUrl;
        private readonly TextField _Model;
        private readonly TextField _ApiKey;
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

            _Name = new TextField();
            _Adapter = new RadioGroup(_Adapters);
            _BaseUrl = new TextField();
            _Model = new TextField();
            _ApiKey = new TextField();

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
                _BaseUrl.Value = "http://localhost:11434/v1";
            }

            _Form = new Form();
            _Form.Add("Name", _Name, () => _Name.Value.Trim().Length == 0 ? "Name is required." : null);
            _Form.Add("Adapter", _Adapter);
            _Form.Add("Base URL", _BaseUrl, () => _BaseUrl.Value.Trim().Length == 0 ? "Base URL is required." : null);
            _Form.Add("Model", _Model, () => _Model.Value.Trim().Length == 0 ? "Model is required." : null);
            _Form.Add("API key (optional)", _ApiKey);
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

            return _Form.HandleKey(key);
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
            int boxHeight = Math.Min(screenHeight, formHeight + hintRows + 2 + (2 * PadY));

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

            // The Form only renders its field widgets into a BufferSurface, so render it to a buffer and
            // copy the cells into the modal box.
            CellBuffer buffer = new CellBuffer(contentWidth, usableHeight);
            _Form.Render(new BufferSurface(buffer));
            for (int y = 0; y < usableHeight; y++)
            {
                for (int x = 0; x < contentWidth; x++)
                {
                    surface.Set(contentX + x, firstRow + y, buffer.Get(x, y));
                }
            }

            int hintRow = boxY + boxHeight - 2;
            if (_Error.Length > 0)
            {
                surface.DrawText(contentX, hintRow, Trim(_Error, contentWidth), CellStyle.Default.WithForeground(Color.FromPalette(9)));
            }
            else
            {
                surface.DrawText(contentX, hintRow, Trim("Tab/↑↓ to move · Enter to save · Esc to cancel", contentWidth), CellStyle.Default.WithForeground(Color.FromPalette(8)));
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

            EndpointConfig endpoint = new EndpointConfig
            {
                Name = _Name.Value.Trim(),
                AdapterType = ParseAdapter(_Adapter.SelectedOption),
                BaseUrl = _BaseUrl.Value.Trim(),
                Model = _Model.Value.Trim()
            };

            string apiKey = _ApiKey.Value.Trim();
            if (apiKey.Length > 0)
            {
                endpoint.Headers["Authorization"] = "Bearer " + apiKey;
            }

            Close(endpoint);
        }

        private int EstimateFormHeight()
        {
            // Each field renders a label row plus its widget height plus a spacing row.
            int height = 0;
            height += 1 + 1 + 1;               // Name
            height += 1 + _Adapters.Length + 1; // Adapter (one row per option)
            height += 1 + 1 + 1;               // Base URL
            height += 1 + 1 + 1;               // Model
            height += 1 + 1 + 1;               // API key
            return height;
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
