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
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Enums;
    using Mux.Core.Models;
    using Mux.Core.Runs;
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
                    }),
                    new TestCaseDescriptor("MuxServerRoutes", "OpenApiAndSwagger", "OpenAPI document and Swagger UI are complete and unauthenticated", (CancellationToken ct) =>
                    {
                        string tempSessions = Path.Combine(Path.GetTempPath(), "mux-test-" + Guid.NewGuid().ToString("N"));

                        // A key IS configured: the OpenAPI/Swagger routes must still be reachable without it.
                        RestServerSettings rest = new RestServerSettings { Hostname = "127.0.0.1", ApiKey = "testkey123" };
                        List<EndpointConfig> endpoints = new List<EndpointConfig>
                        {
                            new EndpointConfig { Name = "unit-ollama", AdapterType = AdapterTypeEnum.Ollama, BaseUrl = "http://localhost:11434", Model = "gemma3:4b", IsDefault = true }
                        };

                        MuxServer? server = null;
                        int port = 0;
                        for (int bindAttempt = 0; bindAttempt < 10 && server == null; bindAttempt++)
                        {
                            port = FreeLoopbackPort();
                            rest.Port = port;
                            MuxServer candidate = new MuxServer(rest, "9.9.9-test", new SessionStore(tempSessions), () => endpoints, null);
                            try { candidate.Start(); server = candidate; }
                            catch (Exception) { candidate.Dispose(); Thread.Sleep(50); }
                        }

                        MuxAssert.IsNotNull(server, "server bound to a loopback port");
                        string baseUrl = "http://127.0.0.1:" + port;

                        try
                        {
                            using HttpClient http = new HttpClient();
                            http.Timeout = TimeSpan.FromSeconds(5);

                            // The OpenAPI document is served anonymously (no Authorization header), with a readiness retry.
                            string doc = string.Empty;
                            int docStatus = 0;
                            for (int attempt = 0; attempt < 20; attempt++)
                            {
                                try
                                {
                                    HttpResponseMessage r = http.GetAsync(baseUrl + "/openapi.json").GetAwaiter().GetResult();
                                    docStatus = (int)r.StatusCode;
                                    doc = r.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                                    break;
                                }
                                catch (Exception) { Thread.Sleep(100); }
                            }

                            MuxAssert.AreEqual(200, docStatus, "OpenApiStatus (unauthenticated)");

                            // It parses as JSON and is a well-formed OpenAPI 3 document.
                            using System.Text.Json.JsonDocument parsed = System.Text.Json.JsonDocument.Parse(doc);
                            System.Text.Json.JsonElement root = parsed.RootElement;
                            MuxAssert.AreEqual("3.0.3", root.GetProperty("openapi").GetString(), "OpenApiVersion");
                            MuxAssert.AreEqual("mux Local API", root.GetProperty("info").GetProperty("title").GetString(), "OpenApiTitle");
                            MuxAssert.AreEqual("9.9.9-test", root.GetProperty("info").GetProperty("version").GetString(), "OpenApiDocVersion");

                            // Paths are documented (a representative sample across registrars).
                            System.Text.Json.JsonElement paths = root.GetProperty("paths");
                            foreach (string p in new[] { "/v1.0/api/health", "/v1.0/api/endpoints", "/v1.0/api/chat", "/v1.0/api/sessions", "/v1.0/api/usage/summary", "/v1.0/api/runs", "/v1.0/api/prompts/catalog" })
                            {
                                MuxAssert.IsTrue(paths.TryGetProperty(p, out _), "path documented: " + p);
                            }

                            // The chat operation carries a summary, a tag, and a request body referencing the ChatRequest schema.
                            System.Text.Json.JsonElement chatPost = paths.GetProperty("/v1.0/api/chat").GetProperty("post");
                            MuxAssert.IsTrue(chatPost.TryGetProperty("summary", out _), "chat has a summary");
                            MuxAssert.IsTrue(chatPost.TryGetProperty("requestBody", out _), "chat has a request body");
                            MuxAssert.Contains("ChatRequest", chatPost.GetProperty("requestBody").ToString(), "chat request body references ChatRequest");

                            // Component schemas and the bearer security scheme are present, and schemas carry examples.
                            System.Text.Json.JsonElement components = root.GetProperty("components");
                            System.Text.Json.JsonElement schemas = components.GetProperty("schemas");
                            foreach (string schema in new[] { "ChatRequest", "EndpointDto", "SessionSaveRequest", "ApiError", "SettingsDto", "RunStateReply", "RunSummaryDto", "PromptCatalogEntryDto", "PromptOverrideDto" })
                            {
                                MuxAssert.IsTrue(schemas.TryGetProperty(schema, out _), "component schema present: " + schema);
                            }
                            MuxAssert.IsTrue(schemas.GetProperty("ChatRequest").TryGetProperty("example", out _), "ChatRequest carries an example object");
                            MuxAssert.IsTrue(components.GetProperty("securitySchemes").TryGetProperty("bearerAuth", out _), "bearer security scheme present");

                            // The Swagger UI is served (HTML) and is also anonymous.
                            HttpResponseMessage ui = http.GetAsync(baseUrl + "/swagger").GetAwaiter().GetResult();
                            string uiBody = ui.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                            MuxAssert.AreEqual(200, (int)ui.StatusCode, "SwaggerStatus (unauthenticated)");
                            MuxAssert.Contains("swagger-ui", uiBody, "SwaggerBody");
                            MuxAssert.Contains("mux Local API", uiBody, "SwaggerTitle");
                        }
                        finally
                        {
                            server?.Stop();
                            server?.Dispose();
                            try { if (Directory.Exists(tempSessions)) Directory.Delete(tempSessions, true); } catch (Exception) { }
                        }

                        return Task.CompletedTask;
                    }),
                    new TestCaseDescriptor("MuxServerRoutes", "PromptCatalogRoutesAndProfileDto", "The prompt catalog lists/overrides/resets over HTTP and the profile DTO round-trips all three prompts", (CancellationToken ct) =>
                    {
                        string tempSessions = Path.Combine(Path.GetTempPath(), "mux-test-" + Guid.NewGuid().ToString("N"));
                        string tempConfig = Path.Combine(Path.GetTempPath(), "mux-cfg-" + Guid.NewGuid().ToString("N"));
                        Directory.CreateDirectory(tempConfig);
                        // Isolate config so the override/profile writes never touch the developer's real ~/.mux.
                        string? originalConfig = Environment.GetEnvironmentVariable("MUX_CONFIG_DIR");
                        Environment.SetEnvironmentVariable("MUX_CONFIG_DIR", tempConfig);

                        RestServerSettings rest = new RestServerSettings { Hostname = "127.0.0.1", ApiKey = "testkey123" };
                        List<EndpointConfig> endpoints = new List<EndpointConfig>
                        {
                            new EndpointConfig { Name = "unit-ollama", AdapterType = AdapterTypeEnum.Ollama, BaseUrl = "http://localhost:11434", Model = "gemma3:4b", IsDefault = true }
                        };

                        MuxServer? server = null;
                        int port = 0;
                        for (int bindAttempt = 0; bindAttempt < 10 && server == null; bindAttempt++)
                        {
                            port = FreeLoopbackPort();
                            rest.Port = port;
                            MuxServer candidate = new MuxServer(rest, "9.9.9-test", new SessionStore(tempSessions), () => endpoints, null);
                            try { candidate.Start(); server = candidate; }
                            catch (Exception) { candidate.Dispose(); Thread.Sleep(50); }
                        }

                        MuxAssert.IsNotNull(server, "server bound to a loopback port");
                        string baseUrl = "http://127.0.0.1:" + port;

                        try
                        {
                            using HttpClient http = new HttpClient();
                            http.Timeout = TimeSpan.FromSeconds(5);
                            for (int attempt = 0; attempt < 20; attempt++)
                            {
                                try { http.GetAsync(baseUrl + "/v1.0/api/health").GetAwaiter().GetResult(); break; }
                                catch (Exception) { Thread.Sleep(100); }
                            }

                            // Catalog GET without a key -> 401.
                            HttpResponseMessage noKey = http.GetAsync(baseUrl + "/v1.0/api/prompts/catalog").GetAwaiter().GetResult();
                            MuxAssert.AreEqual(401, (int)noKey.StatusCode, "CatalogNoKeyStatus");

                            // Catalog GET with the key -> 200 and lists representative entries.
                            string listBody = GetAuthed(http, baseUrl + "/v1.0/api/prompts/catalog");
                            MuxAssert.Contains("tool.read_file", listBody, "catalog lists a tool description");
                            MuxAssert.Contains("compaction.user", listBody, "catalog lists the compaction framing");

                            // Set a valid override -> 200, reported overridden, effective reflects it.
                            string setResp = PutAuthed(http, baseUrl + "/v1.0/api/prompts/catalog", "{\"key\":\"compaction.user\",\"content\":\"Summarize the earlier turns below:\"}", out int setStatus);
                            MuxAssert.AreEqual(200, setStatus, "SetOverrideStatus");
                            MuxAssert.Contains("\"Overridden\":true", setResp, "override in effect");
                            MuxAssert.Contains("Summarize the earlier turns below:", setResp, "effective reflects the override");

                            // GET reflects the override.
                            MuxAssert.Contains("Summarize the earlier turns below:", GetAuthed(http, baseUrl + "/v1.0/api/prompts/catalog"), "list reflects the override");

                            // Reset with blank content -> 200, no longer overridden.
                            string resetResp = PutAuthed(http, baseUrl + "/v1.0/api/prompts/catalog", "{\"key\":\"compaction.user\",\"content\":\"\"}", out int resetStatus);
                            MuxAssert.AreEqual(200, resetStatus, "ResetStatus");
                            MuxAssert.Contains("\"Overridden\":false", resetResp, "override cleared");

                            // Dropping a required placeholder is rejected.
                            PutAuthed(http, baseUrl + "/v1.0/api/prompts/catalog", "{\"key\":\"tool.run_process\",\"content\":\"no tokens here\"}", out int badPlaceholder);
                            MuxAssert.AreEqual(400, badPlaceholder, "MissingPlaceholderRejected");

                            // A profile-scoped key has no operational override.
                            PutAuthed(http, baseUrl + "/v1.0/api/prompts/catalog", "{\"key\":\"system\",\"content\":\"x\"}", out int profileScoped);
                            MuxAssert.AreEqual(400, profileScoped, "ProfileScopeRejected");

                            // An unknown key is rejected.
                            PutAuthed(http, baseUrl + "/v1.0/api/prompts/catalog", "{\"key\":\"no.such.key\",\"content\":\"x\"}", out int unknownKey);
                            MuxAssert.AreEqual(400, unknownKey, "UnknownKeyRejected");

                            // Profile DTO gap fix: all three prompt fields round-trip through PUT and GET.
                            string profileBody = "{\"items\":[{\"name\":\"Custom\",\"isActive\":true,\"systemPrompt\":\"S\",\"toolsDisabledPrompt\":\"TD\",\"compactionPrompt\":\"CP\"}]}";
                            string putProfileResp = PutAuthed(http, baseUrl + "/v1.0/api/prompts", profileBody, out int putProfileStatus);
                            MuxAssert.AreEqual(200, putProfileStatus, "PutProfileStatus");
                            MuxAssert.Contains("\"CompactionPrompt\":\"CP\"", putProfileResp, "compaction prompt round-trips in the PUT response");
                            string getProfile = GetAuthed(http, baseUrl + "/v1.0/api/prompts");
                            MuxAssert.Contains("\"ToolsDisabledPrompt\":\"TD\"", getProfile, "tools-disabled prompt exposed on GET");
                            MuxAssert.Contains("\"CompactionPrompt\":\"CP\"", getProfile, "compaction prompt exposed on GET");
                        }
                        finally
                        {
                            server?.Stop();
                            server?.Dispose();
                            Environment.SetEnvironmentVariable("MUX_CONFIG_DIR", originalConfig);
                            try { if (Directory.Exists(tempSessions)) Directory.Delete(tempSessions, true); } catch (Exception) { }
                            try { if (Directory.Exists(tempConfig)) Directory.Delete(tempConfig, true); } catch (Exception) { }
                        }

                        return Task.CompletedTask;
                    }),
                    new TestCaseDescriptor("MuxServerRoutes", "RunRoutesListInspectCancel", "Run routes gate on auth, list runs, and 404 for unknown ids", (CancellationToken ct) =>
                    {
                        string tempSessions = Path.Combine(Path.GetTempPath(), "mux-test-" + Guid.NewGuid().ToString("N"));
                        RestServerSettings rest = new RestServerSettings { Hostname = "127.0.0.1", ApiKey = "testkey123" };
                        List<EndpointConfig> endpoints = new List<EndpointConfig>
                        {
                            new EndpointConfig { Name = "unit-ollama", AdapterType = AdapterTypeEnum.Ollama, BaseUrl = "http://localhost:11434", Model = "gemma3:4b", IsDefault = true }
                        };

                        MuxServer? server = null;
                        int port = 0;
                        for (int bindAttempt = 0; bindAttempt < 10 && server == null; bindAttempt++)
                        {
                            port = FreeLoopbackPort();
                            rest.Port = port;
                            MuxServer candidate = new MuxServer(rest, "9.9.9-test", new SessionStore(tempSessions), () => endpoints, null);
                            try { candidate.Start(); server = candidate; }
                            catch (Exception) { candidate.Dispose(); Thread.Sleep(50); }
                        }

                        MuxAssert.IsNotNull(server, "server bound to a loopback port");
                        string baseUrl = "http://127.0.0.1:" + port;

                        try
                        {
                            using HttpClient http = new HttpClient();
                            http.Timeout = TimeSpan.FromSeconds(5);

                            // Readiness.
                            for (int attempt = 0; attempt < 20; attempt++)
                            {
                                try { http.GetAsync(baseUrl + "/v1.0/api/health").GetAwaiter().GetResult(); break; }
                                catch (Exception) { Thread.Sleep(100); }
                            }

                            // List without a key -> 401 (negative).
                            HttpResponseMessage listNoKey = http.GetAsync(baseUrl + "/v1.0/api/runs").GetAwaiter().GetResult();
                            MuxAssert.AreEqual(401, (int)listNoKey.StatusCode, "RunsListNoKeyStatus");

                            // List with the key -> 200 and the list envelope (no active runs yet).
                            using HttpRequestMessage listReq = new HttpRequestMessage(HttpMethod.Get, baseUrl + "/v1.0/api/runs");
                            listReq.Headers.Add("Authorization", "Bearer testkey123");
                            HttpResponseMessage listRes = http.SendAsync(listReq).GetAwaiter().GetResult();
                            string listBody = listRes.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                            MuxAssert.AreEqual(200, (int)listRes.StatusCode, "RunsListStatus");
                            MuxAssert.Contains("Items", listBody, "RunsListEnvelope");

                            // Inspect an unknown run -> 404 (negative).
                            using HttpRequestMessage getReq = new HttpRequestMessage(HttpMethod.Get, baseUrl + "/v1.0/api/runs/nope");
                            getReq.Headers.Add("Authorization", "Bearer testkey123");
                            HttpResponseMessage getRes = http.SendAsync(getReq).GetAwaiter().GetResult();
                            MuxAssert.AreEqual(404, (int)getRes.StatusCode, "RunGetUnknownStatus");

                            // Cancel without a key -> 401 (negative).
                            HttpResponseMessage cancelNoKey = http.PostAsync(baseUrl + "/v1.0/api/runs/nope/cancel", new StringContent(string.Empty)).GetAwaiter().GetResult();
                            MuxAssert.AreEqual(401, (int)cancelNoKey.StatusCode, "RunCancelNoKeyStatus");

                            // Cancel an unknown run with the key -> 404 (negative).
                            using HttpRequestMessage cancelReq = new HttpRequestMessage(HttpMethod.Post, baseUrl + "/v1.0/api/runs/nope/cancel");
                            cancelReq.Headers.Add("Authorization", "Bearer testkey123");
                            HttpResponseMessage cancelRes = http.SendAsync(cancelReq).GetAwaiter().GetResult();
                            MuxAssert.AreEqual(404, (int)cancelRes.StatusCode, "RunCancelUnknownStatus");
                        }
                        finally
                        {
                            server?.Stop();
                            server?.Dispose();
                            try { if (Directory.Exists(tempSessions)) Directory.Delete(tempSessions, true); } catch (Exception) { }
                        }

                        return Task.CompletedTask;
                    }),
                    new TestCaseDescriptor("MuxServerRoutes", "InjectedRegistryExposesHostRuns", "A server built with a shared registry exposes runs recorded by the host (the desktop producer path)", (CancellationToken ct) =>
                    {
                        string tempSessions = Path.Combine(Path.GetTempPath(), "mux-test-" + Guid.NewGuid().ToString("N"));
                        RestServerSettings rest = new RestServerSettings { Hostname = "127.0.0.1", ApiKey = "testkey123" };
                        List<EndpointConfig> endpoints = new List<EndpointConfig>();

                        // A registry owned by the "host" (as the desktop app owns EmbeddedServerService.SharedRuns).
                        using RunRegistry shared = new RunRegistry();
                        RunHandle hostRun = shared.Create("mirror-run", "sess-x", "local", "m", ct);
                        hostRun.ApplyEvent(new Mux.Core.Agent.AssistantTextEvent { Text = "in-process output" });

                        MuxServer? server = null;
                        int port = 0;
                        for (int bindAttempt = 0; bindAttempt < 10 && server == null; bindAttempt++)
                        {
                            port = FreeLoopbackPort();
                            rest.Port = port;
                            MuxServer candidate = new MuxServer(rest, "9.9.9-test", new SessionStore(tempSessions), () => endpoints, null, null, null, false, shared);
                            try { candidate.Start(); server = candidate; }
                            catch (Exception) { candidate.Dispose(); Thread.Sleep(50); }
                        }

                        MuxAssert.IsNotNull(server, "server bound to a loopback port");
                        string baseUrl = "http://127.0.0.1:" + port;

                        try
                        {
                            using HttpClient http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
                            for (int attempt = 0; attempt < 20; attempt++)
                            {
                                try { http.GetAsync(baseUrl + "/v1.0/api/health").GetAwaiter().GetResult(); break; }
                                catch (Exception) { Thread.Sleep(100); }
                            }

                            using HttpRequestMessage listReq = new HttpRequestMessage(HttpMethod.Get, baseUrl + "/v1.0/api/runs");
                            listReq.Headers.Add("Authorization", "Bearer testkey123");
                            HttpResponseMessage listRes = http.SendAsync(listReq).GetAwaiter().GetResult();
                            string listBody = listRes.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                            MuxAssert.AreEqual(200, (int)listRes.StatusCode, "SharedRunsListStatus");
                            MuxAssert.Contains("mirror-run", listBody, "host-recorded run is exposed by the server");

                            using HttpRequestMessage getReq = new HttpRequestMessage(HttpMethod.Get, baseUrl + "/v1.0/api/runs/mirror-run");
                            getReq.Headers.Add("Authorization", "Bearer testkey123");
                            HttpResponseMessage getRes = http.SendAsync(getReq).GetAwaiter().GetResult();
                            MuxAssert.AreEqual(200, (int)getRes.StatusCode, "SharedRunDetailStatus");
                        }
                        finally
                        {
                            server?.Stop();
                            server?.Dispose();
                            try { if (Directory.Exists(tempSessions)) Directory.Delete(tempSessions, true); } catch (Exception) { }
                        }

                        return Task.CompletedTask;
                    }),
                    new TestCaseDescriptor("MuxServerRoutes", "WebSocketBridgeAuthAndSubscribe", "The WebSocket bridge authenticates the upgrade and answers subscribe frames", async (CancellationToken ct) =>
                    {
                        string tempSessions = Path.Combine(Path.GetTempPath(), "mux-test-" + Guid.NewGuid().ToString("N"));
                        RestServerSettings rest = new RestServerSettings { Hostname = "127.0.0.1", ApiKey = "testkey123" };
                        List<EndpointConfig> endpoints = new List<EndpointConfig>
                        {
                            new EndpointConfig { Name = "unit-ollama", AdapterType = AdapterTypeEnum.Ollama, BaseUrl = "http://localhost:11434", Model = "gemma3:4b", IsDefault = true }
                        };

                        MuxServer? server = null;
                        int port = 0;
                        for (int bindAttempt = 0; bindAttempt < 10 && server == null; bindAttempt++)
                        {
                            port = FreeLoopbackPort();
                            rest.Port = port;
                            MuxServer candidate = new MuxServer(rest, "9.9.9-test", new SessionStore(tempSessions), () => endpoints, null);
                            try { candidate.Start(); server = candidate; }
                            catch (Exception) { candidate.Dispose(); Thread.Sleep(50); }
                        }

                        MuxAssert.IsNotNull(server, "server bound to a loopback port");

                        try
                        {
                            // Readiness.
                            using (HttpClient http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) })
                            {
                                for (int attempt = 0; attempt < 20; attempt++)
                                {
                                    try { http.GetAsync("http://127.0.0.1:" + port + "/v1.0/api/health").GetAwaiter().GetResult(); break; }
                                    catch (Exception) { Thread.Sleep(100); }
                                }
                            }

                            // Unauthorized: connect without the key -> the first frame is an unauthorized error (negative).
                            using (ClientWebSocket wsNoKey = new ClientWebSocket())
                            {
                                await wsNoKey.ConnectAsync(new Uri("ws://127.0.0.1:" + port + "/v1.0/ws"), ct).ConfigureAwait(false);
                                string first = await ReceiveTextAsync(wsNoKey, ct).ConfigureAwait(false);
                                MuxAssert.Contains("unauthorized", first, "unauthenticated upgrade is rejected with an error frame");
                            }

                            // Authorized: connect with the key -> server.connected, then subscribe to an unknown run -> not_found.
                            using (ClientWebSocket ws = new ClientWebSocket())
                            {
                                await ws.ConnectAsync(new Uri("ws://127.0.0.1:" + port + "/v1.0/ws?apiKey=testkey123"), ct).ConfigureAwait(false);
                                string connected = await ReceiveTextAsync(ws, ct).ConfigureAwait(false);
                                MuxAssert.Contains("server.connected", connected, "authorized connect announces the server");

                                // A malformed frame is ignored (must not crash the session).
                                await SendTextAsync(ws, "this is not json", ct).ConfigureAwait(false);

                                await SendTextAsync(ws, "{\"action\":\"subscribe\",\"runId\":\"does-not-exist\"}", ct).ConfigureAwait(false);
                                string reply = await ReceiveTextAsync(ws, ct).ConfigureAwait(false);
                                MuxAssert.Contains("not_found", reply, "subscribing to an unknown run returns a not_found error (malformed frame was ignored)");
                            }

                            // Publish a run's events over the socket (the in-process producer path); the hub
                            // materializes the run and relays frames to a separate subscriber.
                            using (ClientWebSocket producer = new ClientWebSocket())
                            {
                                await producer.ConnectAsync(new Uri("ws://127.0.0.1:" + port + "/v1.0/ws?apiKey=testkey123"), ct).ConfigureAwait(false);
                                await ReceiveTextAsync(producer, ct).ConfigureAwait(false); // server.connected
                                await SendTextAsync(producer, "{\"action\":\"publish\",\"runId\":\"pub-1\",\"sessionId\":\"sess-pub\",\"endpointName\":\"local\",\"model\":\"m\",\"frame\":\"{\\\"eventType\\\":\\\"assistant_text\\\",\\\"text\\\":\\\"published hello\\\"}\"}", ct).ConfigureAwait(false);

                                // The published run now shows up over REST.
                                bool appeared = false;
                                using HttpClient http2 = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
                                for (int i = 0; i < 30 && !appeared; i++)
                                {
                                    using HttpRequestMessage runsReq = new HttpRequestMessage(HttpMethod.Get, "http://127.0.0.1:" + port + "/v1.0/api/runs");
                                    runsReq.Headers.Add("Authorization", "Bearer testkey123");
                                    string runsBody = http2.SendAsync(runsReq).GetAwaiter().GetResult().Content.ReadAsStringAsync().GetAwaiter().GetResult();
                                    if (runsBody.Contains("pub-1")) { appeared = true; break; }
                                    Thread.Sleep(100);
                                }
                                MuxAssert.IsTrue(appeared, "a published run is registered in the hub and listed over REST");

                                // A subscriber to that run replays the published frame.
                                using ClientWebSocket sub2 = new ClientWebSocket();
                                await sub2.ConnectAsync(new Uri("ws://127.0.0.1:" + port + "/v1.0/ws?apiKey=testkey123"), ct).ConfigureAwait(false);
                                await ReceiveTextAsync(sub2, ct).ConfigureAwait(false); // server.connected
                                await SendTextAsync(sub2, "{\"action\":\"subscribe\",\"runId\":\"pub-1\"}", ct).ConfigureAwait(false);
                                string mirrored = await ReceiveTextAsync(sub2, ct).ConfigureAwait(false);
                                MuxAssert.Contains("published hello", mirrored, "a subscriber receives the published run's frames");
                            }

                            // Session-scoped subscribe with no run yet: no error, and a run that starts LATER
                            // for that session streams in (this is what makes "mirror on by default" work when
                            // you open an idle conversation).
                            using (ClientWebSocket sessionSub = new ClientWebSocket())
                            {
                                await sessionSub.ConnectAsync(new Uri("ws://127.0.0.1:" + port + "/v1.0/ws?apiKey=testkey123"), ct).ConfigureAwait(false);
                                await ReceiveTextAsync(sessionSub, ct).ConfigureAwait(false); // server.connected
                                await SendTextAsync(sessionSub, "{\"action\":\"subscribe\",\"sessionId\":\"sess-live\"}", ct).ConfigureAwait(false);
                                await Task.Delay(200, ct).ConfigureAwait(false); // let the session watcher register

                                using (ClientWebSocket producer2 = new ClientWebSocket())
                                {
                                    await producer2.ConnectAsync(new Uri("ws://127.0.0.1:" + port + "/v1.0/ws?apiKey=testkey123"), ct).ConfigureAwait(false);
                                    await ReceiveTextAsync(producer2, ct).ConfigureAwait(false); // server.connected
                                    await SendTextAsync(producer2, "{\"action\":\"publish\",\"runId\":\"live-1\",\"sessionId\":\"sess-live\",\"endpointName\":\"local\",\"model\":\"m\",\"frame\":\"{\\\"eventType\\\":\\\"assistant_text\\\",\\\"text\\\":\\\"live mirror text\\\"}\"}", ct).ConfigureAwait(false);
                                }

                                string streamed = await ReceiveTextAsync(sessionSub, ct).ConfigureAwait(false);
                                MuxAssert.Contains("live mirror text", streamed, "a session subscriber receives a run that starts after it subscribed");
                            }
                        }
                        finally
                        {
                            server?.Stop();
                            server?.Dispose();
                            try { if (Directory.Exists(tempSessions)) Directory.Delete(tempSessions, true); } catch (Exception) { }
                        }
                    }),
                    new TestCaseDescriptor("MuxServerRoutes", "SessionMirrorClientConsumesPublishedRun", "SessionMirrorClient (the desktop/TUI consumer) raises RunCompleted for a run published to the hub", async (CancellationToken ct) =>
                    {
                        string tempSessions = Path.Combine(Path.GetTempPath(), "mux-test-" + Guid.NewGuid().ToString("N"));
                        RestServerSettings rest = new RestServerSettings { Hostname = "127.0.0.1", ApiKey = "testkey123" };
                        List<EndpointConfig> endpoints = new List<EndpointConfig>();

                        MuxServer? server = null;
                        int port = 0;
                        for (int bindAttempt = 0; bindAttempt < 10 && server == null; bindAttempt++)
                        {
                            port = FreeLoopbackPort();
                            rest.Port = port;
                            MuxServer candidate = new MuxServer(rest, "9.9.9-test", new SessionStore(tempSessions), () => endpoints, null);
                            try { candidate.Start(); server = candidate; }
                            catch (Exception) { candidate.Dispose(); Thread.Sleep(50); }
                        }

                        MuxAssert.IsNotNull(server, "server bound to a loopback port");
                        string baseUrl = "http://127.0.0.1:" + port;

                        Mux.Core.Runs.SessionMirrorClient? mirror = null;
                        try
                        {
                            using (HttpClient http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) })
                            {
                                for (int attempt = 0; attempt < 20; attempt++)
                                {
                                    try { http.GetAsync(baseUrl + "/v1.0/api/health").GetAwaiter().GetResult(); break; }
                                    catch (Exception) { Thread.Sleep(100); }
                                }
                            }

                            TaskCompletionSource<bool> completed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                            mirror = new Mux.Core.Runs.SessionMirrorClient(baseUrl, "testkey123");
                            mirror.RunCompleted += () => completed.TrySetResult(true);
                            await mirror.StartAsync("sess-consumer", ct).ConfigureAwait(false);
                            await Task.Delay(250, ct).ConfigureAwait(false); // let the session watcher register

                            using (ClientWebSocket producer = new ClientWebSocket())
                            {
                                await producer.ConnectAsync(new Uri("ws://127.0.0.1:" + port + "/v1.0/ws?apiKey=testkey123"), ct).ConfigureAwait(false);
                                await ReceiveTextAsync(producer, ct).ConfigureAwait(false); // server.connected
                                await SendTextAsync(producer, "{\"action\":\"publish\",\"runId\":\"cons-1\",\"sessionId\":\"sess-consumer\",\"endpointName\":\"local\",\"model\":\"m\",\"frame\":\"{\\\"eventType\\\":\\\"run_completed\\\",\\\"status\\\":\\\"completed\\\"}\"}", ct).ConfigureAwait(false);
                            }

                            Task finished = await Task.WhenAny(completed.Task, Task.Delay(TimeSpan.FromSeconds(4), ct)).ConfigureAwait(false);
                            MuxAssert.IsTrue(ReferenceEquals(finished, completed.Task) && completed.Task.IsCompleted, "the consumer client raised RunCompleted for the published run");
                        }
                        finally
                        {
                            if (mirror != null) { await mirror.DisposeAsync().ConfigureAwait(false); }
                            server?.Stop();
                            server?.Dispose();
                            try { if (Directory.Exists(tempSessions)) Directory.Delete(tempSessions, true); } catch (Exception) { }
                        }
                    }),
                    new TestCaseDescriptor("MuxServerRoutes", "AllSubscriberGetsSessionsChangedOnUpsert", "An `all` subscriber receives sessions_changed when a session is upserted over REST", async (CancellationToken ct) =>
                    {
                        string tempSessions = Path.Combine(Path.GetTempPath(), "mux-test-" + Guid.NewGuid().ToString("N"));
                        RestServerSettings rest = new RestServerSettings { Hostname = "127.0.0.1", ApiKey = "testkey123" };
                        List<EndpointConfig> endpoints = new List<EndpointConfig>();

                        MuxServer? server = null;
                        int port = 0;
                        for (int bindAttempt = 0; bindAttempt < 10 && server == null; bindAttempt++)
                        {
                            port = FreeLoopbackPort();
                            rest.Port = port;
                            MuxServer candidate = new MuxServer(rest, "9.9.9-test", new SessionStore(tempSessions), () => endpoints, null);
                            try { candidate.Start(); server = candidate; }
                            catch (Exception) { candidate.Dispose(); Thread.Sleep(50); }
                        }

                        MuxAssert.IsNotNull(server, "server bound to a loopback port");
                        string baseUrl = "http://127.0.0.1:" + port;

                        try
                        {
                            using HttpClient http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
                            for (int attempt = 0; attempt < 20; attempt++)
                            {
                                try { http.GetAsync(baseUrl + "/v1.0/api/health").GetAwaiter().GetResult(); break; }
                                catch (Exception) { Thread.Sleep(100); }
                            }

                            using ClientWebSocket ws = new ClientWebSocket();
                            await ws.ConnectAsync(new Uri("ws://127.0.0.1:" + port + "/v1.0/ws?apiKey=testkey123"), ct).ConfigureAwait(false);
                            await ReceiveTextAsync(ws, ct).ConfigureAwait(false); // server.connected
                            await SendTextAsync(ws, "{\"action\":\"subscribe\",\"all\":true}", ct).ConfigureAwait(false);
                            await Task.Delay(200, ct).ConfigureAwait(false);

                            using HttpRequestMessage put = new HttpRequestMessage(HttpMethod.Put, baseUrl + "/v1.0/api/sessions");
                            put.Headers.Add("Authorization", "Bearer testkey123");
                            put.Content = new StringContent("{\"id\":\"sc-1\",\"title\":\"T\",\"endpointName\":\"local\",\"model\":\"m\",\"messages\":[{\"role\":\"user\",\"content\":\"hi\"}]}", System.Text.Encoding.UTF8, "application/json");
                            http.SendAsync(put).GetAwaiter().GetResult();

                            string frame = await ReceiveTextAsync(ws, ct).ConfigureAwait(false);
                            MuxAssert.Contains("sessions_changed", frame, "the all-subscriber is notified of the list change");
                        }
                        finally
                        {
                            server?.Stop();
                            server?.Dispose();
                            try { if (Directory.Exists(tempSessions)) Directory.Delete(tempSessions, true); } catch (Exception) { }
                        }
                    }),
                    new TestCaseDescriptor("MuxServerRoutes", "SessionSubscriberGetsTranscriptChangedOnUpsert", "A session-scoped subscriber receives transcript_changed when that session's turn is persisted", async (CancellationToken ct) =>
                    {
                        // The heart of the cross-surface fix: persisting a turn for session S must tell a viewer
                        // subscribed to S to reload the OPEN transcript (not just refresh the list). This is what
                        // carries a turn made on one surface into a conversation held open on another.
                        string tempSessions = Path.Combine(Path.GetTempPath(), "mux-test-" + Guid.NewGuid().ToString("N"));
                        RestServerSettings rest = new RestServerSettings { Hostname = "127.0.0.1", ApiKey = "testkey123" };
                        List<EndpointConfig> endpoints = new List<EndpointConfig>();

                        MuxServer? server = null;
                        int port = 0;
                        for (int bindAttempt = 0; bindAttempt < 10 && server == null; bindAttempt++)
                        {
                            port = FreeLoopbackPort();
                            rest.Port = port;
                            MuxServer candidate = new MuxServer(rest, "9.9.9-test", new SessionStore(tempSessions), () => endpoints, null);
                            try { candidate.Start(); server = candidate; }
                            catch (Exception) { candidate.Dispose(); Thread.Sleep(50); }
                        }

                        MuxAssert.IsNotNull(server, "server bound to a loopback port");
                        string baseUrl = "http://127.0.0.1:" + port;

                        try
                        {
                            using HttpClient http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
                            for (int attempt = 0; attempt < 20; attempt++)
                            {
                                try { http.GetAsync(baseUrl + "/v1.0/api/health").GetAwaiter().GetResult(); break; }
                                catch (Exception) { Thread.Sleep(100); }
                            }

                            using ClientWebSocket ws = new ClientWebSocket();
                            await ws.ConnectAsync(new Uri("ws://127.0.0.1:" + port + "/v1.0/ws?apiKey=testkey123"), ct).ConfigureAwait(false);
                            await ReceiveTextAsync(ws, ct).ConfigureAwait(false); // server.connected
                            await SendTextAsync(ws, "{\"action\":\"subscribe\",\"sessionId\":\"tc-1\"}", ct).ConfigureAwait(false);
                            await Task.Delay(200, ct).ConfigureAwait(false); // let the transcript listener register

                            using HttpRequestMessage put = new HttpRequestMessage(HttpMethod.Put, baseUrl + "/v1.0/api/sessions");
                            put.Headers.Add("Authorization", "Bearer testkey123");
                            put.Content = new StringContent("{\"id\":\"tc-1\",\"title\":\"T\",\"endpointName\":\"local\",\"model\":\"m\",\"messages\":[{\"role\":\"user\",\"content\":\"hi\"}]}", System.Text.Encoding.UTF8, "application/json");
                            http.SendAsync(put).GetAwaiter().GetResult();

                            string frame = await ReceiveTextAsync(ws, ct).ConfigureAwait(false);
                            MuxAssert.Contains("transcript_changed", frame, "the session subscriber is told the transcript changed");
                            MuxAssert.Contains("tc-1", frame, "the transcript_changed frame carries the session id");
                        }
                        finally
                        {
                            server?.Stop();
                            server?.Dispose();
                            try { if (Directory.Exists(tempSessions)) Directory.Delete(tempSessions, true); } catch (Exception) { }
                        }
                    }),
                    new TestCaseDescriptor("MuxServerRoutes", "MirrorClientRaisesTranscriptChanged", "SessionMirrorClient raises TranscriptChanged when an in-process surface signals notify-transcript", async (CancellationToken ct) =>
                    {
                        // The in-process producer path: a TUI/desktop turn writes the store directly and sends a
                        // notify-transcript over the socket; a consumer mirror for that session must raise its
                        // TranscriptChanged event so the other surface reloads.
                        string tempSessions = Path.Combine(Path.GetTempPath(), "mux-test-" + Guid.NewGuid().ToString("N"));
                        RestServerSettings rest = new RestServerSettings { Hostname = "127.0.0.1", ApiKey = "testkey123" };
                        List<EndpointConfig> endpoints = new List<EndpointConfig>();

                        MuxServer? server = null;
                        int port = 0;
                        for (int bindAttempt = 0; bindAttempt < 10 && server == null; bindAttempt++)
                        {
                            port = FreeLoopbackPort();
                            rest.Port = port;
                            MuxServer candidate = new MuxServer(rest, "9.9.9-test", new SessionStore(tempSessions), () => endpoints, null);
                            try { candidate.Start(); server = candidate; }
                            catch (Exception) { candidate.Dispose(); Thread.Sleep(50); }
                        }

                        MuxAssert.IsNotNull(server, "server bound to a loopback port");
                        string baseUrl = "http://127.0.0.1:" + port;

                        Mux.Core.Runs.SessionMirrorClient? mirror = null;
                        try
                        {
                            using (HttpClient http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) })
                            {
                                for (int attempt = 0; attempt < 20; attempt++)
                                {
                                    try { http.GetAsync(baseUrl + "/v1.0/api/health").GetAwaiter().GetResult(); break; }
                                    catch (Exception) { Thread.Sleep(100); }
                                }
                            }

                            TaskCompletionSource<bool> changed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                            mirror = new Mux.Core.Runs.SessionMirrorClient(baseUrl, "testkey123");
                            mirror.TranscriptChanged += () => changed.TrySetResult(true);
                            await mirror.StartAsync("tc-mirror", ct).ConfigureAwait(false);
                            await Task.Delay(250, ct).ConfigureAwait(false); // let the transcript listener register

                            using (ClientWebSocket producer = new ClientWebSocket())
                            {
                                await producer.ConnectAsync(new Uri("ws://127.0.0.1:" + port + "/v1.0/ws?apiKey=testkey123"), ct).ConfigureAwait(false);
                                await ReceiveTextAsync(producer, ct).ConfigureAwait(false); // server.connected
                                await SendTextAsync(producer, "{\"action\":\"notify-transcript\",\"sessionId\":\"tc-mirror\"}", ct).ConfigureAwait(false);
                            }

                            Task finished = await Task.WhenAny(changed.Task, Task.Delay(TimeSpan.FromSeconds(4), ct)).ConfigureAwait(false);
                            MuxAssert.IsTrue(ReferenceEquals(finished, changed.Task) && changed.Task.IsCompleted, "the consumer mirror raised TranscriptChanged for the notify-transcript signal");
                        }
                        finally
                        {
                            if (mirror != null) { await mirror.DisposeAsync().ConfigureAwait(false); }
                            server?.Stop();
                            server?.Dispose();
                            try { if (Directory.Exists(tempSessions)) Directory.Delete(tempSessions, true); } catch (Exception) { }
                        }
                    }),
                    new TestCaseDescriptor("MuxServerRoutes", "StoreWriteBroadcastsTranscriptChanged", "The server's store watcher rebroadcasts a direct-to-disk session write to a WebSocket subscriber", async (CancellationToken ct) =>
                    {
                        // The reliability fix: a turn written straight to the shared store by ANOTHER process (an
                        // in-process TUI/desktop run) — with no run and no notify through this server — must still
                        // reach a thin client, because the server watches the store directory and rebroadcasts.
                        string tempSessions = Path.Combine(Path.GetTempPath(), "mux-test-" + Guid.NewGuid().ToString("N"));
                        Directory.CreateDirectory(tempSessions);
                        RestServerSettings rest = new RestServerSettings { Hostname = "127.0.0.1", ApiKey = "testkey123" };
                        List<EndpointConfig> endpoints = new List<EndpointConfig>();

                        MuxServer? server = null;
                        int port = 0;
                        for (int bindAttempt = 0; bindAttempt < 10 && server == null; bindAttempt++)
                        {
                            port = FreeLoopbackPort();
                            rest.Port = port;
                            MuxServer candidate = new MuxServer(rest, "9.9.9-test", new SessionStore(tempSessions), () => endpoints, null);
                            try { candidate.Start(); server = candidate; }
                            catch (Exception) { candidate.Dispose(); Thread.Sleep(50); }
                        }

                        MuxAssert.IsNotNull(server, "server bound to a loopback port");

                        try
                        {
                            using (HttpClient http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) })
                            {
                                for (int attempt = 0; attempt < 20; attempt++)
                                {
                                    try { http.GetAsync("http://127.0.0.1:" + port + "/v1.0/api/health").GetAwaiter().GetResult(); break; }
                                    catch (Exception) { Thread.Sleep(100); }
                                }
                            }

                            using ClientWebSocket ws = new ClientWebSocket();
                            await ws.ConnectAsync(new Uri("ws://127.0.0.1:" + port + "/v1.0/ws?apiKey=testkey123"), ct).ConfigureAwait(false);
                            await ReceiveTextAsync(ws, ct).ConfigureAwait(false); // server.connected
                            await SendTextAsync(ws, "{\"action\":\"subscribe\",\"sessionId\":\"watch-1\"}", ct).ConfigureAwait(false);
                            await Task.Delay(200, ct).ConfigureAwait(false);

                            // Simulate an in-process surface writing the store directly (no run, no notify).
                            SessionStore external = new SessionStore(tempSessions);
                            SessionSnapshot snapshot = new SessionSnapshot { Id = "watch-1", Title = "Direct write" };
                            snapshot.ConversationHistory.Add(new ConversationMessage { Role = RoleEnum.User, Content = "written straight to disk" });
                            await external.SaveAsync(snapshot, ct).ConfigureAwait(false);

                            string frame = await ReceiveTextAsync(ws, ct).ConfigureAwait(false);
                            MuxAssert.Contains("transcript_changed", frame, "the server watcher rebroadcast the external write");
                            MuxAssert.Contains("watch-1", frame, "the rebroadcast carries the changed session id");
                        }
                        finally
                        {
                            server?.Stop();
                            server?.Dispose();
                            try { if (Directory.Exists(tempSessions)) Directory.Delete(tempSessions, true); } catch (Exception) { }
                        }
                    }),
                    new TestCaseDescriptor("MuxServerRoutes", "StoreWriteBroadcastsSessionsChangedToAll", "The store watcher rebroadcasts a direct-to-disk write as sessions_changed to a global (all) subscriber", async (CancellationToken ct) =>
                    {
                        // The list-sync half of the reliability fix: a thin client (dashboard, VS Code) that is NOT
                        // viewing a session still learns of a new/updated conversation written straight to the shared
                        // store by another process — because the server watcher also raises sessions_changed to
                        // every "all" subscriber. This is the signal the surfaces reload their open transcript on.
                        string tempSessions = Path.Combine(Path.GetTempPath(), "mux-test-" + Guid.NewGuid().ToString("N"));
                        Directory.CreateDirectory(tempSessions);
                        RestServerSettings rest = new RestServerSettings { Hostname = "127.0.0.1", ApiKey = "testkey123" };
                        List<EndpointConfig> endpoints = new List<EndpointConfig>();

                        MuxServer? server = null;
                        int port = 0;
                        for (int bindAttempt = 0; bindAttempt < 10 && server == null; bindAttempt++)
                        {
                            port = FreeLoopbackPort();
                            rest.Port = port;
                            MuxServer candidate = new MuxServer(rest, "9.9.9-test", new SessionStore(tempSessions), () => endpoints, null);
                            try { candidate.Start(); server = candidate; }
                            catch (Exception) { candidate.Dispose(); Thread.Sleep(50); }
                        }

                        MuxAssert.IsNotNull(server, "server bound to a loopback port");

                        try
                        {
                            using (HttpClient http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) })
                            {
                                for (int attempt = 0; attempt < 20; attempt++)
                                {
                                    try { http.GetAsync("http://127.0.0.1:" + port + "/v1.0/api/health").GetAwaiter().GetResult(); break; }
                                    catch (Exception) { Thread.Sleep(100); }
                                }
                            }

                            using ClientWebSocket ws = new ClientWebSocket();
                            await ws.ConnectAsync(new Uri("ws://127.0.0.1:" + port + "/v1.0/ws?apiKey=testkey123"), ct).ConfigureAwait(false);
                            await ReceiveTextAsync(ws, ct).ConfigureAwait(false); // server.connected
                            await SendTextAsync(ws, "{\"action\":\"subscribe\",\"all\":true}", ct).ConfigureAwait(false);
                            await Task.Delay(200, ct).ConfigureAwait(false);

                            SessionStore external = new SessionStore(tempSessions);
                            SessionSnapshot snapshot = new SessionSnapshot { Id = "list-1", Title = "Direct write" };
                            snapshot.ConversationHistory.Add(new ConversationMessage { Role = RoleEnum.User, Content = "new conversation from another process" });
                            await external.SaveAsync(snapshot, ct).ConfigureAwait(false);

                            string frame = await ReceiveTextAsync(ws, ct).ConfigureAwait(false);
                            MuxAssert.Contains("sessions_changed", frame, "the watcher rebroadcast the external write as a list change");
                            MuxAssert.Contains("list-1", frame, "the sessions_changed frame carries the changed session id");
                        }
                        finally
                        {
                            server?.Stop();
                            server?.Dispose();
                            try { if (Directory.Exists(tempSessions)) Directory.Delete(tempSessions, true); } catch (Exception) { }
                        }
                    })
                });
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
            byte[] buffer = new byte[8192];
            StringBuilder sb = new StringBuilder();
            while (true)
            {
                WebSocketReceiveResult result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), timeout.Token).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    return sb.ToString();
                }

                sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                if (result.EndOfMessage)
                {
                    return sb.ToString();
                }
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

        private static string GetAuthed(HttpClient http, string url)
        {
            using HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Add("Authorization", "Bearer testkey123");
            HttpResponseMessage res = http.SendAsync(req).GetAwaiter().GetResult();
            return res.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        }

        private static string PutAuthed(HttpClient http, string url, string body, out int status)
        {
            using HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Put, url);
            req.Headers.Add("Authorization", "Bearer testkey123");
            req.Content = new StringContent(body, Encoding.UTF8, "application/json");
            HttpResponseMessage res = http.SendAsync(req).GetAwaiter().GetResult();
            status = (int)res.StatusCode;
            return res.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        }
    }
}
