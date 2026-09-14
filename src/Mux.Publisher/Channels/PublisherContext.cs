namespace Mux.Publisher.Channels
{
    using System;
    using System.Collections.Generic;
    using Mux.Publisher.Manifest;

    /// <summary>
    /// Everything a channel driver needs to plan its work: the manifest, the release version, the
    /// repo/staging/output roots, and the set of already-published artifacts (with their SHA-256
    /// checksums) for the channel's artifact and applicable runtimes. Package-manager manifests read
    /// the checksum from <see cref="PublishedArtifact"/> rather than a hand-copied hash.
    /// </summary>
    public sealed class PublisherContext
    {
        /// <summary>The parsed manifest.</summary>
        public PublisherManifest Manifest { get; }

        /// <summary>The release version supplied at build time (never from the manifest).</summary>
        public string Version { get; }

        /// <summary>Absolute path to the repository root.</summary>
        public string RepoRoot { get; }

        /// <summary>Directory for recipes and intermediate packaging inputs.</summary>
        public string StagingRoot { get; }

        /// <summary>Directory for final, uploadable artifacts.</summary>
        public string OutputRoot { get; }

        /// <summary>The artifact this channel distributes.</summary>
        public ArtifactInfo Artifact { get; }

        /// <summary>The published outputs for this channel's artifact and runtimes.</summary>
        public IReadOnlyList<PublishedArtifact> Published { get; }

        /// <summary>
        /// Known SHA-256 checksums of already-built release assets, keyed by asset file name. The
        /// orchestrator fills this from artifacts other channels produced (for example the Inno installer
        /// that winget and Chocolatey reference), so a driver never hashes files during planning.
        /// </summary>
        public IReadOnlyDictionary<string, string> AssetChecksums { get; }

        private readonly Func<string, string> _assetUrlFactory;

        /// <summary>
        /// Initializes a new instance of the <see cref="PublisherContext"/> class.
        /// </summary>
        /// <param name="manifest">The parsed manifest.</param>
        /// <param name="version">The release version.</param>
        /// <param name="repoRoot">Repository root.</param>
        /// <param name="stagingRoot">Staging directory.</param>
        /// <param name="outputRoot">Output directory.</param>
        /// <param name="artifact">The channel's artifact.</param>
        /// <param name="published">Published outputs for the artifact.</param>
        /// <param name="assetUrlFactory">Maps a release asset file name to its download URL.</param>
        /// <param name="assetChecksums">Known checksums of assets other channels produced, by file name.</param>
        public PublisherContext(
            PublisherManifest manifest,
            string version,
            string repoRoot,
            string stagingRoot,
            string outputRoot,
            ArtifactInfo artifact,
            IReadOnlyList<PublishedArtifact> published,
            Func<string, string> assetUrlFactory,
            IReadOnlyDictionary<string, string>? assetChecksums = null)
        {
            Manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
            Version = version ?? throw new ArgumentNullException(nameof(version));
            RepoRoot = repoRoot ?? throw new ArgumentNullException(nameof(repoRoot));
            StagingRoot = stagingRoot ?? throw new ArgumentNullException(nameof(stagingRoot));
            OutputRoot = outputRoot ?? throw new ArgumentNullException(nameof(outputRoot));
            Artifact = artifact ?? throw new ArgumentNullException(nameof(artifact));
            Published = published ?? Array.Empty<PublishedArtifact>();
            AssetChecksums = assetChecksums ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            _assetUrlFactory = assetUrlFactory ?? throw new ArgumentNullException(nameof(assetUrlFactory));
        }

        /// <summary>
        /// Returns the known SHA-256 of a previously built release asset, or an empty string when the
        /// asset has not been produced (or its hash was not recorded).
        /// </summary>
        /// <param name="assetFileName">The asset file name.</param>
        /// <returns>The lowercase-hex checksum, or empty.</returns>
        public string AssetChecksum(string assetFileName)
        {
            return AssetChecksums.TryGetValue(assetFileName, out string? sha) ? sha : string.Empty;
        }

        /// <summary>
        /// Returns the GitHub Release download URL for an asset uploaded under the release version.
        /// </summary>
        /// <param name="assetFileName">The asset file name.</param>
        /// <returns>The absolute download URL.</returns>
        public string ReleaseAssetUrl(string assetFileName)
        {
            return _assetUrlFactory(assetFileName);
        }

        /// <summary>
        /// Finds the published output for a runtime identifier.
        /// </summary>
        /// <param name="rid">The runtime identifier.</param>
        /// <returns>The published artifact, or null when the artifact was not published for that rid.</returns>
        public PublishedArtifact? PublishedFor(string rid)
        {
            foreach (PublishedArtifact published in Published)
            {
                if (string.Equals(published.Rid, rid, StringComparison.OrdinalIgnoreCase)) return published;
            }

            return null;
        }
    }

    /// <summary>
    /// A single published output of an artifact for one runtime identifier: the self-contained,
    /// single-file publish directory and its primary binary, plus SHA-256 checksums for the binary
    /// and any archive produced from it.
    /// </summary>
    public sealed class PublishedArtifact
    {
        /// <summary>The artifact id.</summary>
        public string ArtifactId { get; set; } = string.Empty;

        /// <summary>The runtime identifier this output targets.</summary>
        public string Rid { get; set; } = string.Empty;

        /// <summary>The target framework this output was built for.</summary>
        public string Tfm { get; set; } = string.Empty;

        /// <summary>The self-contained publish directory.</summary>
        public string PublishDir { get; set; } = string.Empty;

        /// <summary>The primary binary or app launcher inside <see cref="PublishDir"/>.</summary>
        public string PrimaryBinary { get; set; } = string.Empty;

        /// <summary>SHA-256 of the primary binary, lowercase hex; empty when not yet computed.</summary>
        public string Sha256 { get; set; } = string.Empty;

        /// <summary>Path to a distributable archive (zip/tar.gz) of the publish output, if built.</summary>
        public string? ArchivePath { get; set; }

        /// <summary>SHA-256 of the archive, lowercase hex; empty when not built.</summary>
        public string ArchiveSha256 { get; set; } = string.Empty;
    }
}
