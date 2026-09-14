# mux for VS Code

## What this document is

This plan describes a Visual Studio Code extension that puts mux inside the editor without turning the editor into a second copy of mux. The extension is a client. It discovers or starts the local `mux serve` process, drives runs over the REST and Server-Sent Events surface that already exists, and renders the result as native editor UI — a chat panel, inline code actions, native diffs, and a session tree. The agent loop, the tools, the approval policy, the session store, and the usage database stay in `Mux.Core` behind `mux serve`, exactly as they do for the TUI, the desktop app, and the web dashboard.

That boundary is the whole design, and it is a deliberate stance rather than a shortcut. mux already treats routing as something an external gateway owns, and providers as something the OpenAI wire format standardizes; the editor is the same kind of decision. VS Code becomes a fifth surface onto one engine, sharing the same `~/.mux/sessions` store the other three surfaces read and write, so a conversation started at the terminal is resumable in the editor and vice versa. An extension that reimplemented the loop in TypeScript would fork the product, drift from the CLI's contract, and lose that portability the day it shipped.

The plan is written to be executed and annotated. Each phase carries a checkable task list and a **Definition of done** a developer marks off as work lands. It also states, per governing standard in `c:\code\agents\requirements`, what the extension must satisfy and what it deliberately does not — silent omission of an expected surface is itself a compliance failure, so the not-applicable calls are explicit and reasoned.

## How to track progress

Check a box when the task is complete and merged, not when it is written. A box that is checked means the behavior ships and a test covers it. Annotate a line with `— <initials> <yyyy-mm-dd>` when you close it, and add a nested note if the implementation diverged from the plan so the next reader sees the real state, not the intended one. A phase is done only when every box under it and its Definition of done are true together.

Status legend: `- [ ]` open · `- [x]` complete · `- [~]` in progress (annotate what remains) · `- [-]` dropped (annotate why).

## Goals and non-goals

The extension earns its place if a developer can stay in the editor for the work they would otherwise drop to a terminal for: ask about the file they are reading, fix the diagnostic under the cursor, review what the agent changed as a real diff, and pick a conversation back up tomorrow. Everything below serves that, and nothing below rebuilds what the CLI already does well.

In scope:

- A chat panel that streams a mux run — assistant text, thinking, tool calls, and completion stats — and answers tool-approval prompts in the editor.
- Editor context fed into a run on request: the active file, the selection, diagnostics, open tabs, the working-tree diff, and terminal output.
- Inline commands surfaced as editor commands and code actions: explain, fix diagnostic, generate tests, refactor selection, write a commit message, summarize a diff, review the current file.
- Native diff review of what a turn changed, with accept, reject, and restore built on the checkpoint history mux already records.
- A session tree that lists, resumes, renames, duplicates, exports, and deletes the same sessions the TUI and desktop show, because it is the same store.
- Local-server lifecycle: find a running `mux serve`, or start one bound to loopback with a generated key, negotiate the API version, and keep the secret in VS Code secret storage.

Out of scope, and why:

- No in-extension model inference or tool execution. Tools run in `Mux.Core` so a file the agent writes lands through the same sandbox, write-lease, and approval path as every other surface.
- No bundled router, provider adapter, or credential broker. A gateway such as Conductor already sits behind the OpenAI endpoint; the extension points mux at whatever endpoint the user configured.
- No telemetry backend. VS Code's own telemetry consent governs anything the extension might report (see the compliance ledger).

## Architecture

### The transport, and the one change mux needs first

The extension speaks to `mux serve` over its versioned `/v1.0/api` surface. Runs stream over `POST /v1.0/api/chat/stream`, which already emits `token`, `thinking`, `tool`, `done`, and `error` events and, when the server is started with `--allow-tools`, an `approval` event answered by `POST /v1.0/api/chat/approve`. Session management uses `GET/PUT/DELETE /v1.0/api/sessions` plus `sessions/detail` and `sessions/export`. Configuration reads and writes `/v1.0/api/endpoints`, `/v1.0/api/settings`, and the `/v1.0/api/usage/*` routes drive an optional cost view. The extension launches the server against the user's normal config directory, so its sessions are the same files the other surfaces list.

One gap blocks this cleanly, and it is the first work item. The streaming chat route runs its tools in the server process's current directory, not in the workspace the editor has open. An edit the agent makes would land next to `mux serve`, not in the user's repo. mux already records a working directory per session and lets the TUI and desktop change it with `/cwd`, so the fix is small and in keeping with what shipped: `ChatRequest` gains an optional `workingDirectory`, and `StreamChatAsync` resolves the run's directory as the request value, then the session's persisted `WorkingDirectory`, then the server default. The extension sends the workspace root, and every tool the run executes resolves paths against the repo the developer is looking at.

The JSONL path over `@mux/sdk` — spawning `mux print --output-format jsonl` — remains the documented fallback for a machine with the CLI but no running server, and for CI. It runs in the workspace directory natively but cannot prompt for approval mid-run, so it is the read-mostly option, not the default.

### Where the code lives

The extension is a self-contained subtree at `src/Mux.VSCode`, matching the `src/Mux.Desktop` convention and keeping every asset in this repository rather than a satellite the release process would have to reach into. Inside it, the extension host code sits under `src/`, its tests under `test/`, and the webview UI under `dashboard/`, which is the source layout `REPOSITORY_REQUIREMENTS.md` asks for. It depends on the existing `sdk/typescript` package for the JSONL fallback and shares its event typings where they overlap.

The webview is deliberately not a React application. mux's own dashboard is dependency-free vanilla rendering, and an editor panel benefits from the same restraint: no build-time framework, VS Code theme tokens instead of a private palette, and a single hand-rolled `ApiClient` that every host-to-server call goes through. `FRONTEND_ARCHITECTURE.md` mandates React 19 and Vite only for a React webview; this extension declines that stack on purpose, so those specifics are marked not-applicable in the ledger while the component-level rules it does keep — one API client, a typed `ApiError`, loading and empty and error states on every view, and accessible controls — still bind.

### One turn, end to end

A developer types in the panel, or fires an inline command. The extension assembles a prompt from the request plus whatever editor context the user attached, resolves the session bound to the workspace (creating one with the workspace root as its working directory if none exists), and opens the SSE stream. Tokens land in the panel as they arrive. A `tool` event draws a tool card that moves from running to succeeded or failed with its elapsed time. An `approval` event raises a native modal — approve once, approve for the session, or deny — and posts the decision back. When `done` arrives the panel records the per-turn stats, and if the turn wrote files, the diff view refreshes from the checkpoint mux took before the turn. The session is already persisted server-side, so closing the editor loses nothing.

## Phase 0 — mux-side prerequisites

The extension cannot run edits in the right place until the server honors a per-run working directory, and it cannot promise version safety without a contract check. Both are small server changes in this repository, covered by the existing `MuxServerRoutes` suite.

- [ ] Add optional `workingDirectory` to `ChatRequest` (both `/v1.0/api/chat` and `/v1.0/api/chat/stream`).
- [ ] Resolve the run directory in `ChatRoutes.StreamChatAsync` as request → persisted session `WorkingDirectory` → server default, and use it for `AgentLoopOptions.WorkingDirectory` and the `{WorkingDirectory}` prompt substitution.
- [ ] Reject a `workingDirectory` that does not exist with a `400` and an actionable message, rather than silently falling back.
- [ ] Add an API contract version to `GET /v1.0/api/health` (a `contractVersion` field) so a client can refuse an incompatible server instead of failing mid-run.
- [ ] Extend `MuxServerRouteSuite` with a working-directory round-trip (a run tagged to a temp dir writes there) and a health `contractVersion` assertion.
- [ ] Update `docs/REST_API.md` to document `workingDirectory`, the streaming/approve routes, and `contractVersion`.

**Definition of done:** a streamed run executes its tools in a caller-supplied directory, a missing directory is refused with a clear error, health reports a contract version, and the server suite proves all three on net8.0 and net10.0.

## Phase 1 — Extension skeleton and server lifecycle

Nothing renders until the extension can reliably reach a server it trusts. This phase is the activation path, the discovery-or-launch logic, and the single client every later phase builds on.

Discovery follows the pattern the desktop's `AgentLauncher` already uses in reverse: look for a health-responding server on the configured port, and if none answers, start `mux serve --allow-tools` bound to `127.0.0.1` on a chosen port with a generated key, pointed at the user's config directory. The key goes into `vscode.ExtensionContext.secrets`, never into settings or logs. The client refuses a server whose `contractVersion` it does not support and tells the user how to upgrade.

- [ ] Scaffold `src/Mux.VSCode` with `package.json` (activation events, contributed commands, views, configuration), `tsconfig.json` with `strict` enabled, and lint/format config.
- [ ] Implement `MuxServerLifecycle`: probe `127.0.0.1` health, start `mux serve --allow-tools` when absent, and stop a server the extension itself started on deactivate.
- [ ] Implement `ApiClient` over `fetch` with bearer auth, a typed `ApiError(status, body)` on any non-2xx, and per-request cancellation via `AbortSignal`.
- [ ] Store and retrieve the API key through `context.secrets`; never write it to settings, output, or the workspace.
- [ ] Negotiate `contractVersion` on connect; surface an actionable message and a docs link when the server is too old or too new.
- [ ] Route all diagnostics through a `mux` output channel; no `console.log` in shipped code.
- [ ] Contribute the extension settings: server port, auto-start on activate, default endpoint, approval posture, and enabled context sources — each a configuration key with a documented default, not a hard-coded constant.

**Definition of done:** activating the extension connects to a running server or starts one, rejects an incompatible contract version cleanly, keeps the key out of everything a user or log can see, and exposes its behavior through settings with sane defaults.

## Phase 2 — Chat panel and in-editor approvals

This is the first surface a user sees and the reason the transport choice matters. The panel is a webview that streams a run and lets the user approve the tools it wants to use without leaving the editor.

- [ ] Build the chat webview (vanilla TypeScript, VS Code theme tokens) with a transcript, a composer, and a send/stop control.
- [ ] Stream `POST /v1.0/api/chat/stream`; render `token` into the assistant message, `thinking` into a collapsible section, and `tool` events as cards that move running → ok/fail with elapsed time.
- [ ] Handle the `approval` event with a native approve / approve-for-session / deny prompt, and post the decision to `/v1.0/api/chat/approve`; default to deny on dismissal or timeout.
- [ ] Bind the panel to a workspace session: reuse the bound session id, or create one whose `workingDirectory` is the workspace root.
- [ ] Render loading, empty, and error states, with a retry path on error and a first-run empty state that says what to type.
- [ ] Support stop/cancel by aborting the stream and dropping the incomplete turn, matching the TUI and desktop.
- [ ] Never use `window.alert`/`confirm`/`prompt`; confirmations use a custom modal or a VS Code modal message.

**Definition of done:** a user holds a streamed conversation in the panel, sees tool activity as it happens, approves or denies a mutating tool in the editor, and the turn persists to the shared session store with its working directory set to the repo.

## Phase 3 — Editor context injection

A chat panel with no awareness of the open file is a worse terminal. This phase lets a user attach precise context to a prompt and makes the common attachments one click.

- [ ] Implement context providers for: active file, current selection, visible diagnostics, open editors, the workspace file tree (scoped and size-capped), the working-tree git diff, and the focused terminal's recent output.
- [ ] Add a context picker in the composer showing what is attached, its token estimate, and a control to drop each item.
- [ ] Cap and truncate large attachments predictably, and tell the user when an attachment was trimmed rather than silently sending less.
- [ ] Redact nothing silently; if a context source is disabled in settings, omit it and show that it was omitted.

**Definition of done:** a user attaches the file, selection, diagnostics, or diff to a prompt, sees exactly what will be sent and its cost, and no attachment is silently dropped or truncated.

## Phase 4 — Inline commands and code actions

The fastest path to value is the command a developer runs without composing a prompt at all. Each of these is an editor command, and the ones anchored to a location also appear as code actions.

- [ ] Contribute commands: Explain selection, Fix this diagnostic, Generate tests, Refactor selection, Write commit message, Summarize diff, Review current file.
- [ ] Offer Explain, Fix, and Refactor as code actions on the relevant selection or diagnostic.
- [ ] Route each command through the same session and streaming path as the panel, with the right context pre-attached (the diagnostic for Fix, the staged diff for the commit message).
- [ ] Make every command title, tooltip, and code-action label come from the i18n layer (Phase 8), not a literal string.

**Definition of done:** each command produces a streamed result against the workspace session with the correct context attached, the location-anchored ones appear as code actions, and no command title is a hard-coded string.

## Phase 5 — Diff review and checkpoints

Trust in an agent that edits files comes from seeing the edits at the right granularity. mux already snapshots the working tree before each turn inside a git repository, so the extension reviews and reverts against that history rather than inventing its own.

- [ ] After a turn that wrote files, show the changed set as native VS Code diffs (before-turn checkpoint vs. working tree).
- [ ] Offer per-file open and a jump-to-first-change for the turn's edits.
- [ ] Wire Undo and Redo to the checkpoint history via the server, restoring the working tree a turn at a time.
- [ ] Disable review and undo cleanly, with a stated reason, when the workspace is not a git repository.
- [ ] Record a stretch item for a propose-before-write mode (edits staged for accept/reject before they touch disk), dependent on a server capability that does not yet exist.

**Definition of done:** after an editing turn, a user reviews each change as a native diff and can undo the turn's file changes through the checkpoint history, with the feature degrading to a clear message outside a git repository.

## Phase 6 — Session tree and cross-surface continuity

The session tree is where the portability work pays off in the editor. It is a view over the same store the TUI and desktop read, so the list is shared by construction, not synchronized.

- [ ] Add a `mux Sessions` tree view listing sessions newest-updated first, with title, model, and message count.
- [ ] Support resume, rename, duplicate, export (Markdown and HTML), and delete from the tree, each backed by the corresponding session route.
- [ ] Bind and rebind the active session to the workspace, and reflect the bound session in the panel header.
- [ ] Add a "hand off" affordance that reveals the bound session's id so a user can resume it in the TUI or desktop, and pick up an externally-created session here.
- [ ] Refresh the tree on focus and after any run, since another surface may have changed the store.

**Definition of done:** the tree shows the same sessions as the other surfaces, all management verbs work from the editor, a session started in the TUI or desktop opens here with its transcript intact, and one started here appears there.

## Phase 7 — Configuration surface

A user should switch models and set an approval posture without opening a browser or a config file. This phase is a thin editor front end over the config routes, not a second settings system.

- [ ] Add an endpoint/model picker (status bar item plus command) reading `GET /v1.0/api/endpoints` and setting the active endpoint for the session.
- [ ] Surface approval posture (auto-safe vs. prompt) and the enabled context sources as first-class settings that map to the run options.
- [ ] Show a compact usage summary for the workspace session from `/v1.0/api/usage/summary` when telemetry is enabled, and a clear disabled state when it is not.
- [ ] Keep secrets masked end to end; the extension never displays or logs an endpoint key.

**Definition of done:** a user selects an endpoint and approval posture from the editor, the choice takes effect on the next turn, and no secret is ever shown.

## Phase 8 — Internationalization

Every string a user reads is translated, and this is built in from the first contributed command rather than retrofitted. The host strings live in `package.nls.json` and resolve through `vscode.l10n`; any webview strings resolve through `i18next` with stable keys. The baseline locale set matches the desktop app's reach.

- [ ] Externalize all user-visible strings — command titles, menu labels, tooltips, notifications, panel copy, tree labels, and empty/loading/error text — to i18n keys, with `en` as source and fallback.
- [ ] Localize accessibility strings (`aria-label`, `title`) alongside visible text.
- [ ] Ship the twelve baseline locales (`en, es, pt, fr, it, de, zh, ar, ru, ms, hi, ja`) with a locale registry recording code, English name, autonym, direction, and fallback.
- [ ] Route all number, date, relative-time, duration, byte, percent, and list formatting through explicit-locale helpers; no `join(', ')`, no hand-built `5m ago`.
- [ ] Set `lang` and `dir` in the webview, and verify layout survives roughly 40% text expansion, CJK, and RTL.
- [ ] Keep `mux`, command identifiers, and wire keys untranslated by policy, and document that choice.
- [ ] Add CI checks for missing keys, orphaned keys, new hard-coded strings, and a pseudo-locale (expansion plus RTL) pass.

**Definition of done:** no user-facing string is hard-coded, the twelve locales load, formatting is locale-aware, RTL and CJK render without breaking layout, and CI fails on a missing key or a new literal string.

## Phase 9 — Testing

Tests follow the principles in `BACKEND_TEST_ARCHITECTURE.md` translated to the VS Code toolchain: `@vscode/test-electron` for extension-host integration and end-to-end, and a fast unit runner for pure logic. Every test creates its own data and cleans up, the runner exits non-zero on failure, and loopback calls target `127.0.0.1` rather than `localhost` to avoid the Windows IPv6 stall.

- [ ] Unit-test the pure logic: `ApiClient` error mapping, context assembly and truncation, prompt composition, session binding, and every formatter.
- [ ] Integration-test the extension host: activation, server discovery-or-launch, a streamed turn against a stub server, approval round-trips, and the session tree verbs.
- [ ] Cover negative paths explicitly — an unreachable server, an incompatible contract version, a denied approval, a non-existent working directory, a truncated attachment, a non-git workspace.
- [ ] Support skip-with-reason for anything gated on an unshipped server capability (the propose-before-write mode).
- [ ] Wire unit tests to run on every commit and integration/e2e on every pull request, publishing results as CI artifacts.
- [ ] Keep shared test helpers free of console output; the runner owns reporting.

**Definition of done:** unit, integration, and end-to-end suites pass locally and in CI, negative cases are covered alongside happy paths, and a failing test returns a non-zero exit code.

## Phase 10 — Packaging and distribution

Distribution is CI-first and reaches both surfaces a VS Code user actually installs from. A native-installer matrix does not apply — the artifact is a `.vsix`, not an `.exe` or `.deb` — so `INSTALLERS.md`'s OS matrix and the `publisher.json` machinery are not used here, but its policy carries: one CI action publishes everything, assets live in this repository's releases, and no paid certificate is ever a prerequisite.

- [ ] Add a tagged CI workflow that builds, tests, packages the `.vsix`, and publishes it.
- [ ] Publish to both the VS Code Marketplace (`vsce publish`) and Open VSX (`ovsx publish`); shipping only one is an incomplete release.
- [ ] Attach the `.vsix` to the GitHub Release with a SHA-256 checksum.
- [ ] Keep the Marketplace and Open VSX tokens as GitHub Actions secrets; document them as the human-provided, one-time, free prerequisites.
- [ ] Verify the packaged extension activates from a clean install with no bundled server present, falling back to a helpful message that links install docs.

**Definition of done:** a tag produces a checksummed `.vsix` on the GitHub Release and published listings on both marketplaces, driven entirely by CI with only tokens provided by a human.

## Phase 11 — Documentation and website

Documentation ships with the feature, not after it. The repository gains a user guide and the standard project files the extension subtree needs, and the marketing site gains the editor as a first-class surface.

Repository documentation:

- [ ] Write `docs/VSCODE.md`: install, first run, the commands, context sources, approval flow, diff review, session handoff, settings, and troubleshooting by failure class.
- [ ] Add `src/Mux.VSCode/README.md`, `CHANGELOG.md`, and `LICENSE.md` (MIT), and a `.gitignore` for the Node/VS Code build outputs.
- [ ] Add a mux `README.md` highlight and a CLI/surfaces mention so the editor is discoverable from the top of the repo.
- [ ] Refresh `IMPROVEMENTS.md` §3 to mark the IDE surface delivered as phases land.

Website (`c:\code\web sites\usemux.ai`, a separate git repository with its own push):

- [ ] Change the hero badge `TUI · Web · Desktop · Headless` to include the editor, and the "One engine, four surfaces" eyebrow to "five surfaces".
- [ ] Add an editor surface card to the surfaces grid describing the VS Code extension, with its install command.
- [ ] Add a documentation card linking to the VS Code guide.
- [ ] Add a showcase row or screenshot of the extension in the editor once the panel and diff review are real.
- [ ] Commit and push the site repository independently of the mux repository.

**Definition of done:** a new user can install and reach a working run from `docs/VSCODE.md` alone, the extension subtree carries its required project files, and usemux.ai presents the editor as one of five surfaces with a working install path.

## Phase 12 — Stretch: deeper editor integration

These raise the ceiling once the core is trustworthy, and each depends on work outside the extension. They are listed so the plan does not pretend the editor story ends at a chat panel, and marked as dependent so no one starts them before their prerequisite exists.

- [ ] LSP-aware context and tools: feed symbols, definitions, references, hover text, and the call hierarchy into a run, and expose them as tools.
- [ ] Live session mirroring over the completed `/v1.0/ws` event bridge, so a run started in the TUI or desktop streams into the editor panel in real time (depends on the WebSocket bridge, currently a stub).
- [ ] Propose-before-write diff mode, staging a turn's edits for accept/reject before they touch disk (depends on a new server capability).

**Definition of done:** each item ships only after its named dependency does, and its absence is documented as a known limitation until then.

## Capability mapping to `IMPROVEMENTS.md` §3

The IDE surface table in `IMPROVEMENTS.md` set the bar; this plan says where each row lands and in which phase, so the roadmap and the plan do not drift.

| IMPROVEMENTS.md row | Plan phase | Notes |
|---|---|---|
| Context injection | 3 | File, selection, diagnostics, open tabs, tree, git diff, terminal output. |
| Inline commands | 4 | Explain, fix, tests, refactor, commit message, summarize diff, review file. |
| Diff workflow | 5 | Native diffs + checkpoint undo now; propose-before-write is Phase 12. |
| LSP awareness | 12 | Depends on wiring language-server data as context and tools. |
| Run control | 2, 6 | Start, stream, stop, approve, and resume; pause is a server capability, not yet present. |
| Session binding | 6 | A workspace binds to a shared-store session, reopenable across surfaces. |
| Terminal handoff | 6 | Session id handoff both directions; live mirroring is Phase 12. |
| Local server discovery | 1 | Discover-or-launch, contract-version negotiation, secret storage. |
| Extension settings | 1, 7 | Models, approval posture, context sources, and shortcuts. |

## Compliance ledger

Each standard in `c:\code\agents\requirements` is accounted for below, including the ones that do not apply, because an unexplained gap reads as an oversight rather than a decision.

| Standard | How this plan complies, or why it does not apply |
|---|---|
| `WRITING_DOCUMENTS.md` | This document is written to it: prose sections with a stance, no template repetition, and a final revision pass against its checklist before it is considered done. |
| `REPOSITORY_REQUIREMENTS.md` | Extension under `src/Mux.VSCode` with `src/`, `test/`, `dashboard/`; `README.md`, `CHANGELOG.md`, `LICENSE.md` (MIT), `.gitignore`. Docker, `DOCKERHUB_README.md`, and the MCP/REST server files are N/A — the extension ships no container and exposes no server. |
| `CODE_STYLE.md` | C# specifics are adapted to TypeScript intent: TSDoc on exported members only, `strict` null handling, guard-clause validation, specific error types with context, cancellation on async work, one class per file, no `console.log` in shipped code, configurable values over constants. |
| `FRONTEND_ARCHITECTURE.md` | The React 19 + Vite stack, hand-rolled SVG charts, and the operator dashboard pages are N/A by choice — the webview is dependency-free vanilla, matching mux's dashboard. The portable rules bind: one `ApiClient`, typed `ApiError`, loading/empty/error states, VS Code theme tokens, i18n as a first-class concern. |
| `DASHBOARD_STYLE_AND_USABILITY.md` | The dashboard shell, route surface, and operator pages are N/A — this is an editor extension, not a dashboard. Component rules carry: no `window` dialogs, a reusable copy control that preserves exact values, loading/empty/error states, and the accessibility set (landmarks, `aria-label`, focus, color-is-not-the-only-signal, reduced motion). |
| `I18N.md` | Fully applies (Phase 8): every user-facing and accessibility string localized through `vscode.l10n`/`i18next` with stable keys, twelve baseline locales, locale-aware formatters, RTL/CJK verification, and CI checks for missing keys and new literals. |
| `BACKEND_TEST_ARCHITECTURE.md` | Framework specifics (Touchstone/xUnit/`.csproj`) are N/A for TypeScript; the principles apply (Phase 9): unit/integration/e2e tiers, self-contained tests, negative cases, skip-with-reason, non-zero failure exit codes, `127.0.0.1` over `localhost`, CI-wired. |
| `TELEMETRY_REQUIREMENTS.md` | N/A: the extension runs no Watson/OTLP backend and emits no Prometheus/Tempo signals. Any usage reporting obeys VS Code's `telemetry.telemetryLevel` and `vscode.env.isTelemetryEnabled`, is best-effort, and keeps ids and secrets out of labels. |
| `INSTALLERS.md` | The native-installer OS matrix and `publisher.json` machinery are N/A — the artifact is a `.vsix`. The policy applies (Phase 10): CI-first single-action release, both delivery surfaces (Marketplace and Open VSX) plus a checksummed GitHub Release asset, assets in this repository, tokens as GitHub secrets, no paid certificate as a prerequisite. |

## Sequencing and risk

Order follows dependency, not feature glamour. Phase 0 unblocks correct file edits and must land first, or every later demo writes to the wrong directory. Phases 1 and 2 are the spine — a connected server and a streaming panel with approvals — and until both work, nothing else can be seen. Context, inline commands, diff review, and the session tree (Phases 3 through 6) are independent enough to parallelize once the spine holds, though diff review leans on the checkpoint behavior and should follow a working editing turn. Configuration, i18n, tests, packaging, and documentation (Phases 7 through 11) harden what exists; i18n in particular is cheaper done alongside each surface than bolted on at the end, so treat its checklist as a running obligation rather than a late phase, even though it is numbered late for readability.

The sharpest risk is scope drift toward reimplementing the agent in the editor the first time a server round-trip feels slow or a capability is missing. The discipline that keeps the extension honest is the same one that keeps mux's provider and routing stories clean: when the editor wants something the engine does not expose, the fix belongs in `Mux.Core` and its API, behind a version the client negotiates — not in a parallel TypeScript loop that will rot against the contract. A capability the extension needs and the server lacks is a Phase 0-shaped task, not an in-extension shortcut.
