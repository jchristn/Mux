namespace Mux.Core.Skills
{
    using System.Collections.Generic;

    /// <summary>
    /// The Azure default skills, built on the Azure CLI (<c>az</c>) and, for <c>azure.yaml</c> projects, the Azure
    /// Developer CLI (<c>azd</c>). Every command confirms the signed-in subscription first and prints it; changes sit
    /// behind the production guard (matched against the subscription name). Nothing deletes.
    /// </summary>
    public static class DefaultAzureSkills
    {
        #region Private-Members

        private const string Setup = @"$azHint = 'Install the Azure CLI from https://learn.microsoft.com/cli/azure/install-azure-cli.'
$subscription = Get-MuxTarget -Label 'Azure subscription' -Resolve { az account show --query name --output tsv } -LoginHint 'Sign in with az login, then retry. Mux does not sign in for you.'
";

        #endregion

        #region Public-Methods

        /// <summary>Returns the Azure skill definitions.</summary>
        /// <returns>The definitions.</returns>
        public static IReadOnlyList<DefaultSkillDef> All()
        {
            ToolchainSkillFactory f = new ToolchainSkillFactory(Setup, new[] { "azure", "cloud" }, null, new[] { "az" }, ToolchainSkillFactory.GuardedExitNote);
            return new List<DefaultSkillDef>
            {
                f.Skill("azure-whoami", "Show the Azure identity", "Shows the signed-in account and subscription, or lists subscriptions.", false,
                    "Before any Azure work, or when an az command fails with an authentication or permission error.",
                    string.Empty,
                    "`account` shows the active subscription and tenant; `subscriptions` lists every subscription the account can use. Switching subscriptions is left to the user (az account set).",
                    C("account", "Show the active subscription.", @"Invoke-MuxTool -Tool 'az' -Arguments @('account', 'show', '--output', 'table') -InstallHint $azHint"),
                    C("subscriptions", "List subscriptions.", @"Invoke-MuxTool -Tool 'az' -Arguments @('account', 'list', '--output', 'table') -InstallHint $azHint")),

                f.Skill("azure-resources", "List Azure resources", "Lists resource groups and resources, or shows one resource.", false,
                    "The user asks what exists in an Azure subscription or resource group.",
                    "[resource-group | resource-id]",
                    "`groups` lists resource groups; `list [group]` lists resources in the subscription or one group; `show <resource-id>` prints one resource.",
                    C("groups", "List resource groups.", @"Invoke-MuxTool -Tool 'az' -Arguments @('group', 'list', '--output', 'table') -InstallHint $azHint"),
                    C("list", "List resources.", @"$group = Get-MuxArg -Arguments $args -Index 0
$azArgs = @('resource', 'list', '--output', 'table')
if ($group) { $azArgs += @('--resource-group', $group) }
Invoke-MuxTool -Tool 'az' -Arguments $azArgs -InstallHint $azHint"),
                    C("show", "Show one resource by id.", @"$id = Get-MuxArg -Arguments $args -Index 0
if (-not $id) { Exit-MuxNotApplicable 'pass a resource id: azure-resources show <resource-id>' }
Invoke-MuxTool -Tool 'az' -Arguments @('resource', 'show', '--ids', $id, '--output', 'json') -InstallHint $azHint")),

                f.Skill("azure-compute", "Work with Azure VMs", "Lists VMs and starts, stops, or deallocates one.", true,
                    "The user asks about Azure virtual machines.",
                    "<resource-group> <vm> [--confirm <subscription>]",
                    "`vm-list` reads. `vm-start`, `vm-stop` (still billed for compute), and `vm-deallocate` (stops billing for compute) take `<group> <name>`; on a production subscription they need `--confirm <subscription>` after the user approves.",
                    C("vm-list", "List VMs with power state.", @"Invoke-MuxTool -Tool 'az' -Arguments @('vm', 'list', '--show-details', '--query', '[].{name:name, group:resourceGroup, state:powerState, size:hardwareProfile.vmSize}', '--output', 'table') -InstallHint $azHint"),
                    C("vm-start", "Start a VM.", VmAction("start")),
                    C("vm-stop", "Stop a VM.", VmAction("stop")),
                    C("vm-deallocate", "Deallocate a VM.", VmAction("deallocate"))),

                f.Skill("azure-containers", "Work with AKS, ACR, and Container Apps", "Lists AKS clusters and writes kubeconfig, works with ACR, and lists, reads logs of, or updates Container Apps.", true,
                    "The user deploys or debugs containers on Azure.",
                    "<resource-group> <name> [image] [--confirm <subscription>]",
                    "`aks-credentials <group> <cluster>` adds a kubeconfig context, after which the k8s-* skills apply. `acr-login <registry>` logs Docker in. `containerapp-logs <group> <app>` shows recent console logs; `containerapp-update <group> <app> <image>` rolls out a new image behind the production guard.",
                    C("aks-list", "List AKS clusters.", @"Invoke-MuxTool -Tool 'az' -Arguments @('aks', 'list', '--output', 'table') -InstallHint $azHint"),
                    C("aks-credentials", "Add a kubeconfig context for a cluster.", @"$group = Get-MuxArg -Arguments $args -Index 0
$name = Get-MuxArg -Arguments $args -Index 1
if (-not $group -or -not $name) { Exit-MuxNotApplicable 'pass a group and cluster: azure-containers aks-credentials <group> <cluster>' }
Invoke-MuxTool -Tool 'az' -Arguments @('aks', 'get-credentials', '--resource-group', $group, '--name', $name) -InstallHint $azHint"),
                    C("acr-list", "List container registries.", @"Invoke-MuxTool -Tool 'az' -Arguments @('acr', 'list', '--output', 'table') -InstallHint $azHint"),
                    C("acr-login", "Log Docker in to a registry.", @"$name = Get-MuxArg -Arguments $args -Index 0
if (-not $name) { Exit-MuxNotApplicable 'pass a registry: azure-containers acr-login <registry>' }
Invoke-MuxTool -Tool 'az' -Arguments @('acr', 'login', '--name', $name) -InstallHint $azHint"),
                    C("containerapp-list", "List Container Apps.", @"Invoke-MuxTool -Tool 'az' -Arguments @('containerapp', 'list', '--output', 'table') -InstallHint $azHint"),
                    C("containerapp-logs", "Show a Container App's recent logs.", @"$group = Get-MuxArg -Arguments $args -Index 0
$name = Get-MuxArg -Arguments $args -Index 1
if (-not $group -or -not $name) { Exit-MuxNotApplicable 'pass a group and app: azure-containers containerapp-logs <group> <app>' }
Invoke-MuxTool -Tool 'az' -Arguments @('containerapp', 'logs', 'show', '--resource-group', $group, '--name', $name, '--tail', '200', '--format', 'text') -InstallHint $azHint"),
                    C("containerapp-update", "Roll out a new image.", @"$split = Split-MuxConfirm -Arguments $args
Assert-MuxNotProduction -Target $subscription -Confirm $split.Confirm
$group = Get-MuxArg -Arguments $split.Rest -Index 0
$name = Get-MuxArg -Arguments $split.Rest -Index 1
$image = Get-MuxArg -Arguments $split.Rest -Index 2
if (-not $group -or -not $name -or -not $image) { Exit-MuxNotApplicable 'pass a group, app, and image: azure-containers containerapp-update <group> <app> <image>' }
Invoke-MuxTool -Tool 'az' -Arguments @('containerapp', 'update', '--resource-group', $group, '--name', $name, '--image', $image, '--output', 'table') -InstallHint $azHint")),

                f.Skill("azure-apps", "Work with App Service, Functions, and azd", "Lists and shows web apps and function apps, deploys a package to a web app, and previews or deploys an azd project.", true,
                    "The user deploys a web app or function app, or the project has an azure.yaml (Azure Developer CLI).",
                    "<resource-group> <app> [package] [--confirm <subscription>]",
                    "`webapp-list`, `webapp-show <group> <app>`, and `functionapp-list` read. `webapp-deploy <group> <app> <zip-or-folder>` deploys behind the production guard. For azd projects, run `azd-preview` (azd provision --preview) before `azd-deploy`.",
                    C("webapp-list", "List web apps.", @"Invoke-MuxTool -Tool 'az' -Arguments @('webapp', 'list', '--query', '[].{name:name, group:resourceGroup, state:state, host:defaultHostName}', '--output', 'table') -InstallHint $azHint"),
                    C("webapp-show", "Show one web app.", @"$group = Get-MuxArg -Arguments $args -Index 0
$name = Get-MuxArg -Arguments $args -Index 1
if (-not $group -or -not $name) { Exit-MuxNotApplicable 'pass a group and app: azure-apps webapp-show <group> <app>' }
Invoke-MuxTool -Tool 'az' -Arguments @('webapp', 'show', '--resource-group', $group, '--name', $name, '--output', 'table') -InstallHint $azHint"),
                    C("webapp-deploy", "Deploy a package to a web app.", @"$split = Split-MuxConfirm -Arguments $args
Assert-MuxNotProduction -Target $subscription -Confirm $split.Confirm
$group = Get-MuxArg -Arguments $split.Rest -Index 0
$name = Get-MuxArg -Arguments $split.Rest -Index 1
$package = Get-MuxArg -Arguments $split.Rest -Index 2
if (-not $group -or -not $name -or -not $package) { Exit-MuxNotApplicable 'pass a group, app, and package: azure-apps webapp-deploy <group> <app> <zip-or-folder>' }
Invoke-MuxTool -Tool 'az' -Arguments @('webapp', 'deploy', '--resource-group', $group, '--name', $name, '--src-path', $package) -InstallHint $azHint"),
                    C("functionapp-list", "List function apps.", @"Invoke-MuxTool -Tool 'az' -Arguments @('functionapp', 'list', '--query', '[].{name:name, group:resourceGroup, state:state}', '--output', 'table') -InstallHint $azHint"),
                    C("azd-preview", "Preview infrastructure changes for an azd project.", @"Invoke-MuxTool -Tool 'azd' -Arguments @('provision', '--preview', '--no-prompt') -InstallHint 'Install the Azure Developer CLI from https://aka.ms/azd-install.'"),
                    C("azd-deploy", "Deploy an azd project.", @"$split = Split-MuxConfirm -Arguments $args
Assert-MuxNotProduction -Target $subscription -Confirm $split.Confirm
Invoke-MuxTool -Tool 'azd' -Arguments @('deploy', '--no-prompt') -InstallHint 'Install the Azure Developer CLI from https://aka.ms/azd-install.'")),

                f.Skill("azure-data", "Inspect Azure storage and databases", "Lists storage accounts, blobs in a container, SQL servers, and Cosmos DB accounts.", false,
                    "The user asks about Azure Storage, Azure SQL, or Cosmos DB resources.",
                    "<account> <container>",
                    "Read-only. `blob-ls <account> <container>` uses your Azure AD sign-in (--auth-mode login), never account keys.",
                    C("storage-accounts", "List storage accounts.", @"Invoke-MuxTool -Tool 'az' -Arguments @('storage', 'account', 'list', '--query', '[].{name:name, group:resourceGroup, kind:kind, sku:sku.name}', '--output', 'table') -InstallHint $azHint"),
                    C("blob-ls", "List blobs in a container.", @"$account = Get-MuxArg -Arguments $args -Index 0
$container = Get-MuxArg -Arguments $args -Index 1
if (-not $account -or -not $container) { Exit-MuxNotApplicable 'pass an account and container: azure-data blob-ls <account> <container>' }
Invoke-MuxTool -Tool 'az' -Arguments @('storage', 'blob', 'list', '--account-name', $account, '--container-name', $container, '--auth-mode', 'login', '--query', '[].{name:name, size:properties.contentLength, modified:properties.lastModified}', '--output', 'table') -InstallHint $azHint"),
                    C("sql-servers", "List Azure SQL servers.", @"Invoke-MuxTool -Tool 'az' -Arguments @('sql', 'server', 'list', '--output', 'table') -InstallHint $azHint"),
                    C("cosmos-accounts", "List Cosmos DB accounts.", @"Invoke-MuxTool -Tool 'az' -Arguments @('cosmosdb', 'list', '--query', '[].{name:name, group:resourceGroup, kind:kind}', '--output', 'table') -InstallHint $azHint"))
            };
        }

        #endregion

        #region Private-Methods

        private static string VmAction(string verb)
        {
            return @"$split = Split-MuxConfirm -Arguments $args
Assert-MuxNotProduction -Target $subscription -Confirm $split.Confirm
$group = Get-MuxArg -Arguments $split.Rest -Index 0
$name = Get-MuxArg -Arguments $split.Rest -Index 1
if (-not $group -or -not $name) { Exit-MuxNotApplicable 'pass a group and VM name: azure-compute vm-" + verb + @" <group> <vm>' }
Invoke-MuxTool -Tool 'az' -Arguments @('vm', '" + verb + @"', '--resource-group', $group, '--name', $name) -InstallHint $azHint";
        }

        private static DefaultSkillCommandDef C(string name, string description, string code)
        {
            return ToolchainSkillFactory.Command(name, description, code);
        }

        #endregion
    }
}
