namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Agent;
    using Mux.Core.Runs;
    using Touchstone.Core;

    /// <summary>
    /// Touchstone suite for <see cref="RunRegistry"/> and <see cref="RunHandle"/> — the server-side run
    /// lifecycle backing cancel, state inspection, and the WebSocket event bridge. Covers create/lookup,
    /// status transitions, cancellation semantics, subscription replay + live tail, eviction, and negative
    /// cases (unknown ids, double-cancel, concurrency).
    /// </summary>
    public static class RunRegistrySuite
    {
        /// <summary>
        /// Builds the run-registry suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/> for the run-registry cases.</returns>
        public static TestSuiteDescriptor Create()
        {
            return new TestSuiteDescriptor(
                "RunRegistry",
                "Server run lifecycle registry and handle",
                new List<TestCaseDescriptor>
                {
                    new TestCaseDescriptor("RunRegistry", "CreateAndLookup", "A created run is retrievable and starts Running", (CancellationToken ct) =>
                    {
                        using RunRegistry registry = new RunRegistry();
                        RunHandle handle = registry.Create("run-1", "sess-1", "local", "m", ct);
                        MuxAssert.IsTrue(registry.TryGet("run-1", out RunHandle? found), "TryGet finds the run");
                        MuxAssert.IsNotNull(found, "found handle not null");
                        MuxAssert.AreEqual(RunStatusEnum.Running, handle.Status, "starts Running");
                        MuxAssert.AreEqual("sess-1", handle.SessionId, "session id retained");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("RunRegistry", "RunCompletedTransitionsToCompleted", "Applying a run_completed event transitions status and marks terminal", (CancellationToken ct) =>
                    {
                        using RunRegistry registry = new RunRegistry();
                        RunHandle handle = registry.Create("run-2", string.Empty, "local", "m", ct);
                        handle.ApplyEvent(new RunCompletedEvent { RunId = "run-2", Status = "completed", IterationsCompleted = 3, InputTokens = 10, OutputTokens = 5 });
                        MuxAssert.AreEqual(RunStatusEnum.Completed, handle.Status, "status Completed");
                        MuxAssert.IsTrue(handle.IsTerminal, "IsTerminal");
                        MuxAssert.AreEqual(3, handle.IterationsCompleted, "iterations captured");
                        MuxAssert.IsNotNull(handle.CompletedUtc, "completed timestamp set");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("RunRegistry", "CancelRequestsTokenOnce", "Cancel requests the token once and is idempotent thereafter", (CancellationToken ct) =>
                    {
                        using RunRegistry registry = new RunRegistry();
                        RunHandle handle = registry.Create("run-3", string.Empty, "local", "m", ct);
                        MuxAssert.IsTrue(registry.TryCancel("run-3"), "first cancel returns true");
                        MuxAssert.IsTrue(handle.Token.IsCancellationRequested, "token canceled");
                        MuxAssert.IsFalse(registry.TryCancel("run-3"), "second cancel returns false");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("RunRegistry", "CancelUnknownAndCompletedReturnFalse", "Canceling an unknown or already-terminal run returns false", (CancellationToken ct) =>
                    {
                        using RunRegistry registry = new RunRegistry();
                        MuxAssert.IsFalse(registry.TryCancel("nope"), "unknown id false");
                        MuxAssert.IsFalse(registry.TryGet("nope", out RunHandle? missing), "unknown TryGet false");
                        MuxAssert.IsNull(missing, "unknown handle null");

                        RunHandle handle = registry.Create("run-4", string.Empty, "local", "m", ct);
                        handle.ApplyEvent(new RunCompletedEvent { RunId = "run-4", Status = "completed" });
                        MuxAssert.IsFalse(registry.TryCancel("run-4"), "cancel of terminal run false");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("RunRegistry", "SubscriberReceivesLiveEvents", "A subscriber receives events applied after it subscribes", async (CancellationToken ct) =>
                    {
                        using RunRegistry registry = new RunRegistry();
                        RunHandle handle = registry.Create("run-5", string.Empty, "local", "m", ct);
                        RunSubscription sub = handle.Subscribe();
                        MuxAssert.AreEqual(0, sub.Replay.Count, "no replay before events");

                        handle.ApplyEvent(new AssistantTextEvent { Text = "hello" });
                        string received = await ReadOneAsync(sub, ct).ConfigureAwait(false);
                        MuxAssert.Contains("assistant_text", received, "frame carries the event type");
                        MuxAssert.Contains("hello", received, "frame carries the text");
                        handle.Unsubscribe(sub);
                    }),

                    new TestCaseDescriptor("RunRegistry", "LateSubscriberReplaysThenTails", "A late subscriber replays prior events, then tails live ones, and completes on terminal", async (CancellationToken ct) =>
                    {
                        using RunRegistry registry = new RunRegistry();
                        RunHandle handle = registry.Create("run-6", string.Empty, "local", "m", ct);
                        handle.ApplyEvent(new AssistantTextEvent { Text = "first" });

                        RunSubscription sub = handle.Subscribe();
                        MuxAssert.AreEqual(1, sub.Replay.Count, "replay has the earlier event");

                        handle.ApplyEvent(new AssistantTextEvent { Text = "second" });
                        string tailed = await ReadOneAsync(sub, ct).ConfigureAwait(false);
                        MuxAssert.Contains("second", tailed, "tailed the live event");

                        handle.ApplyEvent(new RunCompletedEvent { RunId = "run-6", Status = "completed" });

                        // Drain remaining buffered frames (including the terminal run_completed); the reader's
                        // Completion transitions only once the writer is completed and the buffer is empty.
                        bool sawCompleted = false;
                        using CancellationTokenSource drainTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                        drainTimeout.CancelAfter(TimeSpan.FromSeconds(2));
                        while (await sub.Reader.WaitToReadAsync(drainTimeout.Token).ConfigureAwait(false))
                        {
                            while (sub.Reader.TryRead(out string? drained))
                            {
                                if (drained != null && drained.Contains("run_completed")) sawCompleted = true;
                            }
                        }

                        MuxAssert.IsTrue(sawCompleted, "terminal run_completed observed");
                        MuxAssert.IsTrue(sub.Reader.Completion.IsCompleted, "reader completes on terminal");
                    }),

                    new TestCaseDescriptor("RunRegistry", "GetOrCreateRelaysPublishedEnvelopes", "GetOrCreate materializes a run and ApplyEnvelope relays external frames verbatim to subscribers", async (CancellationToken ct) =>
                    {
                        using RunRegistry registry = new RunRegistry();
                        RunHandle handle = registry.GetOrCreate("ext-1", "sess-ext", "local", "m");
                        MuxAssert.IsTrue(registry.TryGet("ext-1", out _), "GetOrCreate registers the run");
                        MuxAssert.IsTrue(ReferenceEquals(handle, registry.GetOrCreate("ext-1", "sess-ext", "local", "m")), "GetOrCreate returns the same handle");

                        RunSubscription sub = handle.Subscribe();
                        handle.ApplyEnvelope("{\"eventType\":\"assistant_text\",\"text\":\"from another process\"}");
                        string frame = await ReadOneAsync(sub, ct).ConfigureAwait(false);
                        MuxAssert.Contains("from another process", frame, "published frame relayed verbatim");

                        handle.ApplyEnvelope("{\"eventType\":\"run_completed\",\"status\":\"completed\"}");
                        MuxAssert.IsTrue(handle.IsTerminal, "run_completed envelope marks terminal");
                    }),

                    new TestCaseDescriptor("RunRegistry", "TerminalRunEvictedAfterRetention", "A terminal run is evicted after the retention window elapses", async (CancellationToken ct) =>
                    {
                        using RunRegistry registry = new RunRegistry();
                        registry.RetentionWindow = TimeSpan.FromSeconds(1);
                        RunHandle handle = registry.Create("run-7", string.Empty, "local", "m", ct);
                        handle.ApplyEvent(new RunCompletedEvent { RunId = "run-7", Status = "completed" });

                        bool evicted = false;
                        for (int i = 0; i < 60; i++)
                        {
                            if (!registry.TryGet("run-7", out _))
                            {
                                evicted = true;
                                break;
                            }
                            await Task.Delay(100, ct).ConfigureAwait(false);
                        }

                        MuxAssert.IsTrue(evicted, "terminal run evicted within the retention window");
                    }),

                    new TestCaseDescriptor("RunRegistry", "ConcurrentCreateAndCancelIsSafe", "Concurrent create and cancel across many runs does not corrupt the registry", async (CancellationToken ct) =>
                    {
                        using RunRegistry registry = new RunRegistry();
                        List<Task> tasks = new List<Task>();
                        for (int i = 0; i < 50; i++)
                        {
                            string id = "run-c-" + i;
                            tasks.Add(Task.Run(() =>
                            {
                                registry.Create(id, string.Empty, "local", "m", CancellationToken.None);
                                registry.TryCancel(id);
                            }, ct));
                        }

                        await Task.WhenAll(tasks).ConfigureAwait(false);
                        MuxAssert.AreEqual(50, registry.List().Count, "all 50 runs tracked");
                    })
                });
        }

        private static async Task<string> ReadOneAsync(RunSubscription subscription, CancellationToken ct)
        {
            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(2));
            return await subscription.Reader.ReadAsync(timeout.Token).ConfigureAwait(false);
        }
    }
}
