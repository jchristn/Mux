namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Runtime.InteropServices;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Models;
    using Mux.Core.Tools.Tools;
    using Touchstone.Core;

    /// <summary>
    /// Touchstone suite for <see cref="RunProcessTool"/>. Ported from the <c>RunProcessToolTests</c>
    /// xUnit suite; each case creates and cleans up its own temporary directory.
    /// </summary>
    public static class RunProcessToolSuite
    {
        /// <summary>
        /// Builds the run-process-tool suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/> for the run-process-tool cases.</returns>
        public static TestSuiteDescriptor Create()
        {
            return new TestSuiteDescriptor(
                "RunProcessTool",
                "Run-process tool behavior",
                new List<TestCaseDescriptor>
                {
                    Case("EchoCommandCapturesStdout", "Running an echo command captures stdout", async (string dir, RunProcessTool tool, CancellationToken ct) =>
                    {
                        ToolResult result = await tool.ExecuteAsync("call1", ToolArgs.From(new { command = "echo hello from test" }), dir, ct).ConfigureAwait(false);
                        MuxAssert.IsTrue(result.Success, "success");
                        JsonDocument doc = JsonDocument.Parse(result.Content);
                        MuxAssert.Contains("hello from test", doc.RootElement.GetProperty("stdout").GetString(), "stdout");
                        MuxAssert.AreEqual(0, doc.RootElement.GetProperty("exit_code").GetInt32(), "exit code");
                    }),

                    Case("ExitCodeReturnedCorrectly", "The exit code from a failing command is returned correctly", async (string dir, RunProcessTool tool, CancellationToken ct) =>
                    {
                        ToolResult result = await tool.ExecuteAsync("call2", ToolArgs.From(new { command = "exit 42" }), dir, ct).ConfigureAwait(false);
                        MuxAssert.IsFalse(result.Success, "failure");
                        JsonDocument doc = JsonDocument.Parse(result.Content);
                        MuxAssert.AreEqual(42, doc.RootElement.GetProperty("exit_code").GetInt32(), "exit code");
                    }),

                    Case("StderrCaptureWorks", "stderr output is captured in the result", async (string dir, RunProcessTool tool, CancellationToken ct) =>
                    {
                        ToolResult result = await tool.ExecuteAsync("call3", ToolArgs.From(new { command = "echo error_output 1>&2" }), dir, ct).ConfigureAwait(false);
                        JsonDocument doc = JsonDocument.Parse(result.Content);
                        MuxAssert.Contains("error_output", doc.RootElement.GetProperty("stderr").GetString(), "stderr");
                    }),

                    Case("TimeoutKillsProcess", "A process exceeding the timeout is killed and reported as timed out", async (string dir, RunProcessTool tool, CancellationToken ct) =>
                    {
                        ToolResult result = await tool.ExecuteAsync("call4", ToolArgs.From(new { command = "ping -n 30 127.0.0.1", timeout_ms = 1000 }), dir, ct).ConfigureAwait(false);
                        MuxAssert.IsFalse(result.Success, "failure");
                        JsonDocument doc = JsonDocument.Parse(result.Content);
                        MuxAssert.IsTrue(doc.RootElement.GetProperty("timed_out").GetBoolean(), "timed_out");
                    }),

                    Case("DescriptionExposesRuntimeShellContext", "The tool description exposes the active runtime shell and OS guidance", (string dir, RunProcessTool tool, CancellationToken ct) =>
                    {
                        MuxAssert.Contains("Current runtime:", tool.Description, "runtime label");
                        MuxAssert.Contains(RuntimeInformation.OSDescription.Trim(), tool.Description, "OS description");
                        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                        {
                            MuxAssert.Contains("cmd.exe", tool.Description, "cmd.exe");
                            MuxAssert.Contains("/c", tool.Description, "/c");
                        }
                        else
                        {
                            MuxAssert.Contains("/bin/sh", tool.Description, "/bin/sh");
                            MuxAssert.Contains("-c", tool.Description, "-c");
                        }
                        return Task.CompletedTask;
                    }),

                    Case("ParametersSchemaExposesRuntimeContextMetadata", "The tool schema exposes runtime metadata for shell-aware command generation", (string dir, RunProcessTool tool, CancellationToken ct) =>
                    {
                        JsonElement schema = JsonSerializer.SerializeToElement(tool.ParametersSchema);
                        MuxAssert.AreEqual("object", schema.GetProperty("type").GetString(), "type");
                        MuxAssert.Contains("Runtime context:", schema.GetProperty("description").GetString(), "description");

                        JsonElement runtimeContext = schema.GetProperty("mux_runtime_context");
                        MuxAssert.AreEqual(RuntimeInformation.OSDescription.Trim(), runtimeContext.GetProperty("operating_system").GetString(), "operating_system");

                        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                        {
                            MuxAssert.AreEqual("windows", runtimeContext.GetProperty("platform_family").GetString(), "platform_family");
                            MuxAssert.AreEqual("cmd.exe", runtimeContext.GetProperty("shell_program").GetString(), "shell_program");
                            MuxAssert.AreEqual("cmd.exe /c <command>", runtimeContext.GetProperty("shell_invocation").GetString(), "shell_invocation");
                        }
                        else
                        {
                            MuxAssert.AreEqual("unix", runtimeContext.GetProperty("platform_family").GetString(), "platform_family");
                            MuxAssert.AreEqual("/bin/sh", runtimeContext.GetProperty("shell_program").GetString(), "shell_program");
                            MuxAssert.AreEqual("/bin/sh -c \"<command>\"", runtimeContext.GetProperty("shell_invocation").GetString(), "shell_invocation");
                        }

                        JsonElement commandProperty = schema.GetProperty("properties").GetProperty("command");
                        MuxAssert.Contains("mux_runtime_context", commandProperty.GetProperty("description").GetString(), "command description");
                        return Task.CompletedTask;
                    })
                });
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, Func<string, RunProcessTool, CancellationToken, Task> body)
        {
            return new TestCaseDescriptor(
                "RunProcessTool",
                caseId,
                displayName,
                async (CancellationToken ct) =>
                {
                    string tempDir = Path.Combine(Path.GetTempPath(), "mux_test_runprocess_" + Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(tempDir);
                    try
                    {
                        await body(tempDir, new RunProcessTool(), ct).ConfigureAwait(false);
                    }
                    finally
                    {
                        DeleteBestEffort(tempDir);
                    }
                });
        }

        private static void DeleteBestEffort(string dir)
        {
            if (!Directory.Exists(dir)) return;

            for (int attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    Directory.Delete(dir, recursive: true);
                    return;
                }
                catch (IOException) when (attempt < 4)
                {
                    Thread.Sleep(200);
                }
            }
        }
    }
}
