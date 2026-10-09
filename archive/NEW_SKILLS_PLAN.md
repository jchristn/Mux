# New Default Skills and OOBE Parity Plan

_Status: all phases done (2026-10-08). Rows 27 and 30 through 33 were dropped; rows 28 and 29 have their own plans, also done. Check boxes as work lands. `[ ]` = todo, `[x]` = done, `[~]` = in progress._

mux ships 46 default skills, and nearly all of them assume a git repository, a .NET solution, or both. Someone who opens mux in a React app, a Django service, a Maven project, or a CMake tree gets git helpers and nothing that knows how their code builds or tests. That gap is the first thing a Claude Code or Codex user notices, and it is the reason for this plan. The second thing they notice is subtler: both of those harnesses read a project instruction file on startup, can review a diff on request, and let skills be invoked by name with arguments. mux has a strong engine (subagents, MCP, sandboxing, undo, compaction, sessions across four surfaces) but the out-of-box experience still feels like a toolkit rather than an agent that already knows the job.

The scorecard below lists everything that exists in Claude Code or Codex and is missing (or only partly present) in mux, including the six skill families requested by name: JavaScript, Python, React, Java, C++, code review, and loops. Scores are subjective. **Simplicity** is how cheaply the item lands in the current codebase (10 = an afternoon of data entry, 1 = a new subsystem). **Value** is how much it moves the out-of-box experience toward parity (10 = users hit the gap in their first session). **Total** is the sum, and the table is sorted by it.

## Scorecard

| # | Capability | Claude Code | Codex | mux today | Simplicity | Value | Total | Phase |
|---:|---|:---:|:---:|---|---:|---:|---:|:---:|
| 1 | Project instruction file loaded into the system prompt (`AGENTS.md`, `CLAUDE.md`, hierarchical) | yes (`CLAUDE.md`) | yes (`AGENTS.md`) | **done in Phase 1**: `MUX.md` / `AGENTS.md` / `CLAUDE.md` on every surface | 9 | 10 | **19** | 1 |
| 2 | Instruction-only ("playbook") default skills: prose procedures with no mandatory commands | yes | yes | **done in Phase 1**: `DefaultSkillDef` builds playbooks; four default playbooks shipped in Phase 3 | 9 | 8 | **17** | 1 |
| 3 | Code review on demand (uncommitted, vs branch, a commit, a PR) | yes (`/code-review`) | yes (`/review`) | **done in Phase 3**: `code-review` (uncommitted, branch, commit, pr, file); VS Code routes to it | 8 | 9 | **17** | 3 |
| 4 | `/init`: survey the repo and write an instruction file | yes | yes | **done in Phase 3**: `init` | 9 | 8 | **17** | 3 |
| 5 | JavaScript / TypeScript toolchain skills | via model + shell | via model + shell | **done in Phase 2** | 9 | 8 | **17** | 2 |
| 6 | Python toolchain skills | via model + shell | via model + shell | **done in Phase 2** | 9 | 8 | **17** | 2 |
| 7 | Invoke a skill by name with arguments (`/code-review main`, `$ARGUMENTS`) | yes | yes (custom prompts) | **done in Phase 1**: terminal, desktop, dashboard, and `mux print` | 7 | 9 | **16** | 1 |
| 8 | Project-scoped skills (checked into the repo) and Claude-format skill import | yes (`.claude/skills`) | yes (`.agents/skills`) | **done in Phase 1**, with a per-project trust gate | 8 | 8 | **16** | 1 |
| 9 | Security review of pending changes | yes (`/security-review`) | via `/review` | **done in Phase 3**: `security-review` | 9 | 7 | **16** | 3 |
| 10 | Iterate-until-green loops (retry a check, fix-build-test cycle, CI watch) | via model | via model | **done in Phase 4**: `loop-until`, `fix-until-green`, `ci-watch`, `flaky-test-hunt` | 8 | 8 | **16** | 4 |
| 11 | Project detection (languages, package managers, build and test commands) | implicit | implicit | **done in Phase 2** | 9 | 7 | **16** | 2 |
| 12 | Simplify / cleanup pass on changed code | yes (`/simplify`) | no | **done in Phase 3**: `simplify` | 9 | 6 | **15** | 3 |
| 13 | PR review comments fetched for the agent to address | yes (`/pr-comments`) | no | **done in Phase 3**: `pr-comments` | 9 | 6 | **15** | 3 |
| 14 | React skills | via model + shell | via model + shell | **done in Phase 2** | 8 | 7 | **15** | 2 |
| 15 | Java skills (Maven and Gradle) | via model + shell | via model + shell | **done in Phase 2** | 8 | 7 | **15** | 2 |
| 16 | Relevance-gated skill listing so 100+ skills do not flood a small context window | progressive disclosure | progressive disclosure | **done in Phase 1**: `appliesTo` globs and `skillListingMode` | 7 | 7 | **14** | 1 |
| 17 | Tool-level hooks (`pre-tool-use`, `post-tool-use`, `stop`) | yes | partial (`notify`) | **done in Phase 5**: `pre-tool-use`, `post-tool-use`, `stop` with matchers, on every surface | 6 | 8 | **14** | 5 |
| 18 | Background processes (start a dev server, read its output later, stop it) | yes | partial | **done in Phase 5**: `process_start`, `process_output`, `process_list`, `process_stop`, `/processes` | 5 | 9 | **14** | 5 |
| 19 | `@file` mentions in the composer | yes | yes | **done in Phase 6** | 7 | 7 | **14** | 6 |
| 20 | Debugging playbook and `git bisect` driver | via model | via model | **done in Phase 3**: `debug` and `git-bisect` | 9 | 5 | **14** | 3 |
| 21 | Containers and orchestration skills (Docker, Docker Compose, Kubernetes, Minikube, Helm, OpenStack) | via model + shell | via model + shell | **done in Phase 2** | 8 | 8 | **16** | 2 |
| 21a | Cloud provider skills (AWS, Azure, Google Cloud, DigitalOcean, Rackspace, Vercel, Alibaba, Huawei, IBM Cloud, Linode, Netlify, Cloudflare, fly.io) plus Terraform and Pulumi | via model + shell | via model + shell | **done in Phase 2** | 6 | 8 | **14** | 2 |
| 21b | Go and Rust skills | via model + shell | via model + shell | **done in Phase 2** | 9 | 5 | **14** | 2 |
| 22 | C++ skills (CMake, CTest, clang-format, clang-tidy, sanitizers) | via model + shell | via model + shell | **done in Phase 2** | 7 | 6 | **13** | 2 |
| 23 | `/loop`: re-run a prompt on an interval or self-paced | yes | no | **done in Phase 4**: `/loop`, `/loops`, `schedule_next`, `mux print --loop` | 6 | 7 | **13** | 4 |
| 24 | Persistent memory (agent-written facts reused across sessions, `#` quick-add) | yes | partial | **done in Phase 6** | 6 | 7 | **13** | 6 |
| 25 | Plan mode (read-only exploration, then an approved plan, then execution) | yes | partial (approval modes) | **done in Phase 6**: `/plan`, Shift+Tab, `exit_plan`, `mux print --plan` | 6 | 7 | **13** | 6 |
| 26 | Structured "ask the user" tool (multiple choice mid-turn) | yes | no | **done in Phase 6**: `ask_user` | 6 | 6 | **12** | 6 |
| 28 | Git worktree isolation for subagents and parallel jobs | yes | yes (cloud) | **done**: see `archive/WORKTREE_ISOLATION_PLAN.md` | 5 | 6 | **11** | 7 |
| 29 | Expose mux as an MCP server | yes (`claude mcp serve`) | yes (`codex mcp-server`) | **done**: `mux mcp serve` on Voltaic; see `MCP_SERVER_PLAN.md` | 5 | 6 | **11** | 7 |

Rows 27 (image input), 30 (custom status line), 31 (output styles), 32 (scheduled cloud or cron runs), and 33 (Jupyter notebook editing) were dropped from this plan on 2026-10-08 at the user's request.

A few things that look like gaps are not, and they are deliberately absent from the table: sandbox postures, tool allow and deny globs, compaction, resume and fork, subagents, web search and retrieval, undo and redo, task plans, custom slash commands, MCP client support, and usage tracking all exist today.

## What remains

Nothing. All 30 rows still in the scorecard are done (Phases 1 through 6, plus rows 28 and 29 under their own plans). Rows 27, 30, 31, 32, and 33 were dropped at the user's request. The library holds 153 default skills, and `new-tool` and `new-touchstone-suite` are listed only inside the mux repository.

Rows 1, 2, 7, 8, and 16 are harness changes, not skills, but they come first because the new skills depend on them. Most rows from 3 through 15 lean on playbook skills (2), project scoping (8), or name invocation (7). Without them, a React developer would still have to type "please run the react-test skill" instead of `/react-test`, and 152 skills would be listed in every system prompt sent to a 7B model with an 8K window.

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

The full library after this plan is 152 skills. Listed one line each, that is roughly 6,000 tokens on every turn, which is half the context of the local models mux is built to support.

- [x] Frontmatter `appliesTo`, evaluated by `src/Mux.Core/Skills/AppliesToMatcher.cs`: relative globs with `*`, `?`, and `**`, where rooted or `..` globs never match, dependency and build directories are skipped under `**`, and a visit budget bounds the search.
- [x] Relevance is evaluated once per skill per project view and cached until the view is rebuilt on the refresh interval.
- [x] Setting `skillListingMode`: `relevant` (default), `all`, or `none`. A call with no working directory skips filtering.
- [x] Footer from the editable `section.skills.more` prompt (`{Count} more skills are installed ...`), and `skill` with the name `list` returns every usable skill. `list` is now a reserved skill id.
- [x] Tests: `SkillListingSuite` (literal, wildcard, and `**` globs, skipped directories, rejected escapes, relevant mode with the footer, `all` and `none`, settings normalization, and the footer's required placeholder).

**Phase 1 acceptance:** met. A repository with an `AGENTS.md` and a `.claude/skills/foo/SKILL.md` playbook gets the instructions in its system prompt, and `/foo bar` runs the playbook with `$ARGUMENTS = bar`, in the terminal, `mux print`, desktop, and web, on net8.0 and net10.0. The terminal path is covered end to end by `SkillInvocation.ShellRunsSkillAndBuiltInsWin`. The print, desktop, and web paths share the same Core expander and are verified through it and the route build; they have no surface-level automated test yet.

---

## Phase 2: Language, toolchain, infrastructure, and cloud skills (done)

Each family is one category class in `src/Mux.Core/Skills/`, merged in `DefaultSkillLibrary.All()`. Every command skill below uses `pwsh`, detects its tool, sets `appliesTo`, and follows the exit-code convention from the guiding decisions. Commands that take a filter or path read it from `$args[0]`.

**Status:** done. 93 new default skills across 2.0 through 2.8, for 139 in the library (the remaining 13 of the planned 152 are the Phase 3 and 4 playbooks and loops). The full build has no errors or warnings, and all runners pass on net8.0 and net10.0 (Touchstone 1096 cases: 1089 passed, 7 skipped by design; xUnit and NUnit 1090 each). Not done: the opt-in `ToolchainLiveSuite` (see Phase 2 tasks).

Shared infrastructure, as built:

- [x] `src/Mux.Core/Skills/Resources/mux-skill.ps1`, embedded in Mux.Core and exposed by `DefaultSkillHelpers`. It holds the conventions every toolchain skill follows: `Invoke-MuxTool` echoes each command, prints `DRYRUN: <command>` instead of running it when `MUX_SKILL_DRY_RUN=1`, exits 2 with an install hint when the tool is missing, and passes through the tool's own exit code; `Exit-MuxNotApplicable` exits 2 with a reason; plus repo-root and walk-up file discovery, Node package-manager detection (`packageManager` field, then bun, pnpm, yarn, npm lockfiles in the package or repository root), package.json script and dependency checks, and Python environment detection (uv, poetry, pipenv, or a `.venv` with pip). It is a real `.ps1` file in the repository rather than a C# string, so it can be read and linted as PowerShell.
- [x] `DefaultSkillDef.Resources` and `DefaultSkillHelpers.Attach`, which add the helper as `resources/mux-skill.ps1` and prefix each pwsh command with one dot-source line. `DefaultSkillLibrary.Definitions()` lists the data-declared families, `AllResources()` returns their files, and both `SeedInto` and `SeedNewInto` write them beside `SKILL.md` (refusing any path that escapes the skill folder). Each skill folder carries its own copy of the helper, so a seeded skill stays self-contained and editable.
- [x] `SkillExecutor.ExecuteAsync` takes optional per-run environment variables, so tests set `MUX_SKILL_DRY_RUN=1` for one process without touching the test host.

### 2.0 Project detection (row 11): `DefaultProjectSkills.cs` (done)

`project-detect` is the anchor for everything else. `init`, `code-review`, and `fix-until-green` all call it first, so it ships first.

| Id | Mut. | Commands | Notes |
|---|:---:|---|---|
| `project-detect` | no | `summary`, `json` | Reports languages (by file counts), package managers, build systems, test frameworks, linters, formatters, CI providers, container files, and the exact build, test, lint, and format commands it would run. `json` emits the same as a single object for other skills. No `appliesTo`: always listed. |

### 2.1 JavaScript and TypeScript (row 5): `DefaultJavaScriptSkills.cs` (done)

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

### 2.2 Python (row 6): `DefaultPythonSkills.cs` (done)

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

### 2.3 React (row 14): `DefaultReactSkills.cs` (done)

React skills sit on top of the JavaScript detection and only list when `package.json` depends on `react`. `appliesTo` cannot express a dependency check, so the skills use `appliesTo: [package.json]` and each command exits 2 with a note when React is absent. A dependency-aware `appliesTo` is a possible follow-up, not a prerequisite.

| Id | Mut. | Commands | Notes |
|---|:---:|---|---|
| `react-new-component` | yes | `create` | `create <Name> [dir]` writes a function component, a test beside it (matching the detected runner and Testing Library if present), and a CSS module or nothing, following the extension (`.tsx` vs `.jsx`) the repo already uses. Refuses to overwrite. |
| `react-new-hook` | yes | `create` | Same, for `use<Name>` hooks. |
| `react-test` | no | `component` | Runs tests matching one component's file name. |
| `react-build-analyze` | no | `report` | Builds, then lists the largest emitted JS and CSS chunks from `dist/` or `build/` with sizes. |
| `react-lint-hooks` | no | `check` | Runs ESLint restricted to `react-hooks/*` and `jsx-a11y/*` rules when those plugins are installed, reporting what it skipped otherwise. |
| `react-upgrade-check` | no | `report` | Versions of `react`, `react-dom`, `@types/react*`, and peer-dependency conflicts reported by the package manager. |

### 2.4 Java (row 15): `DefaultJavaSkills.cs` (done)

Build tool detection order: `mvnw` → Maven wrapper, `gradlew` → Gradle wrapper, `pom.xml` → `mvn`, `build.gradle*` → `gradle`. Wrappers always win because they pin the version the project tested with. `appliesTo: [pom.xml, build.gradle, build.gradle.kts, mvnw, gradlew]`.

| Id | Mut. | Commands | Notes |
|---|:---:|---|---|
| `java-build` | yes | `compile`, `package`, `clean` | Maven `-q -B`, Gradle `--console=plain`. |
| `java-test` | no | `all`, `filter` | Maven `-Dtest=`, Gradle `--tests`. Prints Surefire or Gradle report paths on failure. |
| `java-format` | yes | `apply`, `verify` | Spotless when configured; otherwise exits 2 and suggests adding it. |
| `java-lint` | no | `check` | Checkstyle, SpotBugs, or PMD tasks when configured. |
| `java-deps` | no | `tree`, `updates` | `dependency:tree` / `dependencies`; `versions:display-dependency-updates` or the Gradle versions plugin. |
| `java-new-class` | yes | `create` | `create com.example.Foo [class\|interface\|record\|enum]` under `src/main/java`, with a matching test under `src/test/java` when JUnit is present. |

### 2.5 C++ (row 22): `DefaultCppSkills.cs` (done)

CMake is the supported path, since it covers most modern C++ projects and `compile_commands.json` makes clang-tidy work. Presets win when `CMakePresets.json` exists. Plain Makefile projects get `cpp-build` only. `appliesTo: [CMakeLists.txt, CMakePresets.json, Makefile, meson.build]`.

| Id | Mut. | Commands | Notes |
|---|:---:|---|---|
| `cpp-configure` | yes | `debug`, `release`, `preset` | Configures into `build/<config>` with `CMAKE_EXPORT_COMPILE_COMMANDS=ON`. |
| `cpp-build` | yes | `build` | `cmake --build` with parallelism set to the processor count; falls back to `make -j` or `meson compile`. |
| `cpp-test` | no | `all`, `filter` | `ctest --output-on-failure`, `-R <regex>` for filter. |
| `cpp-format` | yes | `apply`, `verify` | clang-format over tracked C and C++ sources, honoring `.clang-format`. |
| `cpp-tidy` | no | `check` | clang-tidy against `compile_commands.json`, limited to changed files when given `changed`. |
| `cpp-sanitize` | yes | `asan`, `ubsan` | Configures a separate build tree with the sanitizer flags, builds, and runs CTest. |

### 2.6 Containers, orchestration, and private cloud (row 21) (done)

These skills touch running systems, not just files, so they follow stricter rules than the language families. Every command that talks to a cluster or cloud prints the target first (Docker context, Kubernetes context and namespace, OpenStack cloud and project) on its first line of output, so the model and the transcript always show where a command ran. Read-only commands are `mutating: false`. Anything that changes a cluster runs as a preview by default (`--dry-run=server`, `helm diff`, `helm template`, `--dry-run` for compose) and only applies when the caller passes `apply` explicitly. A guard refuses to apply when the active context, profile, or project name matches `settings.skillProdPattern` (default `prod|production|live`) unless the arguments repeat that name with `--confirm <name>`. No skill ships a delete, destroy, or prune command; those stay with the user.

`DefaultContainerSkills.cs`, `appliesTo: [Dockerfile, "**/Dockerfile", "*.Dockerfile", compose.yaml, compose.yml, docker-compose.yaml, docker-compose.yml]`:

| Id | Mut. | Commands | Notes |
|---|:---:|---|---|
| `docker-build` | yes | `build`, `tag`, `push` | Builds with BuildKit, tags with the git short SHA by default, and pushes only to the registry named in the arguments. |
| `docker-inspect` | no | `ps`, `images`, `logs`, `stats`, `context` | `logs <container> [lines]` trims to the last N lines (default 200); `context` prints the active Docker context and engine. |
| `compose` | yes | `config`, `up`, `down`, `ps`, `logs`, `restart` | `config` validates and prints the resolved file; `up` is detached and waits for health checks with a timeout; `down` never passes `-v`, so named volumes survive. |
| `dockerfile-lint` | no | `check` | hadolint when installed; otherwise a built-in checklist (pinned base tags, `USER` set, no `ADD` for URLs, a `HEALTHCHECK` for services). |

`DefaultKubernetesSkills.cs`, `appliesTo: [Chart.yaml, "**/Chart.yaml", kustomization.yaml, "**/kustomization.yaml", skaffold.yaml, "k8s/**", "kubernetes/**", "manifests/**", "deploy/**/*.yaml"]`:

| Id | Mut. | Commands | Notes |
|---|:---:|---|---|
| `k8s-context` | no | `current`, `list`, `namespaces` | The first thing the model runs; prints context, cluster, user, and namespace. Switching contexts is left to the user. |
| `k8s-inspect` | no | `get`, `describe`, `logs`, `events`, `top`, `rollout-status` | `logs <pod> [container] [lines]`; `events` sorted by time and limited to warnings by default. |
| `k8s-validate` | no | `client`, `server`, `schema` | `kubectl apply --dry-run=client` / `--dry-run=server`, and kubeconform when installed. |
| `k8s-apply` | yes | `diff`, `apply`, `rollout-restart` | `diff` first, always. `apply <path>` uses `-f` or `-k` by detecting a kustomization, and is refused by the production guard without `--confirm`. |
| `minikube` | yes | `status`, `start`, `stop`, `image-load`, `service-url`, `addons` | `start [driver] [k8s-version]`; `image-load <image>` loads a local build without a registry; `addons` lists, never enables silently. |
| `helm` | yes | `lint`, `template`, `deps`, `diff`, `upgrade`, `list`, `history` | `diff` needs the helm-diff plugin and says so when missing. `upgrade <release> <chart> [values...]` runs `--install --atomic --wait` with a timeout, behind the production guard. |

`DefaultOpenStackSkills.cs`, `appliesTo: [clouds.yaml, "**/clouds.yaml", "heat/**", "*.hot.yaml", "**/*.hot.yaml"]`, `requiresTools: [openstack]`:

| Id | Mut. | Commands | Notes |
|---|:---:|---|---|
| `openstack-whoami` | no | `token`, `project`, `catalog`, `quotas` | Resolves the cloud from `OS_CLOUD` or the argument and prints project, region, and user. |
| `openstack-inspect` | no | `servers`, `images`, `flavors`, `networks`, `volumes`, `stacks` | Table output trimmed to the useful columns. |
| `openstack-heat` | yes | `validate`, `preview`, `create`, `update`, `events` | `preview` runs `stack create --dry-run`; `create`/`update` sit behind the production guard. |

### 2.7 Cloud providers (row 21a) (done)

Each provider gets a small family built on its official CLI (`aws`, `az` and `azd`, `gcloud`, `doctl`, `vercel`, `aliyun`, `hcloud` (KooCLI), `ibmcloud`, `linode-cli`, `netlify`, `wrangler`, `flyctl`), plus two cross-cloud infrastructure-as-code skills. The same rules as 2.6 apply: the account, subscription, or project is printed first; reads are the default; changes go through a preview or plan; the production guard applies; and nothing deletes. Two rules are specific to cloud work. Skills never print secret values (secret and environment-variable commands list names and last-updated times only). Skills never run a login flow: when the CLI is not authenticated, the command exits 2 with the exact login command for the user to run.

Most of these providers leave no file in the repository, so `appliesTo` alone cannot decide relevance. This section adds one harness field:

- [ ] Frontmatter `requiresTools`: executables that must be on `PATH` for the skill to be listed (for example `[aws]`). The runtime checks `PATH` once per refresh and caches the result; it never runs the tool to check. A skill is listed when every required tool is present and, if it also has `appliesTo`, at least one glob matches. A machine without `gcloud` therefore never sees the Google skills, and a repository with `vercel.json` sees the Vercel skill only when the Vercel CLI is installed. Tests extend `SkillListingSuite` with a fake `PATH`.

**AWS** (`DefaultAwsSkills.cs`, `requiresTools: [aws]`). AWS is where most users will spend time, so it gets the widest coverage, grouped by job rather than one skill per service:

| Id | Mut. | Commands | Notes |
|---|:---:|---|---|
| `aws-whoami` | no | `identity`, `profiles`, `regions` | `sts get-caller-identity`, the active profile and region, and whether SSO credentials have expired. |
| `aws-compute` | yes | `ec2-list`, `ec2-describe`, `ec2-start`, `ec2-stop`, `lambda-list`, `lambda-invoke`, `lambda-logs` | Start, stop, and invoke sit behind the production guard (matched against the profile and instance `Environment` tag). |
| `aws-containers` | yes | `ecs-services`, `ecs-tasks`, `ecs-redeploy`, `eks-clusters`, `eks-kubeconfig`, `ecr-repos`, `ecr-login`, `ecr-push` | `ecs-redeploy` is `update-service --force-new-deployment` and waits for stability; `eks-kubeconfig` writes a named context and hands off to the 2.6 Kubernetes skills. |
| `aws-storage` | yes | `s3-buckets`, `s3-ls`, `s3-sync`, `s3-presign` | `s3-sync` always runs `--dryrun` first and applies only with `apply`; it never passes `--delete`. |
| `aws-data` | no | `rds-instances`, `rds-snapshots`, `dynamodb-tables`, `dynamodb-describe`, `elasticache-clusters` | Read-only on purpose: database changes stay with migrations and the user. |
| `aws-deploy` | yes | `cfn-stacks`, `cfn-events`, `cfn-changeset`, `sam-validate`, `sam-deploy`, `cdk-synth`, `cdk-diff`, `cdk-deploy` | Detects SAM (`template.yaml`, `samconfig.toml`), CDK (`cdk.json`), or plain CloudFormation. Every deploy shows a change set or diff first. |
| `aws-observe` | no | `logs-groups`, `logs-tail`, `alarms`, `cost-month` | `logs-tail <group> [since]` is bounded (default 15 minutes, 500 lines); `cost-month` is month-to-date cost by service from Cost Explorer. |
| `aws-integration` | no | `sqs-queues`, `sqs-depth`, `sns-topics`, `secrets-list`, `ssm-params`, `route53-zones`, `iam-whoami-policies` | Secret and parameter commands list names only. `iam-whoami-policies` shows the policies attached to the caller. |

**Azure** (`DefaultAzureSkills.cs`, `requiresTools: [az]`; `azure-apps` also uses `azd` when `azure.yaml` exists):

| Id | Mut. | Commands | Notes |
|---|:---:|---|---|
| `azure-whoami` | no | `account`, `subscriptions` | Active subscription, tenant, and user. |
| `azure-resources` | no | `groups`, `list`, `show` | `list [group]` across resource types. |
| `azure-compute` | yes | `vm-list`, `vm-start`, `vm-stop`, `vm-deallocate` | Behind the production guard. |
| `azure-containers` | yes | `aks-list`, `aks-credentials`, `acr-list`, `acr-login`, `containerapp-list`, `containerapp-logs`, `containerapp-update` | `aks-credentials` hands off to the Kubernetes skills. |
| `azure-apps` | yes | `webapp-list`, `webapp-logs`, `webapp-deploy`, `functionapp-list`, `azd-preview`, `azd-deploy` | `azd-preview` is `azd provision --preview`; Bicep files get `az bicep build` validation. |
| `azure-data` | no | `storage-accounts`, `blob-ls`, `sql-servers`, `cosmos-accounts` | Read-only. |

**Google Cloud** (`DefaultGcpSkills.cs`, `requiresTools: [gcloud]`):

| Id | Mut. | Commands | Notes |
|---|:---:|---|---|
| `gcp-whoami` | no | `account`, `project`, `config` | Active account, project, and region. |
| `gcp-compute` | yes | `instances`, `start`, `stop` | Behind the production guard. |
| `gcp-run` | yes | `run-services`, `run-logs`, `run-deploy`, `functions-list`, `appengine-versions` | `run-deploy` deploys a new revision with `--no-traffic` first and shifts traffic only with `promote`. |
| `gcp-gke` | yes | `clusters`, `credentials` | Hands off to the Kubernetes skills. |
| `gcp-storage` | yes | `buckets`, `ls`, `rsync` | `rsync` is dry-run first (`-n`) and never deletes. |
| `gcp-data` | no | `sql-instances`, `firestore-indexes`, `bigquery-datasets`, `bigquery-dry-run` | `bigquery-dry-run <sql>` reports bytes scanned without running the query. |

**Platform and smaller providers.** These are mostly one skill each, because their CLIs are compact:

| Id | File | Gate | Commands | Notes |
|---|---|---|---|---|
| `do-whoami` | `DefaultDigitalOceanSkills.cs` | `requiresTools: [doctl]` | `account`, `balance` | |
| `do-infra` | same | `[doctl]` | `droplets`, `kubernetes`, `databases`, `volumes`, `spaces` | Read-only; DOKS hands off to the Kubernetes skills. |
| `do-apps` | same | `[doctl]`, `appliesTo: [.do/app.yaml]` | `list`, `spec-validate`, `deploy`, `logs` | App Platform. |
| `rackspace` | `DefaultRackspaceSkills.cs` | `[openstack]` | `whoami`, `servers`, `spot-clusters` | Rackspace Cloud is OpenStack-based, so this wraps the 2.6 OpenStack skills with a Rackspace `clouds.yaml` profile; `spot-clusters` uses the Rackspace Spot CLI when installed and otherwise points to the Kubernetes skills with the downloaded kubeconfig. |
| `vercel` | `DefaultEdgePlatformSkills.cs` | `[vercel]`, `appliesTo: [vercel.json, .vercel/**, next.config.*]` | `whoami`, `ls`, `deploy-preview`, `deploy-prod`, `logs`, `env-names` | `deploy-prod` is behind the guard; `env-names` never prints values. |
| `netlify` | same | `[netlify]`, `appliesTo: [netlify.toml, .netlify/**]` | `status`, `deploy-draft`, `deploy-prod`, `logs`, `env-names` | |
| `cloudflare` | same | `[wrangler]`, `appliesTo: [wrangler.toml, wrangler.json, wrangler.jsonc]` | `whoami`, `deploy-dry-run`, `deploy`, `tail`, `d1-list`, `kv-list`, `r2-list`, `pages-deploy` | `tail` is bounded by time and line count. |
| `flyio` | same | `[flyctl]` or `[fly]`, `appliesTo: [fly.toml]` | `status`, `deploy`, `logs`, `scale-show`, `secrets-names`, `releases` | |
| `alibaba-whoami` | `DefaultAlibabaSkills.cs` | `[aliyun]` | `profile`, `regions` | |
| `alibaba-infra` | same | `[aliyun]` | `ecs-instances`, `oss-ls`, `ack-clusters`, `rds-instances`, `fc-functions` | Read-only; OSS through `ossutil` when installed. |
| `huawei-whoami` | `DefaultHuaweiSkills.cs` | `[hcloud]` | `profile`, `regions` | |
| `huawei-infra` | same | `[hcloud]` | `ecs-servers`, `obs-ls`, `cce-clusters`, `rds-instances` | Read-only. |
| `ibm-whoami` | `DefaultIbmCloudSkills.cs` | `[ibmcloud]` | `target`, `account` | |
| `ibm-infra` | same | `[ibmcloud]` | `vpc-instances`, `ks-clusters`, `code-engine-apps`, `cos-buckets`, `code-engine-deploy` | Deploy behind the guard. |
| `linode-whoami` | `DefaultLinodeSkills.cs` | `[linode-cli]` | `profile`, `account` | Linode is now Akamai Connected Cloud; the CLI is unchanged. |
| `linode-infra` | same | `[linode-cli]` | `instances`, `lke-clusters`, `object-storage`, `volumes` | Read-only; LKE hands off to the Kubernetes skills. |

**Cross-cloud infrastructure as code** (`DefaultIacSkills.cs`):

| Id | Mut. | Gate | Commands | Notes |
|---|:---:|---|---|---|
| `terraform` | yes | `requiresTools: [terraform]` or `[tofu]`, `appliesTo: ["*.tf", "**/*.tf"]` | `fmt`, `validate`, `init`, `plan`, `apply`, `state-list`, `output` | Works with OpenTofu. `plan` writes a plan file; `apply` only applies a saved plan file, never a fresh one, and is behind the production guard (matched against the workspace name). No `destroy`. |
| `pulumi` | yes | `requiresTools: [pulumi]`, `appliesTo: [Pulumi.yaml]` | `whoami`, `stack`, `preview`, `up`, `outputs` | `up` runs only after a `preview` in the same session and is behind the guard. |

Together 2.6 and 2.7 add 50 skills (12 in 2.6, 38 in 2.7), almost all hidden unless the matching CLI or files are present.

- [x] Harness: `requiresTools` (`ToolPresenceCache` scans PATH and PATHEXT, caches per refresh interval, and accepts `a|b` alternatives; `SkillRuntime.Tools` is replaceable for tests) and `settings.skillProdPattern` (validated as a regex, passed to every skill run as `MUX_SKILL_PROD_PATTERN` through the new `SkillExecutor.DefaultEnvironment`). **Changed:** the guard lives in the shared `resources/mux-skill.ps1` (`Assert-MuxNotProduction`, `Split-MuxConfirm`, `Get-MuxTarget`) rather than a separate `guard.ps1`, and a refusal exits **3**, a code of its own, so it cannot be confused with "tool missing" (2). `Get-MuxTarget` prints the target first, doubles as the authentication check (exit 2 with the login command), and never runs a login flow.
- [x] Tests. **Changed:** instead of fake CLI scripts on a temp PATH (shell scripts behave differently across Windows, macOS, and Linux), every case runs in dry-run mode and sets the target name through `MUX_SKILL_DRY_RUN_TARGET`, which exercises the same guard code with no credentials. The cases live in `ToolchainSkillsSuite` rather than a separate `CloudSkillsSuite`: guard refusal on production contexts, profiles, workspaces, and image names; passing with the exact `--confirm` name and failing with a different one; a custom `skillProdPattern`; previews by default (`s3-sync --dryrun`, `cdk diff`, `terraform plan` to a saved file, `apply` of that file only, `pulumi preview`, Cloud Run `--no-traffic`, Heat `--dry-run`); `compose down` without volumes; bounded logs; secret listing by name; and a scan of every infrastructure command for delete, destroy, terminate, prune, secret-value, and decryption terms.
- [x] Live runs stay manual: there is no CI account for any provider, and none should be added for this.
- **Other changes from the tables above, with reasons:** the AWS guard matches the profile name only, not an instance `Environment` tag (that needs an extra API call per command and tag conventions vary); Azure `webapp-logs` became `webapp-show` and Netlify `logs` became `sites`, Cloudflare `tail` became `deployments` (those log commands stream without end, which a skill with a timeout cannot bound); `openstack-whoami project` folded into `token`, which already shows the project; Cloud Run deploys also tag the new revision `candidate` so it has a test URL; minikube is also gated on the `minikube` CLI; Alibaba, Huawei, IBM, and Linode share `DefaultRegionalCloudSkills.cs` instead of four files, and dockerfile-lint runs without the Docker CLI. `project-detect` now suggests the container, Kubernetes, IaC, and platform skills it finds.

### 2.8 Go and Rust (row 21b) (done)

Not requested by name, but Go and Rust are each common enough that their absence would read as a hole next to C++ and Java. Each skill is small because the toolchains are already uniform.

- [x] `DefaultGoSkills.cs`, `appliesTo: [go.mod]`: `go-build` (`build`, `vet`), `go-test` (`all`, `filter`, `race`), `go-lint` (`check` with staticcheck or golangci-lint when installed), `go-mod` (`tidy`, `outdated`).
- [x] `DefaultRustSkills.cs`, `appliesTo: [Cargo.toml]`: `cargo-build` (`debug`, `release`), `cargo-test` (`all`, `filter`), `cargo-clippy` (`check`, `fix`), `cargo-fmt` (`apply`, `verify`).

### Phase 2 tasks

- [x] Add the category classes above (2.0 through 2.8), one class per file. A small `ToolchainSkillFactory` (setup block, tags, gates, exit-code note) keeps the 2.6 and 2.7 families short. **Changed in 2.3:** `react-lint-hooks` captures ESLint's unix-format output and keeps only `react-hooks/` and `jsx-a11y/` findings, rather than configuring ESLint to run only those rules, so it works with any existing ESLint config. The JavaScript test-runner selection moved into the shared helper (`Invoke-MuxJsTestRunner`) so React reuses it.
- [x] Register them in `DefaultSkillLibrary.Definitions()`.
- [x] Fixture projects: manifests and lockfiles only, no dependencies installed. **Changed:** fixtures are generated by the test code in temp directories (one small lambda per case) instead of checked into `src/Test.Shared/Fixtures/projects/`, which keeps each case's inputs next to its assertion and avoids committing lockfiles that tooling might try to act on. Done: npm, pnpm, yarn classic and Berry, bun, packageManager field, pnpm workspace package, Vitest, Jest, Mocha-free node --test, Biome, Prettier, uv, poetry with and without a lockfile, pipenv, pip with and without .venv, pyright, and non-projects.
- [x] `ToolchainSkillsSuite` (named for what it covers): runs each case with `MUX_SKILL_DRY_RUN=1` and asserts the printed command and exit code, so detection is tested on every platform without Node, Python, a JDK, or a compiler in CI; cases are skipped when `pwsh` is not on PATH. It also checks that seeding writes the helper and the prelude, and runs `project-detect json` for real against a mixed repository. 87 cases covering 2.0 through 2.8, including real (non-dry-run) scaffolding by `react-new-component` and `java-new-class` and their refusal to overwrite. A manual check ran `js-test all` for real against a `node --test` project: exit 0 when passing and exit 1 when a test fails.
- [ ] `ToolchainLiveSuite`: opt-in (skipped unless `MUX_TEST_LIVE_TOOLCHAINS=1`), runs the real commands against fixtures where the toolchain is installed. Not built yet; manual live checks so far: `js-test all` against a `node --test` project (exit 0 passing, exit 1 failing), and `project-detect` on the mux repository.
- [x] `appliesTo` and `requiresTools` cases in `SkillListingSuite` (gated skills hidden and counted in the footer, shown once the file or CLI exists, alternatives, `all` mode ignoring gates, and gating with project skills disabled, which was broken in Phase 1 and is fixed).

---

## Phase 3: Review and agent playbooks (done)

**Status:** done. Nine new default skills, for 148 in the library. Deviations from the tables below: `code-review` also has a `file <path>` command (used by the VS Code extension); `security-review` scans untracked new files as well as the diff and runs a masked secret scan from the shared helper instead of calling `git-secret-scan`; `pr-comments list` accepts `--from-file <json>` so it can be tested and used offline; `init` and `explain-codebase` are not gated on git, so they work in any folder; diffs are cut at `MUX_SKILL_DIFF_MAX_BYTES` (default 200000). The full build has no errors or warnings, and all runners pass on net8.0 and net10.0 (Touchstone 1145 cases: 1138 passed, 7 skipped by design; xUnit and NUnit 1139 each). The VS Code extension lints clean and its 57 unit tests pass.

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

- [x] Add both category classes (`DefaultReviewSkills.cs`, `DefaultAgentPlaybookSkills.cs`) and a git section in `mux-skill.ps1` (default branch, merge base, capped output, untracked files, masked secret findings, manifest audit hints, review threads).
- [x] Remove `review file` duplication in the VS Code extension: **Review current file** sends `/code-review file <path>` through `POST /v1.0/api/skills/expand` and falls back to the old prompt when the skill is missing. Unknown `/name` commands in the panel now expand as skills too.
- [x] `ReviewSkillsSuite` (49 cases) against real temp git repositories (`Test.Shared/Support/GitFixture.cs`): every `code-review` mode and its invalid-input errors, capping, secret findings (positive, removed-line, and clean cases), manifest hints, `pr-comments` from JSON and dry runs, `test-gap-review`, `init` and `explain-codebase`, and a `git-bisect` run that finds a planted bad commit and restores HEAD, plus refusals for dirty trees, unknown refs, and a bisect already in progress. Git and `gh` gating is covered.
- [x] The Phase 1 deferral: every default playbook body is at least 400 characters (`PlaybookBodiesAreSubstantial`).
- [x] Prompt-quality check (not a gate): `mux print "/code-review uncommitted"` with `homedns-qwen3-coder-30b` on three seeded-bug fixtures. All three planted bugs were found and reported at high severity in the skill's format: a JavaScript off-by-one (`i <= items.length`), an inverted Python authorization check (`!=` for `==`), and a C# percentage divisor (`/ 10m` for `/ 100m`). Two of the runs needed a retry after HTTP 502 errors from the proxy.

---

## Phase 4: Loops (done)

**Status:** done. Four loop skills (library: 152) and the harness `/loop`. Deviations from the plan below: the scheduler is pure state plus an injectable clock (`TimeProvider`); the shell fires a due iteration through the same `RunTurn` path as a typed prompt (a `JobManager` job, so approvals, the write lease, transcripts, and prompt-submit hooks are identical), and only when no turn is running and nothing is queued. The `schedule_next` minimum delay is `loopMinIntervalSeconds` rather than a fixed 30. `mux print --loop` takes an interval or `self`, plus `--loop-max`. Extra types: `LoopCommand` (the `/loop` parser), `LoopDecision`, `LoopToolProvider`, and `LoopDriver` (the headless runner). A failed or cancelled iteration pauses its loop. `fix-until-green check` takes `all|build|test`; `ci-watch` commands accept `--from-file` for offline use and testing; `flaky-test-hunt run` also takes `-- <command>` for runners it does not detect. Default skill commands can now set `timeoutMs`, and the four waiting commands use 30 minutes. The full build has no errors or warnings, and the loop suites (64 cases) pass.

"Loop" covers two different things, and both are in scope. The first is a skill-level loop: keep doing something until a condition holds, inside one turn. The second is a harness-level loop: re-submit a prompt on a schedule across many turns, which is what Claude Code's `/loop` does. The first is pure data and ships with this phase's skills. The second needs a scheduler in `Mux.Core/Jobs`.

### 4.1 Loop skills: `DefaultLoopSkills.cs`

| Id | Mut. | Commands | Notes |
|---|:---:|---|---|
| `loop-until` (done) | yes | `run` | `run <maxAttempts> <intervalSeconds> <command...>` re-runs a command until it exits 0 or attempts run out, printing each attempt's exit code and the last attempt's output. Bounded: `maxAttempts` max 100, interval max 600. Command timeout is the skill's `timeoutMs`, which this skill sets to 30 minutes. |
| `fix-until-green` (done) | yes | `check` | `check` runs the detected build and then the detected tests once and prints a compact pass or fail summary. Body: run `check`; if red, fix the first failure only, then run `check` again; stop after the iteration budget (default 5, `$ARGUMENTS` overrides) or when green; report what changed per iteration. Refuses to edit test assertions to make them pass unless the user said to. |
| `ci-watch` (done) | no | `status`, `watch`, `failed-logs` | `gh run list` for the current branch, `gh run watch` with a timeout, and the logs of failed jobs only, trimmed to the failing step. |
| `flaky-test-hunt` (done) | no | `run` | `run <count> <filter>` runs one test filter N times through the detected runner and reports pass and fail counts with the first failing output. |

### 4.2 `/loop` in the harness (row 23)

- [x] `src/Mux.Core/Jobs/LoopScheduler.cs`: owns recurring prompts for one session. Each loop has an id, prompt text, interval (fixed) or self-paced mode, an iteration cap, a stop condition, and a cancellation token linked to the session. Fires by enqueueing a normal job through `JobManager`, so the write lease, approvals, and transcripts all behave as they do for typed prompts.
- [x] `src/Mux.Core/Jobs/LoopDefinition.cs` and `src/Mux.Core/Jobs/LoopStatusEnum.cs`, one type per file.
- [x] Self-paced mode: the model ends a loop iteration by calling a new `schedule_next` tool with a delay in seconds (min 30, max 3600) and a one-line reason, or `stop: true`. Without a call, the loop stops. Prevents a runaway loop on a model that never stops.
- [x] Settings: `loopMaxIterations` (default 50, min 1, max 1000), `loopMinIntervalSeconds` (default 30).
- [x] TUI: `/loop 5m <prompt>`, `/loop <prompt>` (self-paced), `/loops` (list and cancel). Sidebar shows active loops with the next fire time.
- [x] `mux print --loop 5m` for headless use, which exits when the loop stops.
- [x] Persist active loops with the session so `/sessions` resume restores them paused, not running.
- [x] Tests: `LoopSchedulerSuite` with an injectable clock (fixed interval, self-paced, iteration cap, cancel mid-run, resume-paused, overlap prevention when an iteration outlasts the interval).
- [x] Tests: `LoopSkillsSuite` runs the four skills for real (retries with a counter script, a flaky command, real `npm` scripts when Node is installed, saved `gh` responses) and in dry runs for detection across toolchains; `LoopSchedulerSuite` adds the parser, the `schedule_next` tool, the driver, settings, session save and resume, the sidebar line, the tool binder, the terminal `/loop` and `/loops` commands through a headless shell, and `mux print --loop` end to end against a mock model that calls `schedule_next`.

---

## Phase 5: Hooks and background processes (done)

**Status:** done. Deviations from the tasks below:
- **Hooks (5.1):**
  - A new `HookEvent` agent event (jsonl `hook`) reports hook outcomes and warnings; no existing event fit.
  - `matcher` also accepts `|` alternatives.
  - Hook processes start only when a matching hook exists.
  - A timed-out or unstartable hook never blocks or continues.
  - Wiring goes through `AgentLoopOptions.Hooks` on all four surfaces (terminal, `mux print`, desktop, server).
  - Tests live in a new `ToolHooksSuite` rather than `PluginSuite`.
- **Background processes (5.2):**
  - The tools are an `IExternalToolProvider` (`Mux.Core/Processes/BackgroundProcessToolProvider.cs`) over a per-session `BackgroundProcessRegistry`, not a built-in tool, because built-in tools are stateless.
  - `process_start` can wait for a ready line in the same call.
  - Output is ANSI-stripped and limited by `backgroundProcessOutputBytes`.
  - The desktop app gets a `/processes` command shared by all tabs instead of a separate panel.
  - The REST server owns one registry for its web chats.
  - `mux print` kills its processes when the run ends.

### 5.1 Tool-level hooks (row 17)

- [x] Add `PreToolUse`, `PostToolUse`, and `Stop` to `HookEventEnum`, with wire names `pre-tool-use`, `post-tool-use`, `stop`. Move `HookEventEnumConverter` into its own file while touching it (the current file holds two classes, which `CODE_STYLE.md` disallows).
- [x] `HookDefinition` gains an optional `matcher` (tool-name glob, same syntax as `--allow-tools`).
- [x] `pre-tool-use` hooks receive the tool name and arguments as JSON on stdin. Exit 0 continues; exit 2 blocks the call and returns the hook's stderr to the model as the tool result; any other nonzero logs a warning and continues. This matches Claude Code's contract, so hooks written for it port with only a config change.
- [x] `post-tool-use` receives the result too and can append text to the tool result via stdout. `stop` fires when a run completes and can request one more turn via exit 2 (bounded to 3 per run).
- [x] Wire through `AgentLoop` at the existing `ToolCallProposedEvent` and `ToolCallCompletedEvent` points, after approval for `pre-tool-use`.
- [x] Tests in `PluginSuite`: matcher, block, append, stop re-entry bound, hook timeout.

### 5.2 Background processes (row 18)

React dev servers, `docker compose up`, and watch-mode test runners do not fit a foreground tool with a timeout.

- [x] `src/Mux.Core/Tools/Tools/BackgroundProcessTool.cs` exposing `process_start` (returns an id), `process_output` (new output since last read, with an optional regex wait and timeout), `process_list`, and `process_stop`.
- [x] `src/Mux.Core/Tools/BackgroundProcessRegistry.cs`: per-session ownership, ring-buffered output (configurable, default 1 MB per process), kill on session end, max concurrent processes setting (default 8).
- [x] Mutation classification: `process_start` and `process_stop` are mutating and take the approval path; `process_output` and `process_list` are read-only.
- [x] Sidebar and `/processes` in the TUI; a processes panel in desktop.
- [x] Add a `react-dev-server` playbook to `DefaultReactSkills` once this lands: start the detected dev script, wait for the "ready" line, report the URL.
- [x] Tests: start, read, regex wait, stop, session-end cleanup, output cap. `BackgroundProcessSuite` (20 cases) runs real processes: pattern waits, timeouts, exit codes, the output cap, the concurrency limit, dispose killing every tree, ANSI stripping, the four tools and their errors, settings, the sidebar line, `/processes` in a headless terminal, `mux print` killing its processes at exit, and `react-dev-server` detection for Vite, Next.js (pnpm), and Create React App plus its refusals. `ToolHooksSuite` (13 cases) runs real hook processes against a mock model.

---

## Phase 6: Interaction gaps (done)

**Status:** done. Deviations are listed with each item.

- [x] **`@file` mentions (row 19).** `@path`, `@folder/`, and `@"path with spaces"` attach files (large ones as structural maps) in a delimited block, capped by `fileMentionMaxBytes`; paths outside the working directory are refused. The terminal completes paths in the footer (Tab or Enter accepts, Esc dismisses); `mux print`, desktop, the web dashboard, and VS Code resolve mentions (the last two server-side). New `GET /v1.0/api/files/complete`. Deviations: no completion popup on desktop, web, or VS Code yet; terminal suggestions render in the footer. Tests: `FileMentions` (16).
- [x] **Persistent memory (row 24).** Markdown files under `~/.mux/memory/<project-key>/` and `~/.mux/memory/global/` with a `MEMORY.md` index; `remember`, `forget` (mutating), and `recall` (read-only) tools; a prompt section capped by `memoryMaxBytes`; `#` and `#global` quick-add in the terminal and desktop; `/memory`; `mux memory`; `GET`/`POST`/`DELETE /v1.0/api/memory`. Deviations: `/memory clear` confirms with `--yes`; no desktop edit, dashboard view, or VS Code UI; a new `IPromptSectionProvider` lets tool providers add prompt sections. Tests: `Memory` (16).
- [x] **Plan mode (row 25).** `/plan`, `/plan <prompt>`, and Shift+Tab (normal, auto-approve, plan) in the terminal; plan turns are read-only and end with `exit_plan`; approve, approve with auto-accept, or keep planning with feedback; the approved plan runs next with its steps as the task list. `mux print --plan`, desktop `/plan` with a review card, and the chat API's `planMode` flag with a `plan` event. Deviations: steps become the task list only in the terminal (desktop and server put them in the execution prompt); the mode shows in the footer, not the sidebar; web and VS Code do not render the `plan` event yet. Tests: `PlanMode` (11).
- [x] **Ask-the-user tool (row 26).** `ask_user` with 2 to 4 options, multi-select, and a free-text Other; a pop-up in the terminal and a dialog on desktop; elsewhere the model is told to choose a sensible default and say so. `ask_user` and `exit_plan` never need tool approval. Deviations: web and VS Code get the no-user default (no question channel exists on the server yet); the desktop dialogs are not localized. Tests: `AskUser` (7).

---

## Phase 7: Larger items (separate plans)

- [x] Worktree isolation (row 28): `isolation: worktree` for subagents and Core jobs, `mux worktree list|prune|remove`, and `/worktrees`. Done; see `archive/WORKTREE_ISOLATION_PLAN.md`. Tests: `WorktreeIsolation` (20).
- [x] MCP server mode (row 29): `mux mcp serve` over stdio or Streamable HTTP on Voltaic 2.2.1, with `run`, `list_sessions`, `get_session`, `list_endpoints`, `list_skills`, and `run_skill` (opt-in). Done; see `MCP_SERVER_PLAN.md`. Tests: `McpServer` (17).

Dropped on 2026-10-08: image input (row 27), custom status line (row 30), output styles (row 31), scheduled runs (row 32), and notebook editing (row 33).

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
| `DefaultReactSkills` (done) | 0 | 6 (+1 in Phase 5) | 7 |
| `DefaultJavaSkills` | 0 | 6 | 6 |
| `DefaultCppSkills` | 0 | 6 | 6 |
| `DefaultGoSkills` | 0 | 4 | 4 |
| `DefaultRustSkills` | 0 | 4 | 4 |
| `DefaultContainerSkills` | 0 | 4 | 4 |
| `DefaultKubernetesSkills` | 0 | 6 | 6 |
| `DefaultOpenStackSkills` | 0 | 3 | 3 |
| `DefaultAwsSkills` | 0 | 8 | 8 |
| `DefaultAzureSkills` | 0 | 6 | 6 |
| `DefaultGcpSkills` | 0 | 6 | 6 |
| `DefaultDigitalOceanSkills` | 0 | 3 | 3 |
| `DefaultRackspaceSkills` | 0 | 1 | 1 |
| `DefaultEdgePlatformSkills` (Vercel, Netlify, Cloudflare, fly.io) | 0 | 4 | 4 |
| `DefaultRegionalCloudSkills` (Alibaba, Huawei, IBM Cloud, Linode) | 0 | 8 | 8 |
| `DefaultIacSkills` (Terraform, Pulumi) | 0 | 2 | 2 |
| `DefaultReviewSkills` (done) | 0 | 5 | 5 |
| `DefaultAgentPlaybookSkills` (done) | 0 | 4 | 4 |
| `DefaultLoopSkills` (done) | 0 | 4 | 4 |
| **Total** | **46** | **107** | **153** |

The shipped library has 153 skills, one more than first planned. Existing users receive the new defaults on their next startup through `SeedNewInto`; nothing they have edited or deleted is touched. With relevance gating on, a typical single-language repository lists about 45 skills (the ungated ones, its own family, and the cloud skills for CLIs actually installed) instead of 153.

Done: `new-tool` and `new-touchstone-suite` scaffold mux's own `IToolExecutor` and Touchstone types, so they now carry `appliesTo: [src/Mux.Core/Mux.Core.csproj]` and are listed only inside the mux repository (test `MuxOnlyScaffoldsGated`).

---

## Documentation

- [~] `docs/SKILLS_AUTHORING.md`: playbook skills, hybrids, `appliesTo`, `userInvocable`, `argumentHint`, `$ARGUMENTS`, project scopes and the trust gate, and Claude-format compatibility are documented (Phase 1); the exit-code convention, `MUX_SKILL_DRY_RUN`, the production guard (Phase 2), and the review helpers with `MUX_SKILL_DIFF_MAX_BYTES` (Phase 3) are too.
- [~] `docs/USAGE.md`: invoking skills by name, project skills and trust, the listing mode, and project instruction files are documented (Phase 1), and so are the review, debugging, and codebase skills (Phase 3). Phase 4 added `/loop`, `/loops`, `mux print --loop`, and the loop skills; Phase 5 added tool hooks and background processes. Phase 6 added `@` mentions, memory, `/plan`, and `ask_user`.
- [~] `docs/CONFIG.md`: the Phase 1 settings and files (`trusted-projects.json`, `MUX.md`), `skillProdPattern` (Phase 2), and `loopMaxIterations` and `loopMinIntervalSeconds` (Phase 4), and the hook events and `backgroundProcess*` settings (Phase 5) are documented. Later phases add theirs.
- [~] `docs/REST_API.md` and the Postman collection: `GET /v1.0/api/context/instructions` and `POST /v1.0/api/skills/expand` are documented, both in a new Postman **Context** folder and in the **Skills** folder, with a `workingDirectory` variable. Loop and process routes come later.
- [~] `README.md`: project instruction files and slash invocation are in Highlights, and the two new flags are in the options table. The skills paragraph names the 148-skill library and its families (Phase 3).
- [~] `CHANGELOG.md`: Phases 1 through 3 are recorded under `Unreleased`, and the VS Code extension's `CHANGELOG.md` records the review routing. No version number was changed.
- [x] `docs/DESKTOP.md`: `/<skill>`, `/instructions`, and `/trust` (Phase 1).
- [x] `docs/VSCODE.md`: **Review current file** runs `code-review`, and unknown `/name` commands expand as skills (Phase 3).
- [ ] Final pass over every new doc and every skill body for em-dashes and the phrasing rules in `WRITING_DOCUMENTS.md`.

## Testing summary

Every phase adds Touchstone suites in `src/Test.Shared/Suites/` and registers them in `MuxSuites.cs`, so they run under `Test.Automated`, `Test.Xunit`, and `Test.Nunit` on net8.0 and net10.0. The CI step `mux skill validate` already gates the default library and will cover the 106 new skills with no change. Toolchain behavior is tested through dry-run detection on every platform and through live runs only where the toolchain is present, so CI does not need Node, Python, a JDK, CMake, Go, Rust, Docker, kubectl, or any cloud CLI installed to stay green. The full build must be free of errors and warnings before each phase is marked done.

## Order of work

Phase 1 is done. It was built in the order 1.2, 1.1, 1.4, 1.3, 1.5: the playbook builder unblocks every later skill, instructions are the largest single parity gain, invocation makes skills feel native, and scoping plus gating matter once the library actually grows. Then Phase 3 before Phase 2, because `code-review` and `init` are what a Claude Code or Codex user tries in their first five minutes, and the language families can land one per PR afterward. Phases 4 through 6 are independent of each other and can run in parallel once Phase 1 is in.
