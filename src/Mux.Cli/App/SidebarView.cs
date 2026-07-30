namespace Mux.Cli.App
{
    using System;
    using System.Collections.Generic;
    using Mux.Core.Jobs;
    using TUIKit;
    using TUIKit.Content;

    /// <summary>
    /// Renders the ambient sidebar into a <see cref="Pane"/>: a session header followed by one row per
    /// job (state glyph, short id, title, and a marker on the focused job). <see cref="Refresh"/> is
    /// idempotent — it clears and rewrites the pane from a snapshot of job state — so it is safe to call
    /// on every <c>JobManager</c> event and on focus changes. Calls are serialized internally so
    /// concurrent refreshes from manager worker threads do not interleave.
    /// </summary>
    public sealed class SidebarView
    {
        #region Private-Members

        private const int Width = 28;

        private readonly Pane _Pane;
        private readonly object _Sync = new object();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="SidebarView"/> class.
        /// </summary>
        /// <param name="pane">The sidebar pane to render into. Must not be null.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="pane"/> is null.</exception>
        public SidebarView(Pane pane)
        {
            _Pane = pane ?? throw new ArgumentNullException(nameof(pane));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Rewrites the sidebar from the given job snapshot.
        /// </summary>
        /// <param name="jobs">The jobs to list, in display order. Must not be null.</param>
        /// <param name="focusedJobId">The id of the focused job, or null.</param>
        /// <param name="title">The session title shown in the header.</param>
        /// <param name="sessionId">The session id shown in the header.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="jobs"/> is null.</exception>
        public void Refresh(IReadOnlyList<Job> jobs, string? focusedJobId, string title, string sessionId)
        {
            if (jobs is null) throw new ArgumentNullException(nameof(jobs));

            lock (_Sync)
            {
                _Pane.Clear();
                _Pane.WriteLine(Text.From(Fit(string.IsNullOrWhiteSpace(title) ? "mux" : title)).Cyan().Bold());
                if (!string.IsNullOrWhiteSpace(sessionId))
                {
                    _Pane.WriteLine(Text.From(Fit("session " + Shorten(sessionId, 8))).Dim());
                }

                _Pane.WriteLine(Text.From(string.Empty));
                _Pane.WriteLine(Text.From(Fit($"JOBS ({jobs.Count})")).Bold());

                if (jobs.Count == 0)
                {
                    _Pane.WriteLine(Text.From(Fit("(no jobs)")).Dim());
                    return;
                }

                for (int i = 0; i < jobs.Count; i++)
                {
                    Job job = jobs[i];
                    bool focused = string.Equals(job.Id, focusedJobId, StringComparison.Ordinal);
                    string marker = focused ? "▸" : " ";
                    string index = i < 9 ? (i + 1).ToString() : "·";
                    string label = string.IsNullOrWhiteSpace(job.Title) ? job.Prompt : job.Title;
                    string row = $"{marker}{index} {StateGlyph(job.State)} {label}";
                    StyledText styled = Text.From(Fit(row));
                    _Pane.WriteLine(focused ? styled.Bold() : styled);
                }
            }
        }

        #endregion

        #region Private-Methods

        private static string StateGlyph(JobState state)
        {
            switch (state)
            {
                case JobState.Queued: return "•";
                case JobState.Running: return "↻";
                case JobState.AwaitingApproval: return "?";
                case JobState.AwaitingWriteLease: return "⧗";
                case JobState.Paused: return "⏸";
                case JobState.Completed: return "✓";
                case JobState.Failed: return "✗";
                case JobState.Cancelled: return "⊘";
                default: return "·";
            }
        }

        private static string Fit(string text)
        {
            string value = text ?? string.Empty;
            return value.Length <= Width ? value : value.Substring(0, Width - 1) + "…";
        }

        private static string Shorten(string value, int length)
        {
            return value.Length <= length ? value : value.Substring(0, length);
        }

        #endregion
    }
}
