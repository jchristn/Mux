# mux as an MCP Server Plan

_Status: done (2026-10-08). Row 29 of `archive/NEW_SKILLS_PLAN.md`. Check boxes as work lands. `[ ]` = todo, `[x]` = done, `[~]` = in progress._

Claude Code ships `claude mcp serve` and Codex ships `codex mcp-server`. Both let another agent, an editor, or a script treat the harness as a tool: hand it a task, get an answer back. mux can already do that over its REST server, but REST is not what other agents speak. They speak MCP. Without an MCP entry point, the only way to put mux inside someone else's agent loop is a shell wrapper around `mux print`, which loses cancellation, progress, and any structure in the result.

`mux mcp serve` closes that gap. It runs the same headless agent turn `mux print` runs, behind an MCP `run` tool, and adds a few read-only tools so the caller can see what mux knows: sessions, endpoints, and skills. Skill commands can be exposed too, but only when the operator asks for it.

## Goals

The server has to be safe to point an untrusted model at, and it has to work with the clients people already have. Everything else is secondary. Concretely:

- **Two transports.** stdio by default, because that is what `claude mcp add`, Codex, and mux's own MCP client launch. Streamable HTTP with `--http <port>` for a long-running shared server.
- **One real capability, a few read-only ones.** `run` executes a prompt and returns the final answer plus a compact summary. `list_sessions`, `get_session`, `list_endpoints`, and `list_skills` only read. `run_skill` runs a deterministic skill command and appears only with `--allow-skills`.
- **The operator decides how much a caller may do.** The server's `--approval-policy` (`deny` by default) is a ceiling. A `run` call may ask for a stricter policy, never a looser one, and `ask` is never possible because no human is attached.
- **No secrets leave the process.** Endpoint API keys and headers are never returned, credentials embedded in base URLs are masked, and the HTTP transport can require a bearer key.
- **Progress and cancellation work.** A client that sends a `progressToken` sees one progress notification per agent step, and a cancelled request cancels the agent turn.

## Design decisions

**Voltaic does the protocol.** mux already depends on Voltaic 2.2.1 for its MCP client. The same package ships `McpServer` (stdio) and `McpHttpServer` (Streamable HTTP), both with `RegisterTool`, input-schema validation, `McpToolCallContext.ReportProgressAsync` for progress, `McpToolException` for tool errors the model should read, `AuthenticationHandler` for bearer checks, and `RestrictToLoopbackClients`. No upgrade is needed. The stdio server already moves `Console.Out` to stderr while it runs, so stray writes cannot corrupt the protocol stream.

**Core owns the tools; the CLI owns the agent turn.** The tool definitions and handlers live in `Mux.Core/McpServer`, so a future desktop or tray host can serve them too. The `run` tool depends on `IMcpRunExecutor`, and the CLI implements it with the same runtime resolution `mux print` uses (`CommandRuntimeResolver`), plus skills, hooks, and background processes. Tests substitute a fake executor and exercise every handler without a model.

**One `run` at a time.** Two agent turns writing to the same working tree at once is how files get corrupted. A semaphore serializes `run` calls; read-only tools are never blocked by it. Parallel runs belong to the worktree isolation work (row 28).

**HTTP binds to loopback.** `--host` defaults to `localhost` and loopback-only clients are enforced. Binding elsewhere is allowed, and the docs say plainly that it should come with `--api-key` (or the `mcpServeApiKey` setting).

## Tools

| Tool | Kind | Arguments | Returns |
|---|---|---|---|
| `run` | agent turn | `prompt` (required), `endpoint`, `working_directory`, `approval_policy` (`deny`, `auto-safe`, `auto`; capped by the server), `max_turns` | `answer`, `status`, `endpoint`, `model`, `iterations`, `tool_calls`, `errors`, `duration_ms`, `input_tokens`, `output_tokens` |
| `list_sessions` | read-only | `limit` (default 20, at most 200) | id, title, endpoint, model, message count, working directory, updated time |
| `get_session` | read-only | `id` (required), `max_messages` (default 50, at most 500) | the session's metadata and its last messages (each cut at 8000 characters) |
| `list_endpoints` | read-only | none | name, adapter, model, base URL with credentials masked, default flag; never keys or headers |
| `list_skills` | read-only | `working_directory` | name, description, commands, playbook flag, scope |
| `run_skill` | deterministic command | `name`, `command`, `args`, `working_directory` | the skill's stdout, stderr, and exit code; registered only with `--allow-skills` |

## Client configuration

Claude Code:

```bash
claude mcp add mux -- mux mcp serve --approval-policy auto-safe
```

Codex (`~/.codex/config.toml`):

```toml
[mcp_servers.mux]
command = "mux"
args = ["mcp", "serve", "--approval-policy", "auto-safe"]
```

mux itself (`~/.mux/mcp-servers.json`), for one mux delegating to another, for example a second mux pinned to a different endpoint:

```json
{
  "servers": [
    { "name": "mux-reviewer", "transport": "stdio", "command": "mux", "args": ["mcp", "serve", "--endpoint", "big-model"] }
  ]
}
```

Over HTTP:

```bash
mux mcp serve --http 8811 --api-key "$MUX_MCP_KEY"
# client URL: http://localhost:8811/mcp with "Authorization: Bearer <key>"
```

## Tasks

### Phase A: Core host

- [x] `src/Mux.Core/McpServer/MuxMcpServerOptions.cs`: allow skills, approval ceiling, default endpoint and working directory, config directory, server name.
- [x] `src/Mux.Core/McpServer/IMcpRunExecutor.cs`, `McpRunRequest.cs`, `McpRunResult.cs`: the contract between the `run` tool and whoever executes the turn.
- [x] `src/Mux.Core/McpServer/MuxMcpTools.cs`: tool schemas and handlers (argument validation, approval capping, secret masking, progress, the `run` semaphore).
- [x] `src/Mux.Core/McpServer/MuxMcpServerHost.cs`: registers the tools on Voltaic's `McpServer` (stdio) or `McpHttpServer` (HTTP, loopback, optional bearer check) and runs until cancelled.

### Phase B: CLI

- [x] `src/Mux.Cli/Commands/McpRunExecutor.cs`: the headless turn, resolved like `mux print`, with skills, hooks, and background processes, and nothing written to stdout.
- [x] `src/Mux.Cli/Commands/McpServeCommand.cs`: `mux mcp serve [--http <port>] [--host <name>] [--api-key <key>] [--allow-skills] [--log-messages] [--approval-policy deny|auto-safe|auto] [--yolo] [--endpoint <name>] [-w <dir>]`, dispatched from `Program.cs` without the stdout banner or spacing wrapper.
- [x] Setting `mcpServeApiKey` (used when `--api-key` is not given).

### Phase C: Tests and docs

- [x] `McpServerSuite`: tools/list contents and schemas, `run` through a fake executor (answer, summary, approval capping, refusal of `ask` and of looser policies), progress notifications, cancellation, invalid arguments, sessions (list, get, unknown id), endpoint secrets masked, `run_skill` absent and refused without the flag, HTTP with and without the bearer key, mux's own `McpToolManager` connecting over HTTP, and a stdio child process (`mux mcp serve`) answering `tools/list` and a real `run` against a mock model.
- [x] `docs/USAGE.md` section "mux as an MCP server"; `docs/CONFIG.md` entry for `mcpServeApiKey`; `mux --help` line.

## Deviations

The build stayed on Voltaic 2.2.1; every API the design needed was already there. A few details changed on the way:

- Progress is reported after every tool call as well as after every agent step, which keeps clients with short timeouts alive during long tool runs.
- `run` records durable usage telemetry like `mux print` (the first pass did not; the follow-up added it), and the run summary carries the token counts as well.
- `mux mcp serve` is excluded from the "serve" detection that starts the Prometheus listener for `mux serve`.
- Voltaic 2.2.1's stdio server writes every message it receives and sends to stderr in full, and MCP clients keep that log. `mux mcp serve` wraps stderr with `McpLogFilterWriter`, which shortens those lines to the method, the request id, and the size, so prompts and answers stay out of client logs. `--log-messages` restores the full log for debugging. The HTTP transport only logs through an event mux does not subscribe to.

Tests: `McpServerSuite` (19 cases) passes on net8.0 and net10.0, including the stdio child-process run, mux's own MCP client connecting over HTTP with and without the bearer key, the log filter, a raw stdio session whose stderr keeps the prompt out unless `--log-messages` is passed, and the usage database written by a real `run`.

## Risks

A `run` with `auto` approval is arbitrary code execution on the server's machine. The ceiling defaults to `deny`, the flag has to be passed deliberately, and the docs say so next to the flag. Anyone who points a public HTTP listener at `auto` without an API key has made a choice the tool cannot make for them, but the loopback default and the missing-key warning make it hard to do by accident.

Long turns and client timeouts are the other practical risk. Most MCP clients time a tool call out after a minute or two. Progress notifications keep well-behaved clients waiting, and `max_turns` lets a caller bound the work, but a client with a hard timeout will cancel long runs. Cancellation is honored, so nothing keeps running after the caller gives up.
