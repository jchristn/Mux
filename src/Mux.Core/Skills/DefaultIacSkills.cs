namespace Mux.Core.Skills
{
    using System.Collections.Generic;

    /// <summary>
    /// The infrastructure-as-code default skills: Terraform (or OpenTofu) and Pulumi. Plans and previews are free to
    /// run; applying needs a saved plan (Terraform) and passes the production guard against the workspace or stack
    /// name. No destroy command is offered.
    /// </summary>
    public static class DefaultIacSkills
    {
        #region Public-Methods

        /// <summary>Returns the infrastructure-as-code skill definitions.</summary>
        /// <returns>The definitions.</returns>
        public static IReadOnlyList<DefaultSkillDef> All()
        {
            ToolchainSkillFactory terraform = new ToolchainSkillFactory(@"$tf = if (Test-MuxTool 'terraform') { 'terraform' } elseif (Test-MuxTool 'tofu') { 'tofu' } else { 'terraform' }
$tfHint = 'Install Terraform from https://developer.hashicorp.com/terraform/install or OpenTofu from https://opentofu.org.'
$tfDir = Find-MuxUp -Names @('*.tf')
if (-not $tfDir) { Exit-MuxNotApplicable 'no .tf files here or in a parent; this is not a Terraform configuration.' }
Set-Location -LiteralPath $tfDir
$workspace = Get-MuxTarget -Label 'Terraform workspace' -Resolve { & $tf workspace show 2>$null } -LoginHint 'Run terraform init first.'
", new[] { "terraform", "opentofu", "iac" }, new[] { "*.tf", "**/*.tf" }, new[] { "terraform|tofu" }, ToolchainSkillFactory.GuardedExitNote);

            ToolchainSkillFactory pulumi = new ToolchainSkillFactory(@"$pulumiHint = 'Install Pulumi from https://www.pulumi.com/docs/install/.'
$stackName = Get-MuxTarget -Label 'Pulumi stack' -Resolve { pulumi stack --show-name 2>$null } -LoginHint 'Sign in with pulumi login and select a stack with pulumi stack select, then retry.'
", new[] { "pulumi", "iac" }, new[] { "Pulumi.yaml" }, new[] { "pulumi" }, ToolchainSkillFactory.GuardedExitNote);

            ToolchainSkillFactory ansible = new ToolchainSkillFactory(@"$ansibleHint = 'Install Ansible (pip install ansible) and ansible-lint (pip install ansible-lint).'
$split = Split-MuxOptions -Arguments $args -Names @('-i', '--inventory', '--limit', '-l', '--confirm')
$inventory = @($split.Options['-i'], $split.Options['--inventory']) | Where-Object { $_ } | Select-Object -First 1
$limit = @($split.Options['--limit'], $split.Options['-l']) | Where-Object { $_ } | Select-Object -First 1
$playbook = Get-MuxArg -Arguments $split.Rest -Index 0
$common = @()
if ($inventory) { $common += @('-i', $inventory) }
if ($limit) { $common += @('--limit', $limit) }
function Assert-MuxPlaybook {
    if (-not $playbook) { Exit-MuxNotApplicable 'pass the playbook, for example site.yml.' }
    if (-not (Test-MuxDryRun) -and -not (Test-Path -LiteralPath $playbook -PathType Leaf)) { Exit-MuxNotApplicable ('playbook not found: ' + $playbook) }
}
", new[] { "ansible", "iac" }, new[] { "ansible.cfg", "**/ansible.cfg", "site.yml", "playbook*.yml", "**/playbooks/*.yml", "**/roles/*/tasks/main.yml" }, new[] { "ansible-playbook" }, ToolchainSkillFactory.GuardedExitNote);

            ToolchainSkillFactory bicep = new ToolchainSkillFactory(@"$azHint = 'Install the Azure CLI (https://learn.microsoft.com/cli/azure/install-azure-cli) and sign in with az login.'
$split = Split-MuxOptions -Arguments $args -Names @('--resource-group', '-g', '--parameters', '-p', '--confirm')
$file = Get-MuxArg -Arguments $split.Rest -Index 0
$group = @($split.Options['--resource-group'], $split.Options['-g']) | Where-Object { $_ } | Select-Object -First 1
$parameters = @($split.Options['--parameters'], $split.Options['-p']) | Where-Object { $_ } | Select-Object -First 1
function Assert-MuxBicepFile {
    if (-not $file) { Exit-MuxNotApplicable 'pass the Bicep file, for example main.bicep.' }
    if (-not (Test-MuxDryRun) -and -not (Test-Path -LiteralPath $file -PathType Leaf)) { Exit-MuxNotApplicable ('Bicep file not found: ' + $file) }
}
function Get-MuxDeploymentArgs {
    param([string]$Verb)
    if (-not $group) { Exit-MuxNotApplicable ('pass the target: bicep ' + $Verb + ' <file> --resource-group <name>') }
    $deployment = @('deployment', 'group', $Verb, '--resource-group', $group, '--template-file', $file)
    if ($parameters) { $deployment += @('--parameters', $parameters) }
    return ,$deployment
}
", new[] { "bicep", "azure", "iac" }, new[] { "*.bicep", "**/*.bicep" }, new[] { "az" }, ToolchainSkillFactory.GuardedExitNote);

            return new List<DefaultSkillDef>
            {
                ansible.Skill("ansible", "Lint, check, and run Ansible playbooks", "Ansible: lints playbooks, checks their syntax, previews changes with --check --diff, runs them behind the production guard, and shows the inventory.", true,
                    "The project has Ansible playbooks, roles, or ansible.cfg, or the user asks to configure servers with Ansible.",
                    "<playbook> [-i inventory] [--limit hosts] [--confirm target]",
                    "Work in this order: `lint`, `syntax <playbook>`, `check <playbook>` (a dry run with `--check --diff` that changes nothing on the hosts), show the user the diff, then `apply <playbook>`. Pass `-i <inventory>` and `--limit <hosts>` to any of them. `apply` treats the limit (or else the inventory, or else all) as the target and refuses one that matches the production pattern unless `--confirm <target>` repeats it after the user approves. `inventory` shows the host graph. There is no command that removes hosts or resources.",
                    C("lint", "Run ansible-lint.", @"$target = if ($playbook) { @($playbook) } else { @() }
Invoke-MuxTool -Tool 'ansible-lint' -Arguments $target -InstallHint $ansibleHint"),
                    C("syntax", "Check a playbook's syntax.", @"Assert-MuxPlaybook
Invoke-MuxTool -Tool 'ansible-playbook' -Arguments (@('--syntax-check') + $common + @($playbook)) -InstallHint $ansibleHint"),
                    C("check", "Preview a playbook with --check --diff.", @"Assert-MuxPlaybook
Invoke-MuxTool -Tool 'ansible-playbook' -Arguments (@('--check', '--diff') + $common + @($playbook)) -InstallHint $ansibleHint"),
                    C("apply", "Run a playbook (production guarded).", @"Assert-MuxPlaybook
$target = if ($limit) { $limit } elseif ($inventory) { [System.IO.Path]::GetFileNameWithoutExtension($inventory) } else { 'all' }
Write-Output ('Target: ' + $target)
Assert-MuxNotProduction -Target $target -Confirm ([string]$split.Options['--confirm'])
Invoke-MuxTool -Tool 'ansible-playbook' -Arguments (@('--diff') + $common + @($playbook)) -InstallHint $ansibleHint"),
                    C("inventory", "Show the inventory graph.", @"$inv = if ($inventory) { @('-i', $inventory) } else { @() }
Invoke-MuxTool -Tool 'ansible-inventory' -Arguments ($inv + @('--graph')) -InstallHint $ansibleHint")),

                bicep.Skill("bicep", "Build, preview, and deploy Bicep", "Azure Bicep: builds and lints Bicep files, previews a resource-group deployment with what-if, and deploys it behind the production guard.", true,
                    "The project has .bicep files, or the user asks to deploy Azure infrastructure with Bicep.",
                    "<file> [--resource-group name] [--parameters file] [--confirm group]",
                    "Work in this order: `lint <file>`, `build <file>` (compiles to ARM JSON on standard output, writing nothing), `what-if <file> --resource-group <name>`, show the user the result, then `deploy` with the same arguments. `--parameters <file.bicepparam | file.json>` passes parameters. `deploy` refuses a resource group that matches the production pattern unless `--confirm <group>` repeats it after the user approves. Only resource-group deployments are offered, and there is no delete command.",
                    C("build", "Compile a Bicep file to ARM JSON on standard output.", @"Assert-MuxBicepFile
Invoke-MuxTool -Tool 'az' -Arguments @('bicep', 'build', '--file', $file, '--stdout') -InstallHint $azHint"),
                    C("lint", "Lint a Bicep file.", @"Assert-MuxBicepFile
Invoke-MuxTool -Tool 'az' -Arguments @('bicep', 'lint', '--file', $file) -InstallHint $azHint"),
                    C("what-if", "Preview a resource-group deployment.", @"Assert-MuxBicepFile
Invoke-MuxTool -Tool 'az' -Arguments (Get-MuxDeploymentArgs 'what-if') -InstallHint $azHint"),
                    C("deploy", "Deploy to a resource group (production guarded).", @"Assert-MuxBicepFile
$deployment = Get-MuxDeploymentArgs 'create'
Assert-MuxNotProduction -Target $group -Confirm ([string]$split.Options['--confirm'])
Invoke-MuxTool -Tool 'az' -Arguments $deployment -InstallHint $azHint")),

                terraform.Skill("terraform", "Plan and apply Terraform", "Formats, validates, initializes, and plans; applies only a saved plan; lists state and outputs. Works with OpenTofu.", true,
                    "The project has .tf files, or the user asks to change infrastructure managed by Terraform or OpenTofu.",
                    "[--confirm <workspace>]",
                    "Work in this order: `fmt`, `validate`, `init` (once), `plan`, show the user the plan, then `apply`. `plan` writes mux.tfplan and `apply` applies exactly that saved plan, never a fresh one, so what the user reviewed is what runs. Applying in a workspace that matches the production pattern needs `--confirm <workspace>` after the user approves. There is no destroy command.\n\nTo review Terraform code (the user asks for a review, or before a first plan in an unfamiliar configuration), run `python3 \"${SKILL_DIR}/resources/scripts/tf_module_analyzer.py\" <dir>` for the module structure and `python3 \"${SKILL_DIR}/resources/scripts/tf_security_scanner.py\" <dir>` for security problems (add `--output json` for structured output), then check, reporting only what fails: variables have descriptions and types and sensitive ones set `sensitive = true`; outputs expose only what callers need; nothing is hardcoded that should be a variable or local; the backend is remote with locking and encryption at rest; environments are isolated by workspace or directory; providers are pinned with `~>` in a `required_providers` block and configured only in the root module; IAM is least privilege, storage and databases are encrypted, and no security group opens a sensitive port to 0.0.0.0/0.",
                    C("fmt", "Format .tf files in place.", @"Invoke-MuxTool -Tool $tf -Arguments @('fmt', '-recursive') -InstallHint $tfHint"),
                    C("validate", "Validate the configuration.", @"Invoke-MuxTool -Tool $tf -Arguments @('validate') -InstallHint $tfHint"),
                    C("init", "Initialize providers and the backend.", @"Invoke-MuxTool -Tool $tf -Arguments @('init', '-input=false') -InstallHint $tfHint"),
                    C("plan", "Write a plan to mux.tfplan and show it.", @"Invoke-MuxTool -Tool $tf -Arguments @('plan', '-input=false', '-out=mux.tfplan') -InstallHint $tfHint"),
                    C("apply", "Apply the saved mux.tfplan.", @"$split = Split-MuxConfirm -Arguments $args
if (-not (Test-MuxDryRun) -and -not (Test-Path -LiteralPath 'mux.tfplan')) { Exit-MuxNotApplicable 'no saved plan (mux.tfplan); run terraform plan first and review it with the user.' }
Assert-MuxNotProduction -Target $workspace -Confirm $split.Confirm
Invoke-MuxTool -Tool $tf -Arguments @('apply', '-input=false', 'mux.tfplan') -InstallHint $tfHint"),
                    C("state-list", "List resources in state.", @"Invoke-MuxTool -Tool $tf -Arguments @('state', 'list') -InstallHint $tfHint"),
                    C("output", "Show outputs (sensitive values stay hidden).", @"Invoke-MuxTool -Tool $tf -Arguments @('output') -InstallHint $tfHint")),

                pulumi.Skill("pulumi", "Preview and update Pulumi stacks", "Shows the account and stacks, previews changes, updates the stack, and shows outputs.", true,
                    "The project has a Pulumi.yaml, or the user asks to change infrastructure managed by Pulumi.",
                    "[--confirm <stack>]",
                    "Always run `preview` and show the user the diff before `up`. `up` on a stack whose name matches the production pattern needs `--confirm <stack>` after the user approves. Secret outputs stay masked.",
                    C("whoami", "Show the signed-in account.", @"Invoke-MuxTool -Tool 'pulumi' -Arguments @('whoami', '--verbose') -InstallHint $pulumiHint"),
                    C("stack", "List stacks.", @"Invoke-MuxTool -Tool 'pulumi' -Arguments @('stack', 'ls') -InstallHint $pulumiHint"),
                    C("preview", "Preview changes.", @"Invoke-MuxTool -Tool 'pulumi' -Arguments @('preview', '--diff', '--non-interactive') -InstallHint $pulumiHint"),
                    C("up", "Update the stack.", @"$split = Split-MuxConfirm -Arguments $args
Assert-MuxNotProduction -Target $stackName -Confirm $split.Confirm
Invoke-MuxTool -Tool 'pulumi' -Arguments @('up', '--yes', '--non-interactive', '--skip-preview') -InstallHint $pulumiHint"),
                    C("outputs", "Show stack outputs.", @"Invoke-MuxTool -Tool 'pulumi' -Arguments @('stack', 'output') -InstallHint $pulumiHint"))
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
