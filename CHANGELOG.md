# Changelog

All notable changes to mux are documented here.

## 1.2.0

### Added

- **Docker integration tests.** An opt-in `DockerServices` suite (`--docker`, or `MUX_TEST_DOCKER=1`) runs
  the database and audit skills against real PostgreSQL, MySQL, MariaDB, SQL Server, Oracle, MongoDB, Redis,
  Neo4j, Cassandra, and LiteGraph servers and real npm audit, pip-audit, and osv-scanner output, each in a
  throwaway container that is removed afterwards. Client calls reach the servers through shims into the
  containers, so no database client is needed on the host.
- **Live skill-selection tests.** An opt-in `SkillSelectionLive` suite asks a real model which skill it would
  use for the built-in prompts and fails below a floor. Test.Automated takes `--llm-endpoint`, `--llm-model`,
  `--llm-adapter`, `--llm-api-key`, `--llm-floor`, `--llm-cases`, `--llm-report`, and `--llm-timeout`; the
  xUnit and NUnit runners read the matching `MUX_TEST_LLM_*` variables.
- `scripts/common/generate-builtin-skills.py` (with `generate-builtin-skills` wrappers per OS) regenerates
  BUILTIN_SKILLS.md, and `--check` fails when it is stale.
- **`ansible` and `bicep` skills.** Ansible lints playbooks, checks syntax, previews with `--check --diff`, and
  runs them with `apply` refused for a production-looking limit or inventory unless confirmed. Bicep builds and
  lints files, previews a resource-group deployment with what-if, and deploys behind the same guard. Neither
  offers a destroy command. `aws-deploy` gains `cfn-validate` and `cfn-lint` for CloudFormation templates.
- **`openapi` and `openapi-client` skills.** `openapi lint` (Redocly or Spectral), `openapi diff` against the
  base branch and `openapi breaking <old> <new>` (oasdiff, exits 1 on breaking changes), and `openapi-client
  generate <generator> <dir>` (openapi-generator, into a relative folder only).
- **`web-framework` and `storybook` skills.** `web-framework` detects Next.js, Nuxt, SvelteKit, Angular, Astro,
  Remix, Svelte, Vue, or Vite and runs build, lint, and test through the project's scripts or the framework's
  tools (tests run once, never in watch mode), describes the dev server for `process_start`, and compares
  declared and installed versions. `storybook` builds Storybook, runs story tests, and describes its dev server.
- **Runtime diagnosis skills.** `log-triage summarize <file>` groups a log's errors by message with ids,
  numbers, and paths masked, and shows each group's count, line range, first line, and stack trace (.NET,
  Java, Python tracebacks keyed by their final exception, Node, Go, and JSON log entries). `port-inspect
  [port]` shows what is listening and which process owns it. `bench` times one command or compares two
  (hyperfine, or a built-in timing loop), runs a BenchmarkDotNet project, or runs a k6 script.
- **Upgrade skills.** `dotnet-upgrade`, `node-upgrade`, and `py-upgrade` find every place a project pins its
  runtime version (project files and global.json; .nvmrc, package.json engines; requires-python, ruff and mypy
  targets, .python-version; plus CI workflows and Dockerfiles). `plan` lists the exact edits and changes
  nothing, `apply` makes only those edits (keeping BOMs and line endings), and `dotnet-upgrade apply` builds
  afterwards. Version matrices, classifiers, and framework packages are reported as notes.
- **Ruby, PHP, Swift, Android, and Flutter skills.** `ruby-deps`, `ruby-test` (RSpec, rails test, or rake
  test), `ruby-lint` (RuboCop); `php-deps` (install, outdated, `composer audit`), `php-test` (Pest or PHPUnit),
  `php-lint` (PHPStan, PHP-CS-Fixer); `swift-build` and `swift-test` (Swift packages or Xcode schemes),
  `swift-format`; `android-build`, `android-test`, `android-lint` through the Gradle wrapper; and
  `flutter-analyze`, `flutter-test`, `flutter-build` (plain Dart packages use `dart`). A new `mobile` category
  holds the Swift, Android, and Flutter skills on every surface.
- **Database skills.** `db-migrate` shows status, previews pending migrations as SQL, and applies them for EF
  Core, Prisma, Alembic, Django, Rails, Flyway, and golang-migrate, with `apply` behind the production guard.
  Eleven read-only platform skills connect, list, describe, and query: `sql-sqlite`, `sql-postgres`,
  `sql-mysql` (MySQL and MariaDB), `sql-sqlserver` (SQL Server and Azure SQL), `sql-oracle`, `nosql-mongodb`,
  `nosql-redis`, `nosql-dynamodb`, `nosql-cassandra`, `graph-neo4j`, and `graph-litegraph` (LiteGraph's REST
  API). Each blocks writes the way its platform allows (read-only sessions or transactions, rollbacks, read
  access mode, read-only operations), the SQL skills accept only read statements one at a time, and connection
  strings come from named environment variables and are never shown.
- **`deps-audit` skill.** Finds every ecosystem under the repository root and runs its vulnerability auditor
  (npm, pnpm, or yarn 1 audit; pip-audit; cargo-audit; `dotnet list package --vulnerable`; govulncheck), or
  osv-scanner for all of them when it is installed, and prints one report: ecosystem, package, version,
  severity, advisory, and fixed version. Exits 1 at or above `--min-severity` (default high). A missing auditor
  is noted, not fatal, and `--from <tool>=<file>` replays saved JSON.
- **`sbom` skill.** Writes a CycloneDX JSON software bill of materials with syft (`sbom write [file]`).
- **`shell-lint` skill.** shellcheck for `.sh`, `.bash`, `.ksh`, and shebang scripts and PSScriptAnalyzer for
  `.ps1` and `.psm1`, across the repository, one path, or only the files changed on the branch.
- **Skill selection evaluation.** `mux skill eval [case-id] [--top n] [--cases file] [--output-format json]`
  lists skills for a temporary project exactly as the system prompt does and ranks them against a prompt with
  BM25 over the name and description. It reports gating mistakes (a skill missing where it belongs, or listed
  where it does not), the top-1 and top-3 rates, and near-duplicate descriptions. 120 built-in cases cover every
  default category; the test suites hold the default library to a top-3 floor of 97% and top-1 of 90%.
  `--live [--endpoint <name>]` asks a real model instead: one call per case with the real listing and skill
  tools, recording the skill named by its first `skill` or `run_skill` call. Nothing is executed.
- **`build-vscode.sh` / `build-vscode.bat`** in the repository root package the VS Code extension into
  `src/Mux.VSCode/mux-ai-<version>.vsix` (`npm ci`, then `vsce package`).
- **Project instruction files.** Every surface (terminal, `mux print`, desktop, and the REST server) loads
  `MUX.md`, `AGENTS.md`, or `CLAUDE.md` (the first found in each directory) from the repository root down to
  the working directory, plus a user-level `MUX.md` in the config directory, into the system prompt. Nearer
  files come last and win on conflict. New settings `projectInstructionsEnabled` (default `true`) and
  `projectInstructionsMaxBytes` (default 32768; the outermost files are dropped first when over the cap), a
  `--no-project-instructions` flag, a `section.projectInstructions` catalog prompt, terminal notices at startup
  and after `/cwd`, a desktop `/instructions` command, and `GET /v1.0/api/context/instructions`.
- **Playbook skills.** A skill may declare no commands; its body is a procedure the model follows.
  `DefaultSkillBuilder.Build(DefaultSkillDef)` builds playbooks, hybrids, and command skills from one data class.
- **Invoke a skill by name.** `/<skill> args` runs a skill in the terminal, the desktop app, the web dashboard,
  and `mux print`. `$ARGUMENTS` and `$1`..`$9` are substituted in prose (never inside code fences). Built-in
  commands win, then custom commands, then skills. New frontmatter fields `userInvocable` and `argumentHint`,
  and `POST /v1.0/api/skills/expand`.
- **Project skills and trust.** Skills under `.mux/skills`, `.claude/skills`, or `.agents/skills` in the
  repository load alongside user skills and shadow ones with the same id. Project skills with commands stay
  blocked until the project is trusted with `/trust` (terminal and desktop), `mux skill trust`, or
  `--trust-project-skills` for one run; decisions live in `~/.mux/trusted-projects.json`. The `/skills`
  inventory lists project skills with their state and a trust action. New settings `projectSkillsEnabled` and
  `projectSkillRoots`.
- **Claude Code skill compatibility.** Frontmatter keys match regardless of case, hyphens, and underscores
  (`allowed-tools`, `argument-hint`, `user-invocable`, `disable-model-invocation`), comma-separated scalars read
  as lists, YAML block scalars (`description: >`) are folded, and unrecognized fields become validation
  warnings instead of being silently dropped.
- **Relevance-gated skill listing.** Skills can declare `appliesTo` globs; in the default `relevant` mode
  (`skillListingMode`) a skill is listed in the system prompt only when a glob matches a project file, with a
  footer counting the rest. `skill` with the name `list` returns every available skill (`list` is now a
  reserved skill name).
- **Toolchain skills (Phase 2, in progress).** 16 new default skills, each gated with `appliesTo` so they are
  listed only in matching projects: `project-detect` (languages, package managers, test frameworks, CI, deploy
  files, and the skills that apply); JavaScript and TypeScript (`js-install`, `js-build`, `js-test`, `js-lint`,
  `js-typecheck`, `js-format`, `js-deps`, `js-scripts`), which detect npm, pnpm, yarn (classic and Berry), or bun
  and Vitest, Jest, Mocha, or `node --test`, ESLint or Biome, and Prettier or Biome; and Python (`py-env`,
  `py-install`, `py-test`, `py-lint`, `py-format`, `py-typecheck`, `py-deps`), which run inside the project's uv,
  poetry, pipenv, or `.venv` environment with pytest, Ruff (or flake8 and Black), and mypy or pyright. They share a
  seeded `resources/mux-skill.ps1` helper: exit 0/1/2 conventions, install hints, and `MUX_SKILL_DRY_RUN=1` dry
  runs. Default skills can now ship resource files, and `SkillExecutor` accepts per-run environment variables.
- **More toolchain skills (Phase 2).** React (`react-new-component`, `react-new-hook`, `react-test`,
  `react-build-analyze`, `react-lint-hooks`, `react-upgrade-check`), Java for Maven and Gradle with wrappers
  preferred (`java-build`, `java-test`, `java-format`, `java-lint`, `java-deps`, `java-new-class`), C and C++ for
  CMake (with presets), Meson, and Make (`cpp-configure`, `cpp-build`, `cpp-test`, `cpp-format`, `cpp-tidy`,
  `cpp-sanitize`), Go (`go-build`, `go-test`, `go-lint`, `go-mod`), and Rust (`cargo-build`, `cargo-test`,
  `cargo-clippy`, `cargo-fmt`). The library is now 88 skills; scaffolding commands never overwrite files.
- **Infrastructure and cloud skills (Phase 2).** 51 more default skills (library: 139), listed only when the
  project has matching files and, for clouds, the provider's CLI is installed (new `requiresTools` frontmatter,
  checked by scanning PATH). Containers and orchestration: `docker-build`, `docker-inspect`, `compose`,
  `dockerfile-lint`, `k8s-context`, `k8s-inspect`, `k8s-validate`, `k8s-apply`, `minikube`, `helm`, and
  `openstack-whoami`/`-inspect`/`-heat`. Clouds: eight AWS skills (identity, EC2 and Lambda, ECS/EKS/ECR, S3, RDS
  and DynamoDB, CloudFormation/SAM/CDK, logs/alarms/cost, SQS/SNS/secret names/Route 53/IAM), six each for Azure and
  Google Cloud, three for DigitalOcean, Rackspace, Vercel, Netlify, Cloudflare, fly.io, Alibaba Cloud, Huawei
  Cloud, IBM Cloud, and Linode, plus `terraform` (also OpenTofu) and `pulumi`. Every command prints its target
  first, changes preview by default, and a production guard (new `skillProdPattern` setting, default
  `prod|production|live`) refuses matching targets with exit 3 unless confirmed with `--confirm <name>`. No
  command deletes, destroys, or reads secret values. `project-detect` now suggests these families.
- **Review, debugging, and codebase skills (Phase 3).** Nine more default skills (library: 148).
  `code-review` reviews uncommitted changes (untracked files included), a branch against its base, one commit, a
  pull request, or one file, with `quick` and `deep` effort words and a fixed finding format
  (`[severity: high|medium|low] path:LINE`, `Failure:`, `Fix:`). `security-review` adds a masked secret scan of
  added lines and the audit command for each changed dependency manifest. `simplify` lists changed files for a
  behavior-preserving cleanup pass, `pr-comments` gathers a pull request's review threads (unresolved first) through
  `gh`, and `test-gap-review` pairs changed source files with changed tests. Playbooks `init` (surveys the project
  and drafts `AGENTS.md`), `debug` (reproduce, isolate, fix, verify), `git-bisect` (start, run, and reset, always
  restoring HEAD), and `explain-codebase` (a bounded tree map with entry points). Large diffs are cut at
  `MUX_SKILL_DIFF_MAX_BYTES` (default 200000) with a note. Git-based skills are listed only inside a repository,
  and `pr-comments` only when `gh` is installed.
- **Loops (Phase 4).** `/loop [interval] [--max N] <prompt>` re-runs a prompt across turns on a fixed interval
  (`30s`, `5m`, `1h30m`) or, with no interval, self-paced: the model ends each iteration with the new
  `schedule_next` tool (a delay, or `stop: true`), and an iteration that does not call it stops the loop. Every loop
  has an iteration cap (new settings `loopMaxIterations`, default 50, and `loopMinIntervalSeconds`, default 30).
  Iterations run as ordinary turns, only when the shell is idle, and never overlap; fire times missed while an
  iteration ran are skipped, and a failed or cancelled iteration pauses its loop. `/loops` lists, cancels, pauses,
  and resumes loops, the sidebar shows them with their next fire time, and active loops are saved with the session
  and restored paused. `mux print --loop <interval|self>` and `--loop-max` run a loop headlessly and exit when it
  stops. Four loop skills (library: 152): `loop-until` (retry a command until it exits 0), `fix-until-green` (detect
  and run the build and tests, then fix one failure at a time, never weakening tests), `ci-watch` (GitHub Actions
  status, waiting for a run, and failed-step logs), and `flaky-test-hunt` (run a test filter or command many times and
  report the failure rate with the first failing output). Default skill commands can set `timeoutMs`; the waiting
  commands use 30 minutes.
- **Tool-level hooks (Phase 5).** `pre-tool-use` runs after approval and before the tool (exit 2 blocks the call
  and returns the hook's stderr to the model), `post-tool-use` appends its stdout (or exit-2 stderr) to the tool
  result, and `stop` can make the model continue with exit 2 (at most 3 times per run). Payloads and exit codes
  follow Claude Code's contract (`hook_event_name`, `tool_name`, `tool_input`, `tool_response`, `stop_hook_active`),
  so hooks port by changing only the config. Hooks take an optional `matcher` (an `--allow-tools` glob with `|`
  alternatives), run on every surface, and treat other exit codes, timeouts, and missing commands as warnings. New
  jsonl event `hook` and error code `tool_call_blocked_by_hook`; `mux plugin list` and the hooks REST config show
  the matcher.
- **Background processes (Phase 5).** New tools `process_start` (returns an id at once, optionally waiting for a
  ready line), `process_output` (only new output since the last read, with an optional pattern wait),
  `process_list`, and `process_stop` (also `all`). Start and stop take the approval path; output is ANSI-stripped and
  bounded per process (new setting `backgroundProcessOutputBytes`, default 1 MB), at most
  `backgroundProcessMaxConcurrent` (default 8) run at once, and every process tree is killed when the terminal, a
  `mux print` run, the desktop app, or `mux serve` ends. `/processes` (alias `/ps`) in the terminal and desktop lists
  processes, shows output, stops, and clears them, and the terminal sidebar shows each one. New skill
  `react-dev-server` detects the dev script, framework, port, and ready line (library: 153).
- **`@file` mentions (Phase 6).** Type `@path`, `@folder/`, or `@"path with spaces"` to attach files to a prompt on
  every surface; large files arrive as structural maps, paths outside the working directory are refused, and the new
  `fileMentionMaxBytes` setting (default 256 KB) caps the total. The terminal composer completes paths (Tab or Enter
  accepts, Esc dismisses). New `GET /v1.0/api/files/complete`; chat routes resolve mentions server-side, so the web
  dashboard and VS Code get them with no client changes.
- **Persistent memory (Phase 6).** The model saves, searches, and deletes durable facts with `remember`, `recall`,
  and `forget`; every turn's prompt lists the project's and global memories within `memoryMaxBytes`, stored as
  Markdown under `~/.mux/memory/`. `#` at the start of a terminal or desktop prompt saves a memory without a model
  call (`#global` for every project). Also `/memory`, `mux memory list|show|add|delete`,
  `GET`/`POST`/`DELETE /v1.0/api/memory`, and the settings `memoryEnabled` and `memoryMaxBytes`.
- **Plan mode (Phase 6).** Shift+Tab cycles normal, auto-approve, and plan, or use `/plan` and `/plan <prompt>`. Plan
  turns are read-only and end with the model presenting a plan through the new `exit_plan` tool; approve it
  (optionally auto-accepting edits) or keep planning with feedback, and the approved plan runs next with its steps as
  the task list. Also `mux print --plan`, desktop `/plan`, and the chat API's `planMode` flag with a `plan` event.
- **Ask the user (Phase 6).** The model can ask a 2 to 4 option question mid-turn (multi-select and a free-text
  Other) with `ask_user`, shown as a pop-up in the terminal and a dialog on desktop; where no one can answer it is
  told to pick a sensible default and say so. `ask_user` and `exit_plan` never need tool approval.
- **MCP server.** `mux mcp serve` exposes mux as an MCP server over stdio (default) or Streamable HTTP
  (`--http <port>`, loopback by default, optional bearer key via `--api-key` or the new `mcpServeApiKey` setting),
  built on Voltaic. Tools: `run` (a headless turn returning the answer and a summary, with progress and cancellation,
  under an approval ceiling `--approval-policy deny|auto-safe|auto`, default `deny`), `list_sessions`,
  `get_session`, `list_endpoints` (no secrets), `list_skills`, and `run_skill` (with `--allow-skills`). See
  `archive/MCP_SERVER_PLAN.md`.
- **Worktree isolation.** Subagents (`"isolation": "worktree"`, or `isolation` on `spawn_subagent`) and Core jobs
  can run in their own git worktree on a `mux/<kind>/<name>` branch under `.git/mux-worktrees/`, without the shared
  write lease. Unchanged worktrees are removed; changed ones are committed onto their branch and reported (branch,
  commits, diff stat). Your branch, working tree, and stash are never touched. `mux worktree list|prune|remove` and
  `/worktrees` manage kept worktrees. See `archive/WORKTREE_ISOLATION_PLAN.md`.
- **Worktree management everywhere.** `GET /v1.0/api/worktrees`, `POST /v1.0/api/worktrees/prune`, and
  `DELETE /v1.0/api/worktrees?name=` (409 instead of losing work, 404 for unknown names) with OpenAPI docs and Postman
  requests; `/worktrees` in the desktop app; an Isolation choice in the desktop subagent editor (all 11 languages);
  and `scripts/<os>/worktrees.sh` / `worktrees.bat`.
- **MCP server follow-ups.** `run` now records usage telemetry like `mux print`. Over stdio, mux shortens Voltaic's
  per-message stderr log to the method, request id, and size so prompts and answers stay out of MCP client logs;
  `--log-messages` restores the full log. New `scripts/<os>/run-mcp-server.sh` / `.bat` builds quietly and serves a
  source checkout over stdio, ready for `claude mcp add`.
- **Skill categories.** Every skill has a category (`category:` in SKILL.md, the shipped default, or inferred from
  tags), overridable without editing SKILL.md (stored in `skills.json`). Shown and editable in the terminal
  (`/skills`, `/skills category`), the CLI (`mux skill category`, `mux skill categories`, `mux skill list
  --category`), the web dashboard, the desktop app, and VS Code (the Skills tree groups by category). New
  `PUT /v1.0/api/skills/category` and `GET /v1.0/api/skills/categories`; skill responses carry `Category`,
  `CategoryOverridden`, and `FileCategory`; MCP `list_skills` returns categories and takes a `category` filter.
- **Folder skills and `${SKILL_DIR}`.** Skills can bundle `scripts/`, `references/`, and `assets/`, referenced as
  `${SKILL_DIR}` (Claude-style aliases also work) in the `skill` tool output, `/skill` invocations, and `mux skill
  show`; the `skill` tool lists the bundled files, and commands get `SKILL_DIR` and `CLAUDE_SKILL_DIR`.
- **Skills from alirezarezvani/claude-skills (MIT).** Seventeen new bundled defaults (library then: 170): the Playwright
  family (`pw-init`, `pw-generate`, `pw-fix`, `pw-review`, `pw-coverage`, `pw-migrate`, `pw-report`), `a11y-audit`,
  `skill-security-auditor`, `security-guidance`, `skill-extract`, `ci-cd-pipeline-builder`, `performance-profiler`,
  `ship-gate`, `api-design-reviewer`, `tdd-guide`, and `handoff`, gated where they apply. Ten opt-in packs hold 166
  more (engineering, business, product, compliance, marketing, security, data, research, docs, productivity),
  installed and removed with `mux skill pack`, `/packs`, the dashboard, the desktop app, or
  `/v1.0/api/skills/packs`; your own skills are never replaced or removed. Existing skills absorb the best of the
  collection: `code-review` (blast radius, per-language rules, an adversarial pass), `security-review` (license
  checks, STRIDE threat modeling), `debug` (a five-phase feature-repair mode), `project-detect` (monorepo tooling),
  `terraform` and `dockerfile-lint` (review checklists and analyzers), and `explain-codebase`, `todo-scan`,
  `release-notes`, and `new-skill`. Notices are in `THIRD_PARTY_NOTICES.md`; the plan is `archive/SKILLS_TO_CONSIDER.md`.
- **Script commands for imported skills.** Every script-bearing imported skill exposes its scripts as `run_skill`
  commands (205 commands across 91 skills); `tdd-guide` and `aws-solution-architect` gained command-line entry
  points, and the autoresearch skills ship their evaluators. Script commands answer `MUX_SKILL_DRY_RUN` by reporting
  the command instead of running it, and exit codes pass through.
- **`BUILTIN_SKILLS.md`** lists every default skill by category and every optional pack skill.
- **`mux skill import <folder|git-url>`** brings in Claude-format skills, rewriting paths, Claude references, and
  dashes, adding category, source, and license, and flagging features mux does not support.
- **Skills in `mux print`.** Headless runs now discover skills, list them in the system prompt, and expose
  `skill` and `run_skill`, matching the interactive shell.

### Changed

- **The skills section of the system prompt tells the model when to use a skill.** It now says skills are tested
  procedures, to prefer a matching skill over answering from memory or improvising shell commands, and to act
  with a tool call rather than describe the steps. On 60 evaluation prompts against local models, the share
  where the model used an expected skill rose from 85% to 88% for gpt-oss:20b, 73% to 83% for qwen3:8b, 45% to
  48% for qwen3-coder:30b, and 8% to 17% for qwen2.5:7b. The default system prompt and the file-summary prompts
  no longer contain em-dashes.
- **Faster CI.** Every push runs the console runner as a parallel matrix of Linux and Windows by `net8.0` and
  `net10.0`, cancels superseded runs, caches NuGet packages, and checks that BUILTIN_SKILLS.md is current. The
  xUnit and NUnit adapters, which run the same suites, now run weekly and on demand, and the Docker
  integration suite runs in its own job on pushes to `main`.
- **Pack skills cleaned up.** 502 references to the upstream `/cs:` slash commands across 86 pack files, and the
  bundled `handoff` skill, now name the mux skill that does the job (`/exec-decide`, `/boardroom`, `run_skill
  handoff setup`, and so on); the few upstream commands mux never shipped are marked as such. Descriptions no
  longer open with a `/cs:` usage line, and nine that read like a persona ("World-class senior data scientist
  skill...", "Atlassian Jira expert for...") now say what the skill does. Shipped-skill checks keep both from
  coming back.
- The `dotnet-*` skills are listed only in .NET projects (a solution, project file, or `global.json`) instead
  of in every project. Eleven skill descriptions were rewritten to say what they do in the words people use
  (for example `git-secret-scan` now mentions API keys and tokens, and `py-format` says Python).
- **Startup splashes close by themselves.** The terminal splash closes after about 2.5 seconds (any key still
  closes it sooner; the first-run wizard follows it either way). The desktop shows the About window as a
  splash for about 2.5 seconds at launch (a click closes it sooner). The About window is 25% shorter (less top
  and bottom padding, same fonts and spacing), and no longer has the "Getting started" section or the
  "Diagnostics" heading.
- Relevance gating (`appliesTo`) now also applies when `projectSkillsEnabled` is false, and the listing prints the
  "more skills are installed" footer even when every skill is hidden.
- **Repository scripts moved under `scripts/`.** Windows `.bat` scripts now live in `scripts/windows/`, and the
  shell scripts in both `scripts/linux/` and `scripts/macos/` (for example `scripts/windows/install-tool.bat`,
  `./scripts/macos/run-desktop.sh`). Each script resolves the repository root from its own location, so they
  run from any directory.
- Seeded default skills use `name: description` command headings instead of an em-dash. Existing seeded
  copies are untouched.
- Switching prompt profiles in the terminal after `/cwd` now keeps the new working directory.

### Fixed

- `sql-mysql` (and every SQL skill) refuses `SELECT ... INTO OUTFILE` and `INTO DUMPFILE`, which write a file on
  the database server even inside a read-only transaction.
- Python bytecode no longer ships: seven committed `.pyc` files are gone, `__pycache__` is ignored and excluded
  from the embedded skill resources, and skill processes set `PYTHONDONTWRITEBYTECODE=1` (unless already set)
  so running a skill never writes into its folder.
- **Windows background processes.** `process_start` passed the command to `cmd.exe` with C-runtime quoting,
  which `cmd /c` does not understand, so commands containing quotes ran mangled. The command now goes through
  verbatim, as `run_process` passes it.
- **Python skill scripts on Windows.** Skill processes default to UTF-8 for Python (`PYTHONUTF8`,
  `PYTHONIOENCODING`, unless already set), so a script that prints a character outside the console code page
  (an arrow in its `--help` text) no longer crashes with `UnicodeEncodeError`.
- **Skill `source` paths on Windows.** Double-quoted frontmatter values now undo YAML's `\\` and `\"` escapes, so
  an imported skill's `source` reads back as `C:\Users\...` instead of with doubled backslashes.
- **Worktree isolation from a subdirectory on Windows** mapped the run into `src/lib` with a forward slash; the
  path now uses the platform separator.
- **`git-bisect run`** recognizes the `is the first 'bad' commit` wording that newer git versions print, instead
  of reporting that no commit was found.
- Desktop: the endpoint picker now shows the endpoint you select, and adding, editing, or deleting endpoints keeps
  the active endpoint selected, falling back to the default when it was deleted (the picker reused a stale label).
- Desktop: the (i) under each chat turn is a real button again; click it or press Enter or Space to open selectable
  details (time to first token, streaming, total, tokens, steps, tool calls, errors, context size, status).
- Skill editors on the desktop, web dashboard, terminal (Ctrl+T), and VS Code have a copy-to-clipboard control
  that copies the whole `SKILL.md`, unsaved edits included. The terminal uses the system clipboard command and falls
  back to OSC 52.
- MCP connection failures now say why on every surface: connection refused, host not found, TLS failure, timeout,
  the HTTP status with `Content-Type`, `WWW-Authenticate`, `Retry-After`, `Server`, and the response body, a JSON-RPC
  error, or a stdio command missing from PATH with its stderr. The first line is the cause and the details follow;
  credentials are never shown. The desktop Validate window shows copyable details and the servers list shows the
  error on hover; the terminal MCP manager shows the cause on each offline row and a "Show connection errors"
  action, and multi-line notices render one row per line. New `POST /v1.0/api/mcp-servers/validate`, with Validate
  actions in the web dashboard and the VS Code extension.
- Tray agent: Launch Terminal opens the mux terminal again. On macOS it ran `open -a Terminal mux`, which opened a
  file named `mux` instead of running it; it now locates the CLI (`MUX_CLI`, PATH, `~/.dotnet/tools`, beside the
  agent, or a checkout build) and opens it in a new Terminal window through a self-deleting `.command` script, with a
  login shell. Windows quotes the `start` title correctly and Linux tries seven terminal emulators. When no CLI is
  found the window explains how to install it.
- The `python` skill interpreter resolves `python3` (macOS and Linux) or the `py` launcher (Windows) when no
  `python` is on PATH, so Python skill commands work on systems that only ship `python3`.
- `mux skill pack show`, `/packs`, and the pack REST routes show real skill descriptions instead of `>-` (YAML
  block scalars are now read).
- MCP client: paginated `tools/list` results are followed (only the first page was registered), malformed tool
  entries are skipped instead of dropping the server, duplicate server names keep the first definition, a crashed
  stdio server no longer hides its diagnosis or breaks removal, and a stdio server's `env` entries are set only for its
  launch instead of leaking into mux and later servers. `PUT /v1.0/api/mcp-servers` rejects a body without `items`
  (which used to erase every server) and duplicate names; `DELETE` decodes encoded names.
- Web dashboard: the skill editor's SKILL.md body has its own copy icon in its top-right corner (the footer button
  remains).
- Desktop: the `/?` help table sizes its command column to the longest command instead of cutting commands off, and
  clicking a command that takes an argument (`/cwd <path>`) puts it in the composer to finish.
- `GET /v1.0/api/context/instructions` URL-decodes `workingDirectory`, so paths with spaces work.
- `new-tool` and `new-touchstone-suite` are listed only inside the mux repository (`appliesTo`).
- Web dashboard: the shared copy helper no longer hides the translation function, so copying without a button
  shows the "copied" toast instead of throwing.
- Skill helper: `Exit-MuxNotApplicable` writes its message with `Write-Host`, so the reason is shown even when the
  failing helper ran inside `@(...)` or an assignment.

### Tests

- MCP: 104 new cases in `McpClient`, `McpStdioClient`, `McpConfig`, `McpServerTools`, and `McpEndToEnd`, positive
  and negative, covering mux as an MCP client (HTTP and stdio, auth, pagination, crashes, malformed servers,
  configuration, REST routes, `mux print --mcp-config`, the terminal manager) and as an MCP server (every tool's
  validation, the approval ceiling matrix, serialized runs, cancellation, secret masking, HTTP auth, raw stdio
  protocol edge cases).
- Skills: `ScriptCommands1` to `ScriptCommands3` (141 cases: every script command declared, dry-run, and run with
  `--help`, plus negative cases), `SkillScriptRuntime` (Python resolution and script dry runs), and `ShippedSkills`
  (restored checks on all shipped skill content: counts, validity, categories, unique ids, no em-dashes, every
  `${SKILL_DIR}` reference resolving, packs, and embedded resources). `ImportedSkillsSuite` was registered twice and
  now runs once. The solution builds with no warnings in a full rebuild.
- Skills: `SkillCategories` (16) and `ImportedSkills` (52, including every imported skill validating with a
  taxonomy category, unique ids, no em-dashes, every `${SKILL_DIR}` reference resolving, packs, the importer's
  normalization rules, seeding, REST, and the terminal `/packs` flow).
- Phase 6 and the separate plans: `FileMentions` (16), `Memory` (16), `PlanMode` (11), `AskUser` (7), `McpServer`
  (17, including mux's own MCP client driving `mux mcp serve` over stdio and HTTP), and `WorktreeIsolation` (20,
  real temporary git repositories). Follow-ups add `WorktreeIsolation` REST route coverage (21 cases),
  `McpServer` log filtering, a raw stdio session that keeps prompts out of stderr, and run telemetry (19 cases), and a
  `SkillEditorCopy` check that the dashboard's inline JavaScript parses (`node --check`). `TerminalLaunch` (9) covers CLI location and the generated terminal scripts,
  including running the macOS script; `DefaultSkills` checks the scaffold gating.
- `BackgroundProcesses` (20 cases) runs real processes through the registry, the four tools, `/processes` in a
  headless terminal, and `mux print` (asserting the process is killed at exit), plus `react-dev-server` detection.
  `ToolHooks` (13 cases) runs real hook processes against a mock model: blocking, warnings, timeouts, payload fields,
  post-tool appends, the stop re-entry limit, matchers, and the no-hooks path.
- `LoopScheduler` and `LoopSkills`: 64 cases. The scheduler runs on a manual clock (fixed interval, self-paced
  decisions, iteration cap, cancel mid-run, pause and resume, restore paused, skipped fire times, no overlap), plus
  the `/loop` parser, the `schedule_next` tool's validation, the headless driver, settings clamping, session save and
  resume, the sidebar line, the terminal `/loop` and `/loops` commands in a headless shell, and `mux print --loop`
  end to end against a mock model that calls `schedule_next`. The loop skills run for real (a retrying counter
  script, a flaky command, real `npm` scripts when Node is installed, saved `gh` responses) and in dry runs for
  toolchain detection, with negative cases for every argument check.
- `Test.Automated` accepts `--suite <id>` (repeatable) to run only some suites.
- `ReviewSkills`: 48 cases that run the Phase 3 skills against real throwaway git repositories (`GitFixture`):
  every `code-review` mode and its invalid-input errors, diff capping, masked secret findings (and no finding for
  removed lines or clean changes), manifest audit hints, `pr-comments` from JSON fixtures and dry runs,
  `test-gap-review` pairing, `init` and `explain-codebase` surveys, a `git-bisect` run that finds a planted bad
  commit and restores HEAD, and checks that playbook bodies are substantial, review bodies define the output
  format, and git and `gh` gating hides the skills elsewhere. Skill test helpers moved to `SkillTestContext` and
  `SkillRunResult` in `Test.Shared/Support`.
- `ToolchainSkills`: 87 cases that dry-run the toolchain and infrastructure skills against generated fixture
  projects and assert the exact command and exit code, including production-guard refusals and confirmations,
  preview-by-default behavior, and bounded logs; real scaffolding runs; and a scan proving no infrastructure command
  deletes, destroys, or reads secrets (skipped when `pwsh` is not on PATH). `SkillListing` adds `requiresTools`,
  tool-presence caching, and production-pattern cases.
- New suites `ProjectInstructions`, `ProjectSkills`, `SkillInvocation`, and `SkillListing`, plus new
  `DefaultSkills` cases (playbook builder, no em-dashes, colon headings) and a `SkillCommand` case for
  `mux skill trust`.

## 1.1.1

### Changed

- **PolyPrompt 2.8.0 -> 3.1.0.** PolyPrompt 3 splits each provider client into one client per capability, so
  `LlmClient` now builds `OllamaCompletionClient`, `OpenAiCompletionClient`, `AnthropicCompletionClient`,
  `GeminiCompletionClient`, `AzureOpenAiCompletionClient`, `VertexAiCompletionClient`, and
  `BedrockCompletionClient`. The endpoint's model, max tokens, and reasoning effort now travel on
  `ToolChatRequest.Options` (the inline request fields were removed), and streaming usage is read from
  `TokenUsage`. No mux settings or behavior change, with one wire-level difference from PolyPrompt: Gemini
  (AI Studio) endpoints now send the API key in the `x-goog-api-key` header instead of the request URL, so it no
  longer appears in URLs or logs.
- **Voltaic 2.1.13 -> 2.2.1** (MCP client). No API changes; Voltaic now emits its own `Voltaic` meter and
  activity source.
- **Watson 7.2.1 -> 7.2.2** (REST server). Access-control denials return `403` instead of `500`, and IP matchers
  are disposed with their settings.
- **TUIKit 1.1.1 -> 1.2.1** (interactive UI). No API changes; TUIKit now emits its own `TUIKit` meter and
  activity source.
- **Touchstone 0.1.12 -> 0.2.0** (`Touchstone.Core`, `Touchstone.Cli`, `Touchstone.XunitAdapter`,
  `Touchstone.NunitAdapter`) for the test runners.

### Tests

- `LlmBridge.EndpointSettingsReachTheWire`: the endpoint's model and max tokens reach the OpenAI and Anthropic
  request bodies through the new `ToolChatRequest.Options`, and no system prompt is injected.
- `LlmBridge.GeminiApiKeySentAsHeader`: the Gemini API key is sent as `x-goog-api-key` and never in the URL. The
  local mock server now records request query strings.
- Automated (net8.0 and net10.0), xUnit, and NUnit runners all pass.

## 1.1.0

### Added

- **OpenTelemetry observability.** mux now emits metrics, traces, and logs an operator can watch in Grafana
  (Prometheus, Tempo, Loki) or any OTLP backend. See [TELEMETRY.md](TELEMETRY.md) for the full catalog.
  - `Mux.Core` emits on a BCL `Meter` and `ActivitySource` named `Mux` (no exporter dependency, near-zero cost
    when nothing listens): agent runs with per-stage timing (`llm`, `approval`, `tool`, `compaction`,
    `write_lease_wait`), iterations, errors by type, compactions; LLM requests by provider and operation with
    time-to-first-token, tokens, and retries; tool calls and approval decisions; subagents; outbound integrations
    (`llm`, `mcp`, `web_search`, `git`, `hook`) by service, operation, and outcome; the job pipeline (`queued` and
    `run` stages, outcomes, active/queued/capacity gauges, last-success timestamp); the workspace write lease; the
    usage-history writer queue; session store, run registry, and git checkpoints; build info and safe config
    gauges. Spans cover the same paths (`agent run`, `llm chat_stream`, `tool <name>`, `stage:*`, `mcp *`,
    `job`, ...) with explicit status, recorded exceptions, and bounded metric labels. Names are public constants
    in `Mux.Core.Observability.MuxTelemetryNames`.
  - W3C trace context flows end to end: Watson adopts an inbound `traceparent`, outbound LLM/MCP/web-search HTTP
    calls carry one, jobs parent their span to the submitter across the worker hand-off, and async-iterator
    boundaries keep the run and LLM spans current.
  - `Mux.Server` turns Watson's built-in telemetry on explicitly (HTTP server metrics and a server span per
    request); mux spans nest under it. The request log line is stamped with the request's trace id and never
    includes the query string.
  - `mux`, the tray agent, and the desktop app host one Radiant (`0.1.2`) export pipeline per process when the new
    `observability` settings block is enabled (off by default): OTLP push, an optional in-process Prometheus
    `/metrics` for long-running processes (falls back to OTLP-only on a port conflict), logs with trace
    correlation, and .NET runtime/process metrics. Overrides: `MUX_OBSERVABILITY_ENABLED`, `MUX_OTLP_ENDPOINT`,
    `MUX_OTLP_PROTOCOL`, `MUX_PROMETHEUS_ENABLED`, `MUX_PROMETHEUS_PORT`, `MUX_OTEL_SERVICE_NAME`. `mux serve`
    prints a `telemetry` line when export is active.
  - `docker/compose.yaml`: a pinned, provisioned OpenTelemetry Collector + Prometheus + Tempo + Loki + Grafana
    stack (loopback-bound, healthchecked, ports overridable), alert rules (`docker/prometheus-alerts.yaml`), and
    `docker/update.bat` / `update.sh`. Six per-domain dashboards in `assets/grafana/` load into a **Mux** folder:
    Overview, HTTP, Agent Workflow, LLM Providers, Integrations, and Jobs & Background.
  - The web dashboard's home page gains an **External services** card (Grafana, Prometheus, Tempo, Loki, and the
    process's `/metrics` when served) with copyable URLs and local default credentials, in all 11 locales.
    `GET /v1.0/api/overview` gains an `Observability` block.

### Changed

- Dependencies: `Radiant` 0.1.2 added to the executables only (`Mux.Cli`, `Mux.Agent`, `Mux.Desktop`); the NuGet
  libraries take no new dependency.

### Tests

- `TelemetryCore` and `TelemetryHost` suites (25 cases, in-memory `MeterListener`/`ActivityListener`): every
  instrumented category including failure paths and the no-listener path, span parentage and status,
  `traceparent` propagation to the LLM endpoint and from an inbound request, job parentage across the worker
  hand-off, request-log trace correlation, the Radiant host's Prometheus scrape (second-scale buckets, port-conflict
  fallback), settings defaults and clamps, the overview `Observability` block, and the External services card.
- `RunProcessTool.TimeoutKillsProcess` now uses `sleep 30` on macOS/Linux (`ping -n 30` only counts pings on
  Windows, so the test could never time out elsewhere).

## 1.0.3

### Fixed

- **Gemini tool loops failed on the second request** with a bare `400 Request contains an invalid argument` on
  the native `gemini` and `vertex` adapters. mux's tool-result messages carry only the call id, so PolyPrompt
  fell back to sending the call id as Gemini's `functionResponse.name`. `LlmClient` now resolves each tool
  result's name from the matching assistant tool call when it builds the request, so live, denied, error, and
  resumed-session tool results all send the called function's name. Other adapters identify tool results by
  id and are unchanged.

### Tests

- `LlmBridge.GeminiToolResultCarriesFunctionName`: a native `gemini` request with parallel calls answered in
  reverse order and a denied call's result names every `functionResponse` after its function, both live and
  after the session JSON round trip.

## 1.0.2

### Fixed

- **Gemini 3 tool loops failed on the second request** ("Function call is missing a thought_signature").
  PolyPrompt 2.8.0 captures the thought signature Gemini 3 attaches to each function call; mux now carries it
  on `ToolCall.ThoughtSignature` (JSON `thoughtSignature`, omitted when null), persists it with the session,
  copies it through job snapshots and the server chat DTO (`ChatToolCallDto.ThoughtSignature`), and hands it
  back to PolyPrompt when the history is replayed. Sessions saved before this release have no signatures; on
  the `gemini` and `vertex` adapters PolyPrompt substitutes Google's placeholder for those turns.
- PolyPrompt 2.8.0 also fixes Gemini tool results (sent in `user` turns, parallel results merged, array/scalar/
  plain-text results no longer throw), sends tool schemas as `parametersJsonSchema`, and replays Gemini's own
  function call ids.

### Changed

- Dependencies: PolyPrompt 2.7.1 -> 2.8.0 (drops its SerializationHelper dependency; mux did not use it).

### Tests

- `LlmBridge.ThoughtSignatureRoundTrips`: a signed streamed tool call from a Gemini-style OpenAI-compatible
  mock is captured, survives the session JSON round trip, and is replayed as
  `extra_content.google.thought_signature`; an unsigned call serializes no signature and sends no
  `extra_content`.
- `CoreMapping.ToolCallThoughtSignatureSurvivesDto`: signed and unsigned calls round-trip through
  `ChatMessageMapper`.

## 1.0.1

### Fixed

- **MCP tool errors are reported as failures.** A `tools/call` result carrying `isError: true` (a tool that
  threw, or arguments that failed the tool's input schema) was recorded as a successful tool call. It is now a
  failed `ToolResult` that keeps the server's payload, so the transcript shows the failure reason and the model
  can still read the message and correct itself. Voltaic 2.1+ reports input-schema violations this way (as the
  MCP 2025-11-25 specification requires) instead of as a JSON-RPC `-32602` error.

### Changed

- Dependencies: Voltaic 2.0.0 -> 2.1.13, PolyPrompt 2.7.1, Watson 7.2.1, SQLitePCLRaw.bundle_e_sqlite3 3.0.5,
  NUnit 5.0.0, coverlet.collector 10.1.0.

### Tests

- The strict-schema case now expects an `isError` tool result naming the undeclared property. The test MCP
  server gains a `fail_tool` that always throws, and a new `ExecuteAsyncToolErrorResultIsFailure` case covers
  handler errors becoming failed results while normal results on the same connection still succeed.

## 1.0.0

### Added

- **Desktop installers bundle the app, tray agent, and CLI.** The `desktop` artifact now publishes the
  desktop app, the tray agent (`Mux.Agent`), and the CLI (`mux`) into one payload, so a single installer
  (Inno `.exe`, WiX `.msi`, `.dmg`, `.deb`/`.rpm`, AppImage, and the package managers built from them)
  installs all three. The tray agent sits beside the desktop binary so the app's autostart finds it, and the
  CLI is put on `PATH` — Windows adds the install dir to the system `PATH`, `.deb`/`.rpm` symlink `mux` into
  `/usr/bin`; on macOS the CLI ships inside the `.app` with a logged `ln -s` hint. CLI-only channels (Scoop,
  Homebrew formula, NuGet) still ship just the `mux` tool.
- **Installers require MIT license acceptance.** The interactive installers now display the license from
  `LICENSE.md` and demand acceptance before proceeding: the Inno `.exe` shows a license page (Next disabled
  until accepted), the WiX `.msi` shows the WixUI license dialog, and the macOS `.dmg` carries a Software
  License Agreement that gates mounting behind an Agree/Disagree prompt. Package-manager channels continue to
  record the `MIT` SPDX id as metadata (they have no interactive acceptance step).

- **Session labels and tags.** Annotate any session with **labels** (freeform strings like `wip` or
  `customer-acme`) and **tags** (`key: value` pairs like `env: prod`), then filter the usage analytics by
  label, by tag, or by any combination alongside the existing endpoint/model filters. Metadata persists with
  the session and propagates across every surface. Manage it from anywhere: the terminal (`/label`, `/tag`,
  `/labels`, `/tags`, and `/usage label|tag …` to scope the charts), launch flags (`mux --label … --tag …`),
  a headless verb (`mux session <id> label|tag|unlabel|untag|show`, `mux session --list`), the Desktop app
  and VS Code (right-click a session → Edit labels / Edit tags), the web dashboard's **Sessions** page, and a
  new REST endpoint (`POST /v1.0/api/sessions/{id}/metadata`). Tag keys are normalized (lowercased and
  slugified) so the same concept is one filter facet; values and labels are free-form UTF-8. Usage filtering
  joins each call to its session's *current* metadata at query time, so relabeling a session refilters its
  whole history retroactively; a session carrying several labels counts under each in a label breakdown.

### Changed

- **Dependencies updated: Voltaic 0.7.1 → 2.0.0.** The MCP client already invokes tools only through
  `tools/call` and never reads the old `"pong"` ping result, so no engine change was needed for the
  v1.x/v2.x breaking changes. Behavior against Voltaic 2.x servers: `tools/list` (and mux's discovered tool
  count) now shows only the server's own tools, with no `ping`/`echo`/`getTime`/`getSessions` demo tools;
  a server whose tool schema sets `additionalProperties: false` rejects undeclared arguments, which mux
  reports as a failed tool result naming the property. The in-process MCP test server returns plain-string
  tool results (the v2 idiom) and adds a strict-schema tool; new cases cover exact tool discovery, opt-in
  diagnostic tools, strict-schema accept/reject, and calls to removed demo tools. Also updated: Avalonia
  12.1.3, CommunityToolkit.Mvvm 8.4.2, Microsoft.Data.Sqlite 10.0.12, Microsoft.Extensions.DependencyInjection
  10.0.12, Microsoft.Playwright 1.63.0, Watson 7.2.0, Microsoft.NET.Test.Sdk 18.10.1, NUnit.Analyzers 4.15.0.
  `SQLitePCLRaw.bundle_e_sqlite3` stays on 2.1.13 because Microsoft.Data.Sqlite 10 is built against the 2.1 line.
- **First stable release.** All shipping components are versioned `1.0.0`, and the reusable libraries
  (`Mux.Core`, `Mux.Server`, `Mux.Desktop.Core`) are packaged for NuGet with symbol packages.

## 0.12.3

### Added

- **Adaptive endpoint configuration with flexible API-key placement.** Configuring an endpoint now shows only
  the fields that apply to the selected adapter (base URL and model for everyone; region/project for
  Vertex/Bedrock; api-version for Azure; a custom-headers editor for the HTTP adapters) on every surface —
  web dashboard, terminal, desktop, and VS Code. Critically, the OpenAI-family adapters
  (`ollama`/`openai`/`openai-compatible`/`vllm`) gained an **auth-placement** control: the API key can be sent
  as an `Authorization: Bearer` header (the default), a caller-named header, or a query-string parameter —
  so services that authenticate with a custom header or a query-string value, not a bearer token, are now
  first-class. Backed by a new `AuthPlacement` model on the endpoint (persisted and exposed over REST) and a
  query-string auth handler in the engine; native adapters keep their fixed credential scheme.
- **Usage analytics in the terminal.** `/usage` opens a full-screen, **paged** analytics view — one chart per
  page (tokens over time, cost over time, top models by cost, and a min–avg–p95–p99–max latency
  **distribution**), navigated with ←/→. A **range picker** (keys 1–4: last hour / day / week / month)
  re-queries live, on the same window+bucket grid the web and desktop use. Time-series pages carry a labeled
  Y axis and X-axis time ticks; the latency page uses TUIKit 0.13.1's new vertical `BoxPlotChart`. Reaches
  parity with the web/desktop/VS Code usage dashboards.
- **First-run setup wizard on every UI surface.** On a fresh install — no user-configured endpoint yet — each
  surface (terminal, desktop, web, VS Code) guides you through defining your first endpoint, checking its
  connectivity, and sending your first prompt, then records completion so it does not reappear. Re-run it any
  time (`/setup` in the terminal, "mux: Run setup wizard" in VS Code, a command on desktop, a "Setup wizard"
  button on the web dashboard). The trigger disregards the built-in seed endpoint, so a brand-new install is
  guided rather than silently treated as already configured.
- **Universal prompt management.** Every string mux feeds a model is now legible and editable — no prompt is
  buried in a `.cs` file. A single code-defined catalog (`PromptCatalog`) inventories every model-facing
  prompt with a sensible default, grouped by kind (compaction, task-planning, title generation, tool-section
  lead-ins, all tool descriptions, tool-result payloads, and diagnostics), and one resolver (`PromptResolver`)
  is the single read path — an override else the coded default, with runtime placeholder substitution.
  Overrides persist to a forward-tolerant `operational` map in `~/.mux/prompts.json`; a missing key resolves
  to the default and deleting it is the reset. Overrides are validated so an edit can't drop a required
  placeholder. Persona prompts stay in the switchable prompt profiles, which now expose all three fields
  (system, tools-disabled, and compaction) on every surface — earlier releases hid the latter two. Every
  surface (web dashboard, terminal, desktop, VS Code) can browse the catalog by kind and edit or reset any
  entry; subagent personas are surfaced read-through and deep-link to the subagent editor. New REST routes
  `GET`/`PUT /v1.0/api/prompts/catalog` expose it, and the duplicated compaction literals are consolidated
  into the catalog. See `docs/PROMPTS.md`.
- **Smart large-file context.** A file too big to inline is no longer truncated (or, for the agent's own
  `read_file` tool, refused outright) — it becomes a navigable block. The default **map** mode emits a
  structural outline with line ranges plus the first lines, so the model can pull an exact range with
  `read_file(offset, limit)`; **summarize** runs an iterative map-reduce and emits a dense summary that keeps
  line-range pointers; **truncate** preserves the strict head-slice/refusal. Summaries are cached on disk,
  content-addressed by hash (an edit misses automatically), under `~/.mux/cache/file-summaries/`, with a
  periodic eviction pass. The behavior is a `context` settings group (`largeFileMode`, `inlineThresholdBytes`,
  `summaryChunkLines`, `summaryCacheEnabled`, `summaryCacheRetentionDays`) surfaced on every Settings page.
  `read_file` now returns a map instead of `file_too_large` by default (an explicit `offset`/`limit` still
  pages the exact range), a new route `POST /v1.0/api/context/file` lets a thin client offload
  mapping/summarizing to the server, and the VS Code extension replaced its 8000-character active-file slice
  with the same map/summary (falling back to truncation only when the server is unreachable). "Too large to
  inline" scales with the model: the threshold is a fraction of the selected endpoint's context window
  (`inlineContextWindowFraction`, default 25%, with `inlineThresholdBytes` as the fallback when the window is
  unknown), applied to both `read_file` and the eager path. The VS Code extension exposes its own
  `mux.context.largeFileMode` (inherit/map/summarize/truncate) instead of silently inheriting the server's.

### Fixed

- **Web dashboard Prompts/Pricing cross-wiring.** Clicking a row in the **Prompts** list opened the **Edit
  model pricing** dialog: the two dashboard sections had defined functions with the same names
  (`openPr`/`prFields`/`delPr`), and JavaScript hoisting made the pricing definitions win. The pricing trio is
  renamed (`openPrice`/`priceFields`/`delPrice`) so the Prompts row and add button open the prompt editor, and
  a regression test now fails if any duplicate management-function name is reintroduced.

## 0.12.2

### Fixed

- **Cross-surface conversation sync, made reliable through the shared store.** A turn made on one surface now
  appears in the same conversation open on every other surface (terminal, desktop, web, VS Code) without a
  manual refresh, the conversation list stays live everywhere, and a turn is never silently overwritten by a
  surface that hadn't seen it. The fix makes the on-disk session store the trigger rather than depending on a
  run streaming through one hub:
  - **The server watches the session-store directory** and rebroadcasts any change — including a turn written
    straight to disk by an in-process terminal or desktop run, or by a second server — to its WebSocket
    clients. A thin client (the dashboard, the VS Code extension) therefore learns of every write regardless of
    which hub or run produced it, which also survives a fragmented hub (any server watching the shared store
    tells its own clients).
  - **The terminal and desktop apps watch the store directly**, so they reload the open conversation and
    refresh the list on any external write without depending on the hub at all. This fixes the desktop list
    that never live-refreshed.
  - **The terminal UI now follows the conversation you actually have open.** Its live sync was anchored to the
    (read-only) session the process launched with, so a conversation resumed from `/sessions` never updated;
    it now re-anchors to the resumed session, and the reload is marshaled onto the UI loop so the transcript
    actually repaints.
  - **The VS Code extension now keeps a conversation live-synced whenever it has one open** — a locally created
    or streamed conversation, not only one manually resumed from the tree — so it reloads when the turn was
    made elsewhere.
  - A message-level `transcript_changed` WebSocket event (distinct from the list-level `sessions_changed`) and
    a `notify-transcript` action carry the signal; every turn persist fires it.
  - **The thin clients also reload the open transcript on the list-level `sessions_changed` signal**, not only
    the per-session `transcript_changed`. Because `sessions_changed` fires on every store write and rides the
    surface's persistent global watch (which reconnects independently), content still lands when the
    session-scoped subscription missed a frame or hadn't finished attaching — the case where a conversation
    appeared in the list but its messages did not.
  - Because a stale tray-agent hub runs the old bridge and silently breaks this sync, the product version bump
    forces the agent launcher to replace an older running hub with this build.

## 0.12.0

### Added

- **Run lifecycle: cancel, inspect, and a live WebSocket event bridge.** `mux serve` now tracks each streamed
  run as a first-class, addressable object. New routes: `GET /v1.0/api/runs` (list active + recently-finished
  runs), `GET /v1.0/api/runs/{runId}` (inspect status, counters, current tool, and the task-plan checklist),
  and `POST /v1.0/api/runs/{runId}/cancel` (cooperative server-side cancellation — trips the run's linked
  cancellation token so the agent loop and any in-flight tool stop). The chat stream now emits a `run` event
  first (carrying the run and session ids). The `/v1.0/ws` WebSocket is now a real per-run event bridge:
  authenticate the upgrade (bearer header or `?apiKey=`), subscribe by run or session id, and replay-then-tail
  the **canonical event envelope** — byte-identical to the `mux print --output-format jsonl` contract, because
  both now share one serializer (`Mux.Core.Agent.AgentEventSerializer`). Approvals can be answered over the
  socket too.
- **Stop actually stops, everywhere.** The web dashboard and the VS Code extension (**mux: Stop the current
  run**) now cancel the run server-side rather than only aborting the local stream, so a stopped run no longer
  keeps running on the server.
- **Live session mirroring — a shared run fabric across every surface.** A run on any surface can be watched
  live, read-only, on any other. The run types (`RunRegistry`/`RunHandle`/`RunStatusEnum`/`RunSubscription`)
  moved into `Mux.Core`, and the run stream now carries pre-serialized canonical envelope frames so a run
  driven in another process is indistinguishable from a local one. The WebSocket bridge gained a **publish**
  action (a producer streams its run into the hub) and **session-scoped subscribe** (attach to a session and
  receive whatever run happens there — current or future — without erroring when idle; this is what makes
  mirroring "on by default"). A new `Mux.Core.RunPublisher` is the producer client. **Every surface
  participates:** `mux serve`/dashboard/VS Code produce natively; the **desktop app** and the **terminal**
  publish their in-process runs to the hub; and each surface ensures the hub (the tray agent) is running,
  starting it if needed. **Every surface also consumes by default** — opening a conversation subscribes to
  its session and reflects a run finishing on any other surface with no manual refresh (web dashboard, VS
  Code, desktop, and the terminal/TUI, via `Mux.Core.Runs.SessionMirrorClient`), each de-duplicating its own
  in-flight run. Explicit read-only tails remain (`mux mirror <sessionId>`; dashboard `/mirror on|off`).
  `MuxServer` accepts an externally-owned registry so a host process can expose its own runs.

- **OpenAPI 3.0 document + Swagger UI for `mux serve`.** The local REST server now publishes a complete,
  example-rich OpenAPI 3.0.3 description at `GET /openapi.json` and an interactive Swagger UI at
  `GET /swagger` (Watson 7's `UseOpenApi`). Every route carries a summary, description, tag, parameters,
  request body, and responses; reusable component schemas are documented with example values; the bearer
  API-key scheme is declared and advertised on every operation. Both doc routes are unauthenticated (reachable
  without a key even when one is configured), so a client can discover the surface before authenticating.

## 0.11.0

### Added

- **VS Code extension (`mux-ai`).** A fifth surface: mux in the editor, as a thin client over the local
  `mux serve` API (`src/Mux.VSCode`, published to the VS Code Marketplace). A streaming chat panel with
  in-editor tool approvals, Markdown rendering, and per-turn stats on hover; inline commands and code actions
  (explain, fix, generate tests, refactor, commit message, summarize diff, review file); editor context
  injection (active file, selection, diagnostics, open tabs, git diff, LSP symbols); a **Manage** view with
  full CRUD over endpoints, MCP servers (including bearer/API-key auth), prompt profiles, subagents, skills,
  and settings; a native usage dashboard (stacked token, cost, and latency/TTFT/streaming/throughput
  candlestick charts); a session tree over the shared store; a connection-status indicator with actionable
  help and an About panel; chat slash commands (`/help`, `/new`, `/usage`, …); and localization into twelve
  languages. See `docs/VSCODE.md`.
- **Change the working directory mid-session (`/cwd`).** The terminal UI and desktop app can now show or
  change the directory the agent runs in without relaunching — `/cwd` reports it, `/cwd <path>` changes it for
  subsequent turns (re-probing git checkpoints and re-substituting `{WorkingDirectory}` in the system prompt).
  Backed by a shared, tested `Mux.Core.Utility.WorkingDirectoryResolver`.
- **Tray-agent launchers.** The system-tray agent's menu now offers **Launch Dashboard**, **Launch Terminal**,
  and **Launch Desktop**, so every surface starts from one place.
- **Server prerequisites for editor runs.** `POST /v1.0/api/chat/stream` accepts an optional `workingDirectory`
  (validated; resolved request → session → server), and `GET /v1.0/api/health` reports a `contractVersion`
  clients negotiate against. Server-recorded git checkpoints power undo/redo over REST
  (`/v1.0/api/checkpoints`).
- **Cross-surface session portability & parity.** The terminal UI, desktop app, and web dashboard are now
  three windows onto one session store (`~/.mux/sessions`, keyed by a shared session id), so a conversation
  started in any surface can be resumed in any other — in any ordering — with its full transcript intact.
  - **Working directory** is now recorded on every session (`SessionSnapshot.WorkingDirectory`) so a resumed
    agent continues against the same project directory (falling back to the current directory when the path
    no longer exists). Additive and forward-tolerant on disk.
  - **Canonical history on resume (TUI).** The terminal UI now renders a session's flat `ConversationHistory`
    when it carries no CLI job projection, fixing the case where a Desktop- or Web-authored session opened in
    the TUI showed an empty transcript.
  - **Non-destructive persistence.** The desktop app and web upsert now load-then-mutate, preserving fields
    they do not author (the CLI job projection, prompt history, compaction count, working directory) instead
    of dropping them — so a session survives a TUI → Desktop → TUI round trip.
  - **Shared management verbs.** A single `Mux.Core.Sessions.SessionManager` (`ISessionManager`) implements
    list/create/rename/pin/duplicate/delete/export for every surface; the desktop `ThreadService` now delegates
    to it, and the TUI `/sessions` browser gained rename, duplicate, export, and delete (not just resume).
  - **Server-side web persistence.** `mux serve` chats are now persisted server-side as they complete (keyed
    by session id, minted when absent and returned to the browser), so a web conversation is durable without a
    follow-up save and can never be lost by closing the tab. Tool-call structure round-trips through the web
    surface (session detail/upsert carry tool calls and tool-call ids).
  - **Interactive web tools (opt-in).** `mux serve --allow-tools` lets a mutating tool proposed during a web
    chat prompt the browser for approval (streamed over SSE, answered via `POST /v1.0/api/chat/approve`)
    instead of being auto-denied. The default remains read-only (mutating tools denied).

## 0.10.0

### Added

- **mux Desktop (Avalonia).** A new cross-platform desktop front end (`src/Mux.Desktop`) that reaches the
  full mux TUI capability set plus the `mux serve` dashboard's monitoring/reporting, in a Claude/Codex-style
  interface. It links `Mux.Core` in-process (the REST API is not used for agentic work) under an auto-safe
  approval policy (read-only tools auto-approve; mutating tools prompt). Highlights:
  - **Chat:** streaming transcript with Markdown, tables, and syntax-highlighted code (per-response copy),
    thinking sections, tool-call cards, MCP tools and skills in turns, prompt-history recall, `/compact`,
    `/effort`, and a full set of slash commands.
  - **Parallel conversation tabs:** each open thread has its own runtime, transcript, and streaming state,
    so tabs run agent turns concurrently and switching tabs never blocks (git checkpoints are serialized
    across the shared working tree; undo/redo remain global).
  - **Threads:** a conversation sidebar backed by the real mux session store (shared with the TUI), with
    AI-summarized titles, rename/export/bulk-delete, and a command palette (`Ctrl+K`).
  - **Configuration managers** for endpoints (model import + validation), MCP servers (connectivity checks),
    prompt profiles, skills, subagents, pricing, hooks, custom commands, keybindings, and web-search
    providers, plus a grouped settings surface and an embedded local-server toggle.
  - **Monitoring:** the usage dashboard (KPI strip; token/cost/latency/TTFT/streaming/throughput charts with
    endpoint/conversation filters and a per-call history) and a per-conversation stats window.
  - **Internationalization:** the full UI is translated into the same eleven languages as the dashboard
    (English, Spanish, Portuguese, French, Italian, German, Mandarin Chinese, Arabic, Russian, Malay,
    Hindi) with a live language switcher and right-to-left layout for Arabic.
  - **Accessibility & theming:** system/light/dark plus a high-contrast theme, and accessible names on
    icon-only controls for screen readers.
  - Undo/redo via git checkpoints, a startup splash, an About/Help window, a single-instance lock, and
    self-contained per-OS publish scripts (`publish-desktop.*`). Like the TUI it brings up the tray agent at
    startup (opt out with `MUX_AGENT_AUTOSTART=0`). The UI-framework-agnostic logic lives in a separate,
    Avalonia-free **`Mux.Desktop.Core`** library (localization, formatters, thread/usage/conversation
    services over `Mux.Core`, `TurnProjection`), covered by Touchstone suites in `Test.Shared`;
    `SessionTitleHelper` moved into `Mux.Core.Sessions` so both front ends share it. See `docs/DESKTOP.md`
    (guide) and `archive/DESKTOP_APP.md` (implementation plan). Launch it with
    `run-desktop.bat` / `run-desktop.sh`.
- **`Mux.Core` published to NuGet.** `Mux.Core` (and its `Mux.Search` dependency) now carry full package
  metadata — MIT license, project/repository URLs, README, icon, tags — and produce a symbol package
  (`.snupkg`) with SourceLink, so others can build their own experiences on top of the mux engine.
- **Version bump to 0.10.0** across `Mux.Core`, `Mux.Cli`, `Mux.Agent`, `Mux.Server`, `Mux.Desktop`, and
  `Defaults.ProductVersion`.
- **Usage telemetry (durable, cross-process).** Every model call is recorded to a local SQLite database
  (`~/.mux/usage.db`, WAL mode — no external process) with token counts, time-to-first-token, streaming
  time, total latency, throughput, finish reason, and success, tagged by endpoint, model, provider,
  command, session, and call kind (primary/compaction/subagent/chat). Multiple mux instances write the
  same database concurrently and safely. The `mux serve` dashboard gains **Usage** (KPI strip; token, cost,
  latency/TTFT/streaming/throughput charts over hour/day/week/month with endpoint/model filters; and a
  paginated per-call history) and **Pricing** (a form editor over `pricing.json`) pages, backed by new
  `GET /v1.0/api/usage/{summary,timeseries,breakdown,events,filters,pricing}` and `PUT
  /v1.0/api/usage/pricing` routes. The TUI adds a `/usage` (`/stats`, `/spend`) command showing a
  24h/7d summary from the shared store and a live session-cost line in the sidebar. Cost is derived at read
  time from a seeded, user-editable, manifest-tracked `pricing.json` so a rate fix re-values history.
  Capture is best-effort and never affects a run; disable with `telemetry.enabled` or `MUX_TELEMETRY_ENABLED`.
  Cached- and reasoning-token accounting flows through when the provider reports it (PolyPrompt 2.6.0). New
  `Mux.Core/Telemetry` (`SqliteUsageStore`, `SqliteUsageRecorder`, `UsageQueryService`, `PricingTable`, and
  the `UsageTelemetry` facade). The dashboard Usage page renders full-width, tabbed SVG charts (with axes,
  gridlines, and hover tooltips) and a full-featured history table, and the Pricing page uses the same
  data-grid and modal workflow as the rest of the dashboard. Also fixes a latent bug where `settings.json`'s
  `rest` and `maxTokenBudget` sections were silently dropped on every load/save round-trip.
- **Postman collection.** A documented collection covering the full REST surface, organized into
  per-resource folders with variables and collection/folder/request-level documentation, plus a companion
  environment, under `assets/postman/`.

- **Subagents (delegated, isolated sub-tasks).** Define named subagents in `~/.mux/subagents.json` (name,
  description, system prompt, optional endpoint override, tool allow-list, and iteration cap) and the model
  can hand a self-contained sub-task to one with the new `spawn_subagent` tool. A subagent runs as a nested
  agent loop in an **isolated conversation** — it never sees or mutates the parent's history — and returns
  only its final answer, keeping the primary agent's context clean. It cannot spawn further subagents, never
  carries the parent's task plan, and `spawn_subagent` does not hold the workspace write lease (so a
  delegated task's own mutating tools serialize normally). The tool is offered only when a valid subagent is
  defined. New `Mux.Core/Subagents` (`SubagentDefinition`, `SubagentRegistry`, `ISubagentExecutor`,
  `AgentLoopSubagentExecutor`, `SubagentResult`). A curated **default set covering the product lifecycle** —
  `product-manager`, `architect`, `software-engineer`, `test-engineer`, `ux-engineer`, `experience-evaluator`,
  `code-reviewer`, `devops-engineer` — is seeded into `subagents.json`, each tool-scoped to its role (review/
  planning personas are read-only). Seeding is manifest-tracked so new defaults appear on upgrade without
  resurrecting ones the user deleted or overwriting edits.
- **Local session export / sharing.** `/export` (`/share`) in the shell writes the current session to a
  self-contained HTML file and a Markdown file in the working directory — no server, no network. A new
  `mux export <id>` CLI verb renders a saved session (`--format md|html`, `--output <path>`, `--list`). The
  HTML is a single file with inline styles, no external requests, and all dynamic text escaped. New
  `Mux.Core/Sessions/SessionExporter`.
- **Custom keybindings.** `~/.mux/keybindings.json` overrides the key chord bound to any command by id
  (rebind, or `null` to unbind). Overrides flow through every surface — key bindings, menu bar, footer — and
  an unparseable chord is ignored so a typo never breaks startup.
- **Undo/redo via git checkpoints.** In a git repository, mux snapshots the working tree before each turn;
  `/undo` rolls back the last turn's file changes and `/redo` re-applies them. Snapshots capture tracked and
  untracked (non-ignored) files (reverting edits, restoring deletions, removing new files) using git plumbing
  only — the branch, history, stash, and staged index are never touched. Its reach is the git working tree;
  it cannot reverse effects outside it. New `Mux.Core/Checkpoints` (`GitCheckpointService`,
  `CheckpointManager`, `Checkpoint`).
- **Plugin system: out-of-process hooks and custom commands.** `~/.mux/hooks.json` configures event hooks
  (`session-start`, `user-prompt-submit` — vetoable by a blocking hook — and `session-end`) and custom
  `/<name>` slash commands. Hooks receive the event payload on stdin and their stdout is surfaced into the
  transcript; everything runs as a literal argument vector, never through a shell. New `mux plugin list` verb
  and `Mux.Core/Plugins` (`HookRunner`, `PluginRegistry`, `CustomCommandRunner`, config models).
- New Touchstone suites for all of the above (`Subagent`, `SessionExporter`, `Keybinding`, `Checkpoint`,
  `Plugin`), run under all three runners on `net8.0` and `net10.0`.

### Web dashboard (`mux serve`)

- **Internationalization (11 languages).** The dashboard ships a first-class i18n runtime — a `t(key)`
  lookup with per-locale inline catalogs for **English (default), Spanish, Portuguese, French, Italian,
  German, Mandarin Chinese, Arabic, Russian, Malay, and Hindi**. A topbar language picker persists the
  choice in `localStorage` and falls back to English for any missing string; **Arabic switches the page to
  RTL** (`dir="rtl"` with mirrored accents), and counts/dates format via the browser's `Intl`. The primary
  chrome — navigation, view titles, table headers, row-action menus, empty states, toolbar/pagination,
  toasts, confirmations, the Home overview, chat, and settings section headers — is fully localized. (Long
  help-text tooltips and form-field labels remain English for now and fall back cleanly.)
- **Full configuration management.** The dashboard now manages every mux config surface, not just settings:
  **Endpoints, MCP servers, prompts, subagents, hooks & custom commands, keybindings, skills** (enable/
  disable/view/delete), and **sessions** (browse, export to HTML/Markdown, delete). Each is a full-width
  table with icon row-actions and custom modal add/edit forms — no browser `alert`/`confirm` dialogs. Secrets
  (endpoint API keys/headers, MCP auth) are never sent to the client and are preserved when left blank. New
  REST routes back each domain (see `docs/REST_API.md`).
- **Per-turn chat stats.** Assistant messages show an **(i)** hover with time-to-first-token, streaming time,
  total latency, and provider token counts (the chat route now streams server-side to measure them).
- **Markdown rendering fixed** — code blocks preserve their line breaks and escaping, and lists, headings,
  blockquotes, and inline formatting render correctly (the previous renderer mangled code blocks and ignored
  lists).
- **Bearer-only auth.** The REST server and dashboard standardize on `Authorization: Bearer <key>`; the
  `X-Api-Key` header is no longer accepted (**breaking change** for any external client that used it).
- Polish: GitHub and theme controls are now icons; consistent table/modal styling across the app.
- **Dashboard UX pass.** Config surfaces are full-width tables where each row opens its edit modal on click
  and exposes a single **⋮ action menu** (Edit/Delete, plus view/enable for skills and view/export for
  sessions) that overlays the table and stays within the viewport (flips up near the bottom, clamps
  horizontally). Add/edit use custom modals with section grouping and **conditional fields** (MCP shows only
  the selected transport's fields; endpoints show only the active adapter's auth fields), never browser
  dialogs. Event Hooks and Custom Commands are now separate pages. Sessions can be **previewed in a modal**
  (rendered Markdown, or HTML in a sandboxed iframe) as well as downloaded. Server health/version/uptime moved
  to a topbar status pill and the standalone Server Info tab was removed. Helpful empty states, auto-focus,
  and refined styling throughout.

## v0.9.0 (2026-09-07)

### Added

- **Local REST server & system-tray agent (opt-in).** A new `mux serve` command starts a loopback-bound
  (`127.0.0.1`), token-guarded REST + WebSocket server (Watson 7) over mux's existing in-process services —
  it never auto-starts from a plain `mux`/`mux print` run. Routes: `GET /v1.0/api/health` (anonymous),
  `GET /v1.0/api/endpoints` and `GET /v1.0/api/sessions` (API-key gated, secrets never surfaced), plus a
  `GET /v1.0/ws` WebSocket that announces connection with the JSONL-style event envelope. Auth is a single
  local API key sent as `Authorization: Bearer <key>` or `X-Api-Key`, auto-generated and persisted on first
  serve (or disabled with `--no-auth`). A new cross-platform (Windows/macOS/Linux) **Avalonia system-tray
  agent** (`Mux.Agent`) hosts the server in the background and offers **About**, **Launch Mux**, and **Exit**;
  a single-instance lock prevents duplicate agents. New `rest` settings block in `settings.json`
  (`enabled`/`hostname`/`port` [default 8710]/`ssl`/`apiKey`/`corsAllowOrigin`) with `MUX_REST_HOST`,
  `MUX_REST_PORT`, and `MUX_REST_APIKEY` overrides and `--host`/`--port`/`--api-key`/`--no-auth` flags. New
  projects `Mux.Server` and `Mux.Agent`; see `docs/REST_API.md` and `MUX_COMPARISON.md`. OpenAPI document
  generation and run-driving routes are documented follow-ups.
- **Web dashboard at `/dashboard`.** The REST server now serves a self-contained single-page dashboard
  (inlined CSS/JS/logos, no external assets) with three surfaces: a **Wilson-style chat** over any configured
  endpoint (endpoint picker, user/assistant bubbles, markdown + code blocks, new-chat), a **form-based
  Settings editor** over `settings.json` (agent/context/features/REST groups, masked secrets, restart-required
  hints), and a **Server Info** view (health/version/uptime + endpoints), with a persisted light/dark theme
  using the `assets/` logos. Backed by new routes `POST /v1.0/api/chat` (plain, tool-free completion via the
  LLM client) and `GET`/`PUT /v1.0/api/settings` (secrets masked; the REST key changes only when a new value
  is supplied). Streaming chat (SSE) and OpenAPI generation are documented follow-ups.
- **Tray agent startup scripts.** `scripts/{windows,linux,macos}/run-at-startup` and `remove-from-startup`
  register/unregister the tray agent to launch at login (Windows HKCU `Run` key; Linux systemd `--user`
  service or `autostart` desktop entry; macOS launchd LaunchAgent). The agent runs the local REST server.
- **Startup model warming.** The interactive shell now warms/validates the active endpoint's model at launch
  (the same background load an endpoint switch performs — pulling lazily loading Ollama models into memory and
  confirming hosted credentials/URLs), instead of only on switch. Surfaced as a "Loading model … / loaded"
  notice.
- **Theme-aware, full-size tray icon.** The tray agent now uses the high-resolution `assets/` PNG logos and
  picks the white glyph on a dark taskbar and the black glyph on a light one, updating live on OS theme change.
- **Dependencies updated to latest:** Voltaic 0.6.0 → 0.7.1, TUIKit 0.8.2 → 0.10.1, Avalonia 11.3.20 → 12.1.2
  (tray agent), and NUnit3TestAdapter 6.2.0 → 6.3.0 (PolyPrompt 2.5.0, Watson 7.1.1, and Playwright 1.62.0 are
  already latest). Full self-test suite green after the upgrades.
- **`/settings` global settings editor.** A new interactive command (aliases `/config`, `/preferences`,
  `/prefs`; also in the `F1` menu) opens a scrolling form over `settings.json` covering every scalar/boolean
  setting: the agent-loop run limits (**max agent iterations** — the 1–100 cap on model turns per run — and
  the optional **max token budget**), max concurrency, the default approval and enqueue behaviors, tool and
  process timeouts, the context and compaction tuning, the skills and task-planning toggles, and the
  cert-error and boundary-line flags. Saving persists to `settings.json` and applies the run-affecting values
  (iteration cap, token budget, compaction, context tuning, cert errors) to the next turn; concurrency and
  startup-only wiring apply on the next launch. The editor sets the **global** defaults — a per-model
  iteration override still lives in the endpoint Add/Edit form and wins for that endpoint. Switching
  endpoints now also re-resolves the effective iteration cap so a per-model override takes effect on the next
  turn instead of staying pinned to the startup endpoint's value.
- **Headless output modes, token usage, and a stats toggle for `mux print`.** The four output shapes are now
  first-class and fully documented: **streamed text** (`--output-format text`, default), **buffered text**
  (`--output-format text --buffer`, alias `--no-stream`), **streamed JSONL** (`--output-format jsonl`), and
  **buffered JSON** (`--output-format json`). Two new flags cut across them: `--buffer`/`--no-stream` holds
  text output and emits it in one write instead of streaming; `--stats`/`--no-stats` includes or omits run
  statistics and provider-reported token usage. Structured output (`json`/`jsonl`) includes stats by default;
  text output never carries them on stdout, and `--stats` surfaces a one-line `mux: tokens …` footer on
  **stderr** so stdout stays exactly the answer. The `json` summary object and the `jsonl` `run_completed`
  event now carry a `usage` object (`inputTokens`, `outputTokens`, `totalTokens`, `estimatedTokens`);
  provider counts come straight from the backend's usage metadata. The TypeScript and Python SDKs expose the
  new usage fields on their aggregated result.
- **`--prompt "<text>"` startup prompt.** A new interactive-only option that skips the startup splash and
  submits the supplied prompt as the first turn, then drops into the usual interactive shell. A bare
  positional prompt (`mux "do the thing"`) does the same, aligning behavior with the long-documented
  `mux [prompt]` usage line; an explicit `--prompt` wins over stray positionals.
- **`mux endpoint models [name]`.** Live-enumerates the models each configured endpoint's backend
  advertises — Ollama via its native `/api/tags`, and OpenAI / vLLM / OpenAI-compatible via
  `GET /v1/models` (sending the endpoint's configured auth headers) — as a text table or, with
  `--output-format json`, a machine-readable payload. Query a single endpoint by name or sweep them all;
  per-endpoint failures are classified (`auth_error`, `backend_unreachable`, `models_endpoint_not_found`,
  …) and captured rather than aborting the sweep.
- **Consistent command-output spacing.** The table-rendering commands (`probe`, `endpoint`, `skill`) now
  bracket their stdout with exactly one leading and one trailing blank line, so output is always visually
  separated from the shell prompt. JSON and JSONL consumers are unaffected (surrounding whitespace is
  tolerated). `print` is intentionally excluded — it streams the assistant's answer to stdout terminated
  by a single newline, with no leading or trailing blank line, so it stays clean for humans and pipes.

### Changed

- **Automation contract bumped to `contractVersion` 2.** The `json` run summary and the `jsonl`
  `run_completed` event gained the `usage` token block, and `--no-stats` can now strip the run-metrics and
  `usage` fields entirely. The change is additive for default consumers (stats stay on by default), but the
  version bump signals the new fields to SDKs and downstream parsers.
- **Adopted TUIKit 0.6.0's standardized components.** Bumped the TUIKit dependency `0.5.1 → 0.6.0` and
  replaced hand-built interactive-UI code with the general-purpose components the framework grew from the
  same patterns: the footer-hint wrapper (`HintText`), the command-menu column aligner
  (`ColumnFormatter`), the composer's submit-vs-newline resolver (`SubmitKeyResolver`), the Ollama-import
  multi-select dialog (`MultiSelectModal<T>`), and the "thinking" spinner-plus-phrase indicator
  (`ActivityIndicator`). Behavior is unchanged except the Ollama import picker now renders in TUIKit's
  rounded dialog. The remaining 0.6.0 components were evaluated and either deferred (larger refactors —
  `DialogModal`, `CommandRegistry`, `StreamingTranscript`) or left as-is where TUIKit has a real gap; the
  full per-component assessment is in `TUIKIT_0.6.0_ADOPTION.md`.
- **Bumped TUIKit `0.8.1 → 0.8.2`.** The interactive selection surfaces now support page and jump
  navigation for free: `PageUp`/`PageDown` move by a viewport height and `Home`/`End` jump to the first
  and last row across every list-backed picker (single-select modals, the multi-select Ollama-import
  dialog, and the reorderable/action lists), and `Home`/`End` jump to the top and bottom of scrollable
  views. The change is additive with no API breaks; the project builds clean on net8.0 and net10.0.

### Fixed

- **Retry transient connection failures with backoff.** LLM requests now ride out a momentary connection
  refusal or reset (a local model still warming, or a busy loopback listener under load) instead of failing
  the run. Streaming turns already retried but hammered back-to-back with no delay; they now space attempts
  with exponential backoff (100ms → 500ms, capped). The non-streaming path used by `mux probe` and the
  interactive endpoint model-load probe previously made a single attempt with no retry — both now retry
  transport-level failures the same way. HTTP error statuses (4xx/5xx) remain definitive and are not retried.
  This also removes intermittent CI failures in the in-process CLI test suites, where dense back-to-back mock
  servers occasionally triggered a transient loopback refusal.

## v0.8.0 - 2026-08-12

### Added

- **Reasoning effort.** A per-endpoint reasoning level (`minimal`, `low`, `medium`, `high`) that mux
  translates per backend through PolyPrompt 2.1.0 — OpenAI `reasoning_effort`, Gemini
  `thinkingConfig` budget, and Ollama `think` — and omits entirely when unset, so existing endpoints are
  unchanged. Stored in `endpoints.json` under `reasoningEffort`, with optional per-provider overrides
  (`openAiValue`, `geminiThinkingBudget`, `ollamaThink`).
- **`/effort` (alias `/reasoning`).** An interactive picker (Off / Minimal / Low / Medium / High) that
  persists the choice to the active endpoint and applies it to the next turn. The sidebar shows an
  `EFFORT` line, and the endpoint Add/Edit form gains a **Reasoning effort** field plus an advanced
  **Gemini thinking budget** field.
- **Headless flags.** `--effort <off|minimal|low|medium|high>` sets or clears the level (`off` disables
  even when the endpoint sets a level); `--effort-openai-value`, `--effort-gemini-budget`, and
  `--effort-ollama-think` override the per-provider values (only when a level is active).
- **JSONL contract.** `run_started` now includes a `reasoningEffort` object (the effective level and any
  overrides, or `null`), and `cliOverridesApplied` lists `reasoningEffort` when a flag drove the value.

- **Model thinking display.** A per-endpoint `showThinking` property (in `endpoints.json`, off by default)
  that captures and displays a reasoning model's thinking. When on, thinking streams into the TUI transcript
  under a dim `💭 thinking` header — kept separate from the answer and never sent back to the model — and is
  surfaced in headless as `assistant_thinking` JSONL events (or on stderr in text mode). `/thinking` (alias
  `/think`) toggles it live and persists it; the endpoint Add/Edit form has a **Show thinking** checkbox; the
  sidebar shows a `THINK` line; `--show-thinking` overrides a headless run. `run_started` reports
  `showThinking`, and `cliOverridesApplied` lists it when the flag drove the value.

### Changed

- Bumped the PolyPrompt dependency to `2.2.0` for reasoning-effort control and reasoning ("thinking") capture.

## v0.7.0 - 2026-08-07

### Added

- **Driver SDKs — TypeScript (`sdk/typescript`, `@mux/sdk`) and Python (`sdk/python`, `mux-sdk`).** Each
  spawns `mux print --output-format jsonl`, parses the event stream into typed events, and returns an
  aggregated result — with a `Mux` client, streaming and buffered runs, and multi-turn `Thread`s that
  persist through mux sessions (`--session-id`). They wrap the CLI rather than binding to internals, so
  they stay in lockstep with the versioned JSONL contract and any language can integrate the same way.
  Each ships with a hermetic test harness (a fake mux, no network or model) and a README.
- **Multi-turn stdin input (`--input-format jsonl`).** `mux print --input-format jsonl` reads a stream of
  turn records from stdin — one JSON value per line (`{"prompt":"..."}`, or `text`/`content`, or a bare
  string) — and runs each as a turn against the accumulating conversation, so turn N sees turns 1..N-1.
  Output follows `--output-format` per turn; `--output-last-message` captures the final turn; a session
  flag persists the whole conversation as one session; and MCP servers connect once and are shared across
  turns. Malformed records are reported and skipped without ending the stream.
- **Headless MCP for `print`.** `--mcp-config <path|json>` connects MCP servers for a single `print` run —
  reusing the same runtime as the interactive shell — waits (bounded) for tool discovery, exposes the
  discovered tools to the model, and disposes the connections when the run ends. `--strict-mcp-config`
  uses only the flag's servers, ignoring `mcp-servers.json`. MCP stays off unless `--mcp-config` is given,
  so a plain `mux print` remains hermetic; the active state is reported on `run_started.mcp`.
- **`--output-schema <path>` for `print`.** Folds a JSON Schema directive into the prompt and validates the
  final response against it recursively — `type` (including union type arrays and `integer`), `enum`,
  `required`, nested `properties`, and array `items` — reporting the first violation with a JSON path.
  Value-level bounds/patterns/formats are not enforced, and validation is client-side (mux's LLM layer,
  PolyPrompt, exposes no `response_format` field, so provider-native structured output is not available);
  the approach is backend-agnostic. Code fences are unwrapped; a non-conforming response fails with
  `schema_validation_failed` (exit `1`) and suppresses the artifact and `json` summary.
- **Tool governance and a confinement posture.** `--allow-tools`/`--deny-tools` take comma-separated
  tool-name globs (`*`/`?`) to restrict which tools a run may use — deny always wins, and an excluded tool
  is neither advertised to the model nor allowed to execute. `--sandbox` adds an application-level posture:
  `read-only` refuses every mutating tool, and `workspace-write` confines built-in file writes to the
  working directory plus any `--add-dir` roots (repeatable), refusing writes that escape them. Refusals use
  the `tool_call_denied` error code (exit `2`), and the active posture is reported as `sandboxPosture` on
  `run_started`. This is a mux-level policy over the built-in tools, not an OS sandbox: `run_process` and
  external MCP subprocesses are not kernel-confined and remain gated by the approval policy. All four flags
  apply to `mux print` and to the interactive shell at launch.
- **Headless session continuity.** `mux print` can now resume prior work non-interactively. `--resume
  <id|title>` continues a persisted session, `--continue` picks up the most recently updated one,
  `--session-id <id>` runs under a specific id (creating it if absent), `--fork-session` saves the
  resumed run under a new id, and `--no-session-persistence` runs without writing to disk. Persistence
  is opt-in — a plain `mux print` stays stateless — and print sessions share the one session store with
  the interactive `/sessions` browser, so a session started in either surface can be continued in the
  other. The resolved session id is now carried on the `run_started` and `run_completed` events (and in
  the `json` run summary) so an orchestrator can capture it from one run and pass it to the next.
- **Single-object `json` output for `print`.** `mux print --output-format json` emits one summary object
  at the end of the run — `result`, `status`, `sessionId`, `iterationsCompleted`, `toolCallCount`,
  `errorCount`, `durationMs`, `finalEstimatedTokens`, `compactionCount`, optional `taskSummary`, and
  `contractVersion` — with the same secret redaction as the `jsonl` stream. The streaming `jsonl` form is
  unchanged; `text` remains the default.
- **`--max-turns <int>`.** Overrides the agent loop iteration cap for a single run (clamped to 1-100),
  without editing `settings.json` or the endpoint.
- **`--append-system-prompt <text>`.** Appends text to the resolved system prompt after all placeholder
  substitution, so it survives profile switches. Complements the existing `--system-prompt <path>`.
- **`--max-token-budget <int>` and `maxTokenBudget` in `settings.json`.** A backend-agnostic ceiling on
  the estimated working-context tokens: when the estimate exceeds the budget before a model call, the run
  stops cleanly with a `budget_exceeded` error and a matching `run_completed` status rather than
  continuing. This is mux's provider-neutral analogue of a spend cap; it is based on mux's token estimate,
  not a provider billing figure.

### Fixed

- **Version string.** `mux --version` now reports the current release; the compiled `ProductVersion`
  constant had lagged the changelog.

## v0.6.0 - 2026-08-04

### Added

- **HTTP MCP server authentication.** When configuring an HTTP MCP server through `/mcp`, you can now
  choose an auth scheme — none, a bearer token, or an API key sent in a caller-specified header
  (default `X-API-Key`). The credential is persisted in `mcp-servers.json` under the server's `auth`
  object and attached to every request the client makes to that server (via `SetRequestHeader`),
  including the connection handshake. Token and API-key values support `${VAR}` environment-variable
  expansion, so secrets can be referenced from the environment instead of stored in plaintext. Secret
  fields are masked in the form.

### Fixed

- **The MCP server form no longer mixes transports.** The add/edit form now shows only the selected
  transport's fields — command / args / env for `stdio`, or url / mcp-path / auth for `http` — so
  switching transports no longer leaves the other transport's stale values on screen. Only the fields
  relevant to the chosen transport are collected and persisted.

## v0.5.0 - 2026-07-31

### Added

- **Background tasks** — the model can decompose a job into a tracked plan of tasks and advance them
  as it works, and the interactive shell renders a live checklist that updates in place (pending →
  running → done). Two model tools, `plan_tasks` and `update_task`, are the write path; the plan is
  per-job, persists across session save and resume, and is summarized in the sidebar as `TASKS n/m`.
  Open `/tasks` to inspect and hand-annotate the focused job's plan. Gated by `taskPlanningEnabled`
  (default true).
- `task_plan_updated` joins the `mux print --output-format jsonl` event contract so orchestrators can
  track subtask progress, and `run_completed` carries a `taskSummary` tally.
- Orchestration engine (`TaskOrchestrator`) that runs a task DAG as parallel jobs under the shared
  workspace write lease, gated by `taskParallelismEnabled` (default false). The engine is complete and
  tested; wiring it into the interactive submit path is a planned follow-up.

## v0.4.0 - 2026-07-31

### Added

- **Skills** — versioned Markdown-plus-code capabilities in `~/.mux/skills` that turn a request into a
  fixed, deterministic procedure. Each skill is a folder with a `SKILL.md` (YAML frontmatter plus a body)
  and optional bundled scripts; its commands run a fenced code block or a script through an allowlisted
  interpreter (`bash`, `sh`, `pwsh`, `python`, `node`, `dotnet-script`) with a timeout and captured output.
  On startup the interactive shell discovers skills, lists the enabled ones in the system prompt, and
  exposes two tools — `skill` (read a skill's instructions) and `run_skill` (execute a command, returned
  like `run_process` and gated by the approval policy and write lease). A curated library of 46 default
  skills — spanning git/GitHub, .NET build and quality, repository hygiene, scaffolding, documentation, and
  developer workflow — is seeded on first run and, on upgrade, tops up an existing `~/.mux/skills` with any
  newly shipped defaults (tracked in a `.seeded-defaults` manifest) without resurrecting ones you deleted or
  overwriting your edits. Manage skills in-app with `/skills`
  (inventory with status glyphs, per-skill view/in-app edit/enable/disable/duplicate/remove, a create
  wizard, and local-path import) or with the `mux skill list | show | validate | run | new | add` verb;
  `validate` returns a nonzero exit for CI and `run` returns the process contract for hooks. Caller
  arguments reach the interpreter as separate argv entries with no shell, so shell metacharacters cannot
  inject. New `settings.json` fields: `skillsEnabled`, `skillRefreshIntervalSeconds`, `skillsDirectory`.

- **MCP servers are now connected live in the interactive shell.** On startup mux connects to every server
  configured in `mcp-servers.json`, queries each for its available tools (`tools/list`), and then both
  registers those tools as callable (routing invocations back to the owning server) and appends them to the
  system prompt so the model is explicitly made aware of them. Connectivity is re-validated on a periodic
  timer (default 30s) and disconnected servers are periodically retried; adding, editing, or removing a
  server via `/mcp` reconnects in the background and takes effect on the next turn without a restart.
- The `/mcp` manager now shows each server's live connectivity: `●` online (with its discovered tool
  count) or `○` offline.

- **Import completion models from an Ollama server** directly in the interactive endpoints / models
  picker (`Ctrl+E`, `/endpoint`, `/model`). A new **Import from Ollama…** entry at the bottom of the
  picker prompts for an Ollama base URL (default `http://localhost:11434`; a missing scheme is filled in
  and a trailing `/v1` or slash is stripped to Ollama's native API root), queries the server's installed
  models via `GET /api/tags`, and presents a scrollable multi-select checklist of what it finds. `Space`
  toggles the highlighted model, `a` toggles all, `Enter` imports the checked models, and `Esc` cancels.
  Ollama's model list does not classify models as completion vs. embedding, so every installed model is
  listed with an on-screen reminder to select completion models only (leave embedding models unchecked).
  Model-plus-endpoint combinations already present in `endpoints.json` are excluded from the checklist so
  re-imports never create duplicates, and each selected model is saved as a new `ollama` endpoint at the
  normalized base URL, uniquely named after the model.
- `Mux.Core.Utility.OllamaModelLister` — a small utility that normalizes an Ollama base URL and lists a
  server's installed model names from `GET /api/tags`.
- **Optional boundary lines** in the interactive shell, off by default. A new `/borders` command (aliases
  `/boundaries`, `/lines`; also on the `F1` menu under **View**) toggles dark-grey rules: a horizontal
  rule above the prompt input, a horizontal rule above the queued-messages strip (when shown), and a
  vertical rule in the gutter left of the sidebar. The choice persists to `settings.json` as the new
  `showBoundaryLines` field (default `false`) and is applied on the next launch.
- **Interactive MCP-server management** via a new `/mcp` command (aliases `/mcp-servers`, `/servers`;
  also on the `F1` menu under **Model**). It opens a picker over the servers configured in
  `mcp-servers.json` with **Add**, **Edit** (select a server row), and **Remove** (with confirmation)
  actions. The add/edit form collects a name, a transport (`stdio` or `http`), and the transport-specific
  fields — command / space-separated args / comma-separated `KEY=VALUE` env for `stdio`; url / mcp path
  (default `/mcp`) for `http` — validating that `stdio` has a command and `http` has a url. Changes
  persist to `mcp-servers.json`, with a notice that mux must be restarted for them to take effect.

### Changed

- The endpoints / models picker now renders a blank separator row between the configured endpoints and
  the management actions (**Add endpoint**, **Edit endpoint**, **Remove endpoint**, and
  **Import from Ollama…**).
- The interactive shell now keeps a one-column gutter between the transcript (and queue strip) and the
  right-anchored sidebar, so the two panes no longer butt directly against each other.
- Introduced an external-tool provider seam (`IExternalToolProvider` on the agent loop) so MCP servers and
  skills compose their tools and prompt sections instead of contending for a single hook.
- Renamed the **Theme…** command menu entry to **Theme**.
- `/help` and `/?` now open the same navigable command menu as `F1` (previously a static, non-interactive
  list), and that menu now shows each command's `/slash` aliases alongside its title and key chord.
- Widened the `F1` / `/?` command menu by ~50% and column-aligned it so the key-chord column and the
  `/slash` alias column each line up on a single vertical axis.

### Fixed

- HTTP (streamable) MCP servers no longer fail to connect against spec-compliant servers. The bundled MCP
  client (Voltaic `0.1.11`) sent `Accept: application/json` on its streamable-HTTP requests; servers that
  require the MCP-mandated `Accept: application/json, text/event-stream` (returning `406 Not Acceptable`
  otherwise) were shown as offline with no tools. Upgraded Voltaic to `0.4.0`, which sends the compliant
  `Accept` header (verified end-to-end against a real server discovering all of its tools).

## v0.3.0 - 2026-07-29

This release rebuilds the mux interactive front-end on
[TUIKit](https://www.nuget.org/packages/TUIKit), introduces a concurrent background-job model, and
migrates the test suite to [Touchstone](https://www.nuget.org/packages/Touchstone.Core).
`Spectre.Console` has been removed entirely. `mux print`, `mux probe`, and `mux endpoint` are
unaffected.

### Added

- Concurrent job engine in `Mux.Core` (UI-free, headless-tested): a `JobManager` + scheduler runs
  multiple prompts as background jobs, a fair single-writer `WriteLease` allows parallel reads while
  serializing file-mutating tools ("parallel reads, single writer"), per-job approval routing, and
  atomic session persistence.
- Rebuilt the interactive UI on TUIKit: `mux` with no non-interactive command launches the `MuxTuiApp`
  shell — a sidebar / transcript / composer / footer layout with a streaming `AgentEvent` projector.
  `Ctrl+Q` quits, `Ctrl+L` clears, `Ctrl+N` cycles jobs, `Esc` cancels the focused job, double-tap
  `Ctrl+C` exits. The terminal backend is injected so the shell is driven headlessly in tests.
- Each job renders into its **own** transcript pane; only the focused pane is shown, so concurrent jobs
  never write over one another. The projector renders assistant text as markdown at block boundaries
  and collapses each tool call to a single line updated in place from `running…` to `✓/✗ name (N ms)`.
- A sidebar lists all jobs with a state glyph and focus marker, kept live from `JobManager` events;
  focus by number (`Alt+1`–`Alt+9`) or `Ctrl+N`, toggle with `Ctrl+B`, and it auto-collapses below 100
  columns.
- Multi-line composer with prompt history (`Up`/`Down`) and an enqueue-while-busy chooser: `Enter`
  submits (opening the chooser when a job is active — start a new job, append to the focused job, or
  remember the choice), `Alt+Enter`/`Shift+Enter` insert a newline, `Ctrl+Enter` bypasses the chooser.
  The default is read from `settings.json` (`defaultEnqueueBehavior`).
- Command surfaces over a single catalog — a `/`-slash router, key bindings, and a catalog-derived menu
  (`F1`) — all resolving to the same handlers; `/help` lists commands and keys, and the footer shows
  live `jobs/focused` status.
- Interactive tool approval: read-only tools run automatically and mutating tools prompt with an
  approval modal (Approve once / Deny / Always this session). A jobs modal (`F2` / `/jobs`) lists jobs
  and focuses the one you pick.
- Session persistence: the session autosaves at each turn boundary and can be saved with `Ctrl+S` /
  `/save`; `/sessions` browses and resumes saved sessions. Restored sessions render completed
  conversations read-only and mark interrupted jobs as re-run-required (never auto-running them), and
  prompt history survives a restart.
- Appearance and input: pick a theme from a selector (`/theme`) — the whole UI, including the panes
  behind the text, conforms to the chosen theme — open the endpoints / models picker with `Ctrl+E`, and
  toggle mouse capture (`F12` / `/mouse`), which is on by default.
- Built-in `web_retrieve` tool for fetching rendered URL content through headless Playwright Chromium or
  Firefox, with browser installation handled on demand.
- External web search through the `web_search` tool, configurable with Tavily and You.com providers from
  `settings.json` or the interactive `/search` wizard.
- `mux endpoint list` and `mux endpoint show <name>` as top-level non-interactive commands, including
  machine-readable `json` output with redacted secret-like header values.
- `--ignore-cert-errors` (with `--insecure` alias), `settings.json` field `ignoreCertErrors`, and
  `MUX_IGNORE_CERT_ERRORS` for bypassing TLS certificate validation behind enterprise TLS inspection.
- Nullable endpoint-scoped `maxAgentIterations` overrides, with inherited global defaults and additive
  endpoint inspection metadata.
- `settings.json` fields `maxConcurrency` (1–32) and `defaultEnqueueBehavior`
  (`ask`/`run_now`/`queue_after`/`add_to_focused`) for the interactive job model.
- `ARMADA.md` integration guide for orchestrator consumers, plus a tightened `ARMADA_IMPROVEMENTS.md`.
- A narrow mux-owned command dispatcher/parser replacing `Spectre.Console.Cli`.
- `TUIKit` `0.2.0` as the rendering dependency for `Mux.Cli`.

### Changed

- Migrated the entire test suite from the bespoke `TestSuite`/`TestRunner` framework to Touchstone
  runner-agnostic descriptors, executed through a console runner (`Test.Automated`), xUnit
  (`Test.Xunit`), and NUnit (`Test.Nunit`), all on `net8.0` and `net10.0`.
- Endpoint configs can persist `autoApproveTools`, and interactive `always` approvals save
  endpoint-scoped auto-approval for future sessions.
- Agent-loop iteration limits default to `50` and resolve from endpoint `maxAgentIterations` when set,
  otherwise from `settings.json.maxAgentIterations`, with a `1-100` clamp in both places.
- Interactive `/model` aliases `/endpoint` (`list`, `<name>`, `show`, `add`, `edit`, `remove`).
- `mux print` supports `--output-last-message <path>` to write only the final assistant response text;
  failed runs leave the file absent.
- `mux print`, `mux probe`, and `mux endpoint` support `--config-dir <path>` as a first-class
  config-root override, with precedence over `MUX_CONFIG_DIR`.
- `mux probe --require-tools` fails when the selected endpoint disables tool calling.
- Human-readable `print`, `probe`, and interactive errors suggest `--insecure` on a self-signed
  certificate-chain failure while certificate validation is enabled.
- `/mcp add` runs a wizard-driven workflow supporting both `stdio` and HTTP MCP transports, saving to
  `mcp-servers.json`.
- `/endpoint <name>` switches only to configured endpoint names and refreshes endpoint-dependent tool
  guidance after a successful switch.
- README, `USAGE.md`, `CONFIG.md`, and `TESTING.md` updated for the TUIKit UI, the concurrent job/queue
  model, sessions, the new settings, and the three-runner headless test architecture.

### Removed

- Removed direct and transitive `Spectre.Console` usage from `Mux.Cli`, including `Spectre.Console.Cli`.
- Tore down the legacy hand-rolled interactive renderer (`InteractiveCommand`, cursor/chrome layout,
  `LineBuffer`, prompt history, paste heuristics — 11 files) and replaced it with the TUIKit-based
  `MuxTuiApp` shell.
- Removed the previous REPL's queued-message support, `/queue` commands, and `Alt+Up` queued-prompt
  editing (superseded by the concurrent job model and enqueue chooser).
- Removed the interactive `/endpoint <name>` model-override fallback; unknown endpoint names now produce
  an error and leave the selected endpoint unchanged.

### Testing

- The interactive shell is fully covered by deterministic headless suites driven through TUIKit's
  `HeadlessBackend` (no real terminal, no live LLM): shell, projector, sidebar, composer/chooser,
  command surfaces, modals, persistence, rendered-frame golden snapshots, and polish — 335 passing
  checks (plus 7 documented skips) across the console, xUnit, and NUnit runners on `net8.0` and
  `net10.0`, with positive and negative cases throughout.
- Added engine coverage for the job manager/scheduler, the write lease, approval routing, and session
  save/load/resume.
- Added `Test.Xunit` coverage for `--config-dir`, `--output-last-message`, `probe --require-tools`,
  `endpoint list/show`, endpoint-scoped max-iteration persistence/clamping, self-signed certificate-chain
  hint detection, `web_retrieve`, external-search registration, and endpoint-switch no-fallback behavior.
- Added Armada-style `Test.Automated` contract coverage for isolated config directories and endpoint
  inspection.
- Added a GitHub Actions workflow that builds `src/Mux.sln` and runs all three test runners on Linux and
  Windows.

## v0.2.0 - 2026-04-24

### Added

- Interactive endpoint management via `/endpoint list`, `/endpoint show <name>`, guided `/endpoint add` and `/endpoint edit <name>` workflows, and confirmed `/endpoint remove <name>` so endpoints can be inspected and maintained from within mux itself
- Interactive queued-message support in the REPL so users can keep drafting while mux is busy and queue the next prompt with `Tab`
- `/queue`, `/queue clear`, `/queue drop-last`, and `/queue resume` interactive commands for queue inspection and control
- `/status`, `/compact`, and `/title` interactive commands for session inspection, history compaction, and direct title control
- `/compact summary` and `/compact strategy [summary|trim]` so compaction policy can be overridden per command or changed for the live interactive session
- `/compact trim` for explicit trim-only history compaction without a summary-model sidecar call
- `/context` as an interactive alias for `/status`
- `Alt+Up` editing for the newest queued prompt during interactive sessions
- Inline interactive status above the prompt for busy, paused, and approval states
- Automatic conversation-title tracking in interactive mode, including `Conversation title update: ...` transcript notices when the model revises the title
- Estimated context-budget reporting for system prompt, persisted history, tool surface, remaining budget, and compaction metadata
- Compaction-related settings in `settings.json` for automatic preflight compaction, warning threshold, strategy, and preserved turns

### Changed

- `Esc` now cancels the active interactive generation without exiting mux
- Cancelling or failing an interactive run pauses queued-message auto-dispatch until the user resumes it
- Interactive `/clear` now redraws the screen with the current conversation title at the top
- Interactive streamed output now keeps the next `mux>` prompt off the response line and preserves exactly one blank spacer line before the prompt, including when output reaches the bottom of the terminal
- Interactive runs now check the pending prompt against the estimated context budget before starting and automatically compact older history when needed
- `--compaction-strategy <summary|trim>` now overrides the effective compaction policy for interactive, print, and probe startup
- Interactive mode now emits a low-noise post-turn context notice only when the session is approaching or over the usable context budget
- `AgentLoop` now honors the configured compaction strategy for oversized active conversation state before model calls and emits additive `context_status` / `context_compacted` JSONL events plus extended context metadata on `run_started` / `run_completed`
- Non-streaming LLM calls now build non-streaming backend requests, which stabilizes `probe` and the new model-driven title/compaction sidecar calls
- Interactive help and README documentation now describe queueing, cancellation, and the inline status-line behavior

### Testing

- Added endpoint command parser and endpoint persistence unit coverage
- Added `QueuedMessageManager` unit coverage for FIFO dequeue, newest-item editing/removal, and queue clearing
- Verified with `dotnet test src\Mux.sln --nologo`

## v0.1.0 - 2026-03-31

### Added

- Structured CLI output for orchestration with `mux print --output-format jsonl`
- New lifecycle events: `run_started` and `run_completed`
- `mux probe` command for config and backend health validation
- Machine-readable `json` output for `mux probe`
- Best-effort redaction for secret-like values in structured event payloads
- Documentation for the orchestration contract, output formats, exit codes, and `MUX_CONFIG_DIR`
- Shared `contractVersion` marker across `print` JSONL events and `probe` JSON payloads

### Changed

- `mux print` now has a formal non-interactive contract with documented exit codes
- `mux print` `error` events now expose `errorCode`, `failureCategory`, and runtime metadata when known while remaining backward compatible with existing `code` consumers
- Named endpoint selection now fails explicitly when `--endpoint` references a missing endpoint
- CLI approval parsing now accepts documented values `ask`, `auto`, and `deny`
- Tool-call argument parsing is more tolerant of malformed Windows-style path escaping

### Testing

- Expanded `Test.Xunit` coverage for structured formatting, CLI command output, and config resolution
- Expanded `Test.Automated` coverage for lifecycle events, JSONL output, and probe output
- Stabilized mock-server route matching and process-test cleanup behavior

## 2026-03-30

Initial alpha release.
