namespace Mux.Core.Skills
{
    using System.Collections.Generic;

    /// <summary>
    /// The OpenStack default skills, built on the <c>openstack</c> CLI. The cloud comes from <c>OS_CLOUD</c> (a
    /// <c>clouds.yaml</c> entry) and is printed first. Heat stack creation and updates sit behind the production
    /// guard; nothing deletes.
    /// </summary>
    public static class DefaultOpenStackSkills
    {
        #region Private-Members

        private const string Setup = @"$osHint = 'Install the OpenStack client: pip install python-openstackclient. Set OS_CLOUD to a clouds.yaml entry.'
$cloud = Get-MuxTarget -Label 'OpenStack cloud' -Resolve { if ($env:OS_CLOUD) { $env:OS_CLOUD } elseif ($env:OS_AUTH_URL) { $env:OS_AUTH_URL } else { $null } } -LoginHint 'Set OS_CLOUD to an entry in clouds.yaml, or source an openrc file.'
";

        private static readonly string[] _AppliesTo = { "clouds.yaml", "**/clouds.yaml", "heat/**", "*.hot.yaml", "**/*.hot.yaml" };

        #endregion

        #region Public-Methods

        /// <summary>Returns the OpenStack skill definitions.</summary>
        /// <returns>The definitions.</returns>
        public static IReadOnlyList<DefaultSkillDef> All()
        {
            ToolchainSkillFactory f = new ToolchainSkillFactory(Setup, new[] { "openstack", "cloud" }, _AppliesTo, new[] { "openstack" }, ToolchainSkillFactory.GuardedExitNote);
            return new List<DefaultSkillDef>
            {
                f.Skill("openstack-whoami", "Show the OpenStack identity", "Shows the authenticated token scope, the service catalog, and project quotas.", false,
                    "Before other OpenStack work, or when authentication fails.",
                    string.Empty,
                    "`token` confirms authentication and shows the project; `catalog` lists the services and endpoints; `quotas` shows project limits.",
                    ToolchainSkillFactory.Command("token", "Issue a token to confirm authentication.", @"Invoke-MuxTool -Tool 'openstack' -Arguments @('token', 'issue', '-c', 'project_id', '-c', 'user_id', '-c', 'expires') -InstallHint $osHint"),
                    ToolchainSkillFactory.Command("catalog", "List the service catalog.", @"Invoke-MuxTool -Tool 'openstack' -Arguments @('catalog', 'list') -InstallHint $osHint"),
                    ToolchainSkillFactory.Command("quotas", "Show project quotas.", @"Invoke-MuxTool -Tool 'openstack' -Arguments @('quota', 'show') -InstallHint $osHint")),

                f.Skill("openstack-inspect", "Inspect OpenStack resources", "Lists servers, images, flavors, networks, volumes, or Heat stacks.", false,
                    "The user asks what is running in an OpenStack project.",
                    string.Empty,
                    "Each command lists one resource type in the current project.",
                    ToolchainSkillFactory.Command("servers", "List servers.", @"Invoke-MuxTool -Tool 'openstack' -Arguments @('server', 'list') -InstallHint $osHint"),
                    ToolchainSkillFactory.Command("images", "List images.", @"Invoke-MuxTool -Tool 'openstack' -Arguments @('image', 'list') -InstallHint $osHint"),
                    ToolchainSkillFactory.Command("flavors", "List flavors.", @"Invoke-MuxTool -Tool 'openstack' -Arguments @('flavor', 'list') -InstallHint $osHint"),
                    ToolchainSkillFactory.Command("networks", "List networks.", @"Invoke-MuxTool -Tool 'openstack' -Arguments @('network', 'list') -InstallHint $osHint"),
                    ToolchainSkillFactory.Command("volumes", "List volumes.", @"Invoke-MuxTool -Tool 'openstack' -Arguments @('volume', 'list') -InstallHint $osHint"),
                    ToolchainSkillFactory.Command("stacks", "List Heat stacks.", @"Invoke-MuxTool -Tool 'openstack' -Arguments @('stack', 'list') -InstallHint $osHint")),

                f.Skill("openstack-heat", "Manage Heat stacks", "Validates templates, previews, creates, or updates Heat stacks, and shows stack events.", true,
                    "The user asks to deploy or change infrastructure described by a Heat template.",
                    "<stack> <template> [--confirm <cloud>]",
                    "Run `validate <template>` and `preview <stack> <template>` (a dry run) before `create` or `update`. Creating or updating on a cloud that matches the production pattern needs `--confirm <cloud>`, added only after the user approves. `events <stack>` shows progress and failures.",
                    ToolchainSkillFactory.Command("validate", "Validate a template.", @"$template = Get-MuxArg -Arguments $args -Index 0
if (-not $template) { Exit-MuxNotApplicable 'pass a template: openstack-heat validate <template>' }
Invoke-MuxTool -Tool 'openstack' -Arguments @('orchestration', 'template', 'validate', '-t', $template) -InstallHint $osHint"),
                    ToolchainSkillFactory.Command("preview", "Dry-run a stack creation.", @"$stack = Get-MuxArg -Arguments $args -Index 0
$template = Get-MuxArg -Arguments $args -Index 1
if (-not $stack -or -not $template) { Exit-MuxNotApplicable 'pass a stack and template: openstack-heat preview <stack> <template>' }
Invoke-MuxTool -Tool 'openstack' -Arguments @('stack', 'create', '--dry-run', '-t', $template, $stack) -InstallHint $osHint"),
                    ToolchainSkillFactory.Command("create", "Create a stack.", @"$split = Split-MuxConfirm -Arguments $args
Assert-MuxNotProduction -Target $cloud -Confirm $split.Confirm
$stack = Get-MuxArg -Arguments $split.Rest -Index 0
$template = Get-MuxArg -Arguments $split.Rest -Index 1
if (-not $stack -or -not $template) { Exit-MuxNotApplicable 'pass a stack and template: openstack-heat create <stack> <template>' }
Invoke-MuxTool -Tool 'openstack' -Arguments @('stack', 'create', '--wait', '-t', $template, $stack) -InstallHint $osHint"),
                    ToolchainSkillFactory.Command("update", "Update a stack.", @"$split = Split-MuxConfirm -Arguments $args
Assert-MuxNotProduction -Target $cloud -Confirm $split.Confirm
$stack = Get-MuxArg -Arguments $split.Rest -Index 0
$template = Get-MuxArg -Arguments $split.Rest -Index 1
if (-not $stack -or -not $template) { Exit-MuxNotApplicable 'pass a stack and template: openstack-heat update <stack> <template>' }
Invoke-MuxTool -Tool 'openstack' -Arguments @('stack', 'update', '--wait', '-t', $template, $stack) -InstallHint $osHint"),
                    ToolchainSkillFactory.Command("events", "Show a stack's events.", @"$stack = Get-MuxArg -Arguments $args -Index 0
if (-not $stack) { Exit-MuxNotApplicable 'pass a stack: openstack-heat events <stack>' }
Invoke-MuxTool -Tool 'openstack' -Arguments @('stack', 'event', 'list', $stack) -InstallHint $osHint"))
            };
        }

        #endregion
    }
}
