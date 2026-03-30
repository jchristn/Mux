namespace Test.Xunit.Llm
{
    using System;
    using global::Xunit;
    using Mux.Core.Enums;
    using Mux.Core.Llm;
    using Mux.Core.Models;

    /// <summary>
    /// Unit tests for the <see cref="LlmClient"/> class.
    /// Tests adapter resolution and client construction.
    /// </summary>
    public class LlmClientTests
    {
        #region ResolveAdapter

        /// <summary>
        /// Verifies that the Ollama adapter type resolves to an OllamaAdapter instance.
        /// </summary>
        [Fact]
        public void ResolveAdapter_Ollama_ReturnsOllamaAdapter()
        {
            IBackendAdapter adapter = LlmClient.ResolveAdapter(AdapterTypeEnum.Ollama);
            Assert.IsType<OllamaAdapter>(adapter);
        }

        /// <summary>
        /// Verifies that the OpenAi adapter type resolves to an OpenAiAdapter instance.
        /// </summary>
        [Fact]
        public void ResolveAdapter_OpenAi_ReturnsOpenAiAdapter()
        {
            IBackendAdapter adapter = LlmClient.ResolveAdapter(AdapterTypeEnum.OpenAi);
            Assert.IsType<OpenAiAdapter>(adapter);
        }

        /// <summary>
        /// Verifies that the OpenAiCompatible adapter type resolves to a GenericOpenAiAdapter instance.
        /// </summary>
        [Fact]
        public void ResolveAdapter_OpenAiCompatible_ReturnsGenericAdapter()
        {
            IBackendAdapter adapter = LlmClient.ResolveAdapter(AdapterTypeEnum.OpenAiCompatible);
            Assert.IsType<GenericOpenAiAdapter>(adapter);
        }

        #endregion

        #region Constructor

        /// <summary>
        /// Verifies that the LlmClient constructor creates a client from an endpoint config.
        /// </summary>
        [Fact]
        public void Constructor_CreatesClientFromEndpoint()
        {
            EndpointConfig endpoint = new EndpointConfig
            {
                Name = "test",
                BaseUrl = "http://localhost:11434",
                Model = "test-model",
                AdapterType = AdapterTypeEnum.Ollama
            };

            using (LlmClient client = new LlmClient(endpoint))
            {
                Assert.NotNull(client);
                Assert.Equal(endpoint, client.Endpoint);
            }
        }

        #endregion
    }
}
