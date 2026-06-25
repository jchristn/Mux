# mux Usage Guide

This file focuses on practical usage patterns, backend examples, and orchestration scenarios.

## Common Command Patterns

Interactive:

```bash
mux
mux --endpoint ollama-qwen32
mux --model codellama:34b
```

Single-shot:

```bash
mux print --yolo "add error handling to ParseConfig"
mux print --yolo --endpoint openai-gpt4o "explain the architecture"
mux print --output-last-message result.txt --yolo --endpoint openai-gpt4o "explain the architecture"
echo "refactor AuthService" | mux --print --yolo
```

Structured automation:

```bash
mux print --output-format jsonl --yolo "implement the feature described in TASK.md"
```

Health checks:

```bash
mux probe
mux probe --output-format json
mux probe -e vllm-deepseek
mux probe --output-format json --require-tools -e vllm-deepseek
```

Endpoint inspection:

```bash
mux endpoint list --output-format json
mux endpoint ls --output-format json
mux endpoint show openai-prod --output-format json
```

Interactive endpoint management:

```bash
/endpoint
/model
/endpoint ls
/model ls
/endpoint show openai-prod
/model show openai-prod
/endpoint add
/model add
/endpoint edit openai-prod
/model edit openai-prod
/endpoint remove old-endpoint
/endpoint delete old-endpoint
/endpoint rm old-endpoint
/model remove old-endpoint
/model delete old-endpoint
/model rm old-endpoint
```

External search management:

```bash
/search
/search ls
/search add
/search show tavily-primary
/search edit tavily-primary
/search remove tavily-primary
/search delete tavily-primary
/search rm tavily-primary
```

Notes:
- `/endpoint`, `/endpoint list`, `/endpoint ls`, `/model`, `/model list`, and `/model ls` show the configured endpoints and highlight the active session endpoint
- `/model` is an alias for `/endpoint` and supports the same `<name>`, `show`, `add`, `edit`, and `remove`/`delete`/`rm` forms
- `/endpoint show <name>` runs a lightweight connectivity probe and reports whether the endpoint is reachable
- `/endpoint add` launches a guided creation wizard that prompts for the adapter, base URL, model, auth mode, default status, endpoint-scoped tool auto-approval, and optional advanced settings before probing and saving
- `/endpoint edit <name>` launches the same guided workflow for an existing endpoint; editing the active endpoint clears the current conversation state after the update is saved
- Endpoint configs can persist `autoApproveTools: true` so tool calls auto-approve whenever that endpoint is active unless CLI approval flags override it
- Auth modes are `none`, `bearer token`, and `custom headers`; for auth values you can store either a discrete value in `endpoints.json` or an environment-variable reference
- The wizard accepts bare environment variable names plus `${VAR}`, `%VAR%`, `$VAR`, and `$env:VAR`, then stores environment references canonically as `${VAR}`
- `/endpoint remove <name>`, `/endpoint delete <name>`, and `/endpoint rm <name>` ask for confirmation and refuse to remove the endpoint currently active in the session; switch first if you need to delete it
- `/search` and `/search list` show global external-search status and configured providers
- `/search ls` is an alias for `/search list`
- `/search add [name]` configures Tavily or You.com and enables `web_search` when the provider is usable
- `/search show`, `/search edit`, and `/search remove`/`delete`/`rm` inspect and maintain stored search providers

## Built-In Process Execution

The built-in `run_process` tool executes commands using the host shell for the current operating system:
- Windows: `cmd.exe /c`
- Linux and macOS: `/bin/sh -c`

`run_process` now exposes runtime metadata in its tool description and schema so the model can see:
- the operating system
- the platform family
- the shell program
- the shell invocation form

This matters for command generation. For example, a Windows runtime should use `dir`/`type`/`copy` style commands, while a Unix runtime should use `ls`/`cat`/`cp`.

## Web Search And Retrieval

Mux has two distinct web-facing tools:

| Tool | Purpose | Configuration |
|---|---|---|
| `web_retrieve` | Fetch a known HTTP or HTTPS URL and return rendered page data | Always available when built-in tools are enabled for the selected endpoint |
| `web_search` | Discover public web results and return URLs/snippets | Requires external search to be enabled with a configured Tavily or You.com provider |

`web_retrieve` runs in a headless Playwright browser. Chromium is the default browser and Firefox is also supported. If the requested browser binary is missing at runtime, mux invokes Playwright's installer on demand.

`web_search` is provider-backed discovery. It does not fetch arbitrary local URLs such as `http://localhost:11434`; use `web_retrieve` when you already have a URL and want its contents.

Example prompts:

```text
mux> retrieve https://example.com and display the returned title and text
mux> search the web for mux GitHub releases, then retrieve the most relevant result
```

## Output Formats

`mux print` supports:
- `text` (default): assistant text on stdout, progress and errors on stderr
- `jsonl`: one structured event per stdout line

`mux print --output-last-message <path>` optionally writes only the final assistant response text to a file. If the run fails, mux does not create the file.

`mux probe` supports:
- `text` (default)
- `json`

## Structured JSONL Contract

`mux print --output-format jsonl` emits newline-delimited JSON with stable top-level fields such as:
- `contractVersion`
- `eventType`
- `timestampUtc`

Depending on the event, additional fields may include:
- `runId`
- `endpointName`
- `adapterType`
- `baseUrl`
- `model`
- `approvalPolicy`
- `commandName`
- `workingDirectory`
- `configDirectory`
- `endpointSelectionSource`
- `cliOverridesApplied`
- `toolCall`
- `toolCallId`
- `toolName`
- `result`
- `code`
- `errorCode`
- `failureCategory`
- `message`
- `status`
- `durationMs`
- `contextWindow`
- `reservedOutputTokens`
- `usableInputLimit`
- `warningThresholdTokens`
- `tokenEstimationRatio`
- `estimatedTokens`
- `remainingTokens`
- `remainingPercent`
- `messageCount`
- `trigger`
- `warningLevel`
- `scope`
- `mode`
- `strategy`
- `messagesBefore`
- `messagesAfter`
- `estimatedTokensBefore`
- `estimatedTokensAfter`
- `summaryCreated`
- `reason`
- `finalEstimatedTokens`
- `compactionCount`
- `builtInToolCount`
- `effectiveToolCount`
- `mcp`

Current event types:
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

Example:

```bash
mux print --output-format jsonl --yolo "read README.md"
```

Example JSONL lines:

```json
{"contractVersion":1,"eventType":"run_started","timestampUtc":"2026-03-31T20:00:00Z","runId":"...","endpointName":"ollama-local","model":"qwen2.5-coder:7b"}
{"contractVersion":1,"eventType":"assistant_text","timestampUtc":"2026-03-31T20:00:01Z","text":"Here is the summary..."}
{"contractVersion":1,"eventType":"run_completed","timestampUtc":"2026-03-31T20:00:02Z","runId":"...","status":"completed","durationMs":1042}
```

Notes:
- machine-readable output is on `stdout`
- secret-like values in structured payloads are redacted on a best-effort basis
- default text mode is unchanged
- `run_started.mcp.supported` is always `false` in `print` mode today because non-interactive mode does not load MCP servers
- `run_started` now includes context-budget metadata, and `run_completed` includes `finalEstimatedTokens` plus `compactionCount`
- `context_status` and `context_compacted` are additive event types within `contractVersion = 1`; consumers should ignore unknown event types in a known contract version
- `error` events retain `code` for backward compatibility and also expose `errorCode` plus `failureCategory`
- `contractVersion` is shared across `print` JSONL events and `probe` JSON payloads

## Exit Codes

`mux print`:
- `0`: success
- `1`: config, runtime, backend, or command failure
- `2`: tool call denied

`mux probe`:
- `0`: probe succeeded
- `1`: probe failed

## Approval Policy

Policies:

| Flag | Behavior |
|---|---|
| default interactive | ask before each tool call |
| `--yolo` | auto-approve all tool calls |
| `--approval-policy ask` | explicit ask mode |
| `--approval-policy auto` | explicit auto-approve mode |
| `--approval-policy deny` | deny all tool calls |

Notes:
- `mux print` defaults to `deny` unless `--yolo`, `--approval-policy`, or the selected endpoint's `autoApproveTools` setting overrides it
- interactive mode typically uses ask semantics
- interactive mode also honors endpoint-scoped `autoApproveTools` unless CLI approval flags override it
- `mux print` and `mux probe` reject `--approval-policy ask`

## Config Isolation

Use `--config-dir` or `MUX_CONFIG_DIR` when running under automation or when multiple processes need isolated configs.

```bash
# Bash
export MUX_CONFIG_DIR=/tmp/mux-job-123
mux print --output-format jsonl --yolo "run the task"

# PowerShell
$env:MUX_CONFIG_DIR = "C:\\temp\\mux-job-123"
mux probe --output-format json

# CLI override
mux print --config-dir /tmp/mux-job-123 --output-format jsonl --yolo "run the task"
```

When config isolation is used:
- config is loaded from that directory
- first-run seeding happens in that directory
- `mux` does not fall back to the user-home config directory for those config reads
- `--config-dir` takes precedence over `MUX_CONFIG_DIR`

## Backend Examples

### Ollama

Mux's `ollama` adapter uses Ollama's OpenAI-compatible API surface, so the base URL is typically `http://localhost:11434/v1`.

```json
{
  "endpoints": [
    {
      "name": "ollama-gemma",
      "adapterType": "ollama",
      "baseUrl": "http://localhost:11434/v1",
      "model": "gemma3:4b",
      "isDefault": true
    },
    {
      "name": "ollama-qwen32",
      "adapterType": "ollama",
      "baseUrl": "http://localhost:11434/v1",
      "model": "qwen2.5-coder:32b"
    }
  ]
}
```

```bash
mux
mux --endpoint ollama-qwen32
mux print --yolo --endpoint ollama-qwen32 "refactor UserService"
```

### vLLM

```json
{
  "endpoints": [
    {
      "name": "vllm-deepseek",
      "adapterType": "openai-compatible",
      "baseUrl": "http://localhost:8000/v1",
      "model": "deepseek-ai/DeepSeek-Coder-V2-Instruct",
      "headers": { "Authorization": "Bearer sk-local-dev" },
      "quirks": {
        "assembleToolCallDeltas": true,
        "supportsParallelToolCalls": true,
        "stripRequestFields": ["stream_options"]
      }
    }
  ]
}
```

```bash
mux --endpoint vllm-deepseek
mux print --yolo --endpoint vllm-deepseek "refactor UserService to be async"
mux probe -e vllm-deepseek --output-format json
```

### OpenAI

```json
{
  "endpoints": [
    {
      "name": "openai-gpt4o",
      "adapterType": "openai",
      "baseUrl": "https://api.openai.com/v1",
      "model": "gpt-4o",
      "headers": { "Authorization": "Bearer ${OPENAI_API_KEY}" }
    }
  ]
}
```

```bash
mux --endpoint openai-gpt4o
mux print --yolo -e openai-gpt4o "explain the architecture of this project"
mux probe -e openai-gpt4o
```

### Ad-Hoc CLI-Only Usage

```bash
mux --base-url http://localhost:11434/v1 --model gemma3:4b --adapter-type ollama
mux --base-url https://api.openai.com/v1 --model gpt-4o --adapter-type openai
mux --base-url http://localhost:8000/v1 --model deepseek-coder-v2 --adapter-type openai-compatible
```

CLI overrides always win over endpoint config values.

## External Search Configuration

External search providers are stored in `settings.json` under `externalSearch`. Supported provider types are `tavily` and `you`.

Tavily example:

```json
{
  "externalSearch": {
    "enabled": true,
    "allowFallback": true,
    "providers": [
      {
        "name": "tavily-primary",
        "providerType": "tavily",
        "endpoint": "https://api.tavily.com/search",
        "apiKey": "${TAVILY_API_KEY}",
        "enabled": true,
        "isDefault": true,
        "timeoutMs": 60000
      }
    ]
  }
}
```

You.com example:

```json
{
  "externalSearch": {
    "enabled": true,
    "allowFallback": true,
    "providers": [
      {
        "name": "you-primary",
        "providerType": "you",
        "endpoint": "https://ydc-index.io/v1/search",
        "apiKey": "${YOU_API_KEY}",
        "enabled": true,
        "isDefault": true,
        "timeoutMs": 60000
      }
    ]
  }
}
```

The interactive `/search add` wizard writes the same structure for you.

## MCP Tool Servers

Example `mcp-servers.json`:

```json
{
  "servers": [
    {
      "name": "github",
      "transport": "stdio",
      "command": "npx",
      "args": ["-y", "@modelcontextprotocol/server-github"],
      "env": { "GITHUB_TOKEN": "${GITHUB_TOKEN}" }
    },
    {
      "name": "remote-http",
      "transport": "http",
      "url": "https://mcp.example.com",
      "mcpPath": "/mcp"
    }
  ]
}
```

Runtime management:

```text
/mcp list
/mcp ls
/mcp add
/mcp remove myserver
/mcp delete myserver
/mcp rm myserver
```

`/mcp add` now runs a guided wizard similar to `/endpoint add`. The wizard lets you choose `stdio` or HTTP transport, and successful adds are saved to `mcp-servers.json` as well as connected for the current session. HTTP MCP currently uses the streamable HTTP path, usually `/mcp`.

Skip MCP startup:

```bash
mux --no-mcp
```

Important:
- MCP integration is interactive-only today
- `mux print` and `mux probe` do not load MCP servers
- passing `--no-mcp` to `print` or `probe` returns a structured configuration error instead of silently implying MCP support

## Orchestrator Integration

Recommended command forms:

```bash
mux print --output-format jsonl --yolo "implement the feature described in TASK.md"
mux print --config-dir /tmp/mux-job-123 --output-format jsonl --output-last-message result.txt --yolo "implement the feature described in TASK.md"
mux print --output-format jsonl --yolo --endpoint vllm-deepseek --working-directory /tmp/worktree-abc "fix the bug"
mux print --output-format jsonl --yolo --system-prompt /path/to/persona.md "do the thing"
mux probe --output-format json --require-tools --endpoint vllm-deepseek
mux endpoint list --output-format json
mux endpoint ls --output-format json
mux endpoint show vllm-deepseek --output-format json
```

Recommendations:
- set `--config-dir` per run when you can; otherwise set `MUX_CONFIG_DIR`
- prefer `--output-format jsonl` for `print`
- prefer `--output-format json` for `probe`
- prefer `--output-last-message` when the caller needs a clean final answer artifact
- use explicit `--endpoint` in production automation
- use `--yolo` or `--approval-policy auto` only when automatic tool execution is intended
- use `--require-tools` when validating captain endpoints
- rely on `run_started` and `probe` JSON metadata instead of inferring tool/MCP capability from docs alone
- rely on `contractVersion` for parser compatibility gating
- treat `print.errorCode`/`print.failureCategory` and `probe.errorCode`/`probe.failureCategory` as the stable failure classification surface
- treat `mux endpoint list`, `mux endpoint ls`, and `mux endpoint show <name>` with `--output-format json` as the supported endpoint inspection surface

## Contract Compatibility

Structured non-interactive output uses a shared `contractVersion`.

Compatibility rules:
- additive fields are non-breaking within a contract version
- consumers should ignore unknown fields within a known contract version
- a contract-version bump is required for removals, renames, type changes, or semantic changes to required fields
