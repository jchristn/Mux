# Building installers

mux installers are built by `Mux.Publisher`, which reads `publisher.json` and runs one channel at a time, and wrapped
by `build-installers.sh` (macOS and Linux) and `build-installers.bat` (Windows) at the repository root. The standard
this follows is `~/Code/Agents/requirements/INSTALLERS.md`; what users see is in [INSTALL.md](INSTALL.md).

Native installer formats are OS-locked: the `.pkg` and `.dmg` build only on macOS, the `.deb`, `.rpm`, and AppImage only
on Linux, and the Inno `.exe` only on Windows. One machine builds its own OS's installers; a release builds all three
on GitHub's runners (see "Releasing").

## What the installers contain

The `desktop` artifact bundles three executables into one payload (`publisher.json`, `build.artifacts`,
`desktop.bundle`): the desktop app, the tray agent (`Mux.Agent`), and the command (`mux`). Every desktop installer lays
them down side by side, which is how the app finds the agent to start it.

| Format | Built by | Installs | `mux` on PATH | Tray agent at login | License |
| --- | --- | --- | --- | --- | --- |
| `.pkg` (recommended on macOS) | `pkg` | `/Applications/mux.app` | `/usr/local/bin/mux` (postinstall) | LaunchAgent `ai.latticetech.mux.agent` (postinstall) | Installer.app license page; must agree |
| `.dmg` | `dmg` | drag-to-Applications copy, with an Applications shortcut and `If mux won't open.txt` | link it yourself | the agent registers on first run | inside the bundle |
| Inno `.exe` | `inno` | `C:\Program Files\mux` | system PATH (de-duplicated) | started unelevated after install; registers the `Mux Agent` Run value | wizard license page; must accept |
| `.deb` / `.rpm` | `debrpm` | `/opt/mux` | `/usr/bin/mux` | `/etc/xdg/autostart/ai.latticetech.mux.agent.desktop` | `/usr/share/doc/mux/copyright`, `/usr/share/licenses/mux/LICENSE` |
| AppImage | `appimage` | runs in place | no | the agent registers on first run | in the image |

Every installer with a UI shows the logo, the product name, and the tagline ("Your AI agent. Your models. Your infra.
Anywhere."). The identifiers are `ai.latticetech.mux` and `ai.latticetech.mux.agent`, and the publisher is Lattice AI,
LLC. An upgrade from 1.x moves the old login registration (`com.jchristn.mux.agent`, `MuxAgent`, `mux-agent`) to the
new name (the `.pkg` postinstall and `StartupRegistrar.MigrateLegacy`), so only one agent starts.

macOS apps are ad-hoc signed (`codesign --sign -`) after the bundle is complete and then verified, because Apple
silicon refuses unsigned code and a bundle whose signature doesn't cover its resources opens as "damaged". Nothing is
notarized and no installer carries a paid signature, by policy; INSTALL.md has the first-launch steps.

The `.deb` and `.rpm` declare their runtime dependencies (ICU, OpenSSL, zlib, libstdc++, the X11 client libraries,
fontconfig, CA certificates), so a missing library fails the install instead of the first launch.

## Artwork

`tools/installer-assets/generate.py` (needs Pillow) generates every image from the 19 x 5 pixel wordmark:
`assets/logo.svg`, `assets/icon.svg`, `assets/icon-1024.png`, the Inno wizard bitmaps (`assets/installer/wizard-*.bmp`
at 100%, 150%, and 200%), the `.pkg` backgrounds (light and dark), and the Linux hicolor icons. The outputs are
committed, so builds need no image tools.

## Where output goes

- Installers and archives: `installers/<version>/<os>/` locally (for example
  `installers/2.0.0/macos/mux-2.0.0-osx-arm64.pkg`), `dist/release/<os>/` in CI.
- apt/yum repository trees: `installers/<version>/apt/` and `.../yum/`.
- Package-manager manifests (Scoop, winget, Chocolatey, Homebrew): `dist/staging/`.
- Intermediate publish output: `dist/staging/`. `installers/` and `dist/` are git-ignored.

The version defaults to `<Version>` in `src/Mux.Cli/Mux.Cli.csproj`; pass one to override.

## Prerequisites

- **All platforms:** .NET SDK 8 and 10.
- **Windows:** [Inno Setup](https://jrsoftware.org/isdl.php) 6 (`iscc` on PATH; `choco install innosetup`). WiX only if
  you enable the `.msi` channel.
- **macOS:** Xcode command-line tools (`xcode-select --install`), which provide `codesign`, `pkgbuild`,
  `productbuild`, `hdiutil`, and `plutil`.
- **Linux:** `fpm` (`sudo gem install --no-document fpm`), `rpm`, `appimagetool`, `createrepo_c`, `dpkg-dev`,
  `apt-utils`.

## Building locally

```bash
./build-installers.sh                 # macOS: pkg, dmg, archives, Homebrew manifests; Linux: deb, rpm, AppImage, archives, nupkg
./build-installers.sh 2.0.0 --dry-run # print each channel's plan without building
```

```bat
build-installers.bat                  REM Windows: Inno .exe, archives, Scoop/winget/Chocolatey manifests
```

A locally built file never shows the Gatekeeper or SmartScreen prompt, because only browser downloads are quarantined.
To see the prompts, download a dry-run or release asset through a browser on a machine that has never run mux.

## Releasing

Push a tag that is `v` plus the project version:

```bash
git tag -a v2.0.0 -m "Mux 2.0.0" && git push origin v2.0.0
```

`.github/workflows/release.yml` then:

1. checks that the tag matches `<Version>` and that `CHANGELOG.md` has a non-empty `## [2.0.0]` section;
2. builds each OS's installers on its own runner (no secrets; package-manager pushes skip themselves);
3. installs them on each OS (`setup.exe /VERYSILENT`, `sudo installer -pkg`, `apt install ./*.deb`, `dnf install` in a
   Fedora container, the AppImage extracted) and runs `mux --version`;
4. only then creates the GitHub Release "Mux 2.0.0" with every asset, one `SHA256SUMS`, and notes built by
   `.github/scripts/release-notes.sh` from `.github/release-install-notes.md`, `.github/release-highlights/<version>.md`
   (optional), and the changelog section.

A failure on any OS publishes nothing. Running the workflow by hand (`gh workflow run release.yml --ref main`) is a
dry run that stops before step 4; download its artifacts with `gh run download <run-id>`. `release-check.yml` runs the
same dry run on pushes to `main` that touch packaging.

Package-manager channels (NuGet, winget, Chocolatey, Scoop, Homebrew, apt/yum) run after the release in a separate job,
only when the repository variable `ENABLE_PACKAGE_MANAGERS` is `true`, and each push needs its secret
(`NUGET_API_KEY`, `WINGET_TOKEN`, `CHOCO_API_KEY`, `GPG_PRIVATE_KEY`/`GPG_KEYID`). They are off today.

### Release assets

| Asset | Built by | Contents |
| --- | --- | --- |
| `mux-<v>-osx-<arch>.pkg`, `.dmg` | `pkg`, `dmg` | The desktop app, agent, and command |
| `mux-<v>-win-<arch>-setup.exe` | `inno` | Same, for Windows |
| `mux_<v>_<arch>.deb`, `mux-<v>-1.<arch>.rpm`, `mux-<v>-<arch>.AppImage` | `debrpm`, `appimage` | Same, for Linux |
| `mux-<v>-<rid>.tar.gz` / `.zip` | `cli-archive`, `cli-archive-windows`, `homebrew`, `scoop` | The self-contained command, a single `mux` binary |
| `mux-desktop-<v>-<rid>.tar.gz` / `.zip` | the desktop channels | The portable desktop payload |
| `Mux.Cli.<v>.nupkg` | `nuget` | The .NET tool (needs the .NET runtime) |
| `SHA256SUMS` | the release job | One checksum line per asset |

## A single channel

```bash
dotnet build src/Mux.Publisher/Mux.Publisher.csproj -c Release -f net10.0
PUB=src/Mux.Publisher/bin/Release/net10.0/Mux.Publisher.dll
dotnet $PUB --list                          # every channel
dotnet $PUB --channels-for macos            # enabled channels for one OS, in run order
dotnet $PUB --package-managers-for linux    # only the package-manager channels
dotnet $PUB --channel pkg --version 2.0.0 [--dry-run]   # --out defaults to dist/release
```

Run channels in the order `--channels-for` lists them: installers before the package-manager entries that reference
them (for example `inno` before `winget` and `chocolatey`).
