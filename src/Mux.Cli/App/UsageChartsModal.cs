namespace Mux.Cli.App
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using Mux.Core.Telemetry;
    using TUIKit;
    using TUIKit.Input;
    using TUIKit.Modals;

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

        private static readonly Color[] _ModelPalette =
        {
            Color.FromRgb(110, 168, 230), Color.FromRgb(91, 192, 174),
            Color.FromRgb(224, 179, 102), Color.FromRgb(177, 151, 218),
            Color.FromRgb(223, 142, 153), Color.FromRgb(137, 190, 214),
            Color.FromRgb(180, 196, 128), Color.FromRgb(205, 165, 128)
        };
        private readonly Dictionary<string, Color> _ModelColors = new(StringComparer.Ordinal);
        private int _BarBucketOffset;
        private int _VisibleBuckets = 1;
        private int _LegendOffset;
        private int _LegendVisibleRows = 1;

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
        private int _HeatmapRows = 1;
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
                _BarBucketOffset = 0;
                _Page = (_Page + 1) % _PageTitles.Length;
                return true;
            }

            if (key.Code == KeyCode.Left)
            {
                _BarBucketOffset = 0;
                _Page = (_Page + _PageTitles.Length - 1) % _PageTitles.Length;
                return true;
            }

            if (_Page < 5 && (key.Code == KeyCode.PageDown || key.Code == KeyCode.PageUp))
            {
                _BarBucketOffset = Math.Clamp(_BarBucketOffset + (key.Code == KeyCode.PageDown ? _VisibleBuckets : -_VisibleBuckets), 0, Math.Max(0, _Data.BucketLabels.Count - _VisibleBuckets));
                return true;
            }
            if (_Page < 5 && (key.Code == KeyCode.Down || key.Code == KeyCode.Up))
            {
                _LegendOffset = Math.Clamp(_LegendOffset + (key.Code == KeyCode.Down ? 1 : -1), 0, Math.Max(0, _Data.ModelBuckets.Count - _LegendVisibleRows));
                return true;
            }

            if (_Page == 6 && (key.Code == KeyCode.PageDown || key.Code == KeyCode.PageUp))
            {
                int rows = _HeatmapRows;
                int step = key.Code == KeyCode.PageDown ? _HeatmapVisibleRows : -_HeatmapVisibleRows;
                _HeatmapRowOffset = Math.Clamp(_HeatmapRowOffset + step, 0, Math.Max(0, rows - _HeatmapVisibleRows));
                return true;
            }

            if (key.Rune >= '1' && key.Rune < '1' + _Ranges.Length)
            {
                _Range = _Ranges[key.Rune - '1'];
                _HeatmapRowOffset = 0;
                _BarBucketOffset = 0;
                _LegendOffset = 0;
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
                foreach (string model in _Data.ModelBuckets.Keys.Concat(_Data.ModelLabels).Distinct().OrderBy(k => k, StringComparer.Ordinal)) ModelColor(model);
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
                    RenderBars(surface, x, y, w, h, _Data.TokensPerBucket, v => FmtTokens(v), muted);
                    break;
                case 1:
                    RenderBars(surface, x, y, w, h, _Data.CostPerBucket, v => FmtUsd(v), muted);
                    break;
                case 2:
                    RenderBars(surface, x, y, w, h, _Data.TtftMsPerBucket, v => FmtMs(v), muted);
                    break;
                case 3:
                    RenderBars(surface, x, y, w, h, _Data.TotalMsPerBucket, v => FmtMs(v), muted);
                    break;
                case 4:
                    RenderBars(surface, x, y, w, h, _Data.StreamMsPerBucket, v => FmtMs(v), muted);
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
            if (values.Count == 0 || h < 8 || w < 18)
            { surface.DrawText(x, y, Trim("No data in this range.", w), muted); return; }
            HeatmapShape(out int originalRows, out int originalColumns, out int minutes);
            int count = originalRows * originalColumns;
            const int axisWidth = 12;
            int plotX = x + axisWidth, plotWidth = w - axisWidth, plotHeight = h - 6;
            HeatmapLayout(count, plotWidth, plotHeight, out int rows, out int columns, out int cellHeight);
            int cellWidth = 2 * cellHeight; // Terminal characters are roughly twice as tall as they are wide.
            int pitchX = cellWidth + 1, pitchY = cellHeight + 1;
            _HeatmapRows = rows;
            _HeatmapVisibleRows = Math.Min(rows, Math.Max(1, (plotHeight + 1) / pitchY));
            _HeatmapRowOffset = Math.Clamp(_HeatmapRowOffset, 0, rows - _HeatmapVisibleRows);
            double max = values.Count > 0 ? values.Max() : 0;
            Color[] colors = { Color.FromRgb(45, 48, 52), Color.FromRgb(14, 68, 41), Color.FromRgb(0, 109, 50), Color.FromRgb(38, 166, 65), Color.FromRgb(57, 211, 83) };
            surface.DrawText(x, y, Trim("Row start (local) · column offset → · tokens per spot", w), muted);
            int tickStride = Math.Max(1, (6 + pitchX - 1) / pitchX);
            for (int c = 0; c < columns; c += tickStride)
            {
                int offset = c * minutes;
                string label = offset < 60 ? "+" + offset + "m" : "+" + offset / 60 + "h" + (offset % 60 == 0 ? "" : (offset % 60).ToString("00", CultureInfo.InvariantCulture));
                surface.DrawText(plotX + c * pitchX, y + 1, Trim(label, Math.Min(tickStride * pitchX - 1, plotWidth - c * pitchX)), muted);
            }
            for (int visibleRow = 0; visibleRow < _HeatmapVisibleRows; visibleRow++)
            {
                int row = visibleRow + _HeatmapRowOffset, index = row * columns;
                int top = y + 2 + visibleRow * pitchY;
                string rowLabel = "Row " + (row + 1);
                if (index < timestamps.Count)
                {
                    DateTimeOffset time = DateTimeOffset.FromUnixTimeMilliseconds(timestamps[index]).ToLocalTime();
                    rowLabel = time.ToString(_Range == UsageRange.Hour || _Range == UsageRange.Day ? "HH:mm" : "MM/dd HH:mm", CultureInfo.InvariantCulture);
                }
                else if (index < _Data.BucketLabels.Count) rowLabel = _Data.BucketLabels[index];
                surface.DrawText(x, top, Trim(rowLabel, axisWidth - 1), muted);
                for (int c = 0; c < columns; c++)
                {
                    int i = index + c;
                    double value = i < values.Count ? values[i] : 0;
                    int level = value <= 0 ? 0 : Math.Min(4, 1 + (int)(value / Math.Max(1, max) * 3));
                    CellStyle style = CellStyle.Default.WithBackground(colors[level]);
                    int left = plotX + c * pitchX;
                    surface.Fill(new Rect(left, top, cellWidth, cellHeight), Cell.Blank(style));
                    if (cellHeight >= 3)
                    {
                        string label = Trim(FmtTokens(value), cellWidth);
                        Color foreground = level >= 3 ? Color.FromRgb(12, 35, 21) : Color.FromRgb(225, 238, 228);
                        surface.DrawText(left + Math.Max(0, (cellWidth - label.Length) / 2), top + cellHeight / 2, label, style.WithForeground(foreground));
                    }
                }
            }
            string interval = minutes < 60 ? minutes + " min" : minutes / 60 + " hour";
            string navigation = _HeatmapVisibleRows < rows ? " · PgUp/PgDn scroll" : "";
            surface.DrawText(x, y + h - 4, Trim(count + " spots · " + interval + "/spot · rows " + (_HeatmapRowOffset + 1) + "–" + (_HeatmapRowOffset + _HeatmapVisibleRows) + "/" + rows + navigation, w), muted);
            surface.DrawText(x, y + h - 3, Trim("Oldest → newest across rows · peak " + FmtTokens(max) + " tokens", w), muted);
            surface.DrawText(x, y + h - 2, Trim("Tokens: 0", w), muted);
            for (int i = 0; i < colors.Length; i++)
            {
                int left = x + 10 + i * 3;
                if (left + 2 > x + w) break;
                surface.Fill(new Rect(left, y + h - 2, 2, 1), Cell.Blank(CellStyle.Default.WithBackground(colors[i])));
            }
            if (w > 26) surface.DrawText(x + 26, y + h - 2, Trim(FmtTokens(max) + " (peak)", w - 26), muted);
        }

        private static void HeatmapLayout(int count, int width, int height, out int rows, out int columns, out int cellHeight)
        {
            columns = 1;
            for (int c = 1; c <= count && c * 3 - 1 <= width; c++)
                if (count % c == 0) columns = c;
            rows = count / columns;
            cellHeight = 1;
            double bestFill = -1;
            for (int c = 1; c <= count; c++)
            {
                if (count % c != 0) continue;
                int r = count / c;
                int size = Math.Min((width - c + 1) / (2 * c), (height - r + 1) / r);
                if (size < 1) continue;
                double fill = Math.Min((c * (2 * size + 1) - 1) / (double)width, (r * (size + 1) - 1) / (double)height);
                if (size > cellHeight || (size == cellHeight && fill > bestFill))
                {
                    columns = c; rows = r; cellHeight = size; bestFill = fill;
                }
            }
        }

        // Totals stack; per-model latency averages are grouped, never summed.
        private void RenderBars(ISurface surface, int x, int y, int w, int h, List<double> values, Func<double, string> fmt, CellStyle muted)
        {
            if (values.Count == 0 || w <= YAxisWidth + 2 || h < 7)
            {
                surface.DrawText(x, y, Trim("No data in this range.", w), muted);
                return;
            }
            string unit = _Page == 0 ? "Tokens" : _Page == 1 ? "Cost (USD)" : "Mean latency (ms / s)";
            surface.DrawText(x, y, Trim(unit + " ↑ · Time (local) → · Legend: color = model", w), muted);
            y++; h--;
            List<string> models = _Data.ModelBuckets.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();
            if (models.Count == 0) models.Add("All models");
            bool stacked = _Page < 2;
            int plotX = x + YAxisWidth, plotW = w - YAxisWidth;
            int slot = stacked ? 1 : models.Count + 1;
            if (plotW < slot)
            {
                surface.DrawText(x, y, Trim("Widen terminal to compare " + models.Count + " models.", w), muted);
                return;
            }
            _LegendVisibleRows = Math.Min(models.Count, Math.Max(1, h / 4));
            int plotH = h - _LegendVisibleRows - 3;
            _VisibleBuckets = Math.Min(values.Count, Math.Max(1, plotW / slot));
            _BarBucketOffset = Math.Clamp(_BarBucketOffset, 0, Math.Max(0, values.Count - _VisibleBuckets));
            _LegendOffset = Math.Clamp(_LegendOffset, 0, Math.Max(0, models.Count - _LegendVisibleRows));
            double Value(int model, int bucket)
            {
                if (_Data.ModelBuckets.Count == 0) return values[bucket];
                List<UsageMetrics> metrics = _Data.ModelBuckets[models[model]];
                return bucket < metrics.Count ? MetricValue(metrics[bucket]) : 0;
            }
            // Keep the scale stable while paging buckets.
            double max = 0;
            for (int bucket = 0; bucket < values.Count; bucket++)
            {
                double total = 0;
                for (int m = 0; m < models.Count; m++)
                {
                    double value = Value(m, bucket);
                    total += value;
                    if (!stacked) max = Math.Max(max, value);
                }
                if (stacked) max = Math.Max(max, total);
            }
            DrawRightAligned(surface, x, y, YAxisWidth - 1, fmt(max), muted);
            DrawRightAligned(surface, x, y + plotH / 2, YAxisWidth - 1, fmt(max / 2), muted);
            DrawRightAligned(surface, x, y + plotH - 1, YAxisWidth - 1, fmt(0), muted);
            for (int row = 0; row < plotH; row++)
                surface.Set(plotX - 1, y + row, Cell.Glyph("│", muted, 1));

            for (int c = 0; c < _VisibleBuckets && max > 0; c++)
            {
                int bucket = _BarBucketOffset + c;
                int left = plotX + c * plotW / _VisibleBuckets;
                int right = plotX + (c + 1) * plotW / _VisibleBuckets;
                if (stacked)
                {
                    // Quantize cumulative boundaries, so rounding cannot increase the stack total.
                    int lower = 0;
                    double cumulative = 0;
                    Color previousColor = ModelColor(models[0]);
                    for (int m = 0; m < models.Count; m++)
                    {
                        cumulative += Value(m, bucket);
                        int upper = Math.Clamp((int)Math.Round(cumulative / max * plotH * 8), 0, plotH * 8);
                        DrawSegment(surface, left, y + plotH - 1, Math.Max(1, right - left - 1), lower, upper, ModelColor(models[m]), previousColor);
                        if (upper > lower) previousColor = ModelColor(models[m]);
                        lower = upper;
                    }
                }
                else
                {
                    for (int m = 0; m < models.Count; m++)
                    {
                        int upper = Math.Clamp((int)Math.Round(Value(m, bucket) / max * plotH * 8), 0, plotH * 8);
                        DrawSegment(surface, left + m, y + plotH - 1, 1, 0, upper, ModelColor(models[m]));
                    }
                }
            }
            if (max <= 0) surface.DrawText(plotX, y + plotH / 2, Trim("No activity in this range.", plotW), muted);
            int ticks = Math.Min(_VisibleBuckets, Math.Max(1, plotW / 12));
            for (int t = 0; t < ticks; t++)
            {
                int c = ticks == 1 ? 0 : t * (_VisibleBuckets - 1) / (ticks - 1);
                int bucket = _BarBucketOffset + c;
                if (bucket >= _Data.BucketLabels.Count) continue;
                string label = Trim(_Data.BucketLabels[bucket], Math.Min(12, plotW));
                int labelX = Math.Min(plotX + c * plotW / _VisibleBuckets, plotX + plotW - label.Length);
                surface.DrawText(labelX, y + plotH, label, muted);
            }
            string hint = (stacked ? "Stacked by model" : "Per-model averages") + " · buckets " + (_BarBucketOffset + 1) + "–" + (_BarBucketOffset + _VisibleBuckets) + "/" + values.Count;
            if (_VisibleBuckets < values.Count) hint += " · PgUp/PgDn";
            if (_LegendVisibleRows < models.Count) hint += " · ↑/↓ legend";
            surface.DrawText(x, y + plotH + 1, Trim(hint, w), muted);
            for (int row = 0; row < _LegendVisibleRows; row++)
            {
                string model = models[_LegendOffset + row];
                surface.DrawText(x, y + plotH + 2 + row, "■", CellStyle.Default.WithForeground(ModelColor(model)));
                surface.DrawText(x + 2, y + plotH + 2 + row, Trim(ModelLabel(model), w - 2), CellStyle.Default);
            }
        }

        private double MetricValue(UsageMetrics metrics) => _Page switch
        {
            0 => metrics.TotalTokens,
            1 => metrics.CostUsd,
            2 => metrics.AvgTtftMs,
            3 => metrics.AvgTotalMs,
            _ => metrics.AvgStreamMs
        };

        private static string ModelLabel(string model) => string.IsNullOrWhiteSpace(model) ? "Unknown model" : model;

        private Color ModelColor(string model)
        {
            if (!_ModelColors.TryGetValue(model, out Color color))
            {
                color = _ModelPalette[_ModelColors.Count % _ModelPalette.Length];
                _ModelColors[model] = color;
            }
            return color;
        }

        private static void DrawSegment(ISurface surface, int x, int bottom, int width, int lower, int upper, Color color, Color? previousColor = null)
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
                    string glyph = _Eighths[top - 1];
                    if (start > 0 && top == 8)
                    {
                        Color previous = previousColor ?? color;
                        style = CellStyle.Default.WithForeground(previous).WithBackground(color);
                        glyph = _Eighths[start - 1];
                    }
                    surface.Set(x + dx, bottom - cell, Cell.Glyph(glyph, style, 1));
                }
            }
        }


        // The cost ranking uses the same per-model colors as the time series.
        private void RenderModels(ISurface surface, int x, int y, int w, int h, CellStyle muted)
        {
            if (_Data.ModelLabels.Count == 0)
            {
                surface.DrawText(x, y, Trim("No model usage in this range.", w), muted);
                return;
            }

            surface.DrawText(x, y, Trim("Model ↓ · Cost (USD) → · Legend: color = model", w), muted);
            y++; h--;
            double max = _Data.ModelCosts.Count > 0 ? _Data.ModelCosts.Max() : 0;
            int labelWidth = Math.Min(32, Math.Max(8, w / 3));
            int barWidth = Math.Max(1, w - labelWidth - 12);
            for (int i = 0; i < Math.Min(_Data.ModelLabels.Count, h - 1); i++)
            {
                string model = _Data.ModelLabels[i];
                double cost = i < _Data.ModelCosts.Count ? _Data.ModelCosts[i] : 0;
                surface.DrawText(x, y + i, "■", CellStyle.Default.WithForeground(ModelColor(model)));
                surface.DrawText(x + 2, y + i, Trim(ModelLabel(model), labelWidth - 3), CellStyle.Default);
                int cells = max > 0 ? (int)Math.Round(cost / max * barWidth) : 0;
                surface.DrawText(x + labelWidth, y + i, new string('█', cells), CellStyle.Default.WithForeground(ModelColor(model)));
                surface.DrawText(x + labelWidth + barWidth + 1, y + i, Trim(FmtUsd(cost), 10), muted);
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
