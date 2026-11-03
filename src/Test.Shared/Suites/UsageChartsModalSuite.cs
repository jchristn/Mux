namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Cli.App;
    using Mux.Core.Telemetry;
    using Touchstone.Core;
    using TUIKit;
    using TUIKit.Input;

    /// <summary>
    /// Touchstone suite for <see cref="UsageChartsModal"/>: the paged, range-aware usage view renders one
    /// chart per page with axis labels, navigates pages with the arrow keys, switches range with the number
    /// keys (re-querying its provider), surfaces provider errors, and closes on Enter/Escape.
    /// </summary>
    public static class UsageChartsModalSuite
    {
        private const string SuiteId = "UsageChartsModal";

        /// <summary>
        /// Builds the usage-charts-modal suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/> for the usage-charts-modal cases.</returns>
        public static TestSuiteDescriptor Create()
        {
            return new TestSuiteDescriptor(
                SuiteId,
                "Usage charts modal: paging, ranges, and axes",
                new List<TestCaseDescriptor>
                {
                    new TestCaseDescriptor(SuiteId, "FirstPageTokensWithAxes", "The first page shows tokens over time with X/Y axis labels", (CancellationToken ct) =>
                    {
                        UsageChartsModal modal = new UsageChartsModal("Usage", r => SampleData(), UsageRange.Day);
                        string rendered = Render(modal, 120, 40);

                        MuxAssert.Contains("Tokens over time", rendered, "page title");
                        MuxAssert.Contains("Last day", rendered, "range label");
                        MuxAssert.Contains("page 1/7", rendered, "page indicator");
                        MuxAssert.Contains("9k", rendered, "Y-axis max token label");
                        MuxAssert.Contains("Mon", rendered, "X-axis time label");
                        MuxAssert.Contains("Esc close", rendered, "footer hint");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor(SuiteId, "ArrowKeysPageThroughCharts", "Right arrow pages through cost, the three latency metrics, and models", (CancellationToken ct) =>
                    {
                        UsageChartsModal modal = new UsageChartsModal("Usage", r => SampleData(), UsageRange.Day);

                        modal.HandleKey(KeyEvent.Special(KeyCode.Right));
                        MuxAssert.Contains("Cost over time", Render(modal, 120, 40), "page 2 is cost");

                        modal.HandleKey(KeyEvent.Special(KeyCode.Right));
                        MuxAssert.Contains("Time to first token", Render(modal, 120, 40), "page 3 is TTFT");

                        modal.HandleKey(KeyEvent.Special(KeyCode.Right));
                        MuxAssert.Contains("Total latency", Render(modal, 120, 40), "page 4 is total latency");

                        modal.HandleKey(KeyEvent.Special(KeyCode.Right));
                        MuxAssert.Contains("Streaming time", Render(modal, 120, 40), "page 5 is streaming time");

                        modal.HandleKey(KeyEvent.Special(KeyCode.Right));
                        MuxAssert.Contains("Top models by cost", Render(modal, 120, 40), "page 6 is models");
                        modal.HandleKey(KeyEvent.Special(KeyCode.Right));
                        MuxAssert.Contains("Token activity heatmap", Render(modal, 120, 40), "page 7 is heatmap");
                        MuxAssert.Contains("peak 9k", Render(modal, 120, 40), "heatmap scale");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor(SuiteId, "HeatmapIntensityAndEmptyRange", "Heatmap colors distinguish zero, low, and peak usage and handle empty ranges", (CancellationToken ct) =>
                    {
                        UsageChartData data = new UsageChartData();
                        data.TokensPerBucket.AddRange(new double[] { 0, 1, 100 });
                        UsageChartsModal modal = new UsageChartsModal("Usage", r => data, UsageRange.Day);
                        modal.HandleKey(KeyEvent.Special(KeyCode.Left));
                        CellBuffer buffer = new CellBuffer(120, 40);
                        modal.Render(new BufferSurface(buffer));
                        HashSet<Color> colors = new HashSet<Color>();
                        for (int y = 0; y < 40; y++)
                            for (int x = 0; x < 120; x++) colors.Add(buffer.Get(x, y).Style.Background);
                        MuxAssert.IsTrue(colors.Contains(Color.FromRgb(45, 48, 52)), "zero usage cell");
                        MuxAssert.IsTrue(colors.Contains(Color.FromRgb(14, 68, 41)), "low usage cell");
                        MuxAssert.IsTrue(colors.Contains(Color.FromRgb(57, 211, 83)), "peak usage cell");
                        // The day grid is transposed (columns = hours, rows = quarter-hours), centered,
                        // cells 2x1 with one-column gaps: bucket 0 at (28,6), bucket 1 at (28,8), bucket 2 at (28,10).
                        MuxAssert.AreEqual(Color.FromRgb(45, 48, 52), buffer.Get(28, 6).Style.Background, "zero series value maps to empty level");
                        MuxAssert.AreEqual(Color.FromRgb(14, 68, 41), buffer.Get(28, 8).Style.Background, "tiny series value maps to the lowest level (sqrt scale)");
                        MuxAssert.AreEqual(Color.FromRgb(57, 211, 83), buffer.Get(28, 10).Style.Background, "peak series value maps to the highest level");
                        UsageChartsModal empty = new UsageChartsModal("Usage", r => new UsageChartData(), UsageRange.Day);
                        empty.HandleKey(KeyEvent.Special(KeyCode.Left));
                        MuxAssert.Contains("No data in this range", Render(empty, 120, 40), "empty heatmap");
                        for (int height = 1; height <= 20; height++) Render(modal, 14, height);
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor(SuiteId, "HeatmapCellsStayFixedSquares", "Usage covers the screen while heatmap cells keep a fixed square size with one-column gaps", (CancellationToken ct) =>
                    {
                        foreach (int width in new[] { 40, 120, 200 })
                        {
                            UsageChartData data = new UsageChartData();
                            for (int i = 0; i < 96; i++) data.TokensPerBucket.Add(100);
                            UsageChartsModal modal = new UsageChartsModal("Usage", r => data, UsageRange.Day);
                            modal.HandleKey(KeyEvent.Special(KeyCode.Left));
                            CellBuffer buffer = new CellBuffer(width, 40);
                            new BufferSurface(buffer).Fill(new Rect(0, 0, width, 40), Cell.Glyph("!", CellStyle.Default, 1));
                            modal.Render(new BufferSurface(buffer));
                            MuxAssert.IsTrue(buffer.Get(0, 0).Grapheme != "!", "top left covered");
                            MuxAssert.IsTrue(buffer.Get(width - 1, 39).Grapheme != "!", "bottom right covered");

                            // Cells are 2 columns wide with a 1-column gap at every screen width: every
                            // horizontal peak-colored run is exactly 2 cells, and runs start 3 apart.
                            Color peak = Color.FromRgb(57, 211, 83);
                            int runs = 0;
                            for (int y = 0; y < 40; y++)
                            {
                                int runStart = -1;
                                for (int x = 0; x < width; x++)
                                {
                                    bool isPeak = peak.Equals(buffer.Get(x, y).Style.Background);
                                    if (isPeak && runStart < 0) runStart = x;
                                    if (!isPeak && runStart >= 0)
                                    {
                                        MuxAssert.AreEqual(2, x - runStart, "cell is exactly 2 columns wide at width " + width);
                                        runs++;
                                        runStart = -1;
                                    }
                                }
                            }
                            MuxAssert.IsTrue(runs >= 8, "grid cells rendered at width " + width);
                        }
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor(SuiteId, "HeatmapTimeAxesAndFixedCells", "Each timeframe renders a labeled fixed-cell grid; the month is a weekday calendar that needs no paging", (CancellationToken ct) =>
                    {
                        UsageChartsModal modal = new UsageChartsModal("Usage", range =>
                        {
                            UsageWindow window = range == UsageRange.Month
                                ? UsageWindow.ComputeHeatmapDays(1791870000000L)
                                : UsageWindow.ComputeHeatmap(range, 1791870000000L);
                            UsageChartData data = new UsageChartData();
                            for (int i = 0; i < window.BucketCount; i++)
                            {
                                data.TokensPerBucket.Add(100);
                                data.BucketStartsUnixMs.Add(window.FromUnixMs + i * window.BucketMs);
                            }
                            return data;
                        }, UsageRange.Day);
                        modal.HandleKey(KeyEvent.Special(KeyCode.Left));
                        string[] cells = { "60 cells", "96 cells", "84 cells" };
                        string[] gutters = { "Time", "Time", "Day", "Week" };
                        for (int i = 0; i < 4; i++)
                        {
                            modal.HandleKey(KeyEvent.Char((char)('1' + i)));
                            string rendered = Render(modal, 120, 40);
                            if (i < 3)
                            {
                                MuxAssert.Contains(cells[i], rendered, "fixed cell count for timeframe " + i);
                            }
                            else
                            {
                                MuxAssert.Contains("cells · 1 day/cell · UTC days", rendered, "month calendar footer");
                                foreach (string weekday in new[] { "Mo", "We", "Su" })
                                    MuxAssert.Contains(weekday, rendered, "weekday column " + weekday);
                            }
                            MuxAssert.Contains(gutters[i], rendered, "row-axis label for timeframe " + i);
                            if (i == 1)
                            {
                                foreach (string anchor in new[] { ":00", ":15", ":30", ":45" })
                                    MuxAssert.Contains(anchor, rendered, "quarter-hour row label");
                            }
                        }
                        string shortScreen = Render(modal, 120, 20);
                        modal.HandleKey(KeyEvent.Special(KeyCode.PageDown));
                        MuxAssert.AreEqual(shortScreen, Render(modal, 120, 20), "calendar fits the page; PgDn is inert");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor(SuiteId, "MonthHeatmapRendersWeekdayCalendar", "The month heatmap is a Monday-first calendar of UTC days on a fixed square grid", (CancellationToken ct) =>
                    {
                        long now = new DateTimeOffset(2026, 10, 13, 12, 6, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
                        UsageWindow window = UsageWindow.ComputeHeatmapDays(now);
                        UsageChartData data = new UsageChartData();
                        for (int i = 0; i < window.BucketCount; i++)
                        {
                            data.HeatmapTokensPerBucket.Add(100);
                            data.HeatmapBucketStartsUnixMs.Add(window.FromUnixMs + i * window.BucketMs);
                        }
                        UsageChartsModal modal = new UsageChartsModal("Usage", r => data, UsageRange.Month);
                        modal.HandleKey(KeyEvent.Special(KeyCode.Left));
                        string rendered = Render(modal, 120, 40);
                        MuxAssert.Contains(window.BucketCount + " cells · 1 day/cell · UTC days", rendered, "calendar footer");
                        foreach (string weekday in new[] { "Mo", "Tu", "We", "Th", "Fr", "Sa", "Su" })
                            MuxAssert.Contains(weekday, rendered, "weekday column " + weekday);
                        MuxAssert.Contains("Week", rendered, "row-axis label");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor(SuiteId, "HeatmapUsesClockAlignedSeries", "Day heatmap uses aligned buckets and keeps quarter-hour labels despite an off-boundary rolling series", (CancellationToken ct) =>
                    {
                        UsageChartData data = new UsageChartData();
                        long offBoundary = new DateTimeOffset(2026, 10, 13, 12, 6, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
                        UsageWindow aligned = UsageWindow.ComputeHeatmap(UsageRange.Day, offBoundary);
                        for (int i = 0; i < 96; i++)
                        {
                            data.TokensPerBucket.Add(9999);
                            data.BucketStartsUnixMs.Add(offBoundary + i * 900000L);
                            data.HeatmapTokensPerBucket.Add(100);
                            data.HeatmapBucketStartsUnixMs.Add(aligned.FromUnixMs + i * aligned.BucketMs);
                        }
                        UsageChartsModal modal = new UsageChartsModal("Usage", r => data, UsageRange.Day);
                        modal.HandleKey(KeyEvent.Special(KeyCode.Left));
                        string rendered = Render(modal, 120, 40);
                        foreach (string anchor in new[] { ":00", ":15", ":30", ":45" }) MuxAssert.Contains(anchor, rendered, "quarter-hour row label " + anchor);
                        MuxAssert.Contains("peak 100", rendered, "aligned values used without relabeling rolling values");
                        MuxAssert.IsTrue(!rendered.Contains(":06"), "off-boundary rolling timestamps do not label heatmap");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor(SuiteId, "NumberKeysSwitchRange", "Number keys switch the range and re-query the provider", (CancellationToken ct) =>
                    {
                        UsageRange requested = UsageRange.Day;
                        UsageChartsModal modal = new UsageChartsModal("Usage", r => { requested = r; return SampleData(); }, UsageRange.Day);

                        modal.HandleKey(KeyEvent.Char('3')); // week
                        MuxAssert.AreEqual(UsageRange.Week, requested, "provider queried for the week range");
                        MuxAssert.Contains("Last week", Render(modal, 120, 40), "range label updated");

                        modal.HandleKey(KeyEvent.Char('1')); // hour
                        MuxAssert.AreEqual(UsageRange.Hour, requested, "provider queried for the hour range");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor(SuiteId, "ProviderErrorSurfaces", "A provider failure is shown rather than thrown", (CancellationToken ct) =>
                    {
                        UsageChartsModal modal = new UsageChartsModal("Usage", r => throw new InvalidOperationException("db locked"), UsageRange.Day);
                        string rendered = Render(modal, 120, 40);
                        MuxAssert.Contains("Failed to read usage", rendered, "error surfaced");
                        MuxAssert.Contains("db locked", rendered, "error detail surfaced");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor(SuiteId, "NarrowSurfaceDoesNotThrow", "Rendering onto a tiny surface truncates without throwing", (CancellationToken ct) =>
                    {
                        UsageChartsModal modal = new UsageChartsModal("Usage", r => SampleData(), UsageRange.Day);
                        for (int height = 1; height <= 20; height++) Render(modal, 14, height);
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor(SuiteId, "EnterClosesModal", "Enter closes the modal with a null result", (CancellationToken ct) =>
                    {
                        UsageChartsModal modal = new UsageChartsModal("Usage", r => SampleData(), UsageRange.Day);
                        bool handled = modal.HandleKey(KeyEvent.Special(KeyCode.Enter));
                        MuxAssert.IsTrue(handled, "Enter handled");
                        MuxAssert.IsTrue(modal.Completion.IsCompleted, "modal completed");
                        return Task.CompletedTask;
                    })
                });
        }

        private static UsageChartData SampleData()
        {
            UsageChartData data = new UsageChartData();
            data.HeaderLines.Add("98k tok · $3.20 · 64 calls · 1 err");
            string[] labels = { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" };
            double[] tokens = { 1000, 4000, 2500, 8000, 6000, 1500, 9000 };
            double[] costs = { 0.10, 0.40, 0.25, 0.80, 0.60, 0.15, 0.90 };
            double[] ttft = { 200, 340, 260, 900, 500, 180, 700 };
            double[] total = { 800, 2600, 1400, 6400, 3200, 900, 5000 };
            double[] stream = { 600, 2200, 1100, 5400, 2600, 700, 4200 };
            for (int i = 0; i < labels.Length; i++)
            {
                data.BucketLabels.Add(labels[i]);
                data.TokensPerBucket.Add(tokens[i]);
                data.CostPerBucket.Add(costs[i]);
                data.TtftMsPerBucket.Add(ttft[i]);
                data.TotalMsPerBucket.Add(total[i]);
                data.StreamMsPerBucket.Add(stream[i]);
            }

            data.ModelLabels.Add("gpt-5");
            data.ModelCosts.Add(2.10);
            data.ModelLabels.Add("claude-opus-4-8");
            data.ModelCosts.Add(1.10);
            return data;
        }

        // Renders the modal into a headless buffer and flattens it to a single string for substring assertions.
        private static string Render(UsageChartsModal modal, int width, int height)
        {
            CellBuffer buffer = new CellBuffer(width, height);
            modal.Render(new BufferSurface(buffer));

            StringBuilder sb = new StringBuilder();
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    sb.Append(buffer.Get(x, y).Grapheme);
                }

                sb.Append('\n');
            }

            return sb.ToString();
        }
    }
}
