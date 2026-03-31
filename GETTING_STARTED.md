# Getting Started with mux

## Prerequisites

- .NET 8 SDK or later
- A model runner installed and running separately
- Ollama is the easiest local first-run option

## Install

From source:

```bash
cd c:\code\mux

# Windows
install-tool.bat

# Linux / macOS
chmod +x install-tool.sh
./install-tool.sh
```

Verify:

```bash
mux --version
```

## Pull a Model

Example with Ollama:

```bash
ollama pull qwen2.5-coder:7b
ollama serve
```

## First Run

```bash
mux
```

By default, first run creates `~/.mux/endpoints.json` with a local Ollama endpoint.

If you want an isolated config directory instead:

```bash
# Bash
export MUX_CONFIG_DIR=/tmp/mux-first-run

# PowerShell
$env:MUX_CONFIG_DIR = "C:\\temp\\mux-first-run"
```

Then run `mux` or `mux probe`.

## Verify It Works

Interactive test:

```text
mux> create a file called hello.py that prints "hello world", then read it back to verify. if the file already exists, overwrite it.
```

You should see tool calls such as `write_file` and `read_file`.

## Useful First Commands

Interactive:

```text
/endpoint
/tools
/clear
/exit
```

Single-shot:

```bash
mux print --yolo "read README.md and summarize it"
```

Structured automation:

```bash
mux print --output-format jsonl --yolo "read README.md"
```

Health check:

```bash
mux probe
mux probe --output-format json
```

## Approval Policy

- interactive mode usually asks before tool calls
- `mux print` defaults to denied tool calls unless overridden
- `--yolo` or `--approval-policy auto` enables automatic execution

## Configure More Endpoints

Edit `endpoints.json` in the active config directory to add more backends.

Example:

```json
{
  "endpoints": [
    {
      "name": "ollama-local",
      "adapterType": "ollama",
      "baseUrl": "http://localhost:11434/v1",
      "model": "qwen2.5-coder:7b",
      "isDefault": true
    },
    {
      "name": "openai-gpt4o",
      "adapterType": "openai",
      "baseUrl": "https://api.openai.com/v1",
      "model": "gpt-4o",
      "headers": {
        "Authorization": "Bearer ${OPENAI_API_KEY}"
      }
    }
  ]
}
```

Use one:

```bash
mux --endpoint openai-gpt4o
```

## Next Steps

- [README.md](README.md)
- [USAGE.md](USAGE.md)
- [CONFIG.md](CONFIG.md)
- [TESTING.md](TESTING.md)
