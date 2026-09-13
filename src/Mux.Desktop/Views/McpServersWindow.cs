namespace Mux.Desktop.Views
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Avalonia;
    using Avalonia.Controls;
    using Avalonia.Layout;
    using Avalonia.Media;
    using Avalonia.Threading;
    using Mux.Core.Enums;
    using Mux.Core.Models;
    using Mux.Core.Settings;
    using Mux.Core.Tools;
    using Mux.Desktop.I18n;

    /// <summary>
    /// A manager for configured MCP servers (parity with the TUI's <c>/mcp</c>): a sortable table with a live
    /// connectivity glyph (green ✓ / red ✗, probed on open) and per-row actions (edit, validate, delete).
    /// Persists through <see cref="SettingsLoader.SaveMcpServers"/>.
    /// </summary>
    public sealed class McpServersWindow : Window
    {
        private readonly List<McpServerConfig> _Servers;
        private readonly DataTableView<McpServerConfig> _Table;
        private readonly Dictionary<string, bool?> _Connectivity = new Dictionary<string, bool?>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Instantiate the MCP servers manager.
        /// </summary>
        /// <param name="onChanged">Invoked after servers are saved; null to ignore.</param>
        public McpServersWindow(Action? onChanged)
        {
            _ = onChanged;
            _Servers = SettingsLoader.LoadMcpServers();

            AppTheme theme = AppTheme.Current;

            Title = Localizer.T("nav.mcp");
            Icon = IconResources.LoadWindowIcon();
            Width = 900;
            Height = 580;
            MinWidth = 640;
            MinHeight = 360;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = theme.Surface;

            _Table = new DataTableView<McpServerConfig>(BuildColumns(), BuildActions, OnEdit);
            Content = BuildLayout(theme);
            Refresh();
        }

        /// <summary>
        /// Probe connectivity for all servers once shown.
        /// </summary>
        /// <param name="e">The event data.</param>
        protected override void OnOpened(EventArgs e)
        {
            base.OnOpened(e);
            _ = ProbeAsync();
        }

        private List<TableColumn<McpServerConfig>> BuildColumns()
        {
            return new List<TableColumn<McpServerConfig>>
            {
                new TableColumn<McpServerConfig>("", StatusGlyph, new GridLength(28), null, null, StatusBrush, tooltip: Localizer.T("mcp.col.status.tip")),
                new TableColumn<McpServerConfig>(Localizer.T("col.name"), s => s.Name, new GridLength(2, GridUnitType.Star), s => s.Name, tooltip: Localizer.T("mcp.col.name.tip")),
                new TableColumn<McpServerConfig>(Localizer.T("mcp.transport"), s => s.Transport.ToString(), new GridLength(1, GridUnitType.Star), s => s.Transport.ToString(), tooltip: Localizer.T("mcp.col.transport.tip")),
                new TableColumn<McpServerConfig>(Localizer.T("mcp.target"), DescribeTarget, new GridLength(3, GridUnitType.Star), DescribeTarget, tooltip: Localizer.T("mcp.col.target.tip")),
                new TableColumn<McpServerConfig>(Localizer.T("mcp.auth"), s => s.Auth.Type.ToString(), new GridLength(1, GridUnitType.Star), s => s.Auth.Type.ToString(), tooltip: Localizer.T("mcp.col.auth.tip"))
            };
        }

        private IReadOnlyList<TableRowAction<McpServerConfig>> BuildActions(McpServerConfig server)
        {
            return new List<TableRowAction<McpServerConfig>>
            {
                new TableRowAction<McpServerConfig>(Localizer.T("act.edit"), s => OnEdit(s)),
                new TableRowAction<McpServerConfig>(Localizer.T("mcp.action.validate"), s => OnValidate(s)),
                new TableRowAction<McpServerConfig>(Localizer.T("act.delete"), s => OnDelete(s), destructive: true)
            };
        }

        private string StatusGlyph(McpServerConfig server)
        {
            if (!_Connectivity.TryGetValue(server.Name, out bool? state) || state == null)
            {
                return "…";
            }

            return state.Value ? "✓" : "✗";
        }

        private IBrush StatusBrush(McpServerConfig server)
        {
            AppTheme theme = AppTheme.Current;
            if (!_Connectivity.TryGetValue(server.Name, out bool? state) || state == null)
            {
                return theme.Muted;
            }

            return state.Value ? theme.Success : theme.Error;
        }

        private static string DescribeTarget(McpServerConfig server)
        {
            if (server.Transport == McpTransportTypeEnum.Http)
            {
                return string.IsNullOrEmpty(server.Url) ? "—" : server.Url + server.McpPath;
            }

            string command = string.IsNullOrEmpty(server.Command) ? "—" : server.Command;
            if (server.Args.Count > 0)
            {
                command += " " + string.Join(" ", server.Args);
            }

            return command;
        }

        private Control BuildLayout(AppTheme theme)
        {
            DockPanel root = new DockPanel { Margin = new Thickness(20) };

            DockPanel header = new DockPanel();
            TextBlock title = new TextBlock { Text = Localizer.T("nav.mcp"), FontSize = 20, FontWeight = FontWeight.SemiBold, Foreground = theme.Text, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(title, Dock.Left);
            header.Children.Add(title);

            Button add = new Button { Content = "＋  " + Localizer.T("mcp.addServer"), Background = theme.AccentButton, Foreground = theme.AccentText, HorizontalAlignment = HorizontalAlignment.Right, Padding = new Thickness(12, 6, 12, 6) };
            add.Tip(Localizer.T("mcp.addServer.tip"));
            add.Click += (sender, args) => OnAdd();
            DockPanel.SetDock(add, Dock.Right);
            header.Children.Add(add);

            DockPanel.SetDock(header, Dock.Top);
            root.Children.Add(header);

            _Table.Margin = new Thickness(0, 14, 0, 0);
            root.Children.Add(_Table);
            return root;
        }

        private void Refresh()
        {
            _Table.SetRows(_Servers);
        }

        private async Task ProbeAsync()
        {
            if (_Servers.Count == 0)
            {
                return;
            }

            Dictionary<string, bool> outcome = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using McpToolManager manager = new McpToolManager(new List<McpServerConfig>(_Servers));
                using CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                await manager.InitializeAsync(cts.Token);
                foreach (McpConnectionResult result in manager.GetConnectionResults())
                {
                    outcome[result.Name] = result.Connected;
                }
            }
            catch (Exception)
            {
                // Leave unresolved servers as unknown.
            }

            Dictionary<string, bool> captured = outcome;
            Dispatcher.UIThread.Post(() =>
            {
                foreach (McpServerConfig server in _Servers)
                {
                    _Connectivity[server.Name] = captured.TryGetValue(server.Name, out bool value) ? value : (bool?)false;
                }

                Refresh();
            });
        }

        private async void OnAdd()
        {
            McpServerConfig server = new McpServerConfig { Transport = McpTransportTypeEnum.Stdio };
            if (await new McpServerFormDialog(server, isNew: true).ShowDialog<bool>(this))
            {
                _Servers.Add(server);
                Persist();
            }
        }

        private async void OnEdit(McpServerConfig server)
        {
            if (await new McpServerFormDialog(server, isNew: false).ShowDialog<bool>(this))
            {
                Persist();
            }
        }

        private void OnValidate(McpServerConfig server)
        {
            _ = new McpValidationWindow(server).ShowDialog(this);
        }

        private async void OnDelete(McpServerConfig server)
        {
            bool confirmed = await new ConfirmDialog(Localizer.T("mcp.delete.title"), Localizer.T("mcp.delete.confirm") + " \"" + server.Name + "\"?", Localizer.T("act.delete"), destructive: true).ShowDialog<bool>(this);
            if (confirmed)
            {
                _Servers.Remove(server);
                Persist();
            }
        }

        private void Persist()
        {
            try
            {
                SettingsLoader.SaveMcpServers(_Servers);
            }
            catch (Exception)
            {
                // Best-effort.
            }

            Refresh();
            _ = ProbeAsync();
        }
    }
}
