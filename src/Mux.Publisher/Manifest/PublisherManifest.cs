namespace Mux.Publisher.Manifest
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using System.Text.Json.Serialization;

    /// <summary>
    /// The committed <c>publisher.json</c> manifest: the single source of truth for a release. It
    /// describes the artifacts, the runtime-identifier matrix, the enabled channels, and the per-OS
    /// signing configuration — but never the version. A release is this manifest replayed against a
    /// version string supplied at build time (see <see cref="ManifestValidator"/>).
    /// </summary>
    public sealed class PublisherManifest
    {
        /// <summary>Manifest schema version, for forward compatibility.</summary>
        [JsonPropertyName("schemaVersion")]
        public int SchemaVersion { get; set; } = 1;

        /// <summary>Project identity (name, repo, homepage, license).</summary>
        [JsonPropertyName("project")]
        public ProjectInfo Project { get; set; } = new ProjectInfo();

        /// <summary>Build inputs: the artifacts to publish and the framework/runtime matrix.</summary>
        [JsonPropertyName("build")]
        public BuildInfo Build { get; set; } = new BuildInfo();

        /// <summary>Enabled distribution channels, keyed by channel name (inno, dmg, winget, ...).</summary>
        [JsonPropertyName("channels")]
        public Dictionary<string, ChannelConfig> Channels { get; set; } = new Dictionary<string, ChannelConfig>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Per-OS signing configuration referencing secret names (never secret values).</summary>
        [JsonPropertyName("signing")]
        public SigningInfo Signing { get; set; } = new SigningInfo();

        /// <summary>Where release notes are sourced from for a given version.</summary>
        [JsonPropertyName("releaseNotes")]
        public ReleaseNotesInfo? ReleaseNotes { get; set; }

        /// <summary>
        /// A version string, if one was (incorrectly) committed into the manifest. Must always be
        /// null/absent: <see cref="ManifestValidator"/> rejects a manifest that encodes a version.
        /// </summary>
        [JsonPropertyName("version")]
        public string? Version { get; set; }

        private static readonly JsonSerializerOptions SerializerOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        /// <summary>
        /// Deserializes a manifest from its JSON text.
        /// </summary>
        /// <param name="json">The raw <c>publisher.json</c> content.</param>
        /// <returns>The parsed manifest.</returns>
        public static PublisherManifest Parse(string json)
        {
            if (json == null) throw new ArgumentNullException(nameof(json));
            PublisherManifest? manifest = JsonSerializer.Deserialize<PublisherManifest>(json, SerializerOptions);
            if (manifest == null) throw new FormatException("publisher.json deserialized to null.");
            return manifest;
        }

        /// <summary>
        /// Looks up an artifact by id.
        /// </summary>
        /// <param name="id">The artifact id (for example <c>cli</c> or <c>desktop</c>).</param>
        /// <returns>The matching artifact.</returns>
        public ArtifactInfo GetArtifact(string id)
        {
            foreach (ArtifactInfo artifact in Build.Artifacts)
            {
                if (string.Equals(artifact.Id, id, StringComparison.OrdinalIgnoreCase)) return artifact;
            }

            throw new KeyNotFoundException("No artifact with id '" + id + "' in publisher.json.");
        }
    }

    /// <summary>Project identity metadata.</summary>
    public sealed class ProjectInfo
    {
        /// <summary>Short package name (for example <c>mux</c>).</summary>
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        /// <summary>Human-facing display name.</summary>
        [JsonPropertyName("displayName")]
        public string DisplayName { get; set; } = string.Empty;

        /// <summary>The <c>owner/repo</c> slug on GitHub.</summary>
        [JsonPropertyName("repo")]
        public string Repo { get; set; } = string.Empty;

        /// <summary>Project homepage URL.</summary>
        [JsonPropertyName("homepage")]
        public string Homepage { get; set; } = string.Empty;

        /// <summary>One-line description.</summary>
        [JsonPropertyName("description")]
        public string Description { get; set; } = string.Empty;

        /// <summary>SPDX license identifier.</summary>
        [JsonPropertyName("license")]
        public string License { get; set; } = string.Empty;
    }

    /// <summary>Build inputs: artifacts and the framework/runtime matrix.</summary>
    public sealed class BuildInfo
    {
        /// <summary>The publishable artifacts.</summary>
        [JsonPropertyName("artifacts")]
        public List<ArtifactInfo> Artifacts { get; set; } = new List<ArtifactInfo>();

        /// <summary>Target frameworks (for example <c>net10.0</c>, <c>net8.0</c>). The first is the default.</summary>
        [JsonPropertyName("frameworks")]
        public List<string> Frameworks { get; set; } = new List<string>();

        /// <summary>The runtime-identifier matrix (win-x64, osx-arm64, linux-x64, ...).</summary>
        [JsonPropertyName("runtimes")]
        public List<string> Runtimes { get; set; } = new List<string>();
    }

    /// <summary>A single publishable artifact.</summary>
    public sealed class ArtifactInfo
    {
        /// <summary>Stable artifact id referenced by channels (for example <c>cli</c>, <c>desktop</c>).</summary>
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        /// <summary>Path to the project file, relative to the repo root.</summary>
        [JsonPropertyName("csproj")]
        public string Csproj { get; set; } = string.Empty;

        /// <summary>The artifact kind, controlling how it is published and packaged.</summary>
        [JsonPropertyName("kind")]
        public ArtifactKind Kind { get; set; } = ArtifactKind.Console;
    }

    /// <summary>The kind of a publishable artifact.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum ArtifactKind
    {
        /// <summary>A .NET global tool, shipped framework-dependent via NuGet (<c>dotnet tool install</c>).</summary>
        DotnetTool,

        /// <summary>A GUI application (self-contained, wrapped in a desktop installer).</summary>
        Gui,

        /// <summary>A self-contained console application.</summary>
        Console
    }

    /// <summary>Configuration for a single distribution channel.</summary>
    public sealed class ChannelConfig
    {
        /// <summary>Whether this channel participates in a release.</summary>
        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; }

        /// <summary>The id of the artifact this channel distributes.</summary>
        [JsonPropertyName("artifact")]
        public string Artifact { get; set; } = string.Empty;

        /// <summary>
        /// Optional driver override, letting several channel keys share one driver (for example a
        /// <c>homebrew-cask</c> and a <c>homebrew</c> key both driven by the Homebrew driver). When
        /// absent, the channel key names the driver.
        /// </summary>
        [JsonPropertyName("driver")]
        public string? Driver { get; set; }

        /// <summary>Optional runtime subset; when empty, the channel applies to all build runtimes it supports.</summary>
        [JsonPropertyName("runtimes")]
        public List<string> Runtimes { get; set; } = new List<string>();

        /// <summary>
        /// Optional explicit CI matrix job (<c>windows</c>/<c>macos</c>/<c>linux</c>) this channel runs in.
        /// Required for OS-agnostic channels (nuget, scoop, homebrew) so job placement is unambiguous;
        /// inferred from the driver's required OS otherwise.
        /// </summary>
        [JsonPropertyName("os")]
        public string? Os { get; set; }

        /// <summary>Free-form channel options (tap, bucket, packageId, vaultRef, ...).</summary>
        [JsonPropertyName("options")]
        public Dictionary<string, JsonElement> Options { get; set; } = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Reads a string option by key, or returns the fallback when absent.
        /// </summary>
        /// <param name="key">The option name.</param>
        /// <param name="fallback">Value to return when the option is missing.</param>
        /// <returns>The option value, or the fallback.</returns>
        public string OptionOrDefault(string key, string fallback = "")
        {
            if (Options != null && Options.TryGetValue(key, out JsonElement value) && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString() ?? fallback;
            }

            return fallback;
        }
    }

    /// <summary>Per-OS signing configuration. Values are secret names, never secret material.</summary>
    public sealed class SigningInfo
    {
        /// <summary>macOS signing/notarization identity.</summary>
        [JsonPropertyName("macOs")]
        public SigningEntry? MacOs { get; set; }

        /// <summary>Windows Authenticode identity.</summary>
        [JsonPropertyName("windows")]
        public SigningEntry? Windows { get; set; }

        /// <summary>Linux repository-metadata GPG identity.</summary>
        [JsonPropertyName("linux")]
        public SigningEntry? Linux { get; set; }
    }

    /// <summary>A signing identity that references a secret by name.</summary>
    public sealed class SigningEntry
    {
        /// <summary>The name of the secret providing the signing credential.</summary>
        [JsonPropertyName("vaultRef")]
        public string VaultRef { get; set; } = string.Empty;

        /// <summary>Whether this OS additionally requires notarization (macOS).</summary>
        [JsonPropertyName("notarize")]
        public bool Notarize { get; set; }
    }

    /// <summary>Where to source release notes for a version.</summary>
    public sealed class ReleaseNotesInfo
    {
        /// <summary>The file to read notes from (for example <c>CHANGELOG.md</c>).</summary>
        [JsonPropertyName("source")]
        public string Source { get; set; } = string.Empty;

        /// <summary>The section header pattern; <c>{version}</c> is substituted with the release version.</summary>
        [JsonPropertyName("section")]
        public string Section { get; set; } = string.Empty;
    }
}
