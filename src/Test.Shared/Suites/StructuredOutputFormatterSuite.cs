namespace Test.Shared.Suites
{
    using System.Collections.Generic;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Cli.Commands;
    using Mux.Core.Agent;
    using Mux.Core.Models;
    using Touchstone.Core;

    /// <summary>
    /// Touchstone suite for <see cref="StructuredOutputFormatter"/> structured CLI output. Ported from
    /// the <c>StructuredOutputFormatterTests</c> xUnit suite.
    /// </summary>
    public static class StructuredOutputFormatterSuite
    {
        /// <summary>
        /// Builds the structured-output-formatter suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/> for the structured-output-formatter cases.</returns>
        public static TestSuiteDescriptor Create()
        {
            return new TestSuiteDescriptor(
                "StructuredOutputFormatter",
                "Structured CLI output formatting",
                new List<TestCaseDescriptor>
                {
                    new TestCaseDescriptor("StructuredOutputFormatter", "RunLifecycleEventsUsesStableNames", "Lifecycle events serialize with stable event type names", (CancellationToken ct) =>
                    {
                        RunStartedEvent started = new RunStartedEvent
                        {
                            RunId = "run-1",
                            EndpointName = "local",
                            AdapterType = "OpenAiCompatible",
                            BaseUrl = "http://localhost:1234",
                            Model = "test-model",
                            CommandName = "print",
                            ApprovalPolicy = "AutoApprove",
                            WorkingDirectory = "C:\\Code\\Mux",
                            MaxIterations = 10,
                            ToolsEnabled = true,
                            ConfigDirectory = "C:\\Users\\test\\.mux",
                            EndpointSelectionSource = "named_endpoint",
                            CliOverridesApplied = new List<string> { "endpoint", "model" },
                            McpSupported = false,
                            McpConfigured = true,
                            McpServerCount = 2,
                            BuiltInToolCount = 11,
                            EffectiveToolCount = 11,
                            ContextWindow = 32768,
                            ReservedOutputTokens = 4096,
                            UsableInputLimit = 23756,
                            WarningThresholdTokens = 19004,
                            TokenEstimationRatio = 3.5,
                            CompactionStrategy = "trim"
                        };
                        RunCompletedEvent completed = new RunCompletedEvent
                        {
                            RunId = "run-1",
                            Status = "completed",
                            IterationsCompleted = 1,
                            ToolCallCount = 0,
                            ErrorCount = 0,
                            AssistantTextChars = 12,
                            DurationMs = 25,
                            FinalEstimatedTokens = 512,
                            CompactionCount = 1
                        };

                        JsonDocument startedJson = JsonDocument.Parse(StructuredOutputFormatter.FormatEvent(started));
                        JsonDocument completedJson = JsonDocument.Parse(StructuredOutputFormatter.FormatEvent(completed));

                        MuxAssert.AreEqual(1, startedJson.RootElement.GetProperty("contractVersion").GetInt32(), "started contractVersion");
                        MuxAssert.AreEqual("run_started", startedJson.RootElement.GetProperty("eventType").GetString(), "started eventType");
                        MuxAssert.AreEqual("local", startedJson.RootElement.GetProperty("endpointName").GetString(), "endpointName");
                        MuxAssert.AreEqual("print", startedJson.RootElement.GetProperty("commandName").GetString(), "commandName");
                        MuxAssert.AreEqual(10, startedJson.RootElement.GetProperty("maxIterations").GetInt32(), "maxIterations");
                        MuxAssert.AreEqual(32768, startedJson.RootElement.GetProperty("contextWindow").GetInt32(), "contextWindow");
                        MuxAssert.AreEqual(4096, startedJson.RootElement.GetProperty("reservedOutputTokens").GetInt32(), "reservedOutputTokens");
                        MuxAssert.AreEqual(23756, startedJson.RootElement.GetProperty("usableInputLimit").GetInt32(), "usableInputLimit");
                        MuxAssert.AreEqual(19004, startedJson.RootElement.GetProperty("warningThresholdTokens").GetInt32(), "warningThresholdTokens");
                        MuxAssert.AreEqual("trim", startedJson.RootElement.GetProperty("compactionStrategy").GetString(), "compactionStrategy");
                        MuxAssert.IsFalse(startedJson.RootElement.GetProperty("mcp").GetProperty("supported").GetBoolean(), "mcp.supported");
                        MuxAssert.IsTrue(startedJson.RootElement.GetProperty("mcp").GetProperty("configured").GetBoolean(), "mcp.configured");
                        MuxAssert.AreEqual(1, completedJson.RootElement.GetProperty("contractVersion").GetInt32(), "completed contractVersion");
                        MuxAssert.AreEqual("run_completed", completedJson.RootElement.GetProperty("eventType").GetString(), "completed eventType");
                        MuxAssert.AreEqual("completed", completedJson.RootElement.GetProperty("status").GetString(), "status");
                        MuxAssert.AreEqual(512, completedJson.RootElement.GetProperty("finalEstimatedTokens").GetInt32(), "finalEstimatedTokens");
                        MuxAssert.AreEqual(1, completedJson.RootElement.GetProperty("compactionCount").GetInt32(), "compactionCount");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("StructuredOutputFormatter", "TaskPlanUpdatedSerializesTasks", "A task_plan_updated event serializes its change kind and task snapshot", (CancellationToken ct) =>
                    {
                        TaskPlanUpdatedEvent planEvent = new TaskPlanUpdatedEvent
                        {
                            ChangeKind = Mux.Core.Enums.TaskPlanChangeKindEnum.TaskStatusChanged,
                            ChangedTaskId = "t2",
                            Tasks = new List<Mux.Core.Tasks.AgentTask>
                            {
                                new Mux.Core.Tasks.AgentTask { Id = "t1", Title = "Study the pattern", Status = Mux.Core.Enums.AgentTaskStatusEnum.Completed },
                                new Mux.Core.Tasks.AgentTask { Id = "t2", Title = "Add the interface", Status = Mux.Core.Enums.AgentTaskStatusEnum.InProgress }
                            }
                        };
                        JsonDocument json = JsonDocument.Parse(StructuredOutputFormatter.FormatEvent(planEvent));
                        JsonElement root = json.RootElement;
                        MuxAssert.AreEqual("task_plan_updated", root.GetProperty("eventType").GetString(), "eventType");
                        MuxAssert.AreEqual("task_status_changed", root.GetProperty("changeKind").GetString(), "changeKind");
                        MuxAssert.AreEqual("t2", root.GetProperty("changedTaskId").GetString(), "changedTaskId");
                        MuxAssert.AreEqual(2, root.GetProperty("totalCount").GetInt32(), "totalCount");
                        MuxAssert.AreEqual(1, root.GetProperty("completedCount").GetInt32(), "completedCount");
                        JsonElement tasks = root.GetProperty("tasks");
                        MuxAssert.AreEqual(2, tasks.GetArrayLength(), "tasks length");
                        MuxAssert.AreEqual("completed", tasks[0].GetProperty("status").GetString(), "t1 status");
                        MuxAssert.AreEqual("in_progress", tasks[1].GetProperty("status").GetString(), "t2 status");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("StructuredOutputFormatter", "ToolPayloadsRedactsSensitiveValues", "Sensitive values are redacted from tool payloads", (CancellationToken ct) =>
                    {
                        ToolCallProposedEvent agentEvent = new ToolCallProposedEvent
                        {
                            ToolCall = new ToolCall { Id = "call-1", Name = "run_process", Arguments = "{\"authorization\":\"Bearer sk-secret-token\",\"path\":\"README.md\"}" }
                        };
                        JsonDocument json = JsonDocument.Parse(StructuredOutputFormatter.FormatEvent(agentEvent));
                        JsonElement arguments = json.RootElement.GetProperty("toolCall").GetProperty("arguments");
                        MuxAssert.AreEqual("***REDACTED***", arguments.GetProperty("authorization").GetString(), "authorization redacted");
                        MuxAssert.AreEqual("README.md", arguments.GetProperty("path").GetString(), "path retained");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("StructuredOutputFormatter", "ToolResultsRedactsSecretStrings", "Tool results redact secret-looking values while retaining structure", (CancellationToken ct) =>
                    {
                        ToolCallCompletedEvent agentEvent = new ToolCallCompletedEvent
                        {
                            ToolCallId = "call-1",
                            ToolName = "read_file",
                            Result = new ToolResult { ToolCallId = "call-1", Success = true, Content = "{\"token\":\"sk-super-secret\",\"message\":\"ok\"}" },
                            ElapsedMs = 15
                        };
                        JsonDocument json = JsonDocument.Parse(StructuredOutputFormatter.FormatEvent(agentEvent));
                        JsonElement content = json.RootElement.GetProperty("result").GetProperty("content");
                        MuxAssert.AreEqual("***REDACTED***", content.GetProperty("token").GetString(), "token redacted");
                        MuxAssert.AreEqual("ok", content.GetProperty("message").GetString(), "message retained");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("StructuredOutputFormatter", "ErrorEventUsesContractVersionAndFailureMetadata", "Error events expose the versioned contract and classification metadata", (CancellationToken ct) =>
                    {
                        ErrorEvent agentEvent = new ErrorEvent
                        {
                            Code = "llm_connection_error",
                            Message = "Connection refused",
                            CommandName = "print",
                            ConfigDirectory = "C:\\Users\\test\\.mux",
                            BaseUrl = "http://127.0.0.1:1"
                        };
                        JsonDocument json = JsonDocument.Parse(StructuredOutputFormatter.FormatEvent(agentEvent));
                        MuxAssert.AreEqual(1, json.RootElement.GetProperty("contractVersion").GetInt32(), "contractVersion");
                        MuxAssert.AreEqual("error", json.RootElement.GetProperty("eventType").GetString(), "eventType");
                        MuxAssert.AreEqual("llm_connection_error", json.RootElement.GetProperty("code").GetString(), "code");
                        MuxAssert.AreEqual("llm_connection_error", json.RootElement.GetProperty("errorCode").GetString(), "errorCode");
                        MuxAssert.AreEqual("network", json.RootElement.GetProperty("failureCategory").GetString(), "failureCategory");
                        MuxAssert.AreEqual("print", json.RootElement.GetProperty("commandName").GetString(), "commandName");
                        MuxAssert.AreEqual("http://127.0.0.1:1", json.RootElement.GetProperty("baseUrl").GetString(), "baseUrl");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("StructuredOutputFormatter", "ContextEventsUsesStableNamesAndFields", "Context-related events serialize with stable additive shapes", (CancellationToken ct) =>
                    {
                        ContextStatusEvent statusEvent = new ContextStatusEvent
                        {
                            Scope = "active_conversation",
                            EstimatedTokens = 910,
                            UsableInputLimit = 1000,
                            RemainingTokens = 90,
                            RemainingPercent = 9.0,
                            WarningThresholdTokens = 800,
                            MessageCount = 7,
                            Trigger = "preflight",
                            WarningLevel = "approaching"
                        };
                        ContextCompactedEvent compactedEvent = new ContextCompactedEvent
                        {
                            Scope = "active_conversation",
                            Mode = "auto",
                            Strategy = "trim",
                            MessagesBefore = 14,
                            MessagesAfter = 8,
                            EstimatedTokensBefore = 1400,
                            EstimatedTokensAfter = 620,
                            SummaryCreated = false,
                            Reason = "Active conversation exceeded the usable context budget before a model call."
                        };

                        JsonDocument statusJson = JsonDocument.Parse(StructuredOutputFormatter.FormatEvent(statusEvent));
                        JsonDocument compactedJson = JsonDocument.Parse(StructuredOutputFormatter.FormatEvent(compactedEvent));

                        MuxAssert.AreEqual("context_status", statusJson.RootElement.GetProperty("eventType").GetString(), "status eventType");
                        MuxAssert.AreEqual("active_conversation", statusJson.RootElement.GetProperty("scope").GetString(), "scope");
                        MuxAssert.AreEqual("approaching", statusJson.RootElement.GetProperty("warningLevel").GetString(), "warningLevel");
                        MuxAssert.AreEqual(7, statusJson.RootElement.GetProperty("messageCount").GetInt32(), "messageCount");
                        MuxAssert.AreEqual("context_compacted", compactedJson.RootElement.GetProperty("eventType").GetString(), "compacted eventType");
                        MuxAssert.AreEqual("trim", compactedJson.RootElement.GetProperty("strategy").GetString(), "strategy");
                        MuxAssert.IsFalse(compactedJson.RootElement.GetProperty("summaryCreated").GetBoolean(), "summaryCreated");
                        MuxAssert.AreEqual(1400, compactedJson.RootElement.GetProperty("estimatedTokensBefore").GetInt32(), "estimatedTokensBefore");
                        MuxAssert.AreEqual(620, compactedJson.RootElement.GetProperty("estimatedTokensAfter").GetInt32(), "estimatedTokensAfter");
                        return Task.CompletedTask;
                    })
                });
        }
    }
}
