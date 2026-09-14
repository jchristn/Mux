namespace Mux.Publisher.Manifest
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Validates a <see cref="PublisherManifest"/> against the packaging standard. The single most
    /// important rule: the manifest must never encode a version — a release is the manifest replayed
    /// against a version supplied at build time. The validator also checks that every channel points
    /// at a declared artifact and that the build matrix is non-empty.
    /// </summary>
    public static class ManifestValidator
    {
        /// <summary>
        /// Validates the manifest and throws <see cref="ManifestValidationException"/> on the first
        /// problem found.
        /// </summary>
        /// <param name="manifest">The manifest to validate.</param>
        public static void Validate(PublisherManifest manifest)
        {
            if (manifest == null) throw new ArgumentNullException(nameof(manifest));

            List<string> errors = Collect(manifest);
            if (errors.Count > 0)
            {
                throw new ManifestValidationException(errors);
            }
        }

        /// <summary>
        /// Collects all validation problems without throwing.
        /// </summary>
        /// <param name="manifest">The manifest to validate.</param>
        /// <returns>An ordered list of human-readable problems; empty when the manifest is valid.</returns>
        public static List<string> Collect(PublisherManifest manifest)
        {
            if (manifest == null) throw new ArgumentNullException(nameof(manifest));

            List<string> errors = new List<string>();

            // The defining rule of the standard: no version in the manifest.
            if (!string.IsNullOrWhiteSpace(manifest.Version))
            {
                errors.Add("publisher.json must not encode a version; pass the version at build time (--version). Found: '" + manifest.Version + "'.");
            }

            if (string.IsNullOrWhiteSpace(manifest.Project.Name))
            {
                errors.Add("project.name is required.");
            }

            if (manifest.Build.Artifacts.Count == 0)
            {
                errors.Add("build.artifacts must declare at least one artifact.");
            }

            if (manifest.Build.Runtimes.Count == 0)
            {
                errors.Add("build.runtimes must declare at least one runtime identifier.");
            }

            if (manifest.Build.Frameworks.Count == 0)
            {
                errors.Add("build.frameworks must declare at least one target framework.");
            }

            HashSet<string> artifactIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ArtifactInfo artifact in manifest.Build.Artifacts)
            {
                if (string.IsNullOrWhiteSpace(artifact.Id))
                {
                    errors.Add("every build.artifacts entry needs a non-empty id.");
                    continue;
                }

                if (!artifactIds.Add(artifact.Id))
                {
                    errors.Add("duplicate artifact id '" + artifact.Id + "'.");
                }

                if (string.IsNullOrWhiteSpace(artifact.Csproj))
                {
                    errors.Add("artifact '" + artifact.Id + "' needs a csproj path.");
                }
            }

            foreach (KeyValuePair<string, ChannelConfig> pair in manifest.Channels)
            {
                ChannelConfig channel = pair.Value;
                if (string.IsNullOrWhiteSpace(channel.Artifact))
                {
                    errors.Add("channel '" + pair.Key + "' must reference an artifact.");
                }
                else if (!artifactIds.Contains(channel.Artifact))
                {
                    errors.Add("channel '" + pair.Key + "' references unknown artifact '" + channel.Artifact + "'.");
                }
            }

            return errors;
        }
    }

    /// <summary>Raised when a manifest fails validation.</summary>
    public sealed class ManifestValidationException : Exception
    {
        /// <summary>The list of validation problems.</summary>
        public IReadOnlyList<string> Errors { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="ManifestValidationException"/> class.
        /// </summary>
        /// <param name="errors">The collected validation problems.</param>
        public ManifestValidationException(IReadOnlyList<string> errors)
            : base("publisher.json is invalid:" + Environment.NewLine + "  - " + string.Join(Environment.NewLine + "  - ", errors))
        {
            Errors = errors;
        }
    }
}
