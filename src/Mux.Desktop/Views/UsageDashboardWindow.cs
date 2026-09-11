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
    using Mux.Core.Telemetry;
    using Mux.Desktop.Services;

    /// <summary>
    /// A usage/monitoring window mirroring the mux serve dashboard's Usage page: a range selector, a KPI
    /// strip, metric tabs (tokens/cost/latency/TTFT/streaming/throughput) driving a timeseries chart with
    /// X/Y axis labels, and a sortable recent per-call events table. Only the events table scrolls; the
    /// header, KPIs, tabs, and chart stay pinned.
    /// </summary>
    public sealed class UsageDashboardWindow : Window
    {
        private const string TableColumns = "150,1.2*,1.6*,66,66,86,86,72";

        // Upper bound on events pulled for client-side filtering/aggregation. Generous for a desktop view.
        private const int MaxEvents = 20000;
        private const double PlotHeight = 270;
        private static readonly IBrush PromptColor = new SolidColorBrush(Color.Parse("#4c8bf5"));
        private static readonly IBrush CachedColor = new SolidColorBrush(Color.Parse("#3fb950"));
        private static readonly IBrush OutputColor = new SolidColorBrush(Color.Parse("#d29922"));
        private static readonly IBrush P99Color = new SolidColorBrush(Color.Parse("#dc2626"));
        private static readonly string[] Headers = { "When", "Endpoint", "Conversation", "In", "Out", "Latency", "Cost", "Status" };

        private static readonly string[] HeaderTips =
        {
            "When the call happened (your local time).",
            "The endpoint the call was sent to.",
            "The conversation (session) the call belongs to.",
            "Prompt (input) tokens sent.",
            "Generated (output) tokens received.",
            "Total round-trip time for the call.",
            "Estimated US-dollar cost from the pricing table.",
            "Whether the call succeeded or the error code."
        };

        private readonly IUsageAnalyticsService _Analytics;
        private readonly WrapPanel _KpiStrip = new WrapPanel { Orientation = Orientation.Horizontal };
        private readonly StackPanel _MetricTabs = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(0, 0, 0, 18) };
        private readonly Border _ChartHost = new Border { Height = 340, Margin = new Thickness(0, 0, 0, 8) };
        private readonly Grid _EventsHeader = new Grid { ColumnDefinitions = new ColumnDefinitions(TableColumns), Margin = new Thickness(2, 8, 2, 4) };
        private readonly StackPanel _EventsList = new StackPanel { Spacing = 0 };
        private readonly TextBlock _Status = new TextBlock { FontSize = 12 };
        private readonly Dictionary<UsageRange, Button> _RangeButtons = new Dictionary<UsageRange, Button>();
        private readonly Dictionary<ChartMetric, Button> _MetricButtons = new Dictionary<ChartMetric, Button>();
        private readonly List<UsageBucket> _Series = new List<UsageBucket>();
        private readonly List<UsageEventRow> _AllEvents = new List<UsageEventRow>();
        private readonly List<UsageEventRow> _Events = new List<UsageEventRow>();
        private readonly TextBox _EndpointFilter = new TextBox { Width = 210, PlaceholderText = "Endpoint contains…" };
        private readonly TextBox _ConversationFilter = new TextBox { Width = 230, PlaceholderText = "Conversation contains…" };

        private UsageRange _Range = UsageRange.Day;
        private ChartMetric _Metric = ChartMetric.Tokens;
        private readonly Func<string?, string?>? _ConversationName;
        private bool _LoadTruncated;
        private int _SortColumn;
        private bool _SortDescending = true;

        /// <summary>
        /// Instantiate the usage window.
        /// </summary>
        /// <param name="analytics">The usage analytics service. Required.</param>
        /// <param name="conversationNameResolver">Optional map from a session id to a conversation title.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="analytics"/> is null.</exception>
        public UsageDashboardWindow(IUsageAnalyticsService analytics, Func<string?, string?>? conversationNameResolver = null)
        {
            ArgumentNullException.ThrowIfNull(analytics);
            _Analytics = analytics;
            _ConversationName = conversationNameResolver;

            AppTheme theme = AppTheme.Current;

            Title = "Usage";
            Icon = IconResources.LoadWindowIcon();
            Width = 940;
            Height = 720;
            MinWidth = 720;
            MinHeight = 560;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            WindowState = WindowState.Maximized;
            Background = theme.Surface;

            Content = BuildLayout(theme);
            _ = ReloadAsync();
        }

        private Control BuildLayout(AppTheme theme)
        {
            Grid grid = new Grid
            {
                Margin = new Thickness(20),
                RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,Auto,Auto,Auto,*")
            };

            AddRow(grid, 0, BuildHeader(theme));

            _KpiStrip.Margin = new Thickness(0, 14, 0, 6);
            AddRow(grid, 1, _KpiStrip);

            BuildMetricTabs(theme);
            AddRow(grid, 2, _MetricTabs);

            AddRow(grid, 3, _ChartHost);

            AddRow(grid, 4, BuildFilterBar(theme));

            _Status.Foreground = theme.Muted;
            _Status.Margin = new Thickness(2, 8, 0, 6);
            AddRow(grid, 5, _Status);

            BuildEventsHeader(theme);
            AddRow(grid, 6, new Border { BorderBrush = theme.Border, BorderThickness = new Thickness(0, 0, 0, 1), Child = _EventsHeader });

            ScrollViewer scroll = new ScrollViewer { Content = _EventsList, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto };
            AddRow(grid, 7, scroll);

            return grid;
        }

        private Control BuildFilterBar(AppTheme theme)
        {
            StackPanel bar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 10, 0, 0) };
            bar.Children.Add(new TextBlock { Text = "Filter", VerticalAlignment = VerticalAlignment.Center, Foreground = theme.Muted, FontSize = 12, FontWeight = FontWeight.SemiBold });

            _EndpointFilter.Tip("Show only calls whose endpoint name contains this text. Filters the table and recomputes the KPIs and charts above.");
            _EndpointFilter.TextChanged += (sender, args) => ApplyFilters(AppTheme.Current);
            bar.Children.Add(_EndpointFilter);

            _ConversationFilter.Tip("Show only calls whose conversation name contains this text. Filters the table and recomputes the KPIs and charts above.");
            _ConversationFilter.TextChanged += (sender, args) => ApplyFilters(AppTheme.Current);
            bar.Children.Add(_ConversationFilter);

            Button clear = new Button { Content = "Clear", Padding = new Thickness(10, 4, 10, 4) };
            clear.Tip("Clear both filters and show all loaded calls.");
            clear.Click += (sender, args) =>
            {
                _EndpointFilter.Text = string.Empty;
                _ConversationFilter.Text = string.Empty;
            };
            bar.Children.Add(clear);
            return bar;
        }

        private Control BuildHeader(AppTheme theme)
        {
            DockPanel header = new DockPanel();
            TextBlock title = new TextBlock { Text = "Usage", FontSize = 20, FontWeight = FontWeight.SemiBold, Foreground = theme.Text, VerticalAlignment = VerticalAlignment.Center };
            title.Tip("Token, cost, latency, and per-call telemetry for the selected time range.");
            DockPanel.SetDock(title, Dock.Left);
            header.Children.Add(title);

            StackPanel ranges = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, HorizontalAlignment = HorizontalAlignment.Right };
            ranges.Children.Add(RangeButton("Hour", UsageRange.Hour, theme).Tip("Show usage from the last hour."));
            ranges.Children.Add(RangeButton("Day", UsageRange.Day, theme).Tip("Show usage from the last day."));
            ranges.Children.Add(RangeButton("Week", UsageRange.Week, theme).Tip("Show usage from the last week."));
            ranges.Children.Add(RangeButton("Month", UsageRange.Month, theme).Tip("Show usage from the last month."));
            DockPanel.SetDock(ranges, Dock.Right);
            header.Children.Add(ranges);
            return header;
        }

        private Button RangeButton(string label, UsageRange range, AppTheme theme)
        {
            Button button = new Button { Content = label, BorderBrush = theme.Border, Padding = new Thickness(12, 6, 12, 6) };
            _RangeButtons[range] = button;
            ApplyTabStyle(button, range == _Range, theme);
            button.Click += (sender, args) =>
            {
                _Range = range;
                foreach (KeyValuePair<UsageRange, Button> entry in _RangeButtons)
                {
                    ApplyTabStyle(entry.Value, entry.Key == _Range, theme);
                }

                _ = ReloadAsync();
            };
            return button;
        }

        private void BuildMetricTabs(AppTheme theme)
        {
            _MetricTabs.Children.Clear();
            _MetricButtons.Clear();
            AddMetricTab("Tokens", ChartMetric.Tokens, theme, "Chart prompt/cached/output tokens per time bucket (stacked).");
            AddMetricTab("Cost", ChartMetric.Cost, theme, "Chart estimated US-dollar cost per time bucket.");
            AddMetricTab("Latency", ChartMetric.Latency, theme, "Chart total response time distribution (min–max, avg, p95, p99).");
            AddMetricTab("TTFT", ChartMetric.Ttft, theme, "Chart time-to-first-token distribution per time bucket.");
            AddMetricTab("Streaming", ChartMetric.Streaming, theme, "Chart streaming (generation) time distribution per time bucket.");
            AddMetricTab("Throughput", ChartMetric.Throughput, theme, "Chart output tokens-per-second distribution per time bucket.");
        }

        private void AddMetricTab(string label, ChartMetric metric, AppTheme theme, string tip)
        {
            Button button = new Button { Content = label, BorderBrush = theme.Border, Padding = new Thickness(12, 6, 12, 6) };
            button.Tip(tip);
            _MetricButtons[metric] = button;
            ApplyTabStyle(button, metric == _Metric, theme);
            button.Click += (sender, args) =>
            {
                _Metric = metric;
                foreach (KeyValuePair<ChartMetric, Button> entry in _MetricButtons)
                {
                    ApplyTabStyle(entry.Value, entry.Key == _Metric, theme);
                }

                _ChartHost.Child = BuildChart(theme);
            };
            _MetricTabs.Children.Add(button);
        }

        private static void ApplyTabStyle(Button button, bool active, AppTheme theme)
        {
            button.Background = active ? theme.AccentButton : Brushes.Transparent;
            button.Foreground = active ? theme.AccentText : theme.Text;
        }

        private void BuildEventsHeader(AppTheme theme)
        {
            _EventsHeader.Children.Clear();
            for (int i = 0; i < Headers.Length; i++)
            {
                int column = i;
                string caption = Headers[i];
                if (i == _SortColumn)
                {
                    caption += _SortDescending ? "  ▼" : "  ▲";
                }

                Button cell = new Button
                {
                    Content = new TextBlock { Text = caption, Foreground = theme.Muted, FontSize = 12, FontWeight = FontWeight.SemiBold },
                    Background = Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    Padding = new Thickness(4, 2, 4, 2),
                    HorizontalContentAlignment = HorizontalAlignment.Left
                };
                cell.Tip((i < HeaderTips.Length ? HeaderTips[i] : Headers[i]) + " Click to sort.");
                cell.Click += (sender, args) => SortBy(column, theme);
                Grid.SetColumn(cell, i);
                _EventsHeader.Children.Add(cell);
            }
        }

        private void SortBy(int column, AppTheme theme)
        {
            if (column == _SortColumn)
            {
                _SortDescending = !_SortDescending;
            }
            else
            {
                _SortColumn = column;
                _SortDescending = true;
            }

            BuildEventsHeader(theme);
            RenderEvents(theme);
        }

        private async Task ReloadAsync()
        {
            AppTheme theme = AppTheme.Current;

            if (!_Analytics.IsEnabled)
            {
                _Status.Text = "Usage telemetry is disabled. Enable it in Settings to see analytics.";
                _KpiStrip.Children.Clear();
                _ChartHost.Child = null;
                _EventsList.Children.Clear();
                _Series.Clear();
                _Events.Clear();
                _AllEvents.Clear();
                return;
            }

            try
            {
                // Fetch every call in the range, then filter and aggregate on the client so the table, KPIs,
                // and charts all reflect the endpoint/conversation filters consistently.
                UsageEventPage page = await _Analytics.GetEventsAsync(_Range, null, null, null, 1, MaxEvents, CancellationToken.None);
                _AllEvents.Clear();
                _AllEvents.AddRange(page.Items);
                _LoadTruncated = page.TotalCount > _AllEvents.Count;
                ApplyFilters(theme);
            }
            catch (Exception ex)
            {
                _Status.Text = "Could not load usage: " + ex.Message;
            }
        }

        private void ApplyFilters(AppTheme theme)
        {
            if (!_Analytics.IsEnabled)
            {
                return;
            }

            string endpointFilter = (_EndpointFilter.Text ?? string.Empty).Trim();
            string conversationFilter = (_ConversationFilter.Text ?? string.Empty).Trim();

            _Events.Clear();
            foreach (UsageEventRow row in _AllEvents)
            {
                if (endpointFilter.Length > 0 && (row.EndpointName ?? string.Empty).IndexOf(endpointFilter, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                if (conversationFilter.Length > 0 && ConversationLabel(row).IndexOf(conversationFilter, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                _Events.Add(row);
            }

            RenderKpis(ComputeMetrics(_Events), theme);

            _Series.Clear();
            _Series.AddRange(ComputeSeries(_Events));
            _ChartHost.Child = BuildChart(theme);

            RenderEvents(theme);

            bool filtered = endpointFilter.Length > 0 || conversationFilter.Length > 0;
            string scope = filtered ? " of " + _AllEvents.Count + " loaded" : string.Empty;
            string truncated = _LoadTruncated ? " (most recent " + _AllEvents.Count + " loaded)" : string.Empty;
            _Status.Text = "Showing " + _Events.Count + scope + " call" + (_Events.Count == 1 ? string.Empty : "s")
                + " in the last " + _Range.ToString().ToLowerInvariant() + (filtered ? ", filtered" : string.Empty) + "." + truncated;
        }

        private static UsageMetrics ComputeMetrics(List<UsageEventRow> events)
        {
            UsageMetrics metrics = new UsageMetrics { Calls = events.Count };
            long errors = 0;
            List<double> ttft = new List<double>();
            List<double> latency = new List<double>();
            List<double> streaming = new List<double>();
            List<double> throughput = new List<double>();

            foreach (UsageEventRow row in events)
            {
                if (!row.Success)
                {
                    errors++;
                }

                metrics.InputTokens += row.InputTokens;
                metrics.CachedTokens += row.CachedTokens;
                metrics.OutputTokens += row.OutputTokens;
                metrics.TotalTokens += row.TotalTokens;
                metrics.CostUsd += row.CostUsd;

                if (row.TimeToFirstTokenMs.HasValue)
                {
                    ttft.Add(row.TimeToFirstTokenMs.Value);
                }

                if (row.TotalMs.HasValue)
                {
                    latency.Add(row.TotalMs.Value);
                }

                if (row.StreamingMs.HasValue)
                {
                    streaming.Add(row.StreamingMs.Value);
                }

                if (row.TokensPerSecond.HasValue)
                {
                    throughput.Add(row.TokensPerSecond.Value);
                }
            }

            metrics.Errors = errors;
            metrics.ErrorRate = metrics.Calls > 0 ? (double)errors / metrics.Calls : 0.0;
            metrics.CacheHitRate = metrics.InputTokens > 0 ? (double)metrics.CachedTokens / metrics.InputTokens : 0.0;

            metrics.TtftMsDist = UsageDistribution.From(ttft, Percentile);
            metrics.TotalMsDist = UsageDistribution.From(latency, Percentile);
            metrics.StreamMsDist = UsageDistribution.From(streaming, Percentile);
            metrics.ThroughputDist = UsageDistribution.From(throughput, Percentile);

            metrics.AvgTtftMs = metrics.TtftMsDist.Avg;
            metrics.P95TtftMs = metrics.TtftMsDist.P95;
            metrics.P99TtftMs = metrics.TtftMsDist.P99;
            metrics.AvgTotalMs = metrics.TotalMsDist.Avg;
            metrics.P95TotalMs = metrics.TotalMsDist.P95;
            metrics.P99TotalMs = metrics.TotalMsDist.P99;
            metrics.AvgStreamMs = metrics.StreamMsDist.Avg;
            metrics.AvgTokensPerSec = metrics.ThroughputDist.Avg;
            return metrics;
        }

        private List<UsageBucket> ComputeSeries(List<UsageEventRow> events)
        {
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            UsageWindow window = UsageWindow.Compute(_Range, now);
            int count = Math.Max(1, window.BucketCount);

            List<List<UsageEventRow>> grouped = new List<List<UsageEventRow>>(count);
            for (int i = 0; i < count; i++)
            {
                grouped.Add(new List<UsageEventRow>());
            }

            foreach (UsageEventRow row in events)
            {
                long offset = row.TimestampUnixMs - window.FromUnixMs;
                int index = window.BucketMs > 0 ? (int)(offset / window.BucketMs) : 0;
                index = Math.Clamp(index, 0, count - 1);
                grouped[index].Add(row);
            }

            List<UsageBucket> series = new List<UsageBucket>(count);
            for (int i = 0; i < count; i++)
            {
                series.Add(new UsageBucket
                {
                    BucketStartUnixMs = window.FromUnixMs + (i * window.BucketMs),
                    Metrics = ComputeMetrics(grouped[i])
                });
            }

            return series;
        }

        private static double Percentile(List<double> sortedValues, double quantile)
        {
            if (sortedValues.Count == 0)
            {
                return 0.0;
            }

            int index = (int)Math.Round(quantile * (sortedValues.Count - 1));
            return sortedValues[Math.Clamp(index, 0, sortedValues.Count - 1)];
        }

        private void RenderKpis(UsageMetrics metrics, AppTheme theme)
        {
            _KpiStrip.Children.Clear();
            _KpiStrip.Children.Add(Kpi("Total tokens", metrics.TotalTokens.ToString("#,##0"), "Sum of prompt, cached, and output tokens across every call in this range.", theme));
            _KpiStrip.Children.Add(Kpi("Cost", "$" + metrics.CostUsd.ToString("0.0000"), "Estimated total US-dollar cost, computed from the pricing table.", theme));
            _KpiStrip.Children.Add(Kpi("Calls", metrics.Calls.ToString("#,##0"), "Number of model calls recorded in this range.", theme));
            _KpiStrip.Children.Add(Kpi("Error rate", (metrics.ErrorRate * 100).ToString("0.#") + "%", "Share of calls in this range that failed.", theme));
            _KpiStrip.Children.Add(Kpi("Avg TTFT", metrics.AvgTtftMs.ToString("#,##0") + " ms", "Average time to first token — how quickly responses start streaming.", theme));
            _KpiStrip.Children.Add(Kpi("Avg latency", metrics.AvgTotalMs.ToString("#,##0") + " ms", "Average total round-trip time per call.", theme));
        }

        private Control Kpi(string label, string value, string tip, AppTheme theme)
        {
            StackPanel content = new StackPanel { Spacing = 2 };
            content.Children.Add(new TextBlock { Text = label.ToUpperInvariant(), Foreground = theme.Muted, FontSize = 10, FontWeight = FontWeight.SemiBold });
            content.Children.Add(new TextBlock { Text = value, Foreground = theme.Text, FontSize = 18, FontWeight = FontWeight.SemiBold });

            return new Border
            {
                Background = theme.SurfaceAlt,
                BorderBrush = theme.Border,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(14, 10, 14, 10),
                Margin = new Thickness(0, 0, 10, 10),
                MinWidth = 130,
                Child = content
            }.Tip(tip);
        }

        private Control BuildChart(AppTheme theme)
        {
            if (_Series.Count == 0)
            {
                return new TextBlock { Text = "No activity in this range.", Foreground = theme.Muted, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            }

            double max = ComputeMax();
            if (max <= 0)
            {
                max = 1;
            }

            Grid plot = new Grid();
            for (int i = 0; i < _Series.Count; i++)
            {
                plot.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            }

            bool distribution = IsDistribution();
            IBrush boxColor = BoxColor(theme);

            for (int i = 0; i < _Series.Count; i++)
            {
                UsageMetrics m = _Series[i].Metrics;
                Control column;

                if (_Metric == ChartMetric.Tokens)
                {
                    StackPanel bars = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(1, 0, 1, 0) };
                    AddSegment(bars, m.OutputTokens, max, OutputColor);
                    AddSegment(bars, m.CachedTokens, max, CachedColor);
                    AddSegment(bars, Math.Max(0, m.InputTokens - m.CachedTokens), max, PromptColor);
                    column = bars;
                }
                else if (distribution)
                {
                    column = BuildCandlestick(DistFor(m), max, boxColor, theme);
                }
                else
                {
                    StackPanel bars = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(1, 0, 1, 0) };
                    AddSegment(bars, MetricValue(m), max, theme.Accent);
                    column = bars;
                }

                Border cell = new Border { Background = Brushes.Transparent, Child = column };
                ToolTip.SetTip(cell, BucketTooltip(_Series[i]));
                Grid.SetColumn(cell, i);
                plot.Children.Add(cell);
            }

            Grid chart = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("56,*"),
                RowDefinitions = new RowDefinitions(PlotHeight.ToString(System.Globalization.CultureInfo.InvariantCulture) + ",Auto")
            };

            Control yAxis = BuildYAxis(max, theme);
            Grid.SetRow(yAxis, 0);
            Grid.SetColumn(yAxis, 0);
            chart.Children.Add(yAxis);

            Panel plotArea = new Panel { Height = PlotHeight };
            plotArea.Children.Add(BuildGridlines(theme));
            plotArea.Children.Add(plot);
            Grid.SetRow(plotArea, 0);
            Grid.SetColumn(plotArea, 1);
            chart.Children.Add(plotArea);

            Control xAxis = BuildXAxis(theme);
            Grid.SetRow(xAxis, 1);
            Grid.SetColumn(xAxis, 1);
            chart.Children.Add(xAxis);

            Control? legend = BuildLegend(boxColor, theme);
            if (legend == null)
            {
                return chart;
            }

            DockPanel content = new DockPanel();
            DockPanel.SetDock(legend, Dock.Bottom);
            content.Children.Add(legend);
            content.Children.Add(chart);
            return content;
        }

        private Control? BuildLegend(IBrush boxColor, AppTheme theme)
        {
            StackPanel legend = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Margin = new Thickness(56, 6, 0, 0) };

            if (_Metric == ChartMetric.Tokens)
            {
                legend.Children.Add(LegendItem("Prompt", PromptColor, theme));
                legend.Children.Add(LegendItem("Cached", CachedColor, theme));
                legend.Children.Add(LegendItem("Output", OutputColor, theme));
                return legend;
            }

            if (IsDistribution())
            {
                legend.Children.Add(LegendItem("avg–p95", boxColor, theme));
                legend.Children.Add(LegendItem("min–max", theme.Muted, theme));
                legend.Children.Add(LegendItem("avg", theme.Text, theme));
                legend.Children.Add(LegendItem("p99", P99Color, theme));
                return legend;
            }

            return null;
        }

        private Control BuildCandlestick(UsageDistribution distribution, double max, IBrush boxColor, AppTheme theme)
        {
            Grid column = new Grid { Height = PlotHeight, Margin = new Thickness(1, 0, 1, 0) };
            if (distribution.Count == 0)
            {
                return column;
            }

            double minY = ToY(distribution.Min, max);
            double maxY = ToY(distribution.Max, max);
            double avgY = ToY(distribution.Avg, max);
            double p95Y = ToY(distribution.P95, max);
            double p99Y = ToY(distribution.P99, max);

            // min–max wick with end caps
            column.Children.Add(new Border { Width = 2, Height = Math.Max(1, maxY - minY), Background = theme.Muted, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, minY) });
            column.Children.Add(Cap(minY, theme.Muted));
            column.Children.Add(Cap(maxY, theme.Muted));

            // avg–p95 filled box
            column.Children.Add(new Border { Height = Math.Max(1, p95Y - avgY), Background = boxColor, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(4, 0, 4, avgY) });

            // avg line and p99 tick
            column.Children.Add(HLine(avgY, theme.Text));
            column.Children.Add(HLine(p99Y, P99Color));
            return column;
        }

        private static Border Cap(double y, IBrush brush)
        {
            return new Border { Width = 8, Height = 2, Background = brush, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, y) };
        }

        private static Border HLine(double y, IBrush brush)
        {
            return new Border { Height = 2, Background = brush, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(4, 0, 4, y) };
        }

        private static double ToY(double value, double max)
        {
            return value / max * PlotHeight;
        }

        private bool IsDistribution()
        {
            return _Metric == ChartMetric.Latency || _Metric == ChartMetric.Ttft || _Metric == ChartMetric.Streaming || _Metric == ChartMetric.Throughput;
        }

        private UsageDistribution DistFor(UsageMetrics m)
        {
            switch (_Metric)
            {
                case ChartMetric.Ttft:
                    return m.TtftMsDist;
                case ChartMetric.Streaming:
                    return m.StreamMsDist;
                case ChartMetric.Throughput:
                    return m.ThroughputDist;
                default:
                    return m.TotalMsDist;
            }
        }

        private IBrush BoxColor(AppTheme theme)
        {
            switch (_Metric)
            {
                case ChartMetric.Ttft:
                    return new SolidColorBrush(Color.Parse("#7c3aed"));
                case ChartMetric.Streaming:
                    return new SolidColorBrush(Color.Parse("#0891b2"));
                case ChartMetric.Throughput:
                    return new SolidColorBrush(Color.Parse("#16a34a"));
                case ChartMetric.Latency:
                    return new SolidColorBrush(Color.Parse("#2563eb"));
                default:
                    return theme.Accent;
            }
        }

        private string BucketTooltip(UsageBucket bucket)
        {
            string time = DateTimeOffset.FromUnixTimeMilliseconds(bucket.BucketStartUnixMs).LocalDateTime.ToString("MMM d  HH:mm");
            UsageMetrics m = bucket.Metrics;

            if (_Metric == ChartMetric.Tokens)
            {
                long prompt = Math.Max(0, m.InputTokens - m.CachedTokens);
                return time + "\nPrompt: " + prompt.ToString("#,##0")
                    + "\nCached: " + m.CachedTokens.ToString("#,##0")
                    + "\nOutput: " + m.OutputTokens.ToString("#,##0")
                    + "\nTotal: " + StackTotal(m).ToString("#,##0");
            }

            if (_Metric == ChartMetric.Cost)
            {
                return time + "\nCost: $" + m.CostUsd.ToString("0.0000") + "\nCalls: " + m.Calls.ToString("#,##0");
            }

            UsageDistribution d = DistFor(m);
            string unit = _Metric == ChartMetric.Throughput ? " tok/s" : " ms";
            string format = _Metric == ChartMetric.Throughput ? "0.#" : "#,##0";
            return time
                + "\nMax:  " + d.Max.ToString(format) + unit
                + "\np99:  " + d.P99.ToString(format) + unit
                + "\np95:  " + d.P95.ToString(format) + unit
                + "\nAvg:  " + d.Avg.ToString(format) + unit
                + "\nMin:  " + d.Min.ToString(format) + unit
                + "\nSamples: " + d.Count.ToString("#,##0");
        }

        private Control BuildGridlines(AppTheme theme)
        {
            Grid lines = new Grid { Height = PlotHeight };

            double[] fractions = { 0.25, 0.5, 0.75, 1.0 };
            foreach (double fraction in fractions)
            {
                lines.Children.Add(new Border
                {
                    Height = 1,
                    Background = theme.Border,
                    Opacity = 0.5,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Margin = new Thickness(0, 0, 0, fraction * PlotHeight)
                });
            }

            Grid vertical = new Grid();
            for (int k = 0; k < 6; k++)
            {
                vertical.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            }

            for (int k = 0; k < 5; k++)
            {
                Border line = new Border
                {
                    Width = 1,
                    Background = theme.Border,
                    Opacity = 0.4,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Stretch
                };
                Grid.SetColumn(line, k);
                vertical.Children.Add(line);
            }

            lines.Children.Add(vertical);
            return lines;
        }

        private Control BuildYAxis(double max, AppTheme theme)
        {
            Grid axis = new Grid { Height = PlotHeight, Width = 52 };
            axis.Children.Add(YLabel(FormatY(max), VerticalAlignment.Top, theme));
            axis.Children.Add(YLabel(FormatY(max / 2), VerticalAlignment.Center, theme));
            axis.Children.Add(YLabel(FormatY(0), VerticalAlignment.Bottom, theme));
            return axis;
        }

        private static TextBlock YLabel(string text, VerticalAlignment alignment, AppTheme theme)
        {
            return new TextBlock
            {
                Text = text,
                Foreground = theme.Muted,
                FontSize = 10,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = alignment,
                Margin = new Thickness(0, 0, 6, 0)
            };
        }

        private Control BuildXAxis(AppTheme theme)
        {
            Grid axis = new Grid { Margin = new Thickness(0, 4, 0, 0) };
            const int ticks = 6;
            for (int k = 0; k < ticks; k++)
            {
                axis.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            }

            int count = _Series.Count;
            for (int k = 0; k < ticks; k++)
            {
                int index = count <= 1 ? 0 : (int)Math.Round(k / (double)(ticks - 1) * (count - 1));
                long ts = _Series[Math.Clamp(index, 0, count - 1)].BucketStartUnixMs;

                TextBlock label = new TextBlock
                {
                    Text = FormatX(ts),
                    Foreground = theme.Muted,
                    FontSize = 10,
                    HorizontalAlignment = k == 0 ? HorizontalAlignment.Left : (k == ticks - 1 ? HorizontalAlignment.Right : HorizontalAlignment.Center)
                };
                Grid.SetColumn(label, k);
                axis.Children.Add(label);
            }

            return axis;
        }

        private void AddSegment(StackPanel column, double value, double max, IBrush color)
        {
            if (value <= 0)
            {
                return;
            }

            double height = Math.Max(1, value / max * PlotHeight);
            column.Children.Add(new Border { Height = height, Background = color });
        }

        private double ComputeMax()
        {
            bool distribution = IsDistribution();
            double max = 0;
            foreach (UsageBucket bucket in _Series)
            {
                double value;
                if (distribution)
                {
                    value = DistFor(bucket.Metrics).Max;
                }
                else if (_Metric == ChartMetric.Tokens)
                {
                    value = StackTotal(bucket.Metrics);
                }
                else
                {
                    value = MetricValue(bucket.Metrics);
                }

                if (value > max)
                {
                    max = value;
                }
            }

            return max;
        }

        private double MetricValue(UsageMetrics m)
        {
            switch (_Metric)
            {
                case ChartMetric.Cost:
                    return m.CostUsd;
                case ChartMetric.Latency:
                    return m.AvgTotalMs;
                case ChartMetric.Ttft:
                    return m.AvgTtftMs;
                case ChartMetric.Streaming:
                    return m.AvgStreamMs;
                case ChartMetric.Throughput:
                    return m.AvgTokensPerSec;
                default:
                    return StackTotal(m);
            }
        }

        private string FormatY(double value)
        {
            switch (_Metric)
            {
                case ChartMetric.Cost:
                    return "$" + value.ToString("0.00");
                case ChartMetric.Throughput:
                    return value.ToString("0.#");
                case ChartMetric.Latency:
                case ChartMetric.Ttft:
                case ChartMetric.Streaming:
                    return value.ToString("#,##0");
                default:
                    return FormatCount((long)Math.Round(value));
            }
        }

        private string FormatX(long timestampUnixMs)
        {
            DateTime local = DateTimeOffset.FromUnixTimeMilliseconds(timestampUnixMs).LocalDateTime;
            switch (_Range)
            {
                case UsageRange.Week:
                    return local.ToString("ddd HH:mm");
                case UsageRange.Month:
                    return local.ToString("MMM d");
                default:
                    return local.ToString("HH:mm");
            }
        }

        private static string FormatCount(long value)
        {
            if (value >= 1_000_000)
            {
                return (value / 1_000_000.0).ToString("0.#") + "M";
            }

            if (value >= 1_000)
            {
                return (value / 1_000.0).ToString("0.#") + "k";
            }

            return value.ToString("#,##0");
        }

        private Control LegendItem(string label, IBrush color, AppTheme theme)
        {
            StackPanel row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5 };
            row.Children.Add(new Border { Width = 10, Height = 10, Background = color, CornerRadius = new CornerRadius(2), VerticalAlignment = VerticalAlignment.Center });
            row.Children.Add(new TextBlock { Text = label, Foreground = theme.Muted, FontSize = 11, VerticalAlignment = VerticalAlignment.Center });
            return row;
        }

        private void RenderEvents(AppTheme theme)
        {
            SortEvents();
            _EventsList.Children.Clear();

            foreach (UsageEventRow row in _Events)
            {
                Grid line = new Grid { Margin = new Thickness(2, 3, 2, 3), ColumnDefinitions = new ColumnDefinitions(TableColumns) };
                AddDataCell(line, 0, DateTimeOffset.FromUnixTimeMilliseconds(row.TimestampUnixMs).LocalDateTime.ToString("MMM d HH:mm:ss"), theme);
                AddDataCell(line, 1, row.EndpointName, theme);
                AddDataCell(line, 2, ConversationLabel(row), theme);
                AddDataCell(line, 3, row.InputTokens.ToString("#,##0"), theme);
                AddDataCell(line, 4, row.OutputTokens.ToString("#,##0"), theme);
                AddDataCell(line, 5, row.TotalMs.HasValue ? row.TotalMs.Value.ToString("#,##0") + " ms" : "—", theme);
                AddDataCell(line, 6, "$" + row.CostUsd.ToString("0.0000"), theme);

                TextBlock status = new TextBlock
                {
                    Text = row.Success ? "ok" : (string.IsNullOrEmpty(row.ErrorCode) ? "error" : row.ErrorCode),
                    Foreground = row.Success ? theme.Success : theme.Error,
                    FontSize = 12,
                    Margin = new Thickness(4, 2, 4, 2)
                };
                status.Tip(row.Success ? "The call succeeded." : "The call failed" + (string.IsNullOrEmpty(row.ErrorCode) ? "." : ": " + row.ErrorCode));
                Grid.SetColumn(status, 7);
                line.Children.Add(status);

                _EventsList.Children.Add(line);
            }
        }

        private void SortEvents()
        {
            _Events.Sort((a, b) => CompareByColumn(a, b, _SortColumn));
            if (_SortDescending)
            {
                _Events.Reverse();
            }
        }

        private int CompareByColumn(UsageEventRow a, UsageEventRow b, int column)
        {
            switch (column)
            {
                case 0:
                    return a.TimestampUnixMs.CompareTo(b.TimestampUnixMs);
                case 1:
                    return string.Compare(a.EndpointName, b.EndpointName, StringComparison.OrdinalIgnoreCase);
                case 2:
                    return string.Compare(ConversationLabel(a), ConversationLabel(b), StringComparison.OrdinalIgnoreCase);
                case 3:
                    return a.InputTokens.CompareTo(b.InputTokens);
                case 4:
                    return a.OutputTokens.CompareTo(b.OutputTokens);
                case 5:
                    return (a.TotalMs ?? 0).CompareTo(b.TotalMs ?? 0);
                case 6:
                    return a.CostUsd.CompareTo(b.CostUsd);
                case 7:
                    return a.Success.CompareTo(b.Success);
                default:
                    return 0;
            }
        }

        private string ConversationLabel(UsageEventRow row)
        {
            string? resolved = _ConversationName?.Invoke(row.SessionId);
            if (!string.IsNullOrWhiteSpace(resolved))
            {
                return resolved!;
            }

            if (string.IsNullOrEmpty(row.SessionId))
            {
                return "—";
            }

            return row.SessionId!.Length > 8 ? row.SessionId!.Substring(0, 8) : row.SessionId!;
        }

        private static void AddDataCell(Grid grid, int column, string text, AppTheme theme)
        {
            TextBlock cell = new TextBlock
            {
                Text = text,
                Foreground = theme.Text,
                FontSize = 12,
                Margin = new Thickness(4, 2, 4, 2),
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            if (!string.IsNullOrEmpty(text))
            {
                cell.Tip(text);
            }

            Grid.SetColumn(cell, column);
            grid.Children.Add(cell);
        }

        private static long StackTotal(UsageMetrics metrics)
        {
            return Math.Max(0, metrics.InputTokens - metrics.CachedTokens) + metrics.CachedTokens + metrics.OutputTokens;
        }

        private static void AddRow(Grid grid, int row, Control child)
        {
            Grid.SetRow(child, row);
            grid.Children.Add(child);
        }
    }
}
