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
    /// Touchstone suite for <see cref="SessionManager"/> — the one shared implementation of the
    /// session-management verbs used by every surface (list/create/rename/pin/duplicate/delete/export).
    /// Each case runs against an isolated temporary directory.
    /// </summary>
    public static class SessionManagerSuite
    {
        /// <summary>Builds the session-manager suite descriptor.</summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/>.</returns>
        public static TestSuiteDescriptor Create()
        {
            return new TestSuiteDescriptor(
                "SessionManager",
                "Shared session-management verbs over the session store",
                new List<TestCaseDescriptor>
                {
                    Case("CreateRecordsWorkingDirectory", "Create records the working directory and pins an explicit title", async (SessionManager mgr, SessionStore store, CancellationToken ct) =>
                    {
                        SessionInfo info = await mgr.CreateAsync("My session", "ep", "model", "/proj/root", ct).ConfigureAwait(false);
                        MuxAssert.AreEqual("My session", info.Title, "title");
                        MuxAssert.AreEqual("/proj/root", info.WorkingDirectory, "working dir");
                        MuxAssert.IsTrue(info.TitlePinned, "explicit title pins");

                        SessionSnapshot? loaded = await store.LoadAsync(info.Id, ct).ConfigureAwait(false);
                        MuxAssert.IsNotNull(loaded, "persisted");
                        MuxAssert.AreEqual("/proj/root", loaded!.WorkingDirectory, "persisted working dir");
                    }),

                    Case("ListNewestFirst", "List returns sessions newest-updated first", async (SessionManager mgr, SessionStore store, CancellationToken ct) =>
                    {
                        await store.SaveAsync(new SessionSnapshot { Id = "old", Title = "Old", UpdatedUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) }, ct).ConfigureAwait(false);
                        await store.SaveAsync(new SessionSnapshot { Id = "new", Title = "New", UpdatedUtc = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc) }, ct).ConfigureAwait(false);

                        IReadOnlyList<SessionInfo> list = await mgr.ListAsync(ct).ConfigureAwait(false);
                        MuxAssert.AreEqual(2, list.Count, "count");
                        MuxAssert.AreEqual("new", list[0].Id, "newest first");
                        MuxAssert.AreEqual("old", list[1].Id, "oldest last");
                    }),

                    Case("RenamePinsTitle", "Rename updates and pins the title", async (SessionManager mgr, SessionStore store, CancellationToken ct) =>
                    {
                        await store.SaveAsync(new SessionSnapshot { Id = "s1", Title = "Before" }, ct).ConfigureAwait(false);
                        SessionInfo? renamed = await mgr.RenameAsync("s1", "After", ct).ConfigureAwait(false);
                        MuxAssert.IsNotNull(renamed, "renamed");
                        MuxAssert.AreEqual("After", renamed!.Title, "new title");
                        MuxAssert.IsTrue(renamed.TitlePinned, "pinned");

                        SessionInfo? missing = await mgr.RenameAsync("nope", "x", ct).ConfigureAwait(false);
                        MuxAssert.IsNull(missing, "missing rename null");
                    }),

                    Case("DuplicatePreservesJobsAndWorkingDir", "Duplicate copies content (jobs, working dir) under a new id", async (SessionManager mgr, SessionStore store, CancellationToken ct) =>
                    {
                        SessionSnapshot source = new SessionSnapshot
                        {
                            Id = "src",
                            Title = "Original",
                            WorkingDirectory = "/proj",
                            ConversationHistory = new List<ConversationMessage> { new ConversationMessage { Role = RoleEnum.User, Content = "hi" } },
                            Jobs = new List<PersistedJobSnapshot> { new PersistedJobSnapshot { Id = "j1", State = "Completed", Prompt = "p" } }
                        };
                        await store.SaveAsync(source, ct).ConfigureAwait(false);

                        SessionInfo? copy = await mgr.DuplicateAsync("src", ct).ConfigureAwait(false);
                        MuxAssert.IsNotNull(copy, "copy");
                        MuxAssert.IsFalse(string.Equals(copy!.Id, "src", StringComparison.Ordinal), "new id");
                        MuxAssert.Contains("copy", copy.Title, "title marked copy");

                        SessionSnapshot? loaded = await store.LoadAsync(copy.Id, ct).ConfigureAwait(false);
                        MuxAssert.IsNotNull(loaded, "loaded copy");
                        MuxAssert.AreEqual("/proj", loaded!.WorkingDirectory, "working dir copied");
                        MuxAssert.AreEqual(1, loaded.Jobs.Count, "jobs copied");
                        MuxAssert.IsNotNull(await store.LoadAsync("src", ct).ConfigureAwait(false), "source preserved");
                    }),

                    Case("AddLabelDedupesAndPersists", "AddLabel normalizes, persists, and dedupes case-insensitively", async (SessionManager mgr, SessionStore store, CancellationToken ct) =>
                    {
                        await store.SaveAsync(new SessionSnapshot { Id = "s1", Title = "T" }, ct).ConfigureAwait(false);
                        SessionInfo? one = await mgr.AddLabelAsync("s1", "  WIP  ", ct).ConfigureAwait(false);
                        MuxAssert.IsNotNull(one, "added");
                        MuxAssert.AreEqual(1, one!.Labels.Count, "one label");
                        MuxAssert.AreEqual("WIP", one.Labels[0], "normalized (whitespace collapsed, case preserved)");

                        // Case-insensitive duplicate is a no-op (still one label).
                        SessionInfo? two = await mgr.AddLabelAsync("s1", "wip", ct).ConfigureAwait(false);
                        MuxAssert.AreEqual(1, two!.Labels.Count, "deduped");

                        SessionSnapshot? loaded = await store.LoadAsync("s1", ct).ConfigureAwait(false);
                        MuxAssert.AreEqual(1, loaded!.Labels.Count, "persisted one label");
                    }),

                    Case("RemoveLabelNoOpWhenAbsent", "RemoveLabel removes a present label and is a no-op otherwise", async (SessionManager mgr, SessionStore store, CancellationToken ct) =>
                    {
                        await store.SaveAsync(new SessionSnapshot { Id = "s1", Title = "T", Labels = new List<string> { "wip" } }, ct).ConfigureAwait(false);
                        SessionInfo? removed = await mgr.RemoveLabelAsync("s1", "WIP", ct).ConfigureAwait(false);
                        MuxAssert.AreEqual(0, removed!.Labels.Count, "removed case-insensitively");

                        SessionInfo? again = await mgr.RemoveLabelAsync("s1", "nope", ct).ConfigureAwait(false);
                        MuxAssert.AreEqual(0, again!.Labels.Count, "no-op when absent");
                    }),

                    Case("SetTagUpsertsByKey", "SetTag normalizes the key and upserts by key", async (SessionManager mgr, SessionStore store, CancellationToken ct) =>
                    {
                        await store.SaveAsync(new SessionSnapshot { Id = "s1", Title = "T" }, ct).ConfigureAwait(false);
                        SessionInfo? first = await mgr.SetTagAsync("s1", "Env", "prod", ct).ConfigureAwait(false);
                        MuxAssert.AreEqual(1, first!.Tags.Count, "one tag");
                        MuxAssert.AreEqual("env", first.Tags[0].Key, "key normalized to lowercase");
                        MuxAssert.AreEqual("prod", first.Tags[0].Value, "value");

                        // Same (normalized) key upserts the value rather than adding a second tag.
                        SessionInfo? second = await mgr.SetTagAsync("s1", "ENV", "staging", ct).ConfigureAwait(false);
                        MuxAssert.AreEqual(1, second!.Tags.Count, "upserted, not appended");
                        MuxAssert.AreEqual("staging", second.Tags[0].Value, "value replaced");
                    }),

                    Case("RemoveTagByKey", "RemoveTag removes by normalized key", async (SessionManager mgr, SessionStore store, CancellationToken ct) =>
                    {
                        await store.SaveAsync(new SessionSnapshot { Id = "s1", Title = "T", Tags = new List<SessionTag> { new SessionTag("env", "prod") } }, ct).ConfigureAwait(false);
                        SessionInfo? removed = await mgr.RemoveTagAsync("s1", "ENV", ct).ConfigureAwait(false);
                        MuxAssert.AreEqual(0, removed!.Tags.Count, "removed by key");
                    }),

                    Case("InvalidLabelThrows", "AddLabel rejects an empty/whitespace label", async (SessionManager mgr, SessionStore store, CancellationToken ct) =>
                    {
                        await store.SaveAsync(new SessionSnapshot { Id = "s1", Title = "T" }, ct).ConfigureAwait(false);
                        bool threw = false;
                        try { await mgr.AddLabelAsync("s1", "   ", ct).ConfigureAwait(false); }
                        catch (ArgumentException) { threw = true; }
                        MuxAssert.IsTrue(threw, "empty label throws ArgumentException");
                    }),

                    Case("MutateMissingSessionReturnsNull", "Metadata mutations on a missing session return null", async (SessionManager mgr, SessionStore store, CancellationToken ct) =>
                    {
                        MuxAssert.IsNull(await mgr.AddLabelAsync("nope", "wip", ct).ConfigureAwait(false), "add label null");
                        MuxAssert.IsNull(await mgr.SetTagAsync("nope", "env", "prod", ct).ConfigureAwait(false), "set tag null");
                    }),

                    Case("DuplicateCopiesLabelsAndTags", "Duplicate deep-copies labels and tags", async (SessionManager mgr, SessionStore store, CancellationToken ct) =>
                    {
                        await store.SaveAsync(new SessionSnapshot
                        {
                            Id = "src",
                            Title = "Original",
                            Labels = new List<string> { "wip" },
                            Tags = new List<SessionTag> { new SessionTag("env", "prod") }
                        }, ct).ConfigureAwait(false);

                        SessionInfo? copy = await mgr.DuplicateAsync("src", ct).ConfigureAwait(false);
                        MuxAssert.AreEqual(1, copy!.Labels.Count, "labels copied");
                        MuxAssert.AreEqual(1, copy.Tags.Count, "tags copied");

                        // Mutating the copy must not affect the source (deep copy).
                        await mgr.RemoveLabelAsync(copy.Id, "wip", ct).ConfigureAwait(false);
                        SessionSnapshot? src = await store.LoadAsync("src", ct).ConfigureAwait(false);
                        MuxAssert.AreEqual(1, src!.Labels.Count, "source label untouched");
                    }),

                    Case("DeleteRemoves", "Delete removes the session", async (SessionManager mgr, SessionStore store, CancellationToken ct) =>
                    {
                        await store.SaveAsync(new SessionSnapshot { Id = "s1", Title = "T" }, ct).ConfigureAwait(false);
                        MuxAssert.IsTrue(await mgr.DeleteAsync("s1", ct).ConfigureAwait(false), "deleted");
                        MuxAssert.IsFalse(await mgr.DeleteAsync("s1", ct).ConfigureAwait(false), "missing false");
                    }),

                    Case("ExportRendersMarkdown", "Export renders Markdown, or null for a missing session", async (SessionManager mgr, SessionStore store, CancellationToken ct) =>
                    {
                        await store.SaveAsync(new SessionSnapshot
                        {
                            Id = "s1",
                            Title = "Exported",
                            ConversationHistory = new List<ConversationMessage> { new ConversationMessage { Role = RoleEnum.User, Content = "hello world" } }
                        }, ct).ConfigureAwait(false);

                        string? md = await mgr.ExportAsync("s1", "md", ct).ConfigureAwait(false);
                        MuxAssert.IsNotNull(md, "rendered");
                        MuxAssert.Contains("hello world", md!, "content present");

                        string? missing = await mgr.ExportAsync("nope", "md", ct).ConfigureAwait(false);
                        MuxAssert.IsNull(missing, "missing export null");
                    })
                });
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, Func<SessionManager, SessionStore, CancellationToken, Task> body)
        {
            return new TestCaseDescriptor("SessionManager", caseId, displayName, async (CancellationToken ct) =>
            {
                string dir = Path.Combine(Path.GetTempPath(), "mux_sessmgr_" + Guid.NewGuid().ToString("N"));
                try
                {
                    SessionStore store = new SessionStore(dir);
                    await body(new SessionManager(store), store, ct).ConfigureAwait(false);
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
