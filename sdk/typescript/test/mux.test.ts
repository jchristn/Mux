import { test } from "node:test";
import assert from "node:assert/strict";
import { readFileSync, mkdtempSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { fileURLToPath } from "node:url";

import { Mux, buildPrintArgs, type MuxEvent, type RunResult } from "../src/index.ts";

const fakeMux = fileURLToPath(new URL("./fake-mux.mjs", import.meta.url));

// Builds a client whose "mux" is `node fake-mux.mjs`, recording argv to a fresh temp file per call.
function makeClient(extra: Record<string, unknown> = {}): { client: Mux; argvFile: string } {
  const dir = mkdtempSync(join(tmpdir(), "mux-sdk-"));
  const argvFile = join(dir, "argv.log");
  const client = new Mux({
    muxPath: process.execPath,
    muxArgs: [fakeMux],
    env: { FAKE_MUX_ARGV_FILE: argvFile, ...(extra.env as Record<string, string> | undefined) },
    ...extra,
  });
  return { client, argvFile };
}

function readArgvLines(argvFile: string): string[][] {
  return readFileSync(argvFile, "utf8")
    .split("\n")
    .filter((line) => line.length > 0)
    .map((line) => JSON.parse(line) as string[]);
}

test("buildPrintArgs constructs the expected flags", () => {
  const args = buildPrintArgs(
    {
      endpoint: "prod",
      model: "m",
      yolo: true,
      sandbox: "read-only",
      denyTools: ["delete_file", "run_process"],
      addDir: ["../a", "../b"],
      maxTurns: 3,
    },
    "sess-1",
    "do the thing",
  );

  assert.deepEqual(args.slice(0, 3), ["print", "--output-format", "jsonl"]);
  assert.ok(args.includes("--yolo"));
  assert.ok(!args.includes("--approval-policy"), "yolo suppresses approval-policy");
  assert.equal(args[args.indexOf("--sandbox") + 1], "read-only");
  assert.equal(args[args.indexOf("--deny-tools") + 1], "delete_file,run_process");
  assert.equal(args[args.indexOf("--session-id") + 1], "sess-1");
  assert.equal(args[args.indexOf("--max-turns") + 1], "3");
  // Two --add-dir occurrences, one per directory.
  assert.equal(args.filter((a) => a === "--add-dir").length, 2);
  // Prompt is last.
  assert.equal(args[args.length - 1], "do the thing");
});

test("approvalPolicy is used when yolo is not set", () => {
  const args = buildPrintArgs({ approvalPolicy: "deny" }, undefined, "p");
  assert.equal(args[args.indexOf("--approval-policy") + 1], "deny");
  assert.ok(!args.includes("--session-id"), "no session id when none supplied");
});

test("run aggregates the event stream into a RunResult", async () => {
  const { client } = makeClient();
  const result: RunResult = await client.run("hi");

  assert.equal(result.exitCode, 0);
  assert.equal(result.text, "hello world");
  assert.equal(result.status, "completed");
  assert.equal(result.sessionId, "gen-sid");
  assert.equal(result.finalEstimatedTokens, 42);
  assert.equal(result.inputTokens, 11);
  assert.equal(result.outputTokens, 22);
  assert.equal(result.totalTokens, 33);
  assert.ok(result.events.some((e: MuxEvent) => e.eventType === "run_started"));
  assert.ok(result.events.some((e: MuxEvent) => e.eventType === "run_completed"));
});

test("runStreamed yields typed events in order", async () => {
  const { client } = makeClient();
  const kinds: string[] = [];
  for await (const event of client.runStreamed("hi")) {
    kinds.push(event.eventType);
  }
  assert.equal(kinds[0], "run_started");
  assert.equal(kinds[kinds.length - 1], "run_completed");
  assert.ok(kinds.includes("assistant_text"));
});

test("a non-zero exit is surfaced with an error event", async () => {
  const { client } = makeClient({ env: { FAKE_MUX_EXIT: "2" } });
  const result = await client.run("hi");
  assert.equal(result.exitCode, 2);
  assert.ok(result.events.some((e: MuxEvent) => e.eventType === "error"));
});

test("run passes governance flags through to the CLI", async () => {
  const { client, argvFile } = makeClient({ yolo: true, sandbox: "workspace-write", denyTools: ["run_process"] });
  await client.run("hi");
  const argv = readArgvLines(argvFile)[0];
  assert.ok(argv.includes("--yolo"));
  assert.equal(argv[argv.indexOf("--sandbox") + 1], "workspace-write");
  assert.equal(argv[argv.indexOf("--deny-tools") + 1], "run_process");
});

test("a thread runs every turn under the same session id", async () => {
  const { client, argvFile } = makeClient();
  const thread = client.startThread("thread-123");
  assert.equal(thread.id, "thread-123");

  await thread.run("turn one");
  await thread.run("turn two");

  const argvLines = readArgvLines(argvFile);
  assert.equal(argvLines.length, 2, "two spawns recorded");
  for (const argv of argvLines) {
    assert.equal(argv[argv.indexOf("--session-id") + 1], "thread-123");
  }
});

test("startThread generates a session id when none is given", () => {
  const { client } = makeClient();
  const thread = client.startThread();
  assert.ok(thread.id.length > 0);
  assert.match(thread.id, /^[0-9a-f]+$/);
});
