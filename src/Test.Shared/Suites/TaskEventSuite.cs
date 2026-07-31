namespace Test.Shared.Suites
{
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Agent;
    using Mux.Core.Enums;
    using Mux.Core.Tasks;
    using Touchstone.Core;

    /// <summary>
    /// Touchstone suite verifying that the <see cref="AgentLoop"/> emits a <see cref="TaskPlanUpdatedEvent"/>
    /// when a task tool advances the per-job plan, and that a completed run reports a
    /// <see cref="TaskPlanSummary"/>.
    /// </summary>
    public static class TaskEventSuite
    {
        /// <summary>
        /// Builds the task-event suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/> for the task-event cases.</returns>
        public static TestSuiteDescriptor Create()
        {
            return new TestSuiteDescriptor(
                "TaskEvent",
                "AgentLoop task-plan event emission",
                new List<TestCaseDescriptor>
                {
                    new TestCaseDescriptor("TaskEvent", "PlanTasksEmitsTaskPlanUpdated", "A plan_tasks tool call emits a task_plan_updated event and a run task summary", (CancellationToken ct) => PlanTasksEmitsAsync(ct))
                });
        }

        private static async Task PlanTasksEmitsAsync(CancellationToken ct)
        {
            using (MockHttpServer server = new MockHttpServer())
            {
                string toolArguments = "{\\\"tasks\\\":[{\\\"id\\\":\\\"t1\\\",\\\"title\\\":\\\"Study\\\"},{\\\"id\\\":\\\"t2\\\",\\\"title\\\":\\\"Build\\\"}]}";
                string toolCallChunk = "{\"choices\":[{\"delta\":{\"tool_calls\":[{\"index\":0,\"id\":\"call_plan\",\"function\":{\"name\":\"plan_tasks\",\"arguments\":\"" + toolArguments + "\"}}]},\"finish_reason\":\"tool_calls\"}]}";
                string followUpChunk = "{\"choices\":[{\"delta\":{\"content\":\"Plan established.\"},\"finish_reason\":\"stop\"}]}";

                server.RegisterStreamingResponse("plan the work", new List<string> { toolCallChunk });
                server.RegisterStreamingResponse("taskCount", new List<string> { followUpChunk });
                server.Start();

                TaskPlan plan = new TaskPlan();
                AgentLoopOptions options = new AgentLoopOptions(AgentTestHarness.BuildMockEndpoint(server.BaseUrl))
                {
                    ApprovalPolicy = ApprovalPolicyEnum.AutoApprove,
                    MaxIterations = 5,
                    TaskPlan = plan
                };

                List<AgentEvent> events = await AgentTestHarness.CollectEventsAsync(options, "plan the work", ct).ConfigureAwait(false);

                TaskPlanUpdatedEvent? planEvent = events.OfType<TaskPlanUpdatedEvent>().FirstOrDefault();
                MuxAssert.IsNotNull(planEvent, "a TaskPlanUpdatedEvent was emitted");
                MuxAssert.AreEqual(TaskPlanChangeKindEnum.PlanCreated, planEvent!.ChangeKind, "change kind is plan_created");
                MuxAssert.AreEqual(2, planEvent.TotalCount, "two tasks in the snapshot");
                MuxAssert.AreEqual(2, plan.TotalCount, "plan populated on the shared instance");

                RunCompletedEvent? completed = events.OfType<RunCompletedEvent>().FirstOrDefault();
                MuxAssert.IsNotNull(completed, "a RunCompletedEvent");
                MuxAssert.IsNotNull(completed!.TaskSummary, "run reports a task summary");
                MuxAssert.AreEqual(2, completed.TaskSummary!.Total, "summary total");
            }
        }
    }
}
