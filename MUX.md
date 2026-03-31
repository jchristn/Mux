# mux Design Notes

This file is retained as a short historical note.

`mux` is a C# CLI AI agent with:
- `Mux.Core` for agent logic, models, adapters, and tools
- `Mux.Cli` for the terminal experience and command surface
- `Test.Xunit`, `Test.Automated`, and `Test.Shared` for automated coverage

Current authoritative user-facing documentation lives in:
- [README.md](README.md)
- [GETTING_STARTED.md](GETTING_STARTED.md)
- [USAGE.md](USAGE.md)
- [CONFIG.md](CONFIG.md)
- [TESTING.md](TESTING.md)

Current orchestration-related capabilities include:
- `mux print` for non-interactive execution
- `mux print --output-format jsonl` for structured event streaming
- `mux probe` for backend and config health validation
- `MUX_CONFIG_DIR` for config isolation
