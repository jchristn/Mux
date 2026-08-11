# Headless & CLI Capabilities: mux vs Claude Code vs Codex

A thorough, honest comparison of the **non-interactive (headless) execution surfaces** and
**command-line argument surfaces** of three CLI coding agents:

- **mux** — this repository. Backend-agnostic CLI agent for local/remote LLMs.
- **Claude Code** — Anthropic's official CLI agent (the `claude` command).
- **OpenAI Codex CLI** — OpenAI's open-source Rust coding agent (the `codex` command).

> **Scope and honesty note.** mux is **alpha software** (v0.6.0). Claude Code (~v2.1.x) and Codex
> (~v0.12x–0.13x) are mature, well-resourced commercial tools tied to a single model provider. This
> document does not pretend the three are at feature parity — they are not. The goal is an accurate,
> side-by-side account of what each actually does in headless/scripted use, where mux is competitive,
> where it made a deliberately different design choice, and where it is simply less mature. mux facts
> were verified directly against this repository's source (`src/Mux.Cli/**`); Claude Code and Codex
> facts come from their official docs and repositories as of early 2026 and are noted where
> version-sensitive.

---

## 1. TL;DR

| Dimension | mux | Claude Code | Codex CLI |
|---|---|---|---|
| **Core design goal** | Bring-your-own-backend (Ollama, OpenAI, vLLM, LM Studio, Azure, any OpenAI-compatible) | Anthropic models only | OpenAI models (+ `--oss` local) |
| **Headless entry point** | `mux print` / `-p` / `--print` | `claude -p` / `--print` | `codex exec` (alias `codex e`) |
| **Structured event stream** | ✅ `--output-format jsonl` (11 event types, versioned, secret-redacted) | ✅ `--output-format stream-json` (rich, subagent-aware) | ✅ `--json` (JSONL `thread`/`turn`/`item` events) |
| **Single-object JSON result** | ✅ `--output-format json` (v0.7.0) | ✅ `--output-format json` | ✅ via `-o`/`--output-last-message` + `--json` |
| **Final-message-only artifact** | ✅ `--output-last-message` | ➖ (in `json` result field) | ✅ `-o` / `--output-last-message` |
| **Structured output / JSON schema** | ➖ `--output-schema` (recursive client-side validation, v0.7.0; not provider-native) | ✅ `--json-schema` | ✅ `--output-schema` |
| **Multi-turn / structured input** | ✅ `--input-format jsonl` (v0.7.0) | ✅ `--input-format stream-json` | ➖ (stdin = prompt via `-`) |
| **Headless session resume** | ✅ `--resume`/`--continue`/`--fork-session` (v0.7.0) | ✅ `--continue` / `--resume` / `--fork-session` | ✅ `codex exec resume [--last]` |
| **Per-tool allow/deny** | ✅ `--allow-tools`/`--deny-tools` (v0.7.0) | ✅ `--allowedTools` / `--disallowedTools` | ➖ (governed by sandbox, not per-tool) |
| **Sandbox / confinement** | ➖ app-level `--sandbox read-only\|workspace-write` (v0.7.0; not OS-level) | ➖ (permission modes, not OS sandbox) | ✅ OS-level `read-only` / `workspace-write` / `danger-full-access` |
| **Headless MCP** | ✅ opt-in `--mcp-config` (v0.7.0) | ✅ `--mcp-config` | ✅ config.toml / `codex mcp` |
| **Auto-approve / "yolo"** | ✅ `--yolo` / `--approval-policy auto` | ✅ `--dangerously-skip-permissions` / modes | ✅ `--yolo` / `--full-auto` |
| **Health-check subcommand** | ✅ `mux probe` (machine-readable) | ➖ `claude doctor` (diagnostics) | ❌ |
| **Config-dir isolation flag** | ✅ `--config-dir` / `MUX_CONFIG_DIR` | ✅ `CLAUDE_CONFIG_DIR` (env) | ✅ `CODEX_HOME` (env) |
| **Distinct exit codes** | ✅ `0` / `1` / `2` (tool-denied) | ➖ `0` / `1` / `143` (SIGTERM) | ➖ `0` / non-zero |
| **Programmatic SDK** | ✅ TS + Python SDKs wrap the CLI (v0.7.0) | ✅ Agent SDK (TS + Python) | ✅ TS SDK (wraps `codex exec`) |
| **Deterministic "skill" CLI** | ✅ `mux skill run/validate/...` | ➖ (skills exist, no exec-verb CLI) | ❌ |

Legend: ✅ first-class · ➖ partial/adjacent · ❌ absent.

**One-paragraph summary.** All three expose a real headless mode with a structured event stream, an
auto-approval escape hatch, and MCP somewhere in the product. mux's headless surface is
**deliberately narrow but well-specified**: a single-shot `print` command with a versioned,
secret-redacting JSONL contract, precise three-value exit codes, a machine-readable `probe`
health check, a deterministic `skill` CLI, and strong config-directory isolation for parallel
orchestration. Its **distinctive advantage is backend-agnosticism** — you can point it at any
OpenAI-compatible endpoint from the command line with no config file. Its **distinctive gaps**
versus the two commercial tools are: no headless session resume, no structured-input mode, no
per-tool permission granularity, no OS-level sandbox, no headless MCP, no output-schema
constraint, and no public SDK.

---

## 2. Methodology & versions

| Tool | Version basis | Source of truth |
|---|---|---|
| mux | v0.6.0 (`CHANGELOG.md`) | Verified against `src/Mux.Cli/Commands/*.cs`, `src/Mux.Cli/Program.cs`, `README.md`, `CONFIG.md` |
| Claude Code | ~v2.1.x | Official docs (`code.claude.com/docs`) |
| Codex CLI | ~v0.12x–0.13x | `github.com/openai/codex` + OpenAI hosted docs |

> Minor note for maintainers: `Defaults.ProductVersion` in `src/Mux.Core/Settings/Defaults.cs` still
> reads `"0.5.0"` while `CHANGELOG.md` and recent commits are at `v0.6.0`. The compiled `mux --version`
> therefore lags the changelog by one release.

Claude Code and Codex evolve weekly; some flags noted below are version-sensitive and flagged as such.
mux facts are exact as of the current tree.

---

## 3. Non-interactive entry points

### mux
mux exposes **four** non-interactive command surfaces, all of which resolve config the same way and
never enter the TUI:

| Command | Purpose |
|---|---|
| `mux print [OPTIONS] <prompt>` (also `mux -p` / `mux --print`) | Single-shot agent run |
| `mux probe [OPTIONS]` | Backend/config/auth/model health check |
| `mux endpoint <list\|ls\|show>` | Inspect stored endpoint config |
| `mux skill <list\|show\|validate\|run\|new\|add>` | Deterministic skill inventory & execution |

`mux print` is a **single-shot**: it runs one prompt to completion and exits. The prompt comes from
the positional argument or, if omitted, from **stdin** (`Console.IsInputRedirected` → `ReadToEnd()`).
Verified in `PrintCommand.ResolvePrompt`.

```bash
mux print --yolo "read README.md and summarize it"
echo "refactor AuthService" | mux --print --yolo
mux print --output-format jsonl --output-last-message result.txt --yolo "implement TASK.md"
```

### Claude Code
`claude -p` / `--print` is the primary headless entry point. Prompt via positional arg or stdin (up to
a 10 MB cap). A `--bare` mode exists that skips auto-discovery of hooks/skills/plugins/MCP/memory for
fast, hermetic CI startup (slated to become the `-p` default). Background agents can be launched with
`--bg`.

```bash
claude -p "your prompt"
cat logs.txt | claude -p "find errors"
claude --bare -p "lint the code" --output-format json
```

### Codex CLI
`codex exec` (alias `codex e`) is the headless entry point — one session to completion, then exit.
Progress → **stderr**, final message → **stdout**. Prompt via positional arg or, with the `-` sentinel,
the whole prompt from stdin. It also supports "instruction as arg + context on stdin".

```bash
codex exec "explain this codebase"
cat prompt.txt | codex exec -
tail -n 200 app.log | codex exec "identify the root cause"
```

**Assessment.** All three have a clean single-shot headless mode with stdin support. mux is the only
one that additionally ships **dedicated non-interactive subcommands for health-checking (`probe`) and
config inspection (`endpoint`)**, and a **deterministic skill-execution CLI (`skill run`)**. Codex is
the only one with a first-class stdin sentinel (`-`) that cleanly separates "prompt from stdin" from
"context from stdin". Claude Code is the only one to document a stdin size cap and a hermetic
`--bare` startup path.

---

## 4. CLI argument surface

### 4.1 mux — full flag list (verified from `CliArgumentParser.cs`)

Common flags (apply to `print`, and `probe`; a subset to interactive):

| Flag | Alias | Meaning |
|---|---|---|
| `--help` | `-h`, `-?`, `/?` | Help and exit |
| `--version` | `/version`, bare `-v` | Version and exit |
| `--print` | `-p` | Single-shot mode |
| `--config-dir <path>` | | Override active config directory (wins over `MUX_CONFIG_DIR`) |
| `--endpoint <name>` | `-e` | Named endpoint from `endpoints.json` |
| `--model <name>` | `-m` | Override model |
| `--base-url <url>` | | Override base URL (ad-hoc endpoint) |
| `--adapter-type <type>` | | `ollama` \| `openai` \| `vllm` \| `openai-compatible` |
| `--temperature <float>` | | Override temperature |
| `--max-tokens <int>` | | Override max output tokens |
| `--working-directory <path>` | `-w` | Tool execution directory |
| `--system-prompt <path>` | | Path to a system-prompt **file** (replaces default) |
| `--compaction-strategy <mode>` | | `summary` \| `trim` |
| `--yolo` | | Auto-approve all tool calls |
| `--approval-policy <policy>` | | `ask` (interactive only) \| `auto` \| `deny` |
| `--output-format <format>` | | Per-command: see §5 |
| `--output-last-message <path>` | | (print only) write final assistant text to a file |
| `--no-mcp` | | Interactive only; **rejected** in `print`/`probe` |
| `--ignore-cert-errors` | `--insecure` | Disable TLS validation for mux-owned requests |
| `--verbose` | `-v` | Extra progress to stderr (text mode) |
| `--probe-prompt <text>` | | (probe only) override validation prompt |
| `--require-tools` | | (probe only) fail if endpoint can't use tools |

`skill`/`endpoint` verbs add `--output-format`, `--config-dir`, and (skill) `--cwd`/`--arg`.

**Distinctive:** the `--base-url` + `--adapter-type` + `--model` trio lets you run against **any
OpenAI-compatible backend with no config file at all**:

```bash
mux --base-url http://localhost:11434/v1 --model llama3.1:70b -p --yolo "explain x"
```

Neither Claude Code nor Codex can retarget an arbitrary backend this casually from the CLI (both are
provider-anchored; Codex allows custom providers via `config.toml`, Claude Code via `ANTHROPIC_BASE_URL`
env, but not as a first-class positional CLI concern).

### 4.2 Claude Code — headline flags

Very large surface. Highlights relevant to headless:

- **Output/format:** `-p/--print`, `--output-format text|json|stream-json`, `--input-format text|stream-json`,
  `--json-schema`, `--verbose`, `--include-partial-messages`, `--forward-subagent-text`, `--replay-user-messages`.
- **Session:** `-c/--continue`, `-r/--resume <id|name>`, `--fork-session`, `--session-id <uuid>`,
  `--no-session-persistence`.
- **Model/effort:** `--model`, `--effort low|medium|high|xhigh|max`, `--fallback-model`.
- **Permissions/tools:** `--permission-mode`, `--allowedTools`, `--disallowedTools`, `--tools`,
  `--dangerously-skip-permissions`, `--permission-prompt-tool`.
- **Prompt/config:** `--system-prompt`, `--system-prompt-file`, `--append-system-prompt(-file)`,
  `--settings`, `--setting-sources`, `--add-dir` (multiple dirs), `--mcp-config`, `--strict-mcp-config`,
  `--agents`, `--plugin-dir`.
- **Limits:** `--max-turns`, `--max-budget-usd`, `--autocompact`.

### 4.3 Codex — headline flags

- **Top-level:** `-m/--model`, `-C/--cd`, `-c/--config key=value` (repeatable, dotted keys),
  `-i/--image`, `-s/--sandbox`, `-a/--ask-for-approval`, `--full-auto`, `--yolo`
  (`--dangerously-bypass-approvals-and-sandbox`), `--profile`, `--skip-git-repo-check`, `--oss`, `--search`.
- **`codex exec`:** positional prompt or `-`, `--json`, `-o/--output-last-message`, `--output-schema`,
  `--ephemeral`, `--ignore-user-config`, `--ignore-rules`, plus the shared model/sandbox/approval/config flags.

**Assessment.** Claude Code has by far the **broadest** CLI surface (sessions, budgets, structured I/O,
per-tool rules, multi-dir, plugins, agents, hooks). Codex has a **compact, orthogonal** surface whose
signature move is `-c key=value` overriding *any* config key. mux sits in between in raw count but is
**narrower in capability**: it covers endpoint/model/sampling overrides, working dir, approval, output
format, and config isolation — but has **no** `--max-turns` CLI override (max iterations come only from
settings/endpoint config), **no** multi-directory `--add-dir`, **no** `--append-system-prompt` (only
whole-file replacement via `--system-prompt <path>`), and **no** per-tool flags.

---

## 5. Output formats & structured output

### mux (verified from `StructuredOutputFormatter.cs` and `ParseOutputFormat` call sites)

Output format support is **per-command** and strict — this is an easy point to get wrong:

| Command | Supported `--output-format` |
|---|---|
| `print` | `text`, `jsonl` — **not `json`** |
| `probe` | `text`, `json` — **not `jsonl`** |
| `endpoint` | `text`, `json` |
| `skill` | `text`, `json` |

`mux print --output-format jsonl` emits **one complete JSON object per line**. Properties:

- Every line carries `contractVersion` (currently **`1`**), `eventType`, and `timestampUtc`.
- Default human-readable progress is suppressed; all structured events go to stdout.
- **Built-in secret redaction:** bearer tokens, `sk-…` keys, and `authorization`/`api-key`/`token`/
  `secret`/`password` fields are replaced with `***REDACTED***` in both text and parsed-JSON tool
  arguments/results. (Neither competitor advertises redaction of this kind in its stream.)

**Event types emitted (11):** `run_started`, `assistant_text`, `tool_call_proposed`,
`tool_call_approved`, `tool_call_completed`, `heartbeat`, `context_status`, `context_compacted`,
`error`, `run_completed`, `task_plan_updated`.

Notably rich, automation-friendly payloads:
- `run_started` carries endpoint/adapter/model, `approvalPolicy`, `maxIterations`, `contextWindow`,
  `reservedOutputTokens`, `usableInputLimit`, `tokenEstimationRatio`, `compactionStrategy`,
  `ignoreCertErrors`, tool counts, and an `mcp {supported,configured,serverCount}` block.
- `run_completed` carries `status`, `iterationsCompleted`, `toolCallCount`, `errorCount`,
  `durationMs`, `finalEstimatedTokens`, `compactionCount`, and a `taskSummary` tally.
- `error` carries `code`/`errorCode`, `failureCategory` (e.g. `configuration`, `network`, `backend`,
  `approval`, `tool`, `runtime`), and resolved endpoint metadata.
- `task_plan_updated` streams the full background-task plan snapshot (each task's id/title/status/
  dependsOn/note/duration) so an orchestrator can follow sub-task progress.

`--output-last-message <path>` (print only) atomically writes **only the final assistant text** to a
file, and only on success (no file on failure). Verified in `PrintCommand.WriteLastMessageArtifact`.

**mux does not support constraining the model's output to a JSON schema** — there is no
`--json-schema`/`--output-schema` equivalent.

### Claude Code
`--output-format text|json|stream-json`. `json` returns a single object with `result`, `session_id`,
`model`, `total_cost_usd`, `usage`, and (with `--json-schema`) `structured_output`. `stream-json` is
NDJSON with `system/init`, `assistant`/`user`, `tool_use`/`tool_result`, `stream_event` deltas
(token-level with `--include-partial-messages`), and a final `result` event; subagent messages carry
`parent_tool_use_id`. **`--json-schema`** validates/produces structured output.

### Codex CLI
`--json` → JSONL of public `ThreadEvent`s: `thread.started` (carries thread/session id), `turn.started`/
`turn.completed` (with a `usage` token block), `item.started`/`item.completed` (messages, command
executions, file changes, MCP calls). **`--output-schema <file>`** constrains the final message to a
JSON Schema; **`-o/--output-last-message`** writes the final message to a file.

**Assessment.** All three stream structured events with token/usage accounting. **mux's JSONL is the
most metadata-rich at run boundaries and the only one with built-in secret redaction and an explicit
`contractVersion`.** However, mux is the **only one of the three without an output-schema / structured-
output constraint**, and it lacks a convenience single-object `json` result for `print` (you must
consume the stream and pick the last `assistant_text`/`run_completed`, or use `--output-last-message`).
Cost-in-USD reporting (Claude Code `total_cost_usd`) has no mux analogue — sensible, since mux talks to
backends with no single price model.

---

## 6. Structured input

- **mux:** stdin is treated as a **plain-text prompt** only. No `--input-format`; no way to feed a
  structured/multi-turn event stream in.
- **Claude Code:** `--input-format stream-json` accepts NDJSON on stdin, enabling programmatic
  multi-turn driving and `--replay-user-messages`.
- **Codex:** stdin is the prompt (via `-`), plus the low-level `codex proto` subcommand speaks the raw
  agent protocol over stdin/stdout for advanced drivers.

**Assessment.** Claude Code is clearly ahead here; Codex has an advanced escape hatch (`proto`); mux has
plain-prompt stdin only.

---

## 7. Sessions & resume in headless mode

- **mux:** **No headless session resume.** There is no `--resume`, `--continue`, `--session-id`, or
  `--fork-session` (verified: no such tokens in `src/Mux.Cli/Commands` or `Program.cs`). Sessions are an
  **interactive-only** feature — the TUI autosaves and resumes via `/sessions`, but `mux print` is a
  stateless single-shot every time.
- **Claude Code:** Full headless session control — `--continue`, `--resume <id|name>`,
  `--fork-session`, `--session-id`, `--no-session-persistence`. The `session_id` is returned in JSON
  output so scripts can capture and continue it.
- **Codex:** `codex exec resume --last "<task>"` or `codex exec resume <SESSION_ID>`; the id is surfaced
  in the `--json` stream's `thread.started` event. Sessions persist under `~/.codex/sessions` unless
  `--ephemeral`.

**Assessment.** This is one of mux's **most significant headless gaps**. Both competitors let a CI
pipeline run a step, capture the session id, and continue it later; mux cannot resume a prior run
non-interactively. For multi-step automation, mux expects you to pass full context in each single-shot
prompt (or drive its interactive session, which is out of scope for headless use).

---

## 8. Permissions, approvals & sandboxing

This is where the three tools diverge most in philosophy.

### mux — approval policy (verified in `CommandRuntimeResolver.ResolveApprovalPolicy`)
A single **approval-policy** axis:
- `--yolo` → auto-approve everything.
- `--approval-policy auto` → same (AutoApprove).
- `--approval-policy deny` → block all tool calls.
- `--approval-policy ask` → **rejected in `print`/`probe`** (no interactive prompt available).
- **Default in headless is `deny`** (safe): an endpoint can also opt in via `autoApproveTools: true`.
- Interactive default is `ask`, remapped to "AutoSafe" (read-only tools auto-run, mutating tools
  escalate to the approval modal).

mux has **no per-tool allow/deny lists** and **no OS-level sandbox**. Tool availability is
all-or-nothing per endpoint (`Quirks.SupportsTools`); within an enabled endpoint the model may call any
built-in tool, gated only by the approval policy. Isolation, when you need it, comes from
`--working-directory` plus a disposable `--config-dir`, not from a kernel sandbox.

### Claude Code — permission modes + tool rules
Two mechanisms: **permission modes** (`default`, `acceptEdits`, `plan`, `auto`, `dontAsk`,
`bypassPermissions`, `manual`) and **per-tool pattern rules** (`--allowedTools "Bash(git *)"`,
`--disallowedTools "Bash(rm *)"`, `--tools`), plus `--dangerously-skip-permissions` and a
`--permission-prompt-tool` to route approvals to a custom MCP tool. Fine-grained but not an OS sandbox.

### Codex — sandbox × approval (two orthogonal axes)
Codex's signature model separates **what the agent can technically do** from **when it must ask**:
- **Sandbox** (`-s/--sandbox`): `read-only`, `workspace-write` (network off by default),
  `danger-full-access`. This is a **real OS-level boundary**.
- **Approval** (`-a/--ask-for-approval`): `untrusted`, `on-failure`, `on-request`, `never`.
- Presets: "Auto" = `on-request` + `workspace-write`; `--full-auto`; `--yolo` = full access + never ask.

**Assessment.** For unattended safety, **Codex is strongest** (kernel-enforced sandbox with writable-
roots and network toggles). **Claude Code is strongest for fine-grained tool policy** (allow/deny
patterns, per-tool). **mux is the coarsest**: a single approval axis with a safe `deny` default and no
per-tool or OS sandboxing. mux's model is simple and predictable, and its default-deny in headless is a
reasonable safety posture, but it offers no middle ground between "deny all tools" and "approve all
tools" in a script, and no containment if you choose `--yolo`.

---

## 9. MCP (Model Context Protocol) in headless mode

- **mux:** MCP is **interactive-only**. `mux print` and `mux probe` **do not load MCP servers**, and
  `--no-mcp` is explicitly **rejected** in those modes (verified in
  `CommandRuntimeResolver.ValidateCommandSettings`). The JSONL `run_started` event still reports
  `mcp {supported:false, configured, serverCount}` so orchestrators can see MCP was unavailable. MCP
  servers (stdio + HTTP, with auth) are fully supported in the TUI.
- **Claude Code:** MCP works headless via `--mcp-config <file|json>` and `--strict-mcp-config`; server
  status is reported in the `system/init` event; waits for connect up to `MCP_TIMEOUT`.
- **Codex:** MCP works headless via `config.toml` (`[mcp_servers.*]`, stdio or Streamable HTTP with
  bearer-token env vars) and the `codex mcp` management subcommand; Codex can also (experimentally)
  run *as* an MCP server.

**Assessment.** Another clear mux gap: **mux cannot use MCP tools in headless mode at all.** Both
competitors treat headless MCP as first-class. If your automation depends on MCP tools, mux is not
currently an option for that step.

---

## 10. Backend / provider flexibility

- **mux:** The entire premise. One CLI drives Ollama, OpenAI, vLLM, LM Studio, Azure OpenAI, or any
  OpenAI-compatible API, selectable per run via `--endpoint`, or ad-hoc via `--base-url` +
  `--adapter-type` + `--model` with **no config file required**. Multiple named endpoints live in
  `endpoints.json`; `mux endpoint list/show` inspects them non-interactively.
- **Claude Code:** Anthropic models; `ANTHROPIC_BASE_URL`/gateway env vars allow proxies, but the tool
  is provider-anchored.
- **Codex:** OpenAI models; custom `model_providers.*` in `config.toml` and `--oss` for local models,
  but again provider-anchored by design.

**Assessment.** This is mux's **single biggest differentiator**. For self-hosted, air-gapped,
cost-controlled, or multi-backend automation, mux is the only one of the three built for it from the
ground up, and the only one you can retarget entirely from CLI flags.

---

## 11. Exit codes & error behavior

- **mux (verified):** precise three-value contract — **`0`** success, **`1`** config/runtime/backend/
  command failure, **`2`** tool call denied. In `jsonl` mode, failures are also emitted as structured
  `error` events with `errorCode` + `failureCategory`; in text mode they go to stderr. This is the
  **most differentiated exit-code scheme** of the three (a dedicated "denied" code is genuinely useful
  for CI gating).
- **Claude Code:** `0` success, `1` generic failure, `143` on SIGTERM (graceful abort). Partial
  responses are surfaced in the `result` field on interrupted streams (recent versions). Missing
  plugins/MCP servers are reported as non-fatal fields rather than failing the run.
- **Codex:** conventional `0`/non-zero; no rich documented numeric table. Guidance is to capture the
  `--json` stream and inspect events rather than rely on the code alone (some sandboxed-child error
  surfacing has historically been weak).

**Assessment.** mux's exit codes are the **cleanest and best-documented**; Claude Code adds meaningful
SIGTERM semantics; Codex is the least differentiated and leans on its JSON stream for detail.

---

## 12. Programmatic SDKs

- **mux:** **No public SDK / library.** The intended programmatic surface is `mux print --output-format
  jsonl`, consumed by an external orchestrator. Internally there is a `TaskOrchestrator` for running a
  task DAG as parallel jobs under a single-writer workspace lease, but it is **not exposed as a public
  API** and is gated off by default (`taskParallelismEnabled`).
- **Claude Code:** **Claude Agent SDK** for TypeScript and Python (`@anthropic-ai/claude-agent-sdk`,
  `claude-agent-sdk`) with `query()`, streaming, `ClaudeSDKClient` multi-turn sessions, custom in-process
  MCP tools, and `canUseTool` permission callbacks.
- **Codex:** **TypeScript SDK** (`@openai/codex-sdk`) that thinly wraps `codex exec --json`
  (`startThread`/`resumeThread`, `run`/`runStreamed`, JSON-Schema/Zod structured output, image input).
  No first-class Python SDK — shell out to `codex exec`.

**Assessment.** Both competitors offer a real embedding story; mux's is "shell out and parse JSONL,"
which is workable and language-agnostic but less ergonomic than a native SDK.

---

## 13. Things mux does that the others don't (or do less)

1. **Backend-agnostic from the CLI** — retarget any OpenAI-compatible endpoint with `--base-url`/
   `--adapter-type`/`--model`, no config file (§10).
2. **`mux probe`** — a dedicated, machine-readable health check that validates config loading, backend
   reachability, auth, and model access (`--output-format json`, `--require-tools`, `--probe-prompt`).
   Neither competitor has a direct equivalent; `claude doctor` is diagnostics-oriented, not a scriptable
   endpoint validator.
3. **`mux endpoint list/show`** — non-interactive introspection of stored endpoint config, with
   secret redaction.
4. **`mux skill` CLI** — deterministic, allowlisted skill execution (`skill run`) and a **`skill
   validate` CI gate** (non-zero exit on invalid skills). This turns fuzzy requests into fixed,
   reproducible procedures, callable from scripts.
5. **Built-in secret redaction** in the structured output stream (§5).
6. **Explicit `contractVersion`** on every JSONL line for parser compatibility.
7. **Distinct `exit 2` for tool-denied** (§11).
8. **First-class config-dir isolation** (`--config-dir` beats `MUX_CONFIG_DIR`) designed for
   concurrent/orchestrated runs.
9. **Streaming `task_plan_updated` events** exposing the model's background-task plan to orchestrators.

---

## 14. Where mux is behind (honest gaps)

1. **No headless session resume/continue/fork** (§7) — the biggest gap for multi-step automation.
2. **No structured (stream-json) input** (§6).
3. **No headless MCP** — interactive-only; `--no-mcp` rejected in print/probe (§9).
4. **No per-tool allow/deny and no OS sandbox** — coarse approval axis only; no containment under
   `--yolo` (§8).
5. **No output-schema / structured-output constraint** — no `--json-schema`/`--output-schema` (§5).
6. **No single-object `json` result for `print`** — only the streaming `jsonl` form (§5).
7. **No public SDK** — orchestration is "shell out + parse JSONL" only (§12).
8. **No `--max-turns` CLI override** — iteration cap comes only from settings/endpoint.
9. **No multi-directory (`--add-dir`) and no `--append-system-prompt`** — only `--working-directory`
   and whole-file `--system-prompt` replacement.
10. **No cost/budget accounting** (`--max-budget-usd`, `total_cost_usd`) — reasonable given
    backend-agnosticism, but absent.
11. **Alpha maturity** — CLI behavior, config formats, and the JSONL contract are explicitly subject to
    change (and the version constant currently lags the changelog, §2).

---

## 15. Consolidated feature matrix

| Capability | mux (v0.6.0) | Claude Code (~2.1.x) | Codex CLI (~0.12–0.13x) |
|---|---|---|---|
| Headless single-shot | `print`/`-p`/`--print` | `-p`/`--print` | `codex exec` / `e` |
| Prompt from stdin | ✅ (plain) | ✅ (≤10 MB) | ✅ (`-` sentinel) |
| Structured event stream | ✅ `jsonl` | ✅ `stream-json` | ✅ `--json` |
| Single-object JSON result | ✅ `json` (v0.7.0) | ✅ `json` | ➖ via `-o`+`--json` |
| Final-message file artifact | ✅ `--output-last-message` | ➖ (`result` field) | ✅ `-o` |
| Output JSON-schema constraint | ➖ `--output-schema` (recursive validation, v0.7.0) | ✅ `--json-schema` | ✅ `--output-schema` |
| Multi-turn / structured input | ✅ `--input-format jsonl` (v0.7.0) | ✅ `--input-format` | ➖ `codex proto` |
| Token/usage in stream | ✅ (`run_completed`) | ✅ (`usage`) | ✅ (`turn.completed`) |
| Cost (USD) accounting | ❌ | ✅ | ➖ |
| Secret redaction in stream | ✅ | ➖ | ➖ |
| Versioned output contract | ✅ `contractVersion` | ➖ | ➖ |
| Headless resume/continue | ✅ (v0.7.0) | ✅ | ✅ |
| Fork session | ✅ `--fork-session` (v0.7.0) | ✅ `--fork-session` | ➖ |
| Auto-approve ("yolo") | ✅ | ✅ | ✅ |
| Deny-all tools | ✅ `deny` (default) | ✅ `dontAsk` | ➖ (via sandbox) |
| Per-tool allow/deny | ✅ `--allow-tools`/`--deny-tools` (v0.7.0) | ✅ | ➖ |
| Sandbox / confinement | ➖ app-level (v0.7.0) | ➖ | ✅ OS-level |
| Headless MCP | ✅ opt-in `--mcp-config` (v0.7.0) | ✅ | ✅ |
| MCP auth (headless) | ✅ (via server config, v0.7.0) | ✅ | ✅ |
| Multi-directory access | ✅ `--add-dir` (v0.7.0) | ✅ `--add-dir` | ➖ (`-C`) |
| System prompt override | ✅ file replace + `--append-system-prompt` (v0.7.0) | ✅ replace + append | ➖ (config/instructions) |
| Max-turns / iteration cap (CLI) | ✅ `--max-turns` (v0.7.0) | ✅ `--max-turns` | ➖ (config) |
| Budget cap (CLI) | ✅ `--max-token-budget` (tokens, v0.7.0) | ✅ `--max-budget-usd` | ➖ |
| Config-dir isolation | ✅ `--config-dir` | ✅ env | ✅ `CODEX_HOME` |
| Ad-hoc arbitrary backend (CLI) | ✅ | ❌ | ➖ (config/`--oss`) |
| Health-check subcommand | ✅ `probe` | ➖ `doctor` | ❌ |
| Config-inspection subcommand | ✅ `endpoint` | ➖ `config` | ➖ (`config.toml`) |
| Deterministic skill CLI | ✅ `skill run/validate` | ➖ | ❌ |
| Distinct exit codes | ✅ 0/1/2 | ➖ 0/1/143 | ➖ 0/non-zero |
| Public SDK | ✅ TS + Python (v0.7.0) | ✅ TS+Py | ✅ TS |
| Backend-agnostic | ✅ (many) | ❌ (Anthropic) | ➖ (OpenAI+`--oss`) |

Legend: ✅ first-class · ➖ partial/adjacent/via-config · ❌ absent.

---

## 16. Bottom line

- **Choose Claude Code** for the richest headless surface overall: structured I/O both directions,
  output schemas, fine-grained per-tool permissions, headless sessions and forking, budgets, plugins,
  and mature TS/Python SDKs — all anchored to Anthropic models.
- **Choose Codex** for the strongest unattended-safety story: a real OS sandbox crossed with an
  approval policy, clean `-c key=value` config overrides, `--output-schema`, headless resume, and a
  thin TS SDK — anchored to OpenAI models (with `--oss` for local).
- **Choose mux** when the **backend** matters more than breadth of headless features: it is the only one
  of the three that drives arbitrary local/remote OpenAI-compatible models from the command line, and it
  pairs that with a **well-specified, versioned, secret-redacting JSONL contract**, **precise exit
  codes**, a **scriptable `probe` health check**, and a **deterministic `skill` CLI**. Its headless mode
  is narrower — no session resume, no headless MCP, no per-tool/sandbox permissions, no output schema,
  no SDK — and it is alpha. For single-shot, self-hosted, orchestrator-driven automation where you
  supply full context each run, mux is already competitive; for stateful multi-step headless pipelines
  that need MCP, sandboxing, or fine-grained tool control, the two commercial tools are ahead today.

---

## 17. Implementation plan: closing the reasonable gaps, end to end

> **Implementation status (v0.7.0).** Phases 1–3 (§17.3–§17.5) are implemented, tested, and documented.
> Phase 1/2: `--max-turns`, `--append-system-prompt`, `--max-token-budget` (with a `maxTokenBudget` setting
> and a `budget_exceeded` run status/error), `print --output-format json`, and headless session continuity
> (`--resume`/`--continue`/`--session-id`/`--fork-session`/`--no-session-persistence`) with `sessionId` on
> the run events and interactive `/sessions` interop. Phase 3: `--allow-tools`/`--deny-tools` (glob-matched,
> deny-wins, tools filtered from the advertised set and refused at a pre-approval gate), the `--add-dir`
> roots, and the application-level `--sandbox` posture (`read-only` refuses mutating tools;
> `workspace-write` confines built-in file writes to the working directory plus `--add-dir` roots),
> reported as `sandboxPosture` on `run_started`. Positive and negative coverage lives in
> `src/Test.Shared/Suites/HeadlessFeaturesSuite.cs` and `ToolGovernanceSuite.cs`. **Deferred within Phase
> 3:** persisting per-endpoint allow/deny lists through the `EndpointFormModal` UI (the CLI flags already
> reach the interactive shell at launch).
>
> **Phase 4 (§17.6) is implemented (CLI items).** Headless MCP for `print` (`--mcp-config`,
> `--strict-mcp-config`) reuses the interactive `McpRuntime` with a connect→discover→run→dispose lifecycle
> (opt-in, so a plain `print` stays hermetic); `--output-schema` folds a schema directive into the prompt
> and validates the response as a structural gate (JSON of the declared top-level type with required
> properties present) — backend-agnostic, not full JSON Schema and not provider-native structured output;
> and `--input-format jsonl` drives a multi-turn conversation from stdin turn records, threading history
> across turns (proven end-to-end in `InputFormatSuite.MultiTurnThreadsHistory`). Coverage in
> `Phase4FeaturesSuite.cs` and `InputFormatSuite.cs`.
>
> **The SDKs (§17.6) are implemented** as `sdk/typescript` (`@mux/sdk`) and `sdk/python` (`mux-sdk`):
> thin drivers that spawn `mux print --output-format jsonl`, parse typed events, aggregate a result, and
> offer multi-turn `Thread`s that persist through mux sessions. The TS SDK type-checks and builds with
> `tsc`; each ships a hermetic test harness (a fake mux — no network or model) and was verified end-to-end
> against the real binary.
>
> **Structured output (§17.6) is completed** for the backend-agnostic path: `--output-schema` now validates
> recursively (`type` incl. union arrays and `integer`, `enum`, `required`, nested `properties`, and array
> `items`) with JSON-path error reporting. Provider-native passthrough is **not possible** through mux's LLM
> abstraction — PolyPrompt 2.0.1's request model exposes no `response_format`/`json_schema` field — so mux
> constrains via the prompt and validates client-side; this is documented rather than left implied.
>
> **Still planned (refinements only):** value-level schema constraints (numeric bounds, string
> patterns/formats), and the `EndpointFormModal` per-endpoint allow/deny persistence UI. Live MCP
> round-trips are validated by the interactive path, not in CI here (no `dotnet-script`/live MCP server).
>
> One deviation from §17.2: `DOCKERHUB_README.md` is intentionally not added — mux ships no container
> image, so a Docker Hub page would advertise a distribution path that does not exist.

The gaps in §14 are worth closing, but a headless flag is never the whole job in mux. A new capability
has to land in the agent core, the CLI parser, the `mux --help` text, the interactive TUI where it has a
counterpart, the JSONL contract, `settings.json`/`CONFIG.md`, the README and USAGE guides, the
CHANGELOG, and the test projects — otherwise the product drifts out of sync with its own documentation,
which mux's own code style rules forbid ("If a README exists, analyze it and ensure it is accurate").
This section is written so an engineer can pick up any item and execute it without re-deriving the
design: it names the files, the types, the flags, the events, the docs, and the tests for each one.

Everything here is additive. mux already ships the hard parts — `SessionStore`, history replay in
`AgentLoop`, `McpRuntime`, `BuiltInToolRegistry`, `StructuredOutputFormatter` — wired only to the
interactive shell. The work is mostly connecting existing machinery to `mux print` and keeping the whole
product coherent around it, not rebuilding the agent.

### 17.1 Compliance with `c:\code\agents\requirements`

Two files in that folder govern how this work must be done, and every item below inherits them.

`CODE_STYLE.md` sets the C# conventions for all changes under `src/`. The ones that will actually bite
this work: `namespace` first with `using` statements **inside** the namespace block (system usings
alphabetically, then the rest); XML documentation on every public member, constructor, and method, and
none on private ones; private fields named `_PascalCase` with backing fields where validation is needed;
never `var`; **no tuples** — return a small named DTO class instead (this matters below, because several
new results would be tempting to model as tuples); `.ConfigureAwait(false)` on awaits and a
`CancellationToken` on every async method; guard clauses with specific exception types and `///
<exception>` tags; one class or enum per file; no `Console.WriteLine` anywhere in `Mux.Core` (library
code stays silent — emit `AgentEvent`s instead); regions only in files over 500 lines; and prefer a
configurable public member with a sensible default over a hard-coded constant. New behavior that a user
might want to tune belongs in `MuxSettings`, not a `const`.

`REPOSITORY_REQUIREMENTS.md` governs repository shape. It has three consequences here. First, the SDK
item (§17.6) must live under `sdk/{language}/` with its own thorough test harness and its own README —
not inside `src/`. Second, README.md and CHANGELOG.md must stay accurate as each item lands, and the
repository is currently **missing `DOCKERHUB_README.md`**, which the requirements list as mandatory;
closing that is folded into §17.2 as repo-hygiene the first landed item should carry. Third, source
stays within `src/`, `test/`, `dashboard/`, or `sdk/` — mux keeps its tests in `src/Test.Nunit`,
`src/Test.Xunit`, and `src/Test.Automated`, so new tests go there.

Documentation edits should follow `WRITING_DOCUMENTS.md` in spirit: the prose in README/USAGE should
read like the author wrote it, not like a feature list was auto-expanded. Reference tables and flag
listings are exempt (they are technical assets), but the surrounding explanation is not.

### 17.2 Definition of Done (applies to every work item)

An item is not finished when the flag parses. Each of the following must be true before it ships, and
individual items below add only their item-specific criteria on top of this list.

- **Core + style.** Behavior implemented in `Mux.Core`/`Mux.Cli` per `CODE_STYLE.md`; builds clean with
  no new warnings on both `net8.0` and `net10.0`.
- **CLI + help.** Flag parsed in `CliArgumentParser.cs` (and the matching `*Settings` class), and the
  `PrintHelp()` text in `src/Mux.Cli/Program.cs` updated so `mux --help` documents it.
- **TUI parity.** Where the capability has an interactive counterpart, it is reachable in the TUI through
  the one command catalog (`MuxCommandCatalog`) so key binding, `F1` menu, and slash command stay in
  sync; if it is headless-only, that is stated explicitly in the docs.
- **Config.** Any persistent default added to `MuxSettings` (with a backing field and XML docs) and
  documented in `CONFIG.md` under `settings.json`.
- **Contract.** New JSONL fields are additive and serialized in `StructuredOutputFormatter`; a breaking
  change bumps `StructuredOutputContractVersion` (currently `1`) and is noted in USAGE.md's "Contract
  Compatibility" section.
- **Docs.** README.md (the CLI Usage, Options table, and Automation Contract sections), the relevant
  USAGE.md section, and GETTING_STARTED.md when the change is user-entry-level — all updated in the same
  change.
- **CHANGELOG.** An entry under a new version heading in `CHANGELOG.md`, and `Defaults.ProductVersion`
  bumped to match (the two currently disagree — see §2).
- **Tests.** Parser/serialization/behavior tests in `src/Test.Nunit` or `src/Test.Xunit`; end-to-end
  headless runs in `src/Test.Automated` where a live loop is exercised.
- **Self-consistency.** This file's §1 TL;DR and §15 matrix updated so the comparison stops claiming a
  gap that no longer exists.

Repo hygiene to fold into the first item that lands: create `DOCKERHUB_README.md` mirroring README.md
per `REPOSITORY_REQUIREMENTS.md`, and fix the `Defaults.ProductVersion` lag.

### 17.3 Phase 1 — Low-risk quick wins

These four map onto options the agent loop already honors, so they are mostly plumbing and documentation.
Do them first; they retire four matrix rows for very little risk.

**`--max-turns <n>` (retires §14.8).** `AgentLoopOptions.MaxIterations` already caps the loop; there is
simply no CLI path to it. Add a nullable `MaxTurns` to `CommonSettings`, parse `--max-turns` in
`CliArgumentParser.ParseCommon` (reuse `ReadValue` + `int.Parse` with `CultureInfo.InvariantCulture` as
`--max-tokens` does), and in `CommandRuntimeResolver.ResolveRuntime` let it override
`MaxAgentIterations`. Surface it: it is already reported as `maxIterations` in `run_started`, so no
contract change. Docs: Options table in README, USAGE "Common Command Patterns". Tests: a parser test
plus an automated run asserting the loop stops at N and emits `run_completed` with
`status = "max_iterations_reached"`.

**`print --output-format json` (retires §14.6).** Today `print` accepts only `text`/`jsonl`
(`ParseOutputFormat(..., Text, Jsonl)` in `PrintCommand.cs`). Add `Json` to that call and, when
selected, suppress per-event output and emit exactly one object at the end built from the terminal
`RunCompletedEvent` plus the accumulated `finalAssistantResponse`: `result`, `status`, `sessionId`
(after §17.4), `iterationsCompleted`, `toolCallCount`, `errorCount`, `durationMs`,
`finalEstimatedTokens`, `taskSummary`, `contractVersion`. Model the summary as a real DTO class (not a
tuple — `CODE_STYLE.md`), serialized via the existing `StructuredOutputFormatter` camelCase options and
its secret redaction. Docs: README Automation Contract, USAGE "Output Formats". Tests: xUnit
serialization test; automated run piping to `jq '.result'`.

**`--append-system-prompt <text>` (part of §14.9).** The prompt is assembled as a string in
`CommandRuntimeResolver.ResolveRuntime`; append the flag's text after the `{ToolDescriptions}`/
`{TaskPlanningGuidance}` substitution so it survives profile switches. Add `AppendSystemPrompt` to
`CommonSettings`; parse the flag. TUI parity: the prompt-profile editor (`/prompts`, `PromptEditorModal`)
should accept the same append text so interactive and headless behave identically. Docs: README Options,
CONFIG.md `system-prompt.md`/`prompts.json`. Tests: resolver test asserting the appended text lands in
the effective prompt.

**`--max-token-budget <n>` (backend-agnostic analogue of the competitors' budget caps).** Token
accounting already exists (`ContextBudgetSnapshot`, `finalEstimatedTokens`). Add a `MaxTokenBudget`
setting to `MuxSettings` (nullable, default off) and a `--max-token-budget` override; in `AgentLoop`,
after each turn's estimate, stop cleanly once cumulative estimated tokens exceed the budget, emitting an
`ErrorEvent` with code `budget_exceeded` (add it to `ClassifyFailureCategory` as `runtime`) followed by
`run_completed`. This is the provider-neutral substitute for USD budgets (see §17.7 for why USD is out).
Docs: CONFIG.md `settings.json`, USAGE "Exit Codes" (maps to exit `1`). Tests: automated run with a tiny
budget asserting early, clean termination.

### 17.4 Phase 2 — Headless session continuity (retires §14.1)

The highest-value gap, and almost entirely wiring: `SessionStore` already persists snapshots and
`AgentLoop.RunAsync` already replays `AgentLoopOptions.ConversationHistory` at startup. What is missing is
the glue in `PrintCommand`.

Add five flags to `PrintSettings` and `CliArgumentParser.ParsePrint`: `--resume <id|name>`,
`--continue`, `--session-id <id>`, `--fork-session`, and `--no-session-persistence`. In `PrintCommand`,
construct a `SessionStore` (the same type `Program.RunInteractive` uses), and before the run: if resuming
or continuing, `LoadAsync` the snapshot and populate `loopOptions.ConversationHistory` from it;
`--continue` selects the most recent snapshot scoped to the active config dir and working directory.
After a successful run, persist the updated snapshot with `SaveAsync`, or with `DuplicateAsync` to a new
id for `--fork-session`; skip persistence entirely under `--no-session-persistence`. All new `SessionStore`
calls take a `CancellationToken` and use `.ConfigureAwait(false)` per style.

Contract: add `sessionId` to `RunStartedEvent` and `RunCompletedEvent` and serialize it in
`StructuredOutputFormatter` (additive, no version bump), so an orchestrator can capture the id from run
N's `run_completed` and pass it to run N+1's `--resume`. This mirrors exactly how Claude Code and Codex
thread sessions through CI.

TUI parity is a real requirement, not a nicety: sessions created by `mux print` must appear in and be
resumable from the interactive `/sessions` browser, and interactive sessions must be resumable by
`mux print --resume`, because both use the one `SessionStore`. Add an automated test that runs a print,
captures `sessionId`, resumes it, and asserts the second turn sees the first turn's context.

Docs: README (new "Automation Contract" subsection on session continuity), USAGE "Sessions" and
"Orchestrator Integration", GETTING_STARTED (a two-step resume example), CHANGELOG.

### 17.5 Phase 3 — Tool governance and a pragmatic confinement posture

This gives headless callers the middle ground between `deny` and `--yolo` that both competitors have,
without a kernel sandbox.

**`--allow-tools` / `--deny-tools` (retires the per-tool half of §14.4).** Accept comma-separated name
globs (e.g. `--allow-tools "read_file,grep,run_process"`, `--deny-tools "delete_file"`). Implement as a
filter over `BuiltInToolRegistry.GetToolDefinitions()` so denied tools are never advertised to the model,
plus a re-check in the approval path so a model that hallucinates a denied tool is refused with a
`tool_call_denied` result (exit `2`). Persist the same lists optionally on the endpoint config
(`EndpointConfig`) so an endpoint can carry a default allow/deny set, and expose them in the endpoint form
(`EndpointFormModal`) alongside the existing `autoApproveTools` field — that is the TUI parity obligation.
Docs: README Options + Approval/Safety, USAGE "Approval Policy", CONFIG.md `endpoints.json`. Tests:
resolver test that a denied tool is absent from the effective set; automated test that a denied call
yields exit `2`.

**`--add-dir <path>` (repeatable; the other half of §14.9).** The one moderate lift: file tools resolve
against a single working directory today, so multi-root means teaching the file tools in
`Mux.Core/Tools` to accept a set of allowed roots and validate paths against all of them. Add
`AdditionalDirectories` to `CommonSettings` and thread it into `AgentLoopOptions`. This flag is also the
enforcement surface for the posture below. Tests: file-tool unit tests for path resolution across roots
and rejection outside them.

**Application-level `--sandbox read-only|workspace-write` (pragmatic substitute for §14.4's sandbox).**
Not a kernel sandbox — a posture enforced inside the tool layer. `read-only` auto-denies mutating tools;
`workspace-write` confines file mutations to the working directory plus `--add-dir` roots while leaving
`run_process` under the normal approval gate. Model it as a `SandboxPostureEnum` (own file, per style)
carried on `AgentLoopOptions` and checked in the tool dispatch and approval path. Report the active
posture in `run_started`. TUI parity: show the posture in the sidebar/status and in the approval modal's
explanatory text so an interactive user sees the same boundary. Docs: a new USAGE "Sandbox Posture"
section that is explicit this is application-level, not OS-level (honesty per §17.7). Tests: automated
runs proving a write outside the workspace is refused under `workspace-write` and any write is refused
under `read-only`.

### 17.6 Phase 4 — Headless MCP, output schema, structured input, and the SDK

Higher-effort items that still fit the architecture. Sequence them after the safety and session work.

**Headless MCP (retires §14.3).** Reuse `McpRuntime` in `PrintCommand`. Add `--mcp-config <path|json>`
and `--strict-mcp-config`, stop rejecting MCP in non-interactive mode (remove the guard in
`CommandRuntimeResolver.ValidateCommandSettings` for these paths and pass `supportsMcp: true` when a
config is supplied), and adopt a bounded **connect → wait-for-discovery → run → dispose** lifecycle for
the single shot. Keep it strictly opt-in so a plain `mux print` stays fast and hermetic. The
`run_started.mcp` block already exists (currently `supported:false`); populate it truthfully. Docs: USAGE
"MCP Tool Servers" gains a headless subsection, CONFIG.md `mcp-servers.json`, README Automation Contract
(remove the "does not load MCP" constraint once true). Tests: automated run against a stub stdio MCP
server asserting a tool round-trips and the server is disposed on exit.

**`--output-schema <path>` (retires §14.5).** For OpenAI-compatible adapters that support structured
output, pass the schema through the adapter's `response_format`; for backends that do not, fall back to
injecting the schema into the prompt and validating the final message against it, emitting a structured
`error` (`schema_validation_failed`) on non-conformance. The backend-agnostic fallback is mandatory —
mux cannot assume provider features. Docs: USAGE "Output Formats". Tests: one adapter-native case and one
prompt-fallback case, both asserting conforming output and a clean failure on violation.

**`--input-format jsonl` (retires §14.2).** Multi-turn driving over stdin, where each record is a turn
against the persisted history from §17.4 — so this is cheap once Phase 2 lands and should not be
attempted before it. Lower priority than the rest. Docs: USAGE "Orchestrator Integration". Tests: an
automated multi-turn stdin script.

**Programmatic SDK (retires §14.7), under `sdk/` per `REPOSITORY_REQUIREMENTS.md`.** Two tracks. The
language-agnostic surface already exists — the JSONL contract — so the first deliverable is to stabilize
and document it as the supported programmatic interface (USAGE already has "Contract Compatibility";
promote it). The second is an optional thin driver SDK at `sdk/dotnet/` (and/or `sdk/typescript/`) that
spawns `mux print --output-format jsonl` and yields typed events, each with its own README.md and a
thorough test harness as the requirements mandate. Do not expose `Mux.Core` internals as the SDK; wrap
the CLI, the way Codex's TypeScript SDK wraps `codex exec`.

### 17.7 Deliberately out of scope

Two gaps are left open on purpose, and the honesty is the point.

A full OS/kernel sandbox — seccomp/Landlock on Linux, Seatbelt on macOS, job objects or AppContainer on
Windows, plus interception of every `run_process` launch — is a large, per-platform subsystem that would
disturb the core and duplicate what Codex already does well. The application-level posture in §17.5 is the
proportionate answer for mux; a real sandbox can return later as an isolated optional component rather
than a core change.

USD cost accounting is meaningless across mux's backends. A run might hit a free local Ollama model, a
metered OpenAI endpoint, and a self-hosted vLLM cluster in the same week, with no shared price. The
`--max-token-budget` in §17.3 is the right shape for a tool that does not own the pricing model, and it is
what mux should ship instead of `--max-budget-usd`.

### 17.8 Sequencing and gap-to-plan map

| Gap (§14) | Work item | Phase | Effort | Primary files |
|---|---|---|---|---|
| §14.8 no `--max-turns` | `--max-turns` → `MaxIterations` | 1 | XS | `CliArgumentParser`, `CommonSettings`, `CommandRuntimeResolver` |
| §14.6 no single-object `json` | `print --output-format json` summary DTO | 1 | XS | `PrintCommand`, `StructuredOutputFormatter` |
| §14.9 no `--append-system-prompt` | inline append flag | 1 | XS | `CommandRuntimeResolver`, `PromptEditorModal` |
| §14.10 no budget cap | `--max-token-budget` (tokens) | 1 | S | `MuxSettings`, `AgentLoop` |
| §14.1 no headless resume | `--resume`/`--continue`/`--session-id`/`--fork-session` | 2 | M | `PrintCommand`, `SessionStore`, `RunStarted/CompletedEvent` |
| §14.4 no per-tool control | `--allow-tools`/`--deny-tools` | 3 | M | `BuiltInToolRegistry`, approval path, `EndpointFormModal` |
| §14.9 no `--add-dir` | multi-root file access | 3 | M | `Mux.Core/Tools`, `AgentLoopOptions` |
| §14.4 no sandbox | app-level `--sandbox` posture | 3 | M | new `SandboxPostureEnum`, tool dispatch, TUI status |
| §14.3 no headless MCP | reuse `McpRuntime` in `print` | 4 | M–L | `PrintCommand`, `McpRuntime`, `CommandRuntimeResolver` |
| §14.5 no output schema | `--output-schema` + fallback | 4 | M | adapters, `AgentLoop`, validation |
| §14.2 no structured input | `--input-format jsonl` | 5 | M | `PrintCommand` (builds on Phase 2) |
| §14.7 no SDK | document JSONL contract; `sdk/{language}` driver | Doc | S–M | `USAGE.md`, `sdk/` |
| full OS sandbox | out of scope — posture instead | — | — | — |
| USD cost | out of scope — token budget instead | — | — | — |

Run the phases in order. Phases 1 and 2 alone move mux from "single-shot only" to "scriptable,
resumable, and bounded," which covers the majority of real orchestration needs; Phase 3 makes unattended
runs safe to grant real tools; Phase 4 reaches feature parity on everything except the two items in
§17.7. At each phase, the same edit that changes behavior updates the help text, the guides, the
CHANGELOG, and this comparison — so the product and its documentation never fall out of step, which is
the standard `CODE_STYLE.md` holds mux to in the first place.

---

*Prepared from direct source inspection of this repository (`src/Mux.Cli/**`, `src/Mux.Core/**`) and
from the official documentation/repositories of Claude Code and OpenAI Codex as of early 2026. Competitor
details evolve quickly; verify version-sensitive flags against each tool's `--help` for the exact build
in use.*
