namespace Mux.Server.Routes
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Mux.Core.Enums;
    using Mux.Core.Models;
    using Mux.Server.Models;
    using WatsonWebserver;

    /// <summary>
    /// Read-only routes over the configured endpoints. Never exposes secrets.
    /// </summary>
    public sealed class EndpointRoutes
    {
        private readonly string? _ApiKey;
        private readonly Func<List<EndpointConfig>> _EndpointsProvider;

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="apiKey">Configured API key, or null for no-auth.</param>
        /// <param name="endpointsProvider">Callback returning the configured endpoints.</param>
        public EndpointRoutes(string? apiKey, Func<List<EndpointConfig>> endpointsProvider)
        {
            _ApiKey = apiKey;
            _EndpointsProvider = endpointsProvider ?? throw new ArgumentNullException(nameof(endpointsProvider));
        }

        /// <summary>
        /// Register routes.
        /// </summary>
        /// <param name="app">Watson webserver.</param>
        public void Register(Webserver app)
        {
            if (app == null) throw new ArgumentNullException(nameof(app));

            app.Get("/v1.0/api/endpoints", async (req) =>
            {
                if (!ApiAuth.Authorize(req.Http, _ApiKey))
                {
                    return await Task.FromResult<object>(new ApiError("Unauthorized", "Authentication required.")).ConfigureAwait(false);
                }

                List<EndpointSummary> items = new List<EndpointSummary>();
                foreach (EndpointConfig endpoint in _EndpointsProvider())
                {
                    items.Add(new EndpointSummary
                    {
                        Name = endpoint.Name,
                        AdapterType = AdapterKebab(endpoint.AdapterType),
                        BaseUrl = string.IsNullOrWhiteSpace(endpoint.BaseUrl) ? null : endpoint.BaseUrl,
                        Model = endpoint.Model,
                        IsDefault = endpoint.IsDefault
                    });
                }

                req.Http.Response.StatusCode = 200;
                return await Task.FromResult<object>(new ListResponse<EndpointSummary>(items)).ConfigureAwait(false);
            });
        }

        private static string AdapterKebab(AdapterTypeEnum adapter)
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
    }
}
