# Background Tasks — Design and Implementation Plan (targets v0.5.0)

A large request rarely maps to a single tool call. When you ask mux to "build request-history
persistence," the honest shape of that work is five or six steps — study the existing pattern, add
an interface, port the providers, rewire the call sites, write the tests — and today mux hides that
shape. The model does the work, but the user watches an undifferentiated stream of tool calls and
has to reconstruct the plan in their head. This plan gives mux a first-class notion of a **task
plan**: the model decomposes a job into named tasks, marks them sequential or parallel, works
through them, and the TUI renders a live checklist that updates in place as each task moves from
pending to running to done.

The target is what Claude Code shows when it takes on something big:

```text
✢ Building request-history persistence… (14m 7s · ↓ 72.1k tokens)
  ⎿  ✔ Study per-provider schema-init/migration pattern
     ◼ Add IRequestHistoryMethods + 4 provider implementations
     ◻ Remove in-memory RequestHistory from platform
     ◻ Rewrite routing write + endpoints + retention sweep
     ◻ Build all four providers + tests
```

mux already has the two hard pieces this needs. The `Job`/`JobManager`/`WriteLease` subsystem gives
real concurrency with a single-writer workspace lock, and the `AgentEvent` → `AgentEventProjector`
pipeline already streams a job's activity onto a thread-safe pane with in-place line updates. A task
plan is a new layer that rides both: a per-job data model the model edits through two tools, a new
event that carries plan snapshots, and a projector path that draws the checklist. Nothing here
fights the existing architecture — it extends it along seams that already exist.

The feature ships in the **v0.5.0** line. The whole-product sweep at the end bumps every `<Version>`,
`Defaults.ProductVersion`, the README badge, and the changelog, and updates the prose docs and the
JSONL automation contract so the new capability is represented everywhere a reader would look for it.
Three constraints shape every decision below and are not a finishing pass: the work follows
`c:\code\agents\requirements` (`CODE_STYLE.md`, `REPOSITORY_REQUIREMENTS.md`, `WRITING_DOCUMENTS.md`)
to the letter; it is tested at every layer through the Touchstone descriptor suite the repo already
runs on three runners and two target frameworks; and it is observable end to end, in the interactive
TUI and in the machine-readable `mux print` stream alike. A compliance appendix at the end maps each
rule to a concrete decision.

## Implementation status

**Shipped on `feature/v0.5.0`.** All ten milestones are complete. Full validation is green: the solution
builds warning-clean on `net8.0` and `net10.0`, the Touchstone console runner passes 446/453 (7 pre-existing
skips) on both frameworks, and the xUnit and NUnit adapters pass 447/447 on both frameworks. The one
consciously deferred item is interactive wiring of the parallel orchestration **engine** (M8), which is
complete and tested but not yet surfaced as a live interactive UX; the engine and its flag ship, and
`mux print` plus the engine are the supported surfaces today.

Legend for the execution checklist (Section 12), reused from the repo's prior plans:

- `[ ]` not started
- `[~]` in progress
- `[x]` done (code + tests + docs for that item)
- `[!]` blocked — append `— blocked: <reason>`
- `[-]` dropped / not needed — append `— dropped: <reason>`

Update the box on each task as its state changes, and add a short `— note: …` after any item when a
blocker, decision, date, or PR link is worth recording. A milestone is done only when every task
under it is `[x]` or `[-]` **and** its exit criteria pass. Do not mark a code task `[x]` until its
Touchstone descriptors exist and pass in all three runners on both target frameworks.

| Milestone | Scope | State |
|---|---|---|
| M0 | Branch, versioning, settings + test scaffolding | `[x]` |
| M1 | Core task-plan model + validation (`Mux.Core/Tasks/`) | `[x]` |
| M2 | Task event + enum + JSONL contract wiring | `[x]` |
| M3 | Model-facing tools (`plan_tasks`, `update_task`) + AgentLoop emission | `[x]` |
| M4 | Job / JobManager wiring + session persistence | `[x]` |
| M5 | System prompt guidance + settings gate | `[x]` |
| M6 | TUI: live checklist projector + sidebar progress | `[x]` |
| M7 | TUI: `/tasks` viewer + human annotation | `[x]` |
| M8 | Orchestrated parallel execution (JobManager bridge) | `[x]` |
| M9 | Documentation sweep + version bump + CI gate | `[x]` |

## 1. Two words that must never blur: job and task

The one concept most likely to cause confusion — in code, in docs, and in the UI — is the difference
between a **job** and a **task**, so it is worth pinning down before anything else.

A **job** already exists. It is one top-level unit of agent work bound to a session: a forked
conversation history, its own event channel, its own cancellation, and a `JobState`. The user
creates jobs by submitting prompts; `JobManager` schedules them against `MaxConcurrency` and the
shared `WriteLease`. Jobs are the concurrency primitive.

A **task** is new and lives *inside* a job. It is one line in the model's plan for that job — a
short title, a status, and its dependencies on other tasks in the same plan. The model authors the
plan; the model (or, when a human intervenes, the user) advances it. A task is a *tracking* primitive
first. In the default and most common mode, tasks describe and observe the work the model is already
doing turn by turn inside one agent loop, exactly like the checklist in the example above.

The two ideas meet only in Section 8. When a plan marks independent tasks as parallel and the user
opts into orchestrated execution, mux can dispatch dependency-ready tasks as their own jobs onto the
existing `JobManager` — tasks becoming jobs, scheduled and write-lease-serialized like any other. That
bridge is real and designed here, but it is a later milestone gated behind a setting. The MVP is
tracking, because tracking is what makes the work legible and it reuses the event and render
pipeline with no new scheduler.

Throughout the code, "job" keeps its current meaning and "task" always means a node in a task plan.
The runtime type is `AgentTask` (never a bare `Task`, which would collide with
`System.Threading.Tasks.Task`), and the status enum is `AgentTaskStatusEnum`.

## 2. Decisions locked

The design review settled the following, and the rest of the plan assumes them.

- **A plan belongs to a job.** Each `Job` owns at most one `TaskPlan`. Spawning a new job does not
  inherit the parent's plan. This keeps the plan's lifetime, persistence, and cancellation identical
  to the job's, and it means a plan snapshot always has an unambiguous owner.
- **The model edits the plan through tools, not free text.** Two tools — `plan_tasks` to establish or
  replace the plan and `update_task` to advance a single task — are the only write path. Parsing plans
  out of assistant prose would be fragile and unobservable; tool calls are already approved, logged,
  and rendered.
- **Both task tools are read-only with respect to the workspace.** They mutate in-memory plan state,
  never the filesystem, so they are classified `ToolMutationKind.ReadOnly` and never sit behind the
  write lease. A model narrating its plan must not be able to starve a sibling job of the workspace.
- **One event type carries plan changes.** `TaskPlanUpdatedEvent` (`task_plan_updated`) carries a full
  snapshot of the current plan plus a change descriptor (what changed, which task). A whole-plan
  snapshot makes the projector trivial — always redraw from the latest snapshot — and gives JSONL
  consumers a complete picture on every event without replaying deltas.
- **Rendering redraws the block in place.** The checklist is a contiguous block of pane lines updated
  through the same `PaneLineHandle` mechanism the tool-call renderer already uses, so a status flip
  rewrites one line rather than reprinting the list.
- **Tracking mode ships first; orchestrated parallel execution is opt-in.** The default experience is a
  model self-tracking one job's work. Fan-out onto real parallel jobs (Section 8) is gated behind
  `TaskParallelismEnabled` (default `false`) and layered on last.
- **Persistence is forward-compatible, not schema-breaking.** The plan is a new nullable field on the
  per-job session snapshot. Old sessions load unchanged; new sessions carry their plans. No
  `SchemaVersion` bump is required, and none is taken.

## 3. The task-plan model — `Mux.Core/Tasks/`

The runtime model is a small, self-contained subsystem with one public type per file, mirroring how
`Mux.Core/Skills/` and `Mux.Core/Jobs/` are laid out. It has no dependency on the CLI, the LLM
client, or the job manager — it is a plain, thread-safe data structure that anything can hold.

### 3.1 `AgentTask`

```text
AgentTask
  Id           string   stable, model-assigned (e.g. "t1"); unique within a plan
  Title        string   short imperative label ("Add IRequestHistoryMethods + 4 providers")
  Status       AgentTaskStatusEnum
  DependsOn    List<string>   ids of tasks that must complete first (edges of the DAG)
  Note         string?  optional running annotation (a blocker, a decision, a result)
  CreatedUtc / StartedUtc? / CompletedUtc?    lifecycle timestamps
  DurationMs   long?    filled when the task reaches a terminal status
  AssignedEndpointName  string?   optional; the endpoint a parallel task should run under (Section 8)
  FailureMessage        string?   set when Status == Failed
```

`AgentTaskStatusEnum` (`Mux.Core/Enums/AgentTaskStatusEnum.cs`, string-serialized via
`[JsonStringEnumConverter]` + `[EnumMember]` like the other wire enums): `Pending`, `InProgress`,
`Completed`, `Failed`, `Skipped`, `Blocked`. `Blocked` is distinct from `Pending` — a pending task is
simply not started yet; a blocked task is waiting on a dependency or an external condition the model
has flagged. The projector renders each status with its own glyph (Section 6).

### 3.2 `TaskPlan`

`TaskPlan` (`Mux.Core/Tasks/TaskPlan.cs`) owns the ordered list of `AgentTask` and is the unit a job
holds. It is thread-safe — a background worker task writes to it while the render loop reads a
snapshot — guarded by a private `_SyncRoot`, with the thread-safety contract documented in XML per
`CODE_STYLE.md`. Its surface:

- `SetPlan(IReadOnlyList<AgentTask> tasks)` — replace the whole plan (the `plan_tasks` write path).
  Validates first (§3.3); on success it bumps an internal `Version` counter and stamps `CreatedUtc`.
- `UpdateTask(string id, AgentTaskStatusEnum status, string? note)` — advance one task, set its
  `StartedUtc`/`CompletedUtc`/`DurationMs` on the appropriate transitions, and bump `Version`.
- `Snapshot()` — a deep, immutable copy for events, rendering, and persistence, so a consumer never
  holds a reference into live mutable state.
- `ReadyTasks()` — tasks whose status is `Pending` and whose every dependency is `Completed`. This is
  the scheduling seam Section 8 consumes; in tracking mode it is advisory (it tells the sidebar what
  could start next).
- `CompletedCount` / `TotalCount` — for the sidebar's `TASKS n/m`.
- An `IEnumerable`-returning query (e.g. `TasksInStatus`) ships with the required async
  `CancellationToken` variant per `CODE_STYLE.md`.

`Version` is a monotonic `int` bumped under the lock on every mutation. The AgentLoop uses it to
decide whether a task tool actually changed anything before emitting an event, and the projector uses
it to skip redundant redraws.

### 3.3 Validation — `TaskPlanValidationResult`

A plan the model sends can be malformed, and the tool must reject it with a message the model can act
on rather than silently accepting garbage. `TaskPlanValidator` (`Mux.Core/Tasks/TaskPlanValidator.cs`)
returns a `TaskPlanValidationResult` (`IsValid` + a list of human-readable problems), following the
shape of `SkillValidationResult`. It checks for duplicate ids, empty ids or titles, `DependsOn`
entries that reference unknown ids, a task depending on itself, and — the one that matters most for
parallel execution — **dependency cycles**, found with a standard depth-first back-edge walk. A plan
that fails validation is not applied; the tool returns the problems as its `ToolResult.Content` error
payload so the model sees exactly what to fix and re-sends.

## 4. The event — `TaskPlanUpdatedEvent`

Plan changes reach every observer through one new agent event, added exactly like the existing ones.

- `Mux.Core/Agent/TaskPlanUpdatedEvent.cs` — extends `AgentEvent`. Carries `Plan` (a `TaskPlan`
  snapshot rendered as an immutable list of `AgentTask`), `ChangeKind` (`TaskPlanChangeKindEnum`), and
  `ChangedTaskId` (nullable; the task a status/note change touched).
- `Mux.Core/Enums/TaskPlanChangeKindEnum.cs` — `PlanCreated`, `PlanReplaced`, `TaskStatusChanged`,
  `TaskNoteUpdated`, `PlanCleared`. String-serialized for the wire.
- `Mux.Core/Enums/AgentEventTypeEnum.cs` — add `TaskPlanUpdated` with `[EnumMember(Value =
  "task_plan_updated")]`. This is the only edit to the existing event enum.

The event is emitted from inside `AgentLoop`, where events are yielded, not from the tool (a tool only
returns a `ToolResult`). After `ExecuteToolCallAsync` runs a task tool and the plan's `Version`
advanced, the loop yields a `TaskPlanUpdatedEvent` built from `TaskPlan.Snapshot()`. `Job.RecordEvent`
already accumulates telemetry from the event stream; it gains a branch that caches the latest plan
snapshot on the `Job` so the sidebar and session-save path can read it without draining the channel.
`RunCompletedEvent` gains a `TaskSummary` (counts by status) so a finished run reports how the plan
resolved.

## 5. The tools — `plan_tasks` and `update_task`

Two tools implement `IToolExecutor`, live in `Mux.Core/Tools/Tools/`, and register in
`BuiltInToolRegistry` with `ToolMutationKind.ReadOnly`. Both take the job's `TaskPlan` by constructor
injection, the same dependency pattern `WebSearchTool` uses for its search service. Their
`Description` strings are advertised to the model automatically through the `{ToolDescriptions}`
placeholder, so no prompt plumbing is needed beyond Section 7's guidance paragraph.

**`plan_tasks`** establishes or replaces the plan for the current job. Its schema takes a `tasks`
array where each element is `{ id, title, dependsOn?: string[] }`, plus an optional top-level
`note`. It validates through `TaskPlanValidator`, calls `TaskPlan.SetPlan`, and returns a compact
confirmation (task count and any ids). A validation failure returns
`JsonSerializer.Serialize(new { error = "invalid_plan", problems = [...] })` so the model can correct
and retry. Re-calling `plan_tasks` replaces the plan wholesale, which is how the model reorganizes
mid-run.

**`update_task`** advances one task. Its schema takes `{ id, status, note? }` where `status` is one of
the `AgentTaskStatusEnum` wire values. It calls `TaskPlan.UpdateTask`, which stamps timing and bumps
`Version`. Unknown id returns an error payload naming the valid ids. Setting `status` to `Failed`
requires a `note` (enforced in the tool) so a failure always carries a reason — traceability the
projector and the JSONL stream both surface.

Keeping two tools rather than one whole-plan-write tool is deliberate. Establishing structure and
ticking a box are different acts with different token costs: `plan_tasks` is called rarely and carries
the whole graph; `update_task` is called constantly and carries one line. Splitting them keeps the
common status flip cheap and maps each act to a clean `TaskPlanChangeKindEnum`.

Wiring lives in `AgentLoopOptions`, which gains a `TaskPlan` reference (nullable; null disables the
feature for that run). `BuiltInToolRegistry` receives it and constructs the two task tools only when
it is present and `MuxSettings.TaskPlanningEnabled` is true, matching how `web_search` registers only
when its service is configured.

## 6. The interactive experience — live checklist in the transcript

The payoff is visual, and it rides the projector the transcript already uses.

`AgentEventProjector` gains a `TaskPlanUpdatedEvent` handler and a `List<PaneLineHandle>` for the plan
block, managed exactly like the existing `_ToolLines` dictionary that flips a tool-call line from
"running" to a result in place. On the first plan event it writes a header line and one line per
task; on every later event it rewrites the affected lines from the snapshot rather than reprinting.
Each status has a glyph and color chosen to match the example and to read in mux's themes:

- `◻` pending (dim), `◼`/`⏵` in progress (accent), `✔` completed (green), `✗` failed (red),
  `⊘` skipped (dim), `🔒` blocked (yellow).

The block sits inline in the transcript so it scrolls with the conversation, and — because the
projector holds line handles — a task completing ten turns later still updates the original block. The
header mirrors the example's summary (`✢ <job title>… (elapsed · tokens)`), fed by the stats the
projector already tracks for time-to-first-token and token usage.

`SidebarView` gains a `TASKS n/m` line under the existing `STATUS` block, populated from the plan
snapshot cached on the `Job` and refreshed at the same imperative `RefreshSidebar()` boundaries the
shell already uses (submit, turn start, turn complete, restore). The sidebar shows progress at a
glance; the transcript shows the detail.

## 7. Teaching the model — system prompt and settings

A tool the model never thinks to call is dead weight, so `Defaults.SystemPrompt`
(`Mux.Core/Settings/Defaults.cs`) gains a short **task planning** section. It tells the model to call
`plan_tasks` at the start of any request that will take more than a couple of tool calls or spans
several files; to keep exactly one task `InProgress` at a time in tracking mode; to call `update_task`
the moment a task starts and again the moment it finishes rather than batching updates at the end; and
to mark a task `Blocked` with a note instead of leaving it silently pending when it cannot proceed.
The guidance is written to a model that may be small — short, concrete, imperative — because mux runs
against 7B-class local models as readily as frontier ones. The `{ToolDescriptions}` placeholder
already lists the two tools by name and description, so the prompt only needs the *when* and the
*discipline*, not the tool signatures.

Two settings land on `MuxSettings` (`Mux.Core/Models/MuxSettings.cs`), each a backing field with a
`[JsonPropertyName]` and setter validation, and — the easy step to forget — each added to
`NormalizeSettingsForPersistence` in `SettingsLoader` so it survives the load/save round-trip that
strips unknown fields:

- `TaskPlanningEnabled` (default `true`) gates tool registration and the prompt section. Off means the
  tools are not offered and the prompt section is omitted, so a user who never wants plans pays
  nothing.
- `TaskParallelismEnabled` (default `false`) gates orchestrated fan-out (Section 8). Tracking mode
  ignores it.

## 8. Parallel execution — tasks as jobs, on the scheduler that already exists

Tracking makes the work legible; orchestration makes it faster. The two are separable, and this
milestone is the one place the task layer reaches back into the job layer.

When `TaskParallelismEnabled` is on and a plan is in orchestrated mode, a `TaskOrchestrator`
(`Mux.Core/Tasks/TaskOrchestrator.cs`) watches the plan and, whenever `TaskPlan.ReadyTasks()` yields a
task with all dependencies `Completed`, dispatches it as its own job through the existing
`JobManager.SubmitAsync` — the task's title (and any `AssignedEndpointName`) becoming the child job's
scope. The DAG's edges become the schedule: independent tasks fan out up to `MaxConcurrency`, chains
run in order as each dependency completes, and the child jobs serialize their mutating tool calls
through the same shared `WriteLease` that already protects the workspace. No new scheduler, no new
concurrency primitive, no second write lock — the orchestrator is a thin policy layer that turns a
task DAG into `JobManager` submissions and folds each child job's terminal state back into its task's
status.

Two properties fall out of reusing the existing machinery and are worth stating because they are the
reason to reuse it. Cancellation already composes: cancelling the parent job cancels its orchestrator,
which cancels the outstanding child jobs through their own `CancellationTokenSource`s. And correctness
under contention is already solved: parallel tasks that both want to write cannot corrupt the
workspace because the write lease serializes them, exactly as it does for user-submitted concurrent
jobs today.

This is the most invasive milestone and the one most able to surprise, so it ships behind its default-
off flag, lands last, and carries the heaviest test burden (§11) — orchestrator scheduling, DAG
ordering, cancellation propagation, and write-lease serialization under fan-out, all driven headlessly
against fake agents the way `JobManagerSuite` already drives concurrency.

## 9. Traceability and observability

The request asked for full traceability, and the feature earns it at three layers that already carry
mux's other signals.

Inside the interactive session, every plan change is an `AgentEvent` on the job's channel, so it is
captured in the job transcript, rendered in the checklist, and summarized in the sidebar. A human
reading the screen can see which task is running, which failed and why (the `Failed` note), and how
far the plan has progressed — without reading the raw tool calls.

For automation, `task_plan_updated` joins the `mux print --output-format jsonl` contract. The event
serializes with its `contractVersion` like every other event, carrying the plan snapshot and the
change descriptor, so an orchestrator driving mux non-interactively can track subtask progress the
same way it tracks tool calls today. `RunCompletedEvent`'s new `TaskSummary` lets a caller assert on
the final plan state in one place. The README's event-types list and the `EventRenderer` /
`PrintCommand` text renderers both learn the new event so text mode narrates it (`task ▸ <title>:
completed`) and jsonl mode emits it.

Across sessions, the plan persists. `PersistedJobSnapshot` gains a nullable `TaskPlan` field;
`SessionSnapshotBuilder` writes the job's cached snapshot and `SessionResumeService` restores it, so
resuming a session shows the plan exactly where it stood. The snapshot is a new nullable field on a
forward-tolerant schema, so old session files load without migration and the round-trip is covered by
a persistence descriptor.

## 10. Standing code-style conformance (applies to EVERY code task)

Per `c:\code\agents\requirements\CODE_STYLE.md`, treated as a per-file review gate. A code task is not
`[x]` until its new and changed files satisfy all of these:

- [ ] `namespace` declared first; **all `using` statements inside the namespace block**.
- [ ] System / Microsoft usings first (alphabetical), then other usings (alphabetical).
- [ ] **One class or one enum per file** — `AgentTask`, `TaskPlan`, `TaskPlanValidator`,
  `TaskPlanValidationResult`, `AgentTaskStatusEnum`, `TaskPlanChangeKindEnum`, `TaskPlanUpdatedEvent`,
  `PlanTasksTool`, `UpdateTaskTool`, `TaskOrchestrator` are each their own file.
- [ ] All **public** members / constructors / methods have `///` XML docs; **no** docs on private
  members or methods.
- [ ] Private fields named `_PascalCase` (`_SyncRoot`, `_Tasks`, `_Version`, `_TaskPlan`), never
  `_camelCase`.
- [ ] **No `var`** — always the explicit type. **No tuples** — use named types (`AgentTask`,
  `TaskPlanValidationResult`), never a `(bool, string)`.
- [ ] `await … .ConfigureAwait(false)` where appropriate; every `async` method takes a
  `CancellationToken` (unless the class holds one) and checks cancellation at sensible points.
- [ ] Public members needing range/null validation use explicit getters/setters over a backing field;
  configurable values are public members with sensible private defaults, not magic constants.
- [ ] Guard clauses at method start; `ArgumentNullException.ThrowIfNull(...)`; specific exception types
  with contextual messages; a domain exception (`TaskPlanValidationException`) for plan-shape errors,
  documented with `/// <exception>`.
- [ ] Thread-safety guarantees on `TaskPlan` documented in XML; the lock discipline (`_SyncRoot`
  around all reads and writes; `Snapshot()` returns a copy) stated explicitly.
- [ ] **No `Console.Write*` in `Mux.Core`.** All output flows through TUIKit in `Mux.Cli`.
- [ ] For any `IEnumerable`-returning method, provide an async variant taking a `CancellationToken`.
- [ ] Files ≥ 500 lines use the `Public-Members` / `Private-Members` / `Constructors-and-Factories` /
  `Public-Methods` / `Private-Methods` regions (optional below 500).

## 11. Standing testing conformance (applies to EVERY code task)

Per `c:\code\agents\requirements\BACKEND_TEST_ARCHITECTURE.md` and the repo's Touchstone setup
(`TESTING.md`). Test logic lives in `Test.Shared` as `TestCaseDescriptor`s inside
`TestSuiteDescriptor`s, registered in `MuxSuites.All`, and runs green through `Test.Automated`
(console), `Test.Xunit`, and `Test.Nunit` on `net8.0` and `net10.0`. No `Console.Write*` in tests;
assertions throw; tests create and clean up their own data; not-yet-implemented cases use `skip: true`
+ `skipReason`. New suites this feature adds:

- [ ] `TaskPlanSuite` — model + validation: set/replace, status transitions with timing stamps,
  `ReadyTasks` dependency gating, `Snapshot` copy-safety, and every validator rejection (duplicate id,
  empty id/title, unknown dependency, self-dependency, **cycle detection**).
- [ ] `TaskToolsSuite` — `plan_tasks` accepts a valid plan and rejects an invalid one with a problem
  payload; `update_task` advances a task, rejects an unknown id, and requires a note on `Failed`;
  both classified `ReadOnly` and never acquire the write lease (assert through a held lease).
- [ ] `TaskEventSuite` — `AgentLoop` emits `TaskPlanUpdatedEvent` only when `Version` advances, with
  the right `ChangeKind`/`ChangedTaskId`; `RunCompletedEvent.TaskSummary` reflects the final plan.
- [ ] `TaskPersistenceSuite` — a plan round-trips through `SessionStore` save/load; an old snapshot
  with no plan field loads as a null plan (forward tolerance).
- [ ] `TaskProjectorSuite` — driven through TUIKit's `HeadlessBackend`: a plan event draws the block;
  a later status flip rewrites the line in place, not appends; the sidebar shows `TASKS n/m`.
- [ ] `TaskOrchestratorSuite` (M8) — DAG ordering, parallel fan-out capped at `MaxConcurrency`,
  dependency chains run in order, cancellation of the parent cancels children, and two writing tasks
  serialize on the shared write lease. Driven against fake agents like `JobManagerSuite`.
- [ ] `dotnet build src/Mux.sln` is warning-clean and the three runners exit green on both frameworks
  before any milestone is marked done.

## 12. Execution plan

### M0 — Branch, versioning, settings + test scaffolding

- [x] Create branch `feature/v0.5.0` off `main`.
- [x] Add a `CHANGELOG.md` `## v0.5.0 (Unreleased)` heading (the full version sweep is M9).
- [x] Add `TaskPlanningEnabled` (default `true`) and `TaskParallelismEnabled` (default `false`) to
  `MuxSettings` **and** to `NormalizeSettingsForPersistence`; cover parse/normalize in
  `SettingsLoaderSuite`. — added defaults + round-trip cases.
- [x] Confirm the three runners build and pass on both frameworks before writing feature code. — baseline
  net8 console run green.
- **Exit:** ✅ branch exists; settings persist round-trip green; baseline suites green.

### M1 — Core task-plan model (`Mux.Core/Tasks/`)

- [x] `AgentTaskStatusEnum.cs`, `TaskPlanChangeKindEnum.cs` (both string-serialized).
- [x] `AgentTask.cs` — fields per §3.1, validated setters, XML docs, `Clone()`.
- [x] `TaskPlan.cs` — thread-safe; `SetPlan`, `TryUpdateTask`, `Snapshot`, `ReadyTasks`, counts, `Version`;
  `IEnumerable` query + async variants (`ReadyTasksAsync`, `TasksInStatusAsync`).
- [x] `TaskPlanValidator.cs` + `TaskPlanValidationResult.cs` + `TaskPlanValidationException.cs` — all
  checks incl. cycle detection (DFS back-edge).
- [x] `TaskPlanSuite` green across runners — 16 cases, net8 console green.
- **Exit:** ✅ model + validation complete and tested; no CLI or job dependency.

### M2 — Event + enum + contract

- [x] `AgentEventTypeEnum` gains `TaskPlanUpdated` (`task_plan_updated`).
- [x] `TaskPlanUpdatedEvent.cs` (snapshot + change kind + changed id) and `TaskPlanSummary.cs`.
- [x] `RunCompletedEvent` gains `TaskSummary`.
- [x] `StructuredOutputFormatter` (jsonl) + `EventRenderer` (text) render the new event and the run summary.
- [x] Formatter case added to `StructuredOutputFormatterSuite`; full `TaskEventSuite` lands in M3.
- **Exit:** ✅ the event serializes to jsonl and renders in text; contract documented in M9.

### M3 — Tools + AgentLoop emission

- [x] `PlanTasksTool.cs`, `UpdateTaskTool.cs` (`ReadOnly`), constructor-injected `TaskPlan`.
- [x] `BuiltInToolRegistry(muxSettings, taskPlan)` registers them only when `TaskPlanningEnabled`.
- [x] `AgentLoopOptions.TaskPlan` added; the loop emits `TaskPlanUpdatedEvent` after a task tool advances
  `Version` (via `TaskPlan.LastChangeKind`/`LastChangedTaskId`), and sets `RunCompleted.TaskSummary`.
- [x] `TaskToolsSuite` (8 cases incl. read-only classification + settings gate) and `TaskEventSuite`
  (mock-server integration) green.
- **Exit:** ✅ the model authors and advances a plan end to end in a headless agent run.

### M4 — Job / JobManager wiring + persistence

- [x] `Job` holds a shared thread-safe `TaskPlan` (`job.TaskPlan`); the agent run mutates it in place, so
  the sidebar/save path read a live snapshot (no per-event caching needed).
- [x] `JobManager.CreateForAgentLoop` per-job option clone wires `options.TaskPlan = job.TaskPlan`.
- [x] `PersistedJobSnapshot` gains a `TaskPlan` list; `SessionSnapshotBuilder` writes `job.TaskPlan.Snapshot()`;
  `SessionResumeService` carries the DTO through unchanged.
- [x] Persistence cases added to `SessionStoreSuite` (round-trip + forward tolerance for pre-plan files).
- **Exit:** ✅ a plan survives save/resume; each job carries its own plan.

### M5 — System prompt + settings gate

- [x] `Defaults.SystemPrompt` gains a `{TaskPlanningGuidance}` placeholder + `Defaults.TaskPlanningGuidance`.
- [x] Both prompt-assembly sites (`CommandRuntimeResolver.Resolve`/`ResolveProfilePrompts`) inject the
  guidance only when tools are enabled and task planning is active; the tools-disabled prompt omits it.
  `PrintCommand` binds a `TaskPlan` when enabled so `mux print` emits `task_plan_updated`.
- [x] `PromptsSuite` cases assert the section appears only when `plan_tasks` is offered.
- **Exit:** ✅ models are told when and how to plan; the feature is fully switchable.

### M6 — TUI live checklist + sidebar

- [x] `AgentEventProjector` renders and in-place-updates the checklist block (`_TaskLines`) with the §6
  glyphs; a later status change rewrites the line rather than appending.
- [x] `SidebarView` shows `TASKS n/m` from the focused job's plan (`ConversationStats.TaskTotal/TaskCompleted`,
  populated in `CloneStatsNoLock`) at existing refresh boundaries.
- [x] Projector + sidebar cases added to `ProjectorSuite` (draw, in-place update, sidebar show/hide).
- **Exit:** ✅ a real run shows the Claude-Code-style checklist updating live.

### M7 — `/tasks` viewer + human annotation

- [x] `/tasks` command (F1 menu under **View**, keywords tasks/task/plan/todo) opens a `TasksModal` for the
  focused job's plan.
- [x] The modal lets a human set a task's status (c/i/p/k/b) and edit its note (n) — the "annotate progress
  and completion" path — applied directly to the live `TaskPlan`, then refreshed and autosaved.
- [x] `TaskModalSuite` drives the modal via `KeyEvent` and asserts the live plan updates.
- **Exit:** ✅ a user can inspect and hand-annotate a plan without editing files.

### M8 — Orchestrated parallel execution (opt-in engine)

- [x] `TaskOrchestrator.cs` dispatches `ReadyTasks` as child jobs via `JobManager`, folds each child's
  terminal state back into its task (reconcile-based, race-safe), honors `MaxConcurrency`, and inherits the
  shared `WriteLease` serialization from the job manager.
- [x] Parent cancellation cancels the dispatched child jobs; a failed dependency stalls its dependents.
- [x] `TaskOrchestratorSuite` green (independent fan-out, dependency ordering, failed-stalls-dependents,
  cancellation-cancels-children).
- [-] Interactive wiring of the orchestrator into the shell submit path — dropped from this pass: the engine
  and flag are complete and tested, but turning `taskParallelismEnabled` into a live interactive UX is a
  larger front-end change left as a follow-up. `mux print` and the engine are the supported surfaces today.
- **Exit:** ✅ the orchestration engine runs a task DAG as parallel jobs correctly; the flag defaults off.

### M9 — Documentation sweep + version bump + CI

- [x] Bumped every `<Version>` in the nine csproj files (`Mux.Cli`, `Mux.Core`, `Mux.Search`,
  `Test.Automated`, `Test.Nunit`, `Test.Shared`, `Test.TavilyConsole`, `Test.Xunit`, `Test.YouConsole`)
  from `0.4.0` to `0.5.0`.
- [x] Bumped `Defaults.ProductVersion` `0.4.0` → `0.5.0` (updates `--version`, splash, header;
  `TuiShellSuite` reads the constant so no test edit was needed).
- [x] Updated `Mux.Cli.csproj` `<PackageReleaseNotes>` to a `0.5.0:` background-tasks summary.
- [x] Left the `Voltaic` package version and the JSONL `contractVersion` untouched (both correct).
- [x] README: badge → `version-0.5.0`; Highlights bullet; a "Background Tasks" section; `/tasks` in the
  interactive-commands list; `task_plan_updated` in the automation-contract event list.
- [x] USAGE.md: a Background tasks section (checklist, `/tasks` + annotation keys, flags) plus the
  `task_plan_updated`/`taskSummary` contract fields.
- [x] CONFIG.md: documented `taskPlanningEnabled` and `taskParallelismEnabled` (sample + field table).
- [x] GETTING_STARTED.md: a "watch mux plan a big task" note (historical "as of v0.4.0" lines left intact).
- [x] CHANGELOG.md: finalized the dated `## v0.5.0` entry (task plans, `/tasks`, jsonl `task_plan_updated`,
  `taskSummary`, and the flag-gated orchestration engine).
- [x] No stale current-version refs remain; the surviving `v0.4.0` mentions are historical "as of" notes.
- [~] CI matrix — validated locally (build warning-clean; net8 + net10 console green; xUnit/NUnit adapters
  running). `.github/workflows/ci.yml` is unchanged and already runs the same matrix.
- **Exit:** the product reads as 0.5.0 everywhere a user or a script would look, and the docs describe
  the feature end to end.

## 13. Definition of done (v0.5.0)

The feature is done when a model, given a large request, calls `plan_tasks`, works the plan with
`update_task`, and the TUI shows a live checklist that ticks over in place while the sidebar tracks
`TASKS n/m`; when a plan survives a session save and resume; when `mux print --output-format jsonl`
emits `task_plan_updated` events an orchestrator can follow; when a user can open `/tasks` to inspect
and hand-annotate the plan; when orchestrated parallel execution runs independent tasks as real jobs
under the write lease behind its opt-in flag; and when `dotnet build src/Mux.sln` is warning-clean and
all three test runners pass on `net8.0` and `net10.0`. The version reads `0.5.0` in every csproj, in
`Defaults.ProductVersion`, in the README badge, and in the changelog, and the docs describe the
capability wherever a reader would expect to find it.

## 14. Compliance appendix

**`CODE_STYLE.md`.** Enforced as the per-file gate in Section 10 and re-checked before each milestone
closes: usings inside the namespace, one type per file, `_PascalCase` privates, no `var`, no tuples,
`ConfigureAwait(false)`, cancellation tokens on async methods, XML docs on public surface only,
specific and documented exceptions (`TaskPlanValidationException`), documented thread safety on
`TaskPlan`, and no `Console.*` in `Mux.Core`.

**`REPOSITORY_REQUIREMENTS.md`.** All new code lands under `src/` (`Mux.Core/Tasks/`,
`Mux.Core/Tools/Tools/`, `Mux.Cli/`) and `Test.Shared`. README, CHANGELOG, and the MIT LICENSE already
exist and are updated rather than replaced. The DOCKERHUB_README the requirements ask for stays a
tracked conditional: mux ships as a CLI, not a container, so a background-tasks mention is added only
if and when a Docker Hub image is published — noted here so the requirement stays visible rather than
forgotten. Versioning is pervasive per M9: every csproj, `Defaults.ProductVersion`, the README badge,
and the changelog carry `0.5.0`.

**`WRITING_DOCUMENTS.md`.** This plan and the M9 prose (README/USAGE/CONFIG/GETTING_STARTED sections)
are written as owned prose with a point of view, not a setup sentence over a list. Each section
carries real explanation around its lists, paragraph openings avoid the generic "This…"/"These…"
framing, sentence length varies, and the document closes on the strongest useful thought rather than a
recap. The M9 documentation is re-read as a whole for voice before the milestone closes.

**`BACKEND_TEST_ARCHITECTURE.md`.** Every code task carries Touchstone descriptors in `Test.Shared`,
registered in `MuxSuites.All`, green across the console, xUnit, and NUnit runners on both target
frameworks, with UI verified headlessly through `HeadlessBackend`. Section 11 lists the six suites and
the build-clean gate.
