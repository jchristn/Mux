namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Net;
    using System.Net.Http;
    using System.Net.Sockets;
    using System.Net.WebSockets;
    using System.Runtime.InteropServices;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Enums;
    using Mux.Core.Interaction;
    using Mux.Core.Models;
    using Mux.Core.Processes;
    using Mux.Core.Runs;
    using Mux.Core.Sessions;
    using Mux.Server;
    using MuxCloud.Protocol;
    using Test.Shared.Support;
    using Touchstone.Core;

    /// <summary>
    /// The hub routes and stream events that let the Mux Cloud mobile app match the desktop: <c>ask_user</c> questions
    /// and answers (REST and WebSocket), tool events with arguments and results, the <c>attachments</c> and
    /// <c>compacted</c> events, AI titles for REST conversations, manual compaction, background processes, project
    /// trust, web search providers, the added settings fields with merge-on-PUT, and the keybinding command list.
    /// </summary>
    public static class MobileParityRoutesSuite
    {
        private const string Suite = "MobileParityRoutes";
        private const string Key = "paritykey123";

        /// <summary>
        /// Builds the suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/>.</returns>
        public static TestSuiteDescriptor Create()
        {
            return new TestSuiteDescriptor(Suite, "Hub routes and events for mobile parity", new List<TestCaseDescriptor>
            {
                new TestCaseDescriptor(Suite, "AskUserAnswered", "An ask_user question streams with its options and a REST answer reaches the model", async (CancellationToken ct) =>
                {
                    using ParityScriptedLlm llm = ParityScriptedLlm.Start();
                    await WithHubAsync(llm, async (MuxServer server, int port, string workDir) =>
                    {
                        using HttpClient http = Client();
                        ParityStreamResult result = await StreamAsync(http, port, workDir, "Please ASK_USER now", false, new ParityAnswer { Labels = new List<string> { "Postgres" } }, ct).ConfigureAwait(false);
                        MuxAssert.AreEqual(1, result.Questions.Count, "one question");
                        JsonElement question = result.Questions[0];
                        MuxAssert.AreEqual("Which database?", question.GetProperty("Question").GetString(), "question text");
                        MuxAssert.AreEqual(2, question.GetProperty("Options").GetArrayLength(), "two options");
                        MuxAssert.AreEqual("Postgres", question.GetProperty("Options")[0].GetProperty("Label").GetString(), "option label");
                        MuxAssert.IsTrue(question.GetProperty("AllowOther").GetBoolean() && question.GetProperty("AllowSkip").GetBoolean(), "other and skip allowed");
                        MuxAssert.AreEqual(900, question.GetProperty("TimeoutSeconds").GetInt32(), "timeout");
                        MuxAssert.Contains("TOOL_RESULT", result.Text, "the run continued");
                        MuxAssert.Contains("Postgres", result.Text, "the model received the answer");
                    }).ConfigureAwait(false);
                }),
                new TestCaseDescriptor(Suite, "AskUserSkippedRelayed", "A relayed caller with remote approval off can still skip a question", async (CancellationToken ct) =>
                {
                    using ParityScriptedLlm llm = ParityScriptedLlm.Start();
                    await WithHubAsync(llm, async (MuxServer server, int port, string workDir) =>
                    {
                        server.CloudPolicy.AllowRemoteToolApproval = false;
                        using HttpClient http = Client();
                        ParityStreamResult result = await StreamAsync(http, port, workDir, "ASK_USER again", true, new ParityAnswer { Skip = true }, ct).ConfigureAwait(false);
                        MuxAssert.AreEqual(1, result.Questions.Count, "the question reached the relayed caller");
                        MuxAssert.Contains("dismissed", result.Text, "the model was told the question was skipped");
                    }).ConfigureAwait(false);
                }),
                new TestCaseDescriptor(Suite, "AskUserOptIn", "A client that does not opt in to questions gets the no-user answer, never a question", async (CancellationToken ct) =>
                {
                    using ParityScriptedLlm llm = ParityScriptedLlm.Start();
                    await WithHubAsync(llm, async (MuxServer server, int port, string workDir) =>
                    {
                        using HttpClient http = Client();
                        List<Dictionary<string, object?>> messages = new List<Dictionary<string, object?>>
                        {
                            new Dictionary<string, object?> { ["Role"] = "user", ["Content"] = "ASK_USER please" }
                        };
                        ParityStreamResult result = await StreamMessagesAsync(http, port, workDir, "scripted", messages, false, null, ct, askUser: false).ConfigureAwait(false);
                        MuxAssert.AreEqual(0, result.Questions.Count, "no question streamed");
                        MuxAssert.Contains("TOOL_RESULT", result.Text, "the run finished on its own");
                    }).ConfigureAwait(false);
                }),
                new TestCaseDescriptor(Suite, "AskUserUnknownIs404", "Answering an unknown question returns 404", async (CancellationToken ct) =>
                {
                    using ParityScriptedLlm llm = ParityScriptedLlm.Start();
                    await WithHubAsync(llm, async (MuxServer server, int port, string workDir) =>
                    {
                        using HttpClient http = Client();
                        using HttpResponseMessage response = await PostJsonAsync(http, port, "/v1.0/api/chat/answer", "{\"RunId\":\"nope\",\"QuestionId\":\"q_x\",\"Answers\":[\"a\"]}", ct).ConfigureAwait(false);
                        MuxAssert.AreEqual(404, (int)response.StatusCode, "unknown question");
                        using HttpResponseMessage bad = await PostJsonAsync(http, port, "/v1.0/api/chat/answer", "{\"RunId\":\"\"}", ct).ConfigureAwait(false);
                        MuxAssert.AreEqual(400, (int)bad.StatusCode, "missing ids");
                    }).ConfigureAwait(false);
                }),
                new TestCaseDescriptor(Suite, "AskUserWebSocketAnswer", "A WebSocket answer frame resolves a pending question and the replay carries the ask_user frame", async (CancellationToken ct) =>
                {
                    using ParityScriptedLlm llm = ParityScriptedLlm.Start();
                    await WithHubAsync(llm, async (MuxServer server, int port, string workDir) =>
                    {
                        RunHandle handle = server.Runs.GetOrCreate("ws-q-run", "ws-q-sess", "ep", "m");
                        AskUserRequest request = new AskUserRequest { Question = "Pick one" };
                        request.Options.Add(new AskUserOption { Label = "Alpha" });
                        request.Options.Add(new AskUserOption { Label = "Beta" });
                        TaskCompletionSource<AskUserResponse> pending = handle.RegisterQuestion("q_ws", request, 900);
                        MuxAssert.AreEqual(1, handle.PendingQuestionCount, "pending");
                        MuxAssert.AreEqual(RunStatusEnum.AwaitingApproval, handle.Status, "status shows the run needs the user");

                        using ClientWebSocket ws = new ClientWebSocket();
                        await ws.ConnectAsync(new Uri("ws://127.0.0.1:" + port + "/v1.0/ws?apiKey=" + Key), ct).ConfigureAwait(false);
                        await ReceiveTextAsync(ws, ct).ConfigureAwait(false);
                        await SendTextAsync(ws, "{\"action\":\"subscribe\",\"sessionId\":\"ws-q-sess\"}", ct).ConfigureAwait(false);
                        string replayed = await ReceiveTextAsync(ws, ct).ConfigureAwait(false);
                        MuxAssert.Contains("\"eventType\":\"ask_user\"", replayed, "replayed question frame");
                        MuxAssert.Contains("\"questionId\":\"q_ws\"", replayed, "question id in the frame");
                        await SendTextAsync(ws, "{\"action\":\"answer\",\"runId\":\"ws-q-run\",\"questionId\":\"q_ws\",\"answers\":[\"Beta\"],\"other\":\"because\"}", ct).ConfigureAwait(false);
                        AskUserResponse answer = await pending.Task.WaitAsync(TimeSpan.FromSeconds(5), ct).ConfigureAwait(false);
                        MuxAssert.AreEqual("Beta", answer.Selected[0], "the frame's label");
                        MuxAssert.AreEqual("because", answer.OtherText, "free text");
                        MuxAssert.AreEqual(RunStatusEnum.Running, handle.Status, "status back to running");
                        string resolved = await ReceiveTextAsync(ws, ct).ConfigureAwait(false);
                        MuxAssert.Contains("ask_user_resolved", resolved, "subscribers learn the question was answered");
                    }).ConfigureAwait(false);
                }),
                new TestCaseDescriptor(Suite, "ToolEventsAndAttachments", "Tool events carry arguments and the result; @mentions produce an attachments event", async (CancellationToken ct) =>
                {
                    using ParityScriptedLlm llm = ParityScriptedLlm.Start();
                    await WithHubAsync(llm, async (MuxServer server, int port, string workDir) =>
                    {
                        File.WriteAllText(Path.Combine(workDir, "notes.txt"), "PARITY_FILE_CONTENT");
                        using HttpClient http = Client();
                        ParityStreamResult result = await StreamAsync(http, port, workDir, "READ_FILE:notes.txt see @notes.txt and @missing.txt", false, null, ct).ConfigureAwait(false);
                        JsonElement running = result.Tools.Find(t => t.GetProperty("Status").GetString() == "running");
                        JsonElement finished = result.Tools.Find(t => t.GetProperty("Status").GetString() == "ok");
                        MuxAssert.Contains("notes.txt", running.GetProperty("Arguments").GetString() ?? string.Empty, "running event carries the arguments");
                        MuxAssert.Contains("PARITY_FILE_CONTENT", finished.GetProperty("Result").GetString() ?? string.Empty, "finished event carries the result");
                        MuxAssert.IsFalse(finished.GetProperty("ResultTruncated").GetBoolean(), "short result not truncated");
                        MuxAssert.AreEqual(1, result.Attachments.Count, "one attachments event");
                        JsonElement attachments = result.Attachments[0];
                        MuxAssert.AreEqual("notes.txt", attachments.GetProperty("Attached")[0].GetProperty("Path").GetString(), "attached file");
                        MuxAssert.AreEqual("@missing.txt", attachments.GetProperty("Skipped")[0].GetProperty("Path").GetString(), "skipped mention");
                        MuxAssert.Contains("not found", attachments.GetProperty("Skipped")[0].GetProperty("Reason").GetString() ?? string.Empty, "why it was skipped");
                    }).ConfigureAwait(false);
                }),
                new TestCaseDescriptor(Suite, "AiTitle", "A REST conversation gets an AI title once it crosses the summary threshold", async (CancellationToken ct) =>
                {
                    using ParityScriptedLlm llm = ParityScriptedLlm.Start();
                    await WithHubAsync(llm, async (MuxServer server, int port, string workDir) =>
                    {
                        using HttpClient http = Client();
                        ParityStreamResult result = await StreamAsync(http, port, workDir, "Say something long", false, null, ct).ConfigureAwait(false);
                        string sessionId = result.SessionId;
                        MuxAssert.IsFalse(string.IsNullOrEmpty(sessionId), "session id");
                        string title = string.Empty;
                        for (int i = 0; i < 50 && title != ParityScriptedLlm.Title; i++)
                        {
                            await Task.Delay(100, ct).ConfigureAwait(false);
                            using JsonDocument detail = JsonDocument.Parse(await http.GetStringAsync("http://127.0.0.1:" + port + "/v1.0/api/sessions/detail?id=" + sessionId, ct).ConfigureAwait(false));
                            title = detail.RootElement.GetProperty("Title").GetString() ?? string.Empty;
                        }

                        MuxAssert.AreEqual(ParityScriptedLlm.Title, title, "AI title applied");
                    }).ConfigureAwait(false);
                }),
                new TestCaseDescriptor(Suite, "AutoCompactionEvent", "A run that compacts its context streams a compacted event", async (CancellationToken ct) =>
                {
                    using ParityScriptedLlm llm = ParityScriptedLlm.Start();
                    await WithHubAsync(llm, async (MuxServer server, int port, string workDir) =>
                    {
                        using HttpClient http = Client();
                        List<Dictionary<string, object?>> messages = new List<Dictionary<string, object?>>();
                        for (int i = 0; i < 12; i++)
                        {
                            messages.Add(new Dictionary<string, object?> { ["Role"] = "user", ["Content"] = "Question " + i + " " + new string('q', 6000) });
                            messages.Add(new Dictionary<string, object?> { ["Role"] = "assistant", ["Content"] = "Answer " + i + " " + new string('a', 6000) });
                        }

                        messages.Add(new Dictionary<string, object?> { ["Role"] = "user", ["Content"] = "And now?" });
                        ParityStreamResult result = await StreamMessagesAsync(http, port, workDir, "small", messages, false, null, ct).ConfigureAwait(false);
                        MuxAssert.IsTrue(result.Compacted.Count >= 1, "a compacted event arrived");
                        JsonElement compacted = result.Compacted[0];
                        MuxAssert.IsTrue(compacted.GetProperty("MessagesAfter").GetInt32() < compacted.GetProperty("MessagesBefore").GetInt32(), "fewer messages");
                        MuxAssert.IsTrue(compacted.GetProperty("EstimatedTokensAfter").GetInt32() < compacted.GetProperty("EstimatedTokensBefore").GetInt32(), "fewer tokens");
                    }).ConfigureAwait(false);
                }),
                new TestCaseDescriptor(Suite, "ManualCompaction", "POST /sessions/{id}/compact summarizes a saved conversation and reports the change", async (CancellationToken ct) =>
                {
                    using ParityScriptedLlm llm = ParityScriptedLlm.Start();
                    await WithHubAsync(llm, async (MuxServer server, int port, string workDir) =>
                    {
                        SessionStore store = new SessionStore(Path.Combine(Environment.GetEnvironmentVariable("MUX_CONFIG_DIR")!, "sessions"));
                        SessionSnapshot snapshot = new SessionSnapshot { Id = "compact-me", Title = "Long talk", EndpointName = "scripted", CreatedUtc = DateTime.UtcNow };
                        for (int i = 0; i < 10; i++)
                        {
                            snapshot.ConversationHistory.Add(new ConversationMessage { Role = RoleEnum.User, Content = "Q" + i + " " + new string('q', 400) });
                            snapshot.ConversationHistory.Add(new ConversationMessage { Role = RoleEnum.Assistant, Content = "A" + i + " " + new string('a', 400) });
                        }

                        await store.SaveAsync(snapshot, ct).ConfigureAwait(false);
                        using HttpClient http = Client();
                        using HttpResponseMessage response = await PostJsonAsync(http, port, "/v1.0/api/sessions/compact-me/compact", string.Empty, ct).ConfigureAwait(false);
                        string body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                        MuxAssert.AreEqual(200, (int)response.StatusCode, "compact status: " + body);
                        using JsonDocument doc = JsonDocument.Parse(body);
                        MuxAssert.IsTrue(doc.RootElement.GetProperty("Compacted").GetBoolean(), "compacted");
                        MuxAssert.AreEqual(20, doc.RootElement.GetProperty("MessagesBefore").GetInt32(), "before");
                        MuxAssert.IsTrue(doc.RootElement.GetProperty("MessagesAfter").GetInt32() < 20, "after is smaller");
                        MuxAssert.IsTrue(doc.RootElement.GetProperty("EstimatedTokensAfter").GetInt32() < doc.RootElement.GetProperty("EstimatedTokensBefore").GetInt32(), "fewer tokens");
                        SessionSnapshot? saved = await store.LoadAsync("compact-me", ct).ConfigureAwait(false);
                        MuxAssert.Contains(ParityScriptedLlm.Summary, saved!.ConversationHistory[0].Content ?? string.Empty, "the summary leads the saved history");
                        MuxAssert.AreEqual(1, saved.CompactionCount, "compaction counted");

                        using HttpResponseMessage missing = await PostJsonAsync(http, port, "/v1.0/api/sessions/nope/compact", string.Empty, ct).ConfigureAwait(false);
                        MuxAssert.AreEqual(404, (int)missing.StatusCode, "unknown session");
                    }).ConfigureAwait(false);
                }),
                new TestCaseDescriptor(Suite, "Processes", "List, read, stop, and clear background processes", async (CancellationToken ct) =>
                {
                    using ParityScriptedLlm llm = ParityScriptedLlm.Start();
                    await WithHubAsync(llm, async (MuxServer server, int port, string workDir) =>
                    {
                        bool windows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
                        BackgroundProcessInfo quick = server.Processes.Start(windows ? "echo parity-proc" : "echo parity-proc", workDir, "quick");
                        BackgroundProcessInfo slow = server.Processes.Start(windows ? "ping -n 60 127.0.0.1" : "sleep 60", workDir, "slow");
                        using HttpClient http = Client();
                        using JsonDocument list = JsonDocument.Parse(await http.GetStringAsync("http://127.0.0.1:" + port + "/v1.0/api/processes", ct).ConfigureAwait(false));
                        MuxAssert.AreEqual(2, list.RootElement.GetProperty("Count").GetInt32(), "two processes");

                        string text = string.Empty;
                        for (int i = 0; i < 50 && !text.Contains("parity-proc", StringComparison.Ordinal); i++)
                        {
                            await Task.Delay(100, ct).ConfigureAwait(false);
                            using JsonDocument output = JsonDocument.Parse(await http.GetStringAsync("http://127.0.0.1:" + port + "/v1.0/api/processes/" + quick.Id + "/output", ct).ConfigureAwait(false));
                            text = output.RootElement.GetProperty("Text").GetString() ?? string.Empty;
                        }

                        MuxAssert.Contains("parity-proc", text, "output tail");
                        using HttpResponseMessage stop = await PostJsonAsync(http, port, "/v1.0/api/processes/" + slow.Id + "/stop", string.Empty, ct).ConfigureAwait(false);
                        MuxAssert.AreEqual(200, (int)stop.StatusCode, "stop status");
                        MuxAssert.IsFalse(server.Processes.Get(slow.Id)!.Running, "stopped");

                        for (int i = 0; i < 50 && server.Processes.Get(quick.Id)!.Running; i++)
                        {
                            await Task.Delay(100, ct).ConfigureAwait(false);
                        }

                        using HttpRequestMessage clear = new HttpRequestMessage(HttpMethod.Delete, "http://127.0.0.1:" + port + "/v1.0/api/processes");
                        using HttpResponseMessage cleared = await http.SendAsync(clear, ct).ConfigureAwait(false);
                        using JsonDocument clearedDoc = JsonDocument.Parse(await cleared.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
                        MuxAssert.AreEqual(2, clearedDoc.RootElement.GetProperty("Removed").GetInt32(), "both exited processes cleared");
                        using HttpResponseMessage gone = await http.GetAsync("http://127.0.0.1:" + port + "/v1.0/api/processes/" + quick.Id + "/output", ct).ConfigureAwait(false);
                        MuxAssert.AreEqual(404, (int)gone.StatusCode, "cleared process is gone");
                    }).ConfigureAwait(false);
                }),
                new TestCaseDescriptor(Suite, "Trust", "Read, set, and reset project trust", async (CancellationToken ct) =>
                {
                    using ParityScriptedLlm llm = ParityScriptedLlm.Start();
                    await WithHubAsync(llm, async (MuxServer server, int port, string workDir) =>
                    {
                        using HttpClient http = Client();
                        string url = "http://127.0.0.1:" + port + "/v1.0/api/trust?workingDirectory=" + Uri.EscapeDataString(workDir);
                        using (JsonDocument initial = JsonDocument.Parse(await http.GetStringAsync(url, ct).ConfigureAwait(false)))
                        {
                            MuxAssert.AreEqual("unknown", initial.RootElement.GetProperty("Level").GetString(), "no decision yet");
                        }

                        using HttpResponseMessage set = await PutJsonAsync(http, port, "/v1.0/api/trust", JsonSerializer.Serialize(new Dictionary<string, string> { ["WorkingDirectory"] = workDir, ["Level"] = "all" }), ct).ConfigureAwait(false);
                        MuxAssert.AreEqual(200, (int)set.StatusCode, "set status");
                        using (JsonDocument after = JsonDocument.Parse(await http.GetStringAsync(url, ct).ConfigureAwait(false)))
                        {
                            MuxAssert.AreEqual("all", after.RootElement.GetProperty("Level").GetString(), "recorded");
                        }

                        using HttpResponseMessage reset = await PutJsonAsync(http, port, "/v1.0/api/trust", JsonSerializer.Serialize(new Dictionary<string, string> { ["WorkingDirectory"] = workDir, ["Level"] = "reset" }), ct).ConfigureAwait(false);
                        MuxAssert.AreEqual(200, (int)reset.StatusCode, "reset status");
                        using (JsonDocument cleared = JsonDocument.Parse(await http.GetStringAsync(url, ct).ConfigureAwait(false)))
                        {
                            MuxAssert.AreEqual("unknown", cleared.RootElement.GetProperty("Level").GetString(), "reset removes the decision");
                        }

                        using HttpResponseMessage bad = await PutJsonAsync(http, port, "/v1.0/api/trust", JsonSerializer.Serialize(new Dictionary<string, string> { ["WorkingDirectory"] = workDir, ["Level"] = "sometimes" }), ct).ConfigureAwait(false);
                        MuxAssert.AreEqual(400, (int)bad.StatusCode, "unknown level rejected");
                    }).ConfigureAwait(false);
                }),
                new TestCaseDescriptor(Suite, "SearchProviders", "Search settings round-trip, keys are masked and kept, and providers delete", async (CancellationToken ct) =>
                {
                    using ParityScriptedLlm llm = ParityScriptedLlm.Start();
                    await WithHubAsync(llm, async (MuxServer server, int port, string workDir) =>
                    {
                        using HttpClient http = Client();
                        string first = "{\"Enabled\":true,\"AllowFallback\":false,\"Providers\":[{\"Name\":\"main\",\"ProviderType\":\"tavily\",\"ApiKey\":\"secret-key-1\",\"Enabled\":true},{\"Name\":\"backup\",\"ProviderType\":\"you\",\"Enabled\":true}]}";
                        using HttpResponseMessage put = await PutJsonAsync(http, port, "/v1.0/api/search", first, ct).ConfigureAwait(false);
                        string putBody = await put.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                        MuxAssert.AreEqual(200, (int)put.StatusCode, "put status: " + putBody);
                        MuxAssert.IsFalse(putBody.Contains("secret-key-1", StringComparison.Ordinal), "key never returned");
                        using (JsonDocument doc = JsonDocument.Parse(putBody))
                        {
                            JsonElement main = doc.RootElement.GetProperty("Providers")[0];
                            MuxAssert.IsTrue(main.GetProperty("ApiKeySet").GetBoolean(), "key stored");
                            MuxAssert.IsTrue(main.GetProperty("IsDefault").GetBoolean(), "first enabled becomes default");
                            MuxAssert.IsFalse(doc.RootElement.GetProperty("AllowFallback").GetBoolean(), "fallback saved");
                            MuxAssert.AreEqual(2, doc.RootElement.GetProperty("ProviderTypes").GetArrayLength(), "types listed");
                        }

                        string second = "{\"Enabled\":true,\"AllowFallback\":true,\"Providers\":[{\"Name\":\"main\",\"ProviderType\":\"tavily\",\"ApiKey\":\"\",\"Enabled\":true},{\"Name\":\"backup\",\"ProviderType\":\"you\",\"Enabled\":true,\"IsDefault\":true}]}";
                        using HttpResponseMessage keep = await PutJsonAsync(http, port, "/v1.0/api/search", second, ct).ConfigureAwait(false);
                        MuxAssert.AreEqual(200, (int)keep.StatusCode, "second put");
                        MuxAssert.AreEqual("secret-key-1", Mux.Core.Settings.SettingsLoader.LoadSettings().ExternalSearch.Providers[0].ApiKey, "a blank key keeps the stored key");

                        using HttpRequestMessage delete = new HttpRequestMessage(HttpMethod.Delete, "http://127.0.0.1:" + port + "/v1.0/api/search/providers/backup");
                        using HttpResponseMessage deleted = await http.SendAsync(delete, ct).ConfigureAwait(false);
                        using (JsonDocument after = JsonDocument.Parse(await deleted.Content.ReadAsStringAsync(ct).ConfigureAwait(false)))
                        {
                            MuxAssert.AreEqual(1, after.RootElement.GetProperty("Providers").GetArrayLength(), "one provider left");
                        }

                        using HttpResponseMessage bad = await PutJsonAsync(http, port, "/v1.0/api/search", "{\"Providers\":[{\"Name\":\"x\",\"ProviderType\":\"bing\"}]}", ct).ConfigureAwait(false);
                        MuxAssert.AreEqual(400, (int)bad.StatusCode, "unknown provider type rejected");
                    }).ConfigureAwait(false);
                }),
                new TestCaseDescriptor(Suite, "SettingsFieldsAndPartialPut", "The added settings fields round-trip and a partial PUT leaves other fields alone", async (CancellationToken ct) =>
                {
                    using ParityScriptedLlm llm = ParityScriptedLlm.Start();
                    await WithHubAsync(llm, async (MuxServer server, int port, string workDir) =>
                    {
                        using HttpClient http = Client();
                        using HttpResponseMessage first = await PutJsonAsync(http, port, "/v1.0/api/settings", "{\"maxAgentIterations\":77,\"tokenEstimationRatio\":4.5,\"contextWindowSafetyMarginPercent\":20,\"skillRefreshIntervalSeconds\":45,\"maxTokenBudget\":5000,\"telemetryEnabled\":true,\"systemPromptPath\":\"/tmp/prompt.md\"}", ct).ConfigureAwait(false);
                        MuxAssert.AreEqual(200, (int)first.StatusCode, "first put");
                        using HttpResponseMessage partial = await PutJsonAsync(http, port, "/v1.0/api/settings", "{\"ContextWarningThresholdPercent\":65}", ct).ConfigureAwait(false);
                        MuxAssert.AreEqual(200, (int)partial.StatusCode, "partial put");

                        using JsonDocument doc = JsonDocument.Parse(await http.GetStringAsync("http://127.0.0.1:" + port + "/v1.0/api/settings", ct).ConfigureAwait(false));
                        JsonElement root = doc.RootElement;
                        MuxAssert.AreEqual(65, root.GetProperty("ContextWarningThresholdPercent").GetInt32(), "partial field applied");
                        MuxAssert.AreEqual(77, root.GetProperty("MaxAgentIterations").GetInt32(), "unsent field kept");
                        MuxAssert.AreEqual(4.5, root.GetProperty("TokenEstimationRatio").GetDouble(), "ratio kept");
                        MuxAssert.AreEqual(20, root.GetProperty("ContextWindowSafetyMarginPercent").GetInt32(), "margin kept");
                        MuxAssert.AreEqual(45, root.GetProperty("SkillRefreshIntervalSeconds").GetInt32(), "refresh kept");
                        MuxAssert.AreEqual(5000, root.GetProperty("MaxTokenBudget").GetInt32(), "budget kept");
                        MuxAssert.IsTrue(root.GetProperty("TelemetryEnabled").GetBoolean(), "telemetry kept");
                        MuxAssert.AreEqual("/tmp/prompt.md", root.GetProperty("SystemPromptPath").GetString(), "prompt path kept");
                        MuxAssert.AreEqual(4.5, Mux.Core.Settings.SettingsLoader.LoadSettings().TokenEstimationRatio, "persisted to settings.json");
                    }).ConfigureAwait(false);
                }),
                new TestCaseDescriptor(Suite, "ExistingRoutesRelayed", "The routes the mobile app reuses answer requests relayed through Mux Cloud", async (CancellationToken ct) =>
                {
                    using ParityScriptedLlm llm = ParityScriptedLlm.Start();
                    await WithHubAsync(llm, async (MuxServer server, int port, string workDir) =>
                    {
                        File.WriteAllText(Path.Combine(workDir, "MUX.md"), "Project instructions.");
                        File.WriteAllText(Path.Combine(workDir, "readme.txt"), "hello");
                        string wd = Uri.EscapeDataString(workDir);
                        using HttpClient http = Client();
                        http.DefaultRequestHeaders.Add(TunnelProtocol.RelayHeader, TunnelProtocol.RelayHeaderValue);
                        string[] gets = new string[]
                        {
                            "/v1.0/api/context/instructions?workingDirectory=" + wd,
                            "/v1.0/api/files/complete?workingDirectory=" + wd + "&prefix=rea",
                            "/v1.0/api/usage/timeseries/split?dimension=model&range=day",
                            "/v1.0/api/memory?workingDirectory=" + wd,
                            "/v1.0/api/checkpoints?workingDirectory=" + wd,
                            "/v1.0/api/runs"
                        };
                        foreach (string path in gets)
                        {
                            using HttpResponseMessage response = await http.GetAsync("http://127.0.0.1:" + port + path, ct).ConfigureAwait(false);
                            MuxAssert.AreEqual(200, (int)response.StatusCode, "relayed GET " + path);
                        }

                        using (JsonDocument instructions = JsonDocument.Parse(await http.GetStringAsync("http://127.0.0.1:" + port + gets[0], ct).ConfigureAwait(false)))
                        {
                            MuxAssert.AreEqual(1, instructions.RootElement.GetProperty("Sources").GetArrayLength(), "MUX.md found");
                        }

                        using (JsonDocument complete = JsonDocument.Parse(await http.GetStringAsync("http://127.0.0.1:" + port + gets[1], ct).ConfigureAwait(false)))
                        {
                            MuxAssert.Contains("readme.txt", complete.RootElement.GetProperty("Paths").GetRawText(), "completion lists the file");
                        }

                        using HttpResponseMessage file = await PostJsonAsync(http, port, "/v1.0/api/context/file", "{\"Path\":\"readme.txt\",\"Content\":\"hello\"}", ct).ConfigureAwait(false);
                        MuxAssert.AreEqual(200, (int)file.StatusCode, "relayed context/file");
                        using HttpResponseMessage remember = await PostJsonAsync(http, port, "/v1.0/api/memory", JsonSerializer.Serialize(new Dictionary<string, string> { ["Name"] = "parity-note", ["Description"] = "d", ["Content"] = "remember this", ["WorkingDirectory"] = workDir }), ct).ConfigureAwait(false);
                        MuxAssert.AreEqual(201, (int)remember.StatusCode, "relayed memory save (created): " + await remember.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
                        using HttpResponseMessage undo = await PostJsonAsync(http, port, "/v1.0/api/checkpoints/undo", JsonSerializer.Serialize(new Dictionary<string, string> { ["WorkingDirectory"] = workDir }), ct).ConfigureAwait(false);
                        MuxAssert.AreEqual(200, (int)undo.StatusCode, "relayed undo (not a repository: nothing restored)");
                        using HttpResponseMessage load = await PostJsonAsync(http, port, "/v1.0/api/model/load", "{\"Endpoint\":\"scripted\",\"Messages\":[]}", ct).ConfigureAwait(false);
                        MuxAssert.AreEqual(200, (int)load.StatusCode, "relayed model load");
                    }).ConfigureAwait(false);
                }),
                new TestCaseDescriptor(Suite, "KeybindingCommands", "The command list shows defaults and the effect of overrides", async (CancellationToken ct) =>
                {
                    using ParityScriptedLlm llm = ParityScriptedLlm.Start();
                    await WithHubAsync(llm, async (MuxServer server, int port, string workDir) =>
                    {
                        using HttpClient http = Client();
                        JsonElement save = await FindCommandAsync(http, port, "mux.save", ct).ConfigureAwait(false);
                        MuxAssert.AreEqual("ctrl+s", save.GetProperty("DefaultChord").GetString(), "default chord");
                        MuxAssert.AreEqual("ctrl+s", save.GetProperty("EffectiveChord").GetString(), "effective is the default");
                        MuxAssert.IsFalse(save.GetProperty("Overridden").GetBoolean(), "not overridden");

                        using HttpResponseMessage put = await PutJsonAsync(http, port, "/v1.0/api/keybindings", "{\"Items\":[{\"CommandId\":\"mux.save\",\"Chord\":\"ctrl+shift+s\"},{\"CommandId\":\"mux.quit\",\"Chord\":\"\"}]}", ct).ConfigureAwait(false);
                        MuxAssert.AreEqual(200, (int)put.StatusCode, "override saved");
                        JsonElement rebound = await FindCommandAsync(http, port, "mux.save", ct).ConfigureAwait(false);
                        MuxAssert.AreEqual("ctrl+shift+s", rebound.GetProperty("EffectiveChord").GetString(), "rebound");
                        MuxAssert.IsTrue(rebound.GetProperty("Overridden").GetBoolean(), "overridden");
                        JsonElement unbound = await FindCommandAsync(http, port, "mux.quit", ct).ConfigureAwait(false);
                        MuxAssert.IsTrue(!unbound.TryGetProperty("EffectiveChord", out JsonElement chord) || chord.ValueKind == JsonValueKind.Null, "unbound (null or omitted)");
                    }).ConfigureAwait(false);
                }),
            });
        }

        private static HttpClient Client()
        {
            HttpClient http = new HttpClient { Timeout = TimeSpan.FromSeconds(90) };
            http.DefaultRequestHeaders.Add("Authorization", "Bearer " + Key);
            return http;
        }

        private static async Task<JsonElement> FindCommandAsync(HttpClient http, int port, string id, CancellationToken ct)
        {
            using JsonDocument doc = JsonDocument.Parse(await http.GetStringAsync("http://127.0.0.1:" + port + "/v1.0/api/keybindings/commands", ct).ConfigureAwait(false));
            foreach (JsonElement item in doc.RootElement.GetProperty("Items").EnumerateArray())
            {
                if (item.GetProperty("CommandId").GetString() == id) return item.Clone();
            }

            throw new InvalidOperationException("Command not listed: " + id);
        }

        private static async Task<HttpResponseMessage> PostJsonAsync(HttpClient http, int port, string path, string json, CancellationToken ct)
        {
            using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, "http://127.0.0.1:" + port + path) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
            return await http.SendAsync(request, ct).ConfigureAwait(false);
        }

        private static async Task<HttpResponseMessage> PutJsonAsync(HttpClient http, int port, string path, string json, CancellationToken ct)
        {
            using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Put, "http://127.0.0.1:" + port + path) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
            return await http.SendAsync(request, ct).ConfigureAwait(false);
        }

        private static Task<ParityStreamResult> StreamAsync(HttpClient http, int port, string workDir, string prompt, bool relayed, ParityAnswer? answer, CancellationToken ct)
        {
            List<Dictionary<string, object?>> messages = new List<Dictionary<string, object?>>
            {
                new Dictionary<string, object?> { ["Role"] = "user", ["Content"] = prompt }
            };
            return StreamMessagesAsync(http, port, workDir, "scripted", messages, relayed, answer, ct);
        }

        // Runs one streaming chat turn, collecting the events the suite checks. When a question arrives and an answer is
        // given, answers it over REST (relayed when the turn was) so the run can finish.
        private static async Task<ParityStreamResult> StreamMessagesAsync(HttpClient http, int port, string workDir, string endpoint, List<Dictionary<string, object?>> messages, bool relayed, ParityAnswer? answer, CancellationToken ct, bool askUser = true)
        {
            string payload = JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["Endpoint"] = endpoint,
                ["WorkingDirectory"] = workDir,
                ["AskUser"] = askUser,
                ["Messages"] = messages
            });
            using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, "http://127.0.0.1:" + port + "/v1.0/api/chat/stream")
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };
            if (relayed) request.Headers.Add(TunnelProtocol.RelayHeader, TunnelProtocol.RelayHeaderValue);
            using HttpResponseMessage response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            MuxAssert.AreEqual(200, (int)response.StatusCode, "stream status");

            ParityStreamResult result = new ParityStreamResult();
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
                switch (currentEvent)
                {
                    case "token":
                        text.Append(JsonSerializer.Deserialize<string>(data));
                        break;
                    case "run":
                        using (JsonDocument run = JsonDocument.Parse(data)) result.SessionId = run.RootElement.GetProperty("SessionId").GetString() ?? string.Empty;
                        break;
                    case "tool":
                        using (JsonDocument tool = JsonDocument.Parse(data)) result.Tools.Add(tool.RootElement.Clone());
                        break;
                    case "attachments":
                        using (JsonDocument attached = JsonDocument.Parse(data)) result.Attachments.Add(attached.RootElement.Clone());
                        break;
                    case "compacted":
                        using (JsonDocument compacted = JsonDocument.Parse(data)) result.Compacted.Add(compacted.RootElement.Clone());
                        break;
                    case "ask_user":
                        using (JsonDocument question = JsonDocument.Parse(data))
                        {
                            result.Questions.Add(question.RootElement.Clone());
                            if (answer != null)
                            {
                                string body = JsonSerializer.Serialize(new Dictionary<string, object?>
                                {
                                    ["RunId"] = question.RootElement.GetProperty("RunId").GetString(),
                                    ["QuestionId"] = question.RootElement.GetProperty("QuestionId").GetString(),
                                    ["Answers"] = answer.Labels,
                                    ["Skip"] = answer.Skip
                                });
                                using HttpRequestMessage post = new HttpRequestMessage(HttpMethod.Post, "http://127.0.0.1:" + port + "/v1.0/api/chat/answer")
                                {
                                    Content = new StringContent(body, Encoding.UTF8, "application/json")
                                };
                                if (relayed) post.Headers.Add(TunnelProtocol.RelayHeader, TunnelProtocol.RelayHeaderValue);
                                using HttpResponseMessage answered = await http.SendAsync(post, ct).ConfigureAwait(false);
                                MuxAssert.AreEqual(200, (int)answered.StatusCode, "answer accepted");
                            }
                        }

                        break;
                    case "done":
                    case "error":
                        text.Append(' ').Append(data);
                        break;
                }
            }

            result.Text = text.ToString();
            return result;
        }

        private static async Task WithHubAsync(ParityScriptedLlm llm, Func<MuxServer, int, string, Task> body)
        {
            string dir = Path.Combine(Path.GetTempPath(), "mux-parity-" + Guid.NewGuid().ToString("N"));
            string workDir = Path.Combine(dir, "work");
            Directory.CreateDirectory(workDir);
            string? original = Environment.GetEnvironmentVariable("MUX_CONFIG_DIR");
            Environment.SetEnvironmentVariable("MUX_CONFIG_DIR", dir);
            List<EndpointConfig> endpoints = new List<EndpointConfig>
            {
                new EndpointConfig { Name = "scripted", AdapterType = AdapterTypeEnum.OpenAiCompatible, BaseUrl = llm.BaseUrl, Model = "scripted", IsDefault = true },
                new EndpointConfig { Name = "small", AdapterType = AdapterTypeEnum.OpenAiCompatible, BaseUrl = llm.BaseUrl, Model = "scripted", ContextWindow = 32768 }
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
                    MuxServer candidate = new MuxServer(rest, "9.9.9-test", new SessionStore(Path.Combine(dir, "sessions")), () => endpoints, null, null, null, true);
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
                    try { server.Processes.StopAll(); } catch (Exception) { }
                    server.Stop();
                    server.Dispose();
                }

                Environment.SetEnvironmentVariable("MUX_CONFIG_DIR", original);
                try { Directory.Delete(dir, true); } catch (Exception) { }
            }
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
