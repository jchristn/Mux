# Authoring Mux Skills

A skill teaches mux a procedure once so it runs the same way every time. It lives in a folder under `~/.mux/skills/`, and the fastest way to make one is `/skills` → **+ New skill…** in the interactive shell, or `mux skill new <id>` on the command line. Both write a working starter you then edit. What follows is the full format, so you can write or hand-edit a skill with confidence.

## Layout

One skill is one directory whose name is the skill id — lowercase, hyphen-separated, no slashes, no `..`. The id in the folder name and the `name` in the frontmatter must match.

```
~/.mux/skills/
  git-status-vs-head/
    SKILL.md            # required
    scripts/            # optional bundled scripts referenced by commands
      summarize.ps1
    resources/          # optional reference files the skill can read
      report-template.md
```

## SKILL.md

The file has two parts: YAML-style frontmatter between `---` fences, then a Markdown body. The body is what the model reads when it opens the skill, and it can hold fenced code blocks that commands run.

```markdown
---
name: git-status-vs-head
title: Compare working tree to GitHub HEAD
description: Summarize how the working tree and current branch differ from origin/HEAD before committing.
version: 1.0.0
enabled: true
mutating: false
whenToUse: The user asks how local changes compare to GitHub, or wants a pre-flight check before pushing.
tags: [git, github, vcs]
commands:
  - name: summarize
    description: Print a structured local-vs-remote summary.
    block: summarize
    interpreter: pwsh
    timeoutMs: 60000
---

## What this does

Fetches the remote default branch and reports staged, unstaged, and untracked
changes plus how far the current branch has diverged.

```pwsh id=summarize
git fetch origin --quiet
git status --short
```
```

### Frontmatter fields

| Field | Type | Default | Meaning |
|---|---|---|---|
| `name` | string | required | The skill id; must equal the folder name. |
| `title` | string | `name` | Human label shown in the manager. |
| `description` | string | required | One or two sentences; the only body text the model sees before it opens the skill. |
| `version` | string | `0.0.0` | Semantic version, for provenance. |
| `enabled` | bool | `true` | The author default; the runtime toggle in `skills.json` wins. |
| `mutating` | bool | `true` | `false` marks the skill read-only. See *Safety* below. |
| `whenToUse` | string | empty | Guidance the model uses to decide relevance. |
| `tags` | string[] | empty | Grouping and search. Inline `[a, b]` or a block list. |
| `commands` | list | empty | The runnable units (below). Leave it out for a playbook skill. |
| `appliesTo` | string[] | empty | File globs, relative to the project root, that make the skill relevant (`[package.json]`, `[pyproject.toml, requirements*.txt]`, `[**/*.csproj]`). In the default `relevant` listing mode the skill is advertised to the model only when a glob matches. Empty means always relevant. |
| `userInvocable` | bool | `true` | Whether `/<name> args` runs the skill. Claude Code's `user-invocable` maps here. |
| `modelInvocable` | bool | `true` | Whether the skill is listed in the system prompt. Claude Code's `disable-model-invocation: true` sets it to `false`; the skill stays callable by name and through the `skill` tool. |
| `requiresTools` | string[] | empty | Executables that must be on PATH for the skill to be listed in `relevant` mode (`[aws]`); `a\|b` means either (`[terraform\|tofu]`). PATH is scanned, nothing is run, and the answer is cached for the refresh interval. Combined with `appliesTo`, both must pass. |
| `argumentHint` | string | empty | What the skill expects after its name, for example `"[base-branch]"`. Quote it when it starts with `[`. Claude Code's `argument-hint` maps here. |

The frontmatter parser accepts a small, fixed subset of YAML — scalars, booleans, inline `[a, b]` or block (`- item`) string lists, and the `commands` list of maps. It is intentionally simple; when in doubt, keep values on one line.

Keys match without regard to case, hyphens, or underscores, so `allowed-tools` and `allowedTools` are the same field. A comma-separated scalar (`allowed-tools: Read, Grep`) is read as a list, and a block scalar (`description: >` followed by indented lines) is folded into one value. A field mux does not recognize is reported as a validation warning and ignored; it never makes a skill invalid. The name `list` is reserved: `skill` with the name `list` returns every available skill.

### Commands

A command is a named unit the model runs through `run_skill`. Each command sets exactly one of `block` (an `id=` code block in the body) or `run` (a script path relative to the skill directory), plus an `interpreter` and an optional `timeoutMs`.

| Key | Meaning |
|---|---|
| `name` | The command name, unique within the skill. |
| `description` | What the command does. |
| `block` | The `id` of a fenced body block to run. |
| `run` | A script path under the skill directory to run (must stay inside it). |
| `interpreter` | One of `bash`, `sh`, `pwsh`, `python`, `node`, `dotnet-script`. |
| `timeoutMs` | Kill the command after this many milliseconds. Minimum 1000; default 120000. |

A body block is tagged on its opening fence:

    ```pwsh id=summarize
    git status --short
    ```

Arguments passed to `run_skill` (or `mux skill run … --arg x`) arrive as process arguments after the script — in `pwsh` as `$args`, in `node` as `process.argv`, in `python` as `sys.argv`, in `bash` as `$1`, `$2`. Arguments are never interpolated into a shell string, so quoting and injection are not your problem. The executor sets three environment variables for every run: `MUX_SKILL_NAME`, `MUX_SKILL_DIR`, and `MUX_SKILL_COMMAND`, so a script can find its own `resources/` without guessing.

## Playbooks and invocation by name

A skill with no `commands` is a playbook. Its body is a procedure (what to collect, what to check, how to report) that the model reads and carries out with its normal tools. Code review, debugging, and "write the project's AGENTS.md" are playbooks: the work needs judgment, not a fixed script. A hybrid keeps a few read-only commands that gather facts deterministically (the diff, the detected test command) and a body that says what to do with them.

Any user-invocable skill runs by name: `/code-review main` in the terminal, desktop, or dashboard, or `mux print "/code-review main"`. The body becomes the submitted message, with `$ARGUMENTS` replaced by the whole argument text and `$1` through `$9` by positional arguments (quotes group words). Substitution touches prose only; fenced code blocks are left exactly as written, so a bash block's own `$1` is safe. When the body has no placeholder, the arguments are appended on their own line. A skill with commands also gets a closing line telling the model to run them through `run_skill`.

```markdown
---
name: team-review
description: Review a change the way this team does.
argumentHint: "[base-branch]"
appliesTo: [package.json]
---

Diff the current branch against $1 (default: the origin default branch). Read every changed hunk,
then report correctness problems first, each with file:line and a concrete failure scenario.
```

## Project skills

Put skills in the repository under `.mux/skills`, `.claude/skills`, or `.agents/skills` (configurable with `projectSkillRoots`) and everyone working in that project gets them. A project skill shadows a user skill with the same id, and the inventory marks it. Existing Claude Code and Codex skill folders load as they are.

A project skill with commands runs code from the repository, so it loads only after the user trusts that project (`/trust all`, `mux skill trust all`, or `--trust-project-skills` for a single headless run). Until then it is listed as blocked. Playbook skills execute nothing and load right away, unless the project is set to `ignore`.

## Toolchain skills: exit codes, dry runs, and the shared helper

The default toolchain skills (`project-detect`, `js-*`, `py-*`, and the families that follow) share one PowerShell helper, `resources/mux-skill.ps1`, which each command dot-sources on its first line. Copy that pattern when you write a toolchain skill of your own, and keep its conventions:

- **Exit codes.** 0 means success. 1 means the tool ran and reported problems (failing tests, lint findings, a failed build). 2 means the tool is not installed or the project does not use this toolchain, and the output says which, with an install hint. A model can tell "your tests failed" from "you have no test runner" without reading the text.
- **Exit 3** means the production guard refused (see below).
- **Dry runs.** With `MUX_SKILL_DRY_RUN=1` in the environment, `Invoke-MuxTool` prints `DRYRUN: <command>` instead of running anything, while detection still runs. That shows exactly what a skill would do in a given project, and it is how the test suite checks every skill on every platform without installing the toolchains.
- **Detection lives in the skill.** `Get-MuxNodePackageManager` reads the `packageManager` field, then lockfiles (bun, pnpm, yarn, npm) in the package or repository root; `Get-MuxPythonManager` picks uv, poetry, pipenv, or a project `.venv`, and `Invoke-MuxPython` runs tools as `python -m <tool>` inside that environment. The model calls `js-test all` or `py-test all`; the skill decides the actual command.

Infrastructure skills (Docker, Kubernetes, Helm, OpenStack, the cloud providers, Terraform, Pulumi) add three rules on top:

- **Show the target first.** `Get-MuxTarget` prints the cluster context, profile, subscription, project, or workspace on the first line, and its lookup doubles as an authentication check: a CLI that is not signed in exits 2 with the login command for the user to run. Skills never run a login flow themselves. In a dry run the target comes from `MUX_SKILL_DRY_RUN_TARGET`, so the guard can be tested without credentials.
- **Preview before change, and guard production.** Changes default to a preview (`kubectl diff`, server dry runs, `helm diff`, `--dryrun`, `terraform plan`, `pulumi preview`, Cloud Run `--no-traffic`). `Assert-MuxNotProduction` refuses (exit 3) when the target matches `skillProdPattern` (passed to skills as `MUX_SKILL_PROD_PATTERN`) unless the arguments include `--confirm <exact target name>`. The model should add `--confirm` only after the user explicitly approves.
- **Never destroy or reveal.** No default infrastructure command deletes, destroys, terminates, prunes, or reads a secret's value; secret and environment commands list names only. A test scans every infrastructure command for those words.

Review skills (`code-review`, `security-review`, `simplify`, `test-gap-review`) use the helper's git section: `Assert-MuxGitRepo`, `Get-MuxDefaultBranch`, `Get-MuxChangeBase`, `Write-MuxLimited` (cuts output at `MUX_SKILL_DIFF_MAX_BYTES`, default 200000), `Write-MuxSecretFindings` (masked), and `Write-MuxManifestChanges`. Their commands gather facts; the body defines the finding format the model writes.

Loop skills add `Invoke-MuxCaptured` (run a command and keep its output and exit code without printing it), `Write-MuxTail`, `Get-MuxBoundedInt`, `Split-MuxOptions`, `Get-MuxCheckPlan` (the detected build and test commands, optionally filtered), and `Invoke-MuxCheckStep`. A command that waits can raise its timeout with `timeoutMs:` in its frontmatter (the default is 120000); the default loop skills use 1800000.

The helper is seeded into each skill folder and is yours to edit; mux never overwrites a skill folder that already exists.

## Cross-platform notes

mux runs on Windows, Linux, and macOS. `pwsh` (PowerShell 7) and `node` run on all three and are the safest defaults for skills you intend to share. A bare `bash` is a trap on Windows, where it often resolves to the WSL stub rather than Git Bash — reach for `bash` only when the audience is POSIX, and say so in `whenToUse`. A machine still needs the named interpreter installed to *run* a skill; validation only checks that the interpreter is on the allowlist.

## Safety and approval

A skill can do exactly what `run_process` can do — no more, no less. `run_skill` is treated as mutating and runs under the approval policy and the workspace write lease, so a skill's code never executes silently under the default policy. Mark a skill `mutating: false` to document that it only reads; the flag drives the inventory display, and future versions may use it to relax the lease for read-only work. Guard destructive skills yourself: the seeded `git-commit`, for example, refuses to run on the default branch.

## Resources

Files under `resources/` travel with the skill and are listed when the model opens it. A command reads them from `$MUX_SKILL_DIR/resources/…`. Use them for templates, checklists, or reference text a command needs.

## Validating and running

Validate one skill or the whole library, with a nonzero exit on failure so a CI step can gate it:

```text
mux skill validate            # the whole library
mux skill validate my-skill   # one skill
```

Run a command deterministically, with the same `stdout`/`stderr`/`exit_code` contract the agent sees — useful from a Git hook or a pipeline:

```text
mux skill run git-status-vs-head summarize
mux skill run json-validate run --arg ./package.json
```

## Three worked examples

**A read-only reporter.** `git-status-vs-head` (seeded) fetches `origin`, prints `git status --short`, and reports ahead/behind counts. It sets `mutating: false`, so it runs without the lease. Copy it when your skill only observes.

**A guarded mutator.** `git-commit` (seeded) checks the current branch against the default and aborts before staging if they match, then commits with a message assembled from the caller's arguments. Copy it when your skill changes the workspace and needs a guardrail.

**A bundled-script skill.** Point a command at `run: scripts/build.ps1` instead of an inline block when the logic outgrows a fenced block or you want to reuse a script you already have. The script lives in the skill's `scripts/` folder and runs in place.

Start from a seeded skill that resembles what you need, edit its body and command, run `mux skill validate <id>`, and it is ready.
