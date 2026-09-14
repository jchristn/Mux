namespace Mux.Publisher.Channels.Drivers
{
    using System;
    using System.IO;
    using Mux.Publisher.Manifest;
    using Mux.Publisher.Publishing;

    /// <summary>
    /// Publishes a .NET tool to NuGet. A tool is framework-dependent by definition (it requires the
    /// .NET runtime on the target), so this channel packs rather than doing a self-contained publish.
    /// This is the <c>dotnet tool install -g &lt;PackageId&gt;</c> path for cross-platform CLIs.
    /// </summary>
    public sealed class NuGetDriver : DriverBase
    {
        /// <inheritdoc />
        public override string Name => "nuget";

        /// <inheritdoc />
        public override TargetOs RequiredOs => TargetOs.Any;

        /// <inheritdoc />
        public override bool NeedsSelfContainedPublish => false;

        /// <inheritdoc />
        public override ChannelPlan Plan(PublisherContext context, ChannelConfig config)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (config == null) throw new ArgumentNullException(nameof(config));

            ChannelPlan plan = new ChannelPlan(Name);

            string csproj = Path.Combine(context.RepoRoot, context.Artifact.Csproj.Replace('/', Path.DirectorySeparatorChar));
            string packDir = Path.Combine(context.OutputRoot, "nuget");
            string vaultRef = config.OptionOrDefault("vaultRef", "NUGET_API_KEY");
            string source = config.OptionOrDefault("source", "https://api.nuget.org/v3/index.json");

            ShellCommand pack = new ShellCommand("dotnet", PublishService.BuildPackArgs(csproj, packDir, context.Version))
            {
                Description = "Pack the " + context.Artifact.Id + " .NET tool",
                WorkingDirectory = context.RepoRoot
            };
            plan.AddCommand(pack);

            plan.AddCommand(new ShellCommand("dotnet", new System.Collections.Generic.List<string>
            {
                "nuget", "push",
                Path.Combine(packDir, PackageId(context) + "." + context.Version + ".nupkg"),
                "--api-key", "$" + vaultRef,
                "--source", source,
                "--skip-duplicate"
            })
            {
                Description = "Push the package to NuGet",
                WorkingDirectory = context.RepoRoot
            });

            plan.AddNote("nuget: requires secret '" + vaultRef + "'. Users install via: dotnet tool install -g " + PackageId(context));
            return plan;
        }

        private static string PackageId(PublisherContext context)
        {
            // Mux.Cli's PackageId; keep in sync with the tool project.
            return "Mux.Cli";
        }
    }
}
