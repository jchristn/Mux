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
    using Mux.Core.Skills.Packaging;
    using Test.Shared.Support;
    using Touchstone.Core;

    /// <summary>
    /// Touchstone suite for the script-backed commands added to the imported engineering (ids m through z) and product
    /// pack skills: every command is declared with the python interpreter and an existing script, the bodies document
    /// <c>run_skill</c>, a dry run reports the command without running it, every script answers <c>--help</c> cleanly
    /// through the skill executor, two deterministic real runs produce results, and exit codes pass through. Negative
    /// cases cover an unknown command, a missing script, and invalid arguments. Real runs are skipped without Python.
    /// </summary>
    public static class ScriptCommandsSuite3
    {
        #region Private-Members

        private const string SuiteId = "ScriptCommands3";

        private static readonly EngineeringProductScriptCommand[] _Expected = new EngineeringProductScriptCommand[]
        {
                new EngineeringProductScriptCommand("engineering", "mcp-server-builder", "mcp-validator", "scripts/mcp_validator.py", 0),
                new EngineeringProductScriptCommand("engineering", "mcp-server-builder", "openapi-to-mcp", "scripts/openapi_to_mcp.py", 0),
                new EngineeringProductScriptCommand("engineering", "migration-architect", "compatibility-checker", "scripts/compatibility_checker.py", 0),
                new EngineeringProductScriptCommand("engineering", "migration-architect", "migration-planner", "scripts/migration_planner.py", 0),
                new EngineeringProductScriptCommand("engineering", "migration-architect", "rollback-generator", "scripts/rollback_generator.py", 0),
                new EngineeringProductScriptCommand("engineering", "observability-designer", "alert-optimizer", "scripts/alert_optimizer.py", 0),
                new EngineeringProductScriptCommand("engineering", "observability-designer", "dashboard-generator", "scripts/dashboard_generator.py", 0),
                new EngineeringProductScriptCommand("engineering", "observability-designer", "slo-designer", "scripts/slo_designer.py", 0),
                new EngineeringProductScriptCommand("engineering", "runbook-generator", "runbook-generator", "scripts/runbook_generator.py", 0),
                new EngineeringProductScriptCommand("engineering", "secrets-vault-manager", "audit-log-analyzer", "scripts/audit_log_analyzer.py", 0),
                new EngineeringProductScriptCommand("engineering", "secrets-vault-manager", "rotation-planner", "scripts/rotation_planner.py", 0),
                new EngineeringProductScriptCommand("engineering", "secrets-vault-manager", "vault-config-generator", "scripts/vault_config_generator.py", 0),
                new EngineeringProductScriptCommand("engineering", "senior-architect", "architecture-diagram-generator", "scripts/architecture_diagram_generator.py", 0),
                new EngineeringProductScriptCommand("engineering", "senior-architect", "dependency-analyzer", "scripts/dependency_analyzer.py", 300000),
                new EngineeringProductScriptCommand("engineering", "senior-architect", "project-architect", "scripts/project_architect.py", 300000),
                new EngineeringProductScriptCommand("engineering", "senior-backend", "api-load-tester", "scripts/api_load_tester.py", 600000),
                new EngineeringProductScriptCommand("engineering", "senior-backend", "api-scaffolder", "scripts/api_scaffolder.py", 0),
                new EngineeringProductScriptCommand("engineering", "senior-backend", "backend-decision-engine", "scripts/backend_decision_engine.py", 0),
                new EngineeringProductScriptCommand("engineering", "senior-backend", "database-migration-tool", "scripts/database_migration_tool.py", 0),
                new EngineeringProductScriptCommand("engineering", "senior-devops", "deployment-manager", "scripts/deployment_manager.py", 0),
                new EngineeringProductScriptCommand("engineering", "senior-devops", "pipeline-generator", "scripts/pipeline_generator.py", 0),
                new EngineeringProductScriptCommand("engineering", "senior-devops", "terraform-scaffolder", "scripts/terraform_scaffolder.py", 600000),
                new EngineeringProductScriptCommand("engineering", "senior-frontend", "bundle-analyzer", "scripts/bundle_analyzer.py", 300000),
                new EngineeringProductScriptCommand("engineering", "senior-frontend", "component-generator", "scripts/component_generator.py", 0),
                new EngineeringProductScriptCommand("engineering", "senior-frontend", "frontend-decision-engine", "scripts/frontend_decision_engine.py", 0),
                new EngineeringProductScriptCommand("engineering", "senior-frontend", "frontend-scaffolder", "scripts/frontend_scaffolder.py", 0),
                new EngineeringProductScriptCommand("engineering", "senior-qa", "coverage-analyzer", "scripts/coverage_analyzer.py", 300000),
                new EngineeringProductScriptCommand("engineering", "senior-qa", "e2e-test-scaffolder", "scripts/e2e_test_scaffolder.py", 0),
                new EngineeringProductScriptCommand("engineering", "senior-qa", "test-suite-generator", "scripts/test_suite_generator.py", 0),
                new EngineeringProductScriptCommand("engineering", "senior-secops", "compliance-checker", "scripts/compliance_checker.py", 300000),
                new EngineeringProductScriptCommand("engineering", "senior-secops", "security-scanner", "scripts/security_scanner.py", 300000),
                new EngineeringProductScriptCommand("engineering", "senior-secops", "vulnerability-assessor", "scripts/vulnerability_assessor.py", 300000),
                new EngineeringProductScriptCommand("engineering", "slo-architect", "error-budget-calculator", "scripts/error_budget_calculator.py", 0),
                new EngineeringProductScriptCommand("engineering", "slo-architect", "slo-designer", "scripts/slo_designer.py", 0),
                new EngineeringProductScriptCommand("engineering", "slo-architect", "slo-review", "scripts/slo_review.py", 0),
                new EngineeringProductScriptCommand("engineering", "snowflake-development", "snowflake-query-helper", "scripts/snowflake_query_helper.py", 0),
                new EngineeringProductScriptCommand("engineering", "spec-driven-workflow", "spec-generator", "scripts/spec_generator.py", 0),
                new EngineeringProductScriptCommand("engineering", "spec-driven-workflow", "spec-validator", "scripts/spec_validator.py", 300000),
                new EngineeringProductScriptCommand("engineering", "spec-driven-workflow", "test-extractor", "scripts/test_extractor.py", 300000),
                new EngineeringProductScriptCommand("engineering", "sql-database-assistant", "migration-generator", "scripts/migration_generator.py", 0),
                new EngineeringProductScriptCommand("engineering", "sql-database-assistant", "query-optimizer", "scripts/query_optimizer.py", 0),
                new EngineeringProductScriptCommand("engineering", "sql-database-assistant", "schema-explorer", "scripts/schema_explorer.py", 0),
                new EngineeringProductScriptCommand("product", "apple-hig-expert", "hig-checker", "scripts/hig_checker.py", 300000),
                new EngineeringProductScriptCommand("product", "code-to-prd", "codebase-analyzer", "scripts/codebase_analyzer.py", 300000),
                new EngineeringProductScriptCommand("product", "code-to-prd", "prd-scaffolder", "scripts/prd_scaffolder.py", 0),
                new EngineeringProductScriptCommand("product", "competitive-teardown", "competitive-matrix-builder", "scripts/competitive_matrix_builder.py", 0),
                new EngineeringProductScriptCommand("product", "confluence-expert", "content-audit-analyzer", "scripts/content_audit_analyzer.py", 0),
                new EngineeringProductScriptCommand("product", "confluence-expert", "space-structure-generator", "scripts/space_structure_generator.py", 0),
                new EngineeringProductScriptCommand("product", "experiment-designer", "sample-size-calculator", "scripts/sample_size_calculator.py", 0),
                new EngineeringProductScriptCommand("product", "jira-expert", "jql-query-builder", "scripts/jql_query_builder.py", 0),
                new EngineeringProductScriptCommand("product", "jira-expert", "workflow-validator", "scripts/workflow_validator.py", 0),
                new EngineeringProductScriptCommand("product", "landing-page-generator", "landing-page-scaffolder", "scripts/landing_page_scaffolder.py", 0),
                new EngineeringProductScriptCommand("product", "product-analytics", "metrics-calculator", "scripts/metrics_calculator.py", 0),
                new EngineeringProductScriptCommand("product", "product-discovery", "assumption-mapper", "scripts/assumption_mapper.py", 0),
                new EngineeringProductScriptCommand("product", "product-manager-toolkit", "customer-interview-analyzer", "scripts/customer_interview_analyzer.py", 0),
                new EngineeringProductScriptCommand("product", "product-manager-toolkit", "rice-prioritizer", "scripts/rice_prioritizer.py", 0),
                new EngineeringProductScriptCommand("product", "product-strategist", "okr-cascade-generator", "scripts/okr_cascade_generator.py", 0),
                new EngineeringProductScriptCommand("product", "roadmap-communicator", "changelog-generator", "scripts/changelog_generator.py", 0),
                new EngineeringProductScriptCommand("product", "saas-scaffolder", "project-bootstrapper", "scripts/project_bootstrapper.py", 0),
                new EngineeringProductScriptCommand("product", "scrum-master", "retrospective-analyzer", "scripts/retrospective_analyzer.py", 0),
                new EngineeringProductScriptCommand("product", "scrum-master", "sprint-health-scorer", "scripts/sprint_health_scorer.py", 0),
                new EngineeringProductScriptCommand("product", "scrum-master", "velocity-analyzer", "scripts/velocity_analyzer.py", 0),
                new EngineeringProductScriptCommand("product", "spec-to-repo", "validate-project", "scripts/validate_project.py", 300000),
                new EngineeringProductScriptCommand("product", "ui-design-system", "design-token-generator", "scripts/design_token_generator.py", 0)
        };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Builds the suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/> for the engineering and product script commands.</returns>
        public static TestSuiteDescriptor Create()
        {
            bool python = HasPython();
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();
            void Add(string id, string name, bool needsPython, Func<SkillTestContext, Task> body)
            {
                cases.Add(new TestCaseDescriptor(SuiteId, id, name, (CancellationToken ct) => RunWithSkillsAsync(body, ct), skip: needsPython && !python, skipReason: "Python 3 is not on PATH"));
            }

            Add("CommandsDeclared", "Every engineering (m-z) and product script command is declared with python, an existing script, and its timeout", false, (SkillTestContext c) =>
            {
                foreach (string skillId in SkillIds())
                {
                    Skill skill = Load(c, skillId);
                    MuxAssert.IsTrue(skill.IsValid, skillId + " valid: " + string.Join("; ", skill.Validation.Errors));
                    MuxAssert.AreEqual(0, skill.Validation.Warnings.Count, skillId + " has no warnings: " + string.Join("; ", skill.Validation.Warnings));
                    List<EngineeringProductScriptCommand> expected = _Expected.FindAllFor(skillId);
                    MuxAssert.AreEqual(expected.Count, skill.Manifest.Commands.Count, skillId + " command count");
                    foreach (EngineeringProductScriptCommand entry in expected)
                    {
                        SkillCommand? command = skill.Manifest.Commands.Find((SkillCommand x) => x.Name == entry.Command);
                        MuxAssert.IsNotNull(command, skillId + " declares " + entry.Command);
                        MuxAssert.AreEqual("python", command!.Interpreter, entry.Command + " interpreter");
                        MuxAssert.AreEqual(entry.Script, command.ScriptPath, entry.Command + " script path");
                        MuxAssert.IsTrue(File.Exists(Path.Combine(skill.DirectoryPath, entry.Script)), entry.Command + " script exists");
                        MuxAssert.AreEqual(entry.TimeoutMs == 0 ? 120000 : entry.TimeoutMs, command.TimeoutMs, entry.Command + " timeout");
                        MuxAssert.IsFalse(string.IsNullOrWhiteSpace(command.Description), entry.Command + " has a description");
                    }
                }

                return Task.CompletedTask;
            });

            Add("BodiesDocumentCommands", "Each skill body tells the model to use run_skill and lists every command with its script", false, (SkillTestContext c) =>
            {
                foreach (string skillId in SkillIds())
                {
                    Skill skill = Load(c, skillId);
                    string body = File.ReadAllText(Path.Combine(skill.DirectoryPath, "SKILL.md"));
                    MuxAssert.Contains("`run_skill " + skillId + " <command> [arguments]`", body, skillId + " documents run_skill");
                    MuxAssert.Contains("python3 \"${SKILL_DIR}/scripts/<file>\"", body, skillId + " keeps the direct form");
                    MuxAssert.Contains("MUX_SKILL_DRY_RUN=1", body, skillId + " documents dry runs");
                    MuxAssert.IsFalse(body.Contains('\u2014'), skillId + " has no em-dash");
                    foreach (EngineeringProductScriptCommand entry in _Expected.FindAllFor(skillId))
                    {
                        MuxAssert.Contains("| `" + entry.Command + "` | `" + entry.Script + "` |", body, skillId + " lists " + entry.Command);
                    }
                }

                MuxAssert.Contains("exit 2 here means \"stop and fix\"", File.ReadAllText(Path.Combine(Load(c, "senior-secops").DirectoryPath, "SKILL.md")), "secops explains its exit 2");
                MuxAssert.Contains("sends real HTTP requests", File.ReadAllText(Path.Combine(Load(c, "senior-backend").DirectoryPath, "SKILL.md")), "load tester warning");
                MuxAssert.Contains("runs `terraform fmt`", File.ReadAllText(Path.Combine(Load(c, "senior-devops").DirectoryPath, "SKILL.md")), "terraform note");
                MuxAssert.Contains("YAML specs need PyYAML", File.ReadAllText(Path.Combine(Load(c, "mcp-server-builder").DirectoryPath, "SKILL.md")), "PyYAML note");
                MuxAssert.Contains("`git log`", File.ReadAllText(Path.Combine(Load(c, "roadmap-communicator").DirectoryPath, "SKILL.md")), "git note");
                return Task.CompletedTask;
            });

            Add("DryRunReportsWithoutRunning", "A dry run of every command reports the command and exit 0 without creating files", false, async (SkillTestContext c) =>
            {
                Directory.CreateDirectory(c.Project);
                foreach (EngineeringProductScriptCommand entry in _Expected)
                {
                    (await c.Run(true, entry.Skill, entry.Command, "--help").ConfigureAwait(false))
                        .Exit(0)
                        .Has("DRYRUN: python " + entry.Script + " --help");
                }

                MuxAssert.AreEqual(0, Directory.GetFileSystemEntries(c.Project).Length, "dry runs create nothing");
            });

            foreach (string skillId in SkillIds())
            {
                string id = skillId;
                Add("HelpRuns_" + id, id + ": every command answers --help through the skill executor without a traceback", true, async (SkillTestContext c) =>
                {
                    Directory.CreateDirectory(c.Project);
                    foreach (EngineeringProductScriptCommand entry in _Expected.FindAllFor(id))
                    {
                        SkillRunResult result = await c.Run(entry.Skill, entry.Command, "--help").ConfigureAwait(false);
                        MuxAssert.IsTrue(result.ExitCode >= 0 && result.ExitCode <= 2, entry.Command + " exit " + result.ExitCode + ": " + result.Stderr);
                        result.Lacks("Traceback").Has("usage:");
                    }

                    MuxAssert.AreEqual(0, Directory.GetFileSystemEntries(c.Project).Length, id + " --help writes nothing");
                });
            }

            Add("RealRunsProduceResults", "Deterministic real runs return the computed results", true, async (SkillTestContext c) =>
            {
                Directory.CreateDirectory(c.Project);
                (await c.Run("experiment-designer", "sample-size-calculator", "--baseline-rate", "0.1", "--mde", "0.02", "--mde-type", "absolute").ConfigureAwait(false))
                    .Exit(0).Has("n_per_group: 3843").Has("n_total: 7686");
                SkillRunResult budget = await c.Run("slo-architect", "error-budget-calculator", "--target", "99.9", "--window-days", "30", "--format", "json").ConfigureAwait(false);
                budget.Exit(0).Has("\"budget_minutes\": 43.2");
                using (JsonDocument doc = JsonDocument.Parse(budget.Stdout))
                {
                    MuxAssert.AreEqual(30, doc.RootElement.GetProperty("window_days").GetInt32(), "valid JSON output");
                }
            });

            Add("FindingsExitOne", "A script's own failure code passes through: validate-project on an empty project exits 1", true, async (SkillTestContext c) =>
            {
                Directory.CreateDirectory(Path.Combine(c.Project, "empty"));
                (await c.Run("spec-to-repo", "validate-project", "empty").ConfigureAwait(false)).Exit(1);
            });

            Add("InvalidArgumentsExitTwo", "Unknown options and bad choices exit 2 with usage text", true, async (SkillTestContext c) =>
            {
                Directory.CreateDirectory(c.Project);
                SkillRunResult unknown = await c.Run("experiment-designer", "sample-size-calculator", "--definitely-not-an-option").ConfigureAwait(false);
                unknown.Exit(2);
                MuxAssert.Contains("usage:", unknown.Stderr, "argparse prints usage on stderr");
                MuxAssert.DoesNotContain("Traceback", unknown.Stderr, "no traceback");
                SkillRunResult missing = await c.Run("experiment-designer", "sample-size-calculator").ConfigureAwait(false);
                missing.Exit(2);
                MuxAssert.Contains("required", missing.Stderr, "missing required options are named");
                SkillRunResult choice = await c.Run("slo-architect", "error-budget-calculator", "--format", "xml").ConfigureAwait(false);
                choice.Exit(2);
                MuxAssert.Contains("invalid choice", choice.Stderr, "bad choice is named");
            });

            Add("UnknownCommandRejected", "run_skill refuses a command the skill does not declare", false, async (SkillTestContext c) =>
            {
                Directory.CreateDirectory(c.Project);
                SkillToolProvider provider = new SkillToolProvider(new SkillCatalog(new SkillLoader(c.Skills).Discover()), new SkillExecutor());
                using (JsonDocument args = JsonDocument.Parse("{\"name\":\"senior-secops\",\"command\":\"no-such-command\",\"args\":[]}"))
                {
                    ToolResult result = await provider.ExecuteAsync("run_skill", args.RootElement, c.Project, c.Token).ConfigureAwait(false);
                    MuxAssert.IsFalse(result.Success, "refused");
                    MuxAssert.Contains("command_not_found", result.Content, "error code");
                }

                using (JsonDocument args = JsonDocument.Parse("{\"name\":\"experiment-designer\",\"command\":\"sample-size-calculator\",\"args\":[\"--help\"]}"))
                {
                    ToolResult known = await provider.ExecuteAsync("run_skill", args.RootElement, c.Project, c.Token).ConfigureAwait(false);
                    MuxAssert.DoesNotContain("command_not_found", known.Content, "a declared command is accepted");
                }
            });

            Add("MissingScriptFailsValidation", "A command whose script was deleted makes the skill invalid", false, (SkillTestContext c) =>
            {
                Skill before = Load(c, "jira-expert");
                MuxAssert.IsTrue(before.IsValid, "valid before");
                File.Delete(Path.Combine(before.DirectoryPath, "scripts", "jql_query_builder.py"));
                Skill after = Load(c, "jira-expert");
                MuxAssert.IsFalse(after.IsValid, "invalid after the script is gone");
                MuxAssert.IsTrue(after.Validation.Errors.Exists((string e) => e.Contains("scripts/jql_query_builder.py", StringComparison.Ordinal) && e.Contains("was not found", StringComparison.Ordinal)), "names the missing script: " + string.Join("; ", after.Validation.Errors));
                return Task.CompletedTask;
            });

            return new TestSuiteDescriptor(SuiteId, "Script commands: engineering (m-z) and product pack skills", cases);
        }

        #endregion

        #region Private-Methods

        private static List<EngineeringProductScriptCommand> FindAllFor(this EngineeringProductScriptCommand[] all, string skillId)
        {
            List<EngineeringProductScriptCommand> result = new List<EngineeringProductScriptCommand>();
            foreach (EngineeringProductScriptCommand entry in all)
            {
                if (entry.Skill == skillId) result.Add(entry);
            }

            return result;
        }

        private static List<string> SkillIds()
        {
            List<string> ids = new List<string>();
            foreach (EngineeringProductScriptCommand entry in _Expected)
            {
                if (!ids.Contains(entry.Skill)) ids.Add(entry.Skill);
            }

            return ids;
        }

        private static Skill Load(SkillTestContext c, string skillId)
        {
            return new SkillLoader(c.Skills).Load(Path.Combine(c.Skills, skillId));
        }

        private static async Task RunWithSkillsAsync(Func<SkillTestContext, Task> body, CancellationToken ct)
        {
            string root = Path.Combine(Path.GetTempPath(), "mux-scripts3-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                string skills = Path.Combine(root, "skills");
                Directory.CreateDirectory(skills);
                SkillPackInstaller installer = new SkillPackInstaller(skills);
                foreach (EngineeringProductScriptCommand entry in _Expected)
                {
                    if (!Directory.Exists(Path.Combine(skills, entry.Skill)))
                    {
                        installer.Install(entry.Pack, entry.Skill);
                    }
                }

                await body(new SkillTestContext(root, skills, ct)).ConfigureAwait(false);
            }
            finally
            {
                try { Directory.Delete(root, true); } catch (Exception) { }
            }
        }

        private static bool HasPython()
        {
            string resolved = SkillInterpreterResolver.ResolvePython();
            return !string.Equals(resolved, "python", StringComparison.Ordinal) || IsOnPath("python");
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
