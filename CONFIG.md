# mux Configuration Reference

All config lives under `~/.mux/` by default. Set `MUX_CONFIG_DIR` to use a different directory.

## Config Directory

Default:

```text
~/.mux/
```

Override:

```bash
# Bash
export MUX_CONFIG_DIR=/tmp/mux-config

# PowerShell
$env:MUX_CONFIG_DIR = "C:\\temp\\mux-config"
```

When `MUX_CONFIG_DIR` is set, `mux` uses that directory for:
- `endpoints.json`
- `mcp-servers.json`
- `settings.json`
- `system-prompt.md`

If the directory does not exist, `mux` creates it. If `endpoints.json` is missing, `mux` seeds a default Ollama endpoint there. Existing files are not overwritten.

## Files

| File | Purpose | Required |
|---|---|---|
| `endpoints.json` | Endpoint definitions | No, if CLI endpoint flags are sufficient |
| `mcp-servers.json` | MCP server definitions | No |
| `settings.json` | Global mux settings | No |
| `system-prompt.md` | Custom default system prompt | No |

For current non-interactive orchestration paths:
- `settings.json` is optional
- `mux print` resolves base endpoint values from `endpoints.json` or the internal default, then applies CLI overrides

## `endpoints.json`

Defines named model runner endpoints.

Example:

```json
{
  "endpoints": [
    {
      "name": "ollama-local",
      "adapterType": "ollama",
      "baseUrl": "http://localhost:11434/v1",
      "model": "qwen2.5-coder:7b",
      "isDefault": true,
      "maxTokens": 8192,
      "temperature": 0.1,
      "contextWindow": 32768,
      "timeoutMs": 120000,
      "headers": {},
      "quirks": null
    }
  ]
}
```

Fields:

| Field | Type | Notes |
|---|---|---|
| `name` | string | unique endpoint name |
| `adapterType` | string | `ollama`, `openai`, `vllm`, or `openai-compatible` |
| `baseUrl` | string | API root URL; mux appends `/chat/completions`. For `ollama`, mux uses Ollama's OpenAI-compatible API root, usually `http://localhost:11434/v1` |
| `model` | string | model identifier sent to the backend |
| `isDefault` | bool | preferred default endpoint |
| `maxTokens` | int | max output tokens |
| `temperature` | number | sampling temperature |
| `contextWindow` | int | model context window |
| `timeoutMs` | int | HTTP timeout |
| `headers` | object | auth or custom headers; values may be stored directly or sourced from environment-variable references |
| `quirks` | object or null | backend behavior flags |

Header values support environment expansion:

```json
{
  "headers": {
    "Authorization": "Bearer ${OPENAI_API_KEY}"
  }
}
```

Interactive endpoint management:
- `/endpoint` or `/endpoint list` shows saved endpoints and highlights the current session endpoint
- `/endpoint add` starts a guided endpoint creation wizard
- `/endpoint edit <name>` starts a guided endpoint edit wizard
- `/endpoint show <name>` displays the stored endpoint fields and performs a lightweight connectivity probe
- `/endpoint remove <name>` asks for confirmation and refuses to remove the endpoint active in the current session

Wizard auth options:
- `none`
- `bearer token`
- `custom headers`

When the wizard collects auth values, you can either store the value directly in `endpoints.json` or provide an environment-variable reference. The wizard accepts `OPENAI_API_KEY`, `${OPENAI_API_KEY}`, `%OPENAI_API_KEY%`, `$OPENAI_API_KEY`, and `$env:OPENAI_API_KEY`, then stores environment references canonically as `${OPENAI_API_KEY}`.

Endpoint resolution:
1. If `--endpoint <name>` is provided, mux requires that endpoint to exist.
2. Otherwise mux uses the endpoint marked `isDefault: true`.
3. If no endpoint is marked default, mux falls back to the first configured endpoint.
4. If no endpoints exist, mux falls back to an internal local Ollama default.
5. CLI overrides such as `--model`, `--base-url`, and `--adapter-type` are then applied.

## `mcp-servers.json`

Defines MCP servers launched by mux.

Example:

```json
{
  "servers": [
    {
      "name": "github",
      "command": "npx",
      "args": ["-y", "@modelcontextprotocol/server-github"],
      "env": {
        "GITHUB_TOKEN": "${GITHUB_TOKEN}"
      }
    }
  ]
}
```

Fields:

| Field | Type | Notes |
|---|---|---|
| `name` | string | unique server name |
| `command` | string | executable to launch |
| `args` | string[] | command arguments |
| `env` | object | environment variables with `${VAR}` expansion |

## `settings.json`

Global mux settings.

Example:

```json
{
  "systemPromptPath": null,
  "defaultApprovalPolicy": "ask",
  "toolTimeoutMs": 30000,
  "processTimeoutMs": 120000,
  "contextWindowSafetyMarginPercent": 15,
  "tokenEstimationRatio": 3.5,
  "autoCompactEnabled": true,
  "contextWarningThresholdPercent": 80,
  "compactionStrategy": "summary",
  "compactionPreserveTurns": 3,
  "maxAgentIterations": 25
}
```

Fields:

| Field | Type | Notes |
|---|---|---|
| `systemPromptPath` | string or null | optional path to a custom prompt file |
| `defaultApprovalPolicy` | string | `ask`, `auto`, or `deny` |
| `toolTimeoutMs` | int | built-in tool timeout |
| `processTimeoutMs` | int | `run_process` timeout |
| `contextWindowSafetyMarginPercent` | int | safety margin for conversation truncation |
| `tokenEstimationRatio` | number | rough chars-to-tokens estimate |
| `autoCompactEnabled` | bool | automatically compact persisted history before interactive runs when the next prompt would exceed the usable context budget |
| `contextWarningThresholdPercent` | int | warning threshold for estimated context usage; clamped to `50-95` |
| `compactionStrategy` | string | `summary` or `trim`; controls `/compact`, interactive preflight auto-compaction, and in-run active-conversation compaction |
| `compactionPreserveTurns` | int | number of recent user-led turns to preserve during compaction; clamped to `1-10` |
| `maxAgentIterations` | int | loop guard for tool-using runs |

Notes:
- `mux print` still defaults to deny semantics unless `--yolo` or `--approval-policy` overrides it
- CLI flags override settings file values
- `mux print` and `mux probe` reject `--approval-policy ask`
- `mux print` and `mux probe` do not load MCP servers, even if `mcp-servers.json` exists

## `system-prompt.md`

Optional plain-text or markdown file used as the default system prompt when no higher-priority override is present.

Resolution priority:
1. `--system-prompt <path>`
2. `settings.json.systemPromptPath`
3. `system-prompt.md` in the active config directory
4. built-in default prompt

## Environment Variables

`mux` recognizes:

| Variable | Description |
|---|---|
| `MUX_CONFIG_DIR` | override the active config directory |

Config values may reference environment variables using `${VAR_NAME}`, `%VAR_NAME%`, `$VAR_NAME`, or `$env:VAR_NAME`. The interactive endpoint wizard accepts the same forms and writes stored references as `${VAR_NAME}`.

## CLI Override Notes

Common CLI overrides:
- `--endpoint`
- `--model`
- `--base-url`
- `--adapter-type`
- `--temperature`
- `--max-tokens`
- `--compaction-strategy`
- `--approval-policy`
- `--system-prompt`
- `--working-directory`

These override config values after endpoint selection.
