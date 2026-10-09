namespace Test.Shared.Support
{
    using System;
    using System.Globalization;
    using System.Reflection;
    using System.Runtime.Serialization;
    using Mux.Core.Enums;
    using Mux.Core.Models;

    /// <summary>
    /// Settings for tests that call a real model, read from environment variables so every harness (the console
    /// runner, xUnit, NUnit) can pass them. Test.Automated maps its <c>--llm-*</c> arguments onto these variables.
    /// When no endpoint and model are set, live tests are skipped. The API key is never printed.
    /// </summary>
    public static class LiveModelOptions
    {
        #region Public-Members

        /// <summary>Base URL of the endpoint.</summary>
        public const string EndpointVariable = "MUX_TEST_LLM_ENDPOINT";

        /// <summary>Model name.</summary>
        public const string ModelVariable = "MUX_TEST_LLM_MODEL";

        /// <summary>Adapter type (ollama, openai, openai_compatible, vllm, anthropic, gemini, azure_openai, vertex, bedrock). Default ollama.</summary>
        public const string AdapterVariable = "MUX_TEST_LLM_ADAPTER";

        /// <summary>API key or bearer token, optional.</summary>
        public const string ApiKeyVariable = "MUX_TEST_LLM_API_KEY";

        /// <summary>The top-1 rate the live skill-selection test must reach, from 0 to 1. Default 0.6.</summary>
        public const string FloorVariable = "MUX_TEST_LLM_FLOOR";

        /// <summary>How many built-in cases to ask about (spread across the set). Default all.</summary>
        public const string CasesVariable = "MUX_TEST_LLM_CASES";

        /// <summary>Optional path to write the live run's JSON report to.</summary>
        public const string ReportVariable = "MUX_TEST_LLM_REPORT";

        /// <summary>Request timeout in seconds. Default 180.</summary>
        public const string TimeoutVariable = "MUX_TEST_LLM_TIMEOUT";

        /// <summary>Whether an endpoint and model were supplied.</summary>
        public static bool IsConfigured
        {
            get => !string.IsNullOrWhiteSpace(Get(EndpointVariable)) && !string.IsNullOrWhiteSpace(Get(ModelVariable));
        }

        /// <summary>The model name, or empty.</summary>
        public static string Model
        {
            get => Get(ModelVariable);
        }

        /// <summary>The required top-1 rate.</summary>
        public static double Floor
        {
            get => double.TryParse(Get(FloorVariable), NumberStyles.Float, CultureInfo.InvariantCulture, out double floor) ? Math.Clamp(floor, 0, 1) : 0.6;
        }

        /// <summary>The number of cases to ask about, or 0 for all.</summary>
        public static int CaseLimit
        {
            get => int.TryParse(Get(CasesVariable), NumberStyles.Integer, CultureInfo.InvariantCulture, out int count) && count > 0 ? count : 0;
        }

        /// <summary>The report path, or empty.</summary>
        public static string ReportPath
        {
            get => Get(ReportVariable);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Builds the endpoint configuration from the environment.
        /// </summary>
        /// <returns>The endpoint.</returns>
        /// <exception cref="InvalidOperationException">Thrown when the adapter name is not recognized.</exception>
        public static EndpointConfig BuildEndpoint()
        {
            string adapter = Get(AdapterVariable);
            EndpointConfig endpoint = new EndpointConfig
            {
                Name = "live-test",
                AdapterType = ParseAdapter(string.IsNullOrWhiteSpace(adapter) ? "ollama" : adapter),
                BaseUrl = Get(EndpointVariable),
                Model = Get(ModelVariable),
                TimeoutMs = (int.TryParse(Get(TimeoutVariable), NumberStyles.Integer, CultureInfo.InvariantCulture, out int seconds) && seconds > 0 ? seconds : 180) * 1000,
                Temperature = 0
            };
            string key = Get(ApiKeyVariable);
            if (!string.IsNullOrWhiteSpace(key)) endpoint.ApiKey = key;
            return endpoint;
        }

        /// <summary>
        /// Parses an adapter wire name (as in endpoints.json; dashes are accepted for underscores).
        /// </summary>
        /// <param name="name">The name.</param>
        /// <returns>The adapter.</returns>
        /// <exception cref="InvalidOperationException">Thrown when the name is not recognized.</exception>
        public static AdapterTypeEnum ParseAdapter(string name)
        {
            string wanted = (name ?? string.Empty).Trim().Replace('-', '_');
            foreach (FieldInfo field in typeof(AdapterTypeEnum).GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                EnumMemberAttribute? member = field.GetCustomAttribute<EnumMemberAttribute>();
                if (string.Equals(member?.Value ?? field.Name, wanted, StringComparison.OrdinalIgnoreCase) || string.Equals(field.Name, wanted, StringComparison.OrdinalIgnoreCase))
                {
                    return (AdapterTypeEnum)field.GetValue(null)!;
                }
            }

            throw new InvalidOperationException("Unknown adapter '" + name + "'; use ollama, openai, openai_compatible, vllm, anthropic, gemini, azure_openai, vertex, or bedrock.");
        }

        #endregion

        #region Private-Methods

        private static string Get(string name)
        {
            return Environment.GetEnvironmentVariable(name) ?? string.Empty;
        }

        #endregion
    }
}
