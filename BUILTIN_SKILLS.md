# Built-in Skills

_Generated from mux's default skill library and its bundled skill packs. Regenerate it when skills change._

mux ships with **170 default skills**, seeded into `~/.mux/skills` on first run and topped up on upgrade without overwriting your edits, and **166 more in 10 opt-in packs** that you install when you want them. Every skill has a category; you can override it on any surface (`mux skill category <name> <category>`, `/skills`, the web dashboard, the desktop app, or VS Code) without editing its SKILL.md.

Most skills are listed to the model only where they apply: a skill can declare `appliesTo` file globs (for example `package.json` or `.github/workflows/*.yml`) and `requiresTools` (for example `gh` or `kubectl`), so a Python project never sees the Java skills. Run any skill by name with `/<skill> [arguments]`, list them with `mux skill list` (add `--category <name>` to filter), and read one with `mux skill show <name>`. **Commands** is the number of deterministic commands a skill offers through `run_skill`; a skill with 0 commands is a playbook the model follows with its normal tools. **Origin** is `mux` for skills written for mux and `claude-skills` for skills adapted from [alirezarezvani/claude-skills](https://github.com/alirezarezvani/claude-skills) (MIT; see THIRD_PARTY_NOTICES.md).

## Contents

- [cloud](#cloud) (39)
- [containers](#containers) (4)
- [debugging](#debugging) (2)
- [devops](#devops) (4)
- [docs](#docs) (6)
- [frontend](#frontend) (8)
- [git](#git) (15)
- [hygiene](#hygiene) (9)
- [infrastructure](#infrastructure) (2)
- [kubernetes](#kubernetes) (6)
- [languages](#languages) (42)
- [loops](#loops) (4)
- [productivity](#productivity) (1)
- [review](#review) (6)
- [scaffolding](#scaffolding) (4)
- [security](#security) (4)
- [testing](#testing) (8)
- [workflow](#workflow) (6)
- [Optional packs](#optional-packs) (166)

## Default skills by category

### cloud

| Skill | What it does | Commands | Origin |
|---|---|---:|---|
| `alibaba-infra` | Lists ECS instances, OSS objects, ACK clusters, ApsaraDB RDS instances, and Function Compute functions. | 5 | mux |
| `alibaba-whoami` | Shows the configured profile and the available regions. | 2 | mux |
| `aws-compute` | Lists and describes EC2 instances, starts or stops them, and lists, invokes, or tails Lambda functions. | 7 | mux |
| `aws-containers` | Lists ECS services and tasks, redeploys a service, lists EKS clusters and writes kubeconfig, and works with ECR repositories. | 8 | mux |
| `aws-data` | Lists RDS instances and snapshots, DynamoDB tables, and ElastiCache clusters. | 5 | mux |
| `aws-deploy` | Lists stacks and events, previews change sets, and deploys SAM or CDK apps after a diff. | 8 | mux |
| `aws-integration` | Lists SQS queues and their depth, SNS topics, secret and parameter names, Route 53 zones, and the caller's IAM policies. | 7 | mux |
| `aws-observe` | Lists log groups, shows recent log events, lists alarms in ALARM state, and reports month-to-date cost by service. | 4 | mux |
| `aws-storage` | Lists buckets and objects, syncs folders with a dry run first, and creates presigned URLs. | 4 | mux |
| `aws-whoami` | Shows the caller identity, configured profiles, and available regions. | 3 | mux |
| `azure-apps` | Lists and shows web apps and function apps, deploys a package to a web app, and previews or deploys an azd project. | 6 | mux |
| `azure-compute` | Lists VMs and starts, stops, or deallocates one. | 4 | mux |
| `azure-containers` | Lists AKS clusters and writes kubeconfig, works with ACR, and lists, reads logs of, or updates Container Apps. | 7 | mux |
| `azure-data` | Lists storage accounts, blobs in a container, SQL servers, and Cosmos DB accounts. | 4 | mux |
| `azure-resources` | Lists resource groups and resources, or shows one resource. | 3 | mux |
| `azure-whoami` | Shows the signed-in account and subscription, or lists subscriptions. | 2 | mux |
| `cloudflare` | Checks a Worker build with a dry run, deploys it, lists deployments and D1, KV, and R2 resources, and deploys Pages. | 8 | mux |
| `do-apps` | Lists apps, validates the app spec, deploys, and shows runtime logs. | 4 | mux |
| `do-infra` | Lists Droplets, Kubernetes clusters, managed databases, volumes, and domains. | 5 | mux |
| `do-whoami` | Shows the account and the current balance. | 2 | mux |
| `flyio` | Shows app status, deploys, prints recent logs, shows scaling, lists secret names, and lists releases. | 6 | mux |
| `gcp-compute` | Lists instances and starts or stops one. | 3 | mux |
| `gcp-data` | Lists Cloud SQL instances, Firestore indexes, and BigQuery datasets, and estimates a BigQuery query's cost. | 4 | mux |
| `gcp-gke` | Lists GKE clusters and writes kubeconfig for one. | 2 | mux |
| `gcp-run` | Lists services, reads recent logs, deploys a Cloud Run revision without traffic, promotes it, and lists Functions and App Engine versions. | 6 | mux |
| `gcp-storage` | Lists buckets and objects, and syncs folders with a dry run first. | 3 | mux |
| `gcp-whoami` | Shows the signed-in accounts, the active project, and the gcloud configuration. | 3 | mux |
| `huawei-infra` | Lists ECS servers, OBS objects, CCE clusters, and RDS instances. | 4 | mux |
| `huawei-whoami` | Shows the configured KooCLI profiles and the regions they use. | 2 | mux |
| `ibm-infra` | Lists VPC instances, Kubernetes clusters, Code Engine apps, and Object Storage buckets, and updates a Code Engine app's image. | 5 | mux |
| `ibm-whoami` | Shows the targeted account, region, and resource group, or the account details. | 2 | mux |
| `linode-infra` | Lists instances, LKE clusters, Object Storage buckets, and volumes. | 4 | mux |
| `linode-whoami` | Shows the configured profile and the account. | 2 | mux |
| `netlify` | Shows site status, makes draft or production deploys, lists sites, and lists environment variable names. | 5 | mux |
| `openstack-heat` | Validates templates, previews, creates, or updates Heat stacks, and shows stack events. | 5 | mux |
| `openstack-inspect` | Lists servers, images, flavors, networks, volumes, or Heat stacks. | 6 | mux |
| `openstack-whoami` | Shows the authenticated token scope, the service catalog, and project quotas. | 3 | mux |
| `rackspace` | Confirms the Rackspace identity, lists servers, or lists Rackspace Spot clusters. | 3 | mux |
| `vercel` | Lists deployments, makes preview or production deployments, shows deployment logs, and lists environment variable names. | 6 | mux |

### containers

| Skill | What it does | Commands | Origin |
|---|---|---:|---|
| `compose` | Validates, starts, stops, restarts, and inspects the project's Compose stack. | 6 | mux |
| `docker-build` | Builds an image from the nearest Dockerfile, tags it, or pushes it to a registry. | 3 | mux |
| `docker-inspect` | Lists containers and images, tails a container's logs, or shows resource use. | 5 | mux |
| `dockerfile-lint` | Checks the nearest Dockerfile with hadolint, or with a built-in checklist when hadolint is not installed. | 1 | mux |

### debugging

| Skill | What it does | Commands | Origin |
|---|---|---:|---|
| `debug` | A step-by-step procedure for finding the real cause of a bug or failing test before changing code. | 0 | mux |
| `git-bisect` | Drives git bisect with a test command to find the first bad commit, and always restores the original HEAD. | 3 | mux |

### devops

| Skill | What it does | Commands | Origin |
|---|---|---:|---|
| `ci-cd-pipeline-builder` | Generate pragmatic CI/CD pipelines from detected project stack signals, fast baseline generation, repeatable checks, environment-aware deployment stages. Use when... | 2 | claude-skills |
| `ci-repro` | Run the Release build and tests the way CI does. | 1 | mux |
| `performance-profiler` | Systematic performance profiling for Node.js, Python, and Go applications. Identifies CPU, memory, and I/O bottlenecks, generates flamegraphs, analyzes bundle sizes... | 1 | claude-skills |
| `ship-gate` | Pre-production audit that scans a codebase for security, database, deployment, code quality, AI/LLM, dependency, frontend, and observability issues. Intercepts deploy... | 1 | claude-skills |

### docs

| Skill | What it does | Commands | Origin |
|---|---|---:|---|
| `adr-new` | Creates a new ADR from a template under docs/adr. | 1 | mux |
| `doc-sync` | Reports file paths mentioned in the docs that no longer exist. | 1 | mux |
| `explain-codebase` | Maps the repository (folders with file counts, languages, entry points) and guides a layered explanation that cites real paths. | 1 | mux |
| `readme-audit` | Checks relative markdown links in README.md and reports any whose target file is missing. | 1 | mux |
| `spellcheck-docs` | Scans markdown for accidentally doubled words and reports them. | 1 | mux |
| `url-check` | Extracts URLs from markdown and probes each with a HEAD request. | 1 | mux |

### frontend

| Skill | What it does | Commands | Origin |
|---|---|---:|---|
| `a11y-audit` | Accessibility audit skill for scanning, fixing, and verifying WCAG 2.2 Level A and AA compliance across React, Next.js, Vue, Angular, Svelte, and plain HTML codebases... | 2 | claude-skills |
| `react-build-analyze` | Builds the app and lists the largest emitted JavaScript and CSS files. | 1 | mux |
| `react-dev-server` | Detects the dev script, framework, port, and ready line, so the dev server can be started in the background and its URL reported. | 1 | mux |
| `react-lint-hooks` | Reports only react-hooks and jsx-a11y ESLint findings. | 1 | mux |
| `react-new-component` | Creates a function component and, when the project has a test setup, a test beside it. | 1 | mux |
| `react-new-hook` | Creates a custom hook and, when the project has a test setup, a test beside it. | 1 | mux |
| `react-test` | Runs the tests for a single component or hook by name. | 1 | mux |
| `react-upgrade-check` | Reports the declared React-related versions and what the package manager actually installed. | 1 | mux |

### git

| Skill | What it does | Commands | Origin |
|---|---|---:|---|
| `git-blame-summary` | Summarizes the authors of a path and its commit history. | 2 | mux |
| `git-branch` | Creates a new branch or lists existing branches. | 2 | mux |
| `git-changelog-entry` | Inserts a bullet under the first heading in CHANGELOG.md. | 1 | mux |
| `git-cherry-pick` | Applies a commit onto the current branch, or aborts an in-progress cherry-pick. | 2 | mux |
| `git-commit` | Stages all changes and commits them, refusing to run on the default branch. | 1 | mux |
| `git-conflict-explainer` | Lists and shows files that are currently in conflict. | 2 | mux |
| `git-large-files` | Reports the largest tracked files and the largest blobs in history. | 2 | mux |
| `git-open-pr` | Opens a pull request from the current branch or reports pull request status via the GitHub CLI. | 2 | mux |
| `git-push` | Pushes the current branch to origin and sets its upstream. | 1 | mux |
| `git-release` | Creates an annotated release tag from a required version argument. | 1 | mux |
| `git-stash-manager` | Saves, lists, pops, and shows stashed changes. | 4 | mux |
| `git-status-vs-head` | Summarizes local changes and how the branch compares to the origin default branch. | 2 | mux |
| `git-sync` | Fetches all remotes and rebases the current branch onto the origin default branch. | 2 | mux |
| `git-undo-last-commit` | Undoes the most recent commit while keeping its changes staged. | 1 | mux |
| `pr-description` | Drafts a PR title and bulleted change list from the commit log. | 1 | mux |

### hygiene

| Skill | What it does | Commands | Origin |
|---|---|---:|---|
| `codestyle-audit` | Heuristically flags likely style violations: use of var and files whose first code line is not namespace. | 1 | mux |
| `dead-code-scan` | Heuristically flags disabled and obsolete C# code (#if false blocks and [Obsolete] members). | 1 | mux |
| `gitignore-audit` | Lists git-tracked paths that look like build output or dependencies and probably should be ignored. | 1 | mux |
| `json-validate` | Checks whether a JSON file parses and reports the first error. | 1 | mux |
| `large-file-scan` | Lists working-tree files larger than a size threshold, with their sizes. | 1 | mux |
| `license-header-check` | Lists C# files whose first non-empty line is not a comment, so they may be missing a license header. | 1 | mux |
| `line-ending-check` | Reads text files as bytes and reports any with mixed CRLF/LF line endings. | 1 | mux |
| `todo-scan` | Inventories TODO, FIXME, and HACK markers across common source files. | 1 | mux |
| `yaml-lint` | Flags YAML lines that use a literal tab for indentation. | 1 | mux |

### infrastructure

| Skill | What it does | Commands | Origin |
|---|---|---:|---|
| `pulumi` | Shows the account and stacks, previews changes, updates the stack, and shows outputs. | 5 | mux |
| `terraform` | Formats, validates, initializes, and plans; applies only a saved plan; lists state and outputs. Works with OpenTofu. | 7 | mux |

### kubernetes

| Skill | What it does | Commands | Origin |
|---|---|---:|---|
| `helm` | Lints, renders, diffs, and upgrades Helm releases, and lists releases and history. | 7 | mux |
| `k8s-apply` | Shows a diff against the cluster, applies manifests, or restarts a deployment, behind the production guard. | 3 | mux |
| `k8s-context` | Shows the active context, cluster, and namespace, lists contexts, or lists namespaces. | 3 | mux |
| `k8s-inspect` | Gets and describes resources, shows pod logs, warning events, resource use, and rollout status. | 6 | mux |
| `k8s-validate` | Validates manifests with a client or server dry run, or against schemas with kubeconform. | 3 | mux |
| `minikube` | Shows status, starts or stops the cluster, loads local images, gets service URLs, and lists addons. | 6 | mux |

### languages

| Skill | What it does | Commands | Origin |
|---|---|---:|---|
| `cargo-build` | Builds the crate or workspace in debug or release. | 2 | mux |
| `cargo-clippy` | Runs Clippy with warnings as errors, or applies its fixes. | 2 | mux |
| `cargo-fmt` | Applies or verifies rustfmt formatting. | 2 | mux |
| `cargo-test` | Runs cargo test for everything or for tests whose names match a filter. | 2 | mux |
| `cpp-build` | Builds with CMake, Meson, or Make using every processor core. | 1 | mux |
| `cpp-configure` | Configures a CMake (or Meson) build tree with compile_commands.json exported. | 3 | mux |
| `cpp-format` | Applies or verifies clang-format across the project's C and C++ sources. | 2 | mux |
| `cpp-sanitize` | Configures a separate build tree with AddressSanitizer or UndefinedBehaviorSanitizer, builds it, and runs CTest. | 2 | mux |
| `cpp-test` | Runs CTest (or meson test) with output shown for failures. | 2 | mux |
| `cpp-tidy` | Runs clang-tidy against compile_commands.json, on every source or only changed files. | 1 | mux |
| `dotnet-build` | Build the current .NET project or solution. | 2 | mux |
| `dotnet-format` | Apply or verify code style using dotnet format. | 2 | mux |
| `dotnet-outdated` | List outdated or vulnerable NuGet packages. | 2 | mux |
| `dotnet-pack` | Produce NuGet packages from the current project or solution. | 1 | mux |
| `dotnet-publish` | Publish the application, framework-dependent or self-contained. | 2 | mux |
| `dotnet-restore` | Restore NuGet packages for the current project or solution. | 1 | mux |
| `dotnet-test` | Run the test suite for the current .NET project or solution. | 2 | mux |
| `go-build` | Builds or vets every package in the module. | 2 | mux |
| `go-lint` | Runs golangci-lint when the project configures it, otherwise staticcheck. | 1 | mux |
| `go-mod` | Tidies go.mod and go.sum, or lists dependencies with available updates. | 2 | mux |
| `go-test` | Runs go test for every package, filtered, or with the race detector. | 3 | mux |
| `java-build` | Compiles, packages, or cleans a Maven or Gradle project. | 3 | mux |
| `java-deps` | Prints the dependency tree or available dependency updates. | 2 | mux |
| `java-format` | Applies or verifies formatting with Spotless. | 2 | mux |
| `java-lint` | Runs whichever of Checkstyle, SpotBugs, and PMD the build configures. | 1 | mux |
| `java-new-class` | Creates a class, interface, record, or enum in the standard source layout, with a JUnit test for classes. | 1 | mux |
| `java-test` | Runs JUnit tests through Maven Surefire or Gradle, all or filtered. | 2 | mux |
| `js-build` | Runs the project's build script. | 1 | mux |
| `js-deps` | Lists outdated or vulnerable packages, or explains why a package is installed. | 3 | mux |
| `js-format` | Applies or verifies formatting with Prettier or Biome. | 2 | mux |
| `js-install` | Installs dependencies with the project's package manager (npm, pnpm, yarn, or bun). | 2 | mux |
| `js-lint` | Checks or fixes lint problems with ESLint or Biome. | 2 | mux |
| `js-scripts` | Lists the project's package.json scripts or runs one. | 2 | mux |
| `js-test` | Runs Vitest, Jest, Mocha, or node --test in non-watch mode. | 3 | mux |
| `js-typecheck` | Runs the TypeScript compiler without emitting files. | 1 | mux |
| `py-deps` | Lists outdated packages or known vulnerabilities in the project's environment. | 2 | mux |
| `py-env` | Shows which interpreter and environment manager the project uses, or creates the environment. | 2 | mux |
| `py-format` | Applies or verifies formatting with Ruff, falling back to Black. | 2 | mux |
| `py-install` | Installs the project's dependencies, or adds a package, with its environment manager. | 2 | mux |
| `py-lint` | Checks or fixes lint problems with Ruff, falling back to flake8. | 2 | mux |
| `py-test` | Runs pytest (or unittest when pytest is not installed) inside the project's environment. | 4 | mux |
| `py-typecheck` | Runs mypy or pyright, whichever the project configures. | 1 | mux |

### loops

| Skill | What it does | Commands | Origin |
|---|---|---:|---|
| `ci-watch` | Shows the branch's workflow runs, waits for a run to finish, and prints only the logs of failed steps. | 3 | mux |
| `fix-until-green` | Runs the detected build and tests, then fixes one failure at a time until everything passes or the iteration budget runs out. | 1 | mux |
| `flaky-test-hunt` | Runs a test filter (or any command) many times and reports how often it fails, with the first failing output. | 1 | mux |
| `loop-until` | Re-runs a command until it exits 0 or the attempts run out, printing each attempt's exit code. | 1 | mux |

### productivity

| Skill | What it does | Commands | Origin |
|---|---|---:|---|
| `handoff` | Compact the current conversation into a handoff document for another agent to pick up. Save to a user-configured location (OS temp, home folder, or per-project... | 7 | claude-skills |

### review

| Skill | What it does | Commands | Origin |
|---|---|---:|---|
| `api-design-reviewer` | Comprehensive REST API design review with automated linting, breaking-change detection, and design scorecards. Catches inconsistent conventions, missing versioning, and... | 3 | claude-skills |
| `api-surface-diff` | Shows added and removed public members between two git refs. | 1 | mux |
| `code-review` | Reviews uncommitted changes, a branch, a commit, a pull request, or one file for correctness bugs first, then tests and maintainability. | 5 | mux |
| `pr-comments` | Lists the review threads on a GitHub pull request, unresolved first, so they can be addressed. | 1 | mux |
| `simplify` | Lists the files changed on this branch so the model can remove duplication, dead code, and needless complexity without changing behavior. | 1 | mux |
| `test-gap-review` | Lists changed source files that have no matching test change, using each language's test naming conventions. | 1 | mux |

### scaffolding

| Skill | What it does | Commands | Origin |
|---|---|---:|---|
| `new-class` | Creates a new C# class file following this repo's style. | 1 | mux |
| `new-skill` | Creates a new skill directory with starter SKILL.md content. | 1 | mux |
| `new-tool` | Creates an IToolExecutor implementation stub. | 1 | mux |
| `new-touchstone-suite` | Creates a touchstone test suite stub with one placeholder case. | 1 | mux |

### security

| Skill | What it does | Commands | Origin |
|---|---|---:|---|
| `git-secret-scan` | Checks staged changes for common credential patterns and fails if any are found. | 1 | mux |
| `security-guidance` | PreToolUse security-anti-pattern hook for mux. Catches 12 common security risks (command injection, XSS, SQL injection, unsafe deserialization, GitHub Actions workflow... | 0 | claude-skills |
| `security-review` | Reviews uncommitted changes, a branch, or a pull request for exploitable security problems, with a secret scan and a dependency check. | 3 | mux |
| `skill-security-auditor` | Security audit and vulnerability scanner for AI agent skills before installation. Use when: (1) evaluating a skill from an untrusted source, (2) auditing a skill... | 1 | claude-skills |

### testing

| Skill | What it does | Commands | Origin |
|---|---|---:|---|
| `pw-coverage` | Analyze test coverage gaps. Use when user says "test coverage", "what's not tested", "coverage gaps", "missing tests", "coverage report", or "what needs testing". | 0 | claude-skills |
| `pw-fix` | Fix failing or flaky Playwright tests. Use when user says "fix test", "flaky test", "test failing", "debug test", "test broken", "test passes sometimes", or... | 0 | claude-skills |
| `pw-generate` | Generate Playwright tests. Use when user says "write tests", "generate tests", "add tests for", "test this component", "e2e test", "create test for", "test this page"... | 0 | claude-skills |
| `pw-init` | Set up Playwright in a project. Use when user says "set up playwright", "add e2e tests", "configure playwright", "testing setup", "init playwright", or "add test... | 0 | claude-skills |
| `pw-migrate` | Migrate from Cypress or Selenium to Playwright. Use when user mentions "cypress", "selenium", "migrate tests", "convert tests", "switch to playwright", "move from... | 0 | claude-skills |
| `pw-report` | Generate test report. Use when user says "test report", "results summary", "test status", "show results", "test dashboard", or "how did tests go". | 0 | claude-skills |
| `pw-review` | Review Playwright tests for quality. Use when user says "review tests", "check test quality", "audit tests", "improve tests", "test code review", or "playwright best... | 0 | claude-skills |
| `tdd-guide` | Test-driven development skill for writing unit tests, generating test fixtures and mocks, analyzing coverage gaps, and guiding red-green-refactor workflows across Jest... | 1 | claude-skills |

### workflow

| Skill | What it does | Commands | Origin |
|---|---|---:|---|
| `env-report` | Reports the OS and the versions of common developer tools. | 1 | mux |
| `init` | Surveys the repository and guides writing or updating AGENTS.md: build, test, and run commands, layout, and conventions. | 1 | mux |
| `project-detect` | Reports languages, package managers, build systems, test frameworks, CI, and container or deploy files, plus the mux skills that apply. | 2 | mux |
| `release-notes` | Prints the topmost section of CHANGELOG.md as release notes. | 1 | mux |
| `skill-extract` | Turn a proven pattern or debugging solution into a standalone reusable skill with SKILL.md, reference docs, and examples. Use when the user runs /si:extract or asks to... | 0 | claude-skills |
| `standup-summary` | Groups recent commits by author for a quick standup update. | 1 | mux |

## Optional packs

Packs are not seeded. Install one with `mux skill pack install <pack>` (or a single skill with `--skill <id>`), `/packs` in the terminal, the Packs panel in the web dashboard or desktop app, or `POST /v1.0/api/skills/packs/install`. Removing a pack never touches skills you wrote or edited. Pack skills keep their pack name as their category unless you change it.

### business pack (44 skills)

Executive advisory, board, strategy, and operating playbooks.

| Skill | What it does |
|---|---|
| `board-deck-builder` | Assembles comprehensive board and investor update decks by pulling perspectives from all C-suite roles. Use when preparing board meetings, investor updates, quarterly... |
| `board-meeting` | Multi-agent board meeting protocol for strategic decisions. Runs a structured 6-phase deliberation: context loading, independent C-suite contributions (isolated, no... |
| `board-prep` | Board meeting preparation for the adversarial scenario, not the friendly one. Forces numbers-cold mastery, anticipates hard questions, builds a narrative that... |
| `boardroom` | /cs:boardroom <brief>, 6-phase multi-role deliberation across the C-suite with Phase 2 isolation, critic pre-screen, and synthesis. Outputs a board memo. Use when a... |
| `business-investment-advisor` | Business investment analysis and capital allocation advisor. Use when evaluating whether to invest in equipment, real estate, a new business, hiring, technology, or any... |
| `caio-review` | /cs:caio-review <plan>, Eval-demanding Chief AI Officer interrogation of any plan that involves AI: model selection, risk classification, cost economics, or AI hiring... |
| `cco-review` | /cs:cco-review <plan>, Retention-obsessed Chief Customer Officer interrogation of any plan that touches customer retention, segmentation, CS team sizing, or CS team... |
| `cdo-review` | /cs:cdo-review <plan>, Decision-driven Chief Data Officer interrogation of any plan that touches training data, data architecture, data productization, or data team... |
| `cfo-review` | /cs:cfo-review <plan>, Numerate-skeptic interrogation of any plan that touches money. Unit economics, runway, dilution, capital allocation. Use when a plan commits... |
| `change-management` | Framework for rolling out organizational changes without chaos. Covers the ADKAR model adapted for startups, communication templates, resistance patterns, and change... |
| `chief-of-staff` | C-suite orchestration layer. Routes founder questions to the right advisor role(s), triggers multi-role board meetings for complex decisions, synthesizes outputs, and... |
| `ciso-review` | /cs:ciso-review <plan>, Risk-paranoid interrogation of any plan that touches data, compliance, or production access. Use when launching features that handle customer... |
| `cmo-review` | /cs:cmo-review <plan>, Narrative-first interrogation of positioning, ICP, message house, and channel mix. Use when launching a campaign or repositioning, or when CAC is... |
| `company-os` | The meta-framework for how a company runs, the connective tissue between all C-suite roles. Covers operating system selection (EOS, Scaling Up, OKR-native, hybrid)... |
| `competitive-intel` | Systematic competitor tracking that feeds CMO positioning, CRO battlecards, and CPO roadmap decisions. Use when analyzing competitors, building sales battlecards... |
| `context-engine` | Loads and manages company context for all C-suite advisor skills. Reads ~/.mux/company-context.md, detects stale context (>90 days), enriches context during... |
| `contract-and-proposal-writer` | Generate professional, jurisdiction-aware business documents: freelance contracts, project proposals, SOWs, NDAs, and MSAs. Structured Markdown output with docx... |
| `cpo-review` | /cs:cpo-review <plan>, JTBD-driven interrogation of product roadmap, PMF signal, and portfolio focus. Use when committing a quarter's roadmap, deciding whether to kill a... |
| `cro-review` | /cs:cro-review <plan>, Pipeline-paranoid interrogation of revenue, win rate, NRR, and ramp time. Use when the forecast misses pipeline coverage, win rates drop, or... |
| `cross-eval` | /cs:cross-eval <memo>, Multi-model consensus on a board memo or strategy brief. Claude + Codex + Gemini cross-review with graceful degradation. Use when a high-stakes... |
| `cs-onboard` | Founder onboarding interview that captures company context across 7 dimensions. Invoke with /cs:setup for initial interview or /cs:update for quarterly refresh... |
| `cto-advisor` | Technical leadership guidance for engineering teams, architecture decisions, and technology strategy. Use when assessing technical debt, scaling engineering teams... |
| `cto-review` | /cs:cto-review <plan>, Architecture and scaling interrogation. Tech debt, scaling cliffs, team scaling, build-vs-buy. Use when committing to an architecture, planning... |
| `culture-architect` | Build, measure, and evolve company culture as operational behavior, not wall posters. Covers mission/vision/values workshops, values-to-behaviors translation, culture... |
| `exec-brief` | /cs:brief <topic>, Generate a one-page strategy brief from an office-hours intake. First step in the strategic sprint pipeline. Use when a strategic question needs to be... |
| `exec-decide` | /cs:decide <memo>, Log a decision to two-layer memory via decision-logger. Approved memo becomes durable; raw transcripts kept for reference. Use when the founder has... |
| `exec-execute` | /cs:execute <decision>, Generate a 90-day execution plan with weekly milestones, DRIs, and check-in cadence from an approved decision. Use when a logged decision needs... |
| `exec-freeze` | /cs:freeze <decision> <days>, Lock a strategic decision for a cooldown period to prevent impulse reversal. Mirrors gstack's safety primitives for the business layer. Use... |
| `exec-onboard` | /cs:onboard, Founder interview that populates ~/.mux/company-context.md using the canonical 7-dimension cs-onboard schema. The first command to run when starting with... |
| `exec-post-mortem` | /cs:post-mortem <decision>, Honest retrospective on an executed decision, scored against original assumptions and dissent. Closes the strategic sprint loop. Use when a... |
| `founder-coach` | Personal leadership development for founders and first-time CEOs. Covers founder archetype identification, delegation frameworks, energy management, CEO calendar audits... |
| `founder-mode` | /cs:founder-mode <question>, Auto-routes any founder question to the right C-role advisor or to /cs:boardroom for multi-role topics. The single-command entry point. Use... |
| `gc-review` | /cs:gc-review <plan>, General Counsel interrogation of contracts, IP, regulatory, term sheets, and employment-law surface. Use when reviewing a term sheet before... |
| `internal-narrative` | Build and maintain one coherent company story across all audiences, employees, investors, customers, candidates, and partners. Detects narrative contradictions and... |
| `intl-expansion` | International market expansion strategy. Market selection, entry modes, localization, regulatory compliance, and go-to-market by region. Use when expanding to new... |
| `knowledge-ops` | Use when a Head of Ops, Knowledge Manager, or TPM-Internal needs to author, validate, or clean up company SOPs and internal runbooks (procurement intake, vendor... |
| `ma-playbook` | M&A strategy for acquiring companies or being acquired. Due diligence, valuation, integration, and deal structure. Use when evaluating acquisitions, preparing for... |
| `mentor-challenge` | Pre-mortem plan analysis. Imagine the plan failed 12 months from now and work backwards to find the weaknesses. Surfaces assumptions, dependencies, and execution risks... |
| `mentor-hard-call` | /em:hard-call, Framework for decisions with no good options. Use when every option is painful and a structured 10/10/10 + regret-minimization pass is needed, e.g... |
| `mentor-postmortem` | /em:postmortem, Honest analysis of what went wrong. Use after a failed launch, missed quarter, or bad hire to run a blameless 5-Whys retrospective with a change... |
| `mentor-stress-test` | /em:stress-test, Business assumption stress testing. Use before betting on a plan whose core assumptions are unvalidated, e.g. stress-testing 'enterprise buyers will... |
| `office-hours` | /cs:office-hours <topic>, YC-style 6-question founder interrogation before any advice. Forces clarity on problem, customer, distribution, defensibility, capital, and... |
| `rfp-responder` | Use when an RFP, RFI, RFQ, security questionnaire, vendor questionnaire, or proposal request arrives and the team needs a structured response, parsing multi-section... |
| `vpe-review` | /cs:vpe-review <plan>, Throughput-first VP of Engineering interrogation of any plan that touches delivery, eng hiring, team structure, or production discipline. Use when... |

### compliance pack (10 skills)

Audit preparation and readiness for SOC 2, ISO 27001, ISO 13485, GDPR, FDA, and the EU AI Act.

| Skill | What it does |
|---|---|
| `agent-decision-receipts` | Mint a tamper-evident, post-quantum-signed receipt for a consequential agent action (deploy, delete, pay, grant-access, model decision) so it can be verified later from... |
| `ai-act-readiness` | /cs:ai-act-readiness <system>, EU AI Act 6-question forcing interrogation. Use during AI-system intake, before EU deployment, or during annual compliance refresh as... |
| `aims-audit` | /cs:aims-audit <scope>, ISO/IEC 42001 AIMS internal-audit 6-question forcing interrogation. Use before certification stage 1, before annual internal audit cycles, or... |
| `compliance-readiness` | /cs:compliance-readiness <program>, Multi-framework compliance officer 6-question forcing interrogation of any compliance program. Use before starting a new framework... |
| `fda-qsr-audit-prep` | /cs:fda-qsr-audit-prep <scope>, FDA 21 CFR 820 (QSR / QMSR) audit 6-question forcing interrogation. Post-Feb 2026 substantially harmonized with ISO 13485. Use before... |
| `gdpr-audit-prep` | /cs:gdpr-audit-prep <scope>, GDPR audit 6-question Article-cited forcing interrogation. Use before annual internal GDPR review, post-breach internal audit, DPA... |
| `gdpr-dsgvo-expert` | GDPR and German DSGVO compliance automation. Scans codebases for privacy risks, generates DPIA documentation, tracks data subject rights requests with Art. 12(3)... |
| `iso13485-audit-prep` | /cs:iso13485-audit-prep <scope>, ISO 13485 QMS audit 6-question forcing interrogation. Design controls + CAPA + post-market focused. Use before Clause 8.2.4 internal... |
| `iso27001-audit-prep` | /cs:iso27001-audit-prep <scope>, ISO 27001 ISMS audit readiness 6-question forcing interrogation. Use before annual Clause 9.2 internal audit, surveillance audit prep... |
| `soc2-audit-prep` | /cs:soc2-audit-prep <scope>, SOC 2 Type II readiness 6-question forcing interrogation. Observation-period focused. Use before Type II observation begins, mid-period... |

### data pack (6 skills)

Databases, SQL, data quality, and statistics playbooks.

| Skill | What it does |
|---|---|
| `data-quality-auditor` | Audit datasets for completeness, consistency, accuracy, and validity. Profile data distributions, detect anomalies and outliers, surface structural issues, and produce... |
| `senior-data-scientist` | World-class senior data scientist skill specialising in statistical modeling, experiment design, causal inference, and predictive analytics. Covers A/B testing (sample... |
| `senior-ml-engineer` | ML engineering skill for productionizing models, building MLOps pipelines, and integrating LLMs. Covers model deployment, feature stores, drift monitoring, RAG systems... |
| `senior-prompt-engineer` | Use when the user asks to optimize prompts, design prompt templates, evaluate LLM outputs with an eval set, measure RAG retrieval quality, validate agent/tool... |
| `statistical-analyst` | Run hypothesis tests, analyze A/B experiment results, calculate sample sizes, and interpret statistical significance with effect sizes. Use when you need to validate... |
| `universal-scraping-architect` | Use for web scraping, crawling, document extraction, API parsing, or building validation-heavy data pipelines using Firecrawl or local Python scripts. |

### docs pack (2 skills)

Markdown document authoring and review.

| Skill | What it does |
|---|---|
| `md-document` | Converts long-form markdown (specs, RFCs, reports, plans, explainers) into a single-file, lightly-interactive HTML document with sticky TOC, scrollspy, search filter... |
| `md-review` | Converts a markdown PR writeup or code review (one with ```diff fenced blocks and severity-tagged > [!BLOCKER]/[!MAJOR]/[!MINOR]/[!NIT] callouts) into a single-file... |

### engineering pack (64 skills)

Architecture, backend, frontend, data, ML, agents, and reliability playbooks for software teams.

| Skill | What it does |
|---|---|
| `agent-designer` | Use when the user asks to design a multi-agent system, pick an orchestration pattern (supervisor/swarm/pipeline), generate tool schemas for agents, or evaluate agent... |
| `agent-harness` | Turn any domain folder of skills into a bounded agentic loop: compile a goal into a verifiable task plan, execute tasks with the domain's own tools, verify every task... |
| `agent-workflow-designer` | Design production-grade multi-agent workflows with clear pattern choice (sequential, parallel, hierarchical), handoff contracts, failure handling, and cost/context... |
| `api-test-suite-builder` | Use when the user asks to generate API tests, create integration test suites, test REST endpoints, or build contract tests. |
| `ar-loop` | Start an autonomous experiment loop with user-selected interval (10min, 1h, daily, weekly, monthly). Uses /loop for scheduling. Use when the user runs /ar:loop or asks... |
| `ar-resume` | Resume a paused experiment. Checkout the experiment branch, read results history, continue iterating. Use when the user runs /ar:ar-resume or asks to pick up a... |
| `ar-run` | Run a single experiment iteration. Edit the target file, evaluate, keep or discard. Use when the user runs /ar:run or asks for one manual autoresearch iteration. |
| `ar-setup` | Set up a new autoresearch experiment interactively. Collects domain, target file, eval command, metric, direction, and evaluator. Use when the user runs /ar:setup or... |
| `ar-status` | Show experiment dashboard with results, active loops, and progress. Use when the user runs /ar:ar-status or asks how an autoresearch experiment is going. |
| `autoresearch-agent` | Autonomous experiment loop that optimizes any file by a measurable metric. Inspired by Karpathy's autoresearch. The agent edits a target file, runs a fixed evaluation... |
| `aws-solution-architect` | Design AWS architectures for startups using serverless patterns and IaC templates. Use when asked to design serverless architecture, create CloudFormation templates... |
| `azure-cloud-architect` | Design Azure architectures for startups and enterprises. Use when asked to design Azure infrastructure, create Bicep/ARM templates, optimize Azure costs, set up Azure... |
| `boost-asio-pro` | Use when writing or reviewing asynchronous C++ networking code with Boost.Asio or standalone Asio, TCP/UDP servers and clients, SSL/TLS, timers, strands, io_context... |
| `browser-automation` | Use when the user asks to automate browser tasks, scrape websites, fill forms, capture screenshots, extract structured data from web pages, or build web automation... |
| `browserstack` | Run tests on BrowserStack. Use when user mentions "browserstack", "cross-browser", "cloud testing", "browser matrix", "test on safari", "test on firefox", or "browser... |
| `chaos-engineering` | Use when planning, running, or learning from chaos engineering experiments. Triggers on "chaos experiment", "fault injection", "gameday", "resilience test", "blast... |
| `code-tour` | Use when the user asks to create a CodeTour .tour file, persona-targeted, step-by-step walkthroughs that link to real files and line numbers. Trigger for: create a tour... |
| `database-designer` | Use when the user asks to design database schemas, plan data migrations, optimize queries, choose between SQL and NoSQL, or model data relationships. |
| `database-schema-designer` | Use when the user asks to create ERD diagrams, normalize database schemas, design table relationships, or plan schema migrations. |
| `email-template-builder` | Build complete transactional email systems: React Email templates, provider integration (Resend, Postmark, SendGrid, AWS SES), preview server, i18n support, dark mode... |
| `embedded-iot-mentor` | Mentor for embedded and IoT hardware projects. Helps select MCUs, dev boards, and toolchains, decides where sensor readings end up (phone, PC, dashboard, or alert), and... |
| `env-secrets-manager` | Manage environment-variable hygiene and secrets safety across local development and production. Practical auditing, drift awareness, rotation readiness. Use when... |
| `epic-design` | Build immersive, cinematic 2.5D interactive websites using scroll storytelling, parallax depth, text animations, and premium scroll effects, no WebGL required. Use this... |
| `feature-flags-architect` | Use when adding, retiring, or auditing feature flags. Triggers on "add a flag", "ship behind a flag", "rollout plan", "kill switch", "stale flags", "flag debt"... |
| `full-page-screenshot` | Use when the user asks to capture a full-page screenshot, long screenshot, or complete page capture of a web page. Handles SPA scroll containers, lazy-loaded images, and... |
| `gcp-cloud-architect` | Design GCP architectures for startups and enterprises. Use when asked to design Google Cloud infrastructure, deploy to GKE or Cloud Run, configure BigQuery pipelines... |
| `grill-me` | Interview the user relentlessly about a plan or design until reaching shared understanding, resolving each branch of the decision tree. Use when user wants to... |
| `grill-with-docs` | Docs-anchored grilling session: challenges a plan against the project's existing language (CONTEXT.md) and recorded decisions (docs/adr/), and updates those files inline... |
| `hub-board` | Read, write, and browse the AgentHub message board for agent coordination. Use when the user runs /hub:board or asks to post, read, or inspect coordination messages... |
| `hub-eval` | Evaluate and rank agent results by metric or LLM judge for an AgentHub session. Use when the user runs /hub:eval or asks to score, compare, or pick a winner among... |
| `hub-init` | Create a new AgentHub collaboration session with task, agent count, and evaluation criteria. Use when the user runs /hub:hub-init or asks to start a multi-agent... |
| `hub-merge` | Merge the winning agent's branch into base, archive losers, and clean up worktrees. Use when the user runs /hub:merge or asks to land the winning AgentHub result and... |
| `hub-run` | One-shot lifecycle command that chains init → baseline → spawn → eval → merge in a single invocation. Use when the user runs /hub:run or asks to execute a full AgentHub... |
| `hub-spawn` | Launch N parallel subagents in isolated git worktrees to compete on the session task. Use when the user runs /hub:spawn or asks to start the competing agents for an... |
| `hub-status` | Show DAG state, agent progress, and branch status for an AgentHub session. Use when the user runs /hub:hub-status or asks how the AgentHub agents are doing. |
| `incident-commander` | Comprehensive incident response framework from detection through resolution and post-incident review. Battle-tested SRE/DevOps practices: severity classification... |
| `karpathy-coder` | Use when writing, reviewing, or committing code to enforce Karpathy's 4 coding principles, surface assumptions before coding, keep it simple, make surgical changes... |
| `kubernetes-operator` | Use when building a Kubernetes Operator: custom controllers that reconcile CRD state. Triggers on "build an operator", "CRD design", "reconcile loop"... |
| `llm-cost-optimizer` | Use proactively whenever LLM API costs come up -- or should. Triggers include: 'my AI costs are too high', 'optimize token usage', 'which model should I use', 'LLM spend... |
| `loop-library` | Discover, find, compare, audit, repair, adapt, and design repeatable AI-agent loops with explicit triggers, actions, verification, stopping conditions, guardrails, and... |
| `mcp-server-builder` | Design and ship production-ready MCP (Model Context Protocol) servers from OpenAPI contracts instead of hand-written tool wrappers. Python and TypeScript support, schema... |
| `migration-architect` | Zero-downtime migration planning, compatibility validation, and rollback strategy generation. Tools for system, database, and infrastructure migrations with minimal... |
| `minimalist` | Use when the user asks to write code efficiently, avoid over-engineering, reduce dependencies, or prevent unnecessary abstractions. Enforces a strict efficiency ladder... |
| `named-persona-adversarial-review` | Code review through the lens of real engineers' documented philosophies (Torvalds, Thompson, Carmack, Kent Beck, Jobs, Cagan). Complements abstract-role adversarial... |
| `observability-designer` | Design production-ready observability strategies combining metrics, logs, and traces. Includes SLI/SLO design, golden-signals monitoring, alert optimization. Use when... |
| `prompt-governance` | Use when managing prompts in production at scale: versioning prompts, running A/B tests on prompts, building prompt registries, preventing prompt regressions, or... |
| `rag-architect` | Use when the user asks to design a RAG pipeline, choose a chunking strategy or embedding model, pick a vector database, or evaluate retrieval quality (precision@k... |
| `runbook-generator` | Generate operational runbooks from a service name, deployment, incident response, maintenance, and rollback workflows. Templated structure customizable per environment... |
| `secrets-vault-manager` | Use when the user asks to set up secret management infrastructure, integrate HashiCorp Vault, configure cloud secret stores (AWS Secrets Manager, Azure Key Vault, GCP... |
| `self-eval` | Honestly evaluate AI work quality using a two-axis scoring system. Use after completing a task, code review, or work session to get an unbiased assessment. Detects score... |
| `senior-architect` | This skill should be used when the user asks to "design system architecture", "evaluate microservices vs monolith", "create architecture diagrams", "analyze... |
| `senior-backend` | Designs and implements backend systems including REST APIs, microservices, database architectures, authentication flows, and security hardening. Use when the user asks... |
| `senior-devops` | Comprehensive DevOps skill for CI/CD, infrastructure automation, containerization, and cloud platforms (AWS, GCP, Azure). Includes pipeline setup, infrastructure as... |
| `senior-frontend` | Frontend development skill for React, Next.js, TypeScript, and Tailwind CSS applications. Use when building React components, optimizing Next.js performance, analyzing... |
| `senior-qa` | Generates unit tests, integration tests, and E2E tests for React/Next.js applications. Scans components to create Jest + React Testing Library test stubs, analyzes... |
| `senior-secops` | Senior SecOps engineer skill for application security, vulnerability management, compliance verification, and secure development practices. Runs SAST/DAST scans... |
| `slo-architect` | Use when defining, reviewing, or operating SLOs/SLIs/error budgets. Triggers on "define an SLO", "what should our SLO be", "error budget", "burn rate", "SLI", "service... |
| `snowflake-development` | Use when writing Snowflake SQL, building data pipelines with Dynamic Tables or Streams/Tasks, using Cortex AI functions, creating Cortex Agents, writing Snowpark Python... |
| `spec-driven-workflow` | Use when the user asks to write specs before code, define acceptance criteria, plan features before implementation, generate tests from specifications, or follow... |
| `sql-database-assistant` | Use when the user asks to write SQL queries, optimize database performance, generate migrations, explore database schemas, or work with ORMs like Prisma, Drizzle... |
| `strict-api` | Use when the user says 'no hallucinations', 'verify APIs', 'reality check', or 'don't invent functions'. Prevents the agent from calling methods, imports, or variables... |
| `stripe-integration-expert` | Production-grade Stripe integrations: subscriptions with trials and proration, one-time payments, usage-based billing, checkout sessions, idempotent webhook handlers... |
| `testrail` | Sync tests with TestRail. Use when user mentions "testrail", "test management", "test cases", "test run", "sync test cases", "push results to testrail", or "import from... |
| `zero-hallucination-coder` | Runs a disciplined Discuss -> Map -> Decompose -> Execute -> Verify loop that grounds code in verified structure, no invented APIs, no assumed imports, no placeholder... |

### marketing pack (10 skills)

Brand, positioning, content, and conversion playbooks.

| Skill | What it does |
|---|---|
| `brand-guidelines` | When the user wants to apply, document, or enforce brand guidelines for any product or company. Also use when the user mentions 'brand guidelines,' 'brand colors,'... |
| `business-name-fit` | Suggest, pick, or vet a business, startup, or product name that stays true to the founder's cultural origin while working professionally in the markets they want to sell... |
| `marketing-ideas` | When the user needs marketing ideas, inspiration, or strategies for their SaaS or software product. Also use when the user asks for 'marketing ideas,' 'growth ideas,'... |
| `marketing-psychology` | When the user wants to apply psychological principles, mental models, or behavioral science to marketing. Also use when the user mentions 'psychology,' 'mental models,'... |
| `marketing-strategy-pmm` | Product marketing skill for positioning, GTM strategy, competitive intelligence, and product launches. Use when the user asks about product positioning, go-to-market... |
| `paywall-upgrade-cro` | When the user wants to create or optimize in-app paywalls, upgrade screens, upsell modals, or feature gates. Also use when the user mentions "paywall," "upgrade screen,"... |
| `popup-cro` | When the user wants to create or optimize popups, modals, overlays, slide-ins, or banners for conversion purposes. Also use when the user mentions "exit intent," "popup... |
| `social-content` | When the user wants help creating, scheduling, or optimizing social media content for LinkedIn, Twitter/X, Instagram, TikTok, Facebook, or other platforms. Also use when... |
| `video-content-strategist` | Use when planning video content strategy, writing video scripts, optimizing YouTube channels, building short-form video pipelines (Reels, TikTok, Shorts), or repurposing... |
| `youtube-full` | Use when the user needs YouTube transcripts, video search, channel browsing, playlist extraction, or content monitoring. Trigger phrases: 'get the transcript for'... |

### product pack (18 skills)

Product discovery, strategy, analytics, roadmaps, and design playbooks.

| Skill | What it does |
|---|---|
| `apple-hig-expert` | Audits and designs iOS/macOS/watchOS/visionOS interfaces against the Apple Human Interface Guidelines, including the Liquid Glass design language (announced WWDC25... |
| `code-to-prd` | Reverse-engineer any codebase into a complete Product Requirements Document (PRD). Analyzes routes, components, state management, API integrations, and user interactions... |
| `competitive-teardown` | Analyzes competitor products and companies by synthesizing data from pricing pages, app store reviews, job postings, SEO signals, and social media into structured... |
| `confluence-expert` | Atlassian Confluence expert for creating and managing spaces, knowledge bases, and documentation. Configures space permissions and hierarchies, creates page templates... |
| `experiment-designer` | Use when planning product experiments, writing testable hypotheses, estimating sample size, prioritizing tests, or interpreting A/B outcomes with practical statistical... |
| `jira-expert` | Atlassian Jira expert for creating and managing projects, planning, product discovery, JQL queries, workflows, custom fields, automation, reporting, and all Jira... |
| `landing-page-generator` | Generates high-converting landing pages as complete Next.js/React (TSX) components with Tailwind CSS. Creates hero sections, feature grids, pricing tables, FAQ... |
| `meeting-analyzer` | Analyzes meeting transcripts and recordings to surface behavioral patterns, communication anti-patterns, and actionable coaching feedback. Use this skill whenever the... |
| `product-analytics` | Use when defining product KPIs, building metric dashboards, running cohort or retention analysis, or interpreting feature adoption trends across product stages. |
| `product-discovery` | Use when validating product opportunities, mapping assumptions, planning discovery sprints, or testing problem-solution fit before committing delivery resources. |
| `product-manager-toolkit` | Comprehensive toolkit for product managers including RICE prioritization, customer interview analysis, PRD templates, discovery frameworks, and go-to-market strategies... |
| `product-strategist` | Strategic product leadership toolkit for Head of Product covering OKR cascade generation, quarterly planning, competitive landscape analysis, product vision documents... |
| `roadmap-communicator` | Use when preparing roadmap narratives, release notes, changelogs, or stakeholder updates tailored for executives, engineering teams, and customers. |
| `saas-scaffolder` | Generates complete, production-ready SaaS project boilerplate including authentication, database schemas, billing integration, API routes, and a working dashboard using... |
| `scrum-master` | Advanced Scrum Master skill for data-driven agile team analysis and coaching. Use when the user asks about sprint planning, velocity tracking, retrospectives, standup... |
| `spec-to-repo` | Use when the user says 'build me an app', 'create a project from this spec', 'scaffold a new repo', 'generate a starter', 'turn this idea into code', 'bootstrap a... |
| `team-communications` | Write internal company communications: 3P updates (Progress/Plans/Problems), company-wide newsletters, FAQ roundups, incident reports, leadership updates, status... |
| `ui-design-system` | UI design system toolkit for Senior UI Designer including design token generation, component documentation, responsive design calculations, and developer handoff tools... |

### productivity pack (1 skills)

Personal reflection and working habits.

| Skill | What it does |
|---|---|
| `reflect` | Mid-conversation reflection skill that pauses execution and zooms out from detail-mode to honestly reassess direction, assumptions, and bias. Use when the user says... |

### research pack (5 skills)

Deep research, literature review, and briefing playbooks.

| Skill | What it does |
|---|---|
| `deep-research` | Run a disciplined, multi-source research investigation for a high-stakes question or decision, fan-out web search across many channels, parallel sub-agents, source... |
| `deepread` | Use when the user asks to deeply read a book, article, PDF, or document set; extract claims and evidence; build a knowledge map; or learn through Feynman explanation and... |
| `dossier` | Decision-grade entity research skill: produces a hypothesis-tested dossier on a specific company, person, nonprofit, or government org, not a generic profile. Forcing... |
| `litreview` | Academic literature orientation skill that searches papers via free keyless APIs (PubMed E-utilities + OpenAlex) by default, with the Consensus MCP as an optional... |
| `pulse` | Multi-source recency research skill that takes the pulse of any topic across Reddit, Hacker News, the open web, and optionally X/Twitter within a configurable recent... |

### security pack (6 skills)

Application, cloud, AI, and offensive security playbooks.

| Skill | What it does |
|---|---|
| `ai-security` | Use when assessing AI/ML systems for prompt injection, jailbreak vulnerabilities, model inversion risk, data poisoning exposure, or agent tool abuse. Covers MITRE ATLAS... |
| `cloud-security` | Use when assessing cloud infrastructure for security misconfigurations, IAM privilege escalation paths, S3 public exposure, open security group rules, or IaC security... |
| `incident-response` | Use when a security incident has been detected or declared and needs classification, triage, escalation path determination, and forensic evidence collection. Covers... |
| `red-team` | Use when planning or executing authorized red team engagements, attack path analysis, or offensive security simulations. Covers MITRE ATT&CK kill-chain planning... |
| `security-pen-testing` | Use when the user asks to perform security audits, penetration testing, vulnerability scanning, OWASP Top 10 checks, or offensive security assessments. Covers static... |
| `threat-detection` | Use when hunting for threats in an environment, analyzing IOCs, or detecting behavioral anomalies in telemetry. Covers hypothesis-driven threat hunting, IOC sweep... |
