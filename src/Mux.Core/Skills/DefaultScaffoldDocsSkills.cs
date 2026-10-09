namespace Mux.Core.Skills
{
    using System;
    using System.Collections.Generic;

    /// <summary>The default scaffolding and documentation skills seeded into ~/.mux/skills.</summary>
    public static class DefaultScaffoldDocsSkills
    {
        #region Private-Members

        // new-tool and new-touchstone-suite scaffold mux's own types, so they are listed only inside the mux repository.
        private static readonly IReadOnlyList<string> MuxRepositoryOnly = new[] { "src/Mux.Core/Mux.Core.csproj" };

        #endregion

        /// <summary>Returns the scaffolding and documentation default skills, keyed by id.</summary>
        /// <returns>The skills as id to SKILL.md content.</returns>
        public static IReadOnlyDictionary<string, string> All()
        {
            Dictionary<string, string> skills = new Dictionary<string, string>(StringComparer.Ordinal);

            skills["new-class"] = DefaultSkillBuilder.Build(
                "new-class", "Scaffold a new C# class", "Creates a new C# class file following this repo's style.", true, "scaffold, dotnet",
                "The user wants to create a new C# class file in this repo's style.",
                new List<DefaultSkillCommandDef>
                {
                    new DefaultSkillCommandDef("create", "Create a new class file from the repo template.", "pwsh",
@"$ErrorActionPreference = 'Stop'
if ($args.Count -lt 1 -or [string]::IsNullOrWhiteSpace($args[0])) { Write-Error 'Usage: create <ClassName> [Namespace]'; exit 1 }
$name = $args[0]
$ns = 'Mux.Core'
if ($args.Count -ge 2 -and -not [string]::IsNullOrWhiteSpace($args[1])) { $ns = $args[1] }
$path = ""$name.cs""
$content = ""namespace $ns`n{`n    using System;`n`n    /// <summary>`n    /// TODO.`n    /// </summary>`n    public class $name`n    {`n    }`n}`n""
Set-Content -Path $path -Value $content -NoNewline
Write-Output ""Created $path""
")
                });

            skills["new-tool"] = DefaultSkillBuilder.Build(
                "new-tool", "Scaffold a new tool executor", "Creates an IToolExecutor implementation stub.", true, "scaffold, dotnet",
                "The user wants to create a new IToolExecutor implementation stub.",
                new List<DefaultSkillCommandDef>
                {
                    new DefaultSkillCommandDef("create", "Create an IToolExecutor stub with TODOs.", "pwsh",
@"$ErrorActionPreference = 'Stop'
if ($args.Count -lt 1 -or [string]::IsNullOrWhiteSpace($args[0])) { Write-Error 'Usage: create <ToolName>'; exit 1 }
$name = $args[0]
$path = ""${name}Tool.cs""
$content = ""namespace Mux.Core.Tools`n{`n    using System;`n    using System.Threading;`n    using System.Threading.Tasks;`n`n    /// <summary>`n    /// TODO.`n    /// </summary>`n    public class ${name}Tool : IToolExecutor`n    {`n        public string Name => ""${name}"";`n`n        public string Description => ""TODO."";`n`n        public string ParametersSchema => ""{}"";`n`n        public Task<string> ExecuteAsync(string arguments, CancellationToken token)`n        {`n            // TODO: implement.`n            return Task.FromResult(string.Empty);`n        }`n    }`n}`n""
Set-Content -Path $path -Value $content -NoNewline
Write-Output ""Created $path""
")
                },
                MuxRepositoryOnly);

            skills["new-touchstone-suite"] = DefaultSkillBuilder.Build(
                "new-touchstone-suite", "Scaffold a new touchstone test suite", "Creates a touchstone test suite stub with one placeholder case.", true, "scaffold, test",
                "The user wants to create a new touchstone test suite stub.",
                new List<DefaultSkillCommandDef>
                {
                    new DefaultSkillCommandDef("create", "Create a TestSuiteDescriptor stub with one placeholder case.", "pwsh",
@"$ErrorActionPreference = 'Stop'
if ($args.Count -lt 1 -or [string]::IsNullOrWhiteSpace($args[0])) { Write-Error 'Usage: create <SuiteName>'; exit 1 }
$name = $args[0]
$path = ""${name}Suite.cs""
$content = ""namespace Mux.Core.Touchstone`n{`n    using System;`n    using System.Collections.Generic;`n`n    /// <summary>`n    /// TODO.`n    /// </summary>`n    public static class ${name}Suite`n    {`n        public static TestSuiteDescriptor Create()`n        {`n            // TODO: describe the suite.`n            return new TestSuiteDescriptor(`n                ""${name}"",`n                new List<TestCaseDescriptor>`n                {`n                    // TODO: replace this placeholder case.`n                    new TestCaseDescriptor(""placeholder"", ""TODO."")`n                });`n        }`n    }`n}`n""
Set-Content -Path $path -Value $content -NoNewline
Write-Output ""Created $path""
")
                },
                MuxRepositoryOnly);

            skills["new-skill"] = DefaultSkillBuilder.Build(
                "new-skill", "Scaffold a new skill", "Creates a new skill directory with starter SKILL.md content.", true, "scaffold",
                "The user wants to create a new skill with starter SKILL.md content.",
                new List<DefaultSkillCommandDef>
                {
                    new DefaultSkillCommandDef("create", "Create ./<id>/SKILL.md with starter frontmatter and a pwsh block.", "pwsh",
@"$ErrorActionPreference = 'Stop'
if ($args.Count -lt 1 -or [string]::IsNullOrWhiteSpace($args[0])) { Write-Error 'Usage: create <skill-id>'; exit 1 }
$id = $args[0]
$dir = Join-Path '.' $id
New-Item -ItemType Directory -Force -Path $dir | Out-Null
$path = Join-Path $dir 'SKILL.md'
$lines = New-Object System.Collections.Generic.List[string]
$lines.Add('---')
$lines.Add(""name: $id"")
$lines.Add('description: TODO describe when to use this skill.')
$lines.Add('mutating: false')
$lines.Add('tags: '''')
$lines.Add('commands:')
$lines.Add('  - name: run')
$lines.Add('    description: TODO describe this command.')
$lines.Add('    interpreter: pwsh')
$lines.Add('---')
$lines.Add('')
$lines.Add('## run')
$lines.Add('')
$lines.Add('Write-Output ''hello''')
$lines.Add('')
Set-Content -Path $path -Value ($lines -join ""`n"")
Write-Output ""Created $path""
")
                },
                null,
                @"Writing a good skill once `create` has made the folder:

- The description is the only part the model sees when choosing a skill, so make it count: the first sentence says what the skill does, the second starts with ""Use when"" and names the requests, keywords, or file types that should trigger it. Stay under 1024 characters, in the third person.
- Keep SKILL.md short (about 100 lines). Move long reference material into a `references` folder beside it and point to each file through the `${SKILL_DIR}` placeholder, one level deep.
- Add a command or a script when the step is deterministic (validation, formatting, file generation), the same code would otherwise be written again each time, or errors need explicit handling. Leave judgment in the prose.
- Gate the listing with `appliesTo` globs (and `requiresTools`) so the skill appears only in projects where it applies, and give it a `category`.
- Before finishing, check: triggers in the description, consistent terms, a concrete example, no dates or versions that will go stale, and `mux skill validate <id>` passes.");

            skills["doc-sync"] = DefaultSkillBuilder.Build(
                "doc-sync", "Check docs for stale file references", "Reports file paths mentioned in the docs that no longer exist.", false, "docs",
                "The user wants to verify that file paths mentioned in the docs still exist.",
                new List<DefaultSkillCommandDef>
                {
                    new DefaultSkillCommandDef("check", "Report backtick-quoted file paths in the docs that do not exist on disk.", "pwsh",
@"$ErrorActionPreference = 'Continue'
$docs = @('README.md', 'USAGE.md')
$missing = 0
foreach ($doc in $docs) {
    if (-not (Test-Path $doc)) { continue }
    $text = Get-Content -Path $doc -Raw
    $matches = [System.Text.RegularExpressions.Regex]::Matches($text, '`([^`]+?\.[A-Za-z0-9]+)`')
    foreach ($m in $matches) {
        $ref = $m.Groups[1].Value
        if ($ref -match '[/\\]' -and -not (Test-Path $ref)) {
            Write-Output ""$doc references missing path: $ref""
            $missing++
        }
    }
}
if ($missing -eq 0) { Write-Output 'No missing references found.' }
")
                });

            skills["api-surface-diff"] = DefaultSkillBuilder.Build(
                "api-surface-diff", "Diff the public API surface between two refs", "Shows added and removed public members between two git refs.", false, "docs, dotnet",
                "The user wants to see public API changes between two git refs.",
                new List<DefaultSkillCommandDef>
                {
                    new DefaultSkillCommandDef("diff", "Print added/removed lines containing 'public' between two refs.", "pwsh",
@"$ErrorActionPreference = 'Continue'
$a = 'HEAD~1'
$b = 'HEAD'
if ($args.Count -ge 1 -and -not [string]::IsNullOrWhiteSpace($args[0])) { $a = $args[0] }
if ($args.Count -ge 2 -and -not [string]::IsNullOrWhiteSpace($args[1])) { $b = $args[1] }
$diff = git diff $a $b -- '*.cs'
foreach ($line in $diff) {
    if ($line -match '^[+-]' -and $line -notmatch '^[+-][+-]' -and $line -match 'public') {
        Write-Output $line
    }
}
")
                });

            skills["adr-new"] = DefaultSkillBuilder.Build(
                "adr-new", "Create an architecture decision record", "Creates a new ADR from a template under docs/adr.", true, "docs",
                "The user wants to record an architecture decision.",
                new List<DefaultSkillCommandDef>
                {
                    new DefaultSkillCommandDef("create", "Create a new ADR from a template.", "pwsh",
@"$ErrorActionPreference = 'Stop'
if ($args.Count -lt 1) { Write-Error 'Usage: create <title>'; exit 1 }
$title = $args -join ' '
$dir = 'docs/adr'
New-Item -ItemType Directory -Force -Path $dir | Out-Null
$slug = ($title.ToLower() -replace '[^a-z0-9]+','-').Trim('-')
$path = Join-Path $dir ""$slug.md""
Set-Content -Path $path -Value ""# $title`n`nStatus: proposed`n`n## Context`n`n## Decision`n`n## Consequences`n""
Write-Output ""Created $path""
")
                });

            return skills;
        }
    }
}
