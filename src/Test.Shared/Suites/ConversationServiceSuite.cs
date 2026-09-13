namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Agent;
    using Mux.Core.Enums;
    using Mux.Core.Models;
    using Mux.Core.Conversation;
    using Mux.Desktop.Conversation;
    using Mux.Desktop.Services;
    using Test.Shared.Support;
    using Touchstone.Core;

    /// <summary>
    /// Touchstone suite for <see cref="ConversationService"/> over a fake <see cref="ITurnRunner"/>: a
    /// completed turn appends user and assistant messages and surfaces events; a blank prompt is rejected;
    /// and a cancelled turn is flagged without appending an assistant reply.
    /// </summary>
    public static class ConversationServiceSuite
    {
        /// <summary>
        /// Builds the conversation-service suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/> for the conversation-service cases.</returns>
        public static TestSuiteDescriptor Create()
        {
            return new TestSuiteDescriptor(
                "ConversationService",
                "Conversation turn orchestration",
                new List<TestCaseDescriptor>
                {
                    new TestCaseDescriptor("ConversationService", "CompletedTurn", "A completed turn appends user + assistant and raises events", async (CancellationToken ct) =>
                    {
                        List<AgentEvent> events = new List<AgentEvent>
                        {
                            new AssistantTextEvent { Text = "Hi" },
                            new RunCompletedEvent { Status = "completed" }
                        };
                        ConversationService service = new ConversationService(new ListTurnRunner(events), null);

                        int eventCount = 0;
                        service.Event += (sender, args) => eventCount++;

                        TurnProjection projection = await service.RunTurnAsync("hello", ct);

                        MuxAssert.AreEqual("Hi", projection.AssistantText, "assistant text");
                        MuxAssert.AreEqual(2, service.History.Count, "history count");
                        MuxAssert.AreEqual(RoleEnum.User, service.History[0].Role, "first is user");
                        MuxAssert.AreEqual(RoleEnum.Assistant, service.History[1].Role, "second is assistant");
                        MuxAssert.AreEqual("Hi", service.History[1].Content, "assistant content");
                        MuxAssert.IsFalse(service.IsBusy, "idle after turn");
                        MuxAssert.AreEqual(2, eventCount, "event count");
                    }),

                    new TestCaseDescriptor("ConversationService", "BlankPromptRejected", "A blank prompt is rejected", async (CancellationToken ct) =>
                    {
                        ConversationService service = new ConversationService(new ListTurnRunner(new List<AgentEvent>()), null);
                        await MuxAssert.ThrowsAsync<ArgumentException>(
                            async () => await service.RunTurnAsync("   ", ct),
                            "blank prompt");
                        MuxAssert.AreEqual(0, service.History.Count, "history untouched");
                    }),

                    new TestCaseDescriptor("ConversationService", "CancelledTurn", "A cancelled turn is flagged and leaves no dangling user message", async (CancellationToken ct) =>
                    {
                        ConversationService service = new ConversationService(new ListTurnRunner(null, cancel: true), null);
                        TurnProjection projection = await service.RunTurnAsync("do work", ct);

                        MuxAssert.IsTrue(projection.WasCancelled, "cancelled flag");
                        // The user message added before the turn is removed when the turn yields no assistant
                        // reply, so history is not polluted with a dangling, unanswered prompt.
                        MuxAssert.AreEqual(0, service.History.Count, "no dangling user message after cancel");
                        MuxAssert.IsFalse(service.IsBusy, "idle after cancel");
                    }),

                    new TestCaseDescriptor("ConversationService", "CompletedEmptyTurnIsKept", "A completed turn with no answer keeps the user prompt (empty assistant) for context", async (CancellationToken ct) =>
                    {
                        // The model ran to completion (a RunCompletedEvent) but produced no answer text — for
                        // example it was asked to output nothing. The prompt is KEPT (with an empty assistant
                        // reply) so the next turn still has it as context; only cancelled/errored turns drop.
                        ConversationService service = new ConversationService(
                            new ListTurnRunner(new List<AgentEvent> { new RunCompletedEvent { Status = "completed" } }),
                            null);
                        await service.RunTurnAsync("output nothing", ct);
                        MuxAssert.AreEqual(2, service.History.Count, "user + empty assistant kept for a completed turn");
                        MuxAssert.AreEqual(RoleEnum.User, service.History[0].Role, "user first");
                        MuxAssert.AreEqual("output nothing", service.History[0].Content, "user prompt preserved");
                        MuxAssert.AreEqual(RoleEnum.Assistant, service.History[1].Role, "empty assistant second");
                        MuxAssert.AreEqual(string.Empty, service.History[1].Content, "assistant reply is empty");
                    }),

                    new TestCaseDescriptor("ConversationService", "ErroredTurnDropsUserMessage", "A turn with no answer and no completion (errored/timed out) drops its user message", async (CancellationToken ct) =>
                    {
                        // No AssistantTextEvent and no RunCompletedEvent — an aborted/errored turn. The user
                        // message must not linger so the next turn is not batched with a dangling prompt.
                        ConversationService service = new ConversationService(
                            new ListTurnRunner(new List<AgentEvent> { new ErrorEvent { Code = "e", Message = "boom" } }),
                            null);
                        await service.RunTurnAsync("first (errored)", ct);
                        MuxAssert.AreEqual(0, service.History.Count, "errored turn leaves nothing in history");
                    }),

                    new TestCaseDescriptor("ConversationService", "IndependentInstancesIsolated", "Two per-tab conversations run concurrently without sharing history (parallel-tabs premise)", async (CancellationToken ct) =>
                    {
                        // Each desktop tab owns its own runner + ConversationService so tabs can run turns at
                        // the same time. Two services driven concurrently must keep entirely separate histories.
                        ConversationService a = new ConversationService(
                            new ListTurnRunner(new List<AgentEvent> { new AssistantTextEvent { Text = "answer-A" }, new RunCompletedEvent { Status = "completed" } }), null);
                        ConversationService b = new ConversationService(
                            new ListTurnRunner(new List<AgentEvent> { new AssistantTextEvent { Text = "answer-B" }, new RunCompletedEvent { Status = "completed" } }), null);

                        Task<TurnProjection> turnA = a.RunTurnAsync("prompt-A", ct);
                        Task<TurnProjection> turnB = b.RunTurnAsync("prompt-B", ct);
                        await Task.WhenAll(turnA, turnB);

                        MuxAssert.AreEqual(2, a.History.Count, "a history isolated");
                        MuxAssert.AreEqual(2, b.History.Count, "b history isolated");
                        MuxAssert.AreEqual("prompt-A", a.History[0].Content, "a user");
                        MuxAssert.AreEqual("answer-A", a.History[1].Content, "a assistant");
                        MuxAssert.AreEqual("prompt-B", b.History[0].Content, "b user");
                        MuxAssert.AreEqual("answer-B", b.History[1].Content, "b assistant");
                    }),

                    new TestCaseDescriptor("ConversationService", "ResumeFromHistory", "Initial history is preserved", async (CancellationToken ct) =>
                    {
                        List<ConversationMessage> seed = new List<ConversationMessage>
                        {
                            new ConversationMessage { Role = RoleEnum.User, Content = "earlier" },
                            new ConversationMessage { Role = RoleEnum.Assistant, Content = "reply" }
                        };
                        ConversationService service = new ConversationService(
                            new ListTurnRunner(new List<AgentEvent> { new AssistantTextEvent { Text = "ok" }, new RunCompletedEvent { Status = "completed" } }),
                            seed);

                        MuxAssert.AreEqual(2, service.History.Count, "seeded history");
                        await service.RunTurnAsync("next", ct);
                        // The seeded history is preserved and the new completed exchange is appended after it.
                        MuxAssert.AreEqual(4, service.History.Count, "seed + new exchange");
                        MuxAssert.AreEqual("reply", service.History[1].Content, "seeded assistant intact");
                        MuxAssert.AreEqual("next", service.History[2].Content, "new user appended");
                        MuxAssert.AreEqual("ok", service.History[3].Content, "new assistant appended");
                    })
                });
        }
    }
}
