namespace Mux.Cli.App
{
    using System;
    using System.Collections.Generic;
    using Mux.Core.Cloud;
    using TUIKit;
    using TUIKit.Input;
    using TUIKit.Modals;
    using TUIKit.Widgets;

    /// <summary>
    /// The terminal UI's Mux Cloud sign-in form: email, masked password, server URL, and machine name, under the
    /// notice every connect screen shows (remote users can operate this machine, including approving tools, and how to
    /// turn remote approval off). Enter returns a <see cref="CloudLoginInput"/>; Escape cancels.
    /// </summary>
    public sealed class CloudLoginModal : Modal
    {
        #region Private-Members

        private const int PadX = 3;
        private const int PadY = 1;
        private const int ContentWidth = 64;

        private readonly Form _Form;
        private readonly ScrollingFormHost _Host;
        private readonly TextField _Email;
        private readonly TextField _Password;
        private readonly TextField _ServerUrl;
        private readonly TextField _MachineName;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="CloudLoginModal"/> class.
        /// </summary>
        /// <param name="serverUrl">Server URL to prefill.</param>
        /// <param name="machineName">Machine name to prefill.</param>
        /// <param name="email">Email to prefill, or empty.</param>
        public CloudLoginModal(string serverUrl, string machineName, string email)
        {
            _Email = new TextField { Value = email ?? string.Empty, Placeholder = "you@example.com" };
            _Password = SecretFields.Create();
            _ServerUrl = new TextField { Value = serverUrl ?? string.Empty };
            _MachineName = new TextField { Value = machineName ?? string.Empty };
            _Form = new Form();
            _Form.AddSection("Sign in to Mux Cloud");
            _Form.Add("Email", _Email, () => string.IsNullOrWhiteSpace(_Email.Value) ? "Email is required." : null);
            _Form.Add("Password", _Password, () => string.IsNullOrEmpty(_Password.Value) ? "Password is required." : null);
            _Form.Add("Server URL", _ServerUrl);
            _Form.Add("Machine name", _MachineName);
            _Form.SubmitRequested += (sender, e) => Close(new CloudLoginInput
            {
                Email = _Email.Value.Trim(),
                Password = _Password.Value,
                ServerUrl = _ServerUrl.Value.Trim(),
                MachineName = _MachineName.Value.Trim()
            });
            _Host = new ScrollingFormHost(_Form);
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
                _Form.Submit();
                return true;
            }

            return _Form.HandleKey(key);
        }

        /// <inheritdoc/>
        public override bool HandlePaste(string text)
        {
            _Form.HandlePaste(text);
            return true;
        }

        /// <inheritdoc/>
        public override bool HandleMouse(MouseEvent mouse)
        {
            _Host.HandleMouse(mouse);
            return true;
        }

        /// <inheritdoc/>
        public override void Render(ISurface surface)
        {
            if (surface == null) throw new ArgumentNullException(nameof(surface));

            int screenWidth = surface.Size.Width;
            int screenHeight = surface.Size.Height;
            int contentWidth = Math.Max(8, Math.Min(ContentWidth, screenWidth - 2 - (2 * PadX)));
            List<string> notice = Wrap(CloudAccountService.RemoteAccessNoticeFor("/cloud remote-approval off"), contentWidth);
            int noticeRows = notice.Count + 1;
            List<string> signup = Wrap(CloudSignup.Hint(_ServerUrl.Value), contentWidth);
            int hintRows = 2 + signup.Count;
            int formHeight = _Host.MeasureHeight(contentWidth);
            int boxWidth = Math.Min(screenWidth, contentWidth + 2 + (2 * PadX));
            int maxContentHeight = Math.Max(1, screenHeight - hintRows - noticeRows - 2 - (2 * PadY));
            int visibleFormHeight = Math.Min(formHeight, maxContentHeight);
            int boxHeight = Math.Min(screenHeight, visibleFormHeight + noticeRows + hintRows + 2 + (2 * PadY));
            int boxX = Math.Max(0, (screenWidth - boxWidth) / 2);
            int boxY = Math.Max(0, (screenHeight - boxHeight) / 2);
            Rect box = new Rect(boxX, boxY, boxWidth, boxHeight);

            surface.Fill(box, Cell.Blank(CellStyle.Default));
            surface.DrawBox(box, CellStyle.Default.WithForeground(Color.FromPalette(8)), "Mux Cloud");

            int contentX = boxX + 1 + PadX;
            int row = boxY + 1 + PadY;
            foreach (string line in notice)
            {
                if (row >= boxY + boxHeight - 1) break;
                surface.DrawText(contentX, row++, line, CellStyle.Default.WithForeground(Color.FromPalette(11)));
            }

            row++;
            int usableHeight = boxY + boxHeight - 1 - PadY - hintRows - row;
            if (usableHeight >= 1)
            {
                _Host.Render(surface, new Rect(contentX, row, contentWidth, usableHeight));
            }

            int hintRow = boxY + boxHeight - 2;
            int signupRow = hintRow - 1 - signup.Count;
            foreach (string line in signup)
            {
                if (signupRow > boxY) surface.DrawText(contentX, signupRow, line, CellStyle.Default.WithForeground(Color.FromPalette(8)));
                signupRow++;
            }

            string? errors = _Host.ErrorSummary;
            string? reveal = SecretFields.Hint(_Form.FocusedLeaf, "password");
            string hint = errors ?? (reveal == null ? "Tab/↑↓ move · Enter sign in · Esc cancel" : "Tab/↑↓ move · " + reveal + " · Enter sign in · Esc cancel");
            surface.DrawText(contentX, hintRow, hint.Length > contentWidth ? hint.Substring(0, contentWidth) : hint, CellStyle.Default.WithForeground(Color.FromPalette((byte)(errors != null ? 9 : 8))));
        }

        #endregion

        #region Private-Methods

        private static List<string> Wrap(string text, int width)
        {
            List<string> lines = new List<string>();
            string current = string.Empty;
            foreach (string word in (text ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (current.Length == 0) current = word;
                else if (current.Length + 1 + word.Length <= width) current += " " + word;
                else
                {
                    lines.Add(current);
                    current = word;
                }
            }

            if (current.Length > 0) lines.Add(current);
            return lines;
        }

        #endregion
    }
}
