namespace Mux.Core.Llm
{
    using System;
    using System.Collections.Generic;
    using System.Net.Http;
    using Mux.Core.Models;

    /// <summary>
    /// Adapter for the official OpenAI API. Requires an API key and enables parallel tool calls.
    /// </summary>
    public class OpenAiAdapter : GenericOpenAiAdapter
    {
        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="OpenAiAdapter"/> class.
        /// </summary>
        public OpenAiAdapter()
        {
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Builds an <see cref="HttpRequestMessage"/> for the OpenAI API, requiring an Authorization
        /// header and ensuring parallel tool calls are enabled.
        /// </summary>
        /// <param name="messages">The conversation messages to send.</param>
        /// <param name="tools">The tool definitions available to the model.</param>
        /// <param name="endpoint">The endpoint configuration for the OpenAI backend.</param>
        /// <param name="stream">True to request an SSE stream; false for a single JSON response.</param>
        /// <returns>A fully configured <see cref="HttpRequestMessage"/>.</returns>
        /// <exception cref="InvalidOperationException">Thrown when the endpoint has no Authorization header configured.</exception>
        public override HttpRequestMessage BuildRequest(
            List<ConversationMessage> messages,
            List<ToolDefinition> tools,
            EndpointConfig endpoint,
            bool stream = true)
        {
            if (endpoint == null) throw new ArgumentNullException(nameof(endpoint));

            if (!endpoint.Headers.ContainsKey("Authorization"))
            {
                throw new InvalidOperationException(
                    "OpenAI adapter requires an Authorization header. Add an 'Authorization' entry to the endpoint's headers dictionary.");
            }

            return base.BuildRequest(messages, tools, endpoint, stream);
        }

        #endregion

        #region Private-Methods

        /// <inheritdoc />
        protected override void CustomizeRequestBody(
            OpenAiChatRequest requestBody,
            List<ConversationMessage> messages,
            List<ToolDefinition> tools,
            EndpointConfig endpoint,
            bool stream)
        {
            if (tools != null && tools.Count > 0)
            {
                requestBody.ParallelToolCalls = true;
            }
        }

        #endregion
    }
}
