# Getting Started with mux

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download) or later
- [Ollama](https://ollama.ai) installed and running (for local models)

## Install

mux is not yet published to NuGet. Install from source using the provided scripts:

**Windows:**

```cmd
cd c:\code\mux
install-tool.bat
```

**Linux / macOS:**

```bash
cd ~/code/mux
chmod +x install-tool.sh
./install-tool.sh
```

Or manually:

```bash
cd c:\code\mux
dotnet pack src/Mux.Cli/Mux.Cli.csproj --configuration Release
dotnet tool install -g --add-source src/Mux.Cli/bin/Release Mux.Cli
```

Verify the install:

```bash
mux --version
```

To reinstall after code changes:

| Windows | Linux / macOS |
|---------|---------------|
| `reinstall-tool.bat` | `./reinstall-tool.sh` |

To uninstall:

| Windows | Linux / macOS |
|---------|---------------|
| `remove-tool.bat` | `./remove-tool.sh` |

## Pull a Model

If you don't have a model yet:

```bash
ollama pull qwen2.5-coder:7b
```

Make sure Ollama is running:

```bash
ollama serve
```

## First Run

Just type:

```bash
mux
```

On first run, mux creates `~/.mux/` with a default `endpoints.json` pointing to Ollama at `localhost:11434` with `qwen2.5-coder:7b`. You can start chatting immediately.

## Verify It Works

Try this prompt to confirm the LLM and tools are working:

```
mux> create a file called hello.py that prints "hello world", then read it back to verify. if the file already exists, overwrite it.
```

You should see `write_file` and `read_file` tool calls execute, the file created on disk, and the contents echoed back. If you're not running with `--yolo`, type `always` at the first approval prompt to auto-approve for the session.

## Interactive Commands

Inside the REPL:

```
mux> hello, what can you do?
```

Multi-line input: press **Shift+Enter** or **Ctrl+Enter** to insert a newline. **Enter** submits.

```
mux> write a function that
...> takes a list of integers
...> and returns the sum
```

Slash commands:

```
/model              List available endpoints
/model ollama-big   Switch to a different endpoint
/tools              List available tools
/clear              Reset conversation
/help               Show all commands
/exit               Quit
```

## Single-Shot Mode

Run a prompt and exit:

```bash
mux print --yolo "read README.md and summarize it"
```

The `--yolo` flag auto-approves tool calls (file reads, writes, shell commands). Without it, tool calls are denied in non-interactive mode.

## Configure Endpoints

Edit `~/.mux/endpoints.json` to add more backends:

```json
{
  "endpoints": [
    {
      "name": "ollama-local",
      "adapterType": "ollama",
      "baseUrl": "http://localhost:11434/v1",
      "model": "gemma3:4b",
      "isDefault": true,
      "maxTokens": 8192,
      "temperature": 0.1,
      "contextWindow": 32768
    },
    {
      "name": "ollama-big",
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

Switch between endpoints at runtime:

```bash
mux --endpoint ollama-big
```

Or inside the REPL:

```
/model ollama-big
```

## Ad-Hoc Endpoint (No Config File)

You don't need `endpoints.json` at all:

```bash
mux --base-url http://localhost:11434/v1 --model llama3.1:70b --adapter-type ollama
```

## Next Steps

- [USAGE.md](USAGE.md) — Backend-specific examples (Ollama, vLLM, OpenAI, Azure, etc.)
- [CONFIG.md](CONFIG.md) — Full configuration reference for `~/.mux/` files
- `mux --help` — All CLI flags and options
