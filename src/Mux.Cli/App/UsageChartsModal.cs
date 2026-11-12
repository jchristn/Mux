namespace Mux.Cli.App
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Text;
    using Mux.Core.Settings;
    using Mux.Core.Telemetry;
    using TUIKit;
    using TUIKit.Input;
    using TUIKit.Modals;

    /// <summary>
    /// A large, paged usage-analytics view. Each chart is its own page, in this order: calls (succeeded and failed,
    /// with failure reasons), tokens by type, a metric (tokens, calls, or cost) by a dimension (model, endpoint,
    /// provider, or call kind), cost, box plots of latency, time to first token, streaming time, and throughput, usage
    /// by the same dimension (ranked by the same metric), and a token activity heatmap. ←/→ (or Tab/Shift+Tab) change
    /// pages; 1-7 pick the range (hour, day, week, month, quarter, year, all); d/D cycle the dimension and m/M the
    /// metric; l switches the stacked over-time charts between bars and lines; e exports the page's data as CSV
    /// (<see cref="UsageCsv"/>) into the exports folder and shows the path in the footer; [ and ] (or Home/End) move a
    /// bucket cursor on the over-time pages and Enter opens the calls in the selected bucket
    /// (<see cref="UsageCallsModal"/>); Esc closes. All data comes from injected providers; the only I/O is the export.
    /// </summary>
    public sealed class UsageChartsModal : Modal
    {
        #region Public-Members

        /// <summary>The dimension keys the "&lt;Metric&gt; by" and "Usage by" pages cycle through, in order.</summary>
        public static IReadOnlyList<string> Dimensions { get; } = new[] { "model", "endpoint", "provider", "callkind" };

        /// <summary>The metric keys m/M cycle through, in order (tokens first; see <see cref="UsageCsv.Metrics"/>).</summary>
        public static IReadOnlyList<string> Metrics
        {
            get => UsageCsv.Metrics;
        }

        /// <summary>The number of pages.</summary>
        public static int PageCount
        {
            get => PageTotal;
        }

        /// <summary>The zero-based index of the current page.</summary>
        public int Page
        {
            get => _Page;
        }

        /// <summary>The selected range.</summary>
        public UsageRange Range
        {
            get => _Range;
        }

        /// <summary>The selected dimension key (see <see cref="Dimensions"/>).</summary>
        public string Dimension
        {
            get => Dimensions[_DimensionIndex];
        }

        /// <summary>The selected metric key for the "&lt;Metric&gt; by" and "Usage by" pages (see <see cref="Metrics"/>).</summary>
        public string Metric
        {
            get => Metrics[_MetricIndex];
        }

        /// <summary>True when the stacked over-time charts (calls, tokens, metric by dimension, cost) draw lines instead of bars.</summary>
        public bool Lines
        {
            get => _Lines;
        }

        /// <summary>The full path of the last CSV export, or null when nothing has been exported.</summary>
        public string? LastExportPath
        {
            get => _LastExportPath;
        }

        /// <summary>The footer notice from the last export (the saved path or why nothing was saved), or empty.</summary>
        public string Notice
        {
            get => _Notice;
        }

        /// <summary>The index of the bucket under the cursor on the over-time pages, or -1 when there are no buckets.</summary>
        public int Cursor
        {
            get => _Cursor;
        }

        /// <summary>The open bucket drill-down, or null when the charts are showing.</summary>
        public UsageCallsModal? DrillDown
        {
            get => _Drill;
        }

        #endregion

        #region Private-Members

        private const int PageCalls = 0;
        private const int PageTokens = 1;
        private const int PageTokensBy = 2;
        private const int PageCost = 3;
        private const int PageLatency = 4;
        private const int PageTtft = 5;
        private const int PageStream = 6;
        private const int PageThroughput = 7;
        private const int PageUsageBy = 8;
        private const int PageHeatmap = 9;
        private const int PageTotal = 10;

        private const int PadX = 2;
        private const int PadY = 1;
        private const int YAxisWidth = 10;
        private const long MinuteMs = 60_000L;
        private const long DayMs = 1440L * MinuteMs;

        private static readonly Color[] _Palette =
        {
            Color.FromRgb(110, 168, 230), Color.FromRgb(91, 192, 174),
            Color.FromRgb(224, 179, 102), Color.FromRgb(177, 151, 218),
            Color.FromRgb(223, 142, 153), Color.FromRgb(137, 190, 214),
            Color.FromRgb(180, 196, 128), Color.FromRgb(205, 165, 128)
        };
        private static readonly Color _OtherColor = Color.FromRgb(128, 128, 128);
        private static readonly Color _SucceededColor = Color.FromPalette(2);
        private static readonly Color _FailedColor = Color.FromPalette(1);
        private static readonly Color _UncachedColor = _Palette[0];
        private static readonly Color _CachedColor = _Palette[1];
        private static readonly Color _OutputColor = _Palette[3];
        private static readonly Color _CostColor = _Palette[2];
        private static readonly Color _AvgColor = Color.FromRgb(240, 240, 240);
        private static readonly Color _P99Color = Color.FromRgb(223, 142, 153);
        private static readonly Color _CursorTint = Color.FromRgb(60, 64, 72);
        // Heatmap shades for levels 1-4 (low to high): one green hue, dark to bright, validated for monotone lightness,
        // visible steps, and at least 2:1 contrast for the darkest step on a dark terminal. Level 0 (no usage) has no
        // fill and is drawn as a dim dot, so it never reads as a dark shade.
        private static readonly Color[] _HeatColors =
        {
            Color.FromRgb(31, 107, 72), Color.FromRgb(47, 154, 102), Color.FromRgb(79, 201, 138), Color.FromRgb(155, 232, 189)
        };

        // Eighth-block glyphs (U+2581..U+2588) for sub-cell bar heights.
        private static readonly string[] _Eighths = { "▁", "▂", "▃", "▄", "▅", "▆", "▇", "█" };

        private static readonly UsageRange[] _Ranges =
        {
            UsageRange.Hour, UsageRange.Day, UsageRange.Week, UsageRange.Month, UsageRange.Quarter, UsageRange.Year, UsageRange.All
        };

        private static readonly string[] _DimensionLabels = { "Model", "Endpoint", "Provider", "Call kind" };
        private static readonly string[] _MetricLabels = { "Tokens", "Calls", "Cost" };

        // Braille dot bits by (column, row) within a cell: the left column is dots 1, 2, 3, 7 and the right 4, 5, 6, 8.
        private static readonly int[,] _BrailleBits = { { 0x01, 0x02, 0x04, 0x40 }, { 0x08, 0x10, 0x20, 0x80 } };

        private readonly string _Title;
        private readonly Func<UsageRange, UsageChartData> _Provider;
        private readonly Func<UsageChartData, string, UsageDimensionData>? _DimensionProvider;
        private readonly Func<long, long, UsageEventPage>? _CallsProvider;
        private UsageRange _Range;
        private int _Page;
        private readonly string? _ExportDirectory;
        private int _DimensionIndex;
        private int _MetricIndex;
        private bool _Lines;
        private string? _LastExportPath;
        private string _Notice = string.Empty;
        private bool _NoticeIsError;
        private int _Cursor = -1;
        private int _BarBucketOffset;
        private int _VisibleBuckets = 1;
        private int _ListOffset;
        private int _ListVisibleRows = 1;
        private int _ListRows;
        // The heatmap's first visible column when it scrolls sideways (int.MaxValue: scrolled to the newest).
        private int _HeatmapColumnOffset = int.MaxValue;
        private int _HeatmapVisibleColumns = 1;
        private int _HeatmapColumns = 1;
        private UsageChartData _Data = new UsageChartData();
        private UsageDimensionData _Dim = new UsageDimensionData();
        private double[][] _DimValues = Array.Empty<double[]>();
        private string _Error = string.Empty;
        private string _DimError = string.Empty;
        private UsageCallsModal? _Drill;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="UsageChartsModal"/> class.
        /// </summary>
        /// <param name="title">The box title.</param>
        /// <param name="provider">Supplies pre-computed chart data for a requested range. Required.</param>
        /// <param name="initialRange">The range shown first.</param>
        /// <param name="dimensionProvider">Supplies the split series and whole-range rows for a dimension over the window
        /// of loaded chart data; null leaves the "Tokens by" and "Usage by" pages empty.</param>
        /// <param name="callsProvider">Supplies the calls in an inclusive window (from, to; Unix ms) for a bucket
        /// drill-down; null makes the drill-down report that call history is unavailable.</param>
        /// <param name="initialDimension">The dimension shown first ("model" when null or unknown).</param>
        /// <param name="initialMetric">The metric shown first ("tokens" when null or unknown).</param>
        /// <param name="initialLines">True to start the stacked over-time charts in lines mode.</param>
        /// <param name="exportDirectory">The folder CSV exports are written to; null uses <c>exports</c> under
        /// <see cref="SettingsLoader.GetConfigDirectory"/>.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="provider"/> is null.</exception>
        public UsageChartsModal(
            string title,
            Func<UsageRange, UsageChartData> provider,
            UsageRange initialRange,
            Func<UsageChartData, string, UsageDimensionData>? dimensionProvider = null,
            Func<long, long, UsageEventPage>? callsProvider = null,
            string? initialDimension = null,
            string? initialMetric = null,
            bool initialLines = false,
            string? exportDirectory = null)
        {
            _MetricIndex = Math.Max(0, UsageCsv.Metrics.ToList().IndexOf(UsageCsv.NormalizeMetric(initialMetric)));
            _Lines = initialLines;
            _ExportDirectory = exportDirectory;
            _Title = title ?? "Usage";
            _Provider = provider ?? throw new ArgumentNullException(nameof(provider));
            _DimensionProvider = dimensionProvider;
            _CallsProvider = callsProvider;
            _Range = initialRange;
            int dim = initialDimension == null ? 0 : Array.IndexOf(Dimensions.ToArray(), initialDimension.Trim().ToLowerInvariant());
            _DimensionIndex = dim < 0 ? 0 : dim;
            Load();
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc/>
        public override bool HandleKey(KeyEvent key)
        {
            if (_Drill != null)
            {
                _Drill.HandleKey(key);
                if (_Drill.IsClosed || _Drill.Completion.IsCompleted) _Drill = null;
                return true;
            }

            if (key.Code == KeyCode.Escape)
            {
                Close(null);
                return true;
            }

            // An export notice stays in the footer until the next key.
            _Notice = string.Empty;

            if (key.Code == KeyCode.Enter)
            {
                if (IsOverTimePage(_Page)) OpenDrillDown();
                return true;
            }

            if (key.Code == KeyCode.Right || (key.Code == KeyCode.Tab && key.Modifiers != KeyModifiers.Shift))
            {
                SetPage((_Page + 1) % PageTotal);
                return true;
            }

            if (key.Code == KeyCode.Left || (key.Code == KeyCode.Tab && key.Modifiers == KeyModifiers.Shift))
            {
                SetPage((_Page + PageTotal - 1) % PageTotal);
                return true;
            }

            int bucketCount = _Data.Buckets.Count;
            if (IsOverTimePage(_Page) && bucketCount > 0)
            {
                if (key.Rune == '[' || key.Rune == ']')
                {
                    _Cursor = Math.Clamp(_Cursor + (key.Rune == ']' ? 1 : -1), 0, bucketCount - 1);
                    return true;
                }

                if (key.Code == KeyCode.Home || key.Code == KeyCode.End)
                {
                    _Cursor = key.Code == KeyCode.Home ? 0 : bucketCount - 1;
                    return true;
                }

                if (key.Code == KeyCode.PageDown || key.Code == KeyCode.PageUp)
                {
                    int step = key.Code == KeyCode.PageDown ? _VisibleBuckets : -_VisibleBuckets;
                    _BarBucketOffset = Math.Clamp(_BarBucketOffset + step, 0, Math.Max(0, bucketCount - _VisibleBuckets));
                    _Cursor = Math.Clamp(_Cursor + step, _BarBucketOffset, Math.Min(bucketCount - 1, _BarBucketOffset + _VisibleBuckets - 1));
                    return true;
                }
            }

            if (_Page == PageHeatmap && (key.Code == KeyCode.PageDown || key.Code == KeyCode.PageUp))
            {
                // PgUp scrolls to older columns (left), PgDn to newer (right).
                int step = key.Code == KeyCode.PageDown ? _HeatmapVisibleColumns : -_HeatmapVisibleColumns;
                int limit = Math.Max(0, _HeatmapColumns - _HeatmapVisibleColumns);
                _HeatmapColumnOffset = Math.Clamp(Math.Min(_HeatmapColumnOffset, limit) + step, 0, limit);
                return true;
            }

            if (key.Code == KeyCode.Down || key.Code == KeyCode.Up || (_Page == PageUsageBy && (key.Code == KeyCode.PageDown || key.Code == KeyCode.PageUp)))
            {
                int step = key.Code == KeyCode.Down ? 1 : key.Code == KeyCode.Up ? -1 : key.Code == KeyCode.PageDown ? _ListVisibleRows : -_ListVisibleRows;
                _ListOffset = Math.Clamp(_ListOffset + step, 0, Math.Max(0, _ListRows - _ListVisibleRows));
                return true;
            }

            if (key.Rune == 'd' || key.Rune == 'D')
            {
                _DimensionIndex = (_DimensionIndex + (key.Rune == 'd' ? 1 : Dimensions.Count - 1)) % Dimensions.Count;
                _ListOffset = 0;
                LoadDimension();
                return true;
            }

            if (key.Rune == 'm' || key.Rune == 'M')
            {
                _MetricIndex = (_MetricIndex + (key.Rune == 'm' ? 1 : Metrics.Count - 1)) % Metrics.Count;
                _ListOffset = 0;
                AlignDimension();
                return true;
            }

            if (key.Rune == 'l' || key.Rune == 'L')
            {
                _Lines = !_Lines;
                return true;
            }

            if (key.Rune == 'e' || key.Rune == 'E')
            {
                Export();
                return true;
            }

            if (key.Rune >= '1' && key.Rune < '1' + _Ranges.Length)
            {
                _Range = _Ranges[key.Rune - '1'];
                _HeatmapColumnOffset = int.MaxValue;
                _BarBucketOffset = 0;
                _ListOffset = 0;
                Load();
                return true;
            }

            return true;
        }

        /// <inheritdoc/>
        public override void Render(ISurface surface)
        {
            if (surface == null) throw new ArgumentNullException(nameof(surface));
            if (surface.Size.Width <= 0 || surface.Size.Height <= 0) return;

            int boxWidth = surface.Size.Width;
            int boxHeight = surface.Size.Height;
            Rect box = new Rect(0, 0, boxWidth, boxHeight);

            surface.Fill(box, Cell.Blank(CellStyle.Default));
            surface.DrawBox(box, CellStyle.Default.WithForeground(Color.FromPalette(8)), _Title);

            int contentX = 1 + PadX;
            int contentW = Math.Max(1, boxWidth - 2 - (2 * PadX));
            int firstRow = 1 + PadY;
            int footerRow = boxHeight - 1 - PadY;

            CellStyle heading = CellStyle.Default.WithForeground(Color.FromPalette(6));
            CellStyle muted = CellStyle.Default.WithForeground(Color.FromPalette(8));

            string header = PageTitle(_Page) + "   ·   " + RangeLabel() + "   ·   page " + (_Page + 1) + "/" + PageTotal;
            surface.DrawText(contentX, firstRow, Trim(header, contentW), heading);
            string kpi = _Error.Length > 0 ? _Error : (_Data.HeaderLines.Count > 0 ? _Data.HeaderLines[0] : "No usage in this range.");
            surface.DrawText(contentX, firstRow + 1, Trim(kpi, contentW), _Error.Length > 0 ? CellStyle.Default.WithForeground(Color.FromPalette(9)) : CellStyle.Default);

            int chartTop = firstRow + 3;
            int chartBottom = footerRow - 1;
            if (chartBottom >= chartTop && _Error.Length == 0)
            {
                RenderPage(surface, contentX, chartTop, contentW, chartBottom - chartTop + 1, muted);
            }

            if (footerRow > firstRow + 1)
            {
                if (_Notice.Length > 0)
                {
                    surface.DrawText(contentX, footerRow, TrimStart(_Notice, contentW), CellStyle.Default.WithForeground(Color.FromPalette((byte)(_NoticeIsError ? 9 : 10))));
                }
                else
                {
                    surface.DrawText(contentX, footerRow, FooterHint(contentW), muted);
                }
            }

            _Drill?.Render(surface);
        }

        #endregion

        #region Private-Methods

        private static bool IsOverTimePage(int page) => page <= PageThroughput;

        private static bool IsBoxPage(int page) => page >= PageLatency && page <= PageThroughput;

        // The over-time charts that stack series and can switch to lines.
        private static bool IsStackedPage(int page) => page == PageCalls || page == PageTokens || page == PageTokensBy || page == PageCost;

        private void SetPage(int page)
        {
            _Page = page;
            _ListOffset = 0;
        }

        private void Load()
        {
            try
            {
                _Data = _Provider(_Range) ?? new UsageChartData();
                _Error = string.Empty;
            }
            catch (Exception ex)
            {
                _Data = new UsageChartData();
                _Error = "Failed to read usage: " + ex.Message;
            }

            _Cursor = _Data.Buckets.Count - 1;
            LoadDimension();
        }

        private void LoadDimension()
        {
            _DimError = string.Empty;
            try
            {
                _Dim = _Error.Length == 0 && _DimensionProvider != null
                    ? _DimensionProvider(_Data, Dimension) ?? new UsageDimensionData()
                    : new UsageDimensionData();
            }
            catch (Exception ex)
            {
                _Dim = new UsageDimensionData();
                _DimError = "Failed to read usage by " + DimensionLabel().ToLowerInvariant() + ": " + ex.Message;
            }

            AlignDimension();
        }

        // Aligns every split series with the main buckets by start time (falling back to position), reading the
        // selected metric.
        private void AlignDimension()
        {

            Dictionary<long, int> index = new Dictionary<long, int>();
            for (int i = 0; i < _Data.BucketStartsUnixMs.Count; i++) index[_Data.BucketStartsUnixMs[i]] = i;
            _DimValues = new double[_Dim.Series.Count][];
            for (int s = 0; s < _Dim.Series.Count; s++)
            {
                double[] values = new double[_Data.Buckets.Count];
                List<UsageBucket> buckets = _Dim.Series[s].Buckets;
                for (int b = 0; b < buckets.Count; b++)
                {
                    int at = index.Count > 0 ? (index.TryGetValue(buckets[b].BucketStartUnixMs, out int found) ? found : -1) : b;
                    if (at >= 0 && at < values.Length) values[at] += UsageCsv.MetricValue(buckets[b].Metrics, Metric);
                }

                _DimValues[s] = values;
            }
        }

        private void OpenDrillDown()
        {
            if (_Cursor < 0 || _Cursor >= _Data.Buckets.Count) return;
            long width = BucketWidthMs();
            long start = _Cursor < _Data.BucketStartsUnixMs.Count ? _Data.BucketStartsUnixMs[_Cursor] : 0;
            string title = start > 0 ? "Calls " + UsageChartFormat.BucketWindow(start, width) : "Calls " + BucketLabel(_Cursor);
            UsageEventPage? page = null;
            string? error = null;
            if (_CallsProvider == null || start <= 0)
            {
                error = "Call history is unavailable for this bucket.";
            }
            else
            {
                try
                {
                    page = _CallsProvider(start, start + width - 1);
                }
                catch (Exception ex)
                {
                    error = "Failed to read calls: " + ex.Message;
                }
            }

            _Drill = new UsageCallsModal(title, page, error, width >= DayMs);
        }

        private long BucketWidthMs()
        {
            if (_Data.BucketMs > 0) return _Data.BucketMs;
            List<long> starts = _Data.BucketStartsUnixMs;
            if (starts.Count >= 2 && starts[1] > starts[0]) return starts[1] - starts[0];
            if (_Data.WindowToUnixMs > _Data.WindowFromUnixMs && _Data.WindowFromUnixMs > 0) return _Data.WindowToUnixMs - _Data.WindowFromUnixMs;
            UsageWindow.Bucketing(UsageWindow.KeyOf(_Range), out long bucketMs, out _);
            return bucketMs;
        }

        private string BucketLabel(int bucket) => bucket >= 0 && bucket < _Data.BucketLabels.Count ? _Data.BucketLabels[bucket] : "#" + (bucket + 1);

        private string BucketWindowLabel(int bucket)
        {
            if (bucket >= 0 && bucket < _Data.BucketStartsUnixMs.Count && _Data.BucketStartsUnixMs[bucket] > 0)
            {
                return UsageChartFormat.BucketWindow(_Data.BucketStartsUnixMs[bucket], BucketWidthMs());
            }

            return BucketLabel(bucket);
        }

        private string DimensionLabel() => _DimensionLabels[_DimensionIndex];

        private string MetricLabel() => _MetricLabels[_MetricIndex];

        private string PageTitle(int page)
        {
            switch (page)
            {
                case PageCalls: return "Calls over time";
                case PageTokens: return "Tokens by type";
                case PageTokensBy: return MetricLabel() + " by " + DimensionLabel().ToLowerInvariant();
                case PageCost: return "Cost over time";
                case PageLatency: return "Latency (ms)";
                case PageTtft: return "Time to first token (ms)";
                case PageStream: return "Streaming time (ms)";
                case PageThroughput: return "Throughput (tokens/s)";
                case PageUsageBy: return "Usage by " + DimensionLabel().ToLowerInvariant();
                default: return "Token activity heatmap";
            }
        }

        private string RangeLabel()
        {
            switch (_Range)
            {
                case UsageRange.Hour: return "Last hour";
                case UsageRange.Day: return "Last day";
                case UsageRange.Week: return "Last week";
                case UsageRange.Month: return "Last month";
                case UsageRange.Quarter: return "Last quarter";
                case UsageRange.Year: return "Last year";
                default:
                    return _Data.Buckets.Count > 0 ? "All time (" + UsageChartFormat.Interval(BucketWidthMs()) + " buckets)" : "All time";
            }
        }

        // Builds the footer from key hints, shortening and then dropping the least important ones (highest priority
        // number, later hints first) so it always fits; Esc close is never dropped.
        private string FooterHint(int width)
        {
            List<(int Priority, string[] Forms)> parts = new List<(int, string[])>
            {
                (1, new[] { "←/→ page", "←/→" }),
                (3, new[] { "1-7 hour/day/week/month/quarter/year/all", "1-7 range", "1-7" })
            };
            if (IsOverTimePage(_Page))
            {
                parts.Add((2, new[] { "[ ] bucket", "[ ]" }));
                parts.Add((2, new[] { "Enter calls in bucket", "Enter calls", "Enter" }));
                parts.Add((5, new[] { "PgUp/PgDn scroll", "PgUp/PgDn" }));
            }

            if (_Page == PageTokensBy || _Page == PageUsageBy)
            {
                parts.Add((2, new[] { "d dimension (" + DimensionLabel() + ")", "d dimension", "d dim" }));
                parts.Add((2, new[] { "m metric (" + MetricLabel() + ")", "m metric", "m" }));
            }

            if (IsStackedPage(_Page)) parts.Add((4, new[] { "l " + (_Lines ? "bars" : "lines") }));
            parts.Add((5, new[] { "e export CSV", "e export" }));
            if (_Page == PageHeatmap && _HeatmapVisibleColumns < _HeatmapColumns) parts.Add((4, new[] { "PgUp/PgDn scroll", "PgUp/PgDn" }));
            if (_Page == PageCalls || _Page == PageTokensBy || _Page == PageUsageBy) parts.Add((6, new[] { "↑/↓ list", "↑/↓" }));
            parts.Add((0, new[] { "Esc close", "Esc" }));

            int[] form = new int[parts.Count];
            bool[] dropped = new bool[parts.Count];
            string Join()
            {
                List<string> shown = new List<string>();
                for (int i = 0; i < parts.Count; i++) if (!dropped[i]) shown.Add(parts[i].Forms[form[i]]);
                return string.Join(" · ", shown);
            }

            // The least important candidate: highest priority number, and the later one among equals.
            int Pick(Func<int, bool> eligible)
            {
                int pick = -1;
                for (int i = 0; i < parts.Count; i++)
                {
                    if (!dropped[i] && eligible(i) && (pick < 0 || parts[i].Priority >= parts[pick].Priority)) pick = i;
                }

                return pick;
            }

            // First use each hint's short wording, then drop hints, then use the terse forms of what is left.
            string text = Join();
            while (text.Length > width)
            {
                int pick = Pick(i => form[i] == 0 && parts[i].Forms.Length > 1 && parts[i].Priority > 0);
                if (pick >= 0)
                {
                    form[pick] = 1;
                }
                else if ((pick = Pick(i => parts[i].Priority > 0)) >= 0)
                {
                    dropped[pick] = true;
                }
                else if ((pick = Pick(i => form[i] < parts[i].Forms.Length - 1)) >= 0)
                {
                    form[pick]++;
                }
                else
                {
                    break;
                }

                text = Join();
            }

            return Trim(text, width);
        }

        private void RenderPage(ISurface surface, int x, int y, int w, int h, CellStyle muted)
        {
            if (_Page == PageHeatmap)
            {
                RenderHeatmap(surface, x, y, w, h, muted);
                return;
            }

            if (_Page == PageUsageBy)
            {
                RenderUsageBy(surface, x, y, w, h, muted);
                return;
            }

            if (!HasUsage())
            {
                surface.DrawText(x, y, Trim("No usage in this range.", w), muted);
                return;
            }

            if (w <= YAxisWidth + 4 || h < 7)
            {
                surface.DrawText(x, y, Trim("Enlarge the terminal to see this chart.", w), muted);
                return;
            }

            switch (_Page)
            {
                case PageCalls:
                    RenderCalls(surface, x, y, w, h, muted);
                    break;
                case PageTokens:
                    RenderStacked(surface, x, y, w, h, "Tokens ↑ · Time (local) →",
                        new[] { ("uncached input", _UncachedColor), ("cached input", _CachedColor), ("output", _OutputColor) },
                        (s, b) =>
                        {
                            UsageMetrics m = _Data.Buckets[b];
                            return s == 0 ? Math.Max(0, m.InputTokens - m.CachedTokens) : s == 1 ? m.CachedTokens : m.OutputTokens;
                        },
                        UsageChartFormat.Tokens,
                        b =>
                        {
                            UsageMetrics m = _Data.Buckets[b];
                            return "uncached " + UsageChartFormat.Tokens(Math.Max(0, m.InputTokens - m.CachedTokens)) + " · cached " + UsageChartFormat.Tokens(m.CachedTokens)
                                + " · output " + UsageChartFormat.Tokens(m.OutputTokens);
                        },
                        muted);
                    break;
                case PageTokensBy:
                    RenderTokensBy(surface, x, y, w, h, muted);
                    break;
                case PageCost:
                    RenderStacked(surface, x, y, w, h, "Cost (USD) ↑ · Time (local) →",
                        new[] { ("cost (USD)", _CostColor) },
                        (s, b) => _Data.Buckets[b].CostUsd,
                        UsageChartFormat.Usd,
                        b => UsageChartFormat.Usd(_Data.Buckets[b].CostUsd) + " · " + _Data.Buckets[b].Calls + " calls",
                        muted);
                    break;
                case PageLatency:
                    RenderBoxes(surface, x, y, w, h, "Total latency (ms / s) ↑ · Time (local) →", m => m.TotalMsDist, UsageChartFormat.Ms, _Palette[0], muted);
                    break;
                case PageTtft:
                    RenderBoxes(surface, x, y, w, h, "Time to first token (ms / s) ↑ · Time (local) →", m => m.TtftMsDist, UsageChartFormat.Ms, _Palette[1], muted);
                    break;
                case PageStream:
                    RenderBoxes(surface, x, y, w, h, "Streaming time (ms / s) ↑ · Time (local) →", m => m.StreamMsDist, UsageChartFormat.Ms, _Palette[3], muted);
                    break;
                default:
                    RenderBoxes(surface, x, y, w, h, "Throughput (tokens/s) ↑ · Time (local) →", m => m.ThroughputDist, UsageChartFormat.Rate, _Palette[5], muted);
                    break;
            }
        }

        private bool HasUsage()
        {
            foreach (UsageMetrics m in _Data.Buckets)
            {
                if (m.Calls > 0 || m.TotalTokens > 0 || m.CostUsd > 0) return true;
            }

            return false;
        }

        private void RenderCalls(ISurface surface, int x, int y, int w, int h, CellStyle muted)
        {
            List<UsageErrorCount> errors = _Data.ErrorCounts;
            bool side = w >= 80;
            int panelW = side ? Math.Min(36, w / 3) : w;
            int listRows = side ? h - 1 : Math.Min(Math.Max(1, errors.Count), Math.Max(1, (h - 8) / 2));
            int chartW = side ? w - panelW - 2 : w;
            int chartH = side ? h : h - listRows - 1;
            int panelX = side ? x + chartW + 2 : x;
            int panelY = side ? y : y + chartH;

            RenderStacked(surface, x, y, chartW, chartH, "Calls ↑ · Time (local) →",
                new[] { ("succeeded", _SucceededColor), ("failed", _FailedColor) },
                (s, b) => s == 0 ? Math.Max(0, _Data.Buckets[b].Calls - _Data.Buckets[b].Errors) : _Data.Buckets[b].Errors,
                v => v.ToString("0", CultureInfo.InvariantCulture),
                b =>
                {
                    UsageMetrics m = _Data.Buckets[b];
                    double rate = m.Calls > 0 ? (double)m.Errors / m.Calls : 0;
                    return "succeeded " + Math.Max(0, m.Calls - m.Errors) + " · failed " + m.Errors + " · " + UsageChartFormat.Percent(rate) + " errors";
                },
                muted);

            if (panelY >= y + h) return;
            surface.DrawText(panelX, panelY, Trim("Failure reasons", panelW), CellStyle.Default.WithForeground(Color.FromPalette(6)));
            if (errors.Count == 0)
            {
                _ListRows = 0;
                surface.DrawText(panelX, panelY + 1, Trim("No failures in this range.", panelW), muted);
                return;
            }

            _ListRows = errors.Count;
            _ListVisibleRows = Math.Max(1, Math.Min(listRows, y + h - panelY - 1));
            _ListOffset = Math.Clamp(_ListOffset, 0, Math.Max(0, errors.Count - _ListVisibleRows));
            for (int i = 0; i < _ListVisibleRows && _ListOffset + i < errors.Count; i++)
            {
                UsageErrorCount error = errors[_ListOffset + i];
                string count = error.Count.ToString(CultureInfo.InvariantCulture);
                string code = string.IsNullOrWhiteSpace(error.ErrorCode) ? "unknown" : error.ErrorCode;
                surface.DrawText(panelX, panelY + 1 + i, Trim(code, panelW - count.Length - 1), CellStyle.Default.WithForeground(_FailedColor));
                DrawRightAligned(surface, panelX, panelY + 1 + i, panelW, count, CellStyle.Default);
            }

            if (_ListVisibleRows < errors.Count && side && panelY + 1 + _ListVisibleRows < y + h)
            {
                surface.DrawText(panelX, panelY + 1 + _ListVisibleRows, Trim("↑/↓ " + (_ListOffset + 1) + "–" + (_ListOffset + _ListVisibleRows) + " of " + errors.Count, panelW), muted);
            }
        }

        private void RenderTokensBy(ISurface surface, int x, int y, int w, int h, CellStyle muted)
        {
            if (_DimError.Length > 0)
            {
                surface.DrawText(x, y, Trim(_DimError, w), CellStyle.Default.WithForeground(Color.FromPalette(9)));
                return;
            }

            if (_Dim.Series.Count == 0)
            {
                surface.DrawText(x, y, Trim("No usage in this range.", w), muted);
                return;
            }

            (string, Color)[] series = new (string, Color)[_Dim.Series.Count];
            int rank = 0;
            for (int i = 0; i < _Dim.Series.Count; i++)
            {
                UsageSeries s = _Dim.Series[i];
                series[i] = s.IsOther ? ("Other", _OtherColor) : (ValueLabel(s.Key), _Palette[rank++ % _Palette.Length]);
            }

            Func<double, string> fmt = MetricFormat();
            string axis = Metric == UsageCsv.MetricCost ? "Cost (USD)" : MetricLabel();
            RenderStacked(surface, x, y, w, h, axis + " ↑ · Time (local) → · color = " + DimensionLabel().ToLowerInvariant(), series,
                (s, b) => b < _DimValues[s].Length ? _DimValues[s][b] : 0,
                fmt,
                b =>
                {
                    int top = -1;
                    for (int s = 0; s < _DimValues.Length; s++) if (b < _DimValues[s].Length && _DimValues[s][b] > 0 && (top < 0 || _DimValues[s][b] > _DimValues[top][b])) top = s;
                    string total = MetricTotal(_Data.Buckets[b]);
                    return "total " + total + (top >= 0 ? " · top " + series[top].Item1 + " " + fmt(_DimValues[top][b]) : string.Empty);
                },
                muted, true);
        }

        // The axis and value format for the selected metric.
        private Func<double, string> MetricFormat()
        {
            switch (Metric)
            {
                case UsageCsv.MetricCalls: return v => v.ToString("0", CultureInfo.InvariantCulture);
                case UsageCsv.MetricCost: return UsageChartFormat.Usd;
                default: return UsageChartFormat.Tokens;
            }
        }

        // A bucket's or row's total for the selected metric, with its unit: "1.2k tok", "14 calls", or "$0.12".
        private string MetricTotal(UsageMetrics m)
        {
            switch (Metric)
            {
                case UsageCsv.MetricCalls: return m.Calls.ToString(CultureInfo.InvariantCulture) + " calls";
                case UsageCsv.MetricCost: return UsageChartFormat.Usd(m.CostUsd);
                default: return UsageChartFormat.Tokens(m.TotalTokens) + " tok";
            }
        }

        private string ValueLabel(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "Unknown " + DimensionLabel().ToLowerInvariant();
            return value;
        }

        // Stacked columns per bucket with a stable Y scale, X-axis ticks, a highlighted cursor column, a detail line for
        // the cursor's bucket, and a legend packed into lines (scrolled with ↑/↓ when it does not fit).
        private void RenderStacked(
            ISurface surface,
            int x,
            int y,
            int w,
            int h,
            string caption,
            IReadOnlyList<(string Name, Color Color)> series,
            Func<int, int, double> value,
            Func<double, string> fmt,
            Func<int, string> detail,
            CellStyle muted,
            bool scrollLegend = false)
        {
            surface.DrawText(x, y, Trim(_Lines ? caption + " · lines" : caption, w), muted);
            y++;
            h -= 1;

            List<List<(string Name, Color Color)>> legendLines = PackLegend(series, w);
            int legendVisible = Math.Min(legendLines.Count, Math.Max(1, h / 4));
            int plotH = h - 2 - legendVisible;
            if (plotH < 2)
            {
                surface.DrawText(x, y, Trim("Enlarge the terminal to see this chart.", w), muted);
                return;
            }

            int bucketCount = _Data.Buckets.Count;
            int plotX = x + YAxisWidth, plotW = w - YAxisWidth;
            FitBuckets(plotW, bucketCount);

            // Bars stack, so their scale is the largest bucket total; lines do not, so theirs is the largest single value.
            double max = 0;
            for (int b = 0; b < bucketCount; b++)
            {
                double total = 0;
                for (int s = 0; s < series.Count; s++) total = _Lines ? Math.Max(total, value(s, b)) : total + Math.Max(0, value(s, b));
                max = Math.Max(max, total);
            }

            DrawYAxis(surface, x, y, plotX, plotH, max, fmt, muted);
            if (_Lines)
            {
                DrawLines(surface, plotX, y, plotW, plotH, series, value, max);
            }

            for (int c = 0; c < _VisibleBuckets && !_Lines; c++)
            {
                int bucket = _BarBucketOffset + c;
                int left = plotX + c * plotW / _VisibleBuckets;
                int right = plotX + (c + 1) * plotW / _VisibleBuckets;
                int width = Math.Max(1, right - left - 1);
                Color? tint = bucket == _Cursor ? _CursorTint : null;
                if (tint.HasValue) surface.Fill(new Rect(left, y, width, plotH), Cell.Blank(CellStyle.Default.WithBackground(_CursorTint)));
                if (max <= 0) continue;

                // Quantize cumulative boundaries, so rounding cannot increase the stack total.
                int lower = 0;
                double cumulative = 0;
                Color previous = series[0].Color;
                for (int s = 0; s < series.Count; s++)
                {
                    cumulative += Math.Max(0, value(s, bucket));
                    int upper = Math.Clamp((int)Math.Round(cumulative / max * plotH * 8), 0, plotH * 8);
                    DrawSegment(surface, left, y + plotH - 1, width, lower, upper, series[s].Color, previous, tint);
                    if (upper > lower) previous = series[s].Color;
                    lower = upper;
                }
            }

            if (max <= 0) surface.DrawText(plotX, y + plotH / 2, Trim("No activity in this range.", plotW), muted);
            DrawXAxis(surface, plotX, y + plotH, plotW, muted);
            DrawDetail(surface, x, y + plotH + 1, w, detail, muted);

            ApplyListScroll(scrollLegend, legendLines.Count, legendVisible);
            int legendOffset = scrollLegend ? _ListOffset : 0;
            for (int row = 0; row < legendVisible && legendOffset + row < legendLines.Count; row++)
            {
                int lx = x;
                foreach ((string name, Color color) in legendLines[legendOffset + row])
                {
                    surface.DrawText(lx, y + plotH + 2 + row, _Lines ? "━" : "■", CellStyle.Default.WithForeground(color));
                    string label = Trim(name, Math.Max(0, x + w - lx - 2));
                    surface.DrawText(lx + 2, y + plotH + 2 + row, label, CellStyle.Default);
                    lx += label.Length + 5;
                }
            }
        }

        // Lines mode: one unstacked line per series through the bucket centers, drawn in braille dots (two by four per
        // cell) so slopes read smoothly; the first series is drawn last so it stays on top where lines cross. With room
        // to spare (at least six columns per bucket) each bucket also gets a "●" point marker. The cursor column keeps
        // its tint.
        private void DrawLines(ISurface surface, int plotX, int y, int plotW, int plotH, IReadOnlyList<(string Name, Color Color)> series, Func<int, int, double> value, double max)
        {
            int cursorLeft = -1, cursorRight = -1;
            for (int c = 0; c < _VisibleBuckets; c++)
            {
                int left = plotX + c * plotW / _VisibleBuckets;
                int right = plotX + (c + 1) * plotW / _VisibleBuckets;
                if (_BarBucketOffset + c == _Cursor)
                {
                    cursorLeft = left;
                    cursorRight = left + Math.Max(1, right - left - 1);
                }
            }

            if (cursorLeft >= 0) surface.Fill(new Rect(cursorLeft, y, cursorRight - cursorLeft, plotH), Cell.Blank(CellStyle.Default.WithBackground(_CursorTint)));
            if (max <= 0 || series.Count == 0) return;

            int dotsW = plotW * 2, dotsH = plotH * 4;
            int[,] bits = new int[plotW, plotH];
            int[,] owner = new int[plotW, plotH];
            for (int cx = 0; cx < plotW; cx++)
                for (int cy = 0; cy < plotH; cy++) owner[cx, cy] = -1;
            List<(int X, int Row, int Series)> markers = new List<(int, int, int)>();
            bool showMarkers = _VisibleBuckets * 6 <= plotW;

            void Dot(int dx, int dy, int s)
            {
                if (dx < 0 || dy < 0 || dx >= dotsW || dy >= dotsH) return;
                bits[dx / 2, dy / 4] |= _BrailleBits[dx % 2, dy % 4];
                owner[dx / 2, dy / 4] = s;
            }

            for (int s = series.Count - 1; s >= 0; s--)
            {
                int px = -1, py = -1;
                for (int c = 0; c < _VisibleBuckets; c++)
                {
                    int bucket = _BarBucketOffset + c;
                    int left = plotX + c * plotW / _VisibleBuckets - plotX;
                    int right = plotX + (c + 1) * plotW / _VisibleBuckets - plotX;
                    int width = Math.Max(1, right - left - 1);
                    int dx = Math.Clamp(left * 2 + width - 1, 0, dotsW - 1);
                    double v = Math.Max(0, value(s, bucket));
                    int dy = dotsH - 1 - (int)Math.Round(Math.Clamp(v / max, 0, 1) * (dotsH - 1));
                    if (px < 0)
                    {
                        Dot(dx, dy, s);
                    }
                    else
                    {
                        // Bresenham from the previous bucket's point.
                        int x0 = px, y0 = py, ddx = Math.Abs(dx - x0), ddy = -Math.Abs(dy - y0), sx = x0 < dx ? 1 : -1, sy = y0 < dy ? 1 : -1, err = ddx + ddy;
                        while (true)
                        {
                            Dot(x0, y0, s);
                            if (x0 == dx && y0 == dy) break;
                            int e2 = 2 * err;
                            if (e2 >= ddy) { err += ddy; x0 += sx; }
                            if (e2 <= ddx) { err += ddx; y0 += sy; }
                        }
                    }

                    if (showMarkers) markers.Add((plotX + dx / 2, y + dy / 4, s));
                    px = dx;
                    py = dy;
                }
            }

            for (int cx = 0; cx < plotW; cx++)
            {
                for (int cy = 0; cy < plotH; cy++)
                {
                    if (bits[cx, cy] == 0) continue;
                    int sx = plotX + cx;
                    CellStyle style = CellStyle.Default.WithForeground(series[owner[cx, cy]].Color);
                    if (sx >= cursorLeft && sx < cursorRight) style = style.WithBackground(_CursorTint);
                    surface.Set(sx, y + cy, Cell.Glyph(char.ConvertFromUtf32(0x2800 + bits[cx, cy]), style, 1));
                }
            }

            foreach ((int mx, int row, int s) in markers)
            {
                CellStyle style = CellStyle.Default.WithForeground(series[s].Color);
                if (mx >= cursorLeft && mx < cursorRight) style = style.WithBackground(_CursorTint);
                surface.Set(mx, row, Cell.Glyph("●", style, 1));
            }
        }

        private void ApplyListScroll(bool scroll, int lines, int visible)
        {
            if (!scroll) return;
            _ListRows = lines;
            _ListVisibleRows = visible;
            _ListOffset = Math.Clamp(_ListOffset, 0, Math.Max(0, lines - visible));
        }

        private static List<List<(string Name, Color Color)>> PackLegend(IReadOnlyList<(string Name, Color Color)> series, int width)
        {
            List<List<(string, Color)>> lines = new List<List<(string, Color)>>();
            List<(string, Color)> line = new List<(string, Color)>();
            int used = 0;
            foreach ((string name, Color color) in series)
            {
                int need = name.Length + 5;
                if (line.Count > 0 && used + need - 3 > width)
                {
                    lines.Add(line);
                    line = new List<(string, Color)>();
                    used = 0;
                }

                line.Add((name, color));
                used += need;
            }

            if (line.Count > 0) lines.Add(line);
            return lines;
        }

        // Box plots per bucket: a min-max wick, an avg-p95 box, an avg mark, and a p99 tick.
        private void RenderBoxes(ISurface surface, int x, int y, int w, int h, string caption, Func<UsageMetrics, UsageDistribution> pick, Func<double, string> fmt, Color color, CellStyle muted)
        {
            int bucketCount = _Data.Buckets.Count;
            double max = 0;
            bool any = false;
            for (int b = 0; b < bucketCount; b++)
            {
                UsageDistribution? d = pick(_Data.Buckets[b]);
                if (d == null || d.Count <= 0) continue;
                any = true;
                max = Math.Max(max, Math.Max(d.Max, Math.Max(d.P99, Math.Max(d.P95, d.Avg))));
            }

            surface.DrawText(x, y, Trim(caption, w), muted);
            if (!any)
            {
                surface.DrawText(x, y + 1, Trim("No timing samples in this range.", w), muted);
                return;
            }

            y++;
            h--;
            int plotH = h - 3;
            if (plotH < 2)
            {
                surface.DrawText(x, y, Trim("Enlarge the terminal to see this chart.", w), muted);
                return;
            }

            int plotX = x + YAxisWidth, plotW = w - YAxisWidth;
            FitBuckets(plotW, bucketCount);
            DrawYAxis(surface, x, y, plotX, plotH, max, fmt, muted);
            int RowOf(double v) => y + plotH - 1 - (int)Math.Round(Math.Clamp(max > 0 ? v / max : 0, 0, 1) * (plotH - 1));

            for (int c = 0; c < _VisibleBuckets; c++)
            {
                int bucket = _BarBucketOffset + c;
                int left = plotX + c * plotW / _VisibleBuckets;
                int right = plotX + (c + 1) * plotW / _VisibleBuckets;
                int width = Math.Max(1, right - left - 1);
                int center = left + (width - 1) / 2;
                bool cursor = bucket == _Cursor;
                CellStyle Style(Color fg) => cursor ? CellStyle.Default.WithForeground(fg).WithBackground(_CursorTint) : CellStyle.Default.WithForeground(fg);
                if (cursor) surface.Fill(new Rect(left, y, width, plotH), Cell.Blank(CellStyle.Default.WithBackground(_CursorTint)));

                UsageDistribution? d = pick(_Data.Buckets[bucket]);
                if (d == null || d.Count <= 0) continue;
                for (int row = RowOf(d.Max); row <= RowOf(d.Min); row++) surface.Set(center, row, Cell.Glyph("│", Style(color), 1));
                int boxTop = RowOf(Math.Max(d.Avg, d.P95)), boxBottom = RowOf(Math.Min(d.Avg, d.P95));
                for (int row = boxTop; row <= boxBottom; row++)
                    for (int dx = 0; dx < width; dx++) surface.Set(left + dx, row, Cell.Glyph("█", Style(color), 1));
                surface.Set(center, RowOf(d.P99), Cell.Glyph("┼", Style(_P99Color), 1));
                for (int dx = 0; dx < width; dx++) surface.Set(left + dx, RowOf(d.Avg), Cell.Glyph("━", CellStyle.Default.WithForeground(_AvgColor).WithBackground(color), 1));
            }

            DrawXAxis(surface, plotX, y + plotH, plotW, muted);
            DrawDetail(surface, x, y + plotH + 1, w, b =>
            {
                UsageDistribution? d = pick(_Data.Buckets[b]);
                if (d == null || d.Count <= 0) return "no samples";
                return "n=" + d.Count + " · min " + fmt(d.Min) + " · avg " + fmt(d.Avg) + " · p95 " + fmt(d.P95) + " · p99 " + fmt(d.P99) + " · max " + fmt(d.Max);
            }, muted);

            int ky = y + plotH + 2, kx = x;
            foreach ((string glyph, CellStyle style, string label) in new[]
            {
                ("│", CellStyle.Default.WithForeground(color), "min–max"),
                ("█", CellStyle.Default.WithForeground(color), "avg–p95"),
                ("━", CellStyle.Default.WithForeground(_AvgColor).WithBackground(color), "avg"),
                ("┼", CellStyle.Default.WithForeground(_P99Color), "p99")
            })
            {
                if (kx + 2 + label.Length > x + w) break;
                surface.Set(kx, ky, Cell.Glyph(glyph, style, 1));
                surface.DrawText(kx + 2, ky, label, muted);
                kx += label.Length + 5;
            }
        }

        // Sizes the visible bucket window to the plot width and keeps the cursor inside it.
        private void FitBuckets(int plotW, int bucketCount)
        {
            _VisibleBuckets = Math.Max(1, Math.Min(bucketCount, plotW));
            _Cursor = bucketCount == 0 ? -1 : Math.Clamp(_Cursor, 0, bucketCount - 1);
            if (_Cursor >= 0 && _Cursor < _BarBucketOffset) _BarBucketOffset = _Cursor;
            if (_Cursor >= _BarBucketOffset + _VisibleBuckets) _BarBucketOffset = _Cursor - _VisibleBuckets + 1;
            _BarBucketOffset = Math.Clamp(_BarBucketOffset, 0, Math.Max(0, bucketCount - _VisibleBuckets));
        }

        private static void DrawYAxis(ISurface surface, int x, int y, int plotX, int plotH, double max, Func<double, string> fmt, CellStyle muted)
        {
            DrawRightAligned(surface, x, y, YAxisWidth - 1, fmt(max), muted);
            if (plotH >= 3) DrawRightAligned(surface, x, y + plotH / 2, YAxisWidth - 1, fmt(max / 2), muted);
            DrawRightAligned(surface, x, y + plotH - 1, YAxisWidth - 1, fmt(0), muted);
            for (int row = 0; row < plotH; row++) surface.Set(plotX - 1, y + row, Cell.Glyph("│", muted, 1));
        }

        private void DrawXAxis(ISurface surface, int plotX, int row, int plotW, CellStyle muted)
        {
            int labelWidth = 5;
            for (int i = 0; i < _VisibleBuckets; i++) labelWidth = Math.Max(labelWidth, BucketLabel(_BarBucketOffset + i).Length);
            labelWidth = Math.Min(12, labelWidth);
            int ticks = Math.Min(_VisibleBuckets, Math.Max(1, plotW / (labelWidth + 2)));
            int lastEnd = int.MinValue;
            for (int t = 0; t < ticks; t++)
            {
                int c = ticks == 1 ? 0 : t * (_VisibleBuckets - 1) / (ticks - 1);
                int bucket = _BarBucketOffset + c;
                if (bucket >= _Data.BucketLabels.Count) continue;
                string label = Trim(_Data.BucketLabels[bucket], Math.Min(labelWidth, plotW));
                int labelX = Math.Min(plotX + c * plotW / _VisibleBuckets, plotX + plotW - label.Length);
                if (labelX <= lastEnd) continue; // Never let a clamped label run into its neighbor.
                surface.DrawText(labelX, row, label, muted);
                lastEnd = labelX + label.Length;
            }
        }

        private void DrawDetail(ISurface surface, int x, int row, int w, Func<int, string> detail, CellStyle muted)
        {
            int count = _Data.Buckets.Count;
            string paging = " · buckets " + (_BarBucketOffset + 1) + "–" + (_BarBucketOffset + _VisibleBuckets) + "/" + count;
            string text = _Cursor >= 0 && _Cursor < count ? "▸ " + BucketWindowLabel(_Cursor) + " · " + detail(_Cursor) : string.Empty;
            if (text.Length + paging.Length <= w) text += paging;
            surface.DrawText(x, row, Trim(text, w), CellStyle.Default);
        }

        private void RenderUsageBy(ISurface surface, int x, int y, int w, int h, CellStyle muted)
        {
            if (_DimError.Length > 0)
            {
                surface.DrawText(x, y, Trim(_DimError, w), CellStyle.Default.WithForeground(Color.FromPalette(9)));
                return;
            }

            List<UsageBreakdownRow> rows = UsageByRows(UsageChartQueries.BreakdownRows);
            if (rows.Count == 0)
            {
                surface.DrawText(x, y, Trim("No usage in this range.", w), muted);
                return;
            }

            string metric = Metric;
            string axis = metric == UsageCsv.MetricCost ? "Cost (USD)" : MetricLabel();
            surface.DrawText(x, y, Trim(DimensionLabel() + " ↓ · " + axis + " → · sorted by " + MetricLabel().ToLowerInvariant(), w), muted);
            (string Name, Color Color)[] keys = metric == UsageCsv.MetricCalls
                ? new[] { ("succeeded", _SucceededColor), ("failed", _FailedColor) }
                : metric == UsageCsv.MetricCost
                    ? new[] { ("cost (USD)", _CostColor) }
                    : new[] { ("uncached input", _UncachedColor), ("cached input", _CachedColor), ("output", _OutputColor) };
            int kx = x;
            foreach ((string name, Color color) in keys)
            {
                if (h < 3 || kx + 2 + name.Length > x + w) break;
                surface.DrawText(kx, y + 1, "■", CellStyle.Default.WithForeground(color));
                surface.DrawText(kx + 2, y + 1, name, CellStyle.Default);
                kx += name.Length + 5;
            }

            int top = y + 3;
            _ListRows = rows.Count;
            _ListVisibleRows = Math.Max(1, Math.Min(rows.Count, y + h - top));
            _ListOffset = Math.Clamp(_ListOffset, 0, Math.Max(0, rows.Count - _ListVisibleRows));
            if (top >= y + h) return;

            List<string> stats = rows.Select(r => UsageChartFormat.Tokens(r.Metrics.TotalTokens) + " tok · " + UsageChartFormat.Usd(r.Metrics.CostUsd) + " · " + r.Metrics.Calls + " calls").ToList();
            int statsW = Math.Min(w / 2, stats.Max(s => s.Length));
            int labelW = Math.Min(32, Math.Max(8, w / 4));
            int barW = Math.Max(1, w - labelW - statsW - 2);
            double max = rows.Max(r => UsageCsv.MetricValue(r.Metrics, metric));
            for (int i = 0; i < _ListVisibleRows && _ListOffset + i < rows.Count; i++)
            {
                UsageBreakdownRow row = rows[_ListOffset + i];
                int ry = top + i;
                surface.DrawText(x, ry, Trim(ValueLabel(row.Value), labelW - 1), CellStyle.Default);
                UsageMetrics m = row.Metrics;
                double[] parts = metric == UsageCsv.MetricCalls
                    ? new double[] { Math.Max(0, m.Calls - m.Errors), m.Errors }
                    : metric == UsageCsv.MetricCost
                        ? new[] { m.CostUsd }
                        : new double[] { Math.Max(0, m.InputTokens - m.CachedTokens), m.CachedTokens, m.OutputTokens };
                Color[] colors = keys.Select(k => k.Color).ToArray();
                double scale = max > 0 ? barW / max : 0;
                double cumulative = 0;
                int lower = 0;
                for (int p = 0; p < parts.Length; p++)
                {
                    cumulative += parts[p];
                    int upper = Math.Clamp((int)Math.Round(cumulative * scale), 0, barW);
                    if (upper > lower) surface.DrawText(x + labelW + lower, ry, new string('█', upper - lower), CellStyle.Default.WithForeground(colors[p]));
                    lower = upper;
                }

                surface.DrawText(x + labelW + barW + 2, ry, Trim(stats[_ListOffset + i], statsW), muted);
            }
        }

        // The "Usage by" rows ranked by the selected metric: every row when max is 0, else the top max.
        private List<UsageBreakdownRow> UsageByRows(int max)
        {
            return UsageChartQueries.RankRows(_Dim.AllRows.Count > 0 ? _Dim.AllRows : _Dim.Rows, Metric, max);
        }

        // Writes the current page's data as CSV into the exports folder and puts the path (or the reason nothing was
        // written) in the footer notice.
        private void Export()
        {
            string? csv = null;
            string chart;
            long width = BucketWidthMs();
            List<UsageBucket> buckets = new List<UsageBucket>();
            for (int i = 0; i < _Data.Buckets.Count; i++)
            {
                long start = i < _Data.BucketStartsUnixMs.Count ? _Data.BucketStartsUnixMs[i] : 0;
                buckets.Add(new UsageBucket { BucketStartUnixMs = start, Metrics = _Data.Buckets[i] ?? new UsageMetrics() });
            }

            bool usage = _Error.Length == 0 && HasUsage();
            switch (_Page)
            {
                case PageCalls:
                    chart = UsageCsv.ChartCalls;
                    if (usage) csv = UsageCsv.Calls(buckets, width);
                    break;
                case PageTokens:
                    chart = UsageCsv.ChartTokens;
                    if (usage) csv = UsageCsv.Tokens(buckets, width);
                    break;
                case PageTokensBy:
                    chart = UsageCsv.SplitChart(Metric, Dimension);
                    if (usage && _DimError.Length == 0 && _Dim.Series.Count > 0) csv = UsageCsv.Split(_Data.BucketStartsUnixMs, width, _Dim.Series, Metric);
                    break;
                case PageCost:
                    chart = UsageCsv.ChartCost;
                    if (usage) csv = UsageCsv.Cost(buckets, width);
                    break;
                case PageLatency:
                    chart = UsageCsv.ChartLatency;
                    if (usage) csv = UsageCsv.Distribution(buckets, width, m => m.TotalMsDist);
                    break;
                case PageTtft:
                    chart = UsageCsv.ChartTtft;
                    if (usage) csv = UsageCsv.Distribution(buckets, width, m => m.TtftMsDist);
                    break;
                case PageStream:
                    chart = UsageCsv.ChartStreaming;
                    if (usage) csv = UsageCsv.Distribution(buckets, width, m => m.StreamMsDist);
                    break;
                case PageThroughput:
                    chart = UsageCsv.ChartThroughput;
                    if (usage) csv = UsageCsv.Distribution(buckets, width, m => m.ThroughputDist);
                    break;
                case PageUsageBy:
                    chart = UsageCsv.UsageByChart(Dimension);
                    List<UsageBreakdownRow> rows = UsageByRows(0);
                    if (_Error.Length == 0 && _DimError.Length == 0 && rows.Count > 0) csv = UsageCsv.UsageBy(_Data.WindowFromUnixMs, _Data.WindowToUnixMs, Dimension, rows);
                    break;
                default:
                    chart = UsageCsv.ChartHeatmap;
                    if (_Error.Length == 0 && _Data.Heatmap != null && _Data.Heatmap.HasUsage()) csv = UsageCsv.Heatmap(_Data.Heatmap);
                    break;
            }

            if (csv == null)
            {
                SetNotice("Nothing to export: no usage in this range.", true);
                return;
            }

            try
            {
                string directory = _ExportDirectory ?? Path.Combine(SettingsLoader.GetConfigDirectory(), "exports");
                Directory.CreateDirectory(directory);
                string path = Path.GetFullPath(Path.Combine(directory, UsageCsv.FileName(chart, UsageWindow.KeyOf(_Range), DateTime.Now)));
                File.WriteAllText(path, csv, new UTF8Encoding(false));
                _LastExportPath = path;
                SetNotice("Saved CSV: " + path, false);
            }
            catch (Exception ex)
            {
                SetNotice("Export failed: " + ex.Message, true);
            }
        }

        private void SetNotice(string text, bool error)
        {
            _Notice = text;
            _NoticeIsError = error;
        }

        private void RenderHeatmap(ISurface surface, int x, int y, int w, int h, CellStyle muted)
        {
            UsageHeatmap? heatmap = _Data.Heatmap;
            if (heatmap == null || !heatmap.HasUsage())
            {
                surface.DrawText(x, y, Trim("No usage in this range.", w), muted);
                return;
            }

            UsageHeatmapLayout layout = heatmap.Layout;
            int rows = layout.Rows, columns = layout.Columns, count = layout.CellCount;
            int axisWidth = Math.Min(12, layout.RowLabels.Max(l => l.Length)) + 1;
            int plotX = x + axisWidth, plotWidth = w - axisWidth, plotHeight = h - 6;
            if (plotHeight < rows || plotWidth < 8)
            {
                surface.DrawText(x, y, Trim("Enlarge the terminal to see the heatmap.", w), muted);
                return;
            }

            // The shared clock-aligned grid: time runs left to right, each column one local period (labeled along the
            // top), each row one step within it (labeled on the left); the month is a calendar of half-day columns
            // and week rows. Cells stretch to the terminal's width; when even one-character cells without gaps do
            // not fit, the grid scrolls sideways (PgUp/PgDn), starting at the newest columns.
            HeatmapCellSize(columns, rows, plotWidth, plotHeight, out int cellWidth, out int cellHeight, out int gapX, out int gapY);
            int pitchX = cellWidth + gapX, pitchY = cellHeight + gapY;
            int visible = Math.Min(columns, Math.Max(1, (plotWidth + gapX) / pitchX));
            _HeatmapColumns = columns;
            _HeatmapVisibleColumns = visible;
            _HeatmapColumnOffset = Math.Clamp(_HeatmapColumnOffset, 0, columns - visible);
            int offset = _HeatmapColumnOffset;
            double[] values = heatmap.TotalTokens.Select(v => (double)v).ToArray();
            double max = values.Length > 0 ? values.Max() : 0;
            UsageHeatmapScale scale = UsageHeatmapScale.FromValues(values);
            surface.DrawText(x, y, Trim(layout.Caption + " (local time)", w), muted);

            // Column labels, thinned so they never collide, each left-justified at the first column it names (a weekday
            // at its AM column); one that would run past the grid is dropped, never moved.
            int span = (visible * pitchX) - gapX;
            int widest = layout.ColumnLabels.Max(l => l.Length);
            foreach (int column in HeatmapLabels(layout, offset, visible, (widest + 1 + pitchX - 1) / pitchX))
            {
                surface.DrawText(plotX + (column * pitchX), y + 1, Trim(layout.ColumnLabels[column + offset], span - (column * pitchX)), muted);
            }

            for (int row = 0; row < rows; row++)
            {
                int top = y + 2 + row * pitchY;
                surface.DrawText(x, top + (cellHeight - 1) / 2, Trim(layout.RowLabels[row], axisWidth - 1), muted);
                for (int v = 0; v < visible; v++)
                {
                    int i = layout.IndexAt(row, offset + v);
                    int left = plotX + v * pitchX;
                    if (layout.IsOutsideWindow(i)) continue; // After now, or before the month's or year's window: no data.
                    double value = values[i];
                    int level = scale.LevelOf(value);
                    if (level == 0)
                    {
                        surface.Fill(new Rect(left, top, cellWidth, cellHeight), Cell.Blank(CellStyle.Default));
                        surface.DrawText(left + (cellWidth - 1) / 2, top + (cellHeight - 1) / 2, "·", muted);
                        continue;
                    }

                    CellStyle style = CellStyle.Default.WithBackground(_HeatColors[level - 1]);
                    surface.Fill(new Rect(left, top, cellWidth, cellHeight), Cell.Blank(style));
                    if (cellHeight >= 3)
                    {
                        string label = Trim(UsageChartFormat.Tokens(value), cellWidth);
                        Color foreground = level >= 3 ? Color.FromRgb(12, 35, 21) : Color.FromRgb(225, 238, 228);
                        surface.DrawText(left + Math.Max(0, (cellWidth - label.Length) / 2), top + cellHeight / 2, label, style.WithForeground(foreground));
                    }
                }
            }

            // When the grid scrolls, the visible columns lead the line so they survive a narrow terminal.
            string navigation = visible < columns ? "columns " + (offset + 1) + "–" + (offset + visible) + "/" + columns + " · PgUp/PgDn · " : string.Empty;
            surface.DrawText(x, y + h - 4, Trim(navigation + count + " spots · " + UsageChartFormat.Interval(layout.BucketMs) + "/spot", w), muted);
            string order = layout.ColumnKind == UsageHeatmapLayout.ColumnKindHalfDay ? "Oldest at the top-left, newest at the bottom-right" : "Oldest at the left, newest at the right";
            surface.DrawText(x, y + h - 3, Trim(order + " · peak " + UsageChartFormat.Tokens(max) + " tokens", w), muted);
            // Legend: "· none  less ▇▇ ▇▇ ▇▇ ▇▇ more (quartiles) · peak".
            surface.DrawText(x, y + h - 2, Trim("· none  less", w), muted);
            for (int i = 0; i < _HeatColors.Length; i++)
            {
                int left = x + 13 + i * 3;
                if (left + 2 > x + w) break;
                surface.Fill(new Rect(left, y + h - 2, 2, 1), Cell.Blank(CellStyle.Default.WithBackground(_HeatColors[i])));
            }

            if (w > 26) surface.DrawText(x + 25, y + h - 2, Trim("more (quartiles) · peak " + UsageChartFormat.Tokens(max), w - 25), muted);
        }

        // The labeled columns of the visible part of the grid, relative to its first visible column. The whole grid
        // uses the layout's placement; a scrolled part places the anchors whose columns are all visible (plus its
        // first and last column when labels are not grouped), greedily.
        private static List<int> HeatmapLabels(UsageHeatmapLayout layout, int offset, int visible, int minGap)
        {
            if (offset == 0 && visible == layout.Columns) return layout.PlaceColumnLabels(minGap);
            int group = layout.ColumnGroup;
            List<int> anchors = layout.ColumnLabelAnchors.Where(a => a >= offset && a + group <= offset + visible).Select(a => a - offset).ToList();
            if (group == 1)
            {
                anchors.Add(0);
                anchors.Add(visible - 1);
            }

            anchors = anchors.Distinct().OrderBy(a => a).ToList();
            return UsageHeatmapLayout.PlaceLabels(anchors, visible, minGap, false);
        }

        // The cell size that fills the plot's width: cells up to 8 characters wide and about half as tall (terminal
        // cells are about twice as tall as wide, up to 3 lines), with a one-character gap between columns and a
        // one-line gap between rows when they fit. When one-character cells with gaps do not fit, the gaps go; when
        // even that does not fit, cells stay one character wide and the grid scrolls sideways.
        private static void HeatmapCellSize(int columns, int rows, int width, int height, out int cellWidth, out int cellHeight, out int gapX, out int gapY)
        {
            gapX = 1;
            cellWidth = Math.Min(8, ((width + 1) / Math.Max(1, columns)) - 1);
            if (cellWidth < 1)
            {
                gapX = 0;
                cellWidth = Math.Max(1, width / Math.Max(1, columns));
                cellWidth = Math.Min(cellWidth, 1);
            }

            cellHeight = 1;
            gapY = rows * 2 - 1 <= height ? 1 : 0;
            for (int size = 2; size <= Math.Min(3, cellWidth / 2); size++)
            {
                if (rows * (size + gapY) - gapY > height) break;
                cellHeight = size;
            }
        }

        private static void DrawSegment(ISurface surface, int x, int bottom, int width, int lower, int upper, Color color, Color previousColor, Color? tint)
        {
            if (upper <= lower) return;
            for (int cell = lower / 8; cell < (upper + 7) / 8; cell++)
            {
                int top = Math.Min(8, upper - cell * 8);
                int start = Math.Max(0, lower - cell * 8);

                // A terminal cell can show two segment colors at a boundary. Use the lower block
                // in the preceding color and this segment as its background when the cell is full.
                for (int dx = 0; dx < width; dx++)
                {
                    CellStyle style = CellStyle.Default.WithForeground(color);
                    if (tint.HasValue) style = style.WithBackground(tint.Value);
                    string glyph = _Eighths[top - 1];
                    if (start > 0 && top == 8)
                    {
                        style = CellStyle.Default.WithForeground(previousColor).WithBackground(color);
                        glyph = _Eighths[start - 1];
                    }

                    surface.Set(x + dx, bottom - cell, Cell.Glyph(glyph, style, 1));
                }
            }
        }

        private static void DrawRightAligned(ISurface surface, int x, int y, int width, string value, CellStyle style)
        {
            string trimmed = value.Length > width ? value.Substring(0, Math.Max(0, width)) : value;
            surface.DrawText(x + Math.Max(0, width - trimmed.Length), y, trimmed, style);
        }

        private static string Trim(string text, int width) => UsageChartFormat.Trim(text, width);

        // Keeps the end of a long text (a file path's name matters most), with a leading ellipsis.
        private static string TrimStart(string text, int width)
        {
            if (width <= 0) return string.Empty;
            if (text.Length <= width) return text;
            return width == 1 ? "…" : "…" + text.Substring(text.Length - (width - 1));
        }

        #endregion
    }
}
