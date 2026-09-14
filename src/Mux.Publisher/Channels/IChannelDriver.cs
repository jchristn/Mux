namespace Mux.Publisher.Channels
{
    using System;
    using System.Collections.Generic;
    using Mux.Publisher.Manifest;

    /// <summary>
    /// A distribution-channel driver. Each driver holds the testable packaging logic for one channel:
    /// given a <see cref="PublisherContext"/> and its <see cref="ChannelConfig"/>, it produces a
    /// <see cref="ChannelPlan"/> of files to write and commands to run. The driver never executes
    /// anything itself — the orchestrator runs the plan (or prints it under <c>--dry-run</c>).
    /// </summary>
    public interface IChannelDriver
    {
        /// <summary>The channel name this driver handles (matches the manifest key).</summary>
        string Name { get; }

        /// <summary>
        /// The operating system this channel's packaging and signing must run on. The orchestrator uses
        /// this to fail fast with a clear message when a channel is invoked on the wrong OS.
        /// </summary>
        TargetOs RequiredOs { get; }

        /// <summary>
        /// Whether the orchestrator must produce self-contained, single-file publishes (and archives)
        /// of the channel's artifact before planning. True for every channel that packages a native
        /// binary; false only for NuGet, which packs a framework-dependent .NET tool instead.
        /// </summary>
        bool NeedsSelfContainedPublish { get; }

        /// <summary>
        /// Plans the channel's work: rendered recipe files plus packaging/signing/publish commands.
        /// </summary>
        /// <param name="context">The publisher context.</param>
        /// <param name="config">The channel configuration from the manifest.</param>
        /// <returns>The channel plan.</returns>
        ChannelPlan Plan(PublisherContext context, ChannelConfig config);
    }

    /// <summary>The operating system a channel's native packaging must run on.</summary>
    public enum TargetOs
    {
        /// <summary>Runs on any OS (.NET cross-compiles; no native packager needed).</summary>
        Any,

        /// <summary>Requires Windows (signtool, Inno Setup, WiX).</summary>
        Windows,

        /// <summary>Requires macOS (codesign, notarytool, hdiutil).</summary>
        MacOs,

        /// <summary>Requires Linux (fpm, appimagetool, createrepo, dpkg).</summary>
        Linux
    }

    /// <summary>
    /// Shared helpers for driver implementations: RID classification, naming, and small text utilities.
    /// </summary>
    public static class ChannelHelpers
    {
        /// <summary>
        /// Determines which OS a runtime identifier targets.
        /// </summary>
        /// <param name="rid">The runtime identifier (win-x64, osx-arm64, linux-x64, ...).</param>
        /// <returns>The OS family of the RID.</returns>
        public static TargetOs OsForRid(string rid)
        {
            if (string.IsNullOrEmpty(rid)) return TargetOs.Any;
            if (rid.StartsWith("win", StringComparison.OrdinalIgnoreCase)) return TargetOs.Windows;
            if (rid.StartsWith("osx", StringComparison.OrdinalIgnoreCase)) return TargetOs.MacOs;
            if (rid.StartsWith("linux", StringComparison.OrdinalIgnoreCase)) return TargetOs.Linux;
            return TargetOs.Any;
        }

        /// <summary>
        /// Maps a RID to the Debian/AppImage architecture token (amd64, arm64).
        /// </summary>
        /// <param name="rid">The runtime identifier.</param>
        /// <returns>The dpkg-style architecture.</returns>
        public static string DebArch(string rid)
        {
            if (rid.EndsWith("arm64", StringComparison.OrdinalIgnoreCase)) return "arm64";
            if (rid.EndsWith("x64", StringComparison.OrdinalIgnoreCase)) return "amd64";
            return "amd64";
        }

        /// <summary>
        /// Maps a RID to the RPM architecture token (x86_64, aarch64).
        /// </summary>
        /// <param name="rid">The runtime identifier.</param>
        /// <returns>The rpm-style architecture.</returns>
        public static string RpmArch(string rid)
        {
            if (rid.EndsWith("arm64", StringComparison.OrdinalIgnoreCase)) return "aarch64";
            if (rid.EndsWith("x64", StringComparison.OrdinalIgnoreCase)) return "x86_64";
            return "x86_64";
        }

        /// <summary>
        /// Resolves the applicable runtimes for a channel: its own subset when set, otherwise every
        /// build runtime, further narrowed to those matching the channel's required OS.
        /// </summary>
        /// <param name="manifest">The manifest (for the build runtime matrix).</param>
        /// <param name="config">The channel configuration.</param>
        /// <param name="requiredOs">The channel's required OS (<see cref="TargetOs.Any"/> keeps all).</param>
        /// <returns>The resolved, filtered runtime list.</returns>
        public static List<string> ResolveRuntimes(PublisherManifest manifest, ChannelConfig config, TargetOs requiredOs)
        {
            List<string> source = (config.Runtimes != null && config.Runtimes.Count > 0)
                ? config.Runtimes
                : manifest.Build.Runtimes;

            List<string> result = new List<string>();
            foreach (string rid in source)
            {
                if (requiredOs == TargetOs.Any || OsForRid(rid) == requiredOs)
                {
                    result.Add(rid);
                }
            }

            return result;
        }
    }
}
