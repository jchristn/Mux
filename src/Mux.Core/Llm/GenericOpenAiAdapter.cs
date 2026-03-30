namespace Mux.Core.Llm
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Net.Http;
    using System.Net.Http.Headers;
    using System.Runtime.CompilerServices;
    using System.Text;
    using System.Text.Json;
    using System.Text.Json.Nodes;
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
        /// <returns>A fully configured <see cref="HttpRequestMessage"/>.</returns>
        public virtual HttpRequestMessage BuildRequest(
            List<ConversationMessage> messages,
            List<ToolDefinition> tools,
            EndpointConfig endpoint)
        {
            if (messages == null) throw new ArgumentNullException(nameof(messages));
            if (tools == null) throw new ArgumentNullException(nameof(tools));
            if (endpoint == null) throw new ArgumentNullException(nameof(endpoint));

            JsonObject body = new JsonObject();
            body["model"] = endpoint.Model;
            body["messages"] = ConvertMessages(messages);
            body["temperature"] = endpoint.Temperature;
            body["max_tokens"] = endpoint.MaxTokens;
            body["stream"] = true;

            BackendQuirks quirks = endpoint.Quirks ?? new BackendQuirks();

            if (tools.Count > 0 && quirks.SupportsTools)
            {
                body["tools"] = ConvertTools(tools);

                if (quirks.SupportsParallelToolCalls)
                {
                    body["parallel_tool_calls"] = true;
                }
            }

            // Strip fields specified in quirks
            foreach (string field in quirks.StripRequestFields)
            {
                body.Remove(field);
            }

            string json = body.ToJsonString(new JsonSerializerOptions
            {
                WriteIndented = false
            });

            string url = endpoint.BaseUrl.TrimEnd('/') + "/chat/completions";

            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");

            string? authToken = endpoint.BearerToken ?? endpoint.ApiKey;
            if (!string.IsNullOrEmpty(authToken))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", authToken);
            }

            return request;
        }

        /// <summary>
        /// Reads a server-sent events stream from an OpenAI-compatible endpoint and yields agent events.
        /// </summary>
        /// <param name="responseStream">The HTTP response body stream.</param>
        /// <param name="cancellationToken">A token to cancel the streaming operation.</param>
        /// <returns>An async sequence of <see cref="AgentEvent"/> instances.</returns>
        public virtual async IAsyncEnumerable<AgentEvent> ReadStreamingEvents(
            Stream responseStream,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            if (responseStream == null) throw new ArgumentNullException(nameof(responseStream));

            // Accumulated tool call deltas keyed by index
            Dictionary<int, ToolCallAccumulator> toolCallAccumulators = new Dictionary<int, ToolCallAccumulator>();

            // Accumulated assistant text for malformed tool call fallback
            StringBuilder assistantTextBuilder = new StringBuilder();
            bool foundToolCalls = false;

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
                        if (!foundToolCalls)
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

                    JsonElement chunk;
                    try
                    {
                        chunk = JsonDocument.Parse(data).RootElement;
                    }
                    catch (JsonException)
                    {
                        continue;
                    }

                    // Extract the delta from choices[0].delta
                    if (!chunk.TryGetProperty("choices", out JsonElement choices))
                        continue;

                    if (choices.GetArrayLength() == 0)
                        continue;

                    JsonElement firstChoice = choices[0];

                    // Check for finish_reason
                    string? finishReason = null;
                    if (firstChoice.TryGetProperty("finish_reason", out JsonElement finishReasonElement)
                        && finishReasonElement.ValueKind == JsonValueKind.String)
                    {
                        finishReason = finishReasonElement.GetString();
                    }

                    if (firstChoice.TryGetProperty("delta", out JsonElement delta))
                    {
                        // Text content delta
                        if (delta.TryGetProperty("content", out JsonElement contentElement)
                            && contentElement.ValueKind == JsonValueKind.String)
                        {
                            string? text = contentElement.GetString();
                            if (!string.IsNullOrEmpty(text))
                            {
                                assistantTextBuilder.Append(text);
                                yield return new AssistantTextEvent { Text = text! };
                            }
                        }

                        // Tool call deltas
                        if (delta.TryGetProperty("tool_calls", out JsonElement toolCallsElement)
                            && toolCallsElement.ValueKind == JsonValueKind.Array)
                        {
                            foreach (JsonElement toolCallDelta in toolCallsElement.EnumerateArray())
                            {
                                int index = 0;
                                if (toolCallDelta.TryGetProperty("index", out JsonElement indexElement))
                                {
                                    index = indexElement.GetInt32();
                                }

                                if (!toolCallAccumulators.ContainsKey(index))
                                {
                                    toolCallAccumulators[index] = new ToolCallAccumulator();
                                }

                                ToolCallAccumulator accumulator = toolCallAccumulators[index];

                                if (toolCallDelta.TryGetProperty("id", out JsonElement idElement)
                                    && idElement.ValueKind == JsonValueKind.String)
                                {
                                    string? id = idElement.GetString();
                                    if (!string.IsNullOrEmpty(id))
                                    {
                                        accumulator.Id = id!;
                                    }
                                }

                                if (toolCallDelta.TryGetProperty("function", out JsonElement functionElement))
                                {
                                    if (functionElement.TryGetProperty("name", out JsonElement nameElement)
                                        && nameElement.ValueKind == JsonValueKind.String)
                                    {
                                        string? name = nameElement.GetString();
                                        if (!string.IsNullOrEmpty(name))
                                        {
                                            accumulator.Name = name!;
                                        }
                                    }

                                    if (functionElement.TryGetProperty("arguments", out JsonElement argsElement)
                                        && argsElement.ValueKind == JsonValueKind.String)
                                    {
                                        string? argChunk = argsElement.GetString();
                                        if (argChunk != null)
                                        {
                                            accumulator.ArgumentsBuilder.Append(argChunk);
                                        }
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
                if (!foundToolCalls)
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
        /// <param name="responseBody">The parsed JSON element from the response.</param>
        /// <returns>A normalized <see cref="ConversationMessage"/>.</returns>
        public virtual ConversationMessage NormalizeFinalResponse(JsonElement responseBody)
        {
            ConversationMessage message = new ConversationMessage();
            message.Role = RoleEnum.Assistant;

            if (!responseBody.TryGetProperty("choices", out JsonElement choices)
                || choices.GetArrayLength() == 0)
            {
                return message;
            }

            JsonElement firstChoice = choices[0];

            if (!firstChoice.TryGetProperty("message", out JsonElement messageElement))
            {
                return message;
            }

            if (messageElement.TryGetProperty("content", out JsonElement contentElement)
                && contentElement.ValueKind == JsonValueKind.String)
            {
                message.Content = contentElement.GetString();
            }

            if (messageElement.TryGetProperty("tool_calls", out JsonElement toolCallsElement)
                && toolCallsElement.ValueKind == JsonValueKind.Array)
            {
                List<ToolCall> toolCalls = new List<ToolCall>();

                foreach (JsonElement tc in toolCallsElement.EnumerateArray())
                {
                    ToolCall toolCall = new ToolCall();

                    if (tc.TryGetProperty("id", out JsonElement idElement)
                        && idElement.ValueKind == JsonValueKind.String)
                    {
                        toolCall.Id = idElement.GetString()!;
                    }

                    if (tc.TryGetProperty("function", out JsonElement funcElement))
                    {
                        if (funcElement.TryGetProperty("name", out JsonElement nameElement)
                            && nameElement.ValueKind == JsonValueKind.String)
                        {
                            toolCall.Name = nameElement.GetString()!;
                        }

                        if (funcElement.TryGetProperty("arguments", out JsonElement argsElement)
                            && argsElement.ValueKind == JsonValueKind.String)
                        {
                            toolCall.Arguments = argsElement.GetString()!;
                        }
                    }

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
        private JsonArray ConvertMessages(List<ConversationMessage> messages)
        {
            JsonArray array = new JsonArray();

            foreach (ConversationMessage msg in messages)
            {
                JsonObject obj = new JsonObject();

                string role = msg.Role switch
                {
                    RoleEnum.System => "system",
                    RoleEnum.User => "user",
                    RoleEnum.Assistant => "assistant",
                    RoleEnum.Tool => "tool",
                    _ => "user"
                };
                obj["role"] = role;

                if (msg.Content != null)
                {
                    obj["content"] = msg.Content;
                }
                else if (msg.Role != RoleEnum.Assistant)
                {
                    // OpenAI requires content field for non-assistant messages
                    obj["content"] = (string?)null;
                }

                if (msg.ToolCalls != null && msg.ToolCalls.Count > 0)
                {
                    JsonArray toolCallsArray = new JsonArray();

                    foreach (ToolCall tc in msg.ToolCalls)
                    {
                        JsonObject toolCallObj = new JsonObject();
                        toolCallObj["id"] = tc.Id;
                        toolCallObj["type"] = "function";

                        JsonObject functionObj = new JsonObject();
                        functionObj["name"] = tc.Name;
                        functionObj["arguments"] = tc.Arguments;

                        toolCallObj["function"] = functionObj;
                        toolCallsArray.Add(toolCallObj);
                    }

                    obj["tool_calls"] = toolCallsArray;
                }

                if (msg.ToolCallId != null)
                {
                    obj["tool_call_id"] = msg.ToolCallId;
                }

                array.Add(obj);
            }

            return array;
        }

        /// <summary>
        /// Converts a list of <see cref="ToolDefinition"/> instances to an OpenAI-format tools JSON array.
        /// </summary>
        private JsonArray ConvertTools(List<ToolDefinition> tools)
        {
            JsonArray array = new JsonArray();

            foreach (ToolDefinition tool in tools)
            {
                JsonObject obj = new JsonObject();
                obj["type"] = "function";

                JsonObject functionObj = new JsonObject();
                functionObj["name"] = tool.Name;
                functionObj["description"] = tool.Description;

                // ParametersSchema is an object; serialize and re-parse to get a JsonNode
                string schemaJson = JsonSerializer.Serialize(tool.ParametersSchema);
                JsonNode? schemaNode = JsonNode.Parse(schemaJson);
                functionObj["parameters"] = schemaNode;

                obj["function"] = functionObj;
                array.Add(obj);
            }

            return array;
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
