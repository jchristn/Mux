namespace Mux.Core.Skills
{
    using System.Collections.Generic;

    /// <summary>
    /// Frontend framework skills beyond React: <c>web-framework</c> detects Next.js, Nuxt, SvelteKit, Angular, Astro,
    /// Remix, Svelte, Vue, or Vite and maps build, lint, test, dev, and upgrade checks onto the project's scripts or the
    /// framework's CLI; <c>storybook</c> builds and tests stories. Dev servers are described for process_start rather
    /// than started here, the same way react-dev-server works.
    /// </summary>
    public static class DefaultWebFrameworkSkills
    {
        #region Private-Members

        private const string WebSetup = @"$dir = Get-MuxNodeProject
Set-Location -LiteralPath $dir
$pkg = Get-MuxPackageJson $dir
$pm = Get-MuxNodePackageManager $dir
$fw = $null
foreach ($candidate in @(
        @{ Package = 'next'; Name = 'Next.js'; Cli = 'next'; Port = 3000; Build = @('build'); Dev = @('dev'); Lint = @('lint') },
        @{ Package = 'nuxt'; Name = 'Nuxt'; Cli = 'nuxt'; Port = 3000; Build = @('build'); Dev = @('dev'); Lint = $null },
        @{ Package = '@sveltejs/kit'; Name = 'SvelteKit'; Cli = 'vite'; Port = 5173; Build = @('build'); Dev = @('dev'); Lint = $null },
        @{ Package = '@angular/core'; Name = 'Angular'; Cli = 'ng'; Port = 4200; Build = @('build'); Dev = @('serve'); Lint = @('lint') },
        @{ Package = 'astro'; Name = 'Astro'; Cli = 'astro'; Port = 4321; Build = @('build'); Dev = @('dev'); Lint = @('check') },
        @{ Package = '@remix-run/dev'; Name = 'Remix'; Cli = 'remix'; Port = 3000; Build = @('vite:build'); Dev = @('vite:dev'); Lint = $null },
        @{ Package = 'svelte'; Name = 'Svelte'; Cli = 'vite'; Port = 5173; Build = @('build'); Dev = @('dev'); Lint = $null },
        @{ Package = 'vue'; Name = 'Vue'; Cli = 'vite'; Port = 5173; Build = @('build'); Dev = @('dev'); Lint = $null },
        @{ Package = 'vite'; Name = 'Vite'; Cli = 'vite'; Port = 5173; Build = @('build'); Dev = @('dev'); Lint = $null })) {
    if (Test-MuxPackageDependency $pkg $candidate.Package) { $fw = $candidate; break }
}
if (-not $fw) { Exit-MuxNotApplicable 'no supported web framework in package.json (Next.js, Nuxt, SvelteKit, Angular, Astro, Remix, Svelte, Vue, or Vite). For other React apps use the react-* skills.' }
function Get-MuxDeclaredVersion {
    param([string]$Name)
    foreach ($section in @('dependencies', 'devDependencies', 'peerDependencies')) {
        if ($pkg.ContainsKey($section) -and $null -ne $pkg[$section] -and $pkg[$section].ContainsKey($Name)) { return [string]$pkg[$section][$Name] }
    }
    return $null
}
";

        private const string StorybookSetup = @"$dir = Get-MuxNodeProject
Set-Location -LiteralPath $dir
$pkg = Get-MuxPackageJson $dir
$pm = Get-MuxNodePackageManager $dir
if (-not (Test-Path -LiteralPath (Join-Path $dir '.storybook') -PathType Container) -and -not (Test-MuxPackageDependency $pkg 'storybook')) { Exit-MuxNotApplicable 'no .storybook folder or storybook dependency; this project does not use Storybook.' }
";

        #endregion

        #region Public-Methods

        /// <summary>Returns the web framework skill definitions.</summary>
        /// <returns>The definitions.</returns>
        public static IReadOnlyList<DefaultSkillDef> All()
        {
            ToolchainSkillFactory web = new ToolchainSkillFactory(WebSetup, new[] { "frontend", "javascript" }, new[] { "package.json" }, null);
            ToolchainSkillFactory storybook = new ToolchainSkillFactory(StorybookSetup, new[] { "frontend", "javascript" }, new[] { ".storybook/main.*" }, null);
            return new List<DefaultSkillDef>
            {
                web.Skill("web-framework", "Build, lint, and test a web framework app",
                    "Next.js, Nuxt, SvelteKit, Angular, Astro, Remix, Svelte, Vue, and Vite apps: detects the framework, then builds, lints, tests, describes the dev server, or compares declared and installed versions.",
                    false,
                    "The user works on a Next.js, Nuxt, SvelteKit, Angular, Astro, Remix, Svelte, Vue, or Vite app and asks to build, lint, test, run the dev server, or check versions.",
                    string.Empty,
                    "`detect` names the framework, package manager, and scripts. `build`, `lint`, and `test` run the project's own script when there is one, otherwise the framework's tool: `next build`/`next lint`, `nuxt build`, `vite build`, `ng build`/`ng lint`/`ng test --watch=false`, `astro build`/`astro check`, svelte-check, vue-tsc, eslint, or `vitest run` (tests run with CI=true so watch mode stays off). `dev` prints the dev command, expected URL, and ready pattern to pass to process_start. `upgrade-check` lists declared and installed versions of the framework and its companions. Plain React apps without one of these frameworks use the react-* skills.",
                    ToolchainSkillFactory.Command("detect", "Name the framework, package manager, and scripts.", @"Write-Output ('Framework: ' + $fw.Name + ' (' + $fw.Package + ' ' + (Get-MuxDeclaredVersion $fw.Package) + ')')
Write-Output ('Package manager: ' + $pm)
Write-Output ('Working directory: ' + $dir)
foreach ($name in @('dev', 'build', 'lint', 'test', 'preview')) {
    if (Test-MuxPackageScript $pkg $name) { Write-Output ('Script ' + $name + ': ' + [string]$pkg['scripts'][$name]) }
}
"),
                    ToolchainSkillFactory.Command("build", "Build the app.", @"if (Test-MuxPackageScript $pkg 'build') { Invoke-MuxPackageScript -Manager $pm -Script 'build' }
else { Invoke-MuxPackageBin -Manager $pm -Bin $fw.Cli -Arguments $fw.Build }
"),
                    ToolchainSkillFactory.Command("lint", "Lint or type-check the app.", @"if (Test-MuxPackageScript $pkg 'lint') { Invoke-MuxPackageScript -Manager $pm -Script 'lint'; exit 0 }
if ($fw.Lint) { Invoke-MuxPackageBin -Manager $pm -Bin $fw.Cli -Arguments $fw.Lint; exit 0 }
if (Test-MuxPackageDependency $pkg 'svelte-check') { Invoke-MuxPackageBin -Manager $pm -Bin 'svelte-check'; exit 0 }
if (Test-MuxPackageDependency $pkg 'vue-tsc') { Invoke-MuxPackageBin -Manager $pm -Bin 'vue-tsc' -Arguments @('--noEmit'); exit 0 }
if (Test-MuxPackageDependency $pkg 'eslint') { Invoke-MuxPackageBin -Manager $pm -Bin 'eslint' -Arguments @('.'); exit 0 }
Exit-MuxNotApplicable ('no lint script and no linter for ' + $fw.Name + ' (add a lint script, eslint, svelte-check, or vue-tsc).')
"),
                    ToolchainSkillFactory.Command("test", "Run the tests once, without watch mode.", @"$env:CI = 'true'
if (Test-MuxPackageScript $pkg 'test') { Invoke-MuxPackageScript -Manager $pm -Script 'test'; exit 0 }
if ($fw.Package -eq '@angular/core') { Invoke-MuxPackageBin -Manager $pm -Bin 'ng' -Arguments @('test', '--watch=false'); exit 0 }
if (Test-MuxPackageDependency $pkg 'vitest') { Invoke-MuxPackageBin -Manager $pm -Bin 'vitest' -Arguments @('run'); exit 0 }
if (Test-MuxPackageDependency $pkg '@playwright/test') { Invoke-MuxPackageBin -Manager $pm -Bin 'playwright' -Arguments @('test'); exit 0 }
Exit-MuxNotApplicable ('no test script and no test runner for ' + $fw.Name + ' (add a test script or vitest).')
"),
                    ToolchainSkillFactory.Command("dev", "Describe the dev server for process_start.", @"$script = ''
foreach ($candidate in @('dev', 'start', 'serve')) { if (Test-MuxPackageScript $pkg $candidate) { $script = $candidate; break } }
$port = [int]$fw.Port
if ($script) {
    $text = [string]$pkg['scripts'][$script]
    if ($text -match '(--port|-p)[ =](\d+)') { $port = [int]$Matches[2] }
    $command = if ($pm -eq 'npm') { 'npm run ' + $script } else { $pm + ' run ' + $script }
} else {
    $prefix = switch ($pm) { 'pnpm' { @('pnpm', 'exec') } 'yarn' { @('yarn') } 'bun' { @('bunx') } default { @('npx') } }
    $command = Format-MuxCommand -Tool $prefix[0] -Arguments (@($prefix | Select-Object -Skip 1) + @($fw.Cli) + $fw.Dev)
}
Write-Output ('Dev command: ' + $command)
Write-Output ('Working directory: ' + $dir)
Write-Output ('Framework: ' + $fw.Name)
Write-Output ('Expected URL: http://localhost:' + $port)
Write-Output 'Ready pattern: https?://(localhost|127\.0\.0\.1|0\.0\.0\.0|\[::1?\])(:\d+)?\S*'
Write-Output 'Failure pattern: (EADDRINUSE|address already in use|Port \d+ is in use|ERR!|error)'
Write-Output 'Start it with process_start using the dev command and ready pattern above.'
"),
                    ToolchainSkillFactory.Command("upgrade-check", "Compare declared and installed framework versions.", @"$names = @($fw.Package) + @(switch ($fw.Package) {
        'next' { 'react', 'react-dom', 'eslint-config-next' }
        'nuxt' { 'vue' }
        '@sveltejs/kit' { 'svelte', 'vite', '@sveltejs/vite-plugin-svelte' }
        '@angular/core' { '@angular/cli', '@angular/compiler', 'typescript', 'zone.js' }
        'astro' { 'typescript' }
        'svelte' { 'vite', '@sveltejs/vite-plugin-svelte' }
        'vue' { 'vite', '@vitejs/plugin-vue', 'vue-router' }
        default { 'typescript' }
    })
Write-Output ('PACKAGE'.PadRight(32) + 'DECLARED'.PadRight(16) + 'INSTALLED')
foreach ($name in $names) {
    $declared = Get-MuxDeclaredVersion $name
    if (-not $declared) { continue }
    $installed = '(not installed)'
    $manifest = Join-Path (Join-Path $dir 'node_modules') (Join-Path $name 'package.json')
    if (Test-Path -LiteralPath $manifest) { try { $installed = [string]((Get-Content -LiteralPath $manifest -Raw | ConvertFrom-Json).version) } catch { } }
    Write-Output ($name.PadRight(32) + $declared.PadRight(16) + $installed)
}
Write-Output ('Compare with the latest ' + $fw.Name + ' release notes before upgrading; js-deps outdated lists newer versions.')
")),

                storybook.Skill("storybook", "Build and test Storybook",
                    "Storybook: builds the static Storybook, runs the story tests, or describes the Storybook dev server.",
                    false,
                    "The user asks to build Storybook, test stories, or run the Storybook dev server.",
                    string.Empty,
                    "`build` runs the build-storybook script or `storybook build`. `test` runs the test-storybook script, @storybook/test-runner, or the Storybook Vitest project (the test runner needs Storybook running; start it with `dev` first). `dev` prints the command, URL (port 6006), and ready pattern for process_start.",
                    ToolchainSkillFactory.Command("build", "Build the static Storybook.", @"if (Test-MuxPackageScript $pkg 'build-storybook') { Invoke-MuxPackageScript -Manager $pm -Script 'build-storybook' }
else { Invoke-MuxPackageBin -Manager $pm -Bin 'storybook' -Arguments @('build') }
"),
                    ToolchainSkillFactory.Command("test", "Run the story tests.", @"$env:CI = 'true'
if (Test-MuxPackageScript $pkg 'test-storybook') { Invoke-MuxPackageScript -Manager $pm -Script 'test-storybook'; exit 0 }
if (Test-MuxPackageDependency $pkg '@storybook/test-runner') { Invoke-MuxPackageBin -Manager $pm -Bin 'test-storybook'; exit 0 }
if (Test-MuxPackageDependency $pkg '@storybook/addon-vitest') { Invoke-MuxPackageBin -Manager $pm -Bin 'vitest' -Arguments @('run', '--project=storybook'); exit 0 }
Exit-MuxNotApplicable 'no test-storybook script, @storybook/test-runner, or @storybook/addon-vitest; story tests are not set up.'
"),
                    ToolchainSkillFactory.Command("dev", "Describe the Storybook dev server for process_start.", @"$command = if (Test-MuxPackageScript $pkg 'storybook') { $(if ($pm -eq 'npm') { 'npm run storybook' } else { $pm + ' run storybook' }) } else { $(switch ($pm) { 'pnpm' { 'pnpm exec storybook dev -p 6006' } 'yarn' { 'yarn storybook dev -p 6006' } 'bun' { 'bunx storybook dev -p 6006' } default { 'npx storybook dev -p 6006' } }) }
Write-Output ('Dev command: ' + $command)
Write-Output ('Working directory: ' + $dir)
Write-Output 'Expected URL: http://localhost:6006'
Write-Output 'Ready pattern: https?://(localhost|127\.0\.0\.1)(:\d+)?\S*'
Write-Output 'Start it with process_start using the dev command and ready pattern above.'
"))
            };
        }

        #endregion
    }
}
