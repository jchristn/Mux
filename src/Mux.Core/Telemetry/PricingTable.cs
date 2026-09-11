namespace Mux.Core.Telemetry
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    /// <summary>
    /// The user-editable model pricing table loaded from <c>pricing.json</c>: a version stamp and a map of
    /// model identifier to <see cref="ModelPricing"/>. Used to derive cost from stored token counts at read
    /// time. Unknown models resolve to no pricing (zero cost) and are surfaced in the dashboard so the user
    /// can add a rate.
    /// </summary>
    public sealed class PricingTable
    {
        #region Private-Members

        private string _Version = string.Empty;
        private Dictionary<string, ModelPricing> _Models = new Dictionary<string, ModelPricing>(StringComparer.OrdinalIgnoreCase);

        #endregion

        #region Public-Members

        /// <summary>
        /// A version stamp for the table (for example a date), recorded on rows for audit. May be empty.
        /// </summary>
        [JsonPropertyName("version")]
        public string Version
        {
            get => _Version;
            set => _Version = value ?? string.Empty;
        }

        /// <summary>
        /// The model-to-pricing map, keyed case-insensitively by model identifier. Never null.
        /// </summary>
        [JsonPropertyName("models")]
        public Dictionary<string, ModelPricing> Models
        {
            get => _Models;
            set => _Models = value ?? new Dictionary<string, ModelPricing>(StringComparer.OrdinalIgnoreCase);
        }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="PricingTable"/> class.
        /// </summary>
        public PricingTable()
        {
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Attempts to resolve pricing for a model identifier (exact match, case-insensitive).
        /// </summary>
        /// <param name="model">The model identifier. When null or blank, no pricing is returned.</param>
        /// <param name="pricing">The resolved pricing when found; otherwise null.</param>
        /// <returns>True when a rate is known for the model; otherwise false.</returns>
        public bool TryGetPricing(string? model, out ModelPricing? pricing)
        {
            pricing = null;
            if (string.IsNullOrWhiteSpace(model))
            {
                return false;
            }

            // The dictionary is case-insensitive; keys may have been supplied by the user in any casing.
            if (_Models.TryGetValue(model, out ModelPricing? found) && found != null)
            {
                pricing = found;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Computes the US-dollar cost for a set of token counts under this table. Returns 0 when the model
        /// has no known rate.
        /// </summary>
        /// <param name="model">The model identifier.</param>
        /// <param name="inputTokens">Total input/prompt tokens as reported by the provider.</param>
        /// <param name="cachedTokens">Cached (cache-read) input tokens, a subset of <paramref name="inputTokens"/>.</param>
        /// <param name="outputTokens">Output/completion tokens.</param>
        /// <returns>The computed cost in USD, or 0 when the model is unpriced.</returns>
        public double ComputeCostUsd(string? model, long inputTokens, long cachedTokens, long outputTokens)
        {
            if (!TryGetPricing(model, out ModelPricing? pricing) || pricing == null)
            {
                return 0.0;
            }

            // Cached tokens are treated as a subset of the prompt count (OpenAI/Gemini semantics): the
            // uncached remainder bills at the input rate, the cached portion at the cached rate. Because
            // cached counts are currently always zero (pending a provider-library change), this reduces to
            // input*inputRate + output*outputRate for every provider today. Revisit the per-provider
            // subset-vs-additional nuance (see PolyPrompt CACHED_TOKENS.md) when cache data starts flowing.
            long billedInput = inputTokens - cachedTokens;
            if (billedInput < 0)
            {
                billedInput = 0;
            }

            double cost = (billedInput / 1_000_000.0) * pricing.InputPerMTok;
            cost += (cachedTokens / 1_000_000.0) * pricing.CachedInputPerMTok;
            cost += (outputTokens / 1_000_000.0) * pricing.OutputPerMTok;
            return cost;
        }

        #endregion
    }
}
