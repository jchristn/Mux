# Changelog

All notable changes to the mux VS Code extension are documented here.

## 0.1.0

Initial alpha.

### Added

- Chat panel that streams a mux run (assistant text, tool activity, completion stats) over the local server's
  SSE surface, with in-editor tool approvals when the server runs with `--allow-tools`.
- Editor context injection: active file, selection, diagnostics, open tabs, and working-tree diff, with a
  report of anything requested but unavailable (terminal scrollback, which the stable API does not expose).
- Inline commands and code actions: Explain selection, Fix this problem, Generate tests, Refactor selection,
  Write commit message, Summarize diff, Review current file.
- Session tree over the shared mux session store: resume, rename, duplicate, export, and delete, reflecting
  sessions created in the terminal UI and desktop app.
- Endpoint picker in the status bar, backed by the server's endpoint list.
- Local-server lifecycle: discover a reachable, contract-compatible server or start one on loopback with a
  key kept in VS Code secret storage.
- Internationalization scaffold: locale registry, locale-aware formatters, English strings, a pseudo-locale,
  and a CI key-drift check. Human translations for the other eleven baseline locales are seeded and pending.
