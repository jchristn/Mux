namespace Mux.Core.Skills
{
    using System.Collections.Generic;

    /// <summary>
    /// The agent playbook default skills: writing the project's AGENTS.md (<c>init</c>), a debugging procedure
    /// (<c>debug</c>), a <c>git bisect</c> driver that always restores the original HEAD, and a codebase map
    /// (<c>explain-codebase</c>). <c>debug</c> is a pure playbook with no commands.
    /// </summary>
    public static class DefaultAgentPlaybookSkills
    {
        #region Public-Methods

        /// <summary>Returns the agent playbook skill definitions.</summary>
        /// <returns>The definitions.</returns>
        public static IReadOnlyList<DefaultSkillDef> All()
        {
            ToolchainSkillFactory plain = new ToolchainSkillFactory(string.Empty, new[] { "playbook", "project" }, null, null,
                " Exit codes: 0 success, 2 the input is invalid or does not apply here.");
            ToolchainSkillFactory git = new ToolchainSkillFactory(@"Assert-MuxGitRepo
", new[] { "playbook", "git", "debug" }, new[] { ".git" }, null,
                " Exit codes: 0 success, 1 git bisect could not finish, 2 not a git repository, the tree is dirty, or the input is invalid.");

            return new List<DefaultSkillDef>
            {
                plain.Skill("init", "Write the project's AGENTS.md", "Surveys the repository and guides writing or updating AGENTS.md: build, test, and run commands, layout, and conventions.", true,
                    "The user asks to set up the project for agents, run /init, or create or refresh AGENTS.md (or CLAUDE.md).",
                    string.Empty,
                    InitBody,
                    ToolchainSkillFactory.Command("survey", "Print what an AGENTS.md needs: toolchain, layout, entry points, CI, existing instruction files, and the README's opening.", DefaultProjectSkills.DetectScript + @"Push-Location $root
try {
    Write-Output '== Project'
    Write-MuxProjectReport -Report $report
    Write-Output ''
    Write-Output '== Top-level entries'
    Get-ChildItem -Force -ErrorAction SilentlyContinue | Where-Object { $_.Name -notin @('.git', 'node_modules', 'bin', 'obj', '.venv', 'venv', 'dist', 'build', 'target', '.idea', '.vs') } |
        Sort-Object { -not $_.PSIsContainer }, Name | ForEach-Object { Write-Output ('  ' + $_.Name + $(if ($_.PSIsContainer) { '/' } else { '' })) }
    Write-Output ''
    Write-Output '== Likely entry points'
    $entryPatterns = '(^|/)(Program\.cs|main\.(go|rs|py|ts|js|c|cpp|java|kt)|index\.(ts|tsx|js|jsx)|app\.(py|ts|js)|server\.(ts|js|py)|manage\.py|__main__\.py|Main\.java|Application\.java)$'
    $entries = @($files | Where-Object { ($_ -replace '\\', '/') -match $entryPatterns } | Select-Object -First 15)
    if ($entries.Count -eq 0) { Write-Output '  none found by name' } else { $entries | ForEach-Object { Write-Output ('  ' + ($_ -replace '\\', '/')) } }
    Write-Output ''
    Write-Output '== Existing instruction files'
    $instructionFiles = @($files | Where-Object { (Split-Path -Leaf $_) -in @('AGENTS.md', 'CLAUDE.md', 'MUX.md', 'CONTRIBUTING.md', '.cursorrules', 'copilot-instructions.md') })
    if ($instructionFiles.Count -eq 0) { Write-Output '  none' } else { $instructionFiles | ForEach-Object { Write-Output ('  ' + ($_ -replace '\\', '/') + ' (' + @(Get-Content -LiteralPath $_).Count + ' lines)') } }
    if (Test-Path -LiteralPath 'package.json') {
        $pkgSurvey = Get-MuxPackageJson -Dir $root
        if ($pkgSurvey.ContainsKey('scripts') -and $pkgSurvey['scripts'].Count -gt 0) {
            Write-Output ''
            Write-Output '== package.json scripts'
            foreach ($entry in $pkgSurvey['scripts'].GetEnumerator() | Sort-Object Key) { Write-Output ('  ' + $entry.Key + ': ' + $entry.Value) }
        }
    }
    $readme = @('README.md', 'README', 'readme.md', 'README.rst') | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    if ($readme) {
        Write-Output ''
        Write-Output ('== ' + $readme + ' (first 40 lines)')
        Get-Content -LiteralPath $readme -TotalCount 40 | ForEach-Object { Write-Output ('  ' + $_) }
    }
} finally {
    Pop-Location
}")),

                plain.Skill("debug", "Debug a failure systematically", "A step-by-step procedure for finding the real cause of a bug or failing test before changing code.", false,
                    "Something fails (a test, a build, a crash, wrong output) and the cause is not obvious.",
                    "[symptom]",
                    DebugBody),

                git.Skill("git-bisect", "Find the commit that broke something", "Drives git bisect with a test command to find the first bad commit, and always restores the original HEAD.", true,
                    "A behavior worked at an earlier commit and fails now, and a command can tell good from bad.",
                    "<good> [bad] | run <command...>",
                    BisectBody,
                    ToolchainSkillFactory.Command("start", "Start bisecting between a good commit and a bad one (default HEAD).", @"$good = Get-MuxArg -Arguments $args -Index 0
$bad = Get-MuxArg -Arguments $args -Index 1 -Default 'HEAD'
if (-not $good) { Exit-MuxNotApplicable 'pass a known-good commit: git-bisect start <good> [bad]' }
foreach ($ref in @($good, $bad)) { if (-not (Test-MuxGitRef $ref)) { Exit-MuxNotApplicable (""'"" + $ref + ""' is not a commit in this repository."") } }
$dirty = @(& git status --porcelain --untracked-files=no 2>$null)
if ($dirty.Count -gt 0) { Exit-MuxNotApplicable 'the working tree has uncommitted changes; commit or stash them before bisecting.' }
$bisectStart = & git rev-parse --git-path BISECT_START
if (Test-Path -LiteralPath $bisectStart) { Exit-MuxNotApplicable 'a bisect is already in progress; finish it with run or abandon it with reset.' }
$output = (& git bisect start $bad $good 2>&1 | Out-String)
Write-Output $output.TrimEnd()
if ($LASTEXITCODE -ne 0) { & git bisect reset *> $null; exit 2 }
Write-Output 'Bisect started. Next: git-bisect run <command...> (exit 0 = good, 1-124 = bad, 125 = skip).'"),
                    ToolchainSkillFactory.Command("run", "Run the test command at each step, report the first bad commit, and restore HEAD.", @"$bisectStart = & git rev-parse --git-path BISECT_START
if (-not (Test-Path -LiteralPath $bisectStart)) { Exit-MuxNotApplicable 'no bisect in progress; start one with git-bisect start <good> [bad].' }
$command = @($args)
if ($command.Count -eq 0) { Exit-MuxNotApplicable 'pass the test command: git-bisect run <command...>' }
$found = $false
try {
    $output = (& git bisect run @command 2>&1 | Out-String)
    $code = $LASTEXITCODE
    Write-Output $output.TrimEnd()
    $badCommit = & git rev-parse --verify --quiet refs/bisect/bad 2>$null
    if ($code -eq 0 -and $output -match 'is the first bad commit' -and $badCommit) {
        $found = $true
        Write-Output ''
        Write-Output '== First bad commit'
        Write-Output (Get-MuxGitText @('show', '--stat', '--format=%H%n%an <%ae>%n%ad%n%n%s%n%n%b', ""$badCommit"".Trim())).TrimEnd()
    }
} finally {
    & git bisect reset *> $null
    Write-Output ''
    Write-Output ('Bisect reset; HEAD restored to ' + (& git rev-parse --abbrev-ref HEAD) + '.')
}
if (-not $found) { Write-Output 'mux: bisect did not identify a single first bad commit (see the log above).'; exit 1 }"),
                    ToolchainSkillFactory.Command("reset", "Abandon a bisect and restore the original HEAD.", @"$bisectStart = & git rev-parse --git-path BISECT_START
if (-not (Test-Path -LiteralPath $bisectStart)) { Write-Output 'No bisect in progress.'; exit 0 }
& git bisect reset 2>&1 | ForEach-Object { Write-Output $_ }
Write-Output ('HEAD restored to ' + (& git rev-parse --abbrev-ref HEAD) + '.')")),

                plain.Skill("explain-codebase", "Explain how a codebase is organized", "Maps the repository (folders with file counts, languages, entry points) and guides a layered explanation that cites real paths.", false,
                    "The user is new to a repository, or asks how a project is structured or how a request flows through it.",
                    "[depth]",
                    ExplainBody,
                    ToolchainSkillFactory.Command("map", "Print a depth-limited folder map with file counts, languages, and entry points.", DefaultProjectSkills.DetectScript + @"$depth = 2
$parsedDepth = 0
if ([int]::TryParse((Get-MuxArg -Arguments $args -Index 0), [ref]$parsedDepth) -and $parsedDepth -ge 1 -and $parsedDepth -le 4) { $depth = $parsedDepth }
Write-Output ('Project root: ' + $report.root)
$langs = ($report.languages.GetEnumerator() | Select-Object -First 6 | ForEach-Object { $_.Key + ' (' + $_.Value + ')' }) -join ', '
Write-Output ('Languages: ' + $(if ($langs) { $langs } else { 'none detected' }))
foreach ($e in $report.ecosystems) { Write-Output ('Ecosystem: ' + $e.name + ' (' + $e.manager + ')') }
Write-Output ('Map (depth ' + $depth + ', files per folder including subfolders):')
$counts = @{}
foreach ($file in $files) {
    $parts = @(($file -replace '\\', '/') -split '/')
    if ($parts.Count -le 1) { $counts['.'] = 1 + $(if ($counts.ContainsKey('.')) { $counts['.'] } else { 0 }); continue }
    for ($level = 1; $level -le [Math]::Min($depth, $parts.Count - 1); $level++) {
        $key = ($parts[0..($level - 1)] -join '/')
        $counts[$key] = 1 + $(if ($counts.ContainsKey($key)) { $counts[$key] } else { 0 })
    }
}
if ($counts.ContainsKey('.')) { Write-Output ('  (root files): ' + $counts['.']) }
foreach ($key in ($counts.Keys | Where-Object { $_ -ne '.' } | Sort-Object)) {
    $level = @($key -split '/').Count
    Write-Output (('  ' * $level) + (Split-Path -Leaf $key) + '/ (' + $counts[$key] + ')')
}
$entryPatterns = '(^|/)(Program\.cs|main\.(go|rs|py|ts|js|c|cpp|java|kt)|index\.(ts|tsx|js|jsx)|app\.(py|ts|js)|server\.(ts|js|py)|manage\.py|__main__\.py)$'
$entries = @($files | Where-Object { ($_ -replace '\\', '/') -match $entryPatterns } | Select-Object -First 15)
Write-Output 'Entry points:'
if ($entries.Count -eq 0) { Write-Output '  none found by name' } else { $entries | ForEach-Object { Write-Output ('  ' + ($_ -replace '\\', '/')) } }"))
            };
        }

        #endregion

        #region Private-Methods

        private const string InitBody = @"Procedure:

1. Run `survey`. It prints the detected toolchain and suggested skills, the top-level folders, likely entry points, any existing instruction files (AGENTS.md, CLAUDE.md, MUX.md, CONTRIBUTING.md), package.json scripts, and the start of the README.
2. Confirm the commands before writing them down: open the build files, CI workflows, and scripts the survey points to, and prefer what CI actually runs.
3. Write AGENTS.md at the repository root (Codex, Claude Code, and mux all read it) with these sections, in this order, and nothing generic:
   - What this is: one or two sentences.
   - Build, test, lint, and run: exact commands, including how to run a single test.
   - Layout: the folders that matter and what lives in each.
   - Conventions: naming, error handling, testing, and style rules visible in the code or config, not general advice.
   - Gotchas: anything a new contributor would trip on (generated files, required services, environment variables, slow tests).
4. Keep it under 200 lines. Every line should be something an agent could not guess.
5. If AGENTS.md (or CLAUDE.md) already exists, update it instead: keep every human-written section, change only what is out of date or missing, and show the user the diff before saving.";

        private const string DebugBody = @"Symptom reported with the request (may be empty): $ARGUMENTS

Procedure:

1. Reproduce first. Run the failing test, command, or request and capture the exact error, stack trace, and inputs. If it does not reproduce, find out what differs (data, environment, timing, versions) before going further.
2. Read the failure carefully: the first error in the output is usually the real one, and the line in your code nearest the top of the stack trace is the place to start.
3. List two to four hypotheses, most likely first, each one specific enough to test (""the cache returns stale data after an update"", not ""something with caching"").
4. Test the cheapest hypothesis first, with the smallest probe that can rule it in or out: read the code path, add a temporary log line, write a minimal script, or check the data. Change one thing at a time.
5. Narrow until a single cause explains every symptom. If it worked before, git-bisect can find the commit that broke it.
6. Fix the cause, not the symptom. Prefer the smallest change; do not weaken a test to make it pass.
7. Verify: re-run the original reproduction, then the nearby tests. Never declare it fixed without re-running the reproduction.
8. Remove temporary logging and scripts, then report: the cause, the evidence, the fix, and how you verified it.";

        private const string BisectBody = @"Procedure:

1. Find a good commit (one where the behavior works, such as the last release tag) and confirm the current HEAD is bad.
2. Write a test command that exits 0 when the behavior is good and 1 to 124 when it is bad (125 means skip this commit, for example when it does not build). Prefer an existing test filter, for example dotnet test --filter Name or npm test -- -t name. Run it once on HEAD to confirm it fails.
3. Run `start <good> [bad]`. It refuses a dirty working tree, so commit or stash first.
4. Run `run <command...>`, passing the test command as separate arguments. It runs git bisect run, prints the first bad commit with its message and changed files, and always resets to the original HEAD, even on failure.
5. If you stop early, run `reset`.
6. Read the first bad commit's diff and explain which change caused the failure, then fix it on the current branch.";

        private const string ExplainBody = @"Procedure:

1. Run `map [depth]` (depth 1 to 4, default 2). It prints the languages, ecosystems, a folder map with file counts, and likely entry points.
2. Open the entry points and the main configuration or composition root (dependency injection setup, router, main function) to learn how the pieces connect.
3. Explain in three layers, citing real paths:
   - What it does: one paragraph in plain terms.
   - How it is organized: each important folder or project, and what it owns.
   - How a request or command flows: trace one representative path end to end, file by file.
4. Mention the build and test commands and where tests live.
5. Keep it concrete. If something is unclear from the code, say so rather than guessing.";

        #endregion
    }
}
