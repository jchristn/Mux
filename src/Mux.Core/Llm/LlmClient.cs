namespace Mux.Core.Llm
{
    using System;
    using System.Collections.Generic;
    using System.Net;
    using System.Net.Http;
    using System.Runtime.CompilerServices;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Agent;
    using Mux.Core.Enums;
    using Mux.Core.Models;
    using Mux.Core.Utility;
    using PolyPrompt.Clients;
    using SyslogLogging;
    using Pp = PolyPrompt.Models;

    /// <summary>
    /// Drives LLM calls through the PolyPrompt library. Maps mux conversation messages and tool
    /// definitions onto PolyPrompt's provider-normalized streaming tool-chat API and projects the
    /// resulting text deltas and assembled tool calls back onto mux <see cref="AgentEvent"/> values.
    /// A mux-owned <see cref="HttpClient"/> is injected into the PolyPrompt client so transport options
    /// such as TLS certificate bypass and per-endpoint headers are honored.
    /// </summary>
    public class LlmClient : IDisposable
    {
        #region Private-Members

        private const int MaxRetries = 3;

        private static readonly LoggingModule SilentLogging = CreateSilentLogging();

        private readonly HttpClient _HttpClient;
        private readonly CompletionClientBase _Client;
        private readonly EndpointConfig _Endpoint;
        private readonly LlmUsage _CumulativeUsage = new LlmUsage();
        private Action<int, int, string>? _OnRetry = null;
        private LlmUsage? _LastUsage = null;
        private bool _Disposed = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="LlmClient"/> class.
        /// </summary>
        /// <param name="endpoint">The endpoint configuration to use for LLM requests.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="endpoint"/> is null.</exception>
        public LlmClient(EndpointConfig endpoint)
            : this(endpoint, false)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="LlmClient"/> class.
        /// </summary>
        /// <param name="endpoint">The endpoint configuration to use for LLM requests.</param>
        /// <param name="ignoreCertErrors">True to bypass TLS certificate validation.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="endpoint"/> is null.</exception>
        public LlmClient(EndpointConfig endpoint, bool ignoreCertErrors)
        {
            _Endpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));

            // A mux-owned transport carries certificate policy and per-endpoint headers. Streaming
            // responses stay open for minutes, so the transport timeout is disabled and per-request
            // timeouts are enforced by PolyPrompt via TimeoutMs.
            _HttpClient = MuxHttpClientFactory.Create(ignoreCertErrors);
            _HttpClient.Timeout = Timeout.InfiniteTimeSpan;
            ApplyEndpointHeaders();

            _Client = CreateClient(_Endpoint, _HttpClient);
        }

        #endregion

        #region Public-Members

        /// <summary>
        /// The endpoint configuration used by this client.
        /// </summary>
        public EndpointConfig Endpoint
        {
            get => _Endpoint;
        }

        /// <summary>
        /// Optional callback invoked on each retry attempt. Parameters: attempt number, max retries, error message.
        /// </summary>
        public Action<int, int, string>? OnRetry
        {
            get => _OnRetry;
            set => _OnRetry = value;
        }

        /// <summary>
        /// Provider-reported token usage for the most recent streaming call, or null when none was reported.
        /// </summary>
        public LlmUsage? LastUsage
        {
            get => _LastUsage;
        }

        /// <summary>
        /// Provider-reported token usage accumulated across every streaming call made by this client.
        /// </summary>
        public LlmUsage CumulativeUsage
        {
            get => _CumulativeUsage;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Sends a non-streaming chat completion request and returns the normalized response.
        /// </summary>
        /// <param name="messages">The conversation messages to send.</param>
        /// <param name="tools">The tool definitions available to the model.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>The normalized assistant response as a <see cref="ConversationMessage"/>.</returns>
        /// <exception cref="HttpRequestException">Thrown when the backend returns an error status code.</exception>
        public async Task<ConversationMessage> SendAsync(
            List<ConversationMessage> messages,
            List<ToolDefinition> tools,
            CancellationToken cancellationToken)
        {
            if (messages == null) throw new ArgumentNullException(nameof(messages));
            if (tools == null) throw new ArgumentNullException(nameof(tools));

            Pp.ToolChatRequest request = BuildRequest(messages, tools);
            Pp.ToolChatResponse response = await _Client.ToolChatAsync(request, cancellationToken).ConfigureAwait(false);

            if (!response.Success)
            {
                throw new HttpRequestException(
                    $"LLM request to {_Endpoint.BaseUrl} failed with status {response.StatusCode}: {response.Error}",
                    null,
                    response.StatusCode.HasValue ? (HttpStatusCode)response.StatusCode.Value : null);
            }

            return new ConversationMessage
            {
                Role = RoleEnum.Assistant,
                Content = string.IsNullOrEmpty(response.Text) ? null : response.Text,
                ToolCalls = MapToolCallsToMux(response.ToolCalls)
            };
        }

        /// <summary>
        /// Attempts to load (and thereby validate) the configured model by issuing a minimal, near-empty
        /// completion request. For providers that lazily load models — such as Ollama — this warms the model
        /// into memory; for hosted providers it confirms the model name, base URL, and credentials are usable.
        /// The request uses the same transport and adapter as real turns, so a success here means the next
        /// turn will reach the same model. Never throws for backend/transport errors; those are returned as a
        /// failure result.
        /// </summary>
        /// <param name="cancellationToken">A token to cancel the probe.</param>
        /// <returns>A <see cref="ModelLoadResult"/> describing success or the failure details.</returns>
        public async Task<ModelLoadResult> LoadModelAsync(CancellationToken cancellationToken)
        {
            try
            {
                Pp.ToolChatRequest request = new Pp.ToolChatRequest
                {
                    Model = string.IsNullOrWhiteSpace(_Endpoint.Model) ? null : _Endpoint.Model,
                    MaxTokens = 1,
                    ToolChoice = "none"
                };

                // A single minimal user message keeps the request valid across providers that reject an empty
                // messages array while still asking for essentially no generation (max 1 token).
                request.Messages.Add(Pp.ChatMessage.User("."));

                Pp.ToolChatResponse response = await _Client.ToolChatAsync(request, cancellationToken).ConfigureAwait(false);

                if (response.Success)
                {
                    return ModelLoadResult.Ok();
                }

                string status = response.StatusCode.HasValue ? $"status {response.StatusCode.Value}" : "no response";
                string detail = string.IsNullOrWhiteSpace(response.Error) ? status : $"{status}: {response.Error}";
                return ModelLoadResult.Fail(detail);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                return ModelLoadResult.Fail(ex.Message);
            }
        }

        /// <summary>
        /// Creates a short-lived client for <paramref name="endpoint"/>, attempts to load its model, and
        /// disposes the client. A convenience for callers (such as the shell) that only need a one-shot
        /// model-load probe on an endpoint switch.
        /// </summary>
        /// <param name="endpoint">The endpoint whose model should be loaded/validated.</param>
        /// <param name="ignoreCertErrors">True to bypass TLS certificate validation.</param>
        /// <param name="cancellationToken">A token to cancel the probe.</param>
        /// <returns>A <see cref="ModelLoadResult"/> describing success or the failure details.</returns>
        public static async Task<ModelLoadResult> LoadModelAsync(EndpointConfig endpoint, bool ignoreCertErrors, CancellationToken cancellationToken)
        {
            if (endpoint == null)
            {
                return ModelLoadResult.Fail("no endpoint");
            }

            using LlmClient client = new LlmClient(endpoint, ignoreCertErrors);
            return await client.LoadModelAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Sends a streaming chat completion request and yields agent events as they arrive. Assistant
        /// text is yielded as it streams; assembled tool calls are yielded after the stream completes.
        /// </summary>
        /// <param name="messages">The conversation messages to send.</param>
        /// <param name="tools">The tool definitions available to the model.</param>
        /// <param name="cancellationToken">A token to cancel the streaming operation.</param>
        /// <returns>An async sequence of <see cref="AgentEvent"/> instances.</returns>
        public async IAsyncEnumerable<AgentEvent> StreamAsync(
            List<ConversationMessage> messages,
            List<ToolDefinition> tools,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            if (messages == null) throw new ArgumentNullException(nameof(messages));
            if (tools == null) throw new ArgumentNullException(nameof(tools));

            Pp.ToolChatRequest request = BuildRequest(messages, tools);

            Pp.ToolChatStreamingResponse? response = null;
            ErrorEvent? startupError = null;

            for (int attempt = 0; ; attempt++)
            {
                bool retry = false;
                string? failureMessage = null;

                try
                {
                    response = await _Client.ToolChatStreamingAsync(request, cancellationToken).ConfigureAwait(false);

                    // PolyPrompt surfaces transport-level failures (connection refused, DNS, TLS) as an
                    // unsuccessful response with no HTTP status rather than throwing. Treat those like the
                    // exceptions the old client retried on.
                    if (!response.Success && IsNetworkFailure(response))
                    {
                        failureMessage = string.IsNullOrEmpty(response.Error) ? "connection error" : response.Error;
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    failureMessage = ex.Message;
                }

                if (failureMessage != null)
                {
                    if (attempt < MaxRetries)
                    {
                        _OnRetry?.Invoke(attempt + 1, MaxRetries, failureMessage);
                        retry = true;
                    }
                    else
                    {
                        startupError = new ErrorEvent
                        {
                            Code = "llm_connection_error",
                            Message = $"Failed to connect to {_Endpoint.BaseUrl}: {failureMessage}"
                        };
                    }
                }

                if (!retry)
                {
                    break;
                }
            }

            if (startupError != null)
            {
                yield return startupError;
                yield break;
            }

            // HTTP-level failures surface as an unsuccessful response rather than an exception. When the
            // model rejects tool definitions, retry once without tools to preserve prior behavior.
            if (!response!.Success
                && tools.Count > 0
                && (response.Error ?? string.Empty).Contains("does not support tools", StringComparison.OrdinalIgnoreCase))
            {
                request.Tools.Clear();
                request.ToolChoice = "none";

                ErrorEvent? retryError = null;
                try
                {
                    response = await _Client.ToolChatStreamingAsync(request, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    retryError = new ErrorEvent
                    {
                        Code = "llm_connection_error",
                        Message = $"Retry without tools failed to {_Endpoint.BaseUrl}: {ex.Message}"
                    };
                }

                if (retryError != null)
                {
                    yield return retryError;
                    yield break;
                }
            }

            if (!response!.Success)
            {
                yield return new ErrorEvent
                {
                    Code = "llm_error",
                    Message = $"LLM request to {_Endpoint.BaseUrl} failed with status {response.StatusCode}: {response.Error}"
                };
                yield break;
            }

            IAsyncEnumerator<Pp.ToolChatStreamingChunk> enumerator = response.Chunks.GetAsyncEnumerator(cancellationToken);
            try
            {
                while (true)
                {
                    Pp.ToolChatStreamingChunk? chunk = null;
                    ErrorEvent? streamError = null;
                    bool moved = false;

                    try
                    {
                        moved = await enumerator.MoveNextAsync().ConfigureAwait(false);
                        if (moved)
                        {
                            chunk = enumerator.Current;
                        }
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        streamError = new ErrorEvent
                        {
                            Code = "llm_stream_error",
                            Message = $"Failed to read response stream from {_Endpoint.BaseUrl}: {ex.Message}"
                        };
                    }

                    if (streamError != null)
                    {
                        yield return streamError;
                        yield break;
                    }

                    if (!moved)
                    {
                        break;
                    }

                    // Reasoning ("thinking") is surfaced only when the endpoint opts in, and always as a
                    // separate event — it is never merged into the assistant text or the conversation
                    // history. Emit it before the answer text so the display reads thinking-then-answer.
                    if (chunk != null && _Endpoint.ShowThinking && !string.IsNullOrEmpty(chunk.ReasoningText))
                    {
                        yield return new AssistantThinkingEvent { Text = chunk.ReasoningText };
                    }

                    if (chunk != null && !string.IsNullOrEmpty(chunk.Text))
                    {
                        yield return new AssistantTextEvent { Text = chunk.Text };
                    }
                }
            }
            finally
            {
                await enumerator.DisposeAsync().ConfigureAwait(false);
            }

            RecordUsage(response);

            if (response.ToolCalls != null)
            {
                foreach (Pp.ToolCall call in response.ToolCalls)
                {
                    yield return new ToolCallProposedEvent
                    {
                        ToolCall = new ToolCall
                        {
                            Id = call.Id ?? string.Empty,
                            Name = call.Name,
                            Arguments = call.ArgumentsJson
                        }
                    };
                }
            }
        }

        /// <summary>
        /// Releases the resources used by this <see cref="LlmClient"/> instance.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion

        #region Private-Methods

        /// <summary>
        /// Releases the unmanaged resources and optionally the managed resources.
        /// </summary>
        /// <param name="disposing">True to release both managed and unmanaged resources.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (!_Disposed)
            {
                if (disposing)
                {
                    // The PolyPrompt client does not own the injected transport, so dispose it explicitly.
                    _Client?.Dispose();
                    _HttpClient?.Dispose();
                }

                _Disposed = true;
            }
        }

        private void ApplyEndpointHeaders()
        {
            if (_Endpoint.Headers == null)
            {
                return;
            }

            foreach (KeyValuePair<string, string> header in _Endpoint.Headers)
            {
                if (!string.IsNullOrEmpty(header.Key))
                {
                    _HttpClient.DefaultRequestHeaders.TryAddWithoutValidation(header.Key, header.Value);
                }
            }
        }

        private static bool IsNetworkFailure(Pp.ToolChatStreamingResponse response)
        {
            // A transport-level failure carries no HTTP status; an HTTP error status (4xx/5xx) does.
            return !response.StatusCode.HasValue || response.StatusCode.Value == 0;
        }

        private void RecordUsage(Pp.ToolChatStreamingResponse response)
        {
            Pp.ChatStreamingUsage? usage = response.Usage;
            int input = usage?.PromptTokens ?? 0;
            int output = usage?.CompletionTokens ?? 0;
            int total = usage?.TotalTokens ?? (input + output);

            LlmUsage record = new LlmUsage
            {
                InputTokens = input,
                OutputTokens = output,
                TotalTokens = total
            };

            _LastUsage = record;
            _CumulativeUsage.Add(record);
        }

        private Pp.ToolChatRequest BuildRequest(List<ConversationMessage> messages, List<ToolDefinition> tools)
        {
            Pp.ToolChatRequest request = new Pp.ToolChatRequest
            {
                Model = string.IsNullOrWhiteSpace(_Endpoint.Model) ? null : _Endpoint.Model,
                MaxTokens = _Endpoint.MaxTokens
            };

            Pp.ReasoningEffort? reasoning = MapReasoningEffort(_Endpoint.ReasoningEffort);
            if (reasoning != null)
            {
                request.ReasoningEffort = reasoning;
            }

            foreach (ConversationMessage message in messages)
            {
                request.Messages.Add(MapMessage(message));
            }

            foreach (ToolDefinition tool in tools)
            {
                request.Tools.Add(Pp.ToolDefinition.Function(
                    tool.Name,
                    tool.Description,
                    ConvertSchema(tool.ParametersSchema)));
            }

            request.ToolChoice = tools.Count > 0 ? "auto" : "none";
            return request;
        }

        private static Pp.ReasoningEffort? MapReasoningEffort(ReasoningEffortConfig? config)
        {
            if (config == null || !config.Level.HasValue)
            {
                return null;
            }

            Pp.ReasoningEffort effort = new Pp.ReasoningEffort(MapReasoningLevel(config.Level.Value));
            if (!string.IsNullOrWhiteSpace(config.OpenAiValue)) effort.OpenAiValue = config.OpenAiValue;
            if (config.GeminiThinkingBudget.HasValue) effort.GeminiThinkingBudget = config.GeminiThinkingBudget.Value;
            if (!string.IsNullOrWhiteSpace(config.OllamaThink)) effort.OllamaThink = config.OllamaThink;
            return effort;
        }

        private static Pp.ReasoningEffortLevel MapReasoningLevel(ReasoningLevelEnum level)
        {
            switch (level)
            {
                case ReasoningLevelEnum.Minimal: return Pp.ReasoningEffortLevel.Minimal;
                case ReasoningLevelEnum.Low: return Pp.ReasoningEffortLevel.Low;
                case ReasoningLevelEnum.Medium: return Pp.ReasoningEffortLevel.Medium;
                case ReasoningLevelEnum.High: return Pp.ReasoningEffortLevel.High;
                default: return Pp.ReasoningEffortLevel.Medium;
            }
        }

        private static Pp.ChatMessage MapMessage(ConversationMessage message)
        {
            switch (message.Role)
            {
                case RoleEnum.System:
                    return Pp.ChatMessage.System(message.Content ?? string.Empty);
                case RoleEnum.User:
                    return Pp.ChatMessage.User(message.Content ?? string.Empty);
                case RoleEnum.Assistant:
                    return new Pp.ChatMessage
                    {
                        Role = "assistant",
                        Content = message.Content,
                        ToolCalls = MapToolCallsToPoly(message.ToolCalls)
                    };
                case RoleEnum.Tool:
                    return new Pp.ChatMessage
                    {
                        Role = "tool",
                        ToolCallId = message.ToolCallId,
                        Content = message.Content
                    };
                default:
                    return Pp.ChatMessage.User(message.Content ?? string.Empty);
            }
        }

        private static List<Pp.ToolCall> MapToolCallsToPoly(List<ToolCall>? toolCalls)
        {
            List<Pp.ToolCall> mapped = new List<Pp.ToolCall>();
            if (toolCalls == null)
            {
                return mapped;
            }

            foreach (ToolCall call in toolCalls)
            {
                mapped.Add(new Pp.ToolCall
                {
                    Id = call.Id,
                    Name = call.Name,
                    ArgumentsJson = string.IsNullOrEmpty(call.Arguments) ? "{}" : call.Arguments
                });
            }

            return mapped;
        }

        private static List<ToolCall>? MapToolCallsToMux(List<Pp.ToolCall>? toolCalls)
        {
            if (toolCalls == null || toolCalls.Count == 0)
            {
                return null;
            }

            List<ToolCall> mapped = new List<ToolCall>();
            foreach (Pp.ToolCall call in toolCalls)
            {
                mapped.Add(new ToolCall
                {
                    Id = call.Id ?? string.Empty,
                    Name = call.Name,
                    Arguments = call.ArgumentsJson
                });
            }

            return mapped;
        }

        private static Dictionary<string, object> ConvertSchema(object? schema)
        {
            if (schema is Dictionary<string, object> dictionary)
            {
                return dictionary;
            }

            if (schema == null)
            {
                return new Dictionary<string, object>();
            }

            try
            {
                string json = JsonSerializer.Serialize(schema);
                Dictionary<string, object>? parsed = JsonSerializer.Deserialize<Dictionary<string, object>>(json);
                return parsed ?? new Dictionary<string, object>();
            }
            catch (Exception)
            {
                return new Dictionary<string, object>();
            }
        }

        private static CompletionClientBase CreateClient(EndpointConfig endpoint, HttpClient httpClient)
        {
            CompletionClientBase client;
            switch (endpoint.AdapterType)
            {
                case AdapterTypeEnum.Ollama:
                    // The Ollama adapter speaks Ollama's native API (/api/chat), which lives at the server
                    // root, not under /v1. A base URL carrying a trailing /v1 targets Ollama's separate
                    // OpenAI-compatible surface and yields "404 page not found" against /v1/api/chat. mux's
                    // own defaults and docs historically appended /v1 to ollama base URLs, so tolerate it
                    // here the way OpenAiClient tolerates a base URL that already ends in /v1.
                    client = new OllamaClient(NormalizeOllamaBaseUrl(endpoint.BaseUrl), apiKey: null, logging: SilentLogging, httpClient: httpClient);
                    break;
                case AdapterTypeEnum.OpenAi:
                case AdapterTypeEnum.Vllm:
                case AdapterTypeEnum.OpenAiCompatible:
                default:
                    client = new OpenAiClient(endpoint.BaseUrl, apiKey: null, logging: SilentLogging, httpClient: httpClient);
                    break;
            }

            if (!string.IsNullOrWhiteSpace(endpoint.Model))
            {
                client.Model = endpoint.Model;
            }

            client.TimeoutMs = endpoint.TimeoutMs > 0 ? endpoint.TimeoutMs : 120000;
            return client;
        }

        private static string NormalizeOllamaBaseUrl(string? baseUrl)
        {
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                return baseUrl ?? string.Empty;
            }

            string trimmed = baseUrl.TrimEnd('/');
            if (trimmed.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
            {
                trimmed = trimmed.Substring(0, trimmed.Length - "/v1".Length);
            }

            return trimmed;
        }

        private static LoggingModule CreateSilentLogging()
        {
            // PolyPrompt logs warnings on request failures; console output would corrupt the interactive
            // TUI, so route the shared logging module to nothing.
            LoggingModule logging = new LoggingModule();
            try
            {
                logging.Settings.EnableConsole = false;
            }
            catch (Exception)
            {
            }

            return logging;
        }

        #endregion
    }
}
