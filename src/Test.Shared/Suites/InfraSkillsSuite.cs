namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Skills;
    using Test.Shared.Support;
    using Touchstone.Core;

    /// <summary>
    /// Touchstone suite for the Ansible and Bicep skills and the CloudFormation template checks in aws-deploy: preview
    /// commands, the production guard on apply and deploy, and bad input. Positive and negative cases.
    /// </summary>
    public static class InfraSkillsSuite
    {
        #region Private-Members

        private const string SuiteId = "InfraSkills";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Builds the suite descriptor.
        /// </summary>
        /// <returns>The descriptor.</returns>
        public static TestSuiteDescriptor Create()
        {
            bool ready = IsOnPath("pwsh");
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();
            void Add(string id, string name, Func<SkillTestContext, Task> body)
            {
                cases.Add(new TestCaseDescriptor(SuiteId, id, name, (CancellationToken ct) => RunWithContextAsync(body, ct), skip: !ready, skipReason: "pwsh is not on PATH"));
            }

            cases.Add(new TestCaseDescriptor(SuiteId, "SkillsDefined", "ansible and bicep are guarded infrastructure skills, and aws-deploy validates and lints templates", (CancellationToken ct) =>
            {
                Dictionary<string, DefaultSkillDef> defs = DefaultSkillLibrary.Definitions().ToDictionary(d => d.Id, StringComparer.Ordinal);
                MuxAssert.AreEqual("lint,syntax,check,apply,inventory", string.Join(",", defs["ansible"].Commands.Select(c => c.Name)), "ansible commands");
                MuxAssert.AreEqual("build,lint,what-if,deploy", string.Join(",", defs["bicep"].Commands.Select(c => c.Name)), "bicep commands");
                MuxAssert.AreEqual("infrastructure", DefaultSkillCategories.For("ansible"), "ansible category");
                MuxAssert.AreEqual("infrastructure", DefaultSkillCategories.For("bicep"), "bicep category");
                MuxAssert.IsTrue(defs["aws-deploy"].Commands.Any(c => c.Name == "cfn-validate") && defs["aws-deploy"].Commands.Any(c => c.Name == "cfn-lint"), "template checks");
                foreach (string id in new[] { "ansible", "bicep" })
                {
                    MuxAssert.IsFalse(defs[id].Commands.Any(c => c.Name.Contains("destroy", StringComparison.Ordinal) || c.Name.Contains("delete", StringComparison.Ordinal)), id + " offers no destroy");
                }

                return Task.CompletedTask;
            }));

            Add("AnsiblePreviewAndGuard", "ansible previews with --check --diff, guards apply by limit or inventory, and needs a playbook", async (SkillTestContext c) =>
            {
                Write(c, "site.yml", "- hosts: all\n  tasks: []\n");
                Write(c, "inventory/prod.ini", "[web]\nweb1\n");
                (await c.Run(true, "ansible", "syntax", "site.yml").ConfigureAwait(false)).Exit(0).Has("DRYRUN: ansible-playbook --syntax-check site.yml");
                (await c.Run(true, "ansible", "check", "site.yml", "-i", "inventory/staging.ini", "--limit", "web").ConfigureAwait(false))
                    .Exit(0).Has("DRYRUN: ansible-playbook --check --diff -i inventory/staging.ini --limit web site.yml");
                (await c.Run(true, "ansible", "apply", "site.yml", "-i", "inventory/prod.ini").ConfigureAwait(false)).Exit(3).Has("Target: prod").Has("looks like production");
                (await c.Run(true, "ansible", "apply", "site.yml", "-i", "inventory/prod.ini", "--confirm", "prod").ConfigureAwait(false)).Exit(0).Has("DRYRUN: ansible-playbook --diff -i inventory/prod.ini site.yml");
                (await c.Run(true, "ansible", "apply", "site.yml", "--limit", "staging-web").ConfigureAwait(false)).Exit(0).Has("Target: staging-web");
                (await c.Run(true, "ansible", "inventory", "-i", "inventory/prod.ini").ConfigureAwait(false)).Exit(0).Has("DRYRUN: ansible-inventory -i inventory/prod.ini --graph");
                (await c.Run(true, "ansible", "lint", "site.yml").ConfigureAwait(false)).Exit(0).Has("DRYRUN: ansible-lint site.yml");
                (await c.Run(true, "ansible", "check").ConfigureAwait(false)).Exit(2).Has("pass the playbook");
                (await c.Run(false, "ansible", "syntax", "missing.yml").ConfigureAwait(false)).Exit(2).Has("playbook not found");
                (await c.Run(true, "ansible", "check", "site.yml", "-i").ConfigureAwait(false)).Exit(2).Has("-i needs a value");
            });

            Add("BicepPreviewAndGuard", "bicep builds to standard output, previews with what-if, guards deploy by resource group, and needs a target", async (SkillTestContext c) =>
            {
                Write(c, "main.bicep", "param location string = resourceGroup().location\n");
                (await c.Run(true, "bicep", "build", "main.bicep").ConfigureAwait(false)).Exit(0).Has("DRYRUN: az bicep build --file main.bicep --stdout");
                (await c.Run(true, "bicep", "lint", "main.bicep").ConfigureAwait(false)).Exit(0).Has("az bicep lint --file main.bicep");
                (await c.Run(true, "bicep", "what-if", "main.bicep").ConfigureAwait(false)).Exit(2).Has("--resource-group <name>");
                (await c.Run(true, "bicep", "what-if", "main.bicep", "-g", "rg-dev", "-p", "main.bicepparam").ConfigureAwait(false))
                    .Exit(0).Has("az deployment group what-if --resource-group rg-dev --template-file main.bicep --parameters main.bicepparam");
                (await c.Run(true, "bicep", "deploy", "main.bicep", "--resource-group", "rg-prod").ConfigureAwait(false)).Exit(3).Has("'rg-prod' looks like production");
                (await c.Run(true, "bicep", "deploy", "main.bicep", "--resource-group", "rg-prod", "--confirm", "rg-prod").ConfigureAwait(false)).Exit(0).Has("az deployment group create --resource-group rg-prod");
                (await c.Run(true, "bicep", "build").ConfigureAwait(false)).Exit(2).Has("pass the Bicep file");
                (await c.Run(false, "bicep", "build", "missing.bicep").ConfigureAwait(false)).Exit(2).Has("Bicep file not found");
            });

            Add("CloudFormationTemplateChecks", "aws-deploy validates a template through the API and lints it with cfn-lint, and needs a template", async (SkillTestContext c) =>
            {
                Write(c, "template.yaml", "Resources: {}\n");
                (await c.Run(true, "aws-deploy", "cfn-validate", "template.yaml").ConfigureAwait(false)).Exit(0).Has("aws cloudformation validate-template --template-body file://template.yaml");
                (await c.Run(true, "aws-deploy", "cfn-lint", "template.yaml").ConfigureAwait(false)).Exit(0).Has("DRYRUN: cfn-lint template.yaml");
                (await c.Run(true, "aws-deploy", "cfn-validate").ConfigureAwait(false)).Exit(2).Has("pass a template");
                (await c.Run(true, "aws-deploy", "cfn-lint").ConfigureAwait(false)).Exit(2).Has("pass a template");
            });

            return new TestSuiteDescriptor(SuiteId, "Ansible, Bicep, and CloudFormation template skills", cases);
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
            string root = Path.Combine(Path.GetTempPath(), "mux-infra-" + Guid.NewGuid().ToString("N"));
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
