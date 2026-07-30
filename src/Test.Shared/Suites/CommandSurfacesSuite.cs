namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Runtime.CompilerServices;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Cli.App;
    using Mux.Core.Agent;
    using Mux.Core.Enums;
    using Mux.Core.Jobs;
    using Touchstone.Core;
    using TUIKit.Terminal;
    using TUIKit.Widgets;

    /// <summary>
    /// Touchstone suite for the M10 command surfaces: the slash router, the fuzzy command palette, key
    /// bindings, and the catalog-derived menu. Verifies that the four surfaces all resolve against the
    /// single <see cref="MuxCommandCatalog"/> and converge on identical command handlers.
    /// </summary>
    public static class CommandSurfacesSuite
    {
        private const string SuiteId = "CommandSurfaces";

        /// <summary>
        /// Builds the command-surfaces suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/> for command-surface cases.</returns>
        public static TestSuiteDescriptor Create()
        {
            return new TestSuiteDescriptor(
                SuiteId,
                "Slash router, command palette, key bindings, and menu convergence",
                new List<TestCaseDescriptor>
                {
                    // ---- Slash parser (unit) ----
                    Case("SlashResolvesByAlias", "The slash parser resolves a command by alias", async (CancellationToken ct) =>
                    {
                        await using (JobManager manager = NewManager())
                        using (MuxTuiApp app = NewApp(out _, manager))
                        {
                            await Task.CompletedTask.ConfigureAwait(false);
                            SlashCommandParser parser = new SlashCommandParser(app.Catalog);
                            MuxAssert.AreEqual("mux.clear", parser.Resolve("/clear")?.Id, "clear alias");
                            MuxAssert.AreEqual("mux.quit", parser.Resolve("/exit")?.Id, "exit alias");
                            MuxAssert.AreEqual("mux.help", parser.Resolve("/?")?.Id, "question alias");
                            MuxAssert.IsTrue(parser.Resolve("/bogus") == null, "unknown is null");
                        }
                    }),

                    Case("SlashTryHandleInvokesAndReports", "TryHandle invokes a match and reports unknowns", async (CancellationToken ct) =>
                    {
                        await using (JobManager manager = NewManager())
                        using (MuxTuiApp app = NewApp(out _, manager))
                        {
                            await Task.CompletedTask.ConfigureAwait(false);
                            MuxCommandCatalog catalog = new MuxCommandCatalog();
                            int hits = 0;
                            catalog.Add(new CommandDescriptor("t.do", "Do", null, () => hits++, "T", new[] { "do" }));
                            SlashCommandParser parser = new SlashCommandParser(catalog);

                            MuxAssert.IsTrue(parser.TryHandle("/do now"), "handled");
                            MuxAssert.AreEqual(1, hits, "invoked once");
                            MuxAssert.IsFalse(parser.TryHandle("/missing"), "unknown not handled");
                            MuxAssert.AreEqual(1, hits, "no extra invoke");
                        }
                    }),

                    // ---- Slash surface wired into the shell ----
                    Case("SlashClearClearsTranscript", "A /clear submission clears the transcript", async (CancellationToken ct) =>
                    {
                        await using (JobManager manager = NewManager())
                        using (MuxTuiApp app = NewApp(out HeadlessBackend backend, manager))
                        {
                            await Task.CompletedTask.ConfigureAwait(false);
                            MuxAssert.IsTrue(app.TranscriptSnapshot().Count > 0, "seeded");
                            Feed(backend, app, "/clear" + "\r");
                            MuxAssert.AreEqual(0, app.TranscriptSnapshot().Count, "cleared via slash");
                            MuxAssert.AreEqual(0, manager.Jobs.Count, "no job created");
                        }
                    }),

                    // ---- Palette ----
                    Case("PaletteFuzzySelectsCommand", "Typing in the palette fuzzy-selects a command", async (CancellationToken ct) =>
                    {
                        await using (JobManager manager = NewManager())
                        using (MuxTuiApp app = NewApp(out HeadlessBackend backend, manager))
                        {
                            await Task.CompletedTask.ConfigureAwait(false);
                            Feed(backend, app, new byte[] { 0x0b }); // Ctrl+K
                            MuxAssert.IsTrue(app.IsPaletteActive, "palette open");

                            Feed(backend, app, "clear");
                            MuxAssert.AreEqual("mux.clear", app.PaletteSelectionId, "selected clear");
                            MuxAssert.IsTrue(app.PaletteMatchCount >= 1, "at least one match");
                        }
                    }),

                    Case("PaletteRunInvokesSelectedCommand", "Enter runs the palette's selection", async (CancellationToken ct) =>
                    {
                        await using (JobManager manager = NewManager())
                        using (MuxTuiApp app = NewApp(out HeadlessBackend backend, manager))
                        {
                            await Task.CompletedTask.ConfigureAwait(false);
                            MuxAssert.IsTrue(app.TranscriptSnapshot().Count > 0, "seeded");

                            Feed(backend, app, new byte[] { 0x0b }); // Ctrl+K
                            Feed(backend, app, "clear");
                            Feed(backend, app, "\r"); // run

                            MuxAssert.IsFalse(app.IsPaletteActive, "palette closed");
                            MuxAssert.AreEqual(0, app.TranscriptSnapshot().Count, "cleared via palette");
                        }
                    }),

                    Case("PaletteEscapeClosesWithoutRunning", "Escape closes the palette without running", async (CancellationToken ct) =>
                    {
                        await using (JobManager manager = NewManager())
                        using (MuxTuiApp app = NewApp(out HeadlessBackend backend, manager))
                        {
                            await Task.CompletedTask.ConfigureAwait(false);
                            int before = app.TranscriptSnapshot().Count;

                            Feed(backend, app, new byte[] { 0x0b }); // Ctrl+K
                            Feed(backend, app, "clear");
                            backend.FeedInput(new byte[] { 0x1b }); // Escape
                            app.PumpInputOnce();
                            app.PumpInputOnce();

                            MuxAssert.IsFalse(app.IsPaletteActive, "palette closed");
                            MuxAssert.AreEqual(before, app.TranscriptSnapshot().Count, "nothing cleared");
                        }
                    }),

                    // ---- Key binding surface ----
                    Case("KeybindingClearsTranscript", "Ctrl+L clears the transcript", async (CancellationToken ct) =>
                    {
                        await using (JobManager manager = NewManager())
                        using (MuxTuiApp app = NewApp(out HeadlessBackend backend, manager))
                        {
                            await Task.CompletedTask.ConfigureAwait(false);
                            MuxAssert.IsTrue(app.TranscriptSnapshot().Count > 0, "seeded");
                            Feed(backend, app, new byte[] { 0x0c }); // Ctrl+L
                            MuxAssert.AreEqual(0, app.TranscriptSnapshot().Count, "cleared via keybinding");
                        }
                    }),

                    // ---- Menu surface (catalog-derived) ----
                    Case("MenuBuiltFromCatalogGroupsByCategory", "The menu bar groups catalog commands by category", async (CancellationToken ct) =>
                    {
                        await using (JobManager manager = NewManager())
                        using (MuxTuiApp app = NewApp(out _, manager))
                        {
                            await Task.CompletedTask.ConfigureAwait(false);
                            IReadOnlyList<Menu> menus = MenuBarBuilder.BuildMenus(app.Catalog);
                            List<string> titles = new List<string>();
                            foreach (Menu menu in menus)
                            {
                                titles.Add(menu.Title);
                            }

                            MuxAssert.IsTrue(titles.Contains("Session"), "has Session menu");
                            MuxAssert.IsTrue(titles.Contains("View"), "has View menu");
                            MuxAssert.IsTrue(app.MenuBar.Count > 0, "menu bar populated");
                        }
                    }),

                    Case("MenuItemInvokesSameHandler", "A menu item invokes the same handler as its command", async (CancellationToken ct) =>
                    {
                        await using (JobManager manager = NewManager())
                        using (MuxTuiApp app = NewApp(out _, manager))
                        {
                            await Task.CompletedTask.ConfigureAwait(false);
                            MuxAssert.IsTrue(app.TranscriptSnapshot().Count > 0, "seeded");

                            MenuItem? clearItem = FindItem(MenuBarBuilder.BuildMenus(app.Catalog), "Clear transcript");
                            MuxAssert.IsNotNull(clearItem, "found clear menu item");
                            clearItem!.Action!();

                            MuxAssert.AreEqual(0, app.TranscriptSnapshot().Count, "cleared via menu item");
                        }
                    }),

                    // ---- Convergence ----
                    Case("FourSurfacesConvergeOnSameCommand", "Slash, palette, keybinding, and menu resolve to one command", async (CancellationToken ct) =>
                    {
                        await using (JobManager manager = NewManager())
                        using (MuxTuiApp app = NewApp(out HeadlessBackend backend, manager))
                        {
                            await Task.CompletedTask.ConfigureAwait(false);

                            CommandDescriptor? keyCommand = null;
                            foreach (CommandDescriptor d in app.Catalog.Commands)
                            {
                                if (d.Chord == "ctrl+l") keyCommand = d;
                            }

                            SlashCommandParser parser = new SlashCommandParser(app.Catalog);
                            CommandDescriptor? slashCommand = parser.Resolve("/clear");

                            Feed(backend, app, new byte[] { 0x0b });
                            Feed(backend, app, "clear");
                            string? paletteId = app.PaletteSelectionId;

                            MenuItem? menuItem = FindItem(MenuBarBuilder.BuildMenus(app.Catalog), "Clear transcript");

                            MuxAssert.AreEqual("mux.clear", keyCommand?.Id, "keybinding -> clear");
                            MuxAssert.AreEqual("mux.clear", slashCommand?.Id, "slash -> clear");
                            MuxAssert.AreEqual("mux.clear", paletteId, "palette -> clear");
                            MuxAssert.IsNotNull(menuItem, "menu -> clear item");
                            // The menu item's action is the very same delegate the command carries.
                            MuxAssert.IsTrue(ReferenceEquals(menuItem!.Action, keyCommand!.Handler), "menu shares the command handler");
                        }
                    }),

                    // ---- Help ----
                    Case("HelpListsCatalogCommands", "Help lists commands with their key bindings", async (CancellationToken ct) =>
                    {
                        await using (JobManager manager = NewManager())
                        using (MuxTuiApp app = NewApp(out HeadlessBackend backend, manager))
                        {
                            await Task.CompletedTask.ConfigureAwait(false);
                            Feed(backend, app, "/help" + "\r");
                            string transcript = Join(app.TranscriptSnapshot());
                            MuxAssert.Contains("Quit", transcript, "help lists quit");
                            MuxAssert.Contains("ctrl+q", transcript, "help lists chord");
                        }
                    })
                });
        }

        #region Helpers

        private static TestCaseDescriptor Case(string id, string name, Func<CancellationToken, Task> body)
        {
            return new TestCaseDescriptor(SuiteId, id, name, body);
        }

        private static JobManager NewManager()
        {
            return new JobManager(EchoRunner, maxConcurrency: 2);
        }

        private static MuxTuiApp NewApp(out HeadlessBackend backend, JobManager manager)
        {
            backend = new HeadlessBackend(100, 24);
            return new MuxTuiApp(backend, manager, "demo", ApprovalPolicyEnum.AutoApprove);
        }

        private static void Feed(HeadlessBackend backend, MuxTuiApp app, string input)
        {
            backend.FeedInput(input);
            app.PumpInputOnce();
        }

        private static void Feed(HeadlessBackend backend, MuxTuiApp app, byte[] input)
        {
            backend.FeedInput(input);
            app.PumpInputOnce();
        }

        private static MenuItem? FindItem(IReadOnlyList<Menu> menus, string label)
        {
            foreach (Menu menu in menus)
            {
                foreach (MenuItem item in menu.Items)
                {
                    if (string.Equals(item.Label, label, StringComparison.Ordinal))
                    {
                        return item;
                    }
                }
            }

            return null;
        }

        private static string Join(IReadOnlyList<string> lines)
        {
            return string.Join("\n", lines);
        }

        private static async IAsyncEnumerable<AgentEvent> EchoRunner(Job job, string prompt, [EnumeratorCancellation] CancellationToken ct)
        {
            await Task.CompletedTask.ConfigureAwait(false);
            yield return new AssistantTextEvent { Text = "Echo: " + prompt };
            yield return new RunCompletedEvent { RunId = Guid.NewGuid().ToString("N"), Status = "completed", IterationsCompleted = 1, DurationMs = 1 };
        }

        #endregion
    }
}
