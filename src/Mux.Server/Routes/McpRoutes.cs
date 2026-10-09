namespace Mux.Server.Routes
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Diagnostics;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Enums;
    using Mux.Core.Models;
    using Mux.Core.Settings;
    using Mux.Core.Tools;
    using Mux.Server.Models;
    using WatsonWebserver;

    /// <summary>
    /// CRUD over the configured MCP servers, backed by <see cref="SettingsLoader.SaveMcpServers"/>. The auth
    /// secret (bearer token or API-key value) is never sent to the client; a blank secret on write preserves
    /// the stored value.
    /// </summary>
    public sealed class McpRoutes
    {
        private static readonly JsonSerializerOptions _JsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        private readonly string? _ApiKey;

        /// <summary>Instantiate.</summary>
        /// <param name="apiKey">Configured API key, or null for no-auth.</param>
        public McpRoutes(string? apiKey)
        {
            _ApiKey = apiKey;
        }

        /// <summary>Register routes.</summary>
        /// <param name="app">Watson webserver.</param>
        public void Register(Webserver app)
        {
            if (app == null) throw new ArgumentNullException(nameof(app));

            app.Get("/v1.0/api/mcp-servers", async (req) =>
            {
                if (!ApiAuth.Authorize(req.Http, _ApiKey)) return Unauthorized();
                List<McpServerDto> items = SettingsLoader.LoadMcpServers().Select(ToDto).ToList();
                req.Http.Response.StatusCode = 200;
                return await Task.FromResult<object>(new ListResponse<McpServerDto>(items)).ConfigureAwait(false);
            }, Documentation.ApiDoc.McpGet);

            app.Put("/v1.0/api/mcp-servers", async (req) =>
            {
                if (!ApiAuth.Authorize(req.Http, _ApiKey)) return Unauthorized();

                ListResponse<McpServerDto>? payload;
                try { payload = JsonSerializer.Deserialize<ListResponse<McpServerDto>>(req.Http.Request.DataAsString ?? string.Empty, _JsonOptions); }
                catch (Exception) { req.Http.Response.StatusCode = 400; return (object)new ApiError("BadRequest", "Request body is not valid JSON."); }

                if (payload?.Items == null) { req.Http.Response.StatusCode = 400; return (object)new ApiError("BadRequest", "An 'items' array is required."); }

                try
                {
                    Dictionary<string, McpServerConfig> existing = SettingsLoader.LoadMcpServers()
                        .GroupBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

                    List<McpServerConfig> merged = new List<McpServerConfig>();
                    foreach (McpServerDto dto in payload.Items)
                    {
                        if (string.IsNullOrWhiteSpace(dto.Name)) { req.Http.Response.StatusCode = 400; return (object)new ApiError("BadRequest", "Every MCP server needs a name."); }
                        existing.TryGetValue(dto.Name, out McpServerConfig? prior);
                        merged.Add(FromDto(dto, prior));
                    }

                    SettingsLoader.SaveMcpServers(merged);
                    req.Http.Response.StatusCode = 200;
                    return await Task.FromResult<object>(new ListResponse<McpServerDto>(SettingsLoader.LoadMcpServers().Select(ToDto).ToList())).ConfigureAwait(false);
                }
                catch (Exception ex) { req.Http.Response.StatusCode = 500; return (object)new ApiError("SaveFailed", "Failed to save MCP servers: " + ex.Message); }
            }, Documentation.ApiDoc.McpPut);

            app.Delete("/v1.0/api/mcp-servers", async (req) =>
            {
                if (!ApiAuth.Authorize(req.Http, _ApiKey)) return Unauthorized();
                string? name = req.Http.Request.Query.Elements["name"];
                if (string.IsNullOrWhiteSpace(name)) { req.Http.Response.StatusCode = 400; return (object)new ApiError("BadRequest", "A 'name' query parameter is required."); }
                try
                {
                    List<McpServerConfig> remaining = SettingsLoader.LoadMcpServers().Where(s => !string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)).ToList();
                    SettingsLoader.SaveMcpServers(remaining);
                    req.Http.Response.StatusCode = 200;
                    return await Task.FromResult<object>(new ListResponse<McpServerDto>(SettingsLoader.LoadMcpServers().Select(ToDto).ToList())).ConfigureAwait(false);
                }
                catch (Exception ex) { req.Http.Response.StatusCode = 500; return (object)new ApiError("DeleteFailed", "Failed to delete MCP server: " + ex.Message); }
            }, Documentation.ApiDoc.McpDelete);

            app.Post("/v1.0/api/mcp-servers/validate", async (req) =>
            {
                if (!ApiAuth.Authorize(req.Http, _ApiKey)) return Unauthorized();

                McpValidateRequestDto? payload;
                try { payload = JsonSerializer.Deserialize<McpValidateRequestDto>(req.Http.Request.DataAsString ?? string.Empty, _JsonOptions); }
                catch (Exception) { req.Http.Response.StatusCode = 400; return (object)new ApiError("BadRequest", "Request body is not valid JSON."); }

                if (payload == null || (payload.Server == null && string.IsNullOrWhiteSpace(payload.Name)))
                {
                    req.Http.Response.StatusCode = 400;
                    return (object)new ApiError("BadRequest", "Pass the 'Name' of a saved server or a 'Server' definition.");
                }

                List<McpServerConfig> saved = SettingsLoader.LoadMcpServers();
                McpServerConfig? config;
                if (payload.Server != null)
                {
                    if (string.IsNullOrWhiteSpace(payload.Server.Name)) { req.Http.Response.StatusCode = 400; return (object)new ApiError("BadRequest", "The server needs a name."); }
                    McpServerConfig? prior = saved.FirstOrDefault(s => string.Equals(s.Name, payload.Server.Name.Trim(), StringComparison.OrdinalIgnoreCase));
                    config = FromDto(payload.Server, prior);
                }
                else
                {
                    config = saved.FirstOrDefault(s => string.Equals(s.Name, payload.Name!.Trim(), StringComparison.OrdinalIgnoreCase));
                    if (config == null) { req.Http.Response.StatusCode = 404; return (object)new ApiError("NotFound", "No MCP server named '" + payload.Name + "'."); }
                }

                int timeoutSeconds = Math.Clamp(payload.TimeoutSeconds ?? 30, 1, 120);
                McpValidateResponseDto response = await ValidateAsync(config, TimeSpan.FromSeconds(timeoutSeconds), req.Http.Token).ConfigureAwait(false);
                req.Http.Response.StatusCode = 200;
                return (object)response;
            }, Documentation.ApiDoc.McpValidate);
        }

        /// <summary>
        /// Connects to one MCP server, lists its tools, and reports the outcome with full failure details.
        /// </summary>
        /// <param name="config">The server to validate.</param>
        /// <param name="timeout">How long to wait.</param>
        /// <param name="cancellationToken">Cancels the attempt.</param>
        /// <returns>The validation result.</returns>
        internal static async Task<McpValidateResponseDto> ValidateAsync(McpServerConfig config, TimeSpan timeout, CancellationToken cancellationToken)
        {
            Stopwatch watch = Stopwatch.StartNew();
            McpValidateResponseDto response = new McpValidateResponseDto
            {
                Name = config.Name,
                Method = config.Transport == McpTransportTypeEnum.Http ? "http" : "stdio"
            };

            try
            {
                using (McpToolManager manager = new McpToolManager(new List<McpServerConfig> { config }))
                using (CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    cts.CancelAfter(timeout);
                    await manager.InitializeAsync(cts.Token).ConfigureAwait(false);
                    McpConnectionResult? result = manager.GetConnectionResults().FirstOrDefault();
                    if (result != null)
                    {
                        response.Connected = result.Connected;
                        response.Method = result.Method;
                        response.ToolCount = result.ToolCount;
                        SplitError(result.Error, result.Details, response);
                    }
                    else
                    {
                        response.Error = "No connection result was reported.";
                    }

                    response.Tools = manager.GetToolDefinitions().Select(t => t.Name).ToList();
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                response.Connected = false;
                response.Error = "Validation of MCP server '" + config.Name + "' timed out after " + Math.Round(timeout.TotalSeconds) + " s.";
                response.Details = config.Transport == McpTransportTypeEnum.Http
                    ? "URL: " + McpConnectionDiagnostics.BuildRpcUrl(config.Url, config.McpPath) + "\nThe server accepted the connection but did not finish the MCP handshake in time."
                    : "Command: " + config.Command + "\nThe process did not finish the MCP handshake in time.";
            }
            catch (Exception ex)
            {
                response.Connected = false;
                SplitError(ex.Message, (ex as McpConnectionException)?.Details, response);
            }

            response.ElapsedMs = watch.ElapsedMilliseconds;
            return response;
        }

        private static void SplitError(string? error, string? details, McpValidateResponseDto response)
        {
            if (string.IsNullOrEmpty(error))
            {
                return;
            }

            string normalized = error.Replace("\r\n", "\n");
            int newline = normalized.IndexOf('\n');
            response.Error = newline < 0 ? normalized : normalized.Substring(0, newline);
            string rest = newline < 0 ? string.Empty : normalized.Substring(newline + 1);
            response.Details = !string.IsNullOrWhiteSpace(details) ? details!.Replace("\r\n", "\n") : (rest.Length > 0 ? rest : null);
        }

        private object Unauthorized() => new ApiError("Unauthorized", "Authentication required.");

        private static McpServerDto ToDto(McpServerConfig s)
        {
            McpAuthConfig auth = s.Auth ?? new McpAuthConfig();
            bool secretSet = !string.IsNullOrWhiteSpace(auth.BearerToken) || !string.IsNullOrWhiteSpace(auth.ApiKeyValue);
            List<string> env = new List<string>();
            if (s.Env != null)
            {
                foreach (KeyValuePair<string, string> kv in s.Env) env.Add(kv.Key + "=" + kv.Value);
            }

            return new McpServerDto
            {
                Name = s.Name,
                Transport = s.Transport == McpTransportTypeEnum.Http ? "http" : "stdio",
                Command = string.IsNullOrWhiteSpace(s.Command) ? null : s.Command,
                Args = s.Args != null ? new List<string>(s.Args) : new List<string>(),
                Env = env,
                Url = string.IsNullOrWhiteSpace(s.Url) ? null : s.Url,
                McpPath = string.IsNullOrWhiteSpace(s.McpPath) ? null : s.McpPath,
                AuthType = AuthKebab(auth.Type),
                AuthHeader = string.IsNullOrWhiteSpace(auth.ApiKeyHeader) ? null : auth.ApiKeyHeader,
                AuthSecretSet = secretSet,
                AuthSecret = null
            };
        }

        private static McpServerConfig FromDto(McpServerDto dto, McpServerConfig? prior)
        {
            McpServerConfig config = new McpServerConfig
            {
                Name = dto.Name.Trim(),
                Transport = string.Equals(dto.Transport, "http", StringComparison.OrdinalIgnoreCase) ? McpTransportTypeEnum.Http : McpTransportTypeEnum.Stdio,
                Command = dto.Command ?? string.Empty,
                Args = dto.Args ?? new List<string>(),
                Env = new Dictionary<string, string>(),
                Url = dto.Url ?? string.Empty,
                McpPath = string.IsNullOrWhiteSpace(dto.McpPath) ? "/mcp" : dto.McpPath
            };

            foreach (string entry in dto.Env ?? new List<string>())
            {
                int eq = (entry ?? string.Empty).IndexOf('=');
                if (eq > 0) config.Env[entry!.Substring(0, eq).Trim()] = entry.Substring(eq + 1);
            }

            McpAuthTypeEnum authType = ParseAuth(dto.AuthType);
            string priorSecret = authType == McpAuthTypeEnum.Bearer
                ? (prior?.Auth?.BearerToken ?? string.Empty)
                : (prior?.Auth?.ApiKeyValue ?? string.Empty);
            string secret = string.IsNullOrWhiteSpace(dto.AuthSecret) ? priorSecret : dto.AuthSecret!;

            config.Auth = new McpAuthConfig
            {
                Type = authType,
                ApiKeyHeader = string.IsNullOrWhiteSpace(dto.AuthHeader) ? "X-API-Key" : dto.AuthHeader!,
                BearerToken = authType == McpAuthTypeEnum.Bearer ? secret : string.Empty,
                ApiKeyValue = authType == McpAuthTypeEnum.ApiKey ? secret : string.Empty
            };

            return config;
        }

        private static string AuthKebab(McpAuthTypeEnum t)
        {
            switch (t)
            {
                case McpAuthTypeEnum.Bearer: return "bearer";
                case McpAuthTypeEnum.ApiKey: return "apikey";
                default: return "none";
            }
        }

        private static McpAuthTypeEnum ParseAuth(string? s)
        {
            switch ((s ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "bearer": return McpAuthTypeEnum.Bearer;
                case "apikey": case "api-key": return McpAuthTypeEnum.ApiKey;
                default: return McpAuthTypeEnum.None;
            }
        }
    }
}
