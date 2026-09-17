# Prompt management

Every string mux feeds a model — the persona, the compaction framing, the tool descriptions, the little
status payloads a tool returns — is legible and editable. There is no prompt buried in a `.cs` file that you
cannot see and change. This document is the map of that system: what the prompts are, where each lives, how it
resolves, and how to edit or reset it on every surface.

## Two homes: profiles and the operational catalog

A prompt lives in one of two places, decided by its **scope**.

**Prompt profiles** hold the switchable *persona* prompts. A profile carries three fields — the system prompt,
the tools-disabled system prompt, and the compaction system prompt — and exactly one profile is active at a
time. Profiles exist because people genuinely want more than one persona and want to swap the active one.
They persist to `~/.mux/prompts.json` under a `prompts` array; a blank field inherits the built-in default, so
a profile only needs to carry what it customizes.

**The operational catalog** is a single, code-defined inventory of every *other* model-facing prompt. It is
independent of which profile is active — nobody wants three copies of their title-generation prompt just
because they keep three personas. Each entry ships with a sensible coded default (the catalog is defined in
`src/Mux.Core/Prompting/PromptCatalog.cs`, so a default always exists even with no config present). A user
override for an entry is stored additively in a forward-tolerant `operational` map in the same
`~/.mux/prompts.json`: a key that is absent resolves to the coded default, and deleting a key is the reset.

One resolver — `PromptResolver` — is the single read path. `GetEffective(key)` returns the override when set
and the coded default otherwise; `Resolve(key, substitutions)` also fills runtime placeholders. Model-facing
call sites (the compaction sidecar, title generation, the endpoint probe, the skills/MCP section lead-ins, all
sixteen tool descriptions, the tool-result payloads) read through it, so a single edit changes what the model
sees everywhere that prompt is used.

## The `PromptKind` taxonomy

Every entry knows what it is for. The kind is a first-class property so a Prompts view can group and label the
whole set uniformly, and so the "type of use" is explicit rather than inferred from which field a string
happens to live in.

| Kind | Scope | Example keys | What it is |
|---|---|---|---|
| `SystemPersona` | Profile | `system`, `tools-disabled` | The persona sent every turn, and its no-tools variant. |
| `Compaction` | Global | `compaction.system`, `compaction.user`, `compaction.summary-prefix` | The history-compaction sidecar's system prompt, the user framing that introduces the history, and the synthetic-summary marker. |
| `TaskPlanning` | Global | `task-planning.guidance` | Guidance injected when task planning is on. |
| `TitleGeneration` | Global | `title.system`, `title.user` | Generates a short session title. |
| `ToolSection` | Global | `section.skills`, `section.mcp` | Lead-ins for the skills and MCP prompt sections. |
| `ToolDescription` | Global | `tool.read_file`, `tool.grep`, `tool.run_process`, `tool.spawn_subagent`, … | The description each tool advertises to the model. |
| `ToolResult` | Global | `result.tool_call_denied`, `result.unknown_tool`, `result.tool_policy_denied`, `digest.truncation-marker` | Structured status messages returned to the model in a tool result. |
| `Diagnostics` | Global | `probe.system`, `probe.user` | The endpoint diagnostic probe. |
| `FileContext` | Global | `file.map.note`, `file.summary.map`, `file.summary.reduce` | The large-file map note and the map/reduce summarizer prompts. |
| `SubagentPersona` | External | `subagent.<name>` | Subagent seed personas. |

### Placeholders

Some prompts carry placeholder tokens that must survive an edit. The persona `system` prompt keeps
`{WorkingDirectory}`, `{ToolDescriptions}`, and `{TaskPlanningGuidance}`; `tools-disabled` keeps
`{WorkingDirectory}`; `tool.run_process` keeps `{OperatingSystem}`, `{Shell}`, and `{ShellArgsHint}`;
`result.unknown_tool` and `result.tool_policy_denied` keep `{ToolName}`; and `file.map.note` keeps `{Path}`.
Setting an override that drops a required placeholder is rejected — the resolver validates every write, so a
mangled prompt cannot quietly degrade a run.

### Two deliberate boundaries

- **Subagent personas keep their own home.** They already persist to `subagents.json` and have a dedicated
  manager on every surface, so duplicating them into the catalog would create two sources of truth. The
  Prompts views list them read-through and deep-link to the Subagents editor — present in the inventory,
  edited where they already live.
- **The synthetic-summary marker is a stable marker.** `compaction.summary-prefix` is also used to *detect*
  already-summarized turns, so it must not drift; it appears in the catalog for visibility but is treated as a
  fixed marker rather than a freely-swapped prompt.

Also by design, the never-duplicated persona / compaction-system / task-planning defaults keep their literal
single home in `Defaults` (which the catalog references) rather than being relocated into the catalog — this
avoids a static-initialization cycle between the two while still surfacing them for editing.

## Editing and resetting, per surface

Every surface can see every prompt grouped by kind, edit any editable one, and reset it to its default.

- **Web dashboard** — the **Prompts** page shows the three profile prompt fields in the profile editor and an
  **Operational prompts** table below it (sorted by kind, with a custom/default indicator). Edit or reset any
  global entry inline; persona rows are read-only there (edited in the profile above), and a pointer links to
  the Subagents view.
- **Terminal (TUI)** — the profile editor (`/prompts`) edits the three persona fields; a sibling
  **operational-prompt catalog** browser (`/operational-prompts`, also `/catalog`) lists every entry by kind
  with `e`/`Enter` to edit, `r` to reset, and `Esc` to close.
- **Desktop** — the Prompts window shows the profiles table and a catalog table below it, with an edit dialog,
  a reset action, and an overridden badge.
- **VS Code** — the management tree groups catalog entries by kind under **Prompts**, each a click-to-edit node
  (`mux.manage.editCatalog` / `mux.manage.resetCatalog`); subagent personas appear as a read-through group that
  deep-links to the subagent editor; and the profile form edits all three prompt fields.

## Over REST

`mux serve` exposes the whole system: `GET`/`PUT /v1.0/api/prompts` for the three-field profiles and
`GET`/`PUT /v1.0/api/prompts/catalog` for the operational catalog (a blank override clears it; unknown,
profile-scoped, and placeholder-dropping writes are rejected with `400`). See `docs/REST_API.md`.
