namespace Mux.Publisher.Channels.Drivers
{
    using System;
    using System.Text;
    using Mux.Publisher.Manifest;

    /// <summary>
    /// Renders a Scoop manifest for the self-contained Windows CLI archive and stages it into the
    /// configured bucket repository. Users install via <c>scoop install &lt;name&gt;</c>. The manifest
    /// references the archive's SHA-256 from the published artifact, never a hand-copied hash.
    /// </summary>
    public sealed class ScoopDriver : DriverBase
    {
        /// <inheritdoc />
        public override string Name => "scoop";

        /// <inheritdoc />
        public override TargetOs RequiredOs => TargetOs.Any;

        /// <inheritdoc />
        public override ChannelPlan Plan(PublisherContext context, ChannelConfig config)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (config == null) throw new ArgumentNullException(nameof(config));

            ChannelPlan plan = new ChannelPlan(Name);
            string project = Project(context);
            string bucket = config.OptionOrDefault("bucket");

            // Scoop is a Windows-only package manager; use the single win-x64 output.
            PublishedArtifact? win = context.PublishedFor("win-x64");
            if (win == null)
            {
                plan.AddNote("scoop: no win-x64 publish available; skipped.");
                return plan;
            }

            string archive = Naming.Archive(project, context.Version, win.Rid);
            string url = context.ReleaseAssetUrl(archive);
            string binName = project + ".exe";

            string manifest = RenderManifest(project, context.Version, context.Manifest.Project.Homepage,
                context.Manifest.Project.Description, context.Manifest.Project.License, url, win.ArchiveSha256, binName);
            plan.AddFile("scoop/" + project + ".json", manifest);

            if (!string.IsNullOrWhiteSpace(bucket))
            {
                plan.AddNote("scoop: commit scoop/" + project + ".json to the '" + bucket + "' bucket repo (git push). Then: scoop install " + project);
            }
            else
            {
                plan.AddNote("scoop: set channels.scoop.options.bucket to auto-stage the manifest.");
            }

            return plan;
        }

        /// <summary>
        /// Renders the Scoop manifest JSON.
        /// </summary>
        /// <param name="name">Package name.</param>
        /// <param name="version">Release version.</param>
        /// <param name="homepage">Homepage URL.</param>
        /// <param name="description">One-line description.</param>
        /// <param name="license">SPDX license id.</param>
        /// <param name="url">Archive download URL.</param>
        /// <param name="sha256">Archive SHA-256.</param>
        /// <param name="bin">The executable inside the archive.</param>
        /// <returns>The rendered manifest.</returns>
        public static string RenderManifest(string name, string version, string homepage, string description, string license, string url, string sha256, string bin)
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("{");
            builder.AppendLine("    \"version\": \"" + Json(version) + "\",");
            builder.AppendLine("    \"description\": \"" + Json(description) + "\",");
            builder.AppendLine("    \"homepage\": \"" + Json(homepage) + "\",");
            builder.AppendLine("    \"license\": \"" + Json(license) + "\",");
            builder.AppendLine("    \"architecture\": {");
            builder.AppendLine("        \"64bit\": {");
            builder.AppendLine("            \"url\": \"" + Json(url) + "\",");
            builder.AppendLine("            \"hash\": \"" + Json(sha256) + "\"");
            builder.AppendLine("        }");
            builder.AppendLine("    },");
            builder.AppendLine("    \"bin\": \"" + Json(bin) + "\"");
            builder.AppendLine("}");
            return builder.ToString();
        }

        private static string Json(string value)
        {
            return (value ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"");
        }
    }
}
