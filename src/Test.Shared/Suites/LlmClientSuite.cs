namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Net.Http;
    using System.Reflection;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Agent;
    using Mux.Core.Enums;
    using Mux.Core.Llm;
    using Mux.Core.Models;
    using Test.Shared.Llm;
    using Touchstone.Core;

    /// <summary>
    /// Touchstone suite for <see cref="LlmClient"/> covering adapter resolution, construction, and
    /// streaming/non-streaming request behavior. Ported from the <c>LlmClientTests</c> xUnit suite.
    /// </summary>
    public static class LlmClientSuite
    {
        private static List<ConversationMessage> CreateMessages()
        {
            return new List<ConversationMessage> { new ConversationMessage { Role = RoleEnum.User, Content = "Hello" } };
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
            MuxAssert.IsNotNull(field, "_TimeoutMs field");
            field!.SetValue(endpoint, timeoutMs);
            return endpoint;
        }

        /// <summary>
        /// Builds the LLM-client suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/> for the LLM-client cases.</returns>
        public static TestSuiteDescriptor Create()
        {
            return new TestSuiteDescriptor(
                "LlmClient",
                "LLM client adapter resolution and requests",
                new List<TestCaseDescriptor>
                {
                    new TestCaseDescriptor("LlmClient", "ResolveAdapterOllama", "Ollama adapter type resolves to OllamaAdapter", (CancellationToken ct) =>
                    {
                        MuxAssert.IsType<OllamaAdapter>(LlmClient.ResolveAdapter(AdapterTypeEnum.Ollama), "Ollama adapter");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("LlmClient", "ResolveAdapterOpenAi", "OpenAi adapter type resolves to OpenAiAdapter", (CancellationToken ct) =>
                    {
                        MuxAssert.IsType<OpenAiAdapter>(LlmClient.ResolveAdapter(AdapterTypeEnum.OpenAi), "OpenAi adapter");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("LlmClient", "ResolveAdapterOpenAiCompatible", "OpenAiCompatible adapter type resolves to GenericOpenAiAdapter", (CancellationToken ct) =>
                    {
                        MuxAssert.IsType<GenericOpenAiAdapter>(LlmClient.ResolveAdapter(AdapterTypeEnum.OpenAiCompatible), "Generic adapter");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("LlmClient", "ConstructorCreatesClientFromEndpoint", "The constructor creates a client from an endpoint config", (CancellationToken ct) =>
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
                            MuxAssert.IsNotNull(client, "client");
                            MuxAssert.AreEqual(endpoint, client.Endpoint, "endpoint");
                        }
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("LlmClient", "ConstructorConfiguresInfiniteHttpClientTimeout", "The shared HTTP client uses no global timeout", (CancellationToken ct) =>
                    {
                        EndpointConfig endpoint = CreateEndpointWithTimeout(50);
                        using LlmClient client = new LlmClient(endpoint);

                        FieldInfo? field = typeof(LlmClient).GetField("_HttpClient", BindingFlags.Instance | BindingFlags.NonPublic);
                        MuxAssert.IsNotNull(field, "_HttpClient field");
                        HttpClient httpClient = MuxAssert.IsType<HttpClient>(field!.GetValue(client), "HttpClient");
                        MuxAssert.AreEqual(Timeout.InfiniteTimeSpan, httpClient.Timeout, "infinite timeout");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("LlmClient", "StreamAsyncDoesNotApplyConfiguredTimeout", "Streaming requests are not cancelled by the endpoint timeout", async (CancellationToken ct) =>
                    {
                        EndpointConfig endpoint = CreateEndpointWithTimeout(50);
                        string streamBody = "data: {\"choices\":[{\"delta\":{\"content\":\"hello\"}}]}\n\n" + "data: [DONE]\n\n";
                        using HttpClient httpClient = new HttpClient(new DelayedSuccessHandler(200, streamBody, "text/event-stream"));
                        using LlmClient client = new LlmClient(endpoint, httpClient, new GenericOpenAiAdapter());

                        List<AgentEvent> events = new List<AgentEvent>();
                        await foreach (AgentEvent agentEvent in client.StreamAsync(CreateMessages(), new List<ToolDefinition>(), CancellationToken.None).ConfigureAwait(false))
                        {
                            events.Add(agentEvent);
                        }

                        MuxAssert.AreEqual(1, events.Count, "single event");
                        AssistantTextEvent textEvent = MuxAssert.IsType<AssistantTextEvent>(events[0], "text event");
                        MuxAssert.AreEqual("hello", textEvent.Text, "text");
                    }),

                    new TestCaseDescriptor("LlmClient", "SendAsyncUsesNonStreamingRequest", "SendAsync builds a non-streaming request", async (CancellationToken ct) =>
                    {
                        EndpointConfig endpoint = CreateEndpointWithTimeout(5000);
                        RecordingAdapter adapter = new RecordingAdapter();
                        using HttpClient httpClient = new HttpClient(new StaticJsonHandler("{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"OK\"}}]}"));
                        using LlmClient client = new LlmClient(endpoint, httpClient, adapter);

                        ConversationMessage response = await client.SendAsync(CreateMessages(), new List<ToolDefinition>(), CancellationToken.None).ConfigureAwait(false);

                        MuxAssert.AreEqual("OK", response.Content, "response content");
                        MuxAssert.IsFalse(adapter.LastBuildRequestStreamFlag, "non-streaming flag");
                    }),

                    new TestCaseDescriptor("LlmClient", "StreamAsyncUsesStreamingRequest", "StreamAsync builds a streaming request", async (CancellationToken ct) =>
                    {
                        EndpointConfig endpoint = CreateEndpointWithTimeout(5000);
                        RecordingAdapter adapter = new RecordingAdapter();
                        string streamBody = "data: {\"choices\":[{\"delta\":{\"content\":\"hello\"}}]}\n\n" + "data: [DONE]\n\n";
                        using HttpClient httpClient = new HttpClient(new DelayedSuccessHandler(1, streamBody, "text/event-stream"));
                        using LlmClient client = new LlmClient(endpoint, httpClient, adapter);

                        List<AgentEvent> events = new List<AgentEvent>();
                        await foreach (AgentEvent agentEvent in client.StreamAsync(CreateMessages(), new List<ToolDefinition>(), CancellationToken.None).ConfigureAwait(false))
                        {
                            events.Add(agentEvent);
                        }

                        MuxAssert.AreEqual(1, events.Count, "single event");
                        AssistantTextEvent textEvent = MuxAssert.IsType<AssistantTextEvent>(events[0], "text event");
                        MuxAssert.AreEqual("hello", textEvent.Text, "text");
                        MuxAssert.IsTrue(adapter.LastBuildRequestStreamFlag, "streaming flag");
                    }),

                    new TestCaseDescriptor("LlmClient", "StreamAsyncPropagatesUserCancellation", "User cancellation on a streaming request is propagated", async (CancellationToken ct) =>
                    {
                        EndpointConfig endpoint = CreateEndpointWithTimeout(5000);
                        using HttpClient httpClient = new HttpClient(new BlockingUntilCancelledHandler());
                        using LlmClient client = new LlmClient(endpoint, httpClient, new GenericOpenAiAdapter());
                        using CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
                        cancellationTokenSource.CancelAfter(50);

                        await MuxAssert.ThrowsAsync<OperationCanceledException>(async () =>
                        {
                            await foreach (AgentEvent _ in client.StreamAsync(CreateMessages(), new List<ToolDefinition>(), cancellationTokenSource.Token).ConfigureAwait(false))
                            {
                            }
                        }, "user cancellation").ConfigureAwait(false);
                    })
                });
        }
    }
}
