namespace Test.Xunit.Tools
{
    using System;
    using System.IO;
    using System.Runtime.InteropServices;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using global::Xunit;
    using Mux.Core.Models;
    using Mux.Core.Tools.Tools;

    /// <summary>
    /// Unit tests for the <see cref="RunProcessTool"/> class.
    /// </summary>
    public class RunProcessToolTests : IDisposable
    {
        #region Private-Members

        private readonly string _TempDir;
        private readonly RunProcessTool _Tool;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new test instance with a temporary directory and tool.
        /// </summary>
        public RunProcessToolTests()
        {
            _TempDir = Path.Combine(Path.GetTempPath(), "mux_test_runprocess_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_TempDir);
            _Tool = new RunProcessTool();
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Verifies that running an echo command captures stdout correctly.
        /// </summary>
        [Fact]
        public async Task RunProcess_EchoCommand_CapturesStdout()
        {
            JsonElement args = MakeArgs(new { command = "echo hello from test" });
            ToolResult result = await _Tool.ExecuteAsync("call1", args, _TempDir, CancellationToken.None);

            Assert.True(result.Success);

            JsonDocument doc = JsonDocument.Parse(result.Content);
            string stdout = doc.RootElement.GetProperty("stdout").GetString()!;
            Assert.Contains("hello from test", stdout);

            int exitCode = doc.RootElement.GetProperty("exit_code").GetInt32();
            Assert.Equal(0, exitCode);
        }

        /// <summary>
        /// Verifies that the exit code from a failing command is returned correctly.
        /// </summary>
        [Fact]
        public async Task RunProcess_ExitCode_ReturnedCorrectly()
        {
            JsonElement args = MakeArgs(new { command = "exit 42" });
            ToolResult result = await _Tool.ExecuteAsync("call2", args, _TempDir, CancellationToken.None);

            Assert.False(result.Success);

            JsonDocument doc = JsonDocument.Parse(result.Content);
            int exitCode = doc.RootElement.GetProperty("exit_code").GetInt32();
            Assert.Equal(42, exitCode);
        }

        /// <summary>
        /// Verifies that stderr output is captured in the result.
        /// </summary>
        [Fact]
        public async Task RunProcess_StderrCapture_Works()
        {
            JsonElement args = MakeArgs(new { command = "echo error_output 1>&2" });
            ToolResult result = await _Tool.ExecuteAsync("call3", args, _TempDir, CancellationToken.None);

            JsonDocument doc = JsonDocument.Parse(result.Content);
            string stderr = doc.RootElement.GetProperty("stderr").GetString()!;
            Assert.Contains("error_output", stderr);
        }

        /// <summary>
        /// Verifies that a process exceeding the timeout is killed and reported as timed out.
        /// </summary>
        [Fact]
        public async Task RunProcess_Timeout_KillsProcess()
        {
            // Use ping with a long wait to simulate a hanging process.
            // On Windows: ping -n 30 127.0.0.1 takes ~30 seconds.
            JsonElement args = MakeArgs(new { command = "ping -n 30 127.0.0.1", timeout_ms = 1000 });
            ToolResult result = await _Tool.ExecuteAsync("call4", args, _TempDir, CancellationToken.None);

            Assert.False(result.Success);

            JsonDocument doc = JsonDocument.Parse(result.Content);
            bool timedOut = doc.RootElement.GetProperty("timed_out").GetBoolean();
            Assert.True(timedOut);
        }

        /// <summary>
        /// Verifies that the tool description exposes the active runtime shell and OS guidance.
        /// </summary>
        [Fact]
        public void RunProcess_Description_ExposesRuntimeShellContext()
        {
            Assert.Contains("Current runtime:", _Tool.Description);
            Assert.Contains(RuntimeInformation.OSDescription.Trim(), _Tool.Description);

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Assert.Contains("cmd.exe", _Tool.Description);
                Assert.Contains("/c", _Tool.Description);
            }
            else
            {
                Assert.Contains("/bin/sh", _Tool.Description);
                Assert.Contains("-c", _Tool.Description);
            }
        }

        /// <summary>
        /// Verifies that the tool schema exposes runtime metadata for shell-aware command generation.
        /// </summary>
        [Fact]
        public void RunProcess_ParametersSchema_ExposesRuntimeContextMetadata()
        {
            JsonElement schema = JsonSerializer.SerializeToElement(_Tool.ParametersSchema);

            Assert.Equal("object", schema.GetProperty("type").GetString());
            Assert.Contains("Runtime context:", schema.GetProperty("description").GetString());

            JsonElement runtimeContext = schema.GetProperty("mux_runtime_context");
            Assert.Equal(RuntimeInformation.OSDescription.Trim(), runtimeContext.GetProperty("operating_system").GetString());

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Assert.Equal("windows", runtimeContext.GetProperty("platform_family").GetString());
                Assert.Equal("cmd.exe", runtimeContext.GetProperty("shell_program").GetString());
                Assert.Equal("cmd.exe /c <command>", runtimeContext.GetProperty("shell_invocation").GetString());
            }
            else
            {
                Assert.Equal("unix", runtimeContext.GetProperty("platform_family").GetString());
                Assert.Equal("/bin/sh", runtimeContext.GetProperty("shell_program").GetString());
                Assert.Equal("/bin/sh -c \"<command>\"", runtimeContext.GetProperty("shell_invocation").GetString());
            }

            JsonElement commandProperty = schema.GetProperty("properties").GetProperty("command");
            Assert.Contains("mux_runtime_context", commandProperty.GetProperty("description").GetString());
        }

        /// <summary>
        /// Cleans up the temporary directory after tests complete.
        /// </summary>
        public void Dispose()
        {
            if (!Directory.Exists(_TempDir))
            {
                return;
            }

            for (int attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    Directory.Delete(_TempDir, recursive: true);
                    return;
                }
                catch (IOException) when (attempt < 4)
                {
                    Thread.Sleep(200);
                }
            }
        }

        #endregion

        #region Private-Methods

        private JsonElement MakeArgs(object obj)
        {
            return JsonSerializer.SerializeToElement(obj);
        }

        #endregion
    }
}
