namespace Mux.Core.Skills
{
    using System.Collections.Generic;

    /// <summary>
    /// The <c>shell-lint</c> default skill: shellcheck for shell scripts and PSScriptAnalyzer for PowerShell, across the
    /// repository, one path, or only the files changed on the branch.
    /// </summary>
    public static class DefaultScriptLintSkills
    {
        #region Private-Members

        private static readonly string[] _AppliesTo = { "*.sh", "**/*.sh", "*.bash", "**/*.bash", "*.ps1", "**/*.ps1", "*.psm1", "**/*.psm1" };

        private const string Setup = @"$script:ScriptSkip = @('.git', 'node_modules', 'bin', 'obj', 'vendor', '.venv', 'venv', 'dist', 'build', 'target', '.terraform')
function Test-MuxShellScript {
    param([string]$Path)
    $ext = [System.IO.Path]::GetExtension($Path).ToLowerInvariant()
    if (@('.sh', '.bash', '.ksh') -contains $ext) { return $true }
    if ($ext) { return $false }
    try { $first = [string](Get-Content -LiteralPath $Path -TotalCount 1 -ErrorAction Stop) } catch { return $false }
    return [bool]($first -match '^#!\s*\S*(/|\s)(sh|bash|dash|ksh)(\s|$)')
}
function Test-MuxPowerShellScript {
    param([string]$Path)
    return (@('.ps1', '.psm1') -contains [System.IO.Path]::GetExtension($Path).ToLowerInvariant())
}
function Get-MuxScriptFiles {
    param([string]$Directory)
    $result = New-Object System.Collections.Generic.List[string]
    $stack = New-Object System.Collections.Generic.Stack[string]
    $stack.Push($Directory)
    while ($stack.Count -gt 0) {
        foreach ($entry in (Get-ChildItem -LiteralPath $stack.Pop() -Force -ErrorAction SilentlyContinue)) {
            if ($entry.PSIsContainer) { if ($script:ScriptSkip -notcontains $entry.Name) { $stack.Push($entry.FullName) }; continue }
            if ($entry.Length -le 1048576) { $result.Add($entry.FullName) }
        }
    }
    return ,$result
}
function Invoke-MuxScriptLint {
    param([string[]]$Files, [string]$Root)
    Set-Location -LiteralPath $Root
    $relative = @($Files | ForEach-Object { [System.IO.Path]::GetRelativePath($Root, [System.IO.Path]::GetFullPath($_, $Root)).Replace('\', '/') } | Sort-Object -Unique)
    $shell = @($relative | Where-Object { Test-MuxShellScript $_ })
    $powershell = @($relative | Where-Object { Test-MuxPowerShellScript $_ })
    if ($shell.Count -eq 0 -and $powershell.Count -eq 0) { Write-Output 'No shell or PowerShell scripts to lint.'; exit 0 }
    $findings = 0
    $ran = 0
    $notes = New-Object System.Collections.Generic.List[string]
    if ($shell.Count -gt 0) {
        if (Test-MuxDryRun) { Write-Output ('DRYRUN: shellcheck -f gcc ' + ($shell -join ' ')) }
        elseif (Test-MuxTool 'shellcheck') {
            $ran++
            Write-Output ('== shellcheck (' + $shell.Count + ' file' + $(if ($shell.Count -ne 1) { 's' }) + ')')
            for ($i = 0; $i -lt $shell.Count; $i += 50) {
                $batch = @($shell[$i..([Math]::Min($i + 49, $shell.Count - 1))])
                foreach ($line in @(& shellcheck -f gcc @batch 2>&1)) {
                    $text = [string]$line
                    if (-not $text.Trim()) { continue }
                    Write-Output $text
                    if ($text -match ':\d+:\d+: (error|warning|note|style)') { $findings++ }
                }
            }
        } else {
            $notes.Add([string]$shell.Count + ' shell script(s) not checked: shellcheck is not installed (https://www.shellcheck.net; brew, apt, or choco install shellcheck).')
        }
    }
    if ($powershell.Count -gt 0) {
        if (Test-MuxDryRun) { Write-Output ('DRYRUN: Invoke-ScriptAnalyzer -Severity Warning,Error on ' + ($powershell -join ' ')) }
        elseif (Get-Module -ListAvailable -Name PSScriptAnalyzer) {
            $ran++
            Import-Module PSScriptAnalyzer -ErrorAction Stop
            Write-Output ('== PSScriptAnalyzer (' + $powershell.Count + ' file' + $(if ($powershell.Count -ne 1) { 's' }) + ')')
            foreach ($file in $powershell) {
                foreach ($record in @(Invoke-ScriptAnalyzer -Path $file -Severity Warning, Error -ErrorAction SilentlyContinue)) {
                    $findings++
                    Write-Output ($file + ':' + $record.Line + ':' + $record.Column + ': ' + ([string]$record.Severity).ToLowerInvariant() + ': ' + $record.RuleName + ': ' + $record.Message)
                }
            }
        } else {
            $notes.Add([string]$powershell.Count + ' PowerShell script(s) not checked: PSScriptAnalyzer is not installed (Install-Module PSScriptAnalyzer -Scope CurrentUser).')
        }
    }
    foreach ($note in $notes) { Write-Output ('note: ' + $note) }
    if (Test-MuxDryRun) { exit 0 }
    if ($ran -eq 0) { Exit-MuxNotApplicable 'no linter could run; install shellcheck or PSScriptAnalyzer.' }
    if ($findings -gt 0) { Write-Output ([string]$findings + ' finding' + $(if ($findings -ne 1) { 's' }) + '.'); exit 1 }
    Write-Output 'No findings.'
    exit 0
}
";

        private const string CheckCode = @"$target = Get-MuxArg -Arguments $args -Index 0
$start = (Get-Location).Path
$root = Get-MuxRepoRoot
if ($target) {
    $full = [System.IO.Path]::GetFullPath($target, $start)
    if (Test-Path -LiteralPath $full -PathType Leaf) {
        if (-not (Test-MuxShellScript $full) -and -not (Test-MuxPowerShellScript $full)) { Exit-MuxNotApplicable ($target + ' is not a shell or PowerShell script (.sh, .bash, .ksh, .ps1, .psm1, or a shell shebang).') }
        Invoke-MuxScriptLint -Files @($full) -Root $root
    }
    if (-not (Test-Path -LiteralPath $full -PathType Container)) { Exit-MuxNotApplicable ('not found: ' + $target) }
    Invoke-MuxScriptLint -Files (Get-MuxScriptFiles $full) -Root $root
}
Invoke-MuxScriptLint -Files (Get-MuxScriptFiles $root) -Root $root
";

        private const string ChangedCode = @"Assert-MuxGitRepo
$root = Get-MuxRepoRoot
Set-Location -LiteralPath $root
$since = Get-MuxChangeBase (Get-MuxArg -Arguments $args -Index 0)
$files = @(Get-MuxChangedFiles -Since $since | Where-Object { Test-Path -LiteralPath (Join-Path $root $_) -PathType Leaf } | ForEach-Object { Join-Path $root $_ })
Invoke-MuxScriptLint -Files $files -Root $root
";

        #endregion

        #region Public-Methods

        /// <summary>Returns the script lint skill definitions.</summary>
        /// <returns>The definitions.</returns>
        public static IReadOnlyList<DefaultSkillDef> All()
        {
            ToolchainSkillFactory factory = new ToolchainSkillFactory(Setup, new[] { "hygiene", "shell", "powershell" }, _AppliesTo, null,
                " Exit codes: 0 no findings, 1 findings, 2 no linter installed, or a bad path.");
            return new List<DefaultSkillDef>
            {
                factory.Skill("shell-lint", "Lint shell and PowerShell scripts",
                    "Lints shell scripts (bash, sh) with shellcheck and PowerShell scripts with PSScriptAnalyzer, across the repository, one path, or the files changed on the branch.",
                    false,
                    "The user asks to lint, check, or review shell scripts, bash scripts, or PowerShell scripts, or after editing one.",
                    "[path | base-branch]",
                    "`check [path]` lints every script under the repository root (or one file or folder), skipping .git, node_modules, bin, obj, vendor, virtual environments, and build output. Shell scripts are `.sh`, `.bash`, `.ksh`, and extension-less files with an sh, bash, dash, or ksh shebang; they go to `shellcheck -f gcc`, which prints `file:line:column: severity: message [SCcode]`. `.ps1` and `.psm1` files go to PSScriptAnalyzer's `Invoke-ScriptAnalyzer` (warnings and errors). `changed [base]` lints only the scripts changed against the base branch, plus untracked ones. A linter that is not installed is noted and its files skipped; the run fails only when neither can run. Read-only.",
                    ToolchainSkillFactory.Command("check", "Lint every script, or one file or folder.", CheckCode),
                    ToolchainSkillFactory.Command("changed", "Lint only the scripts changed against the base branch.", ChangedCode))
            };
        }

        #endregion
    }
}
