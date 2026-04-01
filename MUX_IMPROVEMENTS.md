# Mux Improvements

This file tracks additional mux follow-up work identified while reviewing Armada's `MUX_INTEGRATION.md` against the latest mux source.

These items are not blockers for the current Armada integration plan, but they would make the non-interactive contract easier and safer for an external orchestrator to consume over time.

## 1. Bring `print` Failure Metadata Up To Probe Parity

Status:
- [x] Add structured failure categorization to non-interactive `print` errors, not just `code` and `message`
- [x] Include the same key runtime metadata on bootstrap failure paths that `probe` already returns where practical
- [x] Add automated coverage for `print` bootstrap/runtime failure classification

Why:
- `mux probe --output-format json` now returns `errorCode`, `failureCategory`, and runtime metadata that Armada can map without string parsing
- `mux print --output-format jsonl` still emits a thinner `error` event on bootstrap/runtime failure, so Armada needs a different failure-handling path for launch issues than for health checks

Suggested contract direction:
- keep the current `error` event shape backward compatible
- add `errorCode` as a compatibility alias for `code` and add a machine-readable `failureCategory` field for bootstrap/runtime failures
- expose enough runtime context to diagnose endpoint-selection and config-directory problems without relying on stderr text

Implemented:
- `mux print --output-format jsonl` `error` events now include `contractVersion`, `errorCode`, and `failureCategory`
- `print` bootstrap/runtime failures now carry runtime metadata when known, including `commandName`, `configDirectory`, `endpointSelectionSource`, resolved endpoint identifiers, and `cliOverridesApplied`
- coverage now pins both bootstrap and runtime failure classification paths

## 2. Add An Explicit Structured Output Contract Version

Status:
- [x] Add a version marker for `mux print --output-format jsonl` events and `mux probe --output-format json`
- [x] Document compatibility expectations for additive vs breaking field changes
- [x] Add tests that pin the advertised contract version and required baseline fields

Why:
- mux now exposes materially richer orchestration metadata in `run_started` and `probe`
- Armada will depend on those fields, and an explicit contract version would let orchestrators evolve parsers safely instead of inferring compatibility from release notes or source history

Suggested contract direction:
- add a shared top-level `contractVersion` field that is stable across one contract generation
- treat additive fields as non-breaking within a version
- bump the contract version when field meaning or required presence changes

Implemented:
- `contractVersion: 1` is now emitted on every `mux print --output-format jsonl` event and every `mux probe --output-format json` payload
- additive fields are documented as non-breaking within a contract version
- the contract version should be bumped only for removals, renames, type changes, or semantic changes to required fields

## Definition Of Done

Status:
- [x] Complete

This follow-up file is complete. Non-interactive `print` failures now expose a machine-readable classification surface comparable to `probe`, and mux publishes an explicit structured-output contract version for orchestrators.
