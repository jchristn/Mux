# Mux → TUIKit Front-End Migration Design

**Status:** Draft design (**alpha**). Mux is alpha, single-user. This document assumes we rebuild
the Mux front-end on top of [TUIKit](../TUIKit) as a display/control library and are free to
delete legacy code rather than preserve it. There is no strangler/parity-gating requirement —
we do it right the first time. Target: branch **`feature/v0.3.0`**, shipped as **`0.3.0-alpha`**.

**What this is not:** a merge of the two codebases. `Mux.Core` (the engine) stays a UI-free
library. TUIKit is consumed as a NuGet dependency by a rebuilt `Mux.Cli` presentation layer.

**TUIKit availability:** TUIKit **v0.2.0 is published to NuGet** (`PackageId: TUIKit`). `Mux.Cli`
consumes it as a normal package reference — no project reference, no submodule. Because TUIKit is
itself alpha ("API subject to change; pin your version"), **pin the exact version**
(`Version="0.2.0"`) rather than a floating range, and treat any TUIKit upgrade as a deliberate,
tested step. TUIKit multi-targets `netstandard2.0;net8.0;net10.0`, so it satisfies Mux's
`net8.0;net10.0` targets directly.

**TUIKit source, for reference only:** the TUIKit source tree lives locally at `C:\Code\TUIKit`.
It is **not** added to `Mux.sln` and **not** referenced as a project — always consume the NuGet
package. The local source is there purely as a reference and escape hatch: if Mux development
surfaces a TUIKit bug or a missing capability, read/patch it in `C:\Code\TUIKit`, cut a new TUIKit
package version, and bump Mux's pinned `TUIKit` reference — never wire the two solutions together.
This keeps the boundary honest (Mux exercises TUIKit exactly as any external consumer would) while
making TUIKit fixes cheap to turn around.

---

## 1. Decisions locked (from design review)

| Area | Decision |
|---|---|
| **Base layout** | Persistent right sidebar: transcript left, always-on info column right |
| **Queue UI** | Sidebar glance **+** footer counter that opens a full queue-management modal |
| **Tool-call display** | Inline in the transcript, updating in place (`running…` → `done 0.3s`) |
| **Concurrency model** | **Parallel background jobs** (multiple runs execute concurrently) |
| **Job viewing** | Focused single transcript + switcher (select a job in the sidebar to view it) |
| **Composer** | Multi-line editor, always (undo/redo, kill-ring); Enter submits, Shift+Enter newline |
| **Command surfaces** | All four: slash commands, command palette (Ctrl+K), menu bar, function-key footer |
| **Approvals** | Policy-driven per job, with a modal fallback for sensitive actions |
| **Modals** | Model picker/manager, Settings, MCP + endpoint manager, Queue/job manager, Help |
| **Model UX** | Searchable picker + management (set active, pull, remove, bind to endpoint) |
| **Settings UX** | Grouped form modal, applied live |
| **Job isolation** | Parallel reads; a single **write lease** — only one job may mutate the workspace at a time |
| **Menu bar** | Session/Jobs · Model/Endpoints · View/Appearance · Tools/Help |
| **Enqueue gesture** | Ask per submit — run-now (parallel) vs queue-after; modifier keys bypass the prompt |
| **Persistence** | Persist & resume full sessions; a session/history browser |
| **Approach** | Build it right; remove legacy freely |

---

## 2. Architecture

### 2.1 The boundary that already exists (and stays)

Mux is already cleanly split at the `AgentEvent` async stream:

- **`Mux.Core`** — UI-free engine: `AgentLoop.RunAsync(...)` yields a typed
  `IAsyncEnumerable<AgentEvent>` (`AssistantTextEvent`, `ToolCallProposedEvent`,
  `ToolCallCompletedEvent`, `ContextStatusEvent`, `HeartbeatEvent`, `RunStartedEvent`,
  `RunCompletedEvent`, `ContextCompactedEvent`, `ErrorEvent`). Tools, LLM adapters, context
  management, settings all live here. **This stream is the contract; it does not change shape.**
- **`Mux.Cli`** — presentation. Today this is a 5,411-line `InteractiveCommand` god class that
  hand-rolls an ANSI renderer on `System.Console`. **This is what we delete and rebuild on TUIKit.**

### 2.2 What gets deleted

Rip out, don't port:

- `InteractiveChromeLayout`, the manual cursor bookkeeping (`_ChromeTop`, `_OutputCursorTop`,
  `MaterializePromptScrollIfNeeded`, clear-region math) — TUIKit's diff renderer owns this.
- The bespoke `LineBuffer` editor and `RenderInteractiveChrome` repaint glue — replaced by
  TUIKit `TextEditor` + regions.
- The poll-based `System.Console` input loop, `TryReadPendingKeyBatch`,
  `InteractivePasteHeuristics` — replaced by TUIKit's `InputParser` (bracketed paste, CSI-u,
  SGR mouse) and `CommandRouter`.
- The singleton run model: `ActiveRunState? _ActiveRun`, single `Channel<AgentEvent>`,
  single `_CurrentCts` — replaced by the `JobManager` (§3).
- `EventRenderer`'s interactive branch (the non-interactive `mux print` path can keep a thin
  line-mode renderer; TUIKit degrades to line mode when stdout is not a TTY anyway).

### 2.3 New project shape

```
Mux.Core                    (unchanged engine + NEW job/session infra, still UI-free)
  Agent/                    AgentLoop, events, context mgmt  (unchanged contract)
  Jobs/         <-- NEW     JobManager, Job, JobState, WriteLease, JobScheduler
  Sessions/     <-- NEW     SessionStore, SessionSnapshot, SessionResumeService
Mux.Cli                     (rebuilt on TUIKit)
  App/                      MuxTuiApp (owns TuiApplication, layout, wiring)
  Regions/                  Transcript, Sidebar, Composer, Footer, MenuBar hosts
  Modals/                   ModelModal, SettingsModal, McpEndpointModal, JobsModal,
                            ApprovalModal, HelpModal, SessionBrowserModal
  Commands/                 Command catalog (palette + slash + menu + fkey share this)
  Rendering/                AgentEventProjector (AgentEvent -> Pane writes), theme
```

The key idea: **jobs and sessions are engine concerns** (they own agent runs, cancellation,
the write lease, persistence) and belong in `Mux.Core` so they stay testable without a UI.
`Mux.Cli` only *projects* them onto TUIKit widgets.

`Mux.Cli.csproj` package references change like so:

```xml
<ItemGroup>
  <PackageReference Include="TUIKit" Version="0.2.0" />
</ItemGroup>
```

> `Spectre.Console` and `Spectre.Console.Cli` are both removed. The transitional line-mode renderer
> uses TUIKit-backed mux shims, and top-level command dispatch uses mux's local parser until/unless
> a broader command-host migration is justified.

---

## 3. The job model (the real new infrastructure)

This is the heart of the change and the part TUIKit does **not** give us — TUIKit makes
concurrent work *displayable* (thread-safe panes, in-place `UpdateLine`); we still have to
*orchestrate* it.

### 3.1 `Job`

```
Job
  Id            JobId (short, e.g. "j3")
  SessionId     which session it belongs to
  Title         short label (model-generated or first line of prompt)
  Prompt        the submitted prompt (+ any follow-ups queued to this job)
  State         Queued | Running | AwaitingApproval | AwaitingWriteLease
                | Paused | Completed | Failed | Cancelled
  Events        Channel<AgentEvent>          (per-job, was singleton)
  Cts           CancellationTokenSource      (per-job, was singleton)
  Policy        ApprovalPolicy               (per-job, defaults from settings)
  History       ConversationMessage[]        (the job's slice of session history)
  StartedAt / EndedAt / TokenUsage / LastContextStatus
```

Each job wraps exactly one `AgentLoop.RunAsync(...)` invocation, exactly as today's single
run does — we just have N of them.

### 3.2 `JobManager` (replaces the `_ActiveRun` singleton)

Responsibilities:

- Owns the live set of jobs and a `MaxConcurrency` setting (default e.g. 3; 1 = today's behavior).
- **Scheduler:** when a slot frees, promotes the next `Queued` job to `Running`.
- Hands each job its own `Channel<AgentEvent>`; `Mux.Cli` subscribes per job and writes into
  that job's TUIKit `Pane`.
- Owns the **single write lease** (§3.3).
- Owns per-job cancellation, pause/resume, and reorder for the queue.
- Emits `JobManagerEvent`s (job added/started/state-changed/finished) so the sidebar and the
  jobs modal update reactively via TUIKit `Observable<T>`.

The main REPL no longer "drains one channel on the UI thread." Instead each job streams on its
own worker `Task`; TUIKit's thread-safe `Pane.Write`/`UpdateLine` means those workers write
directly to their pane and the 60 FPS diff renderer coalesces. Only the **focused** job's pane
is bound to the transcript region; background jobs write to unbound panes that are swapped in
when focused.

### 3.3 Write lease (job isolation = "parallel reads, single writer")

A `WriteLease` is a fair async mutex (`SemaphoreSlim(1,1)` + FIFO wait queue) living in the
`JobManager`, scoped to the workspace (working directory / session).

- Read-only / analysis tools (`Read`, `Glob`, `Grep`, `WebRetrieve`, `WebSearch`) run freely
  in parallel — no lease.
- Mutating tools (`Write`, `Edit`, `RunProcess`, anything that can change files/state) must
  `await lease.AcquireAsync(jobId, ct)` before executing and release after.
- A job waiting on the lease enters `AwaitingWriteLease`; the sidebar shows a lock glyph and
  the footer/jobs-modal shows who holds it and who's waiting.

This is enforced inside `Mux.Core` tool execution (the `foreach ExecuteToolCallAsync` loop in
`AgentLoop` gains a lease dependency), **not** in the UI — so it holds regardless of front-end
and is unit-testable headless.

> Tool classification (read vs. mutate) is added to the tool metadata in `BuiltInToolRegistry`
> so the lease requirement is declarative, not hard-coded per tool.

### 3.4 Enqueue gesture ("ask per submit")

When the composer submits and ≥1 job is already `Running`:

- Default: a compact **submit chooser** appears (small modal or footer prompt):
  - **Run now** → spawn a new parallel job immediately (subject to `MaxConcurrency`; overflow → `Queued`).
  - **Queue after current** → append to the queue (FIFO).
  - **Add to focused job** → treat as a follow-up turn appended to the focused job's conversation.
  - `[ ] Remember my choice for this session`.
- Modifier bypass for power users (no prompt): `Enter` = default choice, `Alt+Enter` = run-now
  parallel, `Ctrl+Enter` = queue-after. (Shift+Enter stays "insert newline" in the editor.)
- If no job is running, submit just starts a job immediately (no chooser).

---

## 4. Screen layout & regions

TUIKit layout is a set of named `Region`s with axis constraints. Target composition:

```
┌ menu ─────────────────────────────────────────────────────────┐  row 0  (MenuBar)
├───────────────────────────────────┬───────────────────────────┤
│ transcript                        │ sidebar                   │
│  (focused job's Pane)             │  CONTEXT  ▓▓▓▓░░ 42%       │
│  assistant text …                 │  MODEL    qwen2.5         │
│  ⏵ read foo.cs         done 0.3s  │  SESSION  refactor-api    │
│  ⏵ edit bar.cs         running…   │  CWD      ~/code/mux      │
│                                   │  ── JOBS ──               │
│                                   │  ▶ j1 refactor    run     │
│                                   │  ⏸ j2 tests       lease?  │
│                                   │  … j3 write-docs  queued  │
├───────────────────────────────────┤                           │
│ composer (multi-line TextEditor)  │  (selecting a job here    │
│ mux> _                            │   focuses its transcript) │
├───────────────────────────────────┴───────────────────────────┤
│ ctx 42% · jobs 3 (1 run) · lease:j1 · esc cancel   [F-key hints]│  footer
└────────────────────────────────────────────────────────────────┘
```

### 4.1 Region spec (TUIKit `Layout.Create()`)

| Region | Horizontal | Vertical | Notes |
|---|---|---|---|
| `menu` | `FillWidth` | `TopAnchored(0,1)` | 1 row, `MenuBar` |
| `sidebar` | `RightAnchored(0, 34)` | fill between menu & footer | ~34 cols; collapsible (View menu / hotkey) |
| `transcript` | `FillWidth(0, 34)` | fill between menu & composer | left column; hosts focused job's `Pane` |
| `composer` | `FillWidth(0, 34)` | `BottomAnchored(above footer, 5)` | left column; bordered `TextEditor` |
| `footer` | `FillWidth` | `BottomAnchored(0, 1)` | status + f-key hint strip |

- When the sidebar is collapsed, `transcript`/`composer` reclaim the full width (rebind layout).
- If the terminal is too narrow for the sidebar minimum, TUIKit's `LayoutBlockScreen` / a
  responsive rule auto-collapses the sidebar rather than showing "terminal too small."

### 4.2 Sidebar contents (always-on ambient info — serves Goal 1)

Top-to-bottom, each a small widget bound to an `Observable<T>`:

1. **Context** — a `Gauge`/`ProgressBar` of context-window usage (%), driven by
   `ContextStatusEvent` for the focused job; turns amber/red near the budget; shows compaction
   count.
2. **Model** — active model + endpoint name (click/Enter → model modal).
3. **Session** — title + short id (click → session browser).
4. **Cwd** — working directory (+ git branch if available).
5. **Jobs list** — one row per job: state glyph (`▶` run, `…` queued, `⏸` paused/awaiting,
   `✓` done, `✗` failed, `🔒` holds write lease, `⧗` awaiting lease), id, title, live token/elapsed.
   This is the **glance**; it's also focusable — arrow to a job, Enter focuses its transcript.

### 4.3 Transcript region

- Hosts the **focused** job's `Pane` (TUIKit `Pane`: thread-safe, ring-buffered scrollback,
  smart scroll-lock with an "N new" indicator when scrolled up).
- Tool calls render inline and update in place via `Pane.UpdateLine` (Goal: `running…` →
  `done 0.3s`); optional expand for full args/output/diff later.
- Switching focus (sidebar select or `Ctrl+J` cycle / `Alt+<n>`) swaps which `Pane` is bound.
  Background jobs keep streaming into their own panes; nothing is lost.
- Markdown via TUIKit `MarkdownRenderer`; diffs from `Edit` tools via `DiffView` +
  `SyntaxHighlighter`.

### 4.4 Composer region

- TUIKit `TextEditor` (multi-line, undo/redo, kill-ring), bordered, autogrowing up to a cap
  then internal scroll.
- **Enter** submits (→ §3.4 enqueue gesture). **Shift+Enter** newline. **Alt/Ctrl+Enter**
  bypass modifiers.
- **Up/Down at buffer edges** recall prompt history (port the intent of `PromptHistory`,
  persisted per session now).
- Leading `/` routes to slash-command handling instead of a prompt (§7).

### 4.5 Footer

- Left: live status — `ctx 42% · jobs 3 (1 run) · lease:j1 · <focused job state>`.
- Right: context-sensitive f-key hint strip (`F1 Help  F2 Model  F3 Jobs  F4 Settings  ^K Palette`).
- Ephemeral messages (errors, "copied", "queued j4") surface as TUIKit `NotificationCenter`
  toasts rather than stealing focus.

---

## 5. Menu bar

Full menu tree (TUIKit `MenuBar`/`Menu`/`MenuItem`; every item also exists as a palette command
and, where sensible, a slash command — one command catalog, four surfaces):

**Session / Jobs**
- New session · Save session · Resume session… (→ session browser)
- Rename/Title… · Compact context · Clear transcript
- ── Jobs ──
- New background job… · View queue (jobs modal) · Pause focused · Resume focused
- Cancel focused · Cancel all · Reorder queue…

**Model / Endpoints**
- Pick model… (model modal) · Pull/download model… · Manage models…
- ── Endpoints ──
- Switch endpoint… · Add/edit endpoint… · Test connection
- ── MCP ──
- Manage MCP servers… · Inspect tools…

**View / Appearance**
- Toggle sidebar · Focus next job · Focus previous job · Job switcher…
- Theme… · Density (compact/comfortable) · Toggle tool-detail expansion

**Tools / Help**
- Approval policy… (Ask / Auto-safe / Deny) · Enable/disable tools… · Permissions…
- ── Help ──
- Keybindings… · Documentation · About Mux

---

## 6. Command palette (Ctrl+K)

A TUIKit `FuzzyList` over the **single command catalog**. Each command:
`{ id, title, category, keybinding?, slashAlias?, run(context) }`. The palette, menu bar,
function keys, and slash parser all resolve against this catalog so there's one source of truth.
Categories mirror the menus (Session, Jobs, Model, Endpoints, MCP, View, Tools, Help). Palette
shows the bound key on the right so it doubles as discoverability for the keymap.

---

## 7. Slash commands (retained, mapped to the catalog)

Typed into the composer; preserved for muscle memory. Each maps to a catalog command:

| Slash | Action |
|---|---|
| `/model [name]` | Open model modal, or set directly if name given |
| `/endpoint [name]` | Switch/open endpoint manager |
| `/mcp` | MCP manager modal |
| `/status` | Focused job/context status (also always visible in sidebar/footer) |
| `/compact` | Compact context for focused job |
| `/title [text]` | Set/pin session title |
| `/new` | New session |
| `/clear` | Clear transcript |
| `/jobs` | Jobs/queue modal |
| `/queue <prompt>` | Submit a prompt straight to the queue (skip chooser) |
| `/bg <prompt>` | Submit as a new parallel background job (skip chooser) |
| `/resume` | Session browser |
| `/settings` | Settings modal |
| `/theme [name]` | Theme picker / set |
| `/approve <policy>` | Set approval policy for focused job |
| `/help` | Help/keybindings modal |

---

## 8. Modals

All via TUIKit's `ModalStack` (focus-trapping, awaitable results). Multiple pending modals stack;
approval requests from several jobs queue and present one at a time.

### 8.1 Model picker & manager
- `FuzzyList` of models with a detail pane (context length, params, family, size on disk).
- Actions: **Set active** (Enter), **Pull/download** (with a `ProgressBar`/`Spinner` driven by
  the backend), **Remove**, **Bind to endpoint** (pick which endpoint this model runs on).
- Reflects per-endpoint model availability (Ollama vs OpenAI vs vLLM) from the existing adapters.

### 8.2 Settings (grouped form, live apply)
- TUIKit `Form` with category tabs/sections: **General** (paste, prompt history depth, autosave),
  **Model** (default model, temperature, max iterations), **Tools** (enable/disable per tool,
  permissions), **Approvals** (default policy, per-tool auto-approve list), **Jobs**
  (max concurrency, default enqueue behavior, write-lease timeout), **Appearance**
  (theme, density, sidebar default).
- Fields are toggles/selects/text; changes apply immediately and write through to `MuxSettings` /
  the settings file.

### 8.3 MCP + endpoint manager
- Two panes/tabs. **Endpoints:** list, add/edit (name, base URL, auth, backend type), test
  connection (spinner + result), set active. **MCP servers:** list configured servers, add/remove,
  inspect exposed tools (`DataTable` of tool name/description/enabled), enable/disable per tool.

### 8.4 Queue / job manager
- `DataTable<Job>` — columns: id, title, state, model, elapsed, tokens, lease.
- Row actions: focus, pause/resume, cancel, reorder (move up/down in queue), retry (failed),
  open transcript, view diff (if the job mutated files → `DiffView`).
- Header shows `running X / max N`, lease holder, and total queued.

### 8.5 Approval modal (policy fallback)
- Only shown when a job's policy escalates a specific tool call. Shows tool + args summary
  (+ diff for edits). Options: **Yes** / **No** / **Always for this session** / **Always for this tool**.
- Because approvals are per job and can arrive concurrently, they queue in the `ModalStack`; the
  title shows which job (`j2` is asking).

### 8.6 Help / keybindings
- Read-only reference grouped by category; generated from the command catalog so it never drifts
  from the actual bindings.

### 8.7 Session browser (resume)
- List of persisted sessions (title, last-active, message count, model, whether it had queued
  work). Enter resumes; actions: delete, duplicate, export transcript.

---

## 9. Input & keybindings

Driven by TUIKit `CommandRouter` + `KeyChord` (supports multi-key chords, scopes, conflict policy).

| Key | Action |
|---|---|
| `Enter` | Submit (enqueue gesture) |
| `Shift+Enter` | Newline in composer |
| `Alt+Enter` / `Ctrl+Enter` | Run-now parallel / queue-after (bypass chooser) |
| `Esc` | Cancel focused job (double-press or held → interrupt; configurable via `CtrlCPolicy`) |
| `Ctrl+C` | `CtrlCPolicy` = double-tap-to-exit (don't kill on first press mid-run) |
| `Ctrl+K` | Command palette |
| `Ctrl+J` / `Alt+<n>` | Cycle focus / jump to job n |
| `Ctrl+B` | Toggle sidebar |
| `Ctrl+↑/↓` (in transcript) | Scroll; releasing at bottom re-attaches auto-scroll |
| `Up/Down` (composer edges) | Prompt history |
| `F1..F4` | Help / Model / Jobs / Settings |
| `F12` | Toggle mouse capture (hand mouse back to terminal for native select) |

Mouse (from TUIKit): click-to-focus panes/jobs, wheel scroll, clickable links in transcript.

---

## 10. Approvals & tool safety (consolidated)

Two independent mechanisms, both enforced in `Mux.Core`:

1. **Approval policy** (per job, default from settings): `Ask` → modal; `Auto-safe` →
   auto-approve read/idempotent tools, prompt only for sensitive ones; `Deny` → block. Replaces
   the current single `_ApprovalPolicy` field. Approval hand-off keeps today's
   `TaskCompletionSource` pattern but is now per-job and routed to the `ModalStack`.
2. **Write lease** (§3.3): orthogonal to approval — even an auto-approved `Edit` must acquire the
   lease. Prevents two parallel jobs corrupting the workspace. This is what makes "parallel reads,
   single writer" real.

---

## 11. Persistence & sessions

Today a session is one process lifetime, in memory only. New model:

- **`SessionStore`** (`Mux.Core/Sessions`): one JSON file per session under a sessions dir
  (e.g. `~/.mux/sessions/<id>.json`), holding: title (+ pinned flag), created/updated, active
  endpoint + model snapshot, settings snapshot, full `ConversationMessage[]` history, compaction
  count, and **queue state** (queued prompts + job metadata).
- **Autosave** on each turn boundary (append/replace), plus explicit "Save session."
- **Resume:** on launch, offer resume (last session or the browser). Running jobs at exit are
  persisted as `Interrupted`; on resume they're presented as re-runnable (we don't silently
  restart mutating work).
- **History browser** (§8.7) lists and manages sessions.
- Prompt history persists per session (replaces the in-memory `PromptHistory`).

Keep it simple and forward-compatible: a versioned JSON schema, tolerant of unknown fields.

---

## 12. Rendering & concurrency notes

- TUIKit does **double-buffered diff rendering** at a fixed cadence (default 60 FPS): a 100 Hz
  token stream from the LLM becomes at most 60 diffed frames — strictly better than today's
  repaint-on-every-change and with no manual cursor math.
- **Thread-safe panes** mean each job's worker `Task` writes to its pane directly; no UI-thread
  channel draining, no `_ConsoleSync` lock discipline in our code.
- `UpdateLine`/`PaneLineHandle` give in-place tool-status updates and progress bars without
  redrawing the transcript.
- Headless testability: TUIKit exposes `PumpInputOnce`/`RenderOnce` + `HeadlessBackend`, so the
  whole UI becomes snapshot-testable — something today's Mux UI can't do. The `JobManager` and
  `WriteLease` are plain engine code and unit-testable directly.

---

## 13. Appearance

- Adopt a Mux theme (TUIKit theming; truecolor with graceful 256/16 quantization).
- Density setting (compact/comfortable) affects padding and whether tool calls show inline detail.
- Sidebar default-visible, collapsible; footer hint strip toggleable.

---

## 14. Resolved implementation decisions

These decisions are owner-approved and should be treated as build inputs, not open design space.

1. **Max concurrency default** — `MaxConcurrency` defaults to `3`; `1` remains supported as the
   explicit single-job compatibility setting.
2. **Follow-ups vs. new jobs** — when the user submits mid-run and chooses "add to focused job,"
   append that follow-up after the current turn completes. A separate explicit "interrupt and
   redirect" action may be added later.
3. **Session history for new jobs** — a new job forks a snapshot of the focused job's history at
   spawn time.
4. **Completed background job history** — persist every job transcript. Completed background jobs do
   not merge into the focused/current history automatically; merge is an explicit user action.
5. **Default context for a new prompt** — new prompts use the focused job's history unless the user
   explicitly chooses another context in the enqueue/focus flow.
6. **Write-lease granularity** — use one write lease per workspace for v0.3.0; revisit per-path or
   per-file leases only if the workspace lease becomes a real bottleneck.
7. **Built-in tool classification** — `read_file`, `glob`, `grep`, `web_retrieve`, `web_search`,
   `file_metadata`, and `list_directory` are `ReadOnly`; `write_file`, `edit_file`, `multi_edit`,
   `delete_file`, `manage_directory`, and `run_process` are `Mutating`.
8. **Unknown/external tool classification** — MCP and other external tools default to `Mutating`
   unless explicit metadata marks them `ReadOnly`.
9. **Interrupted-job resume** — read-only interrupted jobs are auto-offered for resume; mutating
   interrupted jobs require an explicit user re-run and are never silently resumed.
10. **Approval policy naming** — `AutoSafe` is the canonical v0.3 policy name. `AutoApprove` may
    remain as a legacy/config alias during migration.
11. **Sidebar breakpoint** — the sidebar auto-collapses below `100` terminal columns.
12. **Local CLI parser** — the mux-owned parser/dispatcher is the v0.3 plan unless it becomes
    painful enough to justify a deliberate command-host replacement.

---

## 15. Suggested build order (non-binding)

Not a phasing gate — just a sane dependency order:

1. **Engine first:** `JobManager`, `Job`, `WriteLease`, tool read/mutate classification,
   per-job approval routing, `SessionStore`. Unit-test headless (no UI).
2. **Shell:** `MuxTuiApp` — regions, menu bar, footer, composer, focused transcript pane,
   command catalog; wire one job end-to-end (parity with today, single job).
3. **Concurrency UI:** sidebar jobs list, job switcher, enqueue chooser, jobs modal, lease
   indicators.
4. **Modals:** model manager, settings form, MCP/endpoint manager, approval modal, help,
   session browser.
5. **Persistence UX:** autosave, resume flow, history browser, persisted prompt history.
6. **Polish:** theming, density, mouse, notifications, diff/expand tool detail.

---

*Grounded in: `Mux.Core` `AgentEvent` stream + `AgentLoop`; TUIKit `TuiApplication`, `Pane`
(thread-safe, `UpdateLine`), `Layout`/`Region`, `TextEditor`, `FuzzyList`, `Form`, `DataTable`,
`DiffView`, `MenuBar`, `ModalStack`, `NotificationCenter`, `CommandRouter`/`KeyChord`,
`Observable<T>`.*

---

# 16. Execution plan (feature/v0.3.0, alpha)

This is the actionable checklist. Work happens on branch **`feature/v0.3.0`** and ships as
**`0.3.0-alpha`**. Annotate each item in place as you go.

### 16.0 Execution decisions log (recorded during implementation)

Reality diverged from the draft in four places; all were resolved with the owner:

1. **Testing framework — Touchstone migration confirmed (full).** Mux currently uses a bespoke
   `TestSuite`/`TestRunner`/`TestResult` framework (suites in `Test.Automated/Suites`) plus plain
   xUnit — **no Touchstone**. Decision: **migrate to Touchstone now**, port existing suites to
   descriptors, add `Test.Nunit`, and write all new v0.3.0 tests as descriptors. Touchstone
   `0.1.12` confirmed available on nuget.org (`Touchstone.Core/.Cli/.XunitAdapter/.NunitAdapter`).
2. **Spectre.Console removal — keep the build green.** Initial draft expected removing
   `Spectre.Console` to require tearing down the legacy interactive renderer immediately. Decision:
   remove the dependency now without breaking the build by replacing the small line-mode
   Spectre surface (`Markup.Escape`, `AnsiConsole.*`, and `Table`) with mux-owned compatibility
   shims over TUIKit `0.2.0`. The full-screen legacy renderer is still replaced later by M6; it no
   longer depends on Spectre while it waits for that rewrite.
3. **Command host — local parser, no replacement package.** Complete package removal requires
   eliminating `Spectre.Console.Cli` because it brings `Spectre.Console` transitively. Decision:
   replace the command host with a narrow mux-owned dispatcher/parser for the documented command
   surface (`print`, `--print`, `probe`, `endpoint`, and default interactive startup) rather than
   introducing another argument-parser dependency. `dotnet list package --include-transitive` must
   show no `Spectre.Console`.
4. **v0.3 behavior defaults — implementation questions locked.** Owner confirmed the §14
   recommendations: `MaxConcurrency=3`; focused-job follow-ups append after the current turn;
   new jobs fork focused history at spawn; completed background transcripts persist but merge only
   by explicit action; new prompts default to focused history; one write lease per workspace;
   `run_process` and unknown external/MCP tools are mutating by default; `AutoSafe` is canonical
   with `AutoApprove` as a legacy/config alias if needed; sidebar auto-collapses below `100`
   columns; the local CLI parser remains the v0.3 plan unless it proves painful.

### 16.1 How to use this checklist

Update the box on each task as its state changes; add a short `— note: …` after any item when
useful (blockers, decisions, PR links, dates).

- `[ ]` not started
- `[~]` in progress
- `[x]` done (code + tests + docs for that item)
- `[!]` blocked — append `— blocked: <reason>`
- `[-]` dropped/not-needed — append `— dropped: <reason>`

A milestone is **done** only when every task under it is `[x]`/`[-]` **and** its *Exit criteria*
pass. Do not mark a code task `[x]` until its Touchstone descriptors exist and pass in all
configured runners (§16.3).

### 16.2 Standing code-style conformance (applies to EVERY code task)

Per `C:\code\agents\requirements\CODE_STYLE.md`. Treat this as a per-file review gate — a code
task is not `[x]` until its new/changed files satisfy all of these:

- [ ] `namespace` declared first; **all `using` statements inside the namespace block**.
- [ ] System/Microsoft usings first (alphabetical), then other usings (alphabetical).
- [ ] **One class or one enum per file** — no nested/multiple top-level types per file.
- [ ] All **public** members/constructors/methods have `///` XML docs; **no** docs on private members/methods.
- [ ] Private fields named `_PascalCase` (e.g. `_JobManager`, not `_jobManager`).
- [ ] **No `var`** — always the explicit type.
- [ ] **No tuples** unless truly unavoidable (they are not, here — use named types/DTOs).
- [ ] `await … .ConfigureAwait(false)` where appropriate.
- [ ] Every `async` method takes a `CancellationToken` (unless the class holds a `CancellationToken`/`CancellationTokenSource` member) and checks cancellation at sensible points.
- [ ] Public members needing range/null validation use explicit getters/setters over a backing field; configurable values are public members with sensible private defaults (no magic constants).
- [ ] Guard clauses at method start; `ArgumentNullException.ThrowIfNull(...)`; specific exception types with contextual messages; custom exception types for domain errors; `/// <exception>` documented.
- [ ] `IDisposable`/`IAsyncDisposable` implemented where disposables/leases are held, full Dispose pattern (`protected virtual void Dispose(bool)`, `base.Dispose()` in derived types).
- [ ] Thread-safety guarantees documented in XML comments; `Interlocked`/`ReaderWriterLockSlim` used where the pattern calls for it.
- [ ] **No `Console.Write*` in library code** (`Mux.Core`). All output goes through TUIKit in `Mux.Cli`.
- [ ] For any `IEnumerable`-returning method, provide an async variant taking a `CancellationToken`.
- [ ] Files ≥ 500 lines use the `Public-Members` / `Private-Members` / `Constructors-and-Factories` / `Public-Methods` / `Private-Methods` regions (optional below 500).

### 16.3 Standing testing conformance (applies to EVERY code task)

Per `C:\code\agents\requirements\BACKEND_TEST_ARCHITECTURE.md` (Touchstone, runner-agnostic):

- [ ] Test logic lives in **`Test.Shared`** as `TestCaseDescriptor`s inside `TestSuiteDescriptor`s; **no `Console.Write*`** there; assertions throw on failure; tests self-create and clean up their data.
- [ ] New suites are registered in the `Test.Shared` `…Suites.All` aggregator.
- [ ] Descriptors run green through **all** configured runners: `Test.Automated` (console), `Test.Xunit`, and `Test.Nunit` (added in M0).
- [ ] Not-yet-implemented cases use `skip: true` + `skipReason`, never a silent omission.
- [ ] UI is tested headlessly via TUIKit's `HeadlessBackend` + `PumpInputOnce`/`RenderOnce` snapshot pattern, expressed as Touchstone descriptors like everything else.
- [ ] `dotnet build src/Mux.sln` is **warning-clean** and `dotnet run --project src/Test.Automated -- --results results.json` exits 0 before any milestone is marked done.

---

## M0 — Branch, versioning, dependencies, test scaffolding

> **Sequenced per §16.0:** (A) Touchstone migration validated green → (B) dependency swap +
> Spectre removal / legacy teardown. Do not remove Spectre until the Touchstone pipeline is proven.

**A. Touchstone migration (build stays green)**
- [x] Create branch `feature/v0.3.0` off `main`.
- [x] Add `Touchstone.Core` to `Test.Shared`; create `MuxSuites.All` aggregator; port the first suite (`LineBuffer`) to descriptors (legacy framework coexists during the port). — added `MuxAssert`/`AssertionFailedException` helpers; `LineBufferSuite` (7 cases).
- [x] Add a `Test.Nunit` project (`Touchstone.NunitAdapter`) running `MuxSuites.All`; add it to `Mux.sln`; restore/build/run **green** to prove the pipeline end-to-end. — 8 passed (7 data-driven + 1 aggregate fact).
- [x] Port remaining suites (`Test.Automated/Suites/*` + the `Test.Xunit` tree) to descriptors; rewire `Test.Automated` → `Touchstone.Cli` `ConsoleRunner` and `Test.Xunit` → `Touchstone.XunitAdapter`. — integration suites + all survivor unit suites (Agent/Tools/Llm/Settings/Search/CLI-commands) ported; interactive-renderer tests dropped per §16.0 (die with the code in step B). `Test.Shared` gained `MUX_CONFIG_DIR`-isolated cases; `InternalsVisibleTo("Test.Shared")` added to `Mux.Cli`; xUnit parallelization disabled (global-state tests).
- [x] Delete the legacy `TestSuite`/`TestRunner`/`TestResult` framework once all suites are ported.
- [x] Confirm `Test.Automated`, `Test.Xunit`, `Test.Nunit` all green on the ported suites. — **213 total / 206 pass / 7 skip** (console); Nunit + Xunit 207 each; green on `net8.0` and `net10.0`, warning-clean.

**B. Dependency swap (TUIKit in; Spectre removed, TUI rewrite still pending)**
- [x] Bump `Mux.Cli.csproj` `<Version>` `0.2.0` → `0.3.0-alpha`; add `<PackageReleaseNotes>` noting the TUIKit rewrite is alpha.
- [x] Add `<PackageReference Include="TUIKit" Version="0.2.0" />` to `Mux.Cli.csproj` (pinned, exact); confirm it restores for `net8.0` and `net10.0`. — restores clean on both TFMs.
- [x] Remove direct `<PackageReference Include="Spectre.Console" ... />` and replace the line-mode rendering usage with mux-owned shims backed by TUIKit `StyledConsole`, `Markup`, and `Table`.
- [x] Remove `Spectre.Console.Cli` and replace the command host with a narrow local parser/dispatcher. — decision recorded in §16.0; this is required for complete transitive package removal.
- [x] Add a top-of-`README.md` alpha banner for 0.3.0 and a `CHANGELOG.md` `## v0.3.0-alpha (Unreleased)` heading.
- **Exit criteria (revised):** Touchstone pipeline green across all runners; `Mux.Core`, `Mux.Cli`, and all test projects build warning-clean on `net8.0` + `net10.0` with **TUIKit referenced**; `dotnet list package --include-transitive` for `Mux.Cli` shows no `Spectre.Console`. The full-screen legacy renderer teardown (`InteractiveCommand`, `InteractiveChromeLayout`, `LineBuffer`, etc.) still moves to **M6**, but it no longer carries a Spectre dependency while it waits for the TUIKit rewrite.

---

## M1 — Engine: Jobs subsystem (`Mux.Core/Jobs/`)

**✅ COMPLETE** (landed by the concurrent agent; reviewed + verified green). Replaces the
`_ActiveRun`/single-`Channel`/single-`Cts` singleton with a multi-job manager. One class/enum per file.

- [x] `JobState.cs` — enum: `Queued`, `Running`, `AwaitingApproval`, `AwaitingWriteLease`, `Paused`, `Completed`, `Failed`, `Cancelled`.
- [x] `Job.cs` — per-job `Channel<AgentEvent>`, per-job `CancellationTokenSource`, deep-forked `ConversationHistory`, `Transcript`, follow-up queue, timing/usage stats, `LastContextStatus`; thread-safe (`_SyncRoot`), state transitions guarded via internal `SetState`.
- [x] `JobManagerEvent.cs` — abstract base for manager notifications.
- [x] `JobAddedEvent.cs`, `JobStateChangedEvent.cs`, `JobCompletedEvent.cs` — concrete events (one per file).
- [x] `JobScheduler.cs` — pure `SelectStartableJobs`/`CountActiveJobs`; treats `Running`/`AwaitingApproval`/`AwaitingWriteLease` as slot-consuming (ready for M2/M3).
- [x] `JobManager.cs` — owns the job set, scheduler, per-job worker `Task`s pumping the agent runner into each job's channel; `SubmitAsync`/`EnqueueAsync`/`AddFollowUpAsync`/`CancelAsync`/`CancelAllAsync`/`PauseAsync`/`ResumeAsync`/`ReorderAsync`/`Focus`/`GetJob`; `JobManagerEvent` stream (async-enumerable `ReadEventsAsync` + `EventPublished`). `IAsyncDisposable`. `CreateForAgentLoop` factory clones `AgentLoopOptions` per job.
- [x] Add `MaxConcurrency` (default `3`, clamped `1–32`) and `DefaultEnqueueBehavior` (`ask`/`run_now`/`queue_after`/`add_to_focused`) to `MuxSettings`; `SettingsLoader` parse/clamp/normalize + `SettingsLoaderSuite` coverage.
- [x] New jobs fork a deep copy of the focused job's history at spawn; follow-ups append after the current turn.
- [~] Completed background job transcripts retained per-job (`Job.Transcript`); explicit **merge** into focused history is a UI concern deferred to M8.
- [x] Per-job agent invocation (fresh `AgentLoop` per turn via the injected runner).
- [x] **Tests** (`Test.Shared/Suites/JobManagerSuite.cs`, 9 cases): submit→completion; concurrency cap queues overflow; 3-concurrent + 4th queued; cancel frees a slot; reorder changes run order; forked focused history (copy-safety); follow-up appends after current turn; dispose cancels all. Registered in `MuxSuites.All`.
- **Exit criteria:** ✅ `JobManager` drives 3 concurrent fake-agent jobs headlessly; suite green across console/xUnit/NUnit on `net8.0` + `net10.0`; `Mux.Core` stays `Console.*`-free. (Concurrency-test safety timeouts raised 5s→30s to remove net8 thread-pool-timing flakiness.)

---

## M2 — Engine: Write lease + tool classification (`Mux.Core/Jobs/`, `Mux.Core/Tools/`)

**✅ COMPLETE** (commits `e62caad` part 1, part 2 the integration). Implements "parallel reads, single writer."

- [x] `ToolMutationKind.cs` — enum: `ReadOnly`, `Mutating`.
- [x] `MutationKind` classification in `BuiltInToolRegistry` (`GetMutationKind`): `read_file`, `glob`, `grep`, `web_retrieve`, `web_search`, `file_metadata`, `list_directory` = `ReadOnly`; `write_file`, `edit_file`, `multi_edit`, `delete_file`, `manage_directory`, `run_process` = `Mutating`.
- [x] Unknown/external (MCP) tools default to `Mutating` (safe) via `GetMutationKind`'s fallback.
- [x] `WriteLeaseHandle.cs` — idempotent `IDisposable`/`IAsyncDisposable` release token.
- [x] `WriteLease.cs` — fair FIFO single-writer lease (custom queue for true FIFO + waiter identity, not a bare `SemaphoreSlim`), `AcquireAsync(jobId, ct)` → handle; exposes `CurrentHolderJobId` + `WaitingJobIds`; `AcquisitionTimeoutMs` (default infinite, documented); cancellation-safe. (+ `WriteLeaseWaiter`, `WriteLeaseTimeoutException`.)
- [x] Lease injected at the tool-execution chokepoint (`AgentLoop.ExecuteToolCallAsync`): mutating tools acquire/release around execution; reads bypass. `AgentLoopOptions` gains `WriteLease`/`JobId`/`OnWriteLeaseWaitChanged`; `JobManager` owns a shared lease and wires each job (race-safe `Job.TrySetAwaitingWriteLease`/`TryResumeRunningFromLeaseWait`).
- [~] Lease wait/acquire/release is observable via `Job.State` (`AwaitingWriteLease`) + `WriteLease.CurrentHolderJobId`/`WaitingJobIds`; publishing dedicated telemetry events for the sidebar (`🔒`/`⧗`) is deferred to M8 (the transitions are queryable now).
- [x] **Tests**: `WriteLeaseSuite` (7 unit cases: serialize+handoff, FIFO, timeout, cancellation recovery, dispose-on-exception, idempotent dispose) **and** `WriteLeaseIntegrationSuite` (2 cases through the real `AgentLoop`: mutating tool blocks on a held lease then executes; read-only tool bypasses a held lease).
- **Exit criteria:** ✅ read-only tools run without the lease; mutating tools serialize; green across console/xUnit/NUnit on `net8.0` + `net10.0` (225 each).

---

## M3 — Engine: Approval routing (`Mux.Core/Approvals/`)

Per-job policy with modal-escalation callback; replaces the single `_ApprovalPolicy` field.

- [ ] `ApprovalPolicy.cs` — enum: `Ask`, `AutoSafe`, `Deny` (map/replace the existing `ApprovalPolicyEnum`; migrate references; keep `AutoApprove` as a legacy/config alias if needed).
- [ ] `ApprovalDecision.cs` — enum: `Approved`, `Denied`, `AlwaysThisSession`, `AlwaysThisTool`.
- [ ] `ApprovalRequest.cs` — DTO: job id, tool name, arg summary, optional diff, mutation kind.
- [ ] `IApprovalRouter.cs` — abstraction the engine calls; resolves via policy, else escalates via an injected UI callback (keeps today's `TaskCompletionSource` handoff, now per job).
- [ ] `ApprovalRouter.cs` — implementation: `AutoSafe` auto-approves `ReadOnly` tools + a configurable per-tool allowlist; escalates the rest; `Deny` blocks; `Ask` always escalates. Concurrent requests from parallel jobs are independently awaitable.
- [ ] Thread the per-job `ApprovalPolicy` and router through `AgentLoop`'s tool loop.
- [ ] **Tests** (`Test.Shared/Suites/ApprovalRoutingSuite.cs`): `AutoSafe` auto-approves reads and prompts for mutations; legacy/config `AutoApprove` maps to `AutoSafe` if retained; `Deny` blocks; `AlwaysThisTool`/`AlwaysThisSession` remembered and honored; two jobs escalate independently without deadlock; a fake router simulates the UI callback.
- **Exit criteria:** policy matrix correct; concurrent escalations don't block each other; suite green.

---

## M4 — Engine: Sessions & persistence (`Mux.Core/Sessions/`)

- [ ] `SessionSnapshot.cs` — versioned serializable DTO: schema version, id, title (+ pinned), created/updated, active endpoint + model, settings snapshot, focused `ConversationMessage[]`, persisted per-job transcripts, compaction count, queued-prompt/job metadata, persisted prompt history. Forward-tolerant of unknown fields.
- [ ] `SessionStore.cs` — CRUD over `~/.mux/sessions/<id>.json` (configurable root as a public member); `SaveAsync`, `LoadAsync`, `ListAsync` (+ async `IEnumerable` variant per style rule), `DeleteAsync`, `DuplicateAsync`; atomic write (temp-file + move); tolerant JSON options.
- [ ] `SessionResumeService.cs` — rehydrates a snapshot into live session state; running-at-exit jobs are restored as `Interrupted` (re-run required for mutating; auto-offer for read-only per §14 decision).
- [ ] `SessionMergeService.cs` — explicit-only merge of a completed background job transcript into the focused/current history; no automatic merge on completion.
- [ ] Autosave hook on each turn boundary (append/replace), throttled/configurable.
- [ ] Replace the in-memory `PromptHistory` with per-session persisted history (read from/write to the snapshot).
- [ ] **Tests** (`Test.Shared/Suites/SessionStoreSuite.cs`): round-trip save/load fidelity; list/delete/duplicate; atomic write survives simulated mid-write failure (temp file only); unknown-field tolerance; resume rehydrates history + queue; background job transcripts persist; merge requires explicit action; special-character titles. Use a temp directory, clean up.
- **Exit criteria:** full round-trip + resume verified headlessly; suite green in all runners.

---

## M5 — Engine gate

- [ ] All of M1–M4 suites registered in `…Suites.All` and green across `Test.Automated`, `Test.Xunit`, `Test.Nunit`.
- [ ] `Mux.Core` has zero `Console.*` and zero UI dependencies (grep-verified).
- [ ] `dotnet build src/Mux.sln` warning-clean.
- **Exit criteria:** the engine can run parallel jobs with lease + approvals + persistence, fully headless, with no UI present.

---

## M6 — Cli shell: `MuxTuiApp` host + layout + command catalog (single job end-to-end)

- [ ] `App/MuxTuiApp.cs` — owns `ConsoleBackend` + `TuiApplication`, builds the `Layout` (regions from §4.1: `menu`, `sidebar`, `transcript`, `composer`, `footer`), binds panes/widgets, registers keybindings, runs `RunAsync`. Implements `IAsyncDisposable`.
- [ ] `Commands/CommandDescriptor.cs` — `{ Id, Title, Category, KeyBinding?, SlashAlias?, RunAsync }`.
- [ ] `Commands/CommandCatalog.cs` — the single source of truth; palette, menu bar, function keys, and slash parser all resolve against it.
- [ ] `Regions/TranscriptRegion.cs` — hosts the focused job's `Pane`; swap binding on focus change.
- [ ] `Regions/ComposerRegion.cs` — TUIKit `TextEditor` host (behavior fleshed out in M9).
- [ ] `Regions/FooterRegion.cs` — status line + f-key hint strip (content in M10).
- [ ] Wire a **single** job end-to-end: type prompt → `JobManager.SubmitAsync` → project its `AgentEvent`s into the transcript pane (projector stub; full in M7).
- [ ] `CtrlCPolicy` = double-tap-to-exit; `Esc` cancels the focused job.
- [ ] **Tests** (`Test.Shared/Suites/TuiShellSuite.cs`, headless): app boots against `HeadlessBackend`; layout regions present at representative sizes; too-narrow collapses sidebar (not a block screen); submitting a prompt drives one job to completion and renders its final text (snapshot).
- **Exit criteria:** a single prompt runs and renders end-to-end on TUIKit with no legacy renderer; headless snapshot stable.

---

## M7 — Cli: AgentEvent projector (events → panes)

- [ ] `Rendering/AgentEventProjector.cs` — maps each `AgentEvent` to `Pane` writes for a given job: assistant text streaming, tool proposed/completed → **inline line updated in place** via `Pane.UpdateLine` (`running…` → `done 0.3s`), context status → sidebar observable, errors → toast, run started/completed → state.
- [ ] `Rendering/ToolCallView.cs` — inline summary line + optional expand (args/output/diff).
- [ ] Markdown via TUIKit `MarkdownRenderer`; `Edit` diffs via `DiffView` + `SyntaxHighlighter`.
- [ ] Ensure each job writes to **its own** `Pane`; only the focused pane is bound to `transcript`.
- [ ] **Tests** (`Test.Shared/Suites/ProjectorSuite.cs`, headless): a scripted `AgentEvent` sequence produces the expected pane snapshot; tool line mutates in place (not appended twice); interleaved events from two jobs land in the correct panes.
- **Exit criteria:** inline tool status updates verified via snapshot; two jobs' output never cross-contaminate panes.

---

## M8 — Cli: Sidebar + focus switcher

- [ ] `Regions/SidebarRegion.cs` — vertical stack: context `Gauge`, model, session, cwd/branch, jobs list.
- [ ] `Regions/JobsListWidget.cs` — one row per job with state glyph, id, title, live tokens/elapsed; focusable; Enter focuses that job's transcript.
- [ ] Bind sidebar fields to `Observable<T>` fed by `JobManagerEvent`s and focused-job `ContextStatusEvent`.
- [ ] Focus switching: sidebar select + `Ctrl+J` cycle + `Alt+<n>` jump; swap transcript pane binding.
- [ ] Sidebar collapse toggle (`Ctrl+B`) + responsive auto-collapse below `100` columns.
- [ ] **Tests** (`Test.Shared/Suites/SidebarSuite.cs`, headless): jobs list reflects manager state; switching focus swaps the rendered transcript; collapse reclaims width; context gauge reflects focused job.
- **Exit criteria:** ambient info + switcher verified headlessly across ≥ 2 concurrent jobs.

---

## M9 — Cli: Composer + enqueue gesture

- [ ] `Regions/ComposerRegion.cs` — multi-line `TextEditor`; Enter submits, Shift+Enter newline; autogrow to a cap then scroll.
- [ ] `App/SubmitChooser.cs` — the "ask per submit" chooser (run-now parallel / queue-after / add-to-focused / remember-choice) shown when ≥ 1 job runs; modifier bypass: `Alt+Enter` run-now, `Ctrl+Enter` queue-after.
- [ ] Prompt-history recall at buffer edges (Up/Down), reading persisted per-session history.
- [ ] Leading `/` routes to the slash parser (M10) instead of submitting a prompt.
- [ ] **Tests** (`Test.Shared/Suites/ComposerSuite.cs`, headless): Enter with no active job submits immediately; Enter with an active job shows the chooser; each chooser branch dispatches to the right `JobManager` call; modifiers bypass the chooser; Shift+Enter inserts a newline; history recall works.
- **Exit criteria:** enqueue-while-running behavior matches §3.4 for every branch, verified headlessly.

---

## M10 — Cli: Footer, menu bar, function keys, palette, slash router

- [ ] `Regions/MenuBarRegion.cs` — TUIKit `MenuBar` with the §5 tree (Session/Jobs, Model/Endpoints, View/Appearance, Tools/Help), each item invoking a `CommandCatalog` command.
- [ ] `App/CommandPalette.cs` — `Ctrl+K` fuzzy palette (`FuzzyList`) over the catalog; shows bound keys.
- [ ] `Commands/SlashCommandParser.cs` — parses composer `/…` input against catalog `SlashAlias`es (§7 table).
- [ ] Footer status content (`ctx · jobs · lease · focused state`) + context-sensitive f-key hint strip (`F1..F4`, `^K`).
- [ ] Bind function keys and global chords via `CommandRouter`/`KeyChord`; document the full keymap (§9).
- [ ] `NotificationCenter` toasts for ephemeral messages (errors, "queued j4", "copied").
- [ ] **Tests** (`Test.Shared/Suites/CommandSurfacesSuite.cs`, headless): the same command id is reachable via menu, palette, slash, and f-key and produces identical effect; palette fuzzy-match ranks expected item; unknown slash shows an error toast, not a crash.
- **Exit criteria:** one catalog, four surfaces, verified to converge on identical behavior.

---

## M11 — Cli: Modals (`Mux.Cli/Modals/`)

All via `ModalStack`; concurrent approval requests queue.

- [ ] `Modals/ModelModal.cs` — searchable picker + detail; actions: set active, pull/download (progress), remove, bind-to-endpoint.
- [ ] `Modals/SettingsModal.cs` — grouped `Form` (General/Model/Tools/Approvals/Jobs/Appearance), live apply → `MuxSettings` + settings file.
- [ ] `Modals/McpEndpointModal.cs` — endpoints (list/add/edit/test/set-active) + MCP servers (add/remove/inspect tools via `DataTable`, enable/disable).
- [ ] `Modals/JobsModal.cs` — `DataTable<Job>` queue manager: focus, pause/resume, cancel, reorder, retry, open transcript, merge transcript, view diff; opened from the footer job counter.
- [ ] `Modals/ApprovalModal.cs` — the policy-escalation prompt (Yes/No/Always-tool/Always-session, with diff); titled with the requesting job id; multiple queue in `ModalStack`.
- [ ] `Modals/HelpModal.cs` — keybindings/commands reference generated from the catalog (never drifts).
- [ ] `Modals/SessionBrowserModal.cs` — list persisted sessions; resume/delete/duplicate/export.
- [ ] **Tests** (`Test.Shared/Suites/ModalsSuite.cs`, headless): each modal opens/renders/returns its awaitable result; settings changes propagate to `MuxSettings`; approval modal resolves the engine's awaiting request; two queued approvals present in order; jobs modal actions call the right `JobManager` methods.
- **Exit criteria:** every §8 modal functional and driven headlessly; approval escalation round-trips with the engine.

---

## M12 — Cli: Persistence UX

- [ ] Autosave wired to turn boundaries via `SessionStore`; status reflected in footer.
- [ ] Launch flow: offer resume (last session or browser); `--resume`/`--new` CLI flags honored.
- [ ] Interrupted-job presentation on resume (explicit re-run for mutating; auto-offer for read-only; no silent mutating resume).
- [ ] `Ctrl+S` / menu "Save session"; session rename/title persists.
- [ ] **Tests** (`Test.Shared/Suites/PersistenceUxSuite.cs`, headless): run → exit → relaunch → resume restores transcript + queue metadata; interrupted mutating job is presented as re-run-required; interrupted read-only job is auto-offered; explicit background-transcript merge updates focused history; prompt history survives restart.
- **Exit criteria:** a session survives a restart end-to-end (engine + UI), verified headlessly.

---

## M13 — Cli headless UI test consolidation

- [ ] Ensure every Cli suite uses `HeadlessBackend` + deterministic `PumpInputOnce`/`RenderOnce` frames (no real terminal, no timing flakiness; use the fake deterministic agent, not a live LLM).
- [ ] Add golden-snapshot cases for the primary screen at a few representative sizes and states (idle, one running job, three jobs with one awaiting lease, modal open).
- [ ] Confirm all Cli suites registered in `…Suites.All` and green in `Test.Automated`, `Test.Xunit`, `Test.Nunit`.
- **Exit criteria:** UI is regression-guarded by headless snapshots across runners.

---

## M14 — Polish

- [ ] `Rendering/MuxTheme.cs` — Mux theme (truecolor + graceful 256/16 quantization); theme picker in Settings/View.
- [ ] Density setting (compact/comfortable) affecting padding + inline tool-detail expansion.
- [ ] Mouse: click-to-focus panes/jobs, wheel scroll, clickable transcript links; `F12` toggle mouse capture.
- [ ] Verify resize handling (TUIKit `SyncSize`) repaints correctly; sidebar breakpoint tuned.
- [ ] **Tests:** headless cases for theme switch (no layout break), density toggle, and resize repaint.
- **Exit criteria:** theming/density/mouse/resize verified; no snapshot breakage.

---

## M15 — Docs, changelog, CI, and merge

- [ ] Update `README.md` to describe the TUIKit-based UI, parallel jobs, queue, sessions; refresh screenshots/GIFs in `assets/`; keep the alpha banner.
- [ ] Rewrite the relevant parts of `USAGE.md` (keymap, menus, palette, slash commands, job/queue workflow) and `CONFIG.md` (new settings: `MaxConcurrency`, enqueue behavior, lease timeout, session store root, theme/density, approval allowlist).
- [ ] Finalize `CHANGELOG.md` `## v0.3.0-alpha` entry (added/changed/removed — call out Spectre.Console removal and the concurrency model).
- [ ] Confirm repository requirements (`REPOSITORY_REQUIREMENTS.md`): `.gitignore`, `README.md`, `CHANGELOG.md`, `LICENSE.md` present; all source under `src/`; if the packaging surface changed, verify `PackageReadmeFile`.
- [ ] Add/refresh the CI workflow to build `src/Mux.sln` and run `Test.Automated` + `Test.Xunit` + `Test.Nunit` on `8.0.x` and `10.0.x` (mirror the test-architecture GitHub Actions sample); upload `results.json`.
- [ ] Full green run: `dotnet build src/Mux.sln` (warning-clean) + all three test runners + a manual smoke test on a real terminal (TUIKit's `ConsoleBackend`/interactive loop is validated by manual smoke, not headless coverage).
- [ ] Verify the local `C:\Code\TUIKit` source is **not** referenced by `Mux.sln` (package reference only); if any TUIKit bug was found, it was fixed by publishing a new TUIKit package and bumping Mux's pin — not by adding a project reference.
- [ ] Update `CLAUDE.md` if any repo-specific conventions changed (per CODE_STYLE guidance to keep it current).
- [ ] Open PR from `feature/v0.3.0`; ensure the checklist above is fully annotated in the PR description.
- **Exit criteria:** docs accurate, CI green on both TFMs across all runners, manual smoke passes, ready to tag `0.3.0-alpha`.

---

### 16.4 Global definition of done (v0.3.0-alpha)

- [ ] Legacy interactive renderer (`InteractiveChromeLayout`, cursor bookkeeping, poll-loop input, paste heuristics, `_ActiveRun` singleton) **deleted**, not dormant.
- [ ] `Mux.Core` remains UI-free (no `Console.*`, no TUIKit reference); jobs/lease/approvals/sessions live there and are headless-tested.
- [ ] `Mux.Cli` renders exclusively via TUIKit (`Spectre.Console` and `Spectre.Console.Cli` gone; local parser retained unless replaced deliberately).
- [x] All §14 implementation decisions are recorded and owner-approved, including forked job history, explicit transcript merge, tool classification defaults, resume behavior, `AutoSafe`, and the sidebar breakpoint.
- [ ] Every code file conforms to §16.2; every feature has Touchstone coverage passing in all runners (§16.3).
- [ ] Build warning-clean on `net8.0` + `net10.0`; `0.3.0-alpha` marked alpha throughout.

---

# 17. Spectre.Console removal — prerequisites

`Spectre.Console` and `Spectre.Console.Cli` have been removed from `Mux.Cli`. This checklist records
the completed prerequisite work: TUIKit capability coverage, the mux-side rendering swap, and the
transitive command-host gate.

Original verified `mux` Spectre footprint: `Markup.Escape` ×122; `AnsiConsole.MarkupLine`/`Markup` ×63;
`new Table()` + `AnsiConsole.Write(table)` ×~18 (`TableBorder.Rounded`); `AnsiConsole.WriteLine` ×4.
Styles: `dim/bold/italic/underline`, fg `cyan/green/red/yellow/grey/blue/grey15`, bg `on grey15`. No
`Live`/`Status`/`Progress`/prompt usage.

### Track A — TUIKit capabilities (see archived `C:\Code\TUIKit\archive\TUIKIT_GAPS.md`)

> **✅ Done in TUIKit v0.2.0** (built warning-clean on `netstandard2.0;net8.0;net10.0`; 264 console /
> 265 xUnit / 265 NUnit Touchstone cases green on net8.0 and net10.0). The capabilities were
> implemented in the TUIKit source tree, versioned to **0.2.0**, published to NuGet, and consumed
> by mux as a pinned `<PackageReference Include="TUIKit" Version="0.2.0" />`.

- [x] **G1** `Markup.Escape` — **satisfied by** `TUIKit.Markup.Escape(string)` (escapes `[`→`[[`, `]`→`]]`; round-trips via `Parse`). Replaces mux's `Markup.Escape` (×122).
- [x] **G2** `StyledText`/markup → ANSI string — **satisfied by** `TUIKit.Terminal.AnsiText.Render(StyledText, TerminalColorDepth)` and a `Render(string markup, …)` overload; additive SGR only, no cursor moves, plain when depth is `None`.
- [x] **G3** `CellBuffer` → colored inline ANSI lines — **satisfied by** `TUIKit.Rendering.InlineRenderer.ToAnsiLines(CellBuffer, TerminalColorDepth)`; coalesced SGR runs, trailing-blank trim, equals `Snapshot.ToText` when plain.
- [x] **G4** capability resolution — **satisfied by** `NO_COLOR` handling folded into `CapabilityDetector.Detect(getEnv, interactive)` plus `CapabilityDetector.ResolveOutputColorDepth(TextWriter)` (None when redirected / `NO_COLOR` / `TERM=dumb`).
- [x] **G5** `StyledConsole` inline writer — **satisfied by** `TUIKit.StyledConsole` (`ForStandardOutput()`/`ForStandardError()`/explicit ctor; `Write`/`WriteLine`/`Markup`/`MarkupLine`/`Write(IWidget)`). Replaces `AnsiConsole.Markup/MarkupLine/WriteLine/Write`; writes only to the injected `TextWriter`, degrades to plain when redirected.
- [x] **G6** `Table` parity — **satisfied by** the extended `TUIKit.Widgets.Table`: `TableBorder` (None/Square/**Rounded**), `AddRow(params StyledText[])`/`AddMarkupRow`, `ColumnSizing.FitContent`, per-column `CellAlignment` (`SetAlignment`); the original even-column, borderless `Table(headers)`+`AddRow(string[])` behavior is preserved.
- [x] **G7** — **decided: Option A** (mux rewrites `grey15`/`on grey15` mux-side to `#262626`; no TUIKit change). Recorded in Track B.

### Track B — mux rendering swap (after Track A ships)
- [x] Bump the pinned `TUIKit` reference in `Mux.Cli.csproj` to the release containing G1–G6. — pinned to `0.2.0`.
- [x] Replace `Markup.Escape(...)` → TUIKit-backed escaping. — done through a mux-owned compatibility shim so call sites can be retired incrementally during M6.
- [x] Replace `AnsiConsole.MarkupLine/Markup/WriteLine(...)` → TUIKit `StyledConsole`. — done through a mux-owned compatibility shim over `Console.Out`.
- [x] Replace `new Table()` + `AnsiConsole.Write(table)` → TUIKit `Table` rendered via `StyledConsole.Write(table)`.
- [x] Rewrite `grey15` / `on grey15` markup → `#262626` mux-side.
- [x] Interactive-renderer Spectre usage removed before M6. — the legacy renderer still exists, but its styled output path is TUIKit-backed.
- [x] Confirm redirection parity: the non-interactive commands' captured stdout/stderr stay **plain** so the existing `CliContract`/`CliCommand` Touchstone suites still pass.
- [x] Remove the direct `<PackageReference Include="Spectre.Console" ... />` from `Mux.Cli.csproj`.

### Track C — transitive package gate (required for *complete* package removal)
- [x] Removing the **package** entirely additionally requires replacing **`Spectre.Console.Cli`** (the
  command/arg-parsing host in `Program.cs`), because it depends on `Spectre.Console` transitively.
  TUIKit does **not** cover this (not an arg parser). Decision: replace `Spectre.Console.Cli` with a
  narrow mux-owned parser/dispatcher rather than introducing a new parser package.

### Exit criteria (complete removal)
- [x] `dotnet list package --include-transitive` for `Mux.Cli` shows **no** `Spectre.Console` (direct
  or transitive) — achievable only once Track C is also done.
- [x] All Touchstone suites green on `net8.0` + `net10.0` across console/xUnit/NUnit runners
  (222 console / 216 xUnit / 216 NUnit; 7 skips) — verified after the swap. Redirection parity holds
  (captured CLI stdout/stderr stay plain, so `CliContract`/`CliCommand` suites pass).
- [ ] Manual smoke: confirm `mux print/probe/endpoint` output visually matches the pre-removal styling
  in a real (non-redirected) terminal — pending an on-terminal check (not headless-verifiable).
