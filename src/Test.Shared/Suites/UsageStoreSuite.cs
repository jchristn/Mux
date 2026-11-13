namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Telemetry;
    using Touchstone.Core;

    /// <summary>
    /// Touchstone suite for the SQLite usage-telemetry store. Verifies schema creation, single and batched
    /// inserts, retention pruning, and — the load-bearing guarantee — that many concurrent writers (the
    /// stand-in for multiple mux instances sharing one WAL database) all land their rows with no loss and
    /// no corruption. Each case runs against a throwaway database in a temp directory.
    /// </summary>
    public static class UsageStoreSuite
    {
        private const string SuiteId = "UsageStore";

        /// <summary>
        /// Builds the usage-store suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/> for the usage-store cases.</returns>
        public static TestSuiteDescriptor Create()
        {
            return new TestSuiteDescriptor(
                SuiteId,
                "SQLite usage telemetry: schema, inserts, retention, and concurrent writers",
                new List<TestCaseDescriptor>
                {
                    StoreCase("AppliedEffortRoundTrips", "The reasoning effort actually sent is stored and read back with each call", async (SqliteUsageStore store, CancellationToken ct) =>
                    {
                        await store.InsertBatchAsync(new List<UsageEvent>
                        {
                            new UsageEvent { TimestampUnixMs = 1000, EndpointName = "o", AdapterType = "OpenAi", Model = "m", AppliedReasoningEffort = "xhigh" },
                            new UsageEvent { TimestampUnixMs = 2000, EndpointName = "o", AdapterType = "OpenAi", Model = "m" }
                        }, ct).ConfigureAwait(false);
                        List<UsageEventRow> rows = await store.QueryEventsAsync(null, 1, 10, ct).ConfigureAwait(false);
                        MuxAssert.AreEqual(2, rows.Count, "both calls");
                        MuxAssert.IsNull(rows[0].AppliedReasoningEffort, "newest call sent no effort");
                        MuxAssert.AreEqual("xhigh", rows[1].AppliedReasoningEffort, "applied effort stored");
                    }),
                    new TestCaseDescriptor(SuiteId, "MigratesVersionOneDatabase", "A mux 1.7 (schema v1) usage.db gains applied_effort and keeps its rows", async (CancellationToken ct) =>
                    {
                        string dir = Path.Combine(Path.GetTempPath(), "mux_usage_v1_" + Guid.NewGuid().ToString("N"));
                        Directory.CreateDirectory(dir);
                        string dbPath = Path.Combine(dir, "usage.db");
                        try
                        {
                            // The 1.7 schema, verbatim except for the indexes.
                            using (Microsoft.Data.Sqlite.SqliteConnection legacy = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=" + dbPath + ";Pooling=False"))
                            {
                                legacy.Open();
                                using Microsoft.Data.Sqlite.SqliteCommand create = legacy.CreateCommand();
                                create.CommandText = @"
CREATE TABLE schema_version (version INTEGER NOT NULL);
INSERT INTO schema_version (version) VALUES (1);
CREATE TABLE usage_events (
    id INTEGER PRIMARY KEY AUTOINCREMENT, ts_utc INTEGER NOT NULL, run_id TEXT, session_id TEXT, job_id TEXT,
    call_kind TEXT NOT NULL, command TEXT, endpoint_name TEXT NOT NULL, adapter_type TEXT NOT NULL, model TEXT NOT NULL,
    base_host TEXT, project TEXT, iteration INTEGER, input_tokens INTEGER NOT NULL DEFAULT 0,
    cached_tokens INTEGER NOT NULL DEFAULT 0, output_tokens INTEGER NOT NULL DEFAULT 0,
    reasoning_tokens INTEGER NOT NULL DEFAULT 0, total_tokens INTEGER NOT NULL DEFAULT 0, ttft_ms INTEGER,
    stream_ms INTEGER, total_ms INTEGER, tokens_per_sec REAL, finish_reason TEXT, success INTEGER NOT NULL DEFAULT 1,
    error_code TEXT, retry_count INTEGER NOT NULL DEFAULT 0, pricing_ver TEXT);
INSERT INTO usage_events (ts_utc, call_kind, endpoint_name, adapter_type, model, input_tokens, total_tokens)
VALUES (500, 'primary', 'legacy', 'OpenAiCompatible', 'old-model', 7, 9);";
                                create.ExecuteNonQuery();
                            }

                            // Opening twice exercises the idempotent migration path.
                            for (int open = 0; open < 2; open++)
                            {
                                using SqliteUsageStore store = new SqliteUsageStore(dbPath, 36500, 5_000_000);
                                if (open == 0)
                                {
                                    await store.InsertBatchAsync(new List<UsageEvent>
                                    {
                                        new UsageEvent { TimestampUnixMs = 1000, EndpointName = "o", AdapterType = "OpenAi", Model = "m", AppliedReasoningEffort = "max" }
                                    }, ct).ConfigureAwait(false);
                                }

                                List<UsageEventRow> rows = await store.QueryEventsAsync(null, 1, 10, ct).ConfigureAwait(false);
                                MuxAssert.AreEqual(2, rows.Count, $"open {open}: legacy and new rows");
                                MuxAssert.AreEqual("max", rows[0].AppliedReasoningEffort, $"open {open}: new row's effort");
                                MuxAssert.AreEqual("legacy", rows[1].EndpointName, $"open {open}: legacy row kept");
                                MuxAssert.IsNull(rows[1].AppliedReasoningEffort, $"open {open}: legacy row has no effort");
                            }

                            using Microsoft.Data.Sqlite.SqliteConnection check = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=" + dbPath + ";Pooling=False");
                            check.Open();
                            using Microsoft.Data.Sqlite.SqliteCommand version = check.CreateCommand();
                            version.CommandText = "SELECT version FROM schema_version;";
                            MuxAssert.AreEqual(2L, Convert.ToInt64(version.ExecuteScalar()), "schema version bumped");
                        }
                        finally
                        {
                            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                            TryDeleteDirectory(dir);
                        }
                    }),
                    StoreCase("ModelTimeseriesPreservesBucketsAndSamples", "Model time series preserve mixed-model totals, empty buckets, filters, and timing sample counts", async (SqliteUsageStore store, CancellationToken ct) =>
                    {
                        UsageEvent a = MakeEvent(1100, "openai", "alpha");
                        a.TimeToFirstTokenMs = 100;
                        UsageEvent b = MakeEvent(1200, "anthropic", "alpha");
                        b.TimeToFirstTokenMs = null;
                        UsageEvent c = MakeEvent(1300, "openai", "beta");
                        c.TimeToFirstTokenMs = 400;
                        await store.InsertBatchAsync(new List<UsageEvent> { a, b, c, MakeEvent(3100, "openai", "beta") }, ct);
                        PricingTable pricing = new PricingTable();
                        pricing.Models["alpha"] = new ModelPricing(1, 0, 2);
                        pricing.Models["beta"] = new ModelPricing(3, 0, 4);
                        UsageQueryService query = new UsageQueryService(store, () => pricing);
                        UsageFilter filter = new UsageFilter { FromUnixMs = 1000, ToUnixMs = 3999 };
                        var models = await query.GetModelTimeseriesAsync(filter, 1000, ct);
                        var totals = await query.GetTimeseriesAsync(filter, 1000, ct);
                        MuxAssert.AreEqual(2, models.Count, "both models");
                        MuxAssert.IsTrue(models["alpha"][0].Metrics.CostUsd > 0, "priced model has cost");
                        MuxAssert.AreEqual(3, models["alpha"].Count, "dense buckets");
                        MuxAssert.AreEqual(100.0, models["alpha"][0].Metrics.AvgTtftMs, "missing timings do not dilute the mean");
                        MuxAssert.AreEqual(400.0, models["beta"][0].Metrics.AvgTtftMs, "independent model average");
                        for (int i = 0; i < totals.Count; i++)
                        {
                            MuxAssert.IsTrue(Math.Abs(totals[i].Metrics.CostUsd - models["alpha"][i].Metrics.CostUsd - models["beta"][i].Metrics.CostUsd) < 1e-10, "model costs sum to total");
                            MuxAssert.AreEqual(totals[i].BucketStartUnixMs, models["alpha"][i].BucketStartUnixMs, "aligned timestamp");
                            MuxAssert.AreEqual(totals[i].Metrics.TotalTokens, models["alpha"][i].Metrics.TotalTokens + models["beta"][i].Metrics.TotalTokens, "stack equals total");
                        }
                        MuxAssert.AreEqual(0L, models["beta"][1].Metrics.Calls, "empty bucket stays empty");
                        filter.EndpointName = "anthropic-endpoint";
                        var filtered = await query.GetModelTimeseriesAsync(filter, 1000, ct);
                        MuxAssert.AreEqual(1, filtered.Count, "endpoint filter");
                        MuxAssert.AreEqual(1L, filtered["alpha"][0].Metrics.Calls, "matching call only");
                    }),
                    StoreCase("EmptyStoreCountsZero", "A freshly created store reports zero events", async (SqliteUsageStore store, CancellationToken ct) =>
                    {
                        MuxAssert.AreEqual(0L, await store.CountAsync(ct).ConfigureAwait(false), "empty store count");
                    }),

                    StoreCase("InsertRoundTrips", "A batch of events is inserted and counted", async (SqliteUsageStore store, CancellationToken ct) =>
                    {
                        List<UsageEvent> events = new List<UsageEvent>
                        {
                            MakeEvent(1_000, "anthropic", "claude-opus-4-8"),
                            MakeEvent(2_000, "openai", "gpt-4o"),
                            MakeEvent(3_000, "ollama", "qwen2.5-coder:32b")
                        };

                        int inserted = await store.InsertBatchAsync(events, ct).ConfigureAwait(false);
                        MuxAssert.AreEqual(3, inserted, "rows inserted");
                        MuxAssert.AreEqual(3L, await store.CountAsync(ct).ConfigureAwait(false), "count after insert");
                    }),

                    StoreCase("NullAndEmptyBatchesAreNoOps", "Null and empty batches insert nothing", async (SqliteUsageStore store, CancellationToken ct) =>
                    {
                        MuxAssert.AreEqual(0, await store.InsertBatchAsync(null, ct).ConfigureAwait(false), "null batch");
                        MuxAssert.AreEqual(0, await store.InsertBatchAsync(new List<UsageEvent>(), ct).ConfigureAwait(false), "empty batch");
                        MuxAssert.AreEqual(0L, await store.CountAsync(ct).ConfigureAwait(false), "count unchanged");
                    }),

                    StoreCase("RetentionPrunesOldRows", "Pruning deletes rows older than the retention window", async (SqliteUsageStore store, CancellationToken ct) =>
                    {
                        long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                        long oldMs = nowMs - (100L * 24L * 60L * 60L * 1000L); // 100 days old, beyond the 90-day window

                        await store.InsertBatchAsync(new List<UsageEvent>
                        {
                            MakeEvent(oldMs, "openai", "gpt-4o"),
                            MakeEvent(nowMs, "openai", "gpt-4o")
                        }, ct).ConfigureAwait(false);

                        int deleted = await store.PruneAsync(nowMs, ct).ConfigureAwait(false);
                        MuxAssert.AreEqual(1, deleted, "one old row pruned");
                        MuxAssert.AreEqual(1L, await store.CountAsync(ct).ConfigureAwait(false), "recent row retained");
                    }),

                    StoreCase("ConcurrentWritersAllLand", "Many concurrent writers all persist their rows without loss", async (SqliteUsageStore store, CancellationToken ct) =>
                    {
                        // Eight writers, each opening its own connections against the one WAL database, mirror
                        // eight mux instances writing at once. Every row must survive.
                        const int writers = 8;
                        const int perWriter = 50;

                        List<Task> tasks = new List<Task>();
                        for (int w = 0; w < writers; w++)
                        {
                            int writerId = w;
                            tasks.Add(Task.Run(async () =>
                            {
                                for (int i = 0; i < perWriter; i++)
                                {
                                    UsageEvent e = MakeEvent(1_000 + (writerId * 1000) + i, "openai", "gpt-4o");
                                    e.SessionId = "writer-" + writerId;
                                    await store.InsertBatchAsync(new List<UsageEvent> { e }, ct).ConfigureAwait(false);
                                }
                            }, ct));
                        }

                        await Task.WhenAll(tasks).ConfigureAwait(false);

                        MuxAssert.AreEqual((long)(writers * perWriter), await store.CountAsync(ct).ConfigureAwait(false), "every concurrent write landed");
                    }),

                    StoreCase("SessionIdsInFilterScopesRows", "A SessionIds filter scopes rows via an IN clause; an empty set yields none", async (SqliteUsageStore store, CancellationToken ct) =>
                    {
                        UsageEvent a = MakeEvent(1_000, "openai", "gpt"); a.SessionId = "s-a";
                        UsageEvent b = MakeEvent(2_000, "openai", "gpt"); b.SessionId = "s-b";
                        UsageEvent c = MakeEvent(3_000, "openai", "gpt"); c.SessionId = "s-c";
                        await store.InsertBatchAsync(new List<UsageEvent> { a, b, c }, ct).ConfigureAwait(false);

                        // Constrain to two of the three sessions.
                        UsageFilter twoOfThree = new UsageFilter { SessionIds = new List<string> { "s-a", "s-c" } };
                        MuxAssert.AreEqual(2L, await store.CountEventsAsync(twoOfThree, ct).ConfigureAwait(false), "IN clause scopes to the set");

                        // An empty (non-null) set means "no session matched" and must return zero rows.
                        UsageFilter none = new UsageFilter { SessionIds = new List<string>() };
                        MuxAssert.AreEqual(0L, await store.CountEventsAsync(none, ct).ConfigureAwait(false), "empty set yields zero");

                        // A null SessionIds set applies no constraint.
                        UsageFilter all = new UsageFilter();
                        MuxAssert.AreEqual(3L, await store.CountEventsAsync(all, ct).ConfigureAwait(false), "null set matches all");
                    }),

                    StoreCase("SecondStoreOnSameFileSharesData", "A second store opened on the same file sees the first store's rows", async (SqliteUsageStore store, CancellationToken ct) =>
                    {
                        await store.InsertBatchAsync(new List<UsageEvent> { MakeEvent(5_000, "anthropic", "claude-opus-4-8") }, ct).ConfigureAwait(false);

                        // A distinct store instance on the same path stands in for a second process.
                        using SqliteUsageStore second = new SqliteUsageStore(store.DatabasePath, 90, 5_000_000);
                        MuxAssert.AreEqual(1L, await second.CountAsync(ct).ConfigureAwait(false), "second store sees shared row");
                    }),

                    StoreCase("DeleteOrphansRemovesRowsWithoutSession", "Rows without a conversation id are purged; others remain", async (SqliteUsageStore store, CancellationToken ct) =>
                    {
                        UsageEvent withSession = MakeEvent(1_000, "openai", "gpt");
                        withSession.SessionId = "sess-x";
                        UsageEvent blankSession = MakeEvent(2_000, "openai", "gpt");
                        blankSession.SessionId = "   ";
                        UsageEvent nullSession = MakeEvent(3_000, "openai", "gpt");

                        await store.InsertBatchAsync(new List<UsageEvent> { withSession, blankSession, nullSession }, ct).ConfigureAwait(false);
                        MuxAssert.AreEqual(3L, await store.CountAsync(ct).ConfigureAwait(false), "all three inserted");

                        int deleted = await store.DeleteOrphansAsync(ct).ConfigureAwait(false);
                        MuxAssert.AreEqual(2, deleted, "the null and blank session rows are deleted");
                        MuxAssert.AreEqual(1L, await store.CountAsync(ct).ConfigureAwait(false), "only the row with a session id remains");
                    })
                });
        }

        #region Helpers

        private static UsageEvent MakeEvent(long tsUtc, string adapter, string model)
        {
            return new UsageEvent
            {
                TimestampUnixMs = tsUtc,
                CallKind = UsageCallKindEnum.Primary,
                Command = "test",
                EndpointName = adapter + "-endpoint",
                AdapterType = adapter,
                Model = model,
                InputTokens = 100,
                OutputTokens = 50,
                TotalTokens = 150,
                TimeToFirstTokenMs = 42,
                StreamingMs = 300,
                TotalMs = 342,
                TokensPerSecond = 12.5,
                FinishReason = "stop",
                Success = true
            };
        }

        private static TestCaseDescriptor StoreCase(string id, string name, Func<SqliteUsageStore, CancellationToken, Task> body)
        {
            return new TestCaseDescriptor(SuiteId, id, name, async (CancellationToken ct) =>
            {
                string dir = Path.Combine(Path.GetTempPath(), "mux_usage_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(dir);
                string dbPath = Path.Combine(dir, "usage.db");

                try
                {
                    using SqliteUsageStore store = new SqliteUsageStore(dbPath, 90, 5_000_000);
                    await body(store, ct).ConfigureAwait(false);
                }
                finally
                {
                    TryDeleteDirectory(dir);
                }
            });
        }

        private static void TryDeleteDirectory(string dir)
        {
            try
            {
                if (Directory.Exists(dir))
                {
                    Directory.Delete(dir, true);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        #endregion
    }
}
