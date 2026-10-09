namespace Mux.Core.Skills
{
    using System.Collections.Generic;

    /// <summary>
    /// Version upgrade skills: <c>dotnet-upgrade</c>, <c>node-upgrade</c>, and <c>py-upgrade</c> find every place a
    /// project pins its runtime version (project files, global.json, .nvmrc, package.json engines, pyproject.toml, CI
    /// workflows, Dockerfiles), print the exact edits as a plan, and apply only those edits. Anything they cannot edit
    /// safely (version matrices, classifiers, framework packages) is reported as a note instead.
    /// </summary>
    public static class DefaultUpgradeSkills
    {
        #region Private-Members

        private const string Setup = @"$script:UpgradeSkip = @('.git', 'node_modules', 'bin', 'obj', 'vendor', '.venv', 'venv', 'dist', 'build', 'target', '.terraform', '__pycache__')
function Get-MuxUpgradeFiles {
    param([string]$Root, [string[]]$Patterns, [int]$Depth = 6)
    $result = New-Object System.Collections.Generic.List[string]
    $queue = New-Object System.Collections.Generic.Queue[object]
    $queue.Enqueue(@($Root, 0))
    while ($queue.Count -gt 0) {
        $item = $queue.Dequeue()
        foreach ($entry in (Get-ChildItem -LiteralPath $item[0] -Force -ErrorAction SilentlyContinue)) {
            if ($entry.PSIsContainer) {
                if (($script:UpgradeSkip -notcontains $entry.Name) -and $item[1] -lt $Depth) { $queue.Enqueue(@($entry.FullName, ($item[1] + 1))) }
                continue
            }
            foreach ($pattern in $Patterns) { if ($entry.Name -like $pattern) { $result.Add($entry.FullName); break } }
        }
    }
    return ,@($result | Sort-Object)
}
function Get-MuxRelative { param([string]$Root, [string]$Path) return [System.IO.Path]::GetRelativePath($Root, $Path).Replace('\', '/') }
# One planned edit: a file, a regex, a replacement evaluator, and a description. Plan prints; apply writes.
$script:UpgradeEdits = New-Object System.Collections.Generic.List[object]
$script:UpgradeNotes = New-Object System.Collections.Generic.List[string]
function Add-MuxUpgradeEdit {
    param([string]$Root, [string]$Path, [string]$Before, [string]$After, [string]$What)
    if ($Before -eq $After) { return }
    $script:UpgradeEdits.Add([pscustomobject]@{ Path = $Path; Before = $Before; After = $After; What = $What; Relative = (Get-MuxRelative $Root $Path) })
}
function Read-MuxText { param([string]$Path) return [System.IO.File]::ReadAllText($Path) }
function Write-MuxUpgradePlan {
    param([string]$Target)
    if ($script:UpgradeEdits.Count -eq 0) { Write-Output ('Nothing to change: everything already targets ' + $Target + '.') }
    else {
        $files = @($script:UpgradeEdits | Select-Object -ExpandProperty Relative -Unique)
        Write-Output ('Changes to target ' + $Target + ' (' + $files.Count + ' file' + $(if ($files.Count -ne 1) { 's' }) + '):')
        foreach ($edit in $script:UpgradeEdits) { Write-Output ('  ' + $edit.Relative + ': ' + $edit.What) }
    }
    foreach ($note in $script:UpgradeNotes) { Write-Output ('note: ' + $note) }
}
function Invoke-MuxUpgradeApply {
    $byFile = $script:UpgradeEdits | Group-Object -Property Path
    foreach ($group in $byFile) {
        $text = Read-MuxText $group.Name
        foreach ($edit in $group.Group) {
            $index = $text.IndexOf($edit.Before, [System.StringComparison]::Ordinal)
            if ($index -lt 0) { Exit-MuxNotApplicable ('could not find the text to change in ' + $edit.Relative + '; re-run plan.') }
            $text = $text.Substring(0, $index) + $edit.After + $text.Substring($index + $edit.Before.Length)
        }
        if (Test-MuxDryRun) { Write-Output ('DRYRUN: would write ' + $group.Group[0].Relative) }
        else {
            $encoding = New-Object System.Text.UTF8Encoding($false)
            $bytes = [System.IO.File]::ReadAllBytes($group.Name)
            if ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) { $encoding = New-Object System.Text.UTF8Encoding($true) }
            [System.IO.File]::WriteAllText($group.Name, $text, $encoding)
            Write-Output ('Updated ' + $group.Group[0].Relative)
        }
    }
}
";

        private const string DotnetCode = @"$target = (Get-MuxArg -Arguments $args -Index 0).ToLowerInvariant()
$noBuild = @($args) -contains '--no-build'
if ($target -notmatch '^net(\d+)\.(\d+)$') { Exit-MuxNotApplicable ('pass the target framework, for example net10.0 (got ''' + $target + ''').') }
$major = [int]$Matches[1]
$root = Get-MuxRepoRoot
Set-Location -LiteralPath $root
$projects = Get-MuxUpgradeFiles $root @('*.csproj', '*.fsproj', '*.vbproj', 'Directory.Build.props')
if ($projects.Count -eq 0) { Exit-MuxNotApplicable 'no .NET project files found under the repository root.' }
foreach ($file in $projects) {
    $text = Read-MuxText $file
    foreach ($m in [regex]::Matches($text, '<TargetFramework>\s*([^<]+?)\s*</TargetFramework>')) {
        $current = $m.Groups[1].Value
        if ($current -match '^net\d+\.\d+$' -and $current -ne $target) { Add-MuxUpgradeEdit $root $file $m.Value ('<TargetFramework>' + $target + '</TargetFramework>') ('TargetFramework ' + $current + ' -> ' + $target) }
        elseif ($current -notmatch '^net\d+\.\d+$' -and $current -ne $target) { $script:UpgradeNotes.Add((Get-MuxRelative $root $file) + ' targets ' + $current + ', which is not a modern .NET target; left as is.') }
    }
    foreach ($m in [regex]::Matches($text, '<TargetFrameworks>\s*([^<]+?)\s*</TargetFrameworks>')) {
        $list = @($m.Groups[1].Value.Split(';') | ForEach-Object { $_.Trim() } | Where-Object { $_ })
        if ($list -notcontains $target) { Add-MuxUpgradeEdit $root $file $m.Value ('<TargetFrameworks>' + (($list + $target) -join ';') + '</TargetFrameworks>') ('TargetFrameworks ' + ($list -join ';') + ' -> adds ' + $target) }
    }
    foreach ($m in [regex]::Matches($text, '<PackageReference\s+Include=""(Microsoft\.(AspNetCore|EntityFrameworkCore|Extensions)[^""]*)""\s+Version=""(\d+)\.')) {
        if ([int]$m.Groups[3].Value -lt $major) { $script:UpgradeNotes.Add((Get-MuxRelative $root $file) + ' references ' + $m.Groups[1].Value + ' ' + $m.Groups[3].Value + '.x; move it to ' + $major + '.x after the upgrade (dotnet-outdated lists them).') }
    }
}
$globalJson = Join-Path $root 'global.json'
if (Test-Path -LiteralPath $globalJson) {
    $text = Read-MuxText $globalJson
    $m = [regex]::Match($text, '""version""\s*:\s*""(\d+)\.[^""]*""')
    if ($m.Success -and [int]$m.Groups[1].Value -lt $major) {
        $sdk = $null
        if (Test-MuxTool 'dotnet') { $sdk = @(& dotnet --list-sdks 2>$null | ForEach-Object { ($_ -split ' ')[0] } | Where-Object { $_ -like ([string]$major + '.*') } | Sort-Object { [version]($_ -replace '-.*$', '') } -Descending) | Select-Object -First 1 }
        if ($sdk) { Add-MuxUpgradeEdit $root $globalJson $m.Value ('""version"": ""' + $sdk + '""') ('SDK ' + ($m.Value -replace '.*""(\S+)""$', '$1') + ' -> ' + $sdk) }
        else { $script:UpgradeNotes.Add('global.json pins an older SDK and no ' + $major + '.x SDK is installed; install one, then re-run.') }
    }
}
Write-MuxUpgradePlan $target
if ($mode -eq 'plan') { Write-Output 'Nothing was changed. Run apply to make these edits and build.'; exit 0 }
if ($script:UpgradeEdits.Count -eq 0) { exit 0 }
Invoke-MuxUpgradeApply
if ($noBuild) { exit 0 }
$solution = Get-ChildItem -LiteralPath $root -Include '*.sln', '*.slnx' -File -Recurse -Depth 1 -ErrorAction SilentlyContinue | Select-Object -First 1
$buildArgs = if ($solution) { @('build', (Get-MuxRelative $root $solution.FullName), '--nologo') } else { @('build', '--nologo') }
Invoke-MuxTool -Tool 'dotnet' -Arguments $buildArgs -InstallHint 'Install the .NET SDK.' -AllowFailure
if ($script:MuxLastExit -ne 0) { Write-Output 'The build failed after the upgrade; the edits are in place for you to fix or revert with git.'; exit 1 }
exit 0
";

        private const string NodeCode = @"$target = (Get-MuxArg -Arguments $args -Index 0).TrimStart('v', 'V')
if ($target -notmatch '^\d{2}$') { Exit-MuxNotApplicable ('pass the Node.js major version, for example 22 (got ''' + $target + ''').') }
$root = Get-MuxRepoRoot
Set-Location -LiteralPath $root
foreach ($name in '.nvmrc', '.node-version') {
    $path = Join-Path $root $name
    if (Test-Path -LiteralPath $path) {
        $text = Read-MuxText $path
        $value = $text.Trim()
        if ($value -notmatch ('^v?' + $target + '(\.|$)')) { Add-MuxUpgradeEdit $root $path $value $target ($value + ' -> ' + $target) }
    }
}
$package = Join-Path $root 'package.json'
if (Test-Path -LiteralPath $package) {
    $text = Read-MuxText $package
    $engines = [regex]::Match($text, '""engines""\s*:\s*\{[^}]*\}')
    if ($engines.Success) {
        $node = [regex]::Match($engines.Value, '""node""\s*:\s*""([^""]*)""')
        if ($node.Success -and $node.Groups[1].Value -ne ('>=' + $target)) { Add-MuxUpgradeEdit $root $package $node.Value ('""node"": "">=' + $target + '""') ('engines.node ' + $node.Groups[1].Value + ' -> >=' + $target) }
    }
    $types = [regex]::Match($text, '""@types/node""\s*:\s*""[\^~]?(\d+)')
    if ($types.Success -and $types.Groups[1].Value -ne $target) { $script:UpgradeNotes.Add('@types/node is ' + $types.Groups[1].Value + '.x; update it with your package manager (for example npm install -D @types/node@' + $target + ').') }
}
foreach ($file in (Get-MuxUpgradeFiles (Join-Path $root '.github') @('*.yml', '*.yaml'))) {
    $text = Read-MuxText $file
    foreach ($m in [regex]::Matches($text, '(node-version:\s*)([''""]?)(\d{2})((\.[x\d]+)*)\2')) {
        if ($m.Groups[3].Value -ne $target) { Add-MuxUpgradeEdit $root $file $m.Value ($m.Groups[1].Value + $m.Groups[2].Value + $target + $m.Groups[4].Value + $m.Groups[2].Value) ('node-version ' + $m.Groups[3].Value + $m.Groups[4].Value + ' -> ' + $target + $m.Groups[4].Value) }
    }
    if ($text -match 'node-version:\s*\[') { $script:UpgradeNotes.Add((Get-MuxRelative $root $file) + ' has a node-version matrix; edit the list by hand.') }
}
foreach ($file in (Get-MuxUpgradeFiles $root @('Dockerfile', '*.Dockerfile', 'Dockerfile.*'))) {
    $text = Read-MuxText $file
    foreach ($m in [regex]::Matches($text, '(?m)^(FROM\s+(--platform=\S+\s+)?node:)(\d{2})([^\s]*)')) {
        if ($m.Groups[3].Value -ne $target) { Add-MuxUpgradeEdit $root $file $m.Value ($m.Groups[1].Value + $target + $m.Groups[4].Value) ('FROM node:' + $m.Groups[3].Value + $m.Groups[4].Value + ' -> node:' + $target + $m.Groups[4].Value) }
    }
}
if ($script:UpgradeEdits.Count -eq 0 -and $script:UpgradeNotes.Count -eq 0 -and -not (Test-Path -LiteralPath $package)) { Exit-MuxNotApplicable 'no Node.js version pins found (.nvmrc, .node-version, package.json engines, workflows, or Dockerfiles).' }
Write-MuxUpgradePlan ('Node.js ' + $target)
if ($mode -eq 'plan') { Write-Output 'Nothing was changed. Run apply to make these edits.'; exit 0 }
Invoke-MuxUpgradeApply
exit 0
";

        private const string PythonCode = @"$target = Get-MuxArg -Arguments $args -Index 0
if ($target -notmatch '^3\.(\d{1,2})$') { Exit-MuxNotApplicable ('pass the Python version, for example 3.12 (got ''' + $target + ''').') }
$minor = $Matches[1]
$compact = '3' + $minor
$root = Get-MuxRepoRoot
Set-Location -LiteralPath $root
$pyproject = Join-Path $root 'pyproject.toml'
if (Test-Path -LiteralPath $pyproject) {
    $text = Read-MuxText $pyproject
    $m = [regex]::Match($text, 'requires-python\s*=\s*""([^""]*)""')
    if ($m.Success -and $m.Groups[1].Value -ne ('>=' + $target)) { Add-MuxUpgradeEdit $root $pyproject $m.Value ('requires-python = "">=' + $target + '""') ('requires-python ' + $m.Groups[1].Value + ' -> >=' + $target) }
    foreach ($m in [regex]::Matches($text, '(target-version\s*=\s*"")py3(\d{1,2})("")')) {
        if ($m.Groups[2].Value -ne $minor) { Add-MuxUpgradeEdit $root $pyproject $m.Value ($m.Groups[1].Value + 'py' + $compact + $m.Groups[3].Value) ('target-version py3' + $m.Groups[2].Value + ' -> py' + $compact) }
    }
    foreach ($m in [regex]::Matches($text, '(python_version\s*=\s*"")3\.(\d{1,2})("")')) {
        if ($m.Groups[2].Value -ne $minor) { Add-MuxUpgradeEdit $root $pyproject $m.Value ($m.Groups[1].Value + $target + $m.Groups[3].Value) ('mypy python_version 3.' + $m.Groups[2].Value + ' -> ' + $target) }
    }
    if ($text -match 'Programming Language :: Python :: 3\.\d') { $script:UpgradeNotes.Add('pyproject.toml lists Python version classifiers; add ""Programming Language :: Python :: ' + $target + '"" and drop the ones you no longer support.') }
}
foreach ($name in '.python-version', 'runtime.txt') {
    $path = Join-Path $root $name
    if (Test-Path -LiteralPath $path) {
        $value = (Read-MuxText $path).Trim()
        $new = if ($name -eq 'runtime.txt') { 'python-' + $target } else { $target }
        if ($value -notlike ($new + '*')) { Add-MuxUpgradeEdit $root $path $value $new ($value + ' -> ' + $new) }
    }
}
foreach ($file in (Get-MuxUpgradeFiles (Join-Path $root '.github') @('*.yml', '*.yaml'))) {
    $text = Read-MuxText $file
    foreach ($m in [regex]::Matches($text, '(python-version:\s*)([''""]?)3\.(\d{1,2})\2(?=\s*$)', [System.Text.RegularExpressions.RegexOptions]::Multiline)) {
        if ($m.Groups[3].Value -ne $minor) { Add-MuxUpgradeEdit $root $file $m.Value ($m.Groups[1].Value + $m.Groups[2].Value + $target + $m.Groups[2].Value) ('python-version 3.' + $m.Groups[3].Value + ' -> ' + $target) }
    }
    if ($text -match 'python-version:\s*\[') { $script:UpgradeNotes.Add((Get-MuxRelative $root $file) + ' has a python-version matrix; edit the list by hand.') }
}
foreach ($file in (Get-MuxUpgradeFiles $root @('Dockerfile', '*.Dockerfile', 'Dockerfile.*'))) {
    $text = Read-MuxText $file
    foreach ($m in [regex]::Matches($text, '(?m)^(FROM\s+(--platform=\S+\s+)?python:)3\.(\d{1,2})([^\s]*)')) {
        if ($m.Groups[3].Value -ne $minor) { Add-MuxUpgradeEdit $root $file $m.Value ($m.Groups[1].Value + $target + $m.Groups[4].Value) ('FROM python:3.' + $m.Groups[3].Value + $m.Groups[4].Value + ' -> python:' + $target + $m.Groups[4].Value) }
    }
}
if ($script:UpgradeEdits.Count -eq 0 -and $script:UpgradeNotes.Count -eq 0 -and -not (Test-Path -LiteralPath $pyproject)) { Exit-MuxNotApplicable 'no Python version pins found (pyproject.toml, .python-version, runtime.txt, workflows, or Dockerfiles).' }
Write-MuxUpgradePlan ('Python ' + $target)
if ($mode -eq 'plan') { Write-Output 'Nothing was changed. Run apply to make these edits.'; exit 0 }
Invoke-MuxUpgradeApply
if ((@($args) -contains '--pyupgrade')) {
    $sources = @(Get-MuxUpgradeFiles $root @('*.py') | ForEach-Object { Get-MuxRelative $root $_ })
    if ($sources.Count -gt 0) { Invoke-MuxTool -Tool 'pyupgrade' -Arguments (@('--py' + $compact + '-plus') + $sources) -InstallHint 'Install pyupgrade: pip install pyupgrade.' -AllowFailure }
}
exit 0
";

        private const string UpgradeExitNote = " Exit codes: 0 success, 1 the build failed after apply, 2 no project or a bad version.";

        #endregion

        #region Public-Methods

        /// <summary>Returns the upgrade skill definitions.</summary>
        /// <returns>The definitions.</returns>
        public static IReadOnlyList<DefaultSkillDef> All()
        {
            ToolchainSkillFactory dotnet = new ToolchainSkillFactory(Setup, new[] { "dotnet", "upgrade" }, new[] { "*.sln", "*.slnx", "**/*.csproj", "**/*.fsproj", "global.json" }, null, UpgradeExitNote);
            ToolchainSkillFactory node = new ToolchainSkillFactory(Setup, new[] { "javascript", "upgrade" }, new[] { "package.json", ".nvmrc", ".node-version" }, null, UpgradeExitNote);
            ToolchainSkillFactory python = new ToolchainSkillFactory(Setup, new[] { "python", "upgrade" }, new[] { "pyproject.toml", ".python-version", "runtime.txt", "requirements*.txt", "setup.py" }, null, UpgradeExitNote);
            return new List<DefaultSkillDef>
            {
                dotnet.Skill("dotnet-upgrade", "Upgrade the .NET target framework",
                    "Upgrades .NET projects to a new target framework (for example net8.0 to net10.0): plans every TargetFramework and global.json edit, applies them, and builds.",
                    true,
                    "The user asks to upgrade, migrate, or move the project to a newer .NET version or target framework.",
                    "<tfm, for example net10.0> [--no-build]",
                    "`plan <tfm>` lists every edit without changing anything: a single TargetFramework is replaced, a TargetFrameworks list gets the new framework added, and global.json moves to the newest installed SDK of that major version. Microsoft.AspNetCore, EntityFrameworkCore, and Extensions packages still on an older major are listed as notes, not changed. `apply <tfm>` makes exactly those edits (bin, obj, and dependency folders are skipped), then runs dotnet build; `--no-build` skips the build. Show the plan to the user before apply.",
                    ToolchainSkillFactory.Command("plan", "List the edits for a target framework without changing anything.", "$mode = 'plan'\n" + DotnetCode),
                    ToolchainSkillFactory.Command("apply", "Make the edits and build.", "$mode = 'apply'\n" + DotnetCode)),

                node.Skill("node-upgrade", "Upgrade the Node.js version",
                    "Upgrades the Node.js version a project pins (.nvmrc, .node-version, package.json engines, CI workflows, Dockerfiles) to a new major version.",
                    true,
                    "The user asks to upgrade or move the project to a newer Node.js version.",
                    "<major, for example 22>",
                    "`plan <major>` lists every edit without changing anything: .nvmrc and .node-version, `engines.node` in package.json (set to `>=major`), `node-version` entries in GitHub workflows (keeping a `.x` suffix), and `FROM node:<version>` in Dockerfiles (keeping the tag suffix such as -alpine). A version matrix and @types/node are reported as notes for you to change. `apply <major>` makes exactly those edits; run js-install and the tests afterwards. Show the plan to the user before apply.",
                    ToolchainSkillFactory.Command("plan", "List the edits for a Node.js major version without changing anything.", "$mode = 'plan'\n" + NodeCode),
                    ToolchainSkillFactory.Command("apply", "Make the edits.", "$mode = 'apply'\n" + NodeCode)),

                python.Skill("py-upgrade", "Upgrade the Python version",
                    "Upgrades the Python version a project targets (requires-python, ruff and mypy targets, .python-version, CI workflows, Dockerfiles) to a new version such as 3.12.",
                    true,
                    "The user asks to upgrade or move the project to a newer Python version.",
                    "<version, for example 3.12> [--pyupgrade]",
                    "`plan <version>` lists every edit without changing anything: `requires-python` (set to `>=version`), ruff and black `target-version`, mypy `python_version`, .python-version, runtime.txt, single `python-version` entries in GitHub workflows, and `FROM python:<version>` in Dockerfiles. Classifiers and version matrices are reported as notes. `apply <version>` makes exactly those edits; `--pyupgrade` also rewrites source with pyupgrade for that version when it is installed. Show the plan to the user before apply.",
                    ToolchainSkillFactory.Command("plan", "List the edits for a Python version without changing anything.", "$mode = 'plan'\n" + PythonCode),
                    ToolchainSkillFactory.Command("apply", "Make the edits.", "$mode = 'apply'\n" + PythonCode))
            };
        }

        #endregion
    }
}
