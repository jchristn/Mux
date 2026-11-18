namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Net;
    using System.Net.Http;
    using System.Net.Sockets;
    using System.Net.WebSockets;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Models;
    using Mux.Core.Runs;
    using Mux.Core.Sessions;
    using Mux.Server;
    using Mux.Server.Models;
    using Mux.Server.Routes;
    using Touchstone.Core;

    /// <summary>
    /// The groundwork Mux Cloud depends on: approval frames on the run stream and WebSocket, run-addressed
    /// approvals, relay-aware dashboard rendering (no local key, a routable base path), and working-directory
    /// suggestions and fields on sessions.
    /// </summary>
    public static class CloudGroundworkSuite
    {
        private const string Suite = "CloudGroundwork";
        private const string ApiKey = "groundkey123";

        /// <summary>
        /// Builds the suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/>.</returns>
        public static TestSuiteDescriptor Create()
        {
            return new TestSuiteDescriptor(Suite, "Mux Cloud groundwork: approval frames, relay rendering, workspaces", new List<TestCaseDescriptor>
            {
                new TestCaseDescriptor(Suite, "ApprovalFramesFanOut", "Registering and resolving an approval fans out approval_requested and approval_resolved, and late subscribers replay the request", async (CancellationToken ct) =>
                {
                    using RunHandle handle = new RunHandle("run-a", "sess-a", "ep", "m", CancellationToken.None);
                    RunSubscription early = handle.Subscribe();
                    TaskCompletionSource<string> pending = handle.RegisterApproval("call-1", "write_file", "{\"path\":\"a.txt\"}", 15);
                    MuxAssert.AreEqual(1, handle.PendingApprovalCount, "one pending approval");
                    MuxAssert.AreEqual(RunStatusEnum.AwaitingApproval, handle.Status, "awaiting approval");

                    string requested = await ReadFrameAsync(early, ct).ConfigureAwait(false);
                    using (JsonDocument doc = JsonDocument.Parse(requested))
                    {
                        JsonElement r = doc.RootElement;
                        MuxAssert.AreEqual("approval_requested", r.GetProperty("eventType").GetString(), "event type");
                        MuxAssert.AreEqual("run-a", r.GetProperty("runId").GetString(), "run id");
                        MuxAssert.AreEqual("sess-a", r.GetProperty("sessionId").GetString(), "session id");
                        MuxAssert.AreEqual("call-1", r.GetProperty("toolCallId").GetString(), "tool call id");
                        MuxAssert.AreEqual("write_file", r.GetProperty("toolName").GetString(), "tool name");
                        MuxAssert.AreEqual("{\"path\":\"a.txt\"}", r.GetProperty("arguments").GetString(), "arguments");
                        MuxAssert.AreEqual(15, r.GetProperty("toolAutoApproveSeconds").GetInt32(), "countdown");
                        MuxAssert.IsTrue(r.TryGetProperty("contractVersion", out _), "carries the contract version");
                    }

                    RunSubscription late = handle.Subscribe();
                    MuxAssert.IsTrue(System.Linq.Enumerable.Any(late.Replay, f => f.Contains("approval_requested", StringComparison.Ordinal)), "a subscriber that attaches while the run waits replays the prompt");

                    MuxAssert.IsTrue(handle.ResolveApproval("call-1", "y"), "resolves");
                    MuxAssert.AreEqual("y", await pending.Task.ConfigureAwait(false), "decision reaches the run");
                    MuxAssert.AreEqual(RunStatusEnum.Running, handle.Status, "running again");
                    MuxAssert.AreEqual(0, handle.PendingApprovalCount, "none pending");
                    string resolved = await ReadFrameAsync(early, ct).ConfigureAwait(false);
                    MuxAssert.Contains("\"eventType\":\"approval_resolved\"", resolved, "resolved frame");
                    MuxAssert.Contains("\"decision\":\"y\"", resolved, "carries the decision");
                    MuxAssert.IsFalse(handle.ResolveApproval("call-1", "y"), "a second resolve is a no-op");
                }),
                new TestCaseDescriptor(Suite, "ApprovalRequestedEvent", "ApprovalRequested fires with the tool call and tool name", (CancellationToken ct) =>
                {
                    using RunHandle handle = new RunHandle("run-e", "sess-e", "ep", "m", CancellationToken.None);
                    string seen = string.Empty;
                    handle.ApprovalRequested += (h, id, name) => { seen = h.RunId + "|" + id + "|" + name; };
                    handle.RegisterApproval("call-9", "run_process", "{}", 0);
                    MuxAssert.AreEqual("run-e|call-9|run_process", seen, "event payload");
                    return Task.CompletedTask;
                }),
                new TestCaseDescriptor(Suite, "WebSocketApproveHonorsRunId", "A WebSocket approve frame resolves the run it names, not the most recently attached run", async (CancellationToken ct) =>
                {
                    using RunRegistry runs = new RunRegistry();
                    RunHandle runA = runs.GetOrCreate("ws-run-a", "ws-sess-a", "ep", "m");
                    RunHandle runB = runs.GetOrCreate("ws-run-b", "ws-sess-b", "ep", "m");
                    TaskCompletionSource<string> pendingA = runA.RegisterApproval("call-a", "write_file", "{}", 0);
                    TaskCompletionSource<string> pendingB = runB.RegisterApproval("call-a", "write_file", "{}", 0);
                    await WithServerAsync(runs, async (int port, HttpClient http, SessionStore store) =>
                    {
                        using ClientWebSocket ws = new ClientWebSocket();
                        await ws.ConnectAsync(new Uri("ws://127.0.0.1:" + port + "/v1.0/ws?apiKey=" + ApiKey), ct).ConfigureAwait(false);
                        await ReceiveTextAsync(ws, ct).ConfigureAwait(false); // server.connected
                        await SendTextAsync(ws, "{\"action\":\"subscribe\",\"sessionId\":\"ws-sess-b\"}", ct).ConfigureAwait(false);
                        string replayed = await ReceiveTextAsync(ws, ct).ConfigureAwait(false);
                        MuxAssert.Contains("approval_requested", replayed, "the subscribed run replays its pending prompt over the WebSocket");
                        await SendTextAsync(ws, "{\"action\":\"approve\",\"runId\":\"ws-run-a\",\"toolCallId\":\"call-a\",\"decision\":\"always\"}", ct).ConfigureAwait(false);
                        string decision = await pendingA.Task.WaitAsync(TimeSpan.FromSeconds(5), ct).ConfigureAwait(false);
                        MuxAssert.AreEqual("always", decision, "run A (named) resolved");
                        MuxAssert.IsFalse(pendingB.Task.IsCompleted, "run B (attached) untouched");
                    }, ct).ConfigureAwait(false);
                }),
                new TestCaseDescriptor(Suite, "SanitizeBasePath", "Only safe absolute paths survive as a relay base path", (CancellationToken ct) =>
                {
                    MuxAssert.AreEqual("/m/mch_01ABC-def", RelayContext.SanitizeBasePath("/m/mch_01ABC-def/"), "trailing slash trimmed");
                    MuxAssert.AreEqual(string.Empty, RelayContext.SanitizeBasePath(null), "null");
                    MuxAssert.AreEqual(string.Empty, RelayContext.SanitizeBasePath("m/x"), "relative");
                    MuxAssert.AreEqual(string.Empty, RelayContext.SanitizeBasePath("/m/x\";alert(1)//"), "script injection");
                    MuxAssert.AreEqual(string.Empty, RelayContext.SanitizeBasePath("//evil.test/x"), "protocol-relative");
                    MuxAssert.AreEqual(string.Empty, RelayContext.SanitizeBasePath("/" + new string('a', 200)), "too long");
                    return Task.CompletedTask;
                }),
                new TestCaseDescriptor(Suite, "DashboardRelayRendering", "The dashboard served through the relay carries no API key and routes every URL under the base path", async (CancellationToken ct) =>
                {
                    using RunRegistry runs = new RunRegistry();
                    await WithServerAsync(runs, async (int port, HttpClient http, SessionStore store) =>
                    {
                        string local = await http.GetStringAsync("http://127.0.0.1:" + port + "/dashboard", ct).ConfigureAwait(false);
                        MuxAssert.Contains("var API_KEY=\"" + ApiKey + "\";", local, "local page carries the key");
                        MuxAssert.Contains("var BASE=\"\";", local, "local page has no base path");
                        MuxAssert.DoesNotContain("__MUX_BASE_PATH__", local, "placeholder replaced");
                        MuxAssert.Contains("fetch(BASE+path", local, "api() prefixes the base path");
                        MuxAssert.Contains("fetch(BASE+\"/v1.0/api/chat/stream\"", local, "streaming chat prefixes the base path");
                        MuxAssert.Contains("location.host+BASE+\"/v1.0/ws\"", local, "WebSocket URLs route under the base path");
                        MuxAssert.DoesNotContain("location.host+\"/v1.0/ws\"", local, "no WebSocket URL bypasses the base path");
                        MuxAssert.Contains("openSessionFromLocation(location.search,location.hash);", local, "the page opens ?session= or #session= deep links");
                        MuxAssert.Contains("id=\"cloud_signup\"", local, "the sign-in form links to signup");
                        MuxAssert.AreEqual(11, local.Split("\"cloud.noAccount\":").Length - 1, "every locale asks whether there is an account");
                        MuxAssert.AreEqual(11, local.Split("\"cloud.createAccountAt\":").Length - 1, "every locale says where to create one");

                        using HttpRequestMessage relayed = new HttpRequestMessage(HttpMethod.Get, "http://127.0.0.1:" + port + "/dashboard");
                        relayed.Headers.Add("X-Mux-Relay", "cloud");
                        relayed.Headers.Add("X-Mux-Base-Path", "/m/mch_test1");
                        string page = await (await http.SendAsync(relayed, ct).ConfigureAwait(false)).Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                        MuxAssert.Contains("var API_KEY=\"\";", page, "relayed page carries no key");
                        MuxAssert.DoesNotContain(ApiKey, page, "the local key never leaves the machine in the page");
                        MuxAssert.Contains("var BASE=\"/m/mch_test1\";", page, "relayed page routes under the machine prefix");

                        using HttpRequestMessage hostile = new HttpRequestMessage(HttpMethod.Get, "http://127.0.0.1:" + port + "/dashboard");
                        hostile.Headers.Add("X-Mux-Relay", "cloud");
                        hostile.Headers.TryAddWithoutValidation("X-Mux-Base-Path", "/m/x\"</script><script>alert(1)</script>");
                        string hostilePage = await (await http.SendAsync(hostile, ct).ConfigureAwait(false)).Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                        MuxAssert.Contains("var BASE=\"\";", hostilePage, "an unsafe base path renders empty");
                        MuxAssert.DoesNotContain("alert(1)", hostilePage, "nothing from the header reaches the page");
                    }, ct).ConfigureAwait(false);
                }),
                new TestCaseDescriptor(Suite, "WorkspaceSuggestions", "Workspace suggestions are distinct existing session folders, most recent first, then home", (CancellationToken ct) =>
                {
                    string root = Path.Combine(Path.GetTempPath(), "mux-ws-" + Guid.NewGuid().ToString("N"));
                    string a = Path.Combine(root, "alpha");
                    string b = Path.Combine(root, "beta");
                    string home = Path.Combine(root, "home");
                    Directory.CreateDirectory(a);
                    Directory.CreateDirectory(b);
                    Directory.CreateDirectory(home);
                    try
                    {
                        DateTime now = DateTime.UtcNow;
                        List<SessionSnapshot> sessions = new List<SessionSnapshot>
                        {
                            new SessionSnapshot { Id = "1", WorkingDirectory = a, UpdatedUtc = now.AddHours(-3) },
                            new SessionSnapshot { Id = "2", WorkingDirectory = b, UpdatedUtc = now.AddHours(-1) },
                            new SessionSnapshot { Id = "3", WorkingDirectory = a + Path.DirectorySeparatorChar, UpdatedUtc = now.AddHours(-2) },
                            new SessionSnapshot { Id = "4", WorkingDirectory = Path.Combine(root, "gone"), UpdatedUtc = now },
                            new SessionSnapshot { Id = "5", WorkingDirectory = string.Empty, UpdatedUtc = now }
                        };
                        List<WorkspaceDto> items = WorkspaceRoutes.Build(sessions, home, 50);
                        MuxAssert.AreEqual(3, items.Count, "two recent folders plus home");
                        MuxAssert.AreEqual(b, items[0].Path, "most recent first");
                        MuxAssert.AreEqual(a, items[1].Path, "trailing separator deduplicated");
                        MuxAssert.AreEqual(2, items[1].SessionCount, "counts sessions per folder");
                        MuxAssert.AreEqual("alpha", items[1].Name, "folder name");
                        MuxAssert.AreEqual("home", items[2].Source, "home last");
                        MuxAssert.IsNull(items[2].LastUsedUtc, "home has no last-used time");
                        MuxAssert.AreEqual(1, WorkspaceRoutes.Build(sessions, a, 50).FindAll(w => w.Path == a).Count, "home already listed is not repeated");
                        MuxAssert.AreEqual(2, WorkspaceRoutes.Build(sessions, home, 1).Count, "the recent cap leaves room for home");
                    }
                    finally
                    {
                        try { Directory.Delete(root, true); } catch (Exception) { }
                    }

                    return Task.CompletedTask;
                }),
                new TestCaseDescriptor(Suite, "WorkingDirectoryOnSessions", "GET /workspaces and the session list and detail expose the session working directory", async (CancellationToken ct) =>
                {
                    string dir = Path.Combine(Path.GetTempPath(), "mux-wd-" + Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(dir);
                    try
                    {
                        using RunRegistry runs = new RunRegistry();
                        await WithServerAsync(runs, async (int port, HttpClient http, SessionStore store) =>
                        {
                            await store.SaveAsync(new SessionSnapshot { Id = "wd-1", Title = "t", WorkingDirectory = dir, CreatedUtc = DateTime.UtcNow, UpdatedUtc = DateTime.UtcNow }, ct).ConfigureAwait(false);
                            string workspaces = await GetAuthedAsync(http, "http://127.0.0.1:" + port + "/v1.0/api/workspaces", ct).ConfigureAwait(false);
                            MuxAssert.Contains(JsonSerializer.Serialize(dir).Trim('"'), workspaces, "workspaces lists the session folder");
                            string list = await GetAuthedAsync(http, "http://127.0.0.1:" + port + "/v1.0/api/sessions", ct).ConfigureAwait(false);
                            MuxAssert.Contains("\"WorkingDirectory\"", list, "summary carries the field");
                            MuxAssert.Contains(JsonSerializer.Serialize(dir).Trim('"'), list, "summary carries the folder");
                            string detail = await GetAuthedAsync(http, "http://127.0.0.1:" + port + "/v1.0/api/sessions/detail?id=wd-1", ct).ConfigureAwait(false);
                            MuxAssert.Contains(JsonSerializer.Serialize(dir).Trim('"'), detail, "detail carries the folder");
                        }, ct).ConfigureAwait(false);
                    }
                    finally
                    {
                        try { Directory.Delete(dir, true); } catch (Exception) { }
                    }
                })
            });
        }

        private static async Task WithServerAsync(RunRegistry runs, Func<int, HttpClient, SessionStore, Task> body, CancellationToken ct)
        {
            string temp = Path.Combine(Path.GetTempPath(), "mux-cloudgw-" + Guid.NewGuid().ToString("N"));
            string config = Path.Combine(temp, "config");
            Directory.CreateDirectory(config);
            string? originalConfigDir = Environment.GetEnvironmentVariable("MUX_CONFIG_DIR");
            Environment.SetEnvironmentVariable("MUX_CONFIG_DIR", config);
            RestServerSettings rest = new RestServerSettings { Hostname = "127.0.0.1", ApiKey = ApiKey };
            List<EndpointConfig> endpoints = new List<EndpointConfig>();
            SessionStore store = new SessionStore(Path.Combine(temp, "sessions"));
            MuxServer? server = null;
            int port = 0;
            try
            {
                for (int attempt = 0; attempt < 10 && server == null; attempt++)
                {
                    port = FreeLoopbackPort();
                    rest.Port = port;
                    MuxServer candidate = new MuxServer(rest, "9.9.9-test", store, () => endpoints, null, null, null, false, runs);
                    try { candidate.Start(); server = candidate; }
                    catch (Exception) { candidate.Dispose(); Thread.Sleep(50); }
                }

                MuxAssert.IsNotNull(server, "server bound to a loopback port");
                using HttpClient http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                for (int i = 0; i < 30; i++)
                {
                    try { await http.GetAsync("http://127.0.0.1:" + port + "/v1.0/api/health", ct).ConfigureAwait(false); break; }
                    catch (HttpRequestException) { await Task.Delay(100, ct).ConfigureAwait(false); }
                }

                await body(port, http, store).ConfigureAwait(false);
            }
            finally
            {
                server?.Stop();
                server?.Dispose();
                Environment.SetEnvironmentVariable("MUX_CONFIG_DIR", originalConfigDir);
                try { Directory.Delete(temp, true); } catch (Exception) { }
            }
        }

        private static async Task<string> ReadFrameAsync(RunSubscription subscription, CancellationToken ct)
        {
            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            return await subscription.Reader.ReadAsync(timeout.Token).ConfigureAwait(false);
        }

        private static async Task<string> GetAuthedAsync(HttpClient http, string url, CancellationToken ct)
        {
            using HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Add("Authorization", "Bearer " + ApiKey);
            HttpResponseMessage res = await http.SendAsync(req, ct).ConfigureAwait(false);
            return await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        }

        private static async Task SendTextAsync(ClientWebSocket ws, string text, CancellationToken ct)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct).ConfigureAwait(false);
        }

        private static async Task<string> ReceiveTextAsync(ClientWebSocket ws, CancellationToken ct)
        {
            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            byte[] buffer = new byte[16384];
            StringBuilder sb = new StringBuilder();
            while (true)
            {
                WebSocketReceiveResult result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), timeout.Token).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close) return sb.ToString();
                sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                if (result.EndOfMessage) return sb.ToString();
            }
        }

        private static int FreeLoopbackPort()
        {
            TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }
    }
}
