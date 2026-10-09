namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Cli.Commands;
    using Mux.Core.Models;
    using Mux.Core.Settings;
    using Mux.Core.Skills;
    using Mux.Core.Skills.Evaluation;
    using Test.Shared.Support;
    using Touchstone.Core;

    /// <summary>
    /// Touchstone suite for the skill selection evaluation: the built-in cases, gating that matches the system prompt's
    /// listing, the ranking floor the default library must hold, the scorer, the collision report, the
    /// <c>mux skill eval</c> verb, and malformed or unsatisfiable cases. Positive and negative cases.
    /// </summary>
    public static class SkillSelectionEvalSuite
    {
        #region Private-Members

        private const string SuiteId = "SkillSelectionEval";

        // The rates the default library reached when the floor was last raised (2026-10-09: 155 of 157 cases,
        // top-1 94%, top-3 99%). Raise them when descriptions improve; never lower them to make a change pass.
        private const double TopThreeFloor = 0.98;
        private const double TopOneFloor = 0.93;

        // Prompts aimed at pack skills, evaluated with every pack installed (2026-10-09: 16 of 16, top-1 94%).
        private const string PackCases = @"[{""id"":""pk-incident"",""prompt"":""we have an outage, help me run the incident response"",""files"":[],""expect"":[""incident-commander""]},{""id"":""pk-jira"",""prompt"":""write a JQL query for open bugs in Jira"",""files"":[],""expect"":[""jira-expert""]},{""id"":""pk-confluence"",""prompt"":""restructure our Confluence space"",""files"":[],""expect"":[""confluence-expert""]},{""id"":""pk-board-deck"",""prompt"":""build the investor update deck for the board"",""files"":[],""expect"":[""board-deck-builder""]},{""id"":""pk-boardroom"",""prompt"":""this pricing decision spans finance and product; deliberate across the C-suite"",""files"":[],""expect"":[""boardroom""]},{""id"":""pk-cto"",""prompt"":""interrogate this architecture plan for scaling risks"",""files"":[],""expect"":[""cto-review""]},{""id"":""pk-gdpr"",""prompt"":""prepare for a GDPR audit"",""files"":[],""expect"":[""gdpr-audit-prep""]},{""id"":""pk-soc2"",""prompt"":""get us ready for SOC 2"",""files"":[],""expect"":[""soc2-audit-prep""]},{""id"":""pk-threat"",""prompt"":""hunt for threats in our telemetry and check these IOCs"",""files"":[],""expect"":[""threat-detection""]},{""id"":""pk-stats"",""prompt"":""is this A/B test result statistically significant?"",""files"":[],""expect"":[""statistical-analyst"",""senior-data-scientist""]},{""id"":""pk-rag"",""prompt"":""design a RAG pipeline and pick a chunking strategy"",""files"":[],""expect"":[""rag-architect""]},{""id"":""pk-llm-cost"",""prompt"":""our LLM API costs are too high"",""files"":[],""expect"":[""llm-cost-optimizer""]},{""id"":""pk-schema"",""prompt"":""design the database schema and ERD for the orders domain"",""files"":[],""expect"":[""database-schema-designer"",""database-designer""]},{""id"":""pk-prd"",""prompt"":""write a PRD and prioritize features with RICE"",""files"":[],""expect"":[""product-manager-toolkit""]},{""id"":""pk-markdown"",""prompt"":""render this markdown document as a styled HTML page"",""files"":[],""expect"":[""md-document""]},{""id"":""pk-devops"",""prompt"":""set up a CI/CD pipeline and infrastructure automation on AWS"",""files"":[],""expect"":[""senior-devops""]}]";

        private static readonly Lazy<List<Skill>> _Defaults = new Lazy<List<Skill>>(LoadDefaults, LazyThreadSafetyMode.ExecutionAndPublication);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Builds the suite descriptor.
        /// </summary>
        /// <returns>The descriptor.</returns>
        public static TestSuiteDescriptor Create()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();
            void Add(string id, string name, Func<CancellationToken, Task> body)
            {
                cases.Add(new TestCaseDescriptor(SuiteId, id, name, body));
            }

            Add("BuiltInCasesAreWellFormed", "The built-in cases load, have unique ids, name real default skills, and cover every default category", (CancellationToken ct) =>
            {
                IReadOnlyList<SkillEvalCase> builtIn = SkillSelectionEvaluator.LoadBuiltInCases();
                MuxAssert.IsTrue(builtIn.Count >= 100, "at least 100 cases (got " + builtIn.Count + ")");
                List<string> duplicates = builtIn.GroupBy(c => c.Id, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
                MuxAssert.AreEqual(0, duplicates.Count, "duplicate ids: " + string.Join(", ", duplicates));

                Dictionary<string, Skill> byName = _Defaults.Value.ToDictionary(s => s.Manifest.Name, StringComparer.OrdinalIgnoreCase);
                HashSet<string> covered = new HashSet<string>(StringComparer.Ordinal);
                foreach (SkillEvalCase evalCase in builtIn)
                {
                    MuxAssert.IsFalse(string.IsNullOrWhiteSpace(evalCase.Prompt), evalCase.Id + " has a prompt");
                    MuxAssert.IsTrue(evalCase.Expect.Count > 0, evalCase.Id + " expects a skill");
                    foreach (string name in evalCase.Expect.Concat(evalCase.Absent))
                    {
                        MuxAssert.IsTrue(byName.ContainsKey(name), evalCase.Id + " names a default skill: " + name);
                    }

                    foreach (string name in evalCase.Expect)
                    {
                        covered.Add(byName[name].Category);
                    }
                }

                List<string> uncovered = _Defaults.Value.Select(s => s.Category).Distinct(StringComparer.Ordinal).Where(c => !covered.Contains(c)).OrderBy(c => c, StringComparer.Ordinal).ToList();
                MuxAssert.AreEqual(0, uncovered.Count, "categories with no case: " + string.Join(", ", uncovered));
                return Task.CompletedTask;
            });

            Add("GatingIsExact", "For every built-in case the expected skills are listed and the absent ones are not", (CancellationToken ct) =>
            {
                SkillEvalReport report = new SkillSelectionEvaluator(_Defaults.Value).Evaluate(SkillSelectionEvaluator.LoadBuiltInCases());
                List<string> failures = report.GatingFailures.Select(Describe).ToList();
                MuxAssert.AreEqual(0, failures.Count, "gating failures:\n" + string.Join("\n", failures));
                return Task.CompletedTask;
            });

            Add("RankingHoldsTheFloor", "The default library keeps its top-1 and top-3 rates at or above the recorded floor", (CancellationToken ct) =>
            {
                SkillEvalReport report = new SkillSelectionEvaluator(_Defaults.Value).Evaluate(SkillSelectionEvaluator.LoadBuiltInCases());
                string misses = string.Join("\n", report.Results.Where(r => !r.RankPassed).Select(Describe));
                MuxAssert.IsTrue(report.TopNRate >= TopThreeFloor, $"top-3 {report.TopNRate:P1} is below the floor {TopThreeFloor:P0}; misses:\n{misses}");
                MuxAssert.IsTrue(report.Top1Rate >= TopOneFloor, $"top-1 {report.Top1Rate:P1} is below the floor {TopOneFloor:P0}");
                MuxAssert.AreEqual(3, report.TopN, "default cutoff");
                return Task.CompletedTask;
            });

            Add("DotnetSkillsOnlyListInDotnetProjects", "dotnet-* skills are listed for a solution or project file and hidden elsewhere", (CancellationToken ct) =>
            {
                SkillSelectionEvaluator evaluator = new SkillSelectionEvaluator(_Defaults.Value);
                foreach (string[] files in new[] { new[] { "App.sln" }, new[] { "src/App/App.csproj" }, new[] { "lib/Lib.fsproj" }, new[] { "global.json" } })
                {
                    SkillEvalCaseResult listed = evaluator.EvaluateCase(new SkillEvalCase { Id = "net", Prompt = "run the .NET tests", Files = files.ToList(), Expect = new List<string> { "dotnet-test" } });
                    MuxAssert.AreEqual(0, listed.NotListed.Count, "listed for " + files[0]);
                }

                foreach (string[] files in new[] { new[] { "go.mod" }, new[] { "package.json" }, Array.Empty<string>() })
                {
                    SkillEvalCaseResult hidden = evaluator.EvaluateCase(new SkillEvalCase { Id = "other", Prompt = "run the tests", Files = files.ToList(), Expect = new List<string> { "loop-until" }, Absent = new List<string> { "dotnet-test", "dotnet-build", "dotnet-format", "dotnet-restore", "dotnet-outdated", "dotnet-pack", "dotnet-publish" } });
                    MuxAssert.AreEqual(0, hidden.WronglyListed.Count, "hidden for [" + string.Join(", ", files) + "]: " + string.Join(", ", hidden.WronglyListed));
                }

                return Task.CompletedTask;
            });

            Add("ScorerTokenizesAndRanks", "Tokens are lowercased, stemmed, and stripped of stop words; name matches rank first; no match never counts", (CancellationToken ct) =>
            {
                MuxAssert.AreEqual("run test", string.Join(" ", SkillSelectionScorer.Tokenize("Running the Tests!")), "stems and stop words");
                MuxAssert.AreEqual("k8s", SkillSelectionScorer.Stem("k8s"), "terms with digits are kept whole");
                MuxAssert.AreEqual("fix", SkillSelectionScorer.Stem("fixes"), "-xes");
                MuxAssert.AreEqual("dependency", SkillSelectionScorer.Stem("dependencies"), "-ies");
                MuxAssert.AreEqual("format", SkillSelectionScorer.Stem("formatted"), "doubled consonant");
                MuxAssert.AreEqual(0, SkillSelectionScorer.Tokenize(null).Count, "null text");
                MuxAssert.AreEqual(0, SkillSelectionScorer.Tokenize("the and of to").Count, "only stop words");

                List<KeyValuePair<string, string>> listing = new List<KeyValuePair<string, string>>
                {
                    new KeyValuePair<string, string>("alpha-build", "Builds things."),
                    new KeyValuePair<string, string>("beta-test", "Runs the tests."),
                    new KeyValuePair<string, string>("gamma-lint", "Checks style and also mentions test once.")
                };
                List<KeyValuePair<string, double>> ranked = SkillSelectionScorer.Rank("run the tests", listing);
                MuxAssert.AreEqual("beta-test", ranked[0].Key, "name and description match ranks first");
                MuxAssert.AreEqual("alpha-build", ranked[2].Key, "no match ranks last");
                MuxAssert.AreEqual(0.0, ranked[2].Value, "no match scores zero");
                MuxAssert.Throws<ArgumentNullException>(() => SkillSelectionScorer.Rank("x", null!), "null listing");

                SkillEvalCaseResult nothing = new SkillSelectionEvaluator(_Defaults.Value).EvaluateCase(new SkillEvalCase { Id = "z", Prompt = "zzqx wvvk", Expect = new List<string> { "go-test" }, Files = new List<string> { "go.mod" } });
                MuxAssert.AreEqual(0, nothing.Rank, "a zero score is not a rank, even at the top of a tie");
                MuxAssert.IsFalse(nothing.Passed, "and the case fails");
                return Task.CompletedTask;
            });

            Add("MalformedCasesAreErrors", "A case with an unknown skill, no prompt, no expectation, or an escaping fixture path is an error, not a pass", (CancellationToken ct) =>
            {
                SkillSelectionEvaluator evaluator = new SkillSelectionEvaluator(_Defaults.Value);
                SkillEvalCaseResult unknown = evaluator.EvaluateCase(new SkillEvalCase { Id = "u", Prompt = "run the tests", Expect = new List<string> { "no-such-skill" } });
                MuxAssert.Contains("unknown skill 'no-such-skill'", unknown.Error, "unknown expected skill");
                SkillEvalCaseResult unknownAbsent = evaluator.EvaluateCase(new SkillEvalCase { Id = "u2", Prompt = "run the tests", Expect = new List<string> { "loop-until" }, Absent = new List<string> { "gone" } });
                MuxAssert.Contains("unknown skill 'gone'", unknownAbsent.Error, "unknown absent skill");
                MuxAssert.Contains("no prompt", evaluator.EvaluateCase(new SkillEvalCase { Id = "p", Prompt = "  ", Expect = new List<string> { "loop-until" } }).Error, "blank prompt");
                MuxAssert.Contains("expects no skill", evaluator.EvaluateCase(new SkillEvalCase { Id = "e", Prompt = "x" }).Error, "no expectation");
                MuxAssert.Contains("must be relative", evaluator.EvaluateCase(new SkillEvalCase { Id = "f", Prompt = "x", Expect = new List<string> { "loop-until" }, Files = new List<string> { "../outside.txt" } }).Error, "parent escape");
                string rooted = OperatingSystem.IsWindows() ? "C:\\x.txt" : "/tmp/x.txt";
                MuxAssert.Contains("must be relative", evaluator.EvaluateCase(new SkillEvalCase { Id = "f2", Prompt = "x", Expect = new List<string> { "loop-until" }, Files = new List<string> { rooted } }).Error, "rooted path");
                MuxAssert.IsFalse(unknown.Passed, "an error never passes");

                SkillEvalReport report = evaluator.Evaluate(new[] { unknown.Case, new SkillEvalCase { Id = "ok", Prompt = "retry the command until it succeeds", Expect = new List<string> { "loop-until" } } });
                MuxAssert.AreEqual(1, report.GatingFailures.Count, "the error counts as a gating failure");
                MuxAssert.AreEqual(1.0, report.TopNRate, "errors are left out of the rates");

                MuxAssert.Throws<ArgumentException>(() => SkillSelectionEvaluator.ParseCases(" "), "blank JSON");
                MuxAssert.Throws<JsonException>(() => SkillSelectionEvaluator.ParseCases("{\"id\":\"x\"}"), "an object instead of an array");
                MuxAssert.Throws<JsonException>(() => SkillSelectionEvaluator.ParseCases("[{"), "broken JSON");
                MuxAssert.Throws<ArgumentNullException>(() => evaluator.Evaluate(null!), "null cases");
                MuxAssert.Throws<ArgumentNullException>(() => new SkillSelectionEvaluator(null!), "null skills");
                return Task.CompletedTask;
            });

            Add("FixturesGateSkills", "A fixture without the skill's project files leaves it unlisted, and an absent skill that is listed fails the case", (CancellationToken ct) =>
            {
                SkillSelectionEvaluator evaluator = new SkillSelectionEvaluator(_Defaults.Value);
                SkillEvalCaseResult noModule = evaluator.EvaluateCase(new SkillEvalCase { Id = "g", Prompt = "run the go tests", Expect = new List<string> { "go-test" } });
                MuxAssert.Contains("go-test", string.Join(",", noModule.NotListed), "go-test needs go.mod");
                MuxAssert.IsFalse(noModule.Passed, "not listed fails");

                SkillEvalCaseResult wrong = evaluator.EvaluateCase(new SkillEvalCase { Id = "w", Prompt = "run the go tests", Files = new List<string> { "go.mod", "package.json" }, Expect = new List<string> { "go-test" }, Absent = new List<string> { "js-test" } });
                MuxAssert.Contains("js-test", string.Join(",", wrong.WronglyListed), "js-test is listed for package.json");
                MuxAssert.IsFalse(wrong.Passed, "wrongly listed fails even with a top rank");
                MuxAssert.AreEqual(1, wrong.Rank, "the rank is still reported");

                SkillEvalCaseResult nested = evaluator.EvaluateCase(new SkillEvalCase { Id = "n", Prompt = "upgrade the helm release", Files = new List<string> { "charts/api/Chart.yaml", "docs/" }, Expect = new List<string> { "helm" } });
                MuxAssert.IsTrue(nested.Passed, "nested fixture files and directories are created: " + Describe(nested));
                return Task.CompletedTask;
            });

            Add("ToolPresenceGatesListing", "requiresTools gates the listing through the supplied check; the default treats every tool as installed", (CancellationToken ct) =>
            {
                SkillEvalCase whoami = new SkillEvalCase { Id = "a", Prompt = "which AWS account am I logged into?", Expect = new List<string> { "aws-whoami" } };
                MuxAssert.IsTrue(new SkillSelectionEvaluator(_Defaults.Value).EvaluateCase(whoami).Passed, "listed when tools count as installed");
                SkillEvalCaseResult missing = new SkillSelectionEvaluator(_Defaults.Value, (IReadOnlyList<string> tools) => tools.Count == 0).EvaluateCase(whoami);
                MuxAssert.Contains("aws-whoami", string.Join(",", missing.NotListed), "hidden without the aws CLI");
                List<Skill> listed = new SkillSelectionEvaluator(_Defaults.Value, (IReadOnlyList<string> tools) => tools.Count == 0).ListFor(null);
                MuxAssert.IsFalse(listed.Any(s => s.Manifest.RequiresTools.Count > 0), "no tool-gated skill is listed");
                MuxAssert.IsTrue(listed.Any(s => s.Manifest.Name == "go-test"), "a null root skips the appliesTo check");
                return Task.CompletedTask;
            });

            Add("CollisionsAreReported", "Near-duplicate descriptions are reported most similar first, and none of the defaults are identical", (CancellationToken ct) =>
            {
                SkillSelectionEvaluator evaluator = new SkillSelectionEvaluator(_Defaults.Value);
                List<SkillCollision> similar = evaluator.FindCollisions(0.8);
                MuxAssert.IsTrue(similar.Any(c => c.First == "aws-storage" && c.Second == "gcp-storage"), "the storage pair is reported");
                for (int i = 1; i < similar.Count; i++)
                {
                    MuxAssert.IsTrue(similar[i - 1].Similarity >= similar[i].Similarity, "sorted by similarity");
                }

                List<SkillCollision> identical = evaluator.FindCollisions(0.95);
                MuxAssert.AreEqual(0, identical.Count, "near-identical descriptions: " + string.Join(", ", identical.Select(c => c.First + "~" + c.Second)));
                MuxAssert.AreEqual(0, evaluator.FindCollisions(1.01).Count, "nothing exceeds 1");
                return Task.CompletedTask;
            });

            Add("CliEval", "mux skill eval reports the run in text and JSON, filters to one case, reads a case file, and rejects bad input", (CancellationToken ct) => WithConfigAsync(async (string config) =>
            {
                CliRun text = await RunAsync(new SkillSettings { Action = "eval", ConfigDir = config }, ct).ConfigureAwait(false);
                MuxAssert.Contains("cases passed. Top-1", text.Out, "summary line: " + text.Err);
                MuxAssert.Contains("0 gating failures", text.Out, "gating summary");

                CliRun one = await RunAsync(new SkillSettings { Action = "eval", Name = "go-run-tests", ConfigDir = config }, ct).ConfigureAwait(false);
                MuxAssert.AreEqual(0, one.Code, "one passing case exits 0: " + one.Out + one.Err);
                MuxAssert.Contains("PASS  go-run-tests", one.Out, "a single case is always shown");
                MuxAssert.Contains("top: go-test", one.Out, "its ranking");

                CliRun json = await RunAsync(new SkillSettings { Action = "eval", Name = "go-run-tests", ConfigDir = config, OutputFormat = "json", Top = 1 }, ct).ConfigureAwait(false);
                using (JsonDocument document = JsonDocument.Parse(json.Out))
                {
                    JsonElement root = document.RootElement;
                    MuxAssert.AreEqual(1, root.GetProperty("cases").GetInt32(), "one case");
                    MuxAssert.AreEqual(1, root.GetProperty("top").GetInt32(), "--top applied");
                    MuxAssert.AreEqual("go-test", root.GetProperty("results")[0].GetProperty("matched").GetString(), "matched skill");
                }

                string file = Path.Combine(config, "cases.json");
                File.WriteAllText(file, "[{\"id\":\"mine\",\"prompt\":\"zzqx\",\"expect\":[\"loop-until\"]}]");
                CliRun custom = await RunAsync(new SkillSettings { Action = "eval", ConfigDir = config, CasesFile = file }, ct).ConfigureAwait(false);
                MuxAssert.AreEqual(1, custom.Code, "a failing case exits 1");
                MuxAssert.Contains("FAIL  mine", custom.Out, "the custom case ran");

                CliRun missingCase = await RunAsync(new SkillSettings { Action = "eval", Name = "nope", ConfigDir = config }, ct).ConfigureAwait(false);
                MuxAssert.AreEqual(1, missingCase.Code, "unknown case id");
                MuxAssert.Contains("No evaluation case has the id 'nope'", missingCase.Err, "unknown case message");

                File.WriteAllText(file, "not json");
                CliRun badFile = await RunAsync(new SkillSettings { Action = "eval", ConfigDir = config, CasesFile = file }, ct).ConfigureAwait(false);
                MuxAssert.AreEqual(1, badFile.Code, "bad case file");
                MuxAssert.Contains("Could not read the evaluation cases", badFile.Err, "bad file message");

                CliRun noFile = await RunAsync(new SkillSettings { Action = "eval", ConfigDir = config, CasesFile = Path.Combine(config, "missing.json") }, ct).ConfigureAwait(false);
                MuxAssert.AreEqual(1, noFile.Code, "missing case file");
            }));

            Add("ModelChooserReadsTheFirstSkillCall", "The live chooser sends the listing and skill tools, and reads the first skill or run_skill call", (CancellationToken ct) =>
            {
                MuxAssert.AreEqual("go-test", SkillModelChooser.ReadChoice(Reply(("run_skill", "{\"name\":\"go-test\",\"command\":\"all\"}"))), "run_skill");
                MuxAssert.AreEqual("go-test", SkillModelChooser.ReadChoice(Reply(("skill", "{\"name\":\"go-test\"}"))), "skill");
                MuxAssert.AreEqual("go-lint", SkillModelChooser.ReadChoice(Reply(("read_file", "{\"path\":\"x\"}"), ("skill", "{\"name\":\"list\"}"), ("skill", "{\"name\":\"go-lint\"}"))), "other tools and list are skipped");
                MuxAssert.IsNull(SkillModelChooser.ReadChoice(Reply(("skill", "not json"))), "malformed arguments");
                MuxAssert.IsNull(SkillModelChooser.ReadChoice(Reply(("skill", "{\"name\":7}"))), "a non-string name");
                MuxAssert.IsNull(SkillModelChooser.ReadChoice(new ConversationMessage { Role = Mux.Core.Enums.RoleEnum.Assistant, Content = "I would run go test." }), "text only");
                MuxAssert.IsNull(SkillModelChooser.ReadChoice(null), "no reply");

                List<Skill> listed = new SkillSelectionEvaluator(_Defaults.Value).ListFor(null).Where(sk => sk.Manifest.Name.StartsWith("go-", StringComparison.Ordinal)).ToList();
                string system = SkillModelChooser.BuildSystemPrompt(listed);
                MuxAssert.Contains("- go-test: " + listed.First(sk => sk.Manifest.Name == "go-test").Manifest.Description, system, "one listing line per skill");
                MuxAssert.DoesNotContain("- py-test:", system, "only the listed skills");
                List<string> tools = SkillModelChooser.BuildTools(listed).Select(t => t.Name).ToList();
                MuxAssert.AreEqual("skill,run_skill", string.Join(",", tools), "the real skill tools");
                MuxAssert.AreEqual(0, SkillModelChooser.BuildTools(new List<Skill>()).Count, "no tools without skills");
                MuxAssert.Throws<ArgumentNullException>(() => new SkillModelChooser(null!), "null send");
                return Task.CompletedTask;
            });

            Add("LiveEvaluationScoresTheModelsPick", "A live run passes when the model picks an expected skill and fails when it picks another, an unlisted one, or none", async (CancellationToken ct) =>
            {
                List<string> systems = new List<string>();
                SkillModelChooser chooser = new SkillModelChooser((List<ConversationMessage> messages, List<ToolDefinition> tools, CancellationToken token) =>
                {
                    systems.Add(messages[0].Content ?? string.Empty);
                    MuxAssert.AreEqual(2, tools.Count, "skill tools offered");
                    string prompt = messages[1].Content ?? string.Empty;
                    ConversationMessage reply = prompt.Contains("go tests", StringComparison.Ordinal) ? Reply(("run_skill", "{\"name\":\"go-test\",\"command\":\"all\"}"))
                        : prompt.Contains("lint", StringComparison.Ordinal) ? Reply(("skill", "{\"name\":\"go-build\"}"))
                        : prompt.Contains("pytest", StringComparison.Ordinal) ? Reply(("skill", "{\"name\":\"py-test\"}"))
                        : new ConversationMessage { Role = Mux.Core.Enums.RoleEnum.Assistant, Content = "No skill." };
                    return Task.FromResult(reply);
                });

                List<string> go = new List<string> { "go.mod" };
                List<SkillEvalCase> cases = new List<SkillEvalCase>
                {
                    new SkillEvalCase { Id = "right", Prompt = "run the go tests", Files = go, Expect = new List<string> { "go-test" } },
                    new SkillEvalCase { Id = "wrong", Prompt = "lint the go code", Files = go, Expect = new List<string> { "go-lint" } },
                    new SkillEvalCase { Id = "unlisted", Prompt = "run pytest", Files = go, Expect = new List<string> { "go-test" } },
                    new SkillEvalCase { Id = "none", Prompt = "hello", Files = go, Expect = new List<string> { "go-test" } }
                };
                SkillSelectionEvaluator evaluator = new SkillSelectionEvaluator(_Defaults.Value) { TopN = 1 };
                SkillEvalReport report = await evaluator.EvaluateAsync(cases, chooser.ChooseAsync, ct).ConfigureAwait(false);
                MuxAssert.IsTrue(report.Results[0].Passed, "the expected pick passes");
                MuxAssert.AreEqual("go-build", string.Join(",", report.Results[1].Top), "the wrong pick is recorded");
                MuxAssert.IsFalse(report.Results[1].Passed, "the wrong pick fails");
                MuxAssert.AreEqual(0, report.Results[2].Top.Count, "a skill that is not listed for the project is ignored");
                MuxAssert.IsFalse(report.Results[3].Passed, "no pick fails");
                MuxAssert.AreEqual(0.25, report.Top1Rate, "one of four");
                MuxAssert.IsTrue(systems.All(sys => sys.Contains("- go-test:", StringComparison.Ordinal) && !sys.Contains("- py-test:", StringComparison.Ordinal)), "each call carried the project's listing");
                MuxAssert.Throws<ArgumentNullException>(() => evaluator.EvaluateAsync(cases, null!, ct).GetAwaiter().GetResult(), "null chooser");
            });

            Add("CliEvalLive", "mux skill eval --live asks the configured endpoint, and rejects an unknown endpoint, --top, and a dead server", (CancellationToken ct) => WithConfigAsync(async (string config) =>
            {
                using (MockHttpServer server = new MockHttpServer())
                using (System.Net.HttpListener slowListener = StartSilentListener(out string slowUrl))
                {
                    var slowServer = new { BaseUrl = slowUrl };
                    server.RegisterResponse("run the go tests", "{\"id\":\"e1\",\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":null,\"tool_calls\":[{\"id\":\"c1\",\"type\":\"function\",\"function\":{\"name\":\"run_skill\",\"arguments\":\"{\\\"name\\\":\\\"go-test\\\",\\\"command\\\":\\\"all\\\"}\"}}]},\"finish_reason\":\"tool_calls\",\"index\":0}]}");
                    server.Start();
                    SettingsLoader.EnsureConfigDirectory();
                    File.WriteAllText(Path.Combine(config, "endpoints.json"), "{\"endpoints\":[{\"name\":\"mock\",\"adapterType\":\"openai_compatible\",\"baseUrl\":\"" + server.BaseUrl + "\",\"model\":\"test-model\",\"isDefault\":true,\"timeoutMs\":5000},{\"name\":\"dead\",\"adapterType\":\"openai_compatible\",\"baseUrl\":\"http://127.0.0.1:9\",\"model\":\"m\",\"timeoutMs\":2000},{\"name\":\"slow\",\"adapterType\":\"openai_compatible\",\"baseUrl\":\"" + slowServer.BaseUrl + "\",\"model\":\"m\",\"timeoutMs\":1000}]}");

                    CliRun live = await RunAsync(new SkillSettings { Action = "eval", Name = "go-run-tests", ConfigDir = config, Live = true }, ct).ConfigureAwait(false);
                    MuxAssert.AreEqual(0, live.Code, "the model picked go-test: " + live.Out + live.Err);
                    MuxAssert.Contains("The model (mock) chose an expected skill 100%", live.Out, "live summary");
                    MuxAssert.IsTrue(server.ReceivedRequests.Any(r => r.Contains("- go-test:", StringComparison.Ordinal) && r.Contains("run_skill", StringComparison.Ordinal)), "the request carried the listing and the tools");

                    CliRun json = await RunAsync(new SkillSettings { Action = "eval", Name = "go-run-tests", ConfigDir = config, Live = true, Endpoint = "mock", OutputFormat = "json" }, ct).ConfigureAwait(false);
                    MuxAssert.Contains("\"mode\": \"live:mock\"", json.Out.Replace("\"mode\":\"", "\"mode\": \""), "mode reported");

                    CliRun unknown = await RunAsync(new SkillSettings { Action = "eval", ConfigDir = config, Live = true, Endpoint = "nope" }, ct).ConfigureAwait(false);
                    MuxAssert.AreEqual(1, unknown.Code, "unknown endpoint");
                    MuxAssert.Contains("No endpoint named 'nope'", unknown.Err, "unknown endpoint message");

                    CliRun withTop = await RunAsync(new SkillSettings { Action = "eval", ConfigDir = config, Live = true, Top = 3 }, ct).ConfigureAwait(false);
                    MuxAssert.AreEqual(1, withTop.Code, "--top with --live");
                    MuxAssert.Contains("--top does not apply to --live", withTop.Err, "--top message");

                    CliRun slow = await RunAsync(new SkillSettings { Action = "eval", Name = "go-run-tests", ConfigDir = config, Live = true, Endpoint = "slow" }, ct).ConfigureAwait(false);
                    MuxAssert.AreEqual(1, slow.Code, "a timed-out call fails its case: " + slow.Out + slow.Err);
                    MuxAssert.Contains("go-run-tests: the model call failed", slow.Err, "the timeout is reported per case, not as a crash");
                    MuxAssert.Contains("0 of 1 cases passed", slow.Out, "the run still finishes with a summary");

                    CliRun dead = await RunAsync(new SkillSettings { Action = "eval", Name = "go-run-tests", ConfigDir = config, Live = true, Endpoint = "dead" }, ct).ConfigureAwait(false);
                    MuxAssert.AreEqual(1, dead.Code, "a dead server fails the case");
                    MuxAssert.Contains("go-run-tests: the model call failed", dead.Err, "the failure is reported per case");
                }
            }));

            Add("CliParsesEvalOptions", "The mux skill parser accepts eval's options and rejects a bad --top", (CancellationToken ct) =>
            {
                SkillSettings parsed = CliArgumentParser.ParseSkill(new[] { "eval", "go-run-tests", "--top", "5", "--cases", "c.json", "--live", "--endpoint=mock" });
                MuxAssert.AreEqual("eval", parsed.Action, "action");
                MuxAssert.AreEqual("go-run-tests", parsed.Name, "case id");
                MuxAssert.AreEqual(5, parsed.Top ?? 0, "--top");
                MuxAssert.AreEqual("c.json", parsed.CasesFile, "--cases");
                MuxAssert.IsTrue(parsed.Live, "--live");
                MuxAssert.AreEqual("mock", parsed.Endpoint, "--endpoint=");
                MuxAssert.Throws<InvalidOperationException>(() => CliArgumentParser.ParseSkill(new[] { "eval", "--top", "zero" }), "non-numeric --top");
                MuxAssert.Throws<InvalidOperationException>(() => CliArgumentParser.ParseSkill(new[] { "eval", "--top", "0" }), "--top below 1");
                MuxAssert.Throws<InvalidOperationException>(() => CliArgumentParser.ParseSkill(new[] { "eval", "--top" }), "--top without a value");
                return Task.CompletedTask;
            });

            Add("PackSkillsWithPacksInstalled", "With every pack installed, pack skills are listed and chosen for the prompts they serve", (CancellationToken ct) =>
            {
                string root = Path.Combine(Path.GetTempPath(), "mux-skilleval-packs-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(root);
                try
                {
                    DefaultSkillLibrary.SeedInto(root);
                    Mux.Core.Skills.Packaging.SkillPackInstaller installer = new Mux.Core.Skills.Packaging.SkillPackInstaller(root);
                    foreach (Mux.Core.Skills.Packaging.SkillPack pack in installer.Catalog.Packs) installer.Install(pack.Id);
                    List<Skill> all = new List<Skill>(new SkillLoader(root).Discover());
                    MuxAssert.IsTrue(all.Count > 300, "defaults and packs loaded (" + all.Count + ")");
                    SkillEvalReport report = new SkillSelectionEvaluator(all).Evaluate(SkillSelectionEvaluator.ParseCases(PackCases));
                    string misses = string.Join("\n", report.Results.Where(r => !r.Passed).Select(Describe));
                    MuxAssert.AreEqual(0, report.GatingFailures.Count, "gating:\n" + misses);
                    MuxAssert.AreEqual(1.0, report.TopNRate, "every pack case in the top three:\n" + misses);
                    MuxAssert.IsTrue(report.Top1Rate >= 0.9, $"top-1 {report.Top1Rate:P1} is below 90%");
                }
                finally
                {
                    try { Directory.Delete(root, true); } catch (Exception) { }
                }

                return Task.CompletedTask;
            });

            return new TestSuiteDescriptor(SuiteId, "Skill selection evaluation: cases, gating, ranking floor, scorer, CLI", cases);
        }

        #endregion

        #region Private-Methods

        private static List<Skill> LoadDefaults()
        {
            string root = Path.Combine(Path.GetTempPath(), "mux-skilleval-defaults-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                DefaultSkillLibrary.SeedInto(root);
                List<Skill> skills = new List<Skill>(new SkillLoader(root).Discover());
                SkillCategories.ApplyOverrides(skills);
                return skills;
            }
            finally
            {
                try { Directory.Delete(root, true); } catch (Exception) { }
            }
        }

        // A listener that accepts requests and never answers, so a client with a short timeout gives up.
        private static System.Net.HttpListener StartSilentListener(out string baseUrl)
        {
            int port = StubHttpServer.FreeLoopbackPort();
            baseUrl = "http://127.0.0.1:" + port;
            System.Net.HttpListener listener = new System.Net.HttpListener();
            listener.Prefixes.Add(baseUrl + "/");
            listener.Start();
            _ = Task.Run(async () =>
            {
                List<System.Net.HttpListenerContext> held = new List<System.Net.HttpListenerContext>();
                while (listener.IsListening)
                {
                    try { held.Add(await listener.GetContextAsync().ConfigureAwait(false)); }
                    catch (Exception) { break; }
                }
            });
            return listener;
        }

        private static ConversationMessage Reply(params (string Name, string Arguments)[] calls)
        {
            return new ConversationMessage
            {
                Role = Mux.Core.Enums.RoleEnum.Assistant,
                ToolCalls = calls.Select((call, i) => new ToolCall { Id = "c" + i, Name = call.Name, Arguments = call.Arguments }).ToList()
            };
        }

        private static string Describe(SkillEvalCaseResult result)
        {
            string detail = !string.IsNullOrEmpty(result.Error)
                ? "error: " + result.Error
                : $"rank {result.Rank}, not listed [{string.Join(", ", result.NotListed)}], wrongly listed [{string.Join(", ", result.WronglyListed)}], top [{string.Join(", ", result.Top)}]";
            return "  " + result.Case.Id + " (\"" + result.Case.Prompt + "\"): " + detail;
        }

        private static async Task<CliRun> RunAsync(SkillSettings settings, CancellationToken ct)
        {
            TextWriter originalOut = Console.Out;
            TextWriter originalError = Console.Error;
            StringWriter stdout = new StringWriter();
            StringWriter stderr = new StringWriter();
            Console.SetOut(stdout);
            Console.SetError(stderr);
            try
            {
                int code = await new Mux.Cli.Commands.SkillCommand().ExecuteAsync(new CommandContext("skill", Array.Empty<string>()), settings, ct).ConfigureAwait(false);
                return new CliRun { Code = code, Out = stdout.ToString(), Err = stderr.ToString() };
            }
            finally
            {
                Console.SetOut(originalOut);
                Console.SetError(originalError);
            }
        }

        private static async Task WithConfigAsync(Func<string, Task> body)
        {
            string config = Path.Combine(Path.GetTempPath(), "mux-skilleval-cfg-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(config);
            try
            {
                using (SettingsLoader.PushConfigDirectoryOverride(config))
                {
                    await body(config).ConfigureAwait(false);
                }
            }
            finally
            {
                try { Directory.Delete(config, true); } catch (Exception) { }
            }
        }

        #endregion
    }
}
