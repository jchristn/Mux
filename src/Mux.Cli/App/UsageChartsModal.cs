namespace Mux.Cli.App
{
    using System;
    using System.Collections.Generic;
    using TUIKit;
    using TUIKit.Input;
    using TUIKit.Modals;
    using TUIKit.Widgets;

    /// <summary>
    /// A modal that renders usage telemetry as charts rather than plain text: KPI header lines, a per-day
    /// tokens line chart and cost bar chart over the trailing window, and a "top models by cost" bar chart.
    /// It uses only the chart widgets available in the pinned TUIKit release (<see cref="LineChart"/>,
    /// <see cref="BarChart"/>); percentile-distribution ("candlestick") views await the widgets planned in
    /// TUIKit's ADDITIONAL_GRAPHS.md. The caller supplies a fully-populated <see cref="UsageChartData"/> so
    /// the modal performs no I/O. Enter or Escape closes it.
    /// </summary>
    public sealed class UsageChartsModal : Modal
    {
        #region Private-Members

        private const int PadX = 2;
        private const int PadY = 1;

        private readonly string _Title;
        private readonly UsageChartData _Data;
        private readonly LineChart? _TokensChart;
        private readonly BarChart? _CostChart;
        private readonly BarChart? _ModelsChart;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="UsageChartsModal"/> class.
        /// </summary>
        /// <param name="title">The box title.</param>
        /// <param name="data">The pre-computed usage data to chart. Required.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="data"/> is null.</exception>
        public UsageChartsModal(string title, UsageChartData data)
        {
            _Title = title ?? "Usage";
            _Data = data ?? throw new ArgumentNullException(nameof(data));

            if (_Data.TokensPerDay.Count > 0)
            {
                _TokensChart = new LineChart(_Data.TokensPerDay) { Color = Color.FromPalette(6) };
            }

            if (_Data.CostPerDay.Count > 0)
            {
                _CostChart = new BarChart { Color = Color.FromPalette(2) };
                for (int i = 0; i < _Data.CostPerDay.Count; i++)
                {
                    string label = i < _Data.DayLabels.Count ? _Data.DayLabels[i] : string.Empty;
                    _CostChart.Add(label, _Data.CostPerDay[i]);
                }
            }

            if (_Data.ModelLabels.Count > 0)
            {
                _ModelsChart = new BarChart { Color = Color.FromPalette(4) };
                for (int i = 0; i < _Data.ModelLabels.Count; i++)
                {
                    _ModelsChart.Add(_Data.ModelLabels[i], _Data.ModelCosts[i]);
                }
            }
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

            return true;
        }

        /// <inheritdoc/>
        public override void Render(ISurface surface)
        {
            if (surface == null) throw new ArgumentNullException(nameof(surface));

            int screenWidth = surface.Size.Width;
            int screenHeight = surface.Size.Height;

            int contentWidth = Math.Max(8, Math.Min(72, screenWidth - 2 - (2 * PadX)));
            int boxWidth = Math.Min(screenWidth, contentWidth + 2 + (2 * PadX));
            int boxHeight = Math.Min(screenHeight, Math.Max(10, screenHeight - 2));

            int boxX = Math.Max(0, (screenWidth - boxWidth) / 2);
            int boxY = Math.Max(0, (screenHeight - boxHeight) / 2);
            Rect box = new Rect(boxX, boxY, boxWidth, boxHeight);

            surface.Fill(box, Cell.Blank(CellStyle.Default));
            surface.DrawBox(box, CellStyle.Default.WithForeground(Color.FromPalette(8)), _Title);

            int contentX = boxX + 1 + PadX;
            int firstRow = boxY + 1 + PadY;
            int lastContentRow = boxY + boxHeight - 2 - PadY; // leave the final interior row for the hint
            int y = firstRow;

            CellStyle text = CellStyle.Default;
            CellStyle heading = CellStyle.Default.WithForeground(Color.FromPalette(8));

            foreach (string line in _Data.HeaderLines)
            {
                if (y > lastContentRow) break;
                surface.DrawText(contentX, y, Trim(line, contentWidth), text);
                y++;
            }

            y = DrawSection(surface, "Tokens per day", _TokensChart, 5, contentX, y, contentWidth, lastContentRow, heading);
            y = DrawSection(surface, "Cost per day", _CostChart, EstimateBarHeight(_CostChart), contentX, y, contentWidth, lastContentRow, heading);
            y = DrawSection(surface, "Top models by cost", _ModelsChart, EstimateBarHeight(_ModelsChart), contentX, y, contentWidth, lastContentRow, heading);

            int hintRow = boxY + boxHeight - 1 - PadY;
            surface.DrawText(contentX, hintRow, Trim("Enter / Esc to close", contentWidth), heading);
        }

        #endregion

        #region Private-Methods

        // Renders a titled chart section starting at row y, returns the next free row. Skips the section
        // entirely (and consumes no rows) when the widget is null or there is no vertical room left.
        private static int DrawSection(ISurface surface, string title, IWidget? widget, int chartHeight, int contentX, int y, int contentWidth, int lastContentRow, CellStyle heading)
        {
            if (widget == null)
            {
                return y;
            }

            // A section needs a blank spacer, a heading row, and at least one chart row to be worth drawing.
            if (y + 3 > lastContentRow)
            {
                return y;
            }

            y++; // spacer
            surface.DrawText(contentX, y, Trim(title, contentWidth), heading);
            y++;

            int available = Math.Max(1, Math.Min(chartHeight, lastContentRow - y + 1));
            CellBuffer buffer = new CellBuffer(contentWidth, available);
            widget.Render(new BufferSurface(buffer));
            for (int row = 0; row < available; row++)
            {
                for (int x = 0; x < contentWidth; x++)
                {
                    surface.Set(contentX + x, y + row, buffer.Get(x, row));
                }
            }

            return y + available;
        }

        // A horizontal bar chart draws one row per bar; cap the height so a long breakdown cannot fill the box.
        private static int EstimateBarHeight(BarChart? chart)
        {
            if (chart == null)
            {
                return 0;
            }

            return Math.Min(8, Math.Max(1, chart.Count));
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
