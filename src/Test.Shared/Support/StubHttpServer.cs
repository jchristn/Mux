namespace Test.Shared.Support
{
    using System;
    using System.Collections.Generic;
    using System.Net;
    using System.Net.Sockets;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// A loopback HTTP server that answers every request with one fixed status, content type, body, and optional
    /// headers, and records the headers each request carried. Used to drive MCP connection failures (401, 500,
    /// non-JSON success) without a real MCP server.
    /// </summary>
    public sealed class StubHttpServer : IDisposable
    {
        #region Private-Members

        private readonly HttpListener _Listener = new HttpListener();
        private readonly CancellationTokenSource _Cts = new CancellationTokenSource();
        private readonly int _Status;
        private readonly string _ContentType;
        private readonly string _Body;
        private readonly Dictionary<string, string> _Headers;
        private readonly List<Dictionary<string, string>> _Requests = new List<Dictionary<string, string>>();
        private readonly Task _Loop;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Starts the server on a free loopback port.
        /// </summary>
        /// <param name="status">The status code for every response.</param>
        /// <param name="contentType">The response content type.</param>
        /// <param name="body">The response body.</param>
        /// <param name="headers">Extra response headers, or null.</param>
        public StubHttpServer(int status, string contentType, string body, Dictionary<string, string>? headers = null)
        {
            _Status = status;
            _ContentType = contentType;
            _Body = body;
            _Headers = headers ?? new Dictionary<string, string>();
            Port = FreeLoopbackPort();
            _Listener.Prefixes.Add("http://127.0.0.1:" + Port + "/");
            _Listener.Start();
            _Loop = Task.Run(LoopAsync);
        }

        #endregion

        #region Public-Members

        /// <summary>The bound port.</summary>
        public int Port { get; }

        /// <summary>The base URL, for example <c>http://127.0.0.1:5123</c>.</summary>
        public string BaseUrl => "http://127.0.0.1:" + Port;

        /// <summary>The request headers received so far (one dictionary per request).</summary>
        public List<Dictionary<string, string>> Requests
        {
            get { lock (_Requests) { return new List<Dictionary<string, string>>(_Requests); } }
        }

        #endregion

        #region Public-Methods

        /// <summary>Returns a loopback port that nothing is listening on.</summary>
        /// <returns>The port.</returns>
        public static int FreeLoopbackPort()
        {
            TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        /// <summary>Stops the server.</summary>
        public void Dispose()
        {
            _Cts.Cancel();
            try { _Listener.Stop(); } catch (Exception) { }
            try { _Listener.Close(); } catch (Exception) { }
            try { _Loop.Wait(2000); } catch (Exception) { }
            _Cts.Dispose();
        }

        #endregion

        #region Private-Methods

        private async Task LoopAsync()
        {
            while (!_Cts.IsCancellationRequested)
            {
                HttpListenerContext context;
                try
                {
                    context = await _Listener.GetContextAsync().ConfigureAwait(false);
                }
                catch (Exception)
                {
                    return;
                }

                try
                {
                    Dictionary<string, string> seen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (string? key in context.Request.Headers.AllKeys)
                    {
                        if (key != null) seen[key] = context.Request.Headers[key] ?? string.Empty;
                    }

                    lock (_Requests)
                    {
                        _Requests.Add(seen);
                    }

                    byte[] bytes = Encoding.UTF8.GetBytes(_Body);
                    context.Response.StatusCode = _Status;
                    context.Response.ContentType = _ContentType;
                    foreach (KeyValuePair<string, string> header in _Headers)
                    {
                        context.Response.Headers[header.Key] = header.Value;
                    }

                    context.Response.ContentLength64 = bytes.Length;
                    await context.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
                    context.Response.Close();
                }
                catch (Exception)
                {
                    // A client that hung up mid-response is not a test failure.
                }
            }
        }

        #endregion
    }
}
