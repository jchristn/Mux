namespace Test.Xunit.Tools
{
    using System;
    using System.IO;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using global::Xunit;
    using Mux.Core.Models;
    using Mux.Core.Tools.Tools;

    /// <summary>
    /// Unit tests for the <see cref="GlobTool"/> class.
    /// </summary>
    public class GlobToolTests : IDisposable
    {
        #region Private-Members

        private readonly string _TempDir;
        private readonly GlobTool _Tool;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new test instance with a temporary directory and tool.
        /// </summary>
        public GlobToolTests()
        {
            _TempDir = Path.Combine(Path.GetTempPath(), "mux_test_glob_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_TempDir);
            _Tool = new GlobTool();

            // Create test file structure
            Directory.CreateDirectory(Path.Combine(_TempDir, "src"));
            Directory.CreateDirectory(Path.Combine(_TempDir, "src", "sub"));
            File.WriteAllText(Path.Combine(_TempDir, "root.txt"), "root");
            File.WriteAllText(Path.Combine(_TempDir, "root.cs"), "root cs");
            File.WriteAllText(Path.Combine(_TempDir, "src", "file1.cs"), "file1");
            File.WriteAllText(Path.Combine(_TempDir, "src", "file2.txt"), "file2");
            File.WriteAllText(Path.Combine(_TempDir, "src", "sub", "deep.cs"), "deep");
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Verifies that glob returns relative paths for matched files.
        /// </summary>
        [Fact]
        public async Task Glob_MatchesFiles_ReturnsRelativePaths()
        {
            JsonElement args = MakeArgs(new { pattern = "**/*.cs", path = _TempDir });
            ToolResult result = await _Tool.ExecuteAsync("call1", args, _TempDir, CancellationToken.None);

            Assert.True(result.Success);
            Assert.Contains("root.cs", result.Content);
            Assert.Contains("src/file1.cs", result.Content);
            Assert.Contains("src/sub/deep.cs", result.Content);
            Assert.DoesNotContain("file2.txt", result.Content);
        }

        /// <summary>
        /// Verifies that a single-star pattern matches files within a specific directory only.
        /// </summary>
        [Fact]
        public async Task Glob_StarPattern_MatchesInDirectory()
        {
            JsonElement args = MakeArgs(new { pattern = "src/*.cs", path = _TempDir });
            ToolResult result = await _Tool.ExecuteAsync("call2", args, _TempDir, CancellationToken.None);

            Assert.True(result.Success);
            Assert.Contains("src/file1.cs", result.Content);
            Assert.DoesNotContain("deep.cs", result.Content);
            Assert.DoesNotContain("root.cs", result.Content);
        }

        /// <summary>
        /// Verifies that the double-star pattern matches files recursively across subdirectories.
        /// </summary>
        [Fact]
        public async Task Glob_DoubleStarPattern_MatchesRecursively()
        {
            JsonElement args = MakeArgs(new { pattern = "src/**/*.cs", path = _TempDir });
            ToolResult result = await _Tool.ExecuteAsync("call3", args, _TempDir, CancellationToken.None);

            Assert.True(result.Success);
            Assert.Contains("src/file1.cs", result.Content);
            Assert.Contains("src/sub/deep.cs", result.Content);
        }

        /// <summary>
        /// Verifies that a pattern with no matches returns a result indicating zero matches.
        /// </summary>
        [Fact]
        public async Task Glob_NoMatches_ReturnsEmptyResult()
        {
            JsonElement args = MakeArgs(new { pattern = "**/*.xyz", path = _TempDir });
            ToolResult result = await _Tool.ExecuteAsync("call4", args, _TempDir, CancellationToken.None);

            Assert.True(result.Success);
            Assert.Contains("Found 0 matching file(s)", result.Content);
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
