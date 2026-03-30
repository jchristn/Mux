namespace Test.Shared
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// A simple test fixture that simulates an MCP server over stdio for integration testing.
    /// Provides known tools: "echo" (returns input), "add" (adds two numbers), and "fail" (throws error).
    /// </summary>
    public class TestMcpServer : IDisposable
    {
        #region Private-Members

        private Process? _Process = null;
        private CancellationTokenSource? _Cts = null;
        private bool _Disposed = false;

        #endregion

        #region Public-Members

        /// <summary>
        /// The command used to launch the test MCP server. Returns "dotnet" for a script-based approach.
        /// </summary>
        public string Command => "dotnet";

        /// <summary>
        /// The command-line arguments for launching the test MCP server.
        /// </summary>
        public string[] Args => new string[] { "script", ScriptPath };

        /// <summary>
        /// Whether the test server is currently running.
        /// </summary>
        public bool IsRunning => _Process != null && !_Process.HasExited;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="TestMcpServer"/> class.
        /// </summary>
        public TestMcpServer()
        {
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Gets the path to the embedded MCP server script.
        /// Creates it in the temp directory if it does not already exist.
        /// </summary>
        public static string ScriptPath
        {
            get
            {
                string path = Path.Combine(Path.GetTempPath(), "mux_test_mcp_server.csx");

                if (!File.Exists(path))
                {
                    string script = GenerateServerScript();
                    File.WriteAllText(path, script);
                }

                return path;
            }
        }

        /// <summary>
        /// Starts the test MCP server process.
        /// </summary>
        public void Start()
        {
            if (_Process != null)
            {
                Stop();
            }

            _Process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = Command,
                    Arguments = string.Join(" ", Args),
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            _Process.Start();
        }

        /// <summary>
        /// Stops the test MCP server process.
        /// </summary>
        public void Stop()
        {
            if (_Process != null && !_Process.HasExited)
            {
                try
                {
                    _Process.StandardInput.Close();
                    bool exited = _Process.WaitForExit(5000);
                    if (!exited)
                    {
                        _Process.Kill(entireProcessTree: true);
                    }
                }
                catch (Exception)
                {
                    // Swallow errors during shutdown.
                }
            }

            _Process?.Dispose();
            _Process = null;
        }

        /// <summary>
        /// Releases all resources used by this <see cref="TestMcpServer"/> instance.
        /// </summary>
        public void Dispose()
        {
            if (!_Disposed)
            {
                Stop();
                _Cts?.Cancel();
                _Cts?.Dispose();
                _Disposed = true;
            }

            GC.SuppressFinalize(this);
        }

        #endregion

        #region Private-Methods

        private static string GenerateServerScript()
        {
            return @"// Minimal MCP server for testing — reads JSON-RPC from stdin, writes to stdout.
using System;
using System.IO;
using System.Text.Json;

while (true)
{
    string? line = Console.ReadLine();
    if (line == null) break;

    try
    {
        using JsonDocument doc = JsonDocument.Parse(line);
        JsonElement root = doc.RootElement;

        object? id = null;
        if (root.TryGetProperty(""id"", out JsonElement idEl))
        {
            id = idEl.GetInt32();
        }

        string method = root.GetProperty(""method"").GetString() ?? """";

        if (method == ""tools/list"")
        {
            var result = new
            {
                jsonrpc = ""2.0"",
                id = id,
                result = new
                {
                    tools = new object[]
                    {
                        new { name = ""echo"", description = ""Returns the input text"", inputSchema = new { type = ""object"", properties = new { text = new { type = ""string"" } } } },
                        new { name = ""add"", description = ""Adds two numbers"", inputSchema = new { type = ""object"", properties = new { a = new { type = ""number"" }, b = new { type = ""number"" } } } },
                        new { name = ""fail"", description = ""Always fails with an error"", inputSchema = new { type = ""object"", properties = new { } } }
                    }
                }
            };
            Console.WriteLine(JsonSerializer.Serialize(result));
        }
        else if (method == ""tools/call"")
        {
            JsonElement paramsEl = root.GetProperty(""params"");
            string toolName = paramsEl.GetProperty(""name"").GetString() ?? """";

            if (toolName == ""echo"")
            {
                string text = paramsEl.GetProperty(""arguments"").GetProperty(""text"").GetString() ?? """";
                var result = new { jsonrpc = ""2.0"", id = id, result = new { content = new object[] { new { type = ""text"", text = text } } } };
                Console.WriteLine(JsonSerializer.Serialize(result));
            }
            else if (toolName == ""add"")
            {
                double a = paramsEl.GetProperty(""arguments"").GetProperty(""a"").GetDouble();
                double b = paramsEl.GetProperty(""arguments"").GetProperty(""b"").GetDouble();
                var result = new { jsonrpc = ""2.0"", id = id, result = new { content = new object[] { new { type = ""text"", text = (a + b).ToString() } } } };
                Console.WriteLine(JsonSerializer.Serialize(result));
            }
            else if (toolName == ""fail"")
            {
                var result = new { jsonrpc = ""2.0"", id = id, error = new { code = -1, message = ""Intentional test failure"" } };
                Console.WriteLine(JsonSerializer.Serialize(result));
            }
            else
            {
                var result = new { jsonrpc = ""2.0"", id = id, error = new { code = -32601, message = $""Unknown tool: {toolName}"" } };
                Console.WriteLine(JsonSerializer.Serialize(result));
            }
        }
        else
        {
            var result = new { jsonrpc = ""2.0"", id = id, error = new { code = -32601, message = $""Unknown method: {method}"" } };
            Console.WriteLine(JsonSerializer.Serialize(result));
        }

        Console.Out.Flush();
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($""Error: {ex.Message}"");
    }
}
";
        }

        #endregion
    }
}
