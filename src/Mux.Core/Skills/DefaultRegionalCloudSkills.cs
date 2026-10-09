namespace Mux.Core.Skills
{
    using System.Collections.Generic;

    /// <summary>
    /// The Alibaba Cloud, Huawei Cloud, IBM Cloud, and Linode (Akamai) default skills. Each provider gets a
    /// <c>*-whoami</c> skill and a read-mostly <c>*-infra</c> skill on its official CLI, listed only when that CLI is
    /// installed. The one change command (an IBM Code Engine image update) sits behind the production guard.
    /// </summary>
    public static class DefaultRegionalCloudSkills
    {
        #region Public-Methods

        /// <summary>Returns the regional cloud skill definitions.</summary>
        /// <returns>The definitions.</returns>
        public static IReadOnlyList<DefaultSkillDef> All()
        {
            ToolchainSkillFactory alibaba = new ToolchainSkillFactory(@"$aliyunHint = 'Install the Alibaba Cloud CLI (aliyun) from https://github.com/aliyun/aliyun-cli.'
$aliyunProfile = Get-MuxTarget -Label 'Alibaba Cloud profile' -Resolve { if ($env:ALIBABA_CLOUD_PROFILE) { $env:ALIBABA_CLOUD_PROFILE } else { $null = aliyun configure get 2>$null; if ($LASTEXITCODE -eq 0) { 'default' } } } -LoginHint 'Configure credentials with aliyun configure, then retry.'
", new[] { "alibaba", "aliyun", "cloud" }, null, new[] { "aliyun" }, ToolchainSkillFactory.GuardedExitNote);

            ToolchainSkillFactory huawei = new ToolchainSkillFactory(@"$hcloudHint = 'Install Huawei Cloud KooCLI (hcloud) from https://support.huaweicloud.com/intl/en-us/qs-hcli/.'
$hcloudProfile = Get-MuxTarget -Label 'Huawei Cloud profile' -Resolve { $null = hcloud configure list 2>$null; if ($LASTEXITCODE -eq 0) { 'default' } } -LoginHint 'Configure credentials with hcloud configure init, then retry.'
", new[] { "huawei", "hcloud", "cloud" }, null, new[] { "hcloud" }, ToolchainSkillFactory.GuardedExitNote);

            ToolchainSkillFactory ibm = new ToolchainSkillFactory(@"$ibmHint = 'Install the IBM Cloud CLI from https://cloud.ibm.com/docs/cli.'
$ibmAccount = Get-MuxTarget -Label 'IBM Cloud account' -Resolve { $target = ibmcloud target --output json 2>$null | ConvertFrom-Json; if ($target.account) { $target.account.name } } -LoginHint 'Sign in with ibmcloud login (or --sso), then retry. Mux does not sign in for you.'
", new[] { "ibm-cloud", "cloud" }, null, new[] { "ibmcloud" }, ToolchainSkillFactory.GuardedExitNote);

            ToolchainSkillFactory linode = new ToolchainSkillFactory(@"$linodeHint = 'Install linode-cli: pip install linode-cli.'
$linodeUser = Get-MuxTarget -Label 'Linode profile' -Resolve { linode-cli profile view --text --no-headers --format username 2>$null } -LoginHint 'Configure linode-cli (linode-cli configure, or set LINODE_CLI_TOKEN), then retry.'
", new[] { "linode", "akamai", "cloud" }, null, new[] { "linode-cli" }, ToolchainSkillFactory.GuardedExitNote);

            return new List<DefaultSkillDef>
            {
                alibaba.Skill("alibaba-whoami", "Show the Alibaba Cloud profile", "Shows the configured profile and the available regions.", false,
                    "Before Alibaba Cloud work, or when aliyun fails to authenticate.",
                    string.Empty,
                    "`profile` lists configured profiles; `regions` lists ECS regions the account can use.",
                    C("profile", "List configured profiles.", @"Invoke-MuxTool -Tool 'aliyun' -Arguments @('configure', 'list') -InstallHint $aliyunHint"),
                    C("regions", "List ECS regions.", @"Invoke-MuxTool -Tool 'aliyun' -Arguments @('ecs', 'DescribeRegions') -InstallHint $aliyunHint")),
                alibaba.Skill("alibaba-infra", "List Alibaba Cloud resources", "Lists ECS instances, OSS objects, ACK clusters, ApsaraDB RDS instances, and Function Compute functions.", false,
                    "The user asks what is running in an Alibaba Cloud account.",
                    "[oss://bucket]",
                    "Read-only. Output is the CLI's JSON; summarize the fields the user cares about. `oss-ls [oss://bucket]` uses ossutil when installed, otherwise the aliyun oss command.",
                    C("ecs-instances", "List ECS instances.", @"Invoke-MuxTool -Tool 'aliyun' -Arguments @('ecs', 'DescribeInstances') -InstallHint $aliyunHint"),
                    C("oss-ls", "List OSS buckets or objects.", @"$uri = Get-MuxArg -Arguments $args -Index 0
$ossArgs = @('ls')
if ($uri) { $ossArgs += $uri }
if (Test-MuxTool 'ossutil') { Invoke-MuxTool -Tool 'ossutil' -Arguments $ossArgs } else { Invoke-MuxTool -Tool 'aliyun' -Arguments (@('oss') + $ossArgs) -InstallHint $aliyunHint }"),
                    C("ack-clusters", "List ACK (Kubernetes) clusters.", @"Invoke-MuxTool -Tool 'aliyun' -Arguments @('cs', 'GET', '/clusters') -InstallHint $aliyunHint"),
                    C("rds-instances", "List ApsaraDB RDS instances.", @"Invoke-MuxTool -Tool 'aliyun' -Arguments @('rds', 'DescribeDBInstances') -InstallHint $aliyunHint"),
                    C("fc-functions", "List Function Compute functions.", @"Invoke-MuxTool -Tool 'aliyun' -Arguments @('fc', 'ListFunctions') -InstallHint $aliyunHint")),

                huawei.Skill("huawei-whoami", "Show the Huawei Cloud profile", "Shows the configured KooCLI profiles and the regions they use.", false,
                    "Before Huawei Cloud work, or when hcloud fails to authenticate.",
                    string.Empty,
                    "`profile` lists configured profiles; `regions` lists the regions the identity service reports.",
                    C("profile", "List configured profiles.", @"Invoke-MuxTool -Tool 'hcloud' -Arguments @('configure', 'list') -InstallHint $hcloudHint"),
                    C("regions", "List regions.", @"Invoke-MuxTool -Tool 'hcloud' -Arguments @('IAM', 'KeystoneListRegions') -InstallHint $hcloudHint")),
                huawei.Skill("huawei-infra", "List Huawei Cloud resources", "Lists ECS servers, OBS objects, CCE clusters, and RDS instances.", false,
                    "The user asks what is running in a Huawei Cloud account.",
                    "[obs://bucket]",
                    "Read-only. `obs-ls [obs://bucket]` needs obsutil.",
                    C("ecs-servers", "List ECS servers.", @"Invoke-MuxTool -Tool 'hcloud' -Arguments @('ECS', 'ListServersDetails') -InstallHint $hcloudHint"),
                    C("obs-ls", "List OBS buckets or objects.", @"$uri = Get-MuxArg -Arguments $args -Index 0
$obsArgs = @('ls')
if ($uri) { $obsArgs += $uri }
Invoke-MuxTool -Tool 'obsutil' -Arguments $obsArgs -InstallHint 'Install obsutil from Huawei Cloud OBS documentation.'"),
                    C("cce-clusters", "List CCE (Kubernetes) clusters.", @"Invoke-MuxTool -Tool 'hcloud' -Arguments @('CCE', 'ListClusters') -InstallHint $hcloudHint"),
                    C("rds-instances", "List RDS instances.", @"Invoke-MuxTool -Tool 'hcloud' -Arguments @('RDS', 'ListInstances') -InstallHint $hcloudHint")),

                ibm.Skill("ibm-whoami", "Show the IBM Cloud target", "Shows the targeted account, region, and resource group, or the account details.", false,
                    "Before IBM Cloud work, or when ibmcloud fails to authenticate.",
                    string.Empty,
                    "`target` shows the account, region, and resource group commands act on; switching them is the user's call.",
                    C("target", "Show the current target.", @"Invoke-MuxTool -Tool 'ibmcloud' -Arguments @('target') -InstallHint $ibmHint"),
                    C("account", "Show the account.", @"Invoke-MuxTool -Tool 'ibmcloud' -Arguments @('account', 'show') -InstallHint $ibmHint")),
                ibm.Skill("ibm-infra", "Work with IBM Cloud resources", "Lists VPC instances, Kubernetes clusters, Code Engine apps, and Object Storage buckets, and updates a Code Engine app's image.", true,
                    "The user asks what is running in IBM Cloud or deploys to Code Engine.",
                    "<app> <image> [--confirm <account>]",
                    "Listing needs the matching plugins (vpc-infrastructure, container-service, code-engine, cloud-object-storage). `code-engine-deploy <app> <image>` rolls out a new image behind the production guard.",
                    C("vpc-instances", "List VPC virtual server instances.", @"Invoke-MuxTool -Tool 'ibmcloud' -Arguments @('is', 'instances') -InstallHint $ibmHint"),
                    C("ks-clusters", "List Kubernetes and OpenShift clusters.", @"Invoke-MuxTool -Tool 'ibmcloud' -Arguments @('ks', 'cluster', 'ls') -InstallHint $ibmHint"),
                    C("code-engine-apps", "List Code Engine applications.", @"Invoke-MuxTool -Tool 'ibmcloud' -Arguments @('ce', 'application', 'list') -InstallHint $ibmHint"),
                    C("cos-buckets", "List Object Storage buckets.", @"Invoke-MuxTool -Tool 'ibmcloud' -Arguments @('cos', 'buckets') -InstallHint $ibmHint"),
                    C("code-engine-deploy", "Roll out a new image to a Code Engine app.", @"$split = Split-MuxConfirm -Arguments $args
Assert-MuxNotProduction -Target $ibmAccount -Confirm $split.Confirm
$app = Get-MuxArg -Arguments $split.Rest -Index 0
$image = Get-MuxArg -Arguments $split.Rest -Index 1
if (-not $app -or -not $image) { Exit-MuxNotApplicable 'pass an app and image: ibm-infra code-engine-deploy <app> <image>' }
Invoke-MuxTool -Tool 'ibmcloud' -Arguments @('ce', 'application', 'update', '--name', $app, '--image', $image) -InstallHint $ibmHint")),

                linode.Skill("linode-whoami", "Show the Linode profile", "Shows the configured profile and the account.", false,
                    "Before Linode (Akamai Connected Cloud) work, or when linode-cli fails to authenticate.",
                    string.Empty,
                    "`profile` shows the signed-in user; `account` shows the account and balance.",
                    C("profile", "Show the profile.", @"Invoke-MuxTool -Tool 'linode-cli' -Arguments @('profile', 'view') -InstallHint $linodeHint"),
                    C("account", "Show the account.", @"Invoke-MuxTool -Tool 'linode-cli' -Arguments @('account', 'view') -InstallHint $linodeHint")),
                linode.Skill("linode-infra", "List Linode resources", "Lists instances, LKE clusters, Object Storage buckets, and volumes.", false,
                    "The user asks what is running in a Linode account.",
                    string.Empty,
                    "Read-only. For an LKE cluster, save its kubeconfig (linode-cli lke kubeconfig-view) and use the k8s-* skills.",
                    C("instances", "List instances.", @"Invoke-MuxTool -Tool 'linode-cli' -Arguments @('linodes', 'list') -InstallHint $linodeHint"),
                    C("lke-clusters", "List LKE clusters.", @"Invoke-MuxTool -Tool 'linode-cli' -Arguments @('lke', 'clusters-list') -InstallHint $linodeHint"),
                    C("object-storage", "List Object Storage buckets.", @"Invoke-MuxTool -Tool 'linode-cli' -Arguments @('obj', 'ls') -InstallHint $linodeHint"),
                    C("volumes", "List volumes.", @"Invoke-MuxTool -Tool 'linode-cli' -Arguments @('volumes', 'list') -InstallHint $linodeHint"))
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
