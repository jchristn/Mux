namespace Mux.Core.Telemetry
{
    using System.Collections.Generic;
    using Mux.Core.Sessions;

    /// <summary>
    /// Filter criteria shared by usage queries: an inclusive time window plus optional endpoint, model,
    /// call-kind, and success constraints. Unset optional fields (null) apply no constraint.
    /// </summary>
    public sealed class UsageFilter
    {
        #region Public-Members

        /// <summary>
        /// The inclusive lower bound of the window, Unix epoch milliseconds. 0 (the default) means no lower
        /// bound.
        /// </summary>
        public long FromUnixMs { get; set; }

        /// <summary>
        /// The exclusive upper bound of the window, Unix epoch milliseconds. 0 (the default) means no upper
        /// bound.
        /// </summary>
        public long ToUnixMs { get; set; }

        /// <summary>
        /// An optional endpoint-name constraint. Null (the default) matches all endpoints.
        /// </summary>
        public string? EndpointName { get; set; }

        /// <summary>
        /// An optional model constraint. Null (the default) matches all models.
        /// </summary>
        public string? Model { get; set; }

        /// <summary>
        /// An optional session-id constraint. Null (the default) matches all sessions. Used to scope usage
        /// metrics to a single conversation.
        /// </summary>
        public string? SessionId { get; set; }

        /// <summary>
        /// Labels to constrain by. When non-empty, only sessions carrying every one of these labels match
        /// (resolved to <see cref="SessionIds"/> at query time by the metadata index). Null/empty applies no
        /// label constraint.
        /// </summary>
        public List<string>? Labels { get; set; }

        /// <summary>
        /// Tags to constrain by. When non-empty, only sessions carrying every one of these key/value tags
        /// match (resolved to <see cref="SessionIds"/> at query time). Null/empty applies no tag constraint.
        /// </summary>
        public List<SessionTag>? Tags { get; set; }

        /// <summary>
        /// The resolved set of session ids the label/tag constraints narrowed to, populated by the query
        /// service before the store runs. Null means "no session-set constraint"; an empty (non-null) set
        /// means "no session matched", which must yield zero rows.
        /// </summary>
        public IReadOnlyCollection<string>? SessionIds { get; set; }

        /// <summary>
        /// An optional call-kind constraint. Null (the default) matches all kinds.
        /// </summary>
        public UsageCallKindEnum? CallKind { get; set; }

        /// <summary>
        /// An optional success constraint. Null (the default) matches both successful and failed calls.
        /// </summary>
        public bool? Success { get; set; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="UsageFilter"/> class.
        /// </summary>
        public UsageFilter()
        {
        }

        #endregion
    }
}
