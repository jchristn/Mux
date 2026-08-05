namespace Mux.Cli.App
{
    using System;
    using System.Collections.Generic;
    using Mux.Core.Enums;
    using Mux.Core.Models;
    using TUIKit;
    using TUIKit.Input;
    using TUIKit.Modals;
    using TUIKit.Widgets;

    /// <summary>
    /// A single modal form for creating or editing an MCP server. It hosts a <see cref="Form"/> whose
    /// fields change with the selected transport: <c>stdio</c> shows command / args / env, while
    /// <c>http</c> shows url / mcp path plus an authentication section (none / bearer token / API key).
    /// The fields for the other transport are never shown, so switching transports cannot leave stale
    /// values on screen. Tab or the arrow keys move between fields; Enter validates and returns a
    /// populated <see cref="McpServerConfig"/> (via <see cref="Modal.Completion"/>), and Escape cancels
    /// (returns null). Changing the transport or the auth type rebuilds the field list in place.
    /// </summary>
    public sealed class McpServerFormModal : Modal
    {
        #region Private-Members

        private const int PadX = 3;
        private const int PadY = 1;
        private const int ContentWidth = 46;
        private const char MaskChar = '•';

        // Fixed field indices used to restore focus to a selector after the form is rebuilt.
        private const int TransportFieldIndex = 1;
        private const int AuthFieldIndex = 4; // Name, Transport, URL, MCP path, Auth (http layout only).

        private static readonly string[] _Transports = { "stdio", "http" };
        private static readonly string[] _AuthTypes = { "none", "bearer", "apikey" };

        private readonly string _Title;
        private readonly TextField _Name;
        private readonly RadioGroup _Transport;
        private readonly TextField _Command;
        private readonly TextField _Args;
        private readonly TextField _Env;
        private readonly TextField _Url;
        private readonly TextField _McpPath;
        private readonly RadioGroup _AuthType;
        private readonly TextField _BearerToken;
        private readonly TextField _ApiKeyHeader;
        private readonly TextField _ApiKeyValue;

        private Form _Form;
        private int _FormHeight;
        private string _Error = string.Empty;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="McpServerFormModal"/> class.
        /// </summary>
        /// <param name="title">The box title (for example "Add MCP server").</param>
        /// <param name="existing">An MCP server to pre-fill for editing, or null to start empty.</param>
        public McpServerFormModal(string title, McpServerConfig? existing = null)
        {
            _Title = title ?? "MCP server";

            _Name = new TextField();
            _Transport = new RadioGroup(_Transports);
            _Command = new TextField();
            _Args = new TextField();
            _Env = new TextField();
            _Url = new TextField();
            _McpPath = new TextField();
            _AuthType = new RadioGroup(_AuthTypes);
            _BearerToken = new TextField { MaskChar = MaskChar };
            _ApiKeyHeader = new TextField();
            _ApiKeyValue = new TextField { MaskChar = MaskChar };

            _ApiKeyHeader.Value = "X-API-Key";
            _McpPath.Value = "/mcp";

            if (existing != null)
            {
                _Name.Value = existing.Name;
                SelectRadio(_Transport, IndexOfTransport(existing.Transport));

                _Command.Value = existing.Command;
                _Args.Value = existing.Args != null ? string.Join(" ", existing.Args) : string.Empty;
                _Env.Value = FormatEnv(existing.Env);
                _Url.Value = existing.Url;
                _McpPath.Value = string.IsNullOrWhiteSpace(existing.McpPath) ? "/mcp" : existing.McpPath;

                McpAuthConfig auth = existing.Auth ?? new McpAuthConfig();
                SelectRadio(_AuthType, IndexOfAuthType(auth.Type));
                _BearerToken.Value = auth.BearerToken ?? string.Empty;
                _ApiKeyHeader.Value = string.IsNullOrWhiteSpace(auth.ApiKeyHeader) ? "X-API-Key" : auth.ApiKeyHeader;
                _ApiKeyValue.Value = auth.ApiKeyValue ?? string.Empty;
            }

            _Form = BuildForm();
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

            int previousTransport = _Transport.SelectedIndex;
            int previousAuth = _AuthType.SelectedIndex;
            bool handled = _Form.HandleKey(key);

            if (_Transport.SelectedIndex != previousTransport)
            {
                RebuildForm(TransportFieldIndex);
            }
            else if (_AuthType.SelectedIndex != previousAuth)
            {
                RebuildForm(AuthFieldIndex);
            }

            return handled;
        }

        /// <inheritdoc/>
        public override void Render(ISurface surface)
        {
            if (surface == null) throw new ArgumentNullException(nameof(surface));

            int screenWidth = surface.Size.Width;
            int screenHeight = surface.Size.Height;

            int formHeight = _FormHeight;
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
                surface.DrawText(contentX, hintRow, Trim("Tab/↑↓ move · Enter save · Esc cancel", contentWidth), CellStyle.Default.WithForeground(Color.FromPalette(8)));
            }
        }

        #endregion

        #region Private-Methods

        private Form BuildForm()
        {
            Form form = new Form();
            int height = 0;

            form.Add("Name", _Name, () => _Name.Value.Trim().Length == 0 ? "Name is required." : null);
            height += FieldHeight(1);
            form.Add("Transport", _Transport);
            height += FieldHeight(_Transports.Length);

            if (ParseTransport(_Transport.SelectedOption) == McpTransportTypeEnum.Stdio)
            {
                form.Add("Command (stdio)", _Command, () => _Command.Value.Trim().Length == 0 ? "Command is required for stdio." : null);
                height += FieldHeight(1);
                form.Add("Args (space-separated)", _Args);
                height += FieldHeight(1);
                form.Add("Env (KEY=VALUE, comma-sep)", _Env);
                height += FieldHeight(1);
            }
            else
            {
                form.Add("URL (http)", _Url, () => _Url.Value.Trim().Length == 0 ? "URL is required for http." : null);
                height += FieldHeight(1);
                form.Add("MCP path (http)", _McpPath);
                height += FieldHeight(1);
                form.Add("Auth (none/bearer/apikey)", _AuthType);
                height += FieldHeight(_AuthTypes.Length);

                switch (ParseAuthType(_AuthType.SelectedOption))
                {
                    case McpAuthTypeEnum.Bearer:
                        form.Add("Bearer token", _BearerToken, () => _BearerToken.Value.Trim().Length == 0 ? "Bearer token is required." : null);
                        height += FieldHeight(1);
                        break;

                    case McpAuthTypeEnum.ApiKey:
                        form.Add("API key header", _ApiKeyHeader, () => _ApiKeyHeader.Value.Trim().Length == 0 ? "API key header is required." : null);
                        height += FieldHeight(1);
                        form.Add("API key value", _ApiKeyValue, () => _ApiKeyValue.Value.Trim().Length == 0 ? "API key value is required." : null);
                        height += FieldHeight(1);
                        break;

                    case McpAuthTypeEnum.None:
                    default:
                        break;
                }
            }

            _FormHeight = height;
            return form;
        }

        private void RebuildForm(int focusIndex)
        {
            _Form = BuildForm();

            // A freshly built form focuses its first field; advance focus to the selector that changed
            // so the user can keep adjusting it and watch the dependent fields update.
            int target = Math.Min(focusIndex, _Form.FieldCount - 1);
            for (int i = 0; i < target; i++)
            {
                _Form.HandleKey(KeyEvent.Special(KeyCode.Tab));
            }
        }

        private void Submit()
        {
            string? error = _Form.Validate();
            if (error != null)
            {
                _Error = error;
                return;
            }

            McpTransportTypeEnum transport = ParseTransport(_Transport.SelectedOption);

            McpServerConfig server = new McpServerConfig
            {
                Name = _Name.Value.Trim(),
                Transport = transport
            };

            if (transport == McpTransportTypeEnum.Stdio)
            {
                server.Command = _Command.Value.Trim();
                server.Args = ParseArgs(_Args.Value);
                server.Env = ParseEnv(_Env.Value);
            }
            else
            {
                server.Url = _Url.Value.Trim();
                server.McpPath = string.IsNullOrWhiteSpace(_McpPath.Value) ? "/mcp" : _McpPath.Value.Trim();
                server.Auth = BuildAuth();
            }

            Close(server);
        }

        private McpAuthConfig BuildAuth()
        {
            McpAuthTypeEnum type = ParseAuthType(_AuthType.SelectedOption);
            McpAuthConfig auth = new McpAuthConfig { Type = type };

            switch (type)
            {
                case McpAuthTypeEnum.Bearer:
                    auth.BearerToken = _BearerToken.Value.Trim();
                    break;

                case McpAuthTypeEnum.ApiKey:
                    auth.ApiKeyHeader = string.IsNullOrWhiteSpace(_ApiKeyHeader.Value) ? "X-API-Key" : _ApiKeyHeader.Value.Trim();
                    auth.ApiKeyValue = _ApiKeyValue.Value.Trim();
                    break;

                case McpAuthTypeEnum.None:
                default:
                    break;
            }

            return auth;
        }

        private static int FieldHeight(int widgetHeight)
        {
            // A field renders a label row, the widget, and a spacing row.
            return 1 + widgetHeight + 1;
        }

        private static void SelectRadio(RadioGroup group, int targetIndex)
        {
            for (int i = 0; i < targetIndex; i++)
            {
                group.HandleKey(KeyEvent.Special(KeyCode.Down));
            }
        }

        private static int IndexOfTransport(McpTransportTypeEnum transport)
        {
            return transport == McpTransportTypeEnum.Http ? 1 : 0;
        }

        private static McpTransportTypeEnum ParseTransport(string transport)
        {
            return string.Equals(transport, "http", StringComparison.OrdinalIgnoreCase)
                ? McpTransportTypeEnum.Http
                : McpTransportTypeEnum.Stdio;
        }

        private static int IndexOfAuthType(McpAuthTypeEnum type)
        {
            return type switch
            {
                McpAuthTypeEnum.Bearer => 1,
                McpAuthTypeEnum.ApiKey => 2,
                _ => 0
            };
        }

        private static McpAuthTypeEnum ParseAuthType(string type)
        {
            if (string.Equals(type, "bearer", StringComparison.OrdinalIgnoreCase))
            {
                return McpAuthTypeEnum.Bearer;
            }

            if (string.Equals(type, "apikey", StringComparison.OrdinalIgnoreCase))
            {
                return McpAuthTypeEnum.ApiKey;
            }

            return McpAuthTypeEnum.None;
        }

        private static List<string> ParseArgs(string value)
        {
            List<string> args = new List<string>();
            if (string.IsNullOrWhiteSpace(value))
            {
                return args;
            }

            foreach (string token in value.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
            {
                args.Add(token);
            }

            return args;
        }

        private static Dictionary<string, string> ParseEnv(string value)
        {
            Dictionary<string, string> env = new Dictionary<string, string>();
            if (string.IsNullOrWhiteSpace(value))
            {
                return env;
            }

            foreach (string pair in value.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                int equals = pair.IndexOf('=');
                if (equals <= 0)
                {
                    continue;
                }

                string key = pair.Substring(0, equals).Trim();
                string val = pair.Substring(equals + 1).Trim();
                if (key.Length > 0)
                {
                    env[key] = val;
                }
            }

            return env;
        }

        private static string FormatEnv(Dictionary<string, string>? env)
        {
            if (env == null || env.Count == 0)
            {
                return string.Empty;
            }

            List<string> pairs = new List<string>(env.Count);
            foreach (KeyValuePair<string, string> entry in env)
            {
                pairs.Add($"{entry.Key}={entry.Value}");
            }

            return string.Join(", ", pairs);
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
