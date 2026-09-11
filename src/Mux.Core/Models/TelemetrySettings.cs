namespace Mux.Core.Models
{
    using System;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Configuration for durable usage telemetry: the per-call record of token counts, latency,
    /// time-to-first-token, streaming time, and derived cost that mux writes to a local SQLite database
    /// (<c>~/.mux/usage.db</c> by default) and surfaces in the TUI and the <c>mux serve</c> dashboard.
    /// Capture is best-effort and never affects a run. Telemetry is enabled by default.
    /// </summary>
    public class TelemetrySettings
    {
        #region Private-Members

        private bool _Enabled = true;
        private int _RetentionDays = 90;
        private string? _DatabasePath = null;
        private bool _PricingEnabled = true;
        private long _MaxRows = 5_000_000;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="TelemetrySettings"/> class with default values.
        /// </summary>
        public TelemetrySettings()
        {
        }

        #endregion

        #region Public-Members

        /// <summary>
        /// Whether usage telemetry is captured and persisted. Defaults to true. When false, no database is
        /// opened and no events are recorded; the dashboard and <c>/usage</c> views show an empty state.
        /// </summary>
        [JsonPropertyName("enabled")]
        public bool Enabled
        {
            get => _Enabled;
            set => _Enabled = value;
        }

        /// <summary>
        /// The number of days of usage history to retain. Rows older than this are pruned on store open and
        /// daily thereafter. Clamped to the range 0-3650; 0 means retain forever. Defaults to 90.
        /// </summary>
        [JsonPropertyName("retentionDays")]
        public int RetentionDays
        {
            get => _RetentionDays;
            set => _RetentionDays = Math.Clamp(value, 0, 3650);
        }

        /// <summary>
        /// An optional override for the SQLite database file path. When null or blank, the database resolves
        /// to <c>usage.db</c> under the effective config directory (<c>~/.mux</c> or <c>MUX_CONFIG_DIR</c>).
        /// </summary>
        [JsonPropertyName("databasePath")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? DatabasePath
        {
            get => _DatabasePath;
            set => _DatabasePath = string.IsNullOrWhiteSpace(value) ? null : value;
        }

        /// <summary>
        /// Whether derived cost is computed and shown (from the user-editable pricing table). Defaults to
        /// true. When false, the cost dimension is hidden but token and timing telemetry are unaffected.
        /// </summary>
        [JsonPropertyName("pricingEnabled")]
        public bool PricingEnabled
        {
            get => _PricingEnabled;
            set => _PricingEnabled = value;
        }

        /// <summary>
        /// A secondary retention guard: the maximum number of rows to retain regardless of age. The oldest
        /// rows beyond this count are pruned. Clamped to a minimum of 1000. Defaults to 5,000,000.
        /// </summary>
        [JsonPropertyName("maxRows")]
        public long MaxRows
        {
            get => _MaxRows;
            set => _MaxRows = Math.Max(1000, value);
        }

        #endregion
    }
}
