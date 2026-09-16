namespace Mux.Server.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// The reply to a successful cancel request.
    /// </summary>
    public class RunCancelReply
    {
        /// <summary>Always true on success.</summary>
        public bool Ok { get; set; }

        /// <summary>The run id that was canceled.</summary>
        public string RunId { get; set; } = string.Empty;

        /// <summary>The resulting status ("canceled").</summary>
        public string Status { get; set; } = string.Empty;
    }

    /// <summary>
    /// A single task in a run's task-plan snapshot, projected for the run-state route.
    /// </summary>
    public class RunTaskDto
    {
        /// <summary>The task id.</summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>The task title.</summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>The task status: pending, in_progress, completed, failed, skipped, or blocked.</summary>
        public string Status { get; set; } = string.Empty;
    }

    /// <summary>
    /// A summary row for a tracked run, returned by <c>GET /v1.0/api/runs</c>.
    /// </summary>
    public class RunSummaryDto
    {
        /// <summary>The run correlation id.</summary>
        public string RunId { get; set; } = string.Empty;

        /// <summary>The session id the run belongs to (may be empty).</summary>
        public string SessionId { get; set; } = string.Empty;

        /// <summary>The endpoint the run targets.</summary>
        public string EndpointName { get; set; } = string.Empty;

        /// <summary>The model the run targets.</summary>
        public string Model { get; set; } = string.Empty;

        /// <summary>Lifecycle status: running, awaiting_approval, completed, failed, or canceled.</summary>
        public string Status { get; set; } = string.Empty;

        /// <summary>Whether the run has reached a terminal status.</summary>
        public bool IsTerminal { get; set; }

        /// <summary>When the run started (UTC).</summary>
        public DateTime StartedUtc { get; set; }

        /// <summary>When the run reached a terminal status (UTC), or null while running.</summary>
        public DateTime? CompletedUtc { get; set; }
    }

    /// <summary>
    /// The full state of a tracked run, returned by <c>GET /v1.0/api/runs/{runId}</c>: identity and status,
    /// live counters, the current tool and last error, and the current task-plan checklist.
    /// </summary>
    public class RunStateReply
    {
        /// <summary>The run correlation id.</summary>
        public string RunId { get; set; } = string.Empty;

        /// <summary>The session id the run belongs to (may be empty).</summary>
        public string SessionId { get; set; } = string.Empty;

        /// <summary>The endpoint the run targets.</summary>
        public string EndpointName { get; set; } = string.Empty;

        /// <summary>The model the run targets.</summary>
        public string Model { get; set; } = string.Empty;

        /// <summary>Lifecycle status: running, awaiting_approval, completed, failed, or canceled.</summary>
        public string Status { get; set; } = string.Empty;

        /// <summary>Whether the run has reached a terminal status.</summary>
        public bool IsTerminal { get; set; }

        /// <summary>When the run started (UTC).</summary>
        public DateTime StartedUtc { get; set; }

        /// <summary>When the run reached a terminal status (UTC), or null while running.</summary>
        public DateTime? CompletedUtc { get; set; }

        /// <summary>Iterations completed (populated at run completion).</summary>
        public int IterationsCompleted { get; set; }

        /// <summary>Tool calls handled (populated at run completion).</summary>
        public int ToolCallCount { get; set; }

        /// <summary>Error events observed during the run.</summary>
        public int ErrorCount { get; set; }

        /// <summary>Provider-reported input tokens (populated at run completion).</summary>
        public int InputTokens { get; set; }

        /// <summary>Provider-reported output tokens (populated at run completion).</summary>
        public int OutputTokens { get; set; }

        /// <summary>Provider-reported total tokens (populated at run completion).</summary>
        public int TotalTokens { get; set; }

        /// <summary>Estimated final context tokens (populated at run completion).</summary>
        public int FinalEstimatedTokens { get; set; }

        /// <summary>The tool currently executing, or null when none is in flight.</summary>
        public string? CurrentToolName { get; set; }

        /// <summary>The most recent error message observed, or null.</summary>
        public string? LastError { get; set; }

        /// <summary>Total tasks in the current task-plan snapshot.</summary>
        public int TotalTaskCount { get; set; }

        /// <summary>Completed tasks in the current task-plan snapshot.</summary>
        public int CompletedTaskCount { get; set; }

        /// <summary>The current task-plan checklist (empty when the run has no plan).</summary>
        public List<RunTaskDto> Tasks { get; set; } = new List<RunTaskDto>();
    }
}
