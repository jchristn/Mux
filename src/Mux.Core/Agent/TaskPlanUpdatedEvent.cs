namespace Mux.Core.Agent
{
    using System;
    using System.Collections.Generic;
    using Mux.Core.Enums;
    using Mux.Core.Tasks;

    /// <summary>
    /// Event emitted when a job's task plan changes: a plan is created, replaced, or cleared, or a single
    /// task is advanced. Carries a full snapshot of the current plan so a consumer has the complete picture
    /// on every event without replaying deltas.
    /// </summary>
    public class TaskPlanUpdatedEvent : AgentEvent
    {
        #region Private-Members

        private IReadOnlyList<AgentTask> _Tasks = new List<AgentTask>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="TaskPlanUpdatedEvent"/> class.
        /// </summary>
        public TaskPlanUpdatedEvent()
        {
            EventType = AgentEventTypeEnum.TaskPlanUpdated;
        }

        #endregion

        #region Public-Members

        /// <summary>
        /// What changed about the plan.
        /// </summary>
        public TaskPlanChangeKindEnum ChangeKind { get; set; }

        /// <summary>
        /// The id of the task a status or note change touched, or null for whole-plan changes
        /// (created, replaced, cleared).
        /// </summary>
        public string? ChangedTaskId { get; set; }

        /// <summary>
        /// A snapshot of the plan's tasks in plan order at the moment of the change. Never null.
        /// </summary>
        public IReadOnlyList<AgentTask> Tasks
        {
            get => _Tasks;
            set => _Tasks = value ?? throw new ArgumentNullException(nameof(Tasks));
        }

        /// <summary>
        /// The number of tasks in <see cref="Tasks"/>.
        /// </summary>
        public int TotalCount => _Tasks.Count;

        /// <summary>
        /// The number of tasks in <see cref="Tasks"/> that have completed.
        /// </summary>
        public int CompletedCount
        {
            get
            {
                int count = 0;
                foreach (AgentTask task in _Tasks)
                {
                    if (task.Status == AgentTaskStatusEnum.Completed) count++;
                }
                return count;
            }
        }

        #endregion
    }
}
