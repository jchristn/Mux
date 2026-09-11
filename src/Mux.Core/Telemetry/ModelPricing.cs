namespace Mux.Core.Telemetry
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Per-model token pricing in US dollars per million tokens. All rates default to zero, which is the
    /// correct value for local models (Ollama, vLLM) that cost nothing to run. Rates are user-editable in
    /// <c>pricing.json</c>; cost is computed from these at read time so a rate correction re-values history.
    /// </summary>
    public sealed class ModelPricing
    {
        #region Private-Members

        private double _InputPerMTok = 0.0;
        private double _CachedInputPerMTok = 0.0;
        private double _OutputPerMTok = 0.0;

        #endregion

        #region Public-Members

        /// <summary>
        /// US dollars per million uncached input (prompt) tokens. Defaults to 0.
        /// </summary>
        [JsonPropertyName("inputPerMTok")]
        public double InputPerMTok
        {
            get => _InputPerMTok;
            set => _InputPerMTok = value < 0 ? 0 : value;
        }

        /// <summary>
        /// US dollars per million cached (cache-read) input tokens, usually a fraction of the input rate.
        /// Defaults to 0.
        /// </summary>
        [JsonPropertyName("cachedInputPerMTok")]
        public double CachedInputPerMTok
        {
            get => _CachedInputPerMTok;
            set => _CachedInputPerMTok = value < 0 ? 0 : value;
        }

        /// <summary>
        /// US dollars per million output (completion) tokens. Defaults to 0.
        /// </summary>
        [JsonPropertyName("outputPerMTok")]
        public double OutputPerMTok
        {
            get => _OutputPerMTok;
            set => _OutputPerMTok = value < 0 ? 0 : value;
        }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="ModelPricing"/> class.
        /// </summary>
        public ModelPricing()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ModelPricing"/> class with explicit rates.
        /// </summary>
        /// <param name="inputPerMTok">USD per million input tokens.</param>
        /// <param name="cachedInputPerMTok">USD per million cached input tokens.</param>
        /// <param name="outputPerMTok">USD per million output tokens.</param>
        public ModelPricing(double inputPerMTok, double cachedInputPerMTok, double outputPerMTok)
        {
            InputPerMTok = inputPerMTok;
            CachedInputPerMTok = cachedInputPerMTok;
            OutputPerMTok = outputPerMTok;
        }

        #endregion
    }
}
