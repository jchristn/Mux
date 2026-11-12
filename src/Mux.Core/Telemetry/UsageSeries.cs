namespace Mux.Core.Telemetry
{
    using System.Collections.Generic;

    /// <summary>
    /// One series of a split time series: the buckets for every call that shares a dimension value (a model,
    /// endpoint, provider, command, or call kind). A split keeps the top values and folds the rest into a
    /// series whose <see cref="IsOther"/> is true. Backs the stacked "by model" and "by endpoint" charts.
    /// </summary>
    public sealed class UsageSeries
    {
        #region Private-Members

        private string _Key = string.Empty;
        private List<UsageBucket> _Buckets = new List<UsageBucket>();

        #endregion

        #region Public-Members

        /// <summary>The dimension value (for example a model id), or "Other" for the folded remainder.</summary>
        public string Key
        {
            get => _Key;
            set => _Key = value ?? string.Empty;
        }

        /// <summary>True for the series that folds every value outside the top N.</summary>
        public bool IsOther { get; set; }

        /// <summary>The buckets, ascending by time, aligned with the total series. Never null.</summary>
        public List<UsageBucket> Buckets
        {
            get => _Buckets;
            set => _Buckets = value ?? new List<UsageBucket>();
        }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="UsageSeries"/> class.
        /// </summary>
        public UsageSeries()
        {
        }

        #endregion
    }
}
