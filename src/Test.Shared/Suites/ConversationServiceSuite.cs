namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Agent;
    using Mux.Core.Enums;
    using Mux.Core.Models;
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

                    new TestCaseDescriptor("ConversationService", "CancelledTurn", "A cancelled turn is flagged and appends no assistant reply", async (CancellationToken ct) =>
                    {
                        ConversationService service = new ConversationService(new ListTurnRunner(null, cancel: true), null);
                        TurnProjection projection = await service.RunTurnAsync("do work", ct);

                        MuxAssert.IsTrue(projection.WasCancelled, "cancelled flag");
                        MuxAssert.AreEqual(1, service.History.Count, "only user message");
                        MuxAssert.AreEqual(RoleEnum.User, service.History[0].Role, "user message");
                        MuxAssert.IsFalse(service.IsBusy, "idle after cancel");
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
                        MuxAssert.AreEqual(3, service.History.Count, "user appended, no assistant text");
                    })
                });
        }
    }
}
