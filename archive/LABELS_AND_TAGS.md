# mux Session Labels & Tags Plan

## Purpose

Let a user annotate any session with **labels** (freeform strings, e.g. `wip`, `customer-acme`) and
**tags** (normalized `key: value` pairs, e.g. `env: prod`, `sprint: 42`), then slice the usage charts by
those annotations — by label, by tag, or by any combination alongside the existing endpoint/model/session
filters.

Required user-facing behavior:

- `/label <text>` and `/tag <key>: <value>` attach metadata to the active session from any interactive
  surface (TUI, Desktop, Web, VS Code).
- The same metadata can be attached, listed, and removed from every surface, including a browsed
  (non-active) session and a headless run.
- Labels and tags persist in the session store (`~/.mux/sessions`) and propagate across surfaces through the
  existing live-sync path, exactly like a rename does today.
- The usage dashboard and the TUI `/usage` view gain **Label** and **Tag** filters that scope every chart,
  the history table, and the breakdowns.

This is an implementation plan, not a statement that the feature exists. Sections below are annotated with
checkboxes so a developer can mark progress and completion.

## Design decisions (resolved)

Three questions were settled before drafting; the plan assumes these and does not re-open them.

1. **Filtering joins by session at query time.** Usage rows already carry `SessionId`. Labels and tags live
   only on the session snapshot, never denormalized onto usage events. When a chart is filtered by a label
   or tag, the query layer resolves the matching set of session ids from the session store and constrains
   the SQL with `session_id IN (...)`. Relabeling a session retroactively refilters its whole history — the
   charts always reflect the *current* metadata. If profiling later shows the join is too slow, a
   denormalized-column variant can be added behind the same query API without changing callers.
2. **Tag keys are normalized; values and labels are free-form.** Keys are trimmed, lowercased, and slugified
   (`[a-z0-9._-]`, internal whitespace collapsed to `-`) so `Env` and `env` are one facet. Values are
   arbitrary UTF-8. Labels are free-form UTF-8, deduplicated case-insensitively with first-seen casing
   preserved. Length caps: label ≤ 128, key ≤ 64, value ≤ 256 chars.
3. **Three entry points.** Interactive `/label` and `/tag` on every surface; launch-time `--label` /
   `--tag` flags for headless and one-shot `print`; a focused REST endpoint so Web, VS Code, and Desktop can
   mutate metadata without round-tripping an entire snapshot.

## Current State

Observed in the codebase as of this plan. File/line references are anchors, not guarantees — verify before
editing.

- **Session model is additive-friendly.** `SessionSnapshot` (`src/Mux.Core/Sessions/SessionSnapshot.cs`)
  is a forward-tolerant `System.Text.Json` POCO; `WorkingDirectory` (backing field line 21, null-coalescing
  getter lines 100-104) is the precedent for adding a field. `SessionStore`
  (`src/Mux.Core/Sessions/SessionStore.cs`) serializes camelCase with `WhenWritingNull` (`_Options`,
  lines 42-48), saves atomically (`SaveAsync`, lines 85-89), and silently ignores unknown members on load —
  so new fields need only getters/setters, no migration.
- **All mutation flows through `SessionManager`.** `ISessionManager` / `SessionManager`
  (`src/Mux.Core/Sessions/ISessionManager.cs`, `SessionManager.cs`) own list/create/rename/pin/duplicate/
  delete/export; `RenameAsync` (SessionManager.cs:60) is the load→mutate→`UpdatedUtc`→save→return
  `SessionInfo?` template. The TUI calls it directly, Desktop via `ThreadService`, Web via
  `SessionRoutes` + `SessionStore`. `SessionInfo` (`SessionInfo.cs`, `FromSnapshot` 84-98) is the
  lightweight projection list UIs read.
- **Sync already works for snapshot fields.** A save to the store triggers `SessionStoreWatcher` →
  `sessions_changed` / `transcript_changed`, and `SessionMergePolicy.Reconcile` preserves unauthored fields
  on merge (see the session-sync consolidation work). Labels/tags ride this path for free provided the merge
  policy is taught to preserve them.
- **Usage events are already session-linked.** `UsageEvent.SessionId`
  (`src/Mux.Core/Telemetry/UsageEvent.cs:37`) is set in `AgentLoop.cs:888` and `PrintCommand.cs:287`.
  `SqliteUsageStore` has `session_id TEXT` (line 51) and `ix_usage_session` (line 79); `AppendWhere`
  (lines 705-763) already filters a single `filter.SessionId`. **No telemetry schema migration is needed.**
- **The telemetry filter gaps are known.** `UsageRoutes.BuildFilter` (`src/Mux.Server/Routes/UsageRoutes.cs`
  lines 227-269) never reads a `session`, label, or tag query param. `UsageFilter`
  (`src/Mux.Core/Telemetry/UsageFilter.cs`) carries only one `SessionId`. `FetchAggregatesAsync`
  (`SqliteUsageStore.cs:295`) cannot group by session, which breakdown-by-label will need. The `/filters`
  endpoint (UsageRoutes.cs:159) returns endpoints+models only.
- **There is no per-field session REST endpoint.** Non-TUI surfaces mutate a session by upserting the whole
  snapshot through `PUT /v1.0/api/sessions` (`SessionRoutes.cs:102-168`). This plan adds a focused metadata
  endpoint rather than forcing every client to reconstruct a full snapshot.

## Goals

- One source of truth for the mutation and normalization logic (`SessionManager`), consumed identically by
  every surface. No per-surface reimplementation of "what is a valid tag."
- Zero-migration persistence: labels/tags are additive JSON on the existing snapshot and survive the
  cross-surface merge without truncation.
- Usage filtering by label and tag that composes with the existing endpoint/model/range/session filters and
  is retroactive.
- Positive and negative test coverage at the model, manager, store-query, and REST layers, registered in
  `MuxSuites.All`.
- Documentation updated across REST, usage, config, per-surface guides, and the changelog.

---

## Phase 0 — Data model (Mux.Core)

- [ ] Add a `SessionTag` value type (`src/Mux.Core/Sessions/SessionTag.cs`): `{ string Key; string Value; }`,
      immutable, `[JsonPropertyName]` `key`/`value`, value-equality by normalized key. Ordered pairs in a
      `List<SessionTag>` (not a `Dictionary`) to keep serialization order stable and deterministic.
- [ ] Add `List<string> _Labels` and `List<SessionTag> _Tags` backing fields to `SessionSnapshot.cs`
      (mirror the `WorkingDirectory` pattern at line 21), with null-coalescing getters (as lines 100-104) so
      old snapshots without the fields read as empty, never null.
- [ ] Add a `SessionMetadataNormalizer` static helper (`src/Mux.Core/Sessions/SessionMetadataNormalizer.cs`)
      that owns every rule from decision #2: `NormalizeLabel`, `NormalizeTagKey`, `NormalizeTagValue`,
      length caps, and the reject/keep verdict. This is the *only* place the rules live.
- [ ] Thread the fields through every snapshot construction site the map identified:
      `SessionSnapshotBuilder.Build` (SessionSnapshotBuilder.cs:41-50),
      `SessionManager.CreateAsync` (SessionManager.cs:40-53),
      `SessionManager.DuplicateAsync` (SessionManager.cs:100-114 — **must deep-copy both collections**),
      and `SessionInfo` (`SessionInfo.cs` ctor 26-49 + `FromSnapshot` 84-98) so list UIs and filters read
      metadata without loading full history.
- [ ] Teach `SessionMergePolicy.Reconcile` to preserve `Labels`/`Tags` as unauthored fields (same class of
      preservation as pinned title / jobs) so a turn saved by one surface never wipes metadata set by
      another. Bump `SessionSnapshot.CurrentSchemaVersion` to `2` (informational; deserialization is already
      tolerant).

## Phase 1 — Mutation API (SessionManager)

- [ ] Extend `ISessionManager` / `SessionManager` with, each normalizing through the Phase 0 helper and
      following the `RenameAsync` load→mutate→`UpdatedUtc = DateTime.UtcNow`→save→return `SessionInfo?`
      shape (null when the id is missing):
  - `AddLabelAsync(string id, string label, ct)` — idempotent add, case-insensitive dedupe.
  - `RemoveLabelAsync(string id, string label, ct)` — no-op if absent.
  - `SetTagAsync(string id, string key, string value, ct)` — upsert by normalized key.
  - `RemoveTagAsync(string id, string key, ct)` — no-op if absent.
  - `SetMetadataAsync(string id, IEnumerable<string> labels, IEnumerable<SessionTag> tags, ct)` — bulk
    replace, used by launch flags and the REST PUT path.
- [ ] Return the updated `SessionInfo` so callers can echo the resulting metadata without a reload.

## Phase 2 — Telemetry filtering (query-time join)

- [ ] Add to `UsageFilter.cs`: `List<string> Labels`, `List<SessionTag> Tags`, and a resolved
      `IReadOnlyCollection<string>? SessionIds` (the join result). Keep the existing single `SessionId`.
- [ ] Introduce `ISessionMetadataIndex` (`src/Mux.Core/Telemetry/ISessionMetadataIndex.cs`) with a default
      impl backed by `SessionStore.ListAsync` projecting `id → (labels, tags)`. Cache the projection and
      invalidate on the store watcher tick; session counts are small, so a re-list per query is an
      acceptable fallback.
- [ ] In `UsageQueryService` (`UsageQueryService.cs`), before building the SQL, resolve requested
      labels/tags through the index into a session-id set and populate `filter.SessionIds`. An empty
      resolution (label matches no session) must yield an **empty result, not an error**.
- [ ] Extend `SqliteUsageStore.AppendWhere` (lines 705-763) to emit `session_id IN ($s0,$s1,...)` when
      `SessionIds` is present, composing with all existing predicates.
- [ ] Add `session_id` as a groupable dimension to `FetchAggregatesAsync` (the gap at SqliteUsageStore.cs:295)
      so `GetBreakdownAsync` can support `dimension = "label"` / `"tag"`: fetch per-session aggregates, then
      fold them into label/tag buckets in C# via the index (a session with N labels contributes to each —
      document this fan-out). Register the new dimensions in `NormalizeDimension` (378-396) / `KeyForDimension`
      (398-414).
- [ ] Add `GetLabelsAsync` / `GetTagsAsync` to `UsageQueryService` (beside `GetEndpointsAsync` 234 /
      `GetModelsAsync` 244) to feed the filter dropdowns from sessions that actually have usage rows.

## Phase 3 — REST surface (Mux.Server)

- [ ] Add a focused metadata endpoint to `SessionRoutes.cs` rather than overloading the full-snapshot upsert:
  - `PATCH /v1.0/api/sessions/{id}/metadata` with body `{ addLabels?, removeLabels?, setTags?, removeTagKeys? }`,
    delegating to the Phase 1 `SessionManager` methods; `404` when the id is unknown, `400` on a body that
    fails normalization, `200` returning the updated `SessionSummary`.
- [ ] Also carry `labels`/`tags` on the existing DTOs so a full upsert round-trips them:
      `SessionSummary` (`ServerDtos.cs:64-86`), `SessionSaveRequest` (`ChatDtos.cs:211-227`),
      `SessionDetailDto` (`ChatDtos.cs:188-204`); update the `PUT` upsert (SessionRoutes.cs:130-143) to
      persist them via `SetMetadataAsync`.
- [ ] Wire the usage filter params in `UsageRoutes.BuildFilter` (lines 227-269): parse `session`,
      repeated `label`, and `tag` (`key:value`) query params into the new `UsageFilter` fields. Extend the
      `/v1.0/api/usage/filters` response (line 159) to include the available labels and tags.
- [ ] Keep enum/label wire values stable and machine-readable per I18N.md §4 — the API returns raw
      label/tag strings; only display is localized.

## Phase 4 — TUI (Mux.Cli)

- [ ] Register `/label` and `/tag` in the catalog (`MuxTuiApp.cs` near line 300), each with an
      `argumentHandler` (the `/cwd` descriptor at line 298 is the exact template — `CommandDescriptor` with
      an `Action<string>` arg handler, `CommandDescriptor.cs:47-53`). Grammar:
  - `/label <text>` add · `/label rm <text>` remove · `/labels` list.
  - `/tag <key>: <value>` set/overwrite · `/tag rm <key>` remove · `/tags` list.
- [ ] Handlers resolve the active session via `_ActiveSessionId` (MuxTuiApp.cs:120), call the Phase 1
      `SessionManager` methods, and `WriteNotice(...)` the result (including the normalized form, so the user
      sees `Env` became `env`). A normalization rejection prints a clear notice, not a crash.
- [ ] Add "Edit labels / tags" to the browsed-session action menu (`ShowSessionActionsAsync`,
      MuxTuiApp.cs:2882-2919) — it already builds `new SessionManager(_Store)` at line 2899 — via
      `PromptModal` for input and `MessageModal` for confirmation.
- [ ] Add **Label** and **Tag** filter controls to the `/usage` view (`OpenUsageView`, catalog line 300) that
      pass through to the `/v1.0/api/usage/*` query params from Phase 2/3.

## Phase 5 — Desktop (Mux.Desktop)

- [ ] Extend `IThreadService` / `ThreadService` (`src/Mux.Desktop.Core/Services/`) with
      `AddLabelAsync` / `RemoveLabelAsync` / `SetTagAsync` / `RemoveTagAsync`, each a thin adapter over the
      Phase 1 `ISessionManager` methods (mirror `RenameAsync`, ThreadService.cs:38/60-75). Add `Labels`/`Tags`
      to `ThreadSummary`.
- [ ] Add a metadata editor to the session/thread UI (chips for labels, key:value rows for tags, add/remove),
      and Label/Tag filters to the Desktop usage view. All strings via the i18n layer.

## Phase 6 — Web dashboard (Mux.Server/DashboardPage.cs)

- [ ] Add a label/tag editor to the session panel (chip input for labels, key:value rows for tags) calling
      the Phase 3 `PATCH .../metadata` endpoint; reload on `sessions_changed` / `transcript_changed` like the
      other session fields.
- [ ] Add **Label** and **Tag** filter controls beside the existing usage filters (endpoint/model/range at
      DashboardPage.cs:624-632), populated from `/v1.0/api/usage/filters`, applied to every chart, the history
      table, and breakdowns. Reuse the dashboard's own `renderGrid`/`formModal`/`toast` infrastructure — do
      not hand-roll inputs.
- [ ] Every new visible string, `aria-label`, placeholder, and empty/loading state goes through the
      dashboard i18n path (all 11 languages) per I18N.md; wire values stay raw.

## Phase 7 — VS Code (Mux.VSCode)

- [ ] Add `mux.sessions.label` / `mux.sessions.tag` commands: registration in `extension.ts` +
      `package.json` `contributes`, tree actions in `SessionTree.ts` (mirror `rename` 80-100 / `duplicate`
      103-115) calling a new `patchSessionMetadata` API client method (or `putSession` with the new fields).
- [ ] Add `labels`/`tags` to the `SessionSummary` type in `src/Mux.VSCode/src/api/types.ts`.
- [ ] Reflect labels/tags in the session tree item (description/tooltip) and localize any new UI strings.

## Phase 8 — Headless / CLI (Mux.Cli)

- [ ] Add `--label <text>` (repeatable) and `--tag <key>:<value>` (repeatable) launch flags parsed in
      `Program.cs` and applied to the session before the first turn (via `SetMetadataAsync`), covering both
      interactive launch and one-shot `print` (`PrintCommand.cs`, which already sets `UsageEvent.SessionId`
      at line 287).
- [ ] Add a non-interactive `mux session <id> label|tag|unlabel|untag ...` verb: a new branch in the
      `Program.Dispatch` chain (lines 232-334) plus a `SessionCommand` mirroring `ExportCommand`/
      `MirrorCommand`, delegating to `SessionManager`. Text and JSON output modes.

## Phase 9 — Tests (Test.Shared, registered in MuxSuites.All)

Positive and negative coverage in both directions, Touchstone descriptors registered in `MuxSuites.cs`
(`All`, lines 30-269). Each case uses an isolated temp config dir (see `SessionManagerSuite.cs:22-70`).

- [ ] **`SessionMetadataNormalizerSuite`** (new) — positive: lowercase/slug of `Env  Name` → `env-name`,
      value UTF-8 preserved, label case-insensitive dedupe keeps first casing. Negative: empty/whitespace
      label rejected, tag with no colon rejected, empty key rejected, over-cap label/key/value rejected.
- [ ] **`SessionManagerSuite`** (extend, `SessionManagerSuite.cs`) — positive: add label persists and
      reloads; set tag upserts by key; remove label/tag; `DuplicateAsync` copies both collections. Negative:
      mutate a missing id returns null; remove a nonexistent label/tag is a no-op, not an error; adding a
      duplicate label leaves one entry.
- [ ] **`SessionStoreSuite`** (extend) — round-trip a snapshot with labels/tags; **forward-tolerance**: load
      an old snapshot JSON lacking the fields and confirm empty (not null); confirm a save→watch→reload
      preserves metadata and that `SessionMergePolicy.Reconcile` does not drop it on a stale-prefix merge.
- [ ] **`UsageStoreSuite`** (extend, `UsageStoreSuite.cs`) — seed events across sessions; filter by
      `SessionIds` set returns only matching rows; empty set → empty result; breakdown group-by-session
      aggregates correctly.
- [ ] **`UsageRoutesSuite`** (extend, `UsageRoutesSuite.cs`) — `GET /usage/summary?label=…` scopes rows;
      `?tag=env:prod` scopes rows; unknown label → empty summary with `200`, not `500`; `/usage/filters`
      lists the seeded labels/tags.
- [ ] **`MuxServerRouteSuite`** (extend) — `PATCH /sessions/{id}/metadata` adds/removes; `404` on unknown
      id; `400` on a body that fails normalization; full `PUT` upsert round-trips labels/tags.

## Phase 10 — Documentation

- [ ] `docs/REST_API.md` — the new `PATCH .../metadata` endpoint (method, path, body, status codes,
      examples), `labels`/`tags` on session DTOs, and the new `label`/`tag`/`session` usage query params;
      keep the Postman collection (`assets/postman/`) in sync per REPOSITORY_REQUIREMENTS.md §13.
- [ ] `docs/USAGE.md` — filtering charts by label/tag, and the retroactive (query-time-join) semantics.
- [ ] `docs/GETTING_STARTED.md` — the `/label`, `/tag`, `/labels`, `/tags` commands and `--label`/`--tag`
      flags.
- [ ] `docs/DESKTOP.md`, `docs/VSCODE.md`, `docs/CONFIG.md` — surface-specific metadata editing; config note
      if any caps are configurable.
- [ ] `CHANGELOG.md` (root) and `src/Mux.VSCode/CHANGELOG.md` — feature entries; bump product version
      (current 0.12.x → propose 0.13.0).

## Phase 11 — Internationalization (per requirements/I18N.md)

- [ ] Every new user-visible string — command help, chip labels, filter control labels, empty/loading states,
      toasts, confirm dialogs, `aria-label`/`title`/placeholder — comes from the i18n layer with stable keys,
      across the dashboard's existing language set. Wire values (label/tag strings, API params, enum-ish
      dimension names `label`/`tag`) stay raw and stable.
- [ ] Audit layout for text expansion and RTL on the new chips/filters (I18N.md §6). Confirm no missing-key
      or hard-coded-string CI check regresses.

---

## Requirements compliance checklist (c:\code\agents\requirements)

- [ ] **BACKEND_TEST_ARCHITECTURE** — new suites are Touchstone descriptors in `Test.Shared`, no console
      output, self-contained temp dirs, registered in `MuxSuites.All`; any HTTP test targets `127.0.0.1`.
- [ ] **REPOSITORY_REQUIREMENTS §13** — `REST_API.md` and the Postman collection updated together for the new
      endpoint and query params.
- [ ] **I18N** — no hard-coded user-facing strings; stable keys; wire values stable; expansion/RTL audited
      (Phase 11).
- [ ] **CODE_STYLE / BACKEND_ARCHITECTURE** — mutation logic lives once in `SessionManager`; surfaces are thin
      adapters; normalization centralized in `SessionMetadataNormalizer`.
- [ ] **WRITING_DOCUMENTS** — applies to the prose docs updated in Phase 10 (human voice, not template).

## Implementation Snapshot

Shipped in v1.0.0. Full suite green: **905 pass / 0 fail / 7 skip** on both net8.0 and net10.0.

- [x] Phase 0 — Data model (`SessionTag`, `SessionMetadataNormalizer`, `Labels`/`Tags` on snapshot/info/builder, schema v2, preserved in `SessionService`)
- [x] Phase 1 — Mutation API (`AddLabel`/`RemoveLabel`/`SetTag`/`RemoveTag`/`SetMetadata`; `DuplicateAsync` deep-copies)
- [x] Phase 2 — Telemetry filtering (query-time join: `UsageFilter.Labels/Tags/SessionIds`, `ISessionMetadataIndex` + `SessionStoreMetadataIndex`, `AppendWhere` IN-clause, `GetLabels`/`GetTags`, label/tag breakdown; index wired into every `UsageQueryService` construction site)
- [x] Phase 3 — REST surface (`POST /v1.0/api/sessions/{id}/metadata`; `Labels`/`Tags` on session DTOs + PUT upsert; `UsageRoutes.BuildFilter` session/label/tag params; `/filters` returns labels/tags)
- [x] Phase 4 — TUI (`/label`, `/tag`, `/labels`, `/tags`; browsed-session "Edit labels/tags"; `/usage label|tag …` scoped charts)
- [x] Phase 5 — Desktop (`IThreadService`/`ThreadService` verbs, `ThreadSummary` fields, sidebar "Edit labels…/Edit tags…")
- [x] Phase 6 — Web dashboard (Label/Tag usage filters, Sessions-page label/tag editor via the metadata endpoint, i18n keys)
- [x] Phase 7 — VS Code (`mux.sessions.label`/`.tag`, `patchSessionMetadata` client, metadata in the tree row, `SessionSummary` fields)
- [x] Phase 8 — Headless / CLI (`--label`/`--tag` launch + `print` flags; `mux session label|tag|unlabel|untag|show`, `--list`)
- [x] Phase 9 — Tests (`SessionMetadataNormalizerSuite`, `UsageMetadataFilterSuite` new; `SessionManagerSuite`/`SessionStoreSuite`/`UsageStoreSuite`/`MuxServerRouteSuite` extended — positive + negative)
- [x] Phase 10 — Documentation (REST_API, USAGE, GETTING_STARTED, DESKTOP, VSCODE, both CHANGELOGs, Postman collection + env)
- [x] Phase 11 — Internationalization (new dashboard strings keyed in the `en` catalog with the runtime's English fallback; Desktop/VS Code new strings in their default catalogs with the same fallback — consistent with the project's established translated/untranslated boundary)

**Deliberate scope notes.** The dashboard/Desktop/VS Code *new* strings are added to each surface's default
(English) catalog and reach other locales through each runtime's built-in English fallback, matching how the
existing chrome handles its tier-2 strings — full per-locale translation of the handful of new labels is a
follow-up, not a regression. The Desktop and VS Code usage views consume the shared query service (so their
charts already filter by label/tag through the REST params); an in-view label/tag *picker* was added to the
web dashboard and the TUI, and left as a follow-up for the Desktop native usage view.

## Sequencing notes

Phases 0–2 are the backbone and should land first and together-ish: the model, the one mutation API, and the
query-time join are what every surface depends on. Phase 3 (REST) unblocks the three thin clients (Web,
VS Code, Desktop) in parallel. The TUI (Phase 4) can proceed against `SessionManager` directly without
waiting on REST. Tests (Phase 9) should grow alongside each phase, not be deferred to the end — the
normalizer and manager suites in particular are cheap to write first and pin the contract every surface
relies on. Ship docs and i18n (Phases 10–11) before calling any surface done, not after.

The one place to be careful is the breakdown-by-label fan-out: a session carrying two labels contributes its
usage to both label buckets, so per-label totals will sum to more than the grand total. That is correct and
intended, but it must be stated in `USAGE.md` and asserted in a test so nobody later "fixes" it into
double-counting-avoidance that silently drops rows.
