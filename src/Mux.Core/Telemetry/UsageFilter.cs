namespace Mux.Core.Telemetry
{
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
