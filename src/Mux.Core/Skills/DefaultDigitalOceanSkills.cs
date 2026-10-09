namespace Mux.Core.Skills
{
    using System.Collections.Generic;

    /// <summary>
    /// The DigitalOcean default skills, built on <c>doctl</c>. Every command confirms the account first; App
    /// Platform deploys sit behind the production guard (matched against the doctl context). Nothing deletes.
    /// </summary>
    public static class DefaultDigitalOceanSkills
    {
        #region Private-Members

        private const string Setup = @"$doHint = 'Install doctl from https://docs.digitalocean.com/reference/doctl/how-to/install/.'
$doContext = Get-MuxTarget -Label 'DigitalOcean context' -Resolve { $null = doctl account get --format Email --no-header 2>$null; if ($LASTEXITCODE -eq 0) { $ctx = (doctl auth list 2>$null | Where-Object { $_ -match '\(current\)' } | Select-Object -First 1); if ($ctx) { ($ctx -replace '\s*\(current\)', '').Trim() } else { 'default' } } } -LoginHint 'Sign in with doctl auth init, then retry. Mux does not sign in for you.'
";

        #endregion

        #region Public-Methods

        /// <summary>Returns the DigitalOcean skill definitions.</summary>
        /// <returns>The definitions.</returns>
        public static IReadOnlyList<DefaultSkillDef> All()
        {
            ToolchainSkillFactory f = new ToolchainSkillFactory(Setup, new[] { "digitalocean", "cloud" }, null, new[] { "doctl" }, ToolchainSkillFactory.GuardedExitNote);
            return new List<DefaultSkillDef>
            {
                f.Skill("do-whoami", "Show the DigitalOcean account", "Shows the account and the current balance.", false,
                    "Before any DigitalOcean work, or when doctl fails to authenticate.",
                    string.Empty,
                    "`account` shows the email, team, and limits; `balance` shows month-to-date usage.",
                    C("account", "Show the account.", @"Invoke-MuxTool -Tool 'doctl' -Arguments @('account', 'get') -InstallHint $doHint"),
                    C("balance", "Show the balance and month-to-date usage.", @"Invoke-MuxTool -Tool 'doctl' -Arguments @('balance', 'get') -InstallHint $doHint")),

                f.Skill("do-infra", "List DigitalOcean infrastructure", "Lists Droplets, Kubernetes clusters, managed databases, volumes, and domains.", false,
                    "The user asks what is running in a DigitalOcean account.",
                    string.Empty,
                    "Read-only. For a DOKS cluster, run `doctl kubernetes cluster kubeconfig save <name>` (the user may prefer to) and then use the k8s-* skills.",
                    C("droplets", "List Droplets.", @"Invoke-MuxTool -Tool 'doctl' -Arguments @('compute', 'droplet', 'list', '--format', 'ID,Name,Status,Region,Memory,PublicIPv4') -InstallHint $doHint"),
                    C("kubernetes", "List Kubernetes clusters.", @"Invoke-MuxTool -Tool 'doctl' -Arguments @('kubernetes', 'cluster', 'list') -InstallHint $doHint"),
                    C("databases", "List managed databases.", @"Invoke-MuxTool -Tool 'doctl' -Arguments @('databases', 'list') -InstallHint $doHint"),
                    C("volumes", "List block storage volumes.", @"Invoke-MuxTool -Tool 'doctl' -Arguments @('compute', 'volume', 'list') -InstallHint $doHint"),
                    C("domains", "List domains.", @"Invoke-MuxTool -Tool 'doctl' -Arguments @('compute', 'domain', 'list') -InstallHint $doHint")),

                f.Skill("do-apps", "Work with App Platform", "Lists apps, validates the app spec, deploys, and shows runtime logs.", true,
                    "The project has a .do/app.yaml, or the user deploys to DigitalOcean App Platform.",
                    "<app-id> [component] [--confirm <context>]",
                    "`spec-validate [path]` checks .do/app.yaml. `deploy <app-id>` starts a deployment behind the production guard. `logs <app-id> [component]` shows recent runtime logs.",
                    new[] { ".do/app.yaml", ".do/deploy.template.yaml" }, new[] { "doctl" },
                    C("list", "List apps.", @"Invoke-MuxTool -Tool 'doctl' -Arguments @('apps', 'list') -InstallHint $doHint"),
                    C("spec-validate", "Validate the app spec.", @"$spec = Get-MuxArg -Arguments $args -Index 0 -Default '.do/app.yaml'
Invoke-MuxTool -Tool 'doctl' -Arguments @('apps', 'spec', 'validate', $spec) -InstallHint $doHint"),
                    C("deploy", "Start a deployment.", @"$split = Split-MuxConfirm -Arguments $args
Assert-MuxNotProduction -Target $doContext -Confirm $split.Confirm
$app = Get-MuxArg -Arguments $split.Rest -Index 0
if (-not $app) { Exit-MuxNotApplicable 'pass an app id: do-apps deploy <app-id>' }
Invoke-MuxTool -Tool 'doctl' -Arguments @('apps', 'create-deployment', $app, '--wait') -InstallHint $doHint"),
                    C("logs", "Show recent runtime logs.", @"$app = Get-MuxArg -Arguments $args -Index 0
if (-not $app) { Exit-MuxNotApplicable 'pass an app id: do-apps logs <app-id> [component]' }
$logArgs = @('apps', 'logs', $app)
$component = Get-MuxArg -Arguments $args -Index 1
if ($component) { $logArgs += $component }
Invoke-MuxTool -Tool 'doctl' -Arguments ($logArgs + @('--type', 'run', '--tail', '200')) -InstallHint $doHint"))
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
