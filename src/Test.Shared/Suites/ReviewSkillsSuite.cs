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
    using Test.Shared.Support;
    using Touchstone.Core;

    /// <summary>
    /// Touchstone suite for the Phase 3 review and playbook skills, run for real against throwaway git repositories:
    /// code-review's diff modes, security-review's secret scan and dependency check, simplify's changed-file list,
    /// pr-comments formatting, test-gap pairing, init's survey, explain-codebase's map, and git-bisect finding a
    /// planted bad commit and always restoring HEAD. Positive and negative cases for each. Skipped when pwsh or git
    /// is not on PATH.
    /// </summary>
    public static class ReviewSkillsSuite
    {
        #region Private-Members

        private const string SuiteId = "ReviewSkills";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Builds the review-skills suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/> for the review and playbook cases.</returns>
        public static TestSuiteDescriptor Create()
        {
            bool ready = IsOnPath("pwsh") && GitFixture.IsAvailable();
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();
            void Add(string id, string name, Func<SkillTestContext, Task> body)
            {
                cases.Add(new TestCaseDescriptor(SuiteId, id, name, (CancellationToken ct) => RunWithContextAsync(body, ct), skip: !ready, skipReason: "pwsh or git is not on PATH"));
            }

            // --- code-review ---
            Add("UncommittedShowsHunksAndNewFiles", "code-review uncommitted shows modified hunks and untracked files", async (SkillTestContext c) =>
            {
                GitFixture repo = c.Repo();
                repo.Write("app.txt", "one\ntwo\n").Commit("base");
                repo.Write("app.txt", "one\nTWO_CHANGED\n").Write("fresh.txt", "brand new line\n");
                SkillRunResult r = await c.Run("code-review", "uncommitted").ConfigureAwait(false);
                r.Exit(0).Has("== Uncommitted changes").Has("+TWO_CHANGED").Has("-two").Has("=== new untracked file: fresh.txt").Has("+brand new line");
            });
            Add("UncommittedCleanSaysSo", "code-review uncommitted on a clean tree says there is nothing to review", async (SkillTestContext c) =>
            {
                c.Repo().Write("a.txt", "x").Commit("base");
                (await c.Run("code-review", "uncommitted").ConfigureAwait(false)).Exit(0).Has("No uncommitted changes.");
            });
            Add("UncommittedWithoutCommits", "code-review uncommitted works in a repository with no commits yet", async (SkillTestContext c) =>
            {
                GitFixture repo = c.Repo();
                repo.Write("staged.txt", "STAGED_CONTENT\n");
                repo.Run("add", "staged.txt");
                (await c.Run("code-review", "uncommitted").ConfigureAwait(false)).Exit(0).Has("+STAGED_CONTENT");
            });
            Add("NotARepository", "code-review outside a git repository exits 2", async (SkillTestContext c) =>
            {
                Directory.CreateDirectory(c.Project);
                (await c.Run("code-review", "uncommitted").ConfigureAwait(false)).Exit(2).Has("not a git repository");
            });
            Add("BranchShowsOnlyBranchChanges", "code-review branch shows the branch's commits and changes, not the base's", async (SkillTestContext c) =>
            {
                GitFixture repo = c.Repo();
                repo.Write("shared.txt", "base\n").Commit("base");
                repo.Run("checkout", "-q", "-b", "feature");
                repo.Write("feature.txt", "FEATURE_WORK\n").Commit("add feature work");
                repo.Run("checkout", "-q", "main");
                repo.Write("main-only.txt", "MAIN_ONLY\n").Commit("main moves on");
                repo.Run("checkout", "-q", "feature");
                (await c.Run("code-review", "branch", "main").ConfigureAwait(false)).Exit(0)
                    .Has("compared with main").Has("add feature work").Has("+FEATURE_WORK").Lacks("MAIN_ONLY");
            });
            Add("BranchDefaultsToMain", "code-review branch without a base uses the default branch and ignores effort words", async (SkillTestContext c) =>
            {
                GitFixture repo = c.Repo();
                repo.Write("a.txt", "a\n").Commit("base");
                repo.Run("checkout", "-q", "-b", "topic");
                repo.Write("b.txt", "TOPIC\n").Commit("topic change");
                (await c.Run("code-review", "branch", "deep").ConfigureAwait(false)).Exit(0).Has("compared with main").Has("+TOPIC");
            });
            Add("BranchWithoutDefaultBranch", "code-review branch exits 2 when no default branch exists and none is given", async (SkillTestContext c) =>
            {
                c.Repo("work").Write("a.txt", "a").Commit("base");
                (await c.Run("code-review", "branch").ConfigureAwait(false)).Exit(2).Has("no default branch");
            });
            Add("BranchUnknownBase", "code-review branch with an unknown base exits 2", async (SkillTestContext c) =>
            {
                c.Repo().Write("a.txt", "a").Commit("base");
                (await c.Run("code-review", "branch", "no-such-branch").ConfigureAwait(false)).Exit(2).Has("is not a branch, tag, or commit");
            });
            Add("BranchNoChanges", "code-review branch on the base itself reports no changes", async (SkillTestContext c) =>
            {
                c.Repo().Write("a.txt", "a").Commit("base");
                (await c.Run("code-review", "branch", "main").ConfigureAwait(false)).Exit(0).Has("No changes compared with main.");
            });
            Add("CommitShowsOneCommit", "code-review commit shows that commit's message and patch", async (SkillTestContext c) =>
            {
                GitFixture repo = c.Repo();
                repo.Write("a.txt", "a\n").Commit("first");
                string sha = repo.Write("a.txt", "a\nSECOND_LINE\n").Commit("second commit message");
                repo.Write("a.txt", "a\nSECOND_LINE\nTHIRD\n").Commit("third");
                (await c.Run("code-review", "commit", sha.Substring(0, 8)).ConfigureAwait(false)).Exit(0)
                    .Has("second commit message").Has("+SECOND_LINE").Lacks("+THIRD");
            });
            Add("CommitUnknownSha", "code-review commit with an unknown sha exits 2", async (SkillTestContext c) =>
            {
                c.Repo().Write("a.txt", "a").Commit("base");
                (await c.Run("code-review", "commit", "deadbeef").ConfigureAwait(false)).Exit(2).Has("is not a commit");
            });
            Add("CommitWithoutSha", "code-review commit without a sha exits 2", async (SkillTestContext c) =>
            {
                c.Repo().Write("a.txt", "a").Commit("base");
                (await c.Run("code-review", "commit").ConfigureAwait(false)).Exit(2).Has("pass a commit");
            });
            Add("PrDryRun", "code-review pr asks gh for the pull request and its diff", async (SkillTestContext c) =>
            {
                c.Repo().Write("a.txt", "a").Commit("base");
                (await c.Run(true, "code-review", "pr", "42").ConfigureAwait(false)).Exit(0).Has("DRYRUN: gh pr view 42").Has("DRYRUN: gh pr diff 42");
            });
            Add("PrWithoutNumber", "code-review pr without a number exits 2", async (SkillTestContext c) =>
            {
                c.Repo().Write("a.txt", "a").Commit("base");
                (await c.Run(true, "code-review", "pr").ConfigureAwait(false)).Exit(2).Has("pass a pull request number");
            });
            Add("FileShowsNumberedContentAndDiff", "code-review file shows numbered lines and the file's uncommitted diff", async (SkillTestContext c) =>
            {
                GitFixture repo = c.Repo();
                repo.Write("src/calc.js", "function add(a, b) {\n  return a + b;\n}\n").Commit("base");
                repo.Write("src/calc.js", "function add(a, b) {\n  return a - b;\n}\n");
                (await c.Run("code-review", "file", "src/calc.js").ConfigureAwait(false)).Exit(0)
                    .Has("src/calc.js (3 lines)").Has("+  return a - b;").Has("    2    return a - b;");
            });
            Add("FileMissing", "code-review file with a missing path exits 2", async (SkillTestContext c) =>
            {
                c.Repo().Write("a.txt", "a").Commit("base");
                (await c.Run("code-review", "file", "nope.cs").ConfigureAwait(false)).Exit(2).Has("does not exist");
            });
            Add("DiffIsCappedAtLimit", "Large diffs are cut at MUX_SKILL_DIFF_MAX_BYTES with a note", async (SkillTestContext c) =>
            {
                GitFixture repo = c.Repo();
                repo.Write("big.txt", "x\n").Commit("base");
                repo.Write("big.txt", new string('y', 5000) + "\n");
                (await c.Run(false, new Dictionary<string, string> { ["MUX_SKILL_DIFF_MAX_BYTES"] = "1500" }, "code-review", "uncommitted").ConfigureAwait(false))
                    .Exit(0).Has("[mux: output cut at 1500 characters");
            });

            // --- security-review ---
            Add("SecretInNewFileFlagged", "security-review flags a secret in a brand-new untracked file and masks it", async (SkillTestContext c) =>
            {
                GitFixture repo = c.Repo();
                repo.Write("readme.md", "x").Commit("base");
                repo.Write("config.js", "const awsKey = 'AKIAABCDEFGHIJKLMNOP';\n");
                (await c.Run("security-review", "uncommitted").ConfigureAwait(false)).Exit(0)
                    .Has("Possible secrets in added lines").Has("config.js:1").Has("AWS access key id  AKIA************");
            });
            Add("SecretInModifiedLineFlagged", "security-review flags a hard-coded password in a modified file with its line", async (SkillTestContext c) =>
            {
                GitFixture repo = c.Repo();
                repo.Write("settings.py", "DEBUG = False\nNAME = 'x'\n").Commit("base");
                repo.Write("settings.py", "DEBUG = False\nNAME = 'x'\npassword = \"hunter2hunter2\"\n");
                (await c.Run("security-review", "uncommitted").ConfigureAwait(false)).Exit(0).Has("settings.py:3").Has("hard-coded credential");
            });
            Add("RemovedSecretNotFlagged", "security-review ignores secrets on removed lines", async (SkillTestContext c) =>
            {
                GitFixture repo = c.Repo();
                repo.Write("old.js", "const key = 'AKIAABCDEFGHIJKLMNOP';\nconst ok = 1;\n").Commit("base");
                repo.Write("old.js", "const ok = 1;\n");
                (await c.Run("security-review", "uncommitted").ConfigureAwait(false)).Exit(0).Has("No likely secrets in added lines.");
            });
            Add("CleanChangeNoSecrets", "security-review reports no secrets and no manifests for an ordinary change", async (SkillTestContext c) =>
            {
                GitFixture repo = c.Repo();
                repo.Write("a.cs", "class A {}\n").Commit("base");
                repo.Write("a.cs", "class A { int x; }\n");
                (await c.Run("security-review", "uncommitted").ConfigureAwait(false)).Exit(0).Has("No likely secrets").Has("No dependency manifests changed.");
            });
            Add("ManifestChangeSuggestsAudit", "security-review lists changed dependency manifests and the audit to run", async (SkillTestContext c) =>
            {
                GitFixture repo = c.Repo();
                repo.Write("package.json", "{}").Write("requirements.txt", "flask\n").Commit("base");
                repo.Write("package.json", "{\"dependencies\":{\"lodash\":\"4\"}}").Write("requirements.txt", "flask\nrequests\n");
                (await c.Run("security-review", "uncommitted").ConfigureAwait(false)).Exit(0)
                    .Has("Dependency manifests changed").Has("js-deps audit").Has("py-deps audit");
            });
            Add("SecurityBranchMode", "security-review branch scans the branch's added lines", async (SkillTestContext c) =>
            {
                GitFixture repo = c.Repo();
                repo.Write("a.txt", "a").Commit("base");
                repo.Run("checkout", "-q", "-b", "feat");
                repo.Write("tokens.txt", "slack = xoxb-1234567890-abcdefghij\n").Commit("add token");
                (await c.Run("security-review", "branch", "main").ConfigureAwait(false)).Exit(0).Has("Slack token").Has("tokens.txt:1");
            });

            // --- simplify ---
            Add("ChangedFilesListed", "simplify lists modified and untracked files, but not deleted ones", async (SkillTestContext c) =>
            {
                GitFixture repo = c.Repo();
                repo.Write("keep.cs", "a").Write("gone.cs", "b").Commit("base");
                repo.Write("keep.cs", "a2");
                File.Delete(Path.Combine(repo.Directory, "gone.cs"));
                repo.Write("added.cs", "c");
                (await c.Run("simplify", "changed-files").ConfigureAwait(false)).Exit(0).Has("keep.cs").Has("added.cs  (new)").Lacks("gone.cs");
            });
            Add("NoChangedFiles", "simplify with nothing changed says so", async (SkillTestContext c) =>
            {
                c.Repo().Write("a.cs", "a").Commit("base");
                (await c.Run("simplify", "changed-files").ConfigureAwait(false)).Exit(0).Has("No changed files.");
            });

            // --- pr-comments ---
            Add("ThreadsFormattedUnresolvedFirst", "pr-comments formats saved review threads with unresolved threads first", async (SkillTestContext c) =>
            {
                c.Repo().Write("a.txt", "a").Commit("base");
                string json = "{\"data\":{\"repository\":{\"pullRequest\":{\"title\":\"Add cache\",\"reviewThreads\":{\"nodes\":[" +
                    "{\"isResolved\":true,\"path\":\"done.cs\",\"line\":3,\"comments\":{\"nodes\":[{\"author\":{\"login\":\"amy\"},\"body\":\"fixed\"}]}}," +
                    "{\"isResolved\":false,\"path\":\"src/cache.cs\",\"line\":42,\"comments\":{\"nodes\":[{\"author\":{\"login\":\"bob\"},\"body\":\"This can race.\\nUse a lock.\"}]}}]}}}}}";
                string file = Path.Combine(c.Root, "threads.json");
                File.WriteAllText(file, json);
                SkillRunResult r = (await c.Run("pr-comments", "list", "--from-file", file).ConfigureAwait(false)).Exit(0)
                    .Has("Pull request: Add cache").Has("1 unresolved, 1 resolved").Has("[UNRESOLVED] src/cache.cs:42").Has("bob: This can race. Use a lock.");
                MuxAssert.IsTrue(r.Stdout.IndexOf("[UNRESOLVED]", StringComparison.Ordinal) < r.Stdout.IndexOf("[RESOLVED]", StringComparison.Ordinal), "unresolved first");
            });
            Add("ThreadsEmpty", "pr-comments with no threads says so", async (SkillTestContext c) =>
            {
                c.Repo().Write("a.txt", "a").Commit("base");
                string file = Path.Combine(c.Root, "empty.json");
                File.WriteAllText(file, "{\"data\":{\"repository\":{\"pullRequest\":{\"reviewThreads\":{\"nodes\":[]}}}}}");
                (await c.Run("pr-comments", "list", "--from-file", file).ConfigureAwait(false)).Exit(0).Has("No review threads.");
            });
            Add("ThreadsBadJson", "pr-comments with invalid saved JSON exits 2", async (SkillTestContext c) =>
            {
                c.Repo().Write("a.txt", "a").Commit("base");
                string file = Path.Combine(c.Root, "bad.json");
                File.WriteAllText(file, "{ not json");
                (await c.Run("pr-comments", "list", "--from-file", file).ConfigureAwait(false)).Exit(2).Has("is not valid JSON");
            });
            Add("ThreadsMissingFile", "pr-comments with a missing saved file exits 2", async (SkillTestContext c) =>
            {
                c.Repo().Write("a.txt", "a").Commit("base");
                (await c.Run("pr-comments", "list", "--from-file", Path.Combine(c.Root, "nope.json")).ConfigureAwait(false)).Exit(2).Has("does not exist");
            });
            Add("ThreadsDryRunQueriesGitHub", "pr-comments asks gh for the repository and review threads", async (SkillTestContext c) =>
            {
                c.Repo().Write("a.txt", "a").Commit("base");
                (await c.Run(true, "pr-comments", "list", "7").ConfigureAwait(false)).Exit(0).Has("DRYRUN: gh repo view --json owner,name").Has("number=7").Lacks("gh pr view --json number");
            });

            // --- test-gap-review ---
            Add("GapReported", "test-gap-review reports a changed source file with no test change", async (SkillTestContext c) =>
            {
                GitFixture repo = c.Repo();
                repo.Write("src/Billing.cs", "class Billing {}").Commit("base");
                repo.Write("src/Billing.cs", "class Billing { int Total; }");
                (await c.Run("test-gap-review", "report").ConfigureAwait(false)).Exit(0).Has("without a matching test change (1)").Has("- src/Billing.cs");
            });
            Add("GapClosedByMatchingTests", "test-gap-review pairs sources and tests across language conventions", async (SkillTestContext c) =>
            {
                GitFixture repo = c.Repo();
                repo.Write("README.md", "x").Commit("base");
                repo.Write("src/Billing.cs", "a").Write("tests/BillingTests.cs", "t")
                    .Write("web/cart.ts", "a").Write("web/cart.test.ts", "t")
                    .Write("py/pricing.py", "a").Write("py/test_pricing.py", "t")
                    .Write("go/parse.go", "a").Write("go/parse_test.go", "t")
                    .Write("docs/guide.md", "not code");
                (await c.Run("test-gap-review", "report").ConfigureAwait(false)).Exit(0)
                    .Has("Every changed source file has a matching test change.").Has("tested   web/cart.ts").Lacks("guide.md");
            });
            Add("OnlyTestsChanged", "test-gap-review with only test changes reports no source files", async (SkillTestContext c) =>
            {
                GitFixture repo = c.Repo();
                repo.Write("README.md", "x").Commit("base");
                repo.Write("tests/FooTests.cs", "t");
                (await c.Run("test-gap-review", "report").ConfigureAwait(false)).Exit(0).Has("No changed source files (1 test files changed).");
            });

            // --- init and explain-codebase ---
            Add("InitSurveyReportsProject", "init survey reports the toolchain, entry points, instruction files, scripts, and README", async (SkillTestContext c) =>
            {
                GitFixture repo = c.Repo();
                repo.Write("package.json", "{\"scripts\":{\"test\":\"vitest\"},\"devDependencies\":{\"vitest\":\"2\"}}")
                    .Write("src/index.ts", "export {}").Write("AGENTS.md", "rule one\nrule two\n").Write("README.md", "# Shop\nA demo shop.\n").Commit("base");
                (await c.Run("init", "survey").ConfigureAwait(false)).Exit(0)
                    .Has("Ecosystem: JavaScript/TypeScript").Has("src/index.ts").Has("AGENTS.md (2 lines)").Has("test: vitest").Has("# Shop");
            });
            Add("InitSurveyOutsideGit", "init survey works outside a git repository", async (SkillTestContext c) =>
            {
                Directory.CreateDirectory(c.Project);
                File.WriteAllText(Path.Combine(c.Project, "main.py"), "print(1)");
                (await c.Run("init", "survey").ConfigureAwait(false)).Exit(0).Has("== Existing instruction files").Has("  none");
            });
            Add("ExplainMapRespectsDepth", "explain-codebase map counts files per folder down to the requested depth", async (SkillTestContext c) =>
            {
                GitFixture repo = c.Repo();
                repo.Write("src/api/users.cs", "a").Write("src/api/orders.cs", "a").Write("src/core/deep/x.cs", "a").Write("Program.cs", "a").Commit("base");
                (await c.Run("explain-codebase", "map", "1").ConfigureAwait(false)).Exit(0).Has("src/ (3)").Has("Program.cs").Lacks("api/");
                (await c.Run("explain-codebase", "map", "3").ConfigureAwait(false)).Exit(0).Has("api/ (2)").Has("deep/ (1)");
            });
            Add("ExplainMapInvalidDepth", "explain-codebase map falls back to depth 2 for an invalid depth", async (SkillTestContext c) =>
            {
                c.Repo().Write("src/api/users.cs", "a").Write("src/api/deep/x.cs", "a").Commit("base");
                (await c.Run("explain-codebase", "map", "banana").ConfigureAwait(false)).Exit(0).Has("Map (depth 2").Has("api/ (2)").Lacks("deep/");
            });

            // --- git-bisect ---
            Add("BisectFindsBadCommitAndResets", "git-bisect finds the planted bad commit and restores HEAD", async (SkillTestContext c) =>
            {
                GitFixture repo = c.Repo();
                string good = repo.Write("state.txt", "ok 1\n").Commit("commit 1");
                repo.Write("state.txt", "ok 2\n").Commit("commit 2");
                repo.Write("state.txt", "ok 3\n").Commit("commit 3");
                repo.Write("state.txt", "BROKEN 4\n").Commit("commit 4 introduces the bug");
                repo.Write("state.txt", "BROKEN 5\n").Commit("commit 5");
                repo.Write("state.txt", "BROKEN 6\n").Commit("commit 6");
                string head = repo.Run("rev-parse", "HEAD").Trim();
                (await c.Run("git-bisect", "start", good).ConfigureAwait(false)).Exit(0).Has("Bisect started.");
                SkillRunResult r = await c.Run("git-bisect", "run", "pwsh", "-NoProfile", "-Command", "if (Select-String -Path state.txt -Pattern BROKEN -Quiet) { exit 1 } else { exit 0 }").ConfigureAwait(false);
                r.Exit(0).Has("== First bad commit").Has("commit 4 introduces the bug").Has("Bisect reset; HEAD restored to main");
                MuxAssert.AreEqual(head, repo.Run("rev-parse", "HEAD").Trim(), "HEAD restored");
                MuxAssert.IsFalse(File.Exists(Path.Combine(repo.Directory, ".git", "BISECT_START")), "bisect state cleared");
            });
            Add("BisectRefusesDirtyTree", "git-bisect start refuses a dirty working tree", async (SkillTestContext c) =>
            {
                GitFixture repo = c.Repo();
                string good = repo.Write("a.txt", "1").Commit("one");
                repo.Write("a.txt", "2").Commit("two");
                repo.Write("a.txt", "dirty");
                (await c.Run("git-bisect", "start", good).ConfigureAwait(false)).Exit(2).Has("uncommitted changes");
            });
            Add("BisectRunWithoutStart", "git-bisect run without start exits 2", async (SkillTestContext c) =>
            {
                c.Repo().Write("a.txt", "1").Commit("one");
                (await c.Run("git-bisect", "run", "pwsh", "-Command", "exit 0").ConfigureAwait(false)).Exit(2).Has("no bisect in progress");
            });
            Add("BisectStartNeedsGood", "git-bisect start without a good commit exits 2", async (SkillTestContext c) =>
            {
                c.Repo().Write("a.txt", "1").Commit("one");
                (await c.Run("git-bisect", "start").ConfigureAwait(false)).Exit(2).Has("pass a known-good commit");
            });
            Add("BisectStartUnknownRef", "git-bisect start with an unknown commit exits 2", async (SkillTestContext c) =>
            {
                c.Repo().Write("a.txt", "1").Commit("one");
                (await c.Run("git-bisect", "start", "v9.9.9").ConfigureAwait(false)).Exit(2).Has("is not a commit");
            });
            Add("BisectResetClearsState", "git-bisect reset abandons a started bisect", async (SkillTestContext c) =>
            {
                GitFixture repo = c.Repo();
                string good = repo.Write("a.txt", "1").Commit("one");
                repo.Write("a.txt", "2").Commit("two");
                repo.Write("a.txt", "3").Commit("three");
                (await c.Run("git-bisect", "start", good).ConfigureAwait(false)).Exit(0);
                (await c.Run("git-bisect", "reset").ConfigureAwait(false)).Exit(0).Has("HEAD restored to main");
                MuxAssert.IsFalse(File.Exists(Path.Combine(repo.Directory, ".git", "BISECT_START")), "bisect state cleared");
                (await c.Run("git-bisect", "reset").ConfigureAwait(false)).Exit(0).Has("No bisect in progress.");
            });
            Add("BisectStartTwiceRefused", "git-bisect start while a bisect is running exits 2", async (SkillTestContext c) =>
            {
                GitFixture repo = c.Repo();
                string good = repo.Write("a.txt", "1").Commit("one");
                repo.Write("a.txt", "2").Commit("two");
                repo.Write("a.txt", "3").Commit("three");
                (await c.Run("git-bisect", "start", good).ConfigureAwait(false)).Exit(0);
                (await c.Run("git-bisect", "start", good).ConfigureAwait(false)).Exit(2).Has("already in progress");
                await c.Run("git-bisect", "reset").ConfigureAwait(false);
            });

            // --- catalog-level checks ---
            cases.Add(new TestCaseDescriptor(SuiteId, "PlaybookBodiesAreSubstantial", "Every default playbook skill (no commands) has a body of at least 400 characters", (CancellationToken ct) =>
            {
                int playbooks = 0;
                foreach (DefaultSkillDef definition in DefaultSkillLibrary.Definitions())
                {
                    if (definition.IsPlaybook)
                    {
                        playbooks++;
                        MuxAssert.IsTrue(definition.Body.Length >= 400, definition.Id + " body length " + definition.Body.Length);
                    }
                }

                MuxAssert.IsTrue(playbooks >= 1, "at least one playbook (debug)");
                return Task.CompletedTask;
            }));
            cases.Add(new TestCaseDescriptor(SuiteId, "ReviewBodiesDefineOutputFormat", "Review skill bodies define severity, location, and fix fields, and the no-findings phrase", (CancellationToken ct) =>
            {
                foreach (DefaultSkillDef definition in DefaultReviewSkills.All())
                {
                    MuxAssert.IsTrue(definition.Body.Contains("Procedure:", StringComparison.Ordinal), definition.Id + " has a procedure");
                }

                DefaultSkillDef review = Find("code-review");
                MuxAssert.Contains("[severity: high|medium|low]", review.Body, "severity field");
                MuxAssert.Contains("Failure:", review.Body, "failure field");
                MuxAssert.Contains("No findings.", review.Body, "no-findings phrase");
                MuxAssert.Contains("Attack:", Find("security-review").Body, "attack path field");
                return Task.CompletedTask;
            }));
            cases.Add(new TestCaseDescriptor(SuiteId, "EmptyReviewInvocationMeansUncommitted", "/code-review and /security-review with no arguments tell the model to review uncommitted changes", (CancellationToken ct) => WithTempAsync((string root) =>
            {
                DefaultSkillLibrary.SeedInto(root);
                foreach (string id in new[] { "code-review", "security-review" })
                {
                    Skill skill = new SkillLoader(root).Load(Path.Combine(root, id));
                    MuxAssert.IsTrue(skill.IsValid, id + " is valid");
                    SkillInvocation invocation = SkillInvocationExpander.Expand(skill, string.Empty);
                    MuxAssert.IsTrue(invocation.Prompt.Contains("with no input", StringComparison.OrdinalIgnoreCase), id + " states the empty-input default");
                    MuxAssert.Contains("`uncommitted`", invocation.Prompt, id + " names the uncommitted mode");
                }

                return Task.CompletedTask;
            })));
            cases.Add(new TestCaseDescriptor(SuiteId, "DebugInvocationSubstitutesSymptom", "/debug <symptom> puts the symptom into the playbook", (CancellationToken ct) => WithTempAsync((string root) =>
            {
                DefaultSkillLibrary.SeedInto(root);
                Skill debug = new SkillLoader(root).Load(Path.Combine(root, "debug"));
                MuxAssert.IsTrue(debug.IsValid && debug.Manifest.IsPlaybook, "debug is a valid playbook");
                SkillInvocation invocation = SkillInvocationExpander.Expand(debug, "login returns 500 after deploy");
                MuxAssert.Contains("Symptom reported with the request (may be empty): login returns 500 after deploy", invocation.Prompt, "symptom substituted");
                MuxAssert.DoesNotContain("$ARGUMENTS", invocation.Prompt, "placeholder replaced");
                return Task.CompletedTask;
            })));
            cases.Add(new TestCaseDescriptor(SuiteId, "ReviewSkillsGatedOnGitAndGh", "Review skills list only in git repositories, and pr-comments only when gh is installed", async (CancellationToken ct) =>
            {
                await WithTempAsync(async (string root) =>
                {
                    string skills = Path.Combine(root, "skills");
                    DefaultSkillLibrary.SeedInto(skills);
                    string plain = Path.Combine(root, "plain");
                    string repo = Path.Combine(root, "repo");
                    Directory.CreateDirectory(plain);
                    Directory.CreateDirectory(Path.Combine(repo, ".git"));
                    using (SkillRuntime runtime = new SkillRuntime(skills, () => new List<SkillIndexEntry>(), () => { }, TimeSpan.FromSeconds(30)))
                    {
                        runtime.Tools = new ToolPresenceCache(() => string.Empty);
                        await runtime.RefreshNowAsync(ct).ConfigureAwait(false);
                        string outside = runtime.BuildPromptSection(plain);
                        MuxAssert.DoesNotContain("- code-review:", outside, "hidden outside git");
                        MuxAssert.Contains("- debug:", outside, "ungated playbook listed");
                        string inside = runtime.BuildPromptSection(repo);
                        MuxAssert.Contains("- code-review:", inside, "listed in a repository");
                        MuxAssert.DoesNotContain("- pr-comments:", inside, "hidden without gh");
                    }
                }).ConfigureAwait(false);
            }));

            return new TestSuiteDescriptor(SuiteId, "Review and playbook skills against real git repositories", cases);
        }

        #endregion

        #region Private-Methods

        private static DefaultSkillDef Find(string id)
        {
            foreach (DefaultSkillDef definition in DefaultSkillLibrary.Definitions())
            {
                if (definition.Id == id)
                {
                    return definition;
                }
            }

            throw new InvalidOperationException("no default skill " + id);
        }

        private static async Task RunWithContextAsync(Func<SkillTestContext, Task> body, CancellationToken ct)
        {
            await WithTempAsync(async (string root) =>
            {
                string skills = Path.Combine(root, "skills");
                DefaultSkillLibrary.SeedInto(skills);
                await body(new SkillTestContext(root, skills, ct)).ConfigureAwait(false);
            }).ConfigureAwait(false);
        }

        private static bool IsOnPath(string executable)
        {
            string[] names = OperatingSystem.IsWindows() ? new[] { executable + ".exe", executable + ".cmd" } : new[] { executable };
            foreach (string directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator))
            {
                foreach (string name in names)
                {
                    if (!string.IsNullOrWhiteSpace(directory) && File.Exists(Path.Combine(directory, name)))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static async Task WithTempAsync(Func<string, Task> body)
        {
            string root = Path.Combine(Path.GetTempPath(), "mux-review-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                await body(root).ConfigureAwait(false);
            }
            finally
            {
                try
                {
                    foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                    {
                        File.SetAttributes(file, FileAttributes.Normal);
                    }

                    Directory.Delete(root, true);
                }
                catch (Exception)
                {
                }
            }
        }

        #endregion
    }
}
