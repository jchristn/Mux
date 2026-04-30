<p align="center">
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="assets/icon-white.png">
    <source media="(prefers-color-scheme: light)" srcset="assets/icon-black.png">
    <img src="assets/icon-black.png" width="256" height="256" alt="mux">
  </picture>
</p>

<h1 align="center">mux</h1>

<p align="center">
  <em>Your AI agent, your models, your infrastructure.</em>
</p>

<p align="center">
  <a href="LICENSE.md"><img src="https://img.shields.io/badge/license-MIT-blue.svg" alt="MIT License"></a>
  <a href="https://dotnet.microsoft.com"><img src="https://img.shields.io/badge/.NET-8.0%20%7C%2010.0-purple.svg" alt=".NET 8 / 10"></a>
  <a href="CHANGELOG.md"><img src="https://img.shields.io/badge/version-0.2.0%20ALPHA-orange.svg" alt="v0.2.0 ALPHA"></a>
</p>

> **v0.2.0 ALPHA**
> This is an early alpha. APIs, interfaces, configuration formats, tool schemas, and CLI behavior are all subject to change. Feedback is welcome via [issues](https://github.com/jchristn/Mux/issues) and [discussions](https://github.com/jchristn/Mux/discussions).

## What is mux?

`mux` is a CLI AI agent that gives you a Claude Code / Codex-like experience using the backend and model you choose. It can run against Ollama, OpenAI, vLLM, LM Studio, Azure OpenAI, or any OpenAI-compatible API.

`mux` can read and write files, run commands, search code, and manage a project through either:
- an interactive REPL
- a single-shot non-interactive command surface

`mux` does not install or manage model runners. You bring your own local or remote inference backend, and `mux` connects to it.

## Highlights

- Backend-agnostic: one CLI for local and remote model runners
- Built-in tools: file edit/read/write/delete, directory management, glob, grep, process execution
- Shell-aware process execution metadata: `run_process` tells the model which OS and shell it will run under
- MCP extensible in interactive mode: external tool servers appear beside built-in tools
- Interactive queueing: keep typing while mux is busy, queue follow-up prompts with `Tab`, and edit the newest queued prompt with `Alt+Up`
- Inline interactive status: when mux is busy, paused, or awaiting approval, it shows a live status line above the prompt instead of pinning a footer to the bottom of the terminal
- Structured automation support: `mux print --output-format jsonl` emits one machine-readable event per line
- Config isolation: set `MUX_CONFIG_DIR` to run with a fully isolated config directory
- Health checks: `mux probe` validates config, backend reachability, auth, and model access

## Quick Start

Prerequisites:
- .NET 8 SDK or later
- A model runner installed and running separately

Example with Ollama:

```bash
ollama pull qwen2.5-coder:7b
ollama serve
```

Install `mux`:

```bash
git clone https://github.com/jchristn/Mux.git
cd Mux

# Windows
install-tool.bat

# Linux / macOS
chmod +x install-tool.sh
./install-tool.sh
```

Run it:

```bash
mux
```

On first run, `mux` creates `~/.mux/endpoints.json` with a default local Ollama endpoint. If you want an isolated config instead, set `MUX_CONFIG_DIR` before first launch.

See [GETTING_STARTED.md](GETTING_STARTED.md) for the full walkthrough.

## Verify It Works

After install, try this prompt to confirm the model and tools are working end to end:

```text
mux> create a file called hello.py that prints "hello world", then read it back to verify. if the file already exists, overwrite it. when finished, delete the file.
```

You should see `write_file` and `read_file` tool calls, the file created on disk, and the contents read back.

## CLI Usage

```text
mux [prompt]                         Interactive REPL (default)
mux [OPTIONS] [prompt]               Interactive with overrides
mux --print [OPTIONS] <prompt>       Single-shot mode
echo "prompt" | mux --print          Read prompt from stdin
mux probe [OPTIONS]                  Validate config and backend access
```

Use `mux print` as the preferred non-interactive entrypoint in scripts and automation. `--print` remains supported and is convenient for stdin piping.

### Options

| Option | Short / Alias | Description |
|---|---|---|
| `--help` | `-h`, `/?` | Show help and exit |
| `--version` | `/version` | Show version and exit; bare `mux -v` also prints the version |
| `--print` | `-p` | Single-shot mode |
| `--endpoint <name>` | `-e` | Use a named endpoint |
| `--model <name>` | `-m` | Override model |
| `--base-url <url>` |  | Override base URL |
| `--adapter-type <type>` |  | `ollama`, `openai`, `vllm`, `openai-compatible` |
| `--temperature <float>` |  | Override temperature |
| `--max-tokens <int>` |  | Override max output tokens |
| `--compaction-strategy <mode>` |  | Override compaction strategy: `summary` or `trim` |
| `--working-directory <path>` | `-w` | Tool execution directory |
| `--system-prompt <path>` |  | Override system prompt file |
| `--yolo` |  | Auto-approve tool calls |
| `--approval-policy <policy>` |  | interactive: `ask`, `auto`, or `deny`; print/probe: `auto` or `deny` |
| `--output-format <format>` |  | `text`, `json`, or `jsonl` depending on the command |
| `--no-mcp` |  | Interactive only: skip MCP server initialization |
| `--verbose` | `-v` | Extra progress to stderr in text mode |

### Interactive Commands

```text
/endpoint                         List configured endpoints
/endpoint list                    Alias for /endpoint
/endpoint <name>                  Switch to a named endpoint
/endpoint show <name>             Show endpoint details and probe connectivity
/endpoint add                     Start the guided endpoint creation wizard
/endpoint edit <name>             Start the guided endpoint edit wizard
/endpoint remove <name>           Remove an endpoint from endpoints.json after confirmation
/tools                            List available tools
/status                           Show session metadata, title, queue state, and estimated context usage
/context                          Alias for /status
/compact                          Compact older conversation history with the configured strategy
/compact summary                  Compact older conversation history with a one-off summary pass
/compact trim                     Trim older conversation history without asking the model to summarize it
/compact strategy [summary|trim]  Show or set the session compaction strategy
/title                            Show the current conversation title
/title <text>                     Set the conversation title and disable automatic retitling
/queue                            List queued prompts and whether dispatch is paused
/queue clear                      Clear all queued prompts
/queue drop-last                  Remove the newest queued prompt
/queue resume                     Resume automatic queue dispatch
/mcp list                         Show MCP server status
/mcp add <name> <cmd> [args...]   Add an MCP server at runtime
/mcp remove <name>                Remove an MCP server
/system                           Show the full current system prompt
/system <text>                    Replace the system prompt for this session
/clear                            Clear conversation history
/help or /?                       Show command help
/exit                             Quit mux
```

In interactive mode, `Up` and `Down` recall prompts submitted earlier in the current session.

Endpoint management happens directly against `endpoints.json`. `show` performs a lightweight probe of the configured endpoint. `add` and `edit` run guided workflows that prompt for the adapter, base URL, model, auth mode (`none`, `bearer token`, or `custom headers`), default status, and optional advanced settings before probing and saving.

For secret values, the wizard lets you either store the value directly in `endpoints.json` or store an environment-variable reference. It accepts a bare variable name plus `${VAR}`, `%VAR%`, `$VAR`, and `$env:VAR`, then stores environment references canonically as `${VAR}`. For `ollama`, mux uses Ollama's OpenAI-compatible API root, so the usual base URL is `http://localhost:11434/v1`. `remove` asks for confirmation and still refuses to delete the endpoint active in the current session.

### Interactive Input

Interactive mode keeps the prompt live while mux is generating. When mux is busy, paused, or awaiting approval, it renders a status line directly above the prompt.
Streamed responses preserve exactly one empty line before the next `mux>` prompt, including when output reaches the bottom edge of the terminal and forces a scroll.

Each interactive session also maintains a short conversation title. By default mux asks the current model to revisit that title periodically as the discussion evolves. If you set a title manually with `/title <text>`, mux keeps that title fixed until you change it again.

While mux is generating, you can keep drafting the next prompt:

- `Tab` queues the current draft to run after the active completion
- `Alt+Up` loads the newest queued prompt back into the editor
- `Esc` cancels the active generation and pauses automatic queue dispatch
- `/queue resume` resumes queued execution after a cancellation or failure pause

Slash commands are session controls and are not queueable.

`/status` reports the active title, model, endpoint, queue state, compaction policy, and estimated context budget; `/context` is an alias. New prompts are checked against that budget before each run. When a prompt would exceed the usable context budget, mux automatically compacts older persisted history before sending the next model call. If an active tool-using run grows too large mid-flight, mux now honors the configured compaction strategy there too: `summary` uses a summary sidecar pass first and trims only if needed, while `trim` stays trim-only. mux also emits a dim post-turn context line when the session is approaching the usable limit, but it does not keep a persistent meter on screen. `/compact` uses the configured compaction strategy, `/compact summary` and `/compact trim` provide one-off overrides, and `/compact strategy [summary|trim]` changes the interactive session policy without touching `settings.json`. `/clear` clears the transcript state and redraws the screen with the current title at the top.

### Interactive Examples

```text
mux> read README.md and suggest improvements
mux> refactor the UserService class to be async
mux> run the tests and fix failures
```

### Single-Shot Examples

```bash
mux print --yolo "read README.md and summarize it"
mux print --yolo --endpoint openai-gpt4o "explain this repository"
echo "refactor AuthService" | mux --print --yolo
```

## Automation Contract

Use `mux print` as the non-interactive entrypoint:

```bash
mux print --output-format jsonl --yolo "implement the feature described in TASK.md"
```

In `jsonl` mode:
- all structured events are written to `stdout`
- each line is a complete JSON object
- default human-readable progress output is suppressed
- every event includes `contractVersion`
- `run_started` includes effective non-interactive capability metadata such as `commandName`, `endpointSelectionSource`, `cliOverridesApplied`, built-in tool counts, and MCP support/config status
- `run_started` also includes context metadata such as `contextWindow`, `reservedOutputTokens`, `usableInputLimit`, `warningThresholdTokens`, `tokenEstimationRatio`, and `compactionStrategy`
- `run_completed` also includes `finalEstimatedTokens` and `compactionCount`
- `error` events keep `code` and also expose `errorCode`, `failureCategory`, and resolved runtime metadata when known

Event types currently emitted:
- `run_started`
- `assistant_text`
- `tool_call_proposed`
- `tool_call_approved`
- `tool_call_completed`
- `heartbeat`
- `context_status`
- `context_compacted`
- `error`
- `run_completed`

Default `text` mode for `mux print` remains:
- `stdout`: assistant text
- `stderr`: progress, denial notices, and errors

Exit codes:
- `0`: success
- `1`: config, runtime, backend, or command failure
- `2`: tool call denied

Non-interactive constraints:
- `mux print` and `mux probe` do not load MCP servers
- `--no-mcp` is interactive-only and is rejected in `print` and `probe`
- `--approval-policy ask` is rejected in `print` and `probe`; use `auto` or `--yolo`, or `deny`

## Probe Command

`mux probe` uses the same config resolution path as `mux print` and performs a lightweight backend validation.

Examples:

```bash
mux probe
mux probe --output-format json
mux probe -e openai-gpt4o
```

`probe` verifies:
- endpoint selection and config loading
- backend reachability
- auth/header configuration
- model access through a minimal completion request

Machine-readable `probe` output also includes:
- `contractVersion` for explicit parser compatibility
- effective config/runtime metadata such as `configDirectory`, `endpointSelectionSource`, and `cliOverridesApplied`
- capability data such as `toolsEnabled`, built-in tool counts, and MCP support/config state
- classified failures via `errorCode` and `failureCategory`

Probe-specific option:
- `--probe-prompt <text>` overrides the default confirmation prompt used during backend validation

## Configuration

Default config directory:

```text
~/.mux/
```

Override it for isolated or concurrent runs:

```bash
# Bash
export MUX_CONFIG_DIR=/tmp/mux-run-1

# PowerShell
$env:MUX_CONFIG_DIR = "C:\\temp\\mux-run-1"
```

Main files:
- `endpoints.json`
- `mcp-servers.json`
- `settings.json`
- `system-prompt.md`

See [CONFIG.md](CONFIG.md) for the full reference.

## Documentation

- [GETTING_STARTED.md](GETTING_STARTED.md)
- [USAGE.md](USAGE.md)
- [CONFIG.md](CONFIG.md)
- [TESTING.md](TESTING.md)
- [CHANGELOG.md](CHANGELOG.md)

## License

[MIT](LICENSE.md)
