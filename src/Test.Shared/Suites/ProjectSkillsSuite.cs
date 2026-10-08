namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Enums;
    using Mux.Core.Models;
    using Mux.Core.Skills;
    using Test.Shared.Support;
    using Touchstone.Core;

    /// <summary>
    /// Touchstone suite for project-scoped skills: discovery under the project skill roots, shadowing of user
    /// skills, the trust gate on skills with commands, Claude Code frontmatter compatibility, and the reserved
    /// <c>list</c> name.
    /// </summary>
    public static class ProjectSkillsSuite
    {
        private const string SuiteId = "ProjectSkills";

        /// <summary>
        /// Builds the project-skills suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/> for the project-skill cases.</returns>
        public static TestSuiteDescriptor Create()
        {
            return new TestSuiteDescriptor(
                SuiteId,
                "Project-scoped skills, trust, and Claude-format compatibility",
                new List<TestCaseDescriptor>
                {
                    Case("PlaybookLoadsWithoutTrust", "A project playbook loads before any trust decision", async (ProjectSkillsFixture f, CancellationToken ct) =>
                    {
                        WritePlaybook(Path.Combine(f.Project, ".mux", "skills"), "team-review", "Review the team's way.");
                        using (SkillRuntime runtime = f.CreateRuntime())
                        {
                            await runtime.RefreshNowAsync(ct).ConfigureAwait(false);
                            MuxAssert.IsTrue(runtime.TryGetSkill("team-review", f.Project, out Skill skill), "playbook available");
                            MuxAssert.AreEqual(SkillScopeEnum.Project, skill.Scope, "project scope");
                            MuxAssert.IsFalse(runtime.TryGetSkill("team-review", null, out _), "not visible without a working directory");
                        }
                    }),

                    Case("CommandSkillBlockedUntilTrusted", "A project skill with commands is blocked until the project is trusted", async (ProjectSkillsFixture f, CancellationToken ct) =>
                    {
                        WriteNodeSkill(Path.Combine(f.Project, ".claude", "skills"), "repo-tool", "REPO_TOOL_MARKER");
                        using (SkillRuntime runtime = f.CreateRuntime())
                        {
                            await runtime.RefreshNowAsync(ct).ConfigureAwait(false);
                            MuxAssert.IsFalse(runtime.TryGetSkill("repo-tool", f.Project, out _), "blocked before trust");
                            SkillStatus status = FindStatus(runtime.GetStatus(f.Project), "repo-tool");
                            MuxAssert.IsTrue(status.CommandsBlocked, "status reports blocked");
                            MuxAssert.AreEqual("project", status.Scope, "status scope");
                            MuxAssert.AreEqual(1, runtime.GetView(f.Project)!.BlockedCount, "blocked count");

                            ToolResult refused = await RunAsync(runtime, "repo-tool", "say", f.Project, ct).ConfigureAwait(false);
                            MuxAssert.IsFalse(refused.Success, "run refused while blocked");

                            runtime.SetProjectTrust(f.Project, ProjectTrustLevelEnum.All);
                            MuxAssert.IsTrue(runtime.TryGetSkill("repo-tool", f.Project, out _), "available after trust");
                            ToolResult ran = await RunAsync(runtime, "repo-tool", "say", f.Project, ct).ConfigureAwait(false);
                            MuxAssert.IsTrue(ran.Success, "runs after trust: " + ran.Content);
                            MuxAssert.Contains("REPO_TOOL_MARKER", ran.Content, "output captured");
                        }

                        MuxAssert.AreEqual(ProjectTrustLevelEnum.All, new ProjectTrustStore(f.TrustPath).GetLevel(f.Project), "decision persisted");
                    }),

                    Case("PlaybooksOnlyAndIgnore", "Playbooks-only keeps commands blocked; ignore hides every project skill", async (ProjectSkillsFixture f, CancellationToken ct) =>
                    {
                        WritePlaybook(Path.Combine(f.Project, ".mux", "skills"), "guide", "Guide text.");
                        WriteNodeSkill(Path.Combine(f.Project, ".mux", "skills"), "tooly", "X");
                        using (SkillRuntime runtime = f.CreateRuntime())
                        {
                            await runtime.RefreshNowAsync(ct).ConfigureAwait(false);
                            runtime.SetProjectTrust(f.Project, ProjectTrustLevelEnum.PlaybooksOnly);
                            MuxAssert.IsTrue(runtime.TryGetSkill("guide", f.Project, out _), "playbook loads");
                            MuxAssert.IsFalse(runtime.TryGetSkill("tooly", f.Project, out _), "command skill still blocked");

                            runtime.SetProjectTrust(f.Project, ProjectTrustLevelEnum.Ignore);
                            MuxAssert.IsFalse(runtime.TryGetSkill("guide", f.Project, out _), "ignored project loads nothing");
                            MuxAssert.AreEqual(0, CountScope(runtime.GetStatus(f.Project), "project"), "no project rows");

                            runtime.SetProjectTrust(f.Project, ProjectTrustLevelEnum.Unknown);
                            MuxAssert.IsTrue(runtime.TryGetSkill("guide", f.Project, out _), "reset restores the default");
                        }
                    }),

                    Case("TrustAllProjectsDoesNotPersist", "The per-run trust flag loads command skills without writing a decision", async (ProjectSkillsFixture f, CancellationToken ct) =>
                    {
                        WriteNodeSkill(Path.Combine(f.Project, ".agents", "skills"), "flagged", "X");
                        using (SkillRuntime runtime = f.CreateRuntime())
                        {
                            runtime.TrustAllProjects = true;
                            await runtime.RefreshNowAsync(ct).ConfigureAwait(false);
                            MuxAssert.IsTrue(runtime.TryGetSkill("flagged", f.Project, out _), "loaded under the flag");
                        }

                        MuxAssert.IsFalse(File.Exists(f.TrustPath), "no trust file written");
                    }),

                    Case("ProjectShadowsUserSkill", "A project skill hides a user skill with the same id", async (ProjectSkillsFixture f, CancellationToken ct) =>
                    {
                        WritePlaybook(f.UserSkills, "shared-name", "USER_VERSION");
                        WritePlaybook(Path.Combine(f.Project, ".mux", "skills"), "shared-name", "PROJECT_VERSION");
                        using (SkillRuntime runtime = f.CreateRuntime())
                        {
                            await runtime.RefreshNowAsync(ct).ConfigureAwait(false);
                            MuxAssert.IsTrue(runtime.TryGetSkill("shared-name", f.Project, out Skill fromProject), "found in project");
                            MuxAssert.Contains("PROJECT_VERSION", fromProject.Body, "project wins");
                            MuxAssert.IsTrue(fromProject.ShadowsUserSkill, "shadowing flagged");
                            MuxAssert.AreEqual(1, CountName(runtime.GetStatus(f.Project), "shared-name"), "one row for the id");

                            MuxAssert.IsTrue(runtime.TryGetSkill("shared-name", null, out Skill fromUser), "found for user scope");
                            MuxAssert.Contains("USER_VERSION", fromUser.Body, "user version without a project");
                            MuxAssert.IsFalse(fromUser.ShadowsUserSkill, "user skill not mutated by the merge");
                        }
                    }),

                    Case("ProjectSkillsDisabled", "With project skills off, the project's skills are ignored", async (ProjectSkillsFixture f, CancellationToken ct) =>
                    {
                        WritePlaybook(Path.Combine(f.Project, ".mux", "skills"), "team-only", "x");
                        using (SkillRuntime runtime = f.CreateRuntime())
                        {
                            runtime.ProjectSkillsEnabled = false;
                            await runtime.RefreshNowAsync(ct).ConfigureAwait(false);
                            MuxAssert.IsFalse(runtime.TryGetSkill("team-only", f.Project, out _), "not discovered");
                        }
                    }),

                    Case("CustomRootsHonored", "Only the configured project roots are searched", async (ProjectSkillsFixture f, CancellationToken ct) =>
                    {
                        WritePlaybook(Path.Combine(f.Project, "tools", "skills"), "custom-root", "x");
                        WritePlaybook(Path.Combine(f.Project, ".mux", "skills"), "default-root", "x");
                        using (SkillRuntime runtime = f.CreateRuntime())
                        {
                            runtime.ProjectSkillRoots = new List<string> { "tools/skills", "../escape", "/abs" };
                            MuxAssert.AreEqual(1, runtime.ProjectSkillRoots.Count, "escaping and rooted entries dropped");
                            await runtime.RefreshNowAsync(ct).ConfigureAwait(false);
                            MuxAssert.IsTrue(runtime.TryGetSkill("custom-root", f.Project, out _), "custom root searched");
                            MuxAssert.IsFalse(runtime.TryGetSkill("default-root", f.Project, out _), "default root not searched");
                        }
                    }),

                    Case("UserLibraryNeverReadAsProject", "A project root equal to the user library stays user scope", async (ProjectSkillsFixture f, CancellationToken ct) =>
                    {
                        // Simulates running mux from the home directory: ~/.mux/skills is both the user library
                        // and "<project>/.mux/skills".
                        string home = Path.Combine(f.Root, "home");
                        string userSkills = Path.Combine(home, ".mux", "skills");
                        WriteNodeSkill(userSkills, "home-tool", "X");
                        using (SkillRuntime runtime = new SkillRuntime(userSkills, () => new List<SkillIndexEntry>(), () => { }, TimeSpan.FromSeconds(30))
                        {
                            ProjectSkillsEnabled = true,
                            TrustStore = new ProjectTrustStore(f.TrustPath)
                        })
                        {
                            await runtime.RefreshNowAsync(ct).ConfigureAwait(false);
                            MuxAssert.IsTrue(runtime.TryGetSkill("home-tool", home, out Skill skill), "still usable");
                            MuxAssert.AreEqual(SkillScopeEnum.User, skill.Scope, "user scope");
                        }
                    }),

                    Case("ClaudeFormatSkillLoads", "A Claude Code SKILL.md loads with its fields mapped and unknowns warned", async (ProjectSkillsFixture f, CancellationToken ct) =>
                    {
                        string dir = Path.Combine(f.Project, ".claude", "skills", "explain-code");
                        Directory.CreateDirectory(dir);
                        File.WriteAllText(Path.Combine(dir, "SKILL.md"),
                            "---\n"
                            + "name: explain-code\n"
                            + "description: >\n"
                            + "  Explains code with diagrams\n"
                            + "  and analogies.\n"
                            + "allowed-tools: Read, Grep\n"
                            + "argument-hint: [file]\n"
                            + "disable-model-invocation: true\n"
                            + "model: sonnet\n"
                            + "---\n"
                            + "Explain $ARGUMENTS step by step.\n");

                        using (SkillRuntime runtime = f.CreateRuntime())
                        {
                            await runtime.RefreshNowAsync(ct).ConfigureAwait(false);
                            MuxAssert.IsTrue(runtime.TryGetSkill("explain-code", f.Project, out Skill skill), "loads as a playbook");
                            MuxAssert.AreEqual("Explains code with diagrams and analogies.", skill.Manifest.Description, "folded block scalar");
                            MuxAssert.AreEqual(2, skill.Manifest.AllowedTools.Count, "comma list parsed");
                            MuxAssert.AreEqual("[file]", skill.Manifest.ArgumentHint, "argument hint mapped");
                            MuxAssert.IsFalse(skill.Manifest.ModelInvocable, "disable-model-invocation mapped");
                            MuxAssert.IsTrue(skill.Manifest.UserInvocable, "still user invocable");
                            MuxAssert.AreEqual(1, skill.Validation.Warnings.Count, "one unrecognized field");
                            MuxAssert.Contains("model", skill.Validation.Warnings[0], "model field warned");
                            MuxAssert.DoesNotContain("explain-code", runtime.BuildPromptSection(f.Project), "not advertised to the model");
                        }
                    }),

                    Case("TrustStoreRoundTripAndParse", "Trust decisions persist and levels parse from synonyms", (ProjectSkillsFixture f, CancellationToken ct) =>
                    {
                        ProjectTrustStore store = new ProjectTrustStore(f.TrustPath);
                        MuxAssert.AreEqual(ProjectTrustLevelEnum.Unknown, store.GetLevel(f.Project), "unknown by default");
                        store.SetLevel(f.Project, ProjectTrustLevelEnum.PlaybooksOnly);
                        MuxAssert.AreEqual(ProjectTrustLevelEnum.PlaybooksOnly, new ProjectTrustStore(f.TrustPath).GetLevel(f.Project + Path.DirectorySeparatorChar), "trailing separator ignored");
                        store.SetLevel(f.Project, ProjectTrustLevelEnum.Unknown);
                        MuxAssert.AreEqual(ProjectTrustLevelEnum.Unknown, store.GetLevel(f.Project), "reset removes");

                        File.WriteAllText(f.TrustPath, "{ not json");
                        MuxAssert.AreEqual(ProjectTrustLevelEnum.Unknown, store.GetLevel(f.Project), "malformed file reads as unknown");

                        MuxAssert.IsTrue(ProjectTrustStore.TryParseLevel("trust", out ProjectTrustLevelEnum all) && all == ProjectTrustLevelEnum.All, "trust");
                        MuxAssert.IsTrue(ProjectTrustStore.TryParseLevel("playbooks-only", out ProjectTrustLevelEnum pb) && pb == ProjectTrustLevelEnum.PlaybooksOnly, "playbooks-only");
                        MuxAssert.IsTrue(ProjectTrustStore.TryParseLevel("reset", out ProjectTrustLevelEnum reset) && reset == ProjectTrustLevelEnum.Unknown, "reset");
                        MuxAssert.IsFalse(ProjectTrustStore.TryParseLevel("maybe", out _), "unknown word rejected");
                        return Task.CompletedTask;
                    }),

                    Case("ReservedListNameAndListAction", "'list' is reserved and the skill tool lists every usable skill", async (ProjectSkillsFixture f, CancellationToken ct) =>
                    {
                        WritePlaybook(f.UserSkills, "list", "x");
                        WritePlaybook(f.UserSkills, "real-one", "x");
                        using (SkillRuntime runtime = f.CreateRuntime())
                        {
                            await runtime.RefreshNowAsync(ct).ConfigureAwait(false);
                            SkillStatus reserved = FindStatus(runtime.GetStatus(), "list");
                            MuxAssert.IsFalse(reserved.Valid, "reserved name invalid");

                            using (JsonDocument doc = JsonDocument.Parse("{\"name\":\"list\"}"))
                            {
                                ToolResult listed = await runtime.ExecuteAsync("skill", doc.RootElement, f.Project, ct).ConfigureAwait(false);
                                MuxAssert.IsTrue(listed.Success, "list succeeds");
                                MuxAssert.Contains("real-one", listed.Content, "usable skill listed");
                                MuxAssert.Contains("\"count\":1", listed.Content, "invalid skill excluded");
                            }
                        }
                    })
                });
        }

        #region Helpers

        private static TestCaseDescriptor Case(string id, string name, Func<ProjectSkillsFixture, CancellationToken, Task> body)
        {
            return new TestCaseDescriptor(SuiteId, id, name, async (CancellationToken ct) =>
            {
                ProjectSkillsFixture fixture = new ProjectSkillsFixture();
                try
                {
                    await body(fixture, ct).ConfigureAwait(false);
                }
                finally
                {
                    try { Directory.Delete(fixture.Root, true); } catch (Exception) { }
                }
            });
        }

        private static async Task<ToolResult> RunAsync(SkillRuntime runtime, string name, string command, string workingDirectory, CancellationToken ct)
        {
            using (JsonDocument doc = JsonDocument.Parse(JsonSerializer.Serialize(new { name, command })))
            {
                return await runtime.ExecuteAsync("run_skill", doc.RootElement, workingDirectory, ct).ConfigureAwait(false);
            }
        }

        private static SkillStatus FindStatus(List<SkillStatus> statuses, string name)
        {
            foreach (SkillStatus status in statuses)
            {
                if (string.Equals(status.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return status;
                }
            }

            MuxAssert.Fail("no status row for " + name);
            return new SkillStatus();
        }

        private static int CountScope(List<SkillStatus> statuses, string scope)
        {
            int count = 0;
            foreach (SkillStatus status in statuses)
            {
                if (status.Scope == scope) count++;
            }

            return count;
        }

        private static int CountName(List<SkillStatus> statuses, string name)
        {
            int count = 0;
            foreach (SkillStatus status in statuses)
            {
                if (string.Equals(status.Name, name, StringComparison.OrdinalIgnoreCase)) count++;
            }

            return count;
        }

        /// <summary>
        /// Writes a playbook skill (no commands) whose body is the given text.
        /// </summary>
        /// <param name="root">The skills directory.</param>
        /// <param name="id">The skill id.</param>
        /// <param name="body">The body text.</param>
        internal static void WritePlaybook(string root, string id, string body)
        {
            string dir = Path.Combine(root, id);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "SKILL.md"), "---\nname: " + id + "\ndescription: playbook " + id + "\n---\n" + body + "\n");
        }

        /// <summary>
        /// Writes a skill with one node command that prints a marker.
        /// </summary>
        /// <param name="root">The skills directory.</param>
        /// <param name="id">The skill id.</param>
        /// <param name="marker">The text the command prints.</param>
        internal static void WriteNodeSkill(string root, string id, string marker)
        {
            string dir = Path.Combine(root, id);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "SKILL.md"),
                "---\nname: " + id + "\ndescription: tool " + id + "\nmutating: false\ncommands:\n  - name: say\n    block: say\n    interpreter: node\n---\n"
                + "Runs a tool.\n\n```js id=say\nconsole.log('" + marker + "');\n```\n");
        }

        #endregion
    }
}
