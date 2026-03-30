namespace Test.Xunit.Tools
{
    using System;
    using System.Collections.Generic;
    using global::Xunit;
    using Mux.Core.Models;
    using Mux.Core.Tools;

    /// <summary>
    /// Unit tests for the <see cref="McpToolManager"/> class.
    /// </summary>
    public class McpToolManagerTests : IDisposable
    {
        #region Private-Members

        private readonly McpToolManager _Manager;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new test instance with an empty McpToolManager.
        /// </summary>
        public McpToolManagerTests()
        {
            _Manager = new McpToolManager(new List<McpServerConfig>());
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Verifies that HasTool returns false for an unknown tool name.
        /// </summary>
        [Fact]
        public void HasTool_UnknownTool_ReturnsFalse()
        {
            bool result = _Manager.HasTool("nonexistent.tool");
            Assert.False(result);
        }

        /// <summary>
        /// Verifies that GetToolDefinitions returns an empty list when no servers are configured.
        /// </summary>
        [Fact]
        public void GetToolDefinitions_EmptyManager_ReturnsEmpty()
        {
            List<ToolDefinition> definitions = _Manager.GetToolDefinitions();
            Assert.NotNull(definitions);
            Assert.Empty(definitions);
        }

        /// <summary>
        /// Verifies that GetServerStatus returns an empty list when no servers are configured.
        /// </summary>
        [Fact]
        public void GetServerStatus_EmptyManager_ReturnsEmpty()
        {
            List<(string Name, int ToolCount, bool Connected)> status = _Manager.GetServerStatus();
            Assert.NotNull(status);
            Assert.Empty(status);
        }

        /// <summary>
        /// Disposes the test manager.
        /// </summary>
        public void Dispose()
        {
            _Manager.Dispose();
        }

        #endregion
    }
}
