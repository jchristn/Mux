namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Net;
    using System.Net.Http;
    using System.Net.Sockets;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Enums;
    using Mux.Core.Models;
    using Mux.Core.Sessions;
    using Mux.Server;
    using MuxCloud.Protocol;
    using Test.Shared.Support;
    using Touchstone.Core;

    /// <summary>
    /// The dashboard chat's auto-approve switch on a live hub with a scripted model that asks for a <c>run_process</c>
    /// call: <c>GET /v1.0/api/chat/capabilities</c> reports whether the caller may use it, <c>AutoApproveTools</c> runs
    /// the tool without an approval prompt, a run without it still prompts, and a request relayed through Mux Cloud
    /// while <c>cloud.allowRemoteToolApproval</c> is false cannot auto-approve.
    /// </summary>
    public static class ChatAutoApproveSuite
    {
        private const string Suite = "ChatAutoApprove";
        private const string Key = "autokey123";

        /// <summary>
        /// Builds the suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/>.</returns>
        public static TestSuiteDescriptor Create()
        {
            return new TestSuiteDescriptor(Suite, "Auto-approve tools in the dashboard chat", new List<TestCaseDescriptor>
            {
                new TestCaseDescriptor(Suite, "Capabilities", "GET /chat/capabilities offers auto-approve only to callers that may approve tools", async (CancellationToken ct) =>
                {
                    using ScriptedToolCallLlm llm = ScriptedToolCallLlm.Start();
                    await WithHubAsync(llm, true, async (MuxServer server, int port, string workDir) =>
                    {
                        using HttpClient http = Client();
                        JsonElement local = await GetJsonAsync(http, port, false, ct).ConfigureAwait(false);
                        MuxAssert.IsTrue(local.GetProperty("AutoApproveTools").GetBoolean(), "interactive hub: local caller may auto-approve");
                        MuxAssert.AreEqual(string.Empty, local.GetProperty("Reason").GetString(), "no reason when available");

                        server.CloudPolicy.AllowRemoteToolApproval = false;
                        JsonElement relayed = await GetJsonAsync(http, port, true, ct).ConfigureAwait(false);
                        MuxAssert.IsFalse(relayed.GetProperty("AutoApproveTools").GetBoolean(), "remote approval off: relayed caller may not");
                        MuxAssert.IsTrue(relayed.GetProperty("Relayed").GetBoolean(), "reports the relay");
                        MuxAssert.AreEqual("remote_approval_off", relayed.GetProperty("Reason").GetString(), "says why");
                        JsonElement stillLocal = await GetJsonAsync(http, port, false, ct).ConfigureAwait(false);
                        MuxAssert.IsTrue(stillLocal.GetProperty("AutoApproveTools").GetBoolean(), "the local caller keeps it");
                    }).ConfigureAwait(false);

                    await WithHubAsync(llm, false, async (MuxServer server, int port, string workDir) =>
                    {
                        using HttpClient http = Client();
                        JsonElement readOnly = await GetJsonAsync(http, port, false, ct).ConfigureAwait(false);
                        MuxAssert.IsFalse(readOnly.GetProperty("AutoApproveTools").GetBoolean(), "read-only hub: no auto-approve");
                        MuxAssert.AreEqual("read_only", readOnly.GetProperty("Reason").GetString(), "says why");
                    }).ConfigureAwait(false);
                }),
                new TestCaseDescriptor(Suite, "DashboardSwitch", "The dashboard chat has the auto-approve switch, sends the flag, and every locale labels it", async (CancellationToken ct) =>
                {
                    using ScriptedToolCallLlm llm = ScriptedToolCallLlm.Start();
                    await WithHubAsync(llm, true, async (MuxServer server, int port, string workDir) =>
                    {
                        using HttpClient http = Client();
                        string page = await http.GetStringAsync("http://127.0.0.1:" + port + "/dashboard", ct).ConfigureAwait(false);
                        MuxAssert.Contains("id=\"autoApprove\"", page, "the checkbox is in the chat toolbar");
                        MuxAssert.Contains("AutoApproveTools:autoApproveActive()", page, "chat runs send the flag");
                        MuxAssert.Contains("\"/v1.0/api/chat/capabilities\"", page, "the page asks whether it may offer the switch");
                        foreach (string key in new string[] { "chat.autoApprove", "chat.autoApprove.tip", "chat.autoApprove.on", "chat.autoApprove.offRemote", "chat.autoApprove.offReadOnly" })
                        {
                            MuxAssert.AreEqual(11, page.Split("\"" + key + "\":").Length - 1, "every locale has " + key);
                        }
                    }).ConfigureAwait(false);
                }),
                new TestCaseDescriptor(Suite, "AutoApproveRunsTool", "With AutoApproveTools the run executes run_process without an approval prompt", async (CancellationToken ct) =>
                {
                    using ScriptedToolCallLlm llm = ScriptedToolCallLlm.Start();
                    await WithHubAsync(llm, true, async (MuxServer server, int port, string workDir) =>
                    {
                        using HttpClient http = Client();
                        ChatStreamResult result = await StreamAsync(http, port, workDir, true, false, null, ct).ConfigureAwait(false);
                        MuxAssert.AreEqual(0, result.Approvals, "no approval prompt");
                        MuxAssert.Contains("TOOL_OUTPUT_SEEN", result.Text, "the tool really ran");
                    }).ConfigureAwait(false);
                }),
                new TestCaseDescriptor(Suite, "WithoutFlagPrompts", "Without AutoApproveTools the same run raises an approval prompt", async (CancellationToken ct) =>
                {
                    using ScriptedToolCallLlm llm = ScriptedToolCallLlm.Start();
                    await WithHubAsync(llm, true, async (MuxServer server, int port, string workDir) =>
                    {
                        using HttpClient http = Client();
                        ChatStreamResult result = await StreamAsync(http, port, workDir, false, false, "n", ct).ConfigureAwait(false);
                        MuxAssert.AreEqual(1, result.Approvals, "one approval prompt");
                        MuxAssert.Contains("TOOL_OUTPUT_MISSING", result.Text, "denied, so the tool did not run");
                    }).ConfigureAwait(false);
                }),
                new TestCaseDescriptor(Suite, "RelayedCannotAutoApprove", "A relayed run with remote approval off ignores AutoApproveTools and denies the tool", async (CancellationToken ct) =>
                {
                    using ScriptedToolCallLlm llm = ScriptedToolCallLlm.Start();
                    await WithHubAsync(llm, true, async (MuxServer server, int port, string workDir) =>
                    {
                        server.CloudPolicy.AllowRemoteToolApproval = false;
                        using HttpClient http = Client();
                        ChatStreamResult relayed = await StreamAsync(http, port, workDir, true, true, null, ct).ConfigureAwait(false);
                        MuxAssert.AreEqual(0, relayed.Approvals, "no prompt either");
                        MuxAssert.Contains("TOOL_OUTPUT_MISSING", relayed.Text, "the tool was denied");

                        ChatStreamResult local = await StreamAsync(http, port, workDir, true, false, null, ct).ConfigureAwait(false);
                        MuxAssert.Contains("TOOL_OUTPUT_SEEN", local.Text, "the same request from the machine itself still auto-approves");
                    }).ConfigureAwait(false);
                }),
            });
        }

        private static HttpClient Client()
        {
            HttpClient http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
            http.DefaultRequestHeaders.Add("Authorization", "Bearer " + Key);
            return http;
        }

        private static async Task<JsonElement> GetJsonAsync(HttpClient http, int port, bool relayed, CancellationToken ct)
        {
            using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, "http://127.0.0.1:" + port + "/v1.0/api/chat/capabilities");
            if (relayed) request.Headers.Add(TunnelProtocol.RelayHeader, TunnelProtocol.RelayHeaderValue);
            using HttpResponseMessage response = await http.SendAsync(request, ct).ConfigureAwait(false);
            MuxAssert.AreEqual(200, (int)response.StatusCode, "capabilities status");
            string body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            using JsonDocument doc = JsonDocument.Parse(body);
            return doc.RootElement.Clone();
        }

        // Runs one streaming chat turn and collects its token text and approval prompts. When a prompt arrives and
        // decision is set, answers it over REST so the blocked run can finish.
        private static async Task<ChatStreamResult> StreamAsync(HttpClient http, int port, string workDir, bool autoApprove, bool relayed, string? decision, CancellationToken ct)
        {
            string payload = JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["Endpoint"] = "scripted",
                ["WorkingDirectory"] = workDir,
                ["AutoApproveTools"] = autoApprove,
                ["Messages"] = new object[] { new Dictionary<string, object?> { ["Role"] = "user", ["Content"] = "Run the tool." } }
            });
            using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, "http://127.0.0.1:" + port + "/v1.0/api/chat/stream")
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };
            if (relayed) request.Headers.Add(TunnelProtocol.RelayHeader, TunnelProtocol.RelayHeaderValue);
            using HttpResponseMessage response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            MuxAssert.AreEqual(200, (int)response.StatusCode, "stream status");

            ChatStreamResult result = new ChatStreamResult();
            StringBuilder text = new StringBuilder();
            using Stream stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using StreamReader reader = new StreamReader(stream, Encoding.UTF8);
            string currentEvent = string.Empty;
            while (true)
            {
                string? line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
                if (line == null) break;
                if (line.StartsWith("event:", StringComparison.Ordinal))
                {
                    currentEvent = line.Substring(6).Trim();
                    continue;
                }

                if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;
                string data = line.Substring(5).Trim();
                if (currentEvent == "token")
                {
                    text.Append(JsonSerializer.Deserialize<string>(data));
                }
                else if (currentEvent == "approval")
                {
                    result.Approvals++;
                    if (decision != null)
                    {
                        using JsonDocument prompt = JsonDocument.Parse(data);
                        string body = JsonSerializer.Serialize(new Dictionary<string, string>
                        {
                            ["RunId"] = prompt.RootElement.GetProperty("RunId").GetString() ?? string.Empty,
                            ["ToolCallId"] = prompt.RootElement.GetProperty("ToolCallId").GetString() ?? string.Empty,
                            ["Decision"] = decision
                        });
                        using HttpRequestMessage approve = new HttpRequestMessage(HttpMethod.Post, "http://127.0.0.1:" + port + "/v1.0/api/chat/approve")
                        {
                            Content = new StringContent(body, Encoding.UTF8, "application/json")
                        };
                        using HttpResponseMessage approved = await http.SendAsync(approve, ct).ConfigureAwait(false);
                        MuxAssert.AreEqual(200, (int)approved.StatusCode, "decision accepted");
                    }
                }
                else if (currentEvent == "done" || currentEvent == "error")
                {
                    text.Append(' ').Append(data);
                }
            }

            result.Text = text.ToString();
            return result;
        }

        private static async Task WithHubAsync(ScriptedToolCallLlm llm, bool allowInteractiveTools, Func<MuxServer, int, string, Task> body)
        {
            string dir = Path.Combine(Path.GetTempPath(), "mux-autoapprove-" + Guid.NewGuid().ToString("N"));
            string workDir = Path.Combine(dir, "work");
            Directory.CreateDirectory(workDir);
            string? original = Environment.GetEnvironmentVariable("MUX_CONFIG_DIR");
            Environment.SetEnvironmentVariable("MUX_CONFIG_DIR", dir);
            List<EndpointConfig> endpoints = new List<EndpointConfig>
            {
                new EndpointConfig { Name = "scripted", AdapterType = AdapterTypeEnum.OpenAiCompatible, BaseUrl = llm.BaseUrl, Model = "scripted", IsDefault = true }
            };
            RestServerSettings rest = new RestServerSettings { Hostname = "127.0.0.1", ApiKey = Key };
            MuxServer? server = null;
            int port = 0;
            try
            {
                for (int attempt = 0; attempt < 10 && server == null; attempt++)
                {
                    port = FreePort();
                    rest.Port = port;
                    MuxServer candidate = new MuxServer(rest, "9.9.9-test", new SessionStore(Path.Combine(dir, "sessions")), () => endpoints, null, null, null, allowInteractiveTools);
                    try { candidate.Start(); server = candidate; }
                    catch (Exception) { candidate.Dispose(); Thread.Sleep(50); }
                }

                MuxAssert.IsNotNull(server, "hub bound");
                await body(server!, port, workDir).ConfigureAwait(false);
            }
            finally
            {
                if (server != null)
                {
                    server.Stop();
                    server.Dispose();
                }

                Environment.SetEnvironmentVariable("MUX_CONFIG_DIR", original);
                try { Directory.Delete(dir, true); } catch (Exception) { }
            }
        }

        private static int FreePort()
        {
            TcpListener probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            int port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            return port;
        }
    }
}
