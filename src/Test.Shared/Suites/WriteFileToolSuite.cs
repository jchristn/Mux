namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Models;
    using Mux.Core.Tools.Tools;
    using Touchstone.Core;

    /// <summary>
    /// Touchstone suite for <see cref="WriteFileTool"/>. Ported from the <c>WriteFileToolTests</c> xUnit
    /// suite; each case creates and cleans up its own temporary directory.
    /// </summary>
    public static class WriteFileToolSuite
    {
        /// <summary>
        /// Builds the write-file-tool suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/> for the write-file-tool cases.</returns>
        public static TestSuiteDescriptor Create()
        {
            return new TestSuiteDescriptor(
                "WriteFileTool",
                "Write-file tool behavior",
                new List<TestCaseDescriptor>
                {
                    Case("WriteNewFileCreatesFileWithContent", "Writing a new file creates it with the specified content", async (string dir, WriteFileTool tool, CancellationToken ct) =>
                    {
                        string filePath = Path.Combine(dir, "new.txt");
                        ToolResult result = await tool.ExecuteAsync("call1", ToolArgs.From(new { file_path = filePath, content = "hello\nworld\n" }), dir, ct).ConfigureAwait(false);
                        MuxAssert.IsTrue(result.Success, "success");
                        MuxAssert.IsTrue(File.Exists(filePath), "file exists");
                        string written = File.ReadAllText(filePath);
                        MuxAssert.Contains("hello", written, "hello");
                        MuxAssert.Contains("world", written, "world");
                    }),

                    Case("WriteNewFileCreatesParentDirectories", "Writing a file creates any missing parent directories", async (string dir, WriteFileTool tool, CancellationToken ct) =>
                    {
                        string filePath = Path.Combine(dir, "sub", "deep", "file.txt");
                        ToolResult result = await tool.ExecuteAsync("call2", ToolArgs.From(new { file_path = filePath, content = "nested content" }), dir, ct).ConfigureAwait(false);
                        MuxAssert.IsTrue(result.Success, "success");
                        MuxAssert.IsTrue(File.Exists(filePath), "file exists");
                        MuxAssert.AreEqual("nested content", File.ReadAllText(filePath), "content");
                    }),

                    Case("OverwriteExistingFilePreservesOriginalLineEndings", "Overwriting an existing CRLF file preserves CRLF line endings", async (string dir, WriteFileTool tool, CancellationToken ct) =>
                    {
                        string filePath = Path.Combine(dir, "crlf.txt");
                        File.WriteAllBytes(filePath, Encoding.UTF8.GetBytes("old line1\r\nold line2\r\n"));
                        ToolResult result = await tool.ExecuteAsync("call3", ToolArgs.From(new { file_path = filePath, content = "new line1\nnew line2\n" }), dir, ct).ConfigureAwait(false);
                        MuxAssert.IsTrue(result.Success, "success");
                        string raw = Encoding.UTF8.GetString(File.ReadAllBytes(filePath));
                        MuxAssert.Contains("\r\n", raw, "has CRLF");
                        MuxAssert.Contains("new line1\r\nnew line2\r\n", raw, "content with CRLF");
                    }),

                    Case("WriteNewFileUsesPlatformLineEnding", "Writing a brand-new file uses the platform default line ending", async (string dir, WriteFileTool tool, CancellationToken ct) =>
                    {
                        string filePath = Path.Combine(dir, "platform.txt");
                        ToolResult result = await tool.ExecuteAsync("call4", ToolArgs.From(new { file_path = filePath, content = "a\nb\n" }), dir, ct).ConfigureAwait(false);
                        MuxAssert.IsTrue(result.Success, "success");
                        string raw = Encoding.UTF8.GetString(File.ReadAllBytes(filePath));
                        string expected = "a" + Environment.NewLine + "b" + Environment.NewLine;
                        MuxAssert.AreEqual(expected, raw, "platform line endings");
                    })
                });
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, Func<string, WriteFileTool, CancellationToken, Task> body)
        {
            return new TestCaseDescriptor(
                "WriteFileTool",
                caseId,
                displayName,
                async (CancellationToken ct) =>
                {
                    string tempDir = Path.Combine(Path.GetTempPath(), "mux_test_writefile_" + Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(tempDir);
                    try
                    {
                        await body(tempDir, new WriteFileTool(), ct).ConfigureAwait(false);
                    }
                    finally
                    {
                        if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
                    }
                });
        }
    }
}
