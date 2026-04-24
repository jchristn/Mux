namespace Mux.Core.Llm
{
    using System.Collections.Generic;
    using System.IO;
    using System.Net.Http;
    using System.Text.Json;
    using System.Threading;
    using Mux.Core.Agent;
    using Mux.Core.Models;

    /// <summary>
    /// Defines the contract for adapting LLM backend APIs into a common request/response model.
    /// </summary>
    public interface IBackendAdapter
    {
        /// <summary>
        /// Builds an <see cref="HttpRequestMessage"/> for the given conversation state and tools.
        /// </summary>
        /// <param name="messages">The conversation messages to send.</param>
        /// <param name="tools">The tool definitions available to the model.</param>
        /// <param name="endpoint">The endpoint configuration for the target backend.</param>
        /// <param name="stream">True to request a streaming response; false for a bounded JSON response.</param>
        /// <returns>A fully configured <see cref="HttpRequestMessage"/>.</returns>
        HttpRequestMessage BuildRequest(
            List<ConversationMessage> messages,
            List<ToolDefinition> tools,
            EndpointConfig endpoint,
            bool stream = true);

        /// <summary>
        /// Reads a server-sent events stream and yields agent events as they arrive.
        /// </summary>
        /// <param name="responseStream">The HTTP response body stream.</param>
        /// <param name="endpoint">The endpoint configuration for the active request.</param>
        /// <param name="cancellationToken">A token to cancel the streaming operation.</param>
        /// <returns>An async sequence of <see cref="AgentEvent"/> instances.</returns>
        IAsyncEnumerable<AgentEvent> ReadStreamingEvents(
            Stream responseStream,
            EndpointConfig endpoint,
            CancellationToken cancellationToken);

        /// <summary>
        /// Normalizes a non-streaming JSON response body into a <see cref="ConversationMessage"/>.
        /// </summary>
        /// <param name="responseBody">The parsed JSON element from the response.</param>
        /// <returns>A normalized <see cref="ConversationMessage"/>.</returns>
        ConversationMessage NormalizeFinalResponse(JsonElement responseBody);
    }
}
