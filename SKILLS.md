# Mux Skills — Design and Implementation Plan (targets v0.4.0)

Mux can already read files, run processes, search the web, and call MCP servers. What it cannot do yet is remember *how you like a job done* and repeat it exactly. A skill closes that gap: a small, versioned folder of Markdown and code that turns a fuzzy request — "see how my working tree compares to what's on GitHub" — into a fixed procedure the agent runs the same way every time. The Markdown carries judgment (when to reach for the skill, what the steps mean, what to watch out for); the code carries determinism. The model decides *whether* to use a skill. The skill decides *what actually executes*.

This is the build plan for the whole feature, and it ships in the **v0.4.0** line — the version has already been bumped across every `csproj`, `Defaults.ProductVersion`, the README badge, and the changelog. Three commitments shape every decision below, because the request named them plainly. A mux user should find it *easy* to author, inventory, and manage skills without leaving the app. The feature should be *ridiculously* tested, at every layer and on every platform mux runs on. And it must follow `c:\code\agents\requirements` to the letter — the `CODE_STYLE.md`, `REPOSITORY_REQUIREMENTS.md`, and `WRITING_DOCUMENTS.md` rules are not a finishing pass here, they are design constraints. A compliance appendix at the end maps each rule to a concrete decision.

Mux lives at `C:\Code\Mux`, split into `Mux.Core` (the harness library) and `Mux.Cli` (the TUIKit app plus the non-interactive verbs), with configuration under `~/.mux/`. Skills extend both halves and add one folder to that directory.

---

## Implementation status

The feature is being built on the `feature/skills` branch, phase by phase, each compiled, tested, and committed. This section is kept current as work lands.

- **Phase 0 — Provider seam — done.** `IExternalToolProvider`, `AgentLoopOptions.ExternalToolProviders`, aggregation/routing/classification in `AgentLoop`, per-job cloning in `JobManager`. Covered by `ExternalToolProviderSuite`.
- **Phase 1 — Core model and loader — done.** The skill model, `SkillInterpreters`, a dependency-free frontmatter parser, and `SkillLoader`. Covered by `SkillLoaderSuite` (validation matrix + 500-iteration fuzz).
- **Phase 2 — Execution and tools — done.** `SkillInterpreterResolver`, `SkillExecutor`, `SkillCatalog`, `SkillToolProvider` (`skill` / `run_skill`). Covered by `SkillProviderSuite`.
- **Phase 3 — Interactive wiring — done.** `MuxSettings` skill fields, `SettingsLoader` skill index (`skills.json`) load/save and `skills/` seeding, `SkillRuntime` (an `IExternalToolProvider` with immutable-swap discovery, index enablement, and prompt-section building), `ExternalToolsBinder` composing MCP and skills onto the template, and the `Program.RunInteractive` wiring. Covered by `SkillRuntimeSuite` and `ExternalToolsBinderSuite`. The model can now discover and run skills in a live session.
- **Phase 4 — Authoring and management surface — done.** `SkillScaffold`/`SkillScaffoldWriter`, a testable `SkillManager` (create, enable/disable, remove, duplicate, import), and the `/skills` inventory modal with status glyphs, per-skill actions (view, **in-app edit**, enable/disable, duplicate, remove), a step-by-step create wizard, local-path import, and reload. Skills are edited in the app through `SkillEditorModal`, a near-full-screen `SKILL.md` editor (Ctrl+S saves and re-validates, Esc cancels). Covered by `SkillManagerSuite`, `SkillManagementSuite`, and the editor-modal cases.
- **Phase 5 — CLI verb — done.** `mux skill list | show | validate | run | new | add`, dispatched from `Program`, sharing the same `SkillManager`/`SkillLoader`/`SkillExecutor` core as the UI. `validate` returns a nonzero exit on failure for CI, and `run` returns the process contract for hooks and automation. Covered by `SkillCommandSuite`.
- **Phase 6 — default library — first wave done.** `DefaultSkillLibrary` seeds a curated, cross-platform set on first run through `EnsureConfigDirectory`, preserving user edits on re-seed. The seeded wave is ten skills spanning the catalog: `git-status-vs-head`, `git-commit`, `git-push`, `git-secret-scan`, `dotnet-build`, `dotnet-test`, `todo-scan`, `gitignore-audit`, `env-report`, `json-validate`. Covered by `DefaultSkillsSuite` (all validate, one runs, re-seed preserves edits). The remaining catalog entries in Section 10.1 are follow-on content the same mechanism ships without code changes.
- **Phase 7 — documentation — done.** README gains a Skills section, a Highlights bullet, the `/skills` command entry, and a `SKILLS_AUTHORING.md` link; CONFIG documents the settings fields, `skills.json`, and the `skills/` directory; USAGE gains a Skills section; the CHANGELOG records skills under v0.4.0 (Added) and the provider seam (Changed); GETTING_STARTED gains a create-your-first-skill note; and `SKILLS_AUTHORING.md` is the full `SKILL.md` reference with three worked examples. CI runs `mux skill validate` against the seeded library as a gate.

All seven phases are implemented, tested, and committed on `feature/skills`.

Three decisions were settled during execution and the plan reflects them:

- **Frontmatter parsing uses a self-contained parser, not `YamlDotNet`.** The schema is small and fixed, and adding the first YAML dependency to a JSON-first codebase was left open for the owner. The parser fails soft and is fuzzed. `YamlDotNet` remains a drop-in replacement if the schema grows.
- **`run_skill` is classified as mutating** regardless of a skill's `mutating` flag. The two-tool design cannot resolve the target skill from the tool name alone at classification time, and gating "run arbitrary skill code" behind the write lease and approval is the safer default. The `mutating` flag drives the inventory display and remains available for a future per-command classification.
- **`SkillRuntime` re-scans on a periodic timer plus explicit refresh, not a `FileSystemWatcher`.** The timer plus the manager's refresh-on-edit covers the workflow without the platform quirks of file watching; a watcher can be added later as a latency optimization.

---

## 1. What a skill is

A skill is a directory. It holds one required file, `SKILL.md`, whose YAML frontmatter names the skill and says when to use it, and whose body explains the procedure to the model in prose. Alongside the prose a skill declares **commands** — named, runnable units that execute either a fenced code block from the body or a bundled script from a `scripts/` subfolder, through a declared interpreter, with a timeout and captured output. Because the code is authored, versioned, and stored rather than improvised per turn, running a command produces the same steps regardless of which model drives.

A skill is deliberately not a compiled plugin and not a second MCP transport. It has no in-process entry point and no long-lived server. It is data plus scripts that the existing process-execution machinery runs on demand, which keeps the trust story honest: a skill can do exactly what `run_process` can do, no more. Authoring stays within reach of anyone who can write a shell script and a paragraph of instructions — and, once the wizard in Section 9 exists, within reach of anyone who can answer a few prompts in a modal.

The design borrows the shape of the MCP feature shipped in this same 0.4.0 line, on purpose. MCP discovers tools from a server, advertises them to the model, and routes calls back. Skills discover capabilities from a folder, advertise them to the model, and route execution back. Reusing that shape keeps the harness coherent and lets both features share one composition point instead of fighting over it.

---

## 2. Anatomy of a skill

### 2.1 Directory layout

One skill occupies `~/.mux/skills/<skill-id>/`. The id is the folder name — lowercase, hyphen-separated, no path separators, no `..`. The loader rejects anything else before it reads a byte of content.

```
~/.mux/skills/
  git-status-vs-head/
    SKILL.md                # required: frontmatter + prose + optional code blocks
    scripts/                # optional: bundled executables referenced by commands
      summarize.sh
    resources/              # optional: templates, checklists, reference text
      report-template.md
```

### 2.2 The SKILL.md contract

Frontmatter is YAML between `---` fences. The body is ordinary Markdown, with one wrinkle: a fenced code block may carry an `id=` tag so a command can point at it.

```markdown
---
name: git-status-vs-head
title: Compare working tree to GitHub HEAD
description: Summarize how the local working tree and current branch differ from the remote default branch on GitHub, before committing or pushing.
version: 1.0.0
enabled: true
mutating: false
whenToUse: The user asks how local changes compare to GitHub, or wants a pre-flight check before committing or pushing.
allowedTools: [run_process, read_file]
tags: [git, github, vcs]
commands:
  - name: summarize
    description: Print a structured summary of local-vs-remote differences.
    run: scripts/summarize.sh
    interpreter: bash
    timeoutMs: 60000
  - name: files-changed
    description: List only the changed file paths versus origin/HEAD.
    block: files-changed
    interpreter: bash
---

## What this does

Fetches the remote default branch, then reports staged, unstaged, and untracked
changes and how the current branch has diverged from `origin/HEAD`. Read-only —
it never writes to the index or pushes.

## How to read the output

...

```bash id=files-changed
set -euo pipefail
git fetch origin --quiet
base="$(git rev-parse --abbrev-ref origin/HEAD | sed 's@^origin/@@')"
git diff --name-only "origin/${base}"
```
```

Frontmatter fields map one-to-one onto `SkillManifest` (Section 5.1):

| Field | Type | Default | Meaning |
|---|---|---|---|
| `name` | string | required | Stable identifier surfaced to the model; must match the folder id. |
| `title` | string | `name` | Human label for menus and listings. |
| `description` | string | required | One or two sentences; the only body text the model sees until it opens the skill. |
| `version` | string | `0.0.0` | Semantic version for provenance and pinning. |
| `enabled` | bool | `true` | Author's default; the runtime override in `skills.json` wins. |
| `mutating` | bool | `true` | `false` marks the skill read-only, so its commands skip the write lease and qualify as auto-safe. |
| `whenToUse` | string | empty | Guidance the model uses to judge relevance; folded into the catalog line. |
| `allowedTools` | string[] | empty | Advisory list of tools the skill expects; recorded, not yet enforced. |
| `tags` | string[] | empty | Grouping, filtering, and search in the inventory view. |
| `commands` | list | empty | Named runnable units (below). |

Each command carries `name`, optional `description`, exactly one of `run` (a script path relative to the skill directory) or `block` (the `id` of a body code block), an `interpreter` from the allowlist, and an optional `timeoutMs` defaulting to the process tool's 120000. A command with neither `run` nor `block`, or with both, is a validation error, not a silent no-op.

### 2.3 Interpreters

The interpreter allowlist maps a friendly name to a concrete launcher per operating system, reusing the platform logic already proven in `RunProcessTool` (`cmd.exe /c` on Windows, `/bin/sh -c` elsewhere). The initial set: `bash`, `sh`, `pwsh`, `python`, `node`, and `dotnet-script`. `SkillInterpreterResolver` owns the mapping and refuses anything off the list, so a malformed manifest cannot ask the harness to launch an arbitrary binary. Block code is written to a temporary file with the right extension and executed by the resolved interpreter; bundled scripts run in place from the skill directory. Neither path interpolates arguments into a command string — arguments arrive as argv, which closes the obvious injection hole.

---

## 3. Where skills live and how state is stored

Skills are files, and mux already treats `~/.mux/` as the home for the files it manages. The `skills/` folder joins `endpoints.json`, `settings.json`, `prompts.json`, and `mcp-servers.json`. Enable and disable state does not belong inside the author's `SKILL.md`, because toggling a skill from a menu should never rewrite a file the user hand-edited. A separate index carries runtime state:

```json
// ~/.mux/skills.json
{
  "skills": [
    { "id": "git-status-vs-head", "enabled": true, "pinnedVersion": null },
    { "id": "git-secret-scan",    "enabled": false, "pinnedVersion": "1.2.0" }
  ]
}
```

`SettingsLoader` gains a `LoadSkillIndex` / `SaveSkillIndex` pair modeled on the MCP wrappers and using the atomic writer, and `EnsureConfigDirectory` learns to create `skills/` and seed the default library (Section 10) when the folder is absent. The index is authoritative for enablement. A skill present on disk but missing from the index defaults to its frontmatter `enabled` value and gains an index row on first save; a skill listed in the index but missing on disk is reported as an error in the inventory view rather than dropped silently.

`MuxSettings` grows a few validated, documented fields: `SkillsEnabled` (bool, default `true`), `SkillRefreshIntervalSeconds` (int, default 30, clamped to a floor), and `SkillsDirectory` (nullable override for teams that keep a shared, version-controlled library outside `~/.mux/`). `RuntimeMetadata` gains `SkillsDirectoryPresent` and `SkillCount`, so `mux probe` and the `run_started` event report the skill surface the way they already report MCP.

---

## 4. Composition: making MCP and skills share one seam

The harness has exactly one external hook. `AgentLoopOptions` exposes a single `ExternalToolExecutor` delegate and a single `AdditionalTools` list, and `AgentLoop.ExecuteToolCallCoreAsync` tries the built-in registry first and that one delegate second. MCP already claims both. Bolting skills on with a second ad-hoc executor would leave two features racing for one slot, so the plan generalizes the seam before adding to it — and this refactor is Phase 0, the prerequisite for everything else.

Introduce `IExternalToolProvider` in `Mux.Core`:

```csharp
public interface IExternalToolProvider
{
    string Name { get; }
    IReadOnlyList<ToolDefinition> GetToolDefinitions();
    bool HasTool(string toolName);
    Task<ToolResult> ExecuteAsync(
        string toolName,
        JsonElement arguments,
        string workingDirectory,
        CancellationToken cancellationToken);
}
```

`AgentLoopOptions` gains `List<IExternalToolProvider>? ExternalToolProviders`. `AgentLoop.MergeToolDefinitions` aggregates built-in definitions, the legacy `AdditionalTools`, and every provider's definitions. `ExecuteToolCallCoreAsync` tries the built-in registry, walks the providers, dispatches to the first whose `HasTool` matches, then falls back to the legacy `ExternalToolExecutor`. The existing MCP path keeps working unchanged during the transition; once both features route through providers, the legacy fields retire in a later cleanup. `GetMutationKind` grows a provider-aware overload so a read-only skill's `run_skill` call is not force-classified as mutating — the classifier asks the responsible provider and falls back to `Mutating` when nothing claims the tool.

---

## 5. Core library: model, loading, execution

Everything that parses, validates, and runs a skill lives in `Mux.Core`, where it is unit-testable without a terminal. Each type is one file, one class or enum, under `src/`.

### 5.1 Data model (`src\Mux.Core\Models\`)

`SkillManifest` mirrors the frontmatter with validated, documented properties and backing fields. `SkillCommand` holds `Name`, `Description`, `ScriptPath`, `BlockId`, `Interpreter`, and `TimeoutMs`, with the documented invariant that exactly one of `ScriptPath`/`BlockId` is set. `Skill` is the fully parsed unit — manifest, raw body, extracted code blocks keyed by id, resolved directory path, and a validation result. `SkillStatus` is the detached snapshot the UI reads: `Name`, `Title`, `Enabled`, `Valid`, `CommandCount`, `Tags`, and an optional `Error`. `SkillValidationResult` carries `IsValid` and an ordered list of human-readable problems. Tool execution keeps returning the existing `ToolResult`, so the harness boundary keeps its shape.

Frontmatter parsing needs real YAML, because `commands` is a sequence of maps. Rather than hand-roll a fragile parser, add `YamlDotNet` (MIT, widely used) as a `Mux.Core` dependency and confine its use to `SkillLoader`; the rest of the codebase stays JSON-first and no public type leaks a YAML surface.

### 5.2 Loader and validation (`src\Mux.Core\Skills\SkillLoader.cs`)

```csharp
public sealed class SkillLoader
{
    public SkillLoader(string skillsDirectory);

    public IReadOnlyList<Skill> Discover();
    public Task<IReadOnlyList<Skill>> DiscoverAsync(CancellationToken cancellationToken);
    public Skill Load(string skillDirectory);
    public SkillValidationResult Validate(Skill skill);
}
```

Discovery enumerates immediate subfolders, rejects any id with a separator or `..`, reads `SKILL.md`, parses frontmatter, extracts `id=`-tagged blocks, resolves each command against a script file or a block, checks the interpreter against the allowlist, and confirms bundled script paths stay inside the skill directory. A skill that fails validation is still returned — `Valid == false`, errors populated — so the inventory view shows *why* a skill is broken instead of pretending it does not exist. Both the synchronous `Discover` and the `CancellationToken`-bearing `DiscoverAsync` are provided, per the rule that an enumerable-returning method gets an async counterpart.

### 5.3 Execution and determinism (`src\Mux.Core\Skills\SkillExecutor.cs`)

`SkillExecutor` runs one command and returns a `ToolResult`. It resolves the interpreter, materializes block code to a temp file or points at the bundled script, roots the working directory the way `RunProcessTool` does, enforces the command timeout through a linked cancellation source, kills the process tree on timeout, and truncates output to the shared `ToolSafetyLimits`. The result JSON matches `run_process` — `stdout`, `stderr`, `exit_code`, `timed_out` — so the model reads skill output with instincts it already has. Determinism comes from the code being fixed and the environment being explicit: the executor sets a small, documented set of variables (`MUX_SKILL_NAME`, `MUX_SKILL_DIR`, `MUX_SKILL_COMMAND`) so a script can find its own resources without guessing.

Skills that mutate the workspace inherit the same safety posture as every other mutating tool. A command from a skill whose manifest leaves `mutating` at its `true` default is classified `Mutating`, serializes through the write lease, and passes through the approval policy exactly like `run_process`. A skill marked `mutating: false` is `ReadOnly` and runs without the lease. A skill can never quietly escalate its own privileges, because the classification is derived by the harness from the manifest, not asserted by the running code.

### 5.4 Tools and the provider (`src\Mux.Core\Skills\SkillToolProvider.cs`, `SkillCatalog.cs`)

The model reaches skills through two tools, deliberately few. `skill` takes `{ "name": string }`, is read-only, and returns the named skill's full body, its command list with descriptions, and its resource inventory — progressive disclosure lives here, since the always-on catalog holds only names and one-line descriptions. `run_skill` takes `{ "name", "command", "args"?, "working_directory"? }`, executes one command deterministically, and returns the process-shaped result with the host skill's mutation kind. `SkillToolProvider` owns the current `SkillCatalog` and a `SkillExecutor`, implements `IExternalToolProvider`, and builds each tool's schema as anonymous objects like every existing tool. `SkillCatalog` holds the loaded skills and answers status and lookup queries so the provider stays thin.

---

## 6. Harness integration and prompt awareness

Tool wiring and prompt wiring meet in one place. `AgentLoop.MergeToolDefinitions` already concatenates built-in tools with `AdditionalTools`; after Phase 0 it also folds in every provider's definitions, so the two skill tools appear to the model beside the built-ins. Awareness of the individual skills — as opposed to the two tools — rides in the system prompt, mirroring how MCP appends a section after the `{ToolDescriptions}` substitution. A "Skills" section lists each enabled skill as `- <name>: <description>` under one instruction: call `skill` to read a skill's instructions, then `run_skill` to execute its commands. The catalog stays compact on purpose; full bodies load only when the model commits to a skill.

---

## 7. Interactive wiring

The CLI half follows the MCP blueprint closely enough that a reviewer who understands `McpRuntime` recognizes every part. `SkillRuntime` (`src\Mux.Cli\App\SkillRuntime.cs`) owns the lifecycle: its constructor takes a loader factory, an `onSkillsChanged` callback, and a refresh interval, matching `McpRuntime(Func<...>, Action, TimeSpan?)`. It discovers skills on a background start, watches the directory with a `FileSystemWatcher` and revalidates on the interval, and swaps the catalog atomically so an in-flight `run_skill` never observes a half-loaded set. It exposes `CurrentTools`, `FirstRefreshCompleted`, `Start()`, `GetStatus()` returning detached `SkillStatus` copies, `RequestRefresh()` for menu-driven reloads, an `ExecuteToolAsync` matching the executor delegate, and `IDisposable` with the full dispose pattern. It implements `IExternalToolProvider`, so the agent loop consumes it directly.

The template binding generalizes rather than duplicates. `McpTemplateBinder.Apply` currently owns both tool wiring and the prompt section for MCP; refactor it into `ExternalToolsBinder` (`src\Mux.Cli\App\ExternalToolsBinder.cs`) that takes the base prompt, base compaction prompt, ordered providers, and built-in tool count, then composes — concatenating every provider's tool definitions into `AdditionalTools`, setting `ExternalToolProviders`, recomputing `EffectiveToolCount`, and appending each provider's prompt section in order. The MCP section keeps its exact wording; the skills section slots in beside it. `Program.RunInteractive` grows by mirroring the MCP block it already contains: it constructs a `SkillRuntime` next to the `McpRuntime`, adds both to the provider list, points the existing `ApplyTemplate` closure at `ExternalToolsBinder.Apply`, reuses the `onToolsChanged → ApplyTemplate` rebind so a skill added at runtime updates awareness on the next turn, and disposes both runtimes in the `finally`.

---

## 8. The user experience for skills: two threads

Two audiences use skills, and the plan serves both without blurring them. The model *runs* skills through the two tools in Section 6, guided by the catalog in the prompt. The person *authors and manages* skills through a dedicated in-app surface, because asking a developer to hand-write YAML frontmatter and hunt for the right folder would waste the whole point. Section 9 is the second thread, and it is where most of the "make it easy" work lives.

---

## 9. Authoring and management: the guided modals

A new catalog entry `mux.skills` (title "Skills", category "Model", slash aliases `/skills` and `/skill`, no default chord to avoid collisions) opens the skills surface. It lands under the existing **Model** menu next to Endpoints and MCP servers, and the widened, column-aligned F1 command menu shows its aliases automatically. Everything below is built from primitives mux already has — `SelectModal`, `WideSelectModal`, `MultiSelectModal`, the form-style modals, `MessageModal`, and `MuxBoxModal` — so the surface feels like the rest of the app rather than a bolt-on.

### 9.1 Inventory: browse, search, and understand what you have

Opening `/skills` lands on the inventory. Each row shows a state glyph — filled for valid-and-enabled, hollow for disabled, a warning mark for invalid — followed by the title, tag chips, and command count, sorted with broken skills first so problems surface immediately. The header line summarizes the library at a glance: total skills, how many are enabled, how many failed validation. Typing filters by name, tag, or description against the loaded catalog, which matters once a developer has thirty skills and wants the three tagged `git`. Selecting a row opens a detail view in a `MuxBoxModal` that renders the skill's title, description, `whenToUse`, version, mutation posture, command list with per-command descriptions, and — when validation failed — the exact errors with the offending frontmatter line. The detail view is also where a broken skill explains itself, so "invalid" is never a dead end.

From a selected skill, a row-action menu drives the lifecycle: **View** (the detail modal), **Enable/Disable** (flip the `skills.json` row and `RequestRefresh()` so the change lands on the next turn), **Edit** (open `SKILL.md` in the user's `$EDITOR`/`$VISUAL` through `run_process`, or fall back to a field-by-field form when no editor is configured), **Validate** (re-run validation and show the result inline), **Duplicate** (copy the directory under a new id as a starting point), **Reveal** (print the skill's path so the user can open it in their own tools), and **Remove** (a `MessageModal` confirm, then delete the directory and its index row). Every mutation ends with a dimmed `WriteNotice`, matching how endpoint and MCP edits report themselves.

### 9.2 Create: a guided wizard, not a blank file

The centerpiece of the authoring experience is a create wizard reachable from a **+ New skill…** row in the inventory. It walks the user through a short sequence of focused modals, each validating before it advances, and it never leaves the user staring at empty YAML.

The first step captures identity: id (validated live against the naming rules and checked for collisions with existing skills), title, and a one- or two-sentence description. The second step captures behavior: read-only or mutating (with a plain-language explanation of what the write lease and approval mean), the primary tag set chosen from existing tags plus free entry, and the `whenToUse` guidance that will steer the model. The third step captures the first command, because a skill with no command is just a note: the user names it, picks an interpreter from the allowlist in a `SelectModal`, and chooses between an inline block (the wizard opens a small editor seeded with a starter snippet for the chosen interpreter) and a bundled script (the wizard creates `scripts/<name>.<ext>` with a shebang and opens it). The final step previews the generated `SKILL.md` in a `MuxBoxModal`, and on confirmation the wizard writes the directory, adds the `skills.json` row, runs validation, reports the result, and offers to open the new skill in the editor. A developer goes from intent to a working, validated skill in under a minute without memorizing the frontmatter schema.

The wizard is implemented as a small state machine over the existing modal primitives — one `SkillWizardState` object threaded through the steps, each step a focused modal that reads and writes that state — so the flow is testable step by step and a cancel at any point leaves nothing behind on disk.

### 9.3 Add and import: bring in skills you did not write

Authoring from scratch is one path; adopting an existing skill is the other, and the request called it out. An **⬇ Import skill…** row offers three sources. Importing **from a local path** copies a skill directory into `~/.mux/skills/` after validating it and resolving id collisions by prompting for a new id. Importing **from a Git repository** clones or sparse-fetches a URL through `run_process`, lets the user pick which skill directories to bring over with a `MultiSelectModal`, and copies the chosen ones — the same multi-select pattern the Ollama model import already uses, so it is familiar and already tested in spirit. Importing **from the bundled gallery** lists the default library (Section 10) that shipped with mux and lets the user install any subset, which doubles as the recovery path if someone deletes a default and wants it back. Every import runs the imported skill through validation before it is written and refuses to install a skill that fails, so the library never fills with broken entries through the front door.

### 9.4 Non-interactive management

Not every workflow happens in the REPL, so the same operations exist as a `mux skill` verb (Section 11). A developer can script the creation of a skill in a dotfiles setup, validate the whole library in CI, or run a skill from a Git hook. The interactive surface and the verb share the same core types, so behavior cannot drift between them.

---

## 10. The default library, curated for developers

An empty `~/.mux/skills/` teaches nobody how to write a skill and helps nobody on day one, so mux seeds a curated set on first run, weighted toward the chores a working developer repeats without thinking. The tables below are the shipping default — the concrete list to author, not a sketch. Every skill is read-only unless its name makes mutation obvious, and every mutating default refuses to run against the default branch without an explicit branch first, the same guardrail the harness follows. The whole set lives in the repository under `src/` as content and is copied into `~/.mux/skills/` by `EnsureConfigDirectory`, so the same skills are testable in CI and editable by the user.

### 10.1 The catalog

Forty-six skills across seven groups. The `M?` column marks whether the skill is mutating (write-lease and approval) or read-only. The `Cmd` column names each command the skill declares; the sentence describes what the skill is for. Interpreter defaults to `pwsh` for cross-platform reach, with `bash` where a POSIX pipeline is clearly cleaner (Section 10.3 covers parity).

**Git and GitHub** — the anchor group, where the motivating example lives.

| id | M? | Commands | Purpose |
|---|---|---|---|
| `git-status-vs-head` | read | `summarize`, `files-changed`, `ahead-behind` | Compare the working tree and current branch to `origin/HEAD` and report the difference. |
| `git-commit` | write | `commit`, `amend` | Stage and commit with a conventional message; refuses on the default branch. |
| `git-push` | write | `push`, `push-force-with-lease` | Push the current branch, set upstream; force only with lease. |
| `git-branch` | write | `create`, `switch`, `list` | Create and switch branches under a naming convention. |
| `git-sync` | write | `fetch`, `fast-forward`, `rebase` | Fetch and reconcile the current branch with the remote default. |
| `git-open-pr` | write | `open`, `status` | Draft and open a pull request through `gh` from the commit range. |
| `git-changelog-entry` | write | `add` | Insert a Keep-a-Changelog line under the current *Unreleased*/version heading. |
| `git-release` | write | `prepare`, `tag` | Roll the changelog and stage a version bump for review, then tag. |
| `git-undo-last-commit` | write | `soft`, `hard` | Undo the last commit, keeping changes by default. |
| `git-cherry-pick` | write | `pick`, `abort` | Apply a commit onto the current branch, with an escape hatch. |
| `git-stash-manager` | write | `save`, `list`, `pop`, `show` | Manage the stash without memorizing the flags. |
| `git-conflict-explainer` | read | `list`, `show` | List conflicted files and show the conflicting hunks. |
| `git-blame-summary` | read | `authors`, `churn` | Report top authors and change frequency for a path. |
| `git-secret-scan` | read | `staged`, `tree` | Scan the staged diff or working tree for credential patterns before they leave the machine. |
| `git-large-files` | read | `history`, `tree` | Find the heaviest blobs in history and the largest tracked files. |

**Build and quality** — mux is a .NET codebase and so are many users' projects.

| id | M? | Commands | Purpose |
|---|---|---|---|
| `dotnet-build` | write | `release`, `debug` | Build the solution and surface errors and warnings. |
| `dotnet-test` | write | `all`, `filter`, `coverage` | Run tests, optionally filtered, optionally with coverage. |
| `dotnet-format` | write | `apply`, `verify` | Apply formatting, or verify with no changes for a gate. |
| `dotnet-restore` | write | `restore` | Restore packages for the solution. |
| `dotnet-outdated` | read | `list`, `vulnerable` | List outdated and vulnerable package references. |
| `dotnet-pack` | write | `pack` | Produce NuGet packages. |
| `dotnet-publish` | write | `framework-dependent`, `self-contained` | Publish the app in either mode. |
| `ci-repro` | write | `run` | Reproduce the repository's CI matrix locally before pushing. |

**Hygiene** — report rather than change.

| id | M? | Commands | Purpose |
|---|---|---|---|
| `todo-scan` | read | `scan` | Find `TODO`/`FIXME`/`HACK` markers with file and line. |
| `dead-code-scan` | read | `scan` | Surface likely-unused symbols as a starting point for cleanup. |
| `license-header-check` | read | `check` | Verify source files carry the expected license header. |
| `gitignore-audit` | read | `audit` | Flag tracked junk and untracked files that should be ignored. |
| `large-file-scan` | read | `scan` | List working-tree files over a size threshold. |
| `line-ending-check` | read | `check` | Detect CRLF/LF/mixed line endings. |
| `readme-audit` | read | `audit` | Flag README drift: referenced commands that no longer exist, dead links. |
| `codestyle-audit` | read | `audit` | Check a C# tree against the `CODE_STYLE.md` rules this plan follows. |

**Scaffolding** — turn a convention into one command.

| id | M? | Commands | Purpose |
|---|---|---|---|
| `new-class` | write | `create` | Emit a C# class file already shaped to the code style. |
| `new-tool` | write | `create` | Stub an `IToolExecutor` with schema and execute skeleton. |
| `new-touchstone-suite` | write | `create` | Lay down a Touchstone test suite. |
| `new-skill` | write | `create` | Scaffold a starter skill so the library documents its own format. |

**Documentation** — keep prose and code together.

| id | M? | Commands | Purpose |
|---|---|---|---|
| `doc-sync` | read | `check` | Flag where README/USAGE have drifted from the code. |
| `api-surface-diff` | read | `diff` | Diff the public API surface between two refs. |
| `adr-new` | write | `create` | Create an Architecture Decision Record from a template. |

**Workflow** — the recurring write-ups.

| id | M? | Commands | Purpose |
|---|---|---|---|
| `standup-summary` | read | `since` | Summarize recent commits by author and day. |
| `release-notes` | read | `assemble` | Assemble notes from the changelog's current section. |
| `pr-description` | read | `draft` | Draft a pull-request description from the commit range. |
| `env-report` | read | `report` | Dump OS, shell, .NET, git, node, and python versions. |

**Utility** — the odd jobs that interrupt real work.

| id | M? | Commands | Purpose |
|---|---|---|---|
| `json-validate` | read | `validate` | Validate JSON files and point at the first error. |
| `yaml-lint` | read | `lint` | Lint YAML files for structure and common mistakes. |
| `url-check` | read | `check` | Check that links in Markdown resolve. |
| `spellcheck-docs` | read | `check` | Spellcheck Markdown against a project word list. |

Two of these deserve a fuller sketch, because they set the pattern the rest follow. `git-status-vs-head` is the read-only exemplar: its `summarize` command fetches `origin`, resolves the remote default branch, and prints staged, unstaged, untracked, and ahead/behind counts as a compact block; `files-changed` is a one-line `git diff --name-only` against the remote base; `ahead-behind` reports the two-number divergence. Nothing writes, so it is `mutating: false` and runs without the lease. `git-commit` is the guarded-mutator exemplar: its `commit` command first checks `git rev-parse --abbrev-ref HEAD` against the repository's default branch and aborts with a clear message if they match, then stages and commits with a message assembled from the caller's arguments in conventional form. It is `mutating: true`, so it serializes through the write lease and passes the approval policy exactly like `run_process`. Every other default is a variation on one of those two shapes.

### 10.2 What "done" means for a default skill

A default skill is not finished when its script runs once on the author's machine. Each one has to clear the same bar before it ships, and the checklist below is the definition of done that the smoke suite and review enforce.

- `SKILL.md` carries complete frontmatter: `name` matching the folder id, `title`, a one- or two-sentence `description`, `version`, an explicit `mutating`, a `whenToUse` that would actually help a model choose it, `tags`, and a `commands` list where every command resolves to a real block or script and a whitelisted interpreter.
- The body reads like documentation, not a stub: what the skill does, how to read its output, and any prerequisite (for example, `git-open-pr` needs `gh` authenticated).
- Guardrails match the mutation posture. Read-only skills touch nothing. Mutating skills refuse to run against the default branch without an explicit branch and never force anything without a lease.
- Output is structured enough for the model to act on — a labeled block or JSON — rather than raw noise.
- The skill passes `SkillLoader.Validate` with zero errors, and it appears in the defaults smoke suite; read-only defaults additionally run inside a throwaway Git repository in CI.
- Cross-platform behavior is settled per Section 10.3, and the skill is proven on every platform it claims to support.

### 10.3 Cross-platform parity

mux runs on Windows, Linux, and macOS, and a git skill written only in `bash` strands Windows users who lack a POSIX shell. Two rules keep the defaults honest. Skills whose logic is plain command orchestration default to `pwsh`, because PowerShell 7 runs on all three platforms and the .NET audience usually has it. Skills that are genuinely cleaner as a POSIX pipeline declare `bash` and record the prerequisite in `whenToUse`, and the test matrix — already running Windows and Linux — proves each one where it claims to run. Per-platform command variants (a `pwsh` and a `bash` command for the same job, selected by availability) are noted as a fast follow rather than a launch blocker, so the first release ships a coherent, tested set rather than a partially-doubled one.

### 10.4 Action items for authoring the library

The library is built in waves so review stays small and each wave is independently shippable. Every skill in a wave follows the same recipe — author `SKILL.md`, write the block or bundled script, add a body that explains it, run `SkillLoader.Validate`, register it in the defaults smoke suite, and (for read-only skills) add a throwaway-repo run — so the action items below name the skill and the reviewer supplies the recipe.

**Wave 1 — Git core (the motivating set).**

- [ ] Author `git-status-vs-head` with `summarize`/`files-changed`/`ahead-behind`; use it as the read-only reference skill and worked example in `SKILLS_AUTHORING.md`.
- [ ] Author `git-commit` and `git-push` with default-branch and force-with-lease guardrails; use `git-commit` as the mutating reference skill.
- [ ] Author `git-branch`, `git-sync`, and `git-undo-last-commit`.
- [ ] Author `git-secret-scan` and `git-large-files` (read-only safety net).
- [ ] Smoke-test the whole wave in a throwaway repository on Windows and Linux CI.

**Wave 2 — Git extended and GitHub.**

- [ ] Author `git-open-pr` and document the `gh`-authenticated prerequisite.
- [ ] Author `git-changelog-entry`, `git-release`, `git-cherry-pick`, `git-stash-manager`, `git-conflict-explainer`, and `git-blame-summary`.
- [ ] Confirm `git-release` and `git-changelog-entry` target the Keep-a-Changelog format this repository uses.

**Wave 3 — .NET build and quality.**

- [ ] Author `dotnet-build`, `dotnet-test`, `dotnet-format`, and `dotnet-restore`.
- [ ] Author `dotnet-outdated`, `dotnet-pack`, and `dotnet-publish`.
- [ ] Author `ci-repro` so it mirrors the repository's own CI matrix (both frameworks, all adapters) and dogfood it against mux.

**Wave 4 — Hygiene.**

- [ ] Author `todo-scan`, `license-header-check`, `gitignore-audit`, `large-file-scan`, and `line-ending-check`.
- [ ] Author `readme-audit` and `dead-code-scan` (best-effort heuristics, clearly labeled).
- [ ] Author `codestyle-audit` and validate it against `Mux.Core` itself so the meta-skill is proven on real code.

**Wave 5 — Scaffolding.**

- [ ] Author `new-class`, `new-tool`, `new-touchstone-suite`, and `new-skill`, each emitting output that already passes the code style and — for `new-touchstone-suite` — registers in `MuxSuites`.

**Wave 6 — Documentation, workflow, and utility.**

- [ ] Author `doc-sync`, `api-surface-diff`, and `adr-new`.
- [ ] Author `standup-summary`, `release-notes`, `pr-description`, and `env-report`.
- [ ] Author `json-validate`, `yaml-lint`, `url-check`, and `spellcheck-docs`; note any optional tool prerequisites in `whenToUse`.

**Wave 7 — Seed and prove the set.**

- [ ] Place all skills under `src/` as content and wire `EnsureConfigDirectory` to copy them into `~/.mux/skills/` on first run without clobbering user edits on subsequent runs.
- [ ] Extend the defaults smoke suite to assert every skill validates, and add the `mux skill validate` step to the repository's CI.
- [ ] Add the gallery-install path (Section 9.3) so a user who deletes a default can reinstall it, and cover it in the import suite.

---

## 11. Command-line surface

Deterministic execution earns its keep outside the REPL, so skills get a verb dispatched from `Program.Dispatch` beside `endpoint`, `print`, and `probe`. `SkillCommand : AsyncCommand<SkillSettings>` follows the `EndpointCommand` shape, including `--config-dir` and `--output-format` for JSON that scripts can parse.

```
mux skill list                       # all skills with validity and enablement
mux skill list --output-format json
mux skill show <name>                # metadata, commands, and body
mux skill validate [<name>]          # validate one skill or the library; nonzero exit on failure
mux skill run <name> <command> [--arg ...] [--cwd <dir>]   # execute deterministically
mux skill new <name>                 # scaffold a skill directory
mux skill add <path|git-url>         # import from a local path or repository
```

`mux skill run` is the piece automation reaches for: a CI job or a Git hook invokes a curated, versioned procedure and gets the same `stdout`/`stderr`/`exit_code` contract the agent sees, with no model in the loop. `mux skill validate` belongs in the project's own CI so a malformed default can never ship.

---

## 12. Ridiculously tested

Skills touch parsing, process execution, prompt assembly, a multi-step TUI flow, and a CLI verb, and every layer gets exhaustive Touchstone coverage registered in `MuxSuites.All`, run under all three adapters and both target frameworks. Config-isolating suites use `SettingsLoader.PushConfigDirectoryOverride` rather than the `MUX_CONFIG_DIR` environment variable, following the isolation fix already made for the interactive suites — a fire-and-forget modal continuation must never write another test's config. The bar is deliberately higher than "it works": the intent is that a regression in any corner of the feature turns a suite red.

**Parsing and validation.** `SkillLoaderSuite` drives a wide matrix — a well-formed skill; a missing `SKILL.md`; malformed YAML; a bad interpreter; a command with both `run` and `block`; a command with neither; a script path that escapes the directory; an id containing `..` or a separator; a duplicate command name; an `id=` block reference with no matching block; a body with no frontmatter. Frontmatter parsing gets a focused fuzz pass that feeds randomized-but-bounded YAML and asserts the loader either parses or reports a clean validation error, never throwing out of the parse boundary.

**Execution.** `SkillExecutorSuite` runs an inline block and a bundled script in a temp skill directory, asserts the `run_process`-shaped output, and exercises the failure surface: a nonzero exit, a timeout that kills the process tree, a cancellation mid-run, output that exceeds the truncation limit, and a missing interpreter that degrades to a clean error rather than a crash. Mutation classification is asserted both ways — a mutating skill serializes through the write lease, a read-only skill does not. Interpreter resolution runs on Windows and Linux in CI so the `cmd.exe` and `/bin/sh` paths are both covered rather than one being assumed.

**Composition.** `ExternalToolsBinderSuite` proves MCP and skills coexist: both tool sets appear in `AdditionalTools`, both prompt sections appear in order, `EffectiveToolCount` adds up, and a tool call routes to the correct provider. A provider-routing suite asserts that an unknown tool falls through to the legacy executor and then to a clean `unknown_tool` result.

**Lifecycle and concurrency.** `SkillRuntimeSuite` mirrors `McpRuntimeSuite` for start, first-refresh completion, periodic revalidation, `RequestRefresh`, and idempotent dispose. A concurrency suite hammers the immutable-swap: it runs `ExecuteToolAsync` in a tight loop while forcing refreshes, and asserts no call ever observes a half-loaded catalog and no exception escapes.

**The interactive surface.** `SkillManagementSuite` drives the `/skills` inventory through filter, view, enable, disable, duplicate, and remove. A dedicated `SkillWizardSuite` walks the create wizard step by step — including a live id-collision rejection, a cancel at each step that leaves the disk untouched, and a completed run that writes a valid, discoverable skill. An import suite covers local-path import with a colliding id, a multi-select repository import, and a gallery install, each asserting that an invalid source is refused. Where a rendered frame matters, the suites assert on the headless backend output the way the existing frame-snapshot suites do.

**The command line.** `SkillCommandSuite` covers `list`, `show`, `validate`, `run`, `new`, and `add`, asserts the JSON output shape for `--output-format json`, and pins exit codes — zero on success, nonzero when validation fails — because a CI gate depends on them.

**Security.** A security suite treats a skill as untrusted input even though it is user-authored: it confirms path traversal is rejected at load, that arguments reach scripts as argv rather than an interpolated string, that the interpreter allowlist holds, and that a skill cannot reach outside its own directory for scripts or resources.

**The defaults.** A smoke suite asserts every seeded skill parses and validates, and runs a representative set of the read-only ones inside a throwaway Git repository so the shipped library is proven, not assumed. The project's own CI gains a `mux skill validate` step against the seed library, so a broken default fails the build instead of a user's first run. Coverage on the new `Mux.Core` skill types is expected to be thorough enough that the untested branches are the ones that genuinely cannot be reached.

---

## 13. Documentation deliverables

A feature that asks users to author files needs documentation they can trust, and the code style's README-accuracy rule makes that a gate rather than a nicety. **README.md** gains a *Skills* section between the MCP material and *Configuration*, a Highlights bullet, a command-list entry for `/skills`, and a screenshot of the inventory and the create wizard. **CONFIG.md** documents the `skills/` directory, `skills.json`, and the new `settings.json` fields. **USAGE.md** adds a *Skills* section covering authoring, importing, using, and the `mux skill` verb, beside the *MCP Tool Servers* section. **GETTING_STARTED.md** gains a short "create your first skill" walkthrough that uses the wizard. **CHANGELOG.md** records the work under the v0.4.0 heading: an *Added* entry for skills, the guided authoring surface, and the `mux skill` verb, and a *Changed* entry for the external-tool provider refactor. **SKILLS_AUTHORING.md** is new — the full `SKILL.md` reference with every frontmatter field, the command forms, block-versus-script execution, the interpreter allowlist, resources, the executor's environment variables, the mutation and approval model, and three complete worked examples spanning a read-only reporter, a guarded mutator, and a bundled-script skill. The `DOCKERHUB_README.md` that `REPOSITORY_REQUIREMENTS.md` asks for stays a tracked conditional: mux ships as a CLI rather than a container today, so a skills mention is added only if and when a Docker Hub image is published, and it is noted here so the requirement stays visible rather than forgotten.

---

## 14. Rollout in phases (all within v0.4.0)

The work sequences so each phase is independently reviewable and leaves the branch green, and the acceptance criteria are concrete because "done" should not be a matter of taste.

**Phase 0 — Provider seam.** Introduce `IExternalToolProvider`, add `ExternalToolProviders` to `AgentLoopOptions`, teach `AgentLoop` to aggregate and route, and adapt MCP onto the seam without behavior change. *Accept when* the full suite passes with MCP running through a provider and the legacy path intact.

**Phase 1 — Core model and loader.** Add the model types, `SkillLoader`, validation, and `YamlDotNet`. *Accept when* `SkillLoaderSuite` and the frontmatter fuzz pass across the good-and-broken matrix.

**Phase 2 — Execution and tools.** Add `SkillExecutor`, `SkillCatalog`, `SkillInterpreterResolver`, and `SkillToolProvider` with the two tools. *Accept when* the executor, provider, and cross-platform interpreter suites pass.

**Phase 3 — Interactive wiring.** Add `SkillRuntime`, refactor `McpTemplateBinder` into `ExternalToolsBinder`, wire both runtimes in `Program.RunInteractive`. *Accept when* the binder, runtime, and concurrency suites pass and a manual session shows the model listing and running a seeded skill.

**Phase 4 — Authoring and management surface.** Add the `mux.skills` command, the inventory, the create wizard, the import flows, the settings fields, and the sidebar count. *Accept when* `SkillManagementSuite`, `SkillWizardSuite`, and the import suite pass and the `/skills` flow round-trips create, import, toggle, view, duplicate, and remove.

**Phase 5 — CLI verb.** Add `SkillCommand` and dispatch. *Accept when* `SkillCommandSuite` passes and `mux skill run` returns the process contract non-interactively.

**Phase 6 — Default library.** Author and seed the skills, wire `EnsureConfigDirectory`, add the CI validation step. *Accept when* every default validates and the smoke suite runs the read-only samples.

**Phase 7 — Documentation and review.** Ship the docs, run the README accuracy pass, and complete a security review of execution and path handling. *Accept when* the docs match behavior and the security suite passes.

---

## 15. Decisions still open

A handful of choices deserve the owner's call before Phase 0. Whether `run_skill` should be callable in `print`/non-interactive mode, given MCP is interactive-only today, trades reach against a wider approval surface. Whether a shared, version-controlled `SkillsDirectory` outside `~/.mux/` is a launch feature or a follow-up decides how much weight `MuxSettings.SkillsDirectory` carries at first. Whether skills should carry a checksum or signature for provenance matters more the moment skills move between machines, which the Git import in Section 9.3 makes easy. And `YamlDotNet`, small and standard as it is, would be the first YAML dependency in a JSON-first codebase and should be an explicit yes.

---

## 16. Compliance appendix

**`CODE_STYLE.md`, to the letter.** Every new file declares its namespace first with usings inside it, system usings alphabetized ahead of the rest. Public members, constructors, and methods carry XML documentation; private members and methods carry none. Validated properties use backing fields named with a leading underscore and Pascal case (`_SkillsDirectory`, `_RefreshIntervalSeconds`). No tuples appear in any signature — status and results are named types (`SkillStatus`, `SkillValidationResult`, `ToolResult`, `SkillWizardState`). Async methods take a `CancellationToken` unless the type already holds one, use `ConfigureAwait(false)`, and check cancellation at loop and I/O boundaries. Types are never inferred with `var`. Each file holds exactly one class or enum. Enumerable-returning methods (`SkillLoader.Discover`) ship an async counterpart (`DiscoverAsync`). Configurable numbers — the refresh interval, command timeouts — are properties with documented defaults, minimums, and meaning, not literals. Public methods document their throwing exceptions with `/// <exception>` tags and throw specific types (`ArgumentException`, `InvalidOperationException`, `FileNotFoundException`) with contextual messages. `SkillRuntime` implements the full dispose pattern with `protected virtual void Dispose(bool)` and, where a base type disposes, calls `base.Dispose()`. Projects keep `<Nullable>enable</Nullable>`, and guard clauses validate inputs at entry with `ArgumentNullException.ThrowIfNull`. LINQ is used where it reads more clearly than a loop, `.Any()` stands in for `.Count() > 0`, and enumerations that would be walked twice are materialized once. No library file writes to the console — only `Mux.Cli` prints. Regions appear only in files over 500 lines. The README accuracy pass is Phase 7's gate, and the build is expected warning-clean.

**`REPOSITORY_REQUIREMENTS.md`.** All new code and content lands under `src/` and `test/`. README, CHANGELOG, and the MIT LICENSE already exist and are updated, not replaced. The Docker Hub README stays a tracked conditional (Section 13). Versioning is pervasive: every `csproj`, `Defaults.ProductVersion`, the README badge, and the changelog carry 0.4.0.

**`WRITING_DOCUMENTS.md`.** The author-facing prose in this plan and in the forthcoming `SKILLS_AUTHORING.md` is written to be read rather than skimmed off a template — sections carry real paragraphs instead of a lead-in and a list, lists are reserved for genuinely parallel items, paragraph openings name their subject rather than leaning on "This…" or "These…", and the voice states preferences plainly.

Skills give mux a memory for procedure, and the guided surface in Section 9 is what keeps that memory from being a chore to build. Once the seam in Section 4 exists, the rest is additive — a folder, two tools, a runtime, a wizard, a verb, and a library — and the first skill worth shipping is the one that started all of this: point mux at a repository, ask how far the working tree has drifted from GitHub, and get the same honest answer every time.
