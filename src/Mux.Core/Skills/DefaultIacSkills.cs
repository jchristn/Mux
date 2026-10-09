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

            return new List<DefaultSkillDef>
            {
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
