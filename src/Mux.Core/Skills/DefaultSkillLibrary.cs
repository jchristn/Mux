namespace Mux.Core.Skills
{
    using System;
    using System.Collections.Generic;
    using System.IO;

    /// <summary>
    /// The curated set of skills mux seeds into an empty skills directory on first run, so the feature is
    /// useful immediately and every default doubles as a worked example. Each entry is a complete, valid
    /// <c>SKILL.md</c>; <see cref="SeedInto"/> writes any that are missing without overwriting user edits.
    /// The git and .NET skills default to <c>pwsh</c> for cross-platform reach; a real machine needs the
    /// named interpreter installed to run them, but they always validate.
    /// </summary>
    public static class DefaultSkillLibrary
    {
        /// <summary>
        /// Returns the default skills as a map of id to <c>SKILL.md</c> content.
        /// </summary>
        /// <returns>The default skills, keyed by id.</returns>
        public static IReadOnlyDictionary<string, string> All()
        {
            Dictionary<string, string> skills = new Dictionary<string, string>(StringComparer.Ordinal);

            skills["git-status-vs-head"] = Skill(
                "git-status-vs-head", "Compare working tree to GitHub HEAD", false, "git, github, vcs",
                "The user asks how local changes compare to GitHub, or wants a pre-flight check before committing or pushing.",
                "pwsh",
                "$ErrorActionPreference = 'Stop'\n" +
                "git fetch origin --quiet\n" +
                "$branch = (git rev-parse --abbrev-ref HEAD).Trim()\n" +
                "$base = ((git rev-parse --abbrev-ref origin/HEAD) -replace '^origin/', '').Trim()\n" +
                "Write-Output \"Branch: $branch    Base: origin/$base\"\n" +
                "Write-Output '--- working tree ---'\n" +
                "git status --short\n" +
                "Write-Output '--- ahead/behind origin/HEAD (left=behind right=ahead) ---'\n" +
                "git rev-list --left-right --count \"origin/$base...HEAD\"\n");

            skills["git-commit"] = Skill(
                "git-commit", "Commit staged and tracked changes", true, "git, vcs",
                "The user wants to commit; refuses to run on the default branch so work lands on a feature branch.",
                "pwsh",
                "$ErrorActionPreference = 'Stop'\n" +
                "$branch = (git rev-parse --abbrev-ref HEAD).Trim()\n" +
                "$default = ((git rev-parse --abbrev-ref origin/HEAD) -replace '^origin/', '').Trim()\n" +
                "if ($branch -eq $default -or $branch -eq 'main' -or $branch -eq 'master') {\n" +
                "  Write-Error \"Refusing to commit on the default branch '$branch'. Create a feature branch first.\"; exit 1\n" +
                "}\n" +
                "$msg = if ($args.Count -gt 0) { $args -join ' ' } else { 'chore: update' }\n" +
                "git add -A\n" +
                "git commit -m $msg\n");

            skills["git-push"] = Skill(
                "git-push", "Push the current branch", true, "git, github, vcs",
                "The user wants to push the current branch to origin and set upstream.",
                "pwsh",
                "$ErrorActionPreference = 'Stop'\n" +
                "$branch = (git rev-parse --abbrev-ref HEAD).Trim()\n" +
                "git push --set-upstream origin $branch\n");

            skills["git-secret-scan"] = Skill(
                "git-secret-scan", "Scan the staged diff for secrets", false, "git, security",
                "Before a commit leaves the machine, or when the user asks to check for leaked credentials in staged changes.",
                "pwsh",
                "$ErrorActionPreference = 'Stop'\n" +
                "$diff = git diff --cached\n" +
                "$patterns = @('AKIA[0-9A-Z]{16}', '-----BEGIN [A-Z ]*PRIVATE KEY-----', 'xox[baprs]-[0-9A-Za-z-]+', 'ghp_[0-9A-Za-z]{36}', 'password\\s*=\\s*[^\\s]+')\n" +
                "$hits = 0\n" +
                "foreach ($p in $patterns) {\n" +
                "  $m = [regex]::Matches($diff, $p)\n" +
                "  if ($m.Count -gt 0) { Write-Output \"Potential secret ($p): $($m.Count) match(es)\"; $hits += $m.Count }\n" +
                "}\n" +
                "if ($hits -eq 0) { Write-Output 'No obvious secrets found in the staged diff.' } else { exit 1 }\n");

            skills["dotnet-build"] = Skill(
                "dotnet-build", "Build the .NET solution", true, "dotnet, build",
                "The user wants to build the current .NET project or solution and see errors and warnings.",
                "pwsh",
                "$ErrorActionPreference = 'Stop'\n" +
                "dotnet build --nologo\n");

            skills["dotnet-test"] = Skill(
                "dotnet-test", "Run the .NET test suite", true, "dotnet, test",
                "The user wants to run the test suite for the current .NET project or solution.",
                "pwsh",
                "$ErrorActionPreference = 'Stop'\n" +
                "if ($args.Count -gt 0) { dotnet test --nologo --filter ($args -join ' ') } else { dotnet test --nologo }\n");

            skills["todo-scan"] = Skill(
                "todo-scan", "Find TODO and FIXME markers", false, "hygiene",
                "The user wants an inventory of TODO, FIXME, or HACK markers in the codebase.",
                "pwsh",
                "$ErrorActionPreference = 'Stop'\n" +
                "$found = 0\n" +
                "Get-ChildItem -Recurse -File -Include *.cs,*.ts,*.js,*.py,*.go,*.md -ErrorAction SilentlyContinue |\n" +
                "  Where-Object { $_.FullName -notmatch '[\\\\/](bin|obj|node_modules|\\.git)[\\\\/]' } |\n" +
                "  ForEach-Object {\n" +
                "    Select-String -Path $_.FullName -Pattern 'TODO|FIXME|HACK' -ErrorAction SilentlyContinue |\n" +
                "      ForEach-Object { Write-Output \"$($_.Path):$($_.LineNumber): $($_.Line.Trim())\"; $script:found++ }\n" +
                "  }\n" +
                "if ($found -eq 0) { Write-Output 'No TODO/FIXME/HACK markers found.' }\n");

            skills["gitignore-audit"] = Skill(
                "gitignore-audit", "Audit for tracked junk", false, "hygiene, git",
                "The user wants to find tracked files that look like build output or dependencies and should probably be ignored.",
                "pwsh",
                "$ErrorActionPreference = 'Stop'\n" +
                "$tracked = git ls-files\n" +
                "$suspect = $tracked | Where-Object { $_ -match '[\\\\/](bin|obj|node_modules|dist|build)[\\\\/]' -or $_ -match '\\.(dll|exe|pdb|log)$' }\n" +
                "if ($suspect) { Write-Output 'Tracked files that may belong in .gitignore:'; $suspect | ForEach-Object { Write-Output \"  $_\" } }\n" +
                "else { Write-Output 'No obviously ignorable files are tracked.' }\n");

            skills["env-report"] = Skill(
                "env-report", "Report the tooling environment", false, "workflow",
                "The user wants a quick dump of the OS and installed tool versions for a bug report or setup check.",
                "pwsh",
                "Write-Output \"OS: $([System.Runtime.InteropServices.RuntimeInformation]::OSDescription)\"\n" +
                "foreach ($tool in 'git','dotnet','node','python','pwsh') {\n" +
                "  try { $v = (& $tool --version 2>$null | Select-Object -First 1); Write-Output \"$tool: $v\" }\n" +
                "  catch { Write-Output \"$tool: (not found)\" }\n" +
                "}\n");

            skills["json-validate"] = Skill(
                "json-validate", "Validate a JSON file", false, "utility",
                "The user wants to check whether a JSON file is well-formed and see the first error if not.",
                "node",
                "const fs = require('fs');\n" +
                "const path = process.argv[2];\n" +
                "if (!path) { console.error('Usage: run_skill json-validate check --arg <file>'); process.exit(1); }\n" +
                "try { JSON.parse(fs.readFileSync(path, 'utf8')); console.log('Valid JSON: ' + path); }\n" +
                "catch (e) { console.error('Invalid JSON in ' + path + ': ' + e.message); process.exit(1); }\n");

            return skills;
        }

        /// <summary>
        /// Writes any default skill whose directory does not already exist into <paramref name="skillsDirectory"/>.
        /// Existing skills are left untouched, so user edits and removals survive.
        /// </summary>
        /// <param name="skillsDirectory">The skills directory. Must not be null.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="skillsDirectory"/> is null.</exception>
        public static void SeedInto(string skillsDirectory)
        {
            if (skillsDirectory == null) throw new ArgumentNullException(nameof(skillsDirectory));

            Directory.CreateDirectory(skillsDirectory);
            foreach (KeyValuePair<string, string> skill in All())
            {
                string dir = Path.Combine(skillsDirectory, skill.Key);
                if (Directory.Exists(dir))
                {
                    continue;
                }

                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "SKILL.md"), skill.Value);
            }
        }

        private static string Skill(string id, string title, bool mutating, string tags, string whenToUse, string interpreter, string blockBody)
        {
            string fenceLanguage = interpreter == "node" ? "js" : interpreter;
            return "---\n"
                + "name: " + id + "\n"
                + "title: " + title + "\n"
                + "description: " + title + ".\n"
                + "version: 1.0.0\n"
                + "mutating: " + (mutating ? "true" : "false") + "\n"
                + "whenToUse: " + whenToUse + "\n"
                + "tags: [" + tags + "]\n"
                + "commands:\n"
                + "  - name: run\n"
                + "    description: " + title + ".\n"
                + "    block: run\n"
                + "    interpreter: " + interpreter + "\n"
                + "---\n\n"
                + "## " + title + "\n\n"
                + whenToUse + "\n\n"
                + "Call `run_skill` with command `run`" + (interpreter == "node" ? " and `--arg <file>` where noted" : string.Empty) + ".\n\n"
                + "```" + fenceLanguage + " id=run\n"
                + blockBody
                + "```\n";
        }
    }
}
