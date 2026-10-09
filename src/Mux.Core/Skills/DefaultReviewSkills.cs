namespace Mux.Core.Skills
{
    using System.Collections.Generic;

    /// <summary>
    /// The review default skills: code review, security review, simplification, PR review comments, and test gaps.
    /// Each is a hybrid: read-only commands gather the diff or file list deterministically, and the body is the
    /// procedure the model follows, including the exact output format, because a small model follows a format
    /// better than it follows advice.
    /// </summary>
    public static class DefaultReviewSkills
    {
        #region Private-Members

        private const string Setup = @"Assert-MuxGitRepo
";

        private static readonly string[] _AppliesTo = { ".git" };

        private const string UncommittedDiff = @"$hasHead = Test-MuxGitRef 'HEAD'
if ($hasHead) {
    $stat = Get-MuxGitText @('diff', '--stat', 'HEAD')
    $patch = Get-MuxGitText @('diff', 'HEAD')
} else {
    $stat = Get-MuxGitText @('diff', '--stat', '--cached')
    $patch = Get-MuxGitText @('diff', '--cached')
}
$untrackedFiles = @(& git -c core.quotepath=off ls-files --others --exclude-standard 2>$null)
if (-not $patch.Trim() -and $untrackedFiles.Count -eq 0) { Write-Output 'No uncommitted changes.'; exit 0 }
Write-Output '== Uncommitted changes'
if ($stat.Trim()) { Write-Output $stat.TrimEnd() }
Write-MuxLimited $patch
Write-MuxUntrackedFiles
";

        private const string BranchDiff = @"$rest = Remove-MuxEffortWords $args
$base = Get-MuxArg -Arguments $rest -Index 0
if (-not $base) { $base = Get-MuxDefaultBranch }
if (-not $base) { Exit-MuxNotApplicable 'no default branch found (origin/HEAD, main, master, trunk, develop); pass the base branch.' }
$mergeBase = Get-MuxMergeBase $base
Write-Output ('== Changes on this branch compared with ' + $base + ' (merge base ' + $mergeBase.Substring(0, [Math]::Min(12, $mergeBase.Length)) + ')')
$log = Get-MuxGitText @('log', '--oneline', '--no-decorate', ($mergeBase + '..HEAD'))
$stat = Get-MuxGitText @('diff', '--stat', $mergeBase)
$patch = Get-MuxGitText @('diff', $mergeBase)
if (-not $log.Trim() -and -not $patch.Trim()) { Write-Output ('No changes compared with ' + $base + '.'); exit 0 }
if ($log.Trim()) { Write-Output 'Commits:'; Write-Output $log.TrimEnd() }
if ($stat.Trim()) { Write-Output $stat.TrimEnd() }
Write-MuxLimited $patch
";

        private const string PrDiff = @"$rest = Remove-MuxEffortWords $args
$number = Get-MuxArg -Arguments $rest -Index 0
if (-not $number) { Exit-MuxNotApplicable 'pass a pull request number or URL: pr <number>' }
$ghHint = 'Install the GitHub CLI from https://cli.github.com and sign in with gh auth login.'
Invoke-MuxTool -Tool 'gh' -Arguments @('pr', 'view', $number) -InstallHint $ghHint
if (Test-MuxDryRun) { Invoke-MuxTool -Tool 'gh' -Arguments @('pr', 'diff', $number); exit 0 }
$patch = (& gh pr diff $number 2>&1 | Out-String)
if ($LASTEXITCODE -ne 0) { Write-Output $patch.TrimEnd(); exit 2 }
Write-MuxLimited $patch
";

        #endregion

        #region Public-Methods

        /// <summary>Returns the review skill definitions.</summary>
        /// <returns>The definitions.</returns>
        public static IReadOnlyList<DefaultSkillDef> All()
        {
            ToolchainSkillFactory f = new ToolchainSkillFactory(Setup, new[] { "review", "git" }, _AppliesTo, null,
                " Exit codes: 0 success, 2 not a git repository, the ref does not exist, or a required tool is missing.");
            ToolchainSkillFactory gh = new ToolchainSkillFactory(Setup, new[] { "review", "git", "github" }, _AppliesTo, new[] { "gh" },
                " Exit codes: 0 success, 2 not a git repository, gh is missing or not signed in, or the input is invalid.");

            return new List<DefaultSkillDef>
            {
                f.Skill("code-review", "Review code changes", "Reviews uncommitted changes, a branch, a commit, a pull request, or one file for correctness bugs first, then tests and maintainability.", false,
                    "The user asks for a code review, asks whether a change is safe, or wants a second look before committing, pushing, or merging.",
                    "[uncommitted|branch [base]|commit <sha>|pr <n>|file <path>] [quick|deep]",
                    ReviewBody,
                    ToolchainSkillFactory.Command("uncommitted", "Show staged, unstaged, and untracked changes.", UncommittedDiff),
                    ToolchainSkillFactory.Command("branch", "Show commits and changes since the merge base with a base branch (default: the origin default branch).", BranchDiff),
                    ToolchainSkillFactory.Command("commit", "Show one commit's message and changes.", @"$rest = Remove-MuxEffortWords $args
$sha = Get-MuxArg -Arguments $rest -Index 0
if (-not $sha) { Exit-MuxNotApplicable 'pass a commit: commit <sha>' }
if (-not (Test-MuxGitRef $sha)) { Exit-MuxNotApplicable (""'"" + $sha + ""' is not a commit in this repository."") }
Write-MuxLimited (Get-MuxGitText @('show', '--stat', '--patch', '--format=fuller', $sha))"),
                    ToolchainSkillFactory.Command("pr", "Show a GitHub pull request's description and diff (needs gh).", PrDiff),
                    ToolchainSkillFactory.Command("file", "Show one file with line numbers, plus its uncommitted diff.", @"$rest = Remove-MuxEffortWords $args
$path = Get-MuxArg -Arguments $rest -Index 0
if (-not $path) { Exit-MuxNotApplicable 'pass a file: file <path>' }
if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { Exit-MuxNotApplicable ($path + ' does not exist or is not a file.') }
$lines = @(Get-Content -LiteralPath $path)
Write-Output ('== ' + $path + ' (' + $lines.Count + ' lines)')
if (Test-MuxGitRef 'HEAD') {
    $patch = (& git -c core.quotepath=off diff HEAD -- $path 2>$null | Out-String)
    if ($patch.Trim()) { Write-Output '-- uncommitted changes in this file:'; Write-MuxLimited $patch }
}
Write-Output '-- current content:'
$max = [Math]::Min($lines.Count, 3000)
for ($i = 0; $i -lt $max; $i++) { Write-Output (('{0,5}  ' -f ($i + 1)) + $lines[$i]) }
if ($lines.Count -gt $max) { Write-Output ('[mux: file cut at ' + $max + ' lines]') }")),

                f.Skill("security-review", "Review changes for security problems", "Reviews uncommitted changes, a branch, or a pull request for exploitable security problems, with a secret scan and a dependency check.", false,
                    "Before merging changes that touch input handling, authentication, data access, file or process access, or dependencies, or whenever the user asks for a security review.",
                    "[uncommitted|branch [base]|pr <n>]",
                    SecurityBody,
                    ToolchainSkillFactory.Command("uncommitted", "Show uncommitted changes with the secret scan and dependency check.", UncommittedDiff + SecurityTail("($patch + [Environment]::NewLine + (Get-MuxUntrackedAsDiff))", "@(@(& git -c core.quotepath=off diff --name-only HEAD 2>$null) + $untrackedFiles)")),
                    ToolchainSkillFactory.Command("branch", "Show branch changes with the secret scan and dependency check.", BranchDiff + SecurityTail("$patch", "@(& git -c core.quotepath=off diff --name-only $mergeBase 2>$null)")),
                    ToolchainSkillFactory.Command("pr", "Show a pull request with the secret scan and dependency check (needs gh).", PrDiff + SecurityTail("$patch", @"@(($patch -split ""`r?`n"") | Where-Object { $_ -match '^\+\+\+ b/' } | ForEach-Object { $_.Substring(6) })"))),

                f.Skill("simplify", "Simplify recently changed code", "Lists the files changed on this branch so the model can remove duplication, dead code, and needless complexity without changing behavior.", true,
                    "After a feature works, before review, or when the user asks to clean up or simplify recent changes.",
                    "[base]",
                    SimplifyBody,
                    ToolchainSkillFactory.Command("changed-files", "List files changed since the base (committed, uncommitted, and untracked).", @"$base = Get-MuxArg -Arguments $args -Index 0
$since = Get-MuxChangeBase $base
$files = @(Get-MuxChangedFiles -Since $since)
if ($files.Count -eq 0) { Write-Output 'No changed files.'; exit 0 }
$label = if ($since -eq 'HEAD') { 'uncommitted changes' } elseif ($since) { 'changes since ' + $since.Substring(0, [Math]::Min(12, $since.Length)) } else { 'files in a repository with no commits' }
Write-Output ('Changed files (' + $label + '):')
$numstat = @{}
if ($since) { @(& git -c core.quotepath=off diff --numstat $since 2>$null) | ForEach-Object { $parts = $_ -split ""`t""; if ($parts.Count -ge 3) { $numstat[$parts[2]] = '+' + $parts[0] + ' -' + $parts[1] } } }
foreach ($file in $files) { Write-Output ('- ' + $file + $(if ($numstat.ContainsKey($file)) { '  (' + $numstat[$file] + ')' } else { '  (new)' })) }")),

                gh.Skill("pr-comments", "Fetch pull request review comments", "Lists the review threads on a GitHub pull request, unresolved first, so they can be addressed.", false,
                    "The user asks to address, answer, or summarize review feedback on a pull request.",
                    "[pr-number] [--from-file threads.json]",
                    PrCommentsBody,
                    ToolchainSkillFactory.Command("list", "List review threads, unresolved first.", @"$fromFile = ''
$rest = New-Object System.Collections.Generic.List[string]
$all = @($args)
for ($i = 0; $i -lt $all.Count; $i++) { if ($all[$i] -eq '--from-file' -and ($i + 1) -lt $all.Count) { $fromFile = [string]$all[$i + 1]; $i++ } else { $rest.Add([string]$all[$i]) } }
if ($fromFile) {
    if (-not (Test-Path -LiteralPath $fromFile)) { Exit-MuxNotApplicable ($fromFile + ' does not exist.') }
    try { $data = Get-Content -LiteralPath $fromFile -Raw | ConvertFrom-Json } catch { Exit-MuxNotApplicable ($fromFile + ' is not valid JSON.') }
} else {
    $ghHint = 'Install the GitHub CLI from https://cli.github.com and sign in with gh auth login.'
    $number = Get-MuxArg -Arguments $rest.ToArray() -Index 0
    $query = 'query($owner:String!,$repo:String!,$number:Int!){repository(owner:$owner,name:$repo){pullRequest(number:$number){title reviewThreads(first:100){nodes{isResolved path line comments(first:20){nodes{author{login} body}}}}}}}'
    if (Test-MuxDryRun) {
        Invoke-MuxTool -Tool 'gh' -Arguments @('repo', 'view', '--json', 'owner,name')
        if (-not $number) { Invoke-MuxTool -Tool 'gh' -Arguments @('pr', 'view', '--json', 'number') }
        Invoke-MuxTool -Tool 'gh' -Arguments @('api', 'graphql', '-f', 'query=<reviewThreads query>', '-F', ('number=' + $(if ($number) { $number } else { '<current>' })))
        exit 0
    }
    if (-not (Test-MuxTool 'gh')) { Exit-MuxNotApplicable $ghHint }
    $repo = & gh repo view --json owner,name 2>$null | ConvertFrom-Json
    if (-not $repo) { Exit-MuxNotApplicable ('could not read the GitHub repository. ' + $ghHint) }
    if (-not $number) {
        $current = & gh pr view --json number 2>$null | ConvertFrom-Json
        if (-not $current) { Exit-MuxNotApplicable 'the current branch has no pull request; pass its number.' }
        $number = [string]$current.number
    }
    $json = & gh api graphql -f ('query=' + $query) -F ('owner=' + $repo.owner.login) -F ('repo=' + $repo.name) -F ('number=' + $number) 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0) { Write-Output $json.TrimEnd(); exit 2 }
    try { $data = $json | ConvertFrom-Json } catch { Exit-MuxNotApplicable 'gh returned something that is not JSON.' }
}
$threads = @($data.data.repository.pullRequest.reviewThreads.nodes)
if ($data.data.repository.pullRequest.title) { Write-Output ('Pull request: ' + $data.data.repository.pullRequest.title) }
if ($threads.Count -eq 0) { Write-Output 'No review threads.'; exit 0 }
Write-MuxReviewThreads $threads")),

                f.Skill("test-gap-review", "Find changes without tests", "Lists changed source files that have no matching test change, using each language's test naming conventions.", false,
                    "Before merging, or when the user asks whether a change is tested.",
                    "[base]",
                    TestGapBody,
                    ToolchainSkillFactory.Command("report", "List changed source files without a matching test change.", @"$base = Get-MuxArg -Arguments $args -Index 0
$since = Get-MuxChangeBase $base
$files = @(Get-MuxChangedFiles -Since $since)
$codePattern = '(?i)\.(cs|fs|vb|java|kt|scala|go|rs|py|rb|php|swift|c|cc|cpp|cxx|ts|tsx|js|jsx|mjs|cjs)$'
$tests = @($files | Where-Object { ($_ -match $codePattern) -and (Test-MuxTestPath $_) })
$sources = @($files | Where-Object { ($_ -match $codePattern) -and -not (Test-MuxTestPath $_) -and ($_ -notmatch '(?i)(^|/)(migrations?|generated|obj|bin|dist|build|vendor|node_modules)/|\.d\.ts$|\.designer\.cs$|AssemblyInfo\.cs$') })
if ($sources.Count -eq 0) { Write-Output ('No changed source files (' + $tests.Count + ' test files changed).'); exit 0 }
$testStems = @{}
foreach ($test in $tests) { $testStems[(Get-MuxTestStem $test)] = $test }
$gaps = @($sources | Where-Object { -not $testStems.ContainsKey((Get-MuxTestStem $_)) })
Write-Output ('Changed source files: ' + $sources.Count + '; changed test files: ' + $tests.Count)
foreach ($source in $sources) { if ($testStems.ContainsKey((Get-MuxTestStem $source))) { Write-Output ('tested   ' + $source + '  <-  ' + $testStems[(Get-MuxTestStem $source)]) } }
if ($gaps.Count -eq 0) { Write-Output 'Every changed source file has a matching test change.'; exit 0 }
Write-Output ('Source files changed without a matching test change (' + $gaps.Count + '):')
$gaps | ForEach-Object { Write-Output ('- ' + $_) }"))
            };
        }

        #endregion

        #region Private-Methods

        private static string SecurityTail(string patchVariable, string filesExpression)
        {
            return "Write-Output ''\nWrite-Output '== Secret scan'\nWrite-MuxSecretFindings -DiffText " + patchVariable + "\nWrite-Output ''\nWrite-Output '== Dependency check'\nWrite-MuxManifestChanges -Files " + filesExpression + "\n";
        }

        private const string ReviewBody = @"Procedure:

1. Pick the input. `uncommitted` for work in progress, `branch [base]` before opening or merging a PR (the base defaults to the origin default branch), `commit <sha>` for one commit, `pr <n>` for a GitHub pull request, `file <path>` for one file. With no input, review `uncommitted`. Add `quick` to read only the changed hunks, or `deep` to also read the callers and callees of every changed function. The default is in between: read each changed hunk plus enough surrounding code to judge it.
2. Gauge the blast radius before judging the change. For each changed file, find its direct dependents (search for imports of it) and note whether it touches a shared contract (types, interfaces, schemas, models, migrations, auth middleware, public API, environment variables). Rate it critical (shared library, data model or migration, auth, public API contract), high (used by three or more modules, shared config), medium (internal to one module), or low (UI, tests, docs). For critical and high changes, read the callers even at the default effort.
3. Load the review rules: always `${SKILL_DIR}/resources/review-rules/universal.md`, plus the one file under `${SKILL_DIR}/resources/review-rules/languages/` that matches the changed files (python, typescript for .ts/.tsx/.js/.jsx/.mjs, go, swift, kotlin, csharp for .cs/.razor, java, c, cpp, rust, ruby, php, dart). Use them as a checklist; a rule match is a candidate, not a finding, until step 6 confirms it.
4. Look for problems in this order, and spend most of the effort on the first group:
   - Correctness: wrong logic or conditions, off-by-one and bounds, null or missing values, error paths that swallow or mishandle failures, race conditions and shared state, resource leaks (files, connections, locks), broken API or data contracts, and behavior that changed without the callers changing.
   - Tests: changed behavior with no test, tests that cannot fail, and tests that assert the wrong thing.
   - Maintainability: only problems that will cause bugs later (misleading names, duplicated logic that will drift). Skip style preferences.
5. Adversarial pass, at `deep` effort or whenever the user asks for an adversarial or hostile review: go over the change again as three reviewers, and have each name at least one issue or, failing that, the most fragile assumption the code relies on.
   - The saboteur, trying to break it in production: what is the worst input for each changed function, what if each external call fails, times out, or returns garbage, what if each state change runs twice, concurrently, or never, and what if neither branch of a conditional is right.
   - The new hire, who must change this in six months with no context: names that hide intent, magic values, logic that needs three or more files to follow, comments that say what instead of why, and tests that check implementation details instead of behavior.
   - The security auditor: every trust boundary the change crosses (user input, network, database, files, environment), with injection, missing authentication or ownership checks, data exposed in logs or errors, insecure defaults, new dependencies, and secrets.
   Merge duplicates; an issue two reviewers found independently is more likely real.
6. Verify every candidate before reporting it: open the code it depends on and confirm the failure can actually happen. Drop anything you cannot confirm, or report it as a question rather than a finding.
7. Report findings most severe first, in exactly this form, one per finding:

   [severity: high|medium|low] path/to/file.ext:LINE - one-sentence problem
   Failure: the concrete input or state and what goes wrong.
   Fix: the smallest change that fixes it.

8. If there are no findings, say ""No findings."" and list what you checked. Do not invent findings to fill space, and do not edit files unless the user asks.";

        private const string SecurityBody = @"Procedure:

1. Run the command for the input (`uncommitted`, `branch [base]`, or `pr <n>`; with no input, use `uncommitted`). It prints the diff, then a secret scan of the added lines (values are masked), then the dependency manifests that changed.
2. For every changed path that handles outside input, check these, in order:
   - Injection: SQL built from strings, shell or process commands built from input, path traversal (user input in file paths), template or HTML injection (XSS), and LDAP, XPath, or NoSQL queries built from input.
   - Authentication and authorization: new endpoints or handlers without checks, checks done on the client only, missing ownership checks (one user reading another's data), and privilege changes.
   - Secrets and keys: anything the scan flagged, credentials in config or tests, secrets written to logs, and keys with excessive scope.
   - Unsafe data handling: deserialization of untrusted data, SSRF (server fetching user-supplied URLs), open redirects, unsafe file uploads, and missing size or rate limits.
   - Cryptography: home-grown crypto, weak algorithms (MD5 or SHA1 for security, ECB mode), predictable randomness for tokens, and disabled certificate validation.
   - Dependencies: for each changed manifest, run the audit skill the dependency check names (js-deps audit, py-deps audit, dotnet-outdated vulnerable, and so on) and report new or upgraded packages with known advisories. Then check licenses with `python3 ""${SKILL_DIR}/resources/scripts/license_checker.py"" . --policy strict --format json`: a strong copyleft license (GPL, AGPL) pulled into a permissively licensed project is a finding, weak copyleft (LGPL, MPL) needs a note, and an unknown license needs a person to look. Rate upgrades by risk: patches and security fixes now, minor versions batched, major versions as their own task with tests, known breaking changes with a rollback plan.
3. For a new component, endpoint, or data flow (or when the user asks for a threat model), walk STRIDE per element: external entities (spoofing, repudiation), processes (all six), data stores (tampering, repudiation, information disclosure, denial of service), and data flows (tampering, information disclosure, denial of service). Spoofing maps to authentication, tampering to integrity, repudiation to audit logs, information disclosure to encryption and access control, denial of service to limits and redundancy, and elevation of privilege to least privilege. `python3 ""${SKILL_DIR}/resources/scripts/threat_modeler.py"" --component ""<name>"" --assets ""<a,b>"" --json` drafts the table with DREAD scores (each 1 to 10); every threat averaging 7 or more needs a named mitigation before the design ships.
4. Report only issues with a plausible attack path, most severe first, in exactly this form:

   [severity: critical|high|medium|low] path/to/file.ext:LINE - the vulnerability
   Attack: who can trigger it and how, step by step.
   Fix: the smallest change that closes it.

5. If nothing is exploitable, say ""No security findings."" and list the areas you checked. Never print a full secret value, even when quoting a finding.";

        private const string SimplifyBody = @"Procedure:

1. Run `changed-files [base]` to get the scope. Work only on those files, and only on the code that changed in them.
2. Read each file and look for:
   - Duplicated logic that could be one function, or that repeats a helper the codebase already has (search before writing a new one).
   - Dead code: unused parameters, variables, branches, imports, and functions the change left behind.
   - Needless abstraction: interfaces with one implementation, wrappers that only forward, and configuration nobody sets.
   - Hand-written versions of standard library or framework functions.
   - Inefficiency that matters: repeated work inside loops, quadratic searches over growing collections, and loading data twice.
3. Fix one thing at a time with small edits. Keep behavior identical: same inputs, same outputs, same errors, same side effects. When a change would alter behavior, list it as a suggestion instead of making it.
4. After the edits, run the project's tests (the language skills, for example js-test all, py-test all, or dotnet-test all) and fix anything you broke.
5. Finish with a short list: each simplification, the file, and why it is equivalent.";

        private const string PrCommentsBody = @"Procedure:

1. Run `list` (it finds the pull request for the current branch) or `list <number>`. Unresolved threads come first, each with its file, line, and every comment in the thread. `list --from-file <path>` formats a saved GraphQL response instead of calling GitHub.
2. Group the unresolved threads by file. For each one, decide:
   - Clear request: make the change, keep it minimal, and note the thread it answers.
   - Question: draft a reply that answers it, citing code.
   - Disagreement or ambiguous request: do not change code; summarize the options and ask the user which to take.
3. Leave resolved threads alone unless the user asks about them.
4. After changing code, run the relevant tests.
5. Finish with a table of threads: file:line, what was asked, and what you did (changed, replied, needs the user). Never resolve threads or post replies on GitHub yourself; the user does that.";

        private const string TestGapBody = @"Procedure:

1. Run `report [base]`. It pairs each changed source file with changed test files by name, using common conventions (Foo.cs and FooTests.cs, foo.ts and foo.test.ts, app.py and test_app.py, main.go and main_test.go, Foo.java and FooTest.java).
2. A file listed as a gap may still be tested elsewhere: search the test folders for its main types and functions before deciding.
3. For each real gap, judge whether a test is warranted. Behavior changes, bug fixes, and new branches need tests; pure renames, logging, and comments do not.
4. For each warranted test, write it next to the existing tests in the project's style and framework: one test for the main path and one for the edge or failure the change handles. Run the tests and make sure the new ones pass, and that they would fail without the change when that is cheap to check.
5. Finish with a list: each gap, whether you added a test, and why not when you did not.";

        #endregion
    }
}
