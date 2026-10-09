namespace Mux.Core.Skills
{
    using System.Collections.Generic;

    /// <summary>
    /// The edge and platform-as-a-service default skills: Vercel, Netlify, Cloudflare (Wrangler), and fly.io. Each is
    /// listed only when its CLI is installed and the project has the platform's config file. Production deploys sit
    /// behind the production guard; environment and secret commands list names only.
    /// </summary>
    public static class DefaultEdgePlatformSkills
    {
        #region Public-Methods

        /// <summary>Returns the edge platform skill definitions.</summary>
        /// <returns>The definitions.</returns>
        public static IReadOnlyList<DefaultSkillDef> All()
        {
            ToolchainSkillFactory vercel = new ToolchainSkillFactory(@"$vercelHint = 'Install the Vercel CLI: npm install -g vercel.'
$vercelUser = Get-MuxTarget -Label 'Vercel account' -Resolve { vercel whoami 2>$null } -LoginHint 'Sign in with vercel login, then retry. Mux does not sign in for you.'
", new[] { "vercel", "deploy" }, new[] { "vercel.json", ".vercel/**" }, new[] { "vercel" }, ToolchainSkillFactory.GuardedExitNote);

            ToolchainSkillFactory netlify = new ToolchainSkillFactory(@"$netlifyHint = 'Install the Netlify CLI: npm install -g netlify-cli.'
$netlifyUser = Get-MuxTarget -Label 'Netlify account' -Resolve { $status = netlify status --json 2>$null | ConvertFrom-Json; if ($status.account) { $status.account.Email } elseif ($status) { 'signed-in' } } -LoginHint 'Sign in with netlify login, then retry. Mux does not sign in for you.'
", new[] { "netlify", "deploy" }, new[] { "netlify.toml", ".netlify/**" }, new[] { "netlify" }, ToolchainSkillFactory.GuardedExitNote);

            ToolchainSkillFactory cloudflare = new ToolchainSkillFactory(@"$wranglerHint = 'Install Wrangler: npm install -g wrangler (or add it to the project).'
$cfAccount = Get-MuxTarget -Label 'Cloudflare account' -Resolve { $out = wrangler whoami 2>$null; if ($LASTEXITCODE -eq 0 -and ($out -match 'logged in')) { 'signed-in' } } -LoginHint 'Sign in with wrangler login (or set CLOUDFLARE_API_TOKEN), then retry. Mux does not sign in for you.'
", new[] { "cloudflare", "workers", "deploy" }, new[] { "wrangler.toml", "wrangler.json", "wrangler.jsonc" }, new[] { "wrangler" }, ToolchainSkillFactory.GuardedExitNote);

            ToolchainSkillFactory fly = new ToolchainSkillFactory(@"$flyTool = if (Test-MuxTool 'flyctl') { 'flyctl' } else { 'fly' }
$flyHint = 'Install flyctl from https://fly.io/docs/flyctl/install/.'
$flyApp = Get-MuxTarget -Label 'fly.io app' -Resolve { $toml = Find-MuxFileUp -Names @('fly.toml'); if ($toml) { $line = Get-Content -LiteralPath $toml | Where-Object { $_ -match '^\s*app\s*=' } | Select-Object -First 1; if ($line) { ($line -replace '^\s*app\s*=\s*', '').Trim().Trim([char]39).Trim([char]34) } } } -LoginHint 'No app name in fly.toml; run fly launch or add app = ""name"".'
", new[] { "fly", "deploy" }, new[] { "fly.toml" }, new[] { "flyctl|fly" }, ToolchainSkillFactory.GuardedExitNote);

            return new List<DefaultSkillDef>
            {
                vercel.Skill("vercel", "Deploy with Vercel", "Lists deployments, makes preview or production deployments, shows deployment logs, and lists environment variable names.", true,
                    "The project deploys to Vercel.",
                    "<deployment-url> [--confirm production]",
                    "`deploy-preview` makes a preview deployment and prints its URL; check it before promoting. `deploy-prod` always needs `--confirm production`, added only after the user approves. `env-names` lists variable names and targets, never values.",
                    C("whoami", "Show the signed-in account.", @"Invoke-MuxTool -Tool 'vercel' -Arguments @('whoami') -InstallHint $vercelHint"),
                    C("ls", "List recent deployments.", @"Invoke-MuxTool -Tool 'vercel' -Arguments @('ls') -InstallHint $vercelHint"),
                    C("deploy-preview", "Make a preview deployment.", @"Invoke-MuxTool -Tool 'vercel' -Arguments @('deploy', '--yes') -InstallHint $vercelHint"),
                    C("deploy-prod", "Make a production deployment.", @"$split = Split-MuxConfirm -Arguments $args
Assert-MuxNotProduction -Target 'production' -Confirm $split.Confirm
Invoke-MuxTool -Tool 'vercel' -Arguments @('deploy', '--prod', '--yes') -InstallHint $vercelHint"),
                    C("logs", "Show a deployment's logs.", @"$url = Get-MuxArg -Arguments $args -Index 0
if (-not $url) { Exit-MuxNotApplicable 'pass a deployment URL or id: vercel logs <deployment>' }
Invoke-MuxTool -Tool 'vercel' -Arguments @('inspect', $url, '--logs') -InstallHint $vercelHint"),
                    C("env-names", "List environment variable names.", @"Invoke-MuxTool -Tool 'vercel' -Arguments @('env', 'ls') -InstallHint $vercelHint")),

                netlify.Skill("netlify", "Deploy with Netlify", "Shows site status, makes draft or production deploys, lists sites, and lists environment variable names.", true,
                    "The project deploys to Netlify.",
                    "[--confirm production]",
                    "`deploy-draft` publishes to a unique draft URL; check it first. `deploy-prod` always needs `--confirm production`, added only after the user approves. `env-names` prints variable names only.",
                    C("status", "Show the linked site and account.", @"Invoke-MuxTool -Tool 'netlify' -Arguments @('status') -InstallHint $netlifyHint"),
                    C("sites", "List sites.", @"Invoke-MuxTool -Tool 'netlify' -Arguments @('sites:list') -InstallHint $netlifyHint"),
                    C("deploy-draft", "Make a draft deploy.", @"Invoke-MuxTool -Tool 'netlify' -Arguments @('deploy') -InstallHint $netlifyHint"),
                    C("deploy-prod", "Make a production deploy.", @"$split = Split-MuxConfirm -Arguments $args
Assert-MuxNotProduction -Target 'production' -Confirm $split.Confirm
Invoke-MuxTool -Tool 'netlify' -Arguments @('deploy', '--prod') -InstallHint $netlifyHint"),
                    C("env-names", "List environment variable names.", @"if (Test-MuxDryRun) { Write-Output 'DRYRUN: netlify env:list --json (names only)'; exit 0 }
if (-not (Test-MuxTool 'netlify')) { Exit-MuxNotApplicable $netlifyHint }
$vars = netlify env:list --json 2>$null | ConvertFrom-Json
if ($null -eq $vars) { Write-Output 'No environment variables, or the site is not linked (netlify link).'; exit 0 }
$vars.PSObject.Properties.Name | Sort-Object | ForEach-Object { Write-Output $_ }")),

                cloudflare.Skill("cloudflare", "Deploy to Cloudflare Workers and Pages", "Checks a Worker build with a dry run, deploys it, lists deployments and D1, KV, and R2 resources, and deploys Pages.", true,
                    "The project has a wrangler config (Workers) or deploys static output to Cloudflare Pages.",
                    "[environment] [--confirm <environment>]",
                    "`deploy-dry-run [environment]` builds and validates without uploading. `deploy [environment]` deploys; an environment matching the production pattern (or none, which is the top-level production worker) needs `--confirm <environment or production>`. `pages-deploy <directory> [project]` uploads a Pages build.",
                    C("whoami", "Show the signed-in account.", @"Invoke-MuxTool -Tool 'wrangler' -Arguments @('whoami') -InstallHint $wranglerHint"),
                    C("deploy-dry-run", "Build and validate without uploading.", @"$wranglerArgs = @('deploy', '--dry-run')
$environment = Get-MuxArg -Arguments $args -Index 0
if ($environment) { $wranglerArgs += @('--env', $environment) }
Invoke-MuxTool -Tool 'wrangler' -Arguments $wranglerArgs -InstallHint $wranglerHint"),
                    C("deploy", "Deploy the Worker.", @"$split = Split-MuxConfirm -Arguments $args
$environment = Get-MuxArg -Arguments $split.Rest -Index 0
$target = if ($environment) { $environment } else { 'production' }
Assert-MuxNotProduction -Target $target -Confirm $split.Confirm
$wranglerArgs = @('deploy')
if ($environment) { $wranglerArgs += @('--env', $environment) }
Invoke-MuxTool -Tool 'wrangler' -Arguments $wranglerArgs -InstallHint $wranglerHint"),
                    C("deployments", "List recent deployments.", @"Invoke-MuxTool -Tool 'wrangler' -Arguments @('deployments', 'list') -InstallHint $wranglerHint"),
                    C("d1-list", "List D1 databases.", @"Invoke-MuxTool -Tool 'wrangler' -Arguments @('d1', 'list') -InstallHint $wranglerHint"),
                    C("kv-list", "List KV namespaces.", @"Invoke-MuxTool -Tool 'wrangler' -Arguments @('kv', 'namespace', 'list') -InstallHint $wranglerHint"),
                    C("r2-list", "List R2 buckets.", @"Invoke-MuxTool -Tool 'wrangler' -Arguments @('r2', 'bucket', 'list') -InstallHint $wranglerHint"),
                    C("pages-deploy", "Deploy a directory to Cloudflare Pages.", @"$directory = Get-MuxArg -Arguments $args -Index 0
if (-not $directory) { Exit-MuxNotApplicable 'pass the build output directory: cloudflare pages-deploy <directory> [project]' }
$pagesArgs = @('pages', 'deploy', $directory)
$projectName = Get-MuxArg -Arguments $args -Index 1
if ($projectName) { $pagesArgs += @('--project-name', $projectName) }
Invoke-MuxTool -Tool 'wrangler' -Arguments $pagesArgs -InstallHint $wranglerHint")),

                fly.Skill("flyio", "Deploy with fly.io", "Shows app status, deploys, prints recent logs, shows scaling, lists secret names, and lists releases.", true,
                    "The project has a fly.toml.",
                    "[--confirm <app>]",
                    "The app comes from fly.toml. `deploy` builds and deploys; an app name matching the production pattern needs `--confirm <app>` after the user approves. `logs` prints recent lines without following. `secrets-names` lists names and digests, never values.",
                    C("status", "Show app status.", @"Invoke-MuxTool -Tool $flyTool -Arguments @('status') -InstallHint $flyHint"),
                    C("deploy", "Build and deploy the app.", @"$split = Split-MuxConfirm -Arguments $args
Assert-MuxNotProduction -Target $flyApp -Confirm $split.Confirm
Invoke-MuxTool -Tool $flyTool -Arguments @('deploy') -InstallHint $flyHint"),
                    C("logs", "Print recent logs.", @"Invoke-MuxTool -Tool $flyTool -Arguments @('logs', '--no-tail') -InstallHint $flyHint"),
                    C("scale-show", "Show machine counts and sizes.", @"Invoke-MuxTool -Tool $flyTool -Arguments @('scale', 'show') -InstallHint $flyHint"),
                    C("secrets-names", "List secret names.", @"Invoke-MuxTool -Tool $flyTool -Arguments @('secrets', 'list') -InstallHint $flyHint"),
                    C("releases", "List releases.", @"Invoke-MuxTool -Tool $flyTool -Arguments @('releases') -InstallHint $flyHint"))
            };
        }

        #endregion

        #region Private-Methods

        private static DefaultSkillCommandDef C(string name, string description, string code)
        {
            return ToolchainSkillFactory.Command(name, description, code);
        }

        #endregion
    }
}
