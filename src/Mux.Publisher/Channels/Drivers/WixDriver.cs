namespace Mux.Publisher.Channels.Drivers
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using Mux.Publisher.Manifest;

    /// <summary>
    /// Builds a Windows <c>.msi</c> with WiX v4/v5, for enterprise/MSI deployment scenarios where an
    /// Inno <c>.exe</c> is not sufficient. Uses the WiX v4 <c>Files</c> element to harvest the whole
    /// self-contained publish directory. Signed with the same <c>signtool</c> step as the Inno channel.
    /// </summary>
    public sealed class WixDriver : DriverBase
    {
        /// <inheritdoc />
        public override string Name => "wix";

        /// <inheritdoc />
        public override TargetOs RequiredOs => TargetOs.Windows;

        /// <inheritdoc />
        public override ChannelPlan Plan(PublisherContext context, ChannelConfig config)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (config == null) throw new ArgumentNullException(nameof(config));

            ChannelPlan plan = new ChannelPlan(Name);
            string project = Project(context);
            string display = Display(context);

            foreach (string rid in ChannelHelpers.ResolveRuntimes(context.Manifest, config, RequiredOs))
            {
                PublishedArtifact? published = context.PublishedFor(rid);
                if (published == null)
                {
                    plan.AddNote("wix: no publish for " + rid + "; skipped.");
                    continue;
                }

                string wxsName = "wix/" + project + "-" + rid + ".wxs";
                string appExe = System.IO.Path.GetFileName(published.PrimaryBinary);

                // Stage the license as RTF so the MSI's WixUI dialog set shows it and requires acceptance.
                string licenseName = "wix/" + project + "-" + rid + "-License.rtf";
                plan.AddFile(licenseName, Mux.Publisher.Publishing.LicenseAssets.ToRtf(
                    Mux.Publisher.Publishing.LicenseAssets.ReadLicenseText(context.RepoRoot)));

                string wxs = RenderWxs(new WixInputs
                {
                    ProductName = display,
                    Manufacturer = "Joel Christner",
                    Version = context.Version,
                    AppExe = appExe,
                    PublishDir = published.PublishDir,
                    Arch = rid.EndsWith("arm64", StringComparison.OrdinalIgnoreCase) ? "arm64" : "x64",
                    LicenseRtfPath = System.IO.Path.Combine(context.StagingRoot, licenseName),
                    // When the desktop payload bundles the CLI, add the install dir to PATH so `mux` works.
                    AddToPath = published.CliBinary != null
                });
                plan.AddFile(wxsName, wxs);

                string msi = Naming.Msi(project, context.Version, rid);
                string msiOut = System.IO.Path.Combine(context.OutputRoot, "windows", msi);
                plan.AddCommand(new ShellCommand("wix", new List<string>
                {
                    "build",
                    System.IO.Path.Combine(context.StagingRoot, wxsName),
                    "-arch", rid.EndsWith("arm64", StringComparison.OrdinalIgnoreCase) ? "arm64" : "x64",
                    // The WixUI license dialog lives in the UI extension; it must be referenced at build time.
                    "-ext", "WixToolset.UI.wixext",
                    "-o", msiOut
                })
                {
                    Description = "Build the MSI for " + rid
                });

                InnoSetupDriver.AddSigning(plan, context, config, msiOut);
            }

            return plan;
        }

        /// <summary>Inputs for rendering a WiX v4 package.</summary>
        public sealed class WixInputs
        {
            /// <summary>Product display name.</summary>
            public string ProductName { get; set; } = string.Empty;

            /// <summary>Manufacturer name.</summary>
            public string Manufacturer { get; set; } = string.Empty;

            /// <summary>Release version.</summary>
            public string Version { get; set; } = string.Empty;

            /// <summary>Main executable file name.</summary>
            public string AppExe { get; set; } = string.Empty;

            /// <summary>Publish directory to harvest.</summary>
            public string PublishDir { get; set; } = string.Empty;

            /// <summary>Package architecture (x64/arm64).</summary>
            public string Arch { get; set; } = "x64";

            /// <summary>Path to the RTF license shown by the WixUI dialog (acceptance is required to install).</summary>
            public string LicenseRtfPath { get; set; } = string.Empty;

            /// <summary>Whether to add the install directory to the system PATH (so the bundled CLI is on PATH).</summary>
            public bool AddToPath { get; set; }
        }

        /// <summary>
        /// Renders a WiX v4/v5 <c>.wxs</c> that installs the whole publish directory under Program Files.
        /// </summary>
        /// <param name="inputs">The render inputs.</param>
        /// <returns>The rendered <c>.wxs</c> content.</returns>
        public static string RenderWxs(WixInputs inputs)
        {
            if (inputs == null) throw new ArgumentNullException(nameof(inputs));

            string upgradeCode = StableGuid.FromString("wix:" + inputs.ProductName);
            StringBuilder builder = new StringBuilder();
            bool withUi = !string.IsNullOrWhiteSpace(inputs.LicenseRtfPath);
            builder.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
            builder.AppendLine("<!-- Generated by Mux.Publisher — do not edit by hand. -->");
            if (withUi)
            {
                builder.AppendLine("<Wix xmlns=\"http://wixtoolset.org/schemas/v4/wxs\" xmlns:ui=\"http://wixtoolset.org/schemas/v4/wxs/ui\">");
            }
            else
            {
                builder.AppendLine("<Wix xmlns=\"http://wixtoolset.org/schemas/v4/wxs\">");
            }

            builder.AppendLine("  <Package Name=\"" + Xml(inputs.ProductName) + "\" Manufacturer=\"" + Xml(inputs.Manufacturer) + "\"");
            builder.AppendLine("           Version=\"" + Xml(inputs.Version) + "\" UpgradeCode=\"" + upgradeCode + "\" Scope=\"perMachine\">");
            builder.AppendLine("    <MajorUpgrade DowngradeErrorMessage=\"A newer version is already installed.\" />");
            builder.AppendLine("    <MediaTemplate EmbedCab=\"yes\" />");
            if (withUi)
            {
                // WixUI_Minimal shows a combined welcome/EULA dialog; Install is disabled until the user ticks
                // "I accept the terms in the License Agreement". WixUILicenseRtf supplies the license shown.
                builder.AppendLine("    <WixVariable Id=\"WixUILicenseRtf\" Value=\"" + Xml(inputs.LicenseRtfPath) + "\" />");
                builder.AppendLine("    <ui:WixUI Id=\"WixUI_Minimal\" />");
            }

            builder.AppendLine("    <StandardDirectory Id=\"ProgramFiles64Folder\">");
            builder.AppendLine("      <Directory Id=\"INSTALLFOLDER\" Name=\"" + Xml(inputs.ProductName) + "\" />");
            builder.AppendLine("    </StandardDirectory>");
            builder.AppendLine("    <Feature Id=\"Main\">");
            builder.AppendLine("      <Files Directory=\"INSTALLFOLDER\" Include=\"" + Xml(inputs.PublishDir) + "\\**\" />");
            if (inputs.AddToPath)
            {
                // A dedicated component owns the PATH entry so the install directory (which holds the bundled
                // CLI) is added to the system PATH and removed cleanly on uninstall.
                string pathGuid = StableGuid.FromString("wixpath:" + inputs.ProductName);
                builder.AppendLine("      <Component Id=\"PathEntry\" Directory=\"INSTALLFOLDER\" Guid=\"" + pathGuid + "\">");
                builder.AppendLine("        <Environment Id=\"MuxPath\" Name=\"PATH\" Value=\"[INSTALLFOLDER]\" Part=\"last\" Action=\"set\" System=\"yes\" Permanent=\"no\" />");
                builder.AppendLine("      </Component>");
            }

            builder.AppendLine("    </Feature>");
            builder.AppendLine("  </Package>");
            builder.AppendLine("</Wix>");
            return builder.ToString();
        }

        private static string Xml(string value)
        {
            return (value ?? string.Empty)
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;")
                .Replace("\"", "&quot;");
        }
    }
}
