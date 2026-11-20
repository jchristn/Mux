namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net.Http;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Cloud;
    using MuxCloud.Protocol;
    using Test.Shared.Support;
    using Touchstone.Core;

    /// <summary>
    /// The Mux Cloud tunnel client against an in-process fake cloud and a scripted local hub: handshake and proof,
    /// terminal and retryable closes, heartbeat loss, concurrency, streaming, cancellation, WebSocket channels, header
    /// rewriting, the stream cap, and enrollment.
    /// </summary>
    public static class CloudTunnelSuite
    {
        private const string Suite = "CloudTunnel";
        private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

        /// <summary>
        /// Builds the suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/>.</returns>
        public static TestSuiteDescriptor Create()
        {
            return new TestSuiteDescriptor(Suite, "Mux Cloud tunnel client", new List<TestCaseDescriptor>
            {
                new TestCaseDescriptor(Suite, "HandshakeAndProof", "The client proves the machine secret, reports Connected after the welcome, and adopts the cloud's machine name", async (CancellationToken ct) =>
                {
                    using FakeCloudServer cloud = new FakeCloudServer { WelcomeMachineName = "renamed-in-cloud" };
                    using LocalHubStub hub = new LocalHubStub();
                    using CloudTunnelClient client = NewClient(cloud, hub, null);
                    client.Start();
                    await WaitForStateAsync(client, CloudStateEnum.Connected).ConfigureAwait(false);
                    MuxAssert.IsTrue(cloud.Hellos.TryPeek(out TunnelHelloPayload? hello) && hello != null, "the fake received a hello");
                    MuxAssert.AreEqual(cloud.MachineId, hello!.MachineId, "machine id");
                    MuxAssert.AreEqual("test-machine", hello.MachineName, "machine name");
                    MuxAssert.AreEqual("2.0.0-test", hello.MuxVersion, "mux version");
                    MuxAssert.AreEqual(TunnelCapabilities.All.Count, hello.Capabilities.Count, "advertises every capability");
                    MuxAssert.AreEqual("renamed-in-cloud", client.Status.MachineName, "adopts the cloud's name");
                    MuxAssert.IsTrue(client.Status.ConnectionId.StartsWith("mcn_", StringComparison.Ordinal), "connection id");
                }),
                new TestCaseDescriptor(Suite, "WrongSecretSignsOut", "A wrong secret is rejected with 4401; the client shows SignedOut and stops reconnecting", async (CancellationToken ct) =>
                {
                    using FakeCloudServer cloud = new FakeCloudServer();
                    using LocalHubStub hub = new LocalHubStub();
                    using CloudTunnelClient client = NewClient(cloud, hub, o => o.MachineSecret = TunnelProof.CreateMachineSecret());
                    client.Start();
                    await WaitForStateAsync(client, CloudStateEnum.SignedOut).ConfigureAwait(false);
                    MuxAssert.Contains("credential", client.Status.Reason, "explains the rejection");
                    MuxAssert.AreEqual(CloudReasonCodes.CredentialRejected, client.Status.ReasonCode, "stable reason code");
                    MuxAssert.AreEqual(CloudStateKeys.SignedOut, client.Status.StateKey, "shown as Signed out");
                    await Task.Delay(2500, ct).ConfigureAwait(false);
                    MuxAssert.AreEqual(1, cloud.Connections, "no reconnect after a terminal close");
                }),
                new TestCaseDescriptor(Suite, "RevokedStops", "A 4403 close shows Revoked and stops reconnecting", async (CancellationToken ct) =>
                {
                    using FakeCloudServer cloud = new FakeCloudServer { RejectHelloWith = TunnelCloseCodes.Revoked };
                    using LocalHubStub hub = new LocalHubStub();
                    using CloudTunnelClient client = NewClient(cloud, hub, null);
                    client.Start();
                    await WaitForStateAsync(client, CloudStateEnum.Revoked).ConfigureAwait(false);
                    MuxAssert.AreEqual(CloudReasonCodes.Removed, client.Status.ReasonCode, "stable reason code");
                    MuxAssert.AreEqual(CloudStateKeys.Removed, client.Status.StateKey, "shown as Removed");
                    await Task.Delay(2500, ct).ConfigureAwait(false);
                    MuxAssert.AreEqual(1, cloud.Connections, "no reconnect after revocation");
                }),
                new TestCaseDescriptor(Suite, "ClockSkewRetries", "A 4400 close that names the clock is shown as clock skew and the client keeps retrying", async (CancellationToken ct) =>
                {
                    using FakeCloudServer cloud = new FakeCloudServer
                    {
                        RejectHelloWith = TunnelCloseCodes.BadHandshake,
                        RejectHelloReason = "timestamp is more than 120 seconds from server time; check this machine's clock"
                    };
                    using LocalHubStub hub = new LocalHubStub();
                    using CloudTunnelClient client = NewClient(cloud, hub, null);
                    client.Start();
                    await WaitForStateAsync(client, CloudStateEnum.Reconnecting).ConfigureAwait(false);
                    DateTime deadline = DateTime.UtcNow + Wait;
                    while (cloud.Connections < 2 && DateTime.UtcNow < deadline) await Task.Delay(100, ct).ConfigureAwait(false);
                    MuxAssert.IsTrue(cloud.Connections >= 2, "retries after a skewed-clock rejection");
                    MuxAssert.AreEqual(CloudReasonCodes.ClockSkew, client.Status.ReasonCode, "reason code names the clock");
                    MuxAssert.Contains("clock is off by more than 2 minutes", CloudStatusText.ReasonLine(client.Status), "plain-language reason");
                }),
                new TestCaseDescriptor(Suite, "ReconnectsAfterDrop", "When the cloud drops the tunnel, the client shows Reconnecting with a reason, then connects again", async (CancellationToken ct) =>
                {
                    using FakeCloudServer cloud = new FakeCloudServer();
                    using LocalHubStub hub = new LocalHubStub();
                    using CloudTunnelClient client = NewClient(cloud, hub, null);
                    List<CloudStateEnum> states = new List<CloudStateEnum>();
                    client.StatusChanged += s => { lock (states) { states.Add(s.State); } };
                    client.Start();
                    await WaitForStateAsync(client, CloudStateEnum.Connected).ConfigureAwait(false);
                    await cloud.CloseCurrentAsync(TunnelCloseCodes.Draining, "deploy").ConfigureAwait(false);
                    MuxAssert.IsTrue(await cloud.WaitForHellosAsync(2, Wait).ConfigureAwait(false), "a second hello after the drop");
                    await WaitForStateAsync(client, CloudStateEnum.Connected).ConfigureAwait(false);
                    lock (states) { MuxAssert.IsTrue(states.Contains(CloudStateEnum.Reconnecting), "passed through Reconnecting"); }
                }),
                new TestCaseDescriptor(Suite, "ReplacedWaits", "A 4409 replaced close is not terminal: the client waits the maximum delay before retrying", async (CancellationToken ct) =>
                {
                    using FakeCloudServer cloud = new FakeCloudServer();
                    using LocalHubStub hub = new LocalHubStub();
                    using CloudTunnelClient client = NewClient(cloud, hub, o => o.ReconnectMaxDelaySeconds = 30);
                    client.Start();
                    await WaitForStateAsync(client, CloudStateEnum.Connected).ConfigureAwait(false);
                    await cloud.CloseCurrentAsync(TunnelCloseCodes.Replaced, "newer connection").ConfigureAwait(false);
                    await WaitForStateAsync(client, CloudStateEnum.Reconnecting).ConfigureAwait(false);
                    await WaitUntilAsync(() => client.Status.RetryInSeconds.HasValue).ConfigureAwait(false);
                    MuxAssert.Contains("replaced", client.Status.Reason, "explains the replacement");
                    MuxAssert.IsTrue(client.Status.RetryInSeconds >= 29, "waits the maximum delay, got " + client.Status.RetryInSeconds);
                }),
                new TestCaseDescriptor(Suite, "HeartbeatLossReconnects", "Three unanswered pings drop the tunnel and the client reconnects", async (CancellationToken ct) =>
                {
                    using FakeCloudServer cloud = new FakeCloudServer { RespondToPings = false };
                    using LocalHubStub hub = new LocalHubStub();
                    using CloudTunnelClient client = NewClient(cloud, hub, o => o.HeartbeatIntervalOverride = TimeSpan.FromMilliseconds(250));
                    client.Start();
                    await WaitForStateAsync(client, CloudStateEnum.Connected).ConfigureAwait(false);
                    MuxAssert.IsTrue(await cloud.WaitForHellosAsync(2, TimeSpan.FromSeconds(15)).ConfigureAwait(false), "reconnected after heartbeat loss");
                }),
                new TestCaseDescriptor(Suite, "AnswersCloudPings", "A ping from the cloud is answered with a pong carrying the same nonce", async (CancellationToken ct) =>
                {
                    using FakeCloudServer cloud = new FakeCloudServer();
                    using LocalHubStub hub = new LocalHubStub();
                    using CloudTunnelClient client = NewClient(cloud, hub, null);
                    client.Start();
                    await WaitForStateAsync(client, CloudStateEnum.Connected).ConfigureAwait(false);
                    TunnelEnvelope ping = TunnelProtocol.CreatePing();
                    await cloud.SendAsync(ping).ConfigureAwait(false);
                    TunnelEnvelope pong = await cloud.NextAsync(e => e.Type == TunnelMessageTypes.Pong, Wait).ConfigureAwait(false);
                    MuxAssert.AreEqual(ping.Ping!.Nonce, pong.Ping!.Nonce, "nonce echoed");
                }),
                new TestCaseDescriptor(Suite, "SlowRequestDoesNotBlock", "A slow relayed request does not block a fast one or the heartbeat", async (CancellationToken ct) =>
                {
                    using FakeCloudServer cloud = new FakeCloudServer();
                    using LocalHubStub hub = new LocalHubStub { SlowDelay = TimeSpan.FromSeconds(3) };
                    using CloudTunnelClient client = NewClient(cloud, hub, null);
                    client.Start();
                    await WaitForStateAsync(client, CloudStateEnum.Connected).ConfigureAwait(false);
                    await cloud.SendAsync(TunnelProtocol.CreateHttpRequest("str_slow", new HttpRequestPayload { Method = "GET", Path = "/slow" })).ConfigureAwait(false);
                    await cloud.SendAsync(TunnelProtocol.CreateHttpRequest("str_fast", new HttpRequestPayload { Method = "GET", Path = "/fast" })).ConfigureAwait(false);
                    DateTime sent = DateTime.UtcNow;
                    List<KeyValuePair<DateTime, TunnelEnvelope>> fast = await cloud.CollectStreamAsync("str_fast", Wait).ConfigureAwait(false);
                    MuxAssert.IsTrue((fast.Last().Key - sent).TotalSeconds < 2, "the fast request finished while the slow one waited");
                    MuxAssert.AreEqual("fast", Body(fast), "fast body");
                    TunnelEnvelope ping = TunnelProtocol.CreatePing();
                    await cloud.SendAsync(ping).ConfigureAwait(false);
                    await cloud.NextAsync(e => e.Type == TunnelMessageTypes.Pong && e.Ping!.Nonce == ping.Ping!.Nonce, TimeSpan.FromSeconds(1)).ConfigureAwait(false);
                    List<KeyValuePair<DateTime, TunnelEnvelope>> slow = await cloud.CollectStreamAsync("str_slow", Wait).ConfigureAwait(false);
                    MuxAssert.AreEqual("slow", Body(slow), "slow body arrives later");
                    MuxAssert.AreEqual(200, slow[0].Value.HttpResponseStart!.StatusCode, "status");
                }),
                new TestCaseDescriptor(Suite, "SsePassThrough", "Server-sent events stream through as chunks as they are produced, not buffered to the end", async (CancellationToken ct) =>
                {
                    using FakeCloudServer cloud = new FakeCloudServer();
                    using LocalHubStub hub = new LocalHubStub();
                    using CloudTunnelClient client = NewClient(cloud, hub, null);
                    client.Start();
                    await WaitForStateAsync(client, CloudStateEnum.Connected).ConfigureAwait(false);
                    await cloud.SendAsync(TunnelProtocol.CreateHttpRequest("str_sse", new HttpRequestPayload { Method = "GET", Path = "/sse" })).ConfigureAwait(false);
                    List<KeyValuePair<DateTime, TunnelEnvelope>> frames = await cloud.CollectStreamAsync("str_sse", Wait).ConfigureAwait(false);
                    MuxAssert.AreEqual(TunnelMessageTypes.HttpResponseStart, frames[0].Value.Type, "starts with headers");
                    MuxAssert.Contains("text/event-stream", frames[0].Value.HttpResponseStart!.ContentType, "content type passes through");
                    List<KeyValuePair<DateTime, TunnelEnvelope>> chunks = frames.Where(f => f.Value.Type == TunnelMessageTypes.HttpResponseChunk).ToList();
                    int firstWithOne = chunks.FindIndex(c => Encoding.UTF8.GetString(Convert.FromBase64String(c.Value.HttpChunk!.Data)).Contains("one", StringComparison.Ordinal));
                    int firstWithTwo = chunks.FindIndex(c => Encoding.UTF8.GetString(Convert.FromBase64String(c.Value.HttpChunk!.Data)).Contains("two", StringComparison.Ordinal));
                    MuxAssert.IsTrue(firstWithOne >= 0 && firstWithTwo > firstWithOne, "events arrive in separate chunks, in order");
                    MuxAssert.IsTrue((chunks[firstWithTwo].Key - chunks[firstWithOne].Key).TotalMilliseconds > 400, "the first event was forwarded before the second was produced");
                    MuxAssert.AreEqual(TunnelMessageTypes.HttpResponseEnd, frames.Last().Value.Type, "ends");
                }),
                new TestCaseDescriptor(Suite, "CancelStopsStream", "http.cancel aborts the local request and frees the stream slot", async (CancellationToken ct) =>
                {
                    using FakeCloudServer cloud = new FakeCloudServer();
                    using LocalHubStub hub = new LocalHubStub();
                    using CloudTunnelClient client = NewClient(cloud, hub, null);
                    client.Start();
                    await WaitForStateAsync(client, CloudStateEnum.Connected).ConfigureAwait(false);
                    await cloud.SendAsync(TunnelProtocol.CreateHttpRequest("str_forever", new HttpRequestPayload { Method = "GET", Path = "/forever" })).ConfigureAwait(false);
                    await cloud.NextAsync(e => e.Id == "str_forever" && e.Type == TunnelMessageTypes.HttpResponseChunk, Wait).ConfigureAwait(false);
                    MuxAssert.AreEqual(1, client.ActiveStreams, "one stream in flight");
                    await cloud.SendAsync(TunnelProtocol.CreateHttpCancel("str_forever")).ConfigureAwait(false);
                    await WaitUntilAsync(() => client.ActiveStreams == 0).ConfigureAwait(false);
                    MuxAssert.AreEqual(0, client.ActiveStreams, "slot freed");
                }),
                new TestCaseDescriptor(Suite, "HeadersRewritten", "Forwarded requests carry the local key and relay headers; cloud credentials never reach the machine", async (CancellationToken ct) =>
                {
                    using FakeCloudServer cloud = new FakeCloudServer();
                    using LocalHubStub hub = new LocalHubStub();
                    using CloudTunnelClient client = NewClient(cloud, hub, null);
                    client.Start();
                    await WaitForStateAsync(client, CloudStateEnum.Connected).ConfigureAwait(false);
                    HttpRequestPayload request = new HttpRequestPayload { Method = "GET", Path = "/headers", Query = "a=1&b=%20x", RequesterIp = "203.0.113.9" };
                    request.Headers["Cookie"] = "cloud_session=abc";
                    request.Headers["Authorization"] = "Bearer cloud-token";
                    request.Headers["Accept"] = "application/json";
                    await cloud.SendAsync(TunnelProtocol.CreateHttpRequest("str_h", request)).ConfigureAwait(false);
                    string body = Body(await cloud.CollectStreamAsync("str_h", Wait).ConfigureAwait(false));
                    using JsonDocument doc = JsonDocument.Parse(body);
                    JsonElement r = doc.RootElement;
                    MuxAssert.AreEqual("Bearer local-key", r.GetProperty("authorization").GetString(), "local key replaces the cloud token");
                    MuxAssert.AreEqual("cloud", r.GetProperty("relay").GetString(), "relay header");
                    MuxAssert.AreEqual("/m/" + cloud.MachineId, r.GetProperty("basePath").GetString(), "base path header");
                    MuxAssert.AreEqual(string.Empty, r.GetProperty("cookie").GetString(), "cloud cookie dropped");
                    MuxAssert.AreEqual("203.0.113.9", r.GetProperty("forwardedFor").GetString(), "requester ip");
                    MuxAssert.AreEqual("application/json", r.GetProperty("accept").GetString(), "ordinary headers pass through");
                    MuxAssert.Contains("a=1&b=%20x", r.GetProperty("query").GetString(), "query passes through encoded");
                }),
                new TestCaseDescriptor(Suite, "RequestBodyRoundTrip", "A POST body and content type reach the local API, and the status code returns", async (CancellationToken ct) =>
                {
                    using FakeCloudServer cloud = new FakeCloudServer();
                    using LocalHubStub hub = new LocalHubStub();
                    using CloudTunnelClient client = NewClient(cloud, hub, null);
                    client.Start();
                    await WaitForStateAsync(client, CloudStateEnum.Connected).ConfigureAwait(false);
                    await cloud.SendAsync(TunnelProtocol.CreateHttpRequest("str_b", new HttpRequestPayload { Method = "POST", Path = "/echo-body", ContentType = "application/json", BodyBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes("{\"hi\":1}")) })).ConfigureAwait(false);
                    List<KeyValuePair<DateTime, TunnelEnvelope>> frames = await cloud.CollectStreamAsync("str_b", Wait).ConfigureAwait(false);
                    MuxAssert.AreEqual(201, frames[0].Value.HttpResponseStart!.StatusCode, "status passes through");
                    MuxAssert.AreEqual("{\"hi\":1}", Body(frames), "body round trip");
                }),
                new TestCaseDescriptor(Suite, "LoopbackFailureReported", "When the local API is unreachable, the stream fails with loopback_failed and 502", async (CancellationToken ct) =>
                {
                    using FakeCloudServer cloud = new FakeCloudServer();
                    LocalHubStub hub = new LocalHubStub();
                    hub.Dispose();
                    using CloudTunnelClient client = NewClient(cloud, hub, null);
                    client.Start();
                    await WaitForStateAsync(client, CloudStateEnum.Connected).ConfigureAwait(false);
                    await cloud.SendAsync(TunnelProtocol.CreateHttpRequest("str_dead", new HttpRequestPayload { Method = "GET", Path = "/fast" })).ConfigureAwait(false);
                    TunnelEnvelope error = await cloud.NextAsync(e => e.Id == "str_dead", Wait).ConfigureAwait(false);
                    MuxAssert.AreEqual(TunnelMessageTypes.StreamError, error.Type, "stream error");
                    MuxAssert.AreEqual(TunnelErrorCodes.LoopbackFailed, error.Error!.Code, "code");
                    MuxAssert.AreEqual(502, error.Error.StatusCode, "status");
                }),
                new TestCaseDescriptor(Suite, "StreamCapEnforced", "Requests beyond the welcome's concurrent stream cap are refused with too_many_streams", async (CancellationToken ct) =>
                {
                    using FakeCloudServer cloud = new FakeCloudServer { MaxConcurrentStreams = 1 };
                    using LocalHubStub hub = new LocalHubStub { SlowDelay = TimeSpan.FromSeconds(2) };
                    using CloudTunnelClient client = NewClient(cloud, hub, null);
                    client.Start();
                    await WaitForStateAsync(client, CloudStateEnum.Connected).ConfigureAwait(false);
                    await cloud.SendAsync(TunnelProtocol.CreateHttpRequest("str_1", new HttpRequestPayload { Method = "GET", Path = "/slow" })).ConfigureAwait(false);
                    await cloud.SendAsync(TunnelProtocol.CreateHttpRequest("str_2", new HttpRequestPayload { Method = "GET", Path = "/fast" })).ConfigureAwait(false);
                    TunnelEnvelope refused = await cloud.NextAsync(e => e.Id == "str_2", Wait).ConfigureAwait(false);
                    MuxAssert.AreEqual(TunnelMessageTypes.StreamError, refused.Type, "second request refused");
                    MuxAssert.AreEqual(TunnelErrorCodes.TooManyStreams, refused.Error!.Code, "code");
                    MuxAssert.AreEqual("slow", Body(await cloud.CollectStreamAsync("str_1", Wait).ConfigureAwait(false)), "first request completes");
                }),
                new TestCaseDescriptor(Suite, "WebSocketChannel", "A relayed WebSocket channel opens on the machine, echoes both ways with the local key, and closes", async (CancellationToken ct) =>
                {
                    using FakeCloudServer cloud = new FakeCloudServer();
                    using LocalHubStub hub = new LocalHubStub();
                    using CloudTunnelClient client = NewClient(cloud, hub, null);
                    client.Start();
                    await WaitForStateAsync(client, CloudStateEnum.Connected).ConfigureAwait(false);
                    await cloud.SendAsync(TunnelProtocol.CreateWsOpen("chn_1", new WsOpenPayload { Path = "/echo" })).ConfigureAwait(false);
                    await cloud.NextAsync(e => e.Id == "chn_1" && e.Type == TunnelMessageTypes.WsOpened, Wait).ConfigureAwait(false);
                    await cloud.SendAsync(TunnelProtocol.CreateWsFrame("chn_1", "hello")).ConfigureAwait(false);
                    TunnelEnvelope echoed = await cloud.NextAsync(e => e.Id == "chn_1" && e.Type == TunnelMessageTypes.WsFrame, Wait).ConfigureAwait(false);
                    MuxAssert.AreEqual("echo:hello|Bearer local-key", echoed.WsFrame!.Text, "frame echoed through the local key");
                    await cloud.SendAsync(TunnelProtocol.CreateWsClose("chn_1", 1000, "bye")).ConfigureAwait(false);
                    await WaitUntilAsync(() => hub.WebSocketCloses >= 1 && client.ActiveStreams == 0).ConfigureAwait(false);
                    MuxAssert.AreEqual(0, client.ActiveStreams, "channel slot freed");
                }),
                new TestCaseDescriptor(Suite, "WebSocketOpenFailure", "A channel that cannot open locally is reported as a stream error", async (CancellationToken ct) =>
                {
                    using FakeCloudServer cloud = new FakeCloudServer();
                    using LocalHubStub hub = new LocalHubStub();
                    using CloudTunnelClient client = NewClient(cloud, hub, null);
                    client.Start();
                    await WaitForStateAsync(client, CloudStateEnum.Connected).ConfigureAwait(false);
                    await cloud.SendAsync(TunnelProtocol.CreateWsOpen("chn_bad", new WsOpenPayload { Path = "/no-socket-here" })).ConfigureAwait(false);
                    TunnelEnvelope error = await cloud.NextAsync(e => e.Id == "chn_bad", Wait).ConfigureAwait(false);
                    MuxAssert.AreEqual(TunnelMessageTypes.StreamError, error.Type, "stream error");
                }),
                new TestCaseDescriptor(Suite, "MachineStatusPush", "machine.status frames reach the cloud only while connected", async (CancellationToken ct) =>
                {
                    using FakeCloudServer cloud = new FakeCloudServer();
                    using LocalHubStub hub = new LocalHubStub();
                    using CloudTunnelClient client = NewClient(cloud, hub, null);
                    MuxAssert.IsFalse(await client.SendMachineStatusAsync(new MachineStatusPayload(), ct).ConfigureAwait(false), "not sent before connecting");
                    client.Start();
                    await WaitForStateAsync(client, CloudStateEnum.Connected).ConfigureAwait(false);
                    MuxAssert.IsTrue(await client.SendMachineStatusAsync(new MachineStatusPayload { Kind = MachineStatusKinds.ApprovalRequested, AwaitingApproval = 1, RunId = "r1" }, ct).ConfigureAwait(false), "sent");
                    TunnelEnvelope status = await cloud.NextAsync(e => e.Type == TunnelMessageTypes.MachineStatus, Wait).ConfigureAwait(false);
                    MuxAssert.AreEqual(MachineStatusKinds.ApprovalRequested, status.MachineStatus!.Kind, "kind");
                    MuxAssert.AreEqual("r1", status.MachineStatus.RunId, "run id");
                }),
                new TestCaseDescriptor(Suite, "StopDisconnects", "StopAsync closes the tunnel and reports Disabled", async (CancellationToken ct) =>
                {
                    using FakeCloudServer cloud = new FakeCloudServer();
                    using LocalHubStub hub = new LocalHubStub();
                    using CloudTunnelClient client = NewClient(cloud, hub, null);
                    client.Start();
                    await WaitForStateAsync(client, CloudStateEnum.Connected).ConfigureAwait(false);
                    await client.StopAsync().ConfigureAwait(false);
                    MuxAssert.AreEqual(CloudStateEnum.Disabled, client.Status.State, "disabled");
                    await Task.Delay(1500, ct).ConfigureAwait(false);
                    MuxAssert.AreEqual(1, cloud.Hellos.Count, "no reconnect after stop");
                }),
                new TestCaseDescriptor(Suite, "EnrollmentClient", "Enrollment sends the machine description and returns the credential; a 401 explains the failure", async (CancellationToken ct) =>
                {
                    using FakeCloudServer cloud = new FakeCloudServer();
                    CloudEnrollmentClient enroll = new CloudEnrollmentClient();
                    CloudEnrollmentRequest request = new CloudEnrollmentRequest { Email = "dev@example.test", Password = "pw", MachineName = "box", Hostname = "h", Os = "o", MuxVersion = "2.0.0", Fingerprint = "f" };
                    CloudEnrollmentResponse result = await enroll.EnrollAsync(cloud.Url, request, false, ct).ConfigureAwait(false);
                    MuxAssert.AreEqual(cloud.MachineId, result.MachineId, "machine id");
                    MuxAssert.AreEqual(cloud.MachineSecret, result.MachineSecret, "secret");
                    using (JsonDocument sent = JsonDocument.Parse(cloud.LastEnrollBody))
                    {
                        foreach (string field in new[] { "email", "password", "machineName", "hostname", "os", "muxVersion", "fingerprint" })
                        {
                            MuxAssert.IsTrue(sent.RootElement.TryGetProperty(field, out _), "request carries " + field);
                        }
                    }

                    cloud.EnrollStatus = 401;
                    try
                    {
                        await enroll.EnrollAsync(cloud.Url, request, false, ct).ConfigureAwait(false);
                        MuxAssert.Fail("a 401 must throw");
                    }
                    catch (CloudEnrollmentException e)
                    {
                        MuxAssert.AreEqual(401, e.StatusCode, "status");
                        MuxAssert.Contains("not correct", e.Message, "user-facing message");
                    }

                    try
                    {
                        await enroll.EnrollAsync("http://127.0.0.1:1", request, false, ct).ConfigureAwait(false);
                        MuxAssert.Fail("an unreachable server must throw");
                    }
                    catch (CloudEnrollmentException e)
                    {
                        MuxAssert.AreEqual(0, e.StatusCode, "no status");
                        MuxAssert.Contains("Could not reach", e.Message, "explains");
                    }
                }),
                new TestCaseDescriptor(Suite, "RoutePolicyMatrix", "Remote approval follows cloud.allowRemoteToolApproval; local approval follows --allow-tools or a connected tunnel", (CancellationToken ct) =>
                {
                    CloudRoutePolicy policy = new CloudRoutePolicy(false);
                    MuxAssert.IsFalse(policy.AllowsInteractiveApproval(false), "read-only hub, no tunnel: local denied");
                    policy.CloudConnected = true;
                    MuxAssert.IsTrue(policy.AllowsInteractiveApproval(false), "connected with remote approval: local prompts");
                    MuxAssert.IsTrue(policy.AllowsInteractiveApproval(true), "connected with remote approval: relayed prompts");
                    MuxAssert.IsTrue(policy.AllowsApprovalDecision(true), "relayed decision allowed");
                    policy.AllowRemoteToolApproval = false;
                    MuxAssert.IsFalse(policy.AllowsInteractiveApproval(true), "remote approval off: relayed denied");
                    MuxAssert.IsFalse(policy.AllowsInteractiveApproval(false), "remote approval off, read-only hub: local denied as before");
                    MuxAssert.IsFalse(policy.AllowsApprovalDecision(true), "relayed decision refused");
                    MuxAssert.IsTrue(policy.AllowsApprovalDecision(false), "local decision allowed");
                    CloudRoutePolicy tools = new CloudRoutePolicy(true) { AllowRemoteToolApproval = false, CloudConnected = true };
                    MuxAssert.IsTrue(tools.AllowsInteractiveApproval(false), "--allow-tools: local prompts");
                    MuxAssert.IsFalse(tools.AllowsInteractiveApproval(true), "--allow-tools with remote approval off: relayed denied");
                    return Task.CompletedTask;
                }),
                new TestCaseDescriptor(Suite, "TunnelLockExclusive", "Only one tunnel lock can be held per config directory, and releasing it frees it", (CancellationToken ct) =>
                {
                    string dir = Path.Combine(Path.GetTempPath(), "mux-lock-" + Guid.NewGuid().ToString("N"));
                    try
                    {
                        using CloudTunnelLock a = new CloudTunnelLock(dir);
                        using CloudTunnelLock b = new CloudTunnelLock(dir);
                        MuxAssert.IsTrue(a.TryAcquire(), "first acquires");
                        MuxAssert.IsFalse(b.TryAcquire(), "second refused");
                        a.Release();
                        MuxAssert.IsTrue(b.TryAcquire(), "free after release");
                    }
                    finally
                    {
                        try { Directory.Delete(dir, true); } catch (Exception) { }
                    }

                    return Task.CompletedTask;
                })
            });
        }

        private static CloudTunnelClient NewClient(FakeCloudServer cloud, LocalHubStub hub, Action<CloudTunnelOptions>? configure)
        {
            CloudTunnelOptions options = new CloudTunnelOptions
            {
                ServerUrl = cloud.Url,
                MachineId = cloud.MachineId,
                MachineSecret = cloud.MachineSecret,
                MachineName = "test-machine",
                MuxVersion = "2.0.0-test",
                ReconnectBaseDelaySeconds = 1,
                ReconnectMaxDelaySeconds = 2,
                HandshakeTimeoutSeconds = 5
            };
            configure?.Invoke(options);
            string baseUrl = hub.BaseUrl;
            string machineId = cloud.MachineId;
            CloudRelayForwarder forwarder = new CloudRelayForwarder(() => new CloudLocalEndpoint { BaseUrl = baseUrl, ApiKey = "local-key", MachineId = machineId });
            return new CloudTunnelClient(options, forwarder);
        }

        private static async Task WaitForStateAsync(CloudTunnelClient client, CloudStateEnum state)
        {
            DateTime deadline = DateTime.UtcNow + Wait;
            while (DateTime.UtcNow < deadline)
            {
                if (client.Status.State == state) return;
                await Task.Delay(25).ConfigureAwait(false);
            }

            MuxAssert.Fail("expected state " + state + ", still " + client.Status.State + " (" + client.Status.Reason + ")");
        }

        private static async Task WaitUntilAsync(Func<bool> condition)
        {
            DateTime deadline = DateTime.UtcNow + Wait;
            while (DateTime.UtcNow < deadline && !condition())
            {
                await Task.Delay(25).ConfigureAwait(false);
            }
        }

        private static string Body(List<KeyValuePair<DateTime, TunnelEnvelope>> frames)
        {
            StringBuilder sb = new StringBuilder();
            foreach (KeyValuePair<DateTime, TunnelEnvelope> frame in frames)
            {
                if (frame.Value.Type == TunnelMessageTypes.HttpResponseChunk) sb.Append(Encoding.UTF8.GetString(Convert.FromBase64String(frame.Value.HttpChunk!.Data)));
            }

            return sb.ToString();
        }
    }
}
