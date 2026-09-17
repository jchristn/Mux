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
                        MuxAssert.Contains("page 1/6", rendered, "page indicator");
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
                        Render(modal, 14, 7);
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
