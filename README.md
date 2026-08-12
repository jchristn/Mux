<p align="center">
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="assets/icon-white.png">
    <source media="(prefers-color-scheme: light)" srcset="assets/icon-black.png">
    <img src="assets/icon-black.png" width="256" height="256" alt="mux">
  </picture>
</p>

<h1 align="center">mux</h1>

<p align="center">
  <em>Your AI agent, your models, your infrastructure.</em>
</p>

<p align="center">
  <a href="LICENSE.md"><img src="https://img.shields.io/badge/license-MIT-blue.svg" alt="MIT License"></a>
  <a href="https://dotnet.microsoft.com"><img src="https://img.shields.io/badge/.NET-8.0%20%7C%2010.0-purple.svg" alt=".NET 8 / 10"></a>
  <a href="CHANGELOG.md"><img src="https://img.shields.io/badge/version-0.8.0-blue.svg" alt="v0.8.0"></a>
  <a href="CHANGELOG.md"><img src="https://img.shields.io/badge/status-alpha-orange.svg" alt="alpha"></a>
</p>

> **Alpha software**
> mux is in alpha. APIs, interfaces, configuration formats, tool schemas, and CLI behavior are all subject to change. Feedback is welcome via [issues](https://github.com/jchristn/Mux/issues) and [discussions](https://github.com/jchristn/Mux/discussions).

## What is mux?

`mux` is a CLI AI agent that gives you a Claude Code / Codex-like experience using the backend and model you choose. It can run against Ollama, OpenAI, vLLM, LM Studio, Azure OpenAI, or any OpenAI-compatible API.

`mux` can read and write files, run commands, search code, and manage a project through either:
- an interactive REPL
- a single-shot non-interactive command surface

`mux` does not install or manage model runners. You bring your own local or remote inference backend, and `mux` connects to it.

## Screenshots

<details>
<summary>Click to expand</summary>

<p align="center"><img src="assets/ss1.png" alt="mux screenshot 1" width="900"></p>
<p align="center"><img src="assets/ss2.png" alt="mux screenshot 2" width="900"></p>
<p align="center"><img src="assets/ss3.png" alt="mux screenshot 3" width="900"></p>
<p align="center"><img src="assets/ss4.png" alt="mux screenshot 4" width="900"></p>
<p align="center"><img src="assets/ss5.png" alt="mux screenshot 5" width="900"></p>

**MCP tools**

<p align="center"><img src="assets/ss6.png" alt="mux MCP tools screenshot 1" width="900"></p>
<p align="center"><img src="assets/ss7.png" alt="mux MCP tools screenshot 2" width="900"></p>
<p align="center"><img src="assets/ss8.png" alt="mux MCP tools screenshot 3" width="900"></p>
<p align="center"><img src="assets/ss9.png" alt="mux MCP tools screenshot 4" width="900"></p>

</details>

## Highlights

- Backend-agnostic: one CLI for local and remote model runners
- Built-in tools: file edit/read/write/delete, directory management, glob, grep, process execution, and rendered web retrieval
- External web search: optional Tavily and You.com providers expose `web_search` for result discovery
- Shell-aware process execution metadata: `run_process` tells the model which OS and shell it will run under
- MCP tool servers: define `stdio`/HTTP servers in `mcp-servers.json` or manage them with `/mcp`; the interactive UI connects to them, discovers their tools, exposes those tools to the model, and shows per-server connectivity
- Skills: versioned Markdown-plus-code capabilities in `~/.mux/skills` that turn a request into a fixed, deterministic procedure; author, inventory, and manage them in-app with `/skills` (or the `mux skill` verb), and a curated default set ships on first run
- TUIKit interactive UI (`v0.3.0`): a full-screen shell with per-job transcripts, a job sidebar, a multi-line composer, slash commands / key bindings / menu over one command catalog, an interactive tool-approval modal, and autosaved resumable sessions. Multiple prompts run as concurrent background jobs (a single-writer lease serializes file edits); enqueue-while-busy lets you start a new job or append to the focused one. See `USAGE.md`.
- Background tasks: for a large request the model lays out the work as a tracked plan of tasks and advances them as it goes; the interactive shell draws a live checklist that updates in place (pending → running → done), the sidebar shows `TASKS n/m`, `/tasks` opens a viewer to inspect and hand-annotate the plan, and the plan persists across save/resume. A `task_plan_updated` event is emitted in `jsonl` mode for orchestrators. See `USAGE.md`
- Structured automation support: `mux print --output-format jsonl` emits one machine-readable event per line
- Config isolation: set `MUX_CONFIG_DIR` to run with a fully isolated config directory
- Health checks: `mux probe` validates config, backend reachability, auth, and model access

## Quick Start

Prerequisites:
- .NET 8 SDK or later
- A model runner installed and running separately

Example with Ollama:

```bash
ollama pull qwen2.5-coder:7b
ollama serve
```

Install `mux`:

```bash
git clone https://github.com/jchristn/Mux.git
cd Mux

# Windows
install-tool.bat
install-tool.bat net8.0

# Linux / macOS
chmod +x install-tool.sh
./install-tool.sh
./install-tool.sh net8.0
```

The install scripts accept an optional target framework argument. They default to `net10.0` when a .NET 10 SDK is installed and otherwise fall back to `net8.0`.

Run it:

```bash
mux
```

On first run, `mux` creates `~/.mux/endpoints.json` with a default local Ollama endpoint and `~/.mux/settings.json` with editable defaults. If you want an isolated config instead, set `MUX_CONFIG_DIR` before first launch.

See [GETTING_STARTED.md](GETTING_STARTED.md) for the full walkthrough.

## Verify It Works

After install, try this prompt to confirm the model and tools are working end to end:

```text
mux> create a file called hello.py that prints "hello world", then read it back to verify. if the file already exists, overwrite it. when finished, delete the file.
```

You should see `write_file` and `read_file` tool calls, the file created on disk, and the contents read back.

## CLI Usage

```text
mux [prompt]                         Interactive REPL (default)
mux [OPTIONS] [prompt]               Interactive with overrides
mux --print [OPTIONS] <prompt>       Single-shot mode
echo "prompt" | mux --print          Read prompt from stdin
mux probe [OPTIONS]                  Validate config and backend access
mux endpoint <subcommand> [OPTIONS]  Inspect configured endpoints
```

Use `mux print` as the preferred non-interactive entrypoint in scripts and automation. `--print` remains supported and is convenient for stdin piping. Use `mux endpoint list`/`ls`/`show` when automation needs to inspect stored endpoint configuration without entering the REPL.

### Options

| Option | Short / Alias | Description |
|---|---|---|
| `--help` | `-h`, `/?` | Show help and exit |
| `--version` | `/version` | Show version and exit; bare `mux -v` also prints the version |
| `--print` | `-p` | Single-shot mode |
| `--endpoint <name>` | `-e` | Use a named endpoint |
| `--model <name>` | `-m` | Override model |
| `--base-url <url>` |  | Override base URL |
| `--adapter-type <type>` |  | `ollama`, `openai`, `vllm`, `openai-compatible` |
| `--temperature <float>` |  | Override temperature |
| `--max-tokens <int>` |  | Override max output tokens |
| `--effort <level>` |  | Reasoning effort: `off`, `minimal`, `low`, `medium`, `high` |
| `--effort-openai-value <str>` |  | Override the OpenAI `reasoning_effort` value |
| `--effort-gemini-budget <int>` |  | Override the Gemini thinking budget (`-1`..`32768`) |
| `--effort-ollama-think <val>` |  | Override the Ollama `think` value (`low`/`medium`/`high`/`true`/`false`) |
| `--show-thinking` |  | Surface the model's reasoning ("thinking") for this run |
| `--max-turns <int>` |  | Override max agent loop iterations (1-100) |
| `--max-token-budget <int>` |  | Stop with `budget_exceeded` when estimated context tokens exceed this |
| `--compaction-strategy <mode>` |  | Override compaction strategy: `summary` or `trim` |
| `--config-dir <path>` |  | Override the active config directory |
| `--working-directory <path>` | `-w` | Tool execution directory |
| `--system-prompt <path>` |  | Override system prompt file |
| `--append-system-prompt <text>` |  | Append text to the resolved system prompt |
| `--sandbox <posture>` |  | Confinement posture: `none` (default), `read-only`, or `workspace-write` |
| `--allow-tools <globs>` |  | Comma-separated tool-name globs; only matching tools are allowed |
| `--deny-tools <globs>` |  | Comma-separated tool-name globs to deny (deny wins over allow) |
| `--add-dir <path>` |  | Additional writable root under `workspace-write` (repeatable) |
| `--output-schema <path>` |  | print: constrain the final response to a JSON Schema file |
| `--mcp-config <path\|json>` |  | print: load MCP servers from a file or inline JSON (enables MCP) |
| `--strict-mcp-config` |  | print: use only `--mcp-config` servers, ignoring `mcp-servers.json` |
| `--input-format <format>` |  | print: `text` (default) or `jsonl` (multi-turn stdin records) |
| `--output-last-message <path>` |  | Write only the final assistant response text to a file |
| `--resume <id\|title>` |  | print: resume a persisted session by id or title |
| `--continue` |  | print: continue the most recently updated persisted session |
| `--session-id <id>` |  | print: run under a specific session id, creating it if absent |
| `--fork-session` |  | print: persist the resumed run under a new session id |
| `--no-session-persistence` |  | print: do not persist the session to disk for this run |
| `--yolo` |  | Auto-approve tool calls |
| `--approval-policy <policy>` |  | interactive: `ask`, `auto`, or `deny`; print/probe: `auto` or `deny` |
| `--output-format <format>` |  | `text`, `json`, or `jsonl` depending on the command |
| `--no-mcp` |  | Interactive only: skip MCP server initialization |
| `--ignore-cert-errors` | `--insecure` | Disable TLS certificate validation for mux-owned network requests |
| `--verbose` | `-v` | Extra progress to stderr in text mode |

### Interactive Commands

Every command is also reachable by key binding and the `F1` menu (one catalog, three surfaces).

```text
/endpoint, /model                 Open the endpoints / models picker (also Ctrl+E)
/endpoints, /models               Aliases for /endpoint
/mcp, /mcp-servers, /servers      Open the MCP servers manager (add / edit / remove)
/skills, /skill                   Open the skills manager (inventory / create / import)
/prompts                          Open the prompt-profile editor (also Ctrl+P)
/effort, /reasoning               Set the active endpoint's reasoning effort level
/thinking, /think                 Toggle displaying the model's reasoning ("thinking")
/sessions                         Browse and resume saved sessions
/tasks                            View and annotate the focused job's task plan
/save                             Save the current session
/theme                            Open the theme selector
/sidebar                          Toggle the sidebar
/borders                          Toggle the boundary lines (off by default)
/mouse                            Toggle mouse capture (on by default)
/menu, /help, /?                  Open the navigable command menu (also F1)
/clear                            Clear the transcript
/exit, /quit, /q                  Quit mux
```

The `/endpoint` picker (also `Ctrl+E`) lists your configured endpoints; pick one to switch the active endpoint for subsequent prompts. The same modal offers **Add**, **Edit**, and **Remove** entries: **Add** and **Edit** run a guided form (adapter, base URL, model, auth mode — `none`, `bearer token`, or `custom headers` — default status, endpoint-scoped tool auto-approval, and optional advanced settings) and probe before saving; **Remove** asks for confirmation and refuses to delete the endpoint active in the current session. All three persist to `endpoints.json`. Endpoints can persist `autoApproveTools: true` so tool calls auto-approve whenever that endpoint is active unless CLI approval flags override it, and can set `maxAgentIterations` (leave it `null` to inherit the global `settings.json` default).

For secret values, the form lets you either store the value directly in `endpoints.json` or store an environment-variable reference. It accepts a bare variable name plus `${VAR}`, `%VAR%`, `$VAR`, and `$env:VAR`, then stores environment references canonically as `${VAR}`. For `ollama`, mux uses Ollama's native API root, so the usual base URL is `http://localhost:11434` (no `/v1`); a trailing `/v1` is tolerated and stripped for this adapter.

Below the configured endpoints (which are separated from the actions by a blank row), the picker also offers **Import from Ollama…** for bulk-importing completion models from a running Ollama server:

1. It prompts for the server's base URL, defaulting to `http://localhost:11434`. A missing scheme is filled in with `http://`, and a trailing `/v1` or slash is stripped so the value matches Ollama's native API root.
2. It queries the server's installed models (`GET /api/tags`) and presents a scrollable multi-select checklist of what it finds. `Space` toggles the highlighted model, `a` toggles all, `Enter` imports the checked models, and `Esc` cancels.
3. Because Ollama's model list does not distinguish completion models from embedding models, **every** installed model is shown with a reminder to select completion models only — leave embedding models (for example `nomic-embed-text`) unchecked.

Any model whose model-plus-endpoint combination is already configured is left out of the checklist, so re-running the import never creates duplicates (the picker's title notes how many were already imported). Each selected model is saved to `endpoints.json` as a new `ollama` endpoint pointing at the normalized base URL, named after the model and de-duplicated against your existing endpoint names, and becomes selectable like any other endpoint.

The `/mcp` command (aliases `/mcp-servers`, `/servers`; also on the `F1` menu under **Model**) opens the MCP servers manager, which edits `mcp-servers.json` through the same modal style as the endpoints picker. Each server row shows a live connectivity glyph — `●` online (with its discovered tool count), `○` offline — followed by the server name and transport detail. The list (separated from the actions by a blank row) sits above a **+ Add MCP server…** entry and, when servers exist, a **- Remove MCP server…** entry; selecting a server row opens its **Edit** form. **Add** and **Edit** run a guided form: a **name**, a **transport** (`stdio` or `http`), and the transport-specific fields — **command**, space-separated **args**, and comma-separated `KEY=VALUE` **env** for `stdio`; **url**, **mcp path** (default `/mcp`), and an **auth** scheme for `http`. Only the selected transport's fields are shown, so switching transports never leaves the other transport's stale values behind. For `http`, the auth scheme is **none**, a **bearer token**, or an **API key** sent in a caller-specified header (default `X-API-Key`); the secret is entered masked, persisted in `mcp-servers.json` under the server's `auth` object, and attached to every request to that server (token and key values support `${VAR}` environment-variable expansion, so secrets can live in the environment rather than in plaintext). The form validates that a `stdio` server has a command and an `http` server has a url. **Remove** asks for confirmation.

Configured MCP servers are connected live: on startup mux connects to each server, queries it for its available tools, and both registers those tools as callable (so the model can invoke them, routed back to the owning server) and appends them to the system prompt so the model is explicitly aware of them. Connectivity is re-validated on a periodic timer, and down servers are periodically retried; the manager's glyphs reflect the current state. Adding, editing, or removing a server through the modal reconnects in the background and takes effect on the next turn — no restart required.

### Reasoning Effort

Reasoning-capable models can trade latency and cost against how hard they think. mux carries one choice — a level — and translates it per backend, so picking `high` sends `reasoning_effort: high` to an OpenAI-compatible endpoint, a dynamic thinking budget to Gemini, and `think: high` to Ollama. The level is stored per endpoint in `endpoints.json`; leaving it unset (the default) sends no reasoning field, so existing endpoints behave exactly as before.

`/effort` (alias `/reasoning`; also on the `F1` menu under **Model**) opens a picker — **Off · Minimal · Low · Medium · High** — with the active level marked. Choosing one persists it to the active endpoint and applies it to the next turn; the sidebar shows an `EFFORT` line so the current level stays visible. The endpoint Add/Edit form also carries a **Reasoning effort** field and an advanced **Gemini thinking budget** field for per-endpoint tuning.

Each level's default projection per provider (every value is individually overridable):

| Level | OpenAI `reasoning_effort` | Gemini `thinkingBudget` | Ollama `think` |
|---|---|---|---|
| Off | *(omitted)* | *(omitted)* | *(omitted)* |
| Minimal | `minimal` | `0` | `false` |
| Low | `low` | `1024` | `low` |
| Medium | `medium` | `8192` | `medium` |
| High | `high` | `-1` (dynamic) | `high` |

Headless runs set the level with `--effort <level>` (with `off` forcing it off even when the endpoint sets a level) and tune the per-provider values with `--effort-openai-value`, `--effort-gemini-budget`, and `--effort-ollama-think`. The provider overrides apply only when a level is active. A run reports its effective selection in the `run_started` JSONL event under `reasoningEffort`, and `cliOverridesApplied` lists `reasoningEffort` when a flag drove the value.

```bash
mux print --yolo --effort high "review this diff and explain the tradeoffs"
mux print --yolo --effort medium --effort-gemini-budget 16000 "summarize the design doc"
```

Reasoning effort reaches Gemini as `thinkingConfig` only through mux's native Gemini path; when you reach a Gemini model through an OpenAI-compatible endpoint, the level ships as `reasoning_effort` instead. Backends whose model has no reasoning concept ignore the field.

### Model Thinking

Where effort controls how hard a model thinks, thinking display controls whether you see it. Reasoning models emit their deliberation on a separate channel, and mux can surface it — dimmed, above the answer in the chat transcript, and as its own event in headless. It is a property of the endpoint (`showThinking` in `endpoints.json`), off by default, so nothing changes until you turn it on.

`/thinking` (alias `/think`; also on the `F1` menu under **View**) toggles it for the active endpoint, persists the choice, and applies it to the next turn; the sidebar shows a `THINK on/off` line, and the endpoint Add/Edit form carries a **Show thinking (reasoning)** checkbox. When on, thinking streams into the transcript under a dim `💭 thinking` header, kept visually distinct from the answer and never mixed into it. Thinking is display-only: mux never sends the model's reasoning back to the model on the next turn.

Headless runs enable it with `--show-thinking` (overriding the endpoint for that run). In `--output-format jsonl`, thinking arrives as `assistant_thinking` events; in `text` mode it is written to stderr so stdout stays the answer (and `--output-last-message` stays clean).

```bash
mux print --yolo --show-thinking --output-format jsonl "explain the tradeoffs in this design"
```

### Skills

A skill is a versioned folder under `~/.mux/skills/<id>/` holding a `SKILL.md` — YAML frontmatter (name, description, when-to-use, mutation posture, tags, and commands) plus a Markdown body — and optional bundled scripts. Each command runs a fenced code block or a bundled script through an allowlisted interpreter (`bash`, `sh`, `pwsh`, `python`, `node`, `dotnet-script`) with a timeout and captured output, so a fuzzy request becomes the same fixed procedure every time. The Markdown carries judgment; the code carries determinism.

On startup mux discovers every skill, lists the enabled ones in the system prompt, and exposes two tools to the model: `skill` (read a skill's instructions and commands) and `run_skill` (execute one command deterministically, returned like `run_process`). A skill's code runs under the approval policy and the workspace write lease, the same posture as `run_process`. Caller arguments are passed to the interpreter as separate argv entries with no shell in between, so shell metacharacters in an argument are delivered literally rather than interpreted. A curated library of 46 default skills — spanning git and GitHub (`git-status-vs-head`, `git-commit`, `git-sync`, `git-stash-manager`, `git-secret-scan`, …), .NET build and quality (`dotnet-build`, `dotnet-test`, `dotnet-format`, `ci-repro`, …), repository hygiene (`todo-scan`, `gitignore-audit`, `line-ending-check`, …), scaffolding (`new-class`, `new-tool`, `new-skill`, …), documentation (`doc-sync`, `adr-new`, …), and developer workflow (`standup-summary`, `release-notes`, `env-report`, `json-validate`, …) — is seeded on first run; on upgrade, mux tops up your `~/.mux/skills` with any newly shipped defaults (tracked in a `.seeded-defaults` manifest) without resurrecting ones you deleted or overwriting your edits.

The `/skills` command (alias `/skill`; also on the `F1` menu under **Model**) opens the manager. The inventory lists each skill with a state glyph — `●` enabled, `○` disabled, `⚠` invalid (the detail view shows why) — its command count, and its tags. Per-skill actions cover view, in-app edit (a near-full-screen `SKILL.md` editor — `Ctrl+S` saves and re-validates, `Esc` cancels), enable/disable, duplicate, and remove; a **+ New skill…** wizard walks you through id, title, description, read-only vs mutating, and interpreter, then writes a working starter skill; and **⬇ Import skill…** brings one in from a local path after validating it. Enable/disable state lives in `~/.mux/skills.json` so toggling never rewrites a hand-edited `SKILL.md`. The same operations are available non-interactively:

```text
mux skill list                       # inventory with validity and enablement
mux skill show <name>                # metadata, commands, and body
mux skill validate [<name>]          # validate one or all; nonzero exit on failure (CI gate)
mux skill run <name> <command> [--arg v ...] [--cwd dir]   # execute deterministically
mux skill new <name>                 # scaffold a skill
mux skill add <path>                 # import from a directory
```

Skills are documented in full in `SKILLS_AUTHORING.md`.

The `/borders` command (aliases `/boundaries`, `/lines`; also on the `F1` menu under **View**) toggles optional dark-grey boundary lines, off by default. When on, the shell draws a horizontal rule above the prompt input, a horizontal rule above the queued-messages strip (when one is shown), and a vertical rule in the gutter to the left of the sidebar. The choice persists to `settings.json` as `showBoundaryLines` (also settable there directly) and is applied on the next launch.

External search is configured in `settings.json` (Tavily or You.com); when at least one enabled provider is fully configured the `web_search` tool is enabled. `web_search` discovers candidate results; fetching the contents of a known URL is handled by `web_retrieve`.

### Background Tasks

When a request is large enough to need more than a couple of steps — porting a pattern across several
files, wiring an endpoint through routing and tests — `mux` lets the model break the work into a plan of
tasks and track it as it goes. The model calls two tools to do this: `plan_tasks` lays out the tasks (each
with a short id, a title, and optional `dependsOn` prerequisites), and `update_task` advances one task's
status as work starts and finishes. You do not call these tools; the model does, and the system prompt
tells it when planning is worth the effort. Task planning is on by default and can be turned off with
`taskPlanningEnabled` in `settings.json`.

In the interactive shell the plan renders as a live checklist inside the transcript, updating in place as
each task moves from pending (`◻`) to running (`◼`) to done (`✔`) — a failed task shows `✗` with the
reason, a blocked one `▦`, a skipped one `⊘`. The sidebar shows overall progress as `TASKS n/m`. Because
the checklist holds its place in the transcript, a task completing several turns later updates the original
line rather than reprinting the list. The plan is saved with the session, so resuming a session shows it
exactly where it stood.

Open `/tasks` (also on the `F1` menu under **View**) to inspect the focused job's plan and annotate it by
hand: `c` marks the selected task complete, `i` marks it in progress, `b` blocked, `k` skipped, `p` pending,
and `n` edits its note. Manual changes apply to the same plan the model edits, so they persist and show up
in the sidebar.

For automation, every plan change is a `task_plan_updated` event in `mux print --output-format jsonl`,
carrying the full task snapshot and what changed, and `run_completed` includes a `taskSummary` tally — so an
orchestrator driving `mux` non-interactively can follow subtask progress the same way it follows tool calls.
An opt-in orchestration engine, `TaskOrchestrator`, runs a task DAG as parallel jobs under the same
single-writer workspace lease that protects concurrent jobs. It is gated by `taskParallelismEnabled`
(default off) and is not yet wired into the interactive shell, so today's interactive experience tracks
one job's plan; the engine is used for programmatic orchestration.

### Interactive Input

Interactive mode works like a chat client. Prompt entry supports multi-line editing and paste. After you press `Enter` the prompt runs; you can keep typing and submitting while a turn is in flight, and those prompts queue to run in order as each turn finishes. The sidebar shows the current status and how many prompts are queued.

Each interactive session also maintains a short conversation title. By default mux asks the current model to revisit that title periodically as the discussion evolves. If you set a title manually with `/title <text>`, mux keeps that title fixed until you change it again.

While idle, `Up` and `Down` recall submitted prompts from the current session. `Shift+Enter` and `Ctrl+Enter` insert a newline so you can compose or paste multi-line prompts before submission.

`Esc` cancels the running turn. If a tool approval is needed, mux shows an approval modal — **Approve once**, **Deny**, or **Always this session** (which auto-approves the rest of the session).

New prompts are checked against the context budget before each run. When a prompt would exceed the usable context budget, mux automatically compacts older persisted history before sending the next model call, using the configured compaction strategy (`summary` uses a summary sidecar pass first and trims only if needed; `trim` stays trim-only). `/clear` clears the transcript.

### Interactive Examples

```text
mux> read README.md and suggest improvements
mux> refactor the UserService class to be async
mux> run the tests and fix failures
mux> retrieve https://example.com and summarize the returned text
mux> search the web for recent mux release notes, then retrieve the most relevant result
```

### Web Search And Retrieval

`mux` exposes two separate web tools when tool calling is enabled for the selected endpoint:

- `web_retrieve` fetches a specific HTTP or HTTPS URL with a headless Playwright browser and returns rendered text, title, final URL, status, content type, and optional HTML. It supports Chromium by default and Firefox by request. If the requested Playwright browser is not installed, mux asks Playwright to install it on demand.
- `web_search` searches the public web through configured external providers and returns structured candidate results with URLs and snippets. Tavily and You.com are supported. The tool is exposed only when external search is enabled and at least one enabled provider is fully configured.

Use `web_search` to discover what exists. Use `web_retrieve` to inspect a selected URL or any URL you already know.

Enterprise TLS inspection can cause errors such as `SELF_SIGNED_CERT_IN_CHAIN` while mux connects to LLM endpoints, calls external search providers, retrieves web pages, or downloads Playwright browsers. Use `--ignore-cert-errors` or its `--insecure` alias to disable TLS certificate validation for mux-owned network requests for that run. The same behavior can be enabled with `settings.json` field `ignoreCertErrors: true` or environment variable `MUX_IGNORE_CERT_ERRORS=1`. mux prints a warning when this mode is active. This does not change TLS behavior for commands launched through `run_process` or for external processes started by MCP servers.

### Single-Shot Examples

```bash
mux print --yolo "read README.md and summarize it"
mux print --yolo --endpoint openai-gpt4o "explain this repository"
echo "refactor AuthService" | mux --print --yolo
```

## Automation Contract

Use `mux print` as the non-interactive entrypoint:

```bash
mux print --output-format jsonl --yolo "implement the feature described in TASK.md"
```

If you need a clean final-response artifact for an orchestrator, add:

```bash
mux print --output-format jsonl --output-last-message result.txt --yolo "implement the feature described in TASK.md"
```

`result.txt` contains only the final assistant response text. If the run fails, mux does not create the file.

For a single machine-readable object instead of a stream, use `--output-format json`:

```bash
mux print --output-format json --yolo "summarize README.md" | jq '.result'
```

`json` emits exactly one object at the end of the run — `result`, `status`, `sessionId`,
`iterationsCompleted`, `toolCallCount`, `errorCount`, `durationMs`, `finalEstimatedTokens`,
`compactionCount`, optional `taskSummary`, and `contractVersion` — with the same secret redaction as the
`jsonl` stream. Failures still report on `stderr` with a non-zero exit code rather than a summary object.

Print runs can be resumed non-interactively. Capture the `sessionId` from one run and continue it in the
next:

```bash
sid=$(mux print --output-format json --session-id review-1 --yolo "start a security review" | jq -r '.sessionId')
mux print --resume "$sid" --output-format json --yolo "now scan the dependencies" | jq -r '.result'
```

Session persistence is opt-in: a plain `mux print` stays stateless, and only a session flag (`--resume`,
`--continue`, `--session-id`, or `--fork-session`) engages the store. Print sessions share the one store
with the interactive `/sessions` browser, so a session started in either surface can be continued in the
other; `--no-session-persistence` reads a session without writing the run back.

For a scripted multi-turn conversation in one process, use `--input-format jsonl`: each stdin line is a
turn record (`{"prompt":"..."}`, or `text`/`content`, or a bare JSON string) and runs against the
accumulating history, so turn N sees turns 1..N-1. Output follows `--output-format` per turn.

```bash
printf '{"prompt":"start a plan"}\n{"prompt":"now refine step 2"}\n' | mux print --input-format jsonl --output-format jsonl --yolo
```

In `jsonl` mode:
- all structured events are written to `stdout`
- each line is a complete JSON object
- default human-readable progress output is suppressed
- every event includes `contractVersion`
- `run_started` and `run_completed` include `sessionId` (empty when the run is not persisted)
- `run_started` includes `sandboxPosture` (`none`, `read-only`, or `workspace-write`); tools refused by the allow/deny lists or the posture surface as `error` events with code `tool_call_denied` (exit `2`)
- `run_started` includes effective non-interactive capability metadata such as `commandName`, `endpointSelectionSource`, `cliOverridesApplied`, built-in tool counts, and MCP support/config status
- `run_started` also includes loop/context metadata such as `maxIterations`, `contextWindow`, `reservedOutputTokens`, `usableInputLimit`, `warningThresholdTokens`, `tokenEstimationRatio`, and `compactionStrategy`
- `run_started` includes `ignoreCertErrors` so consumers can detect whether certificate validation was disabled for mux-owned network requests
- `run_started` includes `reasoningEffort` (the effective level and any per-provider overrides, or `null` when off); `cliOverridesApplied` lists `reasoningEffort` when a `--effort*` flag drove the value
- `run_started` includes `showThinking`; when thinking is enabled, the model's reasoning streams as `assistant_thinking` events, and `cliOverridesApplied` lists `showThinking` when `--show-thinking` drove it
- `run_completed` also includes `finalEstimatedTokens` and `compactionCount`, and reports `status` `budget_exceeded` when `--max-token-budget` stops the run
- `error` events keep `code` and also expose `errorCode`, `failureCategory`, and resolved runtime metadata when known (including `budget_exceeded`, classified as `runtime`)

Event types currently emitted:
- `run_started`
- `assistant_text`
- `assistant_thinking`
- `tool_call_proposed`
- `tool_call_approved`
- `tool_call_completed`
- `heartbeat`
- `context_status`
- `context_compacted`
- `error`
- `run_completed`
- `task_plan_updated`

Default `text` mode for `mux print` remains:
- `stdout`: assistant text
- `stderr`: progress, denial notices, and errors

Exit codes:
- `0`: success
- `1`: config, runtime, backend, or command failure
- `2`: tool call denied

Non-interactive constraints:
- `mux print` loads MCP servers only when `--mcp-config` is supplied (opt-in); otherwise it stays hermetic. `mux probe` never loads MCP
- `--no-mcp` is interactive-only and is rejected in `print` and `probe`
- `--approval-policy ask` is rejected in `print` and `probe`; use `auto` or `--yolo`, or `deny`

Two more print options for orchestrators:
- `--output-schema <path>` folds a JSON Schema directive into the prompt and validates the response recursively (`type` incl. unions/`integer`, `enum`, `required`, nested `properties`, and array `items`; value-level bounds/patterns/formats are not enforced); a non-conforming response fails with `schema_validation_failed` (exit `1`)
- `--mcp-config <path|json>` (+ `--strict-mcp-config`) connects MCP servers for the single run and disposes them on exit

### SDKs

Thin driver SDKs wrap this contract with typed events, an aggregated result, and multi-turn `Thread`s that
persist through mux sessions. Both spawn `mux print --output-format jsonl` rather than binding to internals,
so any language can integrate the same way by consuming the JSONL stream directly.

- [`@mux/sdk`](sdk/typescript/README.md) (TypeScript / Node)
- [`mux-sdk`](sdk/python/README.md) (Python)

```ts
import { Mux } from "@mux/sdk";
const mux = new Mux({ yolo: true, sandbox: "workspace-write" });
const result = await mux.run("implement the feature described in TASK.md");
console.log(result.text, result.exitCode);
```

```python
from mux_sdk import Mux, MuxOptions
mux = Mux(MuxOptions(yolo=True, sandbox="workspace-write"))
result = mux.run("implement the feature described in TASK.md")
print(result.text, result.exit_code)
```

## Probe Command

`mux probe` uses the same config resolution path as `mux print` and performs a lightweight backend validation.

Examples:

```bash
mux probe
mux probe --output-format json
mux probe -e openai-gpt4o
mux probe --output-format json --require-tools
```

`probe` verifies:
- endpoint selection and config loading
- backend reachability
- auth/header configuration
- model access through a minimal completion request

Machine-readable `probe` output also includes:
- `contractVersion` for explicit parser compatibility
- effective config/runtime metadata such as `configDirectory`, `endpointSelectionSource`, and `cliOverridesApplied`
- capability data such as `toolsEnabled`, built-in tool counts, and MCP support/config state
- classified failures via `errorCode` and `failureCategory`

Probe-specific option:
- `--probe-prompt <text>` overrides the default confirmation prompt used during backend validation
- `--require-tools` fails when the selected endpoint disables tool calling

## Endpoint Command

`mux endpoint` exposes stored endpoint configuration through a non-interactive CLI surface.

Examples:

```bash
mux endpoint list
mux endpoint ls
mux endpoint list --output-format json
mux endpoint ls --output-format json
mux endpoint show openai-gpt4o
mux endpoint show openai-gpt4o --output-format json
```

`endpoint list` and `endpoint ls` return configured endpoint names. `endpoint show` returns one configured endpoint with secret-like header values redacted, tool-calling capability, and persisted tool-approval mode. Use `--config-dir` when you need to inspect an isolated config directory.

## Configuration

Default config directory:

```text
~/.mux/
```

Override it for isolated or concurrent runs:

```bash
# Bash
export MUX_CONFIG_DIR=/tmp/mux-run-1
export MUX_IGNORE_CERT_ERRORS=1

# PowerShell
$env:MUX_CONFIG_DIR = "C:\\temp\\mux-run-1"
$env:MUX_IGNORE_CERT_ERRORS = "1"
```

Or pass a first-class CLI override:

```bash
mux print --config-dir /tmp/mux-run-1 --output-format jsonl --yolo "run the task"
mux print --ignore-cert-errors --output-format jsonl --yolo "run behind enterprise TLS inspection"
```

When both are set, `--config-dir` wins over `MUX_CONFIG_DIR`.

Main files:
- `endpoints.json`
- `mcp-servers.json`
- `settings.json`
- `system-prompt.md`

See [CONFIG.md](CONFIG.md) for the full reference.

## Documentation

- [GETTING_STARTED.md](GETTING_STARTED.md)
- [USAGE.md](USAGE.md)
- [CONFIG.md](CONFIG.md)
- [SKILLS_AUTHORING.md](SKILLS_AUTHORING.md)
- [ARMADA.md](ARMADA.md)
- [TESTING.md](TESTING.md)
- [HEADLESS_COMPARISON.md](HEADLESS_COMPARISON.md)
- [TypeScript SDK](sdk/typescript/README.md)
- [Python SDK](sdk/python/README.md)
- [CHANGELOG.md](CHANGELOG.md)

## License

[MIT](LICENSE.md)
