namespace Mux.Core.Skills
{
    using System.Collections.Generic;

    /// <summary>
    /// API contract skills: <c>openapi</c> lints an OpenAPI document (Redocly or Spectral) and finds breaking changes
    /// against a git base or another file (oasdiff); <c>openapi-client</c> generates a client with openapi-generator.
    /// </summary>
    public static class DefaultApiContractSkills
    {
        #region Private-Members

        private static readonly string[] _Specs = { "**/openapi*.yaml", "**/openapi*.yml", "**/openapi*.json", "**/swagger*.yaml", "**/swagger*.yml", "**/swagger*.json" };

        private const string Setup = @"$oasHint = 'Install oasdiff (https://github.com/oasdiff/oasdiff).'
function Resolve-MuxSpec {
    param([string]$Given)
    if ($Given) {
        if (-not (Test-MuxDryRun) -and -not (Test-Path -LiteralPath $Given -PathType Leaf)) { Exit-MuxNotApplicable ('OpenAPI document not found: ' + $Given) }
        return $Given
    }
    $root = Get-MuxRepoRoot
    $found = Get-ChildItem -LiteralPath $root -File -Recurse -Depth 4 -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -match '^(openapi|swagger)[\w.-]*\.(ya?ml|json)$' -and $_.FullName -notmatch '[\\/](node_modules|bin|obj|\.git|dist|build)[\\/]' } |
        Sort-Object { $_.FullName.Length } | Select-Object -First 1
    if (-not $found) { Exit-MuxNotApplicable 'no OpenAPI document found (openapi.yaml, openapi.json, swagger.json, ...); pass its path.' }
    return [System.IO.Path]::GetRelativePath((Get-Location).Path, $found.FullName).Replace('\', '/')
}
";

        #endregion

        #region Public-Methods

        /// <summary>Returns the API contract skill definitions.</summary>
        /// <returns>The definitions.</returns>
        public static IReadOnlyList<DefaultSkillDef> All()
        {
            ToolchainSkillFactory factory = new ToolchainSkillFactory(Setup, new[] { "api", "openapi", "review" }, _Specs, null,
                " Exit codes: 0 success or no breaking changes, 1 lint errors or breaking changes, 2 the tool or document is missing, or bad arguments.");
            return new List<DefaultSkillDef>
            {
                factory.Skill("openapi", "Lint OpenAPI and find breaking changes",
                    "OpenAPI and Swagger: lints the API document, and finds breaking API changes against the base branch or between two versions.",
                    false,
                    "The user asks to validate or lint an OpenAPI or Swagger spec, or whether an API change breaks clients.",
                    "[spec] [base-ref] | <old> <new>",
                    "`lint [spec]` runs `redocly lint` when Redocly is installed, otherwise `spectral lint`. `diff [spec] [base-ref]` reads the same file at the base ref (default: the default branch) with git and runs oasdiff: first the full changelog, then `oasdiff breaking --fail-on ERR`, which exits 1 when a change breaks clients. `breaking <old> <new>` compares two files directly. Without a path, the first openapi* or swagger* document in the repository is used.",
                    ToolchainSkillFactory.Command("lint", "Lint the OpenAPI document.", @"$spec = Resolve-MuxSpec (Get-MuxArg -Arguments $args -Index 0)
if (Test-MuxTool 'redocly') { Invoke-MuxTool -Tool 'redocly' -Arguments @('lint', $spec) }
elseif (Test-MuxTool 'spectral') { Invoke-MuxTool -Tool 'spectral' -Arguments @('lint', $spec) }
elseif (Test-MuxDryRun) { Write-Output ('DRYRUN: redocly lint ' + $spec) }
else { Exit-MuxNotApplicable 'neither Redocly nor Spectral is installed (npm install -g @redocly/cli, or npm install -g @stoplight/spectral-cli).' }"),
                    ToolchainSkillFactory.Command("diff", "Compare the document with the base branch and fail on breaking changes.", @"Assert-MuxGitRepo
$spec = Resolve-MuxSpec (Get-MuxArg -Arguments $args -Index 0)
$base = Get-MuxArg -Arguments $args -Index 1
if (-not $base) { $base = Get-MuxDefaultBranch }
if (-not $base) { Exit-MuxNotApplicable 'no default branch found; pass the base ref: openapi diff <spec> <base-ref>' }
$repoPath = (& git ls-files --full-name -- $spec 2>$null | Select-Object -First 1)
if (-not $repoPath) { $repoPath = (& git rev-parse --show-prefix 2>$null) + $spec }
$old = Join-Path ([System.IO.Path]::GetTempPath()) ('mux-openapi-base-' + [guid]::NewGuid().ToString('N') + [System.IO.Path]::GetExtension($spec))
try {
    $content = & git show ($base + ':' + $repoPath) 2>$null
    if ($LASTEXITCODE -ne 0) { Write-Output ($spec + ' does not exist at ' + $base + '; it is new, so nothing can break.'); exit 0 }
    Set-Content -LiteralPath $old -Value ($content -join [Environment]::NewLine)
    Write-Output ('Comparing ' + $spec + ' with ' + $base + ':' + $repoPath)
    Invoke-MuxTool -Tool 'oasdiff' -Arguments @('changelog', $old, $spec) -InstallHint $oasHint -AllowFailure
    Invoke-MuxTool -Tool 'oasdiff' -Arguments @('breaking', $old, $spec, '--fail-on', 'ERR') -InstallHint $oasHint -AllowFailure
    if ($script:MuxLastExit -ne 0) { Write-Output 'Breaking changes found.'; exit 1 }
    if (-not (Test-MuxDryRun)) { Write-Output 'No breaking changes.' }
} finally {
    if (Test-Path -LiteralPath $old) { Remove-Item -LiteralPath $old -Force }
}"),
                    ToolchainSkillFactory.Command("breaking", "Compare two documents and fail on breaking changes.", @"$old = Get-MuxArg -Arguments $args -Index 0
$new = Get-MuxArg -Arguments $args -Index 1
if (-not $old -or -not $new) { Exit-MuxNotApplicable 'pass both documents: openapi breaking <old> <new>' }
foreach ($file in @($old, $new)) { if (-not (Test-MuxDryRun) -and -not (Test-Path -LiteralPath $file -PathType Leaf)) { Exit-MuxNotApplicable ('OpenAPI document not found: ' + $file) } }
Invoke-MuxTool -Tool 'oasdiff' -Arguments @('breaking', $old, $new, '--fail-on', 'ERR') -InstallHint $oasHint -AllowFailure
if ($script:MuxLastExit -ne 0) { Write-Output 'Breaking changes found.'; exit 1 }
exit 0")),

                factory.Skill("openapi-client", "Generate an API client from OpenAPI",
                    "Generates an API client or server stub from an OpenAPI document with openapi-generator (typescript-fetch, csharp, python, go, java, and more).",
                    true,
                    "The user asks to generate a client SDK, typed client, or server stub from an OpenAPI or Swagger spec.",
                    "<generator> <output-dir> [spec]",
                    "`generate <generator> <output-dir> [spec]` runs `openapi-generator-cli generate -i <spec> -g <generator> -o <output-dir>` (or `openapi-generator` when that is the installed name). The output directory must be a relative path inside the repository; existing files there are overwritten, so commit first. `generators` lists the generator names.",
                    ToolchainSkillFactory.Command("generate", "Generate a client or stub into a folder.", @"$generator = Get-MuxArg -Arguments $args -Index 0
$output = Get-MuxArg -Arguments $args -Index 1
if (-not $generator -or -not $output) { Exit-MuxNotApplicable 'pass the generator and output folder: openapi-client generate <generator> <output-dir> [spec]' }
if ([System.IO.Path]::IsPathRooted($output) -or $output.Replace('\', '/').Split('/') -contains '..') { Exit-MuxNotApplicable ('generate into a relative folder inside the repository, got ' + $output) }
$spec = Resolve-MuxSpec (Get-MuxArg -Arguments $args -Index 2)
$tool = if (Test-MuxTool 'openapi-generator-cli') { 'openapi-generator-cli' } elseif (Test-MuxTool 'openapi-generator') { 'openapi-generator' } else { 'openapi-generator-cli' }
Invoke-MuxTool -Tool $tool -Arguments @('generate', '-i', $spec, '-g', $generator, '-o', $output) -InstallHint 'Install openapi-generator: npm install -g @openapitools/openapi-generator-cli (needs Java), or brew install openapi-generator.'"),
                    ToolchainSkillFactory.Command("generators", "List the available generators.", @"$tool = if (Test-MuxTool 'openapi-generator-cli') { 'openapi-generator-cli' } elseif (Test-MuxTool 'openapi-generator') { 'openapi-generator' } else { 'openapi-generator-cli' }
Invoke-MuxTool -Tool $tool -Arguments @('list', '--short') -InstallHint 'Install openapi-generator: npm install -g @openapitools/openapi-generator-cli (needs Java), or brew install openapi-generator.'"))
            };
        }

        #endregion
    }
}
