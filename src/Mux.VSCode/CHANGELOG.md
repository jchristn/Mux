# Changelog

All notable changes to the mux VS Code extension are documented here.

## 0.12.3

- **Adaptive endpoint form + flexible API-key placement.** The endpoint editor now shows only the fields that
  apply to the chosen adapter (region/project for Vertex/Bedrock, api-version for Azure, a custom-headers
  editor for HTTP adapters), and the OpenAI-family adapters gained an API-key **placement** control — send the
  key as a bearer header, a custom header, or a query-string parameter — for services that don't use bearer
  tokens. Fields show/hide live as you change the adapter or placement.
- **First-run setup wizard.** On a fresh install the extension guides you through defining an endpoint,
  checking connectivity, and sending a first prompt, then records completion (shared with every other surface
  via the server) so it doesn't reappear. Re-run it any time with **mux: Run setup wizard**.
- **Operational prompt catalog in the management tree.** Under **Prompts**, every model-facing prompt now
  appears grouped by kind with a custom/default indicator; click an entry to edit its override or reset it to
  the default (`mux.manage.editCatalog` / `mux.manage.resetCatalog`, backed by the server's prompt-catalog
  routes). The prompt-profile form now edits all three prompt fields (system, tools-disabled, compaction), and
  subagent personas are listed read-through with a deep link to the subagent editor.
- **Clear "mux CLI not found" guidance.** The extension is a thin client for the mux CLI — with auto-start on
  it runs `mux serve` to provide the local server. If mux isn't installed it now says so up front (once) with
  an **Install mux** link and a shortcut to `mux.server.path`, instead of failing a first chat with a vague
  timeout; auto-start also fails fast with the same clear message.
- **Smart large-file context.** The active file is no longer hard-sliced at 8000 characters: a small file is
  inlined whole, and a large file is sent to the server to be turned into a structural map (or summary) with
  line ranges, which is inlined verbatim. "Large" now scales with the selected endpoint's context window (a
  wider window inlines bigger files), and a new `mux.context.largeFileMode` setting (inherit/map/summarize/
  truncate) controls the mode from the editor. The LSP outline now emits line ranges. If the server is
  unreachable the extension falls back to the previous truncation.

## 0.12.2

- **Live cross-surface transcript sync, now reliable.** A conversation open in the editor reloads when a turn
  is added to it from another surface (terminal, desktop, web) — even a turn written straight to the shared
  store with no run through the hub, because the server now watches the store and rebroadcasts changes. The
  extension also keeps a conversation live-synced whenever it has one open (a locally created or streamed
  conversation, not only one resumed from the tree), so it no longer silently misses external updates.

## 0.11.3

- **Usage charts: endpoint and model filters.** The usage dashboard now has endpoint and model dropdowns
  next to the range selector, so you can scope tokens, cost, and latency to a single endpoint and/or model
  (backed by `GET /v1.0/api/usage/filters`).
- **Click a Manage item to edit it.** Single-clicking an endpoint, MCP server, prompt, subagent, or skill in
  the Manage tree opens its editor — no need for the right-click menu.

## 0.11.2

- **Usage charts: axes and a taller plot.** The charts now have Y-axis gridlines with value labels and
  X-axis time ticks, and are 75% taller for readability.

## 0.11.1

- **Fix: Markdown now renders in chat replies.** The Markdown renderer is inlined into the chat page instead
  of loaded as a separate script, so it is always defined before the panel runs — previously a reply could
  render as plain text (raw `**` and code fences).

## 0.11.0

- Aligned the extension version with the rest of the mux product (the .NET assets).
- **Thinking display.** Model reasoning now streams into a collapsible "💭 Thinking" section above the answer,
  matching the web dashboard and desktop app.
- **Fix:** the first token of a reply could be dropped (the streaming buffer was reset after the first token
  was appended), so a reply like "If I had a name…" rendered as "I had a name…". The final render now also
  falls back to the server's authoritative full content.

## 0.2.1

- **Richer usage charts.** The usage dashboard now has six metric views: a stacked token breakdown
  (prompt/cached/output), a cost bar, and min–avg–p95–p99–max distribution candlesticks for latency, TTFT,
  streaming, and throughput — with a legend and per-bar tooltips.
- **Fully localized panels.** The management forms, About, usage, help, and connection dialogs are translated
  into all twelve languages (first pass; CJK/Arabic/Hindi benefit from a native review).
- **Fuller management.** MCP servers now edit auth (bearer token / API key with a preserved-when-blank
  secret, header, and path); prompts, subagents, and skills are fully add/edit/delete (skills create and edit
  their `SKILL.md`); and a **native usage dashboard** (KPIs plus a hand-rolled over-time chart with range and
  metric toggles) opens from the Manage view or `/usage`.
- **Chat slash commands.** Type `/help` (or `/?`) for the list; `/new`, `/clear`, `/endpoints`, `/usage`,
  `/settings`, `/cwd`, `/reconnect`, and `/about` run without leaving the composer.
- **Fixes.** Markdown now preserves single newlines within a paragraph (replies no longer collapse to one
  line). The activity-bar icon is the real blocky mux "M" mark, not a plain letter.
- **Refined chat panel.** Cleaner message layout with role labels, a mux-accent for assistant replies, a
  streaming cursor, rounded chips for tool calls, an auto-growing composer (Enter to send, Shift+Enter for a
  newline), and a tidier empty state — all on VS Code theme tokens.
- **Per-turn stats on hover.** Each completed reply shows a small footer (time · tokens); hovering it reveals
  the full breakdown — time to first token, streaming, total, and input/output/total tokens — like the web
  dashboard and desktop app.
- **Markdown rendering in chat.** Replies stream in as plain text token by token (unchanged responsiveness),
  then render as Markdown when the turn completes — headings, bold/italic, inline and fenced code, lists,
  blockquotes, links, and rules. Loaded conversations render the same way. The renderer is dependency-free
  and HTML-escapes untrusted model output before rendering.
- **Management view.** A new "Manage" tree in the mux side bar surfaces the server's live configuration and
  lets you change it without leaving the editor: connection status (version, contract, URL) with reconnect;
  endpoints (add / edit / delete / set default via a form); MCP servers (add / edit / delete); prompt
  profiles (set active); skills (enable / disable); a settings editor; and a usage summary. Everything runs
  over the same local REST API the dashboard and CLI use.
- **Connection status bar item** showing connected / connecting / not-connected at a glance, with the
  version and contract in the tooltip. Click it for About when connected, or for connection help when not.
- **Connection help.** When the extension can't reach a server, it explains why and offers to retry, start
  `mux serve` in a terminal, or open the relevant settings — instead of a bare error.
- **About panel** with the logo, the extension and server versions, a link to open the connected server's
  dashboard (for the richer browser configuration/monitoring surface), and links to GitHub and the docs.
- Cleaned up the activity-bar icon (a single mux "M" mark instead of the previous "MX").

## 0.1.1

- Added the Marketplace listing icon.

## 0.1.0

Initial alpha.

### Added

- Chat panel that streams a mux run (assistant text, tool activity, completion stats) over the local server's
  SSE surface, with in-editor tool approvals when the server runs with `--allow-tools`.
- Editor context injection: active file, selection, diagnostics, open tabs, and working-tree diff, with a
  report of anything requested but unavailable (terminal scrollback, which the stable API does not expose).
- Inline commands and code actions: Explain selection, Fix this problem, Generate tests, Refactor selection,
  Write commit message, Summarize diff, Review current file.
- LSP-aware context: an optional `symbols` source attaches the file's symbol outline and the hover for the
  selection from the installed language server.
- Turn-level undo/redo: each run records a git checkpoint in the workspace, and Undo/Redo restore a turn's
  file changes through the server (git shadow refs only — your branch, history, and stash are untouched).
- Session tree over the shared mux session store: resume, rename, duplicate, export, and delete, reflecting
  sessions created in the terminal UI and desktop app.
- Endpoint picker in the status bar, backed by the server's endpoint list.
- Local-server lifecycle: discover a reachable, contract-compatible server or start one on loopback with a
  key kept in VS Code secret storage.
- Internationalization: locale registry, locale-aware formatters, and the twelve baseline locales
  (`en, es, pt, fr, it, de, zh, ar, ru, ms, hi, ja`) translated for the command palette, settings, and
  runtime UI, plus a pseudo-locale and a CI key-drift check. Command prompts sent to the model stay in
  English by design. The CJK, Arabic, and Devanagari translations are a first pass and benefit from a native
  review.
