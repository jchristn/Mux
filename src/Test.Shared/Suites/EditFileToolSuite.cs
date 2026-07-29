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
    /// Touchstone suite for <see cref="EditFileTool"/>. Ported from the <c>EditFileToolTests</c> xUnit
    /// suite; each case creates and cleans up its own temporary directory.
    /// </summary>
    public static class EditFileToolSuite
    {
        /// <summary>
        /// Builds the edit-file-tool suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/> for the edit-file-tool cases.</returns>
        public static TestSuiteDescriptor Create()
        {
            return new TestSuiteDescriptor(
                "EditFileTool",
                "Edit-file tool behavior",
                new List<TestCaseDescriptor>
                {
                    Case("SuccessfulEditReturnsSuccess", "A successful single edit modifies the file", async (string dir, EditFileTool tool, CancellationToken ct) =>
                    {
                        string filePath = Path.Combine(dir, "edit.txt");
                        File.WriteAllText(filePath, "hello world\ngoodbye world\n");
                        ToolResult result = await tool.ExecuteAsync("call1", ToolArgs.From(new { file_path = filePath, old_string = "hello world", new_string = "hello universe" }), dir, ct).ConfigureAwait(false);
                        MuxAssert.IsTrue(result.Success, "success");
                        MuxAssert.Contains("success", result.Content, "success message");
                        string content = File.ReadAllText(filePath);
                        MuxAssert.Contains("hello universe", content, "edited text");
                        MuxAssert.Contains("goodbye world", content, "untouched text");
                    }),

                    Case("StringNotFoundReturnsStructuredError", "Searching for an absent string returns a structured error", async (string dir, EditFileTool tool, CancellationToken ct) =>
                    {
                        string filePath = Path.Combine(dir, "notfound.txt");
                        File.WriteAllText(filePath, "alpha\nbeta\n");
                        ToolResult result = await tool.ExecuteAsync("call2", ToolArgs.From(new { file_path = filePath, old_string = "gamma", new_string = "delta" }), dir, ct).ConfigureAwait(false);
                        MuxAssert.IsFalse(result.Success, "failure");
                        MuxAssert.Contains("old_string_not_found", result.Content, "error code");
                    }),

                    Case("AmbiguousMatchReturnsStructuredError", "An ambiguous match returns a structured error with match details", async (string dir, EditFileTool tool, CancellationToken ct) =>
                    {
                        string filePath = Path.Combine(dir, "ambiguous.txt");
                        File.WriteAllText(filePath, "foo bar\nbaz\nfoo bar\n");
                        ToolResult result = await tool.ExecuteAsync("call3", ToolArgs.From(new { file_path = filePath, old_string = "foo bar", new_string = "replaced" }), dir, ct).ConfigureAwait(false);
                        MuxAssert.IsFalse(result.Success, "failure");
                        MuxAssert.Contains("ambiguous_match", result.Content, "ambiguous code");
                        MuxAssert.Contains("match_count", result.Content, "match count");
                    }),

                    Case("CrlfFilePreservesLineEndings", "Editing a CRLF file preserves those line endings", async (string dir, EditFileTool tool, CancellationToken ct) =>
                    {
                        string filePath = Path.Combine(dir, "crlf_edit.txt");
                        File.WriteAllBytes(filePath, Encoding.UTF8.GetBytes("line1\r\nline2\r\nline3\r\n"));
                        ToolResult result = await tool.ExecuteAsync("call4", ToolArgs.From(new { file_path = filePath, old_string = "line2", new_string = "replaced2" }), dir, ct).ConfigureAwait(false);
                        MuxAssert.IsTrue(result.Success, "success");
                        string raw = Encoding.UTF8.GetString(File.ReadAllBytes(filePath));
                        MuxAssert.Contains("line1\r\nreplaced2\r\nline3\r\n", raw, "CRLF preserved");
                    }),

                    Case("FileNotFoundReturnsError", "Editing a non-existent file returns a file-not-found error", async (string dir, EditFileTool tool, CancellationToken ct) =>
                    {
                        string filePath = Path.Combine(dir, "missing.txt");
                        ToolResult result = await tool.ExecuteAsync("call5", ToolArgs.From(new { file_path = filePath, old_string = "x", new_string = "y" }), dir, ct).ConfigureAwait(false);
                        MuxAssert.IsFalse(result.Success, "failure");
                        MuxAssert.Contains("file_not_found", result.Content, "error code");
                    }),

                    Case("EmptyOldStringHandledGracefully", "An empty old_string is handled without crashing", async (string dir, EditFileTool tool, CancellationToken ct) =>
                    {
                        string filePath = Path.Combine(dir, "empty_old.txt");
                        File.WriteAllText(filePath, "some content\n");
                        ToolResult result = await tool.ExecuteAsync("call6", ToolArgs.From(new { file_path = filePath, old_string = "", new_string = "new" }), dir, ct).ConfigureAwait(false);
                        MuxAssert.IsNotNull(result, "result not null");
                        MuxAssert.IsNotNull(result.Content, "content not null");
                    })
                });
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, Func<string, EditFileTool, CancellationToken, Task> body)
        {
            return new TestCaseDescriptor(
                "EditFileTool",
                caseId,
                displayName,
                async (CancellationToken ct) =>
                {
                    string tempDir = Path.Combine(Path.GetTempPath(), "mux_test_editfile_" + Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(tempDir);
                    try
                    {
                        await body(tempDir, new EditFileTool(), ct).ConfigureAwait(false);
                    }
                    finally
                    {
                        if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
                    }
                });
        }
    }
}
