namespace Mux.Desktop.Views
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Avalonia;
    using Avalonia.Controls;
    using Avalonia.Input;
    using Avalonia.Layout;
    using Avalonia.Media;
    using Avalonia.Media.Imaging;
    using Avalonia.Platform.Storage;
    using Avalonia.Threading;
    using Mux.Core.Telemetry;
    using Mux.Desktop.I18n;
    using Mux.Desktop.Services;

    /// <summary>
    /// A usage/monitoring window mirroring the mux serve dashboard's Usage page: a range selector (hour through
    /// all), a KPI strip, chart tabs (calls with failure reasons, tokens, tokens by a dimension, cost, the
    /// latency/TTFT/streaming/throughput box plots, usage by a dimension, and a heatmap) with one shared
    /// dimension picker and one shared metric picker (tokens, calls, or cost) for the "&lt;Metric&gt; by" and
    /// "Usage by" charts, a chart toolbar (bars or lines for the stacked over-time charts, Export CSV, and Save
    /// PNG), and a sortable per-call table. All chart data comes from the shared usage query service through
    /// <see cref="IUsageAnalyticsService"/>; export shaping lives in <see cref="UsageChartExport"/>. Clicking a
    /// bucket in an over-time chart (bars or lines) narrows the call table to that bucket's window, shown by a
    /// clearable indicator. Only the call table scrolls.
    /// </summary>
    public sealed class UsageDashboardWindow : Window
    {
        private const string TableColumns = "150,1.2*,1.6*,66,66,86,86,72";

        // Rows loaded into the call table (the store clamps pages at 500); the status line shows the total.
        private const int TablePageSize = 500;
        private const double PlotHeight = 270;
        private static readonly IBrush PromptColor = new SolidColorBrush(Color.Parse("#4c8bf5"));
        private static readonly IBrush CachedColor = new SolidColorBrush(Color.Parse("#3fb950"));
        private static readonly IBrush OutputColor = new SolidColorBrush(Color.Parse("#d29922"));
        private static readonly IBrush P99Color = new SolidColorBrush(Color.Parse("#dc2626"));
        private static readonly Cursor HandCursor = new Cursor(StandardCursorType.Hand);
        private static readonly IBrush OtherColor = new SolidColorBrush(Color.Parse("#8b949e"));

        // Stable "Tokens by" series colors, by rank; "Other" is always OtherColor.
        private static readonly IBrush[] SeriesPalette =
        {
            new SolidColorBrush(Color.Parse("#4c8bf5")),
            new SolidColorBrush(Color.Parse("#3fb950")),
            new SolidColorBrush(Color.Parse("#d29922")),
            new SolidColorBrush(Color.Parse("#a371f7")),
            new SolidColorBrush(Color.Parse("#0891b2")),
            new SolidColorBrush(Color.Parse("#db61a2"))
        };

        // Resolved per render (not cached) so a live locale change is reflected on the next open.
        private static string[] Headers()
        {
            return new[]
            {
                Localizer.T("usage.col.when"),
                Localizer.T("usage.col.endpoint"),
                Localizer.T("usage.col.conversation"),
                Localizer.T("usage.col.in"),
                Localizer.T("usage.col.out"),
                Localizer.T("usage.col.latency"),
                Localizer.T("usage.col.cost"),
                Localizer.T("usage.col.status")
            };
        }

        private static string[] HeaderTips()
        {
            return new[]
            {
                Localizer.T("usage.col.when.tip"),
                Localizer.T("usage.col.endpoint.tip"),
                Localizer.T("usage.col.conversation.tip"),
                Localizer.T("usage.col.in.tip"),
                Localizer.T("usage.col.out.tip"),
                Localizer.T("usage.col.latency.tip"),
                Localizer.T("usage.col.cost.tip"),
                Localizer.T("usage.col.status.tip")
            };
        }

        private readonly IUsageAnalyticsService _Analytics;
        private readonly WrapPanel _KpiStrip = new WrapPanel { Orientation = Orientation.Horizontal };
        private readonly WrapPanel _MetricTabs = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 18) };
        private readonly Border _ChartHost = new Border { Height = 340, Margin = new Thickness(0, 0, 0, 8) };
        private readonly Grid _EventsHeader = new Grid { ColumnDefinitions = new ColumnDefinitions(TableColumns), Margin = new Thickness(2, 8, 2, 4) };
        private readonly StackPanel _EventsList = new StackPanel { Spacing = 0 };
        private readonly TextBlock _Status = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap };
        private readonly Dictionary<UsageRange, Button> _RangeButtons = new Dictionary<UsageRange, Button>();
        private readonly Dictionary<ChartMetric, Button> _MetricButtons = new Dictionary<ChartMetric, Button>();
        private readonly List<UsageEventRow> _Events = new List<UsageEventRow>();
        private readonly TextBox _EndpointFilter = new TextBox { Width = 210, PlaceholderText = Localizer.T("usage.endpointFilter.placeholder") };
        private readonly TextBox _ConversationFilter = new TextBox { Width = 230, PlaceholderText = Localizer.T("usage.conversationFilter.placeholder") };
        private readonly ComboBox _DimensionPicker = new ComboBox { MinWidth = 130, VerticalAlignment = VerticalAlignment.Center };
        private readonly ComboBox _SplitMetricPicker = new ComboBox { MinWidth = 110, VerticalAlignment = VerticalAlignment.Center };
        private readonly StackPanel _StyleToggle = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 0, VerticalAlignment = VerticalAlignment.Center };
        private readonly Button _BarsButton = new Button { Padding = new Thickness(10, 3, 10, 3) };
        private readonly Button _LinesButton = new Button { Padding = new Thickness(10, 3, 10, 3) };
        private readonly Button _ExportCsvButton = new Button { Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(0, 0, 6, 0) };
        private readonly Button _SavePngButton = new Button { Padding = new Thickness(10, 3, 10, 3) };
        private readonly TextBlock _ExportStatus = new TextBlock { FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
        private readonly Border _DrillChip = new Border { IsVisible = false, CornerRadius = new CornerRadius(12), Padding = new Thickness(10, 2, 4, 2), VerticalAlignment = VerticalAlignment.Center };
        private readonly TextBlock _DrillText = new TextBlock { FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
        private readonly DispatcherTimer _FilterTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        private readonly IReadOnlyDictionary<string, string> _Conversations;
        private readonly Func<string?, string?>? _ConversationName;

        private UsageRange _Range = UsageRange.Day;
        private ChartMetric _Metric = ChartMetric.Calls;
        private string _Dimension = "model";
        private string _SplitMetric = UsageCsv.MetricTokens;
        private bool _Lines;
        private UsageDashboardData? _Data;
        private UsageScope _Scope = UsageScope.Any;
        private List<string> _KnownEndpoints = new List<string>();
        private long _EventsTotal;
        private long? _DrillStart;
        private long _DrillWidth;
        private int _LoadVersion;
        private int _EventsVersion;
        private int _SortColumn;
        private bool _SortDescending = true;

        /// <summary>
        /// Instantiate the usage window.
        /// </summary>
        /// <param name="analytics">The usage analytics service. Required.</param>
        /// <param name="conversationNameResolver">Optional map from a session id to a conversation title.</param>
        /// <param name="conversations">Optional known conversations (session id to title), used to resolve the
        /// conversation filter into session ids for the shared queries.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="analytics"/> is null.</exception>
        public UsageDashboardWindow(IUsageAnalyticsService analytics, Func<string?, string?>? conversationNameResolver = null, IReadOnlyDictionary<string, string>? conversations = null)
        {
            ArgumentNullException.ThrowIfNull(analytics);
            _Analytics = analytics;
            _ConversationName = conversationNameResolver;
            _Conversations = conversations ?? new Dictionary<string, string>();

            AppTheme theme = AppTheme.Current;

            Title = Localizer.T("usage.title");
            Icon = IconResources.LoadWindowIcon();
            Width = 940;
            Height = 720;
            MinWidth = 720;
            MinHeight = 560;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            WindowState = WindowState.Maximized;
            Background = theme.Surface;

            _FilterTimer.Tick += (sender, args) =>
            {
                _FilterTimer.Stop();
                _ = ReloadAsync();
            };
            Closed += (sender, args) => _FilterTimer.Stop();

            Content = BuildLayout(theme);
            _ = ReloadAsync();
        }

        private List<UsageBucket> Buckets
        {
            get => _Data?.Timeseries ?? new List<UsageBucket>();
        }

        private long BucketWidth
        {
            get => UsageChartShaping.BucketWidthMs(Buckets, _Data?.Window);
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

            // The chart toolbar sits directly above the chart it acts on.
            _ChartHost.Background = theme.Surface;
            StackPanel chartArea = new StackPanel { Spacing = 6 };
            chartArea.Children.Add(BuildChartToolbar(theme));
            chartArea.Children.Add(_ChartHost);
            AddRow(grid, 3, chartArea);

            AddRow(grid, 4, BuildFilterBar(theme));

            _Status.Foreground = theme.Muted;
            _Status.Margin = new Thickness(2, 8, 0, 6);
            AddRow(grid, 5, _Status);

            BuildEventsHeader(theme);
            AddRow(grid, 6, new Border { BorderBrush = theme.Border, BorderThickness = new Thickness(0, 0, 0, 1), Child = _EventsHeader });

            ScrollViewer scroll = new ScrollViewer { Classes = { Mux.Desktop.Styling.MuxThemeStyles.GutterClass }, Content = _EventsList, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto };
            AddRow(grid, 7, scroll);

            return grid;
        }

        private Control BuildFilterBar(AppTheme theme)
        {
            WrapPanel bar = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
            bar.Children.Add(new TextBlock { Text = Localizer.T("usage.filter"), VerticalAlignment = VerticalAlignment.Center, Foreground = theme.Muted, FontSize = 12, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 0, 8, 0) });

            _EndpointFilter.Tip(Localizer.T("usage.endpointFilter.tip"));
            _EndpointFilter.Margin = new Thickness(0, 0, 8, 0);
            _EndpointFilter.TextChanged += (sender, args) => ScheduleReload();
            bar.Children.Add(_EndpointFilter);

            _ConversationFilter.Tip(Localizer.T("usage.conversationFilter.tip"));
            _ConversationFilter.Margin = new Thickness(0, 0, 8, 0);
            _ConversationFilter.TextChanged += (sender, args) => ScheduleReload();
            bar.Children.Add(_ConversationFilter);

            Button clear = new Button { Content = Localizer.T("usage.clear"), Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 0, 12, 0) };
            clear.Tip(Localizer.T("usage.clear.tip"));
            clear.Click += (sender, args) =>
            {
                _EndpointFilter.Text = string.Empty;
                _ConversationFilter.Text = string.Empty;
            };
            bar.Children.Add(clear);

            // Drill-down indicator: "Calls <bucket window> ×", visible while the table is narrowed to one bucket.
            _DrillText.Foreground = theme.AccentText;
            _DrillChip.Background = theme.AccentButton;
            Button close = new Button
            {
                Content = new TextBlock { Text = "×", Foreground = theme.AccentText, FontSize = 13, FontWeight = FontWeight.SemiBold },
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(6, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            close.Tip(Localizer.T("usage.drill.clear.tip"));
            close.Click += (sender, args) => ClearDrill();
            StackPanel chip = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
            chip.Children.Add(_DrillText);
            chip.Children.Add(close);
            _DrillChip.Child = chip;
            bar.Children.Add(_DrillChip);
            return bar;
        }

        private Control BuildHeader(AppTheme theme)
        {
            DockPanel header = new DockPanel();
            TextBlock title = new TextBlock { Text = Localizer.T("usage.title"), FontSize = 20, FontWeight = FontWeight.SemiBold, Foreground = theme.Text, VerticalAlignment = VerticalAlignment.Center };
            title.Tip(Localizer.T("usage.title.tip"));
            DockPanel.SetDock(title, Dock.Left);
            header.Children.Add(title);

            StackPanel ranges = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, HorizontalAlignment = HorizontalAlignment.Right };
            foreach (UsageRange range in UsageChartShaping.Ranges)
            {
                ranges.Children.Add(RangeButton(Localizer.T(UsageChartShaping.RangeLabelKey(range)), range, theme).Tip(Localizer.T(UsageChartShaping.RangeTipKey(range))));
            }

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

                // A bucket from the old grid has no meaning in the new one.
                SetDrill(null, 0);
                _ = ReloadAsync();
            };
            return button;
        }

        private void BuildMetricTabs(AppTheme theme)
        {
            AddMetricTab(Localizer.T("usage.metric.calls"), ChartMetric.Calls, theme, Localizer.T("usage.metric.calls.tip"));
            AddMetricTab(Localizer.T("usage.metric.tokens"), ChartMetric.Tokens, theme, Localizer.T("usage.metric.tokens.tip"));
            AddMetricTab(SplitLabel(), ChartMetric.TokensBy, theme, Localizer.T("usage.metric.tokensBy.tip"));
            AddMetricTab(Localizer.T("usage.metric.cost"), ChartMetric.Cost, theme, Localizer.T("usage.metric.cost.tip"));
            AddMetricTab(Localizer.T("usage.metric.latency"), ChartMetric.Latency, theme, Localizer.T("usage.metric.latency.tip"));
            AddMetricTab(Localizer.T("usage.metric.ttft"), ChartMetric.Ttft, theme, Localizer.T("usage.metric.ttft.tip"));
            AddMetricTab(Localizer.T("usage.metric.streaming"), ChartMetric.Streaming, theme, Localizer.T("usage.metric.streaming.tip"));
            AddMetricTab(Localizer.T("usage.metric.throughput"), ChartMetric.Throughput, theme, Localizer.T("usage.metric.throughput.tip"));
            AddMetricTab(ByLabel("usage.metric.usageBy"), ChartMetric.UsageBy, theme, Localizer.T("usage.metric.usageBy.tip"));
            AddMetricTab(Localizer.T("usage.metric.heatmap"), ChartMetric.Heatmap, theme, Localizer.T("usage.metric.heatmap.tip"));

            // One dimension picker shared by "Tokens by" and "Usage by".
            foreach (string dimension in UsageChartShaping.Dimensions)
            {
                _DimensionPicker.Items.Add(new ComboBoxItem { Content = Localizer.T(UsageChartShaping.DimensionLabelKey(dimension)), Tag = dimension });
            }

            _DimensionPicker.SelectedIndex = 0;
            _DimensionPicker.Tip(Localizer.T("usage.dim.tip"));
            _DimensionPicker.SelectionChanged += (sender, args) =>
            {
                if (_DimensionPicker.SelectedItem is ComboBoxItem item && item.Tag is string dimension && dimension != _Dimension)
                {
                    _Dimension = dimension;
                    _MetricButtons[ChartMetric.TokensBy].Content = SplitLabel();
                    _MetricButtons[ChartMetric.UsageBy].Content = ByLabel("usage.metric.usageBy");
                    _ = ReloadAsync();
                }
            };

            // One metric picker shared by "<Metric> by" and "Usage by" (which ranks by it). The split series and
            // breakdown rows already carry tokens, calls, and cost, so a change only redraws.
            foreach (string metric in UsageChartShaping.SplitMetrics)
            {
                _SplitMetricPicker.Items.Add(new ComboBoxItem { Content = Localizer.T(UsageChartShaping.SplitMetricLabelKey(metric)), Tag = metric });
            }

            _SplitMetricPicker.SelectedIndex = 0;
            _SplitMetricPicker.Tip(Localizer.T("usage.splitMetric.tip"));
            _SplitMetricPicker.SelectionChanged += (sender, args) =>
            {
                if (_SplitMetricPicker.SelectedItem is ComboBoxItem item && item.Tag is string metric && metric != _SplitMetric)
                {
                    _SplitMetric = metric;
                    _MetricButtons[ChartMetric.TokensBy].Content = SplitLabel();
                    _ChartHost.Child = BuildChart(AppTheme.Current);
                }
            };

            StackPanel picker = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(12, 0, 0, 6), VerticalAlignment = VerticalAlignment.Center };
            picker.Children.Add(new TextBlock { Text = Localizer.T("usage.splitMetric.label"), Foreground = theme.Muted, FontSize = 12, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center });
            picker.Children.Add(_SplitMetricPicker);
            picker.Children.Add(new TextBlock { Text = Localizer.T("usage.dim.label"), Foreground = theme.Muted, FontSize = 12, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) });
            picker.Children.Add(_DimensionPicker);
            _MetricTabs.Children.Add(picker);
        }

        // "<Metric> by <dimension>", for example "Calls by Call kind".
        private string SplitLabel()
        {
            return string.Format(
                CultureInfo.CurrentCulture,
                Localizer.T("usage.metric.metricBy"),
                Localizer.T(UsageChartShaping.SplitMetricLabelKey(_SplitMetric)),
                Localizer.T(UsageChartShaping.DimensionLabelKey(_Dimension)));
        }

        // Bars/Lines on the left (stacked over-time charts only); Export CSV and Save PNG on the right with the last
        // export's result beside them.
        private Control BuildChartToolbar(AppTheme theme)
        {
            _BarsButton.Content = Localizer.T("usage.chart.bars");
            _BarsButton.Tip(Localizer.T("usage.chart.bars.tip"));
            _BarsButton.BorderBrush = theme.Border;
            _BarsButton.CornerRadius = new CornerRadius(6, 0, 0, 6);
            _BarsButton.Click += (sender, args) => SetLines(false);
            _LinesButton.Content = Localizer.T("usage.chart.lines");
            _LinesButton.Tip(Localizer.T("usage.chart.lines.tip"));
            _LinesButton.BorderBrush = theme.Border;
            _LinesButton.CornerRadius = new CornerRadius(0, 6, 6, 0);
            _LinesButton.Click += (sender, args) => SetLines(true);
            _StyleToggle.Children.Add(_BarsButton);
            _StyleToggle.Children.Add(_LinesButton);

            _ExportCsvButton.Content = Localizer.T("usage.export.csv");
            _ExportCsvButton.Tip(Localizer.T("usage.export.csv.tip"));
            _ExportCsvButton.Click += (sender, args) => _ = ExportCsvAsync();
            _SavePngButton.Content = Localizer.T("usage.export.png");
            _SavePngButton.Tip(Localizer.T("usage.export.png.tip"));
            _SavePngButton.Click += (sender, args) => _ = SavePngAsync();
            _ExportStatus.Foreground = theme.Muted;

            StackPanel actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            actions.Children.Add(_ExportStatus);
            actions.Children.Add(new Border { Width = 10 });
            actions.Children.Add(_ExportCsvButton);
            actions.Children.Add(_SavePngButton);

            DockPanel bar = new DockPanel { LastChildFill = false };
            DockPanel.SetDock(_StyleToggle, Dock.Left);
            bar.Children.Add(_StyleToggle);
            DockPanel.SetDock(actions, Dock.Right);
            bar.Children.Add(actions);
            UpdateToolbar(theme);
            return bar;
        }

        private void SetLines(bool lines)
        {
            if (_Lines == lines)
            {
                return;
            }

            _Lines = lines;
            _ChartHost.Child = BuildChart(AppTheme.Current);
        }

        // Shows the bars/lines toggle on the stacked charts and enables export only when the chart has data.
        private void UpdateToolbar(AppTheme theme)
        {
            string kind = ChartKind(_Metric);
            _StyleToggle.IsVisible = UsageChartExport.IsStacked(kind);
            ApplyTabStyle(_BarsButton, !_Lines, theme);
            ApplyTabStyle(_LinesButton, _Lines, theme);
            bool exportable = _Analytics.IsEnabled && UsageChartExport.HasData(_Data, kind);
            _ExportCsvButton.IsEnabled = exportable;
            _SavePngButton.IsEnabled = exportable;
        }

        private static string ChartKind(ChartMetric metric)
        {
            switch (metric)
            {
                case ChartMetric.Tokens: return UsageChartExport.Tokens;
                case ChartMetric.TokensBy: return UsageChartExport.Split;
                case ChartMetric.Cost: return UsageChartExport.Cost;
                case ChartMetric.Latency: return UsageChartExport.Latency;
                case ChartMetric.Ttft: return UsageChartExport.Ttft;
                case ChartMetric.Streaming: return UsageChartExport.Streaming;
                case ChartMetric.Throughput: return UsageChartExport.Throughput;
                case ChartMetric.UsageBy: return UsageChartExport.UsageBy;
                case ChartMetric.Heatmap: return UsageChartExport.Heatmap;
                default: return UsageChartExport.Calls;
            }
        }

        private async Task ExportCsvAsync()
        {
            string kind = ChartKind(_Metric);
            string? csv = UsageChartExport.BuildCsv(_Data, kind, _SplitMetric);
            if (csv == null)
            {
                return;
            }

            byte[] bytes = new UTF8Encoding(false).GetBytes(csv);
            await SaveAsync(kind, "csv", Localizer.T("usage.export.csvType"), "text/csv", "public.comma-separated-values-text", stream => stream.WriteAsync(bytes, 0, bytes.Length));
        }

        // Renders the chart area (with its background) at twice its size, so the image stays sharp on any display.
        private async Task SavePngAsync()
        {
            string kind = ChartKind(_Metric);
            if (!UsageChartExport.HasData(_Data, kind) || _ChartHost.Bounds.Width <= 0 || _ChartHost.Bounds.Height <= 0)
            {
                return;
            }

            byte[] png;
            try
            {
                PixelSize size = new PixelSize((int)Math.Ceiling(_ChartHost.Bounds.Width * 2), (int)Math.Ceiling(_ChartHost.Bounds.Height * 2));
                using RenderTargetBitmap bitmap = new RenderTargetBitmap(size, new Vector(192, 192));
                bitmap.Render(_ChartHost);
                using MemoryStream buffer = new MemoryStream();
                bitmap.Save(buffer, PngBitmapEncoderOptions.Default);
                png = buffer.ToArray();
            }
            catch (Exception ex)
            {
                _ExportStatus.Text = Format("usage.export.failed", ex.Message);
                return;
            }

            await SaveAsync(kind, "png", Localizer.T("usage.export.pngType"), "image/png", "public.png", stream => stream.WriteAsync(png, 0, png.Length));
        }

        private async Task SaveAsync(string kind, string extension, string typeName, string mimeType, string appleType, Func<Stream, Task> write)
        {
            try
            {
                FilePickerFileType type = new FilePickerFileType(typeName)
                {
                    Patterns = new[] { "*." + extension },
                    MimeTypes = new[] { mimeType },
                    AppleUniformTypeIdentifiers = new[] { appleType }
                };
                IStorageFile? file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = Localizer.T(extension == "png" ? "usage.export.png" : "usage.export.csv"),
                    SuggestedFileName = UsageChartExport.FileName(kind, _SplitMetric, _Dimension, _Range, DateTime.Now, extension),
                    DefaultExtension = extension,
                    FileTypeChoices = new[] { type },
                    ShowOverwritePrompt = true
                });
                if (file == null)
                {
                    return;
                }

                await using (Stream stream = await file.OpenWriteAsync())
                {
                    await write(stream);
                }

                _ExportStatus.Text = Format("usage.export.saved", file.TryGetLocalPath() ?? file.Name);
                _ExportStatus.Tip(_ExportStatus.Text);
            }
            catch (Exception ex)
            {
                _ExportStatus.Text = Format("usage.export.failed", ex.Message);
            }
        }

        private string ByLabel(string key)
        {
            return string.Format(CultureInfo.CurrentCulture, Localizer.T(key), Localizer.T(UsageChartShaping.DimensionLabelKey(_Dimension)));
        }

        private void AddMetricTab(string label, ChartMetric metric, AppTheme theme, string tip)
        {
            Button button = new Button { Content = label, BorderBrush = theme.Border, Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(0, 0, 6, 6) };
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
            string[] headers = Headers();
            string[] headerTips = HeaderTips();
            for (int i = 0; i < headers.Length; i++)
            {
                int column = i;
                string caption = headers[i];
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
                cell.Tip((i < headerTips.Length ? headerTips[i] : headers[i]) + " " + Localizer.T("usage.sortHint"));
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

        private void ScheduleReload()
        {
            _FilterTimer.Stop();
            _FilterTimer.Start();
        }

        private bool IsFiltered()
        {
            return (_EndpointFilter.Text ?? string.Empty).Trim().Length > 0 || (_ConversationFilter.Text ?? string.Empty).Trim().Length > 0;
        }

        private async Task ReloadAsync()
        {
            AppTheme theme = AppTheme.Current;
            int version = ++_LoadVersion;

            if (!_Analytics.IsEnabled)
            {
                _Status.Text = Localizer.T("usage.disabled");
                _KpiStrip.Children.Clear();
                _ChartHost.Child = null;
                UpdateToolbar(theme);
                _EventsList.Children.Clear();
                _Events.Clear();
                _Data = null;
                return;
            }

            try
            {
                // The text filters are resolved into exact endpoint names and session ids, so every chart and KPI
                // comes from the shared query service (see UsageScope and UsageAnalyticsService).
                List<string> endpoints = await _Analytics.GetEndpointsAsync(CancellationToken.None);
                if (version != _LoadVersion)
                {
                    return;
                }

                _KnownEndpoints = endpoints;
                UsageScope scope = new UsageScope(
                    UsageScope.MatchEndpoints(_EndpointFilter.Text, _KnownEndpoints),
                    UsageScope.MatchSessions(_ConversationFilter.Text, _Conversations));

                UsageDashboardData data = await _Analytics.LoadDashboardAsync(_Range, scope, _Dimension, CancellationToken.None);
                if (version != _LoadVersion)
                {
                    return;
                }

                _Data = data;
                _Scope = scope;
                RenderKpis(data.Summary.Metrics, theme);
                _ChartHost.Child = BuildChart(theme);
                await ReloadEventsAsync();
            }
            catch (Exception ex)
            {
                if (version == _LoadVersion)
                {
                    _Status.Text = Localizer.T("usage.loadError") + ex.Message;
                }
            }
        }

        private async Task ReloadEventsAsync()
        {
            if (_Data == null)
            {
                return;
            }

            int version = ++_EventsVersion;
            AppTheme theme = AppTheme.Current;
            long from;
            long to;
            if (_DrillStart.HasValue)
            {
                (from, to) = UsageChartShaping.DrillWindow(_DrillStart.Value, _DrillWidth);
            }
            else
            {
                from = _Data.Window.FromUnixMs;
                to = _Data.Window.ToUnixMs - 1;
            }

            try
            {
                UsageEventPage page = await _Analytics.GetEventsInWindowAsync(from, to, _Scope, TablePageSize, CancellationToken.None);
                if (version != _EventsVersion)
                {
                    return;
                }

                _Events.Clear();
                _Events.AddRange(page.Items);
                _EventsTotal = page.TotalCount;
                RenderEvents(theme);
                UpdateStatus();
            }
            catch (Exception ex)
            {
                if (version == _EventsVersion)
                {
                    _Status.Text = Localizer.T("usage.loadError") + ex.Message;
                }
            }
        }

        private void UpdateStatus()
        {
            string shown = _Events.Count.ToString("#,##0", CultureInfo.CurrentCulture);
            string total = _EventsTotal.ToString("#,##0", CultureInfo.CurrentCulture);
            string text;
            if (_DrillStart.HasValue)
            {
                text = Format("usage.status.bucket", shown, total, UsageChartShaping.FormatBucketWindow(_DrillStart.Value, _DrillWidth));
            }
            else if (_Range == UsageRange.All)
            {
                text = Format("usage.status.rangeAll", shown, total);
            }
            else
            {
                text = Format("usage.status.range", shown, total, Localizer.T(UsageChartShaping.RangeLabelKey(_Range)));
            }

            if (IsFiltered())
            {
                text += " " + Localizer.T("usage.status.filteredNote");
            }

            if (_Scope.Endpoints != null && _Scope.Endpoints.Count > 1)
            {
                text += " " + Localizer.T("usage.status.approx");
            }

            _Status.Text = text;
        }

        private static string Format(string key, params object[] args)
        {
            return string.Format(CultureInfo.CurrentCulture, Localizer.T(key), args);
        }

        private void SelectBucket(long bucketStartUnixMs, long? widthMs = null)
        {
            long width = widthMs ?? BucketWidth;
            if (_DrillStart == bucketStartUnixMs)
            {
                ClearDrill();
                return;
            }

            SetDrill(bucketStartUnixMs, width);
            _ChartHost.Child = BuildChart(AppTheme.Current);
            _ = ReloadEventsAsync();
        }

        private void ClearDrill()
        {
            if (!_DrillStart.HasValue)
            {
                return;
            }

            SetDrill(null, 0);
            _ChartHost.Child = BuildChart(AppTheme.Current);
            _ = ReloadEventsAsync();
        }

        private void SetDrill(long? start, long width)
        {
            _DrillStart = start;
            _DrillWidth = width;
            _DrillChip.IsVisible = start.HasValue;
            _DrillText.Text = start.HasValue ? Format("usage.drill.chip", UsageChartShaping.FormatBucketWindow(start.Value, width)) : string.Empty;
        }

        private void RenderKpis(UsageMetrics metrics, AppTheme theme)
        {
            _KpiStrip.Children.Clear();
            _KpiStrip.Children.Add(Kpi(Localizer.T("usage.kpi.totalTokens"), metrics.TotalTokens.ToString("#,##0"), Localizer.T("usage.kpi.totalTokens.tip"), theme));
            _KpiStrip.Children.Add(Kpi(Localizer.T("usage.kpi.cost"), "$" + metrics.CostUsd.ToString("0.0000"), Localizer.T("usage.kpi.cost.tip"), theme));
            _KpiStrip.Children.Add(Kpi(Localizer.T("usage.kpi.calls"), metrics.Calls.ToString("#,##0"), Localizer.T("usage.kpi.calls.tip"), theme));
            _KpiStrip.Children.Add(Kpi(Localizer.T("usage.kpi.errorRate"), (metrics.ErrorRate * 100).ToString("0.#") + "%", Localizer.T("usage.kpi.errorRate.tip"), theme));
            _KpiStrip.Children.Add(Kpi(Localizer.T("usage.kpi.avgTtft"), metrics.AvgTtftMs.ToString("#,##0") + " ms", Localizer.T("usage.kpi.avgTtft.tip"), theme));
            _KpiStrip.Children.Add(Kpi(Localizer.T("usage.kpi.avgLatency"), metrics.AvgTotalMs.ToString("#,##0") + " ms", Localizer.T("usage.kpi.avgLatency.tip"), theme));
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

        // The token heatmap on the shared clock-aligned grid: time runs left to right, each column one local period
        // (labeled along the top), each row one step within it (labeled on the left); the month is a calendar of
        // half-day columns and week rows. Columns stretch to the window; an axis caption names both axes.
        private Control BuildTokenHeatmap(AppTheme theme)
        {
            UsageHeatmap heatmap = _Data!.Heatmap!;
            UsageHeatmapLayout layout = heatmap.Layout;
            CultureInfo culture = CultureInfo.CurrentCulture;
            StackPanel host = new StackPanel { Spacing = 8 };
            host.Children.Add(new TextBlock { Text = Localizer.T("usage.heatmap.title"), Foreground = theme.Text, FontSize = 14 });
            host.Children.Add(new TextBlock { Text = UsageChartShaping.HeatmapCaption(layout, Localizer.T), Foreground = theme.Muted, FontSize = 12 });

            double rowHeight = UsageChartShaping.HeatmapRowHeight(layout.Rows, layout.Columns);
            Grid grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            for (int c = 0; c < layout.Columns; c++)
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            }

            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            for (int r = 0; r < layout.Rows; r++)
            {
                grid.RowDefinitions.Add(new RowDefinition(new GridLength(rowHeight, GridUnitType.Pixel)));
            }

            // Column labels along the x-axis, thinned to the width the grid gets so they never collide, each over the
            // columns it names.
            List<string> columnLabels = Enumerable.Range(0, layout.Columns).Select(c => layout.FormatColumnLabel(c, culture)).ToList();
            HeatmapXAxis axis = new HeatmapXAxis(layout, columnLabels, theme.Muted) { Height = 14, Margin = new Thickness(0, 0, 0, 2) };
            Grid.SetRow(axis, 0);
            Grid.SetColumn(axis, 1);
            Grid.SetColumnSpan(axis, layout.Columns);
            grid.Children.Add(axis);

            foreach (int r in UsageChartShaping.HeatmapLabeledRows(layout.Rows, rowHeight))
            {
                TextBlock label = new TextBlock { Text = layout.FormatRowLabel(r, culture), Foreground = theme.Muted, FontSize = 11, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
                Grid.SetRow(label, r + 1);
                Grid.SetColumn(label, 0);
                grid.Children.Add(label);
            }

            long max = heatmap.TotalTokens.Length > 0 ? heatmap.TotalTokens.Max() : 0;

            // Shades by quartile of the used cells; an empty cell is a neutral grey, and a cell outside the window
            // (after now, or before the month's last 30 days) is left blank and not clickable.
            UsageHeatmapScale scale = UsageHeatmapScale.FromValues(heatmap.TotalTokens.Select(v => (double)v));
            for (int i = 0; i < layout.CellCount; i++)
            {
                if (layout.IsOutsideWindow(i))
                {
                    continue;
                }

                long tokens = heatmap.TotalTokens[i];
                int level = scale.LevelOf(tokens);
                long start = layout.CellStartUnixMs(i);
                bool selected = _DrillStart == start;
                Border cell = HeatmapSwatch(level, theme, Math.Max(4, rowHeight - 3));
                cell.Margin = new Thickness(1.5);
                if (selected)
                {
                    cell.BorderBrush = theme.Text;
                    cell.BorderThickness = new Thickness(2);
                }

                cell.Cursor = HandCursor;
                ToolTip.SetTip(cell, UsageChartShaping.FormatBucketWindow(start, layout.BucketMs, null, culture)
                    + "\n" + Localizer.T("usage.tip.total") + ": " + tokens.ToString("#,##0", culture)
                    + "\n" + Localizer.T("us.calls") + ": " + heatmap.Calls[i].ToString("#,##0", culture)
                    + "\n" + Localizer.T("usage.tip.drill"));
                long width = layout.BucketMs;
                cell.PointerPressed += (sender, args) => SelectBucket(start, width);
                Grid.SetColumn(cell, (i % layout.Columns) + 1);
                Grid.SetRow(cell, (i / layout.Columns) + 1);
                grid.Children.Add(cell);
            }

            host.Children.Add(grid);
            host.Children.Add(BuildHeatmapLegend(theme, max));
            return host;
        }

        // The chart area's height: the fixed chart height, or enough for every heatmap row.
        private double ChartHostHeight()
        {
            if (_Metric != ChartMetric.Heatmap || _Data?.Heatmap == null)
            {
                return 340;
            }

            UsageHeatmapLayout layout = _Data.Heatmap.Layout;
            return Math.Max(340, (layout.Rows * UsageChartShaping.HeatmapRowHeight(layout.Rows, layout.Columns)) + 120);
        }

        // A grey swatch for no usage, then the four shades from less to more, then the order and the peak.
        private static Control BuildHeatmapLegend(AppTheme theme, long max)
        {
            StackPanel legend = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
            TextBlock Label(string text) => new TextBlock { Text = text, Foreground = theme.Muted, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
            legend.Children.Add(Label(Localizer.T("usage.heatmap.order") + " · "));
            legend.Children.Add(HeatmapSwatch(0, theme, 12, 12));
            legend.Children.Add(Label(Localizer.T("usage.heatmap.none") + " · " + Localizer.T("usage.heatmap.less")));
            for (int level = 1; level <= UsageHeatmapScale.Levels; level++)
            {
                legend.Children.Add(HeatmapSwatch(level, theme, 12, 12));
            }

            legend.Children.Add(Label(Localizer.T("usage.heatmap.more") + " · " + Format("usage.heatmap.peak", max.ToString("#,##0", CultureInfo.CurrentCulture))));
            return legend;
        }

        // A heatmap cell or legend swatch: a solid fill, the neutral grey for level 0 (no usage), with no border. A NaN
        // width stretches to the grid column.
        private static Border HeatmapSwatch(int level, AppTheme theme, double height, double width = double.NaN)
        {
            return new Border
            {
                Width = width,
                Height = height,
                CornerRadius = new CornerRadius(3),
                Background = new SolidColorBrush(Color.Parse(UsageChartShaping.HeatmapFill(level, theme.IsDark))),
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        private Control EmptyState(AppTheme theme)
        {
            return new TextBlock { Text = Localizer.T("usage.noUsage"), Foreground = theme.Muted, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        }

        private Control BuildChart(AppTheme theme)
        {
            List<UsageBucket> buckets = Buckets;
            _ChartHost.Height = ChartHostHeight();
            UpdateToolbar(theme);
            if (_Metric == ChartMetric.Heatmap)
            {
                return _Data?.Heatmap != null && _Data.Heatmap.HasUsage() ? BuildTokenHeatmap(theme) : EmptyState(theme);
            }

            if (_Data == null || buckets.Count == 0 || !UsageChartShaping.HasUsage(buckets))
            {
                return EmptyState(theme);
            }

            switch (_Metric)
            {
                case ChartMetric.UsageBy:
                    return BuildUsageByChart(theme);
                case ChartMetric.Calls:
                    Grid split = new Grid { ColumnDefinitions = new ColumnDefinitions("*,240") };
                    Control chart = BuildOverTimeChart(buckets, theme);
                    Grid.SetColumn(chart, 0);
                    split.Children.Add(chart);
                    Control failures = BuildFailurePanel(theme);
                    Grid.SetColumn(failures, 1);
                    split.Children.Add(failures);
                    return split;
                default:
                    return BuildOverTimeChart(buckets, theme);
            }
        }

        private Control BuildOverTimeChart(List<UsageBucket> buckets, AppTheme theme)
        {
            List<(UsageSeries Series, int ColorIndex)> stack = _Metric == ChartMetric.TokensBy
                ? UsageChartShaping.SplitStack(_Data?.Split)
                : new List<(UsageSeries Series, int ColorIndex)>();
            List<Dictionary<long, UsageMetrics>> stackByStart = stack
                .Select(s => s.Series.Buckets.GroupBy(b => b.BucketStartUnixMs).ToDictionary(g => g.Key, g => g.First().Metrics))
                .ToList();

            // Lines mode (stacked charts only): one unstacked line per series, scaled to the largest single value.
            bool lines = _Lines && UsageChartExport.IsStacked(ChartKind(_Metric));
            List<(double[] Values, IBrush Color)> lineSeries = lines ? LineSeries(buckets, stack, theme) : new List<(double[] Values, IBrush Color)>();
            double max = lines
                ? UsageChartShaping.LineMax(lineSeries.Select(s => (IReadOnlyList<double>)s.Values))
                : _Metric == ChartMetric.TokensBy ? UsageChartShaping.SplitMax(stack.Select(s => s.Series), _SplitMetric) : ComputeMax(buckets);
            if (max <= 0)
            {
                max = 1;
            }

            Grid plot = new Grid();
            for (int i = 0; i < buckets.Count; i++)
            {
                plot.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            }

            bool distribution = IsDistribution();
            IBrush boxColor = BoxColor(theme);

            for (int i = 0; i < buckets.Count; i++)
            {
                UsageBucket bucket = buckets[i];
                UsageMetrics m = bucket.Metrics;
                Control column;

                if (lines)
                {
                    // The lines are drawn over the whole plot; the column only carries the hover, click, and selection.
                    column = new Panel();
                }
                else if (_Metric == ChartMetric.Calls)
                {
                    (long succeeded, long failed) = UsageChartShaping.CallStack(m);
                    StackPanel bars = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(1, 0, 1, 0) };
                    AddSegment(bars, failed, max, theme.Error);
                    AddSegment(bars, succeeded, max, theme.Success);
                    column = bars;
                }
                else if (_Metric == ChartMetric.Tokens)
                {
                    (long uncached, long cached, long output, long _) = UsageChartShaping.TokenStack(m);
                    StackPanel bars = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(1, 0, 1, 0) };
                    AddSegment(bars, output, max, OutputColor);
                    AddSegment(bars, cached, max, CachedColor);
                    AddSegment(bars, uncached, max, PromptColor);
                    column = bars;
                }
                else if (_Metric == ChartMetric.TokensBy)
                {
                    // Rank 0 at the bottom, "Other" on top.
                    StackPanel bars = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(1, 0, 1, 0) };
                    for (int k = stack.Count - 1; k >= 0; k--)
                    {
                        double amount = stackByStart[k].TryGetValue(bucket.BucketStartUnixMs, out UsageMetrics? part) ? UsageChartShaping.SplitValue(part, _SplitMetric) : 0;
                        AddSegment(bars, amount, max, SeriesColor(stack[k].ColorIndex));
                    }

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

                bool selected = _DrillStart == bucket.BucketStartUnixMs;
                Border cell = new Border
                {
                    Background = selected ? theme.Hover : Brushes.Transparent,
                    Child = column,
                    Cursor = HandCursor
                };
                ToolTip.SetTip(cell, _Metric == ChartMetric.TokensBy ? SplitTooltip(bucket, stack, stackByStart) : BucketTooltip(bucket, _Metric));
                long start = bucket.BucketStartUnixMs;
                cell.PointerPressed += (sender, args) => SelectBucket(start);
                Grid.SetColumn(cell, i);
                plot.Children.Add(cell);
            }

            Grid chart = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("56,*"),
                RowDefinitions = new RowDefinitions(PlotHeight.ToString(CultureInfo.InvariantCulture) + ",Auto")
            };

            Control yAxis = BuildYAxis(max, theme);
            Grid.SetRow(yAxis, 0);
            Grid.SetColumn(yAxis, 0);
            chart.Children.Add(yAxis);

            Panel plotArea = new Panel { Height = PlotHeight };
            plotArea.Children.Add(BuildGridlines(theme));
            plotArea.Children.Add(plot);
            if (lines)
            {
                plotArea.Children.Add(new UsageLinesControl(lineSeries, max) { Height = PlotHeight, IsHitTestVisible = false });
            }
            Grid.SetRow(plotArea, 0);
            Grid.SetColumn(plotArea, 1);
            chart.Children.Add(plotArea);

            Control xAxis = BuildXAxis(theme);
            Grid.SetRow(xAxis, 1);
            Grid.SetColumn(xAxis, 1);
            chart.Children.Add(xAxis);

            Control? legend = BuildLegend(boxColor, stack, theme);
            if (legend == null && lines)
            {
                // A single cost line still gets a key, so lines mode always says what each line is.
                WrapPanel key = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(56, 6, 0, 0) };
                key.Children.Add(LegendItem(Localizer.T("usage.kpi.cost"), theme.Accent, theme));
                legend = key;
            }

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

        // The series lines mode draws for the current stacked chart, in the bars' colors and stacking order.
        private List<(double[] Values, IBrush Color)> LineSeries(List<UsageBucket> buckets, List<(UsageSeries Series, int ColorIndex)> stack, AppTheme theme)
        {
            List<(double[], IBrush)> series = new List<(double[], IBrush)>();
            switch (_Metric)
            {
                case ChartMetric.Calls:
                    series.Add((buckets.Select(b => (double)UsageChartShaping.CallStack(b.Metrics).Succeeded).ToArray(), theme.Success));
                    series.Add((buckets.Select(b => (double)UsageChartShaping.CallStack(b.Metrics).Failed).ToArray(), theme.Error));
                    break;
                case ChartMetric.Tokens:
                    series.Add((buckets.Select(b => (double)UsageChartShaping.TokenStack(b.Metrics).Uncached).ToArray(), PromptColor));
                    series.Add((buckets.Select(b => (double)UsageChartShaping.TokenStack(b.Metrics).Cached).ToArray(), CachedColor));
                    series.Add((buckets.Select(b => (double)UsageChartShaping.TokenStack(b.Metrics).Output).ToArray(), OutputColor));
                    break;
                case ChartMetric.TokensBy:
                    foreach ((UsageSeries s, int colorIndex) in stack)
                    {
                        series.Add((UsageChartShaping.AlignSeries(s, buckets, _SplitMetric), SeriesColor(colorIndex)));
                    }

                    break;
                default:
                    series.Add((buckets.Select(b => b.Metrics.CostUsd).ToArray(), theme.Accent));
                    break;
            }

            return series;
        }

        private Control BuildFailurePanel(AppTheme theme)
        {
            StackPanel panel = new StackPanel { Spacing = 6, Margin = new Thickness(16, 0, 0, 0) };
            panel.Children.Add(new TextBlock { Text = Localizer.T("usage.failures.title"), Foreground = theme.Text, FontSize = 13, FontWeight = FontWeight.SemiBold });

            List<UsageErrorCount> reasons = UsageChartShaping.FailureReasons(_Data?.Errors);
            if (reasons.Count == 0)
            {
                panel.Children.Add(new TextBlock { Text = Localizer.T("usage.failures.none"), Foreground = theme.Muted, FontSize = 12, TextWrapping = TextWrapping.Wrap });
            }

            foreach (UsageErrorCount reason in reasons)
            {
                DockPanel row = new DockPanel();
                TextBlock count = new TextBlock { Text = reason.Count.ToString("#,##0", CultureInfo.CurrentCulture), Foreground = theme.Error, FontSize = 12, FontWeight = FontWeight.SemiBold, Margin = new Thickness(8, 0, 0, 0) };
                DockPanel.SetDock(count, Dock.Right);
                row.Children.Add(count);
                TextBlock code = new TextBlock { Text = reason.ErrorCode, Foreground = theme.Text, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis };
                code.Tip(reason.ErrorCode);
                row.Children.Add(code);
                panel.Children.Add(row);
            }

            return new Border
            {
                BorderBrush = theme.Border,
                BorderThickness = new Thickness(1, 0, 0, 0),
                Child = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto }
            };
        }

        private Control BuildUsageByChart(AppTheme theme)
        {
            // Ranked, and drawn, by the shared metric; every row still lists tokens, cost, and calls.
            List<UsageBreakdownRow> rows = UsageChartShaping.RankBreakdown(_Data?.Breakdown, _SplitMetric);
            if (rows.Count == 0)
            {
                return EmptyState(theme);
            }

            string metricName = Localizer.T(UsageChartShaping.SplitMetricLabelKey(_SplitMetric));
            List<(string Label, IBrush Color)> keys = _SplitMetric == UsageCsv.MetricCalls
                ? new List<(string, IBrush)> { (Localizer.T("usage.legend.succeeded"), theme.Success), (Localizer.T("usage.legend.failed"), theme.Error) }
                : _SplitMetric == UsageCsv.MetricCost
                    ? new List<(string, IBrush)> { (Localizer.T("usage.kpi.cost"), theme.Accent) }
                    : new List<(string, IBrush)> { (Localizer.T("usage.legend.prompt"), PromptColor), (Localizer.T("usage.legend.cached"), CachedColor), (Localizer.T("usage.legend.output"), OutputColor) };
            double max = Math.Max(1e-12, rows.Max(r => UsageChartShaping.SplitValue(r.Metrics, _SplitMetric)));
            StackPanel list = new StackPanel { Spacing = 6 };
            StackPanel heading = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Margin = new Thickness(0, 0, 0, 4) };
            heading.Children.Add(new TextBlock { Text = ByLabel("usage.metric.usageBy"), Foreground = theme.Text, FontSize = 14, VerticalAlignment = VerticalAlignment.Center });
            heading.Children.Add(new TextBlock { Text = Format("usage.usageBy.sortedBy", metricName), Foreground = theme.Muted, FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
            list.Children.Add(heading);

            foreach (UsageBreakdownRow row in rows)
            {
                (long uncached, long cached, long output, long total) = UsageChartShaping.TokenStack(row.Metrics);
                string value = string.IsNullOrWhiteSpace(row.Value) ? "—" : row.Value;
                string detail = Format("usage.usageBy.tokens", FormatCount(total)) + " · $" + row.Metrics.CostUsd.ToString("0.0000")
                    + " · " + Format("usage.usageBy.calls", row.Metrics.Calls.ToString("#,##0", CultureInfo.CurrentCulture));

                Grid line = new Grid { ColumnDefinitions = new ColumnDefinitions("200,*,260"), Height = 20 };
                TextBlock label = new TextBlock { Text = value, Foreground = theme.Text, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 8, 0) };
                label.Tip(value);
                Grid.SetColumn(label, 0);
                line.Children.Add(label);

                (long succeeded, long failed) = UsageChartShaping.CallStack(row.Metrics);
                double[] parts = _SplitMetric == UsageCsv.MetricCalls
                    ? new double[] { succeeded, failed }
                    : _SplitMetric == UsageCsv.MetricCost
                        ? new[] { row.Metrics.CostUsd }
                        : new double[] { uncached, cached, output };
                Grid bar = new Grid { Height = 14, VerticalAlignment = VerticalAlignment.Center };
                for (int p = 0; p < parts.Length; p++)
                {
                    bar.ColumnDefinitions.Add(new ColumnDefinition(Math.Max(0, parts[p]), GridUnitType.Star));
                    AddBarPart(bar, p, keys[p].Color);
                }

                bar.ColumnDefinitions.Add(new ColumnDefinition(Math.Max(0, max - parts.Sum()), GridUnitType.Star));
                Border barHost = new Border { Background = Brushes.Transparent, Child = bar };
                ToolTip.SetTip(barHost, value
                    + "\n" + Localizer.T("usage.legend.prompt") + ": " + uncached.ToString("#,##0")
                    + "\n" + Localizer.T("usage.legend.cached") + ": " + cached.ToString("#,##0")
                    + "\n" + Localizer.T("usage.legend.output") + ": " + output.ToString("#,##0")
                    + "\n" + Localizer.T("usage.legend.succeeded") + ": " + succeeded.ToString("#,##0")
                    + "\n" + Localizer.T("usage.legend.failed") + ": " + failed.ToString("#,##0")
                    + "\n" + detail);
                Grid.SetColumn(barHost, 1);
                line.Children.Add(barHost);

                TextBlock info = new TextBlock { Text = detail, Foreground = theme.Muted, FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
                Grid.SetColumn(info, 2);
                line.Children.Add(info);
                list.Children.Add(line);
            }

            WrapPanel legend = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(200, 6, 0, 0) };
            foreach ((string label, IBrush color) in keys)
            {
                legend.Children.Add(LegendItem(label, color, theme));
            }

            list.Children.Add(legend);
            return new ScrollViewer { Content = list, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto };
        }

        private static void AddBarPart(Grid bar, int column, IBrush color)
        {
            Border part = new Border { Background = color };
            Grid.SetColumn(part, column);
            bar.Children.Add(part);
        }

        private static IBrush SeriesColor(int colorIndex)
        {
            return colorIndex < 0 ? OtherColor : SeriesPalette[colorIndex % SeriesPalette.Length];
        }

        private static string SeriesLabel(UsageSeries series)
        {
            if (series.IsOther)
            {
                return Localizer.T("usage.legend.other");
            }

            return string.IsNullOrWhiteSpace(series.Key) ? "—" : series.Key;
        }

        private Control? BuildLegend(IBrush boxColor, List<(UsageSeries Series, int ColorIndex)> stack, AppTheme theme)
        {
            WrapPanel legend = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(56, 6, 0, 0) };

            switch (_Metric)
            {
                case ChartMetric.Calls:
                    legend.Children.Add(LegendItem(Localizer.T("usage.legend.succeeded"), theme.Success, theme));
                    legend.Children.Add(LegendItem(Localizer.T("usage.legend.failed"), theme.Error, theme));
                    return legend;
                case ChartMetric.Tokens:
                    legend.Children.Add(LegendItem(Localizer.T("usage.legend.prompt"), PromptColor, theme));
                    legend.Children.Add(LegendItem(Localizer.T("usage.legend.cached"), CachedColor, theme));
                    legend.Children.Add(LegendItem(Localizer.T("usage.legend.output"), OutputColor, theme));
                    return legend;
                case ChartMetric.TokensBy:
                    foreach ((UsageSeries series, int colorIndex) in stack)
                    {
                        legend.Children.Add(LegendItem(SeriesLabel(series), SeriesColor(colorIndex), theme));
                    }

                    return legend;
            }

            if (IsDistribution())
            {
                legend.Children.Add(LegendItem(Localizer.T("usage.legend.avgP95"), boxColor, theme));
                legend.Children.Add(LegendItem(Localizer.T("usage.legend.minMax"), theme.Muted, theme));
                legend.Children.Add(LegendItem(Localizer.T("usage.legend.avg"), theme.Text, theme));
                legend.Children.Add(LegendItem(Localizer.T("usage.legend.p99"), P99Color, theme));
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
            return IsDistribution(_Metric);
        }

        private static bool IsDistribution(ChartMetric metric)
        {
            return metric == ChartMetric.Latency || metric == ChartMetric.Ttft || metric == ChartMetric.Streaming || metric == ChartMetric.Throughput;
        }

        private UsageDistribution DistFor(UsageMetrics m)
        {
            return DistFor(m, _Metric);
        }

        private static UsageDistribution DistFor(UsageMetrics m, ChartMetric metric)
        {
            switch (metric)
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

        private string BucketTitle(UsageBucket bucket)
        {
            return UsageChartShaping.FormatBucketWindow(bucket.BucketStartUnixMs, BucketWidth);
        }

        private string BucketTooltip(UsageBucket bucket, ChartMetric metric)
        {
            string time = BucketTitle(bucket);
            UsageMetrics m = bucket.Metrics;
            string hint = "\n" + Localizer.T("usage.tip.drill");

            if (metric == ChartMetric.Calls)
            {
                (long succeeded, long failed) = UsageChartShaping.CallStack(m);
                double rate = m.Calls > 0 ? (double)failed / m.Calls : 0.0;
                return time
                    + "\n" + Localizer.T("usage.legend.succeeded") + ": " + succeeded.ToString("#,##0")
                    + "\n" + Localizer.T("usage.legend.failed") + ": " + failed.ToString("#,##0")
                    + "\n" + Localizer.T("usage.kpi.errorRate") + ": " + (rate * 100).ToString("0.#") + "%"
                    + hint;
            }

            if (metric == ChartMetric.Tokens)
            {
                (long uncached, long cached, long output, long total) = UsageChartShaping.TokenStack(m);
                return time
                    + "\n" + Localizer.T("usage.legend.prompt") + ": " + uncached.ToString("#,##0")
                    + "\n" + Localizer.T("usage.legend.cached") + ": " + cached.ToString("#,##0")
                    + "\n" + Localizer.T("usage.legend.output") + ": " + output.ToString("#,##0")
                    + "\n" + Localizer.T("usage.tip.total") + ": " + total.ToString("#,##0")
                    + hint;
            }

            if (metric == ChartMetric.Cost)
            {
                return time
                    + "\n" + Localizer.T("usage.kpi.cost") + ": $" + m.CostUsd.ToString("0.0000")
                    + "\n" + Localizer.T("usage.kpi.calls") + ": " + m.Calls.ToString("#,##0")
                    + hint;
            }

            UsageDistribution d = DistFor(m, metric);
            string unit = metric == ChartMetric.Throughput ? " tok/s" : " ms";
            string format = metric == ChartMetric.Throughput ? "0.#" : "#,##0";
            return time
                + "\n" + Localizer.T("usage.tip.max") + ":  " + d.Max.ToString(format) + unit
                + "\np99:  " + d.P99.ToString(format) + unit
                + "\np95:  " + d.P95.ToString(format) + unit
                + "\n" + Localizer.T("usage.legend.avg") + ":  " + d.Avg.ToString(format) + unit
                + "\n" + Localizer.T("usage.tip.min") + ":  " + d.Min.ToString(format) + unit
                + "\n" + Localizer.T("usage.tip.samples") + ": " + d.Count.ToString("#,##0")
                + hint;
        }

        private string SplitTooltip(UsageBucket bucket, List<(UsageSeries Series, int ColorIndex)> stack, List<Dictionary<long, UsageMetrics>> stackByStart)
        {
            string text = BucketTitle(bucket) + "\n" + Localizer.T(UsageChartShaping.SplitMetricLabelKey(_SplitMetric));
            double total = 0;
            for (int k = 0; k < stack.Count; k++)
            {
                double amount = stackByStart[k].TryGetValue(bucket.BucketStartUnixMs, out UsageMetrics? part) ? UsageChartShaping.SplitValue(part, _SplitMetric) : 0;
                total += amount;
                text += "\n" + SeriesLabel(stack[k].Series) + ": " + FormatSplitValue(amount);
            }

            return text + "\n" + Localizer.T("usage.tip.total") + ": " + FormatSplitValue(total) + "\n" + Localizer.T("usage.tip.drill");
        }

        // A split value in the metric's unit: "$0.0123" for cost, a grouped count for tokens and calls.
        private string FormatSplitValue(double value)
        {
            return _SplitMetric == UsageCsv.MetricCost ? "$" + value.ToString("0.0000", CultureInfo.CurrentCulture) : value.ToString("#,##0", CultureInfo.CurrentCulture);
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
            List<UsageBucket> buckets = Buckets;
            Grid axis = new Grid { Margin = new Thickness(0, 4, 0, 0) };
            const int ticks = 6;
            for (int k = 0; k < ticks; k++)
            {
                axis.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            }

            int count = buckets.Count;
            if (count == 0)
            {
                return axis;
            }

            string format = UsageChartShaping.AxisFormat(_Range, BucketWidth);
            for (int k = 0; k < ticks; k++)
            {
                int index = count <= 1 ? 0 : (int)Math.Round(k / (double)(ticks - 1) * (count - 1));
                long ts = buckets[Math.Clamp(index, 0, count - 1)].BucketStartUnixMs;

                TextBlock label = new TextBlock
                {
                    Text = DateTimeOffset.FromUnixTimeMilliseconds(ts).LocalDateTime.ToString(format),
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

        private double ComputeMax(List<UsageBucket> buckets)
        {
            bool distribution = IsDistribution();
            double max = 0;
            foreach (UsageBucket bucket in buckets)
            {
                double value;
                if (distribution)
                {
                    value = DistFor(bucket.Metrics).Max;
                }
                else if (_Metric == ChartMetric.Calls)
                {
                    value = bucket.Metrics.Calls;
                }
                else if (_Metric == ChartMetric.Tokens)
                {
                    value = UsageChartShaping.TokenStack(bucket.Metrics).Total;
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
                case ChartMetric.Calls:
                    return m.Calls;
                case ChartMetric.Latency:
                    return m.AvgTotalMs;
                case ChartMetric.Ttft:
                    return m.AvgTtftMs;
                case ChartMetric.Streaming:
                    return m.AvgStreamMs;
                case ChartMetric.Throughput:
                    return m.AvgTokensPerSec;
                default:
                    return UsageChartShaping.TokenStack(m).Total;
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
                case ChartMetric.Calls:
                    return value >= 1000 ? FormatCount((long)Math.Round(value)) : value.ToString("#,##0.#");
                case ChartMetric.TokensBy when _SplitMetric == UsageCsv.MetricCost:
                    return "$" + value.ToString("0.00");
                case ChartMetric.TokensBy when _SplitMetric == UsageCsv.MetricCalls:
                    return value >= 1000 ? FormatCount((long)Math.Round(value)) : value.ToString("#,##0.#");
                default:
                    return FormatCount((long)Math.Round(value));
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
            StackPanel row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, Margin = new Thickness(0, 0, 12, 2) };
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
                    Text = row.Success ? Localizer.T("usage.status.ok") : (string.IsNullOrEmpty(row.ErrorCode) ? Localizer.T("usage.status.error") : row.ErrorCode),
                    Foreground = row.Success ? theme.Success : theme.Error,
                    FontSize = 12,
                    Margin = new Thickness(4, 2, 4, 2)
                };
                status.Tip(row.Success ? Localizer.T("usage.status.ok.tip") : Localizer.T("usage.status.failed.tip") + (string.IsNullOrEmpty(row.ErrorCode) ? "." : ": " + row.ErrorCode));
                Grid.SetColumn(status, 7);
                line.Children.Add(status);

                if (!string.IsNullOrEmpty(row.AppliedReasoningEffort))
                {
                    line.Tip(Localizer.T("usage.effortSent") + ": " + row.AppliedReasoningEffort);
                }

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
            if (string.IsNullOrWhiteSpace(resolved) && row.SessionId != null && _Conversations.TryGetValue(row.SessionId, out string? known))
            {
                resolved = known;
            }

            return UsageScope.ConversationLabel(row.SessionId, resolved);
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

        private static void AddRow(Grid grid, int row, Control child)
        {
            Grid.SetRow(child, row);
            grid.Children.Add(child);
        }

        // The heatmap's x-axis labels, laid out for the width it is given: labels placed by
        // UsageHeatmapLayout.PlaceColumnLabels so none collide, each left-justified at the first column it names (a
        // weekday at its AM column); a label that would run past the grid is dropped, and the axis clips to its bounds.
        // It redraws on resize, so labels thin and return as the window narrows and widens.
        private sealed class HeatmapXAxis : Control
        {
            private readonly UsageHeatmapLayout _Layout;
            private readonly List<string> _Labels;
            private readonly IBrush _Brush;

            public HeatmapXAxis(UsageHeatmapLayout layout, List<string> labels, IBrush brush)
            {
                _Layout = layout;
                _Labels = labels;
                _Brush = brush;
                ClipToBounds = true;
            }

            public override void Render(DrawingContext context)
            {
                double width = Bounds.Width;
                if (width <= 0 || _Labels.Count == 0)
                {
                    return;
                }

                Typeface face = new Typeface(FontFamily.Default);
                FormattedText Text(string text) => new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, face, 10, _Brush);
                double widest = _Labels.Max(l => Text(l).Width);
                foreach (int column in _Layout.PlaceColumnLabels(UsageChartShaping.HeatmapLabelGap(widest, width, _Layout.Columns)))
                {
                    context.DrawText(Text(_Labels[column]), new Point(UsageChartShaping.HeatmapLabelLeft(column, width, _Layout.Columns), 0));
                }
            }
        }

        // Lines mode: one 2px line per series through the bucket centers (see UsageChartShaping.LinePoints), in the
        // bars' colors, with a point marker per bucket when there are few buckets. The first series is drawn last, so
        // it stays on top where lines cross. It does not take input; the bucket columns under it handle hover and click.
        private sealed class UsageLinesControl : Control
        {
            private readonly List<(double[] Values, IBrush Color)> _Series;
            private readonly double _Max;

            public UsageLinesControl(List<(double[] Values, IBrush Color)> series, double max)
            {
                _Series = series;
                _Max = max;
            }

            public override void Render(DrawingContext context)
            {
                double width = Bounds.Width;
                double height = Bounds.Height;
                if (width <= 0 || height <= 0)
                {
                    return;
                }

                for (int s = _Series.Count - 1; s >= 0; s--)
                {
                    (double[] values, IBrush color) = _Series[s];
                    List<(double X, double Y)> points = UsageChartShaping.LinePoints(values, width, height, _Max);
                    if (points.Count == 0)
                    {
                        continue;
                    }

                    if (points.Count > 1)
                    {
                        StreamGeometry geometry = new StreamGeometry();
                        using (StreamGeometryContext path = geometry.Open())
                        {
                            path.BeginFigure(new Point(points[0].X, points[0].Y), false);
                            for (int i = 1; i < points.Count; i++)
                            {
                                path.LineTo(new Point(points[i].X, points[i].Y));
                            }

                            path.EndFigure(false);
                        }

                        context.DrawGeometry(null, new Pen(color, 2, null, PenLineCap.Round, PenLineJoin.Round, 10), geometry);
                    }

                    if (points.Count == 1 || UsageChartShaping.ShowLineMarkers(points.Count))
                    {
                        foreach ((double x, double y) in points)
                        {
                            context.DrawEllipse(color, null, new Point(x, y), 3, 3);
                        }
                    }
                }
            }
        }
    }
}
