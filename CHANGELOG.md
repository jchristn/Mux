# Changelog

All notable changes to mux will be documented in this file.

## v0.1.0 (2026-03-30)

Initial release.

### Features

- **Interactive REPL** with multi-line input (Shift+Enter / Ctrl+Enter), slash commands, and streaming responses
- **Single-shot print mode** (`mux print`) for scripting, CI/CD, and orchestrator integration
- **Backend adapters**: Ollama, OpenAI, and Generic OpenAI-compatible (vLLM, Azure, Groq, Together AI, Fireworks, LM Studio)
- **11 built-in tools**: `read_file`, `write_file`, `edit_file`, `multi_edit`, `delete_file`, `file_metadata`, `list_directory`, `manage_directory`, `glob`, `grep`, `run_process`
- **MCP tool server integration** via Voltaic — connect GitHub, databases, or custom tool servers
- **Configurable endpoints** with per-model settings, API keys, bearer tokens, timeouts, and backend quirks
- **Approval policies**: Ask (interactive prompt), AutoApprove (`--yolo`), Deny (non-interactive default)
- **Retry logic** with exponential backoff for transient failures (5xx, timeouts, connection refused)
- **Malformed tool call recovery** for models that output tool calls as text instead of structured responses
- **Context window management** with token estimation and automatic conversation trimming
- **Graceful shutdown** with Ctrl+C cancellation (first cancels generation, second exits)
- **Working directory control** via `--working-directory` for sandboxed tool execution
- **Custom system prompts** via `~/.mux/system-prompt.md` or `--system-prompt` flag
- **First-run setup** automatically creates `~/.mux/` with default Ollama endpoint configuration
- **Dotnet tool packaging** — install as `mux` via `dotnet tool install`

### Supported Backends

- Ollama (local and remote)
- OpenAI (GPT-4o, GPT-4o-mini, etc.)
- vLLM
- Azure OpenAI
- Together AI
- Groq
- Fireworks
- LM Studio
- Any OpenAI-compatible endpoint

### Testing

- 83 unit tests across settings, adapters, tools, approval handler, and agent loop
- Automated integration tests with MockHttpServer
- Builds and tests on both .NET 8.0 and .NET 10.0
