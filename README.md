# mux

AI agent for local and remote LLMs.

mux provides a Claude Code / Codex-like experience over user-specified model runner backends (Ollama, vLLM, OpenAI, or any OpenAI-compatible endpoint).

## Quick Start

```bash
# Windows
cd c:\code\mux
install-tool.bat

# Linux / macOS
cd ~/code/mux
./install-tool.sh

# Pull a model and go
ollama pull gemma3:4b
mux
```

See [GETTING_STARTED.md](GETTING_STARTED.md) for the full walkthrough.

Scripts: `install-tool`, `reinstall-tool`, `remove-tool` (`.bat` and `.sh` variants).

## Documentation

- [GETTING_STARTED.md](GETTING_STARTED.md) — Install, first run, basic usage
- [USAGE.md](USAGE.md) — Backend examples (Ollama, vLLM, OpenAI, Azure, Groq, etc.)
- [CONFIG.md](CONFIG.md) — Full configuration reference for `~/.mux/` files

## License

MIT — (c) 2026 Joel Christner
