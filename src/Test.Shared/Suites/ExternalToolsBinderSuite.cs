namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Cli.App;
    using Mux.Core.Agent;
    using Mux.Core.Models;
    using Touchstone.Core;

    /// <summary>
    /// Touchstone suite for <see cref="ExternalToolsBinder"/>: MCP and skills compose onto one template —
    /// MCP tools land in AdditionalTools with the legacy executor, the skills runtime is registered as a
    /// provider, and both prompt sections are appended to the base prompt.
    /// </summary>
    public static class ExternalToolsBinderSuite
    {
        private const string SuiteId = "ExternalToolsBinder";

        /// <summary>
        /// Builds the binder suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/> for the composition cases.</returns>
        public static TestSuiteDescriptor Create()
        {
            return new TestSuiteDescriptor(
                SuiteId,
                "MCP and skills compose onto the agent template",
                new List<TestCaseDescriptor>
                {
                    Case("ComposesMcpAndSkills", "MCP tools and skills both bind and appear in the prompt", (CancellationToken ct) =>
                        WithSkillsDirAsync(async (root) =>
                        {
                            WriteNodeEchoSkill(root, "echoer");
                            using (SkillRuntime skillRuntime = new SkillRuntime(root, () => new List<SkillIndexEntry>(), () => { }, TimeSpan.FromMilliseconds(50)))
                            {
                                skillRuntime.Start();
                                await skillRuntime.FirstRefreshCompleted.WaitAsync(TimeSpan.FromSeconds(5), ct).ConfigureAwait(false);

                                AgentLoopOptions template = new AgentLoopOptions(new EndpointConfig { Name = "e", BaseUrl = "http://localhost", Model = "m" });
                                List<ToolDefinition> mcpTools = new List<ToolDefinition>
                                {
                                    new ToolDefinition { Name = "srv.tool", Description = "[MCP:srv] a tool" }
                                };

                                ExternalToolsBinder.Apply(template, "BASE PROMPT.", "COMPACT.", mcpTools, Executor, skillRuntime, 5);

                                MuxAssert.IsNotNull(template.AdditionalTools, "MCP tools bound");
                                MuxAssert.AreEqual(1, template.AdditionalTools!.Count, "one MCP tool");
                                MuxAssert.IsNotNull(template.ExternalToolProviders, "skills provider registered");
                                MuxAssert.AreEqual(1, template.ExternalToolProviders!.Count, "one provider");
                                MuxAssert.AreEqual(8, template.EffectiveToolCount, "5 built-in + 1 MCP + 2 skill tools");

                                MuxAssert.Contains("BASE PROMPT.", template.SystemPrompt, "base prompt preserved");
                                MuxAssert.Contains("srv.tool", template.SystemPrompt, "MCP tool in prompt");
                                MuxAssert.Contains("echoer", template.SystemPrompt, "skill in prompt");
                            }
                        })),

                    Case("NoMcpNoSkillsLeavesBase", "With neither MCP nor skills the prompt is exactly the base", (CancellationToken ct) =>
                    {
                        AgentLoopOptions template = new AgentLoopOptions(new EndpointConfig { Name = "e", BaseUrl = "http://localhost", Model = "m" });
                        ExternalToolsBinder.Apply(template, "BASE PROMPT.", "COMPACT.", new List<ToolDefinition>(), Executor, null, 5);

                        MuxAssert.IsNull(template.AdditionalTools, "no MCP tools");
                        MuxAssert.IsNull(template.ExternalToolProviders, "no providers");
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

        private static Task<ToolResult> Executor(string toolName, JsonElement arguments, string workingDirectory, CancellationToken cancellationToken)
        {
            return Task.FromResult(new ToolResult { ToolCallId = toolName, Success = true, Content = "{}" });
        }

        private static void WriteNodeEchoSkill(string root, string id)
        {
            string content = "---\nname: " + id + "\ndescription: echoes a marker\nmutating: false\ncommands:\n  - name: say\n    block: say\n    interpreter: node\n---\n## How to use\n\nRuns.\n\n```js id=say\nconsole.log('x');\n```\n";
            string dir = Path.Combine(root, id);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "SKILL.md"), content);
        }

        private static async Task WithSkillsDirAsync(Func<string, Task> body)
        {
            string root = Path.Combine(Path.GetTempPath(), "mux-binder-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                await body(root).ConfigureAwait(false);
            }
            finally
            {
                try
                {
                    if (Directory.Exists(root))
                    {
                        Directory.Delete(root, true);
                    }
                }
                catch (IOException)
                {
                }
            }
        }

        #endregion
    }
}
