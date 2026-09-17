namespace Mux.Core.Telemetry
{
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Sessions;

    /// <summary>
    /// One entry in a <see cref="ISessionMetadataIndex"/> snapshot: a session's id and the labels/tags it
    /// currently carries. Used to join usage rows (which store only a <c>session_id</c>) to session metadata at
    /// query time, so a relabeled session refilters its whole history.
    /// </summary>
    public sealed class SessionMetadataEntry
    {
        /// <summary>The session id (matches <c>usage_events.session_id</c>).</summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>The session's freeform labels.</summary>
        public IReadOnlyList<string> Labels { get; set; } = new List<string>();

        /// <summary>The session's key/value tags.</summary>
        public IReadOnlyList<SessionTag> Tags { get; set; } = new List<SessionTag>();
    }

    /// <summary>
    /// Provides a point-in-time projection of session id → labels/tags so usage queries can filter and break
    /// down by label/tag without those values being denormalized onto usage rows. Implementations are
    /// expected to be cheap to call per query (session counts are small); the query service owns the join and
    /// resolution logic.
    /// </summary>
    public interface ISessionMetadataIndex
    {
        /// <summary>
        /// Returns the current label/tag metadata for every persisted session.
        /// </summary>
        /// <param name="token">A token to cancel the operation.</param>
        /// <returns>One entry per session.</returns>
        Task<IReadOnlyList<SessionMetadataEntry>> SnapshotAsync(CancellationToken token);
    }
}
