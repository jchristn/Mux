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
    using Touchstone.Core;
    using TUIKit.Terminal;

    /// <summary>
    /// Touchstone suite for the interactive skills manager: the <c>/skills</c> command opens the inventory
    /// modal, populated from the live skill runtime. The lifecycle operations behind the modal are covered
    /// directly by <c>SkillManagerSuite</c>; this confirms the command surface is wired.
    /// </summary>
    public static class SkillManagementSuite
    {
        private const string SuiteId = "SkillManagement";

        /// <summary>
        /// Builds the skill-management suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/> for the manager surface.</returns>
        public static TestSuiteDescriptor Create()
        {
            return new TestSuiteDescriptor(
                SuiteId,
                "Interactive skills manager command surface",
                new List<TestCaseDescriptor>
                {
                    Case("SkillsCommandOpensModal", "The /skills command opens the inventory modal", (CancellationToken ct) =>
                        WithConfigDirAsync(async (skillsDir) =>
                        {
                            WriteNodeEchoSkill(skillsDir, "echoer");
                            using (SkillRuntime skillRuntime = new SkillRuntime(skillsDir, SettingsLoader.LoadSkillIndex, () => { }, TimeSpan.FromMilliseconds(50)))
                            {
                                skillRuntime.Start();
                                await skillRuntime.FirstRefreshCompleted.WaitAsync(TimeSpan.FromSeconds(5), ct).ConfigureAwait(false);

                                HeadlessBackend backend = new HeadlessBackend(100, 30);
                                await using (JobManager manager = new JobManager(EchoRunner, maxConcurrency: 2))
                                using (MuxTuiApp app = new MuxTuiApp(backend, manager, "demo", ApprovalPolicyEnum.AutoApprove, skillRuntime: skillRuntime))
                                {
                                    backend.FeedInput("/skills" + "\r");
                                    app.PumpInputOnce();
                                    await WaitUntilAsync(() => app.IsModalActive, ct).ConfigureAwait(false);
                                    MuxAssert.IsTrue(app.IsModalActive, "skills modal open");
                                }
                            }
                        }))
                });
        }

        #region Helpers

        private static TestCaseDescriptor Case(string id, string name, Func<CancellationToken, Task> body)
        {
            return new TestCaseDescriptor(SuiteId, id, name, body);
        }

        private static void WriteNodeEchoSkill(string root, string id)
        {
            string content = "---\nname: " + id + "\ndescription: echoes\nmutating: false\ncommands:\n  - name: say\n    block: say\n    interpreter: node\n---\n## How to use\n\nRuns.\n\n```js id=say\nconsole.log('x');\n```\n";
            string dir = Path.Combine(root, id);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "SKILL.md"), content);
        }

        private static async Task WithConfigDirAsync(Func<string, Task> body)
        {
            string configDir = Path.Combine(Path.GetTempPath(), "mux-skillui-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(configDir);
            using (SettingsLoader.PushConfigDirectoryOverride(configDir))
            {
                string skillsDir = Path.Combine(configDir, "skills");
                Directory.CreateDirectory(skillsDir);
                try
                {
                    await body(skillsDir).ConfigureAwait(false);
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
        }

        private static async IAsyncEnumerable<AgentEvent> EchoRunner(Job job, string prompt, [EnumeratorCancellation] CancellationToken ct)
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
