namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using System.Threading.Tasks;
    using Mux.Cli.App;
    using Mux.Core.Approvals;
    using Mux.Core.Models;
    using TUIKit;
    using TUIKit.Input;
    using TUIKit.Modals;
    using Touchstone.Core;

    /// <summary>Deterministic countdown, settings, and artwork tests.</summary>
    public static class ApprovalCountdownSuite
    {
        /// <summary>Builds the suite.</summary>
        public static TestSuiteDescriptor Create() => new TestSuiteDescriptor("ApprovalCountdown", "Timed interactive approvals and idle artwork", new List<TestCaseDescriptor>
        {
            new TestCaseDescriptor("ApprovalCountdown", "VisibilityAndZero", "Countdown starts on display, pauses when covered, and zero never expires", ct =>
            {
                Clock clock = new Clock();
                ApprovalCountdown countdown = new ApprovalCountdown(10, clock);
                clock.Advance(100); countdown.Update(true);
                MuxAssert.AreEqual(10, countdown.RemainingSeconds, "not started while queued");
                clock.Advance(9); MuxAssert.IsFalse(countdown.Update(true), "nine seconds not enough");
                MuxAssert.AreEqual(1, countdown.RemainingSeconds, "remaining label");
                countdown.Update(false); clock.Advance(100); countdown.Update(false); countdown.Update(true);
                MuxAssert.AreEqual(1, countdown.RemainingSeconds, "hidden time excluded");
                clock.Advance(1); MuxAssert.IsTrue(countdown.Update(true), "expires after ten visible seconds");
                ApprovalCountdown disabled = new ApprovalCountdown(0, clock);
                disabled.Update(true); clock.Advance(100000);
                MuxAssert.IsFalse(disabled.Update(true), "zero stays disabled");
                return Task.CompletedTask;
            }),
            new TestCaseDescriptor("ApprovalCountdown", "ModalDecisions", "Visible countdown approves once; denial or escape wins and covered prompts wait", async ct =>
            {
                foreach (int choice in new[] { 0, 1, 2 })
                {
                    Clock clock = new Clock();
                    ToolApprovalModal modal = new ToolApprovalModal("write_file", 10, clock);
                    ModalStack stack = new ModalStack(); stack.Push(modal);
                    BufferSurface surface = new BufferSurface(new CellBuffer(100, 30));
                    modal.Render(surface);
                    if (choice == 1) modal.HandleKey(KeyEvent.Special(KeyCode.Escape));
                    if (choice == 2) { modal.HandleKey(KeyEvent.Special(KeyCode.Down)); modal.HandleKey(KeyEvent.Special(KeyCode.Enter)); }
                    clock.Advance(10); modal.Render(surface);
                    MuxAssert.AreEqual(choice == 0 ? 0 : choice == 1 ? -1 : 1, (int)(await modal.Completion)!, "first decision wins");
                }
                Clock pausedClock = new Clock();
                ToolApprovalModal covered = new ToolApprovalModal("run_process", 1, pausedClock);
                ModalStack modals = new ModalStack(); modals.Push(covered);
                modals.Push(new SelectModal("other", new[] { "Close" }));
                covered.Render(new BufferSurface(new CellBuffer(80, 24))); pausedClock.Advance(20);
                covered.Render(new BufferSurface(new CellBuffer(80, 24)));
                MuxAssert.IsFalse(covered.Completion.IsCompleted, "covered prompt cannot expire");
            }),
            new TestCaseDescriptor("ApprovalCountdown", "SettingsRoundTrip", "Unset endpoint inherits, explicit zero disables, and settings survive JSON and DTO roundtrips", ct =>
            {
                MuxSettings settings = JsonSerializer.Deserialize<MuxSettings>("{\"toolAutoApproveSeconds\":10}")!;
                foreach (int? value in new int?[] { null, 0, 3 })
                {
                    EndpointConfig endpoint = new EndpointConfig { ToolAutoApproveSeconds = value };
                    EndpointConfig restored = JsonSerializer.Deserialize<EndpointConfig>(JsonSerializer.Serialize(endpoint))!;
                    MuxAssert.AreEqual(value ?? 10, restored.ToolAutoApproveSeconds ?? settings.ToolAutoApproveSeconds, "override precedence");
                }
                MuxAssert.AreEqual(0, new MuxSettings().ToolAutoApproveSeconds, "global default off");
                MuxAssert.IsTrue(new EndpointConfig().ToolAutoApproveSeconds == null, "old endpoint inherits");
                MuxSettings copy = new MuxSettings();
                Mux.Server.Models.SettingsDto.FromSettings(settings).ApplyTo(copy);
                MuxAssert.AreEqual(10, copy.ToolAutoApproveSeconds, "REST settings roundtrip");
                settings.ToolAutoApproveSeconds = -2; MuxAssert.AreEqual(0, settings.ToolAutoApproveSeconds, "negative disabled");
                return Task.CompletedTask;
            }),
            new TestCaseDescriptor("ApprovalCountdown", "ArtworkUsesCanonicalBlocks", "Watermark reproduces the actual banner and enlarges filled half-cells without letter tiles or stripes", ct =>
            {
                var lines = MuxBanner.WordmarkLines();
                foreach (int scale in new[] { 1, 2, 3 })
                {
                    int width = lines[0].Length * scale;
                    int height = lines.Count * scale;
                    CellBuffer buffer = new CellBuffer(width * 3, height * 3);
                    IdleMuxArtwork.Render(new BufferSurface(buffer));
                    for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
                    {
                        string glyph = buffer.Get(width + x, height + y).Grapheme;
                        MuxAssert.IsTrue(glyph == " " || glyph == "█" || glyph == "▀" || glyph == "▄", "only block glyphs");
                        if (scale == 1) MuxAssert.AreEqual(lines[y][x].ToString(), glyph, "canonical banner at native size");
                        // Compare the rendered halves with the corresponding source halves, including
                        // spaces within letters: this catches repeated half-block stripes when enlarged.
                        for (int half = 0; half < 2; half++)
                        {
                            int sourceHalf = (y * 2 + half) / scale;
                            char source = lines[sourceHalf / 2][x / scale];
                            bool expected = source == '█' || (source == '▀' && sourceHalf % 2 == 0) || (source == '▄' && sourceHalf % 2 == 1);
                            bool actual = glyph == "█" || (glyph == "▀" && half == 0) || (glyph == "▄" && half == 1);
                            MuxAssert.AreEqual(expected, actual, "scaled silhouette");
                        }
                    }
                }
                return Task.CompletedTask;
            }),
            new TestCaseDescriptor("ApprovalCountdown", "ArtworkGeometry", "Canonical block wordmark scales within one third, stays faint, and preserves foreground text", ct =>
            {
                foreach (var size in new[] { (90, 24), (180, 60), (240, 90) })
                {
                    CellBuffer buffer = new CellBuffer(size.Item1, size.Item2);
                    BufferSurface surface = new BufferSurface(buffer);
                    surface.DrawText(size.Item1 / 2, size.Item2 / 2, "foreground", CellStyle.Default);
                    IdleMuxArtwork.Render(surface);
                    int minX = size.Item1, maxX = 0, minY = size.Item2, maxY = 0;
                    int count = 0;
                    for (int y = 0; y < size.Item2; y++) for (int x = 0; x < size.Item1; x++)
                    {
                        Cell cell = buffer.Get(x, y);
                        if (cell.Style.Foreground == Color.FromRgb(32, 32, 32))
                        { count++; minX = Math.Min(x, minX); maxX = Math.Max(x, maxX); minY = Math.Min(y, minY); maxY = Math.Max(y, maxY); }
                    }
                    MuxAssert.IsTrue(count > 0, "art rendered");
                    MuxAssert.AreEqual(size.Item1 / 3, maxX - minX + 1, "one third width");
                    MuxAssert.IsTrue(maxY - minY + 1 <= size.Item2 / 3, "fits within one third height");
                    double visibleHeight = (maxX - minX + 1) * 5.0 / 19 / 2;
                    MuxAssert.IsTrue(Math.Abs(maxY - minY + 1 - visibleHeight) <= 1, "banner proportions preserved within one terminal cell");
                    MuxAssert.AreEqual("f", buffer.Get(size.Item1 / 2, size.Item2 / 2).Grapheme, "foreground preserved");
                }
                return Task.CompletedTask;
            })
        });
        private sealed class Clock : TimeProvider
        {
            private long _Ticks;
            public override long TimestampFrequency => TimeSpan.TicksPerSecond;
            public override long GetTimestamp() => _Ticks;
            internal void Advance(int seconds) => _Ticks += TimeSpan.FromSeconds(seconds).Ticks;
        }
    }
}
