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
    using Mux.Core.Models;
    using Touchstone.Core;

    /// <summary>
    /// Touchstone suite for the Mux.Core job manager and scheduler.
    /// </summary>
    public static class JobManagerSuite
    {
        /// <summary>
        /// Builds the job-manager suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/> for job-manager cases.</returns>
        public static TestSuiteDescriptor Create()
        {
            return new TestSuiteDescriptor(
                "JobManager",
                "Job manager scheduling and lifecycle behavior",
                new List<TestCaseDescriptor>
                {
                    new TestCaseDescriptor(
                        "JobManager",
                        "SubmitRunsToCompletion",
                        "Submit starts a job and records completion",
                        async (CancellationToken ct) =>
                        {
                            await using (JobManager manager = new JobManager(CompleteImmediatelyAsync, maxConcurrency: 1))
                            {
                                Job job = await manager.SubmitAsync("hello", ct).ConfigureAwait(false);

                                await WaitForStateAsync(job, JobState.Completed, ct).ConfigureAwait(false);

                                MuxAssert.AreEqual(JobState.Completed, job.State, "state");
                                MuxAssert.AreEqual(1, job.RunCount, "run count");
                                MuxAssert.AreEqual(1, job.Transcript.Count, "transcript count");
                            }
                        }),

                    new TestCaseDescriptor(
                        "JobManager",
                        "ConcurrencyCapQueuesOverflow",
                        "The N+1th job stays queued until a slot frees",
                        async (CancellationToken ct) =>
                        {
                            TaskCompletionSource<bool> firstStarted = NewSignal();
                            TaskCompletionSource<bool> releaseFirst = NewSignal();

                            async IAsyncEnumerable<AgentEvent> Runner(Job job, string prompt, [EnumeratorCancellation] CancellationToken cancellationToken)
                            {
                                if (string.Equals(prompt, "one", StringComparison.Ordinal))
                                {
                                    firstStarted.TrySetResult(true);
                                    await WaitForSignalAsync(releaseFirst.Task, cancellationToken).ConfigureAwait(false);
                                }

                                yield return CompletedEvent();
                            }

                            await using (JobManager manager = new JobManager(Runner, maxConcurrency: 1))
                            {
                                Job first = await manager.SubmitAsync("one", ct).ConfigureAwait(false);
                                await WaitForSignalAsync(firstStarted.Task, ct).ConfigureAwait(false);

                                Job second = await manager.SubmitAsync("two", ct).ConfigureAwait(false);

                                MuxAssert.AreEqual(JobState.Running, first.State, "first running");
                                MuxAssert.AreEqual(JobState.Queued, second.State, "second queued");

                                releaseFirst.TrySetResult(true);
                                await WaitForStateAsync(second, JobState.Completed, ct).ConfigureAwait(false);

                                MuxAssert.AreEqual(JobState.Completed, first.State, "first completed");
                                MuxAssert.AreEqual(JobState.Completed, second.State, "second completed");
                            }
                        }),

                    new TestCaseDescriptor(
                        "JobManager",
                        "MaxConcurrencyAllowsThreeConcurrentJobs",
                        "A max concurrency of three lets three fake-agent jobs overlap while queueing the fourth",
                        async (CancellationToken ct) =>
                        {
                            object syncRoot = new object();
                            List<string> startedPrompts = new List<string>();
                            TaskCompletionSource<bool> threeStarted = NewSignal();
                            TaskCompletionSource<bool> releaseAll = NewSignal();

                            async IAsyncEnumerable<AgentEvent> Runner(Job job, string prompt, [EnumeratorCancellation] CancellationToken cancellationToken)
                            {
                                lock (syncRoot)
                                {
                                    startedPrompts.Add(prompt);
                                    if (startedPrompts.Count == 3)
                                    {
                                        threeStarted.TrySetResult(true);
                                    }
                                }

                                await WaitForSignalAsync(releaseAll.Task, cancellationToken).ConfigureAwait(false);
                                yield return CompletedEvent();
                            }

                            await using (JobManager manager = new JobManager(Runner, maxConcurrency: 3))
                            {
                                Job first = await manager.SubmitAsync("one", ct).ConfigureAwait(false);
                                Job second = await manager.SubmitAsync("two", ct).ConfigureAwait(false);
                                Job third = await manager.SubmitAsync("three", ct).ConfigureAwait(false);
                                Job fourth = await manager.SubmitAsync("four", ct).ConfigureAwait(false);

                                await WaitForSignalAsync(threeStarted.Task, ct).ConfigureAwait(false);

                                MuxAssert.AreEqual(JobState.Running, first.State, "first running");
                                MuxAssert.AreEqual(JobState.Running, second.State, "second running");
                                MuxAssert.AreEqual(JobState.Running, third.State, "third running");
                                MuxAssert.AreEqual(JobState.Queued, fourth.State, "fourth queued");

                                releaseAll.TrySetResult(true);
                                await WaitForStateAsync(fourth, JobState.Completed, ct).ConfigureAwait(false);

                                MuxAssert.AreEqual(JobState.Completed, first.State, "first completed");
                                MuxAssert.AreEqual(JobState.Completed, second.State, "second completed");
                                MuxAssert.AreEqual(JobState.Completed, third.State, "third completed");
                                MuxAssert.AreEqual(JobState.Completed, fourth.State, "fourth completed");
                            }
                        }),

                    new TestCaseDescriptor(
                        "JobManager",
                        "CancelFreesSchedulerSlot",
                        "Cancelling a running job frees a slot for the next queued job",
                        async (CancellationToken ct) =>
                        {
                            TaskCompletionSource<bool> firstStarted = NewSignal();

                            async IAsyncEnumerable<AgentEvent> Runner(Job job, string prompt, [EnumeratorCancellation] CancellationToken cancellationToken)
                            {
                                if (string.Equals(prompt, "one", StringComparison.Ordinal))
                                {
                                    firstStarted.TrySetResult(true);
                                    await Task.Delay(TimeSpan.FromMinutes(5), cancellationToken).ConfigureAwait(false);
                                }

                                yield return CompletedEvent();
                            }

                            await using (JobManager manager = new JobManager(Runner, maxConcurrency: 1))
                            {
                                Job first = await manager.SubmitAsync("one", ct).ConfigureAwait(false);
                                await WaitForSignalAsync(firstStarted.Task, ct).ConfigureAwait(false);
                                Job second = await manager.SubmitAsync("two", ct).ConfigureAwait(false);

                                MuxAssert.AreEqual(JobState.Queued, second.State, "second queued");

                                bool cancelled = await manager.CancelAsync(first.Id, ct).ConfigureAwait(false);
                                MuxAssert.IsTrue(cancelled, "cancelled");
                                await WaitForStateAsync(second, JobState.Completed, ct).ConfigureAwait(false);

                                MuxAssert.AreEqual(JobState.Cancelled, first.State, "first cancelled");
                                MuxAssert.AreEqual(JobState.Completed, second.State, "second completed");
                            }
                        }),

                    new TestCaseDescriptor(
                        "JobManager",
                        "ReorderChangesQueueOrder",
                        "Reordering queued jobs changes the next job to run",
                        async (CancellationToken ct) =>
                        {
                            object syncRoot = new object();
                            List<string> startedPrompts = new List<string>();
                            TaskCompletionSource<bool> firstStarted = NewSignal();
                            TaskCompletionSource<bool> releaseFirst = NewSignal();

                            async IAsyncEnumerable<AgentEvent> Runner(Job job, string prompt, [EnumeratorCancellation] CancellationToken cancellationToken)
                            {
                                lock (syncRoot)
                                {
                                    startedPrompts.Add(prompt);
                                }

                                if (string.Equals(prompt, "one", StringComparison.Ordinal))
                                {
                                    firstStarted.TrySetResult(true);
                                    await WaitForSignalAsync(releaseFirst.Task, cancellationToken).ConfigureAwait(false);
                                }

                                yield return CompletedEvent();
                            }

                            await using (JobManager manager = new JobManager(Runner, maxConcurrency: 1))
                            {
                                Job first = await manager.SubmitAsync("one", ct).ConfigureAwait(false);
                                await WaitForSignalAsync(firstStarted.Task, ct).ConfigureAwait(false);
                                Job second = await manager.SubmitAsync("two", ct).ConfigureAwait(false);
                                Job third = await manager.SubmitAsync("three", ct).ConfigureAwait(false);

                                bool moved = await manager.ReorderAsync(third.Id, 1, ct).ConfigureAwait(false);
                                MuxAssert.IsTrue(moved, "moved");

                                releaseFirst.TrySetResult(true);
                                await WaitForStateAsync(second, JobState.Completed, ct).ConfigureAwait(false);
                                await WaitForStateAsync(third, JobState.Completed, ct).ConfigureAwait(false);

                                lock (syncRoot)
                                {
                                    MuxAssert.AreEqual("one", startedPrompts[0], "prompt 0");
                                    MuxAssert.AreEqual("three", startedPrompts[1], "prompt 1");
                                    MuxAssert.AreEqual("two", startedPrompts[2], "prompt 2");
                                }

                                MuxAssert.AreEqual(JobState.Completed, first.State, "first completed");
                            }
                        }),

                    new TestCaseDescriptor(
                        "JobManager",
                        "NewJobForksFocusedHistory",
                        "New jobs default to a copy of the focused job history",
                        async (CancellationToken ct) =>
                        {
                            List<ConversationMessage> seedHistory = new List<ConversationMessage>
                            {
                                new ConversationMessage { Role = RoleEnum.User, Content = "seed" }
                            };

                            await using (JobManager manager = new JobManager(CompleteImmediatelyAsync, maxConcurrency: 1))
                            {
                                Job first = await manager.SubmitAsync("first", ApprovalPolicyEnum.Ask, seedHistory, ct).ConfigureAwait(false);
                                await WaitForStateAsync(first, JobState.Completed, ct).ConfigureAwait(false);
                                bool focused = manager.Focus(first.Id);
                                MuxAssert.IsTrue(focused, "focused");

                                Job second = await manager.SubmitAsync("second", ct).ConfigureAwait(false);
                                await WaitForStateAsync(second, JobState.Completed, ct).ConfigureAwait(false);

                                List<ConversationMessage> secondHistory = second.ConversationHistory;
                                MuxAssert.AreEqual(1, secondHistory.Count, "forked history count");
                                MuxAssert.AreEqual("seed", secondHistory[0].Content, "forked content");

                                seedHistory[0].Content = "mutated";
                                MuxAssert.AreEqual("seed", second.ConversationHistory[0].Content, "history copied");
                            }
                        }),

                    new TestCaseDescriptor(
                        "JobManager",
                        "FollowUpAppendsAfterCurrentTurn",
                        "Follow-ups are appended after the active turn completes",
                        async (CancellationToken ct) =>
                        {
                            object syncRoot = new object();
                            List<string> prompts = new List<string>();
                            TaskCompletionSource<bool> firstStarted = NewSignal();
                            TaskCompletionSource<bool> secondStarted = NewSignal();
                            TaskCompletionSource<bool> releaseFirst = NewSignal();

                            async IAsyncEnumerable<AgentEvent> Runner(Job job, string prompt, [EnumeratorCancellation] CancellationToken cancellationToken)
                            {
                                lock (syncRoot)
                                {
                                    prompts.Add(prompt);
                                }

                                if (string.Equals(prompt, "first", StringComparison.Ordinal))
                                {
                                    firstStarted.TrySetResult(true);
                                    await WaitForSignalAsync(releaseFirst.Task, cancellationToken).ConfigureAwait(false);
                                }
                                else if (string.Equals(prompt, "second", StringComparison.Ordinal))
                                {
                                    secondStarted.TrySetResult(true);
                                }

                                yield return CompletedEvent();
                            }

                            await using (JobManager manager = new JobManager(Runner, maxConcurrency: 1))
                            {
                                Job job = await manager.SubmitAsync("first", ct).ConfigureAwait(false);
                                await WaitForSignalAsync(firstStarted.Task, ct).ConfigureAwait(false);

                                bool added = await manager.AddFollowUpAsync(job.Id, "second", ct).ConfigureAwait(false);
                                MuxAssert.IsTrue(added, "follow-up added");

                                lock (syncRoot)
                                {
                                    MuxAssert.AreEqual(1, prompts.Count, "second not started before release");
                                }

                                releaseFirst.TrySetResult(true);
                                await WaitForSignalAsync(secondStarted.Task, ct).ConfigureAwait(false);
                                await WaitForStateAsync(job, JobState.Completed, ct).ConfigureAwait(false);

                                lock (syncRoot)
                                {
                                    MuxAssert.AreEqual("first", prompts[0], "prompt 0");
                                    MuxAssert.AreEqual("second", prompts[1], "prompt 1");
                                }

                                MuxAssert.AreEqual(2, job.RunCount, "run count");
                            }
                        }),

                    new TestCaseDescriptor(
                        "JobManager",
                        "DisposeCancelsAll",
                        "Disposing the manager cancels active and queued jobs",
                        async (CancellationToken ct) =>
                        {
                            TaskCompletionSource<bool> firstStarted = NewSignal();

                            async IAsyncEnumerable<AgentEvent> Runner(Job job, string prompt, [EnumeratorCancellation] CancellationToken cancellationToken)
                            {
                                firstStarted.TrySetResult(true);
                                await Task.Delay(TimeSpan.FromMinutes(5), cancellationToken).ConfigureAwait(false);
                                yield return CompletedEvent();
                            }

                            JobManager manager = new JobManager(Runner, maxConcurrency: 1);
                            Job first = await manager.SubmitAsync("one", ct).ConfigureAwait(false);
                            await WaitForSignalAsync(firstStarted.Task, ct).ConfigureAwait(false);
                            Job second = await manager.SubmitAsync("two", ct).ConfigureAwait(false);

                            await manager.DisposeAsync().ConfigureAwait(false);

                            MuxAssert.AreEqual(JobState.Cancelled, first.State, "first cancelled");
                            MuxAssert.AreEqual(JobState.Cancelled, second.State, "second cancelled");
                        })
                });
        }

        private static async IAsyncEnumerable<AgentEvent> CompleteImmediatelyAsync(
            Job job,
            string prompt,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.CompletedTask.ConfigureAwait(false);
            yield return CompletedEvent();
        }

        private static RunCompletedEvent CompletedEvent()
        {
            return new RunCompletedEvent
            {
                RunId = Guid.NewGuid().ToString("N"),
                Status = "completed",
                IterationsCompleted = 1,
                DurationMs = 1
            };
        }

        private static TaskCompletionSource<bool> NewSignal()
        {
            return new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
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
