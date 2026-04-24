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
