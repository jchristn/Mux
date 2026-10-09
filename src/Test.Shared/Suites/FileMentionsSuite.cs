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
    using Mux.Core.Agent;
    using Mux.Core.Context;
    using Mux.Core.Enums;
    using Mux.Core.Jobs;
    using Mux.Core.Models;
    using Mux.Core.Sessions;
    using Mux.Server;
    using Test.Shared.Support;
    using Touchstone.Core;
    using TUIKit.Terminal;

    /// <summary>
    /// Touchstone suite for <c>@path</c> file mentions (Phase 6, row 19): parsing (plain, quoted, directories,
    /// trailing punctuation, emails, the <c>@@</c> escape, duplicates), resolution (relative and nested paths, refusal
    /// outside the working directory, missing and binary files, structural maps for large files, the
    /// <c>fileMentionMaxBytes</c> budget), completion ranking and exclusions, the terminal composer's completion and
    /// submit, <c>mux print</c> end to end, and the <c>GET /v1.0/api/files/complete</c> route. Positive and negative
    /// cases throughout.
    /// </summary>
    public static class FileMentionsSuite
    {
        #region Private-Members

        private const string SuiteId = "FileMentions";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Builds the file mentions suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/> for the file mention cases.</returns>
        public static TestSuiteDescriptor Create()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();
            void Add(string id, string name, Action body)
            {
                cases.Add(new TestCaseDescriptor(SuiteId, id, name, (CancellationToken ct) => { body(); return Task.CompletedTask; }));
            }

            void AddProject(string id, string name, Func<string, CancellationToken, Task> body)
            {
                cases.Add(new TestCaseDescriptor(SuiteId, id, name, (CancellationToken ct) => WithProjectAsync((string root) => body(root, ct))));
            }

            // --- parsing ---
            Add("ParsePlainQuotedAndDirectory", "Plain, quoted, and directory mentions are found with their positions", () =>
            {
                string text = "look at @src/app.ts and @\"My Docs/notes.md\" plus @src/";
                List<FileMention> mentions = FileMentionResolver.Parse(text);
                MuxAssert.AreEqual(3, mentions.Count, "three mentions");
                MuxAssert.AreEqual("src/app.ts", mentions[0].Path, "plain path");
                MuxAssert.AreEqual(8, mentions[0].Start, "start index");
                MuxAssert.AreEqual("@src/app.ts", text.Substring(mentions[0].Start, mentions[0].Length), "length covers the raw text");
                MuxAssert.AreEqual("My Docs/notes.md", mentions[1].Path, "quoted path keeps spaces");
                MuxAssert.IsTrue(mentions[1].Quoted, "quoted flag");
                MuxAssert.AreEqual("@\"My Docs/notes.md\"", mentions[1].Raw, "raw includes quotes");
                MuxAssert.IsTrue(mentions[2].IsDirectoryHint && mentions[2].Path == "src/", "directory hint");
                MuxAssert.IsFalse(mentions[0].IsDirectoryHint, "file has no directory hint");
            });
            Add("ParseStripsTrailingPunctuation", "Trailing punctuation and closing brackets are not part of the path", () =>
            {
                List<FileMention> mentions = FileMentionResolver.Parse("see @a.ts, @b.ts. (@c.ts) @d.ts! @e.ts?\n@f.md:");
                MuxAssert.AreEqual("a.ts,b.ts,c.ts,d.ts,e.ts,f.md", string.Join(",", mentions.ConvertAll(m => m.Path)), "paths");
            });
            Add("ParseIgnoresNonMentions", "Emails, @@ escapes, bare @, unclosed quotes, and mid-word @ are not mentions", () =>
            {
                foreach (string text in new[] { "mail joel@example.com", "type @@literal here", "an @ alone", "ends with @", "x@y.z", "@\"unclosed path", "@@", "@\"   \"", "@/", "price: 5@3" })
                {
                    MuxAssert.AreEqual(0, FileMentionResolver.Parse(text).Count, "no mention in '" + text + "'");
                }

                MuxAssert.AreEqual(0, FileMentionResolver.Parse(null).Count, "null text");
                MuxAssert.AreEqual(1, FileMentionResolver.Parse("(@a.ts)").Count, "after an opening bracket");
                MuxAssert.AreEqual(1, FileMentionResolver.Parse("\"@a.ts\"").Count, "after a quote");
                MuxAssert.AreEqual(2, FileMentionResolver.Parse("@a.ts @a.ts").Count, "duplicates parse twice");
            });
            Add("ActiveTokenAndFormatting", "The composer's active @ token is found, and completions are formatted as mentions", () =>
            {
                MuxAssert.IsTrue(FileMentionResolver.TryGetActiveToken("open @src/ap", out int start, out string query), "active");
                MuxAssert.AreEqual(5, start, "token start");
                MuxAssert.AreEqual("src/ap", query, "query");
                MuxAssert.IsTrue(FileMentionResolver.TryGetActiveToken("@", out int bare, out string empty) && bare == 0 && empty.Length == 0, "bare @ starts completion");
                MuxAssert.IsTrue(FileMentionResolver.TryGetActiveToken("(@x", out _, out string bracketed) && bracketed == "x", "after a bracket");
                MuxAssert.IsFalse(FileMentionResolver.TryGetActiveToken("joel@exa", out _, out _), "email is not a mention");
                MuxAssert.IsFalse(FileMentionResolver.TryGetActiveToken("@@lit", out _, out _), "escape is not a mention");
                MuxAssert.IsFalse(FileMentionResolver.TryGetActiveToken("@src/app.ts ", out _, out _), "a finished mention is not active");
                MuxAssert.IsFalse(FileMentionResolver.TryGetActiveToken(null, out _, out _), "null");
                MuxAssert.AreEqual("@src/app.ts", FileMentionResolver.FormatMention("src/app.ts"), "plain");
                MuxAssert.AreEqual("@\"My Docs/a.md\"", FileMentionResolver.FormatMention("My Docs/a.md"), "quoted when it has spaces");
            });

            // --- resolution ---
            AddProject("ResolvesNestedFile", "A nested file is attached with line numbers in a delimited block after the prompt", async (string root, CancellationToken ct) =>
            {
                Write(root, "src/app.ts", "export const a = 1;\nexport const b = 2;\n");
                FileMentionResult result = await new FileMentionResolver(root).ResolveAsync("explain @src/app.ts please", ct).ConfigureAwait(false);
                MuxAssert.IsTrue(result.HadMentions, "had mentions");
                MuxAssert.AreEqual(1, result.Attachments.Count, "one attachment");
                FileMentionAttachment attachment = result.Attachments[0];
                MuxAssert.AreEqual("src/app.ts", attachment.RelativePath, "relative path");
                MuxAssert.AreEqual(FileMentionKindEnum.File, attachment.Kind, "file kind");
                MuxAssert.IsTrue(attachment.Inlined, "inlined");
                MuxAssert.AreEqual("full, 2 lines", attachment.Summary, "summary");
                MuxAssert.IsTrue(result.Prompt.StartsWith("explain @src/app.ts please\n\n<mentioned-files>", StringComparison.Ordinal), "prompt kept, block appended");
                MuxAssert.Contains("<file path=\"src/app.ts\" lines=\"2\" view=\"full\">", result.Prompt, "file tag");
                MuxAssert.Contains("     1\texport const a = 1;", result.Prompt, "numbered line");
                MuxAssert.Contains("</file>\n</mentioned-files>", result.Prompt, "closed");
                MuxAssert.AreEqual(result.Block, result.Prompt.Substring(result.Prompt.IndexOf("<mentioned-files>", StringComparison.Ordinal)), "block exposed alone");
                MuxAssert.AreEqual(attachment.Bytes, result.TotalBytes, "total bytes");
                MuxAssert.AreEqual("Attached src/app.ts (full, 2 lines)", FileMentionResolver.Describe(result)[0], "description");
            });
            AddProject("ResolvesDirectoryListing", "A directory mention attaches a listing, directories first, without dependency folders", async (string root, CancellationToken ct) =>
            {
                Write(root, "web/index.html", "<html></html>");
                Write(root, "web/components/Button.tsx", "x");
                Write(root, "web/node_modules/react/index.js", "x");
                Directory.CreateDirectory(Path.Combine(root, "empty"));
                FileMentionResult result = await new FileMentionResolver(root).ResolveAsync("what is in @web/ and @empty", ct).ConfigureAwait(false);
                MuxAssert.AreEqual(2, result.Attachments.Count, "two listings");
                FileMentionAttachment web = result.Attachments[0];
                MuxAssert.AreEqual(FileMentionKindEnum.Directory, web.Kind, "directory kind");
                MuxAssert.AreEqual("web/", web.RelativePath, "trailing slash");
                MuxAssert.IsTrue(web.Text.StartsWith("components/\n", StringComparison.Ordinal), "directories first: " + web.Text);
                MuxAssert.Contains("index.html  (13 B)", web.Text, "file with size");
                MuxAssert.DoesNotContain("node_modules", web.Text, "dependency folder skipped");
                MuxAssert.AreEqual("directory, 2 entries", web.Summary, "summary");
                MuxAssert.Contains("<directory path=\"web/\" entries=\"2\">", result.Prompt, "directory tag");
                MuxAssert.Contains("(empty)", result.Attachments[1].Text, "empty directory");
            });
            AddProject("RefusesOutsideAndMissing", "Paths outside the working directory, missing paths, and a file named as a directory are left as typed", async (string root, CancellationToken ct) =>
            {
                string project = Path.Combine(root, "project");
                Write(project, "a.ts", "a");
                Write(root, "secret.txt", "TOP SECRET");
                string outside = Path.Combine(root, "secret.txt");
                FileMentionResult result = await new FileMentionResolver(project).ResolveAsync("read @../secret.txt and @" + outside + " and @nope.ts and @a.ts/", ct).ConfigureAwait(false);
                MuxAssert.AreEqual(0, result.Attachments.Count, "nothing attached");
                MuxAssert.AreEqual("read @../secret.txt and @" + outside + " and @nope.ts and @a.ts/", result.Prompt, "prompt unchanged");
                MuxAssert.AreEqual(string.Empty, result.Block, "no block");
                string unresolved = string.Join("\n", result.Unresolved);
                MuxAssert.Contains("@../secret.txt: outside the working directory, not attached", unresolved, "relative escape refused");
                MuxAssert.Contains("@" + outside + ": outside the working directory", unresolved, "absolute escape refused");
                MuxAssert.Contains("@nope.ts: not found", unresolved, "missing");
                MuxAssert.Contains("@a.ts/: is a file, not a directory", unresolved, "file named as a directory");
                MuxAssert.DoesNotContain("TOP SECRET", result.Prompt, "secret not leaked");
                MuxAssert.Contains("Left as typed: @nope.ts: not found", string.Join("\n", FileMentionResolver.Describe(result)), "described");
            });
            AddProject("SkipsBinaryAndDeduplicates", "Binary files are skipped, the same file is attached once, and an absolute path inside the root works", async (string root, CancellationToken ct) =>
            {
                File.WriteAllBytes(Path.Combine(root, "image.png"), new byte[] { 137, 80, 78, 71, 0, 0, 1 });
                Write(root, "a.ts", "one");
                FileMentionResult result = await new FileMentionResolver(root).ResolveAsync("@image.png @a.ts @./a.ts @" + Path.Combine(root, "a.ts"), ct).ConfigureAwait(false);
                MuxAssert.AreEqual(1, result.Attachments.Count, "attached once");
                MuxAssert.Contains("@image.png: binary file, not attached", string.Join("\n", result.Unresolved), "binary skipped");
            });
            AddProject("LargeFileBecomesMap", "A file above the inline threshold is attached as a structural map with line ranges", async (string root, CancellationToken ct) =>
            {
                StringBuilder big = new StringBuilder();
                big.Append("public class Big\n{\n");
                for (int m = 0; m < 4; m++)
                {
                    big.Append("    public void Method").Append(m).Append("()\n    {\n");
                    for (int i = 0; i < 95; i++) big.Append("        total = total + ").Append(i).Append(" * factor;\n");
                    big.Append("    }\n");
                }

                big.Append("}\n");
                Write(root, "Big.cs", big.ToString());
                FileMentionResult result = await new FileMentionResolver(root, FileMentionResolver.DefaultMaxBytes, 2048).ResolveAsync("@Big.cs", ct).ConfigureAwait(false);
                FileMentionAttachment attachment = result.Attachments[0];
                MuxAssert.IsFalse(attachment.Inlined, "not inlined");
                MuxAssert.Contains("structural map, ", attachment.Summary, "summary");
                MuxAssert.Contains("view=\"map\"", result.Prompt, "map view");
                MuxAssert.Contains("Structural map", attachment.Text, "map text");
                MuxAssert.IsTrue(attachment.Bytes < Encoding.UTF8.GetByteCount(big.ToString()), "smaller than the file");
            });
            AddProject("BudgetLimitsAttachments", "The byte budget falls back to a map, then leaves files out with a note; zero turns attachments off", async (string root, CancellationToken ct) =>
            {
                Write(root, "small.txt", "tiny");
                StringBuilder lines = new StringBuilder();
                for (int i = 0; i < 2000; i++) lines.Append("line number ").Append(i).Append(" with some padding text\n");
                Write(root, "large.txt", lines.ToString());
                FileMentionResult full = await new FileMentionResolver(root, FileMentionResolver.MaxBytesLimit, 1048576).ResolveAsync("@small.txt @large.txt", ct).ConfigureAwait(false);
                FileMentionResult mapOnly = await new FileMentionResolver(root, FileMentionResolver.DefaultMaxBytes, 1).ResolveAsync("@large.txt", ct).ConfigureAwait(false);
                int budget = full.Attachments[0].Bytes + mapOnly.Attachments[0].Bytes + 10;
                MuxAssert.IsTrue(budget < full.TotalBytes, "budget is below the full size");
                FileMentionResult capped = await new FileMentionResolver(root, budget, 1048576).ResolveAsync("@small.txt @large.txt", ct).ConfigureAwait(false);
                MuxAssert.AreEqual(2, capped.Attachments.Count, "both attached");
                MuxAssert.IsFalse(capped.Attachments[1].Inlined, "the large file fell back to a map to fit");
                MuxAssert.IsTrue(capped.TotalBytes <= budget, "within budget");

                FileMentionResult tight = await new FileMentionResolver(root, 60).ResolveAsync("@small.txt @large.txt", ct).ConfigureAwait(false);
                MuxAssert.AreEqual(1, tight.Attachments.Count, "only the small file fits");
                MuxAssert.Contains("@large.txt was not attached", string.Join("\n", tight.Notes), "note names the file");
                MuxAssert.Contains("fileMentionMaxBytes budget (60)", string.Join("\n", tight.Notes), "note names the setting");

                FileMentionResult off = await new FileMentionResolver(root, 0).ResolveAsync("@small.txt", ct).ConfigureAwait(false);
                MuxAssert.AreEqual(0, off.Attachments.Count, "nothing attached when off");
                MuxAssert.Contains("turned off", string.Join("\n", off.Notes), "off note");
                MuxAssert.AreEqual("@small.txt", off.Prompt, "prompt as typed");

                FileMentionResult none = await new FileMentionResolver(root).ResolveAsync("no mentions here", ct).ConfigureAwait(false);
                MuxAssert.IsFalse(none.HadMentions, "no mentions");
                MuxAssert.AreEqual("no mentions here", none.Prompt, "unchanged");
                MuxAssert.AreEqual(0, FileMentionResolver.Describe(none).Count, "nothing to describe");
                MuxAssert.Throws<ArgumentException>(() => new FileMentionResolver(" "), "blank working directory");
            });

            // --- completion ---
            AddProject("CompletionRanksAndExcludes", "Completion ranks exact, prefix, contains, and fuzzy matches and never offers dependency folders", async (string root, CancellationToken ct) =>
            {
                Write(root, "src/app.ts", "x");
                Write(root, "src/app.test.ts", "x");
                Write(root, "src/mapper.ts", "x");
                Write(root, "docs/APP_NOTES.md", "x");
                Write(root, "apps/readme.md", "x");
                Write(root, "node_modules/app/index.js", "x");
                Write(root, "bin/app.dll", "x");
                Write(root, ".git/app", "x");
                Write(root, "README.md", "x");
                FileMentionResolver resolver = new FileMentionResolver(root);
                List<string> app = resolver.Complete("app", 20);
                MuxAssert.IsTrue(app.Contains("src/app.ts") && app.Contains("apps/") && app.Contains("docs/APP_NOTES.md"), "matches: " + string.Join(", ", app));
                MuxAssert.IsFalse(app.Exists(p => p.StartsWith("node_modules", StringComparison.Ordinal) || p.StartsWith("bin", StringComparison.Ordinal) || p.StartsWith(".git", StringComparison.Ordinal)), "excluded folders");
                MuxAssert.IsTrue(app.IndexOf("src/app.ts") < app.IndexOf("src/mapper.ts"), "name prefix beats contains");
                MuxAssert.AreEqual("src/app.ts", resolver.Complete("src/app.ts", 5)[0], "exact match first");
                MuxAssert.AreEqual("src/app.ts", resolver.Complete("@src/app.ts".TrimStart('@'), 5)[0], "exact match first again");
                MuxAssert.Contains("src/app.ts", string.Join(",", resolver.Complete("sapts", 5)), "fuzzy subsequence");
                MuxAssert.AreEqual(0, resolver.Complete("zzzqqq", 5).Count, "no match");
                MuxAssert.AreEqual(2, resolver.Complete("ts", 2).Count, "max honored");
                List<string> top = resolver.Complete(string.Empty, 10);
                MuxAssert.AreEqual("apps/", top[0], "top level, directories first");
                MuxAssert.IsTrue(top.Contains("README.md"), "top-level file");
                MuxAssert.IsFalse(top.Contains("src/app.ts"), "nested paths are not top level");
                MuxAssert.AreEqual("src/app.ts", resolver.Complete("./src/app.ts", 1)[0], "leading ./ ignored");
                await Task.CompletedTask.ConfigureAwait(false);
            });
            Add("SettingDefaultsAndClamps", "fileMentionMaxBytes defaults to 256 KB, clamps, and round-trips through JSON", () =>
            {
                MuxAssert.AreEqual(262144, new MuxSettings().FileMentionMaxBytes, "default");
                MuxAssert.AreEqual(0, new MuxSettings { FileMentionMaxBytes = -5 }.FileMentionMaxBytes, "floor");
                MuxAssert.AreEqual(FileMentionResolver.MaxBytesLimit, new MuxSettings { FileMentionMaxBytes = int.MaxValue }.FileMentionMaxBytes, "ceiling");
                MuxAssert.AreEqual(1000, JsonSerializer.Deserialize<MuxSettings>("{\"fileMentionMaxBytes\":1000}")!.FileMentionMaxBytes, "from JSON");
            });

            // --- terminal ---
            AddProject("TerminalCompletesAndAttaches", "The composer offers @ completions, Tab accepts, and the submitted turn carries the attachment", async (string root, CancellationToken ct) =>
            {
                Write(root, "src/app.ts", "UNIQUE_APP_CONTENT\n");
                Write(root, "src/util.ts", "x\n");
                List<string> submitted = new List<string>();
                HeadlessBackend backend = new HeadlessBackend(160, 40);
                await using (JobManager manager = new JobManager((Job job, string prompt, CancellationToken token) => Record(submitted, prompt, token), maxConcurrency: 1))
                using (MuxTuiApp app = new MuxTuiApp(backend, manager, "demo", ApprovalPolicyEnum.AutoApprove, workingDirectory: root))
                {
                    Type(backend, app, "look at @sr");
                    MuxAssert.Contains("[src/]", string.Join("\n", app.FooterSnapshot()), "suggestion shown and highlighted");
                    Type(backend, app, "\t");
                    MuxAssert.AreEqual("look at @src/", app.ComposerText, "directory accepted and left open");
                    Type(backend, app, "ap");
                    MuxAssert.Contains("src/app.ts", string.Join("\n", app.FooterSnapshot()), "children offered");
                    Type(backend, app, "\t");
                    MuxAssert.AreEqual("look at @src/app.ts ", app.ComposerText, "file accepted with a space");
                    MuxAssert.DoesNotContain("Tab accept", string.Join("\n", app.FooterSnapshot()), "suggestions closed");
                    Type(backend, app, "now\r");
                    await app.DrainProjectorsAsync().ConfigureAwait(false);
                    MuxAssert.AreEqual(1, submitted.Count, "one turn");
                    MuxAssert.Contains("UNIQUE_APP_CONTENT", submitted[0], "file content reached the turn");
                    MuxAssert.Contains("<mentioned-files>", submitted[0], "delimited block");
                    string transcript = string.Join("\n", app.TranscriptSnapshot());
                    MuxAssert.Contains("Attached src/app.ts (full, 1 line)", transcript, "attachment notice");
                    MuxAssert.DoesNotContain("UNIQUE_APP_CONTENT", transcript, "transcript shows the prompt as typed");
                }
            });
            AddProject("TerminalEscapeAndUnknown", "Esc dismisses suggestions without clearing the text, and an unknown mention is reported", async (string root, CancellationToken ct) =>
            {
                Write(root, "src/app.ts", "x\n");
                List<string> submitted = new List<string>();
                HeadlessBackend backend = new HeadlessBackend(160, 40);
                await using (JobManager manager = new JobManager((Job job, string prompt, CancellationToken token) => Record(submitted, prompt, token), maxConcurrency: 1))
                using (MuxTuiApp app = new MuxTuiApp(backend, manager, "demo", ApprovalPolicyEnum.AutoApprove, workingDirectory: root))
                {
                    Type(backend, app, "@sr");
                    MuxAssert.Contains("Tab accept", string.Join("\n", app.FooterSnapshot()), "suggestions open");
                    Type(backend, app, "\u001b");
                    app.PumpInputOnce();
                    MuxAssert.DoesNotContain("Tab accept", string.Join("\n", app.FooterSnapshot()), "dismissed");
                    MuxAssert.AreEqual("@sr", app.ComposerText, "text kept");
                    Type(backend, app, "x");
                    MuxAssert.DoesNotContain("Tab accept", string.Join("\n", app.FooterSnapshot()), "stays dismissed for this mention");
                    Type(backend, app, " and @missing.ts\r");
                    await app.DrainProjectorsAsync().ConfigureAwait(false);
                    MuxAssert.AreEqual(1, submitted.Count, "submitted");
                    MuxAssert.DoesNotContain("<mentioned-files>", submitted[0], "nothing attached");
                    MuxAssert.Contains("Left as typed: @missing.ts: not found", string.Join("\n", app.TranscriptSnapshot()), "reported");
                }
            });

            // --- mux print ---
            AddProject("PrintAttachesMentions", "mux print resolves @ mentions so the file content reaches the model", async (string root, CancellationToken ct) =>
            {
                Write(root, "notes.txt", "ZEBRA_CONTENT_42\n");
                string configDir = Path.Combine(root, ".muxcfg");
                Directory.CreateDirectory(configDir);
                File.WriteAllText(Path.Combine(configDir, "settings.json"), "{\"skillsEnabled\":false}");
                using (MockHttpServer server = new MockHttpServer())
                {
                    server.RegisterStreamingResponse("summarize", new List<string> { AgentTestHarness.BuildTextSseChunk("It mentions a zebra.") });
                    server.Start();
                    CliInvocationResult result = InvokeCli(new[]
                    {
                        "print", "--config-dir", configDir, "--working-directory", root, "--yolo",
                        "--base-url", server.BaseUrl, "--model", "test-model", "--adapter-type", "openai-compatible",
                        "summarize @notes.txt and @gone.txt"
                    });
                    MuxAssert.AreEqual(0, result.ExitCode, "exit 0: " + result.StdErr);
                    string request = server.ReceivedRequests[server.ReceivedRequests.Count - 1];
                    MuxAssert.Contains("ZEBRA_CONTENT_42", request, "file content in the model request");
                    MuxAssert.Contains("mentioned-files", request, "delimited block");
                    MuxAssert.Contains("Attached notes.txt (full, 1 line)", result.StdErr, "attachment reported on stderr");
                    MuxAssert.Contains("Left as typed: @gone.txt: not found", result.StdErr, "missing reported");
                    MuxAssert.Contains("It mentions a zebra.", result.StdOut, "answer printed");
                }

                await Task.CompletedTask.ConfigureAwait(false);
            });

            // --- REST route ---
            AddProject("CompleteRoute", "GET /v1.0/api/files/complete returns ranked paths and mentions, with auth and input checks", async (string root, CancellationToken ct) =>
            {
                Write(root, "src/app.ts", "x");
                Write(root, "My Docs/app notes.md", "x");
                await RunRouteAsync(root, ct).ConfigureAwait(false);
            });

            return new TestSuiteDescriptor(SuiteId, "@file mentions: parsing, resolution, completion, terminal, print, REST", cases);
        }

        #endregion

        #region Private-Methods

        private static async Task RunRouteAsync(string root, CancellationToken ct)
        {
            string tempSessions = Path.Combine(root, ".sessions");
            MuxServer? server = null;
            try
            {
                RestServerSettings rest = new RestServerSettings { Hostname = "127.0.0.1", ApiKey = "testkey123" };
                List<EndpointConfig> endpoints = new List<EndpointConfig>();
                int port = 0;
                for (int attempt = 0; attempt < 10 && server == null; attempt++)
                {
                    port = StubHttpServer.FreeLoopbackPort();
                    rest.Port = port;
                    MuxServer candidate = new MuxServer(rest, "9.9.9-test", new SessionStore(tempSessions), () => endpoints, null);
                    try { candidate.Start(); server = candidate; }
                    catch (Exception) { candidate.Dispose(); Thread.Sleep(50); }
                }

                MuxAssert.IsNotNull(server, "server bound");
                string baseUrl = "http://127.0.0.1:" + port + "/v1.0/api/files/complete";
                using (HttpClient http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) })
                {
                    for (int attempt = 0; attempt < 20; attempt++)
                    {
                        try { await http.GetAsync("http://127.0.0.1:" + port + "/v1.0/api/health", ct).ConfigureAwait(false); break; }
                        catch (Exception) { await Task.Delay(100, ct).ConfigureAwait(false); }
                    }

                    using (HttpResponseMessage unauthorized = await http.GetAsync(baseUrl + "?prefix=app", ct).ConfigureAwait(false))
                    {
                        MuxAssert.AreEqual(401, (int)unauthorized.StatusCode, "no key is rejected");
                    }

                    http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "testkey123");
                    string query = "?prefix=" + Uri.EscapeDataString("@app") + "&workingDirectory=" + Uri.EscapeDataString(root) + "&max=5";
                    using (HttpResponseMessage ok = await http.GetAsync(baseUrl + query, ct).ConfigureAwait(false))
                    {
                        string body = await ok.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                        MuxAssert.AreEqual(200, (int)ok.StatusCode, "ok: " + body);
                        using (JsonDocument doc = JsonDocument.Parse(body))
                        {
                            List<string> paths = new List<string>();
                            foreach (JsonElement p in doc.RootElement.GetProperty("Paths").EnumerateArray()) paths.Add(p.GetString() ?? string.Empty);
                            MuxAssert.IsTrue(paths.Contains("src/app.ts"), "path returned: " + body);
                            List<string> mentions = new List<string>();
                            foreach (JsonElement m in doc.RootElement.GetProperty("Mentions").EnumerateArray()) mentions.Add(m.GetString() ?? string.Empty);
                            MuxAssert.IsTrue(mentions.Contains("@\"My Docs/app notes.md\""), "spaced path quoted as a mention: " + body);
                        }
                    }

                    using (HttpResponseMessage missing = await http.GetAsync(baseUrl + "?prefix=a&workingDirectory=" + Uri.EscapeDataString(Path.Combine(root, "nope")), ct).ConfigureAwait(false))
                    {
                        MuxAssert.AreEqual(400, (int)missing.StatusCode, "missing directory is 400");
                    }

                    using (HttpResponseMessage badMax = await http.GetAsync(baseUrl + "?prefix=a&max=0&workingDirectory=" + Uri.EscapeDataString(root), ct).ConfigureAwait(false))
                    {
                        MuxAssert.AreEqual(400, (int)badMax.StatusCode, "max out of range is 400");
                    }

                    // Regression: the instructions route decodes an encoded working directory (a path with a space).
                    string spaced = Path.Combine(root, "Spaced Dir");
                    Directory.CreateDirectory(spaced);
                    File.WriteAllText(Path.Combine(spaced, "AGENTS.md"), "Use tabs.\n");
                    using (HttpResponseMessage instructions = await http.GetAsync("http://127.0.0.1:" + port + "/v1.0/api/context/instructions?workingDirectory=" + Uri.EscapeDataString(spaced), ct).ConfigureAwait(false))
                    {
                        string body = await instructions.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                        MuxAssert.AreEqual(200, (int)instructions.StatusCode, "encoded directory accepted: " + body);
                        MuxAssert.Contains("AGENTS.md", body, "instruction file found");
                    }
                }
            }
            finally
            {
                server?.Stop();
                server?.Dispose();
            }
        }

        private static async IAsyncEnumerable<AgentEvent> Record(List<string> submitted, string prompt, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            lock (submitted)
            {
                submitted.Add(prompt);
            }

            await Task.CompletedTask.ConfigureAwait(false);
            yield return new AssistantTextEvent { Text = "ok" };
            yield return new RunCompletedEvent { RunId = Guid.NewGuid().ToString("N"), Status = "completed", IterationsCompleted = 1, DurationMs = 1 };
        }

        private static void Type(HeadlessBackend backend, MuxTuiApp app, string text)
        {
            backend.FeedInput(text);
            app.PumpInputOnce();
        }

        private static void Write(string root, string relative, string content)
        {
            string path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
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

        private static async Task WithProjectAsync(Func<string, Task> body)
        {
            string root = Path.Combine(Path.GetTempPath(), "mux-mentions-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                await body(Path.GetFullPath(root)).ConfigureAwait(false);
            }
            finally
            {
                try { Directory.Delete(root, true); } catch (Exception) { }
            }
        }

        #endregion
    }
}
