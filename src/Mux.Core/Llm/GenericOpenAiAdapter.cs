namespace Mux.Core.Llm
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Net.Http;
    using System.Runtime.CompilerServices;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using Mux.Core.Agent;
    using Mux.Core.Enums;
    using Mux.Core.Models;

    /// <summary>
    /// A generic adapter for OpenAI-compatible chat completion APIs.
    /// Handles request building, SSE streaming, and response normalization.
    /// </summary>
    public class GenericOpenAiAdapter : IBackendAdapter
    {
        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="GenericOpenAiAdapter"/> class.
        /// </summary>
        public GenericOpenAiAdapter()
        {
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Builds an <see cref="HttpRequestMessage"/> targeting the OpenAI-compatible chat completions endpoint.
        /// </summary>
        /// <param name="messages">The conversation messages to send.</param>
        /// <param name="tools">The tool definitions available to the model.</param>
        /// <param name="endpoint">The endpoint configuration for the target backend.</param>
        /// <param name="stream">True to request an SSE stream; false for a single JSON response.</param>
        /// <returns>A fully configured <see cref="HttpRequestMessage"/>.</returns>
        public virtual HttpRequestMessage BuildRequest(
            List<ConversationMessage> messages,
            List<ToolDefinition> tools,
            EndpointConfig endpoint,
            bool stream = true)
        {
            if (messages == null) throw new ArgumentNullException(nameof(messages));
            if (tools == null) throw new ArgumentNullException(nameof(tools));
            if (endpoint == null) throw new ArgumentNullException(nameof(endpoint));

            BackendQuirks quirks = endpoint.Quirks ?? new BackendQuirks();
            OpenAiChatRequest body = new OpenAiChatRequest
            {
                Model = endpoint.Model,
                Messages = ConvertMessages(messages),
                Temperature = endpoint.Temperature,
                MaxTokens = endpoint.MaxTokens,
                Stream = stream
            };

            if (tools.Count > 0 && quirks.SupportsTools)
            {
                body.Tools = ConvertTools(tools);

                if (quirks.SupportsParallelToolCalls)
                {
                    body.ParallelToolCalls = true;
                }
            }

            foreach (string field in quirks.StripRequestFields)
            {
                StripRequestField(body, field);
            }

            CustomizeRequestBody(body, messages, tools, endpoint, stream);

            string json = JsonSerializer.Serialize(body, new JsonSerializerOptions
            {
                WriteIndented = false
            });

            string url = endpoint.BaseUrl.TrimEnd('/') + "/chat/completions";

            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");

            foreach (KeyValuePair<string, string> header in endpoint.Headers)
            {
                request.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            return request;
        }

        /// <summary>
        /// Reads a server-sent events stream from an OpenAI-compatible endpoint and yields agent events.
        /// </summary>
        /// <param name="responseStream">The HTTP response body stream.</param>
        /// <param name="endpoint">The endpoint configuration for the active request.</param>
        /// <param name="cancellationToken">A token to cancel the streaming operation.</param>
        /// <returns>An async sequence of <see cref="AgentEvent"/> instances.</returns>
        public virtual async IAsyncEnumerable<AgentEvent> ReadStreamingEvents(
            Stream responseStream,
            EndpointConfig endpoint,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            if (responseStream == null) throw new ArgumentNullException(nameof(responseStream));
            if (endpoint == null) throw new ArgumentNullException(nameof(endpoint));

            // Accumulated tool call deltas keyed by index
            Dictionary<int, ToolCallAccumulator> toolCallAccumulators = new Dictionary<int, ToolCallAccumulator>();

            // Accumulated assistant text for malformed tool call fallback
            StringBuilder assistantTextBuilder = new StringBuilder();
            bool foundToolCalls = false;
            bool enableMalformedToolCallRecovery =
                endpoint.Quirks?.EnableMalformedToolCallRecovery ?? true;

            using (StreamReader reader = new StreamReader(responseStream, Encoding.UTF8))
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    string? line;

                    try
                    {
                        line = await reader.ReadLineAsync().ConfigureAwait(false);
                    }
                    catch (IOException)
                    {
                        // Incomplete SSE chunk or connection drop mid-stream
                        break;
                    }

                    if (line == null)
                    {
                        // End of stream
                        break;
                    }

                    if (string.IsNullOrWhiteSpace(line))
                    {
                        continue;
                    }

                    if (!line.StartsWith("data: ", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    string data = line.Substring(6).Trim();

                    if (string.Equals(data, "[DONE]", StringComparison.Ordinal))
                    {
                        // Yield any accumulated tool calls
                        List<AgentEvent> doneToolEvents = EmitAccumulatedToolCalls(toolCallAccumulators);
                        if (doneToolEvents.Count > 0) foundToolCalls = true;
                        foreach (AgentEvent toolEvent in doneToolEvents)
                        {
                            yield return toolEvent;
                        }

                        // Malformed tool call fallback
                        if (enableMalformedToolCallRecovery && !foundToolCalls)
                        {
                            string accumulatedText = assistantTextBuilder.ToString();
                            List<ToolCall>? malformedCalls = MalformedToolCallParser.TryExtractToolCalls(accumulatedText);
                            if (malformedCalls != null)
                            {
                                foreach (ToolCall tc in malformedCalls)
                                {
                                    yield return new ToolCallProposedEvent { ToolCall = tc };
                                }
                            }
                        }

                        yield break;
                    }

                    OpenAiChatCompletionChunk? chunk;
                    try
                    {
                        chunk = JsonSerializer.Deserialize<OpenAiChatCompletionChunk>(data);
                    }
                    catch (JsonException)
                    {
                        continue;
                    }

                    if (chunk == null || chunk.Choices.Count == 0)
                        continue;

                    OpenAiStreamingChoice firstChoice = chunk.Choices[0];

                    string? finishReason = firstChoice.FinishReason;
                    OpenAiStreamingDelta? delta = firstChoice.Delta;

                    if (delta != null)
                    {
                        if (!string.IsNullOrEmpty(delta.Content))
                        {
                            assistantTextBuilder.Append(delta.Content);
                            yield return new AssistantTextEvent { Text = delta.Content };
                        }

                        if (delta.ToolCalls != null)
                        {
                            foreach (OpenAiStreamingToolCall toolCallDelta in delta.ToolCalls)
                            {
                                int index = toolCallDelta.Index ?? 0;

                                if (!toolCallAccumulators.ContainsKey(index))
                                {
                                    toolCallAccumulators[index] = new ToolCallAccumulator();
                                }

                                ToolCallAccumulator accumulator = toolCallAccumulators[index];

                                if (!string.IsNullOrEmpty(toolCallDelta.Id))
                                {
                                    accumulator.Id = toolCallDelta.Id!;
                                }

                                if (toolCallDelta.Function != null)
                                {
                                    if (!string.IsNullOrEmpty(toolCallDelta.Function.Name))
                                    {
                                        accumulator.Name = toolCallDelta.Function.Name;
                                    }

                                    if (toolCallDelta.Function.Arguments != null)
                                    {
                                        accumulator.ArgumentsBuilder.Append(toolCallDelta.Function.Arguments);
                                    }
                                }
                            }
                        }
                    }

                    // If finish_reason is set (e.g. "tool_calls" or "stop"), emit accumulated tool calls
                    if (!string.IsNullOrEmpty(finishReason))
                    {
                        List<AgentEvent> finishToolEvents = EmitAccumulatedToolCalls(toolCallAccumulators);
                        if (finishToolEvents.Count > 0) foundToolCalls = true;
                        foreach (AgentEvent toolEvent in finishToolEvents)
                        {
                            yield return toolEvent;
                        }
                    }
                }

                // If stream ended without [DONE] or finish_reason, still emit any remaining tool calls
                List<AgentEvent> remainingToolEvents = EmitAccumulatedToolCalls(toolCallAccumulators);
                if (remainingToolEvents.Count > 0) foundToolCalls = true;
                foreach (AgentEvent toolEvent in remainingToolEvents)
                {
                    yield return toolEvent;
                }

                // Malformed tool call fallback at end of stream
                if (enableMalformedToolCallRecovery && !foundToolCalls)
                {
                    string accumulatedText = assistantTextBuilder.ToString();
                    List<ToolCall>? malformedCalls = MalformedToolCallParser.TryExtractToolCalls(accumulatedText);
                    if (malformedCalls != null)
                    {
                        foreach (ToolCall tc in malformedCalls)
                        {
                            yield return new ToolCallProposedEvent { ToolCall = tc };
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Normalizes a non-streaming JSON response into a <see cref="ConversationMessage"/>.
        /// </summary>
        /// <param name="responseBody">The deserialized response body.</param>
        /// <returns>A normalized <see cref="ConversationMessage"/>.</returns>
        public virtual ConversationMessage NormalizeFinalResponse(OpenAiChatCompletionResponse responseBody)
        {
            ConversationMessage message = new ConversationMessage();
            message.Role = RoleEnum.Assistant;

            if (responseBody.Choices.Count == 0)
            {
                return message;
            }

            OpenAiChatChoice firstChoice = responseBody.Choices[0];
            OpenAiChatMessage? messageElement = firstChoice.Message;

            if (messageElement == null)
            {
                return message;
            }

            message.Content = messageElement.Content;

            if (messageElement.ToolCalls != null)
            {
                List<ToolCall> toolCalls = new List<ToolCall>();

                foreach (OpenAiToolCall tc in messageElement.ToolCalls)
                {
                    ToolCall toolCall = new ToolCall
                    {
                        Id = tc.Id,
                        Name = tc.Function.Name,
                        Arguments = tc.Function.Arguments
                    };

                    toolCalls.Add(toolCall);
                }

                if (toolCalls.Count > 0)
                {
                    message.ToolCalls = toolCalls;
                }
            }

            return message;
        }

        #endregion

        #region Private-Methods

        /// <summary>
        /// Converts a list of <see cref="ConversationMessage"/> instances to an OpenAI-format JSON array.
        /// </summary>
        protected virtual void CustomizeRequestBody(
            OpenAiChatRequest requestBody,
            List<ConversationMessage> messages,
            List<ToolDefinition> tools,
            EndpointConfig endpoint,
            bool stream)
        {
        }

        private static void StripRequestField(OpenAiChatRequest body, string field)
        {
            if (string.IsNullOrWhiteSpace(field))
            {
                return;
            }

            string normalized = field.Replace("-", "_", StringComparison.OrdinalIgnoreCase);
            switch (normalized)
            {
                case "temperature":
                    body.Temperature = null;
                    break;
                case "max_tokens":
                    body.MaxTokens = null;
                    break;
                case "stream":
                    body.Stream = null;
                    break;
                case "tools":
                    body.Tools = null;
                    break;
                case "parallel_tool_calls":
                    body.ParallelToolCalls = null;
                    break;
            }
        }

        private List<OpenAiChatMessage> ConvertMessages(List<ConversationMessage> messages)
        {
            List<OpenAiChatMessage> converted = new List<OpenAiChatMessage>();

            foreach (ConversationMessage msg in messages)
            {
                string role = msg.Role switch
                {
                    RoleEnum.System => "system",
                    RoleEnum.User => "user",
                    RoleEnum.Assistant => "assistant",
                    RoleEnum.Tool => "tool",
                    _ => "user"
                };

                OpenAiChatMessage convertedMessage = new OpenAiChatMessage
                {
                    Role = role,
                    Content = msg.Content
                };

                if (msg.ToolCalls != null && msg.ToolCalls.Count > 0)
                {
                    List<OpenAiToolCall> toolCalls = new List<OpenAiToolCall>();

                    foreach (ToolCall tc in msg.ToolCalls)
                    {
                        toolCalls.Add(new OpenAiToolCall
                        {
                            Id = tc.Id,
                            Type = "function",
                            Function = new OpenAiFunctionCall
                            {
                                Name = tc.Name,
                                Arguments = tc.Arguments
                            }
                        });
                    }

                    convertedMessage.ToolCalls = toolCalls;
                }

                if (msg.ToolCallId != null)
                {
                    convertedMessage.ToolCallId = msg.ToolCallId;
                }

                converted.Add(convertedMessage);
            }

            return converted;
        }

        /// <summary>
        /// Converts a list of <see cref="ToolDefinition"/> instances to an OpenAI-format tools JSON array.
        /// </summary>
        private List<OpenAiToolDefinition> ConvertTools(List<ToolDefinition> tools)
        {
            List<OpenAiToolDefinition> converted = new List<OpenAiToolDefinition>();

            foreach (ToolDefinition tool in tools)
            {
                converted.Add(new OpenAiToolDefinition
                {
                    Type = "function",
                    Function = new OpenAiFunctionDefinition
                    {
                        Name = tool.Name,
                        Description = tool.Description,
                        Parameters = tool.ParametersSchema
                    }
                });
            }

            return converted;
        }

        /// <summary>
        /// Emits <see cref="ToolCallProposedEvent"/> instances from accumulated tool call deltas and clears the accumulators.
        /// </summary>
        private List<AgentEvent> EmitAccumulatedToolCalls(Dictionary<int, ToolCallAccumulator> accumulators)
        {
            List<AgentEvent> events = new List<AgentEvent>();

            foreach (KeyValuePair<int, ToolCallAccumulator> kvp in accumulators)
            {
                ToolCallAccumulator accumulator = kvp.Value;

                if (string.IsNullOrEmpty(accumulator.Name))
                    continue;

                ToolCall toolCall = new ToolCall
                {
                    Id = accumulator.Id,
                    Name = accumulator.Name,
                    Arguments = accumulator.ArgumentsBuilder.ToString()
                };

                ToolCallProposedEvent proposed = new ToolCallProposedEvent
                {
                    ToolCall = toolCall
                };

                events.Add(proposed);
            }

            accumulators.Clear();

            return events;
        }

        #endregion

        #region Private-Members

        /// <summary>
        /// Internal accumulator for assembling streamed tool call deltas.
        /// </summary>
        private class ToolCallAccumulator
        {
            public string Id = string.Empty;
            public string Name = string.Empty;
            public StringBuilder ArgumentsBuilder = new StringBuilder();
        }

        #endregion
    }
}
