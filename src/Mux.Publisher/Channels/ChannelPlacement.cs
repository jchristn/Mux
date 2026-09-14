namespace Mux.Publisher.Channels
{
    using System;
    using System.Collections.Generic;
    using Mux.Publisher.Manifest;

    /// <summary>
    /// Decides which CI matrix job runs each channel and in what order, so the workflow can ask the
    /// manifest ("which channels run on macOS?") instead of hard-coding a list that drifts. Placement is
    /// the channel's explicit <c>os</c> when set, else its driver's required OS; OS-agnostic channels must
    /// set <c>os</c>. Ordering guarantees a channel that references another's output (winget→inno,
    /// apt→debrpm, homebrew-cask→dmg) runs after it within the same job.
    /// </summary>
    public static class ChannelPlacement
    {
        /// <summary>
        /// Resolves the matrix job OS for a channel: <c>windows</c>, <c>macos</c>, or <c>linux</c>.
        /// </summary>
        /// <param name="channelName">The channel name.</param>
        /// <param name="config">The channel configuration.</param>
        /// <returns>The lowercase OS token.</returns>
        public static string EffectiveOs(string channelName, ChannelConfig config)
        {
            if (config != null && !string.IsNullOrWhiteSpace(config.Os)) return Normalize(config.Os!);

            string driverName = (config != null && !string.IsNullOrWhiteSpace(config.Driver)) ? config.Driver! : channelName;
            IChannelDriver driver = DriverRegistry.Resolve(driverName);
            switch (driver.RequiredOs)
            {
                case TargetOs.Windows: return "windows";
                case TargetOs.MacOs: return "macos";
                case TargetOs.Linux: return "linux";
                default:
                    throw new InvalidOperationException(
                        "Channel '" + channelName + "' is OS-agnostic; set an explicit \"os\" in publisher.json so CI can place it.");
            }
        }

        /// <summary>
        /// Returns the intra-job stage for a channel. Stage 0 channels build base artifacts (installers,
        /// packages, archives); stage 1 channels consume them (store manifests, repos). Lower runs first.
        /// </summary>
        /// <param name="channelName">The channel name.</param>
        /// <param name="config">The channel configuration.</param>
        /// <returns>The stage index.</returns>
        public static int Stage(string channelName, ChannelConfig config)
        {
            string driverName = (config != null && !string.IsNullOrWhiteSpace(config.Driver)) ? config.Driver! : channelName;
            switch (driverName.ToLowerInvariant())
            {
                case "winget":
                case "chocolatey":
                case "apt":
                case "yum":
                    return 1;
                case "homebrew":
                    // A cask consumes the dmg (stage 1); a formula builds its own archive (stage 0).
                    return string.Equals(config?.Artifact, "desktop", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
                default:
                    return 0;
            }
        }

        /// <summary>
        /// Lists the enabled channels assigned to an OS job, ordered by stage then name.
        /// </summary>
        /// <param name="manifest">The manifest.</param>
        /// <param name="os">The OS job (<c>windows</c>/<c>macos</c>/<c>linux</c>).</param>
        /// <returns>The ordered channel names.</returns>
        public static List<string> ChannelsFor(PublisherManifest manifest, string os)
        {
            if (manifest == null) throw new ArgumentNullException(nameof(manifest));
            string target = Normalize(os);

            List<KeyValuePair<string, ChannelConfig>> matching = new List<KeyValuePair<string, ChannelConfig>>();
            foreach (KeyValuePair<string, ChannelConfig> pair in manifest.Channels)
            {
                if (!pair.Value.Enabled) continue;
                if (!string.Equals(EffectiveOs(pair.Key, pair.Value), target, StringComparison.OrdinalIgnoreCase)) continue;
                matching.Add(pair);
            }

            matching.Sort((a, b) =>
            {
                int byStage = Stage(a.Key, a.Value).CompareTo(Stage(b.Key, b.Value));
                return byStage != 0 ? byStage : string.CompareOrdinal(a.Key, b.Key);
            });

            List<string> result = new List<string>();
            foreach (KeyValuePair<string, ChannelConfig> pair in matching) result.Add(pair.Key);
            return result;
        }

        private static string Normalize(string os)
        {
            string trimmed = (os ?? string.Empty).Trim().ToLowerInvariant();
            switch (trimmed)
            {
                case "windows":
                case "win":
                    return "windows";
                case "macos":
                case "osx":
                case "mac":
                    return "macos";
                case "linux":
                case "ubuntu":
                    return "linux";
                default:
                    return trimmed;
            }
        }
    }
}
