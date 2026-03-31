<p align="center">
  <img src="https://raw.githubusercontent.com/jchristn/Mux/main/assets/icon-black.png" width="256" height="256" alt="mux">
</p>

<h1 align="center">mux</h1>

<p align="center">
  <em>Your AI agent, your models, your infrastructure.</em>
</p>

<p align="center">
  <a href="https://github.com/jchristn/Mux/blob/main/LICENSE.md"><img src="https://img.shields.io/badge/license-MIT-blue.svg" alt="MIT License"></a>
  <a href="https://dotnet.microsoft.com"><img src="https://img.shields.io/badge/.NET-8.0%20%7C%2010.0-purple.svg" alt=".NET 8 / 10"></a>
  <img src="https://img.shields.io/badge/version-0.1.0%20alpha-orange.svg" alt="v0.1.0-alpha">
</p>

> **v0.1.0 — Alpha Release**
> This is an early alpha. APIs, interfaces, configuration formats, tool schemas, and CLI behavior are all subject to change. Feedback welcome via [issues](https://github.com/jchristn/Mux/issues) and [discussions](https://github.com/jchristn/Mux/discussions).

---

## What is mux?

mux is a CLI AI agent that gives you a Claude Code / Codex-like experience using **any model** on **any backend** you choose. Run it against Ollama on your laptop, vLLM on a GPU server, OpenAI's API, Azure, Groq, Together AI, or any OpenAI-compatible endpoint.

mux reads your files, writes code, runs commands, searches your codebase, and manages your project — all through a conversational REPL or single-shot commands. You own the models. You own the data. Nothing leaves your machine unless you point it at a cloud API.

**mux does not install, configure, or manage model runners.** You bring your own inference backend (Ollama, vLLM, OpenAI, etc.) — mux connects to it. See the [Quick Start](#quick-start) section for setup.

## Why mux?

- **Backend-agnostic** — one tool for Ollama, vLLM, OpenAI, Azure, Groq, Together AI, LM Studio, and any OpenAI-compatible API. Switch between them with a single command.
- **Full tool calling** — file read/write/edit/delete, directory management, glob, grep, process execution. The agent doesn't just talk — it works.
- **MCP extensible** — connect any Model Context Protocol server for GitHub, databases, custom APIs, or anything else. Tools from MCP servers appear alongside built-in tools automatically.
- **Interactive and scriptable** — use the REPL for interactive sessions with multi-line input, or pipe prompts through `mux print` for CI/CD, automation, and orchestrator integration.
- **Privacy-first** — local models stay local. No telemetry, no cloud dependency, no accounts. Point mux at `localhost` and everything stays on your machine.
- **Configurable** — named endpoints with per-model settings, approval policies, custom system prompts, backend quirks, and timeout control. All in simple JSON files under `~/.mux/`.

## Quick Start

### Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download) or later
- A model runner installed and running separately — mux does not manage model runners

### Step 1: Install a model runner

mux connects to model runners that you install and manage independently. The default configuration assumes [Ollama](https://ollama.ai) running locally. Install Ollama from [ollama.ai](https://ollama.ai), then pull the recommended model:

```bash
ollama pull qwen2.5-coder:7b
```

Verify the model is available:

```bash
ollama ls
```

You should see:

```
NAME                  ID              SIZE      MODIFIED
qwen2.5-coder:7b     2b0496514e35    4.7 GB    just now
```

Make sure Ollama is running (`ollama serve` if it's not already started as a service).

### Step 2: Install mux

```bash
git clone https://github.com/jchristn/Mux.git
cd Mux

# Windows
install-tool.bat

# Linux / macOS
chmod +x install-tool.sh
./install-tool.sh
```

### Step 3: Run

```bash
mux
```

On first run, mux creates `~/.mux/endpoints.json` configured to connect to Ollama at `localhost:11434` with `qwen2.5-coder:7b`. No additional configuration needed if you followed the steps above.

See [GETTING_STARTED.md](https://github.com/jchristn/Mux/blob/main/GETTING_STARTED.md) for the full walkthrough.

### Using a different model runner?

mux works with any OpenAI-compatible API. See [USAGE.md](https://github.com/jchristn/Mux/blob/main/USAGE.md) for backend-specific examples (vLLM, OpenAI, Azure, Groq, Together AI, LM Studio, etc.).

## Verify It Works

After install, try this prompt to confirm the LLM and tools are working end-to-end:

```
mux> create a file called hello.py that prints "hello world", then read it back to verify. if the file already exists, overwrite it.
```

You should see `write_file` and `read_file` tool calls, the file created on disk, and the contents read back. If you're prompted for approval, type `always` to auto-approve for the session, or start mux with `--yolo` to skip prompts.

## What Can It Do?

```
mux> create a Python script that reads CSV files and outputs JSON
mux> find all TODO comments in this project
mux> read the README and suggest improvements
mux> refactor the UserService class to use dependency injection
mux> run the test suite and fix any failures
```

### Built-In Tools

| Tool | Description |
|------|-------------|
| `read_file` | Read file contents with line numbers |
| `write_file` | Create or overwrite files |
| `edit_file` | Search-and-replace edits with conflict detection |
| `multi_edit` | Atomic multi-edit within a single file |
| `delete_file` | Delete files |
| `file_metadata` | Read file/directory size, timestamps, attributes |
| `list_directory` | List directory contents |
| `manage_directory` | Create, delete, or rename directories |
| `glob` | Find files by pattern |
| `grep` | Search file contents with regex |
| `run_process` | Execute shell commands |

### Supported Backends

mux connects to model runners that you install and manage separately. It does not download, install, or serve models.

| Backend | Adapter Type | Notes |
|---------|-------------|-------|
| [Ollama](https://ollama.ai) | `ollama` | Default. Local inference, free, open source. |
| [OpenAI](https://openai.com) | `openai` | GPT-4o, GPT-4o-mini. Requires API key. |
| [vLLM](https://vllm.ai) | `openai-compatible` | High-throughput self-hosted serving. |
| [Azure OpenAI](https://azure.microsoft.com/en-us/products/ai-services/openai-service) | `openai-compatible` | Enterprise Azure deployments. |
| [Together AI](https://together.ai) | `openai-compatible` | Cloud inference. Requires API key. |
| [Groq](https://groq.com) | `openai-compatible` | Fast cloud inference. Requires API key. |
| [Fireworks](https://fireworks.ai) | `openai-compatible` | Cloud inference. Requires API key. |
| [LM Studio](https://lmstudio.ai) | `openai-compatible` | Local GUI + API server. |
| Any OpenAI-compatible API | `openai-compatible` | Anything that speaks the OpenAI chat completions format. |

## CLI Usage

```
mux [OPTIONS] [prompt]                Interactive REPL (default)
mux --print [OPTIONS] <prompt>        Single-shot mode
echo "prompt" | mux --print           Read prompt from stdin
```

### Options

| Option | Short | Description |
|--------|-------|-------------|
| `--help` | `-h`, `/?` | Show help message and exit |
| `--version` | `/version` | Show version and exit |
| `--print` | `-p` | Single-shot: process prompt, print result, exit |
| `--endpoint <name>` | `-e` | Named endpoint from `~/.mux/endpoints.json` |
| `--model <name>` | `-m` | Override model name |
| `--base-url <url>` | | Override base URL |
| `--adapter-type <type>` | | Adapter: `ollama`, `openai`, `vllm`, `openai-compatible` |
| `--temperature <float>` | | Override temperature (0.0–2.0) |
| `--max-tokens <int>` | | Override max output tokens |
| `--yolo` | | Auto-approve all tool calls |
| `--approval-policy <policy>` | | `ask`, `auto`, or `deny` (default: `ask` if TTY, `deny` otherwise) |
| `--working-directory <path>` | `-w` | Set working directory for tool execution |
| `--system-prompt <path>` | | Path to a custom system prompt file |
| `--no-mcp` | | Skip MCP server initialization |
| `--verbose` | `-v` | Emit detailed progress to stderr |

### Interactive Commands

```
mux> hello, what can you do?

mux> write a function that       # Shift+Enter for multi-line input
...> takes a list of integers
...> and returns the sum

/endpoint                         # list configured endpoints
/endpoint ollama-big              # switch to a named endpoint
/tools                            # list available tools
/mcp list|add|remove              # manage MCP servers
/clear                            # reset conversation
/system [text]                    # view or set system prompt
/?                                # show all commands
/exit                             # quit
```

### Single-Shot Mode

```bash
mux --print --yolo "read README.md and summarize it"
mux -p -e openai-gpt4o "explain the architecture"
mux --base-url http://localhost:11434/v1 --model llama3.1:70b
echo "refactor AuthService" | mux --print --yolo
```

## Configuration

All config lives in `~/.mux/`. mux tells the model runner what model to use and how to call it — but you must install and run the model runner yourself.

| File | Purpose |
|------|---------|
| `endpoints.json` | Model runner connection details (URL, model, adapter type, auth) |
| `mcp-servers.json` | MCP tool server definitions |
| `settings.json` | Global settings (approval policy, timeouts, etc.) |
| `system-prompt.md` | Custom system prompt (optional) |

Example endpoint:

```json
{
  "name": "ollama-local",
  "adapterType": "ollama",
  "baseUrl": "http://localhost:11434/v1",
  "model": "qwen2.5-coder:7b",
  "isDefault": true,
  "maxTokens": 8192,
  "temperature": 0.1,
  "timeoutMs": 120000
}
```

See [CONFIG.md](https://github.com/jchristn/Mux/blob/main/CONFIG.md) for the full reference.

## Documentation

| Document | Description |
|----------|-------------|
| [GETTING_STARTED.md](https://github.com/jchristn/Mux/blob/main/GETTING_STARTED.md) | Install, first run, basic usage |
| [USAGE.md](https://github.com/jchristn/Mux/blob/main/USAGE.md) | Backend-specific examples and configuration |
| [CONFIG.md](https://github.com/jchristn/Mux/blob/main/CONFIG.md) | Full configuration reference |
| [TESTING.md](https://github.com/jchristn/Mux/blob/main/TESTING.md) | Running unit and integration tests |
| [CHANGELOG.md](https://github.com/jchristn/Mux/blob/main/CHANGELOG.md) | Version history and release notes |

## Scripts

| Script | Description |
|--------|-------------|
| `install-tool.bat` / `.sh` | Build and install `mux` as a .NET global tool |
| `reinstall-tool.bat` / `.sh` | Remove, rebuild, and reinstall |
| `remove-tool.bat` / `.sh` | Uninstall the `mux` tool |

## Issues and Discussions

Found a bug? Have a feature request? Want to discuss how to use mux with a specific backend?

- [Open an issue](https://github.com/jchristn/Mux/issues)
- [Start a discussion](https://github.com/jchristn/Mux/discussions)

## License

[MIT](https://github.com/jchristn/Mux/blob/main/LICENSE.md) — (c) 2026 Joel Christner
