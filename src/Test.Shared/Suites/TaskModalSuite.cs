namespace Test.Shared.Suites
{
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Cli.App;
    using Mux.Core.Enums;
    using Mux.Core.Tasks;
    using Touchstone.Core;
    using TUIKit.Input;

    /// <summary>
    /// Touchstone suite for <see cref="TasksModal"/>: human annotation of a job's task plan applies status
    /// changes to the live plan, and Escape closes the modal.
    /// </summary>
    public static class TaskModalSuite
    {
        private const string SuiteId = "TaskModal";

        /// <summary>
        /// Builds the task-modal suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/> for the task-modal cases.</returns>
        public static TestSuiteDescriptor Create()
        {
            return new TestSuiteDescriptor(
                SuiteId,
                "Tasks viewer and human annotation",
                new List<TestCaseDescriptor>
                {
                    new TestCaseDescriptor(SuiteId, "CompleteKeyMarksSelectedTask", "Pressing c marks the selected task completed on the live plan", (CancellationToken ct) =>
                    {
                        TaskPlan plan = BuildPlan();
                        TasksModal modal = new TasksModal(plan);
                        modal.HandleKey(KeyEvent.Char((int)'c'));
                        MuxAssert.AreEqual(AgentTaskStatusEnum.Completed, plan.Snapshot()[0].Status, "first completed");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor(SuiteId, "DownThenBlockMarksSecondTask", "Down then b blocks the second task", (CancellationToken ct) =>
                    {
                        TaskPlan plan = BuildPlan();
                        TasksModal modal = new TasksModal(plan);
                        modal.HandleKey(KeyEvent.Special(KeyCode.Down));
                        modal.HandleKey(KeyEvent.Char((int)'b'));
                        IReadOnlyList<AgentTask> snap = plan.Snapshot();
                        MuxAssert.AreEqual(AgentTaskStatusEnum.Pending, snap[0].Status, "first unchanged");
                        MuxAssert.AreEqual(AgentTaskStatusEnum.Blocked, snap[1].Status, "second blocked");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor(SuiteId, "EscapeClosesModal", "Escape completes the modal", async (CancellationToken ct) =>
                    {
                        TaskPlan plan = BuildPlan();
                        TasksModal modal = new TasksModal(plan);
                        modal.HandleKey(KeyEvent.Special(KeyCode.Escape));
                        await modal.Completion.ConfigureAwait(false);
                        MuxAssert.IsTrue(modal.Completion.IsCompleted, "completion resolved");
                    })
                });
        }

        private static TaskPlan BuildPlan()
        {
            TaskPlan plan = new TaskPlan();
            plan.SetPlan(new List<AgentTask>
            {
                new AgentTask { Id = "t1", Title = "One" },
                new AgentTask { Id = "t2", Title = "Two" }
            });
            return plan;
        }
    }
}
