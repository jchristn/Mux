namespace Mux.Core.Cloud
{
    using System;
    using System.IO;
    using System.Reflection;
    using System.Runtime.InteropServices;
    using System.Security.Cryptography;
    using System.Text;

    /// <summary>
    /// Describes this machine to Mux Cloud: host name, operating system, mux version, and a stable fingerprint.
    /// <para>Thread safety: stateless; safe to call concurrently.</para>
    /// </summary>
    public static class CloudMachineInfo
    {
        /// <summary>Host name of this machine.</summary>
        /// <returns>Host name, or <c>unknown</c>.</returns>
        public static string Hostname()
        {
            try { return Environment.MachineName; }
            catch (InvalidOperationException) { return "unknown"; }
        }

        /// <summary>Operating system description, for example <c>macOS 26.0 Arm64</c>.</summary>
        /// <returns>Description.</returns>
        public static string Os()
        {
            string name = OperatingSystem.IsMacOS() ? "macOS" : OperatingSystem.IsWindows() ? "Windows" : OperatingSystem.IsLinux() ? "Linux" : RuntimeInformation.OSDescription;
            return name + " " + Environment.OSVersion.Version + " " + RuntimeInformation.OSArchitecture;
        }

        /// <summary>Installed mux version (the Mux.Core informational version without build metadata).</summary>
        /// <returns>Version.</returns>
        public static string MuxVersion()
        {
            string? version = typeof(CloudMachineInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (string.IsNullOrWhiteSpace(version)) return "0.0.0";
            int plus = version.IndexOf('+');
            return plus > 0 ? version.Substring(0, plus) : version;
        }

        /// <summary>
        /// Stable fingerprint: lowercase hex SHA-256 of <c>lower(hostname) + "|" + full config directory path</c>.
        /// </summary>
        /// <param name="configDirectory">The mux config directory.</param>
        /// <returns>64 hex characters.</returns>
        public static string Fingerprint(string configDirectory)
        {
            string dir = string.IsNullOrWhiteSpace(configDirectory) ? string.Empty : Path.GetFullPath(configDirectory);
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(Hostname().ToLowerInvariant() + "|" + dir));
            return Convert.ToHexString(hash).ToLowerInvariant();
        }
    }
}
