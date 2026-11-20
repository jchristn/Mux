namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.IO;
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
                        MuxAssert.Contains("AppPublisher=Lattice AI, LLC", iss.Content, "publisher");
                        MuxAssert.Contains("AppPublisherURL=https://latticetech.ai", iss.Content, "publisher URL");
                        MuxAssert.Contains("WizardImageFile=", iss.Content, "welcome image");
                        MuxAssert.Contains("wizard-328x604.bmp", iss.Content, "high-DPI welcome image");
                        MuxAssert.Contains("WizardSmallImageFile=", iss.Content, "small image");
                        MuxAssert.Contains("WelcomeLabel2=Your AI agent. Your models. Your infra. Anywhere.%n%n", iss.Content, "welcome tagline");
                        MuxAssert.Contains("the mux command (added to your PATH)", iss.Content, "welcome says what is installed");
                        MuxAssert.Contains("Mux.Agent.exe\"; Flags: nowait runasoriginaluser", iss.Content, "starts the agent unelevated");
                        MuxAssert.Contains("[UninstallRun]", iss.Content, "stops the agent before uninstalling");
                        MuxAssert.Contains("AppId={{" + StableGuid.FromString("inno:mux") + "}", iss.Content, "AppId unchanged so upgrades replace 1.x");
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
                        // udifrez is deprecated and fails on current macOS, so it must not stop the release.
                        MuxAssert.IsTrue(plan.Commands.Find(c => c.Arguments.Contains("udifrez"))!.ContinueOnError, "SLA step is best-effort");
                        MuxAssert.Contains("Permission is hereby granted", Find(plan, "Contents/Resources/LICENSE.txt").Content, "license ships in the app bundle");
                        GeneratedFile sla = Find(plan, "sla.plist");
                        MuxAssert.Contains("LPic", sla.Content, "SLA carries the language map");
                        MuxAssert.Contains("STR#", sla.Content, "SLA carries the button labels");
                        MuxAssert.Contains("TEXT", sla.Content, "SLA carries the license text resource");
                        // The tray agent and CLI ride inside the .app bundle.
                        MuxAssert.IsTrue(HasCommandWithArg(plan, "chmod", "Mux.Agent"), "marks the bundled tray agent executable");
                        MuxAssert.IsTrue(HasCommandWithArg(plan, "codesign", "--verify"), "verifies the bundle signature");
                        MuxAssert.Contains("ai.latticetech.mux", plist.Content, "bundle id is the project identifier");
                    }),

                    Case("DmgUnsignedAdHocSignsWithShortcutAndNote", "an unsigned dmg is ad-hoc signed and carries the Applications shortcut and the Gatekeeper note", () =>
                    {
                        ChannelConfig cfg = Cfg("desktop", "osx-arm64");
                        PublisherContext ctx = Ctx(ArtifactKind.Gui, cfg, false, "osx-arm64");
                        ChannelPlan plan = new DmgDriver().Plan(ctx, cfg);
                        ShellCommand sign = plan.Commands.Find(c => c.Executable == "codesign" && c.Arguments.Contains("--sign"))!;
                        MuxAssert.IsTrue(sign != null && sign.Arguments.Contains("-"), "ad-hoc identity");
                        MuxAssert.IsFalse(HasCommandWithArg(plan, "xcrun", "notarytool"), "no notarization without a certificate");
                        int signAt = plan.Commands.IndexOf(sign!);
                        int lastBundleStep = plan.Commands.FindLastIndex(c => c.Executable == "chmod" || c.Executable == "cp" || c.Executable == "sh");
                        MuxAssert.IsTrue(signAt > lastBundleStep, "signs after every file is in place");
                        MuxAssert.IsTrue(HasCommandWithArg(plan, "sh", "move-non-code.sh"), "moves non-code files out of Contents/MacOS before signing");
                        MuxAssert.AreEqual(MacAppBundle.MoveNonCodeScript, Find(plan, "move-non-code.sh").Content, "the script is staged as a file, so $VAR expansion leaves it alone");
                        MuxAssert.IsTrue(HasCommandWithArg(plan, "ln", "/Applications"), "adds the Applications shortcut");
                        GeneratedFile note = Find(plan, "If mux won't open.txt");
                        MuxAssert.Contains("Open Anyway", note.Content, "note has the Gatekeeper steps");
                        MuxAssert.Contains("xattr -dr com.apple.quarantine /Applications/mux.app", note.Content, "note has the Terminal route");
                        MuxAssert.Contains("/usr/local/bin/mux", note.Content, "note explains the PATH link");
                    }),

                    Case("PkgBuildsInstallerWithPostinstall", "pkg builds a product archive with branding, license, PATH link, and the agent LaunchAgent", () =>
                    {
                        ChannelConfig cfg = Cfg("desktop", "osx-arm64");
                        PublisherContext ctx = Ctx(ArtifactKind.Gui, cfg, false, "osx-arm64");
                        ChannelPlan plan = new PkgDriver().Plan(ctx, cfg);
                        GeneratedFile dist = Find(plan, "distribution.xml");
                        MuxAssert.Contains("<title>mux</title>", dist.Content, "titled with the product name");
                        MuxAssert.Contains("<background file=\"pkg-background.png\" alignment=\"bottomleft\" scaling=\"none\"/>", dist.Content, "logo background");
                        MuxAssert.Contains("<background-darkAqua", dist.Content, "dark mode background");
                        MuxAssert.Contains("<license file=\"license.txt\"", dist.Content, "license to agree to");
                        MuxAssert.Contains("<welcome file=\"welcome.html\"", dist.Content, "welcome page");
                        MuxAssert.Contains("hostArchitectures=\"arm64\"", dist.Content, "arm64 host");
                        MuxAssert.Contains("Your AI agent. Your models. Your infra. Anywhere.", Find(plan, "welcome.html").Content, "welcome shows the tagline");
                        MuxAssert.Contains("Permission is hereby granted", Find(plan, "resources/license.txt").Content, "license text");
                        GeneratedFile post = Find(plan, "scripts/postinstall");
                        MuxAssert.Contains("mkdir -p /usr/local/bin", post.Content, "creates /usr/local/bin first");
                        MuxAssert.Contains("ln -sf \"$APP/Contents/MacOS/mux\" /usr/local/bin/mux", post.Content, "links the command");
                        MuxAssert.Contains("launchctl bootout \"gui/$USER_ID/com.jchristn.mux.agent\"", post.Content, "removes the 1.x agent");
                        MuxAssert.Contains("<string>ai.latticetech.mux.agent</string>", post.Content, "LaunchAgent label");
                        MuxAssert.Contains("/Applications/mux.app/Contents/MacOS/Mux.Agent", post.Content, "LaunchAgent runs the bundled agent");
                        MuxAssert.Contains("launchctl bootstrap", post.Content, "starts the agent");
                        MuxAssert.IsFalse(post.Content.Contains("\r"), "LF line endings");
                        MuxAssert.IsTrue(HasCommandWithArg(plan, "plutil", "BundleIsRelocatable"), "turns off relocation");
                        MuxAssert.IsTrue(HasCommandWithArg(plan, "pkgbuild", "ai.latticetech.mux"), "component identifier");
                        MuxAssert.IsTrue(HasCommandWithArg(plan, "productbuild", "mux-" + Version + "-osx-arm64.pkg"), "builds the named .pkg");
                        MuxAssert.Contains("ai.latticetech.mux", Find(plan, "Info.plist").Content, "bundle id");
                        MuxAssert.IsTrue(HasCommandWithArg(plan, "codesign", "-"), "ad-hoc signs the app");
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
                        MuxAssert.Contains("Icon=ai.latticetech.mux", desktop.Content, "menu entry icon by identifier");
                        GeneratedFile autostart = Find(plan, "etc/xdg/autostart/ai.latticetech.mux.agent.desktop");
                        MuxAssert.Contains("Exec=/opt/mux/Mux.Agent", autostart.Content, "autostart runs the tray agent");
                        MuxAssert.Contains("Permission is hereby granted", Find(plan, "usr/share/doc/mux/copyright").Content, "deb license");
                        MuxAssert.Contains("Permission is hereby granted", Find(plan, "usr/share/licenses/mux/LICENSE").Content, "rpm license");
                        MuxAssert.IsTrue(HasCommandWithArg(plan, "cp", "hicolor/256x256/apps/ai.latticetech.mux.png"), "installs hicolor icons");
                        ShellCommand deb = plan.Commands.Find(c => c.Executable == "fpm" && c.Arguments.Contains("deb"))!;
                        ShellCommand rpm = plan.Commands.Find(c => c.Executable == "fpm" && c.Arguments.Contains("rpm"))!;
                        MuxAssert.IsTrue(deb.Arguments.Exists(a => a.StartsWith("libicu", StringComparison.Ordinal)), "deb declares ICU");
                        MuxAssert.IsTrue(deb.Arguments.Contains("libfontconfig1"), "deb declares fontconfig");
                        MuxAssert.IsTrue(rpm.Arguments.Contains("libicu") && rpm.Arguments.Contains("libX11"), "rpm declares ICU and X11");
                        MuxAssert.IsTrue(deb.Arguments.Contains("Lattice AI, LLC"), "vendor is the publisher");
                        MuxAssert.IsTrue(deb.Arguments.Exists(a => a.StartsWith("Your AI agent.", StringComparison.Ordinal)), "description starts with the tagline");
                    }),

                    Case("AppImagePacksSingleFile", "appimage assembles an AppDir and packs it", () =>
                    {
                        ChannelConfig cfg = Cfg("desktop", "linux-x64");
                        PublisherContext ctx = Ctx(ArtifactKind.Gui, cfg, "linux-x64");
                        ChannelPlan plan = new AppImageDriver().Plan(ctx, cfg);
                        GeneratedFile appRun = Find(plan, "AppRun");
                        MuxAssert.Contains("exec ", appRun.Content, "AppRun execs the binary");
                        MuxAssert.IsTrue(HasCommandWithArg(plan, "env", "appimagetool"), "packs with appimagetool");
                        MuxAssert.IsTrue(HasCommandWithArg(plan, "env", "ARCH=x86_64"), "sets ARCH for x64");
                        MuxAssert.IsTrue(HasCommandWithArg(plan, "env", "APPIMAGE_EXTRACT_AND_RUN=1"), "runs appimagetool without FUSE");
                        MuxAssert.Contains("Icon=ai.latticetech.mux", Find(plan, "ai.latticetech.mux.desktop").Content, "top-level desktop entry");
                        MuxAssert.IsTrue(HasCommandWithArg(plan, "cp", ".DirIcon"), "adds .DirIcon");
                    }),

                    Case("AppImageSetsArchPerRuntime", "appimage passes ARCH to appimagetool for each runtime it packs", () =>
                    {
                        ChannelConfig cfg = Cfg("desktop", "linux-arm64");
                        PublisherContext ctx = Ctx(ArtifactKind.Gui, cfg, "linux-arm64");
                        ChannelPlan plan = new AppImageDriver().Plan(ctx, cfg);
                        ShellCommand? pack = plan.Commands.Find(c => c.Arguments.Contains("appimagetool"));
                        MuxAssert.IsTrue(pack != null, "packs with appimagetool");
                        MuxAssert.AreEqual("env", pack!.Executable, "runs through env so ARCH is set");
                        MuxAssert.AreEqual("ARCH=aarch64", pack.Arguments[0], "arm64 packs as aarch64");
                        MuxAssert.AreEqual("x86_64", AppImageDriver.AppImageArch("linux-x64"), "x64 packs as x86_64");
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
                        foreach (string channel in new[] { "nuget", "scoop", "homebrew", "inno", "wix", "winget", "chocolatey", "dmg", "pkg", "debrpm", "appimage", "apt", "yum", "snap", "flatpak" })
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
                        // The CLI ships as `mux` (its tool command name); Windows adds .exe.
                        MuxAssert.AreEqual("mux.exe", PublishService.ProjectExecutableName("src/Mux.Cli/Mux.Cli.csproj", "win-x64"), "cli on windows");
                        MuxAssert.AreEqual("mux", PublishService.ProjectExecutableName("src/Mux.Cli/Mux.Cli.csproj", "linux-x64"), "cli on linux");
                        MuxAssert.AreEqual("Mux.Agent.exe", PublishService.ProjectExecutableName("src/Mux.Agent/Mux.Agent.csproj", "win-x64"), "agent on windows");
                        MuxAssert.AreEqual("Mux.Agent", PublishService.ProjectExecutableName("src/Mux.Agent/Mux.Agent.csproj", "osx-arm64"), "agent on macOS");
                    }),

                    Case("DmgSizesImageFromPublishOutput", "dmg passes hdiutil an explicit -size with headroom when the publish output is on disk", () =>
                    {
                        MuxAssert.AreEqual(64L, DmgDriver.DmgImageSizeMegabytes(0), "empty content still gets the fixed headroom");
                        MuxAssert.AreEqual(64L + 125L, DmgDriver.DmgImageSizeMegabytes(100L * 1024 * 1024), "100 MB of content -> 125 MB + 64 MB");

                        ChannelConfig cfg = Cfg("desktop", "osx-arm64");
                        PublisherContext ctx = Ctx(ArtifactKind.Gui, cfg, "osx-arm64");
                        List<string> unsized = CreateArgs(new DmgDriver().Plan(ctx, cfg));
                        MuxAssert.IsTrue(!unsized.Contains("-size"), "no -size when the publish output is not on disk (dry run)");

                        string dir = Path.Combine(Path.GetTempPath(), "mux-dmgsize-" + Guid.NewGuid().ToString("N"));
                        Directory.CreateDirectory(Path.Combine(dir, "sub"));
                        try
                        {
                            File.WriteAllBytes(Path.Combine(dir, "Mux.Desktop"), new byte[3 * 1024 * 1024]);
                            File.WriteAllBytes(Path.Combine(dir, "sub", "lib.dylib"), new byte[1024 * 1024]);
                            ctx.PublishedFor("osx-arm64")!.PublishDir = dir;
                            List<string> sized = CreateArgs(new DmgDriver().Plan(ctx, cfg));
                            int i = sized.IndexOf("-size");
                            MuxAssert.IsTrue(i >= 0, "passes -size");
                            MuxAssert.AreEqual(DmgDriver.DmgImageSizeMegabytes(4L * 1024 * 1024) + "m", sized[i + 1], "size covers every file, nested ones included");
                            MuxAssert.IsTrue(i < sized.IndexOf("-format"), "-size precedes the format and output");
                        }
                        finally
                        {
                            try { Directory.Delete(dir, true); } catch (Exception) { }
                        }

                        static List<string> CreateArgs(ChannelPlan plan) => plan.Commands.Find(c => c.Executable == "hdiutil" && c.Arguments.Count > 0 && c.Arguments[0] == "create")!.Arguments;
                    }),

                    Case("BundledCliHostIsRenamed", "the published Mux.Cli host is renamed to mux (mux.exe on Windows) so installers find it; other bundles keep their names", () =>
                    {
                        string dir = Path.Combine(Path.GetTempPath(), "mux-bundle-" + Guid.NewGuid().ToString("N"));
                        Directory.CreateDirectory(dir);
                        try
                        {
                            File.WriteAllText(Path.Combine(dir, "Mux.Cli"), "cli");
                            File.WriteAllText(Path.Combine(dir, "Mux.Agent"), "agent");
                            MuxAssert.AreEqual("mux", PublishService.NormalizeBundledExecutable(dir, "src/Mux.Cli/Mux.Cli.csproj", "linux-x64"), "linux name");
                            MuxAssert.IsTrue(File.Exists(Path.Combine(dir, "mux")) && !File.Exists(Path.Combine(dir, "Mux.Cli")), "renamed on Linux");
                            MuxAssert.AreEqual("cli", File.ReadAllText(Path.Combine(dir, "mux")), "same file");
                            MuxAssert.AreEqual("Mux.Agent", PublishService.NormalizeBundledExecutable(dir, "src/Mux.Agent/Mux.Agent.csproj", "linux-x64"), "agent name");
                            MuxAssert.IsTrue(File.Exists(Path.Combine(dir, "Mux.Agent")), "agent left alone");
                            MuxAssert.AreEqual("mux", PublishService.NormalizeBundledExecutable(dir, "src/Mux.Cli/Mux.Cli.csproj", "osx-arm64"), "already renamed is a no-op");
                            MuxAssert.IsTrue(File.Exists(Path.Combine(dir, "mux")), "still there");

                            File.WriteAllText(Path.Combine(dir, "Mux.Cli.exe"), "win");
                            MuxAssert.AreEqual("mux.exe", PublishService.NormalizeBundledExecutable(dir, "src/Mux.Cli/Mux.Cli.csproj", "win-x64"), "windows name");
                            MuxAssert.IsTrue(File.Exists(Path.Combine(dir, "mux.exe")) && !File.Exists(Path.Combine(dir, "Mux.Cli.exe")), "renamed on Windows");

                            string empty = Path.Combine(dir, "empty");
                            Directory.CreateDirectory(empty);
                            MuxAssert.AreEqual("mux", PublishService.NormalizeBundledExecutable(empty, "src/Mux.Cli/Mux.Cli.csproj", "linux-arm64"), "missing host does not throw");
                        }
                        finally
                        {
                            try { Directory.Delete(dir, true); } catch (Exception) { }
                        }
                    }),

                    Case("ArtifactArchiveSeparatesDesktopFromCli", "the CLI archive keeps the plain name and the desktop archive carries its artifact id, so neither overwrites the other", () =>
                    {
                        ArtifactInfo cli = new ArtifactInfo { Id = "cli", Csproj = "src/Mux.Cli/Mux.Cli.csproj", Kind = ArtifactKind.DotnetTool };
                        ArtifactInfo desktop = new ArtifactInfo { Id = "desktop", Csproj = "src/Mux.Desktop/Mux.Desktop.csproj", Kind = ArtifactKind.Gui };
                        MuxAssert.AreEqual("mux-1.2.3-linux-x64.tar.gz", Naming.ArtifactArchive("mux", cli, Version, "linux-x64"), "cli linux");
                        MuxAssert.AreEqual("mux-1.2.3-win-x64.zip", Naming.ArtifactArchive("mux", cli, Version, "win-x64"), "cli windows");
                        MuxAssert.AreEqual("mux-desktop-1.2.3-osx-arm64.tar.gz", Naming.ArtifactArchive("mux", desktop, Version, "osx-arm64"), "desktop macOS");
                    }),

                    Case("ArchiveDriverListsReleaseAssets", "the archive channel runs no commands and names each runtime's release asset", () =>
                    {
                        ChannelConfig cfg = Cfg("cli", "linux-x64", "linux-arm64");
                        PublisherContext ctx = Ctx(ArtifactKind.DotnetTool, cfg, "linux-x64", "linux-arm64");
                        ChannelPlan plan = new ArchiveDriver().Plan(ctx, cfg);
                        MuxAssert.AreEqual(0, plan.Commands.Count, "no commands");
                        string notes = string.Join("\n", plan.Notes);
                        MuxAssert.Contains("releases/download/v1.2.3/mux-1.2.3-linux-x64.tar.gz", notes, "x64 asset");
                        MuxAssert.Contains("mux-1.2.3-linux-arm64.tar.gz", notes, "arm64 asset");
                        MuxAssert.IsTrue(DriverRegistry.IsKnown("archive"), "registered");
                    }),

                    Case("PushStepsDeclareTheirSecrets", "NuGet, Chocolatey, and winget pushes declare the secret they need", () =>
                    {
                        ChannelConfig nugetCfg = Cfg("cli", "linux-x64");
                        ChannelPlan nuget = new NuGetDriver().Plan(Ctx(ArtifactKind.DotnetTool, nugetCfg, "linux-x64"), nugetCfg);
                        MuxAssert.AreEqual("NUGET_API_KEY", nuget.Commands.Find(c => c.Arguments.Contains("push"))!.RequiresSecret, "nuget push");
                        MuxAssert.IsNull(nuget.Commands.Find(c => c.Arguments.Contains("pack"))!.RequiresSecret, "nuget pack needs none");

                        ChannelConfig winCfg = Cfg("desktop", "win-x64");
                        ChannelPlan choco = new ChocolateyDriver().Plan(Ctx(ArtifactKind.Gui, winCfg, "win-x64"), winCfg);
                        MuxAssert.AreEqual("CHOCO_API_KEY", choco.Commands.Find(c => c.Arguments.Contains("push"))!.RequiresSecret, "choco push");
                        ChannelPlan winget = new WingetDriver().Plan(Ctx(ArtifactKind.Gui, winCfg, "win-x64"), winCfg);
                        MuxAssert.AreEqual("WINGET_TOKEN", winget.Commands.Find(c => c.Executable == "wingetcreate")!.RequiresSecret, "winget submit");

                        ChannelConfig linuxCfg = Cfg("desktop", "linux-x64");
                        ChannelPlan apt = new AptRepoDriver().Plan(Ctx(ArtifactKind.Gui, linuxCfg, "linux-x64"), linuxCfg);
                        MuxAssert.AreEqual("GPG_SIGNING_KEYID", apt.Commands.Find(c => c.Description.Contains("GPG-sign"))!.RequiresSecret, "apt signing");
                        ChannelPlan yum = new YumRepoDriver().Plan(Ctx(ArtifactKind.Gui, linuxCfg, "linux-x64"), linuxCfg);
                        MuxAssert.IsTrue(yum.Commands.FindAll(c => c.Description.Contains("GPG-sign")).TrueForAll(c => c.RequiresSecret == "GPG_SIGNING_KEYID"), "yum signing");
                        MuxAssert.IsTrue(yum.Commands.Exists(c => c.Description.Contains("GPG-sign")), "yum has signing steps");
                    }),

                    new TestCaseDescriptor("PublisherDrivers", "UnsetSecretSkipsPush", "a push whose secret is unset is skipped and the channel still succeeds", async (CancellationToken ct) =>
                    {
                        string secret = "MUX_TEST_PUSH_KEY_" + Guid.NewGuid().ToString("N").Substring(0, 8).ToUpperInvariant();
                        RecordingRunner runner = new RecordingRunner();
                        await RunTempChannelAsync(runner, "nuget", "{ \"enabled\": true, \"artifact\": \"cli\", \"os\": \"linux\", \"options\": { \"vaultRef\": \"" + secret + "\" } }", ct).ConfigureAwait(false);
                        MuxAssert.AreEqual(1, runner.Calls.Count, "only pack ran: " + string.Join(" | ", runner.Calls));
                        MuxAssert.Contains("pack", runner.Calls[0], "pack");

                        Environment.SetEnvironmentVariable(secret, "key");
                        try
                        {
                            RecordingRunner withKey = new RecordingRunner();
                            await RunTempChannelAsync(withKey, "nuget", "{ \"enabled\": true, \"artifact\": \"cli\", \"os\": \"linux\", \"options\": { \"vaultRef\": \"" + secret + "\" } }", ct).ConfigureAwait(false);
                            MuxAssert.AreEqual(2, withKey.Calls.Count, "pack and push ran");
                            MuxAssert.Contains("push", withKey.Calls[1], "push");
                        }
                        finally
                        {
                            Environment.SetEnvironmentVariable(secret, null);
                        }
                    }),

                    new TestCaseDescriptor("PublisherDrivers", "MissingOptionalToolContinues", "a continue-on-error step whose executable is not installed is noted and the channel still succeeds", async (CancellationToken ct) =>
                    {
                        string secret = "MUX_TEST_WINGET_" + Guid.NewGuid().ToString("N").Substring(0, 8).ToUpperInvariant();
                        Environment.SetEnvironmentVariable(secret, "token");
                        try
                        {
                            RecordingRunner runner = new RecordingRunner { MissingExecutable = "wingetcreate" };
                            string log = await RunTempChannelAsync(runner, "winget", "{ \"enabled\": true, \"artifact\": \"desktop\", \"runtimes\": [ \"win-x64\" ], \"options\": { \"vaultRef\": \"" + secret + "\" } }", ct).ConfigureAwait(false);
                            MuxAssert.Contains("could not start wingetcreate", log, "noted");
                        }
                        finally
                        {
                            Environment.SetEnvironmentVariable(secret, null);
                        }
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

        private static async Task<string> RunTempChannelAsync(RecordingRunner runner, string channel, string channelJson, CancellationToken ct)
        {
            string repo = Path.Combine(Path.GetTempPath(), "mux-pubrun-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(repo);
            try
            {
                string manifest = "{ \"schemaVersion\": 1, \"project\": { \"name\": \"mux\", \"repo\": \"jchristn/mux\", \"homepage\": \"https://example.com\", \"description\": \"d\", \"license\": \"MIT\" },"
                    + " \"build\": { \"artifacts\": [ { \"id\": \"cli\", \"csproj\": \"src/Mux.Cli/Mux.Cli.csproj\", \"kind\": \"DotnetTool\" }, { \"id\": \"desktop\", \"csproj\": \"src/Mux.Desktop/Mux.Desktop.csproj\", \"kind\": \"Gui\" } ],"
                    + " \"frameworks\": [ \"net10.0\" ], \"runtimes\": [ \"linux-x64\", \"win-x64\" ] },"
                    + " \"channels\": { \"" + channel + "\": " + channelJson + " } }";
                File.WriteAllText(Path.Combine(repo, "publisher.json"), manifest);

                StringWriter log = new StringWriter();
                Mux.Publisher.Orchestrator orchestrator = new Mux.Publisher.Orchestrator(runner, log);
                await orchestrator.RunChannelAsync(new Mux.Publisher.RunOptions
                {
                    Channel = channel,
                    Version = Version,
                    RepoRoot = repo,
                    OutputRoot = Path.Combine(repo, "out"),
                    StagingRoot = Path.Combine(repo, "staging"),
                    Force = true
                }, ct).ConfigureAwait(false);
                return log.ToString();
            }
            finally
            {
                try { Directory.Delete(repo, true); } catch (Exception) { }
            }
        }

        private sealed class RecordingRunner : IProcessRunner
        {
            public List<string> Calls { get; } = new List<string>();

            public string? MissingExecutable { get; set; }

            public Task<ProcessResult> RunAsync(string executable, IReadOnlyList<string> arguments, string? workingDirectory, CancellationToken ct)
            {
                if (string.Equals(executable, MissingExecutable, StringComparison.Ordinal))
                {
                    throw new System.ComponentModel.Win32Exception(2, "The system cannot find the file specified.");
                }

                Calls.Add(executable + " " + string.Join(" ", arguments));
                return Task.FromResult(new ProcessResult { ExitCode = 0 });
            }
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
            return Ctx(kind, cfg, true, rids);
        }

        private static PublisherContext Ctx(ArtifactKind kind, ChannelConfig cfg, bool signed, params string[] rids)
        {
            PublisherManifest manifest = new PublisherManifest();
            manifest.Project = new ProjectInfo
            {
                Name = "mux",
                DisplayName = "mux",
                Repo = "jchristn/mux",
                Homepage = "https://github.com/jchristn/mux",
                Description = "Multiplexed AI agent.",
                Tagline = "Your AI agent. Your models. Your infra. Anywhere.",
                Publisher = "Lattice AI, LLC",
                PublisherUrl = "https://latticetech.ai",
                License = "MIT"
            };
            manifest.Build.Frameworks.Add("net10.0");
            manifest.Build.Runtimes.AddRange(rids);
            ArtifactInfo artifact = new ArtifactInfo { Id = cfg.Artifact, Csproj = "src/Mux." + (kind == ArtifactKind.Gui ? "Desktop/Mux.Desktop" : "Cli/Mux.Cli") + ".csproj", Kind = kind };
            manifest.Build.Artifacts.Add(artifact);
            manifest.Signing = !signed ? new SigningInfo { Linux = new SigningEntry { VaultRef = "GPG_SIGNING" } } : new SigningInfo
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
