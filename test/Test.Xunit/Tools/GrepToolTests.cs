namespace Test.Xunit.Tools
{
    using System;
    using System.IO;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using global::Xunit;
    using Mux.Core.Models;
    using Mux.Core.Tools.Tools;

    /// <summary>
    /// Unit tests for the <see cref="GrepTool"/> class.
    /// </summary>
    public class GrepToolTests : IDisposable
    {
        #region Private-Members

        private readonly string _TempDir;
        private readonly GrepTool _Tool;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new test instance with a temporary directory and tool.
        /// </summary>
        public GrepToolTests()
        {
            _TempDir = Path.Combine(Path.GetTempPath(), "mux_test_grep_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_TempDir);
            _Tool = new GrepTool();

            // Create test files
            File.WriteAllText(Path.Combine(_TempDir, "code.cs"), "public class Foo\n{\n    int bar = 42;\n}\n");
            File.WriteAllText(Path.Combine(_TempDir, "readme.txt"), "This is a readme.\nNothing special here.\n");
            File.WriteAllText(Path.Combine(_TempDir, "data.cs"), "public class Bar\n{\n    string name = \"test\";\n}\n");
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Verifies that a simple literal pattern returns matching lines with file and line number.
        /// </summary>
        [Fact]
        public async Task Grep_SimplePattern_ReturnsMatchingLines()
        {
            JsonElement args = MakeArgs(new { pattern = "class", path = _TempDir });
            ToolResult result = await _Tool.ExecuteAsync("call1", args, _TempDir, CancellationToken.None);

            Assert.True(result.Success);
            Assert.Contains("class Foo", result.Content);
            Assert.Contains("class Bar", result.Content);
        }

        /// <summary>
        /// Verifies that a regex pattern with special syntax works correctly.
        /// </summary>
        [Fact]
        public async Task Grep_RegexPattern_Works()
        {
            JsonElement args = MakeArgs(new { pattern = @"\d+", path = _TempDir });
            ToolResult result = await _Tool.ExecuteAsync("call2", args, _TempDir, CancellationToken.None);

            Assert.True(result.Success);
            Assert.Contains("42", result.Content);
        }

        /// <summary>
        /// Verifies that the include filter restricts the search to matching file names.
        /// </summary>
        [Fact]
        public async Task Grep_WithIncludeFilter_FiltersFiles()
        {
            JsonElement args = MakeArgs(new { pattern = "class", path = _TempDir, include = "*.cs" });
            ToolResult result = await _Tool.ExecuteAsync("call3", args, _TempDir, CancellationToken.None);

            Assert.True(result.Success);
            Assert.Contains("class", result.Content);
            Assert.DoesNotContain("readme.txt", result.Content);
        }

        /// <summary>
        /// Verifies that searching for a pattern with no matches returns an appropriate message.
        /// </summary>
        [Fact]
        public async Task Grep_NoMatches_ReturnsEmptyResult()
        {
            JsonElement args = MakeArgs(new { pattern = "zzzznonexistent", path = _TempDir });
            ToolResult result = await _Tool.ExecuteAsync("call4", args, _TempDir, CancellationToken.None);

            Assert.True(result.Success);
            Assert.Contains("No matches found", result.Content);
        }

        /// <summary>
        /// Verifies that grep output is limited to 100 matches when there are more.
        /// </summary>
        [Fact]
        public async Task Grep_LimitTo100Matches()
        {
            // Create a file with 150 matching lines
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < 150; i++)
            {
                sb.AppendLine($"match_target line {i}");
            }

            File.WriteAllText(Path.Combine(_TempDir, "many.txt"), sb.ToString());

            JsonElement args = MakeArgs(new { pattern = "match_target", path = _TempDir });
            ToolResult result = await _Tool.ExecuteAsync("call5", args, _TempDir, CancellationToken.None);

            Assert.True(result.Success);
            Assert.Contains("truncated at 100 matches", result.Content);
        }

        /// <summary>
        /// Cleans up the temporary directory after tests complete.
        /// </summary>
        public void Dispose()
        {
            if (Directory.Exists(_TempDir))
            {
                Directory.Delete(_TempDir, recursive: true);
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
