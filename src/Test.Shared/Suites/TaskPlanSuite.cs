namespace Test.Shared.Suites
{
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Enums;
    using Mux.Core.Tasks;
    using Touchstone.Core;

    /// <summary>
    /// Touchstone suite for the task-plan model: <see cref="TaskPlan"/> mutation and timing,
    /// dependency-gated <see cref="TaskPlan.ReadyTasks"/>, snapshot copy-safety, and the full
    /// <see cref="TaskPlanValidator"/> rejection matrix including cycle detection.
    /// </summary>
    public static class TaskPlanSuite
    {
        /// <summary>
        /// Builds the task-plan suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/> for the task-plan cases.</returns>
        public static TestSuiteDescriptor Create()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>
            {
                Case("SetPlanCreatesThenReplaces", "SetPlan reports created, then replaced, and bumps version", (CancellationToken ct) =>
                {
                    TaskPlan plan = new TaskPlan();
                    MuxAssert.IsTrue(plan.IsEmpty, "empty initially");
                    TaskPlanChangeKindEnum first = plan.SetPlan(new List<AgentTask> { MakeTask("t1", "One"), MakeTask("t2", "Two") });
                    MuxAssert.AreEqual(TaskPlanChangeKindEnum.PlanCreated, first, "first is created");
                    MuxAssert.AreEqual(2, plan.TotalCount, "count");
                    int versionAfterCreate = plan.Version;
                    TaskPlanChangeKindEnum second = plan.SetPlan(new List<AgentTask> { MakeTask("t1", "One") });
                    MuxAssert.AreEqual(TaskPlanChangeKindEnum.PlanReplaced, second, "second is replaced");
                    MuxAssert.IsTrue(plan.Version > versionAfterCreate, "version bumped");
                    return Task.CompletedTask;
                }),

                Case("SetPlanEmptyClears", "SetPlan with an empty list clears the plan", (CancellationToken ct) =>
                {
                    TaskPlan plan = new TaskPlan();
                    plan.SetPlan(new List<AgentTask> { MakeTask("t1", "One") });
                    TaskPlanChangeKindEnum change = plan.SetPlan(new List<AgentTask>());
                    MuxAssert.AreEqual(TaskPlanChangeKindEnum.PlanCleared, change, "cleared");
                    MuxAssert.IsTrue(plan.IsEmpty, "empty after clear");
                    return Task.CompletedTask;
                }),

                Case("UpdateTaskStampsTimingAndDuration", "Advancing a task through in-progress to completed stamps timing", (CancellationToken ct) =>
                {
                    TaskPlan plan = new TaskPlan();
                    plan.SetPlan(new List<AgentTask> { MakeTask("t1", "One") });
                    MuxAssert.IsTrue(plan.TryUpdateTask("t1", AgentTaskStatusEnum.InProgress, null, out TaskPlanChangeKindEnum k1), "in progress found");
                    MuxAssert.AreEqual(TaskPlanChangeKindEnum.TaskStatusChanged, k1, "status changed");
                    MuxAssert.IsTrue(plan.TryUpdateTask("t1", AgentTaskStatusEnum.Completed, null, out _), "completed found");
                    IReadOnlyList<AgentTask> snap = plan.Snapshot();
                    MuxAssert.IsNotNull(snap[0].StartedUtc, "startedUtc set");
                    MuxAssert.IsNotNull(snap[0].CompletedUtc, "completedUtc set");
                    MuxAssert.IsNotNull(snap[0].DurationMs, "durationMs set");
                    MuxAssert.AreEqual(1, plan.CompletedCount, "completed count");
                    return Task.CompletedTask;
                }),

                Case("UpdateTaskUnknownIdReturnsFalse", "Updating an unknown task id returns false", (CancellationToken ct) =>
                {
                    TaskPlan plan = new TaskPlan();
                    plan.SetPlan(new List<AgentTask> { MakeTask("t1", "One") });
                    MuxAssert.IsFalse(plan.TryUpdateTask("nope", AgentTaskStatusEnum.Completed, null, out _), "unknown id");
                    return Task.CompletedTask;
                }),

                Case("UpdateTaskFailedRequiresNote", "Marking a task failed without a note throws", (CancellationToken ct) =>
                {
                    TaskPlan plan = new TaskPlan();
                    plan.SetPlan(new List<AgentTask> { MakeTask("t1", "One") });
                    MuxAssert.Throws<System.ArgumentException>(() => plan.TryUpdateTask("t1", AgentTaskStatusEnum.Failed, "   ", out _), "blank note");
                    MuxAssert.IsTrue(plan.TryUpdateTask("t1", AgentTaskStatusEnum.Failed, "disk full", out _), "with note");
                    MuxAssert.AreEqual("disk full", plan.Snapshot()[0].FailureMessage, "failure message");
                    return Task.CompletedTask;
                }),

                Case("NoteOnlyUpdateReportsNoteChange", "Re-applying the same status with a note reports a note update", (CancellationToken ct) =>
                {
                    TaskPlan plan = new TaskPlan();
                    plan.SetPlan(new List<AgentTask> { MakeTask("t1", "One") });
                    plan.TryUpdateTask("t1", AgentTaskStatusEnum.InProgress, null, out _);
                    MuxAssert.IsTrue(plan.TryUpdateTask("t1", AgentTaskStatusEnum.InProgress, "still working", out TaskPlanChangeKindEnum kind), "found");
                    MuxAssert.AreEqual(TaskPlanChangeKindEnum.TaskNoteUpdated, kind, "note updated");
                    return Task.CompletedTask;
                }),

                Case("ReadyTasksGateOnDependencies", "ReadyTasks returns only pending tasks whose dependencies completed", (CancellationToken ct) =>
                {
                    TaskPlan plan = new TaskPlan();
                    AgentTask t2 = MakeTask("t2", "Two");
                    t2.DependsOn = new List<string> { "t1" };
                    plan.SetPlan(new List<AgentTask> { MakeTask("t1", "One"), t2 });
                    IReadOnlyList<AgentTask> readyBefore = plan.ReadyTasks();
                    MuxAssert.AreEqual(1, readyBefore.Count, "only t1 ready");
                    MuxAssert.AreEqual("t1", readyBefore[0].Id, "t1 ready");
                    plan.TryUpdateTask("t1", AgentTaskStatusEnum.Completed, null, out _);
                    IReadOnlyList<AgentTask> readyAfter = plan.ReadyTasks();
                    MuxAssert.AreEqual(1, readyAfter.Count, "t2 ready now");
                    MuxAssert.AreEqual("t2", readyAfter[0].Id, "t2 ready");
                    return Task.CompletedTask;
                }),

                Case("SnapshotIsDeepCopy", "Mutating a snapshot does not affect the plan", (CancellationToken ct) =>
                {
                    TaskPlan plan = new TaskPlan();
                    plan.SetPlan(new List<AgentTask> { MakeTask("t1", "One") });
                    IReadOnlyList<AgentTask> snap = plan.Snapshot();
                    snap[0].Title = "mutated";
                    snap[0].DependsOn.Add("x");
                    IReadOnlyList<AgentTask> again = plan.Snapshot();
                    MuxAssert.AreEqual("One", again[0].Title, "title unchanged");
                    MuxAssert.AreEqual(0, again[0].DependsOn.Count, "dependsOn unchanged");
                    return Task.CompletedTask;
                }),

                Case("ValidateAcceptsValidDag", "A well-formed DAG validates", (CancellationToken ct) =>
                {
                    AgentTask t2 = MakeTask("t2", "Two");
                    t2.DependsOn = new List<string> { "t1" };
                    TaskPlanValidationResult result = TaskPlanValidator.Validate(new List<AgentTask> { MakeTask("t1", "One"), t2 });
                    MuxAssert.IsTrue(result.IsValid, "valid");
                    MuxAssert.AreEqual(0, result.Problems.Count, "no problems");
                    return Task.CompletedTask;
                }),

                Case("ValidateRejectsDuplicateId", "Duplicate ids are rejected", (CancellationToken ct) =>
                {
                    TaskPlanValidationResult result = TaskPlanValidator.Validate(new List<AgentTask> { MakeTask("t1", "One"), MakeTask("t1", "Dup") });
                    MuxAssert.IsFalse(result.IsValid, "invalid");
                    MuxAssert.Contains("Duplicate task id", string.Join(" | ", result.Problems), "duplicate reported");
                    return Task.CompletedTask;
                }),

                Case("ValidateRejectsEmptyIdAndTitle", "Empty id and empty title are rejected", (CancellationToken ct) =>
                {
                    TaskPlanValidationResult result = TaskPlanValidator.Validate(new List<AgentTask> { MakeTask("", ""), MakeTask("t2", "  ") });
                    MuxAssert.IsFalse(result.IsValid, "invalid");
                    MuxAssert.Contains("empty id", string.Join(" | ", result.Problems), "empty id reported");
                    MuxAssert.Contains("empty title", string.Join(" | ", result.Problems), "empty title reported");
                    return Task.CompletedTask;
                }),

                Case("ValidateRejectsUnknownDependency", "A dependency on an unknown id is rejected", (CancellationToken ct) =>
                {
                    AgentTask t1 = MakeTask("t1", "One");
                    t1.DependsOn = new List<string> { "ghost" };
                    TaskPlanValidationResult result = TaskPlanValidator.Validate(new List<AgentTask> { t1 });
                    MuxAssert.IsFalse(result.IsValid, "invalid");
                    MuxAssert.Contains("unknown task 'ghost'", string.Join(" | ", result.Problems), "unknown dep reported");
                    return Task.CompletedTask;
                }),

                Case("ValidateRejectsSelfDependency", "A task depending on itself is rejected", (CancellationToken ct) =>
                {
                    AgentTask t1 = MakeTask("t1", "One");
                    t1.DependsOn = new List<string> { "t1" };
                    TaskPlanValidationResult result = TaskPlanValidator.Validate(new List<AgentTask> { t1 });
                    MuxAssert.IsFalse(result.IsValid, "invalid");
                    MuxAssert.Contains("depends on itself", string.Join(" | ", result.Problems), "self dep reported");
                    return Task.CompletedTask;
                }),

                Case("ValidateRejectsCycle", "A dependency cycle is detected", (CancellationToken ct) =>
                {
                    AgentTask a = MakeTask("a", "A");
                    a.DependsOn = new List<string> { "c" };
                    AgentTask b = MakeTask("b", "B");
                    b.DependsOn = new List<string> { "a" };
                    AgentTask c = MakeTask("c", "C");
                    c.DependsOn = new List<string> { "b" };
                    TaskPlanValidationResult result = TaskPlanValidator.Validate(new List<AgentTask> { a, b, c });
                    MuxAssert.IsFalse(result.IsValid, "invalid");
                    MuxAssert.Contains("dependency cycle", string.Join(" | ", result.Problems), "cycle reported");
                    return Task.CompletedTask;
                }),

                Case("SetPlanThrowsOnInvalid", "SetPlan throws a validation exception for an invalid plan", (CancellationToken ct) =>
                {
                    TaskPlan plan = new TaskPlan();
                    TaskPlanValidationException ex = MuxAssert.Throws<TaskPlanValidationException>(() => plan.SetPlan(new List<AgentTask> { MakeTask("t1", "One"), MakeTask("t1", "Dup") }), "invalid plan");
                    MuxAssert.IsTrue(ex.Problems.Count > 0, "problems carried");
                    return Task.CompletedTask;
                })
            };

            return new TestSuiteDescriptor("TaskPlan", "Task-plan model, timing, readiness, and validation", cases);
        }

        private static AgentTask MakeTask(string id, string title)
        {
            return new AgentTask { Id = id, Title = title };
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, System.Func<CancellationToken, Task> body)
        {
            return new TestCaseDescriptor("TaskPlan", caseId, displayName, body);
        }
    }
}
