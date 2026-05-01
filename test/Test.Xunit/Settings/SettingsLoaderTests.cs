namespace Test.Xunit.Settings
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text.Json;
    using global::Xunit;
    using Mux.Core.Enums;
    using Mux.Core.Models;
    using Mux.Core.Settings;

    /// <summary>
    /// Unit tests for the <see cref="SettingsLoader"/> class.
    /// Tests configuration loading, endpoint resolution, environment variable expansion,
    /// and system prompt fallback behavior.
    /// </summary>
    [Collection("SettingsLoader")]
    public class SettingsLoaderTests : IDisposable
    {
        #region Private-Members

        private readonly string _TempDir;
        private readonly string? _OriginalConfigDir;

        #endregion

        #region Constructors

        /// <summary>
        /// Initializes a new test instance with a temporary configuration directory.
        /// </summary>
        public SettingsLoaderTests()
        {
            _TempDir = Path.Combine(Path.GetTempPath(), "mux_test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_TempDir);
            _OriginalConfigDir = Environment.GetEnvironmentVariable("MUX_CONFIG_DIR");
            Environment.SetEnvironmentVariable("MUX_CONFIG_DIR", _TempDir);
        }

        #endregion

        #region IDisposable

        /// <summary>
        /// Cleans up the temporary directory and restores the original environment variable.
        /// </summary>
        public void Dispose()
        {
            Environment.SetEnvironmentVariable("MUX_CONFIG_DIR", _OriginalConfigDir);

            try
            {
                if (Directory.Exists(_TempDir))
                {
                    Directory.Delete(_TempDir, true);
                }
            }
            catch
            {
                // Best-effort cleanup
            }
        }

        #endregion

        #region LoadEndpoints

        /// <summary>
        /// Verifies that a valid endpoints.json file is parsed into the correct endpoint objects.
        /// </summary>
        [Fact]
        public void LoadEndpoints_ValidJson_ParsesCorrectly()
        {
            string json = @"{
                ""endpoints"": [
                    {
                        ""name"": ""test-ollama"",
                        ""adapterType"": ""Ollama"",
                        ""baseUrl"": ""http://localhost:11434"",
                        ""model"": ""llama3"",
                        ""isDefault"": true,
                        ""maxTokens"": 4096,
                        ""temperature"": 0.5
                    },
                    {
                        ""name"": ""test-openai"",
                        ""adapterType"": ""OpenAi"",
                        ""baseUrl"": ""https://api.openai.com/v1"",
                        ""model"": ""gpt-4o"",
                        ""headers"": { ""Authorization"": ""Bearer sk-test"" }
                    }
                ]
            }";

            File.WriteAllText(Path.Combine(_TempDir, "endpoints.json"), json);

            List<EndpointConfig> endpoints = SettingsLoader.LoadEndpoints();

            Assert.Equal(2, endpoints.Count);

            EndpointConfig first = endpoints[0];
            Assert.Equal("test-ollama", first.Name);
            Assert.Equal(AdapterTypeEnum.Ollama, first.AdapterType);
            Assert.Equal("http://localhost:11434", first.BaseUrl);
            Assert.Equal("llama3", first.Model);
            Assert.True(first.IsDefault);
            Assert.Equal(4096, first.MaxTokens);
            Assert.Equal(0.5, first.Temperature);

            EndpointConfig second = endpoints[1];
            Assert.Equal("test-openai", second.Name);
            Assert.Equal(AdapterTypeEnum.OpenAi, second.AdapterType);
            Assert.Equal("gpt-4o", second.Model);
            Assert.Equal("Bearer sk-test", second.Headers["Authorization"]);
        }

        /// <summary>
        /// Verifies that a missing endpoints.json file returns an empty list.
        /// </summary>
        [Fact]
        public void LoadEndpoints_MissingFile_ReturnsEmptyList()
        {
            List<EndpointConfig> endpoints = SettingsLoader.LoadEndpoints();
            Assert.NotNull(endpoints);
            Assert.Empty(endpoints);
        }

        /// <summary>
        /// Verifies that saving endpoints promotes the first endpoint to default when none are marked.
        /// </summary>
        [Fact]
        public void SaveEndpoints_NoDefault_AssignsFirstEndpointAsDefault()
        {
            SettingsLoader.SaveEndpoints(new List<EndpointConfig>
            {
                new EndpointConfig { Name = "alpha", AdapterType = AdapterTypeEnum.Ollama, BaseUrl = "http://alpha", Model = "model-a" },
                new EndpointConfig { Name = "beta", AdapterType = AdapterTypeEnum.OpenAiCompatible, BaseUrl = "http://beta", Model = "model-b" }
            });

            List<EndpointConfig> endpoints = SettingsLoader.LoadEndpoints();

            Assert.Equal(2, endpoints.Count);
            Assert.True(endpoints[0].IsDefault);
            Assert.False(endpoints[1].IsDefault);
        }

        /// <summary>
        /// Verifies that saving endpoints collapses multiple defaults to a single persisted default.
        /// </summary>
        [Fact]
        public void SaveEndpoints_MultipleDefaults_RetainsOnlyFirstDefault()
        {
            SettingsLoader.SaveEndpoints(new List<EndpointConfig>
            {
                new EndpointConfig { Name = "alpha", AdapterType = AdapterTypeEnum.Ollama, BaseUrl = "http://alpha", Model = "model-a", IsDefault = true },
                new EndpointConfig { Name = "beta", AdapterType = AdapterTypeEnum.OpenAiCompatible, BaseUrl = "http://beta", Model = "model-b", IsDefault = true }
            });

            List<EndpointConfig> endpoints = SettingsLoader.LoadEndpoints();

            Assert.Equal(2, endpoints.Count);
            Assert.True(endpoints[0].IsDefault);
            Assert.False(endpoints[1].IsDefault);
        }

        #endregion

        #region MCP Servers

        /// <summary>
        /// Verifies that MCP server definitions persist and round-trip through mcp-servers.json.
        /// </summary>
        [Fact]
        public void SaveMcpServers_RoundTripsDefinitions()
        {
            SettingsLoader.SaveMcpServers(new List<McpServerConfig>
            {
                new McpServerConfig
                {
                    Name = "github",
                    Command = "npx",
                    Args = new List<string> { "-y", "@modelcontextprotocol/server-github" },
                    Env = new Dictionary<string, string>
                    {
                        ["GITHUB_TOKEN"] = "${GITHUB_TOKEN}"
                    }
                }
            });

            List<McpServerConfig> servers = SettingsLoader.LoadMcpServers();

            Assert.Single(servers);
            Assert.Equal("github", servers[0].Name);
            Assert.Equal("npx", servers[0].Command);
            Assert.Equal(2, servers[0].Args.Count);
            Assert.Equal("@modelcontextprotocol/server-github", servers[0].Args[1]);
            Assert.Equal("${GITHUB_TOKEN}", servers[0].Env["GITHUB_TOKEN"]);
        }

        #endregion

        #region LoadSettings

        /// <summary>
        /// Verifies that a valid settings.json file is parsed correctly.
        /// </summary>
        [Fact]
        public void LoadSettings_ValidJson_ParsesCorrectly()
        {
            string json = @"{
                ""systemPromptPath"": ""/tmp/prompt.md"",
                ""defaultApprovalPolicy"": ""auto"",
                ""toolTimeoutMs"": 60000,
                ""processTimeoutMs"": 240000,
                ""autoCompactEnabled"": false,
                ""contextWarningThresholdPercent"": 85,
                ""compactionStrategy"": ""trim"",
                ""compactionPreserveTurns"": 4,
                ""maxAgentIterations"": 50
            }";

            File.WriteAllText(Path.Combine(_TempDir, "settings.json"), json);

            MuxSettings settings = SettingsLoader.LoadSettings();

            Assert.Equal("/tmp/prompt.md", settings.SystemPromptPath);
            Assert.Equal("auto", settings.DefaultApprovalPolicy);
            Assert.Equal(60000, settings.ToolTimeoutMs);
            Assert.Equal(240000, settings.ProcessTimeoutMs);
            Assert.False(settings.AutoCompactEnabled);
            Assert.Equal(85, settings.ContextWarningThresholdPercent);
            Assert.Equal("trim", settings.CompactionStrategy);
            Assert.Equal(4, settings.CompactionPreserveTurns);
            Assert.Equal(50, settings.MaxAgentIterations);
        }

        /// <summary>
        /// Verifies that a missing settings.json file returns a default MuxSettings instance.
        /// </summary>
        [Fact]
        public void LoadSettings_MissingFile_ReturnsDefaults()
        {
            MuxSettings settings = SettingsLoader.LoadSettings();

            Assert.NotNull(settings);
            Assert.Null(settings.SystemPromptPath);
            Assert.Equal("ask", settings.DefaultApprovalPolicy);
            Assert.Equal(30000, settings.ToolTimeoutMs);
            Assert.Equal(120000, settings.ProcessTimeoutMs);
            Assert.True(settings.AutoCompactEnabled);
            Assert.Equal(80, settings.ContextWarningThresholdPercent);
            Assert.Equal("summary", settings.CompactionStrategy);
            Assert.Equal(3, settings.CompactionPreserveTurns);
            Assert.Equal(25, settings.MaxAgentIterations);
        }

        /// <summary>
        /// Verifies that new compaction-related settings are clamped and normalized.
        /// </summary>
        [Fact]
        public void LoadSettings_CompactionFields_AreClampedAndNormalized()
        {
            string json = @"{
                ""contextWarningThresholdPercent"": 10,
                ""compactionStrategy"": ""unexpected"",
                ""compactionPreserveTurns"": 99
            }";

            File.WriteAllText(Path.Combine(_TempDir, "settings.json"), json);

            MuxSettings settings = SettingsLoader.LoadSettings();

            Assert.Equal(50, settings.ContextWarningThresholdPercent);
            Assert.Equal("summary", settings.CompactionStrategy);
            Assert.Equal(10, settings.CompactionPreserveTurns);
        }

        #endregion

        #region ResolveEndpoint

        /// <summary>
        /// Verifies that an endpoint can be resolved by its name.
        /// </summary>
        [Fact]
        public void ResolveEndpoint_ByName_FindsCorrect()
        {
            List<EndpointConfig> endpoints = new List<EndpointConfig>
            {
                new EndpointConfig { Name = "alpha", Model = "model-a", BaseUrl = "http://a" },
                new EndpointConfig { Name = "beta", Model = "model-b", BaseUrl = "http://b" }
            };

            EndpointConfig resolved = SettingsLoader.ResolveEndpoint(
                endpoints, "beta", null, null, null, null, null);

            Assert.Equal("beta", resolved.Name);
            Assert.Equal("model-b", resolved.Model);
        }

        /// <summary>
        /// Verifies that the endpoint marked as default is selected when no name is given.
        /// </summary>
        [Fact]
        public void ResolveEndpoint_DefaultEndpoint_Selected()
        {
            List<EndpointConfig> endpoints = new List<EndpointConfig>
            {
                new EndpointConfig { Name = "first", Model = "model-1", BaseUrl = "http://1" },
                new EndpointConfig { Name = "second", Model = "model-2", BaseUrl = "http://2", IsDefault = true }
            };

            EndpointConfig resolved = SettingsLoader.ResolveEndpoint(
                endpoints, null, null, null, null, null, null);

            Assert.Equal("second", resolved.Name);
        }

        /// <summary>
        /// Verifies that CLI overrides for model, temperature, and maxTokens are applied.
        /// </summary>
        [Fact]
        public void ResolveEndpoint_CliOverrides_Applied()
        {
            List<EndpointConfig> endpoints = new List<EndpointConfig>
            {
                new EndpointConfig
                {
                    Name = "base",
                    Model = "original-model",
                    BaseUrl = "http://base",
                    Temperature = 0.5,
                    MaxTokens = 4096,
                    IsDefault = true
                }
            };

            EndpointConfig resolved = SettingsLoader.ResolveEndpoint(
                endpoints, null, "override-model", null, null, 1.0, 16384);

            Assert.Equal("override-model", resolved.Model);
            Assert.Equal(1.0, resolved.Temperature);
            Assert.Equal(16384, resolved.MaxTokens);
        }

        /// <summary>
        /// Verifies that when no endpoints exist, a fallback endpoint is created with Ollama defaults.
        /// </summary>
        [Fact]
        public void ResolveEndpoint_NoEndpoints_FallbackCreated()
        {
            List<EndpointConfig> endpoints = new List<EndpointConfig>();

            EndpointConfig resolved = SettingsLoader.ResolveEndpoint(
                endpoints, null, null, null, null, null, null);

            Assert.Equal("ollama-local", resolved.Name);
            Assert.Equal(AdapterTypeEnum.Ollama, resolved.AdapterType);
            Assert.Equal("http://localhost:11434/v1", resolved.BaseUrl);
            Assert.Equal("qwen2.5-coder:7b", resolved.Model);
        }

        #endregion

        #region ExpandEnvironmentVariables

        /// <summary>
        /// Verifies that ${VAR_NAME} patterns are replaced with actual environment variable values.
        /// </summary>
        [Fact]
        public void ExpandEnvironmentVariables_ReplacesVars()
        {
            string varName = "MUX_TEST_VAR_" + Guid.NewGuid().ToString("N").Substring(0, 8);
            Environment.SetEnvironmentVariable(varName, "replaced_value");

            try
            {
                string input = "prefix_${" + varName + "}_suffix";
                string result = SettingsLoader.ExpandEnvironmentVariables(input);

                Assert.Equal("prefix_replaced_value_suffix", result);
            }
            finally
            {
                Environment.SetEnvironmentVariable(varName, null);
            }
        }

        /// <summary>
        /// Verifies that Windows-style environment variable references are expanded.
        /// </summary>
        [Fact]
        public void ExpandEnvironmentVariables_WindowsStyle_ReplacesVars()
        {
            string varName = "MUX_TEST_WIN_" + Guid.NewGuid().ToString("N").Substring(0, 8);
            Environment.SetEnvironmentVariable(varName, "windows_value");

            try
            {
                string result = SettingsLoader.ExpandEnvironmentVariables("prefix_%" + varName + "%_suffix");
                Assert.Equal("prefix_windows_value_suffix", result);
            }
            finally
            {
                Environment.SetEnvironmentVariable(varName, null);
            }
        }

        /// <summary>
        /// Verifies that POSIX-style environment variable references are expanded.
        /// </summary>
        [Fact]
        public void ExpandEnvironmentVariables_PosixStyle_ReplacesVars()
        {
            string varName = "MUX_TEST_POSIX_" + Guid.NewGuid().ToString("N").Substring(0, 8);
            Environment.SetEnvironmentVariable(varName, "posix_value");

            try
            {
                string result = SettingsLoader.ExpandEnvironmentVariables("Bearer $" + varName);
                Assert.Equal("Bearer posix_value", result);
            }
            finally
            {
                Environment.SetEnvironmentVariable(varName, null);
            }
        }

        /// <summary>
        /// Verifies that PowerShell-style environment variable references are expanded.
        /// </summary>
        [Fact]
        public void ExpandEnvironmentVariables_PowerShellStyle_ReplacesVars()
        {
            string varName = "MUX_TEST_PS_" + Guid.NewGuid().ToString("N").Substring(0, 8);
            Environment.SetEnvironmentVariable(varName, "powershell_value");

            try
            {
                string result = SettingsLoader.ExpandEnvironmentVariables("Bearer $env:" + varName);
                Assert.Equal("Bearer powershell_value", result);
            }
            finally
            {
                Environment.SetEnvironmentVariable(varName, null);
            }
        }

        /// <summary>
        /// Verifies that multiple supported environment-variable syntaxes may appear in the same value.
        /// </summary>
        [Fact]
        public void ExpandEnvironmentVariables_MixedStyles_ReplacesVars()
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
                string result = SettingsLoader.ExpandEnvironmentVariables(input);

                Assert.Equal("brace|windows|posix|powershell", result);
            }
            finally
            {
                Environment.SetEnvironmentVariable(braceVar, null);
                Environment.SetEnvironmentVariable(winVar, null);
                Environment.SetEnvironmentVariable(posixVar, null);
                Environment.SetEnvironmentVariable(psVar, null);
            }
        }

        /// <summary>
        /// Verifies that user-provided environment references are normalized to the canonical form.
        /// </summary>
        [Theory]
        [InlineData("OPENAI_API_KEY", "${OPENAI_API_KEY}")]
        [InlineData("${OPENAI_API_KEY}", "${OPENAI_API_KEY}")]
        [InlineData("%OPENAI_API_KEY%", "${OPENAI_API_KEY}")]
        [InlineData("$OPENAI_API_KEY", "${OPENAI_API_KEY}")]
        [InlineData("$env:OPENAI_API_KEY", "${OPENAI_API_KEY}")]
        public void TryNormalizeEnvironmentVariableReference_NormalizesSupportedForms(string input, string expected)
        {
            bool success = SettingsLoader.TryNormalizeEnvironmentVariableReference(input, out string normalized);

            Assert.True(success);
            Assert.Equal(expected, normalized);
        }

        /// <summary>
        /// Verifies that composite or unsupported values are rejected as environment-variable references.
        /// </summary>
        [Theory]
        [InlineData("")]
        [InlineData("Bearer $OPENAI_API_KEY")]
        [InlineData("https://example.com/$API_VERSION")]
        [InlineData("${OPENAI_API_KEY}-suffix")]
        public void TryNormalizeEnvironmentVariableReference_RejectsUnsupportedValues(string input)
        {
            bool success = SettingsLoader.TryNormalizeEnvironmentVariableReference(input, out string normalized);

            Assert.False(success);
            Assert.Equal(string.Empty, normalized);
        }

        /// <summary>
        /// Verifies that explicit stored environment references expose their variable names for diagnostics.
        /// </summary>
        [Theory]
        [InlineData("${OPENAI_API_KEY}", "OPENAI_API_KEY")]
        [InlineData("%OPENAI_API_KEY%", "OPENAI_API_KEY")]
        [InlineData("$OPENAI_API_KEY", "OPENAI_API_KEY")]
        [InlineData("$env:OPENAI_API_KEY", "OPENAI_API_KEY")]
        public void TryGetEnvironmentVariableName_ExtractsSupportedStoredForms(string input, string expected)
        {
            bool success = SettingsLoader.TryGetEnvironmentVariableName(input, out string variableName);

            Assert.True(success);
            Assert.Equal(expected, variableName);
        }

        #endregion

        #region LoadSystemPrompt

        /// <summary>
        /// Verifies that when no prompt file exists, the built-in default prompt is returned.
        /// </summary>
        [Fact]
        public void LoadSystemPrompt_FallsBackToDefault()
        {
            MuxSettings settings = new MuxSettings();

            string prompt = SettingsLoader.LoadSystemPrompt(null, settings);

            Assert.Equal(Defaults.SystemPrompt, prompt);
        }

        /// <summary>
        /// Verifies that requesting a missing named endpoint throws instead of silently falling back.
        /// </summary>
        [Fact]
        public void ResolveEndpoint_UnknownName_Throws()
        {
            List<EndpointConfig> endpoints = new List<EndpointConfig>
            {
                new EndpointConfig { Name = "alpha", Model = "model-a", BaseUrl = "http://a" }
            };

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                SettingsLoader.ResolveEndpoint(endpoints, "missing", null, null, null, null, null));

            Assert.Contains("No endpoint named 'missing'", exception.Message);
        }

        /// <summary>
        /// Verifies that EnsureConfigDirectory creates missing defaults without overwriting an existing endpoints file.
        /// </summary>
        [Fact]
        public void EnsureConfigDirectory_DoesNotOverwriteExistingEndpoints()
        {
            string endpointsPath = Path.Combine(_TempDir, "endpoints.json");
            string existingJson = @"{ ""endpoints"": [{ ""name"": ""custom"", ""adapterType"": ""Ollama"", ""baseUrl"": ""http://custom"", ""model"": ""custom-model"" }] }";
            File.WriteAllText(endpointsPath, existingJson);

            SettingsLoader.EnsureConfigDirectory();

            string finalJson = File.ReadAllText(endpointsPath);
            Assert.Equal(existingJson, finalJson);
        }

        #endregion
    }
}
