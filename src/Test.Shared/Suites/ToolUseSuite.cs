namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Agent;
    using Mux.Core.Enums;
    using Mux.Core.Models;
    using Touchstone.Core;

    /// <summary>
    /// Touchstone suite for tool-use interactions where the model proposes and executes tool calls.
    /// Ported from the legacy <c>ToolUseTests</c> suite; runs in mock mode.
    /// </summary>
    public static class ToolUseSuite
    {
        /// <summary>
        /// Builds the tool-use suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/> containing all tool-use cases.</returns>
        public static TestSuiteDescriptor Create()
        {
            return new TestSuiteDescriptor(
                "ToolUse",
                "Tool-use proposal and execution",
                new List<TestCaseDescriptor>
                {
                    new TestCaseDescriptor(
                        "ToolUse",
                        "ReadFileToolInAgentLoop",
                        "read_file tool executes within the agent loop",
                        (CancellationToken ct) => ReadFileToolInAgentLoopAsync(ct)),

                    new TestCaseDescriptor(
                        "ToolUse",
                        "ToolCallDeniedEmitsError",
                        "Denied tool call emits tool_call_denied error",
                        (CancellationToken ct) => ToolCallDeniedEmitsErrorAsync(ct)),

                    new TestCaseDescriptor(
                        "ToolUse",
                        "WriteFileToolCreatesFile",
                        "write_file tool creates a file on disk",
                        (CancellationToken ct) => WriteFileToolCreatesFileAsync(ct))
                });
        }

        private static async Task ReadFileToolInAgentLoopAsync(CancellationToken ct)
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "mux_test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            string tempFile = Path.Combine(tempDir, "test_read.txt");
            File.WriteAllText(tempFile, "Hello from the test file!");

            try
            {
                using (MockHttpServer server = new MockHttpServer())
                {
                    string escapedPath = tempFile.Replace("\\", "\\\\").Replace("\"", "\\\"");
                    string toolCallChunk = "{\"choices\":[{\"delta\":{\"tool_calls\":[{\"index\":0,\"id\":\"call_1\",\"function\":{\"name\":\"read_file\",\"arguments\":\"{\\\"file_path\\\":\\\"" + escapedPath + "\\\"}\"}}]},\"finish_reason\":\"tool_calls\"}]}";
                    string followUpChunk = "{\"choices\":[{\"delta\":{\"content\":\"The file contains: Hello from the test file!\"},\"finish_reason\":\"stop\"}]}";

                    server.RegisterStreamingResponse("read this file", new List<string> { toolCallChunk });
                    server.RegisterStreamingResponse("Hello from the test file", new List<string> { followUpChunk });
                    server.Start();

                    AgentLoopOptions options = new AgentLoopOptions(AgentTestHarness.BuildMockEndpoint(server.BaseUrl))
                    {
                        ApprovalPolicy = ApprovalPolicyEnum.AutoApprove,
                        MaxIterations = 5,
                        WorkingDirectory = tempDir
                    };

                    List<AgentEvent> events = await AgentTestHarness.CollectEventsAsync(options, "read this file", ct).ConfigureAwait(false);

                    MuxAssert.IsTrue(events.Any((AgentEvent e) => e is ToolCallProposedEvent), "a ToolCallProposedEvent");
                    MuxAssert.IsTrue(events.Any((AgentEvent e) => e is ToolCallCompletedEvent), "a ToolCallCompletedEvent");

                    ToolCallCompletedEvent completedEvent = (ToolCallCompletedEvent)events.First((AgentEvent e) => e is ToolCallCompletedEvent);
                    MuxAssert.IsTrue(completedEvent.Result.Success, "tool call succeeded");
                    MuxAssert.IsTrue(events.Any((AgentEvent e) => e is AssistantTextEvent), "AssistantTextEvent with final response");
                }
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }

        private static async Task ToolCallDeniedEmitsErrorAsync(CancellationToken ct)
        {
            using (MockHttpServer server = new MockHttpServer())
            {
                string toolCallChunk = "{\"choices\":[{\"delta\":{\"tool_calls\":[{\"index\":0,\"id\":\"call_deny\",\"function\":{\"name\":\"read_file\",\"arguments\":\"{\\\"file_path\\\":\\\"test.txt\\\"}\"}}]},\"finish_reason\":\"tool_calls\"}]}";
                string followUpChunk = "{\"choices\":[{\"delta\":{\"content\":\"I understand the tool call was denied.\"},\"finish_reason\":\"stop\"}]}";

                server.RegisterStreamingResponse("deny this", new List<string> { toolCallChunk });
                server.RegisterStreamingResponse("tool_call_denied", new List<string> { followUpChunk });
                server.Start();

                AgentLoopOptions options = new AgentLoopOptions(AgentTestHarness.BuildMockEndpoint(server.BaseUrl))
                {
                    ApprovalPolicy = ApprovalPolicyEnum.Deny,
                    MaxIterations = 5
                };

                List<AgentEvent> events = await AgentTestHarness.CollectEventsAsync(options, "deny this", ct).ConfigureAwait(false);

                MuxAssert.IsTrue(events.Any((AgentEvent e) => e is ToolCallProposedEvent), "a ToolCallProposedEvent");
                MuxAssert.IsTrue(
                    events.Any((AgentEvent e) => e is ErrorEvent errorEvent && errorEvent.Code == "tool_call_denied"),
                    "ErrorEvent with code 'tool_call_denied'");
            }
        }

        private static async Task WriteFileToolCreatesFileAsync(CancellationToken ct)
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "mux_test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            string targetFile = Path.Combine(tempDir, "created_by_tool.txt");
            string escapedPath = targetFile.Replace("\\", "\\\\").Replace("\"", "\\\"");

            try
            {
                using (MockHttpServer server = new MockHttpServer())
                {
                    string toolCallChunk = "{\"choices\":[{\"delta\":{\"tool_calls\":[{\"index\":0,\"id\":\"call_write\",\"function\":{\"name\":\"write_file\",\"arguments\":\"{\\\"file_path\\\":\\\"" + escapedPath + "\\\",\\\"content\\\":\\\"Written by test\\\"}\"}}]},\"finish_reason\":\"tool_calls\"}]}";
                    string followUpChunk = "{\"choices\":[{\"delta\":{\"content\":\"File has been written.\"},\"finish_reason\":\"stop\"}]}";

                    server.RegisterStreamingResponse("write a file", new List<string> { toolCallChunk });
                    server.RegisterStreamingResponse("Written by test", new List<string> { followUpChunk });
                    server.Start();

                    AgentLoopOptions options = new AgentLoopOptions(AgentTestHarness.BuildMockEndpoint(server.BaseUrl))
                    {
                        ApprovalPolicy = ApprovalPolicyEnum.AutoApprove,
                        MaxIterations = 5,
                        WorkingDirectory = tempDir
                    };

                    List<AgentEvent> events = await AgentTestHarness.CollectEventsAsync(options, "write a file", ct).ConfigureAwait(false);

                    MuxAssert.IsTrue(events.Any((AgentEvent e) => e is ToolCallCompletedEvent), "a ToolCallCompletedEvent for write_file");
                    MuxAssert.IsTrue(File.Exists(targetFile), "the file was created on disk");
                    MuxAssert.Contains("Written by test", File.ReadAllText(targetFile), "file content");
                }
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }
    }
}
