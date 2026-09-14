namespace Mux.Publisher.Channels.Drivers
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using Mux.Publisher.Manifest;

    /// <summary>
    /// Renders a Flatpak manifest that packages the self-contained Linux publish and builds/exports it
    /// to a repo (or bundle). Publishing to Flathub ends at an external gate (Flathub PR review), so this
    /// reports a pending state after building rather than claiming the app is live on Flathub.
    /// </summary>
    public sealed class FlatpakDriver : DriverBase
    {
        /// <inheritdoc />
        public override string Name => "flatpak";

        /// <inheritdoc />
        public override TargetOs RequiredOs => TargetOs.Linux;

        /// <inheritdoc />
        public override ChannelPlan Plan(PublisherContext context, ChannelConfig config)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (config == null) throw new ArgumentNullException(nameof(config));

            ChannelPlan plan = new ChannelPlan(Name) { EndsAtExternalGate = true };
            string project = Project(context);
            string appId = config.OptionOrDefault("appId", "com.jchristn." + project);

            PublishedArtifact? published = context.PublishedFor("linux-x64");
            string binaryName = published != null ? System.IO.Path.GetFileName(published.PrimaryBinary) : project;

            plan.AddFile("flatpak/" + appId + ".yaml", RenderManifest(appId, binaryName));

            string buildDir = System.IO.Path.Combine(context.StagingRoot, "flatpak", "build");
            string repoDir = System.IO.Path.Combine(context.OutputRoot, "flatpak", "repo");
            plan.AddCommand(new ShellCommand("flatpak-builder", new List<string>
            {
                "--force-clean", "--repo=" + repoDir, buildDir,
                System.IO.Path.Combine(context.StagingRoot, "flatpak", appId + ".yaml")
            })
            { Description = "Build and export the Flatpak", ContinueOnError = true });

            plan.AddNote("flatpak: built to a local repo — Flathub publication is a PR that must be reviewed. Users install via: flatpak install " + appId);
            return plan;
        }

        /// <summary>Renders a minimal Flatpak YAML manifest for a prebuilt binary.</summary>
        /// <param name="appId">The Flatpak application id (reverse-DNS).</param>
        /// <param name="binaryName">The published launcher file name.</param>
        /// <returns>The rendered YAML manifest.</returns>
        public static string RenderManifest(string appId, string binaryName)
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("app-id: " + appId);
            builder.AppendLine("runtime: org.freedesktop.Platform");
            builder.AppendLine("runtime-version: '23.08'");
            builder.AppendLine("sdk: org.freedesktop.Sdk");
            builder.AppendLine("command: " + binaryName);
            builder.AppendLine("finish-args:");
            builder.AppendLine("  - --share=network");
            builder.AppendLine("  - --socket=fallback-x11");
            builder.AppendLine("  - --socket=wayland");
            builder.AppendLine("  - --filesystem=home");
            builder.AppendLine("modules:");
            builder.AppendLine("  - name: " + binaryName);
            builder.AppendLine("    buildsystem: simple");
            builder.AppendLine("    build-commands:");
            builder.AppendLine("      - install -Dm755 -t /app/bin/ payload/*");
            builder.AppendLine("    sources:");
            builder.AppendLine("      - type: dir");
            builder.AppendLine("        path: ./payload");
            return builder.ToString();
        }
    }
}
