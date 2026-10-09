namespace Mux.Core.Skills
{
    using System.Collections.Generic;

    /// <summary>
    /// Dependency supply-chain skills: <c>deps-audit</c> runs each ecosystem's vulnerability auditor (or osv-scanner for
    /// all of them) and prints one merged report, and <c>sbom</c> writes a CycloneDX software bill of materials with syft.
    /// The auditors' JSON is parsed in the skill, so <c>--from &lt;tool&gt;=&lt;file&gt;</c> can replay recorded output.
    /// </summary>
    public static class DefaultDependencyAuditSkills
    {
        #region Private-Members

        private static readonly string[] _Manifests =
        {
            "package.json", "requirements*.txt", "pyproject.toml", "Pipfile.lock", "poetry.lock", "Cargo.lock",
            "*.sln", "*.slnx", "**/*.csproj", "go.mod"
        };

        private const string AuditSetup = @"$script:DepRows = New-Object System.Collections.Generic.List[object]
$script:DepSeen = New-Object System.Collections.Generic.HashSet[string]
$script:DepNotes = New-Object System.Collections.Generic.List[string]
$script:DepScanned = New-Object System.Collections.Generic.List[string]
$script:DepRank = @{ low = 1; moderate = 2; high = 3; critical = 4; unknown = 3 }
function ConvertTo-MuxSeverity {
    param([object]$Value)
    $text = ([string]$Value).Trim().ToLowerInvariant()
    $number = 0.0
    if ([double]::TryParse($text, [System.Globalization.NumberStyles]::Float, [System.Globalization.CultureInfo]::InvariantCulture, [ref]$number)) {
        if ($number -ge 9) { return 'critical' }
        if ($number -ge 7) { return 'high' }
        if ($number -ge 4) { return 'moderate' }
        if ($number -gt 0) { return 'low' }
        return 'unknown'
    }
    if ($text -eq 'critical') { return 'critical' }
    if ($text -eq 'high') { return 'high' }
    if ($text -eq 'moderate' -or $text -eq 'medium') { return 'moderate' }
    if ($text -eq 'low') { return 'low' }
    return 'unknown'
}
function Get-MuxAdvisoryLabel {
    param([object]$Url, [object]$Fallback)
    $u = [string]$Url
    if ($u -match '(GHSA(-[a-z0-9]{4}){3}|CVE-\d{4}-\d+|GO-\d{4}-\d+|RUSTSEC-\d{4}-\d+|PYSEC-\d{4}-\d+)') { return $Matches[1] }
    if ([string]$Fallback) { return [string]$Fallback }
    if ($u) { return $u }
    return 'advisory'
}
function Add-MuxDepRow {
    param([string]$Ecosystem, [object]$Package, [object]$Version, [string]$Severity, [object]$Advisory, [object]$Fixed)
    $row = [pscustomobject]@{
        Ecosystem = $Ecosystem
        Package = [string]$Package
        Version = $(if ([string]$Version) { [string]$Version } else { '?' })
        Severity = $Severity
        Advisory = [string]$Advisory
        Fixed = $(if ([string]$Fixed) { [string]$Fixed } else { 'none' })
    }
    $key = ($row.Ecosystem, $row.Package, $row.Version, $row.Advisory) -join '|'
    if ($script:DepSeen.Add($key)) { $script:DepRows.Add($row) }
}
function Add-MuxV6Advisory {
    param([string]$Ecosystem, [object]$Advisory)
    $versions = @($Advisory.findings | ForEach-Object { [string]$_.version } | Where-Object { $_ } | Select-Object -Unique) -join ', '
    $patched = [string]$Advisory.patched_versions
    if ($patched -eq '<0.0.0') { $patched = 'none' }
    Add-MuxDepRow $Ecosystem $Advisory.module_name $versions (ConvertTo-MuxSeverity $Advisory.severity) (Get-MuxAdvisoryLabel $Advisory.url $Advisory.title) $patched
}
function Split-MuxJsonStream {
    param([string]$Text)
    $objects = New-Object System.Collections.Generic.List[string]
    $depth = 0
    $inString = $false
    $escape = $false
    $start = -1
    for ($i = 0; $i -lt $Text.Length; $i++) {
        $c = $Text[$i]
        if ($inString) {
            if ($escape) { $escape = $false }
            elseif ($c -eq [char]92) { $escape = $true }
            elseif ($c -eq [char]34) { $inString = $false }
            continue
        }
        if ($c -eq [char]34) { $inString = $true }
        elseif ($c -eq '{') { if ($depth -eq 0) { $start = $i }; $depth++ }
        elseif ($c -eq '}') {
            $depth--
            if ($depth -eq 0 -and $start -ge 0) { $objects.Add($Text.Substring($start, $i - $start + 1)); $start = -1 }
        }
    }
    return ,$objects
}
function Read-MuxAuditOutput {
    param([string]$Tool, [string]$Text)
    switch ($Tool) {
        { $_ -in @('npm', 'pnpm') } {
            $doc = $Text | ConvertFrom-Json -Depth 64
            if ($doc.PSObject.Properties['vulnerabilities'] -and $doc.vulnerabilities) {
                foreach ($property in $doc.vulnerabilities.PSObject.Properties) {
                    $v = $property.Value
                    $fixed = 'none'
                    if ($v.fixAvailable -is [bool]) { if ($v.fixAvailable) { $fixed = 'available' } }
                    elseif ($v.fixAvailable) { $fixed = [string]$v.fixAvailable.name + '@' + [string]$v.fixAvailable.version }
                    foreach ($via in @($v.via)) {
                        if ($via -is [string]) { continue }
                        Add-MuxDepRow 'npm' $v.name $v.range (ConvertTo-MuxSeverity $via.severity) (Get-MuxAdvisoryLabel $via.url $via.title) $fixed
                    }
                }
            } elseif ($doc.PSObject.Properties['advisories'] -and $doc.advisories) {
                foreach ($property in $doc.advisories.PSObject.Properties) { Add-MuxV6Advisory 'npm' $property.Value }
            } elseif (-not $doc.PSObject.Properties['metadata']) {
                throw 'no vulnerabilities, advisories, or metadata field'
            }
        }
        'yarn' {
            foreach ($line in ($Text -split ""`n"")) {
                if (-not $line.Trim()) { continue }
                $entry = $line | ConvertFrom-Json -Depth 64
                if ($entry.type -eq 'auditAdvisory') { Add-MuxV6Advisory 'npm' $entry.data.advisory }
            }
        }
        'pip-audit' {
            $items = @($Text | ConvertFrom-Json -Depth 64)
            $deps = $items
            if ($items.Count -eq 1 -and $items[0].PSObject.Properties['dependencies']) { $deps = @($items[0].dependencies) }
            foreach ($d in $deps) {
                foreach ($v in @($d.vulns)) {
                    if (-not $v) { continue }
                    $fix = @($v.fix_versions) -join ', '
                    Add-MuxDepRow 'python' $d.name $d.version 'unknown' $v.id $fix
                }
            }
        }
        'cargo-audit' {
            $doc = $Text | ConvertFrom-Json -Depth 64
            if (-not $doc.PSObject.Properties['vulnerabilities']) { throw 'no vulnerabilities field' }
            foreach ($v in @($doc.vulnerabilities.list)) {
                if (-not $v) { continue }
                Add-MuxDepRow 'rust' $v.package.name $v.package.version 'unknown' $v.advisory.id (@($v.versions.patched) -join ', ')
            }
        }
        'dotnet' {
            $doc = $Text | ConvertFrom-Json -Depth 64
            if (-not $doc.PSObject.Properties['projects']) { throw 'no projects field' }
            foreach ($project in @($doc.projects)) {
                foreach ($framework in @($project.frameworks)) {
                    foreach ($package in (@($framework.topLevelPackages) + @($framework.transitivePackages))) {
                        if (-not $package) { continue }
                        foreach ($v in @($package.vulnerabilities)) {
                            if (-not $v) { continue }
                            Add-MuxDepRow 'nuget' $package.id $package.resolvedVersion (ConvertTo-MuxSeverity $v.severity) (Get-MuxAdvisoryLabel $v.advisoryurl $v.advisoryurl) 'see advisory'
                        }
                    }
                }
            }
        }
        'govulncheck' {
            $fixedById = @{}
            foreach ($json in (Split-MuxJsonStream $Text)) {
                $message = $json | ConvertFrom-Json -Depth 64
                if (-not $message.PSObject.Properties['finding']) { continue }
                $finding = $message.finding
                $frame = @($finding.trace)[0]
                if (-not $frame) { continue }
                Add-MuxDepRow 'go' $frame.module $frame.version 'unknown' $finding.osv $finding.fixed_version
            }
        }
        'osv-scanner' {
            $doc = $Text | ConvertFrom-Json -Depth 64
            if (-not $doc.PSObject.Properties['results']) { throw 'no results field' }
            foreach ($result in @($doc.results)) {
                foreach ($package in @($result.packages)) {
                    if (-not $package) { continue }
                    $groupSeverity = @{}
                    foreach ($group in @($package.groups)) { foreach ($id in @($group.ids)) { $groupSeverity[[string]$id] = $group.max_severity } }
                    foreach ($v in @($package.vulnerabilities)) {
                        if (-not $v) { continue }
                        $severity = 'unknown'
                        if ($v.database_specific -and $v.database_specific.severity) { $severity = ConvertTo-MuxSeverity $v.database_specific.severity }
                        elseif ($groupSeverity.ContainsKey([string]$v.id)) { $severity = ConvertTo-MuxSeverity $groupSeverity[[string]$v.id] }
                        $fixes = @(foreach ($affected in @($v.affected)) { foreach ($range in @($affected.ranges)) { foreach ($event in @($range.events)) { if ($event.fixed) { [string]$event.fixed } } } }) | Select-Object -Unique
                        Add-MuxDepRow ([string]$package.package.ecosystem).ToLowerInvariant() $package.package.name $package.package.version $severity $v.id (@($fixes) -join ', ')
                    }
                }
            }
        }
        default { throw ('unknown tool ' + $Tool) }
    }
}
";

        private const string AuditCode = @"$minSeverity = 'high'
$sources = New-Object System.Collections.Generic.List[object]
$native = $false
$all = @($args)
for ($i = 0; $i -lt $all.Count; $i++) {
    $token = [string]$all[$i]
    if ($token -eq '--min-severity') {
        if (($i + 1) -ge $all.Count) { Exit-MuxNotApplicable '--min-severity needs a value: low, moderate, high, or critical.' }
        $minSeverity = ([string]$all[$i + 1]).ToLowerInvariant()
        $i++
        continue
    }
    if ($token -eq '--from') {
        if (($i + 1) -ge $all.Count) { Exit-MuxNotApplicable '--from needs <tool>=<file>.' }
        $pair = [string]$all[$i + 1]
        $i++
        $eq = $pair.IndexOf('=')
        if ($eq -lt 1) { Exit-MuxNotApplicable ('--from takes <tool>=<file>, got ' + $pair) }
        $sources.Add(@{ Tool = $pair.Substring(0, $eq).ToLowerInvariant(); File = $pair.Substring($eq + 1) })
        continue
    }
    if ($token -eq '--native') { $native = $true; continue }
    Exit-MuxNotApplicable ('unknown argument ' + $token + '. Use --min-severity <level>, --native, or --from <tool>=<file>.')
}
if ($minSeverity -eq 'medium') { $minSeverity = 'moderate' }
if (@('low', 'moderate', 'high', 'critical') -notcontains $minSeverity) { Exit-MuxNotApplicable ('unknown severity ' + $minSeverity + '; use low, moderate, high, or critical.') }
$knownTools = @('npm', 'pnpm', 'yarn', 'pip-audit', 'cargo-audit', 'dotnet', 'govulncheck', 'osv-scanner')

if ($sources.Count -gt 0) {
    foreach ($source in $sources) {
        if ($knownTools -notcontains $source.Tool) { Exit-MuxNotApplicable ('unknown tool ' + $source.Tool + ' in --from; use one of ' + ($knownTools -join ', ') + '.') }
        if (-not (Test-Path -LiteralPath $source.File -PathType Leaf)) { Exit-MuxNotApplicable ('file not found: ' + $source.File) }
        try { Read-MuxAuditOutput $source.Tool (Get-Content -LiteralPath $source.File -Raw) }
        catch { Exit-MuxNotApplicable ('could not read ' + $source.Tool + ' output in ' + $source.File + ': ' + $_.Exception.Message) }
        $script:DepScanned.Add($source.Tool)
    }
} else {
    $root = Get-MuxRepoRoot
    Set-Location -LiteralPath $root
    $plan = New-Object System.Collections.Generic.List[object]
    $detected = New-Object System.Collections.Generic.List[string]
    $has = { param($pattern) [bool](Get-ChildItem -LiteralPath $root -Filter $pattern -File -Force -ErrorAction SilentlyContinue | Select-Object -First 1) }
    if (Test-Path -LiteralPath (Join-Path $root 'package.json')) {
        $detected.Add('javascript')
        if (Test-Path -LiteralPath (Join-Path $root 'pnpm-lock.yaml')) { $plan.Add(@{ Eco = 'javascript'; Tool = 'pnpm'; Exe = 'pnpm'; Args = @('audit', '--json'); Hint = 'Install pnpm.' }) }
        elseif (Test-Path -LiteralPath (Join-Path $root 'yarn.lock')) { $plan.Add(@{ Eco = 'javascript'; Tool = 'yarn'; Exe = 'yarn'; Args = @('audit', '--json'); Hint = 'Install yarn 1 (yarn audit); for yarn 2+ install osv-scanner.' }) }
        elseif (Test-Path -LiteralPath (Join-Path $root 'package-lock.json')) { $plan.Add(@{ Eco = 'javascript'; Tool = 'npm'; Exe = 'npm'; Args = @('audit', '--json'); Hint = 'Install Node.js and npm.' }) }
        else { $script:DepNotes.Add('javascript: no lockfile, so there is nothing to audit; install dependencies first (js-install).') }
    }
    $requirements = Join-Path $root 'requirements.txt'
    if ((Test-Path -LiteralPath $requirements) -or (Test-Path -LiteralPath (Join-Path $root 'pyproject.toml'))) {
        $detected.Add('python')
        $pipArgs = if (Test-Path -LiteralPath $requirements) { @('-f', 'json', '-r', 'requirements.txt') } else { @('-f', 'json', '.') }
        $plan.Add(@{ Eco = 'python'; Tool = 'pip-audit'; Exe = 'pip-audit'; Args = $pipArgs; Hint = 'Install pip-audit: pip install pip-audit.' })
    }
    if (Test-Path -LiteralPath (Join-Path $root 'Cargo.lock')) {
        $detected.Add('rust')
        $plan.Add(@{ Eco = 'rust'; Tool = 'cargo-audit'; Exe = 'cargo'; Args = @('audit', '--json'); Hint = 'Install cargo-audit: cargo install cargo-audit.'; Needs = 'cargo-audit' })
    }
    $dotnetTarget = Get-ChildItem -LiteralPath $root -Include '*.sln', '*.slnx' -File -Recurse -Depth 1 -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $dotnetTarget) { $dotnetTarget = Get-ChildItem -LiteralPath $root -Filter '*.csproj' -File -Recurse -Depth 2 -ErrorAction SilentlyContinue | Select-Object -First 1 }
    if ($dotnetTarget) {
        $detected.Add('nuget')
        $plan.Add(@{ Eco = 'nuget'; Tool = 'dotnet'; Exe = 'dotnet'; Args = @('list', $dotnetTarget.FullName, 'package', '--vulnerable', '--include-transitive', '--format', 'json'); Hint = 'Install the .NET SDK.' })
    }
    if (Test-Path -LiteralPath (Join-Path $root 'go.mod')) {
        $detected.Add('go')
        $plan.Add(@{ Eco = 'go'; Tool = 'govulncheck'; Exe = 'govulncheck'; Args = @('-json', './...'); Hint = 'Install govulncheck: go install golang.org/x/vuln/cmd/govulncheck@latest.' })
    }
    if ($detected.Count -eq 0) { Exit-MuxNotApplicable ('no package manifest or lockfile found under ' + $root + ' (package.json, requirements.txt, pyproject.toml, Cargo.lock, a .NET solution or project, or go.mod).') }
    if (-not $native -and (Test-MuxTool 'osv-scanner')) {
        $plan.Clear()
        $plan.Add(@{ Eco = ($detected -join ', '); Tool = 'osv-scanner'; Exe = 'osv-scanner'; Args = @('--format', 'json', '-r', '.'); Hint = '' })
    }
    foreach ($step in $plan) {
        $needs = if ($step.Needs) { $step.Needs } else { $step.Exe }
        if (Test-MuxDryRun) { Write-Output ('DRYRUN: ' + (Format-MuxCommand -Tool $step.Exe -Arguments $step.Args)); continue }
        if (-not (Test-MuxTool $needs)) { $script:DepNotes.Add($step.Eco + ': not audited; ' + $needs + ' is not installed. ' + $step.Hint); continue }
        Write-Output ('> ' + (Format-MuxCommand -Tool $step.Exe -Arguments $step.Args))
        $text = (& $step.Exe @($step.Args) 2>$null | Out-String)
        try { Read-MuxAuditOutput $step.Tool $text; $script:DepScanned.Add($step.Eco) }
        catch {
            $first = (($text -split ""`n"") | Where-Object { $_.Trim() } | Select-Object -First 1)
            $script:DepNotes.Add($step.Eco + ': ' + $step.Tool + ' did not produce a report (' + $_.Exception.Message + '). First line: ' + $first)
        }
    }
    if (Test-MuxDryRun) { foreach ($note in $script:DepNotes) { Write-Output ('note: ' + $note) }; exit 0 }
    if ($script:DepScanned.Count -eq 0) {
        foreach ($note in $script:DepNotes) { Write-Output ('mux: ' + $note) }
        Exit-MuxNotApplicable 'no auditor could run; install one of the tools above, or osv-scanner for every ecosystem at once.'
    }
}

$rows = @($script:DepRows | Sort-Object -Property @{ Expression = { $script:DepRank[$_.Severity] }; Descending = $true }, Ecosystem, Package, Advisory)
$threshold = $script:DepRank[$minSeverity]
$failing = @($rows | Where-Object { $script:DepRank[$_.Severity] -ge $threshold })
Write-Output ''
if ($rows.Count -eq 0) {
    Write-Output ('No known vulnerabilities (' + ($script:DepScanned -join ', ') + ').')
} else {
    $header = [pscustomobject]@{ Ecosystem = 'ECOSYSTEM'; Package = 'PACKAGE'; Version = 'VERSION'; Severity = 'SEVERITY'; Advisory = 'ADVISORY'; Fixed = 'FIXED' }
    $table = @($header) + $rows
    $widths = @{}
    foreach ($column in 'Ecosystem', 'Package', 'Version', 'Severity', 'Advisory') { $widths[$column] = [Math]::Min(40, ($table | ForEach-Object { ([string]$_.$column).Length } | Measure-Object -Maximum).Maximum) }
    foreach ($row in $table) {
        $cells = foreach ($column in 'Ecosystem', 'Package', 'Version', 'Severity', 'Advisory') { $value = [string]$row.$column; if ($value.Length -gt 40) { $value = $value.Substring(0, 37) + '...' }; $value.PadRight($widths[$column]) }
        Write-Output ((@($cells) + [string]$row.Fixed) -join '  ')
    }
    $counts = foreach ($level in 'critical', 'high', 'moderate', 'low', 'unknown') { $n = @($rows | Where-Object { $_.Severity -eq $level }).Count; if ($n) { [string]$n + ' ' + $level } }
    Write-Output ''
    Write-Output ([string]$rows.Count + $(if ($rows.Count -eq 1) { ' advisory (' } else { ' advisories (' }) + (@($counts) -join ', ') + '); ' + [string]$failing.Count + ' at or above ' + $minSeverity + '. Advisories without a severity count as high.')
}
foreach ($note in $script:DepNotes) { Write-Output ('note: ' + $note) }
if ($failing.Count -gt 0) { exit 1 }
exit 0
";

        private const string SbomCode = @"$output = Get-MuxArg -Arguments $args -Index 0
if (-not $output) { $output = 'sbom.cdx.json' }
if ([System.IO.Path]::IsPathRooted($output) -or $output.Contains('..')) { Exit-MuxNotApplicable ('write the SBOM inside the repository with a relative path, got ' + $output) }
$root = Get-MuxRepoRoot
Set-Location -LiteralPath $root
Invoke-MuxTool -Tool 'syft' -Arguments @('scan', 'dir:.', '--output', ('cyclonedx-json=' + $output)) -InstallHint 'Install syft from https://github.com/anchore/syft.'
if (-not (Test-MuxDryRun)) { Write-Output ('Wrote ' + (Join-Path $root $output) + ' (CycloneDX JSON).') }
";

        #endregion

        #region Public-Methods

        /// <summary>Returns the dependency audit skill definitions.</summary>
        /// <returns>The definitions.</returns>
        public static IReadOnlyList<DefaultSkillDef> All()
        {
            ToolchainSkillFactory audit = new ToolchainSkillFactory(AuditSetup, new[] { "security", "dependencies" }, _Manifests, null,
                " Exit codes: 0 no advisory at or above the threshold, 1 advisories found, 2 no project or auditor found, or bad arguments.");
            ToolchainSkillFactory sbom = new ToolchainSkillFactory(string.Empty, new[] { "security", "dependencies" }, _Manifests, null);

            return new List<DefaultSkillDef>
            {
                audit.Skill("deps-audit", "Audit dependencies for known vulnerabilities",
                    "Checks the project's dependencies for known vulnerabilities and security advisories (CVEs) with npm, pnpm, yarn, pip-audit, cargo-audit, dotnet, govulncheck for Go modules, or osv-scanner, and prints one report.",
                    false,
                    "The user asks whether dependencies have known vulnerabilities, CVEs, or security advisories, before a release, or after a dependency change.",
                    "[--min-severity low|moderate|high|critical] [--native] [--from tool=file]",
                    "`audit` finds every ecosystem under the repository root and runs its auditor: npm, pnpm, or yarn 1 audit for a JavaScript lockfile, pip-audit for requirements.txt or pyproject.toml, cargo-audit for Cargo.lock, `dotnet list package --vulnerable --include-transitive` for a .NET solution or project, and govulncheck for go.mod. When osv-scanner is installed it scans every lockfile at once instead (`--native` keeps the per-ecosystem tools). The report lists ecosystem, package, version, severity, advisory id, and fixed version, and exits 1 when any advisory is at or above `--min-severity` (default high). pip-audit, cargo-audit, and govulncheck do not report a severity, so those advisories count as high. A missing auditor is noted and skipped, not fatal, unless nothing could run. `--from <tool>=<file>` (repeatable) reads saved JSON output instead of running anything. Read-only.",
                    ToolchainSkillFactory.Command("audit", "Audit every detected ecosystem and print one report.", AuditCode)),

                sbom.Skill("sbom", "Write a software bill of materials",
                    "Writes a CycloneDX JSON software bill of materials (SBOM) for the repository with syft.",
                    true,
                    "The user asks for an SBOM, a software bill of materials, or an inventory of every dependency for compliance or a release.",
                    "[output-file]",
                    "`write [output-file]` runs `syft scan dir:.` from the repository root and writes CycloneDX JSON to the given relative path (default `sbom.cdx.json`). Paths outside the repository are refused.",
                    ToolchainSkillFactory.Command("write", "Write the SBOM (default sbom.cdx.json).", SbomCode))
            };
        }

        #endregion
    }
}
