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
