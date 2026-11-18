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
    using Mux.Core.Cloud;
    using Mux.Core.Models;
    using Mux.Core.Runs;
    using Mux.Core.Sessions;
    using Mux.Core.Settings;
    using Mux.Server;
    using Mux.Server.Cloud;
    using Mux.Server.Models;
    using MuxCloud.Protocol;
    using Test.Shared.Support;
    using Touchstone.Core;

    /// <summary>
    /// Mux Cloud on a real hub: the cloud settings block round-trips and stays masked, the tunnel host connects from
    /// settings and follows sign-in and sign-out, only one process per machine runs a tunnel, the relayed dashboard and
    /// WebSocket work through the tunnel, remote approval honors the owner's switch, and status pushes fire.
    /// </summary>
    public static class CloudHostSuite
    {
        private const string Suite = "CloudHost";
        private const string Key = "hostkey123";
        private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

        /// <summary>
        /// Builds the suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/>.</returns>
        public static TestSuiteDescriptor Create()
        {
            return new TestSuiteDescriptor(Suite, "Mux Cloud tunnel host on a live hub", new List<TestCaseDescriptor>
            {
                new TestCaseDescriptor(Suite, "SettingsRoundTrip", "The cloud block survives load and save, unchanged files are not rewritten, and env overrides apply", async (CancellationToken ct) =>
                {
                    await WithConfigAsync(async (string config) =>
                    {
                        MuxSettings settings = new MuxSettings();
                        settings.Cloud.Enabled = true;
                        settings.Cloud.ServerUrl = "https://cloud.example.test/";
                        settings.Cloud.MachineId = "mch_round";
                        settings.Cloud.MachineSecret = "mcs_secret";
                        settings.Cloud.MachineName = "round-trip";
                        settings.Cloud.AccountEmail = "dev@example.test";
                        settings.Cloud.AllowRemoteToolApproval = false;
                        settings.Cloud.HeartbeatSeconds = 1;
                        settings.Cloud.ReconnectMaxDelaySeconds = 99999;
                        settings.Cloud.AllowInvalidCertificates = true;
                        SettingsLoader.SaveSettings(settings);
                        SettingsLoader.UpdateCloud(c => { c.Enabled = true; c.MachineId = "mch_round"; c.MachineSecret = "mcs_secret"; c.AccountEmail = "dev@example.test"; });

                        // A save from a stale copy (no credential, connection off) must not sign the machine out.
                        MuxSettings stale = new MuxSettings();
                        stale.Cloud.ServerUrl = "https://cloud.example.test/";
                        stale.Cloud.MachineName = "round-trip";
                        stale.Cloud.AllowRemoteToolApproval = false;
                        stale.Cloud.HeartbeatSeconds = 1;
                        stale.Cloud.ReconnectMaxDelaySeconds = 99999;
                        stale.Cloud.AllowInvalidCertificates = true;
                        SettingsLoader.SaveSettings(stale);
                        string path = Path.Combine(config, "settings.json");
                        DateTime written = File.GetLastWriteTimeUtc(path);
                        await Task.Delay(50, ct).ConfigureAwait(false);

                        MuxSettings loaded = SettingsLoader.LoadSettings();
                        CloudSettings c = loaded.Cloud;
                        MuxAssert.IsTrue(c.Enabled, "enabled");
                        MuxAssert.AreEqual("https://cloud.example.test", c.ServerUrl, "url trimmed");
                        MuxAssert.AreEqual("mch_round", c.MachineId, "machine id");
                        MuxAssert.AreEqual("mcs_secret", c.MachineSecret, "secret");
                        MuxAssert.AreEqual("round-trip", c.MachineName, "name");
                        MuxAssert.AreEqual("dev@example.test", c.AccountEmail, "email");
                        MuxAssert.IsFalse(c.AllowRemoteToolApproval, "remote approval off");
                        MuxAssert.AreEqual(5, c.HeartbeatSeconds, "heartbeat clamped");
                        MuxAssert.AreEqual(3600, c.ReconnectMaxDelaySeconds, "reconnect clamped");
                        MuxAssert.IsTrue(c.AllowInvalidCertificates, "invalid certs");
                        MuxAssert.AreEqual(written, File.GetLastWriteTimeUtc(path), "an already-normalized settings.json is not rewritten on load");

                        string? original = Environment.GetEnvironmentVariable("MUX_CLOUD_ALLOW_REMOTE_TOOL_APPROVAL");
                        Environment.SetEnvironmentVariable("MUX_CLOUD_ALLOW_REMOTE_TOOL_APPROVAL", "true");
                        try
                        {
                            MuxAssert.IsTrue(SettingsLoader.LoadSettings().Cloud.AllowRemoteToolApproval, "env override applies");
                        }
                        finally
                        {
                            Environment.SetEnvironmentVariable("MUX_CLOUD_ALLOW_REMOTE_TOOL_APPROVAL", original);
                        }

                        MuxAssert.AreEqual(TunnelProtocol.DefaultServerUrl, new MuxSettings().Cloud.ServerUrl, "default server is app.usemux.ai");
                        MuxAssert.IsTrue(new MuxSettings().Cloud.AllowRemoteToolApproval, "remote approval defaults on");
                    }).ConfigureAwait(false);
                }),
                new TestCaseDescriptor(Suite, "SettingsDtoMasksSecret", "The settings DTO never carries the machine secret, and applying it never changes the credential", (CancellationToken ct) =>
                {
                    MuxSettings settings = new MuxSettings();
                    settings.Cloud.MachineId = "mch_mask";
                    settings.Cloud.MachineSecret = "mcs_do_not_leak";
                    settings.Cloud.AccountEmail = "dev@example.test";
                    SettingsDto dto = SettingsDto.FromSettings(settings);
                    string json = JsonSerializer.Serialize(dto);
                    MuxAssert.DoesNotContain("mcs_do_not_leak", json, "secret never serialized");
                    MuxAssert.IsTrue(dto.Cloud!.MachineSecretSet, "reports that a secret is set");
                    dto.Cloud.AllowRemoteToolApproval = false;
                    dto.Cloud.MachineId = "mch_other";
                    dto.Cloud.MachineName = "edited";
                    dto.ApplyTo(settings);
                    MuxAssert.IsFalse(settings.Cloud.AllowRemoteToolApproval, "the remote approval switch is editable");
                    MuxAssert.AreEqual("edited", settings.Cloud.MachineName, "name editable");
                    MuxAssert.AreEqual("mch_mask", settings.Cloud.MachineId, "machine id unchanged");
                    MuxAssert.AreEqual("mcs_do_not_leak", settings.Cloud.MachineSecret, "secret unchanged");
                    return Task.CompletedTask;
                }),
                new TestCaseDescriptor(Suite, "HostRelaysDashboardAndWebSocket", "The host connects from settings; the relayed dashboard has no key and the machine prefix, and the relayed WebSocket reaches the hub", async (CancellationToken ct) =>
                {
                    using FakeCloudServer cloud = new FakeCloudServer();
                    await WithConfigAsync(async (string config) =>
                    {
                        WriteEnrolled(cloud, true);
                        await WithHubAsync(config, async (MuxServer server, CloudTunnelHost host, int port) =>
                        {
                            await WaitForAsync(() => host.Status.State == CloudStateEnum.Connected, "host connected").ConfigureAwait(false);
                            MuxAssert.IsTrue(server.CloudPolicy.CloudConnected, "policy sees the connection");

                            await cloud.SendAsync(TunnelProtocol.CreateHttpRequest("str_dash", new HttpRequestPayload { Method = "GET", Path = "/dashboard" })).ConfigureAwait(false);
                            string page = await BodyAsync(cloud, "str_dash").ConfigureAwait(false);
                            MuxAssert.Contains("var API_KEY=\"\";", page, "no local key in the relayed page");
                            MuxAssert.DoesNotContain(Key, page, "key never leaves the machine");
                            MuxAssert.Contains("var BASE=\"/m/" + cloud.MachineId + "\";", page, "page routes through the machine prefix");

                            await cloud.SendAsync(TunnelProtocol.CreateHttpRequest("str_health", new HttpRequestPayload { Method = "GET", Path = "/v1.0/api/cloud/status" })).ConfigureAwait(false);
                            string status = await BodyAsync(cloud, "str_health").ConfigureAwait(false);
                            MuxAssert.Contains("\"StateName\":\"Connected\"", status, "relayed REST call authenticated with the local key");

                            await cloud.SendAsync(TunnelProtocol.CreateWsOpen("chn_hub", new WsOpenPayload { Path = "/v1.0/ws", Query = "apiKey=" })).ConfigureAwait(false);
                            await cloud.NextAsync(e => e.Id == "chn_hub" && e.Type == TunnelMessageTypes.WsOpened, Wait).ConfigureAwait(false);
                            TunnelEnvelope connected = await cloud.NextAsync(e => e.Id == "chn_hub" && e.Type == TunnelMessageTypes.WsFrame, Wait).ConfigureAwait(false);
                            MuxAssert.Contains("server.connected", connected.WsFrame!.Text, "the hub's own WebSocket answers through the tunnel");
                        }).ConfigureAwait(false);
                    }).ConfigureAwait(false);
                }),
                new TestCaseDescriptor(Suite, "HostFollowsSignOutAndIn", "Signing out from any surface disconnects the running host; signing back in reconnects it", async (CancellationToken ct) =>
                {
                    using FakeCloudServer cloud = new FakeCloudServer();
                    await WithConfigAsync(async (string config) =>
                    {
                        WriteEnrolled(cloud, true);
                        await WithHubAsync(config, async (MuxServer server, CloudTunnelHost host, int port) =>
                        {
                            await WaitForAsync(() => host.Status.State == CloudStateEnum.Connected, "connected").ConfigureAwait(false);
                            CloudAccountService.Logout();
                            await WaitForAsync(() => host.Status.State == CloudStateEnum.Disabled, "disconnected after sign-out").ConfigureAwait(false);
                            MuxAssert.IsFalse(server.CloudPolicy.CloudConnected, "policy cleared");
                            await CloudAccountService.LoginAsync("dev@example.test", "pw", cloud.Url, "relogged", ct).ConfigureAwait(false);
                            await WaitForAsync(() => host.Status.State == CloudStateEnum.Connected, "reconnected after sign-in").ConfigureAwait(false);
                            MuxAssert.AreEqual("fake-name", SettingsLoader.LoadSettings().Cloud.MachineName, "name from the enrollment response");
                            MuxAssert.DoesNotContain("\"pw\"", File.ReadAllText(Path.Combine(config, "settings.json")), "the password is never stored");
                        }).ConfigureAwait(false);
                    }).ConfigureAwait(false);
                }),
                new TestCaseDescriptor(Suite, "OneTunnelPerMachine", "A second hub sharing the config directory does not open a second tunnel", async (CancellationToken ct) =>
                {
                    using FakeCloudServer cloud = new FakeCloudServer();
                    await WithConfigAsync(async (string config) =>
                    {
                        WriteEnrolled(cloud, true);
                        await WithHubAsync(config, async (MuxServer first, CloudTunnelHost firstHost, int firstPort) =>
                        {
                            await WaitForAsync(() => firstHost.Status.State == CloudStateEnum.Connected, "first connected").ConfigureAwait(false);
                            await WithHubAsync(config, async (MuxServer second, CloudTunnelHost secondHost, int secondPort) =>
                            {
                                await Task.Delay(500, ct).ConfigureAwait(false);
                                MuxAssert.AreEqual(CloudStateEnum.Disabled, secondHost.Status.State, "second host stays off");
                                MuxAssert.Contains("Another mux process", secondHost.Status.Reason, "explains why");
                                MuxAssert.AreEqual(CloudReasonCodes.AnotherProcess, secondHost.Status.ReasonCode, "stable reason code");
                                MuxAssert.AreEqual(CloudStateKeys.Off, secondHost.Status.StateKey, "shown as Off");
                                MuxAssert.AreEqual(1, cloud.Hellos.Count, "only one tunnel");
                            }).ConfigureAwait(false);
                        }).ConfigureAwait(false);
                    }).ConfigureAwait(false);
                }),
                new TestCaseDescriptor(Suite, "RemoteApprovalSwitch", "With cloud.allowRemoteToolApproval false, relayed approvals are refused over REST and WebSocket; local ones are not", async (CancellationToken ct) =>
                {
                    await WithConfigAsync(async (string config) =>
                    {
                        await WithHubAsync(config, async (MuxServer server, CloudTunnelHost host, int port) =>
                        {
                            server.CloudPolicy.AllowRemoteToolApproval = false;
                            RunHandle run = server.Runs.GetOrCreate("appr-run", "appr-sess", "ep", "m");
                            TaskCompletionSource<string> pending = run.RegisterApproval("call-x", "write_file", "{}", 0);
                            using HttpClient http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
                            string body = "{\"RunId\":\"appr-run\",\"ToolCallId\":\"call-x\",\"Decision\":\"y\"}";

                            using (HttpRequestMessage relayed = Post(port, "/v1.0/api/chat/approve", body))
                            {
                                relayed.Headers.Add(TunnelProtocol.RelayHeader, TunnelProtocol.RelayHeaderValue);
                                HttpResponseMessage res = await http.SendAsync(relayed, ct).ConfigureAwait(false);
                                MuxAssert.AreEqual(403, (int)res.StatusCode, "relayed approval refused");
                            }

                            using (ClientWebSocket ws = new ClientWebSocket())
                            {
                                ws.Options.SetRequestHeader("Authorization", "Bearer " + Key);
                                ws.Options.SetRequestHeader(TunnelProtocol.RelayHeader, TunnelProtocol.RelayHeaderValue);
                                await ws.ConnectAsync(new Uri("ws://127.0.0.1:" + port + "/v1.0/ws"), ct).ConfigureAwait(false);
                                await ReceiveAsync(ws, ct).ConfigureAwait(false);
                                await SendAsync(ws, "{\"action\":\"approve\",\"runId\":\"appr-run\",\"toolCallId\":\"call-x\",\"decision\":\"y\"}", ct).ConfigureAwait(false);
                                MuxAssert.Contains("forbidden", await ReceiveAsync(ws, ct).ConfigureAwait(false), "relayed WebSocket approval refused");
                            }

                            MuxAssert.IsFalse(pending.Task.IsCompleted, "still pending");
                            using (HttpRequestMessage local = Post(port, "/v1.0/api/chat/approve", body))
                            {
                                HttpResponseMessage res = await http.SendAsync(local, ct).ConfigureAwait(false);
                                MuxAssert.AreEqual(200, (int)res.StatusCode, "local approval accepted");
                            }

                            MuxAssert.AreEqual("y", await pending.Task.ConfigureAwait(false), "decided locally");
                        }).ConfigureAwait(false);
                    }).ConfigureAwait(false);
                }),
                new TestCaseDescriptor(Suite, "LoginRouteAndStatusPush", "POST /cloud/login enrolls and connects; an approval wait pushes machine.status approval_requested", async (CancellationToken ct) =>
                {
                    using FakeCloudServer cloud = new FakeCloudServer();
                    await WithConfigAsync(async (string config) =>
                    {
                        await WithHubAsync(config, async (MuxServer server, CloudTunnelHost host, int port) =>
                        {
                            MuxAssert.AreEqual(CloudStateEnum.Disabled, host.Status.State, "starts signed out");
                            using HttpClient http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                            using (HttpRequestMessage login = Post(port, "/v1.0/api/cloud/login", "{\"Email\":\"dev@example.test\",\"Password\":\"pw\",\"ServerUrl\":\"" + cloud.Url + "\"}"))
                            {
                                HttpResponseMessage res = await http.SendAsync(login, ct).ConfigureAwait(false);
                                string text = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                                MuxAssert.AreEqual(200, (int)res.StatusCode, "login ok: " + text);
                            }

                            await WaitForAsync(() => host.Status.State == CloudStateEnum.Connected, "connected after login").ConfigureAwait(false);
                            MuxSettings hubSettings = SettingsLoader.LoadSettings();
                            hubSettings.Rest.Port = port;
                            hubSettings.Rest.ApiKey = Key;
                            CloudStatus? viaHub = await CloudHubStatusClient.GetAsync(hubSettings, ct).ConfigureAwait(false);
                            MuxAssert.IsNotNull(viaHub, "the CLI, terminal, and desktop read the hub's status");
                            MuxAssert.AreEqual(CloudStateEnum.Connected, viaHub!.State, "state name parses");
                            using (HttpRequestMessage get = new HttpRequestMessage(HttpMethod.Get, "http://127.0.0.1:" + port + "/v1.0/api/settings"))
                            {
                                get.Headers.Add("Authorization", "Bearer " + Key);
                                string settingsJson = await (await http.SendAsync(get, ct).ConfigureAwait(false)).Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                                MuxAssert.DoesNotContain(cloud.MachineSecret, settingsJson, "settings route masks the machine secret");
                                MuxAssert.Contains("\"MachineSecretSet\":true", settingsJson, "reports the secret is set");
                            }

                            RunHandle run = server.Runs.GetOrCreate("push-run", "push-sess", "ep", "m");
                            run.RegisterApproval("call-p", "run_process", "{}", 0);
                            TunnelEnvelope push = await cloud.NextAsync(e => e.Type == TunnelMessageTypes.MachineStatus && e.MachineStatus!.Kind == MachineStatusKinds.ApprovalRequested, Wait).ConfigureAwait(false);
                            MuxAssert.AreEqual("push-run", push.MachineStatus!.RunId, "run id");
                            MuxAssert.AreEqual("push-sess", push.MachineStatus.SessionId, "session id");
                            MuxAssert.AreEqual(1, push.MachineStatus.AwaitingApproval, "one run waiting");

                            using (HttpRequestMessage logout = Post(port, "/v1.0/api/cloud/logout", "{}"))
                            {
                                HttpResponseMessage res = await http.SendAsync(logout, ct).ConfigureAwait(false);
                                MuxAssert.AreEqual(200, (int)res.StatusCode, "logout ok");
                            }

                            await WaitForAsync(() => host.Status.State == CloudStateEnum.Disabled, "disconnected after logout").ConfigureAwait(false);
                        }).ConfigureAwait(false);
                    }).ConfigureAwait(false);
                })
            });
        }

        private static void WriteEnrolled(FakeCloudServer cloud, bool enabled)
        {
            MuxSettings settings = SettingsLoader.LoadSettings();
            settings.Rest.ApiKey = Key;
            SettingsLoader.SaveSettings(settings);
            SettingsLoader.UpdateCloud(c =>
            {
                c.Enabled = enabled;
                c.ServerUrl = cloud.Url;
                c.MachineId = cloud.MachineId;
                c.MachineSecret = cloud.MachineSecret;
                c.MachineName = "host-test";
            });
        }

        private static async Task WithConfigAsync(Func<string, Task> body)
        {
            string dir = Path.Combine(Path.GetTempPath(), "mux-cloudhost-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            string? original = Environment.GetEnvironmentVariable("MUX_CONFIG_DIR");
            Environment.SetEnvironmentVariable("MUX_CONFIG_DIR", dir);
            try
            {
                await body(dir).ConfigureAwait(false);
            }
            finally
            {
                Environment.SetEnvironmentVariable("MUX_CONFIG_DIR", original);
                try { Directory.Delete(dir, true); } catch (Exception) { }
            }
        }

        private static async Task WithHubAsync(string config, Func<MuxServer, CloudTunnelHost, int, Task> body)
        {
            RestServerSettings rest = new RestServerSettings { Hostname = "127.0.0.1", ApiKey = Key };
            List<EndpointConfig> endpoints = new List<EndpointConfig>();
            MuxServer? server = null;
            int port = 0;
            for (int attempt = 0; attempt < 10 && server == null; attempt++)
            {
                port = FreePort();
                rest.Port = port;
                MuxServer candidate = new MuxServer(rest, "9.9.9-test", new SessionStore(Path.Combine(config, "sessions")), () => endpoints, null);
                try { candidate.Start(); server = candidate; }
                catch (Exception) { candidate.Dispose(); Thread.Sleep(50); }
            }

            MuxAssert.IsNotNull(server, "hub bound");
            CloudTunnelHost host = new CloudTunnelHost(server!, "2.0.0-test", config) { ReconnectBaseDelaySeconds = 1 };
            try
            {
                host.Start();
                await body(server!, host, port).ConfigureAwait(false);
            }
            finally
            {
                host.Dispose();
                server!.Stop();
                server.Dispose();
            }
        }

        private static async Task WaitForAsync(Func<bool> condition, string what)
        {
            DateTime deadline = DateTime.UtcNow + Wait;
            while (DateTime.UtcNow < deadline)
            {
                if (condition()) return;
                await Task.Delay(50).ConfigureAwait(false);
            }

            MuxAssert.Fail("timed out waiting: " + what);
        }

        private static async Task<string> BodyAsync(FakeCloudServer cloud, string streamId)
        {
            List<KeyValuePair<DateTime, TunnelEnvelope>> frames = await cloud.CollectStreamAsync(streamId, Wait).ConfigureAwait(false);
            StringBuilder sb = new StringBuilder();
            foreach (KeyValuePair<DateTime, TunnelEnvelope> f in frames)
            {
                if (f.Value.Type == TunnelMessageTypes.HttpResponseChunk) sb.Append(Encoding.UTF8.GetString(Convert.FromBase64String(f.Value.HttpChunk!.Data)));
            }

            return sb.ToString();
        }

        private static HttpRequestMessage Post(int port, string path, string json)
        {
            HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Post, "http://127.0.0.1:" + port + path) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
            req.Headers.Add("Authorization", "Bearer " + Key);
            return req;
        }

        private static async Task SendAsync(ClientWebSocket ws, string text, CancellationToken ct)
        {
            await ws.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(text)), WebSocketMessageType.Text, true, ct).ConfigureAwait(false);
        }

        private static async Task<string> ReceiveAsync(ClientWebSocket ws, CancellationToken ct)
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

        private static int FreePort()
        {
            TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }
    }
}
