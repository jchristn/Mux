namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Agent;
    using Mux.Core.Enums;
    using Mux.Core.Models;
    using Mux.Core.Plugins;
    using Touchstone.Core;

    /// <summary>
    /// Touchstone suite for tool-level hooks (<c>pre-tool-use</c>, <c>post-tool-use</c>, <c>stop</c>): event parsing and
    /// the matcher, the JSON payloads, and end-to-end agent runs against a mock model with real hook processes
    /// (pwsh scripts). Covers continue, block (the tool does not run and the hook's stderr reaches the model), warnings
    /// for other exit codes, start failures, and timeouts, result appends and exit-2 feedback, stop re-entry bounded to
    /// three, and unchanged behavior without hooks. Process cases skip when pwsh is not on PATH.
    /// </summary>
    public static class ToolHooksSuite
    {
        #region Private-Members

        private const string SuiteId = "ToolHooks";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Builds the tool-hooks suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/> for the tool hook cases.</returns>
        public static TestSuiteDescriptor Create()
        {
            bool ready = IsOnPath("pwsh");
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();
            void Add(string id, string name, Action body)
            {
                cases.Add(new TestCaseDescriptor(SuiteId, id, name, (CancellationToken ct) => { body(); return Task.CompletedTask; }));
            }

            void AddRun(string id, string name, Func<string, CancellationToken, Task> body)
            {
                cases.Add(new TestCaseDescriptor(SuiteId, id, name, (CancellationToken ct) => WithTempAsync((string dir) => body(dir, ct)), skip: !ready, skipReason: "pwsh is not on PATH"));
            }

            // --- parsing, matcher, payloads ---
            Add("NewEventsParseAndRoundTrip", "pre-tool-use, post-tool-use, and stop parse in every accepted form and write kebab-case", () =>
            {
                foreach (string form in new[] { "pre-tool-use", "pre_tool_use", "PreToolUse", "PRETOOLUSE" })
                {
                    MuxAssert.IsTrue(HookEventEnumConverter.TryParse(form, out HookEventEnum value) && value == HookEventEnum.PreToolUse, form);
                }

                MuxAssert.IsTrue(HookEventEnumConverter.TryParse("post-tool-use", out HookEventEnum post) && post == HookEventEnum.PostToolUse, "post");
                MuxAssert.IsTrue(HookEventEnumConverter.TryParse(" Stop ", out HookEventEnum stop) && stop == HookEventEnum.Stop, "stop trimmed");
                MuxAssert.AreEqual("pre-tool-use", HookEventEnumConverter.ToWireName(HookEventEnum.PreToolUse), "pre wire");
                MuxAssert.AreEqual("post-tool-use", HookEventEnumConverter.ToWireName(HookEventEnum.PostToolUse), "post wire");
                MuxAssert.AreEqual("stop", HookEventEnumConverter.ToWireName(HookEventEnum.Stop), "stop wire");
                foreach (string bad in new[] { "pre-tool", "tool-use", "stopped", string.Empty, null! })
                {
                    MuxAssert.IsFalse(HookEventEnumConverter.TryParse(bad, out _), "rejects '" + bad + "'");
                }
            });
            Add("HookJsonCarriesMatcher", "hooks.json entries round-trip the event and matcher; unknown events fail with the accepted names", () =>
            {
                HookDefinition hook = JsonSerializer.Deserialize<HookDefinition>("{\"event\":\"pre-tool-use\",\"matcher\":\"write_*|edit_file\",\"command\":\"x\"}")!;
                MuxAssert.AreEqual(HookEventEnum.PreToolUse, hook.Event, "event");
                MuxAssert.AreEqual("write_*|edit_file", hook.Matcher, "matcher");
                string json = JsonSerializer.Serialize(hook);
                MuxAssert.Contains("\"event\":\"pre-tool-use\"", json, "event written kebab-case");
                MuxAssert.Contains("\"matcher\":\"write_*|edit_file\"", json, "matcher written");
                MuxAssert.AreEqual(string.Empty, JsonSerializer.Deserialize<HookDefinition>("{\"event\":\"stop\",\"command\":\"x\",\"matcher\":null}")!.Matcher, "null matcher becomes empty");
                JsonException error = MuxAssert.Throws<JsonException>(() => JsonSerializer.Deserialize<HookDefinition>("{\"event\":\"before-tool\",\"command\":\"x\"}"), "unknown event");
                MuxAssert.Contains("pre-tool-use, post-tool-use, stop", error.Message, "names listed");
            });
            Add("MatcherSelectsTools", "Matchers use --allow-tools globs, case-insensitively, with | alternatives; empty matches all", () =>
            {
                MuxAssert.IsTrue(ToolGovernance.MatchesToolPattern("write_file", "write_*"), "wildcard");
                MuxAssert.IsTrue(ToolGovernance.MatchesToolPattern("WRITE_FILE", "write_file"), "case-insensitive");
                MuxAssert.IsTrue(ToolGovernance.MatchesToolPattern("edit_file", "write_file|edit_file"), "alternative");
                MuxAssert.IsTrue(ToolGovernance.MatchesToolPattern("read_file", "rea?_file"), "single character");
                MuxAssert.IsTrue(ToolGovernance.MatchesToolPattern("anything", string.Empty), "empty matches all");
                MuxAssert.IsTrue(ToolGovernance.MatchesToolPattern("anything", "*"), "star matches all");
                MuxAssert.IsFalse(ToolGovernance.MatchesToolPattern("read_file", "write_*"), "no match");
                MuxAssert.IsFalse(ToolGovernance.MatchesToolPattern("write_file_extra", "write_file"), "whole name only");
                MuxAssert.IsFalse(ToolGovernance.MatchesToolPattern(null, "write_file"), "null tool");

                PluginRegistry registry = new PluginRegistry(new PluginConfig
                {
                    Hooks = new List<HookDefinition>
                    {
                        new HookDefinition { Event = HookEventEnum.PreToolUse, Matcher = "write_*", Command = "a" },
                        new HookDefinition { Event = HookEventEnum.PreToolUse, Command = "b" },
                        new HookDefinition { Event = HookEventEnum.PostToolUse, Matcher = "write_file", Command = "c" },
                        new HookDefinition { Event = HookEventEnum.Stop, Command = "d" }
                    }
                });
                MuxAssert.AreEqual(2, registry.HooksFor(HookEventEnum.PreToolUse, "write_file").Count, "matcher and catch-all");
                MuxAssert.AreEqual(1, registry.HooksFor(HookEventEnum.PreToolUse, "read_file").Count, "catch-all only");
                MuxAssert.AreEqual(0, registry.HooksFor(HookEventEnum.PostToolUse, "read_file").Count, "no post hook for read_file");
                MuxAssert.AreEqual(1, registry.HooksFor(HookEventEnum.Stop).Count, "stop hook");
            });
            Add("PayloadsUseClaudeCodeFields", "Hook payloads use Claude Code's field names and embed tool input as JSON", () =>
            {
                using (JsonDocument pre = JsonDocument.Parse(ToolHookPayload.ForTool("PreToolUse", "s1", "/w", "write_file", "c1", "{\"file_path\":\"a.txt\"}", null, null)))
                {
                    JsonElement root = pre.RootElement;
                    MuxAssert.AreEqual("PreToolUse", root.GetProperty("hook_event_name").GetString(), "event");
                    MuxAssert.AreEqual("s1", root.GetProperty("session_id").GetString(), "session");
                    MuxAssert.AreEqual("/w", root.GetProperty("cwd").GetString(), "cwd");
                    MuxAssert.AreEqual("write_file", root.GetProperty("tool_name").GetString(), "tool");
                    MuxAssert.AreEqual("a.txt", root.GetProperty("tool_input").GetProperty("file_path").GetString(), "input object");
                    MuxAssert.IsFalse(root.TryGetProperty("tool_response", out _), "no response before the call");
                }

                using (JsonDocument post = JsonDocument.Parse(ToolHookPayload.ForTool("PostToolUse", null, null, "t", "c", "not json {", false, "boom")))
                {
                    JsonElement root = post.RootElement;
                    MuxAssert.AreEqual("not json {", root.GetProperty("tool_input").GetString(), "invalid input sent as a string");
                    MuxAssert.IsFalse(root.GetProperty("tool_response").GetProperty("success").GetBoolean(), "success");
                    MuxAssert.AreEqual("boom", root.GetProperty("tool_response").GetProperty("content").GetString(), "content");
                }

                using (JsonDocument empty = JsonDocument.Parse(ToolHookPayload.ForTool("PreToolUse", null, null, "t", "c", "  ", null, null)))
                {
                    MuxAssert.AreEqual(JsonValueKind.Object, empty.RootElement.GetProperty("tool_input").ValueKind, "blank input is an empty object");
                }

                using (JsonDocument stop = JsonDocument.Parse(ToolHookPayload.ForStop("s", "/w", true, "done")))
                {
                    MuxAssert.AreEqual("Stop", stop.RootElement.GetProperty("hook_event_name").GetString(), "stop event");
                    MuxAssert.IsTrue(stop.RootElement.GetProperty("stop_hook_active").GetBoolean(), "active flag");
                    MuxAssert.AreEqual("done", stop.RootElement.GetProperty("last_assistant_message").GetString(), "last message");
                }
            });
            Add("HookEventSerializes", "A hook event serializes as eventType hook with its fields, redacting secrets", () =>
            {
                HookEvent hookEvent = new HookEvent { HookEventName = "pre-tool-use", HookName = "guard", Outcome = HookEvent.OutcomeBlocked, ExitCode = 2, ToolName = "write_file", ToolCallId = "c1", Message = "no: Bearer abc123" };
                Dictionary<string, object?> payload = AgentEventSerializer.ToEnvelope(hookEvent, true);
                MuxAssert.AreEqual("hook", payload["eventType"]?.ToString(), "event type");
                MuxAssert.AreEqual("blocked", payload["outcome"]?.ToString(), "outcome");
                MuxAssert.AreEqual("write_file", payload["toolName"]?.ToString(), "tool");
                MuxAssert.DoesNotContain("abc123", payload["message"]?.ToString() ?? string.Empty, "secret redacted");
            });

            // --- end to end with real hook processes ---
            AddRun("PreHookAllowsCall", "A pre-tool-use hook that exits 0 lets the tool run and gets the payload on stdin", async (string dir, CancellationToken ct) =>
            {
                string data = Path.Combine(dir, "data.txt");
                File.WriteAllText(data, "FILE_BODY_MARKER");
                string captured = Path.Combine(dir, "payload.json");
                PluginRegistry hooks = Hooks(HookEventEnum.PreToolUse, "read_*", Script(dir, "capture.ps1", "[Console]::In.ReadToEnd() | Set-Content -LiteralPath '" + captured + "'\nexit 0\n"));
                using (MockHttpServer server = new MockHttpServer())
                {
                    server.RegisterStreamingResponse("allow run", new List<string> { ToolCallChunk("read_file", "{\"file_path\":" + JsonSerializer.Serialize(data) + "}") });
                    server.RegisterStreamingResponse("FILE_BODY_MARKER", new List<string> { AgentTestHarness.BuildTextSseChunk("Read it.") });
                    server.Start();
                    List<AgentEvent> events = await RunAsync(server, dir, hooks, "allow run", ct).ConfigureAwait(false);
                    ToolCallCompletedEvent completed = events.OfType<ToolCallCompletedEvent>().Single();
                    MuxAssert.IsTrue(completed.Result.Success, "tool ran");
                    MuxAssert.Contains("FILE_BODY_MARKER", completed.Result.Content, "real result");
                    MuxAssert.AreEqual(0, events.OfType<HookEvent>().Count(), "no hook events on a clean pass");
                    using (JsonDocument payload = JsonDocument.Parse(File.ReadAllText(captured)))
                    {
                        MuxAssert.AreEqual("PreToolUse", payload.RootElement.GetProperty("hook_event_name").GetString(), "event name");
                        MuxAssert.AreEqual("read_file", payload.RootElement.GetProperty("tool_name").GetString(), "tool name");
                        MuxAssert.AreEqual(data, payload.RootElement.GetProperty("tool_input").GetProperty("file_path").GetString(), "tool input");
                        MuxAssert.AreEqual(dir, payload.RootElement.GetProperty("cwd").GetString(), "cwd");
                        MuxAssert.AreEqual("hooks-session", payload.RootElement.GetProperty("session_id").GetString(), "session id");
                    }
                }
            });
            AddRun("PreHookBlocksCall", "A pre-tool-use hook that exits 2 blocks the call and its stderr reaches the model", async (string dir, CancellationToken ct) =>
            {
                string target = Path.Combine(dir, "should-not-exist.txt");
                PluginRegistry hooks = Hooks(HookEventEnum.PreToolUse, "write_file", Script(dir, "block.ps1", "[Console]::Error.WriteLine('BLOCKED_BY_POLICY: no writes here')\nexit 2\n"));
                using (MockHttpServer server = new MockHttpServer())
                {
                    server.RegisterStreamingResponse("block run", new List<string> { ToolCallChunk("write_file", "{\"file_path\":" + JsonSerializer.Serialize(target) + ",\"content\":\"x\"}") });
                    server.RegisterStreamingResponse("BLOCKED_BY_POLICY: no writes here", new List<string> { AgentTestHarness.BuildTextSseChunk("Understood.") });
                    server.Start();
                    List<AgentEvent> events = await RunAsync(server, dir, hooks, "block run", ct).ConfigureAwait(false);
                    MuxAssert.IsFalse(File.Exists(target), "the tool did not run");
                    MuxAssert.IsTrue(events.OfType<ErrorEvent>().Any((ErrorEvent e) => e.Code == "tool_call_blocked_by_hook"), "error event");
                    HookEvent blocked = events.OfType<HookEvent>().Single();
                    MuxAssert.AreEqual(HookEvent.OutcomeBlocked, blocked.Outcome, "blocked outcome");
                    MuxAssert.Contains("BLOCKED_BY_POLICY", blocked.Message, "stderr in the event");
                    ToolCallCompletedEvent completed = events.OfType<ToolCallCompletedEvent>().Single();
                    MuxAssert.IsFalse(completed.Result.Success, "failure result");
                    MuxAssert.Contains("tool_call_blocked_by_hook", completed.Result.Content, "error code in the result");
                    MuxAssert.Contains("BLOCKED_BY_POLICY: no writes here", server.ReceivedRequests[server.ReceivedRequests.Count - 1], "the model saw the stderr");
                    MuxAssert.AreEqual("completed_with_errors", events.OfType<RunCompletedEvent>().Single().Status, "run completes");
                }
            });
            AddRun("PreHookOtherExitWarns", "A pre-tool-use hook that exits 1, cannot start, or times out warns and the tool still runs", async (string dir, CancellationToken ct) =>
            {
                string target = Path.Combine(dir, "written.txt");
                PluginConfig config = new PluginConfig
                {
                    Hooks = new List<HookDefinition>
                    {
                        Hook(HookEventEnum.PreToolUse, "write_file", Script(dir, "one.ps1", "[Console]::Error.WriteLine('soft failure')\nexit 1\n")),
                        new HookDefinition { Name = "missing", Event = HookEventEnum.PreToolUse, Command = "mux-no-such-hook-xyz" },
                        Hook(HookEventEnum.PreToolUse, string.Empty, Script(dir, "slow.ps1", "Start-Sleep -Seconds 20\nexit 2\n"), 1500)
                    }
                };
                using (MockHttpServer server = new MockHttpServer())
                {
                    server.RegisterStreamingResponse("warn run", new List<string> { ToolCallChunk("write_file", "{\"file_path\":" + JsonSerializer.Serialize(target) + ",\"content\":\"WRITTEN\"}") });
                    server.RegisterStreamingResponse("line_count", new List<string> { AgentTestHarness.BuildTextSseChunk("Done.") });
                    server.Start();
                    List<AgentEvent> events = await RunAsync(server, dir, new PluginRegistry(config), "warn run", ct).ConfigureAwait(false);
                    MuxAssert.IsTrue(File.Exists(target), "the tool ran");
                    List<HookEvent> warnings = events.OfType<HookEvent>().Where((HookEvent e) => e.Outcome == HookEvent.OutcomeWarning).ToList();
                    MuxAssert.AreEqual(3, warnings.Count, "three warnings");
                    MuxAssert.Contains("exited with code 1: soft failure", warnings[0].Message, "exit 1 warning");
                    MuxAssert.Contains("could not start", warnings[1].Message, "start failure warning");
                    MuxAssert.Contains("timed out", warnings[2].Message, "timeout warning (a timed-out exit 2 never blocks)");
                    MuxAssert.IsFalse(events.OfType<ErrorEvent>().Any((ErrorEvent e) => e.Code == "tool_call_blocked_by_hook"), "nothing blocked");
                }
            });
            AddRun("MatcherSkipsOtherTools", "A pre-tool-use hook whose matcher does not match never runs", async (string dir, CancellationToken ct) =>
            {
                string data = Path.Combine(dir, "data.txt");
                File.WriteAllText(data, "UNMATCHED_BODY");
                string marker = Path.Combine(dir, "ran.txt");
                PluginRegistry hooks = Hooks(HookEventEnum.PreToolUse, "write_*", Script(dir, "mark.ps1", "Set-Content -LiteralPath '" + marker + "' -Value x\nexit 2\n"));
                using (MockHttpServer server = new MockHttpServer())
                {
                    server.RegisterStreamingResponse("skip run", new List<string> { ToolCallChunk("read_file", "{\"file_path\":" + JsonSerializer.Serialize(data) + "}") });
                    server.RegisterStreamingResponse("UNMATCHED_BODY", new List<string> { AgentTestHarness.BuildTextSseChunk("Fine.") });
                    server.Start();
                    List<AgentEvent> events = await RunAsync(server, dir, hooks, "skip run", ct).ConfigureAwait(false);
                    MuxAssert.IsFalse(File.Exists(marker), "hook never started");
                    MuxAssert.IsTrue(events.OfType<ToolCallCompletedEvent>().Single().Result.Success, "tool ran");
                }
            });
            AddRun("PostHookAppendsAndFeedsBack", "post-tool-use stdout (exit 0) and stderr (exit 2) are appended to the result the model sees", async (string dir, CancellationToken ct) =>
            {
                string data = Path.Combine(dir, "data.txt");
                File.WriteAllText(data, "POST_BODY");
                PluginConfig config = new PluginConfig
                {
                    Hooks = new List<HookDefinition>
                    {
                        Hook(HookEventEnum.PostToolUse, "read_file", Script(dir, "note.ps1", "$p = [Console]::In.ReadToEnd() | ConvertFrom-Json\nWrite-Output ('LINT_NOTE success=' + $p.tool_response.success)\nexit 0\n"), name: "note"),
                        Hook(HookEventEnum.PostToolUse, "read_file", Script(dir, "fb.ps1", "[Console]::Error.WriteLine('FEEDBACK_TWO')\nexit 2\n"), name: "feedback"),
                        Hook(HookEventEnum.PostToolUse, "read_file", Script(dir, "quiet.ps1", "exit 0\n"), name: "quiet")
                    }
                };
                using (MockHttpServer server = new MockHttpServer())
                {
                    server.RegisterStreamingResponse("post run", new List<string> { ToolCallChunk("read_file", "{\"file_path\":" + JsonSerializer.Serialize(data) + "}") });
                    server.RegisterStreamingResponse("[hook feedback] FEEDBACK_TWO", new List<string> { AgentTestHarness.BuildTextSseChunk("Noted.") });
                    server.Start();
                    List<AgentEvent> events = await RunAsync(server, dir, new PluginRegistry(config), "post run", ct).ConfigureAwait(false);
                    string content = events.OfType<ToolCallCompletedEvent>().Single().Result.Content;
                    MuxAssert.Contains("POST_BODY", content, "original result kept");
                    MuxAssert.Contains("[hook note] LINT_NOTE success=True", content, "stdout appended with the response payload");
                    MuxAssert.Contains("[hook feedback] FEEDBACK_TWO", content, "exit 2 stderr appended");
                    MuxAssert.DoesNotContain("[hook quiet]", content, "empty output appends nothing");
                    MuxAssert.AreEqual(2, events.OfType<HookEvent>().Count((HookEvent e) => e.Outcome == HookEvent.OutcomeAppended), "two appended events");
                    MuxAssert.Contains("LINT_NOTE", server.ReceivedRequests[server.ReceivedRequests.Count - 1], "the model saw the note");
                }
            });
            AddRun("StopHookContinuesBounded", "A stop hook that exits 2 makes the model continue, at most three times per run", async (string dir, CancellationToken ct) =>
            {
                PluginRegistry hooks = Hooks(HookEventEnum.Stop, string.Empty, Script(dir, "stop.ps1", "$p = [Console]::In.ReadToEnd() | ConvertFrom-Json\n[Console]::Error.WriteLine('KEEP_GOING active=' + $p.stop_hook_active)\nexit 2\n"));
                using (MockHttpServer server = new MockHttpServer())
                {
                    server.RegisterStreamingResponse("stop run", new List<string> { AgentTestHarness.BuildTextSseChunk("First answer.") });
                    server.RegisterStreamingResponse("A stop hook asked to continue: KEEP_GOING", new List<string> { AgentTestHarness.BuildTextSseChunk("Another answer.") });
                    server.Start();
                    List<AgentEvent> events = await RunAsync(server, dir, hooks, "stop run", ct).ConfigureAwait(false);
                    List<HookEvent> continued = events.OfType<HookEvent>().Where((HookEvent e) => e.Outcome == HookEvent.OutcomeContinued).ToList();
                    MuxAssert.AreEqual(3, continued.Count, "three continuations");
                    MuxAssert.Contains("active=False", continued[0].Message, "first stop is not active");
                    MuxAssert.Contains("active=True", continued[1].Message, "later stops are active");
                    MuxAssert.IsTrue(events.OfType<HookEvent>().Any((HookEvent e) => e.Outcome == HookEvent.OutcomeWarning && e.Message.Contains("limit of 3", StringComparison.Ordinal)), "limit warning");
                    MuxAssert.AreEqual(4, server.ReceivedRequests.Count, "one model call plus three continuations");
                    MuxAssert.Contains("A stop hook asked to continue: KEEP_GOING", server.ReceivedRequests[1], "continuation message sent");
                    MuxAssert.AreEqual("completed", events.OfType<RunCompletedEvent>().Single().Status, "run completes");
                }
            });
            AddRun("StopHookExitZeroEnds", "A stop hook that exits 0 (or fails) ends the run normally", async (string dir, CancellationToken ct) =>
            {
                PluginConfig config = new PluginConfig
                {
                    Hooks = new List<HookDefinition>
                    {
                        Hook(HookEventEnum.Stop, string.Empty, Script(dir, "ok.ps1", "exit 0\n")),
                        Hook(HookEventEnum.Stop, string.Empty, Script(dir, "bad.ps1", "exit 5\n"))
                    }
                };
                using (MockHttpServer server = new MockHttpServer())
                {
                    server.RegisterStreamingResponse("calm run", new List<string> { AgentTestHarness.BuildTextSseChunk("All done.") });
                    server.Start();
                    List<AgentEvent> events = await RunAsync(server, dir, new PluginRegistry(config), "calm run", ct).ConfigureAwait(false);
                    MuxAssert.AreEqual(1, server.ReceivedRequests.Count, "no continuation");
                    MuxAssert.AreEqual(1, events.OfType<HookEvent>().Count(), "one warning for exit 5");
                    MuxAssert.Contains("exited with code 5", events.OfType<HookEvent>().Single().Message, "warning text");
                }
            });
            AddRun("NoHooksNoChange", "Without hooks a run behaves exactly as before", async (string dir, CancellationToken ct) =>
            {
                string target = Path.Combine(dir, "plain.txt");
                using (MockHttpServer server = new MockHttpServer())
                {
                    server.RegisterStreamingResponse("plain run", new List<string> { ToolCallChunk("write_file", "{\"file_path\":" + JsonSerializer.Serialize(target) + ",\"content\":\"PLAIN\"}") });
                    server.RegisterStreamingResponse("line_count", new List<string> { AgentTestHarness.BuildTextSseChunk("Done.") });
                    server.Start();
                    List<AgentEvent> withNull = await RunAsync(server, dir, null, "plain run", ct).ConfigureAwait(false);
                    MuxAssert.IsTrue(File.Exists(target), "tool ran");
                    MuxAssert.AreEqual(0, withNull.OfType<HookEvent>().Count(), "no hook events");
                    MuxAssert.AreEqual("completed", withNull.OfType<RunCompletedEvent>().Single().Status, "completed");
                    File.Delete(target);
                    PluginRegistry sessionOnly = Hooks(HookEventEnum.SessionStart, string.Empty, "mux-no-such-hook-xyz");
                    List<AgentEvent> withSessionHooks = await RunAsync(server, dir, sessionOnly, "plain run", ct).ConfigureAwait(false);
                    MuxAssert.IsTrue(File.Exists(target), "tool ran again");
                    MuxAssert.AreEqual(0, withSessionHooks.OfType<HookEvent>().Count(), "session hooks never run in the loop");
                }
            });

            return new TestSuiteDescriptor(SuiteId, "Tool-level hooks: pre-tool-use, post-tool-use, stop", cases);
        }

        #endregion

        #region Private-Methods

        private static async Task<List<AgentEvent>> RunAsync(MockHttpServer server, string dir, PluginRegistry? hooks, string prompt, CancellationToken ct)
        {
            AgentLoopOptions options = new AgentLoopOptions(AgentTestHarness.BuildMockEndpoint(server.BaseUrl))
            {
                ApprovalPolicy = ApprovalPolicyEnum.AutoApprove,
                MaxIterations = 8,
                WorkingDirectory = dir,
                SessionId = "hooks-session",
                Hooks = hooks
            };
            return await AgentTestHarness.CollectEventsAsync(options, prompt, ct).ConfigureAwait(false);
        }

        private static string ToolCallChunk(string tool, string argumentsJson)
        {
            string arguments = JsonSerializer.Serialize(argumentsJson);
            return "{\"choices\":[{\"delta\":{\"tool_calls\":[{\"index\":0,\"id\":\"call_" + tool + "\",\"function\":{\"name\":\"" + tool + "\",\"arguments\":" + arguments + "}}]},\"finish_reason\":\"tool_calls\"}]}";
        }

        private static PluginRegistry Hooks(HookEventEnum hookEvent, string matcher, string script)
        {
            return new PluginRegistry(new PluginConfig { Hooks = new List<HookDefinition> { Hook(hookEvent, matcher, script) } });
        }

        private static HookDefinition Hook(HookEventEnum hookEvent, string matcher, string script, int timeoutMs = 20000, string name = "")
        {
            if (!script.EndsWith(".ps1", StringComparison.Ordinal))
            {
                return new HookDefinition { Name = name, Event = hookEvent, Matcher = matcher, Command = script, TimeoutMs = timeoutMs };
            }

            return new HookDefinition
            {
                Name = string.IsNullOrEmpty(name) ? Path.GetFileNameWithoutExtension(script) : name,
                Event = hookEvent,
                Matcher = matcher,
                Command = "pwsh",
                Args = new List<string> { "-NoProfile", "-NonInteractive", "-File", script },
                TimeoutMs = timeoutMs
            };
        }

        private static string Script(string dir, string name, string content)
        {
            string path = Path.Combine(dir, "hooks", name);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
            return path;
        }

        private static async Task WithTempAsync(Func<string, Task> body)
        {
            string dir = Path.Combine(Path.GetTempPath(), "mux-toolhooks-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            dir = new DirectoryInfo(dir).FullName;
            try
            {
                await body(dir).ConfigureAwait(false);
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch (Exception) { }
            }
        }

        private static bool IsOnPath(string executable)
        {
            string[] names = OperatingSystem.IsWindows() ? new[] { executable + ".exe", executable + ".cmd" } : new[] { executable };
            foreach (string directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator))
            {
                foreach (string name in names)
                {
                    if (!string.IsNullOrWhiteSpace(directory) && File.Exists(Path.Combine(directory, name)))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        #endregion
    }
}
