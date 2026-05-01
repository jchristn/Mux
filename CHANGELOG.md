# Changelog

All notable changes to mux are documented here.

## Unreleased

### Added

- `mux endpoint list` and `mux endpoint show <name>` as top-level non-interactive commands, including machine-readable `json` output for stored endpoint inspection with redacted secret-like header values
- `ARMADA.md` as a focused integration guide for orchestrator consumers, plus a tightened `ARMADA_IMPROVEMENTS.md` plan that reflects the current CLI/runtime surface

### Removed

- Interactive queued-message support, `/queue` commands, and `Alt+Up` queued-prompt editing from the current REPL flow

### Changed

- `mux print` now supports `--output-last-message <path>` to write only the final assistant response text to a file; failed runs leave the file absent
- `mux print`, `mux probe`, and `mux endpoint` now support `--config-dir <path>` as a first-class config-root override, with precedence over `MUX_CONFIG_DIR`
- `mux probe --require-tools` now fails when the selected endpoint disables tool calling
- `/mcp add` now runs a guided wizard similar to `/endpoint add`, supports optional inline defaults, and saves MCP server definitions to `mcp-servers.json`
- Interactive REPL prompt entry now uses a simpler blocking one-prompt-at-a-time flow with idle multi-line editing and paste support, inline approvals, `Esc` cancellation, a visible `Generating title...` notice when automatic title refresh runs, and an explicit blank spacer line before the next `mux>` prompt
- README and usage documentation now describe Armada-oriented automation flows, isolated config overrides, clean final-response artifacts, and machine-readable endpoint inspection

### Testing

- Added `Test.Xunit` coverage for `--config-dir`, `--output-last-message`, `probe --require-tools`, and `endpoint list/show`
- Added Armada-style `Test.Automated` contract coverage for isolated config directories and endpoint inspection

## v0.2.0 - 2026-04-24

### Added

- Interactive endpoint management via `/endpoint list`, `/endpoint show <name>`, guided `/endpoint add` and `/endpoint edit <name>` workflows, and confirmed `/endpoint remove <name>` so endpoints can be inspected and maintained from within mux itself
- Interactive queued-message support in the REPL so users can keep drafting while mux is busy and queue the next prompt with `Tab`
- `/queue`, `/queue clear`, `/queue drop-last`, and `/queue resume` interactive commands for queue inspection and control
- `/status`, `/compact`, and `/title` interactive commands for session inspection, history compaction, and direct title control
- `/compact summary` and `/compact strategy [summary|trim]` so compaction policy can be overridden per command or changed for the live interactive session
- `/compact trim` for explicit trim-only history compaction without a summary-model sidecar call
- `/context` as an interactive alias for `/status`
- `Alt+Up` editing for the newest queued prompt during interactive sessions
- Inline interactive status above the prompt for busy, paused, and approval states
- Automatic conversation-title tracking in interactive mode, including `Conversation title update: ...` transcript notices when the model revises the title
- Estimated context-budget reporting for system prompt, persisted history, tool surface, remaining budget, and compaction metadata
- Compaction-related settings in `settings.json` for automatic preflight compaction, warning threshold, strategy, and preserved turns

### Changed

- `Esc` now cancels the active interactive generation without exiting mux
- Cancelling or failing an interactive run pauses queued-message auto-dispatch until the user resumes it
- Interactive `/clear` now redraws the screen with the current conversation title at the top
- Interactive streamed output now keeps the next `mux>` prompt off the response line and preserves exactly one blank spacer line before the prompt, including when output reaches the bottom of the terminal
- Interactive runs now check the pending prompt against the estimated context budget before starting and automatically compact older history when needed
- `--compaction-strategy <summary|trim>` now overrides the effective compaction policy for interactive, print, and probe startup
- Interactive mode now emits a low-noise post-turn context notice only when the session is approaching or over the usable context budget
- `AgentLoop` now honors the configured compaction strategy for oversized active conversation state before model calls and emits additive `context_status` / `context_compacted` JSONL events plus extended context metadata on `run_started` / `run_completed`
- Non-streaming LLM calls now build non-streaming backend requests, which stabilizes `probe` and the new model-driven title/compaction sidecar calls
- Interactive help and README documentation now describe queueing, cancellation, and the inline status-line behavior

### Testing

- Added endpoint command parser and endpoint persistence unit coverage
- Added `QueuedMessageManager` unit coverage for FIFO dequeue, newest-item editing/removal, and queue clearing
- Verified with `dotnet test src\Mux.sln --nologo`

## v0.1.0 - 2026-03-31

### Added

- Structured CLI output for orchestration with `mux print --output-format jsonl`
- New lifecycle events: `run_started` and `run_completed`
- `mux probe` command for config and backend health validation
- Machine-readable `json` output for `mux probe`
- Best-effort redaction for secret-like values in structured event payloads
- Documentation for the orchestration contract, output formats, exit codes, and `MUX_CONFIG_DIR`
- Shared `contractVersion` marker across `print` JSONL events and `probe` JSON payloads

### Changed

- `mux print` now has a formal non-interactive contract with documented exit codes
- `mux print` `error` events now expose `errorCode`, `failureCategory`, and runtime metadata when known while remaining backward compatible with existing `code` consumers
- Named endpoint selection now fails explicitly when `--endpoint` references a missing endpoint
- CLI approval parsing now accepts documented values `ask`, `auto`, and `deny`
- Tool-call argument parsing is more tolerant of malformed Windows-style path escaping

### Testing

- Expanded `Test.Xunit` coverage for structured formatting, CLI command output, and config resolution
- Expanded `Test.Automated` coverage for lifecycle events, JSONL output, and probe output
- Stabilized mock-server route matching and process-test cleanup behavior

## 2026-03-30

Initial alpha release.
