namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Cli.App;
    using Mux.Core.Agent;
    using Mux.Core.Models;
    using Touchstone.Core;

    /// <summary>
    /// Touchstone suite for <see cref="McpTemplateBinder"/>: MCP tools are registered as callable tools and
    /// appended to the system prompt so the model is made aware of them, and removing the MCP tools restores
    /// the untouched base prompt.
    /// </summary>
    public static class McpTemplateBinderSuite
    {
        private const string SuiteId = "McpTemplateBinder";

        /// <summary>
        /// Builds the MCP template-binder suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/> for the binder cases.</returns>
        public static TestSuiteDescriptor Create()
        {
            return new TestSuiteDescriptor(
                SuiteId,
                "MCP tools are injected into the tool list and the system prompt",
                new List<TestCaseDescriptor>
                {
                    Case("InjectsToolsAndPrompt", "MCP tools become callable and are listed in the system prompt", (CancellationToken ct) =>
                    {
                        AgentLoopOptions template = NewTemplate();
                        List<ToolDefinition> builtIn = new List<ToolDefinition>
                        {
                            new ToolDefinition { Name = "read_file", Description = "Reads a file" },
                            new ToolDefinition { Name = "write_file", Description = "Writes a file" }
                        };
                        List<ToolDefinition> mcp = new List<ToolDefinition>
                        {
                            new ToolDefinition { Name = "docs.search", Description = "[MCP:docs] Searches the docs" }
                        };

                        McpTemplateBinder.Apply(template, "BASE PROMPT.", "COMPACT.", mcp, Executor, builtIn.Count);

                        MuxAssert.IsNotNull(template.AdditionalTools, "additional tools set");
                        MuxAssert.AreEqual(1, template.AdditionalTools!.Count, "one MCP tool registered");
                        MuxAssert.AreEqual("docs.search", template.AdditionalTools![0].Name, "MCP tool name registered");
                        MuxAssert.IsNotNull(template.ExternalToolExecutor, "external executor wired");
                        MuxAssert.AreEqual(3, template.EffectiveToolCount, "effective tool count includes MCP");

                        MuxAssert.Contains("BASE PROMPT.", template.SystemPrompt, "base prompt preserved");
                        MuxAssert.Contains("docs.search", template.SystemPrompt, "MCP tool named in prompt");
                        MuxAssert.Contains("[MCP:docs] Searches the docs", template.SystemPrompt, "MCP tool description in prompt");
                        MuxAssert.AreEqual("COMPACT.", template.CompactionSystemPrompt, "compaction prompt applied");

                        return Task.CompletedTask;
                    }),

                    Case("NoMcpToolsLeavesBasePrompt", "With no MCP tools the base prompt is untouched and no tools are added", (CancellationToken ct) =>
                    {
                        AgentLoopOptions template = NewTemplate();

                        McpTemplateBinder.Apply(template, "BASE PROMPT.", "COMPACT.", new List<ToolDefinition>(), Executor, 5);

                        MuxAssert.IsNull(template.AdditionalTools, "no additional tools");
                        MuxAssert.AreEqual("BASE PROMPT.", template.SystemPrompt, "prompt is exactly the base");
                        MuxAssert.AreEqual(5, template.EffectiveToolCount, "effective count is built-in only");

                        return Task.CompletedTask;
                    })
                });
        }

        #region Helpers

        private static TestCaseDescriptor Case(string id, string name, Func<CancellationToken, Task> body)
        {
            return new TestCaseDescriptor(SuiteId, id, name, body);
        }

        private static AgentLoopOptions NewTemplate()
        {
            return new AgentLoopOptions(new EndpointConfig { Name = "e", BaseUrl = "http://localhost", Model = "m" });
        }

        private static Task<ToolResult> Executor(string toolName, JsonElement arguments, string workingDirectory, CancellationToken cancellationToken)
        {
            return Task.FromResult(new ToolResult { ToolCallId = toolName, Success = true, Content = "{}" });
        }

        #endregion
    }
}
