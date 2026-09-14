# Building Installers

mux installers are built by `Mux.Publisher` (reads `publisher.json`) and wrapped by the
`build-installers.{sh,bat}` scripts. **Native installer formats are OS-locked** — a `.dmg` builds only on
macOS, `.deb`/`.rpm`/AppImage only on Linux, the `.exe` installer only on Windows — so one machine builds
its own OS's installers. To build all three at once, push a `v<version>` tag and let CI do it.

## Where output goes

- **Installers/binaries →** `installers/<version>/<os>/`
  (e.g. `installers/0.10.0/windows/mux-0.10.0-win-x64-setup.exe`, `.../macos/*.dmg`, `.../linux/*.deb *.rpm *.AppImage`, `.../nuget/*.nupkg`).
- **apt/yum repos →** `installers/<version>/apt/` and `installers/<version>/yum/` (publish these to your repo host).
- **Package-manager manifests** (scoop `.json`, winget `.yaml`, Chocolatey `.nuspec`, Homebrew `.rb`) **→** `dist/staging/` — commit/push these to your tap/bucket repos.
- Intermediate publish output stays in `dist/staging/`. `installers/` and `dist/` are git-ignored.

Version defaults to `<Version>` in `src/Mux.Cli/Mux.Cli.csproj`; pass one explicitly to override.

## Prerequisites

- **All platforms:** .NET SDK 8 and 10.
- **Windows:** [Inno Setup](https://jrsoftware.org/isdl.php) (`iscc` on PATH); WiX (`wix`) only if you enable the `.msi` channel.
- **macOS:** Xcode command-line tools (`xcode-select --install`) — provides `hdiutil`, `sips`, `iconutil`.
- **Linux:** `fpm`, `rpm`, `appimagetool`, `createrepo_c`, `dpkg-dev`, `apt-utils` (`libfuse2` for AppImage).

## Windows

```bat
build-installers.bat                REM builds inno, scoop, chocolatey, winget
build-installers.bat 0.11.0 --dry-run
```
Produces the signed-less `mux-<version>-win-x64-setup.exe` (SmartScreen shows "More info → Run anyway"),
plus the scoop/winget/choco manifests.

## macOS

```bash
./build-installers.sh               # builds dmg, homebrew (formula), homebrew-cask
./build-installers.sh 0.11.0 --dry-run
```
Produces an **unsigned** `.dmg`. First launch: right-click the app → **Open** (or
`xattr -dr com.apple.quarantine /Applications/mux.app`) — no Apple Developer certificate is configured.

## Linux

```bash
./build-installers.sh               # builds appimage, debrpm, nuget, apt, yum
./build-installers.sh 0.11.0 --dry-run
```
Produces `.deb`, `.rpm`, `.AppImage`, the NuGet tool package, and GPG-signed apt/yum repo trees (needs the
free `GPG_SIGNING_KEYID`; metadata is unsigned without it).

## All three at once (CI)

```bash
git tag v0.10.0 && git push origin v0.10.0
```
The `.github/workflows/release.yml` matrix builds windows/macOS/Linux and uploads every installer to the
GitHub Release. No local packagers needed; installers build without secrets (unsigned). Secrets only gate
the *publish* steps (NuGet push, winget PR, Chocolatey push, GPG repo signing) — see the workflow's `env`.

## Manual (single channel)

```bash
dotnet build src/Mux.Publisher/Mux.Publisher.csproj -c Release -f net10.0
PUB=src/Mux.Publisher/bin/Release/net10.0/Mux.Publisher.dll
dotnet $PUB --list                    # all channels
dotnet $PUB --channels-for macos      # channels for one OS, in run order
dotnet $PUB --channel dmg --version 0.10.0 [--dry-run]   # --out defaults to dist/release
```
Run channels in the order `--channels-for` lists them (installers before the package-manager entries that
reference them, e.g. `inno` before `winget`/`chocolatey`).
