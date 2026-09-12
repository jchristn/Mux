namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Agent;
    using Mux.Core.Conversation;
    using Mux.Core.Models;
    using Touchstone.Core;

    /// <summary>
    /// Touchstone suite for <see cref="TurnProjection"/> — the single shared turn accumulator. Cases cover
    /// the state the desktop renders from (assistant/thinking text accumulation, the tool-call lifecycle
    /// proposed → approved → completed/failed, error capture, completion, cancellation, the null guard) and
    /// the streaming signals the TUI drives its indicator from (first-token fires once on the first non-empty
    /// token; the responded latch fires once per stretch and re-arms on a heartbeat, which also raises
    /// model-working).
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
                    }),

                    new TestCaseDescriptor("TurnProjection", "FirstTokenSignalFiresOnceOnNonEmpty", "First-token signal fires once, on the first non-empty token", (CancellationToken ct) =>
                    {
                        TurnProjection projection = new TurnProjection();
                        int fired = 0;
                        projection.FirstTokenReceived += () => fired++;
                        projection.Apply(new AssistantTextEvent { Text = string.Empty });
                        MuxAssert.AreEqual(0, fired, "empty token does not stamp first-token");
                        projection.Apply(new AssistantTextEvent { Text = "a" });
                        projection.Apply(new AssistantTextEvent { Text = "b" });
                        MuxAssert.AreEqual(1, fired, "first-token fires exactly once");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("TurnProjection", "RespondedLatchesAndReArmsOnHeartbeat", "Responded latches per stretch and re-arms on a heartbeat (which raises model-working)", (CancellationToken ct) =>
                    {
                        TurnProjection projection = new TurnProjection();
                        int responded = 0;
                        int working = 0;
                        projection.ModelResponded += () => responded++;
                        projection.ModelWorking += () => working++;

                        projection.Apply(new AssistantTextEvent { Text = "x" });
                        projection.Apply(new AssistantTextEvent { Text = "y" });
                        MuxAssert.AreEqual(1, responded, "responded fires once until re-armed");

                        projection.Apply(new HeartbeatEvent { StepNumber = 1 });
                        MuxAssert.AreEqual(1, working, "heartbeat raises model-working");

                        projection.Apply(new ToolCallCompletedEvent { ToolCallId = "z", ToolName = "t", Result = new ToolResult { ToolCallId = "z", Success = true, Content = "ok" }, ElapsedMs = 1 });
                        MuxAssert.AreEqual(2, responded, "responded fires again after heartbeat re-arm");
                        return Task.CompletedTask;
                    })
                });
        }
    }
}
