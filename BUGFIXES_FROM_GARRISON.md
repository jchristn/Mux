# Bug fixes and improvements found while integrating Mux into Garrison

Garrison (https://github.com/jchristn/Garrison) runs Mux headless (`mux --print --output-format jsonl`) as one of its agent harnesses, next to Claude Code and Codex. These are the Mux-side issues found while doing that. Garrison already works around each of them, so none blocks it; fixing them in Mux makes Mux easier to drive headless from any tool.

Source references are to commit `f4d4ea4` (before the fixes). Status values: **Open** (not fixed in Mux), **Fixed** (fixed in Mux, with the commit).

Last updated: 2026-10-10 (third pass: item 8 from a looping gpt-oss run; items 7 and 8 fixed in Mux 1.4.0).

---

## 1. An unresolved `${VAR}` in an endpoint API key is sent to the provider as the key

**Status:** Fixed in `60fd88a` (release 1.3.0). Unset references in `apiKey`, header values, `apiVersion`, `project`, and `region` fail before any request with `Endpoint '<name>': <field> references environment variable <VAR>, which is not set.`; JSONL emits an `error` event with code `config_unresolved_env` (category `configuration`) and exits 1. `probe` uses the same code, and `LlmClient` refuses to send an unresolved value from the TUI, server, and desktop too. **Severity:** medium (confusing failure; leaks the variable name to the provider).

**What happened.** Garrison writes an `endpoints.json` whose `apiKey` is `"${GARRISON_MUX_API_KEY}"` and sets that variable for the run. When the variable was missing (a Garrison configuration bug, since fixed), Mux sent the literal text `${GARRISON_MUX_API_KEY}` as the bearer token. The provider answered 401 and echoed it back: `Incorrect API key provided: ${GARRIS***********KEY}`.

**Why.** `LlmClient.ResolveConfigValue` calls `SettingsLoader.ExpandEnvironmentVariables`, which leaves a reference unchanged when the variable is not set (`return envValue ?? match.Value;` in `SettingsLoader.cs`, about line 1160). The unexpanded string then goes out in the `Authorization` header (`LlmClient.cs`, about lines 1116 and 1132).

**Suggested fix.** When a credential field (`apiKey`, and header values used for auth) still contains an unresolved `${VAR}`, `%VAR%`, or `$env:VAR` reference after expansion, fail before the first request with a clear error naming the variable, for example `Endpoint 'garrison': apiKey references environment variable GARRISON_MUX_API_KEY, which is not set.` Never send an unresolved reference as a credential. In JSONL mode, emit it as an `error` event with a stable code such as `config_unresolved_env` and exit non-zero.

---

## 2. A failed model request ends as `completed_with_errors` instead of a failure status

**Status:** Fixed in `60fd88a` (release 1.3.0). A run that ends on a model error (on any turn, not only the first) or on a context limit compaction cannot get under reports `failed`; `completed_with_errors` now means the model answered but something failed along the way. Exit codes are unchanged. Statuses and their exit codes are documented in docs/USAGE.md (Run statuses). **Severity:** medium for headless callers.

**What happened.** When every model request failed (401 above), the run produced no assistant output, yet `run_completed.status` was `completed_with_errors`. The process did exit with code 1, but a caller that reads the final event (as Garrison originally did) sees "completed" and reports success. Garrison now also requires exit code 0.

**Why.** `AgentLoop` picks the status from budget, iteration, and error counts only (`AgentLoop.cs`, about line 792): any error count above zero becomes `completed_with_errors`, whether the errors were recoverable tool failures or the model never answering.

**Suggested fix.** Distinguish "the run finished but some tool calls failed" from "the run could not talk to the model". For example, report `failed` (or a dedicated `llm_error`) when the run ends because an LLM request failed and no assistant turn completed, and keep `completed_with_errors` for runs that produced an answer despite tool errors. Document the status values and their exit codes in the JSONL contract.

---

## 3. Print-mode `run_completed.usage` omits cached and reasoning tokens

**Status:** Fixed in `60fd88a` (release 1.3.0). `usage` (JSONL `run_completed` and the `json` summary) adds `cachedTokens`, `reasoningTokens`, and `costUsd` (from `pricing.json`; omitted when the model has no rate). **Severity:** low (missing telemetry).

**What happened.** Garrison shows input, output, cached, and reasoning tokens for every run. Claude Code and Codex report cached and reasoning tokens; Mux runs always show 0 for both.

**Why.** Mux tracks them: `LlmUsage` has `CachedTokens` and `ReasoningTokens`, accumulated per call (`LlmUsage.cs`, about lines 24 to 52). But `AgentEventSerializer.FormatUsage` (about line 260) only writes `inputTokens`, `outputTokens`, `totalTokens`, and `estimatedTokens`, and `RunCompletedEvent` has no fields for the other two.

**Suggested fix.** Add `CachedTokens` and `ReasoningTokens` to `RunCompletedEvent`, fill them from `_LlmClient.CumulativeUsage`, and write `cachedTokens` and `reasoningTokens` in `FormatUsage`. If Mux knows the price (it has `pricing.json`), a `costUsd` field in the same object would let callers report cost without their own price table.

---

## 4. The context window silently defaults to 32,768 tokens

**Status:** Fixed in `60fd88a` (release 1.3.0). `run_started.contextWindowSource` is `endpoint`, `cli`, or `default`, and a new `--context-window` flag sets it for one run. Not done: reading Ollama's `/api/show`, because mux does not send `num_ctx`, so the model's trained length would overstate what Ollama actually serves; and the stderr warning, since the field covers headless callers and a warning on every `--base-url` run would be noise. **Severity:** low (enhancement).

**What happened.** Garrison's generated endpoint did not set `contextWindow` (fixed on the Garrison side), so Mux used its default of 32,768 (`SettingsLoader.cs`, about line 100) against a model whose endpoint allows 131,072. Nothing in the output said the value was a default rather than a choice. `run_started` reports `contextWindow: 32768` and `usableInputLimit: 19661`, which is how Garrison noticed.

**Suggested fix.** Add a `contextWindowSource` field to `run_started` (`endpoint`, `cli`, or `default`). Optionally, for the Ollama adapter, read the model's context length from `/api/show` when the endpoint does not set one, and print a one-line warning on stderr when the default is used.

---

## 5. `run_started.mcp.supported` reads like a capability but means "MCP was configured"

**Status:** Fixed in `60fd88a` (release 1.3.0). `run_started.mcp.enabled` added; `supported` kept as an alias for one release and documented as deprecated. **Severity:** polish.

**What happened.** A run without `--mcp-config` reports `"mcp": { "supported": false, "configured": false, "serverCount": 0 }`. It reads as "this Mux cannot do MCP", which sent Garrison looking for a missing feature. The same build reports `supported: true` as soon as `--mcp-config` is passed, and MCP works.

**Suggested fix.** Rename the field to `enabled` (keeping `supported` for a release as an alias), or document that `supported` means MCP was enabled for this run.

---

## 6. No published release artifact for embedding

**Status:** Fixed in `60fd88a` and `571b169` (release 1.3.0, published 2026-10-10 at https://github.com/jchristn/mux/releases/tag/v1.3.0). Each GitHub Release now carries self-contained CLI archives `mux-<version>-<rid>.tar.gz` (linux-x64, linux-arm64, osx-x64, osx-arm64) and `mux-<version>-win-x64.zip`, each holding a single `mux` binary, plus `SHA256SUMS-*.txt`. Example: `curl -fsSL https://github.com/jchristn/mux/releases/download/v1.3.0/mux-1.3.0-linux-x64.tar.gz | tar xz -C /usr/local/bin mux`. **Severity:** polish (packaging).

**What happened.** Claude Code and Codex install with `npm install -g` at a pinned version. Mux has no published package or binary that Garrison's worker image can pin, so the image clones Mux at a pinned commit and builds it from source. That adds the .NET SDK and several minutes to every cold image build.

**Suggested fix.** Publish versioned release artifacts (for example a `dotnet tool`, self-contained binaries per platform on GitHub Releases, or both) so other tools can pin a Mux version with one command. Releases were made manual in `f4d4ea4`; a documented artifact per manual release is enough.

---

## 7. Print mode keeps no session unless a session flag is given, and says so only by leaving `sessionId` empty

**Status:** Fixed in `e4c96ac` (release 1.4.0). `run_completed.resumable` (and `resumable` in the `json` summary) is `false` for a plain `mux print` and for `--no-session-persistence`, `true` when the run is saved to a session; it is left out on surfaces that do not report it. docs/USAGE.md (Print Sessions) explains that only runs started with a session flag can be resumed and recommends `--session-id` when a resume may be needed. **Severity:** polish (documentation and ergonomics).

**What happened.** Garrison repairs invalid structured output by resuming the harness session with `--resume <id>`. For Mux, the resume never happened: without a session flag, `mux --print` keeps no session and reports an empty `sessionId`, so there was nothing to resume. Garrison now passes `--session-id <run id>` on the first run when it may need to resume, and resume then works (verified with `homedns-gpt-oss-20b`).

**Why.** This is deliberate (`PrintCommand.cs`, about line 284: only `--resume`, `--continue`, `--session-id`, or `--fork-session` engages the session store). The only signal to a headless caller is an empty `sessionId` in `run_started` and `run_completed`.

**Suggested fix.** Document in docs/USAGE.md, next to the JSONL contract, that print runs are not resumable unless started with `--session-id` (or another session flag), and that an empty `sessionId` means "not resumable". Optionally add `"resumable": true|false` to `run_completed` so callers do not have to infer it.

## 8. A model stuck in a repetition loop runs until the caller stops it

**Status:** Fixed in `e4c96ac` (release 1.4.0). A repetition guard stops the stream when the last 2,000 characters of the answer repeat one unit of up to 64 characters, or the last 1,000 characters contain no letters or digits, and ends the run `failed` with error code `degenerate_output` (category `backend`, exit 1); pending tool calls from that turn are dropped. On by default on every surface (not only print); `"repetitionGuardEnabled": false` or `--no-repetition-guard` turns it off. The related note is addressed too: docs/USAGE.md (Interactive Features in Print Mode) lists memory, skills, project instructions, task planning, hooks, and the guard, with how to turn each off. **Severity:** medium for headless callers.

**What happened.** Asked to print its instructions, `gpt-oss:20b` (Ollama adapter, Mux 1.3.0, `--print --output-format jsonl`) printed them and then streamed `"*"` and `" \n"` deltas alternately for about 50 seconds (over 2,000 `assistant_text` events, 8 KB of `* ` lines) until the caller cancelled it. The model never ended its turn, and Mux had nothing that noticed. The only bounds were `maxTokens` (8192 by default, minutes away at that speed) and the caller's own timeout.

**Suggested fix.** An optional degenerate-output guard in the agent loop: when the tail of the current assistant turn is a short unit repeated many times (for example the last 2,000 characters are a unit of 1 to 64 characters repeated at least 30 times, or only whitespace and punctuation for 1,000 characters), stop the stream, end the run with status `failed` and an error code such as `degenerate_output`, and exit non-zero. Expose it as a setting (on by default for print mode) and a flag. Garrison now does this on its side (Garrison task P3.15), but every headless caller would otherwise need its own.

**Related, not a bug.** Mux's persistent memory (`memoryEnabled`, default `true`) adds the `remember`, `recall`, and `forget` tools and a `## Memory` section to the system prompt in print mode too. For headless and automated use that is usually unwanted. Garrison now writes `"memoryEnabled": false`; Mux could document in docs/USAGE.md which interactive features apply to `--print` and how to turn them off together.

## Review of 60fd88a (Mux 1.3.0)

Reviewed from Garrison on 2026-10-10. Items 1 to 6 behave as described and Garrison now relies on them: the worker image builds Mux at `60fd88a`, the parser reads `failed`, `cachedTokens`, `reasoningTokens`, `costUsd`, `contextWindowSource`, and `mcp.enabled` (falling back to `supported`), and maps `config_unresolved_env` and model 401s to Garrison's `NotCredentialed` outcome. Verified on K3S (k3d), minikube, and Docker with real runs.

One packaging note for item 6: the `v1.3.0` GitHub release is still a draft, so its archives cannot be downloaded yet; Garrison keeps building Mux from source until the release is published.

> Reply from Mux (2026-10-10): `v1.3.0` is not a draft. It was published at 18:23 UTC, and its assets download without authentication (for example `https://github.com/jchristn/mux/releases/download/v1.3.0/mux-1.3.0-linux-x64.tar.gz` returns 200). The first 1.3.0 release run uploaded only the Windows files; the second, about 30 minutes later, uploaded the rest, so a check during that window would have found the Linux archive missing. `v1.3.1`, `v1.3.2`, and `v1.4.0` are published the same way. Pin `v1.4.0` (or later) to get items 7 and 8.

Not an issue: when output does not match `--output-schema`, Mux exits non-zero with error code `schema_validation_failed` (category `validation`). That is distinguishable from a model failure, and Garrison now validates and repairs the output itself in that case.

## Not Mux issues (for the record)

- **Ollama tool-call quirks.** Garrison first suspected its generated endpoint needed `assembleToolCallDeltas`, `stripRequestFields`, and related settings. Mux's adapter defaults (`Defaults.OllamaDefaults` plus the `BackendQuirks` property defaults) already match a hand-tuned Ollama endpoint exactly, so nothing is needed.
- **Weaker models filling optional tool arguments with wrong values** (gpt-oss passed `tenantId: "global"` and an agent ID as `deploymentId` to Garrison's MCP tools). That is Garrison's tool schema and descriptions, tracked as Garrison gap G-19.
- **Slower time to first token with gpt-oss:20b** (about 11 seconds, against 1.6 seconds for Claude) comes from the model and hardware, not Mux.
