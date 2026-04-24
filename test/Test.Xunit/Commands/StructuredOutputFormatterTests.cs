namespace Test.Xunit.Commands
{
    using System.Text.Json;
    using global::Xunit;
    using Mux.Cli.Commands;
    using Mux.Core.Agent;
    using Mux.Core.Models;

    /// <summary>
    /// Unit tests for structured CLI output formatting.
    /// </summary>
    public class StructuredOutputFormatterTests
    {
        /// <summary>
        /// Verifies that lifecycle events serialize with stable event type names.
        /// </summary>
        [Fact]
        public void FormatEvent_RunLifecycleEvents_UsesStableNames()
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
                CliOverridesApplied = new System.Collections.Generic.List<string> { "endpoint", "model" },
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

            Assert.Equal(1, startedJson.RootElement.GetProperty("contractVersion").GetInt32());
            Assert.Equal("run_started", startedJson.RootElement.GetProperty("eventType").GetString());
            Assert.Equal("local", startedJson.RootElement.GetProperty("endpointName").GetString());
            Assert.Equal("print", startedJson.RootElement.GetProperty("commandName").GetString());
            Assert.Equal(32768, startedJson.RootElement.GetProperty("contextWindow").GetInt32());
            Assert.Equal(4096, startedJson.RootElement.GetProperty("reservedOutputTokens").GetInt32());
            Assert.Equal(23756, startedJson.RootElement.GetProperty("usableInputLimit").GetInt32());
            Assert.Equal(19004, startedJson.RootElement.GetProperty("warningThresholdTokens").GetInt32());
            Assert.Equal("trim", startedJson.RootElement.GetProperty("compactionStrategy").GetString());
            Assert.False(startedJson.RootElement.GetProperty("mcp").GetProperty("supported").GetBoolean());
            Assert.True(startedJson.RootElement.GetProperty("mcp").GetProperty("configured").GetBoolean());
            Assert.Equal(1, completedJson.RootElement.GetProperty("contractVersion").GetInt32());
            Assert.Equal("run_completed", completedJson.RootElement.GetProperty("eventType").GetString());
            Assert.Equal("completed", completedJson.RootElement.GetProperty("status").GetString());
            Assert.Equal(512, completedJson.RootElement.GetProperty("finalEstimatedTokens").GetInt32());
            Assert.Equal(1, completedJson.RootElement.GetProperty("compactionCount").GetInt32());
        }

        /// <summary>
        /// Verifies that sensitive values are redacted from structured tool payloads.
        /// </summary>
        [Fact]
        public void FormatEvent_ToolPayloads_RedactsSensitiveValues()
        {
            ToolCallProposedEvent agentEvent = new ToolCallProposedEvent
            {
                ToolCall = new ToolCall
                {
                    Id = "call-1",
                    Name = "run_process",
                    Arguments = "{\"authorization\":\"Bearer sk-secret-token\",\"path\":\"README.md\"}"
                }
            };

            JsonDocument json = JsonDocument.Parse(StructuredOutputFormatter.FormatEvent(agentEvent));
            JsonElement toolCall = json.RootElement.GetProperty("toolCall");
            JsonElement arguments = toolCall.GetProperty("arguments");

            Assert.Equal("***REDACTED***", arguments.GetProperty("authorization").GetString());
            Assert.Equal("README.md", arguments.GetProperty("path").GetString());
        }

        /// <summary>
        /// Verifies that tool results retain structure while redacting secret-looking values.
        /// </summary>
        [Fact]
        public void FormatEvent_ToolResults_RedactsSecretStrings()
        {
            ToolCallCompletedEvent agentEvent = new ToolCallCompletedEvent
            {
                ToolCallId = "call-1",
                ToolName = "read_file",
                Result = new ToolResult
                {
                    ToolCallId = "call-1",
                    Success = true,
                    Content = "{\"token\":\"sk-super-secret\",\"message\":\"ok\"}"
                },
                ElapsedMs = 15
            };

            JsonDocument json = JsonDocument.Parse(StructuredOutputFormatter.FormatEvent(agentEvent));
            JsonElement result = json.RootElement.GetProperty("result");
            JsonElement content = result.GetProperty("content");

            Assert.Equal("***REDACTED***", content.GetProperty("token").GetString());
            Assert.Equal("ok", content.GetProperty("message").GetString());
        }

        /// <summary>
        /// Verifies that error events expose the versioned compatibility contract and classification metadata.
        /// </summary>
        [Fact]
        public void FormatEvent_ErrorEvent_UsesContractVersionAndFailureMetadata()
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

            Assert.Equal(1, json.RootElement.GetProperty("contractVersion").GetInt32());
            Assert.Equal("error", json.RootElement.GetProperty("eventType").GetString());
            Assert.Equal("llm_connection_error", json.RootElement.GetProperty("code").GetString());
            Assert.Equal("llm_connection_error", json.RootElement.GetProperty("errorCode").GetString());
            Assert.Equal("network", json.RootElement.GetProperty("failureCategory").GetString());
            Assert.Equal("print", json.RootElement.GetProperty("commandName").GetString());
            Assert.Equal("http://127.0.0.1:1", json.RootElement.GetProperty("baseUrl").GetString());
        }

        /// <summary>
        /// Verifies that context-related events serialize with stable additive shapes.
        /// </summary>
        [Fact]
        public void FormatEvent_ContextEvents_UsesStableNamesAndFields()
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

            Assert.Equal("context_status", statusJson.RootElement.GetProperty("eventType").GetString());
            Assert.Equal("active_conversation", statusJson.RootElement.GetProperty("scope").GetString());
            Assert.Equal("approaching", statusJson.RootElement.GetProperty("warningLevel").GetString());
            Assert.Equal(7, statusJson.RootElement.GetProperty("messageCount").GetInt32());

            Assert.Equal("context_compacted", compactedJson.RootElement.GetProperty("eventType").GetString());
            Assert.Equal("trim", compactedJson.RootElement.GetProperty("strategy").GetString());
            Assert.False(compactedJson.RootElement.GetProperty("summaryCreated").GetBoolean());
            Assert.Equal(1400, compactedJson.RootElement.GetProperty("estimatedTokensBefore").GetInt32());
            Assert.Equal(620, compactedJson.RootElement.GetProperty("estimatedTokensAfter").GetInt32());
        }
    }
}
