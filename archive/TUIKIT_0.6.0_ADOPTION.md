# TUIKit 0.6.0 Adoption

TUIKit 0.6.0 shipped 18 general-purpose components that were drawn from patterns mux had built by
hand (see TUIKit's own `IMPROVEMENTS_FOR_MUX.md`). This document records what mux adopted, what it
did not, and why. The package reference moved from `0.5.1` to `0.6.0` in `Mux.Cli.csproj`.

**Status:** builds clean on `net8.0` + `net10.0`; full Touchstone suite green (**528/528**) after each
adoption.

---

## Adopted

| # | Component | mux code it replaced | Notes |
|---|-----------|----------------------|-------|
| 14 | `HintText.Wrap` (`TUIKit.Content`) | `App/HintText.cs` (deleted) | Same default ` · ` separator and wrapping. Callers in `PromptEditorModal`/`TasksModal` pass a non-null hint and a floored width, so TUIKit's stricter guards don't fire. |
| 15 | `ColumnFormatter.Format` (`TUIKit.Content`) | `App/CommandMenuFormatter.cs` (deleted) | The command menu builds `[title, chord, alias]` rows and delegates aligning to `ColumnFormatter.Format(rows, 2)`. `CommandMenuFormatterSuite` was repointed to `ColumnFormatter` and still guards the column alignment. |
| 17 | `SubmitKeyResolver` / `SubmitDecision` (`TUIKit.Input`) | submit-vs-newline logic in `MuxTuiApp.OnKeyFilter` | Enter → submit, Shift+Enter / Ctrl+J → newline now come from `_SubmitResolver.Resolve(key)`. The `IsCarriageReturn` (raw rune 13/10) branch is kept as a fallback for terminals that deliver a modified Enter as a character, which the resolver classifies as `Ignore`. The redundant `case 'j':` in the Ctrl switch was removed. |
| 3 | `MultiSelectModal<T>` (`TUIKit.Modals`) | `App/MultiSelectModal.cs` (deleted) | Ollama import now uses `MultiSelectModal<string>`. Same behavior (Space toggle, `a` all, Enter → checked indices, Esc → null). The completion-vs-embedding warning moved into `FooterHint`. Result type changed `List<int>` → `IReadOnlyList<int>` at the call site. |
| 11 | `ActivityIndicator` (`TUIKit.Widgets`) | the full-screen "thinking" spinner/phrase state machine in `MuxTuiApp` + `ThinkingMessages.Next/SpinnerFrame` (removed) | The indicator's default braille frames and `CurrentLine` format (`glyph + " " + phrase`) match mux's exactly, so the on-screen line is identical. mux still owns the 130 ms animation loop and the `PaneLineHandle` (the indicator has no pane binding) and drives it with `Tick()`. Phrases are shuffled before being handed to the indicator so its sequential rotation keeps the variety mux's per-swap random pick gave. `ThinkingMessages.All`/`Spinner` are retained as the phrase/frame source (and for `ComposerSuite`). |

**Forced by the upgrade:** `ListView`/`FuzzyList` became generic in 0.6.0, so the three select modals
that wrap a list (`WideSelectModal`, `EffortSelectModal`, `EndpointSelectModal`) now declare
`ListView<string>`. This was required just to compile and is unrelated to any new feature.

---

## Not adopted (and why)

These are grouped by the reason. "Deferred" means capable but a large or risky refactor best done
deliberately; "incompatible" means a real API/behavior gap; "no benefit" means the mux code is already
the framework default or better for its case.

### Deferred — large refactor, real regression risk

- **#2 `DialogModal` base — the biggest consolidation, deferred.** mux has ~11 modals that each
  duplicate centered-box sizing, border draw, footer, and a private `Trim`. `DialogModal` would remove
  that duplication, but adoption is not a drop-in: each modal's `Render` must be rewritten into the
  `MeasureContentWidth` / `MeasureContentHeight` / `RenderContent(innerSurface)` contract, and several
  modals are non-trivial (forms rendered through an off-screen `CellBuffer` with focus-following
  scroll, a multi-line editor pane, tabbed profile editing). Two concrete gaps also apply:
  `DialogModal` hardcodes `BorderStyle.Rounded` and sets `ascii = false` unconditionally, so the
  theme's ASCII-border fallback is lost for non-Unicode terminals; and every modal's border/background
  fill would be re-derived, risking `FrameSnapshotSuite`/modal snapshot drift. Recommended as the top
  follow-up, migrated one modal at a time with snapshots checked per modal — not as a single sweep.

- **#10 `Command` / `CommandRegistry` (`TUIKit.Input`) — deferred.** mux's
  `CommandDescriptor` + `MuxCommandCatalog` + `MenuBarBuilder` + `SlashCommandParser` already mirror
  `CommandRegistry`'s design (one catalog → chords, menu bar, palette, slash routing). Swapping to it
  is a large, mostly-lateral rewrite, and it changes UX: `CommandRegistry.BuildPalette()` returns a
  `FuzzyList<Command>`, whereas mux's palette is a static, three-column aligned list (the
  `ColumnFormatter` work above). Worth doing for the consolidation, but it should be a deliberate
  change that also decides whether to move the palette to fuzzy filtering.

- **#12 `StreamingTranscript` (`TUIKit.Content`) — deferred.** `StreamingTranscript` covers the
  reusable core of `AgentEventProjector` (buffer streaming text → finalize a block as Markdown → keep
  keyed status lines updatable in place). But `AgentEventProjector` (401 lines) layers agent-specific
  behavior on top — thinking blocks, task-plan lines, tool status flipping to ✓/✗ — and is guarded by
  `ProjectorSuite`. The right shape is to use `StreamingTranscript` as the engine and keep the
  agent-specific orchestration above it; that is a meaningful rewrite of a central, well-tested class,
  not a swap.

- **#4 / #8 focus-following scroll + dynamic form fields — deferred, coupled to #2.**
  `ScrollView.AutoScrollToFocus` + `Form : IScrollExtent` would replace `EndpointFormModal`'s
  hand-rolled `ComputeScrollOffset` (render the form into an off-screen buffer and copy a scrolled
  window), and `Form.Clear()` + re-`Add` + `SetFocusedField` matches how `McpServerFormModal` already
  rebuilds fields on transport/auth change. Both live inside form modals whose box drawing is the
  `DialogModal` migration, so they are best done together with #2. Note `FormField` is `internal` in
  TUIKit and there is no `VisibleWhen` predicate, so conditional fields still require a `Clear()` +
  rebuild (which is what mux already does).

- **#5 generic `ListView<T>` parallel-array removal — deferred, coupled to the modals.** The
  index→object parallel arrays live in `MuxTuiApp`'s menu builders and are consumed by
  `WideSelectModal`/`EndpointSelectModal`, which return an index. Removing the parallel arrays means
  making those modals generic over the row type (return the object, not the index), which is part of
  the same modal rework as #2/#6.

### Incompatible — real API / behavior gap

- **#6 `ActionListView<T>` (`TUIKit.Widgets`) — partial fit.** It provides exactly mux's
  `EndpointSelectModal` shape (per-row chords → a typed `ListAction<T>`), but its per-row enabled
  predicate is `Func<T, bool>` — keyed on the **item**, whereas mux gates the edit/remove shortcuts by
  **row position** (only the first N rows are endpoints). That can only be bridged by a closure over
  the list's `SelectedIndex`, i.e. working around the API rather than using it. There is also no
  `SelectListModal` wrapper (it was planned but not shipped), so the modal shell stays hand-written.
  Net benefit over the current ~40-line `HandleKey` is marginal, so it was left as-is.

- **#7 `ReorderableList<T>` (`TUIKit.Widgets`) — missing capability.** It does reorder (`[`/`]` or
  Alt+Up/Down) + delete + `Reordered`/`Removed` events, but `QueueEditorModal`'s defining feature is
  inline **editing** (a `TextField` edit mode with a navigate/edit state machine). `ReorderableList<T>`
  ships no inline rename/edit, so adopting it would drop functionality.

- **#18 OS-adaptive key labels (`KeyChord.ToLabel` / `KeyLabel`) — not behavior-preserving.** mux's
  `ModifierLabel` renders the **words** `CTRL` / `OPTION` / `CMD` / `SHIFT` per-OS. TUIKit's
  `ToLabel` renders either compact glyphs (`⌃⌥⇧⌘`) or ASCII `Ctrl+`/`Alt+`/`Shift+`/`Super+` — it never
  emits `Cmd`/`Option`/`Command` words, and the glyph set is chosen by `KeyLabelStyle`, not by OS
  (only `KeyLabel.Recommended` picks the style by OS). Adopting it would change the visible footer
  labels, so it was left as-is to preserve the current UX. (Adopt only if the symbol style is desired.)

### No benefit — mux already at the framework default, or a poor structural fit

- **#1 per-region backgrounds — no benefit.** The feature exists to *tint* a region (the
  `SidebarRole`/`StatusBarRole` styles). mux deliberately renders a fully transparent look:
  `CreateTheme()` uses a default (transparent) background and `ApplyPaneBackgrounds` only reinforces
  transparency, which is already the framework default. There is nothing to gain and switching the
  panes to region backgrounds is a lateral change with snapshot risk.

- **#9 `DefinitionList` (`TUIKit.Widgets`) — structural mismatch.** It right-aligns values to the
  panel width and models a flat list of `(label, value)` rows plus section headers. `SidebarView`
  left-aligns values (`{label,-9}{value}`) and also renders a cyan bold **header** line (the model
  name), blank spacers between groups, and a label-less status line — none of which `DefinitionList`
  represents. Adopting it would change the sidebar's appearance and still leave the non-tabular parts
  hand-written, so it was left as-is.

### New capability — opportunity, not a replacement

- **#13 autocomplete (`AutocompleteOverlay` + `ISuggestionProvider`).** mux has no completion popup for
  `/`-commands today, so this is an enhancement rather than a replacement. It pairs naturally with a
  future `CommandRegistry` adoption (#10): feed the registry's slash names to a
  `PrefixSuggestionProvider` behind an `AutocompleteOverlay` anchored to the composer caret.

- **#16 `Rule` (`TUIKit.Widgets`) — poor fit for mux's usage.** `Rule` is a layout widget (a region's
  content). mux draws its boundary rules in a `RenderOverlay` hook at absolute coordinates (above the
  composer and the queue strip), which is not a layout-widget placement. Converting would mean
  restructuring the overlay into real regions for little gain.

---

## Recommended follow-up sequence

1. **`DialogModal` migration (#2)** — highest consolidation value. Migrate one modal at a time, start
   with the simplest box modals (`MuxBoxModal`, `WideSelectModal`, `EffortSelectModal`), check the
   modal snapshot after each, and decide on the ASCII-border fallback (either accept Rounded-only or
   raise it upstream in TUIKit). The form modals (`EndpointFormModal`, `McpServerFormModal`) come last
   and pull in #4/#8/#5 together.
2. **`CommandRegistry` (#10)** — after deciding palette UX (keep aligned columns vs. move to
   `BuildPalette`'s fuzzy list).
3. **`StreamingTranscript` (#12)** — refactor `AgentEventProjector` to sit on top of it.
4. **autocomplete (#13)** — once #10 lands, wire the composer to slash-name completion.

Items #6, #7, #18, #1, #9, #16 are intentionally left as-is per the reasons above; revisit #18/#1 only
if the visual change is explicitly wanted, and #6/#7 if TUIKit closes the predicate/rename gaps.
