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
    using Mux.Core.Models;
    using Mux.Core.Tools;

    /// <summary>
    /// Validates connectivity to a single MCP server by initializing a temporary
    /// <see cref="McpToolManager"/> for it and reporting the connection result — connected/failed, transport,
    /// discovered tool count, and the tool names (or the error on failure).
    /// </summary>
    public sealed class McpValidationWindow : Window
    {
        private readonly McpServerConfig _Server;
        private readonly StackPanel _Body = new StackPanel { Spacing = 8 };

        /// <summary>
        /// Instantiate and start validating the given server.
        /// </summary>
        /// <param name="server">The server to validate. Required.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="server"/> is null.</exception>
        public McpValidationWindow(McpServerConfig server)
        {
            _Server = server ?? throw new ArgumentNullException(nameof(server));

            AppTheme theme = AppTheme.Current;

            Title = "Validate MCP server";
            Icon = IconResources.LoadWindowIcon();
            Width = 560;
            Height = 460;
            MinWidth = 420;
            MinHeight = 300;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = theme.Surface;

            DockPanel root = new DockPanel { Margin = new Thickness(20) };
            TextBlock title = new TextBlock { Text = "Validating \"" + server.Name + "\"", FontSize = 18, FontWeight = FontWeight.SemiBold, Foreground = theme.Text };
            DockPanel.SetDock(title, Dock.Top);
            root.Children.Add(title);

            _Body.Margin = new Thickness(0, 14, 0, 0);
            root.Children.Add(new ScrollViewer { Content = _Body });
            Content = root;

            _Body.Children.Add(new TextBlock { Text = "Connecting…", Foreground = theme.Muted });
            _ = RunAsync();
        }

        private async Task RunAsync()
        {
            string? error = null;
            List<McpConnectionResult> results = new List<McpConnectionResult>();
            List<ToolDefinition> tools = new List<ToolDefinition>();

            try
            {
                using McpToolManager manager = new McpToolManager(new List<McpServerConfig> { _Server });
                using CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                await manager.InitializeAsync(cts.Token);
                results = manager.GetConnectionResults();
                tools = manager.GetToolDefinitions();
            }
            catch (Exception exception)
            {
                error = exception.Message;
            }

            List<McpConnectionResult> capturedResults = results;
            List<ToolDefinition> capturedTools = tools;
            string? capturedError = error;
            Dispatcher.UIThread.Post(() => Render(capturedResults, capturedTools, capturedError));
        }

        private void Render(List<McpConnectionResult> results, List<ToolDefinition> tools, string? error)
        {
            AppTheme theme = AppTheme.Current;
            _Body.Children.Clear();

            if (error != null)
            {
                _Body.Children.Add(Pill("Failed", theme.Error, Brushes.White));
                _Body.Children.Add(new TextBlock { Text = error, Foreground = theme.Text, TextWrapping = TextWrapping.Wrap });
                return;
            }

            bool connected = false;
            foreach (McpConnectionResult result in results)
            {
                connected = connected || result.Connected;
                _Body.Children.Add(Pill(result.Connected ? "Connected" : "Failed", result.Connected ? theme.Success : theme.Error, Brushes.White));
                _Body.Children.Add(new TextBlock { Text = "Transport: " + result.Method, Foreground = theme.Muted, FontSize = 12 });
                _Body.Children.Add(new TextBlock { Text = "Tools discovered: " + result.ToolCount, Foreground = theme.Muted, FontSize = 12 });
                if (!string.IsNullOrEmpty(result.Error))
                {
                    _Body.Children.Add(new TextBlock { Text = result.Error, Foreground = theme.Error, TextWrapping = TextWrapping.Wrap });
                }
            }

            if (results.Count == 0)
            {
                _Body.Children.Add(new TextBlock { Text = "No connection result was reported.", Foreground = theme.Muted });
            }

            if (connected && tools.Count > 0)
            {
                _Body.Children.Add(new TextBlock { Text = "Tools", FontWeight = FontWeight.SemiBold, Foreground = theme.Text, Margin = new Thickness(0, 8, 0, 0) });
                foreach (ToolDefinition tool in tools)
                {
                    _Body.Children.Add(new TextBlock { Text = "• " + tool.Name, Foreground = theme.Text, FontSize = 12 });
                }
            }
        }

        private static Control Pill(string text, IBrush background, IBrush foreground)
        {
            return new Border
            {
                Background = background,
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 2, 8, 2),
                HorizontalAlignment = HorizontalAlignment.Left,
                Child = new TextBlock { Text = text, Foreground = foreground, FontSize = 12, FontWeight = FontWeight.SemiBold }
            };
        }
    }
}
