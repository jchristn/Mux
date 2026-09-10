namespace Mux.Server.Routes
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
    using System.Threading.Tasks;
    using Mux.Core.Enums;
    using Mux.Core.Models;
    using Mux.Core.Settings;
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
            });

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
                    return (object)new ListResponse<McpServerDto>(SettingsLoader.LoadMcpServers().Select(ToDto).ToList());
                }
                catch (Exception ex) { req.Http.Response.StatusCode = 500; return (object)new ApiError("SaveFailed", "Failed to save MCP servers: " + ex.Message); }
            });

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
            });
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
