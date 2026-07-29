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
    using Mux.Core.Jobs;
    using Touchstone.Core;

    /// <summary>
    /// Touchstone suite verifying that the workspace write lease is enforced through the real agent
    /// tool-execution path: a mutating tool blocks while another holder owns the lease, while a
    /// read-only tool bypasses it and runs to completion regardless.
    /// </summary>
    public static class WriteLeaseIntegrationSuite
    {
        private static readonly TimeSpan Guard = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Builds the write-lease integration suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/> for the integration cases.</returns>
        public static TestSuiteDescriptor Create()
        {
            return new TestSuiteDescriptor(
                "WriteLeaseIntegration",
                "Write-lease enforcement through the agent tool loop",
                new List<TestCaseDescriptor>
                {
                    new TestCaseDescriptor("WriteLeaseIntegration", "MutatingToolWaitsForHeldLease", "A mutating tool blocks until the held lease is released, then executes", async (CancellationToken ct) =>
                    {
                        string tempDir = NewTempDir();
                        string target = Path.Combine(tempDir, "leased.txt");
                        try
                        {
                            using MockHttpServer server = new MockHttpServer();
                            string escaped = target.Replace("\\", "\\\\").Replace("\"", "\\\"");
                            string toolCall = "{\"choices\":[{\"delta\":{\"tool_calls\":[{\"index\":0,\"id\":\"call_w\",\"function\":{\"name\":\"write_file\",\"arguments\":\"{\\\"file_path\\\":\\\"" + escaped + "\\\",\\\"content\\\":\\\"Written by lease test\\\"}\"}}]},\"finish_reason\":\"tool_calls\"}]}";
                            server.RegisterStreamingResponse("write via lease", new List<string> { toolCall });
                            server.RegisterStreamingResponse("Written by lease test", new List<string> { "{\"choices\":[{\"delta\":{\"content\":\"done\"},\"finish_reason\":\"stop\"}]}" });
                            server.Start();

                            WriteLease lease = new WriteLease();
                            WriteLeaseHandle blocker = await lease.AcquireAsync("blocker", ct).ConfigureAwait(false);

                            List<AgentEvent> events = new List<AgentEvent>();
                            using AgentLoop loop = new AgentLoop(BuildOptions(server.BaseUrl, tempDir, lease));
                            Task run = Task.Run(async () =>
                            {
                                await foreach (AgentEvent agentEvent in loop.RunAsync("write via lease", ct).ConfigureAwait(false))
                                {
                                    lock (events)
                                    {
                                        events.Add(agentEvent);
                                    }
                                }
                            });

                            // The loop reaches the mutating tool and blocks acquiring the held lease.
                            await WaitUntilAsync(() => lease.WaitingJobIds.Contains("loop"), ct).ConfigureAwait(false);
                            MuxAssert.IsFalse(run.IsCompleted, "run blocked while lease held");
                            MuxAssert.IsFalse(File.Exists(target), "file not written while blocked");

                            blocker.Dispose();
                            await run.WaitAsync(Guard, ct).ConfigureAwait(false);

                            MuxAssert.IsTrue(File.Exists(target), "file written after lease released");
                            MuxAssert.IsNull(lease.CurrentHolderJobId, "lease free after run");
                            lock (events)
                            {
                                MuxAssert.IsTrue(events.Exists(e => e is ToolCallCompletedEvent), "tool completed event present");
                            }
                        }
                        finally
                        {
                            DeleteTempDir(tempDir);
                        }
                    }),

                    new TestCaseDescriptor("WriteLeaseIntegration", "ReadOnlyToolBypassesHeldLease", "A read-only tool runs to completion even while the lease is held", async (CancellationToken ct) =>
                    {
                        string tempDir = NewTempDir();
                        string source = Path.Combine(tempDir, "source.txt");
                        File.WriteAllText(source, "lease bypass content");
                        try
                        {
                            using MockHttpServer server = new MockHttpServer();
                            string escaped = source.Replace("\\", "\\\\").Replace("\"", "\\\"");
                            string toolCall = "{\"choices\":[{\"delta\":{\"tool_calls\":[{\"index\":0,\"id\":\"call_r\",\"function\":{\"name\":\"read_file\",\"arguments\":\"{\\\"file_path\\\":\\\"" + escaped + "\\\"}\"}}]},\"finish_reason\":\"tool_calls\"}]}";
                            server.RegisterStreamingResponse("read via lease", new List<string> { toolCall });
                            server.RegisterStreamingResponse("lease bypass content", new List<string> { "{\"choices\":[{\"delta\":{\"content\":\"done\"},\"finish_reason\":\"stop\"}]}" });
                            server.Start();

                            WriteLease lease = new WriteLease();
                            WriteLeaseHandle blocker = await lease.AcquireAsync("blocker", ct).ConfigureAwait(false);
                            try
                            {
                                List<AgentEvent> events = await AgentTestHarness.CollectEventsAsync(BuildOptions(server.BaseUrl, tempDir, lease), "read via lease", ct).ConfigureAwait(false);

                                MuxAssert.IsTrue(events.Any(e => e is ToolCallCompletedEvent), "read tool completed despite held lease");
                                ToolCallCompletedEvent completed = (ToolCallCompletedEvent)events.First(e => e is ToolCallCompletedEvent);
                                MuxAssert.IsTrue(completed.Result.Success, "read succeeded");
                                MuxAssert.AreEqual("blocker", lease.CurrentHolderJobId, "lease still held by blocker (read never took it)");
                            }
                            finally
                            {
                                blocker.Dispose();
                            }
                        }
                        finally
                        {
                            DeleteTempDir(tempDir);
                        }
                    })
                });
        }

        private static AgentLoopOptions BuildOptions(string baseUrl, string workingDirectory, WriteLease lease)
        {
            return new AgentLoopOptions(AgentTestHarness.BuildMockEndpoint(baseUrl))
            {
                ApprovalPolicy = ApprovalPolicyEnum.AutoApprove,
                MaxIterations = 5,
                WorkingDirectory = workingDirectory,
                WriteLease = lease,
                JobId = "loop"
            };
        }

        private static string NewTempDir()
        {
            string dir = Path.Combine(Path.GetTempPath(), "mux_lease_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        private static void DeleteTempDir(string dir)
        {
            try
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
            catch (IOException)
            {
            }
        }

        private static async Task WaitUntilAsync(Func<bool> condition, CancellationToken cancellationToken)
        {
            using (CancellationTokenSource timeout = new CancellationTokenSource(Guard))
            using (CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token))
            {
                while (!condition())
                {
                    linked.Token.ThrowIfCancellationRequested();
                    await Task.Delay(20, linked.Token).ConfigureAwait(false);
                }
            }
        }
    }
}
