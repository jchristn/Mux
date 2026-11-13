namespace Mux.Core.Settings
{
    using System;
    using System.Collections.Generic;
    using Mux.Core.Enums;
    using Mux.Core.Models;

    /// <summary>
    /// Advisory notes about an endpoint's configuration: settings that load but no longer have an effect, or
    /// combinations that are likely to fail since mux 1.8 moved the openai adapter to the OpenAI Responses
    /// API. Advisories never block a request; surfaces show them where users inspect an endpoint
    /// (<c>mux probe</c>, <c>mux endpoint show</c>, and the endpoint editors).
    /// </summary>
    public static class EndpointAdvisories
    {
        /// <summary>Advisory code: an <c>openai</c> endpoint points at a server other than api.openai.com.</summary>
        public const string OpenAiNonOpenAiBaseUrl = "openai_non_openai_base_url";

        /// <summary>Advisory code: auth placement is set on an <c>openai</c> endpoint, which always uses a bearer key.</summary>
        public const string OpenAiAuthPlacementIgnored = "openai_auth_placement_ignored";

        /// <summary>Advisory code: <c>apiVersion</c> is set on an <c>azure-openai</c> endpoint, which now uses the v1 API.</summary>
        public const string AzureApiVersionIgnored = "azure_api_version_ignored";

        /// <summary>
        /// Returns the advisories that apply to an endpoint, in a stable order. Empty when none apply.
        /// </summary>
        /// <param name="endpoint">The endpoint to inspect.</param>
        /// <returns>The advisories.</returns>
        public static List<EndpointAdvisory> For(EndpointConfig endpoint)
        {
            if (endpoint == null) throw new ArgumentNullException(nameof(endpoint));
            List<EndpointAdvisory> advisories = new List<EndpointAdvisory>();

            if (endpoint.AdapterType == AdapterTypeEnum.OpenAi)
            {
                if (!string.IsNullOrWhiteSpace(endpoint.BaseUrl) && !IsOpenAiHost(endpoint.BaseUrl))
                {
                    advisories.Add(new EndpointAdvisory(
                        OpenAiNonOpenAiBaseUrl,
                        $"Endpoint '{endpoint.Name}' uses adapter openai with a non-OpenAI baseUrl. Since mux 1.8 this adapter uses the OpenAI Responses API. If the server only supports Chat Completions, change the adapter to openai-compatible."));
                }

                if (endpoint.AuthPlacement != AuthPlacementEnum.Bearer)
                {
                    advisories.Add(new EndpointAdvisory(
                        OpenAiAuthPlacementIgnored,
                        $"Endpoint '{endpoint.Name}' sets authPlacement, but the openai adapter always sends a bearer key. Use adapter openai-compatible for header or query key placement."));
                }
            }

            if (endpoint.AdapterType == AdapterTypeEnum.AzureOpenAi && !string.IsNullOrWhiteSpace(endpoint.ApiVersion))
            {
                advisories.Add(new EndpointAdvisory(
                    AzureApiVersionIgnored,
                    $"Endpoint '{endpoint.Name}' sets apiVersion, which is no longer used: Azure OpenAI now uses the v1 API with the deployment name in model."));
            }

            return advisories;
        }

        private static bool IsOpenAiHost(string baseUrl)
        {
            if (!Uri.TryCreate(baseUrl.Trim(), UriKind.Absolute, out Uri? uri)) return false;
            string host = uri.Host;
            return host.Equals("api.openai.com", StringComparison.OrdinalIgnoreCase)
                || host.EndsWith(".openai.com", StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// One advisory note about an endpoint's configuration.
    /// </summary>
    public sealed class EndpointAdvisory
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="EndpointAdvisory"/> class.
        /// </summary>
        /// <param name="code">The stable, machine-readable advisory code.</param>
        /// <param name="message">The human-readable message.</param>
        public EndpointAdvisory(string code, string message)
        {
            Code = code ?? throw new ArgumentNullException(nameof(code));
            Message = message ?? throw new ArgumentNullException(nameof(message));
        }

        /// <summary>The stable, machine-readable advisory code.</summary>
        public string Code { get; }

        /// <summary>The human-readable message.</summary>
        public string Message { get; }
    }
}
