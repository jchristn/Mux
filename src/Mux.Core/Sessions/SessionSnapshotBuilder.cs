namespace Mux.Core.Sessions
{
    using System;
    using System.Collections.Generic;
    using Mux.Core.Jobs;
    using Mux.Core.Models;

    /// <summary>
    /// Builds a <see cref="SessionSnapshot"/> from a live <see cref="JobManager"/> plus session metadata
    /// and prompt history. Pure and side-effect free; the caller persists the result via
    /// <see cref="SessionStore"/>. Kept in <c>Mux.Core</c> so headless and orchestration callers can
    /// snapshot a session without the Cli.
    /// </summary>
    public static class SessionSnapshotBuilder
    {
        /// <summary>
        /// Builds a snapshot of the manager's jobs and the supplied session metadata.
        /// </summary>
        /// <param name="manager">The job manager to snapshot. Must not be null.</param>
        /// <param name="id">The session id.</param>
        /// <param name="title">The session title.</param>
        /// <param name="endpointName">The effective endpoint name.</param>
        /// <param name="model">The effective model.</param>
        /// <param name="promptHistory">The prompt-history entries to persist (oldest-first).</param>
        /// <param name="nowUtc">The timestamp to stamp as <c>UpdatedUtc</c>.</param>
        /// <param name="workingDirectory">The working directory (project root) to persist, so the session can be resumed against the same directory on any surface. Optional.</param>
        /// <param name="labels">Freeform labels to carry onto the snapshot. Optional; null seeds none. The persist path preserves any labels already stored.</param>
        /// <param name="tags">Key/value tags to carry onto the snapshot. Optional; null seeds none. The persist path preserves any tags already stored.</param>
        /// <returns>The populated snapshot.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="manager"/> is null.</exception>
        public static SessionSnapshot Build(
            JobManager manager,
            string id,
            string title,
            string endpointName,
            string model,
            IEnumerable<string>? promptHistory,
            DateTime nowUtc,
            string? workingDirectory = null,
            IEnumerable<string>? labels = null,
            IEnumerable<SessionTag>? tags = null)
        {
            if (manager is null) throw new ArgumentNullException(nameof(manager));

            SessionSnapshot snapshot = new SessionSnapshot
            {
                Id = id ?? string.Empty,
                Title = title ?? string.Empty,
                EndpointName = endpointName ?? string.Empty,
                Model = model ?? string.Empty,
                WorkingDirectory = workingDirectory ?? string.Empty,
                CreatedUtc = nowUtc,
                UpdatedUtc = nowUtc
            };

            if (labels != null)
            {
                foreach (string label in labels)
                {
                    if (!string.IsNullOrWhiteSpace(label)) snapshot.Labels.Add(label);
                }
            }

            if (tags != null)
            {
                foreach (SessionTag tag in tags)
                {
                    if (tag != null && !string.IsNullOrWhiteSpace(tag.Key)) snapshot.Tags.Add(tag);
                }
            }

            if (promptHistory != null)
            {
                foreach (string entry in promptHistory)
                {
                    snapshot.PromptHistory.Add(entry);
                }
            }

            foreach (Job job in manager.Jobs)
            {
                PersistedJobSnapshot persisted = new PersistedJobSnapshot
                {
                    Id = job.Id,
                    Title = job.Title,
                    Prompt = job.Prompt,
                    State = job.State.ToString(),
                    ApprovalPolicy = job.ApprovalPolicy.ToString(),
                    ConversationHistory = new List<ConversationMessage>(job.ConversationHistory),
                    TaskPlan = new List<Mux.Core.Tasks.AgentTask>(job.TaskPlan.Snapshot())
                };

                snapshot.Jobs.Add(persisted);
            }

            Job? focused = manager.FocusedJob;
            if (focused != null)
            {
                snapshot.ConversationHistory.AddRange(focused.ConversationHistory);
            }

            return snapshot;
        }
    }
}
