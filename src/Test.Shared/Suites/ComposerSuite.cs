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
    using TUIKit.Input;
    using TUIKit.Terminal;

    /// <summary>
    /// Touchstone suite for the composer, prompt history, and the serial prompt queue with its editor.
    /// Covers <see cref="PromptHistory"/> in isolation plus the shell's newline / submit / queue / editor /
    /// history / slash-routing gestures driven through the real input path.
    /// </summary>
    public static class ComposerSuite
    {
        private const string SuiteId = "Composer";
        private const char Esc = (char)27;

        /// <summary>
        /// Builds the composer suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/> for composer cases.</returns>
        public static TestSuiteDescriptor Create()
        {
            return new TestSuiteDescriptor(
                SuiteId,
                "Composer, prompt history, and submit chooser",
                new List<TestCaseDescriptor>
                {
                    // ---- PromptHistory unit cases ----
                    Case("HistoryRecallsInReverseOrder", "Previous walks from newest to oldest", (CancellationToken ct) =>
                    {
                        PromptHistory history = new PromptHistory();
                        history.Add("a");
                        history.Add("b");
                        history.Add("c");

                        MuxAssert.IsTrue(history.TryPrevious(out string p1), "prev 1"); MuxAssert.AreEqual("c", p1, "newest");
                        MuxAssert.IsTrue(history.TryPrevious(out string p2), "prev 2"); MuxAssert.AreEqual("b", p2, "middle");
                        MuxAssert.IsTrue(history.TryPrevious(out string p3), "prev 3"); MuxAssert.AreEqual("a", p3, "oldest");
                        MuxAssert.IsTrue(history.TryPrevious(out string p4), "prev 4"); MuxAssert.AreEqual("a", p4, "clamped at oldest");
                        return Task.CompletedTask;
                    }),

                    Case("HistoryNextReturnsToFreshDraft", "Next walks back to an empty fresh draft", (CancellationToken ct) =>
                    {
                        PromptHistory history = new PromptHistory();
                        history.Add("a");
                        history.Add("b");
                        history.TryPrevious(out _); // b
                        history.TryPrevious(out _); // a

                        MuxAssert.IsTrue(history.TryNext(out string n1), "next 1"); MuxAssert.AreEqual("b", n1, "back to b");
                        MuxAssert.IsTrue(history.TryNext(out string n2), "next 2"); MuxAssert.AreEqual(string.Empty, n2, "fresh draft");
                        MuxAssert.IsFalse(history.TryNext(out _), "no move past fresh");
                        return Task.CompletedTask;
                    }),

                    Case("HistoryIgnoresBlankAndConsecutiveDuplicates", "Blank and repeated entries are not stored", (CancellationToken ct) =>
                    {
                        PromptHistory history = new PromptHistory();
                        history.Add("a");
                        history.Add("a");
                        history.Add("   ");
                        history.Add("b");
                        MuxAssert.AreEqual(2, history.Count, "deduped count");
                        return Task.CompletedTask;
                    }),

                    Case("HistoryEmptyPreviousReturnsFalse", "Previous on empty history returns false", (CancellationToken ct) =>
                    {
                        PromptHistory history = new PromptHistory();
                        MuxAssert.IsFalse(history.TryPrevious(out _), "no history");
                        return Task.CompletedTask;
                    }),

                    // ---- Composer newline gestures ----
                    Case("AltEnterInsertsNewlineNotSubmit", "Alt+Enter inserts a newline instead of submitting", async (CancellationToken ct) =>
                    {
                        HeadlessBackend backend = new HeadlessBackend(100, 24);
                        await using (JobManager manager = NewManager(EchoRunner))
                        using (MuxTuiApp app = NewApp(backend, manager))
                        {
                            Feed(backend, app, "line1" + Esc + "\r" + "line2");
                            MuxAssert.AreEqual("line1\nline2", app.ComposerText, "two-line composer");
                            MuxAssert.AreEqual(0, manager.Jobs.Count, "not submitted");
                        }
                    }),

                    Case("ShiftEnterInsertsNewline", "Shift+Enter (CSI-u) inserts a newline", async (CancellationToken ct) =>
                    {
                        HeadlessBackend backend = new HeadlessBackend(100, 24);
                        await using (JobManager manager = NewManager(EchoRunner))
                        using (MuxTuiApp app = NewApp(backend, manager))
                        {
                            Feed(backend, app, "a" + Esc + "[13;2u" + "b");
                            MuxAssert.AreEqual("a\nb", app.ComposerText, "two-line composer");
                        }
                    }),

                    Case("ComposerGrowsWithNewlines", "The composer region grows a row per newline and resets on submit", async (CancellationToken ct) =>
                    {
                        HeadlessBackend backend = new HeadlessBackend(100, 24);
                        await using (JobManager manager = NewManager(EchoRunner))
                        using (MuxTuiApp app = NewApp(backend, manager))
                        {
                            MuxAssert.AreEqual(1, app.ComposerRowCount, "one row at rest");

                            Feed(backend, app, "a" + Esc + "[106;5u" + "b" + Esc + "[106;5u" + "c");
                            MuxAssert.AreEqual(3, app.ComposerRowCount, "three rows for three lines");

                            Feed(backend, app, "\r"); // submit
                            MuxAssert.AreEqual(1, app.ComposerRowCount, "resets to one row after submit");
                            await app.DrainProjectorsAsync().ConfigureAwait(false);
                        }
                    }),

                    Case("MultiLinePromptEchoesAcrossLines", "A multi-line prompt echoes with each line preserved and indented", async (CancellationToken ct) =>
                    {
                        HeadlessBackend backend = new HeadlessBackend(100, 24);
                        await using (JobManager manager = NewManager(EchoRunner))
                        using (MuxTuiApp app = NewApp(backend, manager))
                        {
                            Feed(backend, app, "this" + Esc + "[106;5u" + "is" + Esc + "[106;5u" + "a" + Esc + "[106;5u" + "test" + "\r");
                            MuxAssert.AreEqual("this\nis\na\ntest", manager.Jobs[0].Prompt, "prompt keeps newlines");

                            string transcript = Join(app.TranscriptSnapshot());
                            MuxAssert.Contains("mux> this", transcript, "first line after the marker");
                            MuxAssert.Contains("     is", transcript, "second line indented under the marker");
                            MuxAssert.Contains("     test", transcript, "last line indented");
                            MuxAssert.IsFalse(transcript.Contains("thisisatest"), "lines are not flattened");
                            await app.DrainProjectorsAsync().ConfigureAwait(false);
                        }
                    }),

                    Case("EnterSubmitsMultiLinePrompt", "Enter submits a multi-line prompt as one job", async (CancellationToken ct) =>
                    {
                        HeadlessBackend backend = new HeadlessBackend(100, 24);
                        await using (JobManager manager = NewManager(EchoRunner))
                        using (MuxTuiApp app = NewApp(backend, manager))
                        {
                            Feed(backend, app, "line1" + Esc + "\r" + "line2" + "\r");
                            MuxAssert.AreEqual(1, manager.Jobs.Count, "one job");
                            MuxAssert.AreEqual("line1\nline2", manager.Jobs[0].Prompt, "multi-line prompt");
                            await app.DrainProjectorsAsync().ConfigureAwait(false);
                        }
                    }),

                    Case("CtrlJInsertsNewlineNotSubmit", "Ctrl+J inserts a newline instead of submitting", async (CancellationToken ct) =>
                    {
                        HeadlessBackend backend = new HeadlessBackend(100, 24);
                        await using (JobManager manager = NewManager(EchoRunner))
                        using (MuxTuiApp app = NewApp(backend, manager))
                        {
                            // Ctrl+J via the enhanced-keyboard CSI-u encoding (codepoint 106='j', modifier 5=Ctrl).
                            // On terminals without the enhanced protocol it arrives as a raw line feed (0x0A);
                            // both decode to Char('j', Ctrl), so this exercises the same mux binding.
                            Feed(backend, app, "a" + Esc + "[106;5u" + "b");
                            MuxAssert.AreEqual("a\nb", app.ComposerText, "two-line composer");
                            MuxAssert.AreEqual(0, manager.Jobs.Count, "not submitted");
                        }
                    }),

                    Case("RawLineFeedInsertsNewlineNotSubmit", "A raw line feed (Ctrl+J on terminals without the enhanced protocol) inserts a newline", async (CancellationToken ct) =>
                    {
                        HeadlessBackend backend = new HeadlessBackend(100, 24);
                        await using (JobManager manager = NewManager(EchoRunner))
                        using (MuxTuiApp app = NewApp(backend, manager))
                        {
                            // The bare 0x0A byte is exactly what Windows Terminal / macOS Terminal.app send
                            // for Ctrl+J. Enter is carriage return (0x0D) and still submits.
                            Feed(backend, app, "a" + "\n" + "b");
                            MuxAssert.AreEqual("a\nb", app.ComposerText, "raw LF inserts a newline");
                            MuxAssert.AreEqual(0, manager.Jobs.Count, "raw LF does not submit");
                        }
                    }),

                    // ---- Multi-line paste ----
                    Case("PastedMultiLineStaysOneUnitAndSubmitsAsOnePrompt", "A bracketed paste keeps its newlines and submits as a single prompt", async (CancellationToken ct) =>
                    {
                        HeadlessBackend backend = new HeadlessBackend(100, 24);
                        await using (JobManager manager = NewManager(EchoRunner))
                        using (MuxTuiApp app = NewApp(backend, manager))
                        {
                            string pasted = "first paragraph\nsecond paragraph\nthird paragraph";

                            // Bracketed paste: the terminal wraps pasted content in CSI 200~ ... CSI 201~, so
                            // the embedded newlines never trigger a submit and the block lands intact.
                            Feed(backend, app, Esc + "[200~" + pasted + Esc + "[201~");
                            MuxAssert.AreEqual(pasted, app.ComposerText, "paste preserved as one multi-line unit");
                            MuxAssert.AreEqual(0, manager.Jobs.Count, "paste alone does not submit");

                            // A single Enter then sends the whole pasted block as one prompt.
                            Feed(backend, app, "\r");
                            MuxAssert.AreEqual(1, manager.Jobs.Count, "one job for the whole paste");
                            MuxAssert.AreEqual(pasted, manager.Jobs[0].Prompt, "prompt is the full pasted block");
                            await app.DrainProjectorsAsync().ConfigureAwait(false);
                        }
                    }),

                    // ---- Serial queue ----
                    Case("SecondPromptQueuesToStripNotTranscript", "A prompt entered while a turn runs queues to the strip, not the transcript", async (CancellationToken ct) =>
                    {
                        TaskCompletionSource<bool> release = NewSignal();
                        await using (JobManager manager = new JobManager(Gated(release), maxConcurrency: 2))
                        using (MuxTuiApp app = NewApp(backend: out HeadlessBackend backend, manager: manager))
                        {
                            Feed(backend, app, "first" + "\r");
                            await WaitForActiveAsync(manager, ct).ConfigureAwait(false);
                            MuxAssert.AreEqual(1, app.JobIds.Count, "first turn running");
                            MuxAssert.IsTrue(app.IsBusy, "busy");

                            Feed(backend, app, "second" + "\r");
                            MuxAssert.AreEqual(1, app.JobIds.Count, "second is queued, no new turn yet");
                            MuxAssert.AreEqual(1, app.QueuedCount, "one queued");

                            // The active prompt is echoed to the transcript; the queued one shows in the
                            // strip above the composer and is NOT echoed to the transcript until it starts.
                            MuxAssert.Contains("first", Join(app.TranscriptSnapshot()), "first echoed to transcript");
                            MuxAssert.IsFalse(Join(app.TranscriptSnapshot()).Contains("second"), "second not yet in transcript");
                            IReadOnlyList<string> strip = app.QueueStripSnapshot();
                            MuxAssert.Contains("second", Join(strip), "second shown in the queue strip");
                            MuxAssert.Contains("QUEUED", Join(strip), "queue strip labeled");
                            // A blank spacer row sits above the QUEUED header.
                            MuxAssert.AreEqual(string.Empty, strip[0].Trim(), "blank spacer above the header");
                            MuxAssert.Contains("QUEUED", strip[1], "header on the second row");

                            release.TrySetResult(true);
                            await app.DrainProjectorsAsync().ConfigureAwait(false);
                        }
                    }),

                    Case("QueuedPromptsRunInOrderAsTurnsComplete", "Queued prompts run one at a time, in order, as each turn finishes", async (CancellationToken ct) =>
                    {
                        TaskCompletionSource<bool> release = NewSignal();
                        await using (JobManager manager = new JobManager(Gated(release), maxConcurrency: 2))
                        using (MuxTuiApp app = NewApp(backend: out HeadlessBackend backend, manager: manager))
                        {
                            Feed(backend, app, "first" + "\r");
                            await WaitForActiveAsync(manager, ct).ConfigureAwait(false);
                            Feed(backend, app, "second" + "\r");
                            Feed(backend, app, "third" + "\r");

                            // Serial: only one runs; the other two wait in the queue.
                            MuxAssert.AreEqual(1, app.JobIds.Count, "one running");
                            MuxAssert.AreEqual(2, app.QueuedCount, "two queued");

                            release.TrySetResult(true);
                            await app.DrainProjectorsAsync().ConfigureAwait(false);

                            MuxAssert.AreEqual(3, app.JobIds.Count, "all three ran in turn");
                            MuxAssert.AreEqual(0, app.QueuedCount, "queue drained");
                            MuxAssert.IsFalse(app.IsBusy, "idle after all turns");
                            // The queue strip collapses once empty.
                            MuxAssert.AreEqual(string.Empty, Join(app.QueueStripSnapshot()).Trim(), "strip cleared");
                        }
                    }),

                    Case("QueueEditorPausesProcessingUntilClosed", "Opening the queue editor pauses processing; closing it resumes", async (CancellationToken ct) =>
                    {
                        TaskCompletionSource<bool> release = NewSignal();
                        await using (JobManager manager = new JobManager(Gated(release), maxConcurrency: 2))
                        using (MuxTuiApp app = NewApp(backend: out HeadlessBackend backend, manager: manager))
                        {
                            Feed(backend, app, "first" + "\r");
                            await WaitForActiveAsync(manager, ct).ConfigureAwait(false);
                            Feed(backend, app, "second" + "\r");

                            // Ctrl+G (0x07) opens the queue editor and pauses processing.
                            Feed(backend, app, "");
                            MuxAssert.IsTrue(app.IsQueuePaused, "paused while editor open");

                            // The active turn finishes while paused — the queued prompt must NOT start.
                            release.TrySetResult(true);
                            await app.DrainProjectorsAsync().ConfigureAwait(false);
                            MuxAssert.AreEqual(1, app.JobIds.Count, "queued prompt held while paused");
                            MuxAssert.AreEqual(1, app.QueuedCount, "still queued");
                            MuxAssert.IsFalse(app.IsBusy, "idle while paused");

                            // Esc closes the editor, which resumes and starts the queued prompt (the close
                            // handler runs asynchronously, so wait for the resume before draining). A lone
                            // Escape byte is held pending until a no-input pump flushes it, so pump twice.
                            Feed(backend, app, Esc.ToString());
                            app.PumpInputOnce();
                            await WaitUntilAsync(() => app.JobIds.Count == 2, ct).ConfigureAwait(false);
                            await app.DrainProjectorsAsync().ConfigureAwait(false);
                            MuxAssert.IsFalse(app.IsQueuePaused, "resumed after close");
                            MuxAssert.AreEqual(2, app.JobIds.Count, "queued prompt ran after resume");
                            MuxAssert.AreEqual(0, app.QueuedCount, "queue drained");
                        }
                    }),

                    // ---- Queue editor modal ----
                    Case("QueueEditorReordersEntries", "The editor reorders queued prompts", async (CancellationToken ct) =>
                    {
                        QueueEditorModal modal = new QueueEditorModal(new List<string> { "a", "b", "c" });
                        modal.HandleKey(KeyEvent.Special(KeyCode.Down));   // select "b"
                        modal.HandleKey(KeyEvent.Char((int)']'));          // move "b" down → a, c, b
                        modal.HandleKey(KeyEvent.Special(KeyCode.Escape)); // close

                        List<string> result = (List<string>)(await modal.Completion.ConfigureAwait(false))!;
                        MuxAssert.AreEqual("a,c,b", string.Join(",", result), "reordered");
                    }),

                    Case("QueueEditorRemovesEntries", "The editor removes a queued prompt", async (CancellationToken ct) =>
                    {
                        QueueEditorModal modal = new QueueEditorModal(new List<string> { "a", "b", "c" });
                        modal.HandleKey(KeyEvent.Special(KeyCode.Down));   // select "b"
                        modal.HandleKey(KeyEvent.Char((int)'d'));          // delete "b" → a, c
                        modal.HandleKey(KeyEvent.Special(KeyCode.Escape));

                        List<string> result = (List<string>)(await modal.Completion.ConfigureAwait(false))!;
                        MuxAssert.AreEqual("a,c", string.Join(",", result), "removed");
                    }),

                    Case("QueueEditorEditsText", "The editor edits a queued prompt's text", async (CancellationToken ct) =>
                    {
                        QueueEditorModal modal = new QueueEditorModal(new List<string> { "a", "b" });
                        modal.HandleKey(KeyEvent.Char((int)'e'));          // begin editing "a"
                        modal.HandleKey(KeyEvent.Special(KeyCode.Backspace)); // clear "a"
                        modal.HandleKey(KeyEvent.Char((int)'x'));
                        modal.HandleKey(KeyEvent.Char((int)'y'));
                        modal.HandleKey(KeyEvent.Special(KeyCode.Enter));  // commit → "xy"
                        modal.HandleKey(KeyEvent.Special(KeyCode.Escape));

                        List<string> result = (List<string>)(await modal.Completion.ConfigureAwait(false))!;
                        MuxAssert.AreEqual("xy,b", string.Join(",", result), "edited");
                    }),

                    // ---- Thinking indicator ----
                    Case("ThinkingLibraryLoadsFromResource", "The thinking-message library loads a large set from the embedded resource", async (CancellationToken ct) =>
                    {
                        await Task.CompletedTask.ConfigureAwait(false);
                        MuxAssert.IsTrue(ThinkingMessages.All.Count > 100, "many thinking phrases loaded");
                        MuxAssert.IsTrue(ThinkingMessages.All.Contains("Thinking..."), "a known phrase from the library is present");
                        MuxAssert.IsTrue(ThinkingMessages.Spinner.Count > 0, "spinner frames present");
                    }),

                    Case("ThinkingIndicatorShownWhileWorkingThenHidden", "A thinking indicator shows beneath the prompt while the model works and vanishes when output begins", async (CancellationToken ct) =>
                    {
                        TaskCompletionSource<bool> release = NewSignal();
                        await using (JobManager manager = new JobManager(Gated(release), maxConcurrency: 2))
                        using (MuxTuiApp app = NewApp(backend: out HeadlessBackend backend, manager: manager))
                        {
                            Feed(backend, app, "hello" + "\r");
                            await WaitForActiveAsync(manager, ct).ConfigureAwait(false);

                            MuxAssert.IsTrue(app.IsThinking, "indicator shown while working");
                            string message = app.CurrentThinkingMessage ?? string.Empty;
                            MuxAssert.IsTrue(ThinkingMessages.All.Contains(message), "indicator shows a phrase from the library");
                            MuxAssert.Contains(message, Join(app.TranscriptSnapshot()), "indicator rendered into the transcript");

                            release.TrySetResult(true);
                            await app.DrainProjectorsAsync().ConfigureAwait(false);

                            MuxAssert.IsFalse(app.IsThinking, "indicator hidden once results stream");
                            MuxAssert.IsTrue(app.CurrentThinkingMessage == null, "no active thinking phrase after completion");
                        }
                    }),

                    // ---- Slash routing ----
                    Case("SlashInputRoutesToHandler", "A leading slash routes to the slash handler, not a job", async (CancellationToken ct) =>
                    {
                        HeadlessBackend backend = new HeadlessBackend(100, 24);
                        await using (JobManager manager = NewManager(EchoRunner))
                        using (MuxTuiApp app = NewApp(backend, manager))
                        {
                            string? captured = null;
                            app.SlashHandler = (string input) => { captured = input; return true; };

                            Feed(backend, app, "/model list" + "\r");
                            await Task.CompletedTask.ConfigureAwait(false);

                            MuxAssert.AreEqual("/model list", captured, "handler received input");
                            MuxAssert.AreEqual(0, manager.Jobs.Count, "no job created");
                        }
                    }),

                    Case("UnknownSlashWritesNotice", "An unhandled slash command writes a notice", async (CancellationToken ct) =>
                    {
                        HeadlessBackend backend = new HeadlessBackend(100, 24);
                        await using (JobManager manager = NewManager(EchoRunner))
                        using (MuxTuiApp app = NewApp(backend, manager))
                        {
                            Feed(backend, app, "/bogus" + "\r");
                            await Task.CompletedTask.ConfigureAwait(false);

                            MuxAssert.AreEqual(0, manager.Jobs.Count, "no job created");
                            MuxAssert.Contains("Unknown command", Join(app.TranscriptSnapshot()), "notice written");
                        }
                    }),

                    // ---- History recall ----
                    Case("UpArrowRecallsLastPrompt", "Up recalls the last submitted prompt into the composer", async (CancellationToken ct) =>
                    {
                        HeadlessBackend backend = new HeadlessBackend(100, 24);
                        await using (JobManager manager = NewManager(EchoRunner))
                        using (MuxTuiApp app = NewApp(backend, manager))
                        {
                            Feed(backend, app, "alpha" + "\r");
                            await app.DrainProjectorsAsync().ConfigureAwait(false);
                            MuxAssert.AreEqual(string.Empty, app.ComposerText, "composer cleared after submit");

                            Feed(backend, app, Esc + "[A"); // Up
                            MuxAssert.AreEqual("alpha", app.ComposerText, "recalled last prompt");

                            Feed(backend, app, Esc + "[B"); // Down -> fresh draft
                            MuxAssert.AreEqual(string.Empty, app.ComposerText, "back to fresh draft");
                        }
                    })
                });
        }

        #region Helpers

        private static TestCaseDescriptor Case(string id, string name, Func<CancellationToken, Task> body)
        {
            return new TestCaseDescriptor(SuiteId, id, name, body);
        }

        private static JobManager NewManager(Func<Job, string, CancellationToken, IAsyncEnumerable<AgentEvent>> runner)
        {
            return new JobManager(runner, maxConcurrency: 2);
        }

        private static MuxTuiApp NewApp(HeadlessBackend backend, JobManager manager)
        {
            // Default (Ask) enqueue behavior so the chooser cases exercise it.
            return new MuxTuiApp(backend, manager, "demo", ApprovalPolicyEnum.AutoApprove);
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

        private static Func<Job, string, CancellationToken, IAsyncEnumerable<AgentEvent>> Gated(TaskCompletionSource<bool> release)
        {
            return (Job job, string prompt, CancellationToken ct) => GatedStream(release, ct);
        }

        private static async IAsyncEnumerable<AgentEvent> GatedStream(TaskCompletionSource<bool> release, [EnumeratorCancellation] CancellationToken ct)
        {
            await release.Task.WaitAsync(ct).ConfigureAwait(false);
            yield return new AssistantTextEvent { Text = "done" };
            yield return CompletedEvent();
        }

        private static async IAsyncEnumerable<AgentEvent> EchoRunner(Job job, string prompt, [EnumeratorCancellation] CancellationToken ct)
        {
            await Task.CompletedTask.ConfigureAwait(false);
            yield return new AssistantTextEvent { Text = "Echo: " + prompt };
            yield return CompletedEvent();
        }

        private static RunCompletedEvent CompletedEvent()
        {
            return new RunCompletedEvent { RunId = Guid.NewGuid().ToString("N"), Status = "completed", IterationsCompleted = 1, DurationMs = 1 };
        }

        private static TaskCompletionSource<bool> NewSignal()
        {
            return new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        private static string Join(IReadOnlyList<string> lines)
        {
            return string.Join("\n", lines);
        }

        private static async Task WaitForActiveAsync(JobManager manager, CancellationToken cancellationToken)
        {
            using (CancellationTokenSource timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30)))
            using (CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token))
            {
                while (!linked.Token.IsCancellationRequested)
                {
                    foreach (Job job in manager.Jobs)
                    {
                        if (job.State == JobState.Running)
                        {
                            return;
                        }
                    }

                    await Task.Delay(10, linked.Token).ConfigureAwait(false);
                }

                linked.Token.ThrowIfCancellationRequested();
            }
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

        #endregion
    }
}
