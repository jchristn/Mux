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
    /// Touchstone suite for the script commands added to the imported engineering-pack skills whose ids start with a
    /// through l. For every command it checks the frontmatter (name, interpreter, script path), that a dry run reports
    /// the command without running it or creating files, and that a real <c>--help</c> run (or, for the two Node.js
    /// scripts, the usage path) finishes quickly with exit 0, 1, or 2 and no Python traceback. Each skill is copied to
    /// a temporary folder first so running scripts never writes into the source tree. Negative cases cover an unknown
    /// command, a missing script file, and invalid arguments. Skipped without a source checkout, and per command when
    /// Python or Node.js is not installed.
    /// </summary>
    public static class ScriptCommandsSuite2
    {
        #region Private-Members

        private const string SuiteId = "ScriptCommands2";

        private static readonly Dictionary<string, string[]> _Expected = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["agent-harness"] = new[] { "goal-compiler", "harness-manifest-builder", "loop-controller" },
            ["agent-workflow-designer"] = new[] { "workflow-scaffolder" },
            ["ar-loop"] = new[] { "run-experiment" },
            ["ar-resume"] = new[] { "setup-experiment" },
            ["ar-run"] = new[] { "run-experiment", "setup-experiment" },
            ["ar-setup"] = new[] { "setup-experiment" },
            ["ar-status"] = new[] { "log-results" },
            ["autoresearch-agent"] = new[] { "log-results", "run-experiment", "setup-experiment" },
            ["aws-solution-architect"] = new[] { "architecture-designer", "cost-optimizer", "serverless-stack" },
            ["azure-cloud-architect"] = new[] { "architecture-designer", "bicep-generator", "cost-optimizer" },
            ["browser-automation"] = new[] { "anti-detection-checker", "form-automation-builder", "scraping-toolkit" },
            ["chaos-engineering"] = new[] { "blast-radius-calculator", "experiment-designer", "experiment-postmortem" },
            ["env-secrets-manager"] = new[] { "env-auditor" },
            ["epic-design"] = new[] { "inspect-assets", "validate-layers" },
            ["feature-flags-architect"] = new[] { "flag-debt-scanner", "kill-switch-audit", "rollout-planner" },
            ["full-page-screenshot"] = new[] { "full-page-screenshot" },
            ["gcp-cloud-architect"] = new[] { "architecture-designer", "cost-optimizer", "deployment-manager" },
            ["grill-me"] = new[] { "decision-tree-extractor", "grill-session-tracker", "question-generator" },
            ["grill-with-docs"] = new[] { "adr-scanner", "context-md-linter", "glossary-code-consistency" },
            ["hub-board"] = new[] { "board-manager" },
            ["hub-eval"] = new[] { "result-ranker", "session-manager" },
            ["hub-init"] = new[] { "hub-init" },
            ["hub-merge"] = new[] { "session-manager" },
            ["hub-run"] = new[] { "hub-init" },
            ["hub-spawn"] = new[] { "session-manager" },
            ["hub-status"] = new[] { "board-manager", "dag-analyzer", "session-manager" },
            ["incident-commander"] = new[] { "incident-classifier", "pir-generator", "timeline-reconstructor" },
            ["karpathy-coder"] = new[] { "assumption-linter", "complexity-checker", "diff-surgeon", "goal-verifier" },
            ["kubernetes-operator"] = new[] { "crd-validator", "operator-capability-audit", "reconcile-lint" }
        };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Builds the suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/> with one case per skill plus the negative cases.</returns>
        public static TestSuiteDescriptor Create()
        {
            string? source = ImportedSkillChecks.FindSourceRoot();
            string? packDir = source == null ? null : Path.Combine(source, "Mux.Core", "Skills", "Packs", "engineering");
            bool ready = packDir != null && Directory.Exists(packDir);
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            foreach (KeyValuePair<string, string[]> entry in _Expected)
            {
                string skillId = entry.Key;
                string[] commands = entry.Value;
                cases.Add(new TestCaseDescriptor(SuiteId, "Commands_" + skillId, skillId + ": commands are declared, dry-run cleanly, and answer --help", (CancellationToken ct) =>
                    CheckSkillAsync(Path.Combine(packDir!, skillId), skillId, commands, ct), skip: !ready, skipReason: "the source tree is not available"));
            }

            cases.Add(new TestCaseDescriptor(SuiteId, "EveryScriptSkillCovered", "Every engineering a-l skill with Python or Node.js scripts is listed here, and nothing else", (CancellationToken ct) =>
            {
                List<string> found = new List<string>();
                foreach (string dir in Directory.GetDirectories(packDir!))
                {
                    string id = Path.GetFileName(dir);
                    if (id.Length == 0 || id[0] < 'a' || id[0] > 'l') continue;
                    string scripts = Path.Combine(dir, "scripts");
                    if (Directory.Exists(scripts) && Directory.GetFiles(scripts).Length > 0) found.Add(id);
                }

                found.Sort(StringComparer.Ordinal);
                List<string> expected = new List<string>(_Expected.Keys);
                expected.Sort(StringComparer.Ordinal);
                MuxAssert.AreEqual(string.Join(",", expected), string.Join(",", found), "script-bearing skills in scope");
                return Task.CompletedTask;
            }, skip: !ready, skipReason: "the source tree is not available"));

            cases.Add(new TestCaseDescriptor(SuiteId, "UnknownCommandRefused", "run_skill refuses a command the skill does not declare", (CancellationToken ct) =>
                WithCopyAsync(Path.Combine(packDir!, "env-secrets-manager"), async (string root, string skillDir) =>
                {
                    SkillToolProvider provider = new SkillToolProvider(new SkillCatalog(new SkillLoader(root).Discover()), new SkillExecutor());
                    string json = JsonSerializer.Serialize(new { name = "env-secrets-manager", command = "no-such-command", working_directory = root });
                    using (JsonDocument doc = JsonDocument.Parse(json))
                    {
                        ToolResult result = await provider.ExecuteAsync("run_skill", doc.RootElement, root, ct).ConfigureAwait(false);
                        MuxAssert.IsFalse(result.Success, "unknown command fails");
                        MuxAssert.Contains("command_not_found", result.Content, "error code");
                    }
                }), skip: !ready, skipReason: "the source tree is not available"));

            cases.Add(new TestCaseDescriptor(SuiteId, "MissingScriptInvalid", "A command whose script file is missing makes the skill invalid and cannot run", (CancellationToken ct) =>
                WithCopyAsync(Path.Combine(packDir!, "kubernetes-operator"), async (string root, string skillDir) =>
                {
                    File.Delete(Path.Combine(skillDir, "scripts", "crd_validator.py"));
                    Skill skill = new SkillLoader(root).Load(skillDir);
                    MuxAssert.IsFalse(skill.IsValid, "skill is invalid without its script");
                    MuxAssert.Contains("scripts/crd_validator.py' was not found", string.Join("; ", skill.Validation.Errors), "names the missing script");
                    SkillCommand command = Find(skill, "crd-validator");
                    ToolResult result = await new SkillExecutor().ExecuteAsync("t", skill, command, new List<string> { "--help" }, root, ct, Hermetic()).ConfigureAwait(false);
                    MuxAssert.IsFalse(result.Success, "running a missing script fails");
                }), skip: !ready, skipReason: "the source tree is not available"));

            cases.Add(new TestCaseDescriptor(SuiteId, "InvalidArgumentsExitTwo", "Invalid arguments exit 2 (argparse usage errors and the scripts' own input checks)", (CancellationToken ct) =>
                WithCopyAsync(Path.Combine(packDir!, "aws-solution-architect"), async (string root, string skillDir) =>
                {
                    if (!PythonAvailable()) return;
                    Skill skill = new SkillLoader(root).Load(skillDir);
                    EngineeringScriptRun unknownFlag = await RunAsync(skill, "architecture-designer", new List<string> { "--no-such-flag" }, root, false, ct).ConfigureAwait(false);
                    MuxAssert.AreEqual(2, unknownFlag.ExitCode, "unknown flag exits 2: " + unknownFlag.Stderr);
                    EngineeringScriptRun missingRequired = await RunAsync(skill, "cost-optimizer", new List<string>(), root, false, ct).ConfigureAwait(false);
                    MuxAssert.AreEqual(2, missingRequired.ExitCode, "missing required arguments exit 2");
                    MuxAssert.Contains("--resources", missingRequired.Stderr, "names the missing argument");
                    EngineeringScriptRun missingFile = await RunAsync(skill, "architecture-designer", new List<string> { "--input", Path.Combine(root, "nope.json") }, root, false, ct).ConfigureAwait(false);
                    MuxAssert.AreEqual(2, missingFile.ExitCode, "an unreadable input file exits 2");
                    MuxAssert.DoesNotContain("Traceback", missingFile.Stderr, "no traceback");
                }), skip: !ready, skipReason: "the source tree is not available"));

            cases.Add(new TestCaseDescriptor(SuiteId, "AwsEntryPointsProduceOutput", "The aws-solution-architect entry points added in mux produce JSON and templates", (CancellationToken ct) =>
                WithCopyAsync(Path.Combine(packDir!, "aws-solution-architect"), async (string root, string skillDir) =>
                {
                    if (!PythonAvailable()) return;
                    Skill skill = new SkillLoader(root).Load(skillDir);
                    File.WriteAllText(Path.Combine(root, "req.json"), "{\"application_type\":\"web_application\",\"expected_users\":500}");
                    EngineeringScriptRun design = await RunAsync(skill, "architecture-designer", new List<string> { "--input", "req.json" }, root, false, ct).ConfigureAwait(false);
                    MuxAssert.AreEqual(0, design.ExitCode, "design succeeds: " + design.Stderr);
                    MuxAssert.Contains("pattern_name", design.Stdout, "design JSON");
                    File.WriteAllText(Path.Combine(root, "inventory.json"), "{}");
                    EngineeringScriptRun cost = await RunAsync(skill, "cost-optimizer", new List<string> { "--resources", "inventory.json", "--monthly-spend", "250" }, root, false, ct).ConfigureAwait(false);
                    MuxAssert.AreEqual(0, cost.ExitCode, "cost report succeeds: " + cost.Stderr);
                    MuxAssert.Contains("current_monthly_spend", cost.Stdout, "cost JSON");
                    EngineeringScriptRun stack = await RunAsync(skill, "serverless-stack", new List<string> { "--app-name", "demo", "--format", "terraform", "--output", "main.tf" }, root, false, ct).ConfigureAwait(false);
                    MuxAssert.AreEqual(0, stack.ExitCode, "template succeeds: " + stack.Stderr);
                    MuxAssert.Contains("required_providers", File.ReadAllText(Path.Combine(root, "main.tf")), "terraform written to --output");
                }), skip: !ready, skipReason: "the source tree is not available"));

            cases.Add(new TestCaseDescriptor(SuiteId, "EvaluatorsShippedForAutoresearch", "The autoresearch skills ship the built-in evaluators setup-experiment lists, normalized for mux", (CancellationToken ct) =>
                WithCopyAsync(Path.Combine(packDir!, "ar-setup"), async (string root, string skillDir) =>
                {
                    foreach (string id in new[] { "ar-resume", "ar-run", "ar-setup", "autoresearch-agent" })
                    {
                        string evaluators = Path.Combine(packDir!, id, "evaluators");
                        MuxAssert.AreEqual(8, Directory.GetFiles(evaluators, "*.py").Length, id + " ships eight evaluators");
                        MuxAssert.Contains("CLI_TOOL = \"mux\"", File.ReadAllText(Path.Combine(evaluators, "llm_judge_content.py")), id + " judges call mux by default");
                    }

                    if (!PythonAvailable()) return;
                    Skill skill = new SkillLoader(root).Load(skillDir);
                    EngineeringScriptRun listed = await RunAsync(skill, "setup-experiment", new List<string> { "--list-evaluators" }, root, false, ct).ConfigureAwait(false);
                    MuxAssert.AreEqual(0, listed.ExitCode, "listing succeeds: " + listed.Stderr);
                    MuxAssert.Contains("test_pass_rate", listed.Stdout, "lists the shipped evaluators");
                }), skip: !ready, skipReason: "the source tree is not available"));

            return new TestSuiteDescriptor(SuiteId, "Script commands: engineering pack skills a through l", cases);
        }

        #endregion

        #region Private-Methods

        private static Task CheckSkillAsync(string sourceSkillDir, string skillId, string[] expected, CancellationToken ct)
        {
            return WithCopyAsync(sourceSkillDir, async (string root, string skillDir) =>
            {
                Skill skill = new SkillLoader(root).Load(skillDir);
                MuxAssert.IsTrue(skill.IsValid, skillId + " valid: " + string.Join("; ", skill.Validation.Errors));
                MuxAssert.AreEqual(0, skill.Validation.Warnings.Count, skillId + " has no warnings: " + string.Join("; ", skill.Validation.Warnings));
                List<string> names = new List<string>();
                foreach (SkillCommand command in skill.Manifest.Commands) names.Add(command.Name);
                MuxAssert.AreEqual(string.Join(",", expected), string.Join(",", names), skillId + " commands");

                foreach (string name in expected)
                {
                    SkillCommand command = Find(skill, name);
                    string script = command.ScriptPath ?? string.Empty;
                    MuxAssert.IsTrue(script.StartsWith("scripts/", StringComparison.Ordinal), name + " runs a bundled script");
                    MuxAssert.IsTrue(File.Exists(Path.Combine(skillDir, script)), name + " script exists");
                    bool isNode = script.EndsWith(".js", StringComparison.Ordinal) || script.EndsWith(".mjs", StringComparison.Ordinal);
                    MuxAssert.AreEqual(isNode ? "node" : "python", command.Interpreter, name + " interpreter");

                    string work = Path.Combine(root, "work-" + name);
                    Directory.CreateDirectory(work);
                    EngineeringScriptRun dry = await RunAsync(skill, name, new List<string> { "--help" }, work, true, ct).ConfigureAwait(false);
                    MuxAssert.AreEqual(0, dry.ExitCode, name + " dry run exits 0");
                    MuxAssert.Contains("DRYRUN: " + command.Interpreter + " " + script + " --help", dry.Stdout, name + " dry run reports the command");
                    MuxAssert.AreEqual(0, Directory.GetFileSystemEntries(work).Length, name + " dry run creates nothing");

                    if (isNode)
                    {
                        if (!IsOnPath("node")) continue;
                        EngineeringScriptRun usage = await RunAsync(skill, name, new List<string>(), work, false, ct).ConfigureAwait(false);
                        MuxAssert.AreEqual(1, usage.ExitCode, name + " without arguments prints usage and exits 1");
                        MuxAssert.Contains("Usage", usage.Stdout + usage.Stderr, name + " usage text");
                        continue;
                    }

                    if (!PythonAvailable()) continue;
                    EngineeringScriptRun help = await RunAsync(skill, name, new List<string> { "--help" }, work, false, ct).ConfigureAwait(false);
                    MuxAssert.IsTrue(help.ExitCode >= 0 && help.ExitCode <= 2, name + " --help exits 0, 1, or 2 (got " + help.ExitCode + "): " + help.Stderr);
                    MuxAssert.DoesNotContain("Traceback", help.Stderr, name + " --help has no traceback");
                    MuxAssert.IsTrue(help.Stdout.IndexOf("usage", StringComparison.OrdinalIgnoreCase) >= 0, name + " --help prints usage");
                }
            });
        }

        private static async Task<EngineeringScriptRun> RunAsync(Skill skill, string commandName, List<string> arguments, string workingDirectory, bool dryRun, CancellationToken ct)
        {
            SkillCommand command = Find(skill, commandName);
            Dictionary<string, string> env = Hermetic();
            if (dryRun) env[DefaultSkillHelpers.DryRunVariable] = "1";
            using (CancellationTokenSource limit = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                limit.CancelAfter(TimeSpan.FromSeconds(30));
                ToolResult result = await new SkillExecutor().ExecuteAsync("t", skill, command, arguments, workingDirectory, limit.Token, env).ConfigureAwait(false);
                using (JsonDocument doc = JsonDocument.Parse(result.Content))
                {
                    JsonElement root = doc.RootElement;
                    EngineeringScriptRun run = new EngineeringScriptRun
                    {
                        Stdout = root.TryGetProperty("stdout", out JsonElement o) ? o.GetString() ?? string.Empty : string.Empty,
                        Stderr = root.TryGetProperty("stderr", out JsonElement e) ? e.GetString() ?? string.Empty : (root.TryGetProperty("message", out JsonElement m) ? m.GetString() ?? string.Empty : string.Empty),
                        ExitCode = root.TryGetProperty("exit_code", out JsonElement x) && x.ValueKind == JsonValueKind.Number ? x.GetInt32() : -1
                    };
                    return run;
                }
            }
        }

        private static Dictionary<string, string> Hermetic()
        {
            return new Dictionary<string, string>
            {
                ["PYTHONDONTWRITEBYTECODE"] = "1",
                ["NO_COLOR"] = "1"
            };
        }

        private static SkillCommand Find(Skill skill, string name)
        {
            foreach (SkillCommand command in skill.Manifest.Commands)
            {
                if (command.Name == name) return command;
            }

            throw new InvalidOperationException(skill.Manifest.Name + " has no command " + name);
        }

        private static bool PythonAvailable()
        {
            string python = SkillInterpreterResolver.ResolvePython();
            return Path.IsPathRooted(python) ? File.Exists(python) : IsOnPath(python);
        }

        private static async Task WithCopyAsync(string sourceSkillDir, Func<string, string, Task> body)
        {
            string root = Path.Combine(Path.GetTempPath(), "mux-scriptcmd2-" + Guid.NewGuid().ToString("N"));
            string skillDir = Path.Combine(root, Path.GetFileName(sourceSkillDir));
            CopyDirectory(sourceSkillDir, skillDir);
            try
            {
                await body(root, skillDir).ConfigureAwait(false);
            }
            finally
            {
                try { Directory.Delete(root, true); } catch (Exception) { }
            }
        }

        private static void CopyDirectory(string from, string to)
        {
            Directory.CreateDirectory(to);
            foreach (string file in Directory.GetFiles(from))
            {
                if (file.EndsWith(".pyc", StringComparison.Ordinal)) continue;
                File.Copy(file, Path.Combine(to, Path.GetFileName(file)));
            }

            foreach (string dir in Directory.GetDirectories(from))
            {
                if (Path.GetFileName(dir) == "__pycache__") continue;
                CopyDirectory(dir, Path.Combine(to, Path.GetFileName(dir)));
            }
        }

        private static bool IsOnPath(string executable)
        {
            string[] names = OperatingSystem.IsWindows() ? new[] { executable + ".exe", executable + ".cmd" } : new[] { executable };
            foreach (string directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator))
            {
                foreach (string name in names)
                {
                    if (!string.IsNullOrWhiteSpace(directory) && File.Exists(Path.Combine(directory, name))) return true;
                }
            }

            return false;
        }

        #endregion
    }
}
