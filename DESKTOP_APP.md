<p align="center">
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="assets/icon-white.png">
    <source media="(prefers-color-scheme: light)" srcset="assets/icon-black.png">
    <img src="assets/icon-black.png" width="160" height="160" alt="mux">
  </picture>
</p>

<h1 align="center">mux Desktop — Implementation Plan</h1>

<p align="center"><em>An Avalonia desktop client that delivers the full mux TUI capability set, the mux serve dashboard's monitoring and reporting, and first-class conversations/threads — in a Claude-desktop-style interface.</em></p>

---

## 1. Purpose and scope

`mux` today ships two front ends over one engine: the interactive TUI (`Mux.Cli`) and an optional loopback REST server with a web dashboard (`Mux.Server`). This document specifies a third front end — a native cross-platform desktop application, `Mux.Desktop`, built on Avalonia — that reaches **complete parity** with the TUI's capability set, **absorbs the dashboard's monitoring/visualization/reporting**, and adds a proper conversation/thread experience with full thread management. The visual target is the Claude desktop app: a quiet, content-first chat surface with a conversation list on the left, a streaming transcript in the center, and a multi-line composer at the bottom.

The plan is written to be executed. It names the exact `Mux.Core` classes each feature calls, maps every TUI command to a desktop surface, itemizes every dashboard chart and KPI, evaluates which orchestration logic should move out of the TUI into shared `Mux.Core`, specifies an exhaustive test strategy, and states how the result complies with the standards in `c:\code\agents\requirements`.

Three decisions frame everything that follows:

- **Reuse the engine in-process; do not drive it over HTTP.** The `mux serve` REST API exposes only a *tool-free, single-shot* `POST /v1.0/api/chat`; the agentic run-driving routes and the per-run WebSocket event bridge are documented follow-ups, not implemented (`docs/REST_API.md`, "Planned"). A full-fidelity chat client needs streaming text, streamed thinking, tool-call proposals, an interactive approval channel, task-plan events, and cancellation. All of that is already in `Mux.Core` behind `AgentLoop`. `Mux.Desktop` links `Mux.Core` directly, where `Mux.Cli` sits in the dependency graph.
- **Threads are mux sessions.** The engine persists a conversation as a `SessionSnapshot` JSON file under `~/.mux/sessions/`. A desktop "conversation/thread" is one session; thread management maps onto `SessionStore` + `SessionResumeService` + `SessionExporter`. The desktop app adds the *UI* for managing many threads that the TUI only exposes through the `/sessions` picker.
- **Shared logic belongs in `Mux.Core`, not in either front end.** A meaningful amount of front-end-agnostic orchestration (turn lifecycle, the enqueue-while-busy queue, stats aggregation, autosave timing, checkpoint sequencing, tool rebinding) currently lives inside `Mux.Cli/App/MuxTuiApp.cs` only by accident of history. Rather than re-implement it in Avalonia, the plan extracts it into `Mux.Core` so both the TUI and the desktop app consume it. Section 4 is the concrete evaluation and extraction plan.

Out of scope: changing the engine's behavior, replacing the TUI, or adding a hosted/multi-user backend.

---

## 2. Goals and non-goals

**Goals**

- Full behavioral parity with the interactive TUI (§6 parity matrix is the contract).
- Full parity with the `mux serve` dashboard's monitoring, charts, KPIs, and reporting (§8).
- A conversation/thread model with create, rename, pin-title, resume, duplicate/fork, export, delete, and search.
- Extraction of reusable TUI orchestration into `Mux.Core` so both front ends share one implementation (§4).
- A Claude-desktop-style shell: conversation sidebar, centered transcript, bottom composer, light/dark themes, subtle borders over heavy chrome.
- Internationalization from day one per `I18N.md` — the TUI has none, so this is net-new and required.
- An exhaustive, first-class test suite with positive and negative coverage (§14), conforming to the repo's Touchstone architecture.
- Cross-platform packaging (Windows, macOS, Linux) with single-instance behavior and an update path.

**Non-goals**

- No reimplementation of tools, MCP, skills, subagents, telemetry, or session persistence — those are consumed from `Mux.Core`.
- No dependency on `mux serve` being reachable; the server is optional and, if wanted, hosted in-process (§12).
- No web/React port — the frontend React reference (`FRONTEND_ARCHITECTURE.md`) informs *principles* (i18n foundation, theming tokens, form-based settings, copy controls), not the stack.

---

## 3. Architecture

### 3.1 Where the project sits

`Mux.Desktop` is a new Avalonia project under `src/`, referencing `Mux.Core` (the engine) and — optionally, only for the embedded-server toggle — `Mux.Server`. The dependency direction stays one-way and matches the existing tray app:

```
Mux.Search → Mux.Core → { Mux.Server, Mux.Cli, Mux.Agent, Mux.Desktop(new) }
```

`Mux.Agent` already proves Avalonia 12.1.2 coexists with the engine on the repo's shared `net8.0;net10.0` targets (`src/Directory.Build.props`).

### 3.2 UI stack

- **Avalonia 12.1.2** (match `Mux.Agent`), targeting `net8.0;net10.0`, versioned **0.10.0** in lockstep with the rest of the product.
- **MVVM with `CommunityToolkit.Mvvm`** (`ObservableObject` view models over a service layer). `MuxTuiApp` is a 4,000-line imperative shell; the desktop app decomposes that into view models over a service layer, which is both more testable and required for headless UI tests.
- **Code-only chrome in Phase 0, XAML for data-heavy views later.** The shell, splash, and About/Help windows are authored in code, matching the proven `Mux.Agent` pattern (which disables Avalonia XAML compilation to avoid a parallel-build race on the shared intermediate assembly across `net8.0;net10.0`). This guarantees the foundation builds cleanly on both TFMs today. As the transcript, managers, and analytics views grow, they adopt compiled XAML + MVVM data binding; if the parallel XAML-compile race appears, the fallback is to pin the project to a single TFM (§13). The Phase 0 foundation already builds with zero warnings on both frameworks.
- **Markdown + code rendering:** `Markdown.Avalonia` for assistant messages, with `AvaloniaEdit` + TextMate grammars for fenced code blocks and the in-app editors (SKILL.md, prompt profiles, JSON views).
- **Charts:** hand-rolled, no charting dependency — the dashboard already proves this works, and §8 specifies the exact marks to draw.

### 3.3 Layering

```
Mux.Desktop/
  App/                 App bootstrap, DI container, single-instance lock, lifecycle, dispatcher marshaling
  Shell/               MainWindow + shell chrome (sidebar, header, workspace host)
  Views/               XAML views, one per feature surface
  ViewModels/          One VM per view; observable state + commands
  Services/            Front-end services over Mux.Core (the seam)
  Controls/            Reusable controls (CopyButton, MessageBubble, ToolCard, StatusPill, LanguageSelector, charts)
  Charts/              Native chart controls (StackedBarChart, BarChart, DistributionChart)
  I18n/                Localization service, locale registry, catalogs, formatters, markup extension
  Theme/               Design tokens + light/dark resource dictionaries
  Assets/              Icons (reuse assets/icon-*.png), fonts (incl. CJK fallback)
```

View models never touch `Mux.Core` types beyond DTOs; engine wiring lives in **Services**. The critical seam is `IConversationService` (§5.2), which wraps the shared `Mux.Core.Conversation.ConversationController` produced by §4.

**Two assemblies, one UI-free and testable.** The Avalonia app (`Mux.Desktop`) holds only the view layer (windows, controls, splash, About, app lifecycle). All UI-framework-agnostic logic — localization, formatters, view models, and the services over `Mux.Core` — lives in a separate **`Mux.Desktop.Core`** library with no Avalonia dependency. It is referenced by the app and by `Test.Shared`, so the logic is unit-testable in isolation (no display, no headless harness). This split is already in place (§15, Phase 1 logic delivered): `Mux.Desktop.Core` builds on both target frameworks and its services are covered by passing Touchstone suites.

---

## 4. Centralizing shared logic into Mux.Core

A desktop app that re-implements the TUI's turn orchestration would fork logic that ought to be shared, and the two front ends would drift. A structural read of `MuxTuiApp.cs` (4,033 lines) shows roughly 70% is genuine TUIKit presentation, but it hosts a substantial band of **front-end-agnostic conversation orchestration** that belongs in `Mux.Core`. `Mux.Core` today has the primitives (`JobManager`, `JobScheduler`, `WriteLease`, `AgentLoop`, `SessionStore`, `SessionSnapshotBuilder`, `SessionResumeService`, `CheckpointManager`, `HookRunner`, `PricingTable`) but **no controller that sequences turns for an interactive session** — that is the central thing to create.

The extraction is executed as part of this project, not deferred to a separate effort or another team. When implementation reaches Phase 0.5 (§15), the work refactors the existing TUI (`Mux.Cli`) in place: the promoted logic moves into `Mux.Core`, `MuxTuiApp` and its collaborators are rewired to consume the new Core types, and the desktop app's `IConversationService` (§5.2) delegates to the same shared `ConversationController` — one implementation, two front ends, no desktop-local copy. The refactor is **behavior-preserving for the TUI**: it must not change interactive behavior, and it is verified by the TUI's existing suites plus the new Core suites before either front end builds on it. `MuxTuiApp` shrinks by an estimated ~1,500 lines of logic (plus ~1,100 lines of already-agnostic collaborators that simply relocate).

### 4.1 Classification of MuxTuiApp responsibilities

| Concern (source in `MuxTuiApp.cs` unless noted) | Verdict | Destination / note |
|---|---|---|
| Turn lifecycle: `EnqueueOrRun`, `RunTurn`, `OnTurnComplete`, `CancelActiveTurn` (~200 lines) | **Promote** | `ConversationController` |
| Prompt queue / enqueue-while-busy / pause (`_PendingPrompts`, gating) | **Promote** | `ConversationController` (queue policy); queue *strip/editor* stays UI |
| Job scheduling & concurrency (`JobManager`/`JobScheduler`/`WriteLease`) | **Already in Core** | Controller sits on top; nothing moves |
| Stats aggregation math in `OnTurnComplete` + `CloneStatsNoLock` + `ConversationStats` POCO | **Promote** | `ConversationStatsAggregator` + move `ConversationStats` to Core |
| Prompt history (`App/PromptHistory.cs`, 133 lines, no TUIKit) | **Promote (move verbatim)** | `Mux.Core.Sessions.PromptHistoryStore` |
| Autosave-at-turn-boundary + single-flight save gate | **Promote (thin)** over Core | `ConversationController` (build via `SessionSnapshotBuilder`) |
| Session resume history rebuild (`RestoreSession`); replay *rendering* | **Core** rebuild + **Keep** render | `SessionResumeService` exists; controller rebuilds history, UI renders |
| Live tool rebind on MCP/skill/profile edits (`Program.cs` `ApplyTemplate`, `RefreshMcpRuntime`) | **Promote** | `ToolRuntimeBinder` |
| MCP runtime (`App/McpRuntime.cs`, 502 lines, Core-only deps) | **Promote (move verbatim)** | `Mux.Core.Tools.Mcp.McpRuntime` |
| Skill runtime (`App/SkillRuntime.cs`, 397 lines, implements Core `IExternalToolProvider`) | **Promote (move verbatim)** | `Mux.Core.Skills.SkillRuntime` |
| Tool template binders (`ExternalToolsBinder.cs`, `McpTemplateBinder.cs`) | **Promote (move verbatim)** | fold into `ToolRuntimeBinder` |
| Approval-modal wiring (`RequestApprovalAsync` → `PromptUserFunc`) | **Keep** behind existing seam | `AgentLoopOptions.PromptUserFunc` already abstracts it |
| Endpoint switch / effort / thinking / save-and-warm (`ApplyEffort`, `SwitchEndpoint`, `SaveEndpoint`, `WarmInitialModel`) | **Promote** (config mutation) + **Keep** (modals) | `EndpointMutationService` (optional, medium priority) |
| Hook gating (`PassesPromptSubmitHooks`, `FireLifecycleHooksAsync`) | **Core** wrapper | `HookRunner` exists; gate becomes a pre-submit step in the controller |
| Checkpoint/undo/redo sequencing (`RunTurn` record; `UndoLastTurn`/`RedoLastUndo`) | **Core** wrapper | `CheckpointManager` exists; record-before-turn moves into controller |
| Streaming event projection (`AgentEventProjector.cs`, 451 lines) | **Keep** (render) + **Promote** (accumulator) | extract `TurnProjection`/`ITurnObserver`; projector renders over it |
| Title management (`Commands/SessionTitleHelper.cs`, 69 lines) | **Promote (move verbatim)** | `Mux.Core.Sessions.SessionTitleHelper` |
| Session export (`ExportSession`) | **Already in Core** | `SessionExporter`; keep the file/notice glue in UI |
| Usage view formatting (`BuildUsageLines`) | **Core** query + **Keep** render | `UsageQueryService` exists; terminal string formatting stays TUI |
| Endpoint/prompt/tool resolution (`Commands/CommandRuntimeResolver.cs`, 600 lines) | **Promote** | `Mux.Core.Runtime.RuntimeResolver` / `ResolvedRuntime` |
| Agent autostart (`AgentLauncher.cs`) | **Keep** | host-specific; each front end decides |
| Layout, themes, composer, key input, modals, footer, sidebar, thinking animation | **Keep** | pure TUIKit |

### 4.2 New Mux.Core types

- **`Mux.Core.Conversation.ConversationController`** — the centerpiece, sitting on `JobManager`. Owns the turn lifecycle, the pending-prompt queue, stats, autosave, and checkpoint sequencing. Public surface: `Task SubmitAsync(string prompt, CancellationToken)` (hook-gate → checkpoint-record → enqueue-or-run), `void Cancel()`, `PauseQueue()/ResumeQueue()`, queue mutation (`ReorderPending`/`RemovePending`), read-only state (`IsBusy`, `QueuedCount`, `TurnCount`, `PendingPrompts`, `History`, `Stats`, `FocusedJobId`), `SessionSnapshot BuildSnapshot()` / `Task SaveAsync(CancellationToken)`, `void Restore(SessionResumeResult)`, and observer/event hooks (`TurnStarting`, `CreateObserver()`, `TurnCompleted`, `QueueChanged`, `StatsChanged`, `NoticePosted`).
- **`Mux.Core.Conversation.TurnProjection` + `ITurnObserver`** — the UI-free accumulator extracted from `AgentEventProjector`: consumes `IAsyncEnumerable<AgentEvent>`, exposes `CapturedAssistantText`, `LastRunCompleted`, `RenderedError`, `WasCancelled`, and raises first-token / model-working / completed signals. `AgentEventProjector` becomes the TUIKit implementation; the desktop app supplies an Avalonia implementation.
- **`Mux.Core.Conversation.ConversationStatsAggregator`** + the relocated `ConversationStats` POCO — `RecordTurn(projection, totalMs, ttftMs)` and `Snapshot(taskTotal, taskDone, pricing, model)`; cost via the existing `PricingTable`.
- **`Mux.Core.Tools.ToolRuntimeBinder`** — absorbs `ExternalToolsBinder` + `McpTemplateBinder` + the `Program.cs` `ApplyTemplate` coordinator: `Rebind()` rebuilds the `AgentLoopOptions` template from current MCP tools + skills + base prompt under a lock; `SetProfilePrompt(system, compaction)`. `McpRuntime`/`SkillRuntime` are constructed with `Rebind` as their change callback.
- **Move-verbatim** `McpRuntime`, `SkillRuntime`, `PromptHistoryStore`, `SessionTitleHelper`; **new** `RuntimeResolver`/`ResolvedRuntime`; **optional** `EndpointMutationService`.

### 4.3 Risks the extraction must respect

- **Sync-over-async will deadlock Avalonia.** `RunTurn` blocks on `CheckpointManager.RecordAsync` and `JobManager.SubmitAsync` via `.GetAwaiter().GetResult()`, and `PassesPromptSubmitHooks` blocks on `HookRunner.RunAsync`. Safe in the terminal loop, fatal on the UI dispatcher. The Core controller must expose genuinely async methods (no sync-over-async); the terminal caller may keep blocking if it wants.
- **Threading model.** `MuxTuiApp` relies on TUIKit `Pane` thread-safety so background projection writes freely. Avalonia is thread-affine. The controller must raise events with **no thread guarantee** and let each front end marshal to its dispatcher; do not bake "lock then touch UI" into Core.
- **Split the `_Sync` lock.** One monitor currently guards both model state (history, pending prompts, turn-in-flight) and layout state (composer height, theme index). Extraction separates the model lock (moves to the controller) from the layout lock (stays in UI).
- **Preserve the clone-per-job template boundary.** `JobManager` clones the `AgentLoopOptions` template per job; the TUI mutates that template between turns. The `promptSync` lock guards only the prompt/tools rebind, not the endpoint/settings fields — a latent race that must not be copied verbatim into Core. Widen the lock during extraction.
- **No "before the input loop" moment on desktop.** `SubmitInitialPrompt` relies on terminal thread-ordering; the controller must offer an explicit `SubmitAsync` the desktop calls after wiring.

### 4.4 Sequencing (lowest risk first)

1. Move-verbatim the six already-agnostic files + the `ConversationStats` POCO and `RuntimeResolver` — immediate reuse, no behavior change; update their existing suites in `src/Test.Shared/Suites/`.
2. Extract `TurnProjection`/`ITurnObserver` and `ConversationStatsAggregator` — pure additions; the projector delegates to them.
3. Introduce `ToolRuntimeBinder` absorbing the `Program.cs` coordinator.
4. Extract `ConversationController` behind `ITurnObserver` — the largest and highest-value step; do it last, once the observer and binder exist. The TUI adopts it in the same change so both front ends share one path.

Every step is a refactor of shipping TUI code, so each lands behind the TUI's own regression tests: run the existing `Mux.Cli`/`Test.Shared` suites before and after, and treat any behavioral diff in the interactive shell as a defect in the extraction, not an accepted change. New `Test.Shared` descriptors (§14) cover the promoted logic directly. The address-of-record for the shared behavior becomes `Mux.Core`, and `Mux.Cli` keeps only its TUIKit presentation plus a thin `ITurnObserver` renderer.

---

## 5. The engine seam

### 5.1 One turn, end to end

A turn runs as `PrintCommand`/`MuxTuiApp` do: build `AgentLoopOptions` (endpoint, resolved system prompt, `ConversationHistory`, `ApprovalPolicy`, `WorkingDirectory`, `SandboxPosture`, tool allow/deny, `UsageRecorder`, optional MCP/skill tools), construct an `AgentLoop`, enumerate its `AgentEvent` stream, then persist `loop.FinalConversation`. Once §4 lands, the desktop app drives this through `ConversationController` rather than hand-rolling the loop.

The one callback the GUI must implement is **`AgentLoopOptions.PromptUserFunc : Func<ToolCall, Task<string>>`**, returning `"y"` / `"n"` / `"always"` from an approval dialog. The event stream maps to UI updates:

| Event | Desktop rendering |
|---|---|
| `RunStartedEvent` | mark "running", start TTFT stopwatch |
| `AssistantTextEvent` | stream into the assistant bubble; finalize as Markdown |
| `AssistantThinkingEvent` | collapsible dimmed "💭 thinking" panel (endpoint `ShowThinking`) |
| `ToolCallProposedEvent` / `Approved` / `Completed` | tool card: running → approved → ✓/✗ with elapsed ms, result summary, failure reason |
| `TaskPlanUpdatedEvent` | live task checklist card + sidebar `TASKS n/m` |
| `ContextStatusEvent` / `ContextCompactedEvent` | context meter; "compacted" notice |
| `ErrorEvent` | inline error styling (`Code`/`Message`) |
| `HeartbeatEvent` | keep the thinking indicator alive between steps |
| `RunCompletedEvent` | stop timers; write per-turn stats; autosave |

### 5.2 `IConversationService` (the seam)

```csharp
namespace Mux.Desktop.Services
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Agent;
    using Mux.Core.Models;

    /// <summary>
    /// Drives agent turns for a single conversation (thread) and persists the session.
    /// Wraps the shared <see cref="Mux.Core.Conversation.ConversationController"/> so view
    /// models never touch the engine directly.
    /// </summary>
    public interface IConversationService
    {
        /// <summary>Submits one user turn; the caller renders streamed events via the observer.</summary>
        Task SubmitAsync(string prompt, CancellationToken token);

        /// <summary>Cancels the in-flight turn, if any.</summary>
        Task CancelAsync(CancellationToken token);

        /// <summary>Persists the current conversation snapshot to the session store.</summary>
        Task SaveAsync(CancellationToken token);

        /// <summary>Approval callback surfaced as a modal; returns "y" / "n" / "always".</summary>
        Func<ToolCall, Task<string>> ApprovalHandler { get; set; }
    }
}
```

Concurrency (multiple threads with in-flight turns, the single-writer file lease, enqueue-while-busy) is the shared `ConversationController` over `JobManager`/`WriteLease` — not a bespoke desktop scheduler.

### 5.3 Config, sessions, telemetry, MCP — direct calls

- **Config:** `Mux.Core.Settings.SettingsLoader` (endpoints, settings, MCP servers, prompt profile, system prompt, env-var expansion, config dir). All config files are shared verbatim with the TUI.
- **Sessions/threads:** `SessionStore` (`ListAsync`/`LoadAsync`/`SaveAsync`/`DeleteAsync`/`DuplicateAsync`), `SessionSnapshot`, `SessionSnapshotBuilder`, `SessionResumeService.Resume`, `SessionExporter`.
- **Telemetry:** `UsageTelemetry.Create(...)` once at startup; `.Recorder` into every `AgentLoopOptions.UsageRecorder`; read via `UsageQueryService` (§8). Dispose at shutdown.
- **MCP / skills / subagents / plugins / checkpoints:** the shared `McpRuntime`/`SkillRuntime` (post-§4), `SubagentRegistry`, `Mux.Core.Plugins`, `CheckpointManager` + `GitCheckpointService`.

---

## 6. Capability parity matrix

Every interactive capability of the TUI, its backing engine API, and its desktop surface. This is the acceptance contract for parity; nothing here ships "TUI-only."

| # | TUI capability (source) | Backing `Mux.Core` API | Desktop surface |
|---|---|---|---|
| 1 | Send prompt, streamed answer (`AgentEventProjector`) | `AgentLoop.RunAsync` → `AssistantTextEvent` | Composer + streaming assistant bubble, Markdown + code |
| 2 | Streamed thinking (`/thinking`) | `AssistantThinkingEvent`, `ShowThinking` | Collapsible dimmed thinking panel; per-endpoint toggle |
| 3 | Cancel a turn (Esc) | `JobManager.CancelAsync` | Stop button + Esc; "(cancelled)" |
| 4 | Tool-use display | `ToolCallProposed/Approved/Completed` | Tool cards with arg/result summaries, elapsed ms |
| 5 | Tool approval modal | `PromptUserFunc`, `ApprovalRouter` | Custom approval dialog (never a native OS box) |
| 6 | Approval policy (ask/auto/deny), `--yolo` | `ApprovalPolicyEnum` | Per-conversation + global control; "Always this session" |
| 7 | Sandbox posture + allow/deny tools | `ToolGovernance`, `SandboxPostureEnum` | Conversation safety panel: posture, add-dir, globs |
| 8 | Background jobs / prompt queue (Ctrl+G) | `JobManager`/`JobScheduler`, `ConversationController` | Queue strip + editor (reorder/remove) |
| 9 | Enqueue-while-busy behavior | `defaultEnqueueBehavior` | Chooser: run now / queue / add-to-focused |
| 10 | Single-writer write lease | `WriteLease`, `OnWriteLeaseWaitChanged` | "waiting for file lock" indicator |
| 11 | Task plans (`/tasks`) | `TaskPlanUpdatedEvent` | Live checklist + Tasks panel + sidebar `TASKS n/m` |
| 12 | Subagents (`spawn_subagent`) | `SubagentRegistry` | Subagents manager; nested runs inline |
| 13 | Endpoints/models picker (Ctrl+E) | `SettingsLoader`, `RuntimeResolver` | Header switcher + manager |
| 14 | Add/Edit endpoint wizard | `EndpointConfig`, env expansion | Form modal (adapter, URL, model, auth, effort, thinking, quirks) |
| 15 | Import models from Ollama | `OllamaModelLister` | Multi-select import dialog, de-duped |
| 16 | Reasoning effort (`/effort`) | `ReasoningEffortConfig` | Effort selector + per-provider overrides |
| 17 | Model warm/validate on switch | `LlmClient.LoadModelAsync` | Validate on save/switch with status |
| 18 | Prompt profiles (Ctrl+P) | `prompts.json`, `GetActivePromptProfile` | Profile editor (system/tools-disabled/compaction) |
| 19 | MCP servers manager (`/mcp`) | `McpRuntime`, `LoadMcpServers` | Manager: live ●/○ status, tool counts, stdio/http forms, auth |
| 20 | Skills manager (`/skills`) | `SkillRuntime`, `skills.json` | Manager: glyphs/tags, view/edit/enable/duplicate/remove, New/Import |
| 21 | Sessions browser (`/sessions`) | `SessionStore`, `SessionResumeService` | The conversation sidebar + resume |
| 22 | Save + autosave (Ctrl+S) | `SessionSnapshotBuilder`, `ConversationController` | Autosave at turn boundary; explicit Save; dirty indicator |
| 23 | Export session (`/export`) | `SessionExporter` | Export HTML/Markdown from row action + menu |
| 24 | Undo/redo turn | `CheckpointManager`, `GitCheckpointService` | Undo/redo buttons (git work trees only) |
| 25 | Usage view (`/usage`) | `UsageQueryService`, `PricingTable` | Full analytics surface (§8) |
| 26 | Live cost/timing sidebar | `ConversationStatsAggregator`, `PricingTable` | Header/sidebar live stats: TTFT, stream, tokens, cost |
| 27 | Global settings (`/settings`) | `MuxSettings`, `SaveSettings` | Form-based Settings page (grouped, validated, apply-timing hints) |
| 28 | Themes (`/theme`) | n/a (UI) | Theme selector (light/dark + high-contrast); persisted |
| 29 | Sidebar/borders/mouse toggles | n/a (UI) | View menu equivalents |
| 30 | Keybindings overrides | catalog + overrides | Keybindings editor (rebind/unbind by command id) |
| 31 | Plugins: hooks + custom `/name` commands | `Mux.Core.Plugins`, `hooks.json` | Plugins manager; custom commands in the palette |
| 32 | Command palette / menu (F1) | `MuxCommandCatalog` | Command palette (Ctrl/Cmd+K) over the same catalog |
| 33 | Prompt history recall (Up/Down) | `PromptHistoryStore` | Composer history navigation |
| 34 | Web search/retrieve tools | `web_search`/`web_retrieve`, `externalSearch` | Tool cards; providers managed in settings |
| 35 | Compaction (auto/summary/trim) | `MuxSettings` compaction fields | Context meter + "compacted" notices; strategy in settings |
| 36 | Multi-line composer + paste | (UI) | Enter submits, Shift/Ctrl+Enter newline; paste |
| 37 | Conversation title (auto + `/title`) | `SessionTitleHelper`, `TitlePinned` | Editable title; auto-title unless pinned |
| 38 | Config isolation (`MUX_CONFIG_DIR`) | `SettingsLoader.GetConfigDirectory` | Respected; shown in settings/about |
| 39 | Home/overview dashboard | `OverviewRoutes`/`OverviewDto` data (or read directly) | Home view: notices, config KPIs, recent sessions, environment (§8) |
| 40 | Pricing editor | `PricingTable`, `SettingsLoader` pricing | Pricing table editor (§8) |
| 41 | Server info / health | `HealthResponse` / process facts | About/Server Info; embedded-server toggle (§12) |
| 42 | Embedded REST server + dashboard (`mux serve`) | `Mux.Server.MuxServer`, `AgentHost` | Optional "Start local server" toggle |

Capabilities the desktop app *adds* beyond TUI parity because the standards or platform call for them: a **tabbed workspace for parallel workloads** with per-tab status/attention indicators (§7 — the visible surface of the concurrency primitives in rows 8–10), **internationalization** (required by `I18N.md`, §9), and an optional **composer attachment** affordance (§13, flagged, not required for parity).

### 6.1 Delivered status (2026-09-11)

Shipped in the working tree (all builds 0/0 both TFMs): rows **1–6** (streamed chat, thinking panel, cancel, tool cards, approval dialog, approval policy), **11** (**live task-plan checklist** — `TaskPlanUpdatedEvent` renders/updates an in-transcript checklist with per-task status glyphs), **12** (**Subagents manager**), **13–14** (endpoint picker + **Endpoints manager**, form at full TUI field parity — timeout, auto-approve, max-iterations, reasoning effort, Gemini budget, show-thinking), **18** (**Prompt profiles manager**), **19** (**MCP servers manager** — stdio/http forms, **auth** none/bearer/apikey, **Validate connectivity** window; live ●/○ inline status still pending), **20** (**Skills manager** — table with enable/disable, **Add** scaffold + **Edit** SKILL.md; Import still pending), **21–23** (conversation sidebar, autosave/auto-title, export), **25–26** (Usage dashboard with candlestick charts + per-conversation `/context` stats), **27–28** (Settings form, light/dark theme), **32** (in-chat `/?` menu + **command palette** `Ctrl+K` / `/commands`), **36–37** (composer keys, editable title; auto title starts from the first message and is replaced by an **AI summary** once the conversation reaches 250 chars), **15** (**Import models** — an endpoint row action that lists a backend's models via `EndpointModelLister` and adds the selected ones as endpoints; covers Ollama), **16** (**`/effort` quick selector** — Off/Minimal/Low/Medium/High for the current endpoint), **35** (**`/compact`** — manual conversation compaction in both the desktop and TUI via a shared `ConversationCompactor`; the editable compaction prompt now drives both manual and automatic compaction), **17** (**validate model on switch** — the header shows ✓ ready / ✗ unreachable when you pick an endpoint, via `LlmClient.LoadModelAsync`), **33** (**prompt-history recall** — Up/Down at the composer edges walk previous prompts; persisted, unit-tested), **39** (**home/overview** — the empty state shows a config snapshot [endpoints/default model/MCP/skills/telemetry] and clickable recent conversations), **34** (**Web-search providers manager** — `/search`), **38** (config directory shown in About), **40** (**Pricing editor** — `/pricing`), **31** (**Plugins manager** — `/plugins`: two sortable tables over `hooks.json` for lifecycle hooks [name/event/command/args/blocking/timeout] and custom `/name` commands, with add/edit/delete via `HookFormDialog`/`CustomCommandFormDialog`), **30** (**Keybindings editor** — `/keys`: a sortable table over a data-only `KeybindingCatalog` [ids mirror the TUI] with rebind [key-capture dialog] / unbind / reset / reset-all, layered by a testable `KeybindingEditorModel` and persisted to the shared `keybindings.json`), **24** (**Undo/redo turn** — header ↶/↷ buttons + `/undo`,`/redo` + palette: a per-turn checkpoint is recorded via `Mux.Core.Checkpoints.CheckpointManager`/`GitCheckpointService` before each turn, and undo/redo restore the git working tree; the buttons hide outside a git repository and disable when the stacks are empty), **29** (**View toggles** — a header ☰ View menu with checkable "Collapse sidebar" and "Auto-expand thinking" items, both persisted in `desktop.json` [`DesktopPreferences.SidebarCollapsed`/`AutoExpandThinking`] and restored on launch). About also shows runtime/OS/architecture diagnostics (part of 41). Every interactive control now carries a descriptive hover tooltip; the Usage events table includes a Conversation column. All five managers are **sortable tables** (reusable `DataTableView<T>` — ⋯/right-click actions, per-column foreground, green badges, **single-click row opens the editor**) and open **modally**; the MCP table shows a live **✓/✗ connectivity glyph** probed on open. Sidebar expanded sections (Conversations, Manage) render in a highlight inset. The endpoint form is a **two-column no-scroll** layout; **Settings** now spans ~20 grouped fields. Every dialog keeps its buttons **outside** the scroll area. **Code-block syntax highlighting** via `Mux.Desktop.Core` `CodeTokenizer` (unit-tested). **Not yet built:** rows 7–10, 42 (and the health/embedded-server parts of 41). Row 35 (compaction) is done — manual `/compact` plus an automatic-compaction notice during a run.

**Tabbed workspace — Increment 1 (2026-09-11):** the `WorkspaceViewModel`/`WorkspaceTabViewModel` VMs are now bound to a real Avalonia **tab strip** (`BuildTabStrip`/`RenderTabStrip`/`BuildTabButton` in `MainWindow`, docked below the header). Opening any conversation (sidebar click, New, or a turn that creates a thread) routes through `OpenThreadAsync`, which calls `OpenOrFocusTab` (open-or-focus, no duplicates); each tab shows a **status dot** (idle/running/unread/error/needs-approval → `TabStatus`), the title, an unread count, and a **✕ close**. Clicking a tab switches threads (`SelectTabAsync`); closing the active tab falls back to another open tab or clears to the empty state (`ClearActiveConversation`); deleting a thread (single or bulk) prunes its tab (`PruneClosedTabs` on reload). The active tab's title tracks the heuristic/AI/rename title (`SyncActiveTabTitle`), and `SetSending` feeds `NotifyBusy` so the active tab shows a running dot. **Still single-conversation under the hood** — switching tabs reloads the thread into the one shared runtime, and switching/closing is blocked mid-turn.

**Increment 2a — per-tab transcript preservation (2026-09-11):** each tab now keeps its **own rendered transcript panel** (`_TabTranscripts` dict keyed by thread id). `OpenThreadAsync` points `_Transcript` (the target of every render helper) at the thread's cached panel via `GetOrCreateTranscriptPanel` and swaps `_TranscriptScroll.Content`, so switching tabs is **instant and preserves scroll** instead of clearing and re-rendering from disk. Panels are dropped on tab close / prune / delete, and cleared on theme rebuild (`RefreshAfterRebuild` re-renders the current thread). Verified the app launches clean headless. Still a single live runtime/turn (no background execution yet).

**Increment 2b (not done):** true parallel per-tab runtimes — each tab its own `ConversationService` + `AgentLoopTurnRunner` (the runner carries SessionId/EndpointName) + streaming state, plus routing a **background** tab's tool-approval to `WorkspaceTabViewModel.NotifyApprovalPending` instead of the live modal. This is the rows 8–10 concurrency surface; its parallel behavior **cannot be verified headless** and needs the user running the GUI.

---

## 7. Conversation / thread experience

The TUI treats sessions as a modal picker; the desktop app makes threads the spine of the UI, styled like Claude's conversation list.

**Left sidebar — conversation list.** Backed by `SessionStore.ListAsync`, showing title, locale-formatted last-updated time, and endpoint/model, sorted by `UpdatedUtc`. Actions: **New** (create a session; per-conversation working directory), **Rename / pin title** (`Title`/`TitlePinned`; auto-titling continues unless pinned, mirroring `SessionTitleHelper`), **Resume** (`SessionResumeService.Resume` rehydrates and replays completed/interrupted turns), **Duplicate / fork** (`DuplicateAsync`), **Export** (`SessionExporter`), **Delete** (behind a custom confirm), and **Search** (client-side over titles and, on demand, message content).

Each conversation carries its own endpoint/model, approval posture, working directory, effort, and thinking setting, so different threads can target different backends at once. Because each thread's turns are jobs under the shared `WriteLease`, several conversations can stream simultaneously while file-mutating tools serialize safely.

**Center — transcript.** Message bubbles, Markdown with fenced-code highlighting, inline tool cards, collapsible thinking, and the live task checklist, in an independent scroll container.

**Bottom — composer.** Multi-line, Enter to submit, Shift/Ctrl+Enter for newline, paste-as-one-prompt, prompt-history recall, and the queue strip when a turn is in flight.

### Tabbed workspace and parallel workloads

The workspace is tabbed so several conversations run as parallel workloads at once — start a long turn in one tab, open another and keep working, and let the first finish in the background. Each open tab is one conversation bound to its own `ConversationController` (§4) over the shared `JobManager`/`WriteLease`, so turns in different tabs stream concurrently while file-mutating tools serialize safely across all of them. Opening a thread from the sidebar opens (or focuses) its tab; closing a tab leaves the session persisted on disk. The sidebar remains the full thread list; the tab strip is the set of *currently open* workloads.

Each tab carries a **status indicator** so a glance across the strip answers "what needs me?" The states, in priority order:

- **Awaiting approval** — a tool call is proposed and the conversation's policy is *ask*. This is the highest-priority signal (an amber badge), because a background workload is blocked on the user. It is the desktop equivalent of the TUI's approval modal, surfaced per tab.
- **Attention / unread** — new assistant text, a completed tool call, a task-plan change, or an error arrived in a tab that is **not** focused. The tab shows an unread dot and, optionally, a count of new events. Focusing the tab clears it.
- **Running** — a turn is in flight (an animated spinner in the tab).
- **Error** — the last turn ended in an error (a red marker) until the tab is next focused.
- **Idle** — nothing pending.

The attention model is driven by the same `AgentEvent` stream the transcript renders. Because the shared controller raises events with no thread affinity (§4.3), each tab's view model marshals to the UI dispatcher and, when its tab is not the active one, promotes the event to the tab's status: approval-needed and error outrank unread, unread outranks running. Focus resets the tab to reflect only live state (running or idle). Optionally — off by default, respecting focus and a user setting — the app raises an OS notification or flashes the taskbar when a **background** tab needs approval or finishes a turn, so a workload that completes while the user is in another app still gets noticed. Notifications are never raised for the focused tab.

This sits directly on the concurrency primitives the parity matrix already requires (rows 8–10): the queue, the write lease, and per-conversation controllers. Tabs are the visible surface of running many of them at once. The attention model itself is already implemented as tested, UI-free view models in `Mux.Desktop.Core` — `WorkspaceTabViewModel` (status/attention from the event stream + focus) and `WorkspaceViewModel` (open/focus/dedup/close) — covered by `WorkspaceSuite`; the Avalonia tab strip binds to them in Phase 4.

### Conversation titles, composer, and transcript UX

The details that make the chat feel finished, gathered here because they are required, not optional:

- **Titles.** A conversation shows a meaningful title: the model-generated/first-message title when one exists, otherwise **"Untitled conversation"** — never the raw model or endpoint name. Titles are **editable** (rename) and conversations are **deletable** by the user, both from a right-click **context menu** on each sidebar row (Rename, Delete, Export), with a confirm on delete. The sidebar list is compact (small row padding), not loosely spaced.
- **Composer keys.** **Enter sends.** **Ctrl+Enter, Shift+Enter, and Ctrl+J** insert a newline. (This matches the TUI's submit/newline convention.)
- **Response rendering.** Assistant replies render in their own **bubble**, as **Markdown** (headings, lists, bold/italic/inline-code, links, and fenced code blocks) via a dependency-free `MarkdownRenderer` — streamed as plain text, then rendered on completion. A collapsible **"💭 Thinking"** panel sits *above* the response and streams the model's reasoning when the endpoint has thinking enabled (collapsed by default). Later: syntax highlighting inside code blocks.
- **Wait state.** While a turn is in flight before the first token, a **thinking indicator** shows an animated state with rotating light-hearted quips to read while waiting, replaced by the response as it streams.
- **Per-turn and per-conversation stats.** Each completed turn shows an **ⓘ** with TTFT/streaming/tokens on hover. **`/context`** opens a per-conversation statistics view: token totals by type (horizontal bar chart with values), time-to-first-token and latency (avg/p95/p99), turn count, context window, and context utilization — sourced from the shared usage telemetry store filtered by the conversation's session id (`UsageFilter.SessionId`, added to `Mux.Core`).
- **Slash-command menu.** Typing **`/?`** (or `/help`) shows an in-chat, expandable menu of quick commands (`/clear`, `/context`, `/new`, `/help`); each is clickable. Slash commands are intercepted at submit and executed instead of being sent to the model.

### Settings and management access

A **Settings** surface is reachable from the chrome (a gear in the header), editing the key `settings.json` fields through a form (approval policy, iteration cap, auto-compact, task planning, telemetry) via `SettingsLoader`. The other managers (endpoints, MCP, skills, prompts, keybindings) follow in Phase 6–8; the settings entry point and a first form land early so the app is configurable without hand-editing JSON.

---

## 8. Monitoring, visualization, and reporting

The `mux serve` dashboard's analytics are the second surface the desktop app must reproduce natively. The dashboard is a hand-rolled SVG single page over `UsageQueryService`; the desktop app draws the same marks with native controls and calls `UsageQueryService` directly — no HTTP, no charting dependency. Every chart, KPI, filter, and column below is specified so it can be rebuilt exactly.

### 8.1 Home / overview

A command-center landing view backed by the same data `OverviewRoutes` assembles (config files + session store + server facts; no telemetry DB): a **notices strip** (info/warning/success next-step hints), a **row of nine config KPIs** (endpoints, MCP servers, prompts, subagents, skills enabled/total, hooks + commands, keybindings, sessions, total messages), a **default endpoint** card (endpoint/adapter/model), an **environment** card (version, uptime, active prompt, auth enabled, config dir), and **recent sessions** (five newest, clickable into the thread). The desktop app reads these values directly from `SettingsLoader` + `SessionStore` rather than the HTTP route.

### 8.2 Usage analytics

The primary monitoring surface: a filter bar, a KPI strip that changes with the selected metric, a chart with six metric tabs, and a per-call history table. All four data pulls are `UsageQueryService` calls (`GetSummaryAsync`, `GetTimeseriesAsync`, `GetEventsAsync`, `GetEndpointsAsync`/`GetModelsAsync`); when telemetry is disabled they return empty with an `Enabled:false` signal and the view shows a disabled state.

**Filters and ranges.** Range segmented control (Hour / **Day** default / Week / Month), endpoint select, model select, and refresh. The desktop app should also surface the two filters the web UI omits but `UsageFilter` supports — **call kind** and **success/failure** — and the **breakdown dimension** (`model`/`endpoint`/`provider`/`command`/`callkind`) via `GetBreakdownAsync`, rendered as a "group by" table. Bucketing is server-side-fixed and must be replicated when calling `GetTimeseriesAsync`: hour = 60×1-min, day = 96×15-min, week = 84×2-hour, month = 60×12-hour, dense/zero-filled so every slice renders.

**Charts** (native controls under `Charts/`), each reading `UsageBucket.Metrics` per slice:

- **Tokens — stacked bar.** Three series: Prompt (`InputTokens − CachedTokens`, floored at 0), Cached (`CachedTokens`), Output (`OutputTokens`).
- **Cost — bar.** `CostUsd` per bucket.
- **Latency / TTFT / Streaming / Throughput — distribution (box-and-whisker) charts.** Each bucket draws a five-number summary from the matching `UsageDistribution` (`Min`, `Avg`, `P95`, `P99`, `Max`, `Count`): a min–max wick with end caps, an avg–P95 filled box, an avg line, and a P99 tick. Sources: `TotalMsDist`, `TtftMsDist`, `StreamMsDist`, `ThroughputDist`.

Each chart carries a legend and a hover tooltip; bar tooltips list each series plus a total, distribution tooltips list Max/P99/P95/Avg/Min plus sample count. X-axis labels format by range (time-of-day, weekday+hour, month/day) using locale-aware formatters (§9).

**KPI strip** (changes per tab, all from `UsageSummary.Metrics`): tokens tab shows total/prompt/cached/output tokens, cost, calls, cache-hit rate, error rate; cost tab shows cost, calls, avg cost/call, error rate; distribution tabs show Min/Avg/P95/P99/Max/Samples from the tab's `UsageDistribution`.

**Per-call history table** (`GetEventsAsync` → `UsageEventPage` of `UsageEventRow`): columns When, Endpoint, In, Cached, Out, TTFT, Latency, tok/s, Cost, Status. Reuse the shared desktop data-grid: sortable headers (tri-state), per-column filtering, page-size `[10,25,50,100]` (default 25), first/prev/next/last with "showing a–b of n", and a column-visibility picker persisted per table. Row actions: **View** (a detail modal grouped into Identity / Tokens / Timing / Cost from the full `UsageEventRow` — including provider, call kind, command, session, host, reasoning tokens, finish reason), **View JSON**, and **Delete** (`DeleteEventAsync` behind a confirm). This satisfies the request-history/reporting expectations in `DASHBOARD_STYLE_AND_USABILITY.md`.

### 8.3 Pricing editor and server info

**Pricing** is a form-based table over `pricing.json` (`PricingTable` of `ModelPricing`): columns Model, Input, Cached, Output ($/Mtok); add/edit/duplicate/delete rows; save via `SettingsLoader.SavePricing`. Because cost is derived at read time, a rate edit re-values history immediately. **Server Info / About** shows health, version, uptime, pid, config dir, and the optional embedded-server toggle (§12), with copy controls on the server URL.

### 8.4 Data-source summary

| Surface | `Mux.Core` call | Returned shape |
|---|---|---|
| KPI strip | `UsageQueryService.GetSummaryAsync(filter)` | `UsageSummary{FromUnixMs, ToUnixMs, Metrics}` |
| All six charts | `GetTimeseriesAsync(filter, bucketMs)` | `ListResponse<UsageBucket>` |
| Group-by table | `GetBreakdownAsync(dimension, filter)` | `ListResponse<UsageBreakdownRow>` |
| History table | `GetEventsAsync(filter, page, pageSize)` | `UsageEventPage` |
| Row delete | `DeleteEventAsync(id)` | `UsageDeleteResult{Deleted}` |
| Filter selects / enabled state | `GetEndpointsAsync` / `GetModelsAsync` | endpoint/model lists |
| Pricing | `SettingsLoader.LoadPricing`/`SavePricing` | `PricingTable` |

`UsageQueryService` is constructed with a `SqliteUsageStore` (the shared `~/.mux/usage.db`) and a `Func<PricingTable>` provider, so the desktop app sees CLI activity too.

---

## 9. Internationalization

`I18N.md` is a hard requirement, and the TUI ships none, so this is built fresh and adapted from web `i18next` guidance to Avalonia.

A single **locale registry** lists the twelve baseline locales (`en, es, pt, fr, it, de, zh, ar, ru, ms, hi, ja`) with English name, autonym, direction, fallback (`en`), and font hint; `ar` exercises RTL, `zh`/`ja` CJK, plus a pseudo-locale for expansion/bidi. An **`ILocalizationService`** exposes `SetActiveLocale`, a `this[key]` indexer, and a `CultureChanged` event; a `{loc:Tr Key}` **XAML markup extension** binds text to the indexer and updates live without a restart. Stable keys, not English sentences, are identifiers; wire values, enum names, tool names, and config keys stay stable — only their display labels translate. Catalogs are per-locale UTF-8 JSON, lazy-loaded, with CI checks for missing/orphaned keys and new hard-coded strings.

A **`LocaleFormatters`** helper wraps culture-aware number, date, time, date-time, relative-time, duration, byte, percent, and list formatting — every formatter takes the active culture explicitly, including chart axis and tooltip timestamps. On locale change the shell sets `FlowDirection` (RTL for `ar`) and persists the choice to a desktop preferences file (`~/.mux/desktop.json`); a language selector lives in the header and Settings. The Definition of Done follows `I18N.md`: no hard-coded user-facing strings, locale persists across restarts, direction flips correctly, formatters are locale-aware, statuses/confirmations/errors/accessibility strings are localized, and pseudo-locale + missing-key checks pass.

---

## 10. Visual design system

The look follows the Claude desktop app and the restraint in `DASHBOARD_STYLE_AND_USABILITY.md`: quiet, dense-but-calm, subtle borders over shadows, one accent, mono for code and IDs. **Tokens** are Avalonia resources resolved via `DynamicResource`, themed light/dark: `surface`, `surface-alt`, `border`, `text`, `text-muted`, `accent`, `success`, `warning`, `danger`, `info`; typography tokens (UI font, mono font, a type scale of base 14–15 / code 12–13 / titles 22–28); spacing, radii (4–8 controls, 8–12 panels), control heights. A warm off-white light theme and a charcoal dark theme echo Claude's palette; a high-contrast theme covers accessibility. Theme choice persists.

The **shell** matches the required structure: a persistent conversation sidebar (220–260 px, collapsible to ~56–64 px), a ~52–64 px header carrying endpoint/model/effort/thinking chips and utility icon buttons (theme, language, settings, new chat), and an independently scrollable workspace. Copy-to-clipboard is one reusable control everywhere (IDs, URLs, session ids, code, resolved commands) with a consistent copied-state and no layout shift. Modals are custom components — never native `alert`/`confirm` — with visible headers and body-scoped scrolling.

**Splash and About/Help.** A borderless startup **splash** (the mux glyph, product name, version, tagline, loading status over the dark terminal palette) shows while the app initializes, then hands off to the main window; shutdown is tied to the main window so the splash closing never ends the process. An **About / Help** window — modeled on the tray agent's About window — presents the mux ASCII wordmark in the accent green, the version and license, a clickable repository link, and a short getting-started help section, reachable from the header. Both are localized and load the theme-aware embedded logo.

---

## 11. Settings, management, and diagnostics surfaces

Each management surface is a form-based view (labeled controls, validation, masked secrets), with a secondary read-only JSON view for inspection.

- **Settings** — grouped editor over `MuxSettings` (Agent, Context/Compaction, Concurrency, Skills, Tasks, External Search, REST, Telemetry), each field with the right control and the engine's clamps. Fields that apply next-turn vs. next-launch are annotated; secrets show "set / not set" and write only when changed; env-overridden values render read-only. Backed by `SaveSettings`.
- **Endpoints / MCP / Prompts / Subagents / Skills / Keybindings / Plugins** — one manager view each, all CRUD over the shared config files through `SettingsLoader`, with live effects where supported (MCP reconnect, effort/thinking next turn).
- **Usage / Home / Pricing / Server Info** — the monitoring surfaces in §8.

---

## 12. Optional embedded server

Settings exposes a "Run local server" toggle that starts `Mux.Server.MuxServer` in-process, reusing the wiring from `Mux.Agent/AgentHost.cs` (the `rest` block, auto-generated API key, shared `SessionStore` and `UsageTelemetry`). It is off by default and never required — the app always drives the engine in-process. A single-instance lock (the `Mux.Agent.AgentInstanceLock` pattern) prevents two hosts binding the same port.

> **Delivered (2026-09-12, row 42):** `Mux.Desktop.Core/Services/EmbeddedServerService.cs` (process-wide singleton, testable) reuses `ServeCommand`'s boot — loopback bind, generated/persisted local API key, shared `SessionStore` + `UsageTelemetry` — with `Start`/`Stop`/`Toggle` and `IsRunning`/`BaseUrl`/`DashboardUrl`/`HealthUrl`/`ApiKey`/`LastError`. Surfaced via `Views/LocalServerWindow` (Start/Stop toggle + URLs + API key + "Open dashboard"), reachable from the command palette and the Manage drawer (🌐). Off by default; `EmbeddedServer` Touchstone suite covers the stopped-state contract and no-op safety. *Not yet done:* the single-instance/port lock (a failed bind currently surfaces as `LastError`).

---

## 13. Project structure, coding standards, and open decisions

Source lives at `src/Mux.Desktop/`, added to `Mux.sln`, and follows `CODE_STYLE.md` strictly: one class/enum per file; namespace at top with `using` inside; system usings first then others, each alphabetized; XML docs on every public member; `_PascalCase` private fields; no `var`; no tuples; explicit types; `Nullable` enabled; guard clauses and specific exceptions with context; the full dispose pattern for anything holding the engine, write lease, or telemetry; a `CancellationToken` on every async method; no `Console.WriteLine` in library code; configurable values as backed properties, not literals. View models are `ObservableObject`s with `[ObservableProperty]`/`[RelayCommand]`; views are XAML with no logic in code-behind; services are constructor-injected.

**Open decisions** (each has a default so work is not blocked):

- **XAML compilation across `net8.0;net10.0`** — *resolved for Phase 0:* the shell/splash/About chrome is code-only (builds clean on both TFMs, matching `Mux.Agent`); data-heavy views adopt compiled XAML as they land, with a single-TFM pin as the fallback if the race recurs.
- **Runtime resolver + orchestration reuse** — *decided:* the §4 extraction into `Mux.Core` is done as part of this project in Phase 0.5, refactoring the TUI in place; the desktop app builds on the shared `ConversationController`, not a local copy.
- **Markdown/code libraries** — recommend `Markdown.Avalonia` + `AvaloniaEdit`/TextMate; alternative is a custom lightweight renderer.
- **Composer attachments/images** — recommend a later phase feeding file paths to tools (not a new model input path), so it stays parity-safe. Not required for parity.
- **Auto-update** — recommend Velopack (cross-platform, MIT); alternative is OS-native per platform.

---

## 14. Testing

Testing is a first-class deliverable, not a trailing task. Coverage is exhaustive and deliberately balanced between **positive** cases (the feature does what it should on valid input and expected state) and **negative** cases (invalid input, missing config, cancelled work, backend failure, permission denial, corrupt data, race conditions). The suite conforms to the repo's Touchstone architecture: front-end-agnostic logic is expressed as `TestCaseDescriptor`s in `Test.Shared` (no console output, self-contained, assert by throwing, `skip`+`skipReason` for not-yet-ready) and executed through `Test.Automated` (console runner, JSON export, exit 0/1), `Test.Xunit`, and `Test.Nunit`. UI behavior that needs an Avalonia application lifetime runs under `Avalonia.Headless` as xUnit/NUnit tests in a dedicated `Test.Desktop.Ui` project, since headless UI cannot be modeled as a pure descriptor. Any loopback target (MCP stub, embedded server) binds and calls `127.0.0.1`, never `localhost`.

Because the §4 extraction moves orchestration into `Mux.Core`, the most valuable logic tests live in `Test.Shared` and protect **both** front ends at once.

### 14.1 Layers

- **Descriptor suites (`Test.Shared`, Touchstone)** — `ConversationController`, `ConversationStatsAggregator`, `TurnProjection`, `PromptHistoryStore`, `SessionTitleHelper`, thread management over `SessionStore`, `RuntimeResolver`, `ToolRuntimeBinder`, settings validation/clamping, i18n registry/formatters, usage bucketing/mapping and pricing math, tool-governance and approval mapping, MCP config CRUD. Executed by all three runners.
- **Headless UI (`Test.Desktop.Ui`, Avalonia.Headless)** — shell navigation, streaming render, the approval modal, thread CRUD, the settings form, chart controls, and the data grid.
- **Integration** — the engine seam against a stub `IChatClient` / local Ollama, and MCP against a stub server on `127.0.0.1`.
- **Localization** — bootstrap/persistence, `FlowDirection` sync, formatter correctness across Latin/CJK/RTL, and a pseudo-locale smoke pass.
- **Visual/snapshot, accessibility, performance, and packaging smoke** — cross-cutting passes (§14.4).

### 14.2 Positive and negative coverage by area

| Area | Representative positive cases | Representative negative cases |
|---|---|---|
| Turn lifecycle (`ConversationController`) | valid prompt streams events, appends user+assistant to history, autosaves at boundary, stats update | empty/whitespace prompt rejected; cancel mid-turn yields cancelled state and no history append; submit while busy respects enqueue policy; backend error surfaces `ErrorEvent` without corrupting history |
| Prompt queue | enqueue while busy runs FIFO on completion; reorder/remove mutate pending; pause suspends dequeue | dequeue while paused does nothing; remove non-existent index is a no-op, not a crash; cancel drains the active turn but preserves the queue |
| Tool approval | "y"/"always" execute; "always" auto-approves the rest of the session; deny blocks and reports | denied mutating tool never writes; approval for an unknown tool id fails closed; approval callback exception aborts the turn cleanly |
| Tool governance / sandbox | read-only tools auto-run under AutoSafe; allow-glob permits matching tools | deny-glob wins over allow-glob; `read-only` posture blocks a write tool; a write outside an add-dir root is refused |
| Write lease / concurrency | two conversations stream concurrently; file-mutating tools serialize | second mutating job waits for the lease and does not interleave writes; lease released on cancel/fault |
| Threads (`SessionStore`) | create/list/rename/pin/duplicate/delete round-trip; resume replays completed and interrupted turns | load missing id throws typed error; delete non-existent is a no-op; corrupt/forward-version session file reads tolerantly or fails with context, never crashes the app |
| Titles | auto-title updates until pinned; `/title` sets and holds | pinned title never overwritten by auto-title; empty title normalizes to a safe default |
| Runtime resolution | named → default → first endpoint; CLI-equivalent overrides applied; env vars expanded | unknown endpoint name errors; no endpoints falls back to internal default; unresolved `${VAR}` handled deterministically |
| Endpoints CRUD | add/edit/remove persist; blank secret preserves stored value | removing the active endpoint refused; invalid base URL/port rejected; duplicate name de-duplicated |
| Ollama import | discovered models import and de-dupe against existing | unreachable server surfaces a clear error; already-configured model omitted |
| MCP | stdio/http server connects, tools discovered and callable, live status flips | bad command/URL shows offline and retries; `${VAR}` auth expands; malformed `mcp-servers.json` isolated, not fatal |
| Skills | enable/disable/duplicate/remove; valid `SKILL.md` runs a command | invalid skill flagged `⚠` with reason; disabled skill not exposed; interpreter timeout captured |
| Settings | each field saves and clamps; next-turn vs next-launch honored | out-of-range values clamped to bounds; masked secret unchanged when left blank; env-overridden field read-only |
| Undo/redo | inside a git tree, undo/redo roll the working tree a turn | outside a git tree, controls disabled; undo with no checkpoint is a no-op |
| Usage analytics | summary/timeseries/breakdown/events map to DTOs; buckets zero-filled per range; cost re-values on pricing edit | telemetry disabled returns empty with `Enabled:false`; empty window renders empty chart not an error; delete of unknown row returns `Deleted:0` |
| Pricing | add/edit/delete rate persists and re-values history | negative/non-numeric rate rejected; unknown model costs zero until added |
| i18n | switching locale updates strings live and persists; RTL flips `FlowDirection`; formatters match culture | missing key falls back to `en` and is flagged in CI; pseudo-locale expansion does not clip; unknown locale canonicalizes to a supported code |
| Export | HTML and Markdown render a session offline | export of an empty/missing session handled with a clear message |
| Command palette / keybindings | every catalog command reachable; rebind takes effect | unparseable chord ignored (built-in stays); unbinding removes the chord without breaking startup |

### 14.3 Parity guard

A data-driven descriptor asserts that **every `MuxCommandCatalog` command id maps to a registered desktop surface** and that every §6 matrix row has a covering test. A new TUI command then fails the build until the desktop app catches up — parity is enforced, not aspirational.

### 14.4 Cross-cutting passes

Snapshot/visual checks cover the shell, transcript, the six chart types, and the settings form at representative window widths. Accessibility checks cover keyboard navigation, focus order, and contrast in both themes. A performance pass covers a long transcript (thousands of messages), a large usage table, and rapid streaming without UI-thread stalls — verifying the §4 async contract holds and background completion marshals to the dispatcher. A packaging smoke test installs and launches each platform artifact and confirms single-instance behavior.

### 14.5 Delivered so far

The logic layer already ships with Touchstone suites in `Test.Shared`, running through all three runners: `DesktopLocalizationSuite` (catalog resolution, missing-key fallback, locale switch + event, unknown-locale rejection, RTL metadata), `DesktopFormattersSuite` (culture-aware number/byte/duration/relative-time/list formatting + null-culture guard), `ThreadServiceSuite` (create/list/rename/duplicate/delete/export over a temp store, plus missing-thread and bad-format negatives), `UsageWindowSuite` (the range→bucket mapping, grid snapping, unknown-range guard), `UsageAnalyticsSuite` (an integration test that records events through a real SQLite telemetry store and verifies summary/events/endpoint-filter through the service), `TurnProjectionSuite` (text accumulation, tool lifecycle, error/completion, cancellation, null guard), `ConversationServiceSuite` (completed turn appends user+assistant and raises events, blank-prompt rejection, cancelled turn, resume-from-history), and `WorkspaceSuite` (the tabbed attention model — background unread, error, the approval > error > unread > running priority, focus clearing, and tab open/focus/dedup/close). All pass; the full repository suite is green (0 failures).

### 14.6 CI gates

The GitHub Actions workflow restores and builds the solution across `net8.0` and `net10.0`, runs `Test.Automated` (with `--results`), `Test.Xunit`, and `Test.Nunit`, runs the headless UI project, and enforces the i18n missing-key/hard-coded-string checks, the parity guard, and `dotnet format`. Exit is non-zero on any failure; results are uploaded as an artifact.

---

## 15. Delivery roadmap

Phases are ordered so a usable chat client exists early and parity fills in behind it. Each phase lists primary engine calls and exit criteria.

**Phase 0 — Foundations. _(delivered)_** Scaffold `src/Mux.Desktop` (fully-populated csproj, Avalonia 12.1.2, `CommunityToolkit.Mvvm`, DI), add to `Mux.sln`, single-instance lock (`DesktopInstanceLock`), the shell window (sidebar/header/workspace), a startup **splash**, an **About/Help** window, and the i18n scaffold (`ILocalizationService` + 12-locale registry + `en` catalog, RTL `FlowDirection`). Alongside: bump the product to **0.10.0** across `Mux.Core`/`Mux.Cli`/`Mux.Agent`/`Mux.Desktop`/`Defaults`, prep `Mux.Core`+`Mux.Search` as NuGet packages with symbols, and add `run-desktop.bat`/`run-desktop.sh`. *Exit (met):* builds clean on `net8.0` and `net10.0`; splash → shell renders; About/Help opens; `Mux.Core` packs to `.nupkg`+`.snupkg`. *Remaining for Phase 0 polish:* theme tokens/high-contrast, locale-aware formatters, and the header language selector wired to `SetActiveLocale`. *Calls:* `SettingsLoader.GetConfigDirectory`, `UsageTelemetry.Create`.

**Phase 0.5 — Core extraction and TUI refactor (§4), lowest-risk-first.** This phase does the refactor work on the existing TUI, not just green-field desktop code. Move-verbatim the agnostic collaborators + `RuntimeResolver`; extract `TurnProjection`/`ITurnObserver` and `ConversationStatsAggregator`; introduce `ToolRuntimeBinder`; extract `ConversationController`. In the same phase, rewire `Mux.Cli`/`MuxTuiApp` to consume each new Core type as it lands, keeping the interactive shell behavior-identical, and add `Test.Shared` suites (positive + negative). The desktop app's `IConversationService` binds to the same shared `ConversationController`. *Exit:* the TUI is refactored onto shared Core code with no behavioral change (existing suites green), the new Core suites pass, and the desktop seam is ready to build on. *Steps 1–3 can begin in parallel with Phases 1–2; the `ConversationController` extraction (step 4) precedes the desktop's Phase 4 concurrency work.*

> **Progress (2026-09-12):** Steps 1–2 landed. The shared **`TurnProjection`** (single accumulator: the desktop's rich state — assistant/thinking text, tool-call lifecycle, error/completion — *plus* the TUI's streaming signals `FirstTokenReceived`/`ModelResponded`/`ModelWorking` with heartbeat re-arm) and **`ITurnObserver`** now live in `Mux.Core.Conversation`; the desktop's projection was promoted into it (with `ToolCallRecord`/`ToolCallStatus`), and the TUI `AgentEventProjector` was refactored to implement `ITurnObserver` and delegate all accumulation/signalling to it while keeping its pane rendering. **`ConversationStats`** moved to Core and **`ConversationStatsAggregator`** now owns the per-turn timing/token math (the TUI aggregates through it). Behavior-preserving on both front ends; `TurnProjection` + `ConversationStats` suites merged/added, 714→717 tests green, both TFMs build.
>
> **Also consolidated (2026-09-12, the §4.1 move-verbatim band):** the remaining pure, duplicated (or CLI-only-but-shared-shaped) logic was promoted into `Mux.Core`: **`Mux.Core.Prompting.PromptHistory`** (merges the TUI + desktop recall buffers into one superset — capacity, draft preservation, `Snapshot`/`Restore` for the TUI snapshot and `Load`/`Entries` for the desktop store), **`Mux.Core.Conversation.ThinkingPhrases`** (merges the TUI's `ThinkingMessages` + the desktop's `ThinkingQuips`; the `thinking-messages.txt` resource now lives in Mux.Core), **`Mux.Core.Tools.ToolFailureReason`** (was TUI-only; desktop tool cards can now share it), **`Mux.Core.Prompting.PromptText.Preview`** (the TUI's `PromptPreview` ≈ the desktop's `CheckpointLabel`), and the two tool-runtime wrappers **`Mux.Core.Tools.McpRuntime`** + **`Mux.Core.Skills.SkillRuntime`** (verbatim; groundwork for `ToolRuntimeBinder` and desktop MCP/skill parity). All behavior-preserving, both TFMs green.
>
> **Done since (2026-09-12):** the **endpoint→system-prompt resolver** was extracted to **`Mux.Core.Prompting.SystemPromptResolver`** — `CommandRuntimeResolver.ResolveRuntime` delegates to it (behavior-preserving), and the desktop `AgentLoopTurnRunner` now uses it, closing its prompt-profile/tool-description parity gaps (it previously left `{ToolDescriptions}` empty, leaked a literal `{TaskPlanningGuidance}`, never used the tools-disabled variant, and used the wrong max-iterations). **Step 3 `ToolRuntimeBinder`** landed: `ExternalToolsBinder` + `McpTemplateBinder` moved to `Mux.Core.Tools`, and the new **`Mux.Core.Tools.ToolRuntimeBinder`** absorbed the `Program.cs ApplyTemplate` closure/`promptSync` (its `Rebind()`/`SetProfilePrompt` now drive the template; `McpRuntime`/`SkillRuntime` take `binder.Rebind` as their change callback).
>
> **Remaining:** only the **`ConversationController`** (step 4) with its in-place TUI turn-loop adoption — it rewires the shipping interactive control flow, so it lands with an interactive smoke-test rather than blind.

**Phase 1 — Core chat. _(logic delivered; view pending)_** The `Mux.Desktop.Core` logic layer is in place and tested: `ConversationService` + `TurnProjection` + `ITurnRunner` seam (turn lifecycle, event accumulation, cancellation), `ThreadService` (thread CRUD/export), `UsageAnalyticsService` (+ the dashboard-matching `UsageWindow` mapping), locale-aware `LocaleFormatters`, and the i18n service — all covered by passing Touchstone suites in `Test.Shared`. *Remaining:* the Avalonia views (composer, streaming Markdown/code bubbles, thinking panel, tool cards) and the real `AgentLoopTurnRunner` that builds `AgentLoopOptions` and drives `AgentLoop` (wired in tandem with Phase 0.5's `ConversationController`). *Exit:* a real turn streams and renders.

**Phase 2 — Tools and approvals.** Tool cards, the approval modal on `PromptUserFunc`, approval policy, sandbox + allow/deny + add-dir, write-lease indicator. *Exit:* a file-mutating turn prompts and shows results. *Calls:* `ApprovalRouter`, `ToolGovernance`, `WriteLease`.

**Phase 3 — Conversations/threads.** Sidebar list, new/rename/pin/resume/duplicate/export/delete/search, autosave, dirty state, auto-title. *Exit:* many threads; resume replays history. *Calls:* `SessionStore`, `SessionResumeService`, `SessionExporter`, `SessionTitleHelper`.

**Phase 4 — Tabbed workspace, concurrency, and queue.** The tabbed workspace (§7): open threads as parallel workload tabs, per-tab status/attention indicators (awaiting-approval, unread, running, error), optional focus-aware OS notifications, plus the queue strip + editor and the enqueue chooser. *Exit:* two tabs stream at once; a background tab shows unread/approval state and clears on focus; queued prompts run in order.

**Phase 5 — Tasks.** Live task checklist, Tasks panel, sidebar `TASKS n/m`, persistence. *Exit:* a multi-step turn renders and persists its plan.

**Phase 6 — Model & prompt management.** Endpoint switcher + manager, Add/Edit wizard, Ollama import, effort, thinking, validate, prompt profiles. *Exit:* full endpoint lifecycle without editing JSON. *Calls:* `RuntimeResolver`, `OllamaModelLister`, `LlmClient.LoadModelAsync`.

**Phase 7 — MCP, Skills, Subagents, Plugins.** Four managers + custom-command palette integration. *Exit:* MCP live status/reconnect; skills CRUD + editor; subagents CRUD; hooks + custom commands. *Calls:* `McpRuntime`, `SkillRuntime`, `SubagentRegistry`, `Mux.Core.Plugins`.

**Phase 8 — Settings & keybindings.** Form-based settings with apply-timing hints; keybindings editor. *Exit:* every `MuxSettings` field editable and validated; rebinding works.

**Phase 9 — Monitoring & reporting (§8).** Home/overview, Usage analytics (all six charts, per-tab KPI strip, filters incl. call-kind/success, group-by, events table with detail + delete), Pricing editor, Server Info. *Exit:* analytics reflect shared-DB activity; charts match the dashboard. *Calls:* `UsageQueryService`, `PricingTable`, `OverviewRoutes` data.

**Phase 10 — Undo/redo, export polish, command palette.** Git-checkpoint undo/redo, palette (Ctrl/Cmd+K), prompt history. *Exit:* undo/redo in a git tree; palette reaches every command.

**Phase 11 — i18n rollout & accessibility.** All twelve catalogs, RTL/CJK/expansion QA, keyboard nav, focus order, contrast, responsive widths. *Exit:* i18n Definition of Done met; a11y pass green.

**Phase 12 — Packaging & distribution.** Per-OS bundles, single-instance, signing/notarization, optional auto-update. *Exit:* installable artifacts for all three OSes.

**Phase 13 — Docs, tests, CI gates.** README/CHANGELOG, `docs/DESKTOP.md`, the full test suite and CI gates (§14). *Exit:* standards conformance verified.

---

## 16. Packaging and distribution

- **Windows** — self-contained build, MSIX or signed installer, single-instance lock reusing the `AgentInstanceLock` pattern.
- **macOS** — signed, notarized `.app` in a `.dmg`; hardened runtime.
- **Linux** — AppImage plus a `.tar.gz`; a `.deb` where useful.
- **Updates** — Velopack recommended for a uniform cross-platform channel.
- Icons reuse `assets/icon-*.png`; the app respects `MUX_CONFIG_DIR`.

No Docker, REST, or MCP server artifacts apply to a desktop binary, so the corresponding repository requirements (healthchecks, compose, `REST_API.md`, Postman, `MCP_API.md`) are not triggered by this project. The optional embedded server (§12) is the existing `Mux.Server`, already documented in `docs/REST_API.md`.

### Mux.Core as a reusable NuGet package

The desktop app is one consumer of the engine; the engine itself is published so anyone can build their own experience on top of it. `Mux.Core` (and its `Mux.Search` dependency) carry full package metadata — MIT license expression, project and repository URLs, a concise package README (`PACKAGE.md`, rendered on nuget.org), the mux icon, and discovery tags — and produce a **symbol package (`.snupkg`)** alongside the main `.nupkg`. Shared repository/SourceLink metadata lives in `src/Directory.Build.props` (`PublishRepositoryUrl`, `EmbedUntrackedSources`); the .NET 8+ SDK's built-in SourceLink stamps the repository commit into the nuspec so the symbols map back to GitHub sources — no extra package reference, and no build-time advisory noise. A consumer references `Mux.Core`, resolves `AgentLoop`, `SessionStore`, `McpToolManager`, `UsageQueryService`, and `SettingsLoader`, and drives the engine exactly as the desktop app does. `dotnet pack src/Mux.Core/Mux.Core.csproj -c Release` produces `Mux.Core.0.10.0.nupkg` + `.snupkg` with `lib/net8.0` and `lib/net10.0` assemblies, the README, and the icon embedded.

---

## 17. Repository-requirements compliance

- Source under `src/Mux.Desktop`, added to `Mux.sln` (`REPOSITORY_REQUIREMENTS.md`).
- `CHANGELOG.md` entry and a README update listing the desktop front end alongside the TUI and dashboard.
- A `docs/DESKTOP.md` user guide (install, first run, threads, managers, monitoring, i18n, packaging), kept in sync.
- `LICENSE.md` (MIT) inherited.
- Code conforms to `CODE_STYLE.md`; UI to the applicable parts of `DASHBOARD_STYLE_AND_USABILITY.md`; localization to `I18N.md`; tests to `BACKEND_TEST_ARCHITECTURE.md` (Touchstone).

---

## 18. Definition of done

The desktop app is complete when the parity matrix (§6) is fully covered with no TUI-only capability; the dashboard's monitoring, charts, KPIs, and reporting (§8) are reproduced natively; threads support create/rename/pin/resume/duplicate/export/delete/search; the shared orchestration (§4) is centralized in `Mux.Core` and consumed by both front ends; the i18n Definition of Done holds across all twelve locales including RTL and CJK; the test suite (§14) passes with balanced positive and negative coverage and the parity guard is green; and signed installable artifacts exist for Windows, macOS, and Linux. The measure is simple: a mux user who lives in the TUI can move to the desktop app and give up nothing — then gains a real conversation list, native analytics, localization, and a chat surface that looks like it belongs on the desktop.
