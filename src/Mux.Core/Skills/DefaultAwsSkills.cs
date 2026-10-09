namespace Mux.Core.Skills
{
    using System.Collections.Generic;

    /// <summary>
    /// The AWS default skills, built on the AWS CLI v2 and grouped by job (compute, containers, storage, data,
    /// deploy, observability, integration). Every command confirms the caller identity first and prints the
    /// profile. Changes preview first where AWS supports it and sit behind the production guard (matched against
    /// the profile name). Secret and parameter commands list names only, and nothing deletes or terminates.
    /// </summary>
    public static class DefaultAwsSkills
    {
        #region Private-Members

        private const string Setup = @"$awsHint = 'Install the AWS CLI v2 from https://aws.amazon.com/cli/.'
$awsProfile = Get-MuxTarget -Label 'AWS profile' -Resolve { $name = if ($env:AWS_PROFILE) { $env:AWS_PROFILE } else { 'default' }; $null = aws sts get-caller-identity --output text 2>$null; if ($LASTEXITCODE -eq 0) { $name } } -LoginHint 'Sign in with aws sso login --profile <name> or aws configure, then retry. Mux does not sign in for you.'
";

        #endregion

        #region Public-Methods

        /// <summary>Returns the AWS skill definitions.</summary>
        /// <returns>The definitions.</returns>
        public static IReadOnlyList<DefaultSkillDef> All()
        {
            ToolchainSkillFactory f = new ToolchainSkillFactory(Setup, new[] { "aws", "cloud" }, null, new[] { "aws" }, ToolchainSkillFactory.GuardedExitNote);
            return new List<DefaultSkillDef>
            {
                f.Skill("aws-whoami", "Show the AWS identity", "Shows the caller identity, configured profiles, and available regions.", false,
                    "Before any AWS work, or when an AWS command fails with an authentication or permission error.",
                    string.Empty,
                    "`identity` shows the account, ARN, and user or role; `profiles` lists configured profiles; `regions` lists enabled regions. If identity fails, ask the user to sign in (aws sso login) rather than guessing credentials.",
                    C("identity", "Show the caller identity.", @"Invoke-MuxTool -Tool 'aws' -Arguments @('sts', 'get-caller-identity', '--output', 'table') -InstallHint $awsHint"),
                    C("profiles", "List configured profiles.", @"Invoke-MuxTool -Tool 'aws' -Arguments @('configure', 'list-profiles') -InstallHint $awsHint"),
                    C("regions", "List enabled regions.", @"Invoke-MuxTool -Tool 'aws' -Arguments @('ec2', 'describe-regions', '--query', 'Regions[].RegionName', '--output', 'text') -InstallHint $awsHint")),

                f.Skill("aws-compute", "Work with EC2 and Lambda", "Lists and describes EC2 instances, starts or stops them, and lists, invokes, or tails Lambda functions.", true,
                    "The user asks about EC2 instances or Lambda functions.",
                    "<instance-id | function> [--confirm <profile>]",
                    "`ec2-list` and `ec2-describe <id>` read. `ec2-start <id>` and `ec2-stop <id>` change state; stopping is reversible, and terminating is not offered. `lambda-list`, `lambda-logs <function> [since]` (default 15m) read; `lambda-invoke <function> [json-payload]` runs the function. Changes on a production profile need `--confirm <profile>` after the user approves.",
                    C("ec2-list", "List EC2 instances.", @"Invoke-MuxTool -Tool 'aws' -Arguments @('ec2', 'describe-instances', '--query', 'Reservations[].Instances[].[InstanceId,State.Name,InstanceType,Placement.AvailabilityZone,Tags[?Key==`Name`]|[0].Value]', '--output', 'table') -InstallHint $awsHint"),
                    C("ec2-describe", "Describe one instance.", @"$id = Get-MuxArg -Arguments $args -Index 0
if (-not $id) { Exit-MuxNotApplicable 'pass an instance id: aws-compute ec2-describe <id>' }
Invoke-MuxTool -Tool 'aws' -Arguments @('ec2', 'describe-instances', '--instance-ids', $id, '--output', 'json') -InstallHint $awsHint"),
                    C("ec2-start", "Start an instance.", @"$split = Split-MuxConfirm -Arguments $args
Assert-MuxNotProduction -Target $awsProfile -Confirm $split.Confirm
$id = Get-MuxArg -Arguments $split.Rest -Index 0
if (-not $id) { Exit-MuxNotApplicable 'pass an instance id: aws-compute ec2-start <id>' }
Invoke-MuxTool -Tool 'aws' -Arguments @('ec2', 'start-instances', '--instance-ids', $id, '--output', 'table') -InstallHint $awsHint"),
                    C("ec2-stop", "Stop an instance.", @"$split = Split-MuxConfirm -Arguments $args
Assert-MuxNotProduction -Target $awsProfile -Confirm $split.Confirm
$id = Get-MuxArg -Arguments $split.Rest -Index 0
if (-not $id) { Exit-MuxNotApplicable 'pass an instance id: aws-compute ec2-stop <id>' }
Invoke-MuxTool -Tool 'aws' -Arguments @('ec2', 'stop-instances', '--instance-ids', $id, '--output', 'table') -InstallHint $awsHint"),
                    C("lambda-list", "List Lambda functions.", @"Invoke-MuxTool -Tool 'aws' -Arguments @('lambda', 'list-functions', '--query', 'Functions[].[FunctionName,Runtime,LastModified]', '--output', 'table') -InstallHint $awsHint"),
                    C("lambda-invoke", "Invoke a Lambda function.", @"$split = Split-MuxConfirm -Arguments $args
Assert-MuxNotProduction -Target $awsProfile -Confirm $split.Confirm
$function = Get-MuxArg -Arguments $split.Rest -Index 0
if (-not $function) { Exit-MuxNotApplicable 'pass a function: aws-compute lambda-invoke <function> [json-payload]' }
$payload = Get-MuxArg -Arguments $split.Rest -Index 1 -Default '{}'
$out = Join-Path ([IO.Path]::GetTempPath()) ('mux-lambda-' + [Guid]::NewGuid().ToString('N') + '.json')
Invoke-MuxTool -Tool 'aws' -Arguments @('lambda', 'invoke', '--function-name', $function, '--payload', $payload, '--cli-binary-format', 'raw-in-base64-out', $out) -InstallHint $awsHint
if (-not (Test-MuxDryRun) -and (Test-Path -LiteralPath $out)) { Write-Output 'Response:'; Get-Content -LiteralPath $out -Raw; Remove-Item -LiteralPath $out -Force }"),
                    C("lambda-logs", "Show a function's recent logs.", @"$function = Get-MuxArg -Arguments $args -Index 0
if (-not $function) { Exit-MuxNotApplicable 'pass a function: aws-compute lambda-logs <function> [since]' }
$since = Get-MuxArg -Arguments $args -Index 1 -Default '15m'
Invoke-MuxTool -Tool 'aws' -Arguments @('logs', 'tail', ('/aws/lambda/' + $function), '--since', $since, '--format', 'short') -InstallHint $awsHint")),

                f.Skill("aws-containers", "Work with ECS, EKS, and ECR", "Lists ECS services and tasks, redeploys a service, lists EKS clusters and writes kubeconfig, and works with ECR repositories.", true,
                    "The user deploys or debugs containers on ECS or EKS, or pushes images to ECR.",
                    "<cluster> [service] [--confirm <profile>]",
                    "`ecs-services <cluster>` and `ecs-tasks <cluster> [service]` read; `ecs-redeploy <cluster> <service>` forces a new deployment and waits for it to stabilize. `eks-clusters` lists clusters and `eks-kubeconfig <cluster>` adds a context named after the cluster, after which the k8s-* skills apply. `ecr-repos`, `ecr-login [registry]`, and `ecr-push <image>` handle images. Redeploys on a production profile need `--confirm <profile>`.",
                    C("ecs-services", "List services in a cluster.", @"$cluster = Get-MuxArg -Arguments $args -Index 0
if (-not $cluster) { Exit-MuxNotApplicable 'pass a cluster: aws-containers ecs-services <cluster>' }
Invoke-MuxTool -Tool 'aws' -Arguments @('ecs', 'list-services', '--cluster', $cluster, '--output', 'table') -InstallHint $awsHint"),
                    C("ecs-tasks", "List tasks in a cluster or service.", @"$cluster = Get-MuxArg -Arguments $args -Index 0
if (-not $cluster) { Exit-MuxNotApplicable 'pass a cluster: aws-containers ecs-tasks <cluster> [service]' }
$service = Get-MuxArg -Arguments $args -Index 1
$ecsArgs = @('ecs', 'list-tasks', '--cluster', $cluster, '--output', 'table')
if ($service) { $ecsArgs += @('--service-name', $service) }
Invoke-MuxTool -Tool 'aws' -Arguments $ecsArgs -InstallHint $awsHint"),
                    C("ecs-redeploy", "Force a new deployment and wait for it.", @"$split = Split-MuxConfirm -Arguments $args
Assert-MuxNotProduction -Target $awsProfile -Confirm $split.Confirm
$cluster = Get-MuxArg -Arguments $split.Rest -Index 0
$service = Get-MuxArg -Arguments $split.Rest -Index 1
if (-not $cluster -or -not $service) { Exit-MuxNotApplicable 'pass a cluster and service: aws-containers ecs-redeploy <cluster> <service>' }
Invoke-MuxTool -Tool 'aws' -Arguments @('ecs', 'update-service', '--cluster', $cluster, '--service', $service, '--force-new-deployment', '--query', 'service.deployments[0].[status,rolloutState]', '--output', 'text') -InstallHint $awsHint
Invoke-MuxTool -Tool 'aws' -Arguments @('ecs', 'wait', 'services-stable', '--cluster', $cluster, '--services', $service) -InstallHint $awsHint"),
                    C("eks-clusters", "List EKS clusters.", @"Invoke-MuxTool -Tool 'aws' -Arguments @('eks', 'list-clusters', '--output', 'table') -InstallHint $awsHint"),
                    C("eks-kubeconfig", "Add a kubeconfig context for a cluster.", @"$cluster = Get-MuxArg -Arguments $args -Index 0
if (-not $cluster) { Exit-MuxNotApplicable 'pass a cluster: aws-containers eks-kubeconfig <cluster>' }
Invoke-MuxTool -Tool 'aws' -Arguments @('eks', 'update-kubeconfig', '--name', $cluster, '--alias', $cluster) -InstallHint $awsHint"),
                    C("ecr-repos", "List ECR repositories.", @"Invoke-MuxTool -Tool 'aws' -Arguments @('ecr', 'describe-repositories', '--query', 'repositories[].[repositoryName,repositoryUri]', '--output', 'table') -InstallHint $awsHint"),
                    C("ecr-login", "Log Docker in to the account's ECR registry.", @"$registry = Get-MuxArg -Arguments $args -Index 0
if (Test-MuxDryRun) { Write-Output ('DRYRUN: aws ecr get-login-password | docker login --username AWS --password-stdin ' + $(if ($registry) { $registry } else { '<account>.dkr.ecr.<region>.amazonaws.com' })); exit 0 }
if (-not $registry) {
    $account = (& aws sts get-caller-identity --query Account --output text).Trim()
    $region = (& aws configure get region).Trim()
    if (-not $region) { Exit-MuxNotApplicable 'no default region; pass the registry host: aws-containers ecr-login <account>.dkr.ecr.<region>.amazonaws.com' }
    $registry = $account + '.dkr.ecr.' + $region + '.amazonaws.com'
}
if (-not (Test-MuxTool 'docker')) { Exit-MuxNotApplicable 'docker was not found on PATH.' }
Write-Output ('> aws ecr get-login-password | docker login --username AWS --password-stdin ' + $registry)
& aws ecr get-login-password | & docker login --username AWS --password-stdin $registry
exit $LASTEXITCODE"),
                    C("ecr-push", "Push an image to ECR.", @"$image = Get-MuxArg -Arguments $args -Index 0
if (-not $image) { Exit-MuxNotApplicable 'pass the full image: aws-containers ecr-push <account>.dkr.ecr.<region>.amazonaws.com/<repo>:<tag>' }
Invoke-MuxTool -Tool 'docker' -Arguments @('push', $image) -InstallHint 'Install Docker.'")),

                f.Skill("aws-storage", "Work with S3", "Lists buckets and objects, syncs folders with a dry run first, and creates presigned URLs.", true,
                    "The user asks about S3 buckets or objects, or to upload or download a folder.",
                    "<s3://bucket/prefix> [destination] [apply] [--confirm <profile>]",
                    "`s3-buckets` and `s3-ls <s3://bucket/prefix>` read. `s3-sync <source> <destination>` always runs with --dryrun and shows what would copy; add `apply` as the third argument to copy for real. Sync never passes --delete, so nothing is removed at the destination. `s3-presign <s3://bucket/key> [seconds]` makes a temporary download link (default one hour).",
                    C("s3-buckets", "List buckets.", @"Invoke-MuxTool -Tool 'aws' -Arguments @('s3', 'ls') -InstallHint $awsHint"),
                    C("s3-ls", "List objects under a prefix.", @"$uri = Get-MuxArg -Arguments $args -Index 0
if (-not $uri) { Exit-MuxNotApplicable 'pass a location: aws-storage s3-ls s3://bucket/prefix' }
Invoke-MuxTool -Tool 'aws' -Arguments @('s3', 'ls', $uri, '--human-readable', '--summarize') -InstallHint $awsHint"),
                    C("s3-sync", "Sync a folder, as a dry run unless apply is passed.", @"$split = Split-MuxConfirm -Arguments $args
$source = Get-MuxArg -Arguments $split.Rest -Index 0
$destination = Get-MuxArg -Arguments $split.Rest -Index 1
if (-not $source -or -not $destination) { Exit-MuxNotApplicable 'pass a source and destination: aws-storage s3-sync <source> <destination> [apply]' }
$apply = (Get-MuxArg -Arguments $split.Rest -Index 2) -eq 'apply'
$syncArgs = @('s3', 'sync', $source, $destination)
if (-not $apply) { $syncArgs += '--dryrun'; Write-Output 'Preview only (--dryrun). Re-run with apply to copy.' }
else { Assert-MuxNotProduction -Target $awsProfile -Confirm $split.Confirm }
Invoke-MuxTool -Tool 'aws' -Arguments $syncArgs -InstallHint $awsHint"),
                    C("s3-presign", "Create a temporary download URL.", @"$uri = Get-MuxArg -Arguments $args -Index 0
if (-not $uri) { Exit-MuxNotApplicable 'pass an object: aws-storage s3-presign s3://bucket/key [seconds]' }
$seconds = Get-MuxArg -Arguments $args -Index 1 -Default '3600'
Invoke-MuxTool -Tool 'aws' -Arguments @('s3', 'presign', $uri, '--expires-in', $seconds) -InstallHint $awsHint")),

                f.Skill("aws-data", "Inspect AWS databases", "Lists RDS instances and snapshots, DynamoDB tables, and ElastiCache clusters.", false,
                    "The user asks about RDS, DynamoDB, or ElastiCache resources.",
                    "[table]",
                    "Read-only on purpose: schema and data changes belong in migrations the user runs. `dynamodb-describe <table>` shows keys, indexes, and capacity.",
                    C("rds-instances", "List RDS instances.", @"Invoke-MuxTool -Tool 'aws' -Arguments @('rds', 'describe-db-instances', '--query', 'DBInstances[].[DBInstanceIdentifier,Engine,EngineVersion,DBInstanceStatus,DBInstanceClass]', '--output', 'table') -InstallHint $awsHint"),
                    C("rds-snapshots", "List RDS snapshots.", @"Invoke-MuxTool -Tool 'aws' -Arguments @('rds', 'describe-db-snapshots', '--query', 'DBSnapshots[].[DBSnapshotIdentifier,DBInstanceIdentifier,SnapshotCreateTime,Status]', '--output', 'table') -InstallHint $awsHint"),
                    C("dynamodb-tables", "List DynamoDB tables.", @"Invoke-MuxTool -Tool 'aws' -Arguments @('dynamodb', 'list-tables', '--output', 'table') -InstallHint $awsHint"),
                    C("dynamodb-describe", "Describe a DynamoDB table.", @"$table = Get-MuxArg -Arguments $args -Index 0
if (-not $table) { Exit-MuxNotApplicable 'pass a table: aws-data dynamodb-describe <table>' }
Invoke-MuxTool -Tool 'aws' -Arguments @('dynamodb', 'describe-table', '--table-name', $table, '--output', 'json') -InstallHint $awsHint"),
                    C("elasticache-clusters", "List ElastiCache clusters.", @"Invoke-MuxTool -Tool 'aws' -Arguments @('elasticache', 'describe-cache-clusters', '--query', 'CacheClusters[].[CacheClusterId,Engine,EngineVersion,CacheClusterStatus,CacheNodeType]', '--output', 'table') -InstallHint $awsHint")),

                f.Skill("aws-deploy", "Deploy with CloudFormation, SAM, or CDK", "Lists stacks and events, previews change sets, and deploys SAM or CDK apps after a diff.", true,
                    "The project uses CloudFormation, SAM (template.yaml, samconfig.toml), or CDK (cdk.json), or the user asks to deploy one.",
                    "<stack> [template] [apply] [--confirm <profile>]",
                    "Read with `cfn-stacks` and `cfn-events <stack>`. Preview with `cfn-changeset <stack> <template>` (creates a change set without executing it), `sam-validate`, `cdk-synth`, and `cdk-diff [stack]`. `sam-deploy` and `cdk-deploy [stack]` preview by default and deploy only when `apply` is passed; deploying on a production profile also needs `--confirm <profile>`.",
                    C("cfn-stacks", "List CloudFormation stacks.", @"Invoke-MuxTool -Tool 'aws' -Arguments @('cloudformation', 'list-stacks', '--stack-status-filter', 'CREATE_COMPLETE', 'UPDATE_COMPLETE', 'UPDATE_ROLLBACK_COMPLETE', 'ROLLBACK_COMPLETE', 'CREATE_IN_PROGRESS', 'UPDATE_IN_PROGRESS', '--query', 'StackSummaries[].[StackName,StackStatus,LastUpdatedTime]', '--output', 'table') -InstallHint $awsHint"),
                    C("cfn-events", "Show a stack's recent events.", @"$stack = Get-MuxArg -Arguments $args -Index 0
if (-not $stack) { Exit-MuxNotApplicable 'pass a stack: aws-deploy cfn-events <stack>' }
Invoke-MuxTool -Tool 'aws' -Arguments @('cloudformation', 'describe-stack-events', '--stack-name', $stack, '--max-items', '30', '--query', 'StackEvents[].[Timestamp,LogicalResourceId,ResourceStatus,ResourceStatusReason]', '--output', 'table') -InstallHint $awsHint"),
                    C("cfn-changeset", "Create a change set without executing it.", @"$stack = Get-MuxArg -Arguments $args -Index 0
$template = Get-MuxArg -Arguments $args -Index 1
if (-not $stack -or -not $template) { Exit-MuxNotApplicable 'pass a stack and template: aws-deploy cfn-changeset <stack> <template>' }
Invoke-MuxTool -Tool 'aws' -Arguments @('cloudformation', 'deploy', '--stack-name', $stack, '--template-file', $template, '--no-execute-changeset', '--capabilities', 'CAPABILITY_IAM', 'CAPABILITY_NAMED_IAM') -InstallHint $awsHint"),
                    C("sam-validate", "Validate the SAM template.", @"Invoke-MuxTool -Tool 'sam' -Arguments @('validate', '--lint') -InstallHint 'Install the AWS SAM CLI.'"),
                    C("sam-deploy", "Preview, or with apply deploy, the SAM app.", @"$split = Split-MuxConfirm -Arguments $args
if ((Get-MuxArg -Arguments $split.Rest -Index 0) -eq 'apply') {
    Assert-MuxNotProduction -Target $awsProfile -Confirm $split.Confirm
    Invoke-MuxTool -Tool 'sam' -Arguments @('deploy', '--no-confirm-changeset', '--no-fail-on-empty-changeset') -InstallHint 'Install the AWS SAM CLI.'
} else {
    Write-Output 'Preview only (change set not executed). Re-run with apply to deploy.'
    Invoke-MuxTool -Tool 'sam' -Arguments @('deploy', '--no-execute-changeset', '--no-fail-on-empty-changeset') -InstallHint 'Install the AWS SAM CLI.'
}"),
                    C("cdk-synth", "Synthesize the CDK app.", @"Invoke-MuxTool -Tool 'cdk' -Arguments @('synth', '--quiet') -InstallHint 'Install the AWS CDK: npm install -g aws-cdk.'"),
                    C("cdk-diff", "Show what a CDK deploy would change.", @"$stack = Get-MuxArg -Arguments $args -Index 0
$cdkArgs = @('diff')
if ($stack) { $cdkArgs += $stack }
Invoke-MuxTool -Tool 'cdk' -Arguments $cdkArgs -InstallHint 'Install the AWS CDK: npm install -g aws-cdk.'"),
                    C("cdk-deploy", "Preview, or with apply deploy, CDK stacks.", @"$split = Split-MuxConfirm -Arguments $args
$stack = Get-MuxArg -Arguments $split.Rest -Index 0
if ($stack -eq 'apply') { $stack = ''; $apply = $true } else { $apply = (Get-MuxArg -Arguments $split.Rest -Index 1) -eq 'apply' }
if (-not $apply) {
    Write-Output 'Preview only. Re-run with apply to deploy.'
    $cdkArgs = @('diff'); if ($stack) { $cdkArgs += $stack }
    Invoke-MuxTool -Tool 'cdk' -Arguments $cdkArgs -InstallHint 'Install the AWS CDK: npm install -g aws-cdk.'
    exit 0
}
Assert-MuxNotProduction -Target $awsProfile -Confirm $split.Confirm
$cdkArgs = @('deploy', '--require-approval', 'never'); if ($stack) { $cdkArgs += $stack }
Invoke-MuxTool -Tool 'cdk' -Arguments $cdkArgs -InstallHint 'Install the AWS CDK: npm install -g aws-cdk.'")),

                f.Skill("aws-observe", "Observe AWS: logs, alarms, and cost", "Lists log groups, shows recent log events, lists alarms in ALARM state, and reports month-to-date cost by service.", false,
                    "Debugging with CloudWatch logs or alarms, or when the user asks what AWS is costing.",
                    "<log-group> [since]",
                    "`logs-tail <group> [since]` prints events from the last 15 minutes by default and stops; it never follows. `cost-month` uses Cost Explorer (it must be enabled, and each call has a small charge).",
                    C("logs-groups", "List log groups.", @"Invoke-MuxTool -Tool 'aws' -Arguments @('logs', 'describe-log-groups', '--query', 'logGroups[].[logGroupName,storedBytes]', '--output', 'table') -InstallHint $awsHint"),
                    C("logs-tail", "Show recent events from a log group.", @"$group = Get-MuxArg -Arguments $args -Index 0
if (-not $group) { Exit-MuxNotApplicable 'pass a log group: aws-observe logs-tail <group> [since]' }
$since = Get-MuxArg -Arguments $args -Index 1 -Default '15m'
Invoke-MuxTool -Tool 'aws' -Arguments @('logs', 'tail', $group, '--since', $since, '--format', 'short') -InstallHint $awsHint"),
                    C("alarms", "List alarms currently in ALARM.", @"Invoke-MuxTool -Tool 'aws' -Arguments @('cloudwatch', 'describe-alarms', '--state-value', 'ALARM', '--query', 'MetricAlarms[].[AlarmName,StateReason,StateUpdatedTimestamp]', '--output', 'table') -InstallHint $awsHint"),
                    C("cost-month", "Report month-to-date cost by service.", @"$start = (Get-Date -Day 1).ToString('yyyy-MM-dd')
$end = (Get-Date).AddDays(1).ToString('yyyy-MM-dd')
Invoke-MuxTool -Tool 'aws' -Arguments @('ce', 'get-cost-and-usage', '--time-period', ('Start=' + $start + ',End=' + $end), '--granularity', 'MONTHLY', '--metrics', 'UnblendedCost', '--group-by', 'Type=DIMENSION,Key=SERVICE', '--query', 'ResultsByTime[0].Groups[].[Keys[0],Metrics.UnblendedCost.Amount]', '--output', 'table') -InstallHint $awsHint")),

                f.Skill("aws-integration", "Inspect AWS messaging, secrets, DNS, and IAM", "Lists SQS queues and their depth, SNS topics, secret and parameter names, Route 53 zones, and the caller's IAM policies.", false,
                    "Debugging integrations (queues backing up, missing configuration) or checking what the current identity may do.",
                    "[queue-url | path]",
                    "Secrets and parameters are listed by name and last change only; their values are never read. `sqs-depth <queue-url>` shows visible and in-flight message counts. `iam-whoami-policies` lists the managed policies attached to the calling user or role.",
                    C("sqs-queues", "List SQS queues.", @"Invoke-MuxTool -Tool 'aws' -Arguments @('sqs', 'list-queues', '--output', 'table') -InstallHint $awsHint"),
                    C("sqs-depth", "Show a queue's message counts.", @"$queue = Get-MuxArg -Arguments $args -Index 0
if (-not $queue) { Exit-MuxNotApplicable 'pass a queue URL: aws-integration sqs-depth <queue-url>' }
Invoke-MuxTool -Tool 'aws' -Arguments @('sqs', 'get-queue-attributes', '--queue-url', $queue, '--attribute-names', 'ApproximateNumberOfMessages', 'ApproximateNumberOfMessagesNotVisible', '--output', 'table') -InstallHint $awsHint"),
                    C("sns-topics", "List SNS topics.", @"Invoke-MuxTool -Tool 'aws' -Arguments @('sns', 'list-topics', '--output', 'table') -InstallHint $awsHint"),
                    C("secrets-list", "List secret names (never values).", @"Invoke-MuxTool -Tool 'aws' -Arguments @('secretsmanager', 'list-secrets', '--query', 'SecretList[].[Name,LastChangedDate]', '--output', 'table') -InstallHint $awsHint"),
                    C("ssm-params", "List parameter names (never values).", @"$path = Get-MuxArg -Arguments $args -Index 0
$ssmArgs = @('ssm', 'describe-parameters', '--query', 'Parameters[].[Name,Type,LastModifiedDate]', '--output', 'table')
if ($path) { $ssmArgs += @('--parameter-filters', ('Key=Path,Option=Recursive,Values=' + $path)) }
Invoke-MuxTool -Tool 'aws' -Arguments $ssmArgs -InstallHint $awsHint"),
                    C("route53-zones", "List Route 53 hosted zones.", @"Invoke-MuxTool -Tool 'aws' -Arguments @('route53', 'list-hosted-zones', '--query', 'HostedZones[].[Name,Id,Config.PrivateZone]', '--output', 'table') -InstallHint $awsHint"),
                    C("iam-whoami-policies", "List the managed policies attached to the caller.", @"if (Test-MuxDryRun) { Write-Output 'DRYRUN: aws sts get-caller-identity, then aws iam list-attached-role-policies or list-attached-user-policies'; exit 0 }
$arn = (& aws sts get-caller-identity --query Arn --output text).Trim()
Write-Output ('Caller: ' + $arn)
if ($arn -match ':assumed-role/([^/]+)/') { Invoke-MuxTool -Tool 'aws' -Arguments @('iam', 'list-attached-role-policies', '--role-name', $Matches[1], '--output', 'table') -InstallHint $awsHint }
elseif ($arn -match ':user/(.+)$') { Invoke-MuxTool -Tool 'aws' -Arguments @('iam', 'list-attached-user-policies', '--user-name', ($Matches[1] -split '/')[-1], '--output', 'table') -InstallHint $awsHint }
else { Write-Output 'mux: the caller is neither an IAM user nor an assumed role (for example the root user); nothing to list.' }"))
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
