# Changelog

All notable changes to the mux VS Code extension are documented here.

## 0.2.0

- **Management view.** A new "Manage" tree in the mux side bar surfaces the server's live configuration and
  lets you change it without leaving the editor: connection status (version, contract, URL) with reconnect;
  endpoints (add / edit / delete / set default via a form); MCP servers (add / edit / delete); prompt
  profiles (set active); skills (enable / disable); a settings editor; and a usage summary. Everything runs
  over the same local REST API the dashboard and CLI use.
- **Connection status bar item** showing connected / connecting / not-connected at a glance, with the
  version and contract in the tooltip; click to reconnect.
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
