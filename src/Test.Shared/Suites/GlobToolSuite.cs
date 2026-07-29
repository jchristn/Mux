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
    /// Touchstone suite for <see cref="GlobTool"/>. Ported from the <c>GlobToolTests</c> xUnit suite;
    /// each case builds and cleans up the same temporary file tree.
    /// </summary>
    public static class GlobToolSuite
    {
        /// <summary>
        /// Builds the glob-tool suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/> for the glob-tool cases.</returns>
        public static TestSuiteDescriptor Create()
        {
            return new TestSuiteDescriptor(
                "GlobTool",
                "Glob tool pattern matching",
                new List<TestCaseDescriptor>
                {
                    Case("MatchesFilesReturnsRelativePaths", "Glob returns relative paths for matched files", async (string dir, GlobTool tool, CancellationToken ct) =>
                    {
                        ToolResult result = await tool.ExecuteAsync("call1", ToolArgs.From(new { pattern = "**/*.cs", path = dir }), dir, ct).ConfigureAwait(false);
                        MuxAssert.IsTrue(result.Success, "success");
                        MuxAssert.Contains("root.cs", result.Content, "root.cs");
                        MuxAssert.Contains("src/file1.cs", result.Content, "src/file1.cs");
                        MuxAssert.Contains("src/sub/deep.cs", result.Content, "src/sub/deep.cs");
                        MuxAssert.DoesNotContain("file2.txt", result.Content, "no file2.txt");
                    }),

                    Case("StarPatternMatchesInDirectory", "A single-star pattern matches files within a specific directory only", async (string dir, GlobTool tool, CancellationToken ct) =>
                    {
                        ToolResult result = await tool.ExecuteAsync("call2", ToolArgs.From(new { pattern = "src/*.cs", path = dir }), dir, ct).ConfigureAwait(false);
                        MuxAssert.IsTrue(result.Success, "success");
                        MuxAssert.Contains("src/file1.cs", result.Content, "src/file1.cs");
                        MuxAssert.DoesNotContain("deep.cs", result.Content, "no deep.cs");
                        MuxAssert.DoesNotContain("root.cs", result.Content, "no root.cs");
                    }),

                    Case("DoubleStarPatternMatchesRecursively", "The double-star pattern matches files recursively across subdirectories", async (string dir, GlobTool tool, CancellationToken ct) =>
                    {
                        ToolResult result = await tool.ExecuteAsync("call3", ToolArgs.From(new { pattern = "src/**/*.cs", path = dir }), dir, ct).ConfigureAwait(false);
                        MuxAssert.IsTrue(result.Success, "success");
                        MuxAssert.Contains("src/file1.cs", result.Content, "src/file1.cs");
                        MuxAssert.Contains("src/sub/deep.cs", result.Content, "src/sub/deep.cs");
                    }),

                    Case("NoMatchesReturnsEmptyResult", "A pattern with no matches reports zero matches", async (string dir, GlobTool tool, CancellationToken ct) =>
                    {
                        ToolResult result = await tool.ExecuteAsync("call4", ToolArgs.From(new { pattern = "**/*.xyz", path = dir }), dir, ct).ConfigureAwait(false);
                        MuxAssert.IsTrue(result.Success, "success");
                        MuxAssert.Contains("Found 0 matching file(s)", result.Content, "zero matches message");
                    })
                });
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, Func<string, GlobTool, CancellationToken, Task> body)
        {
            return new TestCaseDescriptor(
                "GlobTool",
                caseId,
                displayName,
                async (CancellationToken ct) =>
                {
                    string tempDir = Path.Combine(Path.GetTempPath(), "mux_test_glob_" + Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(tempDir);
                    Directory.CreateDirectory(Path.Combine(tempDir, "src"));
                    Directory.CreateDirectory(Path.Combine(tempDir, "src", "sub"));
                    File.WriteAllText(Path.Combine(tempDir, "root.txt"), "root");
                    File.WriteAllText(Path.Combine(tempDir, "root.cs"), "root cs");
                    File.WriteAllText(Path.Combine(tempDir, "src", "file1.cs"), "file1");
                    File.WriteAllText(Path.Combine(tempDir, "src", "file2.txt"), "file2");
                    File.WriteAllText(Path.Combine(tempDir, "src", "sub", "deep.cs"), "deep");
                    try
                    {
                        await body(tempDir, new GlobTool(), ct).ConfigureAwait(false);
                    }
                    finally
                    {
                        if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
                    }
                });
        }
    }
}
