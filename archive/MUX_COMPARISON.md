# mux — Client/Server + Tray Agent: Product Plan

_Status: active implementation plan. Check boxes as work lands. `[ ]` = todo, `[x]` = done, `[~]` = in progress._

This plan turns the "client/server" evolution into a shippable feature for mux: an **opt-in local REST
API server** (Watson 7) over mux's existing in-process services, plus a **cross-platform system-tray agent**
(Avalonia) that hosts it and offers **About / Launch Mux / Exit**. It follows the normative backend
guidelines in `C:\Code\agents\requirements` where they apply to a local single-user tool, and documents the
justified deviations (see [§8 Conformance](#8-conformance-with-agentsrequirements)).

Target release: **mux v0.9.0** (minor bump from 0.8.0).

## Status — shipped in v0.9.0

**Shipped & verified** (full solution builds on net8.0 + net10.0; hermetic self-tests 572/572 passing, 0 fail):
- `rest` settings block (`RestServerSettings`) + `MuxSettings.Rest` + docs.
- `Mux.Server` (Watson 7): `MuxServer` host, `HealthRoutes`, `EndpointRoutes`, `SessionRoutes`, single-local-key
  auth, CORS Preflight/PostRouting, a `/v1.0/ws` WebSocket (connect announce), 404 default route.
- `mux serve` CLI (`--host/--port/--api-key/--no-auth`; `MUX_REST_*` env overrides; auto-generated key).
- `Mux.Agent` cross-platform Avalonia tray with **About / Launch Mux / Exit** + single-instance lock.
- `docs/REST_API.md`, `docs/CONFIG.md` `rest` block, README highlight + version badge, `run-agent.sh/.bat`,
  CHANGELOG `v0.9.0`, version bumps (Core/Cli/Server/Agent = 0.9.0).
- Tests: `RestServerSettingsSuite` (6) + `MuxServerRouteSuite` (live boot: health, endpoints, 401 auth, 404,
  dashboard HTML, settings, chat routing).
- **Web dashboard** at `/dashboard`: Wilson-style chat (`POST /v1.0/api/chat`), form-based settings editor
  (`GET`/`PUT /v1.0/api/settings`, masked secrets), server info, light/dark theme with the `assets/` logos.
- **Tray startup scripts** (`scripts/{windows,linux,macos}/run-at-startup` + `remove-from-startup`) and a
  theme-aware full-size tray icon.
- **Dependencies** updated to latest (Voltaic 0.7.1, TUIKit 0.10.1, Avalonia 12.1.2, NUnit adapter 6.3.0).

**Deferred (documented follow-ups, unchecked below):** run-driving routes (`PromptRoutes`/`TaskRoutes`), the
full WebSocket per-run event bridge, `Server.UseOpenApi()` OpenAPI generation, a `mux agent` launcher verb, a
`docs/USAGE.md` section, and the WebSocket/lock test suites.

---

## 1. Why this, and the revised scorecard

Earlier analysis scored a from-scratch client/server rewrite as high-value / low-simplicity. Adopting the
**proven Watson 7 host + Avalonia tray pattern** used by Armor, Armada, and S3Drive turns it into a "wrap,
not rewrite": routes call mux's existing `JobManager` / `SessionStore` / `AgentLoop` directly, the WebSocket
carries mux's existing event stream, and `UseOpenApi()` yields the SDK path for free.

| Enhancement | Value | Simplicity | Total | Notes |
|---|---:|---:|---:|---|
| **REST/agent config surface** (settings.json `rest` block) — **prerequisite** | 4 | 9 | **13** | Enables everything below; small, additive. **This plan ships it.** |
| Frontier/cloud provider adapters ✅ *done (v0.8.x)* | 10 | 8 | 18 | Anthropic/Gemini/Azure/Vertex/Bedrock |
| **Client/server (`mux serve`, Watson 7 over existing services)** | 10 | 7 | **17** | **This plan ships it.** |
| System-tray agent (background mux, Avalonia) | 5 | 7 | 12 | **This plan ships it** (About/Launch Mux/Exit) |
| Desktop / Web / IDE clients | 8 | 4 | 12 | Rides on the REST+WS+OpenAPI substrate |
| OAuth (Claude Max / Copilot) | 7 | 4 | 11 | Loopback callback eased by the running server; flows/ToS unchanged |
| LSP — full | 8 | 4 | 12 | Orthogonal to this work |
| Subagents | 9 | 5 | 14 | Separate track |
| Undo/redo | 8 | 5 | 13 | Separate track |
| Plugin system (hooks + commands) | 7 | 6 | 13 | Separate track |
| Session sharing (local export) | 6 | 8 | 14 | Separate track; hosted tier rides on this server |
| Custom keybinds | 5 | 9 | 14 | Separate track |
| LSP — interim (Skills build/lint) | 6 | 9 | 15 | Separate track |

**Scope of this plan:** the three bolded rows — the config prerequisite, `mux serve` (Watson 7), and the
Avalonia tray agent.

---

## 2. Architecture

```
                                 ┌─────────────────────────────┐
   mux (TUI/CLI, in-process) ──> │            Mux.Core          │  JobManager, SessionStore,
                                 │  (unchanged domain services) │  AgentLoop, LlmClient, Tools
   mux serve  ───────────────┐   └──────────────┬──────────────┘
                             │                  │ in-process calls (no bus)
                             ▼                  ▼
                    ┌─────────────────────────────────────┐
                    │              Mux.Server              │  Watson 7 Webserver @ 127.0.0.1:port
                    │  MuxServer host + Routes/* registrars │  REST + WebSocket(/ws) + OpenAPI
                    └──────────────────┬───────────────────┘
                                       │ hosted by
                    ┌──────────────────▼───────────────────┐
                    │      Mux.Agent (Avalonia tray)        │  Tray: About / Launch Mux / Exit
                    │  windowless WinExe, OnExplicitShutdown │  single-instance lock, launches TUI
                    └───────────────────────────────────────┘
```

- **`mux serve`** (in `Mux.Cli`) starts `MuxServer` headless (no tray). Loopback-bound, token-guarded.
- **`Mux.Agent`** hosts `MuxServer` in-process **and** owns the tray icon. "Launch Mux" spawns the TUI exe.
- Both are **opt-in**. Default `mux` / `mux print` behavior is unchanged and listens on nothing.
- Concurrency: REST mutations funnel through the **existing single-writer `WriteLease`**, the same guard the
  TUI uses, so TUI + REST never corrupt a session. Session store writes stay atomic (last-writer-wins per id,
  already documented).

---

## 3. Configuration surface (prerequisite) — `settings.json` `rest` block

- [x] Add a `RestServerSettings` model: `enabled` (bool, default `false`), `hostname` (default `127.0.0.1`),
  `port` (int, default `8710`, clamped 1–65535), `ssl` (bool, default `false`), `apiKey` (string, auto-generated
  on first serve if blank; `${VAR}`-expandable), `corsAllowOrigin` (default `*`).
- [x] Add `Rest` to `MuxSettings` + defaults + clamping + `SettingsLoader` round-trip.
- [x] Environment overrides: `MUX_REST_ENABLED`, `MUX_REST_HOST`, `MUX_REST_PORT`, `MUX_REST_APIKEY`.
- [x] CLI flags on `serve`: `--host`, `--port`, `--no-auth` (dev only), `--api-key`.
- [x] Document in `docs/CONFIG.md`.

Chosen default port **8710** ("8710" ≈ mux keypad) to avoid common collisions.

---

## 4. `Mux.Server` (Watson 7 REST host)

New project `src/Mux.Server/Mux.Server.csproj` (`net8.0;net10.0`), `PackageReference Watson 7.1.1`,
`ProjectReference Mux.Core`. Follows the Watson 7 hosting rules from `BACKEND_ARCHITECTURE.md`.

- [x] `MuxServer` host class: thin ctor `(MuxServerSettings, <Mux.Core services>)`; `StartAsync`/`Stop`;
  `Webserver` bound to `127.0.0.1`; `DefaultRouteAsync` → 404.
- [x] `ConfigureServer()`: `AuthenticateRequest` hook (single local API-key: `Authorization: Bearer` /
  `X-Api-Key`), `UseOpenApi()`, required `Preflight` + `PostRouting` (CORS + request logging).
- [x] Route registrars (per-feature classes with `Register(Webserver)`), all under `/v1.0/api`:
  - [x] `HealthRoutes` — `GET /v1.0/api/health` (anonymous): version, uptime, pid.
  - [x] `EndpointRoutes` — `GET /endpoints`, `GET /endpoints/{name}` (list/inspect configured endpoints).
  - [x] `SessionRoutes` — `GET /sessions`, `GET /sessions/{id}`, `DELETE /sessions/{id}`.
  - [ ] `PromptRoutes` — `POST /sessions` (start a run), `POST /sessions/{id}/messages` (append a turn);
    body is a typed DTO; drives a headless `AgentLoop` run under the write lease.
  - [ ] `TaskRoutes` — `GET /sessions/{id}/tasks` (task plan snapshot).
- [x] WebSocket `/v1.0/ws` — streams the live `AgentEvent` sequence (reusing the JSONL event schema as JSON
  frames) so clients see `assistant_text` / `tool_call_*` / `task_plan_updated` / `run_completed` in real time.
- [x] Typed request/response DTOs in `Mux.Server/Models` (no `JsonElement` for fixed contracts).
- [x] Secret redaction on any surfaced config (reuse mux's redaction).
- [x] `MuxServerSettings` from `MuxSettings.Rest`; `127.0.0.1` pinned (loopback latency rule).

---

## 5. `Mux.Agent` (Avalonia system-tray agent)

New project `src/Mux.Agent/Mux.Agent.csproj` (`WinExe`, `EnableAvaloniaXamlCompilation=false`, `net8.0;net10.0`),
Avalonia 11.3.20 (`Avalonia`, `Avalonia.Desktop`, `Avalonia.Themes.Fluent`), `ProjectReference Mux.Server` +
`Mux.Core`. Adapts the Armor/S3Drive code-only pattern. **Cross-platform** (Windows/macOS/Linux).

- [x] `Program.cs` — resolve config dir, logging, **single-instance lock** (`agent.lock`, `FileShare.None`),
  `StartWithClassicDesktopLifetime(args, ShutdownMode.OnExplicitShutdown)`.
- [x] `App.cs` — `FluentTheme`, build `TrayIcon` + `NativeMenu`, own an `AgentHost`.
- [x] **Tray menu (required): About, Launch Mux, Exit.**
  - [x] **About** — opens `AboutWindow` (name, version, REST URL, license).
  - [x] **Launch Mux** — `Process.Start` the `mux` TUI exe (resolved beside the agent, then dev fallback),
    `UseShellExecute = true`.
  - [x] **Exit** — stops `MuxServer`, releases the lock, `desktop.Shutdown()`.
- [x] `AgentHost` — starts/stops `MuxServer`; exposes status; tray tooltip shows `mux — http://127.0.0.1:port`.
- [x] Embedded tray icon (`Assets/logo.ico` / `.png`) loaded via manifest stream.
- [x] `AgentInstanceLock` — cross-process lock (adapted from Armor).

---

## 6. CLI, scripts, docs

- [x] **CLI**: `mux serve [--host H] [--port N] [--api-key K] [--no-auth]` in `Mux.Cli` (new `ServeCommand`),
  registered in `Program.cs` dispatch and usage text; blocks until Ctrl+C/SIGTERM, clean shutdown.
- [ ] _(deferred)_ **CLI**: `mux agent` (optional) launches the tray agent exe (parity with Armor's launcher), or document
  running `Mux.Agent` directly.
- [x] **Scripts**: `run-agent.bat` / `run-agent.sh` (start tray), and update `install-tool.*` to build the new
  projects.
- [x] **Docs — new `docs/REST_API.md`**: endpoints, auth, WebSocket event contract, OpenAPI location, examples
  (curl + a minimal client), security posture (loopback + token, opt-in).
- [ ] _(deferred)_ **Docs — `docs/USAGE.md`**: a "Server & tray agent" section.
- [x] **Docs — `docs/CONFIG.md`**: the `rest` block (done in §3).
- [x] **README.md**: highlight bullet for the server/tray; version badge → 0.9.0.

---

## 7. Testing

Mirror mux's existing Touchstone suites (`Test.Shared/Suites/*`), hermetic and offline.

- [x] `RestServerSettingsSuite` — defaults, clamping, env overrides, JSON round-trip.
- [x] `MuxServerRouteSuite` — boot `MuxServer` on an ephemeral loopback port; assert `GET /health` 200 +
  payload; `GET /endpoints` shape; 401 without/with-bad token; 404 default route; CORS preflight 200.
- [ ] _(deferred)_ `MuxServerWebSocketSuite` — connect to `/v1.0/ws`, drive a fake run, assert event frames arrive in order.
- [ ] _(deferred)_ `AgentInstanceLockSuite` — second acquire fails while first is held; releases on dispose.
- [x] All suites green across `Test.Automated selftest`, `Test.Xunit`, `Test.Nunit` on net8.0 + net10.0.

---

## 8. Conformance with `agents/requirements`

`BACKEND_ARCHITECTURE.md` is normative for **multi-tenant backend services**. mux is a **local, single-user
CLI**, so the plan conforms to the applicable rules and documents justified deviations:

**Conformed:**
- [x] Watson 7 as the only HTTP stack; `WebserverSettings` + `Webserver`.
- [x] Per-feature route registrar classes calling `server.Routes.*.Add(...)`; no monolithic handler.
- [x] Required `Preflight` + `PostRouting` hooks (CORS + end-of-request logging).
- [ ] _(deferred)_ `Server.UseOpenApi()` exposed.
- [x] Typed request/response DTOs; no `JsonElement` for fixed contracts.
- [x] Thin `Program.cs`; orchestration in an instance host (`MuxServer`).
- [x] `127.0.0.1` pinned for loopback (both bind and any loopback client).
- [x] Versioned `/v1.0/api/...` paths; explicit status codes; `ctx.Token` threaded to services.
- [x] Strict C# code style, `ConfigureAwait(false)`, XML docs, region layout.
- [x] Settings management + env-override rules; secrets never committed.

**Justified deviations (documented in REST_API.md):**
- [x] **No multi-tenancy / `RequestContext` tenant fields** — mux is single-user local; auth is one local API
  key, not tenant/user identity. (Spec permits justified exceptions.)
- [x] **No database layer / 4-provider drivers / `RequestHistory` tables** — mux's state is its existing
  file-based `SessionStore`; introducing SQLite+MySQL+Postgres+SqlServer for a local CLI is inappropriate
  gold-plating. Request logging goes to mux's logger, not a DB table.
- [x] **No dashboard** — the tray agent + OpenAPI are the operator surfaces; a web dashboard is a future track.

---

## 9. Versioning & release

- [x] `CHANGELOG.md`: new `## v0.9.0` section (Added: REST server, tray agent, `mux serve`, `rest` settings,
  `REST_API.md`; Changed: version; Tests).
- [x] `README.md` version badge `0.8.0 → 0.9.0` + highlight bullet.
- [x] `src/Mux.Core/Mux.Core.csproj`, `src/Mux.Cli/Mux.Cli.csproj`, `src/Mux.Search/Mux.Search.csproj`,
  and new `Mux.Server` / `Mux.Agent` csproj `<Version>` → `0.9.0`.

---

## 10. Sequencing (recommended build order)

1. [x] Config surface (`RestServerSettings`) + tests — the prerequisite.
2. [x] `Mux.Server` host + Health/Endpoint/Session routes + tests.
3. [x] WebSocket event bridge + tests.
4. [x] `mux serve` CLI command + scripts.
5. [x] `Mux.Agent` tray (About/Launch Mux/Exit) + single-instance lock.
6. [x] `REST_API.md`, README/USAGE/CONFIG updates.
7. [x] Version bump across README/CHANGELOG/csproj; full test pass.

---

## 11. Risks & open decisions

- **Avalonia dependency weight** — the tray adds Avalonia (~large). It lives only in `Mux.Agent`; the core CLI
  and `mux serve` do **not** depend on Avalonia. Accepted.
- **Opt-in posture** — the server must never auto-listen from a plain `mux` invocation; only `mux serve` or the
  tray agent start it. Preserves mux's hermetic default.
- **Watson 7 API drift** — pin `Watson 7.1.1` (the version Armada ships) and follow `BACKEND_ARCHITECTURE.md`'s
  `server.Routes.*.Add(...)` surface.
- **TUI ↔ REST session concurrency** — mitigated by the shared write lease; document last-writer-wins per
  session id and recommend distinct ids or `--fork-session`.
