namespace Mux.Server.Routes
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Context;
    using Mux.Core.Enums;
    using Mux.Core.Llm;
    using Mux.Core.Models;
    using Mux.Core.Settings;
    using Mux.Server.Models;
    using WatsonWebserver;

    /// <summary>
    /// Route that builds a model-context block from a file's contents so a thin client (the VS Code extension)
    /// can offload mapping and summarizing to the server, which alone can run the summarizer's model calls and
    /// owns the summary cache. Map and truncate need no model; summarize resolves the default endpoint and runs
    /// the same tools-off sidecar pattern as conversation compaction.
    /// </summary>
    public sealed class ContextRoutes
    {
        private static readonly JsonSerializerOptions _JsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        private readonly string? _ApiKey;
        private readonly Func<List<EndpointConfig>> _EndpointsProvider;

        /// <summary>Instantiate.</summary>
        /// <param name="apiKey">Configured API key, or null for no-auth.</param>
        /// <param name="endpointsProvider">Supplies the configured endpoints (for the summarize path). Must not be null.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="endpointsProvider"/> is null.</exception>
        public ContextRoutes(string? apiKey, Func<List<EndpointConfig>> endpointsProvider)
        {
            _ApiKey = apiKey;
            _EndpointsProvider = endpointsProvider ?? throw new ArgumentNullException(nameof(endpointsProvider));
        }

        /// <summary>Register routes.</summary>
        /// <param name="app">Watson webserver.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="app"/> is null.</exception>
        public void Register(Webserver app)
        {
            if (app == null) throw new ArgumentNullException(nameof(app));

            app.Post("/v1.0/api/context/file", async (req) =>
            {
                if (!ApiAuth.Authorize(req.Http, _ApiKey)) return new ApiError("Unauthorized", "Authentication required.");

                FileContextRequestDto? payload = Parse<FileContextRequestDto>(req.Http.Request.DataAsString);
                if (payload == null || payload.Content == null)
                {
                    req.Http.Response.StatusCode = 400;
                    return (object)new ApiError("BadRequest", "A 'content' field is required.");
                }

                ContextSettings settings = LoadContextSettings();
                ContextSettings.TryNormalizeLargeFileMode(payload.Mode ?? settings.LargeFileMode, out string modeString);
                FileContextMode mode = ToMode(modeString);

                int inlineThreshold = payload.InlineThresholdBytes ?? settings.InlineThresholdBytes;
                int headLines = payload.HeadLines ?? 40;
                int chunkLines = payload.SummaryChunkLines ?? settings.SummaryChunkLines;

                IFileSummarizer? summarizer = null;
                string modelKey = string.Empty;
                if (mode == FileContextMode.Summarize)
                {
                    EndpointConfig? endpoint = ResolveDefaultEndpoint();
                    if (endpoint == null)
                    {
                        mode = FileContextMode.Map;
                    }
                    else
                    {
                        modelKey = endpoint.Model ?? string.Empty;
                        bool ignoreCert = false;
                        try { ignoreCert = SettingsLoader.LoadSettings().IgnoreCertErrors; } catch (Exception) { }
                        FileSummaryCache? cache = settings.SummaryCacheEnabled ? new FileSummaryCache(null) : null;
                        summarizer = new FileSummarizer((system, user, token) => RunSidecarAsync(endpoint, ignoreCert, system, user, token), cache);
                    }
                }

                FileContextBuilder builder = new FileContextBuilder(null);
                FileContextResult result = await builder.BuildAsync(
                    new FileContextRequest(payload.Path, payload.Content, mode, inlineThreshold, headLines, chunkLines, null, modelKey),
                    summarizer,
                    CancellationToken.None).ConfigureAwait(false);

                req.Http.Response.StatusCode = 200;
                return (object)new FileContextResponseDto
                {
                    Text = result.Text,
                    Mode = ToWire(result.Mode),
                    Inlined = result.Inlined,
                    OutlineEntryCount = result.OutlineEntryCount,
                    FromCache = result.FromCache
                };
            }, Documentation.ApiDoc.ContextFilePost);
        }

        private static async Task<string> RunSidecarAsync(EndpointConfig endpoint, bool ignoreCert, string systemPrompt, string userPrompt, CancellationToken token)
        {
            using LlmClient client = new LlmClient(endpoint, ignoreCert);
            ConversationMessage response = await client.SendAsync(
                new List<ConversationMessage>
                {
                    new ConversationMessage { Role = RoleEnum.System, Content = systemPrompt },
                    new ConversationMessage { Role = RoleEnum.User, Content = userPrompt }
                },
                new List<ToolDefinition>(),
                token).ConfigureAwait(false);
            return response.Content?.Trim() ?? string.Empty;
        }

        private EndpointConfig? ResolveDefaultEndpoint()
        {
            List<EndpointConfig> endpoints = _EndpointsProvider();
            return endpoints.FirstOrDefault(e => e.IsDefault) ?? endpoints.FirstOrDefault();
        }

        private static ContextSettings LoadContextSettings()
        {
            try { return SettingsLoader.LoadSettings().Context; } catch (Exception) { return new ContextSettings(); }
        }

        private static FileContextMode ToMode(string normalized)
        {
            switch (normalized)
            {
                case "summarize": return FileContextMode.Summarize;
                case "truncate": return FileContextMode.Truncate;
                default: return FileContextMode.Map;
            }
        }

        private static string ToWire(FileContextMode mode)
        {
            switch (mode)
            {
                case FileContextMode.Summarize: return "summarize";
                case FileContextMode.Truncate: return "truncate";
                default: return "map";
            }
        }

        private T? Parse<T>(string body) where T : class
        {
            try { return JsonSerializer.Deserialize<T>(body ?? string.Empty, _JsonOptions); }
            catch (JsonException) { return null; }
        }
    }
}
