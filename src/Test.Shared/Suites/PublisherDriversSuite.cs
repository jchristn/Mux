namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Publisher.Channels;
    using Mux.Publisher.Channels.Drivers;
    using Mux.Publisher.Manifest;
    using Mux.Publisher.Publishing;
    using Touchstone.Core;

    /// <summary>
    /// Touchstone suite for the channel drivers: each renders correct recipe content and command plans
    /// from a fabricated context, checksums flow from the published artifacts (never hand-copied), and the
    /// external-gate channels report a pending state.
    /// </summary>
    public static class PublisherDriversSuite
    {
        private const string Version = "1.2.3";

        /// <summary>Builds the drivers suite descriptor.</summary>
        /// <returns>The suite descriptor.</returns>
        public static TestSuiteDescriptor Create()
        {
            return new TestSuiteDescriptor(
                "PublisherDrivers",
                "Channel driver rendering and command plans",
                new List<TestCaseDescriptor>
                {
                    Case("InnoRendersScriptAndSigns", "Inno renders an .iss and signs the installer", () =>
                    {
                        ChannelConfig cfg = Cfg("desktop", "win-x64");
                        PublisherContext ctx = Ctx(ArtifactKind.Gui, cfg, "win-x64");
                        ChannelPlan plan = new InnoSetupDriver().Plan(ctx, cfg);
                        GeneratedFile iss = Find(plan, ".iss");
                        MuxAssert.Contains("[Setup]", iss.Content, "iss has setup section");
                        MuxAssert.Contains("AppVersion=" + Version, iss.Content, "iss version");
                        MuxAssert.Contains("[Run]", iss.Content, "iss launches app post-install");
                        // The installer must show a license page and demand acceptance before proceeding.
                        MuxAssert.Contains("LicenseFile=", iss.Content, "iss references a license file");
                        GeneratedFile license = Find(plan, "LICENSE.txt");
                        MuxAssert.Contains("Permission is hereby granted", license.Content, "license text staged");
                        // The bundled CLI is put on PATH via a de-duplicated system PATH entry.
                        MuxAssert.Contains("ChangesEnvironment=yes", iss.Content, "declares environment change");
                        MuxAssert.Contains("NeedsAddPath", iss.Content, "adds the install dir to PATH");
                        MuxAssert.IsTrue(HasCommand(plan, "iscc"), "compiles with iscc");
                        MuxAssert.IsTrue(HasCommand(plan, "signtool"), "signs with signtool");
                        MuxAssert.AreEqual(TargetOs.Windows, new InnoSetupDriver().RequiredOs, "requires windows");
                    }),

                    Case("WixRendersMsi", "WiX renders a .wxs using the Files element", () =>
                    {
                        ChannelConfig cfg = Cfg("desktop", "win-x64");
                        PublisherContext ctx = Ctx(ArtifactKind.Gui, cfg, "win-x64");
                        ChannelPlan plan = new WixDriver().Plan(ctx, cfg);
                        GeneratedFile wxs = Find(plan, ".wxs");
                        MuxAssert.Contains("<Files Directory=\"INSTALLFOLDER\"", wxs.Content, "harvests publish dir");
                        MuxAssert.Contains("UpgradeCode=", wxs.Content, "stable upgrade code");
                        // The MSI must present a license the user accepts, via the WixUI dialog set.
                        MuxAssert.Contains("WixUI_Minimal", wxs.Content, "uses the WixUI license dialog set");
                        MuxAssert.Contains("WixUILicenseRtf", wxs.Content, "supplies the license RTF");
                        MuxAssert.IsTrue(HasCommandWithArg(plan, "wix", "WixToolset.UI.wixext"), "references the UI extension at build");
                        GeneratedFile rtf = Find(plan, "License.rtf");
                        MuxAssert.Contains("\\rtf1", rtf.Content, "license is RTF");
                        MuxAssert.Contains("Permission is hereby granted", rtf.Content, "license text present");
                        // The bundled CLI is put on PATH via an Environment component.
                        MuxAssert.Contains("<Environment", wxs.Content, "adds a PATH environment entry");
                        MuxAssert.Contains("Name=\"PATH\"", wxs.Content, "targets PATH");
                        MuxAssert.IsTrue(HasCommand(plan, "wix"), "builds with wix");
                    }),

                    Case("WingetReferencesInstallerHashAndIsPending", "winget uses the installer hash and reports pending", () =>
                    {
                        ChannelConfig cfg = Cfg("desktop", "win-x64");
                        cfg.Options["packageId"] = System.Text.Json.JsonSerializer.SerializeToElement("jchristn.mux");
                        PublisherContext ctx = Ctx(ArtifactKind.Gui, cfg, "win-x64");
                        ChannelPlan plan = new WingetDriver().Plan(ctx, cfg);
                        GeneratedFile installer = Find(plan, ".installer.yaml");
                        MuxAssert.Contains("InstallerSha256: " + InstallerHash().ToUpperInvariant(), installer.Content, "uses recorded installer hash");
                        MuxAssert.IsTrue(plan.EndsAtExternalGate, "winget is an external gate");
                    }),

                    Case("ChocolateyDownloadsAndVerifies", "Chocolatey install script verifies the installer hash", () =>
                    {
                        ChannelConfig cfg = Cfg("desktop", "win-x64");
                        PublisherContext ctx = Ctx(ArtifactKind.Gui, cfg, "win-x64");
                        ChannelPlan plan = new ChocolateyDriver().Plan(ctx, cfg);
                        GeneratedFile nuspec = Find(plan, ".nuspec");
                        GeneratedFile install = Find(plan, "chocolateyInstall.ps1");
                        MuxAssert.Contains("<id>mux</id>", nuspec.Content, "nuspec id");
                        MuxAssert.Contains("checksum      = '" + InstallerHash() + "'", install.Content, "verifies installer checksum");
                        MuxAssert.Contains("VERYSILENT", install.Content, "silent install");
                        MuxAssert.IsTrue(plan.EndsAtExternalGate, "choco is an external gate");
                    }),

                    Case("ScoopReferencesArchiveHash", "Scoop manifest uses the CLI archive hash", () =>
                    {
                        ChannelConfig cfg = Cfg("cli", "win-x64");
                        PublisherContext ctx = Ctx(ArtifactKind.DotnetTool, cfg, "win-x64");
                        ChannelPlan plan = new ScoopDriver().Plan(ctx, cfg);
                        GeneratedFile json = Find(plan, ".json");
                        MuxAssert.Contains("\"hash\": \"" + ArchiveHash() + "\"", json.Content, "uses archive hash");
                        MuxAssert.Contains("\"bin\": \"mux.exe\"", json.Content, "bin is mux.exe");
                    }),

                    Case("DmgAssemblesBundleSignsNotarizes", "dmg builds .app, codesigns, notarizes, staples", () =>
                    {
                        ChannelConfig cfg = Cfg("desktop", "osx-arm64");
                        PublisherContext ctx = Ctx(ArtifactKind.Gui, cfg, "osx-arm64");
                        ChannelPlan plan = new DmgDriver().Plan(ctx, cfg);
                        GeneratedFile plist = Find(plan, "Info.plist");
                        MuxAssert.Contains("CFBundleIdentifier", plist.Content, "bundle identifier");
                        MuxAssert.IsTrue(HasCommand(plan, "codesign"), "codesigns");
                        MuxAssert.IsTrue(HasCommandWithArg(plan, "xcrun", "notarytool"), "notarizes");
                        MuxAssert.IsTrue(HasCommandWithArg(plan, "xcrun", "stapler"), "staples");
                        MuxAssert.IsTrue(HasCommand(plan, "hdiutil"), "builds dmg");
                        // The disk image must gate mounting behind a license agreement (SLA).
                        MuxAssert.IsTrue(HasCommandWithArg(plan, "hdiutil", "udifrez"), "attaches the SLA with udifrez");
                        GeneratedFile sla = Find(plan, "sla.plist");
                        MuxAssert.Contains("LPic", sla.Content, "SLA carries the language map");
                        MuxAssert.Contains("STR#", sla.Content, "SLA carries the button labels");
                        MuxAssert.Contains("TEXT", sla.Content, "SLA carries the license text resource");
                        // The tray agent and CLI ride inside the .app bundle.
                        MuxAssert.IsTrue(HasCommandWithArg(plan, "chmod", "Mux.Agent"), "marks the bundled tray agent executable");
                    }),

                    Case("HomebrewCaskForGuiFormulaForCli", "Homebrew renders a cask for GUI and a formula for CLI", () =>
                    {
                        ChannelConfig caskCfg = Cfg("desktop", "osx-arm64", "osx-x64");
                        PublisherContext caskCtx = Ctx(ArtifactKind.Gui, caskCfg, "osx-arm64", "osx-x64");
                        ChannelPlan caskPlan = new HomebrewDriver().Plan(caskCtx, caskCfg);
                        GeneratedFile cask = Find(caskPlan, "Casks/mux.rb");
                        MuxAssert.Contains("cask \"mux\" do", cask.Content, "is a cask");
                        MuxAssert.Contains("app \"mux.app\"", cask.Content, "installs the app");

                        ChannelConfig formulaCfg = Cfg("cli", "osx-arm64", "osx-x64");
                        PublisherContext formulaCtx = Ctx(ArtifactKind.DotnetTool, formulaCfg, "osx-arm64", "osx-x64");
                        ChannelPlan formulaPlan = new HomebrewDriver().Plan(formulaCtx, formulaCfg);
                        GeneratedFile formula = Find(formulaPlan, "Formula/mux.rb");
                        MuxAssert.Contains("class Mux < Formula", formula.Content, "is a formula");
                        MuxAssert.Contains("sha256 \"" + ArchiveHash() + "\"", formula.Content, "uses archive hash");
                    }),

                    Case("DebRpmBuildsBothWithFpm", "debrpm builds a .deb and .rpm with fpm", () =>
                    {
                        ChannelConfig cfg = Cfg("desktop", "linux-x64");
                        PublisherContext ctx = Ctx(ArtifactKind.Gui, cfg, "linux-x64");
                        ChannelPlan plan = new DebRpmDriver().Plan(ctx, cfg);
                        MuxAssert.IsTrue(HasCommandWithArg(plan, "fpm", "deb"), "builds deb");
                        MuxAssert.IsTrue(HasCommandWithArg(plan, "fpm", "rpm"), "builds rpm");
                        GeneratedFile desktop = Find(plan, ".desktop");
                        MuxAssert.Contains("[Desktop Entry]", desktop.Content, "desktop entry");
                        // /usr/bin/mux must be the CLI, not the GUI; the .desktop launcher runs the GUI binary.
                        MuxAssert.IsTrue(HasCommandWithArg(plan, "ln", "/opt/mux/mux"), "symlinks the CLI into /usr/bin");
                        MuxAssert.IsTrue(HasCommandWithArg(plan, "chmod", "Mux.Agent"), "marks the bundled tray agent executable");
                        MuxAssert.Contains("Mux.Desktop", desktop.Content, "desktop entry launches the GUI");
                    }),

                    Case("AppImagePacksSingleFile", "appimage assembles an AppDir and packs it", () =>
                    {
                        ChannelConfig cfg = Cfg("desktop", "linux-x64");
                        PublisherContext ctx = Ctx(ArtifactKind.Gui, cfg, "linux-x64");
                        ChannelPlan plan = new AppImageDriver().Plan(ctx, cfg);
                        GeneratedFile appRun = Find(plan, "AppRun");
                        MuxAssert.Contains("exec ", appRun.Content, "AppRun execs the binary");
                        MuxAssert.IsTrue(HasCommand(plan, "appimagetool"), "packs with appimagetool");
                    }),

                    Case("AptSignsRepoMetadata", "apt generates and GPG-signs Release metadata", () =>
                    {
                        ChannelConfig cfg = Cfg("desktop", "linux-x64");
                        PublisherContext ctx = Ctx(ArtifactKind.Gui, cfg, "linux-x64");
                        ChannelPlan plan = new AptRepoDriver().Plan(ctx, cfg);
                        MuxAssert.IsTrue(PlanText(plan).Contains("apt-ftparchive"), "generates Release");
                        MuxAssert.IsTrue(PlanText(plan).Contains("gpg"), "signs metadata with gpg");
                        MuxAssert.IsFalse(new AptRepoDriver().NeedsSelfContainedPublish, "apt consumes prior debs");
                    }),

                    Case("NuGetPacksAndPushes", "nuget packs the tool and pushes it", () =>
                    {
                        ChannelConfig cfg = Cfg("cli");
                        PublisherContext ctx = Ctx(ArtifactKind.DotnetTool, cfg);
                        ChannelPlan plan = new NuGetDriver().Plan(ctx, cfg);
                        MuxAssert.IsTrue(HasCommandWithArg(plan, "dotnet", "pack"), "packs");
                        MuxAssert.IsTrue(HasCommandWithArg(plan, "dotnet", "push"), "pushes");
                        MuxAssert.IsFalse(new NuGetDriver().NeedsSelfContainedPublish, "nuget is framework-dependent");
                    }),

                    Case("RegistryResolvesAllChannelsAndRejectsUnknown", "the driver registry knows every channel", () =>
                    {
                        foreach (string channel in new[] { "nuget", "scoop", "homebrew", "inno", "wix", "winget", "chocolatey", "dmg", "debrpm", "appimage", "apt", "yum", "snap", "flatpak" })
                        {
                            MuxAssert.IsTrue(DriverRegistry.IsKnown(channel), "knows " + channel);
                            MuxAssert.IsNotNull(DriverRegistry.Resolve(channel), "resolves " + channel);
                        }

                        MuxAssert.Throws<KeyNotFoundException>(() => DriverRegistry.Resolve("bogus"), "rejects unknown channel");
                    }),

                    Case("StableGuidIsDeterministic", "the same seed yields the same GUID", () =>
                    {
                        MuxAssert.AreEqual(StableGuid.FromString("inno:mux"), StableGuid.FromString("inno:mux"), "deterministic");
                        MuxAssert.AreNotEqual(StableGuid.FromString("inno:mux"), StableGuid.FromString("wix:mux"), "distinct seeds differ");
                    }),

                    Case("LicenseAssetsRenderEveryInstallerFormat", "license renders to plain text, RTF, and a dmg SLA resource plist", () =>
                    {
                        // Missing LICENSE.md falls back to the canonical MIT text so rendering never fails.
                        string plain = Mux.Publisher.Publishing.LicenseAssets.ReadLicenseText("/nonexistent-repo");
                        MuxAssert.Contains("MIT License", plain, "plain text carries the license");
                        MuxAssert.Contains("Permission is hereby granted", plain, "plain text carries the grant");

                        string rtf = Mux.Publisher.Publishing.LicenseAssets.ToRtf(plain);
                        MuxAssert.IsTrue(rtf.StartsWith("{\\rtf1", StringComparison.Ordinal), "RTF header");
                        MuxAssert.Contains("\\par", rtf, "RTF paragraph breaks");
                        MuxAssert.Contains("Permission is hereby granted", rtf, "RTF carries the license");

                        string sla = Mux.Publisher.Publishing.LicenseAssets.DmgSlaResourcesPlist(plain);
                        MuxAssert.Contains("<key>LPic</key>", sla, "SLA language map");
                        MuxAssert.Contains("<key>STR#</key>", sla, "SLA button labels");
                        MuxAssert.Contains("<key>TEXT</key>", sla, "SLA license text");
                        MuxAssert.Contains("<data>", sla, "SLA resources are base64 data");
                    }),

                    Case("BundledExecutableNamesResolve", "bundled project executable names map correctly per OS", () =>
                    {
                        // The CLI publishes as `mux` (its assembly name); Windows adds .exe.
                        MuxAssert.AreEqual("mux.exe", PublishService.ProjectExecutableName("src/Mux.Cli/Mux.Cli.csproj", "win-x64"), "cli on windows");
                        MuxAssert.AreEqual("mux", PublishService.ProjectExecutableName("src/Mux.Cli/Mux.Cli.csproj", "linux-x64"), "cli on linux");
                        MuxAssert.AreEqual("Mux.Agent.exe", PublishService.ProjectExecutableName("src/Mux.Agent/Mux.Agent.csproj", "win-x64"), "agent on windows");
                        MuxAssert.AreEqual("Mux.Agent", PublishService.ProjectExecutableName("src/Mux.Agent/Mux.Agent.csproj", "osx-arm64"), "agent on macOS");
                    })
                });
        }

        // ----- helpers -----------------------------------------------------------------------------

        private static TestCaseDescriptor Case(string name, string description, Action body)
        {
            return new TestCaseDescriptor("PublisherDrivers", name, description, (CancellationToken ct) =>
            {
                body();
                return Task.CompletedTask;
            });
        }

        private static string InstallerHash() => "1111111111111111111111111111111111111111111111111111111111111111";

        private static string ArchiveHash() => "2222222222222222222222222222222222222222222222222222222222222222";

        private static string DmgHash() => "3333333333333333333333333333333333333333333333333333333333333333";

        private static ChannelConfig Cfg(string artifact, params string[] runtimes)
        {
            return new ChannelConfig { Enabled = true, Artifact = artifact, Runtimes = new List<string>(runtimes) };
        }

        private static PublisherContext Ctx(ArtifactKind kind, ChannelConfig cfg, params string[] rids)
        {
            PublisherManifest manifest = new PublisherManifest();
            manifest.Project = new ProjectInfo
            {
                Name = "mux",
                DisplayName = "mux",
                Repo = "jchristn/mux",
                Homepage = "https://github.com/jchristn/mux",
                Description = "Multiplexed AI agent.",
                License = "MIT"
            };
            manifest.Build.Frameworks.Add("net10.0");
            manifest.Build.Runtimes.AddRange(rids);
            ArtifactInfo artifact = new ArtifactInfo { Id = cfg.Artifact, Csproj = "src/Mux." + (kind == ArtifactKind.Gui ? "Desktop/Mux.Desktop" : "Cli/Mux.Cli") + ".csproj", Kind = kind };
            manifest.Build.Artifacts.Add(artifact);
            manifest.Signing = new SigningInfo
            {
                MacOs = new SigningEntry { VaultRef = "APPLE_SIGNING", Notarize = true },
                Windows = new SigningEntry { VaultRef = "WINDOWS_SIGNING" },
                Linux = new SigningEntry { VaultRef = "GPG_SIGNING" }
            };

            // The desktop artifact bundles the tray agent and the CLI into its payload.
            if (kind == ArtifactKind.Gui)
            {
                artifact.Bundle.Add(new BundledProject { Csproj = "src/Mux.Agent/Mux.Agent.csproj", Role = "agent" });
                artifact.Bundle.Add(new BundledProject { Csproj = "src/Mux.Cli/Mux.Cli.csproj", Role = "cli" });
            }

            List<PublishedArtifact> published = new List<PublishedArtifact>();
            foreach (string rid in rids)
            {
                bool win = rid.StartsWith("win", StringComparison.OrdinalIgnoreCase);
                PublishedArtifact pub = new PublishedArtifact
                {
                    ArtifactId = artifact.Id,
                    Rid = rid,
                    Tfm = "net10.0",
                    PublishDir = "pub/" + rid,
                    PrimaryBinary = "pub/" + rid + "/" + (kind == ArtifactKind.Gui ? (win ? "Mux.Desktop.exe" : "Mux.Desktop") : (win ? "mux.exe" : "mux")),
                    Sha256 = "deadbeef",
                    ArchivePath = "out/" + Naming.Archive("mux", Version, rid),
                    ArchiveSha256 = ArchiveHash()
                };

                if (kind == ArtifactKind.Gui)
                {
                    pub.Bundled.Add(new BundledBinary { Role = "agent", FileName = win ? "Mux.Agent.exe" : "Mux.Agent" });
                    pub.Bundled.Add(new BundledBinary { Role = "cli", FileName = win ? "mux.exe" : "mux" });
                }

                published.Add(pub);
            }

            Dictionary<string, string> checksums = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [Naming.InnoInstaller("mux", Version, "win-x64")] = InstallerHash(),
                [Naming.Dmg("mux", Version, "osx-arm64")] = DmgHash(),
                [Naming.Dmg("mux", Version, "osx-x64")] = DmgHash()
            };

            return new PublisherContext(manifest, Version, "/repo", "/repo/staging", "/repo/out", artifact, published,
                fileName => "https://github.com/jchristn/mux/releases/download/v" + Version + "/" + fileName, checksums);
        }

        private static GeneratedFile Find(ChannelPlan plan, string pathContains)
        {
            foreach (GeneratedFile file in plan.Files)
            {
                if (file.RelativePath.Contains(pathContains)) return file;
            }

            throw new AssertionFailedException("no generated file whose path contains '" + pathContains + "' (channel " + plan.Channel + ")");
        }

        private static bool HasCommand(ChannelPlan plan, string exe)
        {
            return plan.Commands.Exists(c => string.Equals(c.Executable, exe, StringComparison.OrdinalIgnoreCase));
        }

        private static bool HasCommandWithArg(ChannelPlan plan, string exe, string argContains)
        {
            return plan.Commands.Exists(c => string.Equals(c.Executable, exe, StringComparison.OrdinalIgnoreCase)
                && c.Arguments.Exists(a => a.Contains(argContains)));
        }

        private static string PlanText(ChannelPlan plan)
        {
            System.Text.StringBuilder builder = new System.Text.StringBuilder();
            foreach (ShellCommand command in plan.Commands) builder.AppendLine(command.ToDisplayString());
            foreach (GeneratedFile file in plan.Files) builder.AppendLine(file.Content);
            return builder.ToString();
        }
    }
}
