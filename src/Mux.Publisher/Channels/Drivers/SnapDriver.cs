namespace Mux.Publisher.Channels.Drivers
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using Mux.Publisher.Manifest;

    /// <summary>
    /// Renders a <c>snapcraft.yaml</c> that wraps the self-contained Linux publish and builds/pushes the
    /// snap. Store publication ends at an external gate (Snap Store review for classic/strict changes),
    /// so this reports a pending state after <c>snapcraft push</c> rather than claiming availability.
    /// </summary>
    public sealed class SnapDriver : DriverBase
    {
        /// <inheritdoc />
        public override string Name => "snap";

        /// <inheritdoc />
        public override TargetOs RequiredOs => TargetOs.Linux;

        /// <inheritdoc />
        public override ChannelPlan Plan(PublisherContext context, ChannelConfig config)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (config == null) throw new ArgumentNullException(nameof(config));

            ChannelPlan plan = new ChannelPlan(Name) { EndsAtExternalGate = true };
            string project = Project(context);

            PublishedArtifact? published = context.PublishedFor("linux-x64");
            string binaryName = published != null ? System.IO.Path.GetFileName(published.PrimaryBinary) : project;
            string grade = config.OptionOrDefault("grade", "stable");
            string confinement = config.OptionOrDefault("confinement", "classic");

            plan.AddFile("snap/snapcraft.yaml", RenderSnapcraft(project, context.Version, context.Manifest.Project.Description,
                context.Manifest.Project.Homepage, context.Manifest.Project.License, binaryName, grade, confinement));

            plan.AddCommand(new ShellCommand("snapcraft", new List<string>()) { Description = "Build the snap", WorkingDirectory = System.IO.Path.Combine(context.StagingRoot, "snap") });
            plan.AddCommand(new ShellCommand("snapcraft", new List<string> { "upload", "--release=stable", project + "_" + context.Version + "_amd64.snap" })
            {
                Description = "Upload the snap to the store",
                WorkingDirectory = System.IO.Path.Combine(context.StagingRoot, "snap"),
                ContinueOnError = true
            });

            plan.AddNote("snap: uploaded — store review may be PENDING for classic confinement. Users install via: snap install " + project);
            return plan;
        }

        /// <summary>Renders a <c>snapcraft.yaml</c>.</summary>
        /// <param name="name">Snap name.</param>
        /// <param name="version">Release version.</param>
        /// <param name="description">Description.</param>
        /// <param name="homepage">Homepage URL (used as source-code/website).</param>
        /// <param name="license">SPDX license id.</param>
        /// <param name="binaryName">The published launcher file name.</param>
        /// <param name="grade">Snap grade (stable/devel).</param>
        /// <param name="confinement">Snap confinement (strict/classic).</param>
        /// <returns>The rendered YAML.</returns>
        public static string RenderSnapcraft(string name, string version, string description, string homepage, string license, string binaryName, string grade, string confinement)
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("name: " + name);
            builder.AppendLine("version: '" + version + "'");
            builder.AppendLine("summary: " + Truncate(description, 78));
            builder.AppendLine("description: |");
            builder.AppendLine("  " + description);
            builder.AppendLine("website: " + homepage);
            builder.AppendLine("license: " + license);
            builder.AppendLine("grade: " + grade);
            builder.AppendLine("confinement: " + confinement);
            builder.AppendLine("base: core22");
            builder.AppendLine();
            builder.AppendLine("apps:");
            builder.AppendLine("  " + name + ":");
            builder.AppendLine("    command: bin/" + binaryName);
            builder.AppendLine();
            builder.AppendLine("parts:");
            builder.AppendLine("  " + name + ":");
            builder.AppendLine("    plugin: dump");
            builder.AppendLine("    source: ./payload");
            builder.AppendLine("    organize:");
            builder.AppendLine("      '*': bin/");
            return builder.ToString();
        }

        private static string Truncate(string value, int max)
        {
            value = value ?? string.Empty;
            return value.Length <= max ? value : value.Substring(0, max);
        }
    }
}
