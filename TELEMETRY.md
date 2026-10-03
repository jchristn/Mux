# mux Telemetry

mux emits OpenTelemetry **metrics**, **traces**, and **logs** so an operator can watch it work and find out why
when it doesn't, from Grafana (Prometheus, Tempo, Loki) or any OTLP backend. This document is the contract: every
meter and activity-source name, every metric family and label, every span, how to turn export on, how to subscribe
from your own host, the bundled stack and dashboards, and recommended alerts.

> This is separate from mux's **usage history** (`settings.json` `telemetry` block, the SQLite `usage.db` behind
> the `/usage` view and the dashboard's Usage page). Usage history is a product feature for the person using mux.
> OpenTelemetry export, described here, is for operators.

## Contents

1. [How it fits together](#how-it-fits-together)
2. [Quick start](#quick-start)
3. [Turning export on](#turning-export-on)
4. [Subscribing from your own host](#subscribing-from-your-own-host)
5. [Metrics catalog](#metrics-catalog)
6. [Spans catalog](#spans-catalog)
7. [Context propagation](#context-propagation)
8. [Logs](#logs)
9. [The observability stack](#the-observability-stack)
10. [Dashboards](#dashboards)
11. [Recommended alerts](#recommended-alerts)
12. [Guarantees: cost, safety, cardinality](#guarantees-cost-safety-cardinality)
13. [Known gaps](#known-gaps)

## How it fits together

| Layer | What it is | Telemetry role |
|---|---|---|
| `Mux.Core` (NuGet library) | The engine: agent loop, LLM client, tools, MCP, jobs, sessions, usage writer | Emits on a BCL `Meter` and `ActivitySource`, both named **`Mux`**. No exporter or SDK dependency; effectively free when nothing listens. |
| `Mux.Server` (NuGet library) | The local REST + WebSocket server (Watson 7.2) | Watson's built-in telemetry (meter and source **`Watson`**) is explicitly on: HTTP server metrics and one server span per request that adopts an inbound `traceparent`. Mux spans nest under it. |
| `mux`, tray agent, desktop app (executables) | Composition roots | Each starts **one** [Radiant](https://www.nuget.org/packages/Radiant) host (`Radiant` 0.1.2) when export is enabled. It subscribes to `Mux`, `Watson`, and `System.Net.Http`, pushes OTLP, optionally serves Prometheus `/metrics` in-process, ships logs, and adds .NET runtime and process metrics. Disposed on exit so buffers flush. |

Libraries never reference Radiant; the hosts subscribe by name. Export is **off by default** because mux usually runs
on a developer machine with no collector (emission itself is always on and costs nanoseconds when unobserved).

## Quick start

```bash
# 1. Start the stack (OpenTelemetry Collector, Prometheus, Tempo, Loki, Grafana)
docker compose -f docker/compose.yaml up -d

# 2. Run mux with export on (or set "observability": { "enabled": true } in ~/.mux/settings.json)
MUX_OBSERVABILITY_ENABLED=true mux serve
#   PowerShell:  $env:MUX_OBSERVABILITY_ENABLED="true"; mux serve

# 3. Open Grafana and the "Mux" folder
open http://127.0.0.1:3000      # admin / admin (local development default)
```

`mux serve` prints a `telemetry` line when export is active. The dashboard's home page has an **External services**
card with each tool's URL and default credentials.

## Turning export on

Configure the `observability` block in `settings.json` (see also [docs/CONFIG.md](docs/CONFIG.md)):

```json
{
  "observability": {
    "enabled": true,
    "serviceName": "mux",
    "otlpEnabled": true,
    "otlpEndpoint": "http://127.0.0.1:4317",
    "otlpProtocol": "grpc",
    "prometheusEnabled": false,
    "prometheusHostname": "127.0.0.1",
    "prometheusPort": 9464,
    "logsEnabled": true,
    "lokiEnabled": false,
    "lokiEndpoint": "http://127.0.0.1:3100/otlp",
    "traceSamplingRatio": 1.0,
    "metricsExportIntervalMs": 15000,
    "grafanaUrl": "http://127.0.0.1:3000",
    "prometheusUrl": "http://127.0.0.1:9090",
    "tempoUrl": "http://127.0.0.1:3200",
    "lokiUrl": "http://127.0.0.1:3100"
  }
}
```

| Key | Default | Meaning |
|---|---|---|
| `enabled` | `false` | Master switch. When false no exporter starts, no port is bound, no connection is opened. |
| `serviceName` | `mux` | `service.name` on every signal; becomes the Prometheus `job` label through the collector. |
| `otlpEnabled` | `true` | Push metrics, traces, and logs over OTLP. |
| `otlpEndpoint` | `http://127.0.0.1:4317` | Collector endpoint. Absolute http(s) URI; invalid values reset to the default. |
| `otlpProtocol` | `grpc` | `grpc` (port 4317) or `httpprotobuf` (port 4318). |
| `prometheusEnabled` | `false` | Serve `/metrics` in-process. Only long-running processes (`mux serve`, tray agent, desktop) bind it; short CLI commands never do. A port conflict (for example `mux serve` and the tray agent both running) falls back to OTLP-only. |
| `prometheusHostname` | `127.0.0.1` | Bind address for `/metrics`. The endpoint has no authentication. |
| `prometheusPort` | `9464` | Clamped 1-65535. |
| `logsEnabled` | `true` | Export mux diagnostic log lines as OpenTelemetry logs with trace correlation. |
| `lokiEnabled` / `lokiEndpoint` | `false` / `http://127.0.0.1:3100/otlp` | Push logs directly to Loki 3.x (the bundled stack routes logs through the collector instead). |
| `traceSamplingRatio` | `1.0` | Parent-based head sampling for root spans, clamped 0-1. |
| `metricsExportIntervalMs` | `15000` | OTLP metric push cadence, clamped 1000-300000. |
| `grafanaUrl`, `prometheusUrl`, `tempoUrl`, `lokiUrl` | `http://127.0.0.1:3000` / `:9090` / `:3200` / `:3100` | Browser URLs shown on the dashboard's External services card. Display only. |

Environment overrides (applied on load; unset variables change nothing):

| Variable | Overrides |
|---|---|
| `MUX_OBSERVABILITY_ENABLED` | `enabled` (`1`/`true`/`yes`/`on` or `0`/`false`/`no`/`off`) |
| `MUX_OTLP_ENDPOINT` | `otlpEndpoint` |
| `MUX_OTLP_PROTOCOL` | `otlpProtocol` |
| `MUX_PROMETHEUS_ENABLED` | `prometheusEnabled` |
| `MUX_PROMETHEUS_PORT` | `prometheusPort` |
| `MUX_OTEL_SERVICE_NAME` | `serviceName` |

Which processes export: `mux` (every command; only `mux serve` may bind Prometheus), the tray agent (`Mux.Agent`), and
the desktop app (`Mux.Desktop`). Each process is one OpenTelemetry resource with its own `service.instance.id`.

## Subscribing from your own host

If you embed `Mux.Core` or `Mux.Server` in your own application, subscribe to the names. Nothing else is needed.

With Radiant:

```csharp
RadiantSettings settings = new RadiantSettings("my-app");
settings.Sources.AddMeter("Mux");
settings.Sources.AddActivitySource("Mux");
settings.Sources.AddMeter("Watson");            // if you host Mux.Server
settings.Sources.AddActivitySource("Watson");
using (RadiantHost host = RadiantHost.Start(settings)) { /* run */ }
```

With the OpenTelemetry SDK directly:

```csharp
builder.Services.AddOpenTelemetry()
    .WithMetrics(m => m.AddMeter("Mux", "Watson").AddOtlpExporter())
    .WithTracing(t => t.AddSource("Mux", "Watson").AddOtlpExporter());
```

Names are available as constants: `Mux.Core.Observability.MuxTelemetryNames.MeterName` / `.ActivitySourceName`.
Histograms are in seconds and span milliseconds to minutes, so give them explicit buckets: `MuxTelemetry.LongHistogramNames`
with `MuxTelemetry.LongBuckets` (10 ms to 30 min), and `MuxTelemetry.ShortHistogramNames` with `MuxTelemetry.ShortBuckets`
(0.5 ms to 10 s). The mux executables do this through Radiant's metric catalog. For tests, a plain `MeterListener` and
`ActivityListener` see everything with no exporter.

## Metrics catalog

Meter: **`Mux`** (version = product version). Instrument names are dotted; Prometheus exporters rewrite them to snake
case with unit and `_total` suffixes, and dotted label keys become underscores (`error.type` becomes `error_type`).
Every label is bounded: ids, prompts, paths, URLs, and model or endpoint names go on spans, never on metrics.

### Agent workflow

| Instrument (Prometheus name) | Type | Unit | Labels | Description |
|---|---|---|---|---|
| `mux.agent.runs` (`mux_agent_runs_total`) | counter | `{run}` | `outcome`, `call_kind` | Finished agent runs. |
| `mux.agent.run.duration` (`mux_agent_run_duration_seconds`) | histogram | s | `outcome`, `call_kind` | End-to-end run duration. |
| `mux.agent.runs.active` (`mux_agent_runs_active`) | up/down counter | `{run}` | none | Runs executing now. |
| `mux.agent.stage.duration` (`mux_agent_stage_duration_seconds`) | histogram | s | `stage`, `outcome` | Per-stage duration: `llm`, `approval` (includes waiting for a human), `tool`, `compaction`, `write_lease_wait`. |
| `mux.agent.iterations` (`mux_agent_iterations`) | histogram | `{iteration}` | `outcome` | Model calls per run. |
| `mux.agent.errors` (`mux_agent_errors_total`) | counter | `{error}` | `error.type` | Error events raised by runs (`llm_connection_error`, `llm_error`, `llm_stream_error`, `tool_call_denied`, `approval_error`, `context_limit_exceeded`, `budget_exceeded`, `max_iterations_reached`, ...). |
| `mux.agent.compactions` (`mux_agent_compactions_total`) | counter | `{compaction}` | `strategy` | In-run context compactions (`summary`, `trim`, `summary+trim`). |
| `mux.tool.calls` (`mux_tool_calls_total`) | counter | `{call}` | `tool.kind`, `tool.name`, `outcome` | Tool calls. `tool.kind`: `builtin`, `mcp`, `skill`, `external`, `unknown`. `tool.name` is the built-in name; other kinds collapse to the kind. `outcome`: `success`, `failure`, `error`, `denied`. |
| `mux.tool.duration` (`mux_tool_duration_seconds`) | histogram | s | `tool.kind`, `tool.name`, `outcome` | Tool execution time (denied calls never execute and are not timed). |
| `mux.approval.decisions` (`mux_approval_decisions_total`) | counter | `{decision}` | `decision` | `approved`, `denied`, `policy_denied` (allow/deny list or sandbox), `error`. |
| `mux.subagent.runs` (`mux_subagent_runs_total`) | counter | `{run}` | `outcome` | Subagent delegations. |
| `mux.subagent.duration` (`mux_subagent_duration_seconds`) | histogram | s | `outcome` | Subagent run duration. |

`outcome` on runs is the run status: `completed`, `completed_with_errors`, `max_iterations_reached`, `budget_exceeded`,
or `cancelled`, `failed` (unhandled exception), `abandoned` (the consumer stopped reading). `call_kind`: `primary`,
`compaction`, `subagent`, `chat`.

### LLM providers

| Instrument (Prometheus name) | Type | Unit | Labels | Description |
|---|---|---|---|---|
| `mux.llm.requests` (`mux_llm_requests_total`) | counter | `{request}` | `gen_ai.provider.name`, `gen_ai.operation.name`, `outcome` | Model calls. Provider is the mux adapter type (`ollama`, `openai`, `vllm`, `openaicompatible`, `anthropic`, `gemini`, `azureopenai`, `vertex`, `bedrock`). Operation: `chat_stream` (agent turns), `chat` (sidecar: compaction and summaries), `model_load` (probes). Outcome: `success`, `llm_connection_error`, `llm_error`, `llm_stream_error`, `cancelled`, `error`, `abandoned`. |
| `mux.llm.request.duration` (`mux_llm_request_duration_seconds`) | histogram | s | same | Request duration (a streaming call is timed to the last chunk). |
| `mux.llm.time_to_first_token` (`mux_llm_time_to_first_token_seconds`) | histogram | s | `gen_ai.provider.name` | Provider-reported time to first token. |
| `mux.llm.tokens` (`mux_llm_tokens_total`) | counter | `{token}` | `gen_ai.provider.name`, `gen_ai.token.type` | Provider-reported tokens: `input`, `output`, `cached`, `reasoning`. |
| `mux.llm.retries` (`mux_llm_retries_total`) | counter | `{retry}` | `gen_ai.provider.name`, `gen_ai.operation.name` | Transport-level retries (connection refused, reset, DNS, TLS). |

### Integrations

| Instrument (Prometheus name) | Type | Unit | Labels | Description |
|---|---|---|---|---|
| `mux.integration.requests` (`mux_integration_requests_total`) | counter | `{request}` | `service`, `operation`, `outcome` | Every outbound call. `service`/`operation`: `llm` (`chat_stream`, `chat`, `model_load`), `mcp` (`connect`, `tools/list`, `tools/call`), `web_search` (`search`), `git` (the git subcommand: `add`, `write-tree`, `commit-tree`, `read-tree`, `ls-files`, `ls-tree`, `rev-parse`, `status`, ...), `hook` (the hook event: `sessionstart`, `userpromptsubmit`, `sessionend`). Outcome: `success`, `failure` (the call answered with a failure: MCP `isError`, git non-zero exit, hook non-zero exit), `error`, `timeout`, `cancelled`. |
| `mux.integration.duration` (`mux_integration_duration_seconds`) | histogram | s | same | Outbound call latency. |

Hosts also subscribe to the BCL `System.Net.Http` meter, so `http_client_request_duration_seconds` (labels
`server_address`, `http_request_method`, `error_type`, ...) gives raw HTTP timing per provider host.

### Jobs and background work

| Instrument (Prometheus name) | Type | Unit | Labels | Description |
|---|---|---|---|---|
| `mux.jobs` (`mux_jobs_total`) | counter | `{job}` | `outcome` | Finished background jobs: `completed`, `cancelled`, `failed`. |
| `mux.job.stage.duration` (`mux_job_stage_duration_seconds`) | histogram | s | `stage` | `queued` (waiting for a concurrency slot) and `run` (one agent turn). |
| `mux.job.stage.events` (`mux_job_stage_events_total`) | counter | `{event}` | `stage`, `outcome` | Stage completions (`success`, `error`, `cancelled`). |
| `mux.jobs.queued` (`mux_jobs_queued`) | gauge | `{job}` | none | Jobs waiting for a slot, across live job managers. |
| `mux.jobs.active` (`mux_jobs_active`) | gauge | `{job}` | none | Jobs holding a slot (running, awaiting approval, awaiting the write lease). |
| `mux.jobs.capacity` (`mux_jobs_capacity`) | gauge | `{job}` | none | Total concurrency slots across live job managers. |
| `mux.job.last_success.timestamp` (`mux_job_last_success_timestamp_seconds`) | gauge | s | none | Unix time of the last completed job. |
| `mux.write_lease.wait.duration` (`mux_write_lease_wait_duration_seconds`) | histogram | s | `outcome` | Waits for the shared workspace write lease: `acquired`, `timeout`, `cancelled`. |
| `mux.write_lease.waiters` (`mux_write_lease_waiters`) | up/down counter | `{job}` | none | Jobs waiting for the lease now. |
| `mux.usage.events` (`mux_usage_events_total`) | counter | `{event}` | `outcome` | Usage-history pipeline: `enqueued`, `written`, `dropped` (queue full or no session), `write_failed`. |
| `mux.usage.queue.depth` (`mux_usage_queue_depth`) | gauge | `{event}` | none | Usage events buffered and not yet persisted. |
| `mux.usage.write.duration` (`mux_usage_write_duration_seconds`) | histogram | s | `outcome` | SQLite batch write time. |
| `mux.session.operations` (`mux_session_operations_total`) | counter | `{operation}` | `operation`, `outcome` | Session store: `save`, `load`, `list`, `delete`; `failure` means not found. |
| `mux.session.operation.duration` (`mux_session_operation_duration_seconds`) | histogram | s | `operation`, `outcome` | Session store latency. |
| `mux.runs.active` (`mux_runs_active`) | gauge | `{run}` | none | Non-terminal runs tracked by the REST/WebSocket run registry. |
| `mux.runs.completed` (`mux_runs_completed_total`) | counter | `{run}` | `status` | Runs reaching a terminal state in the registry (`completed`, `failed`, `canceled`). |
| `mux.checkpoint.operations` (`mux_checkpoint_operations_total`) | counter | `{operation}` | `operation`, `outcome` | Git checkpoints (undo/redo): `capture`, `restore`. |
| `mux.checkpoint.duration` (`mux_checkpoint_duration_seconds`) | histogram | s | `operation`, `outcome` | Checkpoint latency. |

### Build and configuration

| Instrument (Prometheus name) | Type | Labels | Description |
|---|---|---|---|
| `mux.build.info` (`mux_build_info`) | gauge (always 1) | `service.version`, `runtime` | Product version and .NET runtime. |
| `mux.config.endpoints` (`mux_config_endpoints`) | gauge | none | Configured LLM endpoints (count only). |
| `mux.config.mcp_servers` (`mux_config_mcp_servers`) | gauge | none | Configured MCP servers (count only). |
| `mux.config.max_concurrency` (`mux_config_max_concurrency`) | gauge | none | Configured job concurrency limit. |

### From Watson and the runtime (not mux-defined)

- **Watson** (`Mux.Server`): `http_server_request_duration_seconds` (labels `http_request_method`, `http_route` as the
  route template, `http_response_status_code`), `http_server_active_requests`, request/response body-size histograms,
  and `watson_*` server, route, auth, and WebSocket metrics. See Watson's `TELEMETRY.md`.
- **Runtime and process** (Radiant): `dotnet_gc_*`, `dotnet_thread_pool_*`, `dotnet_jit_*`, `dotnet_process_*`,
  `process_*`.

## Spans catalog

Activity source: **`Mux`**. Every span sets its status explicitly (Ok, or Error with `error.type`); caught exceptions are
recorded as an `exception` event (type and message, no stack trace).

| Span name | Kind | Parent | Key attributes | Status / notes |
|---|---|---|---|---|
| `agent run` | internal | caller's span (Watson request, `stage:run`, `subagent run`), or a root | `mux.run.id`, `mux.session.id`, `mux.job.id`, `mux.endpoint.name`, `gen_ai.provider.name`, `gen_ai.request.model`, `call_kind`, `outcome`, `mux.agent.iterations`, `mux.agent.tool_calls`, `gen_ai.usage.input_tokens`/`output_tokens` | Ok only for `completed`. An `mux.agent.error` event (with `error.type`) per error event. |
| `llm chat_stream` / `llm chat` / `llm model_load` | client | `agent run` (or the caller) | `gen_ai.operation.name`, `gen_ai.provider.name`, `gen_ai.request.model`, `gen_ai.response.model`, `gen_ai.response.finish_reasons`, `gen_ai.usage.*`, `server.address`, `mux.llm.time_to_first_token_ms`, `http.response.status_code` on HTTP failures | Error with `error.type` = the failure code. |
| `stage:approval` | internal | `agent run` | `gen_ai.tool.name`, `gen_ai.tool.call.id`, `decision` | Includes time waiting for a human. |
| `tool <name>` | internal | `agent run` | `gen_ai.tool.name`, `gen_ai.tool.call.id`, `tool.kind` | Error when the tool returns a failure or throws. |
| `stage:write_lease_wait` | internal | `tool <name>` | none | Only when a mutating tool had to wait for the workspace write lease. |
| `stage:compaction` | internal | `agent run` | `strategy` | Error `compaction_failed` when nothing could be compacted. Its sidecar model call is an `llm chat` child. |
| `mcp connect` / `mcp tools/list` / `mcp tools/call` | client | caller, or `mcp refresh` | `mux.mcp.server`, `mux.mcp.transport`, `rpc.method`, `gen_ai.tool.name` | Error on exceptions, timeouts, and `isError` results. |
| `mcp refresh` | internal | root (background) | `mux.mcp.server_count` | The MCP runtime's periodic (re)connect. |
| `web_search search` | client | `tool web_search` | `mux.search.preferred_provider` | |
| `subagent run` | internal | `tool spawn_subagent` | `mux.subagent.name`, `mux.agent.iterations` | Wraps the child `agent run`. |
| `hook <event>` | internal | caller | `mux.hook.event`, `mux.hook.count`; `mux.hook.vetoed` event | Error when a hook fails to start or times out. |
| `job` | internal | the submitter's span (captured at submit, across the worker hand-off), or a root | `mux.job.id`, `mux.session.id`, `outcome` | Children: `stage:queued` (reconstructed from submit to slot grant) and one `stage:run` per prompt, which parents the `agent run`. |
| `session save` / `session load` | internal | caller | `mux.session.id`, `mux.session.found` | `list` and `delete` are metrics only (a listing would otherwise emit one span per file). |
| `checkpoint capture` / `checkpoint restore` | internal | caller | none | Children: `git <subcommand>` client spans (`process.executable.name`, `process.exit.code`). |
| `usage write_batch` | internal | root (background) | `mux.usage.batch_size` | One per SQLite batch. |

Watson adds one `{method} {route}` server span per HTTP request (activity source `Watson`) with `http.request.method`,
`http.route`, `http.response.status_code`, `url.scheme`, and client detail. Hosts also subscribe to `System.Net.Http`, so
on .NET 9+ each outbound HTTP request is an `HTTP {method}` client span under the `llm` or `mcp` span.

A typical `mux serve` chat trace: `POST /v1.0/api/chat` (Watson) > `agent run` > (`llm chat_stream`, `stage:approval`,
`tool read_file`, `llm chat_stream`) > `session save`.

## Context propagation

- **Inbound HTTP**: Watson adopts a W3C `traceparent` on every request (`PropagateContext = true`), so a caller's trace
  continues into mux.
- **Outbound HTTP**: LLM providers, MCP-over-HTTP servers, and web-search providers receive a `traceparent` header
  from the active `llm`/`mcp` span (BCL `HttpClient` propagation; verified by test).
- **Background hand-offs**: a job captures the trace context at submission and its worker parents the `job` span to it.
  Async-iterator boundaries (the agent loop and LLM stream) re-establish the run and LLM spans as current at every step,
  so nested work never attaches to the wrong parent. Background loops (usage writer, MCP refresh) start their own roots
  rather than inheriting a stale request context.

## Logs

When `logsEnabled` is true, mux diagnostic lines are exported as OpenTelemetry logs (logger categories `Mux.Server` and
`Mux.UsageTelemetry`): the REST server's request log (`[MuxServer] GET /path 200`, path only, never the query string)
and background-worker diagnostics (usage writer failures). Each record carries `trace_id` and `span_id`; request log
lines carry the request's trace id, so Grafana's trace-to-logs link (Tempo > Loki) and Loki's derived `trace_id`
field both work. The bundled collector forwards logs to Loki; query `{service_name="mux"}`.

## The observability stack

[`docker/compose.yaml`](docker/compose.yaml) brings up a pinned, provisioned stack with one command. mux runs on the
host and pushes OTLP into it.

| Service | Image | Host port (override env var) | Role |
|---|---|---|---|
| OpenTelemetry Collector | `otel/opentelemetry-collector-contrib:0.109.0` | 4317 (`MUX_OTLP_GRPC_PORT`), 4318 (`MUX_OTLP_HTTP_PORT`), 8889 (`MUX_COLLECTOR_METRICS_PORT`) | Receives OTLP; metrics to a Prometheus exporter, traces to Tempo, logs to Loki. |
| Prometheus | `prom/prometheus:v3.5.4` | 9090 (`MUX_PROMETHEUS_PORT`) | Scrapes the collector (and optionally a mux process's `/metrics` via `host.docker.internal:9464`); loads [`prometheus-alerts.yaml`](docker/prometheus-alerts.yaml). |
| Tempo | `grafana/tempo:2.6.1` | 3200 (`MUX_TEMPO_PORT`) | Trace storage and query (72 h retention). |
| Loki | `grafana/loki:3.2.1` | 3100 (`MUX_LOKI_PORT`) | Log storage and query. |
| Grafana | `grafana/grafana-oss:13.0.2` | 3000 (`MUX_GRAFANA_PORT`) | Datasources (`prometheus`, `tempo`, `loki` UIDs) and the Mux dashboards. |

All ports bind to `127.0.0.1`. Healthchecks use `interval: 5s`, `retries: 2`; Grafana waits for Prometheus, Tempo,
and Loki to be healthy; the collector waits for Tempo and Loki. If a default port collides with another stack, move
it with the variable in the table (and point mux at a moved OTLP port with `MUX_OTLP_ENDPOINT`).
[`docker/update.bat`](docker/update.bat) / [`update.sh`](docker/update.sh) pull and recreate the stack without
deleting its volumes.

**Production and shared deployments**: Grafana's `admin` / `admin` is a local default. Set `GF_SECURITY_ADMIN_PASSWORD`
(and optionally `GF_SECURITY_ADMIN_USER`) in the environment when you run compose; never write a real password into
`compose.yaml`. Sign-up is disabled. Prometheus, Tempo, Loki, the collector, and mux's `/metrics` have no
authentication: keep them on loopback or an internal network.

## Dashboards

Provisioned from [`assets/grafana/`](assets/grafana) into the **Mux** folder. Every dashboard has Service (`job`) and
Instance selectors and links to the others.

| Dashboard | UID | Answers |
|---|---|---|
| Mux / Overview | `mux-overview` | Is mux up and healthy? Processes reporting, version, collector up, active runs and jobs, run/LLM/HTTP error ratios, run latency, where run time goes (p95 by stage), integration errors, recent failing traces. Start here. |
| Mux / HTTP | `mux-http` | Which REST route is slow or failing? Rate by route and status, latency quantiles, top routes by p95, 5xx by route. |
| Mux / Agent Workflow | `mux-agent` | Why is a run slow or failing? Runs by outcome and call kind, per-stage p95 and time share, stage failures, errors by type, tool calls and p95 by tool, approvals, compactions, subagents, slowest runs in Tempo. |
| Mux / LLM Providers | `mux-llm` | Is a provider the cause? Requests and error ratio by provider, p95 by provider and operation, time to first token, tokens per second, retries, raw HTTP client timing per host. |
| Mux / Integrations | `mux-integrations` | Is a downstream the cause? Calls, error ratio, and p95 by service and operation; MCP, web search, git, and hooks; failing client spans. |
| Mux / Jobs & Background | `mux-jobs` | Are background jobs stuck? Jobs by outcome, queued vs run p95, slots, time since last success, write-lease waits, usage writer health, session store, run registry, checkpoints, runtime health, and mux logs from Loki. |

## Recommended alerts

Shipped in [`docker/prometheus-alerts.yaml`](docker/prometheus-alerts.yaml). Thresholds are starting points.

| Alert | Expression (abridged) | For |
|---|---|---|
| `MuxNotReporting` | `absent(mux_build_info) == 1` | 10m |
| `MuxAgentRunErrorRatioHigh` | runs with `outcome!="completed"` / all runs > 0.2 | 10m |
| `MuxLlmErrorRatioHigh` | per provider, `mux_llm_requests_total{outcome!="success"}` / all > 0.1 | 5m |
| `MuxLlmLatencyHigh` | `histogram_quantile(0.95, ... mux_llm_request_duration_seconds_bucket{gen_ai_operation_name="chat_stream"})` > 120 s | 15m |
| `MuxIntegrationErrorRatioHigh` | per service, failing / all `mux_integration_requests_total` > 0.25 | 10m |
| `MuxJobsQueuedTooLong` | p95 of `mux_job_stage_duration_seconds{stage="queued"}` > 300 s | 15m |
| `MuxJobFailures` | `increase(mux_jobs_total{outcome="failed"}[15m]) > 0` | immediate |
| `MuxWriteLeaseTimeouts` | `increase(mux_write_lease_wait_duration_seconds_count{outcome="timeout"}[15m]) > 0` | immediate |
| `MuxUsageEventsLost` | `increase(mux_usage_events_total{outcome=~"dropped|write_failed"}[15m]) > 0` | immediate |
| `MuxHttp5xxRatioHigh` | 5xx / all `http_server_request_duration_seconds_count` > 0.05 | 5m |

## Guarantees: cost, safety, cardinality

- **Best-effort**: every recording call and span helper swallows its own failures; telemetry can never fail a run.
  An export host that fails to start leaves mux running without export.
- **Near-zero cost when unobserved**: with no listener, `StartActivity` returns null and instruments are inert.
- **Bounded labels**: label values come from fixed sets (stages, outcomes, adapter types, built-in tool names, git
  subcommands, hook events); free-form codes are normalized to `[a-z0-9_./-]{1,48}` or `other`. MCP and skill tool
  names, model names, endpoint names, session/run/job ids, and URLs appear only on spans.
- **No secrets or payloads**: prompts, completions, tool arguments and results, API keys, and query strings are never
  recorded. Exception messages are recorded on spans (they can contain a file path or provider error text).
- **No in-process quantiles**: histograms carry raw buckets; Grafana derives p50/p95/p99.

## Known gaps

- MCP over **stdio** has no transport for a `traceparent`, so an MCP server's own spans do not join mux's trace (the
  `mcp tools/call` client span still times the call).
- Run frames **published** to a hub by another process over the WebSocket bridge (desktop or TUI mirroring) do not
  carry trace context across the socket.
- On .NET 8, `System.Net.Http` emits metrics but not activity-source spans; outbound HTTP timing is still covered by
  the `llm`/`mcp`/`web_search` client spans and `http_client_*` metrics.
- The desktop app's in-process logs are not exported (its embedded server lives in `Mux.Desktop.Core`, a library);
  its metrics and traces are.
- The External services card is on the web dashboard (`mux serve`, tray agent). The desktop app and the VS Code
  extension do not show it.
