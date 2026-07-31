namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Cli.Commands;
    using Touchstone.Core;

    /// <summary>
    /// Touchstone suite for the <c>mux skill</c> CLI verb: exit codes and side effects for new, validate,
    /// run, and list, plus argument parsing. Each invocation targets an isolated config directory through
    /// <c>--config-dir</c>, and console output is captured so the run does not pollute the test log.
    /// </summary>
    public static class SkillCommandSuite
    {
        private const string SuiteId = "SkillCommand";

        /// <summary>
        /// Builds the skill-command suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/> for the CLI verb cases.</returns>
        public static TestSuiteDescriptor Create()
        {
            return new TestSuiteDescriptor(
                SuiteId,
                "The mux skill CLI verb",
                new List<TestCaseDescriptor>
                {
                    Case("NewCreatesSkill", "skill new creates a loadable skill and returns 0", (CancellationToken ct) =>
                        WithConfigDirAsync(async (configDir, skillsDir) =>
                        {
                            int code = await RunSkillAsync(new SkillSettings { Action = "new", Name = "cli-made", ConfigDir = configDir }, ct).ConfigureAwait(false);
                            MuxAssert.AreEqual(0, code, "new returns 0");
                            MuxAssert.IsTrue(File.Exists(Path.Combine(skillsDir, "cli-made", "SKILL.md")), "SKILL.md created");
                        })),

                    Case("ValidateReturnsNonZeroForInvalid", "skill validate returns nonzero when a skill is invalid", (CancellationToken ct) =>
                        WithConfigDirAsync(async (configDir, skillsDir) =>
                        {
                            string dir = Path.Combine(skillsDir, "broken");
                            Directory.CreateDirectory(dir);
                            File.WriteAllText(Path.Combine(dir, "SKILL.md"), "---\nname: broken\n---\n");

                            int code = await RunSkillAsync(new SkillSettings { Action = "validate", ConfigDir = configDir }, ct).ConfigureAwait(false);
                            MuxAssert.AreEqual(1, code, "validate returns 1 with an invalid skill");
                        })),

                    Case("RunExecutesCommand", "skill run executes a command and returns 0 on success", (CancellationToken ct) =>
                        WithConfigDirAsync(async (configDir, skillsDir) =>
                        {
                            WriteNodeEchoSkill(skillsDir, "runme");
                            int code = await RunSkillAsync(new SkillSettings { Action = "run", Name = "runme", Command = "say", ConfigDir = configDir }, ct).ConfigureAwait(false);
                            MuxAssert.AreEqual(0, code, "run returns 0 on success");
                        })),

                    Case("ListReturnsZero", "skill list returns 0", (CancellationToken ct) =>
                        WithConfigDirAsync(async (configDir, skillsDir) =>
                        {
                            WriteNodeEchoSkill(skillsDir, "listed");
                            int code = await RunSkillAsync(new SkillSettings { Action = "list", ConfigDir = configDir }, ct).ConfigureAwait(false);
                            MuxAssert.AreEqual(0, code, "list returns 0");
                        })),

                    Case("ParseMapsPositionalsAndArgs", "ParseSkill maps positionals and repeatable --arg", (CancellationToken ct) =>
                    {
                        SkillSettings settings = CliArgumentParser.ParseSkill(new[] { "run", "myskill", "docmd", "--arg", "one", "--arg", "two", "--cwd", "/tmp" });
                        MuxAssert.AreEqual("run", settings.Action, "action");
                        MuxAssert.AreEqual("myskill", settings.Name, "name");
                        MuxAssert.AreEqual("docmd", settings.Command, "command");
                        MuxAssert.AreEqual(2, settings.Args.Count, "two args");
                        MuxAssert.AreEqual("/tmp", settings.WorkingDirectory, "cwd");
                        return Task.CompletedTask;
                    })
                });
        }

        #region Helpers

        private static TestCaseDescriptor Case(string id, string name, Func<CancellationToken, Task> body)
        {
            return new TestCaseDescriptor(SuiteId, id, name, body);
        }

        private static async Task<int> RunSkillAsync(SkillSettings settings, CancellationToken ct)
        {
            TextWriter originalOut = Console.Out;
            TextWriter originalError = Console.Error;
            Console.SetOut(TextWriter.Null);
            Console.SetError(TextWriter.Null);
            try
            {
                return await new SkillCommand().ExecuteAsync(new CommandContext("skill", Array.Empty<string>()), settings, ct).ConfigureAwait(false);
            }
            finally
            {
                Console.SetOut(originalOut);
                Console.SetError(originalError);
            }
        }

        private static void WriteNodeEchoSkill(string root, string id)
        {
            string content = "---\nname: " + id + "\ndescription: echoes\nmutating: false\ncommands:\n  - name: say\n    block: say\n    interpreter: node\n---\n## How to use\n\nRuns.\n\n```js id=say\nconsole.log('x');\n```\n";
            string dir = Path.Combine(root, id);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "SKILL.md"), content);
        }

        private static async Task WithConfigDirAsync(Func<string, string, Task> body)
        {
            string configDir = Path.Combine(Path.GetTempPath(), "mux-skillcli-" + Guid.NewGuid().ToString("N"));
            string skillsDir = Path.Combine(configDir, "skills");
            Directory.CreateDirectory(skillsDir);
            try
            {
                await body(configDir, skillsDir).ConfigureAwait(false);
            }
            finally
            {
                try
                {
                    if (Directory.Exists(configDir))
                    {
                        Directory.Delete(configDir, true);
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
