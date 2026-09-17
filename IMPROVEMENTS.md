# mux Improvements

This document outlines the work needed to move mux from a capable local-first coding agent into a polished, broadly extensible developer product. It focuses on four areas:

- out-of-box polish
- provider coverage
- IDE and desktop capabilities
- hackability and extension depth

## Current Position

mux already has strong bones: backend-agnostic endpoint configuration, interactive and headless execution, structured JSON/JSONL output, MCP support, skills, task tracking, subagents, undo/redo, local session export, a local REST server, and a web dashboard. It now runs on **five surfaces** — terminal UI, desktop app, web dashboard, VS Code extension, and headless CLI — and they are no longer just five clients that happen to share a session file. As of **0.12.2** they share a live run fabric: sessions are portable across every surface (resume/rename/duplicate/export/delete everywhere, a working directory recorded per session and changeable mid-run with `/cwd`, web chats persisted server-side), a run started on any surface can be watched live and read-only on any other, and a turn made on one surface appears in the same conversation open elsewhere without a manual refresh. The `mux serve` REST server publishes a complete OpenAPI 3.0.3 document with Swagger UI, tracks each run as an addressable object with cancel/inspect routes, and exposes a WebSocket bridge whose event envelope is byte-identical to the headless JSONL contract.

The main gap is not raw capability, and it is increasingly not the control plane either — that has largely converged. The remaining gap is product finish at the edges: the first-run path, provider onboarding convenience, the desktop app's job/approval depth, the extension/package runtime story, and legible context/prompt handling (the large-file truncation and universal-prompt work now scoped for 0.13.0 in `CONTEXT_AND_SUMMARIZATION.md`).

### Recent Momentum (last three days, 0.11.0 → 0.12.2)

The control-plane vision in sections 2–3 moved from "planned" to "shipped" in a concentrated burst:

- **Run lifecycle became a first-class object (0.12.0).** `GET /v1.0/api/runs`, `GET /v1.0/api/runs/{runId}`, and `POST /v1.0/api/runs/{runId}/cancel` ship; Stop now cancels server-side (dashboard + VS Code) rather than only aborting a local stream.
- **The WebSocket bridge is real (0.12.0).** `/v1.0/ws` authenticates the upgrade, subscribes by run **or** session id, replays-then-tails the canonical envelope (one shared `AgentEventSerializer`, byte-identical to JSONL), and carries over-socket approvals.
- **Live session mirroring across all surfaces (0.12.0).** `RunRegistry`/`RunHandle`/`RunPublisher`/`SessionMirrorClient` moved into `Mux.Core`; every surface both publishes its in-process runs to the hub and consumes others' by default. Mirroring is on by default via session-scoped subscribe.
- **Cross-surface sync made reliable (0.12.2).** A `SessionStoreWatcher` makes the on-disk store the trigger rather than any single hub — the server rebroadcasts external writes, and the TUI/desktop watch the store directly. Session persist/open consolidated into a shared `Mux.Core.Sessions.SessionService` with an anti-truncation `SessionMergePolicy`, so a turn is never silently overwritten by a surface that hadn't seen it.
- **OpenAPI 3.0.3 + Swagger UI shipped** for `mux serve` — discovery works before authenticating.
- **VS Code matured into a full fifth surface (0.11.x).** Streaming chat with in-editor approvals and Markdown, per-turn stats, inline code actions, editor+LSP context injection, full Manage CRUD (endpoints/MCP-with-auth/prompts/subagents/skills/settings), native usage charts, slash commands, and localization into twelve languages.

## 1. Out-Of-Box Polish

| Area | Current Feel | Needed Improvement | Why It Matters |
|---|---|---|---|
| Installation | Source-first and developer-friendly, but not yet frictionless. | Publish signed binaries and packages for major platforms, with checksum verification, upgrade commands, and uninstall paths. | A serious agent should be installable in one command by someone who has not cloned the repo. |
| First run | Seeds useful defaults, but assumes the user understands model runners and endpoints. | Add a guided first-run wizard: choose local or hosted backend, test credentials, pick a model, verify tool calling, and save defaults. | The first successful prompt should be boringly easy. |
| Provider setup | Flexible config and endpoint forms exist. | Add provider-specific setup flows with clear auth hints, model discovery, capability warnings, and pricing/usage metadata where available. | "It can connect" is good; "it guides me to a working configuration" is better. |
| Error messages | Functional, but some failures still require technical interpretation. | Normalize errors into actionable categories: auth, network, unsupported model, tool-calling unavailable, context limit, rate limit, TLS interception, missing runtime. | Great agents fail in ways that tell the user exactly what to do next. |
| Model capability detection | Probing and model listing exist for several backends. | Detect and surface capabilities per model: tool calling, streaming, reasoning controls, thinking output, vision, JSON/schema output, context size, cache support. | Prevents confusing runs where the selected model cannot actually do the requested job. |
| Defaults | Sensible, local-first defaults. | Ship curated endpoint presets, prompt profiles, subagent profiles, and skills tuned for common stacks, with a clear "reset to defaults" path. | Defaults define perceived quality before customization begins. |
| TUI fit and finish | Feature-rich, but still alpha-shaped in places. | Tighten layout, copy, empty states, keyboard discoverability, modal consistency, visual hierarchy, terminal compatibility, and accessibility. | The interface should feel calm under long-running, high-context work. |
| Diff review | Undo/redo and per-turn restore points exist through git checkpoints (TUI + desktop). | Add first-class diff review before and after writes: per-file summary, hunk preview, and accept/reject selected changes. | Users trust agents more when edits are inspectable at the right granularity. |
| Background jobs | Concurrent jobs, queued prompts, task plans, and write leases exist. | Add richer job controls: pause/resume, cancel with cleanup, restart failed step, promote queued prompt, inspect live subprocess output, and job notifications. | Long tasks need operator controls, not just transcript output. |
| Usage and cost | Per-turn cost, trend charts, and per-provider pricing overrides shipped across the dashboard and desktop. | Add budget caps and warnings before expensive runs. | Cost confidence is part of product confidence. |
| Documentation | Broad docs exist, but some sections drift as features move quickly. | Create versioned docs, a feature matrix, copy-paste recipes, troubleshooting by failure class, and architecture diagrams. | Fast-moving alpha software needs docs that reduce surprise. |
| Release quality | Strong tests exist. | Add release smoke tests against representative real endpoints, signed artifacts, changelog discipline, migration notes, and compatibility promises per contract. | Users need to know what can change and what is stable enough to automate against. |

### Polish Milestones

| Milestone | Definition Of Done |
|---|---|
| Polished alpha | One-command install, guided first run, clearer error taxonomy, clean endpoint setup, current docs. |
| Trustworthy beta | Signed releases, upgrade path, stable JSONL contract, diff review, robust crash/session recovery, release smoke tests. |
| Daily-driver ready | Native installer/updater, full provider setup UX, dependable background-job controls, usage/cost visibility, stable extension contracts. |

## 2. Provider Coverage

mux's provider strategy is deliberately compat-first, and that design largely closes this area rather than leaving it open. mux speaks the de facto OpenAI wire format — covering OpenAI, Azure, vLLM, LM Studio, and the entire OpenAI-compatible long tail (Groq, Together, Fireworks, DeepSeek, Mistral, OpenRouter, and gateways) — plus native adapters for the wire formats that are *not* OpenAI-shaped (Anthropic, Gemini, Vertex, Bedrock), used where a native path preserves features a compatibility shim drops (prompt caching, thinking budgets, tool-result formatting). Routing, load balancing, failover, QoS, and cost governance are intentionally **out of scope for the agent**: they belong to an external router/virtualization layer that does that job *behind a standard endpoint* — for example [Conductor](https://github.com/jchristn/Conductor) (which virtualizes model runners and re-exposes them as OpenAI/vLLM/Gemini/Ollama APIs), LiteLLM, or OpenRouter. mux works with these today with no special code because they present as ordinary OpenAI endpoints. So the residual here is not a bigger adapter surface or a provider SDK; it is onboarding convenience and a few optional native-auth flows.

### Model Provider Gaps

| Gap | Needed Capability | Priority |
|---|---|---:|
| Onboarding presets + adapter-aware forms | Ship a preset table (base URL, suggested models, quirks, and an auth hint per known provider) and endpoint forms that show only the fields a given adapter needs. This is pure config + form work in the TUI modal and web form — no engine change — and it is the single genuinely useful item in this section. | P1 |
| Model capability / quirks detection | Keep surfacing per-model capability flags (tools, vision, JSON schema, reasoning, cache) via `BackendQuirks` so a run never selects a model that cannot do the requested job. | P1 |
| Provider conformance tests | A harness that records mocked streams and validates tool calls, errors, and usage for the **native** adapters (Anthropic/Gemini/Vertex/Bedrock); the compat transport is already exercised by the `LlmBridge` suite. | P1 |
| Native non-Bearer auth (optional) | For users who do **not** front a gateway, implement real auth runtimes for the cloud adapters that already have enum slots: Vertex (service-account/ADC token minting + refresh), Bedrock (SigV4 request signing), and OAuth/subscription login where terms permit. This is adapter + secure-credential-storage work, not field layout — and it is avoidable by pointing mux at a gateway that holds the cloud credential and re-exposes OpenAI. | P2 |
| Frontier-format fidelity | Keep the native Anthropic/Gemini adapters ahead of their OpenAI-compat shims where the native format adds value (cache control, thinking config). | P2 |
| Local runner depth | Native llama.cpp server setup, GGUF model metadata, and local model health/capability detection. | P2 |

### How mux Reaches Each Provider Class

| Provider Class | How mux Reaches It Today |
|---|---|
| OpenAI-compatible APIs (OpenAI, Azure, vLLM, LM Studio, Groq, Together, Fireworks, DeepSeek, Mistral, …) | Directly, via the `openai` / `openai_compatible` adapters — no per-vendor code. |
| Non-OpenAI wire formats (Anthropic, Gemini) | Native adapters that preserve format-specific features their compat shims drop. |
| Enterprise cloud (Vertex, Bedrock) | Native adapters (native IAM auth is the only residual) — or fronted by a gateway that re-exposes OpenAI. |
| Routers / gateways / virtualization (Conductor, LiteLLM, OpenRouter) | As an ordinary OpenAI endpoint; the gateway owns routing, load balancing, failover, QoS, and cost. mux stays a thin client on purpose. |
| Subscription accounts | Only where the provider offers an API key, or an OAuth flow whose terms permit programmatic use. |

### Search, Retrieval, And Context Providers

| Gap | Needed Capability | Priority |
|---|---|---:|
| More web search providers | Add several more search backends with ranking metadata, freshness controls, and fallback behavior. | P1 |
| Reader/extraction providers | Add optional services for robust page extraction, crawling, PDF extraction, and documentation retrieval. | P1 |
| Embedding providers | Add first-class embedding endpoint configuration for local and hosted embeddings. | P1 |
| Vector stores | Add optional project memory over local and remote vector stores, with clear privacy boundaries. | P2 |
| Source hosting integrations | Add authenticated repository, issue, pull request, and release-note context providers. | P1 |
| Documentation indexes | Support project-local and remote doc indexes with refresh controls, provenance, and citation-friendly snippets. | P2 |

### Provider Excellence Bar

| Level | Requirement |
|---|---|
| Good | A user can configure a provider manually and complete a tool-using run. |
| Very good | Adapter-aware forms and presets fill a working config, models list, capabilities/quirks are detected, usage/cost is reported, and errors are actionable. |
| Excellent | Compat-first transport + native adapters for the non-OpenAI formats, capability/quirks detection, pricing/cost, and conformance tests for the native adapters — with routing, load balancing, and cost governance delegated to an external gateway rather than reimplemented in the agent. |

## 3. IDE And Desktop Capabilities

The current local REST server, dashboard, and tray agent are the right foundation. The next step is to turn them into a complete local control plane and editor companion.

### Local API Foundation

| Capability | Needed Work |
|---|---|
| Run-driving API | Session CRUD (`/v1.0/api/sessions` create/list/detail/upsert/delete), streamed run events (`/v1.0/api/chat/stream` SSE: `run`, text, thinking, tool calls, completion), server-side persistence, resume-by-id, and browser-approved mutating tools (`--allow-tools`) all ship. **Cancel-run (`POST /v1.0/api/runs/{runId}/cancel`) and run-state inspection (`GET /v1.0/api/runs`, `GET /v1.0/api/runs/{runId}`) now ship** (delivered v0.12.0). Remaining: run-driving `POST /sessions/{id}/messages` (append-to-session) rather than the chat-stream body. |
| ~~Complete WebSocket bridge~~ (delivered v0.12.0) | `/v1.0/ws` now authenticates the upgrade, subscribes by run/session id, and replays-then-tails the canonical event envelope — byte-identical to headless JSONL (one shared serializer) — including assistant text, thinking, tool calls/results, task updates, errors, and completion, plus over-socket approvals. |
| ~~OpenAPI document~~ (delivered) | A complete, example-rich **OpenAPI 3.0.3** document (`GET /openapi.json`) and interactive **Swagger UI** (`GET /swagger`) now ship for `mux serve` — every route carries summary/description/tag/parameters/body/responses, component schemas have examples, and the bearer scheme is declared; both doc routes are reachable pre-auth for discovery. Remaining: generate typed SDKs from it and move 3.0.3 → 3.1. |
| Client auth model | Move local secrets into OS-protected storage where possible, preserve loopback safety, and provide clear remote-binding warnings. |
| Versioned contracts | Treat REST, WebSocket, and JSONL schemas as versioned APIs with compatibility notes. |

### Desktop App Surface

| Surface | Needed Capability |
|---|---|
| Session home | Search and tag sessions from a native shell (browse, resume, rename, fork/duplicate, export, and delete already ship on the desktop, TUI, and web over one shared session store). |
| Job center | Show running, queued, completed, failed, and paused jobs with controls for cancel, retry, resume, and inspect logs. |
| Approval center | Add diff previews and command risk labels to the desktop approve/deny flow (basic approve/deny/always already ships). |
| Notifications | Native notifications when a job needs approval, completes, fails, or exceeds a budget threshold. |
| Update flow | Built-in update checks, release notes, rollback guidance, and compatibility warnings. |
| Diagnostics | One-click diagnostic bundle with redacted config, logs, environment, endpoint probes, and recent failure categories. |

### IDE Extension Surface

The VS Code extension in `src/Mux.VSCode` (planned in `archive/VSCODE_EXTENSION.md`) has matured into a full
fifth surface. It is a thin client over the local `mux serve` API — a streaming chat panel with in-editor
approvals, Markdown rendering, per-turn stats, inline commands and code actions, editor + LSP context
injection, a Manage view with full CRUD (endpoints, MCP with auth, prompts, subagents, skills, settings),
native usage charts, slash commands, twelve-language localization, and the shared session tree — with the
agent loop staying in `Mux.Core`. **Live session mirroring over the WebSocket bridge now ships** (0.12.0):
the extension unified onto the shared hub and key, publishes and consumes runs, and keeps an open
conversation live-synced through the store watcher (0.12.2). The rows below track what remains — deeper
LSP-as-tools awareness and a propose-before-write native diff mode — each dependent on server work called
out in the plan (the file-context route for large files is scoped in `CONTEXT_AND_SUMMARIZATION.md`).

| Capability | Needed Work |
|---|---|
| Context injection | Current file, selection, diagnostics, open tabs, git diff, and LSP symbols already inject into a mux run. Remaining: project tree and terminal output; and replacing the 8000-char active-file hard-slice with the map/summarize file-context route (scoped for 0.13.0). |
| Inline commands | Explain selection, fix diagnostic, generate tests, refactor selected code, write commit message, summarize diff, and review current file. |
| Diff workflow | Show proposed edits as native editor diffs; accept/reject file or hunk; restore checkpoint; open changed files after a run. |
| LSP awareness | Use language-server symbols, definitions, references, hover text, diagnostics, and call hierarchy as context and tools. |
| Run control | Start, steer, pause, cancel, resume, and approve runs without leaving the editor. |
| Session binding | Attach a mux session to a workspace, branch, task, or editor window; reopen it later with full state. |
| Terminal handoff | Let users jump between IDE panel, terminal TUI, and desktop session without losing context. |
| Local server discovery | Automatically find or start the local mux server, negotiate API version, and handle auth securely. |
| Extension settings | Configure models, approval posture, context sources, and shortcuts from the IDE settings UI. |

### IDE/Desktop Excellence Bar

| Level | Requirement |
|---|---|
| Good | A dashboard can chat, edit settings, and inspect sessions. |
| Very good | A desktop app can manage sessions/jobs/config, and an editor extension can pass file/selection/diff context. |
| Excellent — **substantially reached (0.12.2)** | The editor, desktop app, TUI, and headless modes now control the same local run engine with shared sessions, live-mirrored events over one canonical envelope, over-socket approvals, and an OpenAPI-documented REST surface. What keeps it from "fully done" is contract *stability* (REST/WS/JSONL are complete but not yet versioned-and-frozen) and the desktop job/approval **depth** (a full job center and diff-preview approvals are still shallow). |

## 4. Best-In-Class Hackability

mux already has skills, hooks, custom slash commands, MCP, subagents, prompt profiles, and structured automation. To become best-in-class for hackability, it needs a stable extension runtime and a package ecosystem around those primitives.

### Extension Runtime

| Feature | Needed Capability |
|---|---|
| Stable extension API | Define versioned interfaces for lifecycle hooks, tool interception, message transforms, context injection, model routing, UI rendering, and shutdown. |
| In-process and out-of-process options | Keep safe process-isolated hooks, but add richer extension modes for low-latency UI and tool integrations. |
| Hot reload | Reload extensions, skills, prompts, themes, and provider definitions without restarting. |
| Typed SDK | Provide strongly typed libraries for extension authors, including schemas, event types, tool helpers, and UI helpers. |
| Extension testing | Ship a harness for unit tests, golden event streams, fake model responses, fake tools, and permission assertions. |
| Debugging | Add extension logs, tracing, timing, error boundaries, and an interactive inspector. |

### Custom Tools And Workflows

| Feature | Needed Capability |
|---|---|
| Tool SDK | Let extensions register tools with JSON Schema, permission metadata, mutability classification, progress events, cancellation, cleanup, and custom renderers. |
| Tool governance | Give custom tools first-class allow/deny rules, risk labels, dry-run metadata, and approval prompts. |
| Workflow engine | Let users define reusable workflows that compose prompts, tools, subagents, checkpoints, approvals, and validation steps. |
| Durable background tasks | Persist long-running workflows across restarts, with resumable state and visible audit trails. |
| Subagent graphs | Support supervised delegation patterns, fan-out/fan-in, role-specific tools, budgets, and isolated workspaces. |
| Worktree isolation | Add first-class support for running risky or parallel changes in separate worktrees and merging reviewed results. |

### Context Engineering

| Feature | Needed Capability |
|---|---|
| Context assembly hooks | Let extensions add, remove, rank, or summarize context before each model call. |
| Custom compaction | Allow project-specific summarizers, code-aware summaries, memory-preserving summaries, and separate compaction models. |
| Retrieval plugins | Support project memory, documentation indexes, embeddings, vector stores, and source-hosting context through a common interface. |
| Prompt-layer inspection | Show exactly what high-level context sources were included, their token cost, and why they were selected. |
| Memory controls | Provide explicit, inspectable, editable long-term memory with project/user scopes and privacy controls. |

### UI Hackability

| Feature | Needed Capability |
|---|---|
| Custom commands | Slash commands now accept arguments (built-in commands route the text after the token to an argument handler, e.g. `/cwd <path>`). Remaining: extend arguments to user-authored custom commands, plus forms, confirmations, streamed output, and typed results. |
| Custom panels | Let extensions add TUI and desktop panels for dashboards, task boards, logs, approvals, or domain-specific tools. |
| Custom renderers | Let extensions control how tool calls, tool results, artifacts, diffs, and custom messages render. |
| Autocomplete providers | Let extensions contribute file references, issue IDs, symbols, commands, templates, skills, and arbitrary project entities. |
| Theme system | Make colors, icons, spacing, and semantic states extensible without forcing users to fork UI code. |

### Package System

| Feature | Needed Capability |
|---|---|
| Package manifest | Define one package format for extensions, skills, prompts, themes, provider adapters, tools, and workflow templates. |
| Install/update/remove | Add `mux package install`, `list`, `update`, `pin`, `disable`, `trust`, and `remove`. |
| Global and project scopes | Support user-wide packages, project-local packages, and locked project manifests for team workflows. |
| Trust and signing | Add package trust prompts, checksums, optional signing, declared permissions, and audit logs. |
| Dependency management | Resolve package dependencies predictably and isolate package runtime dependencies from mux core. |
| Discovery metadata | Track title, description, version, compatibility, permissions, commands, tools, providers, and screenshots/docs. |

### Self-Extension

| Feature | Needed Capability |
|---|---|
| Scaffolding | Add commands to generate extensions, tools, provider adapters, renderers, workflows, and package manifests. |
| Guided authoring | Let mux inspect its own APIs and help author extensions with tests and validation. |
| Validation | Provide `mux extension validate`, `mux package validate`, and CI-friendly checks for compatibility and security metadata. |
| Examples | Maintain a gallery of small, readable examples: permission gate, path guard, model router, custom tool, custom panel, provider adapter, compactor, workflow. |

### Hackability Excellence Bar

| Level | Requirement |
|---|---|
| Good | Users can write skills, hooks, custom commands, and MCP integrations. |
| Very good | Users can register typed tools, intercept events, customize context, and distribute packages. |
| Best-in-class | Users can reshape the agent's providers, tools, UI, context pipeline, workflows, subagents, and safety model through stable, documented, testable extensions. |

## Recommended Sequence

| Phase | Focus | Outcome |
|---|---|---|
| 1 | Polish the first-run path and provider setup. | New users reach a working, tool-capable run quickly. |
| 2 | Finish the local API and desktop/job control plane. | TUI, dashboard, desktop, and automation all share one reliable engine. |
| 3 | Provider onboarding presets + adapter-aware forms (and, only for gateway-averse users, native cloud-IAM auth). | Provider setup is a two-click preset, not URL/header/quirks gymnastics; routing stays in an external gateway. |
| 4 | Build the IDE extension around the local API. | mux becomes present where users already read and edit code. |
| 5 | Stabilize the extension and package system. | Advanced users can bend mux into custom workflows without forking it. |

## North Star

mux should feel like a local agent operating system for software work:

- easy enough to install and run in minutes
- transparent enough to trust with real repositories
- provider-flexible enough to survive model churn
- scriptable enough for automation
- integrated enough for daily coding
- hackable enough that power users can build their own workflows on top of it

