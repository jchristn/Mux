namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Agent;
    using Mux.Core.Enums;
    using Mux.Core.Models;
    using Mux.Core.Settings;
    using Mux.Core.Tools;
    using Touchstone.Core;

    /// <summary>
    /// Touchstone suite for MCP configuration and composition: parsing <c>mcp-servers.json</c> (valid, empty, invalid
    /// JSON, unknown transports and auth types, missing names, defaults), the transport and auth converters, the
    /// server config model's guards and defaults, saving and loading through the config directory, environment
    /// variable expansion in all three syntaxes, the MCP prompt section, and the external tools binder with MCP tools.
    /// </summary>
    public static class McpConfigSuite
    {
        #region Private-Members

        private const string SuiteId = "McpConfig";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Builds the MCP config suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/> for the MCP config cases.</returns>
        public static TestSuiteDescriptor Create()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();
            void Add(string id, string name, Action body)
            {
                cases.Add(new TestCaseDescriptor(SuiteId, id, name, (CancellationToken ct) => { body(); return Task.CompletedTask; }));
            }

            Add("ParsesStdioAndHttpServers", "A file with stdio and HTTP servers parses every field", () =>
            {
                List<McpServerConfig> servers = SettingsLoader.ParseMcpServers(@"{ ""servers"": [
                    { ""name"": ""fs"", ""transport"": ""stdio"", ""command"": ""npx"", ""args"": [""-y"", ""@mcp/fs""], ""env"": { ""ROOT"": ""/tmp"" } },
                    { ""name"": ""web"", ""transport"": ""http"", ""url"": ""https://example.com"", ""mcpPath"": ""/rpc"", ""auth"": { ""type"": ""bearer"", ""bearerToken"": ""${TOKEN}"" } }
                ] }");
                MuxAssert.AreEqual(2, servers.Count, "two servers");
                MuxAssert.AreEqual(McpTransportTypeEnum.Stdio, servers[0].Transport, "stdio");
                MuxAssert.AreEqual("npx", servers[0].Command, "command");
                MuxAssert.AreEqual("-y,@mcp/fs", string.Join(",", servers[0].Args), "args");
                MuxAssert.AreEqual("/tmp", servers[0].Env["ROOT"], "env");
                MuxAssert.AreEqual(McpTransportTypeEnum.Http, servers[1].Transport, "http");
                MuxAssert.AreEqual("https://example.com", servers[1].Url, "url");
                MuxAssert.AreEqual("/rpc", servers[1].McpPath, "path");
                MuxAssert.AreEqual(McpAuthTypeEnum.Bearer, servers[1].Auth.Type, "auth type");
                MuxAssert.AreEqual("${TOKEN}", servers[1].Auth.BearerToken, "token kept unexpanded in config");
            });
            Add("EmptyAndMissingServerLists", "An empty object, a null list, and an empty list all parse to no servers", () =>
            {
                MuxAssert.AreEqual(0, SettingsLoader.ParseMcpServers("{}").Count, "empty object");
                MuxAssert.AreEqual(0, SettingsLoader.ParseMcpServers("{\"servers\":null}").Count, "null list");
                MuxAssert.AreEqual(0, SettingsLoader.ParseMcpServers("{\"servers\":[]}").Count, "empty list");
                MuxAssert.AreEqual(0, SettingsLoader.ParseMcpServers("null").Count, "JSON null");
                MuxAssert.Throws<ArgumentNullException>(() => SettingsLoader.ParseMcpServers(null!), "null text");
            });
            Add("InvalidFilesRejected", "Invalid JSON, unknown transports, and unknown auth types are rejected with an error", () =>
            {
                foreach (string bad in new[]
                {
                    "{ not json",
                    "{\"servers\":[{\"name\":\"x\",\"transport\":\"carrier-pigeon\"}]}",
                    "{\"servers\":[{\"name\":\"x\",\"transport\":\"http\",\"auth\":{\"type\":\"kerberos\"}}]}",
                    "{\"servers\":\"not a list\"}",
                    "[1,2,3]"
                })
                {
                    bool threw = false;
                    try { SettingsLoader.ParseMcpServers(bad); } catch (Exception) { threw = true; }
                    MuxAssert.IsTrue(threw, "rejects: " + bad);
                }
            });
            Add("UnknownTransportMessageListsChoices", "The unknown-transport error names the accepted values", () =>
            {
                JsonException error = MuxAssert.Throws<JsonException>(() => SettingsLoader.ParseMcpServers("{\"servers\":[{\"name\":\"x\",\"transport\":\"grpc\"}]}"), "unknown transport");
                MuxAssert.Contains("stdio or http", error.Message, "choices listed");
                JsonException auth = MuxAssert.Throws<JsonException>(() => SettingsLoader.ParseMcpServers("{\"servers\":[{\"name\":\"x\",\"auth\":{\"type\":\"oauth9\"}}]}"), "unknown auth");
                MuxAssert.Contains("none, bearer, or apikey", auth.Message, "auth choices listed");
            });
            Add("TransportAndAuthSpellingsAccepted", "Transport and auth values are case-insensitive and ignore dashes and underscores; blank means the default", () =>
            {
                foreach (string spelling in new[] { "HTTP", "Http", "h-t-t-p", "http" })
                {
                    MuxAssert.AreEqual(McpTransportTypeEnum.Http, SettingsLoader.ParseMcpServers("{\"servers\":[{\"name\":\"x\",\"transport\":\"" + spelling + "\"}]}")[0].Transport, spelling);
                }

                MuxAssert.AreEqual(McpTransportTypeEnum.Stdio, SettingsLoader.ParseMcpServers("{\"servers\":[{\"name\":\"x\",\"transport\":\"\"}]}")[0].Transport, "blank transport is stdio");
                MuxAssert.AreEqual(McpTransportTypeEnum.Stdio, SettingsLoader.ParseMcpServers("{\"servers\":[{\"name\":\"x\"}]}")[0].Transport, "missing transport is stdio");
                foreach (string spelling in new[] { "api-key", "API_KEY", "ApiKey", "apikey" })
                {
                    MuxAssert.AreEqual(McpAuthTypeEnum.ApiKey, SettingsLoader.ParseMcpServers("{\"servers\":[{\"name\":\"x\",\"auth\":{\"type\":\"" + spelling + "\"}}]}")[0].Auth.Type, spelling);
                }

                MuxAssert.AreEqual(McpAuthTypeEnum.None, SettingsLoader.ParseMcpServers("{\"servers\":[{\"name\":\"x\",\"auth\":{\"type\":\"\"}}]}")[0].Auth.Type, "blank auth is none");
            });
            Add("ConvertersWriteLowercase", "Transport and auth values serialize as lowercase strings and round-trip", () =>
            {
                McpServerConfig config = new McpServerConfig { Name = "x", Transport = McpTransportTypeEnum.Http, Url = "http://h", Auth = new McpAuthConfig { Type = McpAuthTypeEnum.ApiKey, ApiKeyHeader = "X-K", ApiKeyValue = "v" } };
                string json = JsonSerializer.Serialize(config);
                MuxAssert.Contains("\"transport\":\"http\"", json, "transport lowercase");
                MuxAssert.Contains("\"type\":\"apikey\"", json, "auth lowercase");
                McpServerConfig back = JsonSerializer.Deserialize<McpServerConfig>(json)!;
                MuxAssert.AreEqual(McpTransportTypeEnum.Http, back.Transport, "transport round-trips");
                MuxAssert.AreEqual("X-K", back.Auth.ApiKeyHeader, "header round-trips");
            });
            Add("ModelGuardsAndDefaults", "The config model rejects null names, commands, and URLs and fills sensible defaults", () =>
            {
                McpServerConfig config = new McpServerConfig();
                MuxAssert.AreEqual(McpTransportTypeEnum.Stdio, config.Transport, "stdio by default");
                MuxAssert.AreEqual("/mcp", config.McpPath, "default path");
                MuxAssert.AreEqual(0, config.Args.Count, "no args");
                MuxAssert.AreEqual(0, config.Env.Count, "no env");
                MuxAssert.AreEqual(McpAuthTypeEnum.None, config.Auth.Type, "no auth");
                MuxAssert.Throws<ArgumentNullException>(() => config.Name = null!, "null name");
                MuxAssert.Throws<ArgumentNullException>(() => config.Command = null!, "null command");
                MuxAssert.Throws<ArgumentNullException>(() => config.Url = null!, "null url");
                config.Args = null!;
                config.Env = null!;
                config.Auth = null!;
                config.McpPath = null!;
                MuxAssert.AreEqual(0, config.Args.Count, "null args become empty");
                MuxAssert.AreEqual(0, config.Env.Count, "null env becomes empty");
                MuxAssert.AreEqual(McpAuthTypeEnum.None, config.Auth.Type, "null auth becomes none");
                MuxAssert.AreEqual("/mcp", config.McpPath, "null path becomes /mcp");
            });
            Add("SaveAndLoadThroughConfigDirectory", "Servers saved to the config directory load back identically; a missing file loads empty", () =>
            {
                WithConfigDir((string dir) =>
                {
                    MuxAssert.AreEqual(0, SettingsLoader.LoadMcpServers().Count, "no file means no servers");
                    List<McpServerConfig> servers = new List<McpServerConfig>
                    {
                        new McpServerConfig { Name = "a", Command = "node", Args = new List<string> { "s.js" }, Env = new Dictionary<string, string> { ["K"] = "V" } },
                        new McpServerConfig { Name = "b", Transport = McpTransportTypeEnum.Http, Url = "http://localhost:9", Auth = new McpAuthConfig { Type = McpAuthTypeEnum.Bearer, BearerToken = "t" } }
                    };
                    SettingsLoader.SaveMcpServers(servers);
                    MuxAssert.IsTrue(File.Exists(Path.Combine(dir, "mcp-servers.json")), "file written");
                    List<McpServerConfig> loaded = SettingsLoader.LoadMcpServers();
                    MuxAssert.AreEqual(2, loaded.Count, "two loaded");
                    MuxAssert.AreEqual("V", loaded[0].Env["K"], "env kept");
                    MuxAssert.AreEqual("t", loaded[1].Auth.BearerToken, "auth kept");
                    SettingsLoader.SaveMcpServers(new List<McpServerConfig>());
                    MuxAssert.AreEqual(0, SettingsLoader.LoadMcpServers().Count, "an empty save clears");
                    MuxAssert.Throws<ArgumentNullException>(() => SettingsLoader.SaveMcpServers(null!), "null list");
                });
            });
            Add("CorruptFileThrowsOnLoad", "A corrupt mcp-servers.json is reported when loaded rather than silently ignored", () =>
            {
                WithConfigDir((string dir) =>
                {
                    File.WriteAllText(Path.Combine(dir, "mcp-servers.json"), "{ \"servers\": [ broken");
                    bool threw = false;
                    try { SettingsLoader.LoadMcpServers(); } catch (Exception) { threw = true; }
                    MuxAssert.IsTrue(threw, "corrupt file reported");
                });
            });
            Add("EnvironmentExpansionSyntaxes", "%VAR%, ${VAR}, and $env:VAR expand; unknown variables are left as written", () =>
            {
                Environment.SetEnvironmentVariable("MUX_TEST_EXPAND", "value1");
                try
                {
                    MuxAssert.AreEqual("a-value1-b", SettingsLoader.ExpandEnvironmentVariables("a-%MUX_TEST_EXPAND%-b"), "percent syntax");
                    MuxAssert.AreEqual("a-value1-b", SettingsLoader.ExpandEnvironmentVariables("a-${MUX_TEST_EXPAND}-b"), "brace syntax");
                    MuxAssert.AreEqual("a-value1-b", SettingsLoader.ExpandEnvironmentVariables("a-$env:MUX_TEST_EXPAND-b"), "PowerShell syntax");
                    MuxAssert.AreEqual("${MUX_TEST_NOT_DEFINED_X}", SettingsLoader.ExpandEnvironmentVariables("${MUX_TEST_NOT_DEFINED_X}"), "unknown left as written");
                    MuxAssert.AreEqual("plain", SettingsLoader.ExpandEnvironmentVariables("plain"), "no variables");
                    MuxAssert.AreEqual(string.Empty, SettingsLoader.ExpandEnvironmentVariables(string.Empty), "empty");
                    MuxAssert.IsNull(SettingsLoader.ExpandEnvironmentVariables(null!), "null passes through");
                    MuxAssert.AreEqual("value1value1", SettingsLoader.ExpandEnvironmentVariables("${MUX_TEST_EXPAND}%MUX_TEST_EXPAND%"), "several in one value");
                }
                finally
                {
                    Environment.SetEnvironmentVariable("MUX_TEST_EXPAND", null);
                }
            });
            Add("McpSectionListsEveryTool", "The MCP prompt section lists each tool with its description; no tools means no section", () =>
            {
                List<ToolDefinition> tools = new List<ToolDefinition>
                {
                    new ToolDefinition { Name = "fs.read", Description = "[MCP:fs] Read a file" },
                    new ToolDefinition { Name = "web.fetch", Description = "[MCP:web] Fetch a URL" }
                };
                string section = McpTemplateBinder.BuildMcpSection(tools);
                MuxAssert.Contains("- fs.read: [MCP:fs] Read a file", section, "first tool");
                MuxAssert.Contains("- web.fetch: [MCP:web] Fetch a URL", section, "second tool");
                MuxAssert.IsFalse(section.EndsWith("\n", StringComparison.Ordinal), "trimmed");
                MuxAssert.AreEqual(string.Empty, McpTemplateBinder.BuildMcpSection(null), "null tools");
                MuxAssert.AreEqual(string.Empty, McpTemplateBinder.BuildMcpSection(new List<ToolDefinition>()), "empty tools");
            });
            Add("BinderAddsMcpToolsAndCount", "The external tools binder exposes MCP tools, the executor, the prompt section, and the effective count", () =>
            {
                AgentLoopOptions template = new AgentLoopOptions(new EndpointConfig { Name = "e", BaseUrl = "http://localhost", Model = "m" });
                List<ToolDefinition> tools = new List<ToolDefinition> { new ToolDefinition { Name = "s.t", Description = "d" }, new ToolDefinition { Name = "s.u", Description = "e" } };
                Func<string, JsonElement, string, CancellationToken, Task<ToolResult>> executor = (string n, JsonElement a, string w, CancellationToken c) => Task.FromResult(new ToolResult { Success = true, Content = "ok" });
                ExternalToolsBinder.Apply(template, "BASE", "COMPACT", tools, executor, null, 10);
                MuxAssert.AreEqual(2, template.AdditionalTools!.Count, "tools exposed");
                MuxAssert.IsNotNull(template.ExternalToolExecutor, "executor set");
                MuxAssert.Contains("- s.t: d", template.SystemPrompt, "section in the prompt");
                MuxAssert.IsTrue(template.SystemPrompt.StartsWith("BASE", StringComparison.Ordinal), "base prompt first");
                MuxAssert.AreEqual(12, template.EffectiveToolCount, "built-ins plus MCP");
                ExternalToolsBinder.Apply(template, "BASE", "COMPACT", new List<ToolDefinition>(), null, null, 10);
                MuxAssert.IsNull(template.AdditionalTools, "no MCP tools clears the list");
                MuxAssert.IsNull(template.ExternalToolExecutor, "and the executor");
                MuxAssert.AreEqual("BASE", template.SystemPrompt, "and the section");
                MuxAssert.AreEqual(10, template.EffectiveToolCount, "count back to built-ins");
                MuxAssert.Throws<ArgumentNullException>(() => ExternalToolsBinder.Apply(null!, "B", "C", null, null, null, 0), "null template");
            });
            Add("RpcUrlJoining", "The RPC URL joins the base and path without doubled slashes", () =>
            {
                MuxAssert.AreEqual("http://h:1/mcp", McpConnectionDiagnostics.BuildRpcUrl("http://h:1/", "/mcp"), "trailing slash on the base");
                MuxAssert.AreEqual("http://h:1/mcp", McpConnectionDiagnostics.BuildRpcUrl("http://h:1", "/mcp"), "no trailing slash");
                MuxAssert.AreEqual("http://h:1/api/mcp", McpConnectionDiagnostics.BuildRpcUrl("http://h:1/api//", "/mcp"), "several trailing slashes");
                MuxAssert.AreEqual("/mcp", McpConnectionDiagnostics.BuildRpcUrl(null!, "/mcp"), "null base");
                MuxAssert.AreEqual("http://h", McpConnectionDiagnostics.BuildRpcUrl("http://h", null!), "null path");
            });
            Add("LogFilterEdgeCases", "The stdio log filter handles CRLF, Unicode, null lines, look-alike lines, and flushes through", () =>
            {
                StringWriter inner = new StringWriter();
                Mux.Core.McpServer.McpLogFilterWriter writer = new Mux.Core.McpServer.McpLogFilterWriter(inner);
                writer.Write("[t] Received: {\"id\":9,\"method\":\"tools/call\",\"params\":{\"prompt\":\"héllo 世界\"}}\r\n");
                writer.WriteLine((string?)null);
                writer.WriteLine("note: Received: {\"method\":\"x\"} is just text");
                writer.Write("partial line without newline");
                writer.Flush();
                string output = inner.ToString();
                MuxAssert.Contains("Received: tools/call (id 9, ", output, "CRLF line summarized");
                MuxAssert.DoesNotContain("世界", output, "Unicode prompt removed");
                MuxAssert.Contains("note: Received: {\"method\":\"x\"} is just text", output, "a line that only mentions Received is untouched");
                MuxAssert.DoesNotContain("partial line", output, "an unfinished line waits for its newline");
                MuxAssert.AreEqual(inner.Encoding, writer.Encoding, "encoding passes through");
                MuxAssert.Contains("bytes)", Mux.Core.McpServer.McpLogFilterWriter.Summarize("[t] Sent: {\"id\":\"abc\",\"result\":{}}"), "string ids summarized");
                MuxAssert.Contains("(id abc, ", Mux.Core.McpServer.McpLogFilterWriter.Summarize("[t] Sent: {\"id\":\"abc\",\"result\":{}}"), "string id kept");
            });
            Add("ResultAndStatusDefaults", "Connection results and server status start empty and disconnected", () =>
            {
                McpConnectionResult result = new McpConnectionResult();
                MuxAssert.IsFalse(result.Connected, "not connected");
                MuxAssert.AreEqual(0, result.ToolCount, "no tools");
                McpServerStatus status = new McpServerStatus();
                MuxAssert.IsFalse(status.Connected, "not connected");
                MuxAssert.IsNull(status.Error, "no error");
            });

            return new TestSuiteDescriptor(SuiteId, "MCP configuration: parsing, converters, models, persistence, expansion, composition", cases);
        }

        #endregion

        #region Private-Methods

        private static void WithConfigDir(Action<string> body)
        {
            string dir = Path.Combine(Path.GetTempPath(), "mux-mcpcfg-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            string? original = Environment.GetEnvironmentVariable("MUX_CONFIG_DIR");
            Environment.SetEnvironmentVariable("MUX_CONFIG_DIR", dir);
            try
            {
                body(dir);
            }
            finally
            {
                Environment.SetEnvironmentVariable("MUX_CONFIG_DIR", original);
                try { Directory.Delete(dir, true); } catch (Exception) { }
            }
        }

        #endregion
    }
}
