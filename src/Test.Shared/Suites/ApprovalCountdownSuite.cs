namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using System.Text.Json;
    using System.Threading.Tasks;
    using Mux.Cli.App;
    using Mux.Core.Approvals;
    using Mux.Core.Branding;
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
                    IdleMuxArtwork.Render(new BufferSurface(buffer), BlockArtMode.Glyphs);
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
                    IdleMuxArtwork.Render(surface, BlockArtMode.Glyphs);
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
            }),
            new TestCaseDescriptor("ApprovalCountdown", "ArtworkCellsMode", "In cell mode the watermark is faint background-coloured spaces, no block glyphs, at the glyph mode's size and place", ct =>
            {
                bool[,] half = BlockArt.HalfCells(BlockArt.Wordmark);
                int sourceHalfRows = half.GetLength(0), sourceWidth = half.GetLength(1);
                foreach (var size in new[] { (114, 18), (90, 24), (171, 27), (180, 60), (240, 90) })
                {
                    CellBuffer glyphs = new CellBuffer(size.Item1, size.Item2);
                    IdleMuxArtwork.Render(new BufferSurface(glyphs), BlockArtMode.Glyphs);
                    CellBuffer cells = new CellBuffer(size.Item1, size.Item2);
                    BufferSurface surface = new BufferSurface(cells);
                    surface.DrawText(size.Item1 / 2, size.Item2 / 2, "foreground", CellStyle.Default);
                    IdleMuxArtwork.Render(surface, BlockArtMode.Cells);
                    (int minX, int maxX, int minY, int maxY) g = (int.MaxValue, -1, int.MaxValue, -1), c = (int.MaxValue, -1, int.MaxValue, -1);
                    for (int y = 0; y < size.Item2; y++) for (int x = 0; x < size.Item1; x++)
                    {
                        if (glyphs.Get(x, y).Style.Foreground == IdleMuxArtwork.Faint) g = (Math.Min(g.minX, x), Math.Max(g.maxX, x), Math.Min(g.minY, y), Math.Max(g.maxY, y));
                        Cell cell = cells.Get(x, y);
                        string glyph = cell.Grapheme;
                        MuxAssert.IsFalse(glyph == "█" || glyph == "▀" || glyph == "▄", "no block glyphs in cell mode");
                        MuxAssert.IsFalse(cell.Style.Foreground == IdleMuxArtwork.Faint, "faint colour is a background, not a foreground");
                        if (cell.Style.Background == IdleMuxArtwork.Faint)
                        {
                            MuxAssert.AreEqual(" ", glyph, "filled cells are spaces");
                            c = (Math.Min(c.minX, x), Math.Max(c.maxX, x), Math.Min(c.minY, y), Math.Max(c.maxY, y));
                        }
                    }

                    MuxAssert.IsTrue(c.maxX >= 0, "art rendered at " + size);
                    MuxAssert.AreEqual(g.minX, c.minX, "same left edge as glyph mode at " + size);
                    MuxAssert.AreEqual(g.maxX, c.maxX, "same right edge as glyph mode at " + size);
                    MuxAssert.IsTrue(c.minY >= g.minY && c.maxY <= g.maxY, "within the glyph mode's rows at " + size);
                    MuxAssert.AreEqual(size.Item1 / 3, c.maxX - c.minX + 1, "one third width");
                    MuxAssert.AreEqual("f", cells.Get(size.Item1 / 2, size.Item2 / 2).Grapheme, "foreground preserved");

                    // The same size and place as glyph mode; each terminal row samples the art at its centre and each
                    // column at its left edge, so every cell is either filled or blank.
                    int width = Math.Min(size.Item1 / 3, size.Item2 / 3 * sourceWidth / 3);
                    int height = ((width * sourceHalfRows + sourceWidth / 2) / sourceWidth + 1) / 2;
                    int left = (size.Item1 - width) / 2, top = (size.Item2 - height) / 2;
                    for (int y = 0; y < size.Item2; y++) for (int x = 0; x < size.Item1; x++)
                    {
                        bool inside = x >= left && x < left + width && y >= top && y < top + height;
                        bool expected = inside && half[(2 * (y - top) + 1) * sourceHalfRows / (2 * height), (x - left) * sourceWidth / width];
                        Cell cell = cells.Get(x, y);
                        if (cell.Grapheme != " ") continue; // foreground text wins
                        MuxAssert.AreEqual(expected, cell.Style.Background == IdleMuxArtwork.Faint, "silhouette at " + x + "," + y + " for " + size);
                    }
                }

                // At twice the art's size the watermark is exactly the terminal-cell wordmark.
                CellBuffer exact = new CellBuffer(114, 18);
                IdleMuxArtwork.Render(new BufferSurface(exact), BlockArtMode.Cells);
                bool[,] wordmark = BlockArt.TerminalCells(BlockArt.Wordmark);
                int exactLeft = (114 - 38) / 2, exactTop = (18 - 6) / 2;
                for (int y = 0; y < 18; y++) for (int x = 0; x < 114; x++)
                {
                    bool inside = x >= exactLeft && x < exactLeft + 38 && y >= exactTop && y < exactTop + 6;
                    MuxAssert.AreEqual(inside && wordmark[y - exactTop, x - exactLeft], exact.Get(x, y).Style.Background == IdleMuxArtwork.Faint, "38x6 wordmark at " + x + "," + y);
                }

                CellBuffer small = new CellBuffer(50, 10);
                IdleMuxArtwork.Render(new BufferSurface(small), BlockArtMode.Cells);
                for (int y = 0; y < 10; y++) for (int x = 0; x < 50; x++) MuxAssert.IsFalse(small.Get(x, y).Style.Background == IdleMuxArtwork.Faint, "too small a pane draws nothing");
                return Task.CompletedTask;
            }),
            new TestCaseDescriptor("ApprovalCountdown", "BlockArtModeSelection", "MUX_BLOCK_ART picks cells or glyphs; otherwise Terminal.app gets cells and every other terminal glyphs", ct =>
            {
                Func<string, string?> Env(string? termProgram, string? choice) => name => name == "TERM_PROGRAM" ? termProgram : name == BlockArt.ModeVariable ? choice : null;
                MuxAssert.AreEqual(BlockArtMode.Cells, BlockArt.TerminalMode(Env("Apple_Terminal", null)), "Terminal.app");
                MuxAssert.AreEqual(BlockArtMode.Glyphs, BlockArt.TerminalMode(Env("iTerm.app", null)), "iTerm2");
                MuxAssert.AreEqual(BlockArtMode.Glyphs, BlockArt.TerminalMode(Env("vscode", null)), "VS Code");
                MuxAssert.AreEqual(BlockArtMode.Glyphs, BlockArt.TerminalMode(Env(null, null)), "unknown terminal");
                MuxAssert.AreEqual(BlockArtMode.Cells, BlockArt.TerminalMode(Env("iTerm.app", "cells")), "override to cells");
                MuxAssert.AreEqual(BlockArtMode.Cells, BlockArt.TerminalMode(Env(null, " CELLS ")), "override is case-insensitive");
                MuxAssert.AreEqual(BlockArtMode.Glyphs, BlockArt.TerminalMode(Env("Apple_Terminal", "glyphs")), "override to glyphs");
                MuxAssert.AreEqual(BlockArtMode.Cells, BlockArt.TerminalMode(Env("Apple_Terminal", "bogus")), "an unknown override falls back to detection");
                return Task.CompletedTask;
            }),
            new TestCaseDescriptor("ApprovalCountdown", "BannerGlyphsUnchanged", "Glyph mode prints the same three wordmark rows, shared with the desktop's BlockArt.Wordmark", ct =>
            {
                string[] expected = { "▄▄   ▄▄ ▄▄ ▄▄ ▄▄ ▄▄", "██▀▄▀██ ██ ██ ▀█▄█▀", "██   ██ ▀███▀ ██ ██" };
                IReadOnlyList<string> glyphs = MuxBanner.ScrollbackLines(BlockArtMode.Glyphs);
                MuxAssert.AreEqual(3, glyphs.Count, "three rows");
                for (int i = 0; i < 3; i++)
                {
                    MuxAssert.AreEqual(expected[i], glyphs[i], "row " + i);
                    MuxAssert.AreEqual(BlockArt.Wordmark[i], MuxBanner.WordmarkLines()[i], "TUI and desktop share the wordmark, row " + i);
                    MuxAssert.AreEqual(expected[i], MuxBanner.SplashLines("1.0")[i], "splash starts with the wordmark, row " + i);
                }

                MuxAssert.AreEqual(3 + MuxBanner.SplashText("1.0").Count, MuxBanner.SplashLines("1.0").Count, "splash is wordmark plus text");
                return Task.CompletedTask;
            }),
            new TestCaseDescriptor("ApprovalCountdown", "BannerCellsMode", "Cell mode prints the wordmark as 38x6 reverse-video spaces, filled exactly where the art's half-cells are, reset after each line", ct =>
            {
                IReadOnlyList<string> lines = MuxBanner.ScrollbackLines(BlockArtMode.Cells);
                bool[,] half = BlockArt.HalfCells(BlockArt.Wordmark);
                MuxAssert.AreEqual(6, lines.Count, "one line per half-row");
                for (int y = 0; y < lines.Count; y++)
                {
                    string line = lines[y];
                    MuxAssert.IsTrue(line.EndsWith("\u001b[0m", StringComparison.Ordinal), "reset at end of line " + y);
                    List<bool> filled = new List<bool>();
                    bool reverse = false;
                    for (int i = 0; i < line.Length; i++)
                    {
                        if (line[i] == '\u001b')
                        {
                            int end = line.IndexOf('m', i);
                            string code = line.Substring(i + 2, end - i - 2);
                            MuxAssert.IsTrue(line[i + 1] == '[' && (code == "7" || code == "27" || code == "0"), "only reverse on/off and reset: " + code);
                            reverse = code == "7";
                            i = end;
                            continue;
                        }

                        MuxAssert.AreEqual(' ', line[i], "only spaces");
                        filled.Add(reverse);
                    }

                    MuxAssert.IsFalse(reverse, "line " + y + " ends with reverse off");
                    MuxAssert.AreEqual(38, filled.Count, "38 columns on line " + y);
                    for (int x = 0; x < 38; x++) MuxAssert.AreEqual(half[y, x / 2], filled[x], "cell " + x + "," + y);
                }

                return Task.CompletedTask;
            }),
            new TestCaseDescriptor("ApprovalCountdown", "SplashCellsMode", "The splash box draws the wordmark as reverse-video cells above its text, with no block glyphs", ct =>
            {
                CellBuffer buffer = new CellBuffer(80, 24);
                new MuxBoxModal("mux", MuxBanner.SplashText("1.0"), "press any key to start", centered: true, blockArt: MuxBanner.WordmarkLines()).Render(new BufferSurface(buffer));
                bool[,] cells = BlockArt.TerminalCells(BlockArt.Wordmark);
                int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1, taglineRow = -1;
                for (int y = 0; y < 24; y++)
                {
                    StringBuilder row = new StringBuilder();
                    for (int x = 0; x < 80; x++)
                    {
                        Cell cell = buffer.Get(x, y);
                        row.Append(cell.Grapheme);
                        MuxAssert.IsFalse(cell.Grapheme == "█" || cell.Grapheme == "▀" || cell.Grapheme == "▄", "no block glyphs");
                        if ((cell.Style.Attributes & CellAttributes.Reverse) != 0)
                        {
                            MuxAssert.AreEqual(" ", cell.Grapheme, "art cells are spaces");
                            minX = Math.Min(minX, x); maxX = Math.Max(maxX, x); minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
                        }
                    }

                    if (row.ToString().Contains("AI agent for local and remote LLMs")) taglineRow = y;
                }

                // The wordmark's top half-row is empty (its first row is all lower half blocks), so the filled cells
                // span 38x5 of the 38x6 grid, starting on the grid's second row.
                MuxAssert.AreEqual(38, maxX - minX + 1, "art width");
                MuxAssert.AreEqual(5, maxY - minY + 1, "filled rows of the 6-row grid");
                int gridTop = minY - 1;
                for (int y = 0; y < 6; y++) for (int x = 0; x < 38; x++)
                    MuxAssert.AreEqual(cells[y, x], (buffer.Get(minX + x, gridTop + y).Style.Attributes & CellAttributes.Reverse) != 0, "splash art cell " + x + "," + y);
                MuxAssert.AreEqual(maxY + 2, taglineRow, "tagline follows the art after one blank line");
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
