namespace Mux.Desktop.Services
{
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Manages conversations/threads over the mux session store: list, create, rename, pin, duplicate,
    /// delete, and export. A thread is one persisted session; this service is the desktop app's seam over
    /// <c>SessionStore</c> and the session snapshot/export helpers.
    /// </summary>
    public interface IThreadService
    {
        /// <summary>
        /// List all threads, newest-updated first.
        /// </summary>
        /// <param name="token">A cancellation token.</param>
        /// <returns>The thread summaries.</returns>
        Task<IReadOnlyList<ThreadSummary>> ListAsync(CancellationToken token);

        /// <summary>
        /// Create a new, empty thread.
        /// </summary>
        /// <param name="title">Optional title; when blank a default title is used.</param>
        /// <param name="endpointName">Optional active endpoint name.</param>
        /// <param name="model">Optional active model.</param>
        /// <param name="token">A cancellation token.</param>
        /// <returns>The created thread summary.</returns>
        Task<ThreadSummary> CreateAsync(string? title, string? endpointName, string? model, CancellationToken token);

        /// <summary>
        /// Rename a thread and pin its title so auto-titling will not overwrite it.
        /// </summary>
        /// <param name="id">The thread id.</param>
        /// <param name="title">The new title (normalized before saving).</param>
        /// <param name="token">A cancellation token.</param>
        /// <returns>The updated summary, or null when the thread does not exist.</returns>
        Task<ThreadSummary?> RenameAsync(string id, string title, CancellationToken token);

        /// <summary>
        /// Set or clear the pinned state of a thread's title.
        /// </summary>
        /// <param name="id">The thread id.</param>
        /// <param name="pinned">True to pin; false to allow auto-titling.</param>
        /// <param name="token">A cancellation token.</param>
        /// <returns>True when updated; false when the thread does not exist.</returns>
        Task<bool> SetTitlePinnedAsync(string id, bool pinned, CancellationToken token);

        /// <summary>
        /// Duplicate (fork) a thread from its current state under a new id.
        /// </summary>
        /// <param name="id">The id of the thread to copy.</param>
        /// <param name="token">A cancellation token.</param>
        /// <returns>The new thread summary, or null when the source does not exist.</returns>
        Task<ThreadSummary?> DuplicateAsync(string id, CancellationToken token);

        /// <summary>
        /// Delete a thread.
        /// </summary>
        /// <param name="id">The thread id.</param>
        /// <param name="token">A cancellation token.</param>
        /// <returns>True when a thread was deleted; false when none existed.</returns>
        Task<bool> DeleteAsync(string id, CancellationToken token);

        /// <summary>
        /// Render a thread to a shareable document.
        /// </summary>
        /// <param name="id">The thread id.</param>
        /// <param name="format">The export format (<c>md</c> or <c>html</c>).</param>
        /// <param name="token">A cancellation token.</param>
        /// <returns>The rendered document, or null when the thread does not exist.</returns>
        Task<string?> ExportAsync(string id, string format, CancellationToken token);
    }
}
