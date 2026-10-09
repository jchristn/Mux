namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Models;
    using Mux.Core.Skills;
    using Test.Shared.Support;
    using Touchstone.Core;

    /// <summary>
    /// Touchstone suite for the Phase 4 loop skills, run for real through the skill executor: <c>loop-until</c> retrying
    /// a command until it succeeds, <c>fix-until-green</c> detecting and running the build and tests,
    /// <c>ci-watch</c> formatting workflow runs and failed-step logs from saved GitHub responses, and
    /// <c>flaky-test-hunt</c> measuring how often a command fails. Positive and negative cases for each. Skipped when
    /// pwsh is not on PATH.
    /// </summary>
    public static class LoopSkillsSuite
    {
        #region Private-Members

        private const string SuiteId = "LoopSkills";

        private const string CounterScript = @"$f = Join-Path $PSScriptRoot 'count.txt'
$n = 0
if (Test-Path $f) { $n = [int](Get-Content $f) }
$n++
Set-Content -Path $f -Value $n
Write-Output ('counter at ' + $n)
if ($n -ge 3) { exit 0 } else { exit 1 }
";

        private const string FlipScript = @"$f = Join-Path $PSScriptRoot 'flip.txt'
$n = 0
if (Test-Path $f) { $n = [int](Get-Content $f) }
$n++
Set-Content -Path $f -Value $n
if ($n % 2 -eq 1) { Write-Output ('FLIP_FAILURE on run ' + $n); exit 1 }
Write-Output 'ok'
";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Builds the loop-skills suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/> for the loop skill cases.</returns>
        public static TestSuiteDescriptor Create()
        {
            bool ready = IsOnPath("pwsh");
            bool gitReady = ready && GitFixture.IsAvailable();
            bool npmReady = ready && IsOnPath("npm") && IsOnPath("node");
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();
            void Add(string id, string name, Func<SkillTestContext, Task> body)
            {
                cases.Add(new TestCaseDescriptor(SuiteId, id, name, (CancellationToken ct) => RunWithContextAsync(body, ct), skip: !ready, skipReason: "pwsh is not on PATH"));
            }

            // --- definitions ---
            cases.Add(new TestCaseDescriptor(SuiteId, "LoopSkillsDefined", "The four loop skills exist with long timeouts, gating, and bounded bodies", (CancellationToken ct) =>
            {
                Dictionary<string, DefaultSkillDef> byId = new Dictionary<string, DefaultSkillDef>();
                foreach (DefaultSkillDef definition in DefaultLoopSkills.All()) byId[definition.Id] = definition;
                foreach (string id in new[] { "loop-until", "fix-until-green", "ci-watch", "flaky-test-hunt" })
                {
                    MuxAssert.IsTrue(byId.ContainsKey(id), id + " defined");
                    MuxAssert.IsTrue(byId[id].Body.Contains("Exit codes:", StringComparison.Ordinal), id + " documents exit codes");
                }

                MuxAssert.AreEqual(1800000, byId["loop-until"].Commands[0].TimeoutMs, "loop-until gets 30 minutes");
                MuxAssert.AreEqual(0, byId["ci-watch"].Commands[0].TimeoutMs, "ci-watch status keeps the default timeout");
                MuxAssert.Contains("gh", string.Join(",", byId["ci-watch"].RequiresTools), "ci-watch needs gh");
                MuxAssert.Contains(".github/workflows/*.yml", string.Join(",", byId["ci-watch"].AppliesTo), "ci-watch gated on workflows");
                MuxAssert.Contains("package.json", string.Join(",", byId["fix-until-green"].AppliesTo), "fix-until-green gated on projects");
                MuxAssert.AreEqual(0, byId["loop-until"].AppliesTo.Count, "loop-until applies everywhere");
                MuxAssert.Contains("Never weaken, delete, or skip a test", byId["fix-until-green"].Body, "no gaming tests");
                MuxAssert.Contains("$ARGUMENTS", byId["fix-until-green"].Body, "iteration budget from the request");
                MuxAssert.IsTrue(byId["loop-until"].Mutating && byId["fix-until-green"].Mutating, "mutating skills marked");
                MuxAssert.IsFalse(byId["ci-watch"].Mutating || byId["flaky-test-hunt"].Mutating, "read-only skills marked");
                MuxAssert.IsTrue(DefaultSkillLibrary.All().Count >= 152, "library includes the loop skills");
                return Task.CompletedTask;
            }));
            cases.Add(new TestCaseDescriptor(SuiteId, "TimeoutWrittenToSkillFile", "A command's timeout is written to SKILL.md and read back by the loader", (CancellationToken ct) => WithTempAsync((string root) =>
            {
                DefaultSkillLibrary.SeedInto(root);
                string text = File.ReadAllText(Path.Combine(root, "loop-until", "SKILL.md"));
                MuxAssert.Contains("timeoutMs: 1800000", text, "frontmatter carries the timeout");
                Skill skill = new SkillLoader(root).Load(Path.Combine(root, "loop-until"));
                MuxAssert.IsTrue(skill.IsValid, "valid");
                MuxAssert.AreEqual(1800000, skill.Manifest.Commands[0].TimeoutMs, "loader reads it");
                Skill status = new SkillLoader(root).Load(Path.Combine(root, "ci-watch"));
                MuxAssert.AreEqual(120000, status.Manifest.Commands[0].TimeoutMs, "default timeout elsewhere");
                MuxAssert.DoesNotContain("timeoutMs", File.ReadAllText(Path.Combine(root, "code-review", "SKILL.md")), "no timeout line without one");
                return Task.CompletedTask;
            })));

            // --- loop-until ---
            Add("LoopUntilSucceedsOnThirdAttempt", "loop-until retries until the command exits 0", async (SkillTestContext c) =>
            {
                string script = WriteScript(c, "counter.ps1", CounterScript);
                (await c.Run("loop-until", "run", "5", "0", "pwsh", "-NoProfile", "-File", script).ConfigureAwait(false))
                    .Exit(0).Has("attempt 1/5: exit 1").Has("attempt 3/5: exit 0").Has("Succeeded on attempt 3").Has("counter at 3").Lacks("attempt 4/5");
            });
            Add("LoopUntilGivesUp", "loop-until exits 1 with the last output when every attempt fails", async (SkillTestContext c) =>
            {
                string script = WriteScript(c, "fail.ps1", "Write-Output 'STILL_DOWN'\nexit 4\n");
                (await c.Run("loop-until", "run", "2", "1", "pwsh", "-NoProfile", "-File", script).ConfigureAwait(false))
                    .Exit(1).Has("attempt 2/2: exit 4").Has("Failed all 2 attempts").Has("STILL_DOWN");
            });
            Add("LoopUntilRejectsBadInput", "loop-until rejects missing arguments, out-of-range numbers, and unknown commands", async (SkillTestContext c) =>
            {
                Directory.CreateDirectory(c.Project);
                (await c.Run("loop-until", "run", "3", "1").ConfigureAwait(false)).Exit(2).Has("usage: run <maxAttempts");
                (await c.Run("loop-until", "run", "0", "1", "pwsh").ConfigureAwait(false)).Exit(2).Has("maxAttempts must be a whole number from 1 to 100");
                (await c.Run("loop-until", "run", "101", "1", "pwsh").ConfigureAwait(false)).Exit(2).Has("maxAttempts");
                (await c.Run("loop-until", "run", "3", "601", "pwsh").ConfigureAwait(false)).Exit(2).Has("intervalSeconds must be a whole number from 0 to 600");
                (await c.Run("loop-until", "run", "x", "1", "pwsh").ConfigureAwait(false)).Exit(2).Has("got 'x'");
                (await c.Run("loop-until", "run", "2", "0", "mux-no-such-tool-xyz").ConfigureAwait(false)).Exit(2).Has("'mux-no-such-tool-xyz' was not found on PATH");
            });
            Add("LoopUntilDryRun", "loop-until in a dry run prints the command without running it", async (SkillTestContext c) =>
            {
                Directory.CreateDirectory(c.Project);
                (await c.Run(true, "loop-until", "run", "4", "10", "curl", "-sf", "http://localhost:8080/health").ConfigureAwait(false))
                    .Exit(0).Has("up to 4 times, 10s apart").Has("DRYRUN: curl -sf http://localhost:8080/health").Lacks("attempt 1");
            });

            // --- fix-until-green ---
            Add("CheckDetectsDotnet", "fix-until-green check plans dotnet build then dotnet test", async (SkillTestContext c) =>
            {
                Write(c, "App.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>");
                (await c.Run(true, "fix-until-green", "check").ConfigureAwait(false))
                    .Exit(0).Has("Project: .NET").Has("DRYRUN: dotnet build --nologo").Has("DRYRUN: dotnet test --nologo").Has("RESULT: dry run");
            });
            Add("CheckDetectsNodeScripts", "fix-until-green check uses the package's build and test scripts", async (SkillTestContext c) =>
            {
                Write(c, "package.json", "{\"name\":\"x\",\"scripts\":{\"build\":\"tsc\",\"test\":\"vitest run\"}}");
                Write(c, "pnpm-lock.yaml", "lockfileVersion: 9");
                (await c.Run(true, "fix-until-green", "check").ConfigureAwait(false))
                    .Exit(0).Has("Project: JavaScript (pnpm)").Has("DRYRUN: pnpm run build").Has("DRYRUN: pnpm run test");
            });
            Add("CheckDetectsOtherToolchains", "fix-until-green check detects Python, Go, Rust, Maven, and Gradle", async (SkillTestContext c) =>
            {
                Write(c, "py/pyproject.toml", "[project]\nname='x'\n");
                Write(c, "go/go.mod", "module x\n");
                Write(c, "rs/Cargo.toml", "[package]\nname='x'\n");
                Write(c, "mvn/pom.xml", "<project/>");
                Write(c, "gradle/build.gradle", "plugins {}");
                (await c.RunIn("py", true, "fix-until-green", "check").ConfigureAwait(false)).Exit(0).Has("Project: Python").Has("build: no separate build step").Has("-m pytest -q");
                (await c.RunIn("go", true, "fix-until-green", "check").ConfigureAwait(false)).Exit(0).Has("DRYRUN: go build ./...").Has("DRYRUN: go test -count=1 ./...");
                (await c.RunIn("rs", true, "fix-until-green", "check").ConfigureAwait(false)).Exit(0).Has("DRYRUN: cargo build").Has("DRYRUN: cargo test");
                (await c.RunIn("mvn", true, "fix-until-green", "check").ConfigureAwait(false)).Exit(0).Has("Project: Java (Maven)").Has("DRYRUN: mvn -B compile").Has("DRYRUN: mvn -B test");
                (await c.RunIn("gradle", true, "fix-until-green", "check").ConfigureAwait(false)).Exit(0).Has("Project: Java (Gradle)").Has("assemble");
            });
            Add("CheckModesAndErrors", "fix-until-green check honors build and test modes and rejects bad input", async (SkillTestContext c) =>
            {
                Write(c, "go.mod", "module x\n");
                (await c.Run(true, "fix-until-green", "check", "build").ConfigureAwait(false)).Exit(0).Has("go build").Lacks("go test");
                (await c.Run(true, "fix-until-green", "check", "TEST").ConfigureAwait(false)).Exit(0).Has("go test").Lacks("go build");
                (await c.Run(true, "fix-until-green", "check", "lint").ConfigureAwait(false)).Exit(2).Has("check takes all, build, or test");
            });
            Add("CheckNeedsAProject", "fix-until-green check exits 2 outside a supported project, and CMake needs a build directory", async (SkillTestContext c) =>
            {
                Directory.CreateDirectory(c.Project);
                (await c.Run(true, "fix-until-green", "check").ConfigureAwait(false)).Exit(2).Has("no supported project found");
                Write(c, "CMakeLists.txt", "project(x)\n");
                (await c.Run(true, "fix-until-green", "check").ConfigureAwait(false)).Exit(2).Has("run cpp-configure first");
                Directory.CreateDirectory(Path.Combine(c.Project, "build"));
                (await c.Run(true, "fix-until-green", "check").ConfigureAwait(false)).Exit(0).Has("DRYRUN: cmake --build").Has("DRYRUN: ctest --test-dir");
            });
            cases.Add(new TestCaseDescriptor(SuiteId, "CheckRunsRealNodeScripts", "fix-until-green check really runs the scripts and reports red with the failing tail", (CancellationToken ct) => RunWithContextAsync(async (SkillTestContext c) =>
            {
                Write(c, "package.json", "{\"name\":\"x\",\"scripts\":{\"build\":\"node -e \\\"console.log('built')\\\"\",\"test\":\"node -e \\\"console.log('ASSERTION_BROKE');process.exit(3)\\\"\"}}");
                (await c.Run("fix-until-green", "check").ConfigureAwait(false))
                    .Exit(1).Has("== build: PASS").Has("== tests: FAIL with exit").Has("ASSERTION_BROKE").Has("RESULT: red (tests failed)");
                Write(c, "package.json", "{\"name\":\"x\",\"scripts\":{\"build\":\"node -e \\\"console.log('TYPE_ERROR');process.exit(2)\\\"\",\"test\":\"node -e \\\"0\\\"\"}}");
                (await c.Run("fix-until-green", "check").ConfigureAwait(false))
                    .Exit(1).Has("== build: FAIL").Has("TYPE_ERROR").Has("tests: skipped because the build failed").Has("RESULT: red (build failed)");
                Write(c, "package.json", "{\"name\":\"x\",\"scripts\":{\"build\":\"node -e \\\"0\\\"\",\"test\":\"node -e \\\"0\\\"\"}}");
                (await c.Run("fix-until-green", "check").ConfigureAwait(false)).Exit(0).Has("RESULT: green");
            }, ct), skip: !npmReady, skipReason: "pwsh, node, or npm is not on PATH"));

            // --- ci-watch ---
            Add("CiStatusFromFile", "ci-watch status lists runs newest first and exits 1 when the newest failed", async (SkillTestContext c) =>
            {
                string runs = Write(c, "runs.json", "[{\"databaseId\":101,\"workflowName\":\"CI\",\"displayTitle\":\"Fix parser\",\"status\":\"completed\",\"conclusion\":\"failure\",\"headBranch\":\"feature\"},{\"databaseId\":100,\"workflowName\":\"CI\",\"displayTitle\":\"Older\",\"status\":\"completed\",\"conclusion\":\"success\",\"headBranch\":\"feature\"}]");
                (await c.Run("ci-watch", "status", "feature", "--from-file", runs).ConfigureAwait(false))
                    .Exit(1).Has("Workflow runs on feature, newest first:").Has("101  failure").Has("[feature]  Fix parser").Has("use failed-logs 101");
                string passing = Write(c, "pass.json", "[{\"databaseId\":7,\"workflowName\":\"CI\",\"status\":\"completed\",\"conclusion\":\"success\",\"headBranch\":\"main\"}]");
                (await c.Run("ci-watch", "status", "--from-file", passing).ConfigureAwait(false)).Exit(0).Has("Latest run 7 passed.");
                string running = Write(c, "running.json", "[{\"databaseId\":8,\"workflowName\":\"CI\",\"status\":\"in_progress\",\"conclusion\":\"\",\"headBranch\":\"main\"}]");
                (await c.Run("ci-watch", "status", "--from-file", running).ConfigureAwait(false)).Exit(0).Has("8  in_progress").Has("use watch 8");
                string empty = Write(c, "empty.json", "[]");
                (await c.Run("ci-watch", "status", "--from-file", empty).ConfigureAwait(false)).Exit(0).Has("No workflow runs.");
            });
            Add("CiStatusErrors", "ci-watch status rejects missing files, invalid JSON, and a dangling option", async (SkillTestContext c) =>
            {
                Directory.CreateDirectory(c.Project);
                (await c.Run("ci-watch", "status", "--from-file", Path.Combine(c.Project, "nope.json")).ConfigureAwait(false)).Exit(2).Has("does not exist");
                string bad = Write(c, "bad.json", "{not json");
                (await c.Run("ci-watch", "status", "--from-file", bad).ConfigureAwait(false)).Exit(2).Has("not valid JSON");
                (await c.Run("ci-watch", "status", "--from-file").ConfigureAwait(false)).Exit(2).Has("--from-file needs a value");
            });
            cases.Add(new TestCaseDescriptor(SuiteId, "CiStatusDryRunUsesBranch", "ci-watch status in a dry run queries gh for the current branch", (CancellationToken ct) => RunWithContextAsync(async (SkillTestContext c) =>
            {
                c.Repo("trunk").Write("a.txt", "x").Commit("base");
                (await c.Run(true, "ci-watch", "status").ConfigureAwait(false))
                    .Exit(0).Has("DRYRUN: gh run list --limit 10 --json").Has("--branch trunk");
                (await c.Run(true, "ci-watch", "watch").ConfigureAwait(false)).Exit(0).Has("DRYRUN: gh run view <latest>");
                (await c.Run(true, "ci-watch", "failed-logs", "55").ConfigureAwait(false)).Exit(0).Has("DRYRUN: gh run view 55 --log-failed");
            }, ct), skip: !gitReady, skipReason: "pwsh or git is not on PATH"));
            Add("CiWatchReportsResult", "ci-watch watch prints the result, failed jobs, and the next step", async (SkillTestContext c) =>
            {
                string failed = Write(c, "failed.json", "{\"databaseId\":42,\"status\":\"completed\",\"conclusion\":\"failure\",\"workflowName\":\"CI\",\"jobs\":[{\"name\":\"build\",\"status\":\"completed\",\"conclusion\":\"success\"},{\"name\":\"test\",\"status\":\"completed\",\"conclusion\":\"failure\"}]}");
                (await c.Run("ci-watch", "watch", "42", "--from-file", failed).ConfigureAwait(false))
                    .Exit(1).Has("run 42 CI: completed (2 of 2 jobs done)").Has("failed job: test (failure)").Lacks("failed job: build").Has("RESULT: failure").Has("Next: failed-logs 42");
                string passed = Write(c, "passed.json", "{\"databaseId\":43,\"status\":\"completed\",\"conclusion\":\"success\",\"workflowName\":\"CI\",\"jobs\":[]}");
                (await c.Run("ci-watch", "watch", "43", "--from-file", passed).ConfigureAwait(false)).Exit(0).Has("RESULT: success");
                string running = Write(c, "running.json", "{\"databaseId\":44,\"status\":\"in_progress\",\"conclusion\":\"\",\"workflowName\":\"CI\",\"jobs\":[{\"name\":\"a\",\"status\":\"completed\",\"conclusion\":\"success\"},{\"name\":\"b\",\"status\":\"in_progress\"}]}");
                (await c.Run("ci-watch", "watch", "44", "--from-file", running, "--timeout", "0").ConfigureAwait(false))
                    .Exit(0).Has("in_progress (1 of 2 jobs done)").Has("RESULT: still running").Has("call watch 44 again");
            });
            Add("CiWatchRejectsBadInput", "ci-watch watch rejects a non-numeric run id and out-of-range options", async (SkillTestContext c) =>
            {
                Directory.CreateDirectory(c.Project);
                (await c.Run("ci-watch", "watch", "abc").ConfigureAwait(false)).Exit(2).Has("'abc' is not a workflow run id");
                (await c.Run("ci-watch", "watch", "1", "--timeout", "9999").ConfigureAwait(false)).Exit(2).Has("--timeout must be a whole number from 0 to 1500");
                (await c.Run("ci-watch", "watch", "1", "--interval", "1").ConfigureAwait(false)).Exit(2).Has("--interval must be a whole number from 5 to 120");
                (await c.Run("ci-watch", "failed-logs", "x1").ConfigureAwait(false)).Exit(2).Has("is not a workflow run id");
            });
            Add("CiFailedLogsGroupsSteps", "ci-watch failed-logs groups lines by job and step, strips timestamps, and trims each step", async (SkillTestContext c) =>
            {
                List<string> lines = new List<string>();
                for (int i = 1; i <= 12; i++) lines.Add("test\tRun tests\t2026-10-08T10:00:0" + (i % 10) + ".1234567Z line " + i);
                lines.Add("lint\tRun eslint\t2026-10-08T10:01:00.0000000Z error no-unused-vars");
                string log = Write(c, "log.txt", string.Join("\n", lines) + "\n");
                (await c.Run("ci-watch", "failed-logs", "9", "--from-file", log, "--lines", "5").ConfigureAwait(false))
                    .Exit(0).Has("Failed steps for run 9: 2").Has("== test / Run tests (12 lines)").Has("showing the last 5 of 12 lines").Has("line 12").Lacks("line 7\n").Has("== lint / Run eslint (1 lines)").Has("error no-unused-vars").Lacks("2026-10-08T");
                string empty = Write(c, "empty.txt", "\n");
                (await c.Run("ci-watch", "failed-logs", "--from-file", empty).ConfigureAwait(false)).Exit(0).Has("No failed-step logs.");
                (await c.Run("ci-watch", "failed-logs", "--from-file", log, "--lines", "1").ConfigureAwait(false)).Exit(2).Has("--lines must be a whole number from 5 to 400");
            });

            // --- flaky-test-hunt ---
            Add("FlakyDetectsIntermittentFailure", "flaky-test-hunt reports a flaky command with its first failing output", async (SkillTestContext c) =>
            {
                string script = WriteScript(c, "flip.ps1", FlipScript);
                (await c.Run("flaky-test-hunt", "run", "4", "--", "pwsh", "-NoProfile", "-File", script).ConfigureAwait(false))
                    .Exit(1).Has("run 1/4: FAIL with exit 1").Has("run 2/4: pass").Has("Passed 2 of 4, failed 2.").Has("VERDICT: flaky (50% of runs failed)").Has("first failing run (#1)").Has("FLIP_FAILURE on run 1");
            });
            Add("FlakyVerdicts", "flaky-test-hunt distinguishes no failures from a consistent failure", async (SkillTestContext c) =>
            {
                string pass = WriteScript(c, "pass.ps1", "exit 0\n");
                (await c.Run("flaky-test-hunt", "run", "3", "--", "pwsh", "-NoProfile", "-File", pass).ConfigureAwait(false)).Exit(0).Has("VERDICT: no failures in 3 runs.");
                string fail = WriteScript(c, "fail.ps1", "Write-Output 'BROKEN'\nexit 2\n");
                (await c.Run("flaky-test-hunt", "run", "2", "--", "pwsh", "-NoProfile", "-File", fail).ConfigureAwait(false))
                    .Exit(1).Has("failed every run; this is a consistent failure").Has("BROKEN");
            });
            Add("FlakyFilterGoesToTheRunner", "flaky-test-hunt passes the filter to the detected test runner", async (SkillTestContext c) =>
            {
                Write(c, "dn/App.Tests.csproj", "<Project/>");
                Write(c, "py/requirements.txt", "pytest\n");
                Write(c, "go/go.mod", "module x\n");
                Write(c, "js/package.json", "{\"name\":\"x\",\"devDependencies\":{\"vitest\":\"1\"}}");
                Write(c, "rs/Cargo.toml", "[package]\nname='x'\n");
                (await c.RunIn("dn", true, "flaky-test-hunt", "run", "10", "Parser").ConfigureAwait(false)).Exit(0).Has("Running dotnet test --nologo --filter Parser 10 times.").Has("DRYRUN: dotnet test --nologo --filter Parser");
                (await c.RunIn("py", true, "flaky-test-hunt", "run", "5", "test_login").ConfigureAwait(false)).Exit(0).Has("-m pytest -q -k test_login");
                (await c.RunIn("go", true, "flaky-test-hunt", "run", "5", "TestParse").ConfigureAwait(false)).Exit(0).Has("go test -count=1 -run TestParse ./...");
                (await c.RunIn("js", true, "flaky-test-hunt", "run", "5", "adds numbers").ConfigureAwait(false)).Exit(0).Has("vitest run -t \"adds numbers\"");
                (await c.RunIn("rs", true, "flaky-test-hunt", "run", "5", "parses").ConfigureAwait(false)).Exit(0).Has("DRYRUN: cargo test parses");
            });
            Add("FlakyRejectsBadInput", "flaky-test-hunt rejects bad counts, a missing filter or command, and no project", async (SkillTestContext c) =>
            {
                Directory.CreateDirectory(c.Project);
                (await c.Run("flaky-test-hunt", "run", "5").ConfigureAwait(false)).Exit(2).Has("usage: run <count 2-50>");
                (await c.Run("flaky-test-hunt", "run", "1", "x").ConfigureAwait(false)).Exit(2).Has("count must be a whole number from 2 to 50");
                (await c.Run("flaky-test-hunt", "run", "51", "x").ConfigureAwait(false)).Exit(2).Has("count must be");
                (await c.Run("flaky-test-hunt", "run", "3", "--").ConfigureAwait(false)).Exit(2).Has("pass the command after --");
                (await c.Run("flaky-test-hunt", "run", "3", "SomeTest").ConfigureAwait(false)).Exit(2).Has("no supported project found");
                (await c.Run("flaky-test-hunt", "run", "3", "--", "mux-no-such-tool-xyz").ConfigureAwait(false)).Exit(2).Has("was not found on PATH");
            });

            return new TestSuiteDescriptor(SuiteId, "Loop skills: loop-until, fix-until-green, ci-watch, flaky-test-hunt", cases);
        }

        #endregion

        #region Private-Methods

        private static string Write(SkillTestContext c, string relative, string content)
        {
            string path = Path.Combine(c.Project, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
            return path;
        }

        private static string WriteScript(SkillTestContext c, string name, string content)
        {
            Directory.CreateDirectory(c.Project);
            string path = Path.Combine(c.Root, "scripts-" + Guid.NewGuid().ToString("N").Substring(0, 8), name);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
            return path;
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

        private static Task WithTempAsync(Func<string, Task> body)
        {
            return WithTempCoreAsync(body);
        }

        private static async Task WithTempCoreAsync(Func<string, Task> body)
        {
            string root = Path.Combine(Path.GetTempPath(), "mux-loopskills-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                await body(root).ConfigureAwait(false);
            }
            finally
            {
                try { Directory.Delete(root, true); } catch (Exception) { }
            }
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

        #endregion
    }
}
