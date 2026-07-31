namespace Mux.Core.Skills
{
    using System;
    using System.Collections.Generic;

    /// <summary>The default git and GitHub skills seeded into ~/.mux/skills.</summary>
    public static class DefaultGitSkills
    {
        /// <summary>Returns the git default skills, keyed by id.</summary>
        /// <returns>The skills as id to SKILL.md content.</returns>
        public static IReadOnlyDictionary<string, string> All()
        {
            Dictionary<string, string> skills = new Dictionary<string, string>(StringComparer.Ordinal);

            skills["git-status-vs-head"] = DefaultSkillBuilder.Build(
                "git-status-vs-head", "Compare working tree to origin HEAD",
                "Summarizes local changes and how the branch compares to the origin default branch.",
                false, "git, github, vcs",
                "The user asks how local changes compare to GitHub, or wants a pre-flight check before committing or pushing.",
                new List<DefaultSkillCommandDef>
                {
                    new DefaultSkillCommandDef("summarize", "Show branch, status, and ahead/behind vs origin default.", "pwsh",
@"$ErrorActionPreference = 'Stop'
git fetch origin --quiet
$branch = (git rev-parse --abbrev-ref HEAD).Trim()
$base = ((git rev-parse --abbrev-ref origin/HEAD) -replace '^origin/', '').Trim()
Write-Output ""Branch: $branch    Default: origin/$base""
Write-Output '--- working tree ---'
git status --short
Write-Output '--- ahead/behind origin default (left=behind right=ahead) ---'
git rev-list --left-right --count ""origin/$base...HEAD""
"),
                    new DefaultSkillCommandDef("files-changed", "List files that differ from the origin default branch.", "pwsh",
@"$ErrorActionPreference = 'Stop'
git fetch origin --quiet
$base = ((git rev-parse --abbrev-ref origin/HEAD) -replace '^origin/', '').Trim()
git diff --name-only ""origin/$base""
")
                });

            skills["git-commit"] = DefaultSkillBuilder.Build(
                "git-commit", "Commit tracked and staged changes",
                "Stages all changes and commits them, refusing to run on the default branch.",
                true, "git, vcs",
                "The user wants to commit their changes onto a feature branch.",
                new List<DefaultSkillCommandDef>
                {
                    new DefaultSkillCommandDef("commit", "Stage everything and commit with a message.", "pwsh",
@"$ErrorActionPreference = 'Stop'
$branch = (git rev-parse --abbrev-ref HEAD).Trim()
if ($branch -eq 'main' -or $branch -eq 'master') { Write-Error 'Refusing on the default branch. Create a feature branch first.'; exit 1 }
$msg = if ($args.Count -gt 0) { $args -join ' ' } else { 'chore: update' }
git add -A
git commit -m $msg
")
                });

            skills["git-push"] = DefaultSkillBuilder.Build(
                "git-push", "Push the current branch",
                "Pushes the current branch to origin and sets its upstream.",
                true, "git, github, vcs",
                "The user wants to push the current branch to origin and set upstream tracking.",
                new List<DefaultSkillCommandDef>
                {
                    new DefaultSkillCommandDef("push", "Push the current branch and set upstream.", "pwsh",
@"$ErrorActionPreference = 'Stop'
$branch = (git rev-parse --abbrev-ref HEAD).Trim()
git push --set-upstream origin $branch
")
                });

            skills["git-branch"] = DefaultSkillBuilder.Build(
                "git-branch", "Create and switch branches",
                "Creates a new branch or lists existing branches.",
                true, "git, vcs",
                "The user wants to create or switch branches.",
                new List<DefaultSkillCommandDef>
                {
                    new DefaultSkillCommandDef("create", "Create and switch to a new branch.", "pwsh",
@"$ErrorActionPreference = 'Stop'
if ($args.Count -lt 1) { Write-Error 'Usage: create <branch>'; exit 1 }
git switch -c $args[0]
"),
                    new DefaultSkillCommandDef("list", "List local and remote branches.", "pwsh",
@"git branch -a
")
                });

            skills["git-sync"] = DefaultSkillBuilder.Build(
                "git-sync", "Fetch and rebase onto the default branch",
                "Fetches all remotes and rebases the current branch onto the origin default branch.",
                true, "git, vcs",
                "The user wants to update their branch with the latest changes from the origin default branch.",
                new List<DefaultSkillCommandDef>
                {
                    new DefaultSkillCommandDef("fetch", "Fetch all remotes and prune stale branches.", "pwsh",
@"$ErrorActionPreference = 'Stop'
git fetch --all --prune
"),
                    new DefaultSkillCommandDef("rebase", "Rebase the current branch onto the origin default branch.", "pwsh",
@"$ErrorActionPreference = 'Stop'
git fetch origin --quiet
$base = ((git rev-parse --abbrev-ref origin/HEAD) -replace '^origin/', '').Trim()
git rebase ""origin/$base""
")
                });

            skills["git-open-pr"] = DefaultSkillBuilder.Build(
                "git-open-pr", "Open or check a pull request",
                "Opens a pull request from the current branch or reports pull request status via the GitHub CLI.",
                true, "git, github",
                "The user wants to open or check a pull request. Requires the GitHub CLI (gh) to be installed and authenticated.",
                new List<DefaultSkillCommandDef>
                {
                    new DefaultSkillCommandDef("open", "Create a pull request, filling title and body from commits.", "pwsh",
@"$ErrorActionPreference = 'Stop'
gh pr create --fill
"),
                    new DefaultSkillCommandDef("status", "Show the status of pull requests for this repository.", "pwsh",
@"$ErrorActionPreference = 'Stop'
gh pr status
")
                });

            skills["git-changelog-entry"] = DefaultSkillBuilder.Build(
                "git-changelog-entry", "Add a changelog entry",
                "Inserts a bullet under the first heading in CHANGELOG.md.",
                true, "git, docs",
                "The user wants to record a change as a new bullet in CHANGELOG.md.",
                new List<DefaultSkillCommandDef>
                {
                    new DefaultSkillCommandDef("add", "Insert a bullet under the first '## ' heading in CHANGELOG.md.", "pwsh",
@"$ErrorActionPreference = 'Stop'
if ($args.Count -lt 1) { Write-Error 'Usage: add <text>'; exit 1 }
if (-not (Test-Path 'CHANGELOG.md')) { Write-Error 'No CHANGELOG.md found.'; exit 1 }
$entry = '- ' + ($args -join ' ')
$lines = Get-Content 'CHANGELOG.md'
$out = New-Object System.Collections.Generic.List[string]
$inserted = $false
foreach ($line in $lines) {
  $out.Add($line)
  if (-not $inserted -and $line -like '## *') { $out.Add($entry); $inserted = $true }
}
if (-not $inserted) { Write-Error 'No heading starting with ## found in CHANGELOG.md.'; exit 1 }
Set-Content 'CHANGELOG.md' $out
Write-Output ""Added: $entry""
")
                });

            skills["git-release"] = DefaultSkillBuilder.Build(
                "git-release", "Tag a release",
                "Creates an annotated release tag from a required version argument.",
                true, "git, release",
                "The user wants to tag a release with a specific version.",
                new List<DefaultSkillCommandDef>
                {
                    new DefaultSkillCommandDef("tag", "Create an annotated tag for the given version.", "pwsh",
@"$ErrorActionPreference = 'Stop'
if ($args.Count -lt 1) { Write-Error 'Usage: tag <version>'; exit 1 }
git tag -a $args[0] -m $args[0]
")
                });

            skills["git-undo-last-commit"] = DefaultSkillBuilder.Build(
                "git-undo-last-commit", "Undo the last commit",
                "Undoes the most recent commit while keeping its changes staged.",
                true, "git, vcs",
                "The user wants to undo the last commit but keep the changes staged.",
                new List<DefaultSkillCommandDef>
                {
                    new DefaultSkillCommandDef("soft", "Reset the last commit, keeping its changes staged.", "pwsh",
@"$ErrorActionPreference = 'Stop'
git reset --soft HEAD~1
")
                });

            skills["git-cherry-pick"] = DefaultSkillBuilder.Build(
                "git-cherry-pick", "Cherry-pick a commit",
                "Applies a commit onto the current branch, or aborts an in-progress cherry-pick.",
                true, "git, vcs",
                "The user wants to apply a specific commit onto the current branch.",
                new List<DefaultSkillCommandDef>
                {
                    new DefaultSkillCommandDef("pick", "Cherry-pick the given commit onto the current branch.", "pwsh",
@"$ErrorActionPreference = 'Stop'
if ($args.Count -lt 1) { Write-Error 'Usage: pick <commit>'; exit 1 }
git cherry-pick $args[0]
"),
                    new DefaultSkillCommandDef("abort", "Abort an in-progress cherry-pick.", "pwsh",
@"$ErrorActionPreference = 'Stop'
git cherry-pick --abort
")
                });

            skills["git-stash-manager"] = DefaultSkillBuilder.Build(
                "git-stash-manager", "Manage the stash",
                "Saves, lists, pops, and shows stashed changes.",
                true, "git, vcs",
                "The user wants to save or restore work in progress using the git stash.",
                new List<DefaultSkillCommandDef>
                {
                    new DefaultSkillCommandDef("save", "Stash the working tree with an optional message.", "pwsh",
@"$ErrorActionPreference = 'Stop'
$msg = if ($args.Count -gt 0) { $args -join ' ' } else { 'wip' }
git stash push -m $msg
"),
                    new DefaultSkillCommandDef("list", "List all stashes.", "pwsh",
@"git stash list
"),
                    new DefaultSkillCommandDef("pop", "Pop the most recent stash.", "pwsh",
@"$ErrorActionPreference = 'Stop'
git stash pop
"),
                    new DefaultSkillCommandDef("show", "Show the diff of the most recent stash.", "pwsh",
@"git stash show -p
")
                });

            skills["git-conflict-explainer"] = DefaultSkillBuilder.Build(
                "git-conflict-explainer", "Explain merge conflicts",
                "Lists and shows files that are currently in conflict.",
                false, "git, vcs",
                "The user is in the middle of a merge or rebase and wants to see the conflicting files.",
                new List<DefaultSkillCommandDef>
                {
                    new DefaultSkillCommandDef("list", "List files that are currently in conflict.", "pwsh",
@"git diff --name-only --diff-filter=U
"),
                    new DefaultSkillCommandDef("show", "Show the diff of the conflicting files.", "pwsh",
@"git diff --diff-filter=U
")
                });

            skills["git-blame-summary"] = DefaultSkillBuilder.Build(
                "git-blame-summary", "Summarize authorship and churn",
                "Summarizes the authors of a path and its commit history.",
                false, "git, vcs",
                "The user wants to know who has worked on a file and how often it has changed.",
                new List<DefaultSkillCommandDef>
                {
                    new DefaultSkillCommandDef("authors", "Show commit counts per author for a path.", "pwsh",
@"$ErrorActionPreference = 'Stop'
if ($args.Count -lt 1) { Write-Error 'Usage: authors <path>'; exit 1 }
git shortlog -sn HEAD -- $args[0]
"),
                    new DefaultSkillCommandDef("churn", "List the commits that touched a path.", "pwsh",
@"$ErrorActionPreference = 'Stop'
if ($args.Count -lt 1) { Write-Error 'Usage: churn <path>'; exit 1 }
git log --oneline -- $args[0]
")
                });

            skills["git-secret-scan"] = DefaultSkillBuilder.Build(
                "git-secret-scan", "Scan the staged diff for secrets",
                "Checks staged changes for common credential patterns and fails if any are found.",
                false, "git, security",
                "Before a commit leaves the machine, or when the user asks to check for leaked credentials in staged changes.",
                new List<DefaultSkillCommandDef>
                {
                    new DefaultSkillCommandDef("staged", "Scan the staged diff for common secret patterns.", "pwsh",
@"$ErrorActionPreference = 'Stop'
$diff = git diff --cached
$text = ($diff | Out-String)
$patterns = @('AKIA[0-9A-Z]{16}', '-----BEGIN [A-Z ]*PRIVATE KEY-----', 'ghp_[0-9A-Za-z]{36}', 'xox[baprs]-[0-9A-Za-z-]+')
$hits = 0
foreach ($p in $patterns) {
  $m = [regex]::Matches($text, $p)
  if ($m.Count -gt 0) { Write-Output ""Potential secret ($p): $($m.Count) match(es)""; $hits += $m.Count }
}
if ($hits -gt 0) { exit 1 } else { Write-Output 'No obvious secrets found in the staged diff.' }
")
                });

            skills["git-large-files"] = DefaultSkillBuilder.Build(
                "git-large-files", "Find large files",
                "Reports the largest tracked files and the largest blobs in history.",
                false, "git, hygiene",
                "The user wants to find large files bloating the working tree or the repository history.",
                new List<DefaultSkillCommandDef>
                {
                    new DefaultSkillCommandDef("tree", "List the 20 largest tracked files by size on disk.", "pwsh",
@"$ErrorActionPreference = 'Stop'
git ls-files | ForEach-Object {
  if (Test-Path -LiteralPath $_) {
    $len = (Get-Item -LiteralPath $_).Length
    New-Object PSObject -Property @{ Size = $len; Path = $_ }
  }
} | Sort-Object Size -Descending | Select-Object -First 20 | ForEach-Object {
  Write-Output (""{0,12}  {1}"" -f $_.Size, $_.Path)
}
"),
                    new DefaultSkillCommandDef("history", "List the 20 largest blobs across all history.", "pwsh",
@"$ErrorActionPreference = 'Stop'
git rev-list --objects --all |
  git cat-file --batch-check='%(objecttype) %(objectsize) %(rest)' |
  Where-Object { $_ -match '^blob ' } |
  ForEach-Object {
    $parts = $_ -split ' ', 3
    New-Object PSObject -Property @{ Size = [int64]$parts[1]; Path = $parts[2] }
  } | Sort-Object Size -Descending | Select-Object -First 20 | ForEach-Object {
    Write-Output (""{0,12}  {1}"" -f $_.Size, $_.Path)
  }
")
                });

            return skills;
        }
    }
}
