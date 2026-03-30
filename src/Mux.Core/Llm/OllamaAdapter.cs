namespace Mux.Core.Llm
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Net.Http;
    using System.Runtime.CompilerServices;
    using System.Text.Json.Nodes;
    using System.Threading;
    using Mux.Core.Agent;
    using Mux.Core.Models;

    /// <summary>
    /// Adapter for the Ollama local inference server. Strips unsupported fields
    /// and handles Ollama-specific streaming behavior.
    /// </summary>
    public class OllamaAdapter : GenericOpenAiAdapter
    {
        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="OllamaAdapter"/> class.
        /// </summary>
        public OllamaAdapter()
        {
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Builds an <see cref="HttpRequestMessage"/> for the Ollama API, stripping unsupported
        /// fields such as parallel_tool_calls and stream_options.
        /// </summary>
        /// <param name="messages">The conversation messages to send.</param>
        /// <param name="tools">The tool definitions available to the model.</param>
        /// <param name="endpoint">The endpoint configuration for the Ollama backend.</param>
        /// <returns>A fully configured <see cref="HttpRequestMessage"/>.</returns>
        public override HttpRequestMessage BuildRequest(
            List<ConversationMessage> messages,
            List<ToolDefinition> tools,
            EndpointConfig endpoint)
        {
            if (endpoint == null) throw new ArgumentNullException(nameof(endpoint));

            HttpRequestMessage request = base.BuildRequest(messages, tools, endpoint);

            // Strip parallel_tool_calls and stream_options — Ollama does not support them
            if (request.Content != null)
            {
                string bodyJson = request.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                JsonNode? bodyNode = JsonNode.Parse(bodyJson);

                if (bodyNode is JsonObject bodyObject)
                {
                    bodyObject.Remove("parallel_tool_calls");
                    bodyObject.Remove("stream_options");

                    string updatedJson = bodyObject.ToJsonString();
                    request.Content = new StringContent(
                        updatedJson,
                        System.Text.Encoding.UTF8,
                        "application/json");
                }
            }

            return request;
        }

        /// <summary>
        /// Reads streaming events from an Ollama response stream. Ollama may send complete
        /// tool calls in single chunks rather than deltas, so this override handles both modes.
        /// </summary>
        /// <param name="responseStream">The HTTP response body stream.</param>
        /// <param name="cancellationToken">A token to cancel the streaming operation.</param>
        /// <returns>An async sequence of <see cref="AgentEvent"/> instances.</returns>
        public override async IAsyncEnumerable<AgentEvent> ReadStreamingEvents(
            Stream responseStream,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            // Ollama uses the same SSE format but may deliver complete tool calls
            // in a single chunk rather than streaming deltas. The base implementation
            // handles both cases via the accumulator pattern, so delegate to it.
            await foreach (AgentEvent agentEvent in base.ReadStreamingEvents(responseStream, cancellationToken)
                .ConfigureAwait(false))
            {
                yield return agentEvent;
            }
        }

        #endregion
    }
}
