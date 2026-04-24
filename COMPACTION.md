# mux Context Compaction Scope

## Purpose

Scope a Codex CLI-like context visibility and compaction capability for `mux` that:

- shows the user how much context remains
- warns when the session is nearing the usable limit
- emits clear messages when auto-compaction happens
- lets the user manually compact the session with commands
- is concrete enough that a developer can implement it and mark work complete

This document is a scope and implementation guide, not a promise that the feature already exists.

## Current State

Observed in the current codebase:

- `src/Mux.Core/Agent/ContextWindowManager.cs` exists, but it is not wired into `InteractiveCommand` or `AgentLoop`.
- `src/Mux.Cli/Commands/InteractiveCommand.cs` stores `_ConversationHistory`, but only persists user and assistant text between turns. Tool call / tool result chatter is only kept inside the current `AgentLoop` run.
- `InteractiveCommand` has `/clear`, but no `/context` or `/compact`.
- `PrintCommand` already has a structured JSONL contract, so any compaction feature added to runtime behavior should also be surfaced there.
- Current token estimation is too rough for a user-facing "remaining context" meter because it does not account for:
  - output token reservation
  - tool definition payloads
  - per-message protocol overhead
  - MCP tool growth during a session

This matters because the feature is not just a UI affordance. It needs reliable enough accounting to prevent silent context overruns.

## Goals

- Give interactive users an estimated context budget view they can trust.
- Compact history before the model hits the hard window limit.
- Preserve recent turns and important session state when compacting.
- Allow manual compaction without forcing a full `/clear`.
- Surface compaction activity in both interactive text mode and `print --output-format jsonl`.
- Keep the v1 implementation compatible with the existing architecture and test layout.

## Non-Goals

- Persisting conversation history across mux process restarts.
- Building a live, continuously updating prompt-editor status bar while the user is typing.
- Exact tokenizer parity for every backend. The meter should be explicitly labeled as an estimate.
- Changing `probe` behavior.
- Replacing `/clear`; it remains the hard reset path.

## User Experience

### Interactive REPL

Recommended v1 behavior:

1. After each completed turn, mux prints a short dim context footer.
2. If usage crosses the warning threshold, mux prints a visible warning.
3. If mux auto-compacts, it prints a one-line compaction summary.
4. The user can inspect the current budget with `/context`.
5. The user can manually compact with `/compact`.

Example footer:

```text
[ctx] est. 24.8k / 92.1k used | 67.3k remaining | auto-compact: summary | keep 3 turns
```

Example warning:

```text
[warning] Context is nearing the usable limit (est. 81.4k / 92.1k). mux will compact before the next model call if needed.
```

Example compaction message:

```text
[compaction] Auto-compacted history: summarized 12 messages into 1 summary, est. 89.7k -> 41.2k tokens.
```

Important v1 choice:

- Do not try to inject a sticky context meter into the multi-line prompt renderer.
- Print status after turn completion and on explicit command instead.

This keeps the scope realistic given the current `LineBuffer` and prompt redraw implementation.

### Manual Commands

Add the following interactive commands:

- `/context`
  - Show the current estimated context budget, warning threshold, compaction strategy, preserved turn count, and last compaction result.
- `/compact`
  - Run the default compaction strategy immediately against the persisted session history.
- `/compact trim`
  - Run trim-only compaction without asking the model to summarize older turns.

Command semantics:

- `/clear` remains a full history reset.
- `/compact` should preserve recent turns and session summary state.
- `/endpoint <name>` should continue clearing conversation history and should also reset compaction state.
- `/system <text>` and `/mcp add/remove` should invalidate cached context stats because they change effective prompt/tool payload.

### Non-Interactive `print`

`print` mode does not need manual commands, but it should still expose compaction activity when it occurs inside a run.

Text mode:

- assistant text remains on `stdout`
- context warnings and compaction notices go to `stderr`

JSONL mode:

- emit structured context/compaction events
- keep existing lifecycle events
- do not mix human-readable compaction text into `stdout`

## Functional Requirements

### 1. Accurate Enough Budget Accounting

Compaction and "remaining context" should be based on estimated input budget, not just raw message text length.

The estimate should include:

- system prompt text
- persisted conversation history
- active in-run messages, including tool results
- tool definition payloads sent to the model
- fixed overhead per message / tool call
- reserved output tokens from `EndpointConfig.MaxTokens`
- configured safety margin from `MuxSettings.ContextWindowSafetyMarginPercent`

Recommended formula:

- `hardWindow = endpoint.ContextWindow`
- `reservedOutput = min(endpoint.MaxTokens, hardWindow / 2)`
- `safetyMargin = hardWindow * settings.ContextWindowSafetyMarginPercent / 100`
- `usableInputLimit = hardWindow - reservedOutput - safetyMargin`

If `usableInputLimit <= 0`, clamp to a sane minimum and emit a warning-level diagnostic in verbose mode.

### 2. Warning Threshold

Add a distinct warning threshold below the usable input limit.

Recommended default:

- `contextWarningThresholdPercent = 80`
- threshold is applied against `usableInputLimit`

Behavior:

- below threshold: no warning
- above threshold: warn, but do not compact yet unless necessary
- above usable input limit or projected next-call limit: compact before the next LLM call

### 3. Preserve Recent Turns

Compaction must not blindly delete the last few turns.

Recommended default:

- preserve the last `3` turns
- treat a "turn" as a user message plus the corresponding assistant message when present

For in-run compaction inside `AgentLoop`, preserve:

- system prompt
- synthetic session summary message if one exists
- current user message
- recent persisted turns
- the most recent tool/result messages needed for the next reasoning step

### 4. Prefer Summary, Fallback to Trim

Recommended default strategy:

- `summary_then_trim`

Behavior:

1. Select the oldest eligible messages outside the preserved region.
2. Ask the model for a compact session summary with tools disabled.
3. Replace those messages with one synthetic summary message.
4. Re-estimate usage.
5. If still too large, trim oldest eligible messages until under target.

Fallback rules:

- if summary generation fails, fall back to trim-only compaction
- if there are too few eligible messages to compact, emit a clear warning/error
- if the prompt is still too large after compaction, fail explicitly rather than allowing a silent backend error

### 5. Compact to a Lower Target, Not Barely Under the Line

Avoid repeated compactions every turn.

Recommended internal target:

- compact down to `60%` of `usableInputLimit`

This target does not need to be user-configurable in v1.

## Architecture

### New / Expanded Runtime Pieces

#### A. `ContextWindowManager`

Keep it as the accounting primitive, but expand it to handle:

- reserved output tokens
- tool definition estimation
- message/tool overhead constants
- warning threshold calculations
- richer result objects, not just `int`

Recommended additions:

- `ContextBudgetSnapshot` model
- `EstimateConversation(...)`
- `EstimateToolDefinitions(...)`
- `GetBudgetSnapshot(...)`
- `NeedsCompaction(...)`

#### B. `ConversationCompactor`

Add a new service in `src/Mux.Core/Agent/` responsible for:

- deciding which messages are eligible for compaction
- generating synthetic summaries when enabled
- trimming as fallback
- returning a `CompactionResult`

Recommended model objects:

- `CompactionMode`: `Auto`, `Manual`
- `CompactionStrategy`: `SummaryThenTrim`, `TrimOnly`
- `CompactionResult`
- `CompactionSummaryMessage` metadata or equivalent marker on `ConversationMessage`

#### C. Synthetic Summary Message

The compactor should insert one synthetic message representing condensed prior history.

Recommended content prefix:

```text
[mux summary generated automatically; older conversation condensed]
```

Recommended handling:

- insert after the primary system prompt
- mark it as synthetic in memory so future compactions can replace or collapse older summaries
- do not let multiple synthetic summaries accumulate indefinitely

Implementation note:

The current `ConversationMessage` model does not carry message metadata. Add a minimal flag if needed rather than inferring from content text forever.

### Where Compaction Runs

#### Preflight in `InteractiveCommand`

Before starting a new `AgentLoop` run:

1. Build a snapshot using:
   - current system prompt
   - current persisted history
   - current endpoint context window
   - currently available tools, including MCP
   - new user prompt
2. If over warning threshold, show warning.
3. If over the usable input limit and auto-compaction is enabled, compact persisted history before creating `AgentLoopOptions`.
4. If still too large, tell the user what happened and suggest `/compact trim` or `/clear`.

#### In-Run in `AgentLoop`

Inside the loop, compaction should happen before each model call, not after the backend rejects the request.

Recommended points:

- after `BuildConversation(prompt)`
- after appending assistant tool calls / text
- after appending tool results, before the next `StreamAsync(...)`

This is required because a single tool-heavy run can overflow the context window even when persisted session history is small.

## Event and Rendering Scope

### New Events

Add two new event types:

- `context_status`
- `context_compacted`

Recommended enum additions:

- `AgentEventTypeEnum.ContextStatus`
- `AgentEventTypeEnum.ContextCompacted`

Recommended event payloads:

#### `ContextStatusEvent`

- `scope`: `session_history` or `active_conversation`
- `estimatedTokens`
- `usableInputLimit`
- `remainingTokens`
- `remainingPercent`
- `warningThresholdTokens`
- `messageCount`
- `trigger`: `turn_completed`, `preflight`, `manual_command`, `post_compaction`
- `warningLevel`: `ok`, `approaching`, `critical`

#### `ContextCompactedEvent`

- `scope`
- `mode`: `auto` or `manual`
- `strategy`
- `messagesBefore`
- `messagesAfter`
- `estimatedTokensBefore`
- `estimatedTokensAfter`
- `summaryCreated`
- `reason`

### Existing Event Extensions

Extend `RunStartedEvent` with context metadata:

- `contextWindow`
- `reservedOutputTokens`
- `usableInputLimit`
- `warningThresholdTokens`
- `tokenEstimationRatio`

Extend `RunCompletedEvent` with:

- `finalEstimatedTokens`
- `compactionCount`

### Renderer Behavior

#### `EventRenderer`

Interactive text rendering should:

- render `ContextStatusEvent` as a dim footer or a warning line depending on `warningLevel`
- render `ContextCompactedEvent` as a one-line compaction summary

#### `PrintCommand`

Text mode should write:

- `ContextStatusEvent` warnings to `stderr`
- `ContextCompactedEvent` notices to `stderr`

`jsonl` mode should serialize both new events in `StructuredOutputFormatter`.

### Structured Contract Compatibility

Adding new event types should be treated as additive within the current contract version as long as:

- existing event shapes are not broken
- consumers are expected to ignore unknown event types

Update docs to state this explicitly. If the team wants a stricter closed-set event contract, bump `contractVersion` instead. Recommended v1 decision: keep `contractVersion = 1`.

## Configuration Scope

Extend `MuxSettings` with:

- `autoCompactEnabled` (`bool`, default `true`)
- `contextWarningThresholdPercent` (`int`, default `80`, clamp `50-95`)
- `compactionStrategy` (`string`, `summary` or `trim`, default `summary`)
- `compactionPreserveTurns` (`int`, default `3`, clamp `1-10`)

Keep existing:

- `contextWindowSafetyMarginPercent`
- `tokenEstimationRatio`

Recommended `settings.json` example:

```json
{
  "defaultApprovalPolicy": "ask",
  "contextWindowSafetyMarginPercent": 15,
  "tokenEstimationRatio": 3.5,
  "autoCompactEnabled": true,
  "contextWarningThresholdPercent": 80,
  "compactionStrategy": "summary",
  "compactionPreserveTurns": 3,
  "maxAgentIterations": 25
}
```

## Summary Prompt

If summary-based compaction is enabled, use a dedicated summarization prompt with tools disabled.

The summary should preserve:

- the user's goal
- hard constraints and preferences
- files/components already inspected or changed
- accepted decisions
- unresolved work or follow-ups

The summary should avoid:

- copied tool output
- verbose transcripts
- repeated status text
- irrelevant small talk

Recommended summary length target:

- `<= 512` output tokens by default

Recommended fallback:

- if the candidate block is too large for a single summary call, summarize in chunks and then summarize the chunk summaries once

Chunked summarization is a stretch item if the team wants to keep v1 smaller. Trim-only fallback is not optional.

## Likely Touch Points

Core runtime:

- `src/Mux.Core/Agent/ContextWindowManager.cs`
- `src/Mux.Core/Agent/AgentLoop.cs`
- `src/Mux.Core/Agent/AgentLoopOptions.cs`
- `src/Mux.Core/Agent/AgentEvent.cs`
- `src/Mux.Core/Enums/AgentEventTypeEnum.cs`
- `src/Mux.Core/Models/ConversationMessage.cs`
- `src/Mux.Core/Models/MuxSettings.cs`
- `src/Mux.Core/Settings/SettingsLoader.cs`
- `src/Mux.Core/Llm/LlmClient.cs`

Interactive CLI:

- `src/Mux.Cli/Commands/InteractiveCommand.cs`
- `src/Mux.Cli/Rendering/EventRenderer.cs`

Non-interactive / structured output:

- `src/Mux.Cli/Commands/PrintCommand.cs`
- `src/Mux.Cli/Commands/StructuredOutputFormatter.cs`

Docs:

- `README.md`
- `USAGE.md`
- `CONFIG.md`
- `CHANGELOG.md`

Tests:

- `test/Test.Xunit/Agent/`
- `test/Test.Xunit/Commands/`
- `test/Test.Xunit/Settings/`
- `test/Test.Automated/Suites/`

## Testing Plan

### Unit Tests

- [ ] Add `ContextWindowManager` tests for:
  - [ ] usable input limit calculation
  - [ ] warning threshold calculation
  - [ ] output token reservation
  - [ ] tool definition estimation
  - [ ] preserved-turn behavior inputs
- [ ] Add `ConversationCompactor` tests for:
  - [ ] summary replacement
  - [ ] trim-only fallback
  - [ ] preserving last N turns
  - [ ] replacing an older synthetic summary rather than stacking summaries
  - [ ] explicit failure when compaction cannot free enough room
- [ ] Add `StructuredOutputFormatter` tests for:
  - [ ] `context_status`
  - [ ] `context_compacted`
  - [ ] new `run_started` context fields
  - [ ] new `run_completed` context fields
- [ ] Add `MuxSettings` / `SettingsLoader` tests for new settings fields and clamps

### Automated / Contract Tests

- [ ] Extend `CliContractTests` so JSONL output still starts with `run_started` and ends with `run_completed`
- [ ] Add a contract test where a low-context endpoint triggers `context_status` and `context_compacted`
- [ ] Add a print-mode text test that confirms compaction notices stay on `stderr`
- [ ] Add an agent-loop test with tool-result growth that forces in-run compaction

### Interactive Command Tests

Current `InteractiveCommand` is console-heavy and not easy to test directly.

Recommended approach:

- [ ] Extract context command handling into a small helper/service that can be unit tested
- [ ] Add tests for `/context`, `/compact`, and `/compact trim`
- [ ] Verify `/endpoint` reset also resets compaction state

### Documentation Updates

- [ ] Update `README.md` interactive commands list with `/context` and `/compact`
- [ ] Update `USAGE.md` to describe compaction behavior and new JSONL events
- [ ] Update `CONFIG.md` with new settings and defaults
- [ ] Add a `CHANGELOG.md` entry when implementation lands

## Phased Delivery

### Phase 1: Safe Visibility

- [ ] Wire real context accounting into the interactive command path
- [ ] Add `/context`
- [ ] Show post-turn footer
- [ ] Emit warning when approaching the limit

Exit criteria:

- [ ] User can inspect estimated remaining context
- [ ] Warning appears before the backend hard-fails on context length

### Phase 2: Manual and Automatic Compaction

- [ ] Add `ConversationCompactor`
- [ ] Add `/compact`
- [ ] Add auto-compaction before model calls
- [ ] Add trim fallback

Exit criteria:

- [ ] User can compact without `/clear`
- [ ] Old context is condensed before hard limit errors in common cases

### Phase 3: Structured and Doc Surface

- [ ] Add new event types
- [ ] Extend structured JSONL contract
- [ ] Update renderer / print stderr behavior
- [ ] Update docs

Exit criteria:

- [ ] Interactive and automation surfaces both expose compaction activity
- [ ] Docs match shipped behavior

## Open Decisions

These should be resolved before implementation starts:

- [ ] Should summary compaction be enabled by default, or should v1 ship trim-only first?
- [ ] Should additive event types keep `contractVersion = 1`, or should the team bump to `2` for explicitness?
- [ ] Should synthetic summaries use `system` role, or should `ConversationMessage` gain metadata while keeping a safer role choice?
- [ ] Is chunked summarization in scope for the first pass, or is fallback-to-trim sufficient?

Recommended answers:

- default to summary with trim fallback
- keep `contractVersion = 1`
- add metadata to `ConversationMessage` so synthetic summary handling is explicit
- treat chunked summarization as stretch, not required for the first implementation

## Developer Checklist

Use this section as the execution tracker.

- [ ] Finalize open decisions
- [ ] Expand context accounting so it includes output reserve and tool schema cost
- [ ] Add settings and config documentation for warning/compaction policy
- [ ] Add runtime compactor service and result models
- [ ] Add `/context`
- [ ] Add `/compact`
- [ ] Emit interactive warning/footer lines
- [ ] Compact preflight session history before new runs
- [ ] Compact active in-run conversation before subsequent model calls
- [ ] Add structured events and formatter support
- [ ] Add print-mode stderr notices
- [ ] Add unit tests
- [ ] Add automated contract tests
- [ ] Update README / USAGE / CONFIG / CHANGELOG

## Definition of Done

- [ ] Interactive users can see estimated remaining context without guessing
- [ ] mux warns before the usable limit is exhausted
- [ ] mux emits a clear message when compaction occurs
- [ ] `/compact` preserves useful context better than `/clear`
- [ ] in-run tool chatter cannot silently exhaust context without mux attempting compaction first
- [ ] `print --output-format jsonl` exposes compaction activity in a machine-readable way
- [ ] docs and tests cover the shipped behavior
