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
    /// Touchstone suite for the script commands wrapped around the bundled Python scripts of the imported skills in
    /// <c>Skills/Bundled</c> and the security, data, research, business, docs, compliance, and productivity packs. For
    /// every command: the manifest names the right interpreter and script, the script exists, a dry run reports the
    /// command without running it, and a real <c>--help</c> run finishes cleanly (exit 0, 1, or 2 and no Python
    /// traceback). Negative cases cover an unknown command, a missing script, and invalid arguments. Real runs are
    /// skipped when no Python interpreter is on PATH.
    /// </summary>
    public static class ScriptCommandsSuite1
    {
        #region Private-Members

        private const string SuiteId = "ScriptCommands1";

        private static readonly Dictionary<string, string[]> _Expected = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["Bundled/a11y-audit"] = new[] { "a11y_scanner.py", "contrast_checker.py" },
            ["Bundled/api-design-reviewer"] = new[] { "api_linter.py", "api_scorecard.py", "breaking_change_detector.py" },
            ["Bundled/ci-cd-pipeline-builder"] = new[] { "stack_detector.py", "pipeline_generator.py" },
            ["Bundled/handoff"] = new[] { "setup.py", "config_loader.py", "handoff_template_generator.py", "handoff_self_check.py", "redaction_linter.py", "skill_recommender.py", "cleanup.py" },
            ["Bundled/performance-profiler"] = new[] { "performance_profiler.py" },
            ["Bundled/ship-gate"] = new[] { "ship_gate_scanner.py" },
            ["Bundled/skill-security-auditor"] = new[] { "skill_security_auditor.py" },
            ["Bundled/tdd-guide"] = new[] { "tdd_cli.py" },
            ["Packs/security/ai-security"] = new[] { "ai_threat_scanner.py" },
            ["Packs/security/cloud-security"] = new[] { "cloud_posture_check.py" },
            ["Packs/security/incident-response"] = new[] { "incident_triage.py" },
            ["Packs/security/red-team"] = new[] { "engagement_planner.py" },
            ["Packs/security/security-pen-testing"] = new[] { "vulnerability_scanner.py", "dependency_auditor.py", "pentest_report_generator.py" },
            ["Packs/security/threat-detection"] = new[] { "threat_signal_analyzer.py" },
            ["Packs/data/data-quality-auditor"] = new[] { "data_profiler.py", "missing_value_analyzer.py", "outlier_detector.py" },
            ["Packs/data/senior-data-scientist"] = new[] { "experiment_designer.py", "feature_engineering_pipeline.py", "model_evaluation_suite.py" },
            ["Packs/data/senior-ml-engineer"] = new[] { "model_deployment_pipeline.py", "ml_monitoring_suite.py", "rag_system_builder.py" },
            ["Packs/data/senior-prompt-engineer"] = new[] { "prompt_optimizer.py", "rag_evaluator.py", "agent_orchestrator.py" },
            ["Packs/data/statistical-analyst"] = new[] { "sample_size_calculator.py", "confidence_interval.py", "hypothesis_tester.py" },
            ["Packs/data/universal-scraping-architect"] = new[] { "validate_extraction.py", "local_bs4_example.py", "firecrawl_example.py" },
            ["Packs/research/dossier"] = new[] { "source_tier_classifier.py", "citation_tracker.py", "disconfirming_evidence_balance.py" },
            ["Packs/research/litreview"] = new[] { "framework_recommender.py", "free_search.py", "cross_search_aggregator.py", "citation_tracker.py" },
            ["Packs/research/pulse"] = new[] { "topic_slug_generator.py", "time_window_calculator.py", "citation_tracker.py" },
            ["Packs/business/cto-advisor"] = new[] { "tech_debt_analyzer.py", "team_scaling_calculator.py" },
            ["Packs/business/knowledge-ops"] = new[] { "kb_ingester.py", "runbook_validator.py", "sop_generator.py" },
            ["Packs/business/rfp-responder"] = new[] { "rfp_parser.py", "response_drafter.py", "winrate_predictor.py" },
            ["Packs/docs/md-document"] = new[] { "markdown_parser.py", "html_renderer.py", "interactivity_injector.py" },
            ["Packs/docs/md-review"] = new[] { "diff_parser.py", "annotation_extractor.py", "review_html_renderer.py" },
            ["Packs/compliance/agent-decision-receipts"] = new[] { "build_action_manifest.py" },
            ["Packs/compliance/gdpr-dsgvo-expert"] = new[] { "gdpr_compliance_checker.py", "dpia_generator.py", "data_subject_rights_tracker.py" },
            ["Packs/productivity/reflect"] = new[] { "bias_pattern_detector.py", "conversation_depth_analyzer.py", "directional_recommendation_validator.py" }
        };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Builds the script commands suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/> for the wrapped script commands.</returns>
        public static TestSuiteDescriptor Create()
        {
            string? skillsRoot = FindSkillsRoot();
            bool python = HasPython();
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            foreach (KeyValuePair<string, string[]> entry in _Expected)
            {
                string relative = entry.Key;
                string[] scripts = entry.Value;
                string id = "Commands_" + relative.Replace('/', '_').Replace('-', '_');
                cases.Add(new TestCaseDescriptor(SuiteId, id + "_Manifest", relative + ": every bundled script is a valid python command, and a dry run reports it without running it", async (CancellationToken ct) =>
                {
                    Skill skill = LoadSkill(skillsRoot!, relative);
                    foreach (string script in scripts)
                    {
                        SkillCommand command = FindCommand(skill, CommandName(script));
                        MuxAssert.AreEqual("python", command.Interpreter, script + " interpreter");
                        MuxAssert.AreEqual("scripts/" + script, (command.ScriptPath ?? string.Empty).Replace('\\', '/'), script + " script path");
                        MuxAssert.IsTrue(File.Exists(Path.Combine(skill.DirectoryPath, "scripts", script)), script + " exists");
                        string work = NewTempDirectory();
                        try
                        {
                            ScriptRun1 dry = await RunAsync(skill, command, new[] { "--help" }, work, true, ct).ConfigureAwait(false);
                            MuxAssert.AreEqual(0, dry.ExitCode, script + " dry run exit");
                            MuxAssert.Contains("DRYRUN: python scripts/" + script + " --help", dry.StdOut, script + " dry run line");
                            MuxAssert.AreEqual(0, Directory.GetFileSystemEntries(work).Length, script + " dry run creates nothing");
                        }
                        finally
                        {
                            TryDelete(work);
                        }
                    }
                }, skip: skillsRoot == null, skipReason: "the skill source tree was not found"));

                cases.Add(new TestCaseDescriptor(SuiteId, id + "_Help", relative + ": every command answers --help cleanly in an empty directory", async (CancellationToken ct) =>
                {
                    Skill skill = LoadSkill(skillsRoot!, relative);
                    foreach (string script in scripts)
                    {
                        SkillCommand command = FindCommand(skill, CommandName(script));
                        string work = NewTempDirectory();
                        try
                        {
                            ScriptRun1 run = await RunAsync(skill, command, new[] { "--help" }, work, false, ct).ConfigureAwait(false);
                            MuxAssert.IsTrue(run.ExitCode >= 0 && run.ExitCode <= 2, script + " exit 0-2, got " + run.ExitCode + ": " + run.StdErr);
                            MuxAssert.DoesNotContain("Traceback", run.StdErr, script + " no traceback");
                            MuxAssert.IsTrue(run.StdOut.Trim().Length > 0, script + " printed help");
                        }
                        finally
                        {
                            TryDelete(work);
                        }
                    }
                }, skip: skillsRoot == null || !python, skipReason: "python or the skill source tree is not available"));
            }

            cases.Add(new TestCaseDescriptor(SuiteId, "SkillsValidateWithoutWarnings", "Every wrapped skill loads valid with no warnings", (CancellationToken ct) =>
            {
                foreach (string relative in _Expected.Keys)
                {
                    Skill skill = LoadSkill(skillsRoot!, relative);
                    MuxAssert.AreEqual(0, skill.Validation.Warnings.Count, relative + " warnings: " + string.Join("; ", skill.Validation.Warnings));
                    MuxAssert.Contains("## Commands", File.ReadAllText(Path.Combine(skill.DirectoryPath, "SKILL.md")), relative + " documents its commands");
                    MuxAssert.Contains("run_skill", File.ReadAllText(Path.Combine(skill.DirectoryPath, "SKILL.md")), relative + " names run_skill");
                }

                return Task.CompletedTask;
            }, skip: skillsRoot == null, skipReason: "the skill source tree was not found"));

            cases.Add(new TestCaseDescriptor(SuiteId, "UnknownCommandIsRefused", "run_skill refuses a command the skill does not declare", async (CancellationToken ct) =>
            {
                string skills = NewTempDirectory();
                try
                {
                    CopyDirectory(Path.Combine(skillsRoot!, "Packs", "data", "statistical-analyst"), Path.Combine(skills, "statistical-analyst"));
                    SkillCatalog catalog = new SkillCatalog(new SkillLoader(skills).Discover());
                    SkillToolProvider provider = new SkillToolProvider(catalog, new SkillExecutor());
                    using (JsonDocument args = JsonDocument.Parse("{\"name\":\"statistical-analyst\",\"command\":\"no-such-command\"}"))
                    {
                        ToolResult result = await provider.ExecuteAsync("run_skill", args.RootElement, skills, ct).ConfigureAwait(false);
                        MuxAssert.IsFalse(result.Success, "refused");
                        MuxAssert.Contains("command_not_found", result.Content, "error code");
                    }
                }
                finally
                {
                    TryDelete(skills);
                }
            }, skip: skillsRoot == null, skipReason: "the skill source tree was not found"));

            cases.Add(new TestCaseDescriptor(SuiteId, "MissingScriptFails", "A command whose script file is missing fails instead of succeeding", async (CancellationToken ct) =>
            {
                string skills = NewTempDirectory();
                string work = NewTempDirectory();
                try
                {
                    string copy = Path.Combine(skills, "statistical-analyst");
                    CopyDirectory(Path.Combine(skillsRoot!, "Packs", "data", "statistical-analyst"), copy);
                    File.Delete(Path.Combine(copy, "scripts", "sample_size_calculator.py"));
                    Skill skill = new SkillLoader(skills).Load(copy);
                    SkillCommand command = FindCommand(skill, "sample-size-calculator");
                    ScriptRun1 run = await RunAsync(skill, command, new[] { "--help" }, work, false, ct).ConfigureAwait(false);
                    MuxAssert.IsTrue(run.ExitCode != 0 || run.Error.Length > 0, "a missing script is an error: exit " + run.ExitCode);
                }
                finally
                {
                    TryDelete(skills);
                    TryDelete(work);
                }
            }, skip: skillsRoot == null || !python, skipReason: "python or the skill source tree is not available"));

            cases.Add(new TestCaseDescriptor(SuiteId, "InvalidArgumentsExitTwo", "Invalid arguments exit 2 (argparse usage errors), matching mux's invalid-input code", async (CancellationToken ct) =>
            {
                string work = NewTempDirectory();
                try
                {
                    Skill stats = LoadSkill(skillsRoot!, "Packs/data/statistical-analyst");
                    ScriptRun1 bad = await RunAsync(stats, FindCommand(stats, "sample-size-calculator"), new[] { "--no-such-option" }, work, false, ct).ConfigureAwait(false);
                    MuxAssert.AreEqual(2, bad.ExitCode, "unknown option exits 2");
                    Skill tdd = LoadSkill(skillsRoot!, "Bundled/tdd-guide");
                    ScriptRun1 noSub = await RunAsync(tdd, FindCommand(tdd, "tdd"), Array.Empty<string>(), work, false, ct).ConfigureAwait(false);
                    MuxAssert.AreEqual(2, noSub.ExitCode, "tdd without a subcommand exits 2");
                    ScriptRun1 missing = await RunAsync(tdd, FindCommand(tdd, "tdd"), new[] { "detect", "--file", "nope.py" }, work, false, ct).ConfigureAwait(false);
                    MuxAssert.AreEqual(2, missing.ExitCode, "a missing input file exits 2");
                    MuxAssert.Contains("file not found", missing.StdErr, "explains");
                }
                finally
                {
                    TryDelete(work);
                }
            }, skip: skillsRoot == null || !python, skipReason: "python or the skill source tree is not available"));

            cases.Add(new TestCaseDescriptor(SuiteId, "TddCliWorks", "tdd-guide's tdd command summarizes coverage (exit 1 below the threshold), detects frameworks, and gives phase guidance", async (CancellationToken ct) =>
            {
                string work = NewTempDirectory();
                try
                {
                    File.WriteAllText(Path.Combine(work, "lcov.info"), "TN:\nSF:src/a.py\nDA:1,1\nDA:2,0\nLF:2\nLH:1\nend_of_record\n");
                    File.WriteAllText(Path.Combine(work, "test_calc.py"), "import pytest\n\ndef test_add():\n    assert 1 + 1 == 2\n");
                    Skill tdd = LoadSkill(skillsRoot!, "Bundled/tdd-guide");
                    SkillCommand command = FindCommand(tdd, "tdd");
                    ScriptRun1 below = await RunAsync(tdd, command, new[] { "coverage", "--report", "lcov.info", "--threshold", "80" }, work, false, ct).ConfigureAwait(false);
                    MuxAssert.AreEqual(1, below.ExitCode, "50 percent is below 80: " + below.StdErr);
                    MuxAssert.Contains("\"line_coverage\": 50.0", below.StdOut, "summary");
                    ScriptRun1 above = await RunAsync(tdd, command, new[] { "coverage", "--report", "lcov.info", "--threshold", "10" }, work, false, ct).ConfigureAwait(false);
                    MuxAssert.AreEqual(0, above.ExitCode, "50 percent clears 10");
                    ScriptRun1 detect = await RunAsync(tdd, command, new[] { "detect", "--file", "test_calc.py" }, work, false, ct).ConfigureAwait(false);
                    MuxAssert.Contains("\"test_framework\": \"pytest\"", detect.StdOut, "framework detected");
                    ScriptRun1 guidance = await RunAsync(tdd, command, new[] { "guidance", "--phase", "green" }, work, false, ct).ConfigureAwait(false);
                    MuxAssert.Contains("GREEN", guidance.StdOut, "phase guidance");
                    ScriptRun1 fixtures = await RunAsync(tdd, command, new[] { "fixtures", "--type", "int", "--min", "0", "--max", "10" }, work, false, ct).ConfigureAwait(false);
                    MuxAssert.Contains("boundary_values", fixtures.StdOut, "fixtures");
                }
                finally
                {
                    TryDelete(work);
                }
            }, skip: skillsRoot == null || !python, skipReason: "python or the skill source tree is not available"));

            return new TestSuiteDescriptor(SuiteId, "Script commands: bundled skills and the security, data, research, business, docs, compliance, and productivity packs", cases);
        }

        #endregion

        #region Private-Methods

        private static string CommandName(string script)
        {
            // tdd_cli.py is exposed as the shorter "tdd" command its documentation uses.
            return script == "tdd_cli.py" ? "tdd" : Path.GetFileNameWithoutExtension(script).Replace('_', '-');
        }

        private static Skill LoadSkill(string skillsRoot, string relative)
        {
            string directory = Path.Combine(skillsRoot, relative.Replace('/', Path.DirectorySeparatorChar));
            Skill skill = new SkillLoader(Path.GetDirectoryName(directory)!).Load(directory);
            MuxAssert.IsTrue(skill.IsValid, relative + " valid: " + string.Join("; ", skill.Validation.Errors));
            return skill;
        }

        private static SkillCommand FindCommand(Skill skill, string name)
        {
            foreach (SkillCommand command in skill.Manifest.Commands)
            {
                if (string.Equals(command.Name, name, StringComparison.Ordinal))
                {
                    return command;
                }
            }

            throw new InvalidOperationException(skill.Manifest.Name + " has no command " + name);
        }

        private static async Task<ScriptRun1> RunAsync(Skill skill, SkillCommand command, IReadOnlyList<string> arguments, string workingDirectory, bool dryRun, CancellationToken ct)
        {
            Dictionary<string, string> environment = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [DefaultSkillHelpers.DryRunVariable] = dryRun ? "1" : "0"
            };
            using (CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(30));
                ToolResult result = await new SkillExecutor().ExecuteAsync("t", skill, command, new List<string>(arguments), workingDirectory, timeout.Token, environment).ConfigureAwait(false);
                using (JsonDocument document = JsonDocument.Parse(result.Content))
                {
                    JsonElement root = document.RootElement;
                    return new ScriptRun1
                    {
                        StdOut = root.TryGetProperty("stdout", out JsonElement o) ? o.GetString() ?? string.Empty : string.Empty,
                        StdErr = root.TryGetProperty("stderr", out JsonElement e) ? e.GetString() ?? string.Empty : string.Empty,
                        ExitCode = root.TryGetProperty("exit_code", out JsonElement x) && x.ValueKind == JsonValueKind.Number ? x.GetInt32() : -1,
                        Error = root.TryGetProperty("message", out JsonElement m) ? m.GetString() ?? string.Empty : string.Empty
                    };
                }
            }
        }

        private static bool HasPython()
        {
            string resolved = SkillInterpreterResolver.ResolvePython();
            return Path.IsPathRooted(resolved) && File.Exists(resolved);
        }

        private static string? FindSkillsRoot()
        {
            DirectoryInfo? directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null)
            {
                string candidate = Path.Combine(directory.FullName, "src", "Mux.Core", "Skills");
                if (Directory.Exists(Path.Combine(candidate, "Bundled")))
                {
                    return candidate;
                }

                candidate = Path.Combine(directory.FullName, "Mux.Core", "Skills");
                if (Directory.Exists(Path.Combine(candidate, "Bundled")))
                {
                    return candidate;
                }

                directory = directory.Parent;
            }

            return null;
        }

        private static string NewTempDirectory()
        {
            string path = Path.Combine(Path.GetTempPath(), "mux-scriptcmd-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }

        private static void CopyDirectory(string source, string target)
        {
            Directory.CreateDirectory(target);
            foreach (string file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            {
                string destination = Path.Combine(target, Path.GetRelativePath(source, file));
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(file, destination, true);
            }
        }

        private static void TryDelete(string path)
        {
            try { Directory.Delete(path, true); } catch (Exception) { }
        }

        #endregion
    }
}
