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
  <a href="CHANGELOG.md"><img src="https://img.shields.io/badge/version-0.3.0-blue.svg" alt="v0.3.0"></a>
  <a href="CHANGELOG.md"><img src="https://img.shields.io/badge/status-alpha-orange.svg" alt="alpha"></a>
</p>

> **Alpha software**
> mux is in alpha. APIs, interfaces, configuration formats, tool schemas, and CLI behavior are all subject to change. Feedback is welcome via [issues](https://github.com/jchristn/Mux/issues) and [discussions](https://github.com/jchristn/Mux/discussions).

## What is mux?

`mux` is a CLI AI agent that gives you a Claude Code / Codex-like experience using the backend and model you choose. It can run against Ollama, OpenAI, vLLM, LM Studio, Azure OpenAI, or any OpenAI-compatible API.

`mux` can read and write files, run commands, search code, and manage a project through either:
- an interactive REPL
- a single-shot non-interactive command surface

`mux` does not install or manage model runners. You bring your own local or remote inference backend, and `mux` connects to it.

## Highlights

- Backend-agnostic: one CLI for local and remote model runners
- Built-in tools: file edit/read/write/delete, directory management, glob, grep, process execution, and rendered web retrieval
- External web search: optional Tavily and You.com providers expose `web_search` for result discovery
- Shell-aware process execution metadata: `run_process` tells the model which OS and shell it will run under
- MCP tool servers: define `stdio`/HTTP servers in `mcp-servers.json` (execution inside the new TUIKit interactive UI is not yet wired in v0.3.0 — see CHANGELOG)
- TUIKit interactive UI (`v0.3.0`): a full-screen shell with per-job transcripts, a job sidebar, a multi-line composer, slash commands / key bindings / menu over one command catalog, an interactive tool-approval modal, and autosaved resumable sessions. Multiple prompts run as concurrent background jobs (a single-writer lease serializes file edits); enqueue-while-busy lets you start a new job or append to the focused one. See `USAGE.md`.
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
install-tool.bat net8.0

# Linux / macOS
chmod +x install-tool.sh
./install-tool.sh
./install-tool.sh net8.0
```

The install scripts accept an optional target framework argument. They default to `net10.0` when a .NET 10 SDK is installed and otherwise fall back to `net8.0`.

Run it:

```bash
mux
```

On first run, `mux` creates `~/.mux/endpoints.json` with a default local Ollama endpoint and `~/.mux/settings.json` with editable defaults. If you want an isolated config instead, set `MUX_CONFIG_DIR` before first launch.

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
mux endpoint <subcommand> [OPTIONS]  Inspect configured endpoints
```

Use `mux print` as the preferred non-interactive entrypoint in scripts and automation. `--print` remains supported and is convenient for stdin piping. Use `mux endpoint list`/`ls`/`show` when automation needs to inspect stored endpoint configuration without entering the REPL.

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
| `--config-dir <path>` |  | Override the active config directory |
| `--working-directory <path>` | `-w` | Tool execution directory |
| `--system-prompt <path>` |  | Override system prompt file |
| `--output-last-message <path>` |  | Write only the final assistant response text to a file |
| `--yolo` |  | Auto-approve tool calls |
| `--approval-policy <policy>` |  | interactive: `ask`, `auto`, or `deny`; print/probe: `auto` or `deny` |
| `--output-format <format>` |  | `text`, `json`, or `jsonl` depending on the command |
| `--no-mcp` |  | Interactive only: skip MCP server initialization |
| `--ignore-cert-errors` | `--insecure` | Disable TLS certificate validation for mux-owned network requests |
| `--verbose` | `-v` | Extra progress to stderr in text mode |

### Interactive Commands

Every command is also reachable by key binding and the `F1` menu (one catalog, three surfaces).

```text
/endpoint, /model                 Open the endpoints / models picker (also Ctrl+E)
/endpoints, /models               Aliases for /endpoint
/sessions                         Browse and resume saved sessions
/save                             Save the current session
/theme                            Open the theme selector
/sidebar                          Toggle the sidebar
/mouse                            Toggle mouse capture (on by default)
/menu                             Open the command menu (also F1)
/clear                            Clear the transcript
/help, /?                         Show the keybinding / command reference
/exit, /quit, /q                  Quit mux
```

The `/endpoint` picker (also `Ctrl+E`) lists your configured endpoints; pick one to switch the active endpoint for subsequent prompts. The same modal offers **Add**, **Edit**, and **Remove** entries: **Add** and **Edit** run a guided form (adapter, base URL, model, auth mode — `none`, `bearer token`, or `custom headers` — default status, endpoint-scoped tool auto-approval, and optional advanced settings) and probe before saving; **Remove** asks for confirmation and refuses to delete the endpoint active in the current session. All three persist to `endpoints.json`. Endpoints can persist `autoApproveTools: true` so tool calls auto-approve whenever that endpoint is active unless CLI approval flags override it, and can set `maxAgentIterations` (leave it `null` to inherit the global `settings.json` default).

For secret values, the form lets you either store the value directly in `endpoints.json` or store an environment-variable reference. It accepts a bare variable name plus `${VAR}`, `%VAR%`, `$VAR`, and `$env:VAR`, then stores environment references canonically as `${VAR}`. For `ollama`, mux uses Ollama's native API root, so the usual base URL is `http://localhost:11434` (no `/v1`); a trailing `/v1` is tolerated and stripped for this adapter.

External search is configured in `settings.json` (Tavily or You.com); when at least one enabled provider is fully configured the `web_search` tool is enabled. `web_search` discovers candidate results; fetching the contents of a known URL is handled by `web_retrieve`. MCP servers are configured in `mcp-servers.json`.

### Interactive Input

Interactive mode works like a chat client. Prompt entry supports multi-line editing and paste. After you press `Enter` the prompt runs; you can keep typing and submitting while a turn is in flight, and those prompts queue to run in order as each turn finishes. The sidebar shows the current status and how many prompts are queued.

Each interactive session also maintains a short conversation title. By default mux asks the current model to revisit that title periodically as the discussion evolves. If you set a title manually with `/title <text>`, mux keeps that title fixed until you change it again.

While idle, `Up` and `Down` recall submitted prompts from the current session. `Shift+Enter` and `Ctrl+Enter` insert a newline so you can compose or paste multi-line prompts before submission.

`Esc` cancels the running turn. If a tool approval is needed, mux shows an approval modal — **Approve once**, **Deny**, or **Always this session** (which auto-approves the rest of the session).

New prompts are checked against the context budget before each run. When a prompt would exceed the usable context budget, mux automatically compacts older persisted history before sending the next model call, using the configured compaction strategy (`summary` uses a summary sidecar pass first and trims only if needed; `trim` stays trim-only). `/clear` clears the transcript.

### Interactive Examples

```text
mux> read README.md and suggest improvements
mux> refactor the UserService class to be async
mux> run the tests and fix failures
mux> retrieve https://example.com and summarize the returned text
mux> search the web for recent mux release notes, then retrieve the most relevant result
```

### Web Search And Retrieval

`mux` exposes two separate web tools when tool calling is enabled for the selected endpoint:

- `web_retrieve` fetches a specific HTTP or HTTPS URL with a headless Playwright browser and returns rendered text, title, final URL, status, content type, and optional HTML. It supports Chromium by default and Firefox by request. If the requested Playwright browser is not installed, mux asks Playwright to install it on demand.
- `web_search` searches the public web through configured external providers and returns structured candidate results with URLs and snippets. Tavily and You.com are supported. The tool is exposed only when external search is enabled and at least one enabled provider is fully configured.

Use `web_search` to discover what exists. Use `web_retrieve` to inspect a selected URL or any URL you already know.

Enterprise TLS inspection can cause errors such as `SELF_SIGNED_CERT_IN_CHAIN` while mux connects to LLM endpoints, calls external search providers, retrieves web pages, or downloads Playwright browsers. Use `--ignore-cert-errors` or its `--insecure` alias to disable TLS certificate validation for mux-owned network requests for that run. The same behavior can be enabled with `settings.json` field `ignoreCertErrors: true` or environment variable `MUX_IGNORE_CERT_ERRORS=1`. mux prints a warning when this mode is active. This does not change TLS behavior for commands launched through `run_process` or for external processes started by MCP servers.

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

If you need a clean final-response artifact for an orchestrator, add:

```bash
mux print --output-format jsonl --output-last-message result.txt --yolo "implement the feature described in TASK.md"
```

`result.txt` contains only the final assistant response text. If the run fails, mux does not create the file.

In `jsonl` mode:
- all structured events are written to `stdout`
- each line is a complete JSON object
- default human-readable progress output is suppressed
- every event includes `contractVersion`
- `run_started` includes effective non-interactive capability metadata such as `commandName`, `endpointSelectionSource`, `cliOverridesApplied`, built-in tool counts, and MCP support/config status
- `run_started` also includes loop/context metadata such as `maxIterations`, `contextWindow`, `reservedOutputTokens`, `usableInputLimit`, `warningThresholdTokens`, `tokenEstimationRatio`, and `compactionStrategy`
- `run_started` includes `ignoreCertErrors` so consumers can detect whether certificate validation was disabled for mux-owned network requests
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
mux probe --output-format json --require-tools
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
- `--require-tools` fails when the selected endpoint disables tool calling

## Endpoint Command

`mux endpoint` exposes stored endpoint configuration through a non-interactive CLI surface.

Examples:

```bash
mux endpoint list
mux endpoint ls
mux endpoint list --output-format json
mux endpoint ls --output-format json
mux endpoint show openai-gpt4o
mux endpoint show openai-gpt4o --output-format json
```

`endpoint list` and `endpoint ls` return configured endpoint names. `endpoint show` returns one configured endpoint with secret-like header values redacted, tool-calling capability, and persisted tool-approval mode. Use `--config-dir` when you need to inspect an isolated config directory.

## Configuration

Default config directory:

```text
~/.mux/
```

Override it for isolated or concurrent runs:

```bash
# Bash
export MUX_CONFIG_DIR=/tmp/mux-run-1
export MUX_IGNORE_CERT_ERRORS=1

# PowerShell
$env:MUX_CONFIG_DIR = "C:\\temp\\mux-run-1"
$env:MUX_IGNORE_CERT_ERRORS = "1"
```

Or pass a first-class CLI override:

```bash
mux print --config-dir /tmp/mux-run-1 --output-format jsonl --yolo "run the task"
mux print --ignore-cert-errors --output-format jsonl --yolo "run behind enterprise TLS inspection"
```

When both are set, `--config-dir` wins over `MUX_CONFIG_DIR`.

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
- [ARMADA.md](ARMADA.md)
- [TESTING.md](TESTING.md)
- [CHANGELOG.md](CHANGELOG.md)

## License

[MIT](LICENSE.md)
