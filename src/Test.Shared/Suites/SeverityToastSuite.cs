namespace Test.Shared.Suites
{
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Cli.App;
    using Touchstone.Core;
    using TUIKit;
    using TUIKit.Modals;

    /// <summary>Verifies toast colors, complete padding, expiry, and severity stacking.</summary>
    public static class SeverityToastSuite
    {
        /// <summary>Builds the severity toast test suite.</summary>
        public static TestSuiteDescriptor Create()
        {
            const string id = "SeverityToast";
            return new TestSuiteDescriptor(id, "TUI severity toast rendering", new List<TestCaseDescriptor>
            {
                new TestCaseDescriptor(id, "ColorsAndPadding", "Every severity colors both the message and its padding", (CancellationToken ct) =>
                {
                    foreach (NotificationSeverity severity in new[] { NotificationSeverity.Info, NotificationSeverity.Success, NotificationSeverity.Warning, NotificationSeverity.Error })
                    {
                        NotificationCenter center = new NotificationCenter();
                        center.Add("notice", severity, 0, 100);
                        CellBuffer buffer = new CellBuffer(80, 24);
                        SeverityToastRenderer.Render(new BufferSurface(buffer), center, 1);
                        Color background = severity == NotificationSeverity.Warning || severity == NotificationSeverity.Error
                            ? Color.FromRgb(185, 28, 28) : Color.FromRgb(22, 163, 74);
                        Color foreground = severity == NotificationSeverity.Warning ? Color.FromRgb(255, 224, 102) : Color.FromRgb(255, 255, 255);
                        int painted = 0, spaces = 0;
                        for (int y = 0; y < 24; y++)
                            for (int x = 0; x < 80; x++)
                            {
                                Cell cell = buffer.Get(x, y);
                                if (cell.Style.Background != background) continue;
                                painted++;
                                if (cell.Grapheme == " ") spaces++;
                                MuxAssert.AreEqual(foreground, cell.Style.Foreground, "foreground for " + severity);
                            }
                        MuxAssert.IsTrue(painted >= 8 && painted < 80, "only toast cells painted for " + severity);
                        MuxAssert.IsTrue(spaces >= 2, "leading and trailing padding colored for " + severity);
                    }
                    return Task.CompletedTask;
                }),
                new TestCaseDescriptor(id, "MixedSeverityAndExpiry", "Mixed severities retain separate colors and expired toasts disappear", (CancellationToken ct) =>
                {
                    NotificationCenter center = new NotificationCenter();
                    center.Add("information", NotificationSeverity.Info, 0, 100);
                    center.Add("warning", NotificationSeverity.Warning, 0, 10);
                    CellBuffer buffer = new CellBuffer(80, 24);
                    SeverityToastRenderer.Render(new BufferSurface(buffer), center, 1);
                    bool green = false, red = false;
                    for (int y = 0; y < 24; y++)
                        for (int x = 0; x < 80; x++)
                        {
                            green |= buffer.Get(x, y).Style.Background == Color.FromRgb(22, 163, 74);
                            red |= buffer.Get(x, y).Style.Background == Color.FromRgb(185, 28, 28);
                        }
                    MuxAssert.IsTrue(green && red, "each toast keeps its own severity colors");
                    buffer.Clear(CellStyle.Default);
                    SeverityToastRenderer.Render(new BufferSurface(buffer), center, 101);
                    for (int y = 0; y < 24; y++)
                        for (int x = 0; x < 80; x++)
                            MuxAssert.AreEqual(CellStyle.Default, buffer.Get(x, y).Style, "expired toast leaves surface unchanged");
                    return Task.CompletedTask;
                }),
                new TestCaseDescriptor(id, "NarrowUnicodeSurface", "Unicode notifications clip safely on narrow surfaces", (CancellationToken ct) =>
                {
                    NotificationCenter center = new NotificationCenter();
                    center.Add("你好 👋 warning", NotificationSeverity.Warning, 0, 100);
                    for (int width = 1; width <= 12; width++)
                        SeverityToastRenderer.Render(new BufferSurface(new CellBuffer(width, 2)), center, 1);
                    return Task.CompletedTask;
                })
            });
        }
    }
}
