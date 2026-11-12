<#
Install, check and remove the mux desktop app ("Mux", src\Mux.Desktop) and tray agent ("Mux Agent",
src\Mux.Agent) on Windows. Called by install-tool.bat, reinstall-tool.bat, remove-tool.bat and
run-at-startup.bat.
Runs in Windows PowerShell 5.1 and PowerShell 7.

  -Action Check                    Fail when an app that would be replaced or removed is running.
  -Action Install -Framework <tfm> Publish both apps self-contained once (artifacts\windows-app\<rid>\<tfm>\<component>),
                                   then install them per-user and, from an elevated prompt, system-wide.
  -Action Remove                   Remove both installs and their Start Menu shortcuts.
  -Action InstallUserAgent -Framework <tfm>
                                   Publish only the tray agent and install it per-user only (never system-wide,
                                   so no elevation), with its Start Menu shortcut. run-at-startup.bat uses it,
                                   then registers %LOCALAPPDATA%\Programs\Mux\Agent\Mux.Agent.exe.

Per-user:    %LOCALAPPDATA%\Programs\Mux\Desktop\Mux.Desktop.exe and ...\Mux\Agent\Mux.Agent.exe,
             shortcuts "Mux.lnk" and "Mux Agent.lnk" in %APPDATA%\Microsoft\Windows\Start Menu\Programs\Mux.
System-wide: %ProgramFiles%\Mux\Desktop and ...\Mux\Agent, shortcuts in
             %ProgramData%\Microsoft\Windows\Start Menu\Programs\Mux. Needs an elevated prompt; without one the
             script prints a warning and continues. MUX_SYSTEM_INSTALL=0 skips it quietly.
These paths are fixed: Mux.Core's agent locator searches exactly them.

Exit code 0 on success, 1 on failure (with an "Error: ..." line on stderr).
#>
[CmdletBinding()]
param(
    [string]$Action,
    [string]$Framework
)

$ErrorActionPreference = 'Stop'

$script:MuxApps = @(
    [pscustomobject]@{ Component = 'desktop'; Name = 'Mux'; Executable = 'Mux.Desktop'; Folder = 'Desktop'; Description = 'Mux desktop app' },
    [pscustomobject]@{ Component = 'agent'; Name = 'Mux Agent'; Executable = 'Mux.Agent'; Folder = 'Agent'; Description = 'Mux Agent: the system-tray host for the mux server' }
)
$script:KillHint = 'scripts\windows\kill-mux.bat'

# ---------------------------------------------------------------------------------------------------------
# Platform calls. Kept in small functions so tests can replace them.

# True when this process runs elevated (as Administrator).
function Test-MuxElevated {
    try {
        $principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
        return [bool]$principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
    } catch {
        return $false
    }
}

# Running processes with their executable path (Path is empty when it cannot be read).
function Get-MuxProcesses {
    $result = @()
    $unknown = $false
    foreach ($process in @(Get-Process -ErrorAction SilentlyContinue)) {
        $path = $null
        try { $path = $process.Path } catch { $path = $null }
        if (-not $path -and $process.ProcessName -match '^Mux\.') { $unknown = $true }
        $result += [pscustomobject]@{ Name = $process.ProcessName; Id = $process.Id; Path = $path }
    }
    # Process.Path cannot read every process (for example 64-bit ones from 32-bit PowerShell); ask WMI for
    # the mux apps it missed.
    if ($unknown) {
        try {
            $wmi = @{}
            foreach ($p in @(Get-CimInstance Win32_Process -ErrorAction Stop)) {
                if ($p.ExecutablePath) { $wmi[[int]$p.ProcessId] = $p.ExecutablePath }
            }
            foreach ($entry in $result) {
                if (-not $entry.Path -and $wmi.ContainsKey([int]$entry.Id)) { $entry.Path = $wmi[[int]$entry.Id] }
            }
        } catch { }
    }
    return $result
}

# Run dotnet with the given arguments; returns its exit code. Output goes straight to the console.
function Invoke-MuxDotnet([string[]]$Arguments) {
    & dotnet @Arguments | Out-Host
    return $LASTEXITCODE
}

# Create (or overwrite) a .lnk shortcut.
function New-MuxShortcut([string]$Path, [string]$TargetPath, [string]$WorkingDirectory, [string]$IconPath, [string]$Description) {
    $shell = New-Object -ComObject WScript.Shell
    try {
        $shortcut = $shell.CreateShortcut($Path)
        $shortcut.TargetPath = $TargetPath
        $shortcut.WorkingDirectory = $WorkingDirectory
        $shortcut.IconLocation = "$IconPath,0"
        $shortcut.Description = $Description
        $shortcut.Save()
    } finally {
        [void][Runtime.InteropServices.Marshal]::ReleaseComObject($shell)
    }
}

function Write-MuxWarning([string]$Message) {
    [Console]::Error.WriteLine("Warning: $Message")
}

function Write-MuxInfo([string]$Message) {
    Write-Host $Message
}

# ---------------------------------------------------------------------------------------------------------
# Paths and plan.

# An environment variable holding a folder, falling back to the matching special folder.
function Get-MuxFolder([string]$Name, [Environment+SpecialFolder]$Fallback) {
    $value = [Environment]::GetEnvironmentVariable($Name)
    if ([string]::IsNullOrEmpty($value)) { $value = [Environment]::GetFolderPath($Fallback) }
    if ([string]::IsNullOrEmpty($value)) { throw "Cannot determine %$Name%." }
    return $value
}

# The per-user and system-wide install targets, in that order.
function Get-MuxInstallTargets {
    # A 32-bit PowerShell sees "Program Files (x86)" as %ProgramFiles%; the apps are 64-bit, so use the
    # native folder that the agent locator (a 64-bit process) sees.
    $programFiles = $env:ProgramW6432
    if ([string]::IsNullOrEmpty($programFiles)) { $programFiles = Get-MuxFolder 'ProgramFiles' ProgramFiles }
    $user = [pscustomobject]@{
        Scope       = 'user'
        AppRoot     = [IO.Path]::Combine((Get-MuxFolder 'LOCALAPPDATA' LocalApplicationData), 'Programs', 'Mux')
        ShortcutDir = [IO.Path]::Combine((Get-MuxFolder 'APPDATA' ApplicationData), 'Microsoft', 'Windows', 'Start Menu', 'Programs', 'Mux')
    }
    $system = [pscustomobject]@{
        Scope       = 'system'
        AppRoot     = [IO.Path]::Combine($programFiles, 'Mux')
        ShortcutDir = [IO.Path]::Combine((Get-MuxFolder 'ProgramData' CommonApplicationData), 'Microsoft', 'Windows', 'Start Menu', 'Programs', 'Mux')
    }
    return @($user, $system)
}

function Get-MuxAppDir($Target, $App) { return [IO.Path]::Combine($Target.AppRoot, $App.Folder) }
function Get-MuxShortcutPath($Target, $App) { return [IO.Path]::Combine($Target.ShortcutDir, $App.Name + '.lnk') }

# Which targets this run touches. SystemSkipped is $null, 'disabled' (MUX_SYSTEM_INSTALL=0) or 'not-elevated'.
function Get-MuxPlan {
    $targets = Get-MuxInstallTargets
    $user = $targets[0]
    $system = $targets[1]
    $skipped = $null
    if ($env:MUX_SYSTEM_INSTALL -eq '0') {
        $skipped = 'disabled'
    } elseif (-not (Test-MuxElevated)) {
        $skipped = 'not-elevated'
    }
    if ($skipped) { $active = @($user) } else { $active = @($user, $system) }
    return [pscustomobject]@{ Targets = $active; User = $user; System = $system; SystemSkipped = $skipped }
}

# True when Path is Directory itself or inside it (case-insensitive, as Windows paths are).
function Test-MuxPathUnder([string]$Path, [string]$Directory) {
    if ([string]::IsNullOrEmpty($Path) -or [string]::IsNullOrEmpty($Directory)) { return $false }
    try {
        $full = [IO.Path]::GetFullPath($Path)
        $dir = [IO.Path]::GetFullPath($Directory).TrimEnd('\', '/')
    } catch {
        return $false
    }
    if ($full.Equals($dir, [StringComparison]::OrdinalIgnoreCase)) { return $true }
    return $full.StartsWith($dir + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or
        $full.StartsWith($dir + '/', [StringComparison]::OrdinalIgnoreCase)
}

# Display names of the apps (all of them unless Apps is given) running from the given targets' install folders.
function Get-MuxRunningApps($Targets, $Apps = $script:MuxApps) {
    $processes = @(Get-MuxProcesses)
    $running = @()
    foreach ($target in @($Targets)) {
        foreach ($app in @($Apps)) {
            $dir = Get-MuxAppDir $target $app
            foreach ($process in $processes) {
                if ((Test-MuxPathUnder $process.Path $dir) -and ($running -notcontains $app.Name)) { $running += $app.Name }
            }
        }
    }
    return $running
}

# Never replace or remove an app while it runs from the folder being replaced.
function Assert-MuxNotRunning($Targets, $Apps = $script:MuxApps) {
    $running = @(Get-MuxRunningApps $Targets $Apps)
    if ($running.Count -gt 0) {
        throw "$($running[0]) is running. Quit it (or run $script:KillHint) and try again."
    }
}

function Get-MuxRid {
    # PROCESSOR_ARCHITEW6432 is set when a 32-bit process runs on 64-bit Windows.
    $arch = $env:PROCESSOR_ARCHITEW6432
    if ([string]::IsNullOrEmpty($arch)) { $arch = $env:PROCESSOR_ARCHITECTURE }
    switch ($arch) {
        'AMD64' { return 'win-x64' }
        'ARM64' { return 'win-arm64' }
        default { throw "Unsupported Windows architecture: $arch" }
    }
}

function Remove-MuxPath([string]$Path) {
    if (Test-Path -LiteralPath $Path) { Remove-Item -LiteralPath $Path -Recurse -Force }
}

function Remove-MuxEmptyDir([string]$Path) {
    if ((Test-Path -LiteralPath $Path -PathType Container) -and
        @(Get-ChildItem -LiteralPath $Path -Force).Count -eq 0) {
        Remove-Item -LiteralPath $Path -Force
    }
}

# ---------------------------------------------------------------------------------------------------------
# Actions.

# Publish one app self-contained into its stage folder; returns the stage folder.
function Publish-MuxApp([string]$RepoRoot, $App, [string]$Rid, [string]$Framework) {
    $project = [IO.Path]::Combine($RepoRoot, 'src', $App.Executable, $App.Executable + '.csproj')
    $stage = [IO.Path]::Combine($RepoRoot, 'artifacts', 'windows-app', $Rid, $Framework, $App.Component)
    $icon = [IO.Path]::Combine($RepoRoot, 'assets', 'icons', 'icon-green.ico')
    if (-not (Test-Path -LiteralPath $project -PathType Leaf)) { throw "Project not found: $project" }
    if (-not (Test-Path -LiteralPath $icon -PathType Leaf)) { throw "Icon not found: $icon" }
    Remove-MuxPath $stage
    Write-MuxInfo "Publishing $($App.Name) ($Rid, $Framework)..."
    $code = Invoke-MuxDotnet @('publish', $project, '--configuration', 'Release', '--framework', $Framework,
        "-p:TargetFrameworks=$Framework", '--runtime', $Rid, '--self-contained', 'true', '--output', $stage)
    if ($code -ne 0) { throw "Publishing $($App.Name) failed (dotnet exit code $code)." }
    $exe = [IO.Path]::Combine($stage, $App.Executable + '.exe')
    if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) { throw "$($App.Executable).exe was not found in $stage after publishing." }
    Copy-Item -LiteralPath $icon -Destination ([IO.Path]::Combine($stage, 'mux.ico')) -Force
    return $stage
}

# Copy a published app into one target and create its Start Menu shortcut.
function Install-MuxAppCopy([string]$Stage, $App, $Target) {
    $dest = Get-MuxAppDir $Target $App
    $incoming = "$dest.new"
    New-Item -ItemType Directory -Path $Target.AppRoot -Force | Out-Null
    # Copy next to the old copy first, so a failed copy leaves the installed app untouched.
    Remove-MuxPath $incoming
    Copy-Item -LiteralPath $Stage -Destination $incoming -Recurse -Force
    Remove-MuxPath $dest
    Move-Item -LiteralPath $incoming -Destination $dest
    $exe = [IO.Path]::Combine($dest, $App.Executable + '.exe')
    New-Item -ItemType Directory -Path $Target.ShortcutDir -Force | Out-Null
    New-MuxShortcut -Path (Get-MuxShortcutPath $Target $App) -TargetPath $exe -WorkingDirectory $dest `
        -IconPath ([IO.Path]::Combine($dest, 'mux.ico')) -Description $App.Description
    Write-MuxInfo "Installed $exe"
}

function Install-MuxApps([string]$RepoRoot, [string]$Framework) {
    if ([string]::IsNullOrEmpty($Framework)) { throw 'Install needs -Framework <tfm>.' }
    $plan = Get-MuxPlan
    Assert-MuxNotRunning $plan.Targets
    $rid = Get-MuxRid
    # Build once, both apps, before touching any installed copy.
    $stages = @{}
    foreach ($app in $script:MuxApps) { $stages[$app.Component] = Publish-MuxApp $RepoRoot $app $rid $Framework }
    # Publishing takes a while; check again right before replacing anything.
    Assert-MuxNotRunning $plan.Targets
    foreach ($target in $plan.Targets) {
        foreach ($app in $script:MuxApps) { Install-MuxAppCopy $stages[$app.Component] $app $target }
    }
    if ($plan.SystemSkipped -eq 'not-elevated') {
        Write-MuxWarning ("Skipped the system-wide install to $($plan.System.AppRoot); the per-user install is done. " +
            'Run from an elevated prompt to also install for all users, or set MUX_SYSTEM_INSTALL=0 to skip it quietly.')
    }
}

# Publish only the tray agent and install it per-user only, with its shortcut. Never touches the system-wide
# copy, so it needs no elevation and ignores MUX_SYSTEM_INSTALL.
function Install-MuxUserAgent([string]$RepoRoot, [string]$Framework) {
    if ([string]::IsNullOrEmpty($Framework)) { throw 'InstallUserAgent needs -Framework <tfm>.' }
    $user = (Get-MuxInstallTargets)[0]
    $agent = @($script:MuxApps | Where-Object { $_.Component -eq 'agent' })[0]
    Assert-MuxNotRunning @($user) @($agent)
    $stage = Publish-MuxApp $RepoRoot $agent (Get-MuxRid) $Framework
    Assert-MuxNotRunning @($user) @($agent)
    Install-MuxAppCopy $stage $agent $user
}

# Installed folders and shortcuts that exist for one target.
function Get-MuxInstalledItems($Target) {
    $items = @()
    foreach ($app in $script:MuxApps) {
        foreach ($path in @((Get-MuxAppDir $Target $app), (Get-MuxShortcutPath $Target $app))) {
            if (Test-Path -LiteralPath $path) { $items += $path }
        }
    }
    return $items
}

function Remove-MuxInstall($Target) {
    foreach ($app in $script:MuxApps) {
        $dir = Get-MuxAppDir $Target $app
        Remove-MuxPath "$dir.new"
        if (Test-Path -LiteralPath $dir) {
            Remove-MuxPath $dir
            Write-MuxInfo "Removed $dir"
        } else {
            Write-MuxInfo "$dir is not installed."
        }
        $shortcut = Get-MuxShortcutPath $Target $app
        if (Test-Path -LiteralPath $shortcut) {
            Remove-MuxPath $shortcut
            Write-MuxInfo "Removed $shortcut"
        }
    }
    Remove-MuxEmptyDir $Target.ShortcutDir
    Remove-MuxEmptyDir $Target.AppRoot
}

function Remove-MuxApps {
    $plan = Get-MuxPlan
    Assert-MuxNotRunning $plan.Targets
    foreach ($target in $plan.Targets) { Remove-MuxInstall $target }
    if ($plan.SystemSkipped) {
        $left = @(Get-MuxInstalledItems $plan.System)
        if ($left.Count -gt 0) {
            if ($plan.SystemSkipped -eq 'disabled') {
                $why = 'MUX_SYSTEM_INSTALL=0 is set'
            } else {
                $why = 'run from an elevated prompt to remove it'
            }
            Write-MuxWarning ("Left the system-wide install in place ($why): " + ($left -join ', '))
        }
    }
}

# Entry point; returns the process exit code.
function Invoke-MuxApps([string]$Action, [string]$Framework, [string]$RepoRoot) {
    try {
        switch ($Action) {
            'Check' { $null = Assert-MuxNotRunning (Get-MuxPlan).Targets }
            'Install' { $null = Install-MuxApps $RepoRoot $Framework }
            'Remove' { $null = Remove-MuxApps }
            'InstallUserAgent' { $null = Install-MuxUserAgent $RepoRoot $Framework }
            default { throw "Unknown action: '$Action'. Use -Action Check, Install, Remove or InstallUserAgent." }
        }
        return 0
    } catch {
        [Console]::Error.WriteLine('Error: ' + $_.Exception.Message)
        return 1
    }
}

# Dot-sourcing (tests) only defines the functions.
if ($MyInvocation.InvocationName -ne '.') {
    $repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
    exit (Invoke-MuxApps -Action $Action -Framework $Framework -RepoRoot $repoRoot)
}
