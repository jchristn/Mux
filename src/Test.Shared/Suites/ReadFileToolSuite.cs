namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Models;
    using Mux.Core.Tools.Tools;
    using Touchstone.Core;

    /// <summary>
    /// Touchstone suite for <see cref="ReadFileTool"/>. Ported from the <c>ReadFileToolTests</c> xUnit
    /// suite; each case creates and cleans up its own temporary directory.
    /// </summary>
    public static class ReadFileToolSuite
    {
        /// <summary>
        /// Builds the read-file-tool suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/> for the read-file-tool cases.</returns>
        public static TestSuiteDescriptor Create()
        {
            return new TestSuiteDescriptor(
                "ReadFileTool",
                "Read-file tool behavior",
                new List<TestCaseDescriptor>
                {
                    Case("ReadExistingFileReturnsContentWithLineNumbers", "Reading an existing file returns content with line numbers", async (string dir, ReadFileTool tool, CancellationToken ct) =>
                    {
                        string filePath = Path.Combine(dir, "test.txt");
                        File.WriteAllText(filePath, "line one\nline two\nline three\n");
                        ToolResult result = await tool.ExecuteAsync("call1", ToolArgs.From(new { file_path = filePath }), dir, ct).ConfigureAwait(false);
                        MuxAssert.IsTrue(result.Success, "success");
                        MuxAssert.Contains("1\tline one", result.Content, "line 1");
                        MuxAssert.Contains("2\tline two", result.Content, "line 2");
                        MuxAssert.Contains("3\tline three", result.Content, "line 3");
                    }),

                    Case("ReadMissingFileReturnsError", "Reading a missing file returns an error result", async (string dir, ReadFileTool tool, CancellationToken ct) =>
                    {
                        string filePath = Path.Combine(dir, "nonexistent.txt");
                        ToolResult result = await tool.ExecuteAsync("call2", ToolArgs.From(new { file_path = filePath }), dir, ct).ConfigureAwait(false);
                        MuxAssert.IsFalse(result.Success, "success is false");
                        MuxAssert.Contains("file_not_found", result.Content, "error code");
                    }),

                    Case("ReadWithOffsetReturnsCorrectLines", "The offset parameter starts reading from the correct line", async (string dir, ReadFileTool tool, CancellationToken ct) =>
                    {
                        string filePath = Path.Combine(dir, "offset.txt");
                        File.WriteAllText(filePath, "alpha\nbeta\ngamma\ndelta\n");
                        ToolResult result = await tool.ExecuteAsync("call3", ToolArgs.From(new { file_path = filePath, offset = 3 }), dir, ct).ConfigureAwait(false);
                        MuxAssert.IsTrue(result.Success, "success");
                        MuxAssert.DoesNotContain("alpha", result.Content, "no alpha");
                        MuxAssert.DoesNotContain("beta", result.Content, "no beta");
                        MuxAssert.Contains("3\tgamma", result.Content, "gamma");
                        MuxAssert.Contains("4\tdelta", result.Content, "delta");
                    }),

                    Case("ReadWithLimitReturnsLimitedLines", "The limit parameter restricts the number of returned lines", async (string dir, ReadFileTool tool, CancellationToken ct) =>
                    {
                        string filePath = Path.Combine(dir, "limit.txt");
                        File.WriteAllText(filePath, "one\ntwo\nthree\nfour\nfive\n");
                        ToolResult result = await tool.ExecuteAsync("call4", ToolArgs.From(new { file_path = filePath, limit = 2 }), dir, ct).ConfigureAwait(false);
                        MuxAssert.IsTrue(result.Success, "success");
                        MuxAssert.Contains("1\tone", result.Content, "one");
                        MuxAssert.Contains("2\ttwo", result.Content, "two");
                        MuxAssert.DoesNotContain("three", result.Content, "no three");
                    }),

                    Case("ReadWithOffsetAndLimitReturnsCorrectRange", "Offset and limit together return the correct range of lines", async (string dir, ReadFileTool tool, CancellationToken ct) =>
                    {
                        string filePath = Path.Combine(dir, "range.txt");
                        File.WriteAllText(filePath, "a\nb\nc\nd\ne\n");
                        ToolResult result = await tool.ExecuteAsync("call5", ToolArgs.From(new { file_path = filePath, offset = 2, limit = 2 }), dir, ct).ConfigureAwait(false);
                        MuxAssert.IsTrue(result.Success, "success");
                        MuxAssert.DoesNotContain("1\ta", result.Content, "no a");
                        MuxAssert.Contains("2\tb", result.Content, "b");
                        MuxAssert.Contains("3\tc", result.Content, "c");
                        MuxAssert.DoesNotContain("4\td", result.Content, "no d");
                    }),

                    Case("ReadCrlfFileNormalizesToLf", "A file with CRLF line endings has its output normalized to LF", async (string dir, ReadFileTool tool, CancellationToken ct) =>
                    {
                        string filePath = Path.Combine(dir, "crlf.txt");
                        File.WriteAllBytes(filePath, Encoding.UTF8.GetBytes("line1\r\nline2\r\nline3\r\n"));
                        ToolResult result = await tool.ExecuteAsync("call6", ToolArgs.From(new { file_path = filePath }), dir, ct).ConfigureAwait(false);
                        MuxAssert.IsTrue(result.Success, "success");
                        MuxAssert.DoesNotContain("\r", result.Content, "no CR");
                        MuxAssert.Contains("1\tline1", result.Content, "line1");
                        MuxAssert.Contains("2\tline2", result.Content, "line2");
                    })
                });
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, Func<string, ReadFileTool, CancellationToken, Task> body)
        {
            return new TestCaseDescriptor(
                "ReadFileTool",
                caseId,
                displayName,
                async (CancellationToken ct) =>
                {
                    string tempDir = Path.Combine(Path.GetTempPath(), "mux_test_readfile_" + Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(tempDir);
                    try
                    {
                        await body(tempDir, new ReadFileTool(), ct).ConfigureAwait(false);
                    }
                    finally
                    {
                        if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
                    }
                });
        }
    }
}
