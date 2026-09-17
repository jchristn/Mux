namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Sessions;
    using Mux.Core.Telemetry;
    using Touchstone.Core;

    /// <summary>
    /// Touchstone suite for the query-time join that lets usage analytics filter and break down by session
    /// label/tag. A fake <see cref="ISessionMetadataIndex"/> maps session ids to labels/tags; the query
    /// service resolves those to a session-id set and constrains the SQLite store. Covers the positive
    /// (scoping, fan-out breakdown) and negative (unknown label → empty) directions.
    /// </summary>
    public static class UsageMetadataFilterSuite
    {
        private const string SuiteId = "UsageMetadataFilter";

        /// <summary>Builds the suite descriptor.</summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/>.</returns>
        public static TestSuiteDescriptor Create()
        {
            return new TestSuiteDescriptor(
                SuiteId,
                "Usage filtering and breakdown by session label/tag (query-time join)",
                new List<TestCaseDescriptor>
                {
                    Case("LabelFilterScopesSummary", "Filtering usage by label counts only that label's sessions", async (UsageQueryService query, CancellationToken ct) =>
                    {
                        // 3 sessions: s-wip (label wip), s-both (label wip + tag env:prod), s-prod (tag env:prod).
                        UsageSummary all = await query.GetSummaryAsync(new UsageFilter(), ct).ConfigureAwait(false);
                        MuxAssert.AreEqual(6L, all.Metrics.Calls, "all events counted without a filter");

                        UsageFilter wip = new UsageFilter { Labels = new List<string> { "wip" } };
                        UsageSummary wipSummary = await query.GetSummaryAsync(wip, ct).ConfigureAwait(false);
                        MuxAssert.AreEqual(4L, wipSummary.Metrics.Calls, "only s-wip (2) + s-both (2) events");
                    }),

                    Case("TagFilterScopesSummary", "Filtering usage by tag counts only that tag's sessions", async (UsageQueryService query, CancellationToken ct) =>
                    {
                        UsageFilter prod = new UsageFilter { Tags = new List<SessionTag> { new SessionTag("env", "prod") } };
                        UsageSummary prodSummary = await query.GetSummaryAsync(prod, ct).ConfigureAwait(false);
                        MuxAssert.AreEqual(4L, prodSummary.Metrics.Calls, "only s-prod (2) + s-both (2) events");
                    }),

                    Case("UnknownLabelYieldsEmpty", "An unknown label matches nothing and returns an empty summary, not an error", async (UsageQueryService query, CancellationToken ct) =>
                    {
                        UsageFilter unknown = new UsageFilter { Labels = new List<string> { "does-not-exist" } };
                        UsageSummary summary = await query.GetSummaryAsync(unknown, ct).ConfigureAwait(false);
                        MuxAssert.AreEqual(0L, summary.Metrics.Calls, "no sessions match → zero calls");
                    }),

                    Case("LabelAndTagCompose", "A label and a tag compose with AND semantics", async (UsageQueryService query, CancellationToken ct) =>
                    {
                        // Only s-both carries both label wip AND tag env:prod.
                        UsageFilter both = new UsageFilter
                        {
                            Labels = new List<string> { "wip" },
                            Tags = new List<SessionTag> { new SessionTag("env", "prod") }
                        };
                        UsageSummary summary = await query.GetSummaryAsync(both, ct).ConfigureAwait(false);
                        MuxAssert.AreEqual(2L, summary.Metrics.Calls, "only s-both matches both constraints");
                    }),

                    Case("LabelBreakdownFansOut", "Breakdown by label buckets each session's usage under every label it carries", async (UsageQueryService query, CancellationToken ct) =>
                    {
                        List<UsageBreakdownRow> rows = await query.GetBreakdownAsync("label", new UsageFilter(), ct).ConfigureAwait(false);

                        long wipCalls = 0;
                        foreach (UsageBreakdownRow row in rows)
                        {
                            if (string.Equals(row.Value, "wip", StringComparison.Ordinal)) wipCalls = row.Metrics.Calls;
                        }

                        // s-wip (2) + s-both (2) both carry "wip" → the bucket sums their usage (fan-out).
                        MuxAssert.AreEqual(4L, wipCalls, "wip bucket sums every session carrying it");
                    }),

                    Case("LabelListAndTagList", "GetLabels/GetTags expose the distinct values for filter dropdowns", async (UsageQueryService query, CancellationToken ct) =>
                    {
                        List<string> labels = await query.GetLabelsAsync(ct).ConfigureAwait(false);
                        MuxAssert.AreEqual(1, labels.Count, "one distinct label (wip)");
                        MuxAssert.AreEqual("wip", labels[0], "label value");

                        List<SessionTag> tags = await query.GetTagsAsync(ct).ConfigureAwait(false);
                        MuxAssert.AreEqual(1, tags.Count, "one distinct tag (env:prod)");
                        MuxAssert.AreEqual("env", tags[0].Key, "tag key");
                        MuxAssert.AreEqual("prod", tags[0].Value, "tag value");
                    })
                });
        }

        #region Helpers

        private sealed class FakeMetadataIndex : ISessionMetadataIndex
        {
            private readonly IReadOnlyList<SessionMetadataEntry> _Entries;

            public FakeMetadataIndex(IReadOnlyList<SessionMetadataEntry> entries)
            {
                _Entries = entries;
            }

            public Task<IReadOnlyList<SessionMetadataEntry>> SnapshotAsync(CancellationToken token)
            {
                return Task.FromResult(_Entries);
            }
        }

        private static UsageEvent MakeEvent(long tsUtc, string sessionId)
        {
            return new UsageEvent
            {
                TimestampUnixMs = tsUtc,
                CallKind = UsageCallKindEnum.Primary,
                Command = "test",
                EndpointName = "openai-endpoint",
                AdapterType = "openai",
                Model = "gpt-4o",
                SessionId = sessionId,
                InputTokens = 100,
                OutputTokens = 50,
                TotalTokens = 150,
                Success = true
            };
        }

        private static TestCaseDescriptor Case(string id, string name, Func<UsageQueryService, CancellationToken, Task> body)
        {
            return new TestCaseDescriptor(SuiteId, id, name, async (CancellationToken ct) =>
            {
                string dir = Path.Combine(Path.GetTempPath(), "mux_usmeta_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(dir);
                string dbPath = Path.Combine(dir, "usage.db");

                try
                {
                    using SqliteUsageStore store = new SqliteUsageStore(dbPath, 90, 5_000_000);
                    await store.InsertBatchAsync(new List<UsageEvent>
                    {
                        MakeEvent(1_000, "s-wip"),
                        MakeEvent(2_000, "s-wip"),
                        MakeEvent(3_000, "s-both"),
                        MakeEvent(4_000, "s-both"),
                        MakeEvent(5_000, "s-prod"),
                        MakeEvent(6_000, "s-prod")
                    }, ct).ConfigureAwait(false);

                    List<SessionMetadataEntry> entries = new List<SessionMetadataEntry>
                    {
                        new SessionMetadataEntry { Id = "s-wip", Labels = new List<string> { "wip" }, Tags = new List<SessionTag>() },
                        new SessionMetadataEntry { Id = "s-both", Labels = new List<string> { "wip" }, Tags = new List<SessionTag> { new SessionTag("env", "prod") } },
                        new SessionMetadataEntry { Id = "s-prod", Labels = new List<string>(), Tags = new List<SessionTag> { new SessionTag("env", "prod") } }
                    };

                    UsageQueryService query = new UsageQueryService(store, () => new PricingTable(), new FakeMetadataIndex(entries));
                    await body(query, ct).ConfigureAwait(false);
                }
                finally
                {
                    try
                    {
                        if (Directory.Exists(dir)) Directory.Delete(dir, true);
                    }
                    catch (IOException)
                    {
                    }
                    catch (UnauthorizedAccessException)
                    {
                    }
                }
            });
        }

        #endregion
    }
}
