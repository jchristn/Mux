namespace Mux.Core.Skills
{
    using System;
    using System.Collections.Generic;

    /// <summary>The default workflow and utility skills seeded into ~/.mux/skills.</summary>
    public static class DefaultWorkflowUtilitySkills
    {
        /// <summary>Returns the workflow and utility default skills, keyed by id.</summary>
        /// <returns>The skills as id to SKILL.md content.</returns>
        public static IReadOnlyDictionary<string, string> All()
        {
            Dictionary<string, string> skills = new Dictionary<string, string>(StringComparer.Ordinal);

            skills["standup-summary"] = DefaultSkillBuilder.Build(
                "standup-summary", "Summarize recent commits for standup",
                "Groups recent commits by author for a quick standup update.", false, "workflow",
                "The user wants a quick summary of what was committed recently, grouped by author, for a standup.",
                new List<DefaultSkillCommandDef>
                {
                    new DefaultSkillCommandDef("since", "Summarize commits since a date, grouped by author.", "pwsh",
@"$ErrorActionPreference = 'Stop'
$since = if ($args.Count -gt 0) { $args[0] } else { '1 day ago' }
Write-Output ""Standup summary since '$since':""
$lines = git log --since=""$since"" --pretty=format:'%an|%h %s'
if (-not $lines) { Write-Output '  (no commits)'; return }
foreach ($group in ($lines | Group-Object { ($_ -split '\|', 2)[0] })) {
  Write-Output ""$($group.Name):""
  foreach ($entry in $group.Group) { Write-Output ""  $(($entry -split '\|', 2)[1])"" }
}
")
                });

            skills["release-notes"] = DefaultSkillBuilder.Build(
                "release-notes", "Assemble release notes from the changelog",
                "Prints the topmost section of CHANGELOG.md as release notes.", false, "workflow, release",
                "The user wants the most recent CHANGELOG.md section extracted as release notes.",
                new List<DefaultSkillCommandDef>
                {
                    new DefaultSkillCommandDef("assemble", "Print the first section of CHANGELOG.md.", "pwsh",
@"$ErrorActionPreference = 'Stop'
$path = 'CHANGELOG.md'
if (-not (Test-Path -LiteralPath $path)) { Write-Error ""Not found: $path""; exit 1 }
$lines = @(Get-Content -LiteralPath $path)
$start = -1
for ($i = 0; $i -lt $lines.Count; $i++) { if ($lines[$i] -like '## *') { $start = $i; break } }
if ($start -lt 0) { Write-Output 'No section headings found.'; return }
Write-Output $lines[$start]
for ($i = $start + 1; $i -lt $lines.Count; $i++) { if ($lines[$i] -like '## *') { break }; Write-Output $lines[$i] }
")
                });

            skills["pr-description"] = DefaultSkillBuilder.Build(
                "pr-description", "Draft a pull request description",
                "Drafts a PR title and bulleted change list from the commit log.", false, "workflow, git",
                "The user wants a pull request title and bulleted description generated from commits ahead of a base ref.",
                new List<DefaultSkillCommandDef>
                {
                    new DefaultSkillCommandDef("draft", "Draft a title and change list from commits since the base ref.", "pwsh",
@"$ErrorActionPreference = 'Stop'
$base = if ($args.Count -gt 0) { $args[0] } else { 'origin/HEAD' }
$title = (git log -1 --format='%s').Trim()
Write-Output ""# $title""
Write-Output ''
Write-Output '## Changes'
git log --format='- %s' ""$base..HEAD""
")
                });

            skills["env-report"] = DefaultSkillBuilder.Build(
                "env-report", "Report the tooling environment",
                "Reports the OS and the versions of common developer tools.", false, "workflow",
                "The user wants a quick dump of the OS and installed tool versions for a bug report or setup check.",
                new List<DefaultSkillCommandDef>
                {
                    new DefaultSkillCommandDef("report", "Print the OS and common tool versions.", "pwsh",
@"Write-Output ""OS: $([System.Runtime.InteropServices.RuntimeInformation]::OSDescription)""
foreach ($tool in 'git','dotnet','node','python','pwsh') {
  try {
    $version = (& $tool --version 2>$null | Select-Object -First 1)
    if ($version) { Write-Output ""$tool: $version"" } else { Write-Output ""$tool: (not found)"" }
  }
  catch { Write-Output ""$tool: (not found)"" }
}
")
                });

            skills["json-validate"] = DefaultSkillBuilder.Build(
                "json-validate", "Validate a JSON file",
                "Checks whether a JSON file parses and reports the first error.", false, "utility",
                "The user wants to check whether a JSON file is well-formed.",
                new List<DefaultSkillCommandDef>
                {
                    new DefaultSkillCommandDef("check", "Validate the given JSON file.", "node",
@"const fs = require('fs');
const p = process.argv[2];
if (!p) { console.error('Usage: run_skill json-validate check --arg <file>'); process.exit(1); }
try { JSON.parse(fs.readFileSync(p, 'utf8')); console.log('Valid JSON: ' + p); }
catch (e) { console.error('Invalid JSON in ' + p + ': ' + e.message); process.exit(1); }
")
                });

            skills["yaml-lint"] = DefaultSkillBuilder.Build(
                "yaml-lint", "Lint a YAML file for tab indentation",
                "Flags YAML lines that use a literal tab for indentation.", false, "utility",
                "The user wants a light structural check of a YAML file for tab-based indentation, which YAML forbids.",
                new List<DefaultSkillCommandDef>
                {
                    new DefaultSkillCommandDef("check", "Report lines that use a tab for indentation.", "node",
@"const fs = require('fs');
const p = process.argv[2];
if (!p) { console.error('Usage: run_skill yaml-lint check --arg <file>'); process.exit(1); }
const lines = fs.readFileSync(p, 'utf8').split(/\r?\n/);
let issues = 0;
for (let i = 0; i < lines.length; i++) {
  const indent = lines[i].match(/^[ \t]*/)[0];
  if (indent.indexOf('\t') !== -1) { console.log(p + ':' + (i + 1) + ': tab used for indentation'); issues++; }
}
if (issues === 0) { console.log('No tab-indentation issues found.'); }
")
                });

            skills["url-check"] = DefaultSkillBuilder.Build(
                "url-check", "Check links in a markdown file",
                "Extracts URLs from markdown and probes each with a HEAD request.", false, "utility, docs",
                "The user wants to check whether the http/https links in a markdown file are reachable.",
                new List<DefaultSkillCommandDef>
                {
                    new DefaultSkillCommandDef("check", "Probe each URL found in the markdown file.", "pwsh",
@"$ErrorActionPreference = 'Stop'
if ($args.Count -lt 1) { Write-Error 'Usage: run_skill url-check check --arg <file.md>'; exit 1 }
$path = $args[0]
if (-not (Test-Path -LiteralPath $path)) { Write-Error ""Not found: $path""; exit 1 }
$text = Get-Content -LiteralPath $path -Raw
$urls = [regex]::Matches($text, 'https?://[^\s<>)\]]+') | ForEach-Object { $_.Value } | Sort-Object -Unique
if (-not $urls) { Write-Output 'No URLs found.'; return }
foreach ($url in $urls) {
  try {
    $response = Invoke-WebRequest -Uri $url -Method Head -TimeoutSec 10 -ErrorAction Stop
    Write-Output ""$url  OK ($($response.StatusCode))""
  }
  catch { Write-Output ""$url  FAILED ($($_.Exception.Message))"" }
}
")
                });

            skills["spellcheck-docs"] = DefaultSkillBuilder.Build(
                "spellcheck-docs", "Find doubled words in markdown",
                "Scans markdown for accidentally doubled words and reports them.", false, "utility, docs",
                "The user wants to find accidental doubled words in a markdown file or across all markdown files.",
                new List<DefaultSkillCommandDef>
                {
                    new DefaultSkillCommandDef("check", "Report doubled words with their file and line.", "pwsh",
@"$ErrorActionPreference = 'SilentlyContinue'
try {
  if ($args.Count -ge 1) { $files = @(Get-Item -LiteralPath $args[0]) } else { $files = @(Get-ChildItem -Recurse -File -Filter '*.md') }
  $pattern = '\b(\w+)\s+\1\b'
  foreach ($file in $files) {
    $number = 0
    foreach ($line in (Get-Content -LiteralPath $file.FullName)) {
      $number++
      $match = [regex]::Match($line, $pattern, 'IgnoreCase')
      if ($match.Success) { Write-Output ""$($file.FullName):${number}: $($match.Value)"" }
    }
  }
}
catch { }
")
                });

            return skills;
        }
    }
}
