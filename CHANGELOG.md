# Changelog

All notable changes to mux are documented here.

## v0.7.0 - 2026-08-07

### Added

- **Driver SDKs — TypeScript (`sdk/typescript`, `@mux/sdk`) and Python (`sdk/python`, `mux-sdk`).** Each
  spawns `mux print --output-format jsonl`, parses the event stream into typed events, and returns an
  aggregated result — with a `Mux` client, streaming and buffered runs, and multi-turn `Thread`s that
  persist through mux sessions (`--session-id`). They wrap the CLI rather than binding to internals, so
  they stay in lockstep with the versioned JSONL contract and any language can integrate the same way.
  Each ships with a hermetic test harness (a fake mux, no network or model) and a README.
- **Multi-turn stdin input (`--input-format jsonl`).** `mux print --input-format jsonl` reads a stream of
  turn records from stdin — one JSON value per line (`{"prompt":"..."}`, or `text`/`content`, or a bare
  string) — and runs each as a turn against the accumulating conversation, so turn N sees turns 1..N-1.
  Output follows `--output-format` per turn; `--output-last-message` captures the final turn; a session
  flag persists the whole conversation as one session; and MCP servers connect once and are shared across
  turns. Malformed records are reported and skipped without ending the stream.
- **Headless MCP for `print`.** `--mcp-config <path|json>` connects MCP servers for a single `print` run —
  reusing the same runtime as the interactive shell — waits (bounded) for tool discovery, exposes the
  discovered tools to the model, and disposes the connections when the run ends. `--strict-mcp-config`
  uses only the flag's servers, ignoring `mcp-servers.json`. MCP stays off unless `--mcp-config` is given,
  so a plain `mux print` remains hermetic; the active state is reported on `run_started.mcp`.
- **`--output-schema <path>` for `print`.** Folds a JSON Schema directive into the prompt and validates the
  final response against it recursively — `type` (including union type arrays and `integer`), `enum`,
  `required`, nested `properties`, and array `items` — reporting the first violation with a JSON path.
  Value-level bounds/patterns/formats are not enforced, and validation is client-side (mux's LLM layer,
  PolyPrompt, exposes no `response_format` field, so provider-native structured output is not available);
  the approach is backend-agnostic. Code fences are unwrapped; a non-conforming response fails with
  `schema_validation_failed` (exit `1`) and suppresses the artifact and `json` summary.
- **Tool governance and a confinement posture.** `--allow-tools`/`--deny-tools` take comma-separated
  tool-name globs (`*`/`?`) to restrict which tools a run may use — deny always wins, and an excluded tool
  is neither advertised to the model nor allowed to execute. `--sandbox` adds an application-level posture:
  `read-only` refuses every mutating tool, and `workspace-write` confines built-in file writes to the
  working directory plus any `--add-dir` roots (repeatable), refusing writes that escape them. Refusals use
  the `tool_call_denied` error code (exit `2`), and the active posture is reported as `sandboxPosture` on
  `run_started`. This is a mux-level policy over the built-in tools, not an OS sandbox: `run_process` and
  external MCP subprocesses are not kernel-confined and remain gated by the approval policy. All four flags
  apply to `mux print` and to the interactive shell at launch.
- **Headless session continuity.** `mux print` can now resume prior work non-interactively. `--resume
  <id|title>` continues a persisted session, `--continue` picks up the most recently updated one,
  `--session-id <id>` runs under a specific id (creating it if absent), `--fork-session` saves the
  resumed run under a new id, and `--no-session-persistence` runs without writing to disk. Persistence
  is opt-in — a plain `mux print` stays stateless — and print sessions share the one session store with
  the interactive `/sessions` browser, so a session started in either surface can be continued in the
  other. The resolved session id is now carried on the `run_started` and `run_completed` events (and in
  the `json` run summary) so an orchestrator can capture it from one run and pass it to the next.
- **Single-object `json` output for `print`.** `mux print --output-format json` emits one summary object
  at the end of the run — `result`, `status`, `sessionId`, `iterationsCompleted`, `toolCallCount`,
  `errorCount`, `durationMs`, `finalEstimatedTokens`, `compactionCount`, optional `taskSummary`, and
  `contractVersion` — with the same secret redaction as the `jsonl` stream. The streaming `jsonl` form is
  unchanged; `text` remains the default.
- **`--max-turns <int>`.** Overrides the agent loop iteration cap for a single run (clamped to 1-100),
  without editing `settings.json` or the endpoint.
- **`--append-system-prompt <text>`.** Appends text to the resolved system prompt after all placeholder
  substitution, so it survives profile switches. Complements the existing `--system-prompt <path>`.
- **`--max-token-budget <int>` and `maxTokenBudget` in `settings.json`.** A backend-agnostic ceiling on
  the estimated working-context tokens: when the estimate exceeds the budget before a model call, the run
  stops cleanly with a `budget_exceeded` error and a matching `run_completed` status rather than
  continuing. This is mux's provider-neutral analogue of a spend cap; it is based on mux's token estimate,
  not a provider billing figure.

### Fixed

- **Version string.** `mux --version` now reports the current release; the compiled `ProductVersion`
  constant had lagged the changelog.

## v0.6.0 - 2026-08-04

### Added

- **HTTP MCP server authentication.** When configuring an HTTP MCP server through `/mcp`, you can now
  choose an auth scheme — none, a bearer token, or an API key sent in a caller-specified header
  (default `X-API-Key`). The credential is persisted in `mcp-servers.json` under the server's `auth`
  object and attached to every request the client makes to that server (via `SetRequestHeader`),
  including the connection handshake. Token and API-key values support `${VAR}` environment-variable
  expansion, so secrets can be referenced from the environment instead of stored in plaintext. Secret
  fields are masked in the form.

### Fixed

- **The MCP server form no longer mixes transports.** The add/edit form now shows only the selected
  transport's fields — command / args / env for `stdio`, or url / mcp-path / auth for `http` — so
  switching transports no longer leaves the other transport's stale values on screen. Only the fields
  relevant to the chosen transport are collected and persisted.

## v0.5.0 - 2026-07-31

### Added

- **Background tasks** — the model can decompose a job into a tracked plan of tasks and advance them
  as it works, and the interactive shell renders a live checklist that updates in place (pending →
  running → done). Two model tools, `plan_tasks` and `update_task`, are the write path; the plan is
  per-job, persists across session save and resume, and is summarized in the sidebar as `TASKS n/m`.
  Open `/tasks` to inspect and hand-annotate the focused job's plan. Gated by `taskPlanningEnabled`
  (default true).
- `task_plan_updated` joins the `mux print --output-format jsonl` event contract so orchestrators can
  track subtask progress, and `run_completed` carries a `taskSummary` tally.
- Orchestration engine (`TaskOrchestrator`) that runs a task DAG as parallel jobs under the shared
  workspace write lease, gated by `taskParallelismEnabled` (default false). The engine is complete and
  tested; wiring it into the interactive submit path is a planned follow-up.

## v0.4.0 - 2026-07-31

### Added

- **Skills** — versioned Markdown-plus-code capabilities in `~/.mux/skills` that turn a request into a
  fixed, deterministic procedure. Each skill is a folder with a `SKILL.md` (YAML frontmatter plus a body)
  and optional bundled scripts; its commands run a fenced code block or a script through an allowlisted
  interpreter (`bash`, `sh`, `pwsh`, `python`, `node`, `dotnet-script`) with a timeout and captured output.
  On startup the interactive shell discovers skills, lists the enabled ones in the system prompt, and
  exposes two tools — `skill` (read a skill's instructions) and `run_skill` (execute a command, returned
  like `run_process` and gated by the approval policy and write lease). A curated library of 46 default
  skills — spanning git/GitHub, .NET build and quality, repository hygiene, scaffolding, documentation, and
  developer workflow — is seeded on first run and, on upgrade, tops up an existing `~/.mux/skills` with any
  newly shipped defaults (tracked in a `.seeded-defaults` manifest) without resurrecting ones you deleted or
  overwriting your edits. Manage skills in-app with `/skills`
  (inventory with status glyphs, per-skill view/in-app edit/enable/disable/duplicate/remove, a create
  wizard, and local-path import) or with the `mux skill list | show | validate | run | new | add` verb;
  `validate` returns a nonzero exit for CI and `run` returns the process contract for hooks. Caller
  arguments reach the interpreter as separate argv entries with no shell, so shell metacharacters cannot
  inject. New `settings.json` fields: `skillsEnabled`, `skillRefreshIntervalSeconds`, `skillsDirectory`.

- **MCP servers are now connected live in the interactive shell.** On startup mux connects to every server
  configured in `mcp-servers.json`, queries each for its available tools (`tools/list`), and then both
  registers those tools as callable (routing invocations back to the owning server) and appends them to the
  system prompt so the model is explicitly made aware of them. Connectivity is re-validated on a periodic
  timer (default 30s) and disconnected servers are periodically retried; adding, editing, or removing a
  server via `/mcp` reconnects in the background and takes effect on the next turn without a restart.
- The `/mcp` manager now shows each server's live connectivity: `●` online (with its discovered tool
  count) or `○` offline.

- **Import completion models from an Ollama server** directly in the interactive endpoints / models
  picker (`Ctrl+E`, `/endpoint`, `/model`). A new **Import from Ollama…** entry at the bottom of the
  picker prompts for an Ollama base URL (default `http://localhost:11434`; a missing scheme is filled in
  and a trailing `/v1` or slash is stripped to Ollama's native API root), queries the server's installed
  models via `GET /api/tags`, and presents a scrollable multi-select checklist of what it finds. `Space`
  toggles the highlighted model, `a` toggles all, `Enter` imports the checked models, and `Esc` cancels.
  Ollama's model list does not classify models as completion vs. embedding, so every installed model is
  listed with an on-screen reminder to select completion models only (leave embedding models unchecked).
  Model-plus-endpoint combinations already present in `endpoints.json` are excluded from the checklist so
  re-imports never create duplicates, and each selected model is saved as a new `ollama` endpoint at the
  normalized base URL, uniquely named after the model.
- `Mux.Core.Utility.OllamaModelLister` — a small utility that normalizes an Ollama base URL and lists a
  server's installed model names from `GET /api/tags`.
- **Optional boundary lines** in the interactive shell, off by default. A new `/borders` command (aliases
  `/boundaries`, `/lines`; also on the `F1` menu under **View**) toggles dark-grey rules: a horizontal
  rule above the prompt input, a horizontal rule above the queued-messages strip (when shown), and a
  vertical rule in the gutter left of the sidebar. The choice persists to `settings.json` as the new
  `showBoundaryLines` field (default `false`) and is applied on the next launch.
- **Interactive MCP-server management** via a new `/mcp` command (aliases `/mcp-servers`, `/servers`;
  also on the `F1` menu under **Model**). It opens a picker over the servers configured in
  `mcp-servers.json` with **Add**, **Edit** (select a server row), and **Remove** (with confirmation)
  actions. The add/edit form collects a name, a transport (`stdio` or `http`), and the transport-specific
  fields — command / space-separated args / comma-separated `KEY=VALUE` env for `stdio`; url / mcp path
  (default `/mcp`) for `http` — validating that `stdio` has a command and `http` has a url. Changes
  persist to `mcp-servers.json`, with a notice that mux must be restarted for them to take effect.

### Changed

- The endpoints / models picker now renders a blank separator row between the configured endpoints and
  the management actions (**Add endpoint**, **Edit endpoint**, **Remove endpoint**, and
  **Import from Ollama…**).
- The interactive shell now keeps a one-column gutter between the transcript (and queue strip) and the
  right-anchored sidebar, so the two panes no longer butt directly against each other.
- Introduced an external-tool provider seam (`IExternalToolProvider` on the agent loop) so MCP servers and
  skills compose their tools and prompt sections instead of contending for a single hook.
- Renamed the **Theme…** command menu entry to **Theme**.
- `/help` and `/?` now open the same navigable command menu as `F1` (previously a static, non-interactive
  list), and that menu now shows each command's `/slash` aliases alongside its title and key chord.
- Widened the `F1` / `/?` command menu by ~50% and column-aligned it so the key-chord column and the
  `/slash` alias column each line up on a single vertical axis.

### Fixed

- HTTP (streamable) MCP servers no longer fail to connect against spec-compliant servers. The bundled MCP
  client (Voltaic `0.1.11`) sent `Accept: application/json` on its streamable-HTTP requests; servers that
  require the MCP-mandated `Accept: application/json, text/event-stream` (returning `406 Not Acceptable`
  otherwise) were shown as offline with no tools. Upgraded Voltaic to `0.4.0`, which sends the compliant
  `Accept` header (verified end-to-end against a real server discovering all of its tools).

## v0.3.0 - 2026-07-29

This release rebuilds the mux interactive front-end on
[TUIKit](https://www.nuget.org/packages/TUIKit), introduces a concurrent background-job model, and
migrates the test suite to [Touchstone](https://www.nuget.org/packages/Touchstone.Core).
`Spectre.Console` has been removed entirely. `mux print`, `mux probe`, and `mux endpoint` are
unaffected.

### Added

- Concurrent job engine in `Mux.Core` (UI-free, headless-tested): a `JobManager` + scheduler runs
  multiple prompts as background jobs, a fair single-writer `WriteLease` allows parallel reads while
  serializing file-mutating tools ("parallel reads, single writer"), per-job approval routing, and
  atomic session persistence.
- Rebuilt the interactive UI on TUIKit: `mux` with no non-interactive command launches the `MuxTuiApp`
  shell — a sidebar / transcript / composer / footer layout with a streaming `AgentEvent` projector.
  `Ctrl+Q` quits, `Ctrl+L` clears, `Ctrl+N` cycles jobs, `Esc` cancels the focused job, double-tap
  `Ctrl+C` exits. The terminal backend is injected so the shell is driven headlessly in tests.
- Each job renders into its **own** transcript pane; only the focused pane is shown, so concurrent jobs
  never write over one another. The projector renders assistant text as markdown at block boundaries
  and collapses each tool call to a single line updated in place from `running…` to `✓/✗ name (N ms)`.
- A sidebar lists all jobs with a state glyph and focus marker, kept live from `JobManager` events;
  focus by number (`Alt+1`–`Alt+9`) or `Ctrl+N`, toggle with `Ctrl+B`, and it auto-collapses below 100
  columns.
- Multi-line composer with prompt history (`Up`/`Down`) and an enqueue-while-busy chooser: `Enter`
  submits (opening the chooser when a job is active — start a new job, append to the focused job, or
  remember the choice), `Alt+Enter`/`Shift+Enter` insert a newline, `Ctrl+Enter` bypasses the chooser.
  The default is read from `settings.json` (`defaultEnqueueBehavior`).
- Command surfaces over a single catalog — a `/`-slash router, key bindings, and a catalog-derived menu
  (`F1`) — all resolving to the same handlers; `/help` lists commands and keys, and the footer shows
  live `jobs/focused` status.
- Interactive tool approval: read-only tools run automatically and mutating tools prompt with an
  approval modal (Approve once / Deny / Always this session). A jobs modal (`F2` / `/jobs`) lists jobs
  and focuses the one you pick.
- Session persistence: the session autosaves at each turn boundary and can be saved with `Ctrl+S` /
  `/save`; `/sessions` browses and resumes saved sessions. Restored sessions render completed
  conversations read-only and mark interrupted jobs as re-run-required (never auto-running them), and
  prompt history survives a restart.
- Appearance and input: pick a theme from a selector (`/theme`) — the whole UI, including the panes
  behind the text, conforms to the chosen theme — open the endpoints / models picker with `Ctrl+E`, and
  toggle mouse capture (`F12` / `/mouse`), which is on by default.
- Built-in `web_retrieve` tool for fetching rendered URL content through headless Playwright Chromium or
  Firefox, with browser installation handled on demand.
- External web search through the `web_search` tool, configurable with Tavily and You.com providers from
  `settings.json` or the interactive `/search` wizard.
- `mux endpoint list` and `mux endpoint show <name>` as top-level non-interactive commands, including
  machine-readable `json` output with redacted secret-like header values.
- `--ignore-cert-errors` (with `--insecure` alias), `settings.json` field `ignoreCertErrors`, and
  `MUX_IGNORE_CERT_ERRORS` for bypassing TLS certificate validation behind enterprise TLS inspection.
- Nullable endpoint-scoped `maxAgentIterations` overrides, with inherited global defaults and additive
  endpoint inspection metadata.
- `settings.json` fields `maxConcurrency` (1–32) and `defaultEnqueueBehavior`
  (`ask`/`run_now`/`queue_after`/`add_to_focused`) for the interactive job model.
- `ARMADA.md` integration guide for orchestrator consumers, plus a tightened `ARMADA_IMPROVEMENTS.md`.
- A narrow mux-owned command dispatcher/parser replacing `Spectre.Console.Cli`.
- `TUIKit` `0.2.0` as the rendering dependency for `Mux.Cli`.

### Changed

- Migrated the entire test suite from the bespoke `TestSuite`/`TestRunner` framework to Touchstone
  runner-agnostic descriptors, executed through a console runner (`Test.Automated`), xUnit
  (`Test.Xunit`), and NUnit (`Test.Nunit`), all on `net8.0` and `net10.0`.
- Endpoint configs can persist `autoApproveTools`, and interactive `always` approvals save
  endpoint-scoped auto-approval for future sessions.
- Agent-loop iteration limits default to `50` and resolve from endpoint `maxAgentIterations` when set,
  otherwise from `settings.json.maxAgentIterations`, with a `1-100` clamp in both places.
- Interactive `/model` aliases `/endpoint` (`list`, `<name>`, `show`, `add`, `edit`, `remove`).
- `mux print` supports `--output-last-message <path>` to write only the final assistant response text;
  failed runs leave the file absent.
- `mux print`, `mux probe`, and `mux endpoint` support `--config-dir <path>` as a first-class
  config-root override, with precedence over `MUX_CONFIG_DIR`.
- `mux probe --require-tools` fails when the selected endpoint disables tool calling.
- Human-readable `print`, `probe`, and interactive errors suggest `--insecure` on a self-signed
  certificate-chain failure while certificate validation is enabled.
- `/mcp add` runs a wizard-driven workflow supporting both `stdio` and HTTP MCP transports, saving to
  `mcp-servers.json`.
- `/endpoint <name>` switches only to configured endpoint names and refreshes endpoint-dependent tool
  guidance after a successful switch.
- README, `USAGE.md`, `CONFIG.md`, and `TESTING.md` updated for the TUIKit UI, the concurrent job/queue
  model, sessions, the new settings, and the three-runner headless test architecture.

### Removed

- Removed direct and transitive `Spectre.Console` usage from `Mux.Cli`, including `Spectre.Console.Cli`.
- Tore down the legacy hand-rolled interactive renderer (`InteractiveCommand`, cursor/chrome layout,
  `LineBuffer`, prompt history, paste heuristics — 11 files) and replaced it with the TUIKit-based
  `MuxTuiApp` shell.
- Removed the previous REPL's queued-message support, `/queue` commands, and `Alt+Up` queued-prompt
  editing (superseded by the concurrent job model and enqueue chooser).
- Removed the interactive `/endpoint <name>` model-override fallback; unknown endpoint names now produce
  an error and leave the selected endpoint unchanged.

### Testing

- The interactive shell is fully covered by deterministic headless suites driven through TUIKit's
  `HeadlessBackend` (no real terminal, no live LLM): shell, projector, sidebar, composer/chooser,
  command surfaces, modals, persistence, rendered-frame golden snapshots, and polish — 335 passing
  checks (plus 7 documented skips) across the console, xUnit, and NUnit runners on `net8.0` and
  `net10.0`, with positive and negative cases throughout.
- Added engine coverage for the job manager/scheduler, the write lease, approval routing, and session
  save/load/resume.
- Added `Test.Xunit` coverage for `--config-dir`, `--output-last-message`, `probe --require-tools`,
  `endpoint list/show`, endpoint-scoped max-iteration persistence/clamping, self-signed certificate-chain
  hint detection, `web_retrieve`, external-search registration, and endpoint-switch no-fallback behavior.
- Added Armada-style `Test.Automated` contract coverage for isolated config directories and endpoint
  inspection.
- Added a GitHub Actions workflow that builds `src/Mux.sln` and runs all three test runners on Linux and
  Windows.

## v0.2.0 - 2026-04-24

### Added

- Interactive endpoint management via `/endpoint list`, `/endpoint show <name>`, guided `/endpoint add` and `/endpoint edit <name>` workflows, and confirmed `/endpoint remove <name>` so endpoints can be inspected and maintained from within mux itself
- Interactive queued-message support in the REPL so users can keep drafting while mux is busy and queue the next prompt with `Tab`
- `/queue`, `/queue clear`, `/queue drop-last`, and `/queue resume` interactive commands for queue inspection and control
- `/status`, `/compact`, and `/title` interactive commands for session inspection, history compaction, and direct title control
- `/compact summary` and `/compact strategy [summary|trim]` so compaction policy can be overridden per command or changed for the live interactive session
- `/compact trim` for explicit trim-only history compaction without a summary-model sidecar call
- `/context` as an interactive alias for `/status`
- `Alt+Up` editing for the newest queued prompt during interactive sessions
- Inline interactive status above the prompt for busy, paused, and approval states
- Automatic conversation-title tracking in interactive mode, including `Conversation title update: ...` transcript notices when the model revises the title
- Estimated context-budget reporting for system prompt, persisted history, tool surface, remaining budget, and compaction metadata
- Compaction-related settings in `settings.json` for automatic preflight compaction, warning threshold, strategy, and preserved turns

### Changed

- `Esc` now cancels the active interactive generation without exiting mux
- Cancelling or failing an interactive run pauses queued-message auto-dispatch until the user resumes it
- Interactive `/clear` now redraws the screen with the current conversation title at the top
- Interactive streamed output now keeps the next `mux>` prompt off the response line and preserves exactly one blank spacer line before the prompt, including when output reaches the bottom of the terminal
- Interactive runs now check the pending prompt against the estimated context budget before starting and automatically compact older history when needed
- `--compaction-strategy <summary|trim>` now overrides the effective compaction policy for interactive, print, and probe startup
- Interactive mode now emits a low-noise post-turn context notice only when the session is approaching or over the usable context budget
- `AgentLoop` now honors the configured compaction strategy for oversized active conversation state before model calls and emits additive `context_status` / `context_compacted` JSONL events plus extended context metadata on `run_started` / `run_completed`
- Non-streaming LLM calls now build non-streaming backend requests, which stabilizes `probe` and the new model-driven title/compaction sidecar calls
- Interactive help and README documentation now describe queueing, cancellation, and the inline status-line behavior

### Testing

- Added endpoint command parser and endpoint persistence unit coverage
- Added `QueuedMessageManager` unit coverage for FIFO dequeue, newest-item editing/removal, and queue clearing
- Verified with `dotnet test src\Mux.sln --nologo`

## v0.1.0 - 2026-03-31

### Added

- Structured CLI output for orchestration with `mux print --output-format jsonl`
- New lifecycle events: `run_started` and `run_completed`
- `mux probe` command for config and backend health validation
- Machine-readable `json` output for `mux probe`
- Best-effort redaction for secret-like values in structured event payloads
- Documentation for the orchestration contract, output formats, exit codes, and `MUX_CONFIG_DIR`
- Shared `contractVersion` marker across `print` JSONL events and `probe` JSON payloads

### Changed

- `mux print` now has a formal non-interactive contract with documented exit codes
- `mux print` `error` events now expose `errorCode`, `failureCategory`, and runtime metadata when known while remaining backward compatible with existing `code` consumers
- Named endpoint selection now fails explicitly when `--endpoint` references a missing endpoint
- CLI approval parsing now accepts documented values `ask`, `auto`, and `deny`
- Tool-call argument parsing is more tolerant of malformed Windows-style path escaping

### Testing

- Expanded `Test.Xunit` coverage for structured formatting, CLI command output, and config resolution
- Expanded `Test.Automated` coverage for lifecycle events, JSONL output, and probe output
- Stabilized mock-server route matching and process-test cleanup behavior

## 2026-03-30

Initial alpha release.
