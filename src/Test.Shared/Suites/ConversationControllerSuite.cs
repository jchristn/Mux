namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Agent;
    using Mux.Core.Conversation;
    using Mux.Core.Enums;
    using Touchstone.Core;

    /// <summary>
    /// Touchstone suite for <see cref="ConversationController"/> — the shared turn/queue orchestrator. Cases
    /// drive it with a fake turn executor and assert the history policy (completed exchanges recorded,
    /// completed-but-empty kept for context, cancelled/errored dropped), the serial queue (enqueue while
    /// busy, chain the next on completion, pause/resume), hook gating, queue mutation, and stats.
    /// </summary>
    public static class ConversationControllerSuite
    {
        private const string SuiteId = "ConversationController";

        /// <summary>
        /// Builds the conversation-controller suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/> for controller cases.</returns>
        public static TestSuiteDescriptor Create()
        {
            return new TestSuiteDescriptor(
                SuiteId,
                "Conversation turn/queue orchestration",
                new List<TestCaseDescriptor>
                {
                    Case("CompletedTurnRecordsExchange", "A completed turn records user + assistant and fires events", async (CancellationToken ct) =>
                    {
                        int started = 0, completed = 0;
                        ConversationController c = NewController((p, t) => Task.FromResult(Answer("hi")));
                        c.TurnStarting += _ => started++;
                        c.TurnCompleted += _ => completed++;

                        await c.SubmitAsync("hello", ct);

                        MuxAssert.AreEqual(2, c.History.Count, "user + assistant recorded");
                        MuxAssert.AreEqual(RoleEnum.User, c.History[0].Role, "user first");
                        MuxAssert.AreEqual("hi", c.History[1].Content, "assistant answer");
                        MuxAssert.AreEqual(1, c.TurnCount, "one turn counted");
                        MuxAssert.IsFalse(c.IsBusy, "idle after turn");
                        MuxAssert.AreEqual(1, started, "TurnStarting fired");
                        MuxAssert.AreEqual(1, completed, "TurnCompleted fired");
                    }),

                    Case("CompletedEmptyTurnKept", "A completed turn with no answer keeps the prompt (empty assistant)", async (CancellationToken ct) =>
                    {
                        ConversationController c = NewController((p, t) => Task.FromResult(new TurnOutcome { AssistantText = string.Empty, RunCompleted = Run() }));
                        await c.SubmitAsync("output nothing", ct);

                        MuxAssert.AreEqual(2, c.History.Count, "user + empty assistant kept");
                        MuxAssert.AreEqual("output nothing", c.History[0].Content, "prompt preserved");
                        MuxAssert.AreEqual(string.Empty, c.History[1].Content, "empty assistant");
                    }),

                    Case("CancelledTurnDropped", "A cancelled turn is dropped from history", async (CancellationToken ct) =>
                    {
                        ConversationController c = NewController((p, t) => Task.FromResult(new TurnOutcome { WasCancelled = true }));
                        await c.SubmitAsync("interrupted", ct);
                        MuxAssert.AreEqual(0, c.History.Count, "nothing recorded for a cancelled turn");
                        MuxAssert.AreEqual(1, c.TurnCount, "turn still counted in stats");
                    }),

                    Case("ErroredTurnDropped", "A turn with no answer and no completion is dropped", async (CancellationToken ct) =>
                    {
                        ConversationController c = NewController((p, t) => Task.FromResult(new TurnOutcome { AssistantText = null, RunCompleted = null }));
                        await c.SubmitAsync("errored", ct);
                        MuxAssert.AreEqual(0, c.History.Count, "nothing recorded for an errored turn");
                    }),

                    Case("EnqueueWhileBusyThenChain", "A prompt submitted while busy queues and runs after the first completes", async (CancellationToken ct) =>
                    {
                        TaskCompletionSource<bool> gate = new TaskCompletionSource<bool>();
                        List<string> ran = new List<string>();
                        ConversationController c = NewController(async (p, t) =>
                        {
                            ran.Add(p);
                            if (ran.Count == 1) await gate.Task.ConfigureAwait(false);
                            return Answer("ok:" + p);
                        });

                        Task first = c.SubmitAsync("one", ct);
                        MuxAssert.IsTrue(c.IsBusy, "busy while first runs");

                        await c.SubmitAsync("two", ct);
                        MuxAssert.AreEqual(1, c.QueuedCount, "second is queued");

                        gate.SetResult(true);
                        await first.ConfigureAwait(false);

                        MuxAssert.IsFalse(c.IsBusy, "idle after both");
                        MuxAssert.AreEqual(0, c.QueuedCount, "queue drained");
                        MuxAssert.AreEqual(4, c.History.Count, "both exchanges recorded");
                        MuxAssert.AreEqual("one", c.History[0].Content, "first prompt first");
                        MuxAssert.AreEqual("two", c.History[2].Content, "second prompt after");
                    }),

                    Case("PauseHoldsQueueUntilResume", "A paused queue holds prompts until resumed", async (CancellationToken ct) =>
                    {
                        ConversationController c = NewController((p, t) => Task.FromResult(Answer("ok")));
                        c.PauseQueue();
                        await c.SubmitAsync("queued", ct);
                        MuxAssert.AreEqual(1, c.QueuedCount, "held while paused");
                        MuxAssert.IsFalse(c.IsBusy, "not running while paused");

                        await c.ResumeQueueAsync(ct);
                        MuxAssert.AreEqual(0, c.QueuedCount, "ran after resume");
                        MuxAssert.AreEqual(2, c.History.Count, "exchange recorded after resume");
                    }),

                    Case("HookGateVetoesSubmission", "A failing hook gate vetoes the prompt", async (CancellationToken ct) =>
                    {
                        int calls = 0;
                        ConversationController c = new ConversationController(
                            null, new ConversationStatsAggregator(),
                            (p, t) => { calls++; return Task.FromResult(Answer("x")); },
                            recordCheckpoint: null, passesHooks: _ => false);

                        await c.SubmitAsync("blocked", ct);
                        MuxAssert.AreEqual(0, calls, "executor not invoked");
                        MuxAssert.AreEqual(0, c.History.Count, "nothing recorded");
                        MuxAssert.AreEqual(0, c.QueuedCount, "not queued");
                    }),

                    Case("QueueMutationReordersAndRemoves", "SetPending / Reorder / Remove mutate the queue", (CancellationToken ct) =>
                    {
                        ConversationController c = NewController((p, t) => Task.FromResult(Answer("ok")));
                        c.PauseQueue();
                        c.SetPending(new List<string> { "a", "b", "c" });
                        MuxAssert.AreEqual(3, c.QueuedCount, "three queued");

                        c.ReorderPending(0, 2);
                        MuxAssert.AreEqual("b", c.PendingPrompts[0], "reordered");
                        MuxAssert.AreEqual("a", c.PendingPrompts[2], "a moved to end");

                        c.RemovePending(1);
                        MuxAssert.AreEqual(2, c.QueuedCount, "one removed");
                        return Task.CompletedTask;
                    }),

                    Case("CancelRaisesEvent", "Cancel raises CancelRequested", (CancellationToken ct) =>
                    {
                        ConversationController c = NewController((p, t) => Task.FromResult(Answer("ok")));
                        bool raised = false;
                        c.CancelRequested += () => raised = true;
                        c.Cancel();
                        MuxAssert.IsTrue(raised, "CancelRequested fired");
                        return Task.CompletedTask;
                    })
                });
        }

        #region Helpers

        private static ConversationController NewController(Func<string, CancellationToken, Task<TurnOutcome>> exec)
        {
            return new ConversationController(null, new ConversationStatsAggregator(), exec);
        }

        private static TurnOutcome Answer(string text)
        {
            return new TurnOutcome { AssistantText = text, RunCompleted = Run(), TotalMs = 5, TtftMs = 2 };
        }

        private static RunCompletedEvent Run()
        {
            return new RunCompletedEvent { RunId = "r", Status = "completed", IterationsCompleted = 1, DurationMs = 1 };
        }

        private static TestCaseDescriptor Case(string id, string name, Func<CancellationToken, Task> body)
        {
            return new TestCaseDescriptor(SuiteId, id, name, body);
        }

        #endregion
    }
}
