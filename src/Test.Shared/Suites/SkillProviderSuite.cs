namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Models;
    using Mux.Core.Skills;
    using Mux.Core.Tools;
    using Touchstone.Core;

    /// <summary>
    /// Touchstone suite for the skills execution path: <see cref="SkillToolProvider"/> exposes the two
    /// tools, <c>skill</c> returns a skill's instructions, and <c>run_skill</c> runs a command through
    /// <see cref="SkillExecutor"/> — an inline block or a bundled script — honoring timeouts and reporting
    /// the process-shaped result. Uses <c>bash</c>, which is present on the dev environment and both CI
    /// runners.
    /// </summary>
    public static class SkillProviderSuite
    {
        private const string SuiteId = "SkillProvider";

        /// <summary>
        /// Builds the skill-provider suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/> for the execution and provider cases.</returns>
        public static TestSuiteDescriptor Create()
        {
            return new TestSuiteDescriptor(
                SuiteId,
                "Skill execution and the skill/run_skill tools",
                new List<TestCaseDescriptor>
                {
                    Case("ToolDefinitionsAndMutationKinds", "The provider exposes skill (read-only) and run_skill (mutating)", (CancellationToken ct) =>
                        WithSkillsDirAsync((root) =>
                        {
                            SkillToolProvider provider = BuildProvider(root, "echoer", EchoBlockSkill("echoer"));

                            IReadOnlyList<ToolDefinition> tools = provider.GetToolDefinitions();
                            MuxAssert.AreEqual(2, tools.Count, "two skill tools exposed");
                            MuxAssert.IsTrue(provider.HasTool("skill"), "skill tool present");
                            MuxAssert.IsTrue(provider.HasTool("run_skill"), "run_skill tool present");
                            MuxAssert.AreEqual(ToolMutationKind.ReadOnly, provider.GetMutationKind("skill"), "skill is read-only");
                            MuxAssert.AreEqual(ToolMutationKind.Mutating, provider.GetMutationKind("run_skill"), "run_skill is mutating");
                            return Task.CompletedTask;
                        })),

                    Case("NoEnabledSkillsYieldsNoTools", "The provider exposes no tools when nothing is enabled and valid", (CancellationToken ct) =>
                        WithSkillsDirAsync((root) =>
                        {
                            // An invalid skill (bad interpreter) must not surface the tools.
                            string fm = "---\nname: broken\ndescription: x\ncommands:\n  - name: go\n    block: b\n    interpreter: ruby\n---\n```ruby id=b\n1\n```\n";
                            SkillToolProvider provider = BuildProvider(root, "broken", fm);
                            MuxAssert.AreEqual(0, provider.GetToolDefinitions().Count, "no tools for an invalid library");
                            return Task.CompletedTask;
                        })),

                    Case("OpenSkillReturnsInstructions", "The skill tool returns the body and command list", (CancellationToken ct) =>
                        WithSkillsDirAsync(async (root) =>
                        {
                            SkillToolProvider provider = BuildProvider(root, "echoer", EchoBlockSkill("echoer"));
                            ToolResult result = await ExecuteAsync(provider, "{\"name\":\"echoer\"}", ct).ConfigureAwait(false);
                            MuxAssert.IsTrue(result.Success, "skill open succeeded");
                            MuxAssert.Contains("How to use", result.Content, "body returned");
                            MuxAssert.Contains("say", result.Content, "command listed");
                        })),

                    Case("RunSkillExecutesBlock", "run_skill runs an inline block and returns its output", (CancellationToken ct) =>
                        WithSkillsDirAsync(async (root) =>
                        {
                            SkillToolProvider provider = BuildProvider(root, "echoer", EchoBlockSkill("echoer"));
                            ToolResult result = await ExecuteRunAsync(provider, "echoer", "say", root, ct).ConfigureAwait(false);
                            MuxAssert.IsTrue(result.Success, "run succeeded: " + result.Content);
                            MuxAssert.Contains("SKILL_BLOCK_MARKER", result.Content, "block output captured");
                        })),

                    Case("RunSkillExecutesBundledScript", "run_skill runs a bundled script and returns its output", (CancellationToken ct) =>
                        WithSkillsDirAsync(async (root) =>
                        {
                            string fm = "---\nname: scripted\ndescription: runs a bundled script\nmutating: false\ncommands:\n  - name: go\n    run: scripts/go.js\n    interpreter: node\n---\n## How to use\n\nRuns a script.\n";
                            string dir = WriteSkill(root, "scripted", fm);
                            Directory.CreateDirectory(Path.Combine(dir, "scripts"));
                            File.WriteAllText(Path.Combine(dir, "scripts", "go.js"), "console.log('SKILL_SCRIPT_MARKER');\n");

                            SkillToolProvider provider = new SkillToolProvider(new SkillCatalog(new SkillLoader(root).Discover()), new SkillExecutor());
                            ToolResult result = await ExecuteRunAsync(provider, "scripted", "go", root, ct).ConfigureAwait(false);
                            MuxAssert.IsTrue(result.Success, "script run succeeded: " + result.Content);
                            MuxAssert.Contains("SKILL_SCRIPT_MARKER", result.Content, "script output captured");
                        })),

                    Case("RunSkillTimesOut", "run_skill kills a command that exceeds its timeout", (CancellationToken ct) =>
                        WithSkillsDirAsync(async (root) =>
                        {
                            string fm = "---\nname: slow\ndescription: sleeps too long\nmutating: false\ncommands:\n  - name: wait\n    block: wait\n    interpreter: node\n    timeoutMs: 1000\n---\n## How to use\n\nSleeps.\n\n```js id=wait\nsetTimeout(function () {}, 10000);\n```\n";
                            SkillToolProvider provider = BuildProvider(root, "slow", fm);
                            ToolResult result = await ExecuteRunAsync(provider, "slow", "wait", root, ct).ConfigureAwait(false);
                            MuxAssert.IsFalse(result.Success, "timed-out run is not a success");
                            MuxAssert.Contains("timed_out", result.Content, "result reports timed_out");
                            MuxAssert.Contains("true", result.Content, "timed_out is true");
                        })),

                    Case("RunSkillUnknownSkillAndCommand", "run_skill reports unknown skills and commands", (CancellationToken ct) =>
                        WithSkillsDirAsync(async (root) =>
                        {
                            SkillToolProvider provider = BuildProvider(root, "echoer", EchoBlockSkill("echoer"));

                            ToolResult noSkill = await ExecuteRunAsync(provider, "ghost", "say", root, ct).ConfigureAwait(false);
                            MuxAssert.IsFalse(noSkill.Success, "unknown skill fails");
                            MuxAssert.Contains("skill_not_found", noSkill.Content, "skill_not_found reported");

                            ToolResult noCommand = await ExecuteRunAsync(provider, "echoer", "nope", root, ct).ConfigureAwait(false);
                            MuxAssert.IsFalse(noCommand.Success, "unknown command fails");
                            MuxAssert.Contains("command_not_found", noCommand.Content, "command_not_found reported");
                        }))
                });
        }

        #region Helpers

        private static TestCaseDescriptor Case(string id, string name, Func<CancellationToken, Task> body)
        {
            return new TestCaseDescriptor(SuiteId, id, name, body);
        }

        private static string EchoBlockSkill(string name)
        {
            // node is used rather than bash/pwsh because it resolves consistently on the dev machine and on
            // both CI runners; on Windows a bare "bash" often resolves to the WSL stub.
            return "---\n"
                + "name: " + name + "\n"
                + "description: echoes a marker\n"
                + "mutating: false\n"
                + "commands:\n"
                + "  - name: say\n"
                + "    block: say\n"
                + "    interpreter: node\n"
                + "---\n"
                + "## How to use\n\nRuns a demo.\n\n"
                + "```js id=say\nconsole.log('SKILL_BLOCK_MARKER');\n```\n";
        }

        private static SkillToolProvider BuildProvider(string root, string id, string content)
        {
            WriteSkill(root, id, content);
            return new SkillToolProvider(new SkillCatalog(new SkillLoader(root).Discover()), new SkillExecutor());
        }

        private static string WriteSkill(string root, string id, string content)
        {
            string dir = Path.Combine(root, id);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "SKILL.md"), content);
            return dir;
        }

        private static async Task<ToolResult> ExecuteAsync(SkillToolProvider provider, string json, CancellationToken ct)
        {
            using (JsonDocument doc = JsonDocument.Parse(json))
            {
                return await provider.ExecuteAsync("skill", doc.RootElement, Path.GetTempPath(), ct).ConfigureAwait(false);
            }
        }

        private static async Task<ToolResult> ExecuteRunAsync(SkillToolProvider provider, string skill, string command, string cwd, CancellationToken ct)
        {
            string json = JsonSerializer.Serialize(new { name = skill, command, working_directory = cwd });
            using (JsonDocument doc = JsonDocument.Parse(json))
            {
                return await provider.ExecuteAsync("run_skill", doc.RootElement, cwd, ct).ConfigureAwait(false);
            }
        }

        private static async Task WithSkillsDirAsync(Func<string, Task> body)
        {
            string root = Path.Combine(Path.GetTempPath(), "mux-skillexec-" + Guid.NewGuid().ToString("N"));
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
