namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Enums;
    using Mux.Core.Models;
    using Mux.Core.Tools;
    using Test.Shared.Support;
    using Touchstone.Core;
    using ToolDefinition = Mux.Core.Models.ToolDefinition;

    /// <summary>
    /// Touchstone suite for mux as an MCP client over stdio, against a small Node.js server with scripted behavior:
    /// discovery and calls, arguments and environment variables reaching the process, a server that crashes mid
    /// session, JSON-RPC errors and isError results, noise on stdout, a process that exits at startup, a failed
    /// handshake, malformed tool entries, process cleanup on dispose, concurrency, and two isolated servers. Skipped
    /// when node is not on PATH.
    /// </summary>
    public static class McpStdioClientSuite
    {
        #region Private-Members

        private const string SuiteId = "McpStdioClient";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Builds the stdio client suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/> for the stdio client cases.</returns>
        public static TestSuiteDescriptor Create()
        {
            string? node = McpConnectionDiagnostics.ResolveCommand("node");
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();
            void Add(string id, string name, Func<string, CancellationToken, Task> body)
            {
                cases.Add(new TestCaseDescriptor(SuiteId, id, name, async (CancellationToken ct) =>
                {
                    string script = NodeMcpServerScript.Write();
                    try
                    {
                        await body(script, ct).ConfigureAwait(false);
                    }
                    finally
                    {
                        try { File.Delete(script); } catch (Exception) { }
                    }
                }, skip: node == null, skipReason: "node is not on PATH"));
            }

            Add("ConnectsDiscoversAndCalls", "A stdio server is launched, its tools discovered and prefixed, and a call returns its text", async (string script, CancellationToken ct) =>
            {
                using (McpToolManager manager = await Connect(ct, Stdio("node1", script, "normal")).ConfigureAwait(false))
                {
                    McpConnectionResult result = manager.GetConnectionResults()[0];
                    MuxAssert.IsTrue(result.Connected, "connected: " + result.Error);
                    MuxAssert.AreEqual("stdio", result.Method, "method label");
                    MuxAssert.AreEqual(7, result.ToolCount, "seven tools");
                    ToolResult echo = await manager.ExecuteAsync("c", "node1.echo", Json("{\"text\":\"hi there\"}"), ct).ConfigureAwait(false);
                    MuxAssert.IsTrue(echo.Success, "success: " + echo.Content);
                    MuxAssert.Contains("echo:hi there", echo.Content, "text returned");
                    MuxAssert.IsTrue(manager.GetServerStatus()[0].Connected, "status connected");
                }
            });
            Add("ArgumentsReachProcess", "Configured arguments, including ones with spaces and quotes, reach the process intact", async (string script, CancellationToken ct) =>
            {
                using (McpToolManager manager = await Connect(ct, Stdio("node1", script, "normal", "--flag", "value with spaces", "quote\"d")).ConfigureAwait(false))
                {
                    ToolResult argv = await manager.ExecuteAsync("c", "node1.argv", Json("{}"), ct).ConfigureAwait(false);
                    string text = TextOf(argv.Content);
                    MuxAssert.AreEqual("[\"normal\",\"--flag\",\"value with spaces\",\"quote\\\"d\"]", text, "argv intact");
                }
            });
            Add("EnvironmentVariablesExpandedAndPassed", "Env entries are expanded (including ${VAR} references) and reach the process", async (string script, CancellationToken ct) =>
            {
                Environment.SetEnvironmentVariable("MUX_TEST_PARENT_VALUE", "parent-42");
                try
                {
                    McpServerConfig config = Stdio("node1", script, "normal");
                    config.Env = new Dictionary<string, string> { ["MUX_TEST_CHILD_VALUE"] = "${MUX_TEST_PARENT_VALUE}-child", ["MUX_TEST_PLAIN"] = "plain" };
                    using (McpToolManager manager = await Connect(ct, config).ConfigureAwait(false))
                    {
                        MuxAssert.Contains("env:parent-42-child", (await manager.ExecuteAsync("c", "node1.env", Json("{\"name\":\"MUX_TEST_CHILD_VALUE\"}"), ct).ConfigureAwait(false)).Content, "expanded value");
                        MuxAssert.Contains("env:plain", (await manager.ExecuteAsync("c", "node1.env", Json("{\"name\":\"MUX_TEST_PLAIN\"}"), ct).ConfigureAwait(false)).Content, "plain value");
                        MuxAssert.AreEqual("env:<unset>", TextOf((await manager.ExecuteAsync("c", "node1.env", Json("{\"name\":\"MUX_TEST_NEVER_SET_ANYWHERE\"}"), ct).ConfigureAwait(false)).Content), "unset stays unset");
                        MuxAssert.IsNull(Environment.GetEnvironmentVariable("MUX_TEST_PLAIN"), "a server's env entries do not linger in the mux process");
                        MuxAssert.IsNull(Environment.GetEnvironmentVariable("MUX_TEST_CHILD_VALUE"), "expanded entries do not linger either");
                    }
                }
                finally
                {
                    Environment.SetEnvironmentVariable("MUX_TEST_PARENT_VALUE", null);
                    Environment.SetEnvironmentVariable("MUX_TEST_CHILD_VALUE", null);
                    Environment.SetEnvironmentVariable("MUX_TEST_PLAIN", null);
                }
            });
            Add("CrashMidSessionFailsCleanly", "A server that crashes during a call fails that call and later calls, and can still be removed", async (string script, CancellationToken ct) =>
            {
                using (McpToolManager manager = await Connect(ct, Stdio("node1", script, "normal")).ConfigureAwait(false))
                using (CancellationTokenSource limit = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    limit.CancelAfter(TimeSpan.FromSeconds(30));
                    ToolResult crash = await manager.ExecuteAsync("c", "node1.crash", Json("{}"), limit.Token).ConfigureAwait(false);
                    MuxAssert.IsFalse(crash.Success, "the crashing call fails");
                    ToolResult after = await manager.ExecuteAsync("c", "node1.echo", Json("{\"text\":\"x\"}"), limit.Token).ConfigureAwait(false);
                    MuxAssert.IsFalse(after.Success, "later calls fail too");
                    MuxAssert.Contains("mcp_call_failed", after.Content, "reported as failed calls");
                    await manager.RemoveServerAsync("node1").ConfigureAwait(false);
                    MuxAssert.IsFalse(manager.HasTool("node1.echo"), "removal completes after a crash");
                }
            });
            Add("JsonRpcErrorBecomesFailedResult", "A JSON-RPC error from tools/call becomes a failed result with the server's message", async (string script, CancellationToken ct) =>
            {
                using (McpToolManager manager = await Connect(ct, Stdio("node1", script, "normal")).ConfigureAwait(false))
                {
                    ToolResult result = await manager.ExecuteAsync("c", "node1.rpcerror", Json("{}"), ct).ConfigureAwait(false);
                    MuxAssert.IsFalse(result.Success, "failed");
                    MuxAssert.Contains("quota exceeded", result.Content, "server message kept");
                    ToolResult echo = await manager.ExecuteAsync("c", "node1.echo", Json("{\"text\":\"still ok\"}"), ct).ConfigureAwait(false);
                    MuxAssert.IsTrue(echo.Success, "the connection survives an error response");
                }
            });
            Add("IsErrorResultBecomesFailure", "An isError result over stdio fails the call and keeps the text", async (string script, CancellationToken ct) =>
            {
                using (McpToolManager manager = await Connect(ct, Stdio("node1", script, "normal")).ConfigureAwait(false))
                {
                    ToolResult result = await manager.ExecuteAsync("c", "node1.iserror", Json("{}"), ct).ConfigureAwait(false);
                    MuxAssert.IsFalse(result.Success, "failed");
                    MuxAssert.Contains("tool said no", result.Content, "text kept");
                }
            });
            Add("NoiseOnStdoutTolerated", "Non-JSON lines on the server's stdout do not break the protocol", async (string script, CancellationToken ct) =>
            {
                using (McpToolManager manager = await Connect(ct, Stdio("noisy", script, "noise")).ConfigureAwait(false))
                {
                    MuxAssert.IsTrue(manager.GetConnectionResults()[0].Connected, "connected despite noise: " + manager.GetConnectionResults()[0].Error);
                    ToolResult echo = await manager.ExecuteAsync("c", "noisy.echo", Json("{\"text\":\"through the noise\"}"), ct).ConfigureAwait(false);
                    MuxAssert.Contains("through the noise", echo.Content, "call works");
                }
            });
            Add("ProcessExitingAtStartupDiagnosed", "A process that exits at once is reported with its command, resolved path, and stderr", async (string script, CancellationToken ct) =>
            {
                using (McpToolManager manager = await Connect(ct, Stdio("quits", script, "exit")).ConfigureAwait(false))
                {
                    McpConnectionResult result = manager.GetConnectionResults()[0];
                    MuxAssert.IsFalse(result.Connected, "not connected");
                    MuxAssert.Contains("Failed to start MCP server 'quits'", result.Error ?? string.Empty, "summary");
                    MuxAssert.Contains("Resolved: ", result.Error ?? string.Empty, "resolved path");
                    MuxAssert.AreEqual(0, manager.GetToolDefinitions().Count, "no tools");
                }
            });
            Add("HandshakeErrorDiagnosed", "A server that refuses initialize is reported as a failed handshake", async (string script, CancellationToken ct) =>
            {
                using (McpToolManager manager = await Connect(ct, Stdio("refuses", script, "badinit")).ConfigureAwait(false))
                {
                    McpConnectionResult result = manager.GetConnectionResults()[0];
                    MuxAssert.IsFalse(result.Connected, "not connected");
                    MuxAssert.Contains("Failed to start MCP server 'refuses'", result.Error ?? string.Empty, "summary: " + result.Error);
                }
            });
            Add("MalformedToolEntriesSkipped", "Malformed tools/list entries are skipped while valid ones register", async (string script, CancellationToken ct) =>
            {
                using (McpToolManager manager = await Connect(ct, Stdio("odd", script, "oddtools")).ConfigureAwait(false))
                {
                    List<string> names = manager.GetToolDefinitions().ConvertAll(t => t.Name);
                    names.Sort(StringComparer.Ordinal);
                    MuxAssert.AreEqual("odd.good,odd.noschema", string.Join(",", names), "only the well-formed tools");
                    MuxAssert.IsTrue((await manager.ExecuteAsync("c", "odd.noschema", Json("{}"), ct).ConfigureAwait(false)).Success, "a tool without a schema is callable");
                    ToolDefinition bare = manager.GetToolDefinitions().Find(t => t.Name == "odd.noschema")!;
                    MuxAssert.AreEqual("[MCP:odd] ", bare.Description, "a tool without a description is still tagged with its server");
                }
            });
            Add("DisposeStopsProcess", "Disposing the manager stops the server process", async (string script, CancellationToken ct) =>
            {
                int pid;
                using (McpToolManager manager = await Connect(ct, Stdio("node1", script, "normal")).ConfigureAwait(false))
                {
                    pid = int.Parse(TextOf((await manager.ExecuteAsync("c", "node1.pid", Json("{}"), ct).ConfigureAwait(false)).Content), System.Globalization.CultureInfo.InvariantCulture);
                    MuxAssert.IsTrue(IsAlive(pid), "running while connected");
                }

                bool gone = false;
                for (int i = 0; i < 50 && !gone; i++)
                {
                    gone = !IsAlive(pid);
                    if (!gone) await Task.Delay(100, ct).ConfigureAwait(false);
                }

                MuxAssert.IsTrue(gone, "the process exited after dispose");
            });
            Add("ConcurrentCallsOverStdio", "Concurrent calls over one stdio connection each get their own answer", async (string script, CancellationToken ct) =>
            {
                using (McpToolManager manager = await Connect(ct, Stdio("node1", script, "normal")).ConfigureAwait(false))
                {
                    List<Task<ToolResult>> calls = new List<Task<ToolResult>>();
                    for (int i = 0; i < 8; i++) calls.Add(manager.ExecuteAsync("c" + i, "node1.echo", Json("{\"text\":\"n" + i + "\"}"), ct));
                    ToolResult[] results = await Task.WhenAll(calls).ConfigureAwait(false);
                    for (int i = 0; i < 8; i++) MuxAssert.Contains("echo:n" + i, results[i].Content, "call " + i);
                }
            });
            Add("TwoServersAreSeparateProcesses", "Two stdio servers run as separate processes with separate tools", async (string script, CancellationToken ct) =>
            {
                using (McpToolManager manager = await Connect(ct, Stdio("a", script, "normal"), Stdio("b", script, "normal")).ConfigureAwait(false))
                {
                    string pidA = TextOf((await manager.ExecuteAsync("c", "a.pid", Json("{}"), ct).ConfigureAwait(false)).Content);
                    string pidB = TextOf((await manager.ExecuteAsync("c", "b.pid", Json("{}"), ct).ConfigureAwait(false)).Content);
                    MuxAssert.AreNotEqual(pidA, pidB, "different processes");
                    MuxAssert.AreEqual(14, manager.GetToolDefinitions().Count, "seven tools each");
                }
            });
            Add("MixedStdioAndHttpFailures", "A failing stdio server does not stop a working one from connecting", async (string script, CancellationToken ct) =>
            {
                using (McpToolManager manager = await Connect(ct, Stdio("broken", script, "exit"), Stdio("working", script, "normal")).ConfigureAwait(false))
                {
                    MuxAssert.IsTrue(manager.HasTool("working.echo"), "the working server connected");
                    MuxAssert.IsFalse(manager.HasTool("broken.echo"), "the broken one did not");
                    MuxAssert.AreEqual(2, manager.GetConnectionResults().Count, "both have results");
                }
            });

            return new TestSuiteDescriptor(SuiteId, "MCP client over stdio: launch, calls, crashes, errors, cleanup", cases);
        }

        #endregion

        #region Private-Methods

        private static McpServerConfig Stdio(string name, string script, params string[] args)
        {
            List<string> all = new List<string> { script };
            all.AddRange(args);
            return new McpServerConfig { Name = name, Transport = McpTransportTypeEnum.Stdio, Command = "node", Args = all };
        }

        private static async Task<McpToolManager> Connect(CancellationToken ct, params McpServerConfig[] configs)
        {
            McpToolManager manager = new McpToolManager(new List<McpServerConfig>(configs));
            using (CancellationTokenSource limit = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                limit.CancelAfter(TimeSpan.FromSeconds(60));
                await manager.InitializeAsync(limit.Token).ConfigureAwait(false);
            }

            return manager;
        }

        private static JsonElement Json(string text)
        {
            using (JsonDocument document = JsonDocument.Parse(text))
            {
                return document.RootElement.Clone();
            }
        }

        private static string TextOf(string rawResult)
        {
            using (JsonDocument doc = JsonDocument.Parse(rawResult))
            {
                if (doc.RootElement.TryGetProperty("content", out JsonElement content) && content.ValueKind == JsonValueKind.Array && content.GetArrayLength() > 0)
                {
                    return content[0].GetProperty("text").GetString() ?? string.Empty;
                }
            }

            return string.Empty;
        }

        private static bool IsAlive(int pid)
        {
            try
            {
                using (Process process = Process.GetProcessById(pid))
                {
                    return !process.HasExited;
                }
            }
            catch (ArgumentException)
            {
                return false;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        #endregion
    }
}
