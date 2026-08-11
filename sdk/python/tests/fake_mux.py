"""A stand-in for the mux CLI used by the SDK tests.

It ignores the model/backend entirely: it records the argv it received (so tests can assert flag
construction), echoes back the requested ``--session-id`` (so thread continuity can be verified), and
emits a canned jsonl event stream. Exit code is controlled by ``FAKE_MUX_EXIT``.
"""

import json
import os
import sys

argv = sys.argv[1:]

argv_file = os.environ.get("FAKE_MUX_ARGV_FILE")
if argv_file:
    with open(argv_file, "a", encoding="utf-8") as handle:
        handle.write(json.dumps(argv) + "\n")


def flag_value(name):
    if name in argv:
        index = argv.index(name)
        if index + 1 < len(argv):
            return argv[index + 1]
    return None


session_id = flag_value("--session-id") or "gen-sid"
exit_code = int(os.environ.get("FAKE_MUX_EXIT", "0"))
stamp = "2026-01-01T00:00:00.000Z"


def emit(obj):
    sys.stdout.write(json.dumps(obj) + "\n")


emit({
    "contractVersion": 1,
    "eventType": "run_started",
    "timestampUtc": stamp,
    "runId": "run-fake",
    "sessionId": session_id,
    "endpointName": "fake",
    "adapterType": "openai-compatible",
    "baseUrl": "http://localhost:0",
    "model": "fake-model",
    "commandName": "print",
    "approvalPolicy": "AutoApprove",
    "workingDirectory": ".",
    "maxIterations": 50,
    "toolsEnabled": True,
    "sandboxPosture": flag_value("--sandbox") or "none",
    "mcp": {"supported": False, "configured": False, "serverCount": 0},
})

emit({"contractVersion": 1, "eventType": "assistant_text", "timestampUtc": stamp, "text": "hello "})
emit({"contractVersion": 1, "eventType": "assistant_text", "timestampUtc": stamp, "text": "world"})

if exit_code != 0:
    emit({
        "contractVersion": 1,
        "eventType": "error",
        "timestampUtc": stamp,
        "code": "print_error",
        "errorCode": "print_error",
        "message": "fake failure",
        "failureCategory": "unknown",
    })

emit({
    "contractVersion": 1,
    "eventType": "run_completed",
    "timestampUtc": stamp,
    "runId": "run-fake",
    "sessionId": session_id,
    "status": "completed" if exit_code == 0 else "completed_with_errors",
    "iterationsCompleted": 1,
    "toolCallCount": 0,
    "errorCount": 0 if exit_code == 0 else 1,
    "assistantTextChars": 11,
    "durationMs": 5,
    "finalEstimatedTokens": 42,
    "compactionCount": 0,
})

sys.exit(exit_code)
