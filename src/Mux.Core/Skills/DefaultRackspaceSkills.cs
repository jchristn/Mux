namespace Mux.Core.Skills
{
    using System.Collections.Generic;

    /// <summary>
    /// The Rackspace default skill. Rackspace Cloud is OpenStack-based, so it runs the <c>openstack</c> CLI against a
    /// Rackspace <c>clouds.yaml</c> entry (<c>OS_CLOUD</c>, default <c>rackspace</c>); Rackspace Spot clusters are
    /// listed with <c>spotctl</c> when it is installed. Read-only.
    /// </summary>
    public static class DefaultRackspaceSkills
    {
        #region Public-Methods

        /// <summary>Returns the Rackspace skill definitions.</summary>
        /// <returns>The definitions.</returns>
        public static IReadOnlyList<DefaultSkillDef> All()
        {
            ToolchainSkillFactory f = new ToolchainSkillFactory(@"if (-not $env:OS_CLOUD) { $env:OS_CLOUD = 'rackspace' }
$osHint = 'Install the OpenStack client (pip install python-openstackclient) and add a rackspace entry to clouds.yaml.'
$cloud = Get-MuxTarget -Label 'Rackspace cloud' -Resolve { $env:OS_CLOUD } -LoginHint 'Set OS_CLOUD to your Rackspace clouds.yaml entry.'
", new[] { "rackspace", "openstack", "cloud" }, null, new[] { "openstack" }, ToolchainSkillFactory.GuardedExitNote);
            return new List<DefaultSkillDef>
            {
                f.Skill("rackspace", "Inspect Rackspace Cloud", "Confirms the Rackspace identity, lists servers, or lists Rackspace Spot clusters.", false,
                    "The user works with Rackspace Cloud servers or Rackspace Spot Kubernetes clusters.",
                    string.Empty,
                    "`whoami` issues a token for the Rackspace cloud entry; `servers` lists servers. `spot-clusters` uses the Rackspace Spot CLI (spotctl) when installed; once you have a Spot kubeconfig, use the k8s-* skills for the cluster itself. For Heat stacks use openstack-heat with OS_CLOUD set to the Rackspace entry.",
                    ToolchainSkillFactory.Command("whoami", "Confirm authentication.", @"Invoke-MuxTool -Tool 'openstack' -Arguments @('token', 'issue', '-c', 'project_id', '-c', 'expires') -InstallHint $osHint"),
                    ToolchainSkillFactory.Command("servers", "List servers.", @"Invoke-MuxTool -Tool 'openstack' -Arguments @('server', 'list') -InstallHint $osHint"),
                    ToolchainSkillFactory.Command("spot-clusters", "List Rackspace Spot cloudspaces.", @"Invoke-MuxTool -Tool 'spotctl' -Arguments @('cloudspaces', 'list') -InstallHint 'Install the Rackspace Spot CLI (spotctl), or download the kubeconfig from the Spot console and use the k8s-* skills.'"))
            };
        }

        #endregion
    }
}
