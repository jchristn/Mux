namespace Mux.Server.Routes
{
    using System;
    using System.Collections.Generic;
    using Mux.Core.Enums;
    using Mux.Core.Runs;
    using Mux.Core.Tasks;
    using Mux.Server.Models;
    using WatsonWebserver;

    /// <summary>
    /// Run lifecycle routes over the server's <see cref="RunRegistry"/>: list active and recently-terminal
    /// runs, inspect a single run's state (including its task-plan checklist), and cancel a run. Cancellation
    /// is cooperative — it trips the run's linked cancellation token so the agent loop and any in-flight tool
    /// stop at the next check.
    /// </summary>
    public sealed class RunRoutes
    {
        #region Private-Members

        private readonly string? _ApiKey;
        private readonly RunRegistry _Runs;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="apiKey">Configured API key, or null for no-auth.</param>
        /// <param name="runs">The server's run registry.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="runs"/> is null.</exception>
        public RunRoutes(string? apiKey, RunRegistry runs)
        {
            _ApiKey = apiKey;
            _Runs = runs ?? throw new ArgumentNullException(nameof(runs));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Register routes.
        /// </summary>
        /// <param name="app">Watson webserver.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="app"/> is null.</exception>
        public void Register(Webserver app)
        {
            if (app == null) throw new ArgumentNullException(nameof(app));

            app.Get("/v1.0/api/runs", (req) =>
            {
                if (!ApiAuth.Authorize(req.Http, _ApiKey)) return System.Threading.Tasks.Task.FromResult((object)new ApiError("Unauthorized", "Authentication required."));

                List<RunSummaryDto> items = new List<RunSummaryDto>();
                foreach (RunHandle handle in _Runs.List())
                {
                    items.Add(ToSummary(handle));
                }

                req.Http.Response.StatusCode = 200;
                return System.Threading.Tasks.Task.FromResult((object)new ListResponse<RunSummaryDto>(items));
            }, Documentation.ApiDoc.RunsList);

            app.Get("/v1.0/api/runs/{runId}", (req) =>
            {
                if (!ApiAuth.Authorize(req.Http, _ApiKey)) return System.Threading.Tasks.Task.FromResult((object)new ApiError("Unauthorized", "Authentication required."));

                string runId = req.Parameters?["runId"] ?? string.Empty;
                if (_Runs.TryGet(runId, out RunHandle? handle) && handle != null)
                {
                    req.Http.Response.StatusCode = 200;
                    return System.Threading.Tasks.Task.FromResult((object)ToState(handle));
                }

                req.Http.Response.StatusCode = 404;
                return System.Threading.Tasks.Task.FromResult((object)new ApiError("NotFound", "No run with id " + runId + "."));
            }, Documentation.ApiDoc.RunsGet);

            app.Post("/v1.0/api/runs/{runId}/cancel", (req) =>
            {
                if (!ApiAuth.Authorize(req.Http, _ApiKey)) return System.Threading.Tasks.Task.FromResult((object)new ApiError("Unauthorized", "Authentication required."));

                string runId = req.Parameters?["runId"] ?? string.Empty;
                if (_Runs.TryCancel(runId))
                {
                    req.Http.Response.StatusCode = 200;
                    return System.Threading.Tasks.Task.FromResult((object)new RunCancelReply { Ok = true, RunId = runId, Status = "canceled" });
                }

                req.Http.Response.StatusCode = 404;
                return System.Threading.Tasks.Task.FromResult((object)new ApiError("NotFound", "No active run with id " + runId + " to cancel."));
            }, Documentation.ApiDoc.RunsCancel);
        }

        #endregion

        #region Private-Methods

        private static RunSummaryDto ToSummary(RunHandle handle)
        {
            return new RunSummaryDto
            {
                RunId = handle.RunId,
                SessionId = handle.SessionId,
                EndpointName = handle.EndpointName,
                Model = handle.Model,
                Status = StatusName(handle.Status),
                IsTerminal = handle.IsTerminal,
                StartedUtc = handle.StartedUtc,
                CompletedUtc = handle.CompletedUtc
            };
        }

        private static RunStateReply ToState(RunHandle handle)
        {
            List<RunTaskDto> tasks = new List<RunTaskDto>();
            foreach (AgentTask task in handle.Tasks)
            {
                tasks.Add(new RunTaskDto { Id = task.Id, Title = task.Title, Status = TaskStatusName(task.Status) });
            }

            return new RunStateReply
            {
                RunId = handle.RunId,
                SessionId = handle.SessionId,
                EndpointName = handle.EndpointName,
                Model = handle.Model,
                Status = StatusName(handle.Status),
                IsTerminal = handle.IsTerminal,
                StartedUtc = handle.StartedUtc,
                CompletedUtc = handle.CompletedUtc,
                IterationsCompleted = handle.IterationsCompleted,
                ToolCallCount = handle.ToolCallCount,
                ErrorCount = handle.ErrorCount,
                InputTokens = handle.InputTokens,
                OutputTokens = handle.OutputTokens,
                TotalTokens = handle.TotalTokens,
                FinalEstimatedTokens = handle.FinalEstimatedTokens,
                CurrentToolName = handle.CurrentToolName,
                LastError = handle.LastError,
                TotalTaskCount = handle.TotalTaskCount,
                CompletedTaskCount = handle.CompletedTaskCount,
                Tasks = tasks
            };
        }

        private static string StatusName(RunStatusEnum status)
        {
            return status switch
            {
                RunStatusEnum.Running => "running",
                RunStatusEnum.AwaitingApproval => "awaiting_approval",
                RunStatusEnum.Completed => "completed",
                RunStatusEnum.Failed => "failed",
                RunStatusEnum.Canceled => "canceled",
                _ => status.ToString().ToLowerInvariant()
            };
        }

        private static string TaskStatusName(AgentTaskStatusEnum status)
        {
            return status switch
            {
                AgentTaskStatusEnum.Pending => "pending",
                AgentTaskStatusEnum.InProgress => "in_progress",
                AgentTaskStatusEnum.Completed => "completed",
                AgentTaskStatusEnum.Failed => "failed",
                AgentTaskStatusEnum.Skipped => "skipped",
                AgentTaskStatusEnum.Blocked => "blocked",
                _ => status.ToString().ToLowerInvariant()
            };
        }

        #endregion
    }
}
