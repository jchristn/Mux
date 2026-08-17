// A stand-in for the mux CLI used by the SDK tests. It ignores the model/backend entirely: it records the
// argv it received (so tests can assert flag construction), echoes back the requested --session-id (so
// thread continuity can be verified), and emits a canned jsonl event stream. Exit code is controlled by
// FAKE_MUX_EXIT.
import { appendFileSync } from "node:fs";

const argv = process.argv.slice(2);

const argvFile = process.env.FAKE_MUX_ARGV_FILE;
if (argvFile) {
  appendFileSync(argvFile, JSON.stringify(argv) + "\n");
}

function flagValue(name) {
  const index = argv.indexOf(name);
  return index >= 0 && index + 1 < argv.length ? argv[index + 1] : null;
}

const sessionId = flagValue("--session-id") ?? "gen-sid";
const exitCode = Number(process.env.FAKE_MUX_EXIT ?? "0");
const stamp = "2026-01-01T00:00:00.000Z";

function emit(obj) {
  process.stdout.write(JSON.stringify(obj) + "\n");
}

emit({
  contractVersion: 2,
  eventType: "run_started",
  timestampUtc: stamp,
  runId: "run-fake",
  sessionId,
  endpointName: "fake",
  adapterType: "openai-compatible",
  baseUrl: "http://localhost:0",
  model: "fake-model",
  commandName: "print",
  approvalPolicy: "AutoApprove",
  workingDirectory: ".",
  maxIterations: 50,
  toolsEnabled: true,
  sandboxPosture: flagValue("--sandbox") ?? "none",
  mcp: { supported: false, configured: false, serverCount: 0 },
});

emit({ contractVersion: 2, eventType: "assistant_text", timestampUtc: stamp, text: "hello " });
emit({ contractVersion: 2, eventType: "assistant_text", timestampUtc: stamp, text: "world" });

if (exitCode !== 0) {
  emit({
    contractVersion: 2,
    eventType: "error",
    timestampUtc: stamp,
    code: "print_error",
    errorCode: "print_error",
    message: "fake failure",
    failureCategory: "unknown",
  });
}

emit({
  contractVersion: 2,
  eventType: "run_completed",
  timestampUtc: stamp,
  runId: "run-fake",
  sessionId,
  status: exitCode === 0 ? "completed" : "completed_with_errors",
  iterationsCompleted: 1,
  toolCallCount: 0,
  errorCount: exitCode === 0 ? 0 : 1,
  assistantTextChars: 11,
  durationMs: 5,
  finalEstimatedTokens: 42,
  compactionCount: 0,
  usage: { inputTokens: 11, outputTokens: 22, totalTokens: 33, estimatedTokens: 42 },
});

process.exit(exitCode);
