# Mux — Display Model Thinking (`THINKING.md`)

**Feature:** Capture a reasoning model's *thinking* from PolyPrompt and display it — visualized in the TUI chat window and surfaced in headless — controlled by a per-endpoint `showThinking` property that is configurable in the TUI. Complements reasoning effort (v0.8.0): effort decides *how hard* the model thinks; this decides *whether you see it*.
**Target release:** Mux **`v0.8.0`** — no version bump. Additive within the current line.
**Depends on:** PolyPrompt **`2.2.0`** — **published and referenced.** Its `ReasoningText` (streamed chunk) / `Reasoning` (accumulated response) channel is what mux reads.
**Status:** ✅ **Complete — implemented in v0.8.0**
**Owner:** Claude for Joel.
**Drafted:** 2026-08-12 · **Release date:** _TBD_

---

## How to use this document

Every task is a checkbox: `- [x]` done · `- [~]` in progress · `- [ ]` not started · `- [!]` blocked. Keep the **Progress log** current.

File paths are relative to `C:\Code\Mux`. Line references reflect the tree at drafting time; confirm before editing.

---

## 1. Why this matters

Reasoning models spend tokens thinking before they answer, and PolyPrompt 2.2.0 now hands that thinking to mux on a separate channel (`ToolChatStreamingChunk.ReasoningText`). Today mux drops it: `LlmClient.StreamAsync` yields only assistant text. A user who turns effort up to `high` gets a slower, more deliberate answer with no window into the deliberation.

This adds that window, and makes it a property of the endpoint. Because different endpoints point at different models — a reasoning model here, a plain one there — the natural place to say "show me the thinking" is on the endpoint itself. `showThinking` lives in `endpoints.json`, is edited in the endpoint form, flips live with `/thinking`, and travels into headless runs (or is overridden per run with `--show-thinking`). Off by default, so nothing changes until a user opts in.

Two rules keep it honest. Thinking is **display-only** — mux shows it but never folds it into the conversation it sends upstream, both because providers do not want their own reasoning echoed and because it would burn context. And it is **gated at the source**: `LlmClient` emits thinking events only when the active endpoint has `showThinking` set, so the whole pipeline — TUI and headless alike — honors one switch.

---

## 2. Design

### 2.1 The control: a per-endpoint property
`EndpointConfig.ShowThinking` (bool, default false), serialized as `showThinking`. It is edited in the endpoint Add/Edit form, flipped live by `/thinking` (which mutates the active endpoint and persists, mirroring `/effort`), and overridable per headless run by `--show-thinking`. One source of truth; when false, mux behaves exactly as before.

### 2.2 Capture and gating
`LlmClient.StreamAsync` reads `chunk.ReasoningText` from the PolyPrompt tool-chat stream and, **only when `_Endpoint.ShowThinking` is true**, yields a new `AssistantThinkingEvent`. Gating at the client means both surfaces honor the endpoint property without their own checks, and no thinking is produced when off.

### 2.3 Display-only, never in history
Thinking is a separate event, never merged into the assistant text buffer. `AgentLoop` accumulates conversation history from `AssistantTextEvent` only (its `else` branch already forwards other events without adding them to history), and the TUI projector accumulates `CapturedAssistantText` from text only. So the request mux sends on the next turn is byte-for-byte unchanged.

### 2.4 TUI visualization
The projector renders thinking as a dimmed, labeled block (`💭 thinking`) that streams in before the answer, visually distinct from the response. When the answer text starts, the thinking block is already above it. Nothing renders when `showThinking` is off (no events arrive).

### 2.5 Headless
`run_started` reports `showThinking`. In `jsonl`, thinking arrives as `assistant_thinking` events (only when on, since capture is gated). In `text` mode, thinking is written to **stderr** (dimmed), so `stdout` stays the answer and `--output-last-message`/piping are unaffected.

---

## 3. Implementation — Mux.Core

- [x] `src/Mux.Core/Mux.Core.csproj` — bump PolyPrompt `2.1.0` → `2.2.0`. *(done)*
- [x] `src/Mux.Core/Enums/AgentEventTypeEnum.cs` — add `AssistantThinking` (`assistant_thinking`).
- [x] `src/Mux.Core/Agent/AssistantThinkingEvent.cs` — new event mirroring `AssistantTextEvent` (a `Text` delta; `EventType = AssistantThinking`).
- [x] `src/Mux.Core/Models/EndpointConfig.cs` — add `ShowThinking` (bool, default false, JSON `showThinking`).
- [x] `src/Mux.Core/Settings/SettingsLoader.cs` — `CloneEndpoint` copies `ShowThinking`.
- [x] `src/Mux.Core/Llm/LlmClient.cs` — in `StreamAsync`, when `_Endpoint.ShowThinking` and `chunk.ReasoningText` is non-empty, `yield return new AssistantThinkingEvent { Text = chunk.ReasoningText }`. Never add thinking to history.
- [x] `src/Mux.Core/Agent/RunStartedEvent.cs` — add `ShowThinking` (bool).
- [x] `src/Mux.Core/Agent/AgentLoop.cs` — set `RunStartedEvent.ShowThinking` from the endpoint. (Its stream loop already forwards `AssistantThinkingEvent` via the `else` branch and excludes it from history.)

---

## 4. Implementation — Headless (Mux.Cli)

- [x] `src/Mux.Cli/Commands/CommonSettings.cs` — add `--show-thinking` (`bool ShowThinking`).
- [x] `src/Mux.Cli/Commands/CliArgumentParser.cs` — parse `--show-thinking`.
- [x] `src/Mux.Cli/Commands/CommandRuntimeResolver.cs` — when `--show-thinking` is set, set `endpoint.ShowThinking = true` for the run; add `"showThinking"` to `cliOverridesApplied`.
- [x] `src/Mux.Cli/Commands/StructuredOutputFormatter.cs` — add `case AssistantThinkingEvent` → `assistant_thinking` payload with `text`; map the enum to `"assistant_thinking"`; add `showThinking` to the `run_started` payload.
- [x] `src/Mux.Cli/Commands/PrintCommand.cs` — in `text` mode, write `AssistantThinkingEvent` to stderr (dimmed); `jsonl` already formats every event.

---

## 5. Implementation — Interactive TUI (Mux.Cli/App)

- [x] `src/Mux.Cli/App/AgentEventProjector.cs` — add a thinking buffer + `case AssistantThinkingEvent` that renders a dimmed `💭 thinking` block (its own lines, not markdown-rendered), finalized when the answer/tool block begins. Keep `CapturedAssistantText` text-only.
- [x] `src/Mux.Cli/App/EndpointFormModal.cs` — add a `Show thinking (reasoning)` checkbox seeded from `source.ShowThinking`, persisted on save.
- [x] `src/Mux.Cli/App/MuxTuiApp.cs` — register `/thinking` (aliases `think`, `reasoning-display`) under **View**; `ToggleThinking` flips the active endpoint's `ShowThinking`, persists to `endpoints.json`, re-applies via `_OnEndpointSelected`, updates the sidebar, and writes a notice. Track a `_ShowThinking` label for the sidebar.
- [x] `src/Mux.Cli/App/SidebarView.cs` — add a `THINK on/off` line (mirrors the `EFFORT` line).

---

## 6. TUI layout and aesthetics (examples)

### 6.1 Thinking block in the transcript (endpoint `showThinking` on)

```
  💭 thinking
     The user wants the retry idempotent. A 409 means the write already landed —
     retrying would double-apply. I should treat 409 as success and skip the retry.

  Here's the fix: treat HTTP 409 as a successful write and skip the retry…
```

The `💭 thinking` header and body render dim; the answer below renders normally. When `showThinking` is off, none of this appears — the transcript is identical to today.

### 6.2 `/thinking` toggle notice (period-free, matching the switch-notice style)

```
  ▸ Thinking display on for endpoint openai-gpt5
```

### 6.3 Endpoint form field

```
  Auto-approve tools        [x]
  Reasoning effort          high
  Show thinking (reasoning) [x]
  Max agent iterations
```

### 6.4 Sidebar

```
  ENDPOINT   openai-gpt5
  MODEL      gpt-5
  EFFORT     high
  THINK      on
```

### 6.5 F1 menu (auto from the catalog)

```
  View
    Toggle sidebar            /sidebar       Ctrl+B
    Toggle thinking display   /thinking
    Toggle boundary lines     /borders
```

---

## 7. Tests (Touchstone — positive and negative)

- [x] `LlmBridgeSuite` — with `ShowThinking = true` and a reasoning-emitting local server, the stream yields `AssistantThinkingEvent`s whose concatenation equals the expected thinking, the assistant `Text` is unchanged, and thinking never appears in the accumulated conversation message (history) — a regression lock. With `ShowThinking = false`, **no** thinking events are produced (negative).
- [x] `LlmBridgeSuite` — extend the local LLM server to emit `reasoning_content` deltas on a marker.
- [x] `EndpointConfigSuite` — `ShowThinking` round-trips through `endpoints.json`, defaults to false, and is carried by the endpoint clone.
- [x] `CommandRuntimeResolverSuite` — `--show-thinking` sets `endpoint.ShowThinking`; `cliOverridesApplied` includes `showThinking` only when the flag is set (negative: absent otherwise).
- [x] `HeadlessFeaturesSuite` — `--show-thinking` parses; `run_started` reports `showThinking`.
- [x] `StructuredOutputFormatterSuite` — an `AssistantThinkingEvent` maps to `assistant_thinking` with its text; a run without thinking emits none (negative).
- [x] `ProjectorSuite` — a thinking event renders a dimmed block and does **not** enter `CapturedAssistantText`; text and tool events are unaffected (negative-style separation check).
- [x] `CommandSurfacesSuite` — `/thinking` resolves to `mux.thinking` with its aliases and appears under **View**.
- [x] Run matrix: xUnit + NUnit green on net8.0 and net10.0; Test.Automated exits 0; Release builds with 0 warnings.

---

## 8. Documentation

- [x] README — `--show-thinking` in the options table; `/thinking` in interactive commands; a **Model Thinking** subsection (per-endpoint `showThinking`, display-only, the `/thinking` toggle and the endpoint field, the `assistant_thinking` JSONL event); JSONL bullets for `assistant_thinking` and `run_started.showThinking`.
- [x] CONFIG.md — document the `endpoints.json` `showThinking` field.
- [x] USAGE.md — `/thinking`, the endpoint field, and the headless flag with the stdout/stderr split.
- [x] CHANGELOG.md — add to the **v0.8.0** entry (no new version): model-thinking display gated by per-endpoint `showThinking`; PolyPrompt bumped `2.1.0 → 2.2.0`.
- [x] Confirm no version stamp changes — mux stays `0.8.0`.

---

## 9. Standards compliance (c:\code\agents\requirements)

CODE_STYLE (usings inside namespace, `_PascalCase` backing fields, **no tuples**, XML docs on public members, one type per file, `.ConfigureAwait(false)`, no `var`, no `Console.WriteLine` in `Mux.Core`); Touchstone four-project test layout on `127.0.0.1`; README/CONFIG/USAGE/CHANGELOG updated; human-voice docs.

---

## 10. Acceptance criteria

- [x] An endpoint can carry `showThinking` in `endpoints.json`; omitting it changes nothing (off by default).
- [x] With it on, thinking renders as a dimmed block in the TUI above the answer and is surfaced in headless (`assistant_thinking` in jsonl, stderr in text); with it off, output is identical to today.
- [x] Thinking never enters conversation history (bridge regression lock).
- [x] `/thinking` toggles and persists the active endpoint's property; the endpoint form edits it; the sidebar shows `THINK`.
- [x] `--show-thinking` overrides per run; `run_started.showThinking` and `cliOverridesApplied` reflect it.
- [x] Positive and negative tests pass on net8.0 and net10.0 across Test.Automated, Test.Xunit, and Test.Nunit; Release builds clean; mux stays `0.8.0`.

---

## 11. Progress log

| Date | Author | Update |
|---|---|---|
| 2026-08-12 | Claude (for Joel) | **Complete.** Bumped PolyPrompt to 2.2.0. Added `AssistantThinkingEvent` + enum, `EndpointConfig.ShowThinking`, gated capture in `LlmClient.StreamAsync`, `RunStartedEvent.ShowThinking`. Headless: `--show-thinking`, `assistant_thinking` JSONL event + text-mode stderr, `run_started.showThinking`, `cliOverridesApplied`. TUI: dimmed `💭 thinking` block in the projector (kept out of history), endpoint-form checkbox, `/thinking` toggle, sidebar `THINK` line. Docs (README/CONFIG/USAGE/CHANGELOG) updated; version stays 0.8.0. Tests: 8 new Touchstone cases (positive + negative — bridge capture on/off, config round-trip, resolver override, parse, formatter, command surface, projector separation). **514/514 pass** on net8.0 and net10.0 across xUnit and NUnit. |
| 2026-08-12 | Claude (for Joel) | Revised the plan for the shipped PolyPrompt 2.2.0 (Phase 0 no longer needed) and reworked the control from a global setting to a per-endpoint `showThinking` property (TUI-configurable, headless-overridable). Version stays 0.8.0. |
