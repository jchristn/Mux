namespace Mux.Publisher.Channels
{
    using System;

    /// <summary>
    /// Central, deterministic asset naming so every channel and the CI workflow agree on file names
    /// (and so package-manager manifests reference the same URL the release upload produced).
    /// </summary>
    public static class Naming
    {
        /// <summary>
        /// The base stem for an artifact/runtime: <c>&lt;project&gt;-&lt;version&gt;-&lt;rid&gt;</c>.
        /// </summary>
        /// <param name="project">The project name (for example <c>mux</c>).</param>
        /// <param name="version">The release version.</param>
        /// <param name="rid">The runtime identifier.</param>
        /// <returns>The base file stem.</returns>
        public static string Stem(string project, string version, string rid)
        {
            return project + "-" + version + "-" + rid;
        }

        /// <summary>
        /// The archive file name for a self-contained publish: <c>.zip</c> on Windows, <c>.tar.gz</c>
        /// elsewhere.
        /// </summary>
        /// <param name="project">The project name.</param>
        /// <param name="version">The release version.</param>
        /// <param name="rid">The runtime identifier.</param>
        /// <returns>The archive file name.</returns>
        public static string Archive(string project, string version, string rid)
        {
            string extension = ChannelHelpers.OsForRid(rid) == TargetOs.Windows ? ".zip" : ".tar.gz";
            return Stem(project, version, rid) + extension;
        }

        /// <summary>The Windows Inno installer file name (<c>&lt;project&gt;-&lt;version&gt;-&lt;rid&gt;-setup.exe</c>).</summary>
        /// <param name="project">The project name.</param>
        /// <param name="version">The release version.</param>
        /// <param name="rid">The runtime identifier.</param>
        /// <returns>The installer file name.</returns>
        public static string InnoInstaller(string project, string version, string rid)
        {
            return Stem(project, version, rid) + "-setup.exe";
        }

        /// <summary>The Windows MSI file name.</summary>
        /// <param name="project">The project name.</param>
        /// <param name="version">The release version.</param>
        /// <param name="rid">The runtime identifier.</param>
        /// <returns>The MSI file name.</returns>
        public static string Msi(string project, string version, string rid)
        {
            return Stem(project, version, rid) + ".msi";
        }

        /// <summary>The macOS disk-image file name.</summary>
        /// <param name="project">The project name.</param>
        /// <param name="version">The release version.</param>
        /// <param name="rid">The runtime identifier.</param>
        /// <returns>The dmg file name.</returns>
        public static string Dmg(string project, string version, string rid)
        {
            return Stem(project, version, rid) + ".dmg";
        }

        /// <summary>The Debian package file name (<c>&lt;project&gt;_&lt;version&gt;_&lt;arch&gt;.deb</c>).</summary>
        /// <param name="project">The project name.</param>
        /// <param name="version">The release version.</param>
        /// <param name="rid">The runtime identifier.</param>
        /// <returns>The .deb file name.</returns>
        public static string Deb(string project, string version, string rid)
        {
            return project + "_" + version + "_" + ChannelHelpers.DebArch(rid) + ".deb";
        }

        /// <summary>The RPM package file name (<c>&lt;project&gt;-&lt;version&gt;-1.&lt;arch&gt;.rpm</c>).</summary>
        /// <param name="project">The project name.</param>
        /// <param name="version">The release version.</param>
        /// <param name="rid">The runtime identifier.</param>
        /// <returns>The .rpm file name.</returns>
        public static string Rpm(string project, string version, string rid)
        {
            return project + "-" + version + "-1." + ChannelHelpers.RpmArch(rid) + ".rpm";
        }

        /// <summary>The AppImage file name (<c>&lt;Project&gt;-&lt;version&gt;-&lt;arch&gt;.AppImage</c>).</summary>
        /// <param name="displayName">The display name used for the AppImage.</param>
        /// <param name="version">The release version.</param>
        /// <param name="rid">The runtime identifier.</param>
        /// <returns>The AppImage file name.</returns>
        public static string AppImage(string displayName, string version, string rid)
        {
            string arch = rid.EndsWith("arm64", StringComparison.OrdinalIgnoreCase) ? "aarch64" : "x86_64";
            return displayName + "-" + version + "-" + arch + ".AppImage";
        }
    }
}
