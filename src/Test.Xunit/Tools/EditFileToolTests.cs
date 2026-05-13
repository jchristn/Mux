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
    /// Unit tests for the <see cref="EditFileTool"/> class.
    /// </summary>
    public class EditFileToolTests : IDisposable
    {
        #region Private-Members

        private readonly string _TempDir;
        private readonly EditFileTool _Tool;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new test instance with a temporary directory and tool.
        /// </summary>
        public EditFileToolTests()
        {
            _TempDir = Path.Combine(Path.GetTempPath(), "mux_test_editfile_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_TempDir);
            _Tool = new EditFileTool();
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Verifies that a successful single edit returns success and modifies the file.
        /// </summary>
        [Fact]
        public async Task EditFile_SuccessfulEdit_ReturnsSuccess()
        {
            string filePath = Path.Combine(_TempDir, "edit.txt");
            File.WriteAllText(filePath, "hello world\ngoodbye world\n");

            JsonElement args = MakeArgs(new { file_path = filePath, old_string = "hello world", new_string = "hello universe" });
            ToolResult result = await _Tool.ExecuteAsync("call1", args, _TempDir, CancellationToken.None);

            Assert.True(result.Success);
            Assert.Contains("success", result.Content);
            string content = File.ReadAllText(filePath);
            Assert.Contains("hello universe", content);
            Assert.Contains("goodbye world", content);
        }

        /// <summary>
        /// Verifies that searching for a string not present in the file returns a structured error.
        /// </summary>
        [Fact]
        public async Task EditFile_StringNotFound_ReturnsStructuredError()
        {
            string filePath = Path.Combine(_TempDir, "notfound.txt");
            File.WriteAllText(filePath, "alpha\nbeta\n");

            JsonElement args = MakeArgs(new { file_path = filePath, old_string = "gamma", new_string = "delta" });
            ToolResult result = await _Tool.ExecuteAsync("call2", args, _TempDir, CancellationToken.None);

            Assert.False(result.Success);
            Assert.Contains("old_string_not_found", result.Content);
        }

        /// <summary>
        /// Verifies that an ambiguous match (duplicate text) returns a structured error with match details.
        /// </summary>
        [Fact]
        public async Task EditFile_AmbiguousMatch_ReturnsStructuredError()
        {
            string filePath = Path.Combine(_TempDir, "ambiguous.txt");
            File.WriteAllText(filePath, "foo bar\nbaz\nfoo bar\n");

            JsonElement args = MakeArgs(new { file_path = filePath, old_string = "foo bar", new_string = "replaced" });
            ToolResult result = await _Tool.ExecuteAsync("call3", args, _TempDir, CancellationToken.None);

            Assert.False(result.Success);
            Assert.Contains("ambiguous_match", result.Content);
            Assert.Contains("match_count", result.Content);
        }

        /// <summary>
        /// Verifies that editing a file with CRLF line endings preserves those endings after the edit.
        /// </summary>
        [Fact]
        public async Task EditFile_CrlfFile_PreservesLineEndings()
        {
            string filePath = Path.Combine(_TempDir, "crlf_edit.txt");
            File.WriteAllBytes(filePath, Encoding.UTF8.GetBytes("line1\r\nline2\r\nline3\r\n"));

            JsonElement args = MakeArgs(new { file_path = filePath, old_string = "line2", new_string = "replaced2" });
            ToolResult result = await _Tool.ExecuteAsync("call4", args, _TempDir, CancellationToken.None);

            Assert.True(result.Success);
            byte[] bytes = File.ReadAllBytes(filePath);
            string raw = Encoding.UTF8.GetString(bytes);
            Assert.Contains("line1\r\nreplaced2\r\nline3\r\n", raw);
        }

        /// <summary>
        /// Verifies that editing a file that does not exist returns a file not found error.
        /// </summary>
        [Fact]
        public async Task EditFile_FileNotFound_ReturnsError()
        {
            string filePath = Path.Combine(_TempDir, "missing.txt");

            JsonElement args = MakeArgs(new { file_path = filePath, old_string = "x", new_string = "y" });
            ToolResult result = await _Tool.ExecuteAsync("call5", args, _TempDir, CancellationToken.None);

            Assert.False(result.Success);
            Assert.Contains("file_not_found", result.Content);
        }

        /// <summary>
        /// Verifies that passing an empty old_string is handled gracefully without crashing.
        /// </summary>
        [Fact]
        public async Task EditFile_EmptyOldString_HandledGracefully()
        {
            string filePath = Path.Combine(_TempDir, "empty_old.txt");
            File.WriteAllText(filePath, "some content\n");

            JsonElement args = MakeArgs(new { file_path = filePath, old_string = "", new_string = "new" });
            ToolResult result = await _Tool.ExecuteAsync("call6", args, _TempDir, CancellationToken.None);

            // Empty string matches everywhere, so it should be ambiguous or produce a result,
            // but should not throw an unhandled exception.
            Assert.NotNull(result);
            Assert.NotNull(result.Content);
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
