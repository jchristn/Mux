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
    /// Unit tests for the <see cref="WriteFileTool"/> class.
    /// </summary>
    public class WriteFileToolTests : IDisposable
    {
        #region Private-Members

        private readonly string _TempDir;
        private readonly WriteFileTool _Tool;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new test instance with a temporary directory and tool.
        /// </summary>
        public WriteFileToolTests()
        {
            _TempDir = Path.Combine(Path.GetTempPath(), "mux_test_writefile_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_TempDir);
            _Tool = new WriteFileTool();
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Verifies that writing a new file creates it with the specified content.
        /// </summary>
        [Fact]
        public async Task WriteNewFile_CreatesFileWithContent()
        {
            string filePath = Path.Combine(_TempDir, "new.txt");

            JsonElement args = MakeArgs(new { file_path = filePath, content = "hello\nworld\n" });
            ToolResult result = await _Tool.ExecuteAsync("call1", args, _TempDir, CancellationToken.None);

            Assert.True(result.Success);
            Assert.True(File.Exists(filePath));
            string written = File.ReadAllText(filePath);
            Assert.Contains("hello", written);
            Assert.Contains("world", written);
        }

        /// <summary>
        /// Verifies that writing a file creates any missing parent directories.
        /// </summary>
        [Fact]
        public async Task WriteNewFile_CreatesParentDirectories()
        {
            string filePath = Path.Combine(_TempDir, "sub", "deep", "file.txt");

            JsonElement args = MakeArgs(new { file_path = filePath, content = "nested content" });
            ToolResult result = await _Tool.ExecuteAsync("call2", args, _TempDir, CancellationToken.None);

            Assert.True(result.Success);
            Assert.True(File.Exists(filePath));
            Assert.Equal("nested content", File.ReadAllText(filePath));
        }

        /// <summary>
        /// Verifies that overwriting an existing CRLF file preserves CRLF line endings.
        /// </summary>
        [Fact]
        public async Task OverwriteExistingFile_PreservesOriginalLineEndings()
        {
            string filePath = Path.Combine(_TempDir, "crlf.txt");
            File.WriteAllBytes(filePath, Encoding.UTF8.GetBytes("old line1\r\nold line2\r\n"));

            JsonElement args = MakeArgs(new { file_path = filePath, content = "new line1\nnew line2\n" });
            ToolResult result = await _Tool.ExecuteAsync("call3", args, _TempDir, CancellationToken.None);

            Assert.True(result.Success);
            byte[] bytes = File.ReadAllBytes(filePath);
            string raw = Encoding.UTF8.GetString(bytes);
            Assert.Contains("\r\n", raw);
            Assert.Contains("new line1\r\nnew line2\r\n", raw);
        }

        /// <summary>
        /// Verifies that writing a brand-new file uses the platform default line ending.
        /// </summary>
        [Fact]
        public async Task WriteNewFile_UsesPlatformLineEnding()
        {
            string filePath = Path.Combine(_TempDir, "platform.txt");

            JsonElement args = MakeArgs(new { file_path = filePath, content = "a\nb\n" });
            ToolResult result = await _Tool.ExecuteAsync("call4", args, _TempDir, CancellationToken.None);

            Assert.True(result.Success);
            byte[] bytes = File.ReadAllBytes(filePath);
            string raw = Encoding.UTF8.GetString(bytes);

            string expectedContent = "a" + Environment.NewLine + "b" + Environment.NewLine;
            Assert.Equal(expectedContent, raw);
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
