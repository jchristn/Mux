namespace Mux.Core.Skills
{
    using System.Collections.Generic;

    /// <summary>
    /// The loop default skills: repeat a command until it succeeds, iterate a fix until the build and tests are
    /// green, watch CI, and hunt flaky tests. These are skill-level loops that run inside one turn; the harness-level
    /// <c>/loop</c> (a prompt re-submitted across turns) lives in <c>Mux.Core.Jobs.LoopScheduler</c>. Every loop is
    /// bounded by an attempt count, a timeout, or both.
    /// </summary>
    public static class DefaultLoopSkills
    {
        #region Private-Members

        private const int LongTimeoutMs = 30 * 60 * 1000;

        private static readonly string[] _ProjectFiles =
        {
            "*.sln", "*.slnx", "**/*.csproj", "**/*.fsproj", "package.json", "pyproject.toml", "requirements*.txt", "setup.py",
            "go.mod", "Cargo.toml", "pom.xml", "build.gradle", "build.gradle.kts", "CMakeLists.txt"
        };

        private static readonly string[] _WorkflowFiles = { ".github/workflows/*.yml", ".github/workflows/*.yaml" };

        private const string LoopUntilCode = @"$all = @($args)
if ($all.Count -lt 3) { Exit-MuxNotApplicable 'usage: run <maxAttempts 1-100> <intervalSeconds 0-600> <command> [arguments...]' }
$max = Get-MuxBoundedInt -Value ([string]$all[0]) -Min 1 -Max 100 -Name 'maxAttempts'
$interval = Get-MuxBoundedInt -Value ([string]$all[1]) -Min 0 -Max 600 -Name 'intervalSeconds'
$tool = [string]$all[2]
$toolArgs = @()
if ($all.Count -gt 3) { $toolArgs = @($all[3..($all.Count - 1)] | ForEach-Object { [string]$_ }) }
$line = Format-MuxCommand -Tool $tool -Arguments $toolArgs
Write-Output ('Running ' + $line + ' up to ' + $max + ' times, ' + $interval + 's apart, until it exits 0.')
if (Test-MuxDryRun) { Write-Output ('DRYRUN: ' + $line); exit 0 }
for ($attempt = 1; $attempt -le $max; $attempt++) {
    $watch = [System.Diagnostics.Stopwatch]::StartNew()
    Invoke-MuxCaptured -Tool $tool -Arguments $toolArgs
    Write-Output ('attempt ' + $attempt + '/' + $max + ': exit ' + $script:MuxCapturedExit + ' in ' + [Math]::Round($watch.Elapsed.TotalSeconds, 1) + 's')
    if ($script:MuxCapturedExit -eq 0) {
        Write-Output ('== Succeeded on attempt ' + $attempt + '. Output:')
        Write-MuxTail -Text $script:MuxCapturedOutput -Count 40
        exit 0
    }

    if ($attempt -lt $max -and $interval -gt 0) { Start-Sleep -Seconds $interval }
}

Write-Output ('== Failed all ' + $max + ' attempts. Output of the last attempt:')
Write-MuxTail -Text $script:MuxCapturedOutput -Count 60
exit 1
";

        private const string CheckCode = @"$mode = (Get-MuxArg -Arguments $args -Index 0 -Default 'all').ToLowerInvariant()
if (@('all', 'build', 'test') -notcontains $mode) { Exit-MuxNotApplicable (""check takes all, build, or test (got '"" + $mode + ""')."") }
$plan = Get-MuxCheckPlan
Write-Output ('Project: ' + $plan.Kind)
$failed = ''
if ($mode -ne 'test') {
    if ($plan.Build) {
        Invoke-MuxCheckStep -Name 'build' -Step $plan.Build
        if ($script:MuxStepExit -ne 0) { $failed = 'build' }
    } else {
        Write-Output '== build: no separate build step for this toolchain'
    }
}
if ($mode -ne 'build') {
    if ($failed) {
        Write-Output '== tests: skipped because the build failed'
    } else {
        Invoke-MuxCheckStep -Name 'tests' -Step $plan.Test
        if ($script:MuxStepExit -ne 0) { $failed = 'tests' }
    }
}
if ($failed) { Write-Output ('RESULT: red (' + $failed + ' failed). Fix the first failure above, then run check again.'); exit 1 }
if (Test-MuxDryRun) { Write-Output 'RESULT: dry run (nothing was executed)'; exit 0 }
Write-Output 'RESULT: green'
";

        private const string CiStatusCode = @"$o = Split-MuxOptions -Arguments $args -Names @('--from-file')
$fromFile = [string]$o.Options['--from-file']
$branch = Get-MuxArg -Arguments $o.Rest.ToArray() -Index 0
if (-not $branch -and -not $fromFile) { $branch = (& git rev-parse --abbrev-ref HEAD 2>$null | Out-String).Trim() }
$ghArgs = @('run', 'list', '--limit', '10', '--json', 'databaseId,workflowName,displayTitle,status,conclusion,headBranch,event,createdAt,url')
if ($branch -and $branch -ne 'HEAD') { $ghArgs += @('--branch', $branch) }
if ((Test-MuxDryRun) -and -not $fromFile) { Write-Output ('DRYRUN: ' + (Format-MuxCommand -Tool 'gh' -Arguments $ghArgs)); exit 0 }
$runs = @(Get-MuxGhJson -FromFile $fromFile -Arguments $ghArgs)
$scope = if ($branch -and $branch -ne 'HEAD') { ' on ' + $branch } else { '' }
if ($runs.Count -eq 0) { Write-Output ('No workflow runs' + $scope + '.'); exit 0 }
Write-Output ('Workflow runs' + $scope + ', newest first:')
foreach ($run in $runs) {
    $state = if ([string]$run.status -eq 'completed') { [string]$run.conclusion } else { [string]$run.status }
    Write-Output ('{0}  {1,-12} {2}  [{3}]  {4}' -f $run.databaseId, $state, $run.workflowName, $run.headBranch, $run.displayTitle)
}
$latest = $runs[0]
if ([string]$latest.status -ne 'completed') { Write-Output ('Latest run ' + $latest.databaseId + ' is ' + $latest.status + '; use watch ' + $latest.databaseId + '.'); exit 0 }
if (@('success', 'skipped', 'neutral') -contains [string]$latest.conclusion) { Write-Output ('Latest run ' + $latest.databaseId + ' passed.'); exit 0 }
Write-Output ('Latest run ' + $latest.databaseId + ' ended with ' + $latest.conclusion + '; use failed-logs ' + $latest.databaseId + '.')
exit 1
";

        private const string CiWatchCode = @"$o = Split-MuxOptions -Arguments $args -Names @('--timeout', '--interval', '--from-file')
$fromFile = [string]$o.Options['--from-file']
$timeout = 600
if ($o.Options.ContainsKey('--timeout')) { $timeout = Get-MuxBoundedInt -Value $o.Options['--timeout'] -Min 0 -Max 1500 -Name '--timeout' }
$interval = 15
if ($o.Options.ContainsKey('--interval')) { $interval = Get-MuxBoundedInt -Value $o.Options['--interval'] -Min 5 -Max 120 -Name '--interval' }
$id = Get-MuxArg -Arguments $o.Rest.ToArray() -Index 0
if ($id -and $id -notmatch '^\d+$') { Exit-MuxNotApplicable (""'"" + $id + ""' is not a workflow run id."") }
$viewArgs = @('run', 'view', '<id>', '--json', 'databaseId,status,conclusion,workflowName,url,jobs')
if (-not $id -and -not $fromFile) {
    if (Test-MuxDryRun) {
        Write-Output 'DRYRUN: gh run list --limit 1 --json databaseId'
        $viewArgs[2] = '<latest>'
        Write-Output ('DRYRUN: ' + (Format-MuxCommand -Tool 'gh' -Arguments $viewArgs))
        exit 0
    }
    $branch = (& git rev-parse --abbrev-ref HEAD 2>$null | Out-String).Trim()
    $listArgs = @('run', 'list', '--limit', '1', '--json', 'databaseId')
    if ($branch -and $branch -ne 'HEAD') { $listArgs += @('--branch', $branch) }
    $latest = @(Get-MuxGhJson -Arguments $listArgs)
    if ($latest.Count -eq 0) { Write-Output 'No workflow runs to watch.'; exit 0 }
    $id = [string]$latest[0].databaseId
}
$viewArgs[2] = $id
if ((Test-MuxDryRun) -and -not $fromFile) { Write-Output ('DRYRUN: ' + (Format-MuxCommand -Tool 'gh' -Arguments $viewArgs)); exit 0 }
$watch = [System.Diagnostics.Stopwatch]::StartNew()
$last = ''
while ($true) {
    $run = Get-MuxGhJson -FromFile $fromFile -Arguments $viewArgs
    $jobs = @($run.jobs)
    $done = @($jobs | Where-Object { [string]$_.status -eq 'completed' }).Count
    $state = [string]$run.status
    $summary = $state + ' (' + $done + ' of ' + $jobs.Count + ' jobs done)'
    if ($summary -ne $last) { Write-Output ('[' + [Math]::Round($watch.Elapsed.TotalSeconds) + 's] run ' + $run.databaseId + ' ' + $run.workflowName + ': ' + $summary); $last = $summary }
    if ($state -eq 'completed') {
        $failedJobs = @($jobs | Where-Object { [string]$_.conclusion -and @('success', 'skipped', 'neutral') -notcontains [string]$_.conclusion })
        foreach ($job in $failedJobs) { Write-Output ('  failed job: ' + $job.name + ' (' + $job.conclusion + ')') }
        Write-Output ('RESULT: ' + $run.conclusion)
        if (@('success', 'skipped', 'neutral') -contains [string]$run.conclusion) { exit 0 }
        Write-Output ('Next: failed-logs ' + $run.databaseId)
        exit 1
    }
    if ($watch.Elapsed.TotalSeconds -ge $timeout) {
        Write-Output ('RESULT: still running after ' + [Math]::Round($watch.Elapsed.TotalSeconds) + 's; call watch ' + $run.databaseId + ' again.')
        exit 0
    }
    Start-Sleep -Seconds $interval
}
";

        private const string CiFailedLogsCode = @"$o = Split-MuxOptions -Arguments $args -Names @('--from-file', '--lines')
$fromFile = [string]$o.Options['--from-file']
$perStep = 60
if ($o.Options.ContainsKey('--lines')) { $perStep = Get-MuxBoundedInt -Value $o.Options['--lines'] -Min 5 -Max 400 -Name '--lines' }
$id = Get-MuxArg -Arguments $o.Rest.ToArray() -Index 0
if ($id -and $id -notmatch '^\d+$') { Exit-MuxNotApplicable (""'"" + $id + ""' is not a workflow run id."") }
if ($fromFile) {
    if (-not (Test-Path -LiteralPath $fromFile -PathType Leaf)) { Exit-MuxNotApplicable ($fromFile + ' does not exist.') }
    $text = Get-Content -LiteralPath $fromFile -Raw
} else {
    if (-not $id) {
        if (Test-MuxDryRun) { Write-Output 'DRYRUN: gh run list --status failure --limit 1 --json databaseId'; Write-Output 'DRYRUN: gh run view <latest-failed> --log-failed'; exit 0 }
        $branch = (& git rev-parse --abbrev-ref HEAD 2>$null | Out-String).Trim()
        $listArgs = @('run', 'list', '--status', 'failure', '--limit', '1', '--json', 'databaseId')
        if ($branch -and $branch -ne 'HEAD') { $listArgs += @('--branch', $branch) }
        $latest = @(Get-MuxGhJson -Arguments $listArgs)
        if ($latest.Count -eq 0) { Write-Output 'No failed workflow runs.'; exit 0 }
        $id = [string]$latest[0].databaseId
    }
    if (Test-MuxDryRun) { Write-Output ('DRYRUN: gh run view ' + $id + ' --log-failed'); exit 0 }
    Invoke-MuxCaptured -Tool 'gh' -Arguments @('run', 'view', $id, '--log-failed') -InstallHint 'Install the GitHub CLI from https://cli.github.com and sign in with gh auth login.'
    if ($script:MuxCapturedExit -ne 0) { Write-Output $script:MuxCapturedOutput.TrimEnd(); exit 2 }
    $text = $script:MuxCapturedOutput
}
$groups = [ordered]@{}
foreach ($raw in (($text -replace ""`r"", '') -split ""`n"")) {
    if (-not $raw.Trim()) { continue }
    $parts = $raw -split ""`t"", 3
    if ($parts.Count -eq 3) { $key = $parts[0] + ' / ' + $parts[1]; $line = $parts[2] } else { $key = '(log)'; $line = $raw }
    $line = $line -replace '^\d{4}-\d\d-\d\dT[\d:.]+Z ?', ''
    if (-not $groups.Contains($key)) { $groups[$key] = New-Object System.Collections.Generic.List[string] }
    $groups[$key].Add($line)
}
$label = if ($id) { ' for run ' + $id } else { '' }
if ($groups.Count -eq 0) { Write-Output ('No failed-step logs' + $label + '.'); exit 0 }
Write-Output ('Failed steps' + $label + ': ' + $groups.Count)
foreach ($key in $groups.Keys) {
    $lines = $groups[$key]
    Write-Output ''
    Write-Output ('== ' + $key + ' (' + $lines.Count + ' lines)')
    Write-MuxTail -Text ($lines -join ""`n"") -Count $perStep
}
";

        private const string FlakyCode = @"$all = @($args)
if ($all.Count -lt 2) { Exit-MuxNotApplicable 'usage: run <count 2-50> <test filter>, or run <count> -- <command> [arguments...]' }
$count = Get-MuxBoundedInt -Value ([string]$all[0]) -Min 2 -Max 50 -Name 'count'
if ([string]$all[1] -eq '--') {
    if ($all.Count -lt 3) { Exit-MuxNotApplicable 'pass the command after --.' }
    $toolArgs = @()
    if ($all.Count -gt 3) { $toolArgs = @($all[3..($all.Count - 1)] | ForEach-Object { [string]$_ }) }
    $step = @{ Tool = [string]$all[2]; Arguments = $toolArgs; Hint = '' }
} else {
    $filter = (@($all[1..($all.Count - 1)] | ForEach-Object { [string]$_ }) -join ' ').Trim()
    if (-not $filter) { Exit-MuxNotApplicable 'pass a test filter.' }
    $plan = Get-MuxCheckPlan -Filter $filter
    Write-Output ('Project: ' + $plan.Kind)
    $step = $plan.Test
}
$line = Format-MuxCommand -Tool $step.Tool -Arguments $step.Arguments
Write-Output ('Running ' + $line + ' ' + $count + ' times.')
if (Test-MuxDryRun) { Write-Output ('DRYRUN: ' + $line); exit 0 }
$passed = 0
$failed = 0
$firstFailure = $null
$firstFailureRun = 0
for ($i = 1; $i -le $count; $i++) {
    $watch = [System.Diagnostics.Stopwatch]::StartNew()
    Invoke-MuxCaptured -Tool $step.Tool -Arguments $step.Arguments -InstallHint $step.Hint
    $seconds = [Math]::Round($watch.Elapsed.TotalSeconds, 1)
    if ($script:MuxCapturedExit -eq 0) {
        $passed++
        Write-Output ('run ' + $i + '/' + $count + ': pass in ' + $seconds + 's')
    } else {
        $failed++
        Write-Output ('run ' + $i + '/' + $count + ': FAIL with exit ' + $script:MuxCapturedExit + ' in ' + $seconds + 's')
        if ($null -eq $firstFailure) { $firstFailure = $script:MuxCapturedOutput; $firstFailureRun = $i }
    }
}
Write-Output ('Passed ' + $passed + ' of ' + $count + ', failed ' + $failed + '.')
if ($failed -eq 0) { Write-Output ('VERDICT: no failures in ' + $count + ' runs.'); exit 0 }
if ($passed -eq 0) { Write-Output 'VERDICT: failed every run; this is a consistent failure, not a flaky one.' }
else { Write-Output ('VERDICT: flaky (' + [Math]::Round(100.0 * $failed / $count) + '% of runs failed).') }
Write-Output ''
Write-Output ('== Output of the first failing run (#' + $firstFailureRun + ')')
Write-MuxTail -Text $firstFailure -Count 80
exit 1
";

        private const string FixUntilGreenBody = @"Procedure:

1. Iteration budget: the number in the request, if any, otherwise 5. Request: $ARGUMENTS
2. Run `check`. It detects the toolchain, runs the build and then the tests, and prints PASS or FAIL for each with the tail of the failing output, then `RESULT: green` or `RESULT: red`.
3. If green, stop and report.
4. If red, fix only the first failure shown: read the failing code and the error, make the smallest change that addresses the cause, and do not touch unrelated code. Never weaken, delete, or skip a test or an assertion to make it pass unless the user said to; if a test itself is wrong, say why and ask.
5. Run `check` again (use `check build` or `check test` to repeat only the step that failed, then a final full `check`).
6. Stop when green, when the budget is used up, or when the same failure survives two different fixes; in the last two cases say what you tried and what you think is wrong.
7. Report each iteration on one line: what failed, what you changed (file and a few words), and the result.
";

        private const string LoopUntilBody = @"Use this instead of retrying a command by hand when waiting on something outside your control: a service coming up, a port opening, a deployment rolling out, a file appearing.

1. Run `run <maxAttempts> <intervalSeconds> <command> [arguments...]`. The command runs directly (no shell), so pass a shell explicitly if you need pipes, for example `run 10 6 pwsh -NoProfile -Command ""Test-Connection localhost -TcpPort 8080 -Quiet -ErrorAction Stop""`.
2. Keep maxAttempts times intervalSeconds under 25 minutes; the command is stopped after 30.
3. Each attempt prints its exit code. On success you get the successful output; on failure, the last attempt's output.
4. Do not use it to retry a failing build or test hoping it passes; use `fix-until-green` for that, or `flaky-test-hunt` to measure flakiness.
";

        private const string CiWatchBody = @"Procedure:

1. Run `status` to see the newest workflow runs for the current branch. It exits 1 when the newest run failed.
2. If a run is queued or in progress, run `watch [run-id]` (default: the newest run). It polls until the run completes or the timeout passes (`--timeout` seconds, default 600, at most 1500; `--interval` seconds, default 15) and prints `RESULT: success`, `RESULT: failure`, or `RESULT: still running`. Call it again while it is still running.
3. When a run failed, run `failed-logs [run-id]` (default: the newest failed run). It prints only the failed steps, grouped by job and step, with the last lines of each (`--lines`, default 60).
4. Read the first error in the failed step, find the cause in the code, and report it with the file and line. If the user asked you to fix it, fix it, check locally with `fix-until-green`, and only push when they say so.
";

        private const string FlakyBody = @"Procedure:

1. Run `run <count> <test filter>` (count 2 to 50; start with 10). The filter goes to the detected runner: `dotnet test --filter`, Vitest or Jest `-t`, Mocha `--grep`, `node --test --test-name-pattern`, pytest `-k`, `go test -run`, `cargo test <name>`, Maven `-Dtest=`, Gradle `--tests`, or `ctest -R`. For anything else use `run <count> -- <command> [arguments...]`.
2. Read the verdict. `no failures` means it did not reproduce in that many runs; say so and suggest a larger count before calling it fixed. `failed every run` means a consistent failure: debug it normally. `flaky` means it sometimes fails.
3. For a flaky test, read the first failing output and the test, and look for the usual causes: shared state between tests, test order, time and time zones, randomness without a fixed seed, real network or file system use, timeouts that are too tight, and concurrency (missing awaits, races, unsynchronized collections).
4. Propose a fix for the cause, not a retry. After a fix, run the same count again and report both results.
";

        #endregion

        #region Public-Methods

        /// <summary>Returns the loop skill definitions.</summary>
        /// <returns>The definitions.</returns>
        public static IReadOnlyList<DefaultSkillDef> All()
        {
            ToolchainSkillFactory plain = new ToolchainSkillFactory(null, new[] { "loop" }, null, null,
                " Exit codes: 0 success, 1 the command or check failed, 2 invalid input, a missing tool, or no supported project.");
            ToolchainSkillFactory project = new ToolchainSkillFactory(null, new[] { "loop", "test" }, _ProjectFiles, null,
                " Exit codes: 0 green (or no failures), 1 a build or test failed, 2 invalid input, a missing tool, or no supported project.");
            ToolchainSkillFactory github = new ToolchainSkillFactory(null, new[] { "loop", "ci", "github" }, _WorkflowFiles, new[] { "gh" },
                " Exit codes: 0 success or still running, 1 the run failed, 2 invalid input, gh is missing or not signed in.");

            return new List<DefaultSkillDef>
            {
                plain.Skill("loop-until", "Retry a command until it succeeds", "Re-runs a command until it exits 0 or the attempts run out, printing each attempt's exit code.", true,
                    "Waiting for something outside your control to become ready, such as a service, port, deployment, or file.",
                    "<maxAttempts> <intervalSeconds> <command> [arguments...]",
                    LoopUntilBody,
                    Long(ToolchainSkillFactory.Command("run", "Run the command up to maxAttempts times (1-100), intervalSeconds apart (0-600), until it exits 0.", LoopUntilCode))),

                project.Skill("fix-until-green", "Fix until the build and tests pass", "Runs the detected build and tests, then fixes one failure at a time until everything passes or the iteration budget runs out.", true,
                    "The build or tests are failing and the user wants them fixed, or after a change that should leave everything green.",
                    "[iterations]",
                    FixUntilGreenBody,
                    Long(ToolchainSkillFactory.Command("check", "Run the detected build and then the tests once and print a pass or fail summary (all, build, or test).", CheckCode))),

                github.Skill("ci-watch", "Watch GitHub Actions runs", "Shows the branch's workflow runs, waits for a run to finish, and prints only the logs of failed steps.", false,
                    "After pushing, when the user asks whether CI passed, or when a CI run failed and needs diagnosing.",
                    "[status|watch [run-id]|failed-logs [run-id]]",
                    CiWatchBody,
                    ToolchainSkillFactory.Command("status", "List the newest workflow runs for the current branch (or a named branch).", CiStatusCode),
                    Long(ToolchainSkillFactory.Command("watch", "Wait for a run to finish (--timeout seconds, default 600) and print its result and failed jobs.", CiWatchCode)),
                    ToolchainSkillFactory.Command("failed-logs", "Print the failed steps' logs of a run, trimmed to the last lines of each step.", CiFailedLogsCode)),

                project.Skill("flaky-test-hunt", "Hunt a flaky test", "Runs a test filter (or any command) many times and reports how often it fails, with the first failing output.", false,
                    "A test fails sometimes but not always, or before declaring a flaky test fixed.",
                    "<count> <test filter> | <count> -- <command...>",
                    FlakyBody,
                    Long(ToolchainSkillFactory.Command("run", "Run the test filter (or the command after --) count times (2-50) and report pass and fail counts.", FlakyCode)))
            };
        }

        #endregion

        #region Private-Methods

        private static DefaultSkillCommandDef Long(DefaultSkillCommandDef command)
        {
            command.TimeoutMs = LongTimeoutMs;
            return command;
        }

        #endregion
    }
}
