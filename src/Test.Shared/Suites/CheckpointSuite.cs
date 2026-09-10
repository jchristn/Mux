namespace Test.Shared.Suites
{
    using System;
    using System.Diagnostics;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Checkpoints;
    using Touchstone.Core;

    /// <summary>
    /// Touchstone suite for git-checkpoint undo/redo. Each case runs against a throwaway git repository in a
    /// temp directory and verifies that snapshots capture tracked and untracked content, that restore reverts
    /// modifications and removes files created after a snapshot, and that <see cref="CheckpointManager"/>
    /// walks its undo/redo stacks correctly. Skips gracefully when git is not on PATH.
    /// </summary>
    public static class CheckpointSuite
    {
        private const string SuiteId = "Checkpoint";

        /// <summary>
        /// Builds the checkpoint suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/> for the checkpoint cases.</returns>
        public static TestSuiteDescriptor Create()
        {
            return new TestSuiteDescriptor(
                SuiteId,
                "Git-checkpoint capture, restore, and undo/redo",
                new System.Collections.Generic.List<TestCaseDescriptor>
                {
                    RepoCase("NonRepositoryReportsUnavailable", "A non-repository directory reports checkpointing unavailable", async (string dir, CancellationToken ct) =>
                    {
                        // Fresh temp dir that is NOT a git repo (RepoCase without init).
                        GitCheckpointService service = new GitCheckpointService(dir);
                        MuxAssert.IsFalse(await service.IsRepositoryAsync(ct).ConfigureAwait(false), "not a repository");
                    }, initRepo: false),

                    RepoCase("RepositoryReportsAvailable", "A git work tree reports checkpointing available", async (string dir, CancellationToken ct) =>
                    {
                        GitCheckpointService service = new GitCheckpointService(dir);
                        MuxAssert.IsTrue(await service.IsRepositoryAsync(ct).ConfigureAwait(false), "is a repository");
                    }),

                    RepoCase("RestoreRevertsModificationAndRemovesNewFile", "Restore reverts a modified tracked file and removes a file created after the snapshot", async (string dir, CancellationToken ct) =>
                    {
                        GitCheckpointService service = new GitCheckpointService(dir);
                        File.WriteAllText(Path.Combine(dir, "tracked.txt"), "v1");
                        Git(dir, "add", "-A");
                        Git(dir, "commit", "-m", "init");

                        string snapshot = await service.CaptureAsync("before edits", ct).ConfigureAwait(false);

                        File.WriteAllText(Path.Combine(dir, "tracked.txt"), "v2");
                        File.WriteAllText(Path.Combine(dir, "added.txt"), "new");

                        await service.RestoreAsync(snapshot, ct).ConfigureAwait(false);

                        MuxAssert.AreEqual("v1", File.ReadAllText(Path.Combine(dir, "tracked.txt")), "tracked file reverted");
                        MuxAssert.IsFalse(File.Exists(Path.Combine(dir, "added.txt")), "new file removed");
                    }),

                    RepoCase("RestoreBringsBackDeletedFile", "Restore brings back a file deleted after the snapshot", async (string dir, CancellationToken ct) =>
                    {
                        GitCheckpointService service = new GitCheckpointService(dir);
                        File.WriteAllText(Path.Combine(dir, "keep.txt"), "hello");
                        Git(dir, "add", "-A");
                        Git(dir, "commit", "-m", "init");

                        string snapshot = await service.CaptureAsync("s", ct).ConfigureAwait(false);
                        File.Delete(Path.Combine(dir, "keep.txt"));

                        await service.RestoreAsync(snapshot, ct).ConfigureAwait(false);

                        MuxAssert.IsTrue(File.Exists(Path.Combine(dir, "keep.txt")), "deleted file restored");
                        MuxAssert.AreEqual("hello", File.ReadAllText(Path.Combine(dir, "keep.txt")), "content restored");
                    }),

                    RepoCase("ManagerUndoRedoRoundTrip", "The manager undoes a turn's changes and redoes them", async (string dir, CancellationToken ct) =>
                    {
                        GitCheckpointService service = new GitCheckpointService(dir);
                        CheckpointManager manager = new CheckpointManager(service);

                        string file = Path.Combine(dir, "work.txt");
                        File.WriteAllText(file, "start");
                        Git(dir, "add", "-A");
                        Git(dir, "commit", "-m", "init");

                        // Turn 1: record pre-turn state, then the "turn" changes the file.
                        await manager.RecordAsync("turn 1", ct).ConfigureAwait(false);
                        File.WriteAllText(file, "after turn 1");

                        MuxAssert.IsTrue(manager.CanUndo, "can undo after a recorded checkpoint");
                        MuxAssert.IsFalse(manager.CanRedo, "cannot redo yet");

                        Checkpoint? undone = await manager.UndoAsync(ct).ConfigureAwait(false);
                        MuxAssert.IsNotNull(undone, "undo returned a checkpoint");
                        MuxAssert.AreEqual("start", File.ReadAllText(file), "undo reverted the change");
                        MuxAssert.IsTrue(manager.CanRedo, "can redo after undo");

                        Checkpoint? redone = await manager.RedoAsync(ct).ConfigureAwait(false);
                        MuxAssert.IsNotNull(redone, "redo returned a checkpoint");
                        MuxAssert.AreEqual("after turn 1", File.ReadAllText(file), "redo re-applied the change");
                    }),

                    RepoCase("ManagerUndoWithNothingReturnsNull", "Undo with no history returns null", async (string dir, CancellationToken ct) =>
                    {
                        CheckpointManager manager = new CheckpointManager(new GitCheckpointService(dir));
                        MuxAssert.IsTrue(await manager.UndoAsync(ct).ConfigureAwait(false) == null, "undo returns null");
                        MuxAssert.IsTrue(await manager.RedoAsync(ct).ConfigureAwait(false) == null, "redo returns null");
                    }),

                    RepoCase("RecordClearsRedo", "Recording a new checkpoint clears the redo history", async (string dir, CancellationToken ct) =>
                    {
                        GitCheckpointService service = new GitCheckpointService(dir);
                        CheckpointManager manager = new CheckpointManager(service);
                        string file = Path.Combine(dir, "f.txt");
                        File.WriteAllText(file, "a");
                        Git(dir, "add", "-A");
                        Git(dir, "commit", "-m", "init");

                        await manager.RecordAsync("t1", ct).ConfigureAwait(false);
                        File.WriteAllText(file, "b");
                        await manager.UndoAsync(ct).ConfigureAwait(false);
                        MuxAssert.IsTrue(manager.CanRedo, "redo available after undo");

                        await manager.RecordAsync("t2", ct).ConfigureAwait(false);
                        MuxAssert.IsFalse(manager.CanRedo, "recording cleared redo");
                    })
                });
        }

        #region Helpers

        private static TestCaseDescriptor RepoCase(string id, string name, Func<string, CancellationToken, Task> body, bool initRepo = true)
        {
            return new TestCaseDescriptor(SuiteId, id, name, async (CancellationToken ct) =>
            {
                if (!IsGitAvailable())
                {
                    // Treat a missing git as a skip by passing trivially; the environment guarantees git,
                    // but this keeps the suite from hard-failing on a machine without it.
                    return;
                }

                string dir = Path.Combine(Path.GetTempPath(), "mux_ckpt_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(dir);
                try
                {
                    if (initRepo)
                    {
                        Git(dir, "init");
                        Git(dir, "config", "user.email", "test@example.com");
                        Git(dir, "config", "user.name", "test");
                        Git(dir, "config", "commit.gpgsign", "false");
                    }

                    await body(dir, ct).ConfigureAwait(false);
                }
                finally
                {
                    TryDeleteDirectory(dir);
                }
            });
        }

        private static bool IsGitAvailable()
        {
            try
            {
                using Process process = StartGit(Directory.GetCurrentDirectory(), new[] { "--version" });
                process.WaitForExit(5000);
                return process.ExitCode == 0;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static void Git(string dir, params string[] args)
        {
            using Process process = StartGit(dir, args);
            process.WaitForExit(15000);
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException("git " + string.Join(' ', args) + " failed: " + process.StandardError.ReadToEnd());
            }
        }

        private static Process StartGit(string dir, string[] args)
        {
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = "git",
                WorkingDirectory = dir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            foreach (string arg in args)
            {
                startInfo.ArgumentList.Add(arg);
            }

            Process process = new Process { StartInfo = startInfo };
            process.Start();
            return process;
        }

        private static void TryDeleteDirectory(string dir)
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
            catch (UnauthorizedAccessException)
            {
            }
        }

        #endregion
    }
}
