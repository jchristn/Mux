namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Cli.Commands;
    using Mux.Core.Agent;
    using Mux.Core.Models;
    using Touchstone.Core;

    /// <summary>
    /// Touchstone suite for <see cref="AgentEventSerializer"/> — the Core event envelope shared by the CLI's
    /// headless JSONL output and the <c>mux serve</c> streaming bridges. Covers per-type serialization,
    /// golden parity with the CLI delegation path, and negative/edge cases (null input, redaction,
    /// stats suppression, unknown enum fallback).
    /// </summary>
    public static class AgentEventSerializerSuite
    {
        /// <summary>
        /// Builds the agent-event-serializer suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/> for the serializer cases.</returns>
        public static TestSuiteDescriptor Create()
        {
            return new TestSuiteDescriptor(
                "AgentEventSerializer",
                "Core agent-event envelope serialization",
                new List<TestCaseDescriptor>
                {
                    new TestCaseDescriptor("AgentEventSerializer", "EachEventTypeUsesStableName", "Every event type serializes with its stable eventType name and contract version", (CancellationToken ct) =>
                    {
                        AssertEventType(new RunStartedEvent { RunId = "r" }, "run_started");
                        AssertEventType(new AssistantTextEvent { Text = "hi" }, "assistant_text");
                        AssertEventType(new AssistantThinkingEvent { Text = "hmm" }, "assistant_thinking");
                        AssertEventType(new ToolCallProposedEvent { ToolCall = new ToolCall { Id = "c", Name = "read_file", Arguments = "{}" } }, "tool_call_proposed");
                        AssertEventType(new ToolCallApprovedEvent { ToolCallId = "c" }, "tool_call_approved");
                        AssertEventType(new ToolCallCompletedEvent { ToolCallId = "c", ToolName = "read_file", Result = new ToolResult { ToolCallId = "c", Success = true, Content = "{}" } }, "tool_call_completed");
                        AssertEventType(new ErrorEvent { Code = "llm_error", Message = "boom" }, "error");
                        AssertEventType(new HeartbeatEvent { StepNumber = 3 }, "heartbeat");
                        AssertEventType(new ContextStatusEvent { Scope = "active_conversation" }, "context_status");
                        AssertEventType(new ContextCompactedEvent { Scope = "active_conversation" }, "context_compacted");
                        AssertEventType(new RunCompletedEvent { RunId = "r", Status = "completed" }, "run_completed");
                        AssertEventType(new TaskPlanUpdatedEvent { ChangeKind = Mux.Core.Enums.TaskPlanChangeKindEnum.PlanCreated, Tasks = new List<Mux.Core.Tasks.AgentTask>() }, "task_plan_updated");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("AgentEventSerializer", "GoldenParityWithCliDelegation", "The Core serializer output equals the CLI StructuredOutputFormatter output for the same events", (CancellationToken ct) =>
                    {
                        List<AgentEvent> events = new List<AgentEvent>
                        {
                            new RunStartedEvent { RunId = "run-1", EndpointName = "local", Model = "m", CommandName = "print", MaxIterations = 5, ContextWindow = 4096 },
                            new AssistantTextEvent { Text = "Hello world" },
                            new AssistantThinkingEvent { Text = "Consider the options." },
                            new ToolCallProposedEvent { ToolCall = new ToolCall { Id = "c1", Name = "read_file", Arguments = "{\"path\":\"README.md\"}" } },
                            new ToolCallCompletedEvent { ToolCallId = "c1", ToolName = "read_file", ElapsedMs = 10, Result = new ToolResult { ToolCallId = "c1", Success = true, Content = "{\"ok\":true}" } },
                            new ErrorEvent { Code = "llm_connection_error", Message = "refused", CommandName = "print" },
                            new RunCompletedEvent { RunId = "run-1", Status = "completed", IterationsCompleted = 2, DurationMs = 42, FinalEstimatedTokens = 128 }
                        };

                        foreach (AgentEvent agentEvent in events)
                        {
                            string core = AgentEventSerializer.ToEnvelopeLine(agentEvent);
                            string cli = StructuredOutputFormatter.FormatEvent(agentEvent);
                            MuxAssert.AreEqual(cli, core, "core output equals CLI output for " + agentEvent.EventType);
                        }

                        // includeStats:false parity too.
                        RunCompletedEvent completed = new RunCompletedEvent { RunId = "run-2", Status = "completed", IterationsCompleted = 1, DurationMs = 5 };
                        MuxAssert.AreEqual(
                            StructuredOutputFormatter.FormatEvent(completed, false),
                            AgentEventSerializer.ToEnvelopeLine(completed, false),
                            "includeStats:false parity");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("AgentEventSerializer", "IncludeStatsFalseOmitsMetrics", "run_completed with includeStats:false omits the metrics block and usage object", (CancellationToken ct) =>
                    {
                        RunCompletedEvent completed = new RunCompletedEvent { RunId = "r", Status = "completed", IterationsCompleted = 3, DurationMs = 99, FinalEstimatedTokens = 256 };

                        JsonDocument withStats = JsonDocument.Parse(AgentEventSerializer.ToEnvelopeLine(completed, true));
                        MuxAssert.IsTrue(withStats.RootElement.TryGetProperty("usage", out _), "usage present with stats");
                        MuxAssert.IsTrue(withStats.RootElement.TryGetProperty("durationMs", out _), "durationMs present with stats");

                        JsonDocument noStats = JsonDocument.Parse(AgentEventSerializer.ToEnvelopeLine(completed, false));
                        MuxAssert.IsFalse(noStats.RootElement.TryGetProperty("usage", out _), "usage omitted without stats");
                        MuxAssert.IsFalse(noStats.RootElement.TryGetProperty("durationMs", out _), "durationMs omitted without stats");
                        MuxAssert.AreEqual("completed", noStats.RootElement.GetProperty("status").GetString(), "status retained without stats");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("AgentEventSerializer", "EmptyTextSerializesToEmpty", "An empty assistant text serializes to an empty string, not null", (CancellationToken ct) =>
                    {
                        JsonDocument json = JsonDocument.Parse(AgentEventSerializer.ToEnvelopeLine(new AssistantTextEvent { Text = string.Empty }));
                        MuxAssert.AreEqual(string.Empty, json.RootElement.GetProperty("text").GetString(), "empty text stays empty string");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("AgentEventSerializer", "RedactStripsSecrets", "Redact removes bearer tokens, sk- keys, and header assignments and tolerates null", (CancellationToken ct) =>
                    {
                        string sk = AgentEventSerializer.Redact("key is sk-abc123 here");
                        MuxAssert.DoesNotContain("sk-abc123", sk, "sk key removed");
                        MuxAssert.Contains("***REDACTED***", sk, "sk key replaced with marker");

                        string bearer = AgentEventSerializer.Redact("Authorization: Bearer abc.def-123");
                        MuxAssert.DoesNotContain("abc.def-123", bearer, "bearer secret removed");
                        MuxAssert.Contains("***REDACTED***", bearer, "bearer replaced with marker");

                        MuxAssert.AreEqual(string.Empty, AgentEventSerializer.Redact(null), "null redacts to empty");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("AgentEventSerializer", "NullEventThrows", "Serializing a null event throws ArgumentNullException", (CancellationToken ct) =>
                    {
                        MuxAssert.Throws<ArgumentNullException>(() => AgentEventSerializer.ToEnvelopeLine(null!), "null event throws");
                        return Task.CompletedTask;
                    })
                });
        }

        private static void AssertEventType(AgentEvent agentEvent, string expected)
        {
            JsonDocument json = JsonDocument.Parse(AgentEventSerializer.ToEnvelopeLine(agentEvent));
            MuxAssert.AreEqual(expected, json.RootElement.GetProperty("eventType").GetString(), expected + " eventType");
            MuxAssert.AreEqual(AgentEventSerializer.ContractVersion, json.RootElement.GetProperty("contractVersion").GetInt32(), expected + " contractVersion");
        }
    }
}
