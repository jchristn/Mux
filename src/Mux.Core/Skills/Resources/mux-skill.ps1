# mux-skill.ps1: shared helpers for mux's default toolchain skills.
#
# Each command block dot-sources this file first:
#   . (Join-Path $env:MUX_SKILL_DIR 'resources/mux-skill.ps1')
#
# Exit codes used by every toolchain skill:
#   0  success
#   1  the tool ran and reported problems (failing tests, lint findings, a failed build)
#   2  the tool is not installed, or the project does not use this toolchain
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
    Write-Output "mux: $Message"
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

function Get-MuxNodeInstallHint {
    param([string]$Manager)
    switch ($Manager) {
        'pnpm' { return 'Install pnpm: corepack enable pnpm (or npm install -g pnpm).' }
        'yarn' { return 'Install yarn: corepack enable yarn (or npm install -g yarn).' }
        'bun' { return 'Install bun from https://bun.sh.' }
        default { return 'Install Node.js (which includes npm) from https://nodejs.org.' }
    }
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

# Returns the positional argument at $Index (from the script's $args), or the default when it is missing or blank.
function Get-MuxArg {
    param([object[]]$Arguments, [int]$Index, [string]$Default = '')
    if ($null -ne $Arguments -and $Arguments.Count -gt $Index -and -not [string]::IsNullOrWhiteSpace([string]$Arguments[$Index])) {
        return [string]$Arguments[$Index]
    }

    return $Default
}
