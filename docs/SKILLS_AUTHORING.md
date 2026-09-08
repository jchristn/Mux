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
| `commands` | list | empty | The runnable units (below). |

The frontmatter parser accepts a small, fixed subset of YAML — scalars, booleans, inline `[a, b]` or block (`- item`) string lists, and the `commands` list of maps. It is intentionally simple; when in doubt, keep values on one line.

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
