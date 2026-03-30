namespace Mux.Core.Settings
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text.Json;
    using System.Text.Json.Nodes;
    using System.Text.Json.Serialization;
    using System.Text.RegularExpressions;
    using Mux.Core.Enums;
    using Mux.Core.Models;

    /// <summary>
    /// Loads and resolves mux configuration from disk and environment.
    /// </summary>
    public class SettingsLoader
    {
        #region Private-Members

        private static readonly JsonSerializerOptions _JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Returns the mux configuration directory path.
        /// Uses the <c>MUX_CONFIG_DIR</c> environment variable if set, otherwise defaults to <c>~/.mux/</c>.
        /// </summary>
        /// <returns>The absolute path to the configuration directory.</returns>
        public static string GetConfigDirectory()
        {
            string? envDir = Environment.GetEnvironmentVariable("MUX_CONFIG_DIR");
            if (!string.IsNullOrWhiteSpace(envDir))
            {
                return envDir;
            }

            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, ".mux");
        }

        /// <summary>
        /// Ensures the mux configuration directory exists, creating it and seeding
        /// default configuration files if necessary.
        /// </summary>
        public static void EnsureConfigDirectory()
        {
            string configDir = GetConfigDirectory();
            if (!Directory.Exists(configDir))
            {
                Directory.CreateDirectory(configDir);
            }

            string endpointsPath = Path.Combine(configDir, "endpoints.json");
            if (!File.Exists(endpointsPath))
            {
                JsonObject defaultEndpoint = new JsonObject
                {
                    ["name"] = "ollama-local",
                    ["adapterType"] = "ollama",
                    ["baseUrl"] = "http://localhost:11434/v1",
                    ["model"] = "qwen2.5-coder:7b",
                    ["isDefault"] = true,
                    ["maxTokens"] = 8192,
                    ["temperature"] = 0.1,
                    ["contextWindow"] = 32768,
                    ["timeoutMs"] = 120000,
                    ["apiKey"] = (JsonNode?)null,
                    ["bearerToken"] = (JsonNode?)null,
                    ["quirks"] = (JsonNode?)null
                };

                JsonObject root = new JsonObject
                {
                    ["endpoints"] = new JsonArray(defaultEndpoint)
                };

                string defaultEndpoints = root.ToJsonString(
                    new JsonSerializerOptions { WriteIndented = true });

                File.WriteAllText(endpointsPath, defaultEndpoints);
            }
        }

        /// <summary>
        /// Loads the list of endpoint configurations from <c>~/.mux/endpoints.json</c>.
        /// </summary>
        /// <returns>A list of <see cref="EndpointConfig"/> instances, or an empty list if the file does not exist.</returns>
        public static List<EndpointConfig> LoadEndpoints()
        {
            string filePath = Path.Combine(GetConfigDirectory(), "endpoints.json");
            if (!File.Exists(filePath))
            {
                return new List<EndpointConfig>();
            }

            string json = File.ReadAllText(filePath);
            EndpointsFile? file = JsonSerializer.Deserialize<EndpointsFile>(json, _JsonOptions);
            if (file == null || file.Endpoints == null)
            {
                return new List<EndpointConfig>();
            }

            return file.Endpoints;
        }

        /// <summary>
        /// Loads the global mux settings from <c>~/.mux/settings.json</c>.
        /// </summary>
        /// <returns>A <see cref="MuxSettings"/> instance, or a default instance if the file does not exist.</returns>
        public static MuxSettings LoadSettings()
        {
            string filePath = Path.Combine(GetConfigDirectory(), "settings.json");
            if (!File.Exists(filePath))
            {
                return new MuxSettings();
            }

            string json = File.ReadAllText(filePath);
            MuxSettings? settings = JsonSerializer.Deserialize<MuxSettings>(json, _JsonOptions);
            return settings ?? new MuxSettings();
        }

        /// <summary>
        /// Loads the list of MCP server configurations from <c>~/.mux/mcp-servers.json</c>.
        /// </summary>
        /// <returns>A list of <see cref="McpServerConfig"/> instances, or an empty list if the file does not exist.</returns>
        public static List<McpServerConfig> LoadMcpServers()
        {
            string filePath = Path.Combine(GetConfigDirectory(), "mcp-servers.json");
            if (!File.Exists(filePath))
            {
                return new List<McpServerConfig>();
            }

            string json = File.ReadAllText(filePath);
            McpServersFile? file = JsonSerializer.Deserialize<McpServersFile>(json, _JsonOptions);
            if (file == null || file.Servers == null)
            {
                return new List<McpServerConfig>();
            }

            return file.Servers;
        }

        /// <summary>
        /// Resolves the system prompt to use, checking sources in priority order:
        /// CLI flag override, settings file path, <c>~/.mux/system-prompt.md</c>, then the built-in default.
        /// </summary>
        /// <param name="cliOverridePath">An optional file path provided via CLI flag.</param>
        /// <param name="settings">The current mux settings.</param>
        /// <returns>The resolved system prompt string.</returns>
        public static string LoadSystemPrompt(string? cliOverridePath, MuxSettings settings)
        {
            if (!string.IsNullOrWhiteSpace(cliOverridePath))
            {
                string expandedCliPath = ExpandEnvironmentVariables(cliOverridePath!);
                if (File.Exists(expandedCliPath))
                {
                    return File.ReadAllText(expandedCliPath);
                }
            }

            if (!string.IsNullOrWhiteSpace(settings.SystemPromptPath))
            {
                string expandedSettingsPath = ExpandEnvironmentVariables(settings.SystemPromptPath!);
                if (File.Exists(expandedSettingsPath))
                {
                    return File.ReadAllText(expandedSettingsPath);
                }
            }

            string defaultPromptPath = Path.Combine(GetConfigDirectory(), "system-prompt.md");
            if (File.Exists(defaultPromptPath))
            {
                return File.ReadAllText(defaultPromptPath);
            }

            return Defaults.SystemPrompt;
        }

        /// <summary>
        /// Resolves the effective endpoint configuration by merging the named or default endpoint
        /// with any CLI overrides.
        /// </summary>
        /// <param name="endpoints">The loaded list of endpoint configurations.</param>
        /// <param name="endpointName">An optional endpoint name to select.</param>
        /// <param name="cliModel">An optional model name override from the CLI.</param>
        /// <param name="cliBaseUrl">An optional base URL override from the CLI.</param>
        /// <param name="cliAdapterType">An optional adapter type override from the CLI.</param>
        /// <param name="cliApiKey">An optional API key override from the CLI.</param>
        /// <param name="cliTemperature">An optional temperature override from the CLI.</param>
        /// <param name="cliMaxTokens">An optional max tokens override from the CLI.</param>
        /// <returns>The fully resolved <see cref="EndpointConfig"/>.</returns>
        public static EndpointConfig ResolveEndpoint(
            List<EndpointConfig> endpoints,
            string? endpointName,
            string? cliModel,
            string? cliBaseUrl,
            string? cliAdapterType,
            string? cliApiKey,
            double? cliTemperature,
            int? cliMaxTokens)
        {
            EndpointConfig? selected = null;

            if (!string.IsNullOrWhiteSpace(endpointName))
            {
                selected = endpoints.FirstOrDefault(
                    (EndpointConfig e) => string.Equals(e.Name, endpointName, StringComparison.OrdinalIgnoreCase));
            }

            if (selected == null)
            {
                selected = endpoints.FirstOrDefault((EndpointConfig e) => e.IsDefault);
            }

            if (selected == null && endpoints.Count > 0)
            {
                selected = endpoints[0];
            }

            if (selected == null)
            {
                selected = new EndpointConfig
                {
                    Name = "ollama-local",
                    AdapterType = AdapterTypeEnum.Ollama,
                    BaseUrl = "http://localhost:11434/v1",
                    Model = "qwen2.5-coder:7b"
                };
            }

            if (!string.IsNullOrWhiteSpace(cliModel))
            {
                selected.Model = cliModel!;
            }

            if (!string.IsNullOrWhiteSpace(cliBaseUrl))
            {
                selected.BaseUrl = cliBaseUrl!;
            }

            if (!string.IsNullOrWhiteSpace(cliAdapterType))
            {
                if (Enum.TryParse<AdapterTypeEnum>(cliAdapterType, true, out AdapterTypeEnum parsedAdapter))
                {
                    selected.AdapterType = parsedAdapter;
                }
            }

            if (!string.IsNullOrWhiteSpace(cliApiKey))
            {
                selected.ApiKey = ExpandEnvironmentVariables(cliApiKey!);
            }

            if (cliTemperature.HasValue)
            {
                selected.Temperature = cliTemperature.Value;
            }

            if (cliMaxTokens.HasValue)
            {
                selected.MaxTokens = cliMaxTokens.Value;
            }

            if (!string.IsNullOrWhiteSpace(selected.ApiKey))
            {
                selected.ApiKey = ExpandEnvironmentVariables(selected.ApiKey!);
            }

            if (!string.IsNullOrWhiteSpace(selected.BearerToken))
            {
                selected.BearerToken = ExpandEnvironmentVariables(selected.BearerToken!);
            }

            if (selected.Quirks == null)
            {
                selected.Quirks = Defaults.QuirksForAdapter(selected.AdapterType);
            }

            return selected;
        }

        /// <summary>
        /// Expands environment variable references in the form <c>${VAR_NAME}</c> within the given string.
        /// </summary>
        /// <param name="value">The input string potentially containing <c>${VAR_NAME}</c> patterns.</param>
        /// <returns>The string with all recognized environment variable references replaced.</returns>
        public static string ExpandEnvironmentVariables(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return value;
            }

            return Regex.Replace(value, @"\$\{([^}]+)\}", (Match match) =>
            {
                string varName = match.Groups[1].Value;
                string? envValue = Environment.GetEnvironmentVariable(varName);
                return envValue ?? match.Value;
            });
        }

        #endregion

        #region Private-Members

        /// <summary>
        /// Wrapper class for deserializing the endpoints JSON file.
        /// </summary>
        private class EndpointsFile
        {
            /// <summary>
            /// The list of endpoint configurations.
            /// </summary>
            [JsonPropertyName("endpoints")]
            public List<EndpointConfig>? Endpoints { get; set; }
        }

        /// <summary>
        /// Wrapper class for deserializing the MCP servers JSON file.
        /// </summary>
        private class McpServersFile
        {
            /// <summary>
            /// The list of MCP server configurations.
            /// </summary>
            [JsonPropertyName("servers")]
            public List<McpServerConfig>? Servers { get; set; }
        }

        #endregion
    }
}
