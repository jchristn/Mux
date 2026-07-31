namespace Mux.Core.Skills
{
    using System;
    using System.Collections.Generic;

    /// <summary>The default hygiene (read-only reporting) skills seeded into ~/.mux/skills.</summary>
    public static class DefaultHygieneSkills
    {
        /// <summary>Returns the hygiene default skills, keyed by id.</summary>
        /// <returns>The skills as id to SKILL.md content.</returns>
        public static IReadOnlyDictionary<string, string> All()
        {
            Dictionary<string, string> skills = new Dictionary<string, string>(StringComparer.Ordinal);

            skills["todo-scan"] = DefaultSkillBuilder.Build(
                "todo-scan", "Find TODO and FIXME markers", "Inventories TODO, FIXME, and HACK markers across common source files.", false, "hygiene",
                "The user wants an inventory of TODO/FIXME/HACK markers.",
                new List<DefaultSkillCommandDef>
                {
                    new DefaultSkillCommandDef("scan", "List TODO/FIXME/HACK markers.", "pwsh",
@"$ErrorActionPreference = 'Stop'
$found = 0
Get-ChildItem -Recurse -File -Include *.cs,*.ts,*.js,*.py,*.go,*.md -ErrorAction SilentlyContinue |
  Where-Object { $_.FullName -notmatch '[\\/](bin|obj|node_modules|\.git)[\\/]' } |
  ForEach-Object {
    Select-String -Path $_.FullName -Pattern 'TODO|FIXME|HACK' -ErrorAction SilentlyContinue |
      ForEach-Object { Write-Output ""$($_.Path):$($_.LineNumber): $($_.Line.Trim())""; $script:found++ }
  }
if ($found -eq 0) { Write-Output 'No TODO/FIXME/HACK markers found.' }
")
                });

            skills["dead-code-scan"] = DefaultSkillBuilder.Build(
                "dead-code-scan", "Find possibly-dead code", "Heuristically flags disabled and obsolete C# code (#if false blocks and [Obsolete] members).", false, "hygiene",
                "The user wants a best-effort list of code that may be dead: #if false blocks and [Obsolete]-marked members.",
                new List<DefaultSkillCommandDef>
                {
                    new DefaultSkillCommandDef("scan", "List possibly-dead C# code (best effort).", "pwsh",
@"$ErrorActionPreference = 'SilentlyContinue'
$found = 0
Get-ChildItem -Recurse -File -Include *.cs -ErrorAction SilentlyContinue |
  Where-Object { $_.FullName -notmatch '[\\/](bin|obj|node_modules|\.git)[\\/]' } |
  ForEach-Object {
    Select-String -Path $_.FullName -Pattern '#if\s+false|\[Obsolete' -ErrorAction SilentlyContinue |
      ForEach-Object { Write-Output ""possibly-dead (best effort): $($_.Path):$($_.LineNumber): $($_.Line.Trim())""; $script:found++ }
  }
if ($found -eq 0) { Write-Output 'possibly-dead (best effort): none found.' }
")
                });

            skills["license-header-check"] = DefaultSkillBuilder.Build(
                "license-header-check", "Check for file header comments", "Lists C# files whose first non-empty line is not a comment, so they may be missing a license header.", false, "hygiene, legal",
                "The user wants to find .cs files that are missing a leading license/header comment.",
                new List<DefaultSkillCommandDef>
                {
                    new DefaultSkillCommandDef("check", "List .cs files missing a leading comment header.", "pwsh",
@"$ErrorActionPreference = 'Stop'
$missing = 0
Get-ChildItem -Recurse -File -Include *.cs -ErrorAction SilentlyContinue |
  Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' } |
  ForEach-Object {
    $first = Get-Content -Path $_.FullName -ErrorAction SilentlyContinue |
      Where-Object { $_.Trim() -ne '' } | Select-Object -First 1
    $head = if ($null -eq $first) { '' } else { $first.TrimStart() }
    if (-not ($head.StartsWith('//') -or $head.StartsWith('/*'))) {
      Write-Output ""missing header: $($_.FullName)""; $missing++
    }
  }
if ($missing -eq 0) { Write-Output 'All .cs files start with a header comment.' }
")
                });

            skills["gitignore-audit"] = DefaultSkillBuilder.Build(
                "gitignore-audit", "Audit tracked build artifacts", "Lists git-tracked paths that look like build output or dependencies and probably should be ignored.", false, "hygiene, git",
                "The user wants to find tracked files that look like build output or dependencies and should be gitignored.",
                new List<DefaultSkillCommandDef>
                {
                    new DefaultSkillCommandDef("audit", "List tracked paths that look like build artifacts.", "pwsh",
@"$ErrorActionPreference = 'Stop'
$hits = 0
git ls-files | ForEach-Object {
  if ($_ -match '(^|/)(bin|obj|node_modules|dist|build)(/|$)' -or $_ -match '\.(dll|exe|pdb|log)$') {
    Write-Output ""tracked artifact: $_""; $hits++
  }
}
if ($hits -eq 0) { Write-Output 'No tracked build artifacts found.' }
")
                });

            skills["large-file-scan"] = DefaultSkillBuilder.Build(
                "large-file-scan", "Find large files", "Lists working-tree files larger than a size threshold, with their sizes.", false, "hygiene",
                "The user wants to find unusually large files in the working tree. Pass a size threshold in MB as $args[0] (default 5).",
                new List<DefaultSkillCommandDef>
                {
                    new DefaultSkillCommandDef("scan", "List files larger than the threshold ($args[0] MB, default 5).", "pwsh",
@"$ErrorActionPreference = 'Stop'
$mb = 5.0
if ($args.Count -ge 1 -and $args[0]) {
  $parsed = 0.0
  if ([double]::TryParse([string]$args[0], [ref]$parsed) -and $parsed -gt 0) { $mb = $parsed }
}
$threshold = $mb * 1MB
$found = 0
Get-ChildItem -Recurse -File -ErrorAction SilentlyContinue |
  Where-Object { $_.FullName -notmatch '[\\/](\.git|bin|obj|node_modules)[\\/]' -and $_.Length -gt $threshold } |
  Sort-Object Length -Descending |
  ForEach-Object {
    $sizeMb = [math]::Round($_.Length / 1MB, 2)
    Write-Output ""$($_.FullName): $sizeMb MB""; $found++
  }
if ($found -eq 0) { Write-Output ""No files larger than $mb MB."" }
")
                });

            skills["line-ending-check"] = DefaultSkillBuilder.Build(
                "line-ending-check", "Check line endings", "Reads text files as bytes and reports any with mixed CRLF/LF line endings.", false, "hygiene",
                "The user wants to find text files with inconsistent (mixed CRLF and LF) line endings.",
                new List<DefaultSkillCommandDef>
                {
                    new DefaultSkillCommandDef("check", "Report text files with mixed line endings.", "pwsh",
@"$ErrorActionPreference = 'Stop'
$reported = 0
Get-ChildItem -Recurse -File -Include *.cs,*.md,*.json,*.ts,*.js -ErrorAction SilentlyContinue |
  Where-Object { $_.FullName -notmatch '[\\/](bin|obj|node_modules|\.git)[\\/]' } |
  ForEach-Object {
    $bytes = [System.IO.File]::ReadAllBytes($_.FullName)
    $crlf = 0
    $lf = 0
    for ($i = 0; $i -lt $bytes.Length; $i++) {
      if ($bytes[$i] -eq 10) {
        if ($i -gt 0 -and $bytes[$i - 1] -eq 13) { $crlf++ } else { $lf++ }
      }
    }
    if ($crlf -gt 0 -and $lf -gt 0) {
      Write-Output ""mixed line endings: $($_.FullName) (CRLF=$crlf, LF=$lf)""; $reported++
    }
  }
if ($reported -eq 0) { Write-Output 'No files with mixed line endings found.' }
")
                });

            skills["readme-audit"] = DefaultSkillBuilder.Build(
                "readme-audit", "Audit README links", "Checks relative markdown links in README.md and reports any whose target file is missing.", false, "hygiene, docs",
                "The user wants to verify that relative links in README.md point at files that exist.",
                new List<DefaultSkillCommandDef>
                {
                    new DefaultSkillCommandDef("audit", "Report broken relative links in README.md.", "pwsh",
@"$ErrorActionPreference = 'Stop'
if (-not (Test-Path -LiteralPath 'README.md')) { Write-Output 'No README.md found.'; return }
$text = Get-Content -Path 'README.md' -Raw
$broken = 0
$matches = [regex]::Matches($text, '\]\(([^)]+)\)')
foreach ($m in $matches) {
  $target = $m.Groups[1].Value.Trim()
  if ($target -match '^[a-zA-Z][a-zA-Z0-9+.-]*://' -or $target -match '^https?:' -or $target.StartsWith('#') -or $target.StartsWith('mailto:')) { continue }
  $path = $target.Split('#')[0]
  if ($path -eq '') { continue }
  if (-not (Test-Path -LiteralPath $path)) { Write-Output ""broken link: $target""; $broken++ }
}
if ($broken -eq 0) { Write-Output 'All relative README.md links resolve.' }
")
                });

            skills["codestyle-audit"] = DefaultSkillBuilder.Build(
                "codestyle-audit", "Audit C# style", "Heuristically flags likely style violations: use of var and files whose first code line is not namespace.", false, "hygiene, dotnet",
                "The user wants a best-effort check for this repo's C# style rules: no var declarations, and namespace as the first line.",
                new List<DefaultSkillCommandDef>
                {
                    new DefaultSkillCommandDef("audit", "List likely C# style violations (best effort).", "pwsh",
@"$ErrorActionPreference = 'SilentlyContinue'
$hits = 0
Get-ChildItem -Recurse -File -Include *.cs -ErrorAction SilentlyContinue |
  Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' } |
  ForEach-Object {
    $lines = @(Get-Content -Path $_.FullName -ErrorAction SilentlyContinue)
    for ($i = 0; $i -lt $lines.Count; $i++) {
      if ($lines[$i] -match '(^|\s)var\s+\w+\s*=') {
        Write-Output ""$($_.FullName):$($i + 1): var used as a declaration""; $hits++
      }
    }
    $firstCode = $lines |
      Where-Object {
        $t = $_.Trim()
        $t -ne '' -and -not $t.StartsWith('//') -and -not $t.StartsWith('/*') -and -not $t.StartsWith('*')
      } | Select-Object -First 1
    if ($null -ne $firstCode -and -not $firstCode.TrimStart().StartsWith('namespace')) {
      Write-Output ""$($_.FullName):1: first code line is not namespace""; $hits++
    }
  }
if ($hits -eq 0) { Write-Output 'No likely style violations found.' }
")
                });

            return skills;
        }
    }
}
