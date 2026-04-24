# mux Queued Interactive Messages Plan

## Purpose

Scope a mux interactive-mode feature that lets the user draft and queue follow-up messages while the current LLM run is still thinking or responding.

Required user-facing behavior:

- while mux is busy, the user can keep typing in a draft area
- pressing `Tab` queues the current draft to run after the current run completes
- pressing `Alt+Up` edits the most recently queued message

This document is an implementation plan, not a statement that the feature already exists.

## Current State

Observed in the current codebase:

- `src/Mux.Cli/Commands/InteractiveCommand.cs` is strictly serial:
  - read prompt
  - run agent
  - render output
  - return to prompt
- `ReadMultiLineInput()` blocks on `Console.ReadKey(...)`.
- `EventRenderer.RenderAsync(...)` writes directly to the console during generation.
- `ThinkingAnimation` writes from a background task directly to the console.
- `ToolCallRenderer.PromptApprovalAsync(...)` uses `Console.ReadLine()` and takes over the console when approval is required.
- `Escape` is currently ignored by the line editor and there is no keyboard path that can cancel an active generation except `Ctrl+C`.
- There is no console abstraction or interactive session controller today.
- mux prints endpoint and model once at session start, but there is no persistent bottom-of-screen status surface showing the active model during the session.

Implication:

- this feature is not a small keybinding change
- mux needs an interactive session loop that can own input, output, and queue state concurrently

## Goals

- Preserve today's simple REPL flow when the queue is unused.
- Let users draft future prompts while the current run is active.
- Run queued prompts sequentially in order after the current run completes.
- Let the user edit the newest queued prompt without losing their current draft.
- Keep queueing compatible with streaming output, thinking indicators, and tool approval prompts.
- Let the user press `Escape` during active generation to terminate the current completion without exiting mux.
- Show the active endpoint and model in the bottom status area so the user can always see what backend they are interacting with.
- Make the implementation testable instead of burying more logic in `Console.*` calls.

## Non-Goals

- Running multiple agent loops in parallel.
- Persisting queued prompts across mux process restarts.
- Adding queueing to `mux print` or `mux probe`.
- Building a full-screen TUI framework.
- Editing arbitrary older queued items in v1. The required v1 edit target is the last queued item.

## User Experience

### Core Behavior

Idle behavior remains familiar:

- `Enter` submits immediately
- `Shift+Enter` and `Ctrl+Enter` insert newline
- `Up` and `Down` browse executed prompt history

When mux is busy:

- the user still sees an editable draft prompt at the bottom
- streamed output continues above it
- `Tab` queues the current draft
- `Alt+Up` edits the last queued message

Recommended busy-state footer:

```text
[busy] model=qwen2.5-coder:32b | endpoint=ollama-qwen32 | 2 queued | Tab=queue | Alt+Up=edit last queued | Esc=cancel current run
```

Recommended queue confirmation:

```text
[queued] #2 "add tests for the endpoint switch flow"
```

Recommended idle footer:

```text
[ready] model=qwen2.5-coder:32b | endpoint=ollama-qwen32 | Enter=send | Shift+Enter=newline | Up/Down=history
```

Recommended approval footer:

```text
[approval] model=qwen2.5-coder:32b | endpoint=ollama-qwen32 | Tool call awaiting Y / n / always. Draft input is paused.
```

Recommended paused footer after cancellation:

```text
[queue paused] model=qwen2.5-coder:32b | endpoint=ollama-qwen32 | Current run cancelled | 2 queued | /queue resume
```

### Busy-State Input Semantics

Recommended v1 rules:

- when idle:
  - `Enter` submits immediately
- when busy:
  - `Enter` inserts a newline into the draft instead of attempting immediate submission
  - `Tab` queues the draft

Reason:

- the current REPL already supports explicit multi-line editing
- `Tab` is effectively unused today because control characters are ignored in the draft buffer
- keeping `Enter` as immediate-send while busy would conflict with the required `Tab` queueing model

### `Alt+Up` Edit Semantics

Recommended v1 behavior:

1. If there is no queued message, show a short notice and do nothing.
2. If the active draft is empty, pop the newest queued message into the editor.
3. If the active draft is non-empty, swap the active draft with the newest queued message.

Why swap instead of overwrite:

- the user does not lose the draft they were already typing
- the queue size remains stable unless the editor was empty

### Queue Ordering

Queue ordering should be FIFO:

- the oldest queued message runs next
- `Alt+Up` edits the newest queued message only

This gives simple "run after this" semantics while still letting the user fix the latest queued prompt before it executes.

### Conversation Semantics

Queued messages should execute against session state at dequeue time, not enqueue time.

That means:

- the next queued prompt sees the conversation history updated by the prompt before it
- queued prompts are not bound to a snapshot of prior history

This is the simplest and most intuitive definition of "run after."

### Prompt History Semantics

Recommended rule:

- `PromptHistory` should continue to track prompts that actually execute
- queued-but-not-yet-executed prompts should stay out of prompt history

Reason:

- avoids duplicate or stale history entries
- lets `Alt+Up` keep editing queue items without polluting normal history navigation

## Approval, Cancellation, and Error Behavior

### Tool Approval

Current interactive approval uses `Console.ReadLine()`, which is incompatible with concurrent queued drafting.

Recommended v1 behavior:

- while a tool approval request is active, queued drafting is temporarily suspended
- the queue remains visible but read-only
- the session shows a clear approval-state notice

Example:

```text
[approval] Tool call awaiting Y / n / always. Draft input is paused.
```

This is a reasonable first version. Trying to keep queue drafting active during approval would add a second competing input mode and much more console complexity.

### Cancellation

Recommended `Escape` behavior:

- if a run is active, cancel only the current run
- preserve the queue
- pause automatic queue dispatch after the cancellation

Recommended paused notice:

```text
[queue paused] Current run cancelled. 3 queued messages remain.
```

Why pause instead of continuing immediately:

- a user cancellation usually means "stop and let me intervene"
- immediately consuming the next queued prompt would feel wrong

Recommended `Ctrl+C` behavior:

- keep `Ctrl+C` as the existing stronger interrupt path
- `Escape` should be the fast in-session cancel for active generation
- `Ctrl+C` can continue to cancel the active run and preserve current fallback behavior for exiting the REPL

Scope note:

- this plan requires `Escape` cancellation in the `Running` state
- approval prompts can remain explicit `Y / n / always` in v1 unless the team separately wants `Escape` to map to denial during `AwaitingApproval`

### Errors

Recommended v1 behavior:

- if a run finishes normally, auto-dispatch the next queued message
- if a run is cancelled or terminates with a top-level execution failure, pause the queue

This is safer than blindly plowing through future queued prompts after a bad run.

## Recommended Supporting Commands

The requested feature is key-driven, but keybindings alone are not enough for a usable queue.

Recommended interactive commands:

- `/queue`
  - list pending queued messages and whether dispatch is paused
- `/queue clear`
  - clear all queued messages
- `/queue drop-last`
  - remove the newest queued message
- `/queue resume`
  - resume automatic dispatch after cancellation or failure pause

Recommended v1 rule:

- slash commands are not queueable
- if the current draft starts with `/` and the user presses `Tab`, mux should refuse to queue it and show a short message

This avoids dangerous or confusing cases like queueing `/clear`, `/endpoint`, or `/system`.

## Architecture

### High-Level Design

mux needs a session controller that owns:

- interactive state
- queue state
- console rendering
- agent run lifecycle
- approval prompts

Recommended architecture:

1. A main interactive session loop owns all console writes.
2. Agent runs execute in background tasks and emit events into a channel.
3. The session loop polls:
   - keyboard input
   - agent events
   - animation ticks
4. The session loop mutates state and redraws the interactive layout as needed.

This is a better fit than the current model of "block on input, then block on render."

### New / Expanded Components

#### `InteractiveSessionController`

Add a session-level orchestrator, likely under `src/Mux.Cli/Commands/` or `src/Mux.Cli/Rendering/`.

Responsibilities:

- track session mode: idle, running, awaiting approval, paused, exiting
- own the active draft buffer
- own queued messages
- launch and monitor one `AgentLoop` at a time
- auto-dispatch queued messages when allowed
- broker approval requests
- handle `Escape`-driven cancellation of the active run
- decide when to redraw the screen

#### `QueuedMessageEntry`

Add a queue model with fields such as:

- `Id`
- `Text`
- `EnqueuedAtUtc`
- `SequenceNumber`

Optional but useful:

- `Source`: `ManualDraft`, future extensibility only

#### `QueuedMessageManager`

Add a small queue service responsible for:

- enqueue
- dequeue next
- peek next
- pop last
- swap last with current draft
- clear
- expose count and snapshots for rendering and tests

This logic should not live inline inside `InteractiveCommand`.

#### `InteractiveLayoutRenderer`

Replace the current "prompt first, then output stream" model with a renderer that understands:

- output region
- status or footer region
- draft prompt region

The footer region should always include:

- active model
- active endpoint
- session mode
- queue count when non-zero
- context-specific key hints for the current mode

Recommended behavior:

- before writing new output, temporarily clear the prompt and footer region
- write new output lines
- redraw status and footer
- redraw the active draft prompt and restore cursor position

This keeps streaming output and bottom-of-screen input compatible without needing a full-screen TUI dependency.

#### `ApprovalRequestBroker`

Replace direct `Console.ReadLine()` approval prompting with a broker that:

- receives approval requests from the agent callback
- exposes a `Task<string>` backed by `TaskCompletionSource<string>`
- lets the session loop fulfill the request from key input

This keeps `AgentLoopOptions.PromptUserFunc` intact while removing direct console ownership from `ToolCallRenderer`.

#### Console Abstraction

Add an abstraction for interactive console IO.

Recommended minimal surface:

- `ReadKey`
- `KeyAvailable`
- `Write`
- `WriteLine`
- `SetCursorPosition`
- `CursorTop`
- `BufferWidth`
- optional key polling or event loop support sufficient to notice `Escape` while a run is active

Why this is needed:

- current interactive code is hard to unit test
- queueing, redraw, and approval behavior will otherwise require brittle real-console automation

## Event Flow

Recommended flow:

1. Session starts in `Idle`.
2. User submits a prompt.
3. Session transitions to `Running`.
4. Agent events stream into the session loop.
5. While `Running`, the user may keep editing the active draft.
6. Pressing `Tab` enqueues the draft and clears the editor.
7. When the run completes:
   - update conversation history
   - if queue is not paused and has entries, dequeue next and launch it
   - otherwise transition back to `Idle`

Approval flow:

1. Agent requests approval via `PromptUserFunc`.
2. Session transitions to `AwaitingApproval`.
3. Draft input is paused.
4. User responds with approval keys.
5. Session resolves the pending approval task.
6. Session returns to `Running`.

## Key Handling Plan

### Required Keybindings

- `Enter`
  - idle: submit now
  - busy: insert newline
- `Shift+Enter`
  - insert newline
- `Ctrl+Enter`
  - insert newline
- `Tab`
  - busy: enqueue current draft
- `Alt+Up`
  - busy or paused: edit newest queued message
- `Escape`
  - running: cancel the current completion and pause queue dispatch
- `Ctrl+C`
  - if running: cancel current run and pause queue
  - if idle with empty draft: second press exits as today

### Terminal Compatibility

`Alt+Up` must be validated on real terminals because some environments handle Alt sequences differently.

Recommended fallback:

- support `/queue drop-last` and `/queue resume` regardless
- add `/queue edit-last` if `Alt+Up` proves unreliable in some terminals

Recommended validation targets:

- Windows Terminal plus PowerShell
- standard Windows console
- Linux and macOS terminals if supported by the project team

## Renderer and Animation Changes

### `ThinkingAnimation`

Current `ThinkingAnimation` is a background writer that uses direct console writes.

That is incompatible with a bottom-docked live input editor.

Recommended refactor:

- convert it from "background console writer" into "frame generator" or "state provider"
- let the session renderer draw the current animation frame as part of the normal redraw cycle

This avoids multiple writers fighting over cursor position.

### `EventRenderer`

Current `EventRenderer.RenderAsync(...)` assumes it owns console output for the duration of a run.

Recommended split:

- keep `EventRenderer` for non-interactive-like rendering or print-oriented paths if useful
- create a separate interactive renderer or session presenter for the queued-input REPL

Do not try to bolt queue-aware drafting onto the current `RenderAsync(...)` contract.

## Approval UI Changes

### `ToolCallRenderer`

Current `ToolCallRenderer.PromptApprovalAsync(...)` calls `Console.ReadLine()`.

Recommended change:

- keep formatting helpers
- remove direct blocking reads from the renderer
- let the session controller render approval prompts and collect the response

This is required for queue-aware interactive mode.

## Likely Touch Points

Interactive command and control flow:

- `src/Mux.Cli/Commands/InteractiveCommand.cs`

Rendering and input:

- `src/Mux.Cli/Rendering/LineBuffer.cs`
- `src/Mux.Cli/Rendering/PromptHistory.cs`
- `src/Mux.Cli/Rendering/EventRenderer.cs`
- `src/Mux.Cli/Rendering/ThinkingAnimation.cs`
- `src/Mux.Cli/Rendering/ToolCallRenderer.cs`

Core loop integration:

- `src/Mux.Core/Agent/AgentLoop.cs`
- `src/Mux.Core/Agent/AgentLoopOptions.cs`

Help and docs:

- `src/Mux.Cli/Program.cs`
- `README.md`
- `USAGE.md`
- `CHANGELOG.md`

Tests:

- `test/Test.Xunit/Rendering/`
- `test/Test.Xunit/Commands/`
- `test/Test.Xunit/Agent/`
- `test/Test.Automated/Suites/`

## Recommended State Model

Suggested states:

- `Idle`
- `Running`
- `AwaitingApproval`
- `QueuePaused`
- `Exiting`

Suggested session data:

- active draft `LineBuffer`
- queued messages list
- current run task and cancellation source
- pending approval request
- conversation history
- prompt history
- current endpoint, system prompt, and MCP state
- active footer snapshot including endpoint and model display text

## Testing Plan

### Unit Tests

- [ ] Add `QueuedMessageManager` tests for:
  - [ ] enqueue and dequeue FIFO ordering
  - [ ] pop last
  - [ ] swap current draft with last queued entry
  - [ ] clear
  - [ ] empty-queue no-op behavior
- [ ] Add key-handling tests for:
  - [ ] `Tab` queues only while busy
  - [ ] `Enter` submits when idle
  - [ ] `Enter` inserts newline when busy
  - [ ] `Alt+Up` edits last queued message
  - [ ] `Escape` cancels the active run only while running
  - [ ] slash-command draft cannot be queued
- [ ] Add approval broker tests for:
  - [ ] approval request pauses draft input
  - [ ] `y`, `n`, and `always` responses complete the pending request
- [ ] Add renderer or layout tests for:
  - [ ] output write followed by prompt redraw
  - [ ] queue footer and status text
  - [ ] footer shows active endpoint and model in idle, busy, and approval states
  - [ ] animation frame drawing without background console writes

### Command / Session Tests

- [ ] Extract queue or session logic from `InteractiveCommand` into testable helpers instead of testing through raw `Console.*`
- [ ] Add session-state tests for:
  - [ ] run completion auto-dispatches next queued message
  - [ ] cancellation pauses queue
  - [ ] failure pauses queue
  - [ ] executed queued messages are added to prompt history only after execution

### Automated / Integration Tests

Current interactive mode is not well suited to automated validation.

Recommended prerequisite:

- [ ] add a fake console driver and scripted key-input harness

Then add automated scenarios such as:

- [ ] queue one prompt while a first run is streaming, verify second run starts automatically after first completes
- [ ] queue two prompts, verify FIFO execution order
- [ ] use `Alt+Up` to edit the last queued message before it runs
- [ ] press `Escape` during streaming and verify the active completion terminates while queued messages remain intact and paused
- [ ] trigger tool approval and verify queue drafting pauses until approval is answered
- [ ] cancel current run and verify queue remains intact but paused

### Documentation Updates

- [ ] Update the interactive banner in `InteractiveCommand` to mention `Tab` queueing and `Alt+Up`
- [ ] Update the interactive banner in `InteractiveCommand` to mention `Escape` cancellation during active generation
- [ ] Update `Program.PrintHelp()` interactive help text
- [ ] Update `README.md` interactive section
- [ ] Update `USAGE.md` with queue examples and semantics
- [ ] Document the persistent footer and that it shows the active endpoint and model
- [ ] Add a `CHANGELOG.md` entry when implementation lands

## Phased Delivery

### Phase 1: Testability and Session Skeleton

- [ ] Introduce a console abstraction
- [ ] Extract interactive session state and key handling out of `InteractiveCommand`
- [ ] Preserve existing behavior with no queue enabled yet

Exit criteria:

- [ ] interactive mode still works
- [ ] session logic is unit testable without a real console

### Phase 2: Queue Core

- [ ] Add queued message models and manager
- [ ] Add live draft area during active runs
- [ ] Add `Tab` queueing while busy
- [ ] Add automatic FIFO dispatch after run completion

Exit criteria:

- [ ] user can queue prompts while a run is active
- [ ] queued prompts run sequentially afterward

### Phase 3: Edit and Pause Controls

- [ ] Add `Alt+Up` edit-last behavior
- [ ] Add queue paused state after cancellation or failure
- [ ] Add `/queue` support commands

Exit criteria:

- [ ] user can recover from mistakes in the latest queued message
- [ ] cancellation does not destroy the queue

### Phase 4: Approval / Renderer Integration

- [ ] Replace console-blocking approval reads with approval broker flow
- [ ] Refactor thinking animation to renderer-owned drawing
- [ ] Ensure streaming output and draft input coexist cleanly

Exit criteria:

- [ ] approval prompts no longer break queued drafting architecture
- [ ] no concurrent console writers are fighting over the cursor

## Open Decisions

- [ ] Should `Enter` while busy insert newline or also queue?
- [ ] Should mux immediately auto-dispatch the next queued message after completion, or allow a short grace period?
- [ ] Should slash commands ever be queueable?
- [ ] Should queue state survive `/clear`, `/endpoint`, or `/system` changes if those commands are added while paused?
- [ ] Is `Alt+Up` reliable enough across supported terminals, or is `/queue edit-last` required in v1?

Recommended answers:

- `Enter` while busy inserts newline and `Tab` queues
- auto-dispatch immediately after normal completion
- slash commands are not queueable in v1
- state-changing slash commands should require the queue to be empty or explicitly cleared first
- ship a command fallback if terminal testing shows `Alt+Up` is inconsistent

## Developer Checklist

- [ ] Finalize open decisions
- [ ] Introduce console abstraction for interactive mode
- [ ] Extract session controller or state machine from `InteractiveCommand`
- [ ] Add queued message models and manager
- [ ] Add busy-state draft input area
- [ ] Add persistent footer showing active endpoint and model
- [ ] Add `Tab` queueing
- [ ] Add `Alt+Up` edit-last behavior
- [ ] Add `Escape` cancellation for active generation
- [ ] Add queue pause and resume semantics
- [ ] Refactor approval prompting away from `Console.ReadLine()`
- [ ] Refactor thinking animation away from background console writes
- [ ] Add queue support commands
- [ ] Add unit tests
- [ ] Add automated interactive tests with fake console or scripted input
- [ ] Update help and docs

## Definition of Done

- [ ] While mux is thinking or responding, the user can still type into a visible draft area
- [ ] The bottom status footer always shows the active endpoint and model while in interactive mode
- [ ] Pressing `Escape` during active generation terminates the current completion without exiting mux
- [ ] Pressing `Tab` queues the current draft without interrupting the active run
- [ ] Queued messages run sequentially after the active run completes
- [ ] Pressing `Alt+Up` lets the user edit the newest queued message without losing their current draft
- [ ] Tool approval prompts no longer rely on direct `Console.ReadLine()` in interactive mode
- [ ] There is a single clear owner of interactive console rendering, rather than multiple competing console writers
- [ ] The queue feature is covered by unit tests and interactive integration tests
- [ ] README, USAGE, and help text explain the shipped behavior
