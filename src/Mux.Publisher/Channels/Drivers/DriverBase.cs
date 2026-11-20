namespace Mux.Publisher.Channels.Drivers
{
    using Mux.Publisher.Manifest;

    /// <summary>
    /// Convenience base for channel drivers: defaults <see cref="NeedsSelfContainedPublish"/> to true
    /// (the common case) and exposes the project name as a short accessor.
    /// </summary>
    public abstract class DriverBase : IChannelDriver
    {
        /// <inheritdoc />
        public abstract string Name { get; }

        /// <inheritdoc />
        public abstract TargetOs RequiredOs { get; }

        /// <inheritdoc />
        public virtual bool NeedsSelfContainedPublish => true;

        /// <inheritdoc />
        public abstract ChannelPlan Plan(PublisherContext context, ChannelConfig config);

        /// <summary>Returns the manifest's short project name (for example <c>mux</c>).</summary>
        /// <param name="context">The publisher context.</param>
        /// <returns>The project name.</returns>
        protected static string Project(PublisherContext context) => context.Manifest.Project.Name;

        /// <summary>Returns the manifest's display name, falling back to the project name.</summary>
        /// <param name="context">The publisher context.</param>
        /// <returns>The display name.</returns>
        protected static string Display(PublisherContext context)
        {
            string display = context.Manifest.Project.DisplayName;
            return string.IsNullOrWhiteSpace(display) ? context.Manifest.Project.Name : display;
        }

        /// <summary>The reverse-DNS identifier (for example <c>ai.latticetech.mux</c>).</summary>
        /// <param name="context">The publisher context.</param>
        /// <returns>The identifier.</returns>
        protected static string Identifier(PublisherContext context) => context.Manifest.Project.EffectiveIdentifier;

        /// <summary>
        /// The bundled tray agent's executable name for <paramref name="published"/>, or null when the artifact
        /// bundles no agent. Falls back to the name the publish step will give it, so dry-run plans match.
        /// </summary>
        /// <param name="context">The publisher context.</param>
        /// <param name="published">The published payload.</param>
        /// <returns>The agent file name, or null.</returns>
        protected static string? AgentFileName(PublisherContext context, PublishedArtifact published)
        {
            if (published.AgentBinary != null) return published.AgentBinary;
            foreach (BundledProject bundled in context.Artifact.Bundle)
            {
                if (string.Equals(bundled.Role, "agent", System.StringComparison.OrdinalIgnoreCase))
                {
                    return Mux.Publisher.Publishing.PublishService.ProjectExecutableName(bundled.Csproj, published.Rid);
                }
            }

            return null;
        }

        /// <summary>The bundled CLI's executable name, falling back to the publish step's naming in a dry run.</summary>
        /// <param name="context">The publisher context.</param>
        /// <param name="published">The published payload.</param>
        /// <returns>The CLI file name, or null.</returns>
        protected static string? CliFileName(PublisherContext context, PublishedArtifact published)
        {
            if (published.CliBinary != null) return published.CliBinary;
            foreach (BundledProject bundled in context.Artifact.Bundle)
            {
                if (string.Equals(bundled.Role, "cli", System.StringComparison.OrdinalIgnoreCase))
                {
                    return Mux.Publisher.Publishing.PublishService.ProjectExecutableName(bundled.Csproj, published.Rid);
                }
            }

            return null;
        }
    }
}
