namespace Mux.Core.Llm
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Net;
    using System.Net.Http;
    using System.Runtime.CompilerServices;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Agent;
    using Mux.Core.Enums;
    using Mux.Core.Models;

    /// <summary>
    /// Orchestrates LLM calls by delegating request building and response parsing to an <see cref="IBackendAdapter"/>.
    /// </summary>
    public class LlmClient : IDisposable
    {
        #region Private-Members

        private HttpClient _HttpClient;
        private IBackendAdapter _Adapter;
        private EndpointConfig _Endpoint;
        private Action<int, int, string>? _OnRetry = null;
        private bool _Disposed = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="LlmClient"/> class.
        /// </summary>
        /// <param name="endpoint">The endpoint configuration to use for LLM requests.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="endpoint"/> is null.</exception>
        public LlmClient(EndpointConfig endpoint)
            : this(endpoint, null, null)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="LlmClient"/> class with optional transport overrides.
        /// </summary>
        /// <param name="endpoint">The endpoint configuration to use for LLM requests.</param>
        /// <param name="httpClient">An optional HTTP client override, primarily for tests.</param>
        /// <param name="adapter">An optional backend adapter override, primarily for tests.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="endpoint"/> is null.</exception>
        public LlmClient(EndpointConfig endpoint, HttpClient? httpClient, IBackendAdapter? adapter = null)
        {
            _Endpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
            _HttpClient = httpClient ?? new HttpClient();

            // Streaming chat responses can remain open for minutes. Per-request timeouts are
            // applied explicitly where needed instead of letting HttpClient cancel the whole stream.
            _HttpClient.Timeout = Timeout.InfiniteTimeSpan;
            _Adapter = adapter ?? ResolveAdapter(endpoint.AdapterType);
        }

        /// <summary>
        /// Resolves the appropriate <see cref="IBackendAdapter"/> for the given adapter type.
        /// </summary>
        /// <param name="type">The adapter type to resolve.</param>
        /// <returns>An instance of the corresponding adapter.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when the adapter type is not recognized.</exception>
        public static IBackendAdapter ResolveAdapter(AdapterTypeEnum type)
        {
            switch (type)
            {
                case AdapterTypeEnum.OpenAi:
                    return new OpenAiAdapter();
                case AdapterTypeEnum.Ollama:
                    return new OllamaAdapter();
                case AdapterTypeEnum.Vllm:
                    return new GenericOpenAiAdapter();
                case AdapterTypeEnum.OpenAiCompatible:
                    return new GenericOpenAiAdapter();
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(type),
                        type,
                        "Unsupported adapter type.");
            }
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

            string sendUrl = _Endpoint.BaseUrl.TrimEnd('/') + "/chat/completions";

            return await RetryHandler.ExecuteWithRetryAsync(async () =>
            {
                HttpRequestMessage request = _Adapter.BuildRequest(messages, tools, _Endpoint, stream: false);
                using CancellationTokenSource requestCts = CreateRequestTimeoutTokenSource(cancellationToken);

                HttpResponseMessage response;
                string responseBody;

                try
                {
                    response = await _HttpClient
                        .SendAsync(request, requestCts.Token)
                        .ConfigureAwait(false);

                    responseBody = await response.Content
                        .ReadAsStringAsync(requestCts.Token)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException ex) when (IsRequestTimeout(requestCts, cancellationToken))
                {
                    throw CreateTimeoutException(ex);
                }

                if (!response.IsSuccessStatusCode)
                {
                    throw new HttpRequestException(
                        $"LLM request to {sendUrl} failed with status {(int)response.StatusCode} ({response.StatusCode}): {responseBody}",
                        null,
                        (System.Net.HttpStatusCode)(int)response.StatusCode);
                }

                JsonDocument document = JsonDocument.Parse(responseBody);
                return _Adapter.NormalizeFinalResponse(document.RootElement);
            }, maxRetries: 3, cancellationToken: cancellationToken, onRetry: _OnRetry).ConfigureAwait(false);
        }

        /// <summary>
        /// Sends a streaming chat completion request and yields agent events as they arrive.
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

            string requestUrl = _Endpoint.BaseUrl.TrimEnd('/') + "/chat/completions";

            ErrorEvent? connectionError = null;
            HttpResponseMessage? response = null;

            try
            {
                response = await RetryHandler.ExecuteWithRetryAsync(async () =>
                {
                    HttpRequestMessage retryRequest = _Adapter.BuildRequest(messages, tools, _Endpoint, stream: true);
                    return await _HttpClient
                        .SendAsync(retryRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                        .ConfigureAwait(false);
                }, maxRetries: 3, cancellationToken: cancellationToken, onRetry: _OnRetry).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                connectionError = new ErrorEvent
                {
                    Code = "llm_connection_error",
                    Message = $"Failed to connect to {requestUrl}: {ex.Message}"
                };
            }

            if (connectionError != null)
            {
                yield return connectionError;
                yield break;
            }

            if (!response!.IsSuccessStatusCode)
            {
                string errorBody = string.Empty;
                try
                {
                    errorBody = await response.Content
                        .ReadAsStringAsync()
                        .ConfigureAwait(false);
                }
                catch
                {
                    // Swallow read errors for the error body
                }

                // If 400 and the error mentions tools not being supported, retry without tools
                if ((int)response.StatusCode == 400
                    && errorBody.Contains("does not support tools", StringComparison.OrdinalIgnoreCase)
                    && tools.Count > 0)
                {
                    response.Dispose();
                    List<ToolDefinition> emptyTools = new List<ToolDefinition>();

                    HttpResponseMessage? retryResponse = null;
                    ErrorEvent? retryError = null;

                    try
                    {
                        HttpRequestMessage retryRequest = _Adapter.BuildRequest(messages, emptyTools, _Endpoint, stream: true);
                        retryResponse = await _HttpClient
                            .SendAsync(retryRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                            .ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception retryEx)
                    {
                        retryError = new ErrorEvent
                        {
                            Code = "llm_connection_error",
                            Message = $"Retry without tools failed to {requestUrl}: {retryEx.Message}"
                        };
                    }

                    if (retryError != null)
                    {
                        yield return retryError;
                        yield break;
                    }

                    if (!retryResponse!.IsSuccessStatusCode)
                    {
                        string retryBody = string.Empty;
                        try { retryBody = await retryResponse.Content.ReadAsStringAsync().ConfigureAwait(false); } catch { }
                        yield return new ErrorEvent
                        {
                            Code = "llm_error",
                            Message = $"LLM request to {requestUrl} failed with status {(int)retryResponse.StatusCode}: {retryBody}"
                        };
                        yield break;
                    }

                    response = retryResponse;
                }
                else
                {
                    yield return new ErrorEvent
                    {
                        Code = "llm_error",
                        Message = $"LLM request to {requestUrl} failed with status {(int)response.StatusCode} ({response.StatusCode}): {errorBody}"
                    };
                    yield break;
                }
            }

            ErrorEvent? streamError = null;
            Stream? responseStream = null;

            try
            {
                responseStream = await response.Content
                    .ReadAsStreamAsync(cancellationToken)
                    .ConfigureAwait(false);
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
                    Message = $"Failed to read response stream from {requestUrl}: {ex.Message}"
                };
            }

            if (streamError != null)
            {
                yield return streamError;
                yield break;
            }

            await foreach (AgentEvent agentEvent in _Adapter
                .ReadStreamingEvents(responseStream!, _Endpoint, cancellationToken)
                .ConfigureAwait(false))
            {
                yield return agentEvent;
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
                    _HttpClient?.Dispose();
                }

                _Disposed = true;
            }
        }

        private CancellationTokenSource CreateRequestTimeoutTokenSource(CancellationToken cancellationToken)
        {
            CancellationTokenSource requestCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            requestCts.CancelAfter(_Endpoint.TimeoutMs);
            return requestCts;
        }

        private static bool IsRequestTimeout(CancellationTokenSource requestCts, CancellationToken cancellationToken)
        {
            return requestCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested;
        }

        private TaskCanceledException CreateTimeoutException(OperationCanceledException innerException)
        {
            string message = $"The request timed out after {_Endpoint.TimeoutMs}ms.";
            return new TaskCanceledException(message, innerException);
        }

        #endregion
    }
}
