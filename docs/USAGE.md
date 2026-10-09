# mux Usage Guide

This file focuses on practical usage patterns, backend examples, and orchestration scenarios.

## Common Command Patterns

Interactive:

```bash
mux
mux --endpoint ollama-qwen32
mux --model codellama:34b
mux --prompt "summarize README.md"   # skip the splash and run this prompt, then stay interactive
```

Single-shot:

```bash
mux print --yolo "add error handling to ParseConfig"
mux print --yolo --endpoint openai-gpt4o "explain the architecture"
mux print --output-last-message result.txt --yolo --endpoint openai-gpt4o "explain the architecture"
echo "refactor AuthService" | mux --print --yolo
```

Structured automation:

```bash
mux print --output-format jsonl --yolo "implement the feature described in TASK.md"
mux print --ignore-cert-errors --output-format jsonl --yolo "run behind enterprise TLS inspection"
```

Health checks:

```bash
mux probe
mux probe --output-format json
mux probe -e vllm-deepseek
mux probe --output-format json --require-tools -e vllm-deepseek
```

Endpoint inspection:

```bash
mux endpoint list --output-format json
mux endpoint ls --output-format json
mux endpoint show openai-prod --output-format json
```

Interactive endpoint management:

```bash
/endpoint
/model
/endpoint ls
/model ls
/endpoint show openai-prod
/model show openai-prod
/endpoint add
/model add
/endpoint edit openai-prod
/model edit openai-prod
/endpoint remove old-endpoint
/endpoint delete old-endpoint
/endpoint rm old-endpoint
/model remove old-endpoint
/model delete old-endpoint
/model rm old-endpoint
```

External search management:

```bash
/search
/search ls
/search add
/search show tavily-primary
/search edit tavily-primary
/search remove tavily-primary
/search delete tavily-primary
/search rm tavily-primary
```

Notes:
- `/endpoint`, `/endpoint list`, `/endpoint ls`, `/model`, `/model list`, and `/model ls` show the configured endpoints and highlight the active session endpoint
- `/model` is an alias for `/endpoint` and supports the same `<name>`, `show`, `add`, `edit`, and `remove`/`delete`/`rm` forms
- `/endpoint show <name>` runs a lightweight connectivity probe and reports whether the endpoint is reachable
- `/endpoint add` launches a guided creation wizard that prompts for the adapter, base URL, model, auth mode, default status, endpoint-scoped tool auto-approval, and optional advanced settings before probing and saving
- `/endpoint edit <name>` launches the same guided workflow for an existing endpoint; editing the active endpoint clears the current conversation state after the update is saved
- Endpoint configs can persist `autoApproveTools: true` so tool calls auto-approve whenever that endpoint is active unless CLI approval flags override it
- Endpoint configs can set nullable `maxAgentIterations`; when set it overrides `settings.json.maxAgentIterations` for that endpoint, and when omitted or `null` it inherits the global default
- Auth modes are `none`, `bearer token`, and `custom headers`; for auth values you can store either a discrete value in `endpoints.json` or an environment-variable reference
- The wizard accepts bare environment variable names plus `${VAR}`, `%VAR%`, `$VAR`, and `$env:VAR`, then stores environment references canonically as `${VAR}`
- `/endpoint remove <name>`, `/endpoint delete <name>`, and `/endpoint rm <name>` ask for confirmation and refuse to remove the endpoint currently active in the session; switch first if you need to delete it
- `/search` and `/search list` show global external-search status and configured providers
- `/search ls` is an alias for `/search list`
- `/search add [name]` configures Tavily or You.com and enables `web_search` when the provider is usable
- `/search show`, `/search edit`, and `/search remove`/`delete`/`rm` inspect and maintain stored search providers

## Interactive UI (TUIKit)

Running `mux` with no non-interactive command launches the TUIKit shell.

> **Note:** As of v0.4.0 the interactive shell connects to configured MCP servers, discovers their tools,
> exposes those tools to the model, and shows per-server connectivity; manage servers with `/mcp` or in
> `mcp-servers.json`.

The screen has a single transcript holding the whole conversation, a sidebar showing the active
endpoint and per-turn / session telemetry (status, timings, token counts), a multi-line composer, and
a footer with key hints. It behaves like a chat client: you type a prompt and it runs; typing another
while a turn is in flight queues it to run when the current turn finishes.

### Keys

| Key | Action |
|---|---|
| `Enter` | Submit the prompt (queued if a turn is already running) |
| `Alt+Enter` / `Shift+Enter` | Insert a newline in the composer |
| `Up` / `Down` | Recall prompt history at the composer edges |
| `Esc` | Cancel the running turn (or dismiss a modal) |
| `Ctrl+B` | Toggle the sidebar (auto-collapses below 100 columns) |
| `Ctrl+E` | Open the endpoints / models picker |
| `Ctrl+L` | Clear the transcript |
| `Ctrl+S` | Save the session |
| `F1` | Command menu |
| `F12` | Toggle mouse capture (on by default; toggle off to hand the mouse back for native selection) |
| `Ctrl+Q` / double `Ctrl+C` | Quit |

### Slash commands

Type a leading `/` in the composer to run a command instead of submitting a prompt. Every command is
also reachable by key and the menu (one catalog, three surfaces):
`/endpoint` (`/model`), `/effort` (`/reasoning`), `/settings` (`/config`, `/preferences`, `/prefs`),
`/help` (`/?`), `/clear`, `/sidebar`, `/save`, `/export` (`/share`), `/undo`, `/redo`, `/sessions`,
`/label` (`/labels`), `/tag` (`/tags`), `/tasks`, `/usage` (`/stats`, `/spend`), `/theme`, `/mouse`,
`/menu`, `/quit` (`/exit`), `/trust`, `/loop`, `/loops`, `/processes` (`/ps`). Any custom commands from `hooks.json` also appear here as `/<name>`.
Anything else that names an enabled skill runs that skill: `/code-review main` submits the skill's
instructions with `main` as its arguments (see [Skills](#skills)). Built-in commands win, then custom
commands, then skills.

Key chords for these commands can be rebound in `~/.mux/keybindings.json` — see
[CONFIG.md](CONFIG.md#keybindingsjson-custom-key-chords). `/help` lists the current command ids.

`/theme` opens a theme selector (pick a theme and apply it); the whole UI — including the panes behind
the text — conforms to the chosen theme.

`/effort` (`/reasoning`) opens a reasoning-effort picker — Off, Minimal, Low, Medium, High — for the
active endpoint. The choice persists to `endpoints.json` and applies to the next turn, and the sidebar's
`EFFORT` line shows the active level. Selecting a level drives provider-appropriate defaults: OpenAI
`reasoning_effort`, a Gemini thinking budget, or the Ollama `think` value. Headless runs set it with
`--effort <off|minimal|low|medium|high>` and tune the per-provider values with `--effort-openai-value`,
`--effort-gemini-budget`, and `--effort-ollama-think`. Per-endpoint tuning is also available in the
endpoint Add/Edit form (a **Reasoning effort** field plus an advanced **Gemini thinking budget** field).

`/thinking` (`/think`) toggles whether the model's reasoning ("thinking") is displayed for the active
endpoint. The choice is a per-endpoint property (`showThinking`), persists to `endpoints.json`, and applies
to the next turn; the sidebar's `THINK` line shows `on`/`off`, and the endpoint form has a **Show thinking
(reasoning)** checkbox. When on, thinking streams into the transcript under a dim `💭 thinking` header,
kept separate from the answer and never fed back to the model. Headless runs surface it with
`--show-thinking`: as `assistant_thinking` events in `jsonl`, or on stderr in `text` mode so stdout stays
the answer.

`/settings` (`/config`, `/preferences`, `/prefs`) opens a global settings editor over `settings.json`.
It covers the agent-loop run limits — **max agent iterations** (the 1–100 cap on model turns per run; each
iteration may issue several tool calls) and an optional **max token budget** — plus max concurrency, the
default approval and enqueue behaviors, tool/process timeouts, the context and compaction tuning, the
skills and task-planning toggles, and the cert-error and boundary-line flags. Save (Enter) persists to
`settings.json`; the run-affecting values (iteration cap, token budget, compaction, context tuning, cert
errors) apply on the next turn, while concurrency and startup-only wiring apply on the next launch. This
editor sets the **global** defaults — a per-model iteration override lives in the endpoint Add/Edit form
(the **Max agent iterations (blank = global)** field) and wins over the global value for that endpoint.

`/help` (`/?`) opens the keybinding/command reference in a modal; `F1` opens the command menu (the same
catalog as a pick-and-run list). On startup mux shows a splash box — pass `--prompt "<text>"` (or a bare
positional prompt) to skip the splash and submit that prompt as the first turn before dropping into the
usual interactive shell. Quitting (`Ctrl+Q` / `/quit`) asks for confirmation.

### Choosing, adding, and removing models/endpoints

`/endpoint` (alias `/model`) opens a modal listing your configured endpoints; pick one to switch the
active endpoint for subsequent prompts. The same modal offers **+ Add endpoint…** (a short wizard for
name, adapter type, base URL, and model) and **- Remove endpoint…** (with a confirmation) — both persist
to `endpoints.json`. You can still select an endpoint at launch with `--endpoint <name>` or an ad-hoc
`--base-url`/`--model`/`--adapter-type`, and inspect with `mux endpoint list` / `mux endpoint show`.

### Queueing prompts

Submitting while a turn is running queues the new prompt; queued prompts run in order as each turn
finishes, like a chat client. The sidebar shows the current status and how many prompts are queued.

### Tool approval

Under the default policy, read-only tools run automatically and mutating tools prompt with an approval
modal (Approve once / Deny / Always this session). Use `--yolo` (or `--approval-policy auto`) to
auto-approve, or `--approval-policy deny` to block all tools.

### Sessions

The session autosaves at each turn boundary. `Ctrl+S` / `/save` saves on demand; `/sessions` lists and
resumes saved sessions (under `~/.mux/sessions`). A resumed session shows the completed conversation
read-only and marks an interrupted turn as re-run-required — it never silently re-runs it.

### Labels and tags

Attach metadata to a session so you can group and filter it later. **Labels** are freeform strings
(`wip`, `customer-acme`); **tags** are `key: value` pairs (`env: prod`, `sprint: 42`). Both persist with
the session and propagate across every surface, and both are filterable in the usage analytics.

In the terminal:

```
/label wip                 add a label            /labels        list current labels
/label rm wip              remove a label         /tags          list current tags
/tag env: prod             set (upsert by key)    /tag rm env    remove a tag
```

At launch, `mux --label wip --tag env:prod` seeds the session (repeatable). Headless scripts use the
`mux session` verb:

```bash
mux session <id> label wip
mux session <id> tag env:prod
mux session <id> unlabel wip
mux session <id> untag env
mux session <id> show          # or --json
mux session --list             # id, labels, tags per session
```

Tag **keys** are normalized — trimmed, lowercased, and slugified to `[a-z0-9._-]` (so `Env` and `env` are
one facet) — while tag **values** and labels are free-form UTF-8. Labels dedupe case-insensitively. The
same edits are available from the Desktop app and VS Code (right-click a session → Edit labels / Edit
tags) and from the web dashboard's **Sessions** page.

### Exporting / sharing a session

`/export` (`/share`) writes the current session to a self-contained **HTML** file and a **Markdown**
file in the working directory — a local, server-free way to share a transcript. The HTML is a single
file with inline styles and no external requests. From the command line, `mux export <session-id>`
renders a saved session; `--format md|html` picks the format, `--output <path>` writes to a file (else
stdout), and `mux export --list` lists saved session ids. Titles work in place of ids.

```bash
mux export --list
mux export my-session --format html --output session.html
mux export "Refactor auth" --format md > transcript.md
```

### Undo / redo (git checkpoints)

When the working directory is a git repository, mux snapshots the working tree before each turn, so
`/undo` rolls the tree back to the state before the last turn and `/redo` re-applies it. A snapshot
captures tracked and untracked (non-ignored) files: undo reverts modifications, restores deletions, and
removes files created during the turn. It uses git plumbing only — it never touches your branch, commit
history, stash, or staged index. Its reach is the git working tree: it cannot reverse effects outside it
(spawned processes, network calls, `.gitignore`d files). Outside a git repository the commands report the
feature is unavailable.

### Background tasks

For a request that spans several steps or files, the model decomposes the work into a plan of tasks
(through the `plan_tasks` and `update_task` tools it calls on its own) and advances them as it goes. The
transcript shows a live checklist that updates in place — `◻` pending, `◼` running, `✔` done, `✗` failed,
`▦` blocked, `⊘` skipped — and the sidebar shows overall progress as `TASKS n/m`. The plan is saved with
the session and restored on resume.

`/tasks` opens a viewer for the focused job's plan. Inside it, arrow keys move the selection and single
keys annotate the highlighted task: `c` complete, `i` in progress, `b` blocked, `k` skipped, `p` pending,
`n` edit note. Those manual edits change the same plan the model works from, so they persist and update the
sidebar. Turn the feature off with `taskPlanningEnabled: false` in `settings.json`.

### Loops

`/loop` repeats a prompt across turns. With an interval it is a fixed loop; without one it is self-paced:

```text
/loop 5m check the deploy and report anything new     # every 5 minutes
/loop 1h30m --max 4 summarize new issues              # at most 4 iterations
/loop keep fixing failing tests until they pass       # self-paced
```

Intervals combine `s`, `m`, `h`, and `d` (`90s`, `1h30m`). The first iteration starts as soon as the shell is
idle; loop iterations never run while a turn is in flight or prompts are queued, so they never interleave with
what you type. Each iteration is an ordinary turn: the same approvals, write lease, transcript, and hooks.

In a self-paced loop the model ends each iteration by calling the `schedule_next` tool with `delay_seconds`
(from `loopMinIntervalSeconds` up to 3600) and a one-line reason, or with `stop: true`. An iteration that does
not call it stops the loop, so a model that never decides cannot run away. The tool is offered only while a
self-paced iteration is running.

Every loop has an iteration cap (`--max N`, default and maximum `loopMaxIterations`, 50 unless configured). A
fixed loop's next fire time is measured from when the iteration started; if an iteration runs past one or more
fire times, those are skipped rather than replayed in a burst. A turn you cancel (or one that fails) pauses its
loop.

`/loops` lists every loop with its pacing, iterations, and state; `/loops cancel <id>` (or `all`), `/loops pause
<id>`, and `/loops resume <id>` manage them. The sidebar's LOOPS section shows each active loop and when it
fires next. Active loops are saved with the session; resuming the session restores them paused, so nothing runs
until you `/loops resume` it.

Headless: `mux print --loop 5m "<prompt>"` (or `--loop self`) runs the loop in one process, one turn per
iteration against the growing conversation, and exits when the loop completes, stops, or a turn fails (exit code
of the failing turn). `--loop-max N` sets the cap. Progress lines go to stderr; each iteration's output goes to
stdout in the chosen `--output-format`. `--loop` cannot be combined with `--input-format jsonl`.

For loops inside a single turn, use the skills: `loop-until` (retry a command until it succeeds),
`fix-until-green` (build and test, fix one failure at a time), `ci-watch` (GitHub Actions status, waiting, and
failed-step logs), and `flaky-test-hunt` (run a test many times and report how often it fails).

### Usage analytics

Every model call — interactive, `print`, subagent, or dashboard chat — is recorded to a local SQLite
database (`~/.mux/usage.db`) with its token counts, time-to-first-token, streaming time, latency, and
throughput. The sidebar shows the running session cost (once there is spend), and `/usage` (`/stats`,
`/spend`) opens a summary of the last 24 hours and 7 days — tokens, cost, calls, error rate, average and
p95 TTFT/latency, and the top models by spend — read from the shared database, so it reflects every mux
instance on the machine, not just the current one.

For charts over time (token usage by type, cost, latency/TTFT percentiles, streaming time, throughput) with
endpoint, model, **label**, and **tag** filters, plus a paginated per-call history, run `mux serve` and open
the dashboard's **Usage** page; edit per-model rates on its **Pricing** page. Filtering by label or tag joins
each usage row to its session's *current* metadata, so relabeling a session refilters its whole history
retroactively. A session that carries several labels counts under each in a label breakdown, so per-label
totals can add up to more than the grand total. In the terminal, `/usage label <label>` or
`/usage tag <key:value>` opens the charts scoped to that label or tag. Cost is derived from `pricing.json`
at read
time, so correcting a rate re-values history. Disable capture with `telemetry.enabled: false` (or the
`MUX_TELEMETRY_ENABLED` environment variable). See [CONFIG.md](CONFIG.md#usage-telemetry-settingsjson-telemetry).

Interactively, the model works one job's plan at a time (it keeps a single task `in_progress`), so the
checklist tracks progress rather than fanning out to concurrent jobs. The opt-in `taskParallelismEnabled`
(default off) gates the `TaskOrchestrator` engine, which runs a task DAG as parallel jobs under the shared
write lease for programmatic orchestration; it is not yet wired into the interactive submit path.

### Subagents

Define named subagents in `~/.mux/subagents.json` (see
[CONFIG.md](CONFIG.md#subagentsjson-subagent-delegation)) and the model can delegate a self-contained
sub-task to one via the `spawn_subagent` tool. A subagent runs in an **isolated conversation** — it never
sees or mutates the parent's history — and returns only its final answer, so delegating focused work (a
review, a scoped search, a mechanical change) keeps the primary agent's context clean. Each subagent can
have its own system prompt, endpoint, tool allow-list, and iteration cap; the tool is offered only when
at least one valid subagent is defined. A subagent cannot spawn further subagents.

### Plugins: hooks and custom commands

The plugin system extends mux with out-of-process **event hooks** and **custom slash commands**,
configured in `~/.mux/hooks.json` (see [CONFIG.md](CONFIG.md#hooksjson-plugin-system-hooks--custom-commands)).
Hooks run on `session-start`, `user-prompt-submit` (which a blocking hook can veto to refuse a prompt),
and `session-end`; the event payload arrives on the hook's stdin and its stdout is surfaced into the
transcript.

Tool-level hooks run inside every agent turn on every surface (terminal, `mux print`, desktop, and the web
dashboard), with Claude Code's contract:

- `pre-tool-use` runs after a tool call is approved and before it runs. Exit 2 blocks the call; the hook's stderr
  becomes the tool result the model reads.
- `post-tool-use` runs after the call. Its stdout (exit 0) or stderr (exit 2) is appended to the tool result.
- `stop` runs when the model finishes. Exit 2 sends the hook's stderr back as a new message and the model keeps
  going, at most three times per run.

A `matcher` (glob, `|` between alternatives) limits tool hooks to some tools, for example
`"matcher": "write_file|edit_file"`. The payload on stdin has `hook_event_name`, `session_id`, `cwd`, `tool_name`,
`tool_input`, and (after the call) `tool_response`. Any other exit code, a timeout, or a missing command is a
warning and the turn continues. Outcomes appear as `hook` events in `jsonl` output. See
[CONFIG.md](CONFIG.md#hooksjson-plugin-system-hooks--custom-commands) for the full schema. Custom commands register as `/<name>` and run an external command, posting its output. Both
run as a literal argument vector — never through a shell. Inspect what is configured with
`mux plugin list` (add `--output-format json` for machine-readable output).

## Built-In Process Execution

The built-in `run_process` tool executes commands using the host shell for the current operating system:
- Windows: `cmd.exe /c`
- Linux and macOS: `/bin/sh -c`

`run_process` now exposes runtime metadata in its tool description and schema so the model can see:
- the operating system
- the platform family
- the shell program
- the shell invocation form

This matters for command generation. For example, a Windows runtime should use `dir`/`type`/`copy` style commands, while a Unix runtime should use `ls`/`cat`/`cp`.

### Background processes

Commands that keep running (a dev server, `docker compose up`, a watch-mode test runner) do not fit
`run_process`, which waits for the command to finish. The model uses four tools for them instead:

| Tool | Kind | What it does |
|---|---|---|
| `process_start` | mutating | Starts `command` through the same shell as `run_process` and returns an id (`p1`, `p2`, ...) at once. With `wait_for` (a regular expression) and `timeout_ms` (default 30000, at most 300000) it first waits for a ready line, such as `Local:\s+(\S+)`. Optional `working_directory` and `name`. |
| `process_output` | read-only | Returns only the output produced since the previous read, with `running` and `exit_code`. With `wait_for` it waits until that pattern appears, the process exits, or the timeout passes (`matched`, `timed_out`). |
| `process_list` | read-only | Lists every process with its id, command, pid, state, and unread output. |
| `process_stop` | mutating | Stops a process and its children and returns its last output. `id: "all"` stops every one. |

stdout and stderr are interleaved by line, ANSI color codes are removed, and each process keeps its newest
`backgroundProcessOutputBytes` of output (default 1 MB); a read reports how much was dropped. At most
`backgroundProcessMaxConcurrent` processes (default 8) run at once. Processes belong to the session: closing the
terminal, ending a `mux print` run, closing the desktop app, or stopping `mux serve` kills every process tree.
`process_start` and `process_stop` go through the approval policy like `run_process`; the read-only posture hides
them.

`/processes` (alias `/ps`) in the terminal and the desktop app lists the processes; `/processes output <id>` shows
recent output without consuming it, `/processes stop <id>` (or `all`) stops one, and `/processes clear` removes
exited ones. The terminal sidebar's PROCESSES section shows each process with `●` while it runs and `○` after it
exits. The `react-dev-server` skill detects the dev script, framework, port, and ready line and starts the server
this way.

## Web Search And Retrieval

Mux has two distinct web-facing tools:

| Tool | Purpose | Configuration |
|---|---|---|
| `web_retrieve` | Fetch a known HTTP or HTTPS URL and return rendered page data | Always available when built-in tools are enabled for the selected endpoint |
| `web_search` | Discover public web results and return URLs/snippets | Requires external search to be enabled with a configured Tavily or You.com provider |

`web_retrieve` runs in a headless Playwright browser. Chromium is the default browser and Firefox is also supported. If the requested browser binary is missing at runtime, mux invokes Playwright's installer on demand.

If enterprise TLS inspection causes certificate failures such as `SELF_SIGNED_CERT_IN_CHAIN`, run mux with `--ignore-cert-errors` or the shorter `--insecure` alias. This disables TLS certificate validation for mux-owned network requests, including LLM HTTP calls, external-search provider calls, `web_retrieve` browser navigation, and the Playwright browser installer. The same behavior can be enabled with `ignoreCertErrors: true` in `settings.json` or `MUX_IGNORE_CERT_ERRORS=1`. mux emits a warning when this mode is active.

`web_search` is provider-backed discovery. It does not fetch arbitrary local URLs such as `http://localhost:11434`; use `web_retrieve` when you already have a URL and want its contents.

Example prompts:

```text
mux> retrieve https://example.com and display the returned title and text
mux> search the web for mux GitHub releases, then retrieve the most relevant result
```

## Output Formats

`mux print` supports:
- `text` (default): assistant text on stdout, progress and errors on stderr
- `json`: a single summary object on stdout at the end of the run
- `jsonl`: one structured event per stdout line

These give four output shapes when combined with `--buffer`:

| Mode | Flags | Streaming | Stats |
|---|---|---|---|
| Streamed text | `--output-format text` (default) | yes | never on stdout; `--stats` → stderr footer |
| Buffered text | `--output-format text --buffer` | no | never on stdout; `--stats` → stderr footer |
| Streamed JSONL | `--output-format jsonl` | yes | on by default; `--no-stats` omits |
| Buffered JSON | `--output-format json` | no | on by default; `--no-stats` omits |

Two flags control statistics and buffering:
- `--stats` / `--no-stats`: include or omit run statistics and the token `usage` block. Default is **off for
  `text`** (where stats can only appear as an opt-in one-line `mux: tokens …` footer on **stderr**, never on
  stdout) and **on for `json`/`jsonl`**.
- `--buffer` (alias `--no-stream`): for `text`, hold the answer and emit it in one write at the end instead of
  streaming it. Ignored for `json` (always buffered) and `jsonl` (always streamed).

The `json` object carries `result`, `status`, `sessionId`, `iterationsCompleted`, `toolCallCount`,
`errorCount`, `durationMs`, `finalEstimatedTokens`, `compactionCount`, a `usage` object
(`inputTokens`/`outputTokens`/`totalTokens`/`estimatedTokens`), an optional `taskSummary`, and
`contractVersion`, with the same secret redaction as the `jsonl` stream. With `--no-stats` the run-metrics
fields and `usage` are omitted, leaving `result`, `status`, `sessionId`, and `contractVersion`. A failed run
reports on `stderr` with a non-zero exit code rather than emitting a summary object.

`mux print --output-last-message <path>` optionally writes only the final assistant response text to a file. If the run fails, mux does not create the file.

`mux print --input-format jsonl` switches the prompt source from a single argument to a stream of stdin turn records (see [Multi-Turn Input](#multi-turn-input---input-format-jsonl)); the default `--input-format text` is the single-prompt behavior described above.

`mux probe` supports:
- `text` (default)
- `json`

## Structured JSONL Contract

`mux print --output-format jsonl` emits newline-delimited JSON with stable top-level fields such as:
- `contractVersion`
- `eventType`
- `timestampUtc`

Depending on the event, additional fields may include:
- `runId`
- `sessionId`
- `endpointName`
- `adapterType`
- `baseUrl`
- `model`
- `approvalPolicy`
- `commandName`
- `workingDirectory`
- `configDirectory`
- `endpointSelectionSource`
- `cliOverridesApplied`
- `toolCall`
- `toolCallId`
- `toolName`
- `result`
- `code`
- `errorCode`
- `failureCategory`
- `message`
- `status`
- `durationMs`
- `maxIterations`
- `contextWindow`
- `reservedOutputTokens`
- `usableInputLimit`
- `warningThresholdTokens`
- `tokenEstimationRatio`
- `estimatedTokens`
- `remainingTokens`
- `remainingPercent`
- `messageCount`
- `trigger`
- `warningLevel`
- `scope`
- `mode`
- `strategy`
- `messagesBefore`
- `messagesAfter`
- `estimatedTokensBefore`
- `estimatedTokensAfter`
- `summaryCreated`
- `reason`
- `finalEstimatedTokens`
- `compactionCount`
- `usage` (on `run_completed`: `inputTokens`, `outputTokens`, `totalTokens`, `estimatedTokens`)
- `builtInToolCount`
- `effectiveToolCount`
- `ignoreCertErrors`
- `sandboxPosture`
- `mcp`

Current event types:
- `run_started`
- `assistant_text`
- `tool_call_proposed`
- `tool_call_approved`
- `tool_call_completed`
- `heartbeat`
- `context_status`
- `context_compacted`
- `error`
- `run_completed`
- `task_plan_updated`
- `hook` (a `pre-tool-use`, `post-tool-use`, or `stop` hook blocked a call, added to a result, made the model continue, or failed)

A `task_plan_updated` event carries `changeKind` (`plan_created`, `plan_replaced`, `task_status_changed`,
`task_note_updated`, or `plan_cleared`), an optional `changedTaskId`, `totalCount`/`completedCount`, and a
`tasks` array (each with `id`, `title`, `status`, `dependsOn`, and any `note`/`failureMessage`).
`run_completed` additionally carries a `taskSummary` tally when the run had a task plan.

Example:

```bash
mux print --output-format jsonl --yolo "read README.md"
```

Example JSONL lines:

```json
{"contractVersion":2,"eventType":"run_started","timestampUtc":"2026-03-31T20:00:00Z","runId":"...","endpointName":"ollama-local","model":"qwen2.5-coder:7b","maxIterations":50}
{"contractVersion":2,"eventType":"assistant_text","timestampUtc":"2026-03-31T20:00:01Z","text":"Here is the summary..."}
{"contractVersion":2,"eventType":"run_completed","timestampUtc":"2026-03-31T20:00:02Z","runId":"...","status":"completed","durationMs":1042,"usage":{"inputTokens":1234,"outputTokens":567,"totalTokens":1801,"estimatedTokens":1750}}
```

Notes:
- machine-readable output is on `stdout`
- secret-like values in structured payloads are redacted on a best-effort basis
- default text mode is unchanged
- `run_started.mcp.supported` is `false` in `print` unless `--mcp-config` is supplied; with it, `mcp.configured`/`mcp.serverCount` reflect the loaded servers
- `run_started` and `run_completed` carry `sessionId` (empty when the run is not associated with a persisted session)
- `run_completed.status` is `completed`, `completed_with_errors`, `max_iterations_reached`, or `budget_exceeded`; the matching `error` event code `budget_exceeded` is classified as `runtime`
- `run_started` now includes `maxIterations`, context-budget metadata, and `ignoreCertErrors`, and `run_completed` includes `finalEstimatedTokens`, `compactionCount`, and a `usage` object (`inputTokens`/`outputTokens`/`totalTokens`/`estimatedTokens`)
- `--no-stats` omits the run-metrics fields and the `usage` object from `run_completed` (and from the `json` summary); `--stats` forces them on. Structured output includes them by default
- `context_status` and `context_compacted` are additive event types; consumers should ignore unknown event types in a known contract version
- `error` events retain `code` for backward compatibility and also expose `errorCode` plus `failureCategory`
- `contractVersion` is shared across `print` JSONL events and `probe` JSON payloads

## Exit Codes

`mux print`:
- `0`: success
- `1`: config, runtime, backend, or command failure
- `2`: tool call denied

`mux probe`:
- `0`: probe succeeded
- `1`: probe failed

## Approval Policy

Policies:

| Flag | Behavior |
|---|---|
| default interactive | ask before each tool call |
| `--yolo` | auto-approve all tool calls |
| `--approval-policy ask` | explicit ask mode |
| `--approval-policy auto` | explicit auto-approve mode |
| `--approval-policy deny` | deny all tool calls |

Notes:
- `mux print` defaults to `deny` unless `--yolo`, `--approval-policy`, or the selected endpoint's `autoApproveTools` setting overrides it
- interactive mode typically uses ask semantics
- interactive mode also honors endpoint-scoped `autoApproveTools` unless CLI approval flags override it
- `mux print` and `mux probe` reject `--approval-policy ask`

## Structured Output (`--output-schema`)

`mux print --output-schema <path>` points at a JSON Schema file. mux folds a directive into the system
prompt telling the model to return a single JSON value conforming to that schema, and after the run it
validates the response recursively. It enforces the widely-used keywords — `type` (including union type
arrays and `integer`), `enum`, `required`, `properties` (validated recursively into nested objects), and
array `items` (validated recursively per element) — reporting the first violation with a JSON path (for
example `$.user.id`). Value-level constraints such as numeric bounds, string patterns, and formats are not
enforced. mux does not use provider-native structured-output APIs: its LLM layer (PolyPrompt) does not
expose a `response_format`/`json_schema` request field, so mux stays backend-agnostic by constraining via
the prompt and validating the result itself — which works identically against any model.

```bash
mux print --yolo --output-schema ./person.schema.json "extract the person from bio.txt" | jq .
```

A response wrapped in a Markdown code fence is unwrapped before validation. A response that is not JSON, is
the wrong top-level type, or is missing a required property fails the run with a `schema_validation_failed`
error (exit `1`), and no `--output-last-message` artifact or `json` summary is written.

## Headless MCP (`--mcp-config`)

MCP is off by default in `print` so a plain run stays fast and hermetic. Supplying `--mcp-config` turns it
on for that run: mux connects the servers, waits (bounded) for tool discovery, exposes the discovered tools
to the model, and disposes the connections when the run ends. The value is a file path or inline JSON in
the same `{ "servers": [ ... ] }` shape as `mcp-servers.json`.

```bash
mux print --yolo --mcp-config ./mcp-servers.json "use the database tool to list users"
mux print --yolo --mcp-config '{"servers":[{"name":"ctx","transport":"stdio","command":"npx","args":["-y","@upstash/context7-mcp"]}]}' "look up the docs"
```

By default the `--mcp-config` servers are merged with the config directory's `mcp-servers.json`; add
`--strict-mcp-config` to use only the servers from the flag. The active MCP state is reported on
`run_started` under `mcp` (`supported`/`configured`/`serverCount`). `--no-mcp` remains interactive-only.

## Tool Governance and Sandbox

Between "deny every tool" and `--yolo`, mux offers a middle ground for unattended runs. Two independent
controls layer on top of the approval policy, so they take effect once a tool would otherwise run
(typically under `--yolo` or `--approval-policy auto`).

Allow/deny lists filter which tools exist for a run. `--allow-tools` takes comma-separated tool-name
globs (`*` and `?`); when set, only matching tools are advertised to the model and permitted to execute.
`--deny-tools` removes tools, and a deny match always wins over an allow match. A tool excluded this way is
never offered to the model, and if the model calls it anyway the call is refused with a `tool_call_denied`
error (exit `2`) before it runs.

```bash
mux print --yolo --allow-tools "read_file,grep,glob" "summarize the code"   # read-only-ish, explicit
mux print --yolo --deny-tools "delete_file,run_process" "tidy up imports"    # everything but these two
```

The `--sandbox` posture is an application-level confinement over mux's built-in tools. It is not an
operating-system sandbox: it governs mux's own tools, not arbitrary subprocesses.

| Posture | Effect |
|---|---|
| `none` (default) | No confinement beyond the approval policy and allow/deny lists |
| `read-only` | Every mutating tool (write/edit/delete/manage-directory/run-process) is refused; reads and searches run |
| `workspace-write` | Built-in file writes are confined to the working directory plus any `--add-dir` roots; a write whose path escapes them is refused |

```bash
mux print --yolo --sandbox read-only "audit this repo and report findings"
mux print --yolo --sandbox workspace-write --add-dir ../shared "apply the refactor"
```

Under `workspace-write`, `run_process` is still allowed (subject to approval) because mux cannot
OS-sandbox an arbitrary subprocess; confine those with `--deny-tools "run_process"` when needed. The
active posture is reported as `sandboxPosture` on the `run_started` event, and governance refusals use the
`tool_call_denied` error code (exit `2`), the same as an interactive denial.

## Multi-Turn Input (`--input-format jsonl`)

By default `mux print` runs one prompt. With `--input-format jsonl`, stdin becomes a stream of turn
records — one JSON value per line — and each runs as a turn against the accumulating conversation, so turn
N sees turns 1..N-1. A record is an object with a `prompt` (or `text`, or `content`) string, or a bare
JSON string. Blank lines are skipped; a malformed record is reported and skipped without ending the stream.

```bash
printf '{"prompt":"summarize README.md"}\n{"prompt":"now list the risks you found"}\n' \
  | mux print --input-format jsonl --output-format jsonl --yolo
```

Output follows `--output-format` per turn: `jsonl` streams each turn's events (so there is one
`run_started`/`run_completed` pair per turn), `text` prints each turn's assistant text, and `json` emits
one summary object per turn. `--output-last-message` captures the final turn's response. Combined with a
session flag, the whole multi-turn conversation persists as one session; MCP servers from `--mcp-config`
connect once and are shared across every turn.

## Print Sessions (Headless Resume)

`mux print` is single-shot, but a run can continue an earlier one. Persistence is opt-in: a plain
`mux print "..."` stays stateless and writes nothing, and only a session flag engages the store.

| Flag | Behavior |
|---|---|
| `--resume <id\|title>` | Continue a persisted session, matched first by id and then by title |
| `--continue` | Continue the most recently updated persisted session in the active config directory |
| `--session-id <id>` | Run under a specific id, creating the session if it does not exist |
| `--fork-session` | Persist the resumed run under a new id instead of overwriting the source |
| `--no-session-persistence` | Read the resumed session but do not write the run back to disk |

The run's session id is surfaced on `run_started` and `run_completed` (and in the `json` summary), so an
orchestrator can capture it from one run and feed it to the next:

```bash
sid=$(mux print --output-format json --session-id build-42 --yolo "start the migration" | jq -r '.sessionId')
mux print --resume "$sid" --output-format json --yolo "continue where you left off" | jq -r '.result'
```

Print sessions live in the same store as the interactive shell, so a session started with `mux print` is
resumable from the interactive `/sessions` browser and vice versa. On resume, mux replays the saved
conversation history and re-applies the current system prompt, so switching endpoints or prompts between
runs is safe. The stored history excludes the system message (it is rebuilt each run from the effective
system prompt).

> Concurrency note: print sessions and the interactive shell share one on-disk store. Each save is atomic,
> but two processes writing the **same** session id concurrently (for example `mux print --resume X` while
> the interactive shell has session `X` open) is last-writer-wins. Use distinct session ids, or
> `--fork-session`, when running print against a session that may be open elsewhere.

## Config Isolation

Use `--config-dir` or `MUX_CONFIG_DIR` when running under automation or when multiple processes need isolated configs.

```bash
# Bash
export MUX_CONFIG_DIR=/tmp/mux-job-123
export MUX_IGNORE_CERT_ERRORS=1
mux print --output-format jsonl --yolo "run the task"

# PowerShell
$env:MUX_CONFIG_DIR = "C:\\temp\\mux-job-123"
$env:MUX_IGNORE_CERT_ERRORS = "1"
mux probe --output-format json

# CLI override
mux print --config-dir /tmp/mux-job-123 --output-format jsonl --yolo "run the task"
```

When config isolation is used:
- config is loaded from that directory
- first-run seeding happens in that directory
- `mux` does not fall back to the user-home config directory for those config reads
- `--config-dir` takes precedence over `MUX_CONFIG_DIR`
- `--ignore-cert-errors` takes precedence over `settings.json` for the current run; `MUX_IGNORE_CERT_ERRORS` can also enable or disable the loaded setting for automation

## Backend Examples

### Ollama

Mux's `ollama` adapter speaks Ollama's **native** API (`/api/chat`), which lives at the server root, so the base URL is just `http://localhost:11434` — no `/v1`. (A trailing `/v1` targets Ollama's separate OpenAI-compatible surface and is tolerated — mux strips it for this adapter — but the canonical form omits it. If you specifically want Ollama's OpenAI-compatible surface, use `adapterType: "openai-compatible"` with `http://localhost:11434/v1`.)

```json
{
  "endpoints": [
    {
      "name": "ollama-gemma",
      "adapterType": "ollama",
      "baseUrl": "http://localhost:11434",
      "model": "gemma3:4b",
      "isDefault": true
    },
    {
      "name": "ollama-qwen32",
      "adapterType": "ollama",
      "baseUrl": "http://localhost:11434",
      "model": "qwen2.5-coder:32b",
      "maxAgentIterations": 60
    }
  ]
}
```

```bash
mux
mux --endpoint ollama-qwen32
mux print --yolo --endpoint ollama-qwen32 "refactor UserService"
```

### vLLM

```json
{
  "endpoints": [
    {
      "name": "vllm-deepseek",
      "adapterType": "openai-compatible",
      "baseUrl": "http://localhost:8000/v1",
      "model": "deepseek-ai/DeepSeek-Coder-V2-Instruct",
      "maxAgentIterations": 80,
      "headers": { "Authorization": "Bearer sk-local-dev" },
      "quirks": {
        "assembleToolCallDeltas": true,
        "supportsParallelToolCalls": true,
        "stripRequestFields": ["stream_options"]
      }
    }
  ]
}
```

```bash
mux --endpoint vllm-deepseek
mux print --yolo --endpoint vllm-deepseek "refactor UserService to be async"
mux probe -e vllm-deepseek --output-format json
```

### OpenAI

```json
{
  "endpoints": [
    {
      "name": "openai-gpt4o",
      "adapterType": "openai",
      "baseUrl": "https://api.openai.com/v1",
      "model": "gpt-4o",
      "maxAgentIterations": null,
      "headers": { "Authorization": "Bearer ${OPENAI_API_KEY}" }
    }
  ]
}
```

```bash
mux --endpoint openai-gpt4o
mux print --yolo -e openai-gpt4o "explain the architecture of this project"
mux probe -e openai-gpt4o
```

### Ad-Hoc CLI-Only Usage

```bash
mux --base-url http://localhost:11434 --model gemma3:4b --adapter-type ollama
mux --base-url https://api.openai.com/v1 --model gpt-4o --adapter-type openai
mux --base-url http://localhost:8000/v1 --model deepseek-coder-v2 --adapter-type openai-compatible
```

CLI overrides always win over endpoint config values.

## External Search Configuration

External search providers are stored in `settings.json` under `externalSearch`. Supported provider types are `tavily` and `you`.

Tavily example:

```json
{
  "externalSearch": {
    "enabled": true,
    "allowFallback": true,
    "providers": [
      {
        "name": "tavily-primary",
        "providerType": "tavily",
        "endpoint": "https://api.tavily.com/search",
        "apiKey": "${TAVILY_API_KEY}",
        "enabled": true,
        "isDefault": true,
        "timeoutMs": 60000
      }
    ]
  }
}
```

You.com example:

```json
{
  "externalSearch": {
    "enabled": true,
    "allowFallback": true,
    "providers": [
      {
        "name": "you-primary",
        "providerType": "you",
        "endpoint": "https://ydc-index.io/v1/search",
        "apiKey": "${YOU_API_KEY}",
        "enabled": true,
        "isDefault": true,
        "timeoutMs": 60000
      }
    ]
  }
}
```

The interactive `/search add` wizard writes the same structure for you.

## MCP Tool Servers

Example `mcp-servers.json`:

```json
{
  "servers": [
    {
      "name": "github",
      "transport": "stdio",
      "command": "npx",
      "args": ["-y", "@modelcontextprotocol/server-github"],
      "env": { "GITHUB_TOKEN": "${GITHUB_TOKEN}" }
    },
    {
      "name": "remote-http",
      "transport": "http",
      "url": "https://mcp.example.com",
      "mcpPath": "/mcp"
    }
  ]
}
```

Runtime management:

```text
/mcp list
/mcp ls
/mcp add
/mcp remove myserver
/mcp delete myserver
/mcp rm myserver
```

`/mcp add` now runs a guided wizard similar to `/endpoint add`. The wizard lets you choose `stdio` or HTTP transport, and successful adds are saved to `mcp-servers.json` as well as connected for the current session. HTTP MCP currently uses the streamable HTTP path, usually `/mcp`.

Skip MCP startup:

```bash
mux --no-mcp
```

Important:
- interactive mode loads MCP servers from `mcp-servers.json` automatically; `mux print` loads them only when `--mcp-config` is supplied (see [Headless MCP](#headless-mcp---mcp-config)); `mux probe` never loads MCP
- `--no-mcp` is interactive-only and, in `print`/`probe`, returns a structured configuration error rather than silently implying MCP support
- an MCP tool result marked `isError: true` (the tool failed, or its arguments did not match the tool's input schema) is recorded as a failed tool call; the transcript shows the server's message as the failure reason, and the model still receives the full result so it can correct itself and retry

## Skills

Skills are versioned Markdown-plus-code capabilities under `~/.mux/skills` (and, per project, in the repository; see below). Each is a folder with a `SKILL.md` (frontmatter plus a body). A command skill's commands run a fenced code block or a bundled script through an allowlisted interpreter with a timeout and captured output, turning a request into a fixed, deterministic procedure. A playbook skill declares no commands: its body is a procedure the model follows with its normal tools. Every surface, including `mux print`, discovers skills, lists the relevant enabled ones in the system prompt, and exposes `skill` (read a skill's instructions) and `run_skill` (execute a command, gated by the approval policy and the write lease). A curated default set is seeded on first run and preserved on upgrade.

Manage skills in-app with `/skills` (aliases `/skill`; also on the `F1` menu under **Model**): the inventory shows a state glyph (`●` enabled, `○` disabled, `⚠` invalid), command counts, and tags; per-skill actions cover view, enable/disable, duplicate, and remove; a **+ New skill…** wizard scaffolds a working skill; and **⬇ Import skill…** brings one in from a local path. Enablement lives in `~/.mux/skills.json`, separate from each `SKILL.md`.

The same operations run non-interactively:

```bash
mux skill list                       # inventory with validity and enablement
mux skill show <name>                # metadata, commands, and body
mux skill validate [<name>]          # validate one or all; nonzero exit on failure (CI gate)
mux skill run <name> <command> [--arg v ...] [--cwd dir]   # execute deterministically
mux skill new <name>                 # scaffold a skill
mux skill add <path>                 # import from a directory
mux skill trust [all|playbooks|ignore|reset] [--cwd dir]   # record or report project skill trust
```

`mux skill run` returns the same `stdout`/`stderr`/`exit_code` contract the agent sees, so a Git hook or CI job can invoke a curated procedure with no model in the loop. The full authoring reference is in `SKILLS_AUTHORING.md`.

### Running a skill by name

Type `/<skill> [arguments]` in the terminal, the desktop app, or the web dashboard, or pass it as the prompt to
`mux print "/<skill> args"`. mux replaces the slash text with the skill's instructions: `$ARGUMENTS` becomes the
whole argument text and `$1` through `$9` the positional arguments (quotes group words), substituted in prose
but never inside fenced code blocks. When the body has no placeholder, the arguments are appended on their own
line. A skill with commands also tells the model to run them through `run_skill`. A skill can opt out with
`userInvocable: false`, and `argumentHint` documents what it expects. A skill whose name matches a built-in
command is shadowed by that command; `/skills` flags the collision.

### Project skills and trust

Skills can live in the repository as well as in `~/.mux/skills`. mux also searches `.mux/skills`,
`.claude/skills`, and `.agents/skills` under the repository root (or the working directory outside a
repository), so a project that already ships Claude Code or Codex skills works unchanged. A project skill
shadows a user skill with the same id. Claude Code frontmatter is understood: `allowed-tools`,
`argument-hint`, `user-invocable`, and `disable-model-invocation` map onto mux's fields, YAML block scalars
(`description: >`) are read, and fields mux does not know are reported as warnings rather than errors.

A project skill with runnable commands is code from the repository, so it stays blocked until you decide
to trust that project. Playbook skills (instructions only) load immediately. The terminal tells you when a
project ships blocked skills; decide with `/trust all`, `/trust playbooks`, `/trust ignore`, or `/trust reset`
(bare `/trust` reports the current level). The desktop app has the same `/trust` command, and the
`/skills` inventory lists project skills with their state and a **Trust this project's skills** action.
Decisions are stored per repository root in `~/.mux/trusted-projects.json`. Headless runs use
`mux skill trust <level> [--cwd dir]` to record a decision, or `--trust-project-skills` to trust the
project for one run without recording anything.

### Which skills the model sees

With 100+ skills installed, listing all of them on every turn would crowd a small model's context. A skill
can declare `appliesTo` globs (for example `[package.json]` or `[pyproject.toml, requirements*.txt]`), and
in the default `relevant` listing mode it is advertised only when a glob matches a file in the project. A
footer tells the model how many skills were left out and that `skill` with the name `list` shows them all;
every enabled skill stays callable either way. Set `skillListingMode` to `all` or `none` to change this.

Settings in `settings.json`: `skillsEnabled` (default `true`), `skillRefreshIntervalSeconds` (default `30`),
`skillsDirectory` (override the default `~/.mux/skills`), `projectSkillsEnabled` (default `true`),
`projectSkillRoots`, and `skillListingMode` (default `relevant`). See [CONFIG.md](CONFIG.md).

### Review, debugging, and codebase skills

These default skills cover the review and investigation workflows of other agent harnesses. Run them by name:

```text
/code-review                       # uncommitted changes, untracked files included
/code-review branch [base]         # this branch against its merge base (default branch when omitted)
/code-review commit <sha> deep     # one commit; quick or deep sets the effort
/code-review pr 42                 # a pull request (needs gh)
/code-review file src/app.ts       # one file in full
/security-review [uncommitted|branch|pr <n>]
/simplify [base]                   # behavior-preserving cleanup of changed files
/pr-comments [n]                   # review threads, unresolved first (needs gh)
/test-gap-review [base]            # changed source files with no matching test change
/debug <symptom>                   # reproduce, isolate, fix, verify
/git-bisect start <good> [bad]     # then: run <test command>; reset
/init                              # survey the project and draft AGENTS.md
/explain-codebase [depth]          # tree map (depth 1-4) with entry points
```

Review findings use one format: `[severity: high|medium|low] path:LINE`, then `Failure:` (the concrete input or
state that goes wrong) and `Fix:`, or `No findings.`. `security-review` adds `Attack:` and prints a secret scan of
added lines (values masked) and the audit command for each changed dependency manifest. Diffs longer than
`MUX_SKILL_DIFF_MAX_BYTES` (default 200000) are cut with a note. `git-bisect run` always resets the bisect and
restores HEAD, even when the test command fails. The git-based skills are listed only inside a repository, and
`pr-comments` only when `gh` is installed.

### Loop skills

```text
/loop-until 10 6 curl -sf http://localhost:8080/health   # retry until it exits 0 (attempts, seconds apart)
/fix-until-green 5                                        # check, fix the first failure, repeat (budget 5)
/ci-watch                                                 # status, then watch [run-id], failed-logs [run-id]
/flaky-test-hunt 20 ParserTests                           # run a test filter 20 times; or 20 -- <command>
```

`fix-until-green check` detects .NET, JavaScript (npm, pnpm, yarn, bun), Python, Go, Rust, Maven, Gradle, and
CMake, runs the build and then the tests, and prints PASS or FAIL for each with the tail of the failing output.
Its playbook forbids weakening or skipping tests to get green. `ci-watch` needs `gh` and is listed only in
repositories with GitHub workflows; its commands accept `--from-file` with a saved `gh` JSON response or log for
offline use. Commands that wait (`loop-until run`, `fix-until-green check`, `ci-watch watch`, `flaky-test-hunt
run`) have a 30-minute timeout.

## Project Instructions (MUX.md, AGENTS.md, CLAUDE.md)

mux reads project instruction files into the system prompt on every surface (terminal, `mux print`, desktop,
web, and VS Code through the server). Starting at the repository root and walking down to the working
directory, it takes the first of `MUX.md`, `AGENTS.md`, or `CLAUDE.md` found in each directory, so a
repository written for Codex or Claude Code needs no changes. Outside a repository only the working directory
is read. A user-level `MUX.md` in the config directory (`~/.mux/MUX.md`) comes first, for preferences that
apply everywhere. Outer files come before inner ones, and the model is told that a later (nearer) file wins
on conflict.

The terminal lists the loaded files at startup and after `/cwd`; the desktop app shows them with
`/instructions` and after `/cwd`; the REST server exposes them at `GET /v1.0/api/context/instructions`. The
combined size is capped by `projectInstructionsMaxBytes` (default 32 KB); over the cap, the files farthest from
the working directory are dropped first. Turn the feature off with `projectInstructionsEnabled: false`, or skip
it for one run with `--no-project-instructions`.

## Orchestrator Integration

Recommended command forms:

```bash
mux print --output-format jsonl --yolo "implement the feature described in TASK.md"
mux print --config-dir /tmp/mux-job-123 --output-format jsonl --output-last-message result.txt --yolo "implement the feature described in TASK.md"
mux print --output-format jsonl --yolo --endpoint vllm-deepseek --working-directory /tmp/worktree-abc "fix the bug"
mux print --output-format jsonl --yolo --system-prompt /path/to/persona.md "do the thing"
mux probe --output-format json --require-tools --endpoint vllm-deepseek
mux endpoint list --output-format json
mux endpoint ls --output-format json
mux endpoint show vllm-deepseek --output-format json
```

Recommendations:
- set `--config-dir` per run when you can; otherwise set `MUX_CONFIG_DIR`
- prefer `--output-format jsonl` for `print`
- prefer `--output-format json` for `probe`
- prefer `--output-last-message` when the caller needs a clean final answer artifact
- use explicit `--endpoint` in production automation
- use `--yolo` or `--approval-policy auto` only when automatic tool execution is intended
- use `--require-tools` when validating captain endpoints
- rely on `run_started` and `probe` JSON metadata instead of inferring tool/MCP capability from docs alone
- rely on `contractVersion` for parser compatibility gating
- treat `print.errorCode`/`print.failureCategory` and `probe.errorCode`/`probe.failureCategory` as the stable failure classification surface
- treat `mux endpoint list`, `mux endpoint ls`, and `mux endpoint show <name>` with `--output-format json` as the supported endpoint inspection surface

## Contract Compatibility

Structured non-interactive output uses a shared `contractVersion`, currently **`2`**. Version `2` added the
token `usage` object to the `json` run summary and the `jsonl` `run_completed` event, and introduced the
`--stats` / `--no-stats` toggle (stats remain on by default for `json`/`jsonl`, so existing consumers keep
working).

Compatibility rules:
- additive fields are non-breaking within a contract version
- consumers should ignore unknown fields within a known contract version
- a contract-version bump is required for removals, renames, type changes, or semantic changes to required fields
