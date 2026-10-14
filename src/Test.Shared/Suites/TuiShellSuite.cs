namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Runtime.CompilerServices;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Cli.App;
    using Mux.Core.Agent;
    using Mux.Core.Enums;
    using Mux.Core.Jobs;
    using Mux.Core.Models;
    using Mux.Core.Settings;
    using Mux.Core.Setup;
    using Touchstone.Core;
    using TUIKit.Terminal;

    /// <summary>
    /// Touchstone suite for the TUIKit-hosted interactive shell (<see cref="MuxTuiApp"/>). Every case
    /// drives the shell through an injected <see cref="HeadlessBackend"/> and a fake job runner, so no
    /// terminal, network, or model is required.
    /// </summary>
    public static class TuiShellSuite
    {
        private const string SuiteId = "TuiShell";

        /// <summary>
        /// Builds the shell suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/> for interactive-shell cases.</returns>
        public static TestSuiteDescriptor Create()
        {
            return new TestSuiteDescriptor(
                SuiteId,
                "TUIKit interactive shell boot, input, submit, and event projection",
                new List<TestCaseDescriptor>
                {
                    new TestCaseDescriptor(SuiteId, "BootWritesHeaderAndFooter", "Boot renders the session header and footer hints", async (CancellationToken ct) =>
                    {
                        await using (JobManager manager = NewManager(EchoRunner))
                        using (MuxTuiApp app = NewApp(new HeadlessBackend(80, 24), manager, "demo"))
                        {
                            await Task.CompletedTask.ConfigureAwait(false);

                            string transcript = Join(app.TranscriptSnapshot());
                            string footer = Join(app.FooterSnapshot());

                            // The transcript header is the brand + version; the endpoint/model live in the sidebar.
                            MuxAssert.Contains("mux v" + Mux.Core.Settings.Defaults.ProductVersion, transcript, "header brand + version");
                            // The prompt/newline/quit guidance renders in the footer, not the transcript header.
                            MuxAssert.Contains("Type a prompt", footer, "footer prompt hint");
                            MuxAssert.Contains("CTRL-Q quit", footer, "footer quit hint");
                        }
                    }),

                    new TestCaseDescriptor(SuiteId, "RenderProducesOutput", "Starting and rendering emits output to the backend", async (CancellationToken ct) =>
                    {
                        HeadlessBackend backend = new HeadlessBackend(80, 24);
                        await using (JobManager manager = NewManager(EchoRunner))
                        using (MuxTuiApp app = NewApp(backend, manager, "demo"))
                        {
                            await Task.CompletedTask.ConfigureAwait(false);
                            app.Start();
                            app.RenderOnce();

                            MuxAssert.IsTrue(backend.PeekOutput().Length > 0, "rendered output present");
                        }
                    }),

                    new TestCaseDescriptor(SuiteId, "ToastsUseMuxGreenAndWhite", "Shell info toasts emit mux green backgrounds and white text", async (CancellationToken ct) =>
                    {
                        HeadlessBackend backend = new HeadlessBackend(80, 24);
                        await using (JobManager manager = NewManager(EchoRunner))
                        using (MuxTuiApp app = NewApp(backend, manager, "demo"))
                        {
                            await Task.CompletedTask.ConfigureAwait(false);
                            app.Start();
                            app.ToggleMouseCapture();
                            app.RenderOnce();
                            MuxAssert.Contains("48;2;22;163;74", backend.PeekOutput(), "mux green toast background");
                            MuxAssert.Contains("38;2;255;255;255", backend.PeekOutput(), "white toast text");
                        }
                    }),

                    new TestCaseDescriptor(SuiteId, "HiddenToastCommands", "Hidden slash commands trigger every toast severity without model calls or menu entries", async (CancellationToken ct) =>
                    {
                        string[] levels = { "debug", "info", "success", "warn", "warning", "error" };
                        TUIKit.Modals.NotificationSeverity[] severities =
                        {
                            TUIKit.Modals.NotificationSeverity.Info, TUIKit.Modals.NotificationSeverity.Info,
                            TUIKit.Modals.NotificationSeverity.Success, TUIKit.Modals.NotificationSeverity.Warning,
                            TUIKit.Modals.NotificationSeverity.Warning, TUIKit.Modals.NotificationSeverity.Error
                        };
                        for (int i = 0; i < levels.Length; i++)
                        {
                            HeadlessBackend backend = new HeadlessBackend(80, 24);
                            await using (JobManager manager = NewManager(EchoRunner))
                            using (MuxTuiApp app = NewApp(backend, manager, "demo"))
                            {
                                backend.FeedInput("/toast " + levels[i] + " Custom test message\r");
                                app.PumpInputOnce();
                                var tui = (TUIKit.Hosting.TuiApplication)typeof(MuxTuiApp).GetField("_App",
                                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(app)!;
                                var active = tui.Notifications.Active(tui.NowMilliseconds);
                                MuxAssert.AreEqual(1, active.Count, "one diagnostic toast");
                                MuxAssert.AreEqual(severities[i], active[0].Severity, "severity mapping for " + levels[i]);
                                MuxAssert.AreEqual("Custom test message", active[0].Text, "optional message preserved");
                                MuxAssert.AreEqual(0, manager.Jobs.Count, "diagnostic command makes no model call");
                                foreach (var command in app.Catalog.Commands)
                                    MuxAssert.IsTrue(!command.MatchesSlash("toast"), "hidden command excluded from menu/palette catalog");
                            }
                        }
                    }),

                    new TestCaseDescriptor(SuiteId, "ToastHelpAndValidation", "Toast help lists levels and rejects invalid severities locally", async (CancellationToken ct) =>
                    {
                        HeadlessBackend backend = new HeadlessBackend(80, 24);
                        await using (JobManager manager = NewManager(EchoRunner))
                        using (MuxTuiApp app = NewApp(backend, manager, "demo"))
                        {
                            await Task.CompletedTask.ConfigureAwait(false);
                            backend.FeedInput("/toast ?\r"); app.PumpInputOnce();
                            string help = Join(app.TranscriptSnapshot());
                            foreach (string level in new[] { "debug", "info", "success", "warn/warning", "error" })
                                MuxAssert.Contains(level, help, "available severity " + level);
                            backend.FeedInput("/toast invalid\r"); app.PumpInputOnce();
                            MuxAssert.Contains("Unknown toast severity: invalid", Join(app.TranscriptSnapshot()), "local validation");
                            backend.FeedInput("/toaster\r"); app.PumpInputOnce();
                            MuxAssert.Contains("Unknown command: /toaster", Join(app.TranscriptSnapshot()), "exact command boundary");
                            MuxAssert.AreEqual(0, manager.Jobs.Count, "help/validation are local");
                        }
                    }),

                    new TestCaseDescriptor(SuiteId, "EndpointArgumentCompletionAndNotice", "Endpoint arguments complete and switch inline without opening the editor", async (CancellationToken ct) =>
                    {
                        string directory = Path.Combine(Path.GetTempPath(), "mux-endpoint-completion-" + Guid.NewGuid().ToString("N"));
                        Directory.CreateDirectory(directory);
                        try
                        {
                            using var config = SettingsLoader.PushConfigDirectoryOverride(directory);
                            SettingsLoader.SaveEndpoints(new List<EndpointConfig>
                            {
                                new EndpointConfig { Name = "homedns-gpt", Model = "gpt-oss-20b" },
                                new EndpointConfig { Name = "homedns-llama", Model = "llama" }
                            });
                            HeadlessBackend backend = new HeadlessBackend(100, 24);
                            await using JobManager manager = NewManager(EchoRunner);
                            using MuxTuiApp app = NewApp(backend, manager, "demo");
                            backend.FeedInput("/endpoint homedn"); app.PumpInputOnce();
                            MuxAssert.Contains("/endpoint homedns-gpt", Join(app.FooterSnapshot()), "endpoint name suggestions");
                            backend.FeedInput("\x1b[B\t"); app.PumpInputOnce();
                            MuxAssert.AreEqual("/endpoint homedns-llama ", app.ComposerText, "arrow and Tab select endpoint");
                            backend.FeedInput("\r"); app.PumpInputOnce();
                            MuxAssert.IsFalse(app.IsModalActive, "named endpoint selection does not open editor");
                            MuxAssert.Contains("Switched to endpoint homedns-llama (llama)", Join(app.TranscriptSnapshot()), "inline switch notice");
                            MuxAssert.IsTrue(app.BuildSnapshot().ConversationHistory[0].DisplayOnly, "notice included in persisted history");
                            backend.FeedInput("/endpoint homedns-llama\r"); app.PumpInputOnce();
                            MuxAssert.AreEqual(1, app.BuildSnapshot().ConversationHistory.Count, "same endpoint does not duplicate notice");
                            backend.FeedInput("/endpoint homedns-gpt\r"); app.PumpInputOnce();
                            MuxAssert.AreEqual(2, app.BuildSnapshot().ConversationHistory.Count, "exact endpoint switches directly");
                            MuxAssert.AreEqual(0, manager.Jobs.Count, "commands never become model turns");
                            var snapshot = app.BuildSnapshot();
                            app.RestoreSession(new Mux.Core.Sessions.SessionResumeResult
                            {
                                ConversationHistory = snapshot.ConversationHistory,
                                CompletedJobs = new List<Mux.Core.Sessions.PersistedJobSnapshot>
                                {
                                    new Mux.Core.Sessions.PersistedJobSnapshot { Prompt = "old legacy job" }
                                }
                            });
                            MuxAssert.Contains("Switched to endpoint homedns-gpt (gpt-oss-20b)", Join(app.TranscriptSnapshot()), "notice restored even with persisted jobs");
                            MuxAssert.AreEqual(2, app.BuildSnapshot().ConversationHistory.Count, "resume preserves notices without replay duplication");
                        }
                        finally { Directory.Delete(directory, true); }
                    }),
                    new TestCaseDescriptor(SuiteId, "SlashPrefixCompletion", "Typing a slash prefix shows aliases and Tab completes without executing", async (CancellationToken ct) =>
                    {
                        HeadlessBackend backend = new HeadlessBackend(80, 24);
                        await using (JobManager manager = NewManager(EchoRunner))
                        using (MuxTuiApp app = NewApp(backend, manager, "demo"))
                        {
                            await Task.CompletedTask.ConfigureAwait(false);
                            backend.FeedInput("/end"); app.PumpInputOnce();
                            MuxAssert.Contains("/endpoint", Join(app.FooterSnapshot()), "first match");
                            MuxAssert.Contains("/endpoints", Join(app.FooterSnapshot()), "second match");
                            backend.FeedInput("\x1b[B\t"); app.PumpInputOnce();
                            MuxAssert.AreEqual("/endpoints ", app.ComposerText, "arrow selection and Tab acceptance");
                            MuxAssert.AreEqual(0, manager.Jobs.Count, "completion does not submit");
                            MuxAssert.IsTrue(!app.IsModalActive, "completion does not open endpoints");
                            MuxAssert.IsTrue(!Join(app.FooterSnapshot()).Contains("Tab complete"), "arguments stop suggestions");
                        }
                    }),
                    new TestCaseDescriptor(SuiteId, "SlashCompletionDismissAndArguments", "Escape dismisses completion and edits restore it without touching arguments or hidden commands", async (CancellationToken ct) =>
                    {
                        HeadlessBackend backend = new HeadlessBackend(80, 24);
                        await using (JobManager manager = NewManager(EchoRunner))
                        using (MuxTuiApp app = NewApp(backend, manager, "demo"))
                        {
                            await Task.CompletedTask.ConfigureAwait(false);
                            backend.FeedInput("/end"); app.PumpInputOnce();
                            backend.FeedInput("\x1b"); app.PumpInputOnce(); app.PumpInputOnce();
                            MuxAssert.IsTrue(!Join(app.FooterSnapshot()).Contains("Tab complete"), "Escape dismisses suggestions");
                            MuxAssert.AreEqual("/end", app.ComposerText, "dismissal preserves draft");
                            backend.FeedInput("p"); app.PumpInputOnce();
                            MuxAssert.Contains("/endpoint", Join(app.FooterSnapshot()), "edit restores completion");
                            backend.FeedInput("\targument"); app.PumpInputOnce();
                            MuxAssert.AreEqual("/endpoint argument", app.ComposerText, "completion retains argument entry");
                            MuxAssert.IsTrue(!Join(app.FooterSnapshot()).Contains("Tab complete"), "argument entry is not completed");
                        }
                    }),

                    new TestCaseDescriptor(SuiteId, "SlashEnterCompletesBeforeExecuting", "Enter on a prefix completes it first and Enter again runs the command", async (CancellationToken ct) =>
                    {
                        HeadlessBackend backend = new HeadlessBackend(80, 24);
                        await using (JobManager manager = NewManager(EchoRunner))
                        using (MuxTuiApp app = NewApp(backend, manager, "demo"))
                        {
                            await Task.CompletedTask.ConfigureAwait(false);
                            backend.FeedInput("/end\r"); app.PumpInputOnce();
                            MuxAssert.AreEqual("/endpoint ", app.ComposerText, "Enter completes prefix");
                            MuxAssert.IsTrue(!app.IsModalActive, "not executed on completion");
                            backend.FeedInput("\r"); app.PumpInputOnce();
                            MuxAssert.IsTrue(app.IsModalActive, "completed command executes on second Enter");
                            MuxAssert.AreEqual(0, manager.Jobs.Count, "no model call");
                        }
                    }),

                    new TestCaseDescriptor(SuiteId, "TypingUpdatesComposer", "Typed characters accumulate in the composer", async (CancellationToken ct) =>
                    {
                        HeadlessBackend backend = new HeadlessBackend(80, 24);
                        await using (JobManager manager = NewManager(EchoRunner))
                        using (MuxTuiApp app = NewApp(backend, manager, "demo"))
                        {
                            await Task.CompletedTask.ConfigureAwait(false);
                            backend.FeedInput("hello");
                            app.PumpInputOnce();

                            MuxAssert.AreEqual("hello", app.ComposerText, "composer text");
                            MuxAssert.AreEqual(0, manager.Jobs.Count, "no job before submit");
                        }
                    }),

                    new TestCaseDescriptor(SuiteId, "EnterSubmitsPromptAndClearsComposer", "Enter submits the composer text as a job and clears the composer", async (CancellationToken ct) =>
                    {
                        HeadlessBackend backend = new HeadlessBackend(80, 24);
                        await using (JobManager manager = NewManager(EchoRunner))
                        using (MuxTuiApp app = NewApp(backend, manager, "demo"))
                        {
                            backend.FeedInput("hello\r");
                            app.PumpInputOnce();

                            MuxAssert.AreEqual(1, manager.Jobs.Count, "one job submitted");
                            MuxAssert.AreEqual("hello", manager.Jobs[0].Prompt, "submitted prompt");
                            MuxAssert.AreEqual(string.Empty, app.ComposerText, "composer cleared");

                            await app.DrainProjectorsAsync().ConfigureAwait(false);
                        }
                    }),

                    new TestCaseDescriptor(SuiteId, "VisiblePromptIsNotPinned", "A prompt that fits in the transcript is not duplicated at the top", async (CancellationToken ct) =>
                    {
                        HeadlessBackend backend = new HeadlessBackend(80, 24);
                        await using (JobManager manager = NewManager(EchoRunner))
                        using (MuxTuiApp app = NewApp(backend, manager, "demo"))
                        {
                            backend.FeedInput("hello\r");
                            app.PumpInputOnce();
                            await app.DrainProjectorsAsync().ConfigureAwait(false);
                            app.RenderOnce();
                            MuxAssert.AreEqual(string.Empty, app.RenderRegion("pinnedprompt", 80, 1), "no pinned header while prompt is visible");
                            MuxAssert.Contains("> hello", app.RenderRegion("transcript", 80, 20), "prompt stays in the conversation");
                        }
                    }),

                    new TestCaseDescriptor(SuiteId, "PinnedPromptHasNoBlankRow", "The pinned prompt occupies one row and the transcript starts immediately below it", async (CancellationToken ct) =>
                    {
                        HeadlessBackend backend = new HeadlessBackend(120, 24);
                        await using (JobManager manager = NewManager(LongResponseRunner))
                        using (MuxTuiApp app = NewApp(backend, manager, "demo"))
                        {
                            app.Start();
                            backend.FeedInput("hello\r");
                            app.PumpInputOnce();
                            await app.DrainProjectorsAsync().ConfigureAwait(false);
                            app.RenderOnce();
                            foreach (string field in new[] { "_ExpandedLayout", "_CollapsedLayout" })
                            {
                                var layout = (TUIKit.Layout.Layout)typeof(MuxTuiApp).GetField(field,
                                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(app)!;
                                TUIKit.Rect pinned = default, transcript = default;
                                foreach (var region in layout.Regions)
                                {
                                    if (region.Id == "pinnedprompt") pinned = region.ContentRect(new TUIKit.Size(120, 24));
                                    if (region.Id == "transcript") transcript = region.ContentRect(new TUIKit.Size(120, 24));
                                }
                                MuxAssert.AreEqual(1, pinned.Height, field + " pinned header height");
                                MuxAssert.AreEqual(pinned.Bottom, transcript.Y, field + " transcript follows prompt without a gap");
                            }
                            MuxAssert.AreEqual("> hello", app.RenderRegion("pinnedprompt", 80, 1).Trim(), "short single line prompt has no ellipsis");
                        }
                    }),

                    new TestCaseDescriptor(SuiteId, "PinnedMultilinePromptShowsEllipsis", "Pinned multiline prompts show the first line followed by an ellipsis", async (CancellationToken ct) =>
                    {
                        HeadlessBackend backend = new HeadlessBackend(80, 24);
                        await using (JobManager manager = NewManager(EchoRunner))
                        using (MuxTuiApp app = NewApp(backend, manager, "demo"))
                        {
                            app.Start();
                            // Bracketed paste preserves the prompt's newlines as a single submission.
                            backend.FeedInput("\x1b[200~This is a very long prompt.\r\n\r\n" + new string('x', 3000) + "\x1b[201~\r");
                            app.PumpInputOnce();
                            await app.DrainProjectorsAsync().ConfigureAwait(false);
                            app.RenderOnce();
                            MuxAssert.AreEqual("> This is a very long prompt. ...", app.RenderRegion("pinnedprompt", 80, 1).Trim(), "multiline truncation indicator");
                            MuxAssert.IsTrue(app.RenderRegion("pinnedprompt", 24, 1).TrimEnd().EndsWith(" ...", StringComparison.Ordinal), "ellipsis remains visible on narrow screens");
                        }
                    }),

                    new TestCaseDescriptor(SuiteId, "PromptPinnedAfterOverflow", "The current prompt stays pinned when the response fills the transcript", async (CancellationToken ct) =>
                    {
                        HeadlessBackend backend = new HeadlessBackend(80, 24);
                        await using (JobManager manager = NewManager(EchoRunner))
                        using (MuxTuiApp app = NewApp(backend, manager, "demo"))
                        {
                            app.Start();
                            backend.FeedInput("working on overflow " + new string('x', 3000) + "\r");
                            app.PumpInputOnce();
                            await app.DrainProjectorsAsync().ConfigureAwait(false);
                            app.RenderOnce();
                            string pinned = app.RenderRegion("pinnedprompt", 80, 2);
                            MuxAssert.Contains("working on overflow", pinned, "prompt prefix remains visible");
                            MuxAssert.IsTrue(pinned.Split('\n')[0].TrimEnd().EndsWith(" ...", StringComparison.Ordinal), "wide single line truncation indicator");
                            MuxAssert.Contains("48;2;58;58;58", backend.PeekOutput(), "lighter grey prompt background emitted");
                            MuxAssert.IsTrue(app.RenderRegion("pinnedprompt", 24, 2).Split('\n')[0].TrimEnd().EndsWith(" ...", StringComparison.Ordinal), "narrow viewport keeps truncation indicator");
                            backend.Resize(80, 140);
                            app.ApplyResponsiveLayout();
                            app.RenderOnce();
                            MuxAssert.AreEqual(string.Empty, app.RenderRegion("pinnedprompt", 80, 1), "growing terminal reveals prompt and removes header");
                            backend.Resize(80, 24);
                            app.ApplyResponsiveLayout();
                            app.RenderOnce();
                            MuxAssert.Contains("working on overflow", app.RenderRegion("pinnedprompt", 80, 1), "shrinking terminal pins hidden prompt");
                            var conversation = (TUIKit.Content.Pane)typeof(MuxTuiApp).GetField("_Conversation",
                                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(app)!;
                            conversation.ScrollUp(int.MaxValue);
                            app.RenderOnce();
                            MuxAssert.AreEqual(string.Empty, app.RenderRegion("pinnedprompt", 80, 1), "scrolling back to prompt removes header");
                            conversation.ScrollToBottom();
                            app.RenderOnce();
                            MuxAssert.Contains("working on overflow", app.RenderRegion("pinnedprompt", 80, 1), "scrolling past prompt restores header");
                            backend.FeedInput("replacement prompt\r");
                            app.PumpInputOnce();
                            await app.DrainProjectorsAsync().ConfigureAwait(false);
                            app.RenderOnce();
                            MuxAssert.AreEqual(string.Empty, app.RenderRegion("pinnedprompt", 80, 1), "new visible prompt is not pinned");
                            backend.FeedInput("/clear\r");
                            app.PumpInputOnce();
                            MuxAssert.AreEqual(string.Empty, app.RenderRegion("pinnedprompt", 80, 2).Trim(), "clear removes pinned prompt");
                        }
                    }),

                    new TestCaseDescriptor(SuiteId, "EmptyEnterDoesNotSubmit", "Enter with an empty composer submits nothing", async (CancellationToken ct) =>
                    {
                        HeadlessBackend backend = new HeadlessBackend(80, 24);
                        await using (JobManager manager = NewManager(EchoRunner))
                        using (MuxTuiApp app = NewApp(backend, manager, "demo"))
                        {
                            await Task.CompletedTask.ConfigureAwait(false);
                            backend.FeedInput("\r");
                            app.PumpInputOnce();

                            MuxAssert.AreEqual(0, manager.Jobs.Count, "no job submitted");
                        }
                    }),

                    new TestCaseDescriptor(SuiteId, "WhitespaceEnterDoesNotSubmit", "Enter with only whitespace submits nothing", async (CancellationToken ct) =>
                    {
                        HeadlessBackend backend = new HeadlessBackend(80, 24);
                        await using (JobManager manager = NewManager(EchoRunner))
                        using (MuxTuiApp app = NewApp(backend, manager, "demo"))
                        {
                            await Task.CompletedTask.ConfigureAwait(false);
                            backend.FeedInput("   \r");
                            app.PumpInputOnce();

                            MuxAssert.AreEqual(0, manager.Jobs.Count, "no job submitted");
                        }
                    }),

                    new TestCaseDescriptor(SuiteId, "AssistantTextProjectedToTranscript", "Submitting echoes the prompt and projects assistant text", async (CancellationToken ct) =>
                    {
                        HeadlessBackend backend = new HeadlessBackend(80, 24);
                        await using (JobManager manager = NewManager(EchoRunner))
                        using (MuxTuiApp app = NewApp(backend, manager, "demo"))
                        {
                            backend.FeedInput("hi\r");
                            app.PumpInputOnce();
                            await app.DrainProjectorsAsync().ConfigureAwait(false);

                            string transcript = Join(app.TranscriptSnapshot());
                            MuxAssert.Contains("hi", transcript, "user prompt echoed");
                            MuxAssert.Contains("Echo: hi", transcript, "assistant text projected");
                        }
                    }),

                    new TestCaseDescriptor(SuiteId, "ToolEventsProjectedToTranscript", "Tool proposed and completed events render to the transcript", async (CancellationToken ct) =>
                    {
                        HeadlessBackend backend = new HeadlessBackend(80, 24);
                        await using (JobManager manager = NewManager(ToolRunner))
                        using (MuxTuiApp app = NewApp(backend, manager, "demo"))
                        {
                            backend.FeedInput("do it\r");
                            app.PumpInputOnce();
                            await app.DrainProjectorsAsync().ConfigureAwait(false);

                            string transcript = Join(app.TranscriptSnapshot());
                            MuxAssert.Contains("read_file", transcript, "tool name projected");
                            MuxAssert.Contains("✓", transcript, "tool success mark projected");
                        }
                    }),

                    new TestCaseDescriptor(SuiteId, "ErrorEventProjectedToTranscript", "Error events render to the transcript", async (CancellationToken ct) =>
                    {
                        HeadlessBackend backend = new HeadlessBackend(80, 24);
                        await using (JobManager manager = NewManager(ErrorRunner))
                        using (MuxTuiApp app = NewApp(backend, manager, "demo"))
                        {
                            backend.FeedInput("boom\r");
                            app.PumpInputOnce();
                            await app.DrainProjectorsAsync().ConfigureAwait(false);

                            string transcript = Join(app.TranscriptSnapshot());
                            MuxAssert.Contains("Error [llm_error]", transcript, "error code projected");
                            MuxAssert.Contains("kaboom", transcript, "error message projected");
                        }
                    }),

                    new TestCaseDescriptor(SuiteId, "CtrlLClearsTranscript", "Ctrl+L clears the transcript", async (CancellationToken ct) =>
                    {
                        HeadlessBackend backend = new HeadlessBackend(80, 24);
                        await using (JobManager manager = NewManager(EchoRunner))
                        using (MuxTuiApp app = NewApp(backend, manager, "demo"))
                        {
                            await Task.CompletedTask.ConfigureAwait(false);
                            MuxAssert.IsTrue(app.TranscriptSnapshot().Count > 0, "transcript seeded");

                            backend.FeedInput("\f"); // Ctrl+L
                            app.PumpInputOnce();

                            MuxAssert.AreEqual(0, app.TranscriptSnapshot().Count, "transcript cleared");
                        }
                    }),

                    new TestCaseDescriptor(SuiteId, "EscapeCancelsFocusedJob", "Escape cancels the focused running job", async (CancellationToken ct) =>
                    {
                        HeadlessBackend backend = new HeadlessBackend(80, 24);
                        await using (JobManager manager = NewManager(BlockingRunner))
                        using (MuxTuiApp app = NewApp(backend, manager, "demo"))
                        {
                            backend.FeedInput("run\r");
                            app.PumpInputOnce();

                            MuxAssert.AreEqual(1, manager.Jobs.Count, "job submitted");
                            Job job = manager.Jobs[0];
                            await WaitForStateAsync(job, JobState.Running, ct).ConfigureAwait(false);

                            backend.FeedInput(new byte[] { 0x1b }); // Escape byte, buffered until flush
                            app.PumpInputOnce(); // feeds ESC into the parser
                            app.PumpInputOnce(); // idle pump flushes the lone ESC to an Escape key

                            await WaitForStateAsync(job, JobState.Cancelled, ct).ConfigureAwait(false);
                            MuxAssert.AreEqual(JobState.Cancelled, job.State, "job cancelled");
                        }
                    }),

                    new TestCaseDescriptor(SuiteId, "CtrlQStopsRunLoop", "Ctrl+Q exits the run loop", async (CancellationToken ct) =>
                    {
                        HeadlessBackend backend = new HeadlessBackend(80, 24);
                        await using (JobManager manager = NewManager(EchoRunner))
                        using (MuxTuiApp app = NewApp(backend, manager, "demo"))
                        {
                            Task run = app.RunAsync(ct);
                            backend.FeedInput(new byte[] { 0x11 }); // Ctrl+Q -> quit-confirmation modal
                            await WaitUntilAsync(() => app.IsModalActive, ct).ConfigureAwait(false);
                            backend.FeedInput("\r"); // confirm "Exit" (default button)

                            await run.WaitAsync(TimeSpan.FromSeconds(15), ct).ConfigureAwait(false);
                            MuxAssert.IsTrue(run.IsCompletedSuccessfully, "run loop exited");
                        }
                    }),

                    new TestCaseDescriptor(SuiteId, "FirstRunShowsSetupWizard", "On first run (only the seed endpoint, setup not completed) the wizard modal appears", async (CancellationToken ct) =>
                    {
                        string? originalEnv = Environment.GetEnvironmentVariable("MUX_CONFIG_DIR");
                        string dir = Path.Combine(Path.GetTempPath(), "mux-firstrun-" + Guid.NewGuid().ToString("N"));
                        Directory.CreateDirectory(dir);
                        Environment.SetEnvironmentVariable("MUX_CONFIG_DIR", dir);
                        try
                        {
                            // Seed a fresh config: this writes the ollama-local / qwen2.5-coder:7b seed endpoint
                            // and settings with setupCompleted=false — exactly the first-run state.
                            SettingsLoader.EnsureConfigDirectory();
                            MuxAssert.IsTrue(
                                SetupState.NeedsSetup(SettingsLoader.LoadEndpoints(), SettingsLoader.LoadSettings().SetupCompleted),
                                "precondition: a freshly seeded config needs setup");

                            HeadlessBackend backend = new HeadlessBackend(80, 24);
                            await using (JobManager manager = NewManager(EchoRunner))
                            using (MuxTuiApp app = new MuxTuiApp(backend, manager, "demo", ApprovalPolicyEnum.AutoApprove, showSplash: true, enableFirstRunWizard: true, splashDuration: TimeSpan.FromMinutes(5)))
                            using (CancellationTokenSource runCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
                            {
                                Task run = app.RunAsync(runCts.Token);

                                // The real launch shows the startup splash (no --prompt). The wizard must wait
                                // for the splash to be dismissed and render in its place — never stacked on top.
                                await WaitUntilAsync(() => app.ModalCount >= 1, ct).ConfigureAwait(false);
                                MuxAssert.AreEqual(1, app.ModalCount, "only the splash is shown; the wizard is deferred, not stacked");
                                MuxAssert.DoesNotContain("Set up now", backend.PeekOutput(), "the wizard is not rendered over the splash");

                                backend.FeedInput(" "); // any key dismisses the splash
                                await WaitUntilAsync(() => backend.PeekOutput().Contains("Set up now", StringComparison.Ordinal), ct).ConfigureAwait(false);
                                MuxAssert.Contains("Set up now", backend.PeekOutput(), "the wizard appears after the splash is dismissed");

                                runCts.Cancel();
                                try { await run.ConfigureAwait(false); } catch (OperationCanceledException) { }
                            }
                        }
                        finally
                        {
                            Environment.SetEnvironmentVariable("MUX_CONFIG_DIR", originalEnv);
                            try { Directory.Delete(dir, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                        }
                    }),

                    new TestCaseDescriptor(SuiteId, "SplashClosesByItself", "The startup splash closes by itself after its duration with no key pressed", async (CancellationToken ct) =>
                    {
                        HeadlessBackend backend = new HeadlessBackend(80, 24);
                        await using (JobManager manager = NewManager(EchoRunner))
                        using (MuxTuiApp app = new MuxTuiApp(backend, manager, "demo", ApprovalPolicyEnum.AutoApprove, showSplash: true, splashDuration: TimeSpan.FromMilliseconds(300)))
                        using (CancellationTokenSource runCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
                        {
                            MuxAssert.AreEqual(1, app.ModalCount, "the splash is up at startup");
                            System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
                            Task run = app.RunAsync(runCts.Token);
                            await WaitUntilAsync(() => app.ModalCount == 0, ct).ConfigureAwait(false);
                            MuxAssert.AreEqual(0, app.ModalCount, "the splash closed with no input");
                            MuxAssert.IsTrue(watch.ElapsedMilliseconds >= 250, "it stayed up for its duration first (" + watch.ElapsedMilliseconds + " ms)");
                            runCts.Cancel();
                            try { await run.ConfigureAwait(false); } catch (OperationCanceledException) { }
                        }
                    }),

                    new TestCaseDescriptor(SuiteId, "SplashTimeoutLeadsToWizard", "On first run the splash closes by itself and the setup wizard renders in its place with no key pressed", async (CancellationToken ct) =>
                    {
                        string? originalEnv = Environment.GetEnvironmentVariable("MUX_CONFIG_DIR");
                        string dir = Path.Combine(Path.GetTempPath(), "mux-splashwiz-" + Guid.NewGuid().ToString("N"));
                        Directory.CreateDirectory(dir);
                        Environment.SetEnvironmentVariable("MUX_CONFIG_DIR", dir);
                        try
                        {
                            SettingsLoader.EnsureConfigDirectory();
                            HeadlessBackend backend = new HeadlessBackend(80, 24);
                            await using (JobManager manager = NewManager(EchoRunner))
                            using (MuxTuiApp app = new MuxTuiApp(backend, manager, "demo", ApprovalPolicyEnum.AutoApprove, showSplash: true, enableFirstRunWizard: true, splashDuration: TimeSpan.FromMilliseconds(300)))
                            using (CancellationTokenSource runCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
                            {
                                Task run = app.RunAsync(runCts.Token);
                                await WaitUntilAsync(() => backend.PeekOutput().Contains("Set up now", StringComparison.Ordinal), ct).ConfigureAwait(false);
                                MuxAssert.AreEqual(1, app.ModalCount, "the wizard replaced the splash rather than stacking on it");
                                runCts.Cancel();
                                try { await run.ConfigureAwait(false); } catch (OperationCanceledException) { }
                            }
                        }
                        finally
                        {
                            Environment.SetEnvironmentVariable("MUX_CONFIG_DIR", originalEnv);
                            try { Directory.Delete(dir, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                        }
                    }),

                    new TestCaseDescriptor(SuiteId, "SplashKeyStillClosesEarly", "A key closes the splash before its timer, and the timer firing later does nothing", async (CancellationToken ct) =>
                    {
                        HeadlessBackend backend = new HeadlessBackend(80, 24);
                        await using (JobManager manager = NewManager(EchoRunner))
                        using (MuxTuiApp app = new MuxTuiApp(backend, manager, "demo", ApprovalPolicyEnum.AutoApprove, showSplash: true, splashDuration: TimeSpan.FromMilliseconds(400)))
                        using (CancellationTokenSource runCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
                        {
                            Task run = app.RunAsync(runCts.Token);
                            backend.FeedInput(" ");
                            await WaitUntilAsync(() => app.ModalCount == 0, ct).ConfigureAwait(false);
                            backend.FeedInput("/mcp\r");
                            await WaitUntilAsync(() => app.ModalCount == 1, ct).ConfigureAwait(false);
                            await Task.Delay(700, ct).ConfigureAwait(false);
                            MuxAssert.AreEqual(1, app.ModalCount, "the expired splash timer did not close the next modal");
                            runCts.Cancel();
                            try { await run.ConfigureAwait(false); } catch (OperationCanceledException) { }
                        }
                    }),

                    new TestCaseDescriptor(SuiteId, "ComposerGrowsWhenLineWraps", "Typing a line longer than the composer width grows the composer to its wrapped rows", async (CancellationToken ct) =>
                    {
                        HeadlessBackend backend = new HeadlessBackend(30, 12);
                        await using (JobManager manager = NewManager(EchoRunner))
                        using (MuxTuiApp app = NewApp(backend, manager, "demo"))
                        {
                            MuxAssert.AreEqual(1, app.ComposerRowCount, "composer starts at one row");
                            backend.FeedInput("the quick brown fox jumps over the lazy dog again and again");
                            for (int i = 0; i < 80; i++)
                            {
                                app.PumpInputOnce();
                            }

                            MuxAssert.IsTrue(app.ComposerRowCount > 1, "composer grew to fit the wrapped line (rows=" + app.ComposerRowCount + ")");
                        }
                    }),

                    new TestCaseDescriptor(SuiteId, "HomePaneShownBeforeAnyJob", "Before any job the home pane is focused", async (CancellationToken ct) =>
                    {
                        HeadlessBackend backend = new HeadlessBackend(80, 24);
                        await using (JobManager manager = NewManager(EchoRunner))
                        using (MuxTuiApp app = NewApp(backend, manager, "demo"))
                        {
                            await Task.CompletedTask.ConfigureAwait(false);
                            MuxAssert.IsTrue(app.FocusedJobId == null, "no focused job");
                            MuxAssert.AreEqual(0, app.JobIds.Count, "no jobs");
                            MuxAssert.Contains("mux", Join(app.TranscriptSnapshot()), "home header");
                        }
                    }),

                    new TestCaseDescriptor(SuiteId, "SubmitStartsATurnInTheConversation", "Submitting starts a turn and echoes into the single conversation", async (CancellationToken ct) =>
                    {
                        HeadlessBackend backend = new HeadlessBackend(80, 24);
                        await using (JobManager manager = NewManager(EchoRunner))
                        using (MuxTuiApp app = NewApp(backend, manager, "demo"))
                        {
                            backend.FeedInput("hi\r");
                            app.PumpInputOnce();

                            MuxAssert.AreEqual(1, app.JobIds.Count, "one turn started");
                            string jobId = app.JobIds[0];
                            MuxAssert.AreEqual(jobId, app.FocusedJobId, "turn is current");

                            string transcript = Join(app.TranscriptSnapshot());
                            MuxAssert.Contains("> hi", transcript, "prompt echoed with > prefix");

                            await app.DrainProjectorsAsync().ConfigureAwait(false);
                        }
                    }),

                    new TestCaseDescriptor(SuiteId, "SequentialTurnsShareOneTranscript", "Both turns accumulate into the single conversation transcript", async (CancellationToken ct) =>
                    {
                        HeadlessBackend backend = new HeadlessBackend(80, 24);
                        await using (JobManager manager = new JobManager(EchoRunner, maxConcurrency: 2))
                        using (MuxTuiApp app = NewApp(backend, manager, "demo"))
                        {
                            backend.FeedInput("one\r");
                            app.PumpInputOnce();
                            backend.FeedInput("two\r");
                            app.PumpInputOnce();
                            await app.DrainProjectorsAsync().ConfigureAwait(false);

                            MuxAssert.AreEqual(2, app.JobIds.Count, "two turns ran");
                            string transcript = Join(app.TranscriptSnapshot());
                            MuxAssert.Contains("Echo: one", transcript, "first turn output present");
                            MuxAssert.Contains("Echo: two", transcript, "second turn output present");
                            MuxAssert.Contains("one", transcript, "first prompt echoed");
                            MuxAssert.Contains("two", transcript, "second prompt echoed");
                        }
                    })
                });
        }

        #region Fake-Runners

        private static async IAsyncEnumerable<AgentEvent> LongResponseRunner(
            Job job, string prompt, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask.ConfigureAwait(false);
            // Blank-line separated paragraphs: the reply is rendered as Markdown, where single newlines are soft
            // breaks that would join into one short wrapped paragraph.
            for (int i = 0; i < 50; i++) yield return new AssistantTextEvent { Text = "Life continues.\n\n" };
            yield return CompletedEvent();
        }

        private static async IAsyncEnumerable<AgentEvent> EchoRunner(
            Job job,
            string prompt,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask.ConfigureAwait(false);
            yield return new AssistantTextEvent { Text = "Echo: " + prompt };
            yield return CompletedEvent();
        }

        private static async IAsyncEnumerable<AgentEvent> ToolRunner(
            Job job,
            string prompt,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask.ConfigureAwait(false);
            yield return new ToolCallProposedEvent
            {
                ToolCall = new ToolCall { Id = "t1", Name = "read_file", Arguments = "{}" }
            };
            yield return new ToolCallCompletedEvent
            {
                ToolCallId = "t1",
                ToolName = "read_file",
                Result = new ToolResult { Success = true, Content = "ok" },
                ElapsedMs = 5
            };
            yield return CompletedEvent();
        }

        private static async IAsyncEnumerable<AgentEvent> ErrorRunner(
            Job job,
            string prompt,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask.ConfigureAwait(false);
            yield return new ErrorEvent { Code = "llm_error", Message = "kaboom" };
            yield return CompletedEvent();
        }

        private static async IAsyncEnumerable<AgentEvent> BlockingRunner(
            Job job,
            string prompt,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Delay(TimeSpan.FromMinutes(5), cancellationToken).ConfigureAwait(false);
            yield return CompletedEvent();
        }

        #endregion

        #region Helpers

        private static JobManager NewManager(Func<Job, string, CancellationToken, IAsyncEnumerable<AgentEvent>> runner)
        {
            return new JobManager(runner, maxConcurrency: 1);
        }

        private static MuxTuiApp NewApp(HeadlessBackend backend, JobManager manager, string title)
        {
            MuxTuiApp app = new MuxTuiApp(backend, manager, title, ApprovalPolicyEnum.AutoApprove);
            // These cases exercise panes/focus, not the submit chooser; make submit deterministic.
            return app;
        }

        private static RunCompletedEvent CompletedEvent()
        {
            return new RunCompletedEvent
            {
                RunId = Guid.NewGuid().ToString("N"),
                Status = "completed",
                IterationsCompleted = 1,
                DurationMs = 1
            };
        }

        private static string Join(IReadOnlyList<string> lines)
        {
            return string.Join("\n", lines);
        }

        private static async Task WaitUntilAsync(Func<bool> condition, CancellationToken cancellationToken)
        {
            using (CancellationTokenSource timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30)))
            using (CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token))
            {
                while (!condition())
                {
                    await Task.Delay(10, linked.Token).ConfigureAwait(false);
                }
            }
        }

        private static async Task WaitForStateAsync(Job job, JobState expectedState, CancellationToken cancellationToken)
        {
            using (CancellationTokenSource timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30)))
            using (CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token))
            {
                while (!linked.Token.IsCancellationRequested)
                {
                    if (job.State == expectedState)
                    {
                        return;
                    }

                    await Task.Delay(10, linked.Token).ConfigureAwait(false);
                }

                linked.Token.ThrowIfCancellationRequested();
            }
        }

        #endregion
    }
}
