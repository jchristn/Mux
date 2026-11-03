namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Sessions;
    using Mux.Desktop.Services;
    using Mux.Desktop.Conversation;
    using Touchstone.Core;

    /// <summary>
    /// Touchstone suite for <see cref="ThreadService"/> over a temporary session store: create, list, rename
    /// (with pin), duplicate, delete, and export — including the negative paths (missing thread, unknown
    /// export format). Each case creates and cleans up its own temporary directory.
    /// </summary>
    public static class ThreadServiceSuite
    {
        /// <summary>
        /// Builds the thread-service suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/> for the thread-service cases.</returns>
        public static TestSuiteDescriptor Create()
        {
            return new TestSuiteDescriptor(
                "ThreadService",
                "Conversation/thread management",
                new List<TestCaseDescriptor>
                {
                    new TestCaseDescriptor("ThreadService", "CreateAndList", "Create pins a supplied title and appears in the list", async (CancellationToken ct) =>
                    {
                        string dir = NewDir();
                        try
                        {
                            ThreadService service = new ThreadService(new SessionStore(dir));
                            ThreadSummary created = await service.CreateAsync("My thread", "ep", "model", ct);
                            MuxAssert.AreEqual("My thread", created.Title, "created title");
                            MuxAssert.IsTrue(created.TitlePinned, "title pinned");
                            SessionSnapshot? saved = await new SessionStore(dir).LoadAsync(created.Id, ct);
                            MuxAssert.AreEqual(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), saved!.WorkingDirectory, "new desktop sessions persist home instead of the launcher directory");

                            IReadOnlyList<ThreadSummary> list = await service.ListAsync(ct);
                            MuxAssert.AreEqual(1, list.Count, "list count");
                        }
                        finally
                        {
                            Cleanup(dir);
                        }
                    }),

                    new TestCaseDescriptor("ThreadService", "RunnerDefaultsToHome", "Desktop tools default to home and respect an explicit working directory", (CancellationToken ct) =>
                    {
                        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                        AgentLoopTurnRunner runner = new AgentLoopTurnRunner(Path.GetTempPath(), _ => Task.FromResult("n"), null);
                        MuxAssert.AreEqual(home, runner.WorkingDirectory, "runner defaults to home");
                        runner.WorkingDirectory = Path.GetTempPath();
                        MuxAssert.AreEqual(Path.GetTempPath(), runner.WorkingDirectory, "explicit directory preserved");
                        runner.WorkingDirectory = " ";
                        MuxAssert.AreEqual(home, runner.WorkingDirectory, "blank resets to home");
                        runner.WorkingDirectory = Path.GetPathRoot(home)!;
                        MuxAssert.AreEqual(Path.GetPathRoot(home), runner.WorkingDirectory, "explicit root remains supported");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("ThreadService", "DefaultTitleUnpinned", "Create without a title uses the default and leaves it unpinned", async (CancellationToken ct) =>
                    {
                        string dir = NewDir();
                        try
                        {
                            ThreadService service = new ThreadService(new SessionStore(dir));
                            ThreadSummary created = await service.CreateAsync(null, null, null, ct);
                            MuxAssert.AreEqual(SessionTitleHelper.DefaultTitle, created.Title, "default title");
                            MuxAssert.IsFalse(created.TitlePinned, "not pinned");
                        }
                        finally
                        {
                            Cleanup(dir);
                        }
                    }),

                    new TestCaseDescriptor("ThreadService", "RenameNormalizesAndPins", "Rename normalizes and pins; missing thread returns null", async (CancellationToken ct) =>
                    {
                        string dir = NewDir();
                        try
                        {
                            ThreadService service = new ThreadService(new SessionStore(dir));
                            ThreadSummary created = await service.CreateAsync(null, null, null, ct);

                            ThreadSummary? renamed = await service.RenameAsync(created.Id, "Renamed.", ct);
                            MuxAssert.IsNotNull(renamed, "rename result");
                            MuxAssert.AreEqual("Renamed", renamed!.Title, "normalized title");
                            MuxAssert.IsTrue(renamed.TitlePinned, "pinned after rename");

                            ThreadSummary? missing = await service.RenameAsync("deadbeefdeadbeef", "x", ct);
                            MuxAssert.IsNull(missing, "rename missing returns null");
                        }
                        finally
                        {
                            Cleanup(dir);
                        }
                    }),

                    new TestCaseDescriptor("ThreadService", "DuplicateCreatesCopy", "Duplicate creates a distinct copy titled '(copy)'", async (CancellationToken ct) =>
                    {
                        string dir = NewDir();
                        try
                        {
                            ThreadService service = new ThreadService(new SessionStore(dir));
                            ThreadSummary created = await service.CreateAsync("Original", null, null, ct);

                            ThreadSummary? copy = await service.DuplicateAsync(created.Id, ct);
                            MuxAssert.IsNotNull(copy, "duplicate result");
                            MuxAssert.Contains("(copy)", copy!.Title, "copy title");
                            MuxAssert.AreNotEqual(created.Id, copy.Id, "distinct id");

                            IReadOnlyList<ThreadSummary> list = await service.ListAsync(ct);
                            MuxAssert.AreEqual(2, list.Count, "two threads");
                        }
                        finally
                        {
                            Cleanup(dir);
                        }
                    }),

                    new TestCaseDescriptor("ThreadService", "DeleteIsIdempotent", "Delete removes once then reports false", async (CancellationToken ct) =>
                    {
                        string dir = NewDir();
                        try
                        {
                            ThreadService service = new ThreadService(new SessionStore(dir));
                            ThreadSummary created = await service.CreateAsync(null, null, null, ct);

                            MuxAssert.IsTrue(await service.DeleteAsync(created.Id, ct), "first delete");
                            MuxAssert.IsFalse(await service.DeleteAsync(created.Id, ct), "second delete");
                        }
                        finally
                        {
                            Cleanup(dir);
                        }
                    }),

                    new TestCaseDescriptor("ThreadService", "ExportAndBadFormat", "Export renders markdown; an unknown format throws", async (CancellationToken ct) =>
                    {
                        string dir = NewDir();
                        try
                        {
                            ThreadService service = new ThreadService(new SessionStore(dir));
                            ThreadSummary created = await service.CreateAsync("Exportable", null, null, ct);

                            string? markdown = await service.ExportAsync(created.Id, "md", ct);
                            MuxAssert.IsNotNull(markdown, "markdown export");
                            MuxAssert.Contains("Exportable", markdown!, "title in export");

                            await MuxAssert.ThrowsAsync<ArgumentException>(
                                async () => await service.ExportAsync(created.Id, "pdf", ct),
                                "unknown export format");

                            string? missing = await service.ExportAsync("deadbeefdeadbeef", "md", ct);
                            MuxAssert.IsNull(missing, "export missing returns null");
                        }
                        finally
                        {
                            Cleanup(dir);
                        }
                    })
                });
        }

        private static string NewDir()
        {
            string dir = Path.Combine(Path.GetTempPath(), "mux-desktop-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        private static void Cleanup(string dir)
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
                // Best-effort cleanup.
            }
        }
    }
}
