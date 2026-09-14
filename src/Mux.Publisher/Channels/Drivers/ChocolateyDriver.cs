namespace Mux.Publisher.Channels.Drivers
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using Mux.Publisher.Manifest;

    /// <summary>
    /// Renders a Chocolatey package (<c>.nuspec</c> plus <c>chocolateyInstall.ps1</c>/<c>chocolateyUninstall.ps1</c>)
    /// that downloads the signed Inno installer and runs it silently. Publishing ends at an external gate:
    /// a new package clears the Chocolatey community moderation queue, so this reports a pending state.
    /// </summary>
    public sealed class ChocolateyDriver : DriverBase
    {
        /// <inheritdoc />
        public override string Name => "chocolatey";

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

            string installerName = Naming.InnoInstaller(project, context.Version, "win-x64");
            string installerUrl = context.ReleaseAssetUrl(installerName);
            string sha = context.AssetChecksum(installerName);

            plan.AddFile("choco/" + project + ".nuspec", RenderNuspec(project, context.Version, Display(context),
                context.Manifest.Project.Description, context.Manifest.Project.Homepage, context.Manifest.Project.License, "Joel Christner"));
            plan.AddFile("choco/tools/chocolateyInstall.ps1", RenderInstallScript(Display(context), installerUrl, sha));
            plan.AddFile("choco/tools/chocolateyUninstall.ps1", RenderUninstallScript(Display(context)));

            plan.AddCommand(new ShellCommand("choco", new List<string> { "pack", "choco/" + project + ".nuspec" })
            {
                Description = "Pack the Chocolatey package",
                WorkingDirectory = context.StagingRoot
            });

            string key = config.OptionOrDefault("vaultRef", "CHOCO_API_KEY");
            plan.AddCommand(new ShellCommand("choco", new List<string>
            {
                "push", project + "." + context.Version + ".nupkg",
                "--source", "https://push.chocolatey.org/",
                "--api-key", "$" + key
            })
            {
                Description = "Push to the Chocolatey community feed",
                WorkingDirectory = context.StagingRoot,
                ContinueOnError = true
            });

            plan.AddNote("chocolatey: pushed — PENDING community moderation. Not installable via 'choco install' until approved.");
            return plan;
        }

        /// <summary>Renders the Chocolatey <c>.nuspec</c>.</summary>
        /// <param name="id">Package id.</param>
        /// <param name="version">Release version.</param>
        /// <param name="title">Package title.</param>
        /// <param name="description">Description.</param>
        /// <param name="homepage">Project URL.</param>
        /// <param name="license">SPDX license id.</param>
        /// <param name="authors">Package authors.</param>
        /// <returns>The rendered nuspec.</returns>
        public static string RenderNuspec(string id, string version, string title, string description, string homepage, string license, string authors)
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
            builder.AppendLine("<package xmlns=\"http://schemas.microsoft.com/packaging/2015/06/nuspec.xsd\">");
            builder.AppendLine("  <metadata>");
            builder.AppendLine("    <id>" + Xml(id) + "</id>");
            builder.AppendLine("    <version>" + Xml(version) + "</version>");
            builder.AppendLine("    <title>" + Xml(title) + "</title>");
            builder.AppendLine("    <authors>" + Xml(authors) + "</authors>");
            builder.AppendLine("    <projectUrl>" + Xml(homepage) + "</projectUrl>");
            builder.AppendLine("    <licenseUrl>" + Xml(homepage) + "/blob/main/LICENSE.md</licenseUrl>");
            builder.AppendLine("    <requireLicenseAcceptance>false</requireLicenseAcceptance>");
            builder.AppendLine("    <description>" + Xml(description) + "</description>");
            builder.AppendLine("    <tags>" + Xml(id) + " ai agent cli</tags>");
            builder.AppendLine("  </metadata>");
            builder.AppendLine("  <files>");
            builder.AppendLine("    <file src=\"tools\\**\" target=\"tools\" />");
            builder.AppendLine("  </files>");
            builder.AppendLine("</package>");
            return builder.ToString();
        }

        /// <summary>Renders <c>chocolateyInstall.ps1</c>: download the installer, verify its hash, run silently.</summary>
        /// <param name="softwareName">The display software name (for the uninstall registry match).</param>
        /// <param name="url">Installer download URL.</param>
        /// <param name="sha256">Installer SHA-256.</param>
        /// <returns>The rendered PowerShell.</returns>
        public static string RenderInstallScript(string softwareName, string url, string sha256)
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("$ErrorActionPreference = 'Stop'");
            builder.AppendLine("$packageArgs = @{");
            builder.AppendLine("  packageName   = '" + Ps(softwareName) + "'");
            builder.AppendLine("  fileType      = 'exe'");
            builder.AppendLine("  url           = '" + Ps(url) + "'");
            builder.AppendLine("  checksum      = '" + Ps(sha256) + "'");
            builder.AppendLine("  checksumType  = 'sha256'");
            builder.AppendLine("  silentArgs    = '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP-'");
            builder.AppendLine("  validExitCodes= @(0)");
            builder.AppendLine("}");
            builder.AppendLine("Install-ChocolateyPackage @packageArgs");
            return builder.ToString();
        }

        /// <summary>Renders <c>chocolateyUninstall.ps1</c>.</summary>
        /// <param name="softwareName">The display software name to match in Programs and Features.</param>
        /// <returns>The rendered PowerShell.</returns>
        public static string RenderUninstallScript(string softwareName)
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("$ErrorActionPreference = 'Stop'");
            builder.AppendLine("$app = Get-ItemProperty 'HKLM:\\Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\*',");
            builder.AppendLine("        'HKLM:\\Software\\WOW6432Node\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\*' -ErrorAction SilentlyContinue |");
            builder.AppendLine("       Where-Object { $_.DisplayName -like '" + Ps(softwareName) + "*' } | Select-Object -First 1");
            builder.AppendLine("if ($app -and $app.UninstallString) {");
            builder.AppendLine("  $uninstaller = $app.UninstallString -replace '\"',''");
            builder.AppendLine("  Start-Process -FilePath $uninstaller -ArgumentList '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART' -Wait");
            builder.AppendLine("}");
            return builder.ToString();
        }

        private static string Ps(string value)
        {
            return (value ?? string.Empty).Replace("'", "''");
        }

        private static string Xml(string value)
        {
            return (value ?? string.Empty)
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;");
        }
    }
}
