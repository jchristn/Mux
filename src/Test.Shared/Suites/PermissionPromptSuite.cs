namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Cli.Commands;
    using Mux.Core.Agent;
    using Mux.Core.Approvals;
    using Mux.Core.Enums;
    using Mux.Core.Models;
    using Mux.Core.Tools;
    using Touchstone.Core;

    /// <summary>
    /// Touchstone suite for MCP permission prompts (<see cref="McpPermissionPrompter"/>, the engine of
    /// <c>mux print --permission-prompt-tool</c>), glob patterns in the AutoSafe allowlist, and a denial's message
    /// reaching the model through <see cref="AgentLoop"/>.
    /// </summary>
    public static class PermissionPromptSuite
    {
        /// <summary>
        /// Builds the permission-prompt suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/> for the permission-prompt cases.</returns>
        public static TestSuiteDescriptor Create()
        {
            return new TestSuiteDescriptor(
                "PermissionPrompt",
                "MCP permission prompt tool decisions and the AutoSafe allowlist",
                new List<TestCaseDescriptor>
                {
                    new TestCaseDescriptor("PermissionPrompt", "NormalizesClaudeToolNames", "mcp__server__tool becomes server.tool; other names are kept", (CancellationToken ct) =>
                    {
                        MuxAssert.AreEqual("armada.cli_permission_prompt", McpPermissionPrompter.NormalizeToolName("mcp__armada__cli_permission_prompt"), "claude form");
                        MuxAssert.AreEqual("armada.cli_permission_prompt", McpPermissionPrompter.NormalizeToolName(" armada.cli_permission_prompt "), "mux form");
                        MuxAssert.AreEqual("mcp__broken", McpPermissionPrompter.NormalizeToolName("mcp__broken"), "no separator kept");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("PermissionPrompt", "ParsePrintReadsPromptFlags", "print parser reads the permission prompt flags; --auto-approve-tools accumulates", (CancellationToken ct) =>
                    {
                        PrintSettings settings = CliArgumentParser.ParsePrint(new[]
                        {
                            "--permission-prompt-tool", "armada.cli_permission_prompt",
                            "--permission-prompt-timeout=720",
                            "--auto-approve-tools", "armada.*",
                            "--auto-approve-tools", "write_file|edit_file",
                            "do it"
                        });
                        MuxAssert.AreEqual("armada.cli_permission_prompt", settings.PermissionPromptTool, "tool");
                        MuxAssert.AreEqual(720, settings.PermissionPromptTimeout ?? 0, "timeout");
                        MuxAssert.AreEqual("armada.*,write_file|edit_file", settings.AutoApproveTools, "auto-approve accumulates");
                        MuxAssert.AreEqual("do it", settings.Prompt, "prompt");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("PermissionPrompt", "AllowFromTextContent", "An allow answer in a text content item approves", (CancellationToken ct) =>
                    {
                        PermissionPromptResult result = McpPermissionPrompter.ParseResult("p.t", Envelope("{\"behavior\":\"allow\",\"updatedInput\":{\"command\":\"ls\"}}"));
                        MuxAssert.IsTrue(result.Approved, "approved");
                        MuxAssert.IsNull(result.Message, "no message");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("PermissionPrompt", "DenyCarriesMessage", "A deny answer denies with its message", (CancellationToken ct) =>
                    {
                        PermissionPromptResult result = McpPermissionPrompter.ParseResult("p.t", Envelope("{\"behavior\":\"deny\",\"message\":\"Denied by an approver.\"}"));
                        MuxAssert.IsFalse(result.Approved, "denied");
                        MuxAssert.AreEqual("Denied by an approver.", result.Message, "message");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("PermissionPrompt", "AllowFromStructuredContent", "An allow answer in structuredContent approves", (CancellationToken ct) =>
                    {
                        ToolResult toolResult = new ToolResult { Success = true, Content = "{\"content\":[],\"structuredContent\":{\"behavior\":\"allow\"}}" };
                        MuxAssert.IsTrue(McpPermissionPrompter.ParseResult("p.t", toolResult).Approved, "approved");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("PermissionPrompt", "FailuresDeny", "Errors, missing decisions, and unknown behaviors deny", (CancellationToken ct) =>
                    {
                        ToolResult error = new ToolResult { Success = false, Content = "{\"content\":[{\"type\":\"text\",\"text\":\"{\\\"behavior\\\":\\\"allow\\\"}\"}],\"isError\":true}" };
                        MuxAssert.IsFalse(McpPermissionPrompter.ParseResult("p.t", error).Approved, "isError denies even with an allow text");
                        MuxAssert.IsFalse(McpPermissionPrompter.ParseResult("p.t", Envelope("{\"error\":\"forbidden\"}")).Approved, "no behavior denies");
                        MuxAssert.IsFalse(McpPermissionPrompter.ParseResult("p.t", Envelope("{\"behavior\":\"maybe\"}")).Approved, "unknown behavior denies");
                        MuxAssert.IsFalse(McpPermissionPrompter.ParseResult("p.t", Envelope("not json")).Approved, "text that is not JSON denies");
                        MuxAssert.IsFalse(McpPermissionPrompter.ParseResult("p.t", new ToolResult { Success = true, Content = string.Empty }).Approved, "empty denies");
                        MuxAssert.IsFalse(McpPermissionPrompter.ParseResult("p.t", null).Approved, "null denies");
                        PermissionPromptResult failed = McpPermissionPrompter.ParseResult("p.t", Envelope("{\"error\":\"forbidden\"}"));
                        MuxAssert.IsTrue(failed.Message != null && failed.Message.Contains("forbidden"), "message echoes the answer");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("PermissionPrompt", "PromptSendsClaudeArguments", "The prompt tool receives tool_name, the input object, and tool_use_id", async (CancellationToken ct) =>
                    {
                        string? calledTool = null;
                        string? sentArguments = null;
                        McpPermissionPrompter prompter = new McpPermissionPrompter("mcp__armada__cli_permission_prompt", (string tool, JsonElement arguments, CancellationToken token) =>
                        {
                            calledTool = tool;
                            sentArguments = arguments.GetRawText();
                            return Task.FromResult(Envelope("{\"behavior\":\"allow\"}"));
                        });

                        PermissionPromptResult result = await prompter.PromptAsync(MakeRequest("run_process", "{\"command\":\"ls\"}"), ct).ConfigureAwait(false);
                        MuxAssert.IsTrue(result.Approved, "approved");
                        MuxAssert.AreEqual("armada.cli_permission_prompt", calledTool, "normalized tool");
                        PromptArguments? sent = JsonSerializer.Deserialize<PromptArguments>(sentArguments!);
                        MuxAssert.IsNotNull(sent, "arguments parse");
                        MuxAssert.AreEqual("run_process", sent!.tool_name, "tool_name");
                        MuxAssert.AreEqual("call_1", sent.tool_use_id, "tool_use_id");
                        MuxAssert.AreEqual("ls", sent.input?.command, "input object");
                    }),

                    new TestCaseDescriptor("PermissionPrompt", "PromptExceptionDenies", "A prompt tool that throws denies the call", async (CancellationToken ct) =>
                    {
                        McpPermissionPrompter prompter = new McpPermissionPrompter("p.t", (string tool, JsonElement arguments, CancellationToken token) =>
                            throw new InvalidOperationException("connection refused"));
                        PermissionPromptResult result = await prompter.PromptAsync(MakeRequest("run_process", "not json"), ct).ConfigureAwait(false);
                        MuxAssert.IsFalse(result.Approved, "denied");
                        MuxAssert.IsTrue(result.Message != null && result.Message.Contains("connection refused"), "message names the failure");
                    }),

                    new TestCaseDescriptor("PermissionPrompt", "AllowlistGlobs", "AutoSafe allowlist entries are globs with | alternatives", async (CancellationToken ct) =>
                    {
                        int escalations = 0;
                        ApprovalRouter router = new ApprovalRouter(ApprovalPolicyEnum.AutoSafe, new[] { "armada.*", "write_file|edit_file" });
                        Func<ApprovalRequest, CancellationToken, Task<ApprovalDecision>> escalate = (ApprovalRequest request, CancellationToken token) =>
                        {
                            escalations++;
                            return Task.FromResult(ApprovalDecision.Denied);
                        };

                        MuxAssert.IsTrue(await router.RequestApprovalAsync(MakeRequest("armada.update_mission", "{}"), escalate, ct).ConfigureAwait(false), "server glob");
                        MuxAssert.IsTrue(await router.RequestApprovalAsync(MakeRequest("EDIT_FILE", "{}"), escalate, ct).ConfigureAwait(false), "alternative, case-insensitive");
                        MuxAssert.AreEqual(0, escalations, "allowlisted tools never escalate");
                        MuxAssert.IsFalse(await router.RequestApprovalAsync(MakeRequest("run_process", "{}"), escalate, ct).ConfigureAwait(false), "other tools escalate");
                        MuxAssert.IsFalse(await router.RequestApprovalAsync(MakeRequest("armada_other.tool", "{}"), escalate, ct).ConfigureAwait(false), "whole-name match only");
                        MuxAssert.AreEqual(2, escalations, "escalated the rest");
                    }),

                    new TestCaseDescriptor("PermissionPrompt", "AgentLoopReturnsDenyMessage", "Through AgentLoop, a prompt denial's message is the tool result the model sees", async (CancellationToken ct) =>
                    {
                        string tempDir = Path.Combine(Path.GetTempPath(), "mux_permission_prompt_" + Guid.NewGuid().ToString("N"));
                        Directory.CreateDirectory(tempDir);
                        try
                        {
                            using MockHttpServer server = new MockHttpServer();
                            string toolCall = "{\"choices\":[{\"delta\":{\"tool_calls\":[{\"index\":0,\"id\":\"call_x\",\"function\":{\"name\":\"write_file\",\"arguments\":\"{\\\"file_path\\\":\\\"a.txt\\\",\\\"content\\\":\\\"x\\\"}\"}}]},\"finish_reason\":\"tool_calls\"}]}";
                            server.RegisterStreamingResponse("write it", new List<string> { toolCall });
                            server.RegisterStreamingResponse("Blocked by an approver rule", new List<string> { "{\"choices\":[{\"delta\":{\"content\":\"ok\"},\"finish_reason\":\"stop\"}]}" });
                            server.Start();

                            int prompts = 0;
                            AgentLoopOptions options = new AgentLoopOptions(AgentTestHarness.BuildMockEndpoint(server.BaseUrl))
                            {
                                ApprovalPolicy = ApprovalPolicyEnum.AutoSafe,
                                MaxIterations = 5,
                                WorkingDirectory = tempDir,
                                PromptUserFunc = (ToolCall tc) => Task.FromResult("y"),
                                PermissionPrompt = (ApprovalRequest request, CancellationToken token) =>
                                {
                                    prompts++;
                                    return Task.FromResult(PermissionPromptResult.Deny("Blocked by an approver rule."));
                                }
                            };

                            List<AgentEvent> events = await AgentTestHarness.CollectEventsAsync(options, "write it", ct).ConfigureAwait(false);
                            MuxAssert.AreEqual(1, prompts, "the permission prompt decided, not PromptUserFunc");
                            MuxAssert.IsTrue(events.Any(e => e is ErrorEvent errorEvent && errorEvent.Code == "tool_call_denied" && errorEvent.Message.Contains("Blocked by an approver rule")), "denial names the reason");
                            MuxAssert.IsFalse(File.Exists(Path.Combine(tempDir, "a.txt")), "the tool did not run");
                        }
                        finally
                        {
                            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
                        }
                    })
                });
        }

        private static ToolResult Envelope(string text)
        {
            string content = JsonSerializer.Serialize(new { content = new[] { new { type = "text", text = text } } });
            return new ToolResult { Success = true, Content = content };
        }

        private static ApprovalRequest MakeRequest(string toolName, string arguments)
        {
            return new ApprovalRequest
            {
                JobId = "j1",
                ToolCallId = "call_1",
                ToolName = toolName,
                ArgumentsSummary = arguments,
                MutationKind = ToolMutationKind.Mutating
            };
        }

        private sealed class PromptArguments
        {
            public string? tool_name { get; set; }

            public PromptInput? input { get; set; }

            public string? tool_use_id { get; set; }
        }

        private sealed class PromptInput
        {
            public string? command { get; set; }
        }
    }
}
