# Armada Integration Guide

Use `mux` as a non-interactive runtime through `print`, validate captains through `probe`, and inspect saved endpoints through `endpoint`.

## Launch Contract

Recommended command shape:

```bash
mux print --config-dir /tmp/mux-job-123 --output-format jsonl --output-last-message result.txt --yolo --endpoint captain-prod --working-directory /workspace "implement the task"
```

Notes:
- `--output-last-message <path>` writes only the final assistant response text
- if the run fails, mux does not create the artifact file
- `--output-format jsonl` is the recommended telemetry surface
- `--yolo` or `--approval-policy auto` is required when automatic tool execution is intended

## Validation Contract

Recommended validation command:

```bash
mux probe --config-dir /tmp/mux-job-123 --output-format json --require-tools --endpoint captain-prod
```

`--require-tools` makes probe fail if the selected endpoint disables tool calling. Armada should use this when validating captains meant to read files, edit files, search, and run processes.

## Endpoint Contract

Endpoint selection and overrides follow this order:

1. `--endpoint <name>` if provided
2. the endpoint marked `isDefault: true`
3. the first configured endpoint
4. the built-in local Ollama fallback

After endpoint selection, CLI overrides such as `--model`, `--base-url`, `--adapter-type`, `--temperature`, and `--max-tokens` are applied.

## Config Directory Contract

Config directory precedence is:

1. `--config-dir <path>`
2. `MUX_CONFIG_DIR`
3. `~/.mux/`

Both `print` and `probe` report the effective `configDirectory` in machine-readable output.

## Endpoint Inspection

List configured endpoints:

```bash
mux endpoint list --config-dir /tmp/mux-job-123 --output-format json
```

Show a single configured endpoint:

```bash
mux endpoint show captain-prod --config-dir /tmp/mux-job-123 --output-format json
```

Inspection output redacts header values but still reports header names and tool capability.

## Tool and MCP Expectations

Armada should assume the built-in mux tool set as the baseline:
- file read/write/edit
- directory traversal
- search
- process execution

Non-interactive MCP support is not available today. `mux print` and `mux probe` do not load MCP servers.

## Auth Guidance

Today, the safest supported model for authenticated Armada captains is:
- store named endpoints in the active mux config directory
- store header values directly or as environment-variable references in `endpoints.json`
- prefer environment-variable references for secrets

Ad-hoc authenticated header overrides are not yet a first-class CLI surface.

## Exit Codes

`mux print`:
- `0`: success
- `1`: config, runtime, backend, or command failure
- `2`: tool call denied

`mux probe`:
- `0`: success
- `1`: failure

## Compatibility Rules

- `contractVersion` is the parser compatibility gate for `print` JSONL and `probe` JSON
- additive fields are non-breaking within a contract version
- consumers should ignore unknown fields in a known contract version
