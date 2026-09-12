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

                    new TestCaseDescriptor("ConversationService", "EmptyTurnDropsUserMessage", "A turn with no assistant text drops its user message and the next completed turn stays clean", async (CancellationToken ct) =>
                    {
                        // First turn: the model returns no assistant text (e.g. it ended in its reasoning
                        // channel). The user message must not linger in history.
                        ConversationService service = new ConversationService(
                            new ListTurnRunner(new List<AgentEvent> { new RunCompletedEvent { Status = "completed" } }),
                            null);
                        await service.RunTurnAsync("first (no answer)", ct);
                        MuxAssert.AreEqual(0, service.History.Count, "empty turn leaves nothing in history");

                        // Second turn (a fresh service to swap the runner) completing normally yields a clean
                        // user→assistant pair with no consecutive user messages carried over.
                        ConversationService service2 = new ConversationService(
                            new ListTurnRunner(new List<AgentEvent> { new AssistantTextEvent { Text = "answer" }, new RunCompletedEvent { Status = "completed" } }),
                            service.History);
                        await service2.RunTurnAsync("second", ct);
                        MuxAssert.AreEqual(2, service2.History.Count, "only the completed exchange is recorded");
                        MuxAssert.AreEqual(RoleEnum.User, service2.History[0].Role, "user first");
                        MuxAssert.AreEqual(RoleEnum.Assistant, service2.History[1].Role, "assistant second");
                    }),

                    new TestCaseDescriptor("ConversationService", "ResumeFromHistory", "Initial history is preserved", async (CancellationToken ct) =>
                    {
                        List<ConversationMessage> seed = new List<ConversationMessage>
                        {
                            new ConversationMessage { Role = RoleEnum.User, Content = "earlier" },
                            new ConversationMessage { Role = RoleEnum.Assistant, Content = "reply" }
                        };
                        ConversationService service = new ConversationService(
                            new ListTurnRunner(new List<AgentEvent> { new RunCompletedEvent { Status = "completed" } }),
                            seed);

                        MuxAssert.AreEqual(2, service.History.Count, "seeded history");
                        await service.RunTurnAsync("next", ct);
                        // The turn produced no assistant text, so its user message is dropped and the seeded
                        // history is preserved unchanged (no dangling prompt appended).
                        MuxAssert.AreEqual(2, service.History.Count, "no dangling user message on an empty turn");
                        MuxAssert.AreEqual(RoleEnum.Assistant, service.History[1].Role, "seeded history intact");
                    })
                });
        }
    }
}
