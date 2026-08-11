# mux Configuration Reference

All config lives under `~/.mux/` by default. Use `--config-dir` or `MUX_CONFIG_DIR` to select a different directory.

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

# CLI override
mux print --config-dir /tmp/mux-config --output-format jsonl --yolo "run the task"
```

Resolution precedence:
1. `--config-dir <path>`
2. `MUX_CONFIG_DIR`
3. `~/.mux/`

When config directory selection is applied, mux uses that directory for:
- `endpoints.json`
- `mcp-servers.json`
- `settings.json`
- `system-prompt.md`
- `prompts.json`
- `skills.json` and the `skills/` directory

If the directory does not exist, `mux` creates it. If `endpoints.json` is missing, `mux` seeds a default Ollama endpoint there. If `settings.json` is missing, `mux` writes editable default settings. If `prompts.json` is missing, `mux` seeds one active `Default` profile that inherits every built-in prompt. If the `skills/` directory is missing, `mux` creates it and seeds the curated default skills. Existing files are not overwritten.

## Files

| File | Purpose | Required |
|---|---|---|
| `endpoints.json` | Endpoint definitions | No, if CLI endpoint flags are sufficient |
| `mcp-servers.json` | MCP server definitions | No |
| `settings.json` | Global mux settings | No |
| `system-prompt.md` | Custom default system prompt | No |
| `prompts.json` | Named, switchable prompt profiles (system + internal prompts) | No |
| `skills/` | User-authored skills, one folder per skill (`SKILL.md` plus optional `scripts/` and `resources/`); seeded with a curated default set on first run | Created on demand |
| `skills.json` | Per-skill enablement and pinning, kept separate from each `SKILL.md` so toggling a skill never rewrites it | No |
| `sessions/` | Saved interactive sessions (one JSON file per session); the shell autosaves here at each turn boundary and `/sessions` browses/resumes them | Created on demand |

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
      "baseUrl": "http://localhost:11434",
      "model": "qwen2.5-coder:7b",
      "isDefault": true,
      "maxTokens": 8192,
      "temperature": 0.1,
      "contextWindow": 32768,
      "timeoutMs": 120000,
      "headers": {},
      "autoApproveTools": false,
      "maxAgentIterations": null,
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
| `baseUrl` | string | API root URL. For `openai`/`openai-compatible`/`vllm`, mux appends `/v1/chat/completions` (a base already ending in `/v1` is fine). For `ollama`, mux uses Ollama's native API root, usually `http://localhost:11434` — a trailing `/v1` is stripped for this adapter |
| `model` | string | model identifier sent to the backend |
| `isDefault` | bool | preferred default endpoint |
| `maxTokens` | int | max output tokens |
| `temperature` | number | sampling temperature |
| `contextWindow` | int | model context window |
| `timeoutMs` | int | HTTP timeout |
| `headers` | object | auth or custom headers; values may be stored directly or sourced from environment-variable references |
| `autoApproveTools` | bool | auto-approve tool calls whenever this endpoint is active unless CLI approval flags override it |
| `maxAgentIterations` | int or null | optional endpoint override for the agent loop guard; `null` inherits `settings.json` |
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
- `/endpoint`, `/endpoint list`, `/endpoint ls`, `/model`, `/model list`, or `/model ls` show saved endpoints and highlight the current session endpoint
- `/model` is an alias for `/endpoint` and supports the same `<name>`, `show`, `add`, `edit`, and `remove`/`delete`/`rm` forms
- `/endpoint add` starts a guided endpoint creation wizard
- `/endpoint edit <name>` starts a guided endpoint edit wizard
- `/endpoint show <name>` displays the stored endpoint fields and performs a lightweight connectivity probe
- `/endpoint remove <name>`, `/endpoint delete <name>`, and `/endpoint rm <name>` ask for confirmation and refuse to remove the endpoint active in the current session
- typing `a` or `always` at an approval prompt auto-approves the rest of the current run and saves `autoApproveTools: true` for the active endpoint

Non-interactive endpoint inspection:
- `mux endpoint list --output-format json` lists configured endpoints
- `mux endpoint ls --output-format json` is an alias for `mux endpoint list --output-format json`
- `mux endpoint show <name> --output-format json` returns one configured endpoint with header values redacted, including `maxAgentIterations`, `effectiveMaxAgentIterations`, and `maxAgentIterationsSource`

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
      "transport": "stdio",
      "command": "npx",
      "args": ["-y", "@modelcontextprotocol/server-github"],
      "env": {
        "GITHUB_TOKEN": "${GITHUB_TOKEN}"
      }
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

Fields:

| Field | Type | Notes |
|---|---|---|
| `name` | string | unique server name |
| `transport` | string | `stdio` or `http`; defaults to `stdio` for older configs that omit it |
| `command` | string | executable to launch for `stdio` servers |
| `args` | string[] | command arguments for `stdio` servers |
| `env` | object | environment variables with `${VAR}` expansion for `stdio` servers |
| `url` | string | base URL for HTTP MCP servers |
| `mcpPath` | string | streamable HTTP MCP path, usually `/mcp` |

Notes:
- `stdio` launches a local subprocess and communicates over stdin/stdout
- HTTP MCP currently uses streamable HTTP and does not currently expose per-server auth/header configuration in mux

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
  "maxAgentIterations": 50,
  "maxTokenBudget": null,
  "ignoreCertErrors": false,
  "showBoundaryLines": false,
  "skillsEnabled": true,
  "skillRefreshIntervalSeconds": 30,
  "skillsDirectory": null,
  "taskPlanningEnabled": true,
  "taskParallelismEnabled": false,
  "externalSearch": {
    "enabled": false,
    "allowFallback": true,
    "providers": []
  }
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
| `maxAgentIterations` | int | default loop guard for tool-using runs; clamped to `1-100` and overridden by endpoint `maxAgentIterations` when that value is set |
| `maxTokenBudget` | int or null | optional ceiling on estimated working-context tokens; when set and exceeded before a model call, the run stops with a `budget_exceeded` error; backend-agnostic (mux's estimate, not provider billing); overridden per run by `--max-token-budget`; `null` (default) disables the cap |
| `ignoreCertErrors` | bool | disable TLS certificate validation for mux-owned network requests; default `false` |
| `showBoundaryLines` | bool | draw dark-grey boundary lines in the interactive shell (above the prompt input, above the queued-messages strip, and left of the sidebar); toggle live with `/borders`; default `false` |
| `skillsEnabled` | bool | load user-authored skills and expose them to the model in the interactive shell; default `true` |
| `skillRefreshIntervalSeconds` | int | how often the shell re-scans the skills directory for changes; clamped to a minimum of `5`; default `30` |
| `skillsDirectory` | string or null | override for the skills directory (for a shared, version-controlled library); `null` uses `~/.mux/skills` |
| `maxConcurrency` | int | maximum number of interactive jobs allowed to run at once; clamped to `1-32`, default `3` |
| `taskPlanningEnabled` | bool | offer the `plan_tasks`/`update_task` tools and teach the model to decompose large requests into a tracked task plan; default `true` |
| `taskParallelismEnabled` | bool | allow the opt-in orchestration engine to run independent tasks as parallel jobs under the shared write lease; has no effect unless `taskPlanningEnabled` is also true; default `false` |
| `defaultEnqueueBehavior` | string | how the interactive shell handles a submit while a job is active: `ask` (show the chooser), `run_now`, `queue_after` (both start a new job — the concurrency cap governs parallelism), or `add_to_focused` (append to the focused job); default `ask` |
| `externalSearch` | object | optional Tavily/You.com provider configuration for the `web_search` tool |

Notes:
- `mux print` still defaults to deny semantics unless `--yolo`, `--approval-policy`, or the selected endpoint's `autoApproveTools` setting overrides it
- CLI flags override settings file values
- `--ignore-cert-errors`, `--insecure`, or `MUX_IGNORE_CERT_ERRORS=1` can enable certificate-error bypass for runs behind enterprise TLS inspection
- When `settings.json` is loaded, mux rewrites it with normalized values so newly added settings are visible with defaults
- `mux print` and `mux probe` reject `--approval-policy ask`
- endpoint `maxAgentIterations` is nullable; leave it unset or set it to `null` to inherit the global `settings.json` default
- `mux print` and `mux probe` do not load MCP servers, even if `mcp-servers.json` exists

### `externalSearch`

`externalSearch` controls exposure of the built-in `web_search` tool. `web_search` is exposed only when `enabled` is `true` and at least one enabled provider has a name, provider type, endpoint, and API key. Supported provider types are:

- `tavily`
- `you`

Example with Tavily:

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

Example with You.com:

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

Provider fields:

| Field | Type | Notes |
|---|---|---|
| `name` | string | unique provider name; usable as a `web_search` provider override |
| `providerType` | string | `tavily` or `you` |
| `endpoint` | string | provider API endpoint |
| `apiKey` | string | provider API key or environment-variable reference |
| `enabled` | bool | whether this provider may be selected |
| `isDefault` | bool | preferred provider when no override is supplied |
| `timeoutMs` | int | request timeout; clamped to `1000-300000` |

Use `/search add`, `/search edit`, `/search show`, and `/search remove`/`delete`/`rm` in interactive mode to maintain this configuration without editing JSON by hand.

`web_search` discovers candidate web results. `web_retrieve` fetches a known HTTP or HTTPS URL and does not require external-search configuration.

## `prompts.json`

Named, switchable **prompt profiles** — the prompts mux sends to the model. Edit them in the interactive shell with **`Ctrl+P`** or the **`/prompts`** command (also in the `F1` menu): a large editor lets you switch the active profile, edit each prompt, and add / rename / remove profiles. Changes apply to the running session and are saved here.

Each profile carries three prompts; **an empty field inherits the built-in default**, so a profile only stores what it customizes:

| Field | Purpose |
|---|---|
| `systemPrompt` | The main system prompt (persona) sent with every turn. Keeps the `{WorkingDirectory}` and `{ToolDescriptions}` placeholders, which mux fills in at run time. |
| `toolsDisabledPrompt` | Used instead of `systemPrompt` when the active endpoint does not support tools. Keeps `{WorkingDirectory}`. |
| `compactionPrompt` | The system prompt for the automatic history-compaction sidecar call. |

```json
{
  "prompts": [
    {
      "name": "Default",
      "isActive": true,
      "systemPrompt": "",
      "toolsDisabledPrompt": "",
      "compactionPrompt": ""
    }
  ]
}
```

Exactly one profile is active. The active profile's `systemPrompt` (when non-empty) is the primary source for the system prompt — see the resolution priority below.

## `system-prompt.md`

Optional plain-text or markdown file used as the default system prompt when no higher-priority override is present.

Resolution priority:
1. `--system-prompt <path>`
2. active `prompts.json` profile `systemPrompt` (when non-empty)
3. `settings.json.systemPromptPath`
4. `system-prompt.md` in the active config directory
5. built-in default prompt

## Environment Variables

`mux` recognizes:

| Variable | Description |
|---|---|
| `MUX_CONFIG_DIR` | override the active config directory |
| `MUX_IGNORE_CERT_ERRORS` | set to `1`, `true`, `yes`, or `on` to disable TLS certificate validation for mux-owned network requests; set to `0`, `false`, `no`, or `off` to force it off |

Config values may reference environment variables using `${VAR_NAME}`, `%VAR_NAME%`, `$VAR_NAME`, or `$env:VAR_NAME`. The interactive endpoint wizard accepts the same forms and writes stored references as `${VAR_NAME}`. If both `--config-dir` and `MUX_CONFIG_DIR` are present, the CLI flag wins.

Certificate-error bypass applies to mux-created LLM HTTP clients, external-search provider HTTP clients, `web_retrieve` browser navigation, and Playwright browser installation. It does not change TLS behavior for shell commands launched with `run_process` or for external MCP server processes.

## CLI Override Notes

Common CLI overrides:
- `--config-dir`
- `--endpoint`
- `--model`
- `--base-url`
- `--adapter-type`
- `--temperature`
- `--max-tokens`
- `--compaction-strategy`
- `--ignore-cert-errors`
- `--insecure`
- `--approval-policy`
- `--system-prompt`
- `--working-directory`

These override config values after endpoint selection.
