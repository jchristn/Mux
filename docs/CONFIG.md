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
| `subagents.json` | Named subagents the model can delegate scoped sub-tasks to via `spawn_subagent`; seeded with an example on first run | No |
| `keybindings.json` | User overrides for command key chords (rebind or unbind); seeded empty on first run | No |
| `hooks.json` | Event hooks and custom slash commands run out-of-process (the plugin system); seeded empty on first run | No |

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
      "quirks": null,
      "reasoningEffort": { "level": "high" },
      "showThinking": false
    }
  ]
}
```

Fields:

| Field | Type | Notes |
|---|---|---|
| `name` | string | unique endpoint name |
| `adapterType` | string | `ollama`, `openai`, `vllm`, `openai-compatible`, `anthropic`, `gemini`, `azure-openai`, `vertex`, or `bedrock` |
| `baseUrl` | string | API root URL. For `openai`/`openai-compatible`/`vllm`, mux appends `/v1/chat/completions` (a base already ending in `/v1` is fine). For `ollama`, mux uses Ollama's native API root, usually `http://localhost:11434` — a trailing `/v1` is stripped for this adapter. For `anthropic`/`gemini`, optional — blank uses the provider's public API root. For `azure-openai`, **required** — the Azure resource endpoint (e.g. `https://my-resource.openai.azure.com`). For `vertex`/`bedrock`, optional — blank derives the regional host from `region` |
| `model` | string | model identifier sent to the backend. For `azure-openai`, this is the **deployment name** |
| `isDefault` | bool | preferred default endpoint |
| `maxTokens` | int | max output tokens |
| `temperature` | number | sampling temperature |
| `contextWindow` | int | model context window |
| `timeoutMs` | int | HTTP timeout |
| `headers` | object | auth or custom headers; values may be stored directly or sourced from environment-variable references |
| `autoApproveTools` | bool | auto-approve tool calls whenever this endpoint is active unless CLI approval flags override it |
| `maxAgentIterations` | int or null | optional endpoint override for the agent loop guard; `null` inherits `settings.json` |
| `quirks` | object or null | backend behavior flags |
| `reasoningEffort` | object or null | optional reasoning effort. Omit (or `null`) to send no reasoning field. A `level` (`minimal`, `low`, `medium`, `high`) drives provider defaults; optional `openAiValue`, `geminiThinkingBudget` (`-1`..`32768`), and `ollamaThink` (`low`/`medium`/`high`/`true`/`false`) override individual per-provider values |
| `showThinking` | bool | whether the model's reasoning ("thinking") is captured and displayed when this endpoint is active. Defaults to false; toggle live with `/thinking` or override a headless run with `--show-thinking` |
| `apiKey` | string or null | API key for adapters that pass a key to the client rather than a raw header: `anthropic` (`x-api-key`), `gemini` (URL key), and `azure-openai` (`api-key` header). Literal value or a `${VAR}` reference. Ignored by the OpenAI family (use `headers`) and by `vertex`/`bedrock` (environment credentials) |
| `region` | string or null | cloud region for `vertex` (e.g. `us-central1`) and `bedrock` (e.g. `us-east-1`). Literal or `${VAR}` |
| `project` | string or null | Google Cloud project id, required by `vertex`. Literal or `${VAR}` |
| `apiVersion` | string or null | optional Azure OpenAI `api-version` for `azure-openai`; `null` uses PolyPrompt's default |

Header values support environment expansion:

```json
{
  "headers": {
    "Authorization": "Bearer ${OPENAI_API_KEY}"
  }
}
```

### Frontier and cloud provider adapters

mux reaches these through PolyPrompt's native clients (PolyPrompt 2.5.0+):

```json
{
  "endpoints": [
    { "name": "claude", "adapterType": "anthropic", "model": "claude-opus-4-8", "apiKey": "${ANTHROPIC_API_KEY}" },
    { "name": "gemini", "adapterType": "gemini", "model": "gemini-2.5-pro", "apiKey": "${GEMINI_API_KEY}" },
    { "name": "azure", "adapterType": "azure-openai", "baseUrl": "https://my-resource.openai.azure.com",
      "model": "my-gpt4o-deployment", "apiKey": "${AZURE_OPENAI_API_KEY}", "apiVersion": "2024-10-21" },
    { "name": "vertex", "adapterType": "vertex", "model": "gemini-2.5-pro", "project": "my-gcp-project", "region": "us-central1" },
    { "name": "bedrock", "adapterType": "bedrock", "model": "anthropic.claude-3-5-sonnet-20241022-v2:0", "region": "us-east-1" }
  ]
}
```

Credential notes:
- `anthropic` / `gemini` / `azure-openai` — set `apiKey` (literal or `${VAR}`).
- `vertex` — credentials come from **Application Default Credentials**; set `GOOGLE_APPLICATION_CREDENTIALS` (a service-account key file) or run on GCP with a metadata server. `project` and `region` are required.
- `bedrock` — credentials come from the **AWS environment** (`AWS_ACCESS_KEY_ID`, `AWS_SECRET_ACCESS_KEY`, optional `AWS_SESSION_TOKEN`), SigV4-signed per request. `region` is required.

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

Global mux settings. Edit them in the interactive shell with the **`/settings`** command (aliases
`/config`, `/preferences`, `/prefs`; also in the `F1` menu): a scrolling form covers every scalar/boolean
field below. Saving persists here; the run-affecting values (iteration cap, token budget, compaction,
context tuning, cert errors) apply on the next turn, while concurrency and startup-only wiring apply on the
next launch. Per-model overrides (such as an endpoint's own `maxAgentIterations`) stay in the endpoint
Add/Edit form and win over the global value for that endpoint.

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

## REST server & tray agent (`settings.json` `rest`)

mux v0.9.0 adds an opt-in local REST + WebSocket server and a system-tray agent. Configure them under the
`rest` block:

```json
{
  "rest": {
    "enabled": false,
    "hostname": "127.0.0.1",
    "port": 8710,
    "ssl": false,
    "apiKey": null,
    "corsAllowOrigin": "*"
  }
}
```

| Field | Type | Notes |
|---|---|---|
| `enabled` | bool | Whether the tray agent auto-starts the server. `mux serve` starts it regardless. Default false. |
| `hostname` | string | Bind host. Default `127.0.0.1` (loopback). |
| `port` | int | Bind port, clamped 1-65535. Default 8710. |
| `ssl` | bool | Bind with SSL. Default false. |
| `apiKey` | string or null | Local API key; auto-generated on first `mux serve` when blank. Literal or `${VAR}`. |
| `corsAllowOrigin` | string | `Access-Control-Allow-Origin` value. Default `*`. |

Environment overrides: `MUX_REST_HOST`, `MUX_REST_PORT`, `MUX_REST_APIKEY`. The server is **opt-in** and never
starts from a plain `mux`/`mux print` run. Full reference: [REST_API.md](REST_API.md).

## Usage telemetry (`settings.json` `telemetry`)

Every model call is recorded to a local SQLite database (`~/.mux/usage.db` by default) — token counts,
time-to-first-token, streaming time, total latency, throughput, finish reason, and success — tagged with the
endpoint, model, command, session, and call kind. The store is written concurrently by every mux instance
(WAL mode, no external process) and read by the TUI `/usage` view and the `mux serve` dashboard. Capture is
best-effort: a telemetry fault never affects a run.

```json
{
  "telemetry": {
    "enabled": true,
    "retentionDays": 90,
    "databasePath": null,
    "pricingEnabled": true,
    "maxRows": 5000000
  }
}
```

| Field | Type | Notes |
|---|---|---|
| `enabled` | bool | Whether usage is captured/persisted. Default true. When false, no database is opened and the dashboard/`/usage` show an empty state. |
| `retentionDays` | int | Days of history to keep; rows older are pruned on open and daily. Clamped 0-3650; 0 keeps forever. Default 90. |
| `databasePath` | string or null | Override for the database file. Blank resolves to `usage.db` under the config directory. |
| `pricingEnabled` | bool | Whether derived cost is shown (from `pricing.json`). Default true. |
| `maxRows` | int | Secondary retention guard: max rows regardless of age. Floored at 1000. Default 5,000,000. |

Environment override: `MUX_TELEMETRY_ENABLED` (`1`/`true`/`0`/`false`) toggles capture without editing the file.

## Model pricing (`pricing.json`)

Cost is derived at read time from a user-editable pricing table, so a rate correction re-values history. A
curated set of defaults is seeded on first run and manifest-tracked (`pricing.seeded.json`) so upgrades add
new model defaults without resurrecting rows you deleted or overwriting edited rates. Unknown models cost
nothing until you add a rate (edit this file or use the dashboard **Pricing** page).

```json
{
  "version": "2026-09",
  "models": {
    "claude-opus-4-8": { "inputPerMTok": 15.0, "cachedInputPerMTok": 1.5, "outputPerMTok": 75.0 },
    "gpt-4o":          { "inputPerMTok": 2.5,  "cachedInputPerMTok": 1.25, "outputPerMTok": 10.0 }
  }
}
```

Rates are US dollars per million tokens: `inputPerMTok` (uncached prompt), `cachedInputPerMTok` (cache-read),
and `outputPerMTok` (completion). Cost for a call is `(input − cached)·input + cached·cachedInput + output·output`.
Local models (Ollama, vLLM) have no default entry and cost 0.

## `subagents.json` (subagent delegation)

Subagents are named personas the model can delegate a self-contained sub-task to via the
`spawn_subagent` tool. Each runs in an **isolated conversation** — it never sees or mutates the
parent's history — so delegating focused work (a review, a scoped search, a mechanical change) keeps
the primary agent's context clean and lets a cheaper or more specialized model do the work. The tool is
offered to the model only when at least one valid subagent is defined.

```json
{
  "subagents": [
    {
      "name": "reviewer",
      "description": "Reviews a diff or file for bugs and risks; read-only.",
      "systemPrompt": "You are a meticulous code reviewer. Inspect the described files and report concrete bugs and risks. Do not modify any files.",
      "endpointName": null,
      "allowedTools": ["read_file", "grep", "glob", "list_directory", "file_metadata"],
      "maxIterations": null
    }
  ]
}
```

| Field | Type | Notes |
|---|---|---|
| `name` | string | Unique name the model selects by; empty names are dropped. |
| `description` | string | Shown to the model in the `spawn_subagent` schema so it can pick the right one. |
| `systemPrompt` | string | Fully replaces the parent's system prompt for the isolated child run; required. |
| `endpointName` | string or null | Endpoint the subagent runs under; `null` inherits the parent's endpoint. |
| `allowedTools` | string[] | Tool-name globs the child is limited to; empty inherits the parent's tool policy. A tight list is the main way to constrain a delegated task. |
| `maxIterations` | int or null | Agent-loop cap for the child; `null` inherits the parent's cap. |

A subagent cannot itself spawn subagents, and it never carries the parent's task plan. `spawn_subagent`
does not hold the workspace write lease, so a delegated task's own mutating tools serialize normally.

## `keybindings.json` (custom key chords)

Overrides the default key chord bound to any command, by command id. A chord uses TUIKit syntax
(`"ctrl+k"`, `"f5"`); `null` unbinds the command's default chord. An unparseable chord is ignored (the
built-in binding stays), so a typo never breaks startup. The override applies to every surface — key
bindings, the menu bar, and footer hints. Run `/help` (or press `F1`) to see the current command ids and
their chords.

```json
{
  "bindings": {
    "mux.clear": "ctrl+k",
    "mux.save": null
  }
}
```

Common command ids: `mux.quit`, `mux.endpoint`, `mux.clear`, `mux.sidebar.toggle`, `mux.save`,
`mux.export`, `mux.undo`, `mux.redo`, `mux.queue`, `mux.prompts`, `mux.menu`.

## `hooks.json` (plugin system: hooks & custom commands)

The plugin system extends mux with **out-of-process** event hooks and custom slash commands — no code is
loaded into the mux process. Both are launched as a literal argument vector (never through a shell), so
arguments are passed verbatim with no interpolation.

```json
{
  "hooks": [
    {
      "name": "notify-start",
      "event": "session-start",
      "command": "notify-send",
      "args": ["mux session started"],
      "blocking": false,
      "timeoutMs": 15000
    },
    {
      "name": "block-secrets",
      "event": "user-prompt-submit",
      "command": "python3",
      "args": ["/home/me/.mux/guard.py"],
      "blocking": true,
      "timeoutMs": 5000
    }
  ],
  "commands": [
    { "name": "deploy", "description": "Run the deploy script", "command": "bash", "args": ["scripts/deploy.sh"], "timeoutMs": 60000 }
  ]
}
```

**Hooks** run when a lifecycle event fires. Supported events:

| Event | When it fires | Vetoable |
|---|---|---|
| `session-start` | Once when the interactive shell starts | No |
| `user-prompt-submit` | When a prompt is submitted, before the turn runs | Yes |
| `session-end` | Once on a clean exit | No |

The event payload is delivered to the hook as a JSON document on **stdin**; the hook's **stdout** is
surfaced into the transcript. For a vetoable event, a hook with `"blocking": true` that exits non-zero
**blocks** the action (the prompt is refused). A hook whose command cannot start is treated as absent, so
a typo never wedges the session. `timeoutMs` is clamped to `100-600000` (default 15000).

**Custom commands** register as `/<name>` on the interactive command surface. Invoking one runs the
command out-of-process in the working directory and posts its output into the transcript. `timeoutMs`
defaults to 30000. Inspect the configured hooks and commands with `mux plugin list`.
