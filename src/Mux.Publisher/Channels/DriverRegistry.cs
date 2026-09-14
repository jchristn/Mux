namespace Mux.Publisher.Channels
{
    using System;
    using System.Collections.Generic;
    using Mux.Publisher.Channels.Drivers;

    /// <summary>
    /// Maps channel names to their drivers. Every channel a manifest can enable resolves here; an
    /// unknown channel is a hard error so a typo in <c>publisher.json</c> fails loudly rather than
    /// silently skipping a delivery surface.
    /// </summary>
    public static class DriverRegistry
    {
        private static readonly Dictionary<string, Func<IChannelDriver>> Factories =
            new Dictionary<string, Func<IChannelDriver>>(StringComparer.OrdinalIgnoreCase)
            {
                ["nuget"] = () => new NuGetDriver(),
                ["scoop"] = () => new ScoopDriver(),
                ["homebrew"] = () => new HomebrewDriver(),
                ["inno"] = () => new InnoSetupDriver(),
                ["wix"] = () => new WixDriver(),
                ["winget"] = () => new WingetDriver(),
                ["chocolatey"] = () => new ChocolateyDriver(),
                ["dmg"] = () => new DmgDriver(),
                ["debrpm"] = () => new DebRpmDriver(),
                ["appimage"] = () => new AppImageDriver(),
                ["apt"] = () => new AptRepoDriver(),
                ["yum"] = () => new YumRepoDriver(),
                ["snap"] = () => new SnapDriver(),
                ["flatpak"] = () => new FlatpakDriver()
            };

        /// <summary>The set of known channel names.</summary>
        public static IReadOnlyCollection<string> KnownChannels => Factories.Keys;

        /// <summary>
        /// Determines whether a channel name is registered.
        /// </summary>
        /// <param name="channel">The channel name.</param>
        /// <returns>True when a driver exists for the channel.</returns>
        public static bool IsKnown(string channel) => channel != null && Factories.ContainsKey(channel);

        /// <summary>
        /// Resolves a driver for a channel name.
        /// </summary>
        /// <param name="channel">The channel name.</param>
        /// <returns>A new driver instance.</returns>
        public static IChannelDriver Resolve(string channel)
        {
            if (channel == null) throw new ArgumentNullException(nameof(channel));
            if (!Factories.TryGetValue(channel, out Func<IChannelDriver>? factory))
            {
                throw new KeyNotFoundException("Unknown channel '" + channel + "'. Known channels: " + string.Join(", ", Factories.Keys));
            }

            return factory();
        }
    }
}
