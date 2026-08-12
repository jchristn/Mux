# Mux — Display Model Thinking (`THINKING.md`)

**Feature:** Capture and optionally display a reasoning model's *thinking* (its chain-of-thought / reasoning summary) in the TUI and in headless output, off by default and toggleable. Complements the reasoning-effort control shipped in v0.8.0 (see `archive/EFFORT.md`): effort decides *how hard* the model thinks; this decides *whether you see it*.
**Target release:** Mux **`v0.8.0`** — no version bump. This ships within the current 0.8.0 line as an additive feature.
**Depends on:** PolyPrompt **`2.2.0`** (a new upstream release — see Phase 0). Today PolyPrompt's streaming chunks carry only final `content`, so mux has no thinking to show until PolyPrompt exposes a reasoning channel.
**Status:** ☐ Not started ☐ In progress ☐ Complete
**Owner:** _(assign)_
**Drafted:** 2026-08-12 · **Release date:** _TBD_

---

## How to use this document

Every task is a checkbox. Annotate as you go: `- [x]` done · `- [~]` in progress · `- [ ]` not started · `- [!]` blocked (add a note after an em dash). Keep the **Progress log** at the end current — it is the one place a reviewer looks for status. Do not delete finished tasks; check them off so the history stays auditable.

File paths are relative to `C:\Code\Mux` unless prefixed with `polyprompt:` (which means `C:\Code\PolyPrompt`). Line references reflect the tree at drafting time; confirm before editing.

---

## 1. Why this matters

Reasoning models spend tokens thinking before they answer. That thinking is often the most useful part of the response for a developer: it shows the model weighing an approach, catching its own mistake, or deciding which file to read next. Right now mux throws it away. `LlmClient.StreamAsync` yields only the assistant's final text, because that is all PolyPrompt hands it — the streamed reasoning channel (OpenAI's `reasoning_content`, Ollama's `message.thinking`, Gemini's thought parts) is never parsed. A user who turns effort up to `high` gets a slower, more deliberate answer with no window into the deliberation.

The goal is a clean, opt-in window. Off by default, because most turns do not need it and a wall of chain-of-thought buries the answer. One toggle — `/thinking` in the shell, `--show-thinking` headless — turns it on. When on, thinking renders as a dimmed, clearly-labeled block above the answer, so it reads as context, not as the result. The answer itself stays exactly where it is and looks exactly as it does today.

Two decisions shape the whole design. Thinking is **display-only**: mux shows it but never feeds it back into the conversation it sends upstream, both because providers do not want their own reasoning echoed to them and because it would burn context for no benefit. And thinking is **captured regardless of the toggle** but **rendered only when asked**, so turning the toggle on mid-session shows thinking on the next turn without any reconfiguration of the pipeline.

---

## 2. Design

### 2.1 Terminology
*Thinking* (equivalently *reasoning*) is the model's pre-answer deliberation, distinct from the final assistant text. mux treats it as a separate stream with its own event, its own rendering, and its own on/off state. It is never mixed into the assistant text buffer and never persisted into conversation history.

### 2.2 Where thinking comes from (per provider)
Each backend emits reasoning on its own channel; PolyPrompt normalizes them (Phase 0):

| Backend | Streaming source | Non-streaming source |
|---|---|---|
| OpenAI-compatible (OpenAI, vLLM, many local servers) | `choices[].delta.reasoning_content` (also `delta.reasoning`) | `message.reasoning_content` |
| Ollama | `message.thinking` | `message.thinking` |
| Gemini | `candidates[].content.parts[]` where `part.thought == true` | same |

Reasoning only appears when the model is a reasoning model and thinking is enabled — which, for mux, is exactly when a reasoning **effort level** is set (the v0.8.0 feature). Effort and thinking-display are orthogonal controls that pair naturally: effort turns thinking *on at the model*, this feature turns its display *on in mux*.

### 2.3 The mux pipeline
- PolyPrompt exposes a reasoning delta on its streaming chunk and an accumulated reasoning string on its responses (Phase 0).
- `LlmClient.StreamAsync` emits a new `AssistantThinkingEvent` whenever a chunk carries reasoning text — always, regardless of any display setting.
- The interactive projector renders thinking only when the shell's thinking display is on; otherwise it drops the event. The headless formatter emits/suppresses it based on `--show-thinking`.
- Thinking is **not** written into `ConversationMessage` history, so the next turn's request is byte-for-byte what it is today.

### 2.4 The control
A single global setting, `MuxSettings.ShowThinking` (default `false`), backs the interactive toggle and persists to `settings.json`. `/thinking` flips it live (modeled on `/borders`). Headless runs use `--show-thinking`; it does not read or write the setting, it just governs that run. Off everywhere by default — turning it on is one action.

---

## 3. Phase 0 — PolyPrompt prerequisite (`polyprompt:`, released as 2.2.0)

Mux cannot display what it never receives, so PolyPrompt must surface the reasoning channel first. This phase lives in the PolyPrompt repository and ships as its own release; mux then depends on `2.2.0`. Keep it additive — no existing field changes, reasoning is simply absent (null/empty) for non-reasoning responses.

### 3.1 Models
- [ ] `polyprompt:src/PolyPrompt/Models/ToolChatStreamingChunk.cs` — add `string? ReasoningText` (a streamed reasoning delta; null when the chunk carries none).
- [ ] `polyprompt:src/PolyPrompt/Models/ToolChatStreamingResponse.cs` — add `string Reasoning` accumulated across chunks (mirrors how `Text` accumulates), plus optional `int ReasoningCharCount`.
- [ ] `polyprompt:src/PolyPrompt/Models/ToolChatResponse.cs` — add `string? Reasoning` for the non-streaming path.

### 3.2 Per-provider parsing
- [ ] OpenAI (`OpenAiClient.ReadOpenAiToolChatChunks`) — read `delta.reasoning_content` (fall back to `delta.reasoning`) into `ReasoningText`; in `PopulateOpenAiToolChatResponse`, read `message.reasoning_content` into `Reasoning`.
- [ ] Ollama (`OllamaClient`) — read `message.thinking` from streamed and non-streamed `/api/chat` responses.
- [ ] Gemini (`GeminiClient`) — read parts with `thought == true` into the reasoning channel (streamed and non-streamed), keeping non-thought parts as `Text`.

### 3.3 Accumulation, tests, release
- [ ] Accumulate `ReasoningText` deltas onto `ToolChatStreamingResponse.Reasoning` in `WrapToolChatChunksWithTiming` (alongside the existing `Text` accumulation).
- [ ] Add local Touchstone coverage: each provider's mock server emits reasoning, and the client surfaces it on the chunk and the accumulated response, while a non-reasoning response leaves it null/empty (backward-compatibility lock).
- [ ] Bump PolyPrompt to `2.2.0`; update its README and CHANGELOG; publish.

> Until 2.2.0 is published, the mux work below can proceed against a local PolyPrompt build, but the mux tests that assert thinking capture will fail against 2.1.0. Gate the mux merge on the 2.2.0 dependency being available.

---

## 4. Implementation — Mux.Core

### 4.1 New event type
- [ ] Add `AssistantThinking` to `src/Mux.Core/Enums/AgentEventTypeEnum.cs`.
- [ ] Create `src/Mux.Core/Agent/AssistantThinkingEvent.cs` mirroring `AssistantTextEvent` (a `Text` property carrying the thinking delta; `EventType = AgentEventTypeEnum.AssistantThinking`). One class per file, XML docs on all public members.

### 4.2 Capture in the bridge
- [ ] Bump PolyPrompt to `2.2.0` in `src/Mux.Core/Mux.Core.csproj`.
- [ ] In `src/Mux.Core/Llm/LlmClient.cs` `StreamAsync`, when a chunk carries `ReasoningText`, `yield return new AssistantThinkingEvent { Text = chunk.ReasoningText }` — emitted unconditionally (the display gate lives in the consumers). Keep emitting `AssistantTextEvent` from `chunk.Text` exactly as today; thinking is a separate event, never merged into the text buffer.
- [ ] Do **not** write thinking into `ConversationMessage` history anywhere. The request mux sends upstream is unchanged. Add a regression assertion for this in the bridge suite.

---

## 5. Implementation — Headless (Mux.Cli)

### 5.1 CLI flag
- [ ] Add `--show-thinking` → `bool ShowThinking` to `src/Mux.Cli/Commands/CommonSettings.cs` (with `[Description]`/`[CommandOption]`), and parse it in `CliArgumentParser.ParseCommon`.

### 5.2 JSONL and text behavior
- [ ] Add a `case AssistantThinkingEvent` to `src/Mux.Cli/Commands/StructuredOutputFormatter.cs` and map `AgentEventTypeEnum.AssistantThinking => "assistant_thinking"` in the event-type switch. Emit the event **only when `--show-thinking` is set**, so default `jsonl` streams stay lean; a consumer opts in.
- [ ] Text mode: with `--show-thinking`, write thinking to **stderr** (the progress channel), dimmed, so `stdout` remains the final answer only and `--output-last-message` / piping are unaffected. Without the flag, drop it.
- [ ] `run_started` already reports `reasoningEffort`; add a small `showThinking` boolean to the run metadata so a consumer can see whether thinking is being surfaced. When `--show-thinking` drove it, add `showThinking` to `cliOverridesApplied`.

---

## 6. Implementation — Interactive TUI (Mux.Cli/App)

### 6.1 Setting
- [ ] Add `ShowThinking` (`bool`, default `false`, JSON `showThinking`) to `src/Mux.Core/Models/MuxSettings.cs`, with a backing field and XML doc, next to `ShowBoundaryLines`.

### 6.2 `/thinking` toggle
- [ ] Register in the `MuxTuiApp` catalog next to `/borders` (~line 216):
  ```csharp
  _Catalog.Add(new CommandDescriptor("mux.thinking", "Toggle thinking display", null, ToggleThinking, "View", new[] { "thinking", "think", "reasoning-display" }));
  ```
  No key chord; reachable via `/thinking`, the F1 menu, and the command menu.
- [ ] Implement `ToggleThinking` modeled on `ToggleBoundaries` (~line 769): flip a `_ShowThinking` field under `_Sync`, persist via a `PersistThinking(bool)` best-effort writer (`MuxSettings.ShowThinking` → `SettingsLoader.SaveSettings`), and `WriteNotice(...)`. Notices for a toggle read like the boundary notice (`"Thinking display on."` / `"Thinking display off."`).
- [ ] Seed `_ShowThinking` from `MuxSettings.ShowThinking` at construction (the app already loads settings for `showBoundaries`).

### 6.3 Rendering
- [ ] Give `AgentEventProjector` a `ShowThinking` property (or constructor flag) and a thinking buffer parallel to `_AssistantText`. In `Project` (~line 129), add:
  ```csharp
  case AssistantThinkingEvent thinkingEvent:
      if (_ShowThinking) AppendThinking(thinkingEvent.Text);
      break;
  ```
  When off, the event is dropped (not buffered, not rendered). Flush the thinking block before the answer via the same `FinalizeAssistantBlock()` discipline the other cases use, so ordering is: thinking block, then answer, then tool calls.
- [ ] Render the thinking block dimmed and labeled (see §7 for the exact look), visually distinct from the answer. Keep it plain/dim — it must never be mistaken for the final response.
- [ ] Wire the shell's `_ShowThinking` into the projector when a turn's projector is created, and update live when `/thinking` toggles so the change takes effect on the next turn.

### 6.4 Menu / slash / help
- [ ] No extra work beyond registration: the F1 menu, slash router, and `/help` read the catalog, so `/thinking` appears automatically under **View**. Verify it renders and runs.

---

## 7. TUI layout and aesthetic changes (with examples)

The only new visual element is the thinking block in the transcript; the frame, panes, and key map are unchanged. Nothing renders at all when the toggle is off, so the default experience is identical to today.

### 7.1 Thinking block in the transcript (toggle on)

Thinking streams in first, dimmed and labeled, then the answer follows in its normal style:

```
  💭 thinking
     The user wants the retry to be idempotent. The current code re-sends on any
     5xx, but a 409 means the write already landed — retrying would double-apply.
     I should special-case 409 and treat it as success.

  Here's the fix: treat HTTP 409 as a successful write and skip the retry…
```

The `💭 thinking` header and its body render in dark grey (dim); the answer below renders in the normal assistant style. The two never blur together.

### 7.2 Toggle notice

`/thinking` prints a short notice in the same style as `/borders` (a period-terminated status line, consistent with the existing toggle notices — this is a state notice, not a switch notice):

```
  ▸ Thinking display on.
```

### 7.3 F1 command menu

`/thinking` appears under the **View** group automatically. No layout change beyond the new row:

```
  View
    Clear transcript          /clear         Ctrl+L
    Toggle sidebar            /sidebar       Ctrl+B
    Toggle thinking display   /thinking
    Toggle boundary lines     /borders
    Theme                     /theme
```

### 7.4 Off state

With the toggle off (the default), no thinking header, block, or spacing appears — the transcript is exactly what it is today. This is the point of capturing-but-not-rendering: turning `/thinking` on shows thinking starting with the next turn, with zero residual footprint when off.

---

## 8. Tests (Touchstone)

Descriptors in `Test.Shared/Suites`, no console output there, loopback strictly `127.0.0.1`, run through Test.Automated, Test.Xunit, and Test.Nunit on net8.0 and net10.0.

### 8.1 Capture (bridge)
- [ ] Extend `Test.Shared/Llm/LocalLlmTestServer` to emit reasoning on a marker (OpenAI `reasoning_content` deltas, Ollama `message.thinking`).
- [ ] `LlmBridgeSuite` — a streamed turn against a reasoning-emitting server produces `AssistantThinkingEvent`s whose concatenation equals the expected thinking, while the assistant `Text` is unchanged; a non-reasoning turn produces **no** thinking events.
- [ ] `LlmBridgeSuite` — thinking never enters conversation history: the follow-up request body contains no reasoning content (regression lock).

### 8.2 Rendering (TUI)
- [ ] `ProjectorSuite` — with `ShowThinking = true`, a thinking event renders a dimmed labeled block before the answer; with `ShowThinking = false`, the same event produces no output and does not disturb the answer block.
- [ ] `CommandSurfacesSuite` — `mux.thinking` is registered with its aliases and appears under **View**; the F1 menu lists it.
- [ ] `TuiShellSuite` / settings — `/thinking` flips and persists `MuxSettings.ShowThinking`.

### 8.3 Headless
- [ ] `HeadlessFeaturesSuite` — `--show-thinking` parses; with it set, `jsonl` includes `assistant_thinking` events; without it, none are emitted; `stdout` never carries thinking in text mode.
- [ ] `StructuredOutputFormatterSuite` — an `AssistantThinkingEvent` maps to `assistant_thinking` and is gated by the flag; `run_started.showThinking` reflects the run.

### 8.4 Run matrix
- [ ] `dotnet test src/Test.Xunit` and `src/Test.Nunit` green on net8.0 and net10.0; `dotnet run --project src/Test.Automated` exits 0; Release builds with **0 warnings**.

---

## 9. Documentation

Write the prose so it reads like the rest of mux's docs — direct and specific, no filler, no generic "This enables…" register.

### 9.1 README.md
- [ ] Options table — add `--show-thinking`.
- [ ] Interactive Commands block — add `/thinking` (alias `/think`).
- [ ] New **Model Thinking** subsection near Reasoning Effort — what thinking is, that it is off by default and display-only (never sent back upstream), the `/thinking` toggle and `--show-thinking` flag, and the `assistant_thinking` JSONL event. Note it appears only for reasoning models with an effort level set.
- [ ] JSONL contract bullets — add the `assistant_thinking` event and `run_started.showThinking`.

### 9.2 CONFIG.md
- [ ] Document `settings.json` `showThinking` (bool, default false) in the settings table.

### 9.3 USAGE.md
- [ ] Interactive: `/thinking` and the §7 mockup. Headless: `--show-thinking` with a worked example and the stdout/stderr split.

### 9.4 CHANGELOG.md
- [ ] Add to the existing **v0.8.0** entry (no new version) under **Added**: model-thinking display — capture via PolyPrompt 2.2.0, the `/thinking` toggle and `showThinking` setting, `--show-thinking`, and the `assistant_thinking` JSONL event; off by default and never persisted into history. Under **Changed**: PolyPrompt bumped `2.1.0 → 2.2.0`.

### 9.5 Repository requirements
- [ ] No version-stamp change — mux stays `0.8.0` (per the release target). Only the PolyPrompt dependency string moves to `2.2.0`.
- [ ] `DOCKERHUB_README.md`: not applicable (mux ships as a CLI, no image); note the decision in the PR if one is ever added.

---

## 10. Versioning

- [ ] **mux stays `v0.8.0`** — this is additive within the current line, so no product-version bump. Leave `Defaults.ProductVersion`, the `.csproj` `<Version>` stamps, and the README badge at `0.8.0`.
- [ ] Bump only the **PolyPrompt dependency** to `2.2.0` in `Mux.Core.csproj` once that package is published.
- [ ] Confirm additivity before merge: with the toggle and flag off (the defaults), behavior and output are unchanged from the shipped 0.8.0.

---

## 11. Standards compliance (c:\code\agents\requirements)

- **CODE_STYLE** — usings inside the namespace, system usings first then others alphabetically; XML docs on all public members and methods (none on private); validated members use `_PascalCase` backing fields; **no tuples**; `.ConfigureAwait(false)` on awaits; `CancellationToken` on async methods lacking a class-level token; no `var`; one class or enum per file (a dedicated `AssistantThinkingEvent.cs`); specific exceptions with meaningful messages; no `Console.WriteLine` in `Mux.Core`.
- **BACKEND_TEST_ARCHITECTURE** — descriptors in `Test.Shared` with no console output; loopback strictly `127.0.0.1`; exercised through Test.Automated, Test.Xunit, and Test.Nunit on net8.0 and net10.0.
- **REPOSITORY_REQUIREMENTS** — README, CONFIG, USAGE, and CHANGELOG updated in step; source stays under `src/`.
- **WRITING_DOCUMENTS** — README/CONFIG/USAGE/CHANGELOG prose and this plan avoid the generic "This…/These…" openings and stock connectors, vary sentence rhythm, and keep a clear point of view.

---

## 12. Acceptance criteria (definition of done)

- [ ] PolyPrompt `2.2.0` surfaces reasoning on streamed chunks and accumulated responses for all three providers; mux depends on it.
- [ ] `LlmClient` emits `AssistantThinkingEvent`s from the reasoning channel, and thinking never enters conversation history (proved by a bridge regression case).
- [ ] `/thinking` toggles display live and persists `showThinking`; the F1 menu and slash router list it.
- [ ] With the toggle on, thinking renders as a dimmed labeled block above the answer; with it off, the transcript is identical to today.
- [ ] `--show-thinking` gates headless output: `assistant_thinking` appears in `jsonl` only when set; text-mode thinking goes to stderr only, never stdout.
- [ ] All suites pass on net8.0 and net10.0 across Test.Automated, Test.Xunit, and Test.Nunit; Release builds with zero warnings.
- [ ] README, CONFIG, USAGE, and CHANGELOG updated; mux version stays `0.8.0`; PolyPrompt dependency is `2.2.0`.

---

## 13. Risks & open questions

- [ ] **Provider field variance.** OpenAI-compatible servers disagree on the reasoning field name (`reasoning_content` vs `reasoning`); some omit it entirely even when thinking. PolyPrompt should read both and tolerate absence. Verify against at least one hosted reasoning model and one local one (`gpt-oss` via Ollama). — _note:_ ______
- [ ] **Gemini via OpenAI-compatible adapter.** As with effort, a Gemini model reached through mux's OpenAI-compatible surface returns reasoning under the OpenAI shape, not Gemini thought parts. Thinking capture follows whatever the adapter speaks; document it alongside the effort caveat. — _note:_ ______
- [ ] **Volume and context.** Thinking can be long. It is display-only and never re-sent, so it does not consume context, but the transcript can grow fast — consider whether the block should be collapsed by default (show a one-line summary with expand) in a later iteration. Out of scope for the first cut. — _decision:_ ______
- [ ] **Non-streaming path.** Capture is scoped to `StreamAsync` (what the TUI and print `jsonl` use). Non-streaming `SendAsync` (e.g. compaction) will not surface thinking; confirm that is acceptable. — _decision:_ ______
- [ ] **Sidebar indicator (optional).** Decide whether to add a `THINK on/off` sidebar line for discoverability, mirroring the `EFFORT` line, or rely on the toggle notice alone. — _decision:_ ______

---

## 14. Progress log

_Add dated entries as work proceeds. Newest first._

| Date | Author | Update |
|---|---|---|
| _TBD_ | _ | _ |
