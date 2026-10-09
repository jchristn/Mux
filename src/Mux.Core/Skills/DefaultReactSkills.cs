namespace Mux.Core.Skills
{
    using System.Collections.Generic;

    /// <summary>
    /// The React default skills. They reuse the JavaScript detection (package manager, test runner) and exit 2 when
    /// the project does not depend on <c>react</c>. Scaffolding commands follow the project's file extension
    /// (<c>.tsx</c> when tsconfig.json exists, otherwise <c>.jsx</c>) and its test setup, and never overwrite a file.
    /// </summary>
    public static class DefaultReactSkills
    {
        #region Private-Members

        private static readonly List<string> _AppliesTo = new List<string> { "package.json" };

        private const string Setup = @"$dir = Get-MuxNodeProject
$pm = Get-MuxNodePackageManager -Dir $dir
$pkg = Get-MuxPackageJson -Dir $dir
Assert-MuxReact $pkg
Set-Location -LiteralPath $dir
$ext = if (Test-Path -LiteralPath (Join-Path $dir 'tsconfig.json')) { 'tsx' } else { 'jsx' }
$runner = Get-MuxJsTestRunner $pkg
$hasTestingLibrary = Test-MuxPackageDependency $pkg '@testing-library/react'
$srcRoot = if (Test-Path -LiteralPath (Join-Path $dir 'src')) { 'src' } else { '.' }
";

        private const string DevServerBody = @"Procedure:

1. Run `detect`. It prints the dev command, the directory to run it in, the framework, the expected URL, and a ready pattern.
2. Check `process_list` first; if a dev server for this project is already running, reuse it instead of starting another.
3. Start it in the background with `process_start`: `command` = the dev command, `working_directory` = the printed directory, `name` = `web`, `wait_for` = the ready pattern, `timeout_ms` = 120000.
4. If the result says `ready`, report the URL from `match` (or the first http:// URL in the output). Say that it keeps running in the background and that `/processes` lists it.
5. If it exited or timed out, read the output (`process_output`) and diagnose: a port already in use (offer to stop the other process or use another port), a missing dependency (suggest `js-install`), or a compile error (show the file and line).
6. Stop it with `process_stop` when the user is done, or before starting it again with different settings. Never start a second copy on the same port.
";

        #endregion

        #region Public-Methods

        /// <summary>Returns the React skill definitions.</summary>
        /// <returns>The definitions.</returns>
        public static IReadOnlyList<DefaultSkillDef> All()
        {
            return new List<DefaultSkillDef>
            {
                Skill("react-new-component", "Scaffold a React component", "Creates a function component and, when the project has a test setup, a test beside it.", true,
                    "The user asks for a new React component.",
                    "<Name> [directory]",
                    "`create <Name> [directory]` writes `<Name>.tsx` (or `.jsx`) under `src/components` by default, plus `<Name>.test.tsx` when the project uses Vitest or Jest with Testing Library. Names must be PascalCase. Existing files are never overwritten; the command exits 1 instead.",
                    Command("create", "Create the component (and its test).", @"$name = Get-MuxArg -Arguments $args -Index 0
if ($name -cnotmatch '^[A-Z][A-Za-z0-9]*$') { Exit-MuxNotApplicable 'pass a PascalCase component name: react-new-component create <Name> [directory]' }
$target = Get-MuxArg -Arguments $args -Index 1 -Default (Join-Path $srcRoot 'components')
if ($ext -eq 'tsx') {
$component = @'
import type { ReactNode } from 'react';

export interface __NAME__Props {
  children?: ReactNode;
}

export function __NAME__({ children }: __NAME__Props) {
  return <div>{children}</div>;
}

export default __NAME__;
'@
} else {
$component = @'
export function __NAME__({ children }) {
  return <div>{children}</div>;
}

export default __NAME__;
'@
}
New-MuxFile -Path (Join-Path $target ($name + '.' + $ext)) -Content ($component -replace '__NAME__', $name)
if ($hasTestingLibrary -and ($runner -eq 'vitest' -or $runner -eq 'jest')) {
    $imports = if ($runner -eq 'vitest') { ""import { describe, expect, it } from 'vitest';`n"" } else { '' }
$test = @'
__IMPORTS__import { render, screen } from '@testing-library/react';
import { __NAME__ } from './__NAME__';

describe('__NAME__', () => {
  it('renders its children', () => {
    render(<__NAME__>hello</__NAME__>);
    expect(screen.getByText('hello')).toBeTruthy();
  });
});
'@
    New-MuxFile -Path (Join-Path $target ($name + '.test.' + $ext)) -Content (($test -replace '__IMPORTS__', $imports) -replace '__NAME__', $name)
} else {
    Write-Output 'mux: no Vitest or Jest with @testing-library/react; skipped the test file.'
}
")),

                Skill("react-new-hook", "Scaffold a React hook", "Creates a custom hook and, when the project has a test setup, a test beside it.", true,
                    "The user asks for a new custom React hook.",
                    "<useName> [directory]",
                    "`create <useName> [directory]` writes the hook under `src/hooks` by default. Names must start with `use` followed by a capital letter. Existing files are never overwritten.",
                    Command("create", "Create the hook (and its test).", @"$name = Get-MuxArg -Arguments $args -Index 0
if ($name -cnotmatch '^use[A-Z][A-Za-z0-9]*$') { Exit-MuxNotApplicable 'pass a hook name like useThing: react-new-hook create <useName> [directory]' }
$target = Get-MuxArg -Arguments $args -Index 1 -Default (Join-Path $srcRoot 'hooks')
$hookExt = if ($ext -eq 'tsx') { 'ts' } else { 'js' }
$typeArg = if ($ext -eq 'tsx') { '<unknown>' } else { '' }
$hook = @'
import { useEffect, useState } from 'react';

export function __NAME__() {
  const [value, setValue] = useState__TYPE__(null);

  useEffect(() => {
    // Subscribe or fetch here, and return a cleanup function when one is needed.
  }, []);

  return { value, setValue };
}

export default __NAME__;
'@
New-MuxFile -Path (Join-Path $target ($name + '.' + $hookExt)) -Content (($hook -replace '__TYPE__', $typeArg) -replace '__NAME__', $name)
if ($hasTestingLibrary -and ($runner -eq 'vitest' -or $runner -eq 'jest')) {
    $imports = if ($runner -eq 'vitest') { ""import { describe, expect, it } from 'vitest';`n"" } else { '' }
$test = @'
__IMPORTS__import { renderHook } from '@testing-library/react';
import { __NAME__ } from './__NAME__';

describe('__NAME__', () => {
  it('starts with no value', () => {
    const { result } = renderHook(() => __NAME__());
    expect(result.current.value).toBeNull();
  });
});
'@
    New-MuxFile -Path (Join-Path $target ($name + '.test.' + $hookExt)) -Content (($test -replace '__IMPORTS__', $imports) -replace '__NAME__', $name)
}
")),

                Skill("react-test", "Run one React component's tests", "Runs the tests for a single component or hook by name.", false,
                    "The user changed one component and wants only its tests.",
                    "<ComponentName>",
                    "`component <Name>` runs the detected runner filtered to test files matching the name. Use `js-test all` for the full suite.",
                    Command("component", "Run tests matching a component or hook name.", @"$name = Get-MuxArg -Arguments $args -Index 0
if (-not $name) { Exit-MuxNotApplicable 'pass a component name: react-test component <Name>' }
Invoke-MuxJsTestRunner -Manager $pm -Package $pkg -Mode 'filter' -Filter $name
")),

                Skill("react-build-analyze", "Analyze the React production bundle", "Builds the app and lists the largest emitted JavaScript and CSS files.", false,
                    "The user asks why the bundle is large or wants to see what a build produces.",
                    string.Empty,
                    "`report` runs the `build` script, then lists the 15 largest .js and .css files under dist/, build/, or out/ with their sizes and the total.",
                    Command("report", "Build, then list the largest output files.", @"if (-not (Test-MuxPackageScript $pkg 'build')) { Exit-MuxNotApplicable 'package.json has no build script.' }
Invoke-MuxPackageScript -Manager $pm -Script 'build'
if (Test-MuxDryRun) { Write-Output 'DRYRUN: list the largest files in dist/, build/, or out/'; exit 0 }
$output = @('dist', 'build', 'out') | Where-Object { Test-Path -LiteralPath (Join-Path $dir $_) } | Select-Object -First 1
if (-not $output) { Exit-MuxNotApplicable 'the build produced no dist/, build/, or out/ directory.' }
$assets = @(Get-ChildItem -LiteralPath (Join-Path $dir $output) -Recurse -File -Include *.js, *.mjs, *.css -ErrorAction SilentlyContinue | Sort-Object Length -Descending)
$total = ($assets | Measure-Object Length -Sum).Sum
Write-Output ('Output: ' + $output + ' (' + $assets.Count + ' files, ' + [Math]::Round($total / 1KB, 1) + ' KB total)')
foreach ($file in $assets | Select-Object -First 15) {
    Write-Output (('{0,10:N1} KB  ' -f ($file.Length / 1KB)) + [IO.Path]::GetRelativePath($dir, $file.FullName))
}
")),

                Skill("react-lint-hooks", "Check React hook and accessibility rules", "Reports only react-hooks and jsx-a11y ESLint findings.", false,
                    "After editing React components, to catch broken hook dependencies or accessibility problems without the noise of every lint rule.",
                    string.Empty,
                    "`check` runs ESLint and keeps only `react-hooks/` and `jsx-a11y/` findings, exiting 1 when there are any. It needs eslint-plugin-react-hooks or eslint-plugin-jsx-a11y in the project and says which is missing otherwise.",
                    Command("check", "Report react-hooks and jsx-a11y findings.", @"$plugins = @('eslint-plugin-react-hooks', 'eslint-plugin-jsx-a11y') | Where-Object { Test-MuxPackageDependency $pkg $_ }
if ($plugins.Count -eq 0) { Exit-MuxNotApplicable 'neither eslint-plugin-react-hooks nor eslint-plugin-jsx-a11y is installed.' }
Write-Output ('Rules from: ' + ($plugins -join ', '))
$command = Get-MuxPackageBinCommand -Manager $pm -Bin 'eslint' -Arguments @('.', '--format', 'unix')
if (Test-MuxDryRun) { Write-Output ('DRYRUN: ' + (Format-MuxCommand -Tool $command.Tool -Arguments $command.Arguments)); exit 0 }
if (-not (Test-MuxTool $command.Tool)) { Exit-MuxNotApplicable ($command.Tool + ' was not found on PATH.') }
$findings = @(& $command.Tool @($command.Arguments) 2>&1 | Where-Object { $_ -match '(react-hooks|jsx-a11y)/' })
if ($findings.Count -eq 0) { Write-Output 'No react-hooks or jsx-a11y findings.'; exit 0 }
$findings | ForEach-Object { Write-Output $_ }
exit 1
")),

                Skill("react-dev-server", "Start the React dev server", "Detects the dev script, framework, port, and ready line, so the dev server can be started in the background and its URL reported.", false,
                    "The user wants to run the app locally, see a change in the browser, or test against the running dev server.",
                    string.Empty,
                    DevServerBody,
                    Command("detect", "Print the dev command, working directory, framework, expected URL, and the ready-line pattern.", @"$script = ''
foreach ($candidate in @('dev', 'start', 'serve')) { if (Test-MuxPackageScript $pkg $candidate) { $script = $candidate; break } }
if (-not $script) { Exit-MuxNotApplicable 'package.json has no dev, start, or serve script.' }
$scriptText = [string]$pkg['scripts'][$script]
$framework = 'unknown'
$port = 0
foreach ($pair in @(@('next', 'Next.js', 3000), @('vite', 'Vite', 5173), @('react-scripts', 'Create React App', 3000), @('astro', 'Astro', 4321), @('gatsby', 'Gatsby', 8000), @('parcel', 'Parcel', 1234), @('webpack-dev-server', 'webpack-dev-server', 8080))) {
    if ((Test-MuxPackageDependency $pkg $pair[0]) -or $scriptText -match ('(^|\s|/)' + [regex]::Escape($pair[0]) + '(\s|$)')) { $framework = $pair[1]; $port = [int]$pair[2]; break }
}
if ($scriptText -match '(--port|-p)[ =](\d+)') { $port = [int]$Matches[2] }
elseif ($scriptText -match 'PORT=(\d+)') { $port = [int]$Matches[1] }
$command = if ($pm -eq 'npm') { 'npm run ' + $script } else { $pm + ' run ' + $script }
Write-Output ('Dev command: ' + $command)
Write-Output ('Working directory: ' + $dir)
Write-Output ('Script: ' + $script + ' = ' + $scriptText)
Write-Output ('Framework: ' + $framework)
if ($port -gt 0) { Write-Output ('Expected URL: http://localhost:' + $port) } else { Write-Output 'Expected URL: unknown (read it from the output)' }
Write-Output 'Ready pattern: https?://(localhost|127\.0\.0\.1|0\.0\.0\.0|\[::1?\])(:\d+)?\S*'
Write-Output 'Failure pattern: (EADDRINUSE|address already in use|Port \d+ is in use|Failed to compile|ERR!|error)'
")),

                Skill("react-upgrade-check", "Check React versions and peer conflicts", "Reports the declared React-related versions and what the package manager actually installed.", false,
                    "Before or after upgrading React, or when React reports duplicate copies or invalid hook calls.",
                    string.Empty,
                    "`report` lists the declared versions of react, react-dom, their @types packages, and common frameworks, then asks the package manager what is installed. A nonzero exit from that listing usually means peer-dependency conflicts or duplicate copies of React.",
                    Command("report", "List declared and installed React versions.", @"foreach ($name in @('react', 'react-dom', '@types/react', '@types/react-dom', 'next', 'vite', 'react-scripts', '@vitejs/plugin-react')) {
    foreach ($section in @('dependencies', 'devDependencies', 'peerDependencies')) {
        if ($pkg.ContainsKey($section) -and $null -ne $pkg[$section] -and $pkg[$section].ContainsKey($name)) { Write-Output ('declared ' + $name + ' ' + $pkg[$section][$name] + ' (' + $section + ')') }
    }
}
switch ($pm) {
    'yarn' { Invoke-MuxTool -Tool 'yarn' -Arguments @('why', 'react') -AllowFailure }
    'bun' { Invoke-MuxTool -Tool 'bun' -Arguments @('pm', 'ls') -AllowFailure }
    default { Invoke-MuxTool -Tool $pm -Arguments @('ls', 'react', 'react-dom') -AllowFailure }
}
if ($script:MuxLastExit -ne 0) { Write-Output 'mux: the package manager reported problems (often peer conflicts or duplicate React copies).'; exit 1 }
"))
            };
        }

        #endregion

        #region Private-Methods

        private static DefaultSkillDef Skill(string id, string title, string description, bool mutating, string whenToUse, string argumentHint, string body, params DefaultSkillCommandDef[] commands)
        {
            List<DefaultSkillCommandDef> withSetup = new List<DefaultSkillCommandDef>();
            foreach (DefaultSkillCommandDef command in commands)
            {
                withSetup.Add(new DefaultSkillCommandDef(command.Name, command.Description, command.Interpreter, Setup + command.Code));
            }

            return DefaultSkillHelpers.Attach(new DefaultSkillDef
            {
                Id = id,
                Title = title,
                Description = description,
                Mutating = mutating,
                Tags = new List<string> { "react", "javascript", "typescript" },
                WhenToUse = whenToUse,
                AppliesTo = new List<string>(_AppliesTo),
                ArgumentHint = argumentHint,
                Body = body + " Exit codes: 0 success, 1 the tool reported problems, 2 the tool or project is missing (including projects that do not use React).",
                Commands = withSetup
            });
        }

        private static DefaultSkillCommandDef Command(string name, string description, string code)
        {
            return new DefaultSkillCommandDef(name, description, "pwsh", code);
        }

        #endregion
    }
}
