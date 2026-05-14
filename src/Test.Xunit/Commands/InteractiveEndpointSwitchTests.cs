namespace Test.Xunit.Commands
{
    using System.Collections.Generic;
    using global::Xunit;
    using Mux.Cli.Commands;
    using Mux.Core.Enums;
    using Mux.Core.Models;

    /// <summary>
    /// Unit tests for interactive endpoint switching resolution.
    /// </summary>
    public class InteractiveEndpointSwitchTests
    {
        /// <summary>
        /// Verifies that endpoint switching resolves a configured endpoint by name.
        /// </summary>
        [Fact]
        public void TryResolveEndpointSwitchTarget_WithConfiguredEndpoint_ReturnsEndpoint()
        {
            EndpointConfig endpoint = new EndpointConfig
            {
                Name = "gemma3",
                Model = "gemma3:270m"
            };

            bool resolved = InteractiveCommand.TryResolveEndpointSwitchTarget(
                new List<EndpointConfig> { endpoint },
                "GEMMA3",
                out EndpointConfig? found,
                out string errorMessage);

            Assert.True(resolved);
            Assert.Same(endpoint, found);
            Assert.Equal(string.Empty, errorMessage);
        }

        /// <summary>
        /// Verifies that endpoint switching does not reinterpret missing endpoint names as model overrides.
        /// </summary>
        [Fact]
        public void TryResolveEndpointSwitchTarget_WithMissingEndpoint_ReturnsError()
        {
            EndpointConfig current = new EndpointConfig
            {
                Name = "gemma3",
                Model = "gemma3:270m"
            };

            bool resolved = InteractiveCommand.TryResolveEndpointSwitchTarget(
                new List<EndpointConfig> { current },
                "functiongemma:270,m",
                out EndpointConfig? found,
                out string errorMessage);

            Assert.False(resolved);
            Assert.Null(found);
            Assert.Equal("No endpoint named 'functiongemma:270,m' is configured.", errorMessage);
            Assert.Equal("gemma3:270m", current.Model);
        }

        /// <summary>
        /// Verifies that endpoint switching uses the condensed notification sentence.
        /// </summary>
        [Fact]
        public void BuildEndpointSwitchedNotification_ReturnsCondensedSummary()
        {
            EndpointConfig endpoint = new EndpointConfig
            {
                Name = "ollama-local",
                Model = "qwen2.5-coder:7b",
                AdapterType = AdapterTypeEnum.Ollama,
                BaseUrl = "http://localhost:11434/v1"
            };

            string notification = InteractiveCommand.BuildEndpointSwitchedNotification(endpoint);

            Assert.Equal(
                "Endpoint switched to ollama-local, model qwen2.5-coder:7b, Ollama adapter on base URL http://localhost:11434/v1",
                notification);
        }

        /// <summary>
        /// Verifies that tool-capable endpoints include the current built-in tools in the session prompt.
        /// </summary>
        [Fact]
        public void BuildSystemPromptForEndpoint_WithToolsEnabled_IncludesWebRetrieve()
        {
            EndpointConfig endpoint = new EndpointConfig
            {
                Name = "functiongemma",
                Model = "functiongemma:270m"
            };

            string prompt = InteractiveCommand.BuildSystemPromptForEndpoint(endpoint, "C:\\Code\\mux", new MuxSettings(), null);

            Assert.Contains("web_retrieve", prompt);
            Assert.Contains("When the user asks you to retrieve", prompt);
            Assert.Contains("If the user asks web_search to retrieve a URL, use web_retrieve instead.", prompt);
        }

        /// <summary>
        /// Verifies that endpoints with tool calling disabled do not advertise web_retrieve.
        /// </summary>
        [Fact]
        public void BuildSystemPromptForEndpoint_WithToolsDisabled_OmitsWebRetrieve()
        {
            EndpointConfig endpoint = new EndpointConfig
            {
                Name = "plain-model",
                Model = "plain-model",
                Quirks = new BackendQuirks
                {
                    SupportsTools = false
                }
            };

            string prompt = InteractiveCommand.BuildSystemPromptForEndpoint(endpoint, "C:\\Code\\mux", new MuxSettings(), null);

            Assert.DoesNotContain("web_retrieve", prompt);
        }
    }
}
