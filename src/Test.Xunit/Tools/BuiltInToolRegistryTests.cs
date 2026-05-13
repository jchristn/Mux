namespace Test.Xunit.Tools
{
    using System.Linq;
    using global::Xunit;
    using Mux.Core.Models;
    using Mux.Core.Tools;

    /// <summary>
    /// Tests conditional registration of built-in tools.
    /// </summary>
    public class BuiltInToolRegistryTests
    {
        /// <summary>
        /// Verifies that web_search is absent when external search is not configured.
        /// </summary>
        [Fact]
        public void BuiltInToolRegistry_DefaultSettings_DoesNotRegisterWebSearch()
        {
            BuiltInToolRegistry registry = new BuiltInToolRegistry();

            Assert.False(registry.HasTool("web_search"));
            Assert.True(registry.HasTool("web_retrieve"));
            Assert.DoesNotContain(registry.GetToolDefinitions(), tool => tool.Name == "web_search");
            Assert.Contains(registry.GetToolDefinitions(), tool => tool.Name == "web_retrieve");
        }

        /// <summary>
        /// Verifies that web_search is registered when external search is enabled and configured.
        /// </summary>
        [Fact]
        public void BuiltInToolRegistry_ConfiguredSearch_RegistersWebSearch()
        {
            BuiltInToolRegistry baselineRegistry = new BuiltInToolRegistry();
            MuxSettings settings = new MuxSettings
            {
                ExternalSearch = new ExternalSearchSettings
                {
                    Enabled = true,
                    AllowFallback = true,
                    Providers =
                    {
                        new ExternalSearchProviderConfig
                        {
                            Name = "tavily-primary",
                            ProviderType = "tavily",
                            Endpoint = "https://api.tavily.com/search",
                            ApiKey = "test-key",
                            Enabled = true,
                            IsDefault = true,
                            TimeoutMs = 60000
                        }
                    }
                }
            };

            BuiltInToolRegistry registry = new BuiltInToolRegistry(settings);

            Assert.True(registry.HasTool("web_search"));
            Assert.Contains(registry.GetToolDefinitions(), tool => tool.Name == "web_search");
            Assert.Equal(baselineRegistry.GetToolDefinitions().Count + 1, registry.GetToolDefinitions().Count);
        }
    }
}
