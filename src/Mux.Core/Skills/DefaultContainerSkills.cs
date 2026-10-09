namespace Mux.Core.Skills
{
    using System.Collections.Generic;

    /// <summary>
    /// The Docker and Docker Compose default skills. Every command prints the active Docker context first. Compose
    /// never removes volumes, and no command deletes images, containers, or volumes.
    /// </summary>
    public static class DefaultContainerSkills
    {
        #region Private-Members

        private const string Setup = @"$dockerHint = 'Install Docker Desktop or Docker Engine from https://docs.docker.com/get-docker/.'
$context = Get-MuxTarget -Label 'Docker context' -Resolve { docker context show } -LoginHint 'Install Docker, or check that the docker CLI can read its configuration.'
";

        private static readonly string[] _AppliesTo = { "Dockerfile", "**/Dockerfile", "*.Dockerfile", "**/*.Dockerfile", "compose.yaml", "compose.yml", "docker-compose.yaml", "docker-compose.yml" };

        #endregion

        #region Public-Methods

        /// <summary>Returns the container skill definitions.</summary>
        /// <returns>The definitions.</returns>
        public static IReadOnlyList<DefaultSkillDef> All()
        {
            ToolchainSkillFactory f = new ToolchainSkillFactory(Setup, new[] { "docker", "containers" }, _AppliesTo, null, ToolchainSkillFactory.GuardedExitNote);
            ToolchainSkillFactory lint = new ToolchainSkillFactory(string.Empty, new[] { "docker", "containers" }, _AppliesTo, null);
            return new List<DefaultSkillDef>
            {
                f.Skill("docker-build", "Build and push container images", "Builds an image from the nearest Dockerfile, tags it, or pushes it to a registry.", true,
                    "The user asks to build a container image, tag one, or push one to a registry.",
                    "[tag]",
                    "`build [tag]` builds the nearest Dockerfile's directory; the default tag is `<folder>:<git short sha>` (or `:dev` outside git). `tag <source> <target>` retags, and `push <image>` pushes exactly the image named. Pushing an image whose name matches the production pattern needs `--confirm <image>`.",
                    ToolchainSkillFactory.Command("build", "Build an image from the nearest Dockerfile.", @"$dockerfileDir = Find-MuxUp -Names @('Dockerfile')
if (-not $dockerfileDir) { Exit-MuxNotApplicable 'no Dockerfile found here or in a parent.' }
$tag = Get-MuxArg -Arguments $args -Index 0
if (-not $tag) {
    $sha = if ((Test-MuxTool 'git') -and -not (Test-MuxDryRun)) { (& git rev-parse --short HEAD 2>$null) } else { $null }
    $tag = (Split-Path -Leaf $dockerfileDir).ToLowerInvariant() + ':' + $(if ($sha) { $sha } else { 'dev' })
}
Invoke-MuxTool -Tool 'docker' -Arguments @('build', '-t', $tag, $dockerfileDir) -InstallHint $dockerHint"),
                    ToolchainSkillFactory.Command("tag", "Tag an existing image.", @"$source = Get-MuxArg -Arguments $args -Index 0
$target = Get-MuxArg -Arguments $args -Index 1
if (-not $source -or -not $target) { Exit-MuxNotApplicable 'pass both images: docker-build tag <source> <target>' }
Invoke-MuxTool -Tool 'docker' -Arguments @('tag', $source, $target) -InstallHint $dockerHint"),
                    ToolchainSkillFactory.Command("push", "Push an image to its registry.", @"$split = Split-MuxConfirm -Arguments $args
$image = Get-MuxArg -Arguments $split.Rest -Index 0
if (-not $image) { Exit-MuxNotApplicable 'pass the image to push: docker-build push <registry/name:tag>' }
Assert-MuxNotProduction -Target $image -Confirm $split.Confirm
Invoke-MuxTool -Tool 'docker' -Arguments @('push', $image) -InstallHint $dockerHint")),

                f.Skill("docker-inspect", "Inspect containers and images", "Lists containers and images, tails a container's logs, or shows resource use.", false,
                    "The user asks what is running, why a container failed, or how much it uses.",
                    "[container] [lines]",
                    "`ps` lists all containers, `images` lists images, `logs <container> [lines]` shows the last lines (default 200), `stats` takes one resource snapshot, and `context` shows the Docker context and engine version.",
                    ToolchainSkillFactory.Command("ps", "List containers, running and stopped.", @"Invoke-MuxTool -Tool 'docker' -Arguments @('ps', '--all', '--format', 'table {{.Names}}\t{{.Image}}\t{{.Status}}\t{{.Ports}}') -InstallHint $dockerHint"),
                    ToolchainSkillFactory.Command("images", "List images.", @"Invoke-MuxTool -Tool 'docker' -Arguments @('images', '--format', 'table {{.Repository}}\t{{.Tag}}\t{{.ID}}\t{{.Size}}') -InstallHint $dockerHint"),
                    ToolchainSkillFactory.Command("logs", "Show a container's recent logs.", @"$container = Get-MuxArg -Arguments $args -Index 0
if (-not $container) { Exit-MuxNotApplicable 'pass a container: docker-inspect logs <container> [lines]' }
$lines = Get-MuxLineLimit -Value (Get-MuxArg -Arguments $args -Index 1)
Invoke-MuxTool -Tool 'docker' -Arguments @('logs', '--tail', [string]$lines, $container) -InstallHint $dockerHint"),
                    ToolchainSkillFactory.Command("stats", "Take one snapshot of container resource use.", @"Invoke-MuxTool -Tool 'docker' -Arguments @('stats', '--no-stream') -InstallHint $dockerHint"),
                    ToolchainSkillFactory.Command("context", "Show the Docker context and engine version.", @"Invoke-MuxTool -Tool 'docker' -Arguments @('version', '--format', 'client {{.Client.Version}} / server {{.Server.Version}}') -InstallHint $dockerHint")),

                f.Skill("compose", "Run a Docker Compose stack", "Validates, starts, stops, restarts, and inspects the project's Compose stack.", true,
                    "The user asks to start, stop, restart, or debug the project's Compose services.",
                    "[service] [lines]",
                    "`config` validates and prints the resolved file. `up` starts detached and waits up to 120 seconds for health checks. `down` stops and removes containers but never volumes, so data survives. `ps`, `logs [service] [lines]`, and `restart [service]` act on the nearest compose file.",
                    ToolchainSkillFactory.Command("config", "Validate and print the resolved compose file.", @"$file = Get-MuxComposeFile
Invoke-MuxTool -Tool 'docker' -Arguments @('compose', '-f', $file, 'config') -InstallHint $dockerHint"),
                    ToolchainSkillFactory.Command("up", "Start the stack detached and wait for health.", @"$file = Get-MuxComposeFile
Invoke-MuxTool -Tool 'docker' -Arguments @('compose', '-f', $file, 'up', '-d', '--wait', '--wait-timeout', '120') -InstallHint $dockerHint"),
                    ToolchainSkillFactory.Command("down", "Stop the stack, keeping volumes.", @"$file = Get-MuxComposeFile
Invoke-MuxTool -Tool 'docker' -Arguments @('compose', '-f', $file, 'down') -InstallHint $dockerHint"),
                    ToolchainSkillFactory.Command("ps", "List the stack's services.", @"$file = Get-MuxComposeFile
Invoke-MuxTool -Tool 'docker' -Arguments @('compose', '-f', $file, 'ps') -InstallHint $dockerHint"),
                    ToolchainSkillFactory.Command("logs", "Show recent logs for the stack or one service.", @"$file = Get-MuxComposeFile
$service = Get-MuxArg -Arguments $args -Index 0
$lines = Get-MuxLineLimit -Value (Get-MuxArg -Arguments $args -Index 1)
$composeArgs = @('compose', '-f', $file, 'logs', '--no-color', '--tail', [string]$lines)
if ($service) { $composeArgs += $service }
Invoke-MuxTool -Tool 'docker' -Arguments $composeArgs -InstallHint $dockerHint"),
                    ToolchainSkillFactory.Command("restart", "Restart the stack or one service.", @"$file = Get-MuxComposeFile
$service = Get-MuxArg -Arguments $args -Index 0
$composeArgs = @('compose', '-f', $file, 'restart')
if ($service) { $composeArgs += $service }
Invoke-MuxTool -Tool 'docker' -Arguments $composeArgs -InstallHint $dockerHint")),

                lint.Skill("dockerfile-lint", "Lint the Dockerfile", "Checks the nearest Dockerfile with hadolint, or with a built-in checklist when hadolint is not installed.", false,
                    "Before building or committing a Dockerfile, or when an image is larger or less secure than expected.",
                    string.Empty,
                    "`check` runs hadolint when installed. Otherwise it flags unpinned base images (no tag or `:latest`), a missing `USER` (the container runs as root), `ADD` with a URL, and services without a `HEALTHCHECK`, exiting 1 when it finds any.",
                    ToolchainSkillFactory.Command("check", "Lint the Dockerfile.", @"$file = Find-MuxFileUp -Names @('Dockerfile')
if (-not $file) { Exit-MuxNotApplicable 'no Dockerfile found here or in a parent.' }
if (Test-MuxTool 'hadolint') { Invoke-MuxTool -Tool 'hadolint' -Arguments @($file); exit 0 }
Write-Output ('hadolint is not installed; using the built-in checklist on ' + $file)
$lines = Get-Content -LiteralPath $file
$problems = New-Object System.Collections.Generic.List[string]
foreach ($line in $lines) {
    if ($line -match '^\s*FROM\s+(\S+)') {
        $image = $Matches[1]
        if ($image -ne 'scratch' -and $image -notmatch '\$' -and ($image -notmatch ':' -or $image -match ':latest$') -and $image -notmatch '@sha256:') { $problems.Add('Base image ' + $image + ' is not pinned to a version tag or digest.') }
    }
    if ($line -match '^\s*ADD\s+https?://') { $problems.Add('ADD with a URL: prefer RUN curl with a checksum, or COPY.') }
}
if (-not ($lines | Where-Object { $_ -match '^\s*USER\s+' })) { $problems.Add('No USER instruction: the container runs as root.') }
if (($lines | Where-Object { $_ -match '^\s*(EXPOSE|CMD)\s+' }) -and -not ($lines | Where-Object { $_ -match '^\s*HEALTHCHECK\s+' })) { $problems.Add('No HEALTHCHECK for a service image.') }
if ($problems.Count -eq 0) { Write-Output 'No problems found.'; exit 0 }
$problems | ForEach-Object { Write-Output ('- ' + $_) }
exit 1"))
            };
        }

        #endregion
    }
}
