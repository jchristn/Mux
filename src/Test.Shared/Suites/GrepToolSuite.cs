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
    /// Touchstone suite for <see cref="GrepTool"/>. Ported from the <c>GrepToolTests</c> xUnit suite;
    /// each case builds and cleans up the same temporary file set.
    /// </summary>
    public static class GrepToolSuite
    {
        /// <summary>
        /// Builds the grep-tool suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/> for the grep-tool cases.</returns>
        public static TestSuiteDescriptor Create()
        {
            return new TestSuiteDescriptor(
                "GrepTool",
                "Grep tool content search",
                new List<TestCaseDescriptor>
                {
                    Case("SimplePatternReturnsMatchingLines", "A simple literal pattern returns matching lines", async (string dir, GrepTool tool, CancellationToken ct) =>
                    {
                        ToolResult result = await tool.ExecuteAsync("call1", ToolArgs.From(new { pattern = "class", path = dir }), dir, ct).ConfigureAwait(false);
                        MuxAssert.IsTrue(result.Success, "success");
                        MuxAssert.Contains("class Foo", result.Content, "class Foo");
                        MuxAssert.Contains("class Bar", result.Content, "class Bar");
                    }),

                    Case("RegexPatternWorks", "A regex pattern with special syntax works correctly", async (string dir, GrepTool tool, CancellationToken ct) =>
                    {
                        ToolResult result = await tool.ExecuteAsync("call2", ToolArgs.From(new { pattern = @"\d+", path = dir }), dir, ct).ConfigureAwait(false);
                        MuxAssert.IsTrue(result.Success, "success");
                        MuxAssert.Contains("42", result.Content, "42");
                    }),

                    Case("WithIncludeFilterFiltersFiles", "The include filter restricts the search to matching file names", async (string dir, GrepTool tool, CancellationToken ct) =>
                    {
                        ToolResult result = await tool.ExecuteAsync("call3", ToolArgs.From(new { pattern = "class", path = dir, include = "*.cs" }), dir, ct).ConfigureAwait(false);
                        MuxAssert.IsTrue(result.Success, "success");
                        MuxAssert.Contains("class", result.Content, "class");
                        MuxAssert.DoesNotContain("readme.txt", result.Content, "no readme.txt");
                    }),

                    Case("NoMatchesReturnsEmptyResult", "Searching for a pattern with no matches returns an appropriate message", async (string dir, GrepTool tool, CancellationToken ct) =>
                    {
                        ToolResult result = await tool.ExecuteAsync("call4", ToolArgs.From(new { pattern = "zzzznonexistent", path = dir }), dir, ct).ConfigureAwait(false);
                        MuxAssert.IsTrue(result.Success, "success");
                        MuxAssert.Contains("No matches found", result.Content, "no matches message");
                    }),

                    Case("LimitTo100Matches", "Grep output is limited to 100 matches when there are more", async (string dir, GrepTool tool, CancellationToken ct) =>
                    {
                        StringBuilder sb = new StringBuilder();
                        for (int i = 0; i < 150; i++)
                        {
                            sb.AppendLine($"match_target line {i}");
                        }
                        File.WriteAllText(Path.Combine(dir, "many.txt"), sb.ToString());

                        ToolResult result = await tool.ExecuteAsync("call5", ToolArgs.From(new { pattern = "match_target", path = dir }), dir, ct).ConfigureAwait(false);
                        MuxAssert.IsTrue(result.Success, "success");
                        MuxAssert.Contains("truncated at 100 matches", result.Content, "truncation message");
                    })
                });
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, Func<string, GrepTool, CancellationToken, Task> body)
        {
            return new TestCaseDescriptor(
                "GrepTool",
                caseId,
                displayName,
                async (CancellationToken ct) =>
                {
                    string tempDir = Path.Combine(Path.GetTempPath(), "mux_test_grep_" + Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(tempDir);
                    File.WriteAllText(Path.Combine(tempDir, "code.cs"), "public class Foo\n{\n    int bar = 42;\n}\n");
                    File.WriteAllText(Path.Combine(tempDir, "readme.txt"), "This is a readme.\nNothing special here.\n");
                    File.WriteAllText(Path.Combine(tempDir, "data.cs"), "public class Bar\n{\n    string name = \"test\";\n}\n");
                    try
                    {
                        await body(tempDir, new GrepTool(), ct).ConfigureAwait(false);
                    }
                    finally
                    {
                        if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
                    }
                });
        }
    }
}
