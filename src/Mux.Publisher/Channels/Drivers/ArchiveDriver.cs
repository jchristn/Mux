namespace Mux.Publisher.Channels.Drivers
{
    using System;
    using Mux.Publisher.Manifest;

    /// <summary>
    /// Publishes an artifact self-contained for each configured runtime and leaves the archives
    /// (<c>mux-1.2.1-linux-x64.tar.gz</c>) as release assets, with nothing else to install or submit. This is
    /// the pinnable download for tools that embed the CLI, for example a container image that fetches one
    /// release asset by version.
    /// </summary>
    public sealed class ArchiveDriver : DriverBase
    {
        /// <inheritdoc />
        public override string Name => "archive";

        /// <inheritdoc />
        public override TargetOs RequiredOs => TargetOs.Any;

        /// <inheritdoc />
        public override ChannelPlan Plan(PublisherContext context, ChannelConfig config)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (config == null) throw new ArgumentNullException(nameof(config));

            ChannelPlan plan = new ChannelPlan(Name);
            foreach (PublishedArtifact published in context.Published)
            {
                string archive = Naming.ArtifactArchive(Project(context), context.Artifact, context.Version, published.Rid);
                plan.AddNote("archive: " + archive + " → " + context.ReleaseAssetUrl(archive));
            }

            if (context.Published.Count == 0)
            {
                plan.AddNote("archive: no runtimes matched this channel; nothing archived.");
            }

            return plan;
        }
    }
}
