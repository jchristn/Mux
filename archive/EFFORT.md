# Mux — Reasoning Effort Support (`EFFORT.md`)

**Feature:** Expose a configurable reasoning-effort control across Mux — interactive TUI and headless — built on PolyPrompt v2.1.0's `ReasoningEffort`. A selected level drives provider-specific defaults automatically; headless callers can override the level and the per-provider tuning from the command line.
**Target release:** Mux `v0.8.0` — additive, minor bump (no breaking changes to config, CLI, or the JSONL contract).
**Depends on:** PolyPrompt `2.1.0` (published).
**Status:** ✅ **Complete — shipped in mux `v0.8.0`** (commit `49250c4`).
**Owner:** Implemented by Claude for Joel.
**Drafted:** 2026-08-12 · **Shipped:** 2026-08-12

> **Archived.** This plan is complete and preserved for reference. Every section below was implemented as
> written (with the one deviation noted in the progress log: the CLI override is applied in
> `CommandRuntimeResolver` after `ResolveEndpoint` rather than by extending its signature). Tests:
> 506/506 pass on net8.0 and net10.0 across xUnit and NUnit; Test.Automated 505 passed, 0 failed.

---

## How to use this document

Every task is a checkbox. Annotate as you go: `- [x]` done · `- [~]` in progress · `- [ ]` not started · `- [!]` blocked (add a note after an em dash). Keep the **Progress log** at the end current — it is the one place a reviewer looks for status. Do not delete finished tasks; check them off so the history stays auditable.

File paths are relative to `C:\Code\Mux`. Line references reflect the tree at drafting time; confirm before editing.

---

## 1. Why this belongs in Mux

Mux already lets a user tune temperature and max tokens per endpoint and per run. Reasoning effort is the next knob users of coding agents reach for: on a reasoning-capable model it trades latency and cost against how hard the model thinks, without swapping models. Claude Code exposes it as `/effort`; Codex pairs it with model selection. Mux should offer the same control, and because Mux is backend-agnostic it is the right layer to translate one human choice ("high") into whatever each backend wants.

PolyPrompt v2.1.0 does the provider translation. A `ReasoningEffort` value object carries a semantic `ReasoningEffortLevel` and projects to OpenAI `reasoning_effort`, Gemini `thinkingConfig.thinkingBudget`, and Ollama `think`; when it is null, nothing is sent and the request body is unchanged. Mux's job is narrower and entirely additive: persist a chosen level per endpoint, let the TUI change it, let headless runs override the level and the per-provider tuning, and hand the result to PolyPrompt at the single point where Mux builds a request.

The design keeps one rule central. A level is the everyday choice, and selecting a level is enough — the provider default follows from it. Overriding the exact Gemini budget or the Ollama `think` shape is a power-user affordance that never gets in the way of the common case.

---

## 2. Design

### 2.1 What a user selects, and what gets sent

A level is one of `Minimal`, `Low`, `Medium`, `High`, plus the absence of a level, which means "send no reasoning field." Selecting `High` sends `reasoning_effort: "high"` to an OpenAI-compatible backend, a dynamic thinking budget to Gemini, and `think: "high"` to Ollama — all courtesy of PolyPrompt's level defaults. A headless caller who needs a specific Gemini budget can override just that number while keeping the level's meaning everywhere else.

| Mux selection | OpenAI `reasoning_effort` | Gemini `thinkingBudget` | Ollama `think` |
|---|---|---|---|
| Off (unset) | *(omitted)* | *(omitted)* | *(omitted)* |
| Minimal | `minimal` | `0` | `false` |
| Low | `low` | `1024` | `low` |
| Medium | `medium` | `8192` | `medium` |
| High | `high` | `-1` (dynamic) | `high` |

The right column values are PolyPrompt defaults; Mux never hardcodes them. Mux stores only the level and any explicit per-provider overrides, and lets PolyPrompt fill in the rest at request time.

### 2.2 Configuration model (Mux.Core)

Reasoning effort is endpoint-scoped, stored in `endpoints.json` next to `temperature` and `maxTokens`. Two new types, one per file, per the code-style rule:

- **`Mux.Core.Enums.ReasoningLevelEnum`** — `Minimal`, `Low`, `Medium`, `High`. A JSON converter built on the existing `FlexibleEnumConverter` accepts case-insensitive strings so hand-edited config is forgiving. Absence of a level is modeled as a null `ReasoningEffortConfig` or a null `Level`, not an enum member, so "off" needs no sentinel.
- **`Mux.Core.Models.ReasoningEffortConfig`** — a small persisted object: a nullable `Level` plus three nullable overrides that mirror PolyPrompt's tunables (`OpenAiValue`, `GeminiThinkingBudget`, `OllamaThink`). Each override validates in its setter using a backing field, the same way `EndpointConfig` clamps temperature. `GeminiThinkingBudget` clamps to `-1..32768`; the string overrides normalize case and reject out-of-set values by reverting to null.

`EndpointConfig` gains one nullable property, `ReasoningEffort`, serialized as `reasoningEffort`. When null, the endpoint sends no reasoning field, which is the current behavior for every existing config on disk — so upgrading changes nothing until a user opts in.

```json
{
  "name": "openai-gpt5",
  "adapterType": "OpenAi",
  "model": "gpt-5",
  "reasoningEffort": { "level": "high" }
}
```

```json
{
  "name": "gemini-pro",
  "adapterType": "OpenAiCompatible",
  "model": "gemini-2.5-pro",
  "reasoningEffort": { "level": "medium", "geminiThinkingBudget": 16000 }
}
```

Keeping these types free of any PolyPrompt reference matters: Mux.Core's models are pure configuration, and PolyPrompt stays an implementation detail behind `LlmClient`. The translation lives at the boundary (§2.3), not in the config model.

### 2.3 The single wire integration point (Mux.Core/Llm/LlmClient.cs)

`LlmClient.BuildRequest` (around line 481) is the one place Mux constructs a `PolyPrompt.Models.ToolChatRequest`. It already sets `Model` and `MaxTokens`. It gains one mapping call:

```csharp
Pp.ReasoningEffort? reasoning = MapReasoningEffort(_Endpoint.ReasoningEffort);
if (reasoning != null)
{
    request.ReasoningEffort = reasoning;
}
```

A private `MapReasoningEffort(ReasoningEffortConfig?)` returns null when the config or its level is null, and otherwise builds a `Pp.ReasoningEffort` from the level and applies whichever overrides are set. The mapping lives here, not on the config model, so the PolyPrompt dependency stays contained. `LoadModelAsync`'s minimal probe request is left untouched — a warmup call should not carry effort.

`Mux.Core/Mux.Core.csproj` bumps its PolyPrompt `PackageReference` from `2.0.1` to `2.1.0`.

### 2.4 Headless overrides (Mux.Cli)

Headless is where the per-provider tuning earns its place. Four options, all optional, all documented:

- `--effort <off|minimal|low|medium|high>` — sets or clears the level for the run. `off`/`none` force the field off even when the endpoint config sets a level; a level value replaces it. Omitting the flag inherits the endpoint.
- `--effort-openai-value <string>` — overrides the OpenAI `reasoning_effort` string.
- `--effort-gemini-budget <int>` — overrides the Gemini thinking budget.
- `--effort-ollama-think <low|medium|high|true|false>` — overrides the Ollama `think` value.

The three provider overrides apply only when a level is active (from the endpoint or `--effort`); with no active level, nothing is sent and the overrides are inert. `--effort off` combined with a provider override is a contradiction resolved in favor of off, with a one-line notice on stderr in verbose mode.

The flags flow the same way `--temperature` and `--max-tokens` do today: parsed into `CommonSettings`, threaded through `SettingsLoader.ResolveEndpoint`, and merged onto the resolved `EndpointConfig`. `ResolveEndpoint` already takes discrete `cliTemperature`/`cliMaxTokens` parameters; rather than add four more, it takes one nullable `ReasoningEffortConfig cliReasoningOverride` and merges field by field (a provided level replaces; each provided override replaces its own field). No tuples.

An invalid `--effort` value fails fast with a clear message and a non-zero exit, consistent with how the other typed flags behave.

### 2.5 Observability (JSONL contract)

`run_started` already reports loop and context metadata. It gains a `reasoningEffort` object describing the effective selection for the run — the level and any overrides, or null when off — so an orchestrator can see exactly what Mux sent. When the effective value came from a CLI override, `cliOverridesApplied` includes `reasoningEffort`, matching how `temperature` and `maxTokens` already appear there. The change is purely additive; consumers that ignore the field are unaffected.

---

## 3. Implementation — Mux.Core

### 3.1 New enum and converter
- [x] Create `src/Mux.Core/Enums/ReasoningLevelEnum.cs` — `Minimal`, `Low`, `Medium`, `High`, with XML docs on the enum and each member.
- [x] Create `src/Mux.Core/Enums/ReasoningLevelEnumConverter.cs` following `AdapterTypeEnumConverter`/`FlexibleEnumConverter` — case-insensitive parse, and a `TryParse(string, out ReasoningLevelEnum)` for CLI use.

### 3.2 New config model
- [x] Create `src/Mux.Core/Models/ReasoningEffortConfig.cs`:
  - Nullable `Level` (`ReasoningLevelEnum?`), JSON `level`.
  - `OpenAiValue` (`string?`, JSON `openAiValue`) — setter normalizes to `minimal/low/medium/high`, else null.
  - `GeminiThinkingBudget` (`int?`, JSON `geminiThinkingBudget`) — setter clamps to `-1..32768`.
  - `OllamaThink` (`string?`, JSON `ollamaThink`) — setter normalizes to `low/medium/high/true/false`, else null.
  - Backing fields with `_PascalCase`; XML docs stating defaults, ranges, and what each value means.
  - A `Merge(ReasoningEffortConfig? over)` helper that returns a new config with `over`'s non-null fields taking precedence (used by CLI override merging). No tuples.

### 3.3 EndpointConfig
- [x] Add `ReasoningEffort` (`ReasoningEffortConfig?`, JSON `reasoningEffort`, default null) to `src/Mux.Core/Models/EndpointConfig.cs`, mirroring the existing nullable-property style. Ensure the endpoint clone path (SettingsLoader ~line 1009 copies `MaxTokens`/`Temperature`) also copies `ReasoningEffort` (deep copy).

### 3.4 LlmClient wire mapping
- [x] In `src/Mux.Core/Llm/LlmClient.cs` `BuildRequest`, set `request.ReasoningEffort` from a new private `MapReasoningEffort(ReasoningEffortConfig?)` that returns `Pp.ReasoningEffort?` (null when config/level null; otherwise `new Pp.ReasoningEffort(mappedLevel)` with overrides applied). Level names map one-to-one to `Pp.ReasoningEffortLevel`.
- [x] Leave `LoadModelAsync` unchanged (no effort on the warmup probe).
- [x] Bump PolyPrompt to `2.1.0` in `src/Mux.Core/Mux.Core.csproj`.

### 3.5 Settings resolution
- [x] Extend `SettingsLoader.ResolveEndpoint` (`src/Mux.Core/Settings/SettingsLoader.cs` ~571) with a trailing `ReasoningEffortConfig? cliReasoningOverride` parameter; after the `cliMaxTokens` block, merge it onto `selected.ReasoningEffort` (a provided level replaces; `off`/`none` clears to null; each provided override replaces its field). Update the XML `<param>` docs.
- [ ] (Optional, Phase 2 — **deferred, not shipped in v0.8.0**) Add a global `defaultReasoningEffort` to `MuxSettings` and a `GetEffectiveReasoningEffort(EndpointConfig?)` resolver mirroring `GetEffectiveMaxAgentIterations`. Endpoint-scoped wins. v0.8.0 stayed endpoint-scoped only.

---

## 4. Implementation — Headless (Mux.Cli)

### 4.1 CLI options
- [x] Add to `src/Mux.Cli/Commands/CommonSettings.cs`, with `[Description]` and `[CommandOption]` matching the house pattern:
  - `--effort` → `string? Effort`
  - `--effort-openai-value` → `string? EffortOpenAiValue`
  - `--effort-gemini-budget` → `int? EffortGeminiBudget`
  - `--effort-ollama-think` → `string? EffortOllamaThink`
- [x] Parse them in `src/Mux.Cli/Commands/CliArgumentParser.cs` `ParseCommon` (next to `--temperature`/`--max-tokens` at ~233). Parse the int with `int.Parse(..., CultureInfo.InvariantCulture)`; validate `--effort` against `off/none/minimal/low/medium/high` and throw `InvalidOperationException` with a clear message on a bad value.

### 4.2 Runtime wiring
- [x] In `src/Mux.Cli/Commands/CommandRuntimeResolver.cs` `ResolveRuntime`, build a `ReasoningEffortConfig` from the four settings (null when none supplied) and pass it to `ResolveEndpoint` (the call at ~71). Add `"reasoningEffort"` to `cliOverridesApplied` (~327) when any effort flag was supplied.
- [x] Reject contradictory input consistently (e.g. `--effort off` plus a provider override): honor `off`, and in verbose mode note the ignored override on stderr.

### 4.3 JSONL / observability
- [x] Add a `ReasoningEffort` snapshot to the `RunStartedEvent` model (`src/Mux.Core/Agent/…`) — the effective level and overrides, or null.
- [x] Emit it in `src/Mux.Cli/Commands/StructuredOutputFormatter.cs` under `run_started` (near the `maxIterations`/`cliOverridesApplied` block at ~56) as `payload["reasoningEffort"]`.
- [x] Populate the event where `run_started` is constructed for `print` (and keep `probe` metadata consistent if it reports endpoint capability).

---

## 5. Implementation — Interactive TUI (Mux.Cli/App)

The interactive surface gets a level picker, a per-endpoint form field for the level and its advanced tuning, a sidebar indicator, and automatic inclusion in the F1 menu and slash router. Section 6 shows exactly how each looks.

### 5.1 `/effort` command and picker
- [x] Register in `MuxTuiApp` command catalog next to `/theme` (~line 211):
  ```csharp
  _Catalog.Add(new CommandDescriptor("mux.effort", "Reasoning effort", null, OpenEffortSelector, "Model", new[] { "effort", "reasoning", "reasoning-effort" }));
  ```
  No key chord (the F-keys and Ctrl chords are taken; reachable via `/effort`, the F1 menu, and the picker). Category `Model` so it sits with `/endpoint`, `/model`, `/prompts`, `/mcp`, `/skills`.
- [x] Implement `OpenEffortSelector` + `ResolveEffortSelectorAsync` modeled on `OpenThemeSelector`/`ResolveThemeSelectorAsync` (~493–540), using `SelectModal` with rows `Off · Minimal · Low · Medium · High`, the active level marked, preselected to the current value.
- [x] On choose: update the active endpoint's `ReasoningEffort.Level` (Off ⇒ null), persist to `endpoints.json` (the same save path the endpoint editor uses), refresh the sidebar, and show an endpoint-switch-style notice (§6.5).

### 5.2 Endpoint form field(s)
- [x] In `src/Mux.Cli/App/EndpointFormModal.cs`, add a `Reasoning effort (blank = off)` field near `Temperature`/`Max agent iterations` (~145–149), validated against the level set (blank allowed). Seed it from `source.ReasoningEffort?.Level`.
- [x] Add the provider tuning as advanced fields, blank by default: `Gemini thinking budget (blank = default)` validated as an optional int in `-1..32768`, and optionally `OpenAI value` / `Ollama think` as optional constrained strings. Persist into the endpoint's `ReasoningEffortConfig` on save (the `BuildEndpoint` path ~268 that already reads `Temperature`/`MaxAgentIterations`). Add the field heights to the per-field row array so scrolling stays correct.

### 5.3 Sidebar indicator
- [x] In `src/Mux.Cli/App/SidebarView.cs`, add an `EFFORT <level>` line near the endpoint/model/tasks lines; render nothing (or `EFFORT off`) when unset. Keep it a single row so the sidebar layout is unchanged in height.

### 5.4 Menu / slash / help
- [x] No extra work beyond registration: the F1 command menu, the slash router, and `/help` all read the catalog, so `/effort` appears automatically under **Model**. Verify it renders and runs.

---

## 6. TUI layout and aesthetic changes (with examples)

Each change below is small and consistent with the existing modal and sidebar styling. Nothing changes the overall frame, panes, or key map except the additions listed.

### 6.1 The `/effort` picker (new modal)

A `SelectModal`, same chrome as the theme picker, centered, with the current level marked and the footer hint. Example with the endpoint currently at High:

```
        ┌ Reasoning effort — ↑↓ then Enter to apply ────────┐
        │  Off                                              │
        │  Minimal                                          │
        │  Low                                              │
        │  Medium                                           │
        │▌ High  (current)                                  │
        │                                                   │
        │  ↑↓ move · Enter apply · Esc cancel               │
        └───────────────────────────────────────────────────┘
```

### 6.2 Endpoint Add/Edit form — new fields

The guided form gains one everyday field and one advanced field. Rendered in the same label/field rhythm as the existing rows:

```
  Name                      openai-gpt5
  Adapter                   ( ) ollama  (•) openai  ( ) vllm  ( ) openai-compatible
  Base URL                  https://api.openai.com
  Model                     gpt-5
  Temperature               0.10
  Max tokens                8192
  Reasoning effort          high            ← blank = off; one of minimal/low/medium/high
  Gemini thinking budget                    ← advanced; blank = level default (-1..32768)
  Max agent iterations                      (blank = global)

  Enter save · Esc cancel · Tab next field
```

### 6.3 Sidebar indicator

The sidebar shows the active level so it is visible at a glance without opening a modal, alongside the endpoint, model, and task lines it already renders:

```
  ENDPOINT   openai-gpt5
  MODEL      gpt-5
  EFFORT     high
  TASKS      2/5
```

When effort is off the row reads `EFFORT     off` (or is omitted — pick one and keep it consistent; the mock assumes it is always shown for discoverability).

### 6.4 F1 command menu

`/effort` appears under the **Model** group automatically. No layout change beyond the new row:

```
  Model
    Endpoints / models        /endpoint      Ctrl+E
    Reasoning effort          /effort
    Prompts                   /prompts       Ctrl+P
    MCP servers               /mcp
    Skills                    /skills
```

### 6.5 Switch notice

After a level change (via the picker or a headless run), Mux prints a short notice in the same style as the endpoint-switch notice it already shows. Switch notices do not end with a period:

```
  ▸ Reasoning effort set to High
```

For an endpoint whose adapter has no reasoning concept, the level still applies; the model may ignore it. The notice stays terse and period-free.

**Modal width.** The `/effort` picker (`EffortSelectModal`) sizes its own box to hold the full title and every row, and the endpoint form's `ContentWidth` is widened (54 → 60) so the new labels and the footer hint are never truncated.

---

## 7. Tests (Touchstone)

Follow the backend test architecture: descriptors in `Test.Shared/Suites`, no console output there, loopback on `127.0.0.1`, run through Test.Automated, Test.Xunit, and Test.Nunit. Add cases to existing suites where they fit; create focused new cases rather than broadening unrelated ones.

### 7.1 Core / config
- [x] `EndpointConfigSuite` — `ReasoningEffortConfig` round-trips through `endpoints.json`; default is null; `GeminiThinkingBudget` clamps to `-1..32768`; `OpenAiValue`/`OllamaThink` normalize and reject out-of-set values; `Merge` applies non-null fields.
- [x] `EndpointConfigSuite` / clone — resolving/cloning an endpoint carries `ReasoningEffort` (deep copy, not shared reference).

### 7.2 Wire mapping (the end-to-end proof)
- [x] `LlmBridgeSuite` — using the in-process `Test.Shared/Llm/LocalLlmTestServer` (bound to `127.0.0.1`), run a turn with an endpoint at `High` and assert the recorded outbound body carries `reasoning_effort: "high"`; assert a null config sends **no** `reasoning_effort` (backward-compatibility lock). If the local server does not yet capture request bodies, extend it to record them.
- [x] `LlmBridgeSuite` — a Gemini-shaped override (`geminiThinkingBudget = 16000`) is reflected while the OpenAI projection still derives from the level.

### 7.3 Headless
- [x] `CommandRuntimeResolverSuite` / `SettingsLoaderSuite` — `--effort high` overrides the endpoint; `--effort off` disables even when the endpoint sets a level; provider overrides merge field by field; `cliOverridesApplied` includes `reasoningEffort` only when supplied.
- [x] `CliContractSuite` — invalid `--effort banana` fails with a non-zero exit and a clear message.
- [x] `PrintModeSuite` / `HeadlessFeaturesSuite` — `run_started` JSONL includes the `reasoningEffort` snapshot; a run without the flags omits it or reports null, unchanged from today otherwise.

### 7.4 TUI
- [x] `CommandSurfacesSuite` — `mux.effort` is registered with its slash aliases and appears under **Model**; the F1 menu lists it.
- [x] `ModalsSuite` / `TuiShellSuite` — the picker opens with the current level marked, applies a selection, persists it, and the sidebar `EFFORT` line reflects the change (frame-snapshot assertion where those suites already snapshot the sidebar).

### 7.5 Run matrix
- [x] `dotnet test src/Test.Xunit` and `dotnet test src/Test.Nunit` green on **net8.0** and **net10.0**.
- [x] `dotnet run --project src/Test.Automated -- --results results.json` exits 0.
- [x] Full solution builds with **0 warnings**.

---

## 8. Documentation

Write the prose so it reads like the rest of Mux's docs: direct, specific, no filler. Avoid the generic "This enables…" register.

### 8.1 README.md
- [x] Options table — add `--effort`, `--effort-openai-value`, `--effort-gemini-budget`, `--effort-ollama-think` with concise descriptions.
- [x] Interactive Commands block — add `/effort` (aliases `/reasoning`) with a one-line description.
- [x] New **Reasoning Effort** section — the level table from §2.1, how a level drives provider defaults, per-endpoint config, the headless flags and their precedence, and the `run_started` field. State the backward-compatibility guarantee: no selection ⇒ unchanged requests.
- [x] JSONL contract bullets — add `run_started` `reasoningEffort`, and note `reasoningEffort` can appear in `cliOverridesApplied`.
- [x] Version badge — align to `0.8.0` (currently stale).

### 8.2 CONFIG.md
- [x] Document the `endpoints.json` `reasoningEffort` object (`level` plus the three overrides, ranges, and "omit for off"). If the Phase 2 global default ships, document `settings.json` `defaultReasoningEffort` and precedence.

### 8.3 USAGE.md
- [x] Interactive: `/effort`, the picker, and the endpoint-form fields, with the §6 mockups.
- [x] Headless: worked examples, including a per-provider override and `--effort off`.

### 8.4 CHANGELOG.md
- [x] New `v0.8.0` entry (dated on release) under **Added**, describing the endpoint-scoped level, the level-driven provider defaults via PolyPrompt 2.1.0, the four CLI flags, the `/effort` picker and sidebar indicator, and the `run_started` field — and stating it is additive.

### 8.5 Repository requirements
- [x] Align every version stamp: `Defaults.ProductVersion` (`src/Mux.Core/Settings/Defaults.cs`, currently `0.7.0`), the README badge, any `<Version>` in the `.csproj` files, and the CHANGELOG. Grep for the current string first so none is missed.
- [x] `DOCKERHUB_README.md`: Mux ships as a CLI with no Docker image, so this is not applicable. If a container image is ever added, fold the reasoning-effort summary into it then. Note the decision in the PR description.

---

## 9. Versioning & release

- [x] Confirm the change is strictly additive — a new nullable endpoint field, four new optional flags, one new JSONL field, one new command. No existing behavior changes when nothing is selected. That makes `0.7.0 → 0.8.0` correct.
- [x] Bump PolyPrompt to `2.1.0` in `Mux.Core.csproj` and restore.
- [x] Build Release, run the full test matrix, and confirm the four docs and all version stamps agree before tagging.

---

## 10. Standards compliance (c:\code\agents\requirements)

The implementation must satisfy the shared standards, not just Mux's local conventions:

- **CODE_STYLE** — usings inside the namespace, system usings first then others alphabetically; XML docs on all public members and methods (none on private); validated members use `_PascalCase` backing fields with explicit getters/setters; **no tuples** anywhere (the CLI override merge and the config model use named types and a `Merge` method, not tuples); `.ConfigureAwait(false)` on awaits; `CancellationToken` on async methods that lack a class-level token; no `var`; one class or enum per file (hence separate files for `ReasoningLevelEnum`, its converter, and `ReasoningEffortConfig`); specific exceptions with meaningful messages and `<exception>` docs on new public methods; no `Console.WriteLine` in `Mux.Core`.
- **BACKEND_TEST_ARCHITECTURE** — descriptors in `Test.Shared` with no console output; loopback strictly `127.0.0.1`; exercised through Test.Automated, Test.Xunit, and Test.Nunit on net8.0 and net10.0.
- **REPOSITORY_REQUIREMENTS** — README, CONFIG, USAGE, and CHANGELOG updated in step; source stays under `src/`.
- **WRITING_DOCUMENTS** — the README/CONFIG/USAGE/CHANGELOG prose and this plan avoid the generic "This…/These…" openings and stock connectors, vary sentence rhythm, and keep a clear point of view.

---

## 11. Acceptance criteria (definition of done)

- [x] An endpoint can carry a reasoning level (and optional per-provider overrides) in `endpoints.json`; omitting it changes nothing.
- [x] Selecting a level in the TUI persists it, updates the sidebar, and shows a notice; the endpoint form edits both the level and the advanced tuning.
- [x] `--effort` overrides the level headless; `--effort off` disables; the three provider flags override their fields; contradictions resolve predictably.
- [x] A selected level reaches the wire correctly per provider (proved by the `LlmBridgeSuite` capture) and is omitted entirely when off.
- [x] `run_started` reports the effective `reasoningEffort`, and `cliOverridesApplied` lists it when a flag drove the value.
- [x] All suites pass on net8.0 and net10.0 across Test.Automated, Test.Xunit, and Test.Nunit; Release builds with zero warnings.
- [x] README, CONFIG, USAGE, and CHANGELOG are updated; every version stamp reads `0.8.0`; PolyPrompt is `2.1.0`.

---

## 12. Risks & open questions

- [x] **Gemini through an OpenAI-compatible adapter.** Many Mux users reach Gemini via its OpenAI-compatible surface, where the wire field is `reasoning_effort`, not `thinkingConfig`. — _resolved:_ documented in the README Reasoning Effort section; through `OpenAiCompatible` the OpenAI projection (`reasoning_effort`) ships, which the `LlmBridgeSuite` OpenAI-compatible path already exercises.
- [ ] **Ollama `think` variance (open).** Effort maps to `think` (string level or boolean). — _note:_ live verification against a current reasoning model (e.g. `gpt-oss`) is still pending; the per-endpoint `OllamaThink` override is available as the escape hatch in the meantime.
- [x] **Off vs. omitted in the sidebar.** — _decision:_ the sidebar always shows the `EFFORT` row (`off` when unset) for discoverability.
- [x] **Global default (Phase 2).** — _decision:_ deferred; `v0.8.0` is endpoint-scoped only. The version stayed minor.

---

## 13. Progress log

_Add dated entries as work proceeds. Newest first._

| Date | Author | Update |
|---|---|---|
| 2026-08-12 | Claude (for Joel) | Implemented end to end. Core: `ReasoningLevelEnum` + converter, `ReasoningEffortConfig`, `EndpointConfig.ReasoningEffort`, `LlmClient` mapping onto PolyPrompt 2.1.0, clone. Headless: four `--effort*` flags, parse + validation, `CommandRuntimeResolver.ApplyReasoningOverride` (applied after `ResolveEndpoint` rather than through its signature — same effect, no Core signature change), `cliOverridesApplied`, `run_started.reasoningEffort`. TUI: `/effort` command + `EffortSelectModal` (self-sizing width), sidebar `EFFORT` line, endpoint-form fields, form widened 54→60. Switch notice is period-free per request. Versions aligned to **0.8.0** (csproj ×2, `Defaults.ProductVersion`, README badge); PolyPrompt → 2.1.0. Docs: README, CONFIG, USAGE, CHANGELOG. Tests: added cases to LlmBridge (wire proof), EndpointConfig, CommandRuntimeResolver, HeadlessFeatures, CommandSurfaces — **506/506 pass** on net8.0 and net10.0 across xUnit and NUnit. Global default (Phase 2) intentionally deferred. |
