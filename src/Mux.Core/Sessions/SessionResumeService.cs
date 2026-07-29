namespace Mux.Core.Sessions
{
    using System;
    using System.Collections.Generic;
    using Mux.Core.Models;

    /// <summary>
    /// Rehydrates a <see cref="SessionSnapshot"/> into a <see cref="SessionResumeResult"/>, partitioning
    /// persisted jobs into completed (terminal) and interrupted (non-terminal, requiring an explicit
    /// re-run). Pure and side-effect free.
    /// </summary>
    public static class SessionResumeService
    {
        /// <summary>
        /// Produces the resume result for a snapshot.
        /// </summary>
        /// <param name="snapshot">The snapshot to resume. Must not be null.</param>
        /// <returns>The rehydrated session state.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="snapshot"/> is null.</exception>
        public static SessionResumeResult Resume(SessionSnapshot snapshot)
        {
            if (snapshot is null) throw new ArgumentNullException(nameof(snapshot));

            SessionResumeResult result = new SessionResumeResult
            {
                Id = snapshot.Id,
                Title = snapshot.Title,
                TitlePinned = snapshot.TitlePinned,
                EndpointName = snapshot.EndpointName,
                Model = snapshot.Model,
                CompactionCount = snapshot.CompactionCount,
                ConversationHistory = new List<ConversationMessage>(snapshot.ConversationHistory),
                PromptHistory = new List<string>(snapshot.PromptHistory)
            };

            foreach (PersistedJobSnapshot job in snapshot.Jobs)
            {
                if (IsTerminalStateName(job.State))
                {
                    result.CompletedJobs.Add(job);
                }
                else
                {
                    result.InterruptedJobs.Add(job);
                }
            }

            return result;
        }

        /// <summary>
        /// Determines whether a persisted job state name represents a terminal state.
        /// </summary>
        /// <param name="stateName">The persisted <c>JobState</c> value name.</param>
        /// <returns>True for Completed, Failed, or Cancelled (case-insensitive); otherwise false.</returns>
        public static bool IsTerminalStateName(string stateName)
        {
            return string.Equals(stateName, "Completed", StringComparison.OrdinalIgnoreCase)
                || string.Equals(stateName, "Failed", StringComparison.OrdinalIgnoreCase)
                || string.Equals(stateName, "Cancelled", StringComparison.OrdinalIgnoreCase);
        }
    }
}
