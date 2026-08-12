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
    /// Touchstone suite for the M10 command surfaces: the slash router, key bindings, and the
    /// catalog-derived menu. Verifies that the surfaces all resolve against the single
    /// <see cref="MuxCommandCatalog"/> and converge on identical command handlers.
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
                "Slash router, key bindings, and menu convergence",
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

                    Case("SlashResolvesEffort", "The slash parser resolves the reasoning-effort command", async (CancellationToken ct) =>
                    {
                        await using (JobManager manager = NewManager())
                        using (MuxTuiApp app = NewApp(out _, manager))
                        {
                            await Task.CompletedTask.ConfigureAwait(false);
                            SlashCommandParser parser = new SlashCommandParser(app.Catalog);
                            MuxAssert.AreEqual("mux.effort", parser.Resolve("/effort")?.Id, "effort alias");
                            MuxAssert.AreEqual("mux.effort", parser.Resolve("/reasoning")?.Id, "reasoning alias");
                        }
                    }),

                    Case("SlashResolvesThinking", "The slash parser resolves the thinking-display command", async (CancellationToken ct) =>
                    {
                        await using (JobManager manager = NewManager())
                        using (MuxTuiApp app = NewApp(out _, manager))
                        {
                            await Task.CompletedTask.ConfigureAwait(false);
                            SlashCommandParser parser = new SlashCommandParser(app.Catalog);
                            MuxAssert.AreEqual("mux.thinking", parser.Resolve("/thinking")?.Id, "thinking alias");
                            MuxAssert.AreEqual("mux.thinking", parser.Resolve("/think")?.Id, "think alias");
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
                    Case("ThreeSurfacesConvergeOnSameCommand", "Slash, keybinding, and menu resolve to one command", async (CancellationToken ct) =>
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

                            MenuItem? menuItem = FindItem(MenuBarBuilder.BuildMenus(app.Catalog), "Clear transcript");

                            MuxAssert.AreEqual("mux.clear", keyCommand?.Id, "keybinding -> clear");
                            MuxAssert.AreEqual("mux.clear", slashCommand?.Id, "slash -> clear");
                            MuxAssert.IsNotNull(menuItem, "menu -> clear item");
                            // The menu item's action is the very same delegate the command carries.
                            MuxAssert.IsTrue(ReferenceEquals(menuItem!.Action, keyCommand!.Handler), "menu shares the command handler");
                        }
                    }),

                    // ---- Help ----
                    Case("HelpOpensModalNotInline", "Help opens a modal listing commands, not inline text", async (CancellationToken ct) =>
                    {
                        await using (JobManager manager = NewManager())
                        using (MuxTuiApp app = NewApp(out HeadlessBackend backend, manager))
                        {
                            await Task.CompletedTask.ConfigureAwait(false);
                            Feed(backend, app, "/help" + "\r");

                            MuxAssert.IsTrue(app.IsModalActive, "help modal open");
                            MuxAssert.IsFalse(Join(app.TranscriptSnapshot()).Contains("Exit", StringComparison.Ordinal), "help not written inline");

                            // The modal renders the command listing through the full pipeline.
                            app.Start();
                            app.RenderOnce();
                            MuxAssert.Contains("Exit", backend.PeekOutput(), "modal lists commands");
                        }
                    }),

                    Case("QuestionMarkOpensNavigableMenuWithSlashCommands", "/? opens the interactive menu (like F1) listing slash commands", async (CancellationToken ct) =>
                    {
                        await using (JobManager manager = NewManager())
                        using (MuxTuiApp app = NewApp(out HeadlessBackend backend, manager))
                        {
                            await Task.CompletedTask.ConfigureAwait(false);
                            Feed(backend, app, "/?" + "\r");

                            MuxAssert.IsTrue(app.IsModalActive, "menu modal open");

                            app.Start();
                            app.RenderOnce();
                            string frame = backend.PeekOutput();
                            // The interactive (navigable) menu — same one F1 opens — advertises Enter-to-run.
                            MuxAssert.Contains("Enter to run", frame, "navigable command menu, not a static box");
                            // The menu now surfaces /slash aliases alongside each command.
                            MuxAssert.Contains("/clear", frame, "slash commands shown in the menu");
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
