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
    using Mux.Core.Settings;
    using Mux.Core.Utility;
    using PolyPrompt.Auth;
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
        private LlmCallMetrics? _LastCall = null;
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
        /// Performance metrics (token usage plus time-to-first-token, streaming time, total runtime, and
        /// throughput) for the most recent streaming call, or null when no streaming call has completed.
        /// Populated from the provider response; timing fields are null when the provider did not report
        /// them. Read by callers that persist usage telemetry.
        /// </summary>
        public LlmCallMetrics? LastCall
        {
            get => _LastCall;
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

            Pp.ToolChatResponse response;
            for (int attempt = 0; ; attempt++)
            {
                response = await _Client.ToolChatAsync(request, cancellationToken).ConfigureAwait(false);

                // Retry only transport-level failures (connection refused, reset, DNS, TLS), which
                // PolyPrompt surfaces as an unsuccessful response with no HTTP status. An HTTP error
                // status is a definitive answer and is left for the caller to handle.
                if (response.Success || !IsTransportFailure(response.StatusCode) || attempt >= MaxRetries)
                {
                    break;
                }

                _OnRetry?.Invoke(attempt + 1, MaxRetries, string.IsNullOrEmpty(response.Error) ? "connection error" : response.Error);
                await Task.Delay(RetryBackoff(attempt), cancellationToken).ConfigureAwait(false);
            }

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
        /// Attempts to load (and thereby validate) the configured model by issuing a minimal streaming
        /// completion request and stopping at the first streamed token. Streaming with a real token budget —
        /// rather than a near-zero non-streaming request — means a reasoning ("thinking") model, which spends
        /// tokens thinking before it can emit an answer, still validates: its first reasoning or answer chunk
        /// is enough to confirm the endpoint is reachable and the model is generating, at which point the rest
        /// of the completion is cancelled. For providers that lazily load models (such as Ollama) this warms
        /// the model; for hosted providers it confirms the model name, base URL, and credentials are usable.
        /// The request uses the same transport and adapter as real turns. Never throws for backend/transport
        /// errors; those are returned as a failure result, with <see cref="ModelLoadResult.Reachable"/>
        /// distinguishing an endpoint that answered (even with an error) from one that could not be reached.
        /// </summary>
        /// <param name="cancellationToken">A token to cancel the probe.</param>
        /// <returns>A <see cref="ModelLoadResult"/> describing success or the failure details.</returns>
        public async Task<ModelLoadResult> LoadModelAsync(CancellationToken cancellationToken)
        {
            Pp.ToolChatRequest request = new Pp.ToolChatRequest
            {
                Model = string.IsNullOrWhiteSpace(_Endpoint.Model) ? null : _Endpoint.Model,

                // Give the model a real budget so it can actually produce output — a reasoning ("thinking")
                // model spends tokens thinking before it can emit anything, and a 1-token cap made it fail a
                // perfectly reachable endpoint. We stream and stop at the very first chunk (see below), so the
                // budget is never reached and a healthy endpoint still validates near-instantly.
                MaxTokens = 512,
                ToolChoice = "none"
            };

            // A single minimal user message keeps the request valid across providers that reject an empty
            // messages array.
            request.Messages.Add(Pp.ChatMessage.User("."));

            string failureDetail = "no response";
            bool reachable = false;

            for (int attempt = 0; ; attempt++)
            {
                bool transportFailure = false;

                try
                {
                    // Stream rather than wait for a full completion: the first streamed chunk — whether it is
                    // answer text or the model's reasoning — proves the endpoint is reachable AND the model is
                    // generating, which is exactly what the health check needs. We then stop immediately.
                    Pp.ToolChatStreamingResponse response = await _Client.ToolChatStreamingAsync(request, cancellationToken).ConfigureAwait(false);

                    if (response.Success)
                    {
                        IAsyncEnumerator<Pp.ToolChatStreamingChunk> enumerator = response.Chunks.GetAsyncEnumerator(cancellationToken);
                        try
                        {
                            while (await enumerator.MoveNextAsync().ConfigureAwait(false))
                            {
                                Pp.ToolChatStreamingChunk chunk = enumerator.Current;
                                if (chunk != null && (!string.IsNullOrEmpty(chunk.Text) || !string.IsNullOrEmpty(chunk.ReasoningText)))
                                {
                                    // First real token received — cancel the rest of the completion by
                                    // disposing the stream (in the finally) and report success.
                                    break;
                                }
                            }
                        }
                        finally
                        {
                            try
                            {
                                await enumerator.DisposeAsync().ConfigureAwait(false);
                            }
                            catch (Exception)
                            {
                                // Best-effort: tearing down the stream after we've already validated it must
                                // never turn a healthy endpoint into a failure.
                            }
                        }

                        // A successfully opened stream (with or without a content chunk before it ended)
                        // proves the endpoint responded.
                        return ModelLoadResult.Ok();
                    }

                    string status = response.StatusCode.HasValue ? $"status {response.StatusCode.Value}" : "no response";
                    failureDetail = string.IsNullOrWhiteSpace(response.Error) ? status : $"{status}: {response.Error}";

                    // Only transport-level failures (no HTTP status) are transient; an HTTP error status is a
                    // definitive answer and should not be retried. A returned status — even an error one —
                    // proves the endpoint/URL/credentials are reachable, so the UI does not mislabel a
                    // reachable-but-fussy backend as "unreachable".
                    transportFailure = IsTransportFailure(response.StatusCode);
                    reachable = !transportFailure;
                    if (!transportFailure)
                    {
                        return ModelLoadResult.Fail(failureDetail, reachable: true);
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    failureDetail = ex.Message;
                    transportFailure = true;
                    reachable = false;
                }

                if (!transportFailure || attempt >= MaxRetries)
                {
                    return ModelLoadResult.Fail(failureDetail, reachable);
                }

                await Task.Delay(RetryBackoff(attempt), cancellationToken).ConfigureAwait(false);
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

                // Space retries apart so a transient connection failure has time to clear; immediate
                // back-to-back attempts would all fail against the same brief condition.
                await Task.Delay(RetryBackoff(attempt), cancellationToken).ConfigureAwait(false);
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
            return IsTransportFailure(response.StatusCode);
        }

        private static bool IsTransportFailure(int? statusCode)
        {
            // A transport-level failure (connection refused, reset, DNS, TLS) carries no HTTP status;
            // an HTTP error status (4xx/5xx) is a definitive answer from the endpoint, not transient.
            return !statusCode.HasValue || statusCode.Value == 0;
        }

        private static TimeSpan RetryBackoff(int attempt)
        {
            // Exponential backoff (100ms, 200ms, 400ms, ...) capped at 500ms. Enough to ride out a
            // transient connection refusal or reset — a momentarily saturated loopback listener under
            // load, or a local model still warming — without materially delaying feedback from an
            // endpoint that is genuinely unreachable.
            int shift = Math.Min(attempt, 3);
            int milliseconds = Math.Min(100 * (1 << shift), 500);
            return TimeSpan.FromMilliseconds(milliseconds);
        }

        private void RecordUsage(Pp.ToolChatStreamingResponse response)
        {
            Pp.ChatStreamingUsage? usage = response.Usage;
            int promptReported = usage?.PromptTokens ?? 0;
            int output = usage?.CompletionTokens ?? 0;
            int cacheRead = usage?.CachedPromptTokens ?? 0;
            int cacheCreate = usage?.CacheCreationTokens ?? 0;
            int reasoning = usage?.ReasoningTokens ?? 0;

            // Normalize cached-token semantics to a single convention: mux's InputTokens is always the FULL
            // input count and CachedTokens is the cache-read subset of it. PolyPrompt reports cache buckets
            // provider-native (per its CACHED_TOKENS.md Option A): OpenAI/Gemini fold cached reads into
            // PromptTokens, whereas Anthropic/Bedrock report them as additional. Add the additional buckets
            // for the latter so token totals and cost are consistent across providers. Cache-creation tokens
            // fall into the (uncached) input bucket and bill at the input rate; the cache-read bucket bills at
            // the cached rate. Reasoning tokens are already counted inside CompletionTokens for billing and
            // are surfaced separately for display only.
            bool cachedIsAdditional = _Endpoint.AdapterType == AdapterTypeEnum.Anthropic
                || _Endpoint.AdapterType == AdapterTypeEnum.Bedrock;
            int input = cachedIsAdditional ? promptReported + cacheRead + cacheCreate : promptReported;
            int total = input + output;

            LlmUsage record = new LlmUsage
            {
                InputTokens = input,
                OutputTokens = output,
                CachedTokens = cacheRead,
                ReasoningTokens = reasoning,
                TotalTokens = total
            };

            _LastUsage = record;
            _CumulativeUsage.Add(record);

            long? streamingMs = ComputeStreamingMs(response);

            // Generation throughput = output tokens over the streaming window (first token → last token).
            // We compute it here rather than using the SDK's OverallTokensPerSecond, which divides prompt +
            // output tokens by the whole runtime (including time-to-first-token); for a large prompt answered
            // quickly that reads as many hundreds of tok/s, far above the real token-generation rate. When
            // the streaming window is unknown (e.g. a non-streamed response) throughput is left unreported.
            double? tokensPerSecond = (streamingMs.HasValue && streamingMs.Value > 0 && output > 0)
                ? output / (streamingMs.Value / 1000.0)
                : (double?)null;

            _LastCall = new LlmCallMetrics
            {
                Usage = record,
                TimeToFirstTokenMs = response.TimeToFirstTokenMs >= 0 ? response.TimeToFirstTokenMs : (long?)null,
                StreamingMs = streamingMs,
                TotalMs = response.OverallRuntimeMs >= 0 ? response.OverallRuntimeMs : (long?)null,
                TokensPerSecond = tokensPerSecond,
                FinishReason = string.IsNullOrEmpty(response.FinishReason) ? null : response.FinishReason,
                Model = string.IsNullOrEmpty(response.Model) ? null : response.Model,
                Success = response.Success
            };
        }

        private static long? ComputeStreamingMs(Pp.ToolChatStreamingResponse response)
        {
            // Streaming duration is first-token to last-token. Both timings use -1 as the "unreported"
            // sentinel; only compute when both are present and ordered.
            if (response.TimeToFirstTokenMs < 0 || response.TimeToLastTokenMs < 0)
            {
                return null;
            }

            long delta = response.TimeToLastTokenMs - response.TimeToFirstTokenMs;
            return delta >= 0 ? delta : (long?)null;
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

            // API keys and cloud coordinates may be stored as ${VAR} references; resolve them the same way
            // header values are resolved so the client receives literal secrets.
            string? apiKey = ResolveConfigValue(endpoint.ApiKey);

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
                case AdapterTypeEnum.Anthropic:
                    // Anthropic authenticates with an API key sent as x-api-key; PolyPrompt's client attaches
                    // it to the injected transport. A blank base URL falls back to the public API root.
                    client = new AnthropicClient(
                        DefaultIfBlank(endpoint.BaseUrl, "https://api.anthropic.com"),
                        apiKey,
                        SilentLogging,
                        httpClient);
                    break;
                case AdapterTypeEnum.Gemini:
                    // Gemini (AI Studio) carries the API key in the request URL, so it must be passed to the
                    // client rather than supplied as a header.
                    client = new GeminiClient(
                        DefaultIfBlank(endpoint.BaseUrl, "https://generativelanguage.googleapis.com"),
                        apiKey,
                        SilentLogging,
                        httpClient);
                    break;
                case AdapterTypeEnum.AzureOpenAi:
                    // Azure routes by resource endpoint + deployment (the model), keyed by an api-key header.
                    if (string.IsNullOrWhiteSpace(endpoint.BaseUrl))
                        throw new InvalidOperationException($"Endpoint '{endpoint.Name}' (azure-openai) requires a baseUrl set to the Azure resource endpoint, e.g. https://my-resource.openai.azure.com.");
                    if (string.IsNullOrWhiteSpace(endpoint.Model))
                        throw new InvalidOperationException($"Endpoint '{endpoint.Name}' (azure-openai) requires a model set to the Azure deployment name.");
                    client = new AzureOpenAiClient(
                        endpoint.BaseUrl,
                        endpoint.Model,
                        apiKey ?? string.Empty,
                        string.IsNullOrWhiteSpace(endpoint.ApiVersion) ? null : ResolveConfigValue(endpoint.ApiVersion),
                        SilentLogging,
                        httpClient);
                    break;
                case AdapterTypeEnum.Vertex:
                {
                    // Vertex needs an explicit project and region; credentials come from Application Default
                    // Credentials (GOOGLE_APPLICATION_CREDENTIALS or the metadata server), not from config.
                    string project = ResolveConfigValue(endpoint.Project) ?? string.Empty;
                    string region = ResolveConfigValue(endpoint.Region) ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(project) || string.IsNullOrWhiteSpace(region))
                        throw new InvalidOperationException($"Endpoint '{endpoint.Name}' (vertex) requires both 'project' and 'region'. Credentials come from Application Default Credentials (set GOOGLE_APPLICATION_CREDENTIALS).");
                    client = new VertexAiClient(
                        project,
                        region,
                        new AdcCredential(),
                        DefaultIfBlankOrNull(endpoint.BaseUrl),
                        SilentLogging,
                        httpClient);
                    break;
                }
                case AdapterTypeEnum.Bedrock:
                {
                    // Bedrock needs a region; AWS credentials are resolved from the standard AWS_* environment
                    // variables and SigV4-signed per request by PolyPrompt.
                    string region = ResolveConfigValue(endpoint.Region) ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(region))
                        throw new InvalidOperationException($"Endpoint '{endpoint.Name}' (bedrock) requires a 'region'. Credentials come from the AWS environment (AWS_ACCESS_KEY_ID / AWS_SECRET_ACCESS_KEY / AWS_SESSION_TOKEN).");
                    client = new BedrockClient(
                        new EnvironmentAwsCredential(region),
                        region,
                        SilentLogging,
                        httpClient,
                        DefaultIfBlankOrNull(endpoint.BaseUrl));
                    break;
                }
                case AdapterTypeEnum.OpenAi:
                case AdapterTypeEnum.Vllm:
                case AdapterTypeEnum.OpenAiCompatible:
                default:
                    // The OpenAI family authenticates via Headers (e.g. Authorization: Bearer ${KEY}), which
                    // mux applies to the injected transport, so no api key is passed to the client.
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

        private static string? ResolveConfigValue(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            return SettingsLoader.ExpandEnvironmentVariables(value);
        }

        private static string DefaultIfBlank(string? value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value) ? fallback : value!;
        }

        private static string? DefaultIfBlankOrNull(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value;
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
