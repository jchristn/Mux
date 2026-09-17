# mux REST API

_Introduced in mux v0.9.0. **Opt-in and local by default.**_

mux can expose an optional local HTTP + WebSocket API over its existing in-process services, so other
processes and UIs can drive it without shelling out per run. It is built on **Watson 7** and follows the
Watson 7 hosting conventions in `agents/requirements/BACKEND_ARCHITECTURE.md`, with the single-user
deviations noted under [Conformance](#conformance).

> **The server never starts on its own.** A plain `mux` or `mux print` invocation listens on nothing. The
> API runs only when you start `mux serve` or launch the tray agent.

## Starting the server

```bash
mux serve                       # loopback, auto-generated API key (printed on start)
mux serve --port 9000           # custom port
mux serve --api-key mykey       # explicit key
mux serve --no-auth             # no authentication (loopback dev only)
```

`mux serve` binds to `127.0.0.1` by default, prints the base URL and the effective API key, and blocks until
`Ctrl+C`.

### Tray agent

The cross-platform **tray agent** (`Mux.Agent`) hosts the same server in the background and adds a
system-tray icon with three actions: **About**, **Launch Mux** (opens the interactive TUI), and **Exit**.
Launching the agent is itself the opt-in; only one agent runs at a time (single-instance lock).

## Configuration

The `rest` block in `settings.json`:

```json
{
  "rest": {
    "enabled": false,
    "hostname": "127.0.0.1",
    "port": 8710,
    "ssl": false,
    "apiKey": null,
    "corsAllowOrigin": "*"
  }
}
```

| Field | Default | Notes |
|---|---|---|
| `enabled` | `false` | Whether the **tray agent** auto-starts the server. `mux serve` starts it regardless. |
| `hostname` | `127.0.0.1` | Bind host. Loopback IPv4 is pinned to avoid Windows `localhost`/IPv6 latency. |
| `port` | `8710` | Bind port (clamped 1–65535). |
| `ssl` | `false` | Bind with SSL. |
| `apiKey` | `null` | Local key; auto-generated and persisted on first `serve` when blank. Literal or `${VAR}`. |
| `corsAllowOrigin` | `*` | `Access-Control-Allow-Origin` value; tighten for non-local callers. |

Environment overrides: `MUX_REST_HOST`, `MUX_REST_PORT`, `MUX_REST_APIKEY`. CLI flags (`--host`, `--port`,
`--api-key`, `--no-auth`) win over both.

## Authentication

Unless `--no-auth` is set, every route except `/v1.0/api/health`, `/openapi.json`, and `/swagger` requires
the API key, sent as a **bearer token**:

```
Authorization: Bearer <key>
```

A missing or wrong key returns `401`. (Earlier builds also accepted an `X-Api-Key` header; the server now
standardizes on the bearer token only.)

## API documentation (OpenAPI / Swagger)

The server publishes a complete, machine-readable **OpenAPI 3.0** description of every route and serves an
interactive **Swagger UI** to browse and try it:

| Path | Description |
|---|---|
| `GET /openapi.json` | The OpenAPI 3.0.3 document: info, tag groups, the bearer security scheme, every operation (summary, description, parameters, request body, responses), and reusable component schemas with example values. |
| `GET /swagger` | Swagger UI rendered against `/openapi.json`. |

Both are **unauthenticated** even when a key is configured — the usual expectation for API docs — so a client
can discover the surface before it has a key. Every documented operation still advertises the bearer
requirement, so a client generated from the document sends `Authorization: Bearer <key>` automatically.

## Endpoints

All paths are versioned under `/v1.0/api`.

| Method | Path | Auth | Description |
|---|---|---|---|
| GET | `/openapi.json` | none | OpenAPI 3.0 document describing the whole API (see [API documentation](#api-documentation-openapi--swagger)). |
| GET | `/swagger` | none | Interactive Swagger UI. |
| GET | `/v1.0/api/health` | none | Status, product version, **`contractVersion`** (the REST/SSE API contract a client negotiates against), pid, uptime. |
| GET | `/v1.0/api/endpoints` | key | Configured endpoints (name, adapter type, base URL, model, default). **No secrets.** |
| GET | `/v1.0/api/endpoints/detail` | key | Full endpoint fields for editing, **secrets masked** (`apiKeySet` flag; header/key values blanked). |
| PUT | `/v1.0/api/endpoints` | key | Replace the endpoint collection (`{ "items": [ … ] }`). A blank secret preserves the stored one. |
| DELETE | `/v1.0/api/endpoints?name=<name>` | key | Delete an endpoint. |
| GET/PUT/DELETE | `/v1.0/api/mcp-servers` | key | CRUD over MCP servers (auth secret masked/preserved). DELETE takes `?name=`. |
| GET/PUT | `/v1.0/api/prompts` | key | Prompt profiles (name, active, and all three prompt fields: `systemPrompt`, `toolsDisabledPrompt`, `compactionPrompt`). On PUT a blank field inherits the built-in default; a `null` (omitted) advanced field preserves the stored value. |
| GET | `/v1.0/api/prompts/catalog` | key | The operational prompt catalog: every model-facing prompt grouped by kind, with its coded default, current effective value, required placeholders, `Overridden` flag, and whether it is `Editable`. Returns `{ "items": [ … ] }`. |
| PUT | `/v1.0/api/prompts/catalog` | key | Set or clear one global-scoped override: `{ "key": "<catalog key>", "content": "<text>" }`. A blank/omitted `content` clears the override (restores the default). Rejects an unknown or profile-scoped key (`400`) and an override that drops a required placeholder (`400`). Returns the updated entry. |
| POST | `/v1.0/api/context/file` | key | Build a model-context block from a file: `{ "path", "content", "mode"?, "inlineThresholdBytes"?, "headLines"?, "summaryChunkLines"? }`. A file at or below the inline threshold returns whole; a larger file is **mapped** (a structural outline with line ranges), **summarized** (an iterative map-reduce summary the server runs), or **truncated**, per `mode` (default from settings). Returns `{ "Text", "Mode", "Inlined", "OutlineEntryCount", "FromCache" }`. Lets a thin client offload mapping/summarizing to the server. |
| GET/PUT | `/v1.0/api/subagents` | key | Subagent definitions. |
| GET/PUT | `/v1.0/api/hooks` | key | Plugin config: `{ "hooks": [ … ], "commands": [ … ] }`. |
| GET/PUT | `/v1.0/api/keybindings` | key | Command-id → chord overrides. |
| GET | `/v1.0/api/skills` | key | Skills list (name, description, enabled, valid, command count). |
| GET | `/v1.0/api/skills/detail?id=<id>` | key | One skill with its `SKILL.md` body. |
| PUT | `/v1.0/api/skills/enabled` | key | Toggle enablement: `{ "id": "<id>", "enabled": true }`. |
| DELETE | `/v1.0/api/skills?id=<id>` | key | Delete a skill folder. |
| GET | `/v1.0/api/sessions` | key | Persisted sessions (id, title, endpoint, model, timestamps). |
| GET | `/v1.0/api/sessions/export?id=<id>&format=md\|html` | key | Render a session → `{ "format", "filename", "content" }` for download. |
| DELETE | `/v1.0/api/sessions?id=<id>` | key | Delete a session. |
| POST | `/v1.0/api/chat` | key | Plain (tool-free) chat completion against a configured endpoint. Body: `{ "endpoint": "<name>", "messages": [{ "role": "user", "content": "…" }] }` → `{ "role": "assistant", "content": "…", "endpoint", "model", "stats": { "ttftMs", "streamingMs", "totalMs", "inputTokens", "outputTokens", "totalTokens" } }`. |
| POST | `/v1.0/api/chat/stream` | key | Agentic run over Server-Sent Events (`run`/`token`/`thinking`/`tool`/`approval`/`done`/`canceled`/`error`). The first event is `run` — `{ "RunId", "SessionId" }` — so the client can address the run (cancel it via `POST /v1.0/api/runs/{runId}/cancel`, or subscribe over the WebSocket). Body adds optional `id` (session), `workingDirectory` (the directory tools resolve paths against — must exist, else `400`; falls back to the session's recorded directory, then the server's), and, with `mux serve --allow-tools`, mutating tools raise an `approval` event answered by `POST /v1.0/api/chat/approve`. Persists the turn to the shared session store. |
| GET | `/v1.0/api/settings` | key | Editable settings subset, **secrets masked** (`rest.apiKeySet` instead of the key). |
| PUT | `/v1.0/api/settings` | key | Update settings (validated/clamped, written to `settings.json`). The REST API key changes only when a non-blank `rest.apiKey` is supplied. |
| GET | `/v1.0/api/usage/summary` | key | Window KPI summary. Query: `from`/`to` (epoch ms) or `range=hour\|day\|week\|month\|all`, plus `endpoint`, `model`, `callKind`. Returns `{ FromUnixMs, ToUnixMs, Metrics }`. |
| GET | `/v1.0/api/usage/timeseries` | key | Dense, evenly-spaced series for charting. Granularity is derived from the range — hour = 60×1-min, day = 96×15-min, week = 84×2-hour, month = 60×12-hour — and empty slices are zero-filled. Returns `{ Items: [ { BucketStartUnixMs, Metrics } ], Count }`. |
| GET | `/v1.0/api/usage/breakdown` | key | Totals grouped by `dimension=model\|endpoint\|provider\|command`, sorted by cost. Returns `{ Items: [ { Dimension, Value, Metrics } ], Count }`. |
| GET | `/v1.0/api/usage/events` | key | Paginated raw call history (newest first). Query: window filters + `success`, `page`, `pageSize`. Returns `{ Items: [ … per-call rows with CostUsd … ], TotalCount, PageNumber, PageSize }`. |
| DELETE | `/v1.0/api/usage/events?id=<id>` | key | Delete one recorded call by its row id. Returns `{ Deleted: <count> }`. |
| GET | `/v1.0/api/usage/filters` | key | Distinct endpoints/models for filter controls plus `{ Enabled }` (false when telemetry is off). |
| GET | `/v1.0/api/usage/pricing` | key | The model pricing table from `pricing.json` (`{ version, models }`). |
| PUT | `/v1.0/api/usage/pricing` | key | Replace the pricing table. Returns the saved table. |
| GET | `/v1.0/api/runs` | key | List active and recently-finished runs as summaries (most-recently-started first). Returns `{ Items: [ { RunId, SessionId, EndpointName, Model, Status, IsTerminal, StartedUtc, CompletedUtc } ], Count }`. Terminal runs are retained ~5 minutes for inspection, then evicted. |
| GET | `/v1.0/api/runs/{runId}` | key | Inspect one run: status, counters, current tool, last error, and the task-plan checklist. `404` when the run is unknown or evicted. |
| POST | `/v1.0/api/runs/{runId}/cancel` | key | Cooperatively cancel a run (trips its cancellation token; publishes a terminal `run_completed` with status `canceled` to stream subscribers). `200` `{ Ok, RunId, Status }` on success; `404` when the run is unknown or already finished. |
| GET | `/dashboard` | none¹ | The web dashboard (HTML). |
| GET | `/v1.0/ws` | key² | Live per-run event stream — subscribe by run or session id and replay + tail the canonical event envelope. See [WebSocket](#websocket). |

¹ The dashboard page itself is served anonymously over loopback with the API key injected into the page, so
its own API calls are authenticated. Do not expose a non-loopback `hostname` without a strong `apiKey`.

² When an API key is configured, the WebSocket upgrade must carry it. Browsers cannot set headers on a
WebSocket, so pass it as a query parameter: `ws://127.0.0.1:<port>/v1.0/ws?apiKey=<key>` (native clients may
instead send `Authorization: Bearer <key>`). An unauthenticated upgrade receives a single `error` frame and
is closed.

## Prompt management

Every string the model reads is addressable in one of two places:

- **Prompt profiles** (`/v1.0/api/prompts`) hold the switchable persona prompts — `systemPrompt`,
  `toolsDisabledPrompt`, and `compactionPrompt`. Exactly one profile is active; a blank field inherits the
  built-in default. All three are editable over REST (earlier releases exposed only `systemPrompt`).
- **The operational prompt catalog** (`/v1.0/api/prompts/catalog`) is a single, code-defined inventory of
  every other model-facing prompt, grouped by *kind*: `compaction`, `task-planning`, `title-generation`,
  `tool-section`, `tool-description`, `tool-result`, and `diagnostics`. Each entry ships with a sensible coded
  default, so the catalog is fully populated out of the box. A user override for a global-scoped entry is
  stored in the `operational` map of `~/.mux/prompts.json`; an absent key resolves to the default.

Set an override with `PUT /v1.0/api/prompts/catalog` (`{ "key", "content" }`); clear it (restore the default)
by sending blank `content`. Overrides are validated: an override that drops a placeholder the prompt requires
(for example `{ToolName}` in `result.unknown_tool`, or `{OperatingSystem}`/`{Shell}`/`{ShellArgsHint}` in
`tool.run_process`) is rejected with `400`. Subagent personas are not in the catalog — they keep their own
home in `subagents.json` and are edited through `/v1.0/api/subagents`.

## Large-file context

Instead of truncating a large file (or, for the agent's own `read_file` tool, refusing it outright), mux turns
it into a navigable block. The behavior is a `context` settings group in `settings.json` (also editable on
every Settings page):

- `largeFileMode` — `map` (default), `summarize`, or `truncate`. **Map** emits a structural outline with line
  ranges plus the first lines, so the model can pull an exact range with `read_file(offset, limit)` — ground
  truth, instant, and free. **Summarize** runs an iterative map-reduce and emits a dense summary that still
  carries line-range pointers. **Truncate** keeps the strict head-slice / refusal for anyone who wants the cap.
- `inlineThresholdBytes` — files at or below this size inline whole (default 64 KiB).
- `summaryChunkLines` — lines per chunk when summarizing (default 400).
- `summaryCacheEnabled` / `summaryCacheRetentionDays` — summaries are cached on disk, content-addressed by
  hash (so an edit misses automatically), under `~/.mux/cache/file-summaries/`, and a periodic pass evicts
  entries older than the retention window (default 7 days).

The agent's `read_file` tool applies the same setting: a file over its inline cap returns a structural map
(default) rather than a `file_too_large` refusal, while an explicit `offset`/`limit` still pages the exact
range the map points at. Summarizing needs a model call, so the tool itself maps; a thin client asks the
server to summarize via `POST /v1.0/api/context/file`, which runs the summarizer and serves the cache.

## Web dashboard

`GET /dashboard` serves a self-contained single-page dashboard (no external assets — CSS/JS/logos are
inlined). It provides:

- **Chat** — chat over any configured endpoint (endpoint picker, message bubbles, markdown + code blocks,
  new-chat), with a per-message **(i)** hover showing time-to-first-token, streaming time, and token counts.
  Backed by `POST /v1.0/api/chat`.
- **Configuration** — full-width management tables with custom modal add/edit forms and icon row actions
  (no browser dialogs) for **Endpoints, MCP Servers, Prompts, Subagents, Hooks & custom commands, Keybindings,
  and Skills** (enable/disable/view/delete). Secrets are never shown; leave a secret blank to keep it. Backed
  by the CRUD routes above.
- **Sessions** — browse saved sessions, export any to HTML/Markdown (client-side download), or delete.
- **Settings** — a form-based editor over `settings.json` (agent, context, features, REST server) with masked
  secrets and per-group "restart required" hints. Backed by `GET`/`PUT /v1.0/api/settings`.
- **Usage** — token, cost, latency, TTFT, streaming-time, and throughput analytics over selectable time
  ranges (hour/day/week/month) with endpoint/model filters, a KPI strip, inline SVG charts, and a paginated
  per-call history table. Backed by the `GET /v1.0/api/usage/*` routes.
- **Pricing** — a form-based editor over `pricing.json` (per-model input/cached/output rates in USD per
  million tokens) used to derive usage cost. Backed by `GET`/`PUT /v1.0/api/usage/pricing`.
- **Server Info** — health/version/uptime and the configured endpoints.
- Light/dark theme (persisted), using the mux logos from `assets/`.

The usage routes read a shared local SQLite database (`~/.mux/usage.db`) written by every mux instance on
the machine, so the dashboard reflects CLI activity too. When telemetry is disabled the query routes return
empty results with `Enabled: false`. Every usage `Metrics` object carries: `Calls`, `Errors`, `ErrorRate`,
`InputTokens`, `CachedTokens`, `OutputTokens`, `TotalTokens`, `CostUsd`, `CacheHitRate`, `AvgTtftMs`,
`P50/P95/P99TtftMs`, `AvgTotalMs`, `P50/P95/P99TotalMs`, `AvgStreamMs`, and `AvgTokensPerSec`.

Open it at `http://127.0.0.1:<port>/dashboard` after `mux serve` (or launch the tray agent and open the URL
it hosts).

### Postman collection

A documented Postman collection covering the full API surface lives at
`assets/postman/mux.postman_collection.json`, with a companion environment at
`assets/postman/mux.postman_environment.json`. Import both, set the `baseUrl` and `apiKey` variables
(leave `apiKey` blank when the server runs with `--no-auth`), and the collection-level bearer auth applies
the key to every request except Health. Requests are grouped into folders by resource, each with
collection-, folder-, and request-level documentation and example bodies.

### Examples

```bash
# Health (no auth)
curl http://127.0.0.1:8710/v1.0/api/health

# Endpoints (authenticated)
curl -H "Authorization: Bearer <key>" http://127.0.0.1:8710/v1.0/api/endpoints

# Sessions
curl -H "Authorization: Bearer <key>" http://127.0.0.1:8710/v1.0/api/sessions
```

Health response:

```json
{ "Status": "healthy", "Product": "mux", "Version": "0.9.0", "Pid": 12345,
  "StartedUtc": "2026-09-07T17:00:00Z", "Uptime": "0.00:01:23", "TimestampUtc": "2026-09-07T17:01:23Z" }
```

## WebSocket

Connect to `ws://127.0.0.1:<port>/v1.0/ws` (add `?apiKey=<key>` when a key is configured — see footnote ²).
The server sends a first text frame:

```json
{ "eventType": "server.connected", "product": "mux", "version": "0.12.0" }
```

**Subscribe to a run.** Send a subscribe frame naming a run id (from the chat stream's `run` event):

```json
{ "action": "subscribe", "runId": "8b2f…" }
```

The bridge replays the events the run has already emitted, then live-tails the rest, framing each as the
**canonical event envelope** — byte-for-byte the same `eventType` shape that `mux print --output-format jsonl`
emits (`run_started`, `assistant_text`, `assistant_thinking`, `tool_call_proposed`, `tool_call_completed`,
`context_compacted`, `task_plan_updated`, `error`, `run_completed`). A run-scoped subscription closes the
socket when the run reaches a terminal state. A subscribe frame for an unknown **run** id returns an `error`
frame with code `not_found`.

**Subscribe to a session** (what "mirror on by default" uses). Name a `sessionId` instead of a `runId`:

```json
{ "action": "subscribe", "sessionId": "9f1c…" }
```

The bridge attaches to any run already in flight for that session and to future runs as they start, streaming
each run's frames. It does **not** error when the session is currently idle — the socket stays open and the
next run for the session streams in the moment it begins. This lets a surface subscribe when a conversation is
opened and simply see whatever run happens there, from any surface.

**Publish a run** (how a surface that runs the engine in its own process — the desktop app, the terminal —
feeds the hub so others can mirror it). Send `publish` frames; the first materializes the run in the hub's
registry, and each carries one pre-serialized canonical envelope:

```json
{ "action": "publish", "runId": "8b2f…", "sessionId": "9f1c…", "endpointName": "openai-gpt4o",
  "model": "gpt-4o", "frame": "{\"eventType\":\"assistant_text\",\"text\":\"…\"}" }
```

**Signal a transcript change** (how an in-process surface tells others to reload an open conversation without
streaming a whole run through the hub). After persisting a turn to the shared store, send:

```json
{ "action": "notify-transcript", "sessionId": "9f1c…" }
```

The hub broadcasts a `transcript_changed` event (`{ "eventType": "transcript_changed", "sessionId": "9f1c…" }`)
to every session-scoped subscriber of that id, and refreshes the conversation list too. `transcript_changed`
is the message-level counterpart to the list-level `sessions_changed`: a surface viewing the conversation
reloads its transcript on it, exactly as it does on `run_completed`. The server fires it automatically on every
turn persist (a streamed run and the `PUT /v1.0/api/sessions` upsert) **and whenever any process changes a
session file on disk** — the server watches the session-store directory, so a turn written straight to the
shared store by an in-process terminal or desktop run (or by a second server) is rebroadcast to this server's
subscribers too. A viewer therefore reloads whether or not the writer drove a run through this hub, and even
when the writer is on a different hub that shares the same store.

**Answer an approval over the socket.** When the server runs with `--allow-tools`, a proposed mutating tool
raises an `approval_required`/`tool_call_proposed` event; answer it on the same socket:

```json
{ "action": "approve", "runId": "8b2f…", "toolCallId": "call_1", "decision": "y" }
```

`decision` is `y` (approve once), `always` (approve and remember), or `n` (deny). This resolves the same
pending approval the `POST /v1.0/api/chat/approve` route does.

**Mirroring runs started elsewhere.** The registry the bridge reads is a `Mux.Core` type a host process can
share. The desktop app records its in-process runs into the registry its embedded server exposes, so
subscribing by that run's session id mirrors a desktop run live. Consumers ship on every surface:
`mux mirror <sessionId>` (terminal), `/mirror <sessionId>` (dashboard), and *Mirror a session's live run*
(VS Code).

## Security posture

- **Loopback + token by default.** Do not bind a non-loopback `hostname` without also setting a strong
  `apiKey` and tightening `corsAllowOrigin`.
- **Secrets are never surfaced.** Endpoint responses project a whitelist of fields; API keys, headers, and
  credentials are excluded.
- **Opt-in.** Nothing listens unless you run `mux serve` or the tray agent.

## Conformance

Conforms to the Watson 7 hosting rules in `agents/requirements/BACKEND_ARCHITECTURE.md`: `WebserverSettings`
+ `Webserver`, per-feature route registrar classes, required `Preflight` + `PostRouting` (CORS + logging),
`127.0.0.1` pinned, versioned `/v1.0/api` paths, explicit status codes, typed DTOs, thin entry point with an
instance host (`MuxServer`), strict C# style, and env-overridable settings.

Justified single-user deviations: **no multi-tenancy / tenant `RequestContext`** (mux is single-user local;
auth is one local key), **no database layer / request-history tables** (state is mux's existing file-based
session store; request logging goes to the logger), and **no dashboard** (the tray agent is the operator
surface). The server publishes a full **OpenAPI 3.0** document and Swagger UI (Watson's `UseOpenApi`); further
run-driving routes are documented follow-ups.

## Planned (not yet implemented)

- Per-id session read/delete and run-driving `POST /sessions` / `POST /sessions/{id}/messages`.
- A task-state route beyond `GET /v1.0/api/runs/{runId}` (for example historical/persisted run state).
- Generated client SDKs from the OpenAPI document (the OpenAPI 3.0 document + Swagger UI now ship at
  `/openapi.json` and `/swagger`).
