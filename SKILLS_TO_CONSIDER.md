# Skills to Consider

_Status: imported (2026-10-09). All 197 skills landed: 17 bundled defaults, 166 optional-pack skills, and 14 merges into existing mux skills. `[x]` means done; `[~]` marks script-bearing skills whose scripts were copied and wired through `${SKILL_DIR}` but not executed or wrapped as commands yet (see each Notes line). Phase 0 (directory exposure, placeholder substitution, the importer, pack install, notices, tests, and docs) is done. Every skill also has a category, shown and editable on every surface._

Every skill in [alirezarezvani/claude-skills](https://github.com/alirezarezvani/claude-skills) at commit `19392f7a08264ed00486a251f5b2098321771f94` (committed 2026-08-26) gets a decision here: ship it with mux as a default, offer it in an opt-in pack, fold its best ideas into a skill mux already has, or leave it alone.

The repository is large and repetitive, so the count needs explaining. It holds 846 `SKILL.md` files. 458 of them sit under `.gemini/`, a mirror of the same skills for Gemini CLI, and are not counted. Of the remaining 388, fourteen are copies of the same skill stored in two folders (the c-level advisors, the ISO and EU AI Act specialists, and a few engineering skills appear twice). That leaves 376 distinct skills. This list keeps the **197** worth acting on: every skill recommended *Skip*, and every skill scoring below 10, was removed on 2026-10-08 (179 skills), so each remaining skill has a row in the table and a section below. Two names are used twice for different skills (`handoff` and `run`), so those rows carry a folder name in parentheses.

Most of the collection is not about code. Roughly a third is engineering and security; the rest is executive advisory, marketing, compliance, product, finance, and personal productivity. mux is a coding agent, so the scoring leans hard toward what a developer hits in a normal session. That is a judgment, and the Value column is where it shows.

## How the scores work

**Simplicity** (1 to 10) is how cheaply the skill lands in mux. A 10 is a drop-in. Points come off for bundled scripts (they need mux to know the skill's folder, see Phase 0), third-party Python packages, Claude Code primitives mux does not have (CronCreate, the Workflow tool, Claude Managed Agents), references to `CLAUDE.md` or `~/.claude` that must be rewritten, reads from sibling skills, and bodies over 400 lines, which crowd a small model's context.

**Value** (1 to 10) is how much the skill moves mux toward what its users need. A 10 is a gap people hit in their first session. Engineering skills that fill a real gap score 5 to 7. Skills that duplicate an existing mux default score lower and are usually marked **Adapt**, because merging the good parts beats shipping two skills that compete for the same request. Business, marketing, and compliance skills mostly score 2: they are well written, but a coding agent's users rarely need a board-deck builder.

**Total** is the sum. The table is sorted by Total, then Value.

**Recommendation** is one of four outcomes. *Adopt as default* means ship it in mux's seeded library, gated with `appliesTo` so it only appears where it applies. *Optional pack* means import it into an opt-in pack (engineering, security, data, docs, product, research, productivity, marketing, compliance, or business) that a user installs on purpose; packs keep mux's default listing small. *Adapt into X* means fold the useful parts into existing skill X. *Skip* meant no work; those skills, and every skill with a total below 10, have been removed from this list.

## License and attribution

The repository is MIT-licensed (copyright 2025 Alireza Rezvani). MIT allows copying, modifying, and redistributing the skills, including inside mux, as long as the copyright and permission notice travel with the copied material. In practice that means one `THIRD_PARTY_NOTICES.md` entry for the repository and a `source:` line in each imported skill's frontmatter. Six skills declare that they are derived from [mattpocock/skills](https://github.com/mattpocock/skills), which is also MIT; those need their own notice line naming the original author. No skill states a different license.

## Phase 0: prerequisites in mux

None of the scored work should start before these land. The biggest one is easy to miss: mux's `skill` tool tells the model a skill's instructions, its commands, and the files under `resources/`, but never the skill's own folder. Claude-format skills say `python scripts/foo.py` and expect to be run from their folder, and this repository alone has 1,258 such references spread over the 245 skills that bundle scripts. Today a model reading one of these skills in mux would run the script from the project directory and fail. Fixing that once makes every script-bearing skill in this plan (and in any other Claude skill collection) work.

The other prerequisites follow from the numbers (counted across all 376 skills in the repository, before the list was trimmed). 347 of them contain em-dashes, which mux's default-skill test rejects and which the repository's writing rules forbid everywhere, and 95 mention Claude Code files or primitives (`CLAUDE.md`, `~/.claude`, CronCreate, the Workflow tool, `${CLAUDE_PLUGIN_ROOT}`) that mean nothing in mux. Normalizing those by hand 376 times would be silly, so the importer does it.

- [x] Expose the skill directory to the model: add `directory` (absolute path) to the `skill` tool's result, and list the files under `scripts/`, `references/`, `assets/`, and `templates/` alongside `resources/`.
- [x] Substitute path placeholders in skill bodies when the `skill` tool returns them: `{baseDir}`, `{skill_path}`, `$SKILL`, `$SKILL_ROOT`, and `${CLAUDE_PLUGIN_ROOT}` become the skill's absolute folder, and a bare `scripts/` or `references/` path is rewritten relative to it.
- [x] Build a skill importer (`mux skill import <folder|git-url> [--pack <name>]`) that copies a Claude-format skill, normalizes em-dashes and en-dashes, rewrites `CLAUDE.md` and `~/.claude` references to their mux equivalents (`MUX.md`, `~/.mux`), adds `source:` and `license:` frontmatter, and runs `mux skill validate` on the result.
- [x] Add optional packs (landed as `src/Mux.Core/Skills/Packs/<name>/`, embedded in Mux.Core, with `mux skill pack list|show|install|remove`, `/packs`, the dashboard, the desktop app, and `/v1.0/api/skills/packs`): a `packs/<name>/` layout in the repository, `mux skill pack list|install|remove <name>`, and a `/skills` inventory action. Installing copies the pack's skills into `~/.mux/skills` through the same seeding path the defaults use, so edits and deletions are respected.
- [x] Add `THIRD_PARTY_NOTICES.md` with the MIT notice for alirezarezvani/claude-skills and mattpocock/skills.
- [x] Tests: an `ImportedSkillsSuite` covering the directory field, placeholder substitution, the importer's normalization (em-dashes, Claude references, frontmatter), pack install and removal, and a sample script-bearing skill run end to end.
- [x] Docs: SKILLS_AUTHORING.md (bundled folders and placeholders), USAGE.md (packs and the importer), README, and the CHANGELOG.

## Summary

Seventeen skills are worth shipping as defaults, and they cluster: the Playwright testing family, accessibility auditing, a few engineering workflow skills (TDD, CI generation, performance profiling, API design review, a pre-release gate), and three skills that suit mux's design closely (inspecting an untrusted skill before trusting it, a security hook built for exactly the hook contract mux now has, and turning a solved problem into a new skill). Fourteen more are better merged into skills mux already has. The long tail (166 skills) goes into opt-in packs, where it costs nothing until someone wants it. The 49 skipped skills and the 130 others scoring below 10 are no longer listed.

| # | Skill | Category | What it does | Overlap with mux | Simplicity | Value | Total | Recommendation | Status |
|---:|---|---|---|---|---:|---:|---:|---|:---:|
| 1 | [coverage](#1-coverage) | Engineering | Analyze test coverage gaps. | none (no Playwright test skills) | 9 | 6 | **15** | Adopt as default | [x] |
| 2 | [fix](#2-fix) | Engineering | Fix failing or flaky Playwright tests. | flaky-test-hunt (detection only) | 9 | 6 | **15** | Adopt as default | [x] |
| 3 | [generate](#3-generate) | Engineering | Generate Playwright tests. | none (no Playwright test skills) | 9 | 6 | **15** | Adopt as default | [x] |
| 4 | [pw-init](#4-pw-init) | Engineering | Set up Playwright in a project. | none (no Playwright test skills) | 9 | 6 | **15** | Adopt as default | [x] |
| 5 | [a11y-audit](#5-a11y-audit) | Engineering | Accessibility audit skill for scanning, fixing, and verifying WCAG 2.2 Level A and AA compliance across... | none | 7 | 7 | **14** | Adopt as default | [~] |
| 6 | [skill-security-auditor](#6-skill-security-auditor) | Engineering | Security audit and vulnerability scanner for AI agent skills before installation. | project skill trust gate (partial) | 7 | 7 | **14** | Adopt as default | [~] |
| 7 | [adversarial-reviewer](#7-adversarial-reviewer) | Engineering | Adversarial code review that breaks the self-review monoculture. | code-review (partial) | 8 | 6 | **14** | Adapt into code-review | [x] |
| 8 | [extract](#8-extract) | Engineering | Turn a proven pattern or debugging solution into a standalone reusable skill with SKILL.md, reference... | new-skill (scaffold only) | 8 | 6 | **14** | Adopt as default | [x] |
| 9 | [security-guidance](#9-security-guidance) | Engineering | PreToolUse security-anti-pattern hook for Claude Code. | tool hooks (mechanism only) | 8 | 6 | **14** | Adopt as default | [x] |
| 10 | [api-test-suite-builder](#10-api-test-suite-builder) | Engineering | Use when the user asks to generate API tests, create integration test suites, test REST endpoints, or... | none | 9 | 5 | **14** | Optional pack: engineering | [x] |
| 11 | [database-schema-designer](#11-database-schema-designer) | Engineering | Use when the user asks to create ERD diagrams, normalize database schemas, design table relationships, or... | none | 9 | 5 | **14** | Optional pack: engineering | [x] |
| 12 | [deep-research](#12-deep-research) | Research | Run a disciplined, multi-source research investigation for a high-stakes question or decision. | web_search, web_retrieve tools | 9 | 5 | **14** | Optional pack: research | [x] |
| 13 | [focused-fix](#13-focused-fix) | Engineering | Use when the user asks to fix, debug, or make a specific feature/module/area work end-to-end. | debug, fix-until-green | 9 | 5 | **14** | Adapt into debug | [x] |
| 14 | [full-page-screenshot](#14-full-page-screenshot) | Engineering | Use when the user asks to capture a full-page screenshot, long screenshot, or complete page capture of a... | web_retrieve (Playwright-backed, read-only) | 9 | 5 | **14** | Optional pack: engineering | [x] |
| 15 | [migrate](#15-migrate) | Engineering | Migrate from Cypress or Selenium to Playwright. | none (no Playwright test skills) | 9 | 5 | **14** | Adopt as default | [x] |
| 16 | [pw-review](#16-pw-review) | Engineering | Review Playwright tests for quality. | none (no Playwright test skills) | 9 | 5 | **14** | Adopt as default | [x] |
| 17 | [report](#17-report) | Engineering | Generate test report. Use when user says "test report", "results summary", "test status", "show results",... | none (no Playwright test skills) | 9 | 5 | **14** | Adopt as default | [x] |
| 18 | [ci-cd-pipeline-builder](#18-ci-cd-pipeline-builder) | Engineering | Generate pragmatic CI/CD pipelines from detected project stack signals. | ci-watch (watches, does not generate) | 7 | 6 | **13** | Adopt as default | [~] |
| 19 | [performance-profiler](#19-performance-profiler) | Engineering | Systematic performance profiling for Node.js, Python, and Go applications. | none | 7 | 6 | **13** | Adopt as default | [~] |
| 20 | [ship-gate](#20-ship-gate) | Engineering | Pre-production audit that scans a codebase for security, database, deployment, code quality, AI/LLM,... | security-review, code-review (partial) | 7 | 6 | **13** | Adopt as default | [~] |
| 21 | [ar-resume](#21-ar-resume) | Engineering | Resume a paused experiment. | /loop, loop-until, fix-until-green | 9 | 4 | **13** | Optional pack: engineering | [x] |
| 22 | [ar-status](#22-ar-status) | Engineering | Show experiment dashboard with results, active loops, and progress. | /loop, loop-until, fix-until-green | 9 | 4 | **13** | Optional pack: engineering | [x] |
| 23 | [board](#23-board) | Engineering | Read, write, and browse the AgentHub message board for agent coordination. | worktree isolation, subagents | 9 | 4 | **13** | Optional pack: engineering | [x] |
| 24 | [boost-asio-pro](#24-boost-asio-pro) | Engineering | Use when writing or reviewing asynchronous C++ networking code with Boost.Asio or standalone Asio. | cpp-* (build tooling only) | 9 | 4 | **13** | Optional pack: engineering | [x] |
| 25 | [code-tour](#25-code-tour) | Engineering | Use when the user asks to create a CodeTour .tour file. | explain-codebase (partial) | 9 | 4 | **13** | Optional pack: engineering | [x] |
| 26 | [eval](#26-eval) | Engineering | Evaluate and rank agent results by metric or LLM judge for an AgentHub session. | worktree isolation, subagents | 9 | 4 | **13** | Optional pack: engineering | [x] |
| 27 | [hub-init](#27-hub-init) | Engineering | Create a new AgentHub collaboration session with task, agent count, and evaluation criteria. | worktree isolation, subagents | 9 | 4 | **13** | Optional pack: engineering | [x] |
| 28 | [hub-status](#28-hub-status) | Engineering | Show DAG state, agent progress, and branch status for an AgentHub session. | worktree isolation, subagents | 9 | 4 | **13** | Optional pack: engineering | [x] |
| 29 | [llm-cost-optimizer](#29-llm-cost-optimizer) | Engineering | Use proactively whenever LLM API costs come up -- or should. | none | 9 | 4 | **13** | Optional pack: engineering | [x] |
| 30 | [loop-library](#30-loop-library) | Agent loops | Discover, find, compare, audit, repair, adapt, and design repeatable AI-agent loops with explicit... | /loop, loop skills | 9 | 4 | **13** | Optional pack: engineering | [x] |
| 31 | [merge](#31-merge) | Engineering | Merge the winning agent's branch into base, archive losers, and clean up worktrees. | worktree isolation, subagents | 9 | 4 | **13** | Optional pack: engineering | [x] |
| 32 | [minimalist](#32-minimalist) | Engineering | Use when the user asks to write code efficiently, avoid over-engineering, reduce dependencies, or prevent... | none | 9 | 4 | **13** | Optional pack: engineering | [x] |
| 33 | [pr-review-expert](#33-pr-review-expert) | Engineering | Use when the user asks to review pull requests, analyze code changes, check for security issues in PRs, or... | code-review, pr-comments | 9 | 4 | **13** | Adapt into code-review | [x] |
| 34 | [prompt-governance](#34-prompt-governance) | Engineering | Use when managing prompts in production at scale: versioning prompts, running A/B tests on prompts,... | none | 9 | 4 | **13** | Optional pack: engineering | [x] |
| 35 | [run (agenthub)](#35-run-agenthub) | Engineering | One-shot lifecycle command that chains init → baseline → spawn → eval → merge in a single invocation. | worktree isolation, subagents | 9 | 4 | **13** | Optional pack: engineering | [x] |
| 36 | [run (autoresearch-agent)](#36-run-autoresearch-agent) | Engineering | Run a single experiment iteration. | /loop, loop-until, fix-until-green | 9 | 4 | **13** | Optional pack: engineering | [x] |
| 37 | [self-eval](#37-self-eval) | Engineering | Honestly evaluate AI work quality using a two-axis scoring system. | none | 9 | 4 | **13** | Optional pack: engineering | [x] |
| 38 | [setup](#38-setup) | Engineering | Set up a new autoresearch experiment interactively. | /loop, loop-until, fix-until-green | 9 | 4 | **13** | Optional pack: engineering | [x] |
| 39 | [spawn](#39-spawn) | Engineering | Launch N parallel subagents in isolated git worktrees to compete on the session task. | worktree isolation, subagents | 9 | 4 | **13** | Optional pack: engineering | [x] |
| 40 | [strict-api](#40-strict-api) | Engineering | Use when the user says 'no hallucinations', 'verify APIs', 'reality check', or 'don't invent functions'. | none | 9 | 4 | **13** | Optional pack: engineering | [x] |
| 41 | [zero-hallucination-coder](#41-zero-hallucination-coder) | Engineering | Runs a disciplined Discuss -> Map -> Decompose -> Execute -> Verify loop that grounds code in verified... | none | 9 | 4 | **13** | Optional pack: engineering | [x] |
| 42 | [api-design-reviewer](#42-api-design-reviewer) | Engineering | Comprehensive REST API design review with automated linting, breaking-change detection, and design scorecards. | none | 6 | 6 | **12** | Adopt as default | [~] |
| 43 | [ai-security](#43-ai-security) | Engineering | Use when assessing AI/ML systems for prompt injection, jailbreak vulnerabilities, model inversion risk,... | security-review (code only) | 7 | 5 | **12** | Optional pack: security | [~] |
| 44 | [changelog-generator](#44-changelog-generator) | Engineering | Produce consistent, auditable release notes from Conventional Commits. | git-changelog-entry, release-notes | 7 | 5 | **12** | Adapt into release-notes | [x] |
| 45 | [cloud-security](#45-cloud-security) | Engineering | Use when assessing cloud infrastructure for security misconfigurations, IAM privilege escalation paths, S3... | security-review (code only) | 7 | 5 | **12** | Optional pack: security | [~] |
| 46 | [code-reviewer](#46-code-reviewer) | Engineering | Code review automation for TypeScript, JavaScript, Python, Go, Swift, Kotlin, C#, .NET, Java, C, C++,... | code-review | 7 | 5 | **12** | Adapt into code-review | [x] |
| 47 | [codebase-onboarding](#47-codebase-onboarding) | Engineering | Analyze a codebase and generate onboarding documentation for engineers, tech leads, and contractors. | init, explain-codebase | 7 | 5 | **12** | Adapt into explain-codebase | [x] |
| 48 | [database-designer](#48-database-designer) | Engineering | Use when the user asks to design database schemas, plan data migrations, optimize queries, choose between... | none | 7 | 5 | **12** | Optional pack: engineering | [~] |
| 49 | [dependency-auditor](#49-dependency-auditor) | Engineering | Audit and manage dependencies across multi-language projects. | js-deps, py-deps, dotnet-outdated, java-deps, go-mod | 7 | 5 | **12** | Adapt into security-review | [x] |
| 50 | [mcp-server-builder](#50-mcp-server-builder) | Engineering | Design and ship production-ready MCP (Model Context Protocol) servers from OpenAPI contracts instead of... | mux mcp serve (consumer side only) | 7 | 5 | **12** | Optional pack: engineering | [~] |
| 51 | [runbook-generator](#51-runbook-generator) | Engineering | Generate operational runbooks from a service name. | none | 7 | 5 | **12** | Optional pack: engineering | [~] |
| 52 | [senior-security](#52-senior-security) | Engineering | Use when the user asks for STRIDE threat modeling, DREAD risk scoring, data-flow-diagram threat analysis,... | security-review, git-secret-scan | 7 | 5 | **12** | Adapt into security-review | [x] |
| 53 | [spec-driven-workflow](#53-spec-driven-workflow) | Engineering | Use when the user asks to write specs before code, define acceptance criteria, plan features before... | plan mode (partial) | 7 | 5 | **12** | Optional pack: engineering | [~] |
| 54 | [write-a-skill](#54-write-a-skill) | Engineering | Create new agent skills with proper structure, progressive disclosure, and bundled resources. | new-skill | 7 | 5 | **12** | Adapt into new-skill | [x] |
| 55 | [email-template-builder](#55-email-template-builder) | Engineering | Build complete transactional email systems: React Email templates, provider integration (Resend, Postmark,... | none | 8 | 4 | **12** | Optional pack: engineering | [x] |
| 56 | [playwright-pro](#56-playwright-pro) | Engineering | Production-grade Playwright testing toolkit. | none | 8 | 4 | **12** | Adapt into the Playwright family | [x] |
| 57 | [stripe-integration-expert](#57-stripe-integration-expert) | Engineering | Production-grade Stripe integrations: subscriptions with trials and proration, one-time payments,... | none | 8 | 4 | **12** | Optional pack: engineering | [x] |
| 58 | [cto-review](#58-cto-review) | Executive advisory | Architecture and scaling interrogation. | none | 9 | 3 | **12** | Optional pack: business | [x] |
| 59 | [deepread](#59-deepread) | Research | Use when the user asks to deeply read a book, article, PDF, or document set; extract claims and evidence;... | none | 9 | 3 | **12** | Optional pack: research | [x] |
| 60 | [embedded-iot-mentor](#60-embedded-iot-mentor) | Engineering | Mentor for embedded and IoT hardware projects. | none | 9 | 3 | **12** | Optional pack: engineering | [x] |
| 61 | [team-communications](#61-team-communications) | Project management | Write internal company communications. | none | 9 | 3 | **12** | Optional pack: product | [x] |
| 62 | [tdd-guide](#62-tdd-guide) | Engineering | Test-driven development skill for writing unit tests, generating test fixtures and mocks, analyzing... | test-gap-review (partial) | 5 | 6 | **11** | Adopt as default | [~] |
| 63 | [handoff (productivity)](#63-handoff-productivity) | Productivity | Compact the current conversation into a handoff document for another agent to pick up. | none (sessions persist, but no handoff document) | 6 | 5 | **11** | Adopt as default | [~] |
| 64 | [incident-commander](#64-incident-commander) | Engineering | Comprehensive incident response framework from detection through resolution and post-incident review. | none | 6 | 5 | **11** | Optional pack: engineering | [~] |
| 65 | [monorepo-navigator](#65-monorepo-navigator) | Engineering | Navigate, manage, and optimize monorepos. | project-detect (partial) | 6 | 5 | **11** | Adapt into project-detect | [x] |
| 66 | [sql-database-assistant](#66-sql-database-assistant) | Engineering | Use when the user asks to write SQL queries, optimize database performance, generate migrations, explore... | none | 6 | 5 | **11** | Optional pack: engineering | [~] |
| 67 | [agent-designer](#67-agent-designer) | Engineering | Use when the user asks to design a multi-agent system, pick an orchestration pattern... | none | 7 | 4 | **11** | Optional pack: engineering | [~] |
| 68 | [agent-harness](#68-agent-harness) | Engineering | Turn any domain folder of skills into a bounded agentic loop: compile a goal into a verifiable task plan,... | plan mode, /loop, task plans | 7 | 4 | **11** | Optional pack: engineering | [~] |
| 69 | [agent-workflow-designer](#69-agent-workflow-designer) | Engineering | Design production-grade multi-agent workflows with clear pattern choice (sequential, parallel,... | none | 7 | 4 | **11** | Optional pack: engineering | [~] |
| 70 | [aws-solution-architect](#70-aws-solution-architect) | Engineering | Design AWS architectures for startups using serverless patterns and IaC templates. | aws-*, azure-*, gcp-* (operations only) | 7 | 4 | **11** | Optional pack: engineering | [~] |
| 71 | [chaos-engineering](#71-chaos-engineering) | Engineering | Use when planning, running, or learning from chaos engineering experiments. | none | 7 | 4 | **11** | Optional pack: engineering | [~] |
| 72 | [env-secrets-manager](#72-env-secrets-manager) | Engineering | Manage environment-variable hygiene and secrets safety across local development and production. | git-secret-scan, cloud secret listing | 7 | 4 | **11** | Optional pack: engineering | [~] |
| 73 | [feature-flags-architect](#73-feature-flags-architect) | Engineering | Use when adding, retiring, or auditing feature flags. | none | 7 | 4 | **11** | Optional pack: engineering | [~] |
| 74 | [gdpr-dsgvo-expert](#74-gdpr-dsgvo-expert) | Regulatory and quality | GDPR and German DSGVO compliance automation. | none | 7 | 4 | **11** | Optional pack: compliance | [~] |
| 75 | [grill-me](#75-grill-me) | Engineering | Interview the user relentlessly about a plan or design until reaching shared understanding, resolving each... | plan mode, ask_user | 7 | 4 | **11** | Optional pack: engineering | [~] |
| 76 | [grill-with-docs](#76-grill-with-docs) | Engineering | Docs-anchored grilling session. | plan mode, ask_user | 7 | 4 | **11** | Optional pack: engineering | [~] |
| 77 | [incident-response](#77-incident-response) | Engineering | Use when a security incident has been detected or declared and needs classification, triage, escalation... | none | 7 | 4 | **11** | Optional pack: security | [~] |
| 78 | [jira-expert](#78-jira-expert) | Project management | Atlassian Jira expert for creating and managing projects, planning, product discovery, JQL queries,... | none | 7 | 4 | **11** | Optional pack: product | [~] |
| 79 | [kubernetes-operator](#79-kubernetes-operator) | Engineering | Use when building a Kubernetes Operator. | k8s-* (operations only) | 7 | 4 | **11** | Optional pack: engineering | [~] |
| 80 | [md-review](#80-md-review) | Documents | Converts a markdown PR writeup or code review (one with ```diff fenced blocks and severity-tagged >... | code-review output (Markdown only) | 7 | 4 | **11** | Optional pack: docs | [~] |
| 81 | [observability-designer](#81-observability-designer) | Engineering | Design production-ready observability strategies combining metrics, logs, and traces. | none | 7 | 4 | **11** | Optional pack: engineering | [~] |
| 82 | [rag-architect](#82-rag-architect) | Engineering | Use when the user asks to design a RAG pipeline, choose a chunking strategy or embedding model, pick a... | none | 7 | 4 | **11** | Optional pack: engineering | [~] |
| 83 | [red-team](#83-red-team) | Engineering | Use when planning or executing authorized red team engagements, attack path analysis, or offensive... | none | 7 | 4 | **11** | Optional pack: security | [~] |
| 84 | [reflect](#84-reflect) | Productivity | Mid-conversation reflection skill that pauses execution and zooms out from detail-mode to honestly... | none | 7 | 4 | **11** | Optional pack: productivity | [~] |
| 85 | [security-pen-testing](#85-security-pen-testing) | Engineering | Use when the user asks to perform security audits, penetration testing, vulnerability scanning, OWASP Top... | none | 7 | 4 | **11** | Optional pack: security | [~] |
| 86 | [senior-architect](#86-senior-architect) | Engineering | This skill should be used when the user asks to "design system architecture", "evaluate microservices vs... | none | 7 | 4 | **11** | Optional pack: engineering | [~] |
| 87 | [senior-devops](#87-senior-devops) | Engineering | Comprehensive DevOps skill for CI/CD, infrastructure automation, containerization, and cloud platforms... | none | 7 | 4 | **11** | Optional pack: engineering | [~] |
| 88 | [senior-qa](#88-senior-qa) | Engineering | Generates unit tests, integration tests, and E2E tests for React/Next.js applications. | none | 7 | 4 | **11** | Optional pack: engineering | [~] |
| 89 | [slo-architect](#89-slo-architect) | Engineering | Use when defining, reviewing, or operating SLOs/SLIs/error budgets. | none | 7 | 4 | **11** | Optional pack: engineering | [~] |
| 90 | [spec-to-repo](#90-spec-to-repo) | Product | Use when the user says 'build me an app', 'create a project from this spec', 'scaffold a new repo',... | none | 7 | 4 | **11** | Optional pack: product | [~] |
| 91 | [threat-detection](#91-threat-detection) | Engineering | Use when hunting for threats in an environment, analyzing IOCs, or detecting behavioral anomalies in... | none | 7 | 4 | **11** | Optional pack: security | [~] |
| 92 | [browserstack](#92-browserstack) | Engineering | Run tests on BrowserStack. | none | 8 | 3 | **11** | Optional pack: engineering | [x] |
| 93 | [named-persona-adversarial-review](#93-named-persona-adversarial-review) | Engineering | Code review through the lens of real engineers' documented philosophies (Torvalds, Thompson, Carmack, Kent... | code-review (partial) | 8 | 3 | **11** | Optional pack: engineering | [x] |
| 94 | [testrail](#94-testrail) | Engineering | Sync tests with TestRail. Use when user mentions "testrail", "test management", "test cases", "test run",... | none | 8 | 3 | **11** | Optional pack: engineering | [x] |
| 95 | [board-deck-builder](#95-board-deck-builder) | Executive advisory | Assembles comprehensive board and investor update decks by pulling perspectives from all C-suite roles. | none | 9 | 2 | **11** | Optional pack: business | [x] |
| 96 | [board-prep](#96-board-prep) | Executive advisory | Board meeting preparation for the adversarial scenario, not the friendly one. | none | 9 | 2 | **11** | Optional pack: business | [x] |
| 97 | [business-investment-advisor](#97-business-investment-advisor) | Finance | Business investment analysis and capital allocation advisor. | none | 9 | 2 | **11** | Optional pack: business | [x] |
| 98 | [business-name-fit](#98-business-name-fit) | Marketing | Suggest, pick, or vet a business, startup, or product name that stays true to the founder's cultural... | none | 9 | 2 | **11** | Optional pack: marketing | [x] |
| 99 | [caio-review](#99-caio-review) | Executive advisory | Eval-demanding Chief AI Officer interrogation of any plan that involves AI: model selection, risk... | none | 9 | 2 | **11** | Optional pack: business | [x] |
| 100 | [cco-review](#100-cco-review) | Executive advisory | Retention-obsessed Chief Customer Officer interrogation of any plan that touches customer retention,... | none | 9 | 2 | **11** | Optional pack: business | [x] |
| 101 | [cdo-review](#101-cdo-review) | Executive advisory | Decision-driven Chief Data Officer interrogation of any plan that touches training data, data... | none | 9 | 2 | **11** | Optional pack: business | [x] |
| 102 | [cfo-review](#102-cfo-review) | Executive advisory | Numerate-skeptic interrogation of any plan that touches money. | none | 9 | 2 | **11** | Optional pack: business | [x] |
| 103 | [challenge](#103-challenge) | Executive advisory | Pre-mortem plan analysis. Imagine the plan failed 12 months from now and work backwards to find the... | none | 9 | 2 | **11** | Optional pack: business | [x] |
| 104 | [change-management](#104-change-management) | Executive advisory | Framework for rolling out organizational changes without chaos. | none | 9 | 2 | **11** | Optional pack: business | [x] |
| 105 | [ciso-review](#105-ciso-review) | Executive advisory | Risk-paranoid interrogation of any plan that touches data, compliance, or production access. | none | 9 | 2 | **11** | Optional pack: business | [x] |
| 106 | [cmo-review](#106-cmo-review) | Executive advisory | Narrative-first interrogation of positioning, ICP, message house, and channel mix. | none | 9 | 2 | **11** | Optional pack: business | [x] |
| 107 | [company-os](#107-company-os) | Executive advisory | The meta-framework for how a company runs. | none | 9 | 2 | **11** | Optional pack: business | [x] |
| 108 | [competitive-intel](#108-competitive-intel) | Executive advisory | Systematic competitor tracking that feeds CMO positioning, CRO battlecards, and CPO roadmap decisions. | none | 9 | 2 | **11** | Optional pack: business | [x] |
| 109 | [cpo-review](#109-cpo-review) | Executive advisory | JTBD-driven interrogation of product roadmap, PMF signal, and portfolio focus. | none | 9 | 2 | **11** | Optional pack: business | [x] |
| 110 | [cro-review](#110-cro-review) | Executive advisory | Pipeline-paranoid interrogation of revenue, win rate, NRR, and ramp time. | none | 9 | 2 | **11** | Optional pack: business | [x] |
| 111 | [culture-architect](#111-culture-architect) | Executive advisory | Build, measure, and evolve company culture as operational behavior. | none | 9 | 2 | **11** | Optional pack: business | [x] |
| 112 | [founder-coach](#112-founder-coach) | Executive advisory | Personal leadership development for founders and first-time CEOs. | none | 9 | 2 | **11** | Optional pack: business | [x] |
| 113 | [gc-review](#113-gc-review) | Executive advisory | General Counsel interrogation of contracts, IP, regulatory, term sheets, and employment-law surface. | none | 9 | 2 | **11** | Optional pack: business | [x] |
| 114 | [hard-call](#114-hard-call) | Executive advisory | Framework for decisions with no good options. | none | 9 | 2 | **11** | Optional pack: business | [x] |
| 115 | [internal-narrative](#115-internal-narrative) | Executive advisory | Build and maintain one coherent company story across all audiences. | none | 9 | 2 | **11** | Optional pack: business | [x] |
| 116 | [intl-expansion](#116-intl-expansion) | Executive advisory | International market expansion strategy. | none | 9 | 2 | **11** | Optional pack: business | [x] |
| 117 | [ma-playbook](#117-ma-playbook) | Executive advisory | M&A strategy for acquiring companies or being acquired. | none | 9 | 2 | **11** | Optional pack: business | [x] |
| 118 | [marketing-strategy-pmm](#118-marketing-strategy-pmm) | Marketing | Product marketing skill for positioning, GTM strategy, competitive intelligence, and product launches. | none | 9 | 2 | **11** | Optional pack: marketing | [x] |
| 119 | [meeting-analyzer](#119-meeting-analyzer) | Project management | Analyzes meeting transcripts and recordings to surface behavioral patterns, communication anti-patterns,... | none | 9 | 2 | **11** | Optional pack: product | [x] |
| 120 | [office-hours](#120-office-hours) | Executive advisory | YC-style 6-question founder interrogation before any advice. | none | 9 | 2 | **11** | Optional pack: business | [x] |
| 121 | [postmortem](#121-postmortem) | Executive advisory | Honest analysis of what went wrong. | none | 9 | 2 | **11** | Optional pack: business | [x] |
| 122 | [stress-test](#122-stress-test) | Executive advisory | Business assumption stress testing. | none | 9 | 2 | **11** | Optional pack: business | [x] |
| 123 | [vpe-review](#123-vpe-review) | Executive advisory | Throughput-first VP of Engineering interrogation of any plan that touches delivery, eng hiring, team... | none | 9 | 2 | **11** | Optional pack: business | [x] |
| 124 | [youtube-full](#124-youtube-full) | Marketing | Use when the user needs YouTube transcripts, video search, channel browsing, playlist extraction, or... | none | 9 | 2 | **11** | Optional pack: marketing | [x] |
| 125 | [browser-automation](#125-browser-automation) | Engineering | Use when the user asks to automate browser tasks, scrape websites, fill forms, capture screenshots,... | web_retrieve (Playwright-backed, read-only) | 5 | 5 | **10** | Optional pack: engineering | [~] |
| 126 | [tech-debt-tracker](#126-tech-debt-tracker) | Engineering | Scan codebases for technical debt, score severity, track trends, and generate prioritized remediation plans. | todo-scan, dead-code-scan | 5 | 5 | **10** | Adapt into todo-scan | [x] |
| 127 | [terraform-patterns](#127-terraform-patterns) | Engineering | Terraform infrastructure-as-code agent skill and plugin for Claude Code, Codex, Gemini CLI, Cursor, OpenClaw. | terraform | 5 | 5 | **10** | Adapt into terraform | [x] |
| 128 | [autoresearch-agent](#128-autoresearch-agent) | Engineering | Autonomous experiment loop that optimizes any file by a measurable metric. | /loop, loop-until, fix-until-green | 6 | 4 | **10** | Optional pack: engineering | [~] |
| 129 | [azure-cloud-architect](#129-azure-cloud-architect) | Engineering | Design Azure architectures for startups and enterprises. | aws-*, azure-*, gcp-* (operations only) | 6 | 4 | **10** | Optional pack: engineering | [~] |
| 130 | [code-to-prd](#130-code-to-prd) | Product | Reverse-engineer any codebase into a complete Product Requirements Document (PRD). | none | 6 | 4 | **10** | Optional pack: product | [~] |
| 131 | [docker-development](#131-docker-development) | Engineering | Docker and container development agent skill and plugin for Dockerfile optimization, docker-compose... | docker-build, dockerfile-lint, compose | 6 | 4 | **10** | Adapt into dockerfile-lint | [x] |
| 132 | [gcp-cloud-architect](#132-gcp-cloud-architect) | Engineering | Design GCP architectures for startups and enterprises. | aws-*, azure-*, gcp-* (operations only) | 6 | 4 | **10** | Optional pack: engineering | [~] |
| 133 | [karpathy-coder](#133-karpathy-coder) | Engineering | Use when writing, reviewing, or committing code to enforce Karpathy's 4 coding principles. | none | 6 | 4 | **10** | Optional pack: engineering | [~] |
| 134 | [loop](#134-loop) | Engineering | Start an autonomous experiment loop with user-selected interval (10min, 1h, daily, weekly, monthly). | /loop, loop-until, fix-until-green | 6 | 4 | **10** | Optional pack: engineering | [x] |
| 135 | [migration-architect](#135-migration-architect) | Engineering | Zero-downtime migration planning, compatibility validation, and rollback strategy generation. | none | 6 | 4 | **10** | Optional pack: engineering | [~] |
| 136 | [secrets-vault-manager](#136-secrets-vault-manager) | Engineering | Use when the user asks to set up secret management infrastructure, integrate HashiCorp Vault, configure... | git-secret-scan, cloud secret listing | 6 | 4 | **10** | Optional pack: engineering | [~] |
| 137 | [senior-backend](#137-senior-backend) | Engineering | Designs and implements backend systems including REST APIs, microservices, database architectures,... | none | 6 | 4 | **10** | Optional pack: engineering | [~] |
| 138 | [senior-frontend](#138-senior-frontend) | Engineering | Frontend development skill for React, Next.js, TypeScript, and Tailwind CSS applications. | react-* family | 6 | 4 | **10** | Optional pack: engineering | [~] |
| 139 | [senior-secops](#139-senior-secops) | Engineering | Senior SecOps engineer skill for application security, vulnerability management, compliance verification,... | none | 6 | 4 | **10** | Optional pack: engineering | [~] |
| 140 | [agent-decision-receipts](#140-agent-decision-receipts) | Regulatory and quality | Mint a tamper-evident, post-quantum-signed receipt for a consequential agent action (deploy, delete, pay,... | none | 7 | 3 | **10** | Optional pack: compliance | [~] |
| 141 | [apple-hig-expert](#141-apple-hig-expert) | Product | Audits and designs iOS/macOS/watchOS/visionOS interfaces against the Apple Human Interface Guidelines,... | none | 7 | 3 | **10** | Optional pack: product | [~] |
| 142 | [competitive-teardown](#142-competitive-teardown) | Product | Analyzes competitor products and companies by synthesizing data from pricing pages, app store reviews, job... | none | 7 | 3 | **10** | Optional pack: product | [~] |
| 143 | [confluence-expert](#143-confluence-expert) | Project management | Atlassian Confluence expert for creating and managing spaces, knowledge bases, and documentation. | none | 7 | 3 | **10** | Optional pack: product | [~] |
| 144 | [cto-advisor](#144-cto-advisor) | Executive advisory | Technical leadership guidance for engineering teams, architecture decisions, and technology strategy. | none | 7 | 3 | **10** | Optional pack: business | [~] |
| 145 | [data-quality-auditor](#145-data-quality-auditor) | Engineering | Audit datasets for completeness, consistency, accuracy, and validity. | none | 7 | 3 | **10** | Optional pack: data | [~] |
| 146 | [dossier](#146-dossier) | Research | Decision-grade entity research skill. | none | 7 | 3 | **10** | Optional pack: research | [~] |
| 147 | [epic-design](#147-epic-design) | Engineering | Build immersive, cinematic 2.5D interactive websites using scroll storytelling, parallax depth, text... | none | 7 | 3 | **10** | Optional pack: engineering | [~] |
| 148 | [experiment-designer](#148-experiment-designer) | Product | Use when planning product experiments, writing testable hypotheses, estimating sample size, prioritizing... | none | 7 | 3 | **10** | Optional pack: product | [~] |
| 149 | [knowledge-ops](#149-knowledge-ops) | Business operations | Use when a Head of Ops, Knowledge Manager, or TPM-Internal needs to author, validate, or clean up company... | none | 7 | 3 | **10** | Optional pack: business | [~] |
| 150 | [landing-page-generator](#150-landing-page-generator) | Product | Generates high-converting landing pages as complete Next.js/React (TSX) components with Tailwind CSS. | none | 7 | 3 | **10** | Optional pack: product | [~] |
| 151 | [litreview](#151-litreview) | Research | Academic literature orientation skill that searches papers via free keyless APIs (PubMed E-utilities +... | none | 7 | 3 | **10** | Optional pack: research | [~] |
| 152 | [md-document](#152-md-document) | Documents | Converts long-form markdown (specs, RFCs, reports, plans, explainers) into a single-file,... | none | 7 | 3 | **10** | Optional pack: docs | [~] |
| 153 | [product-analytics](#153-product-analytics) | Product | Use when defining product KPIs, building metric dashboards, running cohort or retention analysis, or... | none | 7 | 3 | **10** | Optional pack: product | [~] |
| 154 | [product-discovery](#154-product-discovery) | Product | Use when validating product opportunities, mapping assumptions, planning discovery sprints, or testing... | none | 7 | 3 | **10** | Optional pack: product | [~] |
| 155 | [product-manager-toolkit](#155-product-manager-toolkit) | Product | Comprehensive toolkit for product managers including RICE prioritization, customer interview analysis, PRD... | none | 7 | 3 | **10** | Optional pack: product | [~] |
| 156 | [product-strategist](#156-product-strategist) | Product | Strategic product leadership toolkit for Head of Product covering OKR cascade generation, quarterly... | none | 7 | 3 | **10** | Optional pack: product | [~] |
| 157 | [pulse](#157-pulse) | Research | Multi-source recency research skill that takes the pulse of any topic across Reddit, Hacker News, the open... | none | 7 | 3 | **10** | Optional pack: research | [~] |
| 158 | [rfp-responder](#158-rfp-responder) | Commercial | Use when an RFP, RFI, RFQ, security questionnaire, vendor questionnaire, or proposal request arrives and... | none | 7 | 3 | **10** | Optional pack: business | [~] |
| 159 | [roadmap-communicator](#159-roadmap-communicator) | Product | Use when preparing roadmap narratives, release notes, changelogs, or stakeholder updates tailored for... | release-notes (partial) | 7 | 3 | **10** | Optional pack: product | [~] |
| 160 | [saas-scaffolder](#160-saas-scaffolder) | Product | Generates complete, production-ready SaaS project boilerplate including authentication, database schemas,... | none | 7 | 3 | **10** | Optional pack: product | [~] |
| 161 | [scrum-master](#161-scrum-master) | Project management | Advanced Scrum Master skill for data-driven agile team analysis and coaching. | none | 7 | 3 | **10** | Optional pack: product | [~] |
| 162 | [senior-data-scientist](#162-senior-data-scientist) | Engineering | World-class senior data scientist skill specialising in statistical modeling, experiment design, causal... | none | 7 | 3 | **10** | Optional pack: data | [~] |
| 163 | [senior-ml-engineer](#163-senior-ml-engineer) | Engineering | ML engineering skill for productionizing models, building MLOps pipelines, and integrating LLMs. | none | 7 | 3 | **10** | Optional pack: data | [~] |
| 164 | [senior-prompt-engineer](#164-senior-prompt-engineer) | Engineering | Use when the user asks to optimize prompts, design prompt templates, evaluate LLM outputs with an eval... | none | 7 | 3 | **10** | Optional pack: data | [~] |
| 165 | [snowflake-development](#165-snowflake-development) | Engineering | Use when writing Snowflake SQL, building data pipelines with Dynamic Tables or Streams/Tasks, using Cortex... | none | 7 | 3 | **10** | Optional pack: engineering | [~] |
| 166 | [statistical-analyst](#166-statistical-analyst) | Engineering | Run hypothesis tests, analyze A/B experiment results, calculate sample sizes, and interpret statistical... | none | 7 | 3 | **10** | Optional pack: data | [~] |
| 167 | [ui-design-system](#167-ui-design-system) | Product | UI design system toolkit for Senior UI Designer including design token generation, component... | none | 7 | 3 | **10** | Optional pack: product | [~] |
| 168 | [universal-scraping-architect](#168-universal-scraping-architect) | Engineering | Use for web scraping, crawling, document extraction, API parsing, or building validation-heavy data... | none | 7 | 3 | **10** | Optional pack: data | [~] |
| 169 | [ai-act-readiness](#169-ai-act-readiness) | Compliance | EU AI Act 6-question forcing interrogation. | none | 8 | 2 | **10** | Optional pack: compliance | [x] |
| 170 | [aims-audit](#170-aims-audit) | Compliance | ISO/IEC 42001 AIMS internal-audit 6-question forcing interrogation. | none | 8 | 2 | **10** | Optional pack: compliance | [x] |
| 171 | [board-meeting](#171-board-meeting) | Executive advisory | Multi-agent board meeting protocol for strategic decisions. | none | 8 | 2 | **10** | Optional pack: business | [x] |
| 172 | [boardroom](#172-boardroom) | Executive advisory | 6-phase multi-role deliberation across the C-suite with Phase 2 isolation, critic pre-screen, and synthesis. | none | 8 | 2 | **10** | Optional pack: business | [x] |
| 173 | [brand-guidelines](#173-brand-guidelines) | Marketing | When the user wants to apply, document, or enforce brand guidelines for any product or company. | none | 8 | 2 | **10** | Optional pack: marketing | [x] |
| 174 | [brief](#174-brief) | Executive advisory | Generate a one-page strategy brief from an office-hours intake. | none | 8 | 2 | **10** | Optional pack: business | [x] |
| 175 | [chief-of-staff](#175-chief-of-staff) | Executive advisory | C-suite orchestration layer. | none | 8 | 2 | **10** | Optional pack: business | [x] |
| 176 | [compliance-readiness](#176-compliance-readiness) | Compliance | Multi-framework compliance officer 6-question forcing interrogation of any compliance program. | none | 8 | 2 | **10** | Optional pack: compliance | [x] |
| 177 | [context-engine](#177-context-engine) | Executive advisory | Loads and manages company context for all C-suite advisor skills. | none | 8 | 2 | **10** | Optional pack: business | [x] |
| 178 | [contract-and-proposal-writer](#178-contract-and-proposal-writer) | Business growth | Generate professional, jurisdiction-aware business documents: freelance contracts, project proposals,... | none | 8 | 2 | **10** | Optional pack: business | [x] |
| 179 | [cross-eval](#179-cross-eval) | Executive advisory | Multi-model consensus on a board memo or strategy brief. | none | 8 | 2 | **10** | Optional pack: business | [x] |
| 180 | [cs-onboard](#180-cs-onboard) | Executive advisory | Founder onboarding interview that captures company context across 7 dimensions. | none | 8 | 2 | **10** | Optional pack: business | [x] |
| 181 | [decide](#181-decide) | Executive advisory | Log a decision to two-layer memory via decision-logger. | none | 8 | 2 | **10** | Optional pack: business | [x] |
| 182 | [execute](#182-execute) | Executive advisory | Generate a 90-day execution plan with weekly milestones, DRIs, and check-in cadence from an approved decision. | none | 8 | 2 | **10** | Optional pack: business | [x] |
| 183 | [fda-qsr-audit-prep](#183-fda-qsr-audit-prep) | Compliance | FDA 21 CFR 820 (QSR / QMSR) audit 6-question forcing interrogation. | none | 8 | 2 | **10** | Optional pack: compliance | [x] |
| 184 | [founder-mode](#184-founder-mode) | Executive advisory | Auto-routes any founder question to the right C-role advisor or to /cs:boardroom for multi-role topics. | none | 8 | 2 | **10** | Optional pack: business | [x] |
| 185 | [freeze](#185-freeze) | Executive advisory | Lock a strategic decision for a cooldown period to prevent impulse reversal. | none | 8 | 2 | **10** | Optional pack: business | [x] |
| 186 | [gdpr-audit-prep](#186-gdpr-audit-prep) | Compliance | GDPR audit 6-question Article-cited forcing interrogation. | none | 8 | 2 | **10** | Optional pack: compliance | [x] |
| 187 | [iso13485-audit-prep](#187-iso13485-audit-prep) | Compliance | ISO 13485 QMS audit 6-question forcing interrogation. | none | 8 | 2 | **10** | Optional pack: compliance | [x] |
| 188 | [iso27001-audit-prep](#188-iso27001-audit-prep) | Compliance | ISO 27001 ISMS audit readiness 6-question forcing interrogation. | none | 8 | 2 | **10** | Optional pack: compliance | [x] |
| 189 | [marketing-ideas](#189-marketing-ideas) | Marketing | When the user needs marketing ideas, inspiration, or strategies for their SaaS or software product. | none | 8 | 2 | **10** | Optional pack: marketing | [x] |
| 190 | [marketing-psychology](#190-marketing-psychology) | Marketing | When the user wants to apply psychological principles, mental models, or behavioral science to marketing. | none | 8 | 2 | **10** | Optional pack: marketing | [x] |
| 191 | [onboard](#191-onboard) | Executive advisory | Founder interview that populates ~/.claude/company-context.md using the canonical 7-dimension cs-onboard... | none | 8 | 2 | **10** | Optional pack: business | [x] |
| 192 | [paywall-upgrade-cro](#192-paywall-upgrade-cro) | Marketing | When the user wants to create or optimize in-app paywalls, upgrade screens, upsell modals, or feature gates. | none | 8 | 2 | **10** | Optional pack: marketing | [x] |
| 193 | [popup-cro](#193-popup-cro) | Marketing | When the user wants to create or optimize popups, modals, overlays, slide-ins, or banners for conversion... | none | 8 | 2 | **10** | Optional pack: marketing | [x] |
| 194 | [post-mortem](#194-post-mortem) | Executive advisory | Honest retrospective on an executed decision, scored against original assumptions and dissent. | none | 8 | 2 | **10** | Optional pack: business | [x] |
| 195 | [soc2-audit-prep](#195-soc2-audit-prep) | Compliance | SOC 2 Type II readiness 6-question forcing interrogation. | none | 8 | 2 | **10** | Optional pack: compliance | [x] |
| 196 | [social-content](#196-social-content) | Marketing | When the user wants help creating, scheduling, or optimizing social media content for LinkedIn, Twitter/X,... | none | 8 | 2 | **10** | Optional pack: marketing | [x] |
| 197 | [video-content-strategist](#197-video-content-strategist) | Marketing | Use when planning video content strategy, writing video scripts, optimizing YouTube channels, building... | none | 8 | 2 | **10** | Optional pack: marketing | [x] |

## Recommended order of work

Start with Phase 0. Every script-bearing skill depends on it, and the importer turns the rest of this plan from 197 hand edits into a review job.

Then ship the defaults in two batches. The Playwright family and `a11y-audit` go first because web projects are the most common thing people open mux in and nothing in mux covers browser tests today. The second batch is the workflow and safety group: `skill-security-auditor` and `security-guidance` (both lean on features mux just shipped, the project-skill trust gate and tool hooks), `extract`, `handoff`, `tdd-guide`, `ci-cd-pipeline-builder`, `performance-profiler`, `api-design-reviewer`, and `ship-gate`.

The **Adapt** items come next, one pull request per target skill: `code-review` (adversarial mode and the review heuristics), `security-review` (STRIDE scoring and license conflicts), `debug`, `release-notes`, `explain-codebase`, `project-detect`, `todo-scan`, `new-skill`, and the infrastructure skills (`terraform`, `dockerfile-lint`). Each is small, and each improves a skill people already use.

Packs last, in this order: engineering, security, data, docs, research, product, productivity, then marketing, compliance, and business. A pack can ship as soon as its own skills validate; there is no reason to wait for all of them.

## How to annotate

Each skill's section has a task list and a **Notes:** line. Tick tasks as they land, change the table's Status cell to `[x]` when a skill is done (or `[-]` when it is dropped for good), and write anything surprising on the Notes line: a script that needed rewriting, a better gating glob, a reason to change the recommendation. If a recommendation changes, update the table row and the section together so they never disagree. Keep the status line at the top current.

## Skills

### 1. coverage

- **Source:** `engineering-team/playwright-pro/skills/coverage/SKILL.md`
- **Category:** Engineering. **Simplicity** 9, **Value** 6, **Total** 15. **Recommendation:** Adopt as default.
- **What it does:** Analyze test coverage gaps. Use when user says "test coverage", "what's not tested", "coverage gaps", "missing tests", "coverage report", or "what needs testing".
- **Overlap with mux:** none (no Playwright test skills).
- **Fit in mux:** Ship with mux as a playbook (instructions only), listed only where it applies (`playwright.config.*`, `package.json`).
- **Integration cost:** a single SKILL.md with no bundled scripts.
- **Assessment:** Playwright test setup, generation, flaky-test fixing, and migration are everyday web work; gate on Playwright config or package.json.

- [x] Port `engineering-team/playwright-pro/skills/coverage/SKILL.md` into a default skill definition (a new `DefaultImportedSkills.cs`, or the family file named under Overlap), rewriting Claude Code references for mux and removing em-dashes.
- [x] Ship it as a playbook (no commands) and keep the procedure numbered, with an explicit output format.
- [x] Gate the listing with `appliesTo`: `playwright.config.*`, `package.json`.
- [x] Add positive and negative cases to a new `ImportedSkillsSuite` (loads, validates, gating, body substitution, and each command against a fixture project).
- [x] Record the source and the MIT notice in `THIRD_PARTY_NOTICES.md`; add the skill to USAGE.md and the CHANGELOG. The notice is in THIRD_PARTY_NOTICES.md; the USAGE.md and CHANGELOG entries come with the release documentation pass.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Bundled/pw-coverage` with category `testing`. Shipped under the id `pw-coverage` (the original name `coverage` was too generic or shared with another skill). Shipped as a bundled folder (SKILL.md plus its files) rather than a C# definition, so the source text and scripts travel unchanged apart from normalization.

### 2. fix

- **Source:** `engineering-team/playwright-pro/skills/fix/SKILL.md`
- **Category:** Engineering. **Simplicity** 9, **Value** 6, **Total** 15. **Recommendation:** Adopt as default.
- **What it does:** Fix failing or flaky Playwright tests. Use when user says "fix test", "flaky test", "test failing", "debug test", "test broken", "test passes sometimes", or "intermittent failure".
- **Overlap with mux:** flaky-test-hunt (detection only).
- **Fit in mux:** Ship with mux as a playbook (instructions only), listed only where it applies (`playwright.config.*`, `package.json`).
- **Integration cost:** a single SKILL.md with no bundled scripts.
- **Assessment:** Playwright test setup, generation, flaky-test fixing, and migration are everyday web work; gate on Playwright config or package.json.

- [x] Port `engineering-team/playwright-pro/skills/fix/SKILL.md` into a default skill definition (a new `DefaultImportedSkills.cs`, or the family file named under Overlap), rewriting Claude Code references for mux and removing em-dashes.
- [x] Ship it as a playbook (no commands) and keep the procedure numbered, with an explicit output format.
- [x] Gate the listing with `appliesTo`: `playwright.config.*`, `package.json`.
- [x] Add positive and negative cases to a new `ImportedSkillsSuite` (loads, validates, gating, body substitution, and each command against a fixture project).
- [x] Record the source and the MIT notice in `THIRD_PARTY_NOTICES.md`; add the skill to USAGE.md and the CHANGELOG. The notice is in THIRD_PARTY_NOTICES.md; the USAGE.md and CHANGELOG entries come with the release documentation pass.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Bundled/pw-fix` with category `testing`. Shipped under the id `pw-fix` (the original name `fix` was too generic or shared with another skill). Shipped as a bundled folder (SKILL.md plus its files) rather than a C# definition, so the source text and scripts travel unchanged apart from normalization.

### 3. generate

- **Source:** `engineering-team/playwright-pro/skills/generate/SKILL.md`
- **Category:** Engineering. **Simplicity** 9, **Value** 6, **Total** 15. **Recommendation:** Adopt as default.
- **What it does:** Generate Playwright tests. Use when user says "write tests", "generate tests", "add tests for", "test this component", "e2e test", "create test for", "test this page", or "test this feature".
- **Overlap with mux:** none (no Playwright test skills).
- **Fit in mux:** Ship with mux as a playbook (instructions only), listed only where it applies (`playwright.config.*`, `package.json`).
- **Integration cost:** a single SKILL.md with no bundled scripts.
- **Assessment:** Playwright test setup, generation, flaky-test fixing, and migration are everyday web work; gate on Playwright config or package.json.

- [x] Port `engineering-team/playwright-pro/skills/generate/SKILL.md` into a default skill definition (a new `DefaultImportedSkills.cs`, or the family file named under Overlap), rewriting Claude Code references for mux and removing em-dashes.
- [x] Ship it as a playbook (no commands) and keep the procedure numbered, with an explicit output format.
- [x] Gate the listing with `appliesTo`: `playwright.config.*`, `package.json`.
- [x] Add positive and negative cases to a new `ImportedSkillsSuite` (loads, validates, gating, body substitution, and each command against a fixture project).
- [x] Record the source and the MIT notice in `THIRD_PARTY_NOTICES.md`; add the skill to USAGE.md and the CHANGELOG. The notice is in THIRD_PARTY_NOTICES.md; the USAGE.md and CHANGELOG entries come with the release documentation pass.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Bundled/pw-generate` with category `testing`. Shipped under the id `pw-generate` (the original name `generate` was too generic or shared with another skill). Shipped as a bundled folder (SKILL.md plus its files) rather than a C# definition, so the source text and scripts travel unchanged apart from normalization.

### 4. pw-init

- **Source:** `engineering-team/playwright-pro/skills/pw-init/SKILL.md`
- **Category:** Engineering. **Simplicity** 9, **Value** 6, **Total** 15. **Recommendation:** Adopt as default.
- **What it does:** Set up Playwright in a project. Use when user says "set up playwright", "add e2e tests", "configure playwright", "testing setup", "init playwright", or "add test infrastructure".
- **Overlap with mux:** none (no Playwright test skills).
- **Fit in mux:** Ship with mux as a playbook (instructions only), listed only where it applies (`package.json`).
- **Integration cost:** a single SKILL.md with no bundled scripts.
- **Assessment:** Playwright test setup, generation, flaky-test fixing, and migration are everyday web work; gate on Playwright config or package.json.

- [x] Port `engineering-team/playwright-pro/skills/pw-init/SKILL.md` into a default skill definition (a new `DefaultImportedSkills.cs`, or the family file named under Overlap), rewriting Claude Code references for mux and removing em-dashes.
- [x] Ship it as a playbook (no commands) and keep the procedure numbered, with an explicit output format.
- [x] Gate the listing with `appliesTo`: `package.json`.
- [x] Add positive and negative cases to a new `ImportedSkillsSuite` (loads, validates, gating, body substitution, and each command against a fixture project).
- [x] Record the source and the MIT notice in `THIRD_PARTY_NOTICES.md`; add the skill to USAGE.md and the CHANGELOG. The notice is in THIRD_PARTY_NOTICES.md; the USAGE.md and CHANGELOG entries come with the release documentation pass.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Bundled/pw-init` with category `testing`. Shipped as a bundled folder (SKILL.md plus its files) rather than a C# definition, so the source text and scripts travel unchanged apart from normalization.

### 5. a11y-audit

- **Source:** `engineering-team/a11y-audit/skills/a11y-audit/SKILL.md`
- **Category:** Engineering. **Simplicity** 7, **Value** 7, **Total** 14. **Recommendation:** Adopt as default.
- **What it does:** Accessibility audit skill for scanning, fixing, and verifying WCAG 2.2 Level A and AA compliance across React, Next.js, Vue, Angular, Svelte, and plain HTML codebases. Use when auditing accessibility, fixing a11y violations, checking color contrast, generating compliance reports, or integrating accessibility checks into CI/CD pipelines.
- **Overlap with mux:** none.
- **Fit in mux:** Ship with mux as a hybrid skill whose commands run the bundled scripts, listed only where it applies (`package.json`, `**/*.html`, `**/*.vue`, `**/*.svelte`).
- **Integration cost:** 2 bundled scripts, standard-library Python; bundled `references/`, `assets/`.
- **Assessment:** WCAG checks for React, Vue, Angular, Svelte, and HTML fill a clear gap next to the React family; gate on package.json or HTML.

- [x] Port `engineering-team/a11y-audit/skills/a11y-audit/SKILL.md` into a default skill definition (a new `DefaultImportedSkills.cs`, or the family file named under Overlap), rewriting Claude Code references for mux and removing em-dashes.
- [~] Wrap the 2 Python scripts as `python` commands with `requiresTools: [python3]`, mapping their exit codes to mux's 0/1/2 convention and adding `MUX_SKILL_DRY_RUN` support; or port the logic to the shared `mux-skill.ps1` helper if it is small.
- [x] Gate the listing with `appliesTo`: `package.json`, `**/*.html`, `**/*.vue`, `**/*.svelte`.
- [x] Add positive and negative cases to a new `ImportedSkillsSuite` (loads, validates, gating, body substitution, and each command against a fixture project).
- [x] Record the source and the MIT notice in `THIRD_PARTY_NOTICES.md`; add the skill to USAGE.md and the CHANGELOG. The notice is in THIRD_PARTY_NOTICES.md; the USAGE.md and CHANGELOG entries come with the release documentation pass.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Bundled/a11y-audit` with category `frontend`. Shipped as a bundled folder (SKILL.md plus its files) rather than a C# definition, so the source text and scripts travel unchanged apart from normalization. Partial: the bundled scripts are called from the body as `python3 "${SKILL_DIR}/scripts/..."` but were not executed during the import (the source is treated as untrusted), and `requiresTools: [python3]` is not declared because Windows installs usually name the interpreter `python`, which would hide the skill there. No `run_skill` command wrappers yet.

### 6. skill-security-auditor

- **Source:** `engineering/skills/skill-security-auditor/SKILL.md`
- **Category:** Engineering. **Simplicity** 7, **Value** 7, **Total** 14. **Recommendation:** Adopt as default.
- **What it does:** Security audit and vulnerability scanner for AI agent skills before installation. Use when: (1) evaluating a skill from an untrusted source, (2) auditing a skill directory or git repo URL for malicious code, (3) pre-install security gate for Claude Code plugins, OpenClaw skills, or Codex skills, (4) scanning Python scripts for dangerous patterns like os.system, eval, subprocess, network exfiltra
- **Overlap with mux:** project skill trust gate (partial).
- **Fit in mux:** Ship with mux as a hybrid skill whose commands run the bundled scripts, listed only where it applies (`.claude/skills/**`, `.mux/skills/**`, `.agents/skills/**`).
- **Integration cost:** 1 bundled script, standard-library Python; bundled `references/`.
- **Assessment:** mux loads untrusted project skills behind a trust prompt; a scanner that inspects a skill before trusting it is a direct fit.

- [x] Port `engineering/skills/skill-security-auditor/SKILL.md` into a default skill definition (a new `DefaultImportedSkills.cs`, or the family file named under Overlap), rewriting Claude Code references for mux and removing em-dashes.
- [~] Wrap the 1 Python script as `python` commands with `requiresTools: [python3]`, mapping their exit codes to mux's 0/1/2 convention and adding `MUX_SKILL_DRY_RUN` support; or port the logic to the shared `mux-skill.ps1` helper if it is small.
- [x] Gate the listing with `appliesTo`: `.claude/skills/**`, `.mux/skills/**`, `.agents/skills/**`.
- [x] Add positive and negative cases to a new `ImportedSkillsSuite` (loads, validates, gating, body substitution, and each command against a fixture project).
- [x] Record the source and the MIT notice in `THIRD_PARTY_NOTICES.md`; add the skill to USAGE.md and the CHANGELOG. The notice is in THIRD_PARTY_NOTICES.md; the USAGE.md and CHANGELOG entries come with the release documentation pass.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Bundled/skill-security-auditor` with category `security`. Shipped as a bundled folder (SKILL.md plus its files) rather than a C# definition, so the source text and scripts travel unchanged apart from normalization. Partial: the bundled scripts are called from the body as `python3 "${SKILL_DIR}/scripts/..."` but were not executed during the import (the source is treated as untrusted), and `requiresTools: [python3]` is not declared because Windows installs usually name the interpreter `python`, which would hide the skill there. No `run_skill` command wrappers yet.

### 7. adversarial-reviewer

- **Source:** `engineering-team/skills/adversarial-reviewer/SKILL.md`
- **Category:** Engineering. **Simplicity** 8, **Value** 6, **Total** 14. **Recommendation:** Adapt into code-review.
- **What it does:** Adversarial code review that breaks the self-review monoculture. Use when you want a genuinely critical review of recent changes, before merging a PR, or when you suspect Claude is being too agreeable about code quality. Forces perspective shifts through hostile reviewer personas that catch blind spots the author's mental model shares with the reviewer.
- **Overlap with mux:** code-review (partial).
- **Fit in mux:** Do not ship it on its own. Its useful parts go into `code-review`.
- **Integration cost:** references Claude Code files or conventions (claude.md) that must be rewritten for mux.
- **Assessment:** An adversarial pass is a real gap: add it as a `code-review adversarial` mode or effort word rather than a second skill.

- [x] Read `engineering-team/skills/adversarial-reviewer/SKILL.md` and list the procedures or checks that `code-review` does not already have.
- [x] Fold those into `code-review` (body, a new command, or an effort word), keeping mux's output format and exit codes.
- [x] Extend the tests that cover `code-review` with a case for each added check.
- [x] Note the borrowed ideas and the MIT source in `THIRD_PARTY_NOTICES.md` and the CHANGELOG. The notice is in THIRD_PARTY_NOTICES.md; the USAGE.md and CHANGELOG entries come with the release documentation pass.

**Notes:** Merged on 2026-10-09; see the target skill's body and, where noted in THIRD_PARTY_NOTICES.md, its `resources/` folder.

### 8. extract

- **Source:** `engineering-team/self-improving-agent/skills/extract/SKILL.md`
- **Category:** Engineering. **Simplicity** 8, **Value** 6, **Total** 14. **Recommendation:** Adopt as default.
- **What it does:** Turn a proven pattern or debugging solution into a standalone reusable skill with SKILL.md, reference docs, and examples. Use when the user runs /si:extract or asks to package a recurring solution from memory into a skill.
- **Overlap with mux:** new-skill (scaffold only).
- **Fit in mux:** Ship with mux as a playbook (instructions only), listed only where it applies (none (listed everywhere)).
- **Integration cost:** references Claude Code files or conventions (.claude/) that must be rewritten for mux.
- **Assessment:** Turning a solved problem into a reusable skill is a strong fit for a skill-first harness; rewrite against mux skill conventions.

- [x] Port `engineering-team/self-improving-agent/skills/extract/SKILL.md` into a default skill definition (a new `DefaultImportedSkills.cs`, or the family file named under Overlap), rewriting Claude Code references for mux and removing em-dashes.
- [x] Ship it as a playbook (no commands) and keep the procedure numbered, with an explicit output format.
- [x] Gate the listing with `appliesTo`: none (listed everywhere).
- [x] Add positive and negative cases to a new `ImportedSkillsSuite` (loads, validates, gating, body substitution, and each command against a fixture project).
- [x] Record the source and the MIT notice in `THIRD_PARTY_NOTICES.md`; add the skill to USAGE.md and the CHANGELOG. The notice is in THIRD_PARTY_NOTICES.md; the USAGE.md and CHANGELOG entries come with the release documentation pass.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Bundled/skill-extract` with category `workflow`. Shipped under the id `skill-extract` (the original name `extract` was too generic or shared with another skill). Shipped as a bundled folder (SKILL.md plus its files) rather than a C# definition, so the source text and scripts travel unchanged apart from normalization.

### 9. security-guidance

- **Source:** `engineering/security-guidance/skills/security-guidance/SKILL.md`
- **Category:** Engineering. **Simplicity** 8, **Value** 6, **Total** 14. **Recommendation:** Adopt as default.
- **What it does:** PreToolUse security-anti-pattern hook for Claude Code. Catches 12 common security risks (command injection, XSS, SQL injection, unsafe deserialization, GitHub Actions workflow injection, eval/new Function code injection) BEFORE the Edit/Write/MultiEdit operation completes. Session-state caching prevents duplicate warnings on the same file+rule combo. Stdlib only. no dependencies. Use when you wan
- **Overlap with mux:** tool hooks (mechanism only).
- **Fit in mux:** Ship with mux as a playbook (instructions only), listed only where it applies (none; it is a hook, not a listed skill).
- **Integration cost:** references Claude Code files or conventions (.claude/, pretooluse, ~/.claude) that must be rewritten for mux; bundled `references/`.
- **Assessment:** Ships as a PreToolUse hook; mux now has pre-tool-use hooks with the same contract, so this ports as a default hook plus a playbook.

- [x] Port `engineering/security-guidance/skills/security-guidance/SKILL.md` into a default skill definition (a new `DefaultImportedSkills.cs`, or the family file named under Overlap), rewriting Claude Code references for mux and removing em-dashes.
- [x] Ship it as a playbook (no commands) and keep the procedure numbered, with an explicit output format.
- [x] Gate the listing with `appliesTo`: none; it is a hook, not a listed skill.
- [x] Add positive and negative cases to a new `ImportedSkillsSuite` (loads, validates, gating, body substitution, and each command against a fixture project).
- [x] Record the source and the MIT notice in `THIRD_PARTY_NOTICES.md`; add the skill to USAGE.md and the CHANGELOG. The notice is in THIRD_PARTY_NOTICES.md; the USAGE.md and CHANGELOG entries come with the release documentation pass.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Bundled/security-guidance` with category `security`. Shipped as a bundled folder (SKILL.md plus its files) rather than a C# definition, so the source text and scripts travel unchanged apart from normalization.

### 10. api-test-suite-builder

- **Source:** `engineering/skills/api-test-suite-builder/SKILL.md`
- **Category:** Engineering. **Simplicity** 9, **Value** 5, **Total** 14. **Recommendation:** Optional pack: engineering.
- **What it does:** Use when the user asks to generate API tests, create integration test suites, test REST endpoints, or build contract tests.
- **Overlap with mux:** none.
- **Fit in mux:** Offer it in the opt-in `engineering` pack as a playbook (instructions only). Nothing is seeded until a user installs the pack.
- **Integration cost:** a single SKILL.md with no bundled scripts; bundled `references/`.
- **Assessment:** Playbook only; fine as an optional skill.

- [x] Import `engineering/skills/api-test-suite-builder/SKILL.md` into `packs/engineering/api-test-suite-builder/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/engineering/api-test-suite-builder` with category `testing`.

### 11. database-schema-designer

- **Source:** `engineering/skills/database-schema-designer/SKILL.md`
- **Category:** Engineering. **Simplicity** 9, **Value** 5, **Total** 14. **Recommendation:** Optional pack: engineering.
- **What it does:** Use when the user asks to create ERD diagrams, normalize database schemas, design table relationships, or plan schema migrations.
- **Overlap with mux:** none.
- **Fit in mux:** Offer it in the opt-in `engineering` pack as a playbook (instructions only). Nothing is seeded until a user installs the pack.
- **Integration cost:** a single SKILL.md with no bundled scripts; bundled `references/`.
- **Assessment:** Database work is common but varied; offer as a pack rather than a default.

- [x] Import `engineering/skills/database-schema-designer/SKILL.md` into `packs/engineering/database-schema-designer/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/engineering/database-schema-designer` with category `data`.

### 12. deep-research

- **Source:** `research/deep-research/skills/deep-research/SKILL.md`
- **Category:** Research. **Simplicity** 9, **Value** 5, **Total** 14. **Recommendation:** Optional pack: research.
- **What it does:** Run a disciplined, multi-source research investigation for a high-stakes question or decision. fan-out web search across many channels, parallel sub-agents, source triangulation (each claim backed by ≥3 independent sources), an adversarial review pass, and every source saved to its own file with verbatim quotes for reuse. Use when a low-quality answer is expensive: strategy work, comparing N prod
- **Overlap with mux:** web_search, web_retrieve tools.
- **Fit in mux:** Offer it in the opt-in `research` pack as a playbook (instructions only). Nothing is seeded until a user installs the pack.
- **Integration cost:** a single SKILL.md with no bundled scripts; bundled `references/`.
- **Assessment:** mux has web search and retrieval; a disciplined multi-source procedure is the missing piece.

- [x] Import `research/deep-research/skills/deep-research/SKILL.md` into `packs/research/deep-research/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/research/deep-research` with category `research`.

### 13. focused-fix

- **Source:** `engineering/skills/focused-fix/SKILL.md`
- **Category:** Engineering. **Simplicity** 9, **Value** 5, **Total** 14. **Recommendation:** Adapt into debug.
- **What it does:** Use when the user asks to fix, debug, or make a specific feature/module/area work end-to-end. Triggers: 'make X work', 'fix the Y feature', 'the Z module is broken', 'focus on [area]'. Not for quick single-bug fixes. this is for systematic deep-dive repair across all files and dependencies.
- **Overlap with mux:** debug, fix-until-green.
- **Fit in mux:** Do not ship it on its own. Its useful parts go into `debug`.
- **Integration cost:** a single SKILL.md with no bundled scripts.
- **Assessment:** Its scope-discipline rules strengthen the debug playbook.

- [x] Read `engineering/skills/focused-fix/SKILL.md` and list the procedures or checks that `debug` does not already have.
- [x] Fold those into `debug` (body, a new command, or an effort word), keeping mux's output format and exit codes.
- [x] Extend the tests that cover `debug` with a case for each added check.
- [x] Note the borrowed ideas and the MIT source in `THIRD_PARTY_NOTICES.md` and the CHANGELOG. The notice is in THIRD_PARTY_NOTICES.md; the USAGE.md and CHANGELOG entries come with the release documentation pass.

**Notes:** Merged on 2026-10-09; see the target skill's body and, where noted in THIRD_PARTY_NOTICES.md, its `resources/` folder.

### 14. full-page-screenshot

- **Source:** `engineering/skills/full-page-screenshot/SKILL.md`
- **Category:** Engineering. **Simplicity** 9, **Value** 5, **Total** 14. **Recommendation:** Optional pack: engineering.
- **What it does:** Use when the user asks to capture a full-page screenshot, long screenshot, or complete page capture of a web page. Handles SPA scroll containers, lazy-loaded images, and very tall pages via Chrome DevTools Protocol with zero external dependencies.
- **Overlap with mux:** web_retrieve (Playwright-backed, read-only).
- **Fit in mux:** Offer it in the opt-in `engineering` pack as a playbook (instructions only). Nothing is seeded until a user installs the pack.
- **Integration cost:** a single SKILL.md with no bundled scripts.
- **Assessment:** Needs Playwright installed; mux already ships Playwright for retrieval, so a command skill could reuse it.

- [x] Import `engineering/skills/full-page-screenshot/SKILL.md` into `packs/engineering/full-page-screenshot/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/engineering/full-page-screenshot` with category `frontend`.

### 15. migrate

- **Source:** `engineering-team/playwright-pro/skills/migrate/SKILL.md`
- **Category:** Engineering. **Simplicity** 9, **Value** 5, **Total** 14. **Recommendation:** Adopt as default.
- **What it does:** Migrate from Cypress or Selenium to Playwright. Use when user mentions "cypress", "selenium", "migrate tests", "convert tests", "switch to playwright", "move from cypress", or "replace selenium".
- **Overlap with mux:** none (no Playwright test skills).
- **Fit in mux:** Ship with mux as a playbook (instructions only), listed only where it applies (`cypress.config.*`, `**/*.cy.*`, `playwright.config.*`).
- **Integration cost:** a single SKILL.md with no bundled scripts.
- **Assessment:** Playwright test setup, generation, flaky-test fixing, and migration are everyday web work; gate on Playwright config or package.json.

- [x] Port `engineering-team/playwright-pro/skills/migrate/SKILL.md` into a default skill definition (a new `DefaultImportedSkills.cs`, or the family file named under Overlap), rewriting Claude Code references for mux and removing em-dashes.
- [x] Ship it as a playbook (no commands) and keep the procedure numbered, with an explicit output format.
- [x] Gate the listing with `appliesTo`: `cypress.config.*`, `**/*.cy.*`, `playwright.config.*`.
- [x] Add positive and negative cases to a new `ImportedSkillsSuite` (loads, validates, gating, body substitution, and each command against a fixture project).
- [x] Record the source and the MIT notice in `THIRD_PARTY_NOTICES.md`; add the skill to USAGE.md and the CHANGELOG. The notice is in THIRD_PARTY_NOTICES.md; the USAGE.md and CHANGELOG entries come with the release documentation pass.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Bundled/pw-migrate` with category `testing`. Shipped under the id `pw-migrate` (the original name `migrate` was too generic or shared with another skill). Shipped as a bundled folder (SKILL.md plus its files) rather than a C# definition, so the source text and scripts travel unchanged apart from normalization.

### 16. pw-review

- **Source:** `engineering-team/playwright-pro/skills/pw-review/SKILL.md`
- **Category:** Engineering. **Simplicity** 9, **Value** 5, **Total** 14. **Recommendation:** Adopt as default.
- **What it does:** Review Playwright tests for quality. Use when user says "review tests", "check test quality", "audit tests", "improve tests", "test code review", or "playwright best practices check".
- **Overlap with mux:** none (no Playwright test skills).
- **Fit in mux:** Ship with mux as a playbook (instructions only), listed only where it applies (`playwright.config.*`).
- **Integration cost:** a single SKILL.md with no bundled scripts.
- **Assessment:** Playwright test setup, generation, flaky-test fixing, and migration are everyday web work; gate on Playwright config or package.json.

- [x] Port `engineering-team/playwright-pro/skills/pw-review/SKILL.md` into a default skill definition (a new `DefaultImportedSkills.cs`, or the family file named under Overlap), rewriting Claude Code references for mux and removing em-dashes.
- [x] Ship it as a playbook (no commands) and keep the procedure numbered, with an explicit output format.
- [x] Gate the listing with `appliesTo`: `playwright.config.*`.
- [x] Add positive and negative cases to a new `ImportedSkillsSuite` (loads, validates, gating, body substitution, and each command against a fixture project).
- [x] Record the source and the MIT notice in `THIRD_PARTY_NOTICES.md`; add the skill to USAGE.md and the CHANGELOG. The notice is in THIRD_PARTY_NOTICES.md; the USAGE.md and CHANGELOG entries come with the release documentation pass.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Bundled/pw-review` with category `testing`. Shipped as a bundled folder (SKILL.md plus its files) rather than a C# definition, so the source text and scripts travel unchanged apart from normalization.

### 17. report

- **Source:** `engineering-team/playwright-pro/skills/report/SKILL.md`
- **Category:** Engineering. **Simplicity** 9, **Value** 5, **Total** 14. **Recommendation:** Adopt as default.
- **What it does:** Generate test report. Use when user says "test report", "results summary", "test status", "show results", "test dashboard", or "how did tests go".
- **Overlap with mux:** none (no Playwright test skills).
- **Fit in mux:** Ship with mux as a playbook (instructions only), listed only where it applies (`playwright.config.*`).
- **Integration cost:** a single SKILL.md with no bundled scripts.
- **Assessment:** Playwright test setup, generation, flaky-test fixing, and migration are everyday web work; gate on Playwright config or package.json.

- [x] Port `engineering-team/playwright-pro/skills/report/SKILL.md` into a default skill definition (a new `DefaultImportedSkills.cs`, or the family file named under Overlap), rewriting Claude Code references for mux and removing em-dashes.
- [x] Ship it as a playbook (no commands) and keep the procedure numbered, with an explicit output format.
- [x] Gate the listing with `appliesTo`: `playwright.config.*`.
- [x] Add positive and negative cases to a new `ImportedSkillsSuite` (loads, validates, gating, body substitution, and each command against a fixture project).
- [x] Record the source and the MIT notice in `THIRD_PARTY_NOTICES.md`; add the skill to USAGE.md and the CHANGELOG. The notice is in THIRD_PARTY_NOTICES.md; the USAGE.md and CHANGELOG entries come with the release documentation pass.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Bundled/pw-report` with category `testing`. Shipped under the id `pw-report` (the original name `report` was too generic or shared with another skill). Shipped as a bundled folder (SKILL.md plus its files) rather than a C# definition, so the source text and scripts travel unchanged apart from normalization.

### 18. ci-cd-pipeline-builder

- **Source:** `engineering/skills/ci-cd-pipeline-builder/SKILL.md`
- **Category:** Engineering. **Simplicity** 7, **Value** 6, **Total** 13. **Recommendation:** Adopt as default.
- **What it does:** Generate pragmatic CI/CD pipelines from detected project stack signals. fast baseline generation, repeatable checks, environment-aware deployment stages. Use when setting up CI for a new project, refactoring existing pipelines, or standardizing deployment workflows across multiple repos.
- **Overlap with mux:** ci-watch (watches, does not generate).
- **Fit in mux:** Ship with mux as a hybrid skill whose commands run the bundled scripts, listed only where it applies (the same project files as `project-detect` (it is most useful where `.github/workflows` does not exist yet)).
- **Integration cost:** 2 bundled scripts, standard-library Python; bundled `references/`.
- **Assessment:** Generating a first CI workflow from detected stack signals pairs naturally with project-detect.

- [x] Port `engineering/skills/ci-cd-pipeline-builder/SKILL.md` into a default skill definition (a new `DefaultImportedSkills.cs`, or the family file named under Overlap), rewriting Claude Code references for mux and removing em-dashes.
- [~] Wrap the 2 Python scripts as `python` commands with `requiresTools: [python3]`, mapping their exit codes to mux's 0/1/2 convention and adding `MUX_SKILL_DRY_RUN` support; or port the logic to the shared `mux-skill.ps1` helper if it is small.
- [x] Gate the listing with `appliesTo`: the same project files as `project-detect` (it is most useful where `.github/workflows` does not exist yet).
- [x] Add positive and negative cases to a new `ImportedSkillsSuite` (loads, validates, gating, body substitution, and each command against a fixture project).
- [x] Record the source and the MIT notice in `THIRD_PARTY_NOTICES.md`; add the skill to USAGE.md and the CHANGELOG. The notice is in THIRD_PARTY_NOTICES.md; the USAGE.md and CHANGELOG entries come with the release documentation pass.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Bundled/ci-cd-pipeline-builder` with category `devops`. Shipped as a bundled folder (SKILL.md plus its files) rather than a C# definition, so the source text and scripts travel unchanged apart from normalization. Partial: the bundled scripts are called from the body as `python3 "${SKILL_DIR}/scripts/..."` but were not executed during the import (the source is treated as untrusted), and `requiresTools: [python3]` is not declared because Windows installs usually name the interpreter `python`, which would hide the skill there. No `run_skill` command wrappers yet.

### 19. performance-profiler

- **Source:** `engineering/skills/performance-profiler/SKILL.md`
- **Category:** Engineering. **Simplicity** 7, **Value** 6, **Total** 13. **Recommendation:** Adopt as default.
- **What it does:** Systematic performance profiling for Node.js, Python, and Go applications. Identifies CPU, memory, and I/O bottlenecks, generates flamegraphs, analyzes bundle sizes, optimizes database queries, runs load tests with k6 and Artillery. Always measures before and after. Use when investigating a slow endpoint, planning a performance budget, or hunting a memory leak in production.
- **Overlap with mux:** none.
- **Fit in mux:** Ship with mux as a hybrid skill whose commands run the bundled scripts, listed only where it applies (`package.json`, `pyproject.toml`, `requirements*.txt`, `go.mod`).
- **Integration cost:** 1 bundled script, standard-library Python; bundled `references/`.
- **Assessment:** Profiling Node, Python, and Go is a real gap; gate per language like the toolchain skills.

- [x] Port `engineering/skills/performance-profiler/SKILL.md` into a default skill definition (a new `DefaultImportedSkills.cs`, or the family file named under Overlap), rewriting Claude Code references for mux and removing em-dashes.
- [~] Wrap the 1 Python script as `python` commands with `requiresTools: [python3]`, mapping their exit codes to mux's 0/1/2 convention and adding `MUX_SKILL_DRY_RUN` support; or port the logic to the shared `mux-skill.ps1` helper if it is small.
- [x] Gate the listing with `appliesTo`: `package.json`, `pyproject.toml`, `requirements*.txt`, `go.mod`.
- [x] Add positive and negative cases to a new `ImportedSkillsSuite` (loads, validates, gating, body substitution, and each command against a fixture project).
- [x] Record the source and the MIT notice in `THIRD_PARTY_NOTICES.md`; add the skill to USAGE.md and the CHANGELOG. The notice is in THIRD_PARTY_NOTICES.md; the USAGE.md and CHANGELOG entries come with the release documentation pass.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Bundled/performance-profiler` with category `devops`. Shipped as a bundled folder (SKILL.md plus its files) rather than a C# definition, so the source text and scripts travel unchanged apart from normalization. Partial: the bundled scripts are called from the body as `python3 "${SKILL_DIR}/scripts/..."` but were not executed during the import (the source is treated as untrusted), and `requiresTools: [python3]` is not declared because Windows installs usually name the interpreter `python`, which would hide the skill there. No `run_skill` command wrappers yet.

### 20. ship-gate

- **Source:** `engineering/skills/ship-gate/SKILL.md`
- **Category:** Engineering. **Simplicity** 7, **Value** 6, **Total** 13. **Recommendation:** Adopt as default.
- **What it does:** Pre-production audit that scans a codebase for security, database, deployment, code quality, AI/LLM, dependency, frontend, and observability issues. Intercepts deploy commands and blocks until critical items pass. Stack-agnostic. Use for "run ship gate", "am I ready to ship", "pre-launch audit", "can I deploy", "push to production", "go live checklist", "preflight check". Not for CI/CD setup or
- **Overlap with mux:** security-review, code-review (partial).
- **Fit in mux:** Ship with mux as a hybrid skill whose commands run the bundled scripts, listed only where it applies (none (listed everywhere)).
- **Integration cost:** 1 bundled script, standard-library Python; bundled `references/`.
- **Assessment:** A pre-release audit across security, deployment, and observability complements the review skills.

- [x] Port `engineering/skills/ship-gate/SKILL.md` into a default skill definition (a new `DefaultImportedSkills.cs`, or the family file named under Overlap), rewriting Claude Code references for mux and removing em-dashes.
- [~] Wrap the 1 Python script as `python` commands with `requiresTools: [python3]`, mapping their exit codes to mux's 0/1/2 convention and adding `MUX_SKILL_DRY_RUN` support; or port the logic to the shared `mux-skill.ps1` helper if it is small.
- [x] Gate the listing with `appliesTo`: none (listed everywhere).
- [x] Add positive and negative cases to a new `ImportedSkillsSuite` (loads, validates, gating, body substitution, and each command against a fixture project).
- [x] Record the source and the MIT notice in `THIRD_PARTY_NOTICES.md`; add the skill to USAGE.md and the CHANGELOG. The notice is in THIRD_PARTY_NOTICES.md; the USAGE.md and CHANGELOG entries come with the release documentation pass.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Bundled/ship-gate` with category `devops`. Shipped as a bundled folder (SKILL.md plus its files) rather than a C# definition, so the source text and scripts travel unchanged apart from normalization. Partial: the bundled scripts are called from the body as `python3 "${SKILL_DIR}/scripts/..."` but were not executed during the import (the source is treated as untrusted), and `requiresTools: [python3]` is not declared because Windows installs usually name the interpreter `python`, which would hide the skill there. No `run_skill` command wrappers yet.

### 21. ar-resume

- **Source:** `engineering/autoresearch-agent/skills/ar-resume/SKILL.md`
- **Category:** Engineering. **Simplicity** 9, **Value** 4, **Total** 13. **Recommendation:** Optional pack: engineering.
- **What it does:** Resume a paused experiment. Checkout the experiment branch, read results history, continue iterating. Use when the user runs /ar:ar-resume or asks to pick up a previously started autoresearch experiment.
- **Overlap with mux:** /loop, loop-until, fix-until-green.
- **Fit in mux:** Offer it in the opt-in `engineering` pack as a playbook (instructions only). Nothing is seeded until a user installs the pack.
- **Integration cost:** a single SKILL.md with no bundled scripts.
- **Assessment:** A metric-driven edit-run-keep loop is a nice addition to mux loops; the scheduling half uses CronCreate and must be rewritten for /loop.

- [x] Import `engineering/autoresearch-agent/skills/ar-resume/SKILL.md` into `packs/engineering/ar-resume/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/engineering/ar-resume` with category `research`.

### 22. ar-status

- **Source:** `engineering/autoresearch-agent/skills/ar-status/SKILL.md`
- **Category:** Engineering. **Simplicity** 9, **Value** 4, **Total** 13. **Recommendation:** Optional pack: engineering.
- **What it does:** Show experiment dashboard with results, active loops, and progress. Use when the user runs /ar:ar-status or asks how an autoresearch experiment is going.
- **Overlap with mux:** /loop, loop-until, fix-until-green.
- **Fit in mux:** Offer it in the opt-in `engineering` pack as a playbook (instructions only). Nothing is seeded until a user installs the pack.
- **Integration cost:** a single SKILL.md with no bundled scripts.
- **Assessment:** A metric-driven edit-run-keep loop is a nice addition to mux loops; the scheduling half uses CronCreate and must be rewritten for /loop.

- [x] Import `engineering/autoresearch-agent/skills/ar-status/SKILL.md` into `packs/engineering/ar-status/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/engineering/ar-status` with category `research`.

### 23. board

- **Source:** `engineering/agenthub/skills/board/SKILL.md`
- **Category:** Engineering. **Simplicity** 9, **Value** 4, **Total** 13. **Recommendation:** Optional pack: engineering.
- **What it does:** Read, write, and browse the AgentHub message board for agent coordination. Use when the user runs /hub:board or asks to post, read, or inspect coordination messages between competing AgentHub agents.
- **Overlap with mux:** worktree isolation, subagents.
- **Fit in mux:** Offer it in the opt-in `engineering` pack as a playbook (instructions only). Nothing is seeded until a user installs the pack.
- **Integration cost:** a single SKILL.md with no bundled scripts.
- **Assessment:** Competing parallel agents in worktrees maps onto mux subagent isolation, but the skill drives Claude Code primitives; port only the lifecycle idea.

- [x] Import `engineering/agenthub/skills/board/SKILL.md` into `packs/engineering/board/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/engineering/hub-board` with category `workflow`. Shipped under the id `hub-board` (the original name `board` was too generic or shared with another skill).

### 24. boost-asio-pro

- **Source:** `engineering/boost-asio-pro/SKILL.md`
- **Category:** Engineering. **Simplicity** 9, **Value** 4, **Total** 13. **Recommendation:** Optional pack: engineering.
- **What it does:** Use when writing or reviewing asynchronous C++ networking code with Boost.Asio or standalone Asio. TCP/UDP servers and clients, SSL/TLS, timers, strands, io_context, co_spawn, awaitable, async_read/async_write, asio::spawn, yield_context, or pre-C++20 completion-handler callbacks.
- **Overlap with mux:** cpp-* (build tooling only).
- **Fit in mux:** Offer it in the opt-in `engineering` pack as a playbook (instructions only). Nothing is seeded until a user installs the pack.
- **Integration cost:** a single SKILL.md with no bundled scripts; bundled `references/`.
- **Assessment:** Specialist C++ networking reference; complements the C++ family for a narrow audience.

- [x] Import `engineering/boost-asio-pro/SKILL.md` into `packs/engineering/boost-asio-pro/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/engineering/boost-asio-pro` with category `engineering`.

### 25. code-tour

- **Source:** `engineering/code-tour/skills/code-tour/SKILL.md`
- **Category:** Engineering. **Simplicity** 9, **Value** 4, **Total** 13. **Recommendation:** Optional pack: engineering.
- **What it does:** Use when the user asks to create a CodeTour .tour file. persona-targeted, step-by-step walkthroughs that link to real files and line numbers. Trigger for: create a tour, onboarding tour, architecture tour, PR review tour, explain how X works, vibe check, RCA tour, contributor guide, or any structured code walkthrough request.
- **Overlap with mux:** explain-codebase (partial).
- **Fit in mux:** Offer it in the opt-in `engineering` pack as a playbook (instructions only). Nothing is seeded until a user installs the pack.
- **Integration cost:** a single SKILL.md with no bundled scripts.

- [x] Import `engineering/code-tour/skills/code-tour/SKILL.md` into `packs/engineering/code-tour/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/engineering/code-tour` with category `docs`.

### 26. eval

- **Source:** `engineering/agenthub/skills/eval/SKILL.md`
- **Category:** Engineering. **Simplicity** 9, **Value** 4, **Total** 13. **Recommendation:** Optional pack: engineering.
- **What it does:** Evaluate and rank agent results by metric or LLM judge for an AgentHub session. Use when the user runs /hub:eval or asks to score, compare, or pick a winner among completed AgentHub agents.
- **Overlap with mux:** worktree isolation, subagents.
- **Fit in mux:** Offer it in the opt-in `engineering` pack as a playbook (instructions only). Nothing is seeded until a user installs the pack.
- **Integration cost:** a single SKILL.md with no bundled scripts.
- **Assessment:** Competing parallel agents in worktrees maps onto mux subagent isolation, but the skill drives Claude Code primitives; port only the lifecycle idea.

- [x] Import `engineering/agenthub/skills/eval/SKILL.md` into `packs/engineering/eval/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/engineering/hub-eval` with category `workflow`. Shipped under the id `hub-eval` (the original name `eval` was too generic or shared with another skill).

### 27. hub-init

- **Source:** `engineering/agenthub/skills/hub-init/SKILL.md`
- **Category:** Engineering. **Simplicity** 9, **Value** 4, **Total** 13. **Recommendation:** Optional pack: engineering.
- **What it does:** Create a new AgentHub collaboration session with task, agent count, and evaluation criteria. Use when the user runs /hub:hub-init or asks to start a multi-agent competition on a task.
- **Overlap with mux:** worktree isolation, subagents.
- **Fit in mux:** Offer it in the opt-in `engineering` pack as a playbook (instructions only). Nothing is seeded until a user installs the pack.
- **Integration cost:** a single SKILL.md with no bundled scripts.
- **Assessment:** Competing parallel agents in worktrees maps onto mux subagent isolation, but the skill drives Claude Code primitives; port only the lifecycle idea.

- [x] Import `engineering/agenthub/skills/hub-init/SKILL.md` into `packs/engineering/hub-init/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/engineering/hub-init` with category `workflow`.

### 28. hub-status

- **Source:** `engineering/agenthub/skills/hub-status/SKILL.md`
- **Category:** Engineering. **Simplicity** 9, **Value** 4, **Total** 13. **Recommendation:** Optional pack: engineering.
- **What it does:** Show DAG state, agent progress, and branch status for an AgentHub session. Use when the user runs /hub:hub-status or asks how the AgentHub agents are doing.
- **Overlap with mux:** worktree isolation, subagents.
- **Fit in mux:** Offer it in the opt-in `engineering` pack as a playbook (instructions only). Nothing is seeded until a user installs the pack.
- **Integration cost:** a single SKILL.md with no bundled scripts.
- **Assessment:** Competing parallel agents in worktrees maps onto mux subagent isolation, but the skill drives Claude Code primitives; port only the lifecycle idea.

- [x] Import `engineering/agenthub/skills/hub-status/SKILL.md` into `packs/engineering/hub-status/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/engineering/hub-status` with category `workflow`.

### 29. llm-cost-optimizer

- **Source:** `engineering/llm-cost-optimizer/skills/llm-cost-optimizer/SKILL.md`
- **Category:** Engineering. **Simplicity** 9, **Value** 4, **Total** 13. **Recommendation:** Optional pack: engineering.
- **What it does:** Use proactively whenever LLM API costs come up -- or should. Triggers include: 'my AI costs are too high', 'optimize token usage', 'which model should I use', 'LLM spend is out of control', 'implement prompt caching', 'we're about to launch an AI feature', 'build me an AI endpoint'. Don't wait for an explicit cost complaint -- if someone is building an AI feature, designing an LLM endpoint, or cho
- **Overlap with mux:** none.
- **Fit in mux:** Offer it in the opt-in `engineering` pack as a playbook (instructions only). Nothing is seeded until a user installs the pack.
- **Integration cost:** a single SKILL.md with no bundled scripts.
- **Assessment:** Relevant to people building LLM systems, which is a subset of mux users.

- [x] Import `engineering/llm-cost-optimizer/skills/llm-cost-optimizer/SKILL.md` into `packs/engineering/llm-cost-optimizer/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/engineering/llm-cost-optimizer` with category `workflow`.

### 30. loop-library

- **Source:** `loop-library/SKILL.md`
- **Category:** Agent loops. **Simplicity** 9, **Value** 4, **Total** 13. **Recommendation:** Optional pack: engineering.
- **What it does:** Discover, find, compare, audit, repair, adapt, and design repeatable AI-agent loops with explicit triggers, actions, verification, stopping conditions, guardrails, and handoffs. Use when a user asks to analyze a codebase for potential loops, mine coding-thread history for work done more than once, turn repeated engineering work into a loop, find or recommend a published loop, create a recurring ag
- **Overlap with mux:** /loop, loop skills.
- **Fit in mux:** Offer it in the opt-in `engineering` pack as a playbook (instructions only). Nothing is seeded until a user installs the pack.
- **Integration cost:** a single SKILL.md with no bundled scripts; bundled `references/`.
- **Assessment:** A catalog of loop designs; useful as reference material for mux loops.

- [x] Import `loop-library/SKILL.md` into `packs/engineering/loop-library/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/engineering/loop-library` with category `loops`.

### 31. merge

- **Source:** `engineering/agenthub/skills/merge/SKILL.md`
- **Category:** Engineering. **Simplicity** 9, **Value** 4, **Total** 13. **Recommendation:** Optional pack: engineering.
- **What it does:** Merge the winning agent's branch into base, archive losers, and clean up worktrees. Use when the user runs /hub:merge or asks to land the winning AgentHub result and tidy the session.
- **Overlap with mux:** worktree isolation, subagents.
- **Fit in mux:** Offer it in the opt-in `engineering` pack as a playbook (instructions only). Nothing is seeded until a user installs the pack.
- **Integration cost:** a single SKILL.md with no bundled scripts.
- **Assessment:** Competing parallel agents in worktrees maps onto mux subagent isolation, but the skill drives Claude Code primitives; port only the lifecycle idea.

- [x] Import `engineering/agenthub/skills/merge/SKILL.md` into `packs/engineering/merge/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/engineering/hub-merge` with category `workflow`. Shipped under the id `hub-merge` (the original name `merge` was too generic or shared with another skill).

### 32. minimalist

- **Source:** `engineering/minimalist/SKILL.md`
- **Category:** Engineering. **Simplicity** 9, **Value** 4, **Total** 13. **Recommendation:** Optional pack: engineering.
- **What it does:** Use when the user asks to write code efficiently, avoid over-engineering, reduce dependencies, or prevent unnecessary abstractions. Enforces a strict efficiency ladder: YAGNI, reuse, stdlib, native platform, existing deps. before writing any new code.
- **Overlap with mux:** none.
- **Fit in mux:** Offer it in the opt-in `engineering` pack as a playbook (instructions only). Nothing is seeded until a user installs the pack.
- **Integration cost:** a single SKILL.md with no bundled scripts.
- **Assessment:** Coding-discipline playbooks; useful as opt-in styles, too opinionated for a default.

- [x] Import `engineering/minimalist/SKILL.md` into `packs/engineering/minimalist/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/engineering/minimalist` with category `engineering`.

### 33. pr-review-expert

- **Source:** `engineering/skills/pr-review-expert/SKILL.md`
- **Category:** Engineering. **Simplicity** 9, **Value** 4, **Total** 13. **Recommendation:** Adapt into code-review.
- **What it does:** Use when the user asks to review pull requests, analyze code changes, check for security issues in PRs, or assess code quality of diffs.
- **Overlap with mux:** code-review, pr-comments.
- **Fit in mux:** Do not ship it on its own. Its useful parts go into `code-review`.
- **Integration cost:** a single SKILL.md with no bundled scripts.
- **Assessment:** Largely duplicates code-review in pr mode; keep only checklist items code-review lacks.

- [x] Read `engineering/skills/pr-review-expert/SKILL.md` and list the procedures or checks that `code-review` does not already have.
- [x] Fold those into `code-review` (body, a new command, or an effort word), keeping mux's output format and exit codes.
- [x] Extend the tests that cover `code-review` with a case for each added check.
- [x] Note the borrowed ideas and the MIT source in `THIRD_PARTY_NOTICES.md` and the CHANGELOG. The notice is in THIRD_PARTY_NOTICES.md; the USAGE.md and CHANGELOG entries come with the release documentation pass.

**Notes:** Merged on 2026-10-09; see the target skill's body and, where noted in THIRD_PARTY_NOTICES.md, its `resources/` folder.

### 34. prompt-governance

- **Source:** `engineering/prompt-governance/skills/prompt-governance/SKILL.md`
- **Category:** Engineering. **Simplicity** 9, **Value** 4, **Total** 13. **Recommendation:** Optional pack: engineering.
- **What it does:** Use when managing prompts in production at scale: versioning prompts, running A/B tests on prompts, building prompt registries, preventing prompt regressions, or creating eval pipelines for production AI features. Triggers: 'manage prompts in production', 'prompt versioning', 'prompt regression', 'prompt A/B test', 'prompt registry', 'eval pipeline'. NOT for writing or improving individual prompts
- **Overlap with mux:** none.
- **Fit in mux:** Offer it in the opt-in `engineering` pack as a playbook (instructions only). Nothing is seeded until a user installs the pack.
- **Integration cost:** a single SKILL.md with no bundled scripts.
- **Assessment:** Relevant to people building LLM systems, which is a subset of mux users.

- [x] Import `engineering/prompt-governance/skills/prompt-governance/SKILL.md` into `packs/engineering/prompt-governance/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/engineering/prompt-governance` with category `workflow`.

### 35. run (agenthub)

- **Source:** `engineering/agenthub/skills/run/SKILL.md`
- **Category:** Engineering. **Simplicity** 9, **Value** 4, **Total** 13. **Recommendation:** Optional pack: engineering.
- **What it does:** One-shot lifecycle command that chains init → baseline → spawn → eval → merge in a single invocation. Use when the user runs /hub:run or asks to execute a full AgentHub competition end-to-end.
- **Overlap with mux:** worktree isolation, subagents.
- **Fit in mux:** Offer it in the opt-in `engineering` pack as a playbook (instructions only). Nothing is seeded until a user installs the pack.
- **Integration cost:** a single SKILL.md with no bundled scripts.
- **Assessment:** Competing parallel agents in worktrees maps onto mux subagent isolation, but the skill drives Claude Code primitives; port only the lifecycle idea.

- [x] Import `engineering/agenthub/skills/run/SKILL.md` into `packs/engineering/run/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/engineering/hub-run` with category `workflow`. Shipped under the id `hub-run` (the original name `run` was too generic or shared with another skill).

### 36. run (autoresearch-agent)

- **Source:** `engineering/autoresearch-agent/skills/run/SKILL.md`
- **Category:** Engineering. **Simplicity** 9, **Value** 4, **Total** 13. **Recommendation:** Optional pack: engineering.
- **What it does:** Run a single experiment iteration. Edit the target file, evaluate, keep or discard. Use when the user runs /ar:run or asks for one manual autoresearch iteration.
- **Overlap with mux:** /loop, loop-until, fix-until-green.
- **Fit in mux:** Offer it in the opt-in `engineering` pack as a playbook (instructions only). Nothing is seeded until a user installs the pack.
- **Integration cost:** a single SKILL.md with no bundled scripts.
- **Assessment:** A metric-driven edit-run-keep loop is a nice addition to mux loops; the scheduling half uses CronCreate and must be rewritten for /loop.

- [x] Import `engineering/autoresearch-agent/skills/run/SKILL.md` into `packs/engineering/run/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/engineering/ar-run` with category `research`. Shipped under the id `ar-run` (the original name `run` was too generic or shared with another skill).

### 37. self-eval

- **Source:** `engineering/skills/self-eval/SKILL.md`
- **Category:** Engineering. **Simplicity** 9, **Value** 4, **Total** 13. **Recommendation:** Optional pack: engineering.
- **What it does:** Honestly evaluate AI work quality using a two-axis scoring system. Use after completing a task, code review, or work session to get an unbiased assessment. Detects score inflation, forces devil's advocate reasoning, and persists scores across sessions.
- **Overlap with mux:** none.
- **Fit in mux:** Offer it in the opt-in `engineering` pack as a playbook (instructions only). Nothing is seeded until a user installs the pack.
- **Integration cost:** a single SKILL.md with no bundled scripts.

- [x] Import `engineering/skills/self-eval/SKILL.md` into `packs/engineering/self-eval/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/engineering/self-eval` with category `workflow`.

### 38. setup

- **Source:** `engineering/autoresearch-agent/skills/setup/SKILL.md`
- **Category:** Engineering. **Simplicity** 9, **Value** 4, **Total** 13. **Recommendation:** Optional pack: engineering.
- **What it does:** Set up a new autoresearch experiment interactively. Collects domain, target file, eval command, metric, direction, and evaluator. Use when the user runs /ar:setup or asks to start optimizing a file with the autoresearch loop.
- **Overlap with mux:** /loop, loop-until, fix-until-green.
- **Fit in mux:** Offer it in the opt-in `engineering` pack as a playbook (instructions only). Nothing is seeded until a user installs the pack.
- **Integration cost:** a single SKILL.md with no bundled scripts.
- **Assessment:** A metric-driven edit-run-keep loop is a nice addition to mux loops; the scheduling half uses CronCreate and must be rewritten for /loop.

- [x] Import `engineering/autoresearch-agent/skills/setup/SKILL.md` into `packs/engineering/setup/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/engineering/ar-setup` with category `research`. Shipped under the id `ar-setup` (the original name `setup` was too generic or shared with another skill).

### 39. spawn

- **Source:** `engineering/agenthub/skills/spawn/SKILL.md`
- **Category:** Engineering. **Simplicity** 9, **Value** 4, **Total** 13. **Recommendation:** Optional pack: engineering.
- **What it does:** Launch N parallel subagents in isolated git worktrees to compete on the session task. Use when the user runs /hub:spawn or asks to start the competing agents for an initialized AgentHub session.
- **Overlap with mux:** worktree isolation, subagents.
- **Fit in mux:** Offer it in the opt-in `engineering` pack as a playbook (instructions only). Nothing is seeded until a user installs the pack.
- **Integration cost:** a single SKILL.md with no bundled scripts.
- **Assessment:** Competing parallel agents in worktrees maps onto mux subagent isolation, but the skill drives Claude Code primitives; port only the lifecycle idea.

- [x] Import `engineering/agenthub/skills/spawn/SKILL.md` into `packs/engineering/spawn/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/engineering/hub-spawn` with category `workflow`. Shipped under the id `hub-spawn` (the original name `spawn` was too generic or shared with another skill).

### 40. strict-api

- **Source:** `engineering/strict-api/SKILL.md`
- **Category:** Engineering. **Simplicity** 9, **Value** 4, **Total** 13. **Recommendation:** Optional pack: engineering.
- **What it does:** Use when the user says 'no hallucinations', 'verify APIs', 'reality check', or 'don't invent functions'. Prevents the agent from calling methods, imports, or variables that do not provably exist in the user's installed version.
- **Overlap with mux:** none.
- **Fit in mux:** Offer it in the opt-in `engineering` pack as a playbook (instructions only). Nothing is seeded until a user installs the pack.
- **Integration cost:** a single SKILL.md with no bundled scripts.
- **Assessment:** Coding-discipline playbooks; useful as opt-in styles, too opinionated for a default.

- [x] Import `engineering/strict-api/SKILL.md` into `packs/engineering/strict-api/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/engineering/strict-api` with category `engineering`.

### 41. zero-hallucination-coder

- **Source:** `engineering/zero-hallucination-coder/skills/zero-hallucination-coder/SKILL.md`
- **Category:** Engineering. **Simplicity** 9, **Value** 4, **Total** 13. **Recommendation:** Optional pack: engineering.
- **What it does:** Runs a disciplined Discuss -> Map -> Decompose -> Execute -> Verify loop that grounds code in verified structure. no invented APIs, no assumed imports, no placeholder code. with a lazy-senior-dev YAGNI ladder that deletes unnecessary code before it is written. Use when a coding task is high-stakes, complex, or spans existing code (auth, databases, migrations, multi-file features), or when the us
- **Overlap with mux:** none.
- **Fit in mux:** Offer it in the opt-in `engineering` pack as a playbook (instructions only). Nothing is seeded until a user installs the pack.
- **Integration cost:** a single SKILL.md with no bundled scripts.
- **Assessment:** Coding-discipline playbooks; useful as opt-in styles, too opinionated for a default.

- [x] Import `engineering/zero-hallucination-coder/skills/zero-hallucination-coder/SKILL.md` into `packs/engineering/zero-hallucination-coder/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/engineering/zero-hallucination-coder` with category `engineering`.

### 42. api-design-reviewer

- **Source:** `engineering/skills/api-design-reviewer/SKILL.md`
- **Category:** Engineering. **Simplicity** 6, **Value** 6, **Total** 12. **Recommendation:** Adopt as default.
- **What it does:** Comprehensive REST API design review with automated linting, breaking-change detection, and design scorecards. Catches inconsistent conventions, missing versioning, and design smells before APIs ship. Use when reviewing a PR that adds or changes API endpoints, auditing an existing API for v2 migration, or establishing API standards for a team.
- **Overlap with mux:** none.
- **Fit in mux:** Ship with mux as a hybrid skill whose commands run the bundled scripts, listed only where it applies (`**/openapi*.yaml`, `**/openapi*.json`, `**/swagger*.json`, `**/swagger*.yaml`).
- **Integration cost:** 3 bundled scripts, standard-library Python; 432 lines, long for small models; bundled `references/`.
- **Assessment:** Breaking-change detection on an API surface is a common need; gate on OpenAPI files.

- [x] Port `engineering/skills/api-design-reviewer/SKILL.md` into a default skill definition (a new `DefaultImportedSkills.cs`, or the family file named under Overlap), rewriting Claude Code references for mux and removing em-dashes.
- [~] Wrap the 3 Python scripts as `python` commands with `requiresTools: [python3]`, mapping their exit codes to mux's 0/1/2 convention and adding `MUX_SKILL_DRY_RUN` support; or port the logic to the shared `mux-skill.ps1` helper if it is small.
- [x] Gate the listing with `appliesTo`: `**/openapi*.yaml`, `**/openapi*.json`, `**/swagger*.json`, `**/swagger*.yaml`.
- [x] Add positive and negative cases to a new `ImportedSkillsSuite` (loads, validates, gating, body substitution, and each command against a fixture project).
- [x] Record the source and the MIT notice in `THIRD_PARTY_NOTICES.md`; add the skill to USAGE.md and the CHANGELOG. The notice is in THIRD_PARTY_NOTICES.md; the USAGE.md and CHANGELOG entries come with the release documentation pass.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Bundled/api-design-reviewer` with category `review`. Shipped as a bundled folder (SKILL.md plus its files) rather than a C# definition, so the source text and scripts travel unchanged apart from normalization. Partial: the bundled scripts are called from the body as `python3 "${SKILL_DIR}/scripts/..."` but were not executed during the import (the source is treated as untrusted), and `requiresTools: [python3]` is not declared because Windows installs usually name the interpreter `python`, which would hide the skill there. No `run_skill` command wrappers yet.

### 43. ai-security

- **Source:** `engineering-team/skills/ai-security/SKILL.md`
- **Category:** Engineering. **Simplicity** 7, **Value** 5, **Total** 12. **Recommendation:** Optional pack: security.
- **What it does:** Use when assessing AI/ML systems for prompt injection, jailbreak vulnerabilities, model inversion risk, data poisoning exposure, or agent tool abuse. Covers MITRE ATLAS technique mapping, injection signature detection, and adversarial robustness scoring.
- **Overlap with mux:** security-review (code only).
- **Fit in mux:** Offer it in the opt-in `security` pack as a hybrid skill whose commands run the bundled scripts. Nothing is seeded until a user installs the pack.
- **Integration cost:** 1 bundled script, standard-library Python; bundled `references/`.

- [x] Import `engineering-team/skills/ai-security/SKILL.md` into `packs/security/ai-security/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [~] Declare `requiresTools: [python3]` and confirm the 1 bundled script run from the skill folder.
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/security/ai-security` with category `security`. Partial: the bundled scripts are called from the body as `python3 "${SKILL_DIR}/scripts/..."` but were not executed during the import (the source is treated as untrusted), and `requiresTools: [python3]` is not declared because Windows installs usually name the interpreter `python`, which would hide the skill there. No `run_skill` command wrappers yet.

### 44. changelog-generator

- **Source:** `engineering/skills/changelog-generator/SKILL.md`
- **Category:** Engineering. **Simplicity** 7, **Value** 5, **Total** 12. **Recommendation:** Adapt into release-notes.
- **What it does:** Produce consistent, auditable release notes from Conventional Commits. Separates commit parsing, semantic-bump logic, and changelog rendering for automated releases with editorial control. Use when cutting a release, generating CHANGELOG.md from git history, computing the next semantic version from commits, automating release notes in CI, or planning a hotfix/rollback. Examples: 'generate the chan
- **Overlap with mux:** git-changelog-entry, release-notes.
- **Fit in mux:** Do not ship it on its own. Its useful parts go into `release-notes`.
- **Integration cost:** 3 bundled scripts, standard-library Python; bundled `references/`, `assets/`.
- **Assessment:** Conventional-commit parsing and semver bump logic would make release-notes deterministic.

- [x] Read `engineering/skills/changelog-generator/SKILL.md` and list the procedures or checks that `release-notes` does not already have.
- [x] Fold those into `release-notes` (body, a new command, or an effort word), keeping mux's output format and exit codes.
- [x] Extend the tests that cover `release-notes` with a case for each added check.
- [x] Note the borrowed ideas and the MIT source in `THIRD_PARTY_NOTICES.md` and the CHANGELOG. The notice is in THIRD_PARTY_NOTICES.md; the USAGE.md and CHANGELOG entries come with the release documentation pass.

**Notes:** Merged on 2026-10-09; see the target skill's body and, where noted in THIRD_PARTY_NOTICES.md, its `resources/` folder.

### 45. cloud-security

- **Source:** `engineering-team/skills/cloud-security/SKILL.md`
- **Category:** Engineering. **Simplicity** 7, **Value** 5, **Total** 12. **Recommendation:** Optional pack: security.
- **What it does:** Use when assessing cloud infrastructure for security misconfigurations, IAM privilege escalation paths, S3 public exposure, open security group rules, or IaC security gaps. Covers AWS, Azure, and GCP posture assessment with MITRE ATT&CK mapping.
- **Overlap with mux:** security-review (code only).
- **Fit in mux:** Offer it in the opt-in `security` pack as a hybrid skill whose commands run the bundled scripts. Nothing is seeded until a user installs the pack.
- **Integration cost:** 1 bundled script, standard-library Python; bundled `references/`.

- [x] Import `engineering-team/skills/cloud-security/SKILL.md` into `packs/security/cloud-security/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [~] Declare `requiresTools: [python3]` and confirm the 1 bundled script run from the skill folder.
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/security/cloud-security` with category `security`. Partial: the bundled scripts are called from the body as `python3 "${SKILL_DIR}/scripts/..."` but were not executed during the import (the source is treated as untrusted), and `requiresTools: [python3]` is not declared because Windows installs usually name the interpreter `python`, which would hide the skill there. No `run_skill` command wrappers yet.

### 46. code-reviewer

- **Source:** `engineering-team/skills/code-reviewer/SKILL.md`
- **Category:** Engineering. **Simplicity** 7, **Value** 5, **Total** 12. **Recommendation:** Adapt into code-review.
- **What it does:** Code review automation for TypeScript, JavaScript, Python, Go, Swift, Kotlin, C#, .NET, Java, C, C++, Rust, Ruby, PHP, and Dart/Flutter. Analyzes PRs for complexity and risk, checks code quality for SOLID violations and code smells, generates review reports. Use when reviewing pull requests, analyzing code quality, identifying issues, generating review checklists.
- **Overlap with mux:** code-review.
- **Fit in mux:** Do not ship it on its own. Its useful parts go into `code-review`.
- **Integration cost:** 3 bundled scripts, standard-library Python; bundled `assets/`.
- **Assessment:** mux code-review already covers diff modes; the complexity and risk heuristics are worth folding into its body.

- [x] Read `engineering-team/skills/code-reviewer/SKILL.md` and list the procedures or checks that `code-review` does not already have.
- [x] Fold those into `code-review` (body, a new command, or an effort word), keeping mux's output format and exit codes.
- [x] Extend the tests that cover `code-review` with a case for each added check.
- [x] Note the borrowed ideas and the MIT source in `THIRD_PARTY_NOTICES.md` and the CHANGELOG. The notice is in THIRD_PARTY_NOTICES.md; the USAGE.md and CHANGELOG entries come with the release documentation pass.

**Notes:** Merged on 2026-10-09; see the target skill's body and, where noted in THIRD_PARTY_NOTICES.md, its `resources/` folder.

### 47. codebase-onboarding

- **Source:** `engineering/skills/codebase-onboarding/SKILL.md`
- **Category:** Engineering. **Simplicity** 7, **Value** 5, **Total** 12. **Recommendation:** Adapt into explain-codebase.
- **What it does:** Analyze a codebase and generate onboarding documentation for engineers, tech leads, and contractors. Fast fact-gathering and repeatable onboarding outputs. Use when onboarding a new engineer, writing architecture-overview docs for a new project, or producing tech-lead briefings for unfamiliar repos.
- **Overlap with mux:** init, explain-codebase.
- **Fit in mux:** Do not ship it on its own. Its useful parts go into `explain-codebase`.
- **Integration cost:** 1 bundled script, standard-library Python; bundled `references/`.
- **Assessment:** mux init and explain-codebase cover most of it; the persona-specific onboarding doc is the extra.

- [x] Read `engineering/skills/codebase-onboarding/SKILL.md` and list the procedures or checks that `explain-codebase` does not already have.
- [x] Fold those into `explain-codebase` (body, a new command, or an effort word), keeping mux's output format and exit codes.
- [x] Extend the tests that cover `explain-codebase` with a case for each added check.
- [x] Note the borrowed ideas and the MIT source in `THIRD_PARTY_NOTICES.md` and the CHANGELOG. The notice is in THIRD_PARTY_NOTICES.md; the USAGE.md and CHANGELOG entries come with the release documentation pass.

**Notes:** Merged on 2026-10-09; see the target skill's body and, where noted in THIRD_PARTY_NOTICES.md, its `resources/` folder.

### 48. database-designer

- **Source:** `engineering/skills/database-designer/SKILL.md`
- **Category:** Engineering. **Simplicity** 7, **Value** 5, **Total** 12. **Recommendation:** Optional pack: engineering.
- **What it does:** Use when the user asks to design database schemas, plan data migrations, optimize queries, choose between SQL and NoSQL, or model data relationships.
- **Overlap with mux:** none.
- **Fit in mux:** Offer it in the opt-in `engineering` pack as a hybrid skill whose commands run the bundled scripts. Nothing is seeded until a user installs the pack.
- **Integration cost:** 3 bundled scripts, standard-library Python; bundled `references/`, `assets/`.
- **Assessment:** Database work is common but varied; offer as a pack rather than a default.

- [x] Import `engineering/skills/database-designer/SKILL.md` into `packs/engineering/database-designer/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [~] Declare `requiresTools: [python3]` and confirm the 3 bundled scripts run from the skill folder.
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/engineering/database-designer` with category `data`. Partial: the bundled scripts are called from the body as `python3 "${SKILL_DIR}/scripts/..."` but were not executed during the import (the source is treated as untrusted), and `requiresTools: [python3]` is not declared because Windows installs usually name the interpreter `python`, which would hide the skill there. No `run_skill` command wrappers yet.

### 49. dependency-auditor

- **Source:** `engineering/skills/dependency-auditor/SKILL.md`
- **Category:** Engineering. **Simplicity** 7, **Value** 5, **Total** 12. **Recommendation:** Adapt into security-review.
- **What it does:** Audit and manage dependencies across multi-language projects. Identifies vulnerabilities, license conflicts, transitive dependency risks, and safe-upgrade paths. Use when auditing third-party packages before release, investigating a CVE, planning a major version bump, or running a license-compliance review. Examples: 'audit our npm dependencies', 'do we have GPL contamination', 'plan the upgrade t
- **Overlap with mux:** js-deps, py-deps, dotnet-outdated, java-deps, go-mod.
- **Fit in mux:** Do not ship it on its own. Its useful parts go into `security-review`.
- **Integration cost:** 3 bundled scripts, standard-library Python; bundled `references/`, `assets/`.
- **Assessment:** Its offline "CVE pattern set" is weaker than the real audit commands mux already runs; the license-conflict check is the part mux lacks.

- [x] Read `engineering/skills/dependency-auditor/SKILL.md` and list the procedures or checks that `security-review` does not already have.
- [x] Fold those into `security-review` (body, a new command, or an effort word), keeping mux's output format and exit codes.
- [x] Extend the tests that cover `security-review` with a case for each added check.
- [x] Note the borrowed ideas and the MIT source in `THIRD_PARTY_NOTICES.md` and the CHANGELOG. The notice is in THIRD_PARTY_NOTICES.md; the USAGE.md and CHANGELOG entries come with the release documentation pass.

**Notes:** Merged on 2026-10-09; see the target skill's body and, where noted in THIRD_PARTY_NOTICES.md, its `resources/` folder.

### 50. mcp-server-builder

- **Source:** `engineering/skills/mcp-server-builder/SKILL.md`
- **Category:** Engineering. **Simplicity** 7, **Value** 5, **Total** 12. **Recommendation:** Optional pack: engineering.
- **What it does:** Design and ship production-ready MCP (Model Context Protocol) servers from OpenAPI contracts instead of hand-written tool wrappers. Python and TypeScript support, schema validation, safe evolution. Use when exposing an existing API as an MCP server, building tool integrations for Claude or Codex or Cursor, or scaffolding an MCP project from scratch.
- **Overlap with mux:** mux mcp serve (consumer side only).
- **Fit in mux:** Offer it in the opt-in `engineering` pack as a hybrid skill whose commands run the bundled scripts. Nothing is seeded until a user installs the pack.
- **Integration cost:** 2 bundled scripts, standard-library Python; bundled `references/`.
- **Assessment:** Building MCP servers from OpenAPI is useful for mux users who extend mux with MCP.

- [x] Import `engineering/skills/mcp-server-builder/SKILL.md` into `packs/engineering/mcp-server-builder/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [~] Declare `requiresTools: [python3]` and confirm the 2 bundled scripts run from the skill folder.
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/engineering/mcp-server-builder` with category `engineering`. Partial: the bundled scripts are called from the body as `python3 "${SKILL_DIR}/scripts/..."` but were not executed during the import (the source is treated as untrusted), and `requiresTools: [python3]` is not declared because Windows installs usually name the interpreter `python`, which would hide the skill there. No `run_skill` command wrappers yet.

### 51. runbook-generator

- **Source:** `engineering/skills/runbook-generator/SKILL.md`
- **Category:** Engineering. **Simplicity** 7, **Value** 5, **Total** 12. **Recommendation:** Optional pack: engineering.
- **What it does:** Generate operational runbooks from a service name. deployment, incident response, maintenance, and rollback workflows. Templated structure customizable per environment. Use when documenting on-call procedures for a new service, standardizing incident response across teams, or producing runbooks before launching to production.
- **Overlap with mux:** none.
- **Fit in mux:** Offer it in the opt-in `engineering` pack as a hybrid skill whose commands run the bundled scripts. Nothing is seeded until a user installs the pack.
- **Integration cost:** 1 bundled script, standard-library Python; bundled `references/`.

- [x] Import `engineering/skills/runbook-generator/SKILL.md` into `packs/engineering/runbook-generator/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [~] Declare `requiresTools: [python3]` and confirm the 1 bundled script run from the skill folder.
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/engineering/runbook-generator` with category `devops`. Partial: the bundled scripts are called from the body as `python3 "${SKILL_DIR}/scripts/..."` but were not executed during the import (the source is treated as untrusted), and `requiresTools: [python3]` is not declared because Windows installs usually name the interpreter `python`, which would hide the skill there. No `run_skill` command wrappers yet.

### 52. senior-security

- **Source:** `engineering-team/skills/senior-security/SKILL.md`
- **Category:** Engineering. **Simplicity** 7, **Value** 5, **Total** 12. **Recommendation:** Adapt into security-review.
- **What it does:** Use when the user asks for STRIDE threat modeling, DREAD risk scoring, data-flow-diagram threat analysis, or a quick secret scan. or when a security request needs routing to the right specialist skill (pen-testing, incident response, cloud posture, red team, AI security, threat hunting, secure code review). This skill owns threat modeling; everything else routes to a sibling.
- **Overlap with mux:** security-review, git-secret-scan.
- **Fit in mux:** Do not ship it on its own. Its useful parts go into `security-review`.
- **Integration cost:** 2 bundled scripts, standard-library Python; bundled `references/`.
- **Assessment:** STRIDE and DREAD scoring would deepen security-review; the secret scan is already covered.

- [x] Read `engineering-team/skills/senior-security/SKILL.md` and list the procedures or checks that `security-review` does not already have.
- [x] Fold those into `security-review` (body, a new command, or an effort word), keeping mux's output format and exit codes.
- [x] Extend the tests that cover `security-review` with a case for each added check.
- [x] Note the borrowed ideas and the MIT source in `THIRD_PARTY_NOTICES.md` and the CHANGELOG. The notice is in THIRD_PARTY_NOTICES.md; the USAGE.md and CHANGELOG entries come with the release documentation pass.

**Notes:** Merged on 2026-10-09; see the target skill's body and, where noted in THIRD_PARTY_NOTICES.md, its `resources/` folder.

### 53. spec-driven-workflow

- **Source:** `engineering/skills/spec-driven-workflow/SKILL.md`
- **Category:** Engineering. **Simplicity** 7, **Value** 5, **Total** 12. **Recommendation:** Optional pack: engineering.
- **What it does:** Use when the user asks to write specs before code, define acceptance criteria, plan features before implementation, generate tests from specifications, or follow spec-first development practices.
- **Overlap with mux:** plan mode (partial).
- **Fit in mux:** Offer it in the opt-in `engineering` pack as a hybrid skill whose commands run the bundled scripts. Nothing is seeded until a user installs the pack.
- **Integration cost:** 3 bundled scripts, standard-library Python; bundled `references/`.
- **Assessment:** Plan mode covers the planning half; acceptance-criteria-to-tests is the useful remainder.

- [x] Import `engineering/skills/spec-driven-workflow/SKILL.md` into `packs/engineering/spec-driven-workflow/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [~] Declare `requiresTools: [python3]` and confirm the 3 bundled scripts run from the skill folder.
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/engineering/spec-driven-workflow` with category `engineering`. Partial: the bundled scripts are called from the body as `python3 "${SKILL_DIR}/scripts/..."` but were not executed during the import (the source is treated as untrusted), and `requiresTools: [python3]` is not declared because Windows installs usually name the interpreter `python`, which would hide the skill there. No `run_skill` command wrappers yet.

### 54. write-a-skill

- **Source:** `engineering/write-a-skill/skills/write-a-skill/SKILL.md`
- **Category:** Engineering. **Simplicity** 7, **Value** 5, **Total** 12. **Recommendation:** Adapt into new-skill.
- **What it does:** Create new agent skills with proper structure, progressive disclosure, and bundled resources. Use when user wants to create, write, build, or author a new skill.
- **Overlap with mux:** new-skill.
- **Fit in mux:** Do not ship it on its own. Its useful parts go into `new-skill`.
- **Integration cost:** 3 bundled scripts, standard-library Python; bundled `references/`.
- **Assessment:** Progressive-disclosure advice belongs in new-skill and SKILLS_AUTHORING.md.

- [x] Read `engineering/write-a-skill/skills/write-a-skill/SKILL.md` and list the procedures or checks that `new-skill` does not already have.
- [x] Fold those into `new-skill` (body, a new command, or an effort word), keeping mux's output format and exit codes.
- [x] Extend the tests that cover `new-skill` with a case for each added check.
- [x] Note the borrowed ideas and the MIT source in `THIRD_PARTY_NOTICES.md` and the CHANGELOG. The notice is in THIRD_PARTY_NOTICES.md; the USAGE.md and CHANGELOG entries come with the release documentation pass.

**Notes:** Merged on 2026-10-09; see the target skill's body and, where noted in THIRD_PARTY_NOTICES.md, its `resources/` folder.

### 55. email-template-builder

- **Source:** `engineering-team/skills/email-template-builder/SKILL.md`
- **Category:** Engineering. **Simplicity** 8, **Value** 4, **Total** 12. **Recommendation:** Optional pack: engineering.
- **What it does:** Build complete transactional email systems: React Email templates, provider integration (Resend, Postmark, SendGrid, AWS SES), preview server, i18n support, dark mode, spam optimization, analytics tracking. Use when adding transactional email to a new product, migrating between email providers, refactoring legacy email templates for accessibility, or adding internationalization to existing templat
- **Overlap with mux:** none.
- **Fit in mux:** Offer it in the opt-in `engineering` pack as a playbook (instructions only). Nothing is seeded until a user installs the pack.
- **Integration cost:** 439 lines, long for small models.

- [x] Import `engineering-team/skills/email-template-builder/SKILL.md` into `packs/engineering/email-template-builder/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/engineering/email-template-builder` with category `frontend`.

### 56. playwright-pro

- **Source:** `engineering-team/playwright-pro/skills/pw/SKILL.md`
- **Category:** Engineering. **Simplicity** 8, **Value** 4, **Total** 12. **Recommendation:** Adapt into the Playwright family.
- **What it does:** Production-grade Playwright testing toolkit. Use when the user mentions Playwright tests, end-to-end testing, browser automation, fixing flaky tests, test migration, CI/CD testing, or test suites. Generate tests, fix flaky failures, migrate from Cypress/Selenium, sync with TestRail, run on BrowserStack. 55 templates, 3 agents, smart reporting.
- **Overlap with mux:** none.
- **Fit in mux:** Do not ship it on its own. Its useful parts go into `the Playwright family`.
- **Integration cost:** references Claude Code files or conventions (claude.md) that must be rewritten for mux; bundled `reference/`, `templates/`.
- **Assessment:** Entry point for the Playwright sub-skills; its shared guidance becomes the family body.

- [x] Read `engineering-team/playwright-pro/skills/pw/SKILL.md` and list the procedures or checks that `the Playwright family` does not already have.
- [x] Fold those into `the Playwright family` (body, a new command, or an effort word), keeping mux's output format and exit codes.
- [x] Extend the tests that cover `the Playwright family` with a case for each added check.
- [x] Note the borrowed ideas and the MIT source in `THIRD_PARTY_NOTICES.md` and the CHANGELOG. The notice is in THIRD_PARTY_NOTICES.md; the USAGE.md and CHANGELOG entries come with the release documentation pass.

**Notes:** Merged on 2026-10-09; see the target skill's body and, where noted in THIRD_PARTY_NOTICES.md, its `resources/` folder.

### 57. stripe-integration-expert

- **Source:** `engineering-team/skills/stripe-integration-expert/SKILL.md`
- **Category:** Engineering. **Simplicity** 8, **Value** 4, **Total** 12. **Recommendation:** Optional pack: engineering.
- **What it does:** Production-grade Stripe integrations: subscriptions with trials and proration, one-time payments, usage-based billing, checkout sessions, idempotent webhook handlers, customer portal, and invoicing. Covers Next.js, Express, and Django patterns. Use when integrating Stripe for the first time, debugging webhook reliability issues, migrating from a different payment provider, or adding usage-based bi
- **Overlap with mux:** none.
- **Fit in mux:** Offer it in the opt-in `engineering` pack as a playbook (instructions only). Nothing is seeded until a user installs the pack.
- **Integration cost:** 476 lines, long for small models.

- [x] Import `engineering-team/skills/stripe-integration-expert/SKILL.md` into `packs/engineering/stripe-integration-expert/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/engineering/stripe-integration-expert` with category `engineering`.

### 58. cto-review

- **Source:** `c-level-agents/skills/cto-review/SKILL.md`
- **Category:** Executive advisory. **Simplicity** 9, **Value** 3, **Total** 12. **Recommendation:** Optional pack: business.
- **What it does:** Architecture and scaling interrogation. Tech debt, scaling cliffs, team scaling, build-vs-buy. Use when committing to an architecture, planning for 10x load, or weighing a rebuild against a vendor.
- **Overlap with mux:** none.
- **Fit in mux:** Offer it in the opt-in `business` pack as a playbook (instructions only). Nothing is seeded until a user installs the pack.
- **Integration cost:** a single SKILL.md with no bundled scripts.

- [x] Import `c-level-agents/skills/cto-review/SKILL.md` into `packs/business/cto-review/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/business/cto-review` with category `business`.

### 59. deepread

- **Source:** `research/deepread/SKILL.md`
- **Category:** Research. **Simplicity** 9, **Value** 3, **Total** 12. **Recommendation:** Optional pack: research.
- **What it does:** Use when the user asks to deeply read a book, article, PDF, or document set; extract claims and evidence; build a knowledge map; or learn through Feynman explanation and recall. Covers quick, deep, map, Feynman, and whole-book reading modes.
- **Overlap with mux:** none.
- **Fit in mux:** Offer it in the opt-in `research` pack as a playbook (instructions only). Nothing is seeded until a user installs the pack.
- **Integration cost:** a single SKILL.md with no bundled scripts; bundled `references/`.

- [x] Import `research/deepread/SKILL.md` into `packs/research/deepread/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/research/deepread` with category `research`.

### 60. embedded-iot-mentor

- **Source:** `engineering-team/skills/embedded-iot-mentor/SKILL.md`
- **Category:** Engineering. **Simplicity** 9, **Value** 3, **Total** 12. **Recommendation:** Optional pack: engineering.
- **What it does:** Mentor for embedded and IoT hardware projects. Helps select MCUs, dev boards, and toolchains, decides where sensor readings end up (phone, PC, dashboard, or alert), and gives time/cost estimates and a phased build plan from breadboard MVP to production PCB. Use when the user mentions embedded, IoT, microcontroller, ESP32, STM32, Arduino, Raspberry Pi Pico, firmware, PCB, KiCad, EasyEDA, PlatformIO
- **Overlap with mux:** none.
- **Fit in mux:** Offer it in the opt-in `engineering` pack as a playbook (instructions only). Nothing is seeded until a user installs the pack.
- **Integration cost:** a single SKILL.md with no bundled scripts; bundled `references/`.

- [x] Import `engineering-team/skills/embedded-iot-mentor/SKILL.md` into `packs/engineering/embedded-iot-mentor/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/engineering/embedded-iot-mentor` with category `engineering`.

### 61. team-communications

- **Source:** `project-management/skills/team-communications/SKILL.md`
- **Category:** Project management. **Simplicity** 9, **Value** 3, **Total** 12. **Recommendation:** Optional pack: product.
- **What it does:** Write internal company communications. 3P updates (Progress/Plans/Problems), company-wide newsletters, FAQ roundups, incident reports, leadership updates, status reports, project updates, and general internal comms. Use this skill any time the user asks to draft, edit, or format something meant for internal audiences. Trigger on keywords like "3P", "weekly update", "newsletter", "FAQ", "internal
- **Overlap with mux:** none.
- **Fit in mux:** Offer it in the opt-in `product` pack as a playbook (instructions only). Nothing is seeded until a user installs the pack.
- **Integration cost:** a single SKILL.md with no bundled scripts; bundled `references/`.

- [x] Import `project-management/skills/team-communications/SKILL.md` into `packs/product/team-communications/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/product/team-communications` with category `productivity`.

### 62. tdd-guide

- **Source:** `engineering-team/skills/tdd-guide/SKILL.md`
- **Category:** Engineering. **Simplicity** 5, **Value** 6, **Total** 11. **Recommendation:** Adopt as default.
- **What it does:** Test-driven development skill for writing unit tests, generating test fixtures and mocks, analyzing coverage gaps, and guiding red-green-refactor workflows across Jest, Pytest, JUnit, Vitest, and Mocha. Use when the user asks to write tests, improve test coverage, practice TDD, generate mocks or stubs, or mentions testing frameworks like Jest, pytest, or JUnit.
- **Overlap with mux:** test-gap-review (partial).
- **Fit in mux:** Ship with mux as a hybrid skill whose commands run the bundled scripts, listed only where it applies (the toolchain project files (`package.json`, `pyproject.toml`, `*.csproj`, `go.mod`, `Cargo.toml`, `pom.xml`)).
- **Integration cost:** 8 bundled scripts; 403 lines, long for small models; bundled `references/`, `assets/`.
- **Assessment:** Red-green-refactor guidance is a gap; drop the bundled code templates and keep the procedure.

- [x] Port `engineering-team/skills/tdd-guide/SKILL.md` into a default skill definition (a new `DefaultImportedSkills.cs`, or the family file named under Overlap), rewriting Claude Code references for mux and removing em-dashes.
- [~] Wrap the 8 Python scripts as `python` commands with `requiresTools: [python3]`, mapping their exit codes to mux's 0/1/2 convention and adding `MUX_SKILL_DRY_RUN` support; or port the logic to the shared `mux-skill.ps1` helper if it is small.
- [x] Gate the listing with `appliesTo`: the toolchain project files (`package.json`, `pyproject.toml`, `*.csproj`, `go.mod`, `Cargo.toml`, `pom.xml`).
- [x] Add positive and negative cases to a new `ImportedSkillsSuite` (loads, validates, gating, body substitution, and each command against a fixture project).
- [x] Record the source and the MIT notice in `THIRD_PARTY_NOTICES.md`; add the skill to USAGE.md and the CHANGELOG. The notice is in THIRD_PARTY_NOTICES.md; the USAGE.md and CHANGELOG entries come with the release documentation pass.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Bundled/tdd-guide` with category `testing`. Shipped as a bundled folder (SKILL.md plus its files) rather than a C# definition, so the source text and scripts travel unchanged apart from normalization. Partial: the bundled scripts are called from the body as `python3 "${SKILL_DIR}/scripts/..."` but were not executed during the import (the source is treated as untrusted), and `requiresTools: [python3]` is not declared because Windows installs usually name the interpreter `python`, which would hide the skill there. No `run_skill` command wrappers yet.

### 63. handoff (productivity)

- **Source:** `productivity/handoff/skills/handoff/SKILL.md`
- **Category:** Productivity. **Simplicity** 6, **Value** 5, **Total** 11. **Recommendation:** Adopt as default.
- **What it does:** Compact the current conversation into a handoff document for another agent to pick up. Save to a user-configured location (OS temp, home folder, or per-project .handoff/), redact secrets before write, suggest skills for the next session, and auto-load the latest handoff on the next SessionStart. First-run setup asks where to save so the project folder never gets cluttered. Use when the user says '
- **Overlap with mux:** none (sessions persist, but no handoff document).
- **Fit in mux:** Ship with mux as a hybrid skill whose commands run the bundled scripts, listed only where it applies (none (listed everywhere)).
- **Integration cost:** 7 bundled scripts, standard-library Python; bundled `references/`, `assets/`.
- **Assessment:** The fuller of the two handoff variants: configurable save location and secret redaction before writing. Ship this one.

- [x] Port `productivity/handoff/skills/handoff/SKILL.md` into a default skill definition (a new `DefaultImportedSkills.cs`, or the family file named under Overlap), rewriting Claude Code references for mux and removing em-dashes.
- [~] Wrap the 7 Python scripts as `python` commands with `requiresTools: [python3]`, mapping their exit codes to mux's 0/1/2 convention and adding `MUX_SKILL_DRY_RUN` support; or port the logic to the shared `mux-skill.ps1` helper if it is small.
- [x] Gate the listing with `appliesTo`: none (listed everywhere).
- [x] Add positive and negative cases to a new `ImportedSkillsSuite` (loads, validates, gating, body substitution, and each command against a fixture project).
- [x] Record the source and the MIT notice in `THIRD_PARTY_NOTICES.md`; add the skill to USAGE.md and the CHANGELOG. The notice is in THIRD_PARTY_NOTICES.md; the USAGE.md and CHANGELOG entries come with the release documentation pass.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Bundled/handoff` with category `productivity`. Shipped as a bundled folder (SKILL.md plus its files) rather than a C# definition, so the source text and scripts travel unchanged apart from normalization. Partial: the bundled scripts are called from the body as `python3 "${SKILL_DIR}/scripts/..."` but were not executed during the import (the source is treated as untrusted), and `requiresTools: [python3]` is not declared because Windows installs usually name the interpreter `python`, which would hide the skill there. No `run_skill` command wrappers yet.

### 64. incident-commander

- **Source:** `engineering-team/skills/incident-commander/SKILL.md`
- **Category:** Engineering. **Simplicity** 6, **Value** 5, **Total** 11. **Recommendation:** Optional pack: engineering.
- **What it does:** Comprehensive incident response framework from detection through resolution and post-incident review. Battle-tested SRE/DevOps practices: severity classification, timeline reconstruction, structured post-incident analysis. Use when declaring an incident, coordinating multi-team response during an outage, leading a post-mortem, or setting up on-call practices for a new service.
- **Overlap with mux:** none.
- **Fit in mux:** Offer it in the opt-in `engineering` pack as a hybrid skill whose commands run the bundled scripts. Nothing is seeded until a user installs the pack.
- **Integration cost:** 3 bundled scripts, standard-library Python; 471 lines, long for small models; bundled `references/`, `assets/`.

- [x] Import `engineering-team/skills/incident-commander/SKILL.md` into `packs/engineering/incident-commander/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [~] Declare `requiresTools: [python3]` and confirm the 3 bundled scripts run from the skill folder.
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/engineering/incident-commander` with category `devops`. Partial: the bundled scripts are called from the body as `python3 "${SKILL_DIR}/scripts/..."` but were not executed during the import (the source is treated as untrusted), and `requiresTools: [python3]` is not declared because Windows installs usually name the interpreter `python`, which would hide the skill there. No `run_skill` command wrappers yet.

### 65. monorepo-navigator

- **Source:** `engineering/skills/monorepo-navigator/SKILL.md`
- **Category:** Engineering. **Simplicity** 6, **Value** 5, **Total** 11. **Recommendation:** Adapt into project-detect.
- **What it does:** Navigate, manage, and optimize monorepos. Covers Turborepo, Nx, pnpm workspaces, and Lerna. Cross-package impact analysis, selective builds/tests on affected packages, remote caching, dependency graph visualization, and structured multi-repo to monorepo migrations. Use when setting up a new monorepo, optimizing CI for a large workspace, debugging cross-package dependency issues, or planning a mult
- **Overlap with mux:** project-detect (partial).
- **Fit in mux:** Do not ship it on its own. Its useful parts go into `project-detect`.
- **Integration cost:** 1 bundled script, standard-library Python; references Claude Code files or conventions (claude.md) that must be rewritten for mux; bundled `references/`.
- **Assessment:** Turborepo, Nx, and workspace detection would improve project-detect output.

- [x] Read `engineering/skills/monorepo-navigator/SKILL.md` and list the procedures or checks that `project-detect` does not already have.
- [x] Fold those into `project-detect` (body, a new command, or an effort word), keeping mux's output format and exit codes.
- [x] Extend the tests that cover `project-detect` with a case for each added check.
- [x] Note the borrowed ideas and the MIT source in `THIRD_PARTY_NOTICES.md` and the CHANGELOG. The notice is in THIRD_PARTY_NOTICES.md; the USAGE.md and CHANGELOG entries come with the release documentation pass.

**Notes:** Merged on 2026-10-09; see the target skill's body and, where noted in THIRD_PARTY_NOTICES.md, its `resources/` folder.

### 66. sql-database-assistant

- **Source:** `engineering/skills/sql-database-assistant/SKILL.md`
- **Category:** Engineering. **Simplicity** 6, **Value** 5, **Total** 11. **Recommendation:** Optional pack: engineering.
- **What it does:** Use when the user asks to write SQL queries, optimize database performance, generate migrations, explore database schemas, or work with ORMs like Prisma, Drizzle, TypeORM, or SQLAlchemy.
- **Overlap with mux:** none.
- **Fit in mux:** Offer it in the opt-in `engineering` pack as a hybrid skill whose commands run the bundled scripts. Nothing is seeded until a user installs the pack.
- **Integration cost:** 3 bundled scripts, standard-library Python; 457 lines, long for small models; bundled `references/`.
- **Assessment:** Database work is common but varied; offer as a pack rather than a default.

- [x] Import `engineering/skills/sql-database-assistant/SKILL.md` into `packs/engineering/sql-database-assistant/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [~] Declare `requiresTools: [python3]` and confirm the 3 bundled scripts run from the skill folder.
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/engineering/sql-database-assistant` with category `data`. Partial: the bundled scripts are called from the body as `python3 "${SKILL_DIR}/scripts/..."` but were not executed during the import (the source is treated as untrusted), and `requiresTools: [python3]` is not declared because Windows installs usually name the interpreter `python`, which would hide the skill there. No `run_skill` command wrappers yet.

### 67. agent-designer

- **Source:** `engineering/skills/agent-designer/SKILL.md`
- **Category:** Engineering. **Simplicity** 7, **Value** 4, **Total** 11. **Recommendation:** Optional pack: engineering.
- **What it does:** Use when the user asks to design a multi-agent system, pick an orchestration pattern (supervisor/swarm/pipeline), generate tool schemas for agents, or evaluate agent execution logs for cost, latency, and failure bottlenecks. Examples: 'design an agent architecture for research automation', 'generate Anthropic tool schemas from these tool descriptions', 'analyze these agent run logs for bottlenecks
- **Overlap with mux:** none.
- **Fit in mux:** Offer it in the opt-in `engineering` pack as a hybrid skill whose commands run the bundled scripts. Nothing is seeded until a user installs the pack.
- **Integration cost:** 3 bundled scripts, standard-library Python; bundled `references/`, `assets/`.
- **Assessment:** Relevant to people building LLM systems, which is a subset of mux users.

- [x] Import `engineering/skills/agent-designer/SKILL.md` into `packs/engineering/agent-designer/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [~] Declare `requiresTools: [python3]` and confirm the 3 bundled scripts run from the skill folder.
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/engineering/agent-designer` with category `workflow`. Partial: the bundled scripts are called from the body as `python3 "${SKILL_DIR}/scripts/..."` but were not executed during the import (the source is treated as untrusted), and `requiresTools: [python3]` is not declared because Windows installs usually name the interpreter `python`, which would hide the skill there. No `run_skill` command wrappers yet.

### 68. agent-harness

- **Source:** `engineering/agent-harness/skills/agent-harness/SKILL.md`
- **Category:** Engineering. **Simplicity** 7, **Value** 4, **Total** 11. **Recommendation:** Optional pack: engineering.
- **What it does:** Turn any domain folder of skills into a bounded agentic loop: compile a goal into a verifiable task plan, execute tasks with the domain's own tools, verify every task with machine-run checks, retry with caps, escalate to a human when budgets exhaust, and refuse to close until everything is verified or explicitly waived. Use when you want an agent or subagent to pick up a goal and drive it to a ver
- **Overlap with mux:** plan mode, /loop, task plans.
- **Fit in mux:** Offer it in the opt-in `engineering` pack as a hybrid skill whose commands run the bundled scripts. Nothing is seeded until a user installs the pack.
- **Integration cost:** 3 bundled scripts, standard-library Python; bundled `references/`, `assets/`.
- **Assessment:** Compiling a goal into a verified task loop overlaps mux plan mode and loops; worth a look for its verification step.

- [x] Import `engineering/agent-harness/skills/agent-harness/SKILL.md` into `packs/engineering/agent-harness/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [~] Declare `requiresTools: [python3]` and confirm the 3 bundled scripts run from the skill folder.
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/engineering/agent-harness` with category `workflow`. Partial: the bundled scripts are called from the body as `python3 "${SKILL_DIR}/scripts/..."` but were not executed during the import (the source is treated as untrusted), and `requiresTools: [python3]` is not declared because Windows installs usually name the interpreter `python`, which would hide the skill there. No `run_skill` command wrappers yet.

### 69. agent-workflow-designer

- **Source:** `engineering/skills/agent-workflow-designer/SKILL.md`
- **Category:** Engineering. **Simplicity** 7, **Value** 4, **Total** 11. **Recommendation:** Optional pack: engineering.
- **What it does:** Design production-grade multi-agent workflows with clear pattern choice (sequential, parallel, hierarchical), handoff contracts, failure handling, and cost/context controls. Use when architecting a multi-step agent pipeline, choosing between single-agent vs multi-agent approaches, or refactoring an LLM workflow that suffers from context bloat or unreliable handoffs.
- **Overlap with mux:** none.
- **Fit in mux:** Offer it in the opt-in `engineering` pack as a hybrid skill whose commands run the bundled scripts. Nothing is seeded until a user installs the pack.
- **Integration cost:** 1 bundled script, standard-library Python; bundled `references/`.
- **Assessment:** Relevant to people building LLM systems, which is a subset of mux users.

- [x] Import `engineering/skills/agent-workflow-designer/SKILL.md` into `packs/engineering/agent-workflow-designer/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [~] Declare `requiresTools: [python3]` and confirm the 1 bundled script run from the skill folder.
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/engineering/agent-workflow-designer` with category `workflow`. Partial: the bundled scripts are called from the body as `python3 "${SKILL_DIR}/scripts/..."` but were not executed during the import (the source is treated as untrusted), and `requiresTools: [python3]` is not declared because Windows installs usually name the interpreter `python`, which would hide the skill there. No `run_skill` command wrappers yet.

### 70. aws-solution-architect

- **Source:** `engineering-team/skills/aws-solution-architect/SKILL.md`
- **Category:** Engineering. **Simplicity** 7, **Value** 4, **Total** 11. **Recommendation:** Optional pack: engineering.
- **What it does:** Design AWS architectures for startups using serverless patterns and IaC templates. Use when asked to design serverless architecture, create CloudFormation templates, optimize AWS costs, set up CI/CD pipelines, or migrate to AWS. Covers Lambda, API Gateway, DynamoDB, ECS, Aurora, and cost optimization.
- **Overlap with mux:** aws-*, azure-*, gcp-* (operations only).
- **Fit in mux:** Offer it in the opt-in `engineering` pack as a hybrid skill whose commands run the bundled scripts. Nothing is seeded until a user installs the pack.
- **Integration cost:** 3 bundled scripts, standard-library Python; bundled `references/`, `assets/`.
- **Assessment:** Design guidance complements mux cloud skills, which operate rather than design; keep out of the default listing.

- [x] Import `engineering-team/skills/aws-solution-architect/SKILL.md` into `packs/engineering/aws-solution-architect/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [~] Declare `requiresTools: [python3]` and confirm the 3 bundled scripts run from the skill folder.
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/engineering/aws-solution-architect` with category `cloud`. Partial: the bundled scripts are called from the body as `python3 "${SKILL_DIR}/scripts/..."` but were not executed during the import (the source is treated as untrusted), and `requiresTools: [python3]` is not declared because Windows installs usually name the interpreter `python`, which would hide the skill there. No `run_skill` command wrappers yet.

### 71. chaos-engineering

- **Source:** `engineering/chaos-engineering/skills/chaos-engineering/SKILL.md` (also at `engineering/skills/chaos-engineering/SKILL.md`)
- **Category:** Engineering. **Simplicity** 7, **Value** 4, **Total** 11. **Recommendation:** Optional pack: engineering.
- **What it does:** Use when planning, running, or learning from chaos engineering experiments. Triggers on "chaos experiment", "fault injection", "gameday", "resilience test", "blast radius", "steady state", "abort criteria", "Chaos Toolkit", "Chaos Mesh", "Litmus", "Gremlin", "AWS FIS", or any deliberate failure-injection question. Ships experiment designer, blast-radius calculator, and postmortem generator (all st
- **Overlap with mux:** none.
- **Fit in mux:** Offer it in the opt-in `engineering` pack as a hybrid skill whose commands run the bundled scripts. Nothing is seeded until a user installs the pack.
- **Integration cost:** 3 bundled scripts, standard-library Python; bundled `references/`, `assets/`.

- [x] Import `engineering/chaos-engineering/skills/chaos-engineering/SKILL.md` into `packs/engineering/chaos-engineering/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [~] Declare `requiresTools: [python3]` and confirm the 3 bundled scripts run from the skill folder.
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/engineering/chaos-engineering` with category `devops`. Partial: the bundled scripts are called from the body as `python3 "${SKILL_DIR}/scripts/..."` but were not executed during the import (the source is treated as untrusted), and `requiresTools: [python3]` is not declared because Windows installs usually name the interpreter `python`, which would hide the skill there. No `run_skill` command wrappers yet.

### 72. env-secrets-manager

- **Source:** `engineering/skills/env-secrets-manager/SKILL.md`
- **Category:** Engineering. **Simplicity** 7, **Value** 4, **Total** 11. **Recommendation:** Optional pack: engineering.
- **What it does:** Manage environment-variable hygiene and secrets safety across local development and production. Practical auditing, drift awareness, rotation readiness. Use when auditing .env files for committed secrets, planning a credential rotation, debugging missing-env-var production incidents, or hardening a new project against secrets leakage.
- **Overlap with mux:** git-secret-scan, cloud secret listing.
- **Fit in mux:** Offer it in the opt-in `engineering` pack as a hybrid skill whose commands run the bundled scripts. Nothing is seeded until a user installs the pack.
- **Integration cost:** 1 bundled script, standard-library Python; bundled `references/`.
- **Assessment:** Hygiene guidance; mux never reads secret values, so keep any commands name-only.

- [x] Import `engineering/skills/env-secrets-manager/SKILL.md` into `packs/engineering/env-secrets-manager/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [~] Declare `requiresTools: [python3]` and confirm the 1 bundled script run from the skill folder.
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/engineering/env-secrets-manager` with category `security`. Partial: the bundled scripts are called from the body as `python3 "${SKILL_DIR}/scripts/..."` but were not executed during the import (the source is treated as untrusted), and `requiresTools: [python3]` is not declared because Windows installs usually name the interpreter `python`, which would hide the skill there. No `run_skill` command wrappers yet.

### 73. feature-flags-architect

- **Source:** `engineering/feature-flags-architect/skills/feature-flags-architect/SKILL.md` (also at `engineering/skills/feature-flags-architect/SKILL.md`)
- **Category:** Engineering. **Simplicity** 7, **Value** 4, **Total** 11. **Recommendation:** Optional pack: engineering.
- **What it does:** Use when adding, retiring, or auditing feature flags. Triggers on "add a flag", "ship behind a flag", "rollout plan", "kill switch", "stale flags", "flag debt", "LaunchDarkly", "GrowthBook", "Statsig", "Unleash", "Flipt", or any progressive-delivery question. Ships flag debt scanner, rollout planner, and kill-switch auditor (all stdlib Python), 4 references on flag taxonomy + provider trade-offs +
- **Overlap with mux:** none.
- **Fit in mux:** Offer it in the opt-in `engineering` pack as a hybrid skill whose commands run the bundled scripts. Nothing is seeded until a user installs the pack.
- **Integration cost:** 3 bundled scripts, standard-library Python; bundled `references/`, `assets/`.

- [x] Import `engineering/feature-flags-architect/skills/feature-flags-architect/SKILL.md` into `packs/engineering/feature-flags-architect/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [~] Declare `requiresTools: [python3]` and confirm the 3 bundled scripts run from the skill folder.
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/engineering/feature-flags-architect` with category `devops`. Partial: the bundled scripts are called from the body as `python3 "${SKILL_DIR}/scripts/..."` but were not executed during the import (the source is treated as untrusted), and `requiresTools: [python3]` is not declared because Windows installs usually name the interpreter `python`, which would hide the skill there. No `run_skill` command wrappers yet.

### 74. gdpr-dsgvo-expert

- **Source:** `ra-qm-team/skills/gdpr-dsgvo-expert/SKILL.md`
- **Category:** Regulatory and quality. **Simplicity** 7, **Value** 4, **Total** 11. **Recommendation:** Optional pack: compliance.
- **What it does:** GDPR and German DSGVO compliance automation. Scans codebases for privacy risks, generates DPIA documentation, tracks data subject rights requests with Art. 12(3) one-month deadlines. Use when running GDPR compliance assessments, privacy audits, data protection planning, DPIA generation, or data subject rights (DSAR) management (e.g., 'check this service for GDPR risks', 'track an access request de
- **Overlap with mux:** none.
- **Fit in mux:** Offer it in the opt-in `compliance` pack as a hybrid skill whose commands run the bundled scripts. Nothing is seeded until a user installs the pack.
- **Integration cost:** 3 bundled scripts, standard-library Python; bundled `references/`.
- **Assessment:** Scans codebases for privacy risks, which is closer to mux's job than the rest of the compliance bundle.

- [x] Import `ra-qm-team/skills/gdpr-dsgvo-expert/SKILL.md` into `packs/compliance/gdpr-dsgvo-expert/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [~] Declare `requiresTools: [python3]` and confirm the 3 bundled scripts run from the skill folder.
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/compliance/gdpr-dsgvo-expert` with category `compliance`. Partial: the bundled scripts are called from the body as `python3 "${SKILL_DIR}/scripts/..."` but were not executed during the import (the source is treated as untrusted), and `requiresTools: [python3]` is not declared because Windows installs usually name the interpreter `python`, which would hide the skill there. No `run_skill` command wrappers yet.

### 75. grill-me

- **Source:** `engineering/grill-me/skills/grill-me/SKILL.md`
- **Category:** Engineering. **Simplicity** 7, **Value** 4, **Total** 11. **Recommendation:** Optional pack: engineering.
- **What it does:** Interview the user relentlessly about a plan or design until reaching shared understanding, resolving each branch of the decision tree. Use when user wants to stress-test a plan, get grilled on their design, or mentions "grill me".
- **Overlap with mux:** plan mode, ask_user.
- **Fit in mux:** Offer it in the opt-in `engineering` pack as a hybrid skill whose commands run the bundled scripts. Nothing is seeded until a user installs the pack.
- **Integration cost:** 3 bundled scripts, standard-library Python; bundled `references/`.
- **Assessment:** mux plan mode and ask_user cover the interview mechanics; the docs-anchored variant adds ADR awareness.

- [x] Import `engineering/grill-me/skills/grill-me/SKILL.md` into `packs/engineering/grill-me/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [~] Declare `requiresTools: [python3]` and confirm the 3 bundled scripts run from the skill folder.
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/engineering/grill-me` with category `review`. Partial: the bundled scripts are called from the body as `python3 "${SKILL_DIR}/scripts/..."` but were not executed during the import (the source is treated as untrusted), and `requiresTools: [python3]` is not declared because Windows installs usually name the interpreter `python`, which would hide the skill there. No `run_skill` command wrappers yet.

### 76. grill-with-docs

- **Source:** `engineering/grill-with-docs/skills/grill-with-docs/SKILL.md`
- **Category:** Engineering. **Simplicity** 7, **Value** 4, **Total** 11. **Recommendation:** Optional pack: engineering.
- **What it does:** Docs-anchored grilling session. challenges a plan against the project's existing language (CONTEXT.md) and recorded decisions (docs/adr/), and updates those files inline as terminology and decisions crystallise. Use when user wants to stress-test a plan against documented domain language, or mentions "grill with docs".
- **Overlap with mux:** plan mode, ask_user.
- **Fit in mux:** Offer it in the opt-in `engineering` pack as a hybrid skill whose commands run the bundled scripts. Nothing is seeded until a user installs the pack.
- **Integration cost:** 3 bundled scripts, standard-library Python; bundled `references/`.
- **Assessment:** mux plan mode and ask_user cover the interview mechanics; the docs-anchored variant adds ADR awareness.

- [x] Import `engineering/grill-with-docs/skills/grill-with-docs/SKILL.md` into `packs/engineering/grill-with-docs/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [~] Declare `requiresTools: [python3]` and confirm the 3 bundled scripts run from the skill folder.
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/engineering/grill-with-docs` with category `review`. Partial: the bundled scripts are called from the body as `python3 "${SKILL_DIR}/scripts/..."` but were not executed during the import (the source is treated as untrusted), and `requiresTools: [python3]` is not declared because Windows installs usually name the interpreter `python`, which would hide the skill there. No `run_skill` command wrappers yet.

### 77. incident-response

- **Source:** `engineering-team/skills/incident-response/SKILL.md`
- **Category:** Engineering. **Simplicity** 7, **Value** 4, **Total** 11. **Recommendation:** Optional pack: security.
- **What it does:** Use when a security incident has been detected or declared and needs classification, triage, escalation path determination, and forensic evidence collection. Covers SEV1-SEV4 classification, false positive filtering, incident taxonomy, and NIST SP 800-61 lifecycle.
- **Overlap with mux:** none.
- **Fit in mux:** Offer it in the opt-in `security` pack as a hybrid skill whose commands run the bundled scripts. Nothing is seeded until a user installs the pack.
- **Integration cost:** 1 bundled script, standard-library Python; bundled `references/`.
- **Assessment:** Dual-use; document that it is for authorized engagements only, and keep it out of the default listing.

- [x] Import `engineering-team/skills/incident-response/SKILL.md` into `packs/security/incident-response/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [~] Declare `requiresTools: [python3]` and confirm the 1 bundled script run from the skill folder.
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/security/incident-response` with category `security`. Partial: the bundled scripts are called from the body as `python3 "${SKILL_DIR}/scripts/..."` but were not executed during the import (the source is treated as untrusted), and `requiresTools: [python3]` is not declared because Windows installs usually name the interpreter `python`, which would hide the skill there. No `run_skill` command wrappers yet.

### 78. jira-expert

- **Source:** `project-management/skills/jira-expert/SKILL.md`
- **Category:** Project management. **Simplicity** 7, **Value** 4, **Total** 11. **Recommendation:** Optional pack: product.
- **What it does:** Atlassian Jira expert for creating and managing projects, planning, product discovery, JQL queries, workflows, custom fields, automation, reporting, and all Jira features. Use when setting up or configuring Jira projects, writing JQL and advanced searches, creating dashboards, designing workflows, or performing technical Jira operations.
- **Overlap with mux:** none.
- **Fit in mux:** Offer it in the opt-in `product` pack as a hybrid skill whose commands run the bundled scripts. Nothing is seeded until a user installs the pack.
- **Integration cost:** 2 bundled scripts, standard-library Python; bundled `references/`.
- **Assessment:** Developers live in Jira; useful when paired with a Jira MCP server.

- [x] Import `project-management/skills/jira-expert/SKILL.md` into `packs/product/jira-expert/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [~] Declare `requiresTools: [python3]` and confirm the 2 bundled scripts run from the skill folder.
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/product/jira-expert` with category `productivity`. Partial: the bundled scripts are called from the body as `python3 "${SKILL_DIR}/scripts/..."` but were not executed during the import (the source is treated as untrusted), and `requiresTools: [python3]` is not declared because Windows installs usually name the interpreter `python`, which would hide the skill there. No `run_skill` command wrappers yet.

### 79. kubernetes-operator

- **Source:** `engineering/kubernetes-operator/skills/kubernetes-operator/SKILL.md` (also at `engineering/skills/kubernetes-operator/SKILL.md`)
- **Category:** Engineering. **Simplicity** 7, **Value** 4, **Total** 11. **Recommendation:** Optional pack: engineering.
- **What it does:** Use when building a Kubernetes Operator. custom controllers that reconcile CRD state. Triggers on "build an operator", "CRD design", "reconcile loop", "controller-runtime", "kubebuilder", "operator-sdk", "metacontroller", "KOPF", "operator capability levels", or "custom resource". Ships CRD validator, reconcile-loop linter, and OperatorHub capability auditor (all stdlib Python), 4 references on t
- **Overlap with mux:** k8s-* (operations only).
- **Fit in mux:** Offer it in the opt-in `engineering` pack as a hybrid skill whose commands run the bundled scripts. Nothing is seeded until a user installs the pack.
- **Integration cost:** 3 bundled scripts, standard-library Python; bundled `references/`, `assets/`.

- [x] Import `engineering/kubernetes-operator/skills/kubernetes-operator/SKILL.md` into `packs/engineering/kubernetes-operator/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [~] Declare `requiresTools: [python3]` and confirm the 3 bundled scripts run from the skill folder.
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/engineering/kubernetes-operator` with category `devops`. Partial: the bundled scripts are called from the body as `python3 "${SKILL_DIR}/scripts/..."` but were not executed during the import (the source is treated as untrusted), and `requiresTools: [python3]` is not declared because Windows installs usually name the interpreter `python`, which would hide the skill there. No `run_skill` command wrappers yet.

### 80. md-review

- **Source:** `markdown-html/skills/md-review/SKILL.md`
- **Category:** Documents. **Simplicity** 7, **Value** 4, **Total** 11. **Recommendation:** Optional pack: docs.
- **What it does:** Converts a markdown PR writeup or code review (one with ```diff fenced blocks and severity-tagged > [!BLOCKER]/[!MAJOR]/[!MINOR]/[!NIT] callouts) into a single-file 2-column HTML review. unified-diff on the left, severity-tagged annotation cards on the right, top jump-nav listing every finding, mandatory named reviewer footer. Triggers when the markdown-html-orchestrator classifies an input as RE
- **Overlap with mux:** code-review output (Markdown only).
- **Fit in mux:** Offer it in the opt-in `docs` pack as a hybrid skill whose commands run the bundled scripts. Nothing is seeded until a user installs the pack.
- **Integration cost:** 3 bundled scripts, standard-library Python; bundled `references/`, `assets/`.
- **Assessment:** Rendering review findings as a single-file HTML page would pair well with code-review output.

- [x] Import `markdown-html/skills/md-review/SKILL.md` into `packs/docs/md-review/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [~] Declare `requiresTools: [python3]` and confirm the 3 bundled scripts run from the skill folder.
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/docs/md-review` with category `docs`. Partial: the bundled scripts are called from the body as `python3 "${SKILL_DIR}/scripts/..."` but were not executed during the import (the source is treated as untrusted), and `requiresTools: [python3]` is not declared because Windows installs usually name the interpreter `python`, which would hide the skill there. No `run_skill` command wrappers yet.

### 81. observability-designer

- **Source:** `engineering/skills/observability-designer/SKILL.md`
- **Category:** Engineering. **Simplicity** 7, **Value** 4, **Total** 11. **Recommendation:** Optional pack: engineering.
- **What it does:** Design production-ready observability strategies combining metrics, logs, and traces. Includes SLI/SLO design, golden-signals monitoring, alert optimization. Use when adding observability to a new service, refactoring alerting that is too noisy, or designing an SLO program before scaling production load.
- **Overlap with mux:** none.
- **Fit in mux:** Offer it in the opt-in `engineering` pack as a hybrid skill whose commands run the bundled scripts. Nothing is seeded until a user installs the pack.
- **Integration cost:** 3 bundled scripts, standard-library Python; bundled `references/`, `assets/`.

- [x] Import `engineering/skills/observability-designer/SKILL.md` into `packs/engineering/observability-designer/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [~] Declare `requiresTools: [python3]` and confirm the 3 bundled scripts run from the skill folder.
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/engineering/observability-designer` with category `devops`. Partial: the bundled scripts are called from the body as `python3 "${SKILL_DIR}/scripts/..."` but were not executed during the import (the source is treated as untrusted), and `requiresTools: [python3]` is not declared because Windows installs usually name the interpreter `python`, which would hide the skill there. No `run_skill` command wrappers yet.

### 82. rag-architect

- **Source:** `engineering/skills/rag-architect/SKILL.md`
- **Category:** Engineering. **Simplicity** 7, **Value** 4, **Total** 11. **Recommendation:** Optional pack: engineering.
- **What it does:** Use when the user asks to design a RAG pipeline, choose a chunking strategy or embedding model, pick a vector database, or evaluate retrieval quality (precision@k, recall@k, NDCG). Examples: 'design a RAG system for our docs', 'what chunk size should I use for this corpus', 'evaluate my retriever against ground truth'. NOT for general LLM cost tuning (use llm-cost-optimizer) or agent loops over re
- **Overlap with mux:** none.
- **Fit in mux:** Offer it in the opt-in `engineering` pack as a hybrid skill whose commands run the bundled scripts. Nothing is seeded until a user installs the pack.
- **Integration cost:** 3 bundled scripts, standard-library Python; bundled `references/`.
- **Assessment:** Relevant to people building LLM systems, which is a subset of mux users.

- [x] Import `engineering/skills/rag-architect/SKILL.md` into `packs/engineering/rag-architect/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [~] Declare `requiresTools: [python3]` and confirm the 3 bundled scripts run from the skill folder.
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/engineering/rag-architect` with category `data`. Partial: the bundled scripts are called from the body as `python3 "${SKILL_DIR}/scripts/..."` but were not executed during the import (the source is treated as untrusted), and `requiresTools: [python3]` is not declared because Windows installs usually name the interpreter `python`, which would hide the skill there. No `run_skill` command wrappers yet.

### 83. red-team

- **Source:** `engineering-team/skills/red-team/SKILL.md`
- **Category:** Engineering. **Simplicity** 7, **Value** 4, **Total** 11. **Recommendation:** Optional pack: security.
- **What it does:** Use when planning or executing authorized red team engagements, attack path analysis, or offensive security simulations. Covers MITRE ATT&CK kill-chain planning, technique scoring, choke point identification, OPSEC risk assessment, and crown jewel targeting.
- **Overlap with mux:** none.
- **Fit in mux:** Offer it in the opt-in `security` pack as a hybrid skill whose commands run the bundled scripts. Nothing is seeded until a user installs the pack.
- **Integration cost:** 1 bundled script, standard-library Python; bundled `references/`.
- **Assessment:** Dual-use; document that it is for authorized engagements only, and keep it out of the default listing.

- [x] Import `engineering-team/skills/red-team/SKILL.md` into `packs/security/red-team/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [~] Declare `requiresTools: [python3]` and confirm the 1 bundled script run from the skill folder.
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/security/red-team` with category `security`. Partial: the bundled scripts are called from the body as `python3 "${SKILL_DIR}/scripts/..."` but were not executed during the import (the source is treated as untrusted), and `requiresTools: [python3]` is not declared because Windows installs usually name the interpreter `python`, which would hide the skill there. No `run_skill` command wrappers yet.

### 84. reflect

- **Source:** `productivity/reflect/skills/reflect/SKILL.md`
- **Category:** Productivity. **Simplicity** 7, **Value** 4, **Total** 11. **Recommendation:** Optional pack: productivity.
- **What it does:** Mid-conversation reflection skill that pauses execution and zooms out from detail-mode to honestly reassess direction, assumptions, and bias. Use when the user says 'reflect', 'take a step back', 'step back', 'zoom out', 'are we missing something', 'bigger picture', 'sanity check this', 'are we on track', 'are we overthinking this', 'forest for the trees', or any variation signaling intent to brea
- **Overlap with mux:** none.
- **Fit in mux:** Offer it in the opt-in `productivity` pack as a hybrid skill whose commands run the bundled scripts. Nothing is seeded until a user installs the pack.
- **Integration cost:** 3 bundled scripts, standard-library Python; bundled `references/`.
- **Assessment:** A mid-task pause to re-check direction and assumptions is relevant to long agent runs.

- [x] Import `productivity/reflect/skills/reflect/SKILL.md` into `packs/productivity/reflect/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [~] Declare `requiresTools: [python3]` and confirm the 3 bundled scripts run from the skill folder.
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/productivity/reflect` with category `productivity`. Partial: the bundled scripts are called from the body as `python3 "${SKILL_DIR}/scripts/..."` but were not executed during the import (the source is treated as untrusted), and `requiresTools: [python3]` is not declared because Windows installs usually name the interpreter `python`, which would hide the skill there. No `run_skill` command wrappers yet.

### 85. security-pen-testing

- **Source:** `engineering-team/skills/security-pen-testing/SKILL.md`
- **Category:** Engineering. **Simplicity** 7, **Value** 4, **Total** 11. **Recommendation:** Optional pack: security.
- **What it does:** Use when the user asks to perform security audits, penetration testing, vulnerability scanning, OWASP Top 10 checks, or offensive security assessments. Covers static analysis, dependency scanning, secret detection, API security testing, and pen test report generation.
- **Overlap with mux:** none.
- **Fit in mux:** Offer it in the opt-in `security` pack as a hybrid skill whose commands run the bundled scripts. Nothing is seeded until a user installs the pack.
- **Integration cost:** 3 bundled scripts, standard-library Python; bundled `references/`.
- **Assessment:** Dual-use; document that it is for authorized engagements only, and keep it out of the default listing.

- [x] Import `engineering-team/skills/security-pen-testing/SKILL.md` into `packs/security/security-pen-testing/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [~] Declare `requiresTools: [python3]` and confirm the 3 bundled scripts run from the skill folder.
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/security/security-pen-testing` with category `security`. Partial: the bundled scripts are called from the body as `python3 "${SKILL_DIR}/scripts/..."` but were not executed during the import (the source is treated as untrusted), and `requiresTools: [python3]` is not declared because Windows installs usually name the interpreter `python`, which would hide the skill there. No `run_skill` command wrappers yet.

### 86. senior-architect

- **Source:** `engineering-team/skills/senior-architect/SKILL.md`
- **Category:** Engineering. **Simplicity** 7, **Value** 4, **Total** 11. **Recommendation:** Optional pack: engineering.
- **What it does:** This skill should be used when the user asks to "design system architecture", "evaluate microservices vs monolith", "create architecture diagrams", "analyze dependencies", "choose a database", "plan for scalability", "make technical decisions", or "review system design". Use for architecture decision records (ADRs), tech stack evaluation, system design reviews, dependency analysis, and generating
- **Overlap with mux:** none.
- **Fit in mux:** Offer it in the opt-in `engineering` pack as a hybrid skill whose commands run the bundled scripts. Nothing is seeded until a user installs the pack.
- **Integration cost:** 3 bundled scripts, standard-library Python; bundled `references/`.
- **Assessment:** Broad role playbooks; mux toolchain skills already cover the deterministic parts, so these mostly add prose.

- [x] Import `engineering-team/skills/senior-architect/SKILL.md` into `packs/engineering/senior-architect/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [~] Declare `requiresTools: [python3]` and confirm the 3 bundled scripts run from the skill folder.
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/engineering/senior-architect` with category `engineering`. Partial: the bundled scripts are called from the body as `python3 "${SKILL_DIR}/scripts/..."` but were not executed during the import (the source is treated as untrusted), and `requiresTools: [python3]` is not declared because Windows installs usually name the interpreter `python`, which would hide the skill there. No `run_skill` command wrappers yet.

### 87. senior-devops

- **Source:** `engineering-team/skills/senior-devops/SKILL.md`
- **Category:** Engineering. **Simplicity** 7, **Value** 4, **Total** 11. **Recommendation:** Optional pack: engineering.
- **What it does:** Comprehensive DevOps skill for CI/CD, infrastructure automation, containerization, and cloud platforms (AWS, GCP, Azure). Includes pipeline setup, infrastructure as code, deployment automation, and monitoring. Use when setting up pipelines, deploying applications, managing infrastructure, implementing monitoring, or optimizing deployment processes.
- **Overlap with mux:** none.
- **Fit in mux:** Offer it in the opt-in `engineering` pack as a hybrid skill whose commands run the bundled scripts. Nothing is seeded until a user installs the pack.
- **Integration cost:** 3 bundled scripts, standard-library Python; bundled `references/`.
- **Assessment:** Broad role playbooks; mux toolchain skills already cover the deterministic parts, so these mostly add prose.

- [x] Import `engineering-team/skills/senior-devops/SKILL.md` into `packs/engineering/senior-devops/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [~] Declare `requiresTools: [python3]` and confirm the 3 bundled scripts run from the skill folder.
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/engineering/senior-devops` with category `devops`. Partial: the bundled scripts are called from the body as `python3 "${SKILL_DIR}/scripts/..."` but were not executed during the import (the source is treated as untrusted), and `requiresTools: [python3]` is not declared because Windows installs usually name the interpreter `python`, which would hide the skill there. No `run_skill` command wrappers yet.

### 88. senior-qa

- **Source:** `engineering-team/skills/senior-qa/SKILL.md`
- **Category:** Engineering. **Simplicity** 7, **Value** 4, **Total** 11. **Recommendation:** Optional pack: engineering.
- **What it does:** Generates unit tests, integration tests, and E2E tests for React/Next.js applications. Scans components to create Jest + React Testing Library test stubs, analyzes Istanbul/LCOV coverage reports to surface gaps, scaffolds Playwright test files from Next.js routes, mocks API calls with MSW, creates test fixtures, and configures test runners. Use when the user asks to "generate tests", "write unit t
- **Overlap with mux:** none.
- **Fit in mux:** Offer it in the opt-in `engineering` pack as a hybrid skill whose commands run the bundled scripts. Nothing is seeded until a user installs the pack.
- **Integration cost:** 3 bundled scripts, standard-library Python; bundled `references/`.
- **Assessment:** Broad role playbooks; mux toolchain skills already cover the deterministic parts, so these mostly add prose.

- [x] Import `engineering-team/skills/senior-qa/SKILL.md` into `packs/engineering/senior-qa/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [~] Declare `requiresTools: [python3]` and confirm the 3 bundled scripts run from the skill folder.
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/engineering/senior-qa` with category `testing`. Partial: the bundled scripts are called from the body as `python3 "${SKILL_DIR}/scripts/..."` but were not executed during the import (the source is treated as untrusted), and `requiresTools: [python3]` is not declared because Windows installs usually name the interpreter `python`, which would hide the skill there. No `run_skill` command wrappers yet.

### 89. slo-architect

- **Source:** `engineering/skills/slo-architect/SKILL.md` (also at `engineering/slo-architect/skills/slo-architect/SKILL.md`)
- **Category:** Engineering. **Simplicity** 7, **Value** 4, **Total** 11. **Recommendation:** Optional pack: engineering.
- **What it does:** Use when defining, reviewing, or operating SLOs/SLIs/error budgets. Triggers on "define an SLO", "what should our SLO be", "error budget", "burn rate", "SLI", "service level objective", "Google SRE workbook", "multi-window burn-rate alert", or any reliability-target question. Ships SLO designer, error-budget calculator with multi-window burn-rate thresholds, and SLO reviewer that catches the commo
- **Overlap with mux:** none.
- **Fit in mux:** Offer it in the opt-in `engineering` pack as a hybrid skill whose commands run the bundled scripts. Nothing is seeded until a user installs the pack.
- **Integration cost:** 3 bundled scripts, standard-library Python; bundled `references/`, `assets/`.

- [x] Import `engineering/skills/slo-architect/SKILL.md` into `packs/engineering/slo-architect/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [~] Declare `requiresTools: [python3]` and confirm the 3 bundled scripts run from the skill folder.
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/engineering/slo-architect` with category `devops`. Partial: the bundled scripts are called from the body as `python3 "${SKILL_DIR}/scripts/..."` but were not executed during the import (the source is treated as untrusted), and `requiresTools: [python3]` is not declared because Windows installs usually name the interpreter `python`, which would hide the skill there. No `run_skill` command wrappers yet.

### 90. spec-to-repo

- **Source:** `product-team/skills/spec-to-repo/SKILL.md`
- **Category:** Product. **Simplicity** 7, **Value** 4, **Total** 11. **Recommendation:** Optional pack: product.
- **What it does:** Use when the user says 'build me an app', 'create a project from this spec', 'scaffold a new repo', 'generate a starter', 'turn this idea into code', 'bootstrap a project', 'I have requirements and need a codebase', or provides a natural-language project specification and expects a complete, runnable repository. Stack-agnostic: Next.js, FastAPI, Rails, Go, Rust, Flutter, and more.
- **Overlap with mux:** none.
- **Fit in mux:** Offer it in the opt-in `product` pack as a hybrid skill whose commands run the bundled scripts. Nothing is seeded until a user installs the pack.
- **Integration cost:** 1 bundled script, standard-library Python; bundled `references/`.

- [x] Import `product-team/skills/spec-to-repo/SKILL.md` into `packs/product/spec-to-repo/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [~] Declare `requiresTools: [python3]` and confirm the 1 bundled script run from the skill folder.
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/product/spec-to-repo` with category `product`. Partial: the bundled scripts are called from the body as `python3 "${SKILL_DIR}/scripts/..."` but were not executed during the import (the source is treated as untrusted), and `requiresTools: [python3]` is not declared because Windows installs usually name the interpreter `python`, which would hide the skill there. No `run_skill` command wrappers yet.

### 91. threat-detection

- **Source:** `engineering-team/skills/threat-detection/SKILL.md`
- **Category:** Engineering. **Simplicity** 7, **Value** 4, **Total** 11. **Recommendation:** Optional pack: security.
- **What it does:** Use when hunting for threats in an environment, analyzing IOCs, or detecting behavioral anomalies in telemetry. Covers hypothesis-driven threat hunting, IOC sweep generation, z-score anomaly detection, and MITRE ATT&CK-mapped signal prioritization.
- **Overlap with mux:** none.
- **Fit in mux:** Offer it in the opt-in `security` pack as a hybrid skill whose commands run the bundled scripts. Nothing is seeded until a user installs the pack.
- **Integration cost:** 1 bundled script, standard-library Python; bundled `references/`.
- **Assessment:** Dual-use; document that it is for authorized engagements only, and keep it out of the default listing.

- [x] Import `engineering-team/skills/threat-detection/SKILL.md` into `packs/security/threat-detection/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [~] Declare `requiresTools: [python3]` and confirm the 1 bundled script run from the skill folder.
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/security/threat-detection` with category `security`. Partial: the bundled scripts are called from the body as `python3 "${SKILL_DIR}/scripts/..."` but were not executed during the import (the source is treated as untrusted), and `requiresTools: [python3]` is not declared because Windows installs usually name the interpreter `python`, which would hide the skill there. No `run_skill` command wrappers yet.

### 92. browserstack

- **Source:** `engineering-team/playwright-pro/skills/browserstack/SKILL.md`
- **Category:** Engineering. **Simplicity** 8, **Value** 3, **Total** 11. **Recommendation:** Optional pack: engineering.
- **What it does:** Run tests on BrowserStack. Use when user mentions "browserstack", "cross-browser", "cloud testing", "browser matrix", "test on safari", "test on firefox", or "browser compatibility".
- **Overlap with mux:** none.
- **Fit in mux:** Offer it in the opt-in `engineering` pack as a playbook (instructions only). Nothing is seeded until a user installs the pack.
- **Integration cost:** references Claude Code files or conventions (claude.md) that must be rewritten for mux.
- **Assessment:** Needs a paid third-party service account.

- [x] Import `engineering-team/playwright-pro/skills/browserstack/SKILL.md` into `packs/engineering/browserstack/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/engineering/browserstack` with category `testing`.

### 93. named-persona-adversarial-review

- **Source:** `engineering-team/skills/named-persona-adversarial-review/SKILL.md`
- **Category:** Engineering. **Simplicity** 8, **Value** 3, **Total** 11. **Recommendation:** Optional pack: engineering.
- **What it does:** Code review through the lens of real engineers' documented philosophies (Torvalds, Thompson, Carmack, Kent Beck, Jobs, Cagan). Complements abstract-role adversarial review with named, sourced perspectives. Use when automated review findings feel generic, when a PR has architectural or UX impact, or when the author wants pre-submit hardening beyond standard checks.
- **Overlap with mux:** code-review (partial).
- **Fit in mux:** Offer it in the opt-in `engineering` pack as a playbook (instructions only). Nothing is seeded until a user installs the pack.
- **Integration cost:** references Claude Code files or conventions (.claude/) that must be rewritten for mux; bundled `references/`.
- **Assessment:** Novelty value; personas of real people are a style choice, not a default.

- [x] Import `engineering-team/skills/named-persona-adversarial-review/SKILL.md` into `packs/engineering/named-persona-adversarial-review/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/engineering/named-persona-adversarial-review` with category `review`.

### 94. testrail

- **Source:** `engineering-team/playwright-pro/skills/testrail/SKILL.md`
- **Category:** Engineering. **Simplicity** 8, **Value** 3, **Total** 11. **Recommendation:** Optional pack: engineering.
- **What it does:** Sync tests with TestRail. Use when user mentions "testrail", "test management", "test cases", "test run", "sync test cases", "push results to testrail", or "import from testrail".
- **Overlap with mux:** none.
- **Fit in mux:** Offer it in the opt-in `engineering` pack as a playbook (instructions only). Nothing is seeded until a user installs the pack.
- **Integration cost:** references Claude Code files or conventions (claude.md) that must be rewritten for mux.
- **Assessment:** Needs a paid third-party service account.

- [x] Import `engineering-team/playwright-pro/skills/testrail/SKILL.md` into `packs/engineering/testrail/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/engineering/testrail` with category `testing`.

### 95. board-deck-builder

- **Source:** `c-level-advisor/skills/board-deck-builder/SKILL.md`
- **Category:** Executive advisory. **Simplicity** 9, **Value** 2, **Total** 11. **Recommendation:** Optional pack: business.
- **What it does:** Assembles comprehensive board and investor update decks by pulling perspectives from all C-suite roles. Use when preparing board meetings, investor updates, quarterly business reviews, or fundraising narratives. Covers structure, narrative framework, bad news delivery, and common mistakes.
- **Overlap with mux:** none.
- **Fit in mux:** Offer it in the opt-in `business` pack as a playbook (instructions only). Nothing is seeded until a user installs the pack.
- **Integration cost:** a single SKILL.md with no bundled scripts; bundled `references/`, `templates/`.

- [x] Import `c-level-advisor/skills/board-deck-builder/SKILL.md` into `packs/business/board-deck-builder/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/business/board-deck-builder` with category `business`.

### 96. board-prep

- **Source:** `c-level-advisor/executive-mentor/skills/board-prep/SKILL.md`
- **Category:** Executive advisory. **Simplicity** 9, **Value** 2, **Total** 11. **Recommendation:** Optional pack: business.
- **What it does:** Board meeting preparation for the adversarial scenario, not the friendly one. Forces numbers-cold mastery, anticipates hard questions, builds a narrative that acknowledges weakness without losing the room. Use when preparing for a board meeting, an investor update, fundraising presentation, or any high-stakes adversarial review where every number must live in your head not just on a slide.
- **Overlap with mux:** none.
- **Fit in mux:** Offer it in the opt-in `business` pack as a playbook (instructions only). Nothing is seeded until a user installs the pack.
- **Integration cost:** a single SKILL.md with no bundled scripts.

- [x] Import `c-level-advisor/executive-mentor/skills/board-prep/SKILL.md` into `packs/business/board-prep/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/business/board-prep` with category `business`.

### 97. business-investment-advisor

- **Source:** `finance/business-investment-advisor/skills/business-investment-advisor/SKILL.md`
- **Category:** Finance. **Simplicity** 9, **Value** 2, **Total** 11. **Recommendation:** Optional pack: business.
- **What it does:** Business investment analysis and capital allocation advisor. Use when evaluating whether to invest in equipment, real estate, a new business, hiring, technology, or any capital expenditure. Also use for ROI calculations, IRR, NPV, payback period, build vs buy decisions, lease vs buy analysis, vendor evaluation, or deciding where to allocate limited budget for maximum return.
- **Overlap with mux:** none.
- **Fit in mux:** Offer it in the opt-in `business` pack as a playbook (instructions only). Nothing is seeded until a user installs the pack.
- **Integration cost:** a single SKILL.md with no bundled scripts.

- [x] Import `finance/business-investment-advisor/skills/business-investment-advisor/SKILL.md` into `packs/business/business-investment-advisor/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/business/business-investment-advisor` with category `business`.

### 98. business-name-fit

- **Source:** `marketing-skill/skills/business-name-fit/SKILL.md`
- **Category:** Marketing. **Simplicity** 9, **Value** 2, **Total** 11. **Recommendation:** Optional pack: marketing.
- **What it does:** Suggest, pick, or vet a business, startup, or product name that stays true to the founder's cultural origin while working professionally in the markets they want to sell into. Use when someone is naming a company, brand, or product and cares about how it lands across languages and regions. for example a name that sounds right at home but might read oddly to English speakers, or an authentic name
- **Overlap with mux:** none.
- **Fit in mux:** Offer it in the opt-in `marketing` pack as a playbook (instructions only). Nothing is seeded until a user installs the pack.
- **Integration cost:** a single SKILL.md with no bundled scripts; bundled `references/`.

- [x] Import `marketing-skill/skills/business-name-fit/SKILL.md` into `packs/marketing/business-name-fit/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/marketing/business-name-fit` with category `marketing`.

### 99. caio-review

- **Source:** `c-level-agents/skills/caio-review/SKILL.md`
- **Category:** Executive advisory. **Simplicity** 9, **Value** 2, **Total** 11. **Recommendation:** Optional pack: business.
- **What it does:** Eval-demanding Chief AI Officer interrogation of any plan that involves AI: model selection, risk classification, cost economics, or AI hiring. Use when shipping an AI feature without an eval set, choosing between API, fine-tune, and self-hosted, or classifying a use case under the EU AI Act.
- **Overlap with mux:** none.
- **Fit in mux:** Offer it in the opt-in `business` pack as a playbook (instructions only). Nothing is seeded until a user installs the pack.
- **Integration cost:** a single SKILL.md with no bundled scripts.

- [x] Import `c-level-agents/skills/caio-review/SKILL.md` into `packs/business/caio-review/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/business/caio-review` with category `business`.

### 100. cco-review

- **Source:** `c-level-agents/skills/cco-review/SKILL.md`
- **Category:** Executive advisory. **Simplicity** 9, **Value** 2, **Total** 11. **Recommendation:** Optional pack: business.
- **What it does:** Retention-obsessed Chief Customer Officer interrogation of any plan that touches customer retention, segmentation, CS team sizing, or CS team hiring. Use when gross retention is slipping, before approving CSM headcount, or when deciding which customer segments to keep or fire.
- **Overlap with mux:** none.
- **Fit in mux:** Offer it in the opt-in `business` pack as a playbook (instructions only). Nothing is seeded until a user installs the pack.
- **Integration cost:** a single SKILL.md with no bundled scripts.

- [x] Import `c-level-agents/skills/cco-review/SKILL.md` into `packs/business/cco-review/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/business/cco-review` with category `business`.

### 101. cdo-review

- **Source:** `c-level-agents/skills/cdo-review/SKILL.md`
- **Category:** Executive advisory. **Simplicity** 9, **Value** 2, **Total** 11. **Recommendation:** Optional pack: business.
- **What it does:** Decision-driven Chief Data Officer interrogation of any plan that touches training data, data architecture, data productization, or data team hiring. Use when validating training-data rights before model work, choosing warehouse vs lakehouse vs mesh, or valuing data assets for productization or M&A.
- **Overlap with mux:** none.
- **Fit in mux:** Offer it in the opt-in `business` pack as a playbook (instructions only). Nothing is seeded until a user installs the pack.
- **Integration cost:** a single SKILL.md with no bundled scripts.

- [x] Import `c-level-agents/skills/cdo-review/SKILL.md` into `packs/business/cdo-review/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/business/cdo-review` with category `business`.

### 102. cfo-review

- **Source:** `c-level-agents/skills/cfo-review/SKILL.md`
- **Category:** Executive advisory. **Simplicity** 9, **Value** 2, **Total** 11. **Recommendation:** Optional pack: business.
- **What it does:** Numerate-skeptic interrogation of any plan that touches money. Unit economics, runway, dilution, capital allocation. Use when a plan commits meaningful spend. e.g. a hiring wave, a fundraise decision, or a new channel budget.
- **Overlap with mux:** none.
- **Fit in mux:** Offer it in the opt-in `business` pack as a playbook (instructions only). Nothing is seeded until a user installs the pack.
- **Integration cost:** a single SKILL.md with no bundled scripts.

- [x] Import `c-level-agents/skills/cfo-review/SKILL.md` into `packs/business/cfo-review/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/business/cfo-review` with category `business`.

### 103. challenge

- **Source:** `c-level-advisor/executive-mentor/skills/challenge/SKILL.md`
- **Category:** Executive advisory. **Simplicity** 9, **Value** 2, **Total** 11. **Recommendation:** Optional pack: business.
- **What it does:** Pre-mortem plan analysis. Imagine the plan failed 12 months from now and work backwards to find the weaknesses. Surfaces assumptions, dependencies, and execution risks before committing resources. Use when before significant resource commitment, before presenting to a board or investors, when feedback has been one-sidedly positive, or when there is pressure to move fast and figure it out later.
- **Overlap with mux:** none.
- **Fit in mux:** Offer it in the opt-in `business` pack as a playbook (instructions only). Nothing is seeded until a user installs the pack.
- **Integration cost:** a single SKILL.md with no bundled scripts.

- [x] Import `c-level-advisor/executive-mentor/skills/challenge/SKILL.md` into `packs/business/challenge/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/business/mentor-challenge` with category `business`. Shipped under the id `mentor-challenge` (the original name `challenge` was too generic or shared with another skill).

### 104. change-management

- **Source:** `c-level-advisor/skills/change-management/SKILL.md`
- **Category:** Executive advisory. **Simplicity** 9, **Value** 2, **Total** 11. **Recommendation:** Optional pack: business.
- **What it does:** Framework for rolling out organizational changes without chaos. Covers the ADKAR model adapted for startups, communication templates, resistance patterns, and change fatigue management. Handles process changes, org restructures, strategy pivots, and culture changes. Use when announcing a reorg, switching tools, pivoting strategy, killing a product, changing leadership, or when user mentions change
- **Overlap with mux:** none.
- **Fit in mux:** Offer it in the opt-in `business` pack as a playbook (instructions only). Nothing is seeded until a user installs the pack.
- **Integration cost:** a single SKILL.md with no bundled scripts; bundled `references/`.

- [x] Import `c-level-advisor/skills/change-management/SKILL.md` into `packs/business/change-management/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/business/change-management` with category `business`.

### 105. ciso-review

- **Source:** `c-level-agents/skills/ciso-review/SKILL.md`
- **Category:** Executive advisory. **Simplicity** 9, **Value** 2, **Total** 11. **Recommendation:** Optional pack: business.
- **What it does:** Risk-paranoid interrogation of any plan that touches data, compliance, or production access. Use when launching features that handle customer data, before a SOC 2 / ISO audit, or after any incident or near-miss.
- **Overlap with mux:** none.
- **Fit in mux:** Offer it in the opt-in `business` pack as a playbook (instructions only). Nothing is seeded until a user installs the pack.
- **Integration cost:** a single SKILL.md with no bundled scripts.

- [x] Import `c-level-agents/skills/ciso-review/SKILL.md` into `packs/business/ciso-review/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/business/ciso-review` with category `business`.

### 106. cmo-review

- **Source:** `c-level-agents/skills/cmo-review/SKILL.md`
- **Category:** Executive advisory. **Simplicity** 9, **Value** 2, **Total** 11. **Recommendation:** Optional pack: business.
- **What it does:** Narrative-first interrogation of positioning, ICP, message house, and channel mix. Use when launching a campaign or repositioning, or when CAC is rising and the one-sentence positioning test fails.
- **Overlap with mux:** none.
- **Fit in mux:** Offer it in the opt-in `business` pack as a playbook (instructions only). Nothing is seeded until a user installs the pack.
- **Integration cost:** a single SKILL.md with no bundled scripts.

- [x] Import `c-level-agents/skills/cmo-review/SKILL.md` into `packs/business/cmo-review/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/business/cmo-review` with category `business`.

### 107. company-os

- **Source:** `c-level-advisor/skills/company-os/SKILL.md`
- **Category:** Executive advisory. **Simplicity** 9, **Value** 2, **Total** 11. **Recommendation:** Optional pack: business.
- **What it does:** The meta-framework for how a company runs. the connective tissue between all C-suite roles. Covers operating system selection (EOS, Scaling Up, OKR-native, hybrid), accountability charts, scorecards, meeting pulse, issue resolution, and 90-day rocks. Use when setting up company operations, selecting a management framework, designing meeting rhythms, building accountability systems, implementing O
- **Overlap with mux:** none.
- **Fit in mux:** Offer it in the opt-in `business` pack as a playbook (instructions only). Nothing is seeded until a user installs the pack.
- **Integration cost:** a single SKILL.md with no bundled scripts; bundled `references/`.

- [x] Import `c-level-advisor/skills/company-os/SKILL.md` into `packs/business/company-os/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/business/company-os` with category `business`.

### 108. competitive-intel

- **Source:** `c-level-advisor/skills/competitive-intel/SKILL.md`
- **Category:** Executive advisory. **Simplicity** 9, **Value** 2, **Total** 11. **Recommendation:** Optional pack: business.
- **What it does:** Systematic competitor tracking that feeds CMO positioning, CRO battlecards, and CPO roadmap decisions. Use when analyzing competitors, building sales battlecards, tracking market moves, positioning against alternatives, or when user mentions competitive intelligence, competitive analysis, competitor research, battlecards, win/loss, or market positioning.
- **Overlap with mux:** none.
- **Fit in mux:** Offer it in the opt-in `business` pack as a playbook (instructions only). Nothing is seeded until a user installs the pack.
- **Integration cost:** a single SKILL.md with no bundled scripts; bundled `references/`, `templates/`.

- [x] Import `c-level-advisor/skills/competitive-intel/SKILL.md` into `packs/business/competitive-intel/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/business/competitive-intel` with category `business`.

### 109. cpo-review

- **Source:** `c-level-agents/skills/cpo-review/SKILL.md`
- **Category:** Executive advisory. **Simplicity** 9, **Value** 2, **Total** 11. **Recommendation:** Optional pack: business.
- **What it does:** JTBD-driven interrogation of product roadmap, PMF signal, and portfolio focus. Use when committing a quarter's roadmap, deciding whether to kill a feature, or claiming PMF without a retention curve.
- **Overlap with mux:** none.
- **Fit in mux:** Offer it in the opt-in `business` pack as a playbook (instructions only). Nothing is seeded until a user installs the pack.
- **Integration cost:** a single SKILL.md with no bundled scripts.

- [x] Import `c-level-agents/skills/cpo-review/SKILL.md` into `packs/business/cpo-review/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/business/cpo-review` with category `business`.

### 110. cro-review

- **Source:** `c-level-agents/skills/cro-review/SKILL.md`
- **Category:** Executive advisory. **Simplicity** 9, **Value** 2, **Total** 11. **Recommendation:** Optional pack: business.
- **What it does:** Pipeline-paranoid interrogation of revenue, win rate, NRR, and ramp time. Use when the forecast misses pipeline coverage, win rates drop, or before scaling the sales team.
- **Overlap with mux:** none.
- **Fit in mux:** Offer it in the opt-in `business` pack as a playbook (instructions only). Nothing is seeded until a user installs the pack.
- **Integration cost:** a single SKILL.md with no bundled scripts.

- [x] Import `c-level-agents/skills/cro-review/SKILL.md` into `packs/business/cro-review/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/business/cro-review` with category `business`.

### 111. culture-architect

- **Source:** `c-level-advisor/skills/culture-architect/SKILL.md`
- **Category:** Executive advisory. **Simplicity** 9, **Value** 2, **Total** 11. **Recommendation:** Optional pack: business.
- **What it does:** Build, measure, and evolve company culture as operational behavior. not wall posters. Covers mission/vision/values workshops, values-to-behaviors translation, culture code creation, culture health assessment, and cultural rituals by stage. Use when building company values, assessing culture health, designing cultural rituals, creating culture codes, handling culture clashes, or when user mentions
- **Overlap with mux:** none.
- **Fit in mux:** Offer it in the opt-in `business` pack as a playbook (instructions only). Nothing is seeded until a user installs the pack.
- **Integration cost:** a single SKILL.md with no bundled scripts; bundled `references/`, `templates/`.

- [x] Import `c-level-advisor/skills/culture-architect/SKILL.md` into `packs/business/culture-architect/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/business/culture-architect` with category `business`.

### 112. founder-coach

- **Source:** `c-level-advisor/skills/founder-coach/SKILL.md`
- **Category:** Executive advisory. **Simplicity** 9, **Value** 2, **Total** 11. **Recommendation:** Optional pack: business.
- **What it does:** Personal leadership development for founders and first-time CEOs. Covers founder archetype identification, delegation frameworks, energy management, CEO calendar audits, leadership style evolution, blind spot identification, imposter syndrome, founder mental health, and succession planning. Use when a founder feels like the bottleneck, struggles to delegate, is burning out, transitioning from IC t
- **Overlap with mux:** none.
- **Fit in mux:** Offer it in the opt-in `business` pack as a playbook (instructions only). Nothing is seeded until a user installs the pack.
- **Integration cost:** a single SKILL.md with no bundled scripts; bundled `references/`.

- [x] Import `c-level-advisor/skills/founder-coach/SKILL.md` into `packs/business/founder-coach/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/business/founder-coach` with category `business`.

### 113. gc-review

- **Source:** `c-level-agents/skills/gc-review/SKILL.md`
- **Category:** Executive advisory. **Simplicity** 9, **Value** 2, **Total** 11. **Recommendation:** Optional pack: business.
- **What it does:** General Counsel interrogation of contracts, IP, regulatory, term sheets, and employment-law surface. Use when reviewing a term sheet before signing, redlining a customer MSA, or checking IP assignment and regulatory exposure on a new product.
- **Overlap with mux:** none.
- **Fit in mux:** Offer it in the opt-in `business` pack as a playbook (instructions only). Nothing is seeded until a user installs the pack.
- **Integration cost:** a single SKILL.md with no bundled scripts.

- [x] Import `c-level-agents/skills/gc-review/SKILL.md` into `packs/business/gc-review/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/business/gc-review` with category `business`.

### 114. hard-call

- **Source:** `c-level-advisor/executive-mentor/skills/hard-call/SKILL.md`
- **Category:** Executive advisory. **Simplicity** 9, **Value** 2, **Total** 11. **Recommendation:** Optional pack: business.
- **What it does:** Framework for decisions with no good options. Use when every option is painful and a structured 10/10/10 + regret-minimization pass is needed. e.g. choosing between a layoff and a down round, or killing a beloved product line.
- **Overlap with mux:** none.
- **Fit in mux:** Offer it in the opt-in `business` pack as a playbook (instructions only). Nothing is seeded until a user installs the pack.
- **Integration cost:** a single SKILL.md with no bundled scripts.

- [x] Import `c-level-advisor/executive-mentor/skills/hard-call/SKILL.md` into `packs/business/hard-call/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/business/mentor-hard-call` with category `business`. Shipped under the id `mentor-hard-call` (the original name `hard-call` was too generic or shared with another skill).

### 115. internal-narrative

- **Source:** `c-level-advisor/skills/internal-narrative/SKILL.md`
- **Category:** Executive advisory. **Simplicity** 9, **Value** 2, **Total** 11. **Recommendation:** Optional pack: business.
- **What it does:** Build and maintain one coherent company story across all audiences. employees, investors, customers, candidates, and partners. Detects narrative contradictions and ensures the same truth is framed for each audience's needs. Use when preparing investor updates, all-hands presentations, board communications, recruiting narratives, crisis communications, or when user mentions company narrative, mess
- **Overlap with mux:** none.
- **Fit in mux:** Offer it in the opt-in `business` pack as a playbook (instructions only). Nothing is seeded until a user installs the pack.
- **Integration cost:** a single SKILL.md with no bundled scripts; bundled `references/`, `templates/`.

- [x] Import `c-level-advisor/skills/internal-narrative/SKILL.md` into `packs/business/internal-narrative/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/business/internal-narrative` with category `business`.

### 116. intl-expansion

- **Source:** `c-level-advisor/skills/intl-expansion/SKILL.md`
- **Category:** Executive advisory. **Simplicity** 9, **Value** 2, **Total** 11. **Recommendation:** Optional pack: business.
- **What it does:** International market expansion strategy. Market selection, entry modes, localization, regulatory compliance, and go-to-market by region. Use when expanding to new countries, evaluating international markets, planning localization, or building regional teams.
- **Overlap with mux:** none.
- **Fit in mux:** Offer it in the opt-in `business` pack as a playbook (instructions only). Nothing is seeded until a user installs the pack.
- **Integration cost:** a single SKILL.md with no bundled scripts; bundled `references/`.

- [x] Import `c-level-advisor/skills/intl-expansion/SKILL.md` into `packs/business/intl-expansion/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/business/intl-expansion` with category `business`.

### 117. ma-playbook

- **Source:** `c-level-advisor/skills/ma-playbook/SKILL.md`
- **Category:** Executive advisory. **Simplicity** 9, **Value** 2, **Total** 11. **Recommendation:** Optional pack: business.
- **What it does:** M&A strategy for acquiring companies or being acquired. Due diligence, valuation, integration, and deal structure. Use when evaluating acquisitions, preparing for acquisition, M&A due diligence, integration planning, or deal negotiation.
- **Overlap with mux:** none.
- **Fit in mux:** Offer it in the opt-in `business` pack as a playbook (instructions only). Nothing is seeded until a user installs the pack.
- **Integration cost:** a single SKILL.md with no bundled scripts; bundled `references/`.

- [x] Import `c-level-advisor/skills/ma-playbook/SKILL.md` into `packs/business/ma-playbook/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/business/ma-playbook` with category `business`.

### 118. marketing-strategy-pmm

- **Source:** `marketing-skill/skills/marketing-strategy-pmm/SKILL.md`
- **Category:** Marketing. **Simplicity** 9, **Value** 2, **Total** 11. **Recommendation:** Optional pack: marketing.
- **What it does:** Product marketing skill for positioning, GTM strategy, competitive intelligence, and product launches. Use when the user asks about product positioning, go-to-market planning, competitive analysis, target audience definition, ICP definition, market research, launch plans, or sales enablement. Covers April Dunford positioning, ICP definition, competitive battlecards, launch playbooks, and internati
- **Overlap with mux:** none.
- **Fit in mux:** Offer it in the opt-in `marketing` pack as a playbook (instructions only). Nothing is seeded until a user installs the pack.
- **Integration cost:** a single SKILL.md with no bundled scripts; bundled `references/`.

- [x] Import `marketing-skill/skills/marketing-strategy-pmm/SKILL.md` into `packs/marketing/marketing-strategy-pmm/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/marketing/marketing-strategy-pmm` with category `marketing`.

### 119. meeting-analyzer

- **Source:** `project-management/skills/meeting-analyzer/SKILL.md`
- **Category:** Project management. **Simplicity** 9, **Value** 2, **Total** 11. **Recommendation:** Optional pack: product.
- **What it does:** Analyzes meeting transcripts and recordings to surface behavioral patterns, communication anti-patterns, and actionable coaching feedback. Use this skill whenever the user uploads or points to meeting transcripts (.txt, .md, .vtt, .srt, .docx), asks about their communication habits, wants feedback on how they run meetings, requests speaking ratio analysis, mentions filler words or conflict avoidan
- **Overlap with mux:** none.
- **Fit in mux:** Offer it in the opt-in `product` pack as a playbook (instructions only). Nothing is seeded until a user installs the pack.
- **Integration cost:** a single SKILL.md with no bundled scripts.

- [x] Import `project-management/skills/meeting-analyzer/SKILL.md` into `packs/product/meeting-analyzer/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/product/meeting-analyzer` with category `productivity`.

### 120. office-hours

- **Source:** `c-level-agents/skills/office-hours/SKILL.md`
- **Category:** Executive advisory. **Simplicity** 9, **Value** 2, **Total** 11. **Recommendation:** Optional pack: business.
- **What it does:** YC-style 6-question founder interrogation before any advice. Forces clarity on problem, customer, distribution, defensibility, capital, and founder fit. Use when a founder question is too vague to route. e.g. 'should we grow faster?'. or before drafting a strategy brief.
- **Overlap with mux:** none.
- **Fit in mux:** Offer it in the opt-in `business` pack as a playbook (instructions only). Nothing is seeded until a user installs the pack.
- **Integration cost:** a single SKILL.md with no bundled scripts.

- [x] Import `c-level-agents/skills/office-hours/SKILL.md` into `packs/business/office-hours/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/business/office-hours` with category `business`.

### 121. postmortem

- **Source:** `c-level-advisor/executive-mentor/skills/postmortem/SKILL.md`
- **Category:** Executive advisory. **Simplicity** 9, **Value** 2, **Total** 11. **Recommendation:** Optional pack: business.
- **What it does:** Honest analysis of what went wrong. Use after a failed launch, missed quarter, or bad hire to run a blameless 5-Whys retrospective with a change register. e.g. dissecting why the Q3 release slipped six weeks.
- **Overlap with mux:** none.
- **Fit in mux:** Offer it in the opt-in `business` pack as a playbook (instructions only). Nothing is seeded until a user installs the pack.
- **Integration cost:** a single SKILL.md with no bundled scripts.

- [x] Import `c-level-advisor/executive-mentor/skills/postmortem/SKILL.md` into `packs/business/postmortem/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/business/mentor-postmortem` with category `business`. Shipped under the id `mentor-postmortem` (the original name `postmortem` was too generic or shared with another skill).

### 122. stress-test

- **Source:** `c-level-advisor/executive-mentor/skills/stress-test/SKILL.md`
- **Category:** Executive advisory. **Simplicity** 9, **Value** 2, **Total** 11. **Recommendation:** Optional pack: business.
- **What it does:** Business assumption stress testing. Use before betting on a plan whose core assumptions are unvalidated. e.g. stress-testing 'enterprise buyers will tolerate a 6-month pilot' or a hockey-stick revenue model.
- **Overlap with mux:** none.
- **Fit in mux:** Offer it in the opt-in `business` pack as a playbook (instructions only). Nothing is seeded until a user installs the pack.
- **Integration cost:** a single SKILL.md with no bundled scripts.

- [x] Import `c-level-advisor/executive-mentor/skills/stress-test/SKILL.md` into `packs/business/stress-test/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/business/mentor-stress-test` with category `business`. Shipped under the id `mentor-stress-test` (the original name `stress-test` was too generic or shared with another skill).

### 123. vpe-review

- **Source:** `c-level-agents/skills/vpe-review/SKILL.md`
- **Category:** Executive advisory. **Simplicity** 9, **Value** 2, **Total** 11. **Recommendation:** Optional pack: business.
- **What it does:** Throughput-first VP of Engineering interrogation of any plan that touches delivery, eng hiring, team structure, or production discipline. Use when cycle time balloons, DORA metrics slide, or before committing to an eng hiring wave or a reorg.
- **Overlap with mux:** none.
- **Fit in mux:** Offer it in the opt-in `business` pack as a playbook (instructions only). Nothing is seeded until a user installs the pack.
- **Integration cost:** a single SKILL.md with no bundled scripts.

- [x] Import `c-level-agents/skills/vpe-review/SKILL.md` into `packs/business/vpe-review/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/business/vpe-review` with category `business`.

### 124. youtube-full

- **Source:** `marketing-skill/skills/youtube-full/SKILL.md`
- **Category:** Marketing. **Simplicity** 9, **Value** 2, **Total** 11. **Recommendation:** Optional pack: marketing.
- **What it does:** Use when the user needs YouTube transcripts, video search, channel browsing, playlist extraction, or content monitoring. Trigger phrases: 'get the transcript for', 'search YouTube for', 'what are the latest videos on', 'list this playlist', 'monitor this channel', or any request involving a YouTube URL, video ID, or @handle. Do NOT use for downloading video or audio files, YouTube engagement data
- **Overlap with mux:** none.
- **Fit in mux:** Offer it in the opt-in `marketing` pack as a playbook (instructions only). Nothing is seeded until a user installs the pack.
- **Integration cost:** a single SKILL.md with no bundled scripts.

- [x] Import `marketing-skill/skills/youtube-full/SKILL.md` into `packs/marketing/youtube-full/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/marketing/youtube-full` with category `marketing`.

### 125. browser-automation

- **Source:** `engineering/skills/browser-automation/SKILL.md`
- **Category:** Engineering. **Simplicity** 5, **Value** 5, **Total** 10. **Recommendation:** Optional pack: engineering.
- **What it does:** Use when the user asks to automate browser tasks, scrape websites, fill forms, capture screenshots, extract structured data from web pages, or build web automation workflows. NOT for testing. use playwright-pro for that.
- **Overlap with mux:** web_retrieve (Playwright-backed, read-only).
- **Fit in mux:** Offer it in the opt-in `engineering` pack as a hybrid skill whose commands run the bundled scripts. Nothing is seeded until a user installs the pack.
- **Integration cost:** 3 bundled scripts; third-party Python packages (playwright); bundled `references/`.
- **Assessment:** Needs Playwright installed; mux already ships Playwright for retrieval, so a command skill could reuse it.

- [x] Import `engineering/skills/browser-automation/SKILL.md` into `packs/engineering/browser-automation/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [~] Declare `requiresTools: [python3]` and confirm the 3 bundled scripts run from the skill folder after installing their third-party packages, documented in the pack README.
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/engineering/browser-automation` with category `frontend`. Partial: the bundled scripts are called from the body as `python3 "${SKILL_DIR}/scripts/..."` but were not executed during the import (the source is treated as untrusted), and `requiresTools: [python3]` is not declared because Windows installs usually name the interpreter `python`, which would hide the skill there. No `run_skill` command wrappers yet.

### 126. tech-debt-tracker

- **Source:** `engineering/skills/tech-debt-tracker/SKILL.md`
- **Category:** Engineering. **Simplicity** 5, **Value** 5, **Total** 10. **Recommendation:** Adapt into todo-scan.
- **What it does:** Scan codebases for technical debt, score severity, track trends, and generate prioritized remediation plans. Use when users mention tech debt, code quality, refactoring priority, debt scoring, cleanup sprints, or code health assessment. Also use for legacy code modernization planning and maintenance cost estimation.
- **Overlap with mux:** todo-scan, dead-code-scan.
- **Fit in mux:** Do not ship it on its own. Its useful parts go into `todo-scan`.
- **Integration cost:** 6 bundled scripts; third-party Python packages (requests); bundled `references/`, `assets/`.
- **Assessment:** Severity scoring and trend tracking can extend todo-scan.

- [x] Read `engineering/skills/tech-debt-tracker/SKILL.md` and list the procedures or checks that `todo-scan` does not already have.
- [x] Fold those into `todo-scan` (body, a new command, or an effort word), keeping mux's output format and exit codes.
- [x] Extend the tests that cover `todo-scan` with a case for each added check.
- [x] Note the borrowed ideas and the MIT source in `THIRD_PARTY_NOTICES.md` and the CHANGELOG. The notice is in THIRD_PARTY_NOTICES.md; the USAGE.md and CHANGELOG entries come with the release documentation pass.

**Notes:** Merged on 2026-10-09; see the target skill's body and, where noted in THIRD_PARTY_NOTICES.md, its `resources/` folder.

### 127. terraform-patterns

- **Source:** `engineering/terraform-patterns/skills/terraform-patterns/SKILL.md`
- **Category:** Engineering. **Simplicity** 5, **Value** 5, **Total** 10. **Recommendation:** Adapt into terraform.
- **What it does:** Terraform infrastructure-as-code agent skill and plugin for Claude Code, Codex, Gemini CLI, Cursor, OpenClaw. Covers module design patterns, state management strategies, provider configuration, security hardening, policy-as-code with Sentinel/OPA, and CI/CD plan/apply workflows. Use when: user wants to design Terraform modules, manage state backends, review Terraform security, implement multi-regi
- **Overlap with mux:** terraform.
- **Fit in mux:** Do not ship it on its own. Its useful parts go into `terraform`.
- **Integration cost:** 2 bundled scripts, standard-library Python; references Claude Code files or conventions (~/.claude) that must be rewritten for mux; 740 lines, long for small models; bundled `references/`.
- **Assessment:** 740 lines of module and state patterns; distill into the terraform skill body and a references file.

- [x] Read `engineering/terraform-patterns/skills/terraform-patterns/SKILL.md` and list the procedures or checks that `terraform` does not already have.
- [x] Fold those into `terraform` (body, a new command, or an effort word), keeping mux's output format and exit codes.
- [x] Extend the tests that cover `terraform` with a case for each added check.
- [x] Note the borrowed ideas and the MIT source in `THIRD_PARTY_NOTICES.md` and the CHANGELOG. The notice is in THIRD_PARTY_NOTICES.md; the USAGE.md and CHANGELOG entries come with the release documentation pass.

**Notes:** Merged on 2026-10-09; see the target skill's body and, where noted in THIRD_PARTY_NOTICES.md, its `resources/` folder.

### 128. autoresearch-agent

- **Source:** `engineering/autoresearch-agent/skills/autoresearch-agent/SKILL.md`
- **Category:** Engineering. **Simplicity** 6, **Value** 4, **Total** 10. **Recommendation:** Optional pack: engineering.
- **What it does:** Autonomous experiment loop that optimizes any file by a measurable metric. Inspired by Karpathy's autoresearch. The agent edits a target file, runs a fixed evaluation, keeps improvements (git commit), discards failures (git reset), and loops indefinitely. Use when: user wants to optimize code speed, reduce bundle/image size, improve test pass rate, optimize prompts, improve content quality (headli
- **Overlap with mux:** /loop, loop-until, fix-until-green.
- **Fit in mux:** Offer it in the opt-in `engineering` pack as a hybrid skill whose commands run the bundled scripts. Nothing is seeded until a user installs the pack.
- **Integration cost:** 3 bundled scripts, standard-library Python; references Claude Code files or conventions (~/.claude) that must be rewritten for mux; bundled `references/`.
- **Assessment:** A metric-driven edit-run-keep loop is a nice addition to mux loops; the scheduling half uses CronCreate and must be rewritten for /loop.

- [x] Import `engineering/autoresearch-agent/skills/autoresearch-agent/SKILL.md` into `packs/engineering/autoresearch-agent/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [~] Declare `requiresTools: [python3]` and confirm the 3 bundled scripts run from the skill folder.
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/engineering/autoresearch-agent` with category `research`. Partial: the bundled scripts are called from the body as `python3 "${SKILL_DIR}/scripts/..."` but were not executed during the import (the source is treated as untrusted), and `requiresTools: [python3]` is not declared because Windows installs usually name the interpreter `python`, which would hide the skill there. No `run_skill` command wrappers yet.

### 129. azure-cloud-architect

- **Source:** `engineering-team/skills/azure-cloud-architect/SKILL.md`
- **Category:** Engineering. **Simplicity** 6, **Value** 4, **Total** 10. **Recommendation:** Optional pack: engineering.
- **What it does:** Design Azure architectures for startups and enterprises. Use when asked to design Azure infrastructure, create Bicep/ARM templates, optimize Azure costs, set up Azure DevOps pipelines, or migrate to Azure. Covers AKS, App Service, Azure Functions, Cosmos DB, and cost optimization.
- **Overlap with mux:** aws-*, azure-*, gcp-* (operations only).
- **Fit in mux:** Offer it in the opt-in `engineering` pack as a hybrid skill whose commands run the bundled scripts. Nothing is seeded until a user installs the pack.
- **Integration cost:** 3 bundled scripts, standard-library Python; 463 lines, long for small models; bundled `references/`.
- **Assessment:** Design guidance complements mux cloud skills, which operate rather than design; keep out of the default listing.

- [x] Import `engineering-team/skills/azure-cloud-architect/SKILL.md` into `packs/engineering/azure-cloud-architect/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [~] Declare `requiresTools: [python3]` and confirm the 3 bundled scripts run from the skill folder.
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/engineering/azure-cloud-architect` with category `cloud`. Partial: the bundled scripts are called from the body as `python3 "${SKILL_DIR}/scripts/..."` but were not executed during the import (the source is treated as untrusted), and `requiresTools: [python3]` is not declared because Windows installs usually name the interpreter `python`, which would hide the skill there. No `run_skill` command wrappers yet.

### 130. code-to-prd

- **Source:** `product-team/code-to-prd/skills/code-to-prd/SKILL.md`
- **Category:** Product. **Simplicity** 6, **Value** 4, **Total** 10. **Recommendation:** Optional pack: product.
- **What it does:** Reverse-engineer any codebase into a complete Product Requirements Document (PRD). Analyzes routes, components, state management, API integrations, and user interactions to produce business-readable documentation detailed enough for engineers or AI agents to fully reconstruct every page and endpoint. Works with frontend frameworks (React, Vue, Angular, Svelte, Next.js, Nuxt), backend frameworks (N
- **Overlap with mux:** none.
- **Fit in mux:** Offer it in the opt-in `product` pack as a hybrid skill whose commands run the bundled scripts. Nothing is seeded until a user installs the pack.
- **Integration cost:** 2 bundled scripts, standard-library Python; 496 lines, long for small models; bundled `references/`, `assets/`.

- [x] Import `product-team/code-to-prd/skills/code-to-prd/SKILL.md` into `packs/product/code-to-prd/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [~] Declare `requiresTools: [python3]` and confirm the 2 bundled scripts run from the skill folder.
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/product/code-to-prd` with category `product`. Partial: the bundled scripts are called from the body as `python3 "${SKILL_DIR}/scripts/..."` but were not executed during the import (the source is treated as untrusted), and `requiresTools: [python3]` is not declared because Windows installs usually name the interpreter `python`, which would hide the skill there. No `run_skill` command wrappers yet.

### 131. docker-development

- **Source:** `engineering/docker-development/skills/docker-development/SKILL.md`
- **Category:** Engineering. **Simplicity** 6, **Value** 4, **Total** 10. **Recommendation:** Adapt into dockerfile-lint.
- **What it does:** Docker and container development agent skill and plugin for Dockerfile optimization, docker-compose orchestration, multi-stage builds, and container security hardening. Use when: user wants to optimize a Dockerfile, create or improve docker-compose configurations, implement multi-stage builds, audit container security, reduce image size, or follow container best practices. Covers build performance
- **Overlap with mux:** docker-build, dockerfile-lint, compose.
- **Fit in mux:** Do not ship it on its own. Its useful parts go into `dockerfile-lint`.
- **Integration cost:** 2 bundled scripts, standard-library Python; references Claude Code files or conventions (~/.claude) that must be rewritten for mux; bundled `references/`.
- **Assessment:** Multi-stage and image-size guidance can extend dockerfile-lint; the rest is covered.

- [x] Read `engineering/docker-development/skills/docker-development/SKILL.md` and list the procedures or checks that `dockerfile-lint` does not already have.
- [x] Fold those into `dockerfile-lint` (body, a new command, or an effort word), keeping mux's output format and exit codes.
- [x] Extend the tests that cover `dockerfile-lint` with a case for each added check.
- [x] Note the borrowed ideas and the MIT source in `THIRD_PARTY_NOTICES.md` and the CHANGELOG. The notice is in THIRD_PARTY_NOTICES.md; the USAGE.md and CHANGELOG entries come with the release documentation pass.

**Notes:** Merged on 2026-10-09; see the target skill's body and, where noted in THIRD_PARTY_NOTICES.md, its `resources/` folder.

### 132. gcp-cloud-architect

- **Source:** `engineering-team/skills/gcp-cloud-architect/SKILL.md`
- **Category:** Engineering. **Simplicity** 6, **Value** 4, **Total** 10. **Recommendation:** Optional pack: engineering.
- **What it does:** Design GCP architectures for startups and enterprises. Use when asked to design Google Cloud infrastructure, deploy to GKE or Cloud Run, configure BigQuery pipelines, optimize GCP costs, or migrate to GCP. Covers Cloud Run, GKE, Cloud Functions, Cloud SQL, BigQuery, and cost optimization.
- **Overlap with mux:** aws-*, azure-*, gcp-* (operations only).
- **Fit in mux:** Offer it in the opt-in `engineering` pack as a hybrid skill whose commands run the bundled scripts. Nothing is seeded until a user installs the pack.
- **Integration cost:** 3 bundled scripts, standard-library Python; 444 lines, long for small models; bundled `references/`.
- **Assessment:** Design guidance complements mux cloud skills, which operate rather than design; keep out of the default listing.

- [x] Import `engineering-team/skills/gcp-cloud-architect/SKILL.md` into `packs/engineering/gcp-cloud-architect/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [~] Declare `requiresTools: [python3]` and confirm the 3 bundled scripts run from the skill folder.
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/engineering/gcp-cloud-architect` with category `cloud`. Partial: the bundled scripts are called from the body as `python3 "${SKILL_DIR}/scripts/..."` but were not executed during the import (the source is treated as untrusted), and `requiresTools: [python3]` is not declared because Windows installs usually name the interpreter `python`, which would hide the skill there. No `run_skill` command wrappers yet.

### 133. karpathy-coder

- **Source:** `engineering/karpathy-coder/skills/karpathy-coder/SKILL.md`
- **Category:** Engineering. **Simplicity** 6, **Value** 4, **Total** 10. **Recommendation:** Optional pack: engineering.
- **What it does:** Use when writing, reviewing, or committing code to enforce Karpathy's 4 coding principles. surface assumptions before coding, keep it simple, make surgical changes, define verifiable goals. Triggers on "review my diff", "check complexity", "am I overcomplicating this", "karpathy check", "before I commit", or any code quality concern where the LLM might be overcoding.
- **Overlap with mux:** none.
- **Fit in mux:** Offer it in the opt-in `engineering` pack as a hybrid skill whose commands run the bundled scripts. Nothing is seeded until a user installs the pack.
- **Integration cost:** 4 bundled scripts, standard-library Python; references Claude Code files or conventions (.claude/, claude.md) that must be rewritten for mux; bundled `references/`.
- **Assessment:** Coding-discipline playbooks; useful as opt-in styles, too opinionated for a default.

- [x] Import `engineering/karpathy-coder/skills/karpathy-coder/SKILL.md` into `packs/engineering/karpathy-coder/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [~] Declare `requiresTools: [python3]` and confirm the 4 bundled scripts run from the skill folder.
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/engineering/karpathy-coder` with category `engineering`. Partial: the bundled scripts are called from the body as `python3 "${SKILL_DIR}/scripts/..."` but were not executed during the import (the source is treated as untrusted), and `requiresTools: [python3]` is not declared because Windows installs usually name the interpreter `python`, which would hide the skill there. No `run_skill` command wrappers yet.

### 134. loop

- **Source:** `engineering/autoresearch-agent/skills/loop/SKILL.md`
- **Category:** Engineering. **Simplicity** 6, **Value** 4, **Total** 10. **Recommendation:** Optional pack: engineering.
- **What it does:** Start an autonomous experiment loop with user-selected interval (10min, 1h, daily, weekly, monthly). Uses CronCreate for scheduling. Use when the user runs /ar:loop or asks to run an autoresearch experiment continuously on a schedule.
- **Overlap with mux:** /loop, loop-until, fix-until-green.
- **Fit in mux:** Offer it in the opt-in `engineering` pack as a playbook (instructions only). Nothing is seeded until a user installs the pack.
- **Integration cost:** depends on Claude Code primitives (croncreate).
- **Assessment:** A metric-driven edit-run-keep loop is a nice addition to mux loops; the scheduling half uses CronCreate and must be rewritten for /loop.

- [x] Import `engineering/autoresearch-agent/skills/loop/SKILL.md` into `packs/engineering/loop/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/engineering/ar-loop` with category `research`. Shipped under the id `ar-loop` (the original name `loop` was too generic or shared with another skill).

### 135. migration-architect

- **Source:** `engineering/skills/migration-architect/SKILL.md`
- **Category:** Engineering. **Simplicity** 6, **Value** 4, **Total** 10. **Recommendation:** Optional pack: engineering.
- **What it does:** Zero-downtime migration planning, compatibility validation, and rollback strategy generation. Tools for system, database, and infrastructure migrations with minimal business impact. Use when planning a database migration, infrastructure cutover, system replacement, or any high-risk transition that needs explicit rollback paths.
- **Overlap with mux:** none.
- **Fit in mux:** Offer it in the opt-in `engineering` pack as a hybrid skill whose commands run the bundled scripts. Nothing is seeded until a user installs the pack.
- **Integration cost:** 3 bundled scripts, standard-library Python; 428 lines, long for small models; bundled `references/`, `assets/`.

- [x] Import `engineering/skills/migration-architect/SKILL.md` into `packs/engineering/migration-architect/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [~] Declare `requiresTools: [python3]` and confirm the 3 bundled scripts run from the skill folder.
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/engineering/migration-architect` with category `engineering`. Partial: the bundled scripts are called from the body as `python3 "${SKILL_DIR}/scripts/..."` but were not executed during the import (the source is treated as untrusted), and `requiresTools: [python3]` is not declared because Windows installs usually name the interpreter `python`, which would hide the skill there. No `run_skill` command wrappers yet.

### 136. secrets-vault-manager

- **Source:** `engineering/skills/secrets-vault-manager/SKILL.md`
- **Category:** Engineering. **Simplicity** 6, **Value** 4, **Total** 10. **Recommendation:** Optional pack: engineering.
- **What it does:** Use when the user asks to set up secret management infrastructure, integrate HashiCorp Vault, configure cloud secret stores (AWS Secrets Manager, Azure Key Vault, GCP Secret Manager), implement secret rotation, or audit secret access patterns.
- **Overlap with mux:** git-secret-scan, cloud secret listing.
- **Fit in mux:** Offer it in the opt-in `engineering` pack as a hybrid skill whose commands run the bundled scripts. Nothing is seeded until a user installs the pack.
- **Integration cost:** 3 bundled scripts, standard-library Python; 403 lines, long for small models; bundled `references/`.
- **Assessment:** Hygiene guidance; mux never reads secret values, so keep any commands name-only.

- [x] Import `engineering/skills/secrets-vault-manager/SKILL.md` into `packs/engineering/secrets-vault-manager/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [~] Declare `requiresTools: [python3]` and confirm the 3 bundled scripts run from the skill folder.
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/engineering/secrets-vault-manager` with category `security`. Partial: the bundled scripts are called from the body as `python3 "${SKILL_DIR}/scripts/..."` but were not executed during the import (the source is treated as untrusted), and `requiresTools: [python3]` is not declared because Windows installs usually name the interpreter `python`, which would hide the skill there. No `run_skill` command wrappers yet.

### 137. senior-backend

- **Source:** `engineering-team/skills/senior-backend/SKILL.md`
- **Category:** Engineering. **Simplicity** 6, **Value** 4, **Total** 10. **Recommendation:** Optional pack: engineering.
- **What it does:** Designs and implements backend systems including REST APIs, microservices, database architectures, authentication flows, and security hardening. Use when the user asks to "design REST APIs", "optimize database queries", "implement authentication", "build microservices", "review backend code", "set up GraphQL", "handle database migrations", or "load test APIs". Covers Node.js/Express/Fastify develo
- **Overlap with mux:** none.
- **Fit in mux:** Offer it in the opt-in `engineering` pack as a hybrid skill whose commands run the bundled scripts. Nothing is seeded until a user installs the pack.
- **Integration cost:** 4 bundled scripts; 467 lines, long for small models; bundled `references/`.
- **Assessment:** Broad role playbooks; mux toolchain skills already cover the deterministic parts, so these mostly add prose.

- [x] Import `engineering-team/skills/senior-backend/SKILL.md` into `packs/engineering/senior-backend/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [~] Declare `requiresTools: [python3]` and confirm the 4 bundled scripts run from the skill folder after installing their third-party packages, documented in the pack README.
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/engineering/senior-backend` with category `engineering`. Partial: the bundled scripts are called from the body as `python3 "${SKILL_DIR}/scripts/..."` but were not executed during the import (the source is treated as untrusted), and `requiresTools: [python3]` is not declared because Windows installs usually name the interpreter `python`, which would hide the skill there. No `run_skill` command wrappers yet.

### 138. senior-frontend

- **Source:** `engineering-team/skills/senior-frontend/SKILL.md`
- **Category:** Engineering. **Simplicity** 6, **Value** 4, **Total** 10. **Recommendation:** Optional pack: engineering.
- **What it does:** Frontend development skill for React, Next.js, TypeScript, and Tailwind CSS applications. Use when building React components, optimizing Next.js performance, analyzing bundle sizes, scaffolding frontend projects, implementing accessibility, or reviewing frontend code quality.
- **Overlap with mux:** react-* family.
- **Fit in mux:** Offer it in the opt-in `engineering` pack as a hybrid skill whose commands run the bundled scripts. Nothing is seeded until a user installs the pack.
- **Integration cost:** 4 bundled scripts; 572 lines, long for small models; bundled `references/`.

- [x] Import `engineering-team/skills/senior-frontend/SKILL.md` into `packs/engineering/senior-frontend/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [~] Declare `requiresTools: [python3]` and confirm the 4 bundled scripts run from the skill folder after installing their third-party packages, documented in the pack README.
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/engineering/senior-frontend` with category `frontend`. Partial: the bundled scripts are called from the body as `python3 "${SKILL_DIR}/scripts/..."` but were not executed during the import (the source is treated as untrusted), and `requiresTools: [python3]` is not declared because Windows installs usually name the interpreter `python`, which would hide the skill there. No `run_skill` command wrappers yet.

### 139. senior-secops

- **Source:** `engineering-team/skills/senior-secops/SKILL.md`
- **Category:** Engineering. **Simplicity** 6, **Value** 4, **Total** 10. **Recommendation:** Optional pack: engineering.
- **What it does:** Senior SecOps engineer skill for application security, vulnerability management, compliance verification, and secure development practices. Runs SAST/DAST scans, generates CVE remediation plans, checks dependency vulnerabilities, creates security policies, enforces secure coding patterns, and automates compliance checks against SOC2, PCI-DSS, HIPAA, and GDPR. Use when conducting a security review
- **Overlap with mux:** none.
- **Fit in mux:** Offer it in the opt-in `engineering` pack as a hybrid skill whose commands run the bundled scripts. Nothing is seeded until a user installs the pack.
- **Integration cost:** 3 bundled scripts, standard-library Python; 505 lines, long for small models; bundled `references/`.
- **Assessment:** Broad role playbooks; mux toolchain skills already cover the deterministic parts, so these mostly add prose.

- [x] Import `engineering-team/skills/senior-secops/SKILL.md` into `packs/engineering/senior-secops/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [~] Declare `requiresTools: [python3]` and confirm the 3 bundled scripts run from the skill folder.
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/engineering/senior-secops` with category `security`. Partial: the bundled scripts are called from the body as `python3 "${SKILL_DIR}/scripts/..."` but were not executed during the import (the source is treated as untrusted), and `requiresTools: [python3]` is not declared because Windows installs usually name the interpreter `python`, which would hide the skill there. No `run_skill` command wrappers yet.

### 140. agent-decision-receipts

- **Source:** `ra-qm-team/skills/agent-decision-receipts/SKILL.md`
- **Category:** Regulatory and quality. **Simplicity** 7, **Value** 3, **Total** 10. **Recommendation:** Optional pack: compliance.
- **What it does:** Mint a tamper-evident, post-quantum-signed receipt for a consequential agent action (deploy, delete, pay, grant-access, model decision) so it can be verified later from the certificate alone. Use when an autonomous agent takes a side-effecting action that may need to be proven later, or when satisfying EU AI Act Article 12 record-keeping. Three decisions: whether an action needs a receipt, minting
- **Overlap with mux:** none.
- **Fit in mux:** Offer it in the opt-in `compliance` pack as a hybrid skill whose commands run the bundled scripts. Nothing is seeded until a user installs the pack.
- **Integration cost:** 1 bundled script, standard-library Python; bundled `references/`.

- [x] Import `ra-qm-team/skills/agent-decision-receipts/SKILL.md` into `packs/compliance/agent-decision-receipts/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [~] Declare `requiresTools: [python3]` and confirm the 1 bundled script run from the skill folder.
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/compliance/agent-decision-receipts` with category `compliance`. Partial: the bundled scripts are called from the body as `python3 "${SKILL_DIR}/scripts/..."` but were not executed during the import (the source is treated as untrusted), and `requiresTools: [python3]` is not declared because Windows installs usually name the interpreter `python`, which would hide the skill there. No `run_skill` command wrappers yet.

### 141. apple-hig-expert

- **Source:** `product-team/apple-hig-expert/skills/apple-hig-expert/SKILL.md`
- **Category:** Product. **Simplicity** 7, **Value** 3, **Total** 10. **Recommendation:** Optional pack: product.
- **What it does:** Audits and designs iOS/macOS/watchOS/visionOS interfaces against the Apple Human Interface Guidelines, including the Liquid Glass design language (announced WWDC25, shipped with iOS 26/macOS Tahoe, Sept 2025). Use when reviewing an Apple-platform mockup or app for HIG compliance, checking contrast or tap-target sizes, or designing native-feeling Apple UI (e.g., 'audit my iOS app against the HIG',
- **Overlap with mux:** none.
- **Fit in mux:** Offer it in the opt-in `product` pack as a hybrid skill whose commands run the bundled scripts. Nothing is seeded until a user installs the pack.
- **Integration cost:** 1 bundled script, standard-library Python; bundled `references/`, `templates/`.

- [x] Import `product-team/apple-hig-expert/skills/apple-hig-expert/SKILL.md` into `packs/product/apple-hig-expert/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [~] Declare `requiresTools: [python3]` and confirm the 1 bundled script run from the skill folder.
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/product/apple-hig-expert` with category `product`. Partial: the bundled scripts are called from the body as `python3 "${SKILL_DIR}/scripts/..."` but were not executed during the import (the source is treated as untrusted), and `requiresTools: [python3]` is not declared because Windows installs usually name the interpreter `python`, which would hide the skill there. No `run_skill` command wrappers yet.

### 142. competitive-teardown

- **Source:** `product-team/skills/competitive-teardown/SKILL.md`
- **Category:** Product. **Simplicity** 7, **Value** 3, **Total** 10. **Recommendation:** Optional pack: product.
- **What it does:** Analyzes competitor products and companies by synthesizing data from pricing pages, app store reviews, job postings, SEO signals, and social media into structured competitive intelligence. Produces feature comparison matrices scored across 12 dimensions, SWOT analyses, positioning maps, UX audits, pricing model breakdowns, action item roadmaps, and stakeholder presentation templates. Use when cond
- **Overlap with mux:** none.
- **Fit in mux:** Offer it in the opt-in `product` pack as a hybrid skill whose commands run the bundled scripts. Nothing is seeded until a user installs the pack.
- **Integration cost:** 1 bundled script; bundled `references/`.

- [x] Import `product-team/skills/competitive-teardown/SKILL.md` into `packs/product/competitive-teardown/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [~] Declare `requiresTools: [python3]` and confirm the 1 bundled script run from the skill folder.
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/product/competitive-teardown` with category `product`. Partial: the bundled scripts are called from the body as `python3 "${SKILL_DIR}/scripts/..."` but were not executed during the import (the source is treated as untrusted), and `requiresTools: [python3]` is not declared because Windows installs usually name the interpreter `python`, which would hide the skill there. No `run_skill` command wrappers yet.

### 143. confluence-expert

- **Source:** `project-management/skills/confluence-expert/SKILL.md`
- **Category:** Project management. **Simplicity** 7, **Value** 3, **Total** 10. **Recommendation:** Optional pack: product.
- **What it does:** Atlassian Confluence expert for creating and managing spaces, knowledge bases, and documentation. Configures space permissions and hierarchies, creates page templates with macros, sets up documentation taxonomies, designs page layouts, and manages content governance. Use when users need to build or restructure a Confluence space, design page hierarchies with permission structures, author or standa
- **Overlap with mux:** none.
- **Fit in mux:** Offer it in the opt-in `product` pack as a hybrid skill whose commands run the bundled scripts. Nothing is seeded until a user installs the pack.
- **Integration cost:** 2 bundled scripts, standard-library Python; bundled `references/`.

- [x] Import `project-management/skills/confluence-expert/SKILL.md` into `packs/product/confluence-expert/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [~] Declare `requiresTools: [python3]` and confirm the 2 bundled scripts run from the skill folder.
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/product/confluence-expert` with category `productivity`. Partial: the bundled scripts are called from the body as `python3 "${SKILL_DIR}/scripts/..."` but were not executed during the import (the source is treated as untrusted), and `requiresTools: [python3]` is not declared because Windows installs usually name the interpreter `python`, which would hide the skill there. No `run_skill` command wrappers yet.

### 144. cto-advisor

- **Source:** `c-level-advisor/skills/cto-advisor/SKILL.md`
- **Category:** Executive advisory. **Simplicity** 7, **Value** 3, **Total** 10. **Recommendation:** Optional pack: business.
- **What it does:** Technical leadership guidance for engineering teams, architecture decisions, and technology strategy. Use when assessing technical debt, scaling engineering teams, evaluating technologies, making architecture decisions, establishing engineering metrics, or when user mentions CTO, tech debt, technical debt, team scaling, architecture decisions, technology evaluation, engineering metrics, DORA metri
- **Overlap with mux:** none.
- **Fit in mux:** Offer it in the opt-in `business` pack as a hybrid skill whose commands run the bundled scripts. Nothing is seeded until a user installs the pack.
- **Integration cost:** 2 bundled scripts, standard-library Python; bundled `references/`.

- [x] Import `c-level-advisor/skills/cto-advisor/SKILL.md` into `packs/business/cto-advisor/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [~] Declare `requiresTools: [python3]` and confirm the 2 bundled scripts run from the skill folder.
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/business/cto-advisor` with category `business`. Partial: the bundled scripts are called from the body as `python3 "${SKILL_DIR}/scripts/..."` but were not executed during the import (the source is treated as untrusted), and `requiresTools: [python3]` is not declared because Windows installs usually name the interpreter `python`, which would hide the skill there. No `run_skill` command wrappers yet.

### 145. data-quality-auditor

- **Source:** `engineering/data-quality-auditor/skills/data-quality-auditor/SKILL.md`
- **Category:** Engineering. **Simplicity** 7, **Value** 3, **Total** 10. **Recommendation:** Optional pack: data.
- **What it does:** Audit datasets for completeness, consistency, accuracy, and validity. Profile data distributions, detect anomalies and outliers, surface structural issues, and produce an actionable remediation plan. Use when the user asks to check data quality, profile a dataset, hunt outliers or missing values, or validate data before analysis or model training.
- **Overlap with mux:** none.
- **Fit in mux:** Offer it in the opt-in `data` pack as a hybrid skill whose commands run the bundled scripts. Nothing is seeded until a user installs the pack.
- **Integration cost:** 3 bundled scripts, standard-library Python; bundled `references/`.

- [x] Import `engineering/data-quality-auditor/skills/data-quality-auditor/SKILL.md` into `packs/data/data-quality-auditor/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [~] Declare `requiresTools: [python3]` and confirm the 3 bundled scripts run from the skill folder.
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/data/data-quality-auditor` with category `security`. Partial: the bundled scripts are called from the body as `python3 "${SKILL_DIR}/scripts/..."` but were not executed during the import (the source is treated as untrusted), and `requiresTools: [python3]` is not declared because Windows installs usually name the interpreter `python`, which would hide the skill there. No `run_skill` command wrappers yet.

### 146. dossier

- **Source:** `research/dossier/skills/dossier/SKILL.md`
- **Category:** Research. **Simplicity** 7, **Value** 3, **Total** 10. **Recommendation:** Optional pack: research.
- **What it does:** Decision-grade entity research skill. produces a hypothesis-tested dossier on a specific company, person, nonprofit, or government org, not a generic profile. Forcing intake makes the user state their hypothesis upfront (what they already believe and want to verify or disprove) so the dossier tests it rather than confirms it. Output is an editable Word document (.docx) with verdict on the hypothe
- **Overlap with mux:** none.
- **Fit in mux:** Offer it in the opt-in `research` pack as a hybrid skill whose commands run the bundled scripts. Nothing is seeded until a user installs the pack.
- **Integration cost:** 3 bundled scripts; bundled `references/`.

- [x] Import `research/dossier/skills/dossier/SKILL.md` into `packs/research/dossier/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [~] Declare `requiresTools: [python3]` and confirm the 3 bundled scripts run from the skill folder.
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/research/dossier` with category `research`. Partial: the bundled scripts are called from the body as `python3 "${SKILL_DIR}/scripts/..."` but were not executed during the import (the source is treated as untrusted), and `requiresTools: [python3]` is not declared because Windows installs usually name the interpreter `python`, which would hide the skill there. No `run_skill` command wrappers yet.

### 147. epic-design

- **Source:** `engineering-team/skills/epic-design/SKILL.md`
- **Category:** Engineering. **Simplicity** 7, **Value** 3, **Total** 10. **Recommendation:** Optional pack: engineering.
- **What it does:** Build immersive, cinematic 2.5D interactive websites using scroll storytelling, parallax depth, text animations, and premium scroll effects. no WebGL required. Use this skill for any web design task: landing pages, product sites, hero sections, scroll animations, parallax, sticky sections, section overlaps, floating products between sections, clip-path reveals, text that flies in from sides, wo
- **Overlap with mux:** none.
- **Fit in mux:** Offer it in the opt-in `engineering` pack as a hybrid skill whose commands run the bundled scripts. Nothing is seeded until a user installs the pack.
- **Integration cost:** 2 bundled scripts, standard-library Python; bundled `references/`.

- [x] Import `engineering-team/skills/epic-design/SKILL.md` into `packs/engineering/epic-design/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [~] Declare `requiresTools: [python3]` and confirm the 1 bundled script run from the skill folder.
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/engineering/epic-design` with category `frontend`. Partial: the bundled scripts are called from the body as `python3 "${SKILL_DIR}/scripts/..."` but were not executed during the import (the source is treated as untrusted), and `requiresTools: [python3]` is not declared because Windows installs usually name the interpreter `python`, which would hide the skill there. No `run_skill` command wrappers yet.

### 148. experiment-designer

- **Source:** `product-team/skills/experiment-designer/SKILL.md`
- **Category:** Product. **Simplicity** 7, **Value** 3, **Total** 10. **Recommendation:** Optional pack: product.
- **What it does:** Use when planning product experiments, writing testable hypotheses, estimating sample size, prioritizing tests, or interpreting A/B outcomes with practical statistical rigor.
- **Overlap with mux:** none.
- **Fit in mux:** Offer it in the opt-in `product` pack as a hybrid skill whose commands run the bundled scripts. Nothing is seeded until a user installs the pack.
- **Integration cost:** 1 bundled script, standard-library Python; bundled `references/`.

- [x] Import `product-team/skills/experiment-designer/SKILL.md` into `packs/product/experiment-designer/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [~] Declare `requiresTools: [python3]` and confirm the 1 bundled script run from the skill folder.
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/product/experiment-designer` with category `product`. Partial: the bundled scripts are called from the body as `python3 "${SKILL_DIR}/scripts/..."` but were not executed during the import (the source is treated as untrusted), and `requiresTools: [python3]` is not declared because Windows installs usually name the interpreter `python`, which would hide the skill there. No `run_skill` command wrappers yet.

### 149. knowledge-ops

- **Source:** `business-operations/skills/knowledge-ops/SKILL.md`
- **Category:** Business operations. **Simplicity** 7, **Value** 3, **Total** 10. **Recommendation:** Optional pack: business.
- **What it does:** Use when a Head of Ops, Knowledge Manager, or TPM-Internal needs to author, validate, or clean up company SOPs and internal runbooks (procurement intake, vendor offboarding, incident-comms cascade, employee onboarding). including 5W2H completeness checks (Who-What-When-Where-Why-How-HowMuch), cross-link and orphan-page validation across a sprawling Notion/Confluence/Obsidian wiki, KB ingestion +
- **Overlap with mux:** none.
- **Fit in mux:** Offer it in the opt-in `business` pack as a hybrid skill whose commands run the bundled scripts. Nothing is seeded until a user installs the pack.
- **Integration cost:** 3 bundled scripts, standard-library Python; bundled `references/`, `assets/`.

- [x] Import `business-operations/skills/knowledge-ops/SKILL.md` into `packs/business/knowledge-ops/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [~] Declare `requiresTools: [python3]` and confirm the 3 bundled scripts run from the skill folder.
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/business/knowledge-ops` with category `business`. Partial: the bundled scripts are called from the body as `python3 "${SKILL_DIR}/scripts/..."` but were not executed during the import (the source is treated as untrusted), and `requiresTools: [python3]` is not declared because Windows installs usually name the interpreter `python`, which would hide the skill there. No `run_skill` command wrappers yet.

### 150. landing-page-generator

- **Source:** `product-team/skills/landing-page-generator/SKILL.md`
- **Category:** Product. **Simplicity** 7, **Value** 3, **Total** 10. **Recommendation:** Optional pack: product.
- **What it does:** Generates high-converting landing pages as complete Next.js/React (TSX) components with Tailwind CSS. Creates hero sections, feature grids, pricing tables, FAQ accordions, testimonial blocks, and CTA sections using proven copy frameworks (PAS, AIDA, BAB). Outputs SEO meta tags, structured data, and performance-optimised code targeting Core Web Vitals (LCP < 1s, CLS < 0.1). Use when the user asks t
- **Overlap with mux:** none.
- **Fit in mux:** Offer it in the opt-in `product` pack as a hybrid skill whose commands run the bundled scripts. Nothing is seeded until a user installs the pack.
- **Integration cost:** 1 bundled script; bundled `references/`.

- [x] Import `product-team/skills/landing-page-generator/SKILL.md` into `packs/product/landing-page-generator/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [~] Declare `requiresTools: [python3]` and confirm the 1 bundled script run from the skill folder.
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/product/landing-page-generator` with category `product`. Partial: the bundled scripts are called from the body as `python3 "${SKILL_DIR}/scripts/..."` but were not executed during the import (the source is treated as untrusted), and `requiresTools: [python3]` is not declared because Windows installs usually name the interpreter `python`, which would hide the skill there. No `run_skill` command wrappers yet.

### 151. litreview

- **Source:** `research/litreview/skills/litreview/SKILL.md`
- **Category:** Research. **Simplicity** 7, **Value** 3, **Total** 10. **Recommendation:** Optional pack: research.
- **What it does:** Academic literature orientation skill that searches papers via free keyless APIs (PubMed E-utilities + OpenAlex) by default. with the Consensus MCP as an optional enhancement lane when connected. builds a strategic search plan using PICO (default) or SPIDER / Decomposition / hybrid as fallbacks, and synthesizes findings into a formatted Word (.docx) research guide. Grill-me intake (research ques
- **Overlap with mux:** none.
- **Fit in mux:** Offer it in the opt-in `research` pack as a hybrid skill whose commands run the bundled scripts. Nothing is seeded until a user installs the pack.
- **Integration cost:** 4 bundled scripts, standard-library Python; bundled `references/`.

- [x] Import `research/litreview/skills/litreview/SKILL.md` into `packs/research/litreview/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [~] Declare `requiresTools: [python3]` and confirm the 4 bundled scripts run from the skill folder.
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/research/litreview` with category `research`. Partial: the bundled scripts are called from the body as `python3 "${SKILL_DIR}/scripts/..."` but were not executed during the import (the source is treated as untrusted), and `requiresTools: [python3]` is not declared because Windows installs usually name the interpreter `python`, which would hide the skill there. No `run_skill` command wrappers yet.

### 152. md-document

- **Source:** `markdown-html/skills/md-document/SKILL.md`
- **Category:** Documents. **Simplicity** 7, **Value** 3, **Total** 10. **Recommendation:** Optional pack: docs.
- **What it does:** Converts long-form markdown (specs, RFCs, reports, plans, explainers) into a single-file, lightly-interactive HTML document with sticky TOC, scrollspy, search filter, code-copy buttons, and design-system-driven brand tokens. Triggers when the markdown-html-orchestrator classifies an input as DOCUMENT, or when invoked directly via /cs:md-document. Reads the design-system config via config_loader.py
- **Overlap with mux:** none.
- **Fit in mux:** Offer it in the opt-in `docs` pack as a hybrid skill whose commands run the bundled scripts. Nothing is seeded until a user installs the pack.
- **Integration cost:** 3 bundled scripts, standard-library Python; bundled `references/`, `assets/`.

- [x] Import `markdown-html/skills/md-document/SKILL.md` into `packs/docs/md-document/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [~] Declare `requiresTools: [python3]` and confirm the 3 bundled scripts run from the skill folder.
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/docs/md-document` with category `docs`. Partial: the bundled scripts are called from the body as `python3 "${SKILL_DIR}/scripts/..."` but were not executed during the import (the source is treated as untrusted), and `requiresTools: [python3]` is not declared because Windows installs usually name the interpreter `python`, which would hide the skill there. No `run_skill` command wrappers yet.

### 153. product-analytics

- **Source:** `product-team/skills/product-analytics/SKILL.md`
- **Category:** Product. **Simplicity** 7, **Value** 3, **Total** 10. **Recommendation:** Optional pack: product.
- **What it does:** Use when defining product KPIs, building metric dashboards, running cohort or retention analysis, or interpreting feature adoption trends across product stages.
- **Overlap with mux:** none.
- **Fit in mux:** Offer it in the opt-in `product` pack as a hybrid skill whose commands run the bundled scripts. Nothing is seeded until a user installs the pack.
- **Integration cost:** 1 bundled script, standard-library Python; bundled `references/`.

- [x] Import `product-team/skills/product-analytics/SKILL.md` into `packs/product/product-analytics/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [~] Declare `requiresTools: [python3]` and confirm the 1 bundled script run from the skill folder.
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/product/product-analytics` with category `product`. Partial: the bundled scripts are called from the body as `python3 "${SKILL_DIR}/scripts/..."` but were not executed during the import (the source is treated as untrusted), and `requiresTools: [python3]` is not declared because Windows installs usually name the interpreter `python`, which would hide the skill there. No `run_skill` command wrappers yet.

### 154. product-discovery

- **Source:** `product-team/skills/product-discovery/SKILL.md`
- **Category:** Product. **Simplicity** 7, **Value** 3, **Total** 10. **Recommendation:** Optional pack: product.
- **What it does:** Use when validating product opportunities, mapping assumptions, planning discovery sprints, or testing problem-solution fit before committing delivery resources.
- **Overlap with mux:** none.
- **Fit in mux:** Offer it in the opt-in `product` pack as a hybrid skill whose commands run the bundled scripts. Nothing is seeded until a user installs the pack.
- **Integration cost:** 1 bundled script, standard-library Python; bundled `references/`.

- [x] Import `product-team/skills/product-discovery/SKILL.md` into `packs/product/product-discovery/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [~] Declare `requiresTools: [python3]` and confirm the 1 bundled script run from the skill folder.
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/product/product-discovery` with category `product`. Partial: the bundled scripts are called from the body as `python3 "${SKILL_DIR}/scripts/..."` but were not executed during the import (the source is treated as untrusted), and `requiresTools: [python3]` is not declared because Windows installs usually name the interpreter `python`, which would hide the skill there. No `run_skill` command wrappers yet.

### 155. product-manager-toolkit

- **Source:** `product-team/skills/product-manager-toolkit/SKILL.md`
- **Category:** Product. **Simplicity** 7, **Value** 3, **Total** 10. **Recommendation:** Optional pack: product.
- **What it does:** Comprehensive toolkit for product managers including RICE prioritization, customer interview analysis, PRD templates, discovery frameworks, and go-to-market strategies. Use when prioritizing features, synthesizing user research, writing requirement documentation, or developing product strategy.
- **Overlap with mux:** none.
- **Fit in mux:** Offer it in the opt-in `product` pack as a hybrid skill whose commands run the bundled scripts. Nothing is seeded until a user installs the pack.
- **Integration cost:** 2 bundled scripts, standard-library Python; bundled `references/`, `assets/`.

- [x] Import `product-team/skills/product-manager-toolkit/SKILL.md` into `packs/product/product-manager-toolkit/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [~] Declare `requiresTools: [python3]` and confirm the 2 bundled scripts run from the skill folder.
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/product/product-manager-toolkit` with category `product`. Partial: the bundled scripts are called from the body as `python3 "${SKILL_DIR}/scripts/..."` but were not executed during the import (the source is treated as untrusted), and `requiresTools: [python3]` is not declared because Windows installs usually name the interpreter `python`, which would hide the skill there. No `run_skill` command wrappers yet.

### 156. product-strategist

- **Source:** `product-team/skills/product-strategist/SKILL.md`
- **Category:** Product. **Simplicity** 7, **Value** 3, **Total** 10. **Recommendation:** Optional pack: product.
- **What it does:** Strategic product leadership toolkit for Head of Product covering OKR cascade generation, quarterly planning, competitive landscape analysis, product vision documents, and team scaling proposals. Use when creating quarterly OKR documents, defining product goals or KPIs, building product roadmaps, running competitive analysis, drafting team structure or hiring plans, aligning product strategy acros
- **Overlap with mux:** none.
- **Fit in mux:** Offer it in the opt-in `product` pack as a hybrid skill whose commands run the bundled scripts. Nothing is seeded until a user installs the pack.
- **Integration cost:** 1 bundled script, standard-library Python; bundled `references/`, `assets/`.

- [x] Import `product-team/skills/product-strategist/SKILL.md` into `packs/product/product-strategist/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [~] Declare `requiresTools: [python3]` and confirm the 1 bundled script run from the skill folder.
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/product/product-strategist` with category `product`. Partial: the bundled scripts are called from the body as `python3 "${SKILL_DIR}/scripts/..."` but were not executed during the import (the source is treated as untrusted), and `requiresTools: [python3]` is not declared because Windows installs usually name the interpreter `python`, which would hide the skill there. No `run_skill` command wrappers yet.

### 157. pulse

- **Source:** `research/pulse/skills/pulse/SKILL.md`
- **Category:** Research. **Simplicity** 7, **Value** 3, **Total** 10. **Recommendation:** Optional pack: research.
- **What it does:** Multi-source recency research skill that takes the pulse of any topic across Reddit, Hacker News, the open web, and optionally X/Twitter within a configurable recent window (default 30 days). Forcing intake clarifies topic specificity, angle (trend/sentiment/problems/opportunities/comparison), time window, and platform scope before searching. Returns a synthesized briefing with citations, engageme
- **Overlap with mux:** none.
- **Fit in mux:** Offer it in the opt-in `research` pack as a hybrid skill whose commands run the bundled scripts. Nothing is seeded until a user installs the pack.
- **Integration cost:** 3 bundled scripts, standard-library Python; bundled `references/`.

- [x] Import `research/pulse/skills/pulse/SKILL.md` into `packs/research/pulse/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [~] Declare `requiresTools: [python3]` and confirm the 3 bundled scripts run from the skill folder.
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/research/pulse` with category `research`. Partial: the bundled scripts are called from the body as `python3 "${SKILL_DIR}/scripts/..."` but were not executed during the import (the source is treated as untrusted), and `requiresTools: [python3]` is not declared because Windows installs usually name the interpreter `python`, which would hide the skill there. No `run_skill` command wrappers yet.

### 158. rfp-responder

- **Source:** `commercial/skills/rfp-responder/SKILL.md`
- **Category:** Commercial. **Simplicity** 7, **Value** 3, **Total** 10. **Recommendation:** Optional pack: business.
- **What it does:** Use when an RFP, RFI, RFQ, security questionnaire, vendor questionnaire, or proposal request arrives and the team needs a structured response. parsing multi-section buyer-dictated requirements (MANDATORY vs WEIGHTED vs NICE-TO-HAVE), building a Shipley-method proof-point matrix mapping each requirement to a verifiable proof point, articulating 3-5 win-themes that ladder up across requirements, an
- **Overlap with mux:** none.
- **Fit in mux:** Offer it in the opt-in `business` pack as a hybrid skill whose commands run the bundled scripts. Nothing is seeded until a user installs the pack.
- **Integration cost:** 3 bundled scripts, standard-library Python; bundled `references/`, `assets/`.

- [x] Import `commercial/skills/rfp-responder/SKILL.md` into `packs/business/rfp-responder/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [~] Declare `requiresTools: [python3]` and confirm the 3 bundled scripts run from the skill folder.
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/business/rfp-responder` with category `business`. Partial: the bundled scripts are called from the body as `python3 "${SKILL_DIR}/scripts/..."` but were not executed during the import (the source is treated as untrusted), and `requiresTools: [python3]` is not declared because Windows installs usually name the interpreter `python`, which would hide the skill there. No `run_skill` command wrappers yet.

### 159. roadmap-communicator

- **Source:** `product-team/skills/roadmap-communicator/SKILL.md`
- **Category:** Product. **Simplicity** 7, **Value** 3, **Total** 10. **Recommendation:** Optional pack: product.
- **What it does:** Use when preparing roadmap narratives, release notes, changelogs, or stakeholder updates tailored for executives, engineering teams, and customers.
- **Overlap with mux:** release-notes (partial).
- **Fit in mux:** Offer it in the opt-in `product` pack as a hybrid skill whose commands run the bundled scripts. Nothing is seeded until a user installs the pack.
- **Integration cost:** 1 bundled script, standard-library Python; bundled `references/`.

- [x] Import `product-team/skills/roadmap-communicator/SKILL.md` into `packs/product/roadmap-communicator/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [~] Declare `requiresTools: [python3]` and confirm the 1 bundled script run from the skill folder.
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/product/roadmap-communicator` with category `product`. Partial: the bundled scripts are called from the body as `python3 "${SKILL_DIR}/scripts/..."` but were not executed during the import (the source is treated as untrusted), and `requiresTools: [python3]` is not declared because Windows installs usually name the interpreter `python`, which would hide the skill there. No `run_skill` command wrappers yet.

### 160. saas-scaffolder

- **Source:** `product-team/skills/saas-scaffolder/SKILL.md`
- **Category:** Product. **Simplicity** 7, **Value** 3, **Total** 10. **Recommendation:** Optional pack: product.
- **What it does:** Generates complete, production-ready SaaS project boilerplate including authentication, database schemas, billing integration, API routes, and a working dashboard using Next.js 14+ App Router, TypeScript, Tailwind CSS, shadcn/ui, Drizzle ORM, and Stripe. Use when the user wants to create a new SaaS app, start a subscription-based web project, scaffold a Next.js application, or mentions terms like
- **Overlap with mux:** none.
- **Fit in mux:** Offer it in the opt-in `product` pack as a hybrid skill whose commands run the bundled scripts. Nothing is seeded until a user installs the pack.
- **Integration cost:** 1 bundled script, standard-library Python; bundled `references/`.

- [x] Import `product-team/skills/saas-scaffolder/SKILL.md` into `packs/product/saas-scaffolder/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [~] Declare `requiresTools: [python3]` and confirm the 1 bundled script run from the skill folder.
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/product/saas-scaffolder` with category `product`. Partial: the bundled scripts are called from the body as `python3 "${SKILL_DIR}/scripts/..."` but were not executed during the import (the source is treated as untrusted), and `requiresTools: [python3]` is not declared because Windows installs usually name the interpreter `python`, which would hide the skill there. No `run_skill` command wrappers yet.

### 161. scrum-master

- **Source:** `project-management/skills/scrum-master/SKILL.md`
- **Category:** Project management. **Simplicity** 7, **Value** 3, **Total** 10. **Recommendation:** Optional pack: product.
- **What it does:** Advanced Scrum Master skill for data-driven agile team analysis and coaching. Use when the user asks about sprint planning, velocity tracking, retrospectives, standup facilitation, backlog grooming, story points, burndown charts, blocker resolution, or agile team health. Runs Python scripts to analyse sprint JSON exports from Jira or similar tools: velocity_analyzer.py for Monte Carlo sprint forec
- **Overlap with mux:** none.
- **Fit in mux:** Offer it in the opt-in `product` pack as a hybrid skill whose commands run the bundled scripts. Nothing is seeded until a user installs the pack.
- **Integration cost:** 3 bundled scripts, standard-library Python; bundled `references/`, `assets/`.

- [x] Import `project-management/skills/scrum-master/SKILL.md` into `packs/product/scrum-master/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [~] Declare `requiresTools: [python3]` and confirm the 3 bundled scripts run from the skill folder.
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/product/scrum-master` with category `productivity`. Partial: the bundled scripts are called from the body as `python3 "${SKILL_DIR}/scripts/..."` but were not executed during the import (the source is treated as untrusted), and `requiresTools: [python3]` is not declared because Windows installs usually name the interpreter `python`, which would hide the skill there. No `run_skill` command wrappers yet.

### 162. senior-data-scientist

- **Source:** `engineering-team/skills/senior-data-scientist/SKILL.md`
- **Category:** Engineering. **Simplicity** 7, **Value** 3, **Total** 10. **Recommendation:** Optional pack: data.
- **What it does:** World-class senior data scientist skill specialising in statistical modeling, experiment design, causal inference, and predictive analytics. Covers A/B testing (sample sizing, two-proportion z-tests, Bonferroni correction), difference-in-differences, feature engineering pipelines (Scikit-learn, XGBoost), cross-validated model evaluation (AUC-ROC, AUC-PR, SHAP), and MLflow experiment tracking. usi
- **Overlap with mux:** none.
- **Fit in mux:** Offer it in the opt-in `data` pack as a hybrid skill whose commands run the bundled scripts. Nothing is seeded until a user installs the pack.
- **Integration cost:** 3 bundled scripts, standard-library Python; bundled `references/`.

- [x] Import `engineering-team/skills/senior-data-scientist/SKILL.md` into `packs/data/senior-data-scientist/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [~] Declare `requiresTools: [python3]` and confirm the 3 bundled scripts run from the skill folder.
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/data/senior-data-scientist` with category `data`. Partial: the bundled scripts are called from the body as `python3 "${SKILL_DIR}/scripts/..."` but were not executed during the import (the source is treated as untrusted), and `requiresTools: [python3]` is not declared because Windows installs usually name the interpreter `python`, which would hide the skill there. No `run_skill` command wrappers yet.

### 163. senior-ml-engineer

- **Source:** `engineering-team/skills/senior-ml-engineer/SKILL.md`
- **Category:** Engineering. **Simplicity** 7, **Value** 3, **Total** 10. **Recommendation:** Optional pack: data.
- **What it does:** ML engineering skill for productionizing models, building MLOps pipelines, and integrating LLMs. Covers model deployment, feature stores, drift monitoring, RAG systems, and cost optimization. Use when the user asks about deploying ML models to production, setting up MLOps infrastructure (MLflow, Kubeflow, Kubernetes, Docker), monitoring model performance or drift, building RAG pipelines, or integr
- **Overlap with mux:** none.
- **Fit in mux:** Offer it in the opt-in `data` pack as a hybrid skill whose commands run the bundled scripts. Nothing is seeded until a user installs the pack.
- **Integration cost:** 3 bundled scripts, standard-library Python; bundled `references/`.

- [x] Import `engineering-team/skills/senior-ml-engineer/SKILL.md` into `packs/data/senior-ml-engineer/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [~] Declare `requiresTools: [python3]` and confirm the 3 bundled scripts run from the skill folder.
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/data/senior-ml-engineer` with category `data`. Partial: the bundled scripts are called from the body as `python3 "${SKILL_DIR}/scripts/..."` but were not executed during the import (the source is treated as untrusted), and `requiresTools: [python3]` is not declared because Windows installs usually name the interpreter `python`, which would hide the skill there. No `run_skill` command wrappers yet.

### 164. senior-prompt-engineer

- **Source:** `engineering-team/skills/senior-prompt-engineer/SKILL.md`
- **Category:** Engineering. **Simplicity** 7, **Value** 3, **Total** 10. **Recommendation:** Optional pack: data.
- **What it does:** Use when the user asks to optimize prompts, design prompt templates, evaluate LLM outputs with an eval set, measure RAG retrieval quality, validate agent/tool configurations, analyze token usage, or design structured-output contracts. Covers eval-driven prompt iteration, RAG metrics (relevance, faithfulness, coverage), agent workflow validation, and token/cost budgeting. all model-agnostic, with
- **Overlap with mux:** none.
- **Fit in mux:** Offer it in the opt-in `data` pack as a hybrid skill whose commands run the bundled scripts. Nothing is seeded until a user installs the pack.
- **Integration cost:** 3 bundled scripts, standard-library Python; bundled `references/`.

- [x] Import `engineering-team/skills/senior-prompt-engineer/SKILL.md` into `packs/data/senior-prompt-engineer/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [~] Declare `requiresTools: [python3]` and confirm the 3 bundled scripts run from the skill folder.
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/data/senior-prompt-engineer` with category `workflow`. Partial: the bundled scripts are called from the body as `python3 "${SKILL_DIR}/scripts/..."` but were not executed during the import (the source is treated as untrusted), and `requiresTools: [python3]` is not declared because Windows installs usually name the interpreter `python`, which would hide the skill there. No `run_skill` command wrappers yet.

### 165. snowflake-development

- **Source:** `engineering-team/snowflake-development/skills/snowflake-development/SKILL.md`
- **Category:** Engineering. **Simplicity** 7, **Value** 3, **Total** 10. **Recommendation:** Optional pack: engineering.
- **What it does:** Use when writing Snowflake SQL, building data pipelines with Dynamic Tables or Streams/Tasks, using Cortex AI functions, creating Cortex Agents, writing Snowpark Python, configuring dbt for Snowflake, or troubleshooting Snowflake errors.
- **Overlap with mux:** none.
- **Fit in mux:** Offer it in the opt-in `engineering` pack as a hybrid skill whose commands run the bundled scripts. Nothing is seeded until a user installs the pack.
- **Integration cost:** 1 bundled script, standard-library Python; bundled `references/`.

- [x] Import `engineering-team/snowflake-development/skills/snowflake-development/SKILL.md` into `packs/engineering/snowflake-development/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [~] Declare `requiresTools: [python3]` and confirm the 1 bundled script run from the skill folder.
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/engineering/snowflake-development` with category `data`. Partial: the bundled scripts are called from the body as `python3 "${SKILL_DIR}/scripts/..."` but were not executed during the import (the source is treated as untrusted), and `requiresTools: [python3]` is not declared because Windows installs usually name the interpreter `python`, which would hide the skill there. No `run_skill` command wrappers yet.

### 166. statistical-analyst

- **Source:** `engineering/statistical-analyst/skills/statistical-analyst/SKILL.md`
- **Category:** Engineering. **Simplicity** 7, **Value** 3, **Total** 10. **Recommendation:** Optional pack: data.
- **What it does:** Run hypothesis tests, analyze A/B experiment results, calculate sample sizes, and interpret statistical significance with effect sizes. Use when you need to validate whether observed differences are real, size an experiment correctly before launch, or interpret test results with confidence.
- **Overlap with mux:** none.
- **Fit in mux:** Offer it in the opt-in `data` pack as a hybrid skill whose commands run the bundled scripts. Nothing is seeded until a user installs the pack.
- **Integration cost:** 3 bundled scripts, standard-library Python; bundled `references/`.

- [x] Import `engineering/statistical-analyst/skills/statistical-analyst/SKILL.md` into `packs/data/statistical-analyst/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [~] Declare `requiresTools: [python3]` and confirm the 3 bundled scripts run from the skill folder.
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/data/statistical-analyst` with category `data`. Partial: the bundled scripts are called from the body as `python3 "${SKILL_DIR}/scripts/..."` but were not executed during the import (the source is treated as untrusted), and `requiresTools: [python3]` is not declared because Windows installs usually name the interpreter `python`, which would hide the skill there. No `run_skill` command wrappers yet.

### 167. ui-design-system

- **Source:** `product-team/skills/ui-design-system/SKILL.md`
- **Category:** Product. **Simplicity** 7, **Value** 3, **Total** 10. **Recommendation:** Optional pack: product.
- **What it does:** UI design system toolkit for Senior UI Designer including design token generation, component documentation, responsive design calculations, and developer handoff tools. Use when creating design systems, generating design tokens, maintaining visual consistency, or facilitating design-dev collaboration and developer handoff.
- **Overlap with mux:** none.
- **Fit in mux:** Offer it in the opt-in `product` pack as a hybrid skill whose commands run the bundled scripts. Nothing is seeded until a user installs the pack.
- **Integration cost:** 1 bundled script, standard-library Python; bundled `references/`, `assets/`.

- [x] Import `product-team/skills/ui-design-system/SKILL.md` into `packs/product/ui-design-system/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [~] Declare `requiresTools: [python3]` and confirm the 1 bundled script run from the skill folder.
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/product/ui-design-system` with category `product`. Partial: the bundled scripts are called from the body as `python3 "${SKILL_DIR}/scripts/..."` but were not executed during the import (the source is treated as untrusted), and `requiresTools: [python3]` is not declared because Windows installs usually name the interpreter `python`, which would hide the skill there. No `run_skill` command wrappers yet.

### 168. universal-scraping-architect

- **Source:** `engineering/universal-scraping-architect/skills/universal-scraping-architect/SKILL.md`
- **Category:** Engineering. **Simplicity** 7, **Value** 3, **Total** 10. **Recommendation:** Optional pack: data.
- **What it does:** Use for web scraping, crawling, document extraction, API parsing, or building validation-heavy data pipelines using Firecrawl or local Python scripts.
- **Overlap with mux:** none.
- **Fit in mux:** Offer it in the opt-in `data` pack as a hybrid skill whose commands run the bundled scripts. Nothing is seeded until a user installs the pack.
- **Integration cost:** 3 bundled scripts, standard-library Python; bundled `references/`.
- **Assessment:** Depends on Firecrawl or scraping libraries; mux web_retrieve covers simple cases.

- [x] Import `engineering/universal-scraping-architect/skills/universal-scraping-architect/SKILL.md` into `packs/data/universal-scraping-architect/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [~] Declare `requiresTools: [python3]` and confirm the 3 bundled scripts run from the skill folder.
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/data/universal-scraping-architect` with category `engineering`. Partial: the bundled scripts are called from the body as `python3 "${SKILL_DIR}/scripts/..."` but were not executed during the import (the source is treated as untrusted), and `requiresTools: [python3]` is not declared because Windows installs usually name the interpreter `python`, which would hide the skill there. No `run_skill` command wrappers yet.

### 169. ai-act-readiness

- **Source:** `compliance-os/skills/ai-act-readiness/SKILL.md`
- **Category:** Compliance. **Simplicity** 8, **Value** 2, **Total** 10. **Recommendation:** Optional pack: compliance.
- **What it does:** EU AI Act 6-question forcing interrogation. Use during AI-system intake, before EU deployment, or during annual compliance refresh as Article 113 obligations phase in (2025-02-02 / 2025-08-02 / 2026-08-02 / 2027-08-02).
- **Overlap with mux:** none.
- **Fit in mux:** Offer it in the opt-in `compliance` pack as a playbook (instructions only). Nothing is seeded until a user installs the pack.
- **Integration cost:** reads files from sibling skills.

- [x] Import `compliance-os/skills/ai-act-readiness/SKILL.md` into `packs/compliance/ai-act-readiness/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/compliance/ai-act-readiness` with category `compliance`.

### 170. aims-audit

- **Source:** `compliance-os/skills/aims-audit/SKILL.md`
- **Category:** Compliance. **Simplicity** 8, **Value** 2, **Total** 10. **Recommendation:** Optional pack: compliance.
- **What it does:** ISO/IEC 42001 AIMS internal-audit 6-question forcing interrogation. Use before certification stage 1, before annual internal audit cycles, or when onboarding a new AI system into an existing AIMS.
- **Overlap with mux:** none.
- **Fit in mux:** Offer it in the opt-in `compliance` pack as a playbook (instructions only). Nothing is seeded until a user installs the pack.
- **Integration cost:** reads files from sibling skills.

- [x] Import `compliance-os/skills/aims-audit/SKILL.md` into `packs/compliance/aims-audit/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/compliance/aims-audit` with category `compliance`.

### 171. board-meeting

- **Source:** `c-level-advisor/skills/board-meeting/SKILL.md`
- **Category:** Executive advisory. **Simplicity** 8, **Value** 2, **Total** 10. **Recommendation:** Optional pack: business.
- **What it does:** Multi-agent board meeting protocol for strategic decisions. Runs a structured 6-phase deliberation: context loading, independent C-suite contributions (isolated, no cross-pollination), critic analysis, synthesis, founder review, and decision extraction. Use when the user invokes /cs:boardroom, calls a board meeting, or wants structured multi-perspective executive deliberation on a strategic questi
- **Overlap with mux:** none.
- **Fit in mux:** Offer it in the opt-in `business` pack as a playbook (instructions only). Nothing is seeded until a user installs the pack.
- **Integration cost:** references Claude Code files or conventions (~/.claude) that must be rewritten for mux; bundled `references/`, `templates/`.

- [x] Import `c-level-advisor/skills/board-meeting/SKILL.md` into `packs/business/board-meeting/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/business/board-meeting` with category `business`.

### 172. boardroom

- **Source:** `c-level-agents/skills/boardroom/SKILL.md`
- **Category:** Executive advisory. **Simplicity** 8, **Value** 2, **Total** 10. **Recommendation:** Optional pack: business.
- **What it does:** 6-phase multi-role deliberation across the C-suite with Phase 2 isolation, critic pre-screen, and synthesis. Outputs a board memo. Use when a decision spans multiple executive domains. e.g. a pricing change touching finance, positioning, and product, or a raise-vs-cut runway call.
- **Overlap with mux:** none.
- **Fit in mux:** Offer it in the opt-in `business` pack as a playbook (instructions only). Nothing is seeded until a user installs the pack.
- **Integration cost:** references Claude Code files or conventions (~/.claude) that must be rewritten for mux.

- [x] Import `c-level-agents/skills/boardroom/SKILL.md` into `packs/business/boardroom/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/business/boardroom` with category `business`.

### 173. brand-guidelines

- **Source:** `marketing-skill/skills/brand-guidelines/SKILL.md`
- **Category:** Marketing. **Simplicity** 8, **Value** 2, **Total** 10. **Recommendation:** Optional pack: marketing.
- **What it does:** When the user wants to apply, document, or enforce brand guidelines for any product or company. Also use when the user mentions 'brand guidelines,' 'brand colors,' 'typography,' 'logo usage,' 'brand voice,' 'visual identity,' 'tone of voice,' 'brand standards,' 'style guide,' 'brand consistency,' or 'company design standards.' Covers color systems, typography, logo rules, imagery guidelines, and t
- **Overlap with mux:** none.
- **Fit in mux:** Offer it in the opt-in `marketing` pack as a playbook (instructions only). Nothing is seeded until a user installs the pack.
- **Integration cost:** references Claude Code files or conventions (.claude/) that must be rewritten for mux; bundled `references/`.

- [x] Import `marketing-skill/skills/brand-guidelines/SKILL.md` into `packs/marketing/brand-guidelines/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/marketing/brand-guidelines` with category `marketing`.

### 174. brief

- **Source:** `c-level-agents/skills/brief/SKILL.md`
- **Category:** Executive advisory. **Simplicity** 8, **Value** 2, **Total** 10. **Recommendation:** Optional pack: business.
- **What it does:** Generate a one-page strategy brief from an office-hours intake. First step in the strategic sprint pipeline. Use when a strategic question needs to be framed before boardroom deliberation. e.g. locking options, assumptions, and success criteria for a pricing change or a market-entry decision.
- **Overlap with mux:** none.
- **Fit in mux:** Offer it in the opt-in `business` pack as a playbook (instructions only). Nothing is seeded until a user installs the pack.
- **Integration cost:** references Claude Code files or conventions (~/.claude) that must be rewritten for mux.

- [x] Import `c-level-agents/skills/brief/SKILL.md` into `packs/business/brief/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/business/exec-brief` with category `business`. Shipped under the id `exec-brief` (the original name `brief` was too generic or shared with another skill).

### 175. chief-of-staff

- **Source:** `c-level-advisor/skills/chief-of-staff/SKILL.md`
- **Category:** Executive advisory. **Simplicity** 8, **Value** 2, **Total** 10. **Recommendation:** Optional pack: business.
- **What it does:** C-suite orchestration layer. Routes founder questions to the right advisor role(s), triggers multi-role board meetings for complex decisions, synthesizes outputs, and tracks decisions. Every C-suite interaction starts here. Loads company context automatically. Use when a founder question needs routing to the right advisor. e.g. 'should we raise now or cut burn?'. or when a multi-domain decision
- **Overlap with mux:** none.
- **Fit in mux:** Offer it in the opt-in `business` pack as a playbook (instructions only). Nothing is seeded until a user installs the pack.
- **Integration cost:** references Claude Code files or conventions (~/.claude) that must be rewritten for mux; bundled `references/`.

- [x] Import `c-level-advisor/skills/chief-of-staff/SKILL.md` into `packs/business/chief-of-staff/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/business/chief-of-staff` with category `business`.

### 176. compliance-readiness

- **Source:** `compliance-os/skills/compliance-readiness/SKILL.md`
- **Category:** Compliance. **Simplicity** 8, **Value** 2, **Total** 10. **Recommendation:** Optional pack: compliance.
- **What it does:** Multi-framework compliance officer 6-question forcing interrogation of any compliance program. Use before starting a new framework, planning the annual audit calendar, or preparing for certification stage 1.
- **Overlap with mux:** none.
- **Fit in mux:** Offer it in the opt-in `compliance` pack as a playbook (instructions only). Nothing is seeded until a user installs the pack.
- **Integration cost:** reads files from sibling skills.

- [x] Import `compliance-os/skills/compliance-readiness/SKILL.md` into `packs/compliance/compliance-readiness/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/compliance/compliance-readiness` with category `compliance`.

### 177. context-engine

- **Source:** `c-level-advisor/skills/context-engine/SKILL.md`
- **Category:** Executive advisory. **Simplicity** 8, **Value** 2, **Total** 10. **Recommendation:** Optional pack: business.
- **What it does:** Loads and manages company context for all C-suite advisor skills. Reads ~/.claude/company-context.md, detects stale context (>90 days), enriches context during conversations, and enforces privacy/anonymization rules before external API calls. Use when starting any C-suite advisor session, when context looks stale or missing, or before sending company data to an external service.
- **Overlap with mux:** none.
- **Fit in mux:** Offer it in the opt-in `business` pack as a playbook (instructions only). Nothing is seeded until a user installs the pack.
- **Integration cost:** references Claude Code files or conventions (~/.claude) that must be rewritten for mux; bundled `references/`.

- [x] Import `c-level-advisor/skills/context-engine/SKILL.md` into `packs/business/context-engine/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/business/context-engine` with category `business`.

### 178. contract-and-proposal-writer

- **Source:** `business-growth/skills/contract-and-proposal-writer/SKILL.md`
- **Category:** Business growth. **Simplicity** 8, **Value** 2, **Total** 10. **Recommendation:** Optional pack: business.
- **What it does:** Generate professional, jurisdiction-aware business documents: freelance contracts, project proposals, SOWs, NDAs, and MSAs. Structured Markdown output with docx conversion instructions. Covers US (Delaware), EU (GDPR), UK, and DACH (German law) jurisdictions. Not a substitute for legal counsel. use as strong starting points. Use when drafting a freelance contract, preparing a client proposal, wri
- **Overlap with mux:** none.
- **Fit in mux:** Offer it in the opt-in `business` pack as a playbook (instructions only). Nothing is seeded until a user installs the pack.
- **Integration cost:** 423 lines, long for small models.

- [x] Import `business-growth/skills/contract-and-proposal-writer/SKILL.md` into `packs/business/contract-and-proposal-writer/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/business/contract-and-proposal-writer` with category `business`.

### 179. cross-eval

- **Source:** `c-level-agents/skills/cross-eval/SKILL.md`
- **Category:** Executive advisory. **Simplicity** 8, **Value** 2, **Total** 10. **Recommendation:** Optional pack: business.
- **What it does:** Multi-model consensus on a board memo or strategy brief. Claude + Codex + Gemini cross-review with graceful degradation. Use when a high-stakes memo needs an independent sanity check before the boardroom. e.g. a bet-the-company pivot or fundraise terms.
- **Overlap with mux:** none.
- **Fit in mux:** Offer it in the opt-in `business` pack as a playbook (instructions only). Nothing is seeded until a user installs the pack.
- **Integration cost:** references Claude Code files or conventions (~/.claude) that must be rewritten for mux.

- [x] Import `c-level-agents/skills/cross-eval/SKILL.md` into `packs/business/cross-eval/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/business/cross-eval` with category `business`.

### 180. cs-onboard

- **Source:** `c-level-advisor/skills/cs-onboard/SKILL.md`
- **Category:** Executive advisory. **Simplicity** 8, **Value** 2, **Total** 10. **Recommendation:** Optional pack: business.
- **What it does:** Founder onboarding interview that captures company context across 7 dimensions. Invoke with /cs:setup for initial interview or /cs:update for quarterly refresh. Generates ~/.claude/company-context.md used by all C-suite advisor skills. Use when setting up the C-suite advisors for the first time, or when company context is missing or more than 90 days old. e.g. after a fundraise or pivot.
- **Overlap with mux:** none.
- **Fit in mux:** Offer it in the opt-in `business` pack as a playbook (instructions only). Nothing is seeded until a user installs the pack.
- **Integration cost:** references Claude Code files or conventions (~/.claude) that must be rewritten for mux; bundled `references/`, `templates/`.

- [x] Import `c-level-advisor/skills/cs-onboard/SKILL.md` into `packs/business/cs-onboard/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/business/cs-onboard` with category `business`.

### 181. decide

- **Source:** `c-level-agents/skills/decide/SKILL.md`
- **Category:** Executive advisory. **Simplicity** 8, **Value** 2, **Total** 10. **Recommendation:** Optional pack: business.
- **What it does:** Log a decision to two-layer memory via decision-logger. Approved memo becomes durable; raw transcripts kept for reference. Use when the founder has approved a boardroom memo and the decision must become durable company memory. e.g. right after /cs:boardroom concludes.
- **Overlap with mux:** none.
- **Fit in mux:** Offer it in the opt-in `business` pack as a playbook (instructions only). Nothing is seeded until a user installs the pack.
- **Integration cost:** references Claude Code files or conventions (~/.claude) that must be rewritten for mux.

- [x] Import `c-level-agents/skills/decide/SKILL.md` into `packs/business/decide/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/business/exec-decide` with category `business`. Shipped under the id `exec-decide` (the original name `decide` was too generic or shared with another skill).

### 182. execute

- **Source:** `c-level-agents/skills/execute/SKILL.md`
- **Category:** Executive advisory. **Simplicity** 8, **Value** 2, **Total** 10. **Recommendation:** Optional pack: business.
- **What it does:** Generate a 90-day execution plan with weekly milestones, DRIs, and check-in cadence from an approved decision. Use when a logged decision needs to become an operating plan. e.g. turning an approved market-entry call into weekly milestones with DRIs.
- **Overlap with mux:** none.
- **Fit in mux:** Offer it in the opt-in `business` pack as a playbook (instructions only). Nothing is seeded until a user installs the pack.
- **Integration cost:** references Claude Code files or conventions (~/.claude) that must be rewritten for mux.

- [x] Import `c-level-agents/skills/execute/SKILL.md` into `packs/business/execute/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/business/exec-execute` with category `business`. Shipped under the id `exec-execute` (the original name `execute` was too generic or shared with another skill).

### 183. fda-qsr-audit-prep

- **Source:** `compliance-os/skills/fda-qsr-audit-prep/SKILL.md`
- **Category:** Compliance. **Simplicity** 8, **Value** 2, **Total** 10. **Recommendation:** Optional pack: compliance.
- **What it does:** FDA 21 CFR 820 (QSR / QMSR) audit 6-question forcing interrogation. Post-Feb 2026 substantially harmonized with ISO 13485. Use before annual internal QSR audit, pre-FDA-inspection readiness, or Form 483 response.
- **Overlap with mux:** none.
- **Fit in mux:** Offer it in the opt-in `compliance` pack as a playbook (instructions only). Nothing is seeded until a user installs the pack.
- **Integration cost:** reads files from sibling skills.

- [x] Import `compliance-os/skills/fda-qsr-audit-prep/SKILL.md` into `packs/compliance/fda-qsr-audit-prep/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/compliance/fda-qsr-audit-prep` with category `compliance`.

### 184. founder-mode

- **Source:** `c-level-agents/skills/founder-mode/SKILL.md`
- **Category:** Executive advisory. **Simplicity** 8, **Value** 2, **Total** 10. **Recommendation:** Optional pack: business.
- **What it does:** Auto-routes any founder question to the right C-role advisor or to /cs:boardroom for multi-role topics. The single-command entry point. Use when a founder asks any strategic question without knowing which advisor or command fits. e.g. 'runway pressure' routes to the CFO, 'gross retention dropped' routes to the CCO.
- **Overlap with mux:** none.
- **Fit in mux:** Offer it in the opt-in `business` pack as a playbook (instructions only). Nothing is seeded until a user installs the pack.
- **Integration cost:** references Claude Code files or conventions (~/.claude) that must be rewritten for mux.

- [x] Import `c-level-agents/skills/founder-mode/SKILL.md` into `packs/business/founder-mode/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/business/founder-mode` with category `business`.

### 185. freeze

- **Source:** `c-level-agents/skills/freeze/SKILL.md`
- **Category:** Executive advisory. **Simplicity** 8, **Value** 2, **Total** 10. **Recommendation:** Optional pack: business.
- **What it does:** Lock a strategic decision for a cooldown period to prevent impulse reversal. Mirrors gstack's safety primitives for the business layer. Use when an irreversible decision was made under pressure. e.g. a layoff plan or multi-year contract. and deserves a cooling-off lock before execution.
- **Overlap with mux:** none.
- **Fit in mux:** Offer it in the opt-in `business` pack as a playbook (instructions only). Nothing is seeded until a user installs the pack.
- **Integration cost:** references Claude Code files or conventions (~/.claude) that must be rewritten for mux.

- [x] Import `c-level-agents/skills/freeze/SKILL.md` into `packs/business/freeze/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/business/exec-freeze` with category `business`. Shipped under the id `exec-freeze` (the original name `freeze` was too generic or shared with another skill).

### 186. gdpr-audit-prep

- **Source:** `compliance-os/skills/gdpr-audit-prep/SKILL.md`
- **Category:** Compliance. **Simplicity** 8, **Value** 2, **Total** 10. **Recommendation:** Optional pack: compliance.
- **What it does:** GDPR audit 6-question Article-cited forcing interrogation. Use before annual internal GDPR review, post-breach internal audit, DPA investigation readiness, or acquisition due diligence.
- **Overlap with mux:** none.
- **Fit in mux:** Offer it in the opt-in `compliance` pack as a playbook (instructions only). Nothing is seeded until a user installs the pack.
- **Integration cost:** reads files from sibling skills.

- [x] Import `compliance-os/skills/gdpr-audit-prep/SKILL.md` into `packs/compliance/gdpr-audit-prep/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/compliance/gdpr-audit-prep` with category `compliance`.

### 187. iso13485-audit-prep

- **Source:** `compliance-os/skills/iso13485-audit-prep/SKILL.md`
- **Category:** Compliance. **Simplicity** 8, **Value** 2, **Total** 10. **Recommendation:** Optional pack: compliance.
- **What it does:** ISO 13485 QMS audit 6-question forcing interrogation. Design controls + CAPA + post-market focused. Use before Clause 8.2.4 internal audit, MDR / FDA QSR alignment review, or product-launch DHF closure audit.
- **Overlap with mux:** none.
- **Fit in mux:** Offer it in the opt-in `compliance` pack as a playbook (instructions only). Nothing is seeded until a user installs the pack.
- **Integration cost:** reads files from sibling skills.

- [x] Import `compliance-os/skills/iso13485-audit-prep/SKILL.md` into `packs/compliance/iso13485-audit-prep/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/compliance/iso13485-audit-prep` with category `compliance`.

### 188. iso27001-audit-prep

- **Source:** `compliance-os/skills/iso27001-audit-prep/SKILL.md`
- **Category:** Compliance. **Simplicity** 8, **Value** 2, **Total** 10. **Recommendation:** Optional pack: compliance.
- **What it does:** ISO 27001 ISMS audit readiness 6-question forcing interrogation. Use before annual Clause 9.2 internal audit, surveillance audit prep, or stage 1 certification readiness.
- **Overlap with mux:** none.
- **Fit in mux:** Offer it in the opt-in `compliance` pack as a playbook (instructions only). Nothing is seeded until a user installs the pack.
- **Integration cost:** reads files from sibling skills.

- [x] Import `compliance-os/skills/iso27001-audit-prep/SKILL.md` into `packs/compliance/iso27001-audit-prep/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/compliance/iso27001-audit-prep` with category `compliance`.

### 189. marketing-ideas

- **Source:** `marketing-skill/skills/marketing-ideas/SKILL.md`
- **Category:** Marketing. **Simplicity** 8, **Value** 2, **Total** 10. **Recommendation:** Optional pack: marketing.
- **What it does:** When the user needs marketing ideas, inspiration, or strategies for their SaaS or software product. Also use when the user asks for 'marketing ideas,' 'growth ideas,' 'how to market,' 'marketing strategies,' 'marketing tactics,' 'ways to promote,' or 'ideas to grow.' This skill provides 139 proven marketing approaches organized by category.
- **Overlap with mux:** none.
- **Fit in mux:** Offer it in the opt-in `marketing` pack as a playbook (instructions only). Nothing is seeded until a user installs the pack.
- **Integration cost:** references Claude Code files or conventions (.claude/) that must be rewritten for mux; bundled `references/`.

- [x] Import `marketing-skill/skills/marketing-ideas/SKILL.md` into `packs/marketing/marketing-ideas/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/marketing/marketing-ideas` with category `marketing`.

### 190. marketing-psychology

- **Source:** `marketing-skill/skills/marketing-psychology/SKILL.md`
- **Category:** Marketing. **Simplicity** 8, **Value** 2, **Total** 10. **Recommendation:** Optional pack: marketing.
- **What it does:** When the user wants to apply psychological principles, mental models, or behavioral science to marketing. Also use when the user mentions 'psychology,' 'mental models,' 'cognitive bias,' 'persuasion,' 'behavioral science,' 'why people buy,' 'decision-making,' or 'consumer behavior.' This skill provides 70+ mental models organized for marketing application.
- **Overlap with mux:** none.
- **Fit in mux:** Offer it in the opt-in `marketing` pack as a playbook (instructions only). Nothing is seeded until a user installs the pack.
- **Integration cost:** references Claude Code files or conventions (.claude/) that must be rewritten for mux; bundled `references/`.

- [x] Import `marketing-skill/skills/marketing-psychology/SKILL.md` into `packs/marketing/marketing-psychology/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/marketing/marketing-psychology` with category `marketing`.

### 191. onboard

- **Source:** `c-level-agents/skills/onboard/SKILL.md`
- **Category:** Executive advisory. **Simplicity** 8, **Value** 2, **Total** 10. **Recommendation:** Optional pack: business.
- **What it does:** Founder interview that populates ~/.claude/company-context.md using the canonical 7-dimension cs-onboard schema. The first command to run when starting with c-level-agents. Use when setting up the virtual C-suite for a new company, or when advisors lack company context. e.g. before a first /cs:boardroom or after a fundraise changes the numbers.
- **Overlap with mux:** none.
- **Fit in mux:** Offer it in the opt-in `business` pack as a playbook (instructions only). Nothing is seeded until a user installs the pack.
- **Integration cost:** references Claude Code files or conventions (~/.claude) that must be rewritten for mux.

- [x] Import `c-level-agents/skills/onboard/SKILL.md` into `packs/business/onboard/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/business/exec-onboard` with category `business`. Shipped under the id `exec-onboard` (the original name `onboard` was too generic or shared with another skill).

### 192. paywall-upgrade-cro

- **Source:** `marketing-skill/skills/paywall-upgrade-cro/SKILL.md`
- **Category:** Marketing. **Simplicity** 8, **Value** 2, **Total** 10. **Recommendation:** Optional pack: marketing.
- **What it does:** When the user wants to create or optimize in-app paywalls, upgrade screens, upsell modals, or feature gates. Also use when the user mentions "paywall," "upgrade screen," "upgrade modal," "upsell," "feature gate," "convert free to paid," "freemium conversion," "trial expiration screen," "limit reached screen," "plan upgrade prompt," or "in-app pricing." Distinct from public pricing pages (see page-
- **Overlap with mux:** none.
- **Fit in mux:** Offer it in the opt-in `marketing` pack as a playbook (instructions only). Nothing is seeded until a user installs the pack.
- **Integration cost:** references Claude Code files or conventions (.claude/) that must be rewritten for mux.

- [x] Import `marketing-skill/skills/paywall-upgrade-cro/SKILL.md` into `packs/marketing/paywall-upgrade-cro/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/marketing/paywall-upgrade-cro` with category `marketing`.

### 193. popup-cro

- **Source:** `marketing-skill/skills/popup-cro/SKILL.md`
- **Category:** Marketing. **Simplicity** 8, **Value** 2, **Total** 10. **Recommendation:** Optional pack: marketing.
- **What it does:** When the user wants to create or optimize popups, modals, overlays, slide-ins, or banners for conversion purposes. Also use when the user mentions "exit intent," "popup conversions," "modal optimization," "lead capture popup," "email popup," "announcement banner," or "overlay." For forms outside of popups, see form-cro. For general page conversion optimization, see page-cro.
- **Overlap with mux:** none.
- **Fit in mux:** Offer it in the opt-in `marketing` pack as a playbook (instructions only). Nothing is seeded until a user installs the pack.
- **Integration cost:** references Claude Code files or conventions (.claude/) that must be rewritten for mux; bundled `references/`.

- [x] Import `marketing-skill/skills/popup-cro/SKILL.md` into `packs/marketing/popup-cro/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/marketing/popup-cro` with category `marketing`.

### 194. post-mortem

- **Source:** `c-level-agents/skills/post-mortem/SKILL.md`
- **Category:** Executive advisory. **Simplicity** 8, **Value** 2, **Total** 10. **Recommendation:** Optional pack: business.
- **What it does:** Honest retrospective on an executed decision, scored against original assumptions and dissent. Closes the strategic sprint loop. Use when a decision hits its 90-day review checkpoint or its kill criteria trigger. e.g. scoring last quarter's pricing change against its pre-committed success metrics.
- **Overlap with mux:** none.
- **Fit in mux:** Offer it in the opt-in `business` pack as a playbook (instructions only). Nothing is seeded until a user installs the pack.
- **Integration cost:** references Claude Code files or conventions (~/.claude) that must be rewritten for mux.

- [x] Import `c-level-agents/skills/post-mortem/SKILL.md` into `packs/business/post-mortem/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/business/exec-post-mortem` with category `business`. Shipped under the id `exec-post-mortem` (the original name `post-mortem` was too generic or shared with another skill).

### 195. soc2-audit-prep

- **Source:** `compliance-os/skills/soc2-audit-prep/SKILL.md`
- **Category:** Compliance. **Simplicity** 8, **Value** 2, **Total** 10. **Recommendation:** Optional pack: compliance.
- **What it does:** SOC 2 Type II readiness 6-question forcing interrogation. Observation-period focused. Use before Type II observation begins, mid-period checkpoint, or pre-field-test month-10 readiness.
- **Overlap with mux:** none.
- **Fit in mux:** Offer it in the opt-in `compliance` pack as a playbook (instructions only). Nothing is seeded until a user installs the pack.
- **Integration cost:** reads files from sibling skills.

- [x] Import `compliance-os/skills/soc2-audit-prep/SKILL.md` into `packs/compliance/soc2-audit-prep/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/compliance/soc2-audit-prep` with category `compliance`.

### 196. social-content

- **Source:** `marketing-skill/skills/social-content/SKILL.md`
- **Category:** Marketing. **Simplicity** 8, **Value** 2, **Total** 10. **Recommendation:** Optional pack: marketing.
- **What it does:** When the user wants help creating, scheduling, or optimizing social media content for LinkedIn, Twitter/X, Instagram, TikTok, Facebook, or other platforms. Also use when the user mentions 'LinkedIn post,' 'Twitter thread,' 'social media,' 'content calendar,' 'social scheduling,' 'engagement,' or 'viral content.' This skill covers content creation, repurposing, and platform-specific strategies.
- **Overlap with mux:** none.
- **Fit in mux:** Offer it in the opt-in `marketing` pack as a playbook (instructions only). Nothing is seeded until a user installs the pack.
- **Integration cost:** references Claude Code files or conventions (.claude/) that must be rewritten for mux; bundled `references/`.

- [x] Import `marketing-skill/skills/social-content/SKILL.md` into `packs/marketing/social-content/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/marketing/social-content` with category `marketing`.

### 197. video-content-strategist

- **Source:** `marketing-skill/video-content-strategist/skills/video-content-strategist/SKILL.md`
- **Category:** Marketing. **Simplicity** 8, **Value** 2, **Total** 10. **Recommendation:** Optional pack: marketing.
- **What it does:** Use when planning video content strategy, writing video scripts, optimizing YouTube channels, building short-form video pipelines (Reels, TikTok, Shorts), or repurposing long-form content into video. Triggers: 'start a YouTube channel', 'video content strategy', 'write a video script', 'repurpose into video', 'YouTube SEO', 'short-form video'. NOT for written blog content (use content-production).
- **Overlap with mux:** none.
- **Fit in mux:** Offer it in the opt-in `marketing` pack as a playbook (instructions only). Nothing is seeded until a user installs the pack.
- **Integration cost:** references Claude Code files or conventions (.claude/) that must be rewritten for mux.

- [x] Import `marketing-skill/video-content-strategist/skills/video-content-strategist/SKILL.md` into `packs/marketing/video-content-strategist/` through the Phase 0 importer (path placeholders, Claude references, and em-dashes normalized).
- [x] Run `mux skill validate` on the imported folder and add it to the pack's load test.
- [x] List it in the pack README with one line on when to use it.

**Notes:** Landed 2026-10-09 in `src/Mux.Core/Skills/Packs/marketing/video-content-strategist` with category `marketing`.

