namespace Test.Shared.Suites
{
    using System.Collections.Generic;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Cli.App;
    using Touchstone.Core;
    using TUIKit;

    /// <summary>
    /// Touchstone suite for <see cref="UsageChartsModal"/>: it renders the KPI header and chart sections into
    /// a headless surface without overflowing or throwing, degrades to just the header when a series is empty,
    /// and closes on Enter/Escape.
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
                "Usage charts modal rendering",
                new List<TestCaseDescriptor>
                {
                    new TestCaseDescriptor(SuiteId, "RendersHeaderAndCharts", "The modal renders KPI header and chart sections", (CancellationToken ct) =>
                    {
                        UsageChartData data = SampleData();
                        UsageChartsModal modal = new UsageChartsModal("Usage", data);
                        // Tall enough to hold every section: the header, the tokens line chart, the cost bar
                        // chart (one row per day), and the top-models bar chart all fit without the last
                        // section being room-skipped. Truncation on a short terminal is covered separately.
                        string rendered = Render(modal, 80, 32);

                        MuxAssert.Contains("Usage", rendered, "title drawn");
                        MuxAssert.Contains("24h:", rendered, "24h KPI line drawn");
                        MuxAssert.Contains("Tokens per day", rendered, "tokens section heading drawn");
                        MuxAssert.Contains("Top models by cost", rendered, "models section heading drawn");
                        MuxAssert.Contains("Enter / Esc to close", rendered, "hint drawn");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor(SuiteId, "EmptySeriesRendersHeaderOnly", "With no series the modal still renders the header without throwing", (CancellationToken ct) =>
                    {
                        UsageChartData data = new UsageChartData();
                        data.HeaderLines.Add("24h:  0 tok · $0.00 · 0 calls · 0 err");
                        UsageChartsModal modal = new UsageChartsModal("Usage", data);
                        string rendered = Render(modal, 80, 24);

                        MuxAssert.Contains("24h:", rendered, "header drawn");
                        MuxAssert.DoesNotContain("Tokens per day", rendered, "no chart section when series empty");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor(SuiteId, "NarrowSurfaceDoesNotThrow", "Rendering onto a tiny surface truncates without throwing", (CancellationToken ct) =>
                    {
                        UsageChartsModal modal = new UsageChartsModal("Usage", SampleData());
                        Render(modal, 12, 6);
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor(SuiteId, "EnterClosesModal", "Enter closes the modal with a null result", (CancellationToken ct) =>
                    {
                        UsageChartsModal modal = new UsageChartsModal("Usage", SampleData());
                        bool handled = modal.HandleKey(TUIKit.Input.KeyEvent.Special(TUIKit.Input.KeyCode.Enter));
                        MuxAssert.IsTrue(handled, "Enter handled");
                        MuxAssert.IsTrue(modal.Completion.IsCompleted, "modal completed");
                        return Task.CompletedTask;
                    })
                });
        }

        private static UsageChartData SampleData()
        {
            UsageChartData data = new UsageChartData();
            data.HeaderLines.Add("24h:  12.3k tok · $0.45 · 8 calls · 0 err");
            data.HeaderLines.Add("7d:   98k tok · $3.20 · 64 calls · 1 err");
            string[] days = { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" };
            double[] tokens = { 1000, 4000, 2500, 8000, 6000, 1500, 9000 };
            double[] costs = { 0.10, 0.40, 0.25, 0.80, 0.60, 0.15, 0.90 };
            for (int i = 0; i < days.Length; i++)
            {
                data.DayLabels.Add(days[i]);
                data.TokensPerDay.Add(tokens[i]);
                data.CostPerDay.Add(costs[i]);
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
