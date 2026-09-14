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
