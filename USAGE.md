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
`/endpoint` (`/model`), `/effort` (`/reasoning`), `/help` (`/?`), `/clear`, `/sidebar`, `/save`,
`/sessions`, `/tasks`, `/theme`, `/mouse`, `/menu`, `/quit` (`/exit`).

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

Interactively, the model works one job's plan at a time (it keeps a single task `in_progress`), so the
checklist tracks progress rather than fanning out to concurrent jobs. The opt-in `taskParallelismEnabled`
(default off) gates the `TaskOrchestrator` engine, which runs a task DAG as parallel jobs under the shared
write lease for programmatic orchestration; it is not yet wired into the interactive submit path.

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

The `json` object carries `result`, `status`, `sessionId`, `iterationsCompleted`, `toolCallCount`,
`errorCount`, `durationMs`, `finalEstimatedTokens`, `compactionCount`, an optional `taskSummary`, and
`contractVersion`, with the same secret redaction as the `jsonl` stream. A failed run reports on `stderr`
with a non-zero exit code rather than emitting a summary object.

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
{"contractVersion":1,"eventType":"run_started","timestampUtc":"2026-03-31T20:00:00Z","runId":"...","endpointName":"ollama-local","model":"qwen2.5-coder:7b","maxIterations":50}
{"contractVersion":1,"eventType":"assistant_text","timestampUtc":"2026-03-31T20:00:01Z","text":"Here is the summary..."}
{"contractVersion":1,"eventType":"run_completed","timestampUtc":"2026-03-31T20:00:02Z","runId":"...","status":"completed","durationMs":1042}
```

Notes:
- machine-readable output is on `stdout`
- secret-like values in structured payloads are redacted on a best-effort basis
- default text mode is unchanged
- `run_started.mcp.supported` is `false` in `print` unless `--mcp-config` is supplied; with it, `mcp.configured`/`mcp.serverCount` reflect the loaded servers
- `run_started` and `run_completed` carry `sessionId` (empty when the run is not associated with a persisted session)
- `run_completed.status` is `completed`, `completed_with_errors`, `max_iterations_reached`, or `budget_exceeded`; the matching `error` event code `budget_exceeded` is classified as `runtime`
- `run_started` now includes `maxIterations`, context-budget metadata, and `ignoreCertErrors`, and `run_completed` includes `finalEstimatedTokens` plus `compactionCount`
- `context_status` and `context_compacted` are additive event types within `contractVersion = 1`; consumers should ignore unknown event types in a known contract version
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

## Skills

Skills are versioned Markdown-plus-code capabilities under `~/.mux/skills`. Each is a folder with a `SKILL.md` — frontmatter plus a body — whose commands run a fenced code block or a bundled script through an allowlisted interpreter with a timeout and captured output, turning a request into a fixed, deterministic procedure. The interactive shell discovers skills on startup, lists the enabled ones in the system prompt, and exposes `skill` (read a skill's instructions) and `run_skill` (execute a command, gated by the approval policy and the write lease). A curated default set is seeded on first run and preserved on upgrade.

Manage skills in-app with `/skills` (aliases `/skill`; also on the `F1` menu under **Model**): the inventory shows a state glyph (`●` enabled, `○` disabled, `⚠` invalid), command counts, and tags; per-skill actions cover view, enable/disable, duplicate, and remove; a **+ New skill…** wizard scaffolds a working skill; and **⬇ Import skill…** brings one in from a local path. Enablement lives in `~/.mux/skills.json`, separate from each `SKILL.md`.

The same operations run non-interactively:

```bash
mux skill list                       # inventory with validity and enablement
mux skill show <name>                # metadata, commands, and body
mux skill validate [<name>]          # validate one or all; nonzero exit on failure (CI gate)
mux skill run <name> <command> [--arg v ...] [--cwd dir]   # execute deterministically
mux skill new <name>                 # scaffold a skill
mux skill add <path>                 # import from a directory
```

`mux skill run` returns the same `stdout`/`stderr`/`exit_code` contract the agent sees, so a Git hook or CI job can invoke a curated procedure with no model in the loop. The full authoring reference is in `SKILLS_AUTHORING.md`.

Settings in `settings.json`: `skillsEnabled` (default `true`), `skillRefreshIntervalSeconds` (default `30`), and `skillsDirectory` (override the default `~/.mux/skills`).

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

Structured non-interactive output uses a shared `contractVersion`.

Compatibility rules:
- additive fields are non-breaking within a contract version
- consumers should ignore unknown fields within a known contract version
- a contract-version bump is required for removals, renames, type changes, or semantic changes to required fields
