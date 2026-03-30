namespace Test.Automated.Suites
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
    using Test.Shared;

    /// <summary>
    /// Tests for tool-use interactions where the model proposes and executes tool calls.
    /// </summary>
    public class ToolUseTests : TestSuite
    {
        #region Private-Members

        private bool _LiveMode;

        #endregion

        #region Public-Members

        /// <summary>
        /// The display name of this test suite.
        /// </summary>
        public override string Name => "Tool-Use Tests";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="ToolUseTests"/> class.
        /// </summary>
        /// <param name="liveMode">True to run against a live endpoint, false to use mock.</param>
        public ToolUseTests(bool liveMode)
        {
            _LiveMode = liveMode;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Defines and runs all tests in this suite.
        /// </summary>
        /// <returns>A task representing the asynchronous operation.</returns>
        public override async Task RunTestsAsync()
        {
            await RunTest("ReadFileTool_InAgentLoop", ReadFileTool_InAgentLoop);
            await RunTest("ToolCallDenied_EmitsError", ToolCallDenied_EmitsError);
            await RunTest("WriteFileTool_CreatesFile", WriteFileTool_CreatesFile);
        }

        #endregion

        #region Private-Methods

        /// <summary>
        /// Verifies the full tool call flow: model proposes a read_file tool call, the tool is executed,
        /// and a follow-up text response is returned summarizing the file contents.
        /// </summary>
        private async Task ReadFileTool_InAgentLoop()
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

                    // First response: model proposes read_file tool call
                    string toolCallChunk = "{\"choices\":[{\"delta\":{\"tool_calls\":[{\"index\":0,\"id\":\"call_1\",\"function\":{\"name\":\"read_file\",\"arguments\":\"{\\\"file_path\\\":\\\"" + escapedPath + "\\\"}\"}}]},\"finish_reason\":\"tool_calls\"}]}";

                    // Follow-up response: model returns summary text after seeing tool result
                    string followUpChunk = "{\"choices\":[{\"delta\":{\"content\":\"The file contains: Hello from the test file!\"},\"finish_reason\":\"stop\"}]}";

                    server.RegisterStreamingResponse("read this file", new List<string> { toolCallChunk });
                    server.RegisterStreamingResponse("Hello from the test file", new List<string> { followUpChunk });
                    server.Start();

                    EndpointConfig endpoint = BuildEndpoint(server.BaseUrl);
                    AgentLoopOptions options = new AgentLoopOptions(endpoint)
                    {
                        ApprovalPolicy = ApprovalPolicyEnum.AutoApprove,
                        MaxIterations = 5,
                        WorkingDirectory = tempDir
                    };

                    List<AgentEvent> events = await CollectEvents(options, "read this file");

                    bool hasProposed = events.Any((AgentEvent e) => e is ToolCallProposedEvent);
                    AssertTrue(hasProposed, "Expected a ToolCallProposedEvent");

                    bool hasCompleted = events.Any((AgentEvent e) => e is ToolCallCompletedEvent);
                    AssertTrue(hasCompleted, "Expected a ToolCallCompletedEvent");

                    // Verify tool completed successfully
                    ToolCallCompletedEvent completedEvent = (ToolCallCompletedEvent)events.First(
                        (AgentEvent e) => e is ToolCallCompletedEvent);
                    AssertTrue(completedEvent.Result.Success, "Expected tool call to succeed");

                    // Verify follow-up text response
                    bool hasTextEvent = events.Any((AgentEvent e) => e is AssistantTextEvent);
                    AssertTrue(hasTextEvent, "Expected AssistantTextEvent with final response");
                }
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
            }
        }

        /// <summary>
        /// Verifies that when the approval policy is set to Deny, a tool call proposal results
        /// in an ErrorEvent with code "tool_call_denied".
        /// </summary>
        private async Task ToolCallDenied_EmitsError()
        {
            using (MockHttpServer server = new MockHttpServer())
            {
                // Model proposes a tool call
                string toolCallChunk = "{\"choices\":[{\"delta\":{\"tool_calls\":[{\"index\":0,\"id\":\"call_deny\",\"function\":{\"name\":\"read_file\",\"arguments\":\"{\\\"file_path\\\":\\\"test.txt\\\"}\"}}]},\"finish_reason\":\"tool_calls\"}]}";

                // After denial, the model gets the denial message and responds with text
                string followUpChunk = "{\"choices\":[{\"delta\":{\"content\":\"I understand the tool call was denied.\"},\"finish_reason\":\"stop\"}]}";

                server.RegisterStreamingResponse("deny this", new List<string> { toolCallChunk });
                server.RegisterStreamingResponse("tool_call_denied", new List<string> { followUpChunk });
                server.Start();

                EndpointConfig endpoint = BuildEndpoint(server.BaseUrl);
                AgentLoopOptions options = new AgentLoopOptions(endpoint)
                {
                    ApprovalPolicy = ApprovalPolicyEnum.Deny,
                    MaxIterations = 5
                };

                List<AgentEvent> events = await CollectEvents(options, "deny this");

                bool hasProposed = events.Any((AgentEvent e) => e is ToolCallProposedEvent);
                AssertTrue(hasProposed, "Expected a ToolCallProposedEvent");

                bool hasDeniedError = events.Any((AgentEvent e) =>
                    e is ErrorEvent errorEvent && errorEvent.Code == "tool_call_denied");
                AssertTrue(hasDeniedError, "Expected ErrorEvent with code 'tool_call_denied'");
            }
        }

        /// <summary>
        /// Verifies that when the model proposes a write_file tool call, the file is actually created on disk.
        /// </summary>
        private async Task WriteFileTool_CreatesFile()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "mux_test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            string targetFile = Path.Combine(tempDir, "created_by_tool.txt");
            string escapedPath = targetFile.Replace("\\", "\\\\").Replace("\"", "\\\"");

            try
            {
                using (MockHttpServer server = new MockHttpServer())
                {
                    // Model proposes a write_file tool call
                    string toolCallChunk = "{\"choices\":[{\"delta\":{\"tool_calls\":[{\"index\":0,\"id\":\"call_write\",\"function\":{\"name\":\"write_file\",\"arguments\":\"{\\\"file_path\\\":\\\"" + escapedPath + "\\\",\\\"content\\\":\\\"Written by test\\\"}\"}}]},\"finish_reason\":\"tool_calls\"}]}";

                    // Follow-up response after tool execution
                    string followUpChunk = "{\"choices\":[{\"delta\":{\"content\":\"File has been written.\"},\"finish_reason\":\"stop\"}]}";

                    server.RegisterStreamingResponse("write a file", new List<string> { toolCallChunk });
                    server.RegisterStreamingResponse("Written by test", new List<string> { followUpChunk });
                    server.Start();

                    EndpointConfig endpoint = BuildEndpoint(server.BaseUrl);
                    AgentLoopOptions options = new AgentLoopOptions(endpoint)
                    {
                        ApprovalPolicy = ApprovalPolicyEnum.AutoApprove,
                        MaxIterations = 5,
                        WorkingDirectory = tempDir
                    };

                    List<AgentEvent> events = await CollectEvents(options, "write a file");

                    bool hasCompleted = events.Any((AgentEvent e) => e is ToolCallCompletedEvent);
                    AssertTrue(hasCompleted, "Expected a ToolCallCompletedEvent for write_file");

                    AssertTrue(File.Exists(targetFile), "Expected the file to be created on disk");

                    string fileContent = File.ReadAllText(targetFile);
                    AssertContains(fileContent, "Written by test");
                }
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
            }
        }

        /// <summary>
        /// Builds an endpoint configuration pointing to the mock server.
        /// </summary>
        private EndpointConfig BuildEndpoint(string baseUrl)
        {
            EndpointConfig endpoint = new EndpointConfig
            {
                Name = "mock-test",
                AdapterType = AdapterTypeEnum.OpenAiCompatible,
                BaseUrl = baseUrl,
                Model = "test-model",
                ApiKey = "test-key"
            };
            return endpoint;
        }

        /// <summary>
        /// Collects all events from an agent loop run into a list.
        /// </summary>
        private async Task<List<AgentEvent>> CollectEvents(AgentLoopOptions options, string prompt)
        {
            List<AgentEvent> events = new List<AgentEvent>();
            using (AgentLoop loop = new AgentLoop(options))
            {
                CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                await foreach (AgentEvent agentEvent in loop.RunAsync(prompt, cts.Token))
                {
                    events.Add(agentEvent);
                }
            }
            return events;
        }

        #endregion
    }
}
