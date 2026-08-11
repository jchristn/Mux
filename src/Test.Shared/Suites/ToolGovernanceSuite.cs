namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Agent;
    using Mux.Core.Enums;
    using Mux.Core.Models;
    using Touchstone.Core;

    /// <summary>
    /// Touchstone suite for Phase 3 tool governance: the allow/deny tool lists, the read-only and
    /// workspace-write sandbox postures, and the `sandboxPosture` contract field. Helper-level cases pin
    /// the <see cref="ToolGovernance"/> policy directly; loop-level cases drive a real <see cref="AgentLoop"/>
    /// against a mock model that proposes a governed tool, asserting the gate refuses or permits it. Both
    /// directions (permitted and refused) are covered.
    /// </summary>
    public static class ToolGovernanceSuite
    {
        /// <summary>
        /// Builds the tool-governance suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/> containing all tool-governance cases.</returns>
        public static TestSuiteDescriptor Create()
        {
            return new TestSuiteDescriptor(
                "ToolGovernance",
                "Allow/deny tool lists and sandbox postures",
                new List<TestCaseDescriptor>
                {
                    new TestCaseDescriptor("ToolGovernance", "IsPermittedHonorsAllowDeny", "allow/deny lists gate tools with deny winning", (CancellationToken ct) =>
                    {
                        MuxAssert.IsTrue(ToolGovernance.IsPermitted("read_file", null, null), "no lists allow everything");
                        MuxAssert.IsTrue(ToolGovernance.IsPermitted("read_file", new List<string> { "read_file" }, null), "allow match permitted");
                        MuxAssert.IsFalse(ToolGovernance.IsPermitted("write_file", new List<string> { "read_file" }, null), "non-allow-listed tool refused");
                        MuxAssert.IsFalse(ToolGovernance.IsPermitted("write_file", new List<string> { "write_file" }, new List<string> { "write_file" }), "deny wins over allow");
                        MuxAssert.IsTrue(ToolGovernance.IsPermitted("read_file", new List<string> { "read_*" }, null), "glob star matches");
                        MuxAssert.IsFalse(ToolGovernance.IsPermitted("delete_file", null, new List<string> { "delete_*" }), "glob deny matches");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("ToolGovernance", "PostureParseAndName", "posture strings parse and round-trip", (CancellationToken ct) =>
                    {
                        MuxAssert.IsTrue(ToolGovernance.TryParsePosture("read-only", out SandboxPostureEnum ro), "read-only parses");
                        MuxAssert.AreEqual(SandboxPostureEnum.ReadOnly, ro, "read-only value");
                        MuxAssert.IsTrue(ToolGovernance.TryParsePosture("workspace-write", out SandboxPostureEnum ww), "workspace-write parses");
                        MuxAssert.AreEqual(SandboxPostureEnum.WorkspaceWrite, ww, "workspace-write value");
                        MuxAssert.IsTrue(ToolGovernance.TryParsePosture(null, out SandboxPostureEnum none), "null parses to none");
                        MuxAssert.AreEqual(SandboxPostureEnum.None, none, "null value is none");
                        MuxAssert.IsFalse(ToolGovernance.TryParsePosture("bogus", out SandboxPostureEnum _), "unknown posture rejected");
                        MuxAssert.AreEqual("read-only", ToolGovernance.PostureName(SandboxPostureEnum.ReadOnly), "read-only name");
                        MuxAssert.AreEqual("workspace-write", ToolGovernance.PostureName(SandboxPostureEnum.WorkspaceWrite), "workspace-write name");
                        MuxAssert.AreEqual("none", ToolGovernance.PostureName(SandboxPostureEnum.None), "none name");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("ToolGovernance", "WorkspaceWriteConfinesPaths", "workspace-write allows in-root writes and blocks escapes", (CancellationToken ct) =>
                    {
                        string root = Path.Combine(Path.GetTempPath(), "mux-gov-root");
                        string additional = Path.Combine(Path.GetTempPath(), "mux-gov-extra");

                        JsonElement inside = ParseArgs("{\"file_path\":\"notes/todo.txt\"}");
                        MuxAssert.IsNull(ToolGovernance.CheckWorkspaceWrite("write_file", inside, root, null), "relative in-root write permitted");

                        JsonElement outside = ParseArgs("{\"file_path\":\"" + JsonEscape(Path.Combine(Path.GetTempPath(), "mux-gov-outside.txt")) + "\"}");
                        MuxAssert.IsNotNull(ToolGovernance.CheckWorkspaceWrite("write_file", outside, root, null), "escape write refused");

                        JsonElement inExtra = ParseArgs("{\"file_path\":\"" + JsonEscape(Path.Combine(additional, "x.txt")) + "\"}");
                        MuxAssert.IsNull(ToolGovernance.CheckWorkspaceWrite("write_file", inExtra, root, new List<string> { additional }), "write into an additional root permitted");

                        JsonElement readArgs = ParseArgs("{\"file_path\":\"" + JsonEscape(Path.Combine(Path.GetTempPath(), "elsewhere.txt")) + "\"}");
                        MuxAssert.IsNull(ToolGovernance.CheckWorkspaceWrite("read_file", readArgs, root, null), "read_file is not path-confined");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("ToolGovernance", "RunStartedSerializesPosture", "run_started serializes sandboxPosture", (CancellationToken ct) =>
                    {
                        string json = Mux.Cli.Commands.StructuredOutputFormatter.FormatEvent(new RunStartedEvent { RunId = "r1", SandboxPosture = "read-only" });
                        MuxAssert.Contains("\"sandboxPosture\":\"read-only\"", json, "sandboxPosture present in run_started");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("ToolGovernance", "DenyToolsBlocksCall", "a denied tool is refused before execution", (CancellationToken ct) => DenyToolsBlocksCallAsync(ct)),

                    new TestCaseDescriptor("ToolGovernance", "AllowToolsPermitsListed", "an allow-listed tool executes", (CancellationToken ct) => AllowToolsPermitsListedAsync(ct)),

                    new TestCaseDescriptor("ToolGovernance", "ReadOnlyBlocksMutatingTool", "read-only sandbox blocks a mutating tool", (CancellationToken ct) => ReadOnlyBlocksMutatingToolAsync(ct)),

                    new TestCaseDescriptor("ToolGovernance", "WorkspaceWriteBlocksEscape", "workspace-write sandbox blocks a write outside the root", (CancellationToken ct) => WorkspaceWriteBlocksEscapeAsync(ct))
                });
        }

        private static async Task DenyToolsBlocksCallAsync(CancellationToken ct)
        {
            using (MockHttpServer server = new MockHttpServer())
            {
                server.RegisterStreamingResponse("dowrite", new List<string> { BuildToolCallChunk("c1", "write_file", "{\"file_path\":\"out.txt\",\"content\":\"hi\"}") });
                server.RegisterStreamingResponse("tool_call_denied", new List<string> { AgentTestHarness.BuildTextSseChunk("Acknowledged.") });
                server.Start();

                AgentLoopOptions options = new AgentLoopOptions(AgentTestHarness.BuildMockEndpoint(server.BaseUrl))
                {
                    ApprovalPolicy = ApprovalPolicyEnum.AutoApprove,
                    MaxIterations = 4,
                    DeniedTools = new List<string> { "write_file" }
                };

                List<AgentEvent> events = await AgentTestHarness.CollectEventsAsync(options, "dowrite please", ct).ConfigureAwait(false);

                MuxAssert.IsTrue(
                    events.Any((AgentEvent e) => e is ErrorEvent error && error.Code == "tool_call_denied" && error.Message.Contains("tool policy", StringComparison.OrdinalIgnoreCase)),
                    "write_file refused by the deny list");
                MuxAssert.IsFalse(
                    events.Any((AgentEvent e) => e is ToolCallCompletedEvent completed && completed.ToolName == "write_file"),
                    "write_file never executed");
            }
        }

        private static async Task AllowToolsPermitsListedAsync(CancellationToken ct)
        {
            using (MockHttpServer server = new MockHttpServer())
            {
                server.RegisterStreamingResponse("doread", new List<string> { BuildToolCallChunk("c1", "read_file", "{\"file_path\":\"missing.txt\"}") });
                server.Start();

                AgentLoopOptions options = new AgentLoopOptions(AgentTestHarness.BuildMockEndpoint(server.BaseUrl))
                {
                    ApprovalPolicy = ApprovalPolicyEnum.AutoApprove,
                    MaxIterations = 2,
                    AllowedTools = new List<string> { "read_file" }
                };

                List<AgentEvent> events = await AgentTestHarness.CollectEventsAsync(options, "doread please", ct).ConfigureAwait(false);

                MuxAssert.IsTrue(
                    events.Any((AgentEvent e) => e is ToolCallCompletedEvent completed && completed.ToolName == "read_file"),
                    "allow-listed read_file executed");
                MuxAssert.IsFalse(
                    events.Any((AgentEvent e) => e is ErrorEvent error && error.Code == "tool_call_denied"),
                    "no governance denial for an allow-listed tool");
            }
        }

        private static async Task ReadOnlyBlocksMutatingToolAsync(CancellationToken ct)
        {
            using (MockHttpServer server = new MockHttpServer())
            {
                server.RegisterStreamingResponse("dowrite", new List<string> { BuildToolCallChunk("c1", "write_file", "{\"file_path\":\"out.txt\",\"content\":\"hi\"}") });
                server.RegisterStreamingResponse("tool_call_denied", new List<string> { AgentTestHarness.BuildTextSseChunk("Acknowledged.") });
                server.Start();

                AgentLoopOptions options = new AgentLoopOptions(AgentTestHarness.BuildMockEndpoint(server.BaseUrl))
                {
                    ApprovalPolicy = ApprovalPolicyEnum.AutoApprove,
                    MaxIterations = 4,
                    SandboxPosture = SandboxPostureEnum.ReadOnly
                };

                List<AgentEvent> events = await AgentTestHarness.CollectEventsAsync(options, "dowrite please", ct).ConfigureAwait(false);

                MuxAssert.IsTrue(
                    events.Any((AgentEvent e) => e is ErrorEvent error && error.Code == "tool_call_denied" && error.Message.Contains("read-only", StringComparison.OrdinalIgnoreCase)),
                    "mutating tool blocked by read-only sandbox");
                MuxAssert.IsFalse(
                    events.Any((AgentEvent e) => e is ToolCallCompletedEvent completed && completed.ToolName == "write_file"),
                    "write_file never executed under read-only");
            }
        }

        private static async Task WorkspaceWriteBlocksEscapeAsync(CancellationToken ct)
        {
            using (MockHttpServer server = new MockHttpServer())
            {
                string workingDirectory = Path.Combine(Path.GetTempPath(), "mux-ws-" + Guid.NewGuid().ToString("N"));
                string outsidePath = Path.Combine(Path.GetTempPath(), "mux-outside-" + Guid.NewGuid().ToString("N") + ".txt");

                server.RegisterStreamingResponse("dowrite", new List<string> { BuildToolCallChunk("c1", "write_file", "{\"file_path\":\"" + JsonEscape(outsidePath) + "\",\"content\":\"hi\"}") });
                server.RegisterStreamingResponse("tool_call_denied", new List<string> { AgentTestHarness.BuildTextSseChunk("Acknowledged.") });
                server.Start();

                AgentLoopOptions options = new AgentLoopOptions(AgentTestHarness.BuildMockEndpoint(server.BaseUrl))
                {
                    ApprovalPolicy = ApprovalPolicyEnum.AutoApprove,
                    MaxIterations = 4,
                    WorkingDirectory = workingDirectory,
                    SandboxPosture = SandboxPostureEnum.WorkspaceWrite
                };

                List<AgentEvent> events = await AgentTestHarness.CollectEventsAsync(options, "dowrite please", ct).ConfigureAwait(false);

                MuxAssert.IsTrue(
                    events.Any((AgentEvent e) => e is ErrorEvent error && error.Code == "tool_call_denied" && error.Message.Contains("workspace-write", StringComparison.OrdinalIgnoreCase)),
                    "escaping write blocked by workspace-write sandbox");
                MuxAssert.IsFalse(
                    events.Any((AgentEvent e) => e is ToolCallCompletedEvent completed && completed.ToolName == "write_file"),
                    "escaping write_file never executed");
                MuxAssert.IsFalse(File.Exists(outsidePath), "no file was written outside the workspace");
            }
        }

        private static JsonElement ParseArgs(string json)
        {
            return JsonDocument.Parse(json).RootElement.Clone();
        }

        private static string JsonEscape(string value)
        {
            // Escape a filesystem path for embedding inside a JSON string literal (backslashes and quotes).
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        private static string BuildToolCallChunk(string id, string name, string argumentsJson)
        {
            // The `arguments` field is a JSON string whose value is itself JSON, so it is escaped for
            // embedding. Built as a literal (rather than an anonymous type) to keep the file free of `var`.
            string escapedArgs = JsonEscape(argumentsJson);
            return "{\"choices\":[{\"delta\":{\"tool_calls\":[{\"index\":0,\"id\":\""
                + id
                + "\",\"function\":{\"name\":\""
                + name
                + "\",\"arguments\":\""
                + escapedArgs
                + "\"}}]},\"finish_reason\":\"tool_calls\"}]}";
        }
    }
}
