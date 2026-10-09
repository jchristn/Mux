# mux-skill.ps1: shared helpers for mux's default toolchain skills.
#
# Each command block dot-sources this file first:
#   . (Join-Path $env:MUX_SKILL_DIR 'resources/mux-skill.ps1')
#
# Exit codes used by every toolchain skill:
#   0  success
#   1  the tool ran and reported problems (failing tests, lint findings, a failed build)
#   2  the tool is not installed, the CLI is not signed in, or the project does not use this toolchain
#   3  refused by the production guard: the target looks like production and was not confirmed
#
# Set MUX_SKILL_DRY_RUN=1 to print each command as "DRYRUN: <command>" instead of running it. Detection still
# runs, so dry runs show exactly what a skill would execute in a given project without needing the tool.
#
# This file is seeded by mux and may be edited; mux does not overwrite a skill folder once it exists.

$ErrorActionPreference = 'Stop'

function Test-MuxDryRun {
    return $env:MUX_SKILL_DRY_RUN -eq '1'
}

function Test-MuxTool {
    param([Parameter(Mandatory = $true)][string]$Name)
    return [bool](Get-Command $Name -CommandType Application, ExternalScript -ErrorAction SilentlyContinue)
}

function Exit-MuxNotApplicable {
    param([Parameter(Mandatory = $true)][string]$Message)
    # Write-Host, not Write-Output: a helper called inside @(...) or an assignment would otherwise swallow the message.
    Write-Host "mux: $Message"
    exit 2
}

function Format-MuxCommand {
    param([string]$Tool, [string[]]$Arguments)
    $parts = @($Tool) + @($Arguments | Where-Object { $null -ne $_ })
    return ($parts | ForEach-Object {
        if ($_ -match '[\s"]') { '"' + ($_ -replace '"', '\"') + '"' } else { $_ }
    }) -join ' '
}

# Runs a tool with arguments, echoing the command first. In a dry run only the command is printed. A missing
# tool exits 2 with the install hint; a nonzero exit from the tool ends the skill with that code unless
# -AllowFailure is passed, in which case the code is left in $script:MuxLastExit.
function Invoke-MuxTool {
    param(
        [Parameter(Mandatory = $true)][string]$Tool,
        [string[]]$Arguments = @(),
        [string]$InstallHint = '',
        [switch]$AllowFailure
    )

    $line = Format-MuxCommand -Tool $Tool -Arguments $Arguments
    $script:MuxLastExit = 0
    if (Test-MuxDryRun) {
        Write-Output "DRYRUN: $line"
        return
    }

    if (-not (Test-MuxTool $Tool)) {
        Exit-MuxNotApplicable -Message ("'$Tool' was not found on PATH. $InstallHint".Trim())
    }

    Write-Output "> $line"
    & $Tool @Arguments
    $script:MuxLastExit = $LASTEXITCODE
    if ($script:MuxLastExit -ne 0 -and -not $AllowFailure) {
        exit $script:MuxLastExit
    }
}

# Returns the repository root (nearest ancestor with .git), or the current directory outside a repository.
function Get-MuxRepoRoot {
    $dir = (Get-Location).Path
    while ($dir) {
        if (Test-Path -LiteralPath (Join-Path $dir '.git')) { return $dir }
        $parent = Split-Path -Parent $dir
        if (-not $parent -or $parent -eq $dir) { break }
        $dir = $parent
    }

    return (Get-Location).Path
}

# Returns the nearest directory, from the current one up to the repository root, that contains any of the given
# file names or wildcard patterns. Returns $null when none is found.
function Find-MuxUp {
    param([Parameter(Mandatory = $true)][string[]]$Names)
    $root = Get-MuxRepoRoot
    $dir = (Get-Location).Path
    while ($dir) {
        foreach ($name in $Names) {
            if (Get-ChildItem -LiteralPath $dir -Filter $name -Force -ErrorAction SilentlyContinue | Select-Object -First 1) {
                return $dir
            }
        }

        if ($dir -eq $root) { break }
        $parent = Split-Path -Parent $dir
        if (-not $parent -or $parent -eq $dir) { break }
        $dir = $parent
    }

    return $null
}

# Returns the first file found (from the current directory up to the repo root) for any of the names, or $null.
function Find-MuxFileUp {
    param([Parameter(Mandatory = $true)][string[]]$Names)
    $dir = Find-MuxUp -Names $Names
    if (-not $dir) { return $null }
    foreach ($name in $Names) {
        $hit = Get-ChildItem -LiteralPath $dir -Filter $name -Force -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($hit) { return $hit.FullName }
    }

    return $null
}

# ---------------------------------------------------------------------------------------------------------------
# JavaScript and TypeScript
# ---------------------------------------------------------------------------------------------------------------

function Get-MuxNodeProject {
    $dir = Find-MuxUp -Names @('package.json')
    if (-not $dir) { Exit-MuxNotApplicable 'no package.json in this directory or its parents; this is not a JavaScript project.' }
    return $dir
}

function Get-MuxPackageJson {
    param([Parameter(Mandatory = $true)][string]$Dir)
    try {
        return Get-Content -LiteralPath (Join-Path $Dir 'package.json') -Raw | ConvertFrom-Json -AsHashtable
    } catch {
        Exit-MuxNotApplicable "package.json in $Dir could not be parsed: $($_.Exception.Message)"
    }
}

# The package manager: the packageManager field wins, then the lockfile in the project or repository root
# (bun, pnpm, yarn, npm in that order), then npm.
function Get-MuxNodePackageManager {
    param([Parameter(Mandatory = $true)][string]$Dir)
    $pkg = Get-MuxPackageJson -Dir $Dir
    if ($pkg.ContainsKey('packageManager') -and $pkg['packageManager'] -match '^(npm|pnpm|yarn|bun)@') {
        return $Matches[1]
    }

    $locks = @(
        @('bun.lockb', 'bun'), @('bun.lock', 'bun'),
        @('pnpm-lock.yaml', 'pnpm'), @('yarn.lock', 'yarn'),
        @('package-lock.json', 'npm'), @('npm-shrinkwrap.json', 'npm')
    )
    foreach ($candidate in @($Dir, (Get-MuxRepoRoot)) | Select-Object -Unique) {
        foreach ($lock in $locks) {
            if (Test-Path -LiteralPath (Join-Path $candidate $lock[0])) { return $lock[1] }
        }
    }

    return 'npm'
}

function Test-MuxPackageScript {
    param($Package, [string]$Name)
    return $Package.ContainsKey('scripts') -and $null -ne $Package['scripts'] -and $Package['scripts'].ContainsKey($Name)
}

function Test-MuxPackageDependency {
    param($Package, [string]$Name)
    foreach ($section in @('dependencies', 'devDependencies', 'peerDependencies', 'optionalDependencies')) {
        if ($Package.ContainsKey($section) -and $null -ne $Package[$section] -and $Package[$section].ContainsKey($Name)) {
            return $true
        }
    }

    return $false
}

# Runs a package.json script through the detected package manager; extra arguments reach the script.
function Invoke-MuxPackageScript {
    param([string]$Manager, [string]$Script, [string[]]$Extra = @(), [switch]$AllowFailure)
    $arguments = @('run', $Script)
    if ($Extra.Count -gt 0) {
        if ($Manager -eq 'npm') { $arguments += '--' }
        $arguments += $Extra
    }

    Invoke-MuxTool -Tool $Manager -Arguments $arguments -InstallHint (Get-MuxNodeInstallHint $Manager) -AllowFailure:$AllowFailure
}

# Runs a binary installed in the project (node_modules/.bin) through the detected package manager.
function Invoke-MuxPackageBin {
    param([string]$Manager, [string]$Bin, [string[]]$Arguments = @(), [switch]$AllowFailure)
    switch ($Manager) {
        'pnpm' { $tool = 'pnpm'; $all = @('exec', $Bin) + $Arguments }
        'yarn' { $tool = 'yarn'; $all = @('run', $Bin) + $Arguments }
        'bun' { $tool = 'bunx'; $all = @($Bin) + $Arguments }
        default { $tool = 'npx'; $all = @('--no-install', $Bin) + $Arguments }
    }

    Invoke-MuxTool -Tool $tool -Arguments $all -InstallHint (Get-MuxNodeInstallHint $Manager) -AllowFailure:$AllowFailure
}

# Returns @{ Tool; Arguments } for running a project binary through the package manager, without running it.
function Get-MuxPackageBinCommand {
    param([string]$Manager, [string]$Bin, [string[]]$Arguments = @())
    switch ($Manager) {
        'pnpm' { return @{ Tool = 'pnpm'; Arguments = @('exec', $Bin) + $Arguments } }
        'yarn' { return @{ Tool = 'yarn'; Arguments = @('run', $Bin) + $Arguments } }
        'bun' { return @{ Tool = 'bunx'; Arguments = @($Bin) + $Arguments } }
        default { return @{ Tool = 'npx'; Arguments = @('--no-install', $Bin) + $Arguments } }
    }
}

function Get-MuxNodeInstallHint {
    param([string]$Manager)
    switch ($Manager) {
        'pnpm' { return 'Install pnpm: corepack enable pnpm (or npm install -g pnpm).' }
        'yarn' { return 'Install yarn: corepack enable yarn (or npm install -g yarn).' }
        'bun' { return 'Install bun from https://bun.sh.' }
        default { return 'Install Node.js (which includes npm) from https://nodejs.org.' }
    }
}

# Picks the JavaScript test runner from the project's dependencies: Vitest, Jest, Mocha, then node --test.
function Get-MuxJsTestRunner {
    param($Package)
    foreach ($runner in @('vitest', 'jest', 'mocha')) { if (Test-MuxPackageDependency $Package $runner) { return $runner } }
    return 'node'
}

# Runs the detected JavaScript test runner in non-watch mode. Mode is all, filter, or coverage.
function Invoke-MuxJsTestRunner {
    param([string]$Manager, $Package, [string]$Mode, [string]$Filter = '')
    $env:CI = '1'
    switch (Get-MuxJsTestRunner $Package) {
        'vitest' {
            $a = @('run')
            if ($Mode -eq 'filter') { $a += $Filter }
            if ($Mode -eq 'coverage') { $a += '--coverage' }
            Invoke-MuxPackageBin -Manager $Manager -Bin 'vitest' -Arguments $a
        }
        'jest' {
            $a = @('--ci')
            if ($Mode -eq 'filter') { $a += $Filter }
            if ($Mode -eq 'coverage') { $a += '--coverage' }
            Invoke-MuxPackageBin -Manager $Manager -Bin 'jest' -Arguments $a
        }
        'mocha' {
            $a = @()
            if ($Mode -eq 'filter') { $a += @('--grep', $Filter) }
            if ($Mode -eq 'coverage') { Invoke-MuxPackageBin -Manager $Manager -Bin 'c8' -Arguments (@('mocha') + $a) }
            else { Invoke-MuxPackageBin -Manager $Manager -Bin 'mocha' -Arguments $a }
        }
        default {
            $a = @('--test')
            if ($Mode -eq 'filter') { $a += ('--test-name-pattern=' + $Filter) }
            if ($Mode -eq 'coverage') { $a += '--experimental-test-coverage' }
            Invoke-MuxTool -Tool 'node' -Arguments $a -InstallHint 'Install Node.js from https://nodejs.org.'
        }
    }
}

# Exits 2 unless the project depends on React.
function Assert-MuxReact {
    param($Package)
    if (-not (Test-MuxPackageDependency $Package 'react')) { Exit-MuxNotApplicable 'package.json does not depend on react; this is not a React project.' }
}

# Writes a new file, refusing to overwrite. In a dry run only prints what would be written.
function New-MuxFile {
    param([Parameter(Mandatory = $true)][string]$Path, [Parameter(Mandatory = $true)][string]$Content)
    if (Test-Path -LiteralPath $Path) { Write-Output "mux: $Path already exists; not overwriting."; exit 1 }
    if (Test-MuxDryRun) { Write-Output "DRYRUN: create $Path"; return }
    $parent = Split-Path -Parent $Path
    if ($parent -and -not (Test-Path -LiteralPath $parent)) { New-Item -ItemType Directory -Path $parent -Force | Out-Null }
    Set-Content -LiteralPath $Path -Value $Content -NoNewline
    Write-Output "Created $Path"
}

# ---------------------------------------------------------------------------------------------------------------
# Python
# ---------------------------------------------------------------------------------------------------------------

function Get-MuxPythonProject {
    $dir = Find-MuxUp -Names @('pyproject.toml', 'requirements*.txt', 'setup.py', 'setup.cfg', 'Pipfile', 'uv.lock', 'poetry.lock')
    if (-not $dir) { Exit-MuxNotApplicable 'no pyproject.toml, requirements*.txt, setup.py, setup.cfg, or Pipfile found; this is not a Python project.' }
    return $dir
}

# The environment manager: uv, poetry, pipenv, or pip (a project .venv).
function Get-MuxPythonManager {
    param([Parameter(Mandatory = $true)][string]$Dir)
    if (Test-Path -LiteralPath (Join-Path $Dir 'uv.lock')) { return 'uv' }
    if (Test-Path -LiteralPath (Join-Path $Dir 'poetry.lock')) { return 'poetry' }
    if ((Test-Path -LiteralPath (Join-Path $Dir 'Pipfile.lock')) -or (Test-Path -LiteralPath (Join-Path $Dir 'Pipfile'))) { return 'pipenv' }
    $pyproject = Join-Path $Dir 'pyproject.toml'
    if (Test-Path -LiteralPath $pyproject) {
        $text = Get-Content -LiteralPath $pyproject -Raw
        if ($text -match '(?m)^\[tool\.poetry\]') { return 'poetry' }
        if ($text -match '(?m)^\[tool\.uv\]') { return 'uv' }
    }

    return 'pip'
}

# The project virtual environment's interpreter, or $null when there is none.
function Get-MuxVenvPython {
    param([Parameter(Mandatory = $true)][string]$Dir)
    foreach ($candidate in @('.venv/bin/python', '.venv/Scripts/python.exe', 'venv/bin/python', 'venv/Scripts/python.exe')) {
        $path = Join-Path $Dir $candidate
        if (Test-Path -LiteralPath $path) { return $path }
    }

    return $null
}

function Get-MuxSystemPython {
    foreach ($name in @('python3', 'python', 'py')) {
        if (Test-MuxTool $name) { return $name }
    }

    return 'python3'
}

# Runs "python <arguments>" inside the project's environment: uv run, poetry run, pipenv run, or the .venv
# interpreter (falling back to the system interpreter when no venv exists).
function Invoke-MuxPython {
    param([string]$Dir, [string]$Manager, [string[]]$Arguments = @(), [switch]$AllowFailure)
    switch ($Manager) {
        'uv' { Invoke-MuxTool -Tool 'uv' -Arguments (@('run', 'python') + $Arguments) -InstallHint 'Install uv from https://docs.astral.sh/uv/.' -AllowFailure:$AllowFailure }
        'poetry' { Invoke-MuxTool -Tool 'poetry' -Arguments (@('run', 'python') + $Arguments) -InstallHint 'Install Poetry from https://python-poetry.org.' -AllowFailure:$AllowFailure }
        'pipenv' { Invoke-MuxTool -Tool 'pipenv' -Arguments (@('run', 'python') + $Arguments) -InstallHint 'Install pipenv: pip install --user pipenv.' -AllowFailure:$AllowFailure }
        default {
            $venv = Get-MuxVenvPython -Dir $Dir
            $python = if ($venv) { $venv } else { Get-MuxSystemPython }
            Invoke-MuxTool -Tool $python -Arguments $Arguments -InstallHint 'Install Python 3 from https://www.python.org.' -AllowFailure:$AllowFailure
        }
    }
}

# Whether a Python module is importable in the project's environment. Dry runs assume it is.
function Test-MuxPythonModule {
    param([string]$Dir, [string]$Manager, [string]$Module)
    if (Test-MuxDryRun) { return $true }
    $probe = @('-c', "import importlib.util,sys; sys.exit(0 if importlib.util.find_spec('$Module') else 1)")
    try {
        switch ($Manager) {
            'uv' { & uv run python @probe 2>$null | Out-Null }
            'poetry' { & poetry run python @probe 2>$null | Out-Null }
            'pipenv' { & pipenv run python @probe 2>$null | Out-Null }
            default {
                $venv = Get-MuxVenvPython -Dir $Dir
                $python = if ($venv) { $venv } else { Get-MuxSystemPython }
                & $python @probe 2>$null | Out-Null
            }
        }

        return $LASTEXITCODE -eq 0
    } catch {
        return $false
    }
}

# ---------------------------------------------------------------------------------------------------------------
# Java (Maven and Gradle)
# ---------------------------------------------------------------------------------------------------------------

function Get-MuxJavaProject {
    $dir = Find-MuxUp -Names @('mvnw', 'gradlew', 'pom.xml', 'build.gradle', 'build.gradle.kts', 'settings.gradle', 'settings.gradle.kts')
    if (-not $dir) { Exit-MuxNotApplicable 'no pom.xml, build.gradle, or wrapper found; this is not a Maven or Gradle project.' }
    return $dir
}

# Returns @{ Kind = 'maven'|'gradle'; Tool = <wrapper path or mvn/gradle>; BuildFile = <path or empty> }. Wrappers
# win because they pin the build tool version the project was tested with.
function Get-MuxJavaBuild {
    param([Parameter(Mandatory = $true)][string]$Dir)
    $onWindows = $IsWindows -or $env:OS -eq 'Windows_NT'
    $mvnw = Join-Path $Dir $(if ($onWindows) { 'mvnw.cmd' } else { 'mvnw' })
    $gradlew = Join-Path $Dir $(if ($onWindows) { 'gradlew.bat' } else { 'gradlew' })
    $pom = Join-Path $Dir 'pom.xml'
    $gradleFile = @('build.gradle.kts', 'build.gradle') | ForEach-Object { Join-Path $Dir $_ } | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    if (Test-Path -LiteralPath $mvnw) { return @{ Kind = 'maven'; Tool = $mvnw; BuildFile = $pom } }
    if (Test-Path -LiteralPath $gradlew) { return @{ Kind = 'gradle'; Tool = $gradlew; BuildFile = $gradleFile } }
    if (Test-Path -LiteralPath $pom) { return @{ Kind = 'maven'; Tool = 'mvn'; BuildFile = $pom } }
    if ($gradleFile) { return @{ Kind = 'gradle'; Tool = 'gradle'; BuildFile = $gradleFile } }
    Exit-MuxNotApplicable 'no pom.xml or build.gradle found.'
}

# Whether the Maven or Gradle build file mentions a plugin or dependency (a plain text search).
function Test-MuxBuildFileMentions {
    param($Build, [string]$Text)
    if (-not $Build.BuildFile -or -not (Test-Path -LiteralPath $Build.BuildFile)) { return $false }
    return (Get-Content -LiteralPath $Build.BuildFile -Raw) -match [regex]::Escape($Text)
}

function Invoke-MuxJavaBuild {
    param($Build, [string[]]$MavenArguments = @(), [string[]]$GradleArguments = @(), [switch]$AllowFailure)
    $hint = if ($Build.Kind -eq 'maven') { 'Install Maven from https://maven.apache.org or add the Maven wrapper (mvn wrapper:wrapper).' } else { 'Install Gradle from https://gradle.org or add the Gradle wrapper (gradle wrapper).' }
    if ($Build.Kind -eq 'maven') { Invoke-MuxTool -Tool $Build.Tool -Arguments (@('-B') + $MavenArguments) -InstallHint $hint -AllowFailure:$AllowFailure }
    else { Invoke-MuxTool -Tool $Build.Tool -Arguments (@('--console=plain') + $GradleArguments) -InstallHint $hint -AllowFailure:$AllowFailure }
}

# ---------------------------------------------------------------------------------------------------------------
# C and C++ (CMake, Meson, Make)
# ---------------------------------------------------------------------------------------------------------------

function Get-MuxCppProject {
    $dir = Find-MuxUp -Names @('CMakeLists.txt', 'CMakePresets.json', 'meson.build', 'Makefile')
    if (-not $dir) { Exit-MuxNotApplicable 'no CMakeLists.txt, meson.build, or Makefile found; this is not a C or C++ project.' }
    return $dir
}

# Returns cmake, meson, or make for the project directory.
function Get-MuxCppBuildSystem {
    param([Parameter(Mandatory = $true)][string]$Dir)
    if (Test-Path -LiteralPath (Join-Path $Dir 'CMakeLists.txt')) { return 'cmake' }
    if (Test-Path -LiteralPath (Join-Path $Dir 'meson.build')) { return 'meson' }
    return 'make'
}

function Get-MuxProcessorCount {
    return [Math]::Max(1, [Environment]::ProcessorCount)
}

# Tracked (or, outside git, discovered) C and C++ source and header files, relative to the current directory.
function Get-MuxCppSources {
    param([switch]$ChangedOnly)
    $pattern = '\.(c|cc|cpp|cxx|h|hh|hpp|hxx)$'
    if (Test-MuxTool 'git') {
        if ($ChangedOnly) { $files = @(& git diff --name-only HEAD 2>$null) + @(& git ls-files --others --exclude-standard 2>$null) }
        else { $files = @(& git ls-files 2>$null) }
        if ($LASTEXITCODE -eq 0 -and $files.Count -gt 0) { return @($files | Where-Object { $_ -match $pattern -and (Test-Path -LiteralPath $_) } | Select-Object -Unique) }
    }

    return @(Get-ChildItem -Recurse -File -ErrorAction SilentlyContinue | Where-Object { $_.FullName -notmatch '[\\/](build|out|third_party|vendor|external)[\\/]' -and $_.Name -match $pattern } |
        ForEach-Object { [IO.Path]::GetRelativePath((Get-Location).Path, $_.FullName) })
}

# ---------------------------------------------------------------------------------------------------------------
# Go and Rust
# ---------------------------------------------------------------------------------------------------------------

function Get-MuxGoProject {
    $dir = Find-MuxUp -Names @('go.mod')
    if (-not $dir) { Exit-MuxNotApplicable 'no go.mod found; this is not a Go module.' }
    return $dir
}

function Get-MuxRustProject {
    $dir = Find-MuxUp -Names @('Cargo.toml')
    if (-not $dir) { Exit-MuxNotApplicable 'no Cargo.toml found; this is not a Rust crate.' }
    return $dir
}

# ---------------------------------------------------------------------------------------------------------------
# Deployment targets and the production guard (containers, Kubernetes, clouds, infrastructure as code)
# ---------------------------------------------------------------------------------------------------------------

function Get-MuxProdPattern {
    if ($env:MUX_SKILL_PROD_PATTERN) { return $env:MUX_SKILL_PROD_PATTERN }
    return 'prod|production|live'
}

# Separates "--confirm <name>" from the other arguments. Returns @{ Confirm = <name or empty>; Rest = <string[]> }.
function Split-MuxConfirm {
    param([object[]]$Arguments)
    $rest = New-Object System.Collections.Generic.List[string]
    $confirm = ''
    $list = @($Arguments)
    for ($i = 0; $i -lt $list.Count; $i++) {
        if ([string]$list[$i] -eq '--confirm' -and ($i + 1) -lt $list.Count) {
            $confirm = [string]$list[$i + 1]
            $i++
        } else {
            $rest.Add([string]$list[$i])
        }
    }

    return @{ Confirm = $confirm; Rest = $rest.ToArray() }
}

# Refuses (exit 3) to change a target whose name matches the production pattern, unless the arguments repeated the
# exact name with --confirm. The model should only pass --confirm after the user explicitly approved the change.
function Assert-MuxNotProduction {
    param([string]$Target, [string]$Confirm)
    $pattern = Get-MuxProdPattern
    if ($Target -and ($Target -match $pattern) -and ($Confirm -cne $Target)) {
        Write-Output ("mux: refused: '" + $Target + "' looks like production (matches skillProdPattern '" + $pattern + "'). Only if the user explicitly approved this change, re-run with --confirm " + $Target + '.')
        exit 3
    }
}

# Resolves and prints the deployment target (cluster context, cloud profile, project, workspace) so every command
# shows where it acts. Dry runs use MUX_SKILL_DRY_RUN_TARGET (default dry-run-target) and need no credentials. A
# failed lookup usually means the CLI is not signed in: exit 2 with the login hint. Mux never runs a login flow.
function Get-MuxTarget {
    param([string]$Label, [scriptblock]$Resolve, [string]$LoginHint = '')
    if (Test-MuxDryRun) {
        $value = if ($env:MUX_SKILL_DRY_RUN_TARGET) { $env:MUX_SKILL_DRY_RUN_TARGET } else { 'dry-run-target' }
    } else {
        $value = $null
        $global:LASTEXITCODE = 0
        try {
            $value = & $Resolve 2>$null | Select-Object -First 1
            if ($LASTEXITCODE -ne 0) { $value = $null }
        } catch {
            $value = $null
        }

        if (-not $value) { Exit-MuxNotApplicable ('could not read the ' + $Label + '. ' + $LoginHint).Trim() }
        $value = ([string]$value).Trim()
    }

    Write-Host ($Label + ': ' + $value)
    return $value
}

function Get-MuxKubeContext {
    return Get-MuxTarget -Label 'Kubernetes context' -Resolve { kubectl config current-context } -LoginHint 'Configure a cluster with kubectl config use-context <name> (or your cloud CLI kubeconfig command).'
}

# The kubectl arguments that apply a path: -k for a kustomization directory, otherwise -f.
function Get-MuxKubeApplyArguments {
    param([string]$Path)
    if ((Test-Path -LiteralPath $Path -PathType Container) -and ((Test-Path -LiteralPath (Join-Path $Path 'kustomization.yaml')) -or (Test-Path -LiteralPath (Join-Path $Path 'kustomization.yml')))) {
        return @('-k', $Path)
    }

    if (Test-Path -LiteralPath $Path -PathType Container) { return @('-f', $Path, '--recursive') }
    return @('-f', $Path)
}

# The nearest Helm chart directory (with Chart.yaml), or the argument when one was passed.
function Get-MuxHelmChart {
    param([string]$Chart = '')
    if ($Chart) { return $Chart }
    $dir = Find-MuxUp -Names @('Chart.yaml')
    if (-not $dir) { Exit-MuxNotApplicable 'no Chart.yaml found here or in a parent; pass the chart path.' }
    return $dir
}

function Get-MuxComposeFile {
    $file = Find-MuxFileUp -Names @('compose.yaml', 'compose.yml', 'docker-compose.yaml', 'docker-compose.yml')
    if (-not $file) { Exit-MuxNotApplicable 'no compose.yaml or docker-compose.yml found.' }
    return $file
}

# A short, bounded line count from an argument (default 200, at most 5000).
function Get-MuxLineLimit {
    param([string]$Value, [int]$Default = 200)
    $parsed = 0
    if ([int]::TryParse($Value, [ref]$parsed) -and $parsed -gt 0) { return [Math]::Min($parsed, 5000) }
    return $Default
}

# ---------------------------------------------------------------------------------------------------------------
# Git (review and playbook skills)
# ---------------------------------------------------------------------------------------------------------------

# Exits 2 unless git is installed and the current directory is inside a work tree.
function Assert-MuxGitRepo {
    if (-not (Test-MuxTool 'git')) { Exit-MuxNotApplicable 'git was not found on PATH.' }
    $inside = & git rev-parse --is-inside-work-tree 2>$null
    if ($LASTEXITCODE -ne 0 -or "$inside".Trim() -ne 'true') { Exit-MuxNotApplicable 'this is not a git repository.' }
}

function Test-MuxGitRef {
    param([string]$Ref)
    & git rev-parse --verify --quiet ($Ref + '^{commit}') *> $null
    return $LASTEXITCODE -eq 0
}

# The branch to compare against: origin's default branch, then origin/main, origin/master, main, master, trunk,
# or develop, whichever exists first. Returns $null when none does.
function Get-MuxDefaultBranch {
    $ref = & git symbolic-ref --quiet --short refs/remotes/origin/HEAD 2>$null
    if ($LASTEXITCODE -eq 0 -and $ref) { return "$ref".Trim() }
    foreach ($name in @('origin/main', 'origin/master', 'main', 'master', 'trunk', 'develop')) {
        if (Test-MuxGitRef $name) { return $name }
    }

    return $null
}

# Removes the review effort words (quick, deep) from an argument list.
function Remove-MuxEffortWords {
    param([object[]]$Arguments)
    return @($Arguments | Where-Object { [string]$_ -notin @('quick', 'deep') })
}

# The maximum characters of git output printed by the review commands (MUX_SKILL_DIFF_MAX_BYTES, default 200000).
function Get-MuxDiffLimit {
    $parsed = 0
    if ([int]::TryParse("$env:MUX_SKILL_DIFF_MAX_BYTES", [ref]$parsed) -and $parsed -ge 1000) { return $parsed }
    return 200000
}

# Runs a read-only git command and returns its output as one string. A git failure exits 2 with git's message.
function Get-MuxGitText {
    param([string[]]$Arguments)
    $text = (& git -c core.quotepath=off @Arguments 2>&1 | Out-String)
    if ($LASTEXITCODE -ne 0) {
        Write-Host $text.TrimEnd()
        Exit-MuxNotApplicable ('git ' + ($Arguments -join ' ') + ' failed.')
    }

    return $text
}

# Prints text, cut at the diff limit with a note telling the model how to see the rest.
function Write-MuxLimited {
    param([string]$Text)
    $limit = Get-MuxDiffLimit
    if ($Text.Length -gt $limit) {
        Write-Output $Text.Substring(0, $limit)
        Write-Output ('[mux: output cut at ' + $limit + ' characters; review the remaining files one at a time with code-review file <path>]')
    } else {
        Write-Output $Text.TrimEnd()
    }
}

# Prints each untracked file (not ignored) as added lines, skipping binary files and capping each at 400 lines.
# The number of files is left in $script:MuxUntrackedCount.
function Write-MuxUntrackedFiles {
    $files = @(& git -c core.quotepath=off ls-files --others --exclude-standard 2>$null)
    foreach ($file in $files) {
        Write-Output ('=== new untracked file: ' + $file)
        try {
            $bytes = [IO.File]::ReadAllBytes((Join-Path (Get-Location).Path $file))
            $probe = [Math]::Min($bytes.Length, 8000)
            if ([Array]::IndexOf($bytes, [byte]0, 0, $probe) -ge 0) { Write-Output '(binary file)'; continue }
            $lines = @(Get-Content -LiteralPath $file -TotalCount 401)
            $lines | Select-Object -First 400 | ForEach-Object { Write-Output ('+' + $_) }
            if ($lines.Count -gt 400) { Write-Output '[mux: file cut at 400 lines]' }
        } catch {
            Write-Output ('(could not read: ' + $_.Exception.Message + ')')
        }
    }

    $script:MuxUntrackedCount = $files.Count
}

# Returns the untracked (not ignored) text files as unified-diff text of added lines, so scans that read diffs also
# see brand-new files. Binary files and anything past 2000 lines per file are skipped.
function Get-MuxUntrackedAsDiff {
    $builder = New-Object System.Text.StringBuilder
    foreach ($file in @(& git -c core.quotepath=off ls-files --others --exclude-standard 2>$null)) {
        try {
            $bytes = [IO.File]::ReadAllBytes((Join-Path (Get-Location).Path $file))
            if ([Array]::IndexOf($bytes, [byte]0, 0, [Math]::Min($bytes.Length, 8000)) -ge 0) { continue }
            [void]$builder.AppendLine('+++ b/' + $file)
            [void]$builder.AppendLine('@@ -0,0 +1 @@')
            foreach ($line in @(Get-Content -LiteralPath $file -TotalCount 2000)) { [void]$builder.AppendLine('+' + $line) }
        } catch {
            continue
        }
    }

    return $builder.ToString()
}

# The merge base between HEAD and a base ref. Exits 2 when the base does not exist.
function Get-MuxMergeBase {
    param([string]$Base)
    if (-not (Test-MuxGitRef $Base)) { Exit-MuxNotApplicable ("'" + $Base + "' is not a branch, tag, or commit in this repository.") }
    $mergeBase = & git merge-base HEAD $Base 2>$null
    if ($LASTEXITCODE -ne 0 -or -not $mergeBase) { Exit-MuxNotApplicable ("HEAD and '" + $Base + "' share no history.") }
    return "$mergeBase".Trim()
}

# Files changed since a base (committed and uncommitted, deletions excluded) plus untracked files.
function Get-MuxChangedFiles {
    param([string]$Since)
    $files = New-Object System.Collections.Generic.List[string]
    if ($Since) { @(& git -c core.quotepath=off diff --name-only --diff-filter=d $Since 2>$null) | ForEach-Object { if ($_) { $files.Add($_) } } }
    @(& git -c core.quotepath=off ls-files --others --exclude-standard 2>$null) | ForEach-Object { if ($_) { $files.Add($_) } }
    return @($files | Select-Object -Unique)
}

# The commit to compare the working tree against for "changed files": the merge base with the given or default
# branch when there is one, otherwise HEAD (only uncommitted changes), or nothing in a repository with no commits.
function Get-MuxChangeBase {
    param([string]$Base)
    $hasHead = Test-MuxGitRef 'HEAD'
    if (-not $hasHead) { return $null }
    if (-not $Base) { $Base = Get-MuxDefaultBranch }
    if (-not $Base) { return 'HEAD' }
    return Get-MuxMergeBase $Base
}

# Scans the added lines of a unified diff for likely secrets and prints each with a masked value. The number found
# is left in $script:MuxSecretCount.
function Write-MuxSecretFindings {
    param([string]$DiffText)
    $patterns = @(
        @('AWS access key id', 'AKIA[0-9A-Z]{16}'),
        @('private key', '-----BEGIN [A-Z ]*PRIVATE KEY-----'),
        @('GitHub token', '(ghp|gho|ghu|ghs|ghr)_[A-Za-z0-9]{36}|github_pat_[A-Za-z0-9_]{40,}'),
        @('Slack token', 'xox[abprs]-[A-Za-z0-9-]{10,}'),
        @('API secret key', 'sk-[A-Za-z0-9_-]{20,}'),
        @('hard-coded credential', '(?i)(password|passwd|pwd|secret|api[_-]?key|access[_-]?token|auth[_-]?token)["'']?\s*[:=]\s*["''][^"''\s]{8,}["'']')
    )
    $file = ''
    $line = 0
    $count = 0
    foreach ($row in ($DiffText -split "`r?`n")) {
        if ($row -match '^\+\+\+ (b/)?(.+)$') { $file = $Matches[2]; continue }
        if ($row -match '^@@ -\d+(,\d+)? \+(\d+)') { $line = [int]$Matches[2] - 1; continue }
        if ($row.StartsWith('-')) { continue }
        if ($row.StartsWith('+')) {
            $line++
            foreach ($pattern in $patterns) {
                $hit = [regex]::Match($row, $pattern[1])
                if ($hit.Success) {
                    $value = $hit.Value
                    $masked = if ($value.Length -gt 8) { $value.Substring(0, 4) + ('*' * [Math]::Min(12, $value.Length - 4)) } else { '****' }
                    if ($count -eq 0) { Write-Output 'Possible secrets in added lines (verify each; values are masked):' }
                    Write-Output ('- ' + $file + ':' + $line + '  ' + $pattern[0] + '  ' + $masked)
                    $count++
                    break
                }
            }
        } elseif (-not $row.StartsWith('\')) {
            $line++
        }
    }

    if ($count -eq 0) { Write-Output 'No likely secrets in added lines.' }
    $script:MuxSecretCount = $count
}

# Lists dependency manifests touched by a diff and the audit skills that apply to them.
function Write-MuxManifestChanges {
    param([string[]]$Files)
    $map = [ordered]@{
        'js-deps audit' = '(^|/)(package\.json|package-lock\.json|npm-shrinkwrap\.json|pnpm-lock\.yaml|yarn\.lock|bun\.lockb?)$'
        'py-deps audit' = '(^|/)(requirements[^/]*\.txt|pyproject\.toml|poetry\.lock|uv\.lock|Pipfile(\.lock)?|setup\.py)$'
        'dotnet-outdated vulnerable' = '(\.csproj|\.fsproj|(^|/)Directory\.Packages\.props|(^|/)packages\.lock\.json)$'
        'java-deps tree' = '(^|/)(pom\.xml|build\.gradle(\.kts)?|gradle\.lockfile)$'
        'go-mod outdated' = '(^|/)(go\.mod|go\.sum)$'
        'cargo-build (then cargo audit if installed)' = '(^|/)(Cargo\.toml|Cargo\.lock)$'
    }
    $touched = @()
    foreach ($entry in $map.GetEnumerator()) {
        $hits = @($Files | Where-Object { ($_ -replace '\\', '/') -match $entry.Value })
        if ($hits.Count -gt 0) { $touched += ('- ' + ($hits -join ', ') + '  ->  run ' + $entry.Key) }
    }

    if ($touched.Count -eq 0) { Write-Output 'No dependency manifests changed.'; return }
    Write-Output 'Dependency manifests changed (check new or upgraded packages for advisories):'
    $touched | ForEach-Object { Write-Output $_ }
}

# Formats GitHub review threads (the GraphQL reviewThreads shape) with unresolved threads first.
function Write-MuxReviewThreads {
    param($Threads)
    $open = @($Threads | Where-Object { -not $_.isResolved })
    $done = @($Threads | Where-Object { $_.isResolved })
    Write-Output ('Review threads: ' + $open.Count + ' unresolved, ' + $done.Count + ' resolved.')
    foreach ($group in @(@('UNRESOLVED', $open), @('RESOLVED', $done))) {
        foreach ($thread in $group[1]) {
            $where = if ($thread.path) { $thread.path + $(if ($thread.line) { ':' + $thread.line } else { '' }) } else { '(pull request)' }
            $comments = @($thread.comments.nodes)
            Write-Output ('[' + $group[0] + '] ' + $where)
            foreach ($comment in $comments) {
                $author = if ($comment.author) { $comment.author.login } else { 'unknown' }
                $text = ("$($comment.body)" -replace "`r?`n", ' ').Trim()
                if ($text.Length -gt 600) { $text = $text.Substring(0, 600) + '...' }
                Write-Output ('    ' + $author + ': ' + $text)
            }
        }
    }
}

# Whether a path looks like a test file in any common convention.
function Test-MuxTestPath {
    param([string]$Path)
    $p = $Path -replace '\\', '/'
    $name = Split-Path -Leaf $p
    return ($p -match '(^|/)(tests?|__tests__|spec|specs|testing|Test\.[^/]+|[^/]*\.Tests?)/') -or
        ($name -match '(?i)^test_.*\.py$|_test\.(py|go|rs|exs?)$|\.(test|spec)\.[cm]?[jt]sx?$|(Tests?|Spec|IT)\.(cs|fs|vb|java|kt|scala|swift)$|_spec\.rb$|Test\.php$')
}

# The bare stem used to pair a source file with its tests (lower case, test affixes removed).
function Get-MuxTestStem {
    param([string]$Path)
    $name = [IO.Path]::GetFileNameWithoutExtension((Split-Path -Leaf $Path))
    $name = $name -replace '(?i)\.(test|spec)$', ''
    $name = $name -replace '(?i)^test_', ''
    $name = $name -replace '(?i)(_test|_spec|Tests?|Spec|IT)$', ''
    return $name.ToLowerInvariant()
}

# Returns the positional argument at $Index (from the script's $args), or the default when it is missing or blank.
function Get-MuxArg {
    param([object[]]$Arguments, [int]$Index, [string]$Default = '')
    if ($null -ne $Arguments -and $Arguments.Count -gt $Index -and -not [string]::IsNullOrWhiteSpace([string]$Arguments[$Index])) {
        return [string]$Arguments[$Index]
    }

    return $Default
}

# ---------------------------------------------------------------------------------------------------------------
# Loops (loop-until, fix-until-green, ci-watch, flaky-test-hunt)
# ---------------------------------------------------------------------------------------------------------------

# Parses a whole number from Min to Max; exits 2 naming the argument when it is missing or out of range.
function Get-MuxBoundedInt {
    param([string]$Value, [int]$Min, [int]$Max, [string]$Name)
    $parsed = 0
    if (-not [int]::TryParse($Value, [ref]$parsed) -or $parsed -lt $Min -or $parsed -gt $Max) {
        Exit-MuxNotApplicable ($Name + ' must be a whole number from ' + $Min + ' to ' + $Max + " (got '" + $Value + "').")
    }

    return $parsed
}

# Prints the last Count lines of the text, noting how many were left out.
function Write-MuxTail {
    param([string]$Text, [int]$Count = 60)
    $lines = New-Object System.Collections.Generic.List[string]
    foreach ($line in (($Text -replace "`r", '') -split "`n")) { $lines.Add($line) }
    while ($lines.Count -gt 0 -and -not $lines[$lines.Count - 1].Trim()) { $lines.RemoveAt($lines.Count - 1) }
    $start = 0
    if ($lines.Count -gt $Count) {
        Write-Output ('[mux: showing the last ' + $Count + ' of ' + $lines.Count + ' lines]')
        $start = $lines.Count - $Count
    }

    for ($i = $start; $i -lt $lines.Count; $i++) { Write-Output $lines[$i] }
}

# Runs a command and captures its combined output without printing it. Leaves the output in
# $script:MuxCapturedOutput and the exit code in $script:MuxCapturedExit. A missing tool exits 2 with the hint.
function Invoke-MuxCaptured {
    param([Parameter(Mandatory = $true)][string]$Tool, [string[]]$Arguments = @(), [string]$InstallHint = '')
    if (-not (Test-MuxTool $Tool) -and -not (Test-Path -LiteralPath $Tool -PathType Leaf)) {
        Exit-MuxNotApplicable ("'$Tool' was not found on PATH. $InstallHint".Trim())
    }

    $global:LASTEXITCODE = 0
    try {
        $script:MuxCapturedOutput = (& $Tool @Arguments 2>&1 | Out-String)
        $script:MuxCapturedExit = $LASTEXITCODE
    } catch {
        $script:MuxCapturedOutput = $_.Exception.Message
        $script:MuxCapturedExit = 1
    }
}

# The command (@{ Tool; Arguments; Hint }) that runs a Python module inside the project's environment.
function Get-MuxPythonModuleCommand {
    param([string]$Dir, [string[]]$Arguments)
    switch (Get-MuxPythonManager -Dir $Dir) {
        'uv' { return @{ Tool = 'uv'; Arguments = @('run', 'python') + $Arguments; Hint = 'Install uv from https://docs.astral.sh/uv/.' } }
        'poetry' { return @{ Tool = 'poetry'; Arguments = @('run', 'python') + $Arguments; Hint = 'Install Poetry from https://python-poetry.org.' } }
        'pipenv' { return @{ Tool = 'pipenv'; Arguments = @('run', 'python') + $Arguments; Hint = 'Install pipenv: pip install --user pipenv.' } }
        default {
            $venv = Get-MuxVenvPython -Dir $Dir
            $python = if ($venv) { $venv } else { Get-MuxSystemPython }
            return @{ Tool = $python; Arguments = $Arguments; Hint = 'Install Python 3 from https://www.python.org.' }
        }
    }
}

# The project's build and test commands, detected in this order: .NET, JavaScript, Python, Go, Rust, Java, CMake.
# Returns @{ Kind; Build; Test } where Build and Test are @{ Tool; Arguments; Hint } (Build is $null when the
# toolchain has no separate build step). Filter, when given, narrows the tests to matching names. Exits 2 when no
# supported project is found.
function Get-MuxCheckPlan {
    param([string]$Filter = '')
    $hasFilter = [bool]$Filter

    if (Find-MuxUp -Names @('*.sln', '*.slnx', '*.csproj', '*.fsproj')) {
        $hint = 'Install the .NET SDK from https://dot.net.'
        $test = @('test', '--nologo')
        if ($hasFilter) { $test += @('--filter', $Filter) }
        return @{ Kind = '.NET'; Build = @{ Tool = 'dotnet'; Arguments = @('build', '--nologo'); Hint = $hint }; Test = @{ Tool = 'dotnet'; Arguments = $test; Hint = $hint } }
    }

    $nodeDir = Find-MuxUp -Names @('package.json')
    if ($nodeDir) {
        $pkg = Get-MuxPackageJson -Dir $nodeDir
        $manager = Get-MuxNodePackageManager -Dir $nodeDir
        $hint = Get-MuxNodeInstallHint $manager
        $build = $null
        if (Test-MuxPackageScript $pkg 'build') { $build = @{ Tool = $manager; Arguments = @('run', 'build'); Hint = $hint } }
        if (-not $hasFilter -and (Test-MuxPackageScript $pkg 'test')) {
            $test = @{ Tool = $manager; Arguments = @('run', 'test'); Hint = $hint }
        } else {
            switch (Get-MuxJsTestRunner $pkg) {
                'vitest' { $a = @('run'); if ($hasFilter) { $a += @('-t', $Filter) }; $c = Get-MuxPackageBinCommand -Manager $manager -Bin 'vitest' -Arguments $a }
                'jest' { $a = @('--ci'); if ($hasFilter) { $a += @('-t', $Filter) }; $c = Get-MuxPackageBinCommand -Manager $manager -Bin 'jest' -Arguments $a }
                'mocha' { $a = @(); if ($hasFilter) { $a += @('--grep', $Filter) }; $c = Get-MuxPackageBinCommand -Manager $manager -Bin 'mocha' -Arguments $a }
                default { $a = @('--test'); if ($hasFilter) { $a += ('--test-name-pattern=' + $Filter) }; $c = @{ Tool = 'node'; Arguments = $a } }
            }

            $test = @{ Tool = $c.Tool; Arguments = $c.Arguments; Hint = $hint }
        }

        return @{ Kind = 'JavaScript (' + $manager + ')'; Build = $build; Test = $test }
    }

    $pyDir = Find-MuxUp -Names @('pyproject.toml', 'requirements*.txt', 'setup.py', 'setup.cfg', 'Pipfile', 'uv.lock', 'poetry.lock')
    if ($pyDir) {
        $a = @('-m', 'pytest', '-q')
        if ($hasFilter) { $a += @('-k', $Filter) }
        return @{ Kind = 'Python'; Build = $null; Test = (Get-MuxPythonModuleCommand -Dir $pyDir -Arguments $a) }
    }

    if (Find-MuxUp -Names @('go.mod')) {
        $hint = 'Install Go from https://go.dev/dl/.'
        $a = @('test', '-count=1')
        if ($hasFilter) { $a += @('-run', $Filter) }
        $a += './...'
        return @{ Kind = 'Go'; Build = @{ Tool = 'go'; Arguments = @('build', './...'); Hint = $hint }; Test = @{ Tool = 'go'; Arguments = $a; Hint = $hint } }
    }

    if (Find-MuxUp -Names @('Cargo.toml')) {
        $hint = 'Install Rust from https://rustup.rs.'
        $a = @('test')
        if ($hasFilter) { $a += $Filter }
        return @{ Kind = 'Rust'; Build = @{ Tool = 'cargo'; Arguments = @('build'); Hint = $hint }; Test = @{ Tool = 'cargo'; Arguments = $a; Hint = $hint } }
    }

    $javaDir = Find-MuxUp -Names @('mvnw', 'gradlew', 'pom.xml', 'build.gradle', 'build.gradle.kts')
    if ($javaDir) {
        $java = Get-MuxJavaBuild -Dir $javaDir
        if ($java.Kind -eq 'maven') {
            $hint = 'Install Maven from https://maven.apache.org or add the Maven wrapper.'
            $a = @('-B', 'test')
            if ($hasFilter) { $a += ('-Dtest=' + $Filter) }
            return @{ Kind = 'Java (Maven)'; Build = @{ Tool = $java.Tool; Arguments = @('-B', 'compile'); Hint = $hint }; Test = @{ Tool = $java.Tool; Arguments = $a; Hint = $hint } }
        }

        $hint = 'Install Gradle from https://gradle.org or add the Gradle wrapper.'
        $a = @('--console=plain', 'test')
        if ($hasFilter) { $a += @('--tests', $Filter) }
        return @{ Kind = 'Java (Gradle)'; Build = @{ Tool = $java.Tool; Arguments = @('--console=plain', 'assemble'); Hint = $hint }; Test = @{ Tool = $java.Tool; Arguments = $a; Hint = $hint } }
    }

    $cmakeDir = Find-MuxUp -Names @('CMakeLists.txt')
    if ($cmakeDir) {
        $buildDir = Join-Path $cmakeDir 'build'
        if (-not (Test-Path -LiteralPath $buildDir)) { Exit-MuxNotApplicable 'CMake project has no build directory yet; run cpp-configure first.' }
        $hint = 'Install CMake from https://cmake.org.'
        $a = @('--test-dir', $buildDir, '--output-on-failure')
        if ($hasFilter) { $a += @('-R', $Filter) }
        return @{ Kind = 'C/C++ (CMake)'; Build = @{ Tool = 'cmake'; Arguments = @('--build', $buildDir); Hint = $hint }; Test = @{ Tool = 'ctest'; Arguments = $a; Hint = $hint } }
    }

    Exit-MuxNotApplicable 'no supported project found (.NET, JavaScript, Python, Go, Rust, Java, or CMake).'
}

# Runs one check step and prints a one-line PASS or FAIL, plus the tail of the output on failure. Leaves the exit
# code in $script:MuxStepExit. In a dry run the command is printed and the step counts as passed.
function Invoke-MuxCheckStep {
    param([string]$Name, $Step, [int]$TailLines = 80)
    $line = Format-MuxCommand -Tool $Step.Tool -Arguments $Step.Arguments
    $script:MuxStepExit = 0
    if (Test-MuxDryRun) { Write-Output ('DRYRUN: ' + $line); return }
    $watch = [System.Diagnostics.Stopwatch]::StartNew()
    Invoke-MuxCaptured -Tool $Step.Tool -Arguments $Step.Arguments -InstallHint $Step.Hint
    $script:MuxStepExit = $script:MuxCapturedExit
    $seconds = [Math]::Round($watch.Elapsed.TotalSeconds, 1)
    if ($script:MuxStepExit -eq 0) {
        Write-Output ('== ' + $Name + ': PASS in ' + $seconds + 's  (' + $line + ')')
    } else {
        Write-Output ('== ' + $Name + ': FAIL with exit ' + $script:MuxStepExit + ' in ' + $seconds + 's  (' + $line + ')')
        Write-MuxTail -Text $script:MuxCapturedOutput -Count $TailLines
    }
}

# Reads a gh JSON result from --from-file (for offline use and tests) or by running gh. Returns the parsed object.
function Get-MuxGhJson {
    param([string]$FromFile, [string[]]$Arguments)
    if ($FromFile) {
        if (-not (Test-Path -LiteralPath $FromFile -PathType Leaf)) { Exit-MuxNotApplicable ($FromFile + ' does not exist.') }
        $text = Get-Content -LiteralPath $FromFile -Raw
    } else {
        $hint = 'Install the GitHub CLI from https://cli.github.com and sign in with gh auth login.'
        Invoke-MuxCaptured -Tool 'gh' -Arguments $Arguments -InstallHint $hint
        if ($script:MuxCapturedExit -ne 0) { Write-Output $script:MuxCapturedOutput.TrimEnd(); exit 2 }
        $text = $script:MuxCapturedOutput
    }

    try {
        return ($text | ConvertFrom-Json -NoEnumerate)
    } catch {
        Exit-MuxNotApplicable ('the GitHub response is not valid JSON: ' + $_.Exception.Message)
    }
}

# Splits "--name value" options out of the arguments. Returns @{ Options = hashtable; Rest = list }.
function Split-MuxOptions {
    param([object[]]$Arguments, [string[]]$Names)
    $options = @{}
    $rest = New-Object System.Collections.Generic.List[string]
    $all = @($Arguments)
    for ($i = 0; $i -lt $all.Count; $i++) {
        $token = [string]$all[$i]
        if ($Names -contains $token) {
            if (($i + 1) -ge $all.Count) { Exit-MuxNotApplicable ($token + ' needs a value.') }
            $options[$token] = [string]$all[$i + 1]
            $i++
        } else {
            $rest.Add($token)
        }
    }

    return @{ Options = $options; Rest = $rest }
}
