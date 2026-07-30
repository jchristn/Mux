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
install-tool.bat net8.0

# Linux / macOS
chmod +x install-tool.sh
./install-tool.sh
./install-tool.sh net8.0
```

The install scripts accept an optional target framework argument. They default to `net10.0` when a .NET 10 SDK is installed and otherwise fall back to `net8.0`.

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

Web retrieval test:

```text
mux> retrieve https://example.com and summarize the returned text
```

You should see a `web_retrieve` tool call. The first retrieval may download the Playwright browser used for headless page rendering.

If your enterprise network intercepts TLS and mux reports `SELF_SIGNED_CERT_IN_CHAIN`, retry with `mux --ignore-cert-errors` or `mux --insecure`. This disables certificate validation for mux-owned network requests and prints a warning while active.

## Useful First Commands

Interactive:

```text
/endpoint
/model
/search
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
- `mux print` and `mux probe` reject `--approval-policy ask`
- MCP tool execution inside the TUIKit interactive UI is not yet wired in v0.3.0; built-in tools work in interactive, and MCP servers remain configurable in `mcp-servers.json`

## Configure More Endpoints

Edit `endpoints.json` in the active config directory to add more backends.

Example:

```json
{
  "endpoints": [
    {
      "name": "ollama-local",
      "adapterType": "ollama",
      "baseUrl": "http://localhost:11434",
      "model": "qwen2.5-coder:7b",
      "isDefault": true
    },
    {
      "name": "openai-gpt4o",
      "adapterType": "openai",
      "baseUrl": "https://api.openai.com/v1",
      "model": "gpt-4o",
      "maxAgentIterations": null,
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

## Configure Web Search

`web_retrieve` works without provider configuration and fetches known URLs through a headless browser. Public web discovery uses `web_search`, which requires an external provider.

Interactive setup:

```text
/search add
```

The wizard supports Tavily and You.com. Store API keys directly or as environment-variable references such as `${TAVILY_API_KEY}` or `${YOU_API_KEY}`. After setup, prompts can combine discovery and retrieval:

```text
mux> search the web for mux GitHub releases, then retrieve the most relevant result
```

## Next Steps

- [README.md](README.md)
- [USAGE.md](USAGE.md)
- [CONFIG.md](CONFIG.md)
- [TESTING.md](TESTING.md)
