namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Runtime.CompilerServices;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Cli.App;
    using Mux.Core.Agent;
    using Mux.Core.Enums;
    using Mux.Core.Jobs;
    using Mux.Core.Models;
    using Mux.Core.Settings;
    using Mux.Core.Skills;
    using Test.Shared.Support;
    using Touchstone.Core;
    using TUIKit.Terminal;

    /// <summary>
    /// Touchstone suite for invoking a skill by name: parsing <c>/name args</c>, substituting
    /// <c>$ARGUMENTS</c> and positional placeholders outside code fences, the <c>run_skill</c> hint for skills
    /// with commands, and the interactive shell's routing (built-in commands win, skills come next).
    /// </summary>
    public static class SkillInvocationSuite
    {
        private const string SuiteId = "SkillInvocation";

        /// <summary>
        /// Builds the skill-invocation suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/> for the invocation cases.</returns>
        public static TestSuiteDescriptor Create()
        {
            return new TestSuiteDescriptor(
                SuiteId,
                "Invoking skills by name with arguments",
                new List<TestCaseDescriptor>
                {
                    Case("ParseNameAndArguments", "Slash input splits into a lowercase name and argument text", (CancellationToken ct) =>
                    {
                        MuxAssert.IsTrue(SkillInvocationExpander.TryParse("/Code-Review  main --deep ", out string name, out string args), "parses");
                        MuxAssert.AreEqual("code-review", name, "lowercased name");
                        MuxAssert.AreEqual("main --deep", args, "trimmed arguments");
                        MuxAssert.IsTrue(SkillInvocationExpander.TryParse("/init", out string bare, out string none) && bare == "init" && none.Length == 0, "bare name");
                        MuxAssert.IsFalse(SkillInvocationExpander.TryParse("init", out _, out _), "needs a slash");
                        MuxAssert.IsFalse(SkillInvocationExpander.TryParse("/", out _, out _), "needs a name");
                        MuxAssert.IsFalse(SkillInvocationExpander.TryParse("/../etc", out _, out _), "rejects path-like names");
                        return Task.CompletedTask;
                    }),

                    Case("SubstitutesPlaceholdersInProseOnly", "$ARGUMENTS and $1..$9 are replaced in prose but not in code fences", (CancellationToken ct) =>
                    {
                        Skill skill = MakeSkill("review", "Review $ARGUMENTS against $1, focusing on $2.\n\n```bash\necho \"$1\"\n```\nMissing: [$3]", commands: false);
                        SkillInvocation invocation = SkillInvocationExpander.Expand(skill, "main 'error handling'");
                        MuxAssert.Contains("Review main 'error handling' against main, focusing on error handling.", invocation.Prompt, "prose substituted");
                        MuxAssert.Contains("echo \"$1\"", invocation.Prompt, "fenced code untouched");
                        MuxAssert.Contains("Missing: []", invocation.Prompt, "missing positional becomes empty");
                        MuxAssert.DoesNotContain("Arguments: ", invocation.Prompt, "no trailing arguments line when placeholders were used");
                        MuxAssert.IsTrue(invocation.IsPlaybook, "playbook flagged");
                        return Task.CompletedTask;
                    }),

                    Case("AppendsArgumentsWithoutPlaceholder", "Arguments are appended when the body has no placeholder", (CancellationToken ct) =>
                    {
                        SkillInvocation invocation = SkillInvocationExpander.Expand(MakeSkill("simplify", "Simplify the changed code.", commands: false), "src/app.ts");
                        MuxAssert.Contains("Simplify the changed code.", invocation.Prompt, "body included");
                        MuxAssert.Contains("Arguments: src/app.ts", invocation.Prompt, "arguments appended");
                        MuxAssert.Contains("\"simplify\" skill with these arguments: src/app.ts", invocation.Prompt, "header names the skill");
                        return Task.CompletedTask;
                    }),

                    Case("CommandSkillGetsRunSkillHint", "A skill with commands tells the model to use run_skill", (CancellationToken ct) =>
                    {
                        SkillInvocation invocation = SkillInvocationExpander.Expand(MakeSkill("js-test", "Runs tests.", commands: true), string.Empty);
                        MuxAssert.Contains("`run_skill` tool with name \"js-test\"", invocation.Prompt, "hint present");
                        MuxAssert.IsFalse(invocation.IsPlaybook, "not a playbook");
                        return Task.CompletedTask;
                    }),

                    Case("SplitArgumentsHonorsQuotes", "Positional arguments keep quoted runs together", (CancellationToken ct) =>
                    {
                        List<string> parts = SkillInvocationExpander.SplitArguments("a \"b c\" 'd e' f");
                        MuxAssert.AreEqual(4, parts.Count, "four parts");
                        MuxAssert.AreEqual("b c", parts[1], "double quotes");
                        MuxAssert.AreEqual("d e", parts[2], "single quotes");
                        MuxAssert.AreEqual(0, SkillInvocationExpander.SplitArguments(null).Count, "null is empty");
                        return Task.CompletedTask;
                    }),

                    Case("ShellRunsSkillAndBuiltInsWin", "The shell submits an expanded skill and lets built-in commands win", async (CancellationToken ct) =>
                    {
                        ProjectSkillsFixture f = new ProjectSkillsFixture();
                        string configDir = Path.Combine(f.Root, "config");
                        Directory.CreateDirectory(configDir);
                        try
                        {
                            using (SettingsLoader.PushConfigDirectoryOverride(configDir))
                            {
                                ProjectSkillsSuite.WritePlaybook(Path.Combine(f.Project, ".mux", "skills"), "team-review", "TEAM_REVIEW_BODY for $ARGUMENTS");
                                ProjectSkillsSuite.WritePlaybook(f.UserSkills, "skills", "SHOULD_NOT_RUN");

                                List<string> submitted = new List<string>();
                                object gate = new object();
                                using (SkillRuntime runtime = f.CreateRuntime())
                                {
                                    await runtime.RefreshNowAsync(ct).ConfigureAwait(false);
                                    HeadlessBackend backend = new HeadlessBackend(100, 30);
                                    await using (JobManager manager = new JobManager((Job job, string prompt, CancellationToken token) =>
                                    {
                                        lock (gate) { submitted.Add(prompt); }
                                        return CompletedRunner(token);
                                    }, maxConcurrency: 2))
                                    using (MuxTuiApp app = new MuxTuiApp(backend, manager, "demo", ApprovalPolicyEnum.AutoApprove, skillRuntime: runtime, workingDirectory: f.Project))
                                    {
                                        backend.FeedInput("/team-review src/auth" + "\r");
                                        app.PumpInputOnce();
                                        await WaitUntilAsync(() => { lock (gate) { return submitted.Count > 0; } }, ct).ConfigureAwait(false);
                                        lock (gate)
                                        {
                                            MuxAssert.Contains("TEAM_REVIEW_BODY for src/auth", submitted[0], "expanded body submitted");
                                        }

                                        backend.FeedInput("/skills" + "\r");
                                        app.PumpInputOnce();
                                        await WaitUntilAsync(() => app.IsModalActive, ct).ConfigureAwait(false);
                                        lock (gate)
                                        {
                                            MuxAssert.AreEqual(1, submitted.Count, "the built-in /skills ran instead of the skill");
                                        }
                                    }
                                }
                            }
                        }
                        finally
                        {
                            try { Directory.Delete(f.Root, true); } catch (Exception) { }
                        }
                    })
                });
        }

        #region Helpers

        private static TestCaseDescriptor Case(string id, string name, Func<CancellationToken, Task> body)
        {
            return new TestCaseDescriptor(SuiteId, id, name, body);
        }

        private static Skill MakeSkill(string name, string body, bool commands)
        {
            SkillManifest manifest = new SkillManifest { Name = name, Description = "desc " + name };
            if (commands)
            {
                manifest.Commands.Add(new SkillCommand { Name = "run", BlockId = "run", Interpreter = "pwsh" });
            }

            return new Skill { Manifest = manifest, Body = body };
        }

        private static async IAsyncEnumerable<AgentEvent> CompletedRunner([EnumeratorCancellation] CancellationToken ct)
        {
            await Task.CompletedTask.ConfigureAwait(false);
            yield return new RunCompletedEvent { RunId = Guid.NewGuid().ToString("N"), Status = "completed", IterationsCompleted = 1, DurationMs = 1 };
        }

        private static async Task WaitUntilAsync(Func<bool> condition, CancellationToken cancellationToken)
        {
            using (CancellationTokenSource timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30)))
            using (CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token))
            {
                while (!condition())
                {
                    await Task.Delay(10, linked.Token).ConfigureAwait(false);
                }
            }
        }

        #endregion
    }
}
