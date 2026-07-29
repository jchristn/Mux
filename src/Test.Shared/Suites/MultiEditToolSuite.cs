namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Models;
    using Mux.Core.Tools.Tools;
    using Touchstone.Core;

    /// <summary>
    /// Touchstone suite for <see cref="MultiEditTool"/> atomic multi-edit behavior. Ported from the
    /// <c>MultiEditToolTests</c> xUnit suite; each case creates and cleans up its own temporary directory.
    /// </summary>
    public static class MultiEditToolSuite
    {
        /// <summary>
        /// Builds the multi-edit-tool suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/> for the multi-edit-tool cases.</returns>
        public static TestSuiteDescriptor Create()
        {
            return new TestSuiteDescriptor(
                "MultiEditTool",
                "Multi-edit tool atomic behavior",
                new List<TestCaseDescriptor>
                {
                    Case("AllEditsSucceedAppliesAtomically", "When all edits succeed they are applied atomically", async (string dir, MultiEditTool tool, CancellationToken ct) =>
                    {
                        string filePath = Path.Combine(dir, "multi.txt");
                        File.WriteAllText(filePath, "aaa\nbbb\nccc\n");
                        ToolResult result = await tool.ExecuteAsync("call1", ToolArgs.From(new
                        {
                            file_path = filePath,
                            edits = new[]
                            {
                                new { old_string = "aaa", new_string = "AAA" },
                                new { old_string = "ccc", new_string = "CCC" }
                            }
                        }), dir, ct).ConfigureAwait(false);
                        MuxAssert.IsTrue(result.Success, "success");
                        string content = File.ReadAllText(filePath);
                        MuxAssert.Contains("AAA", content, "AAA");
                        MuxAssert.Contains("bbb", content, "bbb");
                        MuxAssert.Contains("CCC", content, "CCC");
                    }),

                    Case("PartialFailureAbortsWithNoWrites", "When one edit fails validation, no edits are written", async (string dir, MultiEditTool tool, CancellationToken ct) =>
                    {
                        string filePath = Path.Combine(dir, "partial.txt");
                        string originalContent = "first line\nsecond line\n";
                        File.WriteAllText(filePath, originalContent);
                        ToolResult result = await tool.ExecuteAsync("call2", ToolArgs.From(new
                        {
                            file_path = filePath,
                            edits = new[]
                            {
                                new { old_string = "first line", new_string = "FIRST LINE" },
                                new { old_string = "nonexistent text", new_string = "whatever" }
                            }
                        }), dir, ct).ConfigureAwait(false);
                        MuxAssert.IsFalse(result.Success, "failure");
                        MuxAssert.Contains("old_string_not_found", result.Content, "error code");
                        MuxAssert.AreEqual(originalContent, File.ReadAllText(filePath), "file unchanged");
                    }),

                    Case("AmbiguousMatchAbortsWithEditIndex", "An ambiguous match in any edit aborts with the edit index", async (string dir, MultiEditTool tool, CancellationToken ct) =>
                    {
                        string filePath = Path.Combine(dir, "ambiguous.txt");
                        File.WriteAllText(filePath, "dup\nother\ndup\n");
                        ToolResult result = await tool.ExecuteAsync("call3", ToolArgs.From(new
                        {
                            file_path = filePath,
                            edits = new[]
                            {
                                new { old_string = "dup", new_string = "DUP" }
                            }
                        }), dir, ct).ConfigureAwait(false);
                        MuxAssert.IsFalse(result.Success, "failure");
                        MuxAssert.Contains("ambiguous_match", result.Content, "ambiguous code");
                        MuxAssert.Contains("edit_index", result.Content, "edit index");
                    }),

                    Case("SequentialEditsOrderMatters", "Edits are applied in sequence", async (string dir, MultiEditTool tool, CancellationToken ct) =>
                    {
                        string filePath = Path.Combine(dir, "sequential.txt");
                        File.WriteAllText(filePath, "aaa bbb ccc\n");
                        ToolResult result = await tool.ExecuteAsync("call4", ToolArgs.From(new
                        {
                            file_path = filePath,
                            edits = new[]
                            {
                                new { old_string = "aaa", new_string = "xxx" },
                                new { old_string = "ccc", new_string = "zzz" }
                            }
                        }), dir, ct).ConfigureAwait(false);
                        MuxAssert.IsTrue(result.Success, "success");
                        string content = File.ReadAllText(filePath);
                        MuxAssert.Contains("xxx", content, "xxx");
                        MuxAssert.Contains("bbb", content, "bbb");
                        MuxAssert.Contains("zzz", content, "zzz");
                        MuxAssert.DoesNotContain("aaa", content, "no aaa");
                        MuxAssert.DoesNotContain("ccc", content, "no ccc");
                    })
                });
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, Func<string, MultiEditTool, CancellationToken, Task> body)
        {
            return new TestCaseDescriptor(
                "MultiEditTool",
                caseId,
                displayName,
                async (CancellationToken ct) =>
                {
                    string tempDir = Path.Combine(Path.GetTempPath(), "mux_test_multiedit_" + Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(tempDir);
                    try
                    {
                        await body(tempDir, new MultiEditTool(), ct).ConfigureAwait(false);
                    }
                    finally
                    {
                        if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
                    }
                });
        }
    }
}
