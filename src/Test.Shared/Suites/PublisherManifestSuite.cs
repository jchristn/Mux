namespace Test.Shared.Suites
{
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Publisher.Manifest;
    using Touchstone.Core;

    /// <summary>
    /// Touchstone suite for the publisher manifest: it parses, it enforces the "no version encoded"
    /// rule, and it rejects channels that reference unknown artifacts.
    /// </summary>
    public static class PublisherManifestSuite
    {
        private const string ValidJson = @"{
  ""project"": { ""name"": ""mux"", ""repo"": ""jchristn/mux"" },
  ""build"": {
    ""artifacts"": [ { ""id"": ""cli"", ""csproj"": ""src/Mux.Cli/Mux.Cli.csproj"", ""kind"": ""DotnetTool"" } ],
    ""frameworks"": [ ""net10.0"" ],
    ""runtimes"": [ ""win-x64"" ]
  },
  ""channels"": { ""nuget"": { ""enabled"": true, ""artifact"": ""cli"" } }
}";

        /// <summary>Builds the manifest suite descriptor.</summary>
        /// <returns>The suite descriptor.</returns>
        public static TestSuiteDescriptor Create()
        {
            return new TestSuiteDescriptor(
                "PublisherManifest",
                "publisher.json parsing and validation",
                new List<TestCaseDescriptor>
                {
                    new TestCaseDescriptor("PublisherManifest", "ParsesAndValidates", "A well-formed manifest parses and validates", (CancellationToken ct) =>
                    {
                        PublisherManifest manifest = PublisherManifest.Parse(ValidJson);
                        MuxAssert.AreEqual("mux", manifest.Project.Name, "project name");
                        MuxAssert.AreEqual(ArtifactKind.DotnetTool, manifest.GetArtifact("cli").Kind, "artifact kind");
                        ManifestValidator.Validate(manifest);
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("PublisherManifest", "RejectsEncodedVersion", "A manifest that encodes a version fails validation", (CancellationToken ct) =>
                    {
                        PublisherManifest manifest = PublisherManifest.Parse(ValidJson);
                        manifest.Version = "1.2.3";
                        List<string> errors = ManifestValidator.Collect(manifest);
                        MuxAssert.IsTrue(errors.Count > 0, "has errors");
                        bool mentionsVersion = errors.Exists(e => e.Contains("version"));
                        MuxAssert.IsTrue(mentionsVersion, "error mentions version");
                        MuxAssert.Throws<ManifestValidationException>(() => ManifestValidator.Validate(manifest), "throws");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("PublisherManifest", "RejectsUnknownArtifact", "A channel referencing an unknown artifact fails validation", (CancellationToken ct) =>
                    {
                        PublisherManifest manifest = PublisherManifest.Parse(ValidJson);
                        manifest.Channels["inno"] = new ChannelConfig { Enabled = true, Artifact = "ghost" };
                        List<string> errors = ManifestValidator.Collect(manifest);
                        bool mentionsGhost = errors.Exists(e => e.Contains("ghost"));
                        MuxAssert.IsTrue(mentionsGhost, "error mentions the unknown artifact");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("PublisherManifest", "RepoManifestIsValid", "The committed publisher.json parses and validates", (CancellationToken ct) =>
                    {
                        string? repoRoot = TestPaths.FindRepoRoot();
                        if (repoRoot == null)
                        {
                            // Repo layout not available in this runner; the inline cases cover the logic.
                            return Task.CompletedTask;
                        }

                        string path = System.IO.Path.Combine(repoRoot, "publisher.json");
                        if (!System.IO.File.Exists(path)) return Task.CompletedTask;

                        PublisherManifest manifest = PublisherManifest.Parse(System.IO.File.ReadAllText(path));
                        ManifestValidator.Validate(manifest);
                        MuxAssert.IsTrue(manifest.Channels.ContainsKey("inno"), "has inno channel");
                        MuxAssert.IsTrue(manifest.Channels.ContainsKey("dmg"), "has dmg channel");
                        MuxAssert.IsTrue(manifest.Channels.ContainsKey("debrpm"), "has debrpm channel");
                        MuxAssert.IsNull(manifest.Version, "committed manifest encodes no version");

                        // The desktop artifact bundles the tray agent and the CLI into its payload.
                        ArtifactInfo? desktop = manifest.Build.Artifacts.Find(a => string.Equals(a.Id, "desktop", StringComparison.Ordinal));
                        MuxAssert.IsNotNull(desktop, "has a desktop artifact");
                        MuxAssert.IsTrue(desktop!.Bundle.Exists(b => b.Csproj.Contains("Mux.Agent") && b.Role == "agent"), "bundles the tray agent");
                        MuxAssert.IsTrue(desktop.Bundle.Exists(b => b.Csproj.Contains("Mux.Cli") && b.Role == "cli"), "bundles the CLI");
                        return Task.CompletedTask;
                    })
                });
        }
    }
}
