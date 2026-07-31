namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Models;
    using Mux.Core.Skills;
    using Mux.Core.Tools;
    using Touchstone.Core;

    /// <summary>
    /// Touchstone suite for the skills trust boundary: a skill can do exactly what <c>run_process</c> can do
    /// and no more, so caller-supplied arguments must reach the interpreter as separate argv entries without
    /// passing through a shell. These cases prove that shell metacharacters in an argument are delivered
    /// literally and never expanded, both at the resolver seam and end-to-end through <see cref="SkillExecutor"/>.
    /// Uses <c>node</c>, which resolves consistently on the dev machine and both CI runners.
    /// </summary>
    public static class SkillSecuritySuite
    {
        private const string SuiteId = "SkillSecurity";

        /// <summary>
        /// Builds the skill-security suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/> for the argv-isolation cases.</returns>
        public static TestSuiteDescriptor Create()
        {
            return new TestSuiteDescriptor(
                SuiteId,
                "Skill argument isolation (no shell injection)",
                new List<TestCaseDescriptor>
                {
                    Case("ResolverPassesArgumentsAsSeparateArgv", "The resolver appends each argument as its own argv entry with no shell", (CancellationToken ct) =>
                    {
                        List<string> arguments = new List<string> { "a b; echo SHELLRAN", "$(echo EXPANDED)", "`echo BACKTICK`" };
                        ProcessStartInfo startInfo = SkillInterpreterResolver.BuildStartInfo("node", "/tmp/script.js", arguments);

                        MuxAssert.IsFalse(startInfo.UseShellExecute, "UseShellExecute is false");
                        MuxAssert.AreEqual("node", startInfo.FileName, "interpreter is node");
                        MuxAssert.AreEqual(4, startInfo.ArgumentList.Count, "script plus three arguments");
                        MuxAssert.AreEqual("/tmp/script.js", startInfo.ArgumentList[0], "script is first argv");
                        MuxAssert.AreEqual("a b; echo SHELLRAN", startInfo.ArgumentList[1], "first argument is verbatim");
                        MuxAssert.AreEqual("$(echo EXPANDED)", startInfo.ArgumentList[2], "command substitution is verbatim");
                        MuxAssert.AreEqual("`echo BACKTICK`", startInfo.ArgumentList[3], "backtick expansion is verbatim");
                        return Task.CompletedTask;
                    }),

                    Case("RunSkillDeliversArgumentsUnexpanded", "run_skill delivers shell metacharacters to the interpreter literally", (CancellationToken ct) =>
                        WithSkillsDirAsync(async (root) =>
                        {
                            string dir = WriteSkill(root, "echoargs", EchoArgsSkill("echoargs"));
                            Skill skill = new SkillLoader(root).Load(dir);
                            MuxAssert.IsTrue(skill.IsValid, "echoargs valid: " + string.Join("; ", skill.Validation.Errors));
                            SkillCommand command = skill.Manifest.Commands[0];

                            List<string> arguments = new List<string> { "a b; echo SHELLRAN", "$(echo EXPANDED)", "`echo BACKTICK`", "&& echo CHAINED" };
                            ToolResult result = await new SkillExecutor().ExecuteAsync("t", skill, command, arguments, root, ct).ConfigureAwait(false);

                            MuxAssert.IsTrue(result.Success, "run succeeded: " + result.Content);

                            // The result is the process-shaped JSON; assert on the decoded stdout so the check
                            // sees the interpreter's literal output rather than JSON-escaped metacharacters.
                            string stdout;
                            using (JsonDocument document = JsonDocument.Parse(result.Content))
                            {
                                stdout = document.RootElement.GetProperty("stdout").GetString() ?? string.Empty;
                            }

                            // Every argument arrived as its own argv entry: none was split on ';' or merged.
                            MuxAssert.Contains("ARGC=4", stdout, "exactly four argv entries received");
                            MuxAssert.Contains("ARG0=[a b; echo SHELLRAN]", stdout, "spaces and semicolon preserved in one argument");
                            MuxAssert.Contains("ARG1=[$(echo EXPANDED)]", stdout, "command substitution not expanded");
                            MuxAssert.Contains("ARG2=[`echo BACKTICK`]", stdout, "backtick substitution not expanded");
                            MuxAssert.Contains("ARG3=[&& echo CHAINED]", stdout, "command chaining not interpreted");
                            // A shell would have run these and leaked their output as standalone lines.
                            MuxAssert.IsFalse(stdout.Contains("\nEXPANDED"), "no expanded substitution output leaked");
                            MuxAssert.IsFalse(stdout.Contains("\nCHAINED"), "no chained command output leaked");
                        }))
                });
        }

        #region Helpers

        private static TestCaseDescriptor Case(string id, string name, Func<CancellationToken, Task> body)
        {
            return new TestCaseDescriptor(SuiteId, id, name, body);
        }

        private static string EchoArgsSkill(string name)
        {
            return "---\n"
                + "name: " + name + "\n"
                + "description: echoes its arguments verbatim\n"
                + "mutating: false\n"
                + "commands:\n"
                + "  - name: echo\n"
                + "    block: echo\n"
                + "    interpreter: node\n"
                + "---\n"
                + "## How to use\n\nEchoes each argv entry so a test can verify no shell touched them.\n\n"
                + "```js id=echo\n"
                + "const args = process.argv.slice(2);\n"
                + "console.log('ARGC=' + args.length);\n"
                + "for (let i = 0; i < args.length; i++) { console.log('ARG' + i + '=[' + args[i] + ']'); }\n"
                + "```\n";
        }

        private static string WriteSkill(string root, string id, string content)
        {
            string dir = Path.Combine(root, id);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "SKILL.md"), content);
            return dir;
        }

        private static async Task WithSkillsDirAsync(Func<string, Task> body)
        {
            string root = Path.Combine(Path.GetTempPath(), "mux-skillsec-" + Guid.NewGuid().ToString("N"));
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
