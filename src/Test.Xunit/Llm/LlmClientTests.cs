namespace Test.Xunit.Llm
{
    using System;
    using System.Collections.Generic;
    using System.Net;
    using System.Net.Http;
    using System.Reflection;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using global::Xunit;
    using Mux.Core.Agent;
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

        /// <summary>
        /// Verifies that the shared HTTP client is configured without a global timeout so streaming responses are not aborted.
        /// </summary>
        [Fact]
        public void Constructor_ConfiguresInfiniteHttpClientTimeout()
        {
            EndpointConfig endpoint = CreateEndpointWithTimeout(50);

            using LlmClient client = new LlmClient(endpoint);

            FieldInfo? field = typeof(LlmClient).GetField("_HttpClient", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field);
            HttpClient httpClient = Assert.IsType<HttpClient>(field!.GetValue(client));
            Assert.Equal(Timeout.InfiniteTimeSpan, httpClient.Timeout);
        }

        /// <summary>
        /// Verifies that streaming requests are not cancelled by the endpoint timeout while waiting for the first response.
        /// </summary>
        [Fact]
        public async Task StreamAsync_DoesNotApplyConfiguredTimeoutToStreamingResponses()
        {
            EndpointConfig endpoint = CreateEndpointWithTimeout(50);
            string streamBody = "data: {\"choices\":[{\"delta\":{\"content\":\"hello\"}}]}\n\n" +
                "data: [DONE]\n\n";

            using HttpClient httpClient = new HttpClient(new DelayedSuccessHandler(
                delayMs: 200,
                content: streamBody,
                mediaType: "text/event-stream"));

            using LlmClient client = new LlmClient(endpoint, httpClient, new GenericOpenAiAdapter());
            List<AgentEvent> events = new List<AgentEvent>();

            await foreach (AgentEvent agentEvent in client.StreamAsync(
                CreateMessages(),
                new List<ToolDefinition>(),
                CancellationToken.None))
            {
                events.Add(agentEvent);
            }

            AssistantTextEvent textEvent = Assert.IsType<AssistantTextEvent>(Assert.Single(events));
            Assert.Equal("hello", textEvent.Text);
        }

        /// <summary>
        /// Verifies that SendAsync builds a non-streaming request.
        /// </summary>
        [Fact]
        public async Task SendAsync_UsesNonStreamingRequest()
        {
            EndpointConfig endpoint = CreateEndpointWithTimeout(5000);
            RecordingAdapter adapter = new RecordingAdapter();
            using HttpClient httpClient = new HttpClient(new StaticJsonHandler("{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"OK\"}}]}"));
            using LlmClient client = new LlmClient(endpoint, httpClient, adapter);

            ConversationMessage response = await client.SendAsync(
                CreateMessages(),
                new List<ToolDefinition>(),
                CancellationToken.None);

            Assert.Equal("OK", response.Content);
            Assert.False(adapter.LastBuildRequestStreamFlag);
        }

        /// <summary>
        /// Verifies that StreamAsync builds a streaming request.
        /// </summary>
        [Fact]
        public async Task StreamAsync_UsesStreamingRequest()
        {
            EndpointConfig endpoint = CreateEndpointWithTimeout(5000);
            RecordingAdapter adapter = new RecordingAdapter();
            string streamBody = "data: {\"choices\":[{\"delta\":{\"content\":\"hello\"}}]}\n\n" +
                "data: [DONE]\n\n";
            using HttpClient httpClient = new HttpClient(new DelayedSuccessHandler(
                delayMs: 1,
                content: streamBody,
                mediaType: "text/event-stream"));
            using LlmClient client = new LlmClient(endpoint, httpClient, adapter);
            List<AgentEvent> events = new List<AgentEvent>();

            await foreach (AgentEvent agentEvent in client.StreamAsync(
                CreateMessages(),
                new List<ToolDefinition>(),
                CancellationToken.None))
            {
                events.Add(agentEvent);
            }

            AssistantTextEvent textEvent = Assert.IsType<AssistantTextEvent>(Assert.Single(events));
            Assert.Equal("hello", textEvent.Text);
            Assert.True(adapter.LastBuildRequestStreamFlag);
        }

        /// <summary>
        /// Verifies that user cancellation on a streaming request is propagated instead of being converted into a connection error event.
        /// </summary>
        [Fact]
        public async Task StreamAsync_PropagatesUserCancellation()
        {
            EndpointConfig endpoint = CreateEndpointWithTimeout(5000);
            using HttpClient httpClient = new HttpClient(new BlockingUntilCancelledHandler());
            using LlmClient client = new LlmClient(endpoint, httpClient, new GenericOpenAiAdapter());
            using CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();

            cancellationTokenSource.CancelAfter(50);

            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            {
                await foreach (AgentEvent _ in client.StreamAsync(
                    CreateMessages(),
                    new List<ToolDefinition>(),
                    cancellationTokenSource.Token))
                {
                }
            });
        }

        #endregion

        #region Private-Methods

        private static List<ConversationMessage> CreateMessages()
        {
            return new List<ConversationMessage>
            {
                new ConversationMessage
                {
                    Role = RoleEnum.User,
                    Content = "Hello"
                }
            };
        }

        private static EndpointConfig CreateEndpointWithTimeout(int timeoutMs)
        {
            EndpointConfig endpoint = new EndpointConfig
            {
                Name = "test",
                BaseUrl = "http://localhost:11434/v1",
                Model = "test-model",
                AdapterType = AdapterTypeEnum.OpenAiCompatible
            };

            FieldInfo? field = typeof(EndpointConfig).GetField("_TimeoutMs", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field);
            field!.SetValue(endpoint, timeoutMs);
            return endpoint;
        }

        #endregion

        #region Private-Types

        private sealed class DelayedSuccessHandler : HttpMessageHandler
        {
            private readonly int _DelayMs;
            private readonly string _Content;
            private readonly string _MediaType;

            public DelayedSuccessHandler(int delayMs, string content, string mediaType)
            {
                _DelayMs = delayMs;
                _Content = content;
                _MediaType = mediaType;
            }

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                await Task.Delay(_DelayMs, cancellationToken).ConfigureAwait(false);

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(_Content, Encoding.UTF8, _MediaType)
                };
            }
        }

        private sealed class StaticJsonHandler : HttpMessageHandler
        {
            private readonly string _ResponseJson;

            public StaticJsonHandler(string responseJson)
            {
                _ResponseJson = responseJson;
            }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(_ResponseJson, Encoding.UTF8, "application/json")
                });
            }
        }

        private sealed class BlockingUntilCancelledHandler : HttpMessageHandler
        {
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
                throw new InvalidOperationException("Expected cancellation before completion.");
            }
        }

        private sealed class RecordingAdapter : IBackendAdapter
        {
            public bool LastBuildRequestStreamFlag { get; private set; } = true;

            public HttpRequestMessage BuildRequest(
                List<ConversationMessage> messages,
                List<ToolDefinition> tools,
                EndpointConfig endpoint,
                bool stream = true)
            {
                LastBuildRequestStreamFlag = stream;
                return new HttpRequestMessage(HttpMethod.Post, endpoint.BaseUrl.TrimEnd('/') + "/chat/completions")
                {
                    Content = new StringContent("{}", Encoding.UTF8, "application/json")
                };
            }

            public ConversationMessage NormalizeFinalResponse(System.Text.Json.JsonElement responseBody)
            {
                return new ConversationMessage
                {
                    Role = RoleEnum.Assistant,
                    Content = responseBody.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString()
                };
            }

            public async IAsyncEnumerable<AgentEvent> ReadStreamingEvents(
                System.IO.Stream responseStream,
                EndpointConfig endpoint,
                [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
            {
                using System.IO.StreamReader reader = new System.IO.StreamReader(responseStream, Encoding.UTF8);
                string content = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
                if (content.Contains("hello", StringComparison.Ordinal))
                {
                    yield return new AssistantTextEvent { Text = "hello" };
                }
            }
        }

        #endregion
    }
}
