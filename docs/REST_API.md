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

Unless `--no-auth` is set, every route except `/v1.0/api/health` requires the API key, sent as either:

```
Authorization: Bearer <key>
X-Api-Key: <key>
```

A missing or wrong key returns `401`.

## Endpoints

All paths are versioned under `/v1.0/api`.

| Method | Path | Auth | Description |
|---|---|---|---|
| GET | `/v1.0/api/health` | none | Status, product version, pid, uptime. |
| GET | `/v1.0/api/endpoints` | key | Configured endpoints (name, adapter type, base URL, model, default). **No secrets.** |
| GET | `/v1.0/api/sessions` | key | Persisted sessions (id, title, endpoint, model, timestamps). |
| POST | `/v1.0/api/chat` | key | Plain (tool-free) chat completion against a configured endpoint. Body: `{ "endpoint": "<name>", "messages": [{ "role": "user", "content": "…" }] }` → `{ "role": "assistant", "content": "…", "endpoint", "model" }`. |
| GET | `/v1.0/api/settings` | key | Editable settings subset, **secrets masked** (`rest.apiKeySet` instead of the key). |
| PUT | `/v1.0/api/settings` | key | Update settings (validated/clamped, written to `settings.json`). The REST API key changes only when a non-blank `rest.apiKey` is supplied. |
| GET | `/dashboard` | none¹ | The web dashboard (HTML). |
| GET | `/v1.0/ws` | (WebSocket) | Live event stream; first frame is `server.connected`. |

¹ The dashboard page itself is served anonymously over loopback with the API key injected into the page, so
its own API calls are authenticated. Do not expose a non-loopback `hostname` without a strong `apiKey`.

## Web dashboard

`GET /dashboard` serves a self-contained single-page dashboard (no external assets — CSS/JS/logos are
inlined). It provides:

- **Chat** — a Wilson-style chat over any configured endpoint (endpoint picker, message bubbles, markdown +
  code blocks, new-chat). Backed by `POST /v1.0/api/chat` (non-streaming; a streaming SSE variant is a
  planned follow-up).
- **Settings** — a form-based editor over `settings.json` (agent, context, features, REST server) with masked
  secrets and per-group "restart required" hints. Backed by `GET`/`PUT /v1.0/api/settings`.
- **Server Info** — health/version/uptime and the configured endpoints.
- Light/dark theme (persisted), using the mux logos from `assets/`.

Open it at `http://127.0.0.1:<port>/dashboard` after `mux serve` (or launch the tray agent and open the URL
it hosts).

### Examples

```bash
# Health (no auth)
curl http://127.0.0.1:8710/v1.0/api/health

# Endpoints (authenticated)
curl -H "X-Api-Key: <key>" http://127.0.0.1:8710/v1.0/api/endpoints

# Sessions
curl -H "Authorization: Bearer <key>" http://127.0.0.1:8710/v1.0/api/sessions
```

Health response:

```json
{ "Status": "healthy", "Product": "mux", "Version": "0.9.0", "Pid": 12345,
  "StartedUtc": "2026-09-07T17:00:00Z", "Uptime": "0.00:01:23", "TimestampUtc": "2026-09-07T17:01:23Z" }
```

## WebSocket

Connect to `ws://127.0.0.1:<port>/v1.0/ws`. The server sends a first text frame:

```json
{ "eventType": "server.connected", "product": "mux", "version": "0.9.0" }
```

The event envelope reuses the `eventType` shape of the `mux print --output-format jsonl` contract. A full
per-run event bridge (`assistant_text` / `tool_call_*` / `task_plan_updated` / `run_completed`) is a planned
follow-up.

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
surface). OpenAPI document generation (`Server.UseOpenApi`) and run-driving routes are documented follow-ups.

## Planned (not yet implemented)

- Per-id session read/delete and run-driving `POST /sessions` / `POST /sessions/{id}/messages`.
- Full WebSocket per-run event bridge.
- OpenAPI 3.1 document + generated SDK.
