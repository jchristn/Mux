# Mux Enhancements For Armada Integration

This document captures the Mux-side changes needed to support a production-quality Armada integration.

Use this file as the working implementation plan:
- Change `[ ]` to `[x]` when complete.
- Add initials and date beside completed items when helpful.
- Add short notes under blocked or deferred items.

## Goal

Enable Armada to launch Mux non-interactively with isolated config, structured machine-readable events, and predictable operational behavior.

## Guiding Rules

- Keep Mux generic; do not add Armada-branded output formats
- Reuse Mux's existing event model where possible
- Preserve interactive UX while improving non-interactive orchestration support

## 1. Structured CLI Output

Goal: expose the existing agent event stream as a stable CLI contract.

Status:
- [x] Add a CLI flag such as `--output-format jsonl`
- [x] Define the supported values and default behavior
- [x] Serialize one event per line in JSONL mode
- [x] Ensure event payloads include enough data for orchestration consumers
- [x] Keep assistant content distinct from lifecycle/tool events
- [x] Document the schema and backward-compatibility expectations

Existing implementation anchors:
- `C:\code\mux\src\Mux.Core\Agent\AgentLoop.cs`
- `C:\code\mux\src\Mux.Core\Enums\AgentEventTypeEnum.cs`

Minimum event coverage:
- heartbeat
- assistant text
- tool call proposed
- tool call approved
- tool call completed
- error
- completion/final-state event

Acceptance criteria:
- A non-interactive consumer can parse Mux output without scraping plain text
- JSONL mode works for `mux print`
- Existing default human-readable CLI behavior remains intact

## 2. Stable Non-Interactive Launch Contract

Goal: make `mux print` dependable as an orchestrator entrypoint.

Status:
- [x] Confirm and document the supported non-interactive command for orchestrators
- [x] Ensure `mux print` exits with meaningful non-zero codes on failure
- [x] Ensure launch flags needed by orchestrators are supported and documented
- [x] Validate that model and endpoint overrides work cleanly in non-interactive mode
- [x] Ensure approval policy behavior is explicit in docs and CLI help

Acceptance criteria:
- Armada can launch a complete Mux session through one documented command pattern
- Failures are distinguishable by exit code and stderr/log output

## 3. Config Isolation And Resolution

Goal: guarantee that orchestration consumers can fully control the active mux config.

Status:
- [x] `MUX_CONFIG_DIR` support exists in `SettingsLoader.GetConfigDirectory`
- [x] Document `MUX_CONFIG_DIR` as the supported orchestration mechanism
- [x] Confirm all config reads respect `MUX_CONFIG_DIR`
- [x] Confirm config seeding behavior does not overwrite orchestrator-provided files
- [x] Add tests covering temp config directories and concurrent isolated runs

Existing implementation anchor:
- `C:\code\mux\src\Mux.Core\Settings\SettingsLoader.cs`

Acceptance criteria:
- Two Mux processes can run with different config directories on the same host
- Mux never falls back to user-home config when `MUX_CONFIG_DIR` is set

## 4. Event Payload Completeness

Goal: ensure orchestration consumers receive enough detail to drive progress and debugging.

Status:
- [x] Review existing `AgentEvent` payloads for missing launch/runtime metadata
- [x] Include endpoint and effective model information early in the event stream
- [x] Include actionable error payloads for backend/auth/model failures
- [x] Include completion/failure summary data at the end of a run
- [x] Avoid leaking secret values in serialized events

Acceptance criteria:
- Armada can show effective endpoint/model and useful failure reasons without scraping arbitrary text

## 5. Probe And Health Follow-Up

Goal: provide a Mux-owned preflight path that exercises the same config resolution as runtime launch.

Status:
- [x] Design a `mux probe` command or equivalent health-check mode
- [x] Ensure probe uses the same config loading path as `mux print`
- [x] Validate endpoint reachability, auth, and model access
- [x] Return machine-readable success/failure output
- [x] Document intended orchestrator usage

Notes:
- This is not required to unblock Armada v1
- This is the preferred long-term health contract

Acceptance criteria:
- An orchestrator can validate mux config and backend access without running a full session

## 6. Documentation And CLI Help

Goal: make the new orchestration surface discoverable and supportable.

Status:
- [x] Update README or command docs for `mux print` orchestration usage
- [x] Document `MUX_CONFIG_DIR`
- [x] Document structured output mode with example JSONL lines
- [x] Document any approval-policy and model-selection flags used in orchestration scenarios
- [x] Add troubleshooting notes for bad auth, missing model, and unreachable backend

Acceptance criteria:
- Another tool can integrate with Mux using public docs only

## 7. Tests

Goal: prevent regressions in the Mux features Armada depends on.

Status:
- [x] Add tests for JSONL event emission
- [x] Add tests for config isolation with `MUX_CONFIG_DIR`
- [x] Add tests for non-interactive launch error behavior
- [x] Add tests for any future `probe` command
- [x] Add regression tests for secret redaction in logs/events

Acceptance criteria:
- The Armada-critical Mux behavior is covered by automated tests

## Definition Of Done

The Mux enhancement work is complete when all of the following are true:
- Mux exposes a documented structured output mode suitable for orchestration
- `MUX_CONFIG_DIR` is documented and covered by tests
- `mux print` is a stable non-interactive launch surface
- A follow-up path exists for robust Mux-owned health probing

## Completion Note

Completed in `Mux` on 2026-03-31 and validated with build, xUnit, and automated mock-server test runs.
