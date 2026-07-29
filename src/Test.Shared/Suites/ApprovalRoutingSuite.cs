namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Agent;
    using Mux.Core.Approvals;
    using Mux.Core.Enums;
    using Mux.Core.Tools;
    using Touchstone.Core;

    /// <summary>
    /// Touchstone suite for <see cref="ApprovalRouter"/> policy resolution, "always" memory, per-router
    /// independence, and (via a live <see cref="AgentLoop"/>) the prompt-response mapping. Supersedes
    /// the former <c>ApprovalHandler</c> suite.
    /// </summary>
    public static class ApprovalRoutingSuite
    {
        /// <summary>
        /// Builds the approval-routing suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/> for the approval-routing cases.</returns>
        public static TestSuiteDescriptor Create()
        {
            return new TestSuiteDescriptor(
                "ApprovalRouting",
                "Per-job approval policy resolution and escalation",
                new List<TestCaseDescriptor>
                {
                    new TestCaseDescriptor("ApprovalRouting", "AutoApproveApprovesWithoutEscalating", "AutoApprove approves without prompting", async (CancellationToken ct) =>
                    {
                        int escalations = 0;
                        ApprovalRouter router = new ApprovalRouter(ApprovalPolicyEnum.AutoApprove);
                        bool approved = await router.RequestApprovalAsync(MakeRequest("write_file", ToolMutationKind.Mutating), Escalation(ApprovalDecision.Denied, () => escalations++), ct).ConfigureAwait(false);
                        MuxAssert.IsTrue(approved, "approved");
                        MuxAssert.AreEqual(0, escalations, "no escalation");
                    }),

                    new TestCaseDescriptor("ApprovalRouting", "DenyDeniesWithoutEscalating", "Deny denies without prompting", async (CancellationToken ct) =>
                    {
                        int escalations = 0;
                        ApprovalRouter router = new ApprovalRouter(ApprovalPolicyEnum.Deny);
                        bool approved = await router.RequestApprovalAsync(MakeRequest("read_file", ToolMutationKind.ReadOnly), Escalation(ApprovalDecision.Approved, () => escalations++), ct).ConfigureAwait(false);
                        MuxAssert.IsFalse(approved, "denied");
                        MuxAssert.AreEqual(0, escalations, "no escalation");
                    }),

                    new TestCaseDescriptor("ApprovalRouting", "AskEscalatesAndHonorsDecision", "Ask escalates and honors approve/deny decisions", async (CancellationToken ct) =>
                    {
                        int escalations = 0;
                        ApprovalRouter router = new ApprovalRouter(ApprovalPolicyEnum.Ask);
                        bool approved = await router.RequestApprovalAsync(MakeRequest("write_file", ToolMutationKind.Mutating), Escalation(ApprovalDecision.Approved, () => escalations++), ct).ConfigureAwait(false);
                        MuxAssert.IsTrue(approved, "approved on Approved");
                        bool denied = await router.RequestApprovalAsync(MakeRequest("edit_file", ToolMutationKind.Mutating), Escalation(ApprovalDecision.Denied, () => escalations++), ct).ConfigureAwait(false);
                        MuxAssert.IsFalse(denied, "denied on Denied");
                        MuxAssert.AreEqual(2, escalations, "escalated each time");
                    }),

                    new TestCaseDescriptor("ApprovalRouting", "AutoSafeApprovesReadOnlyEscalatesMutating", "AutoSafe auto-approves read-only and prompts for mutating", async (CancellationToken ct) =>
                    {
                        int escalations = 0;
                        ApprovalRouter router = new ApprovalRouter(ApprovalPolicyEnum.AutoSafe);
                        bool read = await router.RequestApprovalAsync(MakeRequest("read_file", ToolMutationKind.ReadOnly), Escalation(ApprovalDecision.Denied, () => escalations++), ct).ConfigureAwait(false);
                        MuxAssert.IsTrue(read, "read auto-approved");
                        MuxAssert.AreEqual(0, escalations, "read did not escalate");
                        bool write = await router.RequestApprovalAsync(MakeRequest("write_file", ToolMutationKind.Mutating), Escalation(ApprovalDecision.Approved, () => escalations++), ct).ConfigureAwait(false);
                        MuxAssert.IsTrue(write, "mutating approved after escalation");
                        MuxAssert.AreEqual(1, escalations, "mutating escalated");
                    }),

                    new TestCaseDescriptor("ApprovalRouting", "AutoSafeAllowlistApprovesWithoutEscalating", "AutoSafe auto-approves allowlisted mutating tools without prompting", async (CancellationToken ct) =>
                    {
                        int escalations = 0;
                        ApprovalRouter router = new ApprovalRouter(ApprovalPolicyEnum.AutoSafe, new[] { "run_process" });
                        bool approved = await router.RequestApprovalAsync(MakeRequest("run_process", ToolMutationKind.Mutating), Escalation(ApprovalDecision.Denied, () => escalations++), ct).ConfigureAwait(false);
                        MuxAssert.IsTrue(approved, "allowlisted mutating approved");
                        MuxAssert.AreEqual(0, escalations, "no escalation for allowlisted");
                    }),

                    new TestCaseDescriptor("ApprovalRouting", "AlwaysThisSessionRemembered", "AlwaysThisSession auto-approves all subsequent tools", async (CancellationToken ct) =>
                    {
                        int escalations = 0;
                        ApprovalRouter router = new ApprovalRouter(ApprovalPolicyEnum.Ask);
                        bool first = await router.RequestApprovalAsync(MakeRequest("write_file", ToolMutationKind.Mutating), Escalation(ApprovalDecision.AlwaysThisSession, () => escalations++), ct).ConfigureAwait(false);
                        MuxAssert.IsTrue(first, "first approved");
                        bool second = await router.RequestApprovalAsync(MakeRequest("edit_file", ToolMutationKind.Mutating), Escalation(ApprovalDecision.Denied, () => escalations++), ct).ConfigureAwait(false);
                        MuxAssert.IsTrue(second, "second auto-approved by session grant");
                        MuxAssert.AreEqual(1, escalations, "escalated only once");
                    }),

                    new TestCaseDescriptor("ApprovalRouting", "AlwaysThisToolRemembered", "AlwaysThisTool auto-approves the same tool but not others", async (CancellationToken ct) =>
                    {
                        int escalations = 0;
                        ApprovalRouter router = new ApprovalRouter(ApprovalPolicyEnum.Ask);
                        bool w1 = await router.RequestApprovalAsync(MakeRequest("write_file", ToolMutationKind.Mutating), Escalation(ApprovalDecision.AlwaysThisTool, () => escalations++), ct).ConfigureAwait(false);
                        MuxAssert.IsTrue(w1, "write_file approved + remembered");
                        bool w2 = await router.RequestApprovalAsync(MakeRequest("write_file", ToolMutationKind.Mutating), Escalation(ApprovalDecision.Denied, () => escalations++), ct).ConfigureAwait(false);
                        MuxAssert.IsTrue(w2, "second write_file auto-approved");
                        MuxAssert.AreEqual(1, escalations, "write_file escalated only once");
                        bool e1 = await router.RequestApprovalAsync(MakeRequest("edit_file", ToolMutationKind.Mutating), Escalation(ApprovalDecision.Denied, () => escalations++), ct).ConfigureAwait(false);
                        MuxAssert.IsFalse(e1, "different tool still escalates and is denied");
                        MuxAssert.AreEqual(2, escalations, "edit_file escalated");
                    }),

                    new TestCaseDescriptor("ApprovalRouting", "PromoteToAutoApproveSkipsEscalation", "PromoteToAutoApprove auto-approves subsequent calls", async (CancellationToken ct) =>
                    {
                        int escalations = 0;
                        ApprovalRouter router = new ApprovalRouter(ApprovalPolicyEnum.Ask);
                        router.PromoteToAutoApprove();
                        bool approved = await router.RequestApprovalAsync(MakeRequest("write_file", ToolMutationKind.Mutating), Escalation(ApprovalDecision.Denied, () => escalations++), ct).ConfigureAwait(false);
                        MuxAssert.IsTrue(approved, "approved after promote");
                        MuxAssert.AreEqual(0, escalations, "no escalation after promote");
                    }),

                    new TestCaseDescriptor("ApprovalRouting", "RoutersAreIndependent", "Promoting one router does not affect another (per-job independence)", async (CancellationToken ct) =>
                    {
                        int escalationsA = 0;
                        int escalationsB = 0;
                        ApprovalRouter routerA = new ApprovalRouter(ApprovalPolicyEnum.Ask);
                        ApprovalRouter routerB = new ApprovalRouter(ApprovalPolicyEnum.Ask);
                        routerA.PromoteToAutoApprove();

                        bool a = await routerA.RequestApprovalAsync(MakeRequest("write_file", ToolMutationKind.Mutating), Escalation(ApprovalDecision.Denied, () => escalationsA++), ct).ConfigureAwait(false);
                        bool b = await routerB.RequestApprovalAsync(MakeRequest("write_file", ToolMutationKind.Mutating), Escalation(ApprovalDecision.Denied, () => escalationsB++), ct).ConfigureAwait(false);

                        MuxAssert.IsTrue(a, "A auto-approved");
                        MuxAssert.AreEqual(0, escalationsA, "A did not escalate");
                        MuxAssert.IsFalse(b, "B independently escalated and denied");
                        MuxAssert.AreEqual(1, escalationsB, "B escalated");
                    }),

                    new TestCaseDescriptor("ApprovalRouting", "NullArgumentsThrow", "Null request or escalate throws ArgumentNullException", async (CancellationToken ct) =>
                    {
                        ApprovalRouter router = new ApprovalRouter(ApprovalPolicyEnum.Ask);
                        await MuxAssert.ThrowsAsync<ArgumentNullException>(async () => await router.RequestApprovalAsync(null!, Escalation(ApprovalDecision.Approved, () => { }), ct).ConfigureAwait(false), "null request").ConfigureAwait(false);
                        await MuxAssert.ThrowsAsync<ArgumentNullException>(async () => await router.RequestApprovalAsync(MakeRequest("read_file", ToolMutationKind.ReadOnly), null!, ct).ConfigureAwait(false), "null escalate").ConfigureAwait(false);
                    }),

                    new TestCaseDescriptor("ApprovalRouting", "AgentLoopAskWithNoResponseDeniesToolCall", "Through AgentLoop, an Ask policy with a 'no' response denies the tool call", async (CancellationToken ct) =>
                    {
                        string tempDir = Path.Combine(Path.GetTempPath(), "mux_approval_" + Guid.NewGuid().ToString("N"));
                        Directory.CreateDirectory(tempDir);
                        try
                        {
                            using MockHttpServer server = new MockHttpServer();
                            string toolCall = "{\"choices\":[{\"delta\":{\"tool_calls\":[{\"index\":0,\"id\":\"call_x\",\"function\":{\"name\":\"read_file\",\"arguments\":\"{\\\"file_path\\\":\\\"whatever.txt\\\"}\"}}]},\"finish_reason\":\"tool_calls\"}]}";
                            server.RegisterStreamingResponse("deny me", new List<string> { toolCall });
                            server.RegisterStreamingResponse("tool_call_denied", new List<string> { "{\"choices\":[{\"delta\":{\"content\":\"ok\"},\"finish_reason\":\"stop\"}]}" });
                            server.Start();

                            AgentLoopOptions options = new AgentLoopOptions(AgentTestHarness.BuildMockEndpoint(server.BaseUrl))
                            {
                                ApprovalPolicy = ApprovalPolicyEnum.Ask,
                                MaxIterations = 5,
                                WorkingDirectory = tempDir,
                                PromptUserFunc = (Mux.Core.Models.ToolCall tc) => Task.FromResult("no")
                            };

                            List<AgentEvent> events = await AgentTestHarness.CollectEventsAsync(options, "deny me", ct).ConfigureAwait(false);
                            MuxAssert.IsTrue(events.Any(e => e is ErrorEvent errorEvent && errorEvent.Code == "tool_call_denied"), "tool_call_denied emitted");
                        }
                        finally
                        {
                            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
                        }
                    })
                });
        }

        private static ApprovalRequest MakeRequest(string toolName, ToolMutationKind mutationKind)
        {
            return new ApprovalRequest
            {
                JobId = "j1",
                ToolCallId = "call",
                ToolName = toolName,
                ArgumentsSummary = "{}",
                MutationKind = mutationKind
            };
        }

        private static Func<ApprovalRequest, CancellationToken, Task<ApprovalDecision>> Escalation(ApprovalDecision decision, Action onCalled)
        {
            return (ApprovalRequest request, CancellationToken cancellationToken) =>
            {
                onCalled();
                return Task.FromResult(decision);
            };
        }
    }
}
