# mux Usage Guide

Backend-specific examples for each supported inference provider. For installation and first-run setup, see [GETTING_STARTED.md](GETTING_STARTED.md). For full configuration reference, see [CONFIG.md](CONFIG.md).

---

## Ollama (Local)

[Ollama](https://ollama.ai) runs models locally and exposes an OpenAI-compatible API.

**Pull a model and start Ollama:**

```bash
ollama pull gemma3:4b
ollama serve
```

**endpoints.json:**

```json
{
  "endpoints": [
    {
      "name": "ollama-gemma",
      "adapterType": "ollama",
      "baseUrl": "http://localhost:11434/v1",
      "model": "gemma3:4b",
      "isDefault": true,
      "maxTokens": 8192,
      "temperature": 0.1,
      "contextWindow": 32768
    },
    {
      "name": "ollama-qwen32",
      "adapterType": "ollama",
      "baseUrl": "http://localhost:11434/v1",
      "model": "qwen2.5-coder:32b",
      "isDefault": false,
      "maxTokens": 8192,
      "temperature": 0.1,
      "contextWindow": 32768
    }
  ]
}
```

**Interactive:**

```bash
mux                              # uses default endpoint (ollama-gemma)
mux --endpoint ollama-qwen32     # use the larger model
mux --model codellama:34b        # override model on the default endpoint
```

**Single-shot:**

```bash
mux print --yolo "add error handling to ParseConfig"
mux print --yolo --endpoint ollama-qwen32 "refactor UserService"
```

**Remote Ollama** (GPU server on the network):

```json
{
  "name": "ollama-remote",
  "adapterType": "ollama",
  "baseUrl": "http://gpu-server:11434/v1",
  "model": "qwen2.5-coder:32b",
  "isDefault": false,
  "maxTokens": 8192,
  "temperature": 0.1,
  "contextWindow": 32768
}
```

---

## vLLM

[vLLM](https://vllm.ai) is a high-throughput model serving engine with an OpenAI-compatible API.

**Start vLLM:**

```bash
python -m vllm.entrypoints.openai.api_server \
  --model deepseek-ai/DeepSeek-Coder-V2-Instruct \
  --port 8000 \
  --trust-remote-code
```

**endpoints.json:**

```json
{
  "endpoints": [
    {
      "name": "vllm-deepseek",
      "adapterType": "openai-compatible",
      "baseUrl": "http://localhost:8000/v1",
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
    }
  ]
}
```

**Usage:**

```bash
mux --endpoint vllm-deepseek
mux print --yolo --endpoint vllm-deepseek "refactor UserService to be async"
```

**Notes:**
- Use `adapterType: "openai-compatible"` (not `"vllm"`)
- Some vLLM versions require an API key even locally — use any non-empty string
- If vLLM rejects unknown fields, add them to `quirks.stripRequestFields`

---

## OpenAI

**Set your API key:**

```bash
# Bash / Linux / macOS
export OPENAI_API_KEY="sk-..."

# PowerShell / Windows
$env:OPENAI_API_KEY = "sk-..."
```

**endpoints.json:**

```json
{
  "endpoints": [
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
    }
  ]
}
```

**Usage:**

```bash
mux --endpoint openai-gpt4o
mux print --yolo -e openai-gpt4o "explain the architecture of this project"
mux -e openai-gpt4o --model gpt-4-turbo     # override model on the fly
```

---

## Azure OpenAI

**endpoints.json:**

```json
{
  "endpoints": [
    {
      "name": "azure-gpt4o",
      "adapterType": "openai-compatible",
      "baseUrl": "https://YOUR-RESOURCE.openai.azure.com/openai/deployments/gpt-4o/",
      "model": "gpt-4o",
      "isDefault": false,
      "maxTokens": 16384,
      "temperature": 0.1,
      "contextWindow": 128000,
      "headers": { "Authorization": "Bearer ${AZURE_OPENAI_API_KEY}" },
      "quirks": {
        "supportsParallelToolCalls": true
      }
    }
  ]
}
```

**Usage:**

```bash
mux --endpoint azure-gpt4o
```

---

## Together AI / Groq / Fireworks

Any OpenAI-compatible cloud API works with `adapterType: "openai-compatible"`.

**Together AI:**

```json
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
```

**Groq:**

```json
{
  "name": "groq-llama",
  "adapterType": "openai-compatible",
  "baseUrl": "https://api.groq.com/openai/v1",
  "model": "llama-3.1-70b-versatile",
  "isDefault": false,
  "maxTokens": 8192,
  "temperature": 0.1,
  "contextWindow": 131072,
  "headers": { "Authorization": "Bearer ${GROQ_API_KEY}" }
}
```

**Fireworks:**

```json
{
  "name": "fireworks-llama",
  "adapterType": "openai-compatible",
  "baseUrl": "https://api.fireworks.ai/inference/v1",
  "model": "accounts/fireworks/models/llama-v3p1-70b-instruct",
  "isDefault": false,
  "maxTokens": 8192,
  "temperature": 0.1,
  "contextWindow": 131072,
  "headers": { "Authorization": "Bearer ${FIREWORKS_API_KEY}" }
}
```

---

## LM Studio

[LM Studio](https://lmstudio.ai) runs local models with an OpenAI-compatible API.

**endpoints.json:**

```json
{
  "name": "lmstudio",
  "adapterType": "openai-compatible",
  "baseUrl": "http://localhost:1234/v1",
  "model": "loaded-model",
  "isDefault": false,
  "maxTokens": 8192,
  "temperature": 0.1,
  "contextWindow": 32768
}
```

---

## Ad-Hoc Usage (No Config File)

Pass everything via CLI flags — no `endpoints.json` needed:

```bash
# Ollama
mux --base-url http://localhost:11434/v1 --model gemma3:4b --adapter-type ollama

# OpenAI (set OPENAI_API_KEY env var, then configure headers in endpoints.json)
mux --base-url https://api.openai.com/v1 --model gpt-4o --adapter-type openai

# vLLM
mux --base-url http://localhost:8000/v1 --model deepseek-coder-v2 --adapter-type openai-compatible
```

CLI flags always take precedence over config file values.

---

## Approval Policy

mux requires explicit permission for tool calls. Three policies:

| Flag | Behavior |
|------|----------|
| *(default in interactive)* | Ask for approval on each tool call |
| `--yolo` | Auto-approve everything |
| `--approval-policy deny` | Deny all tool calls (default in `--print` mode) |

In interactive `ask` mode, each tool call prompts `[Y/n/always]`:
- **Y** (or Enter): Approve this call
- **n**: Deny this call
- **always**: Approve all future calls this session

---

## MCP Tool Servers

Define MCP servers in `~/.mux/mcp-servers.json`:

```json
{
  "servers": [
    {
      "name": "github",
      "command": "npx",
      "args": ["-y", "@modelcontextprotocol/server-github"],
      "env": { "GITHUB_TOKEN": "${GITHUB_TOKEN}" }
    }
  ]
}
```

Manage at runtime:

```
/mcp list                              Show connected servers
/mcp add myserver dotnet run -- ...    Add a server for this session
/mcp remove myserver                   Disconnect a server
```

Skip MCP servers for faster startup:

```bash
mux --no-mcp
```

---

## Orchestrator Integration (Armada)

Armada invokes mux as a subprocess in `--print` mode:

```bash
mux print --yolo "implement the feature described in TASK.md"
mux print --yolo --endpoint vllm-deepseek --working-directory /tmp/worktree-abc "fix the bug"
mux print --yolo --system-prompt /path/to/persona.md "do the thing"
mux print --yolo --verbose "debug the failing test"
```

**Output contract:**
- **stdout**: Assistant's final text response only
- **stderr**: Progress heartbeats, errors, verbose logging
- **Exit code 0**: Success
- **Exit code 1**: Error
- **Exit code 2**: Tool call denied
