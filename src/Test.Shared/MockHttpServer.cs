namespace Test.Shared
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// A lightweight HTTP server using <see cref="HttpListener"/> that simulates OpenAI-compatible endpoints
    /// for use in integration tests.
    /// </summary>
    public class MockHttpServer : IDisposable
    {
        #region Private-Members

        private HttpListener _Listener;
        private CancellationTokenSource _Cts = new CancellationTokenSource();
        private Task? _ListenerTask;
        private bool _Disposed = false;
        private int _Port;
        private List<string> _ReceivedRequests = new List<string>();
        private List<MockRoute> _Routes = new List<MockRoute>();
        private readonly object _Lock = new object();

        #endregion

        #region Public-Members

        /// <summary>
        /// The base URL of the mock server, including scheme and port.
        /// </summary>
        public string BaseUrl
        {
            get => $"http://localhost:{_Port}";
        }

        /// <summary>
        /// The list of raw request bodies received by the server, for use in test assertions.
        /// </summary>
        public List<string> ReceivedRequests
        {
            get
            {
                lock (_Lock)
                {
                    return new List<string>(_ReceivedRequests);
                }
            }
        }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="MockHttpServer"/> class.
        /// A random available port is selected automatically.
        /// </summary>
        public MockHttpServer()
        {
            _Port = GetAvailablePort();
            _Listener = new HttpListener();
            _Listener.Prefixes.Add($"http://localhost:{_Port}/");
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Registers a canned non-streaming response to return when the request body contains the specified prompt text.
        /// </summary>
        /// <param name="promptContains">A substring to match against the request body.</param>
        /// <param name="responseJson">The JSON response body to return.</param>
        public void RegisterResponse(string promptContains, string responseJson)
        {
            lock (_Lock)
            {
                _Routes.Add(new MockRoute
                {
                    PromptContains = promptContains,
                    RouteType = MockRouteType.Standard,
                    ResponseJson = responseJson
                });
            }
        }

        /// <summary>
        /// Registers a tool-call response sequence. The first matching request returns the tool call JSON,
        /// and the subsequent matching request returns the follow-up JSON.
        /// </summary>
        /// <param name="promptContains">A substring to match against the request body.</param>
        /// <param name="toolCallJson">The JSON response containing tool calls to return on the first match.</param>
        /// <param name="followUpJson">The JSON response to return after tool results are submitted.</param>
        public void RegisterToolCallResponse(string promptContains, string toolCallJson, string followUpJson)
        {
            lock (_Lock)
            {
                _Routes.Add(new MockRoute
                {
                    PromptContains = promptContains,
                    RouteType = MockRouteType.ToolCall,
                    ResponseJson = toolCallJson,
                    FollowUpJson = followUpJson,
                    ToolCallReturned = false
                });
            }
        }

        /// <summary>
        /// Registers a streaming SSE response to return when the request body contains the specified prompt text.
        /// </summary>
        /// <param name="promptContains">A substring to match against the request body.</param>
        /// <param name="sseChunks">The list of SSE data lines to stream back.</param>
        public void RegisterStreamingResponse(string promptContains, List<string> sseChunks)
        {
            lock (_Lock)
            {
                _Routes.Add(new MockRoute
                {
                    PromptContains = promptContains,
                    RouteType = MockRouteType.Streaming,
                    SseChunks = new List<string>(sseChunks)
                });
            }
        }

        /// <summary>
        /// Starts the mock HTTP server and begins listening for requests.
        /// </summary>
        public void Start()
        {
            _Listener.Start();
            _ListenerTask = Task.Run(() => ListenLoopAsync(_Cts.Token));
        }

        /// <summary>
        /// Stops the mock HTTP server and waits for the listener to finish.
        /// </summary>
        public void Stop()
        {
            _Cts.Cancel();

            try
            {
                _Listener.Stop();
            }
            catch (ObjectDisposedException)
            {
                // Already stopped
            }

            if (_ListenerTask != null)
            {
                try
                {
                    _ListenerTask.Wait(TimeSpan.FromSeconds(5));
                }
                catch (AggregateException)
                {
                    // Expected on cancellation
                }
            }
        }

        /// <summary>
        /// Releases the resources used by this <see cref="MockHttpServer"/> instance.
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
                    Stop();
                    _Cts.Dispose();
                    (_Listener as IDisposable)?.Dispose();
                }

                _Disposed = true;
            }
        }

        private async Task ListenLoopAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                HttpListenerContext context;

                try
                {
                    context = await _Listener.GetContextAsync().ConfigureAwait(false);
                }
                catch (HttpListenerException)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }

                try
                {
                    await HandleRequestAsync(context).ConfigureAwait(false);
                }
                catch (Exception)
                {
                    // Swallow handler errors in mock server
                }
            }
        }

        private async Task HandleRequestAsync(HttpListenerContext context)
        {
            string requestBody;

            using (StreamReader reader = new StreamReader(context.Request.InputStream, Encoding.UTF8))
            {
                requestBody = await reader.ReadToEndAsync().ConfigureAwait(false);
            }

            lock (_Lock)
            {
                _ReceivedRequests.Add(requestBody);
            }

            MockRoute? matchedRoute = null;

            lock (_Lock)
            {
                int bestLength = -1;

                for (int i = _Routes.Count - 1; i >= 0; i--)
                {
                    MockRoute route = _Routes[i];
                    if (!requestBody.Contains(route.PromptContains, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (route.PromptContains.Length > bestLength)
                    {
                        matchedRoute = route;
                        bestLength = route.PromptContains.Length;
                    }
                }
            }

            if (matchedRoute == null)
            {
                context.Response.StatusCode = 404;
                byte[] notFoundBytes = Encoding.UTF8.GetBytes("{\"error\": \"No matching mock route\"}");
                context.Response.ContentType = "application/json";
                context.Response.ContentLength64 = notFoundBytes.Length;
                await context.Response.OutputStream.WriteAsync(notFoundBytes, 0, notFoundBytes.Length).ConfigureAwait(false);
                context.Response.Close();
                return;
            }

            switch (matchedRoute.RouteType)
            {
                case MockRouteType.Standard:
                    await WriteJsonResponse(context, matchedRoute.ResponseJson!).ConfigureAwait(false);
                    break;

                case MockRouteType.ToolCall:
                    lock (_Lock)
                    {
                        if (!matchedRoute.ToolCallReturned)
                        {
                            matchedRoute.ToolCallReturned = true;
                            WriteJsonResponseSync(context, matchedRoute.ResponseJson!);
                        }
                        else
                        {
                            WriteJsonResponseSync(context, matchedRoute.FollowUpJson!);
                        }
                    }
                    break;

                case MockRouteType.Streaming:
                    await WriteStreamingResponse(context, matchedRoute.SseChunks!).ConfigureAwait(false);
                    break;
            }
        }

        private async Task WriteJsonResponse(HttpListenerContext context, string json)
        {
            byte[] responseBytes = Encoding.UTF8.GetBytes(json);
            context.Response.StatusCode = 200;
            context.Response.ContentType = "application/json";
            context.Response.ContentLength64 = responseBytes.Length;
            await context.Response.OutputStream.WriteAsync(responseBytes, 0, responseBytes.Length).ConfigureAwait(false);
            context.Response.Close();
        }

        private void WriteJsonResponseSync(HttpListenerContext context, string json)
        {
            byte[] responseBytes = Encoding.UTF8.GetBytes(json);
            context.Response.StatusCode = 200;
            context.Response.ContentType = "application/json";
            context.Response.ContentLength64 = responseBytes.Length;
            context.Response.OutputStream.Write(responseBytes, 0, responseBytes.Length);
            context.Response.Close();
        }

        private async Task WriteStreamingResponse(HttpListenerContext context, List<string> sseChunks)
        {
            context.Response.StatusCode = 200;
            context.Response.ContentType = "text/event-stream";
            context.Response.Headers.Add("Cache-Control", "no-cache");

            using (StreamWriter writer = new StreamWriter(context.Response.OutputStream, Encoding.UTF8))
            {
                foreach (string chunk in sseChunks)
                {
                    await writer.WriteAsync($"data: {chunk}\n\n").ConfigureAwait(false);
                    await writer.FlushAsync().ConfigureAwait(false);
                }

                await writer.WriteAsync("data: [DONE]\n\n").ConfigureAwait(false);
                await writer.FlushAsync().ConfigureAwait(false);
            }

            context.Response.Close();
        }

        private static int GetAvailablePort()
        {
            System.Net.Sockets.TcpListener listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        #endregion

        #region Private-Classes

        private enum MockRouteType
        {
            Standard,
            ToolCall,
            Streaming
        }

        private class MockRoute
        {
            public string PromptContains { get; set; } = string.Empty;
            public MockRouteType RouteType { get; set; } = MockRouteType.Standard;
            public string? ResponseJson { get; set; }
            public string? FollowUpJson { get; set; }
            public bool ToolCallReturned { get; set; } = false;
            public List<string>? SseChunks { get; set; }
        }

        #endregion
    }
}
