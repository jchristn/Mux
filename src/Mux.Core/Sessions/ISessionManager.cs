namespace Mux.Core.Sessions
{
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// The single, surface-agnostic set of session-management verbs over a <see cref="SessionStore"/>:
    /// list, create, rename, pin, duplicate, delete, and export. A "session" here is one persisted
    /// conversation; the TUI, Desktop, and Web surfaces all drive the same implementation so management
    /// semantics (titles, ordering, export) are identical everywhere.
    /// </summary>
    public interface ISessionManager
    {
        /// <summary>List all sessions, newest-updated first.</summary>
        /// <param name="token">A cancellation token.</param>
        /// <returns>The session projections.</returns>
        Task<IReadOnlyList<SessionInfo>> ListAsync(CancellationToken token);

        /// <summary>Create a new, empty session.</summary>
        /// <param name="title">Optional title; when blank a default title is used.</param>
        /// <param name="endpointName">Optional active endpoint name.</param>
        /// <param name="model">Optional active model.</param>
        /// <param name="workingDirectory">Optional working directory (project root) to record.</param>
        /// <param name="token">A cancellation token.</param>
        /// <returns>The created session projection.</returns>
        Task<SessionInfo> CreateAsync(string? title, string? endpointName, string? model, string? workingDirectory, CancellationToken token);

        /// <summary>Rename a session and pin its title so auto-titling will not overwrite it.</summary>
        /// <param name="id">The session id.</param>
        /// <param name="title">The new title (normalized before saving).</param>
        /// <param name="token">A cancellation token.</param>
        /// <returns>The updated projection, or null when the session does not exist.</returns>
        Task<SessionInfo?> RenameAsync(string id, string title, CancellationToken token);

        /// <summary>Set or clear the pinned state of a session's title.</summary>
        /// <param name="id">The session id.</param>
        /// <param name="pinned">True to pin; false to allow auto-titling.</param>
        /// <param name="token">A cancellation token.</param>
        /// <returns>True when updated; false when the session does not exist.</returns>
        Task<bool> SetTitlePinnedAsync(string id, bool pinned, CancellationToken token);

        /// <summary>Attach a freeform label to a session (idempotent; deduped case-insensitively).</summary>
        /// <param name="id">The session id.</param>
        /// <param name="label">The raw label; normalized before saving.</param>
        /// <param name="token">A cancellation token.</param>
        /// <returns>The updated projection, or null when the session does not exist.</returns>
        /// <exception cref="System.ArgumentException">Thrown when the label is invalid.</exception>
        Task<SessionInfo?> AddLabelAsync(string id, string label, CancellationToken token);

        /// <summary>Remove a label from a session (no-op when absent).</summary>
        /// <param name="id">The session id.</param>
        /// <param name="label">The label to remove; matched case-insensitively after normalization.</param>
        /// <param name="token">A cancellation token.</param>
        /// <returns>The updated projection, or null when the session does not exist.</returns>
        Task<SessionInfo?> RemoveLabelAsync(string id, string label, CancellationToken token);

        /// <summary>Set (upsert by normalized key) a key/value tag on a session.</summary>
        /// <param name="id">The session id.</param>
        /// <param name="key">The raw tag key; normalized (lowercased/slugified) before saving.</param>
        /// <param name="value">The tag value; normalized before saving.</param>
        /// <param name="token">A cancellation token.</param>
        /// <returns>The updated projection, or null when the session does not exist.</returns>
        /// <exception cref="System.ArgumentException">Thrown when the key or value is invalid.</exception>
        Task<SessionInfo?> SetTagAsync(string id, string key, string value, CancellationToken token);

        /// <summary>Remove a tag by key from a session (no-op when absent).</summary>
        /// <param name="id">The session id.</param>
        /// <param name="key">The raw tag key; normalized before matching.</param>
        /// <param name="token">A cancellation token.</param>
        /// <returns>The updated projection, or null when the session does not exist.</returns>
        /// <exception cref="System.ArgumentException">Thrown when the key is invalid.</exception>
        Task<SessionInfo?> RemoveTagAsync(string id, string key, CancellationToken token);

        /// <summary>Bulk-apply labels and tags to a session: add each label, upsert each tag.</summary>
        /// <param name="id">The session id.</param>
        /// <param name="labels">Raw labels to add; each normalized. Invalid entries are skipped.</param>
        /// <param name="tags">Key/value tags to upsert; each normalized. Invalid entries are skipped.</param>
        /// <param name="token">A cancellation token.</param>
        /// <returns>The updated projection, or null when the session does not exist.</returns>
        Task<SessionInfo?> SetMetadataAsync(string id, System.Collections.Generic.IEnumerable<string>? labels, System.Collections.Generic.IEnumerable<SessionTag>? tags, CancellationToken token);

        /// <summary>Duplicate (fork) a session from its current state under a new id.</summary>
        /// <param name="id">The id of the session to copy.</param>
        /// <param name="token">A cancellation token.</param>
        /// <returns>The new session projection, or null when the source does not exist.</returns>
        Task<SessionInfo?> DuplicateAsync(string id, CancellationToken token);

        /// <summary>Delete a session.</summary>
        /// <param name="id">The session id.</param>
        /// <param name="token">A cancellation token.</param>
        /// <returns>True when a session was deleted; false when none existed.</returns>
        Task<bool> DeleteAsync(string id, CancellationToken token);

        /// <summary>Render a session to a shareable document.</summary>
        /// <param name="id">The session id.</param>
        /// <param name="format">The export format (<c>md</c> or <c>html</c>).</param>
        /// <param name="token">A cancellation token.</param>
        /// <returns>The rendered document, or null when the session does not exist.</returns>
        Task<string?> ExportAsync(string id, string format, CancellationToken token);
    }
}
