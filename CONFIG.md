# mux Configuration Reference

> **Version**: v0.1.0

All mux configuration lives in `~/.mux/`. mux creates this directory and default files on first run if they don't exist.

---

## Table of Contents

- [endpoints.json](#endpointsjson)
- [mcp-servers.json](#mcp-serversjson)
- [settings.json](#settingsjson)
- [system-prompt.md](#system-promptmd)
- [Environment Variables](#environment-variables)
- [CLI Override Precedence](#cli-override-precedence)

---

## `endpoints.json`

**Path**: `~/.mux/endpoints.json`

Defines the model runner backends mux can connect to. Each entry is a named endpoint with connection details, model selection, and behavioral configuration. At least one endpoint must have `isDefault: true`.

### Full Schema

```json
{
  "endpoints": [
    {
      "name": "string (required)",
      "adapterType": "string (required)",
      "baseUrl": "string (required)",
      "model": "string (required)",
      "isDefault": "boolean (default: false)",
      "maxTokens": "integer (default: 8192)",
      "temperature": "number (default: 0.1)",
      "contextWindow": "integer (default: 32768)",
      "headers": "object (default: {})",
      "quirks": "object or null (default: null)"
    }
  ]
}
```

### Field Reference

#### `name` (string, required)

A unique identifier for this endpoint. Used with `--endpoint <name>` to select it from the CLI or with `/model <name>` in interactive mode.

- Must be unique across all endpoints
- Convention: lowercase with hyphens (e.g., `ollama-qwen`, `openai-gpt4o`, `vllm-deepseek`)

#### `adapterType` (string, required)

Selects the backend adapter that handles request shaping and response normalization. Each adapter knows how to talk to a specific type of model runner.

| Value | Description |
|-------|-------------|
| `ollama` | Ollama runner. Strips unsupported fields, handles Ollama's tool-call assembly quirks, maps Ollama-specific finish reasons. |
| `openai` | OpenAI direct API (`api.openai.com`). Requires authentication via `headers`. Full support for parallel tool calls and structured outputs. |
| `vllm` | Reserved for future vLLM-specific adapter. Currently maps to `openai-compatible`. |
| `openai-compatible` | Generic OpenAI-compatible API. Works with vLLM, LM Studio, llama.cpp server, Together AI, Groq, Fireworks, Azure OpenAI, and other OpenAI-format endpoints. |

**When to use which:**
- Running Ollama locally or remotely: `ollama`
- Using OpenAI's API directly: `openai`
- Running vLLM, LM Studio, or any other self-hosted runner: `openai-compatible`
- Using a cloud API that mimics the OpenAI format (Together, Groq, etc.): `openai-compatible`

#### `baseUrl` (string, required)

The base URL for the API endpoint. mux appends `/chat/completions` to this URL for LLM calls.

| Backend | Typical Base URL |
|---------|-----------------|
| Ollama (local) | `http://localhost:11434/v1` |
| Ollama (remote) | `http://gpu-server:11434/v1` |
| vLLM | `http://localhost:8000/v1` |
| OpenAI | `https://api.openai.com/v1` |
| Azure OpenAI | `https://<resource>.openai.azure.com/openai/deployments/<deployment>/` |
| Together AI | `https://api.together.xyz/v1` |
| Groq | `https://api.groq.com/openai/v1` |
| LM Studio | `http://localhost:1234/v1` |

#### `model` (string, required)

The model identifier sent in the `model` field of the chat completions request.

| Backend | Example Values |
|---------|---------------|
| Ollama | `qwen2.5-coder:32b`, `llama3.1:70b`, `codellama:34b`, `deepseek-coder-v2:latest` |
| OpenAI | `gpt-4o`, `gpt-4o-mini`, `gpt-4-turbo`, `o1-preview` |
| vLLM | `deepseek-ai/DeepSeek-Coder-V2-Instruct` (matches the model path used at server launch) |
| Together AI | `meta-llama/Llama-3.1-70B-Instruct-Turbo` |

#### `isDefault` (boolean, default: `false`)

If `true`, this endpoint is used when mux is launched without `--endpoint`. Exactly one endpoint should have `isDefault: true`. If multiple are marked as default, the first one wins. If none is marked, mux errors on startup (unless `--base-url` and `--model` are passed as CLI flags).

#### `maxTokens` (integer, default: `8192`)

Maximum number of tokens the model should generate in a single response. Sent as `max_tokens` in the API request.

- Reasonable range: 1024 - 131072 (clamped on load)
- Higher values allow longer responses but use more of the context window

#### `temperature` (number, default: `0.1`)

Sampling temperature. Lower values produce more deterministic output.

- Range: 0.0 - 2.0 (clamped on load)
- For coding tasks, 0.0 - 0.2 is recommended
- For creative/conversational tasks, 0.5 - 1.0

#### `contextWindow` (integer, default: `32768`)

The model's total context window size in tokens. mux uses this for conversation management — when the estimated token count approaches `contextWindow * (1 - safetyMarginPercent/100)`, older messages are truncated.

- This should match the model's actual context window, not a desired limit
- Common values: 8192, 32768, 65536, 128000, 131072

#### `headers` (object, default: `{}`)

Custom HTTP headers included in every request to this endpoint. Use for authentication or any custom headers the backend requires.

- **Authentication**: Set `"Authorization": "Bearer ${OPENAI_API_KEY}"` for bearer token auth, or `"x-api-key": "..."` for key-based auth
- **Required** for `adapterType: "openai"` and most cloud APIs
- **Optional** for local runners (Ollama typically doesn't need any headers)
- **Supports environment variable expansion**: `"${VAR_NAME}"` is replaced with the value of the environment variable at config load time

**Security note**: Use environment variable expansion (`"${VAR_NAME}"`) rather than pasting keys directly into the config file. The config file may be checked into source control or shared.

#### `quirks` (object or null, default: `null`)

Per-endpoint behavioral flags consumed by the backend adapter. When `null`, the adapter's built-in defaults for the `adapterType` are used.

```json
{
  "quirks": {
    "assembleToolCallDeltas": true,
    "supportsParallelToolCalls": false,
    "requiresToolResultContentAsString": false,
    "defaultFinishReason": "stop",
    "stripRequestFields": []
  }
}
```

| Field | Type | Default | Description |
|-------|------|---------|-------------|
| `assembleToolCallDeltas` | boolean | `true` | Whether tool call chunks arrive as deltas requiring client-side assembly. Most backends: `true`. Ollama sometimes sends complete tool calls in a single chunk. |
| `supportsParallelToolCalls` | boolean | `false` | Whether to send `parallel_tool_calls: true` in requests. OpenAI supports this; most local runners do not. |
| `requiresToolResultContentAsString` | boolean | `false` | Whether tool result `content` must be a flat string (some backends reject structured content objects). |
| `defaultFinishReason` | string | `"stop"` | Finish reason to assume when the backend omits it from the response. |
| `stripRequestFields` | string[] | `[]` | Request fields to remove before sending. Use when a backend rejects unknown fields with 4xx errors. Common values: `"stream_options"`, `"parallel_tool_calls"`, `"response_format"`. |

**When to customize quirks:**
- If you get 4xx errors with a specific backend, add the rejected field names to `stripRequestFields`
- If tool calls arrive pre-assembled (not as deltas), set `assembleToolCallDeltas: false`
- If tool results must be plain strings, set `requiresToolResultContentAsString: true`

### Complete Example

```json
{
  "endpoints": [
    {
      "name": "ollama-qwen",
      "adapterType": "ollama",
      "baseUrl": "http://localhost:11434/v1",
      "model": "qwen2.5-coder:32b",
      "isDefault": true,
      "maxTokens": 8192,
      "temperature": 0.1,
      "contextWindow": 32768
    },
    {
      "name": "ollama-llama",
      "adapterType": "ollama",
      "baseUrl": "http://localhost:11434/v1",
      "model": "llama3.1:70b",
      "isDefault": false,
      "maxTokens": 8192,
      "temperature": 0.1,
      "contextWindow": 131072
    },
    {
      "name": "openai-gpt4o",
      "adapterType": "openai",
      "baseUrl": "https://api.openai.com/v1",
      "model": "gpt-4o",
      "isDefault": false,
      "maxTokens": 16384,
      "temperature": 0.1,
      "contextWindow": 128000,
      "headers": { "Authorization": "Bearer ${OPENAI_API_KEY}" },
      "quirks": {
        "supportsParallelToolCalls": true
      }
    },
    {
      "name": "openai-gpt4o-mini",
      "adapterType": "openai",
      "baseUrl": "https://api.openai.com/v1",
      "model": "gpt-4o-mini",
      "isDefault": false,
      "maxTokens": 16384,
      "temperature": 0.1,
      "contextWindow": 128000,
      "headers": { "Authorization": "Bearer ${OPENAI_API_KEY}" },
      "quirks": {
        "supportsParallelToolCalls": true
      }
    },
    {
      "name": "vllm-deepseek",
      "adapterType": "openai-compatible",
      "baseUrl": "http://gpu-server:8000/v1",
      "model": "deepseek-ai/DeepSeek-Coder-V2-Instruct",
      "isDefault": false,
      "maxTokens": 16384,
      "temperature": 0.0,
      "contextWindow": 131072,
      "headers": { "Authorization": "Bearer sk-local-dev" },
      "quirks": {
        "assembleToolCallDeltas": true,
        "supportsParallelToolCalls": true,
        "stripRequestFields": ["stream_options"]
      }
    },
    {
      "name": "together-llama",
      "adapterType": "openai-compatible",
      "baseUrl": "https://api.together.xyz/v1",
      "model": "meta-llama/Llama-3.1-70B-Instruct-Turbo",
      "isDefault": false,
      "maxTokens": 8192,
      "temperature": 0.1,
      "contextWindow": 131072,
      "headers": { "Authorization": "Bearer ${TOGETHER_API_KEY}" }
    }
  ]
}
```

---

## `mcp-servers.json`

**Path**: `~/.mux/mcp-servers.json`

Defines MCP (Model Context Protocol) tool servers that mux launches on startup. Each server provides additional tools beyond mux's built-in set.

### Full Schema

```json
{
  "servers": [
    {
      "name": "string (required)",
      "command": "string (required)",
      "args": ["string array (default: [])"],
      "env": { "KEY": "VALUE (supports ${ENV_VAR} expansion)" }
    }
  ]
}
```

### Field Reference

#### `name` (string, required)

Unique identifier for this MCP server. Used for tool name prefixing (e.g., tools from a server named `"github"` appear as `github.create_issue`), and for `/mcp remove <name>` in interactive mode.

#### `command` (string, required)

The executable to launch. This must be on `PATH` or an absolute path.

| Common Commands | Use Case |
|----------------|----------|
| `npx` | Node.js-based MCP servers from npm |
| `dotnet` | .NET-based MCP servers |
| `python` | Python-based MCP servers |
| `node` | Direct Node.js execution |

#### `args` (string[], default: `[]`)

Arguments passed to the command.

#### `env` (object, default: `{}`)

Environment variables set for the server process. Supports `${VAR_NAME}` expansion from the host's environment variables.

### Examples

```json
{
  "servers": [
    {
      "name": "github",
      "command": "npx",
      "args": ["-y", "@modelcontextprotocol/server-github"],
      "env": { "GITHUB_TOKEN": "${GITHUB_TOKEN}" }
    },
    {
      "name": "filesystem",
      "command": "npx",
      "args": ["-y", "@modelcontextprotocol/server-filesystem", "/home/user/projects"],
      "env": {}
    },
    {
      "name": "custom-tools",
      "command": "dotnet",
      "args": ["run", "--project", "/path/to/my-mcp-server"],
      "env": {
        "DB_CONNECTION": "${DB_CONNECTION_STRING}"
      }
    }
  ]
}
```

### Server Lifecycle

1. **Startup**: mux launches each configured server as a subprocess via stdio transport
2. **Discovery**: mux calls `tools/list` on each server to discover available tools
3. **Runtime**: Tool calls from the LLM are routed to the appropriate server
4. **Health**: If a server disconnects, mux logs a warning and attempts one lazy reconnect on the next tool call
5. **Shutdown**: All servers are gracefully shut down when mux exits

### Skip MCP Servers

For faster startup or debugging, skip MCP server initialization:

```bash
mux --no-mcp
```

---

## `settings.json`

**Path**: `~/.mux/settings.json`

Global settings that apply across all endpoints and sessions.

### Full Schema

```json
{
  "systemPromptPath": "string or null",
  "defaultApprovalPolicy": "string",
  "toolTimeoutMs": "integer",
  "processTimeoutMs": "integer",
  "contextWindowSafetyMarginPercent": "integer",
  "tokenEstimationRatio": "number",
  "maxAgentIterations": "integer"
}
```

### Field Reference

| Field | Type | Default | Description |
|-------|------|---------|-------------|
| `systemPromptPath` | string or null | `null` | Path to a custom system prompt file. If `null`, checks for `~/.mux/system-prompt.md`, then falls back to the built-in default. Overridden by `--system-prompt` CLI flag. |
| `defaultApprovalPolicy` | string | `"ask"` | Default approval policy for tool calls. Values: `"ask"`, `"auto"`, `"deny"`. Overridden by `--yolo` or `--approval-policy` CLI flags. In non-interactive mode, this is further overridden to `"deny"` unless explicitly set. |
| `toolTimeoutMs` | integer | `30000` | Timeout in milliseconds for built-in tool execution (read_file, edit_file, glob, grep, etc.). |
| `processTimeoutMs` | integer | `120000` | Timeout in milliseconds for `run_process` tool execution. Processes are killed after this timeout. |
| `contextWindowSafetyMarginPercent` | integer | `15` | Percentage of the context window to reserve as a safety margin. When estimated token usage reaches `contextWindow * (1 - margin/100)`, older messages are truncated. Range: 5-50. |
| `tokenEstimationRatio` | number | `3.5` | Characters-per-token ratio for estimating token counts. Lower values are more conservative (assume more tokens per character). Range: 2.0-6.0. |
| `maxAgentIterations` | integer | `25` | Maximum number of LLM call + tool execution cycles in a single agent loop run. Prevents infinite loops. Range: 1-100. |

### Default Values

If `~/.mux/settings.json` does not exist, mux uses these defaults:

```json
{
  "systemPromptPath": null,
  "defaultApprovalPolicy": "ask",
  "toolTimeoutMs": 30000,
  "processTimeoutMs": 120000,
  "contextWindowSafetyMarginPercent": 15,
  "tokenEstimationRatio": 3.5,
  "maxAgentIterations": 25
}
```

---

## `system-prompt.md`

**Path**: `~/.mux/system-prompt.md` (optional)

A custom system prompt that replaces mux's built-in default. Written in plain text or markdown. This prompt is sent as the `system` role message at the start of every conversation.

If this file does not exist and `settings.json.systemPromptPath` is null, mux uses a built-in system prompt focused on coding assistance that includes:
- The working directory
- Available tool descriptions
- Instructions for tool usage patterns
- Code quality guidelines

**Override priority** (highest to lowest):
1. `--system-prompt <path>` CLI flag
2. `settings.json.systemPromptPath`
3. `~/.mux/system-prompt.md` (if exists)
4. Built-in default

---

## Environment Variables

mux recognizes the following environment variables:

| Variable | Description |
|----------|-------------|
| `MUX_CONFIG_DIR` | Override the config directory (default: `~/.mux/`). Useful for testing or running multiple mux configurations. |

Additionally, any environment variable can be referenced in config files using `${VAR_NAME}` syntax:

```json
{
  "headers": { "Authorization": "Bearer ${OPENAI_API_KEY}" },
  "env": { "GITHUB_TOKEN": "${GITHUB_TOKEN}" }
}
```

Expansion happens at config load time. If a referenced variable is not set, mux logs a warning and leaves the value as the literal string `"${VAR_NAME}"` (this will likely cause an auth error at runtime, which is the desired behavior — fail loudly, not silently).

---

## CLI Override Precedence

When the same setting is specified in multiple places, the highest-precedence value wins:

| Priority | Source | Example |
|----------|--------|---------|
| 1 (highest) | CLI flags | `--model gpt-4o`, `--temperature 0.5` |
| 2 | Named endpoint | `--endpoint openai-gpt4o` selects from `endpoints.json` |
| 3 (lowest) | Default endpoint | The endpoint with `isDefault: true` in `endpoints.json` |

**Resolution flow in `SettingsLoader`:**

```
1. Load ~/.mux/endpoints.json
2. If --endpoint <name> given: select that endpoint. Error if not found.
3. Else: select the endpoint where isDefault == true. Error if none found
   (unless --base-url and --model are both provided as CLI flags).
4. Expand environment variables in headers values (${VAR_NAME}).
5. Apply CLI flag overrides: --model, --base-url, --temperature,
   --max-tokens, --adapter-type onto the selected endpoint.
6. Return the fully resolved EndpointConfig.
```

---

## File Locations Summary

| File | Purpose | Required |
|------|---------|----------|
| `~/.mux/endpoints.json` | Model runner endpoint definitions | Yes (or use CLI flags) |
| `~/.mux/mcp-servers.json` | MCP tool server definitions | No |
| `~/.mux/settings.json` | Global settings | No (defaults used) |
| `~/.mux/system-prompt.md` | Custom system prompt | No (built-in default used) |
