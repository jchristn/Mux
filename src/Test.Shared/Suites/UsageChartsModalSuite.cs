namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Cli.App;
    using Mux.Core.Telemetry;
    using Touchstone.Core;
    using TUIKit;
    using TUIKit.Input;

    /// <summary>
    /// Touchstone suite for <see cref="UsageChartsModal"/>: the ten pages in spec order (calls with failure reasons,
    /// tokens by type, tokens by dimension, cost, the four box plots, usage by dimension, heatmap) render with data and
    /// with empty data; number keys 1-7 switch the range; d cycles the dimension; the bucket cursor highlights a column
    /// and Enter opens the drill-down calls list; the heatmap handles every range; the footer fits narrow terminals.
    /// </summary>
    public static class UsageChartsModalSuite
    {
        private const string SuiteId = "UsageChartsModal";
        private const long HourMs = 3_600_000L;
        private const long Start = 1_791_849_600_000L; // 2026-10-13 00:00 UTC
        private const long HeatNow = 1_791_870_000_000L;

        private static readonly Color CursorTint = Color.FromRgb(60, 64, 72);
        private static readonly Color Peak = Color.FromRgb(155, 232, 189);
        private static readonly Color OtherGrey = Color.FromRgb(128, 128, 128);

        /// <summary>
        /// Builds the usage-charts-modal suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/> for the usage-charts-modal cases.</returns>
        public static TestSuiteDescriptor Create()
        {
            return new TestSuiteDescriptor(
                SuiteId,
                "Usage charts modal: pages, ranges, dimensions, drill-down, and heatmap",
                new List<TestCaseDescriptor>
                {
                    new TestCaseDescriptor(SuiteId, "PagesInSpecOrder", "Right/Tab page through the ten charts in spec order and Left/Shift+Tab page back", (CancellationToken ct) =>
                    {
                        UsageChartsModal modal = NewModal();
                        string[] titles =
                        {
                            "Calls over time", "Tokens by type", "Tokens by model", "Cost over time", "Latency (ms)",
                            "Time to first token (ms)", "Streaming time (ms)", "Throughput (tokens/s)", "Usage by model", "Token activity heatmap"
                        };
                        MuxAssert.AreEqual(10, UsageChartsModal.PageCount, "page count");
                        for (int i = 0; i < titles.Length; i++)
                        {
                            string text = Render(modal, 120, 40);
                            MuxAssert.Contains(titles[i], text, "page " + i + " title");
                            MuxAssert.Contains("page " + (i + 1) + "/10", text, "page indicator " + i);
                            modal.HandleKey(i % 2 == 0 ? KeyEvent.Special(KeyCode.Right) : KeyEvent.Special(KeyCode.Tab));
                        }

                        MuxAssert.AreEqual(0, modal.Page, "wraps to the first page");
                        modal.HandleKey(KeyEvent.Special(KeyCode.Left));
                        MuxAssert.AreEqual(9, modal.Page, "left wraps to the heatmap");
                        modal.HandleKey(KeyEvent.Special(KeyCode.Tab, KeyModifiers.Shift));
                        MuxAssert.AreEqual(8, modal.Page, "shift+tab pages back");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor(SuiteId, "CallsPageSucceededFailedAndReasons", "Calls stack succeeded (green) and failed (red) with a failure-reasons list, or a no-failures note", (CancellationToken ct) =>
                    {
                        UsageChartsModal modal = NewModal();
                        CellBuffer buffer = RenderBuffer(modal, 120, 40);
                        string text = Flatten(buffer);
                        MuxAssert.Contains("Calls ↑", text, "calls axis");
                        MuxAssert.Contains("succeeded", text, "succeeded legend");
                        MuxAssert.Contains("failed", text, "failed legend");
                        MuxAssert.Contains("Failure reasons", text, "reasons heading");
                        MuxAssert.Contains("rate_limited", text, "most frequent reason");
                        MuxAssert.Contains("timeout", text, "second reason");
                        MuxAssert.IsTrue(text.IndexOf("rate_limited", StringComparison.Ordinal) < text.IndexOf("timeout", StringComparison.Ordinal), "most frequent first");
                        MuxAssert.Contains("14", text, "Y axis max is the largest call count");
                        HashSet<Color> colors = BarColors(buffer);
                        MuxAssert.IsTrue(colors.Contains(Color.FromPalette(2)), "succeeded segment is green");
                        MuxAssert.IsTrue(colors.Contains(Color.FromPalette(1)), "failed segment is red");
                        MuxAssert.Contains("errors", text, "cursor detail shows the error rate");

                        UsageChartData clean = SampleData();
                        clean.ErrorCounts.Clear();
                        MuxAssert.Contains("No failures in this range.", Render(new UsageChartsModal("Usage", _ => clean, UsageRange.Day), 120, 40), "empty reasons");
                        MuxAssert.Contains("Failure reasons", Render(new UsageChartsModal("Usage", _ => SampleData(), UsageRange.Day), 60, 40), "narrow layout puts reasons under the chart");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor(SuiteId, "TokensByTypeStacks", "Tokens stack uncached input, cached input, and output in three colors on a total scale", (CancellationToken ct) =>
                    {
                        UsageChartsModal modal = NewModal();
                        modal.HandleKey(KeyEvent.Special(KeyCode.Right));
                        CellBuffer buffer = RenderBuffer(modal, 120, 40);
                        string text = Flatten(buffer);
                        MuxAssert.Contains("uncached input", text, "uncached legend");
                        MuxAssert.Contains("cached input", text, "cached legend");
                        MuxAssert.Contains("output", text, "output legend");
                        MuxAssert.Contains("9k", text, "Y axis max is the largest bucket total");
                        MuxAssert.AreEqual(3, BarColors(buffer).Count, "three token types");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor(SuiteId, "TokensByDimensionAndCycling", "Tokens by dimension stack top values plus grey Other, and d/D cycle the dimension for both dimension pages", (CancellationToken ct) =>
                    {
                        List<string> requested = new List<string>();
                        UsageChartsModal modal = new UsageChartsModal("Usage", _ => SampleData(), UsageRange.Day, (data, dim) => { requested.Add(dim); return DimensionData(dim); });
                        MuxAssert.AreEqual("model", modal.Dimension, "model is the default");
                        modal.HandleKey(KeyEvent.Special(KeyCode.Right));
                        modal.HandleKey(KeyEvent.Special(KeyCode.Right));
                        CellBuffer buffer = RenderBuffer(modal, 120, 40);
                        string text = Flatten(buffer);
                        MuxAssert.Contains("Tokens by model", text, "title");
                        MuxAssert.Contains("model-a", text, "first series legend");
                        MuxAssert.Contains("model-b", text, "second series legend");
                        MuxAssert.Contains("Other", text, "other legend");
                        MuxAssert.Contains("d dimension", text, "footer advertises the dimension key");
                        HashSet<Color> colors = BarColors(buffer);
                        MuxAssert.IsTrue(colors.Contains(OtherGrey), "Other is grey");
                        MuxAssert.AreEqual(3, colors.Count, "one color per series");

                        string[] expected = { "endpoint", "provider", "callkind", "model" };
                        string[] titles = { "Tokens by endpoint", "Tokens by provider", "Tokens by call kind", "Tokens by model" };
                        for (int i = 0; i < expected.Length; i++)
                        {
                            modal.HandleKey(KeyEvent.Char('d'));
                            MuxAssert.AreEqual(expected[i], modal.Dimension, "cycle " + i);
                            MuxAssert.AreEqual(expected[i], requested[requested.Count - 1], "provider re-queried for " + expected[i]);
                            string page = Render(modal, 120, 40);
                            MuxAssert.Contains(titles[i], page, "title for " + expected[i]);
                            MuxAssert.Contains(expected[i] + "-a", page, "series for " + expected[i]);
                        }

                        modal.HandleKey(KeyEvent.Char('D'));
                        MuxAssert.AreEqual("callkind", modal.Dimension, "D cycles backwards");
                        for (int i = 0; i < 6; i++) modal.HandleKey(KeyEvent.Special(KeyCode.Right));
                        MuxAssert.Contains("Usage by call kind", Render(modal, 120, 40), "usage-by page shares the dimension");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor(SuiteId, "CostTotals", "Cost draws one total column per bucket on a dollar scale", (CancellationToken ct) =>
                    {
                        UsageChartsModal modal = NewModal();
                        for (int i = 0; i < 3; i++) modal.HandleKey(KeyEvent.Special(KeyCode.Right));
                        CellBuffer buffer = RenderBuffer(modal, 120, 40);
                        string text = Flatten(buffer);
                        MuxAssert.Contains("Cost (USD)", text, "unit");
                        MuxAssert.Contains("$0.90", text, "Y axis max");
                        MuxAssert.AreEqual(1, BarColors(buffer).Count, "totals, not stacked by model");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor(SuiteId, "BoxPlotsForTimings", "Latency, TTFT, streaming, and throughput draw wick, avg-p95 box, avg mark, and p99 tick with a key", (CancellationToken ct) =>
                    {
                        UsageChartsModal modal = NewModal();
                        for (int i = 0; i < 4; i++) modal.HandleKey(KeyEvent.Special(KeyCode.Right));
                        string[] captions = { "Total latency", "Time to first token", "Streaming time", "Throughput (tokens/s)" };
                        string[] maxima = { "11.5s", "2.3s", "9.2s", "57.6" };
                        for (int page = 0; page < 4; page++)
                        {
                            CellBuffer buffer = RenderBuffer(modal, 120, 40);
                            string text = Flatten(buffer);
                            MuxAssert.Contains(captions[page], text, "caption " + page);
                            MuxAssert.Contains(maxima[page], text, "Y axis reaches the largest max on page " + page);
                            MuxAssert.Contains("min–max", text, "key: wick");
                            MuxAssert.Contains("avg–p95", text, "key: box");
                            MuxAssert.Contains("p99", text, "key: p99");
                            MuxAssert.Contains("p95 ", text, "cursor detail lists percentiles");
                            int wick = 0, box = 0, avg = 0, tick = 0;
                            for (int y = 5; y < 34; y++)
                                for (int x = 13; x < 117; x++)
                                {
                                    string g = buffer.Get(x, y).Grapheme;
                                    if (g == "│") wick++;
                                    else if (g == "█") box++;
                                    else if (g == "━") avg++;
                                    else if (g == "┼") tick++;
                                }

                            MuxAssert.IsTrue(wick > 7, "wicks drawn on page " + page);
                            MuxAssert.IsTrue(box > 0, "boxes drawn on page " + page);
                            MuxAssert.IsTrue(avg >= 7, "avg marks drawn on page " + page);
                            MuxAssert.IsTrue(tick >= 7, "p99 ticks drawn on page " + page);
                            modal.HandleKey(KeyEvent.Special(KeyCode.Right));
                        }

                        UsageChartData noSamples = SampleData();
                        foreach (UsageMetrics m in noSamples.Buckets) m.TtftMsDist = new UsageDistribution();
                        UsageChartsModal empty = new UsageChartsModal("Usage", _ => noSamples, UsageRange.Day);
                        for (int i = 0; i < 5; i++) empty.HandleKey(KeyEvent.Special(KeyCode.Right));
                        MuxAssert.Contains("No timing samples in this range.", Render(empty, 120, 40), "calls without samples");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor(SuiteId, "UsageByDimensionBars", "Usage by dimension lists at most ten values with stacked token bars and tokens, cost, and calls", (CancellationToken ct) =>
                    {
                        UsageChartsModal modal = NewModal();
                        for (int i = 0; i < 8; i++) modal.HandleKey(KeyEvent.Special(KeyCode.Right));
                        CellBuffer buffer = RenderBuffer(modal, 120, 40);
                        string text = Flatten(buffer);
                        MuxAssert.Contains("Usage by model", text, "title");
                        MuxAssert.Contains("model-a", text, "top row");
                        MuxAssert.Contains("12k tok · $1.20 · 30 calls", text, "row label shows tokens, cost, and calls");
                        MuxAssert.Contains("uncached input", text, "bar key");
                        MuxAssert.AreEqual(3, BarColors(buffer).Count, "bars stack three token types");
                        MuxAssert.IsTrue(text.IndexOf("model-a", StringComparison.Ordinal) < text.IndexOf("model-b", StringComparison.Ordinal), "sorted by cost");

                        UsageChartsModal none = new UsageChartsModal("Usage", _ => SampleData(), UsageRange.Day, (d, dim) => new UsageDimensionData());
                        for (int i = 0; i < 8; i++) none.HandleKey(KeyEvent.Special(KeyCode.Right));
                        MuxAssert.Contains("No usage in this range.", Render(none, 120, 40), "empty breakdown");

                        UsageChartsModal failing = new UsageChartsModal("Usage", _ => SampleData(), UsageRange.Day, (d, dim) => throw new InvalidOperationException("split broke"));
                        for (int i = 0; i < 8; i++) failing.HandleKey(KeyEvent.Special(KeyCode.Right));
                        MuxAssert.Contains("split broke", Render(failing, 120, 40), "dimension errors surface");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor(SuiteId, "EmptyDataEveryPage", "Every page shows the no-usage state for an empty range", (CancellationToken ct) =>
                    {
                        UsageChartData empty = new UsageChartData();
                        for (int i = 0; i < 5; i++)
                        {
                            empty.BucketStartsUnixMs.Add(Start + i * HourMs);
                            empty.BucketLabels.Add("t" + i);
                            empty.Buckets.Add(new UsageMetrics());
                        }

                        foreach (UsageChartData data in new[] { new UsageChartData(), empty })
                        {
                            UsageChartsModal modal = new UsageChartsModal("Usage", _ => data, UsageRange.Day, (d, dim) => new UsageDimensionData());
                            for (int page = 0; page < UsageChartsModal.PageCount; page++)
                            {
                                MuxAssert.Contains("No usage in this range.", Render(modal, 120, 40), "empty state on page " + page);
                                modal.HandleKey(KeyEvent.Special(KeyCode.Right));
                            }
                        }

                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor(SuiteId, "NumberKeysSwitchAllSevenRanges", "Number keys 1-7 select hour, day, week, month, quarter, year, and all, re-querying the provider", (CancellationToken ct) =>
                    {
                        UsageRange requested = UsageRange.Day;
                        int dimensionCalls = 0;
                        UsageChartsModal modal = new UsageChartsModal("Usage", r => { requested = r; return SampleData(); }, UsageRange.Day, (d, dim) => { dimensionCalls++; return DimensionData(dim); });
                        UsageRange[] ranges = { UsageRange.Hour, UsageRange.Day, UsageRange.Week, UsageRange.Month, UsageRange.Quarter, UsageRange.Year, UsageRange.All };
                        string[] labels = { "Last hour", "Last day", "Last week", "Last month", "Last quarter", "Last year", "All time (1 hour buckets)" };
                        for (int i = 0; i < ranges.Length; i++)
                        {
                            int before = dimensionCalls;
                            modal.HandleKey(KeyEvent.Char('1' + i));
                            MuxAssert.AreEqual(ranges[i], requested, "provider queried for key " + (i + 1));
                            MuxAssert.AreEqual(ranges[i], modal.Range, "range for key " + (i + 1));
                            MuxAssert.AreEqual(before + 1, dimensionCalls, "dimension data reloaded for key " + (i + 1));
                            MuxAssert.Contains(labels[i], Render(modal, 120, 40), "range label for key " + (i + 1));
                        }

                        modal.HandleKey(KeyEvent.Char('8'));
                        MuxAssert.AreEqual(UsageRange.All, modal.Range, "8 is not a range key");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor(SuiteId, "BucketCursorHighlightsAndMoves", "The bucket cursor starts on the newest bucket, moves with [ ] Home End and PgUp/PgDn, and highlights its column", (CancellationToken ct) =>
                    {
                        UsageChartsModal modal = NewModal();
                        MuxAssert.AreEqual(6, modal.Cursor, "starts on the newest bucket");
                        int first = TintColumn(RenderBuffer(modal, 120, 40));
                        MuxAssert.IsTrue(first > 0, "cursor column highlighted");
                        MuxAssert.Contains("▸ " + UsageChartFormat.BucketWindow(Start + 6 * HourMs, HourMs), Render(modal, 120, 40), "detail names the bucket window");
                        modal.HandleKey(KeyEvent.Char('['));
                        MuxAssert.AreEqual(5, modal.Cursor, "[ moves back");
                        int second = TintColumn(RenderBuffer(modal, 120, 40));
                        MuxAssert.IsTrue(second < first, "highlight moves left");
                        modal.HandleKey(KeyEvent.Char(']'));
                        modal.HandleKey(KeyEvent.Char(']'));
                        MuxAssert.AreEqual(6, modal.Cursor, "] stops at the newest bucket");
                        modal.HandleKey(KeyEvent.Special(KeyCode.Home));
                        MuxAssert.AreEqual(0, modal.Cursor, "Home");
                        modal.HandleKey(KeyEvent.Special(KeyCode.End));
                        MuxAssert.AreEqual(6, modal.Cursor, "End");
                        for (int page = 1; page < 8; page++)
                        {
                            modal.HandleKey(KeyEvent.Special(KeyCode.Right));
                            MuxAssert.IsTrue(TintColumn(RenderBuffer(modal, 120, 40)) > 0, "highlight on over-time page " + page);
                        }

                        UsageChartData wide = new UsageChartData { BucketMs = 60_000L };
                        for (int i = 0; i < 60; i++)
                        {
                            wide.BucketStartsUnixMs.Add(Start + i * 60_000L);
                            wide.BucketLabels.Add("t" + i);
                            wide.Buckets.Add(new UsageMetrics { Calls = 1, TotalTokens = 10, InputTokens = 10 });
                        }

                        UsageChartsModal paged = new UsageChartsModal("Usage", _ => wide, UsageRange.Hour);
                        string last = Render(paged, 40, 24);
                        MuxAssert.Contains("t59", last, "newest buckets visible first");
                        paged.HandleKey(KeyEvent.Special(KeyCode.PageUp));
                        string earlier = Render(paged, 40, 24);
                        MuxAssert.IsTrue(earlier != last, "PgUp shows earlier buckets");
                        MuxAssert.IsTrue(paged.Cursor < 59, "cursor follows the page");
                        paged.HandleKey(KeyEvent.Special(KeyCode.PageDown));
                        MuxAssert.Contains("t59", Render(paged, 40, 24), "PgDn returns to the newest buckets");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor(SuiteId, "DrillDownListsBucketCalls", "Enter opens the calls in the cursor's bucket window; Esc returns to the charts and a second Esc closes", (CancellationToken ct) =>
                    {
                        long from = 0, to = 0;
                        UsageChartsModal modal = new UsageChartsModal("Usage", _ => SampleData(), UsageRange.Day, null, (f, t) =>
                        {
                            from = f;
                            to = t;
                            return CallsPage(40);
                        });
                        modal.HandleKey(KeyEvent.Char('['));
                        modal.HandleKey(KeyEvent.Special(KeyCode.Enter));
                        MuxAssert.IsNotNull(modal.DrillDown, "drill-down open");
                        MuxAssert.AreEqual(Start + 5 * HourMs, from, "window starts at the bucket");
                        MuxAssert.AreEqual(Start + 6 * HourMs - 1, to, "window ends at the bucket's last millisecond");
                        string text = Render(modal, 120, 40);
                        MuxAssert.Contains("Calls " + UsageChartFormat.BucketWindow(Start + 5 * HourMs, HourMs), text, "title names the window");
                        foreach (string column in new[] { "Time", "Model", "Endpoint", "Tokens", "Latency", "Status" }) MuxAssert.Contains(column, text, "column " + column);
                        MuxAssert.Contains("gpt-test", text, "model");
                        MuxAssert.Contains("ep-main", text, "endpoint");
                        MuxAssert.Contains("rate_limited", text, "error code");
                        MuxAssert.Contains("ok", text, "success status");
                        MuxAssert.Contains("1.5s", text, "latency");
                        MuxAssert.Contains("of 50", text, "total count shown");
                        MuxAssert.IsTrue(modal.DrillDown!.RowCount == 40, "rows loaded");
                        modal.HandleKey(KeyEvent.Special(KeyCode.PageDown));
                        MuxAssert.IsTrue(modal.DrillDown!.Offset > 0, "drill-down scrolls");
                        modal.HandleKey(KeyEvent.Char('3'));
                        MuxAssert.AreEqual(UsageRange.Day, modal.Range, "keys go to the drill-down while it is open");
                        modal.HandleKey(KeyEvent.Special(KeyCode.Escape));
                        MuxAssert.IsNull(modal.DrillDown, "Esc returns to the charts");
                        MuxAssert.IsFalse(modal.Completion.IsCompleted, "charts stay open");
                        MuxAssert.Contains("Calls over time", Render(modal, 120, 40), "charts visible again");
                        modal.HandleKey(KeyEvent.Special(KeyCode.Escape));
                        MuxAssert.IsTrue(modal.Completion.IsCompleted, "second Esc closes");

                        UsageChartsModal failing = new UsageChartsModal("Usage", _ => SampleData(), UsageRange.Day, null, (f, t) => throw new InvalidOperationException("db locked"));
                        failing.HandleKey(KeyEvent.Special(KeyCode.Enter));
                        MuxAssert.Contains("db locked", Render(failing, 120, 40), "drill-down errors surface");

                        UsageChartsModal noCalls = new UsageChartsModal("Usage", _ => SampleData(), UsageRange.Day, null, (f, t) => new UsageEventPage());
                        noCalls.HandleKey(KeyEvent.Special(KeyCode.Enter));
                        MuxAssert.Contains("No calls in this bucket.", Render(noCalls, 120, 40), "empty bucket");

                        UsageChartsModal heatmap = NewModal();
                        heatmap.HandleKey(KeyEvent.Special(KeyCode.Left));
                        heatmap.HandleKey(KeyEvent.Special(KeyCode.Enter));
                        MuxAssert.IsNull(heatmap.DrillDown, "no drill-down from the heatmap");
                        MuxAssert.IsFalse(heatmap.Completion.IsCompleted, "Enter does not close the charts");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor(SuiteId, "FooterListsKeysAndFitsNarrow", "The footer lists the page's keys and trims hints, never past the width, keeping Esc close", (CancellationToken ct) =>
                    {
                        UsageChartsModal modal = NewModal();
                        string wide = Render(modal, 160, 40);
                        foreach (string hint in new[] { "←/→ page", "1-7 hour/day/week/month/quarter/year/all", "[ ] bucket", "Enter calls", "Esc close" }) MuxAssert.Contains(hint, wide, "wide footer: " + hint);
                        string narrow = Render(modal, 60, 30);
                        MuxAssert.Contains("Esc close", narrow, "narrow footer keeps Esc");
                        MuxAssert.Contains("1-7 range", narrow, "narrow footer shortens range hint");
                        foreach (int width in new[] { 20, 30, 44, 80 })
                        {
                            for (int page = 0; page < UsageChartsModal.PageCount; page++)
                            {
                                string[] lines = Render(modal, width, 30).Split('\n');
                                string footer = lines[30 - 2];
                                MuxAssert.IsTrue(footer.Contains("Esc", StringComparison.Ordinal) || width < 24, "Esc hint kept at width " + width + " page " + page);
                                modal.HandleKey(KeyEvent.Special(KeyCode.Right));
                            }
                        }

                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor(SuiteId, "HeatmapIntensityAndEmptyRange", "Heatmap colors distinguish zero, low, and peak usage and handle empty ranges", (CancellationToken ct) =>
                    {
                        UsageHeatmap heatmap = new UsageHeatmap(UsageHeatmapLayout.Compute(UsageRange.Day, HeatNow, 0));
                        heatmap.TotalTokens[1] = 1;
                        heatmap.TotalTokens[2] = 100;
                        UsageChartData data = new UsageChartData { Heatmap = heatmap };
                        UsageChartsModal modal = new UsageChartsModal("Usage", r => data, UsageRange.Day);
                        modal.HandleKey(KeyEvent.Special(KeyCode.Left));
                        CellBuffer buffer = RenderBuffer(modal, 120, 40);
                        HashSet<Color> colors = new HashSet<Color>();
                        for (int y = 0; y < 40; y++)
                            for (int x = 0; x < 120; x++) colors.Add(buffer.Get(x, y).Style.Background);
                        MuxAssert.IsFalse(colors.Contains(Color.FromRgb(45, 48, 52)), "zero usage has no dark fill that could pass for a shade");
                        MuxAssert.IsTrue(colors.Contains(Color.FromRgb(31, 107, 72)), "low usage cell");
                        MuxAssert.IsTrue(colors.Contains(Peak), "peak usage cell");
                        int dots = 0;
                        for (int y = 2; y < 30; y++)
                            for (int x = 7; x < 120; x++)
                                if (buffer.Get(x, y).Grapheme == "·" && buffer.Get(x, y).Style.Background == CellStyle.Default.Background) dots++;
                        MuxAssert.IsTrue(dots >= 1, "zero usage is drawn as an unfilled dot");
                        MuxAssert.Contains("· none", Render(modal, 120, 40), "legend names the empty cell");
                        UsageChartsModal empty = new UsageChartsModal("Usage", r => new UsageChartData(), UsageRange.Day);
                        empty.HandleKey(KeyEvent.Special(KeyCode.Left));
                        MuxAssert.Contains("No usage in this range.", Render(empty, 120, 40), "empty heatmap");
                        UsageChartsModal zeros = new UsageChartsModal("Usage", r => new UsageChartData { Heatmap = new UsageHeatmap(UsageHeatmapLayout.Compute(UsageRange.Day, HeatNow, 0)) }, UsageRange.Day);
                        zeros.HandleKey(KeyEvent.Special(KeyCode.Left));
                        MuxAssert.Contains("No usage in this range.", Render(zeros, 120, 40), "all-zero heatmap");
                        for (int height = 1; height <= 20; height++) Render(modal, 14, height);
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor(SuiteId, "HeatmapCellsFillTheWidth", "Heatmap cells stretch to the terminal's width, time runs left to right, and every cell in the window is drawn once", (CancellationToken ct) =>
                    {
                        foreach (UsageRange range in new[] { UsageRange.Hour, UsageRange.Day, UsageRange.Week, UsageRange.Month, UsageRange.Quarter, UsageRange.Year })
                        {
                            UsageHeatmapLayout layout = UsageHeatmapLayout.Compute(range, HeatNow, 0);
                            int previousWidth = 0;
                            foreach (var size in new[] { (Width: 80, Height: 40), (Width: 140, Height: 50), (Width: 240, Height: 60) })
                            {
                                UsageChartsModal modal = new UsageChartsModal("Usage", _ => HeatmapData(layout), range);
                                modal.HandleKey(KeyEvent.Special(KeyCode.Left));
                                CellBuffer buffer = RenderBuffer(modal, size.Width, size.Height);
                                string text = Flatten(buffer);
                                string at = " for " + range + " at " + size.Width + "x" + size.Height;
                                MuxAssert.IsFalse(Scrolls(text), "fits without scrolling" + at);
                                (int left, int top) = FirstPeak(buffer);
                                MuxAssert.IsTrue(left >= 0, "a cell is drawn" + at);
                                int runWidth = 0, cellHeight = 0;
                                while (buffer.Get(left + runWidth, top).Style.Background == Peak) runWidth++;
                                while (buffer.Get(left, top + cellHeight).Style.Background == Peak) cellHeight++;
                                // Each cell is drawn once: peak characters = cells in the window x cell area (cells with a gap
                                // between columns; a year at 80 columns has none, one character per cell).
                                int cellWidth = range == UsageRange.Year && size.Width == 80 ? 1 : runWidth;
                                MuxAssert.AreEqual(PastCells(layout) * cellWidth * cellHeight, CountPeak(buffer), "every cell in the window is drawn once" + at);
                                MuxAssert.IsTrue(cellWidth >= previousWidth, "a wider terminal never narrows cells" + at);
                                if (range == UsageRange.Day || range == UsageRange.Quarter) MuxAssert.IsTrue(cellWidth > previousWidth || cellWidth == 8, "a wider terminal widens cells up to 8" + at);
                                MuxAssert.IsTrue(cellWidth <= 8 && cellHeight <= 3, "cells stay within 8x3" + at);
                                previousWidth = cellWidth;
                            }
                        }

                        // Time runs left to right: the oldest cell is in the first column, now in the last.
                        UsageHeatmapLayout day = UsageHeatmapLayout.Compute(UsageRange.Day, HeatNow, 0);
                        UsageHeatmap single = new UsageHeatmap(day);
                        single.TotalTokens[day.IndexOf(HeatNow)] = 100;
                        UsageChartsModal one = new UsageChartsModal("Usage", _ => new UsageChartData { Heatmap = single }, UsageRange.Day);
                        one.HandleKey(KeyEvent.Special(KeyCode.Left));
                        CellBuffer oneBuffer = RenderBuffer(one, 160, 40);
                        (int nowLeft, _) = FirstPeak(oneBuffer);
                        MuxAssert.IsTrue(nowLeft > 100, "now is drawn at the right end: " + nowLeft);
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor(SuiteId, "HeatmapAxesLabelRowsAndColumns", "Both axes are labeled for every range: periods along the top (thinned, never colliding, first and last kept) and steps on the left", (CancellationToken ct) =>
                    {
                        (UsageRange Range, string Spots, string Interval, string Caption, string FirstRow, string LastRow)[] cases =
                        {
                            (UsageRange.Hour, "60 spots", "1 min/spot", "Columns: 5 minutes starting · Rows: minutes after the column start", "+0m", "+4m"),
                            (UsageRange.Day, "96 spots", "15 min/spot", "Columns: hour of day · Rows: minute of the hour", ":00", ":45"),
                            (UsageRange.Week, "84 spots", "2 hour/spot", "Columns: day · Rows: time of day", "00:00", "22:00"),
                            (UsageRange.Month, "84 spots", "12 hour/spot", "Columns: day of the week (AM, PM) · Rows: week starting", "2026-09-14", "2026-10-12"),
                            (UsageRange.Quarter, "91 spots", "1 day/spot", "Columns: week starting · Rows: day of the week", "Mon", "Sun"),
                            (UsageRange.Year, "371 spots", "1 day/spot", "Columns: week (by month) · Rows: day of the week", "Mon", "Sun")
                        };
                        foreach (var c in cases)
                        {
                            UsageHeatmapLayout layout = UsageHeatmapLayout.Compute(c.Range, HeatNow, 0);
                            foreach (int width in new[] { 80, 160 })
                            {
                                UsageChartsModal modal = new UsageChartsModal("Usage", _ => HeatmapData(layout), c.Range);
                                modal.HandleKey(KeyEvent.Special(KeyCode.Left));
                                string text = Render(modal, width, 50);
                                string at = " for " + c.Range + " at " + width;
                                MuxAssert.Contains(c.Spots, text, "spot count" + at);
                                MuxAssert.Contains(c.Interval, text, "interval" + at);
                                if (width >= 160) MuxAssert.Contains(c.Caption, text, "axis caption" + at);
                                MuxAssert.Contains(c.FirstRow, text, "first row label" + at);
                                MuxAssert.Contains(c.LastRow, text, "last row label" + at);
                                string header = HeaderLine(text);
                                MuxAssert.IsTrue(header.StartsWith(layout.ColumnLabels[0], StringComparison.Ordinal), "first column labeled" + at + ": " + header);
                                MuxAssert.IsTrue(header.EndsWith(layout.ColumnLabels[layout.ColumnLabelAnchors[layout.ColumnLabelAnchors.Count - 1]], StringComparison.Ordinal), "last column labeled" + at + ": " + header);
                                // Labels never run into each other: the line splits back into whole labels.
                                HashSet<string> words = new HashSet<string>(layout.ColumnLabels.SelectMany(l => l.Split(' ')));
                                foreach (string word in header.Split(' ', StringSplitOptions.RemoveEmptyEntries)) MuxAssert.IsTrue(words.Contains(word), "label '" + word + "' is whole, not run into a neighbour" + at + ": " + header);
                            }
                        }

                        UsageHeatmapLayout all = UsageHeatmapLayout.Compute(UsageRange.All, HeatNow, 0, HeatNow - 200L * 24L * HourMs);
                        UsageChartsModal allModal = new UsageChartsModal("Usage", _ => HeatmapData(all), UsageRange.All);
                        allModal.HandleKey(KeyEvent.Special(KeyCode.Left));
                        CellBuffer allBuffer = RenderBuffer(allModal, 160, 60);
                        string allText = Flatten(allBuffer);
                        MuxAssert.Contains(all.CellCount + " spots", allText, "all-time spot count");
                        MuxAssert.Contains("1 week/spot", allText, "all-time interval");
                        MuxAssert.Contains(all.ColumnLabels[0], allText, "all-time date columns");
                        MuxAssert.Contains("+3w", allText, "all-time week rows");
                        MuxAssert.AreEqual(PastCells(all), CountSpots(allBuffer, 160, 60), "every past all-time cell drawn once");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor(SuiteId, "HeatmapRangesAndScrolling", "Number keys switch the heatmap range, and a grid too wide for the terminal scrolls sideways with PgUp/PgDn from the newest columns", (CancellationToken ct) =>
                    {
                        UsageChartsModal modal = new UsageChartsModal("Usage", range => HeatmapData(UsageHeatmapLayout.Compute(range, HeatNow, 0)), UsageRange.Day);
                        modal.HandleKey(KeyEvent.Special(KeyCode.Left));
                        string[] expected = { "60 spots", "96 spots", "84 spots", "84 spots", "91 spots", "371 spots" };
                        for (int i = 0; i < expected.Length; i++)
                        {
                            modal.HandleKey(KeyEvent.Char('1' + i));
                            string rendered = Render(modal, 160, 40);
                            MuxAssert.Contains(expected[i], rendered, "fixed spot count for range " + i);
                            MuxAssert.Contains("Columns: ", rendered, "x axis caption");
                            MuxAssert.Contains(" · Rows: ", rendered, "y axis caption");
                        }

                        // The year at 44 columns: one-character cells without gaps still do not fit 53 weeks.
                        modal.HandleKey(KeyEvent.Char('6'));
                        string newest = Render(modal, 44, 30);
                        MuxAssert.IsTrue(Scrolls(newest), "a too-wide grid scrolls");
                        MuxAssert.Contains("PgUp/PgDn", newest, "the scroll keys are named");
                        MuxAssert.IsTrue(Regex.IsMatch(newest, "columns \\d+–53/53"), "starts at the newest weeks");
                        modal.HandleKey(KeyEvent.Special(KeyCode.PageUp));
                        string older = Render(modal, 44, 30);
                        MuxAssert.IsTrue(older != newest, "PgUp scrolls to older weeks");
                        for (int i = 0; i < 5; i++) modal.HandleKey(KeyEvent.Special(KeyCode.PageUp));
                        MuxAssert.IsTrue(Regex.IsMatch(Render(modal, 44, 30), "columns 1–\\d+/53"), "stops at the oldest week");
                        for (int i = 0; i < 10; i++) modal.HandleKey(KeyEvent.Special(KeyCode.PageDown));
                        MuxAssert.AreEqual(newest, Render(modal, 44, 30), "PgDn returns to the newest weeks");
                        string header = HeaderLine(newest);
                        MuxAssert.IsTrue(header.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length >= 2, "a scrolled grid labels its first and last visible column");
                        modal.HandleKey(KeyEvent.Char('2'));
                        MuxAssert.IsFalse(Scrolls(Render(modal, 80, 30)), "a day fits 80 columns");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor(SuiteId, "HeatmapUsesLocalClockRows", "The heatmap draws its own local-time grid, not the rolling chart buckets, and leaves future cells blank", (CancellationToken ct) =>
                    {
                        long offBoundary = new DateTimeOffset(2026, 10, 13, 12, 6, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
                        UsageHeatmapLayout layout = UsageHeatmapLayout.Compute(UsageRange.Day, offBoundary, 330);
                        UsageChartData data = HeatmapData(layout);
                        for (int i = 0; i < 96; i++)
                        {
                            data.Buckets.Add(new UsageMetrics { TotalTokens = 9999, Calls = 1 });
                            data.BucketStartsUnixMs.Add(offBoundary + i * 900000L);
                        }

                        UsageChartsModal modal = new UsageChartsModal("Usage", r => data, UsageRange.Day);
                        modal.HandleKey(KeyEvent.Special(KeyCode.Left));
                        string rendered = Render(modal, 160, 60);
                        string[] header = HeaderLine(rendered).Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        MuxAssert.AreEqual("18", header[0], "columns start on the local hour at UTC+5:30 (12:06 UTC is 17:36 local)");
                        MuxAssert.AreEqual("17", header[header.Length - 1], "the last column is the local hour holding now");
                        MuxAssert.Contains("peak 100", rendered, "heatmap values, not the rolling series");
                        MuxAssert.IsTrue(!rendered.Contains(":06"), "off-boundary rolling timestamps do not label the heatmap");
                        MuxAssert.IsTrue(PastCells(layout) < layout.CellCount, "the current column has future cells");
                        MuxAssert.Contains("Oldest at the left, newest at the right", rendered, "reading order");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor(SuiteId, "HeatmapMonthCalendar", "The month heatmap is a calendar of 14 half-day columns and 5 or 6 week rows, labels each weekday once, and draws exactly its 30 days", (CancellationToken ct) =>
                    {
                        long wednesday = new DateTimeOffset(2026, 11, 11, 15, 30, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
                        long monday = new DateTimeOffset(2026, 11, 9, 8, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
                        foreach ((long now, int rows) in new[] { (wednesday, 5), (monday, 6), (HeatNow, 6) })
                        {
                            UsageHeatmapLayout layout = UsageHeatmapLayout.Compute(UsageRange.Month, now, 0);
                            MuxAssert.AreEqual(rows, layout.Rows, "week rows");
                            UsageHeatmap heatmap = new UsageHeatmap(layout);
                            for (int i = 0; i < layout.CellCount; i++) heatmap.TotalTokens[i] = 100; // Even the cells outside the window.
                            foreach (var size in new[] { (Width: 60, Height: 24), (Width: 160, Height: 60) })
                            {
                                UsageChartsModal modal = new UsageChartsModal("Usage", _ => new UsageChartData { Heatmap = heatmap }, UsageRange.Month);
                                modal.HandleKey(KeyEvent.Special(KeyCode.Left));
                                CellBuffer buffer = RenderBuffer(modal, size.Width, size.Height);
                                string text = Flatten(buffer);
                                string at = " with " + rows + " rows at " + size.Width + "x" + size.Height;
                                if (size.Width >= 100) MuxAssert.Contains("Columns: day of the week (AM, PM) · Rows: week starting", text, "caption" + at);
                                MuxAssert.IsFalse(Scrolls(text), "the calendar fits without scrolling" + at);
                                MuxAssert.AreEqual(60, CountSpots(buffer, size.Width, size.Height), "only the 60 half-days in the window are drawn" + at);
                                MuxAssert.Contains("Oldest at the top-left, newest at the bottom-right", text, "calendar reading order" + at);
                                string header = HeaderLine(text);
                                string[] days = { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" };
                                foreach (string day in days)
                                {
                                    MuxAssert.AreEqual(1, header.Split(day).Length - 1, day + " labeled once" + at);
                                }

                                // Each weekday is centred over its AM and PM columns (Sun over Sun AM and PM, not pushed
                                // to the grid's end): its text overlaps both cells and its centre is within a character
                                // of theirs.
                                string[] lines = text.Split('\n');
                                int headerY = Array.FindIndex(lines, l => l.Contains("Columns:", StringComparison.Ordinal)) + 1;
                                List<(int Left, int Right)> cells = Enumerable.Range(headerY + 1, buffer.Height - headerY - 1).Select(y => PeakRuns(buffer, y)).FirstOrDefault(r => r.Count == 14) ?? new List<(int Left, int Right)>();
                                MuxAssert.AreEqual(14, cells.Count, "a full week row has 14 half-day cells" + at);
                                for (int d = 0; d < days.Length; d++)
                                {
                                    int left = lines[headerY].IndexOf(days[d], StringComparison.Ordinal);
                                    int right = left + days[d].Length - 1;
                                    (int am, int pm) = (cells[2 * d].Left, cells[(2 * d) + 1].Right);
                                    MuxAssert.IsTrue(left <= cells[2 * d].Right && right >= cells[(2 * d) + 1].Left, days[d] + " overlaps its AM and PM cells" + at + ": " + lines[headerY]);
                                    MuxAssert.IsTrue(Math.Abs((left + right) - (am + pm)) <= 2, days[d] + " is centred over its AM and PM cells" + at + ": " + lines[headerY]);
                                }

                                foreach (string week in layout.RowLabels) MuxAssert.Contains(week, text, "week row " + week + at);
                            }
                        }

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

                    new TestCaseDescriptor(SuiteId, "NarrowSurfaceDoesNotThrow", "Every page and the drill-down render onto tiny surfaces without throwing", (CancellationToken ct) =>
                    {
                        UsageChartsModal modal = new UsageChartsModal("Usage", _ => SampleData(), UsageRange.Day, (d, dim) => DimensionData(dim), (f, t) => CallsPage(5));
                        for (int page = 0; page < UsageChartsModal.PageCount; page++)
                        {
                            for (int width = 1; width < 50; width += 3)
                                for (int height = 1; height < 22; height += 2) Render(modal, width, height);
                            modal.HandleKey(KeyEvent.Special(KeyCode.Right));
                        }

                        modal.HandleKey(KeyEvent.Special(KeyCode.Enter));
                        for (int width = 1; width < 50; width += 3)
                            for (int height = 1; height < 22; height += 2) Render(modal, width, height);
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor(SuiteId, "MetricPickerRetitlesAndRescales", "m/M cycle tokens, calls, and cost on the by-dimension page; the title, axis, and values follow", (CancellationToken ct) =>
                    {
                        UsageChartsModal modal = NewModal();
                        MuxAssert.AreEqual("tokens", modal.Metric, "tokens is the default");
                        MuxAssert.AreEqual("tokens,calls,cost", string.Join(",", UsageChartsModal.Metrics), "metric order");
                        modal.HandleKey(KeyEvent.Special(KeyCode.Right));
                        modal.HandleKey(KeyEvent.Special(KeyCode.Right));
                        MuxAssert.Contains("m metric (Tokens)", Render(modal, 160, 40), "footer advertises the metric key");

                        modal.HandleKey(KeyEvent.Char('m'));
                        MuxAssert.AreEqual("calls", modal.Metric, "m advances");
                        string calls = Render(modal, 160, 40);
                        MuxAssert.Contains("Calls by model", calls, "title follows the metric");
                        MuxAssert.Contains("Calls ↑", calls, "axis follows the metric");
                        // The last bucket stacks 7 + 14 + 21 calls.
                        MuxAssert.Contains("42", calls, "Y axis max is the largest call stack");
                        MuxAssert.Contains("total 12 calls", calls, "detail line totals calls for the cursor bucket");

                        modal.HandleKey(KeyEvent.Char('d'));
                        MuxAssert.Contains("Calls by endpoint", Render(modal, 160, 40), "dimension keeps the metric");
                        modal.HandleKey(KeyEvent.Char('m'));
                        string cost = Render(modal, 160, 40);
                        MuxAssert.Contains("Cost by endpoint", cost, "cost title");
                        MuxAssert.Contains("Cost (USD) ↑", cost, "cost axis");
                        modal.HandleKey(KeyEvent.Char('M'));
                        MuxAssert.AreEqual("calls", modal.Metric, "M goes back");
                        modal.HandleKey(KeyEvent.Char('m'));
                        modal.HandleKey(KeyEvent.Char('m'));
                        MuxAssert.AreEqual("tokens", modal.Metric, "wraps to tokens");
                        MuxAssert.AreEqual("cost", new UsageChartsModal("Usage", _ => SampleData(), UsageRange.Day, initialMetric: "COST").Metric, "initial metric");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor(SuiteId, "UsageByRanksByMetric", "Usage by sorts its bars by the chosen metric and still labels tokens, cost, and calls", (CancellationToken ct) =>
                    {
                        UsageDimensionData Rows(string dimension)
                        {
                            UsageDimensionData data = DimensionData(dimension);
                            data.AllRows.AddRange(data.Rows);
                            data.AllRows.Add(new UsageBreakdownRow { Dimension = dimension, Value = "chatty", Metrics = new UsageMetrics { TotalTokens = 300, InputTokens = 200, OutputTokens = 100, CostUsd = 0.01, Calls = 90, Errors = 9 } });
                            return data;
                        }

                        UsageChartsModal modal = new UsageChartsModal("Usage", _ => SampleData(), UsageRange.Day, (d, dim) => Rows(dim));
                        for (int i = 0; i < 8; i++) modal.HandleKey(KeyEvent.Special(KeyCode.Right));
                        string tokens = Render(modal, 140, 40);
                        MuxAssert.Contains("sorted by tokens", tokens, "caption names the sort");
                        MuxAssert.IsTrue(tokens.IndexOf("model-a", StringComparison.Ordinal) < tokens.IndexOf("chatty", StringComparison.Ordinal), "tokens: chatty last");

                        modal.HandleKey(KeyEvent.Char('m'));
                        CellBuffer buffer = RenderBuffer(modal, 140, 40);
                        string calls = Flatten(buffer);
                        MuxAssert.Contains("Model ↓ · Calls → · sorted by calls", calls, "calls caption");
                        MuxAssert.IsTrue(calls.IndexOf("chatty", StringComparison.Ordinal) < calls.IndexOf("model-a", StringComparison.Ordinal), "calls: chatty first");
                        MuxAssert.Contains("90 calls", calls, "stats still list calls");
                        MuxAssert.Contains("tok", calls, "stats still list tokens");
                        MuxAssert.Contains("succeeded", calls, "calls legend");
                        HashSet<Color> colors = BarColors(buffer);
                        MuxAssert.AreEqual(2, colors.Count, "succeeded and failed bar parts");

                        modal.HandleKey(KeyEvent.Char('m'));
                        string cost = Render(modal, 140, 40);
                        MuxAssert.Contains("sorted by cost", cost, "cost caption");
                        MuxAssert.IsTrue(cost.IndexOf("model-a", StringComparison.Ordinal) < cost.IndexOf("chatty", StringComparison.Ordinal), "cost: chatty last");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor(SuiteId, "LinesModeDrawsBrailleLines", "l switches the stacked charts to unstacked braille lines with the same colors and legend; box plots stay boxes", (CancellationToken ct) =>
                    {
                        UsageChartsModal modal = NewModal();
                        MuxAssert.IsFalse(modal.Lines, "bars by default");
                        MuxAssert.Contains("l lines", Render(modal, 160, 40), "footer offers lines");
                        modal.HandleKey(KeyEvent.Char('l'));
                        MuxAssert.IsTrue(modal.Lines, "l toggles");
                        CellBuffer buffer = RenderBuffer(modal, 160, 40);
                        string text = Flatten(buffer);
                        MuxAssert.Contains("l bars", text, "footer offers bars back");
                        MuxAssert.Contains("· lines", text, "caption says lines");
                        MuxAssert.Contains("━ succeeded", text, "legend kept with a line swatch");
                        MuxAssert.AreEqual(0, BarColors(buffer).Count, "no bar blocks in lines mode");
                        HashSet<Color> lineColors = BrailleColors(buffer);
                        MuxAssert.AreEqual(2, lineColors.Count, "one line color per series (succeeded, failed)");
                        MuxAssert.IsTrue(TintColumn(buffer) >= 0, "cursor column still tinted");

                        // Unstacked: the Y axis max is the largest single series value (14 calls minus 2 errors), not the stack.
                        MuxAssert.Contains("12", text, "largest succeeded count");

                        // Seven buckets on a wide plot leave room for point markers.
                        MuxAssert.Contains("●", text, "point markers with few buckets");

                        modal.HandleKey(KeyEvent.Special(KeyCode.Enter));
                        MuxAssert.IsNotNull(modal.DrillDown, "drill-down still works in lines mode");
                        modal.HandleKey(KeyEvent.Special(KeyCode.Escape));

                        modal.HandleKey(KeyEvent.Special(KeyCode.Right));
                        modal.HandleKey(KeyEvent.Special(KeyCode.Right));
                        MuxAssert.AreEqual(3, BrailleColors(RenderBuffer(modal, 160, 40)).Count, "metric-by-dimension lines: one per series, Other included");
                        for (int i = 0; i < 2; i++) modal.HandleKey(KeyEvent.Special(KeyCode.Right));
                        CellBuffer box = RenderBuffer(modal, 160, 40);
                        MuxAssert.Contains("Latency", Flatten(box), "latency page");
                        MuxAssert.AreEqual(0, BrailleColors(box).Count, "box plots are unaffected");
                        MuxAssert.DoesNotContain("l bars", Flatten(box), "no lines key on box plots");

                        UsageChartsModal start = new UsageChartsModal("Usage", _ => SampleData(), UsageRange.Day, initialLines: true);
                        MuxAssert.IsTrue(start.Lines, "initial lines");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor(SuiteId, "ExportWritesCsvAndShowsPath", "e writes the page's CSV into the exports folder, names it by chart and range, and shows the full path", (CancellationToken ct) =>
                    {
                        string dir = Path.Combine(Path.GetTempPath(), "mux-usage-export-" + Guid.NewGuid().ToString("N"));
                        try
                        {
                            UsageChartsModal modal = new UsageChartsModal("Usage", _ => SampleData(), UsageRange.Day, (d, dim) => DimensionData(dim), null, exportDirectory: dir);
                            MuxAssert.Contains("e export CSV", Render(modal, 160, 40), "footer advertises export");
                            modal.HandleKey(KeyEvent.Char('e'));
                            string? path = modal.LastExportPath;
                            MuxAssert.IsNotNull(path, "exported");
                            MuxAssert.IsTrue(File.Exists(path!), "file written");
                            MuxAssert.AreEqual(Path.GetFullPath(dir), Path.GetDirectoryName(path!), "into the exports folder");
                            MuxAssert.IsTrue(System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(path!), "^mux-usage-calls-day-\\d{8}-\\d{6}\\.csv$"), "file name: " + Path.GetFileName(path!));
                            List<UsageBucket> buckets = new List<UsageBucket>();
                            UsageChartData sample = SampleData();
                            for (int i = 0; i < sample.Buckets.Count; i++) buckets.Add(new UsageBucket { BucketStartUnixMs = sample.BucketStartsUnixMs[i], Metrics = sample.Buckets[i] });
                            MuxAssert.AreEqual(UsageCsv.Calls(buckets, HourMs), File.ReadAllText(path!), "content is the shared CSV format");
                            MuxAssert.Contains("Saved CSV: ", modal.Notice, "notice");
                            string shown = Render(modal, 400, 40);
                            MuxAssert.Contains(path!, shown, "full path in the footer");
                            modal.HandleKey(KeyEvent.Special(KeyCode.Right));
                            MuxAssert.AreEqual(string.Empty, modal.Notice, "next key clears the notice");

                            modal.HandleKey(KeyEvent.Special(KeyCode.Right));
                            modal.HandleKey(KeyEvent.Char('m'));
                            modal.HandleKey(KeyEvent.Char('e'));
                            MuxAssert.Contains("mux-usage-calls-by-model-day-", Path.GetFileName(modal.LastExportPath!), "split file name follows metric and dimension");
                            string split = File.ReadAllText(modal.LastExportPath!);
                            MuxAssert.IsTrue(split.StartsWith("bucket_start,bucket_end,model-a,model-b,Other\n", StringComparison.Ordinal), "split header");
                            MuxAssert.Contains(",3,6,9\n", split, "calls per series in the third bucket");

                            for (int i = 0; i < 6; i++) modal.HandleKey(KeyEvent.Special(KeyCode.Right));
                            modal.HandleKey(KeyEvent.Char('e'));
                            MuxAssert.Contains("mux-usage-usage-by-model-day-", Path.GetFileName(modal.LastExportPath!), "usage by file name");
                            MuxAssert.Contains(",model,calls,failed,", File.ReadAllText(modal.LastExportPath!), "usage by header");

                            UsageChartsModal empty = new UsageChartsModal("Usage", _ => new UsageChartData(), UsageRange.Week, exportDirectory: dir);
                            int before = Directory.GetFiles(dir).Length;
                            empty.HandleKey(KeyEvent.Char('e'));
                            MuxAssert.IsNull(empty.LastExportPath, "no data disables export");
                            MuxAssert.Contains("Nothing to export", empty.Notice, "explains why");
                            MuxAssert.AreEqual(before, Directory.GetFiles(dir).Length, "no file written");

                            UsageHeatmapLayout layout = UsageHeatmapLayout.Compute(UsageRange.Day, HeatNow, 0);
                            UsageChartsModal heat = new UsageChartsModal("Usage", _ => HeatmapData(layout), UsageRange.Day, exportDirectory: dir);
                            heat.HandleKey(KeyEvent.Special(KeyCode.Left));
                            heat.HandleKey(KeyEvent.Char('e'));
                            MuxAssert.Contains("mux-usage-heatmap-day-", Path.GetFileName(heat.LastExportPath!), "heatmap file name");
                            MuxAssert.AreEqual(UsageCsv.Heatmap(HeatmapData(layout).Heatmap), File.ReadAllText(heat.LastExportPath!), "heatmap cells");
                        }
                        finally
                        {
                            if (Directory.Exists(dir)) Directory.Delete(dir, true);
                        }

                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor(SuiteId, "EscClosesModal", "Esc closes the modal with a null result", (CancellationToken ct) =>
                    {
                        UsageChartsModal modal = NewModal();
                        bool handled = modal.HandleKey(KeyEvent.Special(KeyCode.Escape));
                        MuxAssert.IsTrue(handled, "Esc handled");
                        MuxAssert.IsTrue(modal.Completion.IsCompleted, "modal completed");
                        return Task.CompletedTask;
                    })
                });
        }

        #region Helpers

        private static UsageChartsModal NewModal()
        {
            return new UsageChartsModal("Usage", _ => SampleData(), UsageRange.Day, (d, dim) => DimensionData(dim), (f, t) => CallsPage(3));
        }

        // Seven hourly buckets with calls, errors, tokens by type, cost, and timing distributions.
        private static UsageChartData SampleData()
        {
            UsageChartData data = new UsageChartData { BucketMs = HourMs, WindowFromUnixMs = Start, WindowToUnixMs = Start + 7 * HourMs };
            data.HeaderLines.Add("98k tok · $3.20 · 64 calls · 4 err (6.3%)");
            string[] labels = { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" };
            long[] calls = { 4, 8, 6, 14, 10, 3, 12 };
            long[] errors = { 0, 1, 0, 2, 0, 0, 1 };
            long[] tokens = { 1000, 4000, 2500, 8000, 6000, 1500, 9000 };
            double[] costs = { 0.10, 0.40, 0.25, 0.80, 0.60, 0.15, 0.90 };
            double[] total = { 800, 2600, 1400, 6400, 3200, 900, 5000 };
            for (int i = 0; i < labels.Length; i++)
            {
                data.BucketStartsUnixMs.Add(Start + i * HourMs);
                data.BucketLabels.Add(labels[i]);
                data.Buckets.Add(new UsageMetrics
                {
                    Calls = calls[i],
                    Errors = errors[i],
                    InputTokens = tokens[i] * 6 / 10,
                    CachedTokens = tokens[i] * 2 / 10,
                    OutputTokens = tokens[i] * 4 / 10,
                    TotalTokens = tokens[i],
                    CostUsd = costs[i],
                    TotalMsDist = Dist(total[i]),
                    TtftMsDist = Dist(total[i] / 5),
                    StreamMsDist = Dist(total[i] * 0.8),
                    ThroughputDist = Dist(20 + i * 2)
                });
            }

            data.ErrorCounts.Add(new UsageErrorCount { ErrorCode = "rate_limited", Count = 3 });
            data.ErrorCounts.Add(new UsageErrorCount { ErrorCode = "timeout", Count = 1 });
            return data;
        }

        private static UsageDistribution Dist(double avg)
        {
            return new UsageDistribution { Min = avg * 0.25, Avg = avg, P95 = avg * 1.5, P99 = avg * 1.7, Max = avg * 1.8, Count = 5 };
        }

        private static UsageDimensionData DimensionData(string dimension)
        {
            UsageDimensionData data = new UsageDimensionData { Dimension = dimension };
            string[] keys = { dimension + "-a", dimension + "-b", "Other" };
            for (int s = 0; s < keys.Length; s++)
            {
                UsageSeries series = new UsageSeries { Key = keys[s], IsOther = s == 2 };
                for (int i = 0; i < 7; i++)
                {
                    series.Buckets.Add(new UsageBucket { BucketStartUnixMs = Start + i * HourMs, Metrics = new UsageMetrics { TotalTokens = (3 - s) * 100 * (i + 1), Calls = (s + 1) * (i + 1), CostUsd = (3 - s) * 0.01 * (i + 1) } });
                }

                data.Series.Add(series);
            }

            data.Rows.Add(new UsageBreakdownRow { Dimension = dimension, Value = dimension + "-a", Metrics = new UsageMetrics { TotalTokens = 12000, InputTokens = 8000, CachedTokens = 2000, OutputTokens = 4000, CostUsd = 1.2, Calls = 30 } });
            data.Rows.Add(new UsageBreakdownRow { Dimension = dimension, Value = dimension + "-b", Metrics = new UsageMetrics { TotalTokens = 6000, InputTokens = 4000, CachedTokens = 1000, OutputTokens = 2000, CostUsd = 0.6, Calls = 20 } });
            return data;
        }

        private static UsageEventPage CallsPage(int count)
        {
            UsageEventPage page = new UsageEventPage { TotalCount = count + 10, PageNumber = 1, PageSize = 200 };
            for (int i = 0; i < count; i++)
            {
                page.Items.Add(new UsageEventRow
                {
                    TimestampUnixMs = Start + 5 * HourMs + i * 60_000L,
                    Model = "gpt-test",
                    EndpointName = "ep-main",
                    TotalTokens = 1200,
                    TotalMs = 1500,
                    Success = i % 3 != 1,
                    ErrorCode = i % 3 == 1 ? "rate_limited" : null
                });
            }

            return page;
        }

        // A heatmap with 100 tokens in every past cell of a layout.
        private static UsageChartData HeatmapData(UsageHeatmapLayout layout)
        {
            UsageHeatmap heatmap = new UsageHeatmap(layout);
            for (int i = 0; i < layout.CellCount; i++)
            {
                if (!layout.IsOutsideWindow(i)) heatmap.TotalTokens[i] = 100;
            }

            return new UsageChartData { Heatmap = heatmap };
        }

        private static int PastCells(UsageHeatmapLayout layout)
        {
            int past = 0;
            for (int i = 0; i < layout.CellCount; i++)
            {
                if (!layout.IsOutsideWindow(i)) past++;
            }

            return past;
        }

        // The top-left corner of the first peak cell, or (-1, -1).
        private static (int Left, int Top) FirstPeak(CellBuffer buffer)
        {
            for (int y = 0; y < buffer.Height - 4; y++)
                for (int x = 0; x < buffer.Width; x++)
                    if (buffer.Get(x, y).Style.Background == Peak) return (x, y);
            return (-1, -1);
        }

        // Whether the heatmap footer says the grid scrolls sideways ("columns 20–53/53").
        private static bool Scrolls(string text)
        {
            return Regex.IsMatch(text, "columns \\d+–\\d+/\\d+");
        }

        // Counts the peak-colored characters above the footer.
        private static int CountPeak(CellBuffer buffer)
        {
            int count = 0;
            for (int y = 0; y < buffer.Height - 5; y++)
                for (int x = 0; x < buffer.Width; x++)
                    if (buffer.Get(x, y).Style.Background == Peak) count++;
            return count;
        }

        // The heatmap's x-axis label line: the line after the axis caption (or after the first line holding "Columns:").
        private static string HeaderLine(string text)
        {
            string[] lines = text.Split('\n');
            int caption = Array.FindIndex(lines, l => l.Contains("Columns:", StringComparison.Ordinal));
            string line = caption >= 0 && caption + 1 < lines.Length ? lines[caption + 1] : string.Empty;
            return line.Replace("│", " ", StringComparison.Ordinal).Replace("║", " ", StringComparison.Ordinal).Trim();
        }

        // Counts the top-left corners of peak cells, excluding the legend at the bottom.
        private static int CountSpots(CellBuffer buffer, int width, int height)
        {
            int spots = 0;
            for (int y = 1; y < height - 6; y++)
                for (int x = 1; x < width - 3; x++)
                    if (buffer.Get(x, y).Style.Background == Peak
                        && buffer.Get(x - 1, y).Style.Background != Peak
                        && buffer.Get(x, y - 1).Style.Background != Peak) spots++;
            return spots;
        }

        // The x ranges (inclusive) of the runs of peak cells on one line.
        private static List<(int Left, int Right)> PeakRuns(CellBuffer buffer, int y)
        {
            List<(int Left, int Right)> runs = new List<(int Left, int Right)>();
            for (int x = 0; x < buffer.Width; x++)
            {
                if (buffer.Get(x, y).Style.Background != Peak) continue;
                int start = x;
                while (x + 1 < buffer.Width && buffer.Get(x + 1, y).Style.Background == Peak) x++;
                runs.Add((start, x));
            }

            return runs;
        }

        // The foreground colors of full bar blocks inside the plot area.
        private static HashSet<Color> BarColors(CellBuffer buffer)
        {
            HashSet<Color> colors = new HashSet<Color>();
            for (int y = 5; y < buffer.Height - 3; y++)
                for (int x = 13; x < buffer.Width - 3; x++)
                {
                    Cell cell = buffer.Get(x, y);
                    if (cell.Grapheme == "█") colors.Add(cell.Style.Foreground);
                }

            return colors;
        }

        // The foreground colors of braille line cells (U+2801..U+28FF) in the plot area.
        private static HashSet<Color> BrailleColors(CellBuffer buffer)
        {
            HashSet<Color> colors = new HashSet<Color>();
            for (int y = 3; y < buffer.Height - 3; y++)
                for (int x = 13; x < buffer.Width - 3; x++)
                {
                    string g = buffer.Get(x, y).Grapheme;
                    if (g.Length == 1 && g[0] > '\u2800' && g[0] <= '\u28FF') colors.Add(buffer.Get(x, y).Style.Foreground);
                }

            return colors;
        }

        // The leftmost column carrying the cursor tint, or -1.
        private static int TintColumn(CellBuffer buffer)
        {
            for (int x = 0; x < buffer.Width; x++)
                for (int y = 0; y < buffer.Height; y++)
                    if (buffer.Get(x, y).Style.Background == CursorTint) return x;
            return -1;
        }

        private static CellBuffer RenderBuffer(UsageChartsModal modal, int width, int height)
        {
            CellBuffer buffer = new CellBuffer(width, height);
            modal.Render(new BufferSurface(buffer));
            return buffer;
        }

        private static string Flatten(CellBuffer buffer)
        {
            StringBuilder sb = new StringBuilder();
            for (int y = 0; y < buffer.Height; y++)
            {
                for (int x = 0; x < buffer.Width; x++) sb.Append(buffer.Get(x, y).Grapheme);
                sb.Append('\n');
            }

            return sb.ToString();
        }

        // Renders the modal into a headless buffer and flattens it to a single string for substring assertions.
        private static string Render(UsageChartsModal modal, int width, int height)
        {
            return Flatten(RenderBuffer(modal, width, height));
        }

        #endregion
    }
}
