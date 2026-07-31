namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Runtime.CompilerServices;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Agent;
    using Mux.Core.Enums;
    using Mux.Core.Jobs;
    using Mux.Core.Tasks;
    using Touchstone.Core;

    /// <summary>
    /// Touchstone suite for <see cref="TaskOrchestrator"/>: dependency-ordered dispatch, parallel fan-out,
    /// a failed dependency stalling its dependents, and cancellation propagating to child jobs. Driven
    /// against fake agent runners like <see cref="JobManagerSuite"/>. Write-lease serialization is inherited
    /// from <see cref="JobManager"/> and covered by the write-lease suites.
    /// </summary>
    public static class TaskOrchestratorSuite
    {
        private const string SuiteId = "TaskOrchestrator";

        /// <summary>
        /// Builds the task-orchestrator suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/> for the orchestrator cases.</returns>
        public static TestSuiteDescriptor Create()
        {
            return new TestSuiteDescriptor(
                SuiteId,
                "Task orchestration over the job manager",
                new List<TestCaseDescriptor>
                {
                    new TestCaseDescriptor(SuiteId, "IndependentTasksAllComplete", "Independent tasks all run and complete", (CancellationToken ct) => IndependentAllCompleteAsync(ct)),
                    new TestCaseDescriptor(SuiteId, "DependencyChainRunsInOrder", "A dependent task runs only after its dependency completes", (CancellationToken ct) => ChainRunsInOrderAsync(ct)),
                    new TestCaseDescriptor(SuiteId, "FailedTaskStallsDependents", "A failed dependency leaves its dependent pending", (CancellationToken ct) => FailedStallsAsync(ct)),
                    new TestCaseDescriptor(SuiteId, "CancellationCancelsChildJobs", "Cancelling the orchestrator cancels the running child job", (CancellationToken ct) => CancellationAsync(ct))
                });
        }

        private static async Task IndependentAllCompleteAsync(CancellationToken ct)
        {
            OrderRecorder recorder = new OrderRecorder();
            await using (JobManager manager = new JobManager(recorder.Run, maxConcurrency: 3))
            {
                TaskPlan plan = new TaskPlan();
                plan.SetPlan(new List<AgentTask>
                {
                    new AgentTask { Id = "t1", Title = "Alpha" },
                    new AgentTask { Id = "t2", Title = "Beta" },
                    new AgentTask { Id = "t3", Title = "Gamma" }
                });

                await new TaskOrchestrator(plan, manager).RunAsync(ct).ConfigureAwait(false);

                foreach (AgentTask task in plan.Snapshot())
                {
                    MuxAssert.AreEqual(AgentTaskStatusEnum.Completed, task.Status, task.Id + " completed");
                }
            }
        }

        private static async Task ChainRunsInOrderAsync(CancellationToken ct)
        {
            OrderRecorder recorder = new OrderRecorder();
            await using (JobManager manager = new JobManager(recorder.Run, maxConcurrency: 3))
            {
                TaskPlan plan = new TaskPlan();
                plan.SetPlan(new List<AgentTask>
                {
                    new AgentTask { Id = "t1", Title = "First" },
                    new AgentTask { Id = "t2", Title = "Second", DependsOn = new List<string> { "t1" } }
                });

                await new TaskOrchestrator(plan, manager).RunAsync(ct).ConfigureAwait(false);

                IReadOnlyList<AgentTask> snap = plan.Snapshot();
                MuxAssert.AreEqual(AgentTaskStatusEnum.Completed, snap[0].Status, "t1 completed");
                MuxAssert.AreEqual(AgentTaskStatusEnum.Completed, snap[1].Status, "t2 completed");
                MuxAssert.IsTrue(recorder.IndexOf("First") < recorder.IndexOf("Second"), "First started before Second");
            }
        }

        private static async Task FailedStallsAsync(CancellationToken ct)
        {
            OrderRecorder recorder = new OrderRecorder();
            await using (JobManager manager = new JobManager(recorder.Run, maxConcurrency: 3))
            {
                TaskPlan plan = new TaskPlan();
                plan.SetPlan(new List<AgentTask>
                {
                    new AgentTask { Id = "t1", Title = "FAIL now" },
                    new AgentTask { Id = "t2", Title = "Dependent", DependsOn = new List<string> { "t1" } }
                });

                await new TaskOrchestrator(plan, manager).RunAsync(ct).ConfigureAwait(false);

                IReadOnlyList<AgentTask> snap = plan.Snapshot();
                MuxAssert.AreEqual(AgentTaskStatusEnum.Failed, snap[0].Status, "t1 failed");
                MuxAssert.AreEqual(AgentTaskStatusEnum.Pending, snap[1].Status, "t2 still pending");
            }
        }

        private static async Task CancellationAsync(CancellationToken ct)
        {
            OrderRecorder recorder = new OrderRecorder();
            await using (JobManager manager = new JobManager(recorder.Run, maxConcurrency: 3))
            {
                TaskPlan plan = new TaskPlan();
                plan.SetPlan(new List<AgentTask> { new AgentTask { Id = "t1", Title = "HANG here" } });

                using (CancellationTokenSource orchCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    Task run = new TaskOrchestrator(plan, manager).RunAsync(orchCts.Token);

                    await WaitForSignalAsync(recorder.HangStarted.Task, ct).ConfigureAwait(false);
                    Job hangJob = manager.Jobs[0];

                    orchCts.Cancel();
                    await MuxAssert.ThrowsAsync<OperationCanceledException>(() => run, "orchestration cancelled").ConfigureAwait(false);
                    await WaitForStateAsync(hangJob, JobState.Cancelled, ct).ConfigureAwait(false);
                    MuxAssert.AreEqual(JobState.Cancelled, hangJob.State, "child job cancelled");
                }
            }
        }

        private sealed class OrderRecorder
        {
            private readonly object _Sync = new object();
            private readonly List<string> _Started = new List<string>();

            public TaskCompletionSource<bool> HangStarted { get; } = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            public int IndexOf(string prompt)
            {
                lock (_Sync)
                {
                    return _Started.IndexOf(prompt);
                }
            }

            public async IAsyncEnumerable<AgentEvent> Run(Job job, string prompt, [EnumeratorCancellation] CancellationToken cancellationToken)
            {
                lock (_Sync)
                {
                    _Started.Add(prompt);
                }

                if (prompt.StartsWith("FAIL", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("Simulated task failure.");
                }

                if (prompt.StartsWith("HANG", StringComparison.Ordinal))
                {
                    HangStarted.TrySetResult(true);
                    await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
                }

                await Task.CompletedTask.ConfigureAwait(false);
                yield return new RunCompletedEvent { RunId = Guid.NewGuid().ToString("N"), Status = "completed", IterationsCompleted = 1, DurationMs = 1 };
            }
        }

        private static async Task WaitForSignalAsync(Task signal, CancellationToken cancellationToken)
        {
            using (CancellationTokenSource timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30)))
            using (CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token))
            {
                await signal.WaitAsync(linked.Token).ConfigureAwait(false);
            }
        }

        private static async Task WaitForStateAsync(Job job, JobState expectedState, CancellationToken cancellationToken)
        {
            using (CancellationTokenSource timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30)))
            using (CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token))
            {
                while (!linked.Token.IsCancellationRequested)
                {
                    if (job.State == expectedState)
                    {
                        return;
                    }

                    await Task.Delay(10, linked.Token).ConfigureAwait(false);
                }

                linked.Token.ThrowIfCancellationRequested();
            }
        }
    }
}
