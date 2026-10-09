namespace Mux.Core.Skills
{
    using System.Collections.Generic;

    /// <summary>
    /// The project-detection default skill: one read-only report of the languages, package managers, build
    /// systems, test frameworks, CI, and deployment files in a repository, plus the mux skills that apply. Other
    /// skills (init, code review, fix-until-green) start from it.
    /// </summary>
    public static class DefaultProjectSkills
    {
        /// <summary>Returns the project-detection skill definitions.</summary>
        /// <returns>The definitions.</returns>
        public static IReadOnlyList<DefaultSkillDef> All()
        {
            return new List<DefaultSkillDef>
            {
                DefaultSkillHelpers.Attach(new DefaultSkillDef
                {
                    Id = "project-detect",
                    Title = "Detect the project's toolchain",
                    Description = "Reports languages, package managers, build systems, test frameworks, CI, and container or deploy files, plus the mux skills that apply.",
                    Mutating = false,
                    Tags = new List<string> { "project", "detect" },
                    WhenToUse = "Start here in an unfamiliar repository, before building, testing, or reviewing, to learn which tools and skills the project uses.",
                    Body = "Run `summary` for a readable report or `json` for a single object other skills can parse. Both read files only; nothing is built or installed. The `skills` line names the mux skills that fit this project, so prefer those over guessing commands.",
                    Commands = new List<DefaultSkillCommandDef>
                    {
                        new DefaultSkillCommandDef("summary", "Print a readable report of the project's toolchain.", "pwsh", DetectScript + "Write-MuxProjectReport -Report $report\n"),
                        new DefaultSkillCommandDef("json", "Print the same report as one JSON object.", "pwsh", DetectScript + "$report | ConvertTo-Json -Depth 6\n")
                    }
                })
            };
        }

        internal const string DetectScript = @"$root = Get-MuxRepoRoot
Push-Location $root
try {
    $skip = '(^|[\\/])(node_modules|bin|obj|target|dist|build|out|\.venv|venv|__pycache__|\.git|\.gradle|\.idea|vendor)([\\/]|$)'
    $files = @()
    if ((Test-Path -LiteralPath (Join-Path $root '.git')) -and (Test-MuxTool 'git')) {
        $files = @(& git -c core.quotepath=off ls-files --cached --others --exclude-standard 2>$null)
    }
    if ($files.Count -eq 0) {
        $files = @(Get-ChildItem -Recurse -File -Force -Depth 6 -ErrorAction SilentlyContinue |
            ForEach-Object { [IO.Path]::GetRelativePath($root, $_.FullName) } |
            Where-Object { $_ -notmatch $skip } | Select-Object -First 20000)
    }
    $files = @($files | Where-Object { $_ -notmatch $skip })
    $names = @($files | ForEach-Object { Split-Path $_ -Leaf })
    function Has([string]$pattern) { return [bool]($names | Where-Object { $_ -like $pattern } | Select-Object -First 1) }
    function HasPath([string]$pattern) { return [bool]($files | Where-Object { ($_ -replace '\\', '/') -like $pattern } | Select-Object -First 1) }

    $languageMap = @{
        '.cs' = 'C#'; '.fs' = 'F#'; '.vb' = 'Visual Basic'; '.ts' = 'TypeScript'; '.tsx' = 'TypeScript'; '.js' = 'JavaScript';
        '.jsx' = 'JavaScript'; '.mjs' = 'JavaScript'; '.cjs' = 'JavaScript'; '.py' = 'Python'; '.java' = 'Java'; '.kt' = 'Kotlin';
        '.go' = 'Go'; '.rs' = 'Rust'; '.c' = 'C'; '.h' = 'C/C++ header'; '.cpp' = 'C++'; '.cc' = 'C++'; '.cxx' = 'C++';
        '.hpp' = 'C++'; '.rb' = 'Ruby'; '.php' = 'PHP'; '.swift' = 'Swift'; '.scala' = 'Scala'; '.sh' = 'Shell'; '.ps1' = 'PowerShell';
        '.sql' = 'SQL'; '.tf' = 'Terraform'; '.dart' = 'Dart'; '.lua' = 'Lua'; '.r' = 'R'; '.ex' = 'Elixir'; '.exs' = 'Elixir'
    }
    $languages = [ordered]@{}
    $files | ForEach-Object { [IO.Path]::GetExtension($_).ToLowerInvariant() } | Group-Object | Sort-Object Count -Descending |
        ForEach-Object { if ($languageMap.ContainsKey($_.Name)) { $lang = $languageMap[$_.Name]; if ($languages.Contains($lang)) { $languages[$lang] += $_.Count } else { $languages[$lang] = $_.Count } } }

    $ecosystems = New-Object System.Collections.Generic.List[object]
    $tests = New-Object System.Collections.Generic.List[string]
    $quality = New-Object System.Collections.Generic.List[string]
    $skills = New-Object System.Collections.Generic.List[string]

    if (Has 'package.json') {
        $pkgDir = $root
        if (-not (Test-Path -LiteralPath (Join-Path $root 'package.json'))) { $first = $files | Where-Object { (Split-Path $_ -Leaf) -eq 'package.json' } | Select-Object -First 1; $pkgDir = Join-Path $root (Split-Path $first -Parent) }
        $pm = Get-MuxNodePackageManager -Dir $pkgDir
        $pkg = Get-MuxPackageJson -Dir $pkgDir
        $ecosystems.Add([ordered]@{ name = 'JavaScript/TypeScript'; manager = $pm; manifest = 'package.json' })
        foreach ($t in @('vitest', 'jest', 'mocha', '@playwright/test', 'cypress', 'ava')) { if (Test-MuxPackageDependency $pkg $t) { $tests.Add($t) } }
        foreach ($q in @('eslint', '@biomejs/biome', 'prettier', 'typescript')) { if (Test-MuxPackageDependency $pkg $q) { $quality.Add($q) } }
        $skills.AddRange([string[]]@('js-install', 'js-build', 'js-test', 'js-lint', 'js-typecheck', 'js-format', 'js-deps', 'js-scripts'))
        if (Test-MuxPackageDependency $pkg 'react') {
            $ecosystems.Add([ordered]@{ name = 'React'; manager = $pm; manifest = 'package.json' })
            $skills.AddRange([string[]]@('react-new-component', 'react-new-hook', 'react-test', 'react-build-analyze', 'react-lint-hooks', 'react-upgrade-check'))
        }
    }
    if ((Has 'pyproject.toml') -or (Has 'requirements*.txt') -or (Has 'setup.py') -or (Has 'Pipfile')) {
        $pyDir = Find-MuxUp -Names @('pyproject.toml', 'requirements*.txt', 'setup.py', 'setup.cfg', 'Pipfile')
        if (-not $pyDir) { $pyDir = $root }
        $ecosystems.Add([ordered]@{ name = 'Python'; manager = (Get-MuxPythonManager -Dir $pyDir); manifest = 'pyproject.toml / requirements*.txt' })
        if ((Has 'pytest.ini') -or (Has 'conftest.py') -or ((Test-Path 'pyproject.toml') -and ((Get-Content 'pyproject.toml' -Raw) -match 'pytest'))) { $tests.Add('pytest') }
        foreach ($q in @('ruff.toml', '.ruff.toml', 'mypy.ini', '.flake8', 'pyrightconfig.json')) { if (Has $q) { $quality.Add($q) } }
        $skills.AddRange([string[]]@('py-env', 'py-install', 'py-test', 'py-lint', 'py-format', 'py-typecheck', 'py-deps'))
    }
    if ((Has 'pom.xml') -or (Has 'build.gradle') -or (Has 'build.gradle.kts')) {
        $tool = if ((Has 'mvnw') -or (Has 'pom.xml')) { if (Has 'mvnw') { 'maven (wrapper)' } else { 'maven' } } else { if (Has 'gradlew') { 'gradle (wrapper)' } else { 'gradle' } }
        $ecosystems.Add([ordered]@{ name = 'Java/JVM'; manager = $tool; manifest = 'pom.xml / build.gradle' })
        $tests.Add('junit (via ' + $tool + ')')
        $skills.AddRange([string[]]@('java-build', 'java-test', 'java-format', 'java-lint', 'java-deps', 'java-new-class'))
    }
    if ((Has 'CMakeLists.txt') -or (Has 'meson.build')) { $ecosystems.Add([ordered]@{ name = 'C/C++'; manager = $(if (Has 'CMakeLists.txt') { 'cmake' } else { 'meson' }); manifest = 'CMakeLists.txt' }); $tests.Add('ctest'); $skills.AddRange([string[]]@('cpp-configure', 'cpp-build', 'cpp-test', 'cpp-format', 'cpp-tidy', 'cpp-sanitize')) }
    if (Has 'go.mod') { $ecosystems.Add([ordered]@{ name = 'Go'; manager = 'go modules'; manifest = 'go.mod' }); $tests.Add('go test'); $skills.AddRange([string[]]@('go-build', 'go-test', 'go-lint', 'go-mod')) }
    if (Has 'Cargo.toml') { $ecosystems.Add([ordered]@{ name = 'Rust'; manager = 'cargo'; manifest = 'Cargo.toml' }); $tests.Add('cargo test'); $skills.AddRange([string[]]@('cargo-build', 'cargo-test', 'cargo-clippy', 'cargo-fmt')) }
    if ((Has '*.sln') -or (Has '*.slnx') -or (Has '*.csproj') -or (Has '*.fsproj')) {
        $ecosystems.Add([ordered]@{ name = '.NET'; manager = 'dotnet'; manifest = '*.sln / *.csproj' })
        $tests.Add('dotnet test')
        $skills.AddRange([string[]]@('dotnet-build', 'dotnet-test', 'dotnet-format', 'dotnet-restore', 'dotnet-outdated'))
    }
    if (Has 'Gemfile') { $ecosystems.Add([ordered]@{ name = 'Ruby'; manager = 'bundler'; manifest = 'Gemfile' }) }
    if (Has 'composer.json') { $ecosystems.Add([ordered]@{ name = 'PHP'; manager = 'composer'; manifest = 'composer.json' }) }

    $ci = @()
    if (HasPath '.github/workflows/*') { $ci += 'GitHub Actions' }
    if (Has '.gitlab-ci.yml') { $ci += 'GitLab CI' }
    if (Has 'azure-pipelines.yml') { $ci += 'Azure Pipelines' }
    if (Has 'Jenkinsfile') { $ci += 'Jenkins' }
    if (HasPath '.circleci/*') { $ci += 'CircleCI' }

    $deploy = @()
    if ((Has 'Dockerfile') -or (Has '*.Dockerfile')) { $deploy += 'Docker' }
    if ((Has 'compose.yaml') -or (Has 'compose.yml') -or (Has 'docker-compose.yaml') -or (Has 'docker-compose.yml')) { $deploy += 'Docker Compose' }
    if (Has 'Chart.yaml') { $deploy += 'Helm' }
    if (Has 'kustomization.yaml') { $deploy += 'Kustomize' }
    if (Has '*.tf') { $deploy += 'Terraform' }
    if (Has 'Pulumi.yaml') { $deploy += 'Pulumi' }
    foreach ($pair in @(@('vercel.json', 'Vercel'), @('netlify.toml', 'Netlify'), @('fly.toml', 'fly.io'), @('wrangler.toml', 'Cloudflare'), @('serverless.yml', 'Serverless'), @('samconfig.toml', 'AWS SAM'), @('cdk.json', 'AWS CDK'), @('azure.yaml', 'Azure Developer CLI'), @('app.yaml', 'Google App Engine'), @('clouds.yaml', 'OpenStack'))) {
        if (Has $pair[0]) { $deploy += $pair[1] }
    }
    $deploySkills = @{
        'Docker' = @('docker-build', 'docker-inspect', 'dockerfile-lint'); 'Docker Compose' = @('compose', 'docker-inspect');
        'Helm' = @('helm', 'k8s-context', 'k8s-inspect', 'k8s-validate', 'k8s-apply'); 'Kustomize' = @('k8s-context', 'k8s-inspect', 'k8s-validate', 'k8s-apply');
        'Terraform' = @('terraform'); 'Pulumi' = @('pulumi'); 'Vercel' = @('vercel'); 'Netlify' = @('netlify'); 'fly.io' = @('flyio'); 'Cloudflare' = @('cloudflare');
        'AWS SAM' = @('aws-whoami', 'aws-deploy'); 'AWS CDK' = @('aws-whoami', 'aws-deploy'); 'Azure Developer CLI' = @('azure-whoami', 'azure-apps');
        'Google App Engine' = @('gcp-whoami', 'gcp-run'); 'OpenStack' = @('openstack-whoami', 'openstack-inspect', 'openstack-heat')
    }
    foreach ($item in $deploy) { if ($deploySkills.ContainsKey($item)) { $skills.AddRange([string[]]$deploySkills[$item]) } }

    $report = [ordered]@{
        root = $root
        languages = $languages
        ecosystems = $ecosystems.ToArray()
        testFrameworks = @($tests | Select-Object -Unique)
        qualityTools = @($quality | Select-Object -Unique)
        ci = $ci
        deploy = $deploy
        skills = @($skills | Select-Object -Unique)
    }
}
finally {
    Pop-Location
}

function Write-MuxProjectReport([object]$Report) {
    Write-Output ('Project root: ' + $Report.root)
    $langs = ($Report.languages.GetEnumerator() | Select-Object -First 8 | ForEach-Object { $_.Key + ' (' + $_.Value + ')' }) -join ', '
    Write-Output ('Languages: ' + $(if ($langs) { $langs } else { 'none detected' }))
    if ($Report.ecosystems.Count -eq 0) { Write-Output 'Ecosystems: none detected' }
    foreach ($e in $Report.ecosystems) { Write-Output ('Ecosystem: ' + $e.name + ' (' + $e.manager + ', ' + $e.manifest + ')') }
    Write-Output ('Tests: ' + $(if ($Report.testFrameworks.Count) { $Report.testFrameworks -join ', ' } else { 'none detected' }))
    Write-Output ('Quality tools: ' + $(if ($Report.qualityTools.Count) { $Report.qualityTools -join ', ' } else { 'none detected' }))
    Write-Output ('CI: ' + $(if ($Report.ci.Count) { $Report.ci -join ', ' } else { 'none detected' }))
    Write-Output ('Containers and deploy: ' + $(if ($Report.deploy.Count) { $Report.deploy -join ', ' } else { 'none detected' }))
    Write-Output ('Skills: ' + $(if ($Report.skills.Count) { $Report.skills -join ', ' } else { 'no toolchain skills apply' }))
}
";
    }
}
