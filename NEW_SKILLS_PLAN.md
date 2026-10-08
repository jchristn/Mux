# New Default Skills and OOBE Parity Plan

_Status: Phase 1 done (2026-10-08); Phases 2 through 7 proposed. Check boxes as work lands. `[ ]` = todo, `[x]` = done, `[~]` = in progress._

mux ships 46 default skills, and nearly all of them assume a git repository, a .NET solution, or both. Someone who opens mux in a React app, a Django service, a Maven project, or a CMake tree gets git helpers and nothing that knows how their code builds or tests. That gap is the first thing a Claude Code or Codex user notices, and it is the reason for this plan. The second thing they notice is subtler: both of those harnesses read a project instruction file on startup, can review a diff on request, and let skills be invoked by name with arguments. mux has a strong engine (subagents, MCP, sandboxing, undo, compaction, sessions across four surfaces) but the out-of-box experience still feels like a toolkit rather than an agent that already knows the job.

The scorecard below lists everything that exists in Claude Code or Codex and is missing (or only partly present) in mux, including the six skill families requested by name: JavaScript, Python, React, Java, C++, code review, and loops. Scores are subjective. **Simplicity** is how cheaply the item lands in the current codebase (10 = an afternoon of data entry, 1 = a new subsystem). **Value** is how much it moves the out-of-box experience toward parity (10 = users hit the gap in their first session). **Total** is the sum, and the table is sorted by it.

## Scorecard

| # | Capability | Claude Code | Codex | mux today | Simplicity | Value | Total | Phase |
|---:|---|:---:|:---:|---|---:|---:|---:|:---:|
| 1 | Project instruction file loaded into the system prompt (`AGENTS.md`, `CLAUDE.md`, hierarchical) | yes (`CLAUDE.md`) | yes (`AGENTS.md`) | **done in Phase 1**: `MUX.md` / `AGENTS.md` / `CLAUDE.md` on every surface | 9 | 10 | **19** | 1 |
| 2 | Instruction-only ("playbook") default skills: prose procedures with no mandatory commands | yes | yes | **done in Phase 1**: `DefaultSkillDef` builds playbooks; the playbooks themselves land in Phase 3 | 9 | 8 | **17** | 1 |
| 3 | Code review on demand (uncommitted, vs branch, a commit, a PR) | yes (`/code-review`) | yes (`/review`) | VS Code "review file" only | 8 | 9 | **17** | 3 |
| 4 | `/init`: survey the repo and write an instruction file | yes | yes | none | 9 | 8 | **17** | 3 |
| 5 | JavaScript / TypeScript toolchain skills | via model + shell | via model + shell | none | 9 | 8 | **17** | 2 |
| 6 | Python toolchain skills | via model + shell | via model + shell | none | 9 | 8 | **17** | 2 |
| 7 | Invoke a skill by name with arguments (`/code-review main`, `$ARGUMENTS`) | yes | yes (custom prompts) | **done in Phase 1**: terminal, desktop, dashboard, and `mux print` | 7 | 9 | **16** | 1 |
| 8 | Project-scoped skills (checked into the repo) and Claude-format skill import | yes (`.claude/skills`) | yes (`.agents/skills`) | **done in Phase 1**, with a per-project trust gate | 8 | 8 | **16** | 1 |
| 9 | Security review of pending changes | yes (`/security-review`) | via `/review` | `git-secret-scan` only (regex on staged diff) | 9 | 7 | **16** | 3 |
| 10 | Iterate-until-green loops (retry a check, fix-build-test cycle, CI watch) | via model | via model | none | 8 | 8 | **16** | 4 |
| 11 | Project detection (languages, package managers, build and test commands) | implicit | implicit | none | 9 | 7 | **16** | 2 |
| 12 | Simplify / cleanup pass on changed code | yes (`/simplify`) | no | none | 9 | 6 | **15** | 3 |
| 13 | PR review comments fetched for the agent to address | yes (`/pr-comments`) | no | `git-open-pr status` only | 9 | 6 | **15** | 3 |
| 14 | React skills | via model + shell | via model + shell | none | 8 | 7 | **15** | 2 |
| 15 | Java skills (Maven and Gradle) | via model + shell | via model + shell | none | 8 | 7 | **15** | 2 |
| 16 | Relevance-gated skill listing so 100+ skills do not flood a small context window | progressive disclosure | progressive disclosure | **done in Phase 1**: `appliesTo` globs and `skillListingMode` | 7 | 7 | **14** | 1 |
| 17 | Tool-level hooks (`pre-tool-use`, `post-tool-use`, `stop`) | yes | partial (`notify`) | `session-start`, `user-prompt-submit`, `session-end` only | 6 | 8 | **14** | 5 |
| 18 | Background processes (start a dev server, read its output later, stop it) | yes | partial | `run_process` is foreground with a timeout | 5 | 9 | **14** | 5 |
| 19 | `@file` mentions in the composer | yes | yes | none | 7 | 7 | **14** | 6 |
| 20 | Debugging playbook and `git bisect` driver | via model | via model | none | 9 | 5 | **14** | 3 |
| 21 | Go, Rust, and container skills | via model + shell | via model + shell | none | 9 | 5 | **14** | 2 |
| 22 | C++ skills (CMake, CTest, clang-format, clang-tidy, sanitizers) | via model + shell | via model + shell | none | 7 | 6 | **13** | 2 |
| 23 | `/loop`: re-run a prompt on an interval or self-paced | yes | no | none | 6 | 7 | **13** | 4 |
| 24 | Persistent memory (agent-written facts reused across sessions, `#` quick-add) | yes | partial | none | 6 | 7 | **13** | 6 |
| 25 | Plan mode (read-only exploration, then an approved plan, then execution) | yes | partial (approval modes) | `--sandbox read-only` covers the read-only half | 6 | 7 | **13** | 6 |
| 26 | Structured "ask the user" tool (multiple choice mid-turn) | yes | no | none | 6 | 6 | **12** | 6 |
| 27 | Image input (paste or attach a screenshot) | yes | yes | none | 4 | 7 | **11** | 7 |
| 28 | Git worktree isolation for subagents and parallel jobs | yes | yes (cloud) | single working tree plus write lease | 5 | 6 | **11** | 7 |
| 29 | Expose mux as an MCP server | yes (`claude mcp serve`) | yes (`codex mcp-server`) | REST + WebSocket only | 5 | 6 | **11** | 7 |
| 30 | Custom status line | yes | no | fixed sidebar | 7 | 4 | **11** | 7 |
| 31 | Output styles | yes | no | prompt profiles cover most of it | 8 | 3 | **11** | backlog |
| 32 | Scheduled cloud or cron runs | yes (routines) | yes (cloud tasks) | none | 4 | 5 | **9** | backlog |
| 33 | Jupyter notebook editing | yes | no | none | 6 | 3 | **9** | backlog |

A few things that look like gaps are not, and they are deliberately absent from the table: sandbox postures, tool allow and deny globs, compaction, resume and fork, subagents, web search and retrieval, undo and redo, task plans, custom slash commands, MCP client support, and usage tracking all exist today.

Rows 1, 2, 7, 8, and 16 are harness changes, not skills, but they come first because the new skills depend on them. Most rows from 3 through 15 lean on playbook skills (2), project scoping (8), or name invocation (7). Without them, a React developer would still have to type "please run the react-test skill" instead of `/react-test`, and 105 skills would be listed in every system prompt sent to a 7B model with an 8K window.

---

## Guiding decisions

**Two kinds of default skill.** A *command skill* is what mux has today: deterministic code behind `run_skill`. A *playbook skill* is a body of procedure that the model reads through the `skill` tool and follows using its normal tools. Claude Code's review, simplify, and init skills are playbooks, and that is the right shape for judgment work. Most new skills here are hybrids: a few read-only commands gather facts deterministically (the diff, the detected test runner), and the body tells the model what to do with them. `DefaultSkillBuilder` grows a playbook path; it does not get a second builder class for every shape.

**Detection lives in the skill, not in the model.** Every toolchain skill starts its `pwsh` block by detecting the tool from files on disk: lockfiles pick npm, pnpm, yarn, or bun; `mvnw` beats `mvn`; `uv.lock` beats `poetry.lock` beats `requirements.txt`. The model calls `js-test all` and the skill decides between `pnpm test` and `npx vitest run`. That keeps small local models from guessing wrong, which is the whole argument for skills in mux.

**`pwsh` stays the default interpreter.** It runs on all three platforms and the existing library already depends on it. Python skills invoke Python tools from `pwsh` instead of declaring `interpreter: python`, so a machine without a global Python still gets a clear "python not found" message from the skill instead of an interpreter resolution failure.

**Missing tools fail politely.** A skill that cannot find its tool prints one line naming the tool and how to install it, then exits with code 2. Exit 1 stays reserved for "the tool ran and found problems," so the model can tell "your tests failed" apart from "you have no test runner."

**No version bump.** Per `VERSIONING.md`, nothing in this plan changes a version number. CHANGELOG entries go under an `Unreleased` heading, and the owner picks the release number.

**Requirements compliance is a design input.** Every new C# file follows `~/Code/Agents/requirements/CODE_STYLE.md`: one class per file, usings inside the namespace, no `var`, XML docs on public members only, `_PascalCase` private fields, guard clauses, specific exception types, and `ConfigureAwait(false)` plus a `CancellationToken` on async paths. No em-dashes anywhere, including skill bodies, titles, and command descriptions. The current `DefaultSkillBuilder` emits a spaced em-dash (U+2014) between a command name and its description; Phase 1 replaces it with `": "`, which changes the seeded text of all 46 existing skills (users with seeded copies keep theirs, since seeding never overwrites).

---

## Phase 1: Harness prerequisites (done)

These changes make playbooks, project scoping, name invocation, and a large library practical. Nothing in later phases ships without them. All five parts are implemented, documented, and tested; the full solution builds with no errors or warnings, and the suite passes on net8.0 and net10.0 under all three runners (Touchstone 1005 cases with 998 passed and 7 skipped by design; xUnit and NUnit 999 each, all passing).

Where the shipped code differs from what this section originally proposed, the bullet says so. The differences are deliberate and each one is called out with its reason.

### 1.1 Project instruction files (row 1)

Claude Code reads `CLAUDE.md`; Codex reads `AGENTS.md`. mux now reads both, so a repository already set up for either agent works unchanged, and it prefers its own `MUX.md` when present. Prompt assembly stays in one place, `SystemPromptResolver`, because three surfaces call it (`Mux.Cli/Commands/CommandRuntimeResolver.cs`, `Mux.Desktop.Core/Conversation/AgentLoopTurnRunner.cs`, `Mux.Server/Routes/ChatRoutes.cs`).

- [x] `src/Mux.Core/Prompting/ProjectInstructionsLoader.cs`: walks from the working directory up to the repository root, taking the first of `MUX.md`, `AGENTS.md`, `CLAUDE.md` in each directory, outer files first. Reads `~/.mux/MUX.md` (the config directory's `MUX.md`) as a user-level file ahead of all project files. **Changed:** outside a repository it reads only the working directory instead of walking to the filesystem root, which matches Codex and avoids picking up a stray `AGENTS.md` in a home or temp directory. The repository root is found by `src/Mux.Core/Utility/RepositoryRootLocator.cs`, which also treats a `.git` *file* (worktrees, submodules) as a root.
- [x] `src/Mux.Core/Prompting/ProjectInstructions.cs`: the result (sources in prompt order, dropped sources, combined text, total bytes, truncated flag).
- [x] Settings `projectInstructionsEnabled` (default `true`) and `projectInstructionsMaxBytes` (default `32768`, clamped `0` to `1048576`, `0` disables). Over the cap, the outermost files are dropped first; a single file larger than the cap is cut short with a note.
- [x] `section.projectInstructions` in `PromptCatalog`, editable like every other prompt.
- [x] `SystemPromptResolver.Resolve` takes an optional `ProjectInstructions?` and appends the section. **Changed:** the section goes after placeholder substitution and before `--append-system-prompt` text, not "after the skills section", because the skills section is appended later by `ExternalToolsBinder` on the interactive surfaces. `CommandRuntimeResolver.ResolveProfilePrompts` applies the same section so profile switches and `/cwd` keep it.
- [x] Surfacing the loaded files. **Changed:** the terminal posts a notice at startup and after `/cwd` instead of a splash line (the splash is skipped when a prompt is passed, and `/cwd` needs the same report); the desktop app gets an `/instructions` command, also run after `/cwd`, instead of a context-panel entry; the server exposes `GET /v1.0/api/context/instructions` as planned.
- [x] `--no-project-instructions` flag, honored by both the interactive shell and `mux print`.
- [x] Tests: `ProjectInstructionsSuite` (precedence within a directory, `CLAUDE.md`-only repositories, outer-to-inner ordering with a repo-root stop, no-repo behavior, `.git` files, the user file, cap drop and cut, disabled settings and clamping, async parity, and the resolver section's position before appended text). Symlink loops cannot occur because the walk only ever moves to a parent directory.

### 1.2 Playbook skills in the default builder (row 2)

- [x] `src/Mux.Core/Skills/DefaultSkillDef.cs`: one default skill as data (id, title, description, mutating, tags, whenToUse, appliesTo, argumentHint, body, commands).
- [x] `DefaultSkillBuilder.Build(DefaultSkillDef)`: accepts an empty command list when `Body` is set, writes the body before the command blocks, and emits `appliesTo` and `argumentHint`. The positional overload is kept as a thin wrapper, so the five existing category files are unchanged.
- [x] The command heading uses a colon instead of an em-dash.
- [x] Tests in `DefaultSkillsSuite`: no default contains U+2014, a playbook definition round-trips through the loader (including `appliesTo` and `argumentHint`), empty definitions are rejected, and headings use a colon. Every default still validates and ids stay unique through `Merge`. **Deferred to Phase 3:** the "playbook body is at least 400 characters" check, since no default playbook exists until then.

### 1.3 Project-scoped skills and Claude-format compatibility (row 8)

- [x] `SkillRuntime` merges project skills from `<root>/.mux/skills`, `<root>/.claude/skills`, and `<root>/.agents/skills` over the user library, where the root is the repository root (or the working directory outside one). A project skill shadows a user skill with the same id. Because the desktop app and the REST server share one runtime across conversations in different directories, the runtime keeps a cached view per project root (`src/Mux.Core/Skills/SkillCatalogView.cs`), refreshed on the same timer as the user library, and resolves the view from the working directory passed to each tool call. The user library is never re-read as a project root, which matters when mux runs from the home directory.
- [x] Settings `projectSkillsEnabled` (default `true`) and `projectSkillRoots` (default the three paths above; rooted and `..` entries are dropped).
- [x] `SkillFrontmatterParser` matches keys regardless of case, hyphens, and underscores, so `allowed-tools`, `argument-hint`, `user-invocable`, and `disable-model-invocation` map onto mux fields. It also reads comma-separated scalars as lists and folds YAML block scalars (`description: >`), which Claude-format skills use often. Unknown fields become validation warnings (new `SkillValidationResult.Warnings`), never errors.
- [x] Trust gate in `src/Mux.Core/Skills/ProjectTrustStore.cs`, stored per repository root in `~/.mux/trusted-projects.json` with levels `all`, `playbooks`, `ignore`, or no decision. Project skills with commands load only at `all`; playbooks load unless the project is ignored. **Changed:** there is no blocking first-run modal. The terminal posts a notice when a project ships blocked skills and offers `/trust all|playbooks|ignore|reset`, and the `/skills` inventory has a **Trust this project's skills** action. The desktop app has the same `/trust` command, and `mux skill trust [level] [--cwd dir]` records a decision headlessly. `--trust-project-skills` trusts the project for one run (interactive or print) without writing anything.
- [x] `/skills` inventory: project skills are listed read-only after the user library, with blocked, shadowing, and slash-collision notes. **Changed:** there is no separate `default` scope, because seeded defaults are ordinary user skills once written.
- [x] Tests: `ProjectSkillsSuite` (playbooks load untrusted, command skills blocked until trusted and runnable after, playbooks-only and ignore, the per-run flag not persisting, shadowing without mutating the user skill, disabled project skills, custom roots, the home-directory case, a Claude-format skill, trust store round-trip and parsing, and the reserved `list` name), plus a `SkillCommand` case for `mux skill trust`.

### 1.4 Invoke a skill by name (row 7)

- [x] `src/Mux.Core/Skills/SkillInvocationExpander.cs` (with `SkillInvocation.cs`): turns `/<skill> args` into the message to submit. The body is substituted for every kind of skill, so the model always gets the procedure without an extra tool call; a skill with commands also gets a closing `run_skill` hint. `$ARGUMENTS` and `$1`..`$9` are substituted in prose only, never inside fenced code, so a bash block's own `$1` survives. Without a placeholder, the arguments are appended on their own line.
- [x] Frontmatter `userInvocable` (default `true`) and `argumentHint`, plus `modelInvocable` for Claude's `disable-model-invocation`.
- [x] Terminal: `RouteSlash` tries skills only after built-in and `hooks.json` commands decline the input, so built-ins win, then custom commands, then skills. `/skills` flags a skill whose name a command already owns. **Deferred:** the terminal has no slash-completion system today, so completion with argument hints waits for one; until then `/skills` shows each skill's invocation.
- [x] `mux print "/code-review main"` expands the same way. **Added beyond the plan:** `mux print` previously had no skills at all; it now discovers them, lists them, and exposes `skill` and `run_skill`. The desktop app expands `/name` in its slash handler, and the web dashboard calls `POST /v1.0/api/skills/expand` for any `/name` that is not a dashboard command. **Deferred:** listing skills inside the desktop `Ctrl+K` palette.
- [x] Tests: `SkillInvocationSuite` (parsing, prose-only substitution, positional and missing arguments, the appended-arguments fallback, the `run_skill` hint, quote-aware splitting, and a terminal case showing that `/name args` submits the expanded body while a built-in with the same name wins).

### 1.5 Relevance-gated listing (row 16)

The full library after this plan is 105 skills. Listed one line each, that is roughly 4,000 tokens on every turn, which is half the context of the local models mux is built to support.

- [x] Frontmatter `appliesTo`, evaluated by `src/Mux.Core/Skills/AppliesToMatcher.cs`: relative globs with `*`, `?`, and `**`, where rooted or `..` globs never match, dependency and build directories are skipped under `**`, and a visit budget bounds the search.
- [x] Relevance is evaluated once per skill per project view and cached until the view is rebuilt on the refresh interval.
- [x] Setting `skillListingMode`: `relevant` (default), `all`, or `none`. A call with no working directory skips filtering.
- [x] Footer from the editable `section.skills.more` prompt (`{Count} more skills are installed ...`), and `skill` with the name `list` returns every usable skill. `list` is now a reserved skill id.
- [x] Tests: `SkillListingSuite` (literal, wildcard, and `**` globs, skipped directories, rejected escapes, relevant mode with the footer, `all` and `none`, settings normalization, and the footer's required placeholder).

**Phase 1 acceptance:** met. A repository with an `AGENTS.md` and a `.claude/skills/foo/SKILL.md` playbook gets the instructions in its system prompt, and `/foo bar` runs the playbook with `$ARGUMENTS = bar`, in the terminal, `mux print`, desktop, and web, on net8.0 and net10.0. The terminal path is covered end to end by `SkillInvocation.ShellRunsSkillAndBuiltInsWin`. The print, desktop, and web paths share the same Core expander and are verified through it and the route build; they have no surface-level automated test yet.

---

## Phase 2: Language and toolchain skills

Each family is one category class in `src/Mux.Core/Skills/`, merged in `DefaultSkillLibrary.All()`. Every command skill below uses `pwsh`, detects its tool, sets `appliesTo`, and follows the exit-code convention from the guiding decisions. Commands that take a filter or path read it from `$args[0]`.

### 2.0 Project detection (row 11): `DefaultProjectSkills.cs`

`project-detect` is the anchor for everything else. `init`, `code-review`, and `fix-until-green` all call it first, so it ships first.

| Id | Mut. | Commands | Notes |
|---|:---:|---|---|
| `project-detect` | no | `summary`, `json` | Reports languages (by file counts), package managers, build systems, test frameworks, linters, formatters, CI providers, container files, and the exact build, test, lint, and format commands it would run. `json` emits the same as a single object for other skills. No `appliesTo`: always listed. |

### 2.1 JavaScript and TypeScript (row 5): `DefaultJavaScriptSkills.cs`

Package manager detection order: `bun.lockb`/`bun.lock` → bun, `pnpm-lock.yaml` → pnpm, `yarn.lock` → yarn, otherwise npm. Script names come from `package.json`; a skill prefers the project's own script (`test`, `lint`, `build`, `typecheck`, `format`) and falls back to the tool directly only when no script exists. `appliesTo: [package.json]`.

| Id | Mut. | Commands | Notes |
|---|:---:|---|---|
| `js-install` | yes | `install`, `ci` | `ci` uses the frozen-lockfile form for each manager. |
| `js-build` | yes | `build` | Runs the `build` script. |
| `js-test` | no | `all`, `filter`, `coverage` | Vitest, Jest, Mocha, or `node --test`, detected from devDependencies. Forces non-watch mode (`CI=1`, `--run`). |
| `js-lint` | yes | `check`, `fix` | ESLint or Biome. |
| `js-typecheck` | no | `check` | `tsc --noEmit -p .`; skips with exit 0 and a note when there is no `tsconfig.json`. |
| `js-format` | yes | `apply`, `verify` | Prettier or Biome. |
| `js-deps` | no | `outdated`, `audit`, `why` | `why <pkg>` explains why a package is installed. |
| `js-scripts` | yes | `list`, `run` | `run <name>` runs any `package.json` script with a timeout. Long-running scripts belong to Phase 5 background processes. |

### 2.2 Python (row 6): `DefaultPythonSkills.cs`

Environment detection order: `uv.lock` → uv, `poetry.lock` → poetry, `Pipfile.lock` → pipenv, otherwise a `.venv` (created on demand) with pip. Every command runs inside the detected environment, which removes the most common Python failure an agent hits: running the system interpreter. `appliesTo: [pyproject.toml, requirements*.txt, setup.py, setup.cfg, Pipfile]`.

| Id | Mut. | Commands | Notes |
|---|:---:|---|---|
| `py-env` | yes | `info`, `create` | `info` prints interpreter path, version, manager, and whether the venv is active. |
| `py-install` | yes | `install`, `add` | `add <pkg>` uses the manager's add verb so the lockfile updates. |
| `py-test` | no | `all`, `filter`, `coverage`, `last-failed` | pytest when importable, otherwise `unittest discover`. |
| `py-lint` | yes | `check`, `fix` | Ruff, falling back to flake8. |
| `py-format` | yes | `apply`, `verify` | `ruff format`, falling back to black. |
| `py-typecheck` | no | `check` | mypy or pyright, whichever is configured in `pyproject.toml`. |
| `py-deps` | no | `outdated`, `audit` | `audit` uses `pip-audit` when installed. |

### 2.3 React (row 14): `DefaultReactSkills.cs`

React skills sit on top of the JavaScript detection and only list when `package.json` depends on `react`. `appliesTo` cannot express a dependency check, so the skills use `appliesTo: [package.json]` and each command exits 2 with a note when React is absent. A dependency-aware `appliesTo` is a possible follow-up, not a prerequisite.

| Id | Mut. | Commands | Notes |
|---|:---:|---|---|
| `react-new-component` | yes | `create` | `create <Name> [dir]` writes a function component, a test beside it (matching the detected runner and Testing Library if present), and a CSS module or nothing, following the extension (`.tsx` vs `.jsx`) the repo already uses. Refuses to overwrite. |
| `react-new-hook` | yes | `create` | Same, for `use<Name>` hooks. |
| `react-test` | no | `component` | Runs tests matching one component's file name. |
| `react-build-analyze` | no | `report` | Builds, then lists the largest emitted JS and CSS chunks from `dist/` or `build/` with sizes. |
| `react-lint-hooks` | no | `check` | Runs ESLint restricted to `react-hooks/*` and `jsx-a11y/*` rules when those plugins are installed, reporting what it skipped otherwise. |
| `react-upgrade-check` | no | `report` | Versions of `react`, `react-dom`, `@types/react*`, and peer-dependency conflicts reported by the package manager. |

### 2.4 Java (row 15): `DefaultJavaSkills.cs`

Build tool detection order: `mvnw` → Maven wrapper, `gradlew` → Gradle wrapper, `pom.xml` → `mvn`, `build.gradle*` → `gradle`. Wrappers always win because they pin the version the project tested with. `appliesTo: [pom.xml, build.gradle, build.gradle.kts, mvnw, gradlew]`.

| Id | Mut. | Commands | Notes |
|---|:---:|---|---|
| `java-build` | yes | `compile`, `package`, `clean` | Maven `-q -B`, Gradle `--console=plain`. |
| `java-test` | no | `all`, `filter` | Maven `-Dtest=`, Gradle `--tests`. Prints Surefire or Gradle report paths on failure. |
| `java-format` | yes | `apply`, `verify` | Spotless when configured; otherwise exits 2 and suggests adding it. |
| `java-lint` | no | `check` | Checkstyle, SpotBugs, or PMD tasks when configured. |
| `java-deps` | no | `tree`, `updates` | `dependency:tree` / `dependencies`; `versions:display-dependency-updates` or the Gradle versions plugin. |
| `java-new-class` | yes | `create` | `create com.example.Foo [class\|interface\|record\|enum]` under `src/main/java`, with a matching test under `src/test/java` when JUnit is present. |

### 2.5 C++ (row 22): `DefaultCppSkills.cs`

CMake is the supported path, since it covers most modern C++ projects and `compile_commands.json` makes clang-tidy work. Presets win when `CMakePresets.json` exists. Plain Makefile projects get `cpp-build` only. `appliesTo: [CMakeLists.txt, CMakePresets.json, Makefile, meson.build]`.

| Id | Mut. | Commands | Notes |
|---|:---:|---|---|
| `cpp-configure` | yes | `debug`, `release`, `preset` | Configures into `build/<config>` with `CMAKE_EXPORT_COMPILE_COMMANDS=ON`. |
| `cpp-build` | yes | `build` | `cmake --build` with parallelism set to the processor count; falls back to `make -j` or `meson compile`. |
| `cpp-test` | no | `all`, `filter` | `ctest --output-on-failure`, `-R <regex>` for filter. |
| `cpp-format` | yes | `apply`, `verify` | clang-format over tracked C and C++ sources, honoring `.clang-format`. |
| `cpp-tidy` | no | `check` | clang-tidy against `compile_commands.json`, limited to changed files when given `changed`. |
| `cpp-sanitize` | yes | `asan`, `ubsan` | Configures a separate build tree with the sanitizer flags, builds, and runs CTest. |

### 2.6 Go, Rust, and containers (row 21)

Not requested by name, but Go and Rust are each common enough that their absence would read as a hole next to C++ and Java. Each skill is small because the toolchains are already uniform.

- [ ] `DefaultGoSkills.cs`, `appliesTo: [go.mod]`: `go-build` (`build`, `vet`), `go-test` (`all`, `filter`, `race`), `go-lint` (`check` with staticcheck or golangci-lint when installed), `go-mod` (`tidy`, `outdated`).
- [ ] `DefaultRustSkills.cs`, `appliesTo: [Cargo.toml]`: `cargo-build` (`debug`, `release`), `cargo-test` (`all`, `filter`), `cargo-clippy` (`check`, `fix`), `cargo-fmt` (`apply`, `verify`).
- [ ] `DefaultContainerSkills.cs`, `appliesTo: [Dockerfile, compose.yaml, docker-compose.yml]`: `docker-build` (`build`), `compose` (`up`, `down`, `ps`, `logs`), `dockerfile-lint` (`check` with hadolint when installed).

### Phase 2 tasks

- [ ] Add the eight category classes above, one class per file.
- [ ] Register them in `DefaultSkillLibrary.All()`.
- [ ] Fixture repositories under `src/Test.Shared/Fixtures/projects/` (`node-npm`, `node-pnpm`, `python-uv`, `python-pip`, `react-vite`, `java-maven`, `java-gradle`, `cmake`, `go`, `rust`): manifests and lockfiles only, no dependencies installed.
- [ ] `ToolchainDetectionSuite`: runs each skill's detection against each fixture with a `MUX_SKILL_DRY_RUN=1` environment variable, which every toolchain skill honors by printing the command it would run instead of running it. The suite asserts the printed command, so detection is tested on every platform without installing Node, Python, a JDK, or a compiler in CI.
- [ ] `ToolchainLiveSuite`: opt-in (skipped unless `MUX_TEST_LIVE_TOOLCHAINS=1`), runs the real commands against fixtures where the toolchain is installed.
- [ ] `appliesTo` cases: each fixture lists exactly its own family plus the ungated skills.

---

## Phase 3: Review and agent playbooks

These are the skills Claude Code and Codex users reach for by name. Each is a hybrid: read-only commands gather inputs deterministically, and the body holds the procedure. Bodies are written as procedures, not essays. They tell the model what to collect, what to check, how to rank findings, and the exact output format, because a small model follows a format far better than it follows advice.

`DefaultReviewSkills.cs`:

| Id | Mut. | Commands | Body (procedure) |
|---|:---:|---|---|
| `code-review` | no | `uncommitted`, `branch`, `commit`, `pr` | `branch [base]` diffs against the merge base with the origin default branch when no base is given; `pr <n>` uses `gh pr diff`. The body: read every changed hunk plus enough surrounding code to judge it, look for correctness bugs first (logic, null and bounds, error handling, concurrency, resource leaks, API contract breaks), then tests, then maintainability; verify each candidate finding by reading the code it depends on before reporting; report as a ranked list with `file:line`, severity, the concrete failure scenario, and a suggested fix; say "no findings" plainly when there are none. Effort argument `quick` or `deep` changes how far to read. `argumentHint: [uncommitted\|branch [base]\|commit <sha>\|pr <n>] [quick\|deep]`. |
| `security-review` | no | `uncommitted`, `branch`, `pr` | Same inputs, plus a call to `git-secret-scan`. Checklist: injection (SQL, command, path, template), authn and authz checks, secrets and key handling, deserialization, SSRF and open redirects, unsafe crypto, dependency changes with known advisories (calls `js-deps audit`, `py-deps audit`, or `dotnet-outdated vulnerable` when relevant). Findings require an attack path, not a pattern match. |
| `simplify` | yes | `changed-files` | Lists files changed versus the base. Body: look for duplicated logic, dead parameters, needless abstraction, reimplemented standard-library helpers, and inefficient loops in the changed code only; apply fixes in small edits; run the detected tests after; never change behavior. |
| `pr-comments` | no | `list` | `gh api` for review comments and review threads on the current branch's PR, unresolved first, with `file:line` and author. Body: group by file, propose a change for each, ask before resolving anything ambiguous. |
| `test-gap-review` | no | `report` | Changed source files with no corresponding test file change, using naming conventions per detected language. Body: for each gap, judge whether a test is warranted and draft it. |

`DefaultAgentPlaybookSkills.cs`:

| Id | Mut. | Commands | Body (procedure) |
|---|:---:|---|---|
| `init` | yes | `survey` | `survey` runs `project-detect json`, lists top-level directories, entry points, CI files, and existing instruction files. Body: write `AGENTS.md` at the repo root (or update it if present, preserving human-written sections) with build, test, lint, and run commands; layout; conventions inferred from the code; and anything a new contributor would trip on. Keep it under 200 lines. Show the diff before writing when the file exists. |
| `debug` | no | none | Playbook: reproduce first and capture the exact failure; form ranked hypotheses; test the cheapest one with a targeted read, log, or minimal script; narrow until one cause explains every symptom; fix; re-run the reproduction and the nearby tests. Never declare a fix without re-running the reproduction. |
| `git-bisect` | yes | `start`, `run`, `reset` | `start <good> [bad]`, then `run <command...>` drives `git bisect run` with the given test command, then reports the first bad commit with its message and diff stat. `reset` always restores the original HEAD. Refuses to start with a dirty working tree. |
| `explain-codebase` | no | `map` | `map` prints a depth-limited tree with file counts and the detected entry points. Body: give a layered explanation (what it does, how it is organized, how a request flows through it) citing real paths. |

- [ ] Add both category classes.
- [ ] Remove `review file` duplication in the VS Code extension by having its "review file" action call `/code-review` with the file path, so review behavior has one definition.
- [ ] `ReviewSkillsSuite`: against a fixture git repo built in a temp directory, each diff command returns the expected hunks for uncommitted, branch, and commit modes; `git-bisect` finds a planted bad commit and always resets.
- [ ] Prompt-quality check: run `/code-review` on three seeded-bug fixtures with the default local model and record whether the planted bug is found. Not a pass or fail gate; the numbers go into the PR description so body wording changes are measured, not guessed.

---

## Phase 4: Loops

"Loop" covers two different things, and both are in scope. The first is a skill-level loop: keep doing something until a condition holds, inside one turn. The second is a harness-level loop: re-submit a prompt on a schedule across many turns, which is what Claude Code's `/loop` does. The first is pure data and ships with this phase's skills. The second needs a scheduler in `Mux.Core/Jobs`.

### 4.1 Loop skills: `DefaultLoopSkills.cs`

| Id | Mut. | Commands | Notes |
|---|:---:|---|---|
| `loop-until` | yes | `run` | `run <maxAttempts> <intervalSeconds> <command...>` re-runs a command until it exits 0 or attempts run out, printing each attempt's exit code and the last attempt's output. Bounded: `maxAttempts` max 100, interval max 600. Command timeout is the skill's `timeoutMs`, which this skill sets to 30 minutes. |
| `fix-until-green` | yes | `check` | `check` runs the detected build and then the detected tests once and prints a compact pass or fail summary. Body: run `check`; if red, fix the first failure only, then run `check` again; stop after the iteration budget (default 5, `$ARGUMENTS` overrides) or when green; report what changed per iteration. Refuses to edit test assertions to make them pass unless the user said to. |
| `ci-watch` | no | `status`, `watch`, `failed-logs` | `gh run list` for the current branch, `gh run watch` with a timeout, and the logs of failed jobs only, trimmed to the failing step. |
| `flaky-test-hunt` | no | `run` | `run <count> <filter>` runs one test filter N times through the detected runner and reports pass and fail counts with the first failing output. |

### 4.2 `/loop` in the harness (row 23)

- [ ] `src/Mux.Core/Jobs/LoopScheduler.cs`: owns recurring prompts for one session. Each loop has an id, prompt text, interval (fixed) or self-paced mode, an iteration cap, a stop condition, and a cancellation token linked to the session. Fires by enqueueing a normal job through `JobManager`, so the write lease, approvals, and transcripts all behave as they do for typed prompts.
- [ ] `src/Mux.Core/Jobs/LoopDefinition.cs` and `src/Mux.Core/Jobs/LoopStatusEnum.cs`, one type per file.
- [ ] Self-paced mode: the model ends a loop iteration by calling a new `schedule_next` tool with a delay in seconds (min 30, max 3600) and a one-line reason, or `stop: true`. Without a call, the loop stops. Prevents a runaway loop on a model that never stops.
- [ ] Settings: `loopMaxIterations` (default 50, min 1, max 1000), `loopMinIntervalSeconds` (default 30).
- [ ] TUI: `/loop 5m <prompt>`, `/loop <prompt>` (self-paced), `/loops` (list and cancel). Sidebar shows active loops with the next fire time.
- [ ] `mux print --loop 5m` for headless use, which exits when the loop stops.
- [ ] Persist active loops with the session so `/sessions` resume restores them paused, not running.
- [ ] Tests: `LoopSchedulerSuite` with an injectable clock (fixed interval, self-paced, iteration cap, cancel mid-run, resume-paused, overlap prevention when an iteration outlasts the interval).

---

## Phase 5: Hooks and background processes

### 5.1 Tool-level hooks (row 17)

- [ ] Add `PreToolUse`, `PostToolUse`, and `Stop` to `HookEventEnum`, with wire names `pre-tool-use`, `post-tool-use`, `stop`. Move `HookEventEnumConverter` into its own file while touching it (the current file holds two classes, which `CODE_STYLE.md` disallows).
- [ ] `HookDefinition` gains an optional `matcher` (tool-name glob, same syntax as `--allow-tools`).
- [ ] `pre-tool-use` hooks receive the tool name and arguments as JSON on stdin. Exit 0 continues; exit 2 blocks the call and returns the hook's stderr to the model as the tool result; any other nonzero logs a warning and continues. This matches Claude Code's contract, so hooks written for it port with only a config change.
- [ ] `post-tool-use` receives the result too and can append text to the tool result via stdout. `stop` fires when a run completes and can request one more turn via exit 2 (bounded to 3 per run).
- [ ] Wire through `AgentLoop` at the existing `ToolCallProposedEvent` and `ToolCallCompletedEvent` points, after approval for `pre-tool-use`.
- [ ] Tests in `PluginSuite`: matcher, block, append, stop re-entry bound, hook timeout.

### 5.2 Background processes (row 18)

React dev servers, `docker compose up`, and watch-mode test runners do not fit a foreground tool with a timeout.

- [ ] `src/Mux.Core/Tools/Tools/BackgroundProcessTool.cs` exposing `process_start` (returns an id), `process_output` (new output since last read, with an optional regex wait and timeout), `process_list`, and `process_stop`.
- [ ] `src/Mux.Core/Tools/BackgroundProcessRegistry.cs`: per-session ownership, ring-buffered output (configurable, default 1 MB per process), kill on session end, max concurrent processes setting (default 8).
- [ ] Mutation classification: `process_start` and `process_stop` are mutating and take the approval path; `process_output` and `process_list` are read-only.
- [ ] Sidebar and `/processes` in the TUI; a processes panel in desktop.
- [ ] Add a `react-dev-server` playbook to `DefaultReactSkills` once this lands: start the detected dev script, wait for the "ready" line, report the URL.
- [ ] Tests: start, read, regex wait, stop, session-end cleanup, output cap.

---

## Phase 6: Interaction gaps

- [ ] **`@file` mentions (row 19).** TUI composer completion on `@` over `glob` results; on submit, mentioned files go through `FileContextBuilder` (so large files become maps, not truncations) and attach to the user message. Desktop and web get the same through a shared Core helper.
- [ ] **Persistent memory (row 24).** `~/.mux/memory/<repo-key>/` holding one Markdown file per fact plus an index. A `remember` tool (mutating, write lease) and a prompt section that includes the index, capped by a `memoryMaxBytes` setting. `#` at the start of a TUI prompt saves the rest of the line as a memory without a model call. `/memory` lists, edits, and deletes.
- [ ] **Plan mode (row 25).** `/plan` toggles: the run uses the `read-only` sandbox posture plus an `exit_plan` tool that presents the plan for approval; on approval, the posture returns to the session's setting and the plan becomes the task plan through `PlanTasksTool`. Shift+Tab cycles normal, auto-approve, and plan in the TUI.
- [ ] **Ask-the-user tool (row 26).** `ask_user` with 2 to 4 options and a free-text fallback, rendered as a modal in the TUI and desktop and as a card in web and VS Code. In `mux print`, it returns "no user available; choose a sensible default and state it."

---

## Phase 7: Larger items (separate plans)

Each of these deserves its own plan file. They are listed so the scorecard stays honest about what parity still requires after Phases 1 through 6.

- [ ] Image input (row 27): attachment plumbing through `ConversationMessage`, per-adapter content parts, a model capability flag, and paste support in each surface.
- [ ] Worktree isolation (row 28): optional `isolation: worktree` on subagents and background jobs, with the existing `GitCheckpointService` patterns reused for cleanup.
- [ ] MCP server mode (row 29): `mux mcp serve` exposing a `run` tool (prompt in, final answer out) and read-only session tools over stdio.
- [ ] Custom status line (row 30): a command in settings whose stdout replaces the sidebar footer, refreshed per turn.

Backlog with no plan yet: output styles (row 31, mostly covered by prompt profiles), scheduled runs outside a live session (row 32), notebook editing (row 33).

---

## Default library after this plan

| Category file | Today | Added | After |
|---|---:|---:|---:|
| `DefaultGitSkills` | 15 | 0 | 15 |
| `DefaultDotnetSkills` | 8 | 0 | 8 |
| `DefaultHygieneSkills` | 8 | 0 | 8 |
| `DefaultScaffoldDocsSkills` | 7 | 0 | 7 |
| `DefaultWorkflowUtilitySkills` | 8 | 0 | 8 |
| `DefaultProjectSkills` | 0 | 1 | 1 |
| `DefaultJavaScriptSkills` | 0 | 8 | 8 |
| `DefaultPythonSkills` | 0 | 7 | 7 |
| `DefaultReactSkills` | 0 | 6 (+1 in Phase 5) | 7 |
| `DefaultJavaSkills` | 0 | 6 | 6 |
| `DefaultCppSkills` | 0 | 6 | 6 |
| `DefaultGoSkills` | 0 | 4 | 4 |
| `DefaultRustSkills` | 0 | 4 | 4 |
| `DefaultContainerSkills` | 0 | 3 | 3 |
| `DefaultReviewSkills` | 0 | 5 | 5 |
| `DefaultAgentPlaybookSkills` | 0 | 4 | 4 |
| `DefaultLoopSkills` | 0 | 4 | 4 |
| **Total** | **46** | **59** | **105** |

Existing users receive the new defaults on their next startup through `SeedNewInto`; nothing they have edited or deleted is touched. With relevance gating on, a typical single-language repository lists about 45 skills (the ungated ones plus its own family) instead of 105.

Two existing defaults deserve a second look while this work is open. `new-tool` and `new-touchstone-suite` scaffold mux's own `IToolExecutor` and Touchstone types, which only make sense inside the mux repository. Giving them `appliesTo: [src/Mux.Core/Mux.Core.csproj]` hides them everywhere else at no cost.

---

## Documentation

- [~] `docs/SKILLS_AUTHORING.md`: playbook skills, hybrids, `appliesTo`, `userInvocable`, `argumentHint`, `$ARGUMENTS`, project scopes and the trust gate, and Claude-format compatibility are documented (Phase 1). Still to come: the exit-code convention and `MUX_SKILL_DRY_RUN` (Phase 2).
- [~] `docs/USAGE.md`: invoking skills by name, project skills and trust, the listing mode, and project instruction files are documented (Phase 1). Still to come: `/loop` and `/loops`, `/processes`, `/plan`, `/memory`, `@` mentions.
- [~] `docs/CONFIG.md`: the Phase 1 settings and files (`trusted-projects.json`, `MUX.md`) are documented. Later phases add theirs.
- [~] `docs/REST_API.md` and the Postman collection: `GET /v1.0/api/context/instructions` and `POST /v1.0/api/skills/expand` are documented, both in a new Postman **Context** folder and in the **Skills** folder, with a `workingDirectory` variable. Loop and process routes come later.
- [~] `README.md`: project instruction files and slash invocation are in Highlights, and the two new flags are in the options table. The new skill families get added as they land.
- [~] `CHANGELOG.md`: Phase 1 is recorded under `Unreleased`. No version number was changed.
- [x] `docs/DESKTOP.md`: `/<skill>`, `/instructions`, and `/trust` (Phase 1).
- [ ] Final pass over every new doc and every skill body for em-dashes and the phrasing rules in `WRITING_DOCUMENTS.md`.

## Testing summary

Every phase adds Touchstone suites in `src/Test.Shared/Suites/` and registers them in `MuxSuites.cs`, so they run under `Test.Automated`, `Test.Xunit`, and `Test.Nunit` on net8.0 and net10.0. The CI step `mux skill validate` already gates the default library and will cover the 59 new skills with no change. Toolchain behavior is tested through dry-run detection on every platform and through live runs only where the toolchain is present, so CI does not need Node, Python, a JDK, CMake, Go, and Rust installed to stay green. The full build must be free of errors and warnings before each phase is marked done.

## Order of work

Phase 1 is done. It was built in the order 1.2, 1.1, 1.4, 1.3, 1.5: the playbook builder unblocks every later skill, instructions are the largest single parity gain, invocation makes skills feel native, and scoping plus gating matter once the library actually grows. Then Phase 3 before Phase 2, because `code-review` and `init` are what a Claude Code or Codex user tries in their first five minutes, and the language families can land one per PR afterward. Phases 4 through 6 are independent of each other and can run in parallel once Phase 1 is in.
