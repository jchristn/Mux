# Skill Completeness Plan

_Status: in progress (started 2026-10-09). `[ ]` = todo, `[~]` = in progress, `[x]` = done. Update the table and the task lists as work lands; record anything that changed from the design under Deviations at the end._

mux ships 170 default skills and 166 more in optional packs. Count is no longer the problem. The defaults cover building, testing, linting, and formatting in seven language families, git, review, the long-running loops, and a wide spread of cloud and Kubernetes work, all behind the guardrails that keep infrastructure skills from deleting anything or reading secrets. What the library cannot yet tell you is whether the model picks the right skill when you ask for something, and it still has holes in places most real repositories touch: dependency vulnerabilities, databases, shell scripts, version upgrades, logs, and several languages and frameworks.

This plan closes those gaps in the order I would want them closed. The evaluation comes first because it is the only phase that makes the later ones measurable. Every skill added after it ships with evaluation cases, so a new skill that steals prompts from an old one, or never gets picked at all, fails a test instead of quietly making the library worse.

## Summary

| # | Phase | Category | New skills | Status |
|---|---|---|---|---|
| 1 | Skill selection evaluation | (tooling) | none | [x] |
| 2 | Dependency vulnerability audit and SBOM | security | `deps-audit`, `sbom` | [x] |
| 3 | Databases: migrations, SQL, NoSQL, and graph | data | `db-migrate`, `sql-*` (5), `nosql-*` (4), `graph-neo4j`, `graph-litegraph` | [x] |
| 4 | Shell and PowerShell linting | hygiene | `shell-lint` | [x] |
| 5 | Version upgrades | languages | `dotnet-upgrade`, `node-upgrade`, `py-upgrade` | [x] |
| 6 | Missing languages and mobile | languages, mobile | `ruby-*`, `php-*`, `swift-*`, `android-*`, `flutter-*` (15) | [x] |
| 7 | Frontend frameworks beyond React | frontend | `web-framework`, `storybook` | [x] |
| 8 | Logs and runtime diagnosis | debugging | `log-triage`, `port-inspect`, `bench` | [x] |
| 9 | Missing infrastructure tools | infrastructure | `ansible`, `bicep`, `cloudformation`, `cdk` | [ ] |
| 10 | API contracts | review | `openapi` | [ ] |
| 11 | Pack description cleanup | (packs) | none | [ ] |

Each skill follows the existing conventions: a pwsh command per action built with `ToolchainSkillFactory` or a family `Skill(...)` helper, `appliesTo` and `requiresTools` gates so it only lists where it applies, exit codes 0 (success), 1 (the tool reported problems), 2 (the tool or project is missing), and 3 (refused by the production guard) where something can change state, a dry run through `MUX_SKILL_DRY_RUN`, and a category in `DefaultSkillCategories`. `BUILTIN_SKILLS.md`, the README skill count, and the CHANGELOG are updated in the same commit as the skills.

## Phase 1: Skill selection evaluation

The model never reads a skill body before choosing. It sees one line per listed skill, `- name: description`, after `appliesTo` and `requiresTools` have dropped the skills that do not fit the project, and it picks from that. So a skill fails in one of two ways: it is not listed when it should be (a gating bug), or it is listed and its name and description do not match the way people ask for it (a wording bug). Both can be measured without a model, and the second can also be measured with one.

The offline evaluation is deterministic and runs in the test suites. Each case names a prompt, a small project fixture (a list of files to create, such as `go.mod` or `package.json`), and the skill or skills that should handle it. The harness builds the fixture in a temporary directory, computes the listing exactly as `SkillRuntime` does, and ranks the listed skills against the prompt with BM25 over the skill name (split on dashes) and description. A case passes when the expected skill is listed and ranks in the top three. The suite reports top-1 and top-3 rates and fails below a floor, so the floor can only move up.

Lexical ranking is a proxy, and I would rather have a cheap proxy that runs on every commit than a perfect signal nobody runs. The live mode covers the rest: `mux skill eval --live` sends each prompt to the configured endpoint with the real listing and the real skill tools, records which skill the model reads or runs first, and prints the same table. It is a manual check, never part of the test suites, because it needs a model and costs tokens.

- [x] `Mux.Core/Skills/Evaluation/`: `SkillEvalCase`, `SkillEvalCaseResult`, `SkillEvalReport`, `SkillCollision`, `SkillSelectionScorer` (BM25 over name and description), and `SkillSelectionEvaluator` (fixture, listing, ranking).
- [x] The listing computed by the same gating code `SkillRuntime` uses, not a copy of it.
- [x] Built-in evaluation cases as an embedded `skill-eval.json` resource: 120 cases covering every default category.
- [x] Description collision report: pairs of listed skills whose descriptions are near duplicates.
- [x] `mux skill eval [case-id] [--top <n>] [--cases <file>] [--output-format json]` (offline).
- [x] `mux skill eval --live [--endpoint <name>]`: ask the model, record the first skill it reads or runs (`SkillModelChooser`; the evaluator takes any chooser through `EvaluateCaseAsync`).
- [x] Touchstone `SkillSelectionEvalSuite`: gating for every case, top-3 floor, ranking sanity, collisions, the CLI verb (offline and `--live` against a mock endpoint), and negative cases (unknown skill in a case, empty prompt, a fixture that gates the skill out, an unknown or dead endpoint).
- [x] Fix what the first run flagged, and set the floor. Baseline: 103 of 120 passed, top-1 84%, top-3 89%, 6 gating failures (every `dotnet-*` skill was listed in every project). After gating the `dotnet-*` skills on .NET project files and rewriting 11 descriptions: 118 of 120, top-1 91%, top-3 98%, no gating failures. Floor: top-3 97%, top-1 90%.
- [x] Docs: `docs/USAGE.md` (Evaluating skill selection), CHANGELOG.

## Phase 2: Dependency vulnerability audit and SBOM

Every ecosystem has its own audit command and none of them agree on output. `deps-audit` detects the project types under the repository root and runs each one's auditor, then prints one summary: ecosystem, package, installed version, advisory, severity, and the fixed version when there is one. `osv-scanner` is used when it is installed because it covers every lockfile at once; otherwise the native tools run.

- [x] `deps-audit audit`: `npm audit --json` / `pnpm audit --json` / `yarn npm audit --json`, `pip-audit -f json`, `cargo audit --json`, `dotnet list package --vulnerable --include-transitive --format json`, `govulncheck -json ./...`; `osv-scanner --format json -r .` when installed.
- [x] `sbom write [file]` (its own skill; see Deviations): CycloneDX through `syft scan`, otherwise exit 2 with the install hint. Relative paths inside the repository only.
- [x] Exit 1 when any advisory at or above `--min-severity` (default `high`) is found.
- [x] Tests (`DependencyAuditSuite`, 14 cases): parsing each tool's JSON from recorded fixtures, the merged summary, severity threshold, a missing tool, a project with no lockfile.

## Phase 3: Databases

Phase 3 started as two skills, `db-migrate` and a generic `db-query`, and grew on request into one basic skill per platform: every major SQL database, the common NoSQL stores, and two graph databases, LiteGraph among them. `db-migrate` is the only one that changes anything, and it goes through the production guard. Every platform skill does the same four or so things (connect, list, describe, query) and none of them can write.

Each platform blocks writes in its own way, because no single trick works everywhere. SQLite opens the file with `-readonly`; Postgres sets `default_transaction_read_only`; MySQL and MariaDB run inside `START TRANSACTION READ ONLY`; SQL Server wraps the statement in a transaction that is always rolled back; Oracle uses `SET TRANSACTION READ ONLY`; Neo4j uses `--access-mode read`. On top of that, the SQL skills accept only read statements (SELECT, WITH, SHOW, EXPLAIN, and the like) one at a time, and the NoSQL and LiteGraph skills offer only read operations. Connection strings always come from a named environment variable, so the secret never reaches the command line shown to the model, the transcript, or a dry run; the tests plant a marker secret and check every output for it.

- [x] `db-migrate detect|status|plan|apply`: EF Core, Prisma, Alembic, Django, Rails, Flyway, golang-migrate; `--tool` to choose; `apply` guarded by the production pattern against the environment name (or the database URL's host), exit 3 unless `--confirm`.
- [x] SQL: `sql-sqlite`, `sql-postgres`, `sql-mysql` (MySQL and MariaDB), `sql-sqlserver` (SQL Server and Azure SQL), `sql-oracle`, each with `ping`, `tables`, `describe`, and `query`.
- [x] NoSQL: `nosql-mongodb` (ping, databases, collections, find, count, indexes), `nosql-redis` (ping, info, keys via SCAN, type-aware get), `nosql-dynamodb` (tables, describe, scan, get), `nosql-cassandra` (keyspaces, tables, describe, SELECT).
- [x] Graph: `graph-neo4j` (ping, labels, relationships, read-mode Cypher) and `graph-litegraph` (ping, tenants, graphs, stats, nodes, edges, and native `MATCH ... RETURN` queries over the REST API on port 8701, with `$LITEGRAPH_ENDPOINT` and `$LITEGRAPH_API_KEY` as LiteGraph's MCP server uses them).
- [x] Tests (`DatabaseSkillsSuite`, 11 cases): a real SQLite database (reads work; DELETE, DROP, stacked statements, and injected identifiers are refused; `-readonly` blocks a PRAGMA write), dry runs for every other platform with a planted secret that must never appear, write refusals per engine, missing connection variables, LiteGraph against a stub server (token sent, HTTP 401 exits 1, dead server exits 2), migration detection, plans, and the guard.
- [x] Evaluation cases for every platform (17 cases).

## Phase 4: Shell and PowerShell linting

mux's own `scripts/` and most of its skills are shell and pwsh, and nothing lints them. `shell-lint` finds `*.sh`, `*.bash`, and files with a shell shebang, runs `shellcheck -f gcc`, then finds `*.ps1` and `*.psm1` and runs `Invoke-ScriptAnalyzer` when the PSScriptAnalyzer module is installed.

- [x] `shell-lint check [path]` and `shell-lint changed` (only files changed against the base branch).
- [x] Tests (`ScriptLintSuite`, 9 cases): a script with a known shellcheck finding, a clean script, PSScriptAnalyzer present and absent, the changed-files mode.

## Phase 5: Version upgrades

Upgrades are chores with checkable steps, which makes them a good fit for hybrid skills: a short playbook body plus commands that do the mechanical part and verify it.

- [x] `dotnet-upgrade plan|apply <tfm>`: lists every `TargetFramework(s)` and `global.json`, rewrites them, then builds. `apply` is mutating and reports each file changed.
- [x] `node-upgrade plan|apply <major>`: `.nvmrc`, `.node-version`, `engines.node`, `@types/node`, CI `node-version` entries.
- [x] `py-upgrade plan|apply <version>`: `requires-python`, `.python-version`, classifiers, CI matrices; runs `pyupgrade` when installed.
- [x] Tests (`UpgradeSkillsSuite`, 7 cases): fixtures for each, plan changes nothing (file hashes compared), apply changes exactly the listed text, a UTF-8 BOM and CRLF line endings survive, a dry-run apply builds nothing, bad versions and empty projects are rejected.

## Phase 6: Missing languages and mobile

Each family matches the existing language families (build, test, lint, format, dependencies) and adds a `mobile` category where it applies.

- [x] Ruby (`Gemfile`): `ruby-deps` (bundle install/outdated), `ruby-test` (rspec or minitest), `ruby-lint` (rubocop).
- [x] PHP (`composer.json`): `php-deps`, `php-test` (phpunit or pest), `php-lint` (phpstan, php-cs-fixer).
- [x] Swift (`Package.swift`, `*.xcodeproj`): `swift-build`, `swift-test`, `swift-format`.
- [x] Android (`build.gradle*` with the Android plugin): `android-build`, `android-test`, `android-lint` through the Gradle wrapper.
- [x] Flutter (`pubspec.yaml`): `flutter-analyze`, `flutter-test`, `flutter-build`.
- [x] Tests (`MobileLanguageSkillsSuite`, 7 cases): definitions and categories, runner choice (RSpec, rails test, rake test; Pest over PHPUnit; swift versus xcodebuild; flutter versus dart), the Gradle wrapper from a subdirectory, and refusals. A `mobile` category was added on every surface.

## Phase 7: Frontend frameworks beyond React

Rather than seven near-identical families, one `web-framework` skill detects Next.js, Nuxt, Vue, SvelteKit, Svelte, Angular, Astro, and Vite, and maps `dev`, `build`, `lint`, `test`, and `upgrade-check` to whatever that framework uses. `react-*` stays as it is.

- [x] `web-framework detect|build|lint|test|upgrade-check`; `dev` runs as a background process like `react-dev-server`.
- [x] `storybook build|test|dev`.
- [x] Tests (`WebFrameworkSkillsSuite`, 6 cases): Next.js, Angular under pnpm, SvelteKit fallbacks, Vue without a linter, plain React refused, and Storybook.

## Phase 8: Logs and runtime diagnosis

- [x] `log-triage summarize <file>`: groups errors and exceptions by normalized message (numbers, GUIDs, and paths masked), counts them, and shows the first and last occurrence with the stack trace.
- [x] `port-inspect [port]`: what is listening and which process owns it (`lsof`, `ss`, or `Get-NetTCPConnection`). Read-only; it never kills anything.
- [x] `bench time|dotnet|k6`: hyperfine for commands (or a built-in timing loop when it is missing), BenchmarkDotNet projects, and `k6 run` for load scripts.
- [x] Tests (`RuntimeSkillsSuite`, 5 cases): a recorded log with repeated .NET errors, a Python traceback, and JSON entries; port inspection against a listener the test opens (its own PID must appear); real in-process timing; BenchmarkDotNet discovery and k6 dry runs; bad input.

## Phase 9: Missing infrastructure tools

Same guardrails as `terraform` and `pulumi`: read and preview freely, apply only with confirmation and never against production without it, never destroy.

- [ ] `ansible lint|syntax|check|apply` (`--check --diff` for preview).
- [ ] `bicep build|lint|what-if|deploy`.
- [ ] `cloudformation validate|lint|changeset|deploy` (cfn-lint, change sets described before execution).
- [ ] `cdk synth|diff|deploy`.
- [ ] Tests: dry runs, the production guard, no destroy path exists in any command.

## Phase 10: API contracts

- [ ] `openapi validate|lint|diff|client`: validation and lint through `redocly` or `spectral`, breaking-change diff through `oasdiff`, client generation through `openapi-generator-cli` into a named directory.
- [ ] Tests: a valid and an invalid spec, a breaking diff, a missing tool.

## Phase 11: Pack description cleanup

The imported packs keep their source's voice, and some descriptions read like a persona ("World-class senior data scientist skill...") instead of telling the model when to use the skill. The evaluation from phase 1 makes this measurable, so the cleanup is a rewrite of each pack description into a trigger sentence plus what the skill does, checked by the evaluation and a lint rule.

- [ ] Rewrite pack descriptions that open with a persona or marketing phrasing.
- [ ] `ShippedSkillsSuite` rule: no pack or default description contains "world-class", "senior", "expert", or similar persona words, and every description says when to use the skill.
- [ ] Evaluation cases for the packs, run with the pack installed.

## Risks

The lexical evaluation can be gamed. Stuffing a description with keywords would raise its rank and make the listing worse for the model. Reviews of description changes should read the new text as the model would, and the collision report is there to catch one skill crowding out another.

Phases 6 through 10 add perhaps forty skills. Gating keeps the listing small in any one project, but a monorepo with many ecosystems will see more lines. The evaluation's top-3 floor is the check on that: if adding skills pushes existing ones down, it fails.

Some tools these skills call (osv-scanner, syft, oasdiff, PSScriptAnalyzer) are not installed on most machines, including CI. Tests run their parsing against recorded output and their commands as dry runs, and a missing tool exits 2 with an install hint rather than failing.

## Deviations

- Phase 8: `bench` times commands with a built-in loop when hyperfine is missing, so it is useful on a stock machine; `log-triage` keys a Python traceback by its final exception line, which is the informative one. Phase 7: dev servers are described for process_start, as react-dev-server does, rather than started by the skill. The evaluation case for starting a SvelteKit dev server accepts react-dev-server too, because it handles any Vite dev script.
- Phase 5: `dotnet-upgrade` adds the new framework to a `TargetFrameworks` list rather than replacing one, since multi-targeting is usually deliberate, and leaves `@types/node`, classifiers, matrices, and framework packages to the user as notes. Phase 6: Android is gated on `AndroidManifest.xml` rather than the Android Gradle plugin, which a glob cannot see; Swift covers Xcode projects through `xcodebuild` schemes as well as packages; the evaluation floor rose to top-3 98% and top-1 93% (155 of 157 cases).
- Phase 3: the generic `db-query` was replaced by one skill per platform after the user asked for basic coverage of every major SQL, NoSQL, and graph database, including LiteGraph. LiteGraph is reached through its REST API rather than its `lg` console, because `lg` in database-file mode initializes the file it is given, which is a write. SQLite statements go to `sqlite3` on standard input, because a statement starting with a `--` comment was read as a command-line option.
- Phase 2: the SBOM is its own `sbom` skill rather than a `deps-audit` command, because writing a file makes a skill mutating, and an audit should not need approval. Advisories without a severity (pip-audit, cargo-audit, govulncheck) count as high for the threshold, so a missing severity never hides a problem. The `deps-audit` description names "security advisories (CVEs)" and "Go modules" because the first evaluation run with it missed prompts phrased that way.
- Phase 1: the `dotnet-*` skills had no `appliesTo` and were listed in every project. They are now gated on `*.sln`, `*.slnx`, `**/*.csproj`, `**/*.fsproj`, `**/*.vbproj`, and `global.json`. The two cases still missing the top three ("which virtualenv", "profile this service, it got slow") are limits of the lexical scorer (no synonyms, crude stemming), so they stay in as honest misses rather than being tuned away.
