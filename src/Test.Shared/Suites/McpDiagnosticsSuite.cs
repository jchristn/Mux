namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Net.Http;
    using System.Net.Sockets;
    using System.Security.Authentication;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Enums;
    using Mux.Core.Models;
    using Mux.Core.Sessions;
    using Mux.Core.Tools;
    using Mux.Server;
    using Test.Shared.Support;
    using Touchstone.Core;

    /// <summary>
    /// Touchstone suite for MCP connection diagnostics: a failed connection must say why (connection refused, host not
    /// found, HTTP status with the response body and headers, a JSON-RPC error, a non-JSON success, a missing stdio
    /// command), never echo auth secrets, and reach every surface through <see cref="McpConnectionResult"/> and the
    /// <c>POST /v1.0/api/mcp-servers/validate</c> route. Positive and negative cases for the classifier and helpers.
    /// </summary>
    public static class McpDiagnosticsSuite
    {
        #region Private-Members

        private const string SuiteId = "McpDiagnostics";
        private const string Secret = "sk-test-SECRET-9f8e7d6c";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Builds the MCP diagnostics suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/> for the MCP diagnostics cases.</returns>
        public static TestSuiteDescriptor Create()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();
            void Add(string id, string name, Func<CancellationToken, Task> body)
            {
                cases.Add(new TestCaseDescriptor(SuiteId, id, name, body));
            }

            // --- HTTP failures, end to end through McpToolManager ---
            Add("Http401ShowsStatusBodyAndChallenge", "A 401 reports the status, the body, and WWW-Authenticate, and masks the token", async (CancellationToken ct) =>
            {
                using (StubHttpServer stub = new StubHttpServer(401, "application/json", "{\"error\":\"invalid token " + Secret + "\"}", new Dictionary<string, string> { ["WWW-Authenticate"] = "Bearer realm=\"mcp\"", ["Retry-After"] = "30", ["X-Stub"] = "yes" }))
                {
                    McpConnectionResult result = await ConnectAsync(Http("auth", stub.BaseUrl, Bearer()), ct).ConfigureAwait(false);
                    MuxAssert.IsFalse(result.Connected, "not connected");
                    string error = result.Error ?? string.Empty;
                    MuxAssert.Contains("Failed to connect to HTTP MCP server 'auth' at " + stub.BaseUrl + ": HTTP 401 Unauthorized", FirstLine(error), "summary names the status");
                    MuxAssert.Contains("Response: HTTP 401 Unauthorized", error, "status line");
                    MuxAssert.Contains("invalid token <redacted>", error, "body shown with the secret masked");
                    MuxAssert.Contains("WWW-Authenticate: Bearer realm=\"mcp\"", error, "challenge header shown");
                    MuxAssert.Contains("Content-Type: application/json", error, "content type shown");
                    MuxAssert.Contains("Retry-After: 30", error, "Retry-After shown");
                    MuxAssert.Contains("Mcp-Session-Id: absent", error, "session header presence shown");
                    MuxAssert.Contains("Auth headers sent: Authorization (values hidden)", error, "auth header named, value hidden");
                    MuxAssert.Contains("Hint: the server rejected the credentials", error, "auth hint");
                    MuxAssert.DoesNotContain(Secret, error, "the secret never appears");
                    MuxAssert.IsNotNull(result.Details, "details carried separately");
                    MuxAssert.DoesNotContain(Secret, result.Details!, "the secret never appears in details");
                    MuxAssert.IsTrue(stub.Requests.Exists(r => r.TryGetValue("Authorization", out string? v) && v == "Bearer " + Secret), "the probe sent the real credentials");
                }
            });
            Add("Http500ShowsBody", "A 500 reports the status and the server's error body", async (CancellationToken ct) =>
            {
                using (StubHttpServer stub = new StubHttpServer(500, "text/plain", "database exploded at row 42"))
                {
                    McpConnectionResult result = await ConnectAsync(Http("boom", stub.BaseUrl, null), ct).ConfigureAwait(false);
                    string error = result.Error ?? string.Empty;
                    MuxAssert.Contains(": HTTP 500 Internal Server Error", FirstLine(error), "summary names the status");
                    MuxAssert.Contains("Body: database exploded at row 42", error, "body shown");
                    MuxAssert.Contains("Content-Type: text/plain", error, "content type shown");
                    MuxAssert.Contains("check its logs", error, "server-error hint");
                    MuxAssert.DoesNotContain("Auth headers sent", error, "no auth line without auth");
                }
            });
            Add("Http404HintsAtPath", "A 404 reports the status and points at the MCP path", async (CancellationToken ct) =>
            {
                using (StubHttpServer stub = new StubHttpServer(404, "text/html", "<h1>Not Found</h1>"))
                {
                    McpServerConfig config = Http("path", stub.BaseUrl, null);
                    config.McpPath = "/wrong";
                    McpConnectionResult result = await ConnectAsync(config, ct).ConfigureAwait(false);
                    string error = result.Error ?? string.Empty;
                    MuxAssert.Contains("HTTP 404", FirstLine(error), "status in summary");
                    MuxAssert.Contains("Request: POST " + stub.BaseUrl + "/wrong", error, "exact URL shown");
                    MuxAssert.Contains("the MCP endpoint path may be wrong (configured path '/wrong')", error, "path hint");
                    MuxAssert.Contains("<h1>Not Found</h1>", error, "body shown");
                }
            });
            Add("Http200NonJsonExplained", "A 200 with HTML says the response is not JSON-RPC", async (CancellationToken ct) =>
            {
                using (StubHttpServer stub = new StubHttpServer(200, "text/html", "<html><body>hello, not mcp</body></html>"))
                {
                    McpConnectionResult result = await ConnectAsync(Http("html", stub.BaseUrl, null), ct).ConfigureAwait(false);
                    string error = result.Error ?? string.Empty;
                    MuxAssert.IsFalse(result.Connected, "not connected");
                    MuxAssert.Contains("not JSON-RPC", FirstLine(error), "summary explains");
                    MuxAssert.Contains("text/html", error, "content type shown");
                    MuxAssert.Contains("hello, not mcp", error, "body shown");
                }
            });
            Add("Http200JsonRpcErrorExplained", "A JSON-RPC error answer to initialize is reported with its code and message", async (CancellationToken ct) =>
            {
                using (StubHttpServer stub = new StubHttpServer(200, "application/json", "{\"jsonrpc\":\"2.0\",\"id\":\"x\",\"error\":{\"code\":-32602,\"message\":\"unsupported protocol version\"}}"))
                {
                    McpConnectionResult result = await ConnectAsync(Http("rpc", stub.BaseUrl, null), ct).ConfigureAwait(false);
                    MuxAssert.Contains("initialize returned JSON-RPC error -32602: unsupported protocol version", FirstLine(result.Error ?? string.Empty), "RPC error in summary");
                }
            });
            Add("ConnectionRefusedNamed", "A closed port is reported as connection refused with host and port", async (CancellationToken ct) =>
            {
                int port = StubHttpServer.FreeLoopbackPort();
                McpConnectionResult result = await ConnectAsync(Http("down", "http://127.0.0.1:" + port, Bearer()), ct).ConfigureAwait(false);
                string error = result.Error ?? string.Empty;
                MuxAssert.IsFalse(result.Connected, "not connected");
                MuxAssert.Contains("connection refused by 127.0.0.1:" + port, FirstLine(error), "refused, with host and port");
                MuxAssert.Contains("Hint: start the MCP server", error, "start hint");
                MuxAssert.DoesNotContain(Secret, error, "secret not echoed");
            });
            Add("UnknownHostNamed", "An unresolvable host is reported as host not found", async (CancellationToken ct) =>
            {
                McpConnectionResult result = await ConnectAsync(Http("dns", "http://mux-test-host.invalid:8080", null), ct).ConfigureAwait(false);
                string error = result.Error ?? string.Empty;
                MuxAssert.Contains("host not found: DNS lookup for 'mux-test-host.invalid' failed", FirstLine(error), "DNS failure named");
                MuxAssert.Contains("check the host name", error, "DNS hint");
            });
            Add("ApiKeyValueMasked", "An API-key header is named but its value never appears, even when the body echoes it", async (CancellationToken ct) =>
            {
                using (StubHttpServer stub = new StubHttpServer(403, "application/json", "{\"error\":\"key " + Secret + " is revoked\"}"))
                {
                    McpServerConfig config = Http("key", stub.BaseUrl, new McpAuthConfig { Type = McpAuthTypeEnum.ApiKey, ApiKeyHeader = "X-Team-Key", ApiKeyValue = Secret });
                    McpConnectionResult result = await ConnectAsync(config, ct).ConfigureAwait(false);
                    string error = result.Error ?? string.Empty;
                    MuxAssert.Contains("HTTP 403", FirstLine(error), "status");
                    MuxAssert.Contains("Auth headers sent: X-Team-Key (values hidden)", error, "header named");
                    MuxAssert.Contains("key <redacted> is revoked", error, "echoed key masked");
                    MuxAssert.DoesNotContain(Secret, error, "key never shown");
                }
            });

            Add("ProbeReplyCarriesFullReply", "The probe exposes the full reply: status, content type and length, headers with sensitive values redacted, body, timing", async (CancellationToken ct) =>
            {
                Dictionary<string, string> headers = new Dictionary<string, string>
                {
                    ["Set-Cookie"] = "session=" + Secret + "; HttpOnly",
                    ["Mcp-Session-Id"] = "abc-123",
                    ["Retry-After"] = "120",
                    ["X-Echo"] = "token " + Secret
                };
                using (StubHttpServer stub = new StubHttpServer(503, "text/plain; charset=utf-8", "overloaded, try later", headers))
                {
                    McpServerConfig config = Http("busy", stub.BaseUrl, Bearer());
                    Dictionary<string, string> sent = new Dictionary<string, string> { ["Authorization"] = "Bearer " + Secret };
                    McpConnectionException ex = await McpConnectionDiagnostics.DiagnoseHttpAsync(config, stub.BaseUrl + "/mcp", sent, null, TimeSpan.FromSeconds(10), ct).ConfigureAwait(false);
                    McpProbeReply? reply = ex.Reply;
                    MuxAssert.IsNotNull(reply, "reply captured");
                    MuxAssert.AreEqual("POST", reply!.RequestMethod, "method");
                    MuxAssert.AreEqual(stub.BaseUrl + "/mcp", reply.RequestUrl, "url");
                    MuxAssert.AreEqual(503, reply.StatusCode, "status");
                    MuxAssert.AreEqual("Service Unavailable", reply.ReasonPhrase, "reason");
                    MuxAssert.Contains("text/plain", reply.ContentType, "content type");
                    MuxAssert.AreEqual((long?)"overloaded, try later".Length, reply.ContentLength, "content length");
                    MuxAssert.AreEqual("overloaded, try later", reply.Body, "body");
                    MuxAssert.IsFalse(reply.BodyTruncated, "short body not cut");
                    MuxAssert.IsFalse(reply.IsSuccess, "503 is not success");
                    MuxAssert.IsTrue(reply.ElapsedMs >= 0, "timing");
                    MuxAssert.AreEqual("<redacted>", reply.GetHeader("set-cookie"), "Set-Cookie redacted (case-insensitive lookup)");
                    MuxAssert.AreEqual("abc-123", reply.GetHeader("Mcp-Session-Id"), "session id kept");
                    MuxAssert.AreEqual("120", reply.GetHeader("Retry-After"), "Retry-After kept");
                    MuxAssert.AreEqual("token <redacted>", reply.GetHeader("X-Echo"), "echoed secret masked");
                    MuxAssert.IsNull(reply.GetHeader("X-Not-There"), "absent header is null");
                    MuxAssert.Contains(": HTTP 503 Service Unavailable", ex.Summary, "summary");
                    MuxAssert.Contains("Retry-After: 120", ex.Details, "Retry-After in details");
                    MuxAssert.Contains("Mcp-Session-Id: present", ex.Details, "session presence in details");
                    MuxAssert.Contains("Content-Length: 21", ex.Details, "length in details");
                    MuxAssert.DoesNotContain(Secret, ex.Message, "secret never in the message");
                    MuxAssert.DoesNotContain("abc-123", ex.Details, "session id value not printed, only presence");
                }
            });
            Add("ProbeReplyCutsLongBody", "A long body is cut and flagged", async (CancellationToken ct) =>
            {
                using (StubHttpServer stub = new StubHttpServer(500, "text/plain", new string('x', 20000)))
                {
                    McpConnectionException ex = await McpConnectionDiagnostics.DiagnoseHttpAsync(Http("big", stub.BaseUrl, null), stub.BaseUrl + "/mcp", null, null, TimeSpan.FromSeconds(10), ct).ConfigureAwait(false);
                    MuxAssert.IsNotNull(ex.Reply, "reply captured");
                    MuxAssert.IsTrue(ex.Reply!.BodyTruncated, "flagged as cut");
                    MuxAssert.IsTrue(ex.Reply.Body.Length <= 2000, "body capped");
                    MuxAssert.Contains("Body (cut): xxx", ex.Details, "details say the body was cut");
                }
            });
            Add("ConnectionFailureHasNoReply", "A refused connection carries no reply object", async (CancellationToken ct) =>
            {
                int port = StubHttpServer.FreeLoopbackPort();
                McpConnectionException ex = await McpConnectionDiagnostics.DiagnoseHttpAsync(Http("none", "http://127.0.0.1:" + port, null), "http://127.0.0.1:" + port + "/mcp", null, null, TimeSpan.FromSeconds(5), ct).ConfigureAwait(false);
                MuxAssert.IsNull(ex.Reply, "no reply");
                MuxAssert.Contains("connection refused", ex.Summary, "cause");
            });
            Add("InvalidUrlExplained", "A malformed URL is reported without a probe", async (CancellationToken ct) =>
            {
                McpConnectionException ex = await McpConnectionDiagnostics.DiagnoseHttpAsync(Http("bad", "not a url", null), "not a url/mcp", null, null, TimeSpan.FromSeconds(5), ct).ConfigureAwait(false);
                MuxAssert.Contains("the URL is not valid", ex.Summary, "cause");
                MuxAssert.IsNull(ex.Reply, "no reply");
                MuxAssert.Throws<ArgumentNullException>(() => McpConnectionDiagnostics.DiagnoseHttpAsync(null!, "x", null, null, TimeSpan.Zero, ct).GetAwaiter().GetResult(), "null config rejected");
            });

            Add("RuntimeStatusAndNoticeCarryCause", "McpRuntime reports the cause on the server status and announces a repeated failure once", async (CancellationToken ct) =>
            {
                int port = StubHttpServer.FreeLoopbackPort();
                List<McpServerConfig> configs = new List<McpServerConfig> { Http("down", "http://127.0.0.1:" + port, null) };
                List<string> notices = new List<string>();
                using (McpRuntime runtime = new McpRuntime(() => configs, () => { }, TimeSpan.FromMinutes(10), message => { lock (notices) { notices.Add(message); } }))
                {
                    runtime.Start();
                    await Task.WhenAny(runtime.FirstRefreshCompleted, Task.Delay(TimeSpan.FromSeconds(30), ct)).ConfigureAwait(false);
                    List<McpServerStatus> status = runtime.GetStatus();
                    MuxAssert.AreEqual(1, status.Count, "one server");
                    MuxAssert.IsFalse(status[0].Connected, "down");
                    MuxAssert.Contains("connection refused by 127.0.0.1:" + port, status[0].Error ?? string.Empty, "status carries the cause");
                    MuxAssert.Contains("Request: POST", status[0].Error ?? string.Empty, "status carries the details");

                    // A forced refresh fails the same way; the details differ only in timings, so no second notice.
                    runtime.RequestRefresh();
                    await Task.Delay(TimeSpan.FromSeconds(2), ct).ConfigureAwait(false);
                    lock (notices)
                    {
                        MuxAssert.AreEqual(1, notices.Count, "announced once");
                        MuxAssert.Contains("Unable to connect to MCP server down using http: Failed to connect to HTTP MCP server 'down'", notices[0], "notice names the server");
                        MuxAssert.Contains("connection refused", notices[0], "notice carries the cause");
                    }
                }
            });

            // --- stdio ---
            Add("StdioMissingCommandNamed", "A stdio command that does not exist is reported as not found on PATH", async (CancellationToken ct) =>
            {
                McpServerConfig config = new McpServerConfig { Name = "ghost", Transport = McpTransportTypeEnum.Stdio, Command = "mux-no-such-command-4d2c", Args = new List<string> { "--port", "7" } };
                McpConnectionResult result = await ConnectAsync(config, ct).ConfigureAwait(false);
                string error = result.Error ?? string.Empty;
                MuxAssert.IsFalse(result.Connected, "not connected");
                MuxAssert.Contains("Failed to start MCP server 'ghost' (mux-no-such-command-4d2c): command 'mux-no-such-command-4d2c' was not found on PATH", FirstLine(error), "summary");
                MuxAssert.Contains("Command: mux-no-such-command-4d2c --port 7", error, "full command shown");
                MuxAssert.Contains("Resolved: not found (searched", error, "PATH search reported");
                MuxAssert.Contains("Hint: install the command", error, "install hint");
            });
            Add("StdioExitingServerReportsStderr", "A stdio server that exits during the handshake is reported with its resolved path", async (CancellationToken ct) =>
            {
                string? shell = McpConnectionDiagnostics.ResolveCommand(OperatingSystem.IsWindows() ? "cmd" : "sh");
                MuxAssert.IsNotNull(shell, "a shell is available");
                List<string> args = OperatingSystem.IsWindows()
                    ? new List<string> { "/c", "echo boom-from-server 1>&2 & exit 3" }
                    : new List<string> { "-c", "echo boom-from-server 1>&2; exit 3" };
                McpServerConfig config = new McpServerConfig { Name = "quits", Transport = McpTransportTypeEnum.Stdio, Command = OperatingSystem.IsWindows() ? "cmd" : "sh", Args = args };
                McpConnectionResult result = await ConnectAsync(config, ct).ConfigureAwait(false);
                string error = result.Error ?? string.Empty;
                MuxAssert.IsFalse(result.Connected, "not connected");
                MuxAssert.Contains("Failed to start MCP server 'quits'", FirstLine(error), "summary");
                MuxAssert.Contains("Resolved: " + shell, error, "resolved path shown");
                MuxAssert.DoesNotContain("was not found", error, "the command was found");
                MuxAssert.Contains("Server stderr", error, "stderr section present");
            });

            // --- classifier and helpers ---
            Add("ClassifySocketErrors", "Socket errors map to specific causes", (CancellationToken ct) =>
            {
                Uri uri = new Uri("http://example.test:9000/mcp");
                MuxAssert.Contains("connection refused by example.test:9000", McpConnectionDiagnostics.Classify(Wrap(new SocketException((int)SocketError.ConnectionRefused)), uri, TimeSpan.FromSeconds(5)), "refused");
                MuxAssert.Contains("host not found", McpConnectionDiagnostics.Classify(Wrap(new SocketException((int)SocketError.HostNotFound)), uri, TimeSpan.FromSeconds(5)), "dns");
                MuxAssert.Contains("timed out", McpConnectionDiagnostics.Classify(Wrap(new SocketException((int)SocketError.TimedOut)), uri, TimeSpan.FromSeconds(5)), "timed out");
                MuxAssert.Contains("unreachable", McpConnectionDiagnostics.Classify(Wrap(new SocketException((int)SocketError.HostUnreachable)), uri, TimeSpan.FromSeconds(5)), "unreachable");
                MuxAssert.Contains("was reset", McpConnectionDiagnostics.Classify(Wrap(new SocketException((int)SocketError.ConnectionReset)), uri, TimeSpan.FromSeconds(5)), "reset");
                return Task.CompletedTask;
            });
            Add("ClassifyHttpTlsAndTimeout", "TLS, HTTP request errors, and timeouts map to specific causes", (CancellationToken ct) =>
            {
                Uri uri = new Uri("https://secure.test/mcp");
                MuxAssert.Contains("TLS handshake with secure.test:443 failed: bad cert", McpConnectionDiagnostics.Classify(new HttpRequestException("ssl", new AuthenticationException("bad cert")), uri, TimeSpan.FromSeconds(5)), "tls");
                MuxAssert.Contains("host not found", McpConnectionDiagnostics.Classify(new HttpRequestException(HttpRequestError.NameResolutionError, "dns"), uri, TimeSpan.FromSeconds(5)), "dns via HttpRequestError");
                MuxAssert.Contains("closed the connection", McpConnectionDiagnostics.Classify(new HttpRequestException(HttpRequestError.ResponseEnded, "ended"), uri, TimeSpan.FromSeconds(5)), "response ended");
                MuxAssert.AreEqual("no response from secure.test:443 within 7 s", McpConnectionDiagnostics.Classify(new TaskCanceledException(), uri, TimeSpan.FromSeconds(7)), "timeout");
                MuxAssert.Contains("InvalidOperationException: odd", McpConnectionDiagnostics.Classify(new InvalidOperationException("odd"), uri, TimeSpan.FromSeconds(5)), "unknown falls back to type and message");
                MuxAssert.Throws<ArgumentNullException>(() => McpConnectionDiagnostics.Classify(null!, uri, TimeSpan.Zero), "null exception rejected");
                return Task.CompletedTask;
            });
            Add("MaskHidesSecretsOnly", "Mask replaces secrets of four or more characters and leaves other text alone", (CancellationToken ct) =>
            {
                MuxAssert.AreEqual("token <redacted> and <redacted>", McpConnectionDiagnostics.Mask("token abcd1234 and abcd1234", new[] { "abcd1234" }), "every occurrence");
                MuxAssert.AreEqual("abc stays", McpConnectionDiagnostics.Mask("abc stays", new[] { "abc" }), "short values are not masked");
                MuxAssert.AreEqual("plain", McpConnectionDiagnostics.Mask("plain", null), "no secrets");
                MuxAssert.AreEqual(string.Empty, McpConnectionDiagnostics.Mask(null!, new[] { "abcd" }), "null text");
                return Task.CompletedTask;
            });
            Add("ResolveCommandFindsAndMisses", "ResolveCommand finds executables on PATH and by path, and misses unknown ones", (CancellationToken ct) =>
            {
                string? shell = McpConnectionDiagnostics.ResolveCommand(OperatingSystem.IsWindows() ? "cmd" : "sh");
                MuxAssert.IsNotNull(shell, "shell found on PATH");
                MuxAssert.IsTrue(File.Exists(shell!), "resolved path exists");
                MuxAssert.AreEqual(shell, McpConnectionDiagnostics.ResolveCommand(shell), "a full path resolves to itself");
                MuxAssert.IsNull(McpConnectionDiagnostics.ResolveCommand("mux-no-such-command-4d2c"), "unknown name");
                MuxAssert.IsNull(McpConnectionDiagnostics.ResolveCommand(Path.Combine(Path.GetTempPath(), "mux-missing-" + Guid.NewGuid().ToString("N"))), "missing path");
                MuxAssert.IsNull(McpConnectionDiagnostics.ResolveCommand("  "), "blank");
                return Task.CompletedTask;
            });
            Add("RpcUrlAndExceptionShape", "BuildRpcUrl joins without doubled slashes, and the exception message is summary plus details", (CancellationToken ct) =>
            {
                MuxAssert.AreEqual("http://h:1/mcp", McpConnectionDiagnostics.BuildRpcUrl("http://h:1/", "/mcp"), "trailing slash trimmed");
                MuxAssert.AreEqual("http://h:1/mcp", McpConnectionDiagnostics.BuildRpcUrl("http://h:1", "/mcp"), "plain join");
                McpConnectionException withDetails = new McpConnectionException("Summary line", "Detail A\nDetail B");
                MuxAssert.AreEqual("Summary line", FirstLine(withDetails.Message), "first line is the summary");
                MuxAssert.Contains("Detail B", withDetails.Message, "details follow");
                MuxAssert.AreEqual("Summary line", new McpConnectionException("Summary line", "  ").Message, "blank details add nothing");
                MuxAssert.IsTrue(withDetails is InvalidOperationException, "still an InvalidOperationException for existing callers");
                return Task.CompletedTask;
            });

            // --- REST route ---
            Add("ValidateRouteReportsDetails", "POST /v1.0/api/mcp-servers/validate returns the cause and details, with auth and input checks", async (CancellationToken ct) =>
            {
                await RunRouteCaseAsync(ct).ConfigureAwait(false);
            });

            return new TestSuiteDescriptor(SuiteId, "MCP connection diagnostics", cases);
        }

        #endregion

        #region Private-Methods

        private static McpServerConfig Http(string name, string url, McpAuthConfig? auth)
        {
            return new McpServerConfig { Name = name, Transport = McpTransportTypeEnum.Http, Url = url, McpPath = "/mcp", Auth = auth ?? new McpAuthConfig { Type = McpAuthTypeEnum.None } };
        }

        private static McpAuthConfig Bearer()
        {
            return new McpAuthConfig { Type = McpAuthTypeEnum.Bearer, BearerToken = Secret };
        }

        private static async Task<McpConnectionResult> ConnectAsync(McpServerConfig config, CancellationToken ct)
        {
            using (McpToolManager manager = new McpToolManager(new List<McpServerConfig> { config }))
            using (CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                cts.CancelAfter(TimeSpan.FromSeconds(60));
                await manager.InitializeAsync(cts.Token).ConfigureAwait(false);
                List<McpConnectionResult> results = manager.GetConnectionResults();
                MuxAssert.AreEqual(1, results.Count, "one result");
                return results[0];
            }
        }

        private static Exception Wrap(SocketException socket)
        {
            return new HttpRequestException("connect failed", socket);
        }

        private static string FirstLine(string text)
        {
            int newline = text.IndexOfAny(new[] { '\r', '\n' });
            return newline < 0 ? text : text.Substring(0, newline);
        }

        private static async Task RunRouteCaseAsync(CancellationToken ct)
        {
            string tempSessions = Path.Combine(Path.GetTempPath(), "mux-test-" + Guid.NewGuid().ToString("N"));
            string tempConfig = Path.Combine(Path.GetTempPath(), "mux-cfg-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempConfig);
            string? originalConfig = Environment.GetEnvironmentVariable("MUX_CONFIG_DIR");
            Environment.SetEnvironmentVariable("MUX_CONFIG_DIR", tempConfig);
            int downPort = StubHttpServer.FreeLoopbackPort();
            MuxServer? server = null;
            try
            {
                Mux.Core.Settings.SettingsLoader.SaveMcpServers(new List<McpServerConfig> { Http("down", "http://127.0.0.1:" + downPort, Bearer()) });

                RestServerSettings rest = new RestServerSettings { Hostname = "127.0.0.1", ApiKey = "testkey123" };
                List<EndpointConfig> endpoints = new List<EndpointConfig>();
                int port = 0;
                for (int attempt = 0; attempt < 10 && server == null; attempt++)
                {
                    port = StubHttpServer.FreeLoopbackPort();
                    rest.Port = port;
                    MuxServer candidate = new MuxServer(rest, "9.9.9-test", new SessionStore(tempSessions), () => endpoints, null);
                    try { candidate.Start(); server = candidate; }
                    catch (Exception) { candidate.Dispose(); Thread.Sleep(50); }
                }

                MuxAssert.IsNotNull(server, "server bound");
                string url = "http://127.0.0.1:" + port + "/v1.0/api/mcp-servers/validate";
                using (HttpClient http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) })
                {
                    for (int attempt = 0; attempt < 20; attempt++)
                    {
                        try { await http.GetAsync("http://127.0.0.1:" + port + "/v1.0/api/health", ct).ConfigureAwait(false); break; }
                        catch (Exception) { await Task.Delay(100, ct).ConfigureAwait(false); }
                    }

                    using (HttpResponseMessage unauthorized = await http.PostAsync(url, new StringContent("{\"Name\":\"down\"}", Encoding.UTF8, "application/json"), ct).ConfigureAwait(false))
                    {
                        MuxAssert.AreEqual(401, (int)unauthorized.StatusCode, "no key is rejected");
                    }

                    HttpReply refused = await PostAsync(http, url, "{\"Name\":\"down\"}", ct).ConfigureAwait(false);
                    MuxAssert.AreEqual(200, refused.StatusCode, "saved server validated");
                    MuxAssert.Contains("\"Connected\":false", refused.Body, "not connected");
                    MuxAssert.Contains("connection refused by 127.0.0.1:" + downPort, refused.Body, "cause in Error");
                    MuxAssert.Contains("\"Details\":\"Request: POST http://127.0.0.1:" + downPort + "/mcp", refused.Body, "details carried separately");
                    MuxAssert.DoesNotContain(Secret, refused.Body, "secret never returned");

                    HttpReply inline = await PostAsync(http, url, "{\"Server\":{\"Name\":\"inline\",\"Transport\":\"stdio\",\"Command\":\"mux-no-such-command-4d2c\"}}", ct).ConfigureAwait(false);
                    MuxAssert.AreEqual(200, inline.StatusCode, "inline definition validated");
                    MuxAssert.Contains("was not found on PATH", inline.Body, "stdio cause");

                    MuxAssert.AreEqual(404, (await PostAsync(http, url, "{\"Name\":\"missing\"}", ct).ConfigureAwait(false)).StatusCode, "unknown name is 404");
                    MuxAssert.AreEqual(400, (await PostAsync(http, url, "{}", ct).ConfigureAwait(false)).StatusCode, "empty body is 400");
                    MuxAssert.AreEqual(400, (await PostAsync(http, url, "not json", ct).ConfigureAwait(false)).StatusCode, "invalid JSON is 400");
                    MuxAssert.AreEqual(400, (await PostAsync(http, url, "{\"Server\":{\"Name\":\" \"}}", ct).ConfigureAwait(false)).StatusCode, "nameless server is 400");
                }
            }
            finally
            {
                server?.Stop();
                server?.Dispose();
                Environment.SetEnvironmentVariable("MUX_CONFIG_DIR", originalConfig);
                try { if (Directory.Exists(tempSessions)) Directory.Delete(tempSessions, true); } catch (Exception) { }
                try { if (Directory.Exists(tempConfig)) Directory.Delete(tempConfig, true); } catch (Exception) { }
            }
        }

        private static async Task<HttpReply> PostAsync(HttpClient http, string url, string json, CancellationToken ct)
        {
            using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, url) { Content = new StringContent(json, Encoding.UTF8, "application/json") })
            {
                request.Headers.Add("Authorization", "Bearer testkey123");
                return await HttpReply.SendAsync(http, request, ct).ConfigureAwait(false);
            }
        }

        #endregion
    }
}
