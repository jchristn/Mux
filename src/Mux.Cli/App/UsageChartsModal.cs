namespace Mux.Cli.App
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using Mux.Core.Telemetry;
    using TUIKit;
    using TUIKit.Input;
    using TUIKit.Modals;
    using TUIKit.Widgets;

    /// <summary>
    /// A large, paged usage-analytics view. Each metric is its own page — tokens, cost, time-to-first-token,
    /// total latency, streaming time (all as bar charts over time), top models by cost, and a token activity heatmap — navigated with
    /// the arrow keys. The time range (last hour / day / week / month) is chosen with the number keys and
    /// re-queried through an injected data provider so the modal performs no I/O. Over-time pages draw a
    /// labeled Y axis and several X-axis time ticks; the models page shows category labels with a value scale.
    /// Enter or Escape closes it.
    /// </summary>
    public sealed class UsageChartsModal : Modal
    {
        #region Private-Members

        private const int PadX = 2;
        private const int PadY = 1;
        private const int YAxisWidth = 10;

        // Fixed heatmap cell metrics: terminal cells are roughly twice as tall as they are wide, so a
        // 2-column by 1-row cell reads as a square. Cells always keep this size instead of stretching to
        // fill the plot, which is what made the old bars look absurdly wide.
        private const int HeatAxisWidth = 7;
        private const int HeatCellWidth = 2;
        private const int HeatGapX = 1;

        // Eighth-block glyphs (U+2581..U+2588) for sub-cell bar heights.
        private static readonly string[] _Eighths = { "▁", "▂", "▃", "▄", "▅", "▆", "▇", "█" };

        // GitHub-style green ramp, empty (dark) to peak (bright), shared by the cells and the legend swatches.
        private static readonly Color[] _HeatColors =
        {
            Color.FromRgb(45, 48, 52), Color.FromRgb(14, 68, 41), Color.FromRgb(0, 109, 50),
            Color.FromRgb(38, 166, 65), Color.FromRgb(57, 211, 83)
        };

        // Monday-first weekday column headers for the month calendar.
        private static readonly string[] _WeekdayHeaders = { "Mo", "Tu", "We", "Th", "Fr", "Sa", "Su" };

        private static readonly UsageRange[] _Ranges = { UsageRange.Hour, UsageRange.Day, UsageRange.Week, UsageRange.Month };
        private static readonly string[] _PageTitles =
        {
            "Tokens over time",
            "Cost over time",
            "Time to first token (ms)",
            "Total latency (ms)",
            "Streaming time (ms)",
            "Top models by cost",
            "Token activity heatmap"
        };

        private readonly string _Title;
        private readonly Func<UsageRange, UsageChartData> _Provider;
        private UsageRange _Range;
        private int _Page;
        private UsageChartData _Data = new UsageChartData();
        private string _Error = string.Empty;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="UsageChartsModal"/> class.
        /// </summary>
        /// <param name="title">The box title.</param>
        /// <param name="provider">Supplies pre-computed chart data for a requested range. Required.</param>
        /// <param name="initialRange">The range shown first.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="provider"/> is null.</exception>
        public UsageChartsModal(string title, Func<UsageRange, UsageChartData> provider, UsageRange initialRange)
        {
            _Title = title ?? "Usage";
            _Provider = provider ?? throw new ArgumentNullException(nameof(provider));
            _Range = initialRange;
            Load();
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc/>
        public override bool HandleKey(KeyEvent key)
        {
            if (key.Code == KeyCode.Escape || (key.Code == KeyCode.Enter && key.Modifiers == KeyModifiers.None))
            {
                Close(null);
                return true;
            }

            if (key.Code == KeyCode.Right || key.Code == KeyCode.Tab)
            {
                _Page = (_Page + 1) % _PageTitles.Length;
                return true;
            }

            if (key.Code == KeyCode.Left)
            {
                _Page = (_Page + _PageTitles.Length - 1) % _PageTitles.Length;
                return true;
            }

            if (key.Rune >= '1' && key.Rune < '1' + _Ranges.Length)
            {
                _Range = _Ranges[key.Rune - '1'];
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

            int screenWidth = surface.Size.Width;
            int screenHeight = surface.Size.Height;

            int boxWidth = screenWidth;
            int boxHeight = screenHeight;
            int boxX = 0;
            int boxY = 0;
            Rect box = new Rect(boxX, boxY, boxWidth, boxHeight);

            surface.Fill(box, Cell.Blank(CellStyle.Default));
            surface.DrawBox(box, CellStyle.Default.WithForeground(Color.FromPalette(8)), _Title);

            int contentX = boxX + 1 + PadX;
            int contentW = Math.Max(1, boxWidth - 2 - (2 * PadX));
            int firstRow = boxY + 1 + PadY;
            int footerRow = boxY + boxHeight - 1 - PadY;

            CellStyle text = CellStyle.Default;
            CellStyle heading = CellStyle.Default.WithForeground(Color.FromPalette(6));
            CellStyle muted = CellStyle.Default.WithForeground(Color.FromPalette(8));

            string header = _PageTitles[_Page] + "   ·   " + RangeLabel(_Range) + "   ·   page " + (_Page + 1) + "/" + _PageTitles.Length;
            surface.DrawText(contentX, firstRow, Trim(header, contentW), heading);
            string kpi = _Error.Length > 0 ? _Error : (_Data.HeaderLines.Count > 0 ? _Data.HeaderLines[0] : "No usage in this range.");
            surface.DrawText(contentX, firstRow + 1, Trim(kpi, contentW), _Error.Length > 0 ? CellStyle.Default.WithForeground(Color.FromPalette(9)) : text);

            int chartTop = firstRow + 3;
            int chartBottom = footerRow - 1;
            if (chartBottom >= chartTop && _Error.Length == 0)
            {
                RenderPage(surface, contentX, chartTop, contentW, chartBottom - chartTop + 1, muted);
            }

            surface.DrawText(contentX, footerRow, Trim("←/→ charts + heatmap · 1 hour · 2 day · 3 week · 4 month · Esc close", contentW), muted);
        }

        #endregion

        #region Private-Methods

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
        }

        private void RenderPage(ISurface surface, int x, int y, int w, int h, CellStyle muted)
        {
            switch (_Page)
            {
                case 0:
                    RenderBars(surface, x, y, w, h, _Data.TokensPerBucket, v => FmtTokens(v), Color.FromPalette(6), muted);
                    break;
                case 1:
                    RenderBars(surface, x, y, w, h, _Data.CostPerBucket, v => FmtUsd(v), Color.FromPalette(2), muted);
                    break;
                case 2:
                    RenderBars(surface, x, y, w, h, _Data.TtftMsPerBucket, v => FmtMs(v), Color.FromPalette(4), muted);
                    break;
                case 3:
                    RenderBars(surface, x, y, w, h, _Data.TotalMsPerBucket, v => FmtMs(v), Color.FromPalette(5), muted);
                    break;
                case 4:
                    RenderBars(surface, x, y, w, h, _Data.StreamMsPerBucket, v => FmtMs(v), Color.FromPalette(3), muted);
                    break;
                case 6:
                    RenderHeatmap(surface, x, y, w, h, muted);
                    break;
                default:
                    RenderModels(surface, x, y, w, h, muted);
                    break;
            }
        }

        // The grid shape per range. Day is transposed (columns are hours, rows are quarter-hours) so it
        // reads as a day timeline. Month is a Monday-first weekday calendar, so its row count depends on
        // how many day buckets the window produced (34-36, padded to whole weeks when drawn).
        private void HeatmapShape(int bucketCount, out int rows, out int columns, out int minutesPerCell)
        {
            switch (_Range)
            {
                case UsageRange.Hour: rows = 4; columns = 15; minutesPerCell = 1; break;
                case UsageRange.Day: rows = 4; columns = 24; minutesPerCell = 15; break;
                case UsageRange.Week: rows = 7; columns = 12; minutesPerCell = 120; break;
                default: rows = (Math.Max(bucketCount, 1) + 6) / 7; columns = 7; minutesPerCell = 1440; break;
            }
        }

        // Maps a grid position to its series index: row-major everywhere except the transposed day view.
        private int HeatmapIndex(int row, int column, int columns)
        {
            return _Range == UsageRange.Day ? (column * 4) + row : (row * columns) + column;
        }

        // Sparse column labels (empty string = unlabeled) chosen so adjacent labels never collide.
        private string HeatmapColumnLabel(int column, List<long> timestamps)
        {
            switch (_Range)
            {
                case UsageRange.Hour:
                    return column % 5 == 0 ? "+" + column.ToString(CultureInfo.InvariantCulture) + "m" : string.Empty;
                case UsageRange.Day:
                    return column % 3 == 0 ? column.ToString("00", CultureInfo.InvariantCulture) : string.Empty;
                case UsageRange.Week:
                    if (column % 3 != 0) return string.Empty;
                    return timestamps.Count > 0
                        ? DateTimeOffset.FromUnixTimeMilliseconds(timestamps[0]).AddHours(column * 2).ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture)
                        : "+" + (column * 2).ToString(CultureInfo.InvariantCulture) + "h";
                default:
                    return _WeekdayHeaders[column];
            }
        }

        private string HeatmapRowLabel(int row, int columns, List<long> timestamps)
        {
            if (_Range == UsageRange.Day)
            {
                return ":" + (row * 15).ToString("00", CultureInfo.InvariantCulture);
            }

            int index = row * columns;
            if (index < timestamps.Count)
            {
                DateTimeOffset time = DateTimeOffset.FromUnixTimeMilliseconds(timestamps[index]);
                switch (_Range)
                {
                    case UsageRange.Hour: return time.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture);
                    case UsageRange.Week: return time.ToLocalTime().ToString("ddd", CultureInfo.InvariantCulture);
                    default: return time.UtcDateTime.ToString("MM/dd", CultureInfo.InvariantCulture); // month calendar days are UTC
                }
            }

            return index < _Data.BucketLabels.Count ? _Data.BucketLabels[index] : string.Empty;
        }

        private void RenderHeatmap(ISurface surface, int x, int y, int w, int h, CellStyle muted)
        {
            List<double> values = _Data.HeatmapTokensPerBucket.Count > 0 ? _Data.HeatmapTokensPerBucket : _Data.TokensPerBucket;
            List<long> timestamps = _Data.HeatmapBucketStartsUnixMs.Count > 0 ? _Data.HeatmapBucketStartsUnixMs : _Data.BucketStartsUnixMs;
            if (values.Count == 0 || h < 6 || w < 24)
            {
                surface.DrawText(x, y, Trim("No data in this range.", w), muted);
                return;
            }

            HeatmapShape(values.Count, out int rows, out int columns, out int minutes);
            int strideX = HeatCellWidth + HeatGapX;
            int strideY = (h - 4) >= (rows * 2) - 1 ? 2 : 1;  // a spacer row between cells when there is room
            int gridWidth = (columns * strideX) - HeatGapX;
            int gridLeft = x + HeatAxisWidth + Math.Max(0, (w - HeatAxisWidth - gridWidth) / 2);
            int labelWidth = Math.Min(9, gridLeft - 1 - x);   // row labels sit flush against the grid
            double max = 0;
            foreach (double value in values) max = Math.Max(max, value);

            surface.DrawText(x, y, _Range == UsageRange.Month ? "Week" : _Range == UsageRange.Week ? "Day" : "Time", muted);
            for (int c = 0; c < columns; c++)
            {
                string columnLabel = HeatmapColumnLabel(c, timestamps);
                if (columnLabel.Length == 0) continue;
                int labelX = gridLeft + (c * strideX);
                if (labelX >= x && labelX < x + w)
                {
                    surface.DrawText(labelX, y, Trim(columnLabel, x + w - labelX), muted);
                }
            }

            for (int row = 0; row < rows; row++)
            {
                int rowY = y + 1 + (row * strideY);
                if (rowY > y + h - 4) break;   // keep the footer rows clear on short screens
                if (labelWidth > 0)
                {
                    DrawRightAligned(surface, gridLeft - 1 - labelWidth, rowY, labelWidth, HeatmapRowLabel(row, columns, timestamps), muted);
                }

                for (int c = 0; c < columns; c++)
                {
                    int cellX = gridLeft + (c * strideX);
                    if (cellX + HeatCellWidth > x + w) break;   // clip whole cells at the right edge
                    int index = HeatmapIndex(row, c, columns);
                    if (index >= values.Count)
                    {
                        // Padding (for example the month's future days) gets a dim dot, never a real level.
                        surface.Set(cellX, rowY, Cell.Glyph("·", muted, 1));
                        continue;
                    }

                    double value = values[index];
                    // Square-root scale: token counts are heavy-tailed, and a linear ramp crushes
                    // everything below the peak into the dimmest level.
                    int level = value <= 0 || max <= 0 ? 0 : Math.Min(4, 1 + (int)(Math.Sqrt(value / max) * 3.0));
                    surface.Fill(new Rect(cellX, rowY, HeatCellWidth, 1), Cell.Blank(CellStyle.Default.WithBackground(_HeatColors[level])));
                }
            }

            int totalCells = _Range == UsageRange.Month ? values.Count : rows * columns;
            string interval = _Range == UsageRange.Month ? "1 day"
                : minutes < 60 ? minutes + " min"
                : minutes / 60 + " hour";
            surface.DrawText(x, y + h - 3, Trim(totalCells + " cells · " + interval + "/cell" + (_Range == UsageRange.Month ? " · UTC days" : ""), w), muted);

            int legendX = x;
            surface.DrawText(legendX, y + h - 2, "Less", muted);
            legendX += 5;
            foreach (Color color in _HeatColors)
            {
                if (legendX + HeatCellWidth <= x + w)
                {
                    surface.Fill(new Rect(legendX, y + h - 2, HeatCellWidth, 1), Cell.Blank(CellStyle.Default.WithBackground(color)));
                }

                legendX += 3;
            }

            surface.DrawText(legendX, y + h - 2, Trim("More · peak " + FmtTokens(max), Math.Max(0, x + w - legendX)), muted);
        }

        // A vertical bar chart over time with a labeled Y axis (left gutter) and several X-axis time ticks.
        private void RenderBars(ISurface surface, int x, int y, int w, int h, List<double> values, Func<double, string> fmt, Color color, CellStyle muted)
        {
            if (values.Count == 0 || w <= YAxisWidth + 2 || h < 3)
            {
                surface.DrawText(x, y, Trim("No data in this range.", w), muted);
                return;
            }

            double max = 0.0;
            foreach (double v in values)
            {
                if (v > max) max = v;
            }

            if (max <= 0.0)
            {
                surface.DrawText(x, y, Trim("No activity in this range.", w), muted);
                return;
            }

            int plotX = x + YAxisWidth;
            int plotW = w - YAxisWidth;
            int plotH = h - 1; // reserve the bottom row for X labels
            CellStyle barStyle = CellStyle.Default.WithForeground(color);

            // Y axis: labels at top (max), middle, and bottom (0), with a divider column.
            DrawRightAligned(surface, x, y, YAxisWidth - 1, fmt(max), muted);
            DrawRightAligned(surface, x, y + (plotH / 2), YAxisWidth - 1, fmt(max / 2.0), muted);
            DrawRightAligned(surface, x, y + plotH - 1, YAxisWidth - 1, fmt(0), muted);
            for (int row = 0; row < plotH; row++)
            {
                surface.Set(x + YAxisWidth - 1, y + row, Cell.Glyph("│", muted, 1));
            }

            // One column per bucket, spread across the plot width; when there are more buckets than columns,
            // adjacent buckets are averaged into each column.
            int cols = Math.Min(values.Count, plotW);
            for (int c = 0; c < cols; c++)
            {
                double v = AggregateColumn(values, c, cols);
                double cells = (v / max) * plotH;
                int full = (int)cells;
                int rem = (int)Math.Round((cells - full) * 8.0);
                if (rem >= 8) { full++; rem = 0; }
                if (full > plotH) { full = plotH; rem = 0; }

                int barX = plotX + (cols <= 1 ? 0 : (int)Math.Round(c * (double)(plotW - 1) / (cols - 1)));
                for (int r = 0; r < full; r++)
                {
                    surface.Set(barX, y + plotH - 1 - r, Cell.Glyph("█", barStyle, 1));
                }

                if (rem > 0 && full < plotH)
                {
                    surface.Set(barX, y + plotH - 1 - full, Cell.Glyph(_Eighths[rem - 1], barStyle, 1));
                }
            }

            DrawXAxisLabels(surface, plotX, y + plotH, plotW, muted);
        }

        // Draws up to six evenly-spaced bucket labels along the X axis.
        private void DrawXAxisLabels(ISurface surface, int plotX, int row, int plotW, CellStyle muted)
        {
            List<string> labels = _Data.BucketLabels;
            if (labels.Count == 0)
            {
                return;
            }

            int desired = Math.Max(2, Math.Min(6, plotW / 10));
            int count = Math.Min(desired, labels.Count);
            for (int i = 0; i < count; i++)
            {
                int labelIndex = count == 1 ? labels.Count - 1 : (int)Math.Round(i * (double)(labels.Count - 1) / (count - 1));
                string label = labels[labelIndex];
                int pos = count == 1 ? 0 : (int)Math.Round(i * (double)(plotW - label.Length) / (count - 1));
                if (pos < 0) pos = 0;
                if (pos + label.Length > plotW) pos = Math.Max(0, plotW - label.Length);
                surface.DrawText(plotX + pos, row, label, muted);
            }
        }

        // A horizontal bar chart: BarChart draws the category (Y) labels and bars; a bottom line gives the
        // value (X) scale.
        private void RenderModels(ISurface surface, int x, int y, int w, int h, CellStyle muted)
        {
            if (_Data.ModelLabels.Count == 0)
            {
                surface.DrawText(x, y, Trim("No model usage in this range.", w), muted);
                return;
            }

            double max = 0.0;
            BarChart chart = new BarChart { Color = Color.FromPalette(4) };
            for (int i = 0; i < _Data.ModelLabels.Count; i++)
            {
                chart.Add(_Data.ModelLabels[i], _Data.ModelCosts[i]);
                if (_Data.ModelCosts[i] > max) max = _Data.ModelCosts[i];
            }

            int plotH = Math.Min(h - 1, Math.Max(1, chart.Count));
            BlitWidget(surface, chart, x, y, w, plotH);
            surface.DrawText(x, y + plotH, Trim("cost (USD) →  0 to " + FmtUsd(max), w), muted);
        }

        private static double AggregateColumn(List<double> values, int column, int columns)
        {
            if (values.Count <= columns)
            {
                return values[column];
            }

            int from = (int)((long)column * values.Count / columns);
            int to = (int)((long)(column + 1) * values.Count / columns);
            if (to <= from) to = from + 1;
            if (to > values.Count) to = values.Count;

            double sum = 0.0;
            for (int i = from; i < to; i++)
            {
                sum += values[i];
            }

            return sum / (to - from);
        }

        private static void BlitWidget(ISurface surface, IWidget widget, int x, int y, int w, int h)
        {
            if (w <= 0 || h <= 0)
            {
                return;
            }

            CellBuffer buffer = new CellBuffer(w, h);
            widget.Render(new BufferSurface(buffer));
            for (int row = 0; row < h; row++)
            {
                for (int col = 0; col < w; col++)
                {
                    surface.Set(x + col, y + row, buffer.Get(col, row));
                }
            }
        }

        private static void DrawRightAligned(ISurface surface, int x, int y, int width, string value, CellStyle style)
        {
            string trimmed = value.Length > width ? value.Substring(0, Math.Max(0, width)) : value;
            surface.DrawText(x + Math.Max(0, width - trimmed.Length), y, trimmed, style);
        }

        private static string RangeLabel(UsageRange range)
        {
            switch (range)
            {
                case UsageRange.Hour: return "Last hour";
                case UsageRange.Day: return "Last day";
                case UsageRange.Week: return "Last week";
                default: return "Last month";
            }
        }

        private static string FmtTokens(double value)
        {
            if (value >= 1_000_000) return (value / 1_000_000.0).ToString("0.#", CultureInfo.InvariantCulture) + "M";
            if (value >= 1000) return (value / 1000.0).ToString("0.#", CultureInfo.InvariantCulture) + "k";
            return value.ToString("0", CultureInfo.InvariantCulture);
        }

        private static string FmtUsd(double value)
        {
            return "$" + value.ToString(value >= 100 ? "0" : "0.00", CultureInfo.InvariantCulture);
        }

        private static string FmtMs(double value)
        {
            if (value >= 1000) return (value / 1000.0).ToString("0.#", CultureInfo.InvariantCulture) + "s";
            return value.ToString("0", CultureInfo.InvariantCulture);
        }

        private static string Trim(string text, int width)
        {
            if (width <= 0)
            {
                return string.Empty;
            }

            string value = text ?? string.Empty;
            return value.Length <= width ? value : value.Substring(0, Math.Max(0, width - 1)) + "…";
        }

        #endregion
    }
}
