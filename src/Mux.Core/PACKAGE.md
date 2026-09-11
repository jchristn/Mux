# Mux.Core

The engine behind [**mux**](https://github.com/jchristn/Mux) — a backend-agnostic AI coding agent — packaged so you can build your own experiences (desktop apps, services, bespoke tools) on top of it.

`Mux.Core` gives you an agent loop with streaming events, a built-in tool suite (file edit/read/write, glob, grep, process execution, web retrieval/search), MCP client integration, skills, subagents, session persistence, git checkpoints, approval/sandbox governance, and durable usage telemetry — all provider-agnostic through [PolyPrompt](https://www.nuget.org/packages/PolyPrompt) (Ollama, OpenAI, vLLM, Azure OpenAI, Anthropic, Gemini, Vertex, Bedrock, and any OpenAI-compatible API).

## Quick start

```csharp
using Mux.Core.Agent;
using Mux.Core.Settings;

EndpointConfig endpoint = SettingsLoader.ResolveEndpoint(
    SettingsLoader.LoadEndpoints(), name: null, model: null, baseUrl: null, adapter: null,
    temperature: null, maxTokens: null);

AgentLoopOptions options = new AgentLoopOptions
{
    Endpoint = endpoint,
    ApprovalPolicy = ApprovalPolicyEnum.Ask,
    PromptUserFunc = async toolCall => "y" // your approval UX returns "y" / "n" / "always"
};

using AgentLoop loop = new AgentLoop(options);
await foreach (AgentEvent evt in loop.RunAsync("List the files in this directory.", CancellationToken.None))
{
    // Render AssistantTextEvent, ToolCallProposedEvent, RunCompletedEvent, etc.
}
```

## What you get

- **`AgentLoop`** — one call per user turn, returning an `IAsyncEnumerable<AgentEvent>` you render live.
- **Sessions** — `SessionStore` / `SessionSnapshot` / `SessionResumeService` / `SessionExporter`.
- **Tools & MCP** — `BuiltInToolRegistry`, `McpToolManager`, allow/deny + sandbox governance.
- **Telemetry** — `UsageTelemetry` + `UsageQueryService` over a shared local SQLite store.
- **Config** — `SettingsLoader` over the `~/.mux` config directory.

## License

MIT. See the [repository](https://github.com/jchristn/Mux) for full documentation.

> **Alpha:** APIs, tool schemas, and configuration formats may change between releases.
