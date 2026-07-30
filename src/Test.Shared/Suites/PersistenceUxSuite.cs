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
    using Mux.Core.Sessions;
    using Touchstone.Core;
    using TUIKit.Terminal;

    /// <summary>
    /// Touchstone suite for M12 persistence UX: the engine snapshot builder, save/load round-trips,
    /// autosave at turn boundaries, session restore (completed vs interrupted jobs), prompt-history
    /// survival across a restart, and the session browser. Positive and negative paths.
    /// </summary>
    public static class PersistenceUxSuite
    {
        private const string SuiteId = "PersistenceUx";
        private const char Esc = (char)27;

        /// <summary>
        /// Builds the persistence-UX suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/> for persistence cases.</returns>
        public static TestSuiteDescriptor Create()
        {
            return new TestSuiteDescriptor(
                SuiteId,
                "Session snapshot, save/load, autosave, restore, and browser",
                new List<TestCaseDescriptor>
                {
                    // ---- Snapshot builder (engine) ----
                    Case("BuilderCapturesJobsAndHistory", "The snapshot builder captures jobs and prompt history", async (CancellationToken ct) =>
                    {
                        await using (JobManager manager = new JobManager(EchoRunner, maxConcurrency: 2))
                        {
                            Job a = await manager.SubmitAsync("alpha", ct).ConfigureAwait(false);
                            Job b = await manager.SubmitAsync("beta", ct).ConfigureAwait(false);
                            await WaitStateAsync(a, JobState.Completed, ct).ConfigureAwait(false);
                            await WaitStateAsync(b, JobState.Completed, ct).ConfigureAwait(false);

                            SessionSnapshot snapshot = SessionSnapshotBuilder.Build(
                                manager, "s1", "My Session", "ep", "model", new[] { "alpha", "beta" }, DateTime.UnixEpoch);

                            MuxAssert.AreEqual("s1", snapshot.Id, "id");
                            MuxAssert.AreEqual(2, snapshot.Jobs.Count, "two jobs");
                            MuxAssert.AreEqual("alpha", snapshot.Jobs[0].Prompt, "first prompt");
                            MuxAssert.AreEqual("Completed", snapshot.Jobs[0].State, "first state");
                            MuxAssert.AreEqual(2, snapshot.PromptHistory.Count, "prompt history");
                        }
                    }),

                    Case("BuilderRejectsNullManager", "The snapshot builder rejects a null manager", (CancellationToken ct) =>
                    {
                        bool threw = false;
                        try
                        {
                            SessionSnapshotBuilder.Build(null!, "s", "t", "e", "m", null, DateTime.UnixEpoch);
                        }
                        catch (ArgumentNullException)
                        {
                            threw = true;
                        }

                        MuxAssert.IsTrue(threw, "throws on null manager");
                        return Task.CompletedTask;
                    }),

                    // ---- Save / load round trip ----
                    Case("SaveThenLoadRestoresJob", "Saving then loading restores the submitted job", async (CancellationToken ct) =>
                    {
                        string dir = NewTempDir();
                        try
                        {
                            SessionStore store = new SessionStore(dir);
                            await using (JobManager manager = new JobManager(EchoRunner, maxConcurrency: 2))
                            using (MuxTuiApp app = NewApp(store, out HeadlessBackend backend, manager))
                            {
                                Submit(backend, app, "hello");
                                await app.DrainProjectorsAsync().ConfigureAwait(false);
                                await app.SaveSessionAsync().ConfigureAwait(false);

                                SessionSnapshot? loaded = await store.LoadAsync(manager.SessionId, ct).ConfigureAwait(false);
                                MuxAssert.IsNotNull(loaded, "session loaded");
                                MuxAssert.AreEqual(1, loaded!.Jobs.Count, "one job persisted");
                                MuxAssert.AreEqual("hello", loaded.Jobs[0].Prompt, "prompt persisted");
                            }
                        }
                        finally
                        {
                            TryDelete(dir);
                        }
                    }),

                    Case("SaveWithoutStoreIsNoOp", "Saving with no store does not throw", async (CancellationToken ct) =>
                    {
                        await using (JobManager manager = new JobManager(EchoRunner, maxConcurrency: 2))
                        using (MuxTuiApp app = new MuxTuiApp(new HeadlessBackend(100, 24), manager, "demo", ApprovalPolicyEnum.AutoApprove))
                        {
                            await app.SaveSessionAsync().ConfigureAwait(false); // no store -> no-op, no throw
                            MuxAssert.IsTrue(true, "did not throw");
                        }
                    }),

                    Case("AutoSaveFiresOnJobCompletion", "A completed job triggers an autosave", async (CancellationToken ct) =>
                    {
                        string dir = NewTempDir();
                        try
                        {
                            SessionStore store = new SessionStore(dir);
                            await using (JobManager manager = new JobManager(EchoRunner, maxConcurrency: 2))
                            using (MuxTuiApp app = NewApp(store, out HeadlessBackend backend, manager))
                            {
                                Submit(backend, app, "work");
                                await WaitUntilAsync(() => ContainsId(store, manager.SessionId), ct).ConfigureAwait(false);
                                MuxAssert.IsTrue(ContainsId(store, manager.SessionId), "session autosaved");
                            }
                        }
                        finally
                        {
                            TryDelete(dir);
                        }
                    }),

                    // ---- Restart / restore ----
                    Case("PromptHistorySurvivesRestart", "Prompt history survives save and restore into a fresh shell", async (CancellationToken ct) =>
                    {
                        string dir = NewTempDir();
                        try
                        {
                            SessionStore store = new SessionStore(dir);
                            string sessionId;

                            await using (JobManager manager1 = new JobManager(EchoRunner, maxConcurrency: 2))
                            using (MuxTuiApp app1 = NewApp(store, out HeadlessBackend backend1, manager1))
                            {
                                Submit(backend1, app1, "first");
                                Submit(backend1, app1, "second");
                                await app1.DrainProjectorsAsync().ConfigureAwait(false);
                                await app1.SaveSessionAsync().ConfigureAwait(false);
                                sessionId = manager1.SessionId;
                            }

                            SessionSnapshot? loaded = await store.LoadAsync(sessionId, ct).ConfigureAwait(false);
                            MuxAssert.IsNotNull(loaded, "reloaded");

                            await using (JobManager manager2 = new JobManager(EchoRunner, maxConcurrency: 2))
                            using (MuxTuiApp app2 = NewApp(store, out HeadlessBackend backend2, manager2))
                            {
                                app2.RestoreSession(SessionResumeService.Resume(loaded!));
                                Feed(backend2, app2, Esc + "[A"); // Up -> newest history entry
                                MuxAssert.AreEqual("second", app2.ComposerText, "recalled newest prompt after restart");
                            }
                        }
                        finally
                        {
                            TryDelete(dir);
                        }
                    }),

                    Case("RestoreRebuildsCompletedPane", "Restore renders a completed job's conversation read-only", async (CancellationToken ct) =>
                    {
                        await using (JobManager manager = new JobManager(EchoRunner, maxConcurrency: 2))
                        using (MuxTuiApp app = new MuxTuiApp(new HeadlessBackend(100, 24), manager, "demo", ApprovalPolicyEnum.AutoApprove))
                        {
                            await Task.CompletedTask.ConfigureAwait(false);
                            SessionSnapshot snapshot = SnapshotWithJob("j1", "Completed", "do it", "did it");
                            app.RestoreSession(SessionResumeService.Resume(snapshot));

                            string transcript = Join(app.JobTranscriptSnapshot("j1"));
                            MuxAssert.Contains("resumed", transcript, "resume header");
                            MuxAssert.Contains("do it", transcript, "user message restored");
                            MuxAssert.Contains("did it", transcript, "assistant message restored");
                            MuxAssert.IsFalse(transcript.Contains("re-run required", StringComparison.Ordinal), "completed not marked interrupted");
                        }
                    }),

                    Case("RestoreMarksInterruptedJob", "Restore marks an interrupted job as re-run required", async (CancellationToken ct) =>
                    {
                        await using (JobManager manager = new JobManager(EchoRunner, maxConcurrency: 2))
                        using (MuxTuiApp app = new MuxTuiApp(new HeadlessBackend(100, 24), manager, "demo", ApprovalPolicyEnum.AutoApprove))
                        {
                            await Task.CompletedTask.ConfigureAwait(false);
                            SessionSnapshot snapshot = SnapshotWithJob("j9", "Running", "long task", null);
                            app.RestoreSession(SessionResumeService.Resume(snapshot));

                            MuxAssert.Contains("re-run required", Join(app.JobTranscriptSnapshot("j9")), "interrupted marked");
                            MuxAssert.AreEqual(0, manager.Jobs.Count, "no job auto-run on restore");
                        }
                    }),

                    // ---- Session browser ----
                    Case("SessionBrowserResumesSelected", "The session browser resumes the selected session", async (CancellationToken ct) =>
                    {
                        string dir = NewTempDir();
                        try
                        {
                            SessionStore store = new SessionStore(dir);
                            await store.SaveAsync(SnapshotWithJob("jx", "Completed", "hi there", "hello back"), ct).ConfigureAwait(false);

                            await using (JobManager manager = new JobManager(EchoRunner, maxConcurrency: 2))
                            using (MuxTuiApp app = NewApp(store, out HeadlessBackend backend, manager))
                            {
                                Feed(backend, app, "/sessions" + "\r");
                                MuxAssert.IsTrue(app.IsModalActive, "browser open");
                                Feed(backend, app, "\r"); // select first

                                await WaitUntilAsync(() => app.JobIds.Count == 1, ct).ConfigureAwait(false);
                                MuxAssert.Contains("hi there", Join(app.JobTranscriptSnapshot("jx")), "resumed transcript");
                            }
                        }
                        finally
                        {
                            TryDelete(dir);
                        }
                    }),

                    Case("SessionBrowserEmptyShowsMessage", "The session browser shows a message when empty", async (CancellationToken ct) =>
                    {
                        string dir = NewTempDir();
                        try
                        {
                            SessionStore store = new SessionStore(dir);
                            await using (JobManager manager = new JobManager(EchoRunner, maxConcurrency: 2))
                            using (MuxTuiApp app = NewApp(store, out HeadlessBackend backend, manager))
                            {
                                await Task.CompletedTask.ConfigureAwait(false);
                                Feed(backend, app, "/sessions" + "\r");
                                MuxAssert.IsTrue(app.IsModalActive, "message modal shown");
                                Feed(backend, app, "\r");
                                MuxAssert.IsFalse(app.IsModalActive, "dismissed");
                            }
                        }
                        finally
                        {
                            TryDelete(dir);
                        }
                    }),

                    Case("SessionBrowserDisabledWithoutStore", "The session browser reports when persistence is disabled", async (CancellationToken ct) =>
                    {
                        HeadlessBackend backend = new HeadlessBackend(100, 24);
                        await using (JobManager manager = new JobManager(EchoRunner, maxConcurrency: 2))
                        using (MuxTuiApp app = new MuxTuiApp(backend, manager, "demo", ApprovalPolicyEnum.AutoApprove))
                        {
                            await Task.CompletedTask.ConfigureAwait(false);
                            Feed(backend, app, "/sessions" + "\r");
                            MuxAssert.IsTrue(app.IsModalActive, "disabled message shown");
                        }
                    })
                });
        }

        #region Helpers

        private static TestCaseDescriptor Case(string id, string name, Func<CancellationToken, Task> body)
        {
            return new TestCaseDescriptor(SuiteId, id, name, body);
        }

        private static MuxTuiApp NewApp(SessionStore store, out HeadlessBackend backend, JobManager manager)
        {
            backend = new HeadlessBackend(100, 24);
            MuxTuiApp app = new MuxTuiApp(backend, manager, "demo", ApprovalPolicyEnum.AutoApprove, store, "ep", "model");
            app.DefaultEnqueueBehavior = EnqueueBehavior.NewJob;
            return app;
        }

        private static void Submit(HeadlessBackend backend, MuxTuiApp app, string prompt)
        {
            backend.FeedInput(prompt + "\r");
            app.PumpInputOnce();
        }

        private static void Feed(HeadlessBackend backend, MuxTuiApp app, string input)
        {
            backend.FeedInput(input);
            app.PumpInputOnce();
        }

        private static SessionSnapshot SnapshotWithJob(string jobId, string state, string userText, string? assistantText)
        {
            List<ConversationMessage> history = new List<ConversationMessage>
            {
                new ConversationMessage { Role = RoleEnum.User, Content = userText }
            };
            if (assistantText != null)
            {
                history.Add(new ConversationMessage { Role = RoleEnum.Assistant, Content = assistantText });
            }

            SessionSnapshot snapshot = new SessionSnapshot { Id = "sess", Title = "Saved" };
            snapshot.PromptHistory.Add(userText);
            snapshot.Jobs.Add(new PersistedJobSnapshot
            {
                Id = jobId,
                Title = userText,
                Prompt = userText,
                State = state,
                ApprovalPolicy = "AutoApprove",
                ConversationHistory = history
            });

            return snapshot;
        }

        private static bool ContainsId(SessionStore store, string id)
        {
            foreach (string existing in store.ListSessionIds())
            {
                if (string.Equals(existing, id, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static string NewTempDir()
        {
            return Path.Combine(Path.GetTempPath(), "mux-persist-" + Guid.NewGuid().ToString("N"));
        }

        private static void TryDelete(string dir)
        {
            try
            {
                if (Directory.Exists(dir))
                {
                    Directory.Delete(dir, true);
                }
            }
            catch (IOException)
            {
            }
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

        private static async Task WaitStateAsync(Job job, JobState state, CancellationToken cancellationToken)
        {
            using (CancellationTokenSource timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30)))
            using (CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token))
            {
                while (job.State != state)
                {
                    await Task.Delay(10, linked.Token).ConfigureAwait(false);
                }
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
