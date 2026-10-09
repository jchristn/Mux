namespace Mux.Core.Skills
{
    using System.Collections.Generic;

    /// <summary>
    /// The Kubernetes, Minikube, and Helm default skills. Every cluster command prints the active context first.
    /// Changes preview first (diff, server dry run, helm diff), and applying to a context that matches the
    /// production pattern needs <c>--confirm &lt;context&gt;</c>. Nothing deletes resources or switches contexts.
    /// </summary>
    public static class DefaultKubernetesSkills
    {
        #region Private-Members

        private const string Setup = @"$kubectlHint = 'Install kubectl from https://kubernetes.io/docs/tasks/tools/.'
$kubeContext = Get-MuxKubeContext
";

        private const string HelmSetup = Setup + @"$helmHint = 'Install Helm from https://helm.sh/docs/intro/install/.'
";

        private static readonly string[] _AppliesTo = { "Chart.yaml", "**/Chart.yaml", "kustomization.yaml", "**/kustomization.yaml", "skaffold.yaml", "helmfile.yaml", "k8s/**", "kubernetes/**", "manifests/**", "deploy/**/*.yaml" };

        #endregion

        #region Public-Methods

        /// <summary>Returns the Kubernetes skill definitions.</summary>
        /// <returns>The definitions.</returns>
        public static IReadOnlyList<DefaultSkillDef> All()
        {
            string[] tags = { "kubernetes", "k8s" };
            ToolchainSkillFactory kube = new ToolchainSkillFactory(Setup, tags, _AppliesTo, null, ToolchainSkillFactory.GuardedExitNote);
            ToolchainSkillFactory helm = new ToolchainSkillFactory(HelmSetup, new[] { "kubernetes", "helm" }, _AppliesTo, null, ToolchainSkillFactory.GuardedExitNote);
            ToolchainSkillFactory mini = new ToolchainSkillFactory(@"$minikubeHint = 'Install minikube from https://minikube.sigs.k8s.io/docs/start/.'
", new[] { "kubernetes", "minikube" }, _AppliesTo, new[] { "minikube" }, ToolchainSkillFactory.GuardedExitNote);

            return new List<DefaultSkillDef>
            {
                kube.Skill("k8s-context", "Show the Kubernetes context", "Shows the active context, cluster, and namespace, lists contexts, or lists namespaces.", false,
                    "Before any other Kubernetes work, to confirm which cluster commands will reach.",
                    string.Empty,
                    "Run `current` first in any Kubernetes task. Switching contexts is left to the user on purpose; if the context is wrong, say so and stop.",
                    ToolchainSkillFactory.Command("current", "Show the active context and namespace.", @"Invoke-MuxTool -Tool 'kubectl' -Arguments @('config', 'view', '--minify', '-o', 'jsonpath={.contexts[0].context.cluster} namespace={.contexts[0].context.namespace}') -InstallHint $kubectlHint"),
                    ToolchainSkillFactory.Command("list", "List every configured context.", @"Invoke-MuxTool -Tool 'kubectl' -Arguments @('config', 'get-contexts') -InstallHint $kubectlHint"),
                    ToolchainSkillFactory.Command("namespaces", "List namespaces in the current cluster.", @"Invoke-MuxTool -Tool 'kubectl' -Arguments @('get', 'namespaces') -InstallHint $kubectlHint")),

                kube.Skill("k8s-inspect", "Inspect Kubernetes resources", "Gets and describes resources, shows pod logs, warning events, resource use, and rollout status.", false,
                    "Debugging a workload: pods not starting, crash loops, failed rollouts, or resource pressure.",
                    "<resource> [name]",
                    "`get <resource> [name]` and `describe <resource> <name>` work in the current namespace. `logs <pod> [container] [lines]` shows the last lines (default 200). `events` shows recent warnings, newest last. `top` shows pod CPU and memory (needs metrics-server). `rollout-status <deployment>` waits up to 120 seconds.",
                    ToolchainSkillFactory.Command("get", "Get resources of a kind.", @"$kind = Get-MuxArg -Arguments $args -Index 0 -Default 'pods'
$name = Get-MuxArg -Arguments $args -Index 1
$kubeArgs = @('get', $kind)
if ($name) { $kubeArgs += $name }
Invoke-MuxTool -Tool 'kubectl' -Arguments ($kubeArgs + @('-o', 'wide')) -InstallHint $kubectlHint"),
                    ToolchainSkillFactory.Command("describe", "Describe one resource.", @"$kind = Get-MuxArg -Arguments $args -Index 0
$name = Get-MuxArg -Arguments $args -Index 1
if (-not $kind -or -not $name) { Exit-MuxNotApplicable 'pass a kind and name: k8s-inspect describe <resource> <name>' }
Invoke-MuxTool -Tool 'kubectl' -Arguments @('describe', $kind, $name) -InstallHint $kubectlHint"),
                    ToolchainSkillFactory.Command("logs", "Show a pod's recent logs.", @"$pod = Get-MuxArg -Arguments $args -Index 0
if (-not $pod) { Exit-MuxNotApplicable 'pass a pod: k8s-inspect logs <pod> [container] [lines]' }
$container = Get-MuxArg -Arguments $args -Index 1
$lines = Get-MuxLineLimit -Value (Get-MuxArg -Arguments $args -Index 2)
$kubeArgs = @('logs', $pod, '--tail', [string]$lines)
if ($container) { $kubeArgs += @('-c', $container) }
Invoke-MuxTool -Tool 'kubectl' -Arguments $kubeArgs -InstallHint $kubectlHint"),
                    ToolchainSkillFactory.Command("events", "Show recent warning events.", @"Invoke-MuxTool -Tool 'kubectl' -Arguments @('get', 'events', '--field-selector', 'type=Warning', '--sort-by=.lastTimestamp') -InstallHint $kubectlHint"),
                    ToolchainSkillFactory.Command("top", "Show pod CPU and memory use.", @"Invoke-MuxTool -Tool 'kubectl' -Arguments @('top', 'pods') -InstallHint $kubectlHint"),
                    ToolchainSkillFactory.Command("rollout-status", "Wait for a deployment rollout to finish.", @"$deployment = Get-MuxArg -Arguments $args -Index 0
if (-not $deployment) { Exit-MuxNotApplicable 'pass a deployment: k8s-inspect rollout-status <deployment>' }
Invoke-MuxTool -Tool 'kubectl' -Arguments @('rollout', 'status', ('deployment/' + $deployment), '--timeout=120s') -InstallHint $kubectlHint")),

                kube.Skill("k8s-validate", "Validate Kubernetes manifests", "Validates manifests with a client or server dry run, or against schemas with kubeconform.", false,
                    "Before applying manifests, or when editing YAML under k8s/, manifests/, or a kustomization.",
                    "[path]",
                    "`client [path]` and `server [path]` run `kubectl apply --dry-run`; the server form asks the cluster's admission chain and changes nothing. `schema [path]` uses kubeconform when installed. A directory with kustomization.yaml is built with -k. The default path is the current directory.",
                    ToolchainSkillFactory.Command("client", "Validate with a client-side dry run.", @"$path = Get-MuxArg -Arguments $args -Index 0 -Default '.'
Invoke-MuxTool -Tool 'kubectl' -Arguments (@('apply', '--dry-run=client') + (Get-MuxKubeApplyArguments $path)) -InstallHint $kubectlHint"),
                    ToolchainSkillFactory.Command("server", "Validate with a server-side dry run.", @"$path = Get-MuxArg -Arguments $args -Index 0 -Default '.'
Invoke-MuxTool -Tool 'kubectl' -Arguments (@('apply', '--dry-run=server') + (Get-MuxKubeApplyArguments $path)) -InstallHint $kubectlHint"),
                    ToolchainSkillFactory.Command("schema", "Validate against Kubernetes schemas with kubeconform.", @"$path = Get-MuxArg -Arguments $args -Index 0 -Default '.'
Invoke-MuxTool -Tool 'kubeconform' -Arguments @('-strict', '-summary', '-ignore-missing-schemas', $path) -InstallHint 'Install kubeconform from https://github.com/yannh/kubeconform.'")),

                kube.Skill("k8s-apply", "Apply Kubernetes changes", "Shows a diff against the cluster, applies manifests, or restarts a deployment, behind the production guard.", true,
                    "The user asks to deploy or update Kubernetes resources.",
                    "<path> [--confirm <context>]",
                    "Always run `diff <path>` first and show the user what will change. `apply <path>` applies (-k for a kustomization directory, -f otherwise); `rollout-restart <deployment>` restarts pods. Both refuse a production context unless the arguments end with `--confirm <context>`, which you may add only after the user explicitly approves.",
                    ToolchainSkillFactory.Command("diff", "Show what applying would change.", @"$path = Get-MuxArg -Arguments $args -Index 0 -Default '.'
Invoke-MuxTool -Tool 'kubectl' -Arguments (@('diff') + (Get-MuxKubeApplyArguments $path)) -InstallHint $kubectlHint -AllowFailure
if ($script:MuxLastExit -eq 1) { Write-Output 'mux: differences found (shown above).'; exit 0 }
exit $script:MuxLastExit"),
                    ToolchainSkillFactory.Command("apply", "Apply manifests to the cluster.", @"$split = Split-MuxConfirm -Arguments $args
Assert-MuxNotProduction -Target $kubeContext -Confirm $split.Confirm
$path = Get-MuxArg -Arguments $split.Rest -Index 0 -Default '.'
Invoke-MuxTool -Tool 'kubectl' -Arguments (@('apply') + (Get-MuxKubeApplyArguments $path)) -InstallHint $kubectlHint"),
                    ToolchainSkillFactory.Command("rollout-restart", "Restart a deployment's pods.", @"$split = Split-MuxConfirm -Arguments $args
Assert-MuxNotProduction -Target $kubeContext -Confirm $split.Confirm
$deployment = Get-MuxArg -Arguments $split.Rest -Index 0
if (-not $deployment) { Exit-MuxNotApplicable 'pass a deployment: k8s-apply rollout-restart <deployment>' }
Invoke-MuxTool -Tool 'kubectl' -Arguments @('rollout', 'restart', ('deployment/' + $deployment)) -InstallHint $kubectlHint")),

                mini.Skill("minikube", "Run a local Minikube cluster", "Shows status, starts or stops the cluster, loads local images, gets service URLs, and lists addons.", true,
                    "Developing against a local Kubernetes cluster.",
                    "[driver] [kubernetes-version]",
                    "`start [driver] [version]` creates or resumes the cluster. `image-load <image>` loads a locally built image without a registry. `service-url <service>` prints a reachable URL. `addons` only lists; enabling one is the user's call.",
                    ToolchainSkillFactory.Command("status", "Show the cluster status.", @"Invoke-MuxTool -Tool 'minikube' -Arguments @('status') -InstallHint $minikubeHint -AllowFailure
exit 0"),
                    ToolchainSkillFactory.Command("start", "Start or resume the cluster.", @"$miniArgs = @('start')
$driver = Get-MuxArg -Arguments $args -Index 0
$version = Get-MuxArg -Arguments $args -Index 1
if ($driver) { $miniArgs += ('--driver=' + $driver) }
if ($version) { $miniArgs += ('--kubernetes-version=' + $version) }
Invoke-MuxTool -Tool 'minikube' -Arguments $miniArgs -InstallHint $minikubeHint"),
                    ToolchainSkillFactory.Command("stop", "Stop the cluster, keeping its state.", @"Invoke-MuxTool -Tool 'minikube' -Arguments @('stop') -InstallHint $minikubeHint"),
                    ToolchainSkillFactory.Command("image-load", "Load a local image into the cluster.", @"$image = Get-MuxArg -Arguments $args -Index 0
if (-not $image) { Exit-MuxNotApplicable 'pass an image: minikube image-load <image>' }
Invoke-MuxTool -Tool 'minikube' -Arguments @('image', 'load', $image) -InstallHint $minikubeHint"),
                    ToolchainSkillFactory.Command("service-url", "Print a service's URL.", @"$service = Get-MuxArg -Arguments $args -Index 0
if (-not $service) { Exit-MuxNotApplicable 'pass a service: minikube service-url <service>' }
Invoke-MuxTool -Tool 'minikube' -Arguments @('service', $service, '--url') -InstallHint $minikubeHint"),
                    ToolchainSkillFactory.Command("addons", "List addons and whether they are enabled.", @"Invoke-MuxTool -Tool 'minikube' -Arguments @('addons', 'list') -InstallHint $minikubeHint")),

                helm.Skill("helm", "Work with Helm charts", "Lints, renders, diffs, and upgrades Helm releases, and lists releases and history.", true,
                    "The project has a Helm chart, or the user asks to install or upgrade a release.",
                    "<release> [chart] [values-file...] [--confirm <context>]",
                    "The chart defaults to the nearest directory with Chart.yaml. Use `lint` and `template <release>` while editing, `deps` to fetch chart dependencies, and always `diff <release>` (needs the helm-diff plugin) before `upgrade <release> [chart] [values...]`, which runs `--install --atomic --wait` with a 5 minute timeout. Upgrading on a production context needs `--confirm <context>`, added only after the user approves.",
                    ToolchainSkillFactory.Command("lint", "Lint the chart.", @"$chart = Get-MuxHelmChart -Chart (Get-MuxArg -Arguments $args -Index 0)
Invoke-MuxTool -Tool 'helm' -Arguments @('lint', $chart) -InstallHint $helmHint"),
                    ToolchainSkillFactory.Command("template", "Render the chart's manifests.", @"$release = Get-MuxArg -Arguments $args -Index 0 -Default 'preview'
$chart = Get-MuxHelmChart -Chart (Get-MuxArg -Arguments $args -Index 1)
$values = @($args | Select-Object -Skip 2 | ForEach-Object { @('-f', $_) })
Invoke-MuxTool -Tool 'helm' -Arguments (@('template', $release, $chart) + $values) -InstallHint $helmHint"),
                    ToolchainSkillFactory.Command("deps", "Fetch the chart's dependencies.", @"$chart = Get-MuxHelmChart -Chart (Get-MuxArg -Arguments $args -Index 0)
Invoke-MuxTool -Tool 'helm' -Arguments @('dependency', 'update', $chart) -InstallHint $helmHint"),
                    ToolchainSkillFactory.Command("diff", "Show what an upgrade would change.", @"$release = Get-MuxArg -Arguments $args -Index 0
if (-not $release) { Exit-MuxNotApplicable 'pass a release: helm diff <release> [chart] [values...]' }
$chart = Get-MuxHelmChart -Chart (Get-MuxArg -Arguments $args -Index 1)
$values = @($args | Select-Object -Skip 2 | ForEach-Object { @('-f', $_) })
if (-not (Test-MuxDryRun) -and -not ((& helm plugin list 2>$null) -match '^diff\s')) { Exit-MuxNotApplicable 'the helm-diff plugin is not installed: helm plugin install https://github.com/databus23/helm-diff' }
Invoke-MuxTool -Tool 'helm' -Arguments (@('diff', 'upgrade', $release, $chart, '--allow-unreleased') + $values) -InstallHint $helmHint"),
                    ToolchainSkillFactory.Command("upgrade", "Install or upgrade a release.", @"$split = Split-MuxConfirm -Arguments $args
Assert-MuxNotProduction -Target $kubeContext -Confirm $split.Confirm
$release = Get-MuxArg -Arguments $split.Rest -Index 0
if (-not $release) { Exit-MuxNotApplicable 'pass a release: helm upgrade <release> [chart] [values...]' }
$chart = Get-MuxHelmChart -Chart (Get-MuxArg -Arguments $split.Rest -Index 1)
$values = @($split.Rest | Select-Object -Skip 2 | ForEach-Object { @('-f', $_) })
Invoke-MuxTool -Tool 'helm' -Arguments (@('upgrade', $release, $chart, '--install', '--atomic', '--wait', '--timeout', '5m') + $values) -InstallHint $helmHint"),
                    ToolchainSkillFactory.Command("list", "List releases in the current namespace.", @"Invoke-MuxTool -Tool 'helm' -Arguments @('list') -InstallHint $helmHint"),
                    ToolchainSkillFactory.Command("history", "Show a release's revision history.", @"$release = Get-MuxArg -Arguments $args -Index 0
if (-not $release) { Exit-MuxNotApplicable 'pass a release: helm history <release>' }
Invoke-MuxTool -Tool 'helm' -Arguments @('history', $release) -InstallHint $helmHint"))
            };
        }

        #endregion
    }
}
