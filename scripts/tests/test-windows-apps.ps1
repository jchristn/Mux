<#
Exercise scripts/windows/mux-apps.ps1 (the desktop app and tray agent install helper used by the Windows
install-tool, reinstall-tool and remove-tool scripts) with its Windows-only calls replaced by fakes.

Run with: pwsh -NoProfile -File scripts/tests/test-windows-apps.ps1   (any OS; Windows PowerShell 5.1 too)

Covers parsing of every scripts/**/*.ps1, install-path resolution, the elevated / non-elevated /
MUX_SYSTEM_INSTALL=0 branches, the running-process guard, RID selection, publish staging, install
replacement and removal, the per-user agent-only install used by run-at-startup.bat, the mux-version.txt
marker, and EnsureUserAgent (run-desktop.bat and run-tui.bat: install or refresh the agent only when it is
missing or stale, never under a running copy, and exit 2 when MUX_AGENT_AUTOSTART turns automatic starts off;
with -Explicit, for run-agent.bat: MUX_AGENT_AUTOSTART is ignored and a running stale agent exits 3). Nothing real is
built or installed: dotnet, elevation, the process list, shortcut creation and the console messages are
fakes, and every path lives under a temporary directory. The .bat callers are not run here.
#>
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$script:count = 0
function Check([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw "FAILED: $Message" }
    $script:count++
}

# Every PowerShell script in scripts/ parses.
foreach ($file in @(Get-ChildItem -LiteralPath (Join-Path $root 'scripts') -Recurse -Filter '*.ps1')) {
    $tokens = $null; $errors = $null
    [void][System.Management.Automation.Language.Parser]::ParseFile($file.FullName, [ref]$tokens, [ref]$errors)
    Check ($errors.Count -eq 0) "$($file.Name) parses: $($errors | ForEach-Object { $_.ToString() })"
}

$temp = Join-Path ([IO.Path]::GetTempPath()) ('mux windows ' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temp | Out-Null
$savedEnv = @{}
foreach ($name in 'LOCALAPPDATA', 'APPDATA', 'ProgramFiles', 'ProgramW6432', 'ProgramData', 'MUX_SYSTEM_INSTALL',
    'PROCESSOR_ARCHITECTURE', 'PROCESSOR_ARCHITEW6432', 'MUX_AGENT_AUTOSTART', 'MUX_REFRESH_AGENT') {
    $savedEnv[$name] = [Environment]::GetEnvironmentVariable($name)
}
function Set-Env([string]$Name, [string]$Value) { [Environment]::SetEnvironmentVariable($Name, $Value) }
$savedError = [Console]::Error

try {
    . (Join-Path $root 'scripts/windows/mux-apps.ps1')
    function J { [IO.Path]::Combine([string[]]$args) }

    # A fake repository with the two projects and the icon.
    $repo = J $temp 'repo'
    foreach ($exe in 'Mux.Desktop', 'Mux.Agent') {
        New-Item -ItemType Directory -Path (J $repo 'src' $exe) -Force | Out-Null
        Set-Content -LiteralPath (J $repo 'src' $exe "$exe.csproj") -Value '<Project><PropertyGroup><Version>1.2.3</Version></PropertyGroup></Project>'
    }
    New-Item -ItemType Directory -Path (J $repo 'assets' 'icons') -Force | Out-Null
    Copy-Item -LiteralPath (J $root 'assets' 'icons' 'icon-green.ico') -Destination (J $repo 'assets' 'icons' 'icon-green.ico')

    Set-Env 'LOCALAPPDATA' (J $temp 'user' 'AppData' 'Local')
    Set-Env 'APPDATA' (J $temp 'user' 'AppData' 'Roaming')
    Set-Env 'ProgramFiles' (J $temp 'Program Files (x86)')
    Set-Env 'ProgramW6432' (J $temp 'Program Files')
    Set-Env 'ProgramData' (J $temp 'ProgramData')
    Set-Env 'MUX_SYSTEM_INSTALL' $null
    Set-Env 'PROCESSOR_ARCHITECTURE' 'AMD64'
    Set-Env 'PROCESSOR_ARCHITEW6432' $null
    Set-Env 'MUX_AGENT_AUTOSTART' $null
    Set-Env 'MUX_REFRESH_AGENT' $null

    # Fakes for the Windows-only calls.
    $script:MockElevated = $false
    $script:MockProcesses = @()
    $script:MockPublishFail = $false
    $script:Calls = New-Object System.Collections.ArrayList
    $script:Warnings = New-Object System.Collections.ArrayList
    $script:Shortcuts = New-Object System.Collections.ArrayList
    function Test-MuxElevated { return $script:MockElevated }
    function Get-MuxProcesses { return $script:MockProcesses }
    function Write-MuxInfo([string]$Message) { }
    function Write-MuxWarning([string]$Message) { [void]$script:Warnings.Add($Message) }
    function New-MuxShortcut([string]$Path, [string]$TargetPath, [string]$WorkingDirectory, [string]$IconPath, [string]$Description) {
        [void]$script:Shortcuts.Add([pscustomobject]@{ Path = $Path; Target = $TargetPath; WorkingDirectory = $WorkingDirectory; Icon = $IconPath; Description = $Description })
        Set-Content -LiteralPath $Path -Value 'lnk'
    }
    function Invoke-MuxDotnet([string[]]$Arguments) {
        [void]$script:Calls.Add($Arguments)
        if ($script:MockPublishFail) { return 7 }
        $out = $Arguments[[array]::IndexOf($Arguments, '--output') + 1]
        New-Item -ItemType Directory -Path (J $out '.playwright') -Force | Out-Null
        Set-Content -LiteralPath (J $out ([IO.Path]::GetFileNameWithoutExtension($Arguments[1]) + '.exe')) -Value 'fake exe'
        Set-Content -LiteralPath (J $out '.playwright' 'driver') -Value 'driver'
        return 0
    }
    function Reset-Mocks([bool]$Elevated = $false, [string]$System = $null) {
        $script:MockElevated = $Elevated
        $script:MockProcesses = @()
        $script:MockPublishFail = $false
        $script:Calls.Clear(); $script:Warnings.Clear(); $script:Shortcuts.Clear()
        Set-Env 'MUX_SYSTEM_INSTALL' $System
    }
    # Run an action and return its exit code and stderr.
    function Run([string]$Action, [string]$Framework = 'net10.0', [switch]$Explicit) {
        $writer = New-Object IO.StringWriter
        [Console]::SetError($writer)
        try { $code = Invoke-MuxApps -Action $Action -Framework $Framework -RepoRoot $repo -Explicit:$Explicit } finally { [Console]::SetError($savedError) }
        return [pscustomobject]@{ Code = $code; Err = $writer.ToString() }
    }
    function Running([string]$Path) { $script:MockProcesses = @([pscustomobject]@{ Name = 'x'; Id = 42; Path = $Path }) }

    $userRoot = J $temp 'user' 'AppData' 'Local' 'Programs' 'Mux'
    $userLinks = J $temp 'user' 'AppData' 'Roaming' 'Microsoft' 'Windows' 'Start Menu' 'Programs' 'Mux'
    $systemRoot = J $temp 'Program Files' 'Mux'
    $systemLinks = J $temp 'ProgramData' 'Microsoft' 'Windows' 'Start Menu' 'Programs' 'Mux'

    # Install paths.
    $targets = Get-MuxInstallTargets
    Check ($targets[0].AppRoot -eq $userRoot) "user root: $($targets[0].AppRoot)"
    Check ($targets[0].ShortcutDir -eq $userLinks) "user shortcuts: $($targets[0].ShortcutDir)"
    Check ($targets[1].AppRoot -eq $systemRoot) "ProgramW6432 wins over a 32-bit ProgramFiles: $($targets[1].AppRoot)"
    Check ($targets[1].ShortcutDir -eq $systemLinks) "system shortcuts: $($targets[1].ShortcutDir)"
    Check ((Get-MuxAppDir $targets[0] $script:MuxApps[0]) -eq (J $userRoot 'Desktop')) 'desktop folder'
    Check ((Get-MuxAppDir $targets[1] $script:MuxApps[1]) -eq (J $systemRoot 'Agent')) 'agent folder'
    Check ((Get-MuxShortcutPath $targets[0] $script:MuxApps[1]) -eq (J $userLinks 'Mux Agent.lnk')) 'agent shortcut name'
    Set-Env 'ProgramW6432' $null
    Check ((Get-MuxInstallTargets)[1].AppRoot -eq (J $temp 'Program Files (x86)' 'Mux')) 'ProgramFiles without ProgramW6432'
    Set-Env 'ProgramW6432' (J $temp 'Program Files')

    # Elevated / non-elevated / MUX_SYSTEM_INSTALL=0.
    Reset-Mocks -Elevated $true
    $plan = Get-MuxPlan
    Check (@($plan.Targets).Count -eq 2 -and -not $plan.SystemSkipped) 'elevated: both targets'
    Reset-Mocks -Elevated $false
    $plan = Get-MuxPlan
    Check (@($plan.Targets).Count -eq 1 -and $plan.Targets[0].Scope -eq 'user' -and $plan.SystemSkipped -eq 'not-elevated') 'not elevated: user only'
    Reset-Mocks -Elevated $true -System '0'
    $plan = Get-MuxPlan
    Check (@($plan.Targets).Count -eq 1 -and $plan.SystemSkipped -eq 'disabled') 'MUX_SYSTEM_INSTALL=0 wins over elevation'
    Reset-Mocks -Elevated $true -System '1'
    Check (@((Get-MuxPlan).Targets).Count -eq 2) 'MUX_SYSTEM_INSTALL=1 installs system-wide'

    # Path matching.
    $desk = J $userRoot 'Desktop'
    Check (Test-MuxPathUnder (J $desk 'Mux.Desktop.exe') $desk) 'exe under folder'
    Check (Test-MuxPathUnder (J $desk '.playwright' 'node' 'node.exe') $desk) 'nested child process'
    Check (Test-MuxPathUnder ((J $desk 'Mux.Desktop.exe').ToUpperInvariant()) $desk) 'case-insensitive'
    Check (Test-MuxPathUnder $desk ($desk + [IO.Path]::DirectorySeparatorChar)) 'folder itself, trailing separator'
    Check (-not (Test-MuxPathUnder (J ($desk + '2') 'Mux.Desktop.exe') $desk)) 'sibling with the same prefix'
    Check (-not (Test-MuxPathUnder $null $desk)) 'process without a path'

    # Running-process guard.
    Reset-Mocks -Elevated $false
    Check ((Run 'Check').Code -eq 0) 'check passes with nothing running'
    Running (J $userRoot 'Agent' 'Mux.Agent.exe')
    $r = Run 'Check'
    Check ($r.Code -eq 1 -and $r.Err -match 'Error: Mux Agent is running\. Quit it \(or run scripts\\windows\\kill-mux\.bat\) and try again\.') "agent running: $($r.Err)"
    Running (J $systemRoot 'Desktop' 'Mux.Desktop.exe')
    Check ((Run 'Check').Code -eq 0) 'not elevated: a running system copy is not replaced, so it does not block'
    $script:MockElevated = $true
    $r = Run 'Check'
    Check ($r.Code -eq 1 -and $r.Err -match 'Mux is running') "elevated: running system copy blocks: $($r.Err)"
    Set-Env 'MUX_SYSTEM_INSTALL' '0'
    Check ((Run 'Check').Code -eq 0) 'MUX_SYSTEM_INSTALL=0: system copy ignored'
    Set-Env 'MUX_SYSTEM_INSTALL' $null
    Running (J $temp 'elsewhere' 'Mux.Desktop.exe')
    Check ((Run 'Check').Code -eq 0) 'a source build elsewhere does not block'

    # RID.
    Check ((Get-MuxRid) -eq 'win-x64') 'AMD64'
    Set-Env 'PROCESSOR_ARCHITECTURE' 'ARM64'
    Check ((Get-MuxRid) -eq 'win-arm64') 'ARM64'
    Set-Env 'PROCESSOR_ARCHITECTURE' 'x86'
    Set-Env 'PROCESSOR_ARCHITEW6432' 'AMD64'
    Check ((Get-MuxRid) -eq 'win-x64') '32-bit process on 64-bit Windows'
    Set-Env 'PROCESSOR_ARCHITEW6432' $null
    $threw = $false
    try { Get-MuxRid | Out-Null } catch { $threw = $_.Exception.Message -match 'Unsupported Windows architecture: x86' }
    Check $threw 'x86 is unsupported'
    Set-Env 'PROCESSOR_ARCHITECTURE' 'AMD64'

    function Assert-Installed($Root, $Links, [string]$Label) {
        foreach ($pair in @(@('Desktop', 'Mux.Desktop', 'Mux'), @('Agent', 'Mux.Agent', 'Mux Agent'))) {
            $dir = J $Root $pair[0]
            Check (Test-Path -LiteralPath (J $dir "$($pair[1]).exe") -PathType Leaf) "$Label $($pair[1]).exe"
            Check (Test-Path -LiteralPath (J $dir 'mux.ico') -PathType Leaf) "$Label $($pair[0]) icon"
            Check ((Get-Content -LiteralPath (J $dir '.playwright' 'driver')) -eq 'driver') "$Label $($pair[0]) .playwright copied"
            Check (-not (Test-Path -LiteralPath "$dir.new")) "$Label no leftover $($pair[0]).new"
            Check ((Get-MuxInstalledVersion $dir) -eq '1.2.3') "$Label $($pair[0]) mux-version.txt"
            $link = @($script:Shortcuts | Where-Object { $_.Path -eq (J $Links "$($pair[2]).lnk") })
            Check ($link.Count -eq 1) "$Label shortcut $($pair[2]).lnk"
            Check ($link[0].Target -eq (J $dir "$($pair[1]).exe") -and $link[0].WorkingDirectory -eq $dir -and $link[0].Icon -eq (J $dir 'mux.ico')) "$Label shortcut fields"
        }
    }

    # Install, elevated: one publish per app, both targets.
    Reset-Mocks -Elevated $true
    $r = Run 'Install' 'net8.0'
    Check ($r.Code -eq 0) "elevated install: $($r.Err)"
    Check ($script:Calls.Count -eq 2) "two publishes: $($script:Calls.Count)"
    foreach ($pair in @(@(0, 'Mux.Desktop', 'desktop'), @(1, 'Mux.Agent', 'agent'))) {
        $call = $script:Calls[$pair[0]]
        Check ($call[0] -eq 'publish' -and $call[1] -eq (J $repo 'src' $pair[1] "$($pair[1]).csproj")) "publish project $($pair[1])"
        Check ($call[[array]::IndexOf($call, '--framework') + 1] -eq 'net8.0' -and $call -contains '-p:TargetFrameworks=net8.0') 'framework forwarded'
        Check ($call[[array]::IndexOf($call, '--runtime') + 1] -eq 'win-x64') 'rid'
        Check ($call[[array]::IndexOf($call, '--self-contained') + 1] -eq 'true') 'self-contained'
        Check ($call[[array]::IndexOf($call, '--output') + 1] -eq (J $repo 'artifacts' 'windows-app' 'win-x64' 'net8.0' $pair[2])) 'stage folder'
    }
    Assert-Installed $userRoot $userLinks 'user'
    Assert-Installed $systemRoot $systemLinks 'system'
    Check ($script:Shortcuts.Count -eq 4 -and $script:Warnings.Count -eq 0) 'four shortcuts, no warning'

    # Reinstall over an existing copy: stale files go.
    Set-Content -LiteralPath (J $userRoot 'Desktop' 'stale.dll') -Value 'old'
    Reset-Mocks -Elevated $false
    $r = Run 'Install'
    Check ($r.Code -eq 0) "non-elevated install: $($r.Err)"
    Check (-not (Test-Path -LiteralPath (J $userRoot 'Desktop' 'stale.dll'))) 'old copy replaced'
    Check ($script:Shortcuts.Count -eq 2) 'non-elevated: user shortcuts only'
    Check ($script:Warnings.Count -eq 1 -and $script:Warnings[0] -match '^Skipped the system-wide install to .*; the per-user install is done\. Run from an elevated prompt to also install for all users, or set MUX_SYSTEM_INSTALL=0 to skip it quietly\.$') "non-elevated warning: $($script:Warnings)"
    Reset-Mocks -Elevated $false -System '0'
    Check ((Run 'Install').Code -eq 0 -and $script:Warnings.Count -eq 0) 'MUX_SYSTEM_INSTALL=0: no warning'

    # A failed publish leaves the installed copies alone.
    Set-Content -LiteralPath (J $userRoot 'Desktop' 'keep.dll') -Value 'installed'
    Reset-Mocks -Elevated $true
    $script:MockPublishFail = $true
    $r = Run 'Install'
    Check ($r.Code -eq 1 -and $r.Err -match 'Error: Publishing Mux failed \(dotnet exit code 7\)\.') "publish failure: $($r.Err)"
    Check ($script:Calls.Count -eq 1 -and $script:Shortcuts.Count -eq 0) 'stops after the first failed publish'
    Check (Test-Path -LiteralPath (J $userRoot 'Desktop' 'keep.dll')) 'installed copy untouched after a failed publish'

    # A running app blocks the install before anything is built.
    Reset-Mocks -Elevated $true
    Running (J $systemRoot 'Agent' 'Mux.Agent.exe')
    $r = Run 'Install'
    Check ($r.Code -eq 1 -and $r.Err -match 'Mux Agent is running' -and $script:Calls.Count -eq 0) "running blocks install: $($r.Err)"
    Check ((Run 'Install' '').Code -eq 1) 'install without a framework fails'

    # InstallUserAgent (run-at-startup.bat): the agent only, per-user only, even when elevated.
    Reset-Mocks -Elevated $true
    Check ((Run 'Remove').Code -eq 0) 'clean slate for InstallUserAgent'
    $r = Run 'InstallUserAgent' 'net8.0'
    Check ($r.Code -eq 0) "user agent install: $($r.Err)"
    Check ($script:Calls.Count -eq 1 -and $script:Calls[0][1] -eq (J $repo 'src' 'Mux.Agent' 'Mux.Agent.csproj')) 'one publish, the agent'
    Check ($script:Calls[0][[array]::IndexOf($script:Calls[0], '--framework') + 1] -eq 'net8.0') 'user agent framework'
    Check ($script:Calls[0][[array]::IndexOf($script:Calls[0], '--output') + 1] -eq (J $repo 'artifacts' 'windows-app' 'win-x64' 'net8.0' 'agent')) 'user agent stage'
    Check (Test-Path -LiteralPath (J $userRoot 'Agent' 'Mux.Agent.exe') -PathType Leaf) 'user agent installed'
    Check (-not (Test-Path -LiteralPath (J $userRoot 'Desktop'))) 'desktop app not installed'
    Check (-not (Test-Path -LiteralPath $systemRoot) -and -not (Test-Path -LiteralPath $systemLinks)) 'nothing system-wide'
    Check ($script:Shortcuts.Count -eq 1 -and $script:Shortcuts[0].Path -eq (J $userLinks 'Mux Agent.lnk')) 'user agent shortcut only'
    Check ($script:Warnings.Count -eq 0) 'no system-wide warning'
    Reset-Mocks -Elevated $false
    Running (J $userRoot 'Agent' 'Mux.Agent.exe')
    $r = Run 'InstallUserAgent'
    Check ($r.Code -eq 1 -and $r.Err -match 'Error: Mux Agent is running\. Quit it' -and $script:Calls.Count -eq 0) "running user agent blocks: $($r.Err)"
    Running (J $userRoot 'Desktop' 'Mux.Desktop.exe')
    Check ((Run 'InstallUserAgent').Code -eq 0) 'a running desktop app does not block the agent install'
    Reset-Mocks -Elevated $true
    Running (J $systemRoot 'Agent' 'Mux.Agent.exe')
    Check ((Run 'InstallUserAgent').Code -eq 0) 'a running system agent is not replaced, so it does not block'
    Reset-Mocks -Elevated $false
    $script:MockPublishFail = $true
    Set-Content -LiteralPath (J $userRoot 'Agent' 'keep.dll') -Value 'installed'
    Check ((Run 'InstallUserAgent').Code -eq 1 -and (Test-Path -LiteralPath (J $userRoot 'Agent' 'keep.dll'))) 'failed publish keeps the installed agent'
    Check ((Run 'InstallUserAgent' '').Code -eq 1) 'InstallUserAgent without a framework fails'

    # EnsureUserAgent (run-desktop.bat, run-tui.bat): install or refresh only when needed.
    foreach ($pair in @(@($null, $true), @('', $true), @('1', $true), @('yes', $true), @('0', $false), @(' Off ', $false),
            @('FALSE', $false), @('no', $false))) {
        Check ((Test-MuxAutostartEnabled $pair[0]) -eq $pair[1]) "autostart '$($pair[0])'"
    }
    Check ((Get-MuxProjectVersion (J $repo 'src' 'Mux.Agent' 'Mux.Agent.csproj')) -eq '1.2.3') 'project version'
    Check ($null -eq (Get-MuxProjectVersion (J $temp 'missing.csproj'))) 'missing project has no version'
    $agentDir = J $userRoot 'Agent'
    $agentMarker = J $agentDir 'mux-version.txt'
    Reset-Mocks -Elevated $true
    Check ((Run 'Remove').Code -eq 0) 'clean slate for EnsureUserAgent'
    $r = Run 'EnsureUserAgent' 'net8.0'
    Check ($r.Code -eq 0 -and $script:Calls.Count -eq 1 -and $script:Calls[0][1] -eq (J $repo 'src' 'Mux.Agent' 'Mux.Agent.csproj')) "missing agent installed: $($r.Err)"
    Check ((Get-MuxInstalledVersion $agentDir) -eq '1.2.3') 'marker written'
    Check (-not (Test-Path -LiteralPath (J $userRoot 'Desktop')) -and -not (Test-Path -LiteralPath $systemRoot)) 'agent only, per-user only'
    Reset-Mocks
    $r = Run 'EnsureUserAgent'
    Check ($r.Code -eq 0 -and $script:Calls.Count -eq 0 -and $script:Warnings.Count -eq 0) 'current agent: no rebuild'
    Set-Content -LiteralPath $agentMarker -Value '0.0.1'
    $r = Run 'EnsureUserAgent'
    Check ($r.Code -eq 0 -and $script:Calls.Count -eq 1 -and (Get-MuxInstalledVersion $agentDir) -eq '1.2.3') 'stale agent refreshed'
    Reset-Mocks
    Remove-Item -LiteralPath $agentMarker
    Check ((Run 'EnsureUserAgent').Code -eq 0 -and $script:Calls.Count -eq 1) 'agent without a marker refreshed'
    Reset-Mocks
    Set-Env 'MUX_REFRESH_AGENT' '1'
    Check ((Run 'EnsureUserAgent').Code -eq 0 -and $script:Calls.Count -eq 1) 'MUX_REFRESH_AGENT=1 forces a refresh'
    Set-Env 'MUX_REFRESH_AGENT' $null
    # Running from the per-user folder: never replaced; a stale one gets one note naming kill-mux.
    Reset-Mocks
    Set-Content -LiteralPath $agentMarker -Value '0.0.1'
    Running (J $agentDir 'Mux.Agent.exe')
    $r = Run 'EnsureUserAgent'
    Check ($r.Code -eq 0 -and $script:Calls.Count -eq 0 -and (Get-MuxInstalledVersion $agentDir) -eq '0.0.1') "running stale agent not replaced: $($r.Err)"
    Check ($script:Warnings.Count -eq 1 -and $script:Warnings[0] -match 'older version \(0\.0\.1, this checkout is 1\.2\.3\)\. Quit it \(or run scripts\\windows\\kill-mux\.bat\) to update it\.') "stale note: $($script:Warnings)"
    Reset-Mocks
    Set-Content -LiteralPath $agentMarker -Value '1.2.3'
    Running (J $agentDir 'Mux.Agent.exe')
    Check ((Run 'EnsureUserAgent').Code -eq 0 -and $script:Calls.Count -eq 0 -and $script:Warnings.Count -eq 0) 'running current agent: nothing to do'
    Set-Env 'MUX_REFRESH_AGENT' '1'
    Check ((Run 'EnsureUserAgent').Code -eq 0 -and $script:Calls.Count -eq 0 -and $script:Warnings[0] -match 'cannot refresh') 'running agent: MUX_REFRESH_AGENT=1 only notes it'
    Set-Env 'MUX_REFRESH_AGENT' $null
    # An agent running from elsewhere does not lock the per-user copy, so a stale one is refreshed.
    Reset-Mocks
    Set-Content -LiteralPath $agentMarker -Value '0.0.1'
    Running (J $temp 'elsewhere' 'Mux.Agent.exe')
    Check ((Run 'EnsureUserAgent').Code -eq 0 -and $script:Calls.Count -eq 1) 'agent running elsewhere: per-user copy refreshed'
    # MUX_AGENT_AUTOSTART=0 / off: nothing is installed and the caller is told (exit 2) not to start it.
    Reset-Mocks
    Check ((Run 'Remove').Code -eq 0) 'clean slate for MUX_AGENT_AUTOSTART'
    foreach ($off in '0', 'OFF') {
        Set-Env 'MUX_AGENT_AUTOSTART' $off
        $r = Run 'EnsureUserAgent'
        Check ($r.Code -eq 2 -and $script:Calls.Count -eq 0 -and -not (Test-Path -LiteralPath $agentDir)) "MUX_AGENT_AUTOSTART=$($off): exit 2, nothing installed"
    }
    Set-Env 'MUX_AGENT_AUTOSTART' $null
    $script:MockPublishFail = $true
    $r = Run 'EnsureUserAgent'
    Check ($r.Code -eq 1 -and $r.Err -match 'Error: Publishing Mux Agent failed') "failed agent install fails: $($r.Err)"
    $script:MockPublishFail = $false
    Check ((Run 'EnsureUserAgent' '').Code -eq 1) 'EnsureUserAgent without a framework fails'

    # EnsureUserAgent -Explicit (run-agent.bat): the same install-or-refresh, but MUX_AGENT_AUTOSTART is ignored
    # (never exit 2), and a stale agent running from the per-user folder exits 3 after its note.
    Reset-Mocks
    Check ((Run 'Remove').Code -eq 0) 'clean slate for EnsureUserAgent -Explicit'
    $r = Run 'EnsureUserAgent' 'net8.0' -Explicit
    Check ($r.Code -eq 0 -and $script:Calls.Count -eq 1 -and (Get-MuxInstalledVersion $agentDir) -eq '1.2.3') "explicit: missing agent installed: $($r.Err)"
    Check (-not (Test-Path -LiteralPath (J $userRoot 'Desktop')) -and -not (Test-Path -LiteralPath $systemRoot)) 'explicit: agent only, per-user only'
    Reset-Mocks
    Check ((Run 'EnsureUserAgent' -Explicit).Code -eq 0 -and $script:Calls.Count -eq 0) 'explicit: current agent, no rebuild'
    Set-Content -LiteralPath $agentMarker -Value '0.0.1'
    Check ((Run 'EnsureUserAgent' -Explicit).Code -eq 0 -and $script:Calls.Count -eq 1 -and (Get-MuxInstalledVersion $agentDir) -eq '1.2.3') 'explicit: stale agent refreshed'
    Reset-Mocks
    Set-Env 'MUX_REFRESH_AGENT' '1'
    Check ((Run 'EnsureUserAgent' -Explicit).Code -eq 0 -and $script:Calls.Count -eq 1) 'explicit: MUX_REFRESH_AGENT=1 rebuilds'
    Set-Env 'MUX_REFRESH_AGENT' $null
    Reset-Mocks
    Running (J $agentDir 'Mux.Agent.exe')
    $r = Run 'EnsureUserAgent' -Explicit
    Check ($r.Code -eq 0 -and $script:Calls.Count -eq 0 -and $script:Warnings.Count -eq 0) "explicit: running current agent, exit 0, no rebuild: $($r.Err)"
    Set-Content -LiteralPath $agentMarker -Value '0.0.1'
    $r = Run 'EnsureUserAgent' -Explicit
    Check ($r.Code -eq 3 -and $script:Calls.Count -eq 0 -and (Get-MuxInstalledVersion $agentDir) -eq '0.0.1') "explicit: running stale agent exits 3, not replaced: $($r.Err)"
    Check ($script:Warnings.Count -eq 1 -and $script:Warnings[0] -match 'older version \(0\.0\.1, this checkout is 1\.2\.3\)\. Quit it') "explicit: stale note: $($script:Warnings)"
    Reset-Mocks
    Check ((Run 'Remove').Code -eq 0) 'clean slate for explicit MUX_AGENT_AUTOSTART=0'
    foreach ($off in '0', 'OFF') {
        Reset-Mocks
        Set-Env 'MUX_AGENT_AUTOSTART' $off
        $r = Run 'EnsureUserAgent' -Explicit
        Check ($r.Code -eq 0 -and $script:Calls.Count -eq 1 -and (Test-Path -LiteralPath (J $agentDir 'Mux.Agent.exe'))) "explicit: MUX_AGENT_AUTOSTART=$($off) ignored, agent installed: $($r.Err)"
        Remove-Item -LiteralPath $agentDir -Recurse -Force
    }
    Set-Env 'MUX_AGENT_AUTOSTART' $null
    Reset-Mocks
    $script:MockPublishFail = $true
    Check ((Run 'EnsureUserAgent' -Explicit).Code -eq 1) 'explicit: failed agent install fails'
    $script:MockPublishFail = $false

    # Remove, not elevated: user copy goes, the system copy is named in a warning.
    Reset-Mocks -Elevated $true
    Check ((Run 'Install').Code -eq 0) 'reinstall both for the remove tests'
    Reset-Mocks -Elevated $false
    Running (J $userRoot 'Desktop' 'Mux.Desktop.exe')
    $r = Run 'Remove'
    Check ($r.Code -eq 1 -and $r.Err -match 'Mux is running' -and (Test-Path -LiteralPath (J $userRoot 'Desktop'))) 'running blocks remove'
    Reset-Mocks -Elevated $false
    Check ((Run 'Remove').Code -eq 0) 'non-elevated remove'
    Check (-not (Test-Path -LiteralPath $userRoot) -and -not (Test-Path -LiteralPath $userLinks)) 'user install and shortcut folder removed'
    Check (Test-Path -LiteralPath (J $systemRoot 'Agent' 'Mux.Agent.exe')) 'system copy left'
    Check ($script:Warnings.Count -eq 1 -and $script:Warnings[0] -match 'elevated prompt' -and
        $script:Warnings[0].Contains((J $systemRoot 'Desktop')) -and $script:Warnings[0].Contains((J $systemLinks 'Mux Agent.lnk'))) "remove warning names what is left: $($script:Warnings)"
    Reset-Mocks -Elevated $true -System '0'
    Check ((Run 'Remove').Code -eq 0 -and $script:Warnings[0] -match 'MUX_SYSTEM_INSTALL=0') 'disabled remove mentions the setting'
    Check (Test-Path -LiteralPath (J $systemRoot 'Agent')) 'disabled remove leaves the system copy'
    Reset-Mocks -Elevated $true
    New-Item -ItemType Directory -Path (J $systemRoot 'Desktop.new') -Force | Out-Null
    Check ((Run 'Remove').Code -eq 0 -and $script:Warnings.Count -eq 0) 'elevated remove'
    Check (-not (Test-Path -LiteralPath $systemRoot) -and -not (Test-Path -LiteralPath $systemLinks)) 'system install, leftovers and shortcuts removed'
    Check ((Run 'Remove').Code -eq 0) 'remove with nothing installed succeeds'
    Check ((Run 'Bogus').Code -eq 1) 'unknown action fails'

    # The script entry point, as the .bat files call it (Windows-only calls are not faked here: on a
    # non-Windows host elevation reads as false and Get-Process has no installed mux).
    # (Get-Process -Id $PID).Path is dotnet when pwsh runs as a .NET global tool, so go by name.
    if ($PSVersionTable.PSEdition -eq 'Desktop') { $exe = 'powershell' } else { $exe = 'pwsh' }
    $helper = Join-Path $root 'scripts/windows/mux-apps.ps1'
    $out = & $exe -NoProfile -File $helper -Action Bogus 2>&1
    Check ($LASTEXITCODE -eq 1 -and ($out -join "`n") -match "Error: Unknown action: 'Bogus'") "entry point error: $out"
    $out = & $exe -NoProfile -File $helper -Action Check 2>&1
    Check ($LASTEXITCODE -eq 0) "entry point check: $out"
    # The .bat callers tell "automatic starts are off" (2) from a failure (1) by the exit code.
    Set-Env 'MUX_AGENT_AUTOSTART' '0'
    $out = & $exe -NoProfile -File $helper -Action EnsureUserAgent -Framework net8.0 2>&1
    Check ($LASTEXITCODE -eq 2) "entry point EnsureUserAgent with MUX_AGENT_AUTOSTART=0 exits 2: $LASTEXITCODE $out"
    # -Explicit reaches EnsureUserAgent through the entry point: MUX_AGENT_AUTOSTART=0 no longer exits 2 (the
    # missing framework fails first, so nothing is built).
    $out = & $exe -NoProfile -File $helper -Action EnsureUserAgent -Framework '' -Explicit 2>&1
    Check ($LASTEXITCODE -eq 1 -and ($out -join "`n") -match 'EnsureUserAgent needs -Framework') "entry point EnsureUserAgent -Explicit ignores MUX_AGENT_AUTOSTART: $LASTEXITCODE $out"
    Set-Env 'MUX_AGENT_AUTOSTART' $null
} finally {
    [Console]::SetError($savedError)
    foreach ($name in $savedEnv.Keys) { Set-Env $name $savedEnv[$name] }
    Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue
}
Write-Output "OK: $script:count checks passed."
