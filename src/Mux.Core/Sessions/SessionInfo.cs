namespace Mux.Core.Sessions
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// A lightweight, surface-agnostic projection of a persisted session for list UIs: identity, title,
    /// active endpoint/model, working directory, timestamps, and message count. Built from a
    /// <see cref="SessionSnapshot"/> so callers never hold the full conversation history in memory. Shared
    /// by every surface (TUI, Desktop, Web) via <see cref="ISessionManager"/>.
    /// </summary>
    public sealed class SessionInfo
    {
        /// <summary>
        /// Instantiate a session info projection.
        /// </summary>
        /// <param name="id">The session id (also the on-disk file stem). Required.</param>
        /// <param name="title">The session title. Required (may be empty).</param>
        /// <param name="endpointName">The active endpoint name.</param>
        /// <param name="model">The active model.</param>
        /// <param name="workingDirectory">The working directory (project root) captured with the session.</param>
        /// <param name="createdUtc">Creation time (UTC).</param>
        /// <param name="updatedUtc">Last-updated time (UTC).</param>
        /// <param name="messageCount">Number of messages in the conversation history.</param>
        /// <param name="titlePinned">Whether the title is user-pinned.</param>
        /// <param name="labels">The session's freeform labels. Optional; null is treated as empty.</param>
        /// <param name="tags">The session's key/value tags. Optional; null is treated as empty.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="id"/> or <paramref name="title"/> is null.</exception>
        public SessionInfo(
            string id,
            string title,
            string endpointName,
            string model,
            string workingDirectory,
            DateTime createdUtc,
            DateTime updatedUtc,
            int messageCount,
            bool titlePinned,
            IReadOnlyList<string>? labels = null,
            IReadOnlyList<SessionTag>? tags = null)
        {
            if (id is null) throw new ArgumentNullException(nameof(id));
            if (title is null) throw new ArgumentNullException(nameof(title));

            Id = id;
            Title = title;
            EndpointName = endpointName ?? string.Empty;
            Model = model ?? string.Empty;
            WorkingDirectory = workingDirectory ?? string.Empty;
            CreatedUtc = createdUtc;
            UpdatedUtc = updatedUtc;
            MessageCount = messageCount;
            TitlePinned = titlePinned;
            Labels = labels ?? Array.Empty<string>();
            Tags = tags ?? Array.Empty<SessionTag>();
        }

        /// <summary>The session id (also the on-disk file stem).</summary>
        public string Id { get; }

        /// <summary>The session title.</summary>
        public string Title { get; }

        /// <summary>The active endpoint name at snapshot time.</summary>
        public string EndpointName { get; }

        /// <summary>The active model at snapshot time.</summary>
        public string Model { get; }

        /// <summary>The working directory (project root) captured with the session, or empty when unknown.</summary>
        public string WorkingDirectory { get; }

        /// <summary>When the session was created (UTC).</summary>
        public DateTime CreatedUtc { get; }

        /// <summary>When the session was last updated (UTC).</summary>
        public DateTime UpdatedUtc { get; }

        /// <summary>Number of messages in the conversation history.</summary>
        public int MessageCount { get; }

        /// <summary>Whether the title was pinned by the user.</summary>
        public bool TitlePinned { get; }

        /// <summary>The session's freeform labels. Never null.</summary>
        public IReadOnlyList<string> Labels { get; }

        /// <summary>The session's key/value tags. Never null.</summary>
        public IReadOnlyList<SessionTag> Tags { get; }

        /// <summary>
        /// Projects a <see cref="SessionSnapshot"/> to a <see cref="SessionInfo"/>.
        /// </summary>
        /// <param name="snapshot">The snapshot to project. Required.</param>
        /// <returns>The projection.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="snapshot"/> is null.</exception>
        public static SessionInfo FromSnapshot(SessionSnapshot snapshot)
        {
            if (snapshot is null) throw new ArgumentNullException(nameof(snapshot));

            return new SessionInfo(
                snapshot.Id,
                snapshot.Title,
                snapshot.EndpointName,
                snapshot.Model,
                snapshot.WorkingDirectory,
                snapshot.CreatedUtc,
                snapshot.UpdatedUtc,
                snapshot.ConversationHistory.Count,
                snapshot.TitlePinned,
                snapshot.Labels.ToList(),
                snapshot.Tags.Select(t => new SessionTag(t.Key, t.Value)).ToList());
        }
    }
}
