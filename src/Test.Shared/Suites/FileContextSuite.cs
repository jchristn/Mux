namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Context;
    using Mux.Core.Models;
    using Mux.Core.Prompting;
    using Mux.Core.Settings;
    using Mux.Core.Tools;
    using Mux.Core.Tools.Tools;
    using Touchstone.Core;

    /// <summary>
    /// Touchstone suite for the large-file context feature: the map, summarize, and truncate modes of
    /// <see cref="FileContextBuilder"/>; the content-hash summary cache and its eviction; and the agent-facing
    /// <c>read_file</c> path returning a map instead of a flat refusal (with truncate mode preserving the strict
    /// refusal and an explicit range still paging). Model calls are faked through the summarizer's sidecar
    /// delegate, so no live endpoint is needed.
    /// </summary>
    public static class FileContextSuite
    {
        private const string SampleCode =
            "namespace Demo\n" +
            "{\n" +
            "    public class Alpha\n" +
            "    {\n" +
            "        public void One() { }\n" +
            "    }\n" +
            "}\n" +
            "\n" +
            "public class Beta\n" +
            "{\n" +
            "    public void Two() { }\n" +
            "}\n" +
            "\n" +
            "public class Gamma\n" +
            "{\n" +
            "    public void Three() { }\n" +
            "}\n";

        /// <summary>
        /// Builds the file-context suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/>.</returns>
        public static TestSuiteDescriptor Create()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>
            {
                Case("MapEmitsLineRangedOutline", "Map mode emits a structural outline with line ranges plus the first lines", async (string dir, CancellationToken ct) =>
                {
                    FileContextBuilder builder = new FileContextBuilder(null);
                    FileContextResult result = await builder.BuildAsync(
                        new FileContextRequest("demo.cs", SampleCode, FileContextMode.Map, 1, 5, 400, null, string.Empty), null, ct).ConfigureAwait(false);

                    MuxAssert.IsFalse(result.Inlined, "not inlined");
                    MuxAssert.AreEqual(FileContextMode.Map, result.Mode, "map mode");
                    MuxAssert.IsTrue(result.OutlineEntryCount >= 3, "several outline entries");
                    MuxAssert.Contains("Structural map", result.Text, "map header");
                    MuxAssert.Contains("lines ", result.Text, "line ranges");
                    MuxAssert.Contains("First 5 lines:", result.Text, "head lines section");
                }),

                Case("InlineWhenBelowThreshold", "A file at or below the inline threshold is returned whole with line numbers", async (string dir, CancellationToken ct) =>
                {
                    FileContextBuilder builder = new FileContextBuilder(null);
                    FileContextResult result = await builder.BuildAsync(
                        new FileContextRequest("demo.cs", SampleCode, FileContextMode.Map, 1_000_000, 5, 400, null, string.Empty), null, ct).ConfigureAwait(false);

                    MuxAssert.IsTrue(result.Inlined, "inlined");
                    MuxAssert.Contains("namespace Demo", result.Text, "content present");
                    MuxAssert.IsFalse(result.Text.Contains("Structural map"), "no map header when inlined");
                }),

                Case("SummarizeProducesPointerBearingSummary", "Summarize mode emits a summary carrying line-range pointers", async (string dir, CancellationToken ct) =>
                {
                    FileSummarizer summarizer = new FileSummarizer((system, user, token) => Task.FromResult("auth handling — lines 9-12"), null);
                    FileContextBuilder builder = new FileContextBuilder(null);
                    FileContextResult result = await builder.BuildAsync(
                        new FileContextRequest("demo.cs", SampleCode, FileContextMode.Summarize, 1, 5, 400, null, "test-model"), summarizer, ct).ConfigureAwait(false);

                    MuxAssert.AreEqual(FileContextMode.Summarize, result.Mode, "summarize mode");
                    MuxAssert.Contains("Summary:", result.Text, "summary header");
                    MuxAssert.Contains("lines 9-12", result.Text, "pointer preserved");
                }),

                Case("SummarizeFallsBackToMapWithoutSummarizer", "Summarize mode with no summarizer falls back to a map", async (string dir, CancellationToken ct) =>
                {
                    FileContextBuilder builder = new FileContextBuilder(null);
                    FileContextResult result = await builder.BuildAsync(
                        new FileContextRequest("demo.cs", SampleCode, FileContextMode.Summarize, 1, 5, 400, null, string.Empty), null, ct).ConfigureAwait(false);
                    MuxAssert.AreEqual(FileContextMode.Map, result.Mode, "fell back to map");
                }),

                Case("SummaryCacheHitsByHashAndMissesAfterEdit", "The summarizer caches by content hash and re-runs after an edit", async (string dir, CancellationToken ct) =>
                {
                    string cacheDir = Path.Combine(dir, "sumcache");
                    FileSummaryCache cache = new FileSummaryCache(cacheDir);
                    int calls = 0;
                    FileSummarizer summarizer = new FileSummarizer((system, user, token) => { calls++; return Task.FromResult("note lines 1-3"); }, cache);

                    string a1 = await summarizer.SummarizeAsync("f.cs", SampleCode, 1000, "m", ct).ConfigureAwait(false);
                    MuxAssert.AreEqual(1, calls, "first run calls the model");
                    string a2 = await summarizer.SummarizeAsync("f.cs", SampleCode, 1000, "m", ct).ConfigureAwait(false);
                    MuxAssert.AreEqual(1, calls, "second run is a cache hit");
                    MuxAssert.AreEqual(a1, a2, "same summary");

                    string edited = SampleCode + "\npublic class Delta { }\n";
                    await summarizer.SummarizeAsync("f.cs", edited, 1000, "m", ct).ConfigureAwait(false);
                    MuxAssert.AreEqual(2, calls, "edited content misses the cache");
                }),

                Case("CacheEvictionDeletesOldKeepsFresh", "Eviction deletes entries past the retention window and keeps fresh ones", (string dir, CancellationToken ct) =>
                {
                    string cacheDir = Path.Combine(dir, "evict");
                    FileSummaryCache cache = new FileSummaryCache(cacheDir);
                    string oldKey = FileSummaryCache.BuildKey("old.cs", "hash-old", "summarize", "m");
                    string freshKey = FileSummaryCache.BuildKey("fresh.cs", "hash-fresh", "summarize", "m");

                    cache.Put(oldKey, "old summary");
                    // Age the just-written entry well past the retention window.
                    foreach (string file in Directory.GetFiles(cacheDir, "*.txt"))
                    {
                        File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddDays(-30));
                    }
                    cache.Put(freshKey, "fresh summary");

                    int deleted = cache.Evict(7);
                    MuxAssert.AreEqual(1, deleted, "one old entry deleted");
                    MuxAssert.IsFalse(cache.TryGet(oldKey, out _), "old entry gone");
                    MuxAssert.IsTrue(cache.TryGet(freshKey, out string fresh), "fresh entry kept");
                    MuxAssert.AreEqual("fresh summary", fresh, "fresh content intact");
                    return Task.CompletedTask;
                }),

                Case("AgentReadFileLargeReturnsMapUnderDefaultMode", "read_file returns a structural map (not a refusal) for a large file under the default mode", async (string dir, CancellationToken ct) =>
                {
                    int savedMax = ToolSafetyLimits.MaxReadFileBytes;
                    ToolSafetyLimits.MaxReadFileBytes = 20;
                    try
                    {
                        string path = Path.Combine(dir, "big.cs");
                        File.WriteAllText(path, SampleCode);
                        ReadFileTool tool = new ReadFileTool();
                        ToolResult result = await tool.ExecuteAsync("tc", Args(path), dir, ct).ConfigureAwait(false);
                        MuxAssert.IsTrue(result.Success, "map is a successful result, not a refusal");
                        MuxAssert.Contains("Structural map", result.Content ?? string.Empty, "returned a map");
                        MuxAssert.IsFalse((result.Content ?? string.Empty).Contains("file_too_large"), "no refusal");
                    }
                    finally
                    {
                        ToolSafetyLimits.MaxReadFileBytes = savedMax;
                    }
                }),

                Case("AgentReadFileTruncateModeStillRefuses", "read_file preserves the strict refusal under truncate mode", async (string dir, CancellationToken ct) =>
                {
                    int savedMax = ToolSafetyLimits.MaxReadFileBytes;
                    ToolSafetyLimits.MaxReadFileBytes = 20;
                    try
                    {
                        MuxSettings settings = new MuxSettings();
                        settings.Context.LargeFileMode = "truncate";

                        string path = Path.Combine(dir, "big.cs");
                        File.WriteAllText(path, SampleCode);
                        ReadFileTool tool = new ReadFileTool(settings);
                        ToolResult result = await tool.ExecuteAsync("tc", Args(path), dir, ct).ConfigureAwait(false);
                        MuxAssert.IsFalse(result.Success, "truncate mode refuses");
                        MuxAssert.Contains("file_too_large", result.Content ?? string.Empty, "refusal error");
                    }
                    finally
                    {
                        ToolSafetyLimits.MaxReadFileBytes = savedMax;
                    }
                }),

                Case("AgentReadFileExplicitRangeStillPagesLargeFile", "read_file honors an explicit offset/limit range on a large file instead of mapping", async (string dir, CancellationToken ct) =>
                {
                    int savedMax = ToolSafetyLimits.MaxReadFileBytes;
                    ToolSafetyLimits.MaxReadFileBytes = 20;
                    try
                    {
                        string path = Path.Combine(dir, "big.cs");
                        File.WriteAllText(path, SampleCode);
                        ReadFileTool tool = new ReadFileTool();
                        JsonElement args = JsonDocument.Parse("{\"file_path\":" + JsonSerializer.Serialize(path) + ",\"offset\":1,\"limit\":2}").RootElement;
                        ToolResult result = await tool.ExecuteAsync("tc", args, dir, ct).ConfigureAwait(false);
                        MuxAssert.IsTrue(result.Success, "paged read succeeds");
                        MuxAssert.IsFalse((result.Content ?? string.Empty).Contains("Structural map"), "range paged, not mapped");
                        MuxAssert.Contains("namespace Demo", result.Content ?? string.Empty, "first line present");
                    }
                    finally
                    {
                        ToolSafetyLimits.MaxReadFileBytes = savedMax;
                    }
                }),

                Case("ResolveInlineThresholdScalesWithWindow", "The inline threshold scales with the endpoint context window and falls back to bytes when unknown", (string dir, CancellationToken ct) =>
                {
                    ContextSettings ctx = new ContextSettings();
                    MuxAssert.AreEqual(4096, ctx.ResolveInlineThresholdBytes(0, 3.5, 4096), "fallback when window unknown");
                    MuxAssert.AreEqual((int)(0.25 * 100000 * 3.5), ctx.ResolveInlineThresholdBytes(100000, 3.5, 4096), "scales with window");
                    MuxAssert.AreEqual(1024, ctx.ResolveInlineThresholdBytes(100, 3.5, 4096), "floored at 1024");
                    return Task.CompletedTask;
                }),

                Case("AgentReadFileMapsWhenWindowSmallInlinesWhenLarge", "read_file maps a mid-size file under a small context window but inlines it under a large one", async (string dir, CancellationToken ct) =>
                {
                    // ~1.7 KB of content; no ToolSafetyLimits mutation, so only the window-derived cap decides.
                    string content = string.Concat(System.Linq.Enumerable.Repeat("public void Method();\n", 80));
                    string path = Path.Combine(dir, "mid.cs");
                    File.WriteAllText(path, content);

                    // Small window (1000 tokens -> ~875-byte inline cap) => the file is mapped.
                    ReadFileTool small = new ReadFileTool(new MuxSettings(), 1000);
                    ToolResult smallResult = await small.ExecuteAsync("tc", Args(path), dir, ct).ConfigureAwait(false);
                    MuxAssert.IsTrue(smallResult.Success, "small-window read succeeds");
                    MuxAssert.Contains("Structural map", smallResult.Content ?? string.Empty, "small window maps the file");

                    // Large window (200000 tokens -> ~175 KB inline cap) => the same file inlines whole.
                    ReadFileTool large = new ReadFileTool(new MuxSettings(), 200000);
                    ToolResult largeResult = await large.ExecuteAsync("tc", Args(path), dir, ct).ConfigureAwait(false);
                    MuxAssert.IsTrue(largeResult.Success, "large-window read succeeds");
                    MuxAssert.IsFalse((largeResult.Content ?? string.Empty).Contains("Structural map"), "large window inlines the file");
                    MuxAssert.Contains("public void Method();", largeResult.Content ?? string.Empty, "inlined content present");
                })
            };

            return new TestSuiteDescriptor("FileContext", "Large-file context: map, summarize, cache, and the agent read_file path", cases);
        }

        private static JsonElement Args(string path)
        {
            return JsonDocument.Parse("{\"file_path\":" + JsonSerializer.Serialize(path) + "}").RootElement;
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, Func<string, CancellationToken, Task> body)
        {
            return new TestCaseDescriptor("FileContext", caseId, displayName, async (CancellationToken ct) =>
            {
                string tempDir = Path.Combine(Path.GetTempPath(), "mux_filectx_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(tempDir);
                string? originalConfigDir = Environment.GetEnvironmentVariable("MUX_CONFIG_DIR");
                Environment.SetEnvironmentVariable("MUX_CONFIG_DIR", tempDir);
                PromptResolver.Invalidate();
                try
                {
                    await body(tempDir, ct).ConfigureAwait(false);
                }
                finally
                {
                    Environment.SetEnvironmentVariable("MUX_CONFIG_DIR", originalConfigDir);
                    PromptResolver.Invalidate();
                    try
                    {
                        if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
                    }
                    catch (IOException)
                    {
                    }
                }
            });
        }
    }
}
