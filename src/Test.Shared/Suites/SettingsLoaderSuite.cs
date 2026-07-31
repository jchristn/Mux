namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Enums;
    using Mux.Core.Models;
    using Mux.Core.Settings;
    using Touchstone.Core;

    /// <summary>
    /// Touchstone suite for <see cref="SettingsLoader"/> configuration loading, endpoint resolution,
    /// environment-variable expansion, and system-prompt fallback. Ported from the
    /// <c>SettingsLoaderTests</c> xUnit suite. Each case runs against an isolated temporary config
    /// directory with the <c>MUX_CONFIG_DIR</c> / <c>MUX_IGNORE_CERT_ERRORS</c> environment variables
    /// saved and restored; cases run sequentially so the process-wide mutation is safe.
    /// </summary>
    public static class SettingsLoaderSuite
    {
        /// <summary>
        /// Builds the settings-loader suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/> for the settings-loader cases.</returns>
        public static TestSuiteDescriptor Create()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>
            {
                Case("LoadEndpointsValidJsonParsesCorrectly", "A valid endpoints.json is parsed into the correct endpoint objects", (string dir, CancellationToken ct) =>
                {
                    string json = "{\"endpoints\":[{\"name\":\"test-ollama\",\"adapterType\":\"Ollama\",\"baseUrl\":\"http://localhost:11434\",\"model\":\"llama3\",\"isDefault\":true,\"maxTokens\":4096,\"temperature\":0.5,\"maxAgentIterations\":40,\"autoApproveTools\":true},{\"name\":\"test-openai\",\"adapterType\":\"OpenAi\",\"baseUrl\":\"https://api.openai.com/v1\",\"model\":\"gpt-4o\",\"headers\":{\"Authorization\":\"Bearer sk-test\"}}]}";
                    File.WriteAllText(Path.Combine(dir, "endpoints.json"), json);

                    List<EndpointConfig> endpoints = SettingsLoader.LoadEndpoints();
                    MuxAssert.AreEqual(2, endpoints.Count, "count");
                    EndpointConfig first = endpoints[0];
                    MuxAssert.AreEqual("test-ollama", first.Name, "first name");
                    MuxAssert.AreEqual(AdapterTypeEnum.Ollama, first.AdapterType, "first adapter");
                    MuxAssert.AreEqual("http://localhost:11434", first.BaseUrl, "first baseUrl");
                    MuxAssert.AreEqual("llama3", first.Model, "first model");
                    MuxAssert.IsTrue(first.IsDefault, "first isDefault");
                    MuxAssert.AreEqual(4096, first.MaxTokens, "first maxTokens");
                    MuxAssert.AreEqual(0.5, first.Temperature, "first temperature");
                    MuxAssert.AreEqual(40, first.MaxAgentIterations, "first maxAgentIterations");
                    MuxAssert.IsTrue(first.AutoApproveTools, "first autoApprove");
                    EndpointConfig second = endpoints[1];
                    MuxAssert.AreEqual("test-openai", second.Name, "second name");
                    MuxAssert.AreEqual(AdapterTypeEnum.OpenAi, second.AdapterType, "second adapter");
                    MuxAssert.AreEqual("gpt-4o", second.Model, "second model");
                    MuxAssert.AreEqual("Bearer sk-test", second.Headers["Authorization"], "second header");
                    MuxAssert.IsNull(second.MaxAgentIterations, "second maxAgentIterations");
                    MuxAssert.IsFalse(second.AutoApproveTools, "second autoApprove");
                    return Task.CompletedTask;
                }),

                Case("LoadEndpointsMissingFileReturnsEmptyList", "A missing endpoints.json returns an empty list", (string dir, CancellationToken ct) =>
                {
                    List<EndpointConfig> endpoints = SettingsLoader.LoadEndpoints();
                    MuxAssert.IsNotNull(endpoints, "not null");
                    MuxAssert.AreEqual(0, endpoints.Count, "empty");
                    return Task.CompletedTask;
                }),

                Case("SaveEndpointsNoDefaultAssignsFirst", "Saving with no default promotes the first endpoint to default", (string dir, CancellationToken ct) =>
                {
                    SettingsLoader.SaveEndpoints(new List<EndpointConfig>
                    {
                        new EndpointConfig { Name = "alpha", AdapterType = AdapterTypeEnum.Ollama, BaseUrl = "http://alpha", Model = "model-a" },
                        new EndpointConfig { Name = "beta", AdapterType = AdapterTypeEnum.OpenAiCompatible, BaseUrl = "http://beta", Model = "model-b" }
                    });
                    List<EndpointConfig> endpoints = SettingsLoader.LoadEndpoints();
                    MuxAssert.AreEqual(2, endpoints.Count, "count");
                    MuxAssert.IsTrue(endpoints[0].IsDefault, "first default");
                    MuxAssert.IsFalse(endpoints[1].IsDefault, "second not default");
                    return Task.CompletedTask;
                }),

                Case("SaveEndpointsMultipleDefaultsRetainsFirst", "Saving with multiple defaults collapses to a single default", (string dir, CancellationToken ct) =>
                {
                    SettingsLoader.SaveEndpoints(new List<EndpointConfig>
                    {
                        new EndpointConfig { Name = "alpha", AdapterType = AdapterTypeEnum.Ollama, BaseUrl = "http://alpha", Model = "model-a", IsDefault = true },
                        new EndpointConfig { Name = "beta", AdapterType = AdapterTypeEnum.OpenAiCompatible, BaseUrl = "http://beta", Model = "model-b", IsDefault = true }
                    });
                    List<EndpointConfig> endpoints = SettingsLoader.LoadEndpoints();
                    MuxAssert.AreEqual(2, endpoints.Count, "count");
                    MuxAssert.IsTrue(endpoints[0].IsDefault, "first default");
                    MuxAssert.IsFalse(endpoints[1].IsDefault, "second not default");
                    return Task.CompletedTask;
                }),

                Case("SaveEndpointsMaxAgentIterationsRoundTrips", "Endpoint-scoped max iteration overrides round-trip", (string dir, CancellationToken ct) =>
                {
                    SettingsLoader.SaveEndpoints(new List<EndpointConfig>
                    {
                        new EndpointConfig { Name = "alpha", AdapterType = AdapterTypeEnum.OpenAiCompatible, BaseUrl = "http://alpha", Model = "model-a", MaxAgentIterations = 65 },
                        new EndpointConfig { Name = "beta", AdapterType = AdapterTypeEnum.OpenAiCompatible, BaseUrl = "http://beta", Model = "model-b", MaxAgentIterations = null }
                    });
                    List<EndpointConfig> endpoints = SettingsLoader.LoadEndpoints();
                    MuxAssert.AreEqual(2, endpoints.Count, "count");
                    MuxAssert.AreEqual(65, endpoints[0].MaxAgentIterations, "first override");
                    MuxAssert.IsNull(endpoints[1].MaxAgentIterations, "second null");
                    return Task.CompletedTask;
                }),

                Case("SaveMcpServersRoundTripsDefinitions", "Stdio MCP server definitions round-trip", (string dir, CancellationToken ct) =>
                {
                    SettingsLoader.SaveMcpServers(new List<McpServerConfig>
                    {
                        new McpServerConfig { Name = "github", Transport = McpTransportTypeEnum.Stdio, Command = "npx", Args = new List<string> { "-y", "@modelcontextprotocol/server-github" }, Env = new Dictionary<string, string> { ["GITHUB_TOKEN"] = "${GITHUB_TOKEN}" } }
                    });
                    List<McpServerConfig> servers = SettingsLoader.LoadMcpServers();
                    MuxAssert.AreEqual(1, servers.Count, "count");
                    MuxAssert.AreEqual("github", servers[0].Name, "name");
                    MuxAssert.AreEqual(McpTransportTypeEnum.Stdio, servers[0].Transport, "transport");
                    MuxAssert.AreEqual("npx", servers[0].Command, "command");
                    MuxAssert.AreEqual(2, servers[0].Args.Count, "args count");
                    MuxAssert.AreEqual("@modelcontextprotocol/server-github", servers[0].Args[1], "args[1]");
                    MuxAssert.AreEqual("${GITHUB_TOKEN}", servers[0].Env["GITHUB_TOKEN"], "env");
                    return Task.CompletedTask;
                }),

                Case("SaveMcpServersRoundTripsHttpDefinitions", "HTTP MCP server definitions round-trip", (string dir, CancellationToken ct) =>
                {
                    SettingsLoader.SaveMcpServers(new List<McpServerConfig>
                    {
                        new McpServerConfig { Name = "remote-http", Transport = McpTransportTypeEnum.Http, Url = "https://mcp.example.com", McpPath = "/mcp" }
                    });
                    List<McpServerConfig> servers = SettingsLoader.LoadMcpServers();
                    MuxAssert.AreEqual(1, servers.Count, "count");
                    MuxAssert.AreEqual("remote-http", servers[0].Name, "name");
                    MuxAssert.AreEqual(McpTransportTypeEnum.Http, servers[0].Transport, "transport");
                    MuxAssert.AreEqual("https://mcp.example.com", servers[0].Url, "url");
                    MuxAssert.AreEqual("/mcp", servers[0].McpPath, "mcpPath");
                    MuxAssert.AreEqual(string.Empty, servers[0].Command, "command empty");
                    MuxAssert.AreEqual(0, servers[0].Args.Count, "args empty");
                    return Task.CompletedTask;
                }),

                Case("LoadMcpServersMissingTransportDefaultsToStdio", "Older MCP definitions without a transport default to stdio", (string dir, CancellationToken ct) =>
                {
                    string json = "{\"servers\":[{\"name\":\"legacy\",\"command\":\"npx\",\"args\":[\"-y\",\"legacy-server\"],\"env\":{}}]}";
                    File.WriteAllText(Path.Combine(dir, "mcp-servers.json"), json);
                    List<McpServerConfig> servers = SettingsLoader.LoadMcpServers();
                    MuxAssert.AreEqual(1, servers.Count, "count");
                    MuxAssert.AreEqual("legacy", servers[0].Name, "name");
                    MuxAssert.AreEqual(McpTransportTypeEnum.Stdio, servers[0].Transport, "transport");
                    MuxAssert.AreEqual("npx", servers[0].Command, "command");
                    MuxAssert.AreEqual("/mcp", servers[0].McpPath, "mcpPath default");
                    return Task.CompletedTask;
                }),

                Case("LoadSettingsValidJsonParsesCorrectly", "A valid settings.json is parsed correctly", (string dir, CancellationToken ct) =>
                {
                    string json = "{\"systemPromptPath\":\"/tmp/prompt.md\",\"defaultApprovalPolicy\":\"auto\",\"toolTimeoutMs\":60000,\"processTimeoutMs\":240000,\"autoCompactEnabled\":false,\"contextWarningThresholdPercent\":85,\"compactionStrategy\":\"trim\",\"compactionPreserveTurns\":4,\"maxAgentIterations\":50,\"maxConcurrency\":5,\"defaultEnqueueBehavior\":\"queue-after\",\"ignoreCertErrors\":true}";
                    File.WriteAllText(Path.Combine(dir, "settings.json"), json);
                    MuxSettings settings = SettingsLoader.LoadSettings();
                    MuxAssert.AreEqual("/tmp/prompt.md", settings.SystemPromptPath, "systemPromptPath");
                    MuxAssert.AreEqual("auto", settings.DefaultApprovalPolicy, "approvalPolicy");
                    MuxAssert.AreEqual(60000, settings.ToolTimeoutMs, "toolTimeoutMs");
                    MuxAssert.AreEqual(240000, settings.ProcessTimeoutMs, "processTimeoutMs");
                    MuxAssert.IsFalse(settings.AutoCompactEnabled, "autoCompactEnabled");
                    MuxAssert.AreEqual(85, settings.ContextWarningThresholdPercent, "warningThreshold");
                    MuxAssert.AreEqual("trim", settings.CompactionStrategy, "compactionStrategy");
                    MuxAssert.AreEqual(4, settings.CompactionPreserveTurns, "preserveTurns");
                    MuxAssert.AreEqual(50, settings.MaxAgentIterations, "maxAgentIterations");
                    MuxAssert.AreEqual(5, settings.MaxConcurrency, "maxConcurrency");
                    MuxAssert.AreEqual("queue_after", settings.DefaultEnqueueBehavior, "enqueueBehavior");
                    MuxAssert.IsTrue(settings.IgnoreCertErrors, "ignoreCertErrors");
                    return Task.CompletedTask;
                }),

                Case("LoadSettingsMissingFileReturnsDefaults", "A missing settings.json returns default settings", (string dir, CancellationToken ct) =>
                {
                    MuxSettings settings = SettingsLoader.LoadSettings();
                    MuxAssert.IsNotNull(settings, "not null");
                    MuxAssert.IsNull(settings.SystemPromptPath, "systemPromptPath null");
                    MuxAssert.AreEqual("ask", settings.DefaultApprovalPolicy, "approvalPolicy");
                    MuxAssert.AreEqual(30000, settings.ToolTimeoutMs, "toolTimeoutMs");
                    MuxAssert.AreEqual(120000, settings.ProcessTimeoutMs, "processTimeoutMs");
                    MuxAssert.IsTrue(settings.AutoCompactEnabled, "autoCompactEnabled");
                    MuxAssert.AreEqual(80, settings.ContextWarningThresholdPercent, "warningThreshold");
                    MuxAssert.AreEqual("summary", settings.CompactionStrategy, "compactionStrategy");
                    MuxAssert.AreEqual(3, settings.CompactionPreserveTurns, "preserveTurns");
                    MuxAssert.AreEqual(50, settings.MaxAgentIterations, "maxAgentIterations");
                    MuxAssert.AreEqual(3, settings.MaxConcurrency, "maxConcurrency");
                    MuxAssert.AreEqual("ask", settings.DefaultEnqueueBehavior, "enqueueBehavior");
                    MuxAssert.IsFalse(settings.IgnoreCertErrors, "ignoreCertErrors");
                    MuxAssert.IsNotNull(settings.ExternalSearch, "externalSearch");
                    MuxAssert.IsFalse(settings.ExternalSearch.Enabled, "search enabled");
                    MuxAssert.IsTrue(settings.ExternalSearch.AllowFallback, "search fallback");
                    MuxAssert.AreEqual(0, settings.ExternalSearch.Providers.Count, "search providers empty");
                    return Task.CompletedTask;
                }),

                Case("LoadSettingsMissingFileAppliesIgnoreCertErrorsEnvOverride", "The certificate env override applies without a settings file", (string dir, CancellationToken ct) =>
                {
                    Environment.SetEnvironmentVariable("MUX_IGNORE_CERT_ERRORS", "true");
                    MuxSettings settings = SettingsLoader.LoadSettings();
                    MuxAssert.IsTrue(settings.IgnoreCertErrors, "ignoreCertErrors");
                    return Task.CompletedTask;
                }),

                Case("LoadSettingsPartialFileRewritesWithDefaults", "Loading a partial settings file rewrites it with normalized defaults", (string dir, CancellationToken ct) =>
                {
                    string settingsPath = Path.Combine(dir, "settings.json");
                    File.WriteAllText(settingsPath, "{ \"maxAgentIterations\": 80 }");
                    MuxSettings settings = SettingsLoader.LoadSettings();
                    MuxAssert.AreEqual(80, settings.MaxAgentIterations, "maxAgentIterations");
                    MuxAssert.AreEqual("ask", settings.DefaultApprovalPolicy, "approvalPolicy");
                    MuxAssert.AreEqual("summary", settings.CompactionStrategy, "compactionStrategy");
                    MuxAssert.IsNotNull(settings.ExternalSearch, "externalSearch");
                    using JsonDocument document = JsonDocument.Parse(File.ReadAllText(settingsPath));
                    JsonElement root = document.RootElement;
                    MuxAssert.AreEqual(80, root.GetProperty("maxAgentIterations").GetInt32(), "persisted maxAgentIterations");
                    foreach (string field in new[] { "systemPromptPath", "defaultApprovalPolicy", "toolTimeoutMs", "processTimeoutMs", "contextWindowSafetyMarginPercent", "tokenEstimationRatio", "autoCompactEnabled", "contextWarningThresholdPercent", "compactionStrategy", "compactionPreserveTurns", "maxConcurrency", "defaultEnqueueBehavior", "ignoreCertErrors", "externalSearch" })
                    {
                        MuxAssert.IsTrue(root.TryGetProperty(field, out _), "has " + field);
                    }
                    MuxAssert.IsTrue(root.TryGetProperty("taskPlanningEnabled", out _), "has taskPlanningEnabled");
                    MuxAssert.IsTrue(root.TryGetProperty("taskParallelismEnabled", out _), "has taskParallelismEnabled");
                    return Task.CompletedTask;
                }),

                Case("LoadSettingsMissingFileTaskDefaults", "Task-planning settings default to enabled tracking with parallelism off", (string dir, CancellationToken ct) =>
                {
                    MuxSettings settings = SettingsLoader.LoadSettings();
                    MuxAssert.IsTrue(settings.TaskPlanningEnabled, "taskPlanningEnabled default");
                    MuxAssert.IsFalse(settings.TaskParallelismEnabled, "taskParallelismEnabled default");
                    return Task.CompletedTask;
                }),

                Case("SaveSettingsTaskFlagsRoundTrip", "Task-planning flags round-trip through save and load", (string dir, CancellationToken ct) =>
                {
                    SettingsLoader.SaveSettings(new MuxSettings { TaskPlanningEnabled = false, TaskParallelismEnabled = true });
                    MuxSettings loaded = SettingsLoader.LoadSettings();
                    MuxAssert.IsFalse(loaded.TaskPlanningEnabled, "taskPlanningEnabled");
                    MuxAssert.IsTrue(loaded.TaskParallelismEnabled, "taskParallelismEnabled");
                    return Task.CompletedTask;
                }),

                Case("LoadSettingsIgnoreCertErrorsEnvOverrideAppliesWithoutPersisting", "The env override affects the runtime setting without persisting it", (string dir, CancellationToken ct) =>
                {
                    string settingsPath = Path.Combine(dir, "settings.json");
                    File.WriteAllText(settingsPath, "{ \"ignoreCertErrors\": false }");
                    Environment.SetEnvironmentVariable("MUX_IGNORE_CERT_ERRORS", "1");
                    MuxSettings settings = SettingsLoader.LoadSettings();
                    MuxAssert.IsTrue(settings.IgnoreCertErrors, "runtime override");
                    using JsonDocument document = JsonDocument.Parse(File.ReadAllText(settingsPath));
                    MuxAssert.IsFalse(document.RootElement.GetProperty("ignoreCertErrors").GetBoolean(), "not persisted");
                    return Task.CompletedTask;
                }),

                Case("LoadSettingsCompactionFieldsClampedAndNormalized", "Compaction-related settings are clamped and normalized", (string dir, CancellationToken ct) =>
                {
                    string json = "{\"contextWarningThresholdPercent\":10,\"compactionStrategy\":\"unexpected\",\"compactionPreserveTurns\":99}";
                    File.WriteAllText(Path.Combine(dir, "settings.json"), json);
                    MuxSettings settings = SettingsLoader.LoadSettings();
                    MuxAssert.AreEqual(50, settings.ContextWarningThresholdPercent, "warningThreshold clamped");
                    MuxAssert.AreEqual("summary", settings.CompactionStrategy, "strategy normalized");
                    MuxAssert.AreEqual(10, settings.CompactionPreserveTurns, "preserveTurns clamped");
                    return Task.CompletedTask;
                }),

                Case("LoadSettingsJobFieldsClampedAndNormalized", "Job-related settings are clamped and normalized", (string dir, CancellationToken ct) =>
                {
                    string json = "{\"maxConcurrency\":0,\"defaultEnqueueBehavior\":\"add-to-focused\"}";
                    File.WriteAllText(Path.Combine(dir, "settings.json"), json);
                    MuxSettings settings = SettingsLoader.LoadSettings();
                    MuxAssert.AreEqual(1, settings.MaxConcurrency, "maxConcurrency clamped");
                    MuxAssert.AreEqual("add_to_focused", settings.DefaultEnqueueBehavior, "enqueueBehavior normalized");
                    return Task.CompletedTask;
                }),

                Case("SaveSettingsExternalSearchRoundTripsAndNormalizes", "External search settings round-trip and normalize default providers", (string dir, CancellationToken ct) =>
                {
                    MuxSettings settings = new MuxSettings
                    {
                        ExternalSearch = new ExternalSearchSettings
                        {
                            Enabled = true,
                            AllowFallback = false,
                            Providers = new List<ExternalSearchProviderConfig>
                            {
                                new ExternalSearchProviderConfig { Name = "primary-tavily", ProviderType = "tavily", Endpoint = "https://api.tavily.com/search", ApiKey = "${TAVILY_API_KEY}", Enabled = true, IsDefault = true, TimeoutMs = 45000 },
                                new ExternalSearchProviderConfig { Name = "backup-you", ProviderType = "you", Endpoint = "https://ydc-index.io/v1/search", ApiKey = "${YOU_API_KEY}", Enabled = true, IsDefault = true, TimeoutMs = 30000 }
                            }
                        }
                    };
                    SettingsLoader.SaveSettings(settings);
                    MuxSettings loaded = SettingsLoader.LoadSettings();
                    MuxAssert.IsTrue(loaded.ExternalSearch.Enabled, "enabled");
                    MuxAssert.IsFalse(loaded.ExternalSearch.AllowFallback, "fallback");
                    MuxAssert.AreEqual(2, loaded.ExternalSearch.Providers.Count, "providers count");
                    MuxAssert.AreEqual("primary-tavily", loaded.ExternalSearch.Providers[0].Name, "provider 0 name");
                    MuxAssert.IsTrue(loaded.ExternalSearch.Providers[0].IsDefault, "provider 0 default");
                    MuxAssert.IsFalse(loaded.ExternalSearch.Providers[1].IsDefault, "provider 1 not default");
                    MuxAssert.AreEqual("${TAVILY_API_KEY}", loaded.ExternalSearch.Providers[0].ApiKey, "provider 0 key");
                    MuxAssert.AreEqual(45000, loaded.ExternalSearch.Providers[0].TimeoutMs, "provider 0 timeout");
                    return Task.CompletedTask;
                }),

                Case("LoadSettingsMissingExternalSearchUsesDefaults", "Settings missing the externalSearch node still hydrate defaults", (string dir, CancellationToken ct) =>
                {
                    File.WriteAllText(Path.Combine(dir, "settings.json"), "{\"defaultApprovalPolicy\":\"auto\",\"maxAgentIterations\":40}");
                    MuxSettings settings = SettingsLoader.LoadSettings();
                    MuxAssert.AreEqual("auto", settings.DefaultApprovalPolicy, "approvalPolicy");
                    MuxAssert.AreEqual(40, settings.MaxAgentIterations, "maxAgentIterations");
                    MuxAssert.IsNotNull(settings.ExternalSearch, "externalSearch");
                    MuxAssert.IsFalse(settings.ExternalSearch.Enabled, "enabled");
                    MuxAssert.IsTrue(settings.ExternalSearch.AllowFallback, "fallback");
                    MuxAssert.AreEqual(0, settings.ExternalSearch.Providers.Count, "providers empty");
                    return Task.CompletedTask;
                }),

                Case("GetEffectiveMaxAgentIterationsEndpointOverrideWins", "Endpoint-scoped max iterations override the global default when set", (string dir, CancellationToken ct) =>
                {
                    MuxSettings settings = new MuxSettings { MaxAgentIterations = 55 };
                    EndpointConfig inherited = new EndpointConfig { MaxAgentIterations = null };
                    EndpointConfig overridden = new EndpointConfig { MaxAgentIterations = 12 };
                    MuxAssert.AreEqual(55, settings.GetEffectiveMaxAgentIterations(inherited), "inherited");
                    MuxAssert.AreEqual(12, settings.GetEffectiveMaxAgentIterations(overridden), "overridden");
                    return Task.CompletedTask;
                }),

                Case("ResolveEndpointByNameFindsCorrect", "An endpoint can be resolved by its name", (string dir, CancellationToken ct) =>
                {
                    List<EndpointConfig> endpoints = new List<EndpointConfig>
                    {
                        new EndpointConfig { Name = "alpha", Model = "model-a", BaseUrl = "http://a" },
                        new EndpointConfig { Name = "beta", Model = "model-b", BaseUrl = "http://b" }
                    };
                    EndpointConfig resolved = SettingsLoader.ResolveEndpoint(endpoints, "beta", null, null, null, null, null);
                    MuxAssert.AreEqual("beta", resolved.Name, "name");
                    MuxAssert.AreEqual("model-b", resolved.Model, "model");
                    return Task.CompletedTask;
                }),

                Case("ResolveEndpointDefaultEndpointSelected", "The default endpoint is selected when no name is given", (string dir, CancellationToken ct) =>
                {
                    List<EndpointConfig> endpoints = new List<EndpointConfig>
                    {
                        new EndpointConfig { Name = "first", Model = "model-1", BaseUrl = "http://1" },
                        new EndpointConfig { Name = "second", Model = "model-2", BaseUrl = "http://2", IsDefault = true }
                    };
                    EndpointConfig resolved = SettingsLoader.ResolveEndpoint(endpoints, null, null, null, null, null, null);
                    MuxAssert.AreEqual("second", resolved.Name, "name");
                    return Task.CompletedTask;
                }),

                Case("ResolveEndpointCliOverridesApplied", "CLI overrides for model, temperature, and maxTokens are applied", (string dir, CancellationToken ct) =>
                {
                    List<EndpointConfig> endpoints = new List<EndpointConfig>
                    {
                        new EndpointConfig { Name = "base", Model = "original-model", BaseUrl = "http://base", Temperature = 0.5, MaxTokens = 4096, IsDefault = true }
                    };
                    EndpointConfig resolved = SettingsLoader.ResolveEndpoint(endpoints, null, "override-model", null, null, 1.0, 16384);
                    MuxAssert.AreEqual("override-model", resolved.Model, "model");
                    MuxAssert.AreEqual(1.0, resolved.Temperature, "temperature");
                    MuxAssert.AreEqual(16384, resolved.MaxTokens, "maxTokens");
                    return Task.CompletedTask;
                }),

                Case("ResolveEndpointNoEndpointsFallbackCreated", "A fallback endpoint with Ollama defaults is created when none exist", (string dir, CancellationToken ct) =>
                {
                    EndpointConfig resolved = SettingsLoader.ResolveEndpoint(new List<EndpointConfig>(), null, null, null, null, null, null);
                    MuxAssert.AreEqual("ollama-local", resolved.Name, "name");
                    MuxAssert.AreEqual(AdapterTypeEnum.Ollama, resolved.AdapterType, "adapter");
                    MuxAssert.AreEqual("http://localhost:11434", resolved.BaseUrl, "baseUrl");
                    MuxAssert.AreEqual("qwen2.5-coder:7b", resolved.Model, "model");
                    return Task.CompletedTask;
                }),

                Case("ResolveEndpointUnknownNameThrows", "Requesting a missing named endpoint throws", (string dir, CancellationToken ct) =>
                {
                    List<EndpointConfig> endpoints = new List<EndpointConfig> { new EndpointConfig { Name = "alpha", Model = "model-a", BaseUrl = "http://a" } };
                    InvalidOperationException exception = MuxAssert.Throws<InvalidOperationException>(() => SettingsLoader.ResolveEndpoint(endpoints, "missing", null, null, null, null, null), "unknown name");
                    MuxAssert.Contains("No endpoint named 'missing'", exception.Message, "message");
                    return Task.CompletedTask;
                }),

                Case("LoadSystemPromptFallsBackToDefault", "The built-in default prompt is returned when no prompt file exists", (string dir, CancellationToken ct) =>
                {
                    string prompt = SettingsLoader.LoadSystemPrompt(null, new MuxSettings());
                    MuxAssert.AreEqual(Defaults.SystemPrompt, prompt, "default prompt");
                    return Task.CompletedTask;
                }),

                Case("EnsureConfigDirectoryDoesNotOverwriteExistingEndpoints", "EnsureConfigDirectory does not overwrite an existing endpoints file", (string dir, CancellationToken ct) =>
                {
                    string endpointsPath = Path.Combine(dir, "endpoints.json");
                    string existingJson = "{ \"endpoints\": [{ \"name\": \"custom\", \"adapterType\": \"Ollama\", \"baseUrl\": \"http://custom\", \"model\": \"custom-model\" }] }";
                    File.WriteAllText(endpointsPath, existingJson);
                    SettingsLoader.EnsureConfigDirectory();
                    MuxAssert.AreEqual(existingJson, File.ReadAllText(endpointsPath), "unchanged");
                    return Task.CompletedTask;
                }),

                Case("EnsureConfigDirectoryCreatesDefaultSettings", "EnsureConfigDirectory creates a default settings file when missing", (string dir, CancellationToken ct) =>
                {
                    string settingsPath = Path.Combine(dir, "settings.json");
                    SettingsLoader.EnsureConfigDirectory();
                    MuxAssert.IsTrue(File.Exists(settingsPath), "file exists");
                    MuxSettings? settings = JsonSerializer.Deserialize<MuxSettings>(File.ReadAllText(settingsPath));
                    MuxSettings defaults = new MuxSettings();
                    MuxAssert.IsNotNull(settings, "deserialized");
                    MuxAssert.AreEqual(defaults.DefaultApprovalPolicy, settings!.DefaultApprovalPolicy, "approvalPolicy");
                    MuxAssert.AreEqual(defaults.MaxAgentIterations, settings.MaxAgentIterations, "maxAgentIterations");
                    MuxAssert.AreEqual(defaults.CompactionStrategy, settings.CompactionStrategy, "compactionStrategy");
                    MuxAssert.IsNotNull(settings.ExternalSearch, "externalSearch");
                    return Task.CompletedTask;
                }),

                Case("EnsureConfigDirectoryDoesNotOverwriteExistingSettings", "EnsureConfigDirectory does not overwrite an existing settings file", (string dir, CancellationToken ct) =>
                {
                    string settingsPath = Path.Combine(dir, "settings.json");
                    string existingJson = "{ \"maxAgentIterations\": 80, \"defaultApprovalPolicy\": \"auto\" }";
                    File.WriteAllText(settingsPath, existingJson);
                    SettingsLoader.EnsureConfigDirectory();
                    MuxAssert.AreEqual(existingJson, File.ReadAllText(settingsPath), "unchanged");
                    return Task.CompletedTask;
                }),

                // ---- prompts.json ----
                Case("LoadPromptsEmptyWhenNoFile", "LoadPrompts returns an empty list when prompts.json is absent", (string dir, CancellationToken ct) =>
                {
                    MuxAssert.AreEqual(0, SettingsLoader.LoadPrompts().Count, "no prompts");
                    return Task.CompletedTask;
                }),

                Case("EnsureConfigDirectorySeedsActiveDefaultProfile", "EnsureConfigDirectory seeds prompts.json with one active Default profile", (string dir, CancellationToken ct) =>
                {
                    SettingsLoader.EnsureConfigDirectory();
                    MuxAssert.IsTrue(File.Exists(Path.Combine(dir, "prompts.json")), "prompts.json created");
                    List<PromptProfile> prompts = SettingsLoader.LoadPrompts();
                    MuxAssert.AreEqual(1, prompts.Count, "one seeded profile");
                    MuxAssert.AreEqual("Default", prompts[0].Name, "named Default");
                    MuxAssert.IsTrue(prompts[0].IsActive, "active");
                    MuxAssert.AreEqual(string.Empty, prompts[0].SystemPrompt, "system prompt inherits (empty)");
                    return Task.CompletedTask;
                }),

                Case("SaveAndLoadPromptsRoundTrips", "SavePrompts persists profiles that LoadPrompts reads back", (string dir, CancellationToken ct) =>
                {
                    SettingsLoader.SavePrompts(new List<PromptProfile>
                    {
                        new PromptProfile { Name = "Default", IsActive = false, SystemPrompt = "A" },
                        new PromptProfile { Name = "Concise", IsActive = true, SystemPrompt = "B", CompactionPrompt = "C" }
                    });

                    List<PromptProfile> loaded = SettingsLoader.LoadPrompts();
                    MuxAssert.AreEqual(2, loaded.Count, "two profiles");
                    MuxAssert.AreEqual("Concise", loaded[1].Name, "second name");
                    MuxAssert.AreEqual("B", loaded[1].SystemPrompt, "second system prompt");
                    MuxAssert.AreEqual("C", loaded[1].CompactionPrompt, "second compaction prompt");
                    return Task.CompletedTask;
                }),

                Case("SavePromptsNormalizesToSingleActive", "SavePrompts keeps exactly one profile active", (string dir, CancellationToken ct) =>
                {
                    SettingsLoader.SavePrompts(new List<PromptProfile>
                    {
                        new PromptProfile { Name = "One", IsActive = true },
                        new PromptProfile { Name = "Two", IsActive = true }
                    });

                    List<PromptProfile> loaded = SettingsLoader.LoadPrompts();
                    int active = 0;
                    foreach (PromptProfile p in loaded) { if (p.IsActive) active++; }
                    MuxAssert.AreEqual(1, active, "one active");
                    MuxAssert.IsTrue(loaded[0].IsActive, "first is active");
                    return Task.CompletedTask;
                }),

                Case("GetActivePromptProfileReturnsActive", "GetActivePromptProfile returns the active profile", (string dir, CancellationToken ct) =>
                {
                    SettingsLoader.SavePrompts(new List<PromptProfile>
                    {
                        new PromptProfile { Name = "One" },
                        new PromptProfile { Name = "Two", IsActive = true, SystemPrompt = "custom" }
                    });

                    PromptProfile active = SettingsLoader.GetActivePromptProfile();
                    MuxAssert.AreEqual("Two", active.Name, "active name");
                    MuxAssert.AreEqual("custom", active.SystemPrompt, "active system prompt");
                    return Task.CompletedTask;
                }),

                Case("LoadSystemPromptUsesActiveProfile", "A non-empty active-profile system prompt is used", (string dir, CancellationToken ct) =>
                {
                    SettingsLoader.SavePrompts(new List<PromptProfile>
                    {
                        new PromptProfile { Name = "Custom", IsActive = true, SystemPrompt = "You are a specialized assistant." }
                    });

                    MuxAssert.AreEqual("You are a specialized assistant.", SettingsLoader.LoadSystemPrompt(null, new MuxSettings()), "profile prompt");
                    return Task.CompletedTask;
                }),

                Case("LoadSystemPromptEmptyActiveProfileFallsBackToDefault", "An empty active-profile system prompt falls back to the built-in default", (string dir, CancellationToken ct) =>
                {
                    SettingsLoader.SavePrompts(new List<PromptProfile>
                    {
                        new PromptProfile { Name = "Default", IsActive = true }
                    });

                    MuxAssert.AreEqual(Defaults.SystemPrompt, SettingsLoader.LoadSystemPrompt(null, new MuxSettings()), "falls back to default");
                    return Task.CompletedTask;
                }),

                Case("LoadSystemPromptCliPathWinsOverActiveProfile", "An explicit CLI system-prompt path overrides the active profile", (string dir, CancellationToken ct) =>
                {
                    SettingsLoader.SavePrompts(new List<PromptProfile>
                    {
                        new PromptProfile { Name = "Custom", IsActive = true, SystemPrompt = "profile prompt" }
                    });
                    string cliPath = Path.Combine(dir, "cli-prompt.md");
                    File.WriteAllText(cliPath, "cli prompt");

                    MuxAssert.AreEqual("cli prompt", SettingsLoader.LoadSystemPrompt(cliPath, new MuxSettings()), "cli path wins");
                    return Task.CompletedTask;
                })
            };

            AddExpandEnvironmentVariableCases(cases);
            AddNormalizationTheoryCases(cases);

            return new TestSuiteDescriptor("SettingsLoader", "Settings loading, resolution, and env expansion", cases);
        }

        private static void AddExpandEnvironmentVariableCases(List<TestCaseDescriptor> cases)
        {
            cases.Add(EnvCase("ExpandBraceStyle", "${VAR} patterns are replaced with environment values", "MUX_TEST_VAR_", "replaced_value", varName => "prefix_${" + varName + "}_suffix", "prefix_replaced_value_suffix"));
            cases.Add(EnvCase("ExpandWindowsStyle", "Windows-style %VAR% references are expanded", "MUX_TEST_WIN_", "windows_value", varName => "prefix_%" + varName + "%_suffix", "prefix_windows_value_suffix"));
            cases.Add(EnvCase("ExpandPosixStyle", "POSIX-style $VAR references are expanded", "MUX_TEST_POSIX_", "posix_value", varName => "Bearer $" + varName, "Bearer posix_value"));
            cases.Add(EnvCase("ExpandPowerShellStyle", "PowerShell-style $env:VAR references are expanded", "MUX_TEST_PS_", "powershell_value", varName => "Bearer $env:" + varName, "Bearer powershell_value"));

            cases.Add(new TestCaseDescriptor("SettingsLoader", "ExpandMixedStyles", "Multiple env-var syntaxes may appear in the same value", (CancellationToken ct) =>
            {
                string braceVar = "MUX_TEST_BRACE_" + Guid.NewGuid().ToString("N").Substring(0, 8);
                string winVar = "MUX_TEST_MIX_WIN_" + Guid.NewGuid().ToString("N").Substring(0, 8);
                string posixVar = "MUX_TEST_MIX_POSIX_" + Guid.NewGuid().ToString("N").Substring(0, 8);
                string psVar = "MUX_TEST_MIX_PS_" + Guid.NewGuid().ToString("N").Substring(0, 8);
                Environment.SetEnvironmentVariable(braceVar, "brace");
                Environment.SetEnvironmentVariable(winVar, "windows");
                Environment.SetEnvironmentVariable(posixVar, "posix");
                Environment.SetEnvironmentVariable(psVar, "powershell");
                try
                {
                    string input = "${" + braceVar + "}|%" + winVar + "%|$" + posixVar + "|$env:" + psVar;
                    MuxAssert.AreEqual("brace|windows|posix|powershell", SettingsLoader.ExpandEnvironmentVariables(input), "mixed expansion");
                }
                finally
                {
                    Environment.SetEnvironmentVariable(braceVar, null);
                    Environment.SetEnvironmentVariable(winVar, null);
                    Environment.SetEnvironmentVariable(posixVar, null);
                    Environment.SetEnvironmentVariable(psVar, null);
                }
                return Task.CompletedTask;
            }));
        }

        private static void AddNormalizationTheoryCases(List<TestCaseDescriptor> cases)
        {
            KeyValuePair<string, string>[] normalizes = new[]
            {
                new KeyValuePair<string, string>("OPENAI_API_KEY", "${OPENAI_API_KEY}"),
                new KeyValuePair<string, string>("${OPENAI_API_KEY}", "${OPENAI_API_KEY}"),
                new KeyValuePair<string, string>("%OPENAI_API_KEY%", "${OPENAI_API_KEY}"),
                new KeyValuePair<string, string>("$OPENAI_API_KEY", "${OPENAI_API_KEY}"),
                new KeyValuePair<string, string>("$env:OPENAI_API_KEY", "${OPENAI_API_KEY}")
            };
            int i = 0;
            foreach (KeyValuePair<string, string> pair in normalizes)
            {
                string input = pair.Key;
                string expected = pair.Value;
                cases.Add(new TestCaseDescriptor("SettingsLoader", "NormalizeEnvRef_" + i, "TryNormalizeEnvironmentVariableReference normalizes '" + input + "'", (CancellationToken ct) =>
                {
                    bool success = SettingsLoader.TryNormalizeEnvironmentVariableReference(input, out string normalized);
                    MuxAssert.IsTrue(success, "success");
                    MuxAssert.AreEqual(expected, normalized, "normalized");
                    return Task.CompletedTask;
                }));
                i++;
            }

            string[] rejects = new[] { "", "Bearer $OPENAI_API_KEY", "https://example.com/$API_VERSION", "${OPENAI_API_KEY}-suffix" };
            int r = 0;
            foreach (string value in rejects)
            {
                string input = value;
                cases.Add(new TestCaseDescriptor("SettingsLoader", "RejectEnvRef_" + r, "TryNormalizeEnvironmentVariableReference rejects unsupported value #" + r, (CancellationToken ct) =>
                {
                    bool success = SettingsLoader.TryNormalizeEnvironmentVariableReference(input, out string normalized);
                    MuxAssert.IsFalse(success, "not success");
                    MuxAssert.AreEqual(string.Empty, normalized, "empty");
                    return Task.CompletedTask;
                }));
                r++;
            }

            KeyValuePair<string, string>[] names = new[]
            {
                new KeyValuePair<string, string>("${OPENAI_API_KEY}", "OPENAI_API_KEY"),
                new KeyValuePair<string, string>("%OPENAI_API_KEY%", "OPENAI_API_KEY"),
                new KeyValuePair<string, string>("$OPENAI_API_KEY", "OPENAI_API_KEY"),
                new KeyValuePair<string, string>("$env:OPENAI_API_KEY", "OPENAI_API_KEY")
            };
            int n = 0;
            foreach (KeyValuePair<string, string> pair in names)
            {
                string input = pair.Key;
                string expected = pair.Value;
                cases.Add(new TestCaseDescriptor("SettingsLoader", "GetEnvVarName_" + n, "TryGetEnvironmentVariableName extracts from '" + input + "'", (CancellationToken ct) =>
                {
                    bool success = SettingsLoader.TryGetEnvironmentVariableName(input, out string variableName);
                    MuxAssert.IsTrue(success, "success");
                    MuxAssert.AreEqual(expected, variableName, "name");
                    return Task.CompletedTask;
                }));
                n++;
            }
        }

        private static TestCaseDescriptor EnvCase(string caseId, string displayName, string prefix, string value, Func<string, string> buildInput, string expected)
        {
            return new TestCaseDescriptor("SettingsLoader", caseId, displayName, (CancellationToken ct) =>
            {
                string varName = prefix + Guid.NewGuid().ToString("N").Substring(0, 8);
                Environment.SetEnvironmentVariable(varName, value);
                try
                {
                    MuxAssert.AreEqual(expected, SettingsLoader.ExpandEnvironmentVariables(buildInput(varName)), "expansion");
                }
                finally
                {
                    Environment.SetEnvironmentVariable(varName, null);
                }
                return Task.CompletedTask;
            });
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, Func<string, CancellationToken, Task> body)
        {
            return new TestCaseDescriptor("SettingsLoader", caseId, displayName, async (CancellationToken ct) =>
            {
                string tempDir = Path.Combine(Path.GetTempPath(), "mux_test_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(tempDir);
                string? originalConfigDir = Environment.GetEnvironmentVariable("MUX_CONFIG_DIR");
                string? originalIgnore = Environment.GetEnvironmentVariable("MUX_IGNORE_CERT_ERRORS");
                Environment.SetEnvironmentVariable("MUX_CONFIG_DIR", tempDir);
                Environment.SetEnvironmentVariable("MUX_IGNORE_CERT_ERRORS", null);
                try
                {
                    await body(tempDir, ct).ConfigureAwait(false);
                }
                finally
                {
                    Environment.SetEnvironmentVariable("MUX_CONFIG_DIR", originalConfigDir);
                    Environment.SetEnvironmentVariable("MUX_IGNORE_CERT_ERRORS", originalIgnore);
                    try
                    {
                        if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
                    }
                    catch (IOException)
                    {
                    }
                }
            });
        }
    }
}
