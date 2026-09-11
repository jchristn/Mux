namespace Mux.Core.Telemetry
{
    using System.Collections.Generic;

    /// <summary>
    /// A page of raw usage events for the dashboard's usage-history table: the matching rows for the current
    /// page plus the total count across the filter, for pagination controls. Each event carries its derived
    /// cost (computed at read time from the pricing table).
    /// </summary>
    public sealed class UsageEventPage
    {
        #region Private-Members

        private List<UsageEventRow> _Items = new List<UsageEventRow>();

        #endregion

        #region Public-Members

        /// <summary>The rows for the current page. Never null.</summary>
        public List<UsageEventRow> Items
        {
            get => _Items;
            set => _Items = value ?? new List<UsageEventRow>();
        }

        /// <summary>The total number of rows matching the filter across all pages.</summary>
        public long TotalCount { get; set; }

        /// <summary>The 1-based page number this page represents.</summary>
        public int PageNumber { get; set; }

        /// <summary>The page size used for this query.</summary>
        public int PageSize { get; set; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="UsageEventPage"/> class.
        /// </summary>
        public UsageEventPage()
        {
        }

        #endregion
    }
}
