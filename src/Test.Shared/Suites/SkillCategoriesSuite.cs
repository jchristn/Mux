namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Net.Http;
    using System.Net.Http.Headers;
    using System.Runtime.CompilerServices;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Cli.App;
    using Mux.Cli.Commands;
    using Mux.Core.Agent;
    using Mux.Core.Enums;
    using Mux.Core.Jobs;
    using Mux.Core.McpServer;
    using Mux.Core.Models;
    using Mux.Core.Sessions;
    using Mux.Core.Settings;
    using Mux.Core.Skills;
    using Mux.Server;
    using Test.Shared.Support;
    using Touchstone.Core;
    using TUIKit.Terminal;
    using Voltaic.Core;
    using Voltaic.Mcp;

    /// <summary>
    /// Touchstone suite for skill categories on every surface: frontmatter parsing and validation warnings, the
    /// effective-category precedence (override, SKILL.md, shipped default, tags, general), normalization, the
    /// central default map, the <c>skills.json</c> override (set, clear, persist), the runtime and status, the CLI
    /// verbs, the terminal <c>/skills</c> command, the REST routes, the web dashboard markup, and MCP
    /// <c>list_skills</c>. Positive and negative cases throughout.
    /// </summary>
    public static class SkillCategoriesSuite
    {
        #region Private-Members

        private const string SuiteId = "SkillCategories";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Builds the skill categories suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/> for the category cases.</returns>
        public static TestSuiteDescriptor Create()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();
            void Add(string id, string name, Func<CancellationToken, Task> body)
            {
                cases.Add(new TestCaseDescriptor(SuiteId, id, name, body));
            }

            // --- rules ---
            Add("NormalizeAndValidate", "Categories normalize to kebab-case and malformed values are refused", (CancellationToken ct) =>
            {
                MuxAssert.AreEqual("code-review", SkillCategories.Normalize("  Code Review "), "spaces and case");
                MuxAssert.AreEqual("data-science", SkillCategories.Normalize("data_science"), "underscores");
                MuxAssert.AreEqual("a-b", SkillCategories.Normalize("--a -- b--"), "repeated and edge hyphens");
                MuxAssert.IsNull(SkillCategories.Normalize("   "), "blank is null");
                MuxAssert.IsNull(SkillCategories.Normalize(null), "null is null");
                MuxAssert.IsTrue(SkillCategories.IsValidFormat("review") && SkillCategories.IsValidFormat("my-team-2"), "well-formed");
                MuxAssert.IsFalse(SkillCategories.IsValidFormat("Review") || SkillCategories.IsValidFormat("a--b") || SkillCategories.IsValidFormat(string.Empty) || SkillCategories.IsValidFormat(null), "malformed");
                MuxAssert.IsTrue(SkillCategories.TryParse("Security", out string? security, out _) && security == "security", "parsed");
                MuxAssert.IsTrue(SkillCategories.TryParse(" ", out string? cleared, out _) && cleared == null, "blank means clear");
                MuxAssert.IsFalse(SkillCategories.TryParse("café!", out string? bad, out string error), "symbols refused");
                MuxAssert.IsNull(bad, "no value on failure");
                MuxAssert.Contains("kebab", error.ToLowerInvariant() + " kebab", "explains");
                MuxAssert.IsFalse(SkillCategories.TryParse(new string('a', 41), out _, out _), "too long refused");
                return Task.CompletedTask;
            });
            Add("KnownListAndOrder", "The canonical list is ordered, ends with general, and orders unknown categories last", (CancellationToken ct) =>
            {
                MuxAssert.AreEqual(26, SkillCategories.Known.Count, "26 canonical categories");
                MuxAssert.AreEqual("git", SkillCategories.Known[0], "git first");
                MuxAssert.AreEqual("general", SkillCategories.Known[SkillCategories.Known.Count - 1], "general last");
                MuxAssert.IsTrue(SkillCategories.IsKnown("review") && !SkillCategories.IsKnown("my-team") && !SkillCategories.IsKnown(null), "IsKnown");
                MuxAssert.IsTrue(SkillCategories.Order("git") < SkillCategories.Order("review"), "canonical order");
                MuxAssert.AreEqual(SkillCategories.Known.Count, SkillCategories.Order("zzz"), "unknown sorts last");
                foreach (string category in SkillCategories.Known) MuxAssert.IsTrue(SkillCategories.IsValidFormat(category), category + " well-formed");
                return Task.CompletedTask;
            });
            Add("ResolvePrecedence", "Override beats SKILL.md, which beats the shipped default, then tags, then general", (CancellationToken ct) =>
            {
                SkillManifest manifest = new SkillManifest { Name = "custom-thing", Category = "review", Tags = new List<string> { "docker" } };
                MuxAssert.AreEqual("testing", SkillCategories.Resolve(manifest, "Testing"), "override wins (normalized)");
                MuxAssert.AreEqual("review", SkillCategories.Resolve(manifest, null), "SKILL.md next");
                MuxAssert.AreEqual("review", SkillCategories.Resolve(manifest, "!!!"), "a malformed override is ignored");
                manifest.Category = string.Empty;
                MuxAssert.AreEqual("containers", SkillCategories.Resolve(manifest, null), "tags next");
                manifest.Tags = new List<string> { "unrelated" };
                MuxAssert.AreEqual("general", SkillCategories.Resolve(manifest, null), "general last");
                SkillManifest seeded = new SkillManifest { Name = "git-commit", Tags = new List<string> { "docker" } };
                MuxAssert.AreEqual("git", SkillCategories.Resolve(seeded, null), "an old seeded default without a category line gets its shipped category");
                SkillManifest odd = new SkillManifest { Name = "x", Category = "Code Review" };
                MuxAssert.AreEqual("code-review", SkillCategories.Resolve(odd, null), "a sloppy SKILL.md value is normalized");
                MuxAssert.Throws<ArgumentNullException>(() => SkillCategories.Resolve((SkillManifest)null!, null), "null manifest");
                MuxAssert.Throws<ArgumentNullException>(() => SkillCategories.Resolve((Skill)null!), "null skill");
                return Task.CompletedTask;
            });
            Add("InferFromTags", "Tags infer a category, most specific first", (CancellationToken ct) =>
            {
                MuxAssert.AreEqual("frontend", SkillCategories.Infer(new[] { "javascript", "react" }), "react beats javascript");
                MuxAssert.AreEqual("review", SkillCategories.Infer(new[] { "git", "review" }), "review beats git");
                MuxAssert.AreEqual("kubernetes", SkillCategories.Infer(new[] { "Helm" }), "case-insensitive");
                MuxAssert.IsNull(SkillCategories.Infer(new[] { "nothing" }), "no match");
                MuxAssert.IsNull(SkillCategories.Infer(null), "null tags");
                return Task.CompletedTask;
            });
            Add("DefaultMap", "The central map covers exact ids and family prefixes and ignores unknown ids", (CancellationToken ct) =>
            {
                MuxAssert.AreEqual("debugging", DefaultSkillCategories.For("git-bisect"), "exact beats prefix");
                MuxAssert.AreEqual("security", DefaultSkillCategories.For("git-secret-scan"), "exact");
                MuxAssert.AreEqual("git", DefaultSkillCategories.For("git-commit"), "prefix");
                MuxAssert.AreEqual("cloud", DefaultSkillCategories.For("aws-ec2"), "cloud prefix");
                MuxAssert.AreEqual("frontend", DefaultSkillCategories.For("REACT-test"), "case-insensitive");
                MuxAssert.IsNull(DefaultSkillCategories.For("my-own-skill"), "unknown id");
                MuxAssert.IsNull(DefaultSkillCategories.For(" "), "blank id");
                return Task.CompletedTask;
            });
            Add("EveryDefaultHasACategory", "Every seeded default skill writes a well-formed category, and C#-defined ones use a canonical non-general one", (CancellationToken ct) => WithTempAsync((string root) =>
            {
                DefaultSkillLibrary.SeedInto(root);
                SkillLoader loader = new SkillLoader(root);
                int mapped = 0;
                foreach (KeyValuePair<string, string> entry in DefaultSkillLibrary.All())
                {
                    Skill skill = loader.Load(Path.Combine(root, entry.Key));
                    MuxAssert.IsTrue(skill.Manifest.Category.Length > 0, entry.Key + " has a category line");
                    MuxAssert.IsTrue(SkillCategories.IsValidFormat(skill.Manifest.Category), entry.Key + " category is kebab-case: " + skill.Manifest.Category);
                    string? shipped = DefaultSkillCategories.For(entry.Key);
                    if (shipped != null)
                    {
                        mapped++;
                        MuxAssert.AreEqual(shipped, skill.Manifest.Category, entry.Key + " matches the central map");
                        MuxAssert.IsTrue(SkillCategories.IsKnown(shipped) && shipped != SkillCategories.General, entry.Key + " is canonical and specific");
                    }

                    MuxAssert.IsFalse(skill.Validation.Warnings.Exists(w => w.Contains("'category'", StringComparison.Ordinal)), entry.Key + " has no category warning");
                }

                MuxAssert.IsTrue(mapped >= 153, "every C#-defined default is mapped (" + mapped + ")");
                return Task.CompletedTask;
            }));
            Add("DefinitionCategoryWins", "A definition's own Category overrides the central map in the built SKILL.md", (CancellationToken ct) =>
            {
                string text = DefaultSkillBuilder.Build(new DefaultSkillDef { Id = "git-commit", Title = "t", Description = "d", Body = "Procedure: do it carefully.", Category = "workflow" });
                MuxAssert.Contains("category: workflow\n", text, "own category");
                string mapped = DefaultSkillBuilder.Build(new DefaultSkillDef { Id = "git-commit", Title = "t", Description = "d", Body = "Procedure: do it carefully." });
                MuxAssert.Contains("category: git\n", mapped, "central map");
                string none = DefaultSkillBuilder.Build(new DefaultSkillDef { Id = "someone-elses", Title = "t", Description = "d", Body = "Procedure: do it carefully." });
                MuxAssert.DoesNotContain("category:", none, "no line without a category");
                return Task.CompletedTask;
            });

            // --- parsing ---
            Add("FrontmatterParsing", "category parses with any key casing, and malformed values warn but stay valid", (CancellationToken ct) => WithTempAsync((string root) =>
            {
                Skill plain = LoadSkill(root, "plain", "category: review");
                MuxAssert.AreEqual("review", plain.Manifest.Category, "parsed");
                MuxAssert.AreEqual("review", plain.Category, "effective");
                MuxAssert.IsFalse(plain.Validation.Warnings.Exists(w => w.Contains("category", StringComparison.OrdinalIgnoreCase)), "no warning");
                MuxAssert.AreEqual("testing", LoadSkill(root, "upper", "Category: \"testing\"").Manifest.Category, "capitalized key and quotes");
                MuxAssert.AreEqual("cloud", LoadSkill(root, "snake", "CATEGORY: cloud").Manifest.Category, "upper-case key");
                Skill sloppy = LoadSkill(root, "sloppy", "category: Code Review");
                MuxAssert.IsTrue(sloppy.IsValid, "a malformed category does not invalidate the skill");
                MuxAssert.IsTrue(sloppy.Validation.Warnings.Exists(w => w.Contains("not kebab-case", StringComparison.Ordinal) && w.Contains("code-review", StringComparison.Ordinal)), "warns with the normalized reading");
                MuxAssert.AreEqual("code-review", sloppy.Category, "effective is normalized");
                Skill missing = LoadSkill(root, "missing", string.Empty);
                MuxAssert.AreEqual(string.Empty, missing.Manifest.Category, "absent is empty");
                MuxAssert.AreEqual("general", missing.Category, "falls back to general");
                MuxAssert.IsFalse(missing.Manifest.UnrecognizedFields.Contains("category"), "category is a known field");
                return Task.CompletedTask;
            }));

            // --- override storage ---
            Add("OverrideSetClearPersist", "SetCategory stores, normalizes, clears, and persists the override in skills.json without touching SKILL.md", (CancellationToken ct) => WithConfigAsync((string config, string skills) =>
            {
                WriteSkill(skills, "alpha", "category: review");
                string before = File.ReadAllText(Path.Combine(skills, "alpha", "SKILL.md"));
                SkillManager manager = new SkillManager(skills);
                MuxAssert.IsNull(manager.GetCategoryOverride("alpha"), "no override yet");
                MuxAssert.AreEqual("my-team", manager.SetCategory("alpha", " My Team "), "normalized");
                MuxAssert.AreEqual("my-team", manager.GetCategoryOverride("ALPHA"), "case-insensitive lookup");
                string index = File.ReadAllText(Path.Combine(config, "skills.json"));
                MuxAssert.Contains("\"category\": \"my-team\"", index, "stored in skills.json");
                MuxAssert.AreEqual(before, File.ReadAllText(Path.Combine(skills, "alpha", "SKILL.md")), "SKILL.md untouched");
                MuxAssert.AreEqual("my-team", SkillCategories.LoadOverrides()["alpha"], "LoadOverrides");
                manager.SetEnabled("alpha", false);
                MuxAssert.AreEqual("my-team", manager.GetCategoryOverride("alpha"), "toggling keeps the override");
                MuxAssert.IsNull(manager.SetCategory("alpha", null), "cleared");
                MuxAssert.IsNull(manager.GetCategoryOverride("alpha"), "gone");
                MuxAssert.DoesNotContain("\"category\"", File.ReadAllText(Path.Combine(config, "skills.json")), "omitted when null");
                MuxAssert.IsNull(manager.SetCategory("never-seen", ""), "clearing a skill with no row is a no-op");
                MuxAssert.IsFalse(SettingsLoader.LoadSkillIndex().Exists(e => e.Id == "never-seen"), "no row created by a clear");
                MuxAssert.Throws<ArgumentException>(() => manager.SetCategory("alpha", "bad!"), "malformed refused");
                MuxAssert.Throws<ArgumentException>(() => manager.SetCategory(" ", "review"), "blank id refused");
                MuxAssert.IsNull(manager.GetCategoryOverride(" "), "blank id lookup");
                return Task.CompletedTask;
            }));
            Add("RuntimeAndStatus", "The runtime applies overrides, statuses carry the category, and changes refresh the catalog", (CancellationToken ct) => WithConfigAsync(async (string config, string seeded) =>
            {
                string skills = Path.Combine(config, "own-skills");
                WriteSkill(skills, "alpha", "category: review");
                WriteSkill(skills, "beta", string.Empty, "tags: [docker]");
                using (SkillRuntime runtime = new SkillRuntime(skills, SettingsLoader.LoadSkillIndex, () => { }))
                {
                    await runtime.RefreshNowAsync(ct).ConfigureAwait(false);
                    SkillStatus alpha = runtime.GetStatus().Find(s => s.Name == "alpha")!;
                    MuxAssert.AreEqual("review", alpha.Category, "file category");
                    MuxAssert.IsFalse(alpha.CategoryOverridden, "not overridden");
                    MuxAssert.AreEqual("containers", runtime.GetStatus().Find(s => s.Name == "beta")!.Category, "inferred from tags");
                    new SkillManager(skills).SetCategory("alpha", "security");
                    await runtime.RefreshNowAsync(ct).ConfigureAwait(false);
                    SkillStatus after = runtime.GetStatus().Find(s => s.Name == "alpha")!;
                    MuxAssert.AreEqual("security", after.Category, "override applied after refresh");
                    MuxAssert.IsTrue(after.CategoryOverridden, "flagged");
                    MuxAssert.IsTrue(after.Clone().CategoryOverridden && after.Clone().Category == "security", "clone keeps the category");
                    List<Skill> loaded = new List<Skill>(new SkillLoader(skills).Discover());
                    SkillCategories.ApplyOverrides(loaded);
                    List<SkillCategoryCount> counts = SkillCategories.Count(loaded);
                    MuxAssert.AreEqual("containers", counts[0].Category, "canonical order (containers before security)");
                    MuxAssert.AreEqual(2, counts.Count, "two categories");
                }
            }));

            // --- CLI ---
            Add("CliVerbs", "mux skill list/show/category/categories show and edit categories, with errors for bad input", (CancellationToken ct) => WithConfigAsync(async (string config, string skills) =>
            {
                WriteSkill(skills, "alpha", "category: qa-alpha");
                WriteSkill(skills, "beta", "category: qa-beta");
                CliRun list = await RunAsync(new SkillSettings { Action = "list", ConfigDir = config }, ct).ConfigureAwait(false);
                MuxAssert.AreEqual(0, list.Code, "list ok");
                MuxAssert.Contains("alpha\tenabled\tqa-alpha\t", list.Out, "category column");
                MuxAssert.Contains("git-commit\tenabled\tgit\t", list.Out, "seeded defaults carry their categories");
                CliRun filtered = await RunAsync(new SkillSettings { Action = "list", ConfigDir = config, Category = "QA Beta" }, ct).ConfigureAwait(false);
                MuxAssert.Contains("beta\t", filtered.Out, "filter keeps the category (normalized)");
                MuxAssert.DoesNotContain("alpha\t", filtered.Out, "filter drops others");
                MuxAssert.DoesNotContain("git-commit", filtered.Out, "filter drops defaults");
                CliRun json = await RunAsync(new SkillSettings { Action = "list", ConfigDir = config, OutputFormat = "json" }, ct).ConfigureAwait(false);
                MuxAssert.Contains("\"name\":\"alpha\",\"valid\":true,\"enabled\":true,\"category\":\"qa-alpha\",\"categoryOverridden\":false", json.Out, "json category");
                CliRun set = await RunAsync(new SkillSettings { Action = "category", Name = "alpha", Command = "QA Gamma", ConfigDir = config }, ct).ConfigureAwait(false);
                MuxAssert.AreEqual(0, set.Code, "set ok");
                MuxAssert.Contains("alpha: qa-gamma (override; SKILL.md says qa-alpha)", set.Out, "explains the override");
                CliRun print = await RunAsync(new SkillSettings { Action = "category", Name = "alpha", ConfigDir = config, OutputFormat = "json" }, ct).ConfigureAwait(false);
                MuxAssert.Contains("\"overridden\":true", print.Out, "json override flag");
                MuxAssert.Contains("\"fileCategory\":\"qa-alpha\"", print.Out, "json file category");
                CliRun show = await RunAsync(new SkillSettings { Action = "show", Name = "alpha", ConfigDir = config }, ct).ConfigureAwait(false);
                MuxAssert.Contains("Category:    qa-gamma (override)", show.Out, "show prints it");
                CliRun showJson = await RunAsync(new SkillSettings { Action = "show", Name = "alpha", ConfigDir = config, OutputFormat = "json" }, ct).ConfigureAwait(false);
                MuxAssert.Contains("\"category\":\"qa-gamma\",\"categoryOverridden\":true", showJson.Out, "show json");
                CliRun categories = await RunAsync(new SkillSettings { Action = "categories", ConfigDir = config }, ct).ConfigureAwait(false);
                MuxAssert.Contains("qa-beta\t1", categories.Out, "counts");
                MuxAssert.Contains("qa-gamma\t1", categories.Out, "override counted");
                MuxAssert.DoesNotContain("qa-alpha", categories.Out, "the overridden file category no longer counts");
                MuxAssert.IsTrue(categories.Out.IndexOf("git\t", StringComparison.Ordinal) < categories.Out.IndexOf("qa-beta", StringComparison.Ordinal), "canonical categories first");
                CliRun categoriesJson = await RunAsync(new SkillSettings { Action = "categories", ConfigDir = config, OutputFormat = "json" }, ct).ConfigureAwait(false);
                MuxAssert.Contains("\"known\":[\"git\"", categoriesJson.Out, "json lists the canonical set");
                CliRun clear = await RunAsync(new SkillSettings { Action = "category", Name = "alpha", Clear = true, ConfigDir = config }, ct).ConfigureAwait(false);
                MuxAssert.Contains("alpha: qa-alpha", clear.Out, "cleared back to SKILL.md");
                CliRun noName = await RunAsync(new SkillSettings { Action = "category", ConfigDir = config }, ct).ConfigureAwait(false);
                MuxAssert.AreEqual(1, noName.Code, "name required");
                MuxAssert.Contains("Usage: mux skill category", noName.Err, "usage");
                CliRun unknown = await RunAsync(new SkillSettings { Action = "category", Name = "nope", Command = "review", ConfigDir = config }, ct).ConfigureAwait(false);
                MuxAssert.AreEqual(1, unknown.Code, "unknown skill");
                MuxAssert.Contains("No skill named 'nope'", unknown.Err, "explains");
                CliRun both = await RunAsync(new SkillSettings { Action = "category", Name = "alpha", Command = "review", Clear = true, ConfigDir = config }, ct).ConfigureAwait(false);
                MuxAssert.Contains("not both", both.Err, "category and --clear conflict");
                CliRun malformed = await RunAsync(new SkillSettings { Action = "category", Name = "alpha", Command = "bad!", ConfigDir = config }, ct).ConfigureAwait(false);
                MuxAssert.AreEqual(1, malformed.Code, "malformed refused");
                MuxAssert.IsNull(new SkillManager(skills).GetCategoryOverride("alpha"), "nothing stored on failure");
                CliRun traversal = await RunAsync(new SkillSettings { Action = "category", Name = "../alpha", Command = "review", ConfigDir = config }, ct).ConfigureAwait(false);
                MuxAssert.AreEqual(1, traversal.Code, "path-like names refused");
                CliRun usage = await RunAsync(new SkillSettings { Action = "frobnicate", ConfigDir = config }, ct).ConfigureAwait(false);
                MuxAssert.Contains("category <name> [<category>|--clear]|categories", usage.Err, "usage lists the new verbs");
            }));
            Add("CliArgumentParsing", "The skill argument parser reads --category and --clear and rejects unknown options", (CancellationToken ct) =>
            {
                SkillSettings parsed = CliArgumentParser.ParseSkill(new[] { "list", "--category", "review" });
                MuxAssert.AreEqual("review", parsed.Category, "--category");
                SkillSettings clear = CliArgumentParser.ParseSkill(new[] { "category", "alpha", "--clear" });
                MuxAssert.IsTrue(clear.Clear, "--clear");
                MuxAssert.AreEqual("alpha", clear.Name, "name");
                SkillSettings inline = CliArgumentParser.ParseSkill(new[] { "list", "--category=docs" });
                MuxAssert.AreEqual("docs", inline.Category, "inline value");
                MuxAssert.Throws<InvalidOperationException>(() => CliArgumentParser.ParseSkill(new[] { "list", "--categry", "x" }), "typo refused");
                MuxAssert.Throws<InvalidOperationException>(() => CliArgumentParser.ParseSkill(new[] { "list", "--category" }), "missing value");
                return Task.CompletedTask;
            });

            // --- terminal ---
            Add("TerminalSkillsCategory", "/skills category shows, sets, normalizes, and clears; bad input is reported", (CancellationToken ct) => WithConfigAsync(async (string config, string seeded) =>
            {
                string skills = Path.Combine(config, "own-skills");
                WriteSkill(skills, "alpha", "category: review");
                using (SkillRuntime runtime = new SkillRuntime(skills, SettingsLoader.LoadSkillIndex, () => { }))
                {
                    await runtime.RefreshNowAsync(ct).ConfigureAwait(false);
                    HeadlessBackend backend = new HeadlessBackend(160, 40);
                    await using (JobManager manager = new JobManager(EchoRunner, maxConcurrency: 1))
                    using (MuxTuiApp app = new MuxTuiApp(backend, manager, "demo", ApprovalPolicyEnum.AutoApprove, skillRuntime: runtime))
                    {
                        Submit(backend, app, "/skills category alpha");
                        Submit(backend, app, "/skills category alpha Data Science");
                        await runtime.RefreshNowAsync(ct).ConfigureAwait(false);
                        MuxAssert.AreEqual("data-science", runtime.GetStatus().Find(s => s.Name == "alpha")!.Category, "set and normalized");
                        Submit(backend, app, "/skills categories");
                        Submit(backend, app, "/skills category alpha bad!");
                        Submit(backend, app, "/skills category nope review");
                        Submit(backend, app, "/skills category");
                        Submit(backend, app, "/skills frobnicate");
                        Submit(backend, app, "/skills category alpha --clear");
                        await runtime.RefreshNowAsync(ct).ConfigureAwait(false);
                        string transcript = string.Join("\n", app.TranscriptSnapshot());
                        MuxAssert.Contains("alpha: review", transcript, "shows the current category");
                        MuxAssert.Contains("alpha is now in category data-science", transcript, "set notice");
                        MuxAssert.Contains("Skill categories: data-science (1)", transcript, "categories listed");
                        MuxAssert.Contains("letters, digits, and single hyphens", transcript, "malformed explained");
                        MuxAssert.Contains("No skill named 'nope'", transcript, "unknown skill");
                        MuxAssert.Contains("Usage: /skills category", transcript, "usage");
                        MuxAssert.Contains("Usage: /skills [category", transcript, "unknown verb usage");
                        MuxAssert.Contains("Cleared the category override for alpha", transcript, "cleared");
                        MuxAssert.AreEqual("review", runtime.GetStatus().Find(s => s.Name == "alpha")!.Category, "back to SKILL.md");
                    }
                }
            }));

            // --- REST and dashboard ---
            Add("RestRoutes", "PUT /skills/category and GET /skills/categories work with auth, 400, and 404, and skills carry the category", (CancellationToken ct) => WithConfigAsync(async (string config, string skills) =>
            {
                WriteSkill(skills, "alpha", "category: review");
                WriteSkill(skills, "beta", string.Empty, "tags: [kubernetes]");
                string? previous = Environment.GetEnvironmentVariable("MUX_CONFIG_DIR");
                Environment.SetEnvironmentVariable("MUX_CONFIG_DIR", config);
                MuxServer? server = null;
                try
                {
                    int port = 0;
                    RestServerSettings rest = new RestServerSettings { Hostname = "127.0.0.1", ApiKey = "catkey" };
                    for (int attempt = 0; attempt < 10 && server == null; attempt++)
                    {
                        port = StubHttpServer.FreeLoopbackPort();
                        rest.Port = port;
                        MuxServer candidate = new MuxServer(rest, "9.9.9-test", new SessionStore(Path.Combine(config, "sessions")), () => new List<EndpointConfig>(), null);
                        try { candidate.Start(); server = candidate; } catch (Exception) { candidate.Dispose(); Thread.Sleep(50); }
                    }

                    MuxAssert.IsNotNull(server, "server bound");
                    string baseUrl = "http://127.0.0.1:" + port + "/v1.0/api/skills";
                    using (HttpClient http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) })
                    {
                        for (int attempt = 0; attempt < 20; attempt++)
                        {
                            try { await http.GetAsync("http://127.0.0.1:" + port + "/v1.0/api/health", ct).ConfigureAwait(false); break; }
                            catch (Exception) { await Task.Delay(100, ct).ConfigureAwait(false); }
                        }

                        using (HttpResponseMessage noKey = await http.PutAsync(baseUrl + "/category", Json(new { Id = "alpha", Category = "testing" }), ct).ConfigureAwait(false))
                        {
                            MuxAssert.AreEqual(401, (int)noKey.StatusCode, "a key is required to set");
                        }

                        using (HttpResponseMessage noKeyList = await http.GetAsync(baseUrl + "/categories", ct).ConfigureAwait(false))
                        {
                            MuxAssert.AreEqual(401, (int)noKeyList.StatusCode, "a key is required to list");
                        }

                        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "catkey");
                        string list = await http.GetStringAsync(baseUrl, ct).ConfigureAwait(false);
                        MuxAssert.Contains("\"Category\":\"review\"", list, "list carries the category");
                        MuxAssert.Contains("\"Category\":\"kubernetes\"", list, "inferred category listed");
                        MuxAssert.Contains("\"FileCategory\":\"review\"", list, "file category listed");

                        using (HttpResponseMessage set = await http.PutAsync(baseUrl + "/category", Json(new { Id = "alpha", Category = "Code Review" }), ct).ConfigureAwait(false))
                        {
                            string body = await set.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                            MuxAssert.AreEqual(200, (int)set.StatusCode, "set: " + body);
                            MuxAssert.Contains("\"Category\":\"code-review\"", body, "normalized and returned");
                            MuxAssert.Contains("\"CategoryOverridden\":true", body, "flagged");
                        }

                        string categories = await http.GetStringAsync(baseUrl + "/categories", ct).ConfigureAwait(false);
                        MuxAssert.Contains("\"Category\":\"code-review\"", categories, "counts include the override");
                        MuxAssert.Contains("\"Known\":[\"git\"", categories, "canonical list included");

                        using (HttpResponseMessage malformed = await http.PutAsync(baseUrl + "/category", Json(new { Id = "alpha", Category = "bad!" }), ct).ConfigureAwait(false))
                        {
                            MuxAssert.AreEqual(400, (int)malformed.StatusCode, "malformed category is 400");
                        }

                        using (HttpResponseMessage noId = await http.PutAsync(baseUrl + "/category", Json(new { Category = "review" }), ct).ConfigureAwait(false))
                        {
                            MuxAssert.AreEqual(400, (int)noId.StatusCode, "missing id is 400");
                        }

                        using (HttpResponseMessage badJson = await http.PutAsync(baseUrl + "/category", new StringContent("{not json", Encoding.UTF8, "application/json"), ct).ConfigureAwait(false))
                        {
                            MuxAssert.AreEqual(400, (int)badJson.StatusCode, "bad JSON is 400");
                        }

                        using (HttpResponseMessage unknown = await http.PutAsync(baseUrl + "/category", Json(new { Id = "nope", Category = "review" }), ct).ConfigureAwait(false))
                        {
                            MuxAssert.AreEqual(404, (int)unknown.StatusCode, "unknown skill is 404");
                        }

                        using (HttpResponseMessage traversal = await http.PutAsync(baseUrl + "/category", Json(new { Id = "../alpha", Category = "review" }), ct).ConfigureAwait(false))
                        {
                            MuxAssert.AreEqual(404, (int)traversal.StatusCode, "path-like ids are 404");
                        }

                        using (HttpResponseMessage clear = await http.PutAsync(baseUrl + "/category", Json(new { Id = "alpha", Category = (string?)null }), ct).ConfigureAwait(false))
                        {
                            string body = await clear.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                            MuxAssert.AreEqual(200, (int)clear.StatusCode, "clear");
                            MuxAssert.Contains("\"Category\":\"review\"", body, "back to SKILL.md");
                            MuxAssert.Contains("\"CategoryOverridden\":false", body, "no longer overridden");
                        }
                    }
                }
                finally
                {
                    server?.Dispose();
                    Environment.SetEnvironmentVariable("MUX_CONFIG_DIR", previous);
                }
            }));
            Add("DashboardMarkup", "The dashboard has a category filter, column, edit field, and menu action, translated in every language", (CancellationToken ct) =>
            {
                string html = DashboardPage.Render(null, "9.9.9-test");
                MuxAssert.Contains("<select id=\"sk_cat\"", html, "filter control");
                MuxAssert.Contains("{h:t(\"col.category\")", html, "category column");
                MuxAssert.Contains("{label:t(\"act.setcategory\"),run:function(){catSk(s.Name);}}", html, "row menu action");
                MuxAssert.Contains("/v1.0/api/skills/category", html, "calls the set route");
                MuxAssert.Contains("/v1.0/api/skills/categories", html, "loads the canonical list");
                MuxAssert.Contains("{id:\"Category\",label:t(\"col.category\"),list:_skKnown", html, "editor field with suggestions");
                MuxAssert.Contains("var s=_skView[i]", html, "row actions use the filtered view");
                foreach (string key in new[] { "col.category", "act.setcategory", "sk.allcategories", "sk.categoryhelp", "toast.categorySaved" })
                {
                    int count = System.Text.RegularExpressions.Regex.Matches(html, "\"" + System.Text.RegularExpressions.Regex.Escape(key) + "\":").Count;
                    MuxAssert.AreEqual(11, count, key + " is translated in all 11 languages");
                }

                return Task.CompletedTask;
            });

            // --- MCP ---
            Add("McpListSkillsCategory", "MCP list_skills returns categories, filters by one, and rejects a malformed filter", (CancellationToken ct) => WithConfigAsync(async (string config, string seeded) =>
            {
                string skills = Path.Combine(config, "own-skills");
                WriteSkill(skills, "alpha", "category: review");
                WriteSkill(skills, "beta", "category: testing");
                using (SkillRuntime runtime = new SkillRuntime(skills, SettingsLoader.LoadSkillIndex, () => { }))
                {
                    await runtime.RefreshNowAsync(ct).ConfigureAwait(false);
                    MuxMcpServerOptions options = new MuxMcpServerOptions { DefaultWorkingDirectory = config };
                    MuxMcpTools tools = new MuxMcpTools(options, new FakeMcpRunExecutor(), () => new List<EndpointConfig>(), new SessionStore(Path.Combine(config, "sessions")), runtime);
                    Dictionary<string, Func<RpcParameters?, CancellationToken, Task<object>>> handlers = new Dictionary<string, Func<RpcParameters?, CancellationToken, Task<object>>>();
                    object? schema = null;
                    tools.RegisterAll((string name, string description, object inputSchema, Func<RpcParameters?, CancellationToken, Task<object>> handler) =>
                    {
                        handlers[name] = handler;
                        if (name == "list_skills") schema = inputSchema;
                    });
                    MuxAssert.Contains("\"category\"", JsonSerializer.Serialize(schema), "schema documents the filter");
                    string all = JsonSerializer.Serialize(await handlers["list_skills"](new RpcParameters("{}"), ct).ConfigureAwait(false));
                    MuxAssert.Contains("review", all, "categories returned");
                    MuxAssert.Contains("testing", all, "both skills listed");
                    string only = JsonSerializer.Serialize(await handlers["list_skills"](new RpcParameters("{\"category\":\"Testing\"}"), ct).ConfigureAwait(false));
                    MuxAssert.Contains("beta", only, "filter keeps the match");
                    MuxAssert.DoesNotContain("alpha", only, "filter drops the rest");
                    string none = JsonSerializer.Serialize(await handlers["list_skills"](new RpcParameters("{\"category\":\"cloud\"}"), ct).ConfigureAwait(false));
                    MuxAssert.DoesNotContain("alpha", none, "an empty category lists nothing");
                    await MuxAssert.ThrowsAsync<McpToolException>(() => handlers["list_skills"](new RpcParameters("{\"category\":\"bad!\"}"), ct), "malformed filter refused").ConfigureAwait(false);
                }
            }));

            return new TestSuiteDescriptor(SuiteId, "Skill categories: rules, defaults, overrides, CLI, terminal, REST, dashboard, MCP", cases);
        }

        #endregion

        #region Private-Methods

        private static StringContent Json(object value)
        {
            return new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json");
        }

        private static Skill LoadSkill(string root, string id, string extraFrontmatter)
        {
            WriteSkill(root, id, extraFrontmatter);
            return new SkillLoader(root).Load(Path.Combine(root, id));
        }

        private static void WriteSkill(string root, string id, string categoryLine, string extra = "")
        {
            string dir = Path.Combine(root, id);
            Directory.CreateDirectory(dir);
            string frontmatter = "---\nname: " + id + "\ndescription: A test skill named " + id + ".\n"
                + (categoryLine.Length > 0 ? categoryLine + "\n" : string.Empty)
                + (extra.Length > 0 ? extra + "\n" : string.Empty)
                + "---\n\nProcedure: follow the steps carefully and report what you found.\n";
            File.WriteAllText(Path.Combine(dir, "SKILL.md"), frontmatter);
        }

        private static void Submit(HeadlessBackend backend, MuxTuiApp app, string prompt)
        {
            backend.FeedInput(prompt + "\r");
            app.PumpInputOnce();
        }

        private static async IAsyncEnumerable<AgentEvent> EchoRunner(Job job, string prompt, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask.ConfigureAwait(false);
            yield return new AssistantTextEvent { Text = "Echo: " + prompt };
            yield return new RunCompletedEvent { RunId = Guid.NewGuid().ToString("N"), Status = "completed", IterationsCompleted = 1, DurationMs = 1 };
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

        private static async Task WithTempAsync(Func<string, Task> body)
        {
            string root = Path.Combine(Path.GetTempPath(), "mux-skillcat-" + Guid.NewGuid().ToString("N"));
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

        // A throwaway config directory (with a skills folder) made current for this async flow, so SkillManager and
        // SettingsLoader read and write it instead of the real ~/.mux.
        private static async Task WithConfigAsync(Func<string, string, Task> body)
        {
            string config = Path.Combine(Path.GetTempPath(), "mux-skillcat-cfg-" + Guid.NewGuid().ToString("N"));
            string skills = Path.Combine(config, "skills");
            Directory.CreateDirectory(skills);
            try
            {
                using (SettingsLoader.PushConfigDirectoryOverride(config))
                {
                    await body(config, skills).ConfigureAwait(false);
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
