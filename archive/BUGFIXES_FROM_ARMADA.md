# Bug fixes found while running Mux as Armada's local-model Worker

Armada (https://github.com/jchristn/Armada) runs Mux headless as the Worker, Usability Engineer, Test Engineer and
Linter captains in a hybrid pipeline. In that pipeline, Claude Code and Codex plan and review, and a local model on
Ollama does the edits. These issues came up while comparing gpt-oss:20b, gpt-oss:120b, qwen3-coder:30b and
qwen3.8:27b on the same task (see `HYBRID_DEVELOPMENT_RESULTS.md` in the Armada repository). Armada works around
both of them, but each one costs every local model extra rework rounds.

Source references are to commit `f476ca3` (Mux v1.4.0). Status values: **Open** (not fixed in Mux), **Fixed** (fixed
in Mux, with the commit).

Last updated: 2026-10-16.

---

## 1. `edit_file`, `multi_edit` and `write_file` strip the UTF-8 BOM from the files they change

**Status:** Fixed in Mux 1.4.1 (this change). **Severity:** high for repositories that keep BOMs (most Visual Studio-created C# projects).

**What happened.** Every model that edited `src/Timestamps/Timestamp.cs` and `README.md` (both UTF-8 with BOM) in
jchristn/Timestamps through Mux's edit tools left them without the BOM. The Codex reviewer rejected each change as
"not additions-only: line 1 replaced, BOM removed". That sent the work back for rework every time, with no change in
behavior to fix. Models then tried to restore the BOM by rewriting the whole file from the shell. That often broke
the CRLF line endings, which caused another rejection. Only qwen3.8:27b avoided the problem, by noticing it and
editing the BOM file with a byte-level Python script instead of `edit_file`.

**Reproduction (no model needed).** A console app that references `Mux.Core` and calls the tools directly on a file
holding `EF BB BF "line one\r\nline two\r\n"`:

```
edit_file    BOM before=True after=False  CRLF after=True  first bytes=6C696E
multi_edit   BOM before=True after=False  CRLF after=True  first bytes=6C696E
write_file   BOM before=True after=False  CRLF after=True  first bytes=6C696E
```

All three keep the line endings, which is correct. All three drop the BOM.

**Why.**
- `EditFileTool` (`src/Mux.Core/Tools/Tools/EditFileTool.cs` lines 88 and 144) and `MultiEditTool`
  (`MultiEditTool.cs` lines 119 and 192) read with `File.ReadAllTextAsync`. That detects and strips the BOM. They
  then write with `File.WriteAllTextAsync(path, string)`, which always writes UTF-8 without a BOM.
- `WriteFileTool` (`WriteFileTool.cs` line 87) checks the existing file's line ending, but not its encoding.

**Suggested fix.**
- Before changing an existing file, detect its encoding and preamble from the first bytes: UTF-8 BOM, UTF-16 LE/BE,
  or none. Write the result with that same encoding, so the preamble is emitted only when the original had one.
- Apply the same rule in all three tools, the same way line endings are already kept.
- New files keep today's behavior (UTF-8, no BOM).
- Add `EditFileToolSuite`, `MultiEditToolSuite` and `WriteFileToolSuite` cases for a BOM file and a BOM-less file.
  Each case should check that the first three bytes match the original after an edit.

---

## 2. `enableMalformedToolCallRecovery` is never used, and an Ollama tool-call parse error ends the run

**Status:** Fixed in Mux 1.4.1 (this change). **Severity:** high for local models; it was the most common way gpt-oss runs died.

**What happened.** gpt-oss:20b and gpt-oss:120b sometimes emit tool-call arguments that are not valid JSON. Ollama
rejects them on the server and answers HTTP 500, for example:

```
{"error":"error parsing tool call: raw='{\"file_path\":\"src/Test.Shared/TimestampsSuites.cs\",\"limit\":15\",\"offset\":\"?\"]}', err=invalid character '\"' after object key:value pair"}
```

gpt-oss:20b also hit `{"error":"... token repeat limit reached"}` (a repetition loop). These 500s showed up 4 times
in the gpt-oss:20b run and 3 times in the gpt-oss:120b run. Each one ended the Mux run with `llm_error` and exit code
1, even though the endpoint has `"enableMalformedToolCallRecovery": true`. Armada now catches these exits and
relaunches the same mission, but each relaunch restarts the model's context from a summary note, which loses work.

**Why.**
- `BackendQuirks.EnableMalformedToolCallRecovery` is documented as "Whether mux should try to infer tool calls from
  malformed freeform assistant text". But nothing reads it. A search of `src/` finds it only in `BackendQuirks.cs`,
  in the settings copy in `SettingsLoader.cs` (line 1731), and in the two copy/disable sites in `AgentLoop.cs`
  (lines 1419 and 1457). No code path changes behavior based on it.
- `LlmClient.ToolChatAsync` retries only transport failures. It treats any HTTP status as definitive
  (`IsTransportFailure`, `LlmClient.cs` line 832) and throws `HttpRequestException` on a 500 (line 228). So the
  agent loop never gets a chance to recover.

**Suggested fix.**
- Implement the quirk, or remove it and its documentation so users are not misled.
- With the quirk on, when an Ollama-style 500 body starts with `error parsing tool call`, do not end the run. Retry
  the turn once or twice. For example, add a short system note before the retry: "Your last tool call had invalid
  JSON arguments: <raw>. Re-issue it with valid JSON." If the retries fail, fall back to asking for the call again.
  Count these retries separately in telemetry, so it is visible how often a model produces broken calls.
- Treat `token repeat limit reached` the same way: retry once with a note to stop repeating and continue, before
  failing the run.
- Keep the final failure code stable (`llm_error`, or a dedicated `malformed_tool_call`), so callers like Armada can
  classify it.

---

## Not Mux bugs, noted for context

- **HTTP 502 `Proxy error: The operation was canceled` after exactly 60 s.** This comes from the reverse proxy in
  front of Ollama, which times out cold loads of large models (gpt-oss:120b is 65 GB). It is not Mux. A warm-up
  request with `keep_alive` avoids it.
- **The 50-iteration limit.** gpt-oss:120b hit `max_iterations_reached` four times in one run. Mux is behaving as
  configured. Armada resumes the mission, and raising `maxAgentIterations` for large models is a settings choice.

## Mux implementation notes (1.4.1)

Encoding preservation is shared across all three tools; tests cover UTF-8 with/without BOM and UTF-16 LE/BE
with both CRLF and LF. BOM-marked UTF-32 is preserved too.

Generation recovery is limited to recognized Ollama HTTP 500 errors before a stream opens and nonstreaming
chat. It retries at most twice, adds a corrective note without echoing backend error contents, and leaves
the caller conversation intact. Unrelated HTTP errors remain definitive; the existing client-side repetition
guard still stops degenerate output. Retry telemetry uses the `reason` label.

Validation: Release solution build succeeds without warnings or errors. The four affected suites pass all
35 cases on .NET 8 and .NET 10; including RunOutcome gives 56 passing cases on .NET 10. A broader run exposes
skill-script subprocess stack overflows. The ToolchainSkills suite reproduces the identical 84 failures
(3 passing cases) on the original `f476ca3` main commit, independently of these fixes.
