# Usage Telemetry — Implementation Plan

Persistent, cross-process usage analytics for mux: token counts, time-to-first-token, streaming
time, latency, and cost, captured from every model call regardless of entrypoint (interactive TUI,
`mux print`, `mux serve` chat, subagents, compaction sidecars) and surfaced in two places — the TUI
sidebar / `/usage` view, and the `mux serve` dashboard with charts over time.

This document is the design of record. It follows `C:\Code\agents\requirements` — `CODE_STYLE.md`,
`TELEMETRY_REQUIREMENTS.md`, `DASHBOARD_STYLE_AND_USABILITY.md`, `REPOSITORY_REQUIREMENTS.md`, and
`I18N.md` — and reconciles the two places those requirements collide with mux's actual shape (a
single-binary CLI with a Watson server and a hand-rendered HTML dashboard, not a Dockerised service
with a React/Vite frontend). Those reconciliations are called out where they occur, per the
cross-document rule that a conflict is surfaced before implementation, not silently resolved.

---

## 1. Scope and the two telemetry systems

There are two distinct telemetry concerns in this product, and conflating them produces a design that
serves neither. Keep them separate.

**Operational observability** is what `TELEMETRY_REQUIREMENTS.md` standardises: Watson 7.1's built-in
`Meter`/`ActivitySource`, scraped by Prometheus, rendered in Grafana. It answers "is the `mux serve`
HTTP surface healthy right now?" It is per-process, ephemeral, and resets when the process exits.
mux already runs on Watson 7.1.1, so this is a settings toggle plus a collector — covered in §11.

**Usage analytics** is what this plan builds: a durable record of model spend and performance that
survives restarts, spans every concurrent mux instance on the machine, and answers "how many tokens
did I burn last week, on which model, and what did it cost?" Prometheus structurally cannot answer
that — it is per-process and short-retention by design. That gap is the entire reason this store
exists, and it is why the store is a real database rather than a Grafana panel.

The two coexist. Watson/Prometheus watches the server's HTTP layer; the SQLite usage store is the
product's own analytics substrate, read by both the TUI and the dashboard. Neither replaces the
other, and the usage store is deliberately **not** wired into Prometheus (its data is high-cardinality
by nature — per-call, per-session, per-model — exactly the shape the telemetry conventions forbid on a
metric label).

Scope of this plan, in priority order:

1. Capture — one record per LLM call, at the single choke point every call already flows through.
2. Persist — SQLite at `~/.mux/usage.db`, multi-process safe, no external process.
3. Query — time-bucketed aggregation with the dimensions in §4.
4. Surface — the `mux serve` dashboard (primary) and the TUI (secondary).

---

## 2. Design decisions and rationale

**SQLite, not flat JSON — and why that reverses a prior stance.** `archive/MUX_COMPARISON.md`
argued against SQLite for this CLI, on the grounds that a local tool should not drag in a database
engine when flat JSON files under `~/.mux` do the job. That reasoning holds for the *session and
config stores*, which are single-writer, whole-file, human-editable documents — and those stores stay
exactly as they are. It does not hold for usage analytics, which is append-heavy, written concurrently
by every running mux instance, and queried with time-range aggregations that a JSON blob cannot serve
without loading and scanning the whole history on every dashboard refresh. The decision here is narrow:
add SQLite for the one workload it fits, leave `SettingsLoader` and `SessionStore` untouched. The prior
note is respected, not contradicted — it was about a different problem.

**Multi-process concurrency is SQLite's job, not ours.** The original sketch — each instance writes
its own file, one instance claims a lock and coalesces — is a reinvention of what SQLite already does
correctly. In WAL mode, many processes read and one writes without blocking each other; a rare
writer-writer collision is resolved by `busy_timeout`. There is no leader election, no per-instance
segment files, no compaction job, and no "I'm working here" claim file. Every mux process opens the
same `usage.db` and writes to it directly. This is the single biggest simplification the SQLite choice
buys, and it is only safe because the store lives on a local filesystem (confirmed — `~/.mux` is never
a network mount here; WAL locking is unreliable over NFS/SMB).

**Capture at `LlmClient.StreamAsync`, exposed for the caller to record.** Every model call in mux —
TUI turn, headless print, dashboard chat, subagent, compaction summary — passes through
`LlmClient.StreamAsync`. PolyPrompt hands that method a `ToolChatStreamingResponse` that already
carries `TimeToFirstTokenMs`, `TimeToLastTokenMs`, `OverallRuntimeMs`, `OverallTokensPerSecond`,
`FinishReason`, `Model`, and `ChunkCount` — all of which `RecordUsage` currently throws away. We stop
throwing them away. `LlmClient` gains a `LastCall` metrics object; the caller (which owns the
correlation context — session id, command, job id) reads it and writes the row. This mirrors the
pattern `ChatRoutes` already uses with `LastUsage`, and keeps `LlmClient` a transport that measures
but does not know about sessions or databases.

**Cost is computed at read time, not stored.** Token counts are immutable facts; prices change and get
corrected. Storing tokens and pricing separately means a pricing fix re-values all history for free.
Cost never lands in a column — it is derived in the query layer from a user-editable `pricing.json`.

**Best-effort, never fatal.** Per `TELEMETRY_REQUIREMENTS.md`, a telemetry write that fails must not
affect the run. Every recorder path swallows its own exceptions to a log sink and returns; a broken or
locked database degrades the dashboard to empty, never the agent to a crash.

---

## 3. Data model

The grain is **one row per LLM call** (one model round-trip = one iteration of the agent loop, one
chat request, one compaction summary). Run- and session-level figures are sums over rows, so the
richest grain is preserved and nothing is pre-aggregated away. Percentiles (§4) require raw rows, so
this grain is mandatory, not a convenience.

### 3.1 Schema

SQL is authored as literal strings (consistent with the house convention that hand-written SQL is
deliberate). Single table plus a version row for migrations.

```sql
CREATE TABLE IF NOT EXISTS schema_version (
    version INTEGER NOT NULL
);

CREATE TABLE IF NOT EXISTS usage_events (
    id                INTEGER PRIMARY KEY AUTOINCREMENT,
    ts_utc            INTEGER NOT NULL,   -- unix epoch ms at call completion
    run_id            TEXT,
    session_id        TEXT,
    job_id            TEXT,
    call_kind         TEXT NOT NULL,      -- primary | compaction | subagent | chat | probe
    command           TEXT,               -- interactive | print | serve | tray | ...
    endpoint_name     TEXT NOT NULL,
    adapter_type      TEXT NOT NULL,      -- provider family (ollama, anthropic, openai, ...)
    model             TEXT NOT NULL,
    base_host         TEXT,               -- host only; never full URL, path, or query
    project           TEXT,               -- working-directory basename (local only)
    iteration         INTEGER,            -- loop iteration index within the run
    input_tokens      INTEGER NOT NULL DEFAULT 0,
    cached_tokens     INTEGER NOT NULL DEFAULT 0,   -- 0 until PolyPrompt exposes it (see §5.3)
    output_tokens     INTEGER NOT NULL DEFAULT 0,
    reasoning_tokens  INTEGER NOT NULL DEFAULT 0,    -- 0 until PolyPrompt exposes it
    total_tokens      INTEGER NOT NULL DEFAULT 0,
    ttft_ms           INTEGER,            -- nullable; provider may not report
    stream_ms         INTEGER,            -- last-token minus first-token
    total_ms          INTEGER,            -- request start to stream end
    tokens_per_sec    REAL,
    finish_reason     TEXT,
    success           INTEGER NOT NULL,   -- 0/1
    error_code        TEXT,               -- e.g. llm_connection_error, llm_stream_error
    retry_count       INTEGER NOT NULL DEFAULT 0,
    pricing_ver       TEXT                -- pricing.json version in effect at write (audit only)
);

CREATE INDEX IF NOT EXISTS ix_usage_ts        ON usage_events (ts_utc);
CREATE INDEX IF NOT EXISTS ix_usage_endpoint  ON usage_events (endpoint_name, ts_utc);
CREATE INDEX IF NOT EXISTS ix_usage_model     ON usage_events (model, ts_utc);
CREATE INDEX IF NOT EXISTS ix_usage_session   ON usage_events (session_id);
```

Notes on specific columns:

- `base_host` stores the host only. The full base URL can carry credentials in a query string for some
  adapters; the host is enough to distinguish endpoints and leaks nothing.
- `project` is the working-directory basename, useful for "which repo is burning tokens." It stays on
  the local machine and is never exported; if that is still too much, store a stable hash instead — a
  one-line switch, flagged as an open question in §16.
- `call_kind` distinguishes the primary agent loop from compaction sidecars (currently invisible in
  `CumulativeUsage`) and subagent calls, so spend can be attributed to where it actually goes.
- `cached_tokens` and `reasoning_tokens` are in the schema now but read 0 until the provider layer can
  supply them (§5.3). Putting the columns in from day one avoids a migration later.

---

## 4. Metrics and dimensions

The four you asked for — token usage, latency, streaming time, TTFT — are the spine. Each is a
`SUM`/`AVG`/percentile over `usage_events` bucketed by time and filterable by endpoint. Everything
below is derivable from the columns above with no additional capture.

### 4.1 The requested metrics

| Metric | Source | Notes |
| --- | --- | --- |
| Token usage over time | `input_tokens`, `cached_tokens`, `output_tokens` | Stacked by token type. Cached series is present but flat at 0 until §5.3. |
| Latency over time | `total_ms` | End-to-end request duration. Show p50/p95/p99, not just mean. |
| Streaming time over time | `stream_ms` | First-token → completion. |
| Time-to-first-token over time | `ttft_ms` | Nullable rows excluded from TTFT aggregates. |

Every chart takes the same time ranges (last hour, day, week, month, custom) and the same
endpoint filter, plus a model filter where it adds signal.

### 4.2 Recommended additional dimensions

These are the ones worth building in the first pass, in rough value order:

**Cost / spend over time.** The headline derived metric, and the one nothing else in the stack can
produce. Computed at read time from `pricing.json` (§6), broken down by model and endpoint. A cost
chart plus a "spend this month" KPI is what makes this feature something an operator opens daily rather
than once.

**Cache hit rate.** `cached_tokens / (input_tokens + cached_tokens)`. It explains cost swings directly
and is the single most actionable number for anyone tuning prompt caching. Gated on §5.3, but the query
is trivial once the data flows.

**Throughput (tokens/sec).** `output_tokens / (stream_ms / 1000)`, or PolyPrompt's own
`OverallTokensPerSecond`. The real "how fast is this model" figure and the cleanest way to compare a
local Ollama model against a hosted one.

**Reliability.** Call volume, error rate (`success = 0` grouped by `error_code`), and `retry_count`
over time. The loop already surfaces `llm_connection_error`, `llm_stream_error`, and retry attempts;
this dimension turns them into a trend instead of a lost log line.

**Breakdowns used as filters and grouping, not just axes:**

- **By model** and **by provider/adapter** — one endpoint can serve several models, and cost is
  per-model, so model is a first-class grouping, not a sub-facet of endpoint.
- **By command / mode** — interactive vs `print` vs `serve` vs subagent vs compaction. Answers "where
  is the spend going," and makes the otherwise-invisible compaction overhead visible.
- **By session and by project** — attribute a turn or a whole repo's worth of work to its cost.

**Context and compaction.** Context-window utilisation (`FinalEstimatedTokens` against the endpoint's
`ContextWindow`) and compaction frequency per run. Both drive cost and latency, and both are already
computed in the loop — they just need a home. These ride on the run-level aggregate rather than the
per-call row, so they are a light addition to the summary endpoint rather than a new column.

### 4.3 Percentiles

`TELEMETRY_REQUIREMENTS.md` is emphatic that quantiles are derived downstream, not precomputed. SQLite
has no `percentile_cont`, so for each time bucket the query layer fetches the bucket's `ttft_ms` /
`total_ms` values and computes exact p50/p95/p99 in C# by ordering. Single-user data volumes make this
cheap; if a bucket ever grows large, an `NTILE`-based approximation in SQL is the fallback. Means are
kept alongside percentiles because a mean plus a p95 tells a different story than either alone.

---

## 5. Capture path

### 5.1 Widen the usage model

`LlmUsage` (`src/Mux.Core/Llm/LlmUsage.cs`) gains two fields, defaulting to 0 and summed in `Add`:

```csharp
/// <summary>
/// Cached/cache-read prompt tokens reported by the provider. Remains 0 for providers or library
/// versions that do not report cache usage.
/// </summary>
public int CachedTokens { get; set; }

/// <summary>
/// Reasoning/thinking tokens reported by the provider. Remains 0 when unreported.
/// </summary>
public int ReasoningTokens { get; set; }
```

A new one-class-per-file type carries the per-call performance metrics PolyPrompt already computes:

```csharp
namespace Mux.Core.Llm
{
    /// <summary>
    /// Performance metrics for a single streaming LLM call, projected from the provider response.
    /// Timing fields are null when the provider did not report them.
    /// </summary>
    public sealed class LlmCallMetrics
    {
        /// <summary>Provider-reported token usage for the call.</summary>
        public LlmUsage Usage { get; set; } = new LlmUsage();

        /// <summary>Time from request start to the first text or tool-call delta, in milliseconds.</summary>
        public long? TimeToFirstTokenMs { get; set; }

        /// <summary>Time from first delta to stream completion, in milliseconds.</summary>
        public long? StreamingMs { get; set; }

        /// <summary>Total request duration, in milliseconds.</summary>
        public long? TotalMs { get; set; }

        /// <summary>Overall output tokens per second, or null when unavailable.</summary>
        public double? TokensPerSecond { get; set; }

        /// <summary>The provider's finish reason (stop, length, tool_calls, ...), or null.</summary>
        public string? FinishReason { get; set; }

        /// <summary>The model id echoed by the provider, or null.</summary>
        public string? Model { get; set; }

        /// <summary>Whether the call completed successfully.</summary>
        public bool Success { get; set; }
    }
}
```

### 5.2 Retain what PolyPrompt reports

`LlmClient.RecordUsage` (`src/Mux.Core/Llm/LlmClient.cs`) is rewritten to project the full
`ToolChatStreamingResponse` into an `LlmCallMetrics`, exposed as a new `LastCall` property alongside
the existing `LastUsage`/`CumulativeUsage`:

```csharp
private void RecordUsage(Pp.ToolChatStreamingResponse response)
{
    Pp.ChatStreamingUsage? usage = response.Usage;
    int input = usage?.PromptTokens ?? 0;
    int output = usage?.CompletionTokens ?? 0;
    int total = usage?.TotalTokens ?? (input + output);

    LlmUsage record = new LlmUsage
    {
        InputTokens = input,
        OutputTokens = output,
        TotalTokens = total
        // CachedTokens / ReasoningTokens set here once PolyPrompt exposes them (see 5.3).
    };

    _LastUsage = record;
    _CumulativeUsage.Add(record);

    _LastCall = new LlmCallMetrics
    {
        Usage = record,
        TimeToFirstTokenMs = response.TimeToFirstTokenMs,
        StreamingMs = ComputeStreamingMs(response),
        TotalMs = response.OverallRuntimeMs,
        TokensPerSecond = response.OverallTokensPerSecond,
        FinishReason = response.FinishReason,
        Model = response.Model,
        Success = response.Success
    };
}
```

`StreamingMs` is `TimeToLastTokenMs - TimeToFirstTokenMs` when both are present. Because `RecordUsage`
runs inside `StreamAsync` for every call, TTFT and streaming time are now captured uniformly for the
TUI, headless, and subagent paths — the client-side `Stopwatch` re-measurement in `ChatRoutes` and
`MuxTuiApp` becomes redundant and can be retired in favour of `LastCall`, though that cleanup is
optional and can lag the initial landing.

### 5.3 Backend gap: cached and reasoning tokens

PolyPrompt 2.5.0's `ChatStreamingUsage` exposes only `PromptTokens`, `CompletionTokens`, and
`TotalTokens` — no cache-read or reasoning-token field. So the `cached_tokens` series the dashboard
requests **cannot be populated from the provider today**, and `ConversationStats.CachedTokens` (already
present in the sidebar) is correspondingly always 0. This is a real limitation, not an oversight, and
the plan handles it honestly:

- The schema and DTOs carry the columns now, so no migration is needed later.
- The cached-tokens chart series renders as a flat zero with a one-line note ("cache metrics require a
  provider-library update") rather than being hidden — per the dashboard rule that a missing backend
  capability is shown as a labelled empty state, not silently dropped.
- Closing the gap is a PolyPrompt upgrade (surface Anthropic `cache_read_input_tokens` / OpenAI
  `prompt_tokens_details.cached_tokens` / reasoning-token counts), tracked as a dependency item in §16.

### 5.4 Recorder and correlation context

A recorder abstraction in `Mux.Core` decouples the hot path from the database:

```csharp
namespace Mux.Core.Telemetry
{
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Records usage events for a single LLM call. Implementations are best-effort: a failed write is
    /// logged and swallowed, never surfaced to the agent run. Implementations must be thread-safe.
    /// </summary>
    public interface IUsageRecorder
    {
        /// <summary>Enqueues an event for durable recording. Non-blocking; never throws.</summary>
        /// <param name="usageEvent">The event to record. Ignored when null.</param>
        void Record(UsageEvent usageEvent);

        /// <summary>Flushes any buffered events to storage.</summary>
        /// <param name="token">A token to cancel the flush.</param>
        Task FlushAsync(CancellationToken token);
    }
}
```

`UsageEvent` is a plain POCO mirroring the schema columns (one class per file). The correlation fields
the transport does not know — `session_id`, `command`, `job_id`, `project`, `run_id`, `iteration` —
come from the caller:

- **`AgentLoop`** gains an optional `IUsageRecorder` on `AgentLoopOptions`. After each `StreamAsync`
  iteration it reads `_LlmClient.LastCall`, stamps it with `Options.SessionId`, `Options.CommandName`,
  `Options.JobId`, `Options.WorkingDirectory`, the run id, and the iteration index, and calls
  `Record`. The compaction sidecar (`RunSidecarPromptAsync`) records with `call_kind = compaction`, so
  its spend stops being invisible. Subagent loops inherit the recorder and tag `call_kind = subagent`.
- **`ChatRoutes`** already builds a `ChatStats`; it additionally constructs a `UsageEvent` from
  `client.LastCall` with `call_kind = chat` and `command = serve`.
- **`mux probe`** may record with `call_kind = probe` so model-load latency is visible, or skip
  recording — an open question in §16.

The timestamp is taken once at record time. (`Stopwatch` handles the durations; wall-clock `ts_utc`
is `DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()` at enqueue.)

---

## 6. Pricing and cost

A seeded, user-editable `~/.mux/pricing.json` maps a model id to its rates:

```json
{
  "version": "2026-09-01",
  "models": {
    "claude-opus-4-8":   { "inputPerMTok": 15.0, "cachedInputPerMTok": 1.5, "outputPerMTok": 75.0 },
    "gpt-4o":            { "inputPerMTok": 2.5,  "cachedInputPerMTok": 1.25, "outputPerMTok": 10.0 },
    "qwen2.5-coder:32b": { "inputPerMTok": 0.0,  "cachedInputPerMTok": 0.0,  "outputPerMTok": 0.0 }
  }
}
```

Cost for a row is `input/1e6 * inputPer + cached/1e6 * cachedPer + output/1e6 * outputPer`, computed in
the query layer. Local models default to 0. The file is seeded on first run and **manifest-tracked**
the same way `subagents.json` is (a `pricing.seeded.json` sidecar records what was shipped), so new
default prices appear on upgrade without resurrecting entries the user deleted or overwriting their
edits. Unknown models cost 0 and are listed in the dashboard's pricing view so the user can add rates.
`pricing_ver` is stamped onto each row for audit, but cost is always recomputed from the current file,
so a price correction re-values history.

Loading/seeding follows the existing `SettingsLoader` conventions (atomic write, shared read,
`GetConfigDirectory()` resolution honouring `MUX_CONFIG_DIR`).

---

## 7. Storage layer

New project component `Mux.Core/Telemetry`, one class per file:

- `UsageEvent` — the row POCO.
- `IUsageRecorder` — the interface above.
- `SqliteUsageStore` — owns the connection, schema creation, migration, writes, queries, retention.
- `SqliteUsageRecorder` — the `IUsageRecorder` implementation; a bounded `Channel<UsageEvent>` fed by
  `Record`, drained by a single background writer task that batches inserts into `SqliteUsageStore`.
  Decoupling the write keeps the agent loop's hot path free of database latency and collapses many
  small inserts into one transaction.
- `UsageQuery` / `UsageQueryResult` — the filter (time range, bucket, endpoint, model, call kind,
  metric) and the bucketed result.
- `PricingTable` / `PricingLoader` — pricing model and its loader.
- `TelemetrySettings` — the settings section (§8).

**Dependency.** Add `Microsoft.Data.Sqlite` to `Mux.Core.csproj`. It bundles the native SQLite per RID
(`SQLitePCLRaw`), so nothing for the user to install. Note that it complicates NativeAOT; mux does not
ship AOT today, but this is recorded in §16 as a constraint to revisit if that changes.

**Connection and pragmas.** The store resolves `Path.Combine(SettingsLoader.GetConfigDirectory(),
"usage.db")`. On open it sets:

```
PRAGMA journal_mode=WAL;      -- persistent; many readers + one writer, no mutual blocking
PRAGMA synchronous=NORMAL;    -- safe under WAL; durable to app crash, fast
PRAGMA busy_timeout=5000;     -- wait out a rare writer-writer collision instead of failing
PRAGMA foreign_keys=ON;
```

`journal_mode=WAL` is persisted in the database header and set once; `synchronous` and `busy_timeout`
are per-connection and set on every open. The store keeps `usage.db` plus its `-wal` and `-shm`
sidecars in `~/.mux`. A periodic `wal_checkpoint(TRUNCATE)` (on writer idle, or every N writes) keeps
the WAL from growing without bound.

**Concurrency.** This is the whole point of §2. Multiple mux processes each open their own connection
to the same file; WAL lets the dashboard's reader run while a TUI instance writes, and `busy_timeout`
serialises the uncommon case of two instances writing at the same instant. No application-level
locking, no coordination file. Within a single process, `SqliteUsageRecorder`'s single writer task
serialises that process's own inserts, which also means each process holds the write lock only in short
bursts.

**Retention.** `TelemetrySettings.RetentionDays` (default 90, 0 = keep forever) drives a prune —
`DELETE FROM usage_events WHERE ts_utc < :cutoff` — run once on store open and then daily by the writer
task. A `MaxRows` ceiling is a secondary guard. Pruning is logged, never silent, per the "no silent
caps" rule.

**Lifecycle.** `SqliteUsageStore` and `SqliteUsageRecorder` implement the full dispose pattern
(`protected virtual void Dispose(bool)`, `IAsyncDisposable` for the writer flush). They are constructed
once per process — in `Program.RunInteractive` for the TUI and in `ServeCommand` for the server (manual
wiring, consistent with the no-DI codebase) — and threaded into `AgentLoopOptions` and `MuxServer`.

---

## 8. Settings

A new `TelemetrySettings` section hangs off `MuxSettings`, modelled on the existing `Rest` /
`ExternalSearch` sections and on Pneuma's `TelemetrySettings` per the requirements:

```csharp
private TelemetrySettings _Telemetry = new TelemetrySettings();

/// <summary>Usage-telemetry capture and retention settings.</summary>
[JsonPropertyName("telemetry")]
public TelemetrySettings Telemetry
{
    get => _Telemetry;
    set => _Telemetry = value ?? new TelemetrySettings();
}
```

`TelemetrySettings` fields (all with backing fields, `[JsonPropertyName]`, validated setters, and XML
docs stating defaults/ranges per `CODE_STYLE.md`):

- `Enabled` — master switch, default `true` (telemetry ships on, per the requirements' "observable by
  default" principle).
- `RetentionDays` — default 90, clamped 0–3650 (0 = forever).
- `DatabasePath` — optional override; blank resolves to `~/.mux/usage.db`.
- `PricingEnabled` — default `true`; when false the cost dimension is hidden.
- `MaxRows` — secondary retention guard, default 5_000_000.

**Critical implementation gotcha.** `SettingsLoader.NormalizeSettingsForPersistence` rebuilds a fresh
`MuxSettings` and copies fields **by hand**, and it currently omits `Rest` and `MaxTokenBudget` — any
field not copied there is silently dropped on the next load/save round-trip (settings are re-normalised
and rewritten on load). Adding `Telemetry` therefore requires two edits, not one:

1. Add the property to `MuxSettings.cs`.
2. Add `Telemetry = NormalizeTelemetrySettings(settings.Telemetry)` to the
   `NormalizeSettingsForPersistence` initializer, with a `NormalizeTelemetrySettings` helper mirroring
   `NormalizeExternalSearchSettings`.

(The pre-existing `Rest` omission is a latent bug in the same method; worth fixing in the same pass, but
out of scope to fix silently — flagged in §16.)

Environment override `MUX_TELEMETRY_ENABLED` is added to `ApplyEnvironmentOverrides` so telemetry can
be disabled without editing the file (useful for CI and ephemeral runs).

---

## 9. REST API

New `UsageRoutes` registrar under `src/Mux.Server/Routes/`, following the established pattern exactly:
`public sealed class`, constructor takes the api key first plus a `SqliteUsageStore` (or a query
service over it), `ApiAuth.Authorize(req.Http, _ApiKey)` as the first line of every handler, versioned
paths under `/v1.0/api/`, DTOs returned as `object` for Watson to serialise, `ApiError` envelopes,
query params via `req.Http.Request.Query.Elements[...]`. Registered in `MuxServer.RegisterRoutes`
alongside the others.

Endpoints:

| Method | Path | Purpose |
| --- | --- | --- |
| GET | `/v1.0/api/usage/summary` | KPI strip for a window: total tokens by type, total cost, calls, error rate, cache hit rate, avg + p95 TTFT and latency. Query: `from`, `to`. |
| GET | `/v1.0/api/usage/timeseries` | Bucketed series for one metric. Query: `metric` (tokens\|latency\|ttft\|stream\|cost\|throughput\|calls\|errors), `from`, `to`, `bucket` (hour\|day), `endpoint`, `model`, `callKind`. |
| GET | `/v1.0/api/usage/breakdown` | Grouped totals for a dimension. Query: `dimension` (endpoint\|model\|provider\|command\|project), `metric`, `from`, `to`. |
| GET | `/v1.0/api/usage/events` | Paginated raw rows for the usage-history table. Query: standard pagination + `endpoint`, `model`, `callKind`, `success`, `from`, `to`. Returns `ListResponse<UsageEventDto>` with total count. |
| GET | `/v1.0/api/usage/pricing` | Current pricing table + list of models seen with no rate. |
| PUT | `/v1.0/api/usage/pricing` | Save edited pricing (admin/local only), following the secret-free config-save convention. |

DTOs live in a new `src/Mux.Server/Models/UsageDtos.cs`-family (one class per file where the house
style calls for it): `UsageSummaryDto`, `UsageSeriesDto` (`{ Buckets: [...] }` with `{ TsUtc, Values }`
points), `UsageBreakdownDto`, `UsageEventDto`, `PricingDto`. System.Text.Json throughout, PascalCase on
the wire to match the dashboard's existing `res.Items` reads.

Time buckets are computed with SQLite date functions on `ts_utc` (`strftime` over the ms value / 1000);
percentiles are computed in C# over each bucket's rows as noted in §4.3. Low-cardinality is not a
concern here (this is an analytical store, not a Prometheus label set), but query inputs are still
validated and clamped — `bucket`, `metric`, and `dimension` are enums, not free text, and a bad value
returns `ApiError` 400 rather than injecting into SQL (parameters are always bound).

---

## 10. Dashboard (`mux serve`)

### 10.1 Reconciliation with the dashboard requirements

`DASHBOARD_STYLE_AND_USABILITY.md` assumes a React/Vite SPA with a shared `ApiClient`, route outlets,
and Playwright visual QA. mux's dashboard is none of that: it is a single self-contained HTML document
generated as a C# raw string in `src/Mux.Server/DashboardPage.cs`, with inlined CSS, vanilla JS, a
client-side `data-view` router, a `VIEW_LOADERS` dispatch map, a single `api()` fetch helper, and a
built-in i18n table. This is a deliberate product choice (zero build step, one file served over
loopback), and it is the documented deviation from the React assumption. The plan satisfies the
**intent** of the requirements — charts with time ranges and filters, a KPI strip, a history table with
an inspector modal, empty/loading/error states, copy controls, responsive behaviour — within that
architecture, rather than importing a frontend framework the product has intentionally avoided.

Concretely, a new dashboard surface is: a nav button with `data-view`, a view section in the `Template`
string, a `VIEW_LOADERS` entry, `api("/v1.0/api/usage/...")` calls, and new i18n keys in the existing
table. No bundler, no new dependency.

### 10.2 Charting without a dependency

The dashboard forbids external assets (self-contained, served over loopback). Charts are rendered by a
small inline vanilla helper drawing **SVG** — line/area for time series, stacked area for the token-type
breakdown, horizontal bars for dimension breakdowns. SVG (not canvas) keeps it crisp, themeable via the
existing CSS variables, and accessible (title/desc elements). The helper is ~150 lines of JS added to
the `Template`; no charting library.

### 10.3 Views

Grouped under a new **Analytics** (or **Usage**) nav section, product-work-first per the navigation
rules:

**Usage Overview** — the command-centre page:

- KPI cards (4–8): total tokens (this window), total cost, calls, error rate, cache hit rate, avg TTFT,
  p95 latency, avg throughput. Cards are clickable through to the filtered history where it makes sense.
- A time-range segmented control (Hour / Day / Week / Month / Custom) driving every chart on the page.
- An endpoint filter and a model filter (selects populated from `/v1.0/api/endpoints`).
- The four core charts — token usage (stacked prompt/cached/output), latency (p50/p95/p99), streaming
  time, TTFT — plus cost over time and throughput.
- Empty state ("no usage recorded yet — run a turn"), loading state, and per-panel partial-failure
  handling so one failed query does not blank the page.
- Last-refreshed indicator and manual refresh; optional auto-refresh, paused while a modal is open.

**Usage History** — the request-history-shaped investigation table, per the Request History
requirements applied to model calls:

- KPI strip over the current window.
- Backend-filtered, paginated table (`/v1.0/api/usage/events`) with an above-table control bar: total
  count, visible range, page size, first/prev/jump/next/last, refresh.
- Columns: when (relative + exact tooltip), endpoint, model, call kind, tokens (in/cached/out), TTFT,
  latency, tokens/sec, cost, status. Monospace for numeric/technical values; status as a badge, not
  colour alone.
- Filters: endpoint, model, call kind, success/failure, time range.
- Row action menu with View (a detail modal showing the full row, copyable ids, sectioned metadata) and
  View JSON. Row click opens the detail modal. Menus portal above table clipping.
- Empty states distinguish "no calls recorded," "no rows matched these filters," and "telemetry store
  unavailable."

**Pricing** — a form-based editor for `pricing.json` (per-model input/cached/output rates), with a list
of models seen in the data that have no rate yet, and inline "add rate" actions. Follows the Settings
page conventions (form controls, not a raw JSON textarea; save via `PUT /v1.0/api/usage/pricing`).

All destructive or write actions (saving pricing) use the dashboard's custom confirm, not browser
`confirm`. Copy controls reuse the existing copy component. New strings go through the i18n table so the
existing language selector covers them.

### 10.4 Overview integration

The existing `OverviewRoutes` / home page (today "no telemetry, no database") gains a compact usage
strip — tokens and cost for the last 24h and 7d, plus a link into Usage Overview — so the landing page
reflects current spend at a glance, per the "overview is a command centre" rule.

---

## 11. TUI surface

The interactive shell already renders per-turn and session stats in the sidebar (`SidebarView` over
`ConversationStats`): TTFT, stream time, context, turns, input/output/cached tokens. Two changes:

- **Persist what the sidebar already shows.** With the recorder wired into the interactive
  `AgentLoop`, every turn's calls land in `usage.db`, so the sidebar's numbers become durable and
  cross-session rather than in-memory only. `ConversationStats.CachedTokens` starts reflecting real
  data once §5.3 lands; until then it stays 0 as it is today.
- **A `/usage` command.** Added to `MuxCommandCatalog` in the `MuxTuiApp` constructor as a single
  `CommandDescriptor` (id `mux.usage`, category `View`, slash aliases `usage`, `stats`, `spend`), which
  automatically surfaces it in the command menu, footer, and slash router. Its handler opens a modal (or
  a transcript-rendered summary) reading the same store: today's and this-week's tokens and cost, top
  models by spend, and average TTFT/throughput — the TUI's equivalent of the dashboard's Overview, for
  users who never start the server. Because it reads the shared `usage.db`, it reflects activity from
  every mux instance, not just the current one.

The sidebar layout, widths, and formatting (`FormatTokens`) are unchanged.

---

## 12. Watson operational telemetry

Separate from the usage store, and required by `TELEMETRY_REQUIREMENTS.md` for the server itself:

- In `MuxServer.Start`, confirm `WebserverSettings.Telemetry.Enable` (Watson's default) and expose the
  in-process Prometheus scrape at `/metrics` gated behind a setting
  (`Telemetry.Prometheus.Enable`), so the `mux serve` HTTP layer is observable with zero extra
  infrastructure. This gives the four HTTP metrics and `watson.*` server metrics for free.
- The full Prometheus/Grafana/Tempo `compose.yaml` stack the requirements describe is written for a
  Dockerised service. mux is a single distributed binary, not a container deployment, so the compose
  stack is **out of scope** for this plan and flagged as a conflict in §16: the in-process `/metrics`
  endpoint is the proportionate answer, and anyone running mux behind their own Prometheus can scrape
  it. This is called out rather than silently skipped, per the cross-document rule.

The operational metrics and the usage store never mix: per-call token/session/model data stays in
SQLite (high-cardinality, durable, product analytics); aggregate HTTP health stays on Watson's meter
(low-cardinality, ephemeral, operational).

---

## 13. Testing

New Touchstone suites in `src/Test.Shared/Suites/`, each a `static class` with `Create()` returning a
`TestSuiteDescriptor`, registered by one line in `MuxSuites.All`:

- **`UsageStoreSuite`** — schema creation and migration; a single insert/read round-trip; retention
  prune deletes only rows past the cutoff; pricing cost computation for known/unknown/local models. Uses
  the `SettingsCase` helper (temp dir + `MUX_CONFIG_DIR`) so each case gets an isolated `usage.db`.
- **`UsageConcurrencySuite`** — the load-bearing test for the whole design: spin up several writer tasks
  (and, ideally, an out-of-process writer to mirror a second mux instance) against one WAL database and
  assert every row lands with no corruption and no `SQLITE_BUSY` failures escaping the recorder. This is
  what proves the "no clobbering across instances" claim.
- **`UsageAggregationSuite`** — bucketing correctness (hour/day boundaries), endpoint/model/call-kind
  filtering, and exact p50/p95/p99 against a known fixture.
- **`UsageRoutesSuite`** — boots a real `MuxServer` on an ephemeral loopback port (as
  `MuxServerRouteSuite` does), seeds the store, and drives `/v1.0/api/usage/*` over `HttpClient`:
  auth gate, filter translation, pagination shape, `ApiError` on bad enums.

Existing suites that touch the changed types (`LlmBridgeSuite`, `MuxServerRouteSuite`) are extended to
cover `LlmCallMetrics` population and the new route registration.

---

## 14. Documentation and repository requirements

Per `REPOSITORY_REQUIREMENTS.md` and the house doc conventions:

- **`docs/REST_API.md`** — document the six new `/v1.0/api/usage/*` endpoints: method, path, query
  params, response bodies, status codes, auth, and examples. Kept in sync with the route surface.
- **Postman collection** under `assets/postman/` — a "Usage" folder with documented requests for each
  endpoint, base URL and token as variables (never hard-coded), `127.0.0.1` loopback.
- **`docs/CONFIG.md`** — document the `telemetry` settings section and `pricing.json`.
- **`docs/USAGE.md`** — the `/usage` command and the dashboard Analytics views.
- **`CHANGELOG.md`** — an Unreleased "Added" entry in the existing style.
- **`README.md`** — a line under features; verify accuracy per `CODE_STYLE.md`'s README rule.
- **`I18N.md`** — new dashboard strings added to the in-template i18n table for every shipped locale.

---

## 15. Work breakdown

Phased so each phase is independently testable and the capture path lands before anything consumes it.

1. **Capture core.** Widen `LlmUsage`; add `LlmCallMetrics`; rewrite `RecordUsage`; expose `LastCall`.
   Unit-covered by `LlmBridgeSuite`. No storage yet — pure projection of data already in hand.
2. **Storage.** Add `Microsoft.Data.Sqlite`; build `Mux.Core/Telemetry` (`UsageEvent`, `IUsageRecorder`,
   `SqliteUsageStore`, `SqliteUsageRecorder`, `UsageQuery`, retention). `UsageStoreSuite` +
   `UsageConcurrencySuite`.
3. **Wiring.** `TelemetrySettings` (+ the normalizer edit); construct the store/recorder in
   `Program.RunInteractive` and `ServeCommand`; thread `IUsageRecorder` into `AgentLoopOptions` and
   record in `AgentLoop` (primary + compaction + subagent) and `ChatRoutes`.
4. **Pricing.** `pricing.json` seed + manifest tracking; `PricingTable`/`PricingLoader`; cost in the
   query layer.
5. **API.** `UsageRoutes` + DTOs; register in `MuxServer`; `UsageRoutesSuite`; `REST_API.md` + Postman.
6. **Dashboard.** SVG chart helper; Usage Overview, Usage History, Pricing views; home-page strip; i18n.
7. **TUI.** `/usage` command + modal; confirm sidebar persistence.
8. **Operational telemetry.** Confirm Watson telemetry on; optional in-process `/metrics`.
9. **Docs + changelog + README pass.**

---

## 16. Open questions and flagged conflicts

- **SQLite reverses `archive/MUX_COMPARISON.md`.** Confirmed intentional for the analytics workload;
  documented in §2. Raise if the maintainer wants the reversal recorded more prominently.
- **Cached / reasoning tokens need a PolyPrompt upgrade** (§5.3). Until then those series are present
  but zero. Is a PolyPrompt change in appetite, or do we ship with the columns dormant?
- **`project` column granularity** — working-directory basename (readable, local-only) vs a stable hash
  (leak-proof if the data is ever exported). Defaulting to basename; confirm.
- **`mux probe` recording** — capture model-load latency as `call_kind = probe`, or exclude probes from
  analytics? Defaulting to capture, since load latency is genuinely useful.
- **NativeAOT** — `Microsoft.Data.Sqlite` complicates AOT. mux does not ship AOT today; revisit if that
  changes.
- **Pre-existing `Rest` omission in `NormalizeSettingsForPersistence`** — a latent settings-drop bug in
  the same method this plan edits. Fix in the same pass, or leave it alone and only add `Telemetry`?
- **Grafana/Prometheus/Tempo compose stack** (§12) is out of scope for a non-Dockerised CLI; the
  in-process `/metrics` endpoint is the proportionate substitute. Confirm that satisfies the operational
  side of `TELEMETRY_REQUIREMENTS.md` for this product shape.

The design's load-bearing claim is the one worth proving first: that many mux instances can write to a
single WAL database on a local disk with no clobbering and no coordination file. `UsageConcurrencySuite`
exists to demonstrate exactly that, and once it passes, the rest is plumbing onto a substrate the
platform already guarantees.
