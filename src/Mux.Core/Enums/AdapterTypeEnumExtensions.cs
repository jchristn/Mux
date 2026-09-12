namespace Mux.Core.Enums
{
    /// <summary>
    /// Conversions between <see cref="AdapterTypeEnum"/> and its kebab-case display string
    /// (for example "openai-compatible"), shared so the server routes and the CLI form do not each hand-roll
    /// the same map. The kebab form is the enum's snake-case wire value with underscores replaced by hyphens.
    /// </summary>
    public static class AdapterTypeEnumExtensions
    {
        /// <summary>
        /// Returns the kebab-case display string for an adapter.
        /// </summary>
        /// <param name="adapter">The adapter.</param>
        /// <returns>The kebab-case string.</returns>
        public static string ToKebab(this AdapterTypeEnum adapter)
        {
            switch (adapter)
            {
                case AdapterTypeEnum.Ollama: return "ollama";
                case AdapterTypeEnum.OpenAi: return "openai";
                case AdapterTypeEnum.Vllm: return "vllm";
                case AdapterTypeEnum.OpenAiCompatible: return "openai-compatible";
                case AdapterTypeEnum.Anthropic: return "anthropic";
                case AdapterTypeEnum.Gemini: return "gemini";
                case AdapterTypeEnum.AzureOpenAi: return "azure-openai";
                case AdapterTypeEnum.Vertex: return "vertex";
                case AdapterTypeEnum.Bedrock: return "bedrock";
                default: return adapter.ToString().ToLowerInvariant();
            }
        }

        /// <summary>
        /// Parses a kebab-case (or snake-case) adapter string, returning <paramref name="fallback"/> for null,
        /// empty, or unrecognized input.
        /// </summary>
        /// <param name="adapter">The adapter string, or null.</param>
        /// <param name="fallback">The value to return when the input is unrecognized.</param>
        /// <returns>The parsed adapter, or <paramref name="fallback"/>.</returns>
        public static AdapterTypeEnum FromKebab(string? adapter, AdapterTypeEnum fallback = AdapterTypeEnum.OpenAiCompatible)
        {
            switch ((adapter ?? string.Empty).Trim().ToLowerInvariant().Replace('_', '-'))
            {
                case "ollama": return AdapterTypeEnum.Ollama;
                case "openai": return AdapterTypeEnum.OpenAi;
                case "vllm": return AdapterTypeEnum.Vllm;
                case "openai-compatible": return AdapterTypeEnum.OpenAiCompatible;
                case "anthropic": return AdapterTypeEnum.Anthropic;
                case "gemini": return AdapterTypeEnum.Gemini;
                case "azure-openai": return AdapterTypeEnum.AzureOpenAi;
                case "vertex": return AdapterTypeEnum.Vertex;
                case "bedrock": return AdapterTypeEnum.Bedrock;
                default: return fallback;
            }
        }
    }
}
