namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Agent;
    using Mux.Core.Models;
    using Mux.Desktop.Conversation;
    using Touchstone.Core;

    /// <summary>
    /// Touchstone suite for <see cref="TurnProjection"/>: assistant/thinking text accumulation, the tool-call
    /// lifecycle (proposed → approved → completed/failed), error capture, completion, cancellation, and the
    /// null-event guard.
    /// </summary>
    public static class TurnProjectionSuite
    {
        /// <summary>
        /// Builds the turn-projection suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/> for the turn-projection cases.</returns>
        public static TestSuiteDescriptor Create()
        {
            return new TestSuiteDescriptor(
                "TurnProjection",
                "Turn event projection",
                new List<TestCaseDescriptor>
                {
                    new TestCaseDescriptor("TurnProjection", "AccumulatesText", "Assistant and thinking text accumulate", (CancellationToken ct) =>
                    {
                        TurnProjection projection = new TurnProjection();
                        projection.Apply(new AssistantTextEvent { Text = "Hello " });
                        projection.Apply(new AssistantTextEvent { Text = "world" });
                        projection.Apply(new AssistantThinkingEvent { Text = "thinking" });

                        MuxAssert.AreEqual("Hello world", projection.AssistantText, "assistant text");
                        MuxAssert.AreEqual("thinking", projection.ThinkingText, "thinking text");
                        MuxAssert.IsTrue(projection.HasFirstToken, "first token");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("TurnProjection", "ToolLifecycleSuccess", "A tool call moves proposed → approved → completed", (CancellationToken ct) =>
                    {
                        TurnProjection projection = new TurnProjection();
                        projection.Apply(new ToolCallProposedEvent { ToolCall = new ToolCall { Id = "t1", Name = "read_file", Arguments = "{}" } });
                        projection.Apply(new ToolCallApprovedEvent { ToolCallId = "t1" });
                        projection.Apply(new ToolCallCompletedEvent
                        {
                            ToolCallId = "t1",
                            ToolName = "read_file",
                            Result = new ToolResult { Success = true, Content = "file contents" },
                            ElapsedMs = 42
                        });

                        MuxAssert.AreEqual(1, projection.ToolCalls.Count, "tool count");
                        MuxAssert.AreEqual(ToolCallStatus.Completed, projection.ToolCalls[0].Status, "completed status");
                        MuxAssert.AreEqual(42L, projection.ToolCalls[0].ElapsedMs, "elapsed");
                        MuxAssert.AreEqual("file contents", projection.ToolCalls[0].ResultSummary, "result summary");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("TurnProjection", "ToolFailure", "A failed tool result yields Failed status", (CancellationToken ct) =>
                    {
                        TurnProjection projection = new TurnProjection();
                        projection.Apply(new ToolCallProposedEvent { ToolCall = new ToolCall { Id = "t2", Name = "write_file", Arguments = "{}" } });
                        projection.Apply(new ToolCallCompletedEvent
                        {
                            ToolCallId = "t2",
                            Result = new ToolResult { Success = false, Content = "denied" },
                            ElapsedMs = 5
                        });

                        MuxAssert.AreEqual(ToolCallStatus.Failed, projection.ToolCalls[0].Status, "failed status");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("TurnProjection", "ErrorAndCompletion", "Error and completion are captured", (CancellationToken ct) =>
                    {
                        TurnProjection projection = new TurnProjection();
                        projection.Apply(new ErrorEvent { Code = "llm_error", Message = "boom" });
                        projection.Apply(new RunCompletedEvent { Status = "completed_with_errors" });

                        MuxAssert.IsNotNull(projection.Error, "error captured");
                        MuxAssert.AreEqual("boom", projection.Error!.Message, "error message");
                        MuxAssert.IsTrue(projection.IsComplete, "is complete");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("TurnProjection", "CancellationAndNullGuard", "Cancellation flag and null-event guard", (CancellationToken ct) =>
                    {
                        TurnProjection projection = new TurnProjection();
                        MuxAssert.IsFalse(projection.WasCancelled, "not cancelled initially");
                        projection.MarkCancelled();
                        MuxAssert.IsTrue(projection.WasCancelled, "cancelled");
                        MuxAssert.Throws<ArgumentNullException>(() => projection.Apply(null!), "null event");
                        return Task.CompletedTask;
                    })
                });
        }
    }
}
