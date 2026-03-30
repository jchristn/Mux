# MUX Implementation Plan

> **Version**: v0.1.0
> **Status**: Draft
> **Last Updated**: 2026-03-29

MUX is a C# CLI coding agent that provides a Claude Code / Codex-like experience over user-specified model runner backends (Ollama, vLLM, OpenAI, or any OpenAI-compatible endpoint). It runs interactively on the terminal or is invoked by an orchestrator like Armada.

See also:
- **[USAGE.md](USAGE.md)** — CLI usage guide with examples for Ollama, vLLM, and OpenAI
- **[CONFIG.md](CONFIG.md)** — Full configuration reference for `~/.mux/` files

This plan is the consensus output of a structured technical debate. Every decision below is grounded in the existing ecosystem (Armada, Voltaic, Verbex, Lattice) and the dual-use requirement: interactive CLI + orchestrator-driven execution.

---

## Table of Contents

- [Architecture Overview](#architecture-overview)
- [Project Structure](#project-structure)
- [Solution and Build Setup](#solution-and-build-setup)
- [Core Design Decisions](#core-design-decisions)
- [Configuration](#configuration)
- [CLI Invocation Contract](#cli-invocation-contract)
- [Phase 1: Walking Skeleton + Tool Execution](#phase-1-walking-skeleton--tool-execution)
- [Phase 2: MCP Integration](#phase-2-mcp-integration)
- [Phase 3: Interactive Polish](#phase-3-interactive-polish)
- [Phase 4: Orchestrator Integration](#phase-4-orchestrator-integration)
- [Phase 5: Hardening](#phase-5-hardening)
- [Testing Strategy](#testing-strategy)
- [Help Menu (`mux --help`)](#help-menu-mux---help)
- [Risks and Mitigations](#risks-and-mitigations)

---

## Architecture Overview

MUX is a **thin, stateless agent loop**. The execution model is:

```
prompt -> LLM call -> tool execution -> append to context -> repeat
```

Both interactive and orchestrator modes use the same loop. The only difference is the I/O adapter: Spectre.Console REPL for interactive, stdout/stderr for orchestrator.

The core loop emits `IAsyncEnumerable<AgentEvent>` — a structured event stream that drives terminal rendering, stderr heartbeats, and deterministic test assertions. Consumers never parse raw strings; they subscribe to typed events.

### High-Level Component Diagram

```
                    +-----------+
                    |  Mux.Cli  |  Spectre.Console REPL / --print mode
                    +-----+-----+
                          |
                   IAsyncEnumerable<AgentEvent>
                          |
                    +-----+-----+
                    | AgentLoop |  Core execution loop
                    +-----+-----+
                     /          \
          +---------+--+   +----+-----------+
          | LlmClient  |   | ToolRegistry   |
          +-----+------+   +----+-----------+
                |                |         \
        +-------+------+   Built-in    Voltaic
        |IBackendAdapter|   Tools      McpClient
        +-------+------+
           /         \
    OllamaAdapter  OpenAiAdapter  GenericOpenAiAdapter
```

---

## Project Structure

```
c:\code\mux\
  MUX.md                          # Original design brief
  MUX_PLAN.md                     # This file
  README.md
  CHANGELOG.md
  LICENSE.md                      # MIT, (c)2026 Joel Christner
  .gitignore
  src/
    Mux.sln
    Directory.Build.props          # Shared build properties (version, author, targets)
    Mux.Core/
      Mux.Core.csproj
      Models/
        EndpointConfig.cs          # Single endpoint definition
        BackendQuirks.cs           # Per-backend behavioral flags
        ConversationMessage.cs     # Role + content + tool calls
        ToolCall.cs                # Tool invocation from LLM
        ToolResult.cs              # Tool execution result (structured JSON)
        MuxSettings.cs             # Global settings model
        McpServerConfig.cs         # MCP server definition
      Enums/
        RoleEnum.cs                # System, User, Assistant, Tool
        ApprovalPolicyEnum.cs      # Ask, AutoApprove, Deny
        AdapterTypeEnum.cs         # Ollama, Vllm, OpenAi, OpenAiCompatible
        AgentEventTypeEnum.cs      # Event type discriminator
      Llm/
        LlmClient.cs              # Orchestrates request/response through adapter
        IBackendAdapter.cs         # Backend adapter interface (3 methods)
        OllamaAdapter.cs           # Ollama-specific request shaping + response normalization
        OpenAiAdapter.cs           # OpenAI direct API (api.openai.com) with bearer auth
        GenericOpenAiAdapter.cs    # Generic OpenAI-compatible adapter
      Tools/
        IToolExecutor.cs           # Interface: execute tool call, return ToolResult
        BuiltInToolRegistry.cs     # Registers and routes built-in tools
        McpToolManager.cs          # Voltaic MCP client lifecycle + tool routing
        Tools/
          ReadFileTool.cs
          WriteFileTool.cs
          EditFileTool.cs
          MultiEditTool.cs
          ListDirectoryTool.cs
          GlobTool.cs
          GrepTool.cs
          RunProcessTool.cs
      Agent/
        AgentLoop.cs               # Core loop: prompt -> LLM -> tools -> repeat
        AgentEvent.cs              # Event base class + concrete event types
        ApprovalHandler.cs         # Ask/AutoApprove/Deny logic
      Settings/
        SettingsLoader.cs          # Load from ~/.mux/ (endpoints, mcp-servers, settings)
        Defaults.cs                # Default system prompt, default quirks presets
    Mux.Cli/
      Mux.Cli.csproj              # PackAsTool=true, ToolCommandName=mux
      Program.cs                   # Entry point, CLI argument parsing
      Commands/
        InteractiveCommand.cs      # REPL mode (default)
        PrintCommand.cs            # --print single-shot mode
      Rendering/
        EventRenderer.cs           # Spectre.Console rendering of AgentEvent stream
        MarkdownRenderer.cs        # Markdown-to-terminal rendering
        ToolCallRenderer.cs        # Tool call approval prompts + result display
  test/
    Test.Shared/
      Test.Shared.csproj
      TestSuite.cs                 # Base test suite (mirrors Armada.Test.Common.TestSuite)
      TestRunner.cs                # Test executor
      TestResult.cs                # Pass/fail/skip tracking
      MockHttpServer.cs            # Fake OpenAI-compatible endpoint for tests
      TestMcpServer.cs             # Voltaic McpServer test fixture
    Test.Xunit/
      Test.Xunit.csproj
      Llm/
        LlmClientTests.cs
        OllamaAdapterTests.cs
        OpenAiAdapterTests.cs
        GenericOpenAiAdapterTests.cs
      Tools/
        ReadFileToolTests.cs
        WriteFileToolTests.cs
        EditFileToolTests.cs
        MultiEditToolTests.cs
        GlobToolTests.cs
        GrepToolTests.cs
        RunProcessToolTests.cs
      Agent/
        AgentLoopTests.cs
        ApprovalHandlerTests.cs
      Settings/
        SettingsLoaderTests.cs
        EndpointConfigTests.cs
    Test.Automated/
      Test.Automated.csproj
      Program.cs                   # Automated test runner entry point
      Suites/
        SingleTurnTests.cs         # Basic prompt -> response
        ToolUseTests.cs            # Agent uses tools to complete tasks
        MultiEditTests.cs          # Atomic multi-edit scenarios
        McpIntegrationTests.cs     # MCP server tool discovery + execution
        PrintModeTests.cs          # Orchestrator invocation pattern
        ApprovalPolicyTests.cs     # --yolo, --approval-policy behavior
        EndpointSwitchingTests.cs  # --endpoint, --model overrides
```

---

## Solution and Build Setup

### Directory.Build.props

```xml
<Project>
  <PropertyGroup>
    <TargetFrameworks>net8.0;net10.0</TargetFrameworks>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <Version>0.1.0</Version>
    <Authors>jchristn</Authors>
    <Company>Joel Christner</Company>
    <Copyright>(c)2026 Joel Christner</Copyright>
    <NoWarn>CS1591;CS1587;CS1572;IDE0063</NoWarn>
  </PropertyGroup>
</Project>
```

### Mux.Cli.csproj (dotnet tool packaging)

```xml
<PropertyGroup>
  <OutputType>Exe</OutputType>
  <PackAsTool>true</PackAsTool>
  <ToolCommandName>mux</ToolCommandName>
  <PackageId>Mux.Cli</PackageId>
</PropertyGroup>
```

### Key NuGet Dependencies

| Package | Project | Purpose |
|---------|---------|---------|
| `Spectre.Console` | Mux.Cli | Terminal rendering, markup, live display |
| `Spectre.Console.Cli` | Mux.Cli | CLI argument parsing, command routing |
| `Voltaic` | Mux.Core | MCP client (stdio transport for tool servers) |
| `System.Text.Json` | Mux.Core | JSON serialization (already in framework) |
| `xunit` | Test.Xunit | Unit test framework |

---

## Core Design Decisions

These decisions are settled. They are not open for renegotiation without new evidence.

### 1. Backend Adapter Seam (not just quirk flags)

Backend differences are **translation differences**, not just policy flags. The adapter interface isolates request shaping, streaming assembly, and response normalization.

```csharp
/// <summary>
/// Adapter interface for backend-specific request/response translation.
/// </summary>
public interface IBackendAdapter
{
    /// <summary>Build an HTTP request from MUX-native inputs.</summary>
    HttpRequestMessage BuildRequest(
        List<ConversationMessage> messages,
        List<ToolDefinition> tools,
        EndpointConfig endpoint);

    /// <summary>Read streaming SSE chunks and yield MUX-native events.</summary>
    IAsyncEnumerable<AgentEvent> ReadStreamingEvents(
        Stream responseStream,
        CancellationToken cancellationToken);

    /// <summary>Normalize a non-streaming JSON response into MUX-native shape.</summary>
    ConversationMessage NormalizeFinalResponse(JsonElement responseBody);
}
```

**Ship three adapters in v0.1.0:**
- `OllamaAdapter` — handles Ollama's tool-call assembly, `num_ctx` passthrough, and finish-reason quirks
- `OpenAiAdapter` — OpenAI direct API (`api.openai.com`), bearer token auth, full tool-calling support including parallel tool calls and structured outputs
- `GenericOpenAiAdapter` — standard OpenAI-compatible behavior, works for vLLM, LM Studio, llama.cpp, and other self-hosted runners

Adapter selection is explicit via `adapterType` field in `endpoints.json`. No auto-detection.

**Authentication**: The `apiKey` field in each endpoint configuration is sent as a `Bearer` token in the `Authorization` header. For OpenAI direct, this is required. For Ollama and local runners, it is typically null. The `apiKey` field also supports environment variable expansion (`${OPENAI_API_KEY}`) so secrets are not stored in plaintext config files.

`BackendQuirks` survives as a data object consumed by adapters:

```csharp
/// <summary>
/// Per-backend behavioral flags, consumed by IBackendAdapter implementations.
/// </summary>
public class BackendQuirks
{
    /// <summary>Whether tool call chunks arrive as deltas requiring assembly.</summary>
    public bool AssembleToolCallDeltas { get; set; } = true;

    /// <summary>Whether the backend supports parallel tool calls.</summary>
    public bool SupportsParallelToolCalls { get; set; } = false;

    /// <summary>Whether tool result content must be a string (not structured).</summary>
    public bool RequiresToolResultContentAsString { get; set; } = false;

    /// <summary>Default finish reason when backend omits it.</summary>
    public string DefaultFinishReason { get; set; } = "stop";

    /// <summary>Fields to strip from requests (backends that reject unknown fields).</summary>
    public List<string> StripRequestFields { get; set; } = new List<string>();
}
```

### 2. Approval Policy Is Explicit, Never TTY-Inferred

Approval policy is a **safety contract**, not a UI detail.

```csharp
public enum ApprovalPolicyEnum
{
    Ask = 0,          // Prompt user for each tool call (interactive default)
    AutoApprove = 1,  // Execute all tool calls without prompting (--yolo)
    Deny = 2          // Reject all tool calls that need approval (non-interactive default)
}
```

- `--yolo` maps to `AutoApprove`
- `--approval-policy ask|auto|deny` for fine-grained control
- Interactive mode defaults to `Ask`
- Non-interactive mode (piped stdin, `--print`) defaults to `Deny` — forcing orchestrators to explicitly pass `--yolo` or `--approval-policy auto`
- Orchestrators must be explicit. No silent auto-approval.

### 3. Evented Core Loop

The agent loop yields `IAsyncEnumerable<AgentEvent>`:

```csharp
public abstract class AgentEvent
{
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
    public AgentEventTypeEnum EventType { get; set; }
}

public class AssistantTextEvent : AgentEvent
{
    public string Text { get; set; }
}

public class ToolCallProposedEvent : AgentEvent
{
    public ToolCall ToolCall { get; set; }
}

public class ToolCallApprovedEvent : AgentEvent
{
    public string ToolCallId { get; set; }
}

public class ToolCallCompletedEvent : AgentEvent
{
    public string ToolCallId { get; set; }
    public ToolResult Result { get; set; }
}

public class ErrorEvent : AgentEvent
{
    public string Code { get; set; }    // Stable machine code: e.g. "llm_error", "tool_timeout"
    public string Message { get; set; }
}

public class HeartbeatEvent : AgentEvent
{
    public int StepNumber { get; set; }
}
```

Consumers:
- **Interactive mode**: `EventRenderer` renders events with Spectre.Console markup
- **Print mode**: Filters to `AssistantTextEvent`, writes to stdout. `HeartbeatEvent` goes to stderr.
- **Tests**: Subscribe to event stream, assert on event sequences deterministically

### 4. Built-In Tools Are Native C#, MCP Is for Extension

Built-in tools (filesystem, process execution) are direct C# implementations. They are NOT wrapped in MCP. MCP adds latency (subprocess, JSON-RPC round-trip) and failure modes that core tools should not have.

MCP is the **only** extension mechanism. No plugin system beyond MCP.

### 5. Edit Semantics

**`edit_file`** — primary single-edit tool:
- `old_string` must match exactly once in the file (uniqueness required)
- If ambiguous: fail with structured error including `match_count` and `candidate_line_numbers`
- If not found: fail with structured error and suggestion to re-read

**`multi_edit`** — atomic multi-edit within one file:
- Array of `{ old_string, new_string }` pairs
- All matches validated against the **original** file content before any edits applied
- If any match fails: entire operation aborted, no partial writes
- Same uniqueness requirement per edit

**Line-ending contract (Windows-first):**
- `read_file` normalizes line endings to LF in the string returned to the model
- `edit_file` / `multi_edit` read on-disk content, normalize to LF for matching, apply replacement, write back preserving the file's original line-ending style
- `write_file` accepts LF from the model, writes using platform default for new files or preserves existing style for overwrites

**Structured failure payloads** with stable machine error codes:

```json
{
  "success": false,
  "error": "ambiguous_match",
  "details": {
    "edit_index": 2,
    "match_count": 3,
    "file_path": "src/Mux.Core/Models/Settings.cs",
    "candidate_line_numbers": [12, 45, 78],
    "suggestion": "Include more surrounding context in old_string to disambiguate."
  }
}
```

Stable error codes for Phase 1: `old_string_not_found`, `ambiguous_match`, `file_not_found`, `permission_denied`, `invalid_utf8`, `process_timeout`.

Success payloads:

```json
{
  "success": true,
  "file_path": "src/Mux.Core/Agent/AgentLoop.cs",
  "edits_applied": 3,
  "new_line_count": 255
}
```

### 6. No Persistent Conversation Storage

Conversations live in memory only. MUX is stateless per invocation. Armada handles persistence and workflow state.

---

## Configuration

All configuration lives in `~/.mux/`. MUX creates this directory on first run if it doesn't exist.

### `~/.mux/endpoints.json`

Defines available model runner backends. One must be marked `isDefault: true`. See **[CONFIG.md](CONFIG.md)** for full field reference.

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
      "contextWindow": 32768,
      "apiKey": null,
      "quirks": null
    },
    {
      "name": "openai-gpt4",
      "adapterType": "openai",
      "baseUrl": "https://api.openai.com/v1",
      "model": "gpt-4o",
      "isDefault": false,
      "maxTokens": 16384,
      "temperature": 0.1,
      "contextWindow": 128000,
      "apiKey": "${OPENAI_API_KEY}",
      "quirks": {
        "supportsParallelToolCalls": true
      }
    },
    {
      "name": "vllm-deepseek",
      "adapterType": "openai-compatible",
      "baseUrl": "http://gpu-server:8000/v1",
      "model": "deepseek-coder-v2",
      "isDefault": false,
      "maxTokens": 16384,
      "temperature": 0.0,
      "contextWindow": 131072,
      "apiKey": "sk-local-dev",
      "quirks": {
        "assembleToolCallDeltas": true,
        "supportsParallelToolCalls": true,
        "stripRequestFields": ["stream_options"]
      }
    }
  ]
}
```

### `~/.mux/mcp-servers.json`

Defines MCP tool servers launched on startup.

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
      "name": "filesystem-extra",
      "command": "dotnet",
      "args": ["run", "--project", "/path/to/my-mcp-server"],
      "env": {}
    }
  ]
}
```

Environment variable expansion (`${VAR_NAME}`) is supported in `env` values.

### `~/.mux/settings.json`

Global settings.

```json
{
  "systemPromptPath": null,
  "defaultApprovalPolicy": "ask",
  "toolTimeoutMs": 30000,
  "processTimeoutMs": 120000,
  "contextWindowSafetyMarginPercent": 15,
  "tokenEstimationRatio": 3.5
}
```

### `~/.mux/system-prompt.md` (optional)

Custom system prompt. If not present, MUX uses a built-in default focused on coding assistance.

---

## CLI Invocation Contract

### Basic Usage

```
mux                                     # Interactive REPL (default endpoint)
mux -i                                  # Explicit interactive mode
mux --print "explain this code"         # Single-shot, stdout output, exit
echo "prompt" | mux --print             # Read prompt from stdin
```

### Endpoint/Model Overrides

CLI arguments **override** `~/.mux/endpoints.json` defaults. Precedence (highest to lowest):

1. Explicit CLI arguments (`--endpoint`, `--model`, `--base-url`, `--temperature`, etc.)
2. Named endpoint from `endpoints.json` (selected by `--endpoint <name>`)
3. Default endpoint from `endpoints.json` (the one with `isDefault: true`)

```
mux --endpoint vllm-deepseek            # Use named endpoint from endpoints.json
mux --model qwen2.5-coder:7b            # Override model on default endpoint
mux --base-url http://localhost:11434/v1 --model llama3.1:70b   # Fully ad-hoc
mux --temperature 0.5                   # Override temperature on default endpoint
mux --max-tokens 4096                   # Override max tokens on default endpoint
mux --adapter-type ollama               # Override adapter type
```

**Resolution logic in `SettingsLoader`:**

```
1. Load endpoints.json
2. If --endpoint <name> is given, select that endpoint. Error if not found.
3. Else select the endpoint where isDefault == true. Error if none.
4. Apply any explicit CLI overrides (--model, --base-url, --temperature, etc.)
   onto the selected endpoint's values.
5. Return the resolved EndpointConfig.
```

### Approval Policy

```
mux --yolo                              # Auto-approve all tool calls
mux --approval-policy auto              # Same as --yolo
mux --approval-policy ask               # Prompt for each tool call (default interactive)
mux --approval-policy deny              # Reject tool calls needing approval (default non-interactive)
```

### Orchestrator Invocation (Armada pattern)

```
mux --print --yolo "implement the feature described in TASK.md"
mux --print --yolo --endpoint vllm-deepseek --working-directory /tmp/worktree-abc "fix the bug"
mux --print --yolo --system-prompt /path/to/persona.md "do the thing"
```

### All CLI Flags

| Flag | Short | Type | Default | Description |
|------|-------|------|---------|-------------|
| `--print` | `-p` | bool | false | Single-shot mode: process prompt, print result, exit |
| `--interactive` | `-i` | bool | auto | Force interactive REPL mode |
| `--yolo` | | bool | false | Auto-approve all tool calls (alias for `--approval-policy auto`) |
| `--approval-policy` | | string | auto | `ask`, `auto`, or `deny`. Auto-detected: `ask` if TTY, `deny` if not |
| `--endpoint` | `-e` | string | null | Named endpoint from `endpoints.json` |
| `--model` | `-m` | string | null | Override model name |
| `--base-url` | | string | null | Override base URL |
| `--adapter-type` | | string | null | Override adapter type (`ollama`, `openai`, `vllm`, `openai-compatible`) |
| `--api-key` | | string | null | Override API key (or use env var `MUX_API_KEY`) |
| `--temperature` | | float | null | Override temperature |
| `--max-tokens` | | int | null | Override max tokens |
| `--working-directory` | `-w` | string | cwd | Set working directory for tool execution |
| `--system-prompt` | | string | null | Path to system prompt file (overrides `~/.mux/system-prompt.md`) |
| `--verbose` | `-v` | bool | false | Emit detailed progress to stderr |
| `--no-mcp` | | bool | false | Skip MCP server initialization |
| `--version` | | bool | | Print version and exit |
| `--help` | `-h` | bool | | Print help and exit |

### Interactive REPL Commands

| Command | Description |
|---------|-------------|
| `/model` | List available endpoints, switch active endpoint |
| `/model <name>` | Switch to named endpoint |
| `/tools` | List all available tools (built-in + MCP) |
| `/mcp add <name> <command> [args...]` | Register MCP server for this session |
| `/mcp remove <name>` | Remove MCP server |
| `/mcp list` | List active MCP servers |
| `/clear` | Reset conversation history |
| `/system <text>` | Set/view system prompt |
| `/help` | Show available commands |
| `/exit` or `/quit` | Exit MUX |
| Ctrl+C | Cancel current generation |

---

## Phase 1: Walking Skeleton + Tool Execution

**Goal**: `mux --print --yolo "read the file main.cs and add a comment to the top"` works end-to-end against a real Ollama instance.

**Why merged into one phase**: A coding agent without tools is a chatbot. The walking skeleton must include tool execution to be testable as an agent.

### Task 1.1: Solution Scaffolding
- [ ] Create `src/Mux.sln` with `Mux.Core`, `Mux.Cli` projects
- [ ] Create `test/` with `Test.Shared`, `Test.Xunit`, `Test.Automated` projects
- [ ] Create `Directory.Build.props` with shared settings (v0.1.0, net8.0;net10.0, author, copyright)
- [ ] Configure `Mux.Cli.csproj` as dotnet tool (`PackAsTool`, `ToolCommandName=mux`)
- [ ] Add `.gitignore` (dotnet template + IDE files)
- [ ] Add `README.md`, `CHANGELOG.md`, `LICENSE.md` (MIT, (c)2026 Joel Christner)
- [ ] Add NuGet references: Spectre.Console, Spectre.Console.Cli, Voltaic, xunit

### Task 1.2: Configuration Models and Loading
- [ ] `EndpointConfig` model with all fields from endpoints.json schema above
- [ ] `BackendQuirks` model with Ollama/GenericOpenAI presets
- [ ] `MuxSettings` model for global settings
- [ ] `McpServerConfig` model for MCP server definitions
- [ ] `SettingsLoader` — load `~/.mux/endpoints.json`, `~/.mux/settings.json`
- [ ] Create `~/.mux/` directory and default config on first run
- [ ] `Defaults.cs` — built-in system prompt, default quirks per adapter type
- [ ] **Test.Xunit**: `SettingsLoaderTests` — parse valid config, reject invalid, handle missing file, handle missing default endpoint
- [ ] **Test.Xunit**: `EndpointConfigTests` — serialization round-trip, quirks preset resolution

### Task 1.3: Enums
- [ ] `RoleEnum` — System, User, Assistant, Tool
- [ ] `ApprovalPolicyEnum` — Ask, AutoApprove, Deny
- [ ] `AdapterTypeEnum` — Ollama, OpenAi, Vllm, OpenAiCompatible
- [ ] `AgentEventTypeEnum` — AssistantText, ToolCallProposed, ToolCallApproved, ToolCallCompleted, Error, Heartbeat

### Task 1.4: Conversation Models
- [ ] `ConversationMessage` — Role, Content (string), ToolCalls (list), ToolCallId (for tool results)
- [ ] `ToolCall` — Id, Name, Arguments (JSON string)
- [ ] `ToolResult` — ToolCallId, Success (bool), Content (string, JSON)
- [ ] `ToolDefinition` — Name, Description, ParametersSchema (JSON object) — MUX-native, maps to/from OpenAI function schema

### Task 1.5: Backend Adapter Interface + Implementations
- [ ] `IBackendAdapter` interface with three methods (see Core Design Decisions above)
- [ ] `GenericOpenAiAdapter` — standard OpenAI chat completions request/response
  - [ ] Request building: messages array, tools array (OpenAI function calling format), temperature, max_tokens
  - [ ] Non-streaming response parsing: extract assistant message, tool_calls if present
  - [ ] Streaming: SSE chunk reading, delta assembly for content and tool_calls
- [ ] `OpenAiAdapter` extends GenericOpenAi behavior with:
  - [ ] Bearer token `Authorization` header from `apiKey` (required, error if missing)
  - [ ] Full parallel tool call support (`parallel_tool_calls: true`)
  - [ ] Structured output / JSON mode support via `response_format`
  - [ ] Environment variable expansion for `apiKey` field (`${OPENAI_API_KEY}`)
- [ ] `OllamaAdapter` extends GenericOpenAi behavior with:
  - [ ] Strip unsupported request fields (`parallel_tool_calls`, `stream_options`)
  - [ ] Handle Ollama's tool-call assembly quirks (single-chunk tool calls vs deltas)
  - [ ] Map Ollama-specific finish reasons to MUX-native values
- [ ] **Test.Xunit**: `GenericOpenAiAdapterTests` — request shape validation, response parsing, streaming chunk assembly
- [ ] **Test.Xunit**: `OpenAiAdapterTests` — auth header injection, env var expansion, parallel tool calls
- [ ] **Test.Xunit**: `OllamaAdapterTests` — field stripping, quirks handling, tool-call assembly

### Task 1.6: LLM Client
- [ ] `LlmClient` — takes `EndpointConfig`, resolves adapter by `adapterType`, manages HttpClient
- [ ] `SendAsync(messages, tools, cancellationToken)` — non-streaming, returns `ConversationMessage`
- [ ] `StreamAsync(messages, tools, cancellationToken)` — streaming, returns `IAsyncEnumerable<AgentEvent>`
- [ ] HTTP error handling: timeout, 4xx (log request shape for debugging), 5xx (retry-eligible)
- [ ] **Test.Xunit**: `LlmClientTests` with `MockHttpServer` (from Test.Shared) — verify request/response cycle, error handling
- [ ] **Test.Shared**: `MockHttpServer` — configurable fake OpenAI-compatible endpoint, returns canned responses or tool-call responses

### Task 1.7: Agent Event Types
- [ ] `AgentEvent` base class with `TimestampUtc` and `EventType`
- [ ] `AssistantTextEvent` — streaming text from LLM
- [ ] `ToolCallProposedEvent` — LLM wants to call a tool
- [ ] `ToolCallApprovedEvent` — tool call approved (by user or policy)
- [ ] `ToolCallCompletedEvent` — tool execution finished, includes `ToolResult`
- [ ] `ErrorEvent` — stable error code + human message
- [ ] `HeartbeatEvent` — step counter for orchestrator stall detection

### Task 1.8: Built-In Tool Implementations
- [ ] `IToolExecutor` interface: `Task<ToolResult> ExecuteAsync(string name, JsonElement arguments, CancellationToken token)`
- [ ] `BuiltInToolRegistry` — registers tools, routes by name, returns tool definitions for LLM
- [ ] `ReadFileTool` — read file with optional offset/limit, line numbers in output, LF-normalized output
  - Parameters: `file_path` (required), `offset` (optional, line number), `limit` (optional, line count)
- [ ] `WriteFileTool` — write/overwrite file, auto-detect line endings for existing files, platform default for new
  - Parameters: `file_path` (required), `content` (required)
- [ ] `EditFileTool` — old_string/new_string replacement, uniqueness required, LF-normalize for matching, preserve original line endings on write
  - Parameters: `file_path` (required), `old_string` (required), `new_string` (required)
  - Returns structured success/failure JSON (see Edit Semantics above)
- [ ] `MultiEditTool` — atomic multi-replacement, all validated against original before any applied
  - Parameters: `file_path` (required), `edits` (required, array of `{old_string, new_string}`)
  - Returns structured success/failure JSON with `edit_index` on failure
- [ ] `ListDirectoryTool` — list directory contents with file types
  - Parameters: `path` (required)
- [ ] `GlobTool` — pattern-based file search
  - Parameters: `pattern` (required), `path` (optional, defaults to working directory)
- [ ] `GrepTool` — regex content search across files
  - Parameters: `pattern` (required), `path` (optional), `include` (optional, glob filter)
- [ ] `RunProcessTool` — execute command, return stdout/stderr/exit code, configurable timeout
  - Parameters: `command` (required), `args` (optional, array), `working_directory` (optional), `timeout_ms` (optional)
- [ ] **Test.Xunit**: Unit tests for each tool in isolation (temp directories, known file content)
  - [ ] `ReadFileToolTests` — read existing, read missing, offset/limit, LF normalization of CRLF files
  - [ ] `WriteFileToolTests` — create new, overwrite existing, line ending preservation
  - [ ] `EditFileToolTests` — successful edit, string not found, ambiguous match, CRLF handling, structured error payloads
  - [ ] `MultiEditToolTests` — atomic success, partial failure (no writes), ambiguity detection, edit ordering
  - [ ] `GlobToolTests` — pattern matching, nested directories
  - [ ] `GrepToolTests` — regex matching, file filtering
  - [ ] `RunProcessToolTests` — stdout capture, stderr capture, exit code, timeout

### Task 1.9: Approval Handler
- [ ] `ApprovalHandler` — takes `ApprovalPolicyEnum`, tool call, returns approved/denied
- [ ] `Ask` mode: prompt user via Spectre.Console (interactive) — show tool name, args preview, [Y/n/always]
- [ ] `AutoApprove` mode: always return approved
- [ ] `Deny` mode: always return denied, emit `ErrorEvent` with tool call details
- [ ] "always" response in Ask mode promotes to AutoApprove for the rest of the session
- [ ] **Test.Xunit**: `ApprovalHandlerTests` — AutoApprove/Deny deterministic, Ask mode with mock input

### Task 1.10: Agent Loop
- [ ] `AgentLoop.RunAsync(prompt, options, cancellationToken)` returns `IAsyncEnumerable<AgentEvent>`
- [ ] Options: conversation history, tools, approval policy, endpoint config, system prompt
- [ ] Loop logic:
  1. Assemble system prompt + conversation history + user prompt
  2. Call LLM via `LlmClient` (streaming or non-streaming based on adapter capability)
  3. If response contains tool_calls:
     a. Emit `ToolCallProposedEvent` for each
     b. Run through `ApprovalHandler`
     c. If approved: execute via `BuiltInToolRegistry` (or `McpToolManager` in Phase 2), emit `ToolCallCompletedEvent`
     d. If denied: emit `ErrorEvent`, append denial to conversation
     e. Append tool results to conversation history
     f. Emit `HeartbeatEvent` with step counter
     g. Loop back to step 2
  4. If response is text-only: emit `AssistantTextEvent`, done
- [ ] Max iterations safety limit (default 25, configurable) to prevent infinite loops
- [ ] **Test.Xunit**: `AgentLoopTests` with `MockHttpServer`
  - [ ] Single-turn (no tools): prompt -> text response
  - [ ] Multi-turn (tools): prompt -> tool call -> tool result -> text response
  - [ ] Tool call denied: prompt -> tool call -> denied -> error in conversation -> text response
  - [ ] Max iterations: loop terminates after limit
  - [ ] Event ordering: verify correct event sequence

### Task 1.11: CLI Entry Point
- [ ] `Program.cs` — Spectre.Console.Cli `CommandApp` with `InteractiveCommand` (default) and `PrintCommand`
- [ ] `PrintCommand`:
  - [ ] Accept prompt as positional argument or read from stdin
  - [ ] Resolve endpoint (default from config, overridden by CLI args — see CLI Invocation Contract)
  - [ ] Run `AgentLoop`, filter events to `AssistantTextEvent` -> stdout, `HeartbeatEvent` -> stderr, `ErrorEvent` -> stderr
  - [ ] Exit code 0 on success, 1 on error
- [ ] `InteractiveCommand`:
  - [ ] REPL loop: read prompt, run `AgentLoop`, render events with Spectre.Console
  - [ ] Basic rendering (polish in Phase 3): print assistant text, show tool call names, show results
  - [ ] Support Ctrl+C to cancel current generation
  - [ ] Support `/exit` and `/quit` to exit
  - [ ] Endpoint resolution with CLI override precedence (see CLI Invocation Contract)
- [ ] **Test.Automated**: `SingleTurnTests` — launch `mux --print` as process, verify stdout contains response
- [ ] **Test.Automated**: `ToolUseTests` — launch `mux --print --yolo` with a prompt that requires file reading, verify correct output
- [ ] **Test.Automated**: `PrintModeTests` — verify exit code, stderr heartbeat format, stdin prompt reading

### Task 1.12: Test Infrastructure
- [ ] `Test.Shared/TestSuite.cs` — base class for automated test suites (mirrors Armada's pattern)
- [ ] `Test.Shared/TestRunner.cs` — discovers and executes test suites, reports results
- [ ] `Test.Shared/TestResult.cs` — pass/fail/skip/error tracking
- [ ] `Test.Shared/MockHttpServer.cs` — configurable OpenAI-compatible mock:
  - [ ] Register canned responses for specific prompts
  - [ ] Register tool-calling responses (model returns tool_calls, then text after tool results)
  - [ ] Streaming and non-streaming modes
  - [ ] Request logging for test assertions
- [ ] `Test.Automated/Program.cs` — entry point, parse args, run selected suites

### Phase 1 Exit Criteria
- [ ] `mux --print --yolo "read the contents of README.md"` successfully calls Ollama, uses `read_file` tool, prints file contents
- [ ] `mux --print --yolo "create a file called hello.txt with the content 'hello world'"` creates the file
- [ ] `mux -i` starts a REPL, accepts prompts, displays responses and tool calls
- [ ] `mux --print "hello"` (no `--yolo`) with a tool-calling response fails with denial error (correct default)
- [ ] `mux --endpoint nonexistent --print "hello"` errors with clear message
- [ ] `mux --model custom-model --print "hello"` overrides the default endpoint's model
- [ ] All Test.Xunit tests pass
- [ ] Test.Automated SingleTurnTests and ToolUseTests pass against MockHttpServer

---

## Phase 2: MCP Integration

**Goal**: User-defined MCP servers work seamlessly. Tools from MCP servers appear alongside built-in tools.

### Task 2.1: MCP Server Lifecycle Management
- [ ] `McpToolManager` class — owns MCP client lifecycle
- [ ] On startup: read `~/.mux/mcp-servers.json`, launch each server via `McpClient.LaunchServerAsync()`
- [ ] Environment variable expansion in `env` values (`${VAR_NAME}` -> `Environment.GetEnvironmentVariable()`)
- [ ] Call `tools/list` on each connected server, collect `ToolDefinition` objects
- [ ] Map Voltaic `ToolDefinition` to MUX-native `ToolDefinition` (name prefixed with server name to avoid collisions: `github.create_issue`)
- [ ] Health monitoring: detect disconnected servers, log warning, skip their tools
- [ ] Lazy restart: if a server disconnects, attempt one reconnect on next tool call
- [ ] Graceful shutdown: `Shutdown()` all MCP clients on MUX exit

### Task 2.2: Tool Routing
- [ ] `BuiltInToolRegistry` owns built-in tools, `McpToolManager` owns MCP tools
- [ ] Unified tool list for LLM: merge both tool sets when assembling the tools parameter
- [ ] Route tool calls by name: built-in names route to `BuiltInToolRegistry`, prefixed names route to `McpToolManager`
- [ ] `McpToolManager.ExecuteAsync(toolName, arguments)` — call `tools/call` on the appropriate MCP client
- [ ] Handle MCP tool errors gracefully: return structured `ToolResult` with error, don't crash the loop

### Task 2.3: Interactive MCP Commands
- [ ] `/mcp list` — show connected servers, tool count per server, health status
- [ ] `/mcp add <name> <command> [args...]` — launch a new MCP server for this session (not persisted to config)
- [ ] `/mcp remove <name>` — disconnect and remove an MCP server

### Task 2.4: MCP Tests
- [ ] **Test.Shared**: `TestMcpServer` — a Voltaic `McpServer` test fixture with known tools (e.g., `echo`, `add`, `fail`)
- [ ] **Test.Xunit**: `McpToolManagerTests` — server launch, tool discovery, tool call routing, server crash handling
- [ ] **Test.Automated**: `McpIntegrationTests` — end-to-end: launch MUX + test MCP server, prompt that uses MCP tool, verify result

### Phase 2 Exit Criteria
- [ ] MUX discovers and lists tools from configured MCP servers
- [ ] Agent can use MCP tools alongside built-in tools in the same conversation
- [ ] MCP server crash doesn't crash MUX
- [ ] `/mcp list` shows server status
- [ ] All MCP-related tests pass

---

## Phase 3: Interactive Polish

**Goal**: The REPL feels good to use — responsive, readable, and navigable.

### Task 3.1: Streaming LLM Output
- [ ] Stream `AssistantTextEvent` tokens to terminal in real-time (not buffered)
- [ ] Use Spectre.Console `LiveDisplay` or `AnsiConsole.Write` with incremental output
- [ ] Handle streaming cancellation (Ctrl+C) gracefully — stop generation, keep conversation intact

### Task 3.2: Markdown Rendering
- [ ] Render LLM output as terminal-formatted markdown (headers, code blocks, lists, bold/italic)
- [ ] Use Spectre.Console markup or a lightweight markdown-to-ANSI library
- [ ] Syntax highlighting in code blocks (at minimum: C#, JSON, shell)

### Task 3.3: Tool Call Display
- [ ] Show tool call proposals with colored name and truncated argument preview
- [ ] Show tool results with success/failure indicators
- [ ] In `Ask` mode: display clear `[Y/n/always]` prompt with tool details
- [ ] Collapse long tool results (file contents, grep output) with expandable display

### Task 3.4: REPL Commands
- [ ] `/model` — list all endpoints with current selection highlighted
- [ ] `/model <name>` — switch endpoint, clear conversation (model context mismatch)
- [ ] `/tools` — list all available tools (built-in + MCP) with descriptions
- [ ] `/clear` — reset conversation history
- [ ] `/system` — display current system prompt
- [ ] `/system <text>` — replace system prompt for this session
- [ ] `/help` — list all commands with descriptions

### Task 3.5: Context Window Management
- [ ] Token estimation: character count / 3.5 (conservative ratio for English/code)
- [ ] Track estimated token count per conversation message
- [ ] When approaching context window limit (configurable safety margin, default 15%):
  - [ ] Summarize or truncate oldest messages (keep system prompt and last N turns)
  - [ ] Emit warning to user
- [ ] Display token usage in status line (optional, `--verbose`)

### Task 3.6: System Prompt
- [ ] Built-in default system prompt: coding-focused, mentions available tools, working directory
- [ ] Load from `~/.mux/system-prompt.md` if present
- [ ] Override via `--system-prompt <path>` CLI flag
- [ ] Include tool descriptions in system prompt dynamically

### Phase 3 Exit Criteria
- [ ] Streaming text appears in terminal in real-time
- [ ] Code blocks render with syntax highlighting
- [ ] `/model`, `/tools`, `/clear`, `/help` all work
- [ ] Context window management prevents silent truncation errors
- [ ] REPL feels responsive and readable

---

## Phase 4: Orchestrator Integration

**Goal**: Armada (or any orchestrator) can use MUX as a first-class agent runtime.

### Task 4.1: Print Mode Hardening
- [ ] Verify `--print` mode: single-shot, stdout-only assistant text, stderr for progress/errors
- [ ] Heartbeat emission to stderr: `[mux] working... (step N)` at configurable interval
- [ ] `--verbose` adds detailed stderr logging (tool calls, tool results, timings)
- [ ] Long prompt via stdin: `echo "long prompt" | mux --print --yolo`
- [ ] Exit codes: 0 = success, 1 = error, 2 = tool call denied (orchestrator didn't pass --yolo)

### Task 4.2: Working Directory Override
- [ ] `--working-directory <path>` sets the CWD for all tool execution
- [ ] All built-in tools resolve relative paths against this directory
- [ ] Verify tools don't escape working directory (safety — log warning, don't hard-block for --yolo)

### Task 4.3: Armada Runtime Adapter
- [ ] Contribute `MuxRuntime.cs` to Armada.Runtimes (follows `BaseAgentRuntime` pattern):
  - [ ] `GetCommand()` returns `"mux"` (or configurable path)
  - [ ] `BuildArguments(prompt)` returns `["--print", "--yolo", "--verbose", prompt]` (with configurable flags)
  - [ ] `ApplyEnvironment()` sets working directory, any MUX-specific env vars
  - [ ] `SkipPermissions` property controls `--yolo` flag
  - [ ] Endpoint selection via `AgentSettings.Args` (e.g., `--endpoint vllm-deepseek`)
- [ ] Add `MuxRuntimeEnum` value to Armada's `AgentRuntimeEnum`
- [ ] Register in Armada's `AgentRuntimeFactory`

### Task 4.4: Orchestrator Tests
- [ ] **Test.Automated**: `PrintModeTests` — simulate Armada invocation:
  - [ ] Launch `mux --print --yolo --working-directory <temp> "create hello.txt"`, verify file created
  - [ ] Verify stdout is clean assistant text (no progress noise)
  - [ ] Verify stderr contains heartbeat lines
  - [ ] Verify exit code 0 on success
  - [ ] Verify exit code 2 when tool denied (no --yolo in non-interactive mode)
- [ ] **Test.Automated**: `EndpointSwitchingTests` — verify `--endpoint`, `--model` overrides work from CLI

### Phase 4 Exit Criteria
- [ ] Armada can launch `mux --print --yolo` and capture clean output
- [ ] Heartbeats prevent stall detection false positives
- [ ] `MuxRuntime` adapter passes Armada.Test.Runtimes pattern
- [ ] Working directory isolation works correctly

---

## Phase 5: Hardening

**Goal**: Production-quality robustness for daily use.

### Task 5.1: Retry Logic
- [ ] Exponential backoff for LLM calls: 1s, 2s, 4s, max 3 retries
- [ ] Retry on 5xx, timeout, connection refused
- [ ] Do not retry on 4xx (client error — log request shape for debugging)
- [ ] Emit `ErrorEvent` on final failure after retries exhausted

### Task 5.2: Streaming Robustness
- [ ] Full streaming support in both adapters
- [ ] Handle incomplete SSE chunks, reconnection on stream drop
- [ ] Assemble tool_call deltas correctly (handle interleaved content + tool_call chunks)
- [ ] Handle malformed tool calls from weak models: regex extraction of JSON from markdown code blocks as fallback

### Task 5.3: Tool Execution Safety
- [ ] Configurable tool timeout (default 30s for tools, 120s for `run_process`)
- [ ] `run_process` timeout with process kill on expiry
- [ ] Catch and wrap all tool exceptions into structured `ToolResult` errors
- [ ] File size limits on `read_file` (default 1MB, configurable)
- [ ] Output size limits on `run_process` (default 100KB stdout/stderr)

### Task 5.4: Graceful Shutdown
- [ ] Handle SIGINT/SIGTERM (Ctrl+C): cancel current LLM call, shut down MCP servers, exit cleanly
- [ ] In interactive mode: first Ctrl+C cancels generation, second Ctrl+C exits
- [ ] In print mode: Ctrl+C exits immediately with non-zero exit code

### Task 5.5: Error Recovery
- [ ] LLM returns malformed tool call JSON: attempt parse, fallback to regex extraction, fallback to treating as text
- [ ] MCP server crashes mid-call: return tool error, continue loop
- [ ] Network timeout during LLM streaming: retry from last complete message
- [ ] Disk full during write: return structured error, don't crash

### Task 5.6: Dotnet Tool Packaging
- [ ] Verify `dotnet tool install -g Mux.Cli` installs `mux` command
- [ ] Verify `mux --version` outputs correct version
- [ ] Verify `mux --help` outputs usage information
- [ ] Create NuGet package configuration for publishing

### Phase 5 Exit Criteria
- [ ] MUX handles network failures gracefully (retry, clear error messages)
- [ ] Malformed model output doesn't crash the loop
- [ ] `dotnet tool install -g Mux.Cli` works
- [ ] 30 minutes of continuous interactive use without crashes

---

## Testing Strategy

### Test.Xunit (Unit Tests)

Fast, deterministic, no external dependencies. Run with `dotnet test`.

**Coverage targets:**
- Configuration parsing and validation (endpoints.json, settings.json, mcp-servers.json)
- Backend adapter request building and response parsing (with canned JSON fixtures)
- Each built-in tool in isolation (temp filesystem)
- Edit tool semantics: uniqueness, CRLF handling, structured errors, atomic multi-edit
- Agent loop event ordering (with MockHttpServer)
- Approval handler logic

**Pattern:**
```csharp
[Fact]
public async Task EditFile_AmbiguousMatch_ReturnsStructuredError()
{
    // Arrange: file with duplicate lines
    // Act: edit_file with old_string matching both
    // Assert: ToolResult.Success == false, error == "ambiguous_match", match_count == 2
}
```

### Test.Automated (Integration Tests)

End-to-end tests that launch `mux` as a process. Require either MockHttpServer or real Ollama instance.

**Pattern** (mirrors Armada.Test.Automated):
- Each test suite extends `TestSuite`
- `TestRunner` discovers and executes suites
- Tests launch `mux` via `Process.Start`, capture stdout/stderr, assert on output

**Two modes:**
1. **Mock mode** (default): Test.Shared `MockHttpServer` provides canned LLM responses. Tests are deterministic and fast.
2. **Live mode** (`--live`): Tests run against a real Ollama instance. Non-deterministic but validates real integration. Requires `OLLAMA_HOST` environment variable.

### Test Execution

```bash
# Unit tests
dotnet test test/Test.Xunit/

# Automated tests (mock mode)
dotnet run --project test/Test.Automated/

# Automated tests (live mode, requires Ollama)
dotnet run --project test/Test.Automated/ -- --live --endpoint http://localhost:11434/v1
```

---

## Help Menu (`mux --help`)

The `--help` / `-h` / `/?` flag prints usage information and exits. This is implemented via Spectre.Console.Cli's built-in help rendering, customized to match the MUX brand.

**Expected output of `mux --help`:**

```
MUX v0.1.0 — AI coding agent for local and remote LLMs
(c)2026 Joel Christner — MIT License

USAGE:
    mux [prompt]                         Interactive REPL (default)
    mux [OPTIONS] [prompt]               Interactive with overrides
    mux --print [OPTIONS] <prompt>       Single-shot mode
    echo "prompt" | mux --print          Read prompt from stdin

OPTIONS:
    -h, --help                           Show this help message and exit
        --version                        Show version and exit
    -i, --interactive                    Force interactive REPL mode
    -p, --print                          Single-shot: process prompt, print result, exit

  Endpoint / Model:
    -e, --endpoint <name>                Named endpoint from ~/.mux/endpoints.json
    -m, --model <name>                   Override model name
        --base-url <url>                 Override base URL
        --adapter-type <type>            Adapter: ollama, openai, vllm, openai-compatible
        --api-key <key>                  Override API key (or set MUX_API_KEY env var)
        --temperature <float>            Override temperature (0.0 - 2.0)
        --max-tokens <int>               Override max output tokens

  Approval / Safety:
        --yolo                           Auto-approve all tool calls
        --approval-policy <policy>       ask, auto, or deny (default: ask if TTY, deny otherwise)

  Execution:
    -w, --working-directory <path>       Set working directory for tool execution
        --system-prompt <path>           Path to system prompt file
        --no-mcp                         Skip MCP server initialization
    -v, --verbose                        Emit detailed progress to stderr

INTERACTIVE COMMANDS:
    /model [name]      List or switch endpoints
    /tools             List available tools (built-in + MCP)
    /mcp list|add|remove   Manage MCP servers
    /clear             Reset conversation history
    /system [text]     View or set system prompt
    /help              Show interactive commands
    /exit, /quit       Exit MUX

EXAMPLES:
    mux                                  Start interactive session (default endpoint)
    mux --endpoint ollama-qwen           Start with specific endpoint
    mux -p --yolo "read README.md"       Single-shot with auto-approval
    mux -p -e openai-gpt4 "explain x"   Single-shot with OpenAI
    mux --base-url http://localhost:11434/v1 --model llama3.1:70b
                                         Ad-hoc endpoint, no config needed

CONFIG:
    ~/.mux/endpoints.json                Model runner endpoints
    ~/.mux/mcp-servers.json              MCP tool servers
    ~/.mux/settings.json                 Global settings
    ~/.mux/system-prompt.md              Custom system prompt (optional)

    See CONFIG.md for full configuration reference.
    See USAGE.md for detailed usage examples.
```

**Implementation notes:**
- Spectre.Console.Cli generates help automatically from command settings; the above is the target output after customization via `ICommandAppSettings.ApplicationName`, `ApplicationVersion`, and `HelpProvider` overrides.
- `/?` support is added by registering it as an alias for `--help` in the Spectre.Console.Cli settings.
- The help text is generated, not hardcoded — so it stays in sync with actual flags.

---

## Risks and Mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| Open-source models produce malformed tool calls | Agent loop stalls or crashes | Robust parsing with regex fallback; structured error recovery; model-specific system prompt tuning |
| Context window estimation is inaccurate | Silent truncation or API errors | Conservative 3.5 chars/token ratio with 15% safety margin; handle API "context too long" errors gracefully |
| Ollama/vLLM streaming behavior changes between versions | Adapter breaks | Pin adapter behavior to known versions; `BackendQuirks` overrides in endpoints.json for user workarounds |
| MCP server subprocess crashes | Tool calls fail | Health monitoring, lazy restart, graceful error wrapping — never crash the agent loop |
| Windows line-ending mismatches | Edit tools fail silently or corrupt files | Normalize-on-read, preserve-on-write contract tested explicitly in Test.Xunit |
| "Similar to Claude Code" bar is high | User disappointment | v0.1.0 targets functional, not beautiful. Ship the loop, ship the tools, ship the integration. Polish in v0.2.0. |

---

## Coding Conventions

Per MUX.md and established patterns in the ecosystem:

- **No `var`** — explicit types everywhere
- **No tuples** — use named types
- **`using` statements** (not declarations), inside namespace blocks
- **XML documentation** on all public members
- **Naming**: public `LikeThis`, private `_LikeThis`
- **One entity per file**
- **Null check on set** where appropriate
- **Value-clamping** to reasonable ranges where appropriate
- **No `JsonElement` property accessors** for things that should be defined types
- Follow patterns in Armada, Voltaic, Verbex, Lattice, Conductor, Chronos

---

## Dependencies Summary

| Dependency | Version | Purpose |
|------------|---------|---------|
| Spectre.Console | latest | Terminal rendering, markup, live display |
| Spectre.Console.Cli | latest | CLI argument parsing, command routing |
| Voltaic | >=0.1.11 | MCP client (stdio transport) |
| xunit | latest | Unit test framework |
| Microsoft.NET.Test.Sdk | latest | Test infrastructure |
| xunit.runner.visualstudio | latest | VS test runner integration |

No other external dependencies. HTTP client is `System.Net.Http.HttpClient`. JSON is `System.Text.Json`. Regex is `System.Text.RegularExpressions`. All framework-provided.

**Authentication note**: API key transmission uses the standard `Authorization: Bearer <key>` header via `HttpClient.DefaultRequestHeaders`. No additional auth libraries are needed. Environment variable expansion (`${VAR_NAME}`) for `apiKey` fields is handled by `SettingsLoader` at config load time.
