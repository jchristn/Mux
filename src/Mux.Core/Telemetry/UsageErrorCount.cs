namespace Mux.Core.Telemetry
{
    /// <summary>
    /// The number of failed calls that share an error code (for example "http_429" or "timeout"). Failures
    /// with no recorded code are counted under "unknown". Backs the failure-reason list beside the calls chart.
    /// </summary>
    public sealed class UsageErrorCount
    {
        #region Private-Members

        private string _ErrorCode = string.Empty;

        #endregion

        #region Public-Members

        /// <summary>The error code.</summary>
        public string ErrorCode
        {
            get => _ErrorCode;
            set => _ErrorCode = value ?? string.Empty;
        }

        /// <summary>The number of failed calls with this code.</summary>
        public long Count { get; set; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="UsageErrorCount"/> class.
        /// </summary>
        public UsageErrorCount()
        {
        }

        #endregion
    }
}
