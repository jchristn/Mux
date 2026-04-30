# Armada Integration Plan for mux

This file tracks the Mux-side work needed to make `mux` a stable Armada captain target. It now reflects the current repo state instead of treating every orchestration surface as net-new work.

## Current Status

Supported today:
- `mux print` as the non-interactive launch contract
- `mux probe` as the machine-readable validation contract
- versioned JSONL and JSON contracts with regression coverage
- named endpoint selection plus CLI endpoint overrides
- `--config-dir` for first-class config isolation
- `--output-last-message <path>` for clean final-answer capture
- `mux endpoint list/show --output-format json` for endpoint inspection
- `mux probe --require-tools` for Armada compatibility validation
- explicit documentation that non-interactive MCP is unsupported

Still pending:
- a first-class secure ad-hoc auth/header override surface for authenticated backends
- optional Armada-specific profile/mode work only if the normal `print` and `probe` contracts become awkward

## Recommended Armada Contract

Launch:

```text
mux print --config-dir <mux-config> --output-format jsonl --output-last-message <result-file> --yolo --endpoint <name> --working-directory <dock> "<prompt>"
```

Validate:

```text
mux probe --config-dir <mux-config> --output-format json --require-tools --endpoint <name>
```

Inspect endpoints:

```text
mux endpoint list --config-dir <mux-config> --output-format json
mux endpoint show <name> --config-dir <mux-config> --output-format json
```

## Must-Have Work

### 1. Clean final-message artifact

- [x] Add a non-interactive flag to `mux print` that writes only the final assistant-visible response to a file.
- [x] Use a stable CLI-aligned flag name: `--output-last-message <path>`.
- [x] Ensure the file contains only the final assistant response text.
- [x] Ensure the file never contains heartbeat lines, tool-call metadata, diagnostics, or JSONL events.
- [x] Define deterministic failure behavior: if the run fails, mux does not create the file.
- [x] Document the behavior clearly.

Notes:
- [x] Progress: implemented in `PrintCommand`, documented in `README.md`, `USAGE.md`, and `ARMADA.md`.
- [x] Notes: covered by CLI regression tests and an Armada-style automated contract test.

### 2. Treat JSONL output as a supported automation contract

- [x] Define `mux print --output-format jsonl` as a supported integration surface.
- [x] Version the contract.
- [x] Document the event sequence and compatibility rules.
- [x] Keep key lifecycle and error events machine-readable and consistent.
- [x] Add regression tests that pin representative fields and event types.

Notes:
- [x] Progress: contract is documented in `USAGE.md` and covered by `CliCommandTests` and `CliContractTests`.
- [x] Notes: `contractVersion` remains the parser compatibility gate.

### 3. First-class config directory flag

- [x] Add `--config-dir <path>`.
- [x] Keep `MUX_CONFIG_DIR` working for backward compatibility.
- [x] Define precedence: `--config-dir` > `MUX_CONFIG_DIR` > `~/.mux/`.
- [x] Ensure `print`, `probe`, and endpoint inspection honor the same rules.
- [x] Include the effective config directory in machine-readable output.

Notes:
- [x] Progress: implemented through scoped config-directory override support in `SettingsLoader`.
- [x] Notes: CLI tests verify that `--config-dir` overrides `MUX_CONFIG_DIR`.

### 4. Stable endpoint selection and override behavior

- [x] Freeze and document the non-interactive endpoint override contract used by `print` and `probe`.
- [x] Support and test:
- [x] named endpoint selection via `--endpoint`
- [x] ad-hoc model override via `--model`
- [x] ad-hoc base URL override via `--base-url`
- [x] ad-hoc adapter override via `--adapter-type`
- [x] ad-hoc sampling override via `--temperature`
- [x] ad-hoc output token override via `--max-tokens`
- [x] Document how named endpoint data and CLI overrides merge.
- [x] Report applied override categories in machine-readable output.

Notes:
- [x] Progress: already implemented before this pass; tightened docs remain in `CONFIG.md` and `USAGE.md`.
- [x] Notes: `print` and `probe` share `CommandRuntimeResolver.ResolveRuntime`.

### 5. Machine-readable endpoint inspection commands

- [x] Add a way to list configured endpoints non-interactively in JSON.
- [x] Add a way to show a single configured endpoint non-interactively in JSON.
- [x] Include enough metadata for Armada to populate or validate captain configuration forms.
- [x] Redact secret-bearing header values while still reporting metadata such as header names.

Notes:
- [x] Progress: `mux endpoint list --output-format json` and `mux endpoint show <name> --output-format json` implemented.
- [x] Notes: values are redacted; tool capability is included.

### 6. Probe-side Armada compatibility validation

- [x] Extend `mux probe` so Armada can verify that a candidate endpoint is suitable for captain work.
- [x] Add `--require-tools`.
- [x] Fail clearly when the selected endpoint disables tools.
- [x] Include tool capability fields that are easy for Armada to consume.

Notes:
- [x] Progress: implemented in `ProbeCommand`.
- [x] Notes: failure uses `errorCode = tools_required` and `failureCategory = capability`.

### 6a. Secure auth/header strategy for Armada-driven endpoint configuration

- [x] Decide the currently supported model.
- [x] Support named authenticated endpoints stored in mux config with env-backed header values.
- [ ] Add a first-class secure ad-hoc auth/header override surface for non-interactive `print` and `probe`.
- [ ] Add non-interactive endpoint import/upsert management if Armada must create named endpoints itself.
- [x] Ensure endpoint inspection redacts secret values.
- [x] Document the current secret-handling model clearly.

Current recommendation:
- use named endpoints for authenticated Armada captains today
- store secrets in environment-variable-backed headers inside `endpoints.json`
- defer raw ad-hoc authenticated CLI overrides until a file-based or similarly safe surface is designed

## Strongly Recommended Work

### 7. Dedicated Armada-facing documentation

- [x] Add dedicated Armada integration documentation.
- [x] Document launch, validation, endpoint inspection, exit codes, tool expectations, and MCP limitations.
- [x] Include Windows/Linux-compatible command shapes.

Notes:
- [x] Progress: `ARMADA.md` added in this pass.

### 8. Armada-style end-to-end tests

- [x] Add an automated test path that mirrors Armada launch and validation.
- [x] Cover config-dir isolation.
- [x] Cover final-message artifact generation.
- [x] Cover JSONL launch parsing.
- [x] Cover probe with `--require-tools`.

Notes:
- [x] Progress: added to `CliContractTests`.

### 9. Non-interactive MCP decision

- [x] Make the product decision explicit: non-interactive MCP is not supported today.
- [x] Document that `print` and `probe` remain built-in-tools-only for the Armada MVP.

Notes:
- [x] Progress: documented in `USAGE.md`, `README.md`, and `ARMADA.md`.

### 10. Tool expectations statement

- [x] Document that Armada expects file read/write/edit, directory traversal, search, and process execution.
- [x] Confirm that built-in mux tools are the baseline capability.

Notes:
- [x] Progress: documented in `ARMADA.md`.

## Optional Work

### 11. Dedicated Armada profile

- [ ] Evaluate whether mux should expose a named Armada compatibility profile.
- [x] Keep the current implementation on top of standard `print` and `probe` unless repeated Armada-specific switches become noisy or fragile.

## Suggested Next Slice

The next high-value implementation slice is:

1. Add a secure non-interactive auth/header surface such as `--headers-file <path>` or endpoint import/upsert commands.
2. Decide whether Armada needs named-endpoint lifecycle management inside mux, or whether external config provisioning is sufficient.
3. Add more contract tests once the secure auth/header surface exists.

## Definition of Done

Mux is ready to be treated as a first-class Armada captain target when all of the following are true:

- [x] Armada can choose a named Mux endpoint or explicit endpoint overrides per captain.
- [x] Armada can validate that endpoint before mission launch.
- [x] Armada can capture a clean final answer from a Mux run without scraping mixed terminal noise.
- [x] Mux publishes a stable, documented automation contract for launch, validation, and endpoint inspection.
- [x] CI contains regression coverage for the Armada-facing surfaces implemented today.
- [ ] Armada has a first-class secure story for authenticated ad-hoc endpoint configuration when named endpoints are not sufficient.
