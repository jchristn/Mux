namespace Test.Shared.Suites
{
    using System.Collections.Generic;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Enums;
    using Mux.Core.Models;
    using Mux.Core.Tasks;
    using Mux.Core.Tools;
    using Mux.Core.Tools.Tools;
    using Touchstone.Core;

    /// <summary>
    /// Touchstone suite for the model-facing task tools <see cref="PlanTasksTool"/> and
    /// <see cref="UpdateTaskTool"/>: valid and invalid inputs, their error payloads, their registration in
    /// <see cref="BuiltInToolRegistry"/> as read-only, and the settings gate.
    /// </summary>
    public static class TaskToolsSuite
    {
        /// <summary>
        /// Builds the task-tools suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/> for the task-tool cases.</returns>
        public static TestSuiteDescriptor Create()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>
            {
                Case("PlanTasksAcceptsValidPlan", "plan_tasks applies a valid plan to the injected TaskPlan", async (CancellationToken ct) =>
                {
                    TaskPlan plan = new TaskPlan();
                    PlanTasksTool tool = new PlanTasksTool(plan);
                    JsonElement args = Args("{\"tasks\":[{\"id\":\"t1\",\"title\":\"One\"},{\"id\":\"t2\",\"title\":\"Two\",\"dependsOn\":[\"t1\"]}]}");
                    ToolResult result = await tool.ExecuteAsync("c1", args, ".", ct).ConfigureAwait(false);
                    MuxAssert.IsTrue(result.Success, "success");
                    MuxAssert.AreEqual(2, plan.TotalCount, "plan populated");
                }),

                Case("PlanTasksRejectsInvalidPlan", "plan_tasks returns an invalid_plan payload for a bad plan", async (CancellationToken ct) =>
                {
                    TaskPlan plan = new TaskPlan();
                    PlanTasksTool tool = new PlanTasksTool(plan);
                    JsonElement args = Args("{\"tasks\":[{\"id\":\"t1\",\"title\":\"One\"},{\"id\":\"t1\",\"title\":\"Dup\"}]}");
                    ToolResult result = await tool.ExecuteAsync("c1", args, ".", ct).ConfigureAwait(false);
                    MuxAssert.IsFalse(result.Success, "not success");
                    MuxAssert.Contains("invalid_plan", result.Content, "invalid_plan error");
                    MuxAssert.IsTrue(plan.IsEmpty, "plan not applied");
                }),

                Case("UpdateTaskAdvancesTask", "update_task advances a task's status", async (CancellationToken ct) =>
                {
                    TaskPlan plan = new TaskPlan();
                    plan.SetPlan(new List<AgentTask> { new AgentTask { Id = "t1", Title = "One" } });
                    UpdateTaskTool tool = new UpdateTaskTool(plan);
                    ToolResult result = await tool.ExecuteAsync("c1", Args("{\"id\":\"t1\",\"status\":\"in_progress\"}"), ".", ct).ConfigureAwait(false);
                    MuxAssert.IsTrue(result.Success, "success");
                    MuxAssert.AreEqual(AgentTaskStatusEnum.InProgress, plan.Snapshot()[0].Status, "status advanced");
                }),

                Case("UpdateTaskUnknownIdErrors", "update_task returns unknown_task for a missing id", async (CancellationToken ct) =>
                {
                    TaskPlan plan = new TaskPlan();
                    plan.SetPlan(new List<AgentTask> { new AgentTask { Id = "t1", Title = "One" } });
                    UpdateTaskTool tool = new UpdateTaskTool(plan);
                    ToolResult result = await tool.ExecuteAsync("c1", Args("{\"id\":\"ghost\",\"status\":\"completed\"}"), ".", ct).ConfigureAwait(false);
                    MuxAssert.IsFalse(result.Success, "not success");
                    MuxAssert.Contains("unknown_task", result.Content, "unknown_task error");
                }),

                Case("UpdateTaskFailedRequiresNote", "update_task requires a note when marking a task failed", async (CancellationToken ct) =>
                {
                    TaskPlan plan = new TaskPlan();
                    plan.SetPlan(new List<AgentTask> { new AgentTask { Id = "t1", Title = "One" } });
                    UpdateTaskTool tool = new UpdateTaskTool(plan);
                    ToolResult withoutNote = await tool.ExecuteAsync("c1", Args("{\"id\":\"t1\",\"status\":\"failed\"}"), ".", ct).ConfigureAwait(false);
                    MuxAssert.IsFalse(withoutNote.Success, "no note fails");
                    MuxAssert.Contains("note_required", withoutNote.Content, "note_required error");
                    ToolResult withNote = await tool.ExecuteAsync("c1", Args("{\"id\":\"t1\",\"status\":\"failed\",\"note\":\"boom\"}"), ".", ct).ConfigureAwait(false);
                    MuxAssert.IsTrue(withNote.Success, "with note succeeds");
                }),

                Case("TaskToolsDisabledWhenPlanNull", "The task tools report disabled when no plan is bound", async (CancellationToken ct) =>
                {
                    PlanTasksTool plan = new PlanTasksTool(null);
                    ToolResult result = await plan.ExecuteAsync("c1", Args("{\"tasks\":[]}"), ".", ct).ConfigureAwait(false);
                    MuxAssert.IsFalse(result.Success, "disabled");
                    MuxAssert.Contains("task_planning_disabled", result.Content, "disabled error");
                }),

                Case("RegistryRegistersTaskToolsReadOnly", "The registry registers both task tools as read-only when enabled", (CancellationToken ct) =>
                {
                    BuiltInToolRegistry registry = new BuiltInToolRegistry(new MuxSettings(), new TaskPlan());
                    MuxAssert.IsTrue(registry.HasTool("plan_tasks"), "plan_tasks registered");
                    MuxAssert.IsTrue(registry.HasTool("update_task"), "update_task registered");
                    MuxAssert.AreEqual(ToolMutationKind.ReadOnly, registry.GetMutationKind("plan_tasks"), "plan_tasks read-only");
                    MuxAssert.AreEqual(ToolMutationKind.ReadOnly, registry.GetMutationKind("update_task"), "update_task read-only");
                    return Task.CompletedTask;
                }),

                Case("RegistryOmitsTaskToolsWhenDisabled", "The registry omits the task tools when task planning is disabled", (CancellationToken ct) =>
                {
                    BuiltInToolRegistry registry = new BuiltInToolRegistry(new MuxSettings { TaskPlanningEnabled = false }, new TaskPlan());
                    MuxAssert.IsFalse(registry.HasTool("plan_tasks"), "plan_tasks omitted");
                    MuxAssert.IsFalse(registry.HasTool("update_task"), "update_task omitted");
                    return Task.CompletedTask;
                })
            };

            return new TestSuiteDescriptor("TaskTools", "Model-facing task tools and registration", cases);
        }

        private static JsonElement Args(string json)
        {
            return JsonDocument.Parse(json).RootElement.Clone();
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, System.Func<CancellationToken, Task> body)
        {
            return new TestCaseDescriptor("TaskTools", caseId, displayName, body);
        }
    }
}
