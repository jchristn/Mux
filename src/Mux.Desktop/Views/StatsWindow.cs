namespace Mux.Desktop.Views
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using Avalonia;
    using Avalonia.Controls;
    using Avalonia.Input.Platform;
    using Avalonia.Layout;
    using Avalonia.Media;
    using Avalonia.Threading;
    using Mux.Core.Telemetry;

    /// <summary>
    /// A per-conversation statistics window: token totals by type (as a horizontal bar chart with values),
    /// time-to-first-token and latency (avg / p95 / p99), the number of turns, the model's context window,
    /// and context utilization. A copy icon copies the statistics as JSON to the clipboard. Metrics come
    /// from the shared usage telemetry store scoped to the conversation's session id.
    /// </summary>
    public sealed class StatsWindow : Window
    {
        private const double TrackWidth = 220;

        private static readonly IBrush InputColor = new SolidColorBrush(Color.Parse("#4c8bf5"));
        private static readonly IBrush CachedColor = new SolidColorBrush(Color.Parse("#3fb950"));
        private static readonly IBrush OutputColor = new SolidColorBrush(Color.Parse("#d29922"));

        private readonly string _Json;
        private readonly TextBlock _CopyGlyph = new TextBlock { Text = "⧉", FontSize = 16 };
        private DispatcherTimer? _CopyResetTimer;

        /// <summary>
        /// Instantiate the statistics window.
        /// </summary>
        /// <param name="metrics">The session's usage metrics. Required.</param>
        /// <param name="turns">The number of user turns in the conversation.</param>
        /// <param name="contextWindow">The active model's context window (tokens); 0 when unknown.</param>
        /// <param name="estimatedTokens">The estimated current context usage (tokens).</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="metrics"/> is null.</exception>
        public StatsWindow(UsageMetrics metrics, int turns, int contextWindow, int estimatedTokens)
        {
            ArgumentNullException.ThrowIfNull(metrics);

            AppTheme theme = AppTheme.Current;
            _Json = BuildJson(metrics, turns, contextWindow, estimatedTokens);
            _CopyGlyph.Foreground = theme.Muted;

            Title = "Conversation statistics";
            Icon = IconResources.LoadWindowIcon();
            Width = 540;
            SizeToContent = SizeToContent.Height;
            CanResize = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = theme.Surface;

            StackPanel panel = new StackPanel { Margin = new Thickness(28, 26, 28, 26), Spacing = 14 };

            panel.Children.Add(BuildHeader(theme));

            panel.Children.Add(KeyValue("Turns", turns.ToString("#,##0"), theme));
            panel.Children.Add(KeyValue("Model calls", metrics.Calls.ToString("#,##0"), theme));

            panel.Children.Add(SectionLabel("Tokens", theme));
            long max = Math.Max(1, Math.Max(metrics.InputTokens, Math.Max(metrics.CachedTokens, metrics.OutputTokens)));
            panel.Children.Add(TokenBar("Input", metrics.InputTokens, max, InputColor, theme));
            panel.Children.Add(TokenBar("Cached", metrics.CachedTokens, max, CachedColor, theme));
            panel.Children.Add(TokenBar("Output", metrics.OutputTokens, max, OutputColor, theme));
            panel.Children.Add(KeyValue("Total tokens", metrics.TotalTokens.ToString("#,##0"), theme));

            panel.Children.Add(SectionLabel("Latency", theme));
            panel.Children.Add(KeyValue("Time to first token", Percentiles(metrics.AvgTtftMs, metrics.P95TtftMs, metrics.P99TtftMs), theme));
            panel.Children.Add(KeyValue("Total latency", Percentiles(metrics.AvgTotalMs, metrics.P95TotalMs, metrics.P99TotalMs), theme));

            panel.Children.Add(SectionLabel("Context", theme));
            panel.Children.Add(KeyValue("Context window", contextWindow > 0 ? contextWindow.ToString("#,##0") + " tokens" : "unknown", theme));
            panel.Children.Add(ContextUtilization(estimatedTokens, contextWindow, theme));

            Button close = new Button { Content = "Close", HorizontalAlignment = HorizontalAlignment.Right, Background = theme.AccentButton, Foreground = theme.AccentText, Margin = new Thickness(0, 6, 0, 0) };
            close.Click += (sender, args) => Close();
            panel.Children.Add(close);

            Content = panel;
        }

        private Control BuildHeader(AppTheme theme)
        {
            DockPanel header = new DockPanel();

            TextBlock title = new TextBlock { Text = "Conversation statistics", FontSize = 18, FontWeight = FontWeight.SemiBold, Foreground = theme.Text, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(title, Dock.Left);
            header.Children.Add(title);

            Button copy = new Button
            {
                Content = _CopyGlyph,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(6, 2, 6, 2),
                HorizontalAlignment = HorizontalAlignment.Right
            };
            ToolTip.SetTip(copy, "Copy statistics as JSON");
            copy.Click += (sender, args) => CopyJson();
            DockPanel.SetDock(copy, Dock.Right);
            header.Children.Add(copy);

            return header;
        }

        private void CopyJson()
        {
            IClipboard? clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard == null)
            {
                return;
            }

            _ = clipboard.SetTextAsync(_Json);
            _CopyGlyph.Text = "✓";

            _CopyResetTimer?.Stop();
            _CopyResetTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.2) };
            _CopyResetTimer.Tick += (sender, args) =>
            {
                _CopyResetTimer?.Stop();
                _CopyResetTimer = null;
                _CopyGlyph.Text = "⧉";
            };
            _CopyResetTimer.Start();
        }

        private static TextBlock SectionLabel(string text, AppTheme theme)
        {
            return new TextBlock { Text = text, Foreground = theme.Muted, FontSize = 11, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 6, 0, 0) };
        }

        private static Control KeyValue(string key, string value, AppTheme theme)
        {
            StackPanel row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            row.Children.Add(new TextBlock { Text = key, Width = 150, Foreground = theme.Muted, FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
            row.Children.Add(new TextBlock { Text = value, Foreground = theme.Text, FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
            return row;
        }

        private static Control TokenBar(string label, long value, long max, IBrush color, AppTheme theme)
        {
            double fraction = max > 0 ? (double)value / max : 0;

            Border fill = new Border { Width = Math.Max(2, fraction * TrackWidth), Height = 14, Background = color, CornerRadius = new CornerRadius(3), HorizontalAlignment = HorizontalAlignment.Left };
            Border track = new Border { Width = TrackWidth, Height = 14, Background = theme.SurfaceAlt, CornerRadius = new CornerRadius(3), VerticalAlignment = VerticalAlignment.Center, Child = fill };

            StackPanel row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 2, 0, 2) };
            row.Children.Add(new TextBlock { Text = label, Width = 70, Foreground = theme.Muted, FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
            row.Children.Add(track);
            row.Children.Add(new TextBlock { Text = value.ToString("#,##0"), Foreground = theme.Text, FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
            return row;
        }

        private static Control ContextUtilization(int estimatedTokens, int contextWindow, AppTheme theme)
        {
            double fraction = contextWindow > 0 ? Math.Clamp((double)estimatedTokens / contextWindow, 0, 1) : 0;
            IBrush color = fraction >= 0.9 ? theme.Error : theme.Accent;

            Border fill = new Border { Width = Math.Max(2, fraction * TrackWidth), Height = 14, Background = color, CornerRadius = new CornerRadius(3), HorizontalAlignment = HorizontalAlignment.Left };
            Border track = new Border { Width = TrackWidth, Height = 14, Background = theme.SurfaceAlt, CornerRadius = new CornerRadius(3), VerticalAlignment = VerticalAlignment.Center, Child = fill };

            string caption = contextWindow > 0
                ? (fraction * 100).ToString("0.#") + "%  (est " + estimatedTokens.ToString("#,##0") + " / " + contextWindow.ToString("#,##0") + ")"
                : "unknown";

            StackPanel row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 2, 0, 2) };
            row.Children.Add(new TextBlock { Text = "Utilization", Width = 70, Foreground = theme.Muted, FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
            row.Children.Add(track);
            row.Children.Add(new TextBlock { Text = caption, Foreground = theme.Text, FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
            return row;
        }

        private static string Percentiles(double avg, double p95, double p99)
        {
            return "avg " + avg.ToString("#,##0") + " · p95 " + p95.ToString("#,##0") + " · p99 " + p99.ToString("#,##0") + " ms";
        }

        private static string BuildJson(UsageMetrics metrics, int turns, int contextWindow, int estimatedTokens)
        {
            Dictionary<string, object?> tokens = new Dictionary<string, object?>
            {
                ["input"] = metrics.InputTokens,
                ["cached"] = metrics.CachedTokens,
                ["output"] = metrics.OutputTokens,
                ["total"] = metrics.TotalTokens
            };

            Dictionary<string, object?> ttft = new Dictionary<string, object?>
            {
                ["avgMs"] = metrics.AvgTtftMs,
                ["p95Ms"] = metrics.P95TtftMs,
                ["p99Ms"] = metrics.P99TtftMs
            };

            Dictionary<string, object?> latency = new Dictionary<string, object?>
            {
                ["avgMs"] = metrics.AvgTotalMs,
                ["p95Ms"] = metrics.P95TotalMs,
                ["p99Ms"] = metrics.P99TotalMs
            };

            Dictionary<string, object?> root = new Dictionary<string, object?>
            {
                ["turns"] = turns,
                ["modelCalls"] = metrics.Calls,
                ["tokens"] = tokens,
                ["timeToFirstToken"] = ttft,
                ["latency"] = latency,
                ["contextWindow"] = contextWindow,
                ["estimatedTokens"] = estimatedTokens,
                ["contextUtilization"] = contextWindow > 0 ? (double)estimatedTokens / contextWindow : 0
            };

            return JsonSerializer.Serialize(root, new JsonSerializerOptions { WriteIndented = true });
        }
    }
}
