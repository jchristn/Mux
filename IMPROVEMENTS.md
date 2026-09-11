# mux Improvements

This document outlines the work needed to move mux from a capable local-first coding agent into a polished, broadly extensible developer product. It focuses on four areas:

- out-of-box polish
- provider coverage
- IDE and desktop capabilities
- hackability and extension depth

## Current Position

mux already has strong bones: backend-agnostic endpoint configuration, interactive and headless execution, structured JSON/JSONL output, MCP support, skills, task tracking, subagents, undo/redo, local session export, a local REST server, and a web dashboard.

The main gap is not raw capability. The main gap is product finish: the first-run path, provider convenience, editor integration, desktop control surface, and extension/runtime story need to feel coherent and dependable without requiring the user to assemble too much by hand.

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
| Diff review | Undo/redo exists through git checkpoints. | Add first-class diff review before and after writes: per-file summary, hunk preview, accept/reject selected changes, and restore points. | Users trust agents more when edits are inspectable at the right granularity. |
| Background jobs | Concurrent jobs, queued prompts, task plans, and write leases exist. | Add richer job controls: pause/resume, cancel with cleanup, restart failed step, promote queued prompt, inspect live subprocess output, and job notifications. | Long tasks need operator controls, not just transcript output. |
| Usage and cost | Usage telemetry and pricing support are emerging. | Make usage visible everywhere: per-turn cost, budget caps, trend charts, per-provider pricing overrides, and warnings before expensive runs. | Cost confidence is part of product confidence. |
| Documentation | Broad docs exist, but some sections drift as features move quickly. | Create versioned docs, a feature matrix, copy-paste recipes, troubleshooting by failure class, and architecture diagrams. | Fast-moving alpha software needs docs that reduce surprise. |
| Release quality | Strong tests exist. | Add release smoke tests against representative real endpoints, signed artifacts, changelog discipline, migration notes, and compatibility promises per contract. | Users need to know what can change and what is stable enough to automate against. |

### Polish Milestones

| Milestone | Definition Of Done |
|---|---|
| Polished alpha | One-command install, guided first run, clearer error taxonomy, clean endpoint setup, current docs. |
| Trustworthy beta | Signed releases, upgrade path, stable JSONL contract, diff review, robust crash/session recovery, release smoke tests. |
| Daily-driver ready | Native installer/updater, full provider setup UX, dependable background-job controls, usage/cost visibility, stable extension contracts. |

## 2. Provider Coverage

mux is already good because it can talk to local runners, hosted APIs, cloud endpoints, and OpenAI-compatible services. To become very good or excellent, it needs fewer "bring your own URL and headers" paths and more first-class adapters with auth, catalog, capability, and usage support.

### Model Provider Gaps

| Gap | Needed Capability | Priority |
|---|---|---:|
| Native model-router providers | First-class setup for router-style providers: API key/OAuth, model catalog, routing parameters, cost metadata, context windows, tool/vision/reasoning flags. | P0 |
| Subscription-backed providers | Login flows that let users authenticate through existing individual or enterprise subscriptions where terms permit it. | P0 |
| Additional frontier API providers | Dedicated adapters for major model labs not currently represented as first-class endpoints, even when they can be reached through compatibility layers. | P1 |
| High-throughput inference providers | Native support for low-latency hosted inference vendors with model catalog refresh, rate-limit handling, and usage reporting. | P1 |
| Enterprise cloud AI platforms | First-class adapters for data-platform and enterprise-cloud model endpoints, including region/project/account scoping and identity-based auth. | P1 |
| Local runner depth | Beyond the current local runner support, add native llama.cpp server setup, GGUF model metadata, local model health, and local capability detection. | P1 |
| Custom provider SDK | A documented way to add providers without changing mux core: auth, catalog, request/response mapping, streaming, usage, reasoning, and tool-call translation. | P0 |
| Provider conformance tests | A reusable test harness that records mocked provider streams and validates tool calls, errors, usage, and edge cases. | P0 |

### Specific Provider Families To Consider

| Provider Family | Why It Helps |
|---|---|
| Model routers and gateways | Immediately broaden model choice, simplify experimentation, and centralize billing for users who switch models frequently. |
| Subscription account bridges | Reduce the need for separate API billing and make setup easier for users who already pay for model access. |
| Low-latency inference clouds | Improve interactive responsiveness and make smaller open models more practical. |
| Enterprise data/cloud platforms | Make mux easier to adopt in locked-down environments where model access is already governed by cloud identity. |
| Regional model providers | Improve coverage for teams outside the US/EU and for users who need region-specific compliance or language performance. |
| Local model runtimes | Strengthen mux's local-first identity and make offline/private workflows more credible. |

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
| Very good | mux can guide setup, list models, detect capabilities, report usage, and show actionable errors. |
| Excellent | mux supports native auth, model catalogs, pricing/cost, capability flags, provider quirks, conformance tests, and extension-based provider additions. |

## 3. IDE And Desktop Capabilities

The current local REST server, dashboard, and tray agent are the right foundation. The next step is to turn them into a complete local control plane and editor companion.

### Local API Foundation

| Capability | Needed Work |
|---|---|
| Run-driving API | Add routes to create sessions, append user messages, stream run events, cancel runs, resume sessions, and inspect task state. |
| Complete WebSocket bridge | Stream the same event contract used by headless JSONL: assistant text, tool calls, tool results, task updates, errors, and completion. |
| OpenAPI document | Publish a complete OpenAPI 3.1 document for the local server and generate typed SDKs from it. |
| Client auth model | Move local secrets into OS-protected storage where possible, preserve loopback safety, and provide clear remote-binding warnings. |
| Versioned contracts | Treat REST, WebSocket, and JSONL schemas as versioned APIs with compatibility notes. |

### Desktop App Surface

| Surface | Needed Capability |
|---|---|
| Session home | Browse, search, tag, fork, resume, export, and delete sessions from a native shell. |
| Multi-session work | Run multiple sessions in tabs or windows, with clear model, branch, workspace, and status indicators. |
| Job center | Show running, queued, completed, failed, and paused jobs with controls for cancel, retry, resume, and inspect logs. |
| Approval center | Approve or deny tool calls from the desktop app, including diff previews and command risk labels. |
| Configuration | Manage endpoints, model catalogs, MCP servers, skills, subagents, prompt profiles, hooks, keybindings, usage, and pricing. |
| Notifications | Native notifications when a job needs approval, completes, fails, or exceeds a budget threshold. |
| Update flow | Built-in update checks, release notes, rollback guidance, and compatibility warnings. |
| Diagnostics | One-click diagnostic bundle with redacted config, logs, environment, endpoint probes, and recent failure categories. |

### IDE Extension Surface

| Capability | Needed Work |
|---|---|
| Context injection | Send current file, selection, diagnostics, open tabs, project tree, terminal output, and git diff into a mux run. |
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
| Excellent | The editor, desktop app, TUI, and headless modes all control the same local run engine with shared sessions, approvals, events, and stable APIs. |

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
| Custom commands | Expand custom slash commands to support arguments, forms, confirmations, streamed output, and typed results. |
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
| 3 | Ship provider SDK and several high-value native providers. | Provider coverage feels broad without config gymnastics. |
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

