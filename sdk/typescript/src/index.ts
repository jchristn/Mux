/**
 * @mux/sdk — a thin TypeScript driver for the mux CLI.
 *
 * The SDK does not reimplement the agent. It spawns `mux print --output-format jsonl`, parses the
 * newline-delimited event stream, and surfaces it as typed events plus an aggregated result. Multi-turn
 * conversations are threaded through mux's own session store (via `--session-id`), so behavior matches the
 * CLI exactly. Wrapping the CLI — rather than binding to internals — keeps the SDK backend-agnostic and in
 * lockstep with the `contractVersion`-versioned JSONL contract.
 */

import { spawn } from "node:child_process";
import { createInterface } from "node:readline";
import { randomUUID } from "node:crypto";

/** The application-level confinement posture (mirrors the CLI `--sandbox` values). */
export type SandboxPosture = "none" | "read-only" | "workspace-write";

/** The non-interactive approval policy (mirrors the CLI `--approval-policy` values for print). */
export type ApprovalPolicy = "auto" | "deny";

/**
 * Options for constructing a {@link Mux} client. Every field maps to a `mux print` flag; all are optional
 * and may be overridden per call via {@link RunOverrides}.
 */
export interface MuxOptions {
  /** The mux executable to spawn. Defaults to `"mux"` (resolved on PATH). */
  muxPath?: string;
  /**
   * Arguments inserted immediately after {@link muxPath}, before `print`. Useful when mux is launched
   * indirectly, e.g. `muxPath: "dotnet", muxArgs: ["/path/Mux.Cli.dll"]`.
   */
  muxArgs?: string[];
  /** Named endpoint from the active `endpoints.json`. */
  endpoint?: string;
  /** Model identifier override. */
  model?: string;
  /** Base URL override (ad-hoc endpoint). */
  baseUrl?: string;
  /** Adapter type: `ollama`, `openai`, `vllm`, or `openai-compatible`. */
  adapterType?: string;
  /** Override the active config directory. */
  configDir?: string;
  /** Working directory for tool execution. */
  workingDirectory?: string;
  /** Sampling temperature. */
  temperature?: number;
  /** Maximum output tokens per response. */
  maxTokens?: number;
  /** Maximum agent loop iterations (1-100). */
  maxTurns?: number;
  /** Estimated-token ceiling; the run stops with `budget_exceeded` when exceeded. */
  maxTokenBudget?: number;
  /** Path to a system-prompt file that replaces the default prompt. */
  systemPrompt?: string;
  /** Text appended to the resolved system prompt. */
  appendSystemPrompt?: string;
  /** Auto-approve all tool calls. Mutually exclusive with {@link approvalPolicy}. */
  yolo?: boolean;
  /** Approval policy when not using {@link yolo}. Defaults to mux's own default (`deny`). */
  approvalPolicy?: ApprovalPolicy;
  /** Confinement posture. */
  sandbox?: SandboxPosture;
  /** Tool-name globs; only matching tools are allowed. */
  allowTools?: string[];
  /** Tool-name globs to deny (deny wins over allow). */
  denyTools?: string[];
  /** Additional writable roots honored under `workspace-write`. */
  addDir?: string[];
  /** MCP server config as a file path or inline JSON; enables MCP for the run. */
  mcpConfig?: string;
  /** Use only {@link mcpConfig} servers, ignoring the config directory's `mcp-servers.json`. */
  strictMcpConfig?: boolean;
  /** Disable TLS certificate validation for mux-owned network requests. */
  ignoreCertErrors?: boolean;
  /** Extra environment variables for the spawned process (merged over `process.env`). */
  env?: Record<string, string>;
}

/** Per-call overrides, plus an optional session id used by {@link Thread}. */
export type RunOverrides = Partial<MuxOptions> & {
  /** Run under a specific mux session id (created if absent, resumed if present). */
  sessionId?: string;
};

/** Common fields present on every JSONL event. */
export interface MuxEventBase {
  contractVersion: number;
  eventType: string;
  timestampUtc: string;
}

/** The `run_started` event: effective runtime and capability metadata for the run. */
export interface RunStartedEvent extends MuxEventBase {
  eventType: "run_started";
  runId: string;
  sessionId: string;
  endpointName: string;
  adapterType: string;
  baseUrl: string;
  model: string;
  commandName: string;
  approvalPolicy: string;
  workingDirectory: string;
  maxIterations: number;
  toolsEnabled: boolean;
  sandboxPosture: string;
  mcp: { supported: boolean; configured: boolean; serverCount: number };
}

/** A chunk of assistant-visible text. */
export interface AssistantTextEvent extends MuxEventBase {
  eventType: "assistant_text";
  text: string;
}

/** A tool call proposed by the model. */
export interface ToolCallProposedEvent extends MuxEventBase {
  eventType: "tool_call_proposed";
  toolCall: { id: string; name: string; arguments: unknown };
}

/** A tool call approved for execution. */
export interface ToolCallApprovedEvent extends MuxEventBase {
  eventType: "tool_call_approved";
  toolCallId: string;
}

/** A completed tool call and its result. */
export interface ToolCallCompletedEvent extends MuxEventBase {
  eventType: "tool_call_completed";
  toolCallId: string;
  toolName: string;
  elapsedMs: number;
  result: { toolCallId: string; success: boolean; content: unknown };
}

/** A structured error, with a stable code and failure classification. */
export interface ErrorEvent extends MuxEventBase {
  eventType: "error";
  code: string;
  errorCode: string;
  message: string;
  failureCategory?: string;
}

/** A periodic progress heartbeat carrying the current step number. */
export interface HeartbeatEvent extends MuxEventBase {
  eventType: "heartbeat";
  stepNumber: number;
}

/** Provider-reported token usage for a run (present on `run_completed` unless `--no-stats` was used). */
export interface MuxUsage {
  /** Prompt/input tokens consumed. */
  inputTokens: number;
  /** Completion/output tokens generated. */
  outputTokens: number;
  /** Total tokens as reported by the provider. */
  totalTokens: number;
  /** mux's own heuristic estimate of the final context size. */
  estimatedTokens: number;
}

/** The terminal event summarizing the run. */
export interface RunCompletedEvent extends MuxEventBase {
  eventType: "run_completed";
  runId: string;
  sessionId: string;
  status: string;
  iterationsCompleted: number;
  toolCallCount: number;
  errorCount: number;
  assistantTextChars: number;
  durationMs: number;
  finalEstimatedTokens: number;
  compactionCount: number;
  /** Token usage; omitted when the run used `--no-stats`. */
  usage?: MuxUsage;
}

/** Any event not otherwise typed (forward-compatible with new event types in a known contract version). */
export interface UnknownEvent extends MuxEventBase {
  [key: string]: unknown;
}

/** The discriminated union of all events mux emits in `jsonl` mode. */
export type MuxEvent =
  | RunStartedEvent
  | AssistantTextEvent
  | ToolCallProposedEvent
  | ToolCallApprovedEvent
  | ToolCallCompletedEvent
  | ErrorEvent
  | HeartbeatEvent
  | RunCompletedEvent
  | UnknownEvent;

/** The aggregated outcome of a single run, collected from the event stream. */
export interface RunResult {
  /** The concatenated assistant-visible text for the run. */
  text: string;
  /** The session id the run belonged to (empty when not persisted). */
  sessionId: string;
  /** The final run status (`completed`, `completed_with_errors`, `max_iterations_reached`, `budget_exceeded`, or `unknown`). */
  status: string;
  /** The process exit code: 0 success, 1 error, 2 tool call denied. */
  exitCode: number;
  /** Iterations completed, from the terminal `run_completed` event. */
  iterationsCompleted: number;
  /** Number of tool calls handled. */
  toolCallCount: number;
  /** Number of error events emitted. */
  errorCount: number;
  /** Wall-clock duration in milliseconds. */
  durationMs: number;
  /** Estimated tokens in the final conversation state. */
  finalEstimatedTokens: number;
  /** Provider-reported prompt/input tokens (0 when unreported or `--no-stats` was used). */
  inputTokens: number;
  /** Provider-reported completion/output tokens. */
  outputTokens: number;
  /** Provider-reported total tokens. */
  totalTokens: number;
  /** Captured stderr (progress/errors in text mode; usually empty in jsonl mode). */
  stderr: string;
  /** Every event observed during the run, in order. */
  events: MuxEvent[];
}

/** Internal: the live handles for one spawned run. */
interface SpawnHandles {
  generator: AsyncGenerator<MuxEvent>;
  exitCode: Promise<number>;
  stderr: Promise<string>;
}

/**
 * A driver for the mux CLI. Construct once with shared options, then {@link run} or {@link runStreamed}.
 * For multi-turn conversations, use {@link startThread} / {@link resumeThread}.
 */
export class Mux {
  readonly #options: MuxOptions;

  /**
   * @param options Shared options applied to every call (overridable per call).
   */
  constructor(options: MuxOptions = {}) {
    this.#options = options;
  }

  /**
   * Runs a prompt to completion and returns the aggregated {@link RunResult}.
   * @param prompt The user prompt.
   * @param overrides Per-call option overrides.
   */
  async run(prompt: string, overrides: RunOverrides = {}): Promise<RunResult> {
    const handles = this.spawnRun(prompt, overrides);

    const events: MuxEvent[] = [];
    let text = "";
    let started: RunStartedEvent | undefined;
    let completed: RunCompletedEvent | undefined;

    for await (const event of handles.generator) {
      events.push(event);
      if (event.eventType === "assistant_text") {
        text += (event as AssistantTextEvent).text;
      } else if (event.eventType === "run_started") {
        started = event as RunStartedEvent;
      } else if (event.eventType === "run_completed") {
        completed = event as RunCompletedEvent;
      }
    }

    const exitCode = await handles.exitCode;
    const stderr = await handles.stderr;

    return {
      text,
      sessionId: completed?.sessionId ?? started?.sessionId ?? "",
      status: completed?.status ?? "unknown",
      exitCode,
      iterationsCompleted: completed?.iterationsCompleted ?? 0,
      toolCallCount: completed?.toolCallCount ?? 0,
      errorCount: completed?.errorCount ?? 0,
      durationMs: completed?.durationMs ?? 0,
      finalEstimatedTokens: completed?.finalEstimatedTokens ?? 0,
      inputTokens: completed?.usage?.inputTokens ?? 0,
      outputTokens: completed?.usage?.outputTokens ?? 0,
      totalTokens: completed?.usage?.totalTokens ?? 0,
      stderr,
      events,
    };
  }

  /**
   * Runs a prompt and yields each event as it arrives. The process is spawned when iteration begins and
   * torn down when it ends; abandon iteration (e.g. `break`) to stop consuming.
   * @param prompt The user prompt.
   * @param overrides Per-call option overrides.
   */
  runStreamed(prompt: string, overrides: RunOverrides = {}): AsyncGenerator<MuxEvent> {
    return this.spawnRun(prompt, overrides).generator;
  }

  /**
   * Starts a multi-turn thread. Every turn runs under the same mux session id, so history accumulates
   * across turns in mux's own session store.
   * @param sessionId An explicit session id; a new one is generated when omitted.
   */
  startThread(sessionId?: string): Thread {
    return new Thread(this, sessionId ?? randomUUID().replace(/-/g, ""));
  }

  /**
   * Resumes a previously persisted thread by its session id.
   * @param sessionId The session id to resume.
   */
  resumeThread(sessionId: string): Thread {
    if (!sessionId) {
      throw new Error("resumeThread requires a non-empty session id.");
    }
    return new Thread(this, sessionId);
  }

  private spawnRun(prompt: string, overrides: RunOverrides): SpawnHandles {
    const merged: MuxOptions = { ...this.#options, ...overrides };
    const muxPath = merged.muxPath ?? "mux";
    const prefix = merged.muxArgs ?? [];
    const args = [...prefix, ...buildPrintArgs(merged, overrides.sessionId, prompt)];

    const child = spawn(muxPath, args, {
      env: { ...process.env, ...merged.env },
      stdio: ["ignore", "pipe", "pipe"],
    });

    let resolveExit: (code: number) => void = () => {};
    let rejectExit: (error: unknown) => void = () => {};
    const exitCode = new Promise<number>((resolve, reject) => {
      resolveExit = resolve;
      rejectExit = reject;
    });
    child.on("close", (code) => resolveExit(code ?? 0));
    child.on("error", (error) => rejectExit(error));

    let stderrText = "";
    if (child.stderr) {
      child.stderr.setEncoding("utf8");
      child.stderr.on("data", (chunk: string) => {
        stderrText += chunk;
      });
    }
    const stderr = exitCode.then(() => stderrText).catch(() => stderrText);

    const stdout = child.stdout;
    async function* generate(): AsyncGenerator<MuxEvent> {
      if (!stdout) {
        return;
      }
      const lines = createInterface({ input: stdout });
      for await (const line of lines) {
        const trimmed = line.trim();
        if (trimmed.length === 0) {
          continue;
        }
        let parsed: MuxEvent;
        try {
          parsed = JSON.parse(trimmed) as MuxEvent;
        } catch {
          continue;
        }
        yield parsed;
      }
    }

    return { generator: generate(), exitCode, stderr };
  }
}

/**
 * A multi-turn conversation bound to a single mux session id. Each {@link run} / {@link runStreamed}
 * continues the same persisted session, so turn N sees turns 1..N-1.
 */
export class Thread {
  readonly #mux: Mux;

  /** The mux session id this thread runs under. */
  readonly id: string;

  /**
   * @param mux The owning client.
   * @param id The mux session id this thread runs under.
   */
  constructor(mux: Mux, id: string) {
    this.#mux = mux;
    this.id = id;
  }

  /**
   * Runs the next turn to completion.
   * @param prompt The user prompt for this turn.
   * @param overrides Per-call option overrides.
   */
  run(prompt: string, overrides: RunOverrides = {}): Promise<RunResult> {
    return this.#mux.run(prompt, { ...overrides, sessionId: this.id });
  }

  /**
   * Runs the next turn and yields its events.
   * @param prompt The user prompt for this turn.
   * @param overrides Per-call option overrides.
   */
  runStreamed(prompt: string, overrides: RunOverrides = {}): AsyncGenerator<MuxEvent> {
    return this.#mux.runStreamed(prompt, { ...overrides, sessionId: this.id });
  }
}

/**
 * Builds the `mux print` argument vector from options. Exported for testing and for callers that want to
 * inspect exactly what would be spawned.
 * @param options The effective options.
 * @param sessionId An optional session id for the run.
 * @param prompt The prompt (placed last).
 */
export function buildPrintArgs(options: MuxOptions, sessionId: string | undefined, prompt: string): string[] {
  const args: string[] = ["print", "--output-format", "jsonl"];

  if (options.configDir) args.push("--config-dir", options.configDir);
  if (options.endpoint) args.push("--endpoint", options.endpoint);
  if (options.model) args.push("--model", options.model);
  if (options.baseUrl) args.push("--base-url", options.baseUrl);
  if (options.adapterType) args.push("--adapter-type", options.adapterType);
  if (options.workingDirectory) args.push("--working-directory", options.workingDirectory);
  if (typeof options.temperature === "number") args.push("--temperature", String(options.temperature));
  if (typeof options.maxTokens === "number") args.push("--max-tokens", String(options.maxTokens));
  if (typeof options.maxTurns === "number") args.push("--max-turns", String(options.maxTurns));
  if (typeof options.maxTokenBudget === "number") args.push("--max-token-budget", String(options.maxTokenBudget));
  if (options.systemPrompt) args.push("--system-prompt", options.systemPrompt);
  if (options.appendSystemPrompt) args.push("--append-system-prompt", options.appendSystemPrompt);

  if (options.yolo) {
    args.push("--yolo");
  } else if (options.approvalPolicy) {
    args.push("--approval-policy", options.approvalPolicy);
  }

  if (options.sandbox) args.push("--sandbox", options.sandbox);
  if (options.allowTools && options.allowTools.length > 0) args.push("--allow-tools", options.allowTools.join(","));
  if (options.denyTools && options.denyTools.length > 0) args.push("--deny-tools", options.denyTools.join(","));
  if (options.addDir) {
    for (const dir of options.addDir) {
      args.push("--add-dir", dir);
    }
  }

  if (options.mcpConfig) args.push("--mcp-config", options.mcpConfig);
  if (options.strictMcpConfig) args.push("--strict-mcp-config");
  if (options.ignoreCertErrors) args.push("--ignore-cert-errors");
  if (sessionId) args.push("--session-id", sessionId);

  args.push(prompt);
  return args;
}
