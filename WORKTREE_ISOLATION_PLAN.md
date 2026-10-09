# Worktree Isolation Plan

_Status: done (2026-10-08). Row 28 of `archive/NEW_SKILLS_PLAN.md`. `[ ]` = todo, `[x]` = done. Deviations from the original design are listed at the end._

mux runs a subagent or a background job in the same working tree as everything else, and it keeps those runs from trampling each other with a single workspace write lease. That works, but it serializes every mutating tool call across every run, and it means a subagent that goes wrong leaves its half-finished edits mixed into your files. Claude Code and Codex both offer a way out: give the delegated run its own checkout, let it work freely, and hand back a branch. Git worktrees make that cheap. A worktree is a second working directory backed by the same repository, so creating one takes a second and costs no clone.

The goal is narrow. A subagent or a job can opt into `isolation: worktree`. It then works on its own branch in its own directory, without the write lease, and when it ends you get either nothing (it changed nothing, so the worktree is gone) or a branch you can review and merge. Everything else about how mux runs stays the same.

## What is in scope

Isolation is opt-in per subagent definition (`"isolation": "worktree"` in `subagents.json`), per call (an `isolation` argument on `spawn_subagent`), and per job (`JobManager.EnqueueAsync` with `IsolationModeEnum.Worktree`, and `TaskOrchestrator.IsolateTasks` for every task of a plan). The leftovers are managed with a `mux worktree list|prune|remove` verb and a `/worktrees` terminal command.

Out of scope, on purpose:

- Merging. mux reports the branch and the diff; merging stays a human decision (or a later explicit tool call).
- Copying uncommitted changes into the worktree. The worktree starts from `HEAD`. A run that needs your in-progress edits should run in the shared tree, and the result says when your tree was dirty so the difference is never silent.
- Repositories without a commit, and directories outside git. Isolation refuses cleanly there instead of falling back to the shared tree, because a silent fallback would defeat the point.
- A desktop or dashboard UI for worktrees. The data is reachable through the CLI verb and the terminal; the other surfaces can add views later.

## Design decisions

**Where worktrees live.** Under the repository's git directory, in `.git/mux-worktrees/<kind>-<label>`. Putting them inside `.git` keeps them out of your working tree, out of `git status`, and out of editors' file trees, and it means deleting the repository deletes them too. A small `<name>.mux.json` beside each records the base commit, kind, label, and creation time, which is what lets `list` and `prune` tell "no changes" from "has commits" later.

**Branch naming.** `mux/<kind>/<label>`, sanitized to lowercase letters, digits, and dashes, at most 40 characters. If the branch or folder already exists, a numeric suffix is added (`-2`, `-3`). mux never reuses or overwrites a branch it did not just create.

**What "changed nothing" means.** When the run ends, mux commits any uncommitted changes in the worktree onto its branch (with your git identity, or a `mux` identity when none is configured; hooks and signing are skipped because an interactive prompt would hang an automated run). If the branch then has no commits beyond its base, the worktree, the branch, and the metadata are removed. Otherwise everything is kept and the outcome lists the commits and the `git diff --stat` against the base.

**The write lease.** An isolated run gets no write lease. Nothing else writes in its worktree, so serializing with the shared tree would only slow both sides down. Subagents never held the lease in the first place; isolated jobs now skip it as well.

**Safety.** The manager only ever runs `git worktree add/remove/prune` and `git branch -D` on `mux/` branches it created, plus commits inside its own worktrees. It never checks out, resets, stashes, or commits on your branch. `remove` refuses to discard uncommitted changes or unmerged commits unless you pass `--force` (or `--keep-branch` to keep the commits). `prune` only removes worktrees with no changes and forgets registrations whose folder is already gone.

## Tasks

- [x] `Mux.Core/Enums/IsolationModeEnum.cs` (`None`, `Worktree`).
- [x] `Mux.Core/Worktrees/`: `WorktreeManager` (create, finish, list, remove, prune, and `TryParseIsolation`), `WorktreeLease`, `WorktreeOutcome`, `WorktreeInfo`, and `GitOutput`, one type per file.
- [x] Subagents: `SubagentDefinition.Isolation` (validated by `SubagentRegistry.IsValid`), an `isolation` argument on `spawn_subagent` that overrides it, and a `worktree` object in the tool result (branch, path, commits, diff stat, whether the base was dirty, and the next step).
- [x] Jobs: `Job.Isolation`, `Job.Worktree`, `Job.WorktreeOutcome`; `JobManager.EnqueueAsync(..., IsolationModeEnum, ...)`, `IsolationDirectoryProvider` (set by `CreateForAgentLoop` to the template's working directory), and `Worktrees`. The worktree is created before the job's first run and finished before the job is marked terminal, so anyone waiting on the job sees its outcome. An isolated agent-loop job runs in the worktree without the write lease.
- [x] `TaskOrchestrator.IsolateTasks`.
- [x] `mux worktree list|prune|remove <name> [--force] [--keep-branch] [--cwd dir] [--output-format json]`.
- [x] `/worktrees [list | prune | remove <name> [--force] [--keep-branch]]` in the terminal.
- [x] The REST subagent DTO round-trips `Isolation`, and the OpenAPI schema documents it.
- [x] Docs: `docs/USAGE.md` (Worktree isolation), `docs/CONFIG.md` (`subagents.json` `isolation`).

## Tests

`WorktreeIsolationSuite` (20 cases) runs everything against real temporary repositories. It covers creation (branch, folder, base commit, metadata, subdirectory mapping), the keep-or-remove decision (no changes, uncommitted edits committed onto the branch, commits the run made itself), branch name collisions with an existing branch and a live worktree, a dirty base tree (flagged, not copied, and still intact afterwards with no stash entries), refusals (outside git, no commits, a missing directory), `list` leaving the user's own worktrees out, `remove` protecting work unless forced and keeping the branch on request, and `prune` touching only clean and missing worktrees. On top of the manager it runs `spawn_subagent` end to end with a scripted executor (kept work, cleanup when nothing changed, the per-call override, bad values, isolation outside git, and an executor that fails halfway with its partial work kept), isolated and shared jobs side by side in one `JobManager`, a job manager with no base directory, `TaskOrchestrator.IsolateTasks` putting two tasks on two branches, the CLI verb, and `/worktrees` in a headless terminal. Every case that touches a repository checks that the main tree is still on `main` at the same commit.

## Risks

Disk use is the obvious one: a kept worktree is a full checkout. `mux worktree list` shows what is there and `prune` reclaims the unchanged ones, but kept work stays until someone merges or removes it, and that is deliberate.

A long-running isolated job that is killed (the process exits) leaves its worktree behind unfinished. `prune` handles the clean case; a dirty leftover shows up in `list` as having uncommitted changes, and `remove --force` clears it.

Large monorepos make `git worktree add` slower because it checks out every file. Sparse checkouts are not used yet.

## Deviations

- Jobs gained isolation at the `JobManager` level rather than through a new "background job" surface: the interactive shells still run one turn at a time, so job isolation is reachable today through the Core API and `TaskOrchestrator.IsolateTasks`. Subagents are the user-facing entry point.
- A kept worktree's uncommitted changes are committed onto its branch when the run ends, so the branch always carries the whole result and `git merge` works without visiting the folder.
- The desktop subagent editor does not show an `isolation` field yet; it preserves the value when editing, and `subagents.json` and the REST API carry it.
