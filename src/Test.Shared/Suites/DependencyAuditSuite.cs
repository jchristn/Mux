namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Models;
    using Mux.Core.Skills;
    using Test.Shared.Support;
    using Touchstone.Core;

    /// <summary>
    /// Touchstone suite for the dependency supply-chain skills: <c>deps-audit</c> parsing every auditor's recorded JSON,
    /// merging and thresholds, detection in dry runs, and bad input; and <c>sbom</c>. Positive and negative cases.
    /// </summary>
    public static class DependencyAuditSuite
    {
        #region Private-Members

        private const string SuiteId = "DependencyAudit";

        private const string NpmJson = @"{""auditReportVersion"":2,""vulnerabilities"":{""lodash"":{""name"":""lodash"",""severity"":""high"",""isDirect"":true,""via"":[{""source"":1096305,""name"":""lodash"",""dependency"":""lodash"",""title"":""Prototype Pollution in lodash"",""url"":""https://github.com/advisories/GHSA-p6mc-m468-83gw"",""severity"":""high"",""range"":""<4.17.19""}],""effects"":[],""range"":""<=4.17.18"",""nodes"":[""node_modules/lodash""],""fixAvailable"":{""name"":""lodash"",""version"":""4.17.21"",""isSemVerMajor"":false}},""minimist"":{""name"":""minimist"",""severity"":""critical"",""isDirect"":false,""via"":[{""source"":1097678,""name"":""minimist"",""dependency"":""minimist"",""title"":""Prototype Pollution in minimist"",""url"":""https://github.com/advisories/GHSA-xvch-5gv4-984h"",""severity"":""critical"",""range"":""<0.2.4""}],""effects"":[""mkdirp""],""range"":""<0.2.4"",""nodes"":[""node_modules/minimist""],""fixAvailable"":true},""mkdirp"":{""name"":""mkdirp"",""severity"":""critical"",""isDirect"":true,""via"":[""minimist""],""effects"":[],""range"":""0.4.1 - 0.5.1"",""nodes"":[""node_modules/mkdirp""],""fixAvailable"":false}},""metadata"":{""vulnerabilities"":{""info"":0,""low"":0,""moderate"":0,""high"":1,""critical"":2,""total"":3}}}
";

        private const string NpmCleanJson = @"{""auditReportVersion"":2,""vulnerabilities"":{},""metadata"":{""vulnerabilities"":{""total"":0}}}
";

        private const string PnpmJson = @"{""advisories"":{""1096305"":{""module_name"":""lodash"",""severity"":""moderate"",""title"":""Prototype Pollution"",""url"":""https://github.com/advisories/GHSA-p6mc-m468-83gw"",""vulnerable_versions"":""<4.17.19"",""patched_versions"":"">=4.17.19"",""findings"":[{""version"":""4.17.15"",""paths"":[""lodash""]},{""version"":""4.17.15"",""paths"":[""a>lodash""]}]}},""metadata"":{}}
";

        private const string YarnJson = @"{""type"":""auditAdvisory"",""data"":{""resolution"":{""id"":1,""path"":""axios""},""advisory"":{""module_name"":""axios"",""severity"":""moderate"",""title"":""Server-Side Request Forgery"",""url"":""https://github.com/advisories/GHSA-4w2v-q235-vp99"",""patched_versions"":"">=0.21.1"",""findings"":[{""version"":""0.21.0""}]}}}
{""type"":""auditSummary"",""data"":{""vulnerabilities"":{""moderate"":1}}}
";

        private const string PipJson = @"{""dependencies"":[{""name"":""requests"",""version"":""2.25.0"",""vulns"":[{""id"":""PYSEC-2023-74"",""fix_versions"":[""2.31.0""],""aliases"":[""CVE-2023-32681""],""description"":""Leaks Proxy-Authorization headers.""}]},{""name"":""flask"",""version"":""2.3.3"",""vulns"":[]}],""fixes"":[]}
";

        private const string CargoJson = @"{""database"":{},""lockfile"":{""dependency-count"":10},""settings"":{},""vulnerabilities"":{""found"":true,""count"":1,""list"":[{""advisory"":{""id"":""RUSTSEC-2020-0071"",""package"":""time"",""title"":""Potential segfault in the time crate"",""url"":""https://github.com/time-rs/time/issues/293"",""cvss"":""CVSS:3.1/AV:N/AC:H/PR:N/UI:N/S:U/C:N/I:N/A:H""},""versions"":{""patched"":["">=0.2.23""],""unaffected"":[""=0.2.0""]},""affected"":null,""package"":{""name"":""time"",""version"":""0.1.45"",""source"":""registry+https://github.com/rust-lang/crates.io-index""}}]},""warnings"":{}}
";

        private const string DotnetJson = @"{""version"":1,""parameters"":""--vulnerable --include-transitive"",""sources"":[""https://api.nuget.org/v3/index.json""],""projects"":[{""path"":""/r/App.csproj"",""frameworks"":[{""framework"":""net8.0"",""topLevelPackages"":[{""id"":""Newtonsoft.Json"",""requestedVersion"":""12.0.1"",""resolvedVersion"":""12.0.1"",""vulnerabilities"":[{""severity"":""High"",""advisoryurl"":""https://github.com/advisories/GHSA-5crp-9r3c-p9vr""}]}],""transitivePackages"":[{""id"":""System.Text.Encodings.Web"",""resolvedVersion"":""4.5.0"",""vulnerabilities"":[{""severity"":""Critical"",""advisoryurl"":""https://github.com/advisories/GHSA-ghhp-997w-qr28""}]}]},{""framework"":""net10.0"",""topLevelPackages"":[{""id"":""Newtonsoft.Json"",""requestedVersion"":""12.0.1"",""resolvedVersion"":""12.0.1"",""vulnerabilities"":[{""severity"":""High"",""advisoryurl"":""https://github.com/advisories/GHSA-5crp-9r3c-p9vr""}]}]}]},{""path"":""/r/Lib.csproj""}]}
";

        private const string GovulncheckJson = @"{
  ""config"": {
    ""protocol_version"": ""v1.0.0"",
    ""scanner_name"": ""govulncheck""
  }
}
{
  ""osv"": {
    ""id"": ""GO-2023-1840"",
    ""summary"": ""Unsafe {behavior} in \""setuid\"" binaries""
  }
}
{
  ""finding"": {
    ""osv"": ""GO-2023-1840"",
    ""fixed_version"": ""v1.20.5"",
    ""trace"": [
      {
        ""module"": ""stdlib"",
        ""version"": ""v1.20.1"",
        ""package"": ""runtime""
      }
    ]
  }
}
{
  ""finding"": {
    ""osv"": ""GO-2023-1840"",
    ""fixed_version"": ""v1.20.5"",
    ""trace"": [
      {
        ""module"": ""stdlib"",
        ""version"": ""v1.20.1"",
        ""package"": ""runtime"",
        ""function"": ""Start""
      }
    ]
  }
}
";

        private const string OsvJson = @"{""results"":[{""source"":{""path"":""/r/package-lock.json"",""type"":""lockfile""},""packages"":[{""package"":{""name"":""lodash"",""version"":""4.17.15"",""ecosystem"":""npm""},""vulnerabilities"":[{""id"":""GHSA-p6mc-m468-83gw"",""summary"":""Prototype Pollution"",""affected"":[{""ranges"":[{""type"":""SEMVER"",""events"":[{""introduced"":""0""},{""fixed"":""4.17.19""}]}]}],""database_specific"":{""severity"":""HIGH""}}],""groups"":[{""ids"":[""GHSA-p6mc-m468-83gw""],""max_severity"":""7.4""}]},{""package"":{""name"":""requests"",""version"":""2.25.0"",""ecosystem"":""PyPI""},""vulnerabilities"":[{""id"":""PYSEC-2023-74"",""affected"":[{""ranges"":[{""type"":""ECOSYSTEM"",""events"":[{""introduced"":""0""},{""fixed"":""2.31.0""}]}]}]}],""groups"":[{""ids"":[""PYSEC-2023-74""],""max_severity"":""6.1""}]}]}]}
";

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
            void Add(string id, string name, Func<SkillTestContext, Task> body, bool skip = false, string reason = "")
            {
                cases.Add(new TestCaseDescriptor(SuiteId, id, name, (CancellationToken ct) => RunWithContextAsync(body, ct), skip: !ready || skip, skipReason: !ready ? "pwsh is not on PATH" : reason));
            }

            cases.Add(new TestCaseDescriptor(SuiteId, "SkillsDefined", "deps-audit is read-only and sbom mutating, both in security and gated on manifests and lockfiles", (CancellationToken ct) =>
            {
                Dictionary<string, DefaultSkillDef> defs = DefaultSkillLibrary.Definitions().ToDictionary(d => d.Id, StringComparer.Ordinal);
                MuxAssert.IsFalse(defs["deps-audit"].Mutating, "deps-audit is read-only");
                MuxAssert.IsTrue(defs["sbom"].Mutating, "sbom writes a file");
                MuxAssert.AreEqual("security", DefaultSkillCategories.For("deps-audit"), "deps-audit category");
                MuxAssert.AreEqual("security", DefaultSkillCategories.For("sbom"), "sbom category");
                foreach (string glob in new[] { "package.json", "Cargo.lock", "go.mod", "**/*.csproj", "requirements*.txt" })
                {
                    MuxAssert.IsTrue(defs["deps-audit"].AppliesTo.Contains(glob), "gated on " + glob);
                }

                return Task.CompletedTask;
            }));

            Add("NpmReport", "npm audit JSON: advisories with severity and fix, transitive-only entries left to their source", async (SkillTestContext c) =>
            {
                string file = Write(c, "npm.json", NpmJson);
                (await c.Run("deps-audit", "audit", "--from", "npm=" + file).ConfigureAwait(false))
                    .Exit(1).Has("minimist").Has("critical").Has("GHSA-xvch-5gv4-984h").Has("available")
                    .Has("lodash").Has("GHSA-p6mc-m468-83gw").Has("lodash@4.17.21").Lacks("mkdirp")
                    .Has("2 advisories (1 critical, 1 high); 2 at or above high.");
            });
            Add("CleanReportPasses", "A report with no advisories says so and exits 0", async (SkillTestContext c) =>
            {
                string file = Write(c, "npm.json", NpmCleanJson);
                (await c.Run("deps-audit", "audit", "--from", "npm=" + file).ConfigureAwait(false)).Exit(0).Has("No known vulnerabilities (npm).");
            });
            Add("NpmV6AndYarnReports", "pnpm's advisories format and yarn 1's line-delimited JSON are read, with duplicate findings merged", async (SkillTestContext c) =>
            {
                string pnpm = Write(c, "pnpm.json", PnpmJson);
                (await c.Run("deps-audit", "audit", "--from", "pnpm=" + pnpm).ConfigureAwait(false))
                    .Exit(0).Has("lodash").Has("4.17.15").Has("moderate").Has(">=4.17.19").Has("1 advisory (1 moderate); 0 at or above high.");
                string yarn = Write(c, "yarn.json", YarnJson);
                (await c.Run("deps-audit", "audit", "--from", "yarn=" + yarn, "--min-severity", "medium").ConfigureAwait(false))
                    .Exit(1).Has("axios").Has("GHSA-4w2v-q235-vp99").Has("1 at or above moderate.");
            });
            Add("PipCargoGovulncheckReports", "pip-audit, cargo-audit, and govulncheck reports are read; without a severity they count as high", async (SkillTestContext c) =>
            {
                (await c.Run("deps-audit", "audit", "--from", "pip-audit=" + Write(c, "pip.json", PipJson)).ConfigureAwait(false))
                    .Exit(1).Has("python").Has("requests").Has("2.25.0").Has("PYSEC-2023-74").Has("2.31.0").Has("unknown").Lacks("flask");
                (await c.Run("deps-audit", "audit", "--from", "cargo-audit=" + Write(c, "cargo.json", CargoJson)).ConfigureAwait(false))
                    .Exit(1).Has("rust").Has("time").Has("0.1.45").Has("RUSTSEC-2020-0071").Has(">=0.2.23");
                (await c.Run("deps-audit", "audit", "--from", "govulncheck=" + Write(c, "go.json", GovulncheckJson)).ConfigureAwait(false))
                    .Exit(1).Has("stdlib").Has("v1.20.1").Has("GO-2023-1840").Has("v1.20.5").Has("1 advisory (1 unknown)");
                (await c.Run("deps-audit", "audit", "--from", "pip-audit=" + Write(c, "pip2.json", PipJson), "--min-severity", "critical").ConfigureAwait(false))
                    .Exit(0).Has("0 at or above critical.");
            });
            Add("DotnetAndOsvReports", "dotnet list package and osv-scanner reports are read, deduplicated across frameworks, with CVSS scores mapped", async (SkillTestContext c) =>
            {
                SkillRunResult dotnet = await c.Run("deps-audit", "audit", "--from", "dotnet=" + Write(c, "dotnet.json", DotnetJson)).ConfigureAwait(false);
                dotnet.Exit(1).Has("System.Text.Encodings.Web").Has("GHSA-ghhp-997w-qr28").Has("Newtonsoft.Json").Has("2 advisories (1 critical, 1 high)");
                MuxAssert.AreEqual(1, CountOccurrences(dotnet.Stdout, "GHSA-5crp-9r3c-p9vr"), "the same advisory in two frameworks is one row");
                MuxAssert.IsTrue(dotnet.Stdout.IndexOf("System.Text.Encodings.Web", StringComparison.Ordinal) < dotnet.Stdout.IndexOf("Newtonsoft.Json", StringComparison.Ordinal), "critical sorts first");
                (await c.Run("deps-audit", "audit", "--from", "osv-scanner=" + Write(c, "osv.json", OsvJson)).ConfigureAwait(false))
                    .Exit(1).Has("npm").Has("lodash").Has("4.17.19").Has("pypi").Has("requests").Has("moderate").Has("1 high, 1 moderate");
            });
            Add("MergedSourcesAndThresholds", "Several --from sources merge into one report, and --min-severity moves the exit code", async (SkillTestContext c) =>
            {
                string npm = Write(c, "npm.json", NpmJson);
                string pnpm = Write(c, "pnpm.json", PnpmJson);
                (await c.Run("deps-audit", "audit", "--from", "npm=" + npm, "--from", "pip-audit=" + Write(c, "pip.json", PipJson)).ConfigureAwait(false))
                    .Exit(1).Has("3 advisories (1 critical, 1 high, 1 unknown)");
                (await c.Run("deps-audit", "audit", "--from", "pnpm=" + pnpm, "--min-severity", "low").ConfigureAwait(false)).Exit(1).Has("1 at or above low.");
                (await c.Run("deps-audit", "audit", "--from", "pnpm=" + pnpm, "--min-severity", "HIGH").ConfigureAwait(false)).Exit(0);
            });
            Add("BadInputIsRejected", "Unknown tools, missing or unreadable files, bad severities, and stray arguments exit 2 with the reason", async (SkillTestContext c) =>
            {
                string good = Write(c, "npm.json", NpmJson);
                (await c.Run("deps-audit", "audit", "--from", "bower=" + good).ConfigureAwait(false)).Exit(2).Has("unknown tool bower");
                (await c.Run("deps-audit", "audit", "--from", "npm=" + Path.Combine(c.Project, "missing.json")).ConfigureAwait(false)).Exit(2).Has("file not found");
                (await c.Run("deps-audit", "audit", "--from", "npm=" + Write(c, "bad.json", "not json")).ConfigureAwait(false)).Exit(2).Has("could not read npm output");
                (await c.Run("deps-audit", "audit", "--from", "dotnet=" + Write(c, "wrong.json", "{\"x\":1}")).ConfigureAwait(false)).Exit(2).Has("no projects field");
                (await c.Run("deps-audit", "audit", "--from", "npm").ConfigureAwait(false)).Exit(2).Has("--from takes <tool>=<file>");
                (await c.Run("deps-audit", "audit", "--from").ConfigureAwait(false)).Exit(2).Has("--from needs <tool>=<file>");
                (await c.Run("deps-audit", "audit", "--min-severity", "severe", "--from", "npm=" + good).ConfigureAwait(false)).Exit(2).Has("unknown severity severe");
                (await c.Run("deps-audit", "audit", "--min-severity").ConfigureAwait(false)).Exit(2).Has("--min-severity needs a value");
                (await c.Run("deps-audit", "audit", "--fix").ConfigureAwait(false)).Exit(2).Has("unknown argument --fix");
            });
            Add("DetectionDryRun", "A dry run lists the auditor for each detected ecosystem from the repository root, with notes for what cannot be audited", async (SkillTestContext c) =>
            {
                Write(c, "package.json", "{}");
                Write(c, "package-lock.json", "{}");
                Write(c, "requirements.txt", "requests==2.25.0");
                Write(c, "Cargo.lock", "");
                Write(c, "go.mod", "module x");
                Write(c, "src/App/App.csproj", "<Project />");
                Directory.CreateDirectory(Path.Combine(c.Project, ".git"));
                (await c.RunIn("src", true, "deps-audit", "audit", "--native").ConfigureAwait(false))
                    .Exit(0).Has("DRYRUN: npm audit --json").Has("DRYRUN: pip-audit -f json -r requirements.txt").Has("DRYRUN: cargo audit --json")
                    .Has("package --vulnerable --include-transitive --format json").Has("App.csproj").Has("DRYRUN: govulncheck -json ./...");
            });
            Add("DetectionChoosesTheRightTool", "pnpm and yarn lockfiles pick their tools, pyproject audits the project, and a manifest without a lockfile is noted", async (SkillTestContext c) =>
            {
                Write(c, "package.json", "{}");
                Write(c, "pnpm-lock.yaml", "");
                Write(c, "pyproject.toml", "[project]");
                (await c.Run(true, "deps-audit", "audit", "--native").ConfigureAwait(false)).Exit(0).Has("DRYRUN: pnpm audit --json").Has("DRYRUN: pip-audit -f json .").Lacks("DRYRUN: npm audit");
                File.Delete(Path.Combine(c.Project, "pnpm-lock.yaml"));
                Write(c, "yarn.lock", "");
                (await c.Run(true, "deps-audit", "audit", "--native").ConfigureAwait(false)).Exit(0).Has("DRYRUN: yarn audit --json");
                File.Delete(Path.Combine(c.Project, "yarn.lock"));
                (await c.Run(true, "deps-audit", "audit", "--native").ConfigureAwait(false)).Exit(0).Has("note: javascript: no lockfile").Lacks("DRYRUN: npm");
            });
            Add("NothingToAudit", "A directory with no manifest exits 2 and names what it looked for", async (SkillTestContext c) =>
            {
                Write(c, "README.md", "# x");
                (await c.Run("deps-audit", "audit").ConfigureAwait(false)).Exit(2).Has("no package manifest or lockfile found").Has("go.mod");
            });
            Add("NoAuditorInstalled", "When the detected ecosystem's auditor is missing, the run explains what to install and exits 2", async (SkillTestContext c) =>
            {
                Write(c, "requirements.txt", "requests==2.25.0");
                (await c.Run("deps-audit", "audit").ConfigureAwait(false)).Exit(2).Has("python: not audited; pip-audit is not installed").Has("pip install pip-audit").Has("no auditor could run");
            }, skip: IsOnPath("pip-audit") || IsOnPath("osv-scanner"), reason: "pip-audit or osv-scanner is installed");
            Add("SbomDryRun", "sbom writes CycloneDX through syft at the repository root, to a relative path only", async (SkillTestContext c) =>
            {
                Write(c, "package.json", "{}");
                (await c.Run(true, "sbom", "write").ConfigureAwait(false)).Exit(0).Has("DRYRUN: syft scan dir:. --output cyclonedx-json=sbom.cdx.json");
                (await c.Run(true, "sbom", "write", "out/bom.json").ConfigureAwait(false)).Exit(0).Has("cyclonedx-json=out/bom.json");
                (await c.Run(true, "sbom", "write", "../bom.json").ConfigureAwait(false)).Exit(2).Has("relative path");
                string rooted = OperatingSystem.IsWindows() ? "C:\\bom.json" : "/tmp/bom.json";
                (await c.Run(true, "sbom", "write", rooted).ConfigureAwait(false)).Exit(2).Has("relative path");
            });
            Add("SbomWithoutSyft", "Without syft, sbom exits 2 with the install hint", async (SkillTestContext c) =>
            {
                Write(c, "package.json", "{}");
                (await c.Run("sbom", "write").ConfigureAwait(false)).Exit(2).Has("'syft' was not found on PATH").Has("github.com/anchore/syft");
            }, skip: IsOnPath("syft"), reason: "syft is installed");

            return new TestSuiteDescriptor(SuiteId, "Dependency audit and SBOM skills", cases);
        }

        #endregion

        #region Private-Methods

        private static int CountOccurrences(string text, string value)
        {
            int count = 0;
            for (int i = text.IndexOf(value, StringComparison.Ordinal); i >= 0; i = text.IndexOf(value, i + value.Length, StringComparison.Ordinal)) count++;
            return count;
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
            string root = Path.Combine(Path.GetTempPath(), "mux-depsaudit-" + Guid.NewGuid().ToString("N"));
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
