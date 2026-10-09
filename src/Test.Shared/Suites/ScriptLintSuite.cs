namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Skills;
    using Test.Shared.Support;
    using Touchstone.Core;

    /// <summary>
    /// Touchstone suite for the <c>shell-lint</c> skill: which files count as scripts, one path, changed files, real
    /// shellcheck and PSScriptAnalyzer runs where installed, and a run with no linter. Positive and negative cases.
    /// </summary>
    public static class ScriptLintSuite
    {
        #region Private-Members

        private const string SuiteId = "ScriptLint";

        private static readonly Lazy<bool> _Analyzer = new Lazy<bool>(HasScriptAnalyzer, LazyThreadSafetyMode.ExecutionAndPublication);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Builds the suite descriptor.
        /// </summary>
        /// <returns>The descriptor.</returns>
        public static TestSuiteDescriptor Create()
        {
            bool ready = IsOnPath("pwsh");
            bool shellcheck = ready && IsOnPath("shellcheck");
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();
            void Add(string id, string name, Func<SkillTestContext, Task> body, Func<bool>? skip = null, string reason = "")
            {
                bool skipped = !ready || (skip != null && skip());
                cases.Add(new TestCaseDescriptor(SuiteId, id, name, (CancellationToken ct) => RunWithContextAsync(body, ct), skip: skipped, skipReason: !ready ? "pwsh is not on PATH" : reason));
            }

            cases.Add(new TestCaseDescriptor(SuiteId, "SkillDefined", "shell-lint is a read-only hygiene skill gated on shell and PowerShell files", (CancellationToken ct) =>
            {
                DefaultSkillDef def = DefaultSkillLibrary.Definitions().Single(d => d.Id == "shell-lint");
                MuxAssert.IsFalse(def.Mutating, "read-only");
                MuxAssert.AreEqual("hygiene", DefaultSkillCategories.For("shell-lint"), "category");
                MuxAssert.IsTrue(def.AppliesTo.Contains("**/*.sh") && def.AppliesTo.Contains("**/*.ps1"), "gated on scripts");
                MuxAssert.AreEqual("check,changed", string.Join(",", def.Commands.Select(c => c.Name)), "commands");
                return Task.CompletedTask;
            }));

            Add("FindsScripts", "check finds .sh, .bash, shebang scripts, .ps1, and .psm1, and skips other files and dependency folders", async (SkillTestContext c) =>
            {
                Write(c, ".git/HEAD", "ref: refs/heads/main");
                Write(c, "a.sh", "echo a");
                Write(c, "scripts/b.bash", "echo b");
                Write(c, "tool", "#!/usr/bin/env bash\necho tool");
                Write(c, "plain", "#!/bin/sh\necho plain");
                Write(c, "notes.txt", "#!/bin/sh\nnot a script");
                Write(c, "pytool", "#!/usr/bin/env python3\nprint(1)");
                Write(c, "node_modules/dep/x.sh", "echo x");
                Write(c, "obj/gen.ps1", "Write-Output 1");
                Write(c, "run.ps1", "Write-Output run");
                Write(c, "lib/mod.psm1", "function F { }");
                SkillRunResult run = await c.Run(true, "shell-lint", "check").ConfigureAwait(false);
                run.Exit(0).Has("DRYRUN: shellcheck -f gcc").Has("a.sh").Has("scripts/b.bash").Has(" tool").Has(" plain")
                    .Has("DRYRUN: Invoke-ScriptAnalyzer").Has("run.ps1").Has("lib/mod.psm1")
                    .Lacks("notes.txt").Lacks("pytool").Lacks("node_modules").Lacks("obj/gen.ps1");
            });
            Add("OnePath", "check <folder> lints only that folder, check <file> one file; other files and missing paths are refused", async (SkillTestContext c) =>
            {
                Write(c, ".git/HEAD", "ref: refs/heads/main");
                Write(c, "a.sh", "echo a");
                Write(c, "scripts/b.bash", "echo b");
                Write(c, "run.ps1", "Write-Output run");
                Write(c, "notes.txt", "x");
                (await c.Run(true, "shell-lint", "check", "scripts").ConfigureAwait(false)).Exit(0).Has("scripts/b.bash").Lacks("a.sh").Lacks("Invoke-ScriptAnalyzer");
                (await c.Run(true, "shell-lint", "check", "run.ps1").ConfigureAwait(false)).Exit(0).Has("run.ps1").Lacks("shellcheck");
                (await c.RunIn("scripts", true, "shell-lint", "check", "b.bash").ConfigureAwait(false)).Exit(0).Has("DRYRUN: shellcheck -f gcc scripts/b.bash");
                (await c.Run(true, "shell-lint", "check", "notes.txt").ConfigureAwait(false)).Exit(2).Has("is not a shell or PowerShell script");
                (await c.Run(true, "shell-lint", "check", "missing").ConfigureAwait(false)).Exit(2).Has("not found: missing");
            });
            Add("NoScripts", "A project without scripts says so and exits 0", async (SkillTestContext c) =>
            {
                Write(c, "README.md", "# x");
                (await c.Run("shell-lint", "check").ConfigureAwait(false)).Exit(0).Has("No shell or PowerShell scripts to lint.");
            });
            Add("ChangedOnly", "changed lints only modified and untracked scripts, not committed unchanged ones", async (SkillTestContext c) =>
            {
                GitFixture repo = c.Repo();
                repo.Write("a.sh", "echo a").Write("b.sh", "echo b").Write("README.md", "x");
                repo.Commit("initial");
                Write(c, "b.sh", "echo changed");
                Write(c, "new.ps1", "Write-Output new");
                Write(c, "README.md", "changed");
                (await c.Run(true, "shell-lint", "changed", "HEAD").ConfigureAwait(false))
                    .Exit(0).Has("DRYRUN: shellcheck -f gcc b.sh").Has("new.ps1").Lacks("a.sh").Lacks("README");
            }, () => !GitFixture.IsAvailable(), "git is not on PATH");
            Add("ChangedOutsideGit", "changed outside a git repository is refused", async (SkillTestContext c) =>
            {
                Write(c, "a.sh", "echo a");
                (await c.Run(true, "shell-lint", "changed").ConfigureAwait(false)).Exit(2);
            });
            Add("ShellcheckFindings", "shellcheck reports an unquoted variable as file:line:col with its code, and a clean script passes", async (SkillTestContext c) =>
            {
                Write(c, "bad.sh", "#!/bin/sh\necho $1\n");
                (await c.Run("shell-lint", "check").ConfigureAwait(false)).Exit(1).Has("== shellcheck (1 file)").Has("bad.sh:2:").Has("SC2086").Has("finding");
                File.WriteAllText(Path.Combine(c.Project, "bad.sh"), "#!/bin/sh\necho \"$1\"\n");
                (await c.Run("shell-lint", "check").ConfigureAwait(false)).Exit(0).Has("No findings.");
            }, () => !shellcheck, "shellcheck is not installed");
            Add("ScriptAnalyzerFindings", "PSScriptAnalyzer reports Invoke-Expression with its rule name", async (SkillTestContext c) =>
            {
                Write(c, "bad.ps1", "$x = 'Get-Date'\nInvoke-Expression $x\n");
                (await c.Run("shell-lint", "check").ConfigureAwait(false)).Exit(1).Has("== PSScriptAnalyzer (1 file)").Has("bad.ps1:2:").Has("PSAvoidUsingInvokeExpression");
            }, () => !_Analyzer.Value, "PSScriptAnalyzer is not installed");
            Add("NoLinterInstalled", "With neither linter installed the run notes both and exits 2", async (SkillTestContext c) =>
            {
                Write(c, "a.sh", "echo a");
                Write(c, "b.ps1", "Write-Output b");
                (await c.Run("shell-lint", "check").ConfigureAwait(false))
                    .Exit(2).Has("1 shell script(s) not checked: shellcheck is not installed").Has("1 PowerShell script(s) not checked").Has("no linter could run");
            }, () => shellcheck || _Analyzer.Value, "a linter is installed");

            return new TestSuiteDescriptor(SuiteId, "Shell and PowerShell script linting", cases);
        }

        #endregion

        #region Private-Methods

        private static bool HasScriptAnalyzer()
        {
            if (!IsOnPath("pwsh")) return false;
            try
            {
                ProcessStartInfo info = new ProcessStartInfo("pwsh") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
                info.ArgumentList.Add("-NoProfile");
                info.ArgumentList.Add("-Command");
                info.ArgumentList.Add("if (Get-Module -ListAvailable -Name PSScriptAnalyzer) { 'yes' } else { 'no' }");
                using (Process process = Process.Start(info)!)
                {
                    string output = process.StandardOutput.ReadToEnd();
                    process.WaitForExit(30000);
                    return output.Contains("yes", StringComparison.Ordinal);
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static string Write(SkillTestContext c, string relative, string content)
        {
            string path = Path.Combine(c.Project, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
            return path;
        }

        private static bool IsOnPath(string executable)
        {
            foreach (string directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator))
            {
                if (string.IsNullOrWhiteSpace(directory)) continue;
                foreach (string candidate in new[] { executable, executable + ".exe", executable + ".cmd" })
                {
                    if (File.Exists(Path.Combine(directory, candidate))) return true;
                }
            }

            return false;
        }

        private static async Task RunWithContextAsync(Func<SkillTestContext, Task> body, CancellationToken ct)
        {
            string root = Path.Combine(Path.GetTempPath(), "mux-scriptlint-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                string skills = Path.Combine(root, "skills");
                DefaultSkillLibrary.SeedInto(skills);
                Directory.CreateDirectory(Path.Combine(root, "project"));
                await body(new SkillTestContext(root, skills, ct)).ConfigureAwait(false);
            }
            finally
            {
                try { Directory.Delete(root, true); } catch (Exception) { }
            }
        }

        #endregion
    }
}
