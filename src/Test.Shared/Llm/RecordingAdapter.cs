namespace Test.Shared.Llm
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Net.Http;
    using System.Runtime.CompilerServices;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Agent;
    using Mux.Core.Enums;
    using Mux.Core.Llm;
    using Mux.Core.Models;

    /// <summary>
    /// Test <see cref="IBackendAdapter"/> that records the streaming flag passed to
    /// <see cref="BuildRequest"/> and returns a minimal streaming/non-streaming response, used to verify
    /// that <see cref="LlmClient"/> selects the correct request mode.
    /// </summary>
    public sealed class RecordingAdapter : IBackendAdapter
    {
        /// <summary>
        /// Gets the value of the <c>stream</c> argument from the most recent <see cref="BuildRequest"/> call.
        /// </summary>
        public bool LastBuildRequestStreamFlag { get; private set; } = true;

        /// <inheritdoc/>
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

        /// <inheritdoc/>
        public ConversationMessage NormalizeFinalResponse(OpenAiChatCompletionResponse responseBody)
        {
            return new ConversationMessage
            {
                Role = RoleEnum.Assistant,
                Content = responseBody.Choices[0].Message?.Content
            };
        }

        /// <inheritdoc/>
        public async IAsyncEnumerable<AgentEvent> ReadStreamingEvents(
            Stream responseStream,
            EndpointConfig endpoint,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            using StreamReader reader = new StreamReader(responseStream, Encoding.UTF8);
            string content = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            if (content.Contains("hello", StringComparison.Ordinal))
            {
                yield return new AssistantTextEvent { Text = "hello" };
            }
        }
    }
}
