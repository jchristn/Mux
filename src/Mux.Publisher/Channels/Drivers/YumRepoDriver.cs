namespace Mux.Publisher.Channels.Drivers
{
    using System;
    using System.Collections.Generic;
    using Mux.Publisher.Manifest;

    /// <summary>
    /// Assembles a self-hosted yum/dnf repository from the built <c>.rpm</c> files with
    /// <c>createrepo_c</c> and GPG-signs the repository metadata (<c>repomd.xml</c>). After publishing
    /// the repo and trusting the public key, users install via <c>dnf install &lt;name&gt;</c> /
    /// <c>yum install &lt;name&gt;</c>. Consumes the rpms from the <c>debrpm</c> channel.
    /// </summary>
    public sealed class YumRepoDriver : DriverBase
    {
        /// <inheritdoc />
        public override string Name => "yum";

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
            string rpmDir = System.IO.Path.Combine(context.OutputRoot, "linux");
            string repoDir = System.IO.Path.Combine(context.OutputRoot, "yum");

            SigningEntry? linux = context.Manifest.Signing.Linux;
            bool canSign = linux != null && !string.IsNullOrWhiteSpace(linux.VaultRef);

            plan.AddCommand(new ShellCommand("mkdir", new List<string> { "-p", repoDir }) { Description = "Create yum repo directory" });
            plan.AddCommand(new ShellCommand("sh", new List<string> { "-c", "cp " + Quote(rpmDir) + "/*.rpm " + Quote(repoDir) + "/" }) { Description = "Copy .rpm files into the repo", ContinueOnError = true });

            if (canSign)
            {
                plan.AddCommand(new ShellCommand("sh", new List<string>
                {
                    "-c",
                    "for f in " + Quote(repoDir) + "/*.rpm; do rpm --define \"_gpg_name $" + linux!.VaultRef + "_KEYID\" --addsign \"$f\"; done"
                })
                { Description = "GPG-sign each RPM", ContinueOnError = true });
            }

            plan.AddCommand(new ShellCommand("createrepo_c", new List<string> { repoDir }) { Description = "Generate yum repo metadata" });

            if (canSign)
            {
                plan.AddCommand(new ShellCommand("sh", new List<string>
                {
                    "-c",
                    "gpg --default-key \"$" + linux!.VaultRef + "_KEYID\" --detach-sign --armor " + Quote(repoDir + "/repodata/repomd.xml")
                })
                { Description = "GPG-sign repomd.xml" });
            }
            else
            {
                plan.AddNote("yum: no GPG identity configured; repo metadata will be UNSIGNED.");
            }

            plan.AddNote("yum: publish " + repoDir + " to your repo host and publish the public key. Then users add the .repo file and run: dnf install " + project);
            return plan;
        }

        private static string Quote(string value)
        {
            return "'" + (value ?? string.Empty).Replace("'", "'\\''") + "'";
        }
    }
}
