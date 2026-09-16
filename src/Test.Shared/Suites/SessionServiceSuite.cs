namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Enums;
    using Mux.Core.Models;
    using Mux.Core.Sessions;
    using Touchstone.Core;

    /// <summary>
    /// Touchstone suite for the consolidated session lifecycle: <see cref="SessionMergePolicy"/> (anti-truncation
    /// reconciliation) and <see cref="SessionService.PersistConversationAsync"/> (load-merge-save that every
    /// surface shares). Each store case runs against an isolated temporary directory.
    /// </summary>
    public static class SessionServiceSuite
    {
        /// <summary>
        /// Builds the session-service suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/>.</returns>
        public static TestSuiteDescriptor Create()
        {
            return new TestSuiteDescriptor(
                "SessionService",
                "Canonical session merge + persist shared across surfaces",
                new List<TestCaseDescriptor>
                {
                    new TestCaseDescriptor("SessionService", "ReconcileTakesIncomingWhenNoExisting", "Reconcile returns the incoming history when the store has none", (CancellationToken ct) =>
                    {
                        List<ConversationMessage> incoming = History("a", "b");
                        List<ConversationMessage> result = SessionMergePolicy.Reconcile(null, incoming);
                        MuxAssert.AreEqual(2, result.Count, "count");
                        MuxAssert.IsTrue(ReferenceEquals(result, incoming), "returns the incoming list");
                        return Task.CompletedTask;
                    }),
                    new TestCaseDescriptor("SessionService", "ReconcileGrowsWhenExistingIsPrefix", "Reconcile keeps the longer incoming history when existing is its prefix (normal turn growth)", (CancellationToken ct) =>
                    {
                        List<ConversationMessage> existing = History("a", "b");
                        List<ConversationMessage> incoming = History("a", "b", "c", "d");
                        List<ConversationMessage> result = SessionMergePolicy.Reconcile(existing, incoming);
                        MuxAssert.AreEqual(4, result.Count, "count");
                        return Task.CompletedTask;
                    }),
                    new TestCaseDescriptor("SessionService", "ReconcileDoesNotTruncateOnStalePrefix", "Reconcile keeps the store's longer history when the incoming view is a stale prefix", (CancellationToken ct) =>
                    {
                        // The anti-truncation guarantee: a surface whose in-memory view is behind the store (it
                        // holds [a,b] while the store already has [a,b,c,d]) must not overwrite the store back to
                        // [a,b] and drop c,d.
                        List<ConversationMessage> existing = History("a", "b", "c", "d");
                        List<ConversationMessage> incoming = History("a", "b");
                        List<ConversationMessage> result = SessionMergePolicy.Reconcile(existing, incoming);
                        MuxAssert.AreEqual(4, result.Count, "count kept at the store's length");
                        MuxAssert.AreEqual("c", result[2].Content, "third message preserved");
                        MuxAssert.AreEqual("d", result[3].Content, "fourth message preserved");
                        return Task.CompletedTask;
                    }),
                    new TestCaseDescriptor("SessionService", "ReconcileActiveWinsOnDivergence", "Reconcile takes the incoming (active) history when the two diverge", (CancellationToken ct) =>
                    {
                        List<ConversationMessage> existing = History("a", "b", "c");
                        List<ConversationMessage> incoming = History("a", "x", "y");
                        List<ConversationMessage> result = SessionMergePolicy.Reconcile(existing, incoming);
                        MuxAssert.AreEqual(3, result.Count, "count");
                        MuxAssert.AreEqual("x", result[1].Content, "active surface's second message wins");
                        return Task.CompletedTask;
                    }),
                    new TestCaseDescriptor("SessionService", "ReconcileEmptyIncomingKeepsExisting", "Reconcile never wipes a populated store with an empty incoming history", (CancellationToken ct) =>
                    {
                        List<ConversationMessage> existing = History("a", "b");
                        List<ConversationMessage> result = SessionMergePolicy.Reconcile(existing, new List<ConversationMessage>());
                        MuxAssert.AreEqual(2, result.Count, "existing kept");
                        return Task.CompletedTask;
                    }),
                    new TestCaseDescriptor("SessionService", "PersistReconcilesAgainstStore", "PersistConversationAsync does not truncate a store that grew under a stale surface", async (CancellationToken ct) =>
                    {
                        string dir = TempDir();
                        try
                        {
                            SessionStore store = new SessionStore(dir);
                            SessionService service = new SessionService(store);

                            // The store already has a four-message conversation (extended on another surface).
                            SessionSnapshot ahead = new SessionSnapshot { Id = "s", Title = "T" };
                            ahead.ConversationHistory = History("a", "b", "c", "d");
                            await store.SaveAsync(ahead, ct).ConfigureAwait(false);

                            // A surface persists its stale two-message view of the same session.
                            SessionSnapshot stale = new SessionSnapshot { Id = "s" };
                            stale.ConversationHistory = History("a", "b");
                            SessionSnapshot persisted = await service.PersistConversationAsync(stale, ct).ConfigureAwait(false);

                            MuxAssert.AreEqual(4, persisted.ConversationHistory.Count, "history not truncated");
                            SessionSnapshot? reloaded = await store.LoadAsync("s", ct).ConfigureAwait(false);
                            MuxAssert.IsNotNull(reloaded, "reloaded");
                            MuxAssert.AreEqual(4, reloaded!.ConversationHistory.Count, "store keeps four messages");
                        }
                        finally
                        {
                            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch (Exception) { }
                        }
                    }),
                    new TestCaseDescriptor("SessionService", "PersistPreservesUnauthoredFields", "PersistConversationAsync preserves created time, pin, jobs, and prompt history a surface did not author", async (CancellationToken ct) =>
                    {
                        string dir = TempDir();
                        try
                        {
                            SessionStore store = new SessionStore(dir);
                            SessionService service = new SessionService(store);

                            DateTime created = new DateTime(2025, 5, 5, 0, 0, 0, DateTimeKind.Utc);
                            SessionSnapshot rich = new SessionSnapshot { Id = "s", Title = "Pinned", TitlePinned = true, CreatedUtc = created, CompactionCount = 2 };
                            rich.ConversationHistory = History("a", "b");
                            rich.PromptHistory = new List<string> { "p1", "p2" };
                            rich.Jobs = new List<PersistedJobSnapshot> { new PersistedJobSnapshot() };
                            await store.SaveAsync(rich, ct).ConfigureAwait(false);

                            // A surface that only tracks the conversation persists a follow-up turn.
                            SessionSnapshot lean = new SessionSnapshot { Id = "s" };
                            lean.ConversationHistory = History("a", "b", "c", "d");
                            SessionSnapshot persisted = await service.PersistConversationAsync(lean, ct).ConfigureAwait(false);

                            MuxAssert.AreEqual(created, persisted.CreatedUtc, "created time preserved");
                            MuxAssert.IsTrue(persisted.TitlePinned, "pin preserved");
                            MuxAssert.AreEqual("Pinned", persisted.Title, "title preserved");
                            MuxAssert.AreEqual(2, persisted.CompactionCount, "compaction count preserved");
                            MuxAssert.AreEqual(2, persisted.PromptHistory.Count, "prompt history preserved");
                            MuxAssert.AreEqual(1, persisted.Jobs.Count, "jobs preserved");
                            MuxAssert.AreEqual(4, persisted.ConversationHistory.Count, "new turn appended");
                        }
                        finally
                        {
                            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch (Exception) { }
                        }
                    }),
                    new TestCaseDescriptor("SessionService", "SubscriptionRaisesFreshSnapshotOnWrite", "SessionTranscriptSubscription raises the fresh full snapshot when the watched session is written", async (CancellationToken ct) =>
                    {
                        string dir = TempDir();
                        SessionTranscriptSubscription? sub = null;
                        try
                        {
                            SessionStore store = new SessionStore(dir);
                            Directory.CreateDirectory(dir);
                            TaskCompletionSource<SessionSnapshot> got = new TaskCompletionSource<SessionSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
                            sub = new SessionTranscriptSubscription(store, "watch");
                            sub.Changed += snap => got.TrySetResult(snap);
                            sub.Start();
                            await Task.Delay(150, ct).ConfigureAwait(false);

                            SessionSnapshot snapshot = new SessionSnapshot { Id = "watch", Title = "W" };
                            snapshot.ConversationHistory = History("a", "b");
                            await store.SaveAsync(snapshot, ct).ConfigureAwait(false);

                            Task finished = await Task.WhenAny(got.Task, Task.Delay(TimeSpan.FromSeconds(4), ct)).ConfigureAwait(false);
                            MuxAssert.IsTrue(ReferenceEquals(finished, got.Task) && got.Task.IsCompleted, "the subscription raised Changed");
                            MuxAssert.AreEqual(2, got.Task.Result.ConversationHistory.Count, "fresh snapshot carried the full history");
                        }
                        finally
                        {
                            sub?.Dispose();
                            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch (Exception) { }
                        }
                    })
                });
        }

        private static List<ConversationMessage> History(params string[] contents)
        {
            List<ConversationMessage> list = new List<ConversationMessage>();
            for (int i = 0; i < contents.Length; i++)
            {
                list.Add(new ConversationMessage
                {
                    Role = (i % 2 == 0) ? RoleEnum.User : RoleEnum.Assistant,
                    Content = contents[i]
                });
            }

            return list;
        }

        private static string TempDir()
        {
            return Path.Combine(Path.GetTempPath(), "mux-svc-" + Guid.NewGuid().ToString("N"));
        }
    }
}
