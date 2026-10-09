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
    using Touchstone.Core;

    /// <summary>
    /// Touchstone suite for running bundled skill scripts: the <c>python</c> interpreter resolving <c>python3</c> (or
    /// <c>py</c> on Windows) when no <c>python</c> is on PATH, and dry runs of script-file commands that report the
    /// command instead of running it. Positive and negative cases.
    /// </summary>
    public static class SkillScriptRuntimeSuite
    {
        #region Private-Members

        private const string SuiteId = "SkillScriptRuntime";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Builds the suite descriptor.
        /// </summary>
        /// <returns>The descriptor.</returns>
        public static TestSuiteDescriptor Create()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();
            void Add(string id, string name, Func<string, CancellationToken, Task> body)
            {
                cases.Add(new TestCaseDescriptor(SuiteId, id, name, (CancellationToken ct) => WithTempAsync((string dir) => body(dir, ct))));
            }

            Add("PythonPrefersPython3OnUnix", "On macOS and Linux the python interpreter prefers python3, then python", (string dir, CancellationToken ct) =>
            {
                string only3 = Touch(dir, "a/python3");
                MuxAssert.AreEqual(only3, SkillInterpreterResolver.ResolvePython(Path.Combine(dir, "a"), false), "python3 alone is found");
                Touch(dir, "b/python");
                string both3 = Touch(dir, "b/python3");
                MuxAssert.AreEqual(both3, SkillInterpreterResolver.ResolvePython(Path.Combine(dir, "b"), false), "python3 wins over python");
                string plain = Touch(dir, "c/python");
                MuxAssert.AreEqual(plain, SkillInterpreterResolver.ResolvePython(Path.Combine(dir, "c"), false), "python alone is found");
                string first = Touch(dir, "d1/python");
                Touch(dir, "d2/python3");
                MuxAssert.AreEqual(Path.Combine(dir, "d2", "python3"), SkillInterpreterResolver.ResolvePython(Path.Combine(dir, "d1") + Path.PathSeparator + Path.Combine(dir, "d2"), false), "the preferred name wins across PATH entries");
                MuxAssert.IsTrue(File.Exists(first), "fixture present");
                return Task.CompletedTask;
            });
            Add("PythonOnWindowsUsesPythonThenPy", "On Windows the interpreter tries python.exe, then the py launcher", (string dir, CancellationToken ct) =>
            {
                string py = Touch(dir, "w/py.exe");
                MuxAssert.AreEqual(py, SkillInterpreterResolver.ResolvePython(Path.Combine(dir, "w"), true), "py launcher used when python is missing");
                string python = Touch(dir, "w/python.exe");
                MuxAssert.AreEqual(python, SkillInterpreterResolver.ResolvePython(Path.Combine(dir, "w"), true), "python.exe preferred");
                Touch(dir, "u/python3");
                MuxAssert.AreEqual("python", SkillInterpreterResolver.ResolvePython(Path.Combine(dir, "u"), true), "a unix name does not count on Windows");
                return Task.CompletedTask;
            });
            Add("PythonFallsBackToBareName", "With no Python on PATH the bare name is used so the failure says what is missing", (string dir, CancellationToken ct) =>
            {
                MuxAssert.AreEqual("python", SkillInterpreterResolver.ResolvePython(Path.Combine(dir, "empty"), false), "missing directory");
                MuxAssert.AreEqual("python", SkillInterpreterResolver.ResolvePython(string.Empty, false), "empty PATH");
                MuxAssert.AreEqual(SkillInterpreterResolver.ResolvePython(null), SkillInterpreterResolver.BuildStartInfo("python", "x.py", null).FileName, "the start info uses the resolved interpreter");
                return Task.CompletedTask;
            });
            Add("ScriptDryRunReportsCommand", "A dry run of a script command prints the command and runs nothing", async (string dir, CancellationToken ct) =>
            {
                Skill skill = MakeSkill(dir, "import sys, pathlib\npathlib.Path(sys.argv[1]).write_text('ran')\n");
                string marker = Path.Combine(dir, "marker.txt");
                Dictionary<string, string> env = new Dictionary<string, string> { [DefaultSkillHelpers.DryRunVariable] = "1" };
                ToolResult result = await new SkillExecutor().ExecuteAsync("t", skill, skill.Manifest.Commands[0], new List<string> { marker, "--flag" }, dir, ct, env).ConfigureAwait(false);
                MuxAssert.IsTrue(result.Success, "dry run succeeds: " + result.Content);
                MuxAssert.AreEqual("DRYRUN: python scripts/write.py " + marker + " --flag", Field(result, "stdout").Trim(), "command reported");
                MuxAssert.AreEqual("0", Field(result, "exit_code"), "exit 0");
                MuxAssert.IsFalse(File.Exists(marker), "the script did not run");

                SkillExecutor configured = new SkillExecutor();
                configured.DefaultEnvironment[DefaultSkillHelpers.DryRunVariable] = "1";
                ToolResult viaDefault = await configured.ExecuteAsync("t", skill, skill.Manifest.Commands[0], new List<string> { marker }, dir, ct).ConfigureAwait(false);
                MuxAssert.Contains("DRYRUN: python scripts/write.py", Field(viaDefault, "stdout"), "the executor's default environment also triggers a dry run");
                MuxAssert.IsFalse(File.Exists(marker), "still not run");
            });
            Add("ScriptRunsWhenNotDryRun", "Without a dry run the script really runs through the resolved Python", async (string dir, CancellationToken ct) =>
            {
                if (SkillInterpreterResolver.ResolvePython() == "python" && !OnPath("python"))
                {
                    return;
                }

                Skill skill = MakeSkill(dir, "import sys, pathlib\npathlib.Path(sys.argv[1]).write_text('ran')\nprint('wrote')\n");
                string marker = Path.Combine(dir, "marker.txt");
                Dictionary<string, string> env = new Dictionary<string, string> { [DefaultSkillHelpers.DryRunVariable] = "0" };
                ToolResult result = await new SkillExecutor().ExecuteAsync("t", skill, skill.Manifest.Commands[0], new List<string> { marker }, dir, ct, env).ConfigureAwait(false);
                MuxAssert.AreEqual("0", Field(result, "exit_code"), "exit 0: " + result.Content);
                MuxAssert.Contains("wrote", Field(result, "stdout"), "output captured");
                MuxAssert.AreEqual("ran", File.ReadAllText(marker), "the script ran");

                Skill strict = MakeSkill(Path.Combine(dir, "strict"), "import argparse\np = argparse.ArgumentParser()\np.add_argument('--n', type=int, required=True)\np.parse_args()\n");
                ToolResult bad = await new SkillExecutor().ExecuteAsync("t", strict, strict.Manifest.Commands[0], new List<string> { "--n", "abc" }, dir, ct, env).ConfigureAwait(false);
                MuxAssert.AreEqual("2", Field(bad, "exit_code"), "argparse usage errors surface as exit 2 (invalid input)");
            });
            Add("BlockCommandsIgnoreGenericDryRun", "Inline block commands still run in a dry run, because their own code handles it", async (string dir, CancellationToken ct) =>
            {
                if (!OnPath("pwsh"))
                {
                    return;
                }

                string folder = Path.Combine(dir, "block-skill");
                Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, "SKILL.md"), "---\nname: block-skill\ndescription: Prints whether a dry run is on.\ncommands:\n  - name: show\n    description: Show the dry-run flag.\n    block: show\n    interpreter: pwsh\n---\n\n```pwsh id=show\nWrite-Output ('flag=' + $env:MUX_SKILL_DRY_RUN)\n```\n");
                Skill skill = new SkillLoader(dir).Load(folder);
                MuxAssert.IsTrue(skill.IsValid, "valid: " + string.Join("; ", skill.Validation.Errors));
                Dictionary<string, string> env = new Dictionary<string, string> { [DefaultSkillHelpers.DryRunVariable] = "1" };
                ToolResult result = await new SkillExecutor().ExecuteAsync("t", skill, skill.Manifest.Commands[0], new List<string>(), dir, ct, env).ConfigureAwait(false);
                MuxAssert.Contains("flag=1", Field(result, "stdout"), "the block ran and saw the flag");
                MuxAssert.DoesNotContain("DRYRUN:", Field(result, "stdout"), "no generic dry-run line for blocks");
            });

            return new TestSuiteDescriptor(SuiteId, "Skill scripts: Python resolution and script dry runs", cases);
        }

        #endregion

        #region Private-Methods

        private static Skill MakeSkill(string dir, string script)
        {
            Directory.CreateDirectory(dir);
            string folder = Path.Combine(dir, "script-skill");
            Directory.CreateDirectory(Path.Combine(folder, "scripts"));
            File.WriteAllText(Path.Combine(folder, "scripts", "write.py"), script);
            File.WriteAllText(Path.Combine(folder, "SKILL.md"), "---\nname: script-skill\ndescription: Runs a bundled Python script.\ncommands:\n  - name: write\n    description: Write a marker file.\n    run: scripts/write.py\n    interpreter: python\n---\n\nRun `write <path>`.\n");
            Skill skill = new SkillLoader(dir).Load(folder);
            MuxAssert.IsTrue(skill.IsValid, "fixture skill valid: " + string.Join("; ", skill.Validation.Errors));
            return skill;
        }

        private static string Field(ToolResult result, string name)
        {
            using (JsonDocument document = JsonDocument.Parse(result.Content))
            {
                return document.RootElement.TryGetProperty(name, out JsonElement value) ? value.ToString() : string.Empty;
            }
        }

        private static string Touch(string root, string relative)
        {
            string path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "x");
            return path;
        }

        private static bool OnPath(string executable)
        {
            foreach (string directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator))
            {
                if (!string.IsNullOrWhiteSpace(directory) && (File.Exists(Path.Combine(directory, executable)) || File.Exists(Path.Combine(directory, executable + ".exe"))))
                {
                    return true;
                }
            }

            return false;
        }

        private static async Task WithTempAsync(Func<string, Task> body)
        {
            string root = Path.Combine(Path.GetTempPath(), "mux-skillscript-" + Guid.NewGuid().ToString("N"));
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

        #endregion
    }
}
