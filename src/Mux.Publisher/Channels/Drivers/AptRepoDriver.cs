namespace Mux.Publisher.Channels.Drivers
{
    using System;
    using System.Collections.Generic;
    using Mux.Publisher.Manifest;

    /// <summary>
    /// Assembles a self-hosted apt repository from the built <c>.deb</c> files and GPG-signs the
    /// repository <c>Release</c> metadata (the standard signs repo metadata, not individual packages).
    /// After the repo is published to its host and the public key is trusted, users install via
    /// <c>apt-get install &lt;name&gt;</c>. This channel does not require a self-contained publish itself —
    /// it consumes the debs produced by the <c>debrpm</c> channel.
    /// </summary>
    public sealed class AptRepoDriver : DriverBase
    {
        /// <inheritdoc />
        public override string Name => "apt";

        /// <inheritdoc />
        public override TargetOs RequiredOs => TargetOs.Linux;

        /// <inheritdoc />
        public override bool NeedsSelfContainedPublish => false;

        /// <inheritdoc />
        public override ChannelPlan Plan(PublisherContext context, ChannelConfig config)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (config == null) throw new ArgumentNullException(nameof(config));

            ChannelPlan plan = new ChannelPlan(Name);
            string project = Project(context);
            string suite = config.OptionOrDefault("suite", "stable");
            string component = config.OptionOrDefault("component", "main");
            string debDir = System.IO.Path.Combine(context.OutputRoot, "linux");
            string repoDir = System.IO.Path.Combine(context.OutputRoot, "apt");
            string poolDir = repoDir + "/pool/" + component;
            string distDir = repoDir + "/dists/" + suite + "/" + component + "/binary-amd64";

            SigningEntry? linux = context.Manifest.Signing.Linux;
            bool canSign = linux != null && !string.IsNullOrWhiteSpace(linux.VaultRef);

            plan.AddCommand(new ShellCommand("mkdir", new List<string> { "-p", poolDir, distDir }) { Description = "Create apt repo layout" });
            plan.AddCommand(new ShellCommand("sh", new List<string> { "-c", "cp " + Quote(debDir) + "/*.deb " + Quote(poolDir) + "/" }) { Description = "Copy .deb files into the pool", ContinueOnError = true });

            plan.AddCommand(new ShellCommand("sh", new List<string>
            {
                "-c",
                "cd " + Quote(repoDir) + " && dpkg-scanpackages --arch amd64 pool/ > " + Quote(distDir + "/Packages") + " && gzip -kf " + Quote(distDir + "/Packages")
            })
            { Description = "Generate Packages index" });

            plan.AddCommand(new ShellCommand("sh", new List<string>
            {
                "-c",
                "cd " + Quote(repoDir + "/dists/" + suite) + " && apt-ftparchive release . > Release"
            })
            { Description = "Generate the Release file" });

            if (canSign)
            {
                plan.AddCommand(new ShellCommand("sh", new List<string>
                {
                    "-c",
                    "cd " + Quote(repoDir + "/dists/" + suite) + " && gpg --default-key \"$" + linux!.VaultRef + "_KEYID\" -abs -o Release.gpg Release && gpg --default-key \"$" + linux.VaultRef + "_KEYID\" --clearsign -o InRelease Release"
                })
                { Description = "GPG-sign the apt Release metadata" });
            }
            else
            {
                plan.AddNote("apt: no GPG identity configured; Release metadata will be UNSIGNED (apt clients will refuse it).");
            }

            plan.AddNote("apt: publish " + repoDir + " to your repo host and publish the public key. Then users add the sources list and run: apt-get install " + project);
            return plan;
        }

        private static string Quote(string value)
        {
            return "'" + (value ?? string.Empty).Replace("'", "'\\''") + "'";
        }
    }
}
