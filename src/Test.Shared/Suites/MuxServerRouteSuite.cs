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

                            // Endpoints without a key -> 401.
                            HttpResponseMessage noKey = http.GetAsync(baseUrl + "/v1.0/api/endpoints").GetAwaiter().GetResult();
                            MuxAssert.AreEqual(401, (int)noKey.StatusCode, "EndpointsNoKeyStatus");

                            // Endpoints with the key -> 200 and the endpoint name.
                            using HttpRequestMessage keyed = new HttpRequestMessage(HttpMethod.Get, baseUrl + "/v1.0/api/endpoints");
                            keyed.Headers.Add("X-Api-Key", "testkey123");
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
                            settingsReq.Headers.Add("X-Api-Key", "testkey123");
                            HttpResponseMessage settingsRes = http.SendAsync(settingsReq).GetAwaiter().GetResult();
                            string settingsBody = settingsRes.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                            MuxAssert.AreEqual(200, (int)settingsRes.StatusCode, "SettingsStatus");
                            MuxAssert.Contains("DefaultApprovalPolicy", settingsBody, "SettingsBody");

                            // Chat against an unknown endpoint -> 404 (routing + parsing without a live model).
                            using HttpRequestMessage chatReq = new HttpRequestMessage(HttpMethod.Post, baseUrl + "/v1.0/api/chat");
                            chatReq.Headers.Add("X-Api-Key", "testkey123");
                            chatReq.Content = new StringContent("{\"endpoint\":\"nope\",\"messages\":[{\"role\":\"user\",\"content\":\"hi\"}]}", System.Text.Encoding.UTF8, "application/json");
                            HttpResponseMessage chatRes = http.SendAsync(chatReq).GetAwaiter().GetResult();
                            MuxAssert.AreEqual(404, (int)chatRes.StatusCode, "ChatUnknownEndpointStatus");
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
