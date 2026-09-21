namespace Mux.Publisher.Channels.Drivers
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using Mux.Publisher.Manifest;

    /// <summary>
    /// Builds a signed Windows <c>.exe</c> installer with Inno Setup — the least-ceremony installer for
    /// a GUI/desktop app. The installer lays down the self-contained publish, adds a Start-menu shortcut,
    /// and optionally launches the app after install; the app self-registers login-startup on first run,
    /// so the installer itself does not write a Run key. The same signed <c>.exe</c> feeds winget and
    /// Chocolatey.
    /// </summary>
    public sealed class InnoSetupDriver : DriverBase
    {
        /// <inheritdoc />
        public override string Name => "inno";

        /// <inheritdoc />
        public override TargetOs RequiredOs => TargetOs.Windows;

        /// <inheritdoc />
        public override ChannelPlan Plan(PublisherContext context, ChannelConfig config)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (config == null) throw new ArgumentNullException(nameof(config));

            ChannelPlan plan = new ChannelPlan(Name);
            string project = Project(context);
            string display = Display(context);

            List<string> runtimes = ChannelHelpers.ResolveRuntimes(context.Manifest, config, RequiredOs);
            foreach (string rid in runtimes)
            {
                PublishedArtifact? published = context.PublishedFor(rid);
                if (published == null)
                {
                    plan.AddNote("inno: no publish for " + rid + "; skipped.");
                    continue;
                }

                string installer = Naming.InnoInstaller(project, context.Version, rid);
                string appExe = System.IO.Path.GetFileName(published.PrimaryBinary);
                string issName = "inno/" + project + "-" + rid + ".iss";

                // Stage the license so the installer shows a license page the user must accept before Next.
                string licenseName = "inno/" + project + "-" + rid + "-LICENSE.txt";
                plan.AddFile(licenseName, Mux.Publisher.Publishing.LicenseAssets.ReadLicenseText(context.RepoRoot));

                string script = RenderIss(new InnoInputs
                {
                    AppName = display,
                    Publisher = "Joel Christner",
                    Version = context.Version,
                    Url = context.Manifest.Project.Homepage,
                    AppExe = appExe,
                    PublishDir = published.PublishDir,
                    OutputBaseName = System.IO.Path.GetFileNameWithoutExtension(installer),
                    OutputDir = System.IO.Path.Combine(context.OutputRoot, "windows"),
                    IconFile = System.IO.Path.Combine(context.RepoRoot, "assets", "icon-green.ico"),
                    LicenseFile = System.IO.Path.Combine(context.StagingRoot, licenseName),
                    // When the desktop payload bundles the CLI, put the install dir on PATH so `mux` works.
                    AddToPath = published.CliBinary != null
                });
                plan.AddFile(issName, script);

                plan.AddCommand(new ShellCommand("iscc", new List<string>
                {
                    System.IO.Path.Combine(context.StagingRoot, issName)
                })
                {
                    Description = "Compile the Inno installer for " + rid
                });

                AddSigning(plan, context, config, System.IO.Path.Combine(context.OutputRoot, "windows", installer));
            }

            return plan;
        }

        /// <summary>
        /// Adds the <c>signtool</c> Authenticode signing step referencing the Windows signing secret.
        /// </summary>
        /// <param name="plan">The plan to append to.</param>
        /// <param name="context">The publisher context.</param>
        /// <param name="config">The channel config (may override the timestamp URL).</param>
        /// <param name="targetFile">The file to sign.</param>
        internal static void AddSigning(ChannelPlan plan, PublisherContext context, ChannelConfig config, string targetFile)
        {
            SigningEntry? windows = context.Manifest.Signing.Windows;
            if (windows == null || string.IsNullOrWhiteSpace(windows.VaultRef))
            {
                plan.AddNote("windows signing: no signing identity configured; installer will be UNSIGNED (SmartScreen will warn).");
                return;
            }

            string timestamp = config.OptionOrDefault("timestampUrl", "http://timestamp.digicert.com");
            plan.AddCommand(new ShellCommand("signtool", new List<string>
            {
                "sign",
                "/f", "$" + windows.VaultRef + "_PFX",
                "/p", "$" + windows.VaultRef + "_PASSWORD",
                "/tr", timestamp,
                "/td", "sha256",
                "/fd", "sha256",
                targetFile
            })
            {
                Description = "Authenticode-sign " + System.IO.Path.GetFileName(targetFile)
            });
        }

        /// <summary>Inputs for rendering an Inno Setup script.</summary>
        public sealed class InnoInputs
        {
            /// <summary>The application display name.</summary>
            public string AppName { get; set; } = string.Empty;

            /// <summary>The publisher name.</summary>
            public string Publisher { get; set; } = string.Empty;

            /// <summary>The release version.</summary>
            public string Version { get; set; } = string.Empty;

            /// <summary>The application URL.</summary>
            public string Url { get; set; } = string.Empty;

            /// <summary>The main executable file name (no path).</summary>
            public string AppExe { get; set; } = string.Empty;

            /// <summary>The self-contained publish directory to install.</summary>
            public string PublishDir { get; set; } = string.Empty;

            /// <summary>The installer output base name (no extension).</summary>
            public string OutputBaseName { get; set; } = string.Empty;

            /// <summary>The installer output directory.</summary>
            public string OutputDir { get; set; } = string.Empty;

            /// <summary>The setup icon file.</summary>
            public string IconFile { get; set; } = string.Empty;

            /// <summary>The license file shown on the wizard's license page (acceptance is required to proceed).</summary>
            public string LicenseFile { get; set; } = string.Empty;

            /// <summary>Whether to add the install directory to the system PATH (so the bundled CLI is on PATH).</summary>
            public bool AddToPath { get; set; }
        }

        /// <summary>
        /// Renders an Inno Setup <c>.iss</c> script from inputs. Deterministic AppId is derived from the
        /// app name so upgrades replace prior installs.
        /// </summary>
        /// <param name="inputs">The render inputs.</param>
        /// <returns>The rendered <c>.iss</c> content.</returns>
        public static string RenderIss(InnoInputs inputs)
        {
            if (inputs == null) throw new ArgumentNullException(nameof(inputs));

            string appId = StableGuid.FromString("inno:" + inputs.AppName);
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("; Generated by Mux.Publisher — do not edit by hand.");
            builder.AppendLine("[Setup]");
            builder.AppendLine("AppId={{" + appId + "}");
            builder.AppendLine("AppName=" + inputs.AppName);
            builder.AppendLine("AppVersion=" + inputs.Version);
            builder.AppendLine("AppPublisher=" + inputs.Publisher);
            builder.AppendLine("AppPublisherURL=" + inputs.Url);
            builder.AppendLine("DefaultDirName={autopf}\\" + inputs.AppName);
            builder.AppendLine("DefaultGroupName=" + inputs.AppName);
            builder.AppendLine("UninstallDisplayIcon={app}\\" + inputs.AppExe);
            builder.AppendLine("Compression=lzma2");
            builder.AppendLine("SolidCompression=yes");
            builder.AppendLine("ArchitecturesInstallIn64BitMode=x64compatible");
            builder.AppendLine("WizardStyle=modern");
            if (inputs.AddToPath)
            {
                builder.AppendLine("ChangesEnvironment=yes");
            }
            builder.AppendLine("OutputBaseFilename=" + inputs.OutputBaseName);
            builder.AppendLine("OutputDir=" + inputs.OutputDir);
            if (!string.IsNullOrWhiteSpace(inputs.IconFile))
            {
                builder.AppendLine("SetupIconFile=" + inputs.IconFile);
            }

            if (!string.IsNullOrWhiteSpace(inputs.LicenseFile))
            {
                // Inno shows the license page and disables Next until the user selects "I accept the agreement".
                builder.AppendLine("LicenseFile=" + inputs.LicenseFile);
            }

            builder.AppendLine();
            builder.AppendLine("[Files]");
            builder.AppendLine("Source: \"" + inputs.PublishDir + "\\*\"; DestDir: \"{app}\"; Flags: recursesubdirs createallsubdirs ignoreversion");
            builder.AppendLine();
            builder.AppendLine("[Icons]");
            builder.AppendLine("Name: \"{group}\\" + inputs.AppName + "\"; Filename: \"{app}\\" + inputs.AppExe + "\"");
            builder.AppendLine("Name: \"{userdesktop}\\" + inputs.AppName + "\"; Filename: \"{app}\\" + inputs.AppExe + "\"; Tasks: desktopicon");
            builder.AppendLine();
            builder.AppendLine("[Tasks]");
            builder.AppendLine("Name: \"desktopicon\"; Description: \"Create a desktop shortcut\"; GroupDescription: \"Additional icons:\"; Flags: unchecked");
            builder.AppendLine();
            builder.AppendLine("[Run]");
            builder.AppendLine("; Launch after install; the app registers login-startup for the tray agent on first run.");
            builder.AppendLine("Filename: \"{app}\\" + inputs.AppExe + "\"; Description: \"Launch " + inputs.AppName + "\"; Flags: nowait postinstall skipifsilent");

            if (inputs.AddToPath)
            {
                // Add the install dir (which holds the bundled CLI) to the system PATH, de-duplicated.
                builder.AppendLine();
                builder.AppendLine("[Registry]");
                builder.AppendLine("Root: HKLM; Subkey: \"System\\CurrentControlSet\\Control\\Session Manager\\Environment\"; ValueType: expandsz; ValueName: \"Path\"; ValueData: \"{olddata};{app}\"; Check: NeedsAddPath('{app}')");
                builder.AppendLine();
                builder.AppendLine("[Code]");
                builder.AppendLine("function NeedsAddPath(Param: string): Boolean;");
                builder.AppendLine("var");
                builder.AppendLine("  OrigPath: string;");
                builder.AppendLine("begin");
                builder.AppendLine("  if not RegQueryStringValue(HKEY_LOCAL_MACHINE, 'System\\CurrentControlSet\\Control\\Session Manager\\Environment', 'Path', OrigPath) then");
                builder.AppendLine("  begin");
                builder.AppendLine("    Result := True;");
                builder.AppendLine("    exit;");
                builder.AppendLine("  end;");
                builder.AppendLine("  Result := Pos(';' + Uppercase(Param) + ';', ';' + Uppercase(OrigPath) + ';') = 0;");
                builder.AppendLine("end;");
            }

            return builder.ToString();
        }
    }
}
