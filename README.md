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
  <img src="https://img.shields.io/badge/version-0.1.0-green.svg" alt="v0.1.0">
</p>

---

## What is mux?

mux is a CLI AI agent that gives you a Claude Code / Codex-like experience using **any model** on **any backend** you choose. Run it against Ollama on your laptop, vLLM on a GPU server, OpenAI's API, Azure, Groq, Together AI, or any OpenAI-compatible endpoint.

mux reads your files, writes code, runs commands, searches your codebase, and manages your project — all through a conversational REPL or single-shot commands. You own the models. You own the data. Nothing leaves your machine unless you point it at a cloud API.

## Why mux?

- **Backend-agnostic** — one tool for Ollama, vLLM, OpenAI, Azure, Groq, Together AI, LM Studio, and any OpenAI-compatible API. Switch between them with a single command.
- **Full tool calling** — file read/write/edit/delete, directory management, glob, grep, process execution. The agent doesn't just talk — it works.
- **MCP extensible** — connect any Model Context Protocol server for GitHub, databases, custom APIs, or anything else. Tools from MCP servers appear alongside built-in tools automatically.
- **Interactive and scriptable** — use the REPL for interactive sessions with multi-line input, or pipe prompts through `mux print` for CI/CD, automation, and orchestrator integration.
- **Privacy-first** — local models stay local. No telemetry, no cloud dependency, no accounts. Point mux at `localhost` and everything stays on your machine.
- **Configurable** — named endpoints with per-model settings, approval policies, custom system prompts, backend quirks, and timeout control. All in simple JSON files under `~/.mux/`.

## Quick Start

**Prerequisites:** [.NET 8 SDK](https://dotnet.microsoft.com/download) or later, [Ollama](https://ollama.ai) (for local models)

```bash
# Clone and install
git clone https://github.com/jchristn/Mux.git
cd Mux

# Windows
install-tool.bat

# Linux / macOS
chmod +x install-tool.sh
./install-tool.sh
```

Pull a model and start:

```bash
ollama pull qwen2.5-coder:7b
mux
```

That's it. mux creates `~/.mux/endpoints.json` on first run with sensible defaults.

See [GETTING_STARTED.md](https://github.com/jchristn/Mux/blob/main/GETTING_STARTED.md) for the full walkthrough.

## Verify It Works

After install, try this prompt to confirm the LLM and tools are working end-to-end:

```
mux> create a file called hello.py that prints "hello world", then read it back to verify. if the file already exists, overwrite it.
```

You should see `write_file` and `read_file` tool calls, the file created on disk, and the contents read back.

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

| Backend | Adapter Type |
|---------|-------------|
| [Ollama](https://ollama.ai) | `ollama` |
| [OpenAI](https://openai.com) | `openai` |
| [vLLM](https://vllm.ai) | `openai-compatible` |
| [Azure OpenAI](https://azure.microsoft.com/en-us/products/ai-services/openai-service) | `openai-compatible` |
| [Together AI](https://together.ai) | `openai-compatible` |
| [Groq](https://groq.com) | `openai-compatible` |
| [Fireworks](https://fireworks.ai) | `openai-compatible` |
| [LM Studio](https://lmstudio.ai) | `openai-compatible` |
| Any OpenAI-compatible API | `openai-compatible` |

## Interactive Mode

```
mux> hello, what can you do?

mux> write a function that       # Shift+Enter for multi-line input
...> takes a list of integers
...> and returns the sum

/model                            # list configured endpoints
/model ollama-big                 # switch endpoint
/tools                            # list available tools
/clear                            # reset conversation
/?                                # show all commands
/exit                             # quit
```

## Single-Shot Mode

```bash
mux print --yolo "read README.md and summarize it"
mux print --yolo --endpoint openai-gpt4o "explain the architecture"
echo "refactor AuthService" | mux print --yolo
```

## Configuration

All config lives in `~/.mux/`:

| File | Purpose |
|------|---------|
| `endpoints.json` | Model runner endpoints (Ollama, OpenAI, etc.) |
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
