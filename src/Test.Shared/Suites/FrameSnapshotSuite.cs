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
    using Mux.Core.Models;
    using Touchstone.Core;
    using TUIKit.Terminal;

    /// <summary>
    /// Touchstone suite for M13 headless frame rendering: renders shell regions to a deterministic text
    /// grid (via TUIKit's own render path) at representative sizes and states, plus full-pipeline
    /// <c>RenderOnce</c> smoke (idle and with a modal). Positive, determinism, and negative/edge cases.
    /// </summary>
    public static class FrameSnapshotSuite
    {
        private const string SuiteId = "FrameSnapshot";

        /// <summary>
        /// Builds the frame-snapshot suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/> for frame-render cases.</returns>
        public static TestSuiteDescriptor Create()
        {
            return new TestSuiteDescriptor(
                SuiteId,
                "Headless frame rendering, determinism, and graceful sizing",
                new List<TestCaseDescriptor>
                {
                    Case("IdleTranscriptRenders", "The idle transcript renders the header", async (CancellationToken ct) =>
                    {
                        await using (JobManager manager = NewManager())
                        using (MuxTuiApp app = NewApp(out _, manager))
                        {
                            await Task.CompletedTask.ConfigureAwait(false);
                            string frame = app.RenderRegion("transcript", 80, 10);
                            MuxAssert.Contains("mux", frame, "header brand rendered");
                            // The prompt guidance lives in the footer hint, not the transcript header.
                            MuxAssert.Contains("Type a prompt", app.RenderRegion("footer", 120, 2), "footer hint rendered");
                        }
                    }),

                    Case("IdleFooterRenders", "The idle footer renders the key hints", async (CancellationToken ct) =>
                    {
                        await using (JobManager manager = NewManager())
                        using (MuxTuiApp app = NewApp(out _, manager))
                        {
                            await Task.CompletedTask.ConfigureAwait(false);
                            // The footer is two rows: a blank spacer above the hint line, so the transcript
                            // never butts into the prompt area.
                            string footer = app.RenderRegion("footer", 120, 2);
                            string[] rows = footer.Replace("\r\n", "\n").Split('\n');
                            MuxAssert.AreEqual(string.Empty, rows[0].Trim(), "blank spacer above the hint");
                            MuxAssert.Contains("CTRL+J/newline", rows[1], "hint on the second row");
                            MuxAssert.Contains("CTRL-Q/quit", rows[1], "quit hint on the second row");
                        }
                    }),

                    Case("RenderIsDeterministic", "Rendering the same state twice is identical", async (CancellationToken ct) =>
                    {
                        await using (JobManager manager = NewManager())
                        using (MuxTuiApp app = NewApp(out _, manager))
                        {
                            await Task.CompletedTask.ConfigureAwait(false);
                            string first = app.RenderRegion("transcript", 80, 12);
                            string second = app.RenderRegion("transcript", 80, 12);
                            MuxAssert.AreEqual(first, second, "deterministic frame");
                        }
                    }),

                    Case("CompletedJobTranscriptRenders", "A completed job's transcript renders the exchange", async (CancellationToken ct) =>
                    {
                        await using (JobManager manager = NewManager())
                        using (MuxTuiApp app = NewApp(out HeadlessBackend backend, manager))
                        {
                            Submit(backend, app, "hello");
                            await app.DrainProjectorsAsync().ConfigureAwait(false);
                            string frame = app.RenderRegion("transcript", 80, 12);
                            MuxAssert.Contains("hello", frame, "prompt rendered");
                            MuxAssert.Contains("Echo: hello", frame, "assistant rendered");
                        }
                    }),

                    Case("RunningStatusRendersInSidebar", "The sidebar renders a running status while a turn is active", async (CancellationToken ct) =>
                    {
                        TaskCompletionSource<bool> release = Signal();
                        await using (JobManager manager = new JobManager(Gated(release), maxConcurrency: 1))
                        using (MuxTuiApp app = NewApp(out HeadlessBackend backend, manager))
                        {
                            Submit(backend, app, "work");
                            await WaitUntilAsync(() => Join(app.SidebarSnapshot()).Contains("running", StringComparison.Ordinal), ct).ConfigureAwait(false);

                            string frame = app.RenderRegion("sidebar", 30, 20);
                            MuxAssert.Contains("STATUS", frame, "status header rendered");
                            MuxAssert.Contains("running", frame, "running status rendered");
                            release.TrySetResult(true);
                        }
                    }),

                    Case("SessionTelemetryRendersInSidebar", "The sidebar renders session telemetry after a turn", async (CancellationToken ct) =>
                    {
                        await using (JobManager manager = NewManager())
                        using (MuxTuiApp app = NewApp(out HeadlessBackend backend, manager))
                        {
                            Submit(backend, app, "one");
                            await app.DrainProjectorsAsync().ConfigureAwait(false);

                            string frame = app.RenderRegion("sidebar", 30, 14);
                            MuxAssert.Contains("SESSION", frame, "session header rendered");
                            MuxAssert.Contains("Turns", frame, "turns row rendered");
                            MuxAssert.Contains("idle", frame, "idle after the turn");
                        }
                    }),

                    Case("ComposerRendersTypedText", "The composer renders in-progress text", async (CancellationToken ct) =>
                    {
                        await using (JobManager manager = NewManager())
                        using (MuxTuiApp app = NewApp(out HeadlessBackend backend, manager))
                        {
                            await Task.CompletedTask.ConfigureAwait(false);
                            backend.FeedInput("draft prompt");
                            app.PumpInputOnce();
                            MuxAssert.Contains("draft", app.RenderRegion("composer", 40, 3), "composer text rendered");
                        }
                    }),

                    // ---- Negative / edge ----
                    Case("TinySizeDoesNotThrow", "Rendering into a tiny grid does not throw", async (CancellationToken ct) =>
                    {
                        await using (JobManager manager = NewManager())
                        using (MuxTuiApp app = NewApp(out _, manager))
                        {
                            await Task.CompletedTask.ConfigureAwait(false);
                            string tiny = app.RenderRegion("transcript", 3, 2);
                            string oneByOne = app.RenderRegion("sidebar", 1, 1);
                            MuxAssert.IsNotNull(tiny, "tiny transcript rendered");
                            MuxAssert.IsNotNull(oneByOne, "1x1 sidebar rendered");
                        }
                    }),

                    Case("UnknownRegionOrBadSizeReturnsEmpty", "Unknown region or non-positive size returns empty", async (CancellationToken ct) =>
                    {
                        await using (JobManager manager = NewManager())
                        using (MuxTuiApp app = NewApp(out _, manager))
                        {
                            await Task.CompletedTask.ConfigureAwait(false);
                            MuxAssert.AreEqual(string.Empty, app.RenderRegion("nope", 80, 10), "unknown region empty");
                            MuxAssert.AreEqual(string.Empty, app.RenderRegion("transcript", 0, 10), "zero width empty");
                            MuxAssert.AreEqual(string.Empty, app.RenderRegion("transcript", 80, -1), "negative height empty");
                        }
                    }),

                    // ---- Full-pipeline RenderOnce smoke ----
                    Case("FullFrameRenderOnceIdle", "The full render pipeline emits output at idle", async (CancellationToken ct) =>
                    {
                        HeadlessBackend backend = new HeadlessBackend(120, 30);
                        await using (JobManager manager = NewManager())
                        using (MuxTuiApp app = new MuxTuiApp(backend, manager, "demo", ApprovalPolicyEnum.AutoApprove))
                        {
                            await Task.CompletedTask.ConfigureAwait(false);
                            app.Start();
                            app.RenderOnce();
                            MuxAssert.IsTrue(backend.PeekOutput().Length > 0, "idle frame emitted");
                        }
                    }),

                    Case("FullFrameRenderOnceWithModal", "The full pipeline renders with a modal active", async (CancellationToken ct) =>
                    {
                        HeadlessBackend backend = new HeadlessBackend(120, 30);
                        await using (JobManager manager = NewManager())
                        using (MuxTuiApp app = new MuxTuiApp(backend, manager, "demo", ApprovalPolicyEnum.AutoApprove))
                        {
                            app.Start();
                            Task<string> approval = app.RequestApprovalAsync(new ToolCall { Id = "t1", Name = "write_file", Arguments = "{}" });
                            MuxAssert.IsTrue(app.IsModalActive, "modal active");

                            app.RenderOnce();
                            MuxAssert.IsTrue(backend.PeekOutput().Length > 0, "frame with modal emitted");

                            backend.FeedInput("\r"); // approve to release the pending task
                            app.PumpInputOnce();
                            MuxAssert.AreEqual("y", await approval.ConfigureAwait(false), "approval resolved");
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
            backend = new HeadlessBackend(120, 30);
            MuxTuiApp app = new MuxTuiApp(backend, manager, "demo", ApprovalPolicyEnum.AutoApprove);
            return app;
        }

        private static void Submit(HeadlessBackend backend, MuxTuiApp app, string prompt)
        {
            backend.FeedInput(prompt + "\r");
            app.PumpInputOnce();
        }

        private static Func<Job, string, CancellationToken, IAsyncEnumerable<AgentEvent>> Gated(TaskCompletionSource<bool> release)
        {
            return (Job job, string prompt, CancellationToken ct) => GatedStream(release, ct);
        }

        private static async IAsyncEnumerable<AgentEvent> GatedStream(TaskCompletionSource<bool> release, [EnumeratorCancellation] CancellationToken ct)
        {
            await release.Task.WaitAsync(ct).ConfigureAwait(false);
            yield return new RunCompletedEvent { RunId = Guid.NewGuid().ToString("N"), Status = "completed", IterationsCompleted = 1, DurationMs = 1 };
        }

        private static async IAsyncEnumerable<AgentEvent> EchoRunner(Job job, string prompt, [EnumeratorCancellation] CancellationToken ct)
        {
            await Task.CompletedTask.ConfigureAwait(false);
            yield return new AssistantTextEvent { Text = "Echo: " + prompt };
            yield return new RunCompletedEvent { RunId = Guid.NewGuid().ToString("N"), Status = "completed", IterationsCompleted = 1, DurationMs = 1 };
        }

        private static TaskCompletionSource<bool> Signal()
        {
            return new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
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

        #endregion
    }
}
