namespace Mux.Server.Routes
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading.Tasks;
    using Mux.Core.Enums;
    using Mux.Core.Models;
    using Mux.Core.Settings;
    using Mux.Core.Sessions;
    using Mux.Core.Skills;
    using Mux.Server.Models;
    using WatsonWebserver;

    /// <summary>
    /// The Home/Overview aggregate route. Computes a command-center summary purely from mux's config files,
    /// the session store, and server facts — no telemetry, no database.
    /// </summary>
    public sealed class OverviewRoutes
    {
        private readonly string? _ApiKey;
        private readonly Func<List<EndpointConfig>> _EndpointsProvider;
        private readonly SessionStore _SessionStore;
        private readonly string _Version;
        private readonly DateTime _StartUtc;

        /// <summary>Instantiate.</summary>
        /// <param name="apiKey">Configured API key, or null for no-auth.</param>
        /// <param name="endpointsProvider">Callback returning the configured endpoints.</param>
        /// <param name="sessionStore">The session store.</param>
        /// <param name="version">Product version.</param>
        /// <param name="startUtc">Server start time (for uptime).</param>
        public OverviewRoutes(string? apiKey, Func<List<EndpointConfig>> endpointsProvider, SessionStore sessionStore, string version, DateTime startUtc)
        {
            _ApiKey = apiKey;
            _EndpointsProvider = endpointsProvider ?? throw new ArgumentNullException(nameof(endpointsProvider));
            _SessionStore = sessionStore ?? throw new ArgumentNullException(nameof(sessionStore));
            _Version = version ?? string.Empty;
            _StartUtc = startUtc;
        }

        /// <summary>Register routes.</summary>
        /// <param name="app">Watson webserver.</param>
        public void Register(Webserver app)
        {
            if (app == null) throw new ArgumentNullException(nameof(app));

            app.Get("/v1.0/api/overview", async (req) =>
            {
                if (!ApiAuth.Authorize(req.Http, _ApiKey)) return (object)new ApiError("Unauthorized", "Authentication required.");

                OverviewDto dto = new OverviewDto
                {
                    Version = _Version,
                    ConfigDir = SafeConfigDir(),
                    Uptime = FormatUptime(DateTime.UtcNow - _StartUtc),
                    AuthEnabled = !string.IsNullOrEmpty(_ApiKey)
                };

                List<EndpointConfig> endpoints = _EndpointsProvider();
                dto.Endpoints = endpoints.Count;
                EndpointConfig? def = endpoints.FirstOrDefault(e => e.IsDefault) ?? endpoints.FirstOrDefault();
                if (def != null && endpoints.Any(e => e.IsDefault))
                {
                    dto.DefaultEndpoint = def.Name;
                    dto.DefaultAdapter = def.AdapterType.ToString().ToLowerInvariant();
                    dto.DefaultModel = def.Model;
                }

                dto.McpServers = SafeCount(() => SettingsLoader.LoadMcpServers().Count);
                dto.Prompts = SafeCount(() => SettingsLoader.LoadPrompts().Count);
                dto.ActivePrompt = Safe(() => SettingsLoader.GetActivePromptProfile().Name);
                dto.Subagents = SafeCount(() => SettingsLoader.LoadSubagents().Count);
                dto.Keybindings = SafeCount(() => SettingsLoader.LoadKeybindings().Count);

                try
                {
                    Mux.Core.Plugins.PluginConfig plugins = SettingsLoader.LoadPluginConfig();
                    dto.Hooks = plugins.Hooks.Count;
                    dto.Commands = plugins.Commands.Count;
                }
                catch (Exception) { }

                LoadSkillCounts(dto);
                await LoadSessionsAsync(dto, req.Http.Token).ConfigureAwait(false);
                BuildNotices(dto);

                req.Http.Response.StatusCode = 200;
                return (object)dto;
            });
        }

        private static void LoadSkillCounts(OverviewDto dto)
        {
            try
            {
                MuxSettings settings = SettingsLoader.LoadSettings();
                string dir = SettingsLoader.ResolveSkillsDirectory(settings);
                if (!Directory.Exists(dir)) return;

                Dictionary<string, bool> enabled = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
                foreach (SkillIndexEntry entry in SettingsLoader.LoadSkillIndex())
                {
                    if (!string.IsNullOrWhiteSpace(entry.Id)) enabled[entry.Id] = entry.Enabled;
                }

                foreach (Skill skill in new SkillLoader(dir).Discover())
                {
                    dto.SkillsTotal++;
                    if (!skill.IsValid) dto.SkillsInvalid++;
                    bool on = enabled.TryGetValue(skill.Manifest.Name, out bool e) ? e : skill.Manifest.Enabled;
                    if (on) dto.SkillsEnabled++;
                }
            }
            catch (Exception) { }
        }

        private async Task LoadSessionsAsync(OverviewDto dto, System.Threading.CancellationToken token)
        {
            try
            {
                IReadOnlyList<SessionSnapshot> snapshots = await _SessionStore.ListAsync(token).ConfigureAwait(false);
                dto.Sessions = snapshots.Count;
                foreach (SessionSnapshot s in snapshots) dto.TotalMessages += s.ConversationHistory.Count;

                foreach (SessionSnapshot s in snapshots.OrderByDescending(x => x.UpdatedUtc).Take(5))
                {
                    dto.RecentSessions.Add(new SessionSummary
                    {
                        Id = s.Id,
                        Title = s.Title,
                        EndpointName = s.EndpointName,
                        Model = s.Model,
                        CreatedUtc = s.CreatedUtc,
                        UpdatedUtc = s.UpdatedUtc,
                        MessageCount = s.ConversationHistory.Count
                    });
                }
            }
            catch (Exception) { }
        }

        private static void BuildNotices(OverviewDto dto)
        {
            if (dto.Endpoints == 0)
            {
                dto.Notices.Add(new OverviewNotice("warning", "No endpoints are configured. Add one on the Endpoints page to start chatting."));
            }
            else if (string.IsNullOrEmpty(dto.DefaultEndpoint))
            {
                dto.Notices.Add(new OverviewNotice("warning", "No default endpoint is set. Mark one as default so it is used automatically."));
            }
            else if (dto.DefaultAdapter == "ollama" || dto.DefaultAdapter == "vllm" || dto.DefaultAdapter == "openaicompatible")
            {
                dto.Notices.Add(new OverviewNotice("info", "Your default endpoint runs a local model. Add a cloud provider (Anthropic, Gemini, Azure, …) for frontier models."));
            }

            if (dto.SkillsInvalid > 0)
            {
                dto.Notices.Add(new OverviewNotice("warning", dto.SkillsInvalid + " skill" + (dto.SkillsInvalid == 1 ? "" : "s") + " fail validation and will not be offered to the model."));
            }

            if (dto.Sessions == 0)
            {
                dto.Notices.Add(new OverviewNotice("info", "No saved sessions yet. Your interactive sessions will appear here as you use mux."));
            }

            if (dto.Notices.Count == 0)
            {
                dto.Notices.Add(new OverviewNotice("success", "Configuration looks healthy."));
            }
        }

        private static string SafeConfigDir()
        {
            try { return SettingsLoader.GetConfigDirectory(); } catch (Exception) { return string.Empty; }
        }

        private static int SafeCount(Func<int> f)
        {
            try { return f(); } catch (Exception) { return 0; }
        }

        private static string? Safe(Func<string?> f)
        {
            try { return f(); } catch (Exception) { return null; }
        }

        private static string FormatUptime(TimeSpan up)
        {
            if (up.TotalDays >= 1) return (int)up.TotalDays + "d " + up.Hours + "h " + up.Minutes + "m";
            if (up.TotalHours >= 1) return up.Hours + "h " + up.Minutes + "m";
            if (up.TotalMinutes >= 1) return up.Minutes + "m " + up.Seconds + "s";
            return up.Seconds + "s";
        }
    }
}
