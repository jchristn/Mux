namespace Mux.Desktop.Services
{
    using System;
    using System.Collections.Generic;
    using Mux.Core.Sessions;

    /// <summary>
    /// A lightweight projection of a persisted session for the conversation list: identity, title, active
    /// endpoint/model, timestamps, and message count. Built from a <c>SessionSnapshot</c> so the sidebar and
    /// tab strip never hold the full conversation history in memory.
    /// </summary>
    public sealed class ThreadSummary
    {
        /// <summary>
        /// Instantiate a thread summary.
        /// </summary>
        /// <param name="id">The session id. Required.</param>
        /// <param name="title">The session title. Required (may be empty).</param>
        /// <param name="endpointName">The active endpoint name.</param>
        /// <param name="model">The active model.</param>
        /// <param name="createdUtc">Creation time (UTC).</param>
        /// <param name="updatedUtc">Last-updated time (UTC).</param>
        /// <param name="messageCount">Number of messages in the conversation history.</param>
        /// <param name="titlePinned">Whether the title is user-pinned.</param>
        /// <param name="labels">The session's freeform labels. Optional; null is treated as empty.</param>
        /// <param name="tags">The session's key/value tags. Optional; null is treated as empty.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="id"/> or <paramref name="title"/> is null.</exception>
        public ThreadSummary(
            string id,
            string title,
            string endpointName,
            string model,
            DateTime createdUtc,
            DateTime updatedUtc,
            int messageCount,
            bool titlePinned,
            IReadOnlyList<string>? labels = null,
            IReadOnlyList<SessionTag>? tags = null)
        {
            ArgumentNullException.ThrowIfNull(id);
            ArgumentNullException.ThrowIfNull(title);

            _Id = id;
            _Title = title;
            _EndpointName = endpointName ?? string.Empty;
            _Model = model ?? string.Empty;
            _CreatedUtc = createdUtc;
            _UpdatedUtc = updatedUtc;
            _MessageCount = messageCount;
            _TitlePinned = titlePinned;
            _Labels = labels ?? Array.Empty<string>();
            _Tags = tags ?? Array.Empty<SessionTag>();
        }

        private readonly string _Id;
        private readonly string _Title;
        private readonly string _EndpointName;
        private readonly string _Model;
        private readonly DateTime _CreatedUtc;
        private readonly DateTime _UpdatedUtc;
        private readonly int _MessageCount;
        private readonly bool _TitlePinned;
        private readonly IReadOnlyList<string> _Labels;
        private readonly IReadOnlyList<SessionTag> _Tags;

        /// <summary>The session id (also the on-disk file stem).</summary>
        public string Id
        {
            get => _Id;
        }

        /// <summary>The session title.</summary>
        public string Title
        {
            get => _Title;
        }

        /// <summary>The active endpoint name at snapshot time.</summary>
        public string EndpointName
        {
            get => _EndpointName;
        }

        /// <summary>The active model at snapshot time.</summary>
        public string Model
        {
            get => _Model;
        }

        /// <summary>When the session was created (UTC).</summary>
        public DateTime CreatedUtc
        {
            get => _CreatedUtc;
        }

        /// <summary>When the session was last updated (UTC).</summary>
        public DateTime UpdatedUtc
        {
            get => _UpdatedUtc;
        }

        /// <summary>Number of messages in the conversation history.</summary>
        public int MessageCount
        {
            get => _MessageCount;
        }

        /// <summary>Whether the title was pinned by the user.</summary>
        public bool TitlePinned
        {
            get => _TitlePinned;
        }

        /// <summary>The session's freeform labels. Never null.</summary>
        public IReadOnlyList<string> Labels
        {
            get => _Labels;
        }

        /// <summary>The session's key/value tags. Never null.</summary>
        public IReadOnlyList<SessionTag> Tags
        {
            get => _Tags;
        }

        /// <summary>A compact, human-readable summary of labels and tags for list/tooltip display.</summary>
        public string MetadataSummary
        {
            get
            {
                List<string> parts = new List<string>();
                foreach (string label in _Labels) parts.Add("#" + label);
                foreach (SessionTag tag in _Tags) parts.Add(tag.Key + ":" + tag.Value);
                return string.Join("  ", parts);
            }
        }
    }
}
