# @mux/sdk

A thin TypeScript driver for the [mux](../../README.md) CLI. It does not reimplement the agent — it spawns
`mux print --output-format jsonl`, parses the newline-delimited event stream, and hands you typed events
plus an aggregated result. Because it wraps the CLI and its `contractVersion`-versioned JSONL contract, the
SDK stays backend-agnostic and in lockstep with mux itself.

> Alpha, tracking mux `v0.7.0`. The API may change alongside the CLI.

## Requirements

- Node.js 20+
- The `mux` CLI installed and on `PATH` (or reachable via `muxPath` / `muxArgs`)

## Install

```bash
npm install @mux/sdk
```

## Quick start

Run a prompt to completion:

```ts
import { Mux } from "@mux/sdk";

const mux = new Mux({ yolo: true });
const result = await mux.run("summarize README.md");

console.log(result.text);        // the assistant's answer
console.log(result.status);      // "completed"
console.log(result.exitCode);    // 0
```

Stream events as they arrive:

```ts
for await (const event of mux.runStreamed("refactor AuthService")) {
  if (event.eventType === "assistant_text") process.stdout.write(event.text);
  if (event.eventType === "tool_call_completed") console.error(`[tool] ${event.toolName}`);
}
```

Hold a multi-turn conversation (threaded through a mux session, so turn N sees turns 1..N-1):

```ts
const thread = mux.startThread();
await thread.run("start a security review");
const followUp = await thread.run("now scan the dependencies");
console.log(followUp.text);

// Later, in another process, resume by id:
const resumed = mux.resumeThread(thread.id);
await resumed.run("summarize what you found");
```

Constrain, confine, and govern a run the same way the CLI does:

```ts
const mux = new Mux({
  yolo: true,
  sandbox: "workspace-write",
  addDir: ["../shared"],
  denyTools: ["run_process"],
  maxTurns: 8,
  maxTokenBudget: 200_000,
});
```

## Pointing at the binary

By default the SDK spawns `mux` from `PATH`. Override it when mux is elsewhere or launched indirectly:

```ts
// A specific binary:
new Mux({ muxPath: "/usr/local/bin/mux" });

// Run the built DLL directly (no global install):
new Mux({ muxPath: "dotnet", muxArgs: ["/path/to/Mux.Cli.dll"] });
```

## API

### `new Mux(options?: MuxOptions)`

Shared options applied to every call; any field is overridable per call.

| Option | Type | Maps to |
|---|---|---|
| `muxPath` | `string` | the executable (default `"mux"`) |
| `muxArgs` | `string[]` | args before `print` (e.g. a DLL path) |
| `endpoint` / `model` / `baseUrl` / `adapterType` | `string` | `--endpoint` / `--model` / `--base-url` / `--adapter-type` |
| `configDir` / `workingDirectory` | `string` | `--config-dir` / `--working-directory` |
| `temperature` / `maxTokens` | `number` | `--temperature` / `--max-tokens` |
| `maxTurns` / `maxTokenBudget` | `number` | `--max-turns` / `--max-token-budget` |
| `systemPrompt` / `appendSystemPrompt` | `string` | `--system-prompt` / `--append-system-prompt` |
| `yolo` | `boolean` | `--yolo` |
| `approvalPolicy` | `"auto" \| "deny"` | `--approval-policy` |
| `sandbox` | `"none" \| "read-only" \| "workspace-write"` | `--sandbox` |
| `allowTools` / `denyTools` | `string[]` | `--allow-tools` / `--deny-tools` (comma-joined) |
| `addDir` | `string[]` | repeated `--add-dir` |
| `mcpConfig` / `strictMcpConfig` | `string` / `boolean` | `--mcp-config` / `--strict-mcp-config` |
| `ignoreCertErrors` | `boolean` | `--ignore-cert-errors` |
| `env` | `Record<string,string>` | extra environment for the spawned process |

### `mux.run(prompt, overrides?): Promise<RunResult>`

Runs to completion and returns the aggregated result: `text`, `sessionId`, `status`, `exitCode`,
`iterationsCompleted`, `toolCallCount`, `errorCount`, `durationMs`, `finalEstimatedTokens`, `inputTokens`,
`outputTokens`, `totalTokens`, `stderr`, and the full ordered `events` array. The token fields come from the
`run_completed` event's `usage` block (provider-reported; `0` when the backend reports none).

### `mux.runStreamed(prompt, overrides?): AsyncGenerator<MuxEvent>`

Yields each event as it arrives. The process is spawned when iteration begins and torn down when it ends.

### `mux.startThread(sessionId?) / mux.resumeThread(sessionId): Thread`

A `Thread` runs every turn under one mux session id (`--session-id`), so history accumulates across turns
in mux's own session store. `Thread.run` / `Thread.runStreamed` mirror the client methods.

### `buildPrintArgs(options, sessionId, prompt): string[]`

Returns the exact `mux print` argv the SDK would spawn — handy for debugging or logging.

### Event types

`MuxEvent` is a discriminated union on `eventType`: `run_started`, `assistant_text`,
`tool_call_proposed`, `tool_call_approved`, `tool_call_completed`, `error`, `heartbeat`,
`run_completed`, and a permissive `UnknownEvent` fallback so new event types in a known contract version
don't break consumers. See [`src/index.ts`](src/index.ts) for the full shapes.

## Exit codes

`RunResult.exitCode` mirrors the CLI: `0` success, `1` error, `2` tool call denied. Governance and schema
refusals surface as `error` events (with codes like `tool_call_denied`, `budget_exceeded`,
`schema_validation_failed`) and the corresponding non-zero exit code.

## Development

```bash
npm install      # dev deps (typescript, @types/node)
npm run build    # tsc -> dist/
npm test         # node --test (hermetic; uses a fake mux, no network or model)
```

The test suite spawns a stand-in `mux` (`test/fake-mux.mjs`) that emits a canned event stream and records
its argv, so it verifies argument construction, event parsing, result aggregation, streaming, exit-code
handling, and thread session continuity without a real model or backend.

## License

[MIT](../../LICENSE.md)
