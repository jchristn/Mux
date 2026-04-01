# Changelog

All notable changes to mux are documented here.

## 2026-03-31

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
