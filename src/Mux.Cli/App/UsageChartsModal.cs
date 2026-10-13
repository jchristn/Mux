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

        // Eighth-block glyphs (U+2581..U+2588) for sub-cell bar heights.
        private static readonly string[] _Eighths = { "▁", "▂", "▃", "▄", "▅", "▆", "▇", "█" };

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
        private int _HeatmapRowOffset;
        private int _HeatmapVisibleRows = 1;
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

            if (_Page == 6 && (key.Code == KeyCode.PageDown || key.Code == KeyCode.PageUp))
            {
                HeatmapShape(out int rows, out _, out _);
                int step = key.Code == KeyCode.PageDown ? _HeatmapVisibleRows : -_HeatmapVisibleRows;
                _HeatmapRowOffset = Math.Clamp(_HeatmapRowOffset + step, 0, Math.Max(0, rows - _HeatmapVisibleRows));
                return true;
            }

            if (key.Rune >= '1' && key.Rune < '1' + _Ranges.Length)
            {
                _Range = _Ranges[key.Rune - '1'];
                _HeatmapRowOffset = 0;
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

        private void HeatmapShape(out int rows, out int columns, out int minutesPerCell)
        {
            switch (_Range)
            {
                case UsageRange.Hour: rows = 4; columns = 15; minutesPerCell = 1; break;
                case UsageRange.Day: rows = 24; columns = 4; minutesPerCell = 15; break;
                case UsageRange.Week: rows = 7; columns = 12; minutesPerCell = 120; break;
                default: rows = 30; columns = 2; minutesPerCell = 720; break;
            }
        }

        private void RenderHeatmap(ISurface surface, int x, int y, int w, int h, CellStyle muted)
        {
            List<double> values = _Data.HeatmapTokensPerBucket.Count > 0 ? _Data.HeatmapTokensPerBucket : _Data.TokensPerBucket;
            List<long> timestamps = _Data.HeatmapBucketStartsUnixMs.Count > 0 ? _Data.HeatmapBucketStartsUnixMs : _Data.BucketStartsUnixMs;
            if (values.Count == 0 || h < 5 || w < 18)
            { surface.DrawText(x, y, Trim("No data in this range.", w), muted); return; }
            HeatmapShape(out int rows, out int columns, out int minutes);
            const int axisWidth = 12;
            int plotX = x + axisWidth, plotWidth = w - axisWidth;
            int plotHeight = h - 4;
            _HeatmapVisibleRows = Math.Min(rows, plotHeight);
            _HeatmapRowOffset = Math.Clamp(_HeatmapRowOffset, 0, rows - _HeatmapVisibleRows);
            double max = 0;
            foreach (double value in values) max = Math.Max(max, value);
            Color[] colors = { Color.FromRgb(45, 48, 52), Color.FromRgb(14, 68, 41), Color.FromRgb(0, 109, 50), Color.FromRgb(38, 166, 65), Color.FromRgb(57, 211, 83) };
            surface.DrawText(x, y, "Row start", muted);
            for (int c = 0; c < columns; c++)
            {
                int offset = c * minutes;
                string label;
                if (_Range == UsageRange.Day) label = ":" + offset.ToString("00", CultureInfo.InvariantCulture);
                else if ((_Range == UsageRange.Week || _Range == UsageRange.Month) && timestamps.Count > 0)
                    label = DateTimeOffset.FromUnixTimeMilliseconds(timestamps[0]).AddMinutes(offset).ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture);
                else label = offset < 60 ? "+" + offset + "m" : "+" + offset / 60 + "h";
                int left = c * plotWidth / columns, right = (c + 1) * plotWidth / columns;
                surface.DrawText(plotX + left, y, Trim(label, right - left), muted);
            }
            for (int visibleRow = 0; visibleRow < _HeatmapVisibleRows; visibleRow++)
            {
                int row = visibleRow + _HeatmapRowOffset;
                int top = visibleRow * plotHeight / _HeatmapVisibleRows;
                int bottom = (visibleRow + 1) * plotHeight / _HeatmapVisibleRows;
                int index = row * columns;
                string rowLabel = "Row " + (row + 1);
                if (index < timestamps.Count)
                {
                    DateTimeOffset time = DateTimeOffset.FromUnixTimeMilliseconds(timestamps[index]).ToLocalTime();
                    rowLabel = time.ToString(_Range == UsageRange.Hour || _Range == UsageRange.Day ? "HH:mm" : "ddd MM/dd", CultureInfo.InvariantCulture);
                }
                else if (index < _Data.BucketLabels.Count) rowLabel = _Data.BucketLabels[index];
                surface.DrawText(x, y + 1 + top, Trim(rowLabel, axisWidth - 2), muted);
                for (int c = 0; c < columns; c++)
                {
                    int i = index + c;
                    double value = i < values.Count ? values[i] : 0;
                    int level = value <= 0 ? 0 : Math.Min(4, 1 + (int)(value / Math.Max(1, max) * 3));
                    int left = c * plotWidth / columns, right = (c + 1) * plotWidth / columns;
                    int gapX = right - left > 2 ? 1 : 0, gapY = bottom - top > 1 ? 1 : 0;
                    surface.Fill(new Rect(plotX + left, y + 1 + top, right - left - gapX, bottom - top - gapY),
                        Cell.Blank(CellStyle.Default.WithBackground(colors[level])));
                }
            }
            string interval = minutes < 60 ? minutes + " min" : minutes / 60 + " hour";
            surface.DrawText(x, y + h - 3, Trim(rows * columns + " spots · " + interval + "/spot · rows " + (_HeatmapRowOffset + 1) + "–" + (_HeatmapRowOffset + _HeatmapVisibleRows) + "/" + rows + " · PgUp/PgDn scroll", w), muted);
            surface.DrawText(x, y + h - 2, Trim("Oldest → newest across rows · Less ░▒▓█ More · peak " + FmtTokens(max), w), muted);
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
