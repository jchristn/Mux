namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Net.Http;
    using System.Net.Sockets;
    using System.Runtime.CompilerServices;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Cli.App;
    using Mux.Core.Agent;
    using Mux.Core.Enums;
    using Mux.Core.Jobs;
    using Mux.Core.Memory;
    using Mux.Core.Models;
    using Mux.Core.Sessions;
    using Mux.Core.Tools;
    using Mux.Server;
    using Test.Shared.Support;
    using Touchstone.Core;
    using TUIKit.Terminal;

    /// <summary>
    /// Touchstone suite for persistent memory (Phase 6, row 24): the <see cref="MemoryStore"/> (save, update by name,
    /// get, delete, clear, search, slugging, project isolation, the global scope, corrupt files, the index file), the
    /// prompt index and its byte cap, the <c>remember</c>, <c>forget</c>, and <c>recall</c> tools, <c>#</c> quick-add and
    /// <c>/memory</c> in a headless terminal, the <c>mux memory</c> verb, the REST routes, and the settings. Positive and
    /// negative cases throughout.
    /// </summary>
    public static class MemorySuite
    {
        #region Private-Members

        private const string SuiteId = "Memory";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Builds the memory suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/> for the memory cases.</returns>
        public static TestSuiteDescriptor Create()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();
            void Add(string id, string name, Action<MemoryFixture> body)
            {
                cases.Add(new TestCaseDescriptor(SuiteId, id, name, (CancellationToken ct) => { using (MemoryFixture f = new MemoryFixture()) { body(f); } return Task.CompletedTask; }));
            }

            void AddAsync(string id, string name, Func<MemoryFixture, CancellationToken, Task> body)
            {
                cases.Add(new TestCaseDescriptor(SuiteId, id, name, async (CancellationToken ct) => { using (MemoryFixture f = new MemoryFixture()) { await body(f, ct).ConfigureAwait(false); } }));
            }

            // --- store ---
            Add("SlugifyAndNames", "Names slug to lowercase hyphenated text, and quick-add names come from the first words", (MemoryFixture f) =>
            {
                MuxAssert.AreEqual("prefers-tabs-over-spaces", MemoryStore.Slugify("  Prefers TABS over spaces!! "), "slug");
                MuxAssert.AreEqual("a-b", MemoryStore.Slugify("a---b"), "repeated separators collapse");
                MuxAssert.AreEqual(string.Empty, MemoryStore.Slugify("!!!"), "no letters gives empty");
                MuxAssert.AreEqual(string.Empty, MemoryStore.Slugify(null), "null gives empty");
                MuxAssert.AreEqual(60, MemoryStore.Slugify(new string('a', 200)).Length, "capped at 60");
                MuxAssert.AreEqual("tests-need-docker-running-before-you", MemoryStore.NameFromText("Tests need docker running before you run them, always."), "first six words");
                MuxAssert.AreEqual(string.Empty, MemoryStore.NameFromText("?? !!"), "no words");
                MuxAssert.IsTrue(MemoryStore.TryParseScope("GLOBAL", out MemoryScopeEnum g) && g == MemoryScopeEnum.Global, "global parses");
                MuxAssert.IsTrue(MemoryStore.TryParseScope("repo", out MemoryScopeEnum p) && p == MemoryScopeEnum.Project, "repo alias");
                MuxAssert.IsFalse(MemoryStore.TryParseScope("team", out _), "unknown scope");
            });
            Add("SaveGetUpdateDelete", "A memory saves, reads back, updates in place by name, and deletes", (MemoryFixture f) =>
            {
                MemoryEntry saved = f.Store.Save("Test Command", "Run tests with dotnet test src/Mux.sln", "Run tests with `dotnet test src/Mux.sln`.\nThey take 4 minutes.", MemoryScopeEnum.Project, f.Project, out bool created);
                MuxAssert.IsTrue(created, "created");
                MuxAssert.AreEqual("test-command", saved.Slug, "slug");
                MuxAssert.IsTrue(File.Exists(saved.FilePath), "file written");
                MuxAssert.Contains("name: Test Command", File.ReadAllText(saved.FilePath), "frontmatter name");
                MemoryEntry read = f.Store.Get("TEST command", null, f.Project)!;
                MuxAssert.Contains("They take 4 minutes.", read.Content, "content round-trips");
                MuxAssert.AreEqual("Run tests with dotnet test src/Mux.sln", read.Description, "description");
                Thread.Sleep(20);
                MemoryEntry updated = f.Store.Save("test-command", "Use the Touchstone runner", null, MemoryScopeEnum.Project, f.Project, out bool createdAgain);
                MuxAssert.IsFalse(createdAgain, "updated, not created");
                MuxAssert.AreEqual(read.CreatedUtc, updated.CreatedUtc, "created time kept");
                MuxAssert.IsTrue(updated.UpdatedUtc > read.UpdatedUtc, "updated time moves");
                MuxAssert.AreEqual("Use the Touchstone runner", f.Store.Get("test-command", null, f.Project)!.Content, "content defaults to the description");
                MuxAssert.AreEqual(1, f.Store.List(MemoryScopeEnum.Project, f.Project).Count, "deduplicated by name");
                MuxAssert.IsNotNull(f.Store.Delete("test-command", null, f.Project), "deleted");
                MuxAssert.IsNull(f.Store.Get("test-command", null, f.Project), "gone");
                MuxAssert.IsNull(f.Store.Delete("test-command", null, f.Project), "second delete finds nothing");
            });
            Add("SaveRejectsBadInput", "Saving needs a name with letters or digits and some text", (MemoryFixture f) =>
            {
                MuxAssert.Throws<ArgumentException>(() => f.Store.Save("!!!", "d", "c", MemoryScopeEnum.Project, f.Project, out _), "no-letter name");
                MuxAssert.Throws<ArgumentException>(() => f.Store.Save("name", "  ", null, MemoryScopeEnum.Project, f.Project, out _), "no text");
                MuxAssert.Throws<ArgumentException>(() => new MemoryStore(" "), "blank root");
                MemoryEntry fromContent = f.Store.Save("x", null, "first line\nsecond line", MemoryScopeEnum.Project, f.Project, out _);
                MuxAssert.AreEqual("first line", fromContent.Description, "description from the first line");
                MemoryEntry longOne = f.Store.Save("long", new string('d', 500), null, MemoryScopeEnum.Project, f.Project, out _);
                MuxAssert.IsTrue(longOne.Description.Length <= 200 && longOne.Description.EndsWith("...", StringComparison.Ordinal), "description capped");
                MemoryEntry multiLine = f.Store.Save("ml", "line one\nline two", null, MemoryScopeEnum.Project, f.Project, out _);
                MuxAssert.AreEqual("line one line two", multiLine.Description, "description is one line");
            });
            Add("ProjectIsolationAndGlobalScope", "Projects do not see each other's memories; global memories are visible everywhere", (MemoryFixture f) =>
            {
                string other = Path.Combine(f.Root, "other-project");
                Directory.CreateDirectory(other);
                f.Store.Save("alpha-only", "only in alpha", null, MemoryScopeEnum.Project, f.Project, out _);
                f.Store.Save("beta-only", "only in beta", null, MemoryScopeEnum.Project, other, out _);
                f.Store.Save("tabs", "prefers tabs", null, MemoryScopeEnum.Global, f.Project, out _);
                MuxAssert.IsNotNull(f.Store.Get("alpha-only", null, f.Project), "own project");
                MuxAssert.IsNull(f.Store.Get("beta-only", null, f.Project), "other project hidden");
                MuxAssert.IsNotNull(f.Store.Get("tabs", null, other), "global visible from another project");
                MuxAssert.AreEqual(2, f.Store.ListAll(f.Project).Count, "project plus global");
                MuxAssert.AreNotEqual(MemoryStore.ProjectKey(f.Project), MemoryStore.ProjectKey(other), "different keys");
                MuxAssert.Contains("my-app-", MemoryStore.ProjectKey(f.Project), "key starts with the folder name");
                MuxAssert.AreEqual(MemoryStore.ProjectKey(Path.Combine(f.Project, "src")), MemoryStore.ProjectKey(f.Project), "a subfolder of a repository shares the key");
                f.Store.Save("tabs", "project override", null, MemoryScopeEnum.Project, f.Project, out _);
                MuxAssert.AreEqual(MemoryScopeEnum.Project, f.Store.Get("tabs", null, f.Project)!.Scope, "project wins over global for the same name");
                MuxAssert.AreEqual(MemoryScopeEnum.Global, f.Store.Get("tabs", MemoryScopeEnum.Global, f.Project)!.Scope, "explicit scope");
            });
            Add("CorruptFilesAndIndex", "Files without frontmatter are ignored and the index lists every memory", (MemoryFixture f) =>
            {
                f.Store.Save("good", "a good one", null, MemoryScopeEnum.Project, f.Project, out _);
                string dir = f.Store.DirectoryFor(MemoryScopeEnum.Project, f.Project);
                File.WriteAllText(Path.Combine(dir, "junk.md"), "no frontmatter here");
                File.WriteAllText(Path.Combine(dir, "noname.md"), "---\ndescription: x\n---\nbody");
                File.WriteAllText(Path.Combine(dir, "unterminated.md"), "---\nname: x\nbody");
                List<MemoryEntry> entries = f.Store.List(MemoryScopeEnum.Project, f.Project);
                MuxAssert.AreEqual(1, entries.Count, "only the valid memory: " + string.Join(",", entries.ConvertAll(e => e.Slug)));
                string index = File.ReadAllText(Path.Combine(dir, "MEMORY.md"));
                MuxAssert.Contains("- [good](good.md): a good one", index, "index entry");
                f.Store.Save("second", "another", null, MemoryScopeEnum.Project, f.Project, out _);
                MuxAssert.Contains("second.md", File.ReadAllText(Path.Combine(dir, "MEMORY.md")), "index rewritten");
                MuxAssert.AreEqual(0, f.Store.List(MemoryScopeEnum.Global, f.Project).Count, "missing folder lists nothing");
                MuxAssert.AreEqual(0, new List<string>(Directory.GetFiles(dir, "*.tmp")).Count, "no temporary files left");
            });
            Add("SearchAndClear", "Search needs every word; clear removes a scope and raises Changed", (MemoryFixture f) =>
            {
                int changes = 0;
                f.Store.Changed += (object? s, EventArgs e) => changes++;
                f.Store.Save("db", "Postgres runs on port 5433", "Postgres runs on port 5433 in docker compose", MemoryScopeEnum.Project, f.Project, out _);
                f.Store.Save("cache", "Redis on 6379", null, MemoryScopeEnum.Project, f.Project, out _);
                f.Store.Save("editor", "prefers vim keys", null, MemoryScopeEnum.Global, f.Project, out _);
                MuxAssert.AreEqual(1, f.Store.Search("postgres DOCKER", f.Project).Count, "all words, any case");
                MuxAssert.AreEqual(0, f.Store.Search("postgres redis", f.Project).Count, "not all words");
                MuxAssert.AreEqual(3, f.Store.Search(null, f.Project).Count, "blank query returns all");
                MuxAssert.AreEqual(2, f.Store.Clear(MemoryScopeEnum.Project, f.Project), "two project memories cleared");
                MuxAssert.AreEqual(0, f.Store.Clear(MemoryScopeEnum.Project, f.Project), "nothing left");
                MuxAssert.AreEqual(1, f.Store.ListAll(f.Project).Count, "global kept");
                MuxAssert.AreEqual(4, changes, "three saves and one clear");
            });

            // --- prompt index ---
            Add("PromptSectionListsAndCaps", "The prompt index lists project then global memories and leaves the oldest out over the cap", (MemoryFixture f) =>
            {
                string empty = MemoryPromptBuilder.Build(f.Store, f.Project, 16384);
                MuxAssert.Contains("## Memory", empty, "heading");
                MuxAssert.Contains("No memories are saved yet.", empty, "empty note");
                MuxAssert.Contains("remember", empty, "explains the tools");
                for (int i = 0; i < 5; i++)
                {
                    f.Store.Save("fact " + i, "Fact number " + i + " " + new string('x', 30), null, MemoryScopeEnum.Project, f.Project, out _);
                    Thread.Sleep(15);
                }

                f.Store.Save("global fact", "A global fact", null, MemoryScopeEnum.Global, f.Project, out _);
                string full = MemoryPromptBuilder.Build(f.Store, f.Project, 16384);
                MuxAssert.Contains("Project memories:", full, "project heading");
                MuxAssert.Contains("Global memories:", full, "global heading");
                MuxAssert.Contains("- fact-4: Fact number 4", full, "newest listed");
                MuxAssert.IsTrue(full.IndexOf("fact-4", StringComparison.Ordinal) < full.IndexOf("fact-0", StringComparison.Ordinal), "newest first");
                string capped = MemoryPromptBuilder.Build(f.Store, f.Project, 120);
                MuxAssert.Contains("fact-4", capped, "newest kept");
                MuxAssert.DoesNotContain("fact-0", capped, "oldest left out");
                MuxAssert.Contains("not listed because of the memoryMaxBytes limit", capped, "omission noted");
                string none = MemoryPromptBuilder.Build(f.Store, f.Project, 0);
                MuxAssert.DoesNotContain("fact-4", none, "zero budget lists nothing");
                MuxAssert.Contains("6 older memories are", none, "all counted as omitted");
                MuxAssert.Throws<ArgumentNullException>(() => MemoryPromptBuilder.Build(null!, f.Project, 10), "null store");
            });
            Add("BinderAppendsPromptSection", "The tool binder appends the memory index from an additional provider", (MemoryFixture f) =>
            {
                f.Store.Save("binder fact", "Visible through the binder", null, MemoryScopeEnum.Project, f.Project, out _);
                AgentLoopOptions template = new AgentLoopOptions(new EndpointConfig { Name = "e", BaseUrl = "http://localhost", Model = "m" }) { WorkingDirectory = f.Project };
                MemoryToolProvider provider = new MemoryToolProvider(f.Store, 16384);
                ExternalToolsBinder.Apply(template, "BASE", "COMPACT", null, null, null, 5, new List<IExternalToolProvider> { provider });
                MuxAssert.Contains("BASE", template.SystemPrompt, "base kept");
                MuxAssert.Contains("- binder-fact: Visible through the binder", template.SystemPrompt, "index appended");
                provider.MaxPromptBytes = -5;
                MuxAssert.AreEqual(0, provider.MaxPromptBytes, "negative budget clamps to zero");
            });

            // --- tools ---
            AddAsync("ToolsRememberRecallForget", "remember saves, recall reads and searches, and forget deletes", async (MemoryFixture f, CancellationToken ct) =>
            {
                MemoryToolProvider tools = new MemoryToolProvider(f.Store, 16384);
                MuxAssert.AreEqual("remember,forget,recall", string.Join(",", new List<ToolDefinition>(tools.GetToolDefinitions()).ConvertAll(d => d.Name)), "tool names");
                MuxAssert.AreEqual(ToolMutationKind.Mutating, tools.GetMutationKind("remember"), "remember mutates");
                MuxAssert.AreEqual(ToolMutationKind.Mutating, tools.GetMutationKind("FORGET"), "forget mutates");
                MuxAssert.AreEqual(ToolMutationKind.ReadOnly, tools.GetMutationKind("recall"), "recall reads");
                MuxAssert.IsTrue(tools.HasTool("Recall") && !tools.HasTool("read_file"), "claims only its tools");
                ToolResult saved = await tools.ExecuteAsync("remember", Json(new { name = "Deploy Target", description = "Deploys go to staging first", content = "Always deploy to staging before prod.", scope = "global" }), f.Project, ct).ConfigureAwait(false);
                MuxAssert.IsTrue(saved.Success, saved.Content);
                MuxAssert.Contains("\"created\":true", saved.Content, "created");
                MuxAssert.Contains("\"scope\":\"global\"", saved.Content, "scope");
                ToolResult again = await tools.ExecuteAsync("remember", Json(new { name = "deploy target", description = "Updated" }), f.Project, ct).ConfigureAwait(false);
                MuxAssert.Contains("\"created\":true", again.Content, "a project memory with the same name is separate from the global one");
                ToolResult read = await tools.ExecuteAsync("recall", Json(new { name = "deploy-target" }), f.Project, ct).ConfigureAwait(false);
                MuxAssert.Contains("\"scope\":\"project\"", read.Content, "project first");
                ToolResult search = await tools.ExecuteAsync("recall", Json(new { query = "staging prod" }), f.Project, ct).ConfigureAwait(false);
                MuxAssert.Contains("\"count\":1", search.Content, "search hit");
                MuxAssert.Contains("Always deploy to staging", search.Content, "content snippet");
                ToolResult all = await tools.ExecuteAsync("recall", Json(new { }), f.Project, ct).ConfigureAwait(false);
                MuxAssert.Contains("\"count\":2", all.Content, "list all");
                ToolResult forgot = await tools.ExecuteAsync("forget", Json(new { name = "deploy-target", scope = "global" }), f.Project, ct).ConfigureAwait(false);
                MuxAssert.Contains("\"deleted\":true", forgot.Content, "forgot");
                MuxAssert.IsNull(f.Store.Get("deploy-target", MemoryScopeEnum.Global, f.Project), "global gone");
                MuxAssert.IsNotNull(f.Store.Get("deploy-target", MemoryScopeEnum.Project, f.Project), "project kept");
                MuxAssert.Contains("## Memory", tools.BuildPromptSection(f.Project), "prompt section");
            });
            AddAsync("ToolsRejectBadCalls", "The memory tools report missing names, bad scopes, unknown memories, and unknown tools", async (MemoryFixture f, CancellationToken ct) =>
            {
                MemoryToolProvider tools = new MemoryToolProvider(f.Store, 16384);
                MuxAssert.Contains("invalid_arguments", (await tools.ExecuteAsync("remember", Json(new { description = "x" }), f.Project, ct).ConfigureAwait(false)).Content, "remember needs a name");
                MuxAssert.Contains("invalid_arguments", (await tools.ExecuteAsync("remember", Json(new { name = "x", description = "y", scope = "team" }), f.Project, ct).ConfigureAwait(false)).Content, "bad scope");
                MuxAssert.Contains("invalid_arguments", (await tools.ExecuteAsync("remember", Json(new { name = "!!!", description = "y" }), f.Project, ct).ConfigureAwait(false)).Content, "no-letter name");
                MuxAssert.Contains("not_found", (await tools.ExecuteAsync("forget", Json(new { name = "nope" }), f.Project, ct).ConfigureAwait(false)).Content, "forget unknown");
                MuxAssert.Contains("invalid_arguments", (await tools.ExecuteAsync("forget", Json(new { }), f.Project, ct).ConfigureAwait(false)).Content, "forget needs a name");
                MuxAssert.Contains("not_found", (await tools.ExecuteAsync("recall", Json(new { name = "nope" }), f.Project, ct).ConfigureAwait(false)).Content, "recall unknown");
                MuxAssert.Contains("unknown_tool", (await tools.ExecuteAsync("memorize", Json(new { }), f.Project, ct).ConfigureAwait(false)).Content, "unknown tool");
                MuxAssert.Throws<ArgumentNullException>(() => new MemoryToolProvider(null!, 1), "null store");
            });

            // --- quick-add parsing ---
            Add("QuickAddParsing", "# and #global are quick-adds; headings and a bare # are not", (MemoryFixture f) =>
            {
                MuxAssert.IsTrue(MemoryQuickAdd.TryParse("# tests need docker", out MemoryScopeEnum a, out string at) && a == MemoryScopeEnum.Project && at == "tests need docker", "project");
                MuxAssert.IsTrue(MemoryQuickAdd.TryParse("#global prefers tabs", out MemoryScopeEnum b, out string bt) && b == MemoryScopeEnum.Global && bt == "prefers tabs", "global");
                MuxAssert.IsTrue(MemoryQuickAdd.TryParse("#globalization notes", out MemoryScopeEnum c, out _) && c == MemoryScopeEnum.Project, "a word starting with global is project text");
                MuxAssert.IsFalse(MemoryQuickAdd.TryParse("## Heading", out _, out _), "markdown heading");
                MuxAssert.IsFalse(MemoryQuickAdd.TryParse("#", out _, out _), "bare #");
                MuxAssert.IsFalse(MemoryQuickAdd.TryParse("# !!!", out _, out _), "no words");
                MuxAssert.IsFalse(MemoryQuickAdd.TryParse("hello # world", out _, out _), "not at the start");
                MuxAssert.Contains("Remembered project memory 'tests-need-docker'", MemoryQuickAdd.Save(f.Store, MemoryScopeEnum.Project, "tests need docker", f.Project), "save message");
                MuxAssert.Contains("Updated project memory", MemoryQuickAdd.Save(f.Store, MemoryScopeEnum.Project, "tests need docker", f.Project), "update message");
            });

            // --- terminal ---
            AddAsync("TerminalQuickAddAndMemoryCommands", "# saves without a model call, and /memory lists, shows, deletes, and clears with confirmation", async (MemoryFixture f, CancellationToken ct) =>
            {
                HeadlessBackend backend = new HeadlessBackend(160, 40);
                int modelCalls = 0;
                await using (JobManager manager = new JobManager((Job job, string prompt, CancellationToken token) => { Interlocked.Increment(ref modelCalls); return EchoRunner(job, prompt, token); }, maxConcurrency: 1))
                using (MuxTuiApp app = new MuxTuiApp(backend, manager, "demo", ApprovalPolicyEnum.AutoApprove, workingDirectory: f.Project, memoryStore: f.Store))
                {
                    Submit(backend, app, "/memory");
                    Submit(backend, app, "# integration tests need docker running");
                    Submit(backend, app, "#global prefers short commit messages");
                    await app.DrainProjectorsAsync().ConfigureAwait(false);
                    MuxAssert.AreEqual(0, modelCalls, "no model call for quick-add");
                    MuxAssert.IsNotNull(f.Store.Get("integration-tests-need-docker-running", MemoryScopeEnum.Project, f.Project), "project memory saved");
                    MuxAssert.IsNotNull(f.Store.Get("prefers-short-commit-messages", MemoryScopeEnum.Global, f.Project), "global memory saved");
                    Submit(backend, app, "/memory");
                    Submit(backend, app, "/memory show integration-tests-need-docker-running");
                    Submit(backend, app, "/memory show nope");
                    Submit(backend, app, "/memory delete");
                    Submit(backend, app, "/memory frob");
                    Submit(backend, app, "/memory clear");
                    MuxAssert.AreEqual(1, f.Store.List(MemoryScopeEnum.Project, f.Project).Count, "clear without --yes deletes nothing");
                    Submit(backend, app, "/memory clear --yes");
                    Submit(backend, app, "/memory delete prefers-short-commit-messages");
                    Submit(backend, app, "/memory clear global");
                    Submit(backend, app, "## a heading is a prompt");
                    await app.DrainProjectorsAsync().ConfigureAwait(false);
                    string transcript = string.Join("\n", app.TranscriptSnapshot());
                    MuxAssert.Contains("No memories yet.", transcript, "empty list");
                    MuxAssert.Contains("Remembered project memory 'integration-tests-need-docker-running'", transcript, "quick-add notice");
                    MuxAssert.Contains("Remembered global memory 'prefers-short-commit-messages'", transcript, "global notice");
                    MuxAssert.Contains("[project] integration-tests-need-docker-running", transcript, "listed");
                    MuxAssert.Contains("[global] prefers-short-commit-messages", transcript, "global listed");
                    MuxAssert.Contains("No memory named 'nope'", transcript, "unknown name");
                    MuxAssert.Contains("Name a memory", transcript, "missing name");
                    MuxAssert.Contains("Usage: /memory", transcript, "usage");
                    MuxAssert.Contains("Confirm with /memory clear --yes", transcript, "confirmation asked");
                    MuxAssert.Contains("Cleared 1 project memories.", transcript, "cleared");
                    MuxAssert.Contains("Deleted global memory 'prefers-short-commit-messages'", transcript, "deleted");
                    MuxAssert.Contains("No global memories to clear.", transcript, "nothing to clear");
                    MuxAssert.AreEqual(1, modelCalls, "a markdown heading still reaches the model");
                    MuxAssert.AreEqual(0, f.Store.ListAll(f.Project).Count, "all gone");
                }
            });
            AddAsync("TerminalWithoutMemory", "Without a store, # is a normal prompt and /memory explains the setting", async (MemoryFixture f, CancellationToken ct) =>
            {
                HeadlessBackend backend = new HeadlessBackend(160, 40);
                int modelCalls = 0;
                await using (JobManager manager = new JobManager((Job job, string prompt, CancellationToken token) => { Interlocked.Increment(ref modelCalls); return EchoRunner(job, prompt, token); }, maxConcurrency: 1))
                using (MuxTuiApp app = new MuxTuiApp(backend, manager, "demo", ApprovalPolicyEnum.AutoApprove, workingDirectory: f.Project))
                {
                    Submit(backend, app, "/memory");
                    Submit(backend, app, "# remember this");
                    await app.DrainProjectorsAsync().ConfigureAwait(false);
                    MuxAssert.Contains("Memory is turned off", string.Join("\n", app.TranscriptSnapshot()), "notice");
                    MuxAssert.AreEqual(1, modelCalls, "# went to the model");
                }
            });

            // --- CLI and REST ---
            Add("CliMemoryVerb", "mux memory add, list, show, and delete work and report bad input", (MemoryFixture f) =>
            {
                string? original = Environment.GetEnvironmentVariable("MUX_CONFIG_DIR");
                Environment.SetEnvironmentVariable("MUX_CONFIG_DIR", f.Config);
                try
                {
                    CliInvocationResult add = InvokeCli(new[] { "memory", "add", "builds", "use", "make", "--name", "build-cmd", "--cwd", f.Project });
                    MuxAssert.AreEqual(0, add.ExitCode, add.StdErr);
                    MuxAssert.Contains("Remembered project memory 'build-cmd'", add.StdOut, "added");
                    CliInvocationResult list = InvokeCli(new[] { "memory", "list", "--cwd", f.Project });
                    MuxAssert.Contains("[project] build-cmd: builds use make", list.StdOut, "listed");
                    CliInvocationResult json = InvokeCli(new[] { "memory", "list", "--output-format", "json", "--cwd", f.Project });
                    MuxAssert.Contains("\"name\":\"build-cmd\"", json.StdOut.Replace(" ", string.Empty), "json");
                    MuxAssert.Contains("builds use make", InvokeCli(new[] { "memory", "show", "build-cmd", "--cwd", f.Project }).StdOut, "show");
                    MuxAssert.AreEqual(1, InvokeCli(new[] { "memory", "show", "nope", "--cwd", f.Project }).ExitCode, "show unknown");
                    MuxAssert.AreEqual(1, InvokeCli(new[] { "memory", "add" }).ExitCode, "add needs text");
                    MuxAssert.AreEqual(1, InvokeCli(new[] { "memory", "frob" }).ExitCode, "unknown action");
                    MuxAssert.AreEqual(0, InvokeCli(new[] { "memory", "delete", "build-cmd", "--cwd", f.Project }).ExitCode, "deleted");
                    MuxAssert.AreEqual(1, InvokeCli(new[] { "memory", "delete", "build-cmd", "--cwd", f.Project }).ExitCode, "second delete fails");
                    MuxAssert.Contains("No memories.", InvokeCli(new[] { "memory", "list", "--cwd", f.Project }).StdOut, "empty");
                }
                finally
                {
                    Environment.SetEnvironmentVariable("MUX_CONFIG_DIR", original);
                }
            });
            Add("RestMemoryRoutes", "The memory REST routes list, save, search, and delete, with auth and input errors", (MemoryFixture f) =>
            {
                string? original = Environment.GetEnvironmentVariable("MUX_CONFIG_DIR");
                Environment.SetEnvironmentVariable("MUX_CONFIG_DIR", f.Config);
                RestServerSettings rest = new RestServerSettings { Hostname = "127.0.0.1", ApiKey = "memkey" };
                MuxServer? server = null;
                int port = 0;
                for (int attempt = 0; attempt < 10 && server == null; attempt++)
                {
                    port = FreePort();
                    rest.Port = port;
                    MuxServer candidate = new MuxServer(rest, "9.9.9-test", new SessionStore(Path.Combine(f.Root, "sessions")), () => new List<EndpointConfig>(), null);
                    try { candidate.Start(); server = candidate; }
                    catch (Exception) { candidate.Dispose(); Thread.Sleep(50); }
                }

                MuxAssert.IsNotNull(server, "server started");
                string baseUrl = "http://127.0.0.1:" + port + "/v1.0/api/memory";
                string dirQuery = "workingDirectory=" + Uri.EscapeDataString(f.Project);
                try
                {
                    using (HttpClient http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) })
                    {
                        for (int i = 0; i < 20; i++) { try { http.GetAsync("http://127.0.0.1:" + port + "/v1.0/api/health").GetAwaiter().GetResult(); break; } catch (Exception) { Thread.Sleep(100); } }
                        MuxAssert.AreEqual(401, (int)http.GetAsync(baseUrl).GetAwaiter().GetResult().StatusCode, "no key");
                        http.DefaultRequestHeaders.Add("Authorization", "Bearer memkey");
                        string body = JsonSerializer.Serialize(new { Name = "Port", Description = "API listens on 8080", Scope = "project", WorkingDirectory = f.Project });
                        HttpResponseMessage created = http.PostAsync(baseUrl, new StringContent(body, Encoding.UTF8, "application/json")).GetAwaiter().GetResult();
                        MuxAssert.AreEqual(201, (int)created.StatusCode, "created");
                        HttpResponseMessage updated = http.PostAsync(baseUrl, new StringContent(body, Encoding.UTF8, "application/json")).GetAwaiter().GetResult();
                        MuxAssert.AreEqual(200, (int)updated.StatusCode, "updated");
                        string listed = http.GetStringAsync(baseUrl + "?" + dirQuery).GetAwaiter().GetResult();
                        MuxAssert.Contains("\"Name\":\"port\"", listed, "listed");
                        MuxAssert.Contains("\"Enabled\":true", listed, "enabled flag");
                        MuxAssert.Contains("\"Memories\":[]", http.GetStringAsync(baseUrl + "?" + dirQuery + "&query=nothing").GetAwaiter().GetResult(), "search miss");
                        MuxAssert.AreEqual(404, (int)http.GetAsync(baseUrl + "?" + dirQuery + "&name=nope").GetAwaiter().GetResult().StatusCode, "name miss");
                        MuxAssert.AreEqual(400, (int)http.PostAsync(baseUrl, new StringContent("{\"Description\":\"x\"}", Encoding.UTF8, "application/json")).GetAwaiter().GetResult().StatusCode, "missing name");
                        MuxAssert.AreEqual(400, (int)http.PostAsync(baseUrl, new StringContent("{not json", Encoding.UTF8, "application/json")).GetAwaiter().GetResult().StatusCode, "bad json");
                        MuxAssert.AreEqual(400, (int)http.PostAsync(baseUrl, new StringContent(JsonSerializer.Serialize(new { Name = "x", Description = "y", Scope = "team" }), Encoding.UTF8, "application/json")).GetAwaiter().GetResult().StatusCode, "bad scope");
                        MuxAssert.AreEqual(400, (int)http.GetAsync(baseUrl + "?workingDirectory=" + Uri.EscapeDataString(Path.Combine(f.Root, "missing"))).GetAwaiter().GetResult().StatusCode, "missing directory");
                        MuxAssert.AreEqual(400, (int)http.DeleteAsync(baseUrl).GetAwaiter().GetResult().StatusCode, "delete needs a name");
                        MuxAssert.AreEqual(200, (int)http.DeleteAsync(baseUrl + "?name=port&" + dirQuery).GetAwaiter().GetResult().StatusCode, "deleted");
                        MuxAssert.AreEqual(404, (int)http.DeleteAsync(baseUrl + "?name=port&" + dirQuery).GetAwaiter().GetResult().StatusCode, "already gone");
                    }
                }
                finally
                {
                    server?.Stop();
                    server?.Dispose();
                    Environment.SetEnvironmentVariable("MUX_CONFIG_DIR", original);
                }
            });

            // --- settings ---
            Add("MemorySettings", "memoryEnabled and memoryMaxBytes default, clamp, and round-trip", (MemoryFixture f) =>
            {
                MuxSettings defaults = new MuxSettings();
                MuxAssert.IsTrue(defaults.MemoryEnabled, "enabled by default");
                MuxAssert.AreEqual(16384, defaults.MemoryMaxBytes, "default budget");
                MuxAssert.AreEqual(0, new MuxSettings { MemoryMaxBytes = -1 }.MemoryMaxBytes, "floor");
                MuxAssert.AreEqual(262144, new MuxSettings { MemoryMaxBytes = int.MaxValue }.MemoryMaxBytes, "ceiling");
                MuxSettings parsed = JsonSerializer.Deserialize<MuxSettings>("{\"memoryEnabled\":false,\"memoryMaxBytes\":2048}")!;
                MuxAssert.IsFalse(parsed.MemoryEnabled, "disabled from JSON");
                MuxAssert.AreEqual(2048, parsed.MemoryMaxBytes, "budget from JSON");
            });

            return new TestSuiteDescriptor(SuiteId, "Persistent memory: store, prompt index, tools, terminal, CLI, and REST", cases);
        }

        #endregion

        #region Private-Methods

        private static JsonElement Json(object value)
        {
            using (JsonDocument document = JsonDocument.Parse(JsonSerializer.Serialize(value)))
            {
                return document.RootElement.Clone();
            }
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

        private static CliInvocationResult InvokeCli(string[] args)
        {
            TextWriter originalOut = Console.Out;
            TextWriter originalErr = Console.Error;
            StringWriter stdout = new StringWriter();
            StringWriter stderr = new StringWriter();
            try
            {
                Console.SetOut(stdout);
                Console.SetError(stderr);
                int exitCode = Mux.Cli.Program.Main(args);
                return new CliInvocationResult(exitCode, stdout.ToString(), stderr.ToString());
            }
            finally
            {
                Console.SetOut(originalOut);
                Console.SetError(originalErr);
            }
        }

        private static int FreePort()
        {
            TcpListener listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
            listener.Start();
            int port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        #endregion
    }
}
