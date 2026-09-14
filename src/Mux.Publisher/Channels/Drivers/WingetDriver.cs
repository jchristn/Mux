namespace Mux.Publisher.Channels.Drivers
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using Mux.Publisher.Manifest;

    /// <summary>
    /// Renders the three winget manifest files (version, installer, default-locale) pointing at the
    /// signed Windows installer and its SHA-256, then submits them. Publishing to winget ends at an
    /// external gate: the PR into <c>microsoft/winget-pkgs</c> must be reviewed and merged, so this
    /// channel reports a pending state rather than claiming the package is live.
    /// </summary>
    public sealed class WingetDriver : DriverBase
    {
        /// <inheritdoc />
        public override string Name => "winget";

        /// <inheritdoc />
        public override TargetOs RequiredOs => TargetOs.Windows;

        /// <inheritdoc />
        public override bool NeedsSelfContainedPublish => false;

        /// <inheritdoc />
        public override ChannelPlan Plan(PublisherContext context, ChannelConfig config)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (config == null) throw new ArgumentNullException(nameof(config));

            ChannelPlan plan = new ChannelPlan(Name) { EndsAtExternalGate = true };
            string project = Project(context);
            string packageId = config.OptionOrDefault("packageId", "jchristn." + project);

            // winget references the same signed installer the inno channel produced (win-x64).
            string installerName = Naming.InnoInstaller(project, context.Version, "win-x64");
            string installerUrl = context.ReleaseAssetUrl(installerName);
            string sha = context.AssetChecksum(installerName);

            string basePath = "winget/manifests/" + char.ToLowerInvariant(packageId[0]) + "/" + packageId.Replace('.', '/') + "/" + context.Version + "/";

            plan.AddFile(basePath + packageId + ".yaml", RenderVersion(packageId, context.Version));
            plan.AddFile(basePath + packageId + ".installer.yaml", RenderInstaller(packageId, context.Version, installerUrl, sha));
            plan.AddFile(basePath + packageId + ".locale.en-US.yaml", RenderLocale(packageId, context.Version, Display(context),
                context.Manifest.Project.Description, context.Manifest.Project.Homepage, context.Manifest.Project.License, "Joel Christner"));

            string token = config.OptionOrDefault("vaultRef", "WINGET_TOKEN");
            plan.AddCommand(new ShellCommand("wingetcreate", new List<string>
            {
                "submit",
                "--token", "$" + token,
                System.IO.Path.Combine(context.StagingRoot, basePath)
            })
            {
                Description = "Submit the winget manifests as a PR",
                ContinueOnError = true
            });

            plan.AddNote("winget: submitted a PR to microsoft/winget-pkgs — PENDING review/merge. Not live until merged.");
            return plan;
        }

        /// <summary>Renders the winget version manifest.</summary>
        /// <param name="packageId">The winget package identifier.</param>
        /// <param name="version">The release version.</param>
        /// <returns>The rendered YAML.</returns>
        public static string RenderVersion(string packageId, string version)
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("PackageIdentifier: " + packageId);
            builder.AppendLine("PackageVersion: " + version);
            builder.AppendLine("DefaultLocale: en-US");
            builder.AppendLine("ManifestType: version");
            builder.AppendLine("ManifestVersion: 1.6.0");
            return builder.ToString();
        }

        /// <summary>Renders the winget installer manifest.</summary>
        /// <param name="packageId">The winget package identifier.</param>
        /// <param name="version">The release version.</param>
        /// <param name="url">The installer download URL.</param>
        /// <param name="sha256">The installer SHA-256 (uppercase per winget convention).</param>
        /// <returns>The rendered YAML.</returns>
        public static string RenderInstaller(string packageId, string version, string url, string sha256)
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("PackageIdentifier: " + packageId);
            builder.AppendLine("PackageVersion: " + version);
            builder.AppendLine("InstallerType: inno");
            builder.AppendLine("Installers:");
            builder.AppendLine("  - Architecture: x64");
            builder.AppendLine("    InstallerUrl: " + url);
            builder.AppendLine("    InstallerSha256: " + (sha256 ?? string.Empty).ToUpperInvariant());
            builder.AppendLine("ManifestType: installer");
            builder.AppendLine("ManifestVersion: 1.6.0");
            return builder.ToString();
        }

        /// <summary>Renders the winget default-locale manifest.</summary>
        /// <param name="packageId">The winget package identifier.</param>
        /// <param name="version">The release version.</param>
        /// <param name="name">The display name.</param>
        /// <param name="description">The short description.</param>
        /// <param name="homepage">The homepage URL.</param>
        /// <param name="license">The SPDX license id.</param>
        /// <param name="publisher">The publisher name.</param>
        /// <returns>The rendered YAML.</returns>
        public static string RenderLocale(string packageId, string version, string name, string description, string homepage, string license, string publisher)
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("PackageIdentifier: " + packageId);
            builder.AppendLine("PackageVersion: " + version);
            builder.AppendLine("PackageLocale: en-US");
            builder.AppendLine("Publisher: " + publisher);
            builder.AppendLine("PackageName: " + name);
            builder.AppendLine("License: " + license);
            builder.AppendLine("ShortDescription: " + description);
            builder.AppendLine("PackageUrl: " + homepage);
            builder.AppendLine("ManifestType: defaultLocale");
            builder.AppendLine("ManifestVersion: 1.6.0");
            return builder.ToString();
        }
    }
}
