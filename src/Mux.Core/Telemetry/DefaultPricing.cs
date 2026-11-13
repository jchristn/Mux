namespace Mux.Core.Telemetry
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Builds the curated default model pricing table seeded into <c>pricing.json</c> on first run and on
    /// upgrade (manifest-tracked, so user edits and deletions are preserved). Rates are US dollars per
    /// million tokens and are necessarily approximate and time-sensitive — they are a convenience starting
    /// point, not authoritative billing. Users edit <c>pricing.json</c> (or the dashboard Pricing page) to
    /// correct them. Local models (Ollama, vLLM) are intentionally absent so they resolve to zero cost.
    /// </summary>
    public static class DefaultPricing
    {
        /// <summary>
        /// The version stamp for the seeded defaults. Bump when the curated rates change so upgrades reseed
        /// only genuinely new model entries.
        /// </summary>
        public const string Version = "2026-11";

        /// <summary>
        /// Builds the curated default pricing table.
        /// </summary>
        /// <returns>A <see cref="PricingTable"/> populated with default per-model rates.</returns>
        public static PricingTable Build()
        {
            PricingTable table = new PricingTable
            {
                Version = Version,
                Models = new Dictionary<string, ModelPricing>(StringComparer.OrdinalIgnoreCase)
                {
                    // Anthropic (input / cached-read / output USD per Mtok). The 5.5 rates were read from
                    // https://platform.claude.com/docs/en/about-claude/pricing on 2026-11-12; Haiku 5.5 is the
                    // tier for prompts up to 100K tokens.
                    ["claude-opus-5-5"] = new ModelPricing(4.0, 0.20, 20.0),
                    ["claude-sonnet-5-5"] = new ModelPricing(2.0, 0.10, 10.0),
                    ["claude-haiku-5-5"] = new ModelPricing(0.10, 0.01, 0.50),
                    ["claude-opus-4-8"] = new ModelPricing(15.0, 1.5, 75.0),
                    ["claude-opus-4-1"] = new ModelPricing(15.0, 1.5, 75.0),
                    ["claude-sonnet-5"] = new ModelPricing(3.0, 0.3, 15.0),
                    ["claude-sonnet-4-5"] = new ModelPricing(3.0, 0.3, 15.0),
                    ["claude-haiku-4-5"] = new ModelPricing(1.0, 0.1, 5.0),
                    ["claude-3-5-sonnet"] = new ModelPricing(3.0, 0.3, 15.0),
                    ["claude-3-5-haiku"] = new ModelPricing(0.8, 0.08, 4.0),

                    // OpenAI. The GPT-6 rates were read from https://developers.openai.com/api/docs/pricing on
                    // 2026-11-12 (short-context tier, up to 272K input tokens).
                    ["gpt-6.1-sol"] = new ModelPricing(2.0, 0.10, 10.0),
                    ["gpt-6-astra"] = new ModelPricing(10.0, 1.0, 50.0),
                    ["gpt-6-luna"] = new ModelPricing(0.10, 0.01, 0.50),
                    ["gpt-4o"] = new ModelPricing(2.5, 1.25, 10.0),
                    ["gpt-4o-mini"] = new ModelPricing(0.15, 0.075, 0.6),
                    ["gpt-4.1"] = new ModelPricing(2.0, 0.5, 8.0),
                    ["gpt-4.1-mini"] = new ModelPricing(0.4, 0.1, 1.6),
                    ["o3"] = new ModelPricing(2.0, 0.5, 8.0),
                    ["o4-mini"] = new ModelPricing(1.1, 0.275, 4.4),

                    // Google Gemini. gemini-3.8-flash was read from https://aistudio.google.com/docs/pricing on
                    // 2026-11-12; Google lists this rate through 2026-12-31 and $1.50 / $0.15 / $7.50 from
                    // 2027-01-01.
                    ["gemini-3.8-flash"] = new ModelPricing(0.75, 0.075, 3.75),
                    ["gemini-2.5-pro"] = new ModelPricing(1.25, 0.31, 10.0),
                    ["gemini-2.5-flash"] = new ModelPricing(0.30, 0.075, 2.5),
                    ["gemini-1.5-pro"] = new ModelPricing(1.25, 0.3125, 5.0)
                }
            };

            return table;
        }
    }
}
