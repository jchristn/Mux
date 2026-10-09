namespace Mux.Core.Skills
{
    using System.Collections.Generic;

    /// <summary>
    /// The Google Cloud default skills, built on <c>gcloud</c> (and <c>bq</c> for BigQuery). Every command reads the
    /// active project first and prints it; changes sit behind the production guard (matched against the project id).
    /// Cloud Run deploys go out with no traffic and are promoted separately. Nothing deletes.
    /// </summary>
    public static class DefaultGcpSkills
    {
        #region Private-Members

        private const string Setup = @"$gcloudHint = 'Install the Google Cloud CLI from https://cloud.google.com/sdk/docs/install.'
$project = Get-MuxTarget -Label 'Google Cloud project' -Resolve { gcloud config get-value project 2>$null } -LoginHint 'Sign in with gcloud auth login and set a project with gcloud config set project <id>. Mux does not sign in for you.'
";

        #endregion

        #region Public-Methods

        /// <summary>Returns the Google Cloud skill definitions.</summary>
        /// <returns>The definitions.</returns>
        public static IReadOnlyList<DefaultSkillDef> All()
        {
            ToolchainSkillFactory f = new ToolchainSkillFactory(Setup, new[] { "gcp", "google-cloud", "cloud" }, null, new[] { "gcloud" }, ToolchainSkillFactory.GuardedExitNote);
            return new List<DefaultSkillDef>
            {
                f.Skill("gcp-whoami", "Show the Google Cloud identity", "Shows the signed-in accounts, the active project, and the gcloud configuration.", false,
                    "Before any Google Cloud work, or when gcloud fails with an authentication or permission error.",
                    string.Empty,
                    "`account` lists credentialed accounts and marks the active one; `project` prints the active project; `config` prints the active configuration (project, region, zone).",
                    C("account", "List signed-in accounts.", @"Invoke-MuxTool -Tool 'gcloud' -Arguments @('auth', 'list') -InstallHint $gcloudHint"),
                    C("project", "Print the active project.", @"Invoke-MuxTool -Tool 'gcloud' -Arguments @('config', 'get-value', 'project') -InstallHint $gcloudHint"),
                    C("config", "Print the active configuration.", @"Invoke-MuxTool -Tool 'gcloud' -Arguments @('config', 'list') -InstallHint $gcloudHint")),

                f.Skill("gcp-compute", "Work with Compute Engine", "Lists instances and starts or stops one.", true,
                    "The user asks about Compute Engine VMs.",
                    "<instance> <zone> [--confirm <project>]",
                    "`instances` reads. `start <instance> <zone>` and `stop <instance> <zone>` change state behind the production guard.",
                    C("instances", "List instances.", @"Invoke-MuxTool -Tool 'gcloud' -Arguments @('compute', 'instances', 'list') -InstallHint $gcloudHint"),
                    C("start", "Start an instance.", Instance("start")),
                    C("stop", "Stop an instance.", Instance("stop"))),

                f.Skill("gcp-run", "Work with Cloud Run, Functions, and App Engine", "Lists services, reads recent logs, deploys a Cloud Run revision without traffic, promotes it, and lists Functions and App Engine versions.", true,
                    "The user deploys or debugs serverless workloads on Google Cloud.",
                    "<service> [image] [region] [--confirm <project>]",
                    "`run-deploy <service> <image> [region]` creates a new revision with no traffic so it can be tested at its tagged URL; `run-promote <service> [region]` sends all traffic to the latest revision. Both sit behind the production guard. `run-logs <service>` shows the last hour, at most 100 entries.",
                    C("run-services", "List Cloud Run services.", @"Invoke-MuxTool -Tool 'gcloud' -Arguments @('run', 'services', 'list') -InstallHint $gcloudHint"),
                    C("run-logs", "Show a service's recent logs.", @"$service = Get-MuxArg -Arguments $args -Index 0
if (-not $service) { Exit-MuxNotApplicable 'pass a service: gcp-run run-logs <service>' }
Invoke-MuxTool -Tool 'gcloud' -Arguments @('logging', 'read', ('resource.type=cloud_run_revision AND resource.labels.service_name=' + $service), '--freshness', '1h', '--limit', '100', '--format', 'value(timestamp,severity,textPayload)') -InstallHint $gcloudHint"),
                    C("run-deploy", "Deploy a new revision with no traffic.", @"$split = Split-MuxConfirm -Arguments $args
Assert-MuxNotProduction -Target $project -Confirm $split.Confirm
$service = Get-MuxArg -Arguments $split.Rest -Index 0
$image = Get-MuxArg -Arguments $split.Rest -Index 1
if (-not $service -or -not $image) { Exit-MuxNotApplicable 'pass a service and image: gcp-run run-deploy <service> <image> [region]' }
$runArgs = @('run', 'deploy', $service, '--image', $image, '--no-traffic', '--tag', 'candidate')
$region = Get-MuxArg -Arguments $split.Rest -Index 2
if ($region) { $runArgs += @('--region', $region) }
Invoke-MuxTool -Tool 'gcloud' -Arguments $runArgs -InstallHint $gcloudHint"),
                    C("run-promote", "Send all traffic to the latest revision.", @"$split = Split-MuxConfirm -Arguments $args
Assert-MuxNotProduction -Target $project -Confirm $split.Confirm
$service = Get-MuxArg -Arguments $split.Rest -Index 0
if (-not $service) { Exit-MuxNotApplicable 'pass a service: gcp-run run-promote <service> [region]' }
$runArgs = @('run', 'services', 'update-traffic', $service, '--to-latest')
$region = Get-MuxArg -Arguments $split.Rest -Index 1
if ($region) { $runArgs += @('--region', $region) }
Invoke-MuxTool -Tool 'gcloud' -Arguments $runArgs -InstallHint $gcloudHint"),
                    C("functions-list", "List Cloud Functions.", @"Invoke-MuxTool -Tool 'gcloud' -Arguments @('functions', 'list') -InstallHint $gcloudHint"),
                    C("appengine-versions", "List App Engine versions.", @"Invoke-MuxTool -Tool 'gcloud' -Arguments @('app', 'versions', 'list') -InstallHint $gcloudHint")),

                f.Skill("gcp-gke", "Work with GKE clusters", "Lists GKE clusters and writes kubeconfig for one.", false,
                    "The user works with Google Kubernetes Engine.",
                    "<cluster> <location>",
                    "`credentials <cluster> <location>` adds a kubeconfig context, after which the k8s-* skills apply.",
                    C("clusters", "List clusters.", @"Invoke-MuxTool -Tool 'gcloud' -Arguments @('container', 'clusters', 'list') -InstallHint $gcloudHint"),
                    C("credentials", "Add a kubeconfig context for a cluster.", @"$cluster = Get-MuxArg -Arguments $args -Index 0
$location = Get-MuxArg -Arguments $args -Index 1
if (-not $cluster -or -not $location) { Exit-MuxNotApplicable 'pass a cluster and location: gcp-gke credentials <cluster> <region-or-zone>' }
Invoke-MuxTool -Tool 'gcloud' -Arguments @('container', 'clusters', 'get-credentials', $cluster, '--location', $location) -InstallHint $gcloudHint")),

                f.Skill("gcp-storage", "Work with Cloud Storage", "Lists buckets and objects, and syncs folders with a dry run first.", true,
                    "The user asks about Cloud Storage buckets or objects, or to upload or download a folder.",
                    "<gs://bucket/prefix> [destination] [apply] [--confirm <project>]",
                    "`rsync <source> <destination>` runs with --dry-run and shows what would copy; add `apply` as the third argument to copy for real. It never deletes objects at the destination.",
                    C("buckets", "List buckets.", @"Invoke-MuxTool -Tool 'gcloud' -Arguments @('storage', 'buckets', 'list', '--format', 'table(name,location,storageClass)') -InstallHint $gcloudHint"),
                    C("ls", "List objects under a prefix.", @"$uri = Get-MuxArg -Arguments $args -Index 0
if (-not $uri) { Exit-MuxNotApplicable 'pass a location: gcp-storage ls gs://bucket/prefix' }
Invoke-MuxTool -Tool 'gcloud' -Arguments @('storage', 'ls', '--long', $uri) -InstallHint $gcloudHint"),
                    C("rsync", "Sync a folder, as a dry run unless apply is passed.", @"$split = Split-MuxConfirm -Arguments $args
$source = Get-MuxArg -Arguments $split.Rest -Index 0
$destination = Get-MuxArg -Arguments $split.Rest -Index 1
if (-not $source -or -not $destination) { Exit-MuxNotApplicable 'pass a source and destination: gcp-storage rsync <source> <destination> [apply]' }
$apply = (Get-MuxArg -Arguments $split.Rest -Index 2) -eq 'apply'
$syncArgs = @('storage', 'rsync', '--recursive', $source, $destination)
if (-not $apply) { $syncArgs += '--dry-run'; Write-Output 'Preview only (--dry-run). Re-run with apply to copy.' }
else { Assert-MuxNotProduction -Target $project -Confirm $split.Confirm }
Invoke-MuxTool -Tool 'gcloud' -Arguments $syncArgs -InstallHint $gcloudHint")),

                f.Skill("gcp-data", "Inspect Google Cloud databases", "Lists Cloud SQL instances, Firestore indexes, and BigQuery datasets, and estimates a BigQuery query's cost.", false,
                    "The user asks about Cloud SQL, Firestore, or BigQuery, or wants to know what a query would scan.",
                    "<sql>",
                    "Read-only. `bigquery-dry-run <sql>` reports the bytes a standard-SQL query would process without running it, which is how BigQuery cost is estimated.",
                    C("sql-instances", "List Cloud SQL instances.", @"Invoke-MuxTool -Tool 'gcloud' -Arguments @('sql', 'instances', 'list') -InstallHint $gcloudHint"),
                    C("firestore-indexes", "List Firestore composite indexes.", @"Invoke-MuxTool -Tool 'gcloud' -Arguments @('firestore', 'indexes', 'composite', 'list') -InstallHint $gcloudHint"),
                    C("bigquery-datasets", "List BigQuery datasets.", @"Invoke-MuxTool -Tool 'bq' -Arguments @('ls') -InstallHint $gcloudHint"),
                    C("bigquery-dry-run", "Estimate the bytes a query would scan.", @"$sql = Get-MuxArg -Arguments $args -Index 0
if (-not $sql) { Exit-MuxNotApplicable 'pass a query: gcp-data bigquery-dry-run ""SELECT ...""' }
Invoke-MuxTool -Tool 'bq' -Arguments @('query', '--dry_run', '--use_legacy_sql=false', $sql) -InstallHint $gcloudHint"))
            };
        }

        #endregion

        #region Private-Methods

        private static string Instance(string verb)
        {
            return @"$split = Split-MuxConfirm -Arguments $args
Assert-MuxNotProduction -Target $project -Confirm $split.Confirm
$name = Get-MuxArg -Arguments $split.Rest -Index 0
$zone = Get-MuxArg -Arguments $split.Rest -Index 1
if (-not $name -or -not $zone) { Exit-MuxNotApplicable 'pass an instance and zone: gcp-compute " + verb + @" <instance> <zone>' }
Invoke-MuxTool -Tool 'gcloud' -Arguments @('compute', 'instances', '" + verb + @"', $name, '--zone', $zone) -InstallHint $gcloudHint";
        }

        private static DefaultSkillCommandDef C(string name, string description, string code)
        {
            return ToolchainSkillFactory.Command(name, description, code);
        }

        #endregion
    }
}
