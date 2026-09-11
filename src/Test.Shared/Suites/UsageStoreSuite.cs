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

                    StoreCase("SecondStoreOnSameFileSharesData", "A second store opened on the same file sees the first store's rows", async (SqliteUsageStore store, CancellationToken ct) =>
                    {
                        await store.InsertBatchAsync(new List<UsageEvent> { MakeEvent(5_000, "anthropic", "claude-opus-4-8") }, ct).ConfigureAwait(false);

                        // A distinct store instance on the same path stands in for a second process.
                        using SqliteUsageStore second = new SqliteUsageStore(store.DatabasePath, 90, 5_000_000);
                        MuxAssert.AreEqual(1L, await second.CountAsync(ct).ConfigureAwait(false), "second store sees shared row");
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
