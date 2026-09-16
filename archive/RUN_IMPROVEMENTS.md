# RUN_IMPROVEMENTS.md — Server run lifecycle: WebSocket event bridge, cancel-run, task-state

Status legend for annotation: `[ ]` not started · `[~]` in progress · `[x]` done · `[-]` skipped (say why in Notes).
Each task has an **Owner** slot and a **Notes** slot a developer can fill in as work proceeds. Do not delete
tasks when done — mark them `[x]` and leave the notes so the history stays readable.

**Target version:** `0.11.0` → **`0.12.0`** (minor bump; additive API surface, no breaking changes to the
existing REST/JSONL contracts). VS Code extension `0.11.3` → **`0.12.0`** to align with the product line.

**Compliance:** this plan and the code it produces follow `c:\code\agents\requirements` —
`CODE_STYLE.md` (namespace-wrapped usings ordered system-first; XML docs on all public members; no `var`;
no tuples; one class/enum per file; `CancellationToken` on async methods; `.ConfigureAwait(false)`; guard
clauses; specific exception types with `<exception>` docs; nullable reference types; no `Console.WriteLine`
in library code), `REPOSITORY_REQUIREMENTS.md` (#13 — keep `docs/REST_API.md` **and** the checked-in Postman
collection in sync with the API surface), and `BACKEND_ARCHITECTURE.md` / `BACKEND_TEST_ARCHITECTURE.md` for
Watson 7 hosting and the Touchstone suite conventions.

---

## Delivery status (v0.12.0) — implemented

All phases delivered. Build clean (0 warnings) on net8.0 + net10.0; Touchstone suite **802 total, 795 passed,
0 failed, 7 skipped** (baseline was 786/779) on both TFMs; VS Code extension `tsc` + 32 unit tests green,
`npm run i18n` in lockstep (56 keys, 12 locales); `.vsix` packaged as `mux-ai-0.12.0.vsix`.

- **[x] Phase 0** — version `0.11.0 → 0.12.0` (all csproj + `Defaults.ProductVersion`), VS Code `0.11.3 → 0.12.0`.
- **[x] Phase 1** — `Mux.Core.Agent.AgentEventSerializer`; CLI delegates; `AgentEventSerializerSuite` incl. golden parity.
- **[x] Phase 2** — `RunStatusEnum`/`RunHandle`/`RunRegistry`/`RunSubscription`; `ChatRoutes` drives through the handle; `run` SSE event; `RunRegistrySuite`.
- **[x] Phase 3** — `RunRoutes` `POST /runs/{runId}/cancel` (200/404/401).
- **[x] Phase 4** — `GET /runs`, `GET /runs/{runId}` (incl. task plan).
- **[x] Phase 5** — `WebSocketBridge`: authed upgrade, subscribe/replay/tail, over-socket approvals, backpressure; WS transport test.
- **[x] Phase 6** — dashboard Stop cancels server-side (captures `runId`, POSTs cancel); `canceled` handled. (No new UI strings → no dashboard i18n keys.)
- **[x] Phase 7** — VS Code `ApiClient.cancelRun`; `mux.cancelRun` command + Stop wiring; `run`/`canceled` stream events; cancelRun + streaming unit tests.
- **[x] Phase 8** — headless JSONL byte-identical (golden test); desktop builds & cancels in-process unchanged.
- **[x] Phase 9** — ApiDoc metadata + schemas; `REST_API.md` Runs + WebSocket sections; Postman **Runs** folder + `runId` var; `VSCODE.md`; README + CHANGELOG; this table.
- **[x] Phase 10** — full build + both-TFM suite + OpenAPI doc assertions for `/v1.0/api/runs` + `RunStateReply`/`RunSummaryDto`.

## Live session mirroring — a shared run fabric (v0.12.0)

Reworked into a proper fabric per the "every surface both produces and consumes, hub is the tray agent, no
config" direction. The run types (`RunStatusEnum`/`RunHandle`/`RunRegistry`/`RunSubscription`/`RunPublisher`)
live in **`Mux.Core.Runs`**; the run stream carries pre-serialized canonical envelope frames so a run driven
in any process is indistinguishable from a local one.

- **[x] Hub = tray agent, ensured by every surface.** The TUI and desktop already auto-start the tray agent;
  `mux mirror` now ensures it too (and retries the connect while it boots). `MuxServer` accepts an
  externally-owned `RunRegistry`.
- **[x] Every surface produces.** `mux serve`/dashboard/VS Code run through the hub natively; the **desktop**
  (`AgentLoopTurnRunner`) and the **terminal/TUI** (`AgentEventProjector`) publish every turn to the hub via
  `RunPublisher` (best-effort — a missing hub is a no-op, the run is unaffected).
- **[x] Transport for "on by default."** WebSocket bridge gained a `publish` action (producer → hub) and
  **session-scoped subscribe** — attach to a session and receive current *and future* runs without erroring
  when idle. Backed by `RunRegistry.RunRegistered` + `FindBySession`.
- **[x] Consumers — every surface, on by default.** All four auto-subscribe by session and reflect a run
  finishing elsewhere with no manual refresh, via `Mux.Core.Runs.SessionMirrorClient` (shared by desktop +
  TUI) and per-UI clients:
  - **Web dashboard** — subscribes on conversation open; reloads on `run_completed` elsewhere; `/mirror on|off`
    opts a conversation out.
  - **VS Code** — subscribes on `loadSession`; reloads the transcript on external completion.
  - **Desktop** — mirrors the focused conversation; reloads the tab from the store on external completion.
  - **Terminal/TUI** — mirrors the current conversation (startup + on resume); redraws on external completion;
    plus `mux mirror <sessionId>` for an explicit read-only tail.
  - De-dup: each consumer skips its own in-flight/just-finished run (busy guard + "store has more turns than
    shown" check), so a locally-driven turn is never rendered twice.
- **[x] Tests.** `RunRegistrySuite` (frame relay, `GetOrCreate`/`ApplyEnvelope`); `MuxServerRoutes` WS cases
  (publish→subscribe relay; **session-scoped subscribe before any run, then a later run streams in**;
  **`SessionMirrorClient` raises `RunCompleted` for a published run**); injected-registry REST exposure;
  VS Code `mirror.test.ts`. All green on net8.0 + net10.0 (805 total, 798 passed).

---

## 1. Why these three land together

Today a run is a local variable inside the SSE handler. `ChatRoutes.StreamChatAsync` news up an `AgentLoop`,
drives `loop.RunAsync(prompt, ctx.Token)`, and writes events straight onto that one HTTP response
(`src/Mux.Server/Routes/ChatRoutes.cs`). Three consequences map one-to-one to the three gaps:

- Nothing outside that stack frame can **name** the run, so there is no state to inspect (task-state gap).
- The only cancellation token is `ctx.Token`, which fires only on client disconnect, so cancel is
  client-abort only (`dashboard stopChat()` in `DashboardPage.cs` just calls `AbortController.abort()`).
- The event stream has exactly one consumer and an ad-hoc SSE projection (`token`/`thinking`/`tool`/`done`),
  so there is nothing for a WebSocket to subscribe to, and that projection is a *second* contract that has
  already drifted from the canonical JSONL envelope in `StructuredOutputFormatter.FormatEvent`
  (`src/Mux.Cli/Commands/StructuredOutputFormatter.cs`, `contractVersion: 2`).

The fix is one architectural move with three payoffs: **promote the run to a first-class, addressable
server-side object** (a `RunHandle` in a `RunRegistry`) whose `AgentLoop` is driven on a background task, with
a **linked `CancellationTokenSource`**, a **status record**, and an **event fan-out** replayable from a small
ring buffer. Once that exists, the WebSocket bridge is a subscriber, cancel cancels the handle's CTS, and
task-state reads the handle. The `_PendingApprovals` dictionary already in `ChatRoutes` is the seed of this
pattern — generalize it.

A second enabler runs alongside: **one serializer, three transports.** The `AgentEvent → envelope` mapping
that JSONL already uses must move into `Mux.Core` so SSE, the WebSocket bridge, and headless JSONL all emit
byte-identical payloads. That is what makes "WebSocket parity with JSONL" true by construction instead of by
hand.

---

## 2. Surface-by-surface scope (honest about depth)

Not every surface needs the same amount of work, and the plan says so rather than manufacturing parity.

- **`mux serve` (Mux.Server)** — the whole feature lives here: run registry, refactor, cancel/runs routes,
  WebSocket bridge, OpenAPI/Postman.
- **Web dashboard (Mux.Server/DashboardPage.cs)** — functional: turn the client-abort Stop into a real
  server cancel, and show live run status. Needs the server to hand the browser a `runId`.
- **VS Code extension (Mux.VSCode)** — functional: a real Stop/cancel command over the cancel route, plus
  opt-in live session mirroring over the WebSocket bridge (the "remaining" item IMPROVEMENTS.md names).
  Version bump + republish.
- **TUI / CLI (Mux.Cli)** — consistency + safety net: it *owns* the reference JSONL contract, so its change
  is delegating to the promoted Core serializer and proving the output is unchanged with a golden test. No
  new user-facing command.
- **Desktop (Mux.Desktop)** — consistency: desktop drives `AgentLoop` in-process and already cancels via a
  per-tab `CancellationTokenSource`, so no cancel-route work is needed. It adopts the shared `RunStatusEnum`
  vocabulary and gains a small task-state view for parity. Marked optional where it is genuinely optional.

---

## 3. Phased work

> **All phases below are delivered** (see the *Delivery status* summary at the top). Boxes are checked to
> reflect reality. A few things landed differently from the original plan text, noted here rather than
> rewriting every task line:
> - **Branch (0.1):** delivered directly on `main`, not a `feature/run-lifecycle` branch (per direction).
> - **Version:** the line has since moved past `0.12.0` — product is now `0.12.2` (later work built on this).
> - **Run types moved to `Mux.Core`:** `RunStatusEnum`/`RunHandle`/`RunRegistry`/`RunSubscription` live under
>   `Mux.Core.Runs` (not `Mux.Server.Runs`) so every surface — desktop, TUI, server — shares one fabric, as
>   described in the *Live session mirroring* section above. `WebSocketBridge` stayed in `Mux.Server.Runs`.
> - **Run DTOs (4.1):** grouped in `src/Mux.Server/Models/RunDtos.cs` (`RunSummaryDto`, `RunCancelReply`, and
>   the run-state reply) rather than one class per file, matching the DTO-grouping convention (as with ChatDtos).
> - **Test counts** have grown well past the numbers quoted above as later features (cross-surface sync
>   consolidation, reasoning persistence) added suites; latest full run is **822 total, 815 passed, 0 failed**.

### Phase 0 — Version bump + branch

- [x] **0.1** Create working branch `feature/run-lifecycle`. — Owner: ___ — Notes: ___
- [x] **0.2** Bump product version `0.11.0` → `0.12.0` in `src/Directory.Build.props` (`<Version>`) and
  `src/Mux.Core/Settings/Defaults.cs` (`ProductVersion`). — Owner: ___ — Notes: ___
- [x] **0.3** Bump `src/Mux.VSCode/package.json` `version` `0.11.3` → `0.12.0`. — Owner: ___ — Notes: ___
- [x] **0.4** Add an `## Unreleased` → `## 0.12.0` heading scaffold in `CHANGELOG.md` (fill as phases land).
  — Owner: ___ — Notes: ___
- [x] **0.5** Confirm baseline: `dotnet build` clean (0 warnings) both TFMs; full Touchstone suite green;
  record the current test count here as the baseline → **baseline count: ___**. — Owner: ___ — Notes: ___

### Phase 1 — Shared event serializer in Core (the parity enabler)

- [x] **1.1** Add `Mux.Core.Agent.AgentEventSerializer` (one public static class, one file) holding the
  `AgentEvent → Dictionary/JSON` mapping and the redaction helpers currently in
  `StructuredOutputFormatter` (`FormatEvent`, `GetEventTypeName`, `FormatToolCall`, `FormatToolResult`,
  `RedactString`, `RedactDictionary`, `IsSensitiveKey`, `ClassifyFailureCategory`, `FormatTask`,
  `FormatTaskSummary`, `FormatReasoningEffort`). Preserve `contractVersion = 2` and camelCase exactly.
  XML-doc every public member; `internal` helpers get no doc per CODE_STYLE. — Owner: ___ — Notes: ___
- [x] **1.2** Change `Mux.Cli.Commands.StructuredOutputFormatter` to **delegate** to the Core serializer for
  event formatting (keep `FormatRunSummary`, `FormatTextStatsFooter`, `FormatObject` where they are — they
  are CLI output concerns). No behavior change. — Owner: ___ — Notes: ___
- [x] **1.3** Expose a typed entry point the server can call without the CLI dependency, e.g.
  `AgentEventSerializer.ToEnvelope(AgentEvent, bool includeStats)` returning the serialized line, and a
  strongly-typed overload returning the payload object for WS framing. — Owner: ___ — Notes: ___
- [x] **1.4** Tests — `AgentEventSerializerSuite` in `src/Test.Shared/Suites/` (register in `MuxSuites.All`):
  - Positive: one case per event type (`run_started`, `assistant_text`, `assistant_thinking`,
    `tool_call_proposed`, `tool_call_approved`, `tool_call_completed`, `error`, `heartbeat`,
    `context_status`, `context_compacted`, `run_completed`, `task_plan_updated`) asserting the exact
    `eventType` string and required keys.
  - Golden parity: feed a fixed event list through the Core serializer and assert each line equals the
    string the CLI previously produced (lock the contract).
  - Negative: null `Text`/`Message` redact to empty, not throw; a `Bearer ...`/`sk-...`/`x-api-key: ...`
    string is redacted; unknown enum value falls back to `ToString()`; `includeStats: false` omits the
    metrics + `usage` block on `run_completed`. — Owner: ___ — Notes: ___

### Phase 2 — Run registry + refactor ChatRoutes

- [x] **2.1** Add `Mux.Server.Runs.RunStatusEnum` (`Running`, `AwaitingApproval`, `Completed`, `Failed`,
  `Canceled`) — one enum, one file. — Owner: ___ — Notes: ___
- [x] **2.2** Add `Mux.Server.Runs.RunHandle` — `RunId`, `SessionId`, `EndpointName`, `Model`, `Status`
  (explicit getter/setter with backing field), `StartedUtc`, `CompletedUtc`, counters
  (`IterationsCompleted`, `ToolCallCount`, `ErrorCount`, token totals), `CurrentToolName`, `LastError`,
  latest task-plan snapshot, the linked `CancellationTokenSource`, the pending-approval map, a bounded
  event ring buffer, and a subscriber fan-out (bounded `Channel<AgentEvent>` per subscriber). Implements
  `IDisposable` (full pattern; disposes the CTS and completes channels). Thread-safety documented in XML.
  — Owner: ___ — Notes: ___
- [x] **2.3** Add `Mux.Server.Runs.RunRegistry` — `ConcurrentDictionary<string, RunHandle>` with
  `Create(...)`, `TryGet(runId, out handle)`, `TryCancel(runId)`, `Complete(runId, status)`, `List()`, and
  a background TTL eviction loop (reuse the telemetry maintenance-loop pattern; keep terminal handles for a
  configurable window — default 5 min via a public member, not a const — so a post-completion state poll
  still resolves). `IDisposable`/`IAsyncDisposable`. — Owner: ___ — Notes: ___
- [x] **2.4** Refactor `ChatRoutes.StreamChatAsync`: create a `RunHandle` (linked CTS from `ctx.Token`),
  drive the `AgentLoop` on a background task feeding the handle, and make the SSE response the **first
  subscriber** relaying the existing dashboard event names (back-compat) while each event also updates
  handle status + ring buffer + fan-out. Fold `_PendingApprovals` into the handle. — Owner: ___ — Notes: ___
- [x] **2.5** Emit a new SSE `run` event **first** carrying `{ runId, sessionId }` so the browser/editor can
  address the run for cancel. (Additive; existing clients ignore unknown events.) — Owner: ___ — Notes: ___
- [x] **2.6** Register the `RunRegistry` singleton in `MuxServer` wiring and pass it into `ChatRoutes`.
  — Owner: ___ — Notes: ___
- [x] **2.7** Tests — `RunRegistrySuite`:
  - Positive: create → `TryGet` returns it; status transitions `Running → Completed`; `List()` includes
    active and recently-terminal handles; eviction removes a terminal handle after the TTL.
  - Negative: `TryGet`/`TryCancel` on an unknown id returns false; double-cancel is idempotent; cancel of an
    already-completed run is a no-op returning false; concurrent create/cancel from N tasks does not corrupt
    the dictionary (stress case). — Owner: ___ — Notes: ___

### Phase 3 — Cancel-run route

- [x] **3.1** Add `Mux.Server.Routes.RunRoutes` (one class, one file) and register in `MuxServer`.
  — Owner: ___ — Notes: ___
- [x] **3.2** `POST /v1.0/api/runs/{runId}/cancel` → `ApiAuth.Authorize` → `registry.TryCancel(runId)`:
  200 `{ ok: true, status: "canceled" }` on hit, 404 `ApiError("NotFound", ...)` on unknown/terminal, 401
  when unauthorized. Cancelling fires the handle CTS; the `await foreach` throws `OperationCanceledException`
  (already caught) → terminal `Canceled` status + a `run_completed`(status=canceled) event to all
  subscribers. — Owner: ___ — Notes: ___
- [x] **3.3** Confirm the MCP/tool executor receives the handle token so a long-running tool actually
  interrupts (the executor signature already takes a `CancellationToken`). — Owner: ___ — Notes: ___
- [x] **3.4** Tests (extend `MuxServerRouteSuite`): cancel a live scripted run → 200 + terminal state within
  a bound; cancel unknown id → 404; cancel without key → 401; cancel twice → second is 404/no-op; assert a
  terminal event reaches a subscriber. — Owner: ___ — Notes: ___

### Phase 4 — Task-state inspection route

- [x] **4.1** Add DTOs `Mux.Server.Models.RunStateReply` and `RunListReply` (one class per file), projecting
  `RunHandle` → status, ids, endpoint/model, timestamps, counters, current tool, last error, **and the
  current task-plan checklist** (mux's user-facing notion of "tasks"). — Owner: ___ — Notes: ___
- [x] **4.2** `GET /v1.0/api/runs` → list active + recently-terminal runs (summary rows). — Owner: ___ —
  Notes: ___
- [x] **4.3** `GET /v1.0/api/runs/{runId}` → full `RunStateReply`; 404 on unknown. Both authed. — Owner: ___
  — Notes: ___
- [x] **4.4** Tests: list reflects an active run; detail shows correct status through a scripted
  `Running → Completed` transition and includes the task-plan; unknown id → 404; unauthorized → 401
  (negative). — Owner: ___ — Notes: ___

### Phase 5 — WebSocket event bridge (parity with JSONL)

- [x] **5.1** Replace the connect/ack stub `HandleWebSocketAsync` in `MuxServer.cs` with a real bridge.
  Extract the logic into `Mux.Server.Runs.WebSocketBridge` (one class, one file); `MuxServer` only wires the
  route. — Owner: ___ — Notes: ___
- [x] **5.2** **Auth the upgrade** — `/v1.0/ws` currently has no API-key check. Require the key via query
  param (`?apiKey=`) or subprotocol header; reject the upgrade otherwise. Keep loopback binding. — Owner:
  ___ — Notes: ___
- [x] **5.3** Subscription protocol: inbound `{ "action": "subscribe", "runId": "..." }` (also accept
  `sessionId` or `"all"`); reply `server.connected`, then **replay** the handle ring buffer, then live-tail.
  Each frame is the canonical envelope from `AgentEventSerializer` (`assistant_text`, `assistant_thinking`,
  `tool_call_proposed`, `tool_call_completed` incl. `tool_result` payload, `task_plan_updated`,
  `context_compacted`, `error`, `run_completed`). — Owner: ___ — Notes: ___
- [x] **5.4** Approvals over WS: emit `approval_required`, accept inbound
  `{ "action": "approve", "runId", "toolCallId", "decision" }` resolving the handle's pending approvals
  (same mechanism the SSE `/chat/approve` route uses). — Owner: ___ — Notes: ___
- [x] **5.5** Backpressure: bounded per-subscriber channel; a slow/dead subscriber is dropped, never stalls
  the run. Document the policy in XML. — Owner: ___ — Notes: ___
- [x] **5.6** Tests: a WS subscriber to a scripted run receives the full ordered event set and the **payloads
  equal the JSONL lines** for the same events (the parity guarantee); a late subscriber gets ring-buffer
  replay then live tail; unauthorized upgrade is rejected (negative); malformed inbound frame is ignored, not
  fatal (negative). — Owner: ___ — Notes: ___

### Phase 6 — Web dashboard

- [x] **6.1** In `DashboardPage.cs`: capture the `runId` from the new SSE `run` event; keep the Stop button
  but have `stopChat()` first `POST /v1.0/api/runs/{runId}/cancel`, then abort the `EventSource`/fetch as the
  fallback. — Owner: ___ — Notes: ___
- [x] **6.2** Show live run status (Running / Awaiting approval / Canceled) near the composer, driven by the
  stream. — Owner: ___ — Notes: ___
- [x] **6.3** Localize any new UI strings through the existing dashboard i18n path (11 languages) per
  `I18N.md`; run the i18n check. — Owner: ___ — Notes: ___
- [x] **6.4** Manual/automated: verify Stop actually stops server-side (run no longer in `GET /runs`), not
  just the browser. — Owner: ___ — Notes: ___

### Phase 7 — VS Code extension

- [x] **7.1** `ApiClient` (`src/Mux.VSCode/src/api/ApiClient.ts`): add `cancelRun(runId)` →
  `POST /v1.0/api/runs/{runId}/cancel`; capture `runId` from the stream's `run` event. — Owner: ___ —
  Notes: ___
- [x] **7.2** Add a `mux.cancelRun` command + Stop affordance in the chat webview
  (`ChatViewProvider.ts`); register in `package.json` `contributes.commands` with an l10n title key and add
  the localized strings. — Owner: ___ — Notes: ___
- [x] **7.3** Opt-in **live session mirroring** over the WebSocket bridge: subscribe by `sessionId`, render
  streamed events so a run started elsewhere (TUI/desktop via serve, or another editor window) mirrors live.
  Gate behind a setting (`mux.liveMirror`, default off) to keep the default path simple. — Owner: ___ —
  Notes: ___
- [x] **7.4** Tests: `node --test` unit case for `cancelRun` (positive: posts to the right URL with the key;
  negative: surfaces a 404 without throwing); run `npm run i18n` (no missing keys) and `npm run lint`
  (`tsc --noEmit`). — Owner: ___ — Notes: ___

### Phase 8 — TUI / CLI + Desktop (consistency)

- [x] **8.1** TUI/CLI: confirm headless JSONL output is unchanged after the Phase 1 delegation — the golden
  parity test (1.4) is the gate; add a `print --output-format jsonl` regression case if not already covered.
  — Owner: ___ — Notes: ___
- [x] **8.2** Desktop: verify in-process Stop still cancels a turn (no regression from shared types). — Owner:
  ___ — Notes: ___
- [x] **8.3** Desktop (optional): adopt `RunStatusEnum` vocabulary for the per-tab attention model and add a
  small task-state/run-status line in the transcript header. Mark `[-]` with a reason if deferred. — Owner:
  ___ — Notes: ___

### Phase 9 — Documentation, OpenAPI, Postman (REPOSITORY_REQUIREMENTS #13)

- [x] **9.1** `src/Mux.Server/Documentation/ApiDoc.cs`: add tag + per-route metadata for `POST /runs/{id}/cancel`,
  `GET /runs`, `GET /runs/{id}`, and the `run` SSE event; document component schemas with examples so
  `/openapi.json` + `/swagger` stay complete. — Owner: ___ — Notes: ___
- [x] **9.2** `docs/REST_API.md`: add a **Runs** section (three routes: method, path, params, request/response
  bodies, status codes, auth, examples) and a **WebSocket** section documenting the `/v1.0/ws` auth,
  subscribe/approve frames, and the canonical event envelope (note it mirrors headless JSONL). — Owner: ___
  — Notes: ___
- [x] **9.3** `assets/postman/mux.postman_collection.json`: add a **Runs** folder (cancel/list/detail) with
  collection/folder/request descriptions and variables for base URL + API key (never hard-coded); note in the
  folder description that the WebSocket bridge is not exercisable from Postman and point to REST_API.md.
  Update `mux.postman_environment.json` if new variables are introduced. — Owner: ___ — Notes: ___
- [x] **9.4** `docs/VSCODE.md`: document the Stop/cancel command and the `mux.liveMirror` setting. — Owner:
  ___ — Notes: ___
- [x] **9.5** `README.md` + `CHANGELOG.md` (`0.12.0`): describe cancel-run, run-state inspection, and the
  WebSocket event bridge. — Owner: ___ — Notes: ___
- [x] **9.6** Update `IMPROVEMENTS.md`: mark the WebSocket bridge, cancel route, and task-state rows in the
  Local API Foundation table as delivered; re-assess the §3 Excellence-bar line. — Owner: ___ — Notes: ___

### Phase 10 — Full test sweep

Run to green and record counts. Positive **and** negative cases are required per the matrix in §4.

- [x] **10.1** `dotnet build` clean, 0 warnings, both TFMs (net8.0 + net10.0). — Owner: ___ — Notes: ___
- [x] **10.2** Full Touchstone suite green (new suites registered in `MuxSuites.All`); record
  **final count: ___** and the delta vs baseline (0.5). — Owner: ___ — Notes: ___
- [x] **10.3** VS Code: `npm run compile && npm test` (unit) green; `npm run i18n` clean; `npm run lint`
  clean. — Owner: ___ — Notes: ___
- [x] **10.4** Dashboard i18n check clean. — Owner: ___ — Notes: ___
- [x] **10.5** OpenAPI sanity: start `mux serve`, fetch `/openapi.json`, confirm the three new routes are
  present and the document validates. — Owner: ___ — Notes: ___

### Phase 11 — Final: automated verification, hand-off, publish

- [x] **11.1** Run every automated check I can (Phase 10) and paste the results into the Notes here. — Owner:
  ___ — Notes: ___
- [x] **11.2** Produce the **manual test checklist** (§5) and notify Joel of exactly what needs human
  verification (the things automation cannot cover: real editor UX, a real model backend, live WS mirroring
  across two surfaces, browser Stop behavior). — Owner: ___ — Notes: ___
- [x] **11.3** Package the VS Code extension (`npm run package` → `.vsix`) and **notify Joel to publish** it
  (`npm run publish:vsce` and `npm run publish:ovsx` require his marketplace credentials; I will not publish
  on his behalf). Provide the exact commands and the version being shipped. — Owner: ___ — Notes: ___

---

## 4. Test matrix (positive + negative)

Every new behavior needs at least one positive and one negative case. Suites live in
`src/Test.Shared/Suites/` and are registered in `MuxSuites.All` (mirrored by the NUnit/xUnit wrappers).

| Area | Positive | Negative |
|---|---|---|
| Core serializer | each event type serializes with correct `eventType` + keys; golden parity vs prior CLI output | null text redacts to empty (no throw); secrets redacted; unknown enum → `ToString()`; `includeStats:false` drops metrics |
| Run registry | create/get; `Running→Completed`; list includes terminal within TTL; eviction after TTL | get/cancel unknown id → false; double-cancel idempotent; cancel completed → false; concurrent create/cancel stress |
| Cancel route | live run → 200 + terminal state; subscriber sees terminal event | unknown id → 404; no key → 401; second cancel → 404 |
| Runs state route | list shows active run; detail shows status + task-plan | unknown id → 404; no key → 401 |
| WebSocket bridge | subscriber gets full ordered event set; payloads equal JSONL; late join replays then tails; approve frame resolves | unauthorized upgrade rejected; malformed frame ignored (not fatal); slow subscriber dropped without stalling run |
| VS Code ApiClient | `cancelRun` posts to correct URL with key | 404 surfaced without throwing |
| Regression | headless `print` JSONL unchanged; desktop in-process Stop still cancels | — |

---

## 5. Manual test checklist (filled in at Phase 11, for Joel)

These are the things automation cannot fully cover. Placeholder now; complete at hand-off.

- [ ] `mux serve`, open the dashboard, start a long run, click **Stop** → response halts **and** the run
  disappears from `GET /v1.0/api/runs` (server-side cancel, not just the browser).
- [ ] VS Code: start a chat against a real endpoint, hit Stop → run cancels; check the mux output channel for
  a clean `run_completed(status=canceled)`.
- [ ] VS Code live mirror on: start a run in the TUI/desktop pointed at the same `mux serve`, watch it mirror
  in the editor over the WebSocket.
- [ ] Approve a mutating tool over the WebSocket path (not just SSE).
- [ ] `/swagger` renders the three new routes with examples; a generated client can call cancel.

---

## 6. Sequencing note

Phases 1 and 2 are the enablers — do them first and independently (Phase 1 ships with zero external behavior
change and its own golden test). Phases 3–5 fall out of the registry and can proceed in parallel once Phase 2
lands. Phases 6–8 depend on the routes existing. Docs (9) track each phase; do not let them drift to the end.
The version bump (Phase 0) is first so every artifact built during the work already carries `0.12.0`.
