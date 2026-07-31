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
    /// A single modal form for creating or editing an MCP server. It hosts a <see cref="Form"/> with
    /// fields for the name, transport, and the transport-specific settings (command / args / env for
    /// <c>stdio</c>; url / mcp path for <c>http</c>), navigated with Tab; Enter validates and returns a
    /// populated <see cref="McpServerConfig"/> (via <see cref="Modal.Completion"/>), and Escape cancels
    /// (returns null). Both transports' fields are always shown; validation and the built result depend
    /// on the selected transport.
    /// </summary>
    public sealed class McpServerFormModal : Modal
    {
        #region Private-Members

        private const int PadX = 3;
        private const int PadY = 1;
        private const int ContentWidth = 46;

        private static readonly string[] _Transports = { "stdio", "http" };

        private readonly string _Title;
        private readonly Form _Form;
        private readonly TextField _Name;
        private readonly RadioGroup _Transport;
        private readonly TextField _Command;
        private readonly TextField _Args;
        private readonly TextField _Env;
        private readonly TextField _Url;
        private readonly TextField _McpPath;
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

            if (existing != null)
            {
                _Name.Value = existing.Name;
                int target = IndexOfTransport(existing.Transport);
                for (int i = 0; i < target; i++)
                {
                    _Transport.HandleKey(KeyEvent.Special(KeyCode.Down));
                }

                _Command.Value = existing.Command;
                _Args.Value = existing.Args != null ? string.Join(" ", existing.Args) : string.Empty;
                _Env.Value = FormatEnv(existing.Env);
                _Url.Value = existing.Url;
                _McpPath.Value = string.IsNullOrWhiteSpace(existing.McpPath) ? "/mcp" : existing.McpPath;
            }
            else
            {
                _McpPath.Value = "/mcp";
            }

            _Form = new Form();
            _Form.Add("Name", _Name, () => _Name.Value.Trim().Length == 0 ? "Name is required." : null);
            _Form.Add("Transport", _Transport);
            _Form.Add("Command (stdio)", _Command, ValidateCommand);
            _Form.Add("Args (space-separated)", _Args);
            _Form.Add("Env (KEY=VALUE, comma-sep)", _Env);
            _Form.Add("URL (http)", _Url, ValidateUrl);
            _Form.Add("MCP path (http)", _McpPath);
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

        private string? ValidateCommand()
        {
            return ParseTransport(_Transport.SelectedOption) == McpTransportTypeEnum.Stdio && _Command.Value.Trim().Length == 0
                ? "Command is required for stdio."
                : null;
        }

        private string? ValidateUrl()
        {
            return ParseTransport(_Transport.SelectedOption) == McpTransportTypeEnum.Http && _Url.Value.Trim().Length == 0
                ? "URL is required for http."
                : null;
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
            }

            Close(server);
        }

        private int EstimateFormHeight()
        {
            // Each field renders a label row plus its widget height plus a spacing row.
            int height = 0;
            height += 1 + 1 + 1;                  // Name
            height += 1 + _Transports.Length + 1; // Transport (one row per option)
            height += 1 + 1 + 1;                  // Command
            height += 1 + 1 + 1;                  // Args
            height += 1 + 1 + 1;                  // Env
            height += 1 + 1 + 1;                  // URL
            height += 1 + 1 + 1;                  // MCP path
            return height;
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
