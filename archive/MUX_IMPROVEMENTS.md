# Mux Improvements

This file tracks helpful Mux follow-up work discovered while validating the Armada integration plan against current source.

These items are not required to unblock Armada v1, but they would reduce ambiguity, improve operator UX, or close capability gaps in the current non-interactive contract.

## 1. Make MCP Support In Print Mode Explicit

Status:
- [x] Either wire MCP server loading into `mux print`, or explicitly document and enforce that MCP is unsupported in print mode
- [x] If MCP remains unsupported, remove or hide misleading CLI implications where possible

Why:
- `mux print` currently accepts `--no-mcp` through shared settings, but MCP servers are only wired in interactive mode
- this creates a false appearance of capability parity and can mislead orchestrators about security or tool availability

## 2. Improve Probe Failure Classification

Status:
- [x] Return richer probe error codes for common failure classes
- [x] Keep machine-readable output stable enough for orchestrators to classify failures without string parsing

Why:
- `mux probe` is good enough for Armada v1 backend validation
- current generic `probe_error` responses force consumers to inspect `errorMessage` text for UX classification

## 3. Strengthen Non-Interactive Contract Docs

Status:
- [x] Document the recommended orchestrator command shape with explicit `--endpoint`
- [x] Document that `mux print` defaults approval policy to `Deny`
- [x] Document precedence rules for print mode: `endpoints.json` base values, then CLI overrides
- [x] Document that `settings.json` is not required for the current print-mode orchestration path

Why:
- the runtime contract is source-backed today, but some of the most important orchestration constraints are still easy to miss

## 4. Optional Capability Introspection

Status:
- [x] Consider adding a machine-readable way to report effective print-mode capabilities, especially MCP availability and tool support

Why:
- orchestrators currently have to infer some capabilities from source knowledge or docs instead of asking Mux directly

## Definition Of Done

This follow-up file is complete when the misleading print-mode MCP surface is resolved, probe failures have better machine-readable classification, and the non-interactive contract is documented with enough precision for external orchestrators.

## Additional Improvements Completed

- [x] Reject `--approval-policy ask` in non-interactive `print` and `probe` modes instead of implicitly falling through to non-interactive approval behavior
- [x] Include effective command metadata in `run_started` JSONL events: command name, config directory, endpoint selection source, CLI override categories, tool counts, and MCP support/config state
- [x] Include the same effective runtime metadata and capability data in `mux probe --output-format json`
- [x] Add automated coverage for structured capability reporting and classified non-interactive failure modes
