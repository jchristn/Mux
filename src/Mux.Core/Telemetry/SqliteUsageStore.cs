namespace Mux.Core.Telemetry
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Data.Sqlite;

    /// <summary>
    /// Owns the local SQLite usage-telemetry database: schema creation and migration, batched inserts,
    /// and retention pruning. Designed for safe concurrent access across multiple mux processes — the
    /// database runs in WAL mode, so many readers and a single writer proceed without blocking each
    /// other, and a rare cross-process writer collision is ridden out by <c>busy_timeout</c>. All writes
    /// are best-effort at the call sites that use this store; this class surfaces failures as exceptions
    /// for the recorder to swallow and log.
    /// </summary>
    /// <remarks>
    /// Thread safety: a fresh pooled connection is opened per operation, so instance methods are safe to
    /// call concurrently. Intended for a local filesystem only; WAL locking is unreliable over network
    /// shares.
    /// </remarks>
    public sealed class SqliteUsageStore : IDisposable
    {
        #region Private-Members

        private const int CurrentSchemaVersion = 1;

        private readonly string _DatabasePath = string.Empty;
        private readonly string _ConnectionString = string.Empty;
        private readonly int _RetentionDays = 90;
        private readonly long _MaxRows = 5_000_000;
        private SqliteConnection? _KeepAlive = null;
        private bool _Disposed = false;

        private const string CreateSchemaSql = @"
CREATE TABLE IF NOT EXISTS schema_version (version INTEGER NOT NULL);

CREATE TABLE IF NOT EXISTS usage_events (
    id                INTEGER PRIMARY KEY AUTOINCREMENT,
    ts_utc            INTEGER NOT NULL,
    run_id            TEXT,
    session_id        TEXT,
    job_id            TEXT,
    call_kind         TEXT NOT NULL,
    command           TEXT,
    endpoint_name     TEXT NOT NULL,
    adapter_type      TEXT NOT NULL,
    model             TEXT NOT NULL,
    base_host         TEXT,
    project           TEXT,
    iteration         INTEGER,
    input_tokens      INTEGER NOT NULL DEFAULT 0,
    cached_tokens     INTEGER NOT NULL DEFAULT 0,
    output_tokens     INTEGER NOT NULL DEFAULT 0,
    reasoning_tokens  INTEGER NOT NULL DEFAULT 0,
    total_tokens      INTEGER NOT NULL DEFAULT 0,
    ttft_ms           INTEGER,
    stream_ms         INTEGER,
    total_ms          INTEGER,
    tokens_per_sec    REAL,
    finish_reason     TEXT,
    success           INTEGER NOT NULL DEFAULT 1,
    error_code        TEXT,
    retry_count       INTEGER NOT NULL DEFAULT 0,
    pricing_ver       TEXT
);

CREATE INDEX IF NOT EXISTS ix_usage_ts       ON usage_events (ts_utc);
CREATE INDEX IF NOT EXISTS ix_usage_endpoint ON usage_events (endpoint_name, ts_utc);
CREATE INDEX IF NOT EXISTS ix_usage_model    ON usage_events (model, ts_utc);
CREATE INDEX IF NOT EXISTS ix_usage_session  ON usage_events (session_id);
";

        private const string InsertSql = @"
INSERT INTO usage_events
    (ts_utc, run_id, session_id, job_id, call_kind, command, endpoint_name, adapter_type, model,
     base_host, project, iteration, input_tokens, cached_tokens, output_tokens, reasoning_tokens,
     total_tokens, ttft_ms, stream_ms, total_ms, tokens_per_sec, finish_reason, success, error_code,
     retry_count, pricing_ver)
VALUES
    ($ts, $run, $session, $job, $kind, $command, $endpoint, $adapter, $model,
     $host, $project, $iteration, $input, $cached, $output, $reasoning,
     $total, $ttft, $stream, $totalms, $tps, $finish, $success, $error,
     $retry, $pricing);
";

        #endregion

        #region Public-Members

        /// <summary>
        /// The resolved absolute path to the SQLite database file.
        /// </summary>
        public string DatabasePath
        {
            get => _DatabasePath;
        }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="SqliteUsageStore"/> class, ensuring the parent
        /// directory and schema exist and that WAL mode is enabled.
        /// </summary>
        /// <param name="databasePath">The absolute path to the database file. Required.</param>
        /// <param name="retentionDays">Days of history to retain; 0 keeps forever. Clamped to 0-3650.</param>
        /// <param name="maxRows">Row-count ceiling before oldest rows are pruned. Floored at 1000.</param>
        /// <exception cref="ArgumentException">Thrown when <paramref name="databasePath"/> is null or blank.</exception>
        /// <exception cref="SqliteException">Thrown when the database cannot be opened or initialized.</exception>
        public SqliteUsageStore(string databasePath, int retentionDays, long maxRows)
        {
            if (string.IsNullOrWhiteSpace(databasePath))
            {
                throw new ArgumentException("Database path is required.", nameof(databasePath));
            }

            _DatabasePath = Path.GetFullPath(databasePath);
            _RetentionDays = Math.Clamp(retentionDays, 0, 3650);
            _MaxRows = Math.Max(1000, maxRows);

            string? directory = Path.GetDirectoryName(_DatabasePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            SqliteConnectionStringBuilder builder = new SqliteConnectionStringBuilder
            {
                DataSource = _DatabasePath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Cache = SqliteCacheMode.Default,

                // Pooling is disabled deliberately. The store keeps its own long-lived keep-alive
                // connection, so pooling adds no benefit, and a pooled handle would keep the database file
                // (and its -wal/-shm sidecars) open after per-operation connections are disposed — which on
                // Windows blocks the config directory from being deleted while the process lives.
                Pooling = false
            };
            _ConnectionString = builder.ToString();

            InitializeSchema();
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Inserts a batch of usage events in a single transaction. Events with a zero timestamp are left
        /// as-is (callers stamp the time before recording).
        /// </summary>
        /// <param name="events">The events to insert. Null or empty is a no-op.</param>
        /// <param name="token">A token to cancel the operation.</param>
        /// <returns>The number of rows inserted.</returns>
        /// <exception cref="SqliteException">Thrown when the write fails.</exception>
        public async Task<int> InsertBatchAsync(IReadOnlyList<UsageEvent>? events, CancellationToken token)
        {
            if (events == null || events.Count == 0)
            {
                return 0;
            }

            token.ThrowIfCancellationRequested();

            await using SqliteConnection connection = await OpenConnectionAsync(token).ConfigureAwait(false);
            await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(token).ConfigureAwait(false);

            int inserted = 0;
            await using (SqliteCommand command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = InsertSql;
                PrepareInsertParameters(command);

                foreach (UsageEvent usageEvent in events)
                {
                    if (usageEvent == null)
                    {
                        continue;
                    }

                    token.ThrowIfCancellationRequested();
                    BindInsertParameters(command, usageEvent);
                    inserted += await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
            }

            await transaction.CommitAsync(token).ConfigureAwait(false);
            return inserted;
        }

        /// <summary>
        /// Deletes rows beyond the configured retention window and row ceiling.
        /// </summary>
        /// <param name="nowUnixMs">The current UTC time as Unix epoch milliseconds.</param>
        /// <param name="token">A token to cancel the operation.</param>
        /// <returns>The number of rows deleted.</returns>
        /// <exception cref="SqliteException">Thrown when the delete fails.</exception>
        public async Task<int> PruneAsync(long nowUnixMs, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            await using SqliteConnection connection = await OpenConnectionAsync(token).ConfigureAwait(false);

            int deleted = 0;

            if (_RetentionDays > 0)
            {
                long cutoff = nowUnixMs - ((long)_RetentionDays * 24L * 60L * 60L * 1000L);
                await using SqliteCommand command = connection.CreateCommand();
                command.CommandText = "DELETE FROM usage_events WHERE ts_utc < $cutoff;";
                command.Parameters.AddWithValue("$cutoff", cutoff);
                deleted += await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            }

            // Secondary guard: cap total rows, deleting the oldest beyond the ceiling.
            await using (SqliteCommand capCommand = connection.CreateCommand())
            {
                capCommand.CommandText = @"
DELETE FROM usage_events
WHERE id IN (
    SELECT id FROM usage_events
    ORDER BY ts_utc DESC, id DESC
    LIMIT -1 OFFSET $max
);";
                capCommand.Parameters.AddWithValue("$max", _MaxRows);
                deleted += await capCommand.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            }

            return deleted;
        }

        /// <summary>
        /// Fetches aggregate rows grouped by (bucket, model, adapter, endpoint, command, call-kind) for a
        /// filter. When <paramref name="bucketMs"/> is 0 the rows are not time-bucketed (a single bucket of
        /// 0). The query service rolls these up into summaries, time series, and breakdowns.
        /// </summary>
        /// <param name="filter">The filter to apply. Null applies no constraints.</param>
        /// <param name="bucketMs">The bucket width in ms (for example 3600000 for hourly), or 0 for none.</param>
        /// <param name="token">A token to cancel the operation.</param>
        /// <returns>The aggregate rows.</returns>
        /// <exception cref="SqliteException">Thrown when the query fails.</exception>
        public async Task<List<UsageAggregateRow>> FetchAggregatesAsync(UsageFilter? filter, long bucketMs, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            string bucketExpr = bucketMs > 0 ? "(ts_utc / " + bucketMs.ToString(CultureInfo.InvariantCulture) + ") * " + bucketMs.ToString(CultureInfo.InvariantCulture) : "0";

            System.Text.StringBuilder sql = new System.Text.StringBuilder();
            sql.Append("SELECT ").Append(bucketExpr).Append(" AS bucket, model, adapter_type, endpoint_name, IFNULL(command,'') AS cmd, call_kind, ");
            sql.Append("COUNT(*) AS calls, SUM(CASE WHEN success=0 THEN 1 ELSE 0 END) AS errors, ");
            sql.Append("SUM(input_tokens), SUM(cached_tokens), SUM(output_tokens), SUM(total_tokens), ");
            sql.Append("SUM(CASE WHEN ttft_ms IS NOT NULL THEN ttft_ms ELSE 0 END), SUM(CASE WHEN ttft_ms IS NOT NULL THEN 1 ELSE 0 END), ");
            sql.Append("SUM(CASE WHEN total_ms IS NOT NULL THEN total_ms ELSE 0 END), SUM(CASE WHEN total_ms IS NOT NULL THEN 1 ELSE 0 END), ");
            sql.Append("SUM(CASE WHEN stream_ms IS NOT NULL THEN stream_ms ELSE 0 END), SUM(CASE WHEN stream_ms IS NOT NULL THEN 1 ELSE 0 END), ");
            sql.Append("SUM(CASE WHEN tokens_per_sec IS NOT NULL THEN tokens_per_sec ELSE 0 END), SUM(CASE WHEN tokens_per_sec IS NOT NULL THEN 1 ELSE 0 END) ");
            sql.Append("FROM usage_events");

            await using SqliteConnection connection = await OpenConnectionAsync(token).ConfigureAwait(false);
            await using SqliteCommand command = connection.CreateCommand();
            AppendWhere(sql, command, filter);
            sql.Append(" GROUP BY bucket, model, adapter_type, endpoint_name, cmd, call_kind");
            command.CommandText = sql.ToString();

            List<UsageAggregateRow> rows = new List<UsageAggregateRow>();
            await using SqliteDataReader reader = (SqliteDataReader)await command.ExecuteReaderAsync(token).ConfigureAwait(false);
            while (await reader.ReadAsync(token).ConfigureAwait(false))
            {
                rows.Add(new UsageAggregateRow
                {
                    BucketStartUnixMs = reader.GetInt64(0),
                    Model = reader.GetString(1),
                    AdapterType = reader.GetString(2),
                    EndpointName = reader.GetString(3),
                    Command = reader.GetString(4),
                    CallKind = reader.GetString(5),
                    Calls = reader.GetInt64(6),
                    Errors = reader.GetInt64(7),
                    InputTokens = reader.GetInt64(8),
                    CachedTokens = reader.GetInt64(9),
                    OutputTokens = reader.GetInt64(10),
                    TotalTokens = reader.GetInt64(11),
                    TtftSumMs = reader.GetInt64(12),
                    TtftCount = reader.GetInt64(13),
                    TotalMsSum = reader.GetInt64(14),
                    TotalMsCount = reader.GetInt64(15),
                    StreamMsSum = reader.GetInt64(16),
                    StreamMsCount = reader.GetInt64(17),
                    TokensPerSecSum = reader.GetDouble(18),
                    TokensPerSecCount = reader.GetInt64(19)
                });
            }

            return rows;
        }

        /// <summary>
        /// Fetches per-call latency samples (time-to-first-token and total runtime) for a filter, tagged with
        /// their bucket, for percentile computation. Rows with both timings null are omitted.
        /// </summary>
        /// <param name="filter">The filter to apply. Null applies no constraints.</param>
        /// <param name="bucketMs">The bucket width in ms, or 0 for a single bucket.</param>
        /// <param name="token">A token to cancel the operation.</param>
        /// <returns>The latency samples.</returns>
        /// <exception cref="SqliteException">Thrown when the query fails.</exception>
        public async Task<List<UsageLatencySample>> FetchLatencySamplesAsync(UsageFilter? filter, long bucketMs, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            string bucketExpr = bucketMs > 0 ? "(ts_utc / " + bucketMs.ToString(CultureInfo.InvariantCulture) + ") * " + bucketMs.ToString(CultureInfo.InvariantCulture) : "0";

            System.Text.StringBuilder sql = new System.Text.StringBuilder();
            sql.Append("SELECT ").Append(bucketExpr).Append(" AS bucket, ttft_ms, total_ms FROM usage_events");

            await using SqliteConnection connection = await OpenConnectionAsync(token).ConfigureAwait(false);
            await using SqliteCommand command = connection.CreateCommand();
            AppendWhere(sql, command, filter, "(ttft_ms IS NOT NULL OR total_ms IS NOT NULL)");
            command.CommandText = sql.ToString();

            List<UsageLatencySample> samples = new List<UsageLatencySample>();
            await using SqliteDataReader reader = (SqliteDataReader)await command.ExecuteReaderAsync(token).ConfigureAwait(false);
            while (await reader.ReadAsync(token).ConfigureAwait(false))
            {
                samples.Add(new UsageLatencySample
                {
                    BucketStartUnixMs = reader.GetInt64(0),
                    TimeToFirstTokenMs = reader.IsDBNull(1) ? (long?)null : reader.GetInt64(1),
                    TotalMs = reader.IsDBNull(2) ? (long?)null : reader.GetInt64(2)
                });
            }

            return samples;
        }

        /// <summary>
        /// Fetches a page of raw usage events (most recent first) matching a filter. Derived cost is not set
        /// here; the query service applies it.
        /// </summary>
        /// <param name="filter">The filter to apply. Null applies no constraints.</param>
        /// <param name="pageNumber">The 1-based page number. Values below 1 are treated as 1.</param>
        /// <param name="pageSize">The page size. Clamped to the range 1-500.</param>
        /// <param name="token">A token to cancel the operation.</param>
        /// <returns>The event rows for the page (without cost).</returns>
        /// <exception cref="SqliteException">Thrown when the query fails.</exception>
        public async Task<List<UsageEventRow>> QueryEventsAsync(UsageFilter? filter, int pageNumber, int pageSize, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            int page = pageNumber < 1 ? 1 : pageNumber;
            int size = Math.Clamp(pageSize, 1, 500);
            int offset = (page - 1) * size;

            System.Text.StringBuilder sql = new System.Text.StringBuilder();
            sql.Append("SELECT id, ts_utc, run_id, session_id, call_kind, command, endpoint_name, adapter_type, model, ");
            sql.Append("base_host, project, input_tokens, cached_tokens, output_tokens, reasoning_tokens, total_tokens, ");
            sql.Append("ttft_ms, stream_ms, total_ms, tokens_per_sec, finish_reason, success, error_code FROM usage_events");

            await using SqliteConnection connection = await OpenConnectionAsync(token).ConfigureAwait(false);
            await using SqliteCommand command = connection.CreateCommand();
            AppendWhere(sql, command, filter);
            sql.Append(" ORDER BY ts_utc DESC, id DESC LIMIT $limit OFFSET $offset");
            command.Parameters.AddWithValue("$limit", size);
            command.Parameters.AddWithValue("$offset", offset);
            command.CommandText = sql.ToString();

            List<UsageEventRow> rows = new List<UsageEventRow>();
            await using SqliteDataReader reader = (SqliteDataReader)await command.ExecuteReaderAsync(token).ConfigureAwait(false);
            while (await reader.ReadAsync(token).ConfigureAwait(false))
            {
                rows.Add(new UsageEventRow
                {
                    Id = reader.GetInt64(0),
                    TimestampUnixMs = reader.GetInt64(1),
                    RunId = reader.IsDBNull(2) ? null : reader.GetString(2),
                    SessionId = reader.IsDBNull(3) ? null : reader.GetString(3),
                    CallKind = reader.GetString(4),
                    Command = reader.IsDBNull(5) ? null : reader.GetString(5),
                    EndpointName = reader.GetString(6),
                    AdapterType = reader.GetString(7),
                    Model = reader.GetString(8),
                    BaseHost = reader.IsDBNull(9) ? null : reader.GetString(9),
                    Project = reader.IsDBNull(10) ? null : reader.GetString(10),
                    InputTokens = reader.GetInt32(11),
                    CachedTokens = reader.GetInt32(12),
                    OutputTokens = reader.GetInt32(13),
                    ReasoningTokens = reader.GetInt32(14),
                    TotalTokens = reader.GetInt32(15),
                    TimeToFirstTokenMs = reader.IsDBNull(16) ? (long?)null : reader.GetInt64(16),
                    StreamingMs = reader.IsDBNull(17) ? (long?)null : reader.GetInt64(17),
                    TotalMs = reader.IsDBNull(18) ? (long?)null : reader.GetInt64(18),
                    TokensPerSecond = reader.IsDBNull(19) ? (double?)null : reader.GetDouble(19),
                    FinishReason = reader.IsDBNull(20) ? null : reader.GetString(20),
                    Success = reader.GetInt64(21) != 0,
                    ErrorCode = reader.IsDBNull(22) ? null : reader.GetString(22)
                });
            }

            return rows;
        }

        /// <summary>
        /// Counts the usage events matching a filter (for pagination totals).
        /// </summary>
        /// <param name="filter">The filter to apply. Null applies no constraints.</param>
        /// <param name="token">A token to cancel the operation.</param>
        /// <returns>The matching row count.</returns>
        /// <exception cref="SqliteException">Thrown when the query fails.</exception>
        public async Task<long> CountEventsAsync(UsageFilter? filter, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            System.Text.StringBuilder sql = new System.Text.StringBuilder();
            sql.Append("SELECT COUNT(*) FROM usage_events");

            await using SqliteConnection connection = await OpenConnectionAsync(token).ConfigureAwait(false);
            await using SqliteCommand command = connection.CreateCommand();
            AppendWhere(sql, command, filter);
            command.CommandText = sql.ToString();

            object? result = await command.ExecuteScalarAsync(token).ConfigureAwait(false);
            return result == null || result == DBNull.Value ? 0L : Convert.ToInt64(result, CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Returns the distinct endpoint names and models seen in the store, for populating filter controls.
        /// </summary>
        /// <param name="column">Either "endpoint_name" or "model".</param>
        /// <param name="token">A token to cancel the operation.</param>
        /// <returns>The distinct values, ascending.</returns>
        /// <exception cref="SqliteException">Thrown when the query fails.</exception>
        public async Task<List<string>> DistinctValuesAsync(string column, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            // Whitelist the column name — it is not a bindable parameter, so it must never come from user input.
            string safeColumn = column == "model" ? "model" : "endpoint_name";

            await using SqliteConnection connection = await OpenConnectionAsync(token).ConfigureAwait(false);
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "SELECT DISTINCT " + safeColumn + " FROM usage_events ORDER BY " + safeColumn + " ASC;";

            List<string> values = new List<string>();
            await using SqliteDataReader reader = (SqliteDataReader)await command.ExecuteReaderAsync(token).ConfigureAwait(false);
            while (await reader.ReadAsync(token).ConfigureAwait(false))
            {
                if (!reader.IsDBNull(0))
                {
                    values.Add(reader.GetString(0));
                }
            }

            return values;
        }

        /// <summary>
        /// Deletes a single usage event by its database id.
        /// </summary>
        /// <param name="id">The row id to delete.</param>
        /// <param name="token">A token to cancel the operation.</param>
        /// <returns>The number of rows deleted (0 or 1).</returns>
        /// <exception cref="SqliteException">Thrown when the delete fails.</exception>
        public async Task<int> DeleteEventAsync(long id, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            await using SqliteConnection connection = await OpenConnectionAsync(token).ConfigureAwait(false);
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "DELETE FROM usage_events WHERE id = $id;";
            command.Parameters.AddWithValue("$id", id);
            return await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
        }

        /// <summary>
        /// Returns the total number of recorded usage events.
        /// </summary>
        /// <param name="token">A token to cancel the operation.</param>
        /// <returns>The row count.</returns>
        /// <exception cref="SqliteException">Thrown when the query fails.</exception>
        public async Task<long> CountAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            await using SqliteConnection connection = await OpenConnectionAsync(token).ConfigureAwait(false);
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM usage_events;";
            object? result = await command.ExecuteScalarAsync(token).ConfigureAwait(false);
            return result == null || result == DBNull.Value ? 0L : Convert.ToInt64(result, CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Releases the resources used by this store.
        /// </summary>
        public void Dispose()
        {
            if (_Disposed)
            {
                return;
            }

            _KeepAlive?.Dispose();
            _KeepAlive = null;
            _Disposed = true;
        }

        #endregion

        #region Private-Methods

        private void InitializeSchema()
        {
            // Hold one keep-alive connection open for the store's lifetime so WAL mode stays established
            // and the pool keeps a warm handle; per-operation connections are still opened separately.
            _KeepAlive = new SqliteConnection(_ConnectionString);
            _KeepAlive.Open();
            ApplyConnectionPragmas(_KeepAlive, setWalMode: true);

            using (SqliteCommand schemaCommand = _KeepAlive.CreateCommand())
            {
                schemaCommand.CommandText = CreateSchemaSql;
                schemaCommand.ExecuteNonQuery();
            }

            EnsureSchemaVersion(_KeepAlive);
        }

        private static void EnsureSchemaVersion(SqliteConnection connection)
        {
            long existing;
            using (SqliteCommand read = connection.CreateCommand())
            {
                read.CommandText = "SELECT version FROM schema_version LIMIT 1;";
                object? value = read.ExecuteScalar();
                existing = value == null || value == DBNull.Value ? -1L : Convert.ToInt64(value, CultureInfo.InvariantCulture);
            }

            if (existing < 0)
            {
                using SqliteCommand insert = connection.CreateCommand();
                insert.CommandText = "INSERT INTO schema_version (version) VALUES ($v);";
                insert.Parameters.AddWithValue("$v", CurrentSchemaVersion);
                insert.ExecuteNonQuery();
            }

            // Future migrations: compare `existing` to CurrentSchemaVersion and apply ALTER steps here.
        }

        private async Task<SqliteConnection> OpenConnectionAsync(CancellationToken token)
        {
            SqliteConnection connection = new SqliteConnection(_ConnectionString);
            await connection.OpenAsync(token).ConfigureAwait(false);
            ApplyConnectionPragmas(connection, setWalMode: false);
            return connection;
        }

        private static void ApplyConnectionPragmas(SqliteConnection connection, bool setWalMode)
        {
            using SqliteCommand command = connection.CreateCommand();

            // journal_mode=WAL is persisted in the database header, so it is set once on the keep-alive
            // connection. synchronous and busy_timeout are per-connection and set on every open.
            command.CommandText = setWalMode
                ? "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA busy_timeout=5000;"
                : "PRAGMA synchronous=NORMAL; PRAGMA busy_timeout=5000;";
            command.ExecuteNonQuery();
        }

        private static void PrepareInsertParameters(SqliteCommand command)
        {
            command.Parameters.Add("$ts", SqliteType.Integer);
            command.Parameters.Add("$run", SqliteType.Text);
            command.Parameters.Add("$session", SqliteType.Text);
            command.Parameters.Add("$job", SqliteType.Text);
            command.Parameters.Add("$kind", SqliteType.Text);
            command.Parameters.Add("$command", SqliteType.Text);
            command.Parameters.Add("$endpoint", SqliteType.Text);
            command.Parameters.Add("$adapter", SqliteType.Text);
            command.Parameters.Add("$model", SqliteType.Text);
            command.Parameters.Add("$host", SqliteType.Text);
            command.Parameters.Add("$project", SqliteType.Text);
            command.Parameters.Add("$iteration", SqliteType.Integer);
            command.Parameters.Add("$input", SqliteType.Integer);
            command.Parameters.Add("$cached", SqliteType.Integer);
            command.Parameters.Add("$output", SqliteType.Integer);
            command.Parameters.Add("$reasoning", SqliteType.Integer);
            command.Parameters.Add("$total", SqliteType.Integer);
            command.Parameters.Add("$ttft", SqliteType.Integer);
            command.Parameters.Add("$stream", SqliteType.Integer);
            command.Parameters.Add("$totalms", SqliteType.Integer);
            command.Parameters.Add("$tps", SqliteType.Real);
            command.Parameters.Add("$finish", SqliteType.Text);
            command.Parameters.Add("$success", SqliteType.Integer);
            command.Parameters.Add("$error", SqliteType.Text);
            command.Parameters.Add("$retry", SqliteType.Integer);
            command.Parameters.Add("$pricing", SqliteType.Text);
        }

        private static void BindInsertParameters(SqliteCommand command, UsageEvent e)
        {
            command.Parameters["$ts"].Value = e.TimestampUnixMs;
            command.Parameters["$run"].Value = NullableText(e.RunId);
            command.Parameters["$session"].Value = NullableText(e.SessionId);
            command.Parameters["$job"].Value = NullableText(e.JobId);
            command.Parameters["$kind"].Value = e.CallKind.ToString().ToLowerInvariant();
            command.Parameters["$command"].Value = NullableText(e.Command);
            command.Parameters["$endpoint"].Value = string.IsNullOrEmpty(e.EndpointName) ? "unknown" : e.EndpointName;
            command.Parameters["$adapter"].Value = string.IsNullOrEmpty(e.AdapterType) ? "unknown" : e.AdapterType;
            command.Parameters["$model"].Value = string.IsNullOrEmpty(e.Model) ? "unknown" : e.Model;
            command.Parameters["$host"].Value = NullableText(e.BaseHost);
            command.Parameters["$project"].Value = NullableText(e.Project);
            command.Parameters["$iteration"].Value = e.Iteration.HasValue ? e.Iteration.Value : (object)DBNull.Value;
            command.Parameters["$input"].Value = e.InputTokens;
            command.Parameters["$cached"].Value = e.CachedTokens;
            command.Parameters["$output"].Value = e.OutputTokens;
            command.Parameters["$reasoning"].Value = e.ReasoningTokens;
            command.Parameters["$total"].Value = e.TotalTokens;
            command.Parameters["$ttft"].Value = e.TimeToFirstTokenMs.HasValue ? e.TimeToFirstTokenMs.Value : (object)DBNull.Value;
            command.Parameters["$stream"].Value = e.StreamingMs.HasValue ? e.StreamingMs.Value : (object)DBNull.Value;
            command.Parameters["$totalms"].Value = e.TotalMs.HasValue ? e.TotalMs.Value : (object)DBNull.Value;
            command.Parameters["$tps"].Value = e.TokensPerSecond.HasValue ? e.TokensPerSecond.Value : (object)DBNull.Value;
            command.Parameters["$finish"].Value = NullableText(e.FinishReason);
            command.Parameters["$success"].Value = e.Success ? 1 : 0;
            command.Parameters["$error"].Value = NullableText(e.ErrorCode);
            command.Parameters["$retry"].Value = e.RetryCount;
            command.Parameters["$pricing"].Value = NullableText(e.PricingVersion);
        }

        private static object NullableText(string? value)
        {
            return string.IsNullOrEmpty(value) ? DBNull.Value : value;
        }

        private static void AppendWhere(System.Text.StringBuilder sql, SqliteCommand command, UsageFilter? filter, string? extraCondition = null)
        {
            List<string> conditions = new List<string>();

            if (filter != null)
            {
                if (filter.FromUnixMs > 0)
                {
                    conditions.Add("ts_utc >= $from");
                    command.Parameters.AddWithValue("$from", filter.FromUnixMs);
                }

                if (filter.ToUnixMs > 0)
                {
                    conditions.Add("ts_utc <= $to");
                    command.Parameters.AddWithValue("$to", filter.ToUnixMs);
                }

                if (!string.IsNullOrWhiteSpace(filter.EndpointName))
                {
                    conditions.Add("endpoint_name = $endpoint");
                    command.Parameters.AddWithValue("$endpoint", filter.EndpointName);
                }

                if (!string.IsNullOrWhiteSpace(filter.Model))
                {
                    conditions.Add("model = $fmodel");
                    command.Parameters.AddWithValue("$fmodel", filter.Model);
                }

                if (filter.CallKind.HasValue)
                {
                    conditions.Add("call_kind = $kind");
                    command.Parameters.AddWithValue("$kind", filter.CallKind.Value.ToString().ToLowerInvariant());
                }

                if (filter.Success.HasValue)
                {
                    conditions.Add("success = $succ");
                    command.Parameters.AddWithValue("$succ", filter.Success.Value ? 1 : 0);
                }
            }

            if (!string.IsNullOrEmpty(extraCondition))
            {
                conditions.Add(extraCondition!);
            }

            if (conditions.Count > 0)
            {
                sql.Append(" WHERE ").Append(string.Join(" AND ", conditions));
            }
        }

        #endregion
    }
}
