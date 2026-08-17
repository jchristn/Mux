"""A thin Python driver for the mux CLI.

The SDK does not reimplement the agent. It spawns ``mux print --output-format jsonl``, parses the
newline-delimited event stream, and returns typed results. Multi-turn conversations are threaded through
mux's own session store (via ``--session-id``), so behavior matches the CLI exactly. Wrapping the CLI --
rather than binding to internals -- keeps the SDK backend-agnostic and in lockstep with the
``contractVersion``-versioned JSONL contract.
"""

from __future__ import annotations

import json
import os
import subprocess
import threading
import uuid
from dataclasses import dataclass, field, replace
from typing import Any, Dict, Iterator, List, Optional


@dataclass
class MuxOptions:
    """Options for a :class:`Mux` client. Every field maps to a ``mux print`` flag; all are optional and
    may be overridden per call."""

    #: The mux executable to spawn (default ``"mux"``, resolved on PATH).
    mux_path: str = "mux"
    #: Arguments inserted immediately after ``mux_path``, before ``print`` (e.g. a DLL path).
    mux_args: List[str] = field(default_factory=list)
    endpoint: Optional[str] = None
    model: Optional[str] = None
    base_url: Optional[str] = None
    adapter_type: Optional[str] = None
    config_dir: Optional[str] = None
    working_directory: Optional[str] = None
    temperature: Optional[float] = None
    max_tokens: Optional[int] = None
    max_turns: Optional[int] = None
    max_token_budget: Optional[int] = None
    system_prompt: Optional[str] = None
    append_system_prompt: Optional[str] = None
    #: Auto-approve all tool calls. Mutually exclusive with ``approval_policy``.
    yolo: bool = False
    #: Approval policy when not using ``yolo``: ``"auto"`` or ``"deny"``.
    approval_policy: Optional[str] = None
    #: Confinement posture: ``"none"``, ``"read-only"``, or ``"workspace-write"``.
    sandbox: Optional[str] = None
    allow_tools: Optional[List[str]] = None
    deny_tools: Optional[List[str]] = None
    add_dir: Optional[List[str]] = None
    mcp_config: Optional[str] = None
    strict_mcp_config: bool = False
    ignore_cert_errors: bool = False
    #: Extra environment variables for the spawned process (merged over ``os.environ``).
    env: Optional[Dict[str, str]] = None


@dataclass
class RunResult:
    """The aggregated outcome of a single run, collected from the event stream."""

    text: str
    session_id: str
    status: str
    exit_code: int
    iterations_completed: int
    tool_call_count: int
    error_count: int
    duration_ms: int
    final_estimated_tokens: int
    #: Provider-reported prompt/input tokens for the run (0 when the provider reported none or ``--no-stats`` was used).
    input_tokens: int
    #: Provider-reported completion/output tokens for the run.
    output_tokens: int
    #: Provider-reported total tokens for the run.
    total_tokens: int
    stderr: str
    events: List[Dict[str, Any]]


def build_print_args(options: MuxOptions, session_id: Optional[str], prompt: str) -> List[str]:
    """Build the ``mux print`` argument vector (excluding the executable) from options.

    Exported for testing and for callers that want to inspect exactly what would be spawned.
    """

    args: List[str] = ["print", "--output-format", "jsonl"]

    if options.config_dir:
        args += ["--config-dir", options.config_dir]
    if options.endpoint:
        args += ["--endpoint", options.endpoint]
    if options.model:
        args += ["--model", options.model]
    if options.base_url:
        args += ["--base-url", options.base_url]
    if options.adapter_type:
        args += ["--adapter-type", options.adapter_type]
    if options.working_directory:
        args += ["--working-directory", options.working_directory]
    if options.temperature is not None:
        args += ["--temperature", str(options.temperature)]
    if options.max_tokens is not None:
        args += ["--max-tokens", str(options.max_tokens)]
    if options.max_turns is not None:
        args += ["--max-turns", str(options.max_turns)]
    if options.max_token_budget is not None:
        args += ["--max-token-budget", str(options.max_token_budget)]
    if options.system_prompt:
        args += ["--system-prompt", options.system_prompt]
    if options.append_system_prompt:
        args += ["--append-system-prompt", options.append_system_prompt]

    if options.yolo:
        args.append("--yolo")
    elif options.approval_policy:
        args += ["--approval-policy", options.approval_policy]

    if options.sandbox:
        args += ["--sandbox", options.sandbox]
    if options.allow_tools:
        args += ["--allow-tools", ",".join(options.allow_tools)]
    if options.deny_tools:
        args += ["--deny-tools", ",".join(options.deny_tools)]
    if options.add_dir:
        for directory in options.add_dir:
            args += ["--add-dir", directory]

    if options.mcp_config:
        args += ["--mcp-config", options.mcp_config]
    if options.strict_mcp_config:
        args.append("--strict-mcp-config")
    if options.ignore_cert_errors:
        args.append("--ignore-cert-errors")
    if session_id:
        args += ["--session-id", session_id]

    args.append(prompt)
    return args


class Mux:
    """A driver for the mux CLI. Construct once with shared options, then :meth:`run` or
    :meth:`run_streamed`. For multi-turn conversations use :meth:`start_thread` / :meth:`resume_thread`."""

    def __init__(self, options: Optional[MuxOptions] = None) -> None:
        self._options = options or MuxOptions()

    def run(self, prompt: str, *, session_id: Optional[str] = None, **overrides: Any) -> RunResult:
        """Run a prompt to completion and return the aggregated :class:`RunResult`."""

        opts = replace(self._options, **overrides) if overrides else self._options
        args = [opts.mux_path, *opts.mux_args, *build_print_args(opts, session_id, prompt)]
        env = {**os.environ, **(opts.env or {})}

        events: List[Dict[str, Any]] = []
        text = ""
        started: Optional[Dict[str, Any]] = None
        completed: Optional[Dict[str, Any]] = None
        stderr_chunks: List[str] = []

        with subprocess.Popen(
            args,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            text=True,
            env=env,
        ) as proc:
            def drain_stderr() -> None:
                if proc.stderr is not None:
                    stderr_chunks.append(proc.stderr.read() or "")

            stderr_thread = threading.Thread(target=drain_stderr)
            stderr_thread.start()

            if proc.stdout is not None:
                for line in proc.stdout:
                    stripped = line.strip()
                    if not stripped:
                        continue
                    try:
                        event = json.loads(stripped)
                    except json.JSONDecodeError:
                        continue
                    events.append(event)
                    kind = event.get("eventType")
                    if kind == "assistant_text":
                        text += event.get("text", "")
                    elif kind == "run_started":
                        started = event
                    elif kind == "run_completed":
                        completed = event

            exit_code = proc.wait()
            stderr_thread.join()

        summary = completed or {}
        session = summary.get("sessionId") or (started or {}).get("sessionId", "")
        # The usage block is present on run_completed unless the run used --no-stats; default to zeros.
        usage = summary.get("usage") or {}

        return RunResult(
            text=text,
            session_id=session or "",
            status=summary.get("status", "unknown"),
            exit_code=exit_code,
            iterations_completed=summary.get("iterationsCompleted", 0),
            tool_call_count=summary.get("toolCallCount", 0),
            error_count=summary.get("errorCount", 0),
            duration_ms=summary.get("durationMs", 0),
            final_estimated_tokens=summary.get("finalEstimatedTokens", 0),
            input_tokens=usage.get("inputTokens", 0),
            output_tokens=usage.get("outputTokens", 0),
            total_tokens=usage.get("totalTokens", 0),
            stderr="".join(stderr_chunks),
            events=events,
        )

    def run_streamed(self, prompt: str, *, session_id: Optional[str] = None, **overrides: Any) -> Iterator[Dict[str, Any]]:
        """Run a prompt and yield each event as it arrives."""

        opts = replace(self._options, **overrides) if overrides else self._options
        args = [opts.mux_path, *opts.mux_args, *build_print_args(opts, session_id, prompt)]
        env = {**os.environ, **(opts.env or {})}

        with subprocess.Popen(
            args,
            stdout=subprocess.PIPE,
            stderr=subprocess.DEVNULL,
            text=True,
            env=env,
        ) as proc:
            if proc.stdout is not None:
                for line in proc.stdout:
                    stripped = line.strip()
                    if not stripped:
                        continue
                    try:
                        yield json.loads(stripped)
                    except json.JSONDecodeError:
                        continue

    def start_thread(self, session_id: Optional[str] = None) -> "Thread":
        """Start a multi-turn thread. Every turn runs under one mux session id, so history accumulates."""

        return Thread(self, session_id or uuid.uuid4().hex)

    def resume_thread(self, session_id: str) -> "Thread":
        """Resume a previously persisted thread by its session id."""

        if not session_id:
            raise ValueError("resume_thread requires a non-empty session id.")
        return Thread(self, session_id)


class Thread:
    """A multi-turn conversation bound to a single mux session id. Each run continues the same persisted
    session, so turn N sees turns 1..N-1."""

    def __init__(self, mux: Mux, thread_id: str) -> None:
        self._mux = mux
        self.id = thread_id

    def run(self, prompt: str, **overrides: Any) -> RunResult:
        """Run the next turn to completion."""

        return self._mux.run(prompt, session_id=self.id, **overrides)

    def run_streamed(self, prompt: str, **overrides: Any) -> Iterator[Dict[str, Any]]:
        """Run the next turn and yield its events."""

        return self._mux.run_streamed(prompt, session_id=self.id, **overrides)
