namespace Mux.Core.Skills
{
    using System.Collections.Generic;

    /// <summary>
    /// Runtime diagnosis skills: <c>log-triage</c> groups the errors in a log file, <c>port-inspect</c> shows what is
    /// listening and which process owns it, and <c>bench</c> times commands (hyperfine, or an in-process loop), runs
    /// BenchmarkDotNet projects, or runs a k6 load script. All read-only except what the benchmarked command does.
    /// </summary>
    public static class DefaultRuntimeSkills
    {
        #region Private-Members

        private const string LogTriageCode = @"$file = Get-MuxArg -Arguments $args -Index 0
if (-not $file) { Exit-MuxNotApplicable 'pass the log file: log-triage summarize <file> [--top n]' }
if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { Exit-MuxNotApplicable ('log file not found: ' + $file) }
$top = 15
$all = @($args)
for ($i = 1; $i -lt $all.Count; $i++) {
    if ([string]$all[$i] -eq '--top') { if (($i + 1) -ge $all.Count) { Exit-MuxNotApplicable '--top needs a number.' }; $top = Get-MuxBoundedInt ([string]$all[$i + 1]) 1 200 '--top'; $i++; continue }
    Exit-MuxNotApplicable ('unknown argument ' + $all[$i] + '. Use --top <n>.')
}
$errorPattern = [regex]'(?i)(\b(error|exception|fatal|panic|critical|crit|unhandled|traceback|failed|failure)\b|""level""\s*:\s*""(error|fatal|critical)""|\bERR\b)'
$framePattern = [regex]'^(\s+at\s|\s+File\s""|\tat\s|\s+\.\.\.\s\d+\smore|Caused by:|\s+raise\s|\s{4,}\S|goroutine\s\d+|\S+\.go:\d+)'
$masks = @(
    @('^\s*\[?\d{4}-\d{2}-\d{2}[T\s]\d{2}:\d{2}:\d{2}(\.\d+)?(Z|[+-]\d{2}:?\d{2})?\]?\s*', ''),
    @('^\s*\[?\w{3}\s+\d{1,2}\s\d{2}:\d{2}:\d{2}\]?\s*', ''),
    @('\b[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\b', '<guid>'),
    @('\b0x[0-9a-fA-F]+\b', '<hex>'),
    @('([A-Za-z]:)?([\\/][\w.\-@]+){2,}', '<path>'),
    @('\b\d+(\.\d+)*\b', '<n>'),
    @('''[^'']{1,200}''', '''<s>'''),
    @('""[^""]{1,200}""', '""<s>""'))
function Get-MuxLogKey {
    param([string]$Line)
    $key = $Line
    foreach ($mask in $masks) { $key = [regex]::Replace($key, $mask[0], $mask[1]) }
    $key = ($key -replace '\s+', ' ').Trim()
    if ($key.Length -gt 220) { $key = $key.Substring(0, 220) + '...' }
    return $key
}
$groups = [ordered]@{}
$headerPattern = [regex]'^\s*([A-Za-z_][\w.$]*\.)?[A-Z]\w*(Exception|Error|Exit|Interrupt|Panic)\b(:|$)'
function Get-MuxJsonLogText {
    param([string]$Line)
    try { $o = $Line | ConvertFrom-Json -ErrorAction Stop } catch { return $null }
    if ($o -isnot [pscustomobject]) { return $null }
    $level = @($o.level, $o.severity, $o.lvl) | Where-Object { $_ } | Select-Object -First 1
    $message = @($o.msg, $o.message, $o.error, $o.err) | Where-Object { $_ } | Select-Object -First 1
    if (-not $message) { return $null }
    return ([string]$level).ToUpperInvariant() + ' ' + [string]$message
}
function Add-MuxLogGroup {
    param([string]$KeyText, [string]$Sample, [int]$Line)
    $key = Get-MuxLogKey $KeyText
    if (-not $groups.Contains($key)) {
        $groups[$key] = [pscustomobject]@{ Key = $key; Count = 0; First = $Line; Last = $Line; Sample = $Sample.Trim(); Stack = (New-Object System.Collections.Generic.List[string]) }
        $script:current = $groups[$key]
    } else {
        $script:current = $null
    }
    $groups[$key].Count++
    $groups[$key].Last = $Line
}
$lines = [System.IO.File]::ReadLines((Resolve-Path -LiteralPath $file).Path)
$number = 0
$script:current = $null
$traceback = $null
$errorsSeen = 0
foreach ($line in $lines) {
    $number++
    if ($traceback) {
        if ($framePattern.IsMatch($line)) { if ($traceback.Frames.Count -lt 15) { $traceback.Frames.Add($line.TrimEnd()) }; continue }
        $errorsSeen++
        $closing = if ($headerPattern.IsMatch($line)) { $line } else { 'Traceback' }
        Add-MuxLogGroup $closing $closing $traceback.Line
        if ($script:current) { foreach ($frame in $traceback.Frames) { $script:current.Stack.Add($frame) } }
        $script:current = $null
        $traceback = $null
        if ($closing -ne 'Traceback') { continue }
    }
    if ($script:current -and $script:current.Stack.Count -lt 15 -and ($framePattern.IsMatch($line) -or $headerPattern.IsMatch($line))) { $script:current.Stack.Add($line.TrimEnd()); continue }
    $script:current = $null
    if ($line -match '^\s*Traceback \(most recent call last\)') { $traceback = @{ Line = $number; Frames = (New-Object System.Collections.Generic.List[string]) }; continue }
    $json = if ($line.TrimStart().StartsWith('{')) { Get-MuxJsonLogText $line } else { $null }
    if ($json) {
        if ($json -notmatch '^(ERROR|FATAL|CRITICAL|CRIT|PANIC)\b') { continue }
        $errorsSeen++
        Add-MuxLogGroup $json $line $number
        continue
    }
    if (-not ($errorPattern.IsMatch($line) -or $headerPattern.IsMatch($line))) { continue }
    $errorsSeen++
    Add-MuxLogGroup $line $line $number
}
if ($traceback) { $errorsSeen++; Add-MuxLogGroup 'Traceback' 'Traceback (most recent call last):' $traceback.Line; if ($script:current) { foreach ($frame in $traceback.Frames) { $script:current.Stack.Add($frame) } } }
Write-Output ('Read ' + $number + ' lines; ' + $errorsSeen + ' error lines in ' + $groups.Count + ' groups.')
if ($groups.Count -eq 0) { Write-Output 'No errors, exceptions, or failures found.'; exit 0 }
$shown = 0
foreach ($group in ($groups.Values | Sort-Object -Property @{ Expression = 'Count'; Descending = $true }, First)) {
    if ($shown -ge $top) { Write-Output ('[mux: ' + ($groups.Count - $top) + ' more groups; pass --top to see them]'); break }
    $shown++
    Write-Output ''
    Write-Output ([string]$group.Count + 'x  lines ' + $group.First + $(if ($group.Last -ne $group.First) { '..' + $group.Last }) + '  ' + $group.Key)
    $sample = if ($group.Sample.Length -gt 400) { $group.Sample.Substring(0, 400) + '...' } else { $group.Sample }
    Write-Output ('    first: ' + $sample)
    foreach ($frame in $group.Stack) { Write-Output ('    ' + $frame) }
}
exit 0
";

        private const string PortsCode = @"$filter = Get-MuxArg -Arguments $args -Index 0
if ($filter -and $filter -notmatch '^\d{1,5}$') { Exit-MuxNotApplicable ('pass a port number, got ' + $filter) }
$rows = New-Object System.Collections.Generic.List[object]
if ($IsWindows) {
    foreach ($c in @(Get-NetTCPConnection -State Listen -ErrorAction SilentlyContinue)) {
        $name = try { (Get-Process -Id $c.OwningProcess -ErrorAction Stop).ProcessName } catch { '?' }
        $rows.Add([pscustomobject]@{ Port = [int]$c.LocalPort; Pid = [string]$c.OwningProcess; Process = $name; Address = [string]$c.LocalAddress })
    }
} elseif (Test-MuxTool 'lsof') {
    foreach ($line in @(& lsof -nP -iTCP -sTCP:LISTEN 2>$null | Select-Object -Skip 1)) {
        $parts = ([string]$line) -split '\s+'
        if ($parts.Count -lt 9) { continue }
        $address = $parts[8]
        $port = ($address -split ':')[-1]
        if ($port -notmatch '^\d+$') { continue }
        $rows.Add([pscustomobject]@{ Port = [int]$port; Pid = $parts[1]; Process = $parts[0]; Address = $address })
    }
} elseif (Test-MuxTool 'ss') {
    foreach ($line in @(& ss -ltnpH 2>$null)) {
        $parts = ([string]$line).Trim() -split '\s+'
        if ($parts.Count -lt 4) { continue }
        $address = $parts[3]
        $port = ($address -split ':')[-1]
        $proc = if (([string]$line) -match 'users:\(\(""([^""]+)"",pid=(\d+)') { @($Matches[1], $Matches[2]) } else { @('?', '?') }
        if ($port -match '^\d+$') { $rows.Add([pscustomobject]@{ Port = [int]$port; Pid = $proc[1]; Process = $proc[0]; Address = $address }) }
    }
} else {
    Exit-MuxNotApplicable 'neither lsof nor ss is available to list listening ports.'
}
$unique = @($rows | Sort-Object Port, Pid, Address -Unique)
if ($filter) { $unique = @($unique | Where-Object { $_.Port -eq [int]$filter }) }
if ($unique.Count -eq 0) {
    if ($filter) { Write-Output ('Nothing is listening on port ' + $filter + '.') } else { Write-Output 'Nothing is listening on a TCP port.' }
    exit 0
}
Write-Output ('PORT'.PadRight(8) + 'PID'.PadRight(10) + 'PROCESS'.PadRight(24) + 'ADDRESS')
foreach ($row in $unique) { Write-Output (([string]$row.Port).PadRight(8) + ([string]$row.Pid).PadRight(10) + ([string]$row.Process).PadRight(24) + $row.Address) }
Write-Output 'Read-only: stopping a process is left to you (or process_stop for one mux started).'
exit 0
";

        private const string TimeCode = @"$all = @($args)
$runs = 10
$commands = New-Object System.Collections.Generic.List[string]
for ($i = 0; $i -lt $all.Count; $i++) {
    if ([string]$all[$i] -eq '--runs') { if (($i + 1) -ge $all.Count) { Exit-MuxNotApplicable '--runs needs a number.' }; $runs = Get-MuxBoundedInt ([string]$all[$i + 1]) 1 1000 '--runs'; $i++; continue }
    $commands.Add([string]$all[$i])
}
if ($commands.Count -eq 0) { Exit-MuxNotApplicable 'pass one command to time, or two to compare: bench time ""<command>"" [""<other command>""] [--runs n]' }
if ($commands.Count -gt 2) { Exit-MuxNotApplicable 'pass at most two commands (quote each one).' }
if (Test-MuxTool 'hyperfine') {
    Invoke-MuxTool -Tool 'hyperfine' -Arguments (@('--warmup', '1', '--runs', [string]$runs, '--style', 'basic') + $commands) -AllowFailure
    if ($script:MuxLastExit -ne 0) { exit 1 }
    exit 0
}
$shell = if ($IsWindows) { @('cmd.exe', '/c') } else { @('/bin/sh', '-c') }
if (Test-MuxDryRun) { foreach ($command in $commands) { Write-Output ('DRYRUN: time ' + $runs + ' runs of ' + $command + ' (hyperfine is not installed; timing in-process)') }; exit 0 }
Write-Output ('hyperfine is not installed, so each command runs ' + $runs + ' times through ' + $shell[0] + ' after one warmup run.')
$failed = $false
foreach ($command in $commands) {
    & $shell[0] $shell[1] $command *> $null
    $times = New-Object System.Collections.Generic.List[double]
    for ($r = 0; $r -lt $runs; $r++) {
        $watch = [System.Diagnostics.Stopwatch]::StartNew()
        & $shell[0] $shell[1] $command *> $null
        $watch.Stop()
        if ($LASTEXITCODE -ne 0) { $failed = $true }
        $times.Add($watch.Elapsed.TotalMilliseconds)
    }
    $mean = ($times | Measure-Object -Average).Average
    $sorted = @($times | Sort-Object)
    $median = $sorted[[int][Math]::Floor($sorted.Count / 2)]
    $deviation = [Math]::Sqrt((($times | ForEach-Object { ($_ - $mean) * ($_ - $mean) }) | Measure-Object -Sum).Sum / [Math]::Max(1, $times.Count))
    Write-Output ''
    Write-Output ('Command: ' + $command)
    Write-Output ('  mean ' + $mean.ToString('0.0') + ' ms +/- ' + $deviation.ToString('0.0') + ', median ' + $median.ToString('0.0') + ', min ' + $sorted[0].ToString('0.0') + ', max ' + $sorted[-1].ToString('0.0') + ' (' + $runs + ' runs)')
}
if ($failed) { Write-Output 'At least one run exited nonzero; the timings include failures.'; exit 1 }
exit 0
";

        private const string DotnetBenchCode = @"$filter = Get-MuxArg -Arguments $args -Index 0 -Default '*'
$root = Get-MuxRepoRoot
$project = Get-ChildItem -LiteralPath $root -Filter '*.csproj' -File -Recurse -Depth 4 -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -notmatch '[\\/](bin|obj|node_modules)[\\/]' -and (Select-String -LiteralPath $_.FullName -Pattern 'BenchmarkDotNet' -SimpleMatch -Quiet) } | Select-Object -First 1
if (-not $project) { Exit-MuxNotApplicable 'no project references BenchmarkDotNet.' }
$relative = [System.IO.Path]::GetRelativePath($root, $project.FullName).Replace('\', '/')
Set-Location -LiteralPath $root
Invoke-MuxTool -Tool 'dotnet' -Arguments @('run', '-c', 'Release', '--project', $relative, '--', '--filter', $filter) -InstallHint 'Install the .NET SDK.'
";

        private const string K6Code = @"$script = Get-MuxArg -Arguments $args -Index 0
if (-not $script) { Exit-MuxNotApplicable 'pass the k6 script: bench k6 <script.js>' }
if (-not (Test-MuxDryRun) -and -not (Test-Path -LiteralPath $script -PathType Leaf)) { Exit-MuxNotApplicable ('k6 script not found: ' + $script) }
Invoke-MuxTool -Tool 'k6' -Arguments @('run', '--quiet', $script) -InstallHint 'Install k6 (https://grafana.com/docs/k6/latest/set-up/install-k6/).'
";

        #endregion

        #region Public-Methods

        /// <summary>Returns the runtime diagnosis skill definitions.</summary>
        /// <returns>The definitions.</returns>
        public static IReadOnlyList<DefaultSkillDef> All()
        {
            ToolchainSkillFactory debugging = new ToolchainSkillFactory(string.Empty, new[] { "debugging" }, null, null);
            return new List<DefaultSkillDef>
            {
                debugging.Skill("log-triage", "Group the errors in a log file",
                    "Summarizes a log file's errors and exceptions: groups repeated messages (numbers, GUIDs, and paths masked), counts them, and shows the first occurrence with its stack trace.",
                    false,
                    "The user points at a log file or crash output and asks what went wrong, which errors repeat, or where an exception came from.",
                    "<file> [--top n]",
                    "`summarize <file>` reads the whole file and keeps lines that look like errors (error, exception, fatal, panic, failed, JSON entries at error level, and exception headers such as `System.NullReferenceException:` or `ValueError:`). Messages are grouped after masking timestamps, GUIDs, hex values, paths, numbers, and quoted strings, so the same failure with different ids counts once. Each group shows its count, first and last line numbers, the first raw line, and the stack frames that followed it (.NET, Java, Python tracebacks keyed by their final exception line, Node, and Go). The 15 largest groups are shown; `--top` changes that (1 to 200).",
                    ToolchainSkillFactory.Command("summarize", "Group and count the errors in a log file.", LogTriageCode)),

                debugging.Skill("port-inspect", "Show listening ports and their processes",
                    "Shows which TCP ports are listening and which process owns each, or what is using one port (for example: what is on port 8080?).",
                    false,
                    "The user asks what is running on a port, why a port is in use (EADDRINUSE), or which process owns a server.",
                    "[port]",
                    "`list [port]` uses lsof or ss on macOS and Linux and Get-NetTCPConnection on Windows, and prints port, PID, process name, and address, optionally for one port. It never stops anything; process_stop stops a background process mux started.",
                    ToolchainSkillFactory.Command("list", "List listening ports, or just one.", PortsCode)),

                debugging.Skill("bench", "Benchmark a command or project",
                    "Benchmarks: times one command or compares two (hyperfine, or a built-in timing loop), runs a BenchmarkDotNet project, or runs a k6 load test script.",
                    false,
                    "The user asks how fast something is, to compare two commands, to run benchmarks, or to load test an endpoint.",
                    "\"<command>\" [\"<command>\"] [--runs n] | [filter] | <script.js>",
                    "`time \"<command>\" [\"<other>\"] [--runs n]` uses hyperfine when installed (with one warmup run) and otherwise runs each command n times (default 10) through the platform shell and prints mean, deviation, median, min, and max. `dotnet [filter]` runs the first project that references BenchmarkDotNet in Release with `--filter` (default `*`). `k6 <script.js>` runs a k6 load test; point it only at local or staging targets. The commands being timed run for real.",
                    ToolchainSkillFactory.Command("time", "Time one command or compare two.", TimeCode),
                    ToolchainSkillFactory.Command("dotnet", "Run a BenchmarkDotNet project.", DotnetBenchCode),
                    ToolchainSkillFactory.Command("k6", "Run a k6 load test script.", K6Code))
            };
        }

        #endregion
    }
}
