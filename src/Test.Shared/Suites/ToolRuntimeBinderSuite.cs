namespace Test.Shared.Suites
{
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Agent;
    using Mux.Core.Models;
    using Mux.Core.Tools;
    using Touchstone.Core;

    /// <summary>
    /// Touchstone suite for <see cref="ToolRuntimeBinder"/> — the stateful coordinator that composes MCP +
    /// skills onto an interactive template (promoted from the CLI's <c>ApplyTemplate</c> closure). These
    /// cases exercise the coordinator mechanics with no live runtimes: a baseline rebind leaves the template
    /// at its base prompt with the built-in tool count, a profile-prompt swap replaces the base prompt and
    /// rebinds, and null runtimes are safe. The MCP/skill composition itself is covered by the
    /// ExternalToolsBinder / McpTemplateBinder suites.
    /// </summary>
    public static class ToolRuntimeBinderSuite
    {
        private const string SuiteId = "ToolRuntimeBinder";

        /// <summary>
        /// Builds the tool-runtime-binder suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/> for coordinator cases.</returns>
        public static TestSuiteDescriptor Create()
        {
            return new TestSuiteDescriptor(
                SuiteId,
                "Tool runtime binder coordinator",
                new List<TestCaseDescriptor>
                {
                    Case("BaselineRebindUsesBasePrompt", "A baseline rebind with no external tools leaves the base prompt and built-in count", (CancellationToken ct) =>
                    {
                        AgentLoopOptions template = NewTemplate();
                        ToolRuntimeBinder binder = new ToolRuntimeBinder(template, "BASE PROMPT.", "COMPACT.", 5);
                        binder.Rebind();

                        MuxAssert.AreEqual("BASE PROMPT.", template.SystemPrompt, "system prompt is the base");
                        MuxAssert.AreEqual("COMPACT.", template.CompactionSystemPrompt, "compaction prompt applied");
                        MuxAssert.IsTrue(template.AdditionalTools == null, "no MCP tools bound");
                        MuxAssert.IsTrue(template.ExternalToolProviders == null, "no skill provider bound");
                        MuxAssert.AreEqual(5, template.EffectiveToolCount, "effective count is the built-in count");
                        return Task.CompletedTask;
                    }),

                    Case("SetProfilePromptSwapsBaseAndRebinds", "SetProfilePrompt replaces the base prompt and rebinds", (CancellationToken ct) =>
                    {
                        AgentLoopOptions template = NewTemplate();
                        ToolRuntimeBinder binder = new ToolRuntimeBinder(template, "BASE PROMPT.", "COMPACT.", 3);
                        binder.Rebind();

                        binder.SetProfilePrompt("NEW BASE.", "NEW COMPACT.");
                        MuxAssert.AreEqual("NEW BASE.", template.SystemPrompt, "base prompt swapped");
                        MuxAssert.AreEqual("NEW COMPACT.", template.CompactionSystemPrompt, "compaction swapped");
                        MuxAssert.AreEqual(3, template.EffectiveToolCount, "count unchanged with no external tools");
                        return Task.CompletedTask;
                    }),

                    Case("NullRuntimesAreSafe", "Rebinding with null MCP and skill runtimes does not throw", (CancellationToken ct) =>
                    {
                        AgentLoopOptions template = NewTemplate();
                        ToolRuntimeBinder binder = new ToolRuntimeBinder(template, "BASE.", "C.", 1)
                        {
                            McpRuntime = null,
                            SkillRuntime = null
                        };
                        binder.Rebind();

                        MuxAssert.AreEqual("BASE.", template.SystemPrompt, "base prompt intact");
                        MuxAssert.IsTrue(template.ExternalToolExecutor == null, "no executor with no MCP runtime");
                        return Task.CompletedTask;
                    })
                });
        }

        private static AgentLoopOptions NewTemplate()
        {
            return new AgentLoopOptions(new EndpointConfig { Name = "e", BaseUrl = "http://localhost", Model = "m" });
        }

        private static TestCaseDescriptor Case(string id, string name, System.Func<CancellationToken, Task> body)
        {
            return new TestCaseDescriptor(SuiteId, id, name, body);
        }
    }
}
