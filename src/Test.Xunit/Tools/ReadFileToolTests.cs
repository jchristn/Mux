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
    /// Unit tests for the <see cref="ReadFileTool"/> class.
    /// </summary>
    public class ReadFileToolTests : IDisposable
    {
        #region Private-Members

        private readonly string _TempDir;
        private readonly ReadFileTool _Tool;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new test instance with a temporary directory and tool.
        /// </summary>
        public ReadFileToolTests()
        {
            _TempDir = Path.Combine(Path.GetTempPath(), "mux_test_readfile_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_TempDir);
            _Tool = new ReadFileTool();
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Verifies that reading an existing file returns content with line numbers.
        /// </summary>
        [Fact]
        public async Task ReadExistingFile_ReturnsContentWithLineNumbers()
        {
            string filePath = Path.Combine(_TempDir, "test.txt");
            File.WriteAllText(filePath, "line one\nline two\nline three\n");

            JsonElement args = MakeArgs(new { file_path = filePath });
            ToolResult result = await _Tool.ExecuteAsync("call1", args, _TempDir, CancellationToken.None);

            Assert.True(result.Success);
            Assert.Contains("1\tline one", result.Content);
            Assert.Contains("2\tline two", result.Content);
            Assert.Contains("3\tline three", result.Content);
        }

        /// <summary>
        /// Verifies that reading a missing file returns an error result.
        /// </summary>
        [Fact]
        public async Task ReadMissingFile_ReturnsError()
        {
            string filePath = Path.Combine(_TempDir, "nonexistent.txt");

            JsonElement args = MakeArgs(new { file_path = filePath });
            ToolResult result = await _Tool.ExecuteAsync("call2", args, _TempDir, CancellationToken.None);

            Assert.False(result.Success);
            Assert.Contains("file_not_found", result.Content);
        }

        /// <summary>
        /// Verifies that the offset parameter starts reading from the correct line.
        /// </summary>
        [Fact]
        public async Task ReadWithOffset_ReturnsCorrectLines()
        {
            string filePath = Path.Combine(_TempDir, "offset.txt");
            File.WriteAllText(filePath, "alpha\nbeta\ngamma\ndelta\n");

            JsonElement args = MakeArgs(new { file_path = filePath, offset = 3 });
            ToolResult result = await _Tool.ExecuteAsync("call3", args, _TempDir, CancellationToken.None);

            Assert.True(result.Success);
            Assert.DoesNotContain("alpha", result.Content);
            Assert.DoesNotContain("beta", result.Content);
            Assert.Contains("3\tgamma", result.Content);
            Assert.Contains("4\tdelta", result.Content);
        }

        /// <summary>
        /// Verifies that the limit parameter restricts the number of returned lines.
        /// </summary>
        [Fact]
        public async Task ReadWithLimit_ReturnsLimitedLines()
        {
            string filePath = Path.Combine(_TempDir, "limit.txt");
            File.WriteAllText(filePath, "one\ntwo\nthree\nfour\nfive\n");

            JsonElement args = MakeArgs(new { file_path = filePath, limit = 2 });
            ToolResult result = await _Tool.ExecuteAsync("call4", args, _TempDir, CancellationToken.None);

            Assert.True(result.Success);
            Assert.Contains("1\tone", result.Content);
            Assert.Contains("2\ttwo", result.Content);
            Assert.DoesNotContain("three", result.Content);
        }

        /// <summary>
        /// Verifies that offset and limit together return the correct range of lines.
        /// </summary>
        [Fact]
        public async Task ReadWithOffsetAndLimit_ReturnsCorrectRange()
        {
            string filePath = Path.Combine(_TempDir, "range.txt");
            File.WriteAllText(filePath, "a\nb\nc\nd\ne\n");

            JsonElement args = MakeArgs(new { file_path = filePath, offset = 2, limit = 2 });
            ToolResult result = await _Tool.ExecuteAsync("call5", args, _TempDir, CancellationToken.None);

            Assert.True(result.Success);
            Assert.DoesNotContain("1\ta", result.Content);
            Assert.Contains("2\tb", result.Content);
            Assert.Contains("3\tc", result.Content);
            Assert.DoesNotContain("4\td", result.Content);
        }

        /// <summary>
        /// Verifies that a file with CRLF line endings has its output normalized to LF.
        /// </summary>
        [Fact]
        public async Task ReadCrlfFile_NormalizesToLf()
        {
            string filePath = Path.Combine(_TempDir, "crlf.txt");
            File.WriteAllBytes(filePath, System.Text.Encoding.UTF8.GetBytes("line1\r\nline2\r\nline3\r\n"));

            JsonElement args = MakeArgs(new { file_path = filePath });
            ToolResult result = await _Tool.ExecuteAsync("call6", args, _TempDir, CancellationToken.None);

            Assert.True(result.Success);
            Assert.DoesNotContain("\r", result.Content);
            Assert.Contains("1\tline1", result.Content);
            Assert.Contains("2\tline2", result.Content);
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
