namespace Test.Shared.Suites
{
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Models;
    using Mux.Core.Tools;
    using Touchstone.Core;

    /// <summary>
    /// Touchstone suite for conditional registration of built-in tools in <see cref="BuiltInToolRegistry"/>.
    /// Ported from the <c>BuiltInToolRegistryTests</c> xUnit suite.
    /// </summary>
    public static class BuiltInToolRegistrySuite
    {
        /// <summary>
        /// Builds the built-in-tool-registry suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/> for the registry cases.</returns>
        public static TestSuiteDescriptor Create()
        {
            return new TestSuiteDescriptor(
                "BuiltInToolRegistry",
                "Conditional built-in tool registration",
                new List<TestCaseDescriptor>
                {
                    new TestCaseDescriptor(
                        "BuiltInToolRegistry",
                        "DefaultSettingsDoesNotRegisterWebSearch",
                        "web_search is absent when external search is not configured",
                        (CancellationToken ct) =>
                        {
                            BuiltInToolRegistry registry = new BuiltInToolRegistry();

                            MuxAssert.IsFalse(registry.HasTool("web_search"), "web_search absent");
                            MuxAssert.IsTrue(registry.HasTool("web_retrieve"), "web_retrieve present");
                            MuxAssert.IsFalse(registry.GetToolDefinitions().Any(tool => tool.Name == "web_search"), "no web_search definition");
                            MuxAssert.IsTrue(registry.GetToolDefinitions().Any(tool => tool.Name == "web_retrieve"), "web_retrieve definition present");
                            return Task.CompletedTask;
                        }),

                    new TestCaseDescriptor(
                        "BuiltInToolRegistry",
                        "ConfiguredSearchRegistersWebSearch",
                        "web_search is registered when external search is enabled and configured",
                        (CancellationToken ct) =>
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

                            MuxAssert.IsTrue(registry.HasTool("web_search"), "web_search present");
                            MuxAssert.IsTrue(registry.GetToolDefinitions().Any(tool => tool.Name == "web_search"), "web_search definition present");
                            MuxAssert.AreEqual(baselineRegistry.GetToolDefinitions().Count + 1, registry.GetToolDefinitions().Count, "definition count");
                            return Task.CompletedTask;
                        })
                });
        }
    }
}
