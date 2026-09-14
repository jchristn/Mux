namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Net;
    using System.Net.Http;
    using System.Net.Sockets;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Enums;
    using Mux.Core.Models;
    using Mux.Core.Sessions;
    using Mux.Server;
    using Touchstone.Core;

    /// <summary>
    /// Touchstone suite that boots a real <see cref="MuxServer"/> on an ephemeral loopback port and exercises
    /// the health, endpoints, auth, and default-route behavior over HTTP.
    /// </summary>
    public static class MuxServerRouteSuite
    {
        /// <summary>
        /// Builds the server-route suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/>.</returns>
        public static TestSuiteDescriptor Create()
        {
            return new TestSuiteDescriptor(
                "MuxServerRoutes",
                "Live MuxServer health, endpoints, auth, and 404 behavior",
                new List<TestCaseDescriptor>
                {
                    new TestCaseDescriptor("MuxServerRoutes", "HealthEndpointsAuthAnd404", "Health, endpoints, auth gating, and 404 all behave", (CancellationToken ct) =>
                    {
                        string tempSessions = Path.Combine(Path.GetTempPath(), "mux-test-" + Guid.NewGuid().ToString("N"));

                        RestServerSettings rest = new RestServerSettings
                        {
                            Hostname = "127.0.0.1",
                            ApiKey = "testkey123"
                        };

                        List<EndpointConfig> endpoints = new List<EndpointConfig>
                        {
                            new EndpointConfig { Name = "unit-ollama", AdapterType = AdapterTypeEnum.Ollama, BaseUrl = "http://localhost:11434", Model = "gemma3:4b", IsDefault = true }
                        };

                        // Bind can lose a race between choosing a free port and the server actually claiming it
                        // (another process may grab the port in that window). Retry on a fresh port so the test
                        // is deterministic rather than flaky.
                        MuxServer? server = null;
                        int port = 0;
                        for (int bindAttempt = 0; bindAttempt < 10 && server == null; bindAttempt++)
                        {
                            port = FreeLoopbackPort();
                            rest.Port = port;
                            MuxServer candidate = new MuxServer(rest, "9.9.9-test", new SessionStore(tempSessions), () => endpoints, null);
                            try
                            {
                                candidate.Start();
                                server = candidate;
                            }
                            catch (Exception)
                            {
                                candidate.Dispose();
                                Thread.Sleep(50);
                            }
                        }

                        MuxAssert.IsNotNull(server, "server bound to a loopback port");
                        string baseUrl = "http://127.0.0.1:" + port;

                        try
                        {
                            using HttpClient http = new HttpClient();
                            http.Timeout = TimeSpan.FromSeconds(5);

                            // Health (anonymous), with a short readiness retry.
                            string healthBody = string.Empty;
                            int healthStatus = 0;
                            for (int attempt = 0; attempt < 20; attempt++)
                            {
                                try
                                {
                                    HttpResponseMessage r = http.GetAsync(baseUrl + "/v1.0/api/health").GetAwaiter().GetResult();
                                    healthStatus = (int)r.StatusCode;
                                    healthBody = r.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                                    break;
                                }
                                catch (Exception)
                                {
                                    Thread.Sleep(100);
                                }
                            }

                            MuxAssert.AreEqual(200, healthStatus, "HealthStatus");
                            MuxAssert.Contains("healthy", healthBody, "HealthBody");
                            MuxAssert.Contains("9.9.9-test", healthBody, "HealthVersion");
                            MuxAssert.Contains("ContractVersion", healthBody, "HealthContractVersion");

                            // Endpoints without a key -> 401.
                            HttpResponseMessage noKey = http.GetAsync(baseUrl + "/v1.0/api/endpoints").GetAwaiter().GetResult();
                            MuxAssert.AreEqual(401, (int)noKey.StatusCode, "EndpointsNoKeyStatus");

                            // Endpoints with the key -> 200 and the endpoint name.
                            using HttpRequestMessage keyed = new HttpRequestMessage(HttpMethod.Get, baseUrl + "/v1.0/api/endpoints");
                            keyed.Headers.Add("Authorization", "Bearer testkey123");
                            HttpResponseMessage withKey = http.SendAsync(keyed).GetAwaiter().GetResult();
                            string endpointsBody = withKey.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                            MuxAssert.AreEqual(200, (int)withKey.StatusCode, "EndpointsKeyStatus");
                            MuxAssert.Contains("unit-ollama", endpointsBody, "EndpointsBody");

                            // Unknown route -> 404.
                            HttpResponseMessage notFound = http.GetAsync(baseUrl + "/no-such-route").GetAwaiter().GetResult();
                            MuxAssert.AreEqual(404, (int)notFound.StatusCode, "NotFoundStatus");

                            // Dashboard is served as HTML (anonymous).
                            HttpResponseMessage dash = http.GetAsync(baseUrl + "/dashboard").GetAwaiter().GetResult();
                            string dashBody = dash.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                            MuxAssert.AreEqual(200, (int)dash.StatusCode, "DashboardStatus");
                            MuxAssert.Contains("mux dashboard", dashBody, "DashboardBody");

                            // Settings (authed) returns the masked DTO.
                            using HttpRequestMessage settingsReq = new HttpRequestMessage(HttpMethod.Get, baseUrl + "/v1.0/api/settings");
                            settingsReq.Headers.Add("Authorization", "Bearer testkey123");
                            HttpResponseMessage settingsRes = http.SendAsync(settingsReq).GetAwaiter().GetResult();
                            string settingsBody = settingsRes.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                            MuxAssert.AreEqual(200, (int)settingsRes.StatusCode, "SettingsStatus");
                            MuxAssert.Contains("DefaultApprovalPolicy", settingsBody, "SettingsBody");

                            // Chat against an unknown endpoint -> 404 (routing + parsing without a live model).
                            using HttpRequestMessage chatReq = new HttpRequestMessage(HttpMethod.Post, baseUrl + "/v1.0/api/chat");
                            chatReq.Headers.Add("Authorization", "Bearer testkey123");
                            chatReq.Content = new StringContent("{\"endpoint\":\"nope\",\"messages\":[{\"role\":\"user\",\"content\":\"hi\"}]}", System.Text.Encoding.UTF8, "application/json");
                            HttpResponseMessage chatRes = http.SendAsync(chatReq).GetAwaiter().GetResult();
                            MuxAssert.AreEqual(404, (int)chatRes.StatusCode, "ChatUnknownEndpointStatus");

                            // Upsert a session carrying an assistant tool call + a tool result, then read it back
                            // via detail: the tool-call structure must survive the web round trip (not flatten to
                            // role+content), so a tool-using transcript is portable through the web surface.
                            string upsertBody =
                                "{\"id\":\"toolsess\",\"endpointName\":\"unit-ollama\",\"model\":\"gemma3:4b\",\"messages\":[" +
                                "{\"role\":\"user\",\"content\":\"list files\"}," +
                                "{\"role\":\"assistant\",\"content\":\"\",\"toolCalls\":[{\"id\":\"tc1\",\"name\":\"glob\",\"arguments\":\"{\\\"pattern\\\":\\\"*\\\"}\"}]}," +
                                "{\"role\":\"tool\",\"content\":\"a.txt\",\"toolCallId\":\"tc1\"}]}";
                            using HttpRequestMessage putReq = new HttpRequestMessage(HttpMethod.Put, baseUrl + "/v1.0/api/sessions");
                            putReq.Headers.Add("Authorization", "Bearer testkey123");
                            putReq.Content = new StringContent(upsertBody, System.Text.Encoding.UTF8, "application/json");
                            HttpResponseMessage putRes = http.SendAsync(putReq).GetAwaiter().GetResult();
                            MuxAssert.AreEqual(200, (int)putRes.StatusCode, "SessionUpsertStatus");

                            using HttpRequestMessage detailReq = new HttpRequestMessage(HttpMethod.Get, baseUrl + "/v1.0/api/sessions/detail?id=toolsess");
                            detailReq.Headers.Add("Authorization", "Bearer testkey123");
                            HttpResponseMessage detailRes = http.SendAsync(detailReq).GetAwaiter().GetResult();
                            string detailBody = detailRes.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                            MuxAssert.AreEqual(200, (int)detailRes.StatusCode, "SessionDetailStatus");
                            MuxAssert.Contains("tc1", detailBody, "tool call id round-trips");
                            MuxAssert.Contains("glob", detailBody, "tool name round-trips");
                            MuxAssert.Contains("ToolCallId", detailBody, "tool result id round-trips");

                            // A streamed chat with a non-existent working directory is rejected up front (400),
                            // before switching to SSE — so an editor client learns the path is bad cleanly.
                            string missingDir = Path.Combine(Path.GetTempPath(), "mux-missing-" + Guid.NewGuid().ToString("N"));
                            string wdBody = "{\"endpoint\":\"unit-ollama\",\"workingDirectory\":" + System.Text.Json.JsonSerializer.Serialize(missingDir) + ",\"messages\":[{\"role\":\"user\",\"content\":\"hi\"}]}";
                            using HttpRequestMessage wdReq = new HttpRequestMessage(HttpMethod.Post, baseUrl + "/v1.0/api/chat/stream");
                            wdReq.Headers.Add("Authorization", "Bearer testkey123");
                            wdReq.Content = new StringContent(wdBody, System.Text.Encoding.UTF8, "application/json");
                            HttpResponseMessage wdRes = http.SendAsync(wdReq).GetAwaiter().GetResult();
                            MuxAssert.AreEqual(400, (int)wdRes.StatusCode, "StreamMissingWorkingDirStatus");

                            // Checkpoints for a non-git directory report unavailable rather than erroring, and
                            // undo there restores nothing — the editor undo path degrades cleanly.
                            string plainDir = Path.Combine(Path.GetTempPath(), "mux-plain-" + Guid.NewGuid().ToString("N"));
                            Directory.CreateDirectory(plainDir);
                            try
                            {
                                using HttpRequestMessage cpReq = new HttpRequestMessage(HttpMethod.Get, baseUrl + "/v1.0/api/checkpoints?workingDirectory=" + Uri.EscapeDataString(plainDir));
                                cpReq.Headers.Add("Authorization", "Bearer testkey123");
                                HttpResponseMessage cpRes = http.SendAsync(cpReq).GetAwaiter().GetResult();
                                string cpBody = cpRes.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                                MuxAssert.AreEqual(200, (int)cpRes.StatusCode, "CheckpointStatusStatus");
                                MuxAssert.Contains("\"IsRepository\":false", cpBody, "non-repo reports not a repository");

                                using HttpRequestMessage undoReq = new HttpRequestMessage(HttpMethod.Post, baseUrl + "/v1.0/api/checkpoints/undo");
                                undoReq.Headers.Add("Authorization", "Bearer testkey123");
                                undoReq.Content = new StringContent("{\"workingDirectory\":" + System.Text.Json.JsonSerializer.Serialize(plainDir) + "}", System.Text.Encoding.UTF8, "application/json");
                                HttpResponseMessage undoRes = http.SendAsync(undoReq).GetAwaiter().GetResult();
                                string undoBody = undoRes.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                                MuxAssert.AreEqual(200, (int)undoRes.StatusCode, "CheckpointUndoStatus");
                                MuxAssert.Contains("\"Restored\":false", undoBody, "nothing to undo in a non-repo");
                            }
                            finally
                            {
                                try { if (Directory.Exists(plainDir)) Directory.Delete(plainDir, true); } catch (Exception) { }
                            }
                        }
                        finally
                        {
                            server?.Stop();
                            server?.Dispose();
                            try { if (Directory.Exists(tempSessions)) Directory.Delete(tempSessions, true); } catch (Exception) { }
                        }

                        return Task.CompletedTask;
                    })
                });
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
