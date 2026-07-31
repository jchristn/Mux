namespace Mux.Core.Settings
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using System.Text.RegularExpressions;
    using System.Threading;
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

        private static readonly JsonSerializerOptions _JsonWriteOptions = new JsonSerializerOptions
        {
            WriteIndented = true
        };

        private static readonly AsyncLocal<string?> _ConfigDirectoryOverride = new AsyncLocal<string?>();

        #endregion

        #region Public-Methods

        /// <summary>
        /// Returns the mux configuration directory path.
        /// Uses the <c>MUX_CONFIG_DIR</c> environment variable if set, otherwise defaults to <c>~/.mux/</c>.
        /// </summary>
        /// <returns>The absolute path to the configuration directory.</returns>
        public static string GetConfigDirectory()
        {
            if (!string.IsNullOrWhiteSpace(_ConfigDirectoryOverride.Value))
            {
                return _ConfigDirectoryOverride.Value!;
            }

            string? envDir = Environment.GetEnvironmentVariable("MUX_CONFIG_DIR");
            if (!string.IsNullOrWhiteSpace(envDir))
            {
                return NormalizeConfigDirectory(envDir)!;
            }

            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.GetFullPath(Path.Combine(home, ".mux"));
        }

        /// <summary>
        /// Temporarily overrides the active config directory for the current async flow.
        /// </summary>
        /// <param name="configDirectory">The directory to use, or null to clear the override.</param>
        /// <returns>An <see cref="IDisposable"/> that restores the previous override on disposal.</returns>
        public static IDisposable PushConfigDirectoryOverride(string? configDirectory)
        {
            string? previous = _ConfigDirectoryOverride.Value;
            _ConfigDirectoryOverride.Value = NormalizeConfigDirectory(configDirectory);
            return new ConfigDirectoryOverrideScope(previous);
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
                EndpointConfig defaultEndpoint = new EndpointConfig
                {
                    Name = "ollama-local",
                    AdapterType = AdapterTypeEnum.Ollama,
                    BaseUrl = "http://localhost:11434",
                    Model = "qwen2.5-coder:7b",
                    IsDefault = true,
                    MaxTokens = 8192,
                    Temperature = 0.1,
                    ContextWindow = 32768,
                    TimeoutMs = 120000,
                    Headers = new Dictionary<string, string>(),
                    AutoApproveTools = false,
                    MaxAgentIterations = null,
                    Quirks = null
                };

                EndpointsFile root = new EndpointsFile
                {
                    Endpoints = new List<EndpointConfig> { defaultEndpoint }
                };

                string defaultEndpoints = JsonSerializer.Serialize(root, _JsonWriteOptions);
                File.WriteAllText(endpointsPath, defaultEndpoints);
            }

            string settingsPath = Path.Combine(configDir, "settings.json");
            if (!File.Exists(settingsPath))
            {
                MuxSettings defaultSettings = NormalizeSettingsForPersistence(new MuxSettings());
                string defaultSettingsJson = JsonSerializer.Serialize(defaultSettings, _JsonWriteOptions);
                File.WriteAllText(settingsPath, defaultSettingsJson);
            }

            string promptsPath = Path.Combine(configDir, "prompts.json");
            if (!File.Exists(promptsPath))
            {
                // Seed a single active "Default" profile with empty fields: every prompt inherits its
                // built-in default until the user edits it, so existing system-prompt overrides are
                // unaffected on upgrade.
                PromptsFile promptsRoot = new PromptsFile
                {
                    Prompts = new List<PromptProfile>
                    {
                        new PromptProfile { Name = "Default", IsActive = true }
                    }
                };

                string defaultPrompts = JsonSerializer.Serialize(promptsRoot, _JsonWriteOptions);
                File.WriteAllText(promptsPath, defaultPrompts);
            }

            string skillsDir = Path.Combine(configDir, "skills");
            if (!Directory.Exists(skillsDir))
            {
                Directory.CreateDirectory(skillsDir);
            }
        }

        /// <summary>
        /// Returns the default skills directory (<c>~/.mux/skills</c>) under the active config directory.
        /// </summary>
        /// <returns>The absolute path of the skills directory.</returns>
        public static string GetSkillsDirectory()
        {
            return Path.Combine(GetConfigDirectory(), "skills");
        }

        /// <summary>
        /// Loads the skills index (<c>~/.mux/skills.json</c>), which holds per-skill enablement and pinning.
        /// </summary>
        /// <returns>The index entries, or an empty list when the file does not exist or cannot be read.</returns>
        public static List<SkillIndexEntry> LoadSkillIndex()
        {
            string filePath = Path.Combine(GetConfigDirectory(), "skills.json");
            if (!File.Exists(filePath))
            {
                return new List<SkillIndexEntry>();
            }

            try
            {
                string json = ReadAllTextShared(filePath);
                SkillIndexFile? file = JsonSerializer.Deserialize<SkillIndexFile>(json, _JsonOptions);
                return file?.Skills ?? new List<SkillIndexEntry>();
            }
            catch (JsonException)
            {
                return new List<SkillIndexEntry>();
            }
            catch (IOException)
            {
                return new List<SkillIndexEntry>();
            }
        }

        /// <summary>
        /// Saves the skills index to <c>~/.mux/skills.json</c>.
        /// </summary>
        /// <param name="entries">The index entries to persist. Must not be null.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="entries"/> is null.</exception>
        public static void SaveSkillIndex(List<SkillIndexEntry> entries)
        {
            if (entries == null)
            {
                throw new ArgumentNullException(nameof(entries));
            }

            EnsureConfigDirectory();
            SkillIndexFile file = new SkillIndexFile { Skills = entries };
            string json = JsonSerializer.Serialize(file, _JsonWriteOptions);
            WriteAllTextAtomic(Path.Combine(GetConfigDirectory(), "skills.json"), json);
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

            string json = ReadAllTextShared(filePath);
            EndpointsFile? file = JsonSerializer.Deserialize<EndpointsFile>(json, _JsonOptions);
            if (file == null || file.Endpoints == null)
            {
                return new List<EndpointConfig>();
            }

            return file.Endpoints;
        }

        /// <summary>
        /// Reads a text file allowing concurrent readers/writers to hold the file, so a read during a
        /// concurrent atomic replace does not raise a sharing violation.
        /// </summary>
        /// <param name="path">The file path.</param>
        /// <returns>The file contents.</returns>
        private static string ReadAllTextShared(string path)
        {
            // Allow ReadWrite and Delete sharing so a concurrent atomic replace (write temp + move over
            // this path) is not blocked by an in-flight reader, and vice versa; retry briefly to absorb
            // transient Windows sharing/pending-delete windows.
            string result = string.Empty;
            WithIoRetry(() =>
            {
                using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (StreamReader reader = new StreamReader(stream))
                {
                    result = reader.ReadToEnd();
                }
            });

            return result;
        }

        private static void WithIoRetry(Action action)
        {
            const int attempts = 12;
            for (int i = 0; i < attempts; i++)
            {
                try
                {
                    action();
                    return;
                }
                catch (IOException) when (i < attempts - 1)
                {
                    System.Threading.Thread.Sleep(20);
                }
                catch (UnauthorizedAccessException) when (i < attempts - 1)
                {
                    System.Threading.Thread.Sleep(20);
                }
            }
        }

        /// <summary>
        /// Writes a text file atomically: writes a temporary sibling and moves it over the target so
        /// readers never observe a partially written or write-locked file.
        /// </summary>
        /// <param name="path">The destination path.</param>
        /// <param name="content">The content to write.</param>
        private static void WriteAllTextAtomic(string path, string content)
        {
            string directory = Path.GetDirectoryName(path) ?? GetConfigDirectory();
            Directory.CreateDirectory(directory);
            string tempPath = Path.Combine(directory, Path.GetRandomFileName());
            File.WriteAllText(tempPath, content);
            WithIoRetry(() => File.Move(tempPath, path, overwrite: true));
        }

        /// <summary>
        /// Saves endpoint configurations to <c>~/.mux/endpoints.json</c>.
        /// </summary>
        /// <param name="endpoints">The endpoint configurations to persist.</param>
        public static void SaveEndpoints(List<EndpointConfig> endpoints)
        {
            if (endpoints == null)
            {
                throw new ArgumentNullException(nameof(endpoints));
            }

            EnsureConfigDirectory();
            List<EndpointConfig> normalizedEndpoints = NormalizeEndpointsForPersistence(endpoints);

            EndpointsFile file = new EndpointsFile
            {
                Endpoints = normalizedEndpoints
            };

            string json = JsonSerializer.Serialize(file, _JsonWriteOptions);
            WriteAllTextAtomic(Path.Combine(GetConfigDirectory(), "endpoints.json"), json);
        }

        /// <summary>
        /// Loads the prompt profiles from <c>~/.mux/prompts.json</c>.
        /// </summary>
        /// <returns>The prompt profiles, or an empty list when the file does not exist.</returns>
        public static List<PromptProfile> LoadPrompts()
        {
            string filePath = Path.Combine(GetConfigDirectory(), "prompts.json");
            if (!File.Exists(filePath))
            {
                return new List<PromptProfile>();
            }

            string json = ReadAllTextShared(filePath);
            PromptsFile? file = JsonSerializer.Deserialize<PromptsFile>(json, _JsonOptions);
            if (file == null || file.Prompts == null)
            {
                return new List<PromptProfile>();
            }

            return file.Prompts;
        }

        /// <summary>
        /// Saves prompt profiles to <c>~/.mux/prompts.json</c>. Exactly one profile is normalized to active
        /// (the first, if none or several are marked).
        /// </summary>
        /// <param name="prompts">The prompt profiles to persist.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="prompts"/> is null.</exception>
        public static void SavePrompts(List<PromptProfile> prompts)
        {
            if (prompts == null)
            {
                throw new ArgumentNullException(nameof(prompts));
            }

            EnsureConfigDirectory();
            NormalizeActiveProfile(prompts);

            PromptsFile file = new PromptsFile { Prompts = prompts };
            string json = JsonSerializer.Serialize(file, _JsonWriteOptions);
            WriteAllTextAtomic(Path.Combine(GetConfigDirectory(), "prompts.json"), json);
        }

        /// <summary>
        /// Returns the active prompt profile from <c>~/.mux/prompts.json</c>, or an all-empty default
        /// profile (which inherits every built-in prompt) when none is configured.
        /// </summary>
        /// <returns>The active <see cref="PromptProfile"/>.</returns>
        public static PromptProfile GetActivePromptProfile()
        {
            List<PromptProfile> prompts = LoadPrompts();
            foreach (PromptProfile profile in prompts)
            {
                if (profile.IsActive)
                {
                    return profile;
                }
            }

            return prompts.Count > 0 ? prompts[0] : new PromptProfile { Name = "Default", IsActive = true };
        }

        private static void NormalizeActiveProfile(List<PromptProfile> prompts)
        {
            bool seenActive = false;
            foreach (PromptProfile profile in prompts)
            {
                if (profile.IsActive && !seenActive)
                {
                    seenActive = true;
                }
                else
                {
                    profile.IsActive = false;
                }
            }

            if (!seenActive && prompts.Count > 0)
            {
                prompts[0].IsActive = true;
            }
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
                MuxSettings defaultSettings = new MuxSettings();
                ApplyEnvironmentOverrides(defaultSettings);
                return defaultSettings;
            }

            string json = File.ReadAllText(filePath);
            MuxSettings? settings = JsonSerializer.Deserialize<MuxSettings>(json, _JsonOptions);
            settings ??= new MuxSettings();

            MuxSettings normalized = NormalizeSettingsForPersistence(settings);
            string normalizedJson = JsonSerializer.Serialize(normalized, _JsonWriteOptions);
            File.WriteAllText(filePath, normalizedJson);

            ApplyEnvironmentOverrides(normalized);
            return normalized;
        }

        /// <summary>
        /// Saves the global mux settings to <c>~/.mux/settings.json</c>.
        /// </summary>
        /// <param name="settings">The mux settings to persist.</param>
        public static void SaveSettings(MuxSettings settings)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            EnsureConfigDirectory();
            MuxSettings normalized = NormalizeSettingsForPersistence(settings);
            string json = JsonSerializer.Serialize(normalized, _JsonWriteOptions);
            File.WriteAllText(Path.Combine(GetConfigDirectory(), "settings.json"), json);
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
        /// Saves MCP server configurations to <c>~/.mux/mcp-servers.json</c>.
        /// </summary>
        /// <param name="servers">The MCP server configurations to persist.</param>
        public static void SaveMcpServers(List<McpServerConfig> servers)
        {
            if (servers == null)
            {
                throw new ArgumentNullException(nameof(servers));
            }

            EnsureConfigDirectory();

            McpServersFile file = new McpServersFile
            {
                Servers = servers
            };

            string json = JsonSerializer.Serialize(file, _JsonWriteOptions);
            File.WriteAllText(Path.Combine(GetConfigDirectory(), "mcp-servers.json"), json);
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

            // The active prompt profile is the primary runtime mechanism: an explicit CLI path still wins,
            // but a customized profile system prompt takes precedence over the legacy settings/file paths.
            // A blank profile field inherits, so an unedited profile leaves the legacy chain untouched.
            string activeSystemPrompt = GetActivePromptProfile().SystemPrompt;
            if (!string.IsNullOrWhiteSpace(activeSystemPrompt))
            {
                return activeSystemPrompt;
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
        /// <param name="cliTemperature">An optional temperature override from the CLI.</param>
        /// <param name="cliMaxTokens">An optional max tokens override from the CLI.</param>
        /// <returns>The fully resolved <see cref="EndpointConfig"/>.</returns>
        public static EndpointConfig ResolveEndpoint(
            List<EndpointConfig> endpoints,
            string? endpointName,
            string? cliModel,
            string? cliBaseUrl,
            string? cliAdapterType,
            double? cliTemperature,
            int? cliMaxTokens)
        {
            EndpointConfig? selected = null;

            if (!string.IsNullOrWhiteSpace(endpointName))
            {
                selected = endpoints.FirstOrDefault(
                    (EndpointConfig e) => string.Equals(e.Name, endpointName, StringComparison.OrdinalIgnoreCase));

                if (selected == null)
                {
                    throw new InvalidOperationException($"No endpoint named '{endpointName}' was found in {Path.Combine(GetConfigDirectory(), "endpoints.json")}.");
                }
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
                    BaseUrl = "http://localhost:11434",
                    Model = "qwen2.5-coder:7b",
                    Headers = new Dictionary<string, string>()
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
                if (AdapterTypeEnumConverter.TryParse(cliAdapterType, out AdapterTypeEnum parsedAdapter))
                {
                    selected.AdapterType = parsedAdapter;
                }
            }

            if (cliTemperature.HasValue)
            {
                selected.Temperature = cliTemperature.Value;
            }

            if (cliMaxTokens.HasValue)
            {
                selected.MaxTokens = cliMaxTokens.Value;
            }

            // Expand environment variables in all header values
            List<string> headerKeys = new List<string>(selected.Headers.Keys);
            foreach (string key in headerKeys)
            {
                selected.Headers[key] = ExpandEnvironmentVariables(selected.Headers[key]);
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

            string expanded = Regex.Replace(value, @"%([A-Za-z_][A-Za-z0-9_]*)%", static (Match match) =>
            {
                string varName = match.Groups[1].Value;
                string? envValue = Environment.GetEnvironmentVariable(varName);
                return envValue ?? match.Value;
            });

            expanded = Regex.Replace(expanded, @"\$\{([A-Za-z_][A-Za-z0-9_]*)\}", static (Match match) =>
            {
                string varName = match.Groups[1].Value;
                string? envValue = Environment.GetEnvironmentVariable(varName);
                return envValue ?? match.Value;
            });

            expanded = Regex.Replace(expanded, @"\$env:([A-Za-z_][A-Za-z0-9_]*)", static (Match match) =>
            {
                string varName = match.Groups[1].Value;
                string? envValue = Environment.GetEnvironmentVariable(varName);
                return envValue ?? match.Value;
            });

            expanded = Regex.Replace(expanded, @"\$(?!env:)([A-Za-z_][A-Za-z0-9_]*)\b", static (Match match) =>
            {
                string varName = match.Groups[1].Value;
                string? envValue = Environment.GetEnvironmentVariable(varName);
                return envValue ?? match.Value;
            }, RegexOptions.IgnoreCase);

            return expanded;
        }

        private static string? NormalizeConfigDirectory(string? configDirectory)
        {
            if (string.IsNullOrWhiteSpace(configDirectory))
            {
                return null;
            }

            string expanded = Environment.ExpandEnvironmentVariables(configDirectory.Trim());
            return Path.GetFullPath(expanded);
        }

        private sealed class ConfigDirectoryOverrideScope : IDisposable
        {
            private readonly string? _PreviousConfigDirectory;
            private bool _Disposed;

            public ConfigDirectoryOverrideScope(string? previousConfigDirectory)
            {
                _PreviousConfigDirectory = previousConfigDirectory;
            }

            public void Dispose()
            {
                if (_Disposed)
                {
                    return;
                }

                _ConfigDirectoryOverride.Value = _PreviousConfigDirectory;
                _Disposed = true;
            }
        }

        /// <summary>
        /// Normalizes a user-provided environment variable reference into the canonical <c>${VAR_NAME}</c> form.
        /// Accepts bare names as well as <c>${VAR}</c>, <c>%VAR%</c>, <c>$VAR</c>, and <c>$env:VAR</c>.
        /// </summary>
        /// <param name="value">The user-provided environment variable reference.</param>
        /// <param name="normalizedReference">The normalized canonical reference when parsing succeeds.</param>
        /// <returns>True when the input could be interpreted as an environment variable reference.</returns>
        public static bool TryNormalizeEnvironmentVariableReference(string value, out string normalizedReference)
        {
            normalizedReference = string.Empty;

            if (!TryExtractEnvironmentVariableName(value, allowBareName: true, out string variableName))
            {
                return false;
            }

            normalizedReference = "${" + variableName + "}";
            return true;
        }

        /// <summary>
        /// Attempts to extract an environment variable name from a stored explicit reference.
        /// Supports <c>${VAR}</c>, <c>%VAR%</c>, and <c>$env:VAR</c>.
        /// </summary>
        /// <param name="value">The stored value to inspect.</param>
        /// <param name="variableName">The extracted variable name when parsing succeeds.</param>
        /// <returns>True when an explicit environment reference was found.</returns>
        public static bool TryGetEnvironmentVariableName(string value, out string variableName)
        {
            return TryExtractEnvironmentVariableName(value, allowBareName: false, out variableName);
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
        /// Wrapper class for deserializing the prompts JSON file.
        /// </summary>
        private class PromptsFile
        {
            /// <summary>
            /// The list of prompt profiles.
            /// </summary>
            [JsonPropertyName("prompts")]
            public List<PromptProfile>? Prompts { get; set; }
        }

        private static bool TryExtractEnvironmentVariableName(string value, bool allowBareName, out string variableName)
        {
            variableName = string.Empty;

            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            string trimmed = value.Trim();

            Match canonicalMatch = Regex.Match(trimmed, @"^\$\{([A-Za-z_][A-Za-z0-9_]*)\}$");
            if (canonicalMatch.Success)
            {
                variableName = canonicalMatch.Groups[1].Value;
                return true;
            }

            Match windowsMatch = Regex.Match(trimmed, @"^%([A-Za-z_][A-Za-z0-9_]*)%$");
            if (windowsMatch.Success)
            {
                variableName = windowsMatch.Groups[1].Value;
                return true;
            }

            Match powerShellMatch = Regex.Match(trimmed, @"^\$env:([A-Za-z_][A-Za-z0-9_]*)$", RegexOptions.IgnoreCase);
            if (powerShellMatch.Success)
            {
                variableName = powerShellMatch.Groups[1].Value;
                return true;
            }

            Match posixMatch = Regex.Match(trimmed, @"^\$([A-Za-z_][A-Za-z0-9_]*)$");
            if (posixMatch.Success)
            {
                variableName = posixMatch.Groups[1].Value;
                return true;
            }

            if (allowBareName && Regex.IsMatch(trimmed, @"^[A-Za-z_][A-Za-z0-9_]*$"))
            {
                variableName = trimmed;
                return true;
            }

            return false;
        }

        private static List<EndpointConfig> NormalizeEndpointsForPersistence(List<EndpointConfig> endpoints)
        {
            List<EndpointConfig> normalized = new List<EndpointConfig>();
            bool defaultAssigned = false;

            foreach (EndpointConfig endpoint in endpoints)
            {
                if (endpoint == null)
                {
                    continue;
                }

                EndpointConfig copy = CloneEndpoint(endpoint);
                copy.Headers ??= new Dictionary<string, string>();
                copy.Quirks ??= Defaults.QuirksForAdapter(copy.AdapterType);

                if (copy.IsDefault)
                {
                    if (!defaultAssigned)
                    {
                        defaultAssigned = true;
                    }
                    else
                    {
                        copy.IsDefault = false;
                    }
                }

                normalized.Add(copy);
            }

            if (normalized.Count > 0 && !defaultAssigned)
            {
                normalized[0].IsDefault = true;
            }

            return normalized;
        }

        private static MuxSettings NormalizeSettingsForPersistence(MuxSettings settings)
        {
            MuxSettings normalized = new MuxSettings
            {
                SystemPromptPath = settings.SystemPromptPath,
                DefaultApprovalPolicy = settings.DefaultApprovalPolicy,
                ToolTimeoutMs = settings.ToolTimeoutMs,
                ProcessTimeoutMs = settings.ProcessTimeoutMs,
                ContextWindowSafetyMarginPercent = settings.ContextWindowSafetyMarginPercent,
                TokenEstimationRatio = settings.TokenEstimationRatio,
                AutoCompactEnabled = settings.AutoCompactEnabled,
                ContextWarningThresholdPercent = settings.ContextWarningThresholdPercent,
                CompactionStrategy = settings.CompactionStrategy,
                CompactionPreserveTurns = settings.CompactionPreserveTurns,
                MaxAgentIterations = settings.MaxAgentIterations,
                MaxConcurrency = settings.MaxConcurrency,
                DefaultEnqueueBehavior = settings.DefaultEnqueueBehavior,
                IgnoreCertErrors = settings.IgnoreCertErrors,
                ShowBoundaryLines = settings.ShowBoundaryLines,
                SkillsEnabled = settings.SkillsEnabled,
                SkillRefreshIntervalSeconds = settings.SkillRefreshIntervalSeconds,
                SkillsDirectory = settings.SkillsDirectory,
                ExternalSearch = NormalizeExternalSearchSettings(settings.ExternalSearch)
            };

            return normalized;
        }

        private static void ApplyEnvironmentOverrides(MuxSettings settings)
        {
            string? ignoreCertErrorsValue = Environment.GetEnvironmentVariable("MUX_IGNORE_CERT_ERRORS");
            if (TryParseBooleanEnvironmentValue(ignoreCertErrorsValue, out bool ignoreCertErrors))
            {
                settings.IgnoreCertErrors = ignoreCertErrors;
            }
        }

        private static bool TryParseBooleanEnvironmentValue(string? value, out bool result)
        {
            result = false;

            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            switch (value.Trim().ToLowerInvariant())
            {
                case "1":
                case "true":
                case "yes":
                case "y":
                case "on":
                    result = true;
                    return true;
                case "0":
                case "false":
                case "no":
                case "n":
                case "off":
                    result = false;
                    return true;
                default:
                    return false;
            }
        }

        private static ExternalSearchSettings NormalizeExternalSearchSettings(ExternalSearchSettings settings)
        {
            ExternalSearchSettings normalized = new ExternalSearchSettings
            {
                Enabled = settings?.Enabled ?? false,
                AllowFallback = settings?.AllowFallback ?? true
            };

            List<ExternalSearchProviderConfig> providers = new List<ExternalSearchProviderConfig>();
            bool defaultAssigned = false;

            foreach (ExternalSearchProviderConfig provider in settings?.Providers ?? new List<ExternalSearchProviderConfig>())
            {
                if (provider == null)
                {
                    continue;
                }

                ExternalSearchProviderConfig copy = new ExternalSearchProviderConfig
                {
                    Name = provider.Name,
                    ProviderType = provider.ProviderType,
                    Endpoint = provider.Endpoint,
                    ApiKey = provider.ApiKey,
                    Enabled = provider.Enabled,
                    IsDefault = provider.Enabled && provider.IsDefault && !defaultAssigned,
                    TimeoutMs = provider.TimeoutMs
                };

                if (copy.IsDefault)
                {
                    defaultAssigned = true;
                }

                providers.Add(copy);
            }

            if (!defaultAssigned)
            {
                ExternalSearchProviderConfig? firstEnabled = providers.FirstOrDefault(provider => provider.Enabled);
                if (firstEnabled != null)
                {
                    firstEnabled.IsDefault = true;
                }
            }

            normalized.Providers = providers;
            return normalized;
        }

        private static EndpointConfig CloneEndpoint(EndpointConfig source)
        {
            return new EndpointConfig
            {
                Name = source.Name,
                AdapterType = source.AdapterType,
                BaseUrl = source.BaseUrl,
                Model = source.Model,
                IsDefault = source.IsDefault,
                MaxTokens = source.MaxTokens,
                Temperature = source.Temperature,
                ContextWindow = source.ContextWindow,
                TimeoutMs = source.TimeoutMs,
                Headers = new Dictionary<string, string>(source.Headers ?? new Dictionary<string, string>()),
                AutoApproveTools = source.AutoApproveTools,
                MaxAgentIterations = source.MaxAgentIterations,
                Quirks = source.Quirks == null
                    ? null
                    : new BackendQuirks
                    {
                        AssembleToolCallDeltas = source.Quirks.AssembleToolCallDeltas,
                        SupportsParallelToolCalls = source.Quirks.SupportsParallelToolCalls,
                        SupportsTools = source.Quirks.SupportsTools,
                        EnableMalformedToolCallRecovery = source.Quirks.EnableMalformedToolCallRecovery,
                        RequiresToolResultContentAsString = source.Quirks.RequiresToolResultContentAsString,
                        DefaultFinishReason = source.Quirks.DefaultFinishReason,
                        StripRequestFields = new List<string>(source.Quirks.StripRequestFields)
                    }
            };
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

        /// <summary>
        /// Wrapper class for deserializing the skills index JSON file.
        /// </summary>
        private class SkillIndexFile
        {
            /// <summary>
            /// The per-skill index entries.
            /// </summary>
            [JsonPropertyName("skills")]
            public List<SkillIndexEntry>? Skills { get; set; }
        }

        #endregion
    }
}
