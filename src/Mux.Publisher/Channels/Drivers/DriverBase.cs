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
    }
}
