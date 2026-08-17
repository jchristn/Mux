# mux-sdk (Python)

A thin Python driver for the [mux](../../README.md) CLI. It does not reimplement the agent — it spawns
`mux print --output-format jsonl`, parses the newline-delimited event stream, and hands you the events plus
an aggregated result. Because it wraps the CLI and its `contractVersion`-versioned JSONL contract, the SDK
stays backend-agnostic and in lockstep with mux itself.

> Alpha, tracking mux `v0.7.0`. The API may change alongside the CLI.

## Requirements

- Python 3.8+
- The `mux` CLI installed and on `PATH` (or reachable via `mux_path` / `mux_args`)

## Install

```bash
pip install mux-sdk
```

Or from this repo:

```bash
pip install ./sdk/python
```

## Quick start

Run a prompt to completion:

```python
from mux_sdk import Mux, MuxOptions

mux = Mux(MuxOptions(yolo=True))
result = mux.run("summarize README.md")

print(result.text)       # the assistant's answer
print(result.status)     # "completed"
print(result.exit_code)  # 0
```

Stream events as they arrive:

```python
for event in mux.run_streamed("refactor AuthService"):
    if event["eventType"] == "assistant_text":
        print(event["text"], end="")
    elif event["eventType"] == "tool_call_completed":
        print(f'\n[tool] {event["toolName"]}')
```

Hold a multi-turn conversation (threaded through a mux session, so turn N sees turns 1..N-1):

```python
thread = mux.start_thread()
thread.run("start a security review")
follow_up = thread.run("now scan the dependencies")
print(follow_up.text)

# Later, in another process, resume by id:
resumed = mux.resume_thread(thread.id)
resumed.run("summarize what you found")
```

Constrain, confine, and govern a run the same way the CLI does:

```python
mux = Mux(MuxOptions(
    yolo=True,
    sandbox="workspace-write",
    add_dir=["../shared"],
    deny_tools=["run_process"],
    max_turns=8,
    max_token_budget=200_000,
))
```

## Pointing at the binary

By default the SDK spawns `mux` from `PATH`. Override it when mux is elsewhere or launched indirectly:

```python
Mux(MuxOptions(mux_path="/usr/local/bin/mux"))
Mux(MuxOptions(mux_path="dotnet", mux_args=["/path/to/Mux.Cli.dll"]))
```

## API

### `MuxOptions`

A dataclass whose fields map to `mux print` flags (all optional): `mux_path`, `mux_args`, `endpoint`,
`model`, `base_url`, `adapter_type`, `config_dir`, `working_directory`, `temperature`, `max_tokens`,
`max_turns`, `max_token_budget`, `system_prompt`, `append_system_prompt`, `yolo`, `approval_policy`
(`"auto"`/`"deny"`), `sandbox` (`"none"`/`"read-only"`/`"workspace-write"`), `allow_tools`, `deny_tools`,
`add_dir`, `mcp_config`, `strict_mcp_config`, `ignore_cert_errors`, `env`.

### `Mux(options=None)`

- `run(prompt, *, session_id=None, **overrides) -> RunResult` — run to completion and return the
  aggregated result. `overrides` are `MuxOptions` field names applied over the base options for this call.
- `run_streamed(prompt, *, session_id=None, **overrides) -> Iterator[dict]` — yield each event as it
  arrives.
- `start_thread(session_id=None) -> Thread` / `resume_thread(session_id) -> Thread`.

### `RunResult`

A dataclass: `text`, `session_id`, `status`, `exit_code`, `iterations_completed`, `tool_call_count`,
`error_count`, `duration_ms`, `final_estimated_tokens`, `input_tokens`, `output_tokens`, `total_tokens`,
`stderr`, and the ordered `events` list (each a `dict` with an `eventType`). The token fields come from the
`run_completed` event's `usage` block (provider-reported; `0` when the backend reports none).

### `Thread`

`run(prompt, **overrides)` / `run_streamed(prompt, **overrides)` — each turn runs under the thread's mux
session id, so history accumulates across turns.

### `build_print_args(options, session_id, prompt) -> list[str]`

Returns the exact `mux print` argv the SDK would spawn — handy for debugging or logging.

## Events and exit codes

Events are dicts discriminated by `eventType`: `run_started`, `assistant_text`, `tool_call_proposed`,
`tool_call_approved`, `tool_call_completed`, `error`, `heartbeat`, `run_completed` (plus any future types
in a known contract version). `RunResult.exit_code` mirrors the CLI: `0` success, `1` error, `2` tool call
denied. Governance and schema refusals surface as `error` events (codes like `tool_call_denied`,
`budget_exceeded`, `schema_validation_failed`) with the corresponding non-zero exit code.

## Development

```bash
python -m unittest discover -s tests
```

The suite spawns a stand-in `mux` (`tests/fake_mux.py`) that emits a canned event stream and records its
argv, so it verifies argument construction, event parsing, result aggregation, streaming, exit-code
handling, and thread session continuity without a real model or backend.

## License

[MIT](../../LICENSE.md)
