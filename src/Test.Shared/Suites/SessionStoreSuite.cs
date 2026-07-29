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
    /// Touchstone suite for <see cref="SessionStore"/> persistence plus <see cref="SessionResumeService"/>
    /// and <see cref="SessionMergeService"/>. Each case runs against an isolated temporary directory.
    /// </summary>
    public static class SessionStoreSuite
    {
        /// <summary>
        /// Builds the session-store suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/> for the session-persistence cases.</returns>
        public static TestSuiteDescriptor Create()
        {
            return new TestSuiteDescriptor(
                "SessionStore",
                "Session persistence, resume, and merge",
                new List<TestCaseDescriptor>
                {
                    Case("RoundTripPreservesFields", "Save then load preserves all session fields", async (SessionStore store, string dir, CancellationToken ct) =>
                    {
                        SessionSnapshot original = BuildRichSnapshot("s1", "Refactor \"auth\" & <flow> 世界");
                        await store.SaveAsync(original, ct).ConfigureAwait(false);

                        SessionSnapshot? loaded = await store.LoadAsync("s1", ct).ConfigureAwait(false);
                        MuxAssert.IsNotNull(loaded, "loaded");
                        MuxAssert.AreEqual(1, loaded!.SchemaVersion, "schemaVersion");
                        MuxAssert.AreEqual("s1", loaded.Id, "id");
                        MuxAssert.AreEqual(original.Title, loaded.Title, "title (special chars)");
                        MuxAssert.IsTrue(loaded.TitlePinned, "titlePinned");
                        MuxAssert.AreEqual("openai-prod", loaded.EndpointName, "endpoint");
                        MuxAssert.AreEqual("gpt-4o", loaded.Model, "model");
                        MuxAssert.AreEqual(2, loaded.CompactionCount, "compactionCount");
                        MuxAssert.AreEqual(2, loaded.ConversationHistory.Count, "history count");
                        MuxAssert.AreEqual(RoleEnum.User, loaded.ConversationHistory[0].Role, "history[0] role");
                        MuxAssert.AreEqual("hello", loaded.ConversationHistory[0].Content, "history[0] content");
                        MuxAssert.AreEqual(2, loaded.PromptHistory.Count, "prompt history count");
                        MuxAssert.AreEqual("second prompt", loaded.PromptHistory[1], "prompt history[1]");
                        MuxAssert.AreEqual(1, loaded.Jobs.Count, "jobs count");
                        MuxAssert.AreEqual("j1", loaded.Jobs[0].Id, "job id");
                        MuxAssert.AreEqual("Running", loaded.Jobs[0].State, "job state");
                        MuxAssert.AreEqual("AutoSafe", loaded.Jobs[0].ApprovalPolicy, "job policy");
                        MuxAssert.AreEqual(1, loaded.Jobs[0].PendingFollowUps.Count, "job follow-ups");
                        MuxAssert.AreEqual("do more", loaded.Jobs[0].PendingFollowUps[0], "job follow-up[0]");
                        MuxAssert.AreEqual(1, loaded.Jobs[0].ConversationHistory.Count, "job history count");
                    }),

                    Case("ListExcludesTempAndUnknownFiles", "ListSessionIds returns saved ids and ignores temp files", async (SessionStore store, string dir, CancellationToken ct) =>
                    {
                        await store.SaveAsync(BuildRichSnapshot("alpha", "A"), ct).ConfigureAwait(false);
                        await store.SaveAsync(BuildRichSnapshot("beta", "B"), ct).ConfigureAwait(false);
                        File.WriteAllText(Path.Combine(dir, "stale.json.tmp"), "garbage");

                        List<string> ids = new List<string>(store.ListSessionIds());
                        ids.Sort(StringComparer.Ordinal);
                        MuxAssert.AreEqual(2, ids.Count, "id count");
                        MuxAssert.AreEqual("alpha", ids[0], "id 0");
                        MuxAssert.AreEqual("beta", ids[1], "id 1");
                    }),

                    Case("SaveLeavesNoTempFile", "A completed save leaves no .tmp artifact", async (SessionStore store, string dir, CancellationToken ct) =>
                    {
                        await store.SaveAsync(BuildRichSnapshot("s1", "T"), ct).ConfigureAwait(false);
                        MuxAssert.IsTrue(File.Exists(Path.Combine(dir, "s1.json")), "final file exists");
                        MuxAssert.IsFalse(File.Exists(Path.Combine(dir, "s1.json.tmp")), "no temp left behind");
                    }),

                    Case("SaveOverwriteReplacesAtomically", "Re-saving replaces the file with the new content", async (SessionStore store, string dir, CancellationToken ct) =>
                    {
                        await store.SaveAsync(BuildRichSnapshot("s1", "first"), ct).ConfigureAwait(false);
                        SessionSnapshot second = BuildRichSnapshot("s1", "second");
                        await store.SaveAsync(second, ct).ConfigureAwait(false);
                        SessionSnapshot? loaded = await store.LoadAsync("s1", ct).ConfigureAwait(false);
                        MuxAssert.IsNotNull(loaded, "loaded");
                        MuxAssert.AreEqual("second", loaded!.Title, "overwritten title");
                    }),

                    Case("DeleteRemovesSession", "Delete removes a session and reports outcome", async (SessionStore store, string dir, CancellationToken ct) =>
                    {
                        await store.SaveAsync(BuildRichSnapshot("s1", "T"), ct).ConfigureAwait(false);
                        bool deleted = await store.DeleteAsync("s1", ct).ConfigureAwait(false);
                        MuxAssert.IsTrue(deleted, "deleted true");
                        MuxAssert.IsNull(await store.LoadAsync("s1", ct).ConfigureAwait(false), "load null after delete");
                        bool again = await store.DeleteAsync("s1", ct).ConfigureAwait(false);
                        MuxAssert.IsFalse(again, "delete missing returns false");
                    }),

                    Case("DuplicateCreatesIndependentCopy", "Duplicate copies content under a new id, leaving the source", async (SessionStore store, string dir, CancellationToken ct) =>
                    {
                        await store.SaveAsync(BuildRichSnapshot("src", "Original"), ct).ConfigureAwait(false);
                        SessionSnapshot? copy = await store.DuplicateAsync("src", "dst", ct).ConfigureAwait(false);
                        MuxAssert.IsNotNull(copy, "copy");
                        MuxAssert.AreEqual("dst", copy!.Id, "copy id");
                        SessionSnapshot? loadedCopy = await store.LoadAsync("dst", ct).ConfigureAwait(false);
                        MuxAssert.IsNotNull(loadedCopy, "loaded copy");
                        MuxAssert.AreEqual("Original", loadedCopy!.Title, "copy title");
                        MuxAssert.IsNotNull(await store.LoadAsync("src", ct).ConfigureAwait(false), "source still present");
                    }),

                    Case("DuplicateMissingSourceReturnsNull", "Duplicating a missing source returns null", async (SessionStore store, string dir, CancellationToken ct) =>
                    {
                        SessionSnapshot? copy = await store.DuplicateAsync("nope", "dst", ct).ConfigureAwait(false);
                        MuxAssert.IsNull(copy, "null copy");
                    }),

                    Case("LoadMissingReturnsNull", "Loading a missing session returns null", async (SessionStore store, string dir, CancellationToken ct) =>
                    {
                        MuxAssert.IsNull(await store.LoadAsync("ghost", ct).ConfigureAwait(false), "missing load null");
                    }),

                    Case("UnknownFieldsAreTolerated", "Deserialization ignores unknown JSON fields (forward compat)", async (SessionStore store, string dir, CancellationToken ct) =>
                    {
                        Directory.CreateDirectory(dir);
                        string json = "{\"schemaVersion\":1,\"id\":\"s1\",\"title\":\"Future\",\"futureOnlyField\":{\"nested\":true},\"conversationHistory\":[],\"promptHistory\":[\"p\"]}";
                        File.WriteAllText(Path.Combine(dir, "s1.json"), json);

                        SessionSnapshot? loaded = await store.LoadAsync("s1", ct).ConfigureAwait(false);
                        MuxAssert.IsNotNull(loaded, "loaded");
                        MuxAssert.AreEqual("s1", loaded!.Id, "id");
                        MuxAssert.AreEqual("Future", loaded.Title, "title");
                        MuxAssert.AreEqual(1, loaded.PromptHistory.Count, "prompt history");
                    }),

                    Case("InvalidIdThrows", "An id with path separators is rejected", async (SessionStore store, string dir, CancellationToken ct) =>
                    {
                        await MuxAssert.ThrowsAsync<ArgumentException>(async () => await store.LoadAsync("../escape", ct).ConfigureAwait(false), "path-traversal id").ConfigureAwait(false);
                        SessionSnapshot bad = BuildRichSnapshot("a/b", "x");
                        await MuxAssert.ThrowsAsync<ArgumentException>(async () => await store.SaveAsync(bad, ct).ConfigureAwait(false), "separator id").ConfigureAwait(false);
                    }),

                    Case("ResumeClassifiesJobs", "Resume partitions jobs into completed and interrupted", async (SessionStore store, string dir, CancellationToken ct) =>
                    {
                        SessionSnapshot snapshot = BuildRichSnapshot("s1", "Resume");
                        snapshot.Jobs = new List<PersistedJobSnapshot>
                        {
                            new PersistedJobSnapshot { Id = "j1", State = "Running", Prompt = "a" },
                            new PersistedJobSnapshot { Id = "j2", State = "Queued", Prompt = "b" },
                            new PersistedJobSnapshot { Id = "j3", State = "Completed", Prompt = "c" },
                            new PersistedJobSnapshot { Id = "j4", State = "Failed", Prompt = "d" },
                            new PersistedJobSnapshot { Id = "j5", State = "Cancelled", Prompt = "e" }
                        };
                        await store.SaveAsync(snapshot, ct).ConfigureAwait(false);

                        SessionSnapshot? loaded = await store.LoadAsync("s1", ct).ConfigureAwait(false);
                        SessionResumeResult result = SessionResumeService.Resume(loaded!);

                        MuxAssert.AreEqual(2, result.InterruptedJobs.Count, "interrupted (Running+Queued)");
                        MuxAssert.AreEqual(3, result.CompletedJobs.Count, "completed (Completed+Failed+Cancelled)");
                        MuxAssert.AreEqual("Resume", result.Title, "carried title");
                        MuxAssert.AreEqual(2, result.ConversationHistory.Count, "carried history");
                    }),

                    Case("MergeAppendsWithoutMutatingInputs", "Explicit merge appends job messages to focused history", async (SessionStore store, string dir, CancellationToken ct) =>
                    {
                        List<ConversationMessage> focused = new List<ConversationMessage>
                        {
                            new ConversationMessage { Role = RoleEnum.User, Content = "f1" }
                        };
                        List<ConversationMessage> jobMessages = new List<ConversationMessage>
                        {
                            new ConversationMessage { Role = RoleEnum.Assistant, Content = "j1" },
                            new ConversationMessage { Role = RoleEnum.User, Content = "j2" }
                        };

                        List<ConversationMessage> merged = SessionMergeService.Merge(focused, jobMessages);

                        MuxAssert.AreEqual(3, merged.Count, "merged count");
                        MuxAssert.AreEqual("f1", merged[0].Content, "merged[0]");
                        MuxAssert.AreEqual("j2", merged[2].Content, "merged[2]");
                        MuxAssert.AreEqual(1, focused.Count, "focused unchanged");
                        MuxAssert.AreEqual(2, jobMessages.Count, "jobMessages unchanged");
                        await Task.CompletedTask.ConfigureAwait(false);
                    })
                });
        }

        private static SessionSnapshot BuildRichSnapshot(string id, string title)
        {
            return new SessionSnapshot
            {
                Id = id,
                Title = title,
                TitlePinned = true,
                CreatedUtc = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc),
                UpdatedUtc = new DateTime(2026, 1, 2, 3, 5, 6, DateTimeKind.Utc),
                EndpointName = "openai-prod",
                Model = "gpt-4o",
                CompactionCount = 2,
                ConversationHistory = new List<ConversationMessage>
                {
                    new ConversationMessage { Role = RoleEnum.User, Content = "hello" },
                    new ConversationMessage { Role = RoleEnum.Assistant, Content = "hi there" }
                },
                PromptHistory = new List<string> { "first prompt", "second prompt" },
                Jobs = new List<PersistedJobSnapshot>
                {
                    new PersistedJobSnapshot
                    {
                        Id = "j1",
                        Title = "job one",
                        Prompt = "do it",
                        State = "Running",
                        ApprovalPolicy = "AutoSafe",
                        PendingFollowUps = new List<string> { "do more" },
                        ConversationHistory = new List<ConversationMessage>
                        {
                            new ConversationMessage { Role = RoleEnum.User, Content = "job seed" }
                        }
                    }
                }
            };
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, Func<SessionStore, string, CancellationToken, Task> body)
        {
            return new TestCaseDescriptor("SessionStore", caseId, displayName, async (CancellationToken ct) =>
            {
                string dir = Path.Combine(Path.GetTempPath(), "mux_sessions_" + Guid.NewGuid().ToString("N"));
                try
                {
                    await body(new SessionStore(dir), dir, ct).ConfigureAwait(false);
                }
                finally
                {
                    try
                    {
                        if (Directory.Exists(dir)) Directory.Delete(dir, true);
                    }
                    catch (IOException)
                    {
                    }
                }
            });
        }
    }
}
