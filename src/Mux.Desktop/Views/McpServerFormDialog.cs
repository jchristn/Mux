namespace Mux.Desktop.Views
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using Avalonia;
    using Avalonia.Controls;
    using Avalonia.Layout;
    using Avalonia.Media;
    using Mux.Core.Enums;
    using Mux.Core.Models;

    /// <summary>
    /// A form for creating or editing a single MCP server, at field parity with the TUI's MCP form: name,
    /// transport, command/args/env (stdio), URL/MCP path (http), and authentication (none, bearer token, or
    /// API key header + value). Pre-fills from the supplied <see cref="McpServerConfig"/> and, on Save, writes
    /// the edited values back and returns true via <c>ShowDialog&lt;bool&gt;</c>.
    /// </summary>
    public sealed class McpServerFormDialog : Window
    {
        private readonly McpServerConfig _Config;
        private readonly TextBox _Name = new TextBox();
        private readonly ComboBox _Transport = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        private readonly TextBox _Command = new TextBox();
        private readonly TextBox _Args = new TextBox { AcceptsReturn = true, MinHeight = 60, TextWrapping = TextWrapping.Wrap };
        private readonly TextBox _Env = new TextBox { AcceptsReturn = true, MinHeight = 60, TextWrapping = TextWrapping.Wrap };
        private readonly TextBox _Url = new TextBox();
        private readonly TextBox _McpPath = new TextBox();
        private readonly ComboBox _AuthType = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        private readonly TextBox _BearerToken = new TextBox { PasswordChar = '●' };
        private readonly TextBox _ApiKeyHeader = new TextBox();
        private readonly TextBox _ApiKeyValue = new TextBox { PasswordChar = '●' };
        private readonly TextBlock _Error = new TextBlock { Foreground = new SolidColorBrush(Color.Parse("#cf222e")), FontSize = 12, TextWrapping = TextWrapping.Wrap };

        /// <summary>
        /// Instantiate the MCP server form.
        /// </summary>
        /// <param name="config">The server to edit (pre-filled and written back on Save). Required.</param>
        /// <param name="isNew">True when creating a new server (affects the title only).</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="config"/> is null.</exception>
        public McpServerFormDialog(McpServerConfig config, bool isNew)
        {
            ArgumentNullException.ThrowIfNull(config);
            _Config = config;

            Title = isNew ? "Add MCP server" : "Edit MCP server";
            Icon = IconResources.LoadWindowIcon();
            Width = 560;
            Height = 720;
            MinWidth = 460;
            MinHeight = 480;
            CanResize = true;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = AppTheme.Current.Surface;

            _Transport.ItemsSource = Enum.GetValues(typeof(McpTransportTypeEnum));
            _Transport.SelectedItem = config.Transport;
            _AuthType.ItemsSource = Enum.GetValues(typeof(McpAuthTypeEnum));
            _AuthType.SelectedItem = config.Auth.Type;
            _Name.Text = config.Name;
            _Command.Text = config.Command;
            _Args.Text = string.Join(Environment.NewLine, config.Args);
            _Env.Text = FormatEnv(config.Env);
            _Url.Text = config.Url;
            _McpPath.Text = string.IsNullOrWhiteSpace(config.McpPath) ? "/mcp" : config.McpPath;
            _BearerToken.Text = config.Auth.BearerToken;
            _ApiKeyHeader.Text = string.IsNullOrWhiteSpace(config.Auth.ApiKeyHeader) ? "X-API-Key" : config.Auth.ApiKeyHeader;
            _ApiKeyValue.Text = config.Auth.ApiKeyValue;

            Content = BuildContent();
        }

        private Control BuildContent()
        {
            AppTheme theme = AppTheme.Current;
            DockPanel root = new DockPanel { Margin = new Thickness(20) };

            StackPanel buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Margin = new Thickness(0, 12, 0, 0) };
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

            StackPanel form = new StackPanel { Spacing = 10, Margin = new Thickness(0, 0, 14, 0) };
            form.Children.Add(Row("Name", _Name));
            form.Children.Add(Row("Transport", _Transport));
            form.Children.Add(Row("Command (stdio)", _Command));
            form.Children.Add(Row("Args (one per line)", _Args));
            form.Children.Add(Row("Env (KEY=VALUE per line)", _Env));
            form.Children.Add(Row("URL (http)", _Url));
            form.Children.Add(Row("MCP path (http)", _McpPath));
            form.Children.Add(Row("Authentication", _AuthType));
            form.Children.Add(Row("Bearer token", _BearerToken));
            form.Children.Add(Row("API key header", _ApiKeyHeader));
            form.Children.Add(Row("API key value", _ApiKeyValue));

            root.Children.Add(new ScrollViewer { Content = form, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto });
            return root;
        }

        private Control Row(string label, Control control)
        {
            StackPanel panel = new StackPanel { Spacing = 4 };
            panel.Children.Add(new TextBlock { Text = label, Foreground = AppTheme.Current.Text, FontWeight = FontWeight.SemiBold });
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

            McpTransportTypeEnum transport = _Transport.SelectedItem is McpTransportTypeEnum selected ? selected : McpTransportTypeEnum.Stdio;
            if (transport == McpTransportTypeEnum.Stdio && (_Command.Text ?? string.Empty).Trim().Length == 0)
            {
                _Error.Text = "Command is required for stdio servers.";
                return;
            }

            if (transport == McpTransportTypeEnum.Http && (_Url.Text ?? string.Empty).Trim().Length == 0)
            {
                _Error.Text = "URL is required for HTTP servers.";
                return;
            }

            McpAuthTypeEnum authType = _AuthType.SelectedItem is McpAuthTypeEnum auth ? auth : McpAuthTypeEnum.None;
            if (authType == McpAuthTypeEnum.Bearer && (_BearerToken.Text ?? string.Empty).Trim().Length == 0)
            {
                _Error.Text = "A bearer token is required for bearer authentication.";
                return;
            }

            if (authType == McpAuthTypeEnum.ApiKey)
            {
                if ((_ApiKeyHeader.Text ?? string.Empty).Trim().Length == 0)
                {
                    _Error.Text = "An API key header is required for API key authentication.";
                    return;
                }

                if ((_ApiKeyValue.Text ?? string.Empty).Trim().Length == 0)
                {
                    _Error.Text = "An API key value is required for API key authentication.";
                    return;
                }
            }

            _Config.Name = name;
            _Config.Transport = transport;
            _Config.Command = (_Command.Text ?? string.Empty).Trim();
            _Config.Args = ParseLines(_Args.Text);
            _Config.Env = ParseEnv(_Env.Text);
            _Config.Url = (_Url.Text ?? string.Empty).Trim();
            _Config.McpPath = (_McpPath.Text ?? string.Empty).Trim();
            _Config.Auth.Type = authType;
            _Config.Auth.BearerToken = (_BearerToken.Text ?? string.Empty).Trim();
            _Config.Auth.ApiKeyHeader = (_ApiKeyHeader.Text ?? string.Empty).Trim();
            _Config.Auth.ApiKeyValue = (_ApiKeyValue.Text ?? string.Empty).Trim();
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

        private static Dictionary<string, string> ParseEnv(string? text)
        {
            Dictionary<string, string> result = new Dictionary<string, string>();
            foreach (string line in ParseLines(text))
            {
                int index = line.IndexOf('=');
                if (index <= 0)
                {
                    continue;
                }

                string key = line.Substring(0, index).Trim();
                string value = line.Substring(index + 1).Trim();
                if (key.Length > 0)
                {
                    result[key] = value;
                }
            }

            return result;
        }

        private static string FormatEnv(Dictionary<string, string> env)
        {
            StringBuilder builder = new StringBuilder();
            foreach (KeyValuePair<string, string> pair in env)
            {
                builder.Append(pair.Key).Append('=').Append(pair.Value).Append(Environment.NewLine);
            }

            return builder.ToString().TrimEnd();
        }
    }
}
