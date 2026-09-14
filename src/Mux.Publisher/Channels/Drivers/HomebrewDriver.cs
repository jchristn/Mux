namespace Mux.Publisher.Channels.Drivers
{
    using System;
    using System.Text;
    using Mux.Publisher.Manifest;

    /// <summary>
    /// Renders a Homebrew tap entry and stages it into the configured tap repository. A GUI artifact
    /// becomes a <b>cask</b> pointing at the notarized <c>.dmg</c> (<c>brew install --cask &lt;name&gt;</c>);
    /// a CLI/console artifact becomes a <b>formula</b> pointing at the self-contained <c>.tar.gz</c>
    /// (<c>brew install &lt;name&gt;</c>). Per-arch checksums come from the published artifacts.
    /// </summary>
    public sealed class HomebrewDriver : DriverBase
    {
        /// <inheritdoc />
        public override string Name => "homebrew";

        /// <inheritdoc />
        public override TargetOs RequiredOs => TargetOs.Any;

        /// <inheritdoc />
        public override ChannelPlan Plan(PublisherContext context, ChannelConfig config)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (config == null) throw new ArgumentNullException(nameof(config));

            ChannelPlan plan = new ChannelPlan(Name);
            string project = Project(context);
            string tap = config.OptionOrDefault("tap");

            PublishedArtifact? arm = context.PublishedFor("osx-arm64");
            PublishedArtifact? intel = context.PublishedFor("osx-x64");

            if (context.Artifact.Kind == ArtifactKind.Gui)
            {
                string armDmg = Naming.Dmg(project, context.Version, "osx-arm64");
                string intelDmg = Naming.Dmg(project, context.Version, "osx-x64");
                string cask = RenderCask(
                    project, context.Version, Display(context), context.Manifest.Project.Description, context.Manifest.Project.Homepage,
                    context.ReleaseAssetUrl(armDmg), context.AssetChecksum(armDmg),
                    context.ReleaseAssetUrl(intelDmg), context.AssetChecksum(intelDmg));
                plan.AddFile("homebrew/Casks/" + project + ".rb", cask);
                plan.AddNote("homebrew: cask → brew install --cask " + project);
            }
            else
            {
                string armTar = Naming.Archive(project, context.Version, "osx-arm64");
                string intelTar = Naming.Archive(project, context.Version, "osx-x64");
                string formula = RenderFormula(
                    project, context.Version, context.Manifest.Project.Description, context.Manifest.Project.Homepage, context.Manifest.Project.License,
                    context.ReleaseAssetUrl(armTar), arm?.ArchiveSha256 ?? string.Empty,
                    context.ReleaseAssetUrl(intelTar), intel?.ArchiveSha256 ?? string.Empty);
                plan.AddFile("homebrew/Formula/" + project + ".rb", formula);
                plan.AddNote("homebrew: formula → brew install " + project);
            }

            plan.AddNote(string.IsNullOrWhiteSpace(tap)
                ? "homebrew: set channels.homebrew.options.tap to auto-stage into the tap repo."
                : "homebrew: commit the rendered ruby to the '" + tap + "' tap repo (git push).");
            return plan;
        }

        /// <summary>Renders a Homebrew cask for a GUI app distributed as arch-specific dmgs.</summary>
        /// <param name="token">The cask token (lowercase package name).</param>
        /// <param name="version">Release version.</param>
        /// <param name="name">Display name.</param>
        /// <param name="description">Short description.</param>
        /// <param name="homepage">Homepage URL.</param>
        /// <param name="armUrl">Apple-Silicon dmg URL.</param>
        /// <param name="armSha">Apple-Silicon dmg SHA-256.</param>
        /// <param name="intelUrl">Intel dmg URL.</param>
        /// <param name="intelSha">Intel dmg SHA-256.</param>
        /// <returns>The rendered cask ruby.</returns>
        public static string RenderCask(string token, string version, string name, string description, string homepage,
            string armUrl, string armSha, string intelUrl, string intelSha)
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("cask \"" + token + "\" do");
            builder.AppendLine("  version \"" + version + "\"");
            builder.AppendLine();
            builder.AppendLine("  on_arm do");
            builder.AppendLine("    sha256 \"" + armSha + "\"");
            builder.AppendLine("    url \"" + armUrl + "\"");
            builder.AppendLine("  end");
            builder.AppendLine("  on_intel do");
            builder.AppendLine("    sha256 \"" + intelSha + "\"");
            builder.AppendLine("    url \"" + intelUrl + "\"");
            builder.AppendLine("  end");
            builder.AppendLine();
            builder.AppendLine("  name \"" + name + "\"");
            builder.AppendLine("  desc \"" + Ruby(description) + "\"");
            builder.AppendLine("  homepage \"" + homepage + "\"");
            builder.AppendLine();
            builder.AppendLine("  app \"" + name + ".app\"");
            builder.AppendLine("end");
            return builder.ToString();
        }

        /// <summary>Renders a Homebrew formula for a CLI distributed as arch-specific tarballs.</summary>
        /// <param name="name">The formula name.</param>
        /// <param name="version">Release version.</param>
        /// <param name="description">Short description.</param>
        /// <param name="homepage">Homepage URL.</param>
        /// <param name="license">SPDX license id.</param>
        /// <param name="armUrl">Apple-Silicon tarball URL.</param>
        /// <param name="armSha">Apple-Silicon tarball SHA-256.</param>
        /// <param name="intelUrl">Intel tarball URL.</param>
        /// <param name="intelSha">Intel tarball SHA-256.</param>
        /// <returns>The rendered formula ruby.</returns>
        public static string RenderFormula(string name, string version, string description, string homepage, string license,
            string armUrl, string armSha, string intelUrl, string intelSha)
        {
            string className = ToClassName(name);
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("class " + className + " < Formula");
            builder.AppendLine("  desc \"" + Ruby(description) + "\"");
            builder.AppendLine("  homepage \"" + homepage + "\"");
            builder.AppendLine("  version \"" + version + "\"");
            builder.AppendLine("  license \"" + license + "\"");
            builder.AppendLine();
            builder.AppendLine("  on_macos do");
            builder.AppendLine("    on_arm do");
            builder.AppendLine("      url \"" + armUrl + "\"");
            builder.AppendLine("      sha256 \"" + armSha + "\"");
            builder.AppendLine("    end");
            builder.AppendLine("    on_intel do");
            builder.AppendLine("      url \"" + intelUrl + "\"");
            builder.AppendLine("      sha256 \"" + intelSha + "\"");
            builder.AppendLine("    end");
            builder.AppendLine("  end");
            builder.AppendLine();
            builder.AppendLine("  def install");
            builder.AppendLine("    bin.install \"" + name + "\"");
            builder.AppendLine("  end");
            builder.AppendLine();
            builder.AppendLine("  test do");
            builder.AppendLine("    system \"#{bin}/" + name + "\", \"--version\"");
            builder.AppendLine("  end");
            builder.AppendLine("end");
            return builder.ToString();
        }

        private static string ToClassName(string name)
        {
            StringBuilder builder = new StringBuilder();
            bool upper = true;
            foreach (char c in name)
            {
                if (c == '-' || c == '_' || c == ' ')
                {
                    upper = true;
                    continue;
                }

                builder.Append(upper ? char.ToUpperInvariant(c) : c);
                upper = false;
            }

            return builder.Length == 0 ? "Formula" : builder.ToString();
        }

        private static string Ruby(string value)
        {
            return (value ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"");
        }
    }
}
