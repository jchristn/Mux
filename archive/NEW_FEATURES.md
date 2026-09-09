# mux — Features & Roadmap

_A capability overview of mux plus the near-term roadmap. For the active implementation plan behind the
client/server work, see [`MUX_COMPARISON.md`](MUX_COMPARISON.md)._

mux is a CLI AI coding agent that gives you a Claude Code / Codex-style experience against **the backend and
model you choose** — local or remote, self-hosted or hosted. It reads and writes files, runs commands,
searches code, and drives a project through an interactive REPL or a single-shot, automation-grade command
surface. mux does not install or manage model runners; you bring your own inference backend.

---

## Current capabilities

### Backend & model support
- **Backend-agnostic core.** One CLI for local and remote runners.
- **Adapters:** `ollama` (native API), `openai`, `vllm`, and generic `openai-compatible` (LM Studio, and any
  OpenAI-compatible gateway).
- **Frontier & cloud providers (v0.8.x):** native `anthropic` (Claude Messages API), `gemini` (AI Studio),
  `azure-openai` (deployment + api-key/AAD), `vertex` (Vertex AI with ADC credentials), and `bedrock` (AWS
  Bedrock, SigV4) — all through PolyPrompt's provider-normalized clients.
- **Per-endpoint control:** reasoning effort (OpenAI/Gemini/Ollama/Anthropic), thinking display, iteration
  caps, quirks, live model enumeration (`mux endpoint models`), and provider token-usage reporting.

### Agent tools
- File `read`/`write`/`edit`/`multi_edit`/`delete`, `glob`, `grep`, `list_directory`, `manage_directory`,
  `file_metadata`.
- `run_process` with shell-aware runtime metadata (OS, platform family, shell) for correct command generation.
- `web_retrieve` (headless Playwright rendering) and provider-backed `web_search` (Tavily / You.com).
- Task planning tools (`plan_tasks` / `update_task`) with a live checklist.

### Skills
- Versioned, **executable** Markdown-plus-code capabilities under `~/.mux/skills`: commands run through an
  allowlisted interpreter (`bash`, `sh`, `pwsh`, `python`, `node`, `dotnet-script`) with a timeout and captured
  output — deterministic, model-free procedures usable from the agent **or** directly (`mux skill run`) in a
  git hook / CI job. A curated default set seeds on first run.

### Interactive shell (TUIKit)
- Full-screen shell: per-job transcripts, job sidebar with telemetry, multi-line composer, one command catalog
  across slash commands / key bindings / F1 menu, interactive tool-approval modal, themes, and autosaved,
  resumable sessions. Concurrent background jobs are serialized on file edits by a single-writer lease.

### Background tasks
- The model decomposes large work into a tracked task plan and advances it live (pending → running → done);
  `/tasks` inspects and hand-annotates the plan; it persists across save/resume and emits `task_plan_updated`
  events for orchestrators.

### Headless automation
- `mux print` with four output shapes (streamed/buffered text, streamed JSONL, buffered JSON), a versioned
  `contractVersion`, a stable event schema, defined exit codes, token usage, `--output-schema` validation,
  multi-turn stdin, and headless session resume. TypeScript and Python SDKs wrap the contract.

### Governance & isolation
- Approval policies, tool allow/deny globs, sandbox postures (`none` / `read-only` / `workspace-write`),
  `mux probe` health checks, and full config isolation via `MUX_CONFIG_DIR` / `--config-dir`.

### MCP
- `stdio` and HTTP MCP servers via `mcp-servers.json` and `/mcp`; headless MCP via `--mcp-config`.

---

## New in v0.9.0 — local REST server & tray agent

An **opt-in** local surface that lets other processes and UIs drive mux without shelling out per run:

- **`mux serve`** — a loopback-bound (`127.0.0.1`), token-guarded **REST API** (Watson 7) over mux's existing
  in-process services, with a **WebSocket** live event stream (reusing the JSONL event schema) and an
  **OpenAPI** spec (`UseOpenApi()`) for generated clients. See [`docs/REST_API.md`](docs/REST_API.md).
- **System-tray agent (`Mux.Agent`)** — a cross-platform (Windows/macOS/Linux) Avalonia tray icon that hosts
  the server in the background and offers **About**, **Launch Mux**, and **Exit**.
- **Configuration** — a `rest` block in `settings.json` (`enabled`, `hostname`, `port`, `ssl`, `apiKey`,
  `corsAllowOrigin`) with `MUX_REST_*` environment overrides.

Both are strictly opt-in: a plain `mux` or `mux print` invocation still listens on nothing.

---

## Roadmap (candidate tracks, by rough ROI)

| Track | Value | Simplicity | Notes |
|---|---:|---:|---|
| Interim LSP (Skills for build/lint diagnostics) | 6 | 9 | Cheap correctness signal using existing Skills |
| Session sharing (local export) | 6 | 8 | Render a session to HTML/Markdown; hosted tier rides on the REST server |
| Custom keybinds | 5 | 9 | Config over the existing id-keyed command catalog |
| Subagents | 9 | 5 | Named agent configs + a spawn tool over the existing `TaskOrchestrator` |
| Undo/redo | 8 | 5 | Per-turn workspace checkpoints |
| Plugin system (hooks + custom commands) | 7 | 6 | Out-of-process hooks + MCP/Skills; no in-process code plugins |
| Desktop / Web / IDE clients | 8 | 4 | Consume the v0.9.0 REST+WS+OpenAPI substrate |
| Full LSP (language-server subsystem) | 8 | 4 | Semantic navigation/diagnostics |
| OAuth subscription access | 7 | 4 | Loopback callback eased by the running server |

_Value / Simplicity are 1–10 (higher simplicity = easier); these are planning estimates, not commitments._
